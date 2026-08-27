using UnityEngine;
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
        public const float DefaultStepDistance = 0.2f;

        [SerializeField] private Transform hmd;
        [SerializeField, Min(0.001f)]
        private float stepDistance = DefaultStepDistance;

        public Transform Hmd => hmd;
        public float StepDistance => stepDistance;

        public void Configure(
            Transform participantHmd,
            float distance = DefaultStepDistance)
        {
            hmd = participantHmd;
            stepDistance = Mathf.Max(0.001f, distance);
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
