using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SignVR.Interaction.Core
{
    public static class InteractionContractV1
    {
        public const int SchemaVersion = 1;
        public const string PilotSignerId = "wang";
    }

    public sealed class InstructionContentReference
    {
        public InstructionContentReference(
            int phaseId,
            string sentenceId,
            string signerId,
            string takeId,
            DateTimeOffset completedUtc,
            int takeIndex,
            string artifactPath,
            string artifactSha256)
        {
            string normalizedSentenceId = CoreGuard.Required(
                sentenceId,
                nameof(sentenceId)
            );
            if (PhaseSentenceRanges.GetPhaseId(normalizedSentenceId) != phaseId)
            {
                throw new ArgumentException(
                    "Sentence ID does not belong to the supplied phase.",
                    nameof(sentenceId)
                );
            }

            if (takeIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(takeIndex));
            }

            string normalizedSha256 = CoreGuard.Required(
                artifactSha256,
                nameof(artifactSha256)
            ).ToLowerInvariant();
            if (normalizedSha256.Length != 64 ||
                normalizedSha256.Any(character =>
                    !Uri.IsHexDigit(character)))
            {
                throw new ArgumentException(
                    "Artifact SHA-256 must contain exactly 64 hexadecimal characters.",
                    nameof(artifactSha256)
                );
            }

            PhaseId = phaseId;
            SentenceId = normalizedSentenceId;
            SignerId = CoreGuard.Required(signerId, nameof(signerId));
            TakeId = CoreGuard.Required(takeId, nameof(takeId));
            CompletedUtc = completedUtc.ToUniversalTime();
            TakeIndex = takeIndex;
            ArtifactPath = CoreGuard.Required(
                artifactPath,
                nameof(artifactPath)
            );
            ArtifactSha256 = normalizedSha256;
        }

        public int PhaseId { get; }

        public string SentenceId { get; }

        public string SignerId { get; }

        public string TakeId { get; }

        public DateTimeOffset CompletedUtc { get; }

        public int TakeIndex { get; }

        public string ArtifactPath { get; }

        public string ArtifactSha256 { get; }
    }

    /// <summary>
    /// An immutable, already-resolved Pilot content set. Resolving the latest
    /// completed Take is intentionally outside this module; W2 supplies one
    /// exact reference for every canonical sentence before a Run is generated.
    /// </summary>
    public sealed class InstructionContentCatalog
    {
        private readonly ReadOnlyCollection<InstructionContentReference> entries;
        private readonly ReadOnlyDictionary<string, InstructionContentReference>
            bySentence;

        public InstructionContentCatalog(
            IEnumerable<InstructionContentReference> references)
        {
            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            var copy = references.ToList();
            if (copy.Count != PhaseSentenceRanges.TotalSentenceCount)
            {
                throw new ArgumentException(
                    "Pilot content must resolve all 31 canonical sentences.",
                    nameof(references)
                );
            }

            var dictionary = new Dictionary<string, InstructionContentReference>(
                StringComparer.Ordinal
            );
            for (int index = 0; index < copy.Count; index++)
            {
                InstructionContentReference reference = copy[index];
                if (reference == null)
                {
                    throw new ArgumentException(
                        "Content references cannot contain null entries.",
                        nameof(references)
                    );
                }

                if (!string.Equals(
                    reference.SignerId,
                    InteractionContractV1.PilotSignerId,
                    StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Interaction Contract V1 requires signer 'wang'.",
                        nameof(references)
                    );
                }

                if (dictionary.ContainsKey(reference.SentenceId))
                {
                    throw new ArgumentException(
                        $"Sentence {reference.SentenceId} is duplicated.",
                        nameof(references)
                    );
                }

                dictionary.Add(reference.SentenceId, reference);
            }

            for (int index = 0;
                index < PhaseSentenceRanges.AllSentenceIds.Count;
                index++)
            {
                string expectedId = PhaseSentenceRanges.AllSentenceIds[index];
                if (!dictionary.ContainsKey(expectedId))
                {
                    throw new ArgumentException(
                        $"Sentence {expectedId} is missing.",
                        nameof(references)
                    );
                }
            }

            copy.Sort((left, right) => string.CompareOrdinal(
                left.SentenceId,
                right.SentenceId
            ));
            entries = copy.AsReadOnly();
            bySentence = new ReadOnlyDictionary<string, InstructionContentReference>(
                dictionary
            );
        }

        public IReadOnlyList<InstructionContentReference> Entries => entries;

        public InstructionContentReference ForSentence(string sentenceId)
        {
            string normalized = CoreGuard.Required(
                sentenceId,
                nameof(sentenceId)
            );
            if (!bySentence.TryGetValue(
                normalized,
                out InstructionContentReference reference))
            {
                throw new KeyNotFoundException(
                    $"No resolved content exists for sentence {normalized}."
                );
            }

            return reference;
        }
    }

    public sealed class SafePassword
    {
        public const int DigitCount = 4;

        private readonly ReadOnlyCollection<int> digits;

        public SafePassword(IEnumerable<int> digits)
        {
            if (digits == null)
            {
                throw new ArgumentNullException(nameof(digits));
            }

            var copy = digits.ToList();
            if (copy.Count != DigitCount)
            {
                throw new ArgumentException(
                    "A safe password must contain exactly four digits.",
                    nameof(digits)
                );
            }

            if (copy.Any(digit => digit < 0 || digit > 9))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(digits),
                    "Safe password digits must be between 0 and 9."
                );
            }

            if (copy.Distinct().Count() != DigitCount)
            {
                throw new ArgumentException(
                    "Safe password digits must be unique.",
                    nameof(digits)
                );
            }

            this.digits = copy.AsReadOnly();
        }

        public IReadOnlyList<int> Digits => digits;
    }

    public sealed class ChestButtonOrder
    {
        private static readonly ReadOnlyCollection<string> requiredButtonIds =
            new List<string> { "blue", "red", "yellow", "green" }
                .AsReadOnly();

        private readonly ReadOnlyCollection<string> buttonIds;

        public ChestButtonOrder(IEnumerable<string> buttonIds)
        {
            if (buttonIds == null)
            {
                throw new ArgumentNullException(nameof(buttonIds));
            }

            List<string> copy = buttonIds
                .Select(buttonId => CoreGuard.Required(
                    buttonId,
                    nameof(buttonIds)
                ))
                .ToList();

            if (copy.Count != requiredButtonIds.Count ||
                !new HashSet<string>(copy, StringComparer.Ordinal).SetEquals(
                    requiredButtonIds))
            {
                throw new ArgumentException(
                    "Chest order must be a permutation of blue, red, yellow, and green.",
                    nameof(buttonIds)
                );
            }

            this.buttonIds = copy.AsReadOnly();
        }

        public static IReadOnlyList<string> RequiredButtonIds => requiredButtonIds;

        public IReadOnlyList<string> ButtonIds => buttonIds;
    }

    public sealed class RunPhasePlan
    {
        public RunPhasePlan(
            int phaseId,
            InstructionContentReference content,
            TaskVariant taskVariant)
        {
            if (phaseId < 1 || phaseId > PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseId));
            }

            Content = content ?? throw new ArgumentNullException(nameof(content));
            TaskVariant = taskVariant ??
                throw new ArgumentNullException(nameof(taskVariant));
            if (content.PhaseId != phaseId || taskVariant.PhaseId != phaseId ||
                !string.Equals(
                    content.SentenceId,
                    taskVariant.SentenceId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Phase, content, and Task Variant must identify the same sentence."
                );
            }

            PhaseId = phaseId;
        }

        public int PhaseId { get; }

        public string SentenceId => Content.SentenceId;

        public InstructionContentReference Content { get; }

        public TaskVariant TaskVariant { get; }
    }

    public sealed class RunPlan
    {
        private readonly ReadOnlyCollection<RunPhasePlan> phases;

        public RunPlan(
            string batchId,
            string participantId,
            string runId,
            string appSessionId,
            DateTimeOffset createdUtc,
            string appVersion,
            string gitCommit,
            int seed,
            AssistanceAssignment conditionAssignment,
            SafePassword safePassword,
            ChestButtonOrder chestButtonOrder,
            IEnumerable<RunPhasePlan> phases)
        {
            BatchId = CoreGuard.Required(batchId, nameof(batchId));
            ParticipantId = CoreGuard.Required(
                participantId,
                nameof(participantId)
            );
            RunId = CoreGuard.Required(runId, nameof(runId));
            AppSessionId = CoreGuard.Required(
                appSessionId,
                nameof(appSessionId)
            );
            CreatedUtc = createdUtc.ToUniversalTime();
            AppVersion = CoreGuard.Required(appVersion, nameof(appVersion));
            GitCommit = CoreGuard.Required(gitCommit, nameof(gitCommit));
            ConditionAssignment = conditionAssignment ??
                throw new ArgumentNullException(nameof(conditionAssignment));
            SafePassword = safePassword ??
                throw new ArgumentNullException(nameof(safePassword));
            ChestButtonOrder = chestButtonOrder ??
                throw new ArgumentNullException(nameof(chestButtonOrder));
            if (phases == null)
            {
                throw new ArgumentNullException(nameof(phases));
            }

            var phaseCopy = phases.ToList();
            if (phaseCopy.Count != PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentException(
                    "A Run Plan must contain exactly six ordered phases.",
                    nameof(phases)
                );
            }

            for (int index = 0; index < phaseCopy.Count; index++)
            {
                if (phaseCopy[index] == null ||
                    phaseCopy[index].PhaseId != index + 1)
                {
                    throw new ArgumentException(
                        "Run phases must be non-null and ordered 1 through 6.",
                        nameof(phases)
                    );
                }
            }

            SchemaVersion = InteractionContractV1.SchemaVersion;
            Seed = seed;
            this.phases = phaseCopy.AsReadOnly();
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

        public AssistanceCondition AssistanceCondition =>
            ConditionAssignment.Condition;

        public AssistanceAssignment ConditionAssignment { get; }

        public SafePassword SafePassword { get; }

        public ChestButtonOrder ChestButtonOrder { get; }

        public IReadOnlyList<RunPhasePlan> Phases => phases;
    }

    public sealed class RunPlanGenerationRequest
    {
        public RunPlanGenerationRequest(
            string batchId,
            string participantId,
            string appSessionId,
            string appVersion,
            string gitCommit,
            int seed,
            InstructionContentCatalog contentCatalog)
        {
            BatchId = CoreGuard.Required(batchId, nameof(batchId));
            ParticipantId = CoreGuard.Required(
                participantId,
                nameof(participantId)
            );
            AppSessionId = CoreGuard.Required(
                appSessionId,
                nameof(appSessionId)
            );
            AppVersion = CoreGuard.Required(appVersion, nameof(appVersion));
            GitCommit = CoreGuard.Required(gitCommit, nameof(gitCommit));
            Seed = seed;
            ContentCatalog = contentCatalog ??
                throw new ArgumentNullException(nameof(contentCatalog));
        }

        public string BatchId { get; }

        public string ParticipantId { get; }

        public string AppSessionId { get; }

        public string AppVersion { get; }

        public string GitCommit { get; }

        public int Seed { get; }

        public InstructionContentCatalog ContentCatalog { get; }
    }
}
