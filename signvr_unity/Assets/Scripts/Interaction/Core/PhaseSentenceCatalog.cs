using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SignVR.Interaction.Core
{
    public sealed class PhaseSentenceRange
    {
        private readonly ReadOnlyCollection<string> sentenceIds;

        public PhaseSentenceRange(
            int phaseId,
            int firstSentenceNumber,
            int lastSentenceNumber)
        {
            if (phaseId < 1 || phaseId > PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseId));
            }

            if (firstSentenceNumber < 1 ||
                lastSentenceNumber < firstSentenceNumber ||
                lastSentenceNumber > PhaseSentenceRanges.TotalSentenceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firstSentenceNumber),
                    "Sentence range bounds are invalid."
                );
            }

            PhaseId = phaseId;
            FirstSentenceNumber = firstSentenceNumber;
            LastSentenceNumber = lastSentenceNumber;

            var ids = new List<string>(Count);
            for (int number = firstSentenceNumber;
                number <= lastSentenceNumber;
                number++)
            {
                ids.Add(PhaseSentenceRanges.FormatSentenceId(number));
            }

            sentenceIds = ids.AsReadOnly();
        }

        public int PhaseId { get; }

        public int FirstSentenceNumber { get; }

        public int LastSentenceNumber { get; }

        public int Count => LastSentenceNumber - FirstSentenceNumber + 1;

        public IReadOnlyList<string> SentenceIds => sentenceIds;

        public bool Contains(int sentenceNumber)
        {
            return sentenceNumber >= FirstSentenceNumber &&
                sentenceNumber <= LastSentenceNumber;
        }

        public bool Contains(string sentenceId)
        {
            return PhaseSentenceRanges.TryParseSentenceNumber(
                sentenceId,
                out int sentenceNumber
            ) && Contains(sentenceNumber);
        }
    }

    public static class PhaseSentenceRanges
    {
        public const int PhaseCount = 6;
        public const int TotalSentenceCount = 31;

        private static readonly ReadOnlyCollection<PhaseSentenceRange> ranges =
            new List<PhaseSentenceRange>
            {
                new PhaseSentenceRange(1, 1, 3),
                new PhaseSentenceRange(2, 4, 12),
                new PhaseSentenceRange(3, 13, 15),
                new PhaseSentenceRange(4, 16, 18),
                new PhaseSentenceRange(5, 19, 25),
                new PhaseSentenceRange(6, 26, 31)
            }.AsReadOnly();

        private static readonly ReadOnlyCollection<string> allSentenceIds =
            ranges.SelectMany(range => range.SentenceIds).ToList().AsReadOnly();

        public static IReadOnlyList<PhaseSentenceRange> All => ranges;

        public static IReadOnlyList<string> AllSentenceIds => allSentenceIds;

        public static PhaseSentenceRange ForPhase(int phaseId)
        {
            if (phaseId < 1 || phaseId > PhaseCount)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseId));
            }

            return ranges[phaseId - 1];
        }

        public static int GetPhaseId(string sentenceId)
        {
            int sentenceNumber = ParseSentenceNumber(sentenceId);
            for (int index = 0; index < ranges.Count; index++)
            {
                if (ranges[index].Contains(sentenceNumber))
                {
                    return ranges[index].PhaseId;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(sentenceId));
        }

        public static string FormatSentenceId(int sentenceNumber)
        {
            if (sentenceNumber < 1 || sentenceNumber > TotalSentenceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(sentenceNumber));
            }

            return sentenceNumber.ToString("D3", CultureInfo.InvariantCulture);
        }

        public static int ParseSentenceNumber(string sentenceId)
        {
            if (!TryParseSentenceNumber(sentenceId, out int sentenceNumber))
            {
                throw new FormatException(
                    "Sentence IDs must be three digits from 001 through 031."
                );
            }

            return sentenceNumber;
        }

        public static bool TryParseSentenceNumber(
            string sentenceId,
            out int sentenceNumber)
        {
            sentenceNumber = 0;
            return sentenceId != null &&
                sentenceId.Length == 3 &&
                int.TryParse(
                    sentenceId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out sentenceNumber
                ) &&
                sentenceNumber >= 1 &&
                sentenceNumber <= TotalSentenceCount;
        }
    }

    public sealed class TaskVariant
    {
        private readonly ReadOnlyCollection<string> targetIds;
        private readonly ReadOnlyCollection<string> orderedTargetIds;

        public TaskVariant(
            int phaseId,
            string sentenceId,
            string variantId,
            IEnumerable<string> targetIds,
            IEnumerable<string> orderedTargetIds = null)
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

            PhaseId = phaseId;
            SentenceId = normalizedSentenceId;
            VariantId = CoreGuard.Required(variantId, nameof(variantId));
            this.targetIds = CopyUniqueRequiredIds(
                targetIds,
                nameof(targetIds),
                allowEmpty: false
            );
            this.orderedTargetIds = CopyUniqueRequiredIds(
                orderedTargetIds ?? Array.Empty<string>(),
                nameof(orderedTargetIds),
                allowEmpty: true
            );

            for (int index = 0; index < this.orderedTargetIds.Count; index++)
            {
                if (!this.targetIds.Contains(this.orderedTargetIds[index]))
                {
                    throw new ArgumentException(
                        "Every ordered target must also be a legal target.",
                        nameof(orderedTargetIds)
                    );
                }
            }
        }

        public int PhaseId { get; }

        public string SentenceId { get; }

        public string VariantId { get; }

        public IReadOnlyList<string> TargetIds => targetIds;

        public IReadOnlyList<string> OrderedTargetIds => orderedTargetIds;

        private static ReadOnlyCollection<string> CopyUniqueRequiredIds(
            IEnumerable<string> values,
            string parameterName,
            bool allowEmpty)
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var copy = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                string normalized = CoreGuard.Required(value, parameterName);
                if (!unique.Add(normalized))
                {
                    throw new ArgumentException(
                        "Target IDs must be unique.",
                        parameterName
                    );
                }

                copy.Add(normalized);
            }

            if (!allowEmpty && copy.Count == 0)
            {
                throw new ArgumentException(
                    "At least one target ID is required.",
                    parameterName
                );
            }

            return copy.AsReadOnly();
        }
    }

    /// <summary>
    /// Maps the 31 canonical sentence IDs to logical scene-independent targets.
    /// Unity adapters own the later mapping from these logical IDs to scene
    /// objects.
    /// </summary>
    public static class TaskVariantCatalog
    {
        private static readonly ReadOnlyCollection<TaskVariant> variants =
            CreateVariants().AsReadOnly();

        private static readonly ReadOnlyDictionary<string, TaskVariant> bySentence =
            new ReadOnlyDictionary<string, TaskVariant>(
                variants.ToDictionary(
                    variant => variant.SentenceId,
                    StringComparer.Ordinal
                )
            );

        public static IReadOnlyList<TaskVariant> All => variants;

        public static TaskVariant ForSentence(string sentenceId)
        {
            string normalized = CoreGuard.Required(
                sentenceId,
                nameof(sentenceId)
            );
            if (!bySentence.TryGetValue(normalized, out TaskVariant variant))
            {
                throw new KeyNotFoundException(
                    $"No Task Variant is defined for sentence {normalized}."
                );
            }

            return variant;
        }

        private static List<TaskVariant> CreateVariants()
        {
            var result = new List<TaskVariant>(
                PhaseSentenceRanges.TotalSentenceCount
            );

            Add(result, 1, 1, new[] { "box_stool" });
            Add(result, 1, 2, new[] { "box_floor_a" });
            Add(result, 1, 3, new[] { "box_floor_b" });

            string[] coins = { "coin_dragon", "coin_a", "coin_b" };
            string[] plates = { "plate_dragon", "plate_a", "plate_b" };
            int sentenceNumber = 4;
            for (int coinIndex = 0; coinIndex < coins.Length; coinIndex++)
            {
                for (int plateIndex = 0; plateIndex < plates.Length; plateIndex++)
                {
                    Add(
                        result,
                        2,
                        sentenceNumber++,
                        new[] { coins[coinIndex], plates[plateIndex] }
                    );
                }
            }

            Add(result, 3, 13, new[] { "picture_frame_a" });
            Add(result, 3, 14, new[] { "picture_frame_b" });
            Add(result, 3, 15, new[] { "picture_frame_c" });

            Add(result, 4, 16, new[] { "key_a" });
            Add(result, 4, 17, new[] { "key_b" });
            Add(result, 4, 18, new[] { "motorbike_key" });

            Add(result, 5, 19, new[] { "button_a" });
            Add(result, 5, 20, new[] { "button_b" });
            Add(result, 5, 21, new[] { "button_c" });
            Add(result, 5, 22, new[] { "button_a", "button_b" });
            Add(result, 5, 23, new[] { "button_a", "button_c" });
            Add(result, 5, 24, new[] { "button_b", "button_c" });
            Add(
                result,
                5,
                25,
                new[] { "button_a", "button_b", "button_c" }
            );

            string[] breakers = { "breaker_a", "breaker_b", "breaker_c" };
            Add(result, 6, 26, breakers, new[] { "breaker_a", "breaker_b", "breaker_c" });
            Add(result, 6, 27, breakers, new[] { "breaker_a", "breaker_c", "breaker_b" });
            Add(result, 6, 28, breakers, new[] { "breaker_b", "breaker_a", "breaker_c" });
            Add(result, 6, 29, breakers, new[] { "breaker_b", "breaker_c", "breaker_a" });
            Add(result, 6, 30, breakers, new[] { "breaker_c", "breaker_a", "breaker_b" });
            Add(result, 6, 31, breakers, new[] { "breaker_c", "breaker_b", "breaker_a" });

            if (result.Count != PhaseSentenceRanges.TotalSentenceCount)
            {
                throw new InvalidOperationException(
                    "The Task Variant catalog must contain exactly 31 entries."
                );
            }

            return result;
        }

        private static void Add(
            ICollection<TaskVariant> result,
            int phaseId,
            int sentenceNumber,
            IEnumerable<string> targetIds,
            IEnumerable<string> orderedTargetIds = null)
        {
            string sentenceId = PhaseSentenceRanges.FormatSentenceId(
                sentenceNumber
            );
            result.Add(new TaskVariant(
                phaseId,
                sentenceId,
                $"variant_{sentenceId}",
                targetIds,
                orderedTargetIds
            ));
        }
    }
}
