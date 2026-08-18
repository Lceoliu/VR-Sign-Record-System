using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Recording
{
    public sealed class RecordingTeacherUI : MonoBehaviour
    {
        [Header("Text")]
        [SerializeField]
        private TMP_Text promptText;

        [SerializeField]
        private TMP_Text statusText;

        [SerializeField]
        private TMP_Text countdownText;

        [SerializeField]
        private TMP_Text takeText;

        [SerializeField]
        private TMP_Text guidanceText;

        [SerializeField]
        private TMP_Text resetProgressLabel;

        [SerializeField]
        private TMP_FontAsset chineseFont;

        [Header("State")]
        [SerializeField]
        private Image statusIndicator;

        [SerializeField]
        private GameObject resetProgressRoot;

        [SerializeField]
        private RecordingProgressRingGraphic resetProgressRing;

        [SerializeField]
        private RecordingViewportFrameGraphic viewportFrame;

        [SerializeField]
        private RecordingReplayController replayController;

        [Header("Colors")]
        [SerializeField]
        private Color readyColor = new Color(0.15f, 0.55f, 0.35f, 1f);

        [SerializeField]
        private Color recordingColor = new Color(0.85f, 0.12f, 0.12f, 1f);

        [SerializeField]
        private Color busyColor = new Color(0.95f, 0.58f, 0.12f, 1f);

        [SerializeField]
        private Color errorColor = new Color(0.75f, 0.08f, 0.08f, 1f);

        private RecordingCoordinator coordinator;

        public void ConfigureEnhancements(
            TMP_FontAsset font,
            TMP_Text guidance,
            TMP_Text resetLabel,
            RecordingViewportFrameGraphic frame,
            RecordingReplayController replay)
        {
            chineseFont = font;
            guidanceText = guidance;
            resetProgressLabel = resetLabel;
            viewportFrame = frame;
            replayController = replay;
            ApplyChineseFont();
            Refresh();
        }

        public void Bind(RecordingCoordinator value)
        {
            if (coordinator != null)
            {
                coordinator.PresentationChanged -= Refresh;
            }

            coordinator = value;

            if (coordinator != null)
            {
                coordinator.PresentationChanged += Refresh;
            }

            if (replayController != null)
            {
                replayController.PresentationChanged += Refresh;
            }

            ApplyChineseFont();
            Refresh();
        }

        private void Update()
        {
            if (coordinator == null)
            {
                return;
            }

            if (
                coordinator.State == RecordingFlowState.Countdown ||
                coordinator.State == RecordingFlowState.Recording ||
                coordinator.State == RecordingFlowState.Reviewing ||
                coordinator.ResetHoldProgress > 0f
            )
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            if (coordinator == null)
            {
                return;
            }

            if (promptText != null)
            {
                promptText.text = coordinator.PromptText;
            }

            RefreshState();
            RefreshCountdown();
            RefreshTake();
            RefreshResetProgress();
            RefreshGuidance();
            RefreshViewportFrame();
        }

        private void RefreshState()
        {
            string label;
            Color color;

            switch (coordinator.State)
            {
                case RecordingFlowState.Ready:
                    label = "准备就绪";
                    color = readyColor;
                    break;
                case RecordingFlowState.Countdown:
                    label = "准备录制";
                    color = busyColor;
                    break;
                case RecordingFlowState.Recording:
                    label = $"● REC  {coordinator.RecordingElapsedSeconds:F1} 秒";
                    color = recordingColor;
                    break;
                case RecordingFlowState.Finalizing:
                    label = "正在保存";
                    color = busyColor;
                    break;
                case RecordingFlowState.Completed:
                    label = "已保存";
                    color = readyColor;
                    break;
                case RecordingFlowState.Reviewing:
                    label = replayController != null && replayController.IsLoading
                        ? "正在载入回看"
                        : "VR 动作回看";
                    color = new Color(0.12f, 0.55f, 0.78f, 1f);
                    break;
                case RecordingFlowState.Resetting:
                    label = "正在重置本句";
                    color = busyColor;
                    break;
                case RecordingFlowState.Error:
                    label = string.IsNullOrWhiteSpace(coordinator.LastError)
                        ? "发生错误"
                        : coordinator.LastError;
                    color = errorColor;
                    break;
                default:
                    label = "等待主机连接";
                    color = busyColor;
                    break;
            }

            if (statusText != null)
            {
                statusText.text = label;
            }

            if (statusIndicator != null)
            {
                statusIndicator.color = color;
            }
        }

        private void RefreshCountdown()
        {
            if (countdownText == null)
            {
                return;
            }

            bool visible = coordinator.State == RecordingFlowState.Countdown;
            countdownText.gameObject.SetActive(visible);

            if (visible)
            {
                countdownText.text = Mathf.Max(
                    1,
                    Mathf.CeilToInt(coordinator.CountdownRemaining)
                ).ToString();
            }
        }

        private void RefreshTake()
        {
            if (takeText == null)
            {
                return;
            }

            takeText.text = coordinator.CurrentTake.IsValid
                ? $"候选 Take {coordinator.CurrentTake.TakeIndex:D3}"
                : string.Empty;
        }

        private void RefreshResetProgress()
        {
            float progress = coordinator.ResetHoldProgress;

            if (resetProgressRoot != null)
            {
                resetProgressRoot.SetActive(progress > 0f);
            }

            if (resetProgressRing != null)
            {
                resetProgressRing.Progress = progress;
            }

            if (resetProgressLabel != null)
            {
                resetProgressLabel.text = $"继续按住以重录\n{Mathf.RoundToInt(progress * 100f)}%";
            }
        }

        private void RefreshGuidance()
        {
            if (guidanceText == null)
            {
                return;
            }

            if (coordinator.ResetHoldProgress > 0f)
            {
                guidanceText.text = "继续踩住直到圆环填满 · 松开可取消";
                return;
            }

            switch (coordinator.State)
            {
                case RecordingFlowState.Ready:
                    guidanceText.text = "踩一下外接空格键开始录制";
                    break;
                case RecordingFlowState.Countdown:
                    guidanceText.text = "请看向镜像角色，保持准备";
                    break;
                case RecordingFlowState.Recording:
                    guidanceText.text = "再踩一下结束录制";
                    break;
                case RecordingFlowState.Finalizing:
                    guidanceText.text = "动作正在保存，请稍候";
                    break;
                case RecordingFlowState.Completed:
                    guidanceText.text = "已保存为候选 Take · 可用桌面触屏重播";
                    break;
                case RecordingFlowState.Reviewing:
                    guidanceText.text = replayController != null && replayController.IsLoading
                        ? replayController.StatusLabel
                        : "桌面按钮可退出重播";
                    break;
                case RecordingFlowState.Resetting:
                    guidanceText.text = "本句已回到开始前状态，旧 Take 仍可追溯";
                    break;
                case RecordingFlowState.Error:
                    guidanceText.text = "请检查身体追踪和主机连接";
                    break;
                default:
                    guidanceText.text = "等待主机发送录制任务";
                    break;
            }
        }

        private void RefreshViewportFrame()
        {
            if (viewportFrame == null)
            {
                return;
            }

            bool visible = true;
            Color color;
            float alpha;

            switch (coordinator.State)
            {
                case RecordingFlowState.Countdown:
                    color = busyColor;
                    alpha = 0.5f;
                    break;
                case RecordingFlowState.Recording:
                    color = recordingColor;
                    alpha = 0.38f + Mathf.Sin(Time.unscaledTime * 4f) * 0.1f;
                    break;
                case RecordingFlowState.Reviewing:
                    color = new Color(0.12f, 0.55f, 0.78f, 1f);
                    alpha = 0f;
                    visible = false;
                    break;
                default:
                    color = busyColor;
                    alpha = coordinator.ResetHoldProgress > 0f
                        ? 0.55f + coordinator.ResetHoldProgress * 0.35f
                        : 0f;
                    visible = alpha > 0f;
                    break;
            }

            viewportFrame.gameObject.SetActive(visible);
            color.a = alpha;
            viewportFrame.color = color;
        }

        private void ApplyChineseFont()
        {
            if (chineseFont == null)
            {
                return;
            }

            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = chineseFont;
            }
        }

        private void OnDestroy()
        {
            if (coordinator != null)
            {
                coordinator.PresentationChanged -= Refresh;
            }
            if (replayController != null)
            {
                replayController.PresentationChanged -= Refresh;
            }
        }
    }
}
