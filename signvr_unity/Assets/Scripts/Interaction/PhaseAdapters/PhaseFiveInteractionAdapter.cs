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
        private bool inputInFlight;
        private uint inputGateGeneration;

        public override int PhaseId => 5;

        public override ValidationResult AcceptInput(PhaseInput input)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (inputInFlight || now < inputBlockedUntil)
            {
                return null;
            }

            uint generation = inputGateGeneration;
            inputInFlight = true;
            try
            {
                ValidationResult result = base.AcceptInput(input);
                if (generation == inputGateGeneration &&
                    result != null && result.ProgressReset &&
                    !result.PhaseGivenUp)
                {
                    inputBlockedUntil = now +
                        InteractionPhaseFeedbackTiming
                            .PhaseFiveErrorResetDelaySeconds;
                }
                return result;
            }
            finally
            {
                inputInFlight = false;
            }
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
            inputGateGeneration++;
            inputBlockedUntil = double.NegativeInfinity;
        }
    }
}
