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
        private RecordingSentenceSequence sentenceSequence;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Development simulation only. The Python host will own the production hold threshold.")]
        private float simulatedHoldSeconds = 1.2f;

        private bool isPressed;
        private bool resetTriggered;
        private double pressedAt;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingReplayController replay = null,
            RecordingSentenceSequence sequence = null)
        {
            coordinator = recordingCoordinator;
            replayController = replay;
            sentenceSequence = sequence;
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

            if (coordinator.State == RecordingFlowState.Ready ||
                coordinator.State == RecordingFlowState.Completed)
            {
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
                {
                    sentenceSequence?.TryMoveNext();
                    return;
                }
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
                {
                    sentenceSequence?.TryMovePrevious();
                    return;
                }
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
                case RecordingFlowState.Countdown:
                    coordinator.StopCurrentTake();
                    break;
                case RecordingFlowState.Recording:
                    coordinator.StopCurrentTake();
                    break;
                case RecordingFlowState.Reviewing:
                    coordinator.StopCurrentTake();
                    break;
                case RecordingFlowState.Completed:
                    // TakeCompleted owns sequence advancement. Starting the
                    // already-loaded prompt is safe, while the final sentence
                    // remains guarded by IsSequenceCompleted.
                    if (sentenceSequence == null ||
                        sentenceSequence.IsSequenceCompleted)
                    {
                        break;
                    }
                    coordinator.BeginCurrentTake();
                    break;
            }
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
