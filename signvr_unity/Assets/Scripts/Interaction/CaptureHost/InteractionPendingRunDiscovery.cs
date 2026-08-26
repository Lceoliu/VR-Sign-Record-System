using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SignVR.Interaction.CaptureHost
{
    public sealed class InteractionPendingRun
    {
        internal InteractionPendingRun(
            string batchId,
            string participantId,
            string runId,
            string directoryPath,
            bool sealedCapture)
        {
            BatchId = batchId;
            ParticipantId = participantId;
            RunId = runId;
            DirectoryPath = directoryPath;
            IsSealed = sealedCapture;
        }

        public string BatchId { get; }
        public string ParticipantId { get; }
        public string RunId { get; }
        public string DirectoryPath { get; }
        public bool IsSealed { get; }
        public bool IsLocallyComplete => IsSealed;
        public bool NeedsRecovery => !IsSealed;
        public bool NeedsAttention => NeedsRecovery;

        public string ManifestPath => ArtifactPath(
            InteractionLocalArtifactTypes.Manifest
        );

        public string ArtifactPath(string artifactType)
        {
            string fileName = InteractionLocalArtifactTypes.FileNameFor(
                artifactType
            );
            return Path.Combine(DirectoryPath, fileName);
        }
    }

    public static class InteractionPendingRunDiscovery
    {
        public static IReadOnlyList<InteractionPendingRun> Discover(
            string persistentDataPath)
        {
            return DiscoverQuestLocal(persistentDataPath);
        }

        /// <summary>
        /// Discovers Quest-authoritative Runs for Standalone Study Mode. A
        /// locally sealed five-file Run is complete. Any Run
        /// directory with a missing, damaged, or mismatched manifest fails the
        /// startup scan closed instead of being silently ignored.
        /// </summary>
        public static IReadOnlyList<InteractionPendingRun> DiscoverQuestLocal(
            string persistentDataPath)
        {
            return DiscoverStrictLocal(persistentDataPath);
        }

        private static IReadOnlyList<InteractionPendingRun> DiscoverStrictLocal(
            string persistentDataPath)
        {
            string root = InteractionStoragePaths.GetInteractionRoot(
                persistentDataPath
            );
            if (!Directory.Exists(root))
            {
                return Array.Empty<InteractionPendingRun>();
            }
            if (IsReparsePoint(root))
            {
                throw new IOException(
                    "Interaction storage root cannot be a reparse point."
                );
            }

            var results = new List<InteractionPendingRun>();
            foreach (string batchDirectory in Directory.GetDirectories(root))
            {
                if (!TryValidateDirectorySegment(batchDirectory, out string batchId))
                {
                    continue;
                }
                foreach (string participantDirectory in
                    Directory.GetDirectories(batchDirectory))
                {
                    if (!TryValidateDirectorySegment(
                            participantDirectory,
                            out string participantId))
                    {
                        continue;
                    }
                    foreach (string runDirectory in
                        Directory.GetDirectories(participantDirectory))
                    {
                        InteractionAtomicFile.RecoverDirectory(runDirectory);
                        if (!TryValidateDirectorySegment(
                                runDirectory,
                                out string runId))
                        {
                            continue;
                        }
                        string expected = InteractionStoragePaths.GetRunDirectory(
                            persistentDataPath,
                            batchId,
                            participantId,
                            runId
                        );
                        if (!string.Equals(
                                Path.GetFullPath(runDirectory),
                                expected,
                                Path.DirectorySeparatorChar == '\\'
                                    ? StringComparison.OrdinalIgnoreCase
                                    : StringComparison.Ordinal))
                        {
                            continue;
                        }
                        string manifest = Path.Combine(
                            runDirectory,
                            InteractionLocalArtifactTypes.FileNameFor(
                                InteractionLocalArtifactTypes.Manifest
                            )
                        );
                        if (!InteractionAtomicFile
                                .TryRecoverInterruptedWriteNew(manifest))
                        {
                            if (!Directory.EnumerateFileSystemEntries(
                                    runDirectory
                                ).Any())
                            {
                                Directory.Delete(runDirectory, false);
                                continue;
                            }
                            throw new IOException(
                                "Quest-local Run manifest is missing from " +
                                runDirectory + "."
                            );
                        }
                        bool manifestMatches;
                        try
                        {
                            manifestMatches = ManifestMatchesPath(
                                manifest,
                                batchId,
                                participantId,
                                runId
                            );
                        }
                        catch (Exception exception) when (
                            exception is IOException ||
                            exception is FormatException ||
                            exception is UnauthorizedAccessException)
                        {
                            throw new IOException(
                                "Quest-local Run manifest is damaged at " +
                                manifest + ".",
                                exception
                            );
                        }
                        if (!manifestMatches)
                        {
                            throw new IOException(
                                "Quest-local Run manifest identity does not " +
                                "match its directory at " + manifest + "."
                            );
                        }
                        DeleteInterruptedStreamRecoveryTemporaries(runDirectory);

                        string summaryPath = Path.Combine(
                            runDirectory,
                            InteractionLocalArtifactTypes.FileNameFor(
                                InteractionLocalArtifactTypes.Summary
                            )
                        );
                        InteractionAtomicFile.TryRecoverInterruptedWriteNew(
                            summaryPath
                        );
                        bool sealedCapture = RequiredLocalArtifactFileNames.All(
                            file => File.Exists(Path.Combine(runDirectory, file))
                        );
                        if (sealedCapture)
                        {
                            ValidateTerminalSummary(
                                summaryPath,
                                runId
                            );
                            DeleteKnownPartialResidue(runDirectory);
                            ValidateExactLocalArtifactSet(runDirectory);
                        }
                        results.Add(new InteractionPendingRun(
                            batchId,
                            participantId,
                            runId,
                            runDirectory,
                            sealedCapture
                        ));
                    }
                }
            }

            results.Sort((left, right) => string.CompareOrdinal(
                left.DirectoryPath,
                right.DirectoryPath
            ));
            return results.AsReadOnly();
        }

        private static readonly string[] RequiredLocalArtifactFileNames =
            InteractionLocalArtifactTypes.All
                .Select(InteractionLocalArtifactTypes.FileNameFor)
                .ToArray();

        private static void DeleteKnownPartialResidue(string runDirectory)
        {
            string[] streamFileNames =
            {
                InteractionStoragePaths.EventsFileName,
                InteractionStoragePaths.PosesFileName,
                InteractionStoragePaths.ObjectsFileName
            };
            for (int index = 0; index < streamFileNames.Length; index++)
            {
                string partial = Path.Combine(
                    runDirectory,
                    "." + streamFileNames[index] + ".partial"
                );
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }
        }

        private static void DeleteInterruptedStreamRecoveryTemporaries(
            string runDirectory)
        {
            string[] streamFileNames =
            {
                InteractionStoragePaths.EventsFileName,
                InteractionStoragePaths.PosesFileName,
                InteractionStoragePaths.ObjectsFileName
            };
            for (int index = 0; index < streamFileNames.Length; index++)
            {
                string pattern = "." + streamFileNames[index] +
                    ".partial-recovery.*.tmp";
                string[] stale = Directory.GetFiles(
                    runDirectory,
                    pattern,
                    SearchOption.TopDirectoryOnly
                );
                for (int staleIndex = 0;
                    staleIndex < stale.Length;
                    staleIndex++)
                {
                    File.Delete(stale[staleIndex]);
                }
            }
        }

        private static void ValidateExactLocalArtifactSet(string runDirectory)
        {
            StringComparer comparer = Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            string[] actual = Directory.GetFiles(
                    runDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly
                )
                .Select(Path.GetFileName)
                .OrderBy(value => value, comparer)
                .ToArray();
            string[] expected = RequiredLocalArtifactFileNames
                .OrderBy(value => value, comparer)
                .ToArray();
            if (Directory.GetDirectories(
                    runDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly
                ).Length > 0 ||
                !actual.SequenceEqual(expected, comparer))
            {
                throw new IOException(
                    "Quest-local sealed Run contains non-authoritative " +
                    "files or directories at " + runDirectory + "."
                );
            }
        }

        private static bool TryValidateDirectorySegment(
            string path,
            out string value)
        {
            value = null;
            if (IsReparsePoint(path))
            {
                return false;
            }
            try
            {
                value = InteractionStoragePaths.ValidateSegment(
                    Path.GetFileName(path),
                    nameof(path)
                );
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool ManifestMatchesPath(
            string manifestPath,
            string batchId,
            string participantId,
            string runId)
        {
            IDictionary<string, object> manifest = InteractionJson.ParseObject(
                InteractionAtomicFile.ReadUtf8(manifestPath)
            );
            return InteractionJson.RequireInt32(manifest, "schema_version") == 1 &&
                string.Equals(
                    InteractionJson.RequireString(manifest, "batch_id"),
                    batchId,
                    StringComparison.Ordinal
                ) &&
                string.Equals(
                    InteractionJson.RequireString(manifest, "participant_id"),
                    participantId,
                    StringComparison.Ordinal
                ) &&
                string.Equals(
                    InteractionJson.RequireString(manifest, "run_id"),
                    runId,
                    StringComparison.Ordinal
                );
        }

        private static void ValidateTerminalSummary(
            string summaryPath,
            string runId)
        {
            try
            {
                IDictionary<string, object> summary = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(summaryPath)
                );
                string status = InteractionJson.RequireString(summary, "status");
                if (InteractionJson.RequireInt32(summary, "schema_version") != 1 ||
                    !string.Equals(
                        InteractionJson.RequireString(summary, "run_id"),
                        runId,
                        StringComparison.Ordinal
                    ) ||
                    (!string.Equals(status, "completed", StringComparison.Ordinal) &&
                        !string.Equals(status, "aborted", StringComparison.Ordinal)))
                {
                    throw new FormatException(
                        "Quest-local terminal summary identity or status is invalid."
                    );
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is FormatException ||
                exception is UnauthorizedAccessException)
            {
                throw new IOException(
                    "Quest-local terminal summary is damaged at " +
                    summaryPath + ".",
                    exception
                );
            }
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
    }
}
