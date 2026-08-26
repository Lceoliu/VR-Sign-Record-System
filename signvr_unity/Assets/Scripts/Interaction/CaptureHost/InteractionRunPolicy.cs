using System;
using System.Threading;
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
            Validate(
                mode,
                debugOverridesActive,
                engineeringLocalExplicitlyArmed,
                debugBuild,
                hostRequired: false
            );
        }

        public static void Validate(
            InteractionRunMode mode,
            bool debugOverridesActive,
            bool engineeringLocalExplicitlyArmed,
            bool debugBuild,
            bool hostRequired)
        {
            if (!Enum.IsDefined(typeof(InteractionRunMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (hostRequired)
            {
                throw new InvalidOperationException(
                    "Standalone Interaction modes refuse Host-required configuration."
                );
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
            int hostClientCount,
            int controllerCount,
            int samplerCount,
            bool referencesWired,
            InteractionRunMode runMode,
            bool debugOverridesActive,
            bool requireHostForStart)
        {
            if (hostClientCount != 0 || controllerCount != 1 ||
                samplerCount != 1)
            {
                throw new InvalidOperationException(
                    "Standalone Interaction structure requires no Host client " +
                    "and exactly one controller and sampler."
                );
            }
            if (!referencesWired)
            {
                throw new InvalidOperationException(
                    "W6 structure references are not wired."
                );
            }
            if (runMode != InteractionRunMode.StandaloneStudy ||
                debugOverridesActive || requireHostForStart)
            {
                throw new InvalidOperationException(
                    "Standalone Interaction structure must default to " +
                    "StandaloneStudy without overrides or Host."
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

    public interface IInteractionHeartbeatGenerationStore
    {
        bool TryRead(out long generation);
        void WriteAndFlush(long generation);
    }

    public static class InteractionHeartbeatGenerationAllocator
    {
        public static long AllocateNext(
            IInteractionHeartbeatGenerationStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            long previous;
            long next;
            if (!store.TryRead(out previous))
            {
                next = 0L;
            }
            else
            {
                if (previous < 0L)
                {
                    throw new InvalidOperationException(
                        "Persisted heartbeat generation is invalid."
                    );
                }
                next = checked(previous + 1L);
            }
            store.WriteAndFlush(next);
            return next;
        }
    }

    public sealed class InteractionHeartbeatDeadlineSchedule
    {
        private readonly double intervalSeconds;
        private bool initialized;

        public InteractionHeartbeatDeadlineSchedule(double intervalSeconds)
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
        }

        public double NextDeadlineSeconds { get; private set; }

        public void Reset(double monotonicNowSeconds)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicNowSeconds,
                nameof(monotonicNowSeconds)
            );
            NextDeadlineSeconds = monotonicNowSeconds;
            initialized = true;
        }

        public void AdvanceAfterAttempt(double completedAtSeconds)
        {
            EnsureInitialized();
            InteractionEventSequencer.ValidateFiniteNonNegative(
                completedAtSeconds,
                nameof(completedAtSeconds)
            );
            do
            {
                NextDeadlineSeconds += intervalSeconds;
            }
            while (NextDeadlineSeconds <= completedAtSeconds);
        }

        public double DelaySeconds(double monotonicNowSeconds)
        {
            EnsureInitialized();
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicNowSeconds,
                nameof(monotonicNowSeconds)
            );
            return Math.Max(0d, NextDeadlineSeconds - monotonicNowSeconds);
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "Heartbeat deadline schedule is not initialized."
                );
            }
        }
    }

    public static class InteractionLifecycleTerminationPolicy
    {
        public static bool RequiresLocalAbort(RunState state)
        {
            return state == RunState.AwaitingHost ||
                state == RunState.Scheduled ||
                state == RunState.Running ||
                state == RunState.Completing;
        }
    }

    /// <summary>
    /// Keeps the standalone controller independent from the legacy transport
    /// vocabulary still present in the W1 state machine transition name.
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
            stateMachine.HostScheduled(startAtUtc);
        }
    }

    public static class InteractionHeartbeatLifecyclePolicy
    {
        public static bool ShouldRestoreAfterResume(RunState state)
        {
            return state == RunState.PreStart;
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

    /// <summary>
    /// Accepts only the newest issued asynchronous response. Once a newer
    /// request exists, an older callback cannot refresh data or freshness.
    /// </summary>
    public sealed class InteractionLatestResponseGate
    {
        private long latestIssued;
        private long latestAccepted;

        public long LatestIssued => latestIssued;
        public long LatestAccepted => latestAccepted;

        public long Issue()
        {
            return latestIssued = checked(latestIssued + 1L);
        }

        public bool TryAccept(long generation)
        {
            if (generation <= 0L || generation != latestIssued ||
                generation <= latestAccepted)
            {
                return false;
            }
            latestAccepted = generation;
            return true;
        }
    }

    public sealed class InteractionHeartbeatLoopState
    {
        private long sequence;

        public bool RoutineActive { get; private set; }
        public long Generation { get; private set; } = -1L;
        public long LastSequence => sequence;

        public bool TryStart(long generation)
        {
            if (generation < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }
            if (RoutineActive)
            {
                return false;
            }
            if (Generation != generation)
            {
                Generation = generation;
                sequence = 0L;
            }
            RoutineActive = true;
            return true;
        }

        public bool Stop()
        {
            if (!RoutineActive)
            {
                return false;
            }
            RoutineActive = false;
            return true;
        }

        public long NextSequence()
        {
            if (!RoutineActive)
            {
                throw new InvalidOperationException(
                    "Heartbeat sequence requires the unique active routine."
                );
            }
            return sequence = checked(sequence + 1L);
        }
    }

    public static class InteractionRegistrationRecoveryPolicy
    {
        public static bool CanRecover(
            InteractionHostRunSnapshot snapshot,
            string expectedBatchId,
            string expectedParticipantId,
            string expectedRunId)
        {
            if (snapshot == null)
            {
                return false;
            }
            string batch;
            string participant;
            string run;
            try
            {
                batch = InteractionStoragePaths.ValidateSegment(
                    expectedBatchId,
                    nameof(expectedBatchId)
                );
                participant = InteractionStoragePaths.ValidateSegment(
                    expectedParticipantId,
                    nameof(expectedParticipantId)
                );
                run = InteractionStoragePaths.ValidateSegment(
                    expectedRunId,
                    nameof(expectedRunId)
                );
            }
            catch (ArgumentException)
            {
                return false;
            }
            return string.Equals(snapshot.BatchId, batch, StringComparison.Ordinal) &&
                string.Equals(
                    snapshot.ParticipantId,
                    participant,
                    StringComparison.Ordinal
                ) &&
                string.Equals(snapshot.RunId, run, StringComparison.Ordinal);
        }
    }

    public sealed class InteractionFrozenRunRegistration
    {
        private readonly byte[] manifestBytes;

        public InteractionFrozenRunRegistration(
            RunPlan plan,
            byte[] manifestBytes)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (manifestBytes == null || manifestBytes.Length == 0)
            {
                throw new ArgumentException(
                    "Manifest bytes are required.",
                    nameof(manifestBytes)
                );
            }
            byte[] expected = InteractionRunManifestContractV1.SerializeUtf8(plan);
            if (!InteractionCaptureWriter.ByteArraysEqual(
                    expected,
                    manifestBytes))
            {
                throw new ArgumentException(
                    "Manifest bytes do not match the frozen RunPlan.",
                    nameof(manifestBytes)
                );
            }
            this.manifestBytes = (byte[])manifestBytes.Clone();
        }

        public RunPlan Plan { get; }
        public int AttemptCount { get; private set; }
        public long LastHttpStatusCode { get; private set; }
        public bool Accepted { get; private set; }

        public byte[] BeginAttempt()
        {
            AttemptCount = checked(AttemptCount + 1);
            return (byte[])manifestBytes.Clone();
        }

        public void RecordResponse(long httpStatusCode, bool accepted)
        {
            LastHttpStatusCode = httpStatusCode;
            Accepted = accepted && httpStatusCode >= 200L &&
                httpStatusCode <= 299L;
            // 409 and every transport failure intentionally leave Plan and
            // bytes untouched so a later retry is byte-identical.
        }

        public void RecordRecoveredConflict()
        {
            if (LastHttpStatusCode != 409L)
            {
                throw new InvalidOperationException(
                    "Only a 409 followed by an exact Run snapshot can recover registration."
                );
            }
            Accepted = true;
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

    internal readonly struct InteractionHostRequestLease
    {
        public InteractionHostRequestLease(long generation)
        {
            Generation = generation;
        }

        public long Generation { get; }
    }

    internal sealed class InteractionHostRequestEpoch
    {
        private readonly object gate = new object();
        private long generation = 1L;
        private bool accepting = true;

        public bool IsAccepting
        {
            get
            {
                lock (gate)
                {
                    return accepting;
                }
            }
        }

        public bool TryAcquire(out InteractionHostRequestLease lease)
        {
            lock (gate)
            {
                lease = new InteractionHostRequestLease(generation);
                return accepting;
            }
        }

        public bool IsCurrent(InteractionHostRequestLease lease)
        {
            lock (gate)
            {
                return accepting && lease.Generation == generation;
            }
        }

        public bool TryExecute(
            InteractionHostRequestLease lease,
            Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            lock (gate)
            {
                if (!accepting || lease.Generation != generation)
                {
                    return false;
                }
                action();
                return true;
            }
        }

        public void CloseAndAdvance()
        {
            lock (gate)
            {
                accepting = false;
                generation = checked(generation + 1L);
            }
        }

        public void Open()
        {
            lock (gate)
            {
                accepting = true;
            }
        }
    }

    internal sealed class InteractionOncePublisher<T>
    {
        private readonly Action<T> publish;
        private int published;

        public InteractionOncePublisher(Action<T> publish)
        {
            this.publish = publish ??
                throw new ArgumentNullException(nameof(publish));
        }

        public bool TryPublish(T value)
        {
            if (Interlocked.Exchange(ref published, 1) != 0)
            {
                return false;
            }
            publish(value);
            return true;
        }
    }
}
