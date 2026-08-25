using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    public enum InteractionUploadStatus
    {
        Idle,
        Uploading,
        AwaitingAck,
        Acknowledged,
        Deferred
    }

    public static class InteractionArtifactSizeLimits
    {
        public static long MaximumBytesFor(string artifactType)
        {
            switch (InteractionArtifactTypes.Validate(artifactType, false))
            {
                case InteractionArtifactTypes.Events:
                    return 512L * 1024L * 1024L;
                case InteractionArtifactTypes.Poses:
                    return 2L * 1024L * 1024L * 1024L;
                case InteractionArtifactTypes.Objects:
                    return 1024L * 1024L * 1024L;
                case InteractionArtifactTypes.Summary:
                    return 16L * 1024L * 1024L;
                default:
                    throw new ArgumentOutOfRangeException(nameof(artifactType));
            }
        }
    }

    public sealed class InteractionFrozenArtifact
    {
        internal InteractionFrozenArtifact(
            string artifactType,
            string path)
        {
            ArtifactType = InteractionArtifactTypes.Validate(
                artifactType,
                false
            );
            Path = System.IO.Path.GetFullPath(
                path ?? throw new ArgumentNullException(nameof(path))
            );
            var info = new FileInfo(Path);
            if (!info.Exists ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Frozen Interaction artifact is missing or is a reparse point."
                );
            }
            ByteCount = info.Length;
            long limit = InteractionArtifactSizeLimits.MaximumBytesFor(
                ArtifactType
            );
            if (ByteCount > limit)
            {
                throw new IOException(
                    ArtifactType + " exceeds the Contract V1 upload limit of " +
                    limit.ToString(CultureInfo.InvariantCulture) + " bytes."
                );
            }
            if (ByteCount == 0L &&
                (ArtifactType == InteractionArtifactTypes.Events ||
                 ArtifactType == InteractionArtifactTypes.Summary))
            {
                throw new IOException(
                    ArtifactType + " cannot be empty."
                );
            }
            Sha256 = ComputeSha256(Path);
        }

        public string ArtifactType { get; }
        public string Path { get; }
        public long ByteCount { get; }
        public string Sha256 { get; }

        public void VerifyUnchanged()
        {
            var info = new FileInfo(Path);
            if (!info.Exists || info.Length != ByteCount ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(
                    ComputeSha256(Path),
                    Sha256,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "Frozen Interaction artifact changed after sealing: " +
                    ArtifactType + "."
                );
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65536,
                FileOptions.SequentialScan))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(stream);
                var builder = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    builder.Append(digest[index].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }

    public sealed class InteractionFrozenArtifactSet
    {
        private readonly Dictionary<string, InteractionFrozenArtifact> artifacts;

        private InteractionFrozenArtifactSet(
            Dictionary<string, InteractionFrozenArtifact> artifacts)
        {
            this.artifacts = artifacts;
        }

        public IReadOnlyList<string> ArtifactTypes =>
            InteractionArtifactTypes.QuestUploadTypes;

        public InteractionFrozenArtifact For(string artifactType)
        {
            string normalized = InteractionArtifactTypes.Validate(
                artifactType,
                false
            );
            return artifacts[normalized];
        }

        public static InteractionFrozenArtifactSet ReadOnce(string runDirectory)
        {
            if (string.IsNullOrWhiteSpace(runDirectory))
            {
                throw new ArgumentException(
                    "Run directory is required.",
                    nameof(runDirectory)
                );
            }
            var result = new Dictionary<string, InteractionFrozenArtifact>(
                StringComparer.Ordinal
            );
            string canonicalDirectory = Path.GetFullPath(runDirectory);
            foreach (string type in InteractionArtifactTypes.QuestUploadTypes)
            {
                string path = System.IO.Path.Combine(
                    runDirectory,
                    InteractionArtifactTypes.FileNameFor(type)
                );
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "Interaction artifact is missing.",
                        path
                    );
                }
                InteractionStoragePaths.EnsureChildPath(
                    canonicalDirectory,
                    path
                );
                result.Add(
                    type,
                    new InteractionFrozenArtifact(
                        type,
                        path
                    )
                );
            }
            return new InteractionFrozenArtifactSet(result);
        }
    }

    public sealed class InteractionUploadStateMachine
    {
        private readonly string runId;
        private readonly string runDirectory;
        private readonly HashSet<string> putResponsesAccepted =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> hostStored =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> hostMissing =
            new HashSet<string>(StringComparer.Ordinal);
        private DateTimeOffset? uploadStartedUtc;
        private DateTimeOffset? acknowledgedUtc;
        private string lastError;

        public InteractionUploadStateMachine(
            string runId,
            string runDirectory)
        {
            this.runId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            this.runDirectory = Path.GetFullPath(
                runDirectory ?? throw new ArgumentNullException(nameof(runDirectory))
            );
            Status = InteractionUploadStatus.Idle;
        }

        public InteractionUploadStatus Status { get; private set; }
        public bool EligibleForLocalCleanup { get; private set; }
        public string LastError => lastError;
        public IReadOnlyCollection<string> PutResponsesAccepted =>
            putResponsesAccepted;
        public IReadOnlyCollection<string> HostStored => hostStored;
        public IReadOnlyCollection<string> HostMissing => hostMissing;

        public void Begin(DateTimeOffset utcTime)
        {
            if (Status != InteractionUploadStatus.Idle &&
                Status != InteractionUploadStatus.Deferred)
            {
                throw new InvalidOperationException(
                    "Upload can begin only from Idle or Deferred."
                );
            }
            uploadStartedUtc = uploadStartedUtc ?? utcTime.ToUniversalTime();
            lastError = null;
            Status = InteractionUploadStatus.Uploading;
            Persist();
        }

        public void RecordPutResponse(string artifactType, long httpStatusCode)
        {
            if (Status != InteractionUploadStatus.Uploading)
            {
                throw new InvalidOperationException("Upload is not active.");
            }
            string normalized = InteractionArtifactTypes.Validate(
                artifactType,
                false
            );
            if (httpStatusCode >= 200L && httpStatusCode <= 299L)
            {
                putResponsesAccepted.Add(normalized);
            }
            // A 409 is deliberately not trusted as durable success. It may be
            // the safe retry after a lost response; GET ACK resolves that case.
            Persist();
        }

        public void AwaitAck()
        {
            if (Status != InteractionUploadStatus.Uploading)
            {
                throw new InvalidOperationException("Upload is not active.");
            }
            Status = InteractionUploadStatus.AwaitingAck;
            Persist();
        }

        public bool ApplyAck(
            InteractionHostArtifactAck ack,
            DateTimeOffset utcTime)
        {
            if (ack == null)
            {
                throw new ArgumentNullException(nameof(ack));
            }
            if (Status != InteractionUploadStatus.AwaitingAck &&
                Status != InteractionUploadStatus.Uploading)
            {
                throw new InvalidOperationException("Upload is not awaiting ACK.");
            }
            if (!string.Equals(ack.RunId, runId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ACK Run ID mismatched.");
            }
            hostStored.Clear();
            hostMissing.Clear();
            foreach (string type in ack.StoredArtifacts)
            {
                hostStored.Add(type);
            }
            foreach (string type in ack.MissingArtifacts)
            {
                hostMissing.Add(type);
            }

            EligibleForLocalCleanup =
                ack.Acknowledged && ack.IsTerminal &&
                InteractionArtifactTypes.RequiredAcknowledgementTypes.All(
                    type => hostStored.Contains(type) &&
                        !hostMissing.Contains(type)
                );
            if (EligibleForLocalCleanup)
            {
                Status = InteractionUploadStatus.Acknowledged;
                acknowledgedUtc = utcTime.ToUniversalTime();
            }
            else
            {
                Status = InteractionUploadStatus.AwaitingAck;
            }
            Persist();
            return EligibleForLocalCleanup;
        }

        public void Defer(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                throw new ArgumentException(
                    "Deferred upload requires an error.",
                    nameof(error)
                );
            }
            lastError = error.Trim();
            Status = InteractionUploadStatus.Deferred;
            EligibleForLocalCleanup = false;
            Persist();
        }

        private void Persist()
        {
            InteractionUploadStateStore.Write(
                runDirectory,
                runId,
                Status,
                uploadStartedUtc,
                acknowledgedUtc,
                EligibleForLocalCleanup,
                putResponsesAccepted,
                hostStored,
                hostMissing,
                lastError
            );
        }
    }

    internal static class InteractionUploadStateStore
    {
        public static void Write(
            string runDirectory,
            string runId,
            InteractionUploadStatus status,
            DateTimeOffset? uploadStartedUtc,
            DateTimeOffset? acknowledgedUtc,
            bool eligibleForCleanup,
            IEnumerable<string> putResponsesAccepted,
            IEnumerable<string> hostStored,
            IEnumerable<string> hostMissing,
            string lastError)
        {
            var builder = new StringBuilder(1024);
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(builder, "schema_version");
            builder.Append('1');
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "run_id");
            InteractionJson.AppendQuoted(builder, runId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "status");
            InteractionJson.AppendQuoted(
                builder,
                status.ToString().ToLowerInvariant()
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "upload_started_utc");
            AppendUtc(builder, uploadStartedUtc);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "acknowledged_utc");
            AppendUtc(builder, acknowledgedUtc);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "eligible_for_cleanup");
            InteractionCaptureJson.AppendBoolean(builder, eligibleForCleanup);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "put_responses_accepted");
            AppendSortedStrings(builder, putResponsesAccepted);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "host_stored_artifacts");
            AppendSortedStrings(builder, hostStored);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "host_missing_artifacts");
            AppendSortedStrings(builder, hostMissing);
            InteractionRunManifestContractV1.AppendSeparatorAndName(builder, "last_error");
            InteractionJson.AppendNullableString(builder, lastError);
            builder.Append('}');
            builder.Append('\n');
            InteractionAtomicFile.WriteTextReplace(
                Path.Combine(
                    runDirectory,
                    InteractionStoragePaths.UploadStateFileName
                ),
                builder.ToString()
            );
        }

        public static bool ReadAcknowledgedFlag(string runDirectory)
        {
            string path = Path.Combine(
                runDirectory,
                InteractionStoragePaths.UploadStateFileName
            );
            InteractionAtomicFile.RecoverDestination(path);
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                IDictionary<string, object> value = InteractionJson.ParseObject(
                    InteractionAtomicFile.ReadUtf8(path)
                );
                if (InteractionJson.RequireInt32(value, "schema_version") != 1 ||
                    !string.Equals(
                        InteractionJson.RequireString(value, "status"),
                        "acknowledged",
                        StringComparison.Ordinal))
                {
                    return false;
                }
                bool? acknowledged = InteractionJson.OptionalBoolean(
                    value,
                    "eligible_for_cleanup"
                );
                if (!acknowledged.HasValue || !acknowledged.Value)
                {
                    return false;
                }
                var stored = new HashSet<string>(
                    InteractionJson.OptionalStringArray(
                        value,
                        "host_stored_artifacts"
                    ),
                    StringComparer.Ordinal
                );
                var missing = new HashSet<string>(
                    InteractionJson.OptionalStringArray(
                        value,
                        "host_missing_artifacts"
                    ),
                    StringComparer.Ordinal
                );
                return InteractionArtifactTypes.RequiredAcknowledgementTypes.All(
                    type => stored.Contains(type) && !missing.Contains(type)
                );
            }
            catch (Exception exception) when (
                exception is IOException || exception is FormatException ||
                exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void AppendUtc(
            StringBuilder builder,
            DateTimeOffset? value)
        {
            if (value.HasValue)
            {
                InteractionJson.AppendQuoted(
                    builder,
                    InteractionRunManifestContractV1.FormatUtc(value.Value)
                );
            }
            else
            {
                builder.Append("null");
            }
        }

        private static void AppendSortedStrings(
            StringBuilder builder,
            IEnumerable<string> values)
        {
            var sorted = values.OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            InteractionRunManifestContractV1.AppendStringArray(
                builder,
                sorted
            );
        }
    }
}
