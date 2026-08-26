using System;
using System.IO;

namespace SignVR.Interaction.CaptureHost
{
    public enum InteractionStandaloneLocalRunRecoveryStatus
    {
        NotStarted = 0,
        Recovering = 1,
        Succeeded = 2,
        Failed = 3
    }

    /// <summary>
    /// Pollable startup coordinator for Standalone Study Mode. Its injected
    /// root is the parent of interaction-tests, so tests and future Controller
    /// integration never need to touch Application.persistentDataPath.
    /// </summary>
    public sealed class InteractionStandaloneLocalRunRecoveryCoordinator
    {
        private readonly string persistentDataPath;
        private InteractionBackgroundOperation<int> operation;

        public InteractionStandaloneLocalRunRecoveryCoordinator(
            string persistentDataPath)
        {
            InteractionStoragePaths.GetInteractionRoot(persistentDataPath);
            this.persistentDataPath = Path.GetFullPath(persistentDataPath);
            Status = InteractionStandaloneLocalRunRecoveryStatus.NotStarted;
            FailureReason = string.Empty;
        }

        public InteractionStandaloneLocalRunRecoveryStatus Status { get; private set; }
        public bool IsRecovering =>
            Status == InteractionStandaloneLocalRunRecoveryStatus.Recovering;
        public int RecoveredRunCount { get; private set; }
        public string FailureReason { get; private set; }

        public void Begin(
            string abortReason,
            DateTimeOffset endedUtc,
            double monotonicTimeSeconds,
            int frame)
        {
            if (IsRecovering)
            {
                throw new InvalidOperationException(
                    "Standalone local Run recovery is already in progress."
                );
            }
            if (string.IsNullOrWhiteSpace(abortReason))
            {
                throw new ArgumentException(
                    "Standalone local Run recovery requires an abort reason.",
                    nameof(abortReason)
                );
            }
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            if (frame < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }

            string frozenReason = abortReason.Trim();
            RecoveredRunCount = 0;
            FailureReason = string.Empty;
            Status = InteractionStandaloneLocalRunRecoveryStatus.Recovering;
            operation = InteractionBackgroundOperation<int>.Start(() =>
                InteractionPartialRunRecovery.TerminalizeAllAbortedQuestLocal(
                    persistentDataPath,
                    frozenReason,
                    endedUtc,
                    monotonicTimeSeconds,
                    frame
                )
            );
        }

        /// <summary>
        /// Polls without blocking. Returns true once a Recovering operation has
        /// published either Succeeded or Failed.
        /// </summary>
        public bool TryComplete()
        {
            if (!IsRecovering || operation == null || !operation.IsCompleted)
            {
                return false;
            }

            if (operation.Succeeded)
            {
                RecoveredRunCount = operation.GetResult();
                FailureReason = string.Empty;
                Status = InteractionStandaloneLocalRunRecoveryStatus.Succeeded;
            }
            else
            {
                RecoveredRunCount = 0;
                FailureReason = DescribeFailure(operation.Error);
                Status = InteractionStandaloneLocalRunRecoveryStatus.Failed;
            }
            return true;
        }

        /// <summary>
        /// Deterministic test helper. Runtime callers should poll TryComplete
        /// from their normal update/coroutine path.
        /// </summary>
        public bool Wait(TimeSpan timeout)
        {
            if (!IsRecovering || operation == null)
            {
                return Status ==
                        InteractionStandaloneLocalRunRecoveryStatus.Succeeded ||
                    Status == InteractionStandaloneLocalRunRecoveryStatus.Failed;
            }
            if (!operation.Wait(timeout))
            {
                return false;
            }
            TryComplete();
            return true;
        }

        private static string DescribeFailure(Exception failure)
        {
            if (failure == null)
            {
                return "Standalone local Run recovery failed without an error.";
            }
            return string.IsNullOrWhiteSpace(failure.Message)
                ? failure.GetType().Name
                : failure.Message;
        }
    }
}
