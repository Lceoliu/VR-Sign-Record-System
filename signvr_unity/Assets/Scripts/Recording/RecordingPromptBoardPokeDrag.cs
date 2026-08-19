using Oculus.Interaction;
using UnityEngine;

namespace SignVR.Recording
{
    public sealed class RecordingPromptBoardPokeDrag : MonoBehaviour
    {
        [SerializeField]
        private PointableUnityEventWrapper eventWrapper;

        [SerializeField]
        private Transform board;

        private bool dragging;
        private Vector3 pointerStartPosition;
        private Vector3 boardStartPosition;

        public void Configure(
            PointableUnityEventWrapper wrapper,
            Transform promptBoard)
        {
            eventWrapper = wrapper;
            board = promptBoard;
        }

        private void Awake()
        {
            eventWrapper.WhenSelect.AddListener(BeginDrag);
            eventWrapper.WhenMove.AddListener(ContinueDrag);
            eventWrapper.WhenUnselect.AddListener(EndDrag);
            eventWrapper.WhenCancel.AddListener(EndDrag);
        }

        private void BeginDrag(PointerEvent pointerEvent)
        {
            dragging = true;
            pointerStartPosition = pointerEvent.Pose.position;
            boardStartPosition = board.position;
        }

        private void ContinueDrag(PointerEvent pointerEvent)
        {
            if (!dragging)
            {
                return;
            }

            Vector3 position = boardStartPosition;
            position.y += pointerEvent.Pose.position.y - pointerStartPosition.y;
            board.position = position;
        }

        private void EndDrag(PointerEvent _)
        {
            dragging = false;
        }

        private void OnDisable()
        {
            dragging = false;
        }

        private void OnDestroy()
        {
            if (eventWrapper == null)
            {
                return;
            }

            eventWrapper.WhenSelect.RemoveListener(BeginDrag);
            eventWrapper.WhenMove.RemoveListener(ContinueDrag);
            eventWrapper.WhenUnselect.RemoveListener(EndDrag);
            eventWrapper.WhenCancel.RemoveListener(EndDrag);
        }
    }
}
