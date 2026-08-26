using System;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    public enum InteractionRunMode
    {
        StandaloneStudy = 0,
        EngineeringLocal = 1
    }

    public static class InteractionStudyStartPolicy
    {
        public static void Validate(
            InteractionRunMode mode,
            bool debugOverridesActive,
            bool engineeringLocalExplicitlyArmed,
            bool debugBuild)
        {
            if (!Enum.IsDefined(typeof(InteractionRunMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (mode == InteractionRunMode.StandaloneStudy)
            {
                if (debugOverridesActive)
                {
                    throw new InvalidOperationException(
                        "StandaloneStudy refuses every active debug override."
                    );
                }
                return;
            }

            if (!engineeringLocalExplicitlyArmed || !debugBuild)
            {
                throw new InvalidOperationException(
                    "EngineeringLocal requires an explicit arm flag and a debug build."
                );
            }
        }
    }

    public static class InteractionStudyCapturePrerequisites
    {
        public static void Validate(
            bool hmdReady,
            bool leftHandReady,
            bool rightHandReady,
            int objectProbeCount)
        {
            if (!hmdReady)
            {
                throw new InvalidOperationException(
                    "Study capture requires a resolved HMD transform."
                );
            }
            if (!leftHandReady || !rightHandReady)
            {
                throw new InvalidOperationException(
                    "Study capture requires resolved left and right hand data sources."
                );
            }
            if (objectProbeCount < 1)
            {
                throw new InvalidOperationException(
                    "Study capture requires at least one key-object state probe."
                );
            }
        }
    }

    public static class InteractionCaptureSetupPolicy
    {
        public static void ValidateStructure(
            int controllerCount,
            int samplerCount,
            bool referencesWired,
            InteractionRunMode runMode,
            bool debugOverridesActive)
        {
            if (controllerCount != 1 || samplerCount != 1)
            {
                throw new InvalidOperationException(
                    "Standalone Interaction structure requires exactly one " +
                    "controller and sampler."
                );
            }
            if (!referencesWired)
            {
                throw new InvalidOperationException(
                    "W6 structure references are not wired."
                );
            }
            if (runMode != InteractionRunMode.StandaloneStudy ||
                debugOverridesActive)
            {
                throw new InvalidOperationException(
                    "Standalone Interaction structure must default to " +
                    "StandaloneStudy without overrides."
                );
            }
        }

        public static void ValidateStudyReadiness(
            bool hmdReady,
            bool leftHandReady,
            bool rightHandReady,
            int objectProbeCount)
        {
            InteractionStudyCapturePrerequisites.Validate(
                hmdReady,
                leftHandReady,
                rightHandReady,
                objectProbeCount
            );
        }
    }

    public static class InteractionLifecycleTerminationPolicy
    {
        public static bool RequiresLocalAbort(RunState state)
        {
            return state == RunState.Preparing ||
                state == RunState.Scheduled ||
                state == RunState.Running ||
                state == RunState.Completing;
        }
    }

    /// <summary>
    /// Centralizes the immediate local scheduling transition used by the
    /// standalone controller.
    /// </summary>
    public static class InteractionLocalScheduleTransition
    {
        public static void Schedule(
            InteractionRunStateMachine stateMachine,
            DateTimeOffset startAtUtc)
        {
            if (stateMachine == null)
            {
                throw new ArgumentNullException(nameof(stateMachine));
            }
            stateMachine.Schedule(startAtUtc);
        }
    }

    public enum InteractionTerminalSealArbitrationState
    {
        Open,
        CheckpointPending,
        AbortReservedAfterCheckpoint,
        AbortSealReady,
        CompletedSealQueued,
        AbortedSealQueued,
        Terminal
    }

    public enum InteractionAbortRequestDisposition
    {
        QueueImmediately,
        QueueAfterCheckpoint,
        Rejected
    }

    public enum InteractionCheckpointResolution
    {
        Continue,
        QueueAbort
    }

    /// <summary>
    /// Arbitrates ownership of the one terminal capture seal. W1 remains the
    /// Run authority; this seam only decides whether local I/O may be queued.
    /// </summary>
    public sealed class InteractionTerminalSealArbiter
    {
        public InteractionTerminalSealArbitrationState State { get; private set; }

        public void BeginCheckpoint()
        {
            EnsureState(InteractionTerminalSealArbitrationState.Open);
            State = InteractionTerminalSealArbitrationState.CheckpointPending;
        }

        public void CancelCheckpoint()
        {
            EnsureState(
                InteractionTerminalSealArbitrationState.CheckpointPending
            );
            State = InteractionTerminalSealArbitrationState.Open;
        }

        public InteractionCheckpointResolution CompleteCheckpoint()
        {
            if (State == InteractionTerminalSealArbitrationState.CheckpointPending)
            {
                State = InteractionTerminalSealArbitrationState.Open;
                return InteractionCheckpointResolution.Continue;
            }
            if (State == InteractionTerminalSealArbitrationState
                    .AbortReservedAfterCheckpoint)
            {
                State = InteractionTerminalSealArbitrationState.AbortSealReady;
                return InteractionCheckpointResolution.QueueAbort;
            }
            throw new InvalidOperationException(
                "No terminal checkpoint is awaiting completion in state " +
                State + "."
            );
        }

        public InteractionAbortRequestDisposition TryRequestAbort(
            out string error)
        {
            error = null;
            if (State == InteractionTerminalSealArbitrationState.Open)
            {
                State = InteractionTerminalSealArbitrationState.AbortSealReady;
                return InteractionAbortRequestDisposition.QueueImmediately;
            }
            if (State == InteractionTerminalSealArbitrationState.CheckpointPending)
            {
                State = InteractionTerminalSealArbitrationState
                    .AbortReservedAfterCheckpoint;
                return InteractionAbortRequestDisposition.QueueAfterCheckpoint;
            }
            if (State == InteractionTerminalSealArbitrationState
                    .CompletedSealQueued)
            {
                error =
                    "Abort rejected because the Completed capture seal is already queued.";
                return InteractionAbortRequestDisposition.Rejected;
            }
            error = "Abort rejected because terminal capture ownership is " +
                State + ".";
            return InteractionAbortRequestDisposition.Rejected;
        }

        public void MarkCompletedSealQueued()
        {
            EnsureState(InteractionTerminalSealArbitrationState.Open);
            State = InteractionTerminalSealArbitrationState.CompletedSealQueued;
        }

        public void MarkAbortedSealQueued()
        {
            EnsureState(InteractionTerminalSealArbitrationState.AbortSealReady);
            State = InteractionTerminalSealArbitrationState.AbortedSealQueued;
        }

        public void MarkTerminal()
        {
            if (State != InteractionTerminalSealArbitrationState
                    .CompletedSealQueued &&
                State != InteractionTerminalSealArbitrationState
                    .AbortedSealQueued &&
                State != InteractionTerminalSealArbitrationState
                    .AbortReservedAfterCheckpoint &&
                State != InteractionTerminalSealArbitrationState.AbortSealReady)
            {
                throw new InvalidOperationException(
                    "Terminal capture cannot finish from state " + State + "."
                );
            }
            State = InteractionTerminalSealArbitrationState.Terminal;
        }

        public void Reset()
        {
            State = InteractionTerminalSealArbitrationState.Open;
        }

        private void EnsureState(
            InteractionTerminalSealArbitrationState expected)
        {
            if (State != expected)
            {
                throw new InvalidOperationException(
                    "Terminal capture expected " + expected + " but was " +
                    State + "."
                );
            }
        }
    }

    public sealed class InteractionCaptureCadence
    {
        private readonly double intervalSeconds;
        private bool armed;
        private double nextSampleMonotonic;
        private double lastObservedMonotonic;

        public InteractionCaptureCadence(double intervalSeconds)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                intervalSeconds,
                nameof(intervalSeconds)
            );
            if (intervalSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            }
            this.intervalSeconds = intervalSeconds;
            Reset();
        }

        public double IntervalSeconds => intervalSeconds;

        public bool ShouldSample(double monotonicTimeSeconds, bool captureActive)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            if (!captureActive)
            {
                Reset();
                return false;
            }
            if (!armed || monotonicTimeSeconds < lastObservedMonotonic)
            {
                armed = true;
                lastObservedMonotonic = monotonicTimeSeconds;
                nextSampleMonotonic = monotonicTimeSeconds + intervalSeconds;
                return true;
            }
            lastObservedMonotonic = monotonicTimeSeconds;
            if (monotonicTimeSeconds < nextSampleMonotonic)
            {
                return false;
            }

            double overdue = monotonicTimeSeconds - nextSampleMonotonic;
            double intervalsElapsed = Math.Floor(overdue / intervalSeconds) + 1d;
            nextSampleMonotonic += intervalsElapsed * intervalSeconds;
            if (nextSampleMonotonic <= monotonicTimeSeconds)
            {
                nextSampleMonotonic += intervalSeconds;
            }
            return true;
        }

        public void Reset()
        {
            armed = false;
            nextSampleMonotonic = 0d;
            lastObservedMonotonic = 0d;
        }
    }

}
