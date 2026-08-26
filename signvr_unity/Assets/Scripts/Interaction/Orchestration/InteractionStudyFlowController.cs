using System;
using System.Collections;
using System.IO;
using System.Linq;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;
using UnityEngine;
using UnityEngine.Networking;

namespace SignVR.Interaction.Orchestration
{
    [DefaultExecutionOrder(-600)]
    [DisallowMultipleComponent]
    public sealed class InteractionStudyFlowController : MonoBehaviour
    {
        private const double ReadinessRefreshIntervalSeconds = 2d;

        [SerializeField]
        private InteractionRunController runController;

        [SerializeField]
        private InstructionPresentationController presentationController;

        [SerializeField]
        private InteractionPhaseCoordinator phaseCoordinator;

        [SerializeField]
        private InteractionStudyCaptureBinding captureBinding;

        [SerializeField]
        private string contentManifestRelativePath =
            "InstructionContent/instruction-content-manifest.json";

        private InteractionStudyFlow flow;
        private Coroutine manifestRoutine;
        private readonly InteractionStudyOperationGate manifestLoadGate =
            new();
        private readonly InteractionStudyReadinessPollGate readinessPollGate =
            new(ReadinessRefreshIntervalSeconds);
        private long manifestLoadToken;
        private bool manifestReady;
        private bool identityArmed;
        private bool applicationPaused;
        private bool reconfiguring;
        private string armedParticipantId = string.Empty;
        private string armedBuildIdentity = string.Empty;
        private string initializationStatus =
            "Instruction manifest has not loaded.";
        private string identityStatus =
            "正在等待主机自动分配匿名实验编号。";
        private string lastPublishedFingerprint = string.Empty;

        public event Action StateChanged;

        public InteractionRunController RunController => runController;
        public InstructionPresentationController PresentationController =>
            presentationController;
        public InteractionPhaseCoordinator PhaseCoordinator => phaseCoordinator;
        public InteractionStudyCaptureBinding CaptureBinding => captureBinding;
        public string ContentManifestRelativePath =>
            contentManifestRelativePath;
        public bool ManifestReady => manifestReady;
        public bool ManifestLoadInFlight => manifestLoadGate.InFlight;
        public bool ReadinessRefreshInFlight => readinessPollGate.InFlight;
        public bool IdentityArmed => identityArmed;
        public bool CanConfigureIdentity => runController != null &&
            runController.State == RunState.PreStart;
        public string ConfiguredParticipantId => runController?.ParticipantId ??
            string.Empty;
        public string ConfiguredBuildIdentity => runController?.GitCommit ??
            string.Empty;
        public string InitializationStatus => initializationStatus;
        public string IdentityStatus => identityStatus;
        public InteractionStudyFlow Flow => flow;
        public InteractionStudyFlowSnapshot Snapshot => flow?.Snapshot;

        public void Configure(
            InteractionRunController run,
            InstructionPresentationController presentation,
            InteractionPhaseCoordinator phases,
            InteractionStudyCaptureBinding capture)
        {
            InteractionRunController validatedRun = run ??
                throw new ArgumentNullException(nameof(run));
            InstructionPresentationController validatedPresentation =
                presentation ??
                throw new ArgumentNullException(nameof(presentation));
            InteractionPhaseCoordinator validatedPhases = phases ??
                throw new ArgumentNullException(nameof(phases));
            InteractionStudyCaptureBinding validatedCapture = capture ??
                throw new ArgumentNullException(nameof(capture));

            // Build and validate every replacement before touching the
            // serialized dependency set. Reconfigure itself rolls back its
            // subscriptions if a replacement publisher cannot attach.
            var newRunPort = new UnityInteractionStudyRunPort(
                validatedRun,
                validatedCapture
            );
            var newPresentationPort =
                new UnityInteractionStudyPresentationPort(
                    validatedPresentation
                );
            var newTaskPort = new UnityInteractionStudyTaskPort(
                validatedPhases
            );

            if (flow != null)
            {
                reconfiguring = true;
                try
                {
                    flow.Reconfigure(
                        newRunPort,
                        newPresentationPort,
                        newTaskPort
                    );
                }
                finally
                {
                    reconfiguring = false;
                }
            }

            InteractionRunController previousRun = runController;
            StopManifestLoad("Study dependencies were reconfigured.");
            readinessPollGate.Invalidate();
            previousRun?.InvalidateHostReadiness();
            runController = validatedRun;
            presentationController = validatedPresentation;
            phaseCoordinator = validatedPhases;
            captureBinding = validatedCapture;
            runController.InvalidateHostReadiness();
            manifestReady = false;
            initializationStatus =
                "Instruction manifest must be loaded for this configuration.";
            DisarmIdentityInternal(
                "Study dependencies changed; apply participant and build " +
                "identity again.",
                invalidateReadiness: false
            );
            if (Application.isPlaying && isActiveAndEnabled)
            {
                RetryManifestLoad();
            }
            PublishStateIfChanged(force: true);
        }

