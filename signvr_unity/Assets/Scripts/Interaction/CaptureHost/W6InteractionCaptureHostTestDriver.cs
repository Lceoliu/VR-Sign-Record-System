#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private static readonly DateTimeOffset FixedUtc =
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

        public static void FrozenRegistrationRetriesExactBytesAfter409()
        {
            RunPlan plan = CreatePlan(12, "P001");
            byte[] manifest = InteractionRunManifestContractV1.SerializeUtf8(plan);
            var registration = new InteractionFrozenRunRegistration(plan, manifest);
            byte[] first = registration.BeginAttempt();
            registration.RecordResponse(409L, false);
            first[0] ^= 0xff;
            byte[] second = registration.BeginAttempt();
            Require(
                InteractionCaptureWriter.ByteArraysEqual(manifest, second),
                "Retry bytes changed after 409."
            );
            Require(
                ReferenceEquals(plan, registration.Plan) &&
                registration.AttemptCount == 2 && !registration.Accepted,
                "409 changed frozen registration state."
            );
        }

        public static void ConflictRecoveryRequiresMatchingSnapshot()
        {
            const string runId = "run_conflict_recovery";
            InteractionHostRunSnapshot snapshot =
                InteractionHostRunSnapshot.Parse(
                    "{\"schema_version\":1," +
                    "\"batch_id\":\"pilot-20260826\"," +
                    "\"participant_id\":\"P001\"," +
                    "\"run_id\":\"" + runId + "\"," +
                    "\"state\":\"Running\"," +
                    "\"start_at_utc\":\"2026-08-26T10:15:32Z\"," +
                    "\"missing_artifacts\":[\"events\",\"poses\"," +
                    "\"objects\",\"summary\",\"webcam\"]," +
                    "\"acknowledged\":false," +
                    "\"terminal_utc\":null,\"abort_reason\":null}",
                    runId
                );
            Require(
                InteractionRegistrationRecoveryPolicy.CanRecover(
                    snapshot,
                    "pilot-20260826",
                    "P001",
                    runId
                ),
                "The exact persisted Run snapshot was not recoverable."
            );
            Require(
                !InteractionRegistrationRecoveryPolicy.CanRecover(
                    snapshot,
                    "pilot-20260826",
                    "P999",
                    runId
                ) &&
                !InteractionRegistrationRecoveryPolicy.CanRecover(
                    snapshot,
                    "other-batch",
                    "P001",
                    runId
                ) &&
                !InteractionRegistrationRecoveryPolicy.CanRecover(
                    snapshot,
                    "pilot-20260826",
                    "P001",
                    "run_other"
                ),
                "A mismatched 409 snapshot was treated as the local Run."
            );
        }

        public static void HostNotReadyAndConflictNeverRedrawPlan()
        {
            InteractionHostReadiness readiness = InteractionHostReadiness.Parse(
                "{\"schema_version\":1,\"ready\":false," +
                "\"backend_ready\":true,\"storage_ready\":true," +
                "\"quest_ready\":true,\"camera_ready\":false," +
                "\"quest_device_id\":\"quest_alpha\"}"
            );
            Require(
                !readiness.IsStudyReady("quest_alpha", "P001"),
                "Stale camera readiness must reject Study."
            );
            InteractionHostReadiness ready = InteractionHostReadiness.Parse(
                "{\"schema_version\":1,\"ready\":true," +
                "\"backend_ready\":true,\"storage_ready\":true," +
                "\"quest_ready\":true,\"camera_ready\":true," +
                "\"quest_fresh\":true," +
                "\"camera_fresh\":true,\"participant_ready\":true," +
                "\"participant_fresh\":true," +
                "\"participant_id\":\"P001\"," +
                "\"quest_device_id\":\"quest_alpha\"," +
                "\"server_utc\":\"2026-08-26T10:15:30Z\"}"
            );
            Require(
                ready.IsStudyReady("quest_alpha", "P001") &&
                !ready.IsStudyReady("quest_other", "P001") &&
                !ready.IsStudyReady("QUEST_ALPHA", "P001") &&
                !ready.IsStudyReady("quest_alpha", "P999"),
                "W3 readiness shape or paired Quest identity was not enforced."
            );

            var machine = new InteractionRunStateMachine(
                new AssistanceBlockAllocator(44),
                CreateGenerator()
            );
            RunPlan plan = machine.Start(CreateRequest(77, "P001"));
            byte[] bytes = InteractionRunManifestContractV1.SerializeUtf8(plan);
            var registration = new InteractionFrozenRunRegistration(plan, bytes);
            registration.BeginAttempt();
            registration.RecordResponse(409L, false);
            registration.BeginAttempt();
            Require(
                ReferenceEquals(machine.Plan, plan) &&
                InteractionCaptureWriter.ByteArraysEqual(
                    bytes,
                    InteractionRunManifestContractV1.SerializeUtf8(machine.Plan)
                ),
                "Host conflict rerandomized the W1 plan."
            );
        }

        public static void StudyReadinessRequiresMatchingFreshParticipant()
        {
            InteractionHostReadiness exact = InteractionHostReadiness.Parse(
                "{\"schema_version\":1,\"ready\":true," +
                "\"backend_ready\":true,\"storage_ready\":true," +
                "\"quest_ready\":true,\"camera_ready\":true," +
                "\"quest_fresh\":true," +
                "\"camera_fresh\":true,\"participant_ready\":true," +
                "\"participant_fresh\":true," +
                "\"participant_id\":\"P001\"," +
                "\"quest_device_id\":\"quest_alpha\"}"
            );
            InteractionHostReadiness missingParticipant =
                InteractionHostReadiness.Parse(
                    "{\"schema_version\":1,\"ready\":true," +
                    "\"backend_ready\":true,\"storage_ready\":true," +
                    "\"quest_ready\":true,\"camera_ready\":true," +
                    "\"quest_fresh\":true," +
                    "\"camera_fresh\":true,\"participant_ready\":true," +
                    "\"participant_fresh\":true," +
                    "\"quest_device_id\":\"quest_alpha\"}"
                );
            InteractionHostReadiness staleParticipant =
                InteractionHostReadiness.Parse(
                    "{\"schema_version\":1,\"ready\":true," +
                    "\"backend_ready\":true,\"storage_ready\":true," +
                    "\"quest_ready\":true,\"camera_ready\":true," +
                    "\"quest_fresh\":true," +
                    "\"camera_fresh\":true,\"participant_ready\":true," +
                    "\"participant_fresh\":false," +
                    "\"participant_id\":\"P001\"," +
                    "\"quest_device_id\":\"quest_alpha\"}"
                );
            InteractionHostReadiness missingFreshness =
                InteractionHostReadiness.Parse(
                    "{\"schema_version\":1,\"ready\":true," +
                    "\"backend_ready\":true,\"storage_ready\":true," +
                    "\"quest_ready\":true,\"camera_ready\":true," +
                    "\"participant_ready\":true," +
                    "\"participant_id\":\"P001\"," +
                    "\"quest_device_id\":\"quest_alpha\"}"
                );
            Require(
                exact.IsStudyReady("quest_alpha", "P001") &&
                !exact.IsStudyReady("quest_alpha", "P002") &&
                !missingParticipant.IsStudyReady("quest_alpha", "P001") &&
                !staleParticipant.IsStudyReady("quest_alpha", "P001") &&
                !missingFreshness.IsStudyReady("quest_alpha", "P001"),
                "Study accepted an absent, mismatched, or stale participant."
            );
        }

        public static void StudyReadinessRejectsQuestIdCaseMismatch()
        {
            InteractionHostReadiness readiness = InteractionHostReadiness.Parse(
                "{\"schema_version\":1,\"ready\":true," +
                "\"backend_ready\":true,\"storage_ready\":true," +
                "\"quest_ready\":true,\"camera_ready\":true," +
                "\"quest_fresh\":true,\"camera_fresh\":true," +
                "\"participant_ready\":true," +
                "\"participant_fresh\":true," +
                "\"participant_id\":\"P001\"," +
                "\"quest_device_id\":\"quest_alpha\"}"
            );
            Require(
                readiness.IsStudyReady("quest_alpha", "P001"),
                "Exact Quest identity did not pass Study readiness."
            );
            Require(
                !readiness.IsStudyReady("QUEST_ALPHA", "P001"),
                "Quest identity comparison accepted a case mismatch."
            );
        }

        public static void ScheduledStartGateBlocksPreStartCapture()
        {
            var gate = new InteractionScheduledStartGate(
                FixedUtc.AddSeconds(2),
                FixedUtc,
                10d
            );
            Require(!gate.IsDue(11.999d), "Capture opened before start_at.");
            Require(gate.IsDue(12d), "Capture did not open at start_at.");
        }

        public static void PresentationHandshakeUsesActualPlaybackOrigin()
        {
            var machine = new InteractionRunStateMachine(
                new AssistanceBlockAllocator(92),
                CreateGenerator()
            );
            RunPlan plan = machine.Start(CreateRequest(93, "P001"));
            machine.HostScheduled(FixedUtc.AddSeconds(2));
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
                completed.Seal(data => completedSummary.SealCompleted(
                    7d,
                    FixedUtc.AddSeconds(7),
                    data
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
                aborted.Seal(data => abortedSummary.SealAborted(
                    0.1d,
                    FixedUtc.AddMilliseconds(100),
                    "safety",
                    data
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
                InteractionEventNames.HostReady,
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
                writer.FlushPhase();
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

        public static void RestartDiscoversPendingRun()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan plan = CreatePlan(50, "P001");
                InteractionCaptureWriter writer = CreateWriter(root, plan);
                var summary = new InteractionSummaryTracker(plan.RunId);
                writer.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                writer.RecordEvent(
                    InteractionEventNames.RunAborted,
                    null,
                    0.1d,
                    FixedUtc,
                    1
                );
                writer.Seal(data => summary.SealAborted(
                    0.1d,
                    FixedUtc,
                    "restart_fixture",
                    data
                ));
                IReadOnlyList<InteractionPendingRun> pending =
                    InteractionPendingRunDiscovery.Discover(root);
                Require(
                    pending.Count == 1 && pending[0].RunId == plan.RunId &&
                    pending[0].IsSealed && pending[0].NeedsUpload,
                    "Restart recovery did not discover the pending Run."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void RestartFlagsAbnormalPartialRunForRecovery()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan plan = CreatePlan(51, "P001");
                InteractionCaptureWriter writer = CreateWriter(root, plan);
                writer.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                writer.BeginCapture();
                writer.TryWritePose(CreatePose(1L, 1d, 1));
                writer.Dispose();

                IReadOnlyList<InteractionPendingRun> pending =
                    InteractionPendingRunDiscovery.Discover(root);
                Require(
                    pending.Count == 1 && pending[0].RunId == plan.RunId &&
                    pending[0].NeedsRecovery && pending[0].NeedsAttention &&
                    !pending[0].NeedsUpload,
                    "Abnormal partial Run was not flagged for recovery."
                );
                Require(
                    Directory.GetFiles(
                        pending[0].DirectoryPath,
                        "*.partial"
                    ).Length > 0,
                    "Abnormal Run did not retain partial capture files."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void PartialRunCanBeTerminalizedAfterRestart()
        {
            string root = CreateTemporaryRoot();
            try
            {
                RunPlan plan = CreatePlan(52, "P001");
                InteractionCaptureWriter writer = CreateWriter(root, plan);
                writer.RecordEvent(
                    InteractionEventNames.RunCreated,
                    null,
                    0d,
                    FixedUtc,
                    0
                );
                writer.BeginCapture();
                writer.TryWritePose(CreatePose(1L, 1d, 1));
                writer.Dispose();
                InteractionPendingRun pending =
                    InteractionPendingRunDiscovery.Discover(root).Single();
                Require(pending.NeedsRecovery, "Fixture was not partial.");

                InteractionPartialRunRecovery.TerminalizeAborted(
                    pending,
                    "process_restart",
                    FixedUtc.AddSeconds(2),
                    2d,
                    2
                );
                InteractionPendingRun recovered =
                    InteractionPendingRunDiscovery.Discover(root).Single();
                Require(
                    recovered.IsSealed && recovered.NeedsUpload &&
                    !recovered.NeedsRecovery,
                    "Recovered partial Run was not terminalized for upload."
                );
                AssertFinalFiles(recovered.DirectoryPath);
                string events = InteractionAtomicFile.ReadUtf8(
                    recovered.ArtifactPath(InteractionArtifactTypes.Events)
                );
                IDictionary<string, object> summary =
                    InteractionJson.ParseObject(InteractionAtomicFile.ReadUtf8(
                        recovered.ArtifactPath(
                            InteractionArtifactTypes.Summary
                        )
                    ));
                Require(
                    events.Contains("\"event_type\":\"run_aborted\"") &&
                    InteractionJson.RequireString(summary, "status") ==
                        "aborted" &&
                    InteractionJson.RequireString(summary, "abort_reason") ==
                        "process_restart",
                    "Partial recovery did not preserve explicit abort truth."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void AtomicBackupRecoversBeforeRead()
        {
            string root = CreateTemporaryRoot();
            try
            {
                string destination = Path.Combine(root, "upload.state.json");
                string backup = destination + ".replace-backup";
                File.WriteAllText(
                    backup,
                    "{\"schema_version\":1}\n",
                    new UTF8Encoding(false)
                );
                string recovered = InteractionAtomicFile.ReadUtf8(destination);
                Require(
                    File.Exists(destination) && !File.Exists(backup) &&
                    recovered == "{\"schema_version\":1}\n",
                    "A valid replace backup was not restored before read."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void AckRequiresWebcamAndAllFiveArtifacts()
        {
            string root = CreateTemporaryRoot();
            try
            {
                string directory = Path.Combine(root, "state");
                Directory.CreateDirectory(directory);
                var tracker = new InteractionUploadStateMachine(
                    "run_ack_test",
                    directory
                );
                tracker.Begin(FixedUtc);
                tracker.AwaitAck();
                InteractionHostArtifactAck missingWebcam =
                    InteractionHostArtifactAck.Parse(
                        "{\"run_id\":\"run_ack_test\"," +
                        "\"state\":\"Completed\",\"acknowledged\":false," +
                        "\"missing_artifacts\":[\"webcam\"]}",
                        "run_ack_test"
                    );
                Require(
                    !missingWebcam.ConfirmsAllRequiredArtifacts &&
                    !tracker.ApplyAck(missingWebcam, FixedUtc),
                    "ACK missing webcam became cleanable."
                );

                var completeTracker = new InteractionUploadStateMachine(
                    "run_ack_complete",
                    directory
                );
                completeTracker.Begin(FixedUtc);
                completeTracker.AwaitAck();
                InteractionHostArtifactAck full = InteractionHostArtifactAck.Parse(
                    "{\"run_id\":\"run_ack_complete\"," +
                    "\"state\":\"Completed\",\"acknowledged\":true," +
                    "\"missing_artifacts\":[]}",
                    "run_ack_complete"
                );
                Require(
                    full.ConfirmsAllRequiredArtifacts &&
                    completeTracker.ApplyAck(full, FixedUtc) &&
                    completeTracker.EligibleForLocalCleanup,
                    "Five-artifact ACK did not become cleanable."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void AckStateNeverMutatesSealedArtifacts()
        {
            string root = CreateTemporaryRoot();
            try
            {
                string runDirectory = Path.Combine(root, "run");
                Directory.CreateDirectory(runDirectory);
                string eventsPath = Path.Combine(
                    runDirectory,
                    InteractionStoragePaths.EventsFileName
                );
                File.WriteAllText(
                    eventsPath,
                    "{\"schema_version\":1,\"run_id\":\"run_immutable\"," +
                    "\"phase_id\":null,\"event_seq\":1," +
                    "\"monotonic_time_s\":0,\"utc_time\":" +
                    "\"2026-08-26T10:15:30Z\",\"frame\":0," +
                    "\"event_type\":\"upload_started\"," +
                    "\"actor_id\":null,\"target_id\":null," +
                    "\"payload\":{}}\n",
                    new UTF8Encoding(false)
                );
                byte[] before = File.ReadAllBytes(eventsPath);
                var tracker = new InteractionUploadStateMachine(
                    "run_immutable",
                    runDirectory
                );
                tracker.Begin(FixedUtc);
                tracker.AwaitAck();
                InteractionHostArtifactAck ack =
                    InteractionHostArtifactAck.Parse(
                        "{\"run_id\":\"run_immutable\"," +
                        "\"state\":\"Completed\",\"acknowledged\":true," +
                        "\"missing_artifacts\":[]}",
                        "run_immutable"
                    );
                Require(
                    tracker.ApplyAck(ack, FixedUtc.AddSeconds(1)),
                    "Complete ACK was not persisted."
                );
                byte[] after = File.ReadAllBytes(eventsPath);
                Require(
                    InteractionCaptureWriter.ByteArraysEqual(before, after) &&
                    File.Exists(Path.Combine(
                        runDirectory,
                        InteractionStoragePaths.UploadStateFileName
                    )),
                    "ACK mutated the sealed events artifact."
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void ArtifactRetrySnapshotStaysByteIdentical()
        {
            string root = CreateTemporaryRoot();
            try
            {
                string runDirectory = Path.Combine(root, "run");
                Directory.CreateDirectory(runDirectory);
                foreach (string type in InteractionArtifactTypes.QuestUploadTypes)
                {
                    File.WriteAllBytes(
                        Path.Combine(
                            runDirectory,
                            InteractionArtifactTypes.FileNameFor(type)
                        ),
                        new UTF8Encoding(false).GetBytes(
                            "{\"type\":\"" + type + "\"}\n"
                        )
                    );
                }
                InteractionFrozenArtifactSet frozen =
                    InteractionFrozenArtifactSet.ReadOnce(runDirectory);
                InteractionFrozenArtifact events = frozen.For(
                    InteractionArtifactTypes.Events
                );
                Require(
                    string.Equals(
                        Path.GetFullPath(events.Path),
                        Path.GetFullPath(Path.Combine(
                            runDirectory,
                            InteractionStoragePaths.EventsFileName
                        )),
                        StringComparison.OrdinalIgnoreCase
                    ) && events.ByteCount > 0L,
                    "Frozen upload is not backed by the final file path."
                );
                events.VerifyUnchanged();
                File.AppendAllText(
                    events.Path,
                    "{\"mutated\":true}\n",
                    new UTF8Encoding(false)
                );
                ExpectThrows<IOException>(() => events.VerifyUnchanged());
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void FrozenArtifactsEnforceHostByteLimits()
        {
            string root = CreateTemporaryRoot();
            try
            {
                string runDirectory = Path.Combine(root, "run");
                Directory.CreateDirectory(runDirectory);
                foreach (string type in InteractionArtifactTypes.QuestUploadTypes)
                {
                    File.WriteAllText(
                        Path.Combine(
                            runDirectory,
                            InteractionArtifactTypes.FileNameFor(type)
                        ),
                        type == InteractionArtifactTypes.Summary
                            ? "{\"schema_version\":1}\n"
                            : type == InteractionArtifactTypes.Events
                                ? "{\"schema_version\":1}\n"
                            : string.Empty,
                        new UTF8Encoding(false)
                    );
                }
                string summaryPath = Path.Combine(
                    runDirectory,
                    InteractionStoragePaths.SummaryFileName
                );
                using (var stream = new FileStream(
                    summaryPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.Read))
                {
                    stream.SetLength(
                        InteractionArtifactSizeLimits.MaximumBytesFor(
                            InteractionArtifactTypes.Summary
                        ) + 1L
                    );
                }
                ExpectThrows<IOException>(() =>
                    InteractionFrozenArtifactSet.ReadOnce(runDirectory)
                );
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static void W3AccurateResponsesAndTerminalBodiesMatch()
        {
            const string runId = "run_w3_contract_test";
            Require(
                InteractionHostContractV1.NormalizeHttpBaseUrl(
                    "http://192.168.1.20:8011/"
                ) == "http://192.168.1.20:8011",
                "LAN HTTP base URL normalization changed."
            );
            ExpectThrows<ArgumentException>(() =>
                InteractionHostContractV1.NormalizeHttpBaseUrl(
                    "https://192.168.1.20:8011"
                )
            );
            ExpectThrows<ArgumentException>(() =>
                InteractionHostContractV1.NormalizeHttpBaseUrl(
                    "http://192.168.1.20:8011/nested"
                )
            );
            InteractionHostRegistration registration =
                InteractionHostRegistration.Parse(
                    "{\"accepted\":true,\"run_id\":\"" + runId +
                    "\",\"start_at_utc\":\"2026-08-26T10:15:32Z\"," +
                    "\"missing_artifacts\":[\"events\",\"poses\",\"objects\"," +
                    "\"summary\",\"webcam\"],\"state\":\"Scheduled\"}",
                    runId
                );
            Require(
                registration.Accepted && registration.RunId == runId &&
                registration.State == "Scheduled" &&
                registration.MissingArtifacts.Count == 5,
                "W3 registration response did not parse."
            );
            ExpectThrows<FormatException>(() =>
                InteractionHostRegistration.Parse(
                    "{\"accepted\":true,\"run_id\":\"" + runId +
                    "\",\"start_at_utc\":\"2026-08-26T10:15:32Z\"," +
                    "\"missing_artifacts\":[],\"state\":\"Running\"}",
                    runId
                )
            );

            byte[] abortBytes = InteractionHostContractV1.BuildTerminalBodyUtf8(
                runId,
                "aborted_utc",
                FixedUtc,
                "operator safety stop"
            );
            IDictionary<string, object> abort = InteractionJson.ParseObject(
                new UTF8Encoding(false, true).GetString(abortBytes)
            );
            Require(
                InteractionJson.RequireInt32(abort, "schema_version") == 1 &&
                InteractionJson.RequireString(abort, "run_id") == runId &&
                InteractionJson.RequireString(abort, "abort_reason") ==
                    "operator safety stop" &&
                abort.ContainsKey("aborted_utc") && !abort.ContainsKey("reason"),
                "Abort body does not match W3's strict request model."
            );

            byte[] completeBytes = InteractionHostContractV1.BuildTerminalBodyUtf8(
                runId,
                "completed_utc",
                FixedUtc,
                null
            );
            IDictionary<string, object> complete = InteractionJson.ParseObject(
                new UTF8Encoding(false, true).GetString(completeBytes)
            );
            Require(
                InteractionJson.RequireInt32(complete, "schema_version") == 1 &&
                InteractionJson.RequireString(complete, "run_id") == runId &&
                complete.ContainsKey("completed_utc") &&
                !complete.ContainsKey("abort_reason"),
                "Complete body does not match W3's strict request model."
            );
            ExpectThrows<ArgumentException>(() =>
                InteractionHostContractV1.BuildTerminalBodyUtf8(
                    runId,
                    "completed_utc",
                    FixedUtc,
                    "not_allowed"
                )
            );
        }

        public static void QuestHeartbeatContractIsExplicit()
        {
            byte[] bytes = InteractionHostContractV1.BuildQuestHeartbeatBodyUtf8(
                "quest_alpha",
                true,
                123456789L,
                7L
            );
            IDictionary<string, object> body = InteractionJson.ParseObject(
                new UTF8Encoding(false, true).GetString(bytes)
            );
            string[] exactFields =
            {
                "schema_version", "quest_device_id", "ready",
                "heartbeat_generation", "heartbeat_sequence"
            };
            Require(
                InteractionHostContractV1.QuestHeartbeatPath ==
                    "/api/interaction/readiness/quest" &&
                new HashSet<string>(body.Keys, StringComparer.Ordinal)
                    .SetEquals(exactFields) &&
                InteractionJson.RequireInt32(body, "schema_version") == 1 &&
                InteractionJson.RequireString(body, "quest_device_id") ==
                    "quest_alpha" &&
                InteractionJson.OptionalBoolean(body, "ready") == true &&
                InteractionJson.RequireInt64(
                    body,
                    "heartbeat_generation"
                ) == 123456789L &&
                InteractionJson.RequireInt64(
                    body,
                    "heartbeat_sequence"
                ) == 7L,
                "Quest heartbeat seam is not explicit Contract V1 JSON."
            );
        }

        public static void HeartbeatLifecycleHasOneRoutineAcrossPauseResume()
        {
            var state = new InteractionHeartbeatLoopState();
            Require(
                state.TryStart(123456789L) &&
                !state.TryStart(123456789L) &&
                state.RoutineActive && state.NextSequence() == 1L,
                "Enable created zero or duplicate heartbeat routines."
            );
            Require(
                state.Stop() && !state.Stop() && !state.RoutineActive,
                "Pause/disable did not cancel exactly one routine."
            );
            Require(
                state.TryStart(123456789L) && state.NextSequence() == 2L &&
                state.Generation == 123456789L,
                "Resume did not restart once with a monotonic sequence."
            );
        }

        public static void ReadinessGenerationRejectsStaleCallbacks()
        {
            var gate = new InteractionLatestResponseGate();
            long first = gate.Issue();
            long second = gate.Issue();
            Require(
                !gate.TryAccept(first) && gate.TryAccept(second) &&
                !gate.TryAccept(first) && !gate.TryAccept(second),
                "An old or duplicate readiness callback refreshed state."
            );
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

        public static void LifecyclePolicyTerminalizesConsumedRuns()
        {
            Require(
                !InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.PreStart
                ) &&
                InteractionLifecycleTerminationPolicy.RequiresLocalAbort(
                    RunState.AwaitingHost
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

        public static void GeneratedFixtureCarriesW3StrictIdentity()
        {
            string root = CreateTemporaryRoot();
            try
            {
                string runDirectory = BuildStrictHostFixture(root);
                IDictionary<string, object> manifest = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(Path.Combine(
                        runDirectory,
                        InteractionStoragePaths.ManifestFileName
                    ))
                );
                string runId = InteractionJson.RequireString(manifest, "run_id");
                foreach (string type in InteractionArtifactTypes.QuestUploadTypes)
                {
                    string path = Path.Combine(
                        runDirectory,
                        InteractionArtifactTypes.FileNameFor(type)
                    );
                    string[] rows = File.ReadAllLines(
                        path,
                        new UTF8Encoding(false, true)
                    );
                    int records = 0;
                    for (int index = 0; index < rows.Length; index++)
                    {
                        if (string.IsNullOrWhiteSpace(rows[index]))
                        {
                            continue;
                        }
                        records++;
                        IDictionary<string, object> row =
                            InteractionJson.ParseObject(rows[index]);
                        Require(
                            InteractionJson.RequireInt32(
                                row,
                                "schema_version"
                            ) == 1 &&
                            InteractionJson.RequireString(row, "run_id") == runId,
                            type + " fixture row has mismatched identity."
                        );
                    }
                    Require(records > 0, type + " fixture is empty.");
                }
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        public static string ExportStrictHostFixture()
        {
            return BuildStrictHostFixture(CreateTemporaryRoot());
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
                    InteractionRunMode.Study,
                    true,
                    false,
                    true,
                    true
                )
            );
            ExpectThrows<InvalidOperationException>(() =>
                InteractionStudyStartPolicy.Validate(
                    InteractionRunMode.Study,
                    false,
                    false,
                    true,
                    false
                )
            );
            ExpectThrows<InvalidOperationException>(() =>
                InteractionStudyStartPolicy.Validate(
                    InteractionRunMode.EngineeringLocal,
                    false,
                    false,
                    true,
                    false
                )
            );
            InteractionStudyStartPolicy.Validate(
                InteractionRunMode.EngineeringLocal,
                false,
                true,
                true,
                false
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

        private static InteractionCaptureWriter CreateWriter(
            string root,
            RunPlan plan)
        {
            byte[] bytes = InteractionRunManifestContractV1.SerializeUtf8(plan);
            return InteractionCaptureWriter.CreateNew(root, plan, bytes);
        }

        private static string BuildStrictHostFixture(string root)
        {
            RunPlan plan = CreatePlan(63, "P901");
            InteractionCaptureWriter writer = CreateWriter(root, plan);
            var summary = new InteractionSummaryTracker(plan.RunId);
            writer.RecordEvent(
                InteractionEventNames.RunCreated,
                null,
                0d,
                FixedUtc,
                0,
                actorId: "quest"
            );
            writer.BeginCapture();
            summary.BeginRun(1d, FixedUtc.AddSeconds(1));
            for (int phaseId = 1; phaseId <= 6; phaseId++)
            {
                double phaseStart = phaseId;
                summary.BeginPhase(phaseId, phaseStart);
                summary.RecordAttempt(phaseId, true, phaseStart + 0.1d);
                summary.FinishPhase(
                    phaseId,
                    completed: true,
                    stuck: false,
                    monotonicTimeSeconds: phaseStart + 0.5d
                );
            }
            WriteOnePoseAndObject(writer);
            writer.RecordEvent(
                InteractionEventNames.RunCompleted,
                null,
                7d,
                FixedUtc.AddSeconds(7),
                7
            );
            writer.RecordEvent(
                InteractionEventNames.UploadStarted,
                null,
                7d,
                FixedUtc.AddSeconds(7),
                7,
                payloadJson: "{\"terminal_status\":\"completed\"}"
            );
            writer.Seal(data => summary.SealCompleted(
                7d,
                FixedUtc.AddSeconds(7),
                data
            ));
            return writer.RunDirectory;
        }

        private static void WriteOnePoseAndObject(
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

        private static RunPlan CreatePlan(int seed, string participantId)
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

        private static string CreateTemporaryRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "signvr-w6-tests-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteTemporaryRoot(string root)
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

        private static void Require(bool condition, string message)
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
                string workerName)
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
        }
    }
}
#endif
