using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using SignVR.Interaction.Core;

namespace SignVR.Interaction.CaptureHost
{
    public sealed class InteractionConditionAssignmentDto
    {
        public InteractionConditionAssignmentDto(int blockIndex, int slotIndex)
        {
            BlockIndex = blockIndex;
            SlotIndex = slotIndex;
        }

        public int BlockIndex { get; }
        public int SlotIndex { get; }
    }

    public sealed class InteractionTaskVariantDto
    {
        public InteractionTaskVariantDto(
            string variantId,
            IReadOnlyList<string> targetIds,
            IReadOnlyList<string> orderedTargetIds)
        {
            VariantId = variantId;
            TargetIds = new List<string>(targetIds ??
                throw new ArgumentNullException(nameof(targetIds))).AsReadOnly();
            OrderedTargetIds = new List<string>(orderedTargetIds ??
                throw new ArgumentNullException(nameof(orderedTargetIds))).AsReadOnly();
        }

        public string VariantId { get; }
        public IReadOnlyList<string> TargetIds { get; }
        public IReadOnlyList<string> OrderedTargetIds { get; }
    }

    public sealed class InteractionRunPhaseManifestDto
    {
        public InteractionRunPhaseManifestDto(
            int phaseId,
            string sentenceId,
            string signerId,
            string takeId,
            string artifactPath,
            string artifactSha256,
            InteractionTaskVariantDto taskVariant)
        {
            PhaseId = phaseId;
            SentenceId = sentenceId;
            SignerId = signerId;
            TakeId = takeId;
            ArtifactPath = artifactPath;
            ArtifactSha256 = artifactSha256;
            TaskVariant = taskVariant;
        }

        public int PhaseId { get; }
        public string SentenceId { get; }
        public string SignerId { get; }
        public string TakeId { get; }
        public string ArtifactPath { get; }
        public string ArtifactSha256 { get; }
        public InteractionTaskVariantDto TaskVariant { get; }
    }

    public sealed class InteractionRunManifestDto
    {
        public InteractionRunManifestDto(
            int schemaVersion,
            string batchId,
            string participantId,
            string runId,
            string appSessionId,
            DateTimeOffset createdUtc,
            string appVersion,
            string gitCommit,
            int seed,
            string assistanceCondition,
            InteractionConditionAssignmentDto conditionAssignment,
            IReadOnlyList<int> safePassword,
            IReadOnlyList<string> chestButtonOrder,
            IReadOnlyList<InteractionRunPhaseManifestDto> phases)
        {
            SchemaVersion = schemaVersion;
            BatchId = batchId;
            ParticipantId = participantId;
            RunId = runId;
            AppSessionId = appSessionId;
            CreatedUtc = createdUtc;
            AppVersion = appVersion;
            GitCommit = gitCommit;
            Seed = seed;
            AssistanceCondition = assistanceCondition;
            ConditionAssignment = conditionAssignment;
            SafePassword = new List<int>(safePassword ??
                throw new ArgumentNullException(nameof(safePassword))).AsReadOnly();
            ChestButtonOrder = new List<string>(chestButtonOrder ??
                throw new ArgumentNullException(nameof(chestButtonOrder))).AsReadOnly();
            Phases = new List<InteractionRunPhaseManifestDto>(phases ??
                throw new ArgumentNullException(nameof(phases))).AsReadOnly();
        }

        public int SchemaVersion { get; }
        public string BatchId { get; }
        public string ParticipantId { get; }
        public string RunId { get; }
        public string AppSessionId { get; }
        public DateTimeOffset CreatedUtc { get; }
        public string AppVersion { get; }
        public string GitCommit { get; }
        public int Seed { get; }
        public string AssistanceCondition { get; }
        public InteractionConditionAssignmentDto ConditionAssignment { get; }
        public IReadOnlyList<int> SafePassword { get; }
        public IReadOnlyList<string> ChestButtonOrder { get; }
        public IReadOnlyList<InteractionRunPhaseManifestDto> Phases { get; }
    }

    /// <summary>
    /// Explicit Contract V1 projection. Returned UTF-8 bytes are saved locally;
    /// RunPlan is never passed through an automatic field serializer.
    /// </summary>
    public static class InteractionRunManifestContractV1
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static InteractionRunManifestDto CreateDto(RunPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.SchemaVersion != InteractionContractV1.SchemaVersion ||
                plan.Phases.Count != PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentException(
                    "RunPlan does not satisfy Interaction Contract V1.",
                    nameof(plan)
                );
            }

