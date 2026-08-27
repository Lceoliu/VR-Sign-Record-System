using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction
{
    /// <summary>
    /// Applies an application-owned, collision-free translation above the
    /// runtime-owned XR tracking origin. InteractionLab uses this for seated
    /// positioning without moving its fixed VRPlayer world-frame root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionSeatedRigMover : MonoBehaviour
    {
        public const float DefaultStepDistance = 0.1f;

        [SerializeField] private Transform hmd;
        [SerializeField] private Button moveButton;
        [SerializeField, Min(0.001f)]
        private float stepDistance = DefaultStepDistance;

        public Transform Hmd => hmd;
        public Button MoveButton => moveButton;
        public float StepDistance => stepDistance;

        public void Configure(
            Transform participantHmd,
            Button participantMoveButton,
            float distance = DefaultStepDistance)
        {
            if (isActiveAndEnabled && moveButton != null)
            {
                moveButton.onClick.RemoveListener(MoveAlongCurrentView);
            }

            hmd = participantHmd;
            moveButton = participantMoveButton;
            stepDistance = Mathf.Max(0.001f, distance);

            if (isActiveAndEnabled && moveButton != null)
            {
                moveButton.onClick.AddListener(MoveAlongCurrentView);
            }
        }

        private void OnEnable()
        {
            if (moveButton != null)
            {
                moveButton.onClick.RemoveListener(MoveAlongCurrentView);
                moveButton.onClick.AddListener(MoveAlongCurrentView);
            }
        }

        private void OnDisable()
        {
            if (moveButton != null)
            {
                moveButton.onClick.RemoveListener(MoveAlongCurrentView);
            }
        }

        /// <summary>
        /// Moves the XR subtree by one step in the HMD's current world-space
        /// forward direction. No axis is discarded and no collision query is
        /// performed.
        /// </summary>
        public void MoveAlongCurrentView()
        {
            if (hmd == null)
            {
                Debug.LogError(
                    "[InteractionSeatedRigMover] Cannot move without an HMD.",
                    this
                );
                return;
            }

            Vector3 direction = hmd.forward;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                Debug.LogError(
                    "[InteractionSeatedRigMover] HMD forward is invalid.",
                    this
                );
                return;
            }

            transform.position += direction.normalized * stepDistance;
        }
    }
}
