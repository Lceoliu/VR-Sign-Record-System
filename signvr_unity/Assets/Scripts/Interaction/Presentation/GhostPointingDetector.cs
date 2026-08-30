using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Interaction.Presentation
{
    public enum GhostPointingDiagnosticStatus
    {
        PhaseNotConfigured = 0,
        ConditionDoesNotIncludePointing = 1,
        IncompleteFingerRig = 2,
        NoEligibleTargets = 3,
        PlaybackNotConfigured = 4,
        PlaybackNotActive = 5,
        NoHit = 6,
        Hit = 7
    }

    /// <summary>
    /// Infers pointing from the replayed signer's two index distal-to-tip rays.
    /// Only the current Task Variant's resolved logical targets are considered.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class GhostPointingDetector : MonoBehaviour
    {
        private const string OverlayShaderName =
            "SignVR/Recording Hand Skeleton Overlay";
        private const string OverlayShaderResource =
            "Shaders/RecordingHandSkeletonOverlay";
        private const int MaximumPhaseTargets = 3;
        private const int PointingRayCount = 2;
        private const int StableCandidateFrames = 30;
        private const double ConfirmationCooldownSeconds = 1d;
        private const double HighlightPersistenceSeconds = 3d;

        private sealed class TargetGeometry
        {
            public string TargetId;
            public Transform Root;
            public Renderer[] Renderers;
            public Collider[] Colliders;
            public Transform[] HighlightRoots;
            public bool AllowGuidanceSnap;
        }

        [Header("Playback source")]
        [SerializeField]
        private InstructionGhostPlayer ghostPlayer;

        [Header("Ghost fingertip rays")]
        [SerializeField]
        private Transform leftIndexDistal;

        [SerializeField]
        private Transform leftIndexTip;

        [SerializeField]
        private Transform rightIndexDistal;

        [SerializeField]
        private Transform rightIndexTip;

        [Header("Strict pointing handshape")]
        [SerializeField]
        private Transform leftPalm;

        [SerializeField]
        private Transform rightPalm;

        [SerializeField]
        private Transform[] leftFoldedFingerTips = Array.Empty<Transform>();

        [SerializeField]
        private Transform[] rightFoldedFingerTips = Array.Empty<Transform>();

        [SerializeField]
        [Min(0.1f)]
        private float maximumRayDistance = 8f;

        [Header("Logical target bindings")]
        [SerializeField]
        private GhostPointingTargetBinding[] targetBindings =
            Array.Empty<GhostPointingTargetBinding>();

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

        [Header("Reused pointing appearance")]
        [SerializeField]
        private InteractionTargetHighlightVisual targetHighlight;

        [SerializeField]
        private Color rayColor = new(0.12f, 0.95f, 1f, 0.88f);

        [SerializeField]
        [Range(0.002f, 0.025f)]
        private float rayWidth = 0.008f;

        private readonly Dictionary<string, TargetGeometry> geometryById = new(
            StringComparer.Ordinal
        );
        private readonly TargetGeometry[] activeTargets =
            new TargetGeometry[MaximumPhaseTargets];
        private readonly GhostPointingState pointingState = new(
            Array.Empty<string>()
        );

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
        private AssistanceCondition condition;
        private int activeTargetCount;
        private bool phaseConfigured;
        private bool playerBound;
        private bool pointingStateBound;
        private string visualTargetId = string.Empty;
        private string[] eligibleTargetIds = Array.Empty<string>();
        private GhostPointingDiagnosticStatus diagnosticStatus =
            GhostPointingDiagnosticStatus.PhaseNotConfigured;
        private string diagnosticTargetId = string.Empty;
        private double lastMonotonicTime = double.NaN;
        private double naturalPlaybackEndDeadline = double.NaN;
        private string pendingTargetId = string.Empty;
        private int pendingTargetFrames;
        private double lastConfirmationTime = double.NegativeInfinity;
        private string primaryHighlightTargetId = string.Empty;

        public event Action<string, double> HitStarted;
        public event Action<string, double> HitEnded;

        public AssistanceCondition Condition => condition;

        public bool PhaseConfigured => phaseConfigured;

        public bool PointingAllowed =>
            phaseConfigured && condition.IncludesPointing();

        public bool IsPointingVisible => pointingState.VisualVisible;

        public string CurrentHitTargetId => pointingState.ActiveTargetId;

        public double PointingExposureSeconds => pointingState.ExposureSeconds;

        public bool HasCompleteFingerRig =>
            leftIndexDistal != null && leftIndexTip != null &&
            rightIndexDistal != null && rightIndexTip != null &&
            leftIndexDistal != leftIndexTip &&
            rightIndexDistal != rightIndexTip;

        public bool HasStrictHandShapeRig =>
            leftPalm != null && rightPalm != null &&
            HasFourTips(leftFoldedFingerTips) &&
            HasFourTips(rightFoldedFingerTips);

        public IReadOnlyList<GhostPointingTargetBinding> TargetBindings =>
            targetBindings;

        public IReadOnlyList<string> EligibleTargetIds => eligibleTargetIds;

        public GhostPointingDiagnosticStatus DiagnosticStatus =>
            diagnosticStatus;

        public string DiagnosticTargetId => diagnosticTargetId;

        public string DiagnosticSummary => diagnosticStatus ==
            GhostPointingDiagnosticStatus.Hit
                ? "hit:" + diagnosticTargetId
                : diagnosticStatus.ToString();

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal bool LifecycleSubscriptionsBoundForTests =>
            playerBound || pointingStateBound;
#endif

        public void ConfigurePlayer(InstructionGhostPlayer player)
        {
            UnbindPlayer();
            ghostPlayer = player;
            BindPlayer();
            RefreshConfigurationDiagnostic();
        }

        public void ConfigureFingerBones(
            Transform leftDistal,
            Transform leftTip,
            Transform rightDistal,
            Transform rightTip)
        {
            leftIndexDistal = leftDistal;
            leftIndexTip = leftTip;
            rightIndexDistal = rightDistal;
            rightIndexTip = rightTip;
            RefreshConfigurationDiagnostic();
        }

        public bool TryConfigureFingerBones(Animator animator)
        {
            if (animator == null)
            {
                return false;
            }

            Transform leftDistal = animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal)
                : null;
            Transform rightDistal = animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.RightIndexDistal)
                : null;
            leftDistal ??= FindNamedDescendant(
                animator.transform,
                "Left_IndexDistal"
            );
            rightDistal ??= FindNamedDescendant(
                animator.transform,
                "Right_IndexDistal"
            );
            bool configured = TryConfigureFingerBones(leftDistal, rightDistal);
            if (configured)
            {
                ConfigureHandShapeBones(animator.transform);
            }
            return configured;
        }

        /// <summary>
        /// Resolves Meta Movement's packaged source skeleton when the prefab
        /// exposes no Animator component. StylizedCharacter keeps Geometry and
        /// Skeleton as siblings below its CharacterRetargeter root.
        /// </summary>
        public bool TryConfigureFingerBones(Transform rigRoot)
        {
            if (rigRoot == null)
            {
                return false;
            }

            Transform leftDistal = FindNamedDescendant(
                rigRoot,
                "Left_IndexDistal"
            );
            Transform rightDistal = FindNamedDescendant(
                rigRoot,
                "Right_IndexDistal"
            );
            bool configured = TryConfigureFingerBones(leftDistal, rightDistal);
            if (configured)
            {
                ConfigureHandShapeBones(rigRoot);
            }
            return configured;
        }

        private bool TryConfigureFingerBones(
            Transform leftDistal,
            Transform rightDistal)
        {
            Transform leftTip = FindTipDescendant(leftDistal);
            Transform rightTip = FindTipDescendant(rightDistal);
            if (leftDistal == null || rightDistal == null ||
                leftTip == null || rightTip == null)
            {
                return false;
            }

            ConfigureFingerBones(
                leftDistal,
                leftTip,
                rightDistal,
                rightTip
            );
            return true;
        }

        private void ConfigureHandShapeBones(Transform rigRoot)
        {
            leftPalm = FindHandRoot(
                rigRoot,
                leftIndexDistal,
                "Left"
            );
            rightPalm = FindHandRoot(
                rigRoot,
                rightIndexDistal,
                "Right"
            );
            leftFoldedFingerTips = FindOtherFingerTips(rigRoot, "Left");
            rightFoldedFingerTips = FindOtherFingerTips(rigRoot, "Right");
        }

        private static Transform[] FindOtherFingerTips(
            Transform rigRoot,
            string handPrefix)
        {
            return new[]
            {
                FindFingerTip(rigRoot, handPrefix, "Thumb"),
                FindFingerTip(rigRoot, handPrefix, "Middle"),
                FindFingerTip(rigRoot, handPrefix, "Ring"),
                FindFingerTip(rigRoot, handPrefix, "Little", "Pinky")
            };
        }

        private static Transform FindFingerTip(
            Transform root,
            string handPrefix,
            params string[] fingerNames)
        {
            for (int index = 0; index < fingerNames.Length; index++)
            {
                string finger = fingerNames[index];
                Transform found = FindNamedDescendant(
                    root,
                    handPrefix + "_" + finger + "Tip"
                );
                found ??= FindNamedDescendant(
                    root,
                    handPrefix + "Hand" + finger + "Tip"
                );
                found ??= FindNamedDescendant(
                    root,
                    handPrefix + finger + "Tip"
                );
                found ??= FindNamedDescendant(
                    root,
                    handPrefix + "_" + finger + "DistalEnd"
                );
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static Transform FindHandRoot(
            Transform rigRoot,
            Transform indexDistal,
            string handPrefix)
        {
            Transform found = FindNamedDescendant(
                rigRoot,
                handPrefix + "_Hand"
            );
            found ??= FindNamedDescendant(rigRoot, handPrefix + "Hand");
            found ??= FindNamedDescendant(rigRoot, handPrefix + "_Palm");
            if (found != null)
            {
                return found;
            }

            Transform current = indexDistal;
            for (int index = 0; current != null && index < 8; index++)
            {
                string name = current.name.ToLowerInvariant();
                if (name.Contains("hand") || name.Contains("wrist"))
                {
                    return current;
                }
                current = current.parent;
            }
            return indexDistal != null ? indexDistal.parent : null;
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

        private static Transform FindNamedDescendant(
            Transform root,
            string expectedName)
        {
            if (root == null || string.IsNullOrWhiteSpace(expectedName))
            {
                return null;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(
                includeInactive: true
            );
            for (int index = 0; index < descendants.Length; index++)
            {
                string candidateName = descendants[index].name;
                int namespaceSeparator = candidateName.LastIndexOf(':');
                if (namespaceSeparator >= 0 &&
                    namespaceSeparator + 1 < candidateName.Length)
                {
                    candidateName = candidateName.Substring(
                        namespaceSeparator + 1
                    );
                }

                if (string.Equals(
                        candidateName,
                        expectedName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return descendants[index];
                }
            }

            return null;
        }

        public void ConfigureTargetBindings(
            GhostPointingTargetBinding[] bindings)
        {
            targetBindings = bindings ??
                Array.Empty<GhostPointingTargetBinding>();
            RebuildGeometryCache();
            RefreshConfigurationDiagnostic();
        }

        public void ConfigureHighlight(
            InteractionTargetHighlightVisual highlight)
        {
            targetHighlight = highlight;
            if (targetHighlight != null)
            {
                targetHighlight.Hide();
            }
        }

        public void ConfigurePhase(
            AssistanceCondition assistanceCondition,
            RunPhasePlan phasePlan)
        {
            if (phasePlan == null)
            {
                throw new ArgumentNullException(nameof(phasePlan));
            }

            ConfigurePhase(assistanceCondition, phasePlan.TaskVariant);
        }

        public void ConfigurePhase(
            AssistanceCondition assistanceCondition,
            TaskVariant taskVariant)
        {
            if (taskVariant == null)
            {
                throw new ArgumentNullException(nameof(taskVariant));
            }

            double now = GetSystemMonotonicTime();
            StopAndClear(now);
            condition = assistanceCondition;
            assistanceCondition.IncludesText();
            assistanceCondition.IncludesPointing();

            IReadOnlyList<string> targetIds = taskVariant.TargetIds;
            activeTargetCount = 0;
            var resolvedTargetIds = new List<string>(targetIds.Count);
            for (int index = 0;
                index < targetIds.Count && index < activeTargets.Length;
                index++)
            {
                if (geometryById.TryGetValue(
                        targetIds[index],
                        out TargetGeometry geometry))
                {
                    activeTargets[activeTargetCount++] = geometry;
                    resolvedTargetIds.Add(geometry.TargetId);
                }
                else if (assistanceCondition.IncludesPointing())
                {
                    Debug.LogError(
                        $"[GhostPointingDetector] No scene target binding for " +
                        $"logical ID '{targetIds[index]}'.",
                        this
                    );
                }
            }

            for (int index = activeTargetCount;
                index < activeTargets.Length;
                index++)
            {
                activeTargets[index] = null;
            }
            eligibleTargetIds = resolvedTargetIds.ToArray();
            pointingState.ResetPhase(eligibleTargetIds, now);
            phaseConfigured = true;
            RefreshConfigurationDiagnostic();
        }

        public void StopPointing()
        {
            try
            {
                StopAndClear(GetSystemMonotonicTime());
            }
            finally
            {
                phaseConfigured = false;
                activeTargetCount = 0;
                eligibleTargetIds = Array.Empty<string>();
                for (int index = 0; index < activeTargets.Length; index++)
                {
                    activeTargets[index] = null;
                }
                SetDiagnostic(
                    GhostPointingDiagnosticStatus.PhaseNotConfigured
                );
            }
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal bool TryPresentPointingHitForTests(
            string targetId,
            Vector3 rayStart,
            Vector3 rayEnd)
        {
            if (!phaseConfigured || !PointingAllowed ||
                !geometryById.TryGetValue(
                    targetId,
                    out TargetGeometry geometry) ||
                !TryCalculateBounds(geometry, out Bounds bounds))
            {
                return false;
            }
            Vector3 direction = rayEnd - rayStart;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return false;
            }
            var ray = new Ray(rayStart, direction.normalized);
            if (!bounds.IntersectRay(ray, out float distance) ||
                distance < 0f || distance > maximumRayDistance)
            {
                return false;
            }

            double now = GetSystemMonotonicTime();
            if (!pointingState.PlaybackActive)
            {
                SynchronizePlayback(true, now);
            }
            if (!pointingState.ObserveHit(targetId, now))
            {
                return false;
            }
            Vector3 actualHitPoint = ray.GetPoint(distance);
            ShowRay(0, rayStart, actualHitPoint);
            SetDiagnostic(GhostPointingDiagnosticStatus.Hit, targetId);
            return true;
        }
#endif

        private void Awake()
        {
            EnsureRayRenderer();
            RebuildGeometryCache();
            if (ghostPlayer != null && ghostPlayer.Retargeter != null)
            {
                if (!HasCompleteFingerRig)
                {
                    TryConfigureFingerBones(ghostPlayer.Retargeter.transform);
                }
                else if (!HasStrictHandShapeRig)
                {
                    ConfigureHandShapeBones(ghostPlayer.Retargeter.transform);
                }
            }
        }

        private void OnEnable()
        {
            BindPointingState();
            BindPlayer();
            RefreshConfigurationDiagnostic();
        }

        private void LateUpdate()
        {
            double now = GetSystemMonotonicTime();
            EvaluatePointing(now);
            if (!double.IsNaN(naturalPlaybackEndDeadline) &&
                now >= naturalPlaybackEndDeadline)
            {
                StopAndClear(now);
                naturalPlaybackEndDeadline = double.NaN;
                RefreshConfigurationDiagnostic();
            }
            ExpireHighlights(now);
        }

        public void SynchronizePlayback(
            bool playbackActive,
            double monotonicTime)
        {
            BindPointingState();
            lastMonotonicTime = monotonicTime;
            if (playbackActive)
            {
                naturalPlaybackEndDeadline = double.NaN;
                if (PointingAllowed)
                {
                    pointingState.BeginPlayback(monotonicTime);
                }
                RefreshConfigurationDiagnostic();
                return;
            }

            StopAndClear(monotonicTime);
            RefreshConfigurationDiagnostic();
        }

        public void EvaluatePointing(double monotonicTime)
        {
            lastMonotonicTime = monotonicTime;
            if (!pointingState.PlaybackActive)
            {
                RefreshConfigurationDiagnostic();
                return;
            }

            if (!PointingAllowed || !HasCompleteFingerRig ||
                activeTargetCount == 0)
            {
                ClearPendingCandidate();
                pointingState.ObserveNoHit(monotonicTime);
                HideRay();
                RefreshConfigurationDiagnostic();
                return;
            }

            // Ghost playback uses the original pose-driven rule: each hand's
            // index distal-to-tip direction is a ray, with no live-player
            // hand-shape gate and no frame dwell. The player's detector keeps
            // its stricter hand-shape and confirmation policy separately.
            if (TryFindLegacyNearestHit(
                    out TargetGeometry target,
                    out Vector3 rayStart,
                    out Vector3 hitPoint))
            {
                pointingState.ObserveHit(
                    target.TargetId,
                    monotonicTime
                );
                if (pointingState.VisualVisible)
                {
                    ShowRay(0, rayStart, hitPoint);
                    HideUnusedRays(1);
                }
                SetDiagnostic(
                    GhostPointingDiagnosticStatus.Hit,
                    target.TargetId
                );
            }
            else
            {
                pointingState.ObserveNoHit(monotonicTime);
                SetDiagnostic(GhostPointingDiagnosticStatus.NoHit);
            }
        }

        private bool TryFindLegacyNearestHit(
            out TargetGeometry nearestTarget,
            out Vector3 rayStart,
            out Vector3 hitPoint)
        {
            nearestTarget = null;
            rayStart = default;
            hitPoint = default;
            float nearestDistance = maximumRayDistance;

            EvaluateLegacyHandRay(
                leftIndexDistal,
                leftIndexTip,
                ref nearestTarget,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            EvaluateLegacyHandRay(
                rightIndexDistal,
                rightIndexTip,
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
            EvaluateLegacyGuidanceRay(
                leftIndexDistal,
                leftIndexTip,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            EvaluateLegacyGuidanceRay(
                rightIndexDistal,
                rightIndexTip,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            return nearestTarget != null;
        }

        private void EvaluateLegacyHandRay(
            Transform distal,
            Transform tip,
            ref TargetGeometry nearestTarget,
            ref float nearestDistance,
            ref Vector3 nearestStart,
            ref Vector3 nearestPoint)
        {
            Vector3 direction = tip.position - distal.position;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }
            direction.Normalize();
            var ray = new Ray(tip.position, direction);

            for (int index = 0; index < activeTargetCount; index++)
            {
                TargetGeometry candidate = activeTargets[index];
                if (!TryCalculateBounds(candidate, out Bounds bounds) ||
                    !bounds.IntersectRay(ray, out float distance) ||
                    distance < 0f || distance > nearestDistance)
                {
                    continue;
                }

                nearestTarget = candidate;
                nearestDistance = distance;
                nearestStart = tip.position;
                nearestPoint = ray.GetPoint(distance);
            }
        }

        private void EvaluateLegacyGuidanceRay(
            Transform distal,
            Transform tip,
            ref TargetGeometry nearestTarget,
            ref float nearestAngle,
            ref float nearestDistance,
            ref Vector3 nearestStart,
            ref Vector3 nearestPoint)
        {
            Vector3 direction = tip.position - distal.position;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }
            direction.Normalize();

            for (int index = 0; index < activeTargetCount; index++)
            {
                TargetGeometry candidate = activeTargets[index];
                if (!candidate.AllowGuidanceSnap ||
                    !TryCalculateBounds(candidate, out Bounds bounds))
                {
                    continue;
                }

                Vector3 toCenter = bounds.center - tip.position;
                float distance = Vector3.Dot(toCenter, direction);
                if (distance <= 0f || distance > maximumRayDistance)
                {
                    continue;
                }

                float angle = Vector3.Angle(direction, toCenter);
                Vector3 closestOnRay = tip.position + direction * distance;
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

                nearestTarget = candidate;
                nearestAngle = angle;
                nearestDistance = distance;
                nearestStart = tip.position;
                nearestPoint = bounds.center;
            }
        }

        private void CollectHandHit(
            Transform distal,
            Transform tip,
            Transform palm,
            Transform[] foldedFingerTips)
        {
            if (!TryFindHandHit(
                    distal,
                    tip,
                    palm,
                    foldedFingerTips,
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

        private bool ObserveGhostTarget(TargetGeometry target, double time)
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
                out int previous
            ) ? previous + 1 : 1;
            candidateFramesByTarget[target.TargetId] = Math.Min(
                StableCandidateFrames,
                frames
            );
            return frames >= StableCandidateFrames;
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

        private void ClearCandidates()
        {
            candidateFramesByTarget.Clear();
        }

        private bool ObserveCandidate(string targetId, double monotonicTime)
        {
            if (!Application.isPlaying ||
                string.Equals(
                    pointingState.ActiveTargetId,
                    targetId,
                    StringComparison.Ordinal))
            {
                return pointingState.ObserveHit(targetId, monotonicTime);
            }

            if (!string.Equals(
                    pendingTargetId,
                    targetId,
                    StringComparison.Ordinal))
            {
                pendingTargetId = targetId;
                pendingTargetFrames = 0;
            }
            pendingTargetFrames = Mathf.Min(
                StableCandidateFrames + 1,
                pendingTargetFrames + 1
            );
            if (pendingTargetFrames <= StableCandidateFrames ||
                monotonicTime - lastConfirmationTime <
                ConfirmationCooldownSeconds)
            {
                pointingState.ObserveNoHit(monotonicTime);
                return false;
            }

            bool started = pointingState.ObserveHit(targetId, monotonicTime);
            if (started)
            {
                lastConfirmationTime = monotonicTime;
                ClearPendingCandidate();
            }
            return started;
        }

        private void ClearPendingCandidate()
        {
            pendingTargetId = string.Empty;
            pendingTargetFrames = 0;
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
                leftIndexDistal,
                leftIndexTip,
                leftPalm,
                leftFoldedFingerTips,
                ref nearestTarget,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            EvaluateHandRay(
                rightIndexDistal,
                rightIndexTip,
                rightPalm,
                rightFoldedFingerTips,
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
                leftIndexDistal,
                leftIndexTip,
                leftPalm,
                leftFoldedFingerTips,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            EvaluateGuidanceRay(
                rightIndexDistal,
                rightIndexTip,
                rightPalm,
                rightFoldedFingerTips,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            return nearestTarget != null;
        }

        private bool TryFindHandHit(
            Transform distal,
            Transform tip,
            Transform palm,
            Transform[] foldedFingerTips,
            out TargetGeometry nearestTarget,
            out Vector3 rayStart,
            out Vector3 hitPoint)
        {
            nearestTarget = null;
            rayStart = default;
            hitPoint = default;
            float nearestDistance = maximumRayDistance;
            EvaluateHandRay(
                distal,
                tip,
                palm,
                foldedFingerTips,
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
                distal,
                tip,
                palm,
                foldedFingerTips,
                ref nearestTarget,
                ref nearestAngle,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            return nearestTarget != null;
        }

        private void EvaluateGuidanceRay(
            Transform distal,
            Transform tip,
            Transform palm,
            Transform[] foldedFingerTips,
            ref TargetGeometry nearestTarget,
            ref float nearestAngle,
            ref float nearestDistance,
            ref Vector3 nearestStart,
            ref Vector3 nearestPoint)
        {
            Vector3 direction = tip.position - distal.position;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }
            direction.Normalize();
            bool strictPointing = IsStrictPointingHand(
                distal,
                tip,
                palm,
                foldedFingerTips
            );

            for (int index = 0; index < activeTargetCount; index++)
            {
                TargetGeometry candidate = activeTargets[index];
                if (!candidate.AllowGuidanceSnap ||
                    (!strictPointing && Application.isPlaying &&
                     !IsLegacyCabinetOrButtonTarget(candidate.TargetId)))
                {
                    continue;
                }
                if (!TryCalculateBounds(candidate, out Bounds bounds))
                {
                    continue;
                }

                Vector3 toCenter = bounds.center - tip.position;
                float distance = Vector3.Dot(toCenter, direction);
                if (distance <= 0f || distance > maximumRayDistance)
                {
                    continue;
                }

                float angle = Vector3.Angle(direction, toCenter);
                Vector3 closestOnRay = tip.position + direction * distance;
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

                nearestTarget = candidate;
                nearestAngle = angle;
                nearestDistance = distance;
                nearestStart = tip.position;
                nearestPoint = bounds.center;
            }
        }

        private void EvaluateHandRay(
            Transform distal,
            Transform tip,
            Transform palm,
            Transform[] foldedFingerTips,
            ref TargetGeometry nearestTarget,
            ref float nearestDistance,
            ref Vector3 nearestStart,
            ref Vector3 nearestPoint)
        {
            Vector3 direction = tip.position - distal.position;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }
            direction.Normalize();
            var ray = new Ray(tip.position, direction);
            bool strictPointing = IsStrictPointingHand(
                distal,
                tip,
                palm,
                foldedFingerTips
            );

            for (int index = 0; index < activeTargetCount; index++)
            {
                TargetGeometry candidate = activeTargets[index];
                bool legacyEditorInput = !Application.isPlaying;
                if (!strictPointing && !legacyEditorInput &&
                    !IsLegacyCabinetOrButtonTarget(candidate.TargetId))
                {
                    continue;
                }
                float distance;
                Vector3 point = default;
                if (strictPointing)
                {
                    if (!TryRaycastTarget(
                            candidate,
                            ray,
                            nearestDistance,
                            out distance,
                            out point))
                    {
                        continue;
                    }
                }
                else if (!TryCalculateBounds(candidate, out Bounds bounds) ||
                    !bounds.IntersectRay(ray, out distance) ||
                    distance < 0f || distance > nearestDistance)
                {
                    continue;
                }

                nearestTarget = candidate;
                nearestDistance = distance;
                nearestStart = tip.position;
                nearestPoint = strictPointing ? point : ray.GetPoint(distance);
            }
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
                        nearestDistance
                    ))
                {
                    continue;
                }

                hitAny = true;
                nearestDistance = hit.distance;
                nearestPoint = hit.point;
            }
            return hitAny;
        }

        private static bool IsStrictPointingHand(
            Transform indexDistal,
            Transform indexTip,
            Transform palm,
            Transform[] foldedFingerTips)
        {
            if (indexDistal == null || indexTip == null || palm == null ||
                !HasFourTips(foldedFingerTips))
            {
                return false;
            }

            Vector3 indexSegment = indexTip.position - indexDistal.position;
            Vector3 indexReach = indexTip.position - palm.position;
            if (indexSegment.sqrMagnitude < 0.000001f ||
                indexReach.sqrMagnitude < 0.0025f)
            {
                return false;
            }

            float segmentAlignment = Vector3.Dot(
                indexSegment.normalized,
                indexReach.normalized
            );
            if (!PointingHandShapeRules.IsIndexExtended(
                    indexDistal.position,
                    indexTip.position,
                    palm.position,
                    indexReach.magnitude,
                    segmentAlignment))
            {
                return false;
            }

            float indexDistance = indexReach.magnitude;
            for (int index = 0; index < foldedFingerTips.Length; index++)
            {
                float foldedDistance = Vector3.Distance(
                    palm.position,
                    foldedFingerTips[index].position
                );
                // The four non-index fingertips must stay close to the palm;
                // an open hand therefore cannot emit a pointing ray.
                if (foldedDistance > indexDistance *
                    PointingHandShapeRules.FoldedTipDistanceRatio)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsLegacyCabinetOrButtonTarget(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return false;
            }

            // The cabinet/key and button sentences retain their authored ray
            // behavior. Other targets, including breakers, require the PDF
            // 3.3 pointing handshape in a player build.
            return targetId.StartsWith("button_", StringComparison.Ordinal) ||
                targetId.StartsWith("key_", StringComparison.Ordinal) ||
                string.Equals(
                    targetId,
                    "motorbike_key",
                    StringComparison.Ordinal
                );
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

            for (int index = 0; index < geometry.Colliders.Length; index++)
            {
                Collider collider = geometry.Colliders[index];
                if (collider == null || !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
            {
                bounds = new Bounds(
                    geometry.Root.position,
                    Vector3.one * minimumBoundsExtent * 2f
                );
            }

            Vector3 extents = bounds.extents;
            extents.x = Mathf.Max(extents.x, minimumBoundsExtent) +
                boundsPadding;
            extents.y = Mathf.Max(extents.y, minimumBoundsExtent) +
                boundsPadding;
            extents.z = Mathf.Max(extents.z, minimumBoundsExtent) +
                boundsPadding;
            bounds.extents = extents;
            return true;
        }

        private void HandlePlaybackStarted(InstructionPlaybackPass _)
        {
            SynchronizePlayback(
                true,
                GetSystemMonotonicTime()
            );
        }

        private void HandlePlaybackEnded(InstructionPlaybackPass _)
        {
            double now = GetSystemMonotonicTime();
            naturalPlaybackEndDeadline =
                now + GhostPointingState.LossGraceSeconds;
            EvaluatePointing(now);
        }

        private void HandlePlayerStopped()
        {
            SynchronizePlayback(
                false,
                GetSystemMonotonicTime()
            );
        }

        private void HandlePlayerFailed(string _)
        {
            SynchronizePlayback(
                false,
                GetSystemMonotonicTime()
            );
        }

        private void HandleHitStarted(string targetId, double time)
        {
            visualTargetId = targetId ?? string.Empty;
            if (geometryById.TryGetValue(
                    targetId,
                    out TargetGeometry geometry))
            {
                if (targetHighlight != null)
                {
                    targetHighlight.Show(
                        geometry.Root,
                        geometry.HighlightRoots
                    );
                }
            }
            HitStarted?.Invoke(targetId, time);
        }

        private void HandleHitEnded(string targetId, double time)
        {
            HideRay();
            if (targetHighlight != null)
            {
                targetHighlight.Hide();
            }
            HideGhostHighlight(targetId);
            HitEnded?.Invoke(targetId, time);
        }

        private void ActivateGhostHighlight(TargetGeometry target, double time)
        {
            if (target == null || string.IsNullOrEmpty(target.TargetId) ||
                highlightExpiryByTarget.ContainsKey(target.TargetId))
            {
                return;
            }
            double expiry = time + HighlightPersistenceSeconds;
            var currentIds = new List<string>(highlightExpiryByTarget.Keys);
            for (int index = 0; index < currentIds.Count; index++)
            {
                highlightExpiryByTarget[currentIds[index]] = expiry;
            }
            highlightExpiryByTarget[target.TargetId] = expiry;
            primaryHighlightTargetId = target.TargetId;
            GetGhostHighlightVisual(target.TargetId)?.Show(
                target.Root,
                target.HighlightRoots
            );
            Debug.Log(
                "[GhostPointingDetector] Highlight started target=" +
                target.TargetId + " frames=" + StableCandidateFrames +
                " duration=" + HighlightPersistenceSeconds.ToString("F2") +
                "s",
                this
            );
        }

        private void ExpireHighlights(double time)
        {
            if (highlightExpiryByTarget.Count == 0)
            {
                return;
            }
            var expired = new List<string>();
            foreach (KeyValuePair<string, double> item in highlightExpiryByTarget)
            {
                if (time >= item.Value)
                {
                    expired.Add(item.Key);
                }
            }
            for (int index = 0; index < expired.Count; index++)
            {
                HideGhostHighlight(expired[index]);
                highlightExpiryByTarget.Remove(expired[index]);
            }
            if (!highlightExpiryByTarget.ContainsKey(primaryHighlightTargetId))
            {
                primaryHighlightTargetId = string.Empty;
                foreach (string targetId in highlightExpiryByTarget.Keys)
                {
                    primaryHighlightTargetId = targetId;
                    break;
                }
            }
        }

        private InteractionTargetHighlightVisual GetGhostHighlightVisual(
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
            InteractionTargetHighlightVisual visual;
            if (highlightVisualsByTarget.Count == 0 && targetHighlight != null)
            {
                visual = targetHighlight;
            }
            else
            {
                GameObject visualObject = new GameObject(
                    "GhostPointingHighlight_" + targetId
                );
                visualObject.transform.SetParent(transform, false);
                visual = visualObject.AddComponent<
                    InteractionTargetHighlightVisual>();
            }
            highlightVisualsByTarget[targetId] = visual;
            return visual;
        }

        private void HideGhostHighlight(string targetId)
        {
            if (!string.IsNullOrEmpty(targetId) &&
                highlightVisualsByTarget.TryGetValue(
                    targetId,
                    out InteractionTargetHighlightVisual visual))
            {
                visual.Hide();
            }
        }

        private void ClearGhostHighlights(bool destroyDynamic)
        {
            foreach (InteractionTargetHighlightVisual visual in
                     highlightVisualsByTarget.Values)
            {
                visual?.Clear();
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
            primaryHighlightTargetId = string.Empty;
        }

        private void StopAndClear(double monotonicTime)
        {
            naturalPlaybackEndDeadline = double.NaN;
            ClearPendingCandidate();
            lastConfirmationTime = double.NegativeInfinity;
            var failures = new List<Exception>();
            TryCleanup(
                () => pointingState.StopPlayback(monotonicTime),
                failures
            );
            TryCleanup(HideRay, failures);
            TryCleanup(() => ClearGhostHighlights(false), failures);
            ClearCandidates();
            visualTargetId = string.Empty;

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Ghost pointing cleanup failed after every visual and " +
                    "state cleanup step was attempted.",
                    failures
                );
            }
        }

        private static void TryCleanup(
            Action cleanup,
            ICollection<Exception> failures)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private void RebuildGeometryCache()
        {
            geometryById.Clear();
            for (int index = 0; index < targetBindings.Length; index++)
            {
                GhostPointingTargetBinding binding = targetBindings[index];
                if (binding == null ||
                    string.IsNullOrWhiteSpace(binding.TargetId) ||
                    binding.TargetRoot == null)
                {
                    continue;
                }

                string id = binding.TargetId.Trim();
                if (geometryById.ContainsKey(id))
                {
                    Debug.LogError(
                        $"[GhostPointingDetector] Duplicate target binding '{id}'.",
                        this
                    );
                    continue;
                }

                Renderer[] candidates =
                    binding.TargetRoot.GetComponentsInChildren<Renderer>(true);
                var renderers = new List<Renderer>(candidates.Length);
                for (int rendererIndex = 0;
                    rendererIndex < candidates.Length;
                    rendererIndex++)
                {
                    if (candidates[rendererIndex] is MeshRenderer ||
                        candidates[rendererIndex] is SkinnedMeshRenderer)
                    {
                        renderers.Add(candidates[rendererIndex]);
                    }
                }

                Transform[] highlightRoots = new Transform[
                    binding.HighlightRoots.Count
                ];
                for (int rootIndex = 0;
                    rootIndex < highlightRoots.Length;
                    rootIndex++)
                {
                    highlightRoots[rootIndex] =
                        binding.HighlightRoots[rootIndex];
                }

                geometryById.Add(id, new TargetGeometry
                {
                    TargetId = id,
                    Root = binding.TargetRoot,
                    Renderers = renderers.ToArray(),
                    Colliders = binding.TargetRoot
                        .GetComponentsInChildren<Collider>(true),
                    HighlightRoots = highlightRoots,
                    AllowGuidanceSnap = binding.AllowGuidanceSnap
                });
            }
        }

        private void RefreshConfigurationDiagnostic()
        {
            if (!phaseConfigured)
            {
                SetDiagnostic(
                    GhostPointingDiagnosticStatus.PhaseNotConfigured
                );
                return;
            }
            if (!condition.IncludesPointing())
            {
                SetDiagnostic(
                    GhostPointingDiagnosticStatus
                        .ConditionDoesNotIncludePointing
                );
                return;
            }
            if (!HasCompleteFingerRig)
            {
                SetDiagnostic(
                    GhostPointingDiagnosticStatus.IncompleteFingerRig
                );
                return;
            }
            if (activeTargetCount == 0)
            {
                SetDiagnostic(
                    GhostPointingDiagnosticStatus.NoEligibleTargets
                );
                return;
            }
            if (!pointingState.PlaybackActive)
            {
                SetDiagnostic(
                    ghostPlayer == null
                        ? GhostPointingDiagnosticStatus
                            .PlaybackNotConfigured
                        : GhostPointingDiagnosticStatus.PlaybackNotActive
                );
                return;
            }
            if (pointingState.VisualVisible)
            {
                SetDiagnostic(
                    GhostPointingDiagnosticStatus.Hit,
                    pointingState.ActiveTargetId
                );
                return;
            }
            SetDiagnostic(GhostPointingDiagnosticStatus.NoHit);
        }

        private void SetDiagnostic(
            GhostPointingDiagnosticStatus status,
            string targetId = null)
        {
            diagnosticStatus = status;
            diagnosticTargetId = status ==
                GhostPointingDiagnosticStatus.Hit
                    ? targetId ?? string.Empty
                    : string.Empty;
        }

        private double GetSystemMonotonicTime()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!double.IsNaN(lastMonotonicTime) && now < lastMonotonicTime)
            {
                return lastMonotonicTime;
            }
            lastMonotonicTime = now;
            return now;
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
                    ? new[] { "GhostPointingRay_Left", "GhostPointingRay" }
                    : new[] { "GhostPointingRay_Right" };
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
                Shader.Find(OverlayShaderName);
            if (shader != null)
            {
                rayMaterial = new Material(shader)
                {
                    name = "Instruction Ghost Pointing Ray",
                    hideFlags = HideFlags.DontSave
                };
                for (int index = 0; index < rayRenderers.Length; index++)
                {
                    rayRenderers[index].sharedMaterial = rayMaterial;
                }
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

        private void BindPlayer()
        {
            if (playerBound || ghostPlayer == null || !isActiveAndEnabled)
            {
                return;
            }

            ghostPlayer.PlaybackStarted += HandlePlaybackStarted;
            ghostPlayer.Completed += HandlePlaybackEnded;
            ghostPlayer.Stopped += HandlePlayerStopped;
            ghostPlayer.Failed += HandlePlayerFailed;
            playerBound = true;
        }

        private void BindPointingState()
        {
            if (pointingStateBound)
            {
                return;
            }
            pointingState.HitStarted += HandleHitStarted;
            pointingState.HitEnded += HandleHitEnded;
            pointingStateBound = true;
        }

        private void UnbindPlayer()
        {
            if (!playerBound)
            {
                return;
            }

            if (ghostPlayer != null)
            {
                ghostPlayer.PlaybackStarted -= HandlePlaybackStarted;
                ghostPlayer.Completed -= HandlePlaybackEnded;
                ghostPlayer.Stopped -= HandlePlayerStopped;
                ghostPlayer.Failed -= HandlePlayerFailed;
            }
            playerBound = false;
        }

        private void UnbindPointingState()
        {
            if (!pointingStateBound)
            {
                return;
            }
            pointingState.HitStarted -= HandleHitStarted;
            pointingState.HitEnded -= HandleHitEnded;
            pointingStateBound = false;
        }

        private static Transform FindTipDescendant(Transform distal)
        {
            if (distal == null)
            {
                return null;
            }

            Transform firstChild = distal.childCount > 0
                ? distal.GetChild(0)
                : null;
            Transform[] descendants = distal.GetComponentsInChildren<Transform>(
                includeInactive: true
            );
            for (int index = 1; index < descendants.Length; index++)
            {
                string name = descendants[index].name.ToLowerInvariant();
                if (name.Contains("tip") || name.Contains("end"))
                {
                    return descendants[index];
                }
            }
            return firstChild;
        }

        private void OnDisable()
        {
            try
            {
                StopPointing();
            }
            finally
            {
                UnbindPlayer();
                UnbindPointingState();
            }
        }

        private void OnDestroy()
        {
            ClearGhostHighlights(true);
            if (rayMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(rayMaterial);
                }
                else
                {
                    DestroyImmediate(rayMaterial);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maximumRayDistance = Mathf.Max(0.1f, maximumRayDistance);
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
}
