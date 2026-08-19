using TMPro;
using UnityEngine;

namespace SignVR.Recording
{
    public sealed class RecordingToolPanelPresenter : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private RecordingReplayController replayController;

        [SerializeField]
        private GameObject defaultControls;

        [SerializeField]
        private GameObject replayControls;

        [SerializeField]
        private GameObject replayButton;

        [SerializeField]
        private TMP_Text replayPauseLabel;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingReplayController replay,
            GameObject normalControls,
            GameObject reviewControls,
            GameObject replayTakeButton,
            TMP_Text pauseLabel)
        {
            coordinator = recordingCoordinator;
            replayController = replay;
            defaultControls = normalControls;
            replayControls = reviewControls;
            replayButton = replayTakeButton;
            replayPauseLabel = pauseLabel;
        }

        private void Update()
        {
            if (coordinator == null)
            {
                return;
            }

            bool reviewing =
                coordinator.State == RecordingFlowState.Reviewing;
            bool showDefault =
                coordinator.State == RecordingFlowState.Ready ||
                coordinator.State == RecordingFlowState.Completed;

            defaultControls.SetActive(showDefault);
            replayControls.SetActive(reviewing);
            replayButton.SetActive(
                showDefault && coordinator.HasLastArtifact
            );

            if (reviewing && replayPauseLabel != null)
            {
                replayPauseLabel.text = replayController.IsComplete
                    ? "重播"
                    : replayController.IsPaused
                        ? "继续"
                        : "暂停";
            }
        }
    }
}