        public InteractionStudyFlowCommandResult TryStart()
        {
            if (flow == null)
            {
                return InteractionStudyFlowCommandResult.Failure(
                    "Study Flow is not initialized."
                );
            }
            if (!manifestReady)
            {
                return InteractionStudyFlowCommandResult.Failure(
                    initializationStatus
                );
            }
            if (!identityArmed)
            {
                return InteractionStudyFlowCommandResult.Failure(
                    identityStatus
                );
            }
            InteractionStudyFlowCommandResult result = flow.TryStart();
            if (result.Succeeded || runController.State != RunState.PreStart)
            {
                // Identity is an operator confirmation for exactly one Run.
                // W6 retains manifest identity, but another Start requires a
                // new explicit confirmation after terminal cleanup.
                identityArmed = false;
                armedParticipantId = string.Empty;
                armedBuildIdentity = string.Empty;
                identityStatus =
                    "Run consumed; reconfirm identity for the next Run.";
                readinessPollGate.Invalidate();
                PublishStateIfChanged(force: true);
            }
            return result;
        }

        public InteractionStudyFlowCommandResult TryConfigureIdentity(
            string participantId,
            string integratedBuildIdentity)
        {
            if (runController == null)
            {
                return InteractionStudyFlowCommandResult.Failure(
                    "W6 Run controller is missing."
                );
            }
            if (runController.State != RunState.PreStart)
            {
                return InteractionStudyFlowCommandResult.Failure(
                    "Study identity can change only in PreStart."
                );
            }

            try
            {
                InteractionStudyIdentityPolicy.Validate(
                    participantId,
                    integratedBuildIdentity,
                    out string safeParticipant,
                    out string safeBuild
                );
                runController.ConfigureIdentity(
                    runController.BatchId,
                    safeParticipant,
                    safeBuild
                );
                identityArmed = true;
                armedParticipantId = safeParticipant;
                armedBuildIdentity = safeBuild;
                identityStatus = "Identity armed for participant " +
                    safeParticipant + " with build " + safeBuild + ".";
                readinessPollGate.Invalidate();
                // Automatic identity adoption runs from inside a completed
                // readiness callback. Let Update start the replacement poll
                // on the next frame, after the Host client has released the
                // completed request's readiness slot.
                PublishStateIfChanged(force: true);
                return InteractionStudyFlowCommandResult.Success();
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                identityArmed = false;
                armedParticipantId = string.Empty;
                armedBuildIdentity = string.Empty;
                identityStatus = "Identity rejected: " + exception.Message;
                readinessPollGate.Invalidate();
                runController.InvalidateHostReadiness();
                PublishStateIfChanged(force: true);
                return InteractionStudyFlowCommandResult.Failure(
                    identityStatus
                );
            }
        }

        public bool DisarmIdentityIfDraftChanged(
            string participantId,
            string integratedBuildIdentity)
        {
            if (!identityArmed)
            {
                return false;
            }
            string participant = participantId?.Trim() ?? string.Empty;
            string build = integratedBuildIdentity?.Trim() ?? string.Empty;
            if (string.Equals(
                    participant,
                    armedParticipantId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    build,
                    armedBuildIdentity,
                    StringComparison.Ordinal))
            {
                return false;
            }
            DisarmIdentityInternal(
                "Identity input changed. The armed identity remains " +
                ConfiguredParticipantId + "/" + ConfiguredBuildIdentity +
                "; apply an explicit PreStart identity before Start.",
                invalidateReadiness: true
            );
            PublishStateIfChanged(force: true);
            return true;
        }

        public bool TryUnlockIdentityForEditing()
        {
            if (!identityArmed || runController == null ||
                runController.State != RunState.PreStart)
            {
                return false;
            }
            DisarmIdentityInternal(
                "Identity editing unlocked. Review both fields and apply " +
                    "them again before Start.",
                invalidateReadiness: true
            );
            PublishStateIfChanged(force: true);
            return true;
        }

        public InteractionStudyFlowCommandResult TryReplay()
        {
            return flow == null
                ? InteractionStudyFlowCommandResult.Failure(
                    "Study Flow is not initialized."
                )
                : flow.TryReplay();
        }

