using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace SignVR.Recording
{
    [DisallowMultipleComponent]
    public sealed class RecordingIndexTipRayOrigin : MonoBehaviour
    {
        private OVRSkeleton skeleton;
        private IHand interactionHand;
        private OVRBone indexDistal;
        private OVRBone indexTip;

        public void Configure(
            OVRSkeleton configuredSkeleton,
            IHand configuredHand,
            RayInteractor interactor)
        {
            skeleton = configuredSkeleton;
            interactionHand = configuredHand;
            ResolveBones();
            interactor.InjectRayOrigin(transform);
        }

        private void LateUpdate()
        {
            if (interactionHand != null &&
                interactionHand.GetJointPose(
                    HandJointId.HandIndex3,
                    out Pose distalPose
                ) &&
                interactionHand.GetJointPose(
                    HandJointId.HandIndexTip,
                    out Pose tipPose
                ))
            {
                SetRayPose(
                    distalPose.position,
                    tipPose.position,
                    tipPose.rotation * Vector3.up
                );
                return;
            }

            if ((indexDistal == null || indexTip == null) && !ResolveBones())
            {
                return;
            }

            Transform distal = indexDistal.Transform;
            Transform tip = indexTip.Transform;
            if (distal == null || tip == null)
            {
                return;
            }

            SetRayPose(
                distal.position,
                tip.position,
                skeleton.transform.up
            );
        }

        private void SetRayPose(
            Vector3 distalPosition,
            Vector3 tipPosition,
            Vector3 upReference)
        {
            Vector3 direction = tipPosition - distalPosition;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }

            direction.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(
                upReference,
                direction
            );
            if (up.sqrMagnitude < 0.000001f)
            {
                up = Vector3.ProjectOnPlane(
                    Vector3.up,
                    direction
                );
            }

            transform.SetPositionAndRotation(
                tipPosition,
                Quaternion.LookRotation(direction, up.normalized)
            );
        }

        private bool ResolveBones()
        {
            if (skeleton == null || skeleton.Bones == null)
            {
                return false;
            }

            OVRBone legacyDistal = null;
            OVRBone legacyTip = null;
            OVRBone xrDistal = null;
            OVRBone xrTip = null;
            foreach (OVRBone bone in skeleton.Bones)
            {
                switch (bone.Id)
                {
                    case OVRSkeleton.BoneId.Hand_Index3:
                        legacyDistal = bone;
                        break;
                    case OVRSkeleton.BoneId.Hand_IndexTip:
                        legacyTip = bone;
                        break;
                    case OVRSkeleton.BoneId.XRHand_IndexDistal:
                        xrDistal = bone;
                        break;
                    case OVRSkeleton.BoneId.XRHand_IndexTip:
                        xrTip = bone;
                        break;
                }
            }

            indexDistal = xrDistal ?? legacyDistal;
            indexTip = xrTip ?? legacyTip;
            return indexDistal != null && indexTip != null;
        }
    }
}
