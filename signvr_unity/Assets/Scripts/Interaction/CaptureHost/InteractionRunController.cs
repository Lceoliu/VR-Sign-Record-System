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
    public sealed class InteractionRunController : MonoBehaviour
    {
        private const string DeviceIdPlayerPrefsKey = "SignVR.DeviceId";

        [Header("Mode")]
        [SerializeField]
        private InteractionRunMode runMode = InteractionRunMode.Study;

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
        private Coroutine registrationRoutine;
        private Coroutine uploadRoutine;
        private bool hostRegistrationAccepted;
        private bool textExposureActive;
        private bool pointingExposureActive;
        private string pointingTargetId;
        private string lastError = string.Empty;

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
        public bool RegistrationInFlight => registrationRoutine != null;
        public bool UploadInFlight => uploadRoutine != null;
        public bool EligibleForLocalCleanup => uploadStateMachine != null &&
            uploadStateMachine.EligibleForLocalCleanup;
        public string LastError => lastError;
        public string AppSessionId => appSessionId;
        public string QuestDeviceId => questDeviceId;
        public InteractionRunMode RunMode => runMode;
        public bool DebugOverridesActive => debugOverridesActive;
        public bool RequireHostForStart => requireHostForStart;
        public InteractionHostClient HostClient => hostClient;
        public InteractionCaptureSampler CaptureSampler => captureSampler;
        public InteractionPresentationRequest PendingPresentationRequest =>
            presentationHandshake?.PendingRequest;
        public IReadOnlyList<InteractionPendingRun> PendingRuns => pendingRuns;

        public event Action<InteractionPresentationRequest>
            PresentationRequested;

        private void Awake()
        {
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
            if (hostClient == null)
            {
                lastError = "Interaction Host client is not configured.";
                return;
            }
            long generation = readinessResponseGate.Issue();
            StartCoroutine(hostClient.GetReadiness(result =>
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
            }));
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
            if (runMode == InteractionRunMode.Study &&
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

            if (runMode == InteractionRunMode.Study)
            {
                if (string.Equals(
                        participantId,
                        "UNCONFIGURED",
                        StringComparison.Ordinal))
                {
                    reason = "Study participant_id must be explicitly configured.";
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
                    captureWriter = InteractionCaptureWriter.CreateNew(
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
                if (runMode == InteractionRunMode.Study)
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

                hostRegistrationAccepted = false;
                lastError = string.Empty;
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

        public int RecoverAllPartialRunsAsAborted(
            string reason = "recovered_after_process_restart")
        {
            if (State != RunState.PreStart || registrationRoutine != null ||
                uploadRoutine != null)
            {
                throw new InvalidOperationException(
                    "Partial recovery requires an idle PreStart controller."
                );
            }
            int count = InteractionPartialRunRecovery.TerminalizeAllAborted(
                Application.persistentDataPath,
                reason,
                DateTimeOffset.UtcNow,
                NowMonotonic(),
                Time.frameCount
            );
            RefreshPendingRuns();
            return count;
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
            RecordAt(
                InteractionEventNames.TaskProgressReset,
                phaseId,
                now,
                actorId,
                targetId,
                "{\"reason\":\"interaction_error\"}"
            );
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
            AbortRunInternal(reason, allowHostUpload: true);
        }

        private void AbortRunInternal(
            string reason,
            bool allowHostUpload)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "Abort reason is required.",
                    nameof(reason)
                );
            }
            if (State != RunState.AwaitingHost && State != RunState.Scheduled &&
                State != RunState.Running && State != RunState.Completing)
            {
                throw new InvalidOperationException(
                    "Run cannot abort from " + State + "."
                );
            }
            double now = NowMonotonic();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            if (registrationRoutine != null)
            {
                StopCoroutine(registrationRoutine);
                registrationRoutine = null;
            }
            int? abortPhaseId = CurrentPhaseId;
            presentationHandshake?.CancelPending();
            stateMachine.AbortRun(reason.Trim());
            bool willUpload = allowHostUpload && requireHostForStart &&
                hostRegistrationAccepted;
            try
            {
                CloseAssistanceExposures(abortPhaseId, now);
                RecordAt(
                    InteractionEventNames.RunAborted,
                    null,
                    now,
                    null,
                    null,
                    BuildReasonPayload(reason)
                );
                if (willUpload)
                {
                    RecordAt(
                        InteractionEventNames.UploadStarted,
                        null,
                        now,
                        null,
                        null,
                        "{\"terminal_status\":\"aborted\"}"
                    );
                }
                captureWriter.Seal(completeness =>
                    summaryTracker.SealAborted(
                        now,
                        utcNow,
                        reason.Trim(),
                        completeness
                    )
                );
                stateMachine.MarkRunAborted();
            }
            catch (Exception exception)
            {
                lastError = "Abort sealing failed: " + exception.Message;
                if (State == RunState.Aborting)
                {
                    stateMachine.FaultRun("abort_capture_seal_failed");
                }
                captureWriter.Dispose();
                throw;
            }
            if (willUpload)
            {
                BeginTerminalUpload(aborted: true, reason.Trim(), utcNow);
            }
        }

        public bool ResetToPreStart()
        {
            if (uploadRoutine != null ||
                (State != RunState.Completed && State != RunState.Aborted &&
                 State != RunState.Faulted))
            {
                return false;
            }
            captureWriter?.Dispose();
            captureWriter = null;
            summaryTracker = null;
            frozenRegistration = null;
            scheduledStartGate = null;
            presentationHandshake = null;
            uploadStateMachine = null;
            hostRegistrationAccepted = false;
            textExposureActive = false;
            pointingExposureActive = false;
            pointingTargetId = null;
            stateMachine.ResetToPreStart();
            RefreshPendingRuns();
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
                captureWriter.FlushPhase();
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

            if (nextRequest != null)
            {
                PublishPresentationRequest(nextRequest);
                return;
            }
            if (State != RunState.Completing)
            {
                throw new InvalidOperationException(
                    "Unexpected Run state after phase result: " + State + "."
                );
            }
            CompleteRun(now);
        }

        private void CompleteRun(double now)
        {
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
                captureWriter.Seal(completeness =>
                    summaryTracker.SealCompleted(
                        now,
                        utcNow,
                        completeness
                    )
                );
                stateMachine.MarkRunCompleted();
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
            if (willUpload)
            {
                BeginTerminalUpload(aborted: false, null, utcNow);
            }
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
                    lastError = terminalError;
                    yield break;
                }

                InteractionFrozenArtifactSet artifacts;
                try
                {
                    artifacts = InteractionFrozenArtifactSet.ReadOnce(
                        captureWriter.RunDirectory
                    );
                }
                catch (Exception exception)
                {
                    uploadStateMachine.Defer(
                        "Artifact snapshot failed: " + exception.Message
                    );
                    lastError = uploadStateMachine.LastError;
                    yield break;
                }

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
                }
                uploadStateMachine.AwaitAck();

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
                InteractionFrozenArtifactSet artifacts =
                    InteractionFrozenArtifactSet.ReadOnce(pending.DirectoryPath);
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
                }
                tracker.AwaitAck();
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
                        if (tracker.ApplyAck(
                                ack.Value,
                                DateTimeOffset.UtcNow))
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

        private void OnApplicationPause(bool paused)
        {
            if (paused &&
                InteractionLifecycleTerminationPolicy.RequiresLocalAbort(State))
            {
                try
                {
                    AbortRun("application_pause");
                }
                catch (Exception exception)
                {
                    lastError = "Pause abort failed: " + exception.Message;
                    Debug.LogError(
                        "[InteractionRunController] " + lastError,
                        this
                    );
                }
            }
        }

        private void OnApplicationQuit()
        {
            if (InteractionLifecycleTerminationPolicy.RequiresLocalAbort(State))
            {
                try
                {
                    AbortRunInternal(
                        "application_quit",
                        allowHostUpload: false
                    );
                }
                catch
                {
                    captureWriter?.Dispose();
                }
            }
        }

        private void OnDestroy()
        {
            if (InteractionLifecycleTerminationPolicy.RequiresLocalAbort(State))
            {
                try
                {
                    AbortRunInternal(
                        "controller_destroyed",
                        allowHostUpload: false
                    );
                }
                catch (Exception exception)
                {
                    lastError =
                        "Destroy abort retained partial data: " +
                        exception.Message;
                    captureWriter?.Dispose();
                }
            }
            else if (captureWriter != null && !captureWriter.IsSealed)
            {
                captureWriter.Dispose();
            }
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
