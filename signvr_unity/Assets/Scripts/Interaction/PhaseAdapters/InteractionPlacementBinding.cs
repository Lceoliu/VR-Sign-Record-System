using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class InteractionPlacementBinding : MonoBehaviour
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
        private readonly Dictionary<InteractionTargetBinding, HashSet<Collider>>
            overlappingCoinColliders =
                new Dictionary<InteractionTargetBinding, HashSet<Collider>>();

        public string PlateTargetId => plateTargetId;

        public PhaseTwoInteractionAdapter Adapter => adapter;

        public Collider PlacementCollider => placementCollider;

        public Transform SnapPoint => snapPoint;

        private void Awake()
        {
            if (placementCollider == null)
            {
                placementCollider = GetComponent<Collider>();
            }
            placementCollider.isTrigger = true;
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
            UnbindAvailability();
            plateTargetId = string.IsNullOrWhiteSpace(stablePlateTargetId)
                ? throw new ArgumentException(
                    "A stable plate target ID is required.",
                    nameof(stablePlateTargetId)
                )
                : stablePlateTargetId.Trim();
            adapter = phaseAdapter ??
                throw new ArgumentNullException(nameof(phaseAdapter));
            placementCollider = triggerCollider ??
                throw new ArgumentNullException(nameof(triggerCollider));
            snapPoint = targetSnapPoint;
            placementCollider.isTrigger = true;
            BindAvailability();
            ApplyAvailability(adapter.IsEnabled);
        }

        public ValidationResult AcceptPlacement(string coinTargetId)
        {
            if (adapter == null)
            {
                throw new InvalidOperationException(
                    $"{name} has no Phase 2 adapter."
                );
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
            if (coinBinding == null)
            {
                throw new ArgumentNullException(nameof(coinBinding));
            }

            if (!IsAllowedCoinTargetId(coinBinding.TargetId))
            {
                return null;
            }

            ValidationResult result = AcceptPlacement(coinBinding.TargetId);
            if (result.Accepted && snapPoint != null)
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
            if (availabilitySubscribed || adapter == null)
            {
                return;
            }
            adapter.AvailabilityChanged += ApplyAvailability;
            availabilitySubscribed = true;
            adapter.ResetPerformed += ClearOverlaps;
            resetSubscribed = true;
        }

        private void UnbindAvailability()
        {
            if (availabilitySubscribed && adapter != null)
            {
                adapter.AvailabilityChanged -= ApplyAvailability;
            }
            if (resetSubscribed && adapter != null)
            {
                adapter.ResetPerformed -= ClearOverlaps;
            }
            availabilitySubscribed = false;
            resetSubscribed = false;
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

        private void OnDisable()
        {
            UnbindAvailability();
            ClearOverlaps();
        }
    }
}
