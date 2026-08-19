using UnityEngine;

namespace SignVR.CoopRelay
{
    /// <summary>
    /// Opens the lift doors after both role stations are ready. The player rig
    /// never moves, so completion feedback does not introduce VR locomotion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopRelayPresentation : MonoBehaviour
    {
        [SerializeField]
        private CoopRelayGameManager manager;

        [SerializeField]
        private Transform leftDoor;

        [SerializeField]
        private Transform rightDoor;

        [SerializeField, Min(0.1f)]
        private float openDistance = 0.82f;

        [SerializeField, Min(0.1f)]
        private float movementSpeed = 1.1f;

        private Vector3 leftClosedPosition;
        private Vector3 rightClosedPosition;
        private bool closedPoseCaptured;

        public CoopRelayGameManager Manager => manager;
        public Transform LeftDoor => leftDoor;
        public Transform RightDoor => rightDoor;

        private void Awake()
        {
            CaptureClosedPose();
        }

        private void OnEnable()
        {
            if (manager != null)
            {
                manager.StateChanged += OnStateChanged;
            }
        }

        private void OnDisable()
        {
            if (manager != null)
            {
                manager.StateChanged -= OnStateChanged;
            }
        }

        private void Update()
        {
            if (!closedPoseCaptured || leftDoor == null || rightDoor == null)
            {
                return;
            }

            bool open = manager != null && manager.RoundComplete;
            Vector3 leftTarget = leftClosedPosition +
                (open ? Vector3.left * openDistance : Vector3.zero);
            Vector3 rightTarget = rightClosedPosition +
                (open ? Vector3.right * openDistance : Vector3.zero);

            leftDoor.localPosition = Vector3.MoveTowards(
                leftDoor.localPosition,
                leftTarget,
                movementSpeed * Time.deltaTime
            );
            rightDoor.localPosition = Vector3.MoveTowards(
                rightDoor.localPosition,
                rightTarget,
                movementSpeed * Time.deltaTime
            );
        }

        public void Configure(
            CoopRelayGameManager relayManager,
            Transform leftLiftDoor,
            Transform rightLiftDoor
        )
        {
            if (manager != null && isActiveAndEnabled)
            {
                manager.StateChanged -= OnStateChanged;
            }

            manager = relayManager;
            leftDoor = leftLiftDoor;
            rightDoor = rightLiftDoor;
            CaptureClosedPose();

            if (manager != null && isActiveAndEnabled && Application.isPlaying)
            {
                manager.StateChanged += OnStateChanged;
            }
        }

        private void CaptureClosedPose()
        {
            if (leftDoor == null || rightDoor == null)
            {
                closedPoseCaptured = false;
                return;
            }

            leftClosedPosition = leftDoor.localPosition;
            rightClosedPosition = rightDoor.localPosition;
            closedPoseCaptured = true;
        }

        private void OnStateChanged(CoopRelayGameManager relayManager)
        {
            if (!relayManager.RoundComplete && closedPoseCaptured)
            {
                leftDoor.localPosition = leftClosedPosition;
                rightDoor.localPosition = rightClosedPosition;
            }
        }
    }
}
