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
    public static class StandaloneLocalRunRecoveryTestDriver
    {
        private static readonly DateTimeOffset FixedUtc =
            new DateTimeOffset(2026, 8, 26, 10, 15, 30, TimeSpan.Zero);

        public static void SealedCompletedRunIsLocallyCompleteWithoutHostAck()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                CreateCompletedRun(root, 701, "P701");

                InteractionPendingRun run = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();

                RequireLocallyComplete(run, "Completed");
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void SealedAbortedRunIsLocallyCompleteWithoutHostAck()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                CreateAbortedRun(root, 702, "P702");

                InteractionPendingRun run = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();

                RequireLocallyComplete(run, "Aborted");
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void StartupRecoverySealsOnlyUnsealedRunAndPreservesValidRows()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                string completedDirectory = CreateCompletedRun(
                    root,
                    703,
                    "P703"
                );
                IDictionary<string, byte[]> completedEvidence = SnapshotFiles(
                    completedDirectory
                );
                PartialFixture partial = CreatePartialRun(root, 704, "P704");
                string[] originalEvents = ReadNonEmptyLines(partial.EventsPartial);
                string[] originalPoses = ReadNonEmptyLines(partial.PosesPartial);
                string[] originalObjects = ReadNonEmptyLines(partial.ObjectsPartial);

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);

                Require(
                    coordinator.RecoveredRunCount == 1,
                    "Startup recovery did not report exactly one recovered Run."
                );
                IReadOnlyList<InteractionPendingRun> runs =
                    InteractionPendingRunDiscovery.DiscoverQuestLocal(root);
                Require(runs.Count == 2, "Standalone discovery lost a sealed Run.");
                InteractionPendingRun recovered = runs.Single(
                    value => value.RunId == partial.RunId
                );
                InteractionPendingRun untouched = runs.Single(
                    value => value.DirectoryPath == completedDirectory
                );
                RequireLocallyComplete(recovered, "Recovered");
                RequireLocallyComplete(untouched, "Existing completed");
                RequireEvidenceUnchanged(
                    completedDirectory,
                    completedEvidence
                );
                RequireFiveSealedFiles(recovered.DirectoryPath);
                Require(
                    Directory.GetFiles(
                        recovered.DirectoryPath,
                        "*.partial",
                        SearchOption.TopDirectoryOnly
                    ).Length == 0,
                    "Successful recovery retained a known partial stream."
                );

                string[] recoveredEvents = ReadNonEmptyLines(
                    recovered.ArtifactPath(InteractionLocalArtifactTypes.Events)
                );
                string[] recoveredPoses = ReadNonEmptyLines(
                    recovered.ArtifactPath(InteractionLocalArtifactTypes.Poses)
                );
                string[] recoveredObjects = ReadNonEmptyLines(
                    recovered.ArtifactPath(InteractionLocalArtifactTypes.Objects)
                );
                Require(
                    recoveredEvents.Length == originalEvents.Length + 1 &&
                    recoveredEvents.Take(originalEvents.Length)
                        .SequenceEqual(originalEvents),
                    "Recovery did not preserve every valid event row before Abort."
                );
                IDictionary<string, object> abort = InteractionJson.ParseObject(
                    recoveredEvents[recoveredEvents.Length - 1]
                );
                Require(
                    InteractionJson.RequireString(abort, "event_type") ==
                        InteractionEventNames.RunAborted,
                    "Recovery did not append the explicit Abort event."
                );
                Require(
                    recoveredPoses.SequenceEqual(originalPoses) &&
                    recoveredObjects.SequenceEqual(originalObjects),
                    "Recovery invented or removed a pose/object row."
                );

                IDictionary<string, object> summary = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(recovered.ArtifactPath(
                        InteractionLocalArtifactTypes.Summary
                    ))
                );
                Require(
                    InteractionJson.RequireString(summary, "status") == "aborted" &&
                    InteractionJson.RequireString(summary, "abort_reason") ==
                        "app_start_partial_recovery",
                    "Recovery summary did not seal the Run as Aborted."
                );
                RequireFiveSealedFiles(recovered.DirectoryPath);
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void MalformedPoseEnvelopeIsDiscardedAndMarkedIncomplete()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                PartialFixture partial = CreatePartialRun(root, 715, "P715");
                File.WriteAllText(
                    partial.PosesPartial,
                    IdentityOnlyRow(partial.RunId) + Environment.NewLine
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);
                Require(
                    coordinator.RecoveredRunCount == 1,
                    "Malformed-pose recovery did not seal exactly one Run."
                );
                InteractionPendingRun recovered = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();
                RequireLocallyComplete(recovered, "Malformed-pose recovered");
                RequireMalformedStreamWasRejected(
                    recovered,
                    InteractionLocalArtifactTypes.Poses,
                    InteractionLocalArtifactTypes.Objects
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void MalformedObjectEnvelopeIsDiscardedAndMarkedIncomplete()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                PartialFixture partial = CreatePartialRun(root, 716, "P716");
                File.WriteAllText(
                    partial.ObjectsPartial,
                    IdentityOnlyRow(partial.RunId) + Environment.NewLine
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);
                Require(
                    coordinator.RecoveredRunCount == 1,
                    "Malformed-object recovery did not seal exactly one Run."
                );
                InteractionPendingRun recovered = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();
                RequireLocallyComplete(recovered, "Malformed-object recovered");
                RequireMalformedStreamWasRejected(
                    recovered,
                    InteractionLocalArtifactTypes.Objects,
                    InteractionLocalArtifactTypes.Poses
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void StartupRecoveryIsIdempotent()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                CreatePartialRun(root, 705, "P705");
                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);
                Require(
                    coordinator.RecoveredRunCount == 1,
                    "The first recovery did not seal its partial Run."
                );

                BeginAndWait(coordinator);

                Require(
                    coordinator.Status ==
                        InteractionStandaloneLocalRunRecoveryStatus.Succeeded &&
                    coordinator.RecoveredRunCount == 0 &&
                    string.IsNullOrEmpty(coordinator.FailureReason),
                    "A second recovery was not an idempotent zero-work success."
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void EmptyRunDirectoryBeforeManifestPublicationIsPruned()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                RunPlan plan = W6InteractionCaptureHostTestDriver.CreatePlan(
                    709,
                    "P709"
                );
                string runDirectory = InteractionStoragePaths.GetRunDirectory(
                    root,
                    plan.BatchId,
                    plan.ParticipantId,
                    plan.RunId
                );
                Directory.CreateDirectory(runDirectory);
                Require(
                    !Directory.EnumerateFileSystemEntries(runDirectory).Any(),
                    "The pre-manifest crash fixture was not empty."
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);

                Require(
                    coordinator.RecoveredRunCount == 0,
                    "An empty pre-manifest directory was counted as a recovered Run."
                );
                Require(
                    !Directory.Exists(runDirectory),
                    "Startup recovery retained the empty pre-manifest Run directory."
                );
                Require(
                    InteractionPendingRunDiscovery
                        .DiscoverQuestLocal(root)
                        .Count == 0,
                    "The pruned pre-manifest directory still blocked local discovery."
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void AtomicManifestTemporaryIsPublishedAndSealedAborted()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                RunPlan plan = W6InteractionCaptureHostTestDriver.CreatePlan(
                    710,
                    "P710"
                );
                string runDirectory = InteractionStoragePaths.GetRunDirectory(
                    root,
                    plan.BatchId,
                    plan.ParticipantId,
                    plan.RunId
                );
                Directory.CreateDirectory(runDirectory);
                byte[] expectedManifest =
                    InteractionRunManifestContractV1.SerializeUtf8(plan);
                string atomicManifest = Path.Combine(
                    runDirectory,
                    "." + InteractionStoragePaths.ManifestFileName +
                        ".atomic.0123456789abcdef0123456789abcdef.tmp"
                );
                File.WriteAllBytes(atomicManifest, expectedManifest);
                Require(
                    Directory.GetFiles(runDirectory).Length == 1,
                    "The atomic-manifest crash fixture contained extra evidence."
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);

                Require(
                    coordinator.RecoveredRunCount == 1,
                    "Startup recovery did not seal the atomic-manifest Run."
                );
                InteractionPendingRun recovered = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();
                RequireLocallyComplete(recovered, "Atomic-manifest recovered");
                Require(
                    !File.Exists(atomicManifest) &&
                    BytesEqual(
                        expectedManifest,
                        File.ReadAllBytes(recovered.ManifestPath)
                    ),
                    "Recovery did not publish the exact atomic manifest bytes."
                );
                IDictionary<string, object> summary = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(recovered.ArtifactPath(
                        InteractionLocalArtifactTypes.Summary
                    ))
                );
                Require(
                    InteractionJson.RequireString(summary, "status") == "aborted" &&
                    InteractionJson.RequireString(summary, "abort_reason") ==
                        "app_start_partial_recovery",
                    "The atomic-manifest Run was not sealed as startup Aborted."
                );
                RequireFiveSealedFiles(runDirectory);
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void SealedAbortedRunCleansKnownPartialResidueIdempotently()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                string runDirectory = CreateAbortedRun(root, 711, "P711");
                IDictionary<string, byte[]> sealedEvidence = SnapshotFiles(
                    runDirectory
                );
                string[] streamTypes =
                {
                    InteractionLocalArtifactTypes.Events,
                    InteractionLocalArtifactTypes.Poses,
                    InteractionLocalArtifactTypes.Objects
                };
                for (int index = 0; index < streamTypes.Length; index++)
                {
                    string finalPath = Path.Combine(
                        runDirectory,
                        InteractionLocalArtifactTypes.FileNameFor(
                            streamTypes[index]
                        )
                    );
                    File.Copy(
                        finalPath,
                        KnownPartialPath(runDirectory, streamTypes[index])
                    );
                }
                Require(
                    Directory.GetFiles(
                        runDirectory,
                        "*.partial",
                        SearchOption.TopDirectoryOnly
                    ).Length == 3,
                    "The post-summary crash fixture did not retain three partials."
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);

                Require(
                    coordinator.RecoveredRunCount == 0,
                    "Residue cleanup incorrectly re-terminalized a sealed Run."
                );
                InteractionPendingRun recovered = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();
                RequireLocallyComplete(recovered, "Residue-cleaned Aborted");
                RequireEvidenceUnchanged(runDirectory, sealedEvidence);
                IDictionary<string, object> summary = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(recovered.ArtifactPath(
                        InteractionLocalArtifactTypes.Summary
                    ))
                );
                Require(
                    InteractionJson.RequireString(summary, "status") == "aborted",
                    "Idempotent residue cleanup changed the terminal Run status."
                );
                RequireFiveSealedFiles(runDirectory);
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void SealedRunCleansInterruptedStreamRecoveryTemporaries()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                string runDirectory = CreateAbortedRun(root, 712, "P712");
                IDictionary<string, byte[]> sealedEvidence = SnapshotFiles(
                    runDirectory
                );
                string[] residue = CreateStreamRecoveryTemporaryResidue(
                    runDirectory,
                    Path.Combine(
                        runDirectory,
                        InteractionStoragePaths.EventsFileName
                    ),
                    Path.Combine(
                        runDirectory,
                        InteractionStoragePaths.ObjectsFileName
                    )
                );
                Require(
                    residue.Length == 3 && residue.All(File.Exists),
                    "The sealed recovery-temp fixture was not created."
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);

                Require(
                    coordinator.RecoveredRunCount == 0,
                    "Internal residue cleanup re-terminalized a sealed Run."
                );
                InteractionPendingRun recovered = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();
                RequireLocallyComplete(recovered, "Recovery-temp-cleaned Aborted");
                RequireEvidenceUnchanged(runDirectory, sealedEvidence);
                Require(
                    residue.All(path => !File.Exists(path)),
                    "A sealed Run retained an interrupted stream-recovery temp."
                );
                RequireAbortedSummary(recovered);
                RequireFiveSealedFiles(runDirectory);
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void UnsealedRunCleansStreamRecoveryTempsBeforeReseal()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                PartialFixture partial = CreatePartialRun(root, 713, "P713");
                string[] residue = CreateStreamRecoveryTemporaryResidue(
                    partial.DirectoryPath,
                    partial.EventsPartial,
                    partial.ObjectsPartial
                );
                Require(
                    residue.Length == 3 && residue.All(File.Exists),
                    "The unsealed recovery-temp fixture was not created."
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);

                Require(
                    coordinator.RecoveredRunCount == 1,
                    "Startup recovery did not reseal the interrupted partial Run."
                );
                InteractionPendingRun recovered = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();
                RequireLocallyComplete(recovered, "Interrupted recovery resealed");
                Require(
                    residue.All(path => !File.Exists(path)),
                    "Resealing retained an interrupted stream-recovery temp."
                );
                RequireAbortedSummary(recovered);
                RequireFiveSealedFiles(partial.DirectoryPath);
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void SummaryAtomicTempPublishesAndCleansStreamRecoveryTemps()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                string runDirectory = CreateAbortedRun(root, 714, "P714");
                string summaryPath = Path.Combine(
                    runDirectory,
                    InteractionStoragePaths.SummaryFileName
                );
                byte[] expectedSummary = File.ReadAllBytes(summaryPath);
                string summaryAtomic = Path.Combine(
                    runDirectory,
                    "." + InteractionStoragePaths.SummaryFileName +
                        ".atomic.44444444444444444444444444444444.tmp"
                );
                File.Move(summaryPath, summaryAtomic);
                string[] residue = CreateStreamRecoveryTemporaryResidue(
                    runDirectory,
                    Path.Combine(
                        runDirectory,
                        InteractionStoragePaths.EventsFileName
                    ),
                    Path.Combine(
                        runDirectory,
                        InteractionStoragePaths.ObjectsFileName
                    )
                );
                Require(
                    !File.Exists(summaryPath) && File.Exists(summaryAtomic) &&
                    residue.All(File.Exists),
                    "The summary atomic crash fixture was not created."
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    Recover(root);

                Require(
                    coordinator.RecoveredRunCount == 0,
                    "Publishing a terminal summary re-terminalized the Run."
                );
                InteractionPendingRun recovered = InteractionPendingRunDiscovery
                    .DiscoverQuestLocal(root)
                    .Single();
                RequireLocallyComplete(recovered, "Atomic-summary recovered");
                Require(
                    !File.Exists(summaryAtomic) &&
                    BytesEqual(expectedSummary, File.ReadAllBytes(summaryPath)),
                    "Recovery did not publish the exact terminal summary bytes."
                );
                Require(
                    residue.All(path => !File.Exists(path)),
                    "Atomic summary publication retained stream-recovery residue."
                );
                RequireAbortedSummary(recovered);
                RequireFiveSealedFiles(runDirectory);
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void ManifestIdentityMismatchFailsClosedAndRetainsEvidence()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                PartialFixture partial = CreatePartialRun(root, 706, "P706");
                string manifestPath = Path.Combine(
                    partial.DirectoryPath,
                    InteractionLocalArtifactTypes.FileNameFor(
                        InteractionLocalArtifactTypes.Manifest
                    )
                );
                string manifest = InteractionAtomicFile.ReadUtf8(manifestPath);
                string mismatched = manifest.Replace(
                    "\"run_id\":\"" + partial.RunId + "\"",
                    "\"run_id\":\"wrong_run_identity\""
                );
                Require(mismatched != manifest, "Manifest fixture was not changed.");
                File.WriteAllText(manifestPath, mismatched);
                IDictionary<string, byte[]> evidence = SnapshotFiles(
                    partial.DirectoryPath
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    RecoverExpectingFailure(root);

                RequireFailureAndEvidence(
                    coordinator,
                    partial.DirectoryPath,
                    evidence,
                    "manifest"
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void CorruptManifestFailsClosedAndRetainsEvidence()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                PartialFixture partial = CreatePartialRun(root, 707, "P707");
                string manifestPath = Path.Combine(
                    partial.DirectoryPath,
                    InteractionLocalArtifactTypes.FileNameFor(
                        InteractionLocalArtifactTypes.Manifest
                    )
                );
                File.WriteAllText(manifestPath, "{not-json");
                IDictionary<string, byte[]> evidence = SnapshotFiles(
                    partial.DirectoryPath
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    RecoverExpectingFailure(root);

                RequireFailureAndEvidence(
                    coordinator,
                    partial.DirectoryPath,
                    evidence,
                    "manifest"
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void RecoveryWriteFailureFailsClosedAndRetainsEvidence()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                PartialFixture partial = CreatePartialRun(root, 708, "P708");
                string summaryPath = Path.Combine(
                    partial.DirectoryPath,
                    InteractionLocalArtifactTypes.FileNameFor(
                        InteractionLocalArtifactTypes.Summary
                    )
                );
                Directory.CreateDirectory(summaryPath);
                IDictionary<string, byte[]> evidence = SnapshotFiles(
                    partial.DirectoryPath
                );

                InteractionStandaloneLocalRunRecoveryCoordinator coordinator =
                    RecoverExpectingFailure(root);

                RequireFailureAndEvidence(
                    coordinator,
                    partial.DirectoryPath,
                    evidence,
                    string.Empty
                );
                Require(
                    Directory.Exists(summaryPath) &&
                    Directory.GetFiles(
                        partial.DirectoryPath,
                        "*.partial",
                        SearchOption.TopDirectoryOnly
                    ).Length == 3,
                    "A failed final write removed the original partial evidence."
                );
                InteractionPendingRun stillPending =
                    InteractionPendingRunDiscovery
                        .DiscoverQuestLocal(root)
                        .Single();
                Require(
                    stillPending.NeedsRecovery && !stillPending.IsSealed,
                    "A failed write was falsely classified as locally complete."
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        public static void RecoveryCoordinatorPublishesPollableStartupState()
        {
            string root = W6InteractionCaptureHostTestDriver.CreateTemporaryRoot();
            try
            {
                var coordinator =
                    new InteractionStandaloneLocalRunRecoveryCoordinator(root);
                Require(
                    coordinator.Status ==
                        InteractionStandaloneLocalRunRecoveryStatus.NotStarted,
                    "A new recovery coordinator did not start idle."
                );

                coordinator.Begin(
                    "app_start_partial_recovery",
                    FixedUtc,
                    0d,
                    0
                );
                Require(
                    coordinator.Status ==
                        InteractionStandaloneLocalRunRecoveryStatus.Recovering &&
                    coordinator.RecoveredRunCount == 0 &&
                    string.IsNullOrEmpty(coordinator.FailureReason),
                    "Begin did not publish a deterministic Recovering state."
                );
                Require(
                    coordinator.Wait(TimeSpan.FromSeconds(5)) &&
                    coordinator.Status ==
                        InteractionStandaloneLocalRunRecoveryStatus.Succeeded &&
                    coordinator.RecoveredRunCount == 0 &&
                    string.IsNullOrEmpty(coordinator.FailureReason),
                    "The coordinator did not publish its zero-work success result."
                );
            }
            finally
            {
                W6InteractionCaptureHostTestDriver.DeleteTemporaryRoot(root);
            }
        }

        private static InteractionStandaloneLocalRunRecoveryCoordinator Recover(
            string root)
        {
            var coordinator =
                new InteractionStandaloneLocalRunRecoveryCoordinator(root);
            BeginAndWait(coordinator);
            Require(
                coordinator.Status ==
                    InteractionStandaloneLocalRunRecoveryStatus.Succeeded &&
                string.IsNullOrEmpty(coordinator.FailureReason),
                "Standalone recovery unexpectedly failed: " +
                    coordinator.FailureReason
            );
            return coordinator;
        }

        private static InteractionStandaloneLocalRunRecoveryCoordinator
            RecoverExpectingFailure(string root)
        {
            var coordinator =
                new InteractionStandaloneLocalRunRecoveryCoordinator(root);
            BeginAndWait(coordinator);
            Require(
                coordinator.Status ==
                    InteractionStandaloneLocalRunRecoveryStatus.Failed &&
                coordinator.RecoveredRunCount == 0 &&
                !string.IsNullOrWhiteSpace(coordinator.FailureReason),
                "Standalone recovery did not fail closed."
            );
            return coordinator;
        }

        private static void BeginAndWait(
            InteractionStandaloneLocalRunRecoveryCoordinator coordinator)
        {
            coordinator.Begin(
                "app_start_partial_recovery",
                FixedUtc.AddSeconds(10),
                10d,
                10
            );
            Require(
                coordinator.Wait(TimeSpan.FromSeconds(5)),
                "Standalone recovery did not complete within the test timeout."
            );
        }

        private static string CreateCompletedRun(
            string root,
            int seed,
            string participantId)
        {
            RunPlan plan = W6InteractionCaptureHostTestDriver.CreatePlan(
                seed,
                participantId
            );
            InteractionCaptureWriter writer =
                W6InteractionCaptureHostTestDriver.CreateWriter(root, plan);
            InteractionSummaryTracker summary =
                W6InteractionCaptureHostTestDriver.CreateCompletedSummary(
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
            W6InteractionCaptureHostTestDriver.WriteOnePoseAndObject(writer);
            writer.RecordEvent(
                InteractionEventNames.RunCompleted,
                null,
                8d,
                FixedUtc.AddSeconds(8),
                8
            );
            W6InteractionCaptureHostTestDriver.Await(writer.BeginSeal(
                InteractionCaptureTerminalKind.Completed,
                data => summary.SealCompleted(
                    8d,
                    FixedUtc.AddSeconds(8),
                    data
                )
            ));
            return writer.RunDirectory;
        }

        private static string CreateAbortedRun(
            string root,
            int seed,
            string participantId)
        {
            RunPlan plan = W6InteractionCaptureHostTestDriver.CreatePlan(
                seed,
                participantId
            );
            InteractionCaptureWriter writer =
                W6InteractionCaptureHostTestDriver.CreateWriter(root, plan);
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
                FixedUtc.AddMilliseconds(100),
                1,
                payloadJson: "{\"reason\":\"test_abort\"}"
            );
            W6InteractionCaptureHostTestDriver.Await(writer.BeginSeal(
                InteractionCaptureTerminalKind.Aborted,
                data => summary.SealAborted(
                    0.1d,
                    FixedUtc.AddMilliseconds(100),
                    "test_abort",
                    data
                )
            ));
            return writer.RunDirectory;
        }

        private static PartialFixture CreatePartialRun(
            string root,
            int seed,
            string participantId)
        {
            RunPlan plan = W6InteractionCaptureHostTestDriver.CreatePlan(
                seed,
                participantId
            );
            InteractionCaptureWriter writer =
                W6InteractionCaptureHostTestDriver.CreateWriter(root, plan);
            writer.RecordEvent(
                InteractionEventNames.RunCreated,
                null,
                0d,
                FixedUtc,
                0
            );
            writer.BeginCapture();
            W6InteractionCaptureHostTestDriver.WriteOnePoseAndObject(writer);
            string directory = writer.RunDirectory;
            writer.Dispose();
            WaitForPartialStreamsToClose(directory);
            return new PartialFixture(directory, plan.RunId);
        }

        private static void WaitForPartialStreamsToClose(string runDirectory)
        {
            string[] partialPaths =
            {
                Path.Combine(
                    runDirectory,
                    "." + InteractionLocalArtifactTypes.FileNameFor(
                        InteractionLocalArtifactTypes.Events
                    ) + ".partial"
                ),
                Path.Combine(
                    runDirectory,
                    "." + InteractionLocalArtifactTypes.FileNameFor(
                        InteractionLocalArtifactTypes.Poses
                    ) + ".partial"
                ),
                Path.Combine(
                    runDirectory,
                    "." + InteractionLocalArtifactTypes.FileNameFor(
                        InteractionLocalArtifactTypes.Objects
                    ) + ".partial"
                )
            };
            Require(
                SpinWait.SpinUntil(
                    () => partialPaths.All(CanOpenExclusively),
                    TimeSpan.FromSeconds(5)
                ),
                "The partial capture streams did not close within the test timeout."
            );
        }

        private static bool CanOpenExclusively(string path)
        {
            try
            {
                using (new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None))
                {
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void RequireLocallyComplete(
            InteractionPendingRun run,
            string description)
        {
            Require(
                run.IsSealed && run.IsLocallyComplete &&
                !run.NeedsRecovery && !run.NeedsAttention,
                description +
                    " Run was not complete under Quest-local authority."
            );
        }

        private static void RequireMalformedStreamWasRejected(
            InteractionPendingRun recovered,
            string malformedArtifactType,
            string validArtifactType)
        {
            Require(
                ReadNonEmptyLines(
                    recovered.ArtifactPath(malformedArtifactType)
                ).Length == 0,
                "Recovery retained an identity-only " +
                    malformedArtifactType + " row."
            );
            Require(
                ReadNonEmptyLines(
                    recovered.ArtifactPath(validArtifactType)
                ).Length > 0,
                "Recovery discarded the valid " + validArtifactType +
                    " stream while rejecting another stream."
            );

            IDictionary<string, object> summary = InteractionJson.ParseObject(
                InteractionAtomicFile.ReadUtf8(recovered.ArtifactPath(
                    InteractionLocalArtifactTypes.Summary
                ))
            );
            IDictionary<string, object> completeness =
                InteractionJson.RequireObject(summary, "data_completeness");
            Require(
                completeness.TryGetValue(
                    malformedArtifactType,
                    out object malformedComplete
                ) && malformedComplete is bool && !(bool)malformedComplete,
                "Summary falsely marked malformed " + malformedArtifactType +
                    " evidence complete."
            );
            Require(
                completeness.TryGetValue(
                    validArtifactType,
                    out object validComplete
                ) && validComplete is bool && (bool)validComplete,
                "Summary did not retain completeness for valid " +
                    validArtifactType + " evidence."
            );
            Require(
                completeness.TryGetValue(
                    "quest_artifacts_complete",
                    out object allComplete
                ) && allComplete is bool && !(bool)allComplete,
                "Summary falsely marked the recovered artifact set complete."
            );
            RequireFiveSealedFiles(recovered.DirectoryPath);
        }

        private static string IdentityOnlyRow(string runId)
        {
            var builder = new StringBuilder();
            builder.Append("{\"schema_version\":1,\"run_id\":");
            InteractionJson.AppendQuoted(builder, runId);
            builder.Append('}');
            return builder.ToString();
        }

        private static void RequireFiveSealedFiles(string directory)
        {
            string[] names = InteractionLocalArtifactTypes.All
                .Select(InteractionLocalArtifactTypes.FileNameFor)
                .ToArray();
            Require(
                names.All(name => File.Exists(Path.Combine(directory, name))),
                "The locally sealed Run does not contain all five files."
            );
            string[] actual = Directory.GetFiles(directory)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] expected = names
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Require(
                actual.SequenceEqual(expected),
                "The locally sealed Run contains non-authoritative files."
            );
        }

        private static void RequireFailureAndEvidence(
            InteractionStandaloneLocalRunRecoveryCoordinator coordinator,
            string directory,
            IDictionary<string, byte[]> before,
            string failureFragment)
        {
            Require(
                Directory.Exists(directory),
                "Recovery failure removed the original Run directory."
            );
            RequireEvidenceUnchanged(directory, before);
            Require(
                string.IsNullOrEmpty(failureFragment) ||
                coordinator.FailureReason.IndexOf(
                    failureFragment,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0,
                "Recovery failure reason did not identify " + failureFragment + "."
            );
        }

        private static void RequireEvidenceUnchanged(
            string directory,
            IDictionary<string, byte[]> before)
        {
            foreach (KeyValuePair<string, byte[]> item in before)
            {
                string path = Path.Combine(directory, item.Key);
                Require(
                    File.Exists(path) && BytesEqual(
                        item.Value,
                        File.ReadAllBytes(path)
                    ),
                    "Recovery changed original evidence " + item.Key + "."
                );
            }
        }

        private static IDictionary<string, byte[]> SnapshotFiles(string directory)
        {
            return Directory.GetFiles(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly
                )
                .ToDictionary(
                    Path.GetFileName,
                    File.ReadAllBytes,
                    StringComparer.Ordinal
                );
        }

        private static string[] ReadNonEmptyLines(string path)
        {
            return File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
        }

        private static string KnownPartialPath(
            string directory,
            string artifactType)
        {
            return Path.Combine(
                directory,
                "." + InteractionLocalArtifactTypes.FileNameFor(artifactType) +
                    ".partial"
            );
        }

        private static string[] CreateStreamRecoveryTemporaryResidue(
            string directory,
            string eventsSource,
            string objectsSource)
        {
            string eventsTemporary = StreamRecoveryTemporaryPath(
                directory,
                InteractionLocalArtifactTypes.Events,
                "11111111111111111111111111111111"
            );
            string posesTemporary = StreamRecoveryTemporaryPath(
                directory,
                InteractionLocalArtifactTypes.Poses,
                "22222222222222222222222222222222"
            );
            string objectsTemporary = StreamRecoveryTemporaryPath(
                directory,
                InteractionLocalArtifactTypes.Objects,
                "33333333333333333333333333333333"
            );

            // A completed event temp, a truncated pose temp, and a completed
            // object temp model process death at different write points.
            File.Copy(eventsSource, eventsTemporary);
            File.WriteAllBytes(posesTemporary, new byte[] { (byte)'{' });
            File.Copy(objectsSource, objectsTemporary);
            return new[]
            {
                eventsTemporary,
                posesTemporary,
                objectsTemporary
            };
        }

        private static string StreamRecoveryTemporaryPath(
            string directory,
            string artifactType,
            string id)
        {
            return Path.Combine(
                directory,
                "." + InteractionLocalArtifactTypes.FileNameFor(artifactType) +
                    ".partial-recovery." + id + ".tmp"
            );
        }

        private static void RequireAbortedSummary(InteractionPendingRun run)
        {
            IDictionary<string, object> summary = InteractionJson.ParseObject(
                InteractionAtomicFile.ReadUtf8(run.ArtifactPath(
                    InteractionLocalArtifactTypes.Summary
                ))
            );
            Require(
                InteractionJson.RequireString(summary, "status") == "aborted",
                "Recovered terminal summary is not Aborted."
            );
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            return left != null && right != null &&
                left.Length == right.Length &&
                !left.Where((value, index) => value != right[index]).Any();
        }

        private static void Require(bool condition, string message)
        {
            W6InteractionCaptureHostTestDriver.Require(condition, message);
        }

        private sealed class PartialFixture
        {
            public PartialFixture(string directoryPath, string runId)
            {
                DirectoryPath = directoryPath;
                RunId = runId;
            }

            public string DirectoryPath { get; }
            public string RunId { get; }
            public string EventsPartial => PartialPath(
                InteractionLocalArtifactTypes.FileNameFor(
                    InteractionLocalArtifactTypes.Events
                )
            );
            public string PosesPartial => PartialPath(
                InteractionLocalArtifactTypes.FileNameFor(
                    InteractionLocalArtifactTypes.Poses
                )
            );
            public string ObjectsPartial => PartialPath(
                InteractionLocalArtifactTypes.FileNameFor(
                    InteractionLocalArtifactTypes.Objects
                )
            );

            private string PartialPath(string finalName)
            {
                return Path.Combine(
                    DirectoryPath,
                    "." + finalName + ".partial"
                );
            }
        }
    }
}
#endif
