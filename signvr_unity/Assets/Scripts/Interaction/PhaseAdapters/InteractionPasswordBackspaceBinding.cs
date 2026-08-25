using System;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class InteractionPasswordBackspaceBinding :
        MonoBehaviour,
        IInteractionTriggerInput
    {
        [SerializeField]
        private PhaseOneInteractionAdapter adapter;

        [SerializeField]
        private Collider inputCollider;

        private bool availabilitySubscribed;

        [SerializeField, HideInInspector]
        private Collider authoredInputCollider;

        [SerializeField, HideInInspector]
        private bool authoredColliderEnabled;

        [SerializeField, HideInInspector]
        private bool authoredColliderStateCaptured;

#if UNITY_INCLUDE_TESTS
        private readonly InteractionSubscriptionDiagnostic
            subscriptionDiagnostic =
                new InteractionSubscriptionDiagnostic();
#endif

        public PhaseOneInteractionAdapter Adapter => adapter;

        public Collider InputCollider => inputCollider;

        public bool IsInputAvailable =>
            isActiveAndEnabled && adapter != null && adapter.IsEnabled;

#if UNITY_INCLUDE_TESTS
        public InteractionSubscriptionDiagnostic SubscriptionDiagnostic =>
            subscriptionDiagnostic;
#endif

        private void OnEnable()
        {
            BindAvailability();
            ApplyAvailability(IsInputAvailable);
        }

        public void Configure(
            PhaseOneInteractionAdapter phaseAdapter,
            Collider targetInputCollider = null)
        {
            PhaseOneInteractionAdapter nextAdapter = phaseAdapter ??
                throw new ArgumentNullException(nameof(phaseAdapter));
            bool colliderChanged = !ReferenceEquals(
                inputCollider,
                targetInputCollider
            );
            bool manageRuntimeSubscriptions =
                Application.isPlaying && isActiveAndEnabled;
            if (manageRuntimeSubscriptions)
            {
                UnbindAvailability();
            }
            if (colliderChanged)
            {
                ReleaseInputOwnership();
            }
            adapter = nextAdapter;
            inputCollider = targetInputCollider;
            CaptureInputOwnership();
            if (manageRuntimeSubscriptions)
            {
                BindAvailability();
            }
            ApplyAvailability(IsInputAvailable);
        }

        public ValidationResult AcceptInput()
        {
            if (!isActiveAndEnabled)
            {
                return null;
            }
            if (adapter == null)
            {
                throw new InvalidOperationException(
                    $"{name} has no Phase 1 adapter."
                );
            }
            if (!adapter.IsEnabled)
            {
                return null;
            }
            return adapter.BackspacePassword();
        }

        public void Poke()
        {
            AcceptInput();
        }

        public void Trigger()
        {
            AcceptInput();
        }

        private void BindAvailability()
        {
            if (!Application.isPlaying || !isActiveAndEnabled ||
                availabilitySubscribed || adapter == null)
            {
                return;
            }
            adapter.AvailabilityChanged += ApplyAvailability;
            availabilitySubscribed = true;
        }

        private void UnbindAvailability()
        {
            if (availabilitySubscribed && adapter != null)
            {
                adapter.AvailabilityChanged -= ApplyAvailability;
            }
            availabilitySubscribed = false;
        }

        private void ApplyAvailability(bool available)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordInvocation();
#endif
            if (inputCollider != null)
            {
                inputCollider.enabled = available;
            }
        }

        private void CaptureInputOwnership()
        {
            if (authoredColliderStateCaptured || inputCollider == null)
            {
                return;
            }
            authoredInputCollider = inputCollider;
            authoredColliderEnabled = inputCollider.enabled;
            authoredColliderStateCaptured = true;
        }

        private void ReleaseInputOwnership()
        {
            if (authoredColliderStateCaptured && authoredInputCollider != null)
            {
                authoredInputCollider.enabled = authoredColliderEnabled;
            }
            authoredInputCollider = null;
            authoredColliderStateCaptured = false;
        }

        private void OnDisable()
        {
            ApplyAvailability(false);
            UnbindAvailability();
        }

        private void OnDestroy()
        {
            UnbindAvailability();
            ReleaseInputOwnership();
        }
    }
}
