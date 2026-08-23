using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Recording
{
    /// <summary>
    /// Draws a ray from each tracked index fingertip and tests it against the
    /// active target bounds. OVRSkeleton bones are preferred; Interaction SDK
    /// hand joints are used as a fallback for rigs that do not own OVRSkeletons.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10020)]
    public sealed class RecordingIndexFingerRays : MonoBehaviour
    {
        private const string OverlayShaderName =
            "SignVR/Recording Hand Skeleton Overlay";
        private const string OverlayShaderResource =
            "Shaders/RecordingHandSkeletonOverlay";

        private sealed class HandRaySource
        {
            public bool IsLeft;
            public OVRSkeleton Skeleton;
            public HandVisual HandVisual;
            public Transform Distal;
            public Transform Tip;
            public LineRenderer Line;
        }

        [Header("Dependencies")]
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private RecordingTargetVisualCues targetVisualCues;

        [SerializeField]
        private Camera editorSimulationCamera;

        [Header("Ray appearance")]
        [SerializeField]
        [Min(0.25f)]
        private float maximumDistance = 6f;

        [SerializeField]
        [Range(0.001f, 0.015f)]
        private float startWidth = 0.005f;

        [SerializeField]
        [Range(0.0005f, 0.01f)]
        private float endWidth = 0.002f;

        [SerializeField]
        private Color searchingColor = new(0.12f, 0.82f, 1f, 0.58f);

        [SerializeField]
        private Color hitColor = new(0.34f, 1f, 0.38f, 0.96f);

#if UNITY_EDITOR
        [Header("Editor simulation")]
        [SerializeField]
        private bool simulateWhenHandsUnavailable = true;
#endif

        private readonly HandRaySource left = new() { IsLeft = true };
        private readonly HandRaySource right = new() { IsLeft = false };
        private Material overlayMaterial;
        private bool coordinatorBound;
        private bool hasManualVisibility;
        private bool manualVisibility;
        private bool visibilityRequested;
        private float nextSourceDiscoveryTime;

        public bool LeftRayVisible => left.Line != null && left.Line.enabled;
        public bool RightRayVisible => right.Line != null && right.Line.enabled;
        public bool LeftHitsTarget { get; private set; }
        public bool RightHitsTarget { get; private set; }
        public bool UsingEditorSimulation { get; private set; }
        public bool IsRayActive => visibilityRequested;

        /// <summary>
        /// Binds state and target-bounds dependencies. Tracked sources can be
        /// supplied separately or discovered once the rig is active.
        /// </summary>
        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingTargetVisualCues visualCues,
            Camera simulationCamera = null)
        {
            UnbindCoordinator();
            coordinator = recordingCoordinator;
            targetVisualCues = visualCues;
            if (simulationCamera != null)
            {
                editorSimulationCamera = simulationCamera;
            }
            BindCoordinator();
            RefreshVisibility();
        }

        /// <summary>
        /// Explicitly supplies OVR skeletons. Hand_Index3 to Hand_IndexTip is
        /// used for OVR hand skeletons; equivalent XR bone IDs are supported.
        /// </summary>
        public void SetSkeletons(
            OVRSkeleton leftSkeleton,
            OVRSkeleton rightSkeleton)
        {
            SetSkeleton(left, leftSkeleton);
            SetSkeleton(right, rightSkeleton);
        }

        /// <summary>
        /// Supplies Interaction SDK visuals as a portable fallback joint source.
        /// </summary>
        public void SetHandVisuals(
            HandVisual leftHandVisual,
            HandVisual rightHandVisual)
        {
            left.HandVisual = leftHandVisual;
            right.HandVisual = rightHandVisual;
        }

        public void SetRayActive(bool active)
        {
            hasManualVisibility = true;
            manualVisibility = active;
            RefreshVisibility();
        }

        public void UseCoordinatorVisibility()
        {
            hasManualVisibility = false;
            RefreshVisibility();
        }

        private void Awake()
        {
            EnsureLines();
        }

        private void OnEnable()
        {
            EnsureLines();
            BindCoordinator();
            nextSourceDiscoveryTime = 0f;
            RefreshVisibility();
        }

        private void LateUpdate()
        {
            UsingEditorSimulation = false;
            if (!visibilityRequested ||
                targetVisualCues == null ||
                !targetVisualCues.IsCueActive ||
                targetVisualCues.ActiveBoundsCount == 0)
            {
                HideRays();
                return;
            }

            DiscoverMissingSources();
            LeftHitsTarget = RefreshHandRay(left);
            RightHitsTarget = RefreshHandRay(right);
        }

        private void OnDisable()
        {
            UnbindCoordinator();
            HideRays();
        }

        private void OnDestroy()
        {
            UnbindCoordinator();
            if (overlayMaterial != null)
            {
                DestroyGeneratedObject(overlayMaterial);
                overlayMaterial = null;
            }
        }

        private void BindCoordinator()
        {
            if (!isActiveAndEnabled || coordinator == null || coordinatorBound)
            {
                return;
            }

            coordinator.PresentationChanged += HandlePresentationChanged;
            coordinatorBound = true;
        }

        private void UnbindCoordinator()
        {
            if (!coordinatorBound || coordinator == null)
            {
                coordinatorBound = false;
                return;
            }

            coordinator.PresentationChanged -= HandlePresentationChanged;
            coordinatorBound = false;
        }

        private void HandlePresentationChanged()
        {
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            visibilityRequested = hasManualVisibility
                ? manualVisibility
                : coordinator != null && IsRecordingCueState(coordinator.State);
            if (!visibilityRequested)
            {
                HideRays();
            }
        }

        private static bool IsRecordingCueState(RecordingFlowState state)
        {
            return state == RecordingFlowState.Countdown ||
                   state == RecordingFlowState.Recording;
        }

        private bool RefreshHandRay(HandRaySource hand)
        {
            if (!TryGetFingerRay(hand, out Vector3 origin, out Vector3 direction))
            {
                hand.Line.enabled = false;
                return false;
            }

            origin += direction * 0.008f;
            var ray = new Ray(origin, direction);
            bool hit = targetVisualCues.RaycastActiveBounds(
                ray,
                maximumDistance,
                out float distance,
                out _
            );

            float visibleDistance = hit
                ? Mathf.Max(0.04f, distance)
                : maximumDistance;
            Color color = hit ? hitColor : searchingColor;
            hand.Line.enabled = true;
            hand.Line.startWidth = hit ? startWidth * 1.2f : startWidth;
            hand.Line.endWidth = hit ? endWidth * 1.2f : endWidth;
            hand.Line.startColor = color;
            hand.Line.endColor = color;
            hand.Line.SetPosition(0, origin);
            hand.Line.SetPosition(1, origin + direction * visibleDistance);
            return hit;
        }

        private bool TryGetFingerRay(
            HandRaySource hand,
            out Vector3 origin,
            out Vector3 direction)
        {
            if (TryGetSkeletonRay(hand, out origin, out direction))
            {
                return true;
            }

            if (TryGetInteractionHandRay(hand, out origin, out direction))
            {
                return true;
            }

#if UNITY_EDITOR
            if (TryGetEditorSimulationRay(hand, out origin, out direction))
            {
                UsingEditorSimulation = true;
                return true;
            }
#endif

            origin = default;
            direction = default;
            return false;
        }

        private bool TryGetSkeletonRay(
            HandRaySource hand,
            out Vector3 origin,
            out Vector3 direction)
        {
            OVRSkeleton skeleton = hand.Skeleton;
            if (skeleton == null ||
                !skeleton.isActiveAndEnabled ||
                !skeleton.IsInitialized ||
                !skeleton.IsDataValid ||
                !skeleton.IsDataHighConfidence)
            {
                origin = default;
                direction = default;
                return false;
            }

            if (hand.Distal == null || hand.Tip == null)
            {
                ResolveSkeletonBones(hand);
            }

            if (hand.Distal == null || hand.Tip == null)
            {
                origin = default;
                direction = default;
                return false;
            }

            origin = hand.Tip.position;
            direction = origin - hand.Distal.position;
            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = default;
                return false;
            }

            direction.Normalize();
            return true;
        }

        private static bool TryGetInteractionHandRay(
            HandRaySource hand,
            out Vector3 origin,
            out Vector3 direction)
        {
            IHand trackedHand = hand.HandVisual != null
                ? hand.HandVisual.Hand
                : null;
            if (trackedHand == null ||
                !trackedHand.IsConnected ||
                !trackedHand.IsTrackedDataValid ||
                !trackedHand.IsHighConfidence ||
                !trackedHand.GetFingerIsHighConfidence(HandFinger.Index) ||
                !trackedHand.GetJointPose(
                    HandJointId.HandIndex3,
                    out Pose distalPose) ||
                !trackedHand.GetJointPose(
                    HandJointId.HandIndexTip,
                    out Pose tipPose))
            {
                origin = default;
                direction = default;
                return false;
            }

            origin = tipPose.position;
            direction = origin - distalPose.position;
            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = default;
                return false;
            }

            direction.Normalize();
            return true;
        }

