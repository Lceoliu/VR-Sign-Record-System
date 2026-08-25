using System;

namespace SignVR.Interaction.Presentation
{
    public enum InstructionGhostPlaybackStatus
    {
        Empty,
        Loaded,
        Playing,
        Completed,
        Stopped,
        Failed
    }

    public enum InstructionPlaybackPass
    {
        None,
        First,
        Replay
    }

    /// <summary>
    /// Pure lifecycle guard used by InstructionGhostPlayer. It freezes one
    /// artifact key for first play and replay and makes a third pass impossible.
    /// </summary>
    public sealed class InstructionGhostPlaybackState
    {
        public event Action Changed;

        public string ArtifactKey { get; private set; } = string.Empty;

        public InstructionGhostPlaybackStatus Status { get; private set; } =
            InstructionGhostPlaybackStatus.Empty;

        public InstructionPlaybackPass CurrentPass { get; private set; } =
            InstructionPlaybackPass.None;

        public InstructionPlaybackPass LastCompletedPass { get; private set; } =
            InstructionPlaybackPass.None;

        public bool FirstPlaybackCompleted { get; private set; }

        public bool ReplayConsumed { get; private set; }

        public bool ReplayPlaybackCompleted { get; private set; }

        public void Load(string artifactKey)
        {
            if (string.IsNullOrWhiteSpace(artifactKey))
            {
                throw new ArgumentException(
                    "A frozen artifact key is required.",
                    nameof(artifactKey)
                );
            }

            ArtifactKey = artifactKey.Trim();
            Status = InstructionGhostPlaybackStatus.Loaded;
            CurrentPass = InstructionPlaybackPass.None;
            LastCompletedPass = InstructionPlaybackPass.None;
            FirstPlaybackCompleted = false;
            ReplayConsumed = false;
            ReplayPlaybackCompleted = false;
            Changed?.Invoke();
        }

        public bool Play()
        {
            if (Status != InstructionGhostPlaybackStatus.Loaded ||
                FirstPlaybackCompleted)
            {
                return false;
            }

            Status = InstructionGhostPlaybackStatus.Playing;
            CurrentPass = InstructionPlaybackPass.First;
            Changed?.Invoke();
            return true;
        }

        public bool Replay()
        {
            if (Status != InstructionGhostPlaybackStatus.Completed ||
                !FirstPlaybackCompleted || ReplayConsumed ||
                ReplayPlaybackCompleted)
            {
                return false;
            }

            // Consumption precedes any downstream retargeting operation and is
            // intentionally never rolled back.
            ReplayConsumed = true;
            Status = InstructionGhostPlaybackStatus.Playing;
            CurrentPass = InstructionPlaybackPass.Replay;
            Changed?.Invoke();
            return true;
        }

        public bool Complete()
        {
            if (Status != InstructionGhostPlaybackStatus.Playing ||
                CurrentPass == InstructionPlaybackPass.None)
            {
                return false;
            }

            LastCompletedPass = CurrentPass;
            if (CurrentPass == InstructionPlaybackPass.First)
            {
                FirstPlaybackCompleted = true;
            }
            else
            {
                ReplayPlaybackCompleted = true;
            }

            CurrentPass = InstructionPlaybackPass.None;
            Status = InstructionGhostPlaybackStatus.Completed;
            Changed?.Invoke();
            return true;
        }

        public void Stop()
        {
            CurrentPass = InstructionPlaybackPass.None;
            Status = InstructionGhostPlaybackStatus.Stopped;
            Changed?.Invoke();
        }

        public void Fail()
        {
            CurrentPass = InstructionPlaybackPass.None;
            Status = InstructionGhostPlaybackStatus.Failed;
            Changed?.Invoke();
        }
    }
}
