using UnityEngine;
using UnityEngine.InputSystem;

namespace SignVR.Recording
{
    public sealed class RecordingDebugInput : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private RecordingReplayController replayController;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Development simulation only. The Python host will own the production hold threshold.")]
        private float simulatedHoldSeconds = 1.2f;

        [SerializeField]
        private string debugSessionId = "editor-session";

        [SerializeField]
        [TextArea(2, 4)]
        private string[] debugPrompts =
        {
            "Sample sentence 001",
            "Sample sentence 002"
        };

        private bool isPressed;
        private bool resetTriggered;
        private double pressedAt;
        private int promptIndex;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingReplayController replay = null)
        {
            coordinator = recordingCoordinator;
            replayController = replay;
        }

        private void Awake()
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            enabled = false;
#endif
            if (replayController == null)
            {
                replayController = GetComponent<RecordingReplayController>();
            }
        }

        private void Update()
        {
            if (coordinator == null || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                if (replayController == null)
                {
                    Debug.LogWarning(
                        "[RecordingDebugInput] Replay is not configured for this scene."
                    );
                    return;
                }

                if (replayController.IsReviewing || replayController.IsLoading)
                {
                    replayController.StopReview();
                }
                else
                {
                    replayController.PlayLastTake();
                }
                return;
            }

            var key = Keyboard.current.spaceKey;

            if (key.wasPressedThisFrame)
            {
                isPressed = true;
                resetTriggered = false;
                pressedAt = Time.unscaledTimeAsDouble;
            }

            if (isPressed && key.isPressed && !resetTriggered)
            {
                float progress = (float)(
                    (Time.unscaledTimeAsDouble - pressedAt) /
                    simulatedHoldSeconds
                );

                coordinator.SetResetHoldProgress(progress);

                if (progress >= 1f)
                {
                    resetTriggered = true;
                    coordinator.CompleteResetHold();
                }
            }

            if (!key.wasReleasedThisFrame)
            {
                return;
            }

            isPressed = false;

            if (resetTriggered)
            {
                return;
            }

            coordinator.CancelResetHold();
            HandleShortPress();
        }

        private void HandleShortPress()
        {
            switch (coordinator.State)
            {
                case RecordingFlowState.Ready:
                    coordinator.BeginCurrentTake();
                    break;
                case RecordingFlowState.Recording:
                    coordinator.StopCurrentTake();
                    break;
                case RecordingFlowState.Reviewing:
                    coordinator.StopCurrentTake();
                    break;
                case RecordingFlowState.Completed:
                    LoadNextDebugPromptAndBegin();
                    break;
            }
        }

        private void LoadNextDebugPromptAndBegin()
        {
            if (debugPrompts == null || debugPrompts.Length == 0)
            {
                Debug.LogWarning("[RecordingDebugInput] No debug prompts are configured.");
                return;
            }

            promptIndex = (promptIndex + 1) % debugPrompts.Length;
            coordinator.LoadPrompt(
                debugSessionId,
                $"debug-{promptIndex + 1:D3}",
                debugPrompts[promptIndex],
                1
            );
            coordinator.BeginCurrentTake();
        }

        private void OnDisable()
        {
            if (coordinator != null)
            {
                coordinator.CancelResetHold();
            }
        }
    }
}
