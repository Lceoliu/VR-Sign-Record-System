using SignVR.Interaction.Core;

namespace SignVR.Interaction.PhaseAdapters
{
    public sealed class PhaseOneInteractionAdapter : InteractionPhaseAdapter
    {
        public override int PhaseId => 1;

        public ValidationResult AcceptDigit(int digit)
        {
            return AcceptInput(PhaseInput.Digit(digit));
        }

        public ValidationResult SubmitPassword()
        {
            return AcceptInput(PhaseInput.Submit());
        }

        public ValidationResult BackspacePassword()
        {
            return AcceptInput(PhaseInput.Backspace());
        }
    }
}
