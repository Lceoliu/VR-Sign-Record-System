using System;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Deterministic presentation policy for one Instruction Phase. It owns no
    /// scene objects and never gates the phase's real interactions.
    /// </summary>
    public sealed class InstructionPhasePresentationState
    {
        public const double BubbleDelaySeconds = 1d;

        private AssistanceCondition condition;
        private bool phaseActive;
        private bool firstPlaybackCompleted;
        private bool bubbleVisible;
        private bool replayConsumed;
        private bool replayInProgress;
        private bool replayCompleted;
        private double bubbleDueAt = double.PositiveInfinity;
        private double lastMonotonicTime = double.NaN;

        public event Action Changed;
        public event Action BubbleShown;
        public event Action BubbleHidden;
        public event Action ReplayBecameAvailable;
        public event Action ReplayUsed;
        public event Action AllowedReplayCompleted;

        public AssistanceCondition Condition => condition;

        public bool PhaseActive => phaseActive;

        public bool HasCompletedFirstPlayback => firstPlaybackCompleted;

        public bool TextAllowed => condition.IncludesText();

        public bool PointingAllowed => condition.IncludesPointing();

        public bool BubbleVisible => bubbleVisible;

        public bool ReplayAvailable =>
            phaseActive && firstPlaybackCompleted && !replayConsumed;

        public bool ReplayConsumed => replayConsumed;

        public bool ReplayInProgress => replayInProgress;

        public bool GiveUpAvailable =>
            phaseActive && replayConsumed && replayCompleted &&
            !replayInProgress;

        public bool InteractionsEnabled => phaseActive;

        public void BeginPhase(AssistanceCondition assistanceCondition)
        {
            // Calling both projections also rejects undefined enum values at
            // the only condition ingress for this presentation session.
            assistanceCondition.IncludesText();
            assistanceCondition.IncludesPointing();

            bool wasVisible = bubbleVisible;
            condition = assistanceCondition;
            phaseActive = true;
            firstPlaybackCompleted = false;
            bubbleVisible = false;
            replayConsumed = false;
            replayInProgress = false;
            replayCompleted = false;
            bubbleDueAt = double.PositiveInfinity;
            lastMonotonicTime = double.NaN;

            if (wasVisible)
            {
                BubbleHidden?.Invoke();
            }
            Changed?.Invoke();
        }

        public void FirstPlaybackCompleted(double monotonicTime)
        {
            EnsurePhaseActive();
            AdvanceTime(monotonicTime);
            if (firstPlaybackCompleted)
            {
                throw new InvalidOperationException(
                    "First playback was already completed for this phase."
                );
            }

            firstPlaybackCompleted = true;
            if (TextAllowed)
            {
                bubbleDueAt = monotonicTime + BubbleDelaySeconds;
            }

            ReplayBecameAvailable?.Invoke();
            Changed?.Invoke();
        }

        public void Tick(double monotonicTime)
        {
            if (!phaseActive)
            {
                return;
            }

            AdvanceTime(monotonicTime);
            if (!bubbleVisible && TextAllowed && firstPlaybackCompleted &&
                monotonicTime >= bubbleDueAt)
            {
                bubbleVisible = true;
                BubbleShown?.Invoke();
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Permanently consumes the phase allowance before playback is asked to
        /// start. A downstream playback failure therefore cannot refund it.
        /// </summary>
        public bool TryConsumeReplay()
        {
            if (!ReplayAvailable)
            {
                return false;
            }

            replayConsumed = true;
            replayInProgress = true;
            ReplayUsed?.Invoke();
            Changed?.Invoke();
            return true;
        }

        public void ReplayCompleted()
        {
            EnsurePhaseActive();
            if (!replayConsumed || !replayInProgress || replayCompleted)
            {
                throw new InvalidOperationException(
                    "The allowed replay is not currently in progress."
                );
            }

            replayInProgress = false;
            replayCompleted = true;
            AllowedReplayCompleted?.Invoke();
            Changed?.Invoke();
        }

        public void EndPhase()
        {
            if (!phaseActive && !bubbleVisible)
            {
                return;
            }

            bool wasVisible = bubbleVisible;
            phaseActive = false;
            bubbleVisible = false;
            replayInProgress = false;
            bubbleDueAt = double.PositiveInfinity;
            if (wasVisible)
            {
                BubbleHidden?.Invoke();
            }
            Changed?.Invoke();
        }

        private void EnsurePhaseActive()
        {
            if (!phaseActive)
            {
                throw new InvalidOperationException(
                    "No Instruction Phase is active."
                );
            }
        }

        private void AdvanceTime(double monotonicTime)
        {
            if (double.IsNaN(monotonicTime) ||
                double.IsInfinity(monotonicTime) ||
                monotonicTime < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(monotonicTime));
            }

            if (!double.IsNaN(lastMonotonicTime) &&
                monotonicTime < lastMonotonicTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTime),
                    "Presentation time must be monotonic."
                );
            }

            lastMonotonicTime = monotonicTime;
        }
    }
}
