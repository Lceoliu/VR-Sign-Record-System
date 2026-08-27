using System;
using System.Collections.Generic;
using Oculus.Interaction;
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
        private readonly HashSet<InteractionTargetBinding>
            attemptedOverlappingCoins =
                new HashSet<InteractionTargetBinding>();
        private readonly Dictionary<InteractionTargetBinding,
            CoinSelectionState> coinSelectionStates =
                new Dictionary<InteractionTargetBinding,
                    CoinSelectionState>();
        private uint acceptanceGeneration;

        private sealed class CoinSelectionState : IDisposable
        {
            private readonly List<SelectionViewSubscription> views =
                new List<SelectionViewSubscription>();

            public int Count => views.Count;

            public bool Contains(IInteractableView view)
            {
                for (int index = 0; index < views.Count; index++)
                {
                    if (ReferenceEquals(views[index].View, view))
                    {
                        return true;
                    }
                }
                return false;
            }

            public void Add(IInteractableView view)
            {
                if (!IsUnityObjectAlive(view) || Contains(view))
                {
                    return;
                }
                SelectionViewSubscription subscription =
                    SelectionViewSubscription.TryCreate(view);
                if (subscription != null)
                {
                    views.Add(subscription);
                }
            }

            public bool IsSelected()
            {
                for (int index = views.Count - 1; index >= 0; index--)
                {
                    SelectionViewSubscription subscription = views[index];
                    if (!IsUnityObjectAlive(subscription.View))
                    {
                        subscription.Dispose();
                        views.RemoveAt(index);
                        continue;
                    }
                    if (subscription.IsSelected())
                    {
                        return true;
                    }
                }
                return false;
            }

            public void Dispose()
            {
                for (int index = views.Count - 1; index >= 0; index--)
                {
                    views[index].Dispose();
                }
                views.Clear();
            }
        }

        private sealed class SelectionViewSubscription : IDisposable
        {
            private readonly List<IInteractorView> selectingInteractors =
                new List<IInteractorView>();
            private readonly Action<IInteractorView> addedHandler;
            private readonly Action<IInteractorView> removedHandler;
            private int anonymousSelectionCount;
            private bool subscribed;

            private SelectionViewSubscription(IInteractableView view)
            {
                View = view;
                addedHandler = HandleAdded;
                removedHandler = HandleRemoved;
            }

            public IInteractableView View { get; }

            public static SelectionViewSubscription TryCreate(
                IInteractableView view)
            {
                if (!IsUnityObjectAlive(view))
                {
                    return null;
                }

                var subscription = new SelectionViewSubscription(view);
                try
                {
                    IEnumerable<IInteractorView> current =
                        view.SelectingInteractorViews;
                    if (current != null)
                    {
                        foreach (IInteractorView interactor in current)
                        {
                            subscription.HandleAdded(interactor);
                        }
                    }
                    view.WhenSelectingInteractorViewAdded +=
                        subscription.addedHandler;
                    subscription.subscribed = true;
                    view.WhenSelectingInteractorViewRemoved +=
                        subscription.removedHandler;
                    return subscription;
                }
                catch (MissingReferenceException)
                {
                    subscription.Dispose();
                    return null;
                }
            }

            public bool IsSelected()
            {
                if (anonymousSelectionCount > 0)
                {
                    return true;
                }
                for (int index = selectingInteractors.Count - 1;
                    index >= 0;
                    index--)
                {
                    if (!IsUnityObjectAlive(selectingInteractors[index]))
                    {
                        selectingInteractors.RemoveAt(index);
                    }
                }
                return selectingInteractors.Count > 0;
            }

            public void Dispose()
            {
                if (subscribed && IsUnityObjectAlive(View))
                {
                    try
                    {
                        View.WhenSelectingInteractorViewAdded -= addedHandler;
                        View.WhenSelectingInteractorViewRemoved -=
                            removedHandler;
                    }
                    catch (MissingReferenceException)
                    {
                        // The Unity component was destroyed between the
                        // liveness check and event removal.
                    }
                }
                subscribed = false;
                selectingInteractors.Clear();
                anonymousSelectionCount = 0;
            }

            private void HandleAdded(IInteractorView interactor)
            {
                if (ReferenceEquals(interactor, null))
                {
                    anonymousSelectionCount++;
                    return;
                }
                if (!selectingInteractors.Contains(interactor))
                {
                    selectingInteractors.Add(interactor);
                }
            }

            private void HandleRemoved(IInteractorView interactor)
            {
                if (ReferenceEquals(interactor, null))
                {
                    anonymousSelectionCount = Math.Max(
                        0,
                        anonymousSelectionCount - 1
                    );
                    return;
                }
                selectingInteractors.Remove(interactor);
            }
        }

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
            InvalidatePendingAcceptance();
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
            if (IsSelectedByInteractor(coinBinding))
            {
                return null;
            }

            uint generation = acceptanceGeneration;
            ValidationResult result = AcceptPlacement(coinBinding.TargetId);
            if (result != null && result.Accepted &&
                generation == acceptanceGeneration &&
                coinBinding != null && snapPoint != null)
            {
                Rigidbody[] bodies =
                    coinBinding.GetComponentsInChildren<Rigidbody>(true);
                for (int index = 0; index < bodies.Length; index++)
                {
                    Rigidbody body = bodies[index];
                    if (body == null || body.isKinematic)
                    {
                        continue;
                    }
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
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

            CacheSelectionViews(coin);
            return null;
        }

        public ValidationResult TryAcceptStay(Collider other)
        {
            if (!isActiveAndEnabled || other == null || adapter == null ||
                !adapter.IsEnabled)
            {
                return null;
            }

            InteractionTargetBinding coin =
                other.GetComponentInParent<InteractionTargetBinding>();
            if (coin == null || !overlappingCoinColliders.TryGetValue(
                    coin,
                    out HashSet<Collider> colliders) ||
                !colliders.Contains(other) ||
                attemptedOverlappingCoins.Contains(coin) ||
                IsSelectedByInteractor(coin))
            {
                return null;
            }

            attemptedOverlappingCoins.Add(coin);
            return AcceptPlacement(coin);
        }

        private void OnTriggerEnter(Collider other)
        {
            AcceptTrigger(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryAcceptStay(other);
        }

        private void OnTriggerExit(Collider other)
        {
            ReleaseTrigger(other);
        }

        public void ReleaseTrigger(Collider other)
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
                attemptedOverlappingCoins.Remove(coin);
                ReleaseSelectionState(coin);
            }
        }

        private bool IsSelectedByInteractor(
            InteractionTargetBinding coin)
        {
            CacheSelectionViews(coin);
            if (!coinSelectionStates.TryGetValue(
                    coin,
                    out CoinSelectionState state))
            {
                return false;
            }
            return state.IsSelected();
        }

        private void CacheSelectionViews(InteractionTargetBinding coin)
        {
            if (coin == null || coinSelectionStates.ContainsKey(coin))
            {
                return;
            }

            var state = new CoinSelectionState();
            IReadOnlyList<Behaviour> configuredBehaviours =
                coin.InteractionBehaviours;
            for (int index = 0; index < configuredBehaviours.Count; index++)
            {
                if (configuredBehaviours[index] is IInteractableView view &&
                    IsUnityObjectAlive(view))
                {
                    state.Add(view);
                }
            }
            if (state.Count == 0)
            {
                MonoBehaviour[] components =
                    coin.GetComponentsInChildren<MonoBehaviour>(true);
                for (int index = 0; index < components.Length; index++)
                {
                    if (components[index] is IInteractableView view &&
                        IsUnityObjectAlive(view))
                    {
                        state.Add(view);
                    }
                }
            }
            coinSelectionStates.Add(coin, state);
        }

        private void ReleaseSelectionState(InteractionTargetBinding coin)
        {
            if (!ReferenceEquals(coin, null) &&
                coinSelectionStates.TryGetValue(
                    coin,
                    out CoinSelectionState state))
            {
                state.Dispose();
                coinSelectionStates.Remove(coin);
            }
        }

        private static bool IsUnityObjectAlive(object value)
        {
            if (ReferenceEquals(value, null))
            {
                return false;
            }
            return !(value is UnityEngine.Object unityObject) ||
                unityObject != null;
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
            InvalidatePendingAcceptance();
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
            foreach (CoinSelectionState state in coinSelectionStates.Values)
            {
                state.Dispose();
            }
            overlappingCoinColliders.Clear();
            attemptedOverlappingCoins.Clear();
            coinSelectionStates.Clear();
        }

        private void InvalidatePendingAcceptance()
        {
            acceptanceGeneration++;
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
            InvalidatePendingAcceptance();
            ApplyAvailability(false);
            UnbindAvailability();
            ClearOverlaps();
        }

        private void OnDestroy()
        {
            InvalidatePendingAcceptance();
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
