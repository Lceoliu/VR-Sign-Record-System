using System;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.Orchestration
{
    public enum InteractionStudyPresentationObservationKind
    {
        BubbleShown,
        BubbleHidden,
        PointingHitStarted,
        PointingHitEnded
    }

    public sealed class InteractionStudyPresentationRequest
    {
        public InteractionStudyPresentationRequest(
            long requestSequence,
            string runId,
            int phaseId,
            InteractionPresentationPlaybackKind playbackKind)
        {
            if (requestSequence <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(requestSequence));
            }
            if (string.IsNullOrWhiteSpace(runId))
            {
                throw new ArgumentException(
                    "Run ID is required.",
                    nameof(runId)
                );
            }
            if (phaseId < 1 || phaseId > PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseId));
            }
            if (!Enum.IsDefined(typeof(InteractionPresentationPlaybackKind),
                    playbackKind))
            {
                throw new ArgumentOutOfRangeException(nameof(playbackKind));
            }

            RequestSequence = requestSequence;
            RunId = runId.Trim();
            PhaseId = phaseId;
            PlaybackKind = playbackKind;
        }

        public long RequestSequence { get; }
        public string RunId { get; }
        public int PhaseId { get; }
        public InteractionPresentationPlaybackKind PlaybackKind { get; }

        public bool Matches(InteractionStudyPresentationRequest other)
        {
            return other != null &&
                RequestSequence == other.RequestSequence &&
                PhaseId == other.PhaseId &&
                PlaybackKind == other.PlaybackKind &&
                string.Equals(RunId, other.RunId, StringComparison.Ordinal);
        }
    }

    public sealed class InteractionStudyPresentationObservation
    {
        public InteractionStudyPresentationObservation(
            InteractionStudyPresentationObservationKind kind,
            string targetId = null)
        {
            if (!Enum.IsDefined(
                    typeof(InteractionStudyPresentationObservationKind),
                    kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (kind ==
                    InteractionStudyPresentationObservationKind
                        .PointingHitStarted &&
                string.IsNullOrWhiteSpace(targetId))
            {
                throw new ArgumentException(
                    "A pointing-hit start requires a target ID.",
                    nameof(targetId)
                );
            }

            Kind = kind;
            TargetId = string.IsNullOrWhiteSpace(targetId)
                ? null
                : targetId.Trim();
        }

        public InteractionStudyPresentationObservationKind Kind { get; }
        public string TargetId { get; }
    }

    public sealed class InteractionStudyFlowCommandResult
    {
        private InteractionStudyFlowCommandResult(
            bool succeeded,
            string error)
        {
            Succeeded = succeeded;
            Error = error ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string Error { get; }

        public static InteractionStudyFlowCommandResult Success()
        {
            return new InteractionStudyFlowCommandResult(true, string.Empty);
        }

        public static InteractionStudyFlowCommandResult Failure(string error)
        {
            return new InteractionStudyFlowCommandResult(
                false,
                string.IsNullOrWhiteSpace(error)
                    ? "The command was rejected."
                    : error.Trim()
            );
        }
    }

    public sealed class InteractionStudyFlowSnapshot
    {
        internal InteractionStudyFlowSnapshot(
            RunState runState,
            int? phaseId,
            int progress,
            int requiredProgress,
            bool canStart,
            bool canReplay,
            bool canGiveUp,
            bool abortInProgress,
            bool isResultVisible,
            bool canAcknowledgeResult,
            RunResult? terminalOutcome,
            string status)
        {
            RunState = runState;
            PhaseId = phaseId;
            Progress = progress;
            RequiredProgress = requiredProgress;
            CanStart = canStart;
            CanReplay = canReplay;
            CanGiveUp = canGiveUp;
            AbortInProgress = abortInProgress;
            IsResultVisible = isResultVisible;
            CanAcknowledgeResult = canAcknowledgeResult;
            TerminalOutcome = terminalOutcome;
            Status = status ?? string.Empty;
        }

        public RunState RunState { get; }
        public int? PhaseId { get; }
        public int Progress { get; }
        public int RequiredProgress { get; }
        public bool CanStart { get; }
        public bool CanReplay { get; }
        public bool CanGiveUp { get; }
        public bool AbortInProgress { get; }
        public bool IsResultVisible { get; }
        public bool CanAcknowledgeResult { get; }
        public RunResult? TerminalOutcome { get; }
        public string Status { get; }
    }

    public interface IInteractionStudyRunPort
    {
        RunState State { get; }
        RunPlan Plan { get; }
        PhaseExecutionSnapshot CurrentPhase { get; }
        string LastError { get; }

        IDisposable Subscribe(
            Action<InteractionStudyPresentationRequest> presentationRequested
        );

        bool CanStart(out string reason);
        bool TryStart(out string error);
        bool TryRequestReplay(out string error);
        bool TryAcknowledgePresentationStarted(
            InteractionStudyPresentationRequest request,
            out string error
        );
        bool TryNotifyPlaybackCompleted(
            InteractionPresentationPlaybackKind playbackKind,
            out string error
        );
        bool TryRecordValidationResult(
            ValidationResult result,
            out string error
        );
        bool TryRecordPresentationObservation(
            InteractionStudyPresentationObservation observation,
            out string error
        );
        bool TryFinishPhase(bool stuck, out string error);
        bool TryAbort(string reason, out string error);
        bool TryResetToPreStart();
    }

    public interface IInteractionStudyPresentationPort
    {
        bool PhaseActive { get; }
        bool ReplayAvailable { get; }
        bool GiveUpAvailable { get; }

        IDisposable Subscribe(
            Action<InteractionPresentationPlaybackKind> firstFramePresented,
            Action<InteractionPresentationPlaybackKind> playbackCompleted,
            Action<InteractionStudyPresentationObservation>
                presentationObserved,
            Action<string> presentationFaulted
        );

        bool TryBeginPhase(
            RunPhasePlan phasePlan,
            AssistanceCondition condition,
            out string error
        );
        bool TryBeginReplay(out string error);
        void EndPhase();
    }

    public interface IInteractionStudyTaskPort
    {
        RunPlan Plan { get; }
        ValidationResult LastResult { get; }

        IDisposable Subscribe(Action<ValidationResult> resultProduced);
        void Configure(RunPlan plan);
        void Enable();
        void Disable();
        void Synchronize(PhaseExecutionSnapshot snapshot);
        ValidationResult GiveUp(PhaseExecutionSnapshot snapshot);
        void Reset();
        void Abort();
    }

    internal sealed class InteractionStudyCallbackDisposable : IDisposable
    {
        private Action dispose;

        public InteractionStudyCallbackDisposable(Action dispose)
        {
            this.dispose = dispose ??
                throw new ArgumentNullException(nameof(dispose));
        }

        public void Dispose()
        {
            Action callback = dispose;
            dispose = null;
            callback?.Invoke();
        }
    }
}
