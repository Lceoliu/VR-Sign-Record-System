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
        public const double TextRevealDelaySeconds = 1d;

        private AssistanceCondition condition;
        private bool phaseActive;
        private bool firstPlaybackCompleted;
        private bool firstPlaybackStarted;
        private bool bubbleVisible;
        private bool replayConsumed;
        private bool replayInProgress;
        private double lastMonotonicTime = double.NaN;
        private double textRevealDeadline = double.PositiveInfinity;

        public event Action Changed;
        public event Action BubbleShown;
        public event Action BubbleHidden;
        public event Action ReplayBecameAvailable;
        public event Action ReplayUsed;
        public event Action AllowedReplayCompleted;

        public AssistanceCondition Condition => condition;

        public bool PhaseActive => phaseActive;

        public bool HasCompletedFirstPlayback => firstPlaybackCompleted;

        public bool HasStartedFirstPlayback => firstPlaybackStarted;

        public bool TextAllowed => condition.IncludesText();

        public bool PointingAllowed => condition.IncludesPointing();

        public bool BubbleVisible => bubbleVisible;

        public bool ReplayAvailable =>
            phaseActive && !replayInProgress &&
            (!firstPlaybackStarted || !replayConsumed);

        public bool ReplayConsumed => replayConsumed;

        public bool ReplayInProgress => replayInProgress;

        public bool GiveUpAvailable =>
            phaseActive;

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
            firstPlaybackStarted = false;
            bubbleVisible = false;
            replayConsumed = false;
            replayInProgress = false;
            lastMonotonicTime = double.NaN;
            textRevealDeadline = double.PositiveInfinity;

            if (wasVisible)
            {
                BubbleHidden?.Invoke();
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Starts signer playback without exposing the transcript. Text is
        /// revealed one second after the first playback completes.
        /// </summary>
        public void InstructionPlaybackStarted(double monotonicTime)
        {
            EnsurePhaseActive();
            AdvanceTime(monotonicTime);
            firstPlaybackStarted = true;
            replayInProgress = true;
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
            replayInProgress = false;
            if (TextAllowed && !bubbleVisible)
            {
                textRevealDeadline = monotonicTime +
                    TextRevealDelaySeconds;
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
                monotonicTime >= textRevealDeadline)
            {
                bubbleVisible = true;
                textRevealDeadline = double.PositiveInfinity;
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

            bool isFirstPlayback = !firstPlaybackStarted;
            if (!isFirstPlayback)
            {
                replayConsumed = true;
            }
            replayInProgress = true;
            if (!isFirstPlayback)
            {
                ReplayUsed?.Invoke();
            }
            Changed?.Invoke();
            return true;
        }

        public void ReplayCompleted()
        {
            EnsurePhaseActive();
            if (!replayConsumed || !replayInProgress)
            {
                throw new InvalidOperationException(
                    "The allowed replay is not currently in progress."
                );
            }

            replayInProgress = false;
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
            textRevealDeadline = double.PositiveInfinity;
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
