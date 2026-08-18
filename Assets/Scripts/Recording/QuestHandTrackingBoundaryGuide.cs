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

        public bool GuidanceEnabled { get; private set; } = true;
        public bool IsCalibrating => !HasCompleteCalibration;

        public string CalibrationInstruction
        {
            get
            {
                if (HasCompleteCalibration)
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
            Material material)
        {
            coordinator = recordingCoordinator;
            hmd = hmdTransform;
            leftHand = left;
            rightHand = right;
            lineMaterial = material;
        }

        public void SetGuidanceEnabled(bool enabled)
        {
            GuidanceEnabled = enabled;
            if (!enabled)
            {
                HideAll();
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
