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

#if UNITY_INCLUDE_TESTS
        private readonly InteractionSubscriptionDiagnostic
            subscriptionDiagnostic =
                new InteractionSubscriptionDiagnostic();
#endif

        [SerializeField, HideInInspector]
        private bool authoredPoseAndPhysicsCaptured;

        [SerializeField, HideInInspector]
        private Vector3 authoredLocalPosition;

        [SerializeField, HideInInspector]
        private Quaternion authoredLocalRotation;

        [SerializeField, HideInInspector]
        private Vector3 authoredLocalScale;

        [SerializeField, HideInInspector]
        private Rigidbody[] bodies = Array.Empty<Rigidbody>();

        [SerializeField, HideInInspector]
        private bool[] authoredBodyKinematic = Array.Empty<bool>();

        [SerializeField, HideInInspector]
        private bool[] authoredBodyGravity = Array.Empty<bool>();

        [SerializeField, HideInInspector]
        private bool authoredInputStateCaptured;

        [SerializeField, HideInInspector]
        private Behaviour[] authoredInteractionBehaviours =
            Array.Empty<Behaviour>();

        [SerializeField, HideInInspector]
        private bool[] authoredBehaviourEnabled = Array.Empty<bool>();

        [SerializeField, HideInInspector]
        private Collider[] authoredInputColliders = Array.Empty<Collider>();

        [SerializeField, HideInInspector]
        private bool[] authoredColliderEnabled = Array.Empty<bool>();

        public string TargetId => targetId;

        public int PhaseId => adapter != null ? adapter.PhaseId : 0;

        public InteractionPhaseAdapter Adapter => adapter;

        public bool IsAvailable => adapter != null && adapter.IsEnabled;

        public bool IsInputAvailable => isActiveAndEnabled && IsAvailable;

#if UNITY_INCLUDE_TESTS
        public InteractionSubscriptionDiagnostic SubscriptionDiagnostic =>
            subscriptionDiagnostic;
#endif

        private void Awake()
        {
            CaptureAuthoredPoseAndPhysics();
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
            string nextTargetId = string.IsNullOrWhiteSpace(stableTargetId)
                ? throw new ArgumentException(
                    "A stable target ID is required.",
                    nameof(stableTargetId)
                )
                : stableTargetId.Trim();
            InteractionPhaseAdapter nextAdapter = phaseAdapter ??
                throw new ArgumentNullException(nameof(phaseAdapter));
            Behaviour[] nextBehaviours = behaviours ??
                Array.Empty<Behaviour>();
            Collider[] nextColliders = colliders ?? Array.Empty<Collider>();
            bool inputBindingsChanged =
                !HaveSameReferences(
                    interactionBehaviours,
                    nextBehaviours
                ) ||
                !HaveSameReferences(inputColliders, nextColliders);
            bool manageRuntimeSubscriptions =
                Application.isPlaying && isActiveAndEnabled;
            if (manageRuntimeSubscriptions)
            {
                UnbindAdapterEvents();
            }
            if (inputBindingsChanged)
            {
                RestoreAuthoredInputState();
                authoredInputStateCaptured = false;
            }
            targetId = nextTargetId;
            adapter = nextAdapter;
            interactionBehaviours = nextBehaviours;
            inputColliders = nextColliders;
            CaptureAuthoredPoseAndPhysics();
            CaptureAuthoredInputState();
            if (manageRuntimeSubscriptions)
            {
                BindAdapterEvents();
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
                    $"{name} has no Interaction Phase adapter."
                );
            }

            if (string.IsNullOrWhiteSpace(targetId))
            {
                throw new InvalidOperationException(
                    $"{name} has no stable target ID."
                );
            }
            if (!adapter.IsEnabled)
            {
                return null;
            }

            return adapter.AcceptTarget(targetId);
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal void RefreshAvailabilityWithoutRuntimeSubscription()
        {
            ApplyAvailability(IsInputAvailable);
        }
