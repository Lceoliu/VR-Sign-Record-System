using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class InteractionPlacementBinding : MonoBehaviour
#if UNITY_EDITOR
        , IInteractionOwnedStateTeardown
#endif
    {
        private static readonly HashSet<string> AllowedCoinIds =
            new HashSet<string>(
                new[] { "coin_dragon", "coin_a", "coin_b" },
                StringComparer.Ordinal
            );

        [SerializeField]
        private string plateTargetId = string.Empty;

        [SerializeField]
        private PhaseTwoInteractionAdapter adapter;

        [SerializeField]
        private Collider placementCollider;

        [SerializeField]
        private Transform snapPoint;

        private bool availabilitySubscribed;
        private bool resetSubscribed;

        [SerializeField, HideInInspector]
        private Collider authoredPlacementCollider;

        [SerializeField, HideInInspector]
        private bool authoredColliderEnabled;

        [SerializeField, HideInInspector]
        private bool authoredColliderIsTrigger;

        [SerializeField, HideInInspector]
        private bool authoredColliderStateCaptured;
        private readonly Dictionary<InteractionTargetBinding, HashSet<Collider>>
            overlappingCoinColliders =
                new Dictionary<InteractionTargetBinding, HashSet<Collider>>();

#if UNITY_INCLUDE_TESTS
        private readonly InteractionSubscriptionDiagnostic
            subscriptionDiagnostic =
                new InteractionSubscriptionDiagnostic();
#endif

        public string PlateTargetId => plateTargetId;

        public PhaseTwoInteractionAdapter Adapter => adapter;

        public Collider PlacementCollider => placementCollider;

        public Transform SnapPoint => snapPoint;

#if UNITY_INCLUDE_TESTS
        public InteractionSubscriptionDiagnostic SubscriptionDiagnostic =>
            subscriptionDiagnostic;
#endif

        private void Awake()
        {
            if (placementCollider == null)
            {
                placementCollider = GetComponent<Collider>();
            }
            CapturePlacementOwnership();
            if (placementCollider != null)
            {
                placementCollider.isTrigger = true;
            }
            BindAvailability();
        }

        private void OnEnable()
        {
            BindAvailability();
            ApplyAvailability(adapter != null && adapter.IsEnabled);
        }

        public void Configure(
            string stablePlateTargetId,
            PhaseTwoInteractionAdapter phaseAdapter,
            Collider triggerCollider,
            Transform targetSnapPoint = null)
        {
            string nextPlateTargetId =
                string.IsNullOrWhiteSpace(stablePlateTargetId)
                    ? throw new ArgumentException(
                        "A stable plate target ID is required.",
                        nameof(stablePlateTargetId)
                    )
                    : stablePlateTargetId.Trim();
            PhaseTwoInteractionAdapter nextAdapter = phaseAdapter ??
                throw new ArgumentNullException(nameof(phaseAdapter));
            Collider nextCollider = triggerCollider ??
                throw new ArgumentNullException(nameof(triggerCollider));
            bool colliderChanged = !ReferenceEquals(
                placementCollider,
                nextCollider
            );
            bool manageRuntimeSubscriptions =
                Application.isPlaying && isActiveAndEnabled;
            if (manageRuntimeSubscriptions)
            {
                UnbindAvailability();
            }
            if (colliderChanged)
            {
                ReleasePlacementOwnership();
            }
            ClearOverlaps();
            plateTargetId = nextPlateTargetId;
            adapter = nextAdapter;
            placementCollider = nextCollider;
            snapPoint = targetSnapPoint;
            CapturePlacementOwnership();
            placementCollider.isTrigger = true;
            if (manageRuntimeSubscriptions)
            {
                BindAvailability();
            }
            ApplyAvailability(isActiveAndEnabled && adapter.IsEnabled);
        }

        public ValidationResult AcceptPlacement(string coinTargetId)
        {
            if (!isActiveAndEnabled)
            {
                return null;
            }

            if (adapter == null || !adapter.IsEnabled ||
                string.IsNullOrWhiteSpace(plateTargetId))
            {
                return null;
            }

            if (!IsAllowedCoinTargetId(coinTargetId))
            {
                return null;
            }

            return adapter.AcceptPlacement(
                coinTargetId,
                plateTargetId
            );
        }

        public ValidationResult AcceptPlacement(
            InteractionTargetBinding coinBinding)
        {
            if (coinBinding == null || !isActiveAndEnabled ||
                adapter == null || !adapter.IsEnabled)
            {
                return null;
            }

            if (!IsAllowedCoinTargetId(coinBinding.TargetId))
            {
                return null;
            }

            ValidationResult result = AcceptPlacement(coinBinding.TargetId);
            if (result != null && result.Accepted && snapPoint != null)
            {
                Rigidbody[] bodies =
                    coinBinding.GetComponentsInChildren<Rigidbody>(true);
                for (int index = 0; index < bodies.Length; index++)
                {
                    bodies[index].linearVelocity = Vector3.zero;
                    bodies[index].angularVelocity = Vector3.zero;
                    bodies[index].isKinematic = true;
                }
                coinBinding.transform.SetPositionAndRotation(
                    snapPoint.position,
                    snapPoint.rotation
                );
            }
            return result;
        }

        public static bool IsAllowedCoinTargetId(string targetId)
        {
            return !string.IsNullOrWhiteSpace(targetId) &&
                AllowedCoinIds.Contains(targetId.Trim());
        }

        public ValidationResult AcceptTrigger(Collider other)
        {
            if (!isActiveAndEnabled || other == null || adapter == null ||
                !adapter.IsEnabled)
            {
                return null;
            }

            InteractionTargetBinding coin =
                other.GetComponentInParent<InteractionTargetBinding>();
            if (coin == null || !IsAllowedCoinTargetId(coin.TargetId))
            {
                return null;
            }

            if (!overlappingCoinColliders.TryGetValue(
                    coin,
                    out HashSet<Collider> colliders))
            {
                colliders = new HashSet<Collider>();
                overlappingCoinColliders.Add(coin, colliders);
            }
            bool firstCollider = colliders.Count == 0;
            if (!colliders.Add(other) || !firstCollider)
            {
                return null;
            }

            return AcceptPlacement(coin);
        }

        private void OnTriggerEnter(Collider other)
        {
            AcceptTrigger(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null)
            {
                return;
            }
            InteractionTargetBinding coin =
                other.GetComponentInParent<InteractionTargetBinding>();
            if (coin == null || !overlappingCoinColliders.TryGetValue(
                    coin,
                    out HashSet<Collider> colliders))
            {
                return;
            }
            colliders.Remove(other);
            if (colliders.Count == 0)
            {
                overlappingCoinColliders.Remove(coin);
            }
        }

        private void BindAvailability()
        {
            if (!Application.isPlaying || !isActiveAndEnabled ||
                availabilitySubscribed || adapter == null)
            {
                return;
            }
            adapter.AvailabilityChanged += HandleAvailabilityChanged;
            availabilitySubscribed = true;
            adapter.ResetPerformed += HandleResetPerformed;
            resetSubscribed = true;
        }

        private void UnbindAvailability()
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
            ClearOverlaps();
        }

        private void ApplyAvailability(bool available)
        {
            if (placementCollider != null)
            {
                placementCollider.enabled = available;
            }
            if (!available)
            {
                ClearOverlaps();
            }
        }

        private void ClearOverlaps()
        {
            overlappingCoinColliders.Clear();
        }

        private void CapturePlacementOwnership()
        {
            if (authoredColliderStateCaptured || placementCollider == null)
            {
                return;
            }
            authoredPlacementCollider = placementCollider;
            authoredColliderEnabled = placementCollider.enabled;
            authoredColliderIsTrigger = placementCollider.isTrigger;
            authoredColliderStateCaptured = true;
        }

        private void ReleasePlacementOwnership()
        {
            if (authoredColliderStateCaptured &&
                authoredPlacementCollider != null)
            {
                authoredPlacementCollider.enabled = authoredColliderEnabled;
                authoredPlacementCollider.isTrigger =
                    authoredColliderIsTrigger;
            }
            authoredPlacementCollider = null;
            authoredColliderStateCaptured = false;
        }

        private void OnDisable()
        {
            ApplyAvailability(false);
            UnbindAvailability();
            ClearOverlaps();
        }

        private void OnDestroy()
        {
            UnbindAvailability();
            ClearOverlaps();
            ReleasePlacementOwnership();
        }

#if UNITY_EDITOR
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
            UnbindAvailability();
            ClearOverlaps();
            ReleasePlacementOwnership();
        }
#endif
    }
}
