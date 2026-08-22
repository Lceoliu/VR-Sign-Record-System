using Meta.XR.Movement.Retargeting;
using Unity.Collections;
using UnityEngine;
using static Meta.XR.Movement.MSDKUtility;

namespace SignVR.Demo
{
    /// <summary>
    /// Presents the frame selected by <see cref="AvatarPreviewMetaPosePlayer"/>
    /// to Meta Movement's CharacterRetargeter.
    /// </summary>
    public sealed class RecordedMetaPoseProvider : MonoBehaviour, ISourceDataProvider
    {
        [SerializeField]
        private AvatarPreviewMetaPosePlayer source;

        public void Configure(AvatarPreviewMetaPosePlayer poseSource)
        {
            source = poseSource;
        }

        public NativeArray<NativeTransform> GetSkeletonPose()
        {
            return source != null ? source.CurrentPose : default;
        }

        public NativeArray<NativeTransform> GetSkeletonTPose()
        {
            return GetSkeletonPose();
        }

        public string GetManifestation()
        {
            // Meta's full-body source uses null; an empty string is interpreted as
            // a named manifestation and makes the native retargeter reject frames.
            return null;
        }

        public bool IsPoseValid()
        {
            return source != null && source.HasValidPose;
        }

        public bool IsNewTPoseAvailable()
        {
            return false;
        }
    }
}