        public InteractionStudyFlowCommandResult TryGiveUp()
        {
            return flow == null
                ? InteractionStudyFlowCommandResult.Failure(
                    "Study Flow is not initialized."
                )
                : flow.TryGiveUp();
        }

        public InteractionStudyFlowCommandResult TryAbort(
            string reason = "participant_requested_abort")
        {
            return flow == null
                ? InteractionStudyFlowCommandResult.Failure(
                    "Study Flow is not initialized."
                )
                : flow.TryAbort(reason);
        }

        public void RetryManifestLoad()
        {
            if (!Application.isPlaying || manifestReady ||
                manifestLoadGate.InFlight ||
                runController == null ||
                runController.State != RunState.PreStart)
            {
                return;
            }
            manifestReady = false;
            if (!manifestLoadGate.TryBegin(out manifestLoadToken))
            {
                return;
            }
            try
            {
                manifestRoutine = StartCoroutine(
                    LoadManifestRoutine(manifestLoadToken)
                );
            }
            catch (Exception exception)
            {
                manifestLoadGate.TryComplete(manifestLoadToken);
                manifestRoutine = null;
                initializationStatus =
                    "Instruction manifest load could not start: " +
                    exception.Message;
                PublishStateIfChanged(force: true);
            }
        }

        private void Awake()
        {
            if (Application.isPlaying)
            {
                InitializeFlow();
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            InitializeFlow();
            if (applicationPaused)
            {
                return;
            }
            SafeResumeFlow();
            RetryManifestLoad();
        }

        private void Start()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            RetryManifestLoad();
        }

        private void Update()
        {
            if (flow == null)
            {
                return;
            }
            flow.Tick();
            RefreshHostReadinessIfDue();
            PublishStateIfChanged();
        }

        private void InitializeFlow()
        {
            if (flow != null)
            {
                return;
            }
            EnsureConfigured();
            captureBinding.ApplyToSampler();
            flow = new InteractionStudyFlow(
                new UnityInteractionStudyRunPort(
                    runController,
                    captureBinding
                ),
                new UnityInteractionStudyPresentationPort(
                    presentationController
                ),
                new UnityInteractionStudyTaskPort(phaseCoordinator)
            );
            flow.StateChanged += HandleFlowStateChanged;
            PublishStateIfChanged(force: true);
        }

        private IEnumerator LoadManifestRoutine(long token)
        {
            try
            {
                initializationStatus = "Loading instruction manifest.";
                PublishStateIfChanged(force: true);
                string requestUri;
                try
                {
                    requestUri = BuildStreamingAssetUri(
                        contentManifestRelativePath
                    );
                }
                catch (Exception exception)
                {
                    SetManifestFailureIfCurrent(
                        token,
                        "Manifest path is invalid: " + exception.Message
                    );
                    yield break;
                }

                UnityWebRequest request;
                try
                {
                    request = UnityWebRequest.Get(requestUri);
                }
                catch (Exception exception)
                {
                    SetManifestFailureIfCurrent(
                        token,
                        "Instruction manifest request creation failed: " +
                        exception.Message
                    );
                    yield break;
                }

                using (request)
                {
                    UnityWebRequestAsyncOperation operation;
                    try
                    {
                        operation = request.SendWebRequest();
                    }
                    catch (Exception exception)
                    {
                        SetManifestFailureIfCurrent(
                            token,
                            "Instruction manifest request failed to start: " +
                            exception.Message
                        );
                        yield break;
                    }
                    yield return operation;
                    if (!IsManifestLoadCurrent(token))
                    {
                        yield break;
                    }
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        SetManifestFailureIfCurrent(
                            token,
                            "Instruction manifest load failed: " +
                            request.error
                        );
                        yield break;
                    }

                    byte[] bytes = request.downloadHandler?.data;
                    if (!TryConfigureManifestBytes(
                            bytes,
                            out string manifestError))
                    {
                        SetManifestFailureIfCurrent(
                            token,
                            "Instruction manifest configuration failed: " +
                            manifestError
                        );
                        yield break;
                    }
                    if (!IsManifestLoadCurrent(token))
                    {
                        yield break;
                    }
                    manifestReady = true;
                    initializationStatus =
                        "Instruction manifest is configured.";
                    readinessPollGate.Invalidate();
                    runController.InvalidateHostReadiness();
                }
            }
            finally
            {
                if (manifestLoadGate.TryComplete(token))
                {
                    manifestRoutine = null;
                    PublishStateIfChanged(force: true);
                }
            }
        }

