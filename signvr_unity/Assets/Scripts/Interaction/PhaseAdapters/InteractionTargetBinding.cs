using System;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class InteractionTargetBinding :
        MonoBehaviour,
        IInteractionTriggerInput
    {
        [SerializeField]
        private string targetId = string.Empty;

        [SerializeField]
        private InteractionPhaseAdapter adapter;

        [SerializeField]
        private Behaviour[] interactionBehaviours = Array.Empty<Behaviour>();

        [SerializeField]
        private Collider[] inputColliders = Array.Empty<Collider>();

        private bool availabilitySubscribed;
        private bool resetSubscribed;
        private bool authoredStateCaptured;
        private Vector3 authoredLocalPosition;
        private Quaternion authoredLocalRotation;
        private Vector3 authoredLocalScale;
        private Rigidbody[] bodies = Array.Empty<Rigidbody>();
        private bool[] authoredBodyKinematic = Array.Empty<bool>();
        private bool[] authoredBodyGravity = Array.Empty<bool>();

        public string TargetId => targetId;

        public int PhaseId => adapter != null ? adapter.PhaseId : 0;

        public InteractionPhaseAdapter Adapter => adapter;

        public bool IsAvailable => adapter != null && adapter.IsEnabled;

        public bool IsInputAvailable => isActiveAndEnabled && IsAvailable;

        private void Awake()
        {
            CaptureAuthoredState();
        }

        private void OnEnable()
        {
            BindAdapterEvents();
            ApplyAvailability(adapter != null && adapter.IsEnabled);
        }

        public void Configure(
            string stableTargetId,
            InteractionPhaseAdapter phaseAdapter,
            Behaviour[] behaviours = null,
            Collider[] colliders = null)
        {
            UnbindAdapterEvents();
            targetId = string.IsNullOrWhiteSpace(stableTargetId)
                ? throw new ArgumentException(
                    "A stable target ID is required.",
                    nameof(stableTargetId)
                )
                : stableTargetId.Trim();
            adapter = phaseAdapter ??
                throw new ArgumentNullException(nameof(phaseAdapter));
            interactionBehaviours = behaviours ?? Array.Empty<Behaviour>();
            inputColliders = colliders ?? Array.Empty<Collider>();
            CaptureAuthoredState();
            BindAdapterEvents();
            ApplyAvailability(adapter.IsEnabled);
        }

        public ValidationResult AcceptInput()
        {
            if (adapter == null)
            {
                throw new InvalidOperationException(
                    $"{name} has no Interaction Phase adapter."
                );
            }

            if (string.IsNullOrWhiteSpace(targetId))
            {
                throw new InvalidOperationException(
                    $"{name} has no stable target ID."
                );
            }

            return adapter.AcceptTarget(targetId);
        }

        // Void entry points remain visible to Meta/UnityEvent wrappers.
        public void Poke()
        {
            AcceptInput();
        }

        public void Trigger()
        {
            AcceptInput();
        }

        public void Grab()
        {
            AcceptInput();
        }

        private void BindAdapterEvents()
        {
            if (adapter == null)
            {
                return;
            }

            if (!availabilitySubscribed)
            {
                adapter.AvailabilityChanged += ApplyAvailability;
                availabilitySubscribed = true;
            }
            if (!resetSubscribed)
            {
                adapter.ResetPerformed += RestoreAuthoredState;
                resetSubscribed = true;
            }
        }

        private void UnbindAdapterEvents()
        {
            if (availabilitySubscribed && adapter != null)
            {
                adapter.AvailabilityChanged -= ApplyAvailability;
            }
            if (resetSubscribed && adapter != null)
            {
                adapter.ResetPerformed -= RestoreAuthoredState;
            }
            availabilitySubscribed = false;
            resetSubscribed = false;
        }

        private void CaptureAuthoredState()
        {
            if (authoredStateCaptured)
            {
                return;
            }

            authoredLocalPosition = transform.localPosition;
            authoredLocalRotation = transform.localRotation;
            authoredLocalScale = transform.localScale;
            bodies = GetComponentsInChildren<Rigidbody>(true);
            authoredBodyKinematic = new bool[bodies.Length];
            authoredBodyGravity = new bool[bodies.Length];
            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                authoredBodyKinematic[index] = body.isKinematic;
                authoredBodyGravity[index] = body.useGravity;
            }
            authoredStateCaptured = true;
        }

        private void RestoreAuthoredState()
        {
            if (!authoredStateCaptured)
            {
                return;
            }

            transform.SetLocalPositionAndRotation(
                authoredLocalPosition,
                authoredLocalRotation
            );
            transform.localScale = authoredLocalScale;
            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = authoredBodyKinematic[index];
                body.useGravity = authoredBodyGravity[index];
            }
        }

        private void ApplyAvailability(bool available)
        {
            for (int index = 0;
                index < interactionBehaviours.Length;
                index++)
            {
                Behaviour behaviour = interactionBehaviours[index];
                if (behaviour != null && behaviour != this)
                {
                    behaviour.enabled = available;
                }
            }

            for (int index = 0; index < inputColliders.Length; index++)
            {
                Collider inputCollider = inputColliders[index];
                if (inputCollider != null)
                {
                    inputCollider.enabled = available;
                }
            }
        }

        private void OnDisable()
        {
            UnbindAdapterEvents();
        }
    }
}
