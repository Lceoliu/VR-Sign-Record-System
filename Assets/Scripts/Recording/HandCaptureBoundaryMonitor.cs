using TMPro;
using UnityEngine;

namespace SignVR.Recording
{
    public enum HandCaptureBoundaryStatus
    {
        Safe,
        NearBoundary,
        OutsideBoundary,
        LowConfidence,
        TrackingLost
    }

    [DisallowMultipleComponent]
    public sealed class HandCaptureBoundaryMonitor : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private Transform hmd;

        [SerializeField]
        private OVRHand leftHand;

        [SerializeField]
        private OVRHand rightHand;

        [SerializeField]
        private GameObject warningRoot;

        [SerializeField]
        private TMP_Text warningLabel;

        [Header("Directional edge glow")]
        [Tooltip("视野边缘的方向性光晕；手往哪边超界，对应方向就渐强。")]
        [SerializeField]
        private RecordingBoundaryGlow boundaryGlow;

        [Tooltip("录制前显示的安全区包络，帮助老师建立空间直觉。")]
        [SerializeField]
        private GameObject safeZonePreview;

        [Header("Conservative HMD-relative working volume")]
        [SerializeField]
        [Min(0.05f)]
        private float nearDistance = 0.18f;

        [SerializeField]
        [Min(0.2f)]
        private float farDistance = 1.2f;

        [SerializeField]
        [Range(20f, 85f)]
        private float horizontalHalfAngle = 58f;

        [SerializeField]
        [Range(20f, 85f)]
        private float upperHalfAngle = 50f;

        [SerializeField]
        [Range(20f, 85f)]
        private float lowerHalfAngle = 58f;

        [Header("Warning behavior")]
        [SerializeField]
        [Range(1f, 25f)]
        private float angularWarningMargin = 10f;

        [SerializeField]
        [Range(0.02f, 0.4f)]
        private float distanceWarningMargin = 0.15f;

        [Tooltip("归一化余量阈值：手的剩余余量低于该值时开始渐强提示。0.18 约等于旧的 10° 角度余量。")]
        [SerializeField]
        [Range(0.05f, 0.6f)]
        private float warningMarginFraction = 0.18f;

        [SerializeField]
        [Range(0f, 0.5f)]
        private float warningDelaySeconds = 0.12f;

        [SerializeField]
        [Range(0f, 1f)]
        private float clearDelaySeconds = 0.35f;

        private float warningTimer;
        private float clearTimer;
        private string pendingMessage = string.Empty;
        private Color pendingColor;
        private HandCaptureSample leftSample;
        private HandCaptureSample rightSample;

        public HandCaptureBoundaryStatus LeftStatus { get; private set; }
        public HandCaptureBoundaryStatus RightStatus { get; private set; }

        /// <summary>
        /// Whether the teacher sees any boundary guidance at all. The operator can
        /// turn this off from the console so the same teacher can record one pass
        /// with guidance and one without, which is what the A/B comparison needs.
        /// Quality sampling keeps running either way, otherwise the two passes
        /// would not be comparable.
        /// </summary>
        public bool GuidanceEnabled { get; private set; } = true;

        public HandCaptureFrame CurrentFrame =>
            new() { left = leftSample, right = rightSample };

