using System;
using System.Collections.Generic;

namespace SignVR.Interaction.Core.Tests
{
    internal static class CoreTestData
    {
        public static readonly DateTimeOffset FixedUtc =
            new DateTimeOffset(2026, 8, 26, 10, 15, 30, TimeSpan.Zero);

        public static InstructionContentCatalog CreateContentCatalog()
        {
            var references = new List<InstructionContentReference>(
                PhaseSentenceRanges.TotalSentenceCount
            );
            for (int sentenceNumber = 1;
                sentenceNumber <= PhaseSentenceRanges.TotalSentenceCount;
                sentenceNumber++)
            {
                string sentenceId = PhaseSentenceRanges.FormatSentenceId(
                    sentenceNumber
                );
                references.Add(new InstructionContentReference(
                    PhaseSentenceRanges.GetPhaseId(sentenceId),
                    sentenceId,
                    InteractionContractV1.PilotSignerId,
                    $"take_{sentenceId}_1",
                    FixedUtc.AddMinutes(sentenceNumber),
                    1,
                    $"wang/sentence_{sentenceId}/pose.jsonl",
                    new string('a', 64)
                ));
            }

            return new InstructionContentCatalog(references);
        }

        public static RunPlanGenerationRequest CreateRequest(
            int seed,
            InstructionContentCatalog contentCatalog = null)
        {
            return new RunPlanGenerationRequest(
                "pilot-20260826",
                "P001",
                "app_test_session",
                "1.0.0-test",
                "35fcc93",
                seed,
                contentCatalog ?? CreateContentCatalog()
            );
        }

        public static RunPlanGenerator CreateDeterministicIdentityGenerator()
        {
            int identity = 0;
            return new RunPlanGenerator(
                () =>
                {
                    identity++;
                    byte[] bytes = new byte[16];
                    BitConverter.GetBytes(identity).CopyTo(bytes, 0);
                    return new Guid(bytes);
                },
                () => FixedUtc
            );
        }

        public static InteractionRunStateMachine CreateStateMachine(
            int allocatorSeed = 1234)
        {
            return new InteractionRunStateMachine(
                new AssistanceBlockAllocator(allocatorSeed),
                CreateDeterministicIdentityGenerator()
            );
        }

        public static InteractionRunStateMachine StartRunning(
            int seed = 5678,
            TimeSpan? startedAt = null)
        {
            InteractionRunStateMachine machine = CreateStateMachine();
            machine.Start(CreateRequest(seed));
            machine.Schedule(FixedUtc.AddSeconds(2));
            machine.RunStarted(startedAt ?? TimeSpan.Zero);
            return machine;
        }
    }
}
