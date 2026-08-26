using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Converts a retained abnormal partial Run into an explicit local Abort.
    /// It never invents pose/object samples: valid complete rows are retained,
    /// empty streams remain empty, and summary completeness reports that fact.
    /// </summary>
    public static class InteractionPartialRunRecovery
    {
        public static InteractionBackgroundOperation<int>
            BeginTerminalizeAllAborted(
            string persistentDataPath,
            string abortReason,
            DateTimeOffset endedUtc,
            double monotonicTimeSeconds,
            int frame)
        {
            return InteractionBackgroundOperation<int>.Start(() =>
                TerminalizeAllAborted(
                    persistentDataPath,
                    abortReason,
                    endedUtc,
                    monotonicTimeSeconds,
                    frame
                )
            );
        }

        public static int TerminalizeAllAborted(
            string persistentDataPath,
            string reason,
            DateTimeOffset utcTime,
            double monotonicTimeSeconds,
            int frame)
        {
            IReadOnlyList<InteractionPendingRun> pending =
                InteractionPendingRunDiscovery.Discover(persistentDataPath);
            return TerminalizeAllAborted(
                pending,
                reason,
                utcTime,
                monotonicTimeSeconds,
                frame
            );
        }

        /// <summary>
        /// Strict Standalone Study startup recovery. Quest-local sealed Runs
        /// are already complete; only unsealed Runs are terminalized. Invalid
        /// manifest evidence throws before a new Run may start.
        /// </summary>
        public static int TerminalizeAllAbortedQuestLocal(
            string persistentDataPath,
            string reason,
            DateTimeOffset utcTime,
            double monotonicTimeSeconds,
            int frame)
        {
            IReadOnlyList<InteractionPendingRun> pending =
                InteractionPendingRunDiscovery.DiscoverQuestLocal(
                    persistentDataPath
                );
            return TerminalizeAllAborted(
                pending,
                reason,
                utcTime,
                monotonicTimeSeconds,
                frame
            );
        }

        private static int TerminalizeAllAborted(
            IReadOnlyList<InteractionPendingRun> pending,
            string reason,
            DateTimeOffset utcTime,
            double monotonicTimeSeconds,
            int frame)
        {
            int recovered = 0;
            for (int index = 0; index < pending.Count; index++)
            {
                if (!pending[index].NeedsRecovery)
                {
                    continue;
                }
                TerminalizeAborted(
                    pending[index],
                    reason,
                    utcTime,
                    monotonicTimeSeconds,
                    frame
                );
                recovered++;
            }
            return recovered;
        }

        public static void TerminalizeAborted(
            InteractionPendingRun pending,
            string reason,
            DateTimeOffset utcTime,
            double monotonicTimeSeconds,
            int frame)
        {
            if (pending == null)
            {
                throw new ArgumentNullException(nameof(pending));
            }
            if (!pending.NeedsRecovery)
            {
                throw new InvalidOperationException(
                    "Only an unsealed pending Run can be terminalized."
                );
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "Partial recovery requires an abort reason.",
                    nameof(reason)
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

            string directory = Path.GetFullPath(pending.DirectoryPath);
            InteractionStoragePaths.EnsureChildPath(
                Path.GetDirectoryName(Path.GetDirectoryName(
                    Path.GetDirectoryName(directory)
                )),
                directory
            );
            InteractionAtomicFile.RecoverDirectory(directory);
            string manifestPath = Path.Combine(
                directory,
                InteractionStoragePaths.ManifestFileName
            );
            IDictionary<string, object> manifest = InteractionJson.ParseObject(
                InteractionAtomicFile.ReadUtf8(manifestPath)
            );
            if (!string.Equals(
                    InteractionJson.RequireString(manifest, "batch_id"),
                    pending.BatchId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    InteractionJson.RequireString(manifest, "participant_id"),
                    pending.ParticipantId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    InteractionJson.RequireString(manifest, "run_id"),
                    pending.RunId,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "Partial Run manifest identity does not match its path."
                );
            }

            RecoveryEventTail tail = RecoverEvents(
                directory,
                pending.RunId,
                reason.Trim(),
                utcTime,
                monotonicTimeSeconds,
                frame
            );
            bool posesComplete = RecoverIdentityStream(
                directory,
                InteractionStoragePaths.PosesFileName,
                pending.RunId
            );
            bool objectsComplete = RecoverIdentityStream(
                directory,
                InteractionStoragePaths.ObjectsFileName,
                pending.RunId
            );
            var completeness = new InteractionDataCompleteness(
                new FileInfo(manifestPath).Length > 0L,
                true,
                posesComplete,
                objectsComplete,
                true,
                tail.CaptureGapCount
            );
            var summary = new InteractionSummaryTracker(pending.RunId);
            InteractionRunSummary aborted = summary.SealAborted(
                tail.MonotonicTimeSeconds,
                utcTime,
                reason.Trim(),
                completeness
            );
            InteractionAtomicFile.WriteNew(
                Path.Combine(
                    directory,
                    InteractionStoragePaths.SummaryFileName
                ),
                new UTF8Encoding(false).GetBytes(
                    InteractionSummaryJson.Serialize(aborted)
                )
            );
            // Retain original partial evidence until the terminal summary is
            // durable. Discovery removes these known residues idempotently if
            // the process exits between summary publication and this cleanup.
            DeleteKnownPartial(
                directory,
                InteractionStoragePaths.EventsFileName
            );
            DeleteKnownPartial(
                directory,
                InteractionStoragePaths.PosesFileName
            );
            DeleteKnownPartial(
                directory,
                InteractionStoragePaths.ObjectsFileName
            );
        }

        private static RecoveryEventTail RecoverEvents(
            string directory,
            string runId,
            string reason,
            DateTimeOffset utcTime,
            double requestedMonotonic,
            int requestedFrame)
        {
            string finalPath = Path.Combine(
                directory,
                InteractionStoragePaths.EventsFileName
            );
            string inputPath = File.Exists(finalPath)
                ? finalPath
                : PartialPath(directory, InteractionStoragePaths.EventsFileName);
            string temporary = InteractionAtomicFile.CreateSiblingTemporaryPath(
                finalPath,
                "partial-recovery"
            );
            long lastSequence = 0L;
            double lastMonotonic = 0d;
            int lastFrame = 0;
            long captureGaps = 0L;
            bool invalidTail = false;
            bool hasAbortEvent = false;
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    65536,
                    FileOptions.SequentialScan))
                using (var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    65536,
                    true))
                {
                    if (File.Exists(inputPath))
                    {
                        using (var reader = new StreamReader(
                            inputPath,
                            new UTF8Encoding(false, true),
                            true,
                            65536))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                {
                                    continue;
                                }
                                try
                                {
                                    IDictionary<string, object> value =
                                        InteractionJson.ParseObject(line);
                                    ValidateIdentity(value, runId);
                                    long sequence = InteractionJson.RequireInt64(
                                        value,
                                        "event_seq"
                                    );
                                    double monotonic = RequireNumber(
                                        value,
                                        "monotonic_time_s"
                                    );
                                    int rowFrame = InteractionJson.RequireInt32(
                                        value,
                                        "frame"
                                    );
                                    ValidateEventEnvelope(value);
                                    if (sequence <= lastSequence ||
                                        monotonic < lastMonotonic ||
                                        rowFrame < lastFrame)
                                    {
                                        throw new FormatException(
                                            "Partial event ordering is invalid."
                                        );
                                    }
                                    if (invalidTail)
                                    {
                                        throw new IOException(
                                            "Malformed partial event is not confined to the tail."
                                        );
                                    }
                                    lastSequence = sequence;
                                    lastMonotonic = monotonic;
                                    lastFrame = rowFrame;
                                    string eventType =
                                        InteractionJson.RequireString(
                                            value,
                                            "event_type"
                                        );
                                    if (string.Equals(
                                            eventType,
                                            InteractionEventNames.CaptureGap,
                                            StringComparison.Ordinal))
                                    {
                                        captureGaps++;
                                    }
                                    if (string.Equals(
                                            eventType,
                                            InteractionEventNames.RunAborted,
                                            StringComparison.Ordinal))
                                    {
                                        hasAbortEvent = true;
                                    }
                                    writer.WriteLine(line);
                                }
                                catch (Exception exception) when (
                                    exception is FormatException ||
                                    exception is ArgumentException)
                                {
                                    invalidTail = true;
                                }
                            }
                        }
                    }
                    double abortMonotonic = Math.Max(
                        requestedMonotonic,
                        lastMonotonic
                    );
                    int abortFrame = Math.Max(requestedFrame, lastFrame);
                    if (!hasAbortEvent)
                    {
                        var sequencer = new InteractionEventSequencer(
                            runId,
                            checked(lastSequence + 1L),
                            lastMonotonic,
                            lastFrame
                        );
                        string payload = "{\"reason\":" + Quote(reason) +
                            ",\"recovered_partial\":true}";
                        InteractionEventRecord aborted = sequencer.Create(
                            InteractionEventNames.RunAborted,
                            null,
                            abortMonotonic,
                            utcTime,
                            abortFrame,
                            payloadJson: payload
                        );
                        writer.WriteLine(
                            InteractionCaptureJson.SerializeEvent(aborted)
                        );
                        lastMonotonic = abortMonotonic;
                    }
                    writer.Flush();
                    stream.Flush();
                    stream.Flush(true);
                }
                InteractionAtomicFile.PublishPreparedFile(
                    temporary,
                    finalPath,
                    replace: File.Exists(finalPath),
                    allowEmpty: false
                );
                return new RecoveryEventTail(lastMonotonic, captureGaps);
            }
            finally
            {
                DeleteIfExists(temporary);
            }
        }

        private static bool RecoverIdentityStream(
            string directory,
            string fileName,
            string runId)
        {
            string finalPath = Path.Combine(directory, fileName);
            string inputPath = File.Exists(finalPath)
                ? finalPath
                : PartialPath(directory, fileName);
            string temporary = InteractionAtomicFile.CreateSiblingTemporaryPath(
                finalPath,
                "partial-recovery"
            );
            long validRows = 0L;
            bool invalidTail = false;
            long lastSequence = 0L;
            double lastMonotonic = -1d;
            int lastFrame = -1;
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    65536,
                    FileOptions.SequentialScan))
                using (var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(false),
                    65536,
                    true))
                {
                    if (File.Exists(inputPath))
                    {
                        using (var reader = new StreamReader(
                            inputPath,
                            new UTF8Encoding(false, true),
                            true,
                            65536))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                {
                                    continue;
                                }
                                try
                                {
                                    IDictionary<string, object> value =
                                        InteractionJson.ParseObject(line);
                                    ValidateIdentityStreamRow(
                                        value,
                                        runId,
                                        fileName,
                                        lastSequence,
                                        lastMonotonic,
                                        lastFrame,
                                        out long sequence,
                                        out double monotonic,
                                        out int rowFrame
                                    );
                                    if (invalidTail)
                                    {
                                        throw new IOException(
                                            "Malformed capture row is not confined to the tail."
                                        );
                                    }
                                    writer.WriteLine(line);
                                    validRows++;
                                    lastSequence = sequence;
                                    lastMonotonic = monotonic;
                                    lastFrame = rowFrame;
                                }
                                catch (Exception exception) when (
                                    exception is FormatException ||
                                    exception is ArgumentException)
                                {
                                    invalidTail = true;
                                }
                            }
                        }
                    }
                    writer.Flush();
                    stream.Flush();
                    stream.Flush(true);
                }
                InteractionAtomicFile.PublishPreparedFile(
                    temporary,
                    finalPath,
                    replace: File.Exists(finalPath),
                    allowEmpty: true
                );
                return validRows > 0L;
            }
            finally
            {
                DeleteIfExists(temporary);
            }
        }

        private static void ValidateIdentityStreamRow(
            IDictionary<string, object> value,
            string runId,
            string fileName,
            long previousSequence,
            double previousMonotonic,
            int previousFrame,
            out long sequence,
            out double monotonic,
            out int frame)
        {
            ValidateIdentity(value, runId);
            ValidatePhase(value);
            string sequenceName;
            if (string.Equals(
                    fileName,
                    InteractionStoragePaths.PosesFileName,
                    StringComparison.Ordinal))
            {
                sequenceName = "pose_seq";
                ValidatePosePayload(value);
            }
            else if (string.Equals(
                fileName,
                InteractionStoragePaths.ObjectsFileName,
                StringComparison.Ordinal))
            {
                sequenceName = "object_seq";
                ValidateObjectPayload(value);
            }
            else
            {
                throw new ArgumentException(
                    "Unknown identity stream file name.",
                    nameof(fileName)
                );
            }

            sequence = InteractionJson.RequireInt64(value, sequenceName);
            monotonic = RequireNumber(value, "monotonic_time_s");
            frame = InteractionJson.RequireInt32(value, "frame");
            ValidateUtc(value);
            if (sequence < 1L || sequence <= previousSequence ||
                monotonic < previousMonotonic ||
                frame < 0 || frame < previousFrame)
            {
                throw new FormatException(
                    "Partial capture stream ordering is invalid."
                );
            }
        }

        private static void ValidatePhase(IDictionary<string, object> value)
        {
            if (!value.TryGetValue("phase_id", out object raw) ||
                (raw != null && !(raw is long)))
            {
                throw new FormatException(
                    "Capture phase_id must be null or an integer."
                );
            }
            if (raw is long phase && (phase < 1L || phase > 6L))
            {
                throw new FormatException("Capture phase_id is invalid.");
            }
        }

        private static void ValidateUtc(IDictionary<string, object> value)
        {
            if (!DateTimeOffset.TryParse(
                    InteractionJson.RequireString(value, "utc_time"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                    out _))
            {
                throw new FormatException("Capture UTC is invalid.");
            }
        }

        private static void ValidatePosePayload(
            IDictionary<string, object> value)
        {
            IDictionary<string, object> hmd = InteractionJson.RequireObject(
                value,
                "hmd"
            );
            RequireBoolean(hmd, "valid");
            RequireFiniteArray(hmd, "position", 3);
            RequireFiniteArray(hmd, "rotation", 4);
            ValidateHandPayload(InteractionJson.RequireObject(
                value,
                "left_hand"
            ));
            ValidateHandPayload(InteractionJson.RequireObject(
                value,
                "right_hand"
            ));
        }

        private static void ValidateHandPayload(
            IDictionary<string, object> hand)
        {
            RequireBoolean(hand, "tracked");
            RequireBoolean(hand, "data_valid");
            RequireBoolean(hand, "high_confidence");
            RequireBoolean(hand, "pose_source_inferred");
            IList<object> joints = InteractionJson.RequireArray(hand, "joints");
            for (int index = 0; index < joints.Count; index++)
            {
                if (!(joints[index] is IDictionary<string, object> joint))
                {
                    throw new FormatException(
                        "Capture hand joints must contain objects."
                    );
                }
                InteractionJson.RequireString(joint, "joint_id");
                RequireBoolean(joint, "valid");
                RequireFiniteArray(joint, "position", 3);
                RequireFiniteArray(joint, "rotation", 4);
            }
        }

        private static void ValidateObjectPayload(
            IDictionary<string, object> value)
        {
            InteractionJson.RequireString(value, "object_id");
            InteractionJson.RequireObject(value, "state");
        }

        private static void RequireBoolean(
            IDictionary<string, object> value,
            string propertyName)
        {
            bool? result = InteractionJson.OptionalBoolean(
                value,
                propertyName
            );
            if (!result.HasValue)
            {
                throw new FormatException(
                    "JSON property '" + propertyName +
                    "' must be a boolean."
                );
            }
        }

        private static void RequireFiniteArray(
            IDictionary<string, object> value,
            string propertyName,
            int expectedCount)
        {
            IList<object> items = InteractionJson.RequireArray(
                value,
                propertyName
            );
            if (items.Count != expectedCount)
            {
                throw new FormatException(
                    "JSON property '" + propertyName + "' must contain " +
                    expectedCount.ToString(CultureInfo.InvariantCulture) +
                    " numbers."
                );
            }
            for (int index = 0; index < items.Count; index++)
            {
                double number = items[index] is long integer
                    ? integer
                    : items[index] is double real
                        ? real
                        : double.NaN;
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw new FormatException(
                        "JSON property '" + propertyName +
                        "' must contain only finite numbers."
                    );
                }
            }
        }

        private static void ValidateEventEnvelope(
            IDictionary<string, object> value)
        {
            object phase;
            if (!value.TryGetValue("phase_id", out phase) ||
                (phase != null && !(phase is long)))
            {
                throw new FormatException("Event phase_id is invalid.");
            }
            if (phase is long)
            {
                long id = (long)phase;
                if (id < 1L || id > 6L)
                {
                    throw new FormatException("Event phase_id is invalid.");
                }
            }
            if (InteractionJson.RequireInt64(value, "event_seq") < 1L ||
                InteractionJson.RequireInt32(value, "frame") < 0)
            {
                throw new FormatException("Event sequence/frame is invalid.");
            }
            InteractionEventNames.Validate(
                InteractionJson.RequireString(value, "event_type")
            );
            DateTimeOffset utc;
            if (!DateTimeOffset.TryParse(
                    InteractionJson.RequireString(value, "utc_time"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                    out utc))
            {
                throw new FormatException("Event UTC is invalid.");
            }
            RequireNullableString(value, "actor_id");
            RequireNullableString(value, "target_id");
            InteractionJson.RequireObject(value, "payload");
        }

        private static void ValidateIdentity(
            IDictionary<string, object> value,
            string runId)
        {
            if (InteractionJson.RequireInt32(value, "schema_version") != 1 ||
                !string.Equals(
                    InteractionJson.RequireString(value, "run_id"),
                    runId,
                    StringComparison.Ordinal))
            {
                throw new FormatException(
                    "Partial capture row identity mismatched."
                );
            }
        }

        private static double RequireNumber(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (!value.TryGetValue(propertyName, out raw))
            {
                throw new FormatException(
                    "Capture number is missing: " + propertyName + "."
                );
            }
            double result = raw is long
                ? (long)raw
                : raw is double
                    ? (double)raw
                    : double.NaN;
            InteractionEventSequencer.ValidateFiniteNonNegative(
                result,
                propertyName
            );
            return result;
        }

        private static void RequireNullableString(
            IDictionary<string, object> value,
            string propertyName)
        {
            object raw;
            if (!value.TryGetValue(propertyName, out raw) ||
                (raw != null && !(raw is string)))
            {
                throw new FormatException(
                    "Event " + propertyName + " is invalid."
                );
            }
        }

        private static string PartialPath(string directory, string fileName)
        {
            return Path.Combine(directory, "." + fileName + ".partial");
        }

        private static void DeleteKnownPartial(
            string directory,
            string fileName)
        {
            DeleteIfExists(PartialPath(directory, fileName));
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string Quote(string value)
        {
            var builder = new StringBuilder();
            InteractionJson.AppendQuoted(builder, value);
            return builder.ToString();
        }

        private sealed class RecoveryEventTail
        {
            public RecoveryEventTail(
                double monotonicTimeSeconds,
                long captureGapCount)
            {
                MonotonicTimeSeconds = monotonicTimeSeconds;
                CaptureGapCount = captureGapCount;
            }

            public double MonotonicTimeSeconds { get; }
            public long CaptureGapCount { get; }
        }
    }
}
