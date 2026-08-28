using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SignVR.Interaction.Core;
using SignVR.Interaction.Diagnostics;

namespace SignVR.Interaction.CaptureHost
{
    public static class InteractionCaptureWaitLimits
    {
        // Critical writes never wait on the caller; this value remains a
        // diagnostic upper bound for the frozen test contract.
        public const int CriticalEnqueueMilliseconds = 1;
        public const int DrainMilliseconds = 500;
        public const int CloseMilliseconds = 500;
    }

    public enum InteractionCaptureTerminalKind
    {
        Completed,
        Aborted
    }

    public sealed class InteractionCaptureSealResult
    {
        internal InteractionCaptureSealResult(
            InteractionRunSummary summary,
            InteractionDataCompleteness completeness)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Completeness = completeness ??
                throw new ArgumentNullException(nameof(completeness));
        }

        public InteractionRunSummary Summary { get; }
        public InteractionDataCompleteness Completeness { get; }
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
        private readonly InteractionCaptureBudgetPolicy budgetPolicy;
        private readonly InteractionDiskBudgetGuard diskBudget;
        private readonly InteractionSerialBackgroundScheduler ioScheduler;
        private readonly InteractionCaptureQualityThresholds qualityThresholds;
        private readonly Dictionary<string, long> requiredProbeSampleCounts;

        private bool captureActive;
        private volatile bool sealedCapture;
        private bool disposed;
        private bool terminalRequested;
        private long nextPoseSequence = 1L;
        private long nextObjectSequence = 1L;
        private double lastPoseMonotonic = -1d;
        private int lastPoseFrame = -1;
        private long captureGapCount;
        private long poseAttemptCount;
        private long acceptedPoseSampleCount;
        private long hmdValidSampleCount;
        private long leftHandValidSampleCount;
        private long rightHandValidSampleCount;
        private double firstAcceptedPoseMonotonic = -1d;
        private double lastAcceptedPoseMonotonic = -1d;

        private InteractionCaptureWriter(
            string runDirectory,
            string runId,
            byte[] manifestBytes,
            int queueCapacity,
            double gapThresholdSeconds,
            IInteractionJsonlChannelFactory channelFactory,
            InteractionCaptureBudgetPolicy budgetPolicy,
            InteractionDiskBudgetGuard diskBudget,
            InteractionCaptureQualityThresholds qualityThresholds,
            IReadOnlyCollection<string> requiredProbeIds)
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
            this.budgetPolicy = budgetPolicy ??
                throw new ArgumentNullException(nameof(budgetPolicy));
            this.diskBudget = diskBudget ??
                throw new ArgumentNullException(nameof(diskBudget));
            this.qualityThresholds = qualityThresholds ??
                throw new ArgumentNullException(nameof(qualityThresholds));
            if (requiredProbeIds == null || requiredProbeIds.Count == 0)
            {
                throw new ArgumentException(
                    "At least one Run Plan target probe is required.",
                    nameof(requiredProbeIds)
                );
            }
            requiredProbeSampleCounts = new Dictionary<string, long>(
                StringComparer.Ordinal
            );
            foreach (string requiredProbeId in requiredProbeIds)
            {
                if (string.IsNullOrWhiteSpace(requiredProbeId) ||
                    requiredProbeSampleCounts.ContainsKey(requiredProbeId))
                {
                    throw new ArgumentException(
                        "Required probe IDs must be unique and non-empty.",
                        nameof(requiredProbeIds)
                    );
                }
                requiredProbeSampleCounts.Add(requiredProbeId, 0L);
            }
            eventSequencer = new InteractionEventSequencer(runId);
            channelFactory = channelFactory ??
                throw new ArgumentNullException(nameof(channelFactory));

