using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Recording
{
    /// <summary>
    /// Draws lightweight world-space bounds around the current pointing targets.
    /// The same cached bounds drive fingertip hit testing, so no physics collider
    /// is required on static recording props.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10010)]
    public sealed class RecordingTargetVisualCues : MonoBehaviour
    {
        private const string OverlayShaderName =
            "SignVR/Recording Hand Skeleton Overlay";
        private const string OverlayShaderResource =
            "Shaders/RecordingHandSkeletonOverlay";
        private const int EdgeCount = 12;

        private static readonly int[,] EdgeCorners =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        private sealed class TargetCue
        {
            public Transform Root;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public GameObject VisualRoot;
            public readonly LineRenderer[] Edges =
                new LineRenderer[EdgeCount];
            public readonly Vector3[] Corners = new Vector3[8];
            public TextMeshPro OrdinalLabel;
            public int Ordinal;
        }

        [Header("Dependencies")]
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private Camera labelCamera;

        [Header("Target outline")]
        [SerializeField]
        private Color outlineColor = new(0.12f, 0.95f, 1f, 0.82f);

        [SerializeField]
        [Range(0.002f, 0.025f)]
        private float lineWidth = 0.008f;

        [SerializeField]
        [Min(0f)]
        private float boundsPadding = 0.035f;

        [SerializeField]
        [Min(0.01f)]
        private float minimumBoundsExtent = 0.035f;

        [SerializeField]
        [Range(0f, 0.35f)]
        private float pulseAmount = 0.12f;

        [Header("Sequence labels")]
        [SerializeField]
        private Color ordinalColor = new(1f, 0.86f, 0.18f, 1f);

        [SerializeField]
        [Min(0f)]
        private float ordinalHeightOffset = 0.075f;

        [SerializeField]
        [Range(0.05f, 1f)]
        private float ordinalWorldScale = 0.8f;

        private readonly List<TargetCue> targetCues = new(3);
        private readonly List<Bounds> activeWorldBounds = new(3);
        private Material overlayMaterial;
        private bool coordinatorBound;
        private bool hasManualVisibility;
        private bool manualVisibility;

        public bool IsCueActive { get; private set; }
        public int TargetCount => targetCues.Count;
        public int ActiveBoundsCount => activeWorldBounds.Count;
        public IReadOnlyList<Bounds> ActiveWorldBounds => activeWorldBounds;

        /// <summary>
        /// Binds the recording state and the HMD camera used by number labels.
        /// Calls made after OnEnable safely replace the previous coordinator.
        /// </summary>
        public void Configure(
            RecordingCoordinator recordingCoordinator,
            Camera hmdCamera = null)
        {
            UnbindCoordinator();
            coordinator = recordingCoordinator;
            if (hmdCamera != null)
            {
                labelCamera = hmdCamera;
            }
            BindCoordinator();
            RefreshVisibility();
        }

        /// <summary>
        /// Replaces all current targets. A positive ordinal creates a floating
        /// label; zero omits it. Passing null ordinals creates outline-only cues.
        /// </summary>
        public void SetTargets(
            Transform[] targetRoots,
            int[] ordinals = null)
        {
            ClearTargets();
            EnsureMaterial();

            if (targetRoots == null)
            {
                RefreshVisibility();
                return;
            }

            for (int index = 0; index < targetRoots.Length; index++)
            {
                Transform targetRoot = targetRoots[index];
                if (targetRoot == null)
                {
                    continue;
                }

                int ordinal = ordinals != null && index < ordinals.Length
                    ? Mathf.Max(0, ordinals[index])
                    : 0;
                targetCues.Add(CreateTargetCue(targetRoot, ordinal));
            }

            RefreshVisibility();
        }

        /// <summary>
        /// Updates only sequence ordinals, allowing the three breaker cues to be
        /// reused across all six permutations.
        /// </summary>
        public void SetOrdinalLabels(int[] ordinals)
        {
            for (int index = 0; index < targetCues.Count; index++)
            {
                int ordinal = ordinals != null && index < ordinals.Length
                    ? Mathf.Max(0, ordinals[index])
                    : 0;
                SetOrdinal(targetCues[index], ordinal);
            }

            if (IsCueActive)
            {
                RefreshGeometry();
            }
        }

        public void ClearTargets()
        {
            for (int index = 0; index < targetCues.Count; index++)
            {
                DestroyGeneratedObject(targetCues[index].VisualRoot);
            }

            targetCues.Clear();
            activeWorldBounds.Clear();
        }

        /// <summary>
        /// Overrides coordinator-driven visibility, useful for editor simulation
        /// and focused runtime tests.
        /// </summary>
        public void SetCueActive(bool active)
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

        public bool TryGetActiveBounds(int index, out Bounds bounds)
        {
            if (index >= 0 && index < activeWorldBounds.Count)
            {
                bounds = activeWorldBounds[index];
                return true;
            }

            bounds = default;
            return false;
        }

        /// <summary>
        /// Returns the nearest active target bounds intersected by a world ray.
        /// This deliberately does not use Physics.Raycast.
        /// </summary>
        public bool RaycastActiveBounds(
            Ray ray,
            float maximumDistance,
            out float distance,
            out int targetIndex)
        {
            distance = maximumDistance;
            targetIndex = -1;

            for (int index = 0; index < activeWorldBounds.Count; index++)
            {
                if (!activeWorldBounds[index].IntersectRay(
                        ray,
                        out float candidateDistance) ||
                    candidateDistance < 0f ||
                    candidateDistance > distance)
                {
                    continue;
                }

                distance = candidateDistance;
                targetIndex = index;
            }

            return targetIndex >= 0;
        }

        private void Awake()
        {
            EnsureMaterial();
        }

        private void OnEnable()
        {
            EnsureMaterial();
            BindCoordinator();
            RefreshVisibility();
        }

        private void LateUpdate()
        {
            if (!IsCueActive)
            {
                return;
            }

            RefreshGeometry();
            RefreshAppearance();
        }

        private void OnDisable()
        {
            UnbindCoordinator();
            SetVisualsEnabled(false);
            activeWorldBounds.Clear();
            IsCueActive = false;
        }

        private void OnDestroy()
        {
            UnbindCoordinator();
            ClearTargets();
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
            bool shouldShow = hasManualVisibility
                ? manualVisibility
                : coordinator != null && IsRecordingCueState(coordinator.State);

            IsCueActive = shouldShow && targetCues.Count > 0;
            SetVisualsEnabled(IsCueActive);
            activeWorldBounds.Clear();
            if (IsCueActive)
            {
                RefreshGeometry();
                RefreshAppearance();
            }
        }

        private static bool IsRecordingCueState(RecordingFlowState state)
        {
            return state == RecordingFlowState.Countdown ||
                   state == RecordingFlowState.Recording;
        }

        private TargetCue CreateTargetCue(Transform root, int ordinal)
        {
            var visualRoot = new GameObject($"TargetCue_{root.name}");
            visualRoot.transform.SetParent(transform, false);

            var cue = new TargetCue
            {
                Root = root,
                Renderers = FindGeometryRenderers(root),
                VisualRoot = visualRoot,
                Ordinal = ordinal
            };

            for (int edgeIndex = 0; edgeIndex < EdgeCount; edgeIndex++)
            {
                cue.Edges[edgeIndex] = CreateEdge(
                    visualRoot.transform,
                    $"Edge_{edgeIndex + 1:00}"
                );
            }

            if (ordinal > 0)
            {
                cue.OrdinalLabel = CreateOrdinalLabel(
                    visualRoot.transform,
                    ordinal
                );
            }

            visualRoot.SetActive(IsCueActive);
            return cue;
        }

        private static Renderer[] FindGeometryRenderers(Transform root)
        {
            Renderer[] candidates = root.GetComponentsInChildren<Renderer>(true);
            var result = new List<Renderer>(candidates.Length);
            for (int index = 0; index < candidates.Length; index++)
            {
                Renderer candidate = candidates[index];
                if (candidate is MeshRenderer || candidate is SkinnedMeshRenderer)
                {
                    result.Add(candidate);
                }
            }
            return result.ToArray();
        }

        private LineRenderer CreateEdge(Transform parent, string edgeName)
        {
            var edgeObject = new GameObject(edgeName);
            edgeObject.transform.SetParent(parent, false);
            var edge = edgeObject.AddComponent<LineRenderer>();
            edge.enabled = false;
            edge.useWorldSpace = true;
            edge.loop = false;
            edge.positionCount = 2;
            edge.startWidth = lineWidth;
            edge.endWidth = lineWidth;
            edge.numCapVertices = 4;
            edge.numCornerVertices = 2;
            edge.alignment = LineAlignment.View;
            edge.textureMode = LineTextureMode.Stretch;
            edge.shadowCastingMode = ShadowCastingMode.Off;
            edge.receiveShadows = false;
            edge.lightProbeUsage = LightProbeUsage.Off;
            edge.reflectionProbeUsage = ReflectionProbeUsage.Off;
            edge.sharedMaterial = overlayMaterial;
            edge.startColor = outlineColor;
            edge.endColor = outlineColor;
            return edge;
        }

        private TextMeshPro CreateOrdinalLabel(Transform parent, int ordinal)
        {
            var labelObject = new GameObject(
                "SequenceOrdinal",
                typeof(RectTransform)
            );
            labelObject.transform.SetParent(parent, false);
            var label = labelObject.AddComponent<TextMeshPro>();
            label.text = ordinal.ToString();
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.fontSize = 4f;
            label.color = ordinalColor;
            label.outlineColor = new Color32(10, 18, 22, 255);
            label.outlineWidth = 0.18f;
            label.enableWordWrapping = false;
            label.rectTransform.sizeDelta = new Vector2(1.25f, 0.7f);
            label.transform.localScale = Vector3.one * ordinalWorldScale;
            label.renderer.sortingOrder = short.MaxValue;
            return label;
        }

        private void SetOrdinal(TargetCue cue, int ordinal)
        {
            cue.Ordinal = ordinal;
            if (ordinal <= 0)
            {
                if (cue.OrdinalLabel != null)
                {
                    DestroyGeneratedObject(cue.OrdinalLabel.gameObject);
                    cue.OrdinalLabel = null;
                }
                return;
            }

            if (cue.OrdinalLabel == null)
            {
                cue.OrdinalLabel = CreateOrdinalLabel(
                    cue.VisualRoot.transform,
                    ordinal
                );
            }
            else
            {
                cue.OrdinalLabel.text = ordinal.ToString();
            }

            cue.OrdinalLabel.gameObject.SetActive(IsCueActive);
        }

        private void SetVisualsEnabled(bool enabled)
        {
            for (int index = 0; index < targetCues.Count; index++)
            {
                GameObject visualRoot = targetCues[index].VisualRoot;
                if (visualRoot != null && visualRoot.activeSelf != enabled)
                {
                    visualRoot.SetActive(enabled);
                }
            }
        }

        private void RefreshGeometry()
        {
            activeWorldBounds.Clear();
            for (int index = 0; index < targetCues.Count; index++)
            {
                TargetCue cue = targetCues[index];
                if (!TryCalculateBounds(cue, out Bounds bounds))
                {
                    SetEdgesEnabled(cue, false);
                    if (cue.OrdinalLabel != null)
                    {
                        cue.OrdinalLabel.gameObject.SetActive(false);
                    }
                    continue;
                }

                activeWorldBounds.Add(bounds);
                SetBoundsCorners(bounds, cue.Corners);
                for (int edgeIndex = 0; edgeIndex < EdgeCount; edgeIndex++)
                {
                    LineRenderer edge = cue.Edges[edgeIndex];
                    edge.enabled = true;
                    edge.SetPosition(0, cue.Corners[EdgeCorners[edgeIndex, 0]]);
                    edge.SetPosition(1, cue.Corners[EdgeCorners[edgeIndex, 1]]);
                }

                RefreshOrdinalLabel(cue, bounds);
            }
        }

        private bool TryCalculateBounds(TargetCue cue, out Bounds bounds)
        {
            if (cue.Root == null)
            {
                bounds = default;
                return false;
            }

            bool hasBounds = false;
            bounds = default;
            for (int index = 0; index < cue.Renderers.Length; index++)
            {
                Renderer renderer = cue.Renderers[index];
                if (renderer == null ||
                    !renderer.enabled ||
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
                    cue.Root.position,
                    Vector3.one * minimumBoundsExtent * 2f
                );
            }

            Vector3 extents = bounds.extents;
            extents.x = Mathf.Max(minimumBoundsExtent, extents.x) +
                        boundsPadding;
            extents.y = Mathf.Max(minimumBoundsExtent, extents.y) +
                        boundsPadding;
            extents.z = Mathf.Max(minimumBoundsExtent, extents.z) +
                        boundsPadding;
            bounds.extents = extents;
            return true;
        }

        private static void SetBoundsCorners(Bounds bounds, Vector3[] corners)
        {
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            corners[0] = new Vector3(minimum.x, minimum.y, minimum.z);
            corners[1] = new Vector3(maximum.x, minimum.y, minimum.z);
            corners[2] = new Vector3(maximum.x, maximum.y, minimum.z);
            corners[3] = new Vector3(minimum.x, maximum.y, minimum.z);
            corners[4] = new Vector3(minimum.x, minimum.y, maximum.z);
            corners[5] = new Vector3(maximum.x, minimum.y, maximum.z);
            corners[6] = new Vector3(maximum.x, maximum.y, maximum.z);
            corners[7] = new Vector3(minimum.x, maximum.y, maximum.z);
        }

        private void RefreshOrdinalLabel(TargetCue cue, Bounds bounds)
        {
            if (cue.OrdinalLabel == null || cue.Ordinal <= 0)
            {
                return;
            }

            cue.OrdinalLabel.gameObject.SetActive(true);
            Camera activeLabelCamera = ResolveLabelCamera();
            Vector3 labelPosition = bounds.center +
                Vector3.up * (bounds.extents.y + ordinalHeightOffset);
            if (activeLabelCamera != null)
            {
                Transform cameraTransform = activeLabelCamera.transform;
                Vector3 cameraRight = cameraTransform.right;
                float horizontalExtent =
                    Mathf.Abs(cameraRight.x) * bounds.extents.x +
                    Mathf.Abs(cameraRight.y) * bounds.extents.y +
                    Mathf.Abs(cameraRight.z) * bounds.extents.z;
                Vector3 towardCamera =
                    (cameraTransform.position - bounds.center).normalized;
                labelPosition = bounds.center +
                    cameraRight * (horizontalExtent + ordinalHeightOffset) +
                    cameraTransform.up * 0.06f +
                    towardCamera * 0.04f;
            }

            cue.OrdinalLabel.transform.position = labelPosition;
            cue.OrdinalLabel.transform.localScale =
                Vector3.one * ordinalWorldScale;
            cue.OrdinalLabel.color = ordinalColor;

            if (activeLabelCamera != null)
            {
                Vector3 facingDirection =
                    cue.OrdinalLabel.transform.position -
                    activeLabelCamera.transform.position;
                if (facingDirection.sqrMagnitude > 0.0001f)
                {
                    cue.OrdinalLabel.transform.rotation = Quaternion.LookRotation(
                        facingDirection,
                        activeLabelCamera.transform.up
                    );
                }
            }
        }

        private Camera ResolveLabelCamera()
        {
            if (labelCamera != null && labelCamera.isActiveAndEnabled)
            {
                return labelCamera;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.isActiveAndEnabled)
            {
                labelCamera = mainCamera;
                return labelCamera;
            }

#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Camera[] activeCameras = Camera.allCameras;
                for (int index = 0; index < activeCameras.Length; index++)
                {
                    Camera candidate = activeCameras[index];
                    if (candidate != null && candidate.isActiveAndEnabled)
                    {
                        labelCamera = candidate;
                        return labelCamera;
                    }
                }
            }
#endif

            return null;
        }

        private void RefreshAppearance()
        {
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 2.5f) *
                          pulseAmount;
            float width = lineWidth * pulse;
            Color color = outlineColor;
            color.a *= Mathf.Lerp(0.9f, 1f, pulse * 0.5f);

            for (int targetIndex = 0;
                 targetIndex < targetCues.Count;
                 targetIndex++)
            {
                LineRenderer[] edges = targetCues[targetIndex].Edges;
                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    LineRenderer edge = edges[edgeIndex];
                    edge.startWidth = width;
                    edge.endWidth = width;
                    edge.startColor = color;
                    edge.endColor = color;
                }
            }
        }

        private static void SetEdgesEnabled(TargetCue cue, bool enabled)
        {
            for (int index = 0; index < cue.Edges.Length; index++)
            {
                if (cue.Edges[index] != null)
                {
                    cue.Edges[index].enabled = enabled;
                }
            }
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
                    "[RecordingTargetVisualCues] Overlay shader is missing."
                );
                enabled = false;
                return false;
            }

            overlayMaterial = new Material(shader)
            {
                name = "Recording Target Cue Material",
                hideFlags = HideFlags.DontSave
            };
            overlayMaterial.SetColor("_BaseColor", Color.white);
            return true;
        }

        private static void DestroyGeneratedObject(UnityEngine.Object value)
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
            lineWidth = Mathf.Clamp(lineWidth, 0.002f, 0.025f);
            boundsPadding = Mathf.Max(0f, boundsPadding);
            minimumBoundsExtent = Mathf.Max(0.01f, minimumBoundsExtent);
            ordinalWorldScale = Mathf.Clamp(ordinalWorldScale, 0.015f, 0.16f);
        }
#endif
    }
}
