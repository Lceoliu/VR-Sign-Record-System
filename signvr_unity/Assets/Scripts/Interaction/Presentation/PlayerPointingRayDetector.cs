using System;
using System.Collections.Generic;
using Oculus.Interaction.Input;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Applies the same strict pointing and target-ray rules used by the
    /// instruction ghost to the live Quest player's hands.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-190)]
    public sealed class PlayerPointingRayDetector : MonoBehaviour
    {
        private const string OverlayShaderName =
            "SignVR/Recording Hand Skeleton Overlay";
        private const string OverlayShaderResource =
            "Shaders/RecordingHandSkeletonOverlay";
        private const int MaximumScheduledResolveAttempts = 20;
        private const int PointingRayCount = 2;

        private sealed class TargetGeometry
        {
            public string TargetId;
            public Transform Root;
            public Renderer[] Renderers;
            public Collider[] Colliders;
            public Transform[] HighlightRoots;
            public bool AllowGuidanceSnap;

            public bool ContainsCollider(Collider candidate)
            {
                if (candidate == null)
                {
                    return false;
                }
                for (int index = 0; index < Colliders.Length; index++)
                {
                    if (Colliders[index] == candidate)
                    {
                        return true;
                    }
                }
                return Root != null &&
                    (candidate.transform == Root ||
                     candidate.transform.IsChildOf(Root)) ||
                    HasMatchingLogicalTarget(candidate);
            }

            private bool HasMatchingLogicalTarget(Collider candidate)
            {
                InteractionTargetBinding binding =
                    candidate.GetComponentInParent<InteractionTargetBinding>();
                return binding != null && string.Equals(
                    binding.TargetId,
                    TargetId,
                    StringComparison.Ordinal
                );
            }
        }

        private sealed class HandRig
        {
            public OVRSkeleton Skeleton;
            public IHand InteractionHand;
            public Transform IndexDistal;
            public Transform IndexTip;
            public Transform Palm;
            public Transform[] FoldedFingerTips = Array.Empty<Transform>();
            public Transform[] ThumbChain = Array.Empty<Transform>();
            public Transform[] MiddleChain = Array.Empty<Transform>();
            public Transform[] RingChain = Array.Empty<Transform>();
            public Transform[] LittleChain = Array.Empty<Transform>();
            public bool IsPointing;
            public PointingMetrics Metrics;
            public bool LiveIndexValid;
            public bool LiveThumbChainValid;
            public bool LiveMiddleChainValid;
            public bool LiveRingChainValid;
            public bool LiveLittleChainValid;
            public Vector3 LiveIndexDistal;
            public Vector3 LiveIndexTip;
            public Vector3 LivePalm;
            public readonly Vector3[] LiveThumbChain = new Vector3[4];
            public readonly Vector3[] LiveMiddleChain = new Vector3[5];
            public readonly Vector3[] LiveRingChain = new Vector3[5];
            public readonly Vector3[] LiveLittleChain = new Vector3[5];
            public Vector3 SmoothedRayOrigin;
            public Vector3 SmoothedRayDirection;
            public bool HasSmoothedRay;

            public bool HasValidSkeletonData =>
                Skeleton != null && Skeleton.IsDataValid;

            public bool IsComplete =>
                IndexDistal != null && IndexTip != null && Palm != null &&
                HasFourTips(FoldedFingerTips);

            public void ResetSmoothedRay()
            {
                HasSmoothedRay = false;
                SmoothedRayOrigin = default;
                SmoothedRayDirection = default;
            }

            public bool TryResolveBones()
            {
                if (Skeleton == null && InteractionHand == null)
                {
                    ClearBones();
                    return false;
                }

                Transform distal = null;
                Transform tip = null;
                Transform palm = null;
                Transform thumbTip = null;
                Transform middleTip = null;
                Transform ringTip = null;
                Transform littleTip = null;
                Transform[] trackedThumbChain = new Transform[4];
                Transform[] trackedMiddleChain = new Transform[5];
                Transform[] trackedRingChain = new Transform[5];
                Transform[] trackedLittleChain = new Transform[5];

                if (Skeleton != null && Skeleton.Bones != null)
                {
                    foreach (OVRBone bone in Skeleton.Bones)
                    {
                        if (bone == null || bone.Transform == null)
                        {
                            continue;
                        }

                        string id = bone.Id.ToString();
                        AssignBoneToChain(
                            id,
                            bone.Transform,
                            trackedThumbChain,
                            "ThumbMetacarpal",
                            "ThumbProximal",
                            "ThumbDistal",
                            "ThumbTip"
                        );
                        AssignBoneToChain(
                            id,
                            bone.Transform,
                            trackedMiddleChain,
                            "MiddleMetacarpal",
                            "MiddleProximal",
                            "MiddleIntermediate",
                            "MiddleDistal",
                            "MiddleTip"
                        );
                        AssignBoneToChain(
                            id,
                            bone.Transform,
                            trackedRingChain,
                            "RingMetacarpal",
                            "RingProximal",
                            "RingIntermediate",
                            "RingDistal",
                            "RingTip"
                        );
                        AssignBoneToChain(
                            id,
                            bone.Transform,
                            trackedLittleChain,
                            "LittleMetacarpal",
                            "LittleProximal",
                            "LittleIntermediate",
                            "LittleDistal",
                            "LittleTip"
                        );
                        if (id.EndsWith("IndexDistal", StringComparison.Ordinal) ||
                            id.EndsWith("Index3", StringComparison.Ordinal))
                        {
                            distal ??= bone.Transform;
                        }
                        else if (id.EndsWith("IndexTip", StringComparison.Ordinal))
                        {
                            tip ??= bone.Transform;
                        }
                        else if (id.EndsWith("Palm", StringComparison.Ordinal) ||
                                 id.EndsWith("WristRoot", StringComparison.Ordinal))
                        {
                            palm ??= bone.Transform;
                        }
                        else if (id.EndsWith("ThumbTip", StringComparison.Ordinal))
                        {
                            thumbTip ??= bone.Transform;
                        }
                        else if (id.EndsWith("MiddleTip", StringComparison.Ordinal))
                        {
                            middleTip ??= bone.Transform;
                        }
                        else if (id.EndsWith("RingTip", StringComparison.Ordinal))
                        {
                            ringTip ??= bone.Transform;
                        }
                        else if (id.EndsWith("PinkyTip", StringComparison.Ordinal) ||
                                 id.EndsWith("LittleTip", StringComparison.Ordinal))
                        {
                            littleTip ??= bone.Transform;
                        }
                    }
                }

                // Meta's body skeleton can expose only a subset through
                // OVRSkeleton.Bones while the generated XRHand hierarchy
                // still contains every tracked hand joint. Prefer the live
                // bone entries above and fill only missing joints by name.
                Transform[] thumbChain = Array.Empty<Transform>();
                Transform[] middleChain = Array.Empty<Transform>();
                Transform[] ringChain = Array.Empty<Transform>();
                Transform[] littleChain = Array.Empty<Transform>();
                if (Skeleton != null)
                {
                    Transform hierarchyDistal = FindNamedDescendant(
                        Skeleton.transform,
                        "XRHand_IndexDistal",
                        "Hand_Index3"
                    );
                    Transform hierarchyTip = FindNamedDescendant(
                        Skeleton.transform,
                        "XRHand_IndexTip",
                        "Hand_IndexTip"
                    );
                    Transform hierarchyPalm = FindNamedDescendant(
                        Skeleton.transform,
                        "XRHand_Palm",
                        "FullBody_LeftHandPalm",
                        "Hand_Palm"
                    );
                    Transform hierarchyThumbTip = FindNamedDescendant(
                        Skeleton.transform,
                        "XRHand_ThumbTip",
                        "Hand_ThumbTip"
                    );
                    Transform hierarchyMiddleTip = FindNamedDescendant(
                        Skeleton.transform,
                        "XRHand_MiddleTip",
                        "Hand_MiddleTip"
                    );
                    Transform hierarchyRingTip = FindNamedDescendant(
                        Skeleton.transform,
                        "XRHand_RingTip",
                        "Hand_RingTip"
                    );
                    Transform hierarchyLittleTip = FindNamedDescendant(
                        Skeleton.transform,
                        "XRHand_LittleTip",
                        "Hand_PinkyTip",
                        "XRHand_PinkyTip"
                    );

                    Transform[] hierarchyThumbChain = FindNamedChain(
                        Skeleton.transform,
                        "XRHand_ThumbMetacarpal",
                        "XRHand_ThumbProximal",
                        "XRHand_ThumbDistal",
                        "XRHand_ThumbTip"
                    );
                    Transform[] hierarchyMiddleChain = FindNamedChain(
                        Skeleton.transform,
                        "XRHand_MiddleMetacarpal",
                        "XRHand_MiddleProximal",
                        "XRHand_MiddleIntermediate",
                        "XRHand_MiddleDistal",
                        "XRHand_MiddleTip"
                    );
                    Transform[] hierarchyRingChain = FindNamedChain(
                        Skeleton.transform,
                        "XRHand_RingMetacarpal",
                        "XRHand_RingProximal",
                        "XRHand_RingIntermediate",
                        "XRHand_RingDistal",
                        "XRHand_RingTip"
                    );
                    Transform[] hierarchyLittleChain = FindNamedChain(
                        Skeleton.transform,
                        "XRHand_LittleMetacarpal",
                        "XRHand_LittleProximal",
                        "XRHand_LittleIntermediate",
                        "XRHand_LittleDistal",
                        "XRHand_LittleTip"
                    );

                    // OVRSkeleton.Bones contains the SDK's tracked transforms;
                    // hierarchy lookup only fills joints missing from that
                    // collection, so a duplicate bind-pose hierarchy cannot
                    // replace a live joint.
                    distal ??= hierarchyDistal;
                    tip ??= hierarchyTip;
                    palm ??= hierarchyPalm;
                    thumbTip ??= hierarchyThumbTip;
                    middleTip ??= hierarchyMiddleTip;
                    ringTip ??= hierarchyRingTip;
                    littleTip ??= hierarchyLittleTip;
                    thumbChain = hierarchyThumbChain;
                    middleChain = hierarchyMiddleChain;
                    ringChain = hierarchyRingChain;
                    littleChain = hierarchyLittleChain;
                }

                thumbChain = PreferCompleteChain(trackedThumbChain, thumbChain);
                middleChain = PreferCompleteChain(trackedMiddleChain, middleChain);
                ringChain = PreferCompleteChain(trackedRingChain, ringChain);
                littleChain = PreferCompleteChain(trackedLittleChain, littleChain);

                IndexDistal = distal;
                IndexTip = tip;
                Palm = palm ?? FindAncestor(IndexDistal, "hand", "wrist");
                FoldedFingerTips = new[]
                {
                    thumbTip,
                    middleTip,
                    ringTip,
                    littleTip
                };
                ThumbChain = thumbChain;
                MiddleChain = middleChain;
                RingChain = ringChain;
                LittleChain = littleChain;
                IsPointing = false;
                Metrics = default;
                return IsComplete;
            }

            public bool UpdateLiveJointData()
            {
                LiveIndexValid = false;
                LiveThumbChainValid = false;
                LiveMiddleChainValid = false;
                LiveRingChainValid = false;
                LiveLittleChainValid = false;
                if (InteractionHand == null)
                {
                    return false;
                }

                LiveIndexValid = TryReadJoint(
                    HandJointId.HandPalm,
                    out LivePalm
                ) && TryReadJoint(
                    HandJointId.HandIndex3,
                    out LiveIndexDistal
                ) && TryReadJoint(
                    HandJointId.HandIndexTip,
                    out LiveIndexTip
                );
                LiveThumbChainValid = TryReadChain(
                    new[]
                    {
                        HandJointId.HandThumb1,
                        HandJointId.HandThumb2,
                        HandJointId.HandThumb3,
                        HandJointId.HandThumbTip
                    },
                    LiveThumbChain
                );
                LiveMiddleChainValid = TryReadChain(
                    new[]
                    {
                        HandJointId.HandMiddle0,
                        HandJointId.HandMiddle1,
                        HandJointId.HandMiddle2,
                        HandJointId.HandMiddle3,
                        HandJointId.HandMiddleTip
                    },
                    LiveMiddleChain
                );
                LiveRingChainValid = TryReadChain(
                    new[]
                    {
                        HandJointId.HandRing0,
                        HandJointId.HandRing1,
                        HandJointId.HandRing2,
                        HandJointId.HandRing3,
                        HandJointId.HandRingTip
                    },
                    LiveRingChain
                );
                LiveLittleChainValid = TryReadChain(
                    new[]
                    {
                        HandJointId.HandPinky0,
                        HandJointId.HandPinky1,
                        HandJointId.HandPinky2,
                        HandJointId.HandPinky3,
                        HandJointId.HandPinkyTip
                    },
                    LiveLittleChain
                );
                return LiveIndexValid;
            }

            private bool TryReadJoint(
                HandJointId joint,
                out Vector3 position)
            {
                position = default;
                if (InteractionHand == null)
                {
                    return false;
                }
                try
                {
                    if (!InteractionHand.GetJointPose(joint, out Pose pose))
                    {
                        return false;
                    }
                    position = pose.position;
                    return position.sqrMagnitude < 1000000f;
                }
                catch (Exception exception)
                {
                    position = default;
                    Debug.LogWarning(
                        "[PlayerPointingRayDetector] Hand joint read " +
                        "failed: " + exception.GetType().Name
                    );
                    return false;
                }
            }

            private bool TryReadChain(
                HandJointId[] joints,
                Vector3[] positions)
            {
                if (joints == null || positions == null ||
                    joints.Length != positions.Length)
                {
                    return false;
                }
                for (int index = 0; index < joints.Length; index++)
                {
                    if (!TryReadJoint(joints[index], out positions[index]))
                    {
                        return false;
                    }
                }
                return true;
            }

            private void ClearBones()
            {
                IndexDistal = null;
                IndexTip = null;
                Palm = null;
                FoldedFingerTips = Array.Empty<Transform>();
                ThumbChain = Array.Empty<Transform>();
                MiddleChain = Array.Empty<Transform>();
                RingChain = Array.Empty<Transform>();
                LittleChain = Array.Empty<Transform>();
                IsPointing = false;
                Metrics = default;
                LiveIndexValid = false;
                LiveThumbChainValid = false;
                LiveMiddleChainValid = false;
                LiveRingChainValid = false;
                LiveLittleChainValid = false;
            }
        }

        private struct PointingMetrics
        {
            public bool Valid;
            public bool IsPointing;
            public float IndexAlignment;
            public float IndexReach;
            public float ThumbBend;
            public float MiddleBend;
            public float RingBend;
            public float LittleBend;
            public int BentFingerCount;
            public string FailureReason;
        }

        [Header("Live player source")]
        [SerializeField]
        private VRPlayerRig playerRig;

        [SerializeField]
        private OVRSkeleton leftSkeleton;

        [SerializeField]
        private OVRSkeleton rightSkeleton;

        [Header("Pointing targets")]
        [SerializeField]
        private GhostPointingTargetBinding[] targetBindings =
            Array.Empty<GhostPointingTargetBinding>();

        [SerializeField]
        [Min(0.1f)]
        private float maximumRayDistance = 8f;

        [SerializeField]
        [Min(0f)]
        private float raySmoothingSeconds = 0.09f;

        [SerializeField]
        [Min(0f)]
        private float strictBoundsFallbackPadding = 0.085f;

        [SerializeField]
        [Min(0f)]
        private float rayOcclusionEpsilon = 0.012f;

        [SerializeField]
        private LayerMask rayOcclusionMask = ~0;

        [SerializeField]
        [Min(1)]
        private int consecutiveHitFramesRequired = 30;

        [SerializeField]
        [Min(0.1f)]
        private float highlightDurationSeconds = 3f;

        [SerializeField]
        [Min(0f)]
        private float boundsPadding = 0.035f;

        [SerializeField]
        [Min(0.01f)]
        private float minimumBoundsExtent = 0.035f;

        [SerializeField]
        [Range(0f, 30f)]
        private float guidanceSnapAngleDegrees = 12f;

        [SerializeField]
        [Min(0f)]
        private float guidanceSnapLateralTolerance = 0.22f;

        [SerializeField]
        private int captureExcludedLayer = -1;

        [Header("Player pointing appearance")]
        [SerializeField]
        private InteractionTargetHighlightVisual targetHighlight;

        [SerializeField]
        private Color rayColor = new(0.12f, 0.95f, 1f, 0.88f);

        [SerializeField]
        [Range(0.002f, 0.025f)]
        private float rayWidth = 0.008f;

        private readonly Dictionary<string, TargetGeometry> geometryById =
            new(StringComparer.Ordinal);
        private readonly HandRig leftHand = new();
        private readonly HandRig rightHand = new();
        private readonly RaycastHit[] occlusionHits = new RaycastHit[32];
        private readonly Dictionary<string, int> candidateFramesByTarget =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> highlightExpiryByTarget =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, InteractionTargetHighlightVisual>
            highlightVisualsByTarget =
                new(StringComparer.Ordinal);
        private readonly HashSet<string> observedTargetIds =
            new(StringComparer.Ordinal);
        private readonly List<TargetGeometry> frameTargets = new(2);
        private readonly List<Vector3> frameRayStarts = new(2);
        private readonly List<Vector3> frameRayPoints = new(2);
        private LineRenderer[] rayRenderers = Array.Empty<LineRenderer>();
        private Material rayMaterial;
        private string currentHitTargetId = string.Empty;
        private string diagnosticSummary = "NotConfigured";
        private double nextResolveAttemptTime;
        private double nextMetricsLogTime;
        private int resolveAttempts;
        private bool loggedFirstFrame;
        private string hitCandidateTargetId = string.Empty;
        private int consecutiveHitFrames;
        private string activeHighlightTargetId = string.Empty;
        private double highlightExpiresAt = double.NaN;
        private string primaryHighlightTargetId = string.Empty;

        public VRPlayerRig PlayerRig => playerRig;

        public string CurrentHitTargetId => currentHitTargetId;

        public int ConsecutiveHitFrames => consecutiveHitFrames;

        public float ConsecutiveHitProgress => Mathf.Clamp01(
            consecutiveHitFrames /
            (float)Math.Max(1, consecutiveHitFramesRequired)
        );

        public bool IsHighlightActive => highlightExpiryByTarget.Count > 0 ||
            !string.IsNullOrEmpty(activeHighlightTargetId);

        public bool IsPointingVisible
        {
            get
            {
                for (int index = 0; index < rayRenderers.Length; index++)
                {
                    if (rayRenderers[index] != null &&
                        rayRenderers[index].enabled)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool HasCompleteFingerRig =>
            leftHand.IsComplete || rightHand.IsComplete;

        public string DiagnosticSummary => diagnosticSummary;

        public IReadOnlyList<GhostPointingTargetBinding> TargetBindings =>
            targetBindings;

        public void Configure(
            VRPlayerRig configuredPlayer,
            OVRSkeleton configuredLeftSkeleton,
            OVRSkeleton configuredRightSkeleton,
            GhostPointingTargetBinding[] bindings,
            InteractionTargetHighlightVisual highlight)
        {
            playerRig = configuredPlayer;
            leftSkeleton = configuredLeftSkeleton;
            rightSkeleton = configuredRightSkeleton;
            leftHand.Skeleton = leftSkeleton;
            rightHand.Skeleton = rightSkeleton;
            targetBindings = bindings ?? Array.Empty<GhostPointingTargetBinding>();
            targetHighlight = highlight;
            resolveAttempts = 0;
            RebuildGeometryCache();
            ClearAllHighlights(false);
        }

        public void ConfigureTargetBindings(
            GhostPointingTargetBinding[] bindings)
        {
            targetBindings = bindings ?? Array.Empty<GhostPointingTargetBinding>();
            RebuildGeometryCache();
        }

        public void ConfigureCaptureExcludedLayer(int layer)
        {
            captureExcludedLayer = layer;
            ApplyCaptureExcludedLayer();
        }

        private void Awake()
        {
            Debug.Log(
                "[PlayerPointingRayDetector] Awake enabled=" + enabled +
                ", active=" + gameObject.activeInHierarchy,
                this
            );
            leftHand.Skeleton = leftSkeleton;
            rightHand.Skeleton = rightSkeleton;
            EnsureFallbackConfiguration();
            EnsureRayRenderer();
            RebuildGeometryCache();
            ApplyCaptureExcludedLayer();
        }

        private void OnEnable()
        {
            Debug.Log(
                "[PlayerPointingRayDetector] OnEnable active=" +
                gameObject.activeInHierarchy,
                this
            );
            ResolveBonesIfDue(true);
        }

        private void LateUpdate()
        {
            if (!loggedFirstFrame)
            {
                loggedFirstFrame = true;
                Debug.Log(
                    "[PlayerPointingRayDetector] LateUpdate first frame " +
                    "enabled=" + enabled + ", active=" +
                    gameObject.activeInHierarchy,
                    this
                );
            }
            EnsureFallbackConfiguration();
            ResolveBonesIfDue(false);
            UpdatePointingDiagnostics(leftHand);
            UpdatePointingDiagnostics(rightHand);
            LogPointingDiagnosticsIfDue();

            double now = Time.realtimeSinceStartupAsDouble;
            ExpireHighlightIfDue(now);
            observedTargetIds.Clear();
            frameTargets.Clear();
            frameRayStarts.Clear();
            frameRayPoints.Clear();
            CollectHandHit(leftHand);
            CollectHandHit(rightHand);
            HideUnusedRays(frameTargets.Count);

            for (int index = 0; index < frameTargets.Count; index++)
            {
                TargetGeometry target = frameTargets[index];
                ShowRay(index, frameRayStarts[index], frameRayPoints[index]);
                if (observedTargetIds.Add(target.TargetId) &&
                    ObserveTargetHit(target, now))
                {
                    ActivateHighlight(target, now);
                }
            }

            RemoveUnobservedCandidates();
            if (frameTargets.Count == 0)
            {
                ResetHitCandidate();
                currentHitTargetId = GetFirstActiveHighlightId();
                diagnosticSummary = geometryById.Count == 0
                    ? "NoEligibleTargets"
                    : HasCompleteFingerRig ? GetHighlightDiagnostic(now)
                    : "IncompleteFingerRig";
                return;
            }

            currentHitTargetId = frameTargets[0].TargetId;
            diagnosticSummary = GetFrameDiagnostic(now);
        }

        private void CollectHandHit(HandRig hand)
        {
            if (hand == null || !TryFindHandHit(
                    hand,
                    out TargetGeometry target,
                    out Vector3 rayStart,
                    out Vector3 hitPoint))
            {
                return;
            }
            frameTargets.Add(target);
            frameRayStarts.Add(rayStart);
            frameRayPoints.Add(hitPoint);
        }

        private bool ObserveTargetHit(TargetGeometry target, double now)
        {
            if (target == null || string.IsNullOrEmpty(target.TargetId))
            {
                return false;
            }
            if (highlightExpiryByTarget.ContainsKey(target.TargetId))
            {
                candidateFramesByTarget.Remove(target.TargetId);
                return false;
            }
            int frames = candidateFramesByTarget.TryGetValue(
                target.TargetId,
                out int existing
            ) ? existing + 1 : 1;
            candidateFramesByTarget[target.TargetId] = Math.Min(
                Math.Max(1, consecutiveHitFramesRequired),
                frames
            );
            hitCandidateTargetId = target.TargetId;
            consecutiveHitFrames = candidateFramesByTarget[target.TargetId];
            return frames >= Math.Max(1, consecutiveHitFramesRequired);
        }

        private void RemoveUnobservedCandidates()
        {
            var stale = new List<string>();
            foreach (string targetId in candidateFramesByTarget.Keys)
            {
                if (!observedTargetIds.Contains(targetId))
                {
                    stale.Add(targetId);
                }
            }
            for (int index = 0; index < stale.Count; index++)
            {
                candidateFramesByTarget.Remove(stale[index]);
            }
        }

        private string GetFrameDiagnostic(double now)
        {
            if (highlightExpiryByTarget.Count > 0)
            {
                return GetHighlightDiagnostic(now);
            }
            string targetId = frameTargets[0].TargetId;
            int frames = candidateFramesByTarget.TryGetValue(
                targetId,
                out int count
            ) ? count : 0;
            return string.Format(
                "candidate:{0}:{1}/{2}frames",
                targetId,
                frames,
                Math.Max(1, consecutiveHitFramesRequired)
            );
        }

        private string GetHighlightDiagnostic(double now)
        {
            string id = GetFirstActiveHighlightId();
            if (string.IsNullOrEmpty(id))
            {
                return "NoHit";
            }
            double expiry = highlightExpiryByTarget.TryGetValue(id, out double value)
                ? value
                : now;
            return string.Format(
                "highlight:{0}:{1:F2}s:count={2}",
                id,
                Math.Max(0d, expiry - now),
                highlightExpiryByTarget.Count
            );
        }

        private bool ObserveConsecutiveHit(string targetId)
        {
            if (!string.Equals(
                    hitCandidateTargetId,
                    targetId,
                    StringComparison.Ordinal))
            {
                hitCandidateTargetId = targetId ?? string.Empty;
                consecutiveHitFrames = string.IsNullOrEmpty(
                    hitCandidateTargetId
                ) ? 0 : 1;
                Debug.Log(
                    "[PlayerPointingRayDetector] Hit sequence started target=" +
                    hitCandidateTargetId,
                    this
                );
            }
            else if (!string.IsNullOrEmpty(hitCandidateTargetId))
            {
                consecutiveHitFrames++;
            }

            return consecutiveHitFrames >=
                Math.Max(1, consecutiveHitFramesRequired);
        }

        private void ActivateHighlight(TargetGeometry target, double now)
        {
            if (target == null || string.IsNullOrEmpty(target.TargetId) ||
                highlightExpiryByTarget.ContainsKey(target.TargetId))
            {
                return;
            }

            double expiry = now + Math.Max(0.1f, highlightDurationSeconds);
            var currentIds = new List<string>(highlightExpiryByTarget.Keys);
            for (int index = 0; index < currentIds.Count; index++)
            {
                highlightExpiryByTarget[currentIds[index]] = expiry;
            }
            highlightExpiryByTarget[target.TargetId] = expiry;
            activeHighlightTargetId = target.TargetId;
            primaryHighlightTargetId = target.TargetId;
            highlightExpiresAt = expiry;
            currentHitTargetId = target.TargetId;
            diagnosticSummary = "highlight:" + target.TargetId;
            GetHighlightVisual(target.TargetId)?.Show(
                target.Root,
                target.HighlightRoots
            );
            Debug.Log(
                "[PlayerPointingRayDetector] Highlight started target=" +
                target.TargetId + " frames=" +
                consecutiveHitFrames + " duration=" +
                highlightDurationSeconds.ToString("F2") + "s",
                this
            );
            candidateFramesByTarget.Remove(target.TargetId);
            if (string.Equals(
                    hitCandidateTargetId,
                    target.TargetId,
                    StringComparison.Ordinal))
            {
                hitCandidateTargetId = string.Empty;
                consecutiveHitFrames = 0;
            }
        }

        private void ExpireHighlightIfDue(double now)
        {
            if (highlightExpiryByTarget.Count == 0)
            {
                return;
            }
            var expired = new List<string>();
            foreach (KeyValuePair<string, double> item in highlightExpiryByTarget)
            {
                if (now >= item.Value)
                {
                    expired.Add(item.Key);
                }
            }
            for (int index = 0; index < expired.Count; index++)
            {
                string targetId = expired[index];
                highlightExpiryByTarget.Remove(targetId);
                if (highlightVisualsByTarget.TryGetValue(
                        targetId,
                        out InteractionTargetHighlightVisual visual))
                {
                    visual.Hide();
                }
            }
            activeHighlightTargetId = GetFirstActiveHighlightId();
            highlightExpiresAt = string.IsNullOrEmpty(activeHighlightTargetId)
                ? double.NaN
                : highlightExpiryByTarget[activeHighlightTargetId];
            currentHitTargetId = activeHighlightTargetId;
        }

        private void ResetHitCandidate()
        {
            hitCandidateTargetId = string.Empty;
            consecutiveHitFrames = 0;
            candidateFramesByTarget.Clear();
        }

        private string GetFirstActiveHighlightId()
        {
            if (!string.IsNullOrEmpty(primaryHighlightTargetId) &&
                highlightExpiryByTarget.ContainsKey(primaryHighlightTargetId))
            {
                return primaryHighlightTargetId;
            }
            foreach (string targetId in highlightExpiryByTarget.Keys)
            {
                primaryHighlightTargetId = targetId;
                return targetId;
            }
            primaryHighlightTargetId = string.Empty;
            return string.Empty;
        }

        private InteractionTargetHighlightVisual GetHighlightVisual(
            string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                return null;
            }
            if (highlightVisualsByTarget.TryGetValue(
                    targetId,
                    out InteractionTargetHighlightVisual existing))
            {
                return existing;
            }

            InteractionTargetHighlightVisual visual = null;
            if (highlightVisualsByTarget.Count == 0 && targetHighlight != null)
            {
                visual = targetHighlight;
            }
            else
            {
                GameObject visualObject = new GameObject(
                    "PlayerPointingHighlight_" + targetId
                );
                visualObject.transform.SetParent(transform, false);
                visual = visualObject.AddComponent<
                    InteractionTargetHighlightVisual>();
            }
            ApplyHighlightCaptureExcludedLayer(visual);
            highlightVisualsByTarget[targetId] = visual;
            return visual;
        }

        private void ClearAllHighlights(bool destroyDynamic)
        {
            foreach (InteractionTargetHighlightVisual visual in
                     highlightVisualsByTarget.Values)
            {
                if (visual != null)
                {
                    visual.Clear();
                }
            }
            if (destroyDynamic)
            {
                foreach (KeyValuePair<string,
                         InteractionTargetHighlightVisual> item in
                         highlightVisualsByTarget)
                {
                    if (item.Value != null && item.Value != targetHighlight)
                    {
                        if (Application.isPlaying)
                        {
                            Destroy(item.Value.gameObject);
                        }
                        else
                        {
                            DestroyImmediate(item.Value.gameObject);
                        }
                    }
                }
                highlightVisualsByTarget.Clear();
            }
            highlightExpiryByTarget.Clear();
            activeHighlightTargetId = string.Empty;
            primaryHighlightTargetId = string.Empty;
            highlightExpiresAt = double.NaN;
        }

        private void EnsureFallbackConfiguration()
        {
            if (playerRig == null)
            {
                playerRig = GetComponent<VRPlayerRig>() ??
                    VRPlayerRig.Instance;
            }

            if (leftSkeleton == null || rightSkeleton == null ||
                leftHand.InteractionHand == null ||
                rightHand.InteractionHand == null)
            {
                InteractionStudyCaptureBinding binding =
                    FindAnyObjectByType<InteractionStudyCaptureBinding>();
                if (binding != null)
                {
                    leftSkeleton ??= binding.LeftSkeleton as OVRSkeleton;
                    rightSkeleton ??= binding.RightSkeleton as OVRSkeleton;
                    leftHand.InteractionHand ??= binding.LeftHand as IHand;
                    rightHand.InteractionHand ??= binding.RightHand as IHand;
                }
            }
            leftHand.Skeleton = leftSkeleton;
            rightHand.Skeleton = rightSkeleton;
            ResolveInteractionHandsIfNeeded();

            if (targetBindings == null || targetBindings.Length == 0)
            {
                GhostPointingDetector ghost =
                    FindAnyObjectByType<GhostPointingDetector>();
                if (ghost != null && ghost.TargetBindings.Count > 0)
                {
                    var copied = new GhostPointingTargetBinding[
                        ghost.TargetBindings.Count
                    ];
                    for (int index = 0; index < copied.Length; index++)
                    {
                        copied[index] = ghost.TargetBindings[index];
                    }
                    targetBindings = copied;
                    RebuildGeometryCache();
                }
            }
        }

        private void ResolveInteractionHandsIfNeeded()
        {
            if (leftHand.InteractionHand != null &&
                rightHand.InteractionHand != null)
            {
                return;
            }

            Hand[] hands = FindObjectsOfType<Hand>(true);
            for (int index = 0; index < hands.Length; index++)
            {
                Hand candidate = hands[index];
                if (candidate == null)
                {
                    continue;
                }
                string path = GetHierarchyPath(candidate.transform);
                bool isLeft = path.IndexOf(
                    "left",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
                bool isRight = path.IndexOf(
                    "right",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
                if (isLeft == isRight)
                {
                    continue;
                }
                if (isLeft)
                {
                    leftHand.InteractionHand ??= candidate;
                }
                else if (isRight)
                {
                    rightHand.InteractionHand ??= candidate;
                }
            }
        }

        private static string GetHierarchyPath(Transform value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            var names = new List<string>(8);
            Transform current = value;
            while (current != null && names.Count < 16)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private void ResolveBonesIfDue(bool force)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now < nextResolveAttemptTime &&
                leftHand.IsComplete && rightHand.IsComplete)
            {
                return;
            }
            if (!force && now < nextResolveAttemptTime &&
                resolveAttempts >= MaximumScheduledResolveAttempts)
            {
                return;
            }

            bool leftResolved = leftHand.TryResolveBones();
            bool rightResolved = rightHand.TryResolveBones();
            if (leftResolved && rightResolved)
            {
                diagnosticSummary = "Ready";
                resolveAttempts = 0;
                nextResolveAttemptTime = now + 1d;
                return;
            }

            resolveAttempts++;
            nextResolveAttemptTime = now + 0.25d;
            diagnosticSummary = leftResolved || rightResolved
                ? "OneHandRigReady"
                : "WaitingForHandBones";
            if (resolveAttempts == 1 || resolveAttempts % 20 == 0)
            {
                Debug.LogWarning(
                    "[PlayerPointingRayDetector] Waiting for live hand " +
                    $"bones (left={leftResolved}, right={rightResolved}).",
                    this
                );
            }
        }

        private bool TryFindNearestHit(
            out TargetGeometry nearestTarget,
            out Vector3 rayStart,
            out Vector3 hitPoint)
        {
            nearestTarget = null;
            rayStart = default;
            hitPoint = default;
            float nearestDistance = maximumRayDistance;
            EvaluateHandRay(
                leftHand,
                ref nearestTarget,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            EvaluateHandRay(
                rightHand,
                ref nearestTarget,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            if (nearestTarget != null)
            {
                return true;
            }

            float nearestAngle = guidanceSnapAngleDegrees;
            EvaluateGuidanceRay(
                leftHand,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            EvaluateGuidanceRay(
                rightHand,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            return nearestTarget != null;
        }

        private bool TryFindHandHit(
            HandRig hand,
            out TargetGeometry nearestTarget,
            out Vector3 rayStart,
            out Vector3 hitPoint)
        {
            nearestTarget = null;
            rayStart = default;
            hitPoint = default;
            float nearestDistance = maximumRayDistance;
            EvaluateHandRay(
                hand,
                ref nearestTarget,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            if (nearestTarget != null)
            {
                return true;
            }

            float nearestAngle = guidanceSnapAngleDegrees;
            EvaluateGuidanceRay(
                hand,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            return nearestTarget != null;
        }

        private void EvaluateHandRay(
            HandRig hand,
            ref TargetGeometry nearestTarget,
            ref float nearestDistance,
            ref Vector3 nearestStart,
            ref Vector3 nearestPoint)
        {
            if (!TryGetStableIndexRay(
                    hand,
                    out Vector3 indexTip,
                    out Vector3 direction))
            {
                return;
            }
            var ray = new Ray(indexTip, direction);
            bool strictPointing = hand.IsPointing;

            foreach (TargetGeometry candidate in geometryById.Values)
            {
                if (!strictPointing && Application.isPlaying &&
                    !IsLegacyCabinetOrButtonTarget(candidate.TargetId))
                {
                    continue;
                }

                float distance;
                Vector3 point = default;
                bool accepted = false;
                if (strictPointing)
                {
                    if (!TryRaycastTarget(
                            candidate,
                            ray,
                            nearestDistance,
                            out distance,
                            out point))
                    {
                        distance = 0f;
                    }
                    else if (!IsRayOccluded(candidate, ray, distance))
                    {
                        accepted = true;
                    }

                    // Small or distant meshes are easy to miss by a single
                    // tracked ray sample. Use an expanded render/collider
                    // bound only after exact raycast misses, and keep the
                    // same occlusion test so this cannot shoot through doors.
                    if (!accepted && TryCalculateBounds(
                            candidate,
                            out Bounds fallbackBounds))
                    {
                        fallbackBounds.Expand(strictBoundsFallbackPadding * 2f);
                        if (fallbackBounds.IntersectRay(
                                ray,
                                out distance) &&
                            distance >= 0f &&
                            distance <= nearestDistance &&
                            !IsRayOccluded(candidate, ray, distance))
                        {
                            point = ray.GetPoint(distance);
                            accepted = true;
                        }
                    }
                }
                else if (!TryCalculateBounds(candidate, out Bounds bounds) ||
                         !bounds.IntersectRay(ray, out distance) ||
                         distance < 0f || distance > nearestDistance ||
                         IsRayOccluded(candidate, ray, distance))
                {
                    continue;
                }

                if (!accepted && strictPointing)
                {
                    continue;
                }

                nearestTarget = candidate;
                nearestDistance = distance;
                nearestStart = indexTip;
                nearestPoint = strictPointing ? point : ray.GetPoint(distance);
            }
        }

        private void EvaluateGuidanceRay(
            HandRig hand,
            ref TargetGeometry nearestTarget,
            ref float nearestAngle,
            ref float nearestDistance,
            ref Vector3 nearestStart,
            ref Vector3 nearestPoint)
        {
            if (!TryGetStableIndexRay(
                    hand,
                    out Vector3 indexTip,
                    out Vector3 direction))
            {
                return;
            }
            bool strictPointing = hand.IsPointing;

            foreach (TargetGeometry candidate in geometryById.Values)
            {
                if (!candidate.AllowGuidanceSnap ||
                    (!strictPointing && Application.isPlaying &&
                     !IsLegacyCabinetOrButtonTarget(candidate.TargetId)) ||
                    !TryCalculateBounds(candidate, out Bounds bounds))
                {
                    continue;
                }

                Vector3 toCenter = bounds.center - indexTip;
                float distance = Vector3.Dot(toCenter, direction);
                if (distance <= 0f || distance > maximumRayDistance)
                {
                    continue;
                }

                float angle = Vector3.Angle(direction, toCenter);
                Vector3 closestOnRay = indexTip + direction * distance;
                float lateralDistance = Vector3.Distance(
                    closestOnRay,
                    bounds.center
                );
                float allowedLateralDistance =
                    guidanceSnapLateralTolerance + bounds.extents.magnitude;
                if (angle > guidanceSnapAngleDegrees ||
                    lateralDistance > allowedLateralDistance ||
                    angle > nearestAngle ||
                    (Mathf.Approximately(angle, nearestAngle) &&
                     distance >= nearestDistance))
                {
                    continue;
                }

                if (IsRayOccluded(candidate, new Ray(indexTip, direction), distance))
                {
                    continue;
                }

                nearestTarget = candidate;
                nearestAngle = angle;
                nearestDistance = distance;
                nearestStart = indexTip;
                nearestPoint = bounds.center;
            }
        }

        private static bool TryGetIndexRayRaw(
            HandRig hand,
            out Vector3 indexDistal,
            out Vector3 indexTip)
        {
            indexDistal = default;
            indexTip = default;
            if (hand == null)
            {
                return false;
            }
            if (hand.LiveIndexValid)
            {
                indexDistal = hand.LiveIndexDistal;
                indexTip = hand.LiveIndexTip;
                return true;
            }
            if (hand.IndexDistal == null || hand.IndexTip == null)
            {
                return false;
            }
            indexDistal = hand.IndexDistal.position;
            indexTip = hand.IndexTip.position;
            return true;
        }

        private bool TryGetStableIndexRay(
            HandRig hand,
            out Vector3 origin,
            out Vector3 direction)
        {
            origin = default;
            direction = default;
            if (!TryGetIndexRayRaw(
                    hand,
                    out Vector3 rawDistal,
                    out Vector3 rawTip))
            {
                hand?.ResetSmoothedRay();
                return false;
            }

            Vector3 rawDirection = rawTip - rawDistal;
            if (rawDirection.sqrMagnitude < 0.000001f)
            {
                hand.ResetSmoothedRay();
                return false;
            }
            rawDirection.Normalize();
            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float smoothingSeconds = Mathf.Max(0f, raySmoothingSeconds);
            float blend = smoothingSeconds <= 0f
                ? 1f
                : 1f - Mathf.Exp(-deltaTime / smoothingSeconds);
            if (!hand.HasSmoothedRay)
            {
                hand.SmoothedRayOrigin = rawTip;
                hand.SmoothedRayDirection = rawDirection;
                hand.HasSmoothedRay = true;
            }
            else
            {
                hand.SmoothedRayOrigin = Vector3.Lerp(
                    hand.SmoothedRayOrigin,
                    rawTip,
                    blend
                );
                hand.SmoothedRayDirection = Vector3.Slerp(
                    hand.SmoothedRayDirection,
                    rawDirection,
                    blend
                ).normalized;
            }

            origin = hand.SmoothedRayOrigin;
            direction = hand.SmoothedRayDirection;
            return direction.sqrMagnitude > 0.000001f;
        }

        private static bool TryGetIndexPoints(
            HandRig hand,
            out Vector3 indexDistal,
            out Vector3 indexTip,
            out Vector3 palmPosition)
        {
            indexDistal = default;
            indexTip = default;
            palmPosition = default;
            if (hand == null)
            {
                return false;
            }
            if (hand.LiveIndexValid)
            {
                indexDistal = hand.LiveIndexDistal;
                indexTip = hand.LiveIndexTip;
                palmPosition = hand.LivePalm;
                return true;
            }
            if (hand.IndexDistal == null || hand.IndexTip == null ||
                hand.Palm == null)
            {
                return false;
            }
            indexDistal = hand.IndexDistal.position;
            indexTip = hand.IndexTip.position;
            palmPosition = hand.Palm.position;
            return true;
        }

        private static Vector3 GetTipPosition(
            HandRig hand,
            int index,
            out bool available)
        {
            available = false;
            if (hand == null)
            {
                return default;
            }

            switch (index)
            {
                case 0 when hand.LiveThumbChainValid:
                    available = true;
                    return hand.LiveThumbChain[hand.LiveThumbChain.Length - 1];
                case 1 when hand.LiveMiddleChainValid:
                    available = true;
                    return hand.LiveMiddleChain[hand.LiveMiddleChain.Length - 1];
                case 2 when hand.LiveRingChainValid:
                    available = true;
                    return hand.LiveRingChain[hand.LiveRingChain.Length - 1];
                case 3 when hand.LiveLittleChainValid:
                    available = true;
                    return hand.LiveLittleChain[hand.LiveLittleChain.Length - 1];
            }

            Transform tip = GetTip(hand.FoldedFingerTips, index);
            if (tip == null)
            {
                return default;
            }
            available = true;
            return tip.position;
        }

        private static bool TryRaycastTarget(
            TargetGeometry geometry,
            Ray ray,
            float maximumDistance,
            out float nearestDistance,
            out Vector3 nearestPoint)
        {
            nearestDistance = maximumDistance;
            nearestPoint = default;
            bool hitAny = false;
            for (int index = 0; index < geometry.Colliders.Length; index++)
            {
                Collider collider = geometry.Colliders[index];
                if (collider == null || !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!collider.Raycast(
                        ray,
                        out RaycastHit hit,
                        nearestDistance))
                {
                    continue;
                }
                hitAny = true;
                nearestDistance = hit.distance;
                nearestPoint = hit.point;
            }
            return hitAny;
        }

        private bool IsRayOccluded(
            TargetGeometry target,
            Ray ray,
            float targetDistance)
        {
            float castDistance = targetDistance -
                Mathf.Max(0.001f, rayOcclusionEpsilon);
            if (castDistance <= 0f || target == null)
            {
                return false;
            }

            int hitCount = Physics.RaycastNonAlloc(
                ray,
                occlusionHits,
                castDistance,
                rayOcclusionMask.value,
                QueryTriggerInteraction.Ignore
            );
            for (int index = 0; index < hitCount; index++)
            {
                Collider collider = occlusionHits[index].collider;
                if (collider == null ||
                    target.ContainsCollider(collider) ||
                    IsPlayerCollider(collider))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        private bool IsPlayerCollider(Collider collider)
        {
            if (collider == null)
            {
                return true;
            }
            Transform colliderTransform = collider.transform;
            if (colliderTransform == transform ||
                colliderTransform.IsChildOf(transform))
            {
                return true;
            }
            return playerRig != null &&
                (colliderTransform == playerRig.transform ||
                 colliderTransform.IsChildOf(playerRig.transform));
        }

        private static bool EvaluatePointingHand(
            HandRig hand,
            out PointingMetrics metrics)
        {
            metrics = default;
            if (!TryGetIndexPoints(
                    hand,
                    out Vector3 indexDistal,
                    out Vector3 indexTip,
                    out Vector3 palmPosition))
            {
                metrics.FailureReason = "missing-index-or-palm";
                return false;
            }

            // Do not treat the generated bind pose as the user's hand when
            // the headset is currently in controller-only/no-hand-tracking
            // mode. The legacy button/cabinet fallback remains available to
            // the ray evaluator, but strict collider hits require live data.
            if (!hand.HasValidSkeletonData && !hand.LiveIndexValid)
            {
                metrics.FailureReason = "skeleton-data-invalid";
                return false;
            }

            Vector3 indexSegment = indexTip - indexDistal;
            Vector3 indexReach = indexTip - palmPosition;
            if (indexSegment.sqrMagnitude < 0.000001f)
            {
                metrics.FailureReason = "index-segment-too-short";
                return false;
            }
            if (indexReach.sqrMagnitude < 0.0009f)
            {
                metrics.FailureReason = "index-reach-too-short";
                return false;
            }

            metrics.Valid = true;
            metrics.IndexAlignment = Vector3.Dot(
                indexSegment.normalized,
                indexReach.normalized
            );
            metrics.IndexReach = indexReach.magnitude;

            // Require a nearly collinear distal segment and palm-to-tip reach.
            // This rejects relaxed/open hands that happen to pass near a
            // target while still allowing normal Quest tracking noise.
            if (!PointingHandShapeRules.IsIndexExtended(
                    indexDistal,
                    indexTip,
                    palmPosition,
                    metrics.IndexReach,
                    metrics.IndexAlignment))
            {
                metrics.FailureReason = "index-not-extended";
                return false;
            }

            metrics.ThumbBend = CalculateBendRatio(
                hand.LiveThumbChain,
                hand.LiveThumbChainValid,
                hand.ThumbChain,
                palmPosition,
                GetTipPosition(hand, 0, out bool hasThumbTip),
                hasThumbTip,
                metrics.IndexReach
            );
            metrics.MiddleBend = CalculateBendRatio(
                hand.LiveMiddleChain,
                hand.LiveMiddleChainValid,
                hand.MiddleChain,
                palmPosition,
                GetTipPosition(hand, 1, out bool hasMiddleTip),
                hasMiddleTip,
                metrics.IndexReach
            );
            metrics.RingBend = CalculateBendRatio(
                hand.LiveRingChain,
                hand.LiveRingChainValid,
                hand.RingChain,
                palmPosition,
                GetTipPosition(hand, 2, out bool hasRingTip),
                hasRingTip,
                metrics.IndexReach
            );
            metrics.LittleBend = CalculateBendRatio(
                hand.LiveLittleChain,
                hand.LiveLittleChainValid,
                hand.LittleChain,
                palmPosition,
                GetTipPosition(hand, 3, out bool hasLittleTip),
                hasLittleTip,
                metrics.IndexReach
            );

            metrics.BentFingerCount = 0;
            if (IsNotFullyExtended(metrics.ThumbBend))
            {
                metrics.BentFingerCount++;
            }
            if (IsNotFullyExtended(metrics.MiddleBend))
            {
                metrics.BentFingerCount++;
            }
            if (IsNotFullyExtended(metrics.RingBend))
            {
                metrics.BentFingerCount++;
            }
            if (IsNotFullyExtended(metrics.LittleBend))
            {
                metrics.BentFingerCount++;
            }

            if (metrics.BentFingerCount < 4)
            {
                metrics.FailureReason = "one-or-more-fingers-straight";
                return false;
            }

            metrics.IsPointing = true;
            metrics.FailureReason = "ok";
            return true;
        }

        private static float CalculateBendRatio(
            Vector3[] liveChain,
            bool liveChainValid,
            Transform[] chain,
            Vector3 palmPosition,
            Vector3 fallbackTip,
            bool hasFallbackTip,
            float indexReach)
        {
            if (liveChainValid)
            {
                return CalculateBendRatio(liveChain);
            }
            if (HasValidChain(chain))
            {
                return CalculateBendRatio(chain);
            }

            // Some runtimes expose tips but omit intermediate joints. This
            // fallback only rejects a tip that is as far from the palm as the
            // pointing index; it intentionally does not require a tightly
            // curled hand.
            if (hasFallbackTip && indexReach > 0.0001f)
            {
                float tipDistance = Vector3.Distance(
                    palmPosition,
                    fallbackTip
                );
                return tipDistance <= indexReach * 1.05f ? 1.06f : 1f;
            }
            return 0f;
        }

        private static float CalculateBendRatio(Vector3[] chain)
        {
            if (chain == null || chain.Length < 3)
            {
                return 0f;
            }
            float pathLength = 0f;
            for (int index = 1; index < chain.Length; index++)
            {
                pathLength += Vector3.Distance(
                    chain[index - 1],
                    chain[index]
                );
            }
            float directLength = Vector3.Distance(
                chain[0],
                chain[chain.Length - 1]
            );
            return directLength > 0.0001f
                ? pathLength / directLength
                : 0f;
        }

        private static float CalculateBendRatio(Transform[] chain)
        {
            if (!HasValidChain(chain))
            {
                return 0f;
            }
            float pathLength = 0f;
            for (int index = 1; index < chain.Length; index++)
            {
                pathLength += Vector3.Distance(
                    chain[index - 1].position,
                    chain[index].position
                );
            }
            float directLength = Vector3.Distance(
                chain[0].position,
                chain[chain.Length - 1].position
            );
            return directLength > 0.0001f
                ? pathLength / directLength
                : 0f;
        }

        private static bool IsNotFullyExtended(float bendRatio)
        {
            // A perfectly straight chain has a ratio of 1.0. A very small
            // 2% bend is enough to satisfy the user's "not fully extended"
            // requirement without demanding a fist or a large curl.
            return bendRatio >= 1.02f;
        }

        private static void UpdatePointingDiagnostics(HandRig hand)
        {
            if (hand == null)
            {
                return;
            }
            hand.UpdateLiveJointData();
            hand.IsPointing = EvaluatePointingHand(
                hand,
                out PointingMetrics metrics
            );
            hand.Metrics = metrics;
        }

        private void LogPointingDiagnosticsIfDue()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextMetricsLogTime)
            {
                return;
            }

            nextMetricsLogTime = now + 1d;
            Debug.Log(
                "[PlayerPointingRayDetector] Pointing metrics " +
                FormatHandMetrics("left", leftHand) + " " +
                FormatHandMetrics("right", rightHand),
                this
            );
        }

        private static string FormatHandMetrics(string label, HandRig hand)
        {
            if (hand == null || !hand.Metrics.Valid)
            {
                return label + "=unavailable(" +
                    (hand?.Metrics.FailureReason ?? "missing") + ")";
            }

            PointingMetrics metrics = hand.Metrics;
            return string.Format(
                "{0}=strict:{1},skeleton:{2},sdkPose:{3},align:{4:F2},reach:{5:F3},bend:[{6:F3},{7:F3},{8:F3},{9:F3}],bent:{10}/4,reason:{11}",
                label,
                metrics.IsPointing,
                hand.HasValidSkeletonData,
                hand.LiveIndexValid,
                metrics.IndexAlignment,
                metrics.IndexReach,
                metrics.ThumbBend,
                metrics.MiddleBend,
                metrics.RingBend,
                metrics.LittleBend,
                metrics.BentFingerCount,
                metrics.FailureReason ?? "unknown"
            );
        }

        private static Transform GetTip(Transform[] tips, int index)
        {
            return tips != null && index >= 0 && index < tips.Length
                ? tips[index]
                : null;
        }

        private static bool HasValidChain(Transform[] chain)
        {
            if (chain == null || chain.Length < 3)
            {
                return false;
            }
            for (int index = 0; index < chain.Length; index++)
            {
                if (chain[index] == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsLegacyCabinetOrButtonTarget(string targetId)
        {
            return !string.IsNullOrWhiteSpace(targetId) &&
                (targetId.StartsWith("button_", StringComparison.Ordinal) ||
                 targetId.StartsWith("key_", StringComparison.Ordinal) ||
                 string.Equals(targetId, "motorbike_key", StringComparison.Ordinal));
        }

        private void RebuildGeometryCache()
        {
            geometryById.Clear();
            if (targetBindings == null)
            {
                return;
            }

            for (int index = 0; index < targetBindings.Length; index++)
            {
                GhostPointingTargetBinding binding = targetBindings[index];
                if (binding == null || string.IsNullOrWhiteSpace(binding.TargetId) ||
                    binding.TargetRoot == null)
                {
                    continue;
                }

                string id = binding.TargetId.Trim();
                if (geometryById.ContainsKey(id))
                {
                    Debug.LogError(
                        $"[PlayerPointingRayDetector] Duplicate target binding '{id}'.",
                        this
                    );
                    continue;
                }

                Renderer[] renderers = binding.TargetRoot
                    .GetComponentsInChildren<Renderer>(true);
                var filteredRenderers = new List<Renderer>(renderers.Length);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    if (renderers[rendererIndex] is MeshRenderer ||
                        renderers[rendererIndex] is SkinnedMeshRenderer)
                    {
                        filteredRenderers.Add(renderers[rendererIndex]);
                    }
                }

                var highlightRoots = new Transform[binding.HighlightRoots.Count];
                for (int rootIndex = 0; rootIndex < highlightRoots.Length; rootIndex++)
                {
                    highlightRoots[rootIndex] = binding.HighlightRoots[rootIndex];
                }

                geometryById.Add(id, new TargetGeometry
                {
                    TargetId = id,
                    Root = binding.TargetRoot,
                    Renderers = filteredRenderers.ToArray(),
                    Colliders = binding.TargetRoot.GetComponentsInChildren<Collider>(true),
                    HighlightRoots = highlightRoots,
                    AllowGuidanceSnap = binding.AllowGuidanceSnap
                });
            }
        }

        private bool TryCalculateBounds(
            TargetGeometry geometry,
            out Bounds bounds)
        {
            if (geometry == null || geometry.Root == null)
            {
                bounds = default;
                return false;
            }

            bool hasBounds = false;
            bounds = default;
            for (int index = 0; index < geometry.Renderers.Length; index++)
            {
                Renderer renderer = geometry.Renderers[index];
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            for (int index = 0; index < geometry.HighlightRoots.Length; index++)
            {
                Transform root = geometry.HighlightRoots[index];
                if (root == null || !root.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!hasBounds)
                {
                    bounds = new Bounds(root.position, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(root.position);
                }
            }

            if (!hasBounds)
            {
                bounds = new Bounds(geometry.Root.position, Vector3.one * minimumBoundsExtent * 2f);
            }

            Vector3 extents = bounds.extents;
            extents.x = Mathf.Max(minimumBoundsExtent, extents.x) + boundsPadding;
            extents.y = Mathf.Max(minimumBoundsExtent, extents.y) + boundsPadding;
            extents.z = Mathf.Max(minimumBoundsExtent, extents.z) + boundsPadding;
            bounds.extents = extents;
            return true;
        }

        private void EnsureRayRenderer()
        {
            if (rayRenderers.Length == PointingRayCount &&
                rayRenderers[0] != null && rayRenderers[1] != null)
            {
                return;
            }

            rayRenderers = new LineRenderer[PointingRayCount];
            for (int index = 0; index < PointingRayCount; index++)
            {
                string[] names = index == 0
                    ? new[] { "PlayerPointingRay_Left", "PlayerPointingRay" }
                    : new[] { "PlayerPointingRay_Right" };
                Transform existing = null;
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    existing = transform.Find(names[nameIndex]);
                    if (existing != null)
                    {
                        break;
                    }
                }
                GameObject rayObject = existing != null
                    ? existing.gameObject
                    : new GameObject(names[0]);
                if (existing == null)
                {
                    rayObject.transform.SetParent(transform, false);
                }
                ApplyCaptureExcludedLayer(rayObject);

                LineRenderer renderer = rayObject.GetComponent<LineRenderer>();
                if (renderer == null)
                {
                    renderer = rayObject.AddComponent<LineRenderer>();
                }
                renderer.enabled = false;
                renderer.useWorldSpace = true;
                renderer.loop = false;
                renderer.positionCount = 2;
                renderer.startWidth = rayWidth;
                renderer.endWidth = rayWidth;
                renderer.startColor = rayColor;
                renderer.endColor = rayColor;
                renderer.alignment = LineAlignment.View;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                rayRenderers[index] = renderer;
            }

            Shader shader = Resources.Load<Shader>(OverlayShaderResource) ??
                Shader.Find(OverlayShaderName) ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                rayMaterial = new Material(shader)
                {
                    name = "Player Pointing Ray",
                    hideFlags = HideFlags.DontSave
                };
                for (int index = 0; index < rayRenderers.Length; index++)
                {
                    rayRenderers[index].sharedMaterial = rayMaterial;
                }
            }
        }

        private void ApplyCaptureExcludedLayer()
        {
            for (int index = 0; index < rayRenderers.Length; index++)
            {
                if (rayRenderers[index] != null)
                {
                    ApplyCaptureExcludedLayer(rayRenderers[index].gameObject);
                }
            }
            if (targetHighlight != null)
            {
                ApplyHighlightCaptureExcludedLayer(targetHighlight);
            }
            foreach (InteractionTargetHighlightVisual visual in
                     highlightVisualsByTarget.Values)
            {
                if (visual != null)
                {
                    ApplyHighlightCaptureExcludedLayer(visual);
                }
            }
        }

        private void ApplyHighlightCaptureExcludedLayer(
            InteractionTargetHighlightVisual visual)
        {
            if (visual == null)
            {
                return;
            }
            if (visual.transform != transform)
            {
                ApplyCaptureExcludedLayer(visual.gameObject);
                return;
            }

            // The shared highlight component lives on VRPlayer. Excluding its
            // whole hierarchy also excludes both tracked hand meshes.
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith(
                        "ActualHitEdge_",
                        StringComparison.Ordinal))
                {
                    ApplyCaptureExcludedLayer(child.gameObject);
                }
            }
        }

        private void ApplyCaptureExcludedLayer(GameObject root)
        {
            if (root == null || captureExcludedLayer < 0 ||
                captureExcludedLayer > 31)
            {
                return;
            }
            root.layer = captureExcludedLayer;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < children.Length; index++)
            {
                children[index].gameObject.layer = captureExcludedLayer;
            }
        }

        private void ShowRay(int handIndex, Vector3 start, Vector3 end)
        {
            EnsureRayRenderer();
            if (handIndex < 0 || handIndex >= rayRenderers.Length ||
                rayRenderers[handIndex] == null)
            {
                return;
            }
            LineRenderer renderer = rayRenderers[handIndex];
            renderer.SetPosition(0, start);
            renderer.SetPosition(1, end);
            renderer.enabled = true;
        }

        private void HideRay()
        {
            for (int index = 0; index < rayRenderers.Length; index++)
            {
                if (rayRenderers[index] != null)
                {
                    rayRenderers[index].enabled = false;
                }
            }
        }

        private void HideUnusedRays(int usedCount)
        {
            for (int index = Mathf.Max(0, usedCount);
                index < rayRenderers.Length;
                index++)
            {
                if (rayRenderers[index] != null)
                {
                    rayRenderers[index].enabled = false;
                }
            }
        }

        private static Transform FindAncestor(
            Transform start,
            string firstToken,
            string secondToken)
        {
            Transform current = start;
            for (int index = 0; current != null && index < 8; index++)
            {
                string name = current.name.ToLowerInvariant();
                if (name.Contains(firstToken) || name.Contains(secondToken))
                {
                    return current;
                }
                current = current.parent;
            }
            return start != null ? start.parent : null;
        }

        private static Transform FindNamedDescendant(
            Transform root,
            params string[] expectedNames)
        {
            if (root == null || expectedNames == null || expectedNames.Length == 0)
            {
                return null;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(
                true
            );
            for (int index = 0; index < descendants.Length; index++)
            {
                string candidate = descendants[index].name;
                for (int nameIndex = 0; nameIndex < expectedNames.Length; nameIndex++)
                {
                    if (string.Equals(
                            candidate,
                            expectedNames[nameIndex],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return descendants[index];
                    }
                }
            }
            return null;
        }

        private static Transform[] FindNamedChain(
            Transform root,
            params string[] expectedNames)
        {
            if (root == null || expectedNames == null || expectedNames.Length == 0)
            {
                return Array.Empty<Transform>();
            }

            var chain = new Transform[expectedNames.Length];
            for (int index = 0; index < expectedNames.Length; index++)
            {
                chain[index] = FindNamedDescendant(root, expectedNames[index]);
                if (chain[index] == null)
                {
                    return Array.Empty<Transform>();
                }
            }
            return chain;
        }

        private static bool HasFourTips(Transform[] tips)
        {
            if (tips == null || tips.Length != 4)
            {
                return false;
            }
            for (int index = 0; index < tips.Length; index++)
            {
                if (tips[index] == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static void AssignBoneToChain(
            string id,
            Transform transform,
            Transform[] chain,
            params string[] suffixes)
        {
            if (string.IsNullOrEmpty(id) || transform == null ||
                chain == null || suffixes == null)
            {
                return;
            }
            for (int index = 0; index < suffixes.Length && index < chain.Length; index++)
            {
                if (id.EndsWith(suffixes[index], StringComparison.Ordinal))
                {
                    chain[index] ??= transform;
                    return;
                }
            }
        }

        private static Transform[] PreferCompleteChain(
            Transform[] tracked,
            Transform[] fallback)
        {
            return HasValidChain(tracked) ? tracked : fallback;
        }

        private void OnDisable()
        {
            currentHitTargetId = string.Empty;
            ResetHitCandidate();
            activeHighlightTargetId = string.Empty;
            highlightExpiresAt = double.NaN;
            ClearAllHighlights(false);
            leftHand.ResetSmoothedRay();
            rightHand.ResetSmoothedRay();
            HideRay();
        }

        private void OnDestroy()
        {
            ClearAllHighlights(true);
            if (rayMaterial == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(rayMaterial);
            }
            else
            {
                DestroyImmediate(rayMaterial);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maximumRayDistance = Mathf.Max(0.1f, maximumRayDistance);
            raySmoothingSeconds = Mathf.Max(0f, raySmoothingSeconds);
            strictBoundsFallbackPadding = Mathf.Max(
                0f,
                strictBoundsFallbackPadding
            );
            rayOcclusionEpsilon = Mathf.Max(0f, rayOcclusionEpsilon);
            consecutiveHitFramesRequired = Mathf.Max(
                1,
                consecutiveHitFramesRequired
            );
            highlightDurationSeconds = Mathf.Max(
                0.1f,
                highlightDurationSeconds
            );
            boundsPadding = Mathf.Max(0f, boundsPadding);
            minimumBoundsExtent = Mathf.Max(0.01f, minimumBoundsExtent);
            guidanceSnapAngleDegrees = Mathf.Clamp(
                guidanceSnapAngleDegrees,
                0f,
                30f
            );
            guidanceSnapLateralTolerance = Mathf.Max(
                0f,
                guidanceSnapLateralTolerance
            );
            rayWidth = Mathf.Clamp(rayWidth, 0.002f, 0.025f);
        }
#endif
    }

    internal static class PointingHandShapeRules
    {
        public const float IndexAlignmentThreshold = 0.92f;
        public const float MinimumIndexReachMeters = 0.0025f;
        public const float FoldedTipDistanceRatio = 0.78f;

        public static bool IsIndexExtended(
            Vector3 indexDistal,
            Vector3 indexTip,
            Vector3 palm,
            float indexReach,
            float alignment)
        {
            return indexReach >= MinimumIndexReachMeters &&
                (indexTip - indexDistal).sqrMagnitude >= 0.000001f &&
                alignment >= IndexAlignmentThreshold;
        }

        public static bool AreNonIndexTipsFolded(
            Vector3 palm,
            Vector3 indexTip,
            Transform[] foldedTips)
        {
            if (foldedTips == null || foldedTips.Length != 4)
            {
                return false;
            }
            float indexDistance = Vector3.Distance(palm, indexTip);
            if (indexDistance < MinimumIndexReachMeters)
            {
                return false;
            }
            for (int index = 0; index < foldedTips.Length; index++)
            {
                if (foldedTips[index] == null ||
                    Vector3.Distance(palm, foldedTips[index].position) >
                    indexDistance * FoldedTipDistanceRatio)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
