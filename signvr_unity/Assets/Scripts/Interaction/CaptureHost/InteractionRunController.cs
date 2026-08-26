using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.CaptureHost
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed partial class InteractionRunController : MonoBehaviour
    {
        private const string DeviceIdPlayerPrefsKey = "SignVR.DeviceId";

        [Header("Mode")]
        [SerializeField]
        private InteractionRunMode runMode = InteractionRunMode.StandaloneStudy;

        [SerializeField]
        [Tooltip("Must stay false for Study. This is a startup self-check, not an override implementation.")]
        private bool debugOverridesActive;

        [SerializeField]
        [Tooltip("EngineeringLocal requires this explicit arm flag and a debug build.")]
        private bool engineeringLocalExplicitlyArmed;

        [SerializeField]
        private bool requireHostForStart = true;

        [Header("Run identity")]
        [SerializeField]
        private string batchId = "pilot-20260826";

        [SerializeField]
        private string participantId = "UNCONFIGURED";

        [SerializeField]
        private string gitCommit = "unintegrated";

        [Header("Host")]
        [SerializeField]
        private InteractionHostClient hostClient;

        [SerializeField]
        [Min(0.5f)]
        private float readinessMaximumAgeSeconds = 5f;

        [SerializeField]
        [Range(1, 10)]
        private int automaticRegistrationAttempts = 3;

        [SerializeField]
        [Min(0.25f)]
        private float registrationRetryDelaySeconds = 2f;

        [SerializeField]
        [Range(1, 20)]
        private int acknowledgementPollAttempts = 5;

        [SerializeField]
        [Min(0.25f)]
        private float acknowledgementPollDelaySeconds = 2f;

        [Header("Capture")]
        [SerializeField]
        private InteractionCaptureSampler captureSampler;

        [SerializeField]
        [Min(16)]
        private int captureQueueCapacity =
            InteractionCaptureWriter.DefaultQueueCapacity;

        [SerializeField]
        [Min(0.05f)]
        private float captureGapThresholdSeconds =
            (float)InteractionCaptureWriter.DefaultGapThresholdSeconds;

        private readonly List<InteractionPendingRun> pendingRuns =
            new List<InteractionPendingRun>();
        private readonly InteractionLatestResponseGate readinessResponseGate =
            new InteractionLatestResponseGate();
        private readonly InteractionLifecycleShutdownGate lifecycleShutdown =
            new InteractionLifecycleShutdownGate();
        private readonly InteractionTerminalSealArbiter terminalSealArbiter =
            new InteractionTerminalSealArbiter();
        private readonly InteractionArtifactOperationRegistry artifactOperations =
            new InteractionArtifactOperationRegistry();
        private IInteractionArtifactReadObserver artifactReadObserver =
            InteractionArtifactReadObserver.None;
        private IInteractionBackgroundWorkQueue lifecycleWorkQueue =
            InteractionThreadPoolBackgroundWorkQueue.Shared;

        private AssistanceBlockAllocator conditionAllocator;
        private InteractionRunStateMachine stateMachine;
        private InstructionContentCatalog contentCatalog;
        private string appSessionId;
        private string questDeviceId;
        private InteractionHostReadiness lastReadiness;
        private double lastReadinessMonotonic = double.NegativeInfinity;
        private InteractionFrozenRunRegistration frozenRegistration;
        private InteractionScheduledStartGate scheduledStartGate;
        private InteractionCaptureWriter captureWriter;
        private InteractionSummaryTracker summaryTracker;
        private InteractionUploadStateMachine uploadStateMachine;
        private InteractionPendingRun pendingUploadTarget;
        private InteractionPresentationHandshake presentationHandshake;
        private InteractionBackgroundOperation<InteractionCaptureWriter>
            captureInitialization;
        private InteractionBackgroundOperation<InteractionCaptureSealResult>
            captureTerminalization;
        private InteractionBackgroundOperation<bool> activePhaseCheckpoint;
        private InteractionCaptureTerminalKind? captureTerminalKind;
        private string captureTerminalAbortReason;
        private DateTimeOffset captureTerminalUtc;
        private bool captureTerminalShouldUpload;
        private bool captureTerminalizationReconciled;
        private Coroutine initializationRoutine;
        private Coroutine registrationRoutine;
        private Coroutine phaseCheckpointRoutine;
        private Coroutine terminalizationRoutine;
        private Coroutine uploadRoutine;
        private InteractionLifecycleTerminalizationJob
            lifecycleTerminalizationJob;
        private bool hostRegistrationAccepted;
        private bool textExposureActive;
        private bool pointingExposureActive;
        private string pointingTargetId;
        private string lastError = string.Empty;
#if UNITY_EDITOR
        private bool editorLifecycleTestsArmed;
        private InteractionDetachedInitializationOwner
            detachedInitializationOwnerForTests;
#endif

        public RunState State => stateMachine == null
            ? RunState.PreStart
            : stateMachine.State;
        public PhaseState? CurrentPhaseState => stateMachine == null ||
            stateMachine.CurrentPhase == null
                ? (PhaseState?)null
                : stateMachine.CurrentPhase.State;
        public int? CurrentPhaseId => stateMachine?.CurrentPhaseId;
        public RunPlan Plan => stateMachine?.Plan;
        public InteractionRunStateMachine StateMachine => stateMachine;
        public bool IsCaptureActive => captureWriter != null &&
            captureWriter.CaptureActive && State == RunState.Running;
        public bool RegistrationInFlight => initializationRoutine != null ||
            registrationRoutine != null;
        public bool UploadInFlight => uploadRoutine != null;
        public bool EligibleForLocalCleanup => uploadStateMachine != null &&
            uploadStateMachine.EligibleForLocalCleanup;
        public string LastError => lastError;
        public string AppSessionId => appSessionId;
        public string QuestDeviceId => questDeviceId;
        public string BatchId => batchId;
        public string ParticipantId => participantId;
        public string GitCommit => gitCommit;
        public InteractionRunMode RunMode => runMode;
        public bool DebugOverridesActive => debugOverridesActive;
        public bool RequireHostForStart => requireHostForStart;
        public InteractionHostClient HostClient => hostClient;
        public InteractionHostReadiness LastHostReadiness => lastReadiness;
        public InteractionCaptureSampler CaptureSampler => captureSampler;
        public InteractionPresentationRequest PendingPresentationRequest =>
            presentationHandshake?.PendingRequest;
        public IReadOnlyList<InteractionPendingRun> PendingRuns => pendingRuns;

        public event Action<InteractionPresentationRequest>
            PresentationRequested;

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            conditionAllocator = new AssistanceBlockAllocator();
            stateMachine = new InteractionRunStateMachine(
                conditionAllocator,
                new RunPlanGenerator()
            );
            appSessionId = "app_" + Guid.NewGuid().ToString("N");
            questDeviceId = ResolveQuestDeviceId();
            if (hostClient == null)
            {
                hostClient = GetComponent<InteractionHostClient>();
            }
            if (hostClient != null)
            {
                hostClient.ConfigureQuestDeviceId(questDeviceId);
            }
            RefreshPendingRuns();
            RefreshHeartbeatConfiguration();
        }

        public void ConfigureHostClient(InteractionHostClient value)
        {
            hostClient = value ?? throw new ArgumentNullException(nameof(value));
            if (!string.IsNullOrWhiteSpace(questDeviceId))
            {
                hostClient.ConfigureQuestDeviceId(questDeviceId);
            }
            RefreshHeartbeatConfiguration();
        }

        public void ConfigureCaptureSampler(InteractionCaptureSampler value)
        {
            captureSampler = value ??
                throw new ArgumentNullException(nameof(value));
        }

        public void ConfigureContentCatalog(InstructionContentCatalog value)
        {
            if (State != RunState.PreStart)
            {
                throw new InvalidOperationException(
                    "Content cannot change after a Run starts."
                );
            }
            contentCatalog = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void ConfigureContentManifest(byte[] manifestUtf8Bytes)
        {
            ConfigureContentCatalog(
                InteractionInstructionContentManifestReader.Read(
                    manifestUtf8Bytes
                )
            );
        }

        public void ConfigureIdentity(
            string configuredBatchId,
            string configuredParticipantId,
            string configuredGitCommit)
        {
            if (State != RunState.PreStart)
            {
                throw new InvalidOperationException(
                    "Identity cannot change after a Run starts."
                );
            }
            batchId = InteractionStoragePaths.ValidateSegment(
                configuredBatchId,
                nameof(configuredBatchId)
            );
            participantId = InteractionStoragePaths.ValidateSegment(
                configuredParticipantId,
                nameof(configuredParticipantId)
            );
            if (string.IsNullOrWhiteSpace(configuredGitCommit))
            {
                throw new ArgumentException(
                    "Git commit identity is required.",
                    nameof(configuredGitCommit)
                );
            }
            gitCommit = configuredGitCommit.Trim();
            InvalidateHostReadiness();
            RefreshHeartbeatConfiguration();
        }

        public void ConfigureMode(
            InteractionRunMode configuredMode,
            bool configuredDebugOverridesActive,
            bool explicitlyArmEngineeringLocal,
            bool configuredRequireHost)
        {
            if (State != RunState.PreStart)
            {
                throw new InvalidOperationException(
                    "Run mode cannot change after a Run starts."
                );
            }
            runMode = configuredMode;
            debugOverridesActive = configuredDebugOverridesActive;
            engineeringLocalExplicitlyArmed = explicitlyArmEngineeringLocal;
            requireHostForStart = configuredRequireHost;
            RefreshHeartbeatConfiguration();
        }

        public void RefreshHostReadiness()
        {
            RefreshHostReadiness(null);
        }

        /// <summary>
        /// W8 completion seam for one-at-a-time readiness polling. The callback
        /// is lifecycle-only; W6 remains the sole owner of readiness parsing,
        /// freshness, participant matching, and CanStart policy.
        /// </summary>
        public void RefreshHostReadiness(Action completed)
        {
            if (hostClient == null)
            {
                lastError = "Interaction Host client is not configured.";
                completed?.Invoke();
                return;
            }
            long generation = readinessResponseGate.Issue();
            try
            {
                StartCoroutine(hostClient.GetReadiness(result =>
                {
                    try
                    {
                        if (!readinessResponseGate.TryAccept(generation))
                        {
                            return;
                        }
                        if (!result.Success)
                        {
                            lastError = "Host readiness failed: " + result.Error;
                            return;
                        }
                        lastReadiness = result.Value;
                        lastReadinessMonotonic = NowMonotonic();
                        lastError = string.Empty;
                    }
                    finally
                    {
                        completed?.Invoke();
                    }
                }));
            }
            catch
            {
                completed?.Invoke();
                throw;
            }
        }

        public void InvalidateHostReadiness()
        {
            readinessResponseGate.Issue();
            hostClient?.CancelReadinessRequest();
            lastReadiness = null;
            lastReadinessMonotonic = double.NegativeInfinity;
        }

        public bool CanStart(out string reason)
        {
            reason = null;
            if (stateMachine == null || State != RunState.PreStart)
            {
                reason = "Run is not in PreStart.";
                return false;
            }
            if (contentCatalog == null)
            {
                reason = "Instruction content manifest is not configured.";
                return false;
            }
            if (runMode == InteractionRunMode.StandaloneStudy &&
                string.Equals(
                    gitCommit,
                    "unintegrated",
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Study requires the integrated build Git commit identity.";
                return false;
            }
            try
            {
                InteractionStudyStartPolicy.Validate(
                    runMode,
                    debugOverridesActive,
                    engineeringLocalExplicitlyArmed,
                    Debug.isDebugBuild,
                    requireHostForStart
                );
                InteractionStoragePaths.ValidateSegment(batchId, nameof(batchId));
                InteractionStoragePaths.ValidateSegment(
                    participantId,
                    nameof(participantId)
                );
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                reason = exception.Message;
                return false;
            }

            if (runMode == InteractionRunMode.StandaloneStudy)
            {
                if (string.Equals(
                        participantId,
                        "UNCONFIGURED",
                        StringComparison.Ordinal))
                {
                    reason = "Study participant_id must be explicitly configured.";
                    return false;
                }
                if (hostClient == null || !hostClient.LastHeartbeatReady)
                {
                    reason =
                        "Study requires an accepted, exactly echoed Quest " +
                        "heartbeat from this application session.";
                    return false;
                }
                double readinessAge = NowMonotonic() - lastReadinessMonotonic;
                if (lastReadiness == null || readinessAge < 0d ||
                    readinessAge > readinessMaximumAgeSeconds)
                {
                    reason = "A fresh Host readiness response is required.";
                    return false;
                }
                if (!lastReadiness.IsStudyReady(
                        questDeviceId,
                        participantId))
                {
                    reason =
                        "Backend/storage plus exact fresh Quest, camera, and " +
                        "participant readiness must all match this Study identity.";
                    return false;
                }
                string captureReason = null;
                if (captureSampler == null ||
                    !captureSampler.IsStudyCaptureReady(
                        out captureReason))
                {
                    reason = captureSampler == null
                        ? "Study capture sampler is not configured."
                        : captureReason;
                    return false;
                }
                if (HasUnsealedPendingRun())
                {
                    reason =
                        "A consumed partial Interaction Run must be recovered " +
                        "as an explicit abort before another Study Run starts.";
                    return false;
                }
            }
            else if (requireHostForStart && hostClient == null)
            {
                reason = "EngineeringLocal is configured to require a Host.";
                return false;
            }
            return true;
        }

        public bool TryStartRun(out string error)
        {
            error = null;
            if (!CanStart(out error))
            {
                lastError = error;
                return false;
            }

            try
            {
                string safeParticipantId = InteractionStoragePaths.ValidateSegment(
                    participantId,
                    nameof(participantId)
                );
                int seed = CreateRandomSeed();
                var request = new RunPlanGenerationRequest(
                    batchId,
                    safeParticipantId,
                    appSessionId,
                    Application.version,
                    gitCommit,
                    seed,
                    contentCatalog
                );

                // W1 is the only Run/Phase authority. AllocateNext commits the
                // block slot only after this immutable plan is constructed.
                RunPlan plan = stateMachine.Start(request);
                byte[] bytes;
                try
                {
                    bytes = InteractionRunManifestContractV1.SerializeUtf8(plan);
                    frozenRegistration = new InteractionFrozenRunRegistration(
                        plan,
                        bytes
                    );
                    summaryTracker = new InteractionSummaryTracker(plan.RunId);
                    captureInitialization =
                        InteractionCaptureWriter.BeginCreateNew(
                        Application.persistentDataPath,
                        plan,
                        bytes,
                        captureQueueCapacity,
                        captureGapThresholdSeconds
                    );
                    presentationHandshake =
                        new InteractionPresentationHandshake(stateMachine);
                }
                catch (Exception writeFailure)
                {
                    if (State != RunState.Faulted)
                    {
                        stateMachine.FaultRun(
                            "manifest_or_capture_initialization_failed"
                        );
                    }
                    throw new IOException(
                        "RunPlan was consumed but could not be durably initialized.",
                        writeFailure
                    );
                }

                hostRegistrationAccepted = false;
                lastError = string.Empty;
                initializationRoutine = StartCoroutine(
                    InitializeConsumedRunRoutine(plan)
                );
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                error = exception.Message;
                lastError = error;
                Debug.LogError(
                    "[InteractionRunController] Start failed: " + exception,
                    this
                );
                return false;
            }
        }

        public void StartRun()
        {
            if (!TryStartRun(out string error))
            {
                Debug.LogWarning(
                    "[InteractionRunController] Start rejected: " + error,
                    this
                );
            }
        }

        public bool RetryHostRegistration()
        {
            if (State != RunState.AwaitingHost || frozenRegistration == null ||
                initializationRoutine != null ||
                captureWriter == null ||
                captureInitialization == null ||
                !captureInitialization.Succeeded ||
                registrationRoutine != null || hostClient == null)
            {
                return false;
            }
            registrationRoutine = StartCoroutine(RegisterCurrentRunRoutine());
            return true;
        }

        public bool RetryPendingUpload(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId) || hostClient == null ||
                uploadRoutine != null)
            {
                return false;
            }
            RefreshPendingRuns();
            pendingUploadTarget = pendingRuns.Find(run =>
                run.NeedsUpload && string.Equals(
                    run.RunId,
                    runId.Trim(),
                    StringComparison.Ordinal
                )
            );
            if (pendingUploadTarget == null)
            {
                lastError = "No sealed pending Run matches " + runId + ".";
                return false;
            }
            uploadRoutine = StartCoroutine(
                UploadDiscoveredRunRoutine(pendingUploadTarget)
            );
            return true;
        }

        public InteractionBackgroundOperation<int>
            BeginRecoverAllPartialRunsAsAborted(
            string reason = "recovered_after_process_restart")
        {
            if (State != RunState.PreStart || registrationRoutine != null ||
                uploadRoutine != null)
            {
                throw new InvalidOperationException(
                    "Partial recovery requires an idle PreStart controller."
                );
            }
            return InteractionPartialRunRecovery.BeginTerminalizeAllAborted(
                Application.persistentDataPath,
                reason,
                DateTimeOffset.UtcNow,
                NowMonotonic(),
                Time.frameCount
            );
        }

        public void NotifyInstructionPlaybackStarted(
            long requestSequence,
            int phaseId,
            InteractionPresentationPlaybackKind playbackKind)
        {
            if (presentationHandshake == null)
            {
                throw new InvalidOperationException(
                    "No presentation handshake is active."
                );
            }
            double now = NowMonotonic();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            InteractionPresentationStart started =
                presentationHandshake.AcknowledgePlaybackStarted(
                    requestSequence,
                    phaseId,
                    playbackKind,
                    now
                );
            if (started.IsInitialRunStart)
            {
                captureSampler?.ResetCadence();
                captureWriter.BeginCapture();
                summaryTracker.BeginRun(now, utcNow);
                summaryTracker.BeginPhase(phaseId, now);
                RecordAt(
                    InteractionEventNames.RunStarted,
                    null,
                    now,
                    null,
                    null,
                    "{\"start_at_utc\":" + Quote(
                        stateMachine.ScheduledStartUtc.HasValue
                            ? InteractionRunManifestContractV1.FormatUtc(
                                stateMachine.ScheduledStartUtc.Value
                            )
                            : InteractionRunManifestContractV1.FormatUtc(
                                utcNow
                            )
                    ) + "}"
                );
                RecordAt(
                    InteractionEventNames.PhaseEntered,
                    phaseId,
                    now,
                    null,
                    null,
                    "{}"
                );
            }
            else if (started.PreviousPhaseId.HasValue)
            {
                summaryTracker.BeginPhase(phaseId, now);
                RecordAt(
                    InteractionEventNames.PhaseEntered,
                    phaseId,
                    now,
                    null,
                    null,
                    "{}"
                );
            }
            else if (playbackKind ==
                InteractionPresentationPlaybackKind.Replay)
            {
                summaryTracker.RecordReplay(phaseId, now);
                RecordAt(
                    InteractionEventNames.ReplayUsed,
                    phaseId,
                    now,
                    null,
                    null,
                    "{}"
                );
            }
            RecordAt(
                InteractionEventNames.InstructionPlayStarted,
                phaseId,
                now,
                null,
                null,
                playbackKind == InteractionPresentationPlaybackKind.Replay
                    ? "{\"playback\":\"replay\"}"
                    : "{\"playback\":\"first\"}"
            );
            lastError = string.Empty;
        }

        public void NotifyFirstPlaybackCompleted()
        {
            int phaseId = RequireCurrentPhaseId();
            stateMachine.FirstPlaybackCompleted();
            RecordNow(
                InteractionEventNames.InstructionPlayCompleted,
                phaseId,
                null,
                null,
                "{\"playback\":\"first\"}"
            );
            RecordNow(
                InteractionEventNames.ReplayAvailable,
                phaseId,
                null,
                null,
                "{}"
            );
        }

        public void ReplayInstruction()
        {
            int phaseId = RequireCurrentPhaseId();
            InteractionPresentationRequest request =
                presentationHandshake.RequestReplayPlayback();
            if (request.PhaseId != phaseId)
            {
                throw new InvalidOperationException(
                    "Replay presentation phase mismatched W1."
                );
            }
            PublishPresentationRequest(request);
        }

        public void NotifyReplayPlaybackCompleted()
        {
            int phaseId = RequireCurrentPhaseId();
            stateMachine.ReplayPlaybackCompleted();
            RecordNow(
                InteractionEventNames.InstructionPlayCompleted,
                phaseId,
                null,
                null,
                "{\"playback\":\"replay\"}"
            );
        }

        public int AdvanceTaskProgress(
            string actorId = null,
            string targetId = null,
            string payloadJson = null)
        {
            RequireCurrentPhaseId();
            int progress = stateMachine.AdvanceTaskProgress();
            return progress;
        }

        public void RecordInteractionAttempt(
            bool correct,
            string actorId = null,
            string targetId = null,
            string detailPayloadJson = null)
        {
            int phaseId = RequireCurrentPhaseId();
            double now = NowMonotonic();
            summaryTracker.RecordAttempt(phaseId, correct, now);
            string detail = InteractionJson.NormalizeObjectJson(
                detailPayloadJson
            );
            RecordAt(
                InteractionEventNames.InteractionAttempt,
                phaseId,
                now,
                actorId,
                targetId,
                "{\"correct\":" + (correct ? "true" : "false") +
                    ",\"detail\":" + detail + "}"
            );
        }

        public void RecordInteractionError(
            string actorId = null,
            string targetId = null,
            string payloadJson = null)
        {
            RecordInteractionValidationError(
                progressReset: true,
                actorId,
                targetId,
                payloadJson
            );
        }

        /// <summary>
        /// Records W7's authoritative validation outcome without inferring a
        /// progress reset from the mere presence of an interaction error.
        /// The legacy RecordInteractionError entry point retains its original
        /// reset behavior.
        /// </summary>
        public void RecordInteractionValidationError(
            bool progressReset,
            string actorId = null,
            string targetId = null,
            string payloadJson = null)
        {
            int phaseId = RequireCurrentPhaseId();
            double now = NowMonotonic();
            stateMachine.RecordInteractionError();
            summaryTracker.RecordError(phaseId, now);
            RecordAt(
                InteractionEventNames.InteractionError,
                phaseId,
                now,
                actorId,
                targetId,
                payloadJson
            );
            if (progressReset)
            {
                RecordAt(
                    InteractionEventNames.TaskProgressReset,
                    phaseId,
                    now,
                    actorId,
                    targetId,
                    "{\"reason\":\"interaction_error\"}"
                );
            }
        }

        public void NotifyBubbleShown()
        {
            int phaseId = RequireCurrentPhaseId();
            if (!Plan.AssistanceCondition.IncludesText() || textExposureActive)
            {
                throw new InvalidOperationException(
                    "Bubble cannot be shown for this condition or is already visible."
                );
            }
            double now = NowMonotonic();
            summaryTracker.BeginExposure(
                phaseId,
                InteractionExposureKind.Text,
                now
            );
            textExposureActive = true;
            RecordAt(
                InteractionEventNames.BubbleShown,
                phaseId,
                now,
                null,
                null,
                "{}"
            );
        }

        public void NotifyBubbleHidden()
        {
            int phaseId = RequireCurrentPhaseId();
            if (!textExposureActive)
            {
                return;
            }
            double now = NowMonotonic();
            summaryTracker.EndExposure(
                phaseId,
                InteractionExposureKind.Text,
                now
            );
            textExposureActive = false;
            RecordAt(
                InteractionEventNames.BubbleHidden,
                phaseId,
                now,
                null,
                null,
                "{}"
            );
        }

        public void NotifyPointingHitStarted(string targetId)
        {
            int phaseId = RequireCurrentPhaseId();
            if (!Plan.AssistanceCondition.IncludesPointing() ||
                pointingExposureActive)
            {
                throw new InvalidOperationException(
                    "Pointing cannot start for this condition or is already active."
                );
            }
            if (string.IsNullOrWhiteSpace(targetId))
            {
                throw new ArgumentException(
                    "Pointing target ID is required.",
                    nameof(targetId)
                );
            }
            double now = NowMonotonic();
            summaryTracker.BeginExposure(
                phaseId,
                InteractionExposureKind.Pointing,
                now
            );
            pointingExposureActive = true;
            pointingTargetId = targetId.Trim();
            RecordAt(
                InteractionEventNames.PointingHitStarted,
                phaseId,
                now,
                null,
                pointingTargetId,
                "{}"
            );
        }

        public void NotifyPointingHitEnded()
        {
            int phaseId = RequireCurrentPhaseId();
            if (!pointingExposureActive)
            {
                return;
            }
            double now = NowMonotonic();
            summaryTracker.EndExposure(
                phaseId,
                InteractionExposureKind.Pointing,
                now
            );
            RecordAt(
                InteractionEventNames.PointingHitEnded,
                phaseId,
                now,
                null,
                pointingTargetId,
                "{}"
            );
            pointingExposureActive = false;
            pointingTargetId = null;
        }

        public void CompleteCurrentPhase()
        {
            FinishCurrentPhase(stuck: false);
        }

        public void GiveUpCurrentPhase()
        {
            FinishCurrentPhase(stuck: true);
        }

        public void AbortRun(string reason)
        {
            if (!TryAbortRun(reason, out string error))
            {
                lastError = error;
            }
        }

        public bool TryAbortRun(string reason, out string error)
        {
            return TryAbortRunInternal(
                reason,
                allowHostUpload: true,
                out error
            );
        }

        private bool TryAbortRunInternal(
            string reason,
            bool allowHostUpload,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(reason))
            {
                error = "Abort reason is required.";
                return false;
            }
            if (State != RunState.AwaitingHost && State != RunState.Scheduled &&
                State != RunState.Running && State != RunState.Completing)
            {
                error = "Run cannot abort from " + State + ".";
                return false;
            }
            if (lifecycleShutdown.IsShutdownInitiated)
            {
                error = "Run abort is already owned by lifecycle shutdown.";
                return false;
            }
            InteractionAbortRequestDisposition disposition =
                terminalSealArbiter.TryRequestAbort(out error);
            if (disposition == InteractionAbortRequestDisposition.Rejected)
            {
                return false;
            }
            string safeReason = reason.Trim();
            double now = NowMonotonic();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            if (registrationRoutine != null)
            {
                hostClient?.CancelActiveRequests();
                StopCoroutine(registrationRoutine);
                registrationRoutine = null;
            }
            if (initializationRoutine != null)
            {
                StopCoroutine(initializationRoutine);
                initializationRoutine = null;
            }
            int? abortPhaseId = CurrentPhaseId;
            presentationHandshake?.CancelPending();
            stateMachine.AbortRun(safeReason);
            bool willUpload = allowHostUpload && requireHostForStart &&
                hostRegistrationAccepted;
            if (disposition ==
                InteractionAbortRequestDisposition.QueueAfterCheckpoint)
            {
                InteractionBackgroundOperation<bool> checkpoint =
                    activePhaseCheckpoint;
                if (checkpoint == null)
                {
                    error =
                        "Abort reserved behind a phase checkpoint that is not owned.";
                    lastError = error;
                    stateMachine.FaultRun("abort_checkpoint_ownership_lost");
                    terminalSealArbiter.MarkTerminal();
                    return false;
                }
                if (phaseCheckpointRoutine != null)
                {
                    StopCoroutine(phaseCheckpointRoutine);
                    phaseCheckpointRoutine = null;
                }
                terminalizationRoutine = StartCoroutine(
                    AbortAfterPhaseCheckpointRoutine(
                        checkpoint,
                        abortPhaseId,
                        safeReason,
                        now,
                        utcNow,
                        willUpload
                    )
                );
                return true;
            }
            if (captureWriter == null && captureInitialization != null)
            {
                if (allowHostUpload && isActiveAndEnabled)
                {
                    terminalizationRoutine = StartCoroutine(
                        AbortAfterInitializationRoutine(
                            safeReason,
                            now,
                            utcNow,
                            Time.frameCount,
                            willUpload
                        )
                    );
                }
                else
                {
                    StartLifecycleTerminalizationJob(
                        safeReason,
                        now,
                        utcNow,
                        Time.frameCount
                    );
                }
                return true;
            }
            try
            {
                BeginAbortSeal(
                    abortPhaseId,
                    safeReason,
                    now,
                    utcNow,
                    willUpload
                );
            }
            catch (Exception exception)
            {
                lastError = "Abort sealing failed: " + exception.Message;
                if (State == RunState.Aborting)
                {
                    stateMachine.FaultRun("abort_capture_seal_failed");
                }
                captureWriter?.Dispose();
                terminalSealArbiter.MarkTerminal();
                error = lastError;
                return false;
            }
            if (allowHostUpload && isActiveAndEnabled)
            {
                terminalizationRoutine = StartCoroutine(
                    AwaitTerminalSealRoutine(
                        InteractionCaptureTerminalKind.Aborted,
                        willUpload,
                        safeReason,
                        utcNow
                    )
                );
            }
            return true;
        }

        private void BeginAbortSeal(
            int? abortPhaseId,
            string reason,
            double monotonicNow,
            DateTimeOffset utcNow,
            bool willUpload)
        {
            CloseAssistanceExposures(abortPhaseId, monotonicNow);
            RecordAt(
                InteractionEventNames.RunAborted,
                null,
                monotonicNow,
                null,
                null,
                BuildReasonPayload(reason)
            );
            if (willUpload)
            {
                RecordAt(
                    InteractionEventNames.UploadStarted,
                    null,
                    monotonicNow,
                    null,
                    null,
                    "{\"terminal_status\":\"aborted\"}"
                );
            }
            captureTerminalization = captureWriter.BeginSeal(
                InteractionCaptureTerminalKind.Aborted,
                completeness => summaryTracker.SealAborted(
                    monotonicNow,
                    utcNow,
                    reason,
                    completeness
                )
            );
            terminalSealArbiter.MarkAbortedSealQueued();
            captureTerminalKind = InteractionCaptureTerminalKind.Aborted;
            captureTerminalAbortReason = reason;
            captureTerminalUtc = utcNow;
            captureTerminalShouldUpload = willUpload;
            captureTerminalizationReconciled = false;
        }

        private IEnumerator AbortAfterInitializationRoutine(
            string reason,
            double monotonicNow,
            DateTimeOffset utcNow,
            int frame,
            bool willUpload)
        {
            try
            {
                // Ensure StartCoroutine returns before this routine can clear
                // its ownership field on an already-completed operation.
                yield return null;
                while (captureInitialization != null &&
                    !captureInitialization.IsCompleted)
                {
                    yield return null;
                }
                if (captureInitialization == null ||
                    !captureInitialization.Succeeded)
                {
                    lastError =
                        "Abort retained initialization partial: " +
                        (captureInitialization?.Error?.Message ??
                         "capture initialization did not complete.");
                    if (State == RunState.Aborting)
                    {
                        stateMachine.FaultRun(
                            "abort_capture_initialization_failed"
                        );
                    }
                    terminalSealArbiter.MarkTerminal();
                    yield break;
                }
                captureWriter = captureInitialization.GetResult();
                BeginAbortSeal(
                    null,
                    reason,
                    monotonicNow,
                    utcNow,
                    willUpload
                );
                while (!captureTerminalization.IsCompleted)
                {
                    yield return null;
                }
                CompleteTerminalSeal(
                    InteractionCaptureTerminalKind.Aborted,
                    willUpload,
                    reason,
                    utcNow
                );
            }
            finally
            {
                terminalizationRoutine = null;
            }
        }

        private IEnumerator AbortAfterPhaseCheckpointRoutine(
            InteractionBackgroundOperation<bool> checkpoint,
            int? abortPhaseId,
            string reason,
            double monotonicNow,
            DateTimeOffset utcNow,
            bool willUpload)
        {
            try
            {
                yield return null;
                while (!checkpoint.IsCompleted)
                {
                    yield return null;
                }
                activePhaseCheckpoint = null;
                if (!checkpoint.Succeeded)
                {
                    Exception failure = checkpoint.Error;
                    lastError = "Abort retained checkpoint partial: " +
                        (failure == null ? "unknown background failure."
                            : failure.Message);
                    if (State == RunState.Aborting)
                    {
                        stateMachine.FaultRun(
                            "abort_capture_checkpoint_failed"
                        );
                    }
                    captureWriter?.Dispose();
                    terminalSealArbiter.MarkTerminal();
                    yield break;
                }
                if (terminalSealArbiter.CompleteCheckpoint() !=
                    InteractionCheckpointResolution.QueueAbort)
                {
                    throw new InvalidOperationException(
                        "Checkpoint lost its accepted Abort reservation."
                    );
                }
                BeginAbortSeal(
                    abortPhaseId,
                    reason,
                    monotonicNow,
                    utcNow,
                    willUpload
                );
                while (!captureTerminalization.IsCompleted)
                {
                    yield return null;
                }
                CompleteTerminalSeal(
                    InteractionCaptureTerminalKind.Aborted,
                    willUpload,
                    reason,
                    utcNow
                );
            }
            finally
            {
                activePhaseCheckpoint = null;
                terminalizationRoutine = null;
            }
        }

        public bool ResetToPreStart()
        {
            ReconcileLifecycleTerminalization();
            if (uploadRoutine != null ||
                lifecycleTerminalizationJob != null ||
                artifactOperations.ActiveCount != 0 ||
                (hostClient != null &&
                 hostClient.ActiveArtifactOperationCount != 0) ||
                (captureTerminalization != null &&
                 !captureTerminalization.IsCompleted) ||
                (State != RunState.Completed && State != RunState.Aborted &&
                 State != RunState.Faulted))
            {
                return false;
            }
            captureWriter?.Dispose();
            captureWriter = null;
            captureInitialization = null;
            captureTerminalization = null;
            activePhaseCheckpoint = null;
            captureTerminalKind = null;
            captureTerminalAbortReason = null;
            captureTerminalShouldUpload = false;
            captureTerminalizationReconciled = false;
            summaryTracker = null;
            frozenRegistration = null;
            scheduledStartGate = null;
            presentationHandshake = null;
            uploadStateMachine = null;
            hostRegistrationAccepted = false;
            textExposureActive = false;
            pointingExposureActive = false;
            pointingTargetId = null;
            lifecycleShutdown.Reset();
            terminalSealArbiter.Reset();
            lifecycleTerminalizationJob = null;
            artifactReadObserver = InteractionArtifactReadObserver.None;
            if (captureSampler != null)
            {
                captureSampler.ResetCadence();
                captureSampler.enabled = true;
            }
            stateMachine.ResetToPreStart();
            RefreshPendingRuns();
            RefreshHeartbeatConfiguration();
            return true;
        }

        public bool TryCapturePose(
            bool hmdValid,
            InteractionVector3Sample hmdPosition,
            InteractionQuaternionSample hmdRotation,
            InteractionHandSample leftHand,
            InteractionHandSample rightHand)
        {
            if (!IsCaptureActive)
            {
                return false;
            }
            try
            {
                return captureWriter.TryWritePose(new InteractionPoseSample(
                    CurrentPhaseId,
                    captureWriter.NextPoseSequence,
                    NowMonotonic(),
                    DateTimeOffset.UtcNow,
                    Time.frameCount,
                    hmdValid,
                    hmdPosition,
                    hmdRotation,
                    leftHand,
                    rightHand
                ));
            }
            catch (Exception exception)
            {
                HandleCaptureFailure("pose_capture_failed", exception);
                throw;
            }
        }

        public bool RecordObjectState(string objectId, string stateJson)
        {
            if (!IsCaptureActive)
            {
                return false;
            }
            try
            {
                return captureWriter.TryWriteObject(new InteractionObjectSample(
                    CurrentPhaseId,
                    captureWriter.NextObjectSequence,
                    NowMonotonic(),
                    DateTimeOffset.UtcNow,
                    Time.frameCount,
                    objectId,
                    stateJson
                ));
            }
            catch (Exception exception)
            {
                HandleCaptureFailure("object_capture_failed", exception);
                throw;
            }
        }

        public void ReportCaptureGap(string reason, double durationSeconds)
        {
            if (!IsCaptureActive)
            {
                return;
            }
            try
            {
                captureWriter.ReportExternalCaptureGap(
                    CurrentPhaseId,
                    NowMonotonic(),
                    DateTimeOffset.UtcNow,
                    Time.frameCount,
                    reason,
                    durationSeconds
                );
            }
            catch (Exception exception)
            {
                HandleCaptureFailure("capture_gap_write_failed", exception);
                throw;
            }
        }

        public void RefreshPendingRuns()
        {
            pendingRuns.Clear();
            try
            {
                pendingRuns.AddRange(
                    InteractionPendingRunDiscovery.Discover(
                        Application.persistentDataPath
                    )
                );
            }
            catch (Exception exception)
            {
                lastError = "Pending Run discovery failed: " + exception.Message;
                Debug.LogWarning(
                    "[InteractionRunController] " + lastError,
                    this
                );
            }
        }

        private void Update()
        {
            ReconcileLifecycleTerminalization();
            if (State == RunState.Scheduled && scheduledStartGate != null &&
                scheduledStartGate.IsDue(NowMonotonic()) &&
                presentationHandshake != null &&
                !presentationHandshake.HasPendingRequest)
            {
                PublishPresentationRequest(
                    presentationHandshake.RequestInitialPlayback()
                );
            }
            if (State == RunState.Running && CurrentPhaseId.HasValue &&
                (presentationHandshake == null ||
                 !presentationHandshake.IsAwaitingNextPhasePlayback))
            {
                double now = NowMonotonic();
                if (stateMachine.TryRecordPhaseTimeout(
                        TimeSpan.FromSeconds(now)))
                {
                    summaryTracker.RecordTimeout(CurrentPhaseId.Value, now);
                    RecordAt(
                        InteractionEventNames.PhaseTimeout,
                        CurrentPhaseId.Value,
                        now,
                        null,
                        null,
                        "{\"threshold_s\":180}"
                    );
                }
            }
        }

        private IEnumerator InitializeConsumedRunRoutine(RunPlan plan)
        {
            try
            {
                yield return null;
                while (captureInitialization != null &&
                    !captureInitialization.IsCompleted)
                {
                    yield return null;
                }
                if (captureInitialization == null)
                {
                    yield break;
                }
                CompleteConsumedRunInitialization(plan);
            }
            finally
            {
                initializationRoutine = null;
            }
        }

        private void CompleteConsumedRunInitialization(RunPlan plan)
        {
            try
            {
                if (!captureInitialization.Succeeded)
                {
                    Exception failure = captureInitialization.Error;
                    throw new IOException(
                        "Consumed Run local initialization failed: " +
                        (failure == null ? "unknown background failure."
                            : failure.Message),
                        failure
                    );
                }
                InteractionCaptureWriter initialized =
                    captureInitialization.GetResult();
                captureWriter = initialized;
                if (lifecycleShutdown.IsShutdownInitiated)
                {
                    StartLifecycleTerminalizationJob(
                        lifecycleShutdown.Reason ?? "lifecycle_shutdown",
                        NowMonotonic(),
                        DateTimeOffset.UtcNow,
                        Time.frameCount
                    );
                    return;
                }

                double now = NowMonotonic();
                DateTimeOffset utcNow = DateTimeOffset.UtcNow;
                RecordAt(
                    InteractionEventNames.RunCreated,
                    null,
                    now,
                    null,
                    null,
                    BuildRunCreatedPayload(plan)
                );
                if (runMode == InteractionRunMode.StandaloneStudy)
                {
                    RecordAt(
                        InteractionEventNames.HostReady,
                        null,
                        now,
                        null,
                        null,
                        BuildHostReadyPayload(lastReadiness)
                    );
                }
                if (!requireHostForStart)
                {
                    ScheduleLocally(utcNow, now);
                }
                else
                {
                    registrationRoutine = StartCoroutine(
                        RegisterCurrentRunRoutine()
                    );
                }
            }
            catch (Exception exception)
            {
                lastError = "Consumed Run initialization failed: " +
                    exception.Message;
                if (State == RunState.AwaitingHost)
                {
                    stateMachine.FaultRun(
                        "manifest_or_capture_initialization_failed"
                    );
                }
                captureWriter?.Dispose();
            }
        }

        private IEnumerator RegisterCurrentRunRoutine()
        {
            try
            {
                if (hostClient == null)
                {
                    lastError = "Interaction Host client is not configured.";
                    yield break;
                }
                int attempts = Mathf.Max(1, automaticRegistrationAttempts);
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    byte[] exactBytes = frozenRegistration.BeginAttempt();
                    InteractionHostResult<InteractionHostRegistration> result = null;
                    yield return hostClient.RegisterRun(
                        frozenRegistration.Plan.RunId,
                        exactBytes,
                        value => result = value
                    );
                    if (result != null && result.Success)
                    {
                        frozenRegistration.RecordResponse(
                            result.ResponseCode,
                            true
                        );
                        stateMachine.HostScheduled(result.Value.StartAtUtc);
                        double observedMonotonic = NowMonotonic();
                        scheduledStartGate = new InteractionScheduledStartGate(
                            result.Value.StartAtUtc,
                            DateTimeOffset.UtcNow,
                            observedMonotonic
                        );
                        hostRegistrationAccepted = true;
                        lastError = string.Empty;
                        yield break;
                    }
                    long status = result == null ? 0L : result.ResponseCode;
                    frozenRegistration.RecordResponse(status, false);
                    if (result != null && result.IsConflict)
                    {
                        InteractionHostResult<InteractionHostRunSnapshot>
                            snapshotResult = null;
                        yield return hostClient.GetRunSnapshot(
                            frozenRegistration.Plan.RunId,
                            value => snapshotResult = value
                        );
                        InteractionHostRunSnapshot snapshot =
                            snapshotResult != null && snapshotResult.Success
                                ? snapshotResult.Value
                                : null;
                        if (snapshot != null && snapshot.IsActive &&
                            InteractionRegistrationRecoveryPolicy.CanRecover(
                                snapshot,
                                frozenRegistration.Plan.BatchId,
                                frozenRegistration.Plan.ParticipantId,
                                frozenRegistration.Plan.RunId
                            ))
                        {
                            frozenRegistration.RecordRecoveredConflict();
                            stateMachine.HostScheduled(snapshot.StartAtUtc);
                            double observedMonotonic = NowMonotonic();
                            scheduledStartGate =
                                new InteractionScheduledStartGate(
                                    snapshot.StartAtUtc,
                                    DateTimeOffset.UtcNow,
                                    observedMonotonic
                                );
                            hostRegistrationAccepted = true;
                            lastError = string.Empty;
                            yield break;
                        }
                        lastError =
                            "Host returned 409, but GET of this exact Run did " +
                            "not return a matching active batch/participant/run snapshot.";
                    }
                    else
                    {
                    lastError = result == null
                        ? "Host registration produced no result."
                        : "Host registration failed: " + result.Error;
                    }
                    if (attempt < attempts)
                    {
                        yield return new WaitForSecondsRealtime(
                            registrationRetryDelaySeconds
                        );
                    }
                }
            }
            finally
            {
                registrationRoutine = null;
            }
        }

        private void ScheduleLocally(
            DateTimeOffset utcNow,
            double monotonicNow)
        {
            stateMachine.HostScheduled(utcNow);
            scheduledStartGate = new InteractionScheduledStartGate(
                utcNow,
                utcNow,
                monotonicNow
            );
        }

        private void FinishCurrentPhase(bool stuck)
        {
            if (phaseCheckpointRoutine != null || terminalizationRoutine != null)
            {
                throw new InvalidOperationException(
                    "A phase checkpoint or terminal seal is already active."
                );
            }
            int phaseId = RequireCurrentPhaseId();
            double now = NowMonotonic();
            InteractionPresentationRequest nextRequest = null;
            if (phaseId == PhaseSentenceRanges.PhaseCount)
            {
                presentationHandshake.CompleteFinalPhase(stuck, now);
            }
            else
            {
                // Validate the W1 phase and create a pending request without
                // advancing W1. The actual next clip start ACK supplies the
                // timeout origin to CompletePhase/GiveUpPhase.
                nextRequest = presentationHandshake.RequestNextPhasePlayback(
                    stuck,
                    now
                );
            }
            try
            {
                CloseAssistanceExposures(phaseId, now);
                summaryTracker.FinishPhase(
                    phaseId,
                    completed: !stuck,
                    stuck: stuck,
                    monotonicTimeSeconds: now
                );
                RecordAt(
                    stuck
                        ? InteractionEventNames.PhaseStuck
                        : InteractionEventNames.PhaseCompleted,
                    phaseId,
                    now,
                    null,
                    null,
                    "{}"
                );
                terminalSealArbiter.BeginCheckpoint();
                InteractionBackgroundOperation<bool> checkpoint =
                    captureWriter.BeginPhaseCheckpoint();
                activePhaseCheckpoint = checkpoint;
                phaseCheckpointRoutine = StartCoroutine(
                    AwaitPhaseCheckpointRoutine(
                        checkpoint,
                        nextRequest,
                        now
                    )
                );
            }
            catch
            {
                presentationHandshake.CancelPending();
                if (State == RunState.Running || State == RunState.Completing)
                {
                    stateMachine.FaultRun("phase_capture_failed");
                }
                captureWriter.Dispose();
                throw;
            }

        }

        private IEnumerator AwaitPhaseCheckpointRoutine(
            InteractionBackgroundOperation<bool> checkpoint,
            InteractionPresentationRequest nextRequest,
            double phaseEndedMonotonic)
        {
            try
            {
                yield return null;
                while (!checkpoint.IsCompleted)
                {
                    yield return null;
                }
                if (!checkpoint.Succeeded)
                {
                    activePhaseCheckpoint = null;
                    terminalSealArbiter.CancelCheckpoint();
                    Exception failure = checkpoint.Error;
                    lastError = "Phase checkpoint failed: " +
                        (failure == null ? "unknown background failure."
                            : failure.Message);
                    presentationHandshake?.CancelPending();
                    if (State == RunState.Running ||
                        State == RunState.Completing)
                    {
                        stateMachine.FaultRun("phase_capture_checkpoint_failed");
                    }
                    captureWriter.Dispose();
                    yield break;
                }
                activePhaseCheckpoint = null;
                if (terminalSealArbiter.CompleteCheckpoint() !=
                    InteractionCheckpointResolution.Continue)
                {
                    throw new InvalidOperationException(
                        "The normal checkpoint waiter observed an Abort reservation."
                    );
                }
                if (nextRequest != null)
                {
                    PublishPresentationRequest(nextRequest);
                    yield break;
                }
                if (State != RunState.Completing)
                {
                    throw new InvalidOperationException(
                        "Unexpected Run state after phase checkpoint: " +
                        State + "."
                    );
                }
                CompleteRun(phaseEndedMonotonic);
            }
            finally
            {
                phaseCheckpointRoutine = null;
            }
        }

        private void CompleteRun(double now)
        {
            if (!TryBeginCompletedSeal(now))
            {
                return;
            }
            terminalizationRoutine = StartCoroutine(
                AwaitTerminalSealRoutine(
                    InteractionCaptureTerminalKind.Completed,
                    captureTerminalShouldUpload,
                    null,
                    captureTerminalUtc
                )
            );
        }

        private bool TryBeginCompletedSeal(double now)
        {
            if (!captureWriter.CanSealCompleted(out string completenessReason))
            {
                lastError = completenessReason;
                TryAbortRunInternal(
                    "completed_capture_missing_required_streams",
                    allowHostUpload: true,
                    out _
                );
                return false;
            }
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            bool willUpload = requireHostForStart && hostRegistrationAccepted;
            try
            {
                RecordAt(
                    InteractionEventNames.RunCompleted,
                    null,
                    now,
                    null,
                    null,
                    "{}"
                );
                if (willUpload)
                {
                    RecordAt(
                        InteractionEventNames.UploadStarted,
                        null,
                        now,
                        null,
                        null,
                        "{\"terminal_status\":\"completed\"}"
                    );
                }
                captureTerminalization = captureWriter.BeginSeal(
                    InteractionCaptureTerminalKind.Completed,
                    completeness => summaryTracker.SealCompleted(
                        now,
                        utcNow,
                        completeness
                    )
                );
                terminalSealArbiter.MarkCompletedSealQueued();
                captureTerminalKind = InteractionCaptureTerminalKind.Completed;
                captureTerminalAbortReason = null;
                captureTerminalUtc = utcNow;
                captureTerminalShouldUpload = willUpload;
                captureTerminalizationReconciled = false;
            }
            catch (Exception exception)
            {
                lastError = "Completion sealing failed: " + exception.Message;
                if (State == RunState.Completing)
                {
                    stateMachine.FaultRun("complete_capture_seal_failed");
                }
                captureWriter.Dispose();
                throw;
            }
            return true;
        }

        private IEnumerator AwaitTerminalSealRoutine(
            InteractionCaptureTerminalKind terminalKind,
            bool willUpload,
            string abortReason,
            DateTimeOffset terminalUtc)
        {
            try
            {
                yield return null;
                while (captureTerminalization != null &&
                    !captureTerminalization.IsCompleted)
                {
                    yield return null;
                }
                CompleteTerminalSeal(
                    terminalKind,
                    willUpload,
                    abortReason,
                    terminalUtc
                );
            }
            finally
            {
                terminalizationRoutine = null;
            }
        }

        private void CompleteTerminalSeal(
            InteractionCaptureTerminalKind terminalKind,
            bool willUpload,
            string abortReason,
            DateTimeOffset terminalUtc)
        {
            if (captureTerminalization == null ||
                !captureTerminalization.IsCompleted)
            {
                throw new InvalidOperationException(
                    "Capture terminalization has not completed."
                );
            }
            if (!captureTerminalization.Succeeded)
            {
                Exception failure = captureTerminalization.Error;
                lastError = "Capture terminalization failed: " +
                    (failure == null ? "unknown background failure."
                        : failure.Message);
                if (State == RunState.Completing || State == RunState.Aborting)
                {
                    stateMachine.FaultRun(
                        terminalKind == InteractionCaptureTerminalKind.Completed
                            ? "complete_capture_seal_failed"
                            : "abort_capture_seal_failed"
                    );
                }
                captureWriter?.Dispose();
                captureTerminalizationReconciled = true;
                terminalSealArbiter.MarkTerminal();
                return;
            }
            if (terminalKind == InteractionCaptureTerminalKind.Completed)
            {
                stateMachine.MarkRunCompleted();
            }
            else
            {
                stateMachine.MarkRunAborted();
            }
            lastError = string.Empty;
            if (willUpload && !lifecycleShutdown.IsShutdownInitiated)
            {
                BeginTerminalUpload(
                    terminalKind == InteractionCaptureTerminalKind.Aborted,
                    abortReason,
                    terminalUtc
                );
            }
            captureTerminalizationReconciled = true;
            terminalSealArbiter.MarkTerminal();
        }

        private void BeginTerminalUpload(
            bool aborted,
            string abortReason,
            DateTimeOffset terminalUtc)
        {
            if (uploadRoutine != null || hostClient == null ||
                captureWriter == null || !captureWriter.IsSealed)
            {
                return;
            }
            uploadStateMachine = new InteractionUploadStateMachine(
                captureWriter.RunId,
                captureWriter.RunDirectory
            );
            uploadRoutine = StartCoroutine(UploadTerminalRunRoutine(
                aborted,
                abortReason,
                terminalUtc
            ));
        }

        private IEnumerator UploadTerminalRunRoutine(
            bool aborted,
            string abortReason,
            DateTimeOffset terminalUtc)
        {
            try
            {
                uploadStateMachine.Begin(DateTimeOffset.UtcNow);
                yield return AwaitUploadStatePersistence(
                    uploadStateMachine,
                    "upload_started"
                );
                InteractionHostResult<bool> terminalResult = null;
                if (aborted)
                {
                    yield return hostClient.NotifyAbort(
                        captureWriter.RunId,
                        terminalUtc,
                        abortReason,
                        value => terminalResult = value
                    );
                }
                else
                {
                    yield return hostClient.NotifyComplete(
                        captureWriter.RunId,
                        terminalUtc,
                        value => terminalResult = value
                    );
                }
                if (terminalResult == null || !terminalResult.Success)
                {
                    string terminalError = terminalResult == null
                        ? "Host terminal notification produced no result."
                        : terminalResult.Error;
                    uploadStateMachine.Defer(terminalError);
                    yield return AwaitUploadStatePersistence(
                        uploadStateMachine,
                        "terminal_notification_deferred"
                    );
                    lastError = terminalError;
                    yield break;
                }

                if (!TryBeginArtifactFreeze(
                        captureWriter.RunDirectory,
                        out InteractionArtifactOperation<
                            InteractionFrozenArtifactSet> freezeArtifacts))
                {
                    uploadStateMachine.Defer(
                        "Artifact snapshot is already active for this Run."
                    );
                    yield return AwaitUploadStatePersistence(
                        uploadStateMachine,
                        "artifact_snapshot_busy"
                    );
                    lastError = uploadStateMachine.LastError;
                    yield break;
                }
                while (!freezeArtifacts.IsCompleted)
                {
                    yield return null;
                }
                if (!freezeArtifacts.Succeeded)
                {
                    Exception exception = freezeArtifacts.Error;
                    uploadStateMachine.Defer(
                        "Artifact snapshot failed: " +
                        (exception == null ? "unknown background failure."
                            : exception.Message)
                    );
                    yield return AwaitUploadStatePersistence(
                        uploadStateMachine,
                        "artifact_snapshot_deferred"
                    );
                    lastError = uploadStateMachine.LastError;
                    yield break;
                }
                InteractionFrozenArtifactSet artifacts =
                    freezeArtifacts.GetResult();

                foreach (string type in artifacts.ArtifactTypes)
                {
                    InteractionHostResult<bool> putResult = null;
                    InteractionFrozenArtifact artifact = artifacts.For(type);
                    yield return hostClient.PutArtifact(
                        captureWriter.RunId,
                        artifact,
                        value => putResult = value
                    );
                    uploadStateMachine.RecordPutResponse(
                        type,
                        putResult == null ? 0L : putResult.ResponseCode
                    );
                    yield return AwaitUploadStatePersistence(
                        uploadStateMachine,
                        "artifact_put_response"
                    );
                }
                uploadStateMachine.AwaitAck();
                yield return AwaitUploadStatePersistence(
                    uploadStateMachine,
                    "awaiting_ack"
                );

                for (int attempt = 1;
                    attempt <= Mathf.Max(1, acknowledgementPollAttempts);
                    attempt++)
                {
                    InteractionHostResult<InteractionHostArtifactAck> ackResult = null;
                    yield return hostClient.GetAck(
                        captureWriter.RunId,
                        value => ackResult = value
                    );
                    if (ackResult != null && ackResult.Success)
                    {
                        bool complete = uploadStateMachine.ApplyAck(
                            ackResult.Value,
                            DateTimeOffset.UtcNow
                        );
                        yield return AwaitUploadStatePersistence(
                            uploadStateMachine,
                            "ack_response"
                        );
                        if (complete)
                        {
                            lastError = string.Empty;
                            yield break;
                        }
                    }
                    else if (ackResult != null)
                    {
                        lastError = "Host ACK failed: " + ackResult.Error;
                    }
                    if (attempt < acknowledgementPollAttempts)
                    {
                        yield return new WaitForSecondsRealtime(
                            acknowledgementPollDelaySeconds
                        );
                    }
                }
                lastError =
                    "Host ACK is incomplete; Quest local Run remains retained.";
            }
            finally
            {
                uploadRoutine = null;
                RefreshPendingRuns();
            }
        }

        private IEnumerator UploadDiscoveredRunRoutine(
            InteractionPendingRun pending)
        {
            try
            {
                byte[] manifestBytes = File.ReadAllBytes(pending.ManifestPath);
                InteractionHostResult<InteractionHostRegistration> registration = null;
                yield return hostClient.RegisterRun(
                    pending.RunId,
                    manifestBytes,
                    value => registration = value
                );
                if (registration == null)
                {
                    lastError = "Pending registration produced no result.";
                    yield break;
                }
                if (!registration.Success)
                {
                    if (!registration.IsConflict)
                    {
                        lastError = "Pending registration failed: " +
                            registration.Error;
                        yield break;
                    }
                    InteractionHostResult<InteractionHostRunSnapshot>
                        snapshotResult = null;
                    yield return hostClient.GetRunSnapshot(
                        pending.RunId,
                        value => snapshotResult = value
                    );
                    InteractionHostRunSnapshot snapshot =
                        snapshotResult != null && snapshotResult.Success
                            ? snapshotResult.Value
                            : null;
                    if (!InteractionRegistrationRecoveryPolicy.CanRecover(
                            snapshot,
                            pending.BatchId,
                            pending.ParticipantId,
                            pending.RunId
                        ))
                    {
                        lastError =
                            "Pending registration 409 was not recoverable: " +
                            "GET did not return the same batch/participant/run.";
                        yield break;
                    }
                }

                string summaryPath = pending.ArtifactPath(
                    InteractionArtifactTypes.Summary
                );
                IDictionary<string, object> summary = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(summaryPath)
                );
                string status = InteractionJson.RequireString(summary, "status");
                string abortReason = InteractionJson.OptionalString(
                    summary,
                    "abort_reason"
                );
                InteractionHostResult<bool> terminal = null;
                if (string.Equals(status, "aborted", StringComparison.Ordinal))
                {
                    yield return hostClient.NotifyAbort(
                        pending.RunId,
                        DateTimeOffset.UtcNow,
                        abortReason ?? "recovered_after_restart",
                        value => terminal = value
                    );
                }
                else if (string.Equals(status, "completed", StringComparison.Ordinal))
                {
                    yield return hostClient.NotifyComplete(
                        pending.RunId,
                        DateTimeOffset.UtcNow,
                        value => terminal = value
                    );
                }
                else
                {
                    lastError = "Pending summary has unknown terminal status.";
                    yield break;
                }
                if (terminal == null || !terminal.Success)
                {
                    lastError = terminal == null
                        ? "Pending terminal notification produced no result."
                        : terminal.Error;
                    yield break;
                }

                var tracker = new InteractionUploadStateMachine(
                    pending.RunId,
                    pending.DirectoryPath
                );
                tracker.Begin(DateTimeOffset.UtcNow);
                yield return AwaitUploadStatePersistence(
                    tracker,
                    "recovered_upload_started"
                );
                if (!TryBeginArtifactFreeze(
                        pending.DirectoryPath,
                        out InteractionArtifactOperation<
                            InteractionFrozenArtifactSet> freezeArtifacts))
                {
                    tracker.Defer(
                        "Recovered artifact snapshot is already active for this Run."
                    );
                    yield return AwaitUploadStatePersistence(
                        tracker,
                        "recovered_snapshot_busy"
                    );
                    lastError = tracker.LastError;
                    yield break;
                }
                while (!freezeArtifacts.IsCompleted)
                {
                    yield return null;
                }
                if (!freezeArtifacts.Succeeded)
                {
                    tracker.Defer(
                        "Recovered artifact snapshot failed: " +
                        (freezeArtifacts.Error == null
                            ? "unknown background failure."
                            : freezeArtifacts.Error.Message)
                    );
                    yield return AwaitUploadStatePersistence(
                        tracker,
                        "recovered_snapshot_deferred"
                    );
                    lastError = tracker.LastError;
                    yield break;
                }
                InteractionFrozenArtifactSet artifacts =
                    freezeArtifacts.GetResult();
                foreach (string type in artifacts.ArtifactTypes)
                {
                    InteractionHostResult<bool> put = null;
                    yield return hostClient.PutArtifact(
                        pending.RunId,
                        artifacts.For(type),
                        value => put = value
                    );
                    tracker.RecordPutResponse(
                        type,
                        put == null ? 0L : put.ResponseCode
                    );
                    yield return AwaitUploadStatePersistence(
                        tracker,
                        "recovered_put_response"
                    );
                }
                tracker.AwaitAck();
                yield return AwaitUploadStatePersistence(
                    tracker,
                    "recovered_awaiting_ack"
                );
                for (int attempt = 1;
                    attempt <= Mathf.Max(1, acknowledgementPollAttempts);
                    attempt++)
                {
                    InteractionHostResult<InteractionHostArtifactAck> ack = null;
                    yield return hostClient.GetAck(
                        pending.RunId,
                        value => ack = value
                    );
                    if (ack != null && ack.Success)
                    {
                        bool acknowledged = tracker.ApplyAck(
                                ack.Value,
                                DateTimeOffset.UtcNow
                            );
                        yield return AwaitUploadStatePersistence(
                            tracker,
                            "recovered_ack_response"
                        );
                        if (acknowledged)
                        {
                            lastError = string.Empty;
                            yield break;
                        }
                    }
                    if (attempt < acknowledgementPollAttempts)
                    {
                        yield return new WaitForSecondsRealtime(
                            acknowledgementPollDelaySeconds
                        );
                    }
                }
                lastError =
                    "Recovered Run ACK is incomplete; local data remains retained.";
            }
            finally
            {
                pendingUploadTarget = null;
                uploadRoutine = null;
                RefreshPendingRuns();
            }
        }

        private IEnumerator AwaitUploadStatePersistence(
            InteractionUploadStateMachine machine,
            string operation)
        {
            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            InteractionBackgroundOperation<bool> persistence =
                machine.PendingPersistence;
            if (persistence == null)
            {
                throw new InvalidOperationException(
                    "Upload state did not schedule persistence for " +
                    operation + "."
                );
            }
            while (!persistence.IsCompleted)
            {
                yield return null;
            }
            if (!persistence.Succeeded)
            {
                lastError = "Upload state persistence failed during " +
                    operation + ": " +
                    (persistence.Error == null
                        ? "unknown background failure."
                        : persistence.Error.Message);
                throw new IOException(lastError, persistence.Error);
            }
        }

        private void CloseAssistanceExposures(double now)
        {
            CloseAssistanceExposures(CurrentPhaseId, now);
        }

        private void CloseAssistanceExposures(int phaseId, double now)
        {
            CloseAssistanceExposures((int?)phaseId, now);
        }

        private void CloseAssistanceExposures(int? phaseId, double now)
        {
            if (!phaseId.HasValue || summaryTracker == null)
            {
                textExposureActive = false;
                pointingExposureActive = false;
                pointingTargetId = null;
                return;
            }
            if (textExposureActive)
            {
                summaryTracker.EndExposure(
                    phaseId.Value,
                    InteractionExposureKind.Text,
                    now
                );
                textExposureActive = false;
                RecordAt(
                    InteractionEventNames.BubbleHidden,
                    phaseId.Value,
                    now,
                    null,
                    null,
                    "{\"reason\":\"phase_exit\"}"
                );
            }
            if (pointingExposureActive)
            {
                summaryTracker.EndExposure(
                    phaseId.Value,
                    InteractionExposureKind.Pointing,
                    now
                );
                RecordAt(
                    InteractionEventNames.PointingHitEnded,
                    phaseId.Value,
                    now,
                    null,
                    pointingTargetId,
                    "{\"reason\":\"phase_exit\"}"
                );
                pointingExposureActive = false;
                pointingTargetId = null;
            }
        }

        private void PublishPresentationRequest(
            InteractionPresentationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            Delegate[] listeners = PresentationRequested?.GetInvocationList();
            if (listeners == null || listeners.Length == 0)
            {
                lastError =
                    "Presentation is pending; W5/W8 must start the requested " +
                    "clip and ACK its actual first frame.";
                return;
            }
            for (int index = 0; index < listeners.Length; index++)
            {
                try
                {
                    ((Action<InteractionPresentationRequest>)listeners[index])(
                        request
                    );
                }
                catch (Exception exception)
                {
                    lastError =
                        "Presentation request listener failed: " +
                        exception.Message;
                    Debug.LogError(
                        "[InteractionRunController] " + lastError,
                        this
                    );
                }
            }
        }

        private void RefreshHeartbeatConfiguration()
        {
            if (hostClient == null)
            {
                return;
            }
            if (stateMachine == null || State == RunState.PreStart)
            {
                hostClient.RestoreRequestsForPreStart();
            }
            hostClient.ConfigureQuestHeartbeat(requireHostForStart);
        }

        private bool HasUnsealedPendingRun()
        {
            for (int index = 0; index < pendingRuns.Count; index++)
            {
                if (pendingRuns[index].NeedsRecovery)
                {
                    return true;
                }
            }
            return false;
        }

        private int RequireCurrentPhaseId()
        {
            if (presentationHandshake != null &&
                presentationHandshake.HasPendingRequest)
            {
                throw new InvalidOperationException(
                    "Interaction is paused until the pending presentation " +
                    "reports its actual playback start."
                );
            }
            if (!CurrentPhaseId.HasValue)
            {
                throw new InvalidOperationException("No phase is active.");
            }
            return CurrentPhaseId.Value;
        }

        private void HandleCaptureFailure(string faultReason, Exception exception)
        {
            lastError = faultReason + ": " + exception.Message;
            if (State == RunState.AwaitingHost || State == RunState.Scheduled ||
                State == RunState.Running || State == RunState.Completing ||
                State == RunState.Aborting)
            {
                stateMachine.FaultRun(faultReason);
            }
            captureWriter?.Dispose();
        }

        private void RecordNow(
            string eventType,
            int? phaseId,
            string actorId,
            string targetId,
            string payloadJson)
        {
            RecordAt(
                eventType,
                phaseId,
                NowMonotonic(),
                actorId,
                targetId,
                payloadJson
            );
        }

        private void RecordAt(
            string eventType,
            int? phaseId,
            double monotonicTime,
            string actorId,
            string targetId,
            string payloadJson)
        {
            if (captureWriter == null)
            {
                throw new InvalidOperationException(
                    "Capture writer is not initialized."
                );
            }
            try
            {
                captureWriter.RecordEvent(
                    eventType,
                    phaseId,
                    monotonicTime,
                    DateTimeOffset.UtcNow,
                    Time.frameCount,
                    actorId,
                    targetId,
                    payloadJson
                );
            }
            catch
            {
                if (State == RunState.AwaitingHost ||
                    State == RunState.Scheduled ||
                    State == RunState.Running ||
                    State == RunState.Completing ||
                    State == RunState.Aborting)
                {
                    stateMachine.FaultRun("event_capture_failed");
                }
                captureWriter.Dispose();
                throw;
            }
        }

        private static string BuildRunCreatedPayload(RunPlan plan)
        {
            return "{\"assistance_condition\":" + Quote(
                plan.AssistanceCondition.ToString()
            ) + ",\"block_index\":" +
                plan.ConditionAssignment.BlockIndex.ToString(
                    CultureInfo.InvariantCulture
                ) + ",\"slot_index\":" +
                plan.ConditionAssignment.SlotIndex.ToString(
                    CultureInfo.InvariantCulture
                ) + "}";
        }

        private static string BuildHostReadyPayload(
            InteractionHostReadiness readiness)
        {
            return "{\"backend_ready\":" + Boolean(readiness.BackendReady) +
                ",\"storage_ready\":" + Boolean(readiness.StorageReady) +
                ",\"paired_quest_ready\":" +
                Boolean(readiness.PairedQuestReady) +
                ",\"quest_fresh\":" +
                Boolean(readiness.QuestHeartbeatFresh) +
                ",\"browser_camera_ready\":" +
                Boolean(readiness.BrowserCameraReady) +
                ",\"browser_camera_fresh\":" +
                Boolean(readiness.BrowserCameraFresh) +
                ",\"participant_ready\":" +
                Boolean(readiness.ParticipantReady) +
                ",\"participant_fresh\":" +
                Boolean(readiness.ParticipantFresh) +
                ",\"participant_id\":" +
                Quote(readiness.ParticipantId) +
                ",\"quest_device_id\":" +
                Quote(readiness.QuestDeviceId) + "}";
        }

        private static string BuildReasonPayload(string reason)
        {
            return "{\"reason\":" + Quote(reason.Trim()) + "}";
        }

        private static string Quote(string value)
        {
            var builder = new StringBuilder();
            InteractionJson.AppendQuoted(builder, value);
            return builder.ToString();
        }

        private static string Boolean(bool value)
        {
            return value ? "true" : "false";
        }

        private static int CreateRandomSeed()
        {
            byte[] bytes = new byte[4];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return BitConverter.ToInt32(bytes, 0);
        }

        private static double NowMonotonic()
        {
            return Time.realtimeSinceStartupAsDouble;
        }

        private bool TryBeginArtifactFreeze(
            string runDirectory,
            out InteractionArtifactOperation<InteractionFrozenArtifactSet>
                operation)
        {
            string canonicalDirectory = Path.GetFullPath(
                runDirectory ?? throw new ArgumentNullException(
                    nameof(runDirectory)
                )
            );
            string key = InteractionArtifactOperationKeys.ForFreeze(
                canonicalDirectory
            );
            IInteractionArtifactReadObserver observerSnapshot =
                artifactReadObserver;
            return artifactOperations.TryStart(
                key,
                cancellation => InteractionFrozenArtifactSet.ReadOnceOnWorker(
                    canonicalDirectory,
                    cancellation,
                    observerSnapshot
                ),
                out operation
            );
        }

        private static string ResolveQuestDeviceId()
        {
            string value = PlayerPrefs.GetString(
                DeviceIdPlayerPrefsKey,
                string.Empty
            );
            if (string.IsNullOrWhiteSpace(value))
            {
                value = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdPlayerPrefsKey, value);
                PlayerPrefs.Save();
            }
            return InteractionStoragePaths.ValidateSegment(
                value,
                "questDeviceId"
            );
        }

        private void BeginLifecycleShutdown(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "Lifecycle shutdown reason is required.",
                    nameof(reason)
                );
            }
            bool first = lifecycleShutdown.TryBegin(reason);
            artifactOperations.CancelAll();
            hostClient?.DisableQuestHeartbeat();
            hostClient?.CancelActiveRequests();
            StopAllCoroutines();
            initializationRoutine = null;
            registrationRoutine = null;
            phaseCheckpointRoutine = null;
            terminalizationRoutine = null;
            uploadRoutine = null;
            pendingUploadTarget = null;
            if (captureSampler != null)
            {
                captureSampler.enabled = false;
            }
            if (!first || stateMachine == null)
            {
                return;
            }

            // A terminal seal already queued behind prior checkpoints is left
            // to finish locally, but shutdown permanently suppresses upload.
            if (captureTerminalization != null)
            {
                captureTerminalShouldUpload = false;
                return;
            }
            if (!InteractionLifecycleTerminationPolicy.RequiresLocalAbort(State) &&
                State != RunState.Aborting)
            {
                if (captureWriter != null && !captureWriter.IsSealed)
                {
                    captureWriter.Dispose();
                }
                return;
            }

            double now = NowMonotonic();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            int frame = Time.frameCount;
            int? abortPhaseId = CurrentPhaseId;
            presentationHandshake?.CancelPending();
            if (State != RunState.Aborting)
            {
                InteractionAbortRequestDisposition disposition =
                    terminalSealArbiter.TryRequestAbort(
                        out string arbitrationError
                    );
                if (disposition == InteractionAbortRequestDisposition.Rejected)
                {
                    lastError = arbitrationError;
                    return;
                }
                stateMachine.AbortRun(reason);
            }
            if (captureWriter != null)
            {
                try
                {
                    CloseAssistanceExposures(abortPhaseId, now);
                }
                catch (Exception exception)
                {
                    lastError =
                        "Lifecycle exposure close retained partial data: " +
                        exception.Message;
                }
            }
            StartLifecycleTerminalizationJob(reason, now, utcNow, frame);
        }

        private void StartLifecycleTerminalizationJob(
            string reason,
            double monotonicNow,
            DateTimeOffset utcNow,
            int frame)
        {
            if (lifecycleTerminalizationJob != null)
            {
                return;
            }
            if (captureWriter == null && captureInitialization == null)
            {
                lastError =
                    "Lifecycle terminalization retained partial capture: " +
                    "writer initialization is unavailable.";
                if (State == RunState.Aborting)
                {
                    stateMachine.FaultRun(
                        "lifecycle_capture_initialization_unavailable"
                    );
                }
                terminalSealArbiter.MarkTerminal();
                return;
            }
            try
            {
                InteractionSummaryTracker detachedSummary = summaryTracker == null
                    ? throw new InvalidOperationException(
                        "Lifecycle terminalization requires a summary snapshot."
                    )
                    : summaryTracker.CreateDetachedCopy();
                lifecycleTerminalizationJob =
                    InteractionLifecycleTerminalizationJob.Start(
                        captureWriter,
                        captureInitialization,
                        detachedSummary,
                        reason,
                        monotonicNow,
                        utcNow,
                        frame,
                        lifecycleWorkQueue
                    );
            }
            catch (Exception exception)
            {
                captureWriter?.Dispose();
                lastError =
                    "Lifecycle terminalization retained partial capture: " +
                    exception.Message;
                if (State == RunState.Aborting)
                {
                    stateMachine.FaultRun(
                        "lifecycle_capture_terminalization_failed"
                    );
                }
                terminalSealArbiter.MarkTerminal();
            }
            ReconcileLifecycleTerminalization();
        }

        private void ReconcileLifecycleTerminalization()
        {
            InteractionLifecycleTerminalizationJob job =
                lifecycleTerminalizationJob;
            if (job != null && job.IsCompleted &&
                job.TryConsume(
                    out InteractionLifecycleTerminalizationResult result))
            {
                lifecycleTerminalizationJob = null;
#if UNITY_EDITOR
                detachedInitializationOwnerForTests =
                    result.DetachedInitializationOwner;
#endif
                if (result.Writer != null)
                {
                    captureWriter = result.Writer;
                }
                if (result.Succeeded)
                {
                    if (State == RunState.Aborting)
                    {
                        stateMachine.MarkRunAborted();
                    }
                    lastError = string.Empty;
                }
                else
                {
                    result.Writer?.Dispose();
                    lastError =
                        "Lifecycle terminalization retained partial capture: " +
                        (result.Error == null
                            ? "unknown background failure."
                            : result.Error.Message);
                    if (State == RunState.Aborting)
                    {
                        stateMachine.FaultRun(
                            "lifecycle_capture_terminalization_failed"
                        );
                    }
                }
                if (terminalSealArbiter.State !=
                    InteractionTerminalSealArbitrationState.Terminal)
                {
                    terminalSealArbiter.MarkTerminal();
                }
                RefreshPendingRuns();
            }

            if (!lifecycleShutdown.IsShutdownInitiated ||
                captureTerminalization == null ||
                !captureTerminalization.IsCompleted ||
                captureTerminalizationReconciled ||
                !captureTerminalKind.HasValue)
            {
                return;
            }
            CompleteTerminalSeal(
                captureTerminalKind.Value,
                false,
                captureTerminalAbortReason,
                captureTerminalUtc
            );
            RefreshPendingRuns();
        }

        private void TryRestorePreStartLifecycle()
        {
            if (stateMachine == null ||
                !InteractionHeartbeatLifecyclePolicy
                    .ShouldRestoreAfterResume(State))
            {
                return;
            }
            lifecycleShutdown.Reset();
            if (captureSampler != null)
            {
                captureSampler.enabled = true;
            }
            RefreshHeartbeatConfiguration();
        }

        private void OnEnable()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            TryRestorePreStartLifecycle();
            ReconcileLifecycleTerminalization();
        }

        private enum ControllerLifecycleSignal
        {
            Disabled,
            ApplicationPaused,
            ApplicationQuit,
            Destroyed
        }

        private void ProcessLifecycleSignal(ControllerLifecycleSignal signal)
        {
            switch (signal)
            {
                case ControllerLifecycleSignal.Disabled:
                    BeginLifecycleShutdown("component_disabled");
                    return;
                case ControllerLifecycleSignal.ApplicationPaused:
                    BeginLifecycleShutdown("application_pause");
                    return;
                case ControllerLifecycleSignal.ApplicationQuit:
                    BeginLifecycleShutdown("application_quit");
                    return;
                case ControllerLifecycleSignal.Destroyed:
                    BeginLifecycleShutdown("controller_destroyed");
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(signal));
            }
        }

        private void OnDisable()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            ProcessLifecycleSignal(ControllerLifecycleSignal.Disabled);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            if (paused)
            {
                ProcessLifecycleSignal(
                    ControllerLifecycleSignal.ApplicationPaused
                );
            }
            else
            {
                TryRestorePreStartLifecycle();
            }
        }

        private void OnApplicationQuit()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            ProcessLifecycleSignal(ControllerLifecycleSignal.ApplicationQuit);
        }

        private void OnDestroy()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            ProcessLifecycleSignal(ControllerLifecycleSignal.Destroyed);
        }

        private bool ShouldProcessUnityLifecycle()
        {
            if (Application.isPlaying)
            {
                return true;
            }
#if UNITY_EDITOR
            return editorLifecycleTestsArmed;
#else
            return false;
#endif
        }

        private void OnValidate()
        {
            readinessMaximumAgeSeconds = Mathf.Max(
                0.5f,
                readinessMaximumAgeSeconds
            );
            automaticRegistrationAttempts = Mathf.Clamp(
                automaticRegistrationAttempts,
                1,
                10
            );
            registrationRetryDelaySeconds = Mathf.Max(
                0.25f,
                registrationRetryDelaySeconds
            );
            acknowledgementPollAttempts = Mathf.Clamp(
                acknowledgementPollAttempts,
                1,
                20
            );
            acknowledgementPollDelaySeconds = Mathf.Max(
                0.25f,
                acknowledgementPollDelaySeconds
            );
            captureQueueCapacity = Mathf.Max(16, captureQueueCapacity);
            captureGapThresholdSeconds = Mathf.Max(
                0.05f,
                captureGapThresholdSeconds
            );
        }
    }
}