            string batchId = InteractionStoragePaths.ValidateSegment(
                plan.BatchId,
                nameof(plan.BatchId)
            );
            string participantId = InteractionStoragePaths.ValidateSegment(
                plan.ParticipantId,
                nameof(plan.ParticipantId)
            );
            string runId = InteractionStoragePaths.ValidateSegment(
                plan.RunId,
                nameof(plan.RunId)
            );
            string appSessionId = InteractionStoragePaths.ValidateSegment(
                plan.AppSessionId,
                nameof(plan.AppSessionId)
            );

            ValidatePlanScalars(plan);

            var phases = new List<InteractionRunPhaseManifestDto>(
                PhaseSentenceRanges.PhaseCount
            );
            for (int index = 0; index < plan.Phases.Count; index++)
            {
                RunPhasePlan phase = plan.Phases[index];
                if (phase == null || phase.PhaseId != index + 1)
                {
                    throw new ArgumentException(
                        "RunPlan phases must be ordered 1 through 6.",
                        nameof(plan)
                    );
                }

                InstructionContentReference content = phase.Content;
                TaskVariant variant = phase.TaskVariant;
                string signerId = InteractionStoragePaths.ValidateSegment(
                    content.SignerId,
                    nameof(content.SignerId)
                );
                if (!string.Equals(
                        signerId,
                        InteractionContractV1.PilotSignerId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Contract V1 pilot content must use signer 'wang'.",
                        nameof(plan)
                    );
                }
                string takeId = InteractionStoragePaths.ValidateSegment(
                    content.TakeId,
                    nameof(content.TakeId)
                );
                string artifactPath =
                    InteractionStoragePaths.ValidateRelativeArtifactPath(
                        content.ArtifactPath,
                        nameof(content.ArtifactPath)
                    );
                string expectedPrefix = signerId + "/sentence_" +
                    phase.SentenceId + "/";
                if (!artifactPath.StartsWith(
                        expectedPrefix,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Artifact path must begin with signer_id/sentence_<sentence_id>/.",
                        nameof(plan)
                    );
                }
                var targetIds = CopyRequiredIds(variant.TargetIds, false);
                var orderedTargetIds = CopyRequiredIds(
                    variant.OrderedTargetIds,
                    true
                );
                phases.Add(new InteractionRunPhaseManifestDto(
                    phase.PhaseId,
                    phase.SentenceId,
                    signerId,
                    takeId,
                    artifactPath,
                    content.ArtifactSha256,
                    new InteractionTaskVariantDto(
                        Required(variant.VariantId, nameof(variant.VariantId)),
                        targetIds,
                        orderedTargetIds
                    )
                ));
            }

