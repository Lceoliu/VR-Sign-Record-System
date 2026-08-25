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
            bool sealedCapture,
            bool acknowledged)
        {
            BatchId = batchId;
            ParticipantId = participantId;
            RunId = runId;
            DirectoryPath = directoryPath;
            IsSealed = sealedCapture;
            IsAcknowledged = acknowledged;
        }

        public string BatchId { get; }
        public string ParticipantId { get; }
        public string RunId { get; }
        public string DirectoryPath { get; }
        public bool IsSealed { get; }
        public bool IsAcknowledged { get; }
        public bool NeedsUpload => IsSealed && !IsAcknowledged;
        public bool NeedsRecovery => !IsSealed && !IsAcknowledged;
        public bool NeedsAttention => !IsAcknowledged;

        public string ManifestPath => Path.Combine(
            DirectoryPath,
            InteractionStoragePaths.ManifestFileName
        );

        public string ArtifactPath(string artifactType)
        {
            string fileName = InteractionArtifactTypes.FileNameFor(
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
                            InteractionStoragePaths.ManifestFileName
                        );
                        if (!File.Exists(manifest) ||
                            !ManifestMatchesPath(
                                manifest,
                                batchId,
                                participantId,
                                runId))
                        {
                            continue;
                        }

                        bool sealedCapture = RequiredQuestArtifacts.All(
                            file => File.Exists(Path.Combine(runDirectory, file))
                        );
                        bool acknowledged = InteractionUploadStateStore
                            .ReadAcknowledgedFlag(runDirectory);
                        results.Add(new InteractionPendingRun(
                            batchId,
                            participantId,
                            runId,
                            runDirectory,
                            sealedCapture,
                            acknowledged
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

        private static readonly string[] RequiredQuestArtifacts =
        {
            InteractionStoragePaths.EventsFileName,
            InteractionStoragePaths.PosesFileName,
            InteractionStoragePaths.ObjectsFileName,
            InteractionStoragePaths.SummaryFileName
        };

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
            try
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
            catch (Exception exception) when (
                exception is IOException || exception is FormatException ||
                exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
    }
}
