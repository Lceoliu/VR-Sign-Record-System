using System;
using SignVR.Interaction.Core;
using SignVR.Interaction.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.Orchestration
{
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
        private TMP_InputField participantIdInput;

        [SerializeField]
        private TMP_InputField buildIdentityInput;

        [SerializeField]
        private Button applyIdentityButton;

        [SerializeField]
        private TMP_Text statusLabel;

        [SerializeField]
        private TMP_Text progressLabel;

        private bool bound;
        private bool synchronizingIdentityInputs;

        public event Action StateChanged;

        public InteractionStudyFlowController FlowController => flowController;
        public InteractionInstructionControls InstructionControls =>
            instructionControls;
        public GameObject PreStartRoot => preStartRoot;
        public Button StartButton => startButton;
        public TMP_InputField ParticipantIdInput => participantIdInput;
        public TMP_InputField BuildIdentityInput => buildIdentityInput;
        public Button ApplyIdentityButton => applyIdentityButton;
        public TMP_Text StatusLabel => statusLabel;
        public TMP_Text ProgressLabel => progressLabel;

        public bool CanReplay =>
            flowController?.Snapshot?.CanReplay == true;
        public bool CanGiveUp =>
            flowController?.Snapshot?.CanGiveUp == true;
        public bool CanAbort
        {
            get
            {
                RunState? state = flowController?.Snapshot?.RunState;
                return state == RunState.AwaitingHost ||
                    state == RunState.Scheduled ||
                    state == RunState.Running ||
                    state == RunState.Completing;
            }
        }

        public void Configure(
            InteractionStudyFlowController flow,
            InteractionInstructionControls existingInstructionControls,
            GameObject startSurface,
            Button start,
            TMP_InputField participantInput,
            TMP_InputField buildInput,
            Button applyIdentity,
            TMP_Text status,
            TMP_Text progress)
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
            TMP_InputField validatedParticipantInput = participantInput ??
                throw new ArgumentNullException(nameof(participantInput));
            TMP_InputField validatedBuildInput = buildInput ??
                throw new ArgumentNullException(nameof(buildInput));
            Button validatedApplyIdentity = applyIdentity ??
                throw new ArgumentNullException(nameof(applyIdentity));
            TMP_Text validatedStatus = status ??
                throw new ArgumentNullException(nameof(status));
            TMP_Text validatedProgress = progress ??
                throw new ArgumentNullException(nameof(progress));
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
            TMP_InputField oldParticipantInput = participantIdInput;
            TMP_InputField oldBuildInput = buildIdentityInput;
            Button oldApplyIdentity = applyIdentityButton;
            TMP_Text oldStatus = statusLabel;
            TMP_Text oldProgress = progressLabel;
            bool restoreBinding = bound;

            Unbind();
            flowController = validatedFlow;
            instructionControls = validatedInstructionControls;
            preStartRoot = validatedStartSurface;
            startButton = validatedStart;
            participantIdInput = validatedParticipantInput;
            buildIdentityInput = validatedBuildInput;
            applyIdentityButton = validatedApplyIdentity;
            statusLabel = validatedStatus;
            progressLabel = validatedProgress;
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
                participantIdInput = oldParticipantInput;
                buildIdentityInput = oldBuildInput;
                applyIdentityButton = oldApplyIdentity;
                statusLabel = oldStatus;
                progressLabel = oldProgress;
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
                instructionControls == null || startButton == null ||
                participantIdInput == null || buildIdentityInput == null ||
                applyIdentityButton == null)
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
                applyIdentityButton.onClick.AddListener(HandleApplyIdentity);
                participantIdInput.onValueChanged.AddListener(
                    HandleIdentityDraftChanged
                );
                buildIdentityInput.onValueChanged.AddListener(
                    HandleIdentityDraftChanged
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
                applyIdentityButton?.onClick.RemoveListener(
                    HandleApplyIdentity
                );
                participantIdInput?.onValueChanged.RemoveListener(
                    HandleIdentityDraftChanged
                );
                buildIdentityInput?.onValueChanged.RemoveListener(
                    HandleIdentityDraftChanged
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
            flowController?.TryStart();
            Refresh();
        }

        private void HandleApplyIdentity()
        {
            if (flowController?.IdentityArmed == true)
            {
                flowController.TryUnlockIdentityForEditing();
                Refresh();
                return;
            }
            InteractionStudyFlowCommandResult result =
                flowController?.TryConfigureIdentity(
                participantIdInput?.text,
                buildIdentityInput?.text
            ) ?? InteractionStudyFlowCommandResult.Failure(
                "Study Flow controller is missing."
            );
            if (result.Succeeded)
            {
                SynchronizeInputsToConfiguredIdentity();
            }
            Refresh();
        }

        private void HandleIdentityDraftChanged(string _)
        {
            if (synchronizingIdentityInputs || flowController == null ||
                !flowController.IdentityArmed)
            {
                return;
            }
            if (flowController.DisarmIdentityIfDraftChanged(
                    participantIdInput?.text,
                    buildIdentityInput?.text))
            {
                // W6 still owns the last configured identity. Discard the
                // unarmed draft so displayed and effective values cannot
                // diverge.
                SynchronizeInputsToConfiguredIdentity();
                Refresh();
            }
        }

        private void SynchronizeInputsToConfiguredIdentity()
        {
            if (flowController == null)
            {
                return;
            }
            synchronizingIdentityInputs = true;
            try
            {
                participantIdInput?.SetTextWithoutNotify(
                    flowController.ConfiguredParticipantId
                );
                buildIdentityInput?.SetTextWithoutNotify(
                    flowController.ConfiguredBuildIdentity
                );
            }
            finally
            {
                synchronizingIdentityInputs = false;
            }
        }

        private void Refresh()
        {
            InteractionStudyFlowSnapshot snapshot = flowController?.Snapshot;
            bool preStart = snapshot == null ||
                snapshot.RunState == RunState.PreStart;
            bool identityArmed =
                flowController?.IdentityArmed == true;
            if (identityArmed)
            {
                SynchronizeInputsToConfiguredIdentity();
            }
            if (preStartRoot != null && preStartRoot != gameObject)
            {
                preStartRoot.SetActive(preStart);
            }
            if (startButton != null)
            {
                startButton.interactable = preStart &&
                    flowController != null && flowController.ManifestReady &&
                    identityArmed &&
                    snapshot?.CanStart == true;
            }
            bool canConfigureIdentity = preStart &&
                flowController?.CanConfigureIdentity == true &&
                !identityArmed;
            if (participantIdInput != null)
            {
                participantIdInput.interactable = canConfigureIdentity;
            }
            if (buildIdentityInput != null)
            {
                buildIdentityInput.interactable = canConfigureIdentity;
            }
            if (applyIdentityButton != null)
            {
                applyIdentityButton.interactable = preStart &&
                    flowController?.CanConfigureIdentity == true;
                TMP_Text applyLabel = applyIdentityButton
                    .GetComponentInChildren<TMP_Text>(true);
                if (applyLabel != null)
                {
                    applyLabel.text = identityArmed
                        ? "修改参与者与构建身份"
                        : "确认参与者与构建身份";
                }
            }
            if (statusLabel != null)
            {
                string flowStatus = snapshot?.Status ?? "PreStart";
                string initialization = flowController == null
                    ? "Study Flow controller is missing."
                    : flowController.InitializationStatus;
                statusLabel.text = flowController?.ManifestReady == true
                    ? identityArmed
                        ? flowStatus
                        : flowController.IdentityStatus
                    : initialization;
            }
            if (progressLabel != null)
            {
                progressLabel.text = snapshot?.PhaseId.HasValue == true
                    ? "阶段 " + snapshot.PhaseId.Value + " 进度 " +
                        snapshot.Progress + "/" + snapshot.RequiredProgress
                    : string.Empty;
            }
            StateChanged?.Invoke();
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
