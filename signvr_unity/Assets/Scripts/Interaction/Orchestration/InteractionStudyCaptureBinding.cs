using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SignVR.Interaction.CaptureHost;
using UnityEngine;
using UnityEngine.XR;

namespace SignVR.Interaction.Orchestration
{
    /// <summary>
    /// W8's injection boundary for W6 capture. It binds real XR/Meta sources;
    /// it never substitutes synthetic tracking when Study hardware is absent.
    /// </summary>
    [DefaultExecutionOrder(-700)]
    [DisallowMultipleComponent]
    public sealed class InteractionStudyCaptureBinding :
        MonoBehaviour,
        IInteractionStudyStrictStartGate
    {
        [SerializeField]
        private InteractionCaptureSampler sampler;

        [SerializeField]
        private Transform hmd;

        [Header("Meta bare-hand tracking")]
        [SerializeField]
        private Component leftHand;

        [SerializeField]
        private Component leftSkeleton;

        [SerializeField]
        private Component rightHand;

        [SerializeField]
        private Component rightSkeleton;

        [Header("W7 task-object probes")]
        [SerializeField]
        private InteractionObjectStateProbe[] objectStateProbes =
            Array.Empty<InteractionObjectStateProbe>();

        public InteractionCaptureSampler Sampler => sampler;
        public Transform Hmd => hmd;
        public Component LeftHand => leftHand;
        public Component LeftSkeleton => leftSkeleton;
        public Component RightHand => rightHand;
        public Component RightSkeleton => rightSkeleton;
        public IReadOnlyList<InteractionObjectStateProbe> ObjectStateProbes =>
            objectStateProbes;

        public void Configure(
            InteractionCaptureSampler captureSampler,
            Transform hmdTransform,
            Component leftHandComponent,
            Component leftSkeletonComponent,
            Component rightHandComponent,
            Component rightSkeletonComponent,
            InteractionObjectStateProbe[] probes)
        {
            sampler = captureSampler ??
                throw new ArgumentNullException(nameof(captureSampler));
            hmd = hmdTransform;
            leftHand = leftHandComponent;
            leftSkeleton = leftSkeletonComponent;
            rightHand = rightHandComponent;
            rightSkeleton = rightSkeletonComponent;
            objectStateProbes = probes ??
                Array.Empty<InteractionObjectStateProbe>();
            ApplyToSampler();
        }

        public void ApplyToSampler()
        {
            if (sampler == null)
            {
                return;
            }
            sampler.ConfigureTracking(
                hmd,
                leftHand,
                leftSkeleton,
                rightHand,
                rightSkeleton
            );
            sampler.ConfigureObjectStateProbes(objectStateProbes);
        }

        public bool ValidateStructure(out string reason)
        {
            if (sampler == null)
            {
                reason = "InteractionCaptureSampler reference is missing.";
                return false;
            }
            if (hmd == null)
            {
                reason = "XR HMD transform reference is missing.";
                return false;
            }
            if (!IsExactComponent(leftHand, "OVRHand") ||
                !IsExactComponent(rightHand, "OVRHand"))
            {
                reason = "Exact left/right OVRHand components are required.";
                return false;
            }
            if (!IsExactComponent(leftSkeleton, "OVRSkeleton") ||
                !IsExactComponent(rightSkeleton, "OVRSkeleton"))
            {
                reason =
                    "Exact left/right OVRSkeleton components are required.";
                return false;
            }
            if (leftHand == rightHand || leftSkeleton == rightSkeleton)
            {
                reason = "Left and right hand sources must be distinct.";
                return false;
            }
            if (!MatchesSkeletonSource(
                    leftHand,
                    leftSkeleton,
                    "Left",
                    out reason) ||
                !MatchesSkeletonSource(
                    rightHand,
                    rightSkeleton,
                    "Right",
                    out reason))
            {
                return false;
            }
            if (!HasValidUniqueProbe(out reason))
            {
                return false;
            }
            reason = null;
            return true;
        }

        public bool CanStartStudy(out string reason)
        {
            ApplyToSampler();
            if (!ValidateStructure(out reason))
            {
                return false;
            }
            if (!Application.isPlaying)
            {
                reason =
                    "Strict Study readiness requires Play Mode and a real XR " +
                    "device; structural Editor readiness alone is insufficient.";
                return false;
            }
            if (!hmd.gameObject.activeInHierarchy)
            {
                reason = "Configured HMD is inactive.";
                return false;
            }

            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid ||
                !head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) ||
                !tracked)
            {
                reason = "A real, currently tracked XR HMD is required.";
                return false;
            }
            if (!IsTrackedHand(leftHand, leftSkeleton, "left", out reason) ||
                !IsTrackedHand(rightHand, rightSkeleton, "right", out reason))
            {
                return false;
            }
            reason = null;
            return true;
        }

        private void Awake()
        {
            ApplyToSampler();
        }

        private void OnEnable()
        {
            ApplyToSampler();
        }

        private bool HasValidUniqueProbe(out string reason)
        {
            if (objectStateProbes == null || objectStateProbes.Length == 0)
            {
                reason = "At least one W7 task-object probe is required.";
                return false;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < objectStateProbes.Length; index++)
            {
                InteractionObjectStateProbe probe = objectStateProbes[index];
                if (probe == null || string.IsNullOrWhiteSpace(probe.ObjectId))
                {
                    reason = "Object probes must be non-null with stable IDs.";
                    return false;
                }
                if (!ids.Add(probe.ObjectId))
                {
                    reason = "Duplicate object probe ID: " + probe.ObjectId;
                    return false;
                }
            }
            reason = null;
            return true;
        }

        private static bool IsTrackedHand(
            Component hand,
            Component skeleton,
            string side,
            out string reason)
        {
            if (!hand.gameObject.activeInHierarchy ||
                !skeleton.gameObject.activeInHierarchy)
            {
                reason = "Configured " + side + " hand source is inactive.";
                return false;
            }
            if (!ReadRequiredBoolean(hand, "IsTracked") ||
                !ReadRequiredBoolean(hand, "IsDataValid"))
            {
                reason = "Configured " + side +
                    " OVRHand is not currently tracked with valid data.";
                return false;
            }
            PropertyInfo bones = skeleton.GetType().GetProperty(
                "Bones",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (bones == null || !(bones.GetValue(skeleton, null) is IEnumerable values))
            {
                reason = "Configured " + side +
                    " OVRSkeleton exposes no live Bones collection.";
                return false;
            }
            IEnumerator enumerator = values.GetEnumerator();
            try
            {
                if (!enumerator.MoveNext())
                {
                    reason = "Configured " + side +
                        " OVRSkeleton has no live joints.";
                    return false;
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
            reason = null;
            return true;
        }

        private static bool ReadRequiredBoolean(Component value, string name)
        {
            PropertyInfo property = value.GetType().GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance
            );
            if (property == null || property.PropertyType != typeof(bool))
            {
                return false;
            }
            try
            {
                return (bool)property.GetValue(value, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool MatchesSkeletonSource(
            Component hand,
            Component skeleton,
            string side,
            out string reason)
        {
            if (hand.gameObject != skeleton.gameObject)
            {
                reason = side +
                    " OVRSkeleton must share the real OVRHand data-source object.";
                return false;
            }

            MethodInfo skeletonTypeMethod = skeleton.GetType().GetMethod(
                "GetSkeletonType",
                BindingFlags.Public | BindingFlags.Instance
            );
            Type providerInterface = null;
            Type[] interfaces = hand.GetType().GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                if (string.Equals(
                        interfaces[index].Name,
                        "IOVRSkeletonDataProvider",
                        StringComparison.Ordinal))
                {
                    providerInterface = interfaces[index];
                    break;
                }
            }
            MethodInfo providerTypeMethod = providerInterface?.GetMethod(
                "GetSkeletonType"
            );
            if (skeletonTypeMethod == null || providerTypeMethod == null)
            {
                reason = side +
                    " Meta hand/skeleton data-provider API is unavailable.";
                return false;
            }

            try
            {
                object skeletonType = skeletonTypeMethod.Invoke(skeleton, null);
                object providerType = providerTypeMethod.Invoke(hand, null);
                string typeName = providerType?.ToString() ?? string.Empty;
                if (providerType == null ||
                    !providerType.Equals(skeletonType) ||
                    typeName.IndexOf(
                        side,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    reason = side +
                        " OVRSkeleton type does not match its OVRHand provider.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = side + " Meta data-provider validation failed: " +
                    exception.Message;
                return false;
            }

            reason = null;
            return true;
        }

        private static bool IsExactComponent(Component value, string typeName)
        {
            return value != null && string.Equals(
                value.GetType().Name,
                typeName,
                StringComparison.Ordinal
            );
        }
    }
}
