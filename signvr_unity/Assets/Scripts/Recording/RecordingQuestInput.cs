using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Quest controller shortcuts for the local recording workflow. A starts
    /// and stops a take, holding A resets it, B loads the next sentence, and X
    /// loads the previous sentence. Host commands use the same coordinator.
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
        private OVRInput.Button nextSentenceButton = OVRInput.Button.Two;

        [SerializeField]
        private OVRInput.Controller nextSentenceController =
            OVRInput.Controller.RTouch;

        [SerializeField]
        private OVRInput.Button previousSentenceButton = OVRInput.Button.Three;

        [SerializeField]
        private OVRInput.Controller previousSentenceController =
            OVRInput.Controller.LTouch;

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

            if (CanSwitchSentence())
            {
                if (OVRInput.GetDown(
                        nextSentenceButton,
                        nextSentenceController
                    ))
                {
                    sentenceSequence.TryMoveNext();
                }
                else if (OVRInput.GetDown(
                             previousSentenceButton,
                             previousSentenceController
                         ))
                {
                    sentenceSequence.TryMovePrevious();
                }
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

        private bool CanSwitchSentence()
        {
            return sentenceSequence != null &&
                   (coordinator.State == RecordingFlowState.Ready ||
                    coordinator.State == RecordingFlowState.Completed);
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
