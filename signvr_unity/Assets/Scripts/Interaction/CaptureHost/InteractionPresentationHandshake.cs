using System;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    public enum InteractionPresentationPlaybackKind
    {
        First,
        Replay
    }

    public sealed class InteractionPresentationRequest
    {
        internal InteractionPresentationRequest(
            long requestSequence,
            string runId,
            int phaseId,
            InteractionPresentationPlaybackKind playbackKind)
        {
            RequestSequence = requestSequence;
            RunId = runId;
            PhaseId = phaseId;
            PlaybackKind = playbackKind;
        }

        public long RequestSequence { get; }
        public string RunId { get; }
        public int PhaseId { get; }
        public InteractionPresentationPlaybackKind PlaybackKind { get; }
        public bool IsReplay =>
            PlaybackKind == InteractionPresentationPlaybackKind.Replay;
    }

    public sealed class InteractionPresentationStart
    {
        internal InteractionPresentationStart(
            InteractionPresentationRequest request,
            bool initialRunStart,
            int? previousPhaseId,
            bool previousPhaseStuck,
            double? previousPhaseCompletedAtSeconds,
            double actualPlaybackStartedAtSeconds)
        {
            Request = request;
            IsInitialRunStart = initialRunStart;
            PreviousPhaseId = previousPhaseId;
            PreviousPhaseStuck = previousPhaseStuck;
            PreviousPhaseCompletedAtSeconds = previousPhaseCompletedAtSeconds;
            ActualPlaybackStartedAtSeconds = actualPlaybackStartedAtSeconds;
        }

        public InteractionPresentationRequest Request { get; }
        public bool IsInitialRunStart { get; }
        public int? PreviousPhaseId { get; }
        public bool PreviousPhaseStuck { get; }
        public double? PreviousPhaseCompletedAtSeconds { get; }
        public double ActualPlaybackStartedAtSeconds { get; }
    }

    /// <summary>
    /// Keeps presentation scheduling separate from the W1 authority. Merely
    /// requesting a clip never advances Run/Phase state. W1 receives the
    /// actual first-frame monotonic timestamp only when presentation ACKs it.
    /// </summary>
    public sealed class InteractionPresentationHandshake
    {
        private readonly InteractionRunStateMachine stateMachine;
        private InteractionPresentationRequest pending;
        private int? pendingPreviousPhaseId;
        private bool pendingPreviousPhaseStuck;
        private double? pendingPreviousPhaseCompletedAt;
        private long nextRequestSequence = 1L;

        public InteractionPresentationHandshake(
            InteractionRunStateMachine stateMachine)
        {
            this.stateMachine = stateMachine ??
                throw new ArgumentNullException(nameof(stateMachine));
        }

        public InteractionPresentationRequest PendingRequest => pending;
        public bool HasPendingRequest => pending != null;
        public bool IsAwaitingNextPhasePlayback =>
            pending != null && pendingPreviousPhaseId.HasValue;

        public InteractionPresentationRequest RequestInitialPlayback()
        {
            if (stateMachine.State != RunState.Scheduled ||
                stateMachine.Plan == null)
            {
                throw new InvalidOperationException(
                    "Initial presentation can be requested only for a scheduled Run."
                );
            }
            return CreateRequest(
                1,
                InteractionPresentationPlaybackKind.First
            );
        }

        public InteractionPresentationRequest RequestNextPhasePlayback(
            bool stuck,
            double phaseCompletedAtSeconds)
        {
            ValidateTime(phaseCompletedAtSeconds);
            PhaseExecutionSnapshot current = RequireInteractivePhase();
            if (current.PhaseId >= PhaseSentenceRanges.PhaseCount)
            {
                throw new InvalidOperationException(
                    "The final phase has no next presentation."
                );
            }
            if (!current.FirstPlaybackStartedAt.HasValue ||
                phaseCompletedAtSeconds <
                    current.FirstPlaybackStartedAt.Value.TotalSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phaseCompletedAtSeconds)
                );
            }
            InteractionPresentationRequest request = CreateRequest(
                current.PhaseId + 1,
                InteractionPresentationPlaybackKind.First
            );
            pendingPreviousPhaseId = current.PhaseId;
            pendingPreviousPhaseStuck = stuck;
            pendingPreviousPhaseCompletedAt = phaseCompletedAtSeconds;
            return request;
        }

        public InteractionPresentationRequest RequestReplayPlayback()
        {
            EnsureNoPending();
            PhaseExecutionSnapshot current = RequireInteractivePhase();
            if (!current.ReplayAvailable)
            {
                throw new InvalidOperationException(
                    "Replay is not available for the current phase."
                );
            }
            return CreateRequest(
                current.PhaseId,
                current.FirstPlaybackCompleted
                    ? InteractionPresentationPlaybackKind.Replay
                    : InteractionPresentationPlaybackKind.First
            );
        }

        public InteractionPresentationStart AcknowledgePlaybackStarted(
            long requestSequence,
            int phaseId,
            InteractionPresentationPlaybackKind playbackKind,
            double actualPlaybackStartedAtSeconds)
        {
            ValidateTime(actualPlaybackStartedAtSeconds);
            InteractionPresentationRequest request = pending;
            if (request == null || request.RequestSequence != requestSequence ||
                request.PhaseId != phaseId ||
                request.PlaybackKind != playbackKind)
            {
                throw new InvalidOperationException(
                    "Presentation start confirmation does not match the pending request."
                );
            }

            bool initial = stateMachine.State == RunState.Scheduled;
            int? previousPhaseId = pendingPreviousPhaseId;
            bool previousStuck = pendingPreviousPhaseStuck;
            double? previousCompletedAt = pendingPreviousPhaseCompletedAt;
            if (initial)
            {
                if (playbackKind != InteractionPresentationPlaybackKind.First ||
                    phaseId != 1 || previousPhaseId.HasValue)
                {
                    throw new InvalidOperationException(
                    "Initial playback confirmation is inconsistent."
                    );
                }
                stateMachine.RunStarted(
                    TimeSpan.FromSeconds(actualPlaybackStartedAtSeconds)
                );
            }
            else if (previousPhaseId.HasValue)
            {
                if (playbackKind != InteractionPresentationPlaybackKind.First ||
                    stateMachine.CurrentPhaseId != previousPhaseId)
                {
                    throw new InvalidOperationException(
                    "Next-phase playback confirmation is inconsistent."
                    );
                }
                if (previousStuck)
                {
                    stateMachine.GiveUpPhase(
                        TimeSpan.FromSeconds(actualPlaybackStartedAtSeconds)
                    );
                }
                else
                {
                    stateMachine.CompletePhase(
                        TimeSpan.FromSeconds(actualPlaybackStartedAtSeconds)
                    );
                }
                if (stateMachine.CurrentPhaseId != phaseId)
                {
                    throw new InvalidOperationException(
                        "W1 advanced to an unexpected phase."
                    );
                }
            }
            else
            {
                if (stateMachine.State != RunState.Running ||
                    stateMachine.CurrentPhaseId != phaseId ||
                    (playbackKind == InteractionPresentationPlaybackKind.First
                        ? stateMachine.CurrentPhase.State !=
                            PhaseState.FirstPlayback ||
                            stateMachine.CurrentPhase.FirstPlaybackCompleted
                        : stateMachine.CurrentPhase.State != PhaseState.Active))
                {
                    throw new InvalidOperationException(
                    "Replay playback confirmation is inconsistent."
                    );
                }
                stateMachine.ReplayInstruction();
            }

            ClearPending();
            return new InteractionPresentationStart(
                request,
                initial,
                previousPhaseId,
                previousStuck,
                previousCompletedAt,
                actualPlaybackStartedAtSeconds
            );
        }

        public void CompleteFinalPhase(
            bool stuck,
            double phaseCompletedAtSeconds)
        {
            ValidateTime(phaseCompletedAtSeconds);
            EnsureNoPending();
            PhaseExecutionSnapshot current = RequireInteractivePhase();
            if (current.PhaseId != PhaseSentenceRanges.PhaseCount)
            {
                throw new InvalidOperationException(
                    "Only phase 6 can complete without another presentation."
                );
            }
            TimeSpan completion = TimeSpan.FromSeconds(
                phaseCompletedAtSeconds
            );
            if (stuck)
            {
                stateMachine.GiveUpPhase(completion);
            }
            else
            {
                stateMachine.CompletePhase(completion);
            }
        }

        public void CancelPending()
        {
            ClearPending();
        }

        private InteractionPresentationRequest CreateRequest(
            int phaseId,
            InteractionPresentationPlaybackKind kind)
        {
            EnsureNoPending();
            pending = new InteractionPresentationRequest(
                nextRequestSequence,
                stateMachine.Plan.RunId,
                phaseId,
                kind
            );
            nextRequestSequence = checked(nextRequestSequence + 1L);
            return pending;
        }

        private PhaseExecutionSnapshot RequireActivePhase()
        {
            if (stateMachine.State != RunState.Running ||
                stateMachine.CurrentPhase == null ||
                stateMachine.CurrentPhase.State != PhaseState.Active)
            {
                throw new InvalidOperationException(
                    "Presentation transition requires an active W1 phase."
                );
            }
            return stateMachine.CurrentPhase;
        }

        private PhaseExecutionSnapshot RequireInteractivePhase()
        {
            if (stateMachine.State != RunState.Running ||
                stateMachine.CurrentPhase == null ||
                !stateMachine.CurrentPhase.InteractionsEnabled)
            {
                throw new InvalidOperationException(
                    "Successful presentation transition requires an " +
                    "interaction-enabled W1 phase."
                );
            }
            return stateMachine.CurrentPhase;
        }

        private void EnsureNoPending()
        {
            if (pending != null)
            {
                throw new InvalidOperationException(
                    "A presentation request is already pending."
                );
            }
        }

        private void ClearPending()
        {
            pending = null;
            pendingPreviousPhaseId = null;
            pendingPreviousPhaseStuck = false;
            pendingPreviousPhaseCompletedAt = null;
        }

        private static void ValidateTime(double value)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                value,
                nameof(value)
            );
        }
    }
}
