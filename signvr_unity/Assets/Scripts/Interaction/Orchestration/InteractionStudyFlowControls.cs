using System;
using System.Globalization;
using SignVR.Interaction.Core;
using SignVR.Interaction.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.Orchestration
{
    internal static class InteractionStudyParticipantText
    {
        internal const string Recovering =
            "正在整理上一次未完整结束的实验，请稍候…";
        internal const string RecoveryFailed =
            "上一次实验数据整理失败，请联系工作人员。";
        internal const string Ready =
            "准备就绪，请点击“开始体验”。";
        internal const string PauseAborted =
            "上一次体验因头盔暂停已安全结束，可以开始新的体验。";
        internal const string CompletedResult = "本次体验已完成。";
        internal const string AbortedResult = "本次体验已安全结束。";
        internal const string SavingResult = "正在安全保存数据，请稍候…";
        internal const string SavedResult = "数据已安全保存。";

        internal static string ForSavedResult(
            InteractionStudyResultReviewSummary summary)
        {
            if (summary == null)
            {
                return SavedResult + "\n录制统计暂不可用，已保存文件可供复核。";
            }

            string qualityDetails = summary.CaptureMeasurementAvailable
                ? summary.ActualSampleRateHz.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture
                    ) + " Hz；最低跟踪 " +
                    summary.MinimumTrackingValidityRate.ToString(
                        "P0",
                        CultureInfo.InvariantCulture
                    ) + "；探针覆盖 " +
                    summary.RequiredProbeCoverageRate.ToString(
                        "P0",
                        CultureInfo.InvariantCulture
                    ) + "；缺口 " + summary.CaptureGapCount
                : "采样测量不可用";
            string artifactStatus = summary.LocalArtifactsComplete
                ? "本地文件：5/5 完整。"
                : "本地文件：需要复核。";

            return SavedResult + "\n录制质量：" + summary.CaptureQuality +
                "（" + qualityDetails + "；低质量数据仍会保留）。\n" +
                "流程统计：阶段 " + summary.CompletedPhaseCount + "/" +
                summary.TotalPhaseCount + "，错误 " + summary.ErrorCount +
                "，重播 " + summary.ReplayCount + "，卡住 " +
                summary.StuckCount + "，用时 " +
                summary.DurationSeconds.ToString(
                    "0.0",
                    CultureInfo.InvariantCulture
                ) + " 秒。\n" + artifactStatus;
        }

        internal static string ForFailure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return "操作暂时无法完成，请稍候重试。";
            }
            if (error.IndexOf(
                    "manifest",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "实验内容仍在加载，请稍候。";
            }
            if (error.IndexOf("HMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("XR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf(
                    "capture gate",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "请戴好头显，并将双手置于可识别范围内。";
            }
            return "操作暂时无法完成，请联系工作人员。";
        }

        internal static string ForPreStart(
            bool recoveryComplete,
            bool recoveryFailed,
            bool manifestReady,
            bool identityReady,
            bool canStart,
            string initializationStatus,
            string flowStatus,
            string commandFeedback,
            string pauseNotice)
        {
            if (recoveryFailed)
            {
                return RecoveryFailed;
            }
            if (!recoveryComplete)
            {
                return Recovering;
            }
            if (!manifestReady)
            {
                return ForFailure(initializationStatus);
            }
            if (!identityReady)
            {
                return "匿名实验编号仍在自动准备，请稍候。";
            }
            if (!string.IsNullOrWhiteSpace(commandFeedback))
            {
                return commandFeedback;
            }
            if (!canStart)
            {
                return ForFailure(flowStatus);
            }
            return string.IsNullOrEmpty(pauseNotice) ? Ready : pauseNotice;
        }

        internal static string ForRunState(RunState state, string status)
        {
            switch (state)
            {
                case RunState.Preparing:
                case RunState.Scheduled:
                    return "正在启动体验，请稍候…";
                case RunState.Running:
                    return "请按照场景提示完成当前体验。";
                case RunState.Completing:
                    return "正在保存本次体验，请稍候…";
                case RunState.Completed:
                    return CompletedResult;
                case RunState.Aborting:
                    return "正在安全结束本次体验，请稍候…";
                case RunState.Aborted:
                    return AbortedResult;
                case RunState.Faulted:
                    return "本次体验无法继续，请联系工作人员。";
                default:
                    return ForFailure(status);
            }
        }
    }

    /// <summary>
    /// Minimal W8 UI adapter. Start is W8-owned; the existing W5 controls are
    /// explicitly routed here for W6-token-authoritative Replay/GiveUp/Abort.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class InteractionStudyFlowControls :
        MonoBehaviour,
        IInteractionInstructionCommandSink
    {
        [SerializeField]
        private InteractionStudyFlowController flowController;

        [SerializeField]
        private InteractionInstructionControls instructionControls;

        [SerializeField]
        private GameObject preStartRoot;

        [SerializeField]
        private Button startButton;

        [SerializeField]
        private TMP_Text statusLabel;

        [SerializeField]
        private TMP_Text progressLabel;

        [SerializeField]
        private GameObject resultRoot;

        [SerializeField]
        private TMP_Text resultOutcomeLabel;

        [SerializeField]
        private TMP_Text resultSaveStatusLabel;

        [SerializeField]
        private Button acknowledgeResultButton;

        private bool bound;
        private string lastCommandFeedback = string.Empty;
        private string pauseAbortNotice = string.Empty;

        public event Action StateChanged;

        public InteractionStudyFlowController FlowController => flowController;
        public InteractionInstructionControls InstructionControls =>
            instructionControls;
        public GameObject PreStartRoot => preStartRoot;
        public Button StartButton => startButton;
        public TMP_Text StatusLabel => statusLabel;
        public TMP_Text ProgressLabel => progressLabel;
        public GameObject ResultRoot => resultRoot;
        public TMP_Text ResultOutcomeLabel => resultOutcomeLabel;
        public TMP_Text ResultSaveStatusLabel => resultSaveStatusLabel;
        public Button AcknowledgeResultButton => acknowledgeResultButton;

        public bool CanReplay =>
            flowController?.Snapshot?.CanReplay == true;
        public bool CanGiveUp =>
            flowController?.Snapshot?.CanGiveUp == true;
        public bool CanAbort
        {
            get
            {
                RunState? state = flowController?.Snapshot?.RunState;
                return state == RunState.Scheduled ||
                    state == RunState.Running ||
                    state == RunState.Completing;
            }
        }

        public void Configure(
            InteractionStudyFlowController flow,
            InteractionInstructionControls existingInstructionControls,
            GameObject startSurface,
            Button start,
            TMP_Text status,
            TMP_Text progress)
        {
            Configure(
                flow,
                existingInstructionControls,
                startSurface,
                start,
                status,
                progress,
                null,
                null,
                null,
                null
            );
        }

        public void Configure(
            InteractionStudyFlowController flow,
            InteractionInstructionControls existingInstructionControls,
            GameObject startSurface,
            Button start,
            TMP_Text status,
            TMP_Text progress,
            GameObject resultSurface,
            TMP_Text resultOutcome,
            TMP_Text resultSaveStatus,
            Button acknowledgeResult)
        {
            InteractionStudyFlowController validatedFlow = flow ??
                throw new ArgumentNullException(nameof(flow));
            InteractionInstructionControls validatedInstructionControls =
                existingInstructionControls ??
                throw new ArgumentNullException(
                    nameof(existingInstructionControls)
                );
            GameObject validatedStartSurface = startSurface ??
                throw new ArgumentNullException(nameof(startSurface));
            Button validatedStart = start ??
                throw new ArgumentNullException(nameof(start));
            TMP_Text validatedStatus = status ??
                throw new ArgumentNullException(nameof(status));
            TMP_Text validatedProgress = progress ??
                throw new ArgumentNullException(nameof(progress));
            bool hasAnyResultBinding = resultSurface != null ||
                resultOutcome != null || resultSaveStatus != null ||
                acknowledgeResult != null;
            if (hasAnyResultBinding && (resultSurface == null ||
                resultOutcome == null || resultSaveStatus == null ||
                acknowledgeResult == null))
            {
                throw new ArgumentException(
                    "Result page bindings must be supplied together."
                );
            }
            if (!validatedInstructionControls.CanInstallCommandSink(this))
            {
                throw new InvalidOperationException(
                    "The replacement W5 controls are owned by another " +
                    "Study Flow command sink."
                );
            }

            InteractionStudyFlowController oldFlow = flowController;
            InteractionInstructionControls oldInstructionControls =
                instructionControls;
            GameObject oldPreStartRoot = preStartRoot;
            Button oldStartButton = startButton;
            TMP_Text oldStatus = statusLabel;
            TMP_Text oldProgress = progressLabel;
            GameObject oldResultRoot = resultRoot;
            TMP_Text oldResultOutcome = resultOutcomeLabel;
            TMP_Text oldResultSaveStatus = resultSaveStatusLabel;
            Button oldAcknowledgeResult = acknowledgeResultButton;
            bool restoreBinding = bound;

            Unbind();
            flowController = validatedFlow;
            instructionControls = validatedInstructionControls;
            preStartRoot = validatedStartSurface;
            startButton = validatedStart;
            statusLabel = validatedStatus;
            progressLabel = validatedProgress;
            resultRoot = resultSurface;
            resultOutcomeLabel = resultOutcome;
            resultSaveStatusLabel = resultSaveStatus;
            acknowledgeResultButton = acknowledgeResult;
            try
            {
                Bind();
                Refresh();
            }
            catch
            {
                Unbind();
                flowController = oldFlow;
                instructionControls = oldInstructionControls;
                preStartRoot = oldPreStartRoot;
                startButton = oldStartButton;
                statusLabel = oldStatus;
                progressLabel = oldProgress;
                resultRoot = oldResultRoot;
                resultOutcomeLabel = oldResultOutcome;
                resultSaveStatusLabel = oldResultSaveStatus;
                acknowledgeResultButton = oldAcknowledgeResult;
                if (restoreBinding)
                {
                    Bind();
                }
                Refresh();
                throw;
            }
        }

        public void RequestReplay()
        {
            flowController?.TryReplay();
            Refresh();
        }

        public void RequestGiveUp()
        {
            flowController?.TryGiveUp();
            Refresh();
        }

        public void RequestAbort()
        {
            flowController?.TryAbort();
            Refresh();
        }

        private void Awake()
        {
            Bind();
            Refresh();
        }

        private void OnEnable()
        {
            Bind();
            Refresh();
        }

        private void Bind()
        {
            if (bound || !isActiveAndEnabled || flowController == null ||
                instructionControls == null || startButton == null)
            {
                return;
            }
            if (!instructionControls.TryInstallCommandSink(this))
            {
                throw new InvalidOperationException(
                    "W5 command routing is already owned by another sink."
                );
            }
            try
            {
                flowController.StateChanged += Refresh;
                startButton.onClick.AddListener(HandleStart);
                acknowledgeResultButton?.onClick.AddListener(
                    HandleAcknowledgeResult
                );
                bound = true;
            }
            catch
            {
                instructionControls.TryClearCommandSink(this);
                throw;
            }
        }

        private void Unbind()
        {
            if (bound)
            {
                if (flowController != null)
                {
                    flowController.StateChanged -= Refresh;
                }
                startButton?.onClick.RemoveListener(HandleStart);
                acknowledgeResultButton?.onClick.RemoveListener(
                    HandleAcknowledgeResult
                );
                bound = false;
            }
            if (instructionControls != null)
            {
                instructionControls.TryClearCommandSink(this);
            }
        }

        private void HandleStart()
        {
            InteractionStudyFlowCommandResult result =
                flowController?.TryStart() ??
                InteractionStudyFlowCommandResult.Failure(
                    "Study Flow controller is missing."
                );
            if (result.Succeeded)
            {
                lastCommandFeedback = string.Empty;
                pauseAbortNotice = string.Empty;
            }
            else
            {
                lastCommandFeedback =
                    InteractionStudyParticipantText.ForFailure(result.Error);
            }
            Refresh();
        }

        private void HandleAcknowledgeResult()
        {
            InteractionStudyFlowCommandResult result =
                flowController?.TryAcknowledgeResult() ??
                InteractionStudyFlowCommandResult.Failure(
                    "Study Flow controller is missing."
                );
            lastCommandFeedback = result.Succeeded
                ? string.Empty
                : InteractionStudyParticipantText.ForFailure(result.Error);
            Refresh();
        }

        private void Refresh()
        {
            InteractionStudyFlowSnapshot snapshot = flowController?.Snapshot;
            bool preStart = snapshot == null ||
                snapshot.RunState == RunState.PreStart;
            bool resultVisible = snapshot?.IsResultVisible == true;
            if (flowController?.TryConsumePauseAbortNotice() == true)
            {
                pauseAbortNotice = InteractionStudyParticipantText.PauseAborted;
            }
            if (preStartRoot != null && preStartRoot != gameObject)
            {
                preStartRoot.SetActive(preStart);
            }
            if (resultRoot != null && resultRoot != gameObject)
            {
                resultRoot.SetActive(resultVisible);
            }
            if (startButton != null)
            {
                startButton.interactable = preStart &&
                    flowController != null && flowController.ManifestReady &&
                    flowController.RecoveryComplete &&
                    flowController.IdentityReady &&
                    snapshot?.CanStart == true;
                TMP_Text startLabel = startButton.GetComponentInChildren<
                    TMP_Text>(true);
                if (startLabel != null)
                {
                    startLabel.text = startButton.interactable
                        ? "开始体验"
                        : "正在准备…";
                }
            }
            ApplyParticipantOnlyLayout();
            if (statusLabel != null)
            {
                string flowStatus = snapshot?.Status ?? "PreStart";
                string initialization = flowController == null
                    ? "Study Flow controller is missing."
                    : flowController.InitializationStatus;
                if (flowController == null)
                {
                    statusLabel.text =
                        InteractionStudyParticipantText.ForFailure(initialization);
                }
                else if (preStart)
                {
                    statusLabel.text = InteractionStudyParticipantText.ForPreStart(
                        flowController.RecoveryComplete,
                        flowController.RecoveryFailed,
                        flowController.ManifestReady,
                        flowController.IdentityReady,
                        snapshot?.CanStart == true,
                        initialization,
                        flowStatus,
                        lastCommandFeedback,
                        pauseAbortNotice
                    );
                }
                else if (flowController.RecoveryFailed)
                {
                    statusLabel.text =
                        InteractionStudyParticipantText.RecoveryFailed;
                }
                else if (!flowController.RecoveryComplete)
                {
                    statusLabel.text = InteractionStudyParticipantText.Recovering;
                }
                else if (!string.IsNullOrWhiteSpace(lastCommandFeedback))
                {
                    statusLabel.text = lastCommandFeedback;
                }
                else
                {
                    statusLabel.text =
                        InteractionStudyParticipantText.ForRunState(
                            snapshot.RunState,
                            flowStatus
                        );
                }
            }
            if (progressLabel != null)
            {
                progressLabel.text = snapshot?.PhaseId.HasValue == true
                    ? "阶段 " + snapshot.PhaseId.Value + " 进度 " +
                        snapshot.Progress + "/" + snapshot.RequiredProgress
                    : string.Empty;
            }
            if (resultOutcomeLabel != null)
            {
                resultOutcomeLabel.text = snapshot?.TerminalOutcome switch
                {
                    RunResult.Completed =>
                        InteractionStudyParticipantText.CompletedResult,
                    RunResult.Aborted =>
                        InteractionStudyParticipantText.AbortedResult,
                    _ => string.Empty
                };
            }
            if (resultSaveStatusLabel != null)
            {
                resultSaveStatusLabel.text =
                    snapshot?.CanAcknowledgeResult == true
                        ? InteractionStudyParticipantText.ForSavedResult(
                            snapshot.ResultReviewSummary
                        )
                        : InteractionStudyParticipantText.SavingResult;
            }
            if (acknowledgeResultButton != null)
            {
                acknowledgeResultButton.interactable = resultVisible &&
                    snapshot?.CanAcknowledgeResult == true;
                TMP_Text acknowledgeLabel = acknowledgeResultButton
                    .GetComponentInChildren<TMP_Text>(true);
                if (acknowledgeLabel != null)
                {
                    acknowledgeLabel.text = "确认返回";
                }
            }
            StateChanged?.Invoke();
        }

        private void ApplyParticipantOnlyLayout()
        {
            if (startButton?.transform is RectTransform startRect)
            {
                startRect.anchoredPosition = new Vector2(0f, 70f);
                startRect.sizeDelta = new Vector2(520f, 110f);
            }
            if (statusLabel?.rectTransform != null)
            {
                statusLabel.rectTransform.anchoredPosition =
                    new Vector2(0f, -55f);
                statusLabel.rectTransform.sizeDelta =
                    new Vector2(700f, 150f);
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
            StateChanged = null;
        }
    }
}
