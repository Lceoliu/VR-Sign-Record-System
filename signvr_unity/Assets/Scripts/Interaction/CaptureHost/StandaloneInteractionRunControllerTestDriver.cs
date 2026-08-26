#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    public static class StandaloneInteractionRunControllerTestDriver
    {
        public static void StartupRecoveryInProgressBlocksStartUntilSuccess()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            GameObject owner = null;
            try
            {
                owner = new GameObject("Standalone Controller Recovery Gate");
                owner.SetActive(false);
                InteractionRunController controller =
                    owner.AddComponent<InteractionRunController>();
                InstallPreStart(controller, root);

                controller.BeginStandaloneStartupRecoveryForTests();

                bool blocked = !controller.CanStart(out string blockedReason);
                Require(
                    blocked && controller.StartupRecoveryStatus ==
                        InteractionStandaloneLocalRunRecoveryStatus.Recovering &&
                    blockedReason.IndexOf(
                        "recovery",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0,
                    "CanStart did not fail closed while startup recovery was pending."
                );

                Require(
                    controller.CompleteStandaloneStartupRecoveryForTests(
                        TimeSpan.FromSeconds(5)
                    ),
                    "Deterministic startup recovery did not complete."
                );
                Require(
                    controller.StartupRecoveryStatus ==
                        InteractionStandaloneLocalRunRecoveryStatus.Succeeded &&
                    controller.CanStart(out string readyReason) &&
                    string.IsNullOrEmpty(readyReason),
                    "Successful startup recovery did not release the Start gate."
                );
            }
            finally
            {
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void WriterInitializationSchedulesRunLocally()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter writer = null;
            try
            {
                owner = new GameObject("Standalone Controller Local Schedule");
                owner.SetActive(false);
                InteractionRunController controller =
                    owner.AddComponent<InteractionRunController>();
                InstallPreStart(controller, root);
                controller.BeginStandaloneStartupRecoveryForTests();
                Require(
                    controller.CompleteStandaloneStartupRecoveryForTests(
                        TimeSpan.FromSeconds(5)
                    ) && controller.CanStart(out _),
                    "Local schedule fixture was not ready to start."
                );

                Require(
                    controller.TryStartRun(out string startError),
                    "Standalone TryStartRun failed: " + startError
                );
                Require(
                    controller.WaitForCaptureInitializationForTests(
                        TimeSpan.FromSeconds(5)
                    ),
                    "Standalone capture writer initialization timed out."
                );
                controller.ReconcileConsumedRunInitializationForTests();
                writer = controller.CaptureWriterForTests;

                Require(
                    controller.State == RunState.Scheduled &&
                    controller.Plan != null && writer != null &&
                    writer.RunId == controller.Plan.RunId,
                    "Writer initialization did not directly schedule the local Run."
                );
                string expectedRoot = InteractionStoragePaths.GetInteractionRoot(
                    root
                );
                Require(
                    Path.GetFullPath(writer.RunDirectory).StartsWith(
                        expectedRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    ),
                    "Controller ignored the injected standalone storage root."
                );
                Require(
                    controller.TryAbortRun(
                        "local_schedule_test_cleanup",
                        out string abortError
                    ),
                    "Local schedule cleanup Abort failed: " + abortError
                );
                Require(
                    controller.TerminalizationForTests.Wait(
                        TimeSpan.FromSeconds(10)
                    ),
                    "Local schedule cleanup seal timed out."
                );
                controller.ReconcileTerminalSealForTests();
                string events = File.ReadAllText(Path.Combine(
                    writer.RunDirectory,
                    InteractionStoragePaths.EventsFileName
                ));
                Require(
                    events.IndexOf(
                        "\"event_type\":\"run_created\"",
                        StringComparison.Ordinal
                    ) >= 0 &&
                    events.IndexOf("host_ready", StringComparison.Ordinal) < 0 &&
                    events.IndexOf("upload_started", StringComparison.Ordinal) < 0,
                    "Local schedule emitted a Host or upload lifecycle event."
                );
            }
            finally
            {
                DisposeAndWaitForCaptureStreams(writer);
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void CompletedSealIsTerminalWithoutUploadState()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter writer = null;
            try
            {
                CreateTerminalFixture(
                    root,
                    seed: 902,
                    participantId: "P902",
                    completing: true,
                    out owner,
                    out InteractionRunController controller,
                    out writer
                );

                controller.BeginCompletedSealForTests(10d);
                Require(
                    controller.TerminalizationForTests.Wait(
                        TimeSpan.FromSeconds(10)
                    ),
                    "Completed local seal timed out."
                );
                controller.ReconcileTerminalSealForTests();

                Require(
                    controller.State == RunState.Completed && writer.IsSealed,
                    "Completed local seal did not become the terminal Run state."
                );
                AssertStandaloneTerminal(
                    controller,
                    writer,
                    expectedStatus: "completed",
                    expectedEvent: InteractionEventNames.RunCompleted
                );
            }
            finally
            {
                DisposeAndWaitForCaptureStreams(writer);
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void AbortedSealIsTerminalWithoutUploadState()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter writer = null;
            try
            {
                CreateTerminalFixture(
                    root,
                    seed: 903,
                    participantId: "P903",
                    completing: false,
                    out owner,
                    out InteractionRunController controller,
                    out writer
                );

                Require(
                    controller.TryAbortRun(
                        "standalone_operator_abort",
                        out string abortError
                    ),
                    "Standalone Abort was rejected: " + abortError
                );
                Require(
                    controller.TerminalizationForTests.Wait(
                        TimeSpan.FromSeconds(10)
                    ),
                    "Aborted local seal timed out."
                );
                controller.ReconcileTerminalSealForTests();

                Require(
                    controller.State == RunState.Aborted && writer.IsSealed,
                    "Aborted local seal did not become the terminal Run state."
                );
                AssertStandaloneTerminal(
                    controller,
                    writer,
                    expectedStatus: "aborted",
                    expectedEvent: InteractionEventNames.RunAborted
                );
            }
            finally
            {
                DisposeAndWaitForCaptureStreams(writer);
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void ApplicationPauseSealsConsumedRunExactlyOnce()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            GameObject owner = null;
            InteractionCaptureWriter writer = null;
            try
            {
                CreateTerminalFixture(
                    root,
                    seed: 904,
                    participantId: "P904",
                    completing: false,
                    out owner,
                    out InteractionRunController controller,
                    out writer
                );

                controller.ProcessApplicationPauseForTests();
                controller.ProcessApplicationPauseForTests();
                Require(
                    controller.WaitForLifecycleTerminalizationForTests(
                        TimeSpan.FromSeconds(10)
                    ),
                    "Application pause did not finish its local terminal owner."
                );
                Require(
                    controller.State == RunState.Aborted && writer.IsSealed,
                    "Application pause did not publish the sealed Aborted state."
                );
                AssertStandaloneTerminal(
                    controller,
                    writer,
                    expectedStatus: "aborted",
                    expectedEvent: InteractionEventNames.RunAborted
                );
                string events = File.ReadAllText(Path.Combine(
                    writer.RunDirectory,
                    InteractionStoragePaths.EventsFileName
                ));
                Require(
                    CountOccurrences(
                        events,
                        "\"event_type\":\"" +
                            InteractionEventNames.RunAborted + "\""
                    ) == 1,
                    "Repeated application pause wrote more than one terminal event."
                );
            }
            finally
            {
                DisposeAndWaitForCaptureStreams(writer);
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void StartupRecoveryFailureBlocksStartAndPreservesEvidence()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            GameObject owner = null;
            try
            {
                string evidenceDirectory = InteractionStoragePaths.GetRunDirectory(
                    root,
                    "damaged-batch",
                    "P905",
                    "run_damaged_manifest"
                );
                Directory.CreateDirectory(evidenceDirectory);
                string evidencePath = Path.Combine(
                    evidenceDirectory,
                    InteractionStoragePaths.ManifestFileName
                );
                File.WriteAllText(evidencePath, "{damaged-manifest");

                owner = new GameObject("Standalone Recovery Failure Gate");
                owner.SetActive(false);
                InteractionRunController controller =
                    owner.AddComponent<InteractionRunController>();
                InstallPreStart(controller, root);
                controller.BeginStandaloneStartupRecoveryForTests();

                Require(
                    controller.CompleteStandaloneStartupRecoveryForTests(
                        TimeSpan.FromSeconds(5)
                    ),
                    "Damaged-manifest recovery did not complete deterministically."
                );
                bool blocked = !controller.CanStart(out string reason);
                Require(
                    controller.StartupRecoveryStatus ==
                        InteractionStandaloneLocalRunRecoveryStatus.Failed &&
                    blocked && !string.IsNullOrWhiteSpace(reason) &&
                    !string.IsNullOrWhiteSpace(
                        controller.StartupRecoveryFailureReason
                    ),
                    "Recovery failure did not fail the Start gate closed."
                );
                Require(
                    Directory.Exists(evidenceDirectory) &&
                    File.Exists(evidencePath) &&
                    File.ReadAllText(evidencePath) == "{damaged-manifest",
                    "Recovery failure modified or removed the original evidence."
                );
            }
            finally
            {
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        private static void CreateTerminalFixture(
            string root,
            int seed,
            string participantId,
            bool completing,
            out GameObject owner,
            out InteractionRunController controller,
            out InteractionCaptureWriter writer)
        {
            owner = new GameObject("Standalone Controller Terminal Fixture");
            owner.SetActive(false);
            controller = owner.AddComponent<InteractionRunController>();
            InteractionRunStateMachine machine = completing
                ? W6InteractionCaptureHostTestDriver
                    .CreateCompletingStateMachine(seed, participantId)
                : W6InteractionCaptureHostTestDriver
                    .CreateRunningStateMachine(seed, participantId);
            writer = W6InteractionCaptureHostTestDriver.CreateWriter(
                root,
                machine.Plan
            );
            writer.BeginCapture();
            W6InteractionCaptureHostTestDriver.WriteOnePoseAndObject(writer);
            InteractionSummaryTracker summary;
            if (completing)
            {
                summary = W6InteractionCaptureHostTestDriver
                    .CreateCompletedSummary(machine.Plan.RunId);
            }
            else
            {
                summary = new InteractionSummaryTracker(machine.Plan.RunId);
                summary.BeginRun(
                    0d,
                    W6InteractionCaptureHostTestDriver.FixedUtc
                );
                summary.BeginPhase(1, 0.1d);
            }
            controller.InstallDeterministicScenarioForTests(
                machine,
                summary,
                writer,
                initialization: null,
                persistentDataPath: root
            );
        }

        private static void AssertStandaloneTerminal(
            InteractionRunController controller,
            InteractionCaptureWriter writer,
            string expectedStatus,
            string expectedEvent)
        {
            string[] files =
            {
                InteractionStoragePaths.ManifestFileName,
                InteractionStoragePaths.EventsFileName,
                InteractionStoragePaths.PosesFileName,
                InteractionStoragePaths.ObjectsFileName,
                InteractionStoragePaths.SummaryFileName
            };
            for (int index = 0; index < files.Length; index++)
            {
                Require(
                    File.Exists(Path.Combine(writer.RunDirectory, files[index])),
                    "Standalone terminal Run is missing " + files[index] + "."
                );
            }
            Require(
                !File.Exists(Path.Combine(
                    writer.RunDirectory,
                    InteractionStoragePaths.UploadStateFileName
                )),
                "Standalone terminal Run created remote-transfer state."
            );

            controller.RefreshPendingRuns();
            InteractionPendingRun pending = null;
            for (int index = 0; index < controller.PendingRuns.Count; index++)
            {
                if (controller.PendingRuns[index].RunId == writer.RunId)
                {
                    pending = controller.PendingRuns[index];
                    break;
                }
            }
            Require(
                pending != null && pending.IsLocallyComplete &&
                !pending.IsAcknowledged && !pending.NeedsUpload &&
                !pending.NeedsRecovery && !pending.NeedsAttention,
                "A sealed Quest-local Run was not classified as locally complete."
            );

            string events = File.ReadAllText(Path.Combine(
                writer.RunDirectory,
                InteractionStoragePaths.EventsFileName
            ));
            string summary = File.ReadAllText(Path.Combine(
                writer.RunDirectory,
                InteractionStoragePaths.SummaryFileName
            ));
            Require(
                events.IndexOf(
                    "\"event_type\":\"" + expectedEvent + "\"",
                    StringComparison.Ordinal
                ) >= 0 &&
                events.IndexOf("upload_started", StringComparison.Ordinal) < 0 &&
                summary.IndexOf(
                    "\"status\":\"" + expectedStatus + "\"",
                    StringComparison.Ordinal
                ) >= 0,
                "Standalone terminal artifacts contain the wrong lifecycle."
            );
        }

        private static int CountOccurrences(string value, string needle)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(
                needle,
                offset,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += needle.Length;
            }
            return count;
        }

        private static void DisposeAndWaitForCaptureStreams(
            InteractionCaptureWriter writer)
        {
            if (writer == null)
            {
                return;
            }
            string runDirectory = writer.RunDirectory;
            writer.Dispose();
            Require(
                SpinWait.SpinUntil(
                    () => CanOpenCapturePartialsExclusively(runDirectory),
                    TimeSpan.FromSeconds(5)
                ),
                "Capture streams did not close within the test timeout."
            );
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
                return false;
            }
        }

        private static void InstallPreStart(
            InteractionRunController controller,
            string storageRoot)
        {
            InstructionContentCatalog catalog = CreateCatalog();
            var machine = new InteractionRunStateMachine(
                new AssistanceBlockAllocator(901),
                new RunPlanGenerator()
            );
            controller.InstallStandalonePreStartForTests(
                machine,
                catalog,
                storageRoot,
                debugBuild: true
            );
            controller.ConfigureIdentity(
                "pilot-standalone",
                "P901",
                "standalone-controller-test"
            );
            controller.ConfigureMode(
                InteractionRunMode.EngineeringLocal,
                configuredDebugOverridesActive: false,
                explicitlyArmEngineeringLocal: true
            );
        }

        private static InstructionContentCatalog CreateCatalog()
        {
            var values = new List<InstructionContentReference>(31);
            DateTimeOffset recordedUtc =
                new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
            for (int sentence = 1; sentence <= 31; sentence++)
            {
                string id = sentence.ToString("D3");
                values.Add(new InstructionContentReference(
                    PhaseSentenceRanges.GetPhaseId(id),
                    id,
                    "wang",
                    "take_" + id,
                    recordedUtc.AddMinutes(sentence),
                    1,
                    "wang/sentence_" + id + "/take.pose.jsonl",
                    new string('a', 64)
                ));
            }
            return new InstructionContentCatalog(values);
        }

        private static void Require(bool condition, string message)
        {
            W6InteractionCaptureHostTestDriver.Require(condition, message);
        }
    }
}
#endif