            events = channelFactory.Create(
                Path.Combine(runDirectory, "." + InteractionStoragePaths.EventsFileName + ".partial"),
                Path.Combine(runDirectory, InteractionStoragePaths.EventsFileName),
                queueCapacity,
                "SignVR Interaction Events Writer",
                budgetPolicy.Events,
                diskBudget
            );
            try
            {
                poses = channelFactory.Create(
                    Path.Combine(runDirectory, "." + InteractionStoragePaths.PosesFileName + ".partial"),
                    Path.Combine(runDirectory, InteractionStoragePaths.PosesFileName),
                    queueCapacity,
                    "SignVR Interaction Poses Writer",
                    budgetPolicy.Poses,
                    diskBudget
                );
                try
                {
                    objects = channelFactory.Create(
                        Path.Combine(runDirectory, "." + InteractionStoragePaths.ObjectsFileName + ".partial"),
                        Path.Combine(runDirectory, InteractionStoragePaths.ObjectsFileName),
                        queueCapacity,
                        "SignVR Interaction Objects Writer",
                        budgetPolicy.Objects,
                        diskBudget
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
            ioScheduler = new InteractionSerialBackgroundScheduler(
                "SignVR Interaction Capture I/O"
            );
        }

        public string RunDirectory { get; }
        public string RunId => runId;
        public bool CaptureActive => captureActive;
        public bool IsSealed => sealedCapture;
        public long CaptureGapCount => captureGapCount;
        public long NextPoseSequence => nextPoseSequence;
        public long NextObjectSequence => nextObjectSequence;
        public byte[] ManifestBytes => (byte[])manifestBytes.Clone();

        public static InteractionBackgroundOperation<InteractionCaptureWriter>
            BeginCreateNew(
            string persistentDataPath,
            RunPlan plan,
            byte[] exactManifestBytes,
            int queueCapacity = DefaultQueueCapacity,
            double gapThresholdSeconds = DefaultGapThresholdSeconds,
            InteractionCaptureBudgetPolicy budgetPolicy = null,
            InteractionCaptureQualityThresholds qualityThresholds = null)
        {
            byte[] frozenManifest = exactManifestBytes == null
                ? null
                : (byte[])exactManifestBytes.Clone();
            return InteractionBackgroundOperation<InteractionCaptureWriter>
                .Start(() => CreateNewOnWorker(
                    persistentDataPath,
                    plan,
                    frozenManifest,
                    queueCapacity,
                    gapThresholdSeconds,
                    new InteractionJsonlChannelFactory(),
                    budgetPolicy ?? InteractionCaptureBudgetPolicy.CreateDefault(),
                    qualityThresholds ??
                        InteractionCaptureQualityThresholds.CreateDefault()
                ));
        }

        internal static InteractionCaptureWriter CreateNew(
            string persistentDataPath,
            RunPlan plan,
            byte[] exactManifestBytes,
            int queueCapacity,
            double gapThresholdSeconds,
            IInteractionJsonlChannelFactory channelFactory,
            InteractionCaptureBudgetPolicy budgetPolicy = null,
            InteractionCaptureQualityThresholds qualityThresholds = null)
        {
            return CreateNewOnWorker(
                persistentDataPath,
                plan,
                exactManifestBytes,
                queueCapacity,
                gapThresholdSeconds,
                channelFactory,
                budgetPolicy ?? InteractionCaptureBudgetPolicy.CreateDefault(),
                qualityThresholds ??
                    InteractionCaptureQualityThresholds.CreateDefault()
            );
        }

        private static InteractionCaptureWriter CreateNewOnWorker(
            string persistentDataPath,
            RunPlan plan,
            byte[] exactManifestBytes,
            int queueCapacity,
            double gapThresholdSeconds,
            IInteractionJsonlChannelFactory channelFactory,
            InteractionCaptureBudgetPolicy budgetPolicy,
            InteractionCaptureQualityThresholds qualityThresholds)
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
            var diskBudget = new InteractionDiskBudgetGuard(
                runDirectory,
                budgetPolicy.MinimumFreeBytes,
                budgetPolicy.FreeSpaceProbe
            );
            diskBudget.EnsureMinimumAvailable();
            diskBudget.Reserve(exactManifestBytes.LongLength);
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
                    channelFactory,
                    budgetPolicy,
                    diskBudget,
                    qualityThresholds,
                    CollectRequiredProbeIds(plan)
                );
            }
            catch
            {
                // Never delete the directory here. A published manifest or a
                // partial stream is evidence needed for restart recovery.
                throw;
            }
        }

        private static IReadOnlyCollection<string> CollectRequiredProbeIds(
            RunPlan plan)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int phaseIndex = 0;
                phaseIndex < plan.Phases.Count;
                phaseIndex++)
            {
                IReadOnlyList<string> targetIds =
                    plan.Phases[phaseIndex].TaskVariant.TargetIds;
                for (int targetIndex = 0;
                    targetIndex < targetIds.Count;
                    targetIndex++)
                {
                    result.Add(targetIds[targetIndex]);
                }
            }
            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    "Run Plan contains no required target probes."
                );
            }
            return result;
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
            // Domain events never wait or silently drop on the caller. They
            // queue immediately or fail closed, preserving partial data.
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
            poseAttemptCount = checked(poseAttemptCount + 1L);
            lastPoseMonotonic = sample.MonotonicTimeSeconds;
            lastPoseFrame = sample.Frame;
            bool accepted = poses.TryWrite(
                InteractionCaptureJson.SerializePose(runId, sample)
            );
            if (accepted)
            {
                RecordAcceptedPoseQuality(sample);
            }
            else
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
            if (accepted)
            {
                if (requiredProbeSampleCounts.TryGetValue(
                        sample.ObjectId,
                        out long acceptedRequiredSamples))
                {
                    requiredProbeSampleCounts[sample.ObjectId] = checked(
                        acceptedRequiredSamples + 1L
                    );
                }
            }
            else
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
            BeginPhaseCheckpoint();
        }

        public InteractionBackgroundOperation<bool> BeginPhaseCheckpoint()
        {
            EnsureOpen();
            return ioScheduler.Enqueue(() =>
            {
                diskBudget.EnsureMinimumAvailable();
                events.FlushAndSync();
                poses.FlushAndSync();
                objects.FlushAndSync();
                return true;
            });
        }

        public bool CanSealCompleted(out string reason)
        {
            if (poses.AcceptedLineCount < 1L)
            {
                reason = "Completed Study capture requires at least one pose row.";
                return false;
            }
            if (objects.AcceptedLineCount < 1L)
            {
                reason = "Completed Study capture requires at least one object row.";
                return false;
            }
            reason = null;
            return true;
        }

        public InteractionBackgroundOperation<InteractionCaptureSealResult>
            BeginSeal(
            InteractionCaptureTerminalKind terminalKind,
            Func<InteractionDataCompleteness, InteractionRunSummary> summaryFactory)
        {
            InteractionRuntimeDiagnosticTrace.Write(
                "capture_writer_begin_seal",
                "run_id=" + runId + "; kind=" + terminalKind +
                "; stack=" + Environment.StackTrace
            );
            EnsureOpen();
            if (summaryFactory == null)
            {
                throw new ArgumentNullException(nameof(summaryFactory));
            }

            if (terminalKind != InteractionCaptureTerminalKind.Completed &&
                terminalKind != InteractionCaptureTerminalKind.Aborted)
            {
                throw new ArgumentOutOfRangeException(nameof(terminalKind));
            }
            captureActive = false;
            terminalRequested = true;
            return ioScheduler.Enqueue(
                () => SealOnWorker(terminalKind, summaryFactory),
                terminal: true
            );
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            InteractionRuntimeDiagnosticTrace.Write(
                "capture_writer_dispose",
                "run_id=" + runId + "; sealed=" + sealedCapture +
                "; terminal_requested=" + terminalRequested +
                "; stack=" + Environment.StackTrace
            );
            disposed = true;
            captureActive = false;
            if (!sealedCapture && !terminalRequested)
            {
                terminalRequested = true;
                try
                {
                    ioScheduler.Enqueue(() =>
                    {
                        CloseAllLeavingPartialOnWorker();
                        return true;
                    }, terminal: true);
                }
                catch
                {
                    RequestAllCloseWithoutJoin();
                    ioScheduler.StopAcceptingWithoutJoin();
                }
            }
        }

        private InteractionCaptureSealResult SealOnWorker(
            InteractionCaptureTerminalKind terminalKind,
            Func<InteractionDataCompleteness, InteractionRunSummary> summaryFactory)
        {
            try
            {
                diskBudget.EnsureMinimumAvailable();
                if (terminalKind == InteractionCaptureTerminalKind.Completed &&
                    !CanSealCompleted(out string completenessReason))
                {
                    throw new InteractionCaptureCompletenessException(
                        completenessReason
                    );
                }

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
                summary = summary.WithCaptureQuality(BuildCaptureQuality());
                byte[] summaryBytes = new UTF8Encoding(false).GetBytes(
                    InteractionSummaryJson.Serialize(summary)
                );
                if (summaryBytes.LongLength > budgetPolicy.SummaryMaxBytes)
                {
                    throw new InteractionCaptureBudgetException(
                        "summary.json exceeds the local byte limit."
                    );
                }
                diskBudget.Reserve(summaryBytes.LongLength);
                InteractionAtomicFile.WriteNew(
                    Path.Combine(
                        RunDirectory,
                        InteractionStoragePaths.SummaryFileName
                    ),
                    summaryBytes
                );
                sealedCapture = true;
                return new InteractionCaptureSealResult(summary, completeness);
            }
            catch
            {
                CloseAllLeavingPartialOnWorker();
                throw;
            }
        }

        private void CloseAllLeavingPartialOnWorker()
        {
            DisposeLeavingPartial(events);
            DisposeLeavingPartial(poses);
            DisposeLeavingPartial(objects);
        }

        private void RequestAllCloseWithoutJoin()
        {
            events.RequestCloseLeavingPartial();
            poses.RequestCloseLeavingPartial();
            objects.RequestCloseLeavingPartial();
        }

        private void RecordAcceptedPoseQuality(InteractionPoseSample sample)
        {
            if (acceptedPoseSampleCount == 0L)
            {
                firstAcceptedPoseMonotonic = sample.MonotonicTimeSeconds;
            }
            lastAcceptedPoseMonotonic = sample.MonotonicTimeSeconds;
            acceptedPoseSampleCount = checked(acceptedPoseSampleCount + 1L);
            if (sample.HmdValid)
            {
                hmdValidSampleCount = checked(hmdValidSampleCount + 1L);
            }
            if (sample.LeftHand.Tracked && sample.LeftHand.DataValid)
            {
                leftHandValidSampleCount = checked(
                    leftHandValidSampleCount + 1L
                );
            }
            if (sample.RightHand.Tracked && sample.RightHand.DataValid)
            {
                rightHandValidSampleCount = checked(
                    rightHandValidSampleCount + 1L
                );
            }
        }

        private InteractionCaptureQuality BuildCaptureQuality()
        {
            double actualSampleRateHz = 0d;
            if (acceptedPoseSampleCount > 1L &&
                lastAcceptedPoseMonotonic > firstAcceptedPoseMonotonic)
            {
                actualSampleRateHz = (acceptedPoseSampleCount - 1L) /
                    (lastAcceptedPoseMonotonic - firstAcceptedPoseMonotonic);
            }

            double hmdValidityRate = Rate(
                hmdValidSampleCount,
                acceptedPoseSampleCount
            );
            double leftHandValidityRate = Rate(
                leftHandValidSampleCount,
                acceptedPoseSampleCount
            );
            double rightHandValidityRate = Rate(
                rightHandValidSampleCount,
                acceptedPoseSampleCount
            );

            double acceptedRequiredProbeSamples = 0d;
            foreach (long acceptedSamples in
                     requiredProbeSampleCounts.Values)
            {
                acceptedRequiredProbeSamples += Math.Min(
                    acceptedSamples,
                    poseAttemptCount
                );
            }
            double expectedRequiredProbeSamples =
                poseAttemptCount * (double)requiredProbeSampleCounts.Count;
            double requiredProbeCoverageRate =
                expectedRequiredProbeSamples <= 0d
                    ? 0d
                    : acceptedRequiredProbeSamples /
                        expectedRequiredProbeSamples;

            return qualityThresholds.Evaluate(
                actualSampleRateHz,
                hmdValidityRate,
                leftHandValidityRate,
                rightHandValidityRate,
                requiredProbeCoverageRate,
                requiredProbeSampleCounts.Count,
                captureGapCount
            );
        }

        private static double Rate(long numerator, long denominator)
        {
            return denominator <= 0L
                ? 0d
                : numerator / (double)denominator;
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
            if (disposed || sealedCapture || terminalRequested)
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
            string workerName,
            InteractionJsonlBudget budget,
            InteractionDiskBudgetGuard diskBudget);
    }

    internal interface IInteractionJsonlChannel
    {
        long AcceptedLineCount { get; }
        bool TryWrite(string line);
        void WriteCritical(string line);
        void FlushAndSync();
        void CloseAndPromote();
        void DisposeLeavingPartial();
        void RequestCloseLeavingPartial();
    }

    internal sealed class InteractionJsonlChannelFactory :
        IInteractionJsonlChannelFactory
    {
        public IInteractionJsonlChannel Create(
            string partialPath,
            string finalPath,
            int capacity,
            string workerName,
            InteractionJsonlBudget budget,
            InteractionDiskBudgetGuard diskBudget)
        {
            return new InteractionAsyncJsonlChannel(
                partialPath,
                finalPath,
                capacity,
                workerName,
                budget,
                diskBudget
            );
        }
    }

    internal sealed class InteractionAsyncJsonlChannel :
        IInteractionJsonlChannel
    {
        private readonly object gate = new object();
        private readonly object ioGate = new object();
        private readonly Queue<QueuedLine> pending = new Queue<QueuedLine>();
        private readonly ManualResetEventSlim workerReady =
            new ManualResetEventSlim(false);
        private readonly int capacity;
        private readonly string partialPath;
        private readonly string finalPath;
        private FileStream stream;
        private readonly Thread worker;
        private readonly string workerName;
        private readonly InteractionJsonlBudgetTracker byteBudget;
        private readonly InteractionDiskBudgetGuard diskBudget;

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
            string workerName,
            InteractionJsonlBudget budget,
            InteractionDiskBudgetGuard diskBudget)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            this.partialPath = partialPath;
            this.finalPath = finalPath;
            this.capacity = capacity;
            this.workerName = workerName;
            byteBudget = new InteractionJsonlBudgetTracker(
                budget ?? throw new ArgumentNullException(nameof(budget))
            );
            this.diskBudget = diskBudget ??
                throw new ArgumentNullException(nameof(diskBudget));
            using (new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read))
            {
                // Only publish the empty partial here. Android IL2CPP may end
                // the lifetime of a FileStream that crosses from the capture
                // initialization worker to a separately-created writer
                // thread. The writer thread opens and owns its own stream.
            }
            worker = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = workerName
            };
            worker.Start();
            if (!workerReady.Wait(InteractionCaptureWaitLimits.CloseMilliseconds))
            {
                RequestCloseLeavingPartial();
                throw new IOException(
                    "Timed out while opening an Interaction capture stream."
                );
            }
            lock (gate)
            {
                ThrowIfFailed();
            }
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
                InteractionJsonlReservation reservation = byteBudget.Reserve(line);
                try
                {
                    diskBudget.Reserve(reservation.ByteCount);
                }
                catch
                {
                    byteBudget.Rollback(reservation);
                    throw;
                }
                pending.Enqueue(new QueuedLine(line, reservation));
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
                ThrowIfFailed();
                if (!accepting)
                {
                    throw new ObjectDisposedException(
                        nameof(InteractionAsyncJsonlChannel)
                    );
                }
                if (pending.Count >= capacity)
                {
                    throw new InteractionCaptureBudgetException(
                        "Critical Interaction event queue is full; partial data is retained."
                    );
                }
                InteractionJsonlReservation reservation = byteBudget.Reserve(line);
                try
                {
                    diskBudget.Reserve(reservation.ByteCount);
                }
                catch
                {
                    byteBudget.Rollback(reservation);
                    throw;
                }
                pending.Enqueue(new QueuedLine(line, reservation));
                acceptedLineCount++;
                Monitor.PulseAll(gate);
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
                stream.Flush(true);
                GC.KeepAlive(stream);
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

        public void RequestCloseLeavingPartial()
        {
            InteractionRuntimeDiagnosticTrace.WriteBackground(
                "jsonl_close_requested",
                "worker=" + workerName + "; caller=" + Environment.StackTrace
            );
            lock (gate)
            {
                accepting = false;
                closeRequested = true;
                Monitor.PulseAll(gate);
            }
        }

        private void StopWorker()
        {
            InteractionRuntimeDiagnosticTrace.WriteBackground(
                "jsonl_stop_worker",
                "worker=" + workerName + "; caller=" + Environment.StackTrace
            );
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
            FileStream ownedStream = null;
            try
            {
                ownedStream = new FileStream(
                    partialPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.Read,
                    65536,
                    FileOptions.SequentialScan
                );
                ownedStream.Seek(0L, SeekOrigin.End);
                lock (ioGate)
                {
                    stream = ownedStream;
                }
                workerReady.Set();
                InteractionRuntimeDiagnosticTrace.WriteBackground(
                    "jsonl_worker_stream_opened",
                    "worker=" + workerName
                );
                while (true)
                {
                    QueuedLine queued;
                    lock (gate)
                    {
                        while (pending.Count == 0 && !closeRequested)
                        {
                            Monitor.Wait(gate, 50);
                        }
                        if (pending.Count == 0 && closeRequested)
                        {
                            InteractionRuntimeDiagnosticTrace.WriteBackground(
                                "jsonl_worker_exit_requested",
                                "worker=" + workerName +
                                "; accepted=" + acceptedLineCount +
                                "; written=" + writtenLineCount
                            );
                            break;
                        }
                        queued = pending.Dequeue();
                        Monitor.PulseAll(gate);
                    }
                    lock (ioGate)
                    {
                        byte[] encodedLine = Encoding.UTF8.GetBytes(
                            queued.Line + "\n"
                        );
                        ownedStream.Write(
                            encodedLine,
                            0,
                            encodedLine.Length
                        );
                        byteBudget.MarkWritten(queued.Reservation);
                        GC.KeepAlive(ownedStream);
                    }
                    lock (gate)
                    {
                        writtenLineCount++;
                        Monitor.PulseAll(gate);
                    }
                }
                lock (ioGate)
                {
                    ownedStream.Flush(true);
                    GC.KeepAlive(ownedStream);
                }
            }
            catch (Exception exception)
            {
                workerReady.Set();
                InteractionRuntimeDiagnosticTrace.WriteBackground(
                    "jsonl_worker_loop_failed",
                    "worker=" + workerName,
                    exception
                );
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
                    lock (ioGate)
                    {
                        stream = null;
                        ownedStream?.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    InteractionRuntimeDiagnosticTrace.WriteBackground(
                        "jsonl_worker_dispose_failed",
                        "worker=" + workerName,
                        exception
                    );
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

        private sealed class QueuedLine
        {
            public QueuedLine(
                string line,
                InteractionJsonlReservation reservation)
            {
                Line = line;
                Reservation = reservation;
            }

            public string Line { get; }
            public InteractionJsonlReservation Reservation { get; }
        }
    }

}