        private void RefreshHostReadinessIfDue()
        {
            if (!manifestReady || runController == null ||
                runController.State != RunState.PreStart ||
                runController.HostClient == null)
            {
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!readinessPollGate.TryBegin(now, out long token))
            {
                return;
            }
            try
            {
                runController.RefreshHostReadiness(() =>
                {
                    if (readinessPollGate.TryComplete(
                            token,
                            Time.realtimeSinceStartupAsDouble))
                    {
                        TryAdoptAutomaticIdentity(
                            runController.LastHostReadiness,
                            ResolveAutomaticBuildIdentity()
                        );
                        PublishStateIfChanged(force: true);
                    }
                });
            }
            catch (Exception exception)
            {
                readinessPollGate.TryComplete(
                    token,
                    Time.realtimeSinceStartupAsDouble
                );
                initializationStatus =
                    "Host readiness request failed to start: " +
                    exception.Message;
                PublishStateIfChanged(force: true);
            }
        }

        private bool TryAdoptAutomaticIdentity(
            InteractionHostReadiness readiness,
            string buildIdentity)
        {
            if (runController == null ||
                runController.State != RunState.PreStart ||
                readiness == null ||
                !readiness.ParticipantReady ||
                !readiness.ParticipantFresh ||
                string.IsNullOrWhiteSpace(readiness.ParticipantId))
            {
                return false;
            }

            string participant = readiness.ParticipantId.Trim();
            string build = buildIdentity?.Trim() ?? string.Empty;
            if (identityArmed &&
                string.Equals(
                    ConfiguredParticipantId,
                    participant,
                    StringComparison.Ordinal
                ) &&
                string.Equals(
                    ConfiguredBuildIdentity,
                    build,
                    StringComparison.Ordinal
                ))
            {
                return false;
            }

            if (identityArmed)
            {
                DisarmIdentityInternal(
                    "主机已为下一轮分配新的匿名实验编号。",
                    invalidateReadiness: true
                );
            }

            InteractionStudyFlowCommandResult result = TryConfigureIdentity(
                participant,
                build
            );
            if (!result.Succeeded)
            {
                return false;
            }

            identityStatus = "匿名实验编号已自动准备：" + participant;
            PublishStateIfChanged(force: true);
            return true;
        }

