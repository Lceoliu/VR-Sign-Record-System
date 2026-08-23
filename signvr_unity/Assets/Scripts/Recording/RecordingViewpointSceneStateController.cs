using System;
using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Applies deterministic, non-animated environment state for a recording
    /// viewpoint. Doors are restored from their authored closed poses before
    /// the state-specific safe or cabinet opening is applied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingViewpointSceneStateController : MonoBehaviour
    {
        [SerializeField]
        private RecordingViewpointController viewpointController;

        [SerializeField]
        private Transform safeDoorPanel;

        [SerializeField]
        private Transform safeDoorHinge;

        [SerializeField]
        private string openSafeViewpointId = "state_02";

        [SerializeField]
        private Vector3 closedDoorLocalPosition;

        [SerializeField]
        private Quaternion closedDoorLocalRotation = Quaternion.identity;

        [SerializeField]
        [Range(-160f, 160f)]
        private float openAngleDegrees = -105f;

        [Header("Cabinet doors")]
        [SerializeField]
        private Transform closetLeftDoor;

        [SerializeField]
        private Transform closetLeftHinge;

        [SerializeField]
        private Transform closetRightDoor;

        [SerializeField]
        private Transform closetRightHinge;

        [SerializeField]
        private string[] openClosetViewpointIds = { "state_05", "state_06" };

        [SerializeField]
        private Vector3 closetLeftClosedLocalPosition;

        [SerializeField]
        private Quaternion closetLeftClosedLocalRotation = Quaternion.identity;

        [SerializeField]
        private Vector3 closetRightClosedLocalPosition;

        [SerializeField]
        private Quaternion closetRightClosedLocalRotation = Quaternion.identity;

        [SerializeField]
        [Range(-160f, 160f)]
        private float closetLeftOpenAngleDegrees = 105f;

        [SerializeField]
        [Range(-160f, 160f)]
        private float closetRightOpenAngleDegrees = -105f;

        private bool bound;

        public Transform SafeDoorPanel => safeDoorPanel;
        public Transform SafeDoorHinge => safeDoorHinge;
        public string OpenSafeViewpointId => openSafeViewpointId;
        public float OpenAngleDegrees => openAngleDegrees;
        public bool IsSafeOpen { get; private set; }
        public Transform ClosetLeftDoor => closetLeftDoor;
        public Transform ClosetLeftHinge => closetLeftHinge;
        public Transform ClosetRightDoor => closetRightDoor;
        public Transform ClosetRightHinge => closetRightHinge;
        public string[] OpenClosetViewpointIds =>
            openClosetViewpointIds ?? Array.Empty<string>();
        public float ClosetLeftOpenAngleDegrees => closetLeftOpenAngleDegrees;
        public float ClosetRightOpenAngleDegrees => closetRightOpenAngleDegrees;
        public bool IsClosetOpen { get; private set; }

        public void Configure(
            RecordingViewpointController recordingViewpoints,
            Transform doorPanel,
            Transform doorHinge,
            string openViewpointId,
            Vector3 closedLocalPosition,
            Quaternion closedLocalRotation,
            float openAngle)
        {
            Unbind();
            viewpointController = recordingViewpoints;
            safeDoorPanel = doorPanel;
            safeDoorHinge = doorHinge;
            openSafeViewpointId = string.IsNullOrWhiteSpace(openViewpointId)
                ? "state_02"
                : openViewpointId.Trim();
            closedDoorLocalPosition = closedLocalPosition;
            closedDoorLocalRotation = closedLocalRotation;
            openAngleDegrees = Mathf.Clamp(openAngle, -160f, 160f);

            if (isActiveAndEnabled)
            {
                Bind();
                ApplyCurrentState();
            }
        }

        public void ConfigureCloset(
            Transform leftDoor,
            Transform leftHinge,
            Transform rightDoor,
            Transform rightHinge,
            string[] openViewpointIds,
            Vector3 leftClosedLocalPosition,
            Quaternion leftClosedLocalRotation,
            Vector3 rightClosedLocalPosition,
            Quaternion rightClosedLocalRotation,
            float leftOpenAngle,
            float rightOpenAngle)
        {
            closetLeftDoor = leftDoor;
            closetLeftHinge = leftHinge;
            closetRightDoor = rightDoor;
            closetRightHinge = rightHinge;
            openClosetViewpointIds = openViewpointIds ?? Array.Empty<string>();
            closetLeftClosedLocalPosition = leftClosedLocalPosition;
            closetLeftClosedLocalRotation = leftClosedLocalRotation;
            closetRightClosedLocalPosition = rightClosedLocalPosition;
            closetRightClosedLocalRotation = rightClosedLocalRotation;
            closetLeftOpenAngleDegrees = Mathf.Clamp(
                leftOpenAngle,
                -160f,
                160f
            );
            closetRightOpenAngleDegrees = Mathf.Clamp(
                rightOpenAngle,
                -160f,
                160f
            );

            if (isActiveAndEnabled)
            {
                ApplyCurrentState();
            }
        }

        private void OnEnable()
        {
            Bind();
        }

        private void Start()
        {
            ApplyCurrentState();
        }

        private void Bind()
        {
            if (bound || viewpointController == null)
            {
                return;
            }

            viewpointController.ViewpointChanged += HandleViewpointChanged;
            bound = true;
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }

            if (viewpointController != null)
            {
                viewpointController.ViewpointChanged -= HandleViewpointChanged;
            }
            bound = false;
        }

        public void ApplyCurrentState()
        {
            string viewpointId = viewpointController != null
                ? viewpointController.CurrentViewpointId
                : string.Empty;
            ApplyViewpointState(viewpointId);
        }

        public void ApplyViewpointState(string viewpointId)
        {
            ApplySafeDoorState(viewpointId);
            ApplyClosetDoorState(viewpointId);
        }

        private void ApplySafeDoorState(string viewpointId)
        {
            if (safeDoorPanel == null || safeDoorHinge == null ||
                safeDoorPanel.parent != safeDoorHinge.parent)
            {
                IsSafeOpen = false;
                return;
            }

            safeDoorPanel.SetLocalPositionAndRotation(
                closedDoorLocalPosition,
                closedDoorLocalRotation
            );

            IsSafeOpen = string.Equals(
                viewpointId,
                openSafeViewpointId,
                StringComparison.Ordinal
            );
            if (!IsSafeOpen)
            {
                return;
            }

            safeDoorPanel.RotateAround(
                safeDoorHinge.position,
                safeDoorHinge.up,
                openAngleDegrees
            );
        }

        private void ApplyClosetDoorState(string viewpointId)
        {
            if (!HasClosetConfiguration())
            {
                IsClosetOpen = false;
                return;
            }

            closetLeftDoor.SetLocalPositionAndRotation(
                closetLeftClosedLocalPosition,
                closetLeftClosedLocalRotation
            );
            closetRightDoor.SetLocalPositionAndRotation(
                closetRightClosedLocalPosition,
                closetRightClosedLocalRotation
            );

            IsClosetOpen = ContainsOpenClosetViewpoint(viewpointId);
            if (!IsClosetOpen)
            {
                return;
            }

            closetLeftDoor.RotateAround(
                closetLeftHinge.position,
                closetLeftHinge.up,
                closetLeftOpenAngleDegrees
            );
            closetRightDoor.RotateAround(
                closetRightHinge.position,
                closetRightHinge.up,
                closetRightOpenAngleDegrees
            );
        }

        private bool HasClosetConfiguration()
        {
            return closetLeftDoor != null && closetLeftHinge != null &&
                   closetRightDoor != null && closetRightHinge != null &&
                   closetLeftDoor.parent == closetLeftHinge.parent &&
                   closetRightDoor.parent == closetRightHinge.parent &&
                   openClosetViewpointIds != null &&
                   openClosetViewpointIds.Length == 2;
        }

        private bool ContainsOpenClosetViewpoint(string viewpointId)
        {
            for (int index = 0; index < openClosetViewpointIds.Length; index++)
            {
                if (string.Equals(
                        viewpointId,
                        openClosetViewpointIds[index],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public bool ValidateConfiguration(bool logErrors)
        {
            bool valid = viewpointController != null && safeDoorPanel != null &&
                         safeDoorHinge != null &&
                         safeDoorPanel.parent == safeDoorHinge.parent &&
                         !string.IsNullOrWhiteSpace(openSafeViewpointId) &&
                         Mathf.Abs(openAngleDegrees) >= 45f &&
                         HasClosetConfiguration() &&
                         Mathf.Abs(closetLeftOpenAngleDegrees) >= 45f &&
                         Mathf.Abs(closetRightOpenAngleDegrees) >= 45f;
            if (!valid && logErrors)
            {
                Debug.LogError(
                    "[RecordingViewpointSceneStateController] Door state " +
                    "configuration is incomplete."
                );
            }
            return valid;
        }

        private void HandleViewpointChanged(RecordingViewpointSelection selection)
        {
            ApplyViewpointState(selection.Id);
        }

        private void OnDisable()
        {
            Unbind();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            openAngleDegrees = Mathf.Clamp(openAngleDegrees, -160f, 160f);
            closetLeftOpenAngleDegrees = Mathf.Clamp(
                closetLeftOpenAngleDegrees,
                -160f,
                160f
            );
            closetRightOpenAngleDegrees = Mathf.Clamp(
                closetRightOpenAngleDegrees,
                -160f,
                160f
            );
        }
#endif
    }
}
