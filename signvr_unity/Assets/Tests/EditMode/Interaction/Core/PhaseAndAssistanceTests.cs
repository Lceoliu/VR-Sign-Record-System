using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SignVR.Interaction.Core.Tests
{
    public sealed class PhaseAndAssistanceTests
    {
        [TestCase(1, 1, 3, 3)]
        [TestCase(2, 4, 12, 9)]
        [TestCase(3, 13, 15, 3)]
        [TestCase(4, 16, 18, 3)]
        [TestCase(5, 19, 25, 7)]
        [TestCase(6, 26, 31, 6)]
        public void SentenceRanges_HaveFrozenInclusiveBounds(
            int phaseId,
            int first,
            int last,
            int count)
        {
            PhaseSentenceRange range = PhaseSentenceRanges.ForPhase(phaseId);

            Assert.That(range.FirstSentenceNumber, Is.EqualTo(first));
            Assert.That(range.LastSentenceNumber, Is.EqualTo(last));
            Assert.That(range.Count, Is.EqualTo(count));
            Assert.That(range.Contains(first), Is.True);
            Assert.That(range.Contains(last), Is.True);
            Assert.That(range.Contains(first - 1), Is.False);
            Assert.That(range.Contains(last + 1), Is.False);
        }

        [Test]
        public void Catalogs_GroupAll31SentencesWithoutOffsetOrGap()
        {
            string[] expectedIds = Enumerable.Range(
                    1,
                    PhaseSentenceRanges.TotalSentenceCount
                )
                .Select(PhaseSentenceRanges.FormatSentenceId)
                .ToArray();

            Assert.That(
                PhaseSentenceRanges.AllSentenceIds,
                Is.EqualTo(expectedIds)
            );
            Assert.That(TaskVariantCatalog.All.Count, Is.EqualTo(31));

            for (int index = 0; index < expectedIds.Length; index++)
            {
                string sentenceId = expectedIds[index];
                TaskVariant variant = TaskVariantCatalog.All[index];
                Assert.That(variant.SentenceId, Is.EqualTo(sentenceId));
                Assert.That(
                    variant.PhaseId,
                    Is.EqualTo(PhaseSentenceRanges.GetPhaseId(sentenceId))
                );
                Assert.That(
                    TaskVariantCatalog.ForSentence(sentenceId),
                    Is.SameAs(variant)
                );
            }
        }

        [Test]
        public void UniformSentenceSampling_StaysInRangeAndReachesEveryId()
        {
            const int sampleCount = 4096;
            InstructionContentCatalog contentCatalog =
                CoreTestData.CreateContentCatalog();
            RunPlanGenerator generator = new RunPlanGenerator(
                () => new Guid("00000000-0000-0000-0000-000000000001"),
                () => CoreTestData.FixedUtc
            );
            var counts = new Dictionary<string, int>[
                PhaseSentenceRanges.PhaseCount
            ];
            for (int phaseIndex = 0; phaseIndex < counts.Length; phaseIndex++)
            {
                counts[phaseIndex] = new Dictionary<string, int>(
                    StringComparer.Ordinal
                );
            }

            var assignment = new AssistanceAssignment(
                AssistanceCondition.SignOnly,
                0,
                0
            );
            for (int seed = 0; seed < sampleCount; seed++)
            {
                RunPlan plan = generator.Generate(
                    CoreTestData.CreateRequest(seed, contentCatalog),
                    assignment
                );
                for (int phaseIndex = 0;
                    phaseIndex < plan.Phases.Count;
                    phaseIndex++)
                {
                    string sentenceId = plan.Phases[phaseIndex].SentenceId;
                    PhaseSentenceRange range = PhaseSentenceRanges.ForPhase(
                        phaseIndex + 1
                    );
                    Assert.That(range.Contains(sentenceId), Is.True);
                    counts[phaseIndex][sentenceId] =
                        counts[phaseIndex].TryGetValue(
                            sentenceId,
                            out int currentCount)
                            ? currentCount + 1
                            : 1;
                }
            }

            for (int phaseIndex = 0;
                phaseIndex < PhaseSentenceRanges.PhaseCount;
                phaseIndex++)
            {
                PhaseSentenceRange range = PhaseSentenceRanges.ForPhase(
                    phaseIndex + 1
                );
                Assert.That(
                    counts[phaseIndex].Keys,
                    Is.EquivalentTo(range.SentenceIds)
                );

                double expected = sampleCount / (double)range.Count;
                foreach (int actual in counts[phaseIndex].Values)
                {
                    Assert.That(
                        Math.Abs(actual - expected),
                        Is.LessThan(expected * 0.25d),
                        $"Phase {phaseIndex + 1} sampling is unexpectedly skewed."
                    );
                }
            }
        }

        [Test]
        public void AssistanceAllocator_EachTwoRunBlockUsesBothConditionsOnce()
        {
            var allocator = new AssistanceBlockAllocator(2468);

            for (int blockIndex = 0; blockIndex < 5; blockIndex++)
            {
                AssistanceAssignment[] block = Enumerable.Range(0, 2)
                    .Select(_ => allocator.AllocateNext())
                    .ToArray();

                Assert.That(
                    block.Select(item => item.Condition),
                    Is.EquivalentTo(new[]
                    {
                        AssistanceCondition.TextAndPointing,
                        AssistanceCondition.TextOnly
                    })
                );
                Assert.That(
                    block.Select(item => item.BlockIndex),
                    Is.All.EqualTo(blockIndex)
                );
                Assert.That(
                    block.Select(item => item.SlotIndex),
                    Is.EqualTo(new[] { 0, 1 })
                );
            }
        }

        [Test]
        public void AssistanceAllocator_TestOverrideForcesEveryRunWithoutShuffle()
        {
            var allocator = new AssistanceBlockAllocator(
                2468,
                forceTextAndPointing: true
            );

            AssistanceAssignment[] assignments = Enumerable.Range(0, 7)
                .Select(_ => allocator.AllocateNext())
                .ToArray();

            Assert.That(
                assignments.Select(item => item.Condition),
                Is.All.EqualTo(AssistanceCondition.TextAndPointing)
            );
            Assert.That(assignments.All(item => item.IsForced), Is.True);
            Assert.That(
                assignments.Select(item => item.Mode),
                Is.All.EqualTo(AssistanceAssignmentMode.ForcedTextAndPointing)
            );
            Assert.That(
                assignments.Select(item => item.BlockIndex),
                Is.EqualTo(new[] { 0, 0, 1, 1, 2, 2, 3 })
            );
            Assert.That(
                assignments.Select(item => item.SlotIndex),
                Is.EqualTo(new[] { 0, 1, 0, 1, 0, 1, 0 })
            );
        }

        [Test]
        public void AssistanceAllocator_NewApplicationSessionRestartsTheBlock()
        {
            var firstSession = new AssistanceBlockAllocator(99);
            AssistanceAssignment originalFirst = firstSession.AllocateNext();
            firstSession.AllocateNext();
            firstSession.AllocateNext();
            firstSession.AllocateNext();

            var restartedSession = new AssistanceBlockAllocator(99);
            AssistanceAssignment restartedFirst = restartedSession.AllocateNext();

            Assert.That(restartedFirst.BlockIndex, Is.Zero);
            Assert.That(restartedFirst.SlotIndex, Is.Zero);
            Assert.That(
                restartedFirst.Condition,
                Is.EqualTo(originalFirst.Condition),
                "Same seed proves session state, rather than persisted position, resets."
            );
        }

        [Test]
        public void AssistancePresentation_DerivesSupportedVisibilityFromThreeStates()
        {
            Assert.That(
                Enum.GetValues(typeof(AssistanceCondition)).Length,
                Is.EqualTo(3)
            );
            Assert.That(
                AssistanceCondition.TextAndPointing.IncludesText(),
                Is.True
            );
            Assert.That(
                AssistanceCondition.TextAndPointing.IncludesPointing(),
                Is.True
            );
            Assert.That(AssistanceCondition.TextOnly.IncludesText(), Is.True);
            Assert.That(
                AssistanceCondition.TextOnly.IncludesPointing(),
                Is.False
            );
            Assert.That(AssistanceCondition.SignOnly.IncludesText(), Is.False);
            Assert.That(
                AssistanceCondition.SignOnly.IncludesPointing(),
                Is.False
            );
        }
    }
}
