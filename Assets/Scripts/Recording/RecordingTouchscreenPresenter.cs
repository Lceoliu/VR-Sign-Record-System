using TMPro;
using UnityEngine;

namespace SignVR.Recording
{
    public sealed class RecordingTouchscreenPresenter : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private RecordingReplayController replayController;

        [SerializeField]
        private RecordingTutorialController tutorialController;

        [SerializeField]
        private RecordingPassthroughController passthroughController;

        [SerializeField]
        private RecordingHelpController helpController;

        [SerializeField]
        private GameObject replayButton;

        [SerializeField]
        private TMP_Text replayLabel;

        [SerializeField]
        private GameObject tutorialButton;

        [SerializeField]
        private TMP_Text tutorialLabel;

        [SerializeField]
        private GameObject passthroughButton;

        [SerializeField]
        private TMP_Text passthroughLabel;

        [SerializeField]
        private GameObject helpButton;

        [SerializeField]
        private TMP_Text helpLabel;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingReplayController replay,
            RecordingTutorialController tutorial,
            GameObject replayControl,
            TMP_Text replayControlLabel,
            GameObject tutorialControl,
            TMP_Text tutorialControlLabel)
        {
            coordinator = recordingCoordinator;
            replayController = replay;
            tutorialController = tutorial;
            replayButton = replayControl;
            replayLabel = replayControlLabel;
            tutorialButton = tutorialControl;
            tutorialLabel = tutorialControlLabel;
        }

        public void ConfigureAssistControls(
            RecordingPassthroughController passthrough,
            RecordingHelpController help,
            GameObject passthroughControl,
            TMP_Text passthroughControlLabel,
            GameObject helpControl,
            TMP_Text helpControlLabel)
        {
            passthroughController = passthrough;
            helpController = help;
            passthroughButton = passthroughControl;
            passthroughLabel = passthroughControlLabel;
            helpButton = helpControl;
            helpLabel = helpControlLabel;
        }

        private void Update()
        {
            bool reviewing =
                coordinator.State == RecordingFlowState.Reviewing;
            bool passthrough =
                passthroughController != null &&
                passthroughController.IsPassthroughActive;
            bool idle =
                coordinator.State == RecordingFlowState.Ready ||
                coordinator.State == RecordingFlowState.Completed;
            bool controlsAvailable = (idle || reviewing) && !passthrough;

            replayButton.SetActive(controlsAvailable);
            replayLabel.text = reviewing ? "退出重播" : "重播动作";

            tutorialButton.SetActive(controlsAvailable && !reviewing);
            tutorialLabel.text = tutorialController.IsOpen
                ? "关闭教程"
                : "查看教程";

            RefreshPassthrough(idle, reviewing, passthrough);
            RefreshHelp();
        }

        private void RefreshPassthrough(bool idle, bool reviewing, bool passthrough)
        {
            if (passthroughButton == null || passthroughLabel == null)
            {
                return;
            }

            // Once in passthrough the button is the only way back, so it must stay
            // reachable even though every other control is hidden.
            passthroughButton.SetActive(passthrough || (idle && !reviewing));
            passthroughLabel.text = passthrough ? "恢复场景" : "退出场景";
        }

        private void RefreshHelp()
        {
            if (helpButton == null || helpLabel == null)
            {
                return;
            }

            // Help is reachable from every state, including while recording.
            helpButton.SetActive(true);
            helpLabel.text = coordinator.IsHelpRequested
                ? "取消呼叫"
                : "呼叫帮助";
        }
    }
}
