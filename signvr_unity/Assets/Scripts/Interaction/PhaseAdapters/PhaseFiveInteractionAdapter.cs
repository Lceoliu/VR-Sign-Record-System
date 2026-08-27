using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    public static class InteractionPhaseFeedbackTiming
    {
        public const double PhaseFiveErrorResetDelaySeconds = 0.6d;
    }

    public sealed class PhaseFiveInteractionAdapter : InteractionPhaseAdapter
    {
        private double inputBlockedUntil = double.NegativeInfinity;

        public override int PhaseId => 5;

        public override ValidationResult AcceptInput(PhaseInput input)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < inputBlockedUntil)
            {
                return null;
            }

            ValidationResult result = base.AcceptInput(input);
            if (result != null && result.ProgressReset &&
                !result.PhaseGivenUp)
            {
                inputBlockedUntil = now +
                    InteractionPhaseFeedbackTiming
                        .PhaseFiveErrorResetDelaySeconds;
            }
            return result;
        }

        public override void Reset()
        {
            ClearInputBlock();
            base.Reset();
        }

        public override void Disable()
        {
            base.Disable();
            if (Coordinator == null || Coordinator.CurrentPhaseId != PhaseId)
            {
                ClearInputBlock();
            }
        }

        private void ClearInputBlock()
        {
            inputBlockedUntil = double.NegativeInfinity;
        }
    }
}
