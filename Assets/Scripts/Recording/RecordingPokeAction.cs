using Oculus.Interaction;
using UnityEngine;

namespace SignVR.Recording
{
    public sealed class RecordingPokeAction : MonoBehaviour
    {
        public enum ActionType
        {
            ReplayLastTake,
            ToggleReplayPause,
            StopReplay,
            ResetPromptBoard,
            StartTutorial,
            NextTutorialStep,
            CloseTutorial,
            ToggleReplay,
            ToggleTutorial
        }

        [SerializeField]
        private PointableUnityEventWrapper eventWrapper;

        [SerializeField]
        private ActionType action;

        [SerializeField]
        private RecordingReplayController replayController;

        [SerializeField]
        private RecordingPromptBoard promptBoard;

        [SerializeField]
        private RecordingTutorialController tutorialController;

        public void Configure(
            PointableUnityEventWrapper wrapper,
            ActionType actionType,
            RecordingReplayController replay,
            RecordingPromptBoard board,
            RecordingTutorialController tutorial)
        {
            eventWrapper = wrapper;
            action = actionType;
            replayController = replay;
            promptBoard = board;
            tutorialController = tutorial;
        }

        private void Awake()
        {
            if (eventWrapper == null)
            {
                eventWrapper = GetComponentInChildren<PointableUnityEventWrapper>(true);
            }

            if (eventWrapper == null)
            {
                Debug.LogError(
                    $"[RecordingPokeAction] {name} has no PointableUnityEventWrapper."
                );
                enabled = false;
                return;
            }

            eventWrapper.WhenSelect.AddListener(HandleSelect);
        }

        private void HandleSelect(PointerEvent _)
        {
            switch (action)
            {
                case ActionType.ReplayLastTake:
                    replayController.PlayLastTake();
                    break;
                case ActionType.ToggleReplayPause:
                    replayController.TogglePause();
                    break;
                case ActionType.StopReplay:
                    replayController.StopReview();
                    break;
                case ActionType.ResetPromptBoard:
                    promptBoard.ResetPlacement();
                    break;
                case ActionType.StartTutorial:
                    tutorialController.StartTutorial();
                    break;
                case ActionType.NextTutorialStep:
                    tutorialController.NextStep();
                    break;
                case ActionType.CloseTutorial:
                    tutorialController.CloseTutorial();
                    break;
                case ActionType.ToggleReplay:
                    if (replayController.IsReviewing || replayController.IsLoading)
                    {
                        replayController.StopReview();
                    }
                    else
                    {
                        replayController.PlayLastTake();
                    }
                    break;
                case ActionType.ToggleTutorial:
                    tutorialController.ToggleTutorial();
                    break;
            }
        }

        private void OnDestroy()
        {
            if (eventWrapper != null)
            {
                eventWrapper.WhenSelect.RemoveListener(HandleSelect);
            }
        }
    }
}
