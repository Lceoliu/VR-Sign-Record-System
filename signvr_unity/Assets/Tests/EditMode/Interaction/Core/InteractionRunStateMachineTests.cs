using System;
using System.Linq;
using NUnit.Framework;

namespace SignVR.Interaction.Core.Tests
{
    public sealed class InteractionRunStateMachineTests
    {
        [Test]
        public void NormalRun_TransitionsAcrossAllSixPhasesAndCompletes()
        {
            InteractionRunStateMachine machine = CoreTestData
                .CreateStateMachine();

            RunPlan plan = machine.Start(CoreTestData.CreateRequest(1001));
            Assert.That(machine.State, Is.EqualTo(RunState.Preparing));
            Assert.That(machine.Plan, Is.SameAs(plan));

            DateTimeOffset scheduled = CoreTestData.FixedUtc.AddSeconds(2);
            machine.Schedule(scheduled);
            Assert.That(machine.State, Is.EqualTo(RunState.Scheduled));
            Assert.That(machine.ScheduledStartUtc, Is.EqualTo(scheduled));

            machine.RunStarted(TimeSpan.Zero);
            Assert.That(machine.State, Is.EqualTo(RunState.Running));
            Assert.That(machine.CurrentPhaseId, Is.EqualTo(1));
            Assert.That(
                machine.CurrentPhase.State,
                Is.EqualTo(PhaseState.FirstPlayback)
            );

            for (int phaseId = 1;
                phaseId <= PhaseSentenceRanges.PhaseCount;
                phaseId++)
            {
                Assert.That(machine.CurrentPhaseId, Is.EqualTo(phaseId));
                machine.FirstPlaybackCompleted();
                Assert.That(
                    machine.CurrentPhase.State,
                    Is.EqualTo(PhaseState.Active)
                );
                machine.CompletePhase(TimeSpan.FromSeconds(phaseId));
            }

            Assert.That(machine.State, Is.EqualTo(RunState.Completing));
            Assert.That(machine.CurrentPhaseId, Is.Null);
            Assert.That(
                machine.PhaseSnapshots.Select(phase => phase.Result),
                Is.All.EqualTo(PhaseResult.Completed)
            );

            machine.MarkRunCompleted();

            Assert.That(machine.State, Is.EqualTo(RunState.Completed));
            Assert.That(
                machine.LastResult.Result,
                Is.EqualTo(RunResult.Completed)
            );
            Assert.That(machine.LastResult.Plan, Is.SameAs(plan));
            Assert.That(machine.LastResult.CompletedPhaseCount, Is.EqualTo(6));
            Assert.That(machine.LastResult.StuckPhaseCount, Is.Zero);
        }

        [Test]
        public void Playback_IsParticipantStartedAndReplayRemainsAvailable()
        {
            InteractionRunStateMachine machine = CoreTestData.StartRunning();

            Assert.That(machine.CurrentPhase.ReplayAvailable, Is.True);
            Assert.That(machine.CurrentPhase.GiveUpAvailable, Is.True);
            machine.ReplayInstruction();
            Assert.That(
                machine.CurrentPhase.State,
                Is.EqualTo(PhaseState.FirstPlayback)
            );

            machine.FirstPlaybackCompleted();
            Assert.That(machine.CurrentPhase.ReplayAvailable, Is.True);
            Assert.That(machine.CurrentPhase.GiveUpAvailable, Is.True);

            machine.ReplayInstruction();
            Assert.That(
                machine.CurrentPhase.State,
                Is.EqualTo(PhaseState.ReplayPlayback)
            );
            Assert.That(machine.CurrentPhase.ReplayUsed, Is.True);
            machine.ReplayPlaybackCompleted();
            Assert.That(machine.CurrentPhase.ReplayAvailable, Is.True);
            Assert.That(machine.CurrentPhase.GiveUpAvailable, Is.True);
            machine.ReplayInstruction();
            Assert.That(
                machine.CurrentPhase.State,
                Is.EqualTo(PhaseState.ReplayPlayback)
            );
        }

        [Test]
        public void InteractionError_ClearsAllTaskProgressWithoutChangingPlan()
        {
            InteractionRunStateMachine machine = CoreTestData.StartRunning();
            RunPlan frozenPlan = machine.Plan;

            Assert.That(
                machine.CurrentPhase.InteractionsEnabled,
                Is.True,
                "Interaction must be enabled during first playback."
            );
            machine.AdvanceTaskProgress();
            machine.AdvanceTaskProgress();
            machine.AdvanceTaskProgress();
            Assert.That(machine.CurrentPhase.TaskProgress, Is.EqualTo(3));

            machine.RecordInteractionError();

            Assert.That(machine.CurrentPhase.TaskProgress, Is.Zero);
            Assert.That(machine.CurrentPhase.InteractionErrorCount, Is.EqualTo(1));
            Assert.That(machine.Plan, Is.SameAs(frozenPlan));
            Assert.That(
                machine.Plan.SafePassword,
                Is.SameAs(frozenPlan.SafePassword)
            );
            Assert.That(
                machine.Plan.Phases[0].TaskVariant,
                Is.SameAs(frozenPlan.Phases[0].TaskVariant)
            );
        }

