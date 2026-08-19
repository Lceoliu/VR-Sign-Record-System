using System;
using System.Collections.Generic;
using UnityEngine;

namespace SignVR.CoopRelay
{
    /// <summary>
    /// Accepts one matching relay item when this socket is the next step in
    /// the manager's sequence.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class CoopRelaySocket : MonoBehaviour
    {
        private enum VisualState
        {
            Idle,
            CorrectHover,
            WrongHover,
            Complete
        }

        private static readonly int BaseColorProperty =
            Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorProperty =
            Shader.PropertyToID("_EmissionColor");

        [SerializeField]
        private string acceptedItemId = string.Empty;

        [SerializeField, Min(0)]
        private int sequenceIndex;

        [SerializeField]
        private Transform snapPoint;

        [SerializeField]
        private Renderer indicatorRenderer;

        [SerializeField]
        private Color idleColor = new Color(0.24f, 0.3f, 0.34f, 0.75f);

        [SerializeField]
        private Color correctHoverColor = new Color(0.2f, 0.9f, 0.55f, 1f);

        [SerializeField]
        private Color wrongHoverColor = new Color(1f, 0.22f, 0.12f, 1f);

        [SerializeField]
        private Color completeColor = new Color(0.08f, 0.95f, 0.72f, 1f);

        [SerializeField]
        private CoopRelayGameManager gameManager;

        private readonly HashSet<CoopRelayItem> candidates =
            new HashSet<CoopRelayItem>();
        private MaterialPropertyBlock propertyBlock;
        private CoopRelayItem occupant;
        private VisualState visualState = (VisualState)(-1);

        public string AcceptedItemId => acceptedItemId;
        public int SequenceIndex => sequenceIndex;
        public bool IsOccupied => occupant != null;
        public CoopRelayItem Occupant => occupant;
        public Transform SnapPoint => snapPoint;

        private void Awake()
        {
            Collider socketCollider = GetComponent<Collider>();
            socketCollider.isTrigger = true;

            if (snapPoint == null)
            {
                snapPoint = transform;
            }

            SetVisualState(VisualState.Idle, force: true);
        }

        private void FixedUpdate()
        {
            if (occupant != null)
            {
                return;
            }

            candidates.RemoveWhere(
                item => item == null || item.IsDocked
            );

            bool hasCorrectCandidate = false;
            bool hasWrongCandidate = false;

            foreach (CoopRelayItem item in candidates)
            {
                bool idMatches = Matches(item);
                bool canAccept =
                    idMatches &&
                    (gameManager == null ||
                        gameManager.CanAcceptSocket(this, item));

                if (canAccept)
                {
                    hasCorrectCandidate = true;

                    if (!item.IsGrabbed && TryAccept(item))
                    {
                        return;
                    }
                }
                else
                {
                    hasWrongCandidate = true;
                }
            }

            SetVisualState(
                hasWrongCandidate
                    ? VisualState.WrongHover
                    : hasCorrectCandidate
                        ? VisualState.CorrectHover
                        : VisualState.Idle
            );
        }

        private void OnTriggerEnter(Collider other)
        {
            CoopRelayItem item =
                other.GetComponentInParent<CoopRelayItem>();

            if (item != null)
            {
                candidates.Add(item);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            CoopRelayItem item =
                other.GetComponentInParent<CoopRelayItem>();

            if (item != null)
            {
                candidates.Remove(item);
            }
        }

        private void OnDisable()
        {
            candidates.Clear();
        }

        public void Configure(
            string acceptedId,
            int orderIndex,
            Transform targetSnapPoint,
            Renderer targetIndicator,
            CoopRelayGameManager manager
        )
        {
            acceptedItemId = acceptedId ?? string.Empty;
            sequenceIndex = Mathf.Max(0, orderIndex);
            snapPoint = targetSnapPoint != null
                ? targetSnapPoint
                : transform;
            indicatorRenderer = targetIndicator;
            gameManager = manager;
            SetVisualState(VisualState.Idle, force: true);
        }

        public void SetGameManager(CoopRelayGameManager manager)
        {
            gameManager = manager;
        }

        public bool TryAccept(CoopRelayItem item)
        {
            if (
                occupant != null ||
                !Matches(item) ||
                item.IsDocked ||
                item.IsGrabbed ||
                snapPoint == null ||
                (gameManager != null &&
                    !gameManager.CanAcceptSocket(this, item))
            )
            {
                return false;
            }

            if (!item.TryDock(snapPoint))
            {
                return false;
            }

            occupant = item;
            candidates.Clear();
            SetVisualState(VisualState.Complete);
            gameManager?.NotifyItemDocked(item, this);
            return true;
        }

        public void ResetSocket()
        {
            occupant = null;
            candidates.Clear();
            SetVisualState(VisualState.Idle, force: true);
        }

        private bool Matches(CoopRelayItem item)
        {
            return
                item != null &&
                string.Equals(
                    item.ItemId,
                    acceptedItemId,
                    StringComparison.Ordinal
                );
        }

        private void SetVisualState(VisualState state, bool force = false)
        {
            if (!force && visualState == state)
            {
                return;
            }

            visualState = state;

            if (indicatorRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            indicatorRenderer.GetPropertyBlock(propertyBlock);

            Color color = state switch
            {
                VisualState.CorrectHover => correctHoverColor,
                VisualState.WrongHover => wrongHoverColor,
                VisualState.Complete => completeColor,
                _ => idleColor
            };

            propertyBlock.SetColor(BaseColorProperty, color);
            propertyBlock.SetColor(
                EmissionColorProperty,
                state == VisualState.Idle ? Color.black : color * 0.45f
            );
            indicatorRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
