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

        [Tooltip("只显示 Quest 3 实机测得边界的空间引导，不绘制假定相机视锥。")]
        [SerializeField]
        private QuestHandTrackingBoundaryGuide boundaryGuide;

        [Header("Warning behavior")]
        [SerializeField]
        [Range(0f, 0.5f)]
        private float warningDelaySeconds = 0.08f;

        [SerializeField]
        [Range(0f, 1f)]
        private float clearDelaySeconds = 0.35f;

        [Tooltip("短暂遮挡不算真正丢失；持续超过此时间才升级为强提醒。")]
        [SerializeField]
        [Range(0.1f, 1f)]
        private float trackingLossConfirmSeconds = 0.35f;

        [Tooltip("越界文字比方向光晕更晚出现，避免正常手语动作频繁打断视线。")]
        [SerializeField]
        [Range(0.2f, 1.5f)]
        private float outsideMessageDelaySeconds = 0.55f;

        private float warningTimer;
        private float clearTimer;
        private string pendingMessage = string.Empty;
        private Color pendingColor;
        private HandCaptureSample leftSample;
        private HandCaptureSample rightSample;
        private float leftTrackingLossSeconds;
        private float rightTrackingLossSeconds;

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
            boundaryGuide?.SetGuidanceEnabled(enabled);
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
            TMP_Text warningText,
            QuestHandTrackingBoundaryGuide trackingBoundaryGuide)
        {
            coordinator = recordingCoordinator;
            hmd = hmdTransform;
            leftHand = left;
            rightHand = right;
            warningRoot = warningPanel;
            warningLabel = warningText;
            boundaryGuide = trackingBoundaryGuide;
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
            boundaryGuide?.SetGuidanceEnabled(GuidanceEnabled);
        }

        private void Update()
        {
            // Sampling runs in every state and regardless of the guidance switch,
            // so the guidance-on and guidance-off passes stay comparable.
            HandCaptureBoundaryStatus rawLeft =
                Evaluate(leftHand, out leftSample, out leftAxis);
            HandCaptureBoundaryStatus rawRight =
                Evaluate(rightHand, out rightSample, out rightAxis);

            LeftStatus = ConfirmTrackingLoss(
                rawLeft,
                ref leftTrackingLossSeconds
            );
            RightStatus = ConfirmTrackingLoss(
                rawRight,
                ref rightTrackingLossSeconds
            );

            if (
                GuidanceEnabled &&
                boundaryGuide != null &&
                boundaryGuide.IsCalibrating &&
                coordinator.State == RecordingFlowState.Ready
            )
            {
                warningTimer = 0f;
                clearTimer = 0f;
                warningLabel.text = boundaryGuide.CalibrationInstruction;
                warningLabel.color = new Color(0.30f, 0.88f, 1f, 1f);
                warningRoot.SetActive(true);
                if (boundaryGlow != null)
                {
                    boundaryGlow.Clear();
                    boundaryGlow.gameObject.SetActive(false);
                }
                return;
            }

            if (!ShouldMonitorCurrentState() || !GuidanceEnabled)
            {
                HideImmediately();
                return;
            }

            RefreshGlow();

            bool shouldWarn = TryBuildWarning(
                LeftStatus,
                RightStatus,
                out pendingMessage,
                out pendingColor,
                out bool critical
            );

            if (shouldWarn)
            {
                clearTimer = 0f;
                warningTimer += Time.unscaledDeltaTime;
                float messageDelay = critical
                    ? warningDelaySeconds
                    : outsideMessageDelaySeconds;
                if (warningTimer >= messageDelay)
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
            return coordinator.State == RecordingFlowState.Countdown ||
                   coordinator.State == RecordingFlowState.Recording ||
                   coordinator.State == RecordingFlowState.Reviewing;
        }

        private HandCaptureBoundaryStatus ConfirmTrackingLoss(
            HandCaptureBoundaryStatus rawStatus,
            ref float lostSeconds)
        {
            if (rawStatus != HandCaptureBoundaryStatus.TrackingLost)
            {
                lostSeconds = 0f;
                return rawStatus;
            }

            if (!ShouldMonitorCurrentState())
            {
                lostSeconds = 0f;
                return HandCaptureBoundaryStatus.LowConfidence;
            }

            lostSeconds += Time.unscaledDeltaTime;
            return lostSeconds >= trackingLossConfirmSeconds
                ? HandCaptureBoundaryStatus.TrackingLost
                : HandCaptureBoundaryStatus.LowConfidence;
        }

        private HandCaptureBoundaryStatus Evaluate(
            OVRHand hand,
            out HandCaptureSample sample,
            out BoundaryAxis axis)
        {
            sample = default;
            sample.boundary_margin = -1f;
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
            sample.pose_source_inferred = hand.PoseSourceInferred;

            Vector3 localPosition = hmd.InverseTransformPoint(
                hand.transform.position
            );
            axis = DirectionFromPosition(localPosition);

            // Meta's WMM flag is the device-level answer to whether this pose still
            // comes from camera hand tracking. It is intentionally used instead of
            // a hand-authored angular frustum: Quest does not expose that geometry.
            if (sample.pose_source_inferred)
            {
                sample.inside_safe_zone = false;
                sample.boundary_margin = -1f;
                sample.status = nameof(HandCaptureBoundaryStatus.OutsideBoundary);
                return HandCaptureBoundaryStatus.OutsideBoundary;
            }

            if (!sample.high_confidence)
            {
                sample.inside_safe_zone = false;
                sample.boundary_margin = 0f;
                sample.status = nameof(HandCaptureBoundaryStatus.LowConfidence);
                return HandCaptureBoundaryStatus.LowConfidence;
            }

            sample.inside_safe_zone = true;
            sample.boundary_margin = 1f;
            sample.status = nameof(HandCaptureBoundaryStatus.Safe);
            return HandCaptureBoundaryStatus.Safe;
        }

        private static BoundaryAxis DirectionFromPosition(Vector3 localPosition)
        {
            if (Mathf.Abs(localPosition.x) >= Mathf.Abs(localPosition.y))
            {
                return localPosition.x >= 0f
                    ? BoundaryAxis.Right
                    : BoundaryAxis.Left;
            }
            return localPosition.y >= 0f
                ? BoundaryAxis.Up
                : BoundaryAxis.Down;
        }

        private enum BoundaryAxis { Left, Right, Up, Down, Depth }

        private BoundaryAxis leftAxis;
        private BoundaryAxis rightAxis;

        /// <summary>
        /// Keeps ordinary signing quiet. A real geometric overrun gets a restrained
        /// directional amber cue; sustained tracking loss switches to an unmistakable
        /// four-edge red pulse.
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

            bool trackingLost =
                LeftStatus == HandCaptureBoundaryStatus.TrackingLost ||
                RightStatus == HandCaptureBoundaryStatus.TrackingLost;

            if (trackingLost)
            {
                float intensity =
                    LeftStatus == HandCaptureBoundaryStatus.TrackingLost &&
                    RightStatus == HandCaptureBoundaryStatus.TrackingLost
                        ? 1f
                        : 0.9f;
                glowLeft = intensity;
                glowRight = intensity;
                glowUp = intensity;
                glowDown = intensity;
                boundaryGlow.SetVisualStyle(
                    new Color(1f, 0.16f, 0.10f, 0.76f),
                    1.8f,
                    0.34f
                );
            }
            else
            {
                Accumulate(
                    leftSample,
                    LeftStatus,
                    leftAxis,
                    ref glowLeft,
                    ref glowRight,
                    ref glowUp,
                    ref glowDown
                );
                Accumulate(
                    rightSample,
                    RightStatus,
                    rightAxis,
                    ref glowLeft,
                    ref glowRight,
                    ref glowUp,
                    ref glowDown
                );
                boundaryGlow.SetVisualStyle(
                    new Color(1f, 0.58f, 0.20f, 0.42f),
                    0.6f,
                    0.08f
                );
            }

            boundaryGlow.SetIntensities(glowLeft, glowRight, glowUp, glowDown);
            boundaryGlow.gameObject.SetActive(boundaryGlow.HasAnyGlow);
        }

        private void Accumulate(
            HandCaptureSample sample,
            HandCaptureBoundaryStatus status,
            BoundaryAxis axis,
            ref float glowLeft,
            ref float glowRight,
            ref float glowUp,
            ref float glowDown)
        {
            if (status == HandCaptureBoundaryStatus.TrackingLost)
            {
                return;
            }

            if (!sample.tracked)
            {
                return;
            }

            // Only a hand that has actually left the safe zone is worth a warning.
            // Fading the glow in while the hand was merely approaching the edge
            // fired almost continuously during signing and became noise.
            float margin = sample.boundary_margin;
            if (margin >= 0f)
            {
                return;
            }

            float intensity = 0.24f;

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

        private static bool TryBuildWarning(
            HandCaptureBoundaryStatus left,
            HandCaptureBoundaryStatus right,
            out string message,
            out Color color,
            out bool critical)
        {
            bool leftLost = left == HandCaptureBoundaryStatus.TrackingLost;
            bool rightLost = right == HandCaptureBoundaryStatus.TrackingLost;
            if (leftLost && rightLost)
            {
                message = "双手未被追踪，请移回头显前方";
                color = new Color(1f, 0.34f, 0.25f, 1f);
                critical = true;
                return true;
            }
            if (leftLost || rightLost)
            {
                message = leftLost
                    ? "左手未被追踪，请移回头显前方"
                    : "右手未被追踪，请移回头显前方";
                color = new Color(1f, 0.34f, 0.25f, 1f);
                critical = true;
                return true;
            }

            bool leftOutside = left == HandCaptureBoundaryStatus.OutsideBoundary;
            bool rightOutside = right == HandCaptureBoundaryStatus.OutsideBoundary;
            if (leftOutside || rightOutside)
            {
                message = leftOutside && rightOutside
                    ? "双手已离开相机追踪，动作正在推断，请移回中央"
                    : leftOutside
                        ? "左手已离开相机追踪，动作正在推断"
                        : "右手已离开相机追踪，动作正在推断";
                color = new Color(1f, 0.52f, 0.2f, 1f);
                critical = false;
                return true;
            }

            // Low confidence is also silent: hands overlap constantly in signing and
            // tracking still produces usable data there. It is still recorded in the
            // per-frame quality samples, so the operator can judge the take later.

            // "Approaching the boundary" is deliberately silent: during signing the
            // hands pass near the edge constantly, and warning there drowned out the
            // cases that actually cost data.
            message = string.Empty;
            color = Color.white;
            critical = false;
            return false;
        }

        private void HideImmediately()
        {
            warningTimer = 0f;
            clearTimer = 0f;
            leftTrackingLossSeconds = 0f;
            rightTrackingLossSeconds = 0f;
            warningRoot.SetActive(false);

            if (boundaryGlow != null)
            {
                boundaryGlow.Clear();
                boundaryGlow.gameObject.SetActive(false);
            }

        }
    }
}
