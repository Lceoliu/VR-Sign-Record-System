using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Recorder-cue-compatible bounds outline for only the actual inferred hit.
    /// It is Interaction-native so the clean scene has no Recording component.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10010)]
    public sealed class InteractionTargetHighlightVisual : MonoBehaviour
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

        private readonly LineRenderer[] edges = new LineRenderer[EdgeCount];
        private readonly Vector3[] corners = new Vector3[8];
        private Transform targetRoot;
        private Renderer[] targetRenderers = Array.Empty<Renderer>();
        private Material overlayMaterial;

        public bool IsVisible { get; private set; }

        public Transform TargetRoot => targetRoot;

        public void Show(Transform actualHitTarget)
        {
            if (actualHitTarget == null)
            {
                Hide();
                return;
            }

            EnsureVisuals();
            if (targetRoot != actualHitTarget)
            {
                targetRoot = actualHitTarget;
                targetRenderers = targetRoot.GetComponentsInChildren<Renderer>(
                    includeInactive: true
                );
            }
            IsVisible = true;
            SetEdgesEnabled(true);
            RefreshGeometry();
        }

        public void Hide()
        {
            IsVisible = false;
            SetEdgesEnabled(false);
        }

        public void Clear()
        {
            Hide();
            targetRoot = null;
            targetRenderers = Array.Empty<Renderer>();
        }

        private void Awake()
        {
            EnsureVisuals();
            Hide();
        }

        private void LateUpdate()
        {
            if (IsVisible)
            {
                RefreshGeometry();
            }
        }

        private void EnsureVisuals()
        {
            if (edges[0] != null)
            {
                return;
            }

            Shader shader = Resources.Load<Shader>(OverlayShaderResource) ??
                Shader.Find(OverlayShaderName);
            if (shader != null)
            {
                overlayMaterial = new Material(shader)
                {
                    name = "Interaction Target Hit Outline",
                    hideFlags = HideFlags.DontSave
                };
            }

            for (int index = 0; index < edges.Length; index++)
            {
                Transform existing = transform.Find(
                    $"ActualHitEdge_{index + 1:00}"
                );
                GameObject edgeObject = existing != null
                    ? existing.gameObject
                    : new GameObject($"ActualHitEdge_{index + 1:00}");
                if (existing == null)
                {
                    edgeObject.transform.SetParent(transform, false);
                }

                LineRenderer edge = edgeObject.GetComponent<LineRenderer>() ??
                    edgeObject.AddComponent<LineRenderer>();
                edge.enabled = false;
                edge.useWorldSpace = true;
                edge.loop = false;
                edge.positionCount = 2;
                edge.startWidth = lineWidth;
                edge.endWidth = lineWidth;
                edge.startColor = outlineColor;
                edge.endColor = outlineColor;
                edge.sharedMaterial = overlayMaterial;
                edge.alignment = LineAlignment.View;
                edge.shadowCastingMode = ShadowCastingMode.Off;
                edge.receiveShadows = false;
                edge.lightProbeUsage = LightProbeUsage.Off;
                edge.reflectionProbeUsage = ReflectionProbeUsage.Off;
                edges[index] = edge;
            }
        }

        private void RefreshGeometry()
        {
            if (!TryCalculateBounds(out Bounds bounds))
            {
                Hide();
                return;
            }

            SetCorners(bounds, corners);
            for (int index = 0; index < edges.Length; index++)
            {
                edges[index].SetPosition(
                    0,
                    corners[EdgeCorners[index, 0]]
                );
                edges[index].SetPosition(
                    1,
                    corners[EdgeCorners[index, 1]]
                );
            }
        }

        private bool TryCalculateBounds(out Bounds bounds)
        {
            if (targetRoot == null)
            {
                bounds = default;
                return false;
            }

            bool found = false;
            bounds = default;
            for (int index = 0; index < targetRenderers.Length; index++)
            {
                Renderer renderer = targetRenderers[index];
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    (!(renderer is MeshRenderer) &&
                        !(renderer is SkinnedMeshRenderer)))
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!found)
            {
                bounds = new Bounds(
                    targetRoot.position,
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

        private void SetEdgesEnabled(bool enabled)
        {
            for (int index = 0; index < edges.Length; index++)
            {
                if (edges[index] != null)
                {
                    edges[index].enabled = enabled;
                }
            }
        }

        private static void SetCorners(Bounds bounds, Vector3[] destination)
        {
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            destination[0] = new Vector3(minimum.x, minimum.y, minimum.z);
            destination[1] = new Vector3(maximum.x, minimum.y, minimum.z);
            destination[2] = new Vector3(maximum.x, maximum.y, minimum.z);
            destination[3] = new Vector3(minimum.x, maximum.y, minimum.z);
            destination[4] = new Vector3(minimum.x, minimum.y, maximum.z);
            destination[5] = new Vector3(maximum.x, minimum.y, maximum.z);
            destination[6] = new Vector3(maximum.x, maximum.y, maximum.z);
            destination[7] = new Vector3(minimum.x, maximum.y, maximum.z);
        }

        private void OnDisable()
        {
            Hide();
        }

        private void OnDestroy()
        {
            if (overlayMaterial == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(overlayMaterial);
            }
            else
            {
                DestroyImmediate(overlayMaterial);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lineWidth = Mathf.Clamp(lineWidth, 0.002f, 0.025f);
            boundsPadding = Mathf.Max(0f, boundsPadding);
            minimumBoundsExtent = Mathf.Max(0.01f, minimumBoundsExtent);
        }
#endif
    }
}
