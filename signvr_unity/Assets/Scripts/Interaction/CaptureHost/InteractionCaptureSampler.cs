using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace SignVR.Interaction.CaptureHost
{
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class InteractionCaptureSampler : MonoBehaviour
    {
        private const double TrackingResolveIntervalSeconds = 1d;
        public const double CaptureIntervalSeconds = 0.05d;

        [SerializeField]
        private InteractionRunController controller;

        [SerializeField]
        private Transform hmd;

        [Header("Optional OVR hand components")]
        [SerializeField]
        private Component leftHand;

        [SerializeField]
        private Component leftSkeleton;

        [SerializeField]
        private Component rightHand;

        [SerializeField]
        private Component rightSkeleton;

        [Header("Key object state probes")]
        [SerializeField]
        private InteractionObjectStateProbe[] objectStateProbes =
            Array.Empty<InteractionObjectStateProbe>();

        private ReflectedHandSource leftSource;
        private ReflectedHandSource rightSource;
        private Component resolvedLeftHand;
        private Component resolvedLeftSkeleton;
        private Component resolvedRightHand;
        private Component resolvedRightSkeleton;
        private double nextTrackingResolveMonotonic;
        private readonly InteractionCaptureCadence captureCadence =
            new InteractionCaptureCadence(CaptureIntervalSeconds);

        public InteractionRunController Controller => controller;
        public Transform Hmd => hmd;
        public bool HmdReady => hmd != null;
        public bool LeftHandDataSourceReady =>
            leftHand != null && leftSkeleton != null;
        public bool RightHandDataSourceReady =>
            rightHand != null && rightSkeleton != null;
        public int ConfiguredObjectProbeCount => CountValidObjectProbes();

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            if (controller == null)
            {
                controller = GetComponent<InteractionRunController>() ??
                    GetComponentInParent<InteractionRunController>();
            }
            ResolveTrackingSources(force: true);
        }

        public void Configure(InteractionRunController value)
        {
            controller = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void ConfigureTracking(
            Transform hmdTransform,
            Component leftHandComponent,
            Component leftSkeletonComponent,
            Component rightHandComponent,
            Component rightSkeletonComponent)
        {
            hmd = hmdTransform;
            leftHand = leftHandComponent;
            leftSkeleton = leftSkeletonComponent;
            rightHand = rightHandComponent;
            rightSkeleton = rightSkeletonComponent;
            RefreshReflectedHandSources();
        }

        public void ConfigureObjectStateProbes(
            InteractionObjectStateProbe[] probes)
        {
            objectStateProbes = probes ??
                Array.Empty<InteractionObjectStateProbe>();
        }

        public bool IsStudyCaptureReady(out string reason)
        {
            ResolveTrackingSources(force: true);
            try
            {
                InteractionStudyCapturePrerequisites.Validate(
                    HmdReady,
                    LeftHandDataSourceReady,
                    RightHandDataSourceReady,
                    ConfiguredObjectProbeCount
                );
                reason = null;
                return true;
            }
            catch (InvalidOperationException exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        private void Update()
        {
            ResolveTrackingSources(force: false);
            bool captureActive = controller != null && controller.IsCaptureActive;
            if (!captureCadence.ShouldSample(
                    Time.realtimeSinceStartupAsDouble,
                    captureActive))
            {
                return;
            }

            bool hasHmdTransform = hmd != null && hmd.gameObject.activeInHierarchy;
            bool hmdValid = hasHmdTransform && IsHmdTracked();
            Vector3 hmdPosition = hasHmdTransform ? hmd.position : Vector3.zero;
            Quaternion hmdRotation = hasHmdTransform
                ? hmd.rotation
                : Quaternion.identity;
            controller.TryCapturePose(
                hmdValid,
                ToSample(hmdPosition),
                ToSample(hmdRotation),
                leftSource.Capture(),
                rightSource.Capture()
            );

            for (int index = 0; index < objectStateProbes.Length; index++)
            {
                InteractionObjectStateProbe probe = objectStateProbes[index];
                if (probe == null || !probe.isActiveAndEnabled)
                {
                    continue;
                }
                controller.RecordObjectState(
                    probe.ObjectId,
                    probe.CaptureStateJson()
                );
            }
        }

        private void OnDisable()
        {
            ResetCadence();
        }

        private void OnEnable()
        {
            ResetCadence();
        }

        internal void ResetCadence()
        {
            captureCadence.Reset();
        }

#if UNITY_EDITOR
        internal bool EvaluateCaptureCadenceForTests(
            double monotonicTimeSeconds,
            bool captureActive)
        {
            return captureCadence.ShouldSample(
                monotonicTimeSeconds,
                captureActive
            );
        }

        internal void ResetCaptureCadenceForTests()
        {
            ResetCadence();
        }
#endif

        private void ResolveOvrComponentsIfNeeded()
        {
            if (leftHand != null && leftSkeleton != null &&
                rightHand != null && rightSkeleton != null)
            {
                return;
            }

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include
            );
            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour component = behaviours[index];
                if (component == null)
                {
                    continue;
                }
                string typeName = component.GetType().Name;
                string hierarchy = GetHierarchyPath(component.transform);
                bool looksLeft = hierarchy.IndexOf(
                    "left",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
                bool looksRight = hierarchy.IndexOf(
                    "right",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
                if (string.Equals(typeName, "OVRHand", StringComparison.Ordinal))
                {
                    if (looksLeft && leftHand == null)
                    {
                        leftHand = component;
                    }
                    else if (looksRight && rightHand == null)
                    {
                        rightHand = component;
                    }
                }
                else if (string.Equals(
                    typeName,
                    "OVRSkeleton",
                    StringComparison.Ordinal))
                {
                    if (looksLeft && leftSkeleton == null)
                    {
                        leftSkeleton = component;
                    }
                    else if (looksRight && rightSkeleton == null)
                    {
                        rightSkeleton = component;
                    }
                }
            }
        }

        private void ResolveTrackingSources(bool force)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now < nextTrackingResolveMonotonic)
            {
                return;
            }
            nextTrackingResolveMonotonic = now +
                TrackingResolveIntervalSeconds;
            if (hmd == null && Camera.main != null)
            {
                hmd = Camera.main.transform;
            }
            ResolveOvrComponentsIfNeeded();
            RefreshReflectedHandSources();
        }

        private void RefreshReflectedHandSources()
        {
            if (leftSource == null || resolvedLeftHand != leftHand ||
                resolvedLeftSkeleton != leftSkeleton)
            {
                resolvedLeftHand = leftHand;
                resolvedLeftSkeleton = leftSkeleton;
                leftSource = new ReflectedHandSource(leftHand, leftSkeleton);
            }
            if (rightSource == null || resolvedRightHand != rightHand ||
                resolvedRightSkeleton != rightSkeleton)
            {
                resolvedRightHand = rightHand;
                resolvedRightSkeleton = rightSkeleton;
                rightSource = new ReflectedHandSource(rightHand, rightSkeleton);
            }
        }

        private int CountValidObjectProbes()
        {
            int count = 0;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            if (objectStateProbes == null)
            {
                return 0;
            }
            for (int index = 0; index < objectStateProbes.Length; index++)
            {
                InteractionObjectStateProbe probe = objectStateProbes[index];
                if (probe == null || !probe.isActiveAndEnabled ||
                    string.IsNullOrWhiteSpace(probe.ObjectId))
                {
                    continue;
                }
                if (!identities.Add(probe.ObjectId))
                {
                    continue;
                }
                count++;
            }
            return count;
        }

        private static InteractionVector3Sample ToSample(Vector3 value)
        {
            return new InteractionVector3Sample(value.x, value.y, value.z);
        }

        private static bool IsHmdTracked()
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.isValid)
            {
                return false;
            }
            return device.TryGetFeatureValue(
                    CommonUsages.isTracked,
                    out bool tracked) && tracked;
        }

        private static InteractionQuaternionSample ToSample(Quaternion value)
        {
            return new InteractionQuaternionSample(
                value.x,
                value.y,
                value.z,
                value.w
            );
        }

        private static string GetHierarchyPath(Transform value)
        {
            var parts = new Stack<string>();
            Transform current = value;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

        private sealed class ReflectedHandSource
        {
            private readonly Component hand;
            private readonly Component skeleton;
            private readonly PropertyInfo isTracked;
            private readonly PropertyInfo isDataValid;
            private readonly PropertyInfo isDataHighConfidence;
            private readonly PropertyInfo poseSourceInferred;
            private readonly PropertyInfo bones;

            public ReflectedHandSource(Component hand, Component skeleton)
            {
                this.hand = hand;
                this.skeleton = skeleton;
                if (hand != null)
                {
                    Type type = hand.GetType();
                    isTracked = FindBooleanProperty(type, "IsTracked");
                    isDataValid = FindBooleanProperty(type, "IsDataValid");
                    isDataHighConfidence = FindBooleanProperty(
                        type,
                        "IsDataHighConfidence"
                    );
                    poseSourceInferred = FindBooleanProperty(
                        type,
                        "PoseSourceInferred"
                    );
                }
                if (skeleton != null)
                {
                    bones = skeleton.GetType().GetProperty(
                        "Bones",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                }
            }

            public InteractionHandSample Capture()
            {
                bool tracked = ReadBoolean(hand, isTracked);
                bool dataValid = ReadBoolean(hand, isDataValid);
                bool highConfidence = ReadBoolean(
                    hand,
                    isDataHighConfidence
                );
                bool inferred = ReadBoolean(hand, poseSourceInferred);
                var joints = new List<InteractionJointSample>();
                if (skeleton != null && bones != null)
                {
                    object rawBones;
                    try
                    {
                        rawBones = bones.GetValue(skeleton, null);
                    }
                    catch
                    {
                        rawBones = null;
                    }
                    var enumerable = rawBones as IEnumerable;
                    if (enumerable != null)
                    {
                        foreach (object bone in enumerable)
                        {
                            if (bone == null)
                            {
                                continue;
                            }
                            Type boneType = bone.GetType();
                            PropertyInfo idProperty = boneType.GetProperty(
                                "Id",
                                BindingFlags.Public | BindingFlags.Instance
                            );
                            PropertyInfo transformProperty = boneType.GetProperty(
                                "Transform",
                                BindingFlags.Public | BindingFlags.Instance
                            );
                            Transform transform = null;
                            string id = "unknown";
                            try
                            {
                                object rawId = idProperty?.GetValue(bone, null);
                                if (rawId != null)
                                {
                                    id = rawId.ToString();
                                }
                                transform = transformProperty?.GetValue(
                                    bone,
                                    null
                                ) as Transform;
                            }
                            catch
                            {
                                transform = null;
                            }
                            bool valid = tracked && dataValid && transform != null;
                            Vector3 position = valid
                                ? transform.position
                                : Vector3.zero;
                            Quaternion rotation = valid
                                ? transform.rotation
                                : Quaternion.identity;
                            joints.Add(new InteractionJointSample(
                                id,
                                valid,
                                ToSample(position),
                                ToSample(rotation)
                            ));
                        }
                    }
                }
                return new InteractionHandSample(
                    tracked,
                    dataValid,
                    highConfidence,
                    inferred,
                    joints.AsReadOnly()
                );
            }

            private static PropertyInfo FindBooleanProperty(
                Type type,
                string name)
            {
                PropertyInfo property = type.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.Instance
                );
                return property != null && property.PropertyType == typeof(bool)
                    ? property
                    : null;
            }

            private static bool ReadBoolean(
                Component component,
                PropertyInfo property)
            {
                if (component == null || property == null)
                {
                    return false;
                }
                try
                {
                    return (bool)property.GetValue(component, null);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
