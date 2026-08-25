using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    public static class InteractionEventNames
    {
        public const string RunCreated = "run_created";
        public const string HostReady = "host_ready";
        public const string RunStarted = "run_started";
        public const string RunCompleted = "run_completed";
        public const string RunAborted = "run_aborted";
        public const string PhaseEntered = "phase_entered";
        public const string PhaseCompleted = "phase_completed";
        public const string PhaseStuck = "phase_stuck";
        public const string PhaseTimeout = "phase_timeout";
        public const string InstructionPlayStarted = "instruction_play_started";
        public const string InstructionPlayCompleted = "instruction_play_completed";
        public const string ReplayAvailable = "replay_available";
        public const string ReplayUsed = "replay_used";
        public const string BubbleShown = "bubble_shown";
        public const string BubbleHidden = "bubble_hidden";
        public const string PointingHitStarted = "pointing_hit_started";
        public const string PointingHitEnded = "pointing_hit_ended";
        public const string InteractionAttempt = "interaction_attempt";
        public const string InteractionError = "interaction_error";
        public const string TaskProgressReset = "task_progress_reset";
        public const string CaptureGap = "capture_gap";
        public const string UploadStarted = "upload_started";
        public const string UploadAcknowledged = "upload_acknowledged";

        private static readonly ReadOnlyCollection<string> required =
            new List<string>
            {
                RunCreated, HostReady, RunStarted, RunCompleted, RunAborted,
                PhaseEntered, PhaseCompleted, PhaseStuck, PhaseTimeout,
                InstructionPlayStarted, InstructionPlayCompleted,
                ReplayAvailable, ReplayUsed, BubbleShown, BubbleHidden,
                PointingHitStarted, PointingHitEnded, InteractionAttempt,
                InteractionError, TaskProgressReset, CaptureGap,
                UploadStarted, UploadAcknowledged
            }.AsReadOnly();

        public static IReadOnlyList<string> Required => required;

        public static string Validate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Event type is required.",
                    nameof(value)
                );
            }

            string normalized = value.Trim();
            if (normalized.Length > 80 || normalized[0] < 'a' ||
                normalized[0] > 'z')
            {
                throw new ArgumentException(
                    "Event types must be 1-80 lower snake_case characters " +
                    "starting with a letter.",
                    nameof(value)
                );
            }
            for (int index = 0; index < normalized.Length; index++)
            {
                char character = normalized[index];
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '_'))
                {
                    throw new ArgumentException(
                        "Event types must use lower snake_case.",
                        nameof(value)
                    );
                }
            }

            return normalized;
        }
    }

    public sealed class InteractionEventRecord
    {
        internal InteractionEventRecord(
            string runId,
            int? phaseId,
            long eventSequence,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string eventType,
            string actorId,
            string targetId,
            string payloadJson)
        {
            SchemaVersion = 1;
            RunId = runId;
            PhaseId = phaseId;
            EventSequence = eventSequence;
            MonotonicTimeSeconds = monotonicTimeSeconds;
            UtcTime = utcTime.ToUniversalTime();
            Frame = frame;
            EventType = eventType;
            ActorId = actorId;
            TargetId = targetId;
            PayloadJson = payloadJson;
        }

        public int SchemaVersion { get; }
        public string RunId { get; }
        public int? PhaseId { get; }
        public long EventSequence { get; }
        public double MonotonicTimeSeconds { get; }
        public DateTimeOffset UtcTime { get; }
        public int Frame { get; }
        public string EventType { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string PayloadJson { get; }
    }

    public sealed class InteractionEventSequencer
    {
        private readonly string runId;
        private long nextSequence;
        private double lastMonotonicTime = -1d;
        private int lastFrame = -1;

        public InteractionEventSequencer(string runId)
            : this(runId, 1L, -1d, -1)
        {
        }

        internal InteractionEventSequencer(
            string runId,
            long nextSequence,
            double lastMonotonicTime,
            int lastFrame)
        {
            this.runId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            if (nextSequence < 1L)
            {
                throw new ArgumentOutOfRangeException(nameof(nextSequence));
            }
            this.nextSequence = nextSequence;
            this.lastMonotonicTime = lastMonotonicTime;
            this.lastFrame = lastFrame;
        }

        public long NextSequence => nextSequence;
        public double LastMonotonicTimeSeconds => lastMonotonicTime;
        public int LastFrame => lastFrame;

        public InteractionEventRecord Create(
            string eventType,
            int? phaseId,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string actorId = null,
            string targetId = null,
            string payloadJson = null)
        {
            ValidatePhase(phaseId);
            ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            if (monotonicTimeSeconds < lastMonotonicTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTimeSeconds),
                    "Event monotonic time must not move backwards."
                );
            }
            if (frame < 0 || frame < lastFrame)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frame),
                    "Event frames must be non-negative and monotonic."
                );
            }

            string normalizedActor = OptionalId(actorId, nameof(actorId));
            string normalizedTarget = OptionalId(targetId, nameof(targetId));
            string normalizedPayload = InteractionJson.NormalizeObjectJson(
                payloadJson
            );

            var result = new InteractionEventRecord(
                runId,
                phaseId,
                nextSequence,
                monotonicTimeSeconds,
                utcTime,
                frame,
                InteractionEventNames.Validate(eventType),
                normalizedActor,
                normalizedTarget,
                normalizedPayload
            );
            nextSequence = checked(nextSequence + 1L);
            lastMonotonicTime = monotonicTimeSeconds;
            lastFrame = frame;
            return result;
        }

        private static string OptionalId(string value, string parameterName)
        {
            if (value == null)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Optional IDs must be null or non-empty.",
                    parameterName
                );
            }
            return value.Trim();
        }

        internal static void ValidatePhase(int? phaseId)
        {
            if (phaseId.HasValue && (phaseId.Value < 1 || phaseId.Value > 6))
            {
                throw new ArgumentOutOfRangeException(nameof(phaseId));
            }
        }

        internal static void ValidateFiniteNonNegative(
            double value,
            string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    public interface IInteractionEventSink
    {
        void RecordEvent(
            string eventType,
            int? phaseId,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string actorId = null,
            string targetId = null,
            string payloadJson = null);

        void FlushPhase();
    }

    public readonly struct InteractionVector3Sample
    {
        public InteractionVector3Sample(double x, double y, double z)
        {
            Validate(x, nameof(x));
            Validate(y, nameof(y));
            Validate(z, nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        private static void Validate(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    public readonly struct InteractionQuaternionSample
    {
        public InteractionQuaternionSample(
            double x,
            double y,
            double z,
            double w)
        {
            Validate(x, nameof(x));
            Validate(y, nameof(y));
            Validate(z, nameof(z));
            Validate(w, nameof(w));
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double W { get; }

        private static void Validate(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    public sealed class InteractionJointSample
    {
        public InteractionJointSample(
            string jointId,
            bool valid,
            InteractionVector3Sample position,
            InteractionQuaternionSample rotation)
        {
            if (string.IsNullOrWhiteSpace(jointId))
            {
                throw new ArgumentException(
                    "Joint ID is required.",
                    nameof(jointId)
                );
            }
            JointId = jointId.Trim();
            Valid = valid;
            Position = position;
            Rotation = rotation;
        }

        public string JointId { get; }
        public bool Valid { get; }
        public InteractionVector3Sample Position { get; }
        public InteractionQuaternionSample Rotation { get; }
    }

    public sealed class InteractionHandSample
    {
        public InteractionHandSample(
            bool tracked,
            bool dataValid,
            bool highConfidence,
            bool poseSourceInferred,
            IReadOnlyList<InteractionJointSample> joints)
        {
            Tracked = tracked;
            DataValid = dataValid;
            HighConfidence = highConfidence;
            PoseSourceInferred = poseSourceInferred;
            Joints = joints ?? Array.Empty<InteractionJointSample>();
        }

        public bool Tracked { get; }
        public bool DataValid { get; }
        public bool HighConfidence { get; }
        public bool PoseSourceInferred { get; }
        public IReadOnlyList<InteractionJointSample> Joints { get; }
    }

    public sealed class InteractionPoseSample
    {
        public InteractionPoseSample(
            int? phaseId,
            long sampleSequence,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            bool hmdValid,
            InteractionVector3Sample hmdPosition,
            InteractionQuaternionSample hmdRotation,
            InteractionHandSample leftHand,
            InteractionHandSample rightHand)
        {
            InteractionEventSequencer.ValidatePhase(phaseId);
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            if (sampleSequence < 1L || frame < 0)
            {
                throw new ArgumentOutOfRangeException(
                    sampleSequence < 1L ? nameof(sampleSequence) : nameof(frame)
                );
            }
            PhaseId = phaseId;
            SampleSequence = sampleSequence;
            MonotonicTimeSeconds = monotonicTimeSeconds;
            UtcTime = utcTime.ToUniversalTime();
            Frame = frame;
            HmdValid = hmdValid;
            HmdPosition = hmdPosition;
            HmdRotation = hmdRotation;
            LeftHand = leftHand ?? throw new ArgumentNullException(nameof(leftHand));
            RightHand = rightHand ?? throw new ArgumentNullException(nameof(rightHand));
        }

        public int? PhaseId { get; }
        public long SampleSequence { get; }
        public double MonotonicTimeSeconds { get; }
        public DateTimeOffset UtcTime { get; }
        public int Frame { get; }
        public bool HmdValid { get; }
        public InteractionVector3Sample HmdPosition { get; }
        public InteractionQuaternionSample HmdRotation { get; }
        public InteractionHandSample LeftHand { get; }
        public InteractionHandSample RightHand { get; }
    }

    public sealed class InteractionObjectSample
    {
        public InteractionObjectSample(
            int? phaseId,
            long sampleSequence,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string objectId,
            string stateJson)
        {
            InteractionEventSequencer.ValidatePhase(phaseId);
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            if (sampleSequence < 1L || frame < 0)
            {
                throw new ArgumentOutOfRangeException(
                    sampleSequence < 1L ? nameof(sampleSequence) : nameof(frame)
                );
            }
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException(
                    "Object ID is required.",
                    nameof(objectId)
                );
            }
            PhaseId = phaseId;
            SampleSequence = sampleSequence;
            MonotonicTimeSeconds = monotonicTimeSeconds;
            UtcTime = utcTime.ToUniversalTime();
            Frame = frame;
            ObjectId = objectId.Trim();
            StateJson = InteractionJson.NormalizeObjectJson(stateJson);
        }

        public int? PhaseId { get; }
        public long SampleSequence { get; }
        public double MonotonicTimeSeconds { get; }
        public DateTimeOffset UtcTime { get; }
        public int Frame { get; }
        public string ObjectId { get; }
        public string StateJson { get; }
    }

    internal static class InteractionCaptureJson
    {
        public static string SerializeEvent(InteractionEventRecord value)
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "schema_version");
            builder.Append(value.SchemaVersion);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "run_id");
            InteractionJson.AppendQuoted(builder, value.RunId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "phase_id");
            AppendNullableInt(builder, value.PhaseId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "event_seq");
            builder.Append(value.EventSequence.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "monotonic_time_s");
            InteractionJson.AppendFiniteDouble(builder, value.MonotonicTimeSeconds);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "utc_time");
            InteractionJson.AppendQuoted(
                builder,
                InteractionRunManifestContractV1.FormatUtc(value.UtcTime)
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "frame");
            builder.Append(value.Frame.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "event_type");
            InteractionJson.AppendQuoted(builder, value.EventType);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "actor_id");
            InteractionJson.AppendNullableString(builder, value.ActorId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "target_id");
            InteractionJson.AppendNullableString(builder, value.TargetId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "payload");
            builder.Append(value.PayloadJson);
            builder.Append('}');
            return builder.ToString();
        }

        public static string SerializePose(string runId, InteractionPoseSample value)
        {
            var builder = new StringBuilder(4096);
            builder.Append('{');
            AppendCapturePrefix(
                builder,
                runId,
                value.PhaseId,
                value.SampleSequence,
                value.MonotonicTimeSeconds,
                value.UtcTime,
                value.Frame,
                "pose_seq"
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "hmd");
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "valid");
            AppendBoolean(builder, value.HmdValid);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "position");
            AppendVector(builder, value.HmdPosition);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "rotation");
            AppendQuaternion(builder, value.HmdRotation);
            builder.Append('}');
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "left_hand");
            AppendHand(builder, value.LeftHand);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "right_hand");
            AppendHand(builder, value.RightHand);
            builder.Append('}');
            return builder.ToString();
        }

        public static string SerializeObject(
            string runId,
            InteractionObjectSample value)
        {
            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendCapturePrefix(
                builder,
                runId,
                value.PhaseId,
                value.SampleSequence,
                value.MonotonicTimeSeconds,
                value.UtcTime,
                value.Frame,
                "object_seq"
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "object_id");
            InteractionJson.AppendQuoted(builder, value.ObjectId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "state");
            builder.Append(value.StateJson);
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendCapturePrefix(
            StringBuilder builder,
            string runId,
            int? phaseId,
            long sequence,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string sequenceName)
        {
            InteractionRunManifestContractV1.AppendName(builder, "schema_version");
            builder.Append('1');
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "run_id");
            InteractionJson.AppendQuoted(builder, runId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "phase_id");
            AppendNullableInt(builder, phaseId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, sequenceName);
            builder.Append(sequence.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "monotonic_time_s");
            InteractionJson.AppendFiniteDouble(builder, monotonicTimeSeconds);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "utc_time");
            InteractionJson.AppendQuoted(
                builder,
                InteractionRunManifestContractV1.FormatUtc(utcTime)
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "frame");
            builder.Append(frame.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendHand(StringBuilder builder, InteractionHandSample value)
        {
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "tracked");
            AppendBoolean(builder, value.Tracked);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "data_valid");
            AppendBoolean(builder, value.DataValid);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "high_confidence");
            AppendBoolean(builder, value.HighConfidence);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "pose_source_inferred");
            AppendBoolean(builder, value.PoseSourceInferred);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "joints");
            builder.Append('[');
            for (int index = 0; index < value.Joints.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                InteractionJointSample joint = value.Joints[index];
                builder.Append('{');
                InteractionRunManifestContractV1.AppendName(builder, "joint_id");
                InteractionJson.AppendQuoted(builder, joint.JointId);
                InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "valid");
                AppendBoolean(builder, joint.Valid);
                InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "position");
                AppendVector(builder, joint.Position);
                InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "rotation");
                AppendQuaternion(builder, joint.Rotation);
                builder.Append('}');
            }
            builder.Append(']');
            builder.Append('}');
        }

        private static void AppendVector(
            StringBuilder builder,
            InteractionVector3Sample value)
        {
            builder.Append('[');
            InteractionJson.AppendFiniteDouble(builder, value.X);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.Y);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.Z);
            builder.Append(']');
        }

        private static void AppendQuaternion(
            StringBuilder builder,
            InteractionQuaternionSample value)
        {
            builder.Append('[');
            InteractionJson.AppendFiniteDouble(builder, value.X);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.Y);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.Z);
            builder.Append(',');
            InteractionJson.AppendFiniteDouble(builder, value.W);
            builder.Append(']');
        }

        internal static void AppendNullableInt(StringBuilder builder, int? value)
        {
            if (value.HasValue)
            {
                builder.Append(value.Value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append("null");
            }
        }

        internal static void AppendBoolean(StringBuilder builder, bool value)
        {
            builder.Append(value ? "true" : "false");
        }
    }
}
