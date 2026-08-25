using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SignVR.Interaction.Core
{
    /// <summary>
    /// Produces a complete immutable Run Plan. Randomized fields depend only on
    /// the request seed and an explicit SplitMix64 draw order: six sentence
    /// samples, the safe password, then the independent chest-button shuffle.
    /// Run identity and creation time are deliberately separate dependencies.
    /// </summary>
    public sealed class RunPlanGenerator
    {
        private readonly Func<Guid> createRunGuid;
        private readonly Func<DateTimeOffset> getUtcNow;

        public RunPlanGenerator()
            : this(() => Guid.NewGuid(), () => DateTimeOffset.UtcNow)
        {
        }

        public RunPlanGenerator(
            Func<Guid> createRunGuid,
            Func<DateTimeOffset> getUtcNow)
        {
            this.createRunGuid = createRunGuid ??
                throw new ArgumentNullException(nameof(createRunGuid));
            this.getUtcNow = getUtcNow ??
                throw new ArgumentNullException(nameof(getUtcNow));
        }

        public RunPlan Generate(
            RunPlanGenerationRequest request,
            AssistanceAssignment conditionAssignment)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (conditionAssignment == null)
            {
                throw new ArgumentNullException(nameof(conditionAssignment));
            }

            var random = new DeterministicRandom(request.Seed);
            var phases = new List<RunPhasePlan>(
                PhaseSentenceRanges.PhaseCount
            );
            for (int phaseId = 1;
                phaseId <= PhaseSentenceRanges.PhaseCount;
                phaseId++)
            {
                PhaseSentenceRange range = PhaseSentenceRanges.ForPhase(phaseId);
                int sentenceNumber = range.FirstSentenceNumber +
                    random.NextInt(range.Count);
                string sentenceId = PhaseSentenceRanges.FormatSentenceId(
                    sentenceNumber
                );
                phases.Add(new RunPhasePlan(
                    phaseId,
                    request.ContentCatalog.ForSentence(sentenceId),
                    TaskVariantCatalog.ForSentence(sentenceId)
                ));
            }

            SafePassword safePassword = GenerateSafePassword(random);
            ChestButtonOrder chestButtonOrder = GenerateChestButtonOrder(random);

            DateTimeOffset createdUtc = getUtcNow().ToUniversalTime();
            Guid unique = createRunGuid();
            if (unique == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Run identity source returned an empty GUID."
                );
            }

            string runId = string.Format(
                CultureInfo.InvariantCulture,
                "run_{0}_{1}",
                createdUtc.ToString(
                    "yyyyMMdd'T'HHmmss'Z'",
                    CultureInfo.InvariantCulture
                ),
                unique.ToString("N")
            );

            return new RunPlan(
                request.BatchId,
                request.ParticipantId,
                runId,
                request.AppSessionId,
                createdUtc,
                request.AppVersion,
                request.GitCommit,
                request.Seed,
                conditionAssignment,
                safePassword,
                chestButtonOrder,
                phases
            );
        }

        private static SafePassword GenerateSafePassword(
            DeterministicRandom random)
        {
            int[] digits = Enumerable.Range(0, 10).ToArray();
            random.Shuffle(digits);
            return new SafePassword(digits.Take(SafePassword.DigitCount));
        }

        private static ChestButtonOrder GenerateChestButtonOrder(
            DeterministicRandom random)
        {
            string[] buttonIds = ChestButtonOrder.RequiredButtonIds.ToArray();
            random.Shuffle(buttonIds);
            return new ChestButtonOrder(buttonIds);
        }
    }
}
