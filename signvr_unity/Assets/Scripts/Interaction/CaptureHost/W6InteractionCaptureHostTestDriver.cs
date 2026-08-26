#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Editor-only deterministic scenarios invoked by the W6 EditMode tests
    /// through reflection because Unity asmdef test assemblies cannot directly
    /// reference predefined Assembly-CSharp.
    /// </summary>
    public static class W6InteractionCaptureHostTestDriver
    {
        internal static readonly DateTimeOffset FixedUtc =
            new DateTimeOffset(2026, 8, 26, 10, 15, 30, TimeSpan.Zero);

        public static void ManifestMatchesFrozenContract()
        {
            var allocator = new AssistanceBlockAllocator(91);
            var generator = CreateGenerator();
            var conditions = new HashSet<string>(StringComparer.Ordinal);
            for (int runIndex = 0; runIndex < 3; runIndex++)
            {
                RunPlan plan = generator.Generate(
                    CreateRequest(100 + runIndex, "P001"),
                    allocator.AllocateNext()
                );
                byte[] bytes = InteractionRunManifestContractV1.SerializeUtf8(plan);
                IDictionary<string, object> json = InteractionJson.ParseObject(
                    new UTF8Encoding(false, true).GetString(bytes)
                );
                string[] exactTopLevel =
                {
                    "schema_version", "batch_id", "participant_id", "run_id",
                    "app_session_id", "created_utc", "app_version", "git_commit",
                    "seed", "assistance_condition", "condition_assignment",
                    "safe_password", "chest_button_order", "phases"
                };
                Require(
                    new HashSet<string>(json.Keys, StringComparer.Ordinal)
                        .SetEquals(exactTopLevel),
                    "Manifest top-level fields are not exact."
                );
                Require(
                    InteractionJson.RequireInt32(json, "schema_version") == 1,
                    "schema_version mismatched."
                );
                IList<object> phases = InteractionJson.RequireArray(json, "phases");
                Require(phases.Count == 6, "Manifest must contain six phases.");
                for (int index = 0; index < phases.Count; index++)
                {
                    var phase = (IDictionary<string, object>)phases[index];
                    Require(
                        InteractionJson.RequireInt32(phase, "phase_id") == index + 1,
                        "Phase order mismatched."
                    );
                    string sentence = InteractionJson.RequireString(
                        phase,
                        "sentence_id"
                    );
                    Require(
                        PhaseSentenceRanges.ForPhase(index + 1).Contains(sentence),
                        "Sentence fell outside its phase range."
                    );
                    Require(
                        InteractionJson.RequireString(phase, "signer_id") == "wang" &&
                        InteractionJson.RequireString(phase, "take_id").Length > 0 &&
                        InteractionJson.RequireString(phase, "artifact_path").Length > 0 &&
                        InteractionJson.RequireString(phase, "artifact_sha256").Length == 64,
                        "Resolved content fields are incomplete."
                    );
                    IDictionary<string, object> variant =
                        InteractionJson.RequireObject(phase, "task_variant");
                    Require(
                        InteractionJson.RequireString(variant, "variant_id").Length > 0 &&
                        InteractionJson.RequireArray(variant, "target_ids").Count > 0 &&
                        variant.ContainsKey("ordered_target_ids"),
                        "Task Variant fields are incomplete."
                    );
                }
                conditions.Add(InteractionJson.RequireString(
                    json,
                    "assistance_condition"
                ));
            }
            Require(conditions.SetEquals(new[]
            {
                "TextAndPointing", "TextOnly", "SignOnly"
            }), "One assistance block must contain all three conditions.");
        }

        public static void ScheduledStartGateBlocksPreStartCapture()
        {
            var gate = new InteractionLocalScheduledStartGate(
                FixedUtc.AddSeconds(2),
                FixedUtc,
                10d
            );
            Require(!gate.IsDue(11.999d), "Capture opened before start_at.");
            Require(gate.IsDue(12d), "Capture did not open at start_at.");

            var cadence = new InteractionCaptureCadence(0.05d);
            int samplesAt72Hz = 0;
            for (int frame = 0; frame < 72; frame++)
            {
                if (cadence.ShouldSample(frame / 72d, true))
                {
                    samplesAt72Hz++;
                }
            }
            Require(
                samplesAt72Hz == 20,
                "A 72 Hz render loop did not produce exactly 20 capture groups."
            );

            cadence.Reset();
            int samplesAt90Hz = 0;
            for (int frame = 0; frame < 90; frame++)
            {
                if (cadence.ShouldSample(frame / 90d, true))
                {
                    samplesAt90Hz++;
                }
            }
            Require(
                samplesAt90Hz == 20,
                "A 90 Hz render loop did not produce exactly 20 capture groups."
            );

            cadence.Reset();
            Require(
                cadence.ShouldSample(10d, true) &&
                cadence.ShouldSample(10.75d, true) &&
                !cadence.ShouldSample(10.75d, true),
                "A long frame caused burst catch-up in one Update."
            );
            Require(
                !cadence.ShouldSample(11d, false) &&
                cadence.ShouldSample(11d, true),
                "Inactive capture did not reset cadence for an immediate new-Run sample."
            );
        }

        public static void PresentationHandshakeUsesActualPlaybackOrigin()
        {
            var machine = new InteractionRunStateMachine(
                new AssistanceBlockAllocator(92),
                CreateGenerator()
            );
            RunPlan plan = machine.Start(CreateRequest(93, "P001"));
            machine.Schedule(FixedUtc.AddSeconds(2));
            var handshake = new InteractionPresentationHandshake(machine);
            InteractionPresentationRequest first =
                handshake.RequestInitialPlayback();
            Require(
                machine.State == RunState.Scheduled &&
                handshake.PendingRequest == first,
                "A scheduled-due request falsely started W1 playback."
            );
            InteractionPresentationStart initial =
                handshake.AcknowledgePlaybackStarted(
                    first.RequestSequence,
                    first.PhaseId,
                    first.PlaybackKind,
                    12.5d
                );
            Require(
                initial.IsInitialRunStart && machine.State == RunState.Running &&
                machine.CurrentPhase.FirstPlaybackStartedAt ==
                    TimeSpan.FromSeconds(12.5d),
                "W1 did not use the actual first-frame callback as its origin."
            );

            machine.FirstPlaybackCompleted();
            InteractionPresentationRequest second =
                handshake.RequestNextPhasePlayback(
                    stuck: false,
                    phaseCompletedAtSeconds: 14d
                );
            Require(
                machine.CurrentPhaseId == 1 &&
                machine.CurrentPhase.State == PhaseState.Active,
                "Task completion falsely started the next instruction."
            );
            handshake.AcknowledgePlaybackStarted(
                second.RequestSequence,
                second.PhaseId,
                second.PlaybackKind,
                20d
            );
            Require(
                machine.CurrentPhaseId == 2 &&
                machine.CurrentPhase.FirstPlaybackStartedAt ==
                    TimeSpan.FromSeconds(20d),
                "Next-phase W1 origin was not the real playback callback."
            );
            ExpectThrows<InvalidOperationException>(() =>
                handshake.AcknowledgePlaybackStarted(
                    second.RequestSequence,
                    second.PhaseId,
                    second.PlaybackKind,
                    21d
                )
            );
        }

        public static void StudyCapturePrerequisitesFailClosed()
        {
            ExpectThrows<InvalidOperationException>(() =>
                InteractionStudyCapturePrerequisites.Validate(
                    hmdReady: false,
                    leftHandReady: true,
                    rightHandReady: true,
                    objectProbeCount: 1
                )
            );
            ExpectThrows<InvalidOperationException>(() =>
                InteractionStudyCapturePrerequisites.Validate(
                    hmdReady: true,
                    leftHandReady: false,
                    rightHandReady: true,
                    objectProbeCount: 1
                )
            );
            ExpectThrows<InvalidOperationException>(() =>
                InteractionStudyCapturePrerequisites.Validate(
                    hmdReady: true,
                    leftHandReady: true,
                    rightHandReady: true,
                    objectProbeCount: 0
                )
            );
            InteractionStudyCapturePrerequisites.Validate(
                hmdReady: true,
                leftHandReady: true,
                rightHandReady: true,
                objectProbeCount: 1
            );
        }

        public static void CompleteAndAbortSealAllLocalFiles()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan completedPlan = CreatePlan(31, "P001");
                InteractionCaptureWriter completed = CreateWriter(
                    root,
                    completedPlan
                );
                var completedSummary = new InteractionSummaryTracker(
                    completedPlan.RunId
                );
                completed.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                completed.BeginCapture();
                completedSummary.BeginRun(1d, FixedUtc.AddSeconds(1));
                for (int phaseId = 1; phaseId <= 6; phaseId++)
                {
                    double phaseStart = phaseId;
                    completedSummary.BeginPhase(phaseId, phaseStart);
                    completedSummary.RecordAttempt(
                        phaseId,
                        true,
                        phaseStart + 0.1d
                    );
                    completedSummary.FinishPhase(
                        phaseId,
                        true,
                        false,
                        phaseStart + 0.5d
                    );
                }
                WriteOnePoseAndObject(completed);
                completed.RecordEvent(
                    InteractionEventNames.RunCompleted,
                    null,
                    7d,
                    FixedUtc.AddSeconds(7),
                    7
                );
                Await(completed.BeginSeal(
                    InteractionCaptureTerminalKind.Completed,
                    data => completedSummary.SealCompleted(
                        7d,
                        FixedUtc.AddSeconds(7),
                        data
                    )
                ));
                AssertFinalFiles(completed.RunDirectory);

                RunPlan abortedPlan = CreatePlan(32, "P002");
                InteractionCaptureWriter aborted = CreateWriter(root, abortedPlan);
                var abortedSummary = new InteractionSummaryTracker(
                    abortedPlan.RunId
                );
                aborted.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                aborted.RecordEvent(
                    InteractionEventNames.RunAborted,
                    null,
                    0.1d,
                    FixedUtc.AddMilliseconds(100),
                    1,
                    payloadJson: "{\"reason\":\"safety\"}"
                );
                Await(aborted.BeginSeal(
                    InteractionCaptureTerminalKind.Aborted,
                    data => abortedSummary.SealAborted(
                        0.1d,
                        FixedUtc.AddMilliseconds(100),
                        "safety",
                        data
                    )
                ));
                AssertFinalFiles(aborted.RunDirectory);
                IDictionary<string, object> abortJson = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(Path.Combine(
                        aborted.RunDirectory,
                        InteractionStoragePaths.SummaryFileName
                    ))
                );
                Require(
                    InteractionJson.RequireString(abortJson, "status") == "aborted",
                    "Abort summary status mismatched."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void EventSequenceAndMonotonicTimeAreStrict()
        {
            var sequencer = new InteractionEventSequencer("run_sequence_test");
            InteractionEventRecord first = sequencer.Create(
                InteractionEventNames.RunCreated,
                null,
                1d,
                FixedUtc,
                10
            );
            InteractionEventRecord second = sequencer.Create(
                InteractionEventNames.RunStarted,
                null,
                1d,
                FixedUtc,
                10
            );
            Require(
                first.EventSequence == 1L && second.EventSequence == 2L,
                "event_seq is not strictly increasing."
            );
            ExpectThrows<ArgumentOutOfRangeException>(() => sequencer.Create(
                InteractionEventNames.RunStarted,
                null,
                0.9d,
                FixedUtc,
                11
            ));
            IDictionary<string, object> serialized = InteractionJson.ParseObject(
                InteractionCaptureJson.SerializeEvent(second)
            );
            string[] requiredFields =
            {
                "schema_version", "run_id", "phase_id", "event_seq",
                "monotonic_time_s", "utc_time", "frame", "event_type",
                "actor_id", "target_id", "payload"
            };
            Require(
                new HashSet<string>(serialized.Keys).SetEquals(requiredFields),
                "Event envelope fields are not exact."
            );
        }

        public static void EventSinkSeamWorksWithFakeAdapter()
        {
            var fake = new FakeEventSink();
            IInteractionEventSink sink = fake;
            sink.RecordEvent(
                InteractionEventNames.InteractionAttempt,
                1,
                1d,
                FixedUtc,
                1,
                "left_hand",
                "target_a",
                "{\"correct\":true}"
            );
            sink.FlushPhase();
            Require(
                fake.EventTypes.SequenceEqual(new[]
                {
                    InteractionEventNames.InteractionAttempt
                }) && fake.FlushCount == 1,
                "Low-coupling event sink seam did not accept a fake adapter."
            );
        }

        public static void BufferOverflowEmitsCaptureGap()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan plan = CreatePlan(40, "P001");
                byte[] bytes = InteractionRunManifestContractV1.SerializeUtf8(plan);
                var factory = new FakeChannelFactory(rejectPoses: true);
                InteractionCaptureWriter writer = InteractionCaptureWriter.CreateNew(
                    root,
                    plan,
                    bytes,
                    1,
                    0.25d,
                    factory
                );
                writer.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                writer.BeginCapture();
                bool accepted = writer.TryWritePose(CreatePose(1L, 1d, 1));
                Require(!accepted, "Rejecting pose channel accepted a sample.");
                Require(
                    factory.Events.Lines.Any(line =>
                        line.IndexOf(
                            "\"event_type\":\"capture_gap\"",
                            StringComparison.Ordinal
                        ) >= 0 && line.IndexOf(
                            "poses_buffer_overflow",
                            StringComparison.Ordinal
                        ) >= 0),
                    "Pose overflow did not emit capture_gap."
                );
                writer.Dispose();
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void EveryPhaseFlushFlushesAllStreams()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan plan = CreatePlan(41, "P001");
                byte[] bytes = InteractionRunManifestContractV1.SerializeUtf8(plan);
                var factory = new FakeChannelFactory(rejectPoses: false);
                InteractionCaptureWriter writer = InteractionCaptureWriter.CreateNew(
                    root,
                    plan,
                    bytes,
                    2,
                    0.25d,
                    factory
                );
                Await(writer.BeginPhaseCheckpoint());
                Require(
                    factory.Events.FlushCount == 1 &&
                    factory.Poses.FlushCount == 1 &&
                    factory.Objects.FlushCount == 1,
                    "Phase flush did not flush all three streams."
                );
                writer.Dispose();
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void WriterWaitsHaveShortFiniteLimits()
        {
            Require(
                InteractionCaptureWaitLimits.CriticalEnqueueMilliseconds > 0 &&
                InteractionCaptureWaitLimits.CriticalEnqueueMilliseconds <= 1000 &&
                InteractionCaptureWaitLimits.DrainMilliseconds > 0 &&
                InteractionCaptureWaitLimits.DrainMilliseconds <= 1000 &&
                InteractionCaptureWaitLimits.CloseMilliseconds > 0 &&
                InteractionCaptureWaitLimits.CloseMilliseconds <= 1000,
                "Capture backpressure or shutdown has an unbounded/long wait."
            );
        }

        public static void BackgroundOperationRunsOffCallingThread()
        {
            int callingThread = Thread.CurrentThread.ManagedThreadId;
            using (var release = new ManualResetEventSlim(false))
            {
                InteractionBackgroundOperation<int> operation =
                    InteractionBackgroundOperation<int>.Start(() =>
                    {
                        release.Wait();
                        return Thread.CurrentThread.ManagedThreadId;
                    });
                Require(
                    operation.CallingThreadId == callingThread &&
                    !operation.IsCompleted,
                    "Starting background work blocked the calling thread."
                );
                release.Set();
                Require(
                    operation.Wait(TimeSpan.FromSeconds(2)) &&
                    operation.Succeeded &&
                    operation.GetResult() != callingThread &&
                    operation.WorkerThreadId == operation.GetResult(),
                    "Background work executed on the calling thread."
                );
            }

            bool rejectedWorkExecuted = false;
            InteractionBackgroundOperation<int> rejected =
                InteractionBackgroundOperation<int>.Start(
                    () =>
                    {
                        rejectedWorkExecuted = true;
                        return 1;
                    },
                    new RejectingBackgroundWorkQueue()
                );
            Require(
                rejected.Wait(TimeSpan.Zero) && rejected.IsCompleted &&
                !rejected.Succeeded &&
                rejected.Error is InvalidOperationException &&
                !rejectedWorkExecuted,
                "A rejected ThreadPool enqueue remained pending or ran work."
            );

            var observedCompletion =
                InteractionBackgroundOperation<int>.CreatePending();
            int secondObserverCalls = 0;
            Exception observerFanoutFailure = null;
            observedCompletion.ObserveCompletion((_, __) =>
                throw new InvalidOperationException(
                    "Injected completion observer failure."
                ));
            observedCompletion.ObserveCompletion((value, failure) =>
            {
                if (value == 17 && failure == null)
                {
                    Interlocked.Increment(ref secondObserverCalls);
                }
            });
            try
            {
                observedCompletion.Complete(17, null);
            }
            catch (Exception exception)
            {
                observerFanoutFailure = exception;
            }
            int lateObserverCalls = 0;
            Exception lateObserverFailure = null;
            try
            {
                observedCompletion.ObserveCompletion((_, __) =>
                    throw new InvalidOperationException(
                        "Injected late completion observer failure."
                    ));
            }
            catch (Exception exception)
            {
                lateObserverFailure = exception;
            }
            observedCompletion.ObserveCompletion((value, failure) =>
            {
                if (value == 17 && failure == null)
                {
                    Interlocked.Increment(ref lateObserverCalls);
                }
            });
            Require(
                observerFanoutFailure == null && lateObserverFailure == null &&
                secondObserverCalls == 1 && lateObserverCalls == 1 &&
                observedCompletion.Succeeded &&
                observedCompletion.GetResult() == 17,
                "A failing completion observer escaped or blocked another owner."
            );

            bool rejectedHandoffWorkExecuted = false;
            InteractionBackgroundHandoff<int> rejectedHandoff =
                InteractionBackgroundHandoff<int>.Start(
                    () =>
                    {
                        rejectedHandoffWorkExecuted = true;
                        return 2;
                    },
                    new RejectingBackgroundWorkQueue()
                );
            Require(
                rejectedHandoff.Wait(TimeSpan.Zero) &&
                rejectedHandoff.Error is InvalidOperationException &&
                rejectedHandoff.TryConsumeCompletion(
                    out int _,
                    out Exception handoffFailure
                ) && handoffFailure is InvalidOperationException &&
                !rejectedHandoff.TryConsumeCompletion(
                    out int _,
                    out Exception _
                ) &&
                !rejectedHandoffWorkExecuted,
                "A rejected handoff was not published once as a failure result."
            );

            var scheduler = new InteractionSerialBackgroundScheduler(
                "W6 rejected enqueue convergence test"
            );
            InteractionBackgroundOperation<int> accepted = scheduler.Enqueue(
                () => 41
            );
            Require(
                accepted.Wait(TimeSpan.FromSeconds(2)) &&
                accepted.Succeeded && accepted.GetResult() == 41,
                "Serial scheduler did not complete its accepted operation."
            );
            scheduler.StopAcceptingWithoutJoin();
            bool schedulerRejectedSynchronously = false;
            try
            {
                scheduler.Enqueue(() => 42);
            }
            catch (InvalidOperationException)
            {
                schedulerRejectedSynchronously = true;
            }
            Require(
                schedulerRejectedSynchronously,
                "A closing serial scheduler returned a permanently pending operation."
            );

        }

        public static void CheckpointAndSealRunSeriallyOffCallingThread()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan plan = CreatePlan(191, "P191");
                InteractionCaptureWriter writer = CreateWriter(root, plan);
                InteractionSummaryTracker summary = CreateCompletedSummary(
                    plan.RunId
                );
                writer.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                writer.BeginCapture();
                WriteOnePoseAndObject(writer);
                int callingThread = Thread.CurrentThread.ManagedThreadId;
                InteractionBackgroundOperation<bool> checkpoint =
                    writer.BeginPhaseCheckpoint();
                InteractionBackgroundOperation<InteractionCaptureSealResult>
                    seal = writer.BeginSeal(
                        InteractionCaptureTerminalKind.Completed,
                        completeness => summary.SealCompleted(
                            10d,
                            FixedUtc.AddSeconds(10),
                            completeness
                        )
                    );
                Require(
                    checkpoint.Wait(TimeSpan.FromSeconds(5)) &&
                    seal.Wait(TimeSpan.FromSeconds(5)) &&
                    checkpoint.Succeeded && seal.Succeeded &&
                    checkpoint.WorkerThreadId == seal.WorkerThreadId &&
                    seal.WorkerThreadId != callingThread,
                    "Checkpoint/seal did not use one serial background worker."
                );
                AssertFinalFiles(writer.RunDirectory);
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void CaptureBudgetsFailClosed()
        {
            var tracker = new InteractionJsonlBudgetTracker(
                new InteractionJsonlBudget(
                    maxLineBytes: 8L,
                    maxPendingBytes: 10L,
                    maxFileBytes: 16L
                )
            );
            InteractionJsonlReservation chinese = tracker.Reserve("中文");
            Require(
                chinese.ByteCount == 7L && tracker.PendingBytes == 7L &&
                tracker.AcceptedFileBytes == 7L,
                "UTF-8 byte accounting did not include the JSONL newline."
            );
            ExpectThrows<InteractionCaptureBudgetException>(() =>
                tracker.Reserve("abcd")
            );
            tracker.MarkWritten(chinese);
            InteractionJsonlReservation ascii = tracker.Reserve("abcd");
            tracker.MarkWritten(ascii);
            ExpectThrows<InteractionCaptureBudgetException>(() =>
                tracker.Reserve("12345")
            );
            ExpectThrows<InteractionCaptureBudgetException>(() =>
                new InteractionJsonlBudgetTracker(
                    new InteractionJsonlBudget(8L, 64L, 128L)
                ).Reserve("12345678")
            );

            string diskRoot = CreateTemporaryRoot();
            try
            {
                var lowDisk = new FakeFreeSpaceProbe(99L);
                var disk = new InteractionDiskBudgetGuard(
                    diskRoot,
                    minimumFreeBytes: 100L,
                    lowDisk
                );
                ExpectThrows<InteractionCaptureBudgetException>(() =>
                    disk.EnsureMinimumAvailable()
                );
            }
            finally
            {
                DeleteTemporaryRoot(diskRoot);
            }
        }

        public static void AndroidFreeSpaceProbeKeepsPersistentDataPath()
        {
            const string persistentDataPath =
                "/storage/emulated/0/Android/data/com.signvr.interaction/files";
            string selected = InteractionDriveFreeSpaceProbe.SelectProbePath(
                persistentDataPath,
                pathAware: true
            );
            Require(
                string.Equals(
                    selected,
                    persistentDataPath,
                    StringComparison.Ordinal
                ),
                "Android StatFs must inspect the full persistent-data path " +
                "instead of the read-only '/' filesystem."
            );
        }

        public static void AndroidFreeSpaceProbeUsesExistingAncestorForFutureRun()
        {
            string root = CreateTemporaryRoot();
            try
            {
                string futureRunDirectory = Path.Combine(
                    root,
                    "interaction-tests",
                    "batch",
                    "participant",
                    "run"
                );
                var resolver = typeof(InteractionDriveFreeSpaceProbe).GetMethod(
                    "ResolveExistingProbePath",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic
                );
                Require(
                    resolver != null,
                    "Android StatFs needs a resolver for capture directories " +
                    "that have not been created yet."
                );
                string selected = (string)resolver.Invoke(
                    null,
                    new object[] { futureRunDirectory }
                );
                Require(
                    string.Equals(
                        selected,
                        root,
                        StringComparison.OrdinalIgnoreCase
                    ),
                    "Android StatFs must inspect the nearest existing parent " +
                    "when the future Run directory does not exist."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void LowDiskSealRetainsPartial()
        {
            string root = CreateTemporaryRoot();
            try
            {
                var free = new FakeFreeSpaceProbe(1024L * 1024L);
                var budget = new InteractionCaptureBudgetPolicy(
                    new InteractionJsonlBudget(4096L, 65536L, 1024L * 1024L),
                    new InteractionJsonlBudget(4096L, 65536L, 1024L * 1024L),
                    new InteractionJsonlBudget(4096L, 65536L, 1024L * 1024L),
                    summaryMaxBytes: 65536L,
                    minimumFreeBytes: 4096L,
                    free
                );
                RunPlan plan = CreatePlan(192, "P192");
                byte[] manifest = InteractionRunManifestContractV1.SerializeUtf8(
                    plan
                );
                InteractionCaptureWriter writer = Await(
                    InteractionCaptureWriter.BeginCreateNew(
                        root,
                        plan,
                        manifest,
                        InteractionCaptureWriter.DefaultQueueCapacity,
                        InteractionCaptureWriter.DefaultGapThresholdSeconds,
                        budget
                    )
                );
                InteractionSummaryTracker summary = CreateCompletedSummary(
                    plan.RunId
                );
                writer.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                writer.BeginCapture();
                WriteOnePoseAndObject(writer);
                free.AvailableBytes = 0L;
                InteractionBackgroundOperation<InteractionCaptureSealResult>
                    seal = writer.BeginSeal(
                        InteractionCaptureTerminalKind.Completed,
                        value => summary.SealCompleted(
                            10d,
                            FixedUtc.AddSeconds(10),
                            value
                        )
                    );
                Require(
                    seal.Wait(TimeSpan.FromSeconds(5)) && !seal.Succeeded &&
                    seal.Error is InteractionCaptureBudgetException &&
                    !File.Exists(Path.Combine(
                        writer.RunDirectory,
                        InteractionStoragePaths.SummaryFileName
                    )) &&
                    Directory.GetFiles(writer.RunDirectory, "*.partial").Length > 0,
                        "Low disk sealed an incomplete Run instead of retaining partials."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void CompletedSealRejectsEmptyCaptureWhileAbortAllowsIt()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan completedPlan = CreatePlan(193, "P193");
                InteractionCaptureWriter completed = CreateWriter(
                    root,
                    completedPlan
                );
                InteractionSummaryTracker completedSummary =
                    CreateCompletedSummary(completedPlan.RunId);
                completed.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                completed.BeginCapture();
                InteractionBackgroundOperation<InteractionCaptureSealResult>
                    rejected = completed.BeginSeal(
                        InteractionCaptureTerminalKind.Completed,
                        value => completedSummary.SealCompleted(
                            10d,
                            FixedUtc.AddSeconds(10),
                            value
                        )
                    );
                Require(
                    rejected.Wait(TimeSpan.FromSeconds(5)) &&
                    !rejected.Succeeded &&
                    rejected.Error is InteractionCaptureCompletenessException &&
                    !File.Exists(Path.Combine(
                        completed.RunDirectory,
                        InteractionStoragePaths.SummaryFileName
                    )),
                    "Completed accepted empty poses/objects."
                );

                RunPlan abortedPlan = CreatePlan(194, "P194");
                InteractionCaptureWriter aborted = CreateWriter(root, abortedPlan);
                var abortedSummary = new InteractionSummaryTracker(
                    abortedPlan.RunId
                );
                aborted.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                InteractionBackgroundOperation<InteractionCaptureSealResult>
                    accepted = aborted.BeginSeal(
                        InteractionCaptureTerminalKind.Aborted,
                        value => abortedSummary.SealAborted(
                            1d,
                            FixedUtc.AddSeconds(1),
                            "test_abort",
                            value
                        )
                    );
                Require(
                    accepted.Wait(TimeSpan.FromSeconds(5)) && accepted.Succeeded,
                    "Abort did not permit explicitly empty capture streams."
                );
                AssertFinalFiles(aborted.RunDirectory);
                Require(
                    new FileInfo(Path.Combine(
                        aborted.RunDirectory,
                        InteractionStoragePaths.PosesFileName
                    )).Length == 0L &&
                    new FileInfo(Path.Combine(
                        aborted.RunDirectory,
                        InteractionStoragePaths.ObjectsFileName
                    )).Length == 0L,
                    "Abort fabricated pose/object capture rows."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void TerminalSealArbitrationPreventsDoubleSeal()
        {
            InteractionRunStateMachine abortMachine =
                CreateCompletingStateMachine(107, "P940");
            var abortDuringCheckpoint = new InteractionTerminalSealArbiter();
            abortDuringCheckpoint.BeginCheckpoint();
            InteractionAbortRequestDisposition accepted =
                abortDuringCheckpoint.TryRequestAbort(out string acceptedError);
            Require(
                accepted == InteractionAbortRequestDisposition.QueueAfterCheckpoint &&
                acceptedError == null &&
                abortDuringCheckpoint.State ==
                    InteractionTerminalSealArbitrationState
                        .AbortReservedAfterCheckpoint,
                "Abort during the final checkpoint was not reserved behind it."
            );
            abortMachine.AbortRun("operator_abort");
            Require(
                abortMachine.State == RunState.Aborting,
                "W1 did not enter Aborting exactly when Abort won arbitration."
            );
            InteractionCheckpointResolution abortResolution =
                abortDuringCheckpoint.CompleteCheckpoint();
            int abortSealCount = 0;
            int abortSummaryCount = 0;
            int abortTerminalEventCount = 0;
            if (abortResolution == InteractionCheckpointResolution.QueueAbort)
            {
                abortSealCount++;
                abortSummaryCount++;
                abortTerminalEventCount++;
                abortDuringCheckpoint.MarkAbortedSealQueued();
            }
            Require(
                abortSealCount == 1 && abortSummaryCount == 1 &&
                abortTerminalEventCount == 1 &&
                abortDuringCheckpoint.TryRequestAbort(out _) ==
                    InteractionAbortRequestDisposition.Rejected,
                "Checkpoint-window abort could schedule duplicate terminal output."
            );
            abortDuringCheckpoint.MarkTerminal();
            abortMachine.MarkRunAborted();
            Require(
                abortMachine.State == RunState.Aborted,
                "The single Aborted seal did not finish the W1 lifecycle."
            );

            InteractionRunStateMachine completedMachine =
                CreateCompletingStateMachine(108, "P941");
            var completedQueued = new InteractionTerminalSealArbiter();
            completedQueued.BeginCheckpoint();
            Require(
                completedQueued.CompleteCheckpoint() ==
                    InteractionCheckpointResolution.Continue,
                "A normal final checkpoint did not preserve completion."
            );
            int completedSealCount = 1;
            int completedSummaryCount = 1;
            int completedTerminalEventCount = 1;
            completedQueued.MarkCompletedSealQueued();
            InteractionAbortRequestDisposition rejected =
                completedQueued.TryRequestAbort(out string rejectedError);
            Require(
                rejected == InteractionAbortRequestDisposition.Rejected &&
                !string.IsNullOrWhiteSpace(rejectedError) &&
                completedQueued.State ==
                    InteractionTerminalSealArbitrationState.CompletedSealQueued &&
                completedSealCount == 1 && completedSummaryCount == 1 &&
                completedTerminalEventCount == 1,
                "Abort after a queued Completed seal changed terminal ownership."
            );
            Require(
                completedMachine.State == RunState.Completing,
                "Rejected late Abort changed W1 before the Completed seal finished."
            );
            completedQueued.MarkTerminal();
            completedMachine.MarkRunCompleted();
            Require(
                completedQueued.State ==
                    InteractionTerminalSealArbitrationState.Terminal &&
                completedMachine.State == RunState.Completed,
                "The original Completed seal did not retain terminal ownership."
            );
        }

        public static void LifecycleTerminalizationHandoffPublishesAtomically()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan plan = CreatePlan(109, "P942");
                InteractionCaptureWriter writer = CreateWriter(root, plan);
                var controllerOwnedSummary =
                    new InteractionSummaryTracker(plan.RunId);
                InteractionSummaryTracker detachedSummary =
                    controllerOwnedSummary.CreateDetachedCopy();
                int callingThread = Thread.CurrentThread.ManagedThreadId;
                using (var releaseInitialization =
                    new ManualResetEventSlim(false))
                {
                    InteractionBackgroundOperation<InteractionCaptureWriter>
                        initialization = InteractionBackgroundOperation<
                            InteractionCaptureWriter>.Start(() =>
                            {
                                releaseInitialization.Wait();
                                return writer;
                            });
                    InteractionLifecycleTerminalizationJob job =
                        InteractionLifecycleTerminalizationJob.Start(
                            writer: null,
                            initialization: initialization,
                            detachedSummary: detachedSummary,
                            abortReason: "component_disabled",
                            monotonicTimeSeconds: 0.5d,
                            utcTime: FixedUtc,
                            frame: 77
                        );
                    Require(
                        !job.TryConsume(out _),
                        "Lifecycle handoff published partial terminal metadata."
                    );
                    releaseInitialization.Set();
                    Require(
                        job.Wait(TimeSpan.FromSeconds(5)) &&
                        job.TryConsume(out InteractionLifecycleTerminalizationResult
                            result) &&
                        !job.TryConsume(out _) &&
                        result.Succeeded && result.Writer == writer &&
                        result.SealResult != null &&
                        result.TerminalKind ==
                            InteractionCaptureTerminalKind.Aborted &&
                        result.AbortReason == "component_disabled" &&
                        job.WorkerThreadId != callingThread,
                        "Lifecycle terminalization did not publish one complete result."
                    );
                    Require(
                        typeof(InteractionLifecycleTerminalizationResult)
                            .GetProperties()
                            .All(property => !property.CanWrite),
                        "Lifecycle terminalization result is externally mutable."
                    );
                }

                var completeness = new InteractionDataCompleteness(
                    manifest: true,
                    events: true,
                    poses: false,
                    objects: false,
                    summary: true,
                    captureGapCount: 0L
                );
                Require(
                    controllerOwnedSummary.SealAborted(
                        0.5d,
                        FixedUtc,
                        "controller_probe",
                        completeness
                    ).Status == "aborted",
                    "Background lifecycle work mutated the Controller-owned summary."
                );
                string events = File.ReadAllText(Path.Combine(
                    writer.RunDirectory,
                    InteractionStoragePaths.EventsFileName
                ));
                Require(
                    events.Split(
                        new[] { "\"event_type\":\"run_aborted\"" },
                        StringSplitOptions.None
                    ).Length - 1 == 1,
                    "Lifecycle job emitted duplicate terminal events."
                );

                AssertLifecycleQueueFailurePublishesOnce(
                    root,
                    CreatePlan(119, "P952"),
                    new RejectingBackgroundWorkQueue()
                );
                AssertLifecycleQueueFailurePublishesOnce(
                    root,
                    CreatePlan(120, "P953"),
                    new ThrowingBackgroundWorkQueue()
                );
                AssertPendingLifecycleQueueFailurePublishesImmediately(
                    root,
                    CreatePlan(121, "P954"),
                    new RejectingBackgroundWorkQueue()
                );
                AssertPendingLifecycleQueueFailurePublishesImmediately(
                    root,
                    CreatePlan(122, "P955"),
                    new ThrowingBackgroundWorkQueue()
                );
                AssertCompletedInitializationQueueFailurePublishesOnce(
                    root,
                    CreatePlan(123, "P956"),
                    new RejectingBackgroundWorkQueue(),
                    initializationSucceeds: true
                );
                AssertCompletedInitializationQueueFailurePublishesOnce(
                    root,
                    CreatePlan(124, "P957"),
                    new RejectingBackgroundWorkQueue(),
                    initializationSucceeds: false
                );
                AssertCompletedInitializationQueueFailurePublishesOnce(
                    root,
                    CreatePlan(125, "P958"),
                    new ThrowingBackgroundWorkQueue(),
                    initializationSucceeds: true
                );
                AssertCompletedInitializationQueueFailurePublishesOnce(
                    root,
                    CreatePlan(126, "P959"),
                    new ThrowingBackgroundWorkQueue(),
                    initializationSucceeds: false
                );
                AssertPendingInitializationFailurePublishesOnce(
                    CreatePlan(127, "P960"),
                    new RejectingBackgroundWorkQueue()
                );
                AssertPendingInitializationFailurePublishesOnce(
                    CreatePlan(128, "P961"),
                    new ThrowingBackgroundWorkQueue()
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void LifecycleTerminalizationOwnsLateInitialization()
        {
            string root = CreateTemporaryRoot();
            InteractionCaptureWriter lateWriter = null;
            InteractionLifecycleTerminalizationJob job = null;
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization = null;
            RunPlan plan = null;
            using (var writerCreated = new ManualResetEventSlim(false))
            using (var releaseInitialization = new ManualResetEventSlim(false))
            {
                try
                {
                    plan = CreatePlan(110, "P943");
                    initialization = InteractionBackgroundOperation<
                            InteractionCaptureWriter>.Start(() =>
                            {
                                lateWriter = CreateWriter(root, plan);
                                writerCreated.Set();
                                releaseInitialization.Wait();
                                return lateWriter;
                            });
                    Require(
                        writerCreated.Wait(TimeSpan.FromSeconds(5)),
                        "Delayed capture writer was not created."
                    );
                    job = InteractionLifecycleTerminalizationJob.Start(
                        writer: null,
                        initialization: initialization,
                        detachedSummary:
                            new InteractionSummaryTracker(plan.RunId),
                        abortReason: "component_disabled",
                        monotonicTimeSeconds: 0.5d,
                        utcTime: FixedUtc,
                        frame: 78
                    );

                    Require(
                        !job.Wait(TimeSpan.FromMilliseconds(5200)),
                        "Lifecycle owner abandoned initialization at the old five-second threshold."
                    );
                    Require(
                        job.IsAwaitingInitialization &&
                        job.InitializationDelayObserved,
                        "Late initialization ownership was not exposed diagnostically."
                    );
                    releaseInitialization.Set();
                    Require(
                        job.Wait(TimeSpan.FromSeconds(10)) &&
                        job.TryConsume(
                            out InteractionLifecycleTerminalizationResult result) &&
                        result.Succeeded && result.Writer == lateWriter &&
                        result.Writer.IsSealed &&
                        result.InitializationDelayObserved &&
                        result.SealResult.Summary.Status == "aborted",
                        "Late capture initialization was not eventually sealed by its detached owner."
                    );
                    AssertFinalFiles(lateWriter.RunDirectory);
                    IReadOnlyList<InteractionPendingRun> pending =
                        InteractionPendingRunDiscovery.DiscoverQuestLocal(root);
                    Require(
                        pending.Count == 1 &&
                        pending[0].IsLocallyComplete &&
                        !pending[0].NeedsRecovery &&
                        !pending[0].NeedsAttention,
                        "Late initialized Abort is not a complete local Run."
                    );
                }
                finally
                {
                    releaseInitialization.Set();
                    initialization?.Wait(TimeSpan.FromSeconds(10));
                    job?.Wait(TimeSpan.FromSeconds(10));
                    if (lateWriter != null && !lateWriter.IsSealed)
                    {
                        var cleanupSummary =
                            new InteractionSummaryTracker(plan.RunId);
                        InteractionBackgroundOperation<
                            InteractionCaptureSealResult> cleanupSeal =
                            lateWriter.BeginSeal(
                                InteractionCaptureTerminalKind.Aborted,
                                completeness => cleanupSummary.SealAborted(
                                    0.6d,
                                    FixedUtc.AddSeconds(0.1d),
                                    "test_cleanup",
                                    completeness
                                )
                            );
                        cleanupSeal.Wait(TimeSpan.FromSeconds(10));
                    }
                    lateWriter?.Dispose();
                    DeleteTemporaryRoot(root);
                }
            }
        }

        public static void LifecycleTerminalizationOwnsSlowSeal()
        {
            string root = CreateTemporaryRoot();
            InteractionCaptureWriter writer = null;
            InteractionLifecycleTerminalizationJob job = null;
            using (var closeEntered = new ManualResetEventSlim(false))
            using (var releaseClose = new ManualResetEventSlim(false))
            {
                try
                {
                    RunPlan plan = CreatePlan(113, "P946");
                    byte[] manifest =
                        InteractionRunManifestContractV1.SerializeUtf8(plan);
                    writer = InteractionCaptureWriter.CreateNew(
                        root,
                        plan,
                        manifest,
                        InteractionCaptureWriter.DefaultQueueCapacity,
                        InteractionCaptureWriter.DefaultGapThresholdSeconds,
                        new BlockingCloseChannelFactory(
                            closeEntered,
                            releaseClose
                        )
                    );
                    job = InteractionLifecycleTerminalizationJob.Start(
                        writer,
                        initialization: null,
                        detachedSummary:
                            new InteractionSummaryTracker(plan.RunId),
                        abortReason: "component_disabled",
                        monotonicTimeSeconds: 0.7d,
                        utcTime: FixedUtc,
                        frame: 79
                    );
                    Require(
                        closeEntered.Wait(TimeSpan.FromSeconds(5)),
                        "Lifecycle seal did not reach controlled storage."
                    );
                    Require(
                        !job.Wait(TimeSpan.FromMilliseconds(5200)),
                        "Lifecycle job abandoned its still-running seal at five seconds."
                    );
                    releaseClose.Set();
                    Require(
                        job.Wait(TimeSpan.FromSeconds(10)) &&
                        job.TryConsume(
                            out InteractionLifecycleTerminalizationResult result) &&
                        result.Succeeded && result.Writer == writer &&
                        result.Writer.IsSealed,
                        "Lifecycle job did not retain ownership through slow seal completion."
                    );
                    AssertFinalFiles(writer.RunDirectory);
                }
                finally
                {
                    releaseClose.Set();
                    job?.Wait(TimeSpan.FromSeconds(10));
                    if (writer != null)
                    {
                        SpinWait.SpinUntil(
                            () => writer.IsSealed,
                            TimeSpan.FromSeconds(10)
                        );
                        writer.Dispose();
                    }
                    DeleteTemporaryRoot(root);
                }
            }
        }

        public static void LifecyclePolicyTerminalizesConsumedRuns()
        {
            Require(
                !InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.PreStart
                ) &&
                InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.Preparing
                ) &&
                InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.Scheduled
                ) &&
                InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.Running
                ) &&
                InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.Completing
                ) &&
                !InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.Completed
                ),
                "A consumed nonterminal Run can be abandoned by lifecycle exit."
            );
        }

        public static void PathsFailClosedAgainstTraversalAndIllegalIds()
        {
            string root = CreateTemporaryRoot();
            try
            {
                ExpectThrows<ArgumentException>(() =>
                    InteractionStoragePaths.GetRunDirectory(
                        root,
                        "../escape",
                        "P001",
                        "run_x"
                    )
                );
                ExpectThrows<ArgumentException>(() =>
                    InteractionStoragePaths.GetRunDirectory(
                        root,
                        "pilot",
                        "参与者",
                        "run_x"
                    )
                );
                ExpectThrows<ArgumentException>(() =>
                    InteractionStoragePaths.GetRunDirectory(
                        root,
                        "pilot",
                        "_P001",
                        "run_x"
                    )
                );
                ExpectThrows<ArgumentException>(() =>
                    InteractionStoragePaths.GetRunDirectory(
                        root,
                        "pilot",
                        "CON",
                        "run_x"
                    )
                );
                ExpectThrows<ArgumentException>(() =>
                    InteractionStoragePaths.ValidateRelativeArtifactPath(
                        "wang/../secret.pose.jsonl",
                        "artifact"
                    )
                );
                string safe = InteractionStoragePaths.GetRunDirectory(
                    root,
                    "pilot",
                    "P001",
                    "run_safe"
                );
                Require(
                    safe.IndexOf(
                        Path.Combine("interaction-tests", "pilot", "P001", "run_safe"),
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0,
                    "Safe path layout mismatched."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void StudyRejectsDebugOverrides()
        {
            ExpectThrows<InvalidOperationException>(() =>
                InteractionStudyStartPolicy.Validate(
                    InteractionRunMode.StandaloneStudy,
                    true,
                    false,
                    true
                )
            );
            ExpectThrows<InvalidOperationException>(() =>
                InteractionStudyStartPolicy.Validate(
                    InteractionRunMode.EngineeringLocal,
                    false,
                    false,
                    true
                )
            );
            InteractionStudyStartPolicy.Validate(
                InteractionRunMode.EngineeringLocal,
                false,
                true,
                true
            );
        }

        public static void SummaryKeepsAccuracyAndStuckSeparate()
        {
            var tracker = new InteractionSummaryTracker("run_summary_test");
            tracker.BeginRun(0d, FixedUtc);
            tracker.BeginPhase(1, 0d);
            tracker.RecordAttempt(1, false, 0.5d);
            tracker.RecordError(1, 0.5d);
            tracker.RecordReplay(1, 1d);
            tracker.RecordTimeout(1, 180d);
            tracker.BeginExposure(1, InteractionExposureKind.Text, 2d);
            tracker.EndExposure(1, InteractionExposureKind.Text, 4d);
            tracker.BeginExposure(1, InteractionExposureKind.Pointing, 5d);
            tracker.EndExposure(1, InteractionExposureKind.Pointing, 5d);
            tracker.FinishPhase(1, false, true, 181d);
            for (int phaseId = 2; phaseId <= 6; phaseId++)
            {
                tracker.BeginPhase(phaseId, 181d + phaseId);
                tracker.RecordAttempt(phaseId, true, 181.1d + phaseId);
                tracker.FinishPhase(
                    phaseId,
                    true,
                    false,
                    182d + phaseId
                );
            }
            InteractionRunSummary summary = tracker.SealCompleted(
                190d,
                FixedUtc.AddSeconds(190),
                new InteractionDataCompleteness(true, true, true, true, true, 0)
            );
            InteractionPhaseSummary first = summary.Phases[0];
            Require(
                first.FirstAttemptCorrect == false &&
                !first.PhaseCompleted && first.PhaseStuck &&
                first.ErrorCount == 1 && first.ReplayUsed &&
                first.TimeoutObserved && first.TextExposed &&
                first.PointingExposed && first.PointingExposureSeconds == 0d &&
                Math.Abs(first.TextExposureSeconds - 2d) < 0.000001d,
                "Phase summary collapsed distinct outcome fields."
            );
            Require(
                summary.CompletedPhaseCount == 5 &&
                summary.TotalStuckCount == 1 &&
                summary.TotalErrorCount == 1,
                "Run summary totals are incorrect."
            );
            string json = InteractionSummaryJson.Serialize(summary);
            Require(
                json.Contains("\"first_attempt_correct\":false") &&
                json.Contains("\"phase_completed\":false") &&
                json.Contains("\"phase_stuck\":true"),
                "Summary JSON omitted separate accuracy/stuck fields."
            );
        }

        public static void InstructionManifestReaderMapsExactArtifacts()
        {
            var builder = new StringBuilder();
            builder.Append("{\"schema_version\":1,\"entries\":[");
            for (int sentence = 1; sentence <= 31; sentence++)
            {
                if (sentence > 1)
                {
                    builder.Append(',');
                }
                string id = sentence.ToString("D3");
                int phase = PhaseSentenceRanges.GetPhaseId(id);
                builder.Append("{\"phase_id\":").Append(phase)
                    .Append(",\"sentence_id\":\"").Append(id)
                    .Append("\",\"signer_id\":\"wang\"," +
                        "\"take_id\":\"take_").Append(id)
                    .Append("\",\"completed_utc\":\"2026-08-26T10:15:30Z\"," +
                        "\"take_index\":1,\"pose_path\":\"wang/sentence_")
                    .Append(id).Append("/take.pose.jsonl\"," +
                        "\"pose_sha256\":\"")
                    .Append(new string('a', 64)).Append("\"}");
            }
            builder.Append("]}");
            InstructionContentCatalog catalog =
                InteractionInstructionContentManifestReader.Read(
                    builder.ToString()
                );
            InstructionContentReference reference = catalog.ForSentence("031");
            Require(
                reference.TakeId == "take_031" &&
                reference.ArtifactPath ==
                    "wang/sentence_031/take.pose.jsonl" &&
                reference.ArtifactSha256 == new string('a', 64),
                "W2 manifest mapping changed exact artifact identity."
            );
        }

        internal static InteractionCaptureWriter CreateWriter(
            string root,
            RunPlan plan)
        {
            byte[] bytes = InteractionRunManifestContractV1.SerializeUtf8(plan);
            return Await(InteractionCaptureWriter.BeginCreateNew(
                root,
                plan,
                bytes
            ));
        }

        internal static InteractionRunStateMachine CreateCompletingStateMachine(
            int seed,
            string participantId)
        {
            var machine = new InteractionRunStateMachine(
                new AssistanceBlockAllocator(seed),
                CreateGenerator()
            );
            machine.Start(CreateRequest(seed + 1, participantId));
            machine.Schedule(FixedUtc);
            machine.RunStarted(TimeSpan.FromSeconds(1d));
            for (int phaseId = 1;
                phaseId <= PhaseSentenceRanges.PhaseCount;
                phaseId++)
            {
                machine.FirstPlaybackCompleted();
                machine.CompletePhase(TimeSpan.FromSeconds(phaseId + 1d));
            }
            Require(
                machine.State == RunState.Completing,
                "W1 test fixture did not reach Completing."
            );
            return machine;
        }

        internal static InteractionRunStateMachine CreateRunningStateMachine(
            int seed,
            string participantId)
        {
            var machine = new InteractionRunStateMachine(
                new AssistanceBlockAllocator(seed),
                CreateGenerator()
            );
            machine.Start(CreateRequest(seed + 1, participantId));
            machine.Schedule(FixedUtc);
            machine.RunStarted(TimeSpan.FromSeconds(1d));
            Require(
                machine.State == RunState.Running,
                "W1 test fixture did not reach Running."
            );
            return machine;
        }

        internal static T Await<T>(
            InteractionBackgroundOperation<T> operation,
            int timeoutSeconds = 5)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            Require(
                operation.Wait(TimeSpan.FromSeconds(timeoutSeconds)),
                "Background operation timed out in the deterministic scenario."
            );
            if (!operation.Succeeded)
            {
                throw new InvalidOperationException(
                    "Background operation failed in the deterministic scenario.",
                    operation.Error
                );
            }
            return operation.GetResult();
        }

        internal static InteractionSummaryTracker CreateCompletedSummary(
            string runId)
        {
            var summary = new InteractionSummaryTracker(runId);
            summary.BeginRun(0d, FixedUtc);
            for (int phaseId = 1; phaseId <= 6; phaseId++)
            {
                double started = phaseId;
                summary.BeginPhase(phaseId, started);
                summary.RecordAttempt(phaseId, true, started + 0.1d);
                summary.FinishPhase(
                    phaseId,
                    completed: true,
                    stuck: false,
                    monotonicTimeSeconds: started + 0.5d
                );
            }
            return summary;
        }

        internal static void WriteOnePoseAndObject(
            InteractionCaptureWriter writer)
        {
            writer.TryWritePose(CreatePose(1L, 6.6d, 6));
            writer.TryWriteObject(new InteractionObjectSample(
                6,
                1L,
                6.6d,
                FixedUtc.AddSeconds(6.6d),
                6,
                "door_final",
                "{\"open\":true}"
            ));
        }

        private static InteractionPoseSample CreatePose(
            long sequence,
            double time,
            int frame)
        {
            var hand = new InteractionHandSample(
                true,
                true,
                true,
                false,
                Array.Empty<InteractionJointSample>()
            );
            return new InteractionPoseSample(
                1,
                sequence,
                time,
                FixedUtc.AddSeconds(time),
                frame,
                true,
                new InteractionVector3Sample(0d, 1.6d, 0d),
                new InteractionQuaternionSample(0d, 0d, 0d, 1d),
                hand,
                hand
            );
        }

        private static void AssertFinalFiles(string runDirectory)
        {
            string[] files =
            {
                InteractionStoragePaths.ManifestFileName,
                InteractionStoragePaths.EventsFileName,
                InteractionStoragePaths.PosesFileName,
                InteractionStoragePaths.ObjectsFileName,
                InteractionStoragePaths.SummaryFileName
            };
            foreach (string file in files)
            {
                Require(
                    File.Exists(Path.Combine(runDirectory, file)),
                    "Final file is missing: " + file
                );
            }
            Require(
                Directory.GetFiles(runDirectory, "*.partial").Length == 0,
                "Sealed Run retained partial streams."
            );
        }

        internal static RunPlan CreatePlan(int seed, string participantId)
        {
            return CreateGenerator().Generate(
                CreateRequest(seed, participantId),
                new AssistanceAssignment(
                    AssistanceCondition.TextAndPointing,
                    seed,
                    0
                )
            );
        }

        private static RunPlanGenerator CreateGenerator()
        {
            int identity = 0;
            return new RunPlanGenerator(
                () =>
                {
                    identity++;
                    byte[] bytes = new byte[16];
                    BitConverter.GetBytes(identity).CopyTo(bytes, 0);
                    BitConverter.GetBytes(Environment.TickCount).CopyTo(bytes, 4);
                    BitConverter.GetBytes(Guid.NewGuid().GetHashCode()).CopyTo(bytes, 8);
                    return new Guid(bytes);
                },
                () => FixedUtc
            );
        }

        private static RunPlanGenerationRequest CreateRequest(
            int seed,
            string participantId)
        {
            return new RunPlanGenerationRequest(
                "pilot-20260826",
                participantId,
                "app_w6_test",
                "1.0.0-test",
                "w6-test-commit",
                seed,
                CreateCatalog()
            );
        }

        private static InstructionContentCatalog CreateCatalog()
        {
            var values = new List<InstructionContentReference>(31);
            for (int sentence = 1; sentence <= 31; sentence++)
            {
                string id = sentence.ToString("D3");
                values.Add(new InstructionContentReference(
                    PhaseSentenceRanges.GetPhaseId(id),
                    id,
                    "wang",
                    "take_" + id,
                    FixedUtc.AddMinutes(sentence),
                    1,
                    "wang/sentence_" + id + "/take.pose.jsonl",
                    new string('a', 64)
                ));
            }
            return new InstructionContentCatalog(values);
        }

        internal static string CreateTemporaryRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "signvr-w6-tests-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(root);
            return root;
        }

        internal static void DeleteTemporaryRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }
            string canonical = Path.GetFullPath(root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ) + Path.DirectorySeparatorChar;
            if (!canonical.StartsWith(
                    temp,
                    Path.DirectorySeparatorChar == '\\'
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal) ||
                Path.GetFileName(canonical).IndexOf(
                    "signvr-w6-tests-",
                    StringComparison.Ordinal
                ) != 0)
            {
                throw new InvalidOperationException(
                    "Refusing to remove a non-W6 temporary directory."
                );
            }
            Directory.Delete(canonical, true);
        }

        internal static void AssertCanOpenExclusively(string path)
        {
            using (new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None))
            {
            }
        }

        private static void AssertLifecycleQueueFailurePublishesOnce(
            string root,
            RunPlan plan,
            IInteractionBackgroundWorkQueue queue)
        {
            InteractionCaptureWriter writer = CreateWriter(root, plan);
            writer.BeginCapture();
            InteractionLifecycleTerminalizationJob job =
                InteractionLifecycleTerminalizationJob.Start(
                    writer,
                    initialization: null,
                    detachedSummary: new InteractionSummaryTracker(plan.RunId),
                    abortReason: "component_disabled",
                    monotonicTimeSeconds: 0.8d,
                    utcTime: FixedUtc,
                    frame: 80,
                    workQueue: queue
                );
            Require(
                job.Wait(TimeSpan.Zero) &&
                job.TryConsume(
                    out InteractionLifecycleTerminalizationResult result) &&
                !result.Succeeded && result.Writer == writer &&
                result.DetachedInitializationOwner == null &&
                result.Error is IOException && !job.TryConsume(out _),
                "Lifecycle queue failure was not published once as an immutable result."
            );
            Require(
                SpinWait.SpinUntil(
                    () => CanOpenCapturePartialsExclusively(writer.RunDirectory),
                    TimeSpan.FromSeconds(5)
                ),
                "Lifecycle queue failure retained an open writer handle."
            );
            writer.Dispose();
        }

        private static void AssertPendingLifecycleQueueFailurePublishesImmediately(
            string root,
            RunPlan plan,
            IInteractionBackgroundWorkQueue queue)
        {
            InteractionCaptureWriter lateWriter = null;
            InteractionBackgroundOperation<InteractionCaptureWriter>
                initialization = null;
            InteractionLifecycleTerminalizationJob job = null;
            InteractionDetachedInitializationOwner detachedOwner = null;
            using (var writerCreated = new ManualResetEventSlim(false))
            using (var releaseInitialization = new ManualResetEventSlim(false))
            {
                try
                {
                    initialization = InteractionBackgroundOperation<
                        InteractionCaptureWriter>.Start(() =>
                        {
                            lateWriter = CreateWriter(root, plan);
                            lateWriter.BeginCapture();
                            writerCreated.Set();
                            releaseInitialization.Wait();
                            return lateWriter;
                        });
                    Require(
                        writerCreated.Wait(TimeSpan.FromSeconds(5)),
                        "Pending initialization did not create its late writer."
                    );
                    job = InteractionLifecycleTerminalizationJob.Start(
                        writer: null,
                        initialization: initialization,
                        detachedSummary: new InteractionSummaryTracker(
                            plan.RunId
                        ),
                        abortReason: "component_disabled",
                        monotonicTimeSeconds: 0.9d,
                        utcTime: FixedUtc,
                        frame: 81,
                        workQueue: queue
                    );
                    Require(
                        job.Wait(TimeSpan.Zero) &&
                        job.TryConsume(
                            out InteractionLifecycleTerminalizationResult result) &&
                        !result.Succeeded && result.Writer == null &&
                        result.Error is IOException &&
                        (detachedOwner = result.DetachedInitializationOwner) !=
                            null &&
                        !detachedOwner.IsCompleted && !job.TryConsume(out _),
                        "Pending initialization made lifecycle queue failure wait " +
                        "for a future Controller callback."
                    );

                    releaseInitialization.Set();
                    Require(
                        initialization.Wait(TimeSpan.FromSeconds(5)) &&
                        initialization.Succeeded,
                        "Late capture initialization did not finish."
                    );
                    Require(
                        SpinWait.SpinUntil(
                            () => detachedOwner.IsCompleted,
                            TimeSpan.FromSeconds(5)
                        ) && detachedOwner.WriterOwned &&
                        detachedOwner.Error == null &&
                        SpinWait.SpinUntil(
                            () => CanOpenCapturePartialsExclusively(
                                lateWriter.RunDirectory
                            ),
                            TimeSpan.FromSeconds(5)
                        ),
                        "Detached lifecycle owner did not close the late writer."
                    );
                    Require(
                        !File.Exists(Path.Combine(
                            lateWriter.RunDirectory,
                            InteractionStoragePaths.SummaryFileName
                        )) &&
                        CountTerminalEventsInPartial(lateWriter.RunDirectory) == 0,
                        "Detached lifecycle owner sealed or duplicated terminal data."
                    );
                }
                finally
                {
                    releaseInitialization.Set();
                    bool initializationCompleted = initialization == null ||
                        initialization.Wait(TimeSpan.FromSeconds(5));
                    bool detachedCompleted = detachedOwner == null ||
                        SpinWait.SpinUntil(
                            () => detachedOwner.IsCompleted,
                            TimeSpan.FromSeconds(5)
                        );
                    job?.Wait(TimeSpan.FromSeconds(5));
                    if (job != null && job.IsCompleted)
                    {
                        job.TryConsume(out _);
                    }
                    if (initializationCompleted && detachedCompleted)
                    {
                        lateWriter?.Dispose();
                    }
                }
            }
        }

        private static void AssertCompletedInitializationQueueFailurePublishesOnce(
            string root,
            RunPlan plan,
            IInteractionBackgroundWorkQueue queue,
            bool initializationSucceeds)
        {
            InteractionCaptureWriter initializedWriter = null;
            try
            {
                var initializationFailure = new IOException(
                    "Injected completed initialization failure."
                );
                if (initializationSucceeds)
                {
                    initializedWriter = CreateWriter(root, plan);
                    initializedWriter.BeginCapture();
                }
                InteractionBackgroundOperation<InteractionCaptureWriter>
                    initialization = InteractionBackgroundOperation<
                        InteractionCaptureWriter>.CreateCompleted(
                            initializedWriter,
                            initializationSucceeds ? null : initializationFailure
                        );
                InteractionLifecycleTerminalizationJob job =
                    InteractionLifecycleTerminalizationJob.Start(
                        writer: null,
                        initialization: initialization,
                        detachedSummary: new InteractionSummaryTracker(plan.RunId),
                        abortReason: "component_disabled",
                        monotonicTimeSeconds: 1d,
                        utcTime: FixedUtc,
                        frame: 82,
                        workQueue: queue
                    );
                Require(
                    job.Wait(TimeSpan.Zero) &&
                    job.TryConsume(
                        out InteractionLifecycleTerminalizationResult result) &&
                    !result.Succeeded && result.Writer == null &&
                    result.Error is IOException && !job.TryConsume(out _) &&
                    result.DetachedInitializationOwner != null &&
                    result.DetachedInitializationOwner.IsCompleted &&
                    result.DetachedInitializationOwner.WriterOwned ==
                        initializationSucceeds &&
                    (initializationSucceeds
                        ? result.DetachedInitializationOwner.Error == null
                        : ReferenceEquals(
                            result.DetachedInitializationOwner.Error,
                            initializationFailure
                        )),
                    "A completed initialization was lost between queue-failure " +
                    "ownership snapshots."
                );
                if (initializedWriter != null)
                {
                    Require(
                        SpinWait.SpinUntil(
                            () => CanOpenCapturePartialsExclusively(
                                initializedWriter.RunDirectory
                            ),
                            TimeSpan.FromSeconds(5)
                        ),
                        "Completed initialization ownership retained an open writer."
                    );
                }
            }
            finally
            {
                initializedWriter?.Dispose();
            }
        }

        private static void AssertPendingInitializationFailurePublishesOnce(
            RunPlan plan,
            IInteractionBackgroundWorkQueue queue)
        {
            var initializationFailure = new IOException(
                "Injected pending initialization failure."
            );
            using (var releaseInitialization = new ManualResetEventSlim(false))
            {
                InteractionBackgroundOperation<InteractionCaptureWriter>
                    initialization = InteractionBackgroundOperation<
                        InteractionCaptureWriter>.Start(() =>
                        {
                            releaseInitialization.Wait();
                            throw initializationFailure;
                        });
                InteractionLifecycleTerminalizationJob job =
                    InteractionLifecycleTerminalizationJob.Start(
                        writer: null,
                        initialization: initialization,
                        detachedSummary: new InteractionSummaryTracker(plan.RunId),
                        abortReason: "component_disabled",
                        monotonicTimeSeconds: 1.1d,
                        utcTime: FixedUtc,
                        frame: 83,
                        workQueue: queue
                    );
                Require(
                    job.Wait(TimeSpan.Zero),
                    "Pending initialization queue failure did not publish immediately."
                );
                bool consumed = job.TryConsume(
                    out InteractionLifecycleTerminalizationResult result
                );
                Require(
                    consumed && !result.Succeeded && result.Writer == null &&
                    result.Error is IOException && !job.TryConsume(out _) &&
                    result.DetachedInitializationOwner != null &&
                    !result.DetachedInitializationOwner.IsCompleted,
                    "Pending initialization failure was not atomically observed."
                );
                InteractionDetachedInitializationOwner detachedOwner =
                    result.DetachedInitializationOwner;
                releaseInitialization.Set();
                Require(
                    initialization.Wait(TimeSpan.FromSeconds(5)) &&
                    !initialization.Succeeded &&
                    SpinWait.SpinUntil(
                        () => detachedOwner.IsCompleted,
                        TimeSpan.FromSeconds(5)
                    ) && !detachedOwner.WriterOwned &&
                    ReferenceEquals(detachedOwner.Error, initializationFailure),
                    "Detached ownership did not publish pending initialization " +
                    "failure exactly once."
                );
            }
        }

        private static int CountTerminalEventsInPartial(string runDirectory)
        {
            string path = Path.Combine(
                runDirectory,
                "." + InteractionStoragePaths.EventsFileName + ".partial"
            );
            if (!File.Exists(path))
            {
                return 0;
            }
            string text = File.ReadAllText(path);
            return CountOccurrences(
                    text,
                    "\"event_type\":\"run_completed\""
                ) +
                CountOccurrences(
                    text,
                    "\"event_type\":\"run_aborted\""
                );
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int offset = 0;
            while ((offset = text.IndexOf(
                       value,
                       offset,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }

        private static bool CanOpenCapturePartialsExclusively(
            string runDirectory)
        {
            string[] names =
            {
                InteractionStoragePaths.EventsFileName,
                InteractionStoragePaths.PosesFileName,
                InteractionStoragePaths.ObjectsFileName
            };
            try
            {
                for (int index = 0; index < names.Length; index++)
                {
                    string path = Path.Combine(
                        runDirectory,
                        "." + names[index] + ".partial"
                    );
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    using (new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None))
                    {
                    }
                }
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(5);
                return false;
            }
        }

        private sealed class RejectingBackgroundWorkQueue :
            IInteractionBackgroundWorkQueue
        {
            public bool TryQueue(Action work)
            {
                if (work == null)
                {
                    throw new ArgumentNullException(nameof(work));
                }
                return false;
            }
        }

        private sealed class ThrowingBackgroundWorkQueue :
            IInteractionBackgroundWorkQueue
        {
            public bool TryQueue(Action work)
            {
                throw new InvalidOperationException(
                    "Injected background queue exception."
                );
            }
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void ExpectThrows<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new InvalidOperationException(
                "Expected exception " + typeof(T).Name + "."
            );
        }

        private sealed class FakeChannelFactory : IInteractionJsonlChannelFactory
        {
            private readonly bool rejectPoses;
            private int created;

            public FakeChannelFactory(bool rejectPoses)
            {
                this.rejectPoses = rejectPoses;
            }

            public FakeChannel Events { get; private set; }
            public FakeChannel Poses { get; private set; }
            public FakeChannel Objects { get; private set; }

            public IInteractionJsonlChannel Create(
                string partialPath,
                string finalPath,
                int capacity,
                string workerName,
                InteractionJsonlBudget budget,
                InteractionDiskBudgetGuard diskBudget)
            {
                var channel = new FakeChannel(
                    rejectWrites: created == 1 && rejectPoses
                );
                if (created == 0)
                {
                    Events = channel;
                }
                else if (created == 1)
                {
                    Poses = channel;
                }
                else
                {
                    Objects = channel;
                }
                created++;
                return channel;
            }
        }

        private sealed class BlockingCloseChannelFactory :
            IInteractionJsonlChannelFactory
        {
            private readonly IInteractionJsonlChannelFactory inner =
                new InteractionJsonlChannelFactory();
            private readonly ManualResetEventSlim closeEntered;
            private readonly ManualResetEventSlim releaseClose;
            private int created;

            public BlockingCloseChannelFactory(
                ManualResetEventSlim closeEntered,
                ManualResetEventSlim releaseClose)
            {
                this.closeEntered = closeEntered ??
                    throw new ArgumentNullException(nameof(closeEntered));
                this.releaseClose = releaseClose ??
                    throw new ArgumentNullException(nameof(releaseClose));
            }

            public IInteractionJsonlChannel Create(
                string partialPath,
                string finalPath,
                int capacity,
                string workerName,
                InteractionJsonlBudget budget,
                InteractionDiskBudgetGuard diskBudget)
            {
                IInteractionJsonlChannel channel = inner.Create(
                    partialPath,
                    finalPath,
                    capacity,
                    workerName,
                    budget,
                    diskBudget
                );
                if (created++ != 0)
                {
                    return channel;
                }
                return new BlockingCloseChannel(
                    channel,
                    closeEntered,
                    releaseClose
                );
            }
        }

        private sealed class BlockingCloseChannel : IInteractionJsonlChannel
        {
            private readonly IInteractionJsonlChannel inner;
            private readonly ManualResetEventSlim closeEntered;
            private readonly ManualResetEventSlim releaseClose;

            public BlockingCloseChannel(
                IInteractionJsonlChannel inner,
                ManualResetEventSlim closeEntered,
                ManualResetEventSlim releaseClose)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
                this.closeEntered = closeEntered;
                this.releaseClose = releaseClose;
            }

            public long AcceptedLineCount => inner.AcceptedLineCount;
            public bool TryWrite(string line) => inner.TryWrite(line);
            public void WriteCritical(string line) => inner.WriteCritical(line);
            public void FlushAndSync() => inner.FlushAndSync();

            public void CloseAndPromote()
            {
                closeEntered.Set();
                releaseClose.Wait();
                inner.CloseAndPromote();
            }

            public void DisposeLeavingPartial()
            {
                inner.DisposeLeavingPartial();
            }

            public void RequestCloseLeavingPartial()
            {
                inner.RequestCloseLeavingPartial();
            }
        }

        private sealed class FakeFreeSpaceProbe : IInteractionFreeSpaceProbe
        {
            public FakeFreeSpaceProbe(long availableBytes)
            {
                AvailableBytes = availableBytes;
            }

            public long AvailableBytes { get; set; }

            public long GetAvailableBytes(string path)
            {
                return AvailableBytes;
            }
        }

        private sealed class FakeEventSink : IInteractionEventSink
        {
            public List<string> EventTypes { get; } = new List<string>();
            public int FlushCount { get; private set; }

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
                EventTypes.Add(eventType);
            }

            public void FlushPhase()
            {
                FlushCount++;
            }
        }

        private sealed class FakeChannel : IInteractionJsonlChannel
        {
            private readonly bool rejectWrites;

            public FakeChannel(bool rejectWrites)
            {
                this.rejectWrites = rejectWrites;
            }

            public List<string> Lines { get; } = new List<string>();
            public int FlushCount { get; private set; }
            public long AcceptedLineCount => Lines.Count;

            public bool TryWrite(string line)
            {
                if (rejectWrites)
                {
                    return false;
                }
                Lines.Add(line);
                return true;
            }

            public void WriteCritical(string line)
            {
                Lines.Add(line);
            }

            public void FlushAndSync()
            {
                FlushCount++;
            }

            public void CloseAndPromote()
            {
            }

            public void DisposeLeavingPartial()
            {
            }

            public void RequestCloseLeavingPartial()
            {
            }
        }
    }
}
#endif
