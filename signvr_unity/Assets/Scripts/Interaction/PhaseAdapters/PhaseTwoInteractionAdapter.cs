using SignVR.Interaction.Core;

namespace SignVR.Interaction.PhaseAdapters
{
    public sealed class PhaseTwoInteractionAdapter : InteractionPhaseAdapter
    {
        public override int PhaseId => 2;

        public ValidationResult AcceptPlacement(
            string coinTargetId,
            string plateTargetId)
        {
            return AcceptInput(PhaseInput.Pair(
                coinTargetId,
                plateTargetId
            ));
        }
    }
}
