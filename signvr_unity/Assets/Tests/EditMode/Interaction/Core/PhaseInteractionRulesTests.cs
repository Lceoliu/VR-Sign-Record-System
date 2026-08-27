using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SignVR.Interaction.Core.Tests
{
    public sealed class PhaseInteractionRulesTests
    {
        [Test]
        public void AllThirtyOneTaskVariantsRouteToFrozenPhaseRanges()
        {
            int[] expectedPhases =
            {
                1, 1, 1,
                2, 2, 2, 2, 2, 2, 2, 2, 2,
                3, 3, 3,
                4, 4, 4,
                5, 5, 5, 5, 5, 5, 5,
                6, 6, 6, 6, 6, 6
            };

            Assert.That(TaskVariantCatalog.All.Count, Is.EqualTo(31));
            for (int index = 0; index < expectedPhases.Length; index++)
            {
                Assert.That(
                    PhaseRuleRouting.GetPhaseId(TaskVariantCatalog.All[index]),
                    Is.EqualTo(expectedPhases[index]),
                    $"sentence {index + 1:D3}"
                );
            }
        }

        [Test]
        public void ConfigureAndEnable_DoNotActivateWithoutW1Snapshot()
        {
            var session = new InteractionPhaseSession();
            InteractionRunStateMachine lifecycle = CoreTestData.StartRunning();

            session.Configure(CreatePlan());
            session.Enable();

            Assert.That(session.CurrentPhaseId, Is.Null);
            Assert.That(session.IsEnabled, Is.False);

            session.Synchronize(lifecycle.CurrentPhase);

            Assert.That(session.CurrentPhaseId, Is.EqualTo(1));
            Assert.That(session.IsEnabled, Is.True);

            lifecycle.FirstPlaybackCompleted();
            lifecycle.ReplayInstruction();
            session.Synchronize(lifecycle.CurrentPhase);

            Assert.That(
                lifecycle.CurrentPhase.State,
                Is.EqualTo(PhaseState.ReplayPlayback)
            );
            Assert.That(session.IsEnabled, Is.True);
        }

        [Test]
        public void Synchronize_RejectsInactiveSkippedAndBackwardSnapshots()
        {
            var session = new InteractionPhaseSession();
            InteractionRunStateMachine phaseOne = CreateLifecycleAtPhase(1);
            InteractionRunStateMachine phaseTwo = CreateLifecycleAtPhase(2);
            InteractionRunStateMachine phaseThree = CreateLifecycleAtPhase(3);
            session.Configure(CreatePlan());
            session.Enable();
            session.Synchronize(phaseOne.CurrentPhase);

            Assert.Throws<ArgumentException>(() =>
                session.Synchronize(phaseOne.PhaseSnapshots[2])
            );
            Assert.Throws<InvalidOperationException>(() =>
                session.Synchronize(phaseThree.CurrentPhase)
            );

            session.Synchronize(phaseTwo.CurrentPhase);

            Assert.Throws<InvalidOperationException>(() =>
                session.Synchronize(phaseOne.CurrentPhase)
            );
        }

        [Test]
        public void PhaseOne_HappyPathSelfLocksUntilW1Advances()
        {
            var harness = new SessionHarness(CreatePlan());
            var results = new List<ValidationResult>();
            harness.Session.ResultProduced += results.Add;

            ValidationResult completed = FinishPhaseOneTask(harness.Session);
            ValidationResult retry = harness.Session.AcceptInput(
                1,
                PhaseInput.Target("box_stool")
            );

            Assert.That(completed.Accepted, Is.True);
            Assert.That(completed.PhaseCompleted, Is.True);
            Assert.That(completed.FeedbackCue, Is.EqualTo(
                PhaseFeedbackCue.SafeDoorOpened
            ));
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(1));
            Assert.That(harness.Session.IsEnabled, Is.False);
            Assert.That(
                retry.Error,
                Is.EqualTo(PhaseValidationError.CompletedPhaseLocked)
            );
            Assert.That(
                results.Count(item => item.PhaseCompleted),
                Is.EqualTo(1)
            );

            harness.AdvanceCompletedPhase();

            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(2));
            Assert.That(harness.Session.IsEnabled, Is.True);
        }

        [Test]
        public void PhaseTwoEntryAfterPhaseOneGiveUpKeepsSafeDoorOpen()
        {
            var harness = new SessionHarness(CreatePlan());
            PhaseExecutionSnapshot giveUpAvailable =
                harness.MakeGiveUpAvailable();

            ValidationResult givenUp = harness.Session.GiveUpCurrentPhase(
                giveUpAvailable
            );
            harness.AdvanceGivenUpPhase();

            Assert.That(givenUp.PhaseGivenUp, Is.True);
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(2));
            Assert.That(
                harness.Session.PresentationSnapshot.SafeDoorOpened,
                Is.True
            );
        }

        [Test]
        public void PhaseTwoSynchronizationOpensSafeDoorWithoutPhaseOneResult()
        {
            var harness = new SessionHarness(CreatePlan());

            harness.AdvanceCompletedPhase();

            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(2));
            Assert.That(
                harness.Session.PresentationSnapshot.SafeDoorOpened,
                Is.True
            );
        }

        [Test]
        public void PhaseOne_WrongBoxRecordsErrorWithoutPasswordProgress()
        {
            var harness = new SessionHarness(CreatePlan());
            ValidationResult wrong = harness.Session.AcceptInput(
                1,
                PhaseInput.Target("box_floor_a")
            );
            ValidationResult completed = harness.Session.AcceptInput(
                1,
                PhaseInput.Target("box_stool")
            );

            Assert.That(wrong.Accepted, Is.False);
            Assert.That(wrong.InteractionError, Is.True);
            Assert.That(wrong.ProgressReset, Is.False);
            Assert.That(wrong.Progress, Is.Zero);
            Assert.That(
                wrong.Error,
                Is.EqualTo(PhaseValidationError.UnexpectedTarget)
            );
            Assert.That(wrong.TargetId, Is.EqualTo("box_floor_a"));
            Assert.That(completed.Accepted, Is.True);
            Assert.That(completed.PhaseCompleted, Is.True);
            Assert.That(completed.Progress, Is.EqualTo(1));
            Assert.That(completed.RequiredProgress, Is.EqualTo(1));
        }

        [Test]
        public void PhaseTwo_AcceptsOnlyPlannedCoinPlatePair()
        {
            var harness = new SessionHarness(
                CreatePlan("001", "012", "013", "016", "019", "026")
            );
            CompletePhaseOne(harness);

            ValidationResult completed = harness.Session.AcceptInput(
                2,
                PhaseInput.Pair("coin_b", "plate_b")
            );

            Assert.That(completed.Accepted, Is.True);
            Assert.That(completed.PhaseCompleted, Is.True);
            Assert.That(completed.FeedbackCue, Is.EqualTo(
                PhaseFeedbackCue.CoinPlaced
            ));
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(2));
        }

        [Test]
        public void PhaseTwo_MismatchDoesNotAdvanceOrResetProgress()
        {
            var harness = new SessionHarness(
                CreatePlan("001", "008", "013", "016", "019", "026")
            );
            CompletePhaseOne(harness);

            ValidationResult wrong = harness.Session.AcceptInput(
                2,
                PhaseInput.Pair("coin_a", "plate_b")
            );

            Assert.That(wrong.Accepted, Is.False);
            Assert.That(wrong.InteractionError, Is.True);
            Assert.That(wrong.ProgressReset, Is.False);
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(2));
            Assert.That(
                harness.Session.AcceptInput(
                    2,
                    PhaseInput.Pair("coin_a", "plate_a")
                ).PhaseCompleted,
                Is.True
            );
        }

        [Test]
        public void ConfigureRejectsMalformedPhaseTwoAndPhaseSixPlans()
        {
            RunPlan valid = CreatePlan();
            var malformedPhaseTwo = new TaskVariant(
                2,
                "004",
                "bad_phase_two",
                new[] { "coin_dragon" }
            );
            var malformedPhaseSix = new TaskVariant(
                6,
                "026",
                "bad_phase_six",
                new[] { "breaker_a" },
                Array.Empty<string>()
            );

            Assert.Throws<ArgumentException>(() =>
                new InteractionPhaseSession().Configure(
                    ReplaceVariant(valid, 2, malformedPhaseTwo)
                )
            );
            Assert.Throws<ArgumentException>(() =>
                new InteractionPhaseSession().Configure(
                    ReplaceVariant(valid, 6, malformedPhaseSix)
                )
            );
        }

        [Test]
        public void PhaseThree_AcceptsPlannedFrameAndRejectsOtherFrames()
        {
            var harness = new SessionHarness(
                CreatePlan("001", "004", "015", "016", "019", "026")
            );
            CompletePhaseOne(harness);
            CompletePhaseTwo(harness);

            ValidationResult wrong = harness.Session.AcceptInput(
                3,
                PhaseInput.Target("picture_frame_a")
            );
            ValidationResult completed = harness.Session.AcceptInput(
                3,
                PhaseInput.Target("picture_frame_c")
            );

            Assert.That(wrong.InteractionError, Is.True);
            Assert.That(completed.PhaseCompleted, Is.True);
            Assert.That(completed.FeedbackCue, Is.EqualTo(
                PhaseFeedbackCue.ChestOrderUnlocked
            ));
        }

        [Test]
        public void PhaseFour_AcceptsPlannedKeyDirectlyAndReleasesOnlyIt()
        {
            var harness = new SessionHarness(
                CreatePlan("001", "004", "013", "018", "019", "026")
            );
            AdvanceToPhase(harness, 4);

            ValidationResult completed = harness.Session.AcceptInput(
                4,
                PhaseInput.Target("motorbike_key")
            );

            Assert.That(completed.PhaseCompleted, Is.True);
            Assert.That(completed.Progress, Is.EqualTo(1));
            Assert.That(completed.RequiredProgress, Is.EqualTo(1));
            Assert.That(completed.FeedbackCue, Is.EqualTo(
                PhaseFeedbackCue.ChestOpened
            ));
            Assert.That(completed.ReleasedTargetId, Is.EqualTo("motorbike_key"));
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(4));
        }

        [Test]
        public void PhaseFour_WrongKeyCanBeRetriedWithPlannedKey()
        {
            var harness = new SessionHarness(CreatePlan());
            AdvanceToPhase(harness, 4);

            ValidationResult wrong = harness.Session.AcceptInput(
                4,
                PhaseInput.Target("key_b")
            );
            ValidationResult retried = harness.Session.AcceptInput(
                4,
                PhaseInput.Target("key_a")
            );

            Assert.That(wrong.Accepted, Is.False);
            Assert.That(wrong.InteractionError, Is.True);
            Assert.That(wrong.ProgressReset, Is.False);
            Assert.That(wrong.Progress, Is.Zero);
            Assert.That(wrong.RequiredProgress, Is.EqualTo(1));
            Assert.That(retried.Accepted, Is.True);
            Assert.That(retried.PhaseCompleted, Is.True);
            Assert.That(retried.ReleasedTargetId, Is.EqualTo("key_a"));
        }

        [Test]
        public void PhaseFive_AcceptsPlannedButtonSetDirectlyWithoutKeyGate()
        {
            var harness = new SessionHarness(
                CreatePlan("001", "004", "013", "017", "025", "026")
            );
            AdvanceToPhase(harness, 5);

            ValidationResult formerKeyGate = harness.Session.AcceptInput(
                5,
                PhaseInput.Target("key_b")
            );
            ValidationResult first = harness.Session.AcceptInput(
                5,
                PhaseInput.Target("button_c")
            );
            harness.Session.AcceptInput(5, PhaseInput.Target("button_a"));
            ValidationResult completed = harness.Session.AcceptInput(
                5,
                PhaseInput.Target("button_b")
            );

            Assert.That(harness.Session.PresentationSnapshot.CabinetUnlocked,
                Is.True);
            Assert.That(formerKeyGate.Accepted, Is.False);
            Assert.That(formerKeyGate.ProgressReset, Is.True);
            Assert.That(first.Accepted, Is.True);
            Assert.That(first.Progress, Is.EqualTo(1));
            Assert.That(first.RequiredProgress, Is.EqualTo(3));
            Assert.That(completed.PhaseCompleted, Is.True);
            Assert.That(completed.FeedbackCue, Is.EqualTo(
                PhaseFeedbackCue.CabinetTaskCompleted
            ));
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(5));
        }

        [Test]
        public void PhaseFive_SupportsAllSevenExistingButtonSets()
        {
            string[] sentenceIds =
            {
                "019", "020", "021", "022", "023", "024", "025"
            };

            foreach (string sentenceId in sentenceIds)
            {
                var harness = new SessionHarness(CreatePlan(
                    "001", "004", "013", "016", sentenceId, "026"
                ));
                AdvanceToPhase(harness, 5);

                ValidationResult completed = FinishPhaseFiveTask(
                    harness.Session
                );

                Assert.That(completed.PhaseCompleted, Is.True, sentenceId);
                Assert.That(completed.Progress, Is.EqualTo(
                    harness.Session.Plan.Phases[4].TaskVariant.TargetIds.Count
                ), sentenceId);
                Assert.That(completed.RequiredProgress, Is.EqualTo(
                    harness.Session.Plan.Phases[4].TaskVariant.TargetIds.Count
                ), sentenceId);
            }
        }

        [Test]
        public void PhaseFive_WrongOrRepeatedButtonClearsEntireSet()
        {
            AssertPhaseFiveButtonReset(
                "022",
                "button_a",
                "button_c",
                new[] { "button_b", "button_a" }
            );
            AssertPhaseFiveButtonReset(
                "025",
                "button_a",
                "button_a",
                new[] { "button_b", "button_c", "button_a" }
            );
        }

        [Test]
        public void PhaseSix_UsesOrderedTargetsAndOnlyRequestsPhaseCompletion()
        {
            var harness = new SessionHarness(
                CreatePlan("001", "004", "013", "016", "019", "031")
            );
            AdvanceToPhase(harness, 6);

            ValidationResult completed = FinishPhaseSixTask(harness.Session);
            ValidationResult retry = harness.Session.AcceptInput(
                6,
                PhaseInput.Target("breaker_a")
            );

            Assert.That(completed.PhaseCompleted, Is.True);
            Assert.That(
                typeof(ValidationResult).GetProperty("RunCompleted"),
                Is.Null,
                "Only W1 may expose a Run completion result."
            );
            Assert.That(completed.FeedbackCue, Is.EqualTo(
                PhaseFeedbackCue.FinalLeftDoorOpened
            ));
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(6));
            Assert.That(
                retry.Error,
                Is.EqualTo(PhaseValidationError.CompletedPhaseLocked)
            );
        }

        [Test]
        public void PhaseSix_AnyWrongTargetClearsEntireOrder()
        {
            var harness = new SessionHarness(CreatePlan());
            AdvanceToPhase(harness, 6);
            harness.Session.AcceptInput(6, PhaseInput.Target("breaker_a"));
            harness.Session.AcceptInput(6, PhaseInput.Target("breaker_b"));

            ValidationResult wrong = harness.Session.AcceptInput(
                6,
                PhaseInput.Target("breaker_a")
            );
            ValidationResult provesReset = harness.Session.AcceptInput(
                6,
                PhaseInput.Target("breaker_b")
            );

            Assert.That(wrong.ProgressReset, Is.True);
            Assert.That(wrong.Progress, Is.Zero);
            Assert.That(provesReset.Accepted, Is.False);
            Assert.That(provesReset.ProgressReset, Is.True);
        }

        [Test]
        public void FutureAndCompletedPhaseInputsAreRejectedBySnapshotGate()
        {
            var harness = new SessionHarness(CreatePlan());

            ValidationResult future = harness.Session.AcceptInput(
                2,
                PhaseInput.Pair("coin_dragon", "plate_dragon")
            );
            CompletePhaseOne(harness);
            ValidationResult completed = harness.Session.AcceptInput(
                1,
                PhaseInput.Target("box_stool")
            );

            Assert.That(
                future.Error,
                Is.EqualTo(PhaseValidationError.FuturePhaseLocked)
            );
            Assert.That(
                completed.Error,
                Is.EqualTo(PhaseValidationError.CompletedPhaseLocked)
            );
        }

        [Test]
        public void GiveUp_RequiresAvailableMatchingW1SnapshotAndCannotChain()
        {
            var harness = new SessionHarness(CreatePlan());

            ValidationResult unavailable = harness.Session.GiveUpCurrentPhase(
                harness.Lifecycle.CurrentPhase
            );
            InteractionRunStateMachine other = CoreTestData.StartRunning();
            other.FirstPlaybackCompleted();
            other.ReplayInstruction();
            other.ReplayPlaybackCompleted();
            ValidationResult mismatch = harness.Session.GiveUpCurrentPhase(
                other.CurrentPhase
            );
            PhaseExecutionSnapshot available = harness.MakeGiveUpAvailable();
            ValidationResult givenUp = harness.Session.GiveUpCurrentPhase(
                available
            );
            ValidationResult duplicate = harness.Session.GiveUpCurrentPhase(
                available
            );

            Assert.That(
                unavailable.Error,
                Is.EqualTo(PhaseValidationError.GiveUpUnavailable)
            );
            Assert.That(
                mismatch.Error,
                Is.EqualTo(PhaseValidationError.LifecycleSnapshotMismatch)
            );
            Assert.That(givenUp.PhaseGivenUp, Is.True);
            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(1));
            Assert.That(
                duplicate.Error,
                Is.EqualTo(PhaseValidationError.CompletedPhaseLocked)
            );

            harness.AdvanceGivenUpPhase();
            ValidationResult nextWithoutReplay =
                harness.Session.GiveUpCurrentPhase(
                    harness.Lifecycle.CurrentPhase
                );

            Assert.That(harness.Session.CurrentPhaseId, Is.EqualTo(2));
            Assert.That(
                nextWithoutReplay.Error,
                Is.EqualTo(PhaseValidationError.GiveUpUnavailable)
            );
        }

        [Test]
        public void PhaseFourGiveUpRequestsDeterministicChestOpenAndPlannedKey()
        {
            var harness = new SessionHarness(
                CreatePlan("001", "004", "013", "018", "019", "026")
            );
            AdvanceToPhase(harness, 4);
            PhaseExecutionSnapshot available = harness.MakeGiveUpAvailable();

            ValidationResult result = harness.Session.GiveUpCurrentPhase(
                available
            );

            Assert.That(result.PhaseGivenUp, Is.True);
            Assert.That(result.PhaseCompleted, Is.False);
            Assert.That(result.FeedbackCue, Is.EqualTo(
                PhaseFeedbackCue.ChestOpened
            ));
            Assert.That(result.ReleasedTargetId, Is.EqualTo("motorbike_key"));
        }

        [Test]
        public void AbortAndNewRunClearOnlyLocalTaskState()
        {
            var session = new InteractionPhaseSession();
            InteractionRunStateMachine firstLifecycle = CoreTestData.StartRunning();
            session.Configure(CreatePlan());
            session.Enable();
            session.Synchronize(firstLifecycle.CurrentPhase);
            session.AcceptInput(1, PhaseInput.Target("box_stool"));
            session.AcceptInput(1, PhaseInput.Digit(7));

            session.Abort();

            Assert.That(session.Plan, Is.Null);
            Assert.That(session.CurrentPhaseId, Is.Null);
            Assert.That(session.IsEnabled, Is.False);
            Assert.That(
                firstLifecycle.State,
                Is.EqualTo(RunState.Running),
                "W7 Abort must not mutate or replace W1 lifecycle authority."
            );

            RunPlan newPlan = CreatePlan(
                "003", "004", "013", "016", "019", "026"
            );
            InteractionRunStateMachine secondLifecycle =
                CoreTestData.StartRunning();
            session.Configure(newPlan);
            session.Enable();
            Assert.That(session.CurrentPhaseId, Is.Null);
            session.Synchronize(secondLifecycle.CurrentPhase);

            ValidationResult staleDigit = session.AcceptInput(
                1,
                PhaseInput.Digit(1)
            );
            ValidationResult newBox = session.AcceptInput(
                1,
                PhaseInput.Target("box_floor_b")
            );

            Assert.That(staleDigit.ProgressReset, Is.False);
            Assert.That(newBox.Accepted, Is.True);
            Assert.That(newBox.Progress, Is.EqualTo(1));
        }

        [Test]
        public void DisableRejectsInputAndEnableAllowsSingleBoxTouch()
        {
            var harness = new SessionHarness(CreatePlan());
            harness.Session.Disable();

            ValidationResult disabled = harness.Session.AcceptInput(
                1,
                PhaseInput.Target("box_stool")
            );
            harness.Session.Enable();
            ValidationResult resumed = harness.Session.AcceptInput(
                1,
                PhaseInput.Target("box_stool")
            );

            Assert.That(
                disabled.Error,
                Is.EqualTo(PhaseValidationError.InteractionsDisabled)
            );
            Assert.That(disabled.ProgressReset, Is.False);
            Assert.That(resumed.Accepted, Is.True);
            Assert.That(resumed.Progress, Is.EqualTo(1));
            Assert.That(resumed.PhaseCompleted, Is.True);
        }

        [Test]
        public void ResultsPreserveOriginalInputMetadataIncludingGateFailure()
        {
            var session = new InteractionPhaseSession();
            RunPlan plan = CreatePlan();
            InteractionRunStateMachine lifecycle = CoreTestData.StartRunning();
            var published = new List<ValidationResult>();
            session.ResultProduced += published.Add;
            session.Configure(plan);
            session.Enable();

            ValidationResult gateFailure = session.AcceptInput(
                2,
                PhaseInput.Pair("coin_dragon", "plate_dragon")
            );

            Assert.That(
                gateFailure.Error,
                Is.EqualTo(PhaseValidationError.LifecycleSnapshotRequired)
            );
            Assert.That(
                gateFailure.TargetId,
                Is.Null,
                "Feedback target and attempted input must remain distinct."
            );
            Assert.That(gateFailure.InputKind, Is.EqualTo(PhaseInputKind.Pair));
            Assert.That(gateFailure.InputTargetId, Is.EqualTo("coin_dragon"));
            Assert.That(
                gateFailure.InputSecondaryTargetId,
                Is.EqualTo("plate_dragon")
            );
            Assert.That(gateFailure.InputDigitValue, Is.Null);
            Assert.That(published, Is.EqualTo(new[] { gateFailure }));

            session.Synchronize(lifecycle.CurrentPhase);
            ValidationResult digit = session.AcceptInput(
                1,
                PhaseInput.Digit(7)
            );
            ValidationResult backspace = session.AcceptInput(
                1,
                PhaseInput.Backspace()
            );
            ValidationResult submit = session.AcceptInput(
                1,
                PhaseInput.Submit()
            );
            ValidationResult target = session.AcceptInput(
                1,
                PhaseInput.Target("box_stool")
            );
            ValidationResult giveUp = session.GiveUpCurrentPhase(
                lifecycle.CurrentPhase
            );

            Assert.That(target.InputKind, Is.EqualTo(PhaseInputKind.Target));
            Assert.That(target.InputTargetId, Is.EqualTo("box_stool"));
            Assert.That(target.InputSecondaryTargetId, Is.Null);
            Assert.That(target.InputDigitValue, Is.Null);
            Assert.That(digit.InputKind, Is.EqualTo(PhaseInputKind.Digit));
            Assert.That(digit.InputDigitValue, Is.EqualTo(7));
            Assert.That(backspace.InputKind, Is.EqualTo(
                PhaseInputKind.Backspace
            ));
            Assert.That(submit.InputKind, Is.EqualTo(PhaseInputKind.Submit));
            Assert.That(
                giveUp.Error,
                Is.EqualTo(PhaseValidationError.CompletedPhaseLocked)
            );
            Assert.That(giveUp.InputKind, Is.Null);
            Assert.That(giveUp.InputTargetId, Is.Null);
            Assert.That(giveUp.InputSecondaryTargetId, Is.Null);
            Assert.That(giveUp.InputDigitValue, Is.Null);
            Assert.That(published.Count, Is.EqualTo(6));
            Assert.That(published[5], Is.SameAs(giveUp));
        }

        [Test]
        public void PresentationSnapshotRetainsAuthoritativeVisualState()
        {
            var hintHarness = new SessionHarness(CreatePlan(
                "001", "004", "013", "016", "025", "026"
            ));
            FinishPhaseOneTask(hintHarness.Session);
            Assert.That(
                hintHarness.Session.PresentationSnapshot.SafeDoorOpened,
                Is.True
            );

            var chestHintHarness = new SessionHarness(CreatePlan(
                "001", "004", "013", "016", "025", "026"
            ));
            AdvanceToPhase(chestHintHarness, 3);
            Assert.That(
                chestHintHarness.Session.PresentationSnapshot.ChestOrderVisible,
                Is.False
            );
            FinishPhaseThreeTask(chestHintHarness.Session);
            Assert.That(
                chestHintHarness.Session.PresentationSnapshot.ChestOrderVisible,
                Is.True
            );
            chestHintHarness.AdvanceCompletedPhase();
            FinishPhaseFourTask(chestHintHarness.Session);
            Assert.That(
                chestHintHarness.Session.PresentationSnapshot.ChestOrderVisible,
                Is.False
            );

            var givenUpHintHarness = new SessionHarness(CreatePlan(
                "001", "004", "013", "016", "025", "026"
            ));
            AdvanceToPhase(givenUpHintHarness, 3);
            PhaseExecutionSnapshot phaseThreeGiveUp =
                givenUpHintHarness.MakeGiveUpAvailable();
            givenUpHintHarness.Session.GiveUpCurrentPhase(phaseThreeGiveUp);
            Assert.That(
                givenUpHintHarness.Session.PresentationSnapshot
                    .ChestOrderVisible,
                Is.True
            );
            givenUpHintHarness.AdvanceGivenUpPhase();
            PhaseExecutionSnapshot phaseFourGiveUp =
                givenUpHintHarness.MakeGiveUpAvailable();
            givenUpHintHarness.Session.GiveUpCurrentPhase(phaseFourGiveUp);
            Assert.That(
                givenUpHintHarness.Session.PresentationSnapshot
                    .ChestOrderVisible,
                Is.False
            );
            givenUpHintHarness.Session.Abort();
            Assert.That(
                givenUpHintHarness.Session.PresentationSnapshot
                    .ChestOrderVisible,
                Is.False
            );

            var harness = new SessionHarness(CreatePlan(
                "001", "004", "013", "016", "025", "026"
            ));
            AdvanceToPhase(harness, 4);

            harness.Session.AcceptInput(4, PhaseInput.Target("blue"));
            InteractionTaskPresentationSnapshot partial =
                harness.Session.PresentationSnapshot;

            Assert.That(partial.ChestOpened, Is.False);
            Assert.That(partial.ChestButtonTargetIds, Is.Empty);

            FinishPhaseFourTask(harness.Session);
            harness.AdvanceCompletedPhase();
            harness.Session.AcceptInput(5, PhaseInput.Target("button_a"));
            harness.Session.AcceptInput(5, PhaseInput.Target("button_b"));

            InteractionTaskPresentationSnapshot completed =
                harness.Session.PresentationSnapshot;

            Assert.That(completed.SafeDoorOpened, Is.True);
            Assert.That(completed.ChestOpened, Is.True);
            Assert.That(completed.ReleasedKeyTargetId, Is.EqualTo("key_a"));
            Assert.That(
                completed.ChestButtonTargetIds,
                Is.Empty
            );
            Assert.That(completed.CabinetUnlocked, Is.True);
            Assert.That(
                completed.CabinetButtonTargetIds,
                Is.EquivalentTo(new[] { "button_a", "button_b" })
            );

            harness.Session.AcceptInput(5, PhaseInput.Target("button_b"));
            InteractionTaskPresentationSnapshot reset =
                harness.Session.PresentationSnapshot;

            Assert.That(reset.CabinetUnlocked, Is.True);
            Assert.That(reset.CabinetButtonTargetIds, Is.Empty);
        }

        private static void AssertPhaseFiveButtonReset(
            string sentenceId,
            string initiallyCorrect,
            string invalid,
            IReadOnlyList<string> requiredAfterReset)
        {
            var harness = new SessionHarness(CreatePlan(
                "001", "004", "013", "016", sentenceId, "026"
            ));
            AdvanceToPhase(harness, 5);
            harness.Session.AcceptInput(5, PhaseInput.Target(initiallyCorrect));

            ValidationResult wrong = harness.Session.AcceptInput(
                5,
                PhaseInput.Target(invalid)
            );

            Assert.That(wrong.Accepted, Is.False);
            Assert.That(wrong.InteractionError, Is.True);
            Assert.That(wrong.ProgressReset, Is.True);
            Assert.That(wrong.Progress, Is.Zero);

            ValidationResult result = null;
            for (int index = 0; index < requiredAfterReset.Count; index++)
            {
                result = harness.Session.AcceptInput(
                    5,
                    PhaseInput.Target(requiredAfterReset[index])
                );
                Assert.That(result.Accepted, Is.True);
                Assert.That(
                    result.PhaseCompleted,
                    Is.EqualTo(index == requiredAfterReset.Count - 1)
                );
            }
            Assert.That(result.PhaseCompleted, Is.True);
        }

        private static void AdvanceToPhase(SessionHarness harness, int phaseId)
        {
            while (harness.Session.CurrentPhaseId < phaseId)
            {
                switch (harness.Session.CurrentPhaseId)
                {
                    case 1:
                        CompletePhaseOne(harness);
                        break;
                    case 2:
                        CompletePhaseTwo(harness);
                        break;
                    case 3:
                        FinishPhaseThreeTask(harness.Session);
                        harness.AdvanceCompletedPhase();
                        break;
                    case 4:
                        FinishPhaseFourTask(harness.Session);
                        harness.AdvanceCompletedPhase();
                        break;
                    case 5:
                        FinishPhaseFiveTask(harness.Session);
                        harness.AdvanceCompletedPhase();
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Test harness cannot advance from this phase."
                        );
                }
            }
        }

        private static void CompletePhaseOne(SessionHarness harness)
        {
            FinishPhaseOneTask(harness.Session);
            harness.AdvanceCompletedPhase();
        }

        private static void CompletePhaseTwo(SessionHarness harness)
        {
            IReadOnlyList<string> targets = harness.Session.Plan.Phases[1]
                .TaskVariant.TargetIds;
            ValidationResult completed = harness.Session.AcceptInput(
                2,
                PhaseInput.Pair(targets[0], targets[1])
            );
            Assert.That(completed.PhaseCompleted, Is.True);
            harness.AdvanceCompletedPhase();
        }

        private static ValidationResult FinishPhaseOneTask(
            InteractionPhaseSession session)
        {
            string box = session.Plan.Phases[0].TaskVariant.TargetIds[0];
            return session.AcceptInput(1, PhaseInput.Target(box));
        }

        private static ValidationResult FinishPhaseThreeTask(
            InteractionPhaseSession session)
        {
            string frame = session.Plan.Phases[2].TaskVariant.TargetIds[0];
            return session.AcceptInput(3, PhaseInput.Target(frame));
        }

        private static ValidationResult FinishPhaseFourTask(
            InteractionPhaseSession session)
        {
            string key = session.Plan.Phases[3].TaskVariant.TargetIds[0];
            return session.AcceptInput(4, PhaseInput.Target(key));
        }

        private static ValidationResult FinishPhaseFiveTask(
            InteractionPhaseSession session)
        {
            ValidationResult result = null;
            foreach (string button in session.Plan.Phases[4]
                         .TaskVariant.TargetIds.Reverse())
            {
                result = session.AcceptInput(5, PhaseInput.Target(button));
            }
            return result;
        }

        private static ValidationResult FinishPhaseSixTask(
            InteractionPhaseSession session)
        {
            ValidationResult result = null;
            foreach (string breaker in session.Plan.Phases[5]
                         .TaskVariant.OrderedTargetIds)
            {
                result = session.AcceptInput(6, PhaseInput.Target(breaker));
            }
            return result;
        }

        private static InteractionRunStateMachine CreateLifecycleAtPhase(
            int phaseId)
        {
            InteractionRunStateMachine lifecycle = CoreTestData.StartRunning();
            for (int current = 1; current < phaseId; current++)
            {
                lifecycle.CompletePhase(TimeSpan.FromSeconds(current));
            }
            return lifecycle;
        }

        private static RunPlan ReplaceVariant(
            RunPlan original,
            int phaseId,
            TaskVariant replacement)
        {
            RunPhasePlan[] phases = original.Phases
                .Select(phase => phase.PhaseId == phaseId
                    ? new RunPhasePlan(phaseId, phase.Content, replacement)
                    : phase)
                .ToArray();
            return new RunPlan(
                original.BatchId,
                original.ParticipantId,
                original.RunId,
                original.AppSessionId,
                original.CreatedUtc,
                original.AppVersion,
                original.GitCommit,
                original.Seed,
                original.ConditionAssignment,
                original.SafePassword,
                original.ChestButtonOrder,
                phases
            );
        }

        private static RunPlan CreatePlan(params string[] sentenceIds)
        {
            string[] selected = sentenceIds == null || sentenceIds.Length == 0
                ? new[] { "001", "004", "013", "016", "019", "026" }
                : sentenceIds;
            if (selected.Length != PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentException(
                    "Tests require exactly six selected sentence IDs.",
                    nameof(sentenceIds)
                );
            }

            InstructionContentCatalog content = CoreTestData.CreateContentCatalog();
            var phases = new List<RunPhasePlan>(
                PhaseSentenceRanges.PhaseCount
            );
            for (int index = 0; index < selected.Length; index++)
            {
                string sentenceId = selected[index];
                phases.Add(new RunPhasePlan(
                    index + 1,
                    content.ForSentence(sentenceId),
                    TaskVariantCatalog.ForSentence(sentenceId)
                ));
            }

            return new RunPlan(
                "pilot-20260826",
                "P001",
                "run_phase_rules_test",
                "app_phase_rules_test",
                CoreTestData.FixedUtc,
                "1.0.0-test",
                "335befa",
                12345,
                new AssistanceAssignment(
                    AssistanceCondition.SignOnly,
                    0,
                    0
                ),
                new SafePassword(new[] { 7, 1, 4, 9 }),
                new ChestButtonOrder(
                    new[] { "blue", "red", "yellow", "green" }
                ),
                phases
            );
        }

        private sealed class SessionHarness
        {
            public SessionHarness(RunPlan plan)
            {
                Session = new InteractionPhaseSession();
                Lifecycle = CoreTestData.StartRunning();
                Session.Configure(plan);
                Session.Enable();
                Session.Synchronize(Lifecycle.CurrentPhase);
            }

            public InteractionPhaseSession Session { get; }

            public InteractionRunStateMachine Lifecycle { get; }

            public void AdvanceCompletedPhase()
            {
                int phaseId = Lifecycle.CurrentPhaseId.GetValueOrDefault();
                Lifecycle.CompletePhase(TimeSpan.FromSeconds(phaseId));
                if (Lifecycle.CurrentPhase != null)
                {
                    Session.Synchronize(Lifecycle.CurrentPhase);
                }
            }

            public PhaseExecutionSnapshot MakeGiveUpAvailable()
            {
                Lifecycle.FirstPlaybackCompleted();
                Lifecycle.ReplayInstruction();
                Lifecycle.ReplayPlaybackCompleted();
                Session.Synchronize(Lifecycle.CurrentPhase);
                return Lifecycle.CurrentPhase;
            }

            public void AdvanceGivenUpPhase()
            {
                int phaseId = Lifecycle.CurrentPhaseId.GetValueOrDefault();
                Lifecycle.GiveUpPhase(TimeSpan.FromSeconds(phaseId));
                if (Lifecycle.CurrentPhase != null)
                {
                    Session.Synchronize(Lifecycle.CurrentPhase);
                }
            }
        }
    }
}