        public void SetGuidanceEnabled(bool enabled)
        {
            GuidanceEnabled = enabled;
            if (!enabled)
            {
                HideImmediately();
            }
        }

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            Transform hmdTransform,
            OVRHand left,
            OVRHand right,
            GameObject warningPanel,
            TMP_Text warningText)
        {
            coordinator = recordingCoordinator;
            hmd = hmdTransform;
            leftHand = left;
            rightHand = right;
            warningRoot = warningPanel;
            warningLabel = warningText;
        }

        private void Awake()
        {
            if (
                coordinator == null ||
                hmd == null ||
                leftHand == null ||
                rightHand == null ||
                warningRoot == null ||
                warningLabel == null
            )
            {
                Debug.LogError(
                    "[HandCaptureBoundaryMonitor] Scene references are not assigned."
                );
                enabled = false;
                return;
            }

            warningRoot.SetActive(false);
        }

        private void Update()
        {
            // Sampling runs in every state and regardless of the guidance switch,
            // so the guidance-on and guidance-off passes stay comparable.
            LeftStatus = Evaluate(leftHand, out leftSample, out leftAxis);
            RightStatus = Evaluate(rightHand, out rightSample, out rightAxis);

            if (!ShouldMonitorCurrentState() || !GuidanceEnabled)
            {
                HideImmediately();
                return;
            }

            RefreshSafeZonePreview();
            RefreshGlow();

            bool shouldWarn = TryBuildWarning(
                LeftStatus,
                RightStatus,
                out pendingMessage,
                out pendingColor
            );

            if (shouldWarn)
            {
                clearTimer = 0f;
                warningTimer += Time.unscaledDeltaTime;
                if (warningTimer >= warningDelaySeconds)
                {
                    warningLabel.text = pendingMessage;
                    warningLabel.color = pendingColor;
                    warningRoot.SetActive(true);
                }
                return;
            }

            warningTimer = 0f;
            clearTimer += Time.unscaledDeltaTime;
            if (clearTimer >= clearDelaySeconds)
            {
                warningRoot.SetActive(false);
            }
        }

        private bool ShouldMonitorCurrentState()
        {
            return coordinator.State == RecordingFlowState.Ready ||
                   coordinator.State == RecordingFlowState.Countdown ||
                   coordinator.State == RecordingFlowState.Recording;
        }

        private HandCaptureBoundaryStatus Evaluate(
            OVRHand hand,
            out HandCaptureSample sample,
            out BoundaryAxis axis)
        {
            sample = default;
            sample.boundary_margin = 1f;
            axis = BoundaryAxis.Depth;

            if (!hand.IsDataValid || !hand.IsTracked)
            {
                sample.status = nameof(HandCaptureBoundaryStatus.TrackingLost);
                return HandCaptureBoundaryStatus.TrackingLost;
            }

            sample.tracked = true;
            sample.confidence =
                hand.HandConfidence == OVRHand.TrackingConfidence.High ? 1f : 0f;
            sample.high_confidence =
                hand.IsDataHighConfidence &&
                hand.HandConfidence != OVRHand.TrackingConfidence.Low;

            Vector3 localPosition = hmd.InverseTransformPoint(
                hand.transform.position
            );
            float depth = localPosition.z;

            // Margin is computed before the confidence early-out so a low-confidence
            // hand still reports where it was, which is what the offline analysis of
            // signing space versus tracking volume needs.
            float margin = depth <= 0f
                ? -1f
                : ComputeMargin(localPosition, depth, out axis);
            sample.boundary_margin = margin;
            sample.inside_safe_zone = margin >= 0f;

            if (!sample.high_confidence)
            {
                sample.status = nameof(HandCaptureBoundaryStatus.LowConfidence);
                return HandCaptureBoundaryStatus.LowConfidence;
            }

            if (margin < 0f)
            {
                sample.status = nameof(HandCaptureBoundaryStatus.OutsideBoundary);
                return HandCaptureBoundaryStatus.OutsideBoundary;
            }

            if (margin <= warningMarginFraction)
            {
                sample.status = nameof(HandCaptureBoundaryStatus.NearBoundary);
                return HandCaptureBoundaryStatus.NearBoundary;
            }

            sample.status = nameof(HandCaptureBoundaryStatus.Safe);
            return HandCaptureBoundaryStatus.Safe;
        }

        /// <summary>
        /// Normalized headroom to the nearest safe-zone wall: 1 at the centre,
        /// 0 on the boundary, negative outside. Also reports which wall is closest
        /// so the glow can light the matching screen edge.
        /// </summary>
        private float ComputeMargin(
            Vector3 localPosition,
            float depth,
            out BoundaryAxis axis)
        {
            float horizontalAngle =
                Mathf.Atan2(localPosition.x, depth) * Mathf.Rad2Deg;
            float verticalAngle =
                Mathf.Atan2(localPosition.y, depth) * Mathf.Rad2Deg;

            float horizontalMargin =
                1f - Mathf.Abs(horizontalAngle) / horizontalHalfAngle;
            float verticalLimit =
                verticalAngle >= 0f ? upperHalfAngle : lowerHalfAngle;
            float verticalMargin =
                1f - Mathf.Abs(verticalAngle) / verticalLimit;

            float depthSpan = Mathf.Max(0.01f, farDistance - nearDistance);
            float depthMargin = Mathf.Min(
                (depth - nearDistance) / depthSpan,
                (farDistance - depth) / depthSpan
            );

            axis = BoundaryAxis.Depth;
            float smallest = depthMargin;
            if (horizontalMargin < smallest)
            {
                smallest = horizontalMargin;
                axis = horizontalAngle >= 0f
                    ? BoundaryAxis.Right
                    : BoundaryAxis.Left;
            }
            if (verticalMargin < smallest)
            {
                smallest = verticalMargin;
                axis = verticalAngle >= 0f
                    ? BoundaryAxis.Up
                    : BoundaryAxis.Down;
            }

            return smallest;
        }

        private enum BoundaryAxis { Left, Right, Up, Down, Depth }

        private BoundaryAxis leftAxis;
        private BoundaryAxis rightAxis;

        /// <summary>
        /// Turns each hand's remaining headroom into edge intensities. The glow
        /// starts rising as soon as the hand enters the warning band and reaches
        /// full strength once it is outside, so it reads as a distance rather than
        /// an on/off alarm.
        /// </summary>
        private void RefreshGlow()
        {
            if (boundaryGlow == null)
            {
                return;
            }

            float glowLeft = 0f;
            float glowRight = 0f;
            float glowUp = 0f;
            float glowDown = 0f;

            Accumulate(leftSample, leftAxis, ref glowLeft, ref glowRight, ref glowUp, ref glowDown);
            Accumulate(rightSample, rightAxis, ref glowLeft, ref glowRight, ref glowUp, ref glowDown);

            boundaryGlow.SetIntensities(glowLeft, glowRight, glowUp, glowDown);
            boundaryGlow.gameObject.SetActive(boundaryGlow.HasAnyGlow);
        }

        private void Accumulate(
            HandCaptureSample sample,
            BoundaryAxis axis,
            ref float glowLeft,
            ref float glowRight,
            ref float glowUp,
            ref float glowDown)
        {
            if (!sample.tracked)
            {
                // A lost hand cannot be localised, so warn on every edge at once.
                // Outside a take the teacher is usually just resting their arms, so
                // the glow stays off there and only the text line mentions it.
                if (coordinator.State != RecordingFlowState.Recording &&
                    coordinator.State != RecordingFlowState.Countdown)
                {
                    return;
                }

                const float lost = 0.85f;
                glowLeft = Mathf.Max(glowLeft, lost);
                glowRight = Mathf.Max(glowRight, lost);
                glowUp = Mathf.Max(glowUp, lost);
                glowDown = Mathf.Max(glowDown, lost);
                return;
            }

            float margin = sample.boundary_margin;
            if (margin > warningMarginFraction)
            {
                return;
            }

            float intensity = margin < 0f
                ? 1f
                : Mathf.InverseLerp(warningMarginFraction, 0f, margin);

            switch (axis)
            {
                case BoundaryAxis.Left:
                    glowLeft = Mathf.Max(glowLeft, intensity);
                    break;
                case BoundaryAxis.Right:
                    glowRight = Mathf.Max(glowRight, intensity);
                    break;
                case BoundaryAxis.Up:
                    glowUp = Mathf.Max(glowUp, intensity);
                    break;
                case BoundaryAxis.Down:
                    glowDown = Mathf.Max(glowDown, intensity);
                    break;
                default:
                    glowLeft = Mathf.Max(glowLeft, intensity * 0.6f);
                    glowRight = Mathf.Max(glowRight, intensity * 0.6f);
                    break;
            }
        }

        /// <summary>
        /// The envelope is shown only before a take, so the teacher can wave around
        /// and learn where the edges are instead of discovering them through
        /// warnings while signing.
        /// </summary>
        private void RefreshSafeZonePreview()
        {
            if (safeZonePreview == null)
            {
                return;
            }

            safeZonePreview.SetActive(
                coordinator.State == RecordingFlowState.Ready ||
                coordinator.State == RecordingFlowState.Completed
            );
        }

        private static bool TryBuildWarning(
            HandCaptureBoundaryStatus left,
            HandCaptureBoundaryStatus right,
            out string message,
            out Color color)
        {
            bool leftLost = left == HandCaptureBoundaryStatus.TrackingLost;
            bool rightLost = right == HandCaptureBoundaryStatus.TrackingLost;
            if (leftLost && rightLost)
            {
                message = "双手未被追踪，请移回头显前方";
                color = new Color(1f, 0.34f, 0.25f, 1f);
                return true;
            }
            if (leftLost || rightLost)
            {
                message = leftLost
                    ? "左手未被追踪，请移回头显前方"
                    : "右手未被追踪，请移回头显前方";
                color = new Color(1f, 0.34f, 0.25f, 1f);
                return true;
            }

            bool leftOutside = left == HandCaptureBoundaryStatus.OutsideBoundary;
            bool rightOutside = right == HandCaptureBoundaryStatus.OutsideBoundary;
            if (leftOutside || rightOutside)
            {
                message = leftOutside && rightOutside
                    ? "双手已超出安全追踪区域，请移回中央"
                    : leftOutside
                        ? "左手已超出安全追踪区域，请移回中央"
                        : "右手已超出安全追踪区域，请移回中央";
                color = new Color(1f, 0.52f, 0.2f, 1f);
                return true;
            }

            bool leftLow = left == HandCaptureBoundaryStatus.LowConfidence;
            bool rightLow = right == HandCaptureBoundaryStatus.LowConfidence;
            if (leftLow || rightLow)
            {
                message = leftLow && rightLow
                    ? "双手追踪不稳定，请避免遮挡"
                    : leftLow
                        ? "左手追踪不稳定，请避免遮挡"
                        : "右手追踪不稳定，请避免遮挡";
                color = new Color(1f, 0.68f, 0.24f, 1f);
                return true;
            }

            bool leftNear = left == HandCaptureBoundaryStatus.NearBoundary;
            bool rightNear = right == HandCaptureBoundaryStatus.NearBoundary;
            if (leftNear || rightNear)
            {
                message = leftNear && rightNear
                    ? "双手接近追踪边界"
                    : leftNear
                        ? "左手接近追踪边界"
                        : "右手接近追踪边界";
                color = new Color(1f, 0.78f, 0.3f, 1f);
                return true;
            }

            message = string.Empty;
            color = Color.white;
            return false;
        }

        private void HideImmediately()
        {
            warningTimer = 0f;
            clearTimer = 0f;
            warningRoot.SetActive(false);

            if (boundaryGlow != null)
            {
                boundaryGlow.Clear();
                boundaryGlow.gameObject.SetActive(false);
            }

            if (safeZonePreview != null)
            {
                safeZonePreview.SetActive(false);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (hmd == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.72f, 0.18f, 0.8f);
            DrawBoundaryGizmo();
        }

        private void DrawBoundaryGizmo()
        {
            Vector3[] nearCorners = BuildPlaneCorners(nearDistance);
            Vector3[] farCorners = BuildPlaneCorners(farDistance);

            for (int index = 0; index < 4; index++)
            {
                int next = (index + 1) % 4;
                Gizmos.DrawLine(
                    hmd.TransformPoint(nearCorners[index]),
                    hmd.TransformPoint(nearCorners[next])
                );
                Gizmos.DrawLine(
                    hmd.TransformPoint(farCorners[index]),
                    hmd.TransformPoint(farCorners[next])
                );
                Gizmos.DrawLine(
                    hmd.TransformPoint(nearCorners[index]),
                    hmd.TransformPoint(farCorners[index])
                );
            }
        }

        private Vector3[] BuildPlaneCorners(float depth)
        {
            float halfWidth =
                Mathf.Tan(horizontalHalfAngle * Mathf.Deg2Rad) * depth;
            float top = Mathf.Tan(upperHalfAngle * Mathf.Deg2Rad) * depth;
            float bottom = Mathf.Tan(lowerHalfAngle * Mathf.Deg2Rad) * depth;
            return new[]
            {
                new Vector3(-halfWidth, top, depth),
                new Vector3(halfWidth, top, depth),
                new Vector3(halfWidth, -bottom, depth),
                new Vector3(-halfWidth, -bottom, depth)
            };
        }
#endif
    }
}