#endif

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
            if (!Application.isPlaying || !isActiveAndEnabled ||
                adapter == null)
            {
                return;
            }

            if (!availabilitySubscribed)
            {
                adapter.AvailabilityChanged += HandleAvailabilityChanged;
                availabilitySubscribed = true;
            }
            if (!resetSubscribed)
            {
                adapter.ResetPerformed += HandleResetPerformed;
                resetSubscribed = true;
            }
        }

        private void UnbindAdapterEvents()
        {
            if (availabilitySubscribed && adapter != null)
            {
                adapter.AvailabilityChanged -= HandleAvailabilityChanged;
            }
            if (resetSubscribed && adapter != null)
            {
                adapter.ResetPerformed -= HandleResetPerformed;
            }
            availabilitySubscribed = false;
            resetSubscribed = false;
        }

        private void CaptureAuthoredPoseAndPhysics()
        {
            if (authoredPoseAndPhysicsCaptured)
            {
                return;
            }

            authoredLocalPosition = transform.localPosition;
            authoredLocalRotation = transform.localRotation;
            authoredLocalScale = transform.localScale;
            bodies = GetComponentsInChildren<Rigidbody>(true);
            authoredBodyKinematic = new bool[bodies.Length];
            authoredBodyGravity = new bool[bodies.Length];
            int bodyCount = Math.Min(
                bodies?.Length ?? 0,
                Math.Min(
                    authoredBodyKinematic?.Length ?? 0,
                    authoredBodyGravity?.Length ?? 0
                )
            );
            for (int index = 0; index < bodyCount; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                authoredBodyKinematic[index] = body.isKinematic;
                authoredBodyGravity[index] = body.useGravity;
            }
            authoredPoseAndPhysicsCaptured = true;
        }

        private void CaptureAuthoredInputState()
        {
            if (authoredInputStateCaptured)
            {
                return;
            }

            authoredInteractionBehaviours =
                (Behaviour[])interactionBehaviours.Clone();
            authoredBehaviourEnabled =
                new bool[authoredInteractionBehaviours.Length];
            for (int index = 0;
                index < authoredInteractionBehaviours.Length;
                index++)
            {
                Behaviour behaviour = authoredInteractionBehaviours[index];
                if (behaviour != null)
                {
                    authoredBehaviourEnabled[index] = behaviour.enabled;
                }
            }

            authoredInputColliders = (Collider[])inputColliders.Clone();
            authoredColliderEnabled =
                new bool[authoredInputColliders.Length];
            for (int index = 0;
                index < authoredInputColliders.Length;
                index++)
            {
                Collider inputCollider = authoredInputColliders[index];
                if (inputCollider != null)
                {
                    authoredColliderEnabled[index] = inputCollider.enabled;
                }
            }
            authoredInputStateCaptured = true;
        }

        private void RestoreAuthoredPoseAndPhysics()
        {
            if (!authoredPoseAndPhysicsCaptured)
            {
                return;
            }

            transform.SetLocalPositionAndRotation(
                authoredLocalPosition,
                authoredLocalRotation
            );
            transform.localScale = authoredLocalScale;
            int bodyCount = Math.Min(
                bodies?.Length ?? 0,
                Math.Min(
                    authoredBodyKinematic?.Length ?? 0,
                    authoredBodyGravity?.Length ?? 0
                )
            );
            for (int index = 0; index < bodyCount; index++)
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

        private void RestoreAuthoredInputState()
        {
            if (!authoredInputStateCaptured)
            {
                return;
            }

            int behaviourCount = Math.Min(
                authoredInteractionBehaviours?.Length ?? 0,
                authoredBehaviourEnabled?.Length ?? 0
            );
            for (int index = 0; index < behaviourCount; index++)
            {
                Behaviour behaviour = authoredInteractionBehaviours[index];
                if (behaviour != null && behaviour != this)
                {
                    behaviour.enabled = authoredBehaviourEnabled[index];
                }
            }

            int colliderCount = Math.Min(
                authoredInputColliders?.Length ?? 0,
                authoredColliderEnabled?.Length ?? 0
            );
            for (int index = 0; index < colliderCount; index++)
            {
                Collider inputCollider = authoredInputColliders[index];
                if (inputCollider != null)
                {
                    inputCollider.enabled = authoredColliderEnabled[index];
                }
            }
        }

        private void RestoreAuthoredStateForTeardown()
        {
            UnbindAdapterEvents();
            RestoreAuthoredPoseAndPhysics();
            RestoreAuthoredInputState();
        }

        private static bool HaveSameReferences<T>(T[] left, T[] right)
            where T : UnityEngine.Object
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int index = 0; index < left.Length; index++)
            {
                if (!ReferenceEquals(left[index], right[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private void HandleAvailabilityChanged(bool available)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordAvailabilityChanged();
#endif
            ApplyAvailability(available);
        }

        private void HandleResetPerformed()
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordResetPerformed();
#endif
            RestoreAuthoredPoseAndPhysics();
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
            ApplyAvailability(false);
            UnbindAdapterEvents();
        }

        private void OnDestroy()
        {
            RestoreAuthoredStateForTeardown();
        }
    }
}