#if UNITY_EDITOR
        private bool TryGetEditorSimulationRay(
            HandRaySource hand,
            out Vector3 origin,
            out Vector3 direction)
        {
            if (!Application.isPlaying || !simulateWhenHandsUnavailable)
            {
                origin = default;
                direction = default;
                return false;
            }

            Camera simulationCamera = ResolveSimulationCamera();
            if (simulationCamera == null)
            {
                origin = default;
                direction = default;
                return false;
            }

            float horizontalOffset = hand.IsLeft ? -0.16f : 0.16f;
            origin = simulationCamera.transform.TransformPoint(
                new Vector3(horizontalOffset, -0.18f, 0.42f)
            );

            int preferredTarget = hand.IsLeft ? 0 : 1;
            if (!targetVisualCues.TryGetActiveBounds(
                    preferredTarget,
                    out Bounds targetBounds) &&
                !targetVisualCues.TryGetActiveBounds(0, out targetBounds))
            {
                direction = simulationCamera.transform.forward;
                return true;
            }

            direction = targetBounds.center - origin;
            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = simulationCamera.transform.forward;
                return true;
            }

            direction.Normalize();
            return true;
        }

        private Camera ResolveSimulationCamera()
        {
            if (editorSimulationCamera != null &&
                editorSimulationCamera.isActiveAndEnabled)
            {
                return editorSimulationCamera;
            }

            editorSimulationCamera = Camera.main;
            if (editorSimulationCamera != null &&
                editorSimulationCamera.isActiveAndEnabled)
            {
                return editorSimulationCamera;
            }

            Camera[] activeCameras = Camera.allCameras;
            for (int index = 0; index < activeCameras.Length; index++)
            {
                Camera candidate = activeCameras[index];
                if (candidate != null && candidate.isActiveAndEnabled)
                {
                    editorSimulationCamera = candidate;
                    return editorSimulationCamera;
                }
            }

            editorSimulationCamera = null;
            return null;
        }