        [Test]
        public void StuckPhase_CanAdvanceBeforePlaybackAndIsRetained()
        {
            InteractionRunStateMachine machine = CoreTestData.StartRunning();

            machine.GiveUpPhase(TimeSpan.FromSeconds(1));

            Assert.That(machine.CurrentPhaseId, Is.EqualTo(2));
            Assert.That(
                machine.PhaseSnapshots[0].State,
                Is.EqualTo(PhaseState.Stuck)
            );
            Assert.That(
                machine.PhaseSnapshots[0].Result,
                Is.EqualTo(PhaseResult.Stuck)
            );

            for (int phaseId = 2; phaseId <= 6; phaseId++)
            {
                machine.FirstPlaybackCompleted();
                machine.CompletePhase(TimeSpan.FromSeconds(phaseId));
            }

            machine.MarkRunCompleted();
            Assert.That(machine.LastResult.CompletedPhaseCount, Is.EqualTo(5));
            Assert.That(machine.LastResult.StuckPhaseCount, Is.EqualTo(1));
        }

        [Test]
        public void Abort_PreservesPartialResultAndDoesNotReturnConditionSlot()
        {
            InteractionRunStateMachine machine = CoreTestData
                .CreateStateMachine(4321);
            RunPlan firstPlan = machine.Start(CoreTestData.CreateRequest(10));
            machine.Schedule(CoreTestData.FixedUtc.AddSeconds(1));
            machine.RunStarted(TimeSpan.Zero);
            machine.AdvanceTaskProgress();
            machine.AdvanceTaskProgress();
            machine.FirstPlaybackCompleted();
            machine.ReplayInstruction();
            machine.ReplayPlaybackCompleted();
            Assert.That(
                machine.TryRecordPhaseTimeout(TimeSpan.FromSeconds(180)),
                Is.True
            );

            machine.AbortRun("participant discomfort");
            Assert.That(machine.State, Is.EqualTo(RunState.Aborting));
            machine.MarkRunAborted();

            InteractionRunResult aborted = machine.LastResult;
            Assert.That(machine.State, Is.EqualTo(RunState.Aborted));
            Assert.That(aborted.Result, Is.EqualTo(RunResult.Aborted));
            Assert.That(aborted.Plan, Is.SameAs(firstPlan));
            Assert.That(aborted.AbortReason, Is.EqualTo("participant discomfort"));
            Assert.That(aborted.Phases[0].TaskProgress, Is.EqualTo(2));
            Assert.That(aborted.Phases[0].ReplayUsed, Is.True);
            Assert.That(aborted.Phases[0].TimeoutRecorded, Is.True);

            machine.ResetToPreStart();
            Assert.That(machine.State, Is.EqualTo(RunState.PreStart));
            Assert.That(machine.LastResult, Is.SameAs(aborted));
            RunPlan secondPlan = machine.Start(CoreTestData.CreateRequest(11));

            Assert.That(firstPlan.ConditionAssignment.BlockIndex, Is.Zero);
            Assert.That(firstPlan.ConditionAssignment.SlotIndex, Is.Zero);
            Assert.That(secondPlan.ConditionAssignment.BlockIndex, Is.Zero);
            Assert.That(secondPlan.ConditionAssignment.SlotIndex, Is.EqualTo(1));
            Assert.That(
                secondPlan.AssistanceCondition,
                Is.Not.EqualTo(firstPlan.AssistanceCondition),
                "Abort must not return the consumed no-replacement slot."
            );
        }

        [Test]
        public void Timeout_RecordsOnceAt180SecondsWithoutAutomaticAdvance()
        {
            TimeSpan phaseStart = TimeSpan.FromSeconds(10);
            InteractionRunStateMachine machine = CoreTestData.StartRunning(
                startedAt: phaseStart
            );
            RunPlan frozenPlan = machine.Plan;

            Assert.That(
                machine.TryRecordPhaseTimeout(
                    phaseStart + TimeSpan.FromMilliseconds(179999)
                ),
                Is.False
            );
            Assert.That(
                machine.TryRecordPhaseTimeout(
                    phaseStart + TimeSpan.FromSeconds(180)
                ),
                Is.True
            );
            Assert.That(
                machine.TryRecordPhaseTimeout(
                    phaseStart + TimeSpan.FromSeconds(181)
                ),
                Is.False,
                "Only the first threshold crossing should request a log event."
            );

            Assert.That(machine.State, Is.EqualTo(RunState.Running));
            Assert.That(machine.CurrentPhaseId, Is.EqualTo(1));
            Assert.That(
                machine.CurrentPhase.State,
                Is.EqualTo(PhaseState.FirstPlayback)
            );
            Assert.That(machine.CurrentPhase.TimeoutRecorded, Is.True);
            Assert.That(machine.Plan, Is.SameAs(frozenPlan));
        }

        [Test]
        public void Fault_PreservesPlanAndPartialPhaseSnapshot()
        {
            InteractionRunStateMachine machine = CoreTestData.StartRunning();
            RunPlan frozenPlan = machine.Plan;
            machine.AdvanceTaskProgress();

            machine.FaultRun("capture writer failed");

            Assert.That(machine.State, Is.EqualTo(RunState.Faulted));
            Assert.That(machine.LastResult.Result, Is.EqualTo(RunResult.Faulted));
            Assert.That(machine.LastResult.Plan, Is.SameAs(frozenPlan));
            Assert.That(machine.LastResult.Phases[0].TaskProgress, Is.EqualTo(1));
            Assert.That(
                machine.LastResult.FaultReason,
                Is.EqualTo("capture writer failed")
            );
        }
    }
}
