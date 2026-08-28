using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class InteractionTargetBinding :
        MonoBehaviour,
        IInteractionTriggerInput,
        IInteractionContactCycleInput
#if UNITY_EDITOR
        , IInteractionOwnedStateTeardown
#endif
    {
        [Flags]
        private enum ContactSource
        {
            None = 0,
            TriggerRelay = 1 << 0,
            Poke = 1 << 1,
            Trigger = 1 << 2,
            Grab = 1 << 3
        }

        [SerializeField]
        private string targetId = string.Empty;

        [SerializeField]
        private InteractionPhaseAdapter adapter;

        [SerializeField]
        private Behaviour[] interactionBehaviours = Array.Empty<Behaviour>();

        [SerializeField]
        private Collider[] inputColliders = Array.Empty<Collider>();

        [SerializeField]
        private GameObject[] availabilityObjects = Array.Empty<GameObject>();

        [SerializeField]
        private bool enableMovablePhysicsWhenAvailable;

        [SerializeField, Min(0f)]
        private float sameTargetCooldownSeconds = 0.3f;

        private float lastAcceptedInputTime = float.NegativeInfinity;
        private int triggerRelayContactCount;
        private int pokeContactCount;
        private int triggerContactCount;
        private int grabContactCount;
        private bool contactCycleLatched;

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
        private bool[] authoredBodyDetectCollisions = Array.Empty<bool>();

        [SerializeField, HideInInspector]
        private RigidbodyConstraints[] authoredBodyConstraints =
            Array.Empty<RigidbodyConstraints>();

        [SerializeField, HideInInspector]
        private RigidbodyInterpolation[] authoredBodyInterpolation =
            Array.Empty<RigidbodyInterpolation>();

        [SerializeField, HideInInspector]
        private CollisionDetectionMode[] authoredBodyCollisionDetection =
            Array.Empty<CollisionDetectionMode>();

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

        [SerializeField, HideInInspector]
        private GameObject[] authoredAvailabilityObjects =
            Array.Empty<GameObject>();

        [SerializeField, HideInInspector]
        private bool[] authoredAvailabilityObjectActive = Array.Empty<bool>();

        public string TargetId => targetId;

        public int PhaseId => adapter != null ? adapter.PhaseId : 0;

        public InteractionPhaseAdapter Adapter => adapter;

        public bool IsAvailable => adapter != null && adapter.IsEnabled;

        public bool IsInputAvailable => isActiveAndEnabled && IsAvailable;

        public IReadOnlyList<Behaviour> InteractionBehaviours =>
            interactionBehaviours;

        public IReadOnlyList<Collider> InputColliders => inputColliders;

        public IReadOnlyList<GameObject> AvailabilityObjects =>
            availabilityObjects;

        public bool EnablesMovablePhysicsWhenAvailable =>
            enableMovablePhysicsWhenAvailable;

        public float SameTargetCooldownSeconds => sameTargetCooldownSeconds;

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
            ConfigureInternal(
                stableTargetId,
                phaseAdapter,
                behaviours,
                colliders,
                Array.Empty<GameObject>(),
                false
            );
        }

        public void ConfigureMovable(
            string stableTargetId,
            InteractionPhaseAdapter phaseAdapter,
            Behaviour[] behaviours,
            Collider[] colliders,
            GameObject[] objectsToActivate)
        {
            ConfigureInternal(
                stableTargetId,
                phaseAdapter,
                behaviours,
                colliders,
                objectsToActivate,
                true
            );
        }

        private void ConfigureInternal(
            string stableTargetId,
            InteractionPhaseAdapter phaseAdapter,
            Behaviour[] behaviours,
            Collider[] colliders,
            GameObject[] objectsToActivate,
            bool enableMovablePhysics)
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
            GameObject[] nextAvailabilityObjects = objectsToActivate ??
                Array.Empty<GameObject>();
            bool inputBindingsChanged =
                !HaveSameReferences(
                    interactionBehaviours,
                    nextBehaviours
                ) ||
                !HaveSameReferences(inputColliders, nextColliders) ||
                !HaveSameReferences(
                    availabilityObjects,
                    nextAvailabilityObjects
                ) ||
                enableMovablePhysicsWhenAvailable != enableMovablePhysics;
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
            availabilityObjects = nextAvailabilityObjects;
            enableMovablePhysicsWhenAvailable = enableMovablePhysics;
            ResetInputGate();
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

            if (ActiveContactCount > 0 && contactCycleLatched)
            {
                return null;
            }
            if (Time.unscaledTime - lastAcceptedInputTime <
                sameTargetCooldownSeconds)
            {
                return null;
            }

            ValidationResult result = adapter.AcceptTarget(targetId);
            if (result != null)
            {
                lastAcceptedInputTime = Time.unscaledTime;
                if (ActiveContactCount > 0)
                {
                    contactCycleLatched = true;
                }
            }
            return result;
        }

        public void ConfigureSameTargetCooldown(float seconds)
        {
            sameTargetCooldownSeconds = float.IsNaN(seconds)
                ? 0f
                : Mathf.Max(0f, seconds);
        }

        public ValidationResult BeginContactCycle()
        {
            return BeginContact(ContactSource.TriggerRelay);
        }

        public void EndContactCycle()
        {
            EndContact(ContactSource.TriggerRelay);
        }

        private void ResetInputGate()
        {
            triggerRelayContactCount = 0;
            pokeContactCount = 0;
            triggerContactCount = 0;
            grabContactCount = 0;
            contactCycleLatched = false;
            lastAcceptedInputTime = float.NegativeInfinity;
        }

        private ValidationResult BeginContact(ContactSource source)
        {
            bool isFirstSource = ActiveContactCount == 0;
            IncrementContactCount(source);
            if (!isFirstSource)
            {
                return null;
            }

            ValidationResult result = AcceptInput();
            contactCycleLatched = true;
            return result;
        }

        private void EndContact(ContactSource source)
        {
            DecrementContactCount(source);
            if (ActiveContactCount == 0)
            {
                contactCycleLatched = false;
            }
        }

        private int ActiveContactCount =>
            triggerRelayContactCount + pokeContactCount +
            triggerContactCount + grabContactCount;

        private void IncrementContactCount(ContactSource source)
        {
            switch (source)
            {
                case ContactSource.TriggerRelay:
                    triggerRelayContactCount++;
                    break;
                case ContactSource.Poke:
                    pokeContactCount++;
                    break;
                case ContactSource.Trigger:
                    triggerContactCount++;
                    break;
                case ContactSource.Grab:
                    grabContactCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source));
            }
        }

        private void DecrementContactCount(ContactSource source)
        {
            switch (source)
            {
                case ContactSource.TriggerRelay:
                    triggerRelayContactCount = Math.Max(
                        0,
                        triggerRelayContactCount - 1
                    );
                    break;
                case ContactSource.Poke:
                    pokeContactCount = Math.Max(0, pokeContactCount - 1);
                    break;
                case ContactSource.Trigger:
                    triggerContactCount = Math.Max(
                        0,
                        triggerContactCount - 1
                    );
                    break;
                case ContactSource.Grab:
                    grabContactCount = Math.Max(0, grabContactCount - 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source));
            }
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
            BeginContact(ContactSource.Poke);
        }

        public void PokeEnded()
        {
            EndContact(ContactSource.Poke);
        }

        public void Trigger()
        {
            BeginContact(ContactSource.Trigger);
        }

        public void TriggerEnded()
        {
            EndContact(ContactSource.Trigger);
        }

        public void Grab()
        {
            BeginContact(ContactSource.Grab);
        }

        public void GrabEnded()
        {
            EndContact(ContactSource.Grab);
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
            Rigidbody[] discoveredBodies =
                GetComponentsInChildren<Rigidbody>(true);
            int serializedBodyCount = discoveredBodies.Length;
            if (authoredPoseAndPhysicsCaptured &&
                HaveSameReferences(bodies, discoveredBodies) &&
                (authoredBodyKinematic?.Length ?? 0) == serializedBodyCount &&
                (authoredBodyGravity?.Length ?? 0) == serializedBodyCount &&
                (authoredBodyDetectCollisions?.Length ?? 0) ==
                    serializedBodyCount &&
                (authoredBodyConstraints?.Length ?? 0) ==
                    serializedBodyCount &&
                (authoredBodyInterpolation?.Length ?? 0) ==
                    serializedBodyCount &&
                (authoredBodyCollisionDetection?.Length ?? 0) ==
                    serializedBodyCount)
            {
                return;
            }

            authoredLocalPosition = transform.localPosition;
            authoredLocalRotation = transform.localRotation;
            authoredLocalScale = transform.localScale;
            bodies = discoveredBodies;
            authoredBodyKinematic = new bool[bodies.Length];
            authoredBodyGravity = new bool[bodies.Length];
            authoredBodyDetectCollisions = new bool[bodies.Length];
            authoredBodyConstraints = new RigidbodyConstraints[bodies.Length];
            authoredBodyInterpolation =
                new RigidbodyInterpolation[bodies.Length];
            authoredBodyCollisionDetection =
                new CollisionDetectionMode[bodies.Length];
            int bodyCount = Math.Min(
                bodies?.Length ?? 0,
                Math.Min(
                    authoredBodyKinematic?.Length ?? 0,
                    Math.Min(
                        authoredBodyGravity?.Length ?? 0,
                        Math.Min(
                            authoredBodyDetectCollisions?.Length ?? 0,
                            Math.Min(
                                authoredBodyConstraints?.Length ?? 0,
                                Math.Min(
                                    authoredBodyInterpolation?.Length ?? 0,
                                    authoredBodyCollisionDetection?.Length ?? 0
                                )
                            )
                        )
                    )
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
                authoredBodyDetectCollisions[index] = body.detectCollisions;
                authoredBodyConstraints[index] = body.constraints;
                authoredBodyInterpolation[index] = body.interpolation;
                authoredBodyCollisionDetection[index] =
                    body.collisionDetectionMode;
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

            authoredAvailabilityObjects =
                (GameObject[])availabilityObjects.Clone();
            authoredAvailabilityObjectActive =
                new bool[authoredAvailabilityObjects.Length];
            for (int index = 0;
                index < authoredAvailabilityObjects.Length;
                index++)
            {
                GameObject availabilityObject =
                    authoredAvailabilityObjects[index];
                if (availabilityObject != null)
                {
                    authoredAvailabilityObjectActive[index] =
                        availabilityObject.activeSelf;
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
            RestoreAuthoredPhysics();
        }

        private void RestoreAuthoredPhysics()
        {
            if (!authoredPoseAndPhysicsCaptured)
            {
                return;
            }

            int bodyCount = Math.Min(
                bodies?.Length ?? 0,
                Math.Min(
                    authoredBodyKinematic?.Length ?? 0,
                    Math.Min(
                        authoredBodyGravity?.Length ?? 0,
                        Math.Min(
                            authoredBodyDetectCollisions?.Length ?? 0,
                            Math.Min(
                                authoredBodyConstraints?.Length ?? 0,
                                Math.Min(
                                    authoredBodyInterpolation?.Length ?? 0,
                                    authoredBodyCollisionDetection?.Length ?? 0
                                )
                            )
                        )
                    )
                )
            );
            for (int index = 0; index < bodyCount; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                if (authoredBodyKinematic[index])
                {
                    if (!body.isKinematic)
                    {
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                        body.isKinematic = true;
                    }
                }
                else
                {
                    body.isKinematic = false;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.useGravity = authoredBodyGravity[index];
                body.detectCollisions =
                    authoredBodyDetectCollisions[index];
                body.constraints = authoredBodyConstraints[index];
                body.interpolation = authoredBodyInterpolation[index];
                body.collisionDetectionMode =
                    authoredBodyCollisionDetection[index];
            }
        }

        private void ApplyMovablePhysics()
        {
            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.detectCollisions = true;
                body.interpolation = index < authoredBodyInterpolation.Length
                    ? authoredBodyInterpolation[index]
                    : RigidbodyInterpolation.None;
                body.collisionDetectionMode =
                    index < authoredBodyCollisionDetection.Length
                        ? authoredBodyCollisionDetection[index]
                        : CollisionDetectionMode.Discrete;
                body.useGravity = false;
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
            int objectCount = Math.Min(
                authoredAvailabilityObjects?.Length ?? 0,
                authoredAvailabilityObjectActive?.Length ?? 0
            );
            for (int index = 0; index < objectCount; index++)
            {
                GameObject availabilityObject =
                    authoredAvailabilityObjects[index];
                if (availabilityObject != null)
                {
                    availabilityObject.SetActive(
                        authoredAvailabilityObjectActive[index]
                    );
                }
            }
        }

        private void RestoreAuthoredStateForTeardown()
        {
            UnbindAdapterEvents();
            RestoreAuthoredPoseAndPhysics();
            RestoreAuthoredInputState();
            authoredPoseAndPhysicsCaptured = false;
            authoredInputStateCaptured = false;
        }

#if UNITY_EDITOR
        public bool HasCapturedAuthoredPose =>
            authoredPoseAndPhysicsCaptured;

        public Vector3 CapturedAuthoredLocalPosition =>
            authoredLocalPosition;

        public Quaternion CapturedAuthoredLocalRotation =>
            authoredLocalRotation;

        public void RestoreCapturedAuthoredPoseForEditorSetup()
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "Editor setup cannot restore a target during Play Mode."
                );
            }
            if (!authoredPoseAndPhysicsCaptured)
            {
                CaptureAuthoredPoseAndPhysics();
            }
            transform.SetLocalPositionAndRotation(
                authoredLocalPosition,
                authoredLocalRotation
            );
            transform.localScale = authoredLocalScale;
        }

        void IInteractionOwnedStateTeardown
            .ReleaseOwnedStateForEditorTeardown()
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "Editor teardown is forbidden during Play Mode."
                );
            }
            enabled = false;
            RestoreAuthoredStateForTeardown();
        }
#endif

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
            ResetInputGate();
            RestoreAuthoredPoseAndPhysics();
            ApplyAvailability(IsInputAvailable);
        }

        private void ApplyAvailability(bool available)
        {
            if (available)
            {
                for (int index = 0;
                    index < availabilityObjects.Length;
                    index++)
                {
                    GameObject availabilityObject =
                        availabilityObjects[index];
                    if (availabilityObject != null)
                    {
                        availabilityObject.SetActive(true);
                    }
                }
            }

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

            if (enableMovablePhysicsWhenAvailable)
            {
                if (available)
                {
                    ApplyMovablePhysics();
                }
                else
                {
                    RestoreAuthoredPhysics();
                }
            }

            if (!available)
            {
                for (int index = 0;
                    index < availabilityObjects.Length;
                    index++)
                {
                    GameObject availabilityObject =
                        availabilityObjects[index];
                    if (availabilityObject != null)
                    {
                        availabilityObject.SetActive(false);
                    }
                }
            }
        }

        private void OnDisable()
        {
            ApplyAvailability(false);
            UnbindAdapterEvents();
            ResetInputGate();
        }

        private void OnDestroy()
        {
            ResetInputGate();
            RestoreAuthoredStateForTeardown();
        }
    }
}
