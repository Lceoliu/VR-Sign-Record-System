using System;

namespace SignVR.Recording
{
    public sealed class RecordingFlowStateMachine
    {
        public RecordingFlowState State { get; private set; } =
            RecordingFlowState.Disconnected;

        public bool HasPrompt { get; private set; }

        public event Action<RecordingFlowState> StateChanged;

        public bool LoadPrompt()
        {
            if (
                State != RecordingFlowState.Disconnected &&
                State != RecordingFlowState.Ready &&
                State != RecordingFlowState.Completed
            )
            {
                return false;
            }

            HasPrompt = true;
            ChangeState(RecordingFlowState.Ready);
            return true;
        }

        public bool BeginCountdown()
        {
            return Transition(
                RecordingFlowState.Ready,
                RecordingFlowState.Countdown
            );
        }

        public bool BeginRecording()
        {
            return Transition(
                RecordingFlowState.Countdown,
                RecordingFlowState.Recording
            );
        }

        public bool CancelCountdown()
        {
            return Transition(
                RecordingFlowState.Countdown,
                RecordingFlowState.Ready
            );
        }

        public bool BeginFinalizing()
        {
            return Transition(
                RecordingFlowState.Recording,
                RecordingFlowState.Finalizing
            );
        }

        public bool InterruptRecording()
        {
            return Transition(
                RecordingFlowState.Recording,
                RecordingFlowState.Ready
            );
        }

        public bool CompleteFinalizing()
        {
            return Transition(
                RecordingFlowState.Finalizing,
                RecordingFlowState.Completed
            );
        }

        public bool AbortFinalizing()
        {
            return Transition(
                RecordingFlowState.Finalizing,
                RecordingFlowState.Ready
            );
        }

        public bool BeginReview()
        {
            if (
                State != RecordingFlowState.Ready &&
                State != RecordingFlowState.Completed
            )
            {
                return false;
            }

            ChangeState(RecordingFlowState.Reviewing);
            return true;
        }

        public bool CompleteReview(bool returnToCompleted)
        {
            return Transition(
                RecordingFlowState.Reviewing,
                returnToCompleted
                    ? RecordingFlowState.Completed
                    : RecordingFlowState.Ready
            );
        }

        public void BeginReset()
        {
            ChangeState(RecordingFlowState.Resetting);
        }

        public void CompleteReset()
        {
            ChangeState(
                HasPrompt
                    ? RecordingFlowState.Ready
                    : RecordingFlowState.Disconnected
            );
        }

        public void Fail()
        {
            ChangeState(RecordingFlowState.Error);
        }

        public void Disconnect()
        {
            HasPrompt = false;
            ChangeState(RecordingFlowState.Disconnected);
        }

        private bool Transition(
            RecordingFlowState expected,
            RecordingFlowState next)
        {
            if (State != expected)
            {
                return false;
            }

            ChangeState(next);
            return true;
        }

        private void ChangeState(RecordingFlowState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
