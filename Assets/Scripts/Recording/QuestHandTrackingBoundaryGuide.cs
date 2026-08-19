using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Recording
{
    /// <summary>
    /// Builds a visible hand-tracking frustum from this Quest's own transitions
    /// between direct optical tracking and Wide Motion Mode inference.
    /// Display depth only controls how far the measured angular planes are drawn;
    /// it is not presented as a measured near/far tracking limit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestHandTrackingBoundaryGuide : MonoBehaviour
    {
        private const string CalibrationKey =
            "SignVR.QuestHandTrackingBoundary.v2";
        private const bool InteractiveCalibrationEnabled = false;
        private const int DirectionCount = 4;
        private const int FrustumEdgeCount = 8;

        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private Transform hmd;

        [SerializeField]
        private OVRHand leftHand;

        [SerializeField]
        private OVRHand rightHand;

        [SerializeField]
        private Material lineMaterial;

        [Header("Measured calibration")]
        [SerializeField]
        [Min(0.2f)]
        private float centerHoldSeconds = 0.55f;

        [SerializeField]
        [Min(0.03f)]
        private float transitionHoldSeconds = 0.08f;

        [SerializeField]
        [Min(0.08f)]
        private float minimumCalibrationTravel = 0.14f;

        [Header("Visual style only")]
        [Tooltip("把实测丢失边界向中心内缩为保守安全区；仍由本机实测数据决定。")]
        [SerializeField]
        [Range(0.5f, 0.95f)]
        private float safeBoundaryRatio = 0.78f;

        [Tooltip("当实测边界超出头显显示视野时，将安全区限制在可见视野内。")]
        [SerializeField]
        [Range(0.6f, 0.95f)]
        private float visibleViewportRatio = 0.84f;

        [Tooltip("只控制实测角度边界从眼前多远处开始显示，不代表近端追踪极限。")]
        [SerializeField]
        [Min(0.15f)]
        private float displayNearDistance = 0.28f;

        [Tooltip("只控制实测角度边界向前延伸多远，不代表远端追踪极限。")]
        [SerializeField]
        [Min(0.5f)]
        private float displayFarDistance = 1.15f;

        [SerializeField]
        [Min(0.002f)]
        private float lineWidth = 0.008f;

        [SerializeField]
        private Color measuredColor = new(0.18f, 0.78f, 0.82f, 0.40f);

        [SerializeField]
        private Color activeColor = new(1f, 0.52f, 0.12f, 0.92f);

        [SerializeField]
        private Color calibrationColor = new(0.18f, 0.82f, 1f, 0.92f);

        private readonly BoundarySample[] samples =
            new BoundarySample[DirectionCount];
        private readonly LineRenderer[] frustumLines =
            new LineRenderer[FrustumEdgeCount];
        private readonly LineRenderer[] calibrationArrow =
            new LineRenderer[3];
        private readonly Vector3[] corners = new Vector3[8];

        private Vector3 calibrationCenter;
        private Vector3 centerAccumulator;
        private int centerSampleCount;
        private float centerStableSeconds;
        private int calibrationTargetIndex;
        private bool centerReady;
        private bool targetWasDirect;
        private bool hasTargetDirect;
        private Vector3 lastTargetDirect;
        private float transitionSeconds;
        private Camera hmdCamera;

        [Header("Soft wall")]
        [Tooltip("视锥侧面的半透明材质；留空则只画边线。")]
        [SerializeField]
        private Material wallMaterial;

        [Tooltip("录制时软墙整体透明度倍数，避免干扰手语。")]
        [SerializeField]
        [Range(0f, 1f)]
        private float recordingWallAlpha = 0.35f;

        [Header("Out-of-view feedback")]
        [Tooltip("手离开相机追踪后，虚拟手染成的颜色。")]
        [SerializeField]
        private Color inferredHandColor = new(0.42f, 0.45f, 0.48f, 0.35f);

        [SerializeField]
        [Min(0.5f)]
        private float exitMarkerSeconds = 2.5f;

        [SerializeField]
        [Min(0.01f)]
        private float exitMarkerRadius = 0.05f;

        private readonly MeshRenderer[] wallRenderers = new MeshRenderer[DirectionCount];
        private readonly Mesh[] wallMeshes = new Mesh[DirectionCount];
        private readonly Vector3[] wallVertices = new Vector3[4];
        private MaterialPropertyBlock wallProperties;

        private HandExitFeedback leftExit;
        private HandExitFeedback rightExit;

        public bool GuidanceEnabled { get; private set; } = true;
        public bool IsCalibrating =>
            InteractiveCalibrationEnabled && !HasCompleteCalibration;

        public string CalibrationInstruction
        {
            get
            {
                if (!InteractiveCalibrationEnabled || HasCompleteCalibration)
                {
                    return string.Empty;
                }

                if (!centerReady)
                {
                    return "追踪范围校准：请将双手放在胸前保持片刻";
                }

                string direction = ((GuideDirection)calibrationTargetIndex) switch
                {
                    GuideDirection.Left => "缓慢把左手向左移出蓝色箭头，再收回",
                    GuideDirection.Right => "缓慢把右手向右移出蓝色箭头，再收回",
                    GuideDirection.Up => "缓慢把右手向上移出蓝色箭头，再收回",
                    _ => "缓慢把右手向下移出蓝色箭头，再收回"
                };
                return $"实测边界 {calibrationTargetIndex + 1}/4：{direction}";
            }
        }

        private bool HasCompleteCalibration
        {
            get
            {
                for (int index = 0; index < samples.Length; index++)
                {
                    if (!samples[index].valid)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private enum GuideDirection
        {
            Left,
            Right,
            Up,
            Down
        }

        /// <summary>
        /// Per-hand state for the out-of-view feedback: the hand itself is greyed
        /// out while Quest is only inferring its pose, and the last place it was
        /// still really seen is marked so the teacher knows where it slipped out.
        /// </summary>
        private sealed class HandExitFeedback
        {
            public OVRHand hand;
            public SkinnedMeshRenderer renderer;
            public MaterialPropertyBlock properties;
            public LineRenderer[] marker;
            public bool wasDirect;
            public Vector3 lastDirectPoint;
            public float markerSecondsLeft;
            public bool dimmed;
        }

        [Serializable]
        private struct BoundarySample
        {
            public bool valid;
            public Vector3 localPoint;
        }

        [Serializable]
        private sealed class CalibrationData
        {
            public int version = 2;
            public Vector3 left;
            public Vector3 right;
            public Vector3 up;
            public Vector3 down;
        }

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            Transform hmdTransform,
            OVRHand left,
            OVRHand right,
            Material material,
            Material wallSurfaceMaterial)
        {
            coordinator = recordingCoordinator;
            hmd = hmdTransform;
            leftHand = left;
            rightHand = right;
            lineMaterial = material;
            wallMaterial = wallSurfaceMaterial;
        }

        public void SetGuidanceEnabled(bool enabled)
        {
            GuidanceEnabled = enabled;
            if (!enabled)
            {
                HideAll();
                RestoreHandTint(leftExit);
                RestoreHandTint(rightExit);
            }
        }

        [ContextMenu("Reset Measured Hand Boundary")]
        public void ResetCalibration()
        {
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = default;
            }

            centerReady = false;
            centerAccumulator = Vector3.zero;
            centerSampleCount = 0;
            centerStableSeconds = 0f;
            calibrationTargetIndex = 0;
            ResetTargetTransition();
            PlayerPrefs.DeleteKey(CalibrationKey);
            HideAll();
        }

        private void Awake()
        {
            if (
                coordinator == null ||
                hmd == null ||
                leftHand == null ||
                rightHand == null ||
                lineMaterial == null
            )
            {
                Debug.LogError(
                    "[QuestHandTrackingBoundaryGuide] Scene references are not assigned."
                );
                enabled = false;
                return;
            }

            for (int index = 0; index < frustumLines.Length; index++)
            {
                frustumLines[index] = CreateLine($"MeasuredFrustum{index}");
            }

            for (int index = 0; index < calibrationArrow.Length; index++)
            {
                calibrationArrow[index] = CreateLine($"CalibrationArrow{index}");
            }

            hmdCamera = hmd.GetComponent<Camera>();
            CreateWalls();
            leftExit = CreateExitFeedback(leftHand, "LeftHandExit");
            rightExit = CreateExitFeedback(rightHand, "RightHandExit");
            LoadCalibration();
        }

        private void Update()
        {
            if (!GuidanceEnabled)
            {
                HideAll();
                return;
            }

            if (IsCalibrating && IsCalibrationState())
            {
                UpdateCalibration();
            }
            else
            {
                HideCalibrationArrow();
            }

            RefreshFrustum(FindActiveDirection());
            RefreshExitFeedback(leftExit);
            RefreshExitFeedback(rightExit);
        }

        private void UpdateCalibration()
        {
            if (!centerReady)
            {
                CaptureCenter();
                HideCalibrationArrow();
                return;
            }

            GuideDirection target = (GuideDirection)calibrationTargetIndex;
            OVRHand targetHand =
                target == GuideDirection.Left ? leftHand : rightHand;
            bool direct = IsDirect(targetHand);

            if (direct)
            {
                lastTargetDirect = hmd.InverseTransformPoint(
                    targetHand.transform.position
                );
                hasTargetDirect = true;
                targetWasDirect = true;
                transitionSeconds = 0f;
            }
            else if (targetWasDirect && hasTargetDirect)
            {
                transitionSeconds += Time.unscaledDeltaTime;
                if (transitionSeconds >= transitionHoldSeconds)
                {
                    if (MatchesTarget(target, lastTargetDirect))
                    {
                        samples[calibrationTargetIndex] = new BoundarySample
                        {
                            valid = true,
                            localPoint = lastTargetDirect
                        };
                        calibrationTargetIndex++;
                        ResetTargetTransition();

                        if (HasCompleteCalibration)
                        {
                            SaveCalibration();
                            HideCalibrationArrow();
                            Debug.Log(
                                "[QuestHandTrackingBoundaryGuide] Quest-measured angular boundary calibration completed."
                            );
                            return;
                        }
                    }
                    else
                    {
                        ResetTargetTransition();
                    }
                }
            }

            RefreshCalibrationArrow(target);
        }

        private void CaptureCenter()
        {
            if (!IsDirect(leftHand) || !IsDirect(rightHand))
            {
                centerAccumulator = Vector3.zero;
                centerSampleCount = 0;
                centerStableSeconds = 0f;
                return;
            }

            Vector3 left = hmd.InverseTransformPoint(leftHand.transform.position);
            Vector3 right = hmd.InverseTransformPoint(rightHand.transform.position);
            centerAccumulator += (left + right) * 0.5f;
            centerSampleCount++;
            centerStableSeconds += Time.unscaledDeltaTime;

            if (centerStableSeconds >= centerHoldSeconds)
            {
                calibrationCenter = centerAccumulator / centerSampleCount;
                centerReady = true;
            }
        }

        private bool MatchesTarget(
            GuideDirection direction,
            Vector3 localPoint)
        {
            Vector3 travel = localPoint - calibrationCenter;
            return direction switch
            {
                GuideDirection.Left => travel.x <= -minimumCalibrationTravel,
                GuideDirection.Right => travel.x >= minimumCalibrationTravel,
                GuideDirection.Up => travel.y >= minimumCalibrationTravel,
                _ => travel.y <= -minimumCalibrationTravel
            };
        }

        private void RefreshCalibrationArrow(GuideDirection direction)
        {
            Vector3 directionVector = direction switch
            {
                GuideDirection.Left => Vector3.left,
                GuideDirection.Right => Vector3.right,
                GuideDirection.Up => Vector3.up,
                _ => Vector3.down
            };
            Vector3 perpendicular =
                direction is GuideDirection.Up or GuideDirection.Down
                    ? Vector3.right
                    : Vector3.up;
            Vector3 start = calibrationCenter;
            Vector3 end = start + directionVector * 0.34f;
            Vector3 headBase = end - directionVector * 0.075f;
            float pulse = 0.78f + Mathf.Sin(Time.unscaledTime * 4f) * 0.18f;
            Color color = calibrationColor;
            color.a *= pulse;

            SetLine(
                calibrationArrow[0],
                hmd.TransformPoint(start),
                hmd.TransformPoint(end),
                color,
                lineWidth * 1.45f
            );
            SetLine(
                calibrationArrow[1],
                hmd.TransformPoint(end),
                hmd.TransformPoint(headBase + perpendicular * 0.045f),
                color,
                lineWidth * 1.45f
            );
            SetLine(
                calibrationArrow[2],
                hmd.TransformPoint(end),
                hmd.TransformPoint(headBase - perpendicular * 0.045f),
                color,
                lineWidth * 1.45f
            );
        }

        private void RefreshFrustum(GuideDirection? activeDirection)
        {
            if (!HasCompleteCalibration || !ShouldShowGuide())
            {
                HideFrustum();
                return;
            }

            BuildFrustumCorners();
            SetFrustumEdge(0, 0, 4, GuideDirection.Left, GuideDirection.Up, activeDirection);
            SetFrustumEdge(1, 1, 5, GuideDirection.Right, GuideDirection.Up, activeDirection);
            SetFrustumEdge(2, 2, 6, GuideDirection.Right, GuideDirection.Down, activeDirection);
            SetFrustumEdge(3, 3, 7, GuideDirection.Left, GuideDirection.Down, activeDirection);
            SetFrustumEdge(4, 4, 5, GuideDirection.Up, null, activeDirection);
            SetFrustumEdge(5, 5, 6, GuideDirection.Right, null, activeDirection);
            SetFrustumEdge(6, 6, 7, GuideDirection.Down, null, activeDirection);
            SetFrustumEdge(7, 7, 4, GuideDirection.Left, null, activeDirection);
            RefreshWalls();
        }

        private void BuildFrustumCorners()
        {
            float leftSlope =
                samples[(int)GuideDirection.Left].localPoint.x /
                samples[(int)GuideDirection.Left].localPoint.z;
            float rightSlope =
                samples[(int)GuideDirection.Right].localPoint.x /
                samples[(int)GuideDirection.Right].localPoint.z;
            float upSlope =
                samples[(int)GuideDirection.Up].localPoint.y /
                samples[(int)GuideDirection.Up].localPoint.z;
            float downSlope =
                samples[(int)GuideDirection.Down].localPoint.y /
                samples[(int)GuideDirection.Down].localPoint.z;

            float centerXSlope = (leftSlope + rightSlope) * 0.5f;
            float centerYSlope = (upSlope + downSlope) * 0.5f;
            leftSlope = Mathf.Lerp(centerXSlope, leftSlope, safeBoundaryRatio);
            rightSlope = Mathf.Lerp(centerXSlope, rightSlope, safeBoundaryRatio);
            upSlope = Mathf.Lerp(centerYSlope, upSlope, safeBoundaryRatio);
            downSlope = Mathf.Lerp(centerYSlope, downSlope, safeBoundaryRatio);

            if (hmdCamera != null)
            {
                float visibleVerticalSlope =
                    Mathf.Tan(hmdCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) *
                    visibleViewportRatio;
                float visibleHorizontalSlope =
                    visibleVerticalSlope * hmdCamera.aspect;
                leftSlope = Mathf.Max(leftSlope, -visibleHorizontalSlope);
                rightSlope = Mathf.Min(rightSlope, visibleHorizontalSlope);
                upSlope = Mathf.Min(upSlope, visibleVerticalSlope);
                downSlope = Mathf.Max(downSlope, -visibleVerticalSlope);
            }

            float near = displayNearDistance;
            float far = Mathf.Max(displayFarDistance, near + 0.25f);
            corners[0] = new Vector3(leftSlope * near, upSlope * near, near);
            corners[1] = new Vector3(rightSlope * near, upSlope * near, near);
            corners[2] = new Vector3(rightSlope * near, downSlope * near, near);
            corners[3] = new Vector3(leftSlope * near, downSlope * near, near);
            corners[4] = new Vector3(leftSlope * far, upSlope * far, far);
            corners[5] = new Vector3(rightSlope * far, upSlope * far, far);
            corners[6] = new Vector3(rightSlope * far, downSlope * far, far);
            corners[7] = new Vector3(leftSlope * far, downSlope * far, far);
        }

        private void SetFrustumEdge(
            int lineIndex,
            int firstCorner,
            int secondCorner,
            GuideDirection firstFace,
            GuideDirection? secondFace,
            GuideDirection? activeDirection)
        {
            bool highlighted =
                activeDirection == firstFace || activeDirection == secondFace;
            Color color = highlighted ? activeColor : measuredColor;
            if (!highlighted)
            {
                color.a = Mathf.Max(color.a, 0.48f);
            }
            if (
                !highlighted &&
                coordinator.State != RecordingFlowState.Ready &&
                coordinator.State != RecordingFlowState.Completed
            )
            {
                color.a *= 0.42f;
            }

            SetLine(
                frustumLines[lineIndex],
                hmd.TransformPoint(corners[firstCorner]),
                hmd.TransformPoint(corners[secondCorner]),
                color,
                highlighted ? lineWidth * 1.7f : lineWidth
            );
        }

        private GuideDirection? FindActiveDirection()
        {
            if (!HasCompleteCalibration)
            {
                return null;
            }

            GuideDirection? result = null;
            float bestScore = 0f;
            EvaluateActiveHand(leftHand, ref result, ref bestScore);
            EvaluateActiveHand(rightHand, ref result, ref bestScore);
            return result;
        }

        private void EvaluateActiveHand(
            OVRHand hand,
            ref GuideDirection? result,
            ref float bestScore)
        {
            if (!hand.IsDataValid || !hand.IsTracked || !hand.PoseSourceInferred)
            {
                return;
            }

            Vector3 local = hmd.InverseTransformPoint(hand.transform.position);
            if (local.z <= 0.05f)
            {
                return;
            }

            float xSlope = local.x / local.z;
            float ySlope = local.y / local.z;
            float leftSlope =
                samples[(int)GuideDirection.Left].localPoint.x /
                samples[(int)GuideDirection.Left].localPoint.z;
            float rightSlope =
                samples[(int)GuideDirection.Right].localPoint.x /
                samples[(int)GuideDirection.Right].localPoint.z;
            float upSlope =
                samples[(int)GuideDirection.Up].localPoint.y /
                samples[(int)GuideDirection.Up].localPoint.z;
            float downSlope =
                samples[(int)GuideDirection.Down].localPoint.y /
                samples[(int)GuideDirection.Down].localPoint.z;
            float horizontalMiddle = (leftSlope + rightSlope) * 0.5f;
            float verticalMiddle = (upSlope + downSlope) * 0.5f;

            GuideDirection horizontalDirection =
                xSlope < horizontalMiddle
                    ? GuideDirection.Left
                    : GuideDirection.Right;
            float horizontalBoundary =
                horizontalDirection == GuideDirection.Left
                    ? leftSlope
                    : rightSlope;
            CompareScore(
                horizontalDirection,
                Mathf.Abs(
                    (xSlope - horizontalMiddle) /
                    (horizontalBoundary - horizontalMiddle)
                ),
                ref result,
                ref bestScore
            );

            GuideDirection verticalDirection =
                ySlope > verticalMiddle
                    ? GuideDirection.Up
                    : GuideDirection.Down;
            float verticalBoundary =
                verticalDirection == GuideDirection.Up
                    ? upSlope
                    : downSlope;
            CompareScore(
                verticalDirection,
                Mathf.Abs(
                    (ySlope - verticalMiddle) /
                    (verticalBoundary - verticalMiddle)
                ),
                ref result,
                ref bestScore
            );
        }

        private static void CompareScore(
            GuideDirection direction,
            float score,
            ref GuideDirection? result,
            ref float bestScore)
        {
            if (score > bestScore)
            {
                bestScore = score;
                result = direction;
            }
        }

        private bool ShouldShowGuide()
        {
            return coordinator.State == RecordingFlowState.Ready ||
                   coordinator.State == RecordingFlowState.Countdown ||
                   coordinator.State == RecordingFlowState.Recording ||
                   coordinator.State == RecordingFlowState.Reviewing ||
                   coordinator.State == RecordingFlowState.Completed;
        }

        private bool IsCalibrationState()
        {
            return coordinator.State == RecordingFlowState.Ready ||
                   coordinator.State == RecordingFlowState.Completed;
        }

        private static bool IsDirect(OVRHand hand)
        {
            return hand.IsDataValid &&
                   hand.IsTracked &&
                   hand.IsDataHighConfidence &&
                   !hand.PoseSourceInferred;
        }

        private void ResetTargetTransition()
        {
            targetWasDirect = false;
            hasTargetDirect = false;
            transitionSeconds = 0f;
        }

        private void SaveCalibration()
        {
            var data = new CalibrationData
            {
                left = samples[(int)GuideDirection.Left].localPoint,
                right = samples[(int)GuideDirection.Right].localPoint,
                up = samples[(int)GuideDirection.Up].localPoint,
                down = samples[(int)GuideDirection.Down].localPoint
            };
            PlayerPrefs.SetString(CalibrationKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        private void LoadCalibration()
        {
            if (!PlayerPrefs.HasKey(CalibrationKey))
            {
                return;
            }

            CalibrationData data = JsonUtility.FromJson<CalibrationData>(
                PlayerPrefs.GetString(CalibrationKey)
            );
            if (
                data == null ||
                data.version != 2 ||
                data.left.z <= 0.05f ||
                data.right.z <= 0.05f ||
                data.up.z <= 0.05f ||
                data.down.z <= 0.05f
            )
            {
                PlayerPrefs.DeleteKey(CalibrationKey);
                return;
            }

            samples[(int)GuideDirection.Left] =
                new BoundarySample { valid = true, localPoint = data.left };
            samples[(int)GuideDirection.Right] =
                new BoundarySample { valid = true, localPoint = data.right };
            samples[(int)GuideDirection.Up] =
                new BoundarySample { valid = true, localPoint = data.up };
            samples[(int)GuideDirection.Down] =
                new BoundarySample { valid = true, localPoint = data.down };
            calibrationTargetIndex = DirectionCount;
        }

        // ---- Soft wall -------------------------------------------------------

        /// <summary>
        /// Builds the four frustum sides as translucent surfaces parented to the
        /// HMD, so their geometry only has to change when the calibration does.
        /// </summary>
        private void CreateWalls()
        {
            if (wallMaterial == null)
            {
                return;
            }

            wallProperties = new MaterialPropertyBlock();

            for (int index = 0; index < DirectionCount; index++)
            {
                var wallObject = new GameObject($"MeasuredWall{(GuideDirection)index}");
                wallObject.transform.SetParent(hmd, false);

                var mesh = new Mesh { name = wallObject.name };
                mesh.MarkDynamic();
                mesh.vertices = new Vector3[4];
                // UVs run corner to corner so the shader can fade in near the rim.
                mesh.uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 0f)
                };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };

                wallObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = wallObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = wallMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.enabled = false;

                wallMeshes[index] = mesh;
                wallRenderers[index] = meshRenderer;
            }
        }

        private void RefreshWalls()
        {
            if (wallProperties == null)
            {
                return;
            }

            SetWall(GuideDirection.Left, 0, 3, 7, 4);
            SetWall(GuideDirection.Right, 1, 5, 6, 2);
            SetWall(GuideDirection.Up, 0, 4, 5, 1);
            SetWall(GuideDirection.Down, 3, 2, 6, 7);

            // Hand positions drive the local touch highlight inside the shader.
            wallProperties.SetVector("_HandLeft", HandShaderPosition(leftHand));
            wallProperties.SetVector("_HandRight", HandShaderPosition(rightHand));
            wallProperties.SetFloat("_GlobalAlpha", CurrentWallAlpha());

            foreach (MeshRenderer wall in wallRenderers)
            {
                if (wall != null)
                {
                    wall.SetPropertyBlock(wallProperties);
                }
            }
        }

        /// <summary>
        /// Outside a take the wall is fully present so the teacher can explore it;
        /// once recording starts it drops back so it never competes with signing.
        /// </summary>
        private float CurrentWallAlpha()
        {
            return coordinator.State == RecordingFlowState.Recording ||
                   coordinator.State == RecordingFlowState.Countdown
                ? recordingWallAlpha
                : 1f;
        }

        private Vector4 HandShaderPosition(OVRHand hand)
        {
            if (!IsDirect(hand))
            {
                return new Vector4(0f, 0f, 0f, 0f);
            }

            Vector3 position = hand.transform.position;
            return new Vector4(position.x, position.y, position.z, 1f);
        }

        private void SetWall(GuideDirection face, int a, int b, int c, int d)
        {
            Mesh mesh = wallMeshes[(int)face];
            MeshRenderer wall = wallRenderers[(int)face];
            if (mesh == null || wall == null)
            {
                return;
            }

            wallVertices[0] = corners[a];
            wallVertices[1] = corners[b];
            wallVertices[2] = corners[c];
            wallVertices[3] = corners[d];
            mesh.vertices = wallVertices;
            mesh.RecalculateBounds();
            wall.enabled = true;
        }

        // ---- Out-of-view feedback -------------------------------------------

        private HandExitFeedback CreateExitFeedback(OVRHand hand, string markerName)
        {
            var ring = new LineRenderer[FrustumEdgeCount];
            for (int index = 0; index < ring.Length; index++)
            {
                ring[index] = CreateLine($"{markerName}{index}");
            }

            return new HandExitFeedback
            {
                hand = hand,
                renderer = hand.GetComponent<SkinnedMeshRenderer>(),
                properties = new MaterialPropertyBlock(),
                marker = ring
            };
        }

        /// <summary>
        /// Greys the hand out while its pose is only inferred, and drops a ring at
        /// the last point where it was still genuinely tracked. The message is not
        /// that a rule was broken, but that the cameras stopped seeing this hand,
        /// and exactly where.
        /// </summary>
        private void RefreshExitFeedback(HandExitFeedback feedback)
        {
            if (feedback == null || feedback.hand == null)
            {
                return;
            }

            bool direct = IsDirect(feedback.hand);
            bool tracked = feedback.hand.IsDataValid && feedback.hand.IsTracked;

            if (direct)
            {
                feedback.lastDirectPoint = feedback.hand.transform.position;
            }
            else if (feedback.wasDirect && tracked)
            {
                // Just slipped out: pin the marker where it was last really seen.
                feedback.markerSecondsLeft = exitMarkerSeconds;
            }

            feedback.wasDirect = direct;
            ApplyHandTint(feedback, GuidanceEnabled && tracked && !direct);

            if (feedback.markerSecondsLeft > 0f)
            {
                feedback.markerSecondsLeft -= Time.unscaledDeltaTime;
                if (direct || !GuidanceEnabled)
                {
                    feedback.markerSecondsLeft = 0f;
                }
            }

            RefreshExitMarker(feedback);
        }

        private void ApplyHandTint(HandExitFeedback feedback, bool dim)
        {
            if (feedback.renderer == null || feedback.dimmed == dim)
            {
                return;
            }

            feedback.dimmed = dim;
            feedback.properties.Clear();
            if (dim)
            {
                // The hand material exposes several colour slots depending on the
                // shader variant, so tint every one that exists.
                feedback.properties.SetColor("_Color", inferredHandColor);
                feedback.properties.SetColor("_ColorTop", inferredHandColor);
                feedback.properties.SetColor("_ColorBottom", inferredHandColor);
            }

            feedback.renderer.SetPropertyBlock(feedback.properties);
        }

        private void RestoreHandTint(HandExitFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            feedback.markerSecondsLeft = 0f;
            ApplyHandTint(feedback, false);
            RefreshExitMarker(feedback);
        }

        private void RefreshExitMarker(HandExitFeedback feedback)
        {
            if (feedback.marker == null)
            {
                return;
            }

            if (feedback.markerSecondsLeft <= 0f)
            {
                foreach (LineRenderer segment in feedback.marker)
                {
                    if (segment != null)
                    {
                        segment.enabled = false;
                    }
                }
                return;
            }

            float fade = Mathf.Clamp01(feedback.markerSecondsLeft / exitMarkerSeconds);
            Color color = activeColor;
            color.a *= fade;

            // A ring facing the teacher, so it reads the same from any angle.
            Vector3 center = feedback.lastDirectPoint;
            Vector3 right = hmd.right * exitMarkerRadius;
            Vector3 up = hmd.up * exitMarkerRadius;
            int segments = feedback.marker.Length;

            for (int index = 0; index < segments; index++)
            {
                float a0 = index / (float)segments * Mathf.PI * 2f;
                float a1 = (index + 1) / (float)segments * Mathf.PI * 2f;
                SetLine(
                    feedback.marker[index],
                    center + right * Mathf.Cos(a0) + up * Mathf.Sin(a0),
                    center + right * Mathf.Cos(a1) + up * Mathf.Sin(a1),
                    color,
                    lineWidth * 1.4f
                );
            }
        }

        private LineRenderer CreateLine(string lineName)
        {
            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = lineMaterial;
            line.enabled = false;
            return line;
        }

        private static void SetLine(
            LineRenderer line,
            Vector3 first,
            Vector3 second,
            Color color,
            float width)
        {
            line.enabled = true;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, first);
            line.SetPosition(1, second);
        }

        private void HideFrustum()
        {
            foreach (LineRenderer line in frustumLines)
            {
                if (line != null)
                {
                    line.enabled = false;
                }
            }

            foreach (MeshRenderer wall in wallRenderers)
            {
                if (wall != null)
                {
                    wall.enabled = false;
                }
            }
        }

        private void HideCalibrationArrow()
        {
            foreach (LineRenderer line in calibrationArrow)
            {
                if (line != null)
                {
                    line.enabled = false;
                }
            }
        }

        private void HideAll()
        {
            HideFrustum();
            HideCalibrationArrow();
        }
    }
}
