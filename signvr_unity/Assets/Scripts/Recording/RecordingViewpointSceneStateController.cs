using System;
using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Applies deterministic, non-animated environment state for a recording
    /// viewpoint. The safe door is restored from its authored closed pose on
    /// every switch, then rotated around the left-edge hinge for state_02.
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

        private bool bound;

        public Transform SafeDoorPanel => safeDoorPanel;
        public Transform SafeDoorHinge => safeDoorHinge;
        public string OpenSafeViewpointId => openSafeViewpointId;
        public float OpenAngleDegrees => openAngleDegrees;
        public bool IsSafeOpen { get; private set; }

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

        public bool ValidateConfiguration(bool logErrors)
        {
            bool valid = viewpointController != null && safeDoorPanel != null &&
                         safeDoorHinge != null &&
                         safeDoorPanel.parent == safeDoorHinge.parent &&
                         !string.IsNullOrWhiteSpace(openSafeViewpointId) &&
                         Mathf.Abs(openAngleDegrees) >= 45f;
            if (!valid && logErrors)
            {
                Debug.LogError(
                    "[RecordingViewpointSceneStateController] Safe door state " +
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
        }
#endif
    }
}
