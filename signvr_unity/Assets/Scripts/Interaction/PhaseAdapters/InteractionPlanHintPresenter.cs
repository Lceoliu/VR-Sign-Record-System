using System;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class InteractionPlanHintPresenter : MonoBehaviour
    {
        [SerializeField]
        private InteractionPhaseCoordinator coordinator;

        [SerializeField]
        private TextMesh safePasswordText;

        [SerializeField]
        private TextMesh chestOrderText;

        private RunPlan plan;
        private bool subscribed;

        public TextMesh SafePasswordText => safePasswordText;
        public TextMesh ChestOrderText => chestOrderText;
        public InteractionPhaseCoordinator Coordinator => coordinator;

        private void Awake()
        {
            Bind();
            HideHints();
        }

        private void OnEnable()
        {
            Bind();
        }

        public void Configure(
            InteractionPhaseCoordinator targetCoordinator,
            TextMesh targetSafePasswordText,
            TextMesh targetChestOrderText)
        {
            Unbind();
            coordinator = targetCoordinator ??
                throw new ArgumentNullException(nameof(targetCoordinator));
            safePasswordText = targetSafePasswordText ??
                throw new ArgumentNullException(nameof(targetSafePasswordText));
            chestOrderText = targetChestOrderText ??
                throw new ArgumentNullException(nameof(targetChestOrderText));
            Bind();
            HideHints();
        }

        private void Bind()
        {
            if (subscribed || coordinator == null || !isActiveAndEnabled)
            {
                return;
            }
            coordinator.RunConfigured += HandleRunConfigured;
            coordinator.RunReset += HideHints;
            coordinator.ResultProduced += HandleResult;
            subscribed = true;
        }

        private void Unbind()
        {
            if (subscribed && coordinator != null)
            {
                coordinator.RunConfigured -= HandleRunConfigured;
                coordinator.RunReset -= HideHints;
                coordinator.ResultProduced -= HandleResult;
            }
            subscribed = false;
        }

        private void HandleRunConfigured(RunPlan runPlan)
        {
            plan = runPlan;
            HideHints();
        }

        private void HandleResult(ValidationResult result)
        {
            if (!isActiveAndEnabled || plan == null || result == null)
            {
                return;
            }

            if (result.PhaseId == 1 &&
                (result.ProgressReset || result.PhaseCompleted ||
                 result.PhaseGivenUp))
            {
                HideSafePassword();
            }
            if (result.PhaseId == 4 &&
                (result.PhaseCompleted || result.PhaseGivenUp))
            {
                HideChestOrder();
            }

            if (result.FeedbackCue ==
                PhaseFeedbackCue.SafePasswordRevealed)
            {
                safePasswordText.text = string.Join(
                    string.Empty,
                    plan.SafePassword.Digits
                );
                safePasswordText.gameObject.SetActive(true);
            }
            else if (result.FeedbackCue ==
                     PhaseFeedbackCue.ChestOrderUnlocked ||
                     (result.PhaseId == 3 && result.PhaseGivenUp))
            {
                chestOrderText.text = string.Join(
                    "  ",
                    plan.ChestButtonOrder.ButtonIds
                );
                chestOrderText.gameObject.SetActive(true);
            }
        }

        private void HideHints()
        {
            plan = coordinator != null ? coordinator.Plan : null;
            HideSafePassword();
            HideChestOrder();
        }

        private void HideSafePassword()
        {
            if (safePasswordText != null)
            {
                safePasswordText.gameObject.SetActive(false);
            }
        }

        private void HideChestOrder()
        {
            if (chestOrderText != null)
            {
                chestOrderText.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            Unbind();
            HideHints();
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
