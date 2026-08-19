using TMPro;
using UnityEngine;

namespace SignVR.SortingGame
{
    [DisallowMultipleComponent]
    public sealed class SortingGameManager : MonoBehaviour
    {
        [SerializeField]
        private PlacementPiece[] pieces = System.Array.Empty<PlacementPiece>();

        [SerializeField]
        private PlacementTarget[] targets = System.Array.Empty<PlacementTarget>();

        [SerializeField]
        private TMP_Text statusLabel;

        [SerializeField]
        private float resetBelowHeight = -0.5f;

        private bool roundComplete;

        public int PlacedCount { get; private set; }
        public int PieceCount => pieces?.Length ?? 0;
        public bool RoundComplete => roundComplete;
        public float ResetBelowHeight => resetBelowHeight;

        private void Awake()
        {
            BindTargets();
            UpdateStatusLabel();
        }

        private void Update()
        {
            if (roundComplete)
            {
                return;
            }

            foreach (PlacementPiece piece in pieces)
            {
                if (
                    piece != null &&
                    !piece.IsPlaced &&
                    !piece.IsGrabbed &&
                    piece.transform.position.y < resetBelowHeight
                )
                {
                    piece.ResetToSpawn();
                }
            }
        }

        public void Configure(
            PlacementPiece[] roundPieces,
            PlacementTarget[] roundTargets,
            TMP_Text roundStatusLabel
        )
        {
            pieces = roundPieces ?? System.Array.Empty<PlacementPiece>();
            targets = roundTargets ?? System.Array.Empty<PlacementTarget>();
            statusLabel = roundStatusLabel;
            BindTargets();
            UpdateStatusLabel();
        }

        public void NotifyPiecePlaced(
            PlacementPiece piece,
            PlacementTarget target
        )
        {
            if (roundComplete || piece == null || target == null)
            {
                return;
            }

            PlacedCount = 0;

            foreach (PlacementPiece roundPiece in pieces)
            {
                if (roundPiece != null && roundPiece.IsPlaced)
                {
                    PlacedCount++;
                }
            }

            roundComplete = PieceCount > 0 && PlacedCount >= PieceCount;

            UpdateStatusLabel();
        }

        public void ResetRound()
        {
            foreach (PlacementTarget target in targets)
            {
                target?.ResetTarget();
            }

            foreach (PlacementPiece piece in pieces)
            {
                piece?.ResetToSpawn();
            }

            PlacedCount = 0;
            roundComplete = false;
            UpdateStatusLabel();
        }

        private void BindTargets()
        {
            foreach (PlacementTarget target in targets)
            {
                target?.SetGameManager(this);
            }
        }

        private void UpdateStatusLabel()
        {
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.text = roundComplete
                ? "SORT COMPLETE\nPRESS RESET"
                : $"SORT  {PlacedCount} / {PieceCount}";
        }
    }
}
