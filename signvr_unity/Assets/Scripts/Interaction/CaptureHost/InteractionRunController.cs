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
        [Header("Mode")]
        [SerializeField]
        private InteractionRunMode runMode = InteractionRunMode.StandaloneStudy;

        [SerializeField]
        [Tooltip("Must stay false for Study. This is a startup self-check, not an override implementation.")]
        private bool debugOverridesActive;

        [SerializeField]
        [Tooltip("EngineeringLocal requires this explicit arm flag and a debug build.")]
        private bool engineeringLocalExplicitlyArmed;

        [Header("Run identity")]
        [SerializeField]
        private string batchId = "pilot-20260826";

        [SerializeField]
        private string participantId = "UNCONFIGURED";

        [SerializeField]
        private string gitCommit = "unintegrated";

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
        private readonly InteractionLifecycleShutdownGate lifecycleShutdown =
            new InteractionLifecycleShutdownGate();
        private readonly InteractionTerminalSealArbiter terminalSealArbiter =
            new InteractionTerminalSealArbiter();
        private IInteractionBackgroundWorkQueue lifecycleWorkQueue =
            InteractionThreadPoolBackgroundWorkQueue.Shared;

        private AssistanceBlockAllocator conditionAllocator;
        private InteractionRunStateMachine stateMachine;
        private InstructionContentCatalog contentCatalog;
        private string appSessionId;
        private InteractionLocalScheduledStartGate scheduledStartGate;
        private InteractionCaptureWriter captureWriter;
        private InteractionSummaryTracker summaryTracker;
        private InteractionPresentationHandshake presentationHandshake;
        private InteractionStandaloneLocalRunRecoveryCoordinator startupRecovery;
        private string storageRoot;
        private InteractionBackgroundOperation<InteractionCaptureWriter>
            captureInitialization;
        private InteractionBackgroundOperation<InteractionCaptureSealResult>
            captureTerminalization;
        private InteractionBackgroundOperation<bool> activePhaseCheckpoint;
        private InteractionCaptureTerminalKind? captureTerminalKind;
        private bool captureInitializationReconciled;
        private bool captureTerminalizationReconciled;
        private Coroutine phaseCheckpointRoutine;
        private Coroutine terminalizationRoutine;
        private InteractionLifecycleTerminalizationJob
            lifecycleTerminalizationJob;
        private bool textExposureActive;
        private bool pointingExposureActive;
        private string pointingTargetId;
        private string pendingRunDiscoveryFailure = string.Empty;
        private string lastError = string.Empty;
        private bool headsetLifecycleSubscribed;
