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

        [Header("Pedal receipt")]
        [SerializeField]
        [Range(0.05f, 0.6f)]
        private float pedalFlashSeconds = 0.18f;

        [SerializeField]
        private Color pedalFlashColor = new Color(1f, 1f, 1f, 1f);

        private RecordingCoordinator coordinator;
        public void ConfigurePromptText(TMP_Text value)
        {
            promptText = value;
            ApplyChineseFont();
            Refresh();
        }

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
                coordinator.ResetHoldProgress > 0f ||
                PedalFlashStrength() > 0f
            )
            {
                Refresh();
            }
        }

        /// <summary>
        /// 1 right after the pedal is pressed, fading to 0 over the flash window.
        /// </summary>
        private float PedalFlashStrength()
        {
            float elapsed = Time.unscaledTime - coordinator.LastPedalPulseTime;
            if (elapsed < 0f || elapsed >= pedalFlashSeconds)
            {
                return 0f;
            }

            return 1f - (elapsed / pedalFlashSeconds);
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

            if (coordinator.IsHelpRequested || coordinator.IsPaused)
            {
                ApplyState(
                    coordinator.IsHelpRequested ? "已呼叫帮助，请稍候" : "已暂停 · 透视模式",
                    busyColor
                );
                return;
            }

            if (
                replayController != null &&
                replayController.HasError &&
                (coordinator.State == RecordingFlowState.Ready ||
                 coordinator.State == RecordingFlowState.Completed)
            )
            {
                ApplyState("回放未开始", errorColor);
                return;
            }

            switch (coordinator.State)
            {
                case RecordingFlowState.Ready:
                    label = "准备就绪";
                    color = readyColor;
                    break;
                case RecordingFlowState.Countdown:
                    label =
                        $"准备录制  {Mathf.Max(1, Mathf.CeilToInt(coordinator.CountdownRemaining))}";
                    color = busyColor;
                    break;
                case RecordingFlowState.Recording:
                    label = $"● REC  录制中 {coordinator.RecordingElapsedSeconds:F1} 秒";
                    color = recordingColor;
                    break;
                case RecordingFlowState.Finalizing:
                    label = "正在保存";
                    color = busyColor;
                    break;
                case RecordingFlowState.Completed:
                    label = "动作已保存";
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

            ApplyState(label, color);
        }

        private void ApplyState(string label, Color color)
        {
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

            countdownText.text = string.Empty;
            countdownText.gameObject.SetActive(false);
        }

        private void RefreshTake()
        {
            if (takeText == null)
            {
                return;
            }

            takeText.text = string.Empty;
            takeText.gameObject.SetActive(false);
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

            guidanceText.text = string.Empty;
            guidanceText.gameObject.SetActive(false);
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

            // The pedal flash overrides whatever the state was drawing. The teacher
            // cannot hear the pedal, so "the press registered" outranks every other
            // message the frame might be carrying at that moment.
            float flash = PedalFlashStrength();
            if (flash > 0f)
            {
                visible = true;
                color = pedalFlashColor;
                alpha = Mathf.Max(alpha, flash * 0.75f);
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

            if (
                promptText != null &&
                !promptText.transform.IsChildOf(transform)
            )
            {
                promptText.font = chineseFont;
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
