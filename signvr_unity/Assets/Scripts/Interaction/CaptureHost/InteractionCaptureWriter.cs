using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    public static class InteractionCaptureWaitLimits
    {
        public const int CriticalEnqueueMilliseconds = 250;
        public const int DrainMilliseconds = 500;
        public const int CloseMilliseconds = 500;
    }

    public sealed class InteractionCaptureWriter : IInteractionEventSink, IDisposable
    {
        public const int DefaultQueueCapacity = 512;
        public const double DefaultGapThresholdSeconds = 0.25d;

        private readonly string runId;
        private readonly byte[] manifestBytes;
        private readonly InteractionEventSequencer eventSequencer;
        private readonly IInteractionJsonlChannel events;
        private readonly IInteractionJsonlChannel poses;
        private readonly IInteractionJsonlChannel objects;
        private readonly double gapThresholdSeconds;

        private bool captureActive;
        private bool sealedCapture;
        private bool disposed;
        private long nextPoseSequence = 1L;
        private long nextObjectSequence = 1L;
        private double lastPoseMonotonic = -1d;
        private int lastPoseFrame = -1;
        private long captureGapCount;

        private InteractionCaptureWriter(
            string runDirectory,
            string runId,
            byte[] manifestBytes,
            int queueCapacity,
            double gapThresholdSeconds,
            IInteractionJsonlChannelFactory channelFactory)
        {
            if (queueCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            }
            InteractionEventSequencer.ValidateFiniteNonNegative(
                gapThresholdSeconds,
                nameof(gapThresholdSeconds)
            );
            if (gapThresholdSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(gapThresholdSeconds));
            }

            RunDirectory = runDirectory;
            this.runId = runId;
            this.manifestBytes = (byte[])manifestBytes.Clone();
            this.gapThresholdSeconds = gapThresholdSeconds;
            eventSequencer = new InteractionEventSequencer(runId);
            channelFactory = channelFactory ??
                throw new ArgumentNullException(nameof(channelFactory));

            events = channelFactory.Create(
                Path.Combine(runDirectory, "." + InteractionStoragePaths.EventsFileName + ".partial"),
                Path.Combine(runDirectory, InteractionStoragePaths.EventsFileName),
                queueCapacity,
                "SignVR Interaction Events Writer"
            );
            try
            {
                poses = channelFactory.Create(
                    Path.Combine(runDirectory, "." + InteractionStoragePaths.PosesFileName + ".partial"),
                    Path.Combine(runDirectory, InteractionStoragePaths.PosesFileName),
                    queueCapacity,
                    "SignVR Interaction Poses Writer"
                );
                try
                {
                    objects = channelFactory.Create(
                        Path.Combine(runDirectory, "." + InteractionStoragePaths.ObjectsFileName + ".partial"),
                        Path.Combine(runDirectory, InteractionStoragePaths.ObjectsFileName),
                        queueCapacity,
                        "SignVR Interaction Objects Writer"
                    );
                }
                catch
                {
                    poses.DisposeLeavingPartial();
                    throw;
                }
            }
            catch
            {
                events.DisposeLeavingPartial();
                throw;
            }
        }

        public string RunDirectory { get; }
        public string RunId => runId;
        public bool CaptureActive => captureActive;
        public bool IsSealed => sealedCapture;
        public long CaptureGapCount => captureGapCount;
        public long NextPoseSequence => nextPoseSequence;
        public long NextObjectSequence => nextObjectSequence;
        public byte[] ManifestBytes => (byte[])manifestBytes.Clone();

        public static InteractionCaptureWriter CreateNew(
            string persistentDataPath,
            RunPlan plan,
            byte[] exactManifestBytes,
            int queueCapacity = DefaultQueueCapacity,
            double gapThresholdSeconds = DefaultGapThresholdSeconds)
        {
            return CreateNew(
                persistentDataPath,
                plan,
                exactManifestBytes,
                queueCapacity,
                gapThresholdSeconds,
                new InteractionJsonlChannelFactory()
            );
        }

        internal static InteractionCaptureWriter CreateNew(
            string persistentDataPath,
            RunPlan plan,
            byte[] exactManifestBytes,
            int queueCapacity,
            double gapThresholdSeconds,
            IInteractionJsonlChannelFactory channelFactory)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (exactManifestBytes == null || exactManifestBytes.Length == 0)
            {
                throw new ArgumentException(
                    "Exact manifest bytes are required.",
                    nameof(exactManifestBytes)
                );
            }
            byte[] expected = InteractionRunManifestContractV1.SerializeUtf8(plan);
            if (!ByteArraysEqual(expected, exactManifestBytes))
            {
                throw new ArgumentException(
                    "Manifest bytes do not exactly match the immutable RunPlan.",
                    nameof(exactManifestBytes)
                );
            }

            string runDirectory = InteractionStoragePaths.GetRunDirectory(
                persistentDataPath,
                plan.BatchId,
                plan.ParticipantId,
                plan.RunId
            );
            if (Directory.Exists(runDirectory))
            {
                throw new IOException(
                    "Refusing to reuse existing Interaction Run directory " +
                    runDirectory + "."
                );
            }

            Directory.CreateDirectory(runDirectory);
            string manifestPath = Path.Combine(
                runDirectory,
                InteractionStoragePaths.ManifestFileName
            );
            try
            {
                // This is deliberately the first file publication in a Run.
                InteractionAtomicFile.WriteNew(manifestPath, exactManifestBytes);
                return new InteractionCaptureWriter(
                    runDirectory,
                    plan.RunId,
                    exactManifestBytes,
                    queueCapacity,
                    gapThresholdSeconds,
                    channelFactory
                );
            }
            catch
            {
                // Never delete the directory here. A published manifest or a
                // partial stream is evidence needed for restart recovery.
                throw;
            }
        }

        public void BeginCapture()
        {
            EnsureOpen();
            if (captureActive)
            {
                throw new InvalidOperationException("Capture already started.");
            }
            captureActive = true;
        }

        public void RecordEvent(
            string eventType,
            int? phaseId,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string actorId = null,
            string targetId = null,
            string payloadJson = null)
        {
            EnsureOpen();
            InteractionEventRecord record = eventSequencer.Create(
                eventType,
                phaseId,
                monotonicTimeSeconds,
                utcTime,
                frame,
                actorId,
                targetId,
                payloadJson
            );
            // Domain events are never silently dropped. A short bounded wait
            // either queues the event or fails closed, preserving partial data.
            events.WriteCritical(InteractionCaptureJson.SerializeEvent(record));
        }

        public bool TryWritePose(InteractionPoseSample sample)
        {
            EnsureOpen();
            if (!captureActive)
            {
                return false;
            }
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            if (sample.SampleSequence != nextPoseSequence ||
                sample.MonotonicTimeSeconds < lastPoseMonotonic ||
                sample.Frame < lastPoseFrame)
            {
                throw new InvalidOperationException(
                    "Pose sequence, frame, and monotonic time must increase."
                );
            }

            if (lastPoseMonotonic >= 0d &&
                sample.MonotonicTimeSeconds - lastPoseMonotonic >
                    gapThresholdSeconds)
            {
                RecordCaptureGap(
                    sample.PhaseId,
                    sample.MonotonicTimeSeconds,
                    sample.UtcTime,
                    sample.Frame,
                    "pose_time_gap",
                    0L,
                    sample.MonotonicTimeSeconds - lastPoseMonotonic
                );
            }

            nextPoseSequence = checked(nextPoseSequence + 1L);
            lastPoseMonotonic = sample.MonotonicTimeSeconds;
            lastPoseFrame = sample.Frame;
            bool accepted = poses.TryWrite(
                InteractionCaptureJson.SerializePose(runId, sample)
            );
            if (!accepted)
            {
                RecordCaptureGap(
                    sample.PhaseId,
                    sample.MonotonicTimeSeconds,
                    sample.UtcTime,
                    sample.Frame,
                    "poses_buffer_overflow",
                    1L,
                    0d
                );
            }
            return accepted;
        }

        public bool TryWriteObject(InteractionObjectSample sample)
        {
            EnsureOpen();
            if (!captureActive)
            {
                return false;
            }
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            if (sample.SampleSequence != nextObjectSequence)
            {
                throw new InvalidOperationException(
                    "Object sample sequence must be strictly increasing."
                );
            }
            nextObjectSequence = checked(nextObjectSequence + 1L);
            bool accepted = objects.TryWrite(
                InteractionCaptureJson.SerializeObject(runId, sample)
            );
            if (!accepted)
            {
                RecordCaptureGap(
                    sample.PhaseId,
                    sample.MonotonicTimeSeconds,
                    sample.UtcTime,
                    sample.Frame,
                    "objects_buffer_overflow",
                    1L,
                    0d
                );
            }
            return accepted;
        }

        public void ReportExternalCaptureGap(
            int? phaseId,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string reason,
            double durationSeconds)
        {
            RecordCaptureGap(
                phaseId,
                monotonicTimeSeconds,
                utcTime,
                frame,
                reason,
                0L,
                durationSeconds
            );
        }

        public void FlushPhase()
        {
            EnsureOpen();
            events.FlushAndSync();
            poses.FlushAndSync();
            objects.FlushAndSync();
        }

        public InteractionRunSummary Seal(
            Func<InteractionDataCompleteness, InteractionRunSummary> summaryFactory)
        {
            EnsureOpen();
            if (summaryFactory == null)
            {
                throw new ArgumentNullException(nameof(summaryFactory));
            }

            captureActive = false;
            events.CloseAndPromote();
            poses.CloseAndPromote();
            objects.CloseAndPromote();

            var completeness = new InteractionDataCompleteness(
                IsNonEmpty(InteractionStoragePaths.ManifestFileName),
                IsNonEmpty(InteractionStoragePaths.EventsFileName),
                IsNonEmpty(InteractionStoragePaths.PosesFileName),
                IsNonEmpty(InteractionStoragePaths.ObjectsFileName),
                true,
                captureGapCount
            );
            InteractionRunSummary summary = summaryFactory(completeness);
            if (summary == null || !string.Equals(
                    summary.RunId,
                    runId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Summary factory returned no summary or the wrong Run ID."
                );
            }
            InteractionAtomicFile.WriteNew(
                Path.Combine(RunDirectory, InteractionStoragePaths.SummaryFileName),
                new UTF8Encoding(false).GetBytes(
                    InteractionSummaryJson.Serialize(summary)
                )
            );
            sealedCapture = true;
            return summary;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            captureActive = false;
            if (!sealedCapture)
            {
                DisposeLeavingPartial(events);
                DisposeLeavingPartial(poses);
                DisposeLeavingPartial(objects);
            }
        }

        private void RecordCaptureGap(
            int? phaseId,
            double monotonicTimeSeconds,
            DateTimeOffset utcTime,
            int frame,
            string reason,
            long droppedSamples,
            double durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("Gap reason is required.", nameof(reason));
            }
            InteractionEventSequencer.ValidateFiniteNonNegative(
                durationSeconds,
                nameof(durationSeconds)
            );
            captureGapCount = checked(captureGapCount + 1L);
            var payload = new StringBuilder(160);
            payload.Append('{');
            InteractionRunManifestContractV1.AppendName(payload, "reason");
            InteractionJson.AppendQuoted(payload, reason.Trim());
            InteractionRunManifestContractV1.AppendSeparatorAndName(payload, "dropped_samples");
            payload.Append(droppedSamples.ToString(CultureInfo.InvariantCulture));
            InteractionRunManifestContractV1.AppendSeparatorAndName(payload, "duration_s");
            InteractionJson.AppendFiniteDouble(payload, durationSeconds);
            payload.Append('}');
            RecordEvent(
                InteractionEventNames.CaptureGap,
                phaseId,
                monotonicTimeSeconds,
                utcTime,
                frame,
                null,
                null,
                payload.ToString()
            );
        }

        private bool IsNonEmpty(string fileName)
        {
            string path = Path.Combine(RunDirectory, fileName);
            return File.Exists(path) && new FileInfo(path).Length > 0L;
        }

        private void EnsureOpen()
        {
            if (disposed || sealedCapture)
            {
                throw new ObjectDisposedException(nameof(InteractionCaptureWriter));
            }
        }

        private static void DisposeLeavingPartial(IInteractionJsonlChannel channel)
        {
            try
            {
                channel.DisposeLeavingPartial();
            }
            catch
            {
                // Dispose is best effort; partial files remain discoverable.
            }
        }

        internal static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }
            return difference == 0;
        }
    }

    internal interface IInteractionJsonlChannelFactory
    {
        IInteractionJsonlChannel Create(
            string partialPath,
            string finalPath,
            int capacity,
            string workerName);
    }

    internal interface IInteractionJsonlChannel
    {
        long AcceptedLineCount { get; }
        bool TryWrite(string line);
        void WriteCritical(string line);
        void FlushAndSync();
        void CloseAndPromote();
        void DisposeLeavingPartial();
    }

    internal sealed class InteractionJsonlChannelFactory :
        IInteractionJsonlChannelFactory
    {
        public IInteractionJsonlChannel Create(
            string partialPath,
            string finalPath,
            int capacity,
            string workerName)
        {
            return new InteractionAsyncJsonlChannel(
                partialPath,
                finalPath,
                capacity,
                workerName
            );
        }
    }

    internal sealed class InteractionAsyncJsonlChannel :
        IInteractionJsonlChannel
    {
        private readonly object gate = new object();
        private readonly object ioGate = new object();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly int capacity;
        private readonly string partialPath;
        private readonly string finalPath;
        private readonly FileStream stream;
        private readonly StreamWriter writer;
        private readonly Thread worker;

        private bool accepting = true;
        private bool closeRequested;
        private bool promoted;
        private Exception workerException;
        private long acceptedLineCount;
        private long writtenLineCount;

        public InteractionAsyncJsonlChannel(
            string partialPath,
            string finalPath,
            int capacity,
            string workerName)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            this.partialPath = partialPath;
            this.finalPath = finalPath;
            this.capacity = capacity;
            stream = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                65536,
                FileOptions.SequentialScan
            );
            writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                65536,
                true
            );
            worker = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = workerName
            };
            worker.Start();
        }

        public long AcceptedLineCount
        {
            get
            {
                lock (gate)
                {
                    return acceptedLineCount;
                }
            }
        }

        public bool TryWrite(string line)
        {
            ValidateLine(line);
            lock (gate)
            {
                ThrowIfFailed();
                if (!accepting || pending.Count >= capacity)
                {
                    return false;
                }
                pending.Enqueue(line);
                acceptedLineCount++;
                Monitor.PulseAll(gate);
                return true;
            }
        }

        public void WriteCritical(string line)
        {
            ValidateLine(line);
            lock (gate)
            {
                long deadline = DeadlineAfter(
                    InteractionCaptureWaitLimits.CriticalEnqueueMilliseconds
                );
                while (true)
                {
                    ThrowIfFailed();
                    if (!accepting)
                    {
                        throw new ObjectDisposedException(
                            nameof(InteractionAsyncJsonlChannel)
                        );
                    }
                    if (pending.Count < capacity)
                    {
                        pending.Enqueue(line);
                        acceptedLineCount++;
                        Monitor.PulseAll(gate);
                        return;
                    }
                    WaitUntilDeadline(
                        deadline,
                        "queueing a critical Interaction event"
                    );
                }
            }
        }

        public void FlushAndSync()
        {
            long target;
            lock (gate)
            {
                ThrowIfFailed();
                target = acceptedLineCount;
                long deadline = DeadlineAfter(
                    InteractionCaptureWaitLimits.DrainMilliseconds
                );
                while (writtenLineCount < target)
                {
                    WaitUntilDeadline(
                        deadline,
                        "draining an Interaction capture stream"
                    );
                    ThrowIfFailed();
                }
            }
            lock (ioGate)
            {
                writer.Flush();
                stream.Flush();
                stream.Flush(true);
            }
        }

        public void CloseAndPromote()
        {
            StopWorker();
            if (File.Exists(finalPath))
            {
                throw new IOException(
                    "Refusing to overwrite finalized capture file " + finalPath + "."
                );
            }
            File.Move(partialPath, finalPath);
            promoted = true;
        }

        public void DisposeLeavingPartial()
        {
            if (promoted)
            {
                return;
            }
            StopWorker();
        }

        private void StopWorker()
        {
            lock (gate)
            {
                if (closeRequested)
                {
                    ThrowIfFailed();
                    if (worker.IsAlive)
                    {
                        throw new IOException(
                            "Interaction capture writer is still closing; " +
                            "partial data remains retained."
                        );
                    }
                    return;
                }
                accepting = false;
                closeRequested = true;
                Monitor.PulseAll(gate);
            }
            if (!worker.Join(InteractionCaptureWaitLimits.CloseMilliseconds))
            {
                throw new IOException(
                    "Timed out while closing an Interaction capture writer."
                );
            }
            lock (gate)
            {
                ThrowIfFailed();
            }
        }

        private void WriteLoop()
        {
            try
            {
                while (true)
                {
                    string line;
                    lock (gate)
                    {
                        while (pending.Count == 0 && !closeRequested)
                        {
                            Monitor.Wait(gate, 50);
                        }
                        if (pending.Count == 0 && closeRequested)
                        {
                            break;
                        }
                        line = pending.Dequeue();
                        Monitor.PulseAll(gate);
                    }
                    lock (ioGate)
                    {
                        writer.WriteLine(line);
                    }
                    lock (gate)
                    {
                        writtenLineCount++;
                        Monitor.PulseAll(gate);
                    }
                }
                lock (ioGate)
                {
                    writer.Flush();
                    stream.Flush();
                    stream.Flush(true);
                }
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    workerException = exception;
                    accepting = false;
                    closeRequested = true;
                    Monitor.PulseAll(gate);
                }
            }
            finally
            {
                try
                {
                    writer.Dispose();
                    stream.Dispose();
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        if (workerException == null)
                        {
                            workerException = exception;
                        }
                        Monitor.PulseAll(gate);
                    }
                }
            }
        }

        private void ThrowIfFailed()
        {
            if (workerException != null)
            {
                throw new IOException(
                    "Interaction capture writer failed.",
                    workerException
                );
            }
        }

        private static long DeadlineAfter(int milliseconds)
        {
            return Stopwatch.GetTimestamp() + checked((long)Math.Ceiling(
                milliseconds * (double)Stopwatch.Frequency / 1000d
            ));
        }

        private void WaitUntilDeadline(long deadline, string operation)
        {
            long remainingTicks = deadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0L)
            {
                throw new IOException(
                    "Timed out while " + operation +
                    "; partial capture data was retained."
                );
            }
            int remainingMilliseconds = Math.Max(
                1,
                (int)Math.Ceiling(
                    remainingTicks * 1000d / Stopwatch.Frequency
                )
            );
            Monitor.Wait(gate, Math.Min(50, remainingMilliseconds));
        }

        private static void ValidateLine(string line)
        {
            if (string.IsNullOrEmpty(line) ||
                line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
            {
                throw new ArgumentException(
                    "JSONL records must be one non-empty line.",
                    nameof(line)
                );
            }
        }
    }

}
