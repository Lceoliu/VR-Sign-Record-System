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

#if UNITY_INCLUDE_TESTS
        private readonly InteractionSubscriptionDiagnostic
            subscriptionDiagnostic =
                new InteractionSubscriptionDiagnostic();
#endif

        public TextMesh SafePasswordText => safePasswordText;
        public TextMesh ChestOrderText => chestOrderText;
        public InteractionPhaseCoordinator Coordinator => coordinator;

#if UNITY_INCLUDE_TESTS
        public InteractionSubscriptionDiagnostic SubscriptionDiagnostic =>
            subscriptionDiagnostic;
#endif

        private void Awake()
        {
            HideHints();
            Bind();
            RebuildFromAuthority();
        }

        private void OnEnable()
        {
            Bind();
            RebuildFromAuthority();
        }

        public void Configure(
            InteractionPhaseCoordinator targetCoordinator,
            TextMesh targetSafePasswordText,
            TextMesh targetChestOrderText)
        {
            InteractionPhaseCoordinator nextCoordinator = targetCoordinator ??
                throw new ArgumentNullException(nameof(targetCoordinator));
            TextMesh nextSafePasswordText = targetSafePasswordText ??
                throw new ArgumentNullException(nameof(targetSafePasswordText));
            TextMesh nextChestOrderText = targetChestOrderText ??
                throw new ArgumentNullException(nameof(targetChestOrderText));
            bool manageRuntimeSubscriptions =
                Application.isPlaying && isActiveAndEnabled;
            if (manageRuntimeSubscriptions)
            {
                Unbind();
            }
            HideHints();
            coordinator = nextCoordinator;
            safePasswordText = nextSafePasswordText;
            chestOrderText = nextChestOrderText;
            plan = coordinator.Plan;
            if (manageRuntimeSubscriptions)
            {
                Bind();
            }
            RebuildFromAuthority();
        }

        private void Bind()
        {
            if (!Application.isPlaying || subscribed || coordinator == null ||
                !isActiveAndEnabled)
            {
                return;
            }
            coordinator.RunConfigured += HandleRunConfigured;
            coordinator.RunReset += HandleRunReset;
            coordinator.ResultProduced += HandleResult;
            subscribed = true;
        }

        private void Unbind()
        {
            if (subscribed && coordinator != null)
            {
                coordinator.RunConfigured -= HandleRunConfigured;
                coordinator.RunReset -= HandleRunReset;
                coordinator.ResultProduced -= HandleResult;
            }
            subscribed = false;
        }

        private void HandleRunConfigured(RunPlan runPlan)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordRunConfigured();
#endif
            plan = runPlan;
            RebuildFromAuthority();
        }

        private void HandleRunReset()
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordRunReset();
#endif
            RebuildFromAuthority();
        }

        private void HandleResult(ValidationResult result)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordResultProduced();
#endif
            if (!isActiveAndEnabled || result == null)
            {
                return;
            }
            RebuildFromAuthority();
        }

        public void RebuildFromAuthority()
        {
            HideHints();
            if (!isActiveAndEnabled || coordinator == null)
            {
                return;
            }

            plan = coordinator.Plan;
            InteractionTaskPresentationSnapshot snapshot =
                coordinator.PresentationSnapshot;
            if (plan == null || snapshot == null)
            {
                return;
            }

            if (snapshot.SafePasswordVisible)
            {
                safePasswordText.text = string.Join(
                    string.Empty,
                    plan.SafePassword.Digits
                );
                safePasswordText.gameObject.SetActive(true);
            }
            if (snapshot.ChestOrderVisible)
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
            HideHints();
        }
    }
}
