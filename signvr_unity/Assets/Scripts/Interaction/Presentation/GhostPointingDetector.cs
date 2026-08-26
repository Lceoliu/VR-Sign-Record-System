using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Interaction.Presentation
{
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

        private sealed class TargetGeometry
        {
            public string TargetId;
            public Transform Root;
            public Renderer[] Renderers;
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

        private LineRenderer rayRenderer;
        private Material rayMaterial;
        private AssistanceCondition condition;
        private int activeTargetCount;
        private bool phaseConfigured;
        private bool playerBound;
        private bool pointingStateBound;
        private string visualTargetId = string.Empty;

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

        public IReadOnlyList<GhostPointingTargetBinding> TargetBindings =>
            targetBindings;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal bool LifecycleSubscriptionsBoundForTests =>
            playerBound || pointingStateBound;
#endif

        public void ConfigurePlayer(InstructionGhostPlayer player)
        {
            UnbindPlayer();
            ghostPlayer = player;
            BindPlayer();
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
            return TryConfigureFingerBones(leftDistal, rightDistal);
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
            return TryConfigureFingerBones(leftDistal, rightDistal);
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
        }

        public void ConfigureHighlight(
            InteractionTargetHighlightVisual highlight)
        {
            targetHighlight = highlight;
            targetHighlight?.Hide();
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

            double now = Time.realtimeSinceStartupAsDouble;
            StopAndClear(now);
            condition = assistanceCondition;
            assistanceCondition.IncludesText();
            assistanceCondition.IncludesPointing();

            IReadOnlyList<string> targetIds = taskVariant.TargetIds;
            pointingState.ResetPhase(targetIds, now);
            activeTargetCount = 0;
            for (int index = 0;
                index < targetIds.Count && index < activeTargets.Length;
                index++)
            {
                if (geometryById.TryGetValue(
                        targetIds[index],
                        out TargetGeometry geometry))
                {
                    activeTargets[activeTargetCount++] = geometry;
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
            phaseConfigured = true;
        }

        public void StopPointing()
        {
            try
            {
                StopAndClear(Time.realtimeSinceStartupAsDouble);
            }
            finally
            {
                phaseConfigured = false;
                activeTargetCount = 0;
                for (int index = 0; index < activeTargets.Length; index++)
                {
                    activeTargets[index] = null;
                }
            }
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal bool TryPresentPointingHitForTests(
            string targetId,
            Vector3 rayStart,
            Vector3 hitPoint)
        {
            if (!phaseConfigured || !PointingAllowed ||
                !pointingStateBound ||
                !geometryById.ContainsKey(targetId))
            {
                return false;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (!pointingState.PlaybackActive)
            {
                pointingState.BeginPlayback(now);
            }
            if (!pointingState.ObserveHit(targetId, now))
            {
                return false;
            }
            ShowRay(rayStart, hitPoint);
            return true;
        }
#endif

        private void Awake()
        {
            EnsureRayRenderer();
            RebuildGeometryCache();
            if (!HasCompleteFingerRig && ghostPlayer != null &&
                ghostPlayer.Retargeter != null)
            {
                TryConfigureFingerBones(
                    ghostPlayer.Retargeter.transform
                );
            }
        }

        private void OnEnable()
        {
            BindPointingState();
            BindPlayer();
        }

        private void LateUpdate()
        {
            if (!pointingState.PlaybackActive)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (!PointingAllowed || !HasCompleteFingerRig ||
                activeTargetCount == 0)
            {
                pointingState.ObserveNoHit(now);
                return;
            }

            if (TryFindNearestHit(
                    out TargetGeometry target,
                    out Vector3 rayStart,
                    out Vector3 hitPoint))
            {
                pointingState.ObserveHit(target.TargetId, now);
                if (pointingState.VisualVisible)
                {
                    ShowRay(rayStart, hitPoint);
                }
            }
            else
            {
                pointingState.ObserveNoHit(now);
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
                leftIndexDistal,
                leftIndexTip,
                ref nearestTarget,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            EvaluateHandRay(
                rightIndexDistal,
                rightIndexTip,
                ref nearestTarget,
                ref nearestDistance,
                ref rayStart,
                ref hitPoint
            );
            return nearestTarget != null;
        }

        private void EvaluateHandRay(
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
            if (PointingAllowed)
            {
                pointingState.BeginPlayback(Time.realtimeSinceStartupAsDouble);
            }
        }

        private void HandlePlaybackEnded(InstructionPlaybackPass _)
        {
            StopAndClear(Time.realtimeSinceStartupAsDouble);
        }

        private void HandlePlayerStopped()
        {
            StopAndClear(Time.realtimeSinceStartupAsDouble);
        }

        private void HandlePlayerFailed(string _)
        {
            StopAndClear(Time.realtimeSinceStartupAsDouble);
        }

        private void HandleHitStarted(string targetId, double time)
        {
            if (geometryById.TryGetValue(
                    targetId,
                    out TargetGeometry geometry))
            {
                if (!string.Equals(
                        visualTargetId,
                        targetId,
                        StringComparison.Ordinal))
                {
                    visualTargetId = targetId;
                }
                targetHighlight?.Show(geometry.Root);
            }

            HitStarted?.Invoke(targetId, time);
        }

        private void HandleHitEnded(string targetId, double time)
        {
            HideRay();
            targetHighlight?.Hide();
            HitEnded?.Invoke(targetId, time);
        }

        private void StopAndClear(double monotonicTime)
        {
            var failures = new List<Exception>();
            TryCleanup(
                () => pointingState.StopPlayback(monotonicTime),
                failures
            );
            TryCleanup(HideRay, failures);
            TryCleanup(() => targetHighlight?.Clear(), failures);
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

                geometryById.Add(id, new TargetGeometry
                {
                    TargetId = id,
                    Root = binding.TargetRoot,
                    Renderers = renderers.ToArray()
                });
            }
        }

        private void EnsureRayRenderer()
        {
            if (rayRenderer != null)
            {
                return;
            }

            Transform existing = transform.Find("GhostPointingRay");
            GameObject rayObject = existing != null
                ? existing.gameObject
                : new GameObject("GhostPointingRay");
            if (existing == null)
            {
                rayObject.transform.SetParent(transform, false);
            }

            rayRenderer = rayObject.GetComponent<LineRenderer>() ??
                rayObject.AddComponent<LineRenderer>();
            Shader shader = Resources.Load<Shader>(OverlayShaderResource) ??
                Shader.Find(OverlayShaderName);
            if (shader != null)
            {
                rayMaterial = new Material(shader)
                {
                    name = "Instruction Ghost Pointing Ray",
                    hideFlags = HideFlags.DontSave
                };
                rayRenderer.sharedMaterial = rayMaterial;
            }

            rayRenderer.enabled = false;
            rayRenderer.useWorldSpace = true;
            rayRenderer.loop = false;
            rayRenderer.positionCount = 2;
            rayRenderer.startWidth = rayWidth;
            rayRenderer.endWidth = rayWidth;
            rayRenderer.startColor = rayColor;
            rayRenderer.endColor = rayColor;
            rayRenderer.alignment = LineAlignment.View;
            rayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            rayRenderer.receiveShadows = false;
            rayRenderer.lightProbeUsage = LightProbeUsage.Off;
            rayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void ShowRay(Vector3 start, Vector3 end)
        {
            EnsureRayRenderer();
            rayRenderer.SetPosition(0, start);
            rayRenderer.SetPosition(1, end);
            rayRenderer.enabled = true;
        }

        private void HideRay()
        {
            if (rayRenderer != null)
            {
                rayRenderer.enabled = false;
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
            rayWidth = Mathf.Clamp(rayWidth, 0.002f, 0.025f);
        }
#endif
    }
}
