using System;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Recording
{
    /// <summary>
    /// Mirrors Meta's OVRSkeletonRenderer: every tracked joint-to-parent bone
    /// gets its own two-point LineRenderer. Interaction SDK already owns the
    /// hand joint transforms, so this reuses them instead of adding a second
    /// OVRSkeleton tracking stack.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class RecordingHandSkeletonVisualizer : MonoBehaviour
    {
        private const string OverlayShaderName =
            "SignVR/Recording Hand Skeleton Overlay";
        private const string OverlayShaderResource =
            "Shaders/RecordingHandSkeletonOverlay";

        private sealed class BoneVisualization
        {
            public Transform Begin;
            public Transform End;
            public LineRenderer Line;
        }

        private sealed class HandVisualization
        {
            public GameObject Root;
            public GameObject SimulatedRoot;
            public HandVisual Source;
            public BoneVisualization[] Bones = Array.Empty<BoneVisualization>();
            public LineRenderer[] SimulatedBones = Array.Empty<LineRenderer>();
        }

#if UNITY_EDITOR
        private static readonly float[] SimulatedFingerOffsets =
            { 0.034f, 0.012f, -0.013f, -0.038f };
        private static readonly float[] SimulatedFingerLengths =
            { 0.135f, 0.15f, 0.14f, 0.115f };
#endif

        [SerializeField]
        private Color skeletonColor = new(0.1f, 0.95f, 1f, 0.96f);

        [SerializeField]
        [Range(0.001f, 0.012f)]
        private float lineWidth = 0.005f;

        [SerializeField]
        private bool hideTrackedHandMesh = true;

#if UNITY_EDITOR
        [SerializeField]
        private bool simulateHandsInEditor = true;
#endif

        private readonly HandVisualization left = new();
        private readonly HandVisualization right = new();
        private Material overlayMaterial;
        private float nextDiscoveryTime;
#if UNITY_EDITOR
        private Camera editorSimulationCamera;
#endif

        public bool LeftVisible => IsVisible(left);
        public bool RightVisible => IsVisible(right);
        public string LeftSourceName => left.Source != null
            ? left.Source.name
            : string.Empty;
        public string RightSourceName => right.Source != null
            ? right.Source.name
            : string.Empty;
        public int LeftBoneCount => left.Bones.Length;
        public int RightBoneCount => right.Bones.Length;
        public bool UsingEditorSimulation { get; private set; }

        public void Configure(IEnumerable<HandVisual> handVisuals)
        {
            EnsureMaterial();
            SelectHandVisuals(handVisuals);
        }

        private void Awake()
        {
            EnsureMaterial();
        }

        private void OnEnable()
        {
            nextDiscoveryTime = 0f;
            EnsureMaterial();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextDiscoveryTime &&
                (!HasSource(left) || !HasSource(right)))
            {
                nextDiscoveryTime = Time.unscaledTime + 1f;
                SelectHandVisuals(UnityEngine.Object.FindObjectsByType<HandVisual>(
                    FindObjectsInactive.Include
                ));
            }

            UsingEditorSimulation = false;
            UpdateHand(left, Handedness.Left);
            UpdateHand(right, Handedness.Right);
        }

        private void SelectHandVisuals(IEnumerable<HandVisual> handVisuals)
        {
            HandVisual[] candidates = handVisuals?
                .Where(candidate => candidate != null && candidate.Hand != null)
                .OrderByDescending(GetVisualPriority)
                .ToArray() ?? Array.Empty<HandVisual>();

            SetSource(
                left,
                candidates.FirstOrDefault(candidate =>
                    candidate.Hand.Handedness == Handedness.Left),
                "LeftHandSkeleton"
            );
            SetSource(
                right,
                candidates.FirstOrDefault(candidate =>
                    candidate.Hand.Handedness == Handedness.Right),
                "RightHandSkeleton"
            );
        }

        private static int GetVisualPriority(HandVisual visual)
        {
            string name = visual.name.ToLowerInvariant();
            string path = GetHierarchyPath(visual.transform).ToLowerInvariant();
            if (path.Contains("reticle") || path.Contains("synthetichand"))
            {
                return -1000;
            }

            int score = 0;
            if (name == "ovrhandvisualleft" || name == "ovrhandvisualright")
            {
                score += 100;
            }
            else if (name.StartsWith("ovrhandvisual", StringComparison.Ordinal))
            {
                score += 70;
            }
            else if (name.Contains("handvisual"))
            {
                score += 30;
            }
            if (path.Contains("ovrcomprehensiveinteractionrig"))
            {
                score += 20;
            }
            return score;
        }

        private void SetSource(
            HandVisualization hand,
            HandVisual source,
            string rootName)
        {
            if (hand.Source == source && hand.Root != null)
            {
                return;
            }

            ReleaseSource(hand);
            hand.Source = source;
            hand.Root = new GameObject(rootName);
            hand.Root.transform.SetParent(transform, false);
            hand.SimulatedRoot = new GameObject("EditorSimulation");
            hand.SimulatedRoot.transform.SetParent(hand.Root.transform, false);
            hand.SimulatedBones = CreateSimulatedBones(hand.SimulatedRoot.transform);
            hand.Bones = source != null
                ? CreateTrackedBones(source, hand.Root.transform)
                : Array.Empty<BoneVisualization>();

            if (source != null && hideTrackedHandMesh)
            {
                source.ForceOffVisibility = true;
            }
        }

        private BoneVisualization[] CreateTrackedBones(
            HandVisual source,
            Transform parent)
        {
            var jointSet = new HashSet<Transform>(
                source.Joints.Where(joint => joint != null)
            );
            var bones = new List<BoneVisualization>();
            foreach (Transform end in source.Joints)
            {
                Transform begin = end != null ? end.parent : null;
                if (begin == null || !jointSet.Contains(begin))
                {
                    continue;
                }

                bones.Add(new BoneVisualization
                {
                    Begin = begin,
                    End = end,
                    Line = CreateBoneLine(parent, end.name)
                });
            }

            return bones.ToArray();
        }

        private LineRenderer[] CreateSimulatedBones(Transform parent)
        {
            const int simulatedBoneCount = 24;
            var lines = new LineRenderer[simulatedBoneCount];
            for (int index = 0; index < lines.Length; index++)
            {
                lines[index] = CreateBoneLine(
                    parent,
                    $"SimulatedBone_{index + 1:00}"
                );
            }
            return lines;
        }

        private LineRenderer CreateBoneLine(Transform parent, string lineName)
        {
            GameObject lineObject = new(lineName);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.enabled = false;
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 2;
            line.widthMultiplier = lineWidth;
            line.numCapVertices = 4;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.sharedMaterial = overlayMaterial;
            line.startColor = Color.white;
            line.endColor = Color.white;
            return line;
        }

        private void UpdateHand(
            HandVisualization hand,
            Handedness handedness)
        {
            HandVisual source = hand.Source;
            IHand trackedHand = source != null ? source.Hand : null;
            bool tracked = source != null && source.isActiveAndEnabled &&
                           trackedHand != null && trackedHand.IsConnected &&
                           trackedHand.IsTrackedDataValid &&
                           hand.Bones.Length > 0;

            if (source != null && hideTrackedHandMesh)
            {
                source.ForceOffVisibility = true;
            }

            SetEnabled(hand.Bones, tracked);
            if (tracked)
            {
                SetEnabled(hand.SimulatedBones, false);
                foreach (BoneVisualization bone in hand.Bones)
                {
                    bone.Line.SetPosition(0, bone.Begin.position);
                    bone.Line.SetPosition(1, bone.End.position);
                }
                return;
            }

            if (TryUpdateEditorSimulation(hand.SimulatedBones, handedness))
            {
                UsingEditorSimulation = true;
            }
            else
            {
                SetEnabled(hand.SimulatedBones, false);
            }
        }

        private bool TryUpdateEditorSimulation(
            IReadOnlyList<LineRenderer> lines,
            Handedness handedness)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || !simulateHandsInEditor)
            {
                return false;
            }

            Camera camera = ResolveEditorSimulationCamera();
            if (camera == null)
            {
                return false;
            }

            List<Vector3[]> bones = BuildSimulatedBones(handedness);
            for (int index = 0; index < lines.Count; index++)
            {
                LineRenderer line = lines[index];
                Vector3[] bone = bones[index];
                line.SetPosition(0, camera.transform.TransformPoint(bone[0]));
                line.SetPosition(1, camera.transform.TransformPoint(bone[1]));
                line.enabled = true;
            }
            return true;
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private Camera ResolveEditorSimulationCamera()
        {
            if (editorSimulationCamera != null &&
                editorSimulationCamera.isActiveAndEnabled)
            {
                return editorSimulationCamera;
            }

            editorSimulationCamera = UnityEngine.Object
                .FindObjectsByType<Camera>(FindObjectsInactive.Exclude)
                .FirstOrDefault(candidate =>
                    candidate.isActiveAndEnabled &&
                    candidate.cameraType == CameraType.Game);
            return editorSimulationCamera;
        }

        private static List<Vector3[]> BuildSimulatedBones(Handedness handedness)
        {
            float sign = handedness == Handedness.Left ? -1f : 1f;
            Vector3 center = new(sign * 0.18f, -0.22f, 0.55f);
            Vector3 wrist = center + new Vector3(0f, -0.055f, 0f);
            var result = new List<Vector3[]>(24);

            Vector3[] thumb =
            {
                wrist,
                center + new Vector3(sign * 0.025f, 0.005f, 0f),
                center + new Vector3(sign * 0.055f, 0.028f, 0.002f),
                center + new Vector3(sign * 0.082f, 0.052f, 0.004f),
                center + new Vector3(sign * 0.105f, 0.07f, 0.006f)
            };
            AppendSegments(result, thumb);

            for (int finger = 0; finger < SimulatedFingerOffsets.Length; finger++)
            {
                var points = new Vector3[6];
                points[0] = wrist;
                for (int joint = 1; joint < points.Length; joint++)
                {
                    float progress = (joint - 1f) / (points.Length - 2f);
                    points[joint] = center + new Vector3(
                        sign * SimulatedFingerOffsets[finger],
                        SimulatedFingerLengths[finger] * progress,
                        0.004f * Mathf.Sin(progress * Mathf.PI)
                    );
                }
                AppendSegments(result, points);
            }
            return result;
        }

        private static void AppendSegments(
            ICollection<Vector3[]> result,
            IReadOnlyList<Vector3> points)
        {
            for (int index = 1; index < points.Count; index++)
            {
                result.Add(new[] { points[index - 1], points[index] });
            }
        }
