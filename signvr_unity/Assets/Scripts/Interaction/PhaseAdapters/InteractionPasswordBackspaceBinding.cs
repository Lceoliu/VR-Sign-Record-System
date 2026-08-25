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

        public PhaseOneInteractionAdapter Adapter => adapter;

        public Collider InputCollider => inputCollider;

        public bool IsInputAvailable =>
            isActiveAndEnabled && adapter != null && adapter.IsEnabled;

        private void OnEnable()
        {
            BindAvailability();
            ApplyAvailability(IsInputAvailable);
        }

        public void Configure(
            PhaseOneInteractionAdapter phaseAdapter,
            Collider targetInputCollider = null)
        {
            UnbindAvailability();
            adapter = phaseAdapter ??
                throw new ArgumentNullException(nameof(phaseAdapter));
            inputCollider = targetInputCollider;
            if (isActiveAndEnabled)
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
            if (availabilitySubscribed || adapter == null)
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
            if (inputCollider != null)
            {
                inputCollider.enabled = available;
            }
        }

        private void OnDisable()
        {
            ApplyAvailability(false);
            UnbindAvailability();
        }
    }
}
