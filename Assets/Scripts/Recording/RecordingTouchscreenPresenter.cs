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
        private GameObject replayButton;

        [SerializeField]
        private TMP_Text replayLabel;

        [SerializeField]
        private GameObject tutorialButton;

        [SerializeField]
        private TMP_Text tutorialLabel;

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

        private void Update()
        {
            bool reviewing =
                coordinator.State == RecordingFlowState.Reviewing;
            bool controlsAvailable =
                coordinator.State == RecordingFlowState.Ready ||
                coordinator.State == RecordingFlowState.Completed ||
                reviewing;

            replayButton.SetActive(controlsAvailable);
            replayLabel.text = reviewing ? "退出重播" : "重播动作";

            tutorialButton.SetActive(controlsAvailable && !reviewing);
            tutorialLabel.text = tutorialController.IsOpen
                ? "关闭教程"
                : "查看教程";
        }
    }
}