        private static string ResolveAutomaticBuildIdentity()
        {
            string buildGuid = Application.buildGUID?.Trim();
            if (!string.IsNullOrWhiteSpace(buildGuid) &&
                buildGuid.Trim('0', '-').Length > 0)
            {
                return buildGuid;
            }

            string version = Application.version ?? string.Empty;
            char[] safe = version.Select(character =>
                (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '.' || character == '_' || character == '-'
                    ? character
                    : '-'
            ).ToArray();
            string suffix = new string(safe).Trim('-', '.');
            return "version-" +
                (string.IsNullOrWhiteSpace(suffix) ? "unversioned" : suffix);
        }

        internal bool TryConfigureManifestBytes(
            byte[] bytes,
            out string error)
        {
            if (bytes == null || bytes.Length == 0)
            {
                error = "The manifest response was empty.";
                return false;
            }
            if (runController == null)
            {
                error = "W6 Run controller is missing.";
                return false;
            }
            try
            {
                runController.ConfigureContentManifest(bytes);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                // Manifest readers may surface FormatException, JSON/parser
                // exceptions, validation exceptions, or provider failures.
                // None may strand the coroutine's retry gate.
                error = exception.Message;
                return false;
            }
        }

        private bool IsManifestLoadCurrent(long token)
        {
            return manifestLoadGate.InFlight && token == manifestLoadToken;
        }

        private void SetManifestFailureIfCurrent(long token, string error)
        {
            if (!IsManifestLoadCurrent(token))
            {
                return;
            }
            manifestReady = false;
            initializationStatus = error;
        }

        private void StopManifestLoad(string reason)
        {
            bool wasInFlight = manifestLoadGate.InFlight;
            Coroutine routine = manifestRoutine;
            manifestRoutine = null;
            manifestLoadGate.Invalidate();
            manifestLoadToken = 0L;
            if (routine != null)
            {
                try
                {
                    StopCoroutine(routine);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
            if (wasInFlight && !manifestReady)
            {
                initializationStatus = string.IsNullOrWhiteSpace(reason)
                    ? "Instruction manifest load was cancelled; Retry is " +
                        "available in PreStart."
                    : reason;
            }
        }

        private void DisarmIdentityInternal(
            string reason,
            bool invalidateReadiness)
        {
            identityArmed = false;
            armedParticipantId = string.Empty;
            armedBuildIdentity = string.Empty;
            identityStatus = string.IsNullOrWhiteSpace(reason)
                ? "正在等待主机自动分配匿名实验编号。"
                : reason;
            readinessPollGate.Invalidate();
            if (invalidateReadiness && runController != null)
            {
                runController.InvalidateHostReadiness();
            }
        }

        private void InvalidateReadinessPolling()
        {
            readinessPollGate.Invalidate();
            if (runController != null)
            {
                runController.InvalidateHostReadiness();
            }
        }

        private void SafeSuspendFlow(string reason)
        {
            if (flow == null)
            {
                return;
            }
            try
            {
                flow.Suspend(reason);
            }
            catch (Exception exception)
            {
                initializationStatus =
                    "Study Flow suspension reported an error: " +
                    exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private void SafeResumeFlow()
        {
            if (flow == null)
            {
                return;
            }
            try
            {
                flow.Resume();
            }
            catch (Exception exception)
            {
                initializationStatus =
                    "Study Flow resume reported an error: " +
                    exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private void HandleFlowStateChanged()
        {
            if (reconfiguring)
            {
                return;
            }
            if (identityArmed && flow?.Snapshot?.RunState !=
                RunState.PreStart)
            {
                DisarmIdentityInternal(
                    "Run consumed; reconfirm identity for the next Run.",
                    invalidateReadiness: false
                );
            }
            PublishStateIfChanged(force: true);
        }

        private void PublishStateIfChanged(bool force = false)
        {
            string fingerprint = BuildFingerprint();
            if (!force && string.Equals(
                    fingerprint,
                    lastPublishedFingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }
            lastPublishedFingerprint = fingerprint;
            StateChanged?.Invoke();
        }

        private string BuildFingerprint()
        {
            InteractionStudyFlowSnapshot snapshot = flow?.Snapshot;
            if (snapshot == null)
            {
                return manifestReady + "|" + initializationStatus + "|" +
                    identityArmed + "|" + identityStatus;
            }
            return manifestReady + "|" + initializationStatus + "|" +
                identityArmed + "|" + identityStatus + "|" +
                snapshot.RunState + "|" + snapshot.PhaseId + "|" +
                snapshot.Progress + "|" + snapshot.RequiredProgress + "|" +
                snapshot.CanStart + "|" + snapshot.CanReplay + "|" +
                snapshot.CanGiveUp + "|" + snapshot.AbortInProgress + "|" +
                snapshot.Status;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!Application.isPlaying || flow == null)
            {
                return;
            }
            applicationPaused = paused;
            if (paused)
            {
                StopManifestLoad(
                    "Application paused during manifest load; Retry will " +
                    "resume in PreStart."
                );
                InvalidateReadinessPolling();
                SafeSuspendFlow("application_pause");
            }
            else if (isActiveAndEnabled)
            {
                SafeResumeFlow();
                try
                {
                    flow.Tick();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
                RetryManifestLoad();
            }
            PublishStateIfChanged(force: true);
        }

        private void OnApplicationQuit()
        {
            StopManifestLoad("Application quit during manifest load.");
            InvalidateReadinessPolling();
            if (Application.isPlaying)
            {
                SafeSuspendFlow("application_quit");
            }
        }

        private void OnDisable()
        {
            StopManifestLoad(
                "Component disabled during manifest load; Retry will resume " +
                "in PreStart."
            );
            InvalidateReadinessPolling();
            if (Application.isPlaying)
            {
                SafeSuspendFlow("component_disabled");
            }
            PublishStateIfChanged(force: true);
        }

        private void OnDestroy()
        {
            StopManifestLoad("Component destroyed during manifest load.");
            InvalidateReadinessPolling();
            InteractionStudyFlow disposing = flow;
            flow = null;
            if (disposing != null)
            {
                disposing.StateChanged -= HandleFlowStateChanged;
                try
                {
                    disposing.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
            StateChanged = null;
        }

        private void EnsureConfigured()
        {
            if (runController == null || presentationController == null ||
                phaseCoordinator == null || captureBinding == null)
            {
                throw new InvalidOperationException(
                    "W8 requires W6 RunController, W5 presentation, W7 " +
                    "coordinator, and the W8 capture binding."
                );
            }
        }

        private static string BuildStreamingAssetUri(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException(
                    "Manifest relative path is required.",
                    nameof(relativePath)
                );
            }
            string combined = Path.Combine(
                Application.streamingAssetsPath,
                relativePath.Trim().TrimStart('/', '\\')
            ).Replace('\\', '/');
            if (combined.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                combined.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                combined.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                combined.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return combined;
            }
            return new Uri(combined, UriKind.Absolute).AbsoluteUri;
        }
    }
}
