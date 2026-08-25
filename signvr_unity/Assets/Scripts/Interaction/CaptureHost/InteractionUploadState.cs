using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
            string path,
            long byteCount,
            string sha256)
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
            ByteCount = byteCount;
            if (info.Length != ByteCount)
            {
                throw new IOException(
                    "Interaction artifact changed while it was being frozen."
                );
            }
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
            if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
            {
                throw new ArgumentException(
                    "Frozen Interaction artifact SHA-256 is invalid.",
                    nameof(sha256)
                );
            }
            Sha256 = sha256;
        }

        public string ArtifactType { get; }
        public string Path { get; }
        public long ByteCount { get; }
        public string Sha256 { get; }

        public InteractionBackgroundOperation<bool> BeginVerifyUnchanged()
        {
            return InteractionBackgroundOperation<bool>.Start(() =>
                VerifyUnchangedOnWorker(
                    InteractionArtifactCancellation.None,
                    InteractionArtifactReadObserver.None
                )
            );
        }

        internal bool VerifyUnchangedOnWorker(
            InteractionArtifactCancellation cancellation,
            IInteractionArtifactReadObserver observer)
        {
            cancellation = cancellation ??
                throw new ArgumentNullException(nameof(cancellation));
            observer = observer ??
                throw new ArgumentNullException(nameof(observer));
            cancellation.ThrowIfCancellationRequested();
            var info = new FileInfo(Path);
            if (!info.Exists || info.Length != ByteCount ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(
                    InteractionArtifactHasher.ComputeSha256(
                        Path,
                        cancellation,
                        observer
                    ),
                    Sha256,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "Frozen Interaction artifact changed after sealing: " +
                    ArtifactType + "."
                );
            }
            return true;
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

        public static InteractionBackgroundOperation<InteractionFrozenArtifactSet>
            BeginReadOnce(string runDirectory)
        {
            if (string.IsNullOrWhiteSpace(runDirectory))
            {
                throw new ArgumentException(
                    "Run directory is required.",
                    nameof(runDirectory)
                );
            }
            string frozenDirectory = Path.GetFullPath(runDirectory);
            return InteractionBackgroundOperation<InteractionFrozenArtifactSet>
                .Start(() => ReadOnceOnWorker(
                    frozenDirectory,
                    InteractionArtifactCancellation.None,
                    InteractionArtifactReadObserver.None
                ));
        }

        internal static InteractionFrozenArtifactSet ReadOnceOnWorker(
            string runDirectory,
            InteractionArtifactCancellation cancellation,
            IInteractionArtifactReadObserver observer)
        {
            if (string.IsNullOrWhiteSpace(runDirectory))
            {
                throw new ArgumentException(
                    "Run directory is required.",
                    nameof(runDirectory)
                );
            }
            cancellation = cancellation ??
                throw new ArgumentNullException(nameof(cancellation));
            observer = observer ??
                throw new ArgumentNullException(nameof(observer));
            cancellation.ThrowIfCancellationRequested();
            var result = new Dictionary<string, InteractionFrozenArtifact>(
                StringComparer.Ordinal
            );
            string canonicalDirectory = Path.GetFullPath(runDirectory);
            foreach (string type in InteractionArtifactTypes.QuestUploadTypes)
            {
                cancellation.ThrowIfCancellationRequested();
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
                var info = new FileInfo(path);
                long byteCount = info.Length;
                long byteLimit =
                    InteractionArtifactSizeLimits.MaximumBytesFor(type);
                if (byteCount > byteLimit)
                {
                    throw new IOException(
                        type + " exceeds the Contract V1 upload byte limit."
                    );
                }
                if (byteCount == 0L &&
                    (type == InteractionArtifactTypes.Events ||
                     type == InteractionArtifactTypes.Summary))
                {
                    throw new IOException(type + " cannot be empty.");
                }
                string sha256 = InteractionArtifactHasher.ComputeSha256(
                    path,
                    cancellation,
                    observer
                );
                result.Add(type, new InteractionFrozenArtifact(
                    type,
                    path,
                    byteCount,
                    sha256
                ));
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
        public InteractionBackgroundOperation<bool> PendingPersistence
        {
            get;
            private set;
        }

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
            PendingPersistence = InteractionUploadStateStore.BeginWrite(
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
        private static readonly InteractionSerialBackgroundScheduler Scheduler =
            new InteractionSerialBackgroundScheduler(
                "SignVR Interaction Upload State I/O"
            );

        public static InteractionBackgroundOperation<bool> BeginWrite(
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
            string[] acceptedSnapshot =
                (putResponsesAccepted ?? Array.Empty<string>()).ToArray();
            string[] storedSnapshot =
                (hostStored ?? Array.Empty<string>()).ToArray();
            string[] missingSnapshot =
                (hostMissing ?? Array.Empty<string>()).ToArray();
            return Scheduler.Enqueue(() =>
            {
                Write(
                    runDirectory,
                    runId,
                    status,
                    uploadStartedUtc,
                    acknowledgedUtc,
                    eligibleForCleanup,
                    acceptedSnapshot,
                    storedSnapshot,
                    missingSnapshot,
                    lastError
                );
                return true;
            });
        }

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
