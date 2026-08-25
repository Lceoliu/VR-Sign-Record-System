using System;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    public enum InteractionRunMode
    {
        Study,
        EngineeringLocal
    }

    public static class InteractionStudyStartPolicy
    {
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
            if (mode == InteractionRunMode.Study)
            {
                if (debugOverridesActive)
                {
                    throw new InvalidOperationException(
                        "Study mode refuses every active debug override."
                    );
                }
                if (!hostRequired)
                {
                    throw new InvalidOperationException(
                        "Study mode always requires the Interaction Host."
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
}
