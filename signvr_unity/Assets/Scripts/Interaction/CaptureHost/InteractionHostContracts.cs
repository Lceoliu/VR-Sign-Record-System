using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SignVR.Interaction.CaptureHost
{
    /// <summary>
    /// Pure Contract V1 request helpers shared by the Unity transport adapter
    /// and deterministic tests. Keeping these rules outside UnityWebRequest
    /// makes the exact bytes independently verifiable.
    /// </summary>
    public static class InteractionHostContractV1
    {
        public const string QuestHeartbeatPath =
            "/api/interaction/readiness/quest";

        public static string NormalizeHttpBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Interaction Host URL is required.",
                    nameof(value)
                );
            }
            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) ||
                !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                (!string.IsNullOrEmpty(uri.AbsolutePath) &&
                 uri.AbsolutePath != "/") ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException(
                    "Interaction Host must be an absolute unauthenticated LAN HTTP URL.",
                    nameof(value)
                );
            }
            return value.Trim().TrimEnd('/');
        }

        public static byte[] BuildTerminalBodyUtf8(
            string runId,
            string utcProperty,
            DateTimeOffset utcTime,
            string abortReason)
        {
            string safeRunId = InteractionStoragePaths.ValidateSegment(
                runId,
                nameof(runId)
            );
            if (utcProperty != "completed_utc" && utcProperty != "aborted_utc")
            {
                throw new ArgumentException(
                    "Terminal UTC property is invalid.",
                    nameof(utcProperty)
                );
            }
            if (utcProperty == "aborted_utc" &&
                string.IsNullOrWhiteSpace(abortReason))
            {
                throw new ArgumentException(
                    "Abort reason is required.",
                    nameof(abortReason)
                );
            }
            if (utcProperty == "completed_utc" && abortReason != null)
            {
                throw new ArgumentException(
                    "Complete requests cannot contain an abort reason.",
                    nameof(abortReason)
                );
            }
            var builder = new StringBuilder(256);
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(
                builder,
                "schema_version"
            );
            builder.Append('1');
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "run_id"
            );
            InteractionJson.AppendQuoted(builder, safeRunId);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                utcProperty
            );
            InteractionJson.AppendQuoted(
                builder,
                InteractionRunManifestContractV1.FormatUtc(utcTime)
            );
            if (abortReason != null)
            {
                InteractionRunManifestContractV1.AppendSeparatorAndName(
                    builder,
                    "abort_reason"
                );
                InteractionJson.AppendQuoted(builder, abortReason.Trim());
            }
            builder.Append('}');
            return new UTF8Encoding(false).GetBytes(builder.ToString());
        }

        public static byte[] BuildQuestHeartbeatBodyUtf8(
            string questDeviceId,
            bool ready,
            long heartbeatGeneration,
            long heartbeatSequence)
        {
            string safeQuest = InteractionStoragePaths.ValidateSegment(
                questDeviceId,
                nameof(questDeviceId)
            );
            if (heartbeatGeneration < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heartbeatGeneration)
                );
            }
            if (heartbeatSequence < 1L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heartbeatSequence)
                );
            }
            var builder = new StringBuilder(256);
            builder.Append('{');
            InteractionRunManifestContractV1.AppendName(
                builder,
                "schema_version"
            );
            builder.Append('1');
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "quest_device_id"
            );
            InteractionJson.AppendQuoted(builder, safeQuest);
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "ready"
            );
            builder.Append(ready ? "true" : "false");
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "heartbeat_generation"
            );
            builder.Append(
                heartbeatGeneration.ToString(CultureInfo.InvariantCulture)
            );
            InteractionRunManifestContractV1.AppendSeparatorAndName(
                builder,
                "heartbeat_sequence"
            );
            builder.Append(
                heartbeatSequence.ToString(CultureInfo.InvariantCulture)
            );
            builder.Append('}');
            return new UTF8Encoding(false).GetBytes(builder.ToString());
        }
    }

    public static class InteractionArtifactTypes
    {
        public const string Events = "events";
        public const string Poses = "poses";
        public const string Objects = "objects";
        public const string Summary = "summary";
        public const string Webcam = "webcam";

        private static readonly string[] questUploadTypes =
        {
            Events,
            Poses,
            Objects,
            Summary
        };

        private static readonly string[] requiredAcknowledgementTypes =
        {
            Events,
            Poses,
            Objects,
            Summary,
            Webcam
        };

        public static IReadOnlyList<string> QuestUploadTypes => questUploadTypes;
        public static IReadOnlyList<string> RequiredAcknowledgementTypes =>
            requiredAcknowledgementTypes;

        public static string Validate(string value, bool allowWebcam)
        {
            if (questUploadTypes.Contains(value, StringComparer.Ordinal) ||
                (allowWebcam && string.Equals(
                    value,
                    Webcam,
                    StringComparison.Ordinal)))
            {
                return value;
            }
            throw new ArgumentException(
                "Unknown Interaction artifact type.",
                nameof(value)
            );
        }

        public static string FileNameFor(string artifactType)
        {
            switch (Validate(artifactType, true))
            {
                case Events:
                    return InteractionStoragePaths.EventsFileName;
                case Poses:
                    return InteractionStoragePaths.PosesFileName;
                case Objects:
                    return InteractionStoragePaths.ObjectsFileName;
                case Summary:
                    return InteractionStoragePaths.SummaryFileName;
                case Webcam:
                    return "webcam.webm";
                default:
                    throw new ArgumentOutOfRangeException(nameof(artifactType));
            }
        }

        public static string ContentTypeFor(string artifactType)
        {
            switch (Validate(artifactType, false))
            {
                case Events:
                case Poses:
                case Objects:
                    return "application/x-ndjson";
                case Summary:
                    return "application/json";
                default:
                    throw new ArgumentOutOfRangeException(nameof(artifactType));
            }
        }
    }

    public sealed class InteractionHostReadiness
    {
        private InteractionHostReadiness(
            bool? overallReady,
            bool backendReady,
            bool storageReady,
            bool pairedQuestReady,
            bool questHeartbeatFresh,
            bool browserCameraReady,
            bool browserCameraFresh,
            bool participantReady,
            bool participantFresh,
            string participantId,
            string questDeviceId,
            DateTimeOffset? observedUtc,
            DateTimeOffset? questLastSeenUtc,
            DateTimeOffset? participantLastSeenUtc,
            DateTimeOffset? cameraLastSeenUtc)
        {
            OverallReady = overallReady;
            BackendReady = backendReady;
            StorageReady = storageReady;
            PairedQuestReady = pairedQuestReady;
            QuestHeartbeatFresh = questHeartbeatFresh;
            BrowserCameraReady = browserCameraReady;
            BrowserCameraFresh = browserCameraFresh;
            ParticipantReady = participantReady;
            ParticipantFresh = participantFresh;
            ParticipantId = participantId;
            QuestDeviceId = questDeviceId;
            ObservedUtc = observedUtc;
            QuestLastSeenUtc = questLastSeenUtc;
            ParticipantLastSeenUtc = participantLastSeenUtc;
            CameraLastSeenUtc = cameraLastSeenUtc;
        }

        public bool? OverallReady { get; }
        public bool BackendReady { get; }
        public bool StorageReady { get; }
        public bool PairedQuestReady { get; }
        public bool QuestHeartbeatFresh { get; }
        public bool BrowserCameraReady { get; }
        public bool BrowserCameraFresh { get; }
        public bool ParticipantReady { get; }
        public bool ParticipantFresh { get; }
        public string ParticipantId { get; }
        public string QuestDeviceId { get; }
        public DateTimeOffset? ObservedUtc { get; }
        public DateTimeOffset? QuestLastSeenUtc { get; }
        public DateTimeOffset? ParticipantLastSeenUtc { get; }
        public DateTimeOffset? CameraLastSeenUtc { get; }

        public bool IsStudyReady(
            string expectedQuestDeviceId,
            string expectedParticipantId)
        {
            if (OverallReady.HasValue && !OverallReady.Value)
            {
                return false;
            }
            if (!BackendReady || !StorageReady || !PairedQuestReady ||
                !QuestHeartbeatFresh || !BrowserCameraReady ||
                !BrowserCameraFresh || !ParticipantReady ||
                !ParticipantFresh)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(expectedParticipantId) ||
                string.IsNullOrWhiteSpace(ParticipantId) ||
                !string.Equals(
                    expectedParticipantId.Trim(),
                    ParticipantId,
                    StringComparison.Ordinal))
            {
                return false;
            }
            return !string.IsNullOrWhiteSpace(expectedQuestDeviceId) &&
                !string.IsNullOrWhiteSpace(QuestDeviceId) &&
                string.Equals(
                    expectedQuestDeviceId.Trim(),
                    QuestDeviceId,
                    StringComparison.Ordinal
                );
        }

        public static InteractionHostReadiness Parse(string json)
        {
            IDictionary<string, object> root = InteractionJson.ParseObject(json);
            bool? overall = ReadBoolean(root, "ready");
            bool backend = RequireAnyBoolean(
                root,
                new[] { "backend_ready" },
                new[] { "backend", "ready" }
            );
            bool storage = RequireAnyBoolean(
                root,
                new[] { "storage_ready" },
                new[] { "storage", "ready" }
            );
            bool quest = RequireAnyBoolean(
                root,
                new[] { "paired_quest_ready" },
                new[] { "quest_paired" },
                new[] { "quest_ready" },
                new[] { "quest", "paired" },
                new[] { "quest", "ready" },
                new[] { "paired_quest", "ready" }
            );
            bool questFresh = FirstBoolean(
                root,
                new[] { "quest_heartbeat_fresh" },
                new[] { "quest_fresh" },
                new[] { "quest", "heartbeat_fresh" },
                new[] { "quest", "fresh" }
            ) ?? false;
            bool cameraReady = RequireAnyBoolean(
                root,
                new[] { "browser_camera_ready" },
                new[] { "camera_ready" },
                new[] { "browser_camera", "ready" },
                new[] { "camera", "ready" }
            );
            bool? explicitCameraFresh = FirstBoolean(
                root,
                new[] { "browser_camera_fresh" },
                new[] { "camera_fresh" },
                new[] { "browser_camera", "fresh" },
                new[] { "camera", "fresh" }
            );
            // Study requires the W8a freshness watermark explicitly. A legacy
            // camera_ready-only response remains parseable for diagnostics but
            // cannot open the formal gate.
            bool cameraFresh = explicitCameraFresh ?? false;

            bool participantFresh = FirstBoolean(
                root,
                new[] { "participant_fresh" },
                new[] { "participant_heartbeat_fresh" },
                new[] { "study", "participant_fresh" }
            ) ?? false;
            bool participantReady = FirstBoolean(
                root,
                new[] { "participant_ready" },
                new[] { "study", "participant_ready" }
            ) ?? false;

            string participant = FirstString(
                root,
                new[] { "participant_id" },
                new[] { "study", "participant_id" },
                new[] { "active_run", "participant_id" }
            );
            string questId = FirstString(
                root,
                new[] { "quest_device_id" },
                new[] { "quest", "device_id" },
                new[] { "paired_quest", "device_id" }
            );
            DateTimeOffset? observed = ParseOptionalUtc(
                FirstString(
                    root,
                    new[] { "observed_utc" },
                    new[] { "readiness_utc" },
                    new[] { "server_utc" }
                )
            );
            DateTimeOffset? questLastSeen = ParseOptionalUtc(FirstString(
                root,
                new[] { "quest_last_seen_utc" },
                new[] { "quest_heartbeat_utc" },
                new[] { "quest", "last_seen_utc" }
            ));
            DateTimeOffset? participantLastSeen = ParseOptionalUtc(FirstString(
                root,
                new[] { "participant_last_seen_utc" },
                new[] { "participant_heartbeat_utc" },
                new[] { "study", "participant_last_seen_utc" }
            ));
            DateTimeOffset? cameraLastSeen = ParseOptionalUtc(FirstString(
                root,
                new[] { "camera_last_seen_utc" },
                new[] { "browser_camera_last_seen_utc" },
                new[] { "camera", "last_seen_utc" }
            ));
            participant = ValidateOptionalId(participant, "participant_id");
            questId = ValidateOptionalId(questId, "quest_device_id");
            return new InteractionHostReadiness(
                overall,
                backend,
                storage,
                quest,
                questFresh,
                cameraReady,
                cameraFresh,
                participantReady,
                participantFresh,
                participant,
                questId,
                observed,
                questLastSeen,
                participantLastSeen,
                cameraLastSeen
            );
        }

        private static string ValidateOptionalId(string value, string label)
        {
            if (value == null)
            {
                return null;
            }
            try
            {
                return InteractionStoragePaths.ValidateSegment(value, label);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException(
                    "Host readiness " + label + " is unsafe.",
                    exception
                );
            }
        }

        private static bool RequireAnyBoolean(
            IDictionary<string, object> root,
            params string[][] paths)
        {
            bool? value = FirstBoolean(root, paths);
            if (value.HasValue)
            {
                return value.Value;
            }
            throw new FormatException(
                "Host readiness omitted a required component flag."
            );
        }

        private static bool? FirstBoolean(
            IDictionary<string, object> root,
            params string[][] paths)
        {
            for (int index = 0; index < paths.Length; index++)
            {
                bool? value = ReadBooleanPath(root, paths[index]);
                if (value.HasValue)
                {
                    return value;
                }
            }
            return null;
        }

        private static bool? ReadBoolean(
            IDictionary<string, object> value,
            string propertyName)
        {
            return InteractionJson.OptionalBoolean(value, propertyName);
        }

        private static bool? ReadBooleanPath(
            IDictionary<string, object> root,
            string[] path)
        {
            if (path.Length == 1)
            {
                return ReadBoolean(root, path[0]);
            }
            object nestedRaw;
            if (!root.TryGetValue(path[0], out nestedRaw) || nestedRaw == null)
            {
                return null;
            }
            var nested = nestedRaw as IDictionary<string, object>;
            if (nested == null)
            {
                throw new FormatException(
                    "Host readiness property '" + path[0] + "' must be an object."
                );
            }
            return ReadBoolean(nested, path[1]);
        }

        private static string FirstString(
            IDictionary<string, object> root,
            params string[][] paths)
        {
            for (int index = 0; index < paths.Length; index++)
            {
                string[] path = paths[index];
                if (path.Length == 1)
                {
                    string direct = InteractionJson.OptionalString(root, path[0]);
                    if (direct != null)
                    {
                        return direct;
                    }
                    continue;
                }
                object nestedRaw;
                if (!root.TryGetValue(path[0], out nestedRaw) || nestedRaw == null)
                {
                    continue;
                }
                var nested = nestedRaw as IDictionary<string, object>;
                if (nested == null)
                {
                    throw new FormatException(
                        "Host readiness property '" + path[0] + "' must be an object."
                    );
                }
                string nestedValue = InteractionJson.OptionalString(nested, path[1]);
                if (nestedValue != null)
                {
                    return nestedValue;
                }
            }
            return null;
        }

        private static DateTimeOffset? ParseOptionalUtc(string value)
        {
            if (value == null)
            {
                return null;
            }
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                throw new FormatException("Host readiness UTC is invalid.");
            }
            return parsed.ToUniversalTime();
        }
    }

    public sealed class InteractionHostRegistration
    {
        private InteractionHostRegistration(
            bool accepted,
            string runId,
            DateTimeOffset startAtUtc,
            string state,
            IReadOnlyList<string> missingArtifacts)
        {
            Accepted = accepted;
            RunId = runId;
            StartAtUtc = startAtUtc;
            State = state;
            MissingArtifacts = missingArtifacts;
        }

        public bool Accepted { get; }
        public string RunId { get; }
        public DateTimeOffset StartAtUtc { get; }
        public string State { get; }
        public IReadOnlyList<string> MissingArtifacts { get; }

        public static InteractionHostRegistration Parse(
            string json,
            string expectedRunId)
        {
            IDictionary<string, object> root = InteractionJson.ParseObject(json);
            bool? accepted = InteractionJson.OptionalBoolean(root, "accepted");
            if (!accepted.HasValue || !accepted.Value)
            {
                throw new FormatException("Host did not accept the Run.");
            }
            string runId = InteractionJson.RequireString(root, "run_id");
            if (!string.Equals(runId, expectedRunId, StringComparison.Ordinal))
            {
                throw new FormatException("Host registration Run ID mismatched.");
            }
            DateTimeOffset startAt;
            if (!DateTimeOffset.TryParse(
                    InteractionJson.RequireString(root, "start_at_utc"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                    out startAt))
            {
                throw new FormatException("Host start_at_utc is invalid.");
            }
            string state = InteractionJson.RequireString(root, "state");
            if (!string.Equals(state, "Scheduled", StringComparison.Ordinal))
            {
                throw new FormatException(
                    "Accepted Host registration must be Scheduled."
                );
            }
            if (!root.ContainsKey("missing_artifacts"))
            {
                throw new FormatException(
                    "Host registration omitted missing_artifacts."
                );
            }
            IReadOnlyList<string> rawMissing =
                InteractionJson.OptionalStringArray(root, "missing_artifacts");
            var missing = new List<string>(rawMissing.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < rawMissing.Count; index++)
            {
                string artifact;
                try
                {
                    artifact = InteractionArtifactTypes.Validate(
                        rawMissing[index],
                        true
                    );
                }
                catch (ArgumentException exception)
                {
                    throw new FormatException(
                        "Host registration returned an unknown missing artifact.",
                        exception
                    );
                }
                if (!unique.Add(artifact))
                {
                    throw new FormatException(
                        "Host registration returned duplicate missing artifacts."
                    );
                }
                missing.Add(artifact);
            }
            return new InteractionHostRegistration(
                true,
                runId,
                startAt.ToUniversalTime(),
                state,
                missing.AsReadOnly()
            );
        }
    }

    /// <summary>
    /// Exact W3 GET /api/interaction/runs/{run_id} response used only to
    /// disambiguate a registration 409 after a possibly lost 2xx response.
    /// </summary>
    public sealed class InteractionHostRunSnapshot
    {
        private InteractionHostRunSnapshot(
            string batchId,
            string participantId,
            string runId,
            string state,
            DateTimeOffset startAtUtc,
            IReadOnlyList<string> missingArtifacts,
            bool acknowledged,
            DateTimeOffset? terminalUtc,
            string abortReason)
        {
            BatchId = batchId;
            ParticipantId = participantId;
            RunId = runId;
            State = state;
            StartAtUtc = startAtUtc;
            MissingArtifacts = missingArtifacts;
            Acknowledged = acknowledged;
            TerminalUtc = terminalUtc;
            AbortReason = abortReason;
        }

        public string BatchId { get; }
        public string ParticipantId { get; }
        public string RunId { get; }
        public string State { get; }
        public DateTimeOffset StartAtUtc { get; }
        public IReadOnlyList<string> MissingArtifacts { get; }
        public bool Acknowledged { get; }
        public DateTimeOffset? TerminalUtc { get; }
        public string AbortReason { get; }
        public bool IsActive => State == "Scheduled" || State == "Running";

        public static InteractionHostRunSnapshot Parse(
            string json,
            string expectedRunId)
        {
            IDictionary<string, object> root = InteractionJson.ParseObject(json);
            if (InteractionJson.RequireInt32(root, "schema_version") != 1)
            {
                throw new FormatException(
                    "Host Run snapshot schema_version must be 1."
                );
            }
            string batchId = RequireSafeId(root, "batch_id");
            string participantId = RequireSafeId(root, "participant_id");
            string runId = RequireSafeId(root, "run_id");
            string safeExpected = InteractionStoragePaths.ValidateSegment(
                expectedRunId,
                nameof(expectedRunId)
            );
            if (!string.Equals(runId, safeExpected, StringComparison.Ordinal))
            {
                throw new FormatException("Host Run snapshot ID mismatched.");
            }
            string state = InteractionJson.RequireString(root, "state");
            if (state != "Scheduled" && state != "Running" &&
                state != "Completed" && state != "Aborted")
            {
                throw new FormatException("Host Run snapshot state is invalid.");
            }
            DateTimeOffset startAtUtc = RequireUtc(root, "start_at_utc");
            if (!root.ContainsKey("missing_artifacts"))
            {
                throw new FormatException(
                    "Host Run snapshot omitted missing_artifacts."
                );
            }
            IReadOnlyList<string> rawMissing =
                InteractionJson.OptionalStringArray(root, "missing_artifacts");
            var missing = new List<string>(rawMissing.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < rawMissing.Count; index++)
            {
                string artifact;
                try
                {
                    artifact = InteractionArtifactTypes.Validate(
                        rawMissing[index],
                        true
                    );
                }
                catch (ArgumentException exception)
                {
                    throw new FormatException(
                        "Host Run snapshot returned an unknown artifact.",
                        exception
                    );
                }
                if (!unique.Add(artifact))
                {
                    throw new FormatException(
                        "Host Run snapshot returned duplicate artifacts."
                    );
                }
                missing.Add(artifact);
            }
            bool? acknowledged = InteractionJson.OptionalBoolean(
                root,
                "acknowledged"
            );
            if (!acknowledged.HasValue)
            {
                throw new FormatException(
                    "Host Run snapshot omitted acknowledged."
                );
            }
            DateTimeOffset? terminalUtc = OptionalUtc(root, "terminal_utc");
            string abortReason = InteractionJson.OptionalString(
                root,
                "abort_reason"
            );
            bool terminal = state == "Completed" || state == "Aborted";
            bool expectedAck = terminal && missing.Count == 0;
            if (acknowledged.Value != expectedAck ||
                (terminal && !terminalUtc.HasValue) ||
                (!terminal && terminalUtc.HasValue) ||
                (state == "Aborted" && string.IsNullOrWhiteSpace(abortReason)) ||
                (state != "Aborted" && abortReason != null))
            {
                throw new FormatException(
                    "Host Run snapshot terminal/ACK fields contradict its state."
                );
            }
            return new InteractionHostRunSnapshot(
                batchId,
                participantId,
                runId,
                state,
                startAtUtc,
                missing.AsReadOnly(),
                acknowledged.Value,
                terminalUtc,
                abortReason
            );
        }

        private static string RequireSafeId(
            IDictionary<string, object> root,
            string propertyName)
        {
            string value = InteractionJson.RequireString(root, propertyName);
            try
            {
                return InteractionStoragePaths.ValidateSegment(
                    value,
                    propertyName
                );
            }
            catch (ArgumentException exception)
            {
                throw new FormatException(
                    "Host Run snapshot " + propertyName + " is unsafe.",
                    exception
                );
            }
        }

        private static DateTimeOffset RequireUtc(
            IDictionary<string, object> root,
            string propertyName)
        {
            DateTimeOffset value;
            if (!DateTimeOffset.TryParse(
                    InteractionJson.RequireString(root, propertyName),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                    out value))
            {
                throw new FormatException(
                    "Host Run snapshot " + propertyName + " is invalid."
                );
            }
            return value.ToUniversalTime();
        }

        private static DateTimeOffset? OptionalUtc(
            IDictionary<string, object> root,
            string propertyName)
        {
            string raw = InteractionJson.OptionalString(root, propertyName);
            if (raw == null)
            {
                return null;
            }
            DateTimeOffset value;
            if (!DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                    out value))
            {
                throw new FormatException(
                    "Host Run snapshot " + propertyName + " is invalid."
                );
            }
            return value.ToUniversalTime();
        }
    }

    public sealed class InteractionHostArtifactAck
    {
        private InteractionHostArtifactAck(
            string runId,
            string state,
            bool acknowledged,
            IReadOnlyList<string> storedArtifacts,
            IReadOnlyList<string> missingArtifacts)
        {
            RunId = runId;
            State = state;
            Acknowledged = acknowledged;
            StoredArtifacts = storedArtifacts;
            MissingArtifacts = missingArtifacts;
        }

        public string RunId { get; }
        public string State { get; }
        public bool Acknowledged { get; }
        public bool IsTerminal => State == "Completed" || State == "Aborted";
        public IReadOnlyList<string> StoredArtifacts { get; }
        public IReadOnlyList<string> MissingArtifacts { get; }
        public bool ConfirmsAllRequiredArtifacts =>
            Acknowledged && IsTerminal && MissingArtifacts.Count == 0 &&
            InteractionArtifactTypes.RequiredAcknowledgementTypes.All(
                type => StoredArtifacts.Contains(type, StringComparer.Ordinal)
            );

        public static InteractionHostArtifactAck Parse(
            string json,
            string expectedRunId)
        {
            IDictionary<string, object> root = InteractionJson.ParseObject(json);
            string runId = InteractionJson.RequireString(root, "run_id");
            if (!string.Equals(runId, expectedRunId, StringComparison.Ordinal))
            {
                throw new FormatException("Host ACK Run ID mismatched.");
            }

            string state = InteractionJson.RequireString(root, "state");
            if (state != "Scheduled" && state != "Running" &&
                state != "Completed" && state != "Aborted")
            {
                throw new FormatException("Host ACK state is invalid.");
            }
            bool? acknowledged = InteractionJson.OptionalBoolean(
                root,
                "acknowledged"
            );
            if (!acknowledged.HasValue)
            {
                throw new FormatException("Host ACK omitted acknowledged.");
            }
            if (!root.ContainsKey("missing_artifacts"))
            {
                throw new FormatException(
                    "Host ACK omitted missing_artifacts."
                );
            }
            IReadOnlyList<string> missing = InteractionJson.OptionalStringArray(
                root,
                "missing_artifacts"
            );
            var normalizedMissing = NormalizeArtifactList(missing);
            var missingSet = new HashSet<string>(
                normalizedMissing,
                StringComparer.Ordinal
            );
            IReadOnlyList<string> normalizedStored =
                InteractionArtifactTypes.RequiredAcknowledgementTypes
                    .Where(type => !missingSet.Contains(type))
                    .ToList()
                    .AsReadOnly();
            bool terminal = state == "Completed" || state == "Aborted";
            bool expectedAcknowledged = terminal && normalizedMissing.Count == 0;
            if (acknowledged.Value != expectedAcknowledged)
            {
                throw new FormatException(
                    "Host ACK acknowledgement contradicts state/missing_artifacts."
                );
            }
            return new InteractionHostArtifactAck(
                runId,
                state,
                acknowledged.Value,
                normalizedStored,
                normalizedMissing
            );
        }

        private static IReadOnlyList<string> NormalizeArtifactList(
            IReadOnlyList<string> values)
        {
            var result = new List<string>(values.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Count; index++)
            {
                string value = InteractionArtifactTypes.Validate(
                    values[index],
                    true
                );
                if (unique.Add(value))
                {
                    result.Add(value);
                }
            }
            return result.AsReadOnly();
        }
    }

    public sealed class InteractionScheduledStartGate
    {
        public InteractionScheduledStartGate(
            DateTimeOffset startAtUtc,
            DateTimeOffset observedUtc,
            double observedMonotonicSeconds)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                observedMonotonicSeconds,
                nameof(observedMonotonicSeconds)
            );
            StartAtUtc = startAtUtc.ToUniversalTime();
            double delay = Math.Max(
                0d,
                (StartAtUtc - observedUtc.ToUniversalTime()).TotalSeconds
            );
            StartAtMonotonicSeconds = observedMonotonicSeconds + delay;
        }

        public DateTimeOffset StartAtUtc { get; }
        public double StartAtMonotonicSeconds { get; }

        public bool IsDue(double monotonicTimeSeconds)
        {
            InteractionEventSequencer.ValidateFiniteNonNegative(
                monotonicTimeSeconds,
                nameof(monotonicTimeSeconds)
            );
            return monotonicTimeSeconds >= StartAtMonotonicSeconds;
        }
    }
}
