using System;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class InteractionDigitBinding :
        MonoBehaviour,
        IInteractionTriggerInput
    {
        [SerializeField, Range(0, 9)]
        private int digit;

        [SerializeField]
        private PhaseOneInteractionAdapter adapter;

        [SerializeField]
        private Collider inputCollider;

        private bool availabilitySubscribed;

        public int Digit => digit;

        public PhaseOneInteractionAdapter Adapter => adapter;

        public Collider InputCollider => inputCollider;

        public bool IsInputAvailable =>
            isActiveAndEnabled && adapter != null && adapter.IsEnabled;

        private void OnEnable()
        {
            BindAvailability();
            ApplyAvailability(adapter != null && adapter.IsEnabled);
        }

        public void Configure(
            int configuredDigit,
            PhaseOneInteractionAdapter phaseAdapter,
            Collider targetInputCollider = null)
        {
            UnbindAvailability();
            if (configuredDigit < 0 || configuredDigit > 9)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(configuredDigit)
                );
            }

            digit = configuredDigit;
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

            return adapter.AcceptDigit(digit);
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

        private void UnbindAvailability()
        {
            if (availabilitySubscribed && adapter != null)
            {
                adapter.AvailabilityChanged -= ApplyAvailability;
            }
            availabilitySubscribed = false;
        }
    }
}
