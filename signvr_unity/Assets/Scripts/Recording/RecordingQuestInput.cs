using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Single-button headset control: tap A to start/stop and hold A to reset
    /// the current sentence. Host pedal commands continue to use the same
    /// coordinator methods, so both control paths produce identical states.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingQuestInput : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private RecordingSentenceSequence sentenceSequence;

        [SerializeField]
        private OVRInput.Button toggleButton = OVRInput.Button.One;

        [SerializeField]
        private OVRInput.Controller controller = OVRInput.Controller.RTouch;

        [SerializeField]
        [Min(0.5f)]
        private float resetHoldSeconds = 1.2f;

        private bool isPressed;
        private bool resetTriggered;
        private double pressedAt;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingSentenceSequence sequence = null)
        {
            coordinator = recordingCoordinator;
            sentenceSequence = sequence;
        }

        private void Update()
        {
            if (coordinator == null)
            {
                return;
            }

            if (OVRInput.GetDown(toggleButton, controller))
            {
                isPressed = true;
                resetTriggered = false;
                pressedAt = Time.unscaledTimeAsDouble;
                coordinator.PulsePedal();
            }

            if (isPressed &&
                OVRInput.Get(toggleButton, controller) &&
                !resetTriggered)
            {
                float progress = (float)(
                    (Time.unscaledTimeAsDouble - pressedAt) /
                    Mathf.Max(0.5f, resetHoldSeconds)
                );
                coordinator.SetResetHoldProgress(progress);
                if (progress >= 1f)
                {
                    resetTriggered = true;
                    coordinator.CompleteResetHold();
                }
            }

            if (!OVRInput.GetUp(toggleButton, controller))
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
                case RecordingFlowState.Reviewing:
                    coordinator.StopCurrentTake();
                    break;
                case RecordingFlowState.Completed:
                    // TakeCompleted owns sequence advancement. If the next
                    // prompt is already loaded, a short-lived Completed state
                    // may still accept the start action; the final sentence
                    // is explicitly guarded.
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
            isPressed = false;
            resetTriggered = false;
            coordinator?.CancelResetHold();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            resetHoldSeconds = Mathf.Max(0.5f, resetHoldSeconds);
        }
#endif
    }
}
