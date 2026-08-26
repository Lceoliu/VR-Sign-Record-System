using System;
using System.Collections;
using System.IO;
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
        private static ParticipantSession applicationParticipantSession;

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
        private ParticipantSession participantSession;
        private long manifestLoadToken;
        private bool manifestReady;
        private bool automaticIdentityConfigured;
        private bool recoveryComplete;
        private int recoveredPartialRunCount;
        private bool applicationPaused;
        private bool activeRunPaused;
        private bool pauseAbortNoticePending;
        private bool reconfiguring;
        private string recoveryFailure = string.Empty;
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        private bool recoveryStateOverriddenForTests;
#endif
        private string initializationStatus =
            "Instruction manifest has not loaded.";
        private string identityStatus =
            "正在自动准备匿名实验编号。";
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
        public bool IdentityArmed => automaticIdentityConfigured;
        public bool RecoveryInFlight => !recoveryComplete &&
            string.IsNullOrEmpty(recoveryFailure);
        public bool RecoveryComplete => recoveryComplete;
        public bool RecoveryFailed => !string.IsNullOrEmpty(recoveryFailure);
        public string RecoveryFailure => recoveryFailure;
        public int RecoveredPartialRunCount => recoveredPartialRunCount;
        public string ParticipantSessionId =>
            participantSession?.ParticipantId ?? string.Empty;
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

            StopManifestLoad("Study dependencies were reconfigured.");
            runController = validatedRun;
            presentationController = validatedPresentation;
            phaseCoordinator = validatedPhases;
            captureBinding = validatedCapture;
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            recoveryStateOverriddenForTests = false;
#endif
            SyncStartupRecoveryState();
            manifestReady = false;
            initializationStatus =
                "Instruction manifest must be loaded for this configuration.";
            automaticIdentityConfigured = false;
            if (participantSession != null)
            {
                ApplyAutomaticIdentityToCurrentRun();
            }
            if (Application.isPlaying && isActiveAndEnabled)
            {
                RetryManifestLoad();
            }
            PublishStateIfChanged(force: true);
        }

        public InteractionStudyFlowCommandResult TryStart()
        {
            SyncStartupRecoveryState();
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
            if (!recoveryComplete)
            {
                return InteractionStudyFlowCommandResult.Failure(
                    RecoveryFailed
                        ? "Startup partial-Run recovery failed."
                        : "Startup partial-Run recovery is still running."
                );
            }
            if (!automaticIdentityConfigured)
            {
                return InteractionStudyFlowCommandResult.Failure(
                    identityStatus
                );
            }
            InteractionStudyFlowCommandResult result = flow.TryStart();
            if (result.Succeeded)
            {
                identityStatus = "匿名实验编号已准备。";
                PublishStateIfChanged(force: true);
            }
            return result;
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
            SyncStartupRecoveryState();
            RetryManifestLoad();
        }

        private void Update()
        {
            if (flow == null)
            {
                return;
            }
            SyncStartupRecoveryState();
            flow.Tick();
            PublishStateIfChanged();
        }

        private void InitializeFlow()
        {
            if (flow != null)
            {
                return;
            }
            EnsureConfigured();
            EnsureParticipantSession(() => new ParticipantSession());
            ApplyAutomaticIdentityToCurrentRun();
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

        private ParticipantSession EnsureParticipantSession(
            Func<ParticipantSession> factory)
        {
            if (participantSession != null)
            {
                return participantSession;
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (Application.isPlaying)
            {
                applicationParticipantSession ??= factory() ??
                    throw new InvalidOperationException(
                        "Participant Session factory returned null."
                    );
                participantSession = applicationParticipantSession;
            }
            else
            {
                participantSession = factory() ??
                    throw new InvalidOperationException(
                        "Participant Session factory returned null."
                    );
            }
            return participantSession;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetApplicationParticipantSession()
        {
            applicationParticipantSession = null;
        }

        private void ApplyAutomaticIdentityToCurrentRun()
        {
            automaticIdentityConfigured = false;
            if (runController == null || participantSession == null)
            {
                identityStatus = "匿名实验编号尚未准备。";
                return;
            }
            if (runController.State != RunState.PreStart)
            {
                identityStatus =
                    "Automatic identity can be applied only in PreStart.";
                return;
            }
            try
            {
                runController.ConfigureIdentity(
                    runController.BatchId,
                    participantSession.ParticipantId,
                    ResolveAutomaticBuildIdentity()
                );
                automaticIdentityConfigured = true;
                identityStatus = "匿名实验编号已准备。";
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                identityStatus =
                    "Automatic participant identity failed: " +
                    exception.Message;
            }
        }

        private void SyncStartupRecoveryState()
        {
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            if (recoveryStateOverriddenForTests)
            {
                return;
            }
#endif
            if (runController == null)
            {
                recoveryComplete = false;
                recoveredPartialRunCount = 0;
                recoveryFailure = string.Empty;
                return;
            }
            InteractionStandaloneLocalRunRecoveryStatus status =
                runController.StartupRecoveryStatus;
            recoveredPartialRunCount = runController.RecoveredPartialRunCount;
            recoveryComplete = status ==
                InteractionStandaloneLocalRunRecoveryStatus.Succeeded;
            if (status == InteractionStandaloneLocalRunRecoveryStatus.Failed)
            {
                recoveryFailure = string.IsNullOrWhiteSpace(
                        runController.StartupRecoveryFailureReason)
                    ? "Quest-local startup recovery failed."
                    : runController.StartupRecoveryFailureReason;
            }
            else
            {
                recoveryFailure = string.Empty;
            }
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
            char[] safe = version.ToCharArray();
            for (int index = 0; index < safe.Length; index++)
            {
                char character = safe[index];
                bool allowed =
                    (character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') ||
                    character == '.' || character == '_' ||
                    character == '-';
                if (!allowed)
                {
                    safe[index] = '-';
                }
            }
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
                    automaticIdentityConfigured + "|" + identityStatus + "|" +
                    recoveryComplete + "|" + recoveredPartialRunCount + "|" +
                    recoveryFailure + "|" +
                    pauseAbortNoticePending;
            }
            return manifestReady + "|" + initializationStatus + "|" +
                automaticIdentityConfigured + "|" + identityStatus + "|" +
                recoveryComplete + "|" + recoveredPartialRunCount + "|" +
                recoveryFailure + "|" +
                pauseAbortNoticePending + "|" +
                snapshot.RunState + "|" + snapshot.PhaseId + "|" +
                snapshot.Progress + "|" + snapshot.RequiredProgress + "|" +
                snapshot.CanStart + "|" + snapshot.CanReplay + "|" +
                snapshot.CanGiveUp + "|" + snapshot.AbortInProgress + "|" +
                snapshot.Status;
        }

        private void OnApplicationPause(bool paused)
        {
            HandleApplicationPause(paused, resumeWhenInactive: false);
        }

        private void HandleApplicationPause(
            bool paused,
            bool resumeWhenInactive)
        {
            if ((!Application.isPlaying && !resumeWhenInactive) || flow == null ||
                applicationPaused == paused)
            {
                return;
            }
            applicationPaused = paused;
            if (paused)
            {
                activeRunPaused = InteractionLifecycleTerminationPolicy
                    .RequiresLocalAbort(flow.Snapshot.RunState);
                StopManifestLoad(
                    "Application paused during manifest load; Retry will " +
                    "resume in PreStart."
                );
                SafeSuspendFlow("application_pause");
            }
            else if (resumeWhenInactive || isActiveAndEnabled)
            {
                if (activeRunPaused)
                {
                    pauseAbortNoticePending = true;
                    activeRunPaused = false;
                }
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
            if (Application.isPlaying)
            {
                SafeSuspendFlow("component_disabled");
            }
            PublishStateIfChanged(force: true);
        }

        private void OnDestroy()
        {
            StopManifestLoad("Component destroyed during manifest load.");
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

        internal bool TryConsumePauseAbortNotice()
        {
            if (!pauseAbortNoticePending)
            {
                return false;
            }
            pauseAbortNoticePending = false;
            return true;
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal ParticipantSession EnsureParticipantSessionForTests(
            Func<ParticipantSession> factory)
        {
            return EnsureParticipantSession(factory);
        }

        internal void ApplyAutomaticIdentityForTests(
            Func<ParticipantSession> factory)
        {
            EnsureParticipantSession(factory);
            ApplyAutomaticIdentityToCurrentRun();
        }

        internal void InstallStandaloneStateForTests(
            InteractionStudyFlow installedFlow,
            bool manifestIsReady,
            bool recoveryIsComplete,
            ParticipantSession session)
        {
            if (installedFlow == null)
            {
                throw new ArgumentNullException(nameof(installedFlow));
            }
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (flow != null && !ReferenceEquals(flow, installedFlow))
            {
                throw new InvalidOperationException(
                    "A different Study Flow is already installed."
                );
            }
            flow = installedFlow;
            flow.StateChanged -= HandleFlowStateChanged;
            flow.StateChanged += HandleFlowStateChanged;
            participantSession = session;
            automaticIdentityConfigured = true;
            manifestReady = manifestIsReady;
            recoveryComplete = recoveryIsComplete;
            recoveredPartialRunCount = 0;
            recoveryFailure = string.Empty;
            recoveryStateOverriddenForTests = true;
            identityStatus = "匿名实验编号已准备。";
            PublishStateIfChanged(force: true);
        }

        internal void SetRecoveryStateForTests(
            bool complete,
            string failure)
        {
            recoveryComplete = complete;
            recoveredPartialRunCount = 0;
            recoveryFailure = failure ?? string.Empty;
            recoveryStateOverriddenForTests = true;
            PublishStateIfChanged(force: true);
        }

        internal void HandleApplicationPauseForTests(bool paused)
        {
            HandleApplicationPause(paused, resumeWhenInactive: true);
        }
#endif

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