#endif

        private void DiscoverMissingSources()
        {
            if (Time.unscaledTime < nextSourceDiscoveryTime ||
                HasTrackedSource(left) && HasTrackedSource(right))
            {
                return;
            }

            nextSourceDiscoveryTime = Time.unscaledTime + 2f;
            OVRSkeleton[] skeletons =
                Object.FindObjectsByType<OVRSkeleton>(FindObjectsInactive.Include);
            for (int index = 0; index < skeletons.Length; index++)
            {
                OVRSkeleton skeleton = skeletons[index];
                switch (skeleton.GetSkeletonType())
                {
                    case OVRSkeleton.SkeletonType.HandLeft:
                    case OVRSkeleton.SkeletonType.XRHandLeft:
                        if (left.Skeleton == null)
                        {
                            SetSkeleton(left, skeleton);
                        }
                        break;
                    case OVRSkeleton.SkeletonType.HandRight:
                    case OVRSkeleton.SkeletonType.XRHandRight:
                        if (right.Skeleton == null)
                        {
                            SetSkeleton(right, skeleton);
                        }
                        break;
                }
            }

            HandVisual[] handVisuals =
                Object.FindObjectsByType<HandVisual>(FindObjectsInactive.Include);
            for (int index = 0; index < handVisuals.Length; index++)
            {
                HandVisual visual = handVisuals[index];
                if (visual == null || visual.Hand == null)
                {
                    continue;
                }

                if (visual.Hand.Handedness == Handedness.Left)
                {
                    if (left.HandVisual == null ||
                        GetVisualPriority(visual) >
                        GetVisualPriority(left.HandVisual))
                    {
                        left.HandVisual = visual;
                    }
                }
                else if (right.HandVisual == null ||
                         GetVisualPriority(visual) >
                         GetVisualPriority(right.HandVisual))
                {
                    right.HandVisual = visual;
                }
            }
        }

        private static bool HasTrackedSource(HandRaySource hand)
        {
            return hand.Skeleton != null ||
                   hand.HandVisual != null && hand.HandVisual.Hand != null;
        }

        private static int GetVisualPriority(HandVisual visual)
        {
            string lowerName = visual.name.ToLowerInvariant();
            if (lowerName.Contains("synthetic") || lowerName.Contains("reticle"))
            {
                return -100;
            }

            if (lowerName.StartsWith("ovrhandvisual"))
            {
                return 100;
            }

            return lowerName.Contains("handvisual") ? 50 : 0;
        }

        private static void SetSkeleton(
            HandRaySource hand,
            OVRSkeleton skeleton)
        {
            hand.Skeleton = skeleton;
            hand.Distal = null;
            hand.Tip = null;
            ResolveSkeletonBones(hand);
        }

        private static void ResolveSkeletonBones(HandRaySource hand)
        {
            OVRSkeleton skeleton = hand.Skeleton;
            if (skeleton == null || skeleton.Bones == null)
            {
                return;
            }

            OVRSkeleton.SkeletonType skeletonType = skeleton.GetSkeletonType();
            bool openXr = skeletonType == OVRSkeleton.SkeletonType.XRHandLeft ||
                          skeletonType == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId distalId = openXr
                ? OVRSkeleton.BoneId.XRHand_IndexDistal
                : OVRSkeleton.BoneId.Hand_Index3;
            OVRSkeleton.BoneId tipId = openXr
                ? OVRSkeleton.BoneId.XRHand_IndexTip
                : OVRSkeleton.BoneId.Hand_IndexTip;

            for (int index = 0; index < skeleton.Bones.Count; index++)
            {
                OVRBone bone = skeleton.Bones[index];
                if (bone == null)
                {
                    continue;
                }

                if (bone.Id == distalId)
                {
                    hand.Distal = bone.Transform;
                }
                else if (bone.Id == tipId)
                {
                    hand.Tip = bone.Transform;
                }
            }
        }

        private void EnsureLines()
        {
            if (!EnsureMaterial())
            {
                return;
            }

            if (left.Line == null)
            {
                left.Line = CreateLine("LeftIndexPointingRay");
            }
            if (right.Line == null)
            {
                right.Line = CreateLine("RightIndexPointingRay");
            }
        }

        private LineRenderer CreateLine(string lineName)
        {
            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.enabled = false;
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 2;
            line.startWidth = startWidth;
            line.endWidth = endWidth;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.sharedMaterial = overlayMaterial;
            return line;
        }

        private bool EnsureMaterial()
        {
            if (overlayMaterial != null)
            {
                return true;
            }

            Shader shader = Resources.Load<Shader>(OverlayShaderResource) ??
                            Shader.Find(OverlayShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    "[RecordingIndexFingerRays] Overlay shader is missing."
                );
                enabled = false;
                return false;
            }

            overlayMaterial = new Material(shader)
            {
                name = "Recording Index Ray Material",
                hideFlags = HideFlags.DontSave
            };
            overlayMaterial.SetColor("_BaseColor", Color.white);
            return true;
        }

        private void HideRays()
        {
            LeftHitsTarget = false;
            RightHitsTarget = false;
            if (left.Line != null)
            {
                left.Line.enabled = false;
            }
            if (right.Line != null)
            {
                right.Line.enabled = false;
            }
        }

        private static void DestroyGeneratedObject(Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maximumDistance = Mathf.Max(0.25f, maximumDistance);
            startWidth = Mathf.Clamp(startWidth, 0.001f, 0.015f);
            endWidth = Mathf.Clamp(endWidth, 0.0005f, 0.01f);
        }
#endif
    }
}
