using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SignVR.Interaction.Core.Tests
{
    public sealed class RunPlanGeneratorTests
    {
        [Test]
        public void Generate_CreatesUniqueFourDigitPasswordAndButtonPermutation()
        {
            InstructionContentCatalog catalog = CoreTestData.CreateContentCatalog();
            RunPlanGenerator generator = CoreTestData
                .CreateDeterministicIdentityGenerator();
            var assignment = new AssistanceAssignment(
                AssistanceCondition.TextOnly,
                0,
                0
            );
            var observedButtonOrders = new HashSet<string>(
                StringComparer.Ordinal
            );

            for (int seed = 0; seed < 256; seed++)
            {
                RunPlan plan = generator.Generate(
                    CoreTestData.CreateRequest(seed, catalog),
                    assignment
                );

                Assert.That(plan.SafePassword.Digits.Count, Is.EqualTo(4));
                Assert.That(
                    plan.SafePassword.Digits.Distinct().Count(),
                    Is.EqualTo(4)
                );
                Assert.That(
                    plan.SafePassword.Digits,
                    Is.All.InRange(0, 9)
                );
                Assert.That(
                    plan.ChestButtonOrder.ButtonIds,
                    Is.EquivalentTo(ChestButtonOrder.RequiredButtonIds)
                );
                observedButtonOrders.Add(string.Join(",", plan
                    .ChestButtonOrder
                    .ButtonIds));
            }

            Assert.That(
                observedButtonOrders.Count,
                Is.GreaterThan(1),
                "Chest-button order must be independently shuffled per seed."
            );
        }

        [Test]
        public void Generate_SameSeedReproducesEveryRandomizedResolvedValue()
        {
            InstructionContentCatalog catalog = CoreTestData.CreateContentCatalog();
            var assignment = new AssistanceAssignment(
                AssistanceCondition.TextAndPointing,
                2,
                1
            );
            Guid fixedGuid = new Guid(
                "11111111-2222-3333-4444-555555555555"
            );
            var firstGenerator = new RunPlanGenerator(
                () => fixedGuid,
                () => CoreTestData.FixedUtc
            );
            var secondGenerator = new RunPlanGenerator(
                () => fixedGuid,
                () => CoreTestData.FixedUtc
            );

            RunPlan first = firstGenerator.Generate(
                CoreTestData.CreateRequest(987654321, catalog),
                assignment
            );
            RunPlan second = secondGenerator.Generate(
                CoreTestData.CreateRequest(987654321, catalog),
                assignment
            );

            Assert.That(second.RunId, Is.EqualTo(first.RunId));
            Assert.That(
                second.Phases.Select(phase => phase.SentenceId),
                Is.EqualTo(first.Phases.Select(phase => phase.SentenceId))
            );
            Assert.That(
                second.Phases.Select(phase => phase.Content.TakeId),
                Is.EqualTo(first.Phases.Select(phase => phase.Content.TakeId))
            );
            Assert.That(
                second.SafePassword.Digits,
                Is.EqualTo(first.SafePassword.Digits)
            );
            Assert.That(
                second.ChestButtonOrder.ButtonIds,
                Is.EqualTo(first.ChestButtonOrder.ButtonIds)
            );
        }

        [Test]
        public void Generate_RepeatedStartsAlwaysReceiveDifferentRunIds()
        {
            InstructionContentCatalog catalog = CoreTestData.CreateContentCatalog();
            RunPlanGenerator generator = CoreTestData
                .CreateDeterministicIdentityGenerator();
            var assignment = new AssistanceAssignment(
                AssistanceCondition.SignOnly,
                0,
                0
            );
            RunPlanGenerationRequest request = CoreTestData.CreateRequest(
                42,
                catalog
            );

            RunPlan first = generator.Generate(request, assignment);
            RunPlan second = generator.Generate(request, assignment);

            Assert.That(second.RunId, Is.Not.EqualTo(first.RunId));
            Assert.That(first.RunId, Does.StartWith("run_20260826T101530Z_"));
            Assert.That(second.RunId, Does.StartWith("run_20260826T101530Z_"));
        }

        [Test]
        public void RunPlanAndNestedValues_DefensivelyFreezeAllSequences()
        {
            RunPlan plan = CoreTestData.CreateDeterministicIdentityGenerator()
                .Generate(
                    CoreTestData.CreateRequest(13579),
                    new AssistanceAssignment(
                        AssistanceCondition.TextOnly,
                        0,
                        0
                    )
                );

            Assert.Throws<NotSupportedException>(() =>
                ((IList<RunPhasePlan>)plan.Phases).Clear()
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<int>)plan.SafePassword.Digits)[0] = 9
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)plan.ChestButtonOrder.ButtonIds)[0] = "red"
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)plan.Phases[0].TaskVariant.TargetIds).Clear()
            );

            int[] mutableDigits = { 1, 2, 3, 4 };
            var password = new SafePassword(mutableDigits);
            mutableDigits[0] = 9;
            Assert.That(password.Digits, Is.EqualTo(new[] { 1, 2, 3, 4 }));

            string[] mutableTargets = { "box_stool" };
            var variant = new TaskVariant(
                1,
                "001",
                "custom_test_variant",
                mutableTargets
            );
            mutableTargets[0] = "changed";
            Assert.That(variant.TargetIds, Is.EqualTo(new[] { "box_stool" }));
        }

        [Test]
        public void GeneratedPlan_ContainsSixOrderedResolvedContentReferences()
        {
            RunPlan plan = CoreTestData.CreateDeterministicIdentityGenerator()
                .Generate(
                    CoreTestData.CreateRequest(24680),
                    new AssistanceAssignment(
                        AssistanceCondition.SignOnly,
                        7,
                        2
                    )
                );

            Assert.That(plan.SchemaVersion, Is.EqualTo(1));
            Assert.That(plan.Phases.Count, Is.EqualTo(6));
            for (int index = 0; index < plan.Phases.Count; index++)
            {
                RunPhasePlan phase = plan.Phases[index];
                Assert.That(phase.PhaseId, Is.EqualTo(index + 1));
                Assert.That(phase.Content.PhaseId, Is.EqualTo(index + 1));
                Assert.That(phase.TaskVariant.PhaseId, Is.EqualTo(index + 1));
                Assert.That(
                    phase.Content.SentenceId,
                    Is.EqualTo(phase.TaskVariant.SentenceId)
                );
                Assert.That(
                    phase.Content.SignerId,
                    Is.EqualTo(InteractionContractV1.PilotSignerId)
                );
            }
        }
    }
}