            return new InteractionRunManifestDto(
                InteractionContractV1.SchemaVersion,
                batchId,
                participantId,
                runId,
                appSessionId,
                plan.CreatedUtc.ToUniversalTime(),
                ValidateBuildIdentity(plan.AppVersion, nameof(plan.AppVersion)),
                ValidateBuildIdentity(plan.GitCommit, nameof(plan.GitCommit)),
                plan.Seed,
                plan.AssistanceCondition.ToString(),
                new InteractionConditionAssignmentDto(
                    plan.ConditionAssignment.BlockIndex,
                    plan.ConditionAssignment.SlotIndex
                ),
                new ReadOnlyCollection<int>(
                    new List<int>(plan.SafePassword.Digits)
                ),
                new ReadOnlyCollection<string>(
                    new List<string>(plan.ChestButtonOrder.ButtonIds)
                ),
                phases.AsReadOnly()
            );
        }

        public static byte[] SerializeUtf8(RunPlan plan)
        {
            return SerializeUtf8(CreateDto(plan));
        }

        public static byte[] SerializeUtf8(InteractionRunManifestDto value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            ValidateDto(value);

            var builder = new StringBuilder(4096);
            builder.Append('{');
            AppendName(builder, "schema_version");
            builder.Append(value.SchemaVersion);
            AppendSeparatorAndName(builder, "batch_id");
            InteractionJson.AppendQuoted(builder, value.BatchId);
            AppendSeparatorAndName(builder, "participant_id");
            InteractionJson.AppendQuoted(builder, value.ParticipantId);
            AppendSeparatorAndName(builder, "run_id");
            InteractionJson.AppendQuoted(builder, value.RunId);
            AppendSeparatorAndName(builder, "app_session_id");
            InteractionJson.AppendQuoted(builder, value.AppSessionId);
            AppendSeparatorAndName(builder, "created_utc");
            InteractionJson.AppendQuoted(builder, FormatUtc(value.CreatedUtc));
            AppendSeparatorAndName(builder, "app_version");
            InteractionJson.AppendQuoted(builder, value.AppVersion);
            AppendSeparatorAndName(builder, "git_commit");
            InteractionJson.AppendQuoted(builder, value.GitCommit);
            AppendSeparatorAndName(builder, "seed");
            builder.Append(value.Seed.ToString(CultureInfo.InvariantCulture));
            AppendSeparatorAndName(builder, "assistance_condition");
            InteractionJson.AppendQuoted(builder, value.AssistanceCondition);
            AppendSeparatorAndName(builder, "condition_assignment");
            builder.Append('{');
            AppendName(builder, "block_index");
            builder.Append(value.ConditionAssignment.BlockIndex);
            AppendSeparatorAndName(builder, "slot_index");
            builder.Append(value.ConditionAssignment.SlotIndex);
            builder.Append('}');
            AppendSeparatorAndName(builder, "safe_password");
            AppendIntArray(builder, value.SafePassword);
            AppendSeparatorAndName(builder, "chest_button_order");
            AppendStringArray(builder, value.ChestButtonOrder);
            AppendSeparatorAndName(builder, "phases");
            builder.Append('[');
            for (int index = 0; index < value.Phases.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendPhase(builder, value.Phases[index]);
            }
            builder.Append(']');
            builder.Append('}');
            builder.Append('\n');
            return Utf8.GetBytes(builder.ToString());
        }

        public static string FormatUtc(DateTimeOffset value)
        {
            return value.ToUniversalTime().UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture
            );
        }

        private static void AppendPhase(
            StringBuilder builder,
            InteractionRunPhaseManifestDto value)
        {
            builder.Append('{');
            AppendName(builder, "phase_id");
            builder.Append(value.PhaseId);
            AppendSeparatorAndName(builder, "sentence_id");
            InteractionJson.AppendQuoted(builder, value.SentenceId);
            AppendSeparatorAndName(builder, "signer_id");
            InteractionJson.AppendQuoted(builder, value.SignerId);
            AppendSeparatorAndName(builder, "take_id");
            InteractionJson.AppendQuoted(builder, value.TakeId);
            AppendSeparatorAndName(builder, "artifact_path");
            InteractionJson.AppendQuoted(builder, value.ArtifactPath);
            AppendSeparatorAndName(builder, "artifact_sha256");
            InteractionJson.AppendQuoted(builder, value.ArtifactSha256);
            AppendSeparatorAndName(builder, "task_variant");
            builder.Append('{');
            AppendName(builder, "variant_id");
            InteractionJson.AppendQuoted(builder, value.TaskVariant.VariantId);
            AppendSeparatorAndName(builder, "target_ids");
            AppendStringArray(builder, value.TaskVariant.TargetIds);
            AppendSeparatorAndName(builder, "ordered_target_ids");
            AppendStringArray(builder, value.TaskVariant.OrderedTargetIds);
            builder.Append('}');
            builder.Append('}');
        }

        private static void AppendIntArray(
            StringBuilder builder,
            IReadOnlyList<int> values)
        {
            builder.Append('[');
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                builder.Append(values[index].ToString(CultureInfo.InvariantCulture));
            }
            builder.Append(']');
        }

        internal static void AppendStringArray(
            StringBuilder builder,
            IReadOnlyList<string> values)
        {
            builder.Append('[');
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                InteractionJson.AppendQuoted(builder, values[index]);
            }
            builder.Append(']');
        }

        internal static void AppendName(StringBuilder builder, string name)
        {
            InteractionJson.AppendQuoted(builder, name);
            builder.Append(':');
        }

        internal static void AppendSeparatorAndName(
            StringBuilder builder,
            string name)
        {
            builder.Append(',');
            AppendName(builder, name);
        }

        private static IReadOnlyList<string> CopyRequiredIds(
            IReadOnlyList<string> values,
            bool allowEmpty)
        {
            if (values == null || (!allowEmpty && values.Count == 0))
            {
                throw new ArgumentException("Task Variant IDs are incomplete.");
            }

            var result = new List<string>(values.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Count; index++)
            {
                string value = Required(values[index], "targetIds");
                if (!unique.Add(value))
                {
                    throw new ArgumentException("Task Variant IDs must be unique.");
                }
                result.Add(value);
            }
            return result.AsReadOnly();
        }

        private static void ValidatePlanScalars(RunPlan plan)
        {
            if (plan.ConditionAssignment.BlockIndex < 0 ||
                plan.ConditionAssignment.SlotIndex < 0 ||
                plan.ConditionAssignment.SlotIndex > 2)
            {
                throw new ArgumentException(
                    "Condition assignment is outside Contract V1.",
                    nameof(plan)
                );
            }
            var digits = new HashSet<int>();
            for (int index = 0; index < plan.SafePassword.Digits.Count; index++)
            {
                int digit = plan.SafePassword.Digits[index];
                if (digit < 0 || digit > 9 || !digits.Add(digit))
                {
                    throw new ArgumentException(
                        "Safe password must contain four unique digits.",
                        nameof(plan)
                    );
                }
            }
            if (plan.SafePassword.Digits.Count != 4 ||
                plan.ChestButtonOrder.ButtonIds.Count != 4 ||
                !new HashSet<string>(
                    plan.ChestButtonOrder.ButtonIds,
                    StringComparer.Ordinal
                ).SetEquals(new[] { "blue", "red", "yellow", "green" }))
            {
                throw new ArgumentException(
                    "RunPlan password or chest-button permutation is invalid.",
                    nameof(plan)
                );
            }
        }

        private static void ValidateDto(InteractionRunManifestDto value)
        {
            if (value.SchemaVersion != 1 || value.Phases.Count != 6 ||
                value.SafePassword.Count != 4 ||
                value.ChestButtonOrder.Count != 4)
            {
                throw new ArgumentException(
                    "Manifest DTO does not satisfy Contract V1.",
                    nameof(value)
                );
            }
            InteractionStoragePaths.ValidateSegment(value.BatchId, "batch_id");
            InteractionStoragePaths.ValidateSegment(
                value.ParticipantId,
                "participant_id"
            );
            InteractionStoragePaths.ValidateSegment(value.RunId, "run_id");
            InteractionStoragePaths.ValidateSegment(
                value.AppSessionId,
                "app_session_id"
            );
            ValidateBuildIdentity(value.AppVersion, "app_version");
            ValidateBuildIdentity(value.GitCommit, "git_commit");
            if (value.ConditionAssignment == null ||
                value.ConditionAssignment.BlockIndex < 0 ||
                value.ConditionAssignment.SlotIndex < 0 ||
                value.ConditionAssignment.SlotIndex > 2 ||
                (value.AssistanceCondition != "TextAndPointing" &&
                 value.AssistanceCondition != "TextOnly" &&
                 value.AssistanceCondition != "SignOnly"))
            {
                throw new ArgumentException(
                    "Condition assignment is outside Contract V1.",
                    nameof(value)
                );
            }
            var digits = new HashSet<int>();
            for (int index = 0; index < value.SafePassword.Count; index++)
            {
                int digit = value.SafePassword[index];
                if (digit < 0 || digit > 9 || !digits.Add(digit))
                {
                    throw new ArgumentException(
                        "Safe password must contain four unique digits.",
                        nameof(value)
                    );
                }
            }
            if (!new HashSet<string>(
                    value.ChestButtonOrder,
                    StringComparer.Ordinal
                ).SetEquals(new[] { "blue", "red", "yellow", "green" }))
            {
                throw new ArgumentException(
                    "Chest buttons must be the four-button permutation.",
                    nameof(value)
                );
            }
            for (int index = 0; index < value.Phases.Count; index++)
            {
                InteractionRunPhaseManifestDto phase = value.Phases[index];
                if (phase == null || phase.PhaseId != index + 1 ||
                    phase.TaskVariant == null ||
                    phase.TaskVariant.TargetIds.Count == 0)
                {
                    throw new ArgumentException(
                        "Manifest DTO phases must be six ordered resolved entries.",
                        nameof(value)
                    );
                }
                if (!PhaseSentenceRanges.ForPhase(phase.PhaseId).Contains(
                        phase.SentenceId) ||
                    !string.Equals(
                        phase.SignerId,
                        InteractionContractV1.PilotSignerId,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Manifest DTO phase content is outside the pilot ranges.",
                        nameof(value)
                    );
                }
                InteractionStoragePaths.ValidateSegment(
                    phase.SignerId,
                    "signer_id"
                );
                InteractionStoragePaths.ValidateSegment(phase.TakeId, "take_id");
                string artifactPath =
                    InteractionStoragePaths.ValidateRelativeArtifactPath(
                        phase.ArtifactPath,
                        "artifact_path"
                    );
                if (!artifactPath.StartsWith(
                        phase.SignerId + "/sentence_" + phase.SentenceId + "/",
                        StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(phase.ArtifactSha256) ||
                    phase.ArtifactSha256.Length != 64)
                {
                    throw new ArgumentException(
                        "Manifest DTO artifact identity is invalid.",
                        nameof(value)
                    );
                }
                for (int hashIndex = 0;
                    hashIndex < phase.ArtifactSha256.Length;
                    hashIndex++)
                {
                    if (!Uri.IsHexDigit(phase.ArtifactSha256[hashIndex]))
                    {
                        throw new ArgumentException(
                            "Manifest DTO artifact SHA-256 is invalid.",
                            nameof(value)
                        );
                    }
                }
                Required(
                    phase.TaskVariant.VariantId,
                    "task_variant.variant_id"
                );
            }
        }

        private static string ValidateBuildIdentity(
            string value,
            string parameterName)
        {
            string normalized = Required(value, parameterName);
            if (normalized.Length > 160 ||
                !string.Equals(value, normalized, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Build identities must contain 1-160 non-padding characters.",
                    parameterName
                );
            }
            return normalized;
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A non-empty value is required.",
                    parameterName
                );
            }
            return value.Trim();
        }
    }

    public static class InteractionInstructionContentManifestReader
    {
        public static InstructionContentCatalog Read(byte[] utf8Bytes)
        {
            if (utf8Bytes == null || utf8Bytes.Length == 0)
            {
                throw new ArgumentException(
                    "Instruction content manifest bytes are required.",
                    nameof(utf8Bytes)
                );
            }

            return Read(new UTF8Encoding(false, true).GetString(utf8Bytes));
        }

        public static InstructionContentCatalog Read(string json)
        {
            IDictionary<string, object> root = InteractionJson.ParseObject(json);
            if (InteractionJson.RequireInt32(root, "schema_version") !=
                InteractionContractV1.SchemaVersion)
            {
                throw new FormatException(
                    "Instruction content manifest schema_version must be 1."
                );
            }

            IList<object> entryValues = InteractionJson.RequireArray(
                root,
                "entries"
            );
            var references = new List<InstructionContentReference>(
                entryValues.Count
            );
            for (int index = 0; index < entryValues.Count; index++)
            {
                var entry = entryValues[index] as IDictionary<string, object>;
                if (entry == null)
                {
                    throw new FormatException(
                        "Instruction manifest entries must be objects."
                    );
                }

                DateTimeOffset completedUtc;
                if (!DateTimeOffset.TryParse(
                        InteractionJson.RequireString(entry, "completed_utc"),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal |
                            DateTimeStyles.AdjustToUniversal,
                        out completedUtc))
                {
                    throw new FormatException(
                        "completed_utc must be a valid UTC timestamp."
                    );
                }

                int phaseId = InteractionJson.RequireInt32(entry, "phase_id");
                string sentenceId = InteractionJson.RequireString(
                    entry,
                    "sentence_id"
                );
                string signerId = InteractionStoragePaths.ValidateSegment(
                    InteractionJson.RequireString(entry, "signer_id"),
                    "signer_id"
                );
                if (!string.Equals(
                        signerId,
                        InteractionContractV1.PilotSignerId,
                        StringComparison.Ordinal))
                {
                    throw new FormatException(
                        "Instruction manifest signer_id must be 'wang'."
                    );
                }
                string takeId = InteractionStoragePaths.ValidateSegment(
                    InteractionJson.RequireString(entry, "take_id"),
                    "take_id"
                );
                string posePath =
                    InteractionStoragePaths.ValidateRelativeArtifactPath(
                        InteractionJson.RequireString(entry, "pose_path"),
                        "pose_path"
                    );
                if (!posePath.StartsWith(
                        signerId + "/sentence_" + sentenceId + "/",
                        StringComparison.Ordinal))
                {
                    throw new FormatException(
                        "Instruction pose_path does not match signer/sentence."
                    );
                }

                references.Add(new InstructionContentReference(
                    phaseId,
                    sentenceId,
                    signerId,
                    takeId,
                    completedUtc,
                    InteractionJson.RequireInt32(entry, "take_index"),
                    posePath,
                    InteractionJson.RequireString(entry, "pose_sha256")
                ));
            }

            return new InstructionContentCatalog(references);
        }
    }
}
