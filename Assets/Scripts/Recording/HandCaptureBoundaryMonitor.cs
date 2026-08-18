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

        public HandCaptureBoundaryStatus LeftStatus { get; private set; }
        public HandCaptureBoundaryStatus RightStatus { get; private set; }

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
            if (!ShouldMonitorCurrentState())
            {
                HideImmediately();
                return;
            }

            LeftStatus = Evaluate(leftHand);
            RightStatus = Evaluate(rightHand);

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

        private HandCaptureBoundaryStatus Evaluate(OVRHand hand)
        {
            if (!hand.IsDataValid || !hand.IsTracked)
            {
                return HandCaptureBoundaryStatus.TrackingLost;
            }

            if (
                !hand.IsDataHighConfidence ||
                hand.HandConfidence == OVRHand.TrackingConfidence.Low
            )
            {
                return HandCaptureBoundaryStatus.LowConfidence;
            }

            Vector3 localPosition = hmd.InverseTransformPoint(
                hand.transform.position
            );
            float depth = localPosition.z;
            if (depth <= 0f)
            {
                return HandCaptureBoundaryStatus.OutsideBoundary;
            }

            float horizontalAngle = Mathf.Abs(
                Mathf.Atan2(localPosition.x, depth) * Mathf.Rad2Deg
            );
            float verticalAngle =
                Mathf.Atan2(localPosition.y, depth) * Mathf.Rad2Deg;
            float verticalLimit = verticalAngle >= 0f
                ? upperHalfAngle
                : lowerHalfAngle;

            if (
                depth < nearDistance ||
                depth > farDistance ||
                horizontalAngle > horizontalHalfAngle ||
                Mathf.Abs(verticalAngle) > verticalLimit
            )
            {
                return HandCaptureBoundaryStatus.OutsideBoundary;
            }

            if (
                depth < nearDistance + distanceWarningMargin ||
                depth > farDistance - distanceWarningMargin ||
                horizontalAngle > horizontalHalfAngle - angularWarningMargin ||
                Mathf.Abs(verticalAngle) > verticalLimit - angularWarningMargin
            )
            {
                return HandCaptureBoundaryStatus.NearBoundary;
            }

            return HandCaptureBoundaryStatus.Safe;
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
