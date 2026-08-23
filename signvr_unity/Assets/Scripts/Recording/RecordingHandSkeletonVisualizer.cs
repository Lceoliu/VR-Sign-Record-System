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
    /// Draws a non-physical hand-skeleton overlay from the same tracked hand
    /// sources that drive the scene's hand meshes.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class RecordingHandSkeletonVisualizer : MonoBehaviour
    {
        private const string OverlayShaderName =
            "SignVR/Recording Hand Skeleton Overlay";
        private const string OverlayShaderResource =
            "Shaders/RecordingHandSkeletonOverlay";

        private static readonly HandJointId[][] JointChains =
        {
            new[]
            {
                HandJointId.HandWristRoot,
                HandJointId.HandThumb1,
                HandJointId.HandThumb2,
                HandJointId.HandThumb3,
                HandJointId.HandThumbTip
            },
            new[]
            {
                HandJointId.HandWristRoot,
                HandJointId.HandIndex0,
                HandJointId.HandIndex1,
                HandJointId.HandIndex2,
                HandJointId.HandIndex3,
                HandJointId.HandIndexTip
            },
            new[]
            {
                HandJointId.HandWristRoot,
                HandJointId.HandMiddle0,
                HandJointId.HandMiddle1,
                HandJointId.HandMiddle2,
                HandJointId.HandMiddle3,
                HandJointId.HandMiddleTip
            },
            new[]
            {
                HandJointId.HandWristRoot,
                HandJointId.HandRing0,
                HandJointId.HandRing1,
                HandJointId.HandRing2,
                HandJointId.HandRing3,
                HandJointId.HandRingTip
            },
            new[]
            {
                HandJointId.HandWristRoot,
                HandJointId.HandPinky0,
                HandJointId.HandPinky1,
                HandJointId.HandPinky2,
                HandJointId.HandPinky3,
                HandJointId.HandPinkyTip
            },
            new[]
            {
                HandJointId.HandThumb1,
                HandJointId.HandIndex0,
                HandJointId.HandMiddle0,
                HandJointId.HandRing0,
                HandJointId.HandPinky0,
                HandJointId.HandWristRoot
            }
        };

#if UNITY_EDITOR
        private static readonly float[] SimulatedFingerRootOffsets =
            { 0.032f, 0.01f, -0.015f, -0.038f };
        private static readonly float[] SimulatedFingerLengths =
            { 0.135f, 0.15f, 0.14f, 0.115f };
#endif

        [SerializeField]
        private Color skeletonColor = new(0.1f, 0.95f, 1f, 0.96f);

        [SerializeField]
        [Range(0.001f, 0.012f)]
        private float lineWidth = 0.0045f;

#if UNITY_EDITOR
        [SerializeField]
        private bool simulateHandsInEditor = true;
#endif

        private HandVisual leftVisual;
        private HandVisual rightVisual;
        private LineRenderer[] leftLines;
        private LineRenderer[] rightLines;
        private Material overlayMaterial;
        private float nextDiscoveryTime;
#if UNITY_EDITOR
        private Camera editorSimulationCamera;
#endif

        public bool LeftVisible => AreLinesVisible(leftLines);
        public bool RightVisible => AreLinesVisible(rightLines);
        public string LeftSourceName => leftVisual != null
            ? leftVisual.name
            : string.Empty;
        public string RightSourceName => rightVisual != null
            ? rightVisual.name
            : string.Empty;
        public bool UsingEditorSimulation { get; private set; }

        public void Configure(IEnumerable<HandVisual> handVisuals)
        {
            SelectHandVisuals(handVisuals);
            EnsureVisuals();
        }

        private void Awake()
        {
            EnsureVisuals();
        }

        private void OnEnable()
        {
            nextDiscoveryTime = 0f;
            EnsureVisuals();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextDiscoveryTime &&
                (!IsUsable(leftVisual) || !IsUsable(rightVisual)))
            {
                nextDiscoveryTime = Time.unscaledTime + 1f;
                SelectHandVisuals(UnityEngine.Object.FindObjectsByType<HandVisual>(
                    FindObjectsInactive.Exclude
                ));
            }

            UsingEditorSimulation = false;
            UpdateHand(leftVisual, leftLines, Handedness.Left);
            UpdateHand(rightVisual, rightLines, Handedness.Right);
        }

        private void SelectHandVisuals(IEnumerable<HandVisual> handVisuals)
        {
            HandVisual[] candidates = handVisuals?
                .Where(IsUsable)
                .OrderByDescending(GetVisualPriority)
                .ToArray() ?? Array.Empty<HandVisual>();

            leftVisual = candidates.FirstOrDefault(candidate =>
                IsHanded(candidate, Handedness.Left));
            rightVisual = candidates.FirstOrDefault(candidate =>
                IsHanded(candidate, Handedness.Right));
        }

        private static bool IsUsable(HandVisual visual)
        {
            return visual != null && visual.isActiveAndEnabled &&
                   visual.Hand != null;
        }

        private static bool IsHanded(HandVisual visual, Handedness handedness)
        {
            return visual.Hand != null && visual.Hand.Handedness == handedness;
        }

        private static int GetVisualPriority(HandVisual visual)
        {
            string visualName = visual.name.ToLowerInvariant();
            if (visualName.Contains("ovrhandvisual"))
            {
                return 2;
            }
            return visualName.Contains("handvisual") ? 1 : 0;
        }

        private void EnsureVisuals()
        {
            if (overlayMaterial == null)
            {
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

            leftLines ??= CreateHandLines("LeftHandSkeleton");
            rightLines ??= CreateHandLines("RightHandSkeleton");
        }

        private LineRenderer[] CreateHandLines(string rootName)
        {
            GameObject handRoot = new(rootName);
            handRoot.transform.SetParent(transform, false);
            LineRenderer[] lines = new LineRenderer[JointChains.Length];
            for (int chainIndex = 0; chainIndex < JointChains.Length; chainIndex++)
            {
                GameObject lineObject = new($"BoneChain_{chainIndex + 1:00}");
                lineObject.transform.SetParent(handRoot.transform, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.enabled = false;
                line.useWorldSpace = true;
                line.loop = false;
                line.positionCount = JointChains[chainIndex].Length;
                line.widthMultiplier = lineWidth;
                line.numCapVertices = 4;
                line.numCornerVertices = 2;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                line.sharedMaterial = overlayMaterial;
                line.startColor = Color.white;
                line.endColor = Color.white;
                lines[chainIndex] = line;
            }
            return lines;
        }

        private void UpdateHand(
            HandVisual visual,
            IReadOnlyList<LineRenderer> lines,
            Handedness handedness)
        {
            IHand hand = visual != null ? visual.Hand : null;
            bool tracked = hand != null && hand.IsConnected &&
                           hand.IsTrackedDataValid;
            if (!tracked && TryUpdateEditorSimulation(lines, handedness))
            {
                UsingEditorSimulation = true;
                return;
            }

            for (int chainIndex = 0; chainIndex < lines.Count; chainIndex++)
            {
                LineRenderer line = lines[chainIndex];
                if (!tracked)
                {
                    line.enabled = false;
                    continue;
                }

                HandJointId[] chain = JointChains[chainIndex];
                bool complete = true;
                for (int jointIndex = 0; jointIndex < chain.Length; jointIndex++)
                {
                    if (!hand.GetJointPose(chain[jointIndex], out Pose pose))
                    {
                        complete = false;
                        break;
                    }
                    line.SetPosition(jointIndex, pose.position);
                }
                line.enabled = complete;
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

            float sign = handedness == Handedness.Left ? -1f : 1f;
            for (int chainIndex = 0; chainIndex < lines.Count; chainIndex++)
            {
                LineRenderer line = lines[chainIndex];
                HandJointId[] chain = JointChains[chainIndex];
                for (int jointIndex = 0; jointIndex < chain.Length; jointIndex++)
                {
                    Vector3 local = GetSimulatedJointPosition(
                        chainIndex,
                        jointIndex,
                        sign
                    );
                    line.SetPosition(jointIndex, camera.transform.TransformPoint(local));
                }
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
                .FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude
                )
                .FirstOrDefault(candidate =>
                    candidate.isActiveAndEnabled &&
                    candidate.cameraType == CameraType.Game);
            return editorSimulationCamera;
        }

        private static Vector3 GetSimulatedJointPosition(
            int chainIndex,
            int jointIndex,
            float sign)
        {
            Vector3 center = new(sign * 0.18f, -0.22f, 0.55f);
            Vector3 wrist = center + new Vector3(0f, -0.055f, 0f);
            if (chainIndex == 0)
            {
                if (jointIndex == 0)
                {
                    return wrist;
                }

                float step = jointIndex / 4f;
                return center + new Vector3(
                    sign * (0.025f + 0.095f * step),
                    0.01f + 0.06f * step,
                    0f
                );
            }

            if (chainIndex < 5)
            {
                if (jointIndex == 0)
                {
                    return wrist;
                }

                int fingerIndex = chainIndex - 1;
                float step = jointIndex / 5f;
                return center + new Vector3(
                    sign * SimulatedFingerRootOffsets[fingerIndex],
                    SimulatedFingerLengths[fingerIndex] * step,
                    0.004f * Mathf.Sin(step * Mathf.PI)
                );
            }

            switch (jointIndex)
            {
                case 0:
                    return center + new Vector3(sign * 0.025f, 0.01f, 0f);
                case 1:
                    return center + new Vector3(sign * 0.032f, 0f, 0f);
                case 2:
                    return center + new Vector3(sign * 0.01f, 0f, 0f);
                case 3:
                    return center + new Vector3(sign * -0.015f, 0f, 0f);
                case 4:
                    return center + new Vector3(sign * -0.038f, 0f, 0f);
                default:
                    return wrist;
            }
        }
#endif

        private static bool AreLinesVisible(IReadOnlyList<LineRenderer> lines)
        {
            return lines != null && lines.Count > 0 && lines[0].enabled;
        }

        private void OnDestroy()
        {
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
            SetLineWidth(leftLines);
            SetLineWidth(rightLines);
        }

        private void SetLineWidth(IEnumerable<LineRenderer> lines)
        {
            if (lines == null)
            {
                return;
            }
            foreach (LineRenderer line in lines)
            {
                line.widthMultiplier = lineWidth;
            }
        }
#endif
    }
}