#if UNITY_EDITOR
        private bool editorLifecycleTestsArmed;
        private bool? debugBuildOverrideForTests;
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
        public string LastError => lastError;
        public string AppSessionId => appSessionId;
        public string BatchId => batchId;
        public string ParticipantId => participantId;
        public string GitCommit => gitCommit;
        public InteractionRunMode RunMode => runMode;
        public bool DebugOverridesActive => debugOverridesActive;
        public InteractionCaptureSampler CaptureSampler => captureSampler;
        public InteractionStandaloneLocalRunRecoveryStatus StartupRecoveryStatus =>
            startupRecovery == null
                ? InteractionStandaloneLocalRunRecoveryStatus.NotStarted
                : startupRecovery.Status;
        public int RecoveredPartialRunCount =>
            startupRecovery?.RecoveredRunCount ?? 0;
        public string StartupRecoveryFailureReason =>
            startupRecovery?.FailureReason ?? string.Empty;
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
            storageRoot = Application.persistentDataPath;
            BeginStandaloneStartupRecovery(storageRoot);
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
        }

        public void ConfigureMode(
            InteractionRunMode configuredMode,
            bool configuredDebugOverridesActive,
            bool explicitlyArmEngineeringLocal)
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
        }

        public bool CanStart(out string reason)
        {
            reason = null;
            if (stateMachine == null || State != RunState.PreStart)
            {
                reason = "Run is not in PreStart.";
                return false;
            }
            if (StartupRecoveryStatus !=
                InteractionStandaloneLocalRunRecoveryStatus.Succeeded)
            {
                reason = StartupRecoveryStatus ==
                        InteractionStandaloneLocalRunRecoveryStatus.Failed
                    ? "Standalone startup recovery failed: " +
                        StartupRecoveryFailureReason
                    : "Standalone startup recovery is still in progress.";
                return false;
            }
            if (lifecycleShutdown.IsShutdownInitiated ||
                captureInitialization != null ||
                captureTerminalization != null ||
                lifecycleTerminalizationJob != null)
            {
                reason = "The local capture lifecycle is not idle.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(pendingRunDiscoveryFailure))
            {
                reason = pendingRunDiscoveryFailure;
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
                    IsDebugBuild()
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
            }
            if (HasUnsealedPendingRun())
            {
                reason =
                    "A consumed partial Interaction Run must finish startup " +
                    "recovery before another Run starts.";
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
                    summaryTracker = new InteractionSummaryTracker(plan.RunId);
                    captureInitialization =
                        InteractionCaptureWriter.BeginCreateNew(
                        storageRoot ?? Application.persistentDataPath,
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

                captureInitializationReconciled = false;
                lastError = string.Empty;
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
            return TryAbortRunInternal(reason, out error);
        }

        private bool TryAbortRunInternal(
            string reason,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(reason))
            {
                error = "Abort reason is required.";
                return false;
            }
            if (!InteractionLifecycleTerminationPolicy.RequiresLocalAbort(State))
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
            int? abortPhaseId = CurrentPhaseId;
            presentationHandshake?.CancelPending();
            stateMachine.AbortRun(safeReason);
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
                        utcNow
                    )
                );
                return true;
            }
            if (captureWriter == null && captureInitialization != null)
            {
                StartLifecycleTerminalizationJob(
                    safeReason,
                    now,
                    utcNow,
                    Time.frameCount
                );
                return true;
            }
            try
            {
                BeginAbortSeal(
                    abortPhaseId,
                    safeReason,
                    now,
                    utcNow
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
            if (isActiveAndEnabled)
            {
                terminalizationRoutine = StartCoroutine(
                    AwaitTerminalSealRoutine(
                        InteractionCaptureTerminalKind.Aborted
                    )
                );
            }
            return true;
        }

        private void BeginAbortSeal(
            int? abortPhaseId,
            string reason,
            double monotonicNow,
            DateTimeOffset utcNow)
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
            captureTerminalizationReconciled = false;
        }

        private IEnumerator AbortAfterPhaseCheckpointRoutine(
            InteractionBackgroundOperation<bool> checkpoint,
            int? abortPhaseId,
            string reason,
            double monotonicNow,
            DateTimeOffset utcNow)
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
                    utcNow
                );
                while (!captureTerminalization.IsCompleted)
                {
                    yield return null;
                }
                CompleteTerminalSeal(
                    InteractionCaptureTerminalKind.Aborted
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
            if (lifecycleTerminalizationJob != null ||
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
            captureInitializationReconciled = false;
            captureTerminalizationReconciled = false;
            summaryTracker = null;
            scheduledStartGate = null;
            presentationHandshake = null;
            textExposureActive = false;
            pointingExposureActive = false;
            pointingTargetId = null;
            lifecycleShutdown.Reset();
            terminalSealArbiter.Reset();
            lifecycleTerminalizationJob = null;
            if (captureSampler != null)
            {
                captureSampler.ResetCadence();
                captureSampler.enabled = true;
            }
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
                    InteractionPendingRunDiscovery.DiscoverQuestLocal(
                        storageRoot ?? Application.persistentDataPath
                    )
                );
                pendingRunDiscoveryFailure = string.Empty;
            }
            catch (Exception exception)
            {
                pendingRunDiscoveryFailure =
                    "Pending Run discovery failed: " + exception.Message;
                lastError = pendingRunDiscoveryFailure;
                Debug.LogWarning(
                    "[InteractionRunController] " + lastError,
                    this
                );
            }
        }

        private void Update()
        {
            PollStandaloneStartupRecovery();
            ReconcileConsumedRunInitialization();
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

        private void BeginStandaloneStartupRecovery(string persistentDataPath)
        {
            storageRoot = Path.GetFullPath(persistentDataPath);
            startupRecovery =
                new InteractionStandaloneLocalRunRecoveryCoordinator(storageRoot);
            startupRecovery.Begin(
                "app_start_partial_recovery",
                DateTimeOffset.UtcNow,
                NowMonotonic(),
                Time.frameCount
            );
        }

        private void PollStandaloneStartupRecovery()
        {
            if (startupRecovery == null)
            {
                return;
            }
            if (startupRecovery.TryComplete())
            {
                ApplyStandaloneStartupRecoveryResult();
            }
        }

        private void ApplyStandaloneStartupRecoveryResult()
        {
            if (startupRecovery == null)
            {
                return;
            }
            if (startupRecovery.Status ==
                InteractionStandaloneLocalRunRecoveryStatus.Failed)
            {
                lastError = "Standalone startup recovery failed: " +
                    startupRecovery.FailureReason;
                return;
            }
            if (startupRecovery.Status ==
                InteractionStandaloneLocalRunRecoveryStatus.Succeeded)
            {
                lastError = string.Empty;
                RefreshPendingRuns();
            }
        }

        private bool IsDebugBuild()
        {
#if UNITY_EDITOR
            if (debugBuildOverrideForTests.HasValue)
            {
                return debugBuildOverrideForTests.Value;
            }
#endif
            return Debug.isDebugBuild;
        }

        private void ReconcileConsumedRunInitialization()
        {
            if (captureInitialization == null ||
                !captureInitialization.IsCompleted ||
                captureInitializationReconciled)
            {
                return;
            }
            captureInitializationReconciled = true;
            CompleteConsumedRunInitialization(stateMachine.Plan);
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
                ScheduleLocally(utcNow, now);
            }
            catch (Exception exception)
            {
                lastError = "Consumed Run initialization failed: " +
                    exception.Message;
                if (InteractionLifecycleTerminationPolicy
                    .RequiresLocalAbort(State))
                {
                    stateMachine.FaultRun(
                        "manifest_or_capture_initialization_failed"
                    );
                }
                captureWriter?.Dispose();
            }
        }

        private void ScheduleLocally(
            DateTimeOffset utcNow,
            double monotonicNow)
        {
            InteractionLocalScheduleTransition.Schedule(stateMachine, utcNow);
            scheduledStartGate = new InteractionLocalScheduledStartGate(
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
                // advancing W1. The actual next clip start confirmation supplies the
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
                    InteractionCaptureTerminalKind.Completed
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
                    out _
                );
                return false;
            }
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
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
            InteractionCaptureTerminalKind terminalKind)
        {
            try
            {
                yield return null;
                while (captureTerminalization != null &&
                    !captureTerminalization.IsCompleted)
                {
                    yield return null;
                }
                CompleteTerminalSeal(terminalKind);
            }
            finally
            {
                terminalizationRoutine = null;
            }
        }

        private void CompleteTerminalSeal(
            InteractionCaptureTerminalKind terminalKind)
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
            captureTerminalizationReconciled = true;
            terminalSealArbiter.MarkTerminal();
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
                    "clip and confirm its actual first frame.";
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
            if (InteractionLifecycleTerminationPolicy.RequiresLocalAbort(State) ||
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
                if (InteractionLifecycleTerminationPolicy
                        .RequiresLocalAbort(State) ||
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
            StopAllCoroutines();
            phaseCheckpointRoutine = null;
            terminalizationRoutine = null;
            if (captureSampler != null)
            {
                captureSampler.enabled = false;
            }
            if (!first || stateMachine == null)
            {
                return;
            }

            // A terminal seal already queued behind prior checkpoints remains
            // the unique local terminalization owner.
            if (captureTerminalization != null)
            {
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
            CompleteTerminalSeal(captureTerminalKind.Value);
            RefreshPendingRuns();
        }

        private void TryRestorePreStartLifecycle()
        {
            if (stateMachine == null || State != RunState.PreStart)
            {
                return;
            }
            lifecycleShutdown.Reset();
            if (captureSampler != null)
            {
                captureSampler.enabled = true;
            }
        }

        private void OnEnable()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            SubscribeHeadsetLifecycle();
            TryRestorePreStartLifecycle();
            ReconcileLifecycleTerminalization();
        }

        private enum ControllerLifecycleSignal
        {
            Disabled,
            ApplicationPaused,
            HeadsetUnmounted,
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
                case ControllerLifecycleSignal.HeadsetUnmounted:
                    BeginLifecycleShutdown("headset_unmounted");
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
            UnsubscribeHeadsetLifecycle();
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
            UnsubscribeHeadsetLifecycle();
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            ProcessLifecycleSignal(ControllerLifecycleSignal.Destroyed);
        }

        private void SubscribeHeadsetLifecycle()
        {
            if (headsetLifecycleSubscribed)
            {
                return;
            }
            OVRManager.HMDUnmounted += HandleHeadsetUnmounted;
            headsetLifecycleSubscribed = true;
        }

        private void UnsubscribeHeadsetLifecycle()
        {
            if (!headsetLifecycleSubscribed)
            {
                return;
            }
            OVRManager.HMDUnmounted -= HandleHeadsetUnmounted;
            headsetLifecycleSubscribed = false;
        }

        private void HandleHeadsetUnmounted()
        {
            if (!ShouldProcessUnityLifecycle())
            {
                return;
            }
            ProcessLifecycleSignal(ControllerLifecycleSignal.HeadsetUnmounted);
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
            captureQueueCapacity = Mathf.Max(16, captureQueueCapacity);
            captureGapThresholdSeconds = Mathf.Max(
                0.05f,
                captureGapThresholdSeconds
            );
        }
    }
}
