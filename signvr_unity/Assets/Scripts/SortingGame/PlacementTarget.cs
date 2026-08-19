using System;
using System.Collections.Generic;
using UnityEngine;

namespace SignVR.SortingGame
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PlacementTarget : MonoBehaviour
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
        private string acceptedPlacementId = string.Empty;

        [SerializeField]
        private Transform snapPoint;

        [SerializeField]
        private Renderer indicatorRenderer;

        [SerializeField]
        private Color idleColor = new Color(0.35f, 0.4f, 0.45f, 0.45f);

        [SerializeField]
        private Color correctHoverColor = new Color(0.35f, 0.95f, 0.65f, 0.8f);

        [SerializeField]
        private Color wrongHoverColor = new Color(1f, 0.2f, 0.18f, 0.85f);

        [SerializeField]
        private Color completeColor = new Color(0.25f, 1f, 0.55f, 1f);

        [SerializeField]
        private SortingGameManager gameManager;

        private readonly HashSet<PlacementPiece> candidates =
            new HashSet<PlacementPiece>();
        private MaterialPropertyBlock propertyBlock;
        private PlacementPiece occupant;
        private VisualState visualState = (VisualState)(-1);

        public string AcceptedPlacementId => acceptedPlacementId;
        public bool IsOccupied => occupant != null;

        private void Awake()
        {
            Collider targetCollider = GetComponent<Collider>();
            targetCollider.isTrigger = true;
            SetVisualState(VisualState.Idle);
        }

        private void FixedUpdate()
        {
            if (occupant != null)
            {
                return;
            }

            candidates.RemoveWhere(piece => piece == null || piece.IsPlaced);

            bool hasCorrectCandidate = false;
            bool hasWrongCandidate = false;

            foreach (PlacementPiece piece in candidates)
            {
                if (Matches(piece))
                {
                    hasCorrectCandidate = true;

                    if (!piece.IsGrabbed && TryAccept(piece))
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
            PlacementPiece piece = other.GetComponentInParent<PlacementPiece>();

            if (piece != null)
            {
                candidates.Add(piece);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlacementPiece piece = other.GetComponentInParent<PlacementPiece>();

            if (piece != null)
            {
                candidates.Remove(piece);
            }
        }

        private void OnDisable()
        {
            candidates.Clear();
        }

        public void Configure(
            string acceptedId,
            Transform targetSnapPoint,
            Renderer targetIndicator,
            Color targetIdleColor,
            Color targetCompleteColor,
            SortingGameManager manager
        )
        {
            acceptedPlacementId = acceptedId ?? string.Empty;
            snapPoint = targetSnapPoint;
            indicatorRenderer = targetIndicator;
            idleColor = targetIdleColor;
            correctHoverColor = Color.Lerp(targetIdleColor, Color.white, 0.35f);
            wrongHoverColor = new Color(1f, 0.16f, 0.12f, 0.9f);
            completeColor = targetCompleteColor;
            gameManager = manager;
            SetVisualState(VisualState.Idle, force: true);
        }

        public void SetGameManager(SortingGameManager manager)
        {
            gameManager = manager;
        }

        public bool TryAccept(PlacementPiece piece)
        {
            if (
                occupant != null ||
                !Matches(piece) ||
                piece.IsPlaced ||
                piece.IsGrabbed ||
                snapPoint == null
            )
            {
                return false;
            }

            if (!piece.TryPlaceAt(snapPoint))
            {
                return false;
            }

            occupant = piece;
            SetVisualState(VisualState.Complete);
            gameManager?.NotifyPiecePlaced(piece, this);
            return true;
        }

        public void ResetTarget()
        {
            occupant = null;
            candidates.Clear();
            SetVisualState(VisualState.Idle, force: true);
        }

        private bool Matches(PlacementPiece piece)
        {
            return
                piece != null &&
                string.Equals(
                    piece.PlacementId,
                    acceptedPlacementId,
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
                state == VisualState.Idle ? Color.black : color * 0.4f
            );
            indicatorRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