#endif

        private static bool HasSource(HandVisualization hand)
        {
            return hand.Source != null && hand.Source.Hand != null;
        }

        private static bool IsVisible(HandVisualization hand)
        {
            return hand.Bones.Any(bone => bone.Line.enabled) ||
                   hand.SimulatedBones.Any(line => line.enabled);
        }

        private static void SetEnabled(
            IEnumerable<BoneVisualization> bones,
            bool enabled)
        {
            foreach (BoneVisualization bone in bones)
            {
                bone.Line.enabled = enabled;
            }
        }

        private static void SetEnabled(
            IEnumerable<LineRenderer> lines,
            bool enabled)
        {
            foreach (LineRenderer line in lines)
            {
                line.enabled = enabled;
            }
        }

        private static string GetHierarchyPath(Transform current)
        {
            var names = new List<string>();
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private void EnsureMaterial()
        {
            if (overlayMaterial != null)
            {
                return;
            }

            Shader shader = Resources.Load<Shader>(OverlayShaderResource) ??
                            Shader.Find(OverlayShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"[RecordingHandSkeletonVisualizer] Shader not found: " +
                    OverlayShaderName
                );
                enabled = false;
                return;
            }

            overlayMaterial = new Material(shader)
            {
                name = "Recording Hand Skeleton Material",
                hideFlags = HideFlags.DontSave
            };
            overlayMaterial.SetColor("_BaseColor", skeletonColor);
        }

        private void ReleaseSource(HandVisualization hand)
        {
            RestoreSourceVisibility(hand);
            if (hand.Root != null)
            {
                Destroy(hand.Root);
            }
            hand.Root = null;
            hand.SimulatedRoot = null;
            hand.Source = null;
            hand.Bones = Array.Empty<BoneVisualization>();
            hand.SimulatedBones = Array.Empty<LineRenderer>();
        }

        private void OnDisable()
        {
            RestoreSourceVisibility(left);
            RestoreSourceVisibility(right);
            SetEnabled(left.Bones, false);
            SetEnabled(right.Bones, false);
            SetEnabled(left.SimulatedBones, false);
            SetEnabled(right.SimulatedBones, false);
        }

        private void RestoreSourceVisibility(HandVisualization hand)
        {
            if (hand.Source != null && hideTrackedHandMesh)
            {
                hand.Source.ForceOffVisibility = false;
            }
        }

        private void OnDestroy()
        {
            ReleaseSource(left);
            ReleaseSource(right);
            if (overlayMaterial != null)
            {
                Destroy(overlayMaterial);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lineWidth = Mathf.Clamp(lineWidth, 0.001f, 0.012f);
            if (overlayMaterial != null)
            {
                overlayMaterial.SetColor("_BaseColor", skeletonColor);
            }
            foreach (LineRenderer line in GetComponentsInChildren<LineRenderer>(true))
            {
                line.widthMultiplier = lineWidth;
            }
        }
#endif
    }
}
