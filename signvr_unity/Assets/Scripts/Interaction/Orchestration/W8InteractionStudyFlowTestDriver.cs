#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.Orchestration
{
    public static class W8InteractionStudyFlowTestDriver
    {
        public static void SixPhaseHappyPathAdvancesOnlyOnActualFirstFrames()
        {
            var fixture = new FlowFixture();

            Assert.That(fixture.Flow.TryStart().Succeeded, Is.True);
            RunPlan frozenPlan = fixture.Run.Plan;
            Assert.That(frozenPlan, Is.Not.Null);
            Assert.That(fixture.Tasks.Plan, Is.SameAs(frozenPlan));

            for (int phaseId = 1;
                phaseId <= PhaseSentenceRanges.PhaseCount;
                phaseId++)
            {
                if (phaseId == 1)
                {
                    fixture.Run.PublishInitialPresentation();
                    Assert.That(fixture.Run.State, Is.EqualTo(RunState.Scheduled));
                }
                else
                {
                    Assert.That(
                        fixture.Run.CurrentPhase.PhaseId,
                        Is.EqualTo(phaseId - 1),
                        "The capture/lifecycle phase must remain old until the next real frame."
                    );
                    fixture.Run.PublishCheckpointedPresentation();
                    Assert.That(
                        fixture.Run.CurrentPhase.PhaseId,
                        Is.EqualTo(phaseId - 1)
                    );
                }

                Assert.That(fixture.Presentation.BeginPhaseCount, Is.EqualTo(phaseId));
                Assert.That(
                    fixture.Presentation.LastCondition,
                    Is.EqualTo(frozenPlan.AssistanceCondition)
                );

                fixture.Presentation.PublishFirstFrame(
                    InteractionPresentationPlaybackKind.First
                );
                Assert.That(fixture.Run.CurrentPhase.PhaseId, Is.EqualTo(phaseId));
                Assert.That(fixture.Tasks.CurrentPhaseId, Is.EqualTo(phaseId));

                ValidationResult completed = CompleteCurrentTask(
                    fixture.Tasks,
                    frozenPlan,
                    phaseId
                );
                Assert.That(completed.PhaseCompleted, Is.True);
                Assert.That(
                    fixture.Flow.Snapshot.Progress,
                    Is.EqualTo(completed.Progress),
                    "Displayed progress must be the W7 ValidationResult value."
                );
                Assert.That(fixture.Run.Plan, Is.SameAs(frozenPlan));

                if (phaseId < PhaseSentenceRanges.PhaseCount)
                {
                    Assert.That(fixture.Run.HasCheckpointedPresentation, Is.True);
                    Assert.That(fixture.Run.State, Is.EqualTo(RunState.Running));
                }
            }

            Assert.That(fixture.Run.State, Is.EqualTo(RunState.Completing));
            Assert.That(fixture.Presentation.EndPhaseCount, Is.EqualTo(6));
            Assert.That(fixture.Run.RecordedResults.Count, Is.GreaterThan(6));

            fixture.Run.MarkCompleted();
            fixture.Flow.Tick();

            Assert.That(fixture.Run.State, Is.EqualTo(RunState.PreStart));
            Assert.That(fixture.Tasks.ResetCount, Is.EqualTo(1));
            Assert.That(fixture.Tasks.AbortCount, Is.Zero);
            Assert.That(fixture.Run.StartCount, Is.EqualTo(1));
        }

        public static void ReplayUsesOneW6TokenAndCannotStartTwice()
        {
            var fixture = StartFirstPhase();
            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.First
            );

            Assert.That(fixture.Run.CurrentPhase.ReplayAvailable, Is.True);
            Assert.That(fixture.Flow.Snapshot.CanReplay, Is.True);
            Assert.That(fixture.Flow.TryReplay().Succeeded, Is.True);
            Assert.That(fixture.Presentation.BeginReplayCount, Is.EqualTo(1));
            Assert.That(fixture.Run.CurrentPhase.State, Is.EqualTo(PhaseState.Active));

            fixture.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.Replay
            );
            Assert.That(
                fixture.Run.CurrentPhase.State,
                Is.EqualTo(PhaseState.ReplayPlayback)
            );
            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.Replay
            );

            Assert.That(fixture.Run.CurrentPhase.State, Is.EqualTo(PhaseState.Active));
            Assert.That(fixture.Run.CurrentPhase.ReplayUsed, Is.True);
            Assert.That(fixture.Flow.TryReplay().Succeeded, Is.False);
            Assert.That(fixture.Presentation.BeginReplayCount, Is.EqualTo(1));
            Assert.That(fixture.Run.ReplayRequestCount, Is.EqualTo(1));
        }

        public static void GiveUpRequiresCompletedReplayAndRetainsStuckResult()
        {
            var fixture = StartFirstPhase();
            Assert.That(fixture.Flow.TryGiveUp().Succeeded, Is.False);

            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.First
            );
            Assert.That(fixture.Flow.TryReplay().Succeeded, Is.True);
            fixture.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.Replay
            );
            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.Replay
            );

            Assert.That(fixture.Flow.TryGiveUp().Succeeded, Is.True);
            Assert.That(fixture.Run.HasCheckpointedPresentation, Is.True);
            Assert.That(fixture.Run.CurrentPhase.PhaseId, Is.EqualTo(1));
            Assert.That(fixture.Run.PhaseSnapshots[0].Result, Is.Null);

            fixture.Run.PublishCheckpointedPresentation();
            fixture.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.First
            );

            Assert.That(fixture.Run.CurrentPhase.PhaseId, Is.EqualTo(2));
            Assert.That(
                fixture.Run.PhaseSnapshots[0].Result,
                Is.EqualTo(PhaseResult.Stuck)
            );
            Assert.That(fixture.Tasks.CurrentPhaseId, Is.EqualTo(2));
        }

        public static void WrongInputResetsW7ProgressAndSnapshotNeverGuessesProgress()
        {
            var fixture = StartFirstPhase();
            RunPlan plan = fixture.Run.Plan;

            ValidationResult accepted = fixture.Tasks.AcceptInput(
                1,
                PhaseInput.Target(plan.Phases[0].TaskVariant.TargetIds[0])
            );
            Assert.That(accepted.Progress, Is.EqualTo(1));
            Assert.That(fixture.Flow.Snapshot.Progress, Is.EqualTo(1));

            ValidationResult wrong = fixture.Tasks.AcceptInput(
                1,
                PhaseInput.Submit()
            );
            Assert.That(wrong.InteractionError, Is.True);
            Assert.That(wrong.ProgressReset, Is.True);
            Assert.That(wrong.Progress, Is.Zero);
            Assert.That(fixture.Flow.Snapshot.Progress, Is.Zero);
            Assert.That(fixture.Tasks.LastResult, Is.SameAs(wrong));
            Assert.That(fixture.Run.RecordedResults.Last(), Is.SameAs(wrong));
            Assert.That(fixture.Run.CurrentPhase.InteractionErrorCount, Is.EqualTo(1));
        }

        public static void ValidationErrorsResynchronizeBeforeReplayGiveUp()
        {
            var reset = StartFirstPhase();
            ValidationResult resetError = reset.Tasks.AcceptInput(
                1,
                PhaseInput.Submit()
            );
            Assert.That(resetError.InteractionError, Is.True);
            Assert.That(resetError.ProgressReset, Is.True);
            Assert.That(
                reset.Run.CurrentPhase.InteractionErrorCount,
                Is.EqualTo(1)
            );
            CompleteReplayThenGiveUp(reset);
            Assert.That(
                reset.Run.HasCheckpointedPresentation,
                Is.True,
                "A reset error must not leave W7 on a stale W1 snapshot."
            );

            var nonReset = StartFirstPhase();
            CompleteCurrentTask(nonReset.Tasks, nonReset.Run.Plan, 1);
            nonReset.Run.PublishCheckpointedPresentation();
            nonReset.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.First
            );
            ValidationResult nonResetError = nonReset.Tasks.AcceptInput(
                2,
                PhaseInput.Pair("not_the_coin", "not_the_plate")
            );
            Assert.That(nonResetError.InteractionError, Is.True);
            Assert.That(nonResetError.ProgressReset, Is.False);
            Assert.That(nonReset.Flow.Snapshot.Progress, Is.Zero);
            Assert.That(
                nonReset.Run.CurrentPhase.InteractionErrorCount,
                Is.EqualTo(1)
            );
            CompleteReplayThenGiveUp(nonReset);
            Assert.That(nonReset.Run.HasCheckpointedPresentation, Is.True);
        }

        public static void AbortEndsW5BeforeW6AndWaitsForTerminalBeforeW7Reset()
        {
            var log = new List<string>();
            var fixture = StartFirstPhase(log);

            Assert.That(
                fixture.Flow.TryAbort("participant_requested_abort").Succeeded,
                Is.True
            );
            Assert.That(fixture.Run.State, Is.EqualTo(RunState.Aborting));
            Assert.That(log.IndexOf("w5.end"), Is.LessThan(log.IndexOf("w6.abort")));
            Assert.That(fixture.Tasks.AbortCount, Is.Zero);
            Assert.That(fixture.Tasks.Plan, Is.Not.Null);

            fixture.Flow.Tick();
            Assert.That(fixture.Tasks.AbortCount, Is.Zero);

            fixture.Run.MarkAborted();
            fixture.Flow.Tick();

            Assert.That(fixture.Tasks.AbortCount, Is.EqualTo(1));
            Assert.That(fixture.Run.State, Is.EqualTo(RunState.PreStart));
            Assert.That(log.IndexOf("w7.abort"), Is.LessThan(log.IndexOf("w6.reset")));
        }

        public static void TerminalAdapterFailuresStillConvergeToPreStart()
        {
            var fixture = StartFirstPhase();
            fixture.Presentation.ThrowOnEndPhase = true;
            fixture.Tasks.ThrowOnAbort = true;

            Assert.That(
                fixture.Flow.TryAbort("participant_requested_abort").Succeeded,
                Is.True
            );
            fixture.Run.MarkAborted();

            fixture.Flow.Tick();

            Assert.That(fixture.Run.State, Is.EqualTo(RunState.PreStart));
            Assert.That(fixture.Tasks.AbortCount, Is.EqualTo(1));
            Assert.That(
                fixture.Flow.Snapshot.Status,
                Does.Contain("terminal cleanup warning")
            );
        }

        public static void SuspendUsesTheSameSafeAbortAndResumeResubscribesOnce()
        {
            var fixture = StartFirstPhase();
            Assert.That(fixture.Run.SubscriberCount, Is.EqualTo(1));
            Assert.That(fixture.Tasks.SubscriberCount, Is.EqualTo(1));

            fixture.Flow.Suspend("component_disabled");

            Assert.That(fixture.Run.State, Is.EqualTo(RunState.Aborting));
            Assert.That(fixture.Run.SubscriberCount, Is.Zero);
            Assert.That(fixture.Tasks.SubscriberCount, Is.Zero);
            Assert.That(fixture.Presentation.SubscriberCount, Is.Zero);

            fixture.Run.MarkAborted();
            fixture.Flow.Resume();
            fixture.Flow.Tick();

            Assert.That(fixture.Run.State, Is.EqualTo(RunState.PreStart));
            Assert.That(fixture.Run.SubscriberCount, Is.EqualTo(1));
            Assert.That(fixture.Tasks.SubscriberCount, Is.EqualTo(1));
            Assert.That(fixture.Presentation.SubscriberCount, Is.EqualTo(1));
        }

        public static void DuplicateAndStalePresentationCallbacksAreExactlyOnce()
        {
            var fixture = new FlowFixture();
            Assert.That(fixture.Flow.TryStart().Succeeded, Is.True);

            InteractionStudyPresentationRequest request =
                fixture.Run.PublishInitialPresentation();
            fixture.Run.PublishAgain(request);
            Assert.That(fixture.Presentation.BeginPhaseCount, Is.EqualTo(1));

            fixture.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.First
            );
            fixture.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.First
            );
            Assert.That(fixture.Run.AcknowledgeCount, Is.EqualTo(1));

            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.First
            );
            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.First
            );
            Assert.That(fixture.Run.PlaybackCompletionCount, Is.EqualTo(1));
        }

        public static void ReconfigureDetachesOldPublishersAndRejectsActiveReplacement()
        {
            var fixtureA = new FlowFixture();
            var fixtureB = new FlowFixture(createFlow: false);
            Action<InteractionStudyPresentationRequest> stale =
                fixtureA.Run.LastSubscribedCallback;

            fixtureA.Flow.Reconfigure(
                fixtureB.Run,
                fixtureB.Presentation,
                fixtureB.Tasks
            );

            Assert.That(fixtureA.Run.SubscriberCount, Is.Zero);
            Assert.That(fixtureB.Run.SubscriberCount, Is.EqualTo(1));
            stale(new InteractionStudyPresentationRequest(
                1,
                "run_stale",
                1,
                InteractionPresentationPlaybackKind.First
            ));
            Assert.That(fixtureB.Presentation.BeginPhaseCount, Is.Zero);

            Assert.That(fixtureA.Flow.TryStart().Succeeded, Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                fixtureA.Flow.Reconfigure(
                    fixtureA.Run,
                    fixtureA.Presentation,
                    fixtureA.Tasks
                )
            );
            Assert.That(fixtureB.Run.SubscriberCount, Is.EqualTo(1));

            var transactional = new FlowFixture();
            var brokenReplacement = new FlowFixture(createFlow: false);
            brokenReplacement.Presentation.ThrowOnSubscribe = true;
            Assert.Throws<InvalidOperationException>(() =>
                transactional.Flow.Reconfigure(
                    brokenReplacement.Run,
                    brokenReplacement.Presentation,
                    brokenReplacement.Tasks
                )
            );
            Assert.That(transactional.Run.SubscriberCount, Is.EqualTo(1));
            Assert.That(
                transactional.Presentation.SubscriberCount,
                Is.EqualTo(1)
            );
            Assert.That(transactional.Tasks.SubscriberCount, Is.EqualTo(1));
            Assert.That(brokenReplacement.Run.SubscriberCount, Is.Zero);
            Assert.That(
                brokenReplacement.Presentation.SubscriberCount,
                Is.Zero
            );
            Assert.That(brokenReplacement.Tasks.SubscriberCount, Is.Zero);
            Assert.That(transactional.Flow.TryStart().Succeeded, Is.True);
            transactional.Run.PublishInitialPresentation();
            Assert.That(
                transactional.Presentation.BeginPhaseCount,
                Is.EqualTo(1)
            );
        }

        public static void ControllerReconfigureFailurePreservesDependenciesAndSubscriptions()
        {
            GameObject owner = new GameObject(
                "W8 Transactional Controller Driver"
            );
            owner.SetActive(false);
            try
            {
                GameObject original = new GameObject("Original Publishers");
                original.transform.SetParent(owner.transform, false);
                GameObject replacement = new GameObject(
                    "Replacement Publishers"
                );
                replacement.transform.SetParent(owner.transform, false);

                var originalRun =
                    original.AddComponent<InteractionRunController>();
                var originalPresentation = original.AddComponent<
                    InstructionPresentationController>();
                var originalTasks = original.AddComponent<
                    SignVR.Interaction.PhaseAdapters
                        .InteractionPhaseCoordinator>();
                var originalCapture = original.AddComponent<
                    InteractionStudyCaptureBinding>();
                var replacementRun =
                    replacement.AddComponent<InteractionRunController>();
                var replacementPresentation = replacement.AddComponent<
                    InstructionPresentationController>();
                var replacementTasks = replacement.AddComponent<
                    SignVR.Interaction.PhaseAdapters
                        .InteractionPhaseCoordinator>();
                var replacementCapture = replacement.AddComponent<
                    InteractionStudyCaptureBinding>();
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                controller.Configure(
                    originalRun,
                    originalPresentation,
                    originalTasks,
                    originalCapture
                );

                var authoritative = new FlowFixture();
                Assert.That(
                    authoritative.Flow.TryStart().Succeeded,
                    Is.True
                );
                typeof(InteractionStudyFlowController).GetField(
                    "flow",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).SetValue(controller, authoritative.Flow);
                var readiness = (InteractionStudyReadinessPollGate)
                    typeof(InteractionStudyFlowController).GetField(
                        "readinessPollGate",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    ).GetValue(controller);
                Assert.That(
                    readiness.TryBegin(0d, out long readinessToken),
                    Is.True
                );

                Assert.Throws<InvalidOperationException>(() =>
                    controller.Configure(
                        replacementRun,
                        replacementPresentation,
                        replacementTasks,
                        replacementCapture
                    )
                );
                Assert.That(
                    controller.RunController,
                    Is.SameAs(originalRun)
                );
                Assert.That(
                    controller.PresentationController,
                    Is.SameAs(originalPresentation)
                );
                Assert.That(
                    controller.PhaseCoordinator,
                    Is.SameAs(originalTasks)
                );
                Assert.That(
                    controller.CaptureBinding,
                    Is.SameAs(originalCapture)
                );
                Assert.That(authoritative.Run.SubscriberCount, Is.EqualTo(1));
                Assert.That(
                    authoritative.Presentation.SubscriberCount,
                    Is.EqualTo(1)
                );
                Assert.That(
                    authoritative.Tasks.SubscriberCount,
                    Is.EqualTo(1)
                );
                Assert.That(controller.ReadinessRefreshInFlight, Is.True);
                Assert.That(
                    readiness.TryComplete(readinessToken, 1d),
                    Is.True
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void SlowReadinessResponseCannotBeStaledByPolling()
        {
            var gate = new InteractionStudyReadinessPollGate(2d);
            Assert.That(gate.TryBegin(0d, out long first), Is.True);
            Assert.That(gate.InFlight, Is.True);
            Assert.That(gate.TryBegin(2d, out _), Is.False);
            Assert.That(gate.TryBegin(20d, out _), Is.False);

            Assert.That(gate.TryComplete(first, 20d), Is.True);
            Assert.That(gate.InFlight, Is.False);
            Assert.That(gate.TryBegin(21.999d, out _), Is.False);
            Assert.That(gate.TryBegin(22d, out long second), Is.True);

            gate.Invalidate();
            Assert.That(gate.InFlight, Is.False);
            Assert.That(gate.TryComplete(second, 23d), Is.False);
            Assert.That(gate.TryBegin(23d, out _), Is.True);
        }

        public static void StartAndInitializationFailuresDoNotInventAnotherRunPlan()
        {
            var rejected = new FlowFixture();
            rejected.Run.StartFailure = "host_not_ready";

            InteractionStudyFlowCommandResult result = rejected.Flow.TryStart();
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("host_not_ready"));
            Assert.That(rejected.Run.StartCount, Is.Zero);
            Assert.That(rejected.Run.Plan, Is.Null);

            var consumed = new FlowFixture();
            Assert.That(consumed.Flow.TryStart().Succeeded, Is.True);
            RunPlan frozen = consumed.Run.Plan;
            consumed.Run.Fault("manifest_or_capture_initialization_failed");
            consumed.Flow.Tick();

            Assert.That(consumed.Run.StartCount, Is.EqualTo(1));
            Assert.That(consumed.Run.LastTerminalPlan, Is.SameAs(frozen));
            Assert.That(consumed.Tasks.AbortCount, Is.EqualTo(1));
            Assert.That(consumed.Run.State, Is.EqualTo(RunState.PreStart));
        }

        public static void PresentationFaultUsesAbortPathWithoutAcknowledgingARequest()
        {
            var fixture = new FlowFixture();
            Assert.That(fixture.Flow.TryStart().Succeeded, Is.True);
            fixture.Run.PublishInitialPresentation();

            fixture.Presentation.PublishFault("pose_sha_mismatch");

            Assert.That(fixture.Run.AcknowledgeCount, Is.Zero);
            Assert.That(fixture.Run.State, Is.EqualTo(RunState.Aborting));
            Assert.That(fixture.Presentation.EndPhaseCount, Is.EqualTo(1));
        }

        public static void SuccessfulTaskMayEndDuringFirstOrReplayPlayback()
        {
            var first = StartFirstPhase();
            CompleteCurrentTask(first.Tasks, first.Run.Plan, 1);
            Assert.That(first.Run.HasCheckpointedPresentation, Is.True);
            Assert.That(first.Run.CurrentPhase.State, Is.EqualTo(PhaseState.FirstPlayback));

            var replay = StartFirstPhase();
            replay.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.First
            );
            Assert.That(replay.Flow.TryReplay().Succeeded, Is.True);
            replay.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.Replay
            );
            CompleteCurrentTask(replay.Tasks, replay.Run.Plan, 1);
            Assert.That(replay.Run.HasCheckpointedPresentation, Is.True);
            Assert.That(
                replay.Run.CurrentPhase.State,
                Is.EqualTo(PhaseState.ReplayPlayback)
            );
        }

        public static void IdentityIsPreStartArmedAndLocksAfterConsumption()
        {
            GameObject owner = new GameObject("W8 Identity Driver");
            owner.SetActive(false);
            try
            {
                var run = owner.AddComponent<InteractionRunController>();
                var presentation =
                    owner.AddComponent<InstructionPresentationController>();
                var tasks = owner.AddComponent<
                    SignVR.Interaction.PhaseAdapters
                        .InteractionPhaseCoordinator>();
                var capture =
                    owner.AddComponent<InteractionStudyCaptureBinding>();
                var controller =
                    owner.AddComponent<InteractionStudyFlowController>();
                controller.Configure(run, presentation, tasks, capture);

                Assert.That(controller.IdentityArmed, Is.False);
                Assert.That(
                    controller.TryConfigureIdentity(
                        "P004",
                        "abcdef0123456789"
                    ).Succeeded,
                    Is.True
                );
                Assert.That(controller.IdentityArmed, Is.True);
                Assert.That(run.ParticipantId, Is.EqualTo("P004"));
                Assert.That(run.GitCommit, Is.EqualTo("abcdef0123456789"));

                var machine = new InteractionRunStateMachine(
                    new AssistanceBlockAllocator(71),
                    new RunPlanGenerator(
                        () => Guid.Parse(
                            "99999999-8888-7777-6666-555555555555"
                        ),
                        () => new DateTimeOffset(
                            2026,
                            8,
                            26,
                            12,
                            0,
                            0,
                            TimeSpan.Zero
                        )
                    )
                );
                machine.Start(new RunPlanGenerationRequest(
                    "pilot-20260826",
                    "P004",
                    "app_w8_identity",
                    "1.0.0",
                    "abcdef0123456789",
                    314159,
                    CreateCatalog()
                ));
                typeof(InteractionRunController).GetField(
                    "stateMachine",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic
                ).SetValue(run, machine);

                InteractionStudyFlowCommandResult rejected =
                    controller.TryConfigureIdentity(
                        "P005",
                        "fedcba9876543210"
                    );
                Assert.That(rejected.Succeeded, Is.False);
                Assert.That(run.ParticipantId, Is.EqualTo("P004"));
                Assert.That(controller.IdentityArmed, Is.True);

                Assert.Throws<ArgumentException>(() =>
                    InteractionStudyIdentityPolicy.Validate(
                        "UNCONFIGURED",
                        "abcdef0123456789",
                        out _,
                        out _
                    )
                );
                Assert.Throws<ArgumentException>(() =>
                    InteractionStudyIdentityPolicy.Validate(
                        "P005",
                        "unintegrated",
                        out _,
                        out _
                    )
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void ArmedIdentityInputsCannotDivergeFromW6Identity()
        {
            GameObject root = new GameObject("W8 Identity Controls Driver");
            GameObject controllerOwner = new GameObject("Controller");
            controllerOwner.transform.SetParent(root.transform, false);
            controllerOwner.SetActive(false);
            try
            {
                var run =
                    controllerOwner.AddComponent<InteractionRunController>();
                var presentation = controllerOwner.AddComponent<
                    InstructionPresentationController>();
                var tasks = controllerOwner.AddComponent<
                    SignVR.Interaction.PhaseAdapters
                        .InteractionPhaseCoordinator>();
                var capture = controllerOwner.AddComponent<
                    InteractionStudyCaptureBinding>();
                var controller = controllerOwner.AddComponent<
                    InteractionStudyFlowController>();
                controller.Configure(run, presentation, tasks, capture);
                var authoritative = new FlowFixture();
                typeof(InteractionStudyFlowController).GetField(
                    "flow",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).SetValue(controller, authoritative.Flow);
                typeof(InteractionStudyFlowController).GetField(
                    "manifestReady",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).SetValue(controller, true);

                Assert.That(controller.TryConfigureIdentity(
                    "P004",
                    "abcdef0123456789"
                ).Succeeded, Is.True);

                GameObject ui = new GameObject("UI");
                ui.transform.SetParent(root.transform, false);
                var instruction =
                    ui.AddComponent<InteractionInstructionControls>();
                instruction.Configure(presentation);
                instruction.ConfigureCommandRouting(true);
                var controls =
                    ui.AddComponent<InteractionStudyFlowControls>();
                GameObject surface = new GameObject("Start Surface");
                surface.transform.SetParent(ui.transform, false);
                Button start = NewUiComponent<Button>(ui.transform, "Start");
                TMP_InputField participant = NewUiComponent<TMP_InputField>(
                    ui.transform,
                    "Participant"
                );
                TMP_InputField build = NewUiComponent<TMP_InputField>(
                    ui.transform,
                    "Build"
                );
                Button apply = NewUiComponent<Button>(ui.transform, "Apply");
                TextMeshProUGUI status = NewUiComponent<TextMeshProUGUI>(
                    ui.transform,
                    "Status"
                );
                TextMeshProUGUI progress = NewUiComponent<TextMeshProUGUI>(
                    ui.transform,
                    "Progress"
                );
                participant.SetTextWithoutNotify("stale-draft");
                build.SetTextWithoutNotify("stale-build");
                controls.Configure(
                    controller,
                    instruction,
                    surface,
                    start,
                    participant,
                    build,
                    apply,
                    status,
                    progress
                );

                Assert.That(participant.text, Is.EqualTo("P004"));
                Assert.That(
                    build.text,
                    Is.EqualTo("abcdef0123456789")
                );
                Assert.That(participant.interactable, Is.False);
                Assert.That(build.interactable, Is.False);
                Assert.That(start.interactable, Is.True);
                Assert.That(apply.interactable, Is.True);

                apply.onClick.Invoke();
                Assert.That(controller.IdentityArmed, Is.False);
                Assert.That(participant.interactable, Is.True);
                Assert.That(build.interactable, Is.True);
                apply.onClick.Invoke();
                Assert.That(controller.IdentityArmed, Is.True);
                Assert.That(participant.interactable, Is.False);
                Assert.That(build.interactable, Is.False);

                participant.SetTextWithoutNotify("P005");
                participant.onValueChanged.Invoke("P005");
                Assert.That(controller.IdentityArmed, Is.False);
                Assert.That(start.interactable, Is.False);
                Assert.That(run.ParticipantId, Is.EqualTo("P004"));
                Assert.That(participant.text, Is.EqualTo("P004"));
                Assert.That(participant.interactable, Is.True);

                if (Application.isPlaying)
                {
                    ui.SetActive(false);
                }
                else
                {
                    typeof(InteractionStudyFlowControls).GetMethod(
                        "OnDisable",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    ).Invoke(controls, null);
                }
                Assert.That(instruction.HasLiveCommandSink, Is.False);
                if (Application.isPlaying)
                {
                    ui.SetActive(true);
                }
                else
                {
                    typeof(InteractionStudyFlowControls).GetMethod(
                        "OnEnable",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    ).Invoke(controls, null);
                }
                Assert.That(
                    instruction.OwnsCommandSink(controls),
                    Is.True
                );

                GameObject replacementOwner = new GameObject(
                    "Replacement W5 Controls"
                );
                replacementOwner.transform.SetParent(root.transform, false);
                var replacement = replacementOwner.AddComponent<
                    InteractionInstructionControls>();
                replacement.Configure(presentation);
                replacement.ConfigureCommandRouting(true);
                controls.Configure(
                    controller,
                    replacement,
                    surface,
                    start,
                    participant,
                    build,
                    apply,
                    status,
                    progress
                );
                Assert.That(instruction.HasLiveCommandSink, Is.False);
                Assert.That(
                    replacement.OwnsCommandSink(controls),
                    Is.True
                );
                UnityEngine.Object.DestroyImmediate(controls);
                Assert.That(replacement.HasLiveCommandSink, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void ManifestFailureRetryAndDisableCancelAreSafe()
        {
            GameObject owner = new GameObject("W8 Manifest Driver");
            owner.SetActive(false);
            try
            {
                var run = owner.AddComponent<InteractionRunController>();
                var presentation = owner.AddComponent<
                    InstructionPresentationController>();
                var tasks = owner.AddComponent<
                    SignVR.Interaction.PhaseAdapters
                        .InteractionPhaseCoordinator>();
                var capture = owner.AddComponent<
                    InteractionStudyCaptureBinding>();
                var controller = owner.AddComponent<
                    InteractionStudyFlowController>();
                controller.Configure(run, presentation, tasks, capture);

                Assert.That(controller.TryConfigureManifestBytes(
                    Encoding.UTF8.GetBytes("{not valid json"),
                    out string badManifest
                ), Is.False);
                Assert.That(
                    string.IsNullOrWhiteSpace(badManifest),
                    Is.False
                );
                Assert.That(controller.TryConfigureManifestBytes(
                    CreateManifestBytes(),
                    out string validManifestError
                ), Is.True);
                Assert.That(validManifestError, Is.Null);

                var loadGate = (InteractionStudyOperationGate)
                    typeof(InteractionStudyFlowController).GetField(
                        "manifestLoadGate",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    ).GetValue(controller);
                Assert.That(
                    loadGate.TryBegin(out long cancelledToken),
                    Is.True
                );
                typeof(InteractionStudyFlowController).GetField(
                    "manifestLoadToken",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).SetValue(controller, cancelledToken);
                Assert.That(controller.ManifestLoadInFlight, Is.True);

                var readinessGate = (InteractionStudyReadinessPollGate)
                    typeof(InteractionStudyFlowController).GetField(
                        "readinessPollGate",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    ).GetValue(controller);
                Assert.That(
                    readinessGate.TryBegin(0d, out long readinessToken),
                    Is.True
                );

                typeof(InteractionStudyFlowController).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).Invoke(controller, null);
                Assert.That(controller.ManifestLoadInFlight, Is.False);
                Assert.That(controller.ReadinessRefreshInFlight, Is.False);
                Assert.That(
                    readinessGate.TryComplete(readinessToken, 1d),
                    Is.False
                );
                Assert.That(
                    loadGate.TryComplete(cancelledToken),
                    Is.False
                );
                Assert.That(loadGate.TryBegin(out long retryToken), Is.True);
                Assert.That(loadGate.TryComplete(retryToken), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void ControllerPauseAndDisableUseTheSafeAbortPath()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            GameObject owner = new GameObject("W8 Lifecycle Driver");
            owner.SetActive(false);
            try
            {
                var run = owner.AddComponent<InteractionRunController>();
                var presentation = owner.AddComponent<
                    InstructionPresentationController>();
                var tasks = owner.AddComponent<
                    SignVR.Interaction.PhaseAdapters
                        .InteractionPhaseCoordinator>();
                var capture = owner.AddComponent<
                    InteractionStudyCaptureBinding>();
                var controller = owner.AddComponent<
                    InteractionStudyFlowController>();
                controller.Configure(run, presentation, tasks, capture);

                var authoritative = new FlowFixture();
                typeof(InteractionStudyFlowController).GetField(
                    "flow",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).SetValue(controller, authoritative.Flow);
                typeof(InteractionStudyFlowController).GetField(
                    "manifestReady",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).SetValue(controller, true);
                owner.SetActive(true);
                Assert.That(
                    authoritative.Flow.TryStart().Succeeded,
                    Is.True
                );

                MethodInfo pause = typeof(InteractionStudyFlowController)
                    .GetMethod(
                        "OnApplicationPause",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    );
                pause.Invoke(controller, new object[] { true });
                Assert.That(
                    authoritative.Run.State,
                    Is.EqualTo(RunState.Aborting)
                );
                Assert.That(authoritative.Run.SubscriberCount, Is.Zero);
                Assert.That(
                    authoritative.Presentation.SubscriberCount,
                    Is.Zero
                );
                Assert.That(authoritative.Tasks.SubscriberCount, Is.Zero);

                authoritative.Run.MarkAborted();
                pause.Invoke(controller, new object[] { false });
                Assert.That(
                    authoritative.Run.State,
                    Is.EqualTo(RunState.PreStart)
                );
                Assert.That(authoritative.Run.SubscriberCount, Is.EqualTo(1));

                Assert.That(
                    authoritative.Flow.TryStart().Succeeded,
                    Is.True
                );
                typeof(InteractionStudyFlowController).GetMethod(
                    "OnDisable",
                    BindingFlags.Instance | BindingFlags.NonPublic
                ).Invoke(controller, null);
                Assert.That(
                    authoritative.Run.State,
                    Is.EqualTo(RunState.Aborting)
                );
                Assert.That(authoritative.Run.SubscriberCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void RealInstructionControlsRouteReplayThroughW8Sink()
        {
            GameObject owner = new GameObject("W8 Command Sink Driver");
            owner.SetActive(false);
            try
            {
                var presentation =
                    owner.AddComponent<InstructionPresentationController>();
                var controls =
                    owner.AddComponent<InteractionInstructionControls>();
                controls.Configure(presentation);
                controls.ConfigureCommandRouting(true);
                Assert.That(controls.RequireCommandSink, Is.True);
                Assert.That(controls.ReplayButton.interactable, Is.False);
                var sink = new CountingCommandSink();
                var otherSink = new CountingCommandSink();
                Assert.That(controls.TryInstallCommandSink(sink), Is.True);
                Assert.That(controls.TryInstallCommandSink(otherSink), Is.False);
                Assert.That(controls.TryClearCommandSink(otherSink), Is.False);

                MethodInfo replay = typeof(InteractionInstructionControls)
                    .GetMethod(
                        "HandleReplay",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic
                    );
                Assert.That(replay, Is.Not.Null);
                replay.Invoke(controls, null);

                Assert.That(sink.ReplayCount, Is.EqualTo(1));
                Assert.That(sink.GiveUpCount, Is.Zero);
                Assert.That(sink.AbortCount, Is.Zero);
                Assert.That(controls.TryClearCommandSink(sink), Is.True);
                Assert.That(controls.HasLiveCommandSink, Is.False);
                Assert.That(controls.ReplayButton.interactable, Is.False);

                GameObject destroyedOwner = new GameObject(
                    "Destroyed W8 Sink"
                );
                var destroyedSink = destroyedOwner.AddComponent<
                    InteractionStudyFlowControls>();
                Assert.That(
                    controls.TryInstallCommandSink(destroyedSink),
                    Is.True
                );
                UnityEngine.Object.DestroyImmediate(destroyedOwner);
                Assert.That(controls.HasLiveCommandSink, Is.False);
                Assert.That(
                    controls.TryInstallCommandSink(otherSink),
                    Is.True
                );
                Assert.That(controls.TryClearCommandSink(otherSink), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        public static void CaptureBindingRequiresRealMatchedMetaSourcesAndProbes()
        {
            GameObject owner = new GameObject("W8 Capture Binding Driver");
            owner.SetActive(false);
            GameObject leftObject = new GameObject("OVRHandDataSourceLeft");
            GameObject rightObject = new GameObject("OVRHandDataSourceRight");
            GameObject probeObject = new GameObject("W7 Stable Probe");
            leftObject.transform.SetParent(owner.transform, false);
            rightObject.transform.SetParent(owner.transform, false);
            probeObject.transform.SetParent(owner.transform, false);
            try
            {
                var sampler = owner.AddComponent<InteractionCaptureSampler>();
                var capture = owner.AddComponent<InteractionStudyCaptureBinding>();
                OVRHand left = leftObject.AddComponent<OVRHand>();
                OVRHand right = rightObject.AddComponent<OVRHand>();
                OVRSkeleton leftSkeleton =
                    leftObject.AddComponent<OVRSkeleton>();
                OVRSkeleton rightSkeleton =
                    rightObject.AddComponent<OVRSkeleton>();
                ConfigureMetaHand(left, "HandLeft", leftSkeleton);
                ConfigureMetaHand(right, "HandRight", rightSkeleton);
                var probe = probeObject.AddComponent<
                    InteractionObjectStateProbe>();
                probe.Configure("box_stool", probeObject.transform);

                capture.Configure(
                    sampler,
                    owner.transform,
                    left,
                    leftSkeleton,
                    right,
                    rightSkeleton,
                    new[] { probe }
                );
                Assert.That(capture.ValidateStructure(out _), Is.True);

                SetSkeletonType(
                    rightSkeleton,
                    ((OVRSkeleton.IOVRSkeletonDataProvider)left)
                        .GetSkeletonType()
                );
                Assert.That(capture.ValidateStructure(out string mismatch), Is.False);
                Assert.That(mismatch, Does.Contain("Right"));

                ConfigureMetaHand(right, "HandRight", rightSkeleton);
                capture.Configure(
                    sampler,
                    owner.transform,
                    left,
                    leftSkeleton,
                    right,
                    rightSkeleton,
                    Array.Empty<InteractionObjectStateProbe>()
                );
                Assert.That(capture.ValidateStructure(out string probes), Is.False);
                Assert.That(probes, Does.Contain("probe"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static void ConfigureMetaHand(
            OVRHand hand,
            string handTypeName,
            OVRSkeleton skeleton)
        {
            FieldInfo handType = typeof(OVRHand).GetField(
                "HandType",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic
            ) ?? throw new MissingFieldException(
                typeof(OVRHand).FullName,
                "HandType"
            );
            handType.SetValue(
                hand,
                Enum.Parse(handType.FieldType, handTypeName)
            );
            SetSkeletonType(
                skeleton,
                ((OVRSkeleton.IOVRSkeletonDataProvider)hand)
                    .GetSkeletonType()
            );
        }

        private static void SetSkeletonType(
            OVRSkeleton skeleton,
            OVRSkeleton.SkeletonType value)
        {
            FieldInfo skeletonType = typeof(OVRSkeleton).GetField(
                "_skeletonType",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new MissingFieldException(
                typeof(OVRSkeleton).FullName,
                "_skeletonType"
            );
            skeletonType.SetValue(skeleton, value);
        }

        private static FlowFixture StartFirstPhase(List<string> log = null)
        {
            var fixture = new FlowFixture(log: log);
            Assert.That(fixture.Flow.TryStart().Succeeded, Is.True);
            fixture.Run.PublishInitialPresentation();
            fixture.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.First
            );
            return fixture;
        }

        private static void CompleteReplayThenGiveUp(FlowFixture fixture)
        {
            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.First
            );
            Assert.That(fixture.Flow.TryReplay().Succeeded, Is.True);
            fixture.Presentation.PublishFirstFrame(
                InteractionPresentationPlaybackKind.Replay
            );
            fixture.Presentation.PublishCompletion(
                InteractionPresentationPlaybackKind.Replay
            );
            Assert.That(fixture.Flow.TryGiveUp().Succeeded, Is.True);
        }

        private static ValidationResult CompleteCurrentTask(
            FakeTaskPort tasks,
            RunPlan plan,
            int phaseId)
        {
            ValidationResult result = null;
            switch (phaseId)
            {
                case 1:
                    result = tasks.AcceptInput(
                        1,
                        PhaseInput.Target(
                            plan.Phases[0].TaskVariant.TargetIds[0]
                        )
                    );
                    foreach (int digit in plan.SafePassword.Digits)
                    {
                        result = tasks.AcceptInput(1, PhaseInput.Digit(digit));
                    }
                    result = tasks.AcceptInput(1, PhaseInput.Submit());
                    break;
                case 2:
                    result = tasks.AcceptInput(
                        2,
                        PhaseInput.Pair(
                            plan.Phases[1].TaskVariant.TargetIds[0],
                            plan.Phases[1].TaskVariant.TargetIds[1]
                        )
                    );
                    break;
                case 3:
                    result = tasks.AcceptInput(
                        3,
                        PhaseInput.Target(
                            plan.Phases[2].TaskVariant.TargetIds[0]
                        )
                    );
                    break;
                case 4:
                    foreach (string target in plan.ChestButtonOrder.ButtonIds)
                    {
                        result = tasks.AcceptInput(4, PhaseInput.Target(target));
                    }
                    break;
                case 5:
                    result = tasks.AcceptInput(
                        5,
                        PhaseInput.Target(
                            plan.Phases[3].TaskVariant.TargetIds[0]
                        )
                    );
                    foreach (string target in
                        plan.Phases[4].TaskVariant.TargetIds)
                    {
                        result = tasks.AcceptInput(5, PhaseInput.Target(target));
                    }
                    break;
                case 6:
                    foreach (string target in
                        plan.Phases[5].TaskVariant.OrderedTargetIds)
                    {
                        result = tasks.AcceptInput(6, PhaseInput.Target(target));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(phaseId));
            }
            return result;
        }

        private sealed class FlowFixture
        {
            public FlowFixture(
                bool createFlow = true,
                List<string> log = null)
            {
                List<string> sharedLog = log ?? new List<string>();
                Run = new FakeRunPort(sharedLog);
                Presentation = new FakePresentationPort(sharedLog);
                Tasks = new FakeTaskPort(sharedLog);
                if (createFlow)
                {
                    Flow = new InteractionStudyFlow(
                        Run,
                        Presentation,
                        Tasks
                    );
                }
            }

            public FakeRunPort Run { get; }
            public FakePresentationPort Presentation { get; }
            public FakeTaskPort Tasks { get; }
            public InteractionStudyFlow Flow { get; }
        }

        private sealed class FakeRunPort : IInteractionStudyRunPort
        {
            private readonly List<string> log;
            private readonly InteractionRunStateMachine machine;
            private readonly RunPlanGenerationRequest request;
            private Action<InteractionStudyPresentationRequest> subscriber;
            private InteractionPresentationHandshake handshake;
            private InteractionStudyPresentationRequest checkpointed;
            private InteractionRunResult lastTerminal;

            public FakeRunPort(List<string> log)
            {
                this.log = log;
                machine = new InteractionRunStateMachine(
                    new AssistanceBlockAllocator(17),
                    new RunPlanGenerator(
                        () => Guid.Parse(
                            "11111111-2222-3333-4444-555555555555"
                        ),
                        () => new DateTimeOffset(
                            2026,
                            8,
                            26,
                            8,
                            0,
                            0,
                            TimeSpan.Zero
                        )
                    )
                );
                request = new RunPlanGenerationRequest(
                    "pilot-20260826",
                    "P001",
                    "app_w8_test",
                    "1.0.0",
                    "testcommit",
                    20260826,
                    CreateCatalog()
                );
            }

            public RunState State => machine.State;
            public RunPlan Plan => machine.Plan;
            public PhaseExecutionSnapshot CurrentPhase => machine.CurrentPhase;
            public IReadOnlyList<PhaseExecutionSnapshot> PhaseSnapshots =>
                machine.PhaseSnapshots;
            public string LastError { get; private set; } = string.Empty;
            public string StartFailure { get; set; }
            public int StartCount { get; private set; }
            public int ReplayRequestCount { get; private set; }
            public int AcknowledgeCount { get; private set; }
            public int PlaybackCompletionCount { get; private set; }
            public int SubscriberCount => subscriber == null
                ? 0
                : subscriber.GetInvocationList().Length;
            public Action<InteractionStudyPresentationRequest>
                LastSubscribedCallback { get; private set; }
            public bool HasCheckpointedPresentation => checkpointed != null;
            public List<ValidationResult> RecordedResults { get; } =
                new List<ValidationResult>();
            public RunPlan LastTerminalPlan => lastTerminal?.Plan;

            public IDisposable Subscribe(
                Action<InteractionStudyPresentationRequest> callback)
            {
                subscriber += callback;
                LastSubscribedCallback = callback;
                return new CallbackDisposable(() => subscriber -= callback);
            }

            public bool CanStart(out string reason)
            {
                reason = StartFailure;
                return State == RunState.PreStart &&
                    string.IsNullOrEmpty(StartFailure);
            }

            public bool TryStart(out string error)
            {
                if (!CanStart(out error))
                {
                    return false;
                }
                machine.Start(request);
                machine.HostScheduled(DateTimeOffset.UtcNow);
                handshake = new InteractionPresentationHandshake(machine);
                StartCount++;
                return true;
            }

            public bool TryRequestReplay(out string error)
            {
                try
                {
                    InteractionPresentationRequest request =
                        handshake.RequestReplayPlayback();
                    ReplayRequestCount++;
                    Publish(Map(request));
                    error = null;
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }

            public bool TryAcknowledgePresentationStarted(
                InteractionStudyPresentationRequest request,
                out string error)
            {
                try
                {
                    handshake.AcknowledgePlaybackStarted(
                        request.RequestSequence,
                        request.PhaseId,
                        request.PlaybackKind,
                        10d + request.RequestSequence
                    );
                    AcknowledgeCount++;
                    error = null;
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }

            public bool TryNotifyPlaybackCompleted(
                InteractionPresentationPlaybackKind playbackKind,
                out string error)
            {
                try
                {
                    if (playbackKind ==
                        InteractionPresentationPlaybackKind.First)
                    {
                        machine.FirstPlaybackCompleted();
                    }
                    else
                    {
                        machine.ReplayPlaybackCompleted();
                    }
                    PlaybackCompletionCount++;
                    error = null;
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }

            public bool TryRecordValidationResult(
                ValidationResult result,
                out string error)
            {
                RecordedResults.Add(result);
                if (result.InteractionError)
                {
                    machine.RecordInteractionError();
                }
                error = null;
                return true;
            }

            public bool TryRecordPresentationObservation(
                InteractionStudyPresentationObservation observation,
                out string error)
            {
                error = null;
                return true;
            }

            public bool TryFinishPhase(bool stuck, out string error)
            {
                try
                {
                    if (CurrentPhase.PhaseId == PhaseSentenceRanges.PhaseCount)
                    {
                        handshake.CompleteFinalPhase(stuck, 100d);
                    }
                    else
                    {
                        checkpointed = Map(
                            handshake.RequestNextPhasePlayback(stuck, 100d)
                        );
                    }
                    error = null;
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }

            public bool TryAbort(string reason, out string error)
            {
                log.Add("w6.abort");
                try
                {
                    handshake?.CancelPending();
                    machine.AbortRun(reason);
                    error = null;
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }

            public bool TryResetToPreStart()
            {
                if (State != RunState.Completed &&
                    State != RunState.Aborted &&
                    State != RunState.Faulted)
                {
                    return false;
                }
                lastTerminal = machine.LastResult;
                machine.ResetToPreStart();
                handshake = null;
                checkpointed = null;
                log.Add("w6.reset");
                return true;
            }

            public InteractionStudyPresentationRequest
                PublishInitialPresentation()
            {
                InteractionStudyPresentationRequest mapped = Map(
                    handshake.RequestInitialPlayback()
                );
                Publish(mapped);
                return mapped;
            }

            public void PublishCheckpointedPresentation()
            {
                InteractionStudyPresentationRequest next = checkpointed ??
                    throw new InvalidOperationException(
                        "No checkpointed presentation is pending."
                    );
                checkpointed = null;
                Publish(next);
            }

            public void PublishAgain(
                InteractionStudyPresentationRequest request)
            {
                Publish(request);
            }

            public void MarkCompleted()
            {
                machine.MarkRunCompleted();
            }

            public void MarkAborted()
            {
                machine.MarkRunAborted();
            }

            public void Fault(string reason)
            {
                machine.FaultRun(reason);
                lastTerminal = machine.LastResult;
            }

            private void Publish(InteractionStudyPresentationRequest value)
            {
                subscriber?.Invoke(value);
            }

            private static InteractionStudyPresentationRequest Map(
                InteractionPresentationRequest value)
            {
                return new InteractionStudyPresentationRequest(
                    value.RequestSequence,
                    value.RunId,
                    value.PhaseId,
                    value.PlaybackKind
                );
            }
        }

        private sealed class FakePresentationPort :
            IInteractionStudyPresentationPort
        {
            private readonly List<string> log;
            private readonly InstructionPhasePresentationState state = new();
            private Action<InteractionPresentationPlaybackKind> firstFrame;
            private Action<InteractionPresentationPlaybackKind> completed;
            private Action<InteractionStudyPresentationObservation> observation;
            private Action<string> faulted;
            private double now = 1d;

            public FakePresentationPort(List<string> log)
            {
                this.log = log;
            }

            public bool PhaseActive => state.PhaseActive;
            public bool ReplayAvailable => state.ReplayAvailable;
            public bool GiveUpAvailable => state.GiveUpAvailable;
            public int BeginPhaseCount { get; private set; }
            public int BeginReplayCount { get; private set; }
            public int EndPhaseCount { get; private set; }
            public AssistanceCondition LastCondition { get; private set; }
            public int SubscriberCount => firstFrame == null ? 0 : 1;
            public bool ThrowOnSubscribe { get; set; }
            public bool ThrowOnEndPhase { get; set; }

            public IDisposable Subscribe(
                Action<InteractionPresentationPlaybackKind>
                    firstFramePresented,
                Action<InteractionPresentationPlaybackKind>
                    playbackCompleted,
                Action<InteractionStudyPresentationObservation>
                    presentationObserved,
                Action<string> presentationFaulted)
            {
                if (ThrowOnSubscribe)
                {
                    throw new InvalidOperationException(
                        "injected presentation subscribe failure"
                    );
                }
                firstFrame = firstFramePresented;
                completed = playbackCompleted;
                observation = presentationObserved;
                faulted = presentationFaulted;
                return new CallbackDisposable(() =>
                {
                    firstFrame = null;
                    completed = null;
                    observation = null;
                    faulted = null;
                });
            }

            public bool TryBeginPhase(
                RunPhasePlan phasePlan,
                AssistanceCondition condition,
                out string error)
            {
                state.BeginPhase(condition);
                LastCondition = condition;
                BeginPhaseCount++;
                error = null;
                return true;
            }

            public bool TryBeginReplay(out string error)
            {
                bool accepted = state.TryConsumeReplay();
                if (accepted)
                {
                    BeginReplayCount++;
                    error = null;
                    return true;
                }
                error = "W5 replay is unavailable.";
                return false;
            }

            public void EndPhase()
            {
                if (ThrowOnEndPhase)
                {
                    throw new InvalidOperationException(
                        "injected presentation EndPhase failure"
                    );
                }
                state.EndPhase();
                EndPhaseCount++;
                log.Add("w5.end");
            }

            public void PublishFirstFrame(
                InteractionPresentationPlaybackKind kind)
            {
                firstFrame?.Invoke(kind);
            }

            public void PublishCompletion(
                InteractionPresentationPlaybackKind kind)
            {
                now += 1d;
                if (kind == InteractionPresentationPlaybackKind.First &&
                    !state.HasCompletedFirstPlayback)
                {
                    state.FirstPlaybackCompleted(now);
                }
                else if (kind == InteractionPresentationPlaybackKind.Replay &&
                    state.ReplayInProgress)
                {
                    state.ReplayCompleted();
                }
                completed?.Invoke(kind);
            }

            public void PublishFault(string error)
            {
                faulted?.Invoke(error);
            }
        }

        private sealed class FakeTaskPort : IInteractionStudyTaskPort
        {
            private readonly List<string> log;
            private readonly InteractionPhaseSession session = new();
            private readonly List<Action<ValidationResult>> subscribers =
                new List<Action<ValidationResult>>();

            public FakeTaskPort(List<string> log)
            {
                this.log = log;
            }

            public RunPlan Plan => session.Plan;
            public ValidationResult LastResult => session.LastResult;
            public int? CurrentPhaseId => session.CurrentPhaseId;
            public int ResetCount { get; private set; }
            public int AbortCount { get; private set; }
            public int SubscriberCount => subscribers.Count;
            public bool ThrowOnAbort { get; set; }

            public IDisposable Subscribe(Action<ValidationResult> callback)
            {
                subscribers.Add(callback);
                session.ResultProduced += callback;
                return new CallbackDisposable(() =>
                {
                    session.ResultProduced -= callback;
                    subscribers.Remove(callback);
                });
            }

            public void Configure(RunPlan plan)
            {
                session.Configure(plan);
            }

            public void Enable()
            {
                session.Enable();
            }

            public void Disable()
            {
                session.Disable();
            }

            public void Synchronize(PhaseExecutionSnapshot snapshot)
            {
                session.Synchronize(snapshot);
            }

            public ValidationResult GiveUp(
                PhaseExecutionSnapshot snapshot)
            {
                return session.GiveUpCurrentPhase(snapshot);
            }

            public void Reset()
            {
                session.Reset();
                ResetCount++;
                log.Add("w7.reset");
            }

            public void Abort()
            {
                AbortCount++;
                if (ThrowOnAbort)
                {
                    throw new InvalidOperationException(
                        "injected task Abort failure"
                    );
                }
                session.Abort();
                log.Add("w7.abort");
            }

            public ValidationResult AcceptInput(int phaseId, PhaseInput input)
            {
                return session.AcceptInput(phaseId, input);
            }
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action dispose;

            public CallbackDisposable(Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose()
            {
                Action callback = dispose;
                dispose = null;
                callback?.Invoke();
            }
        }

        private sealed class CountingCommandSink :
            IInteractionInstructionCommandSink
        {
            public event Action StateChanged
            {
                add { }
                remove { }
            }

            public bool CanReplay => true;
            public bool CanGiveUp => true;
            public bool CanAbort => true;
            public int ReplayCount { get; private set; }
            public int GiveUpCount { get; private set; }
            public int AbortCount { get; private set; }

            public void RequestReplay()
            {
                ReplayCount++;
            }

            public void RequestGiveUp()
            {
                GiveUpCount++;
            }

            public void RequestAbort()
            {
                AbortCount++;
            }
        }

        private interface IDriverConstraint
        {
            bool Matches(object actual);
            string Describe();
        }

        private sealed class DriverConstraint : IDriverConstraint
        {
            private readonly Func<object, bool> predicate;
            private readonly string description;

            public DriverConstraint(
                Func<object, bool> predicate,
                string description)
            {
                this.predicate = predicate;
                this.description = description;
            }

            public bool Matches(object actual)
            {
                return predicate(actual);
            }

            public string Describe()
            {
                return description;
            }
        }

        private static class Assert
        {
            public static void That(
                object actual,
                IDriverConstraint constraint,
                string message = null)
            {
                if (constraint == null)
                {
                    throw new ArgumentNullException(nameof(constraint));
                }
                if (!constraint.Matches(actual))
                {
                    throw new InvalidOperationException(
                        (string.IsNullOrWhiteSpace(message)
                            ? "W8 driver assertion failed."
                            : message) + " Expected " +
                        constraint.Describe() + ", actual " +
                        (actual ?? "<null>") + "."
                    );
                }
            }

            public static TException Throws<TException>(Action action)
                where TException : Exception
            {
                try
                {
                    action();
                }
                catch (TException exception)
                {
                    return exception;
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Expected " + typeof(TException).Name +
                        " but observed " + exception.GetType().Name + ".",
                        exception
                    );
                }
                throw new InvalidOperationException(
                    "Expected " + typeof(TException).Name +
                    " but no exception was thrown."
                );
            }
        }

        private static class Is
        {
            public static IDriverConstraint True => new DriverConstraint(
                actual => actual is bool value && value,
                "true"
            );
            public static IDriverConstraint False => new DriverConstraint(
                actual => actual is bool value && !value,
                "false"
            );
            public static IDriverConstraint Null => new DriverConstraint(
                actual => actual == null,
                "null"
            );
            public static IDriverConstraint Zero => new DriverConstraint(
                actual => actual is IConvertible &&
                    Convert.ToDecimal(actual) == decimal.Zero,
                "zero"
            );
            public static DriverNegation Not => new DriverNegation();

            public static IDriverConstraint EqualTo(object expected)
            {
                return new DriverConstraint(
                    actual => object.Equals(actual, expected),
                    "equal to " + (expected ?? "<null>")
                );
            }

            public static IDriverConstraint SameAs(object expected)
            {
                return new DriverConstraint(
                    actual => ReferenceEquals(actual, expected),
                    "the same reference"
                );
            }

            public static IDriverConstraint GreaterThan(IComparable expected)
            {
                return new DriverConstraint(
                    actual => actual is IComparable comparable &&
                        comparable.CompareTo(expected) > 0,
                    "greater than " + expected
                );
            }

            public static IDriverConstraint LessThan(IComparable expected)
            {
                return new DriverConstraint(
                    actual => actual is IComparable comparable &&
                        comparable.CompareTo(expected) < 0,
                    "less than " + expected
                );
            }
        }

        private sealed class DriverNegation
        {
            public IDriverConstraint Null => new DriverConstraint(
                actual => actual != null,
                "not null"
            );
        }

        private static class Does
        {
            public static IDriverConstraint Contain(string expected)
            {
                return new DriverConstraint(
                    actual => actual is string text &&
                        text.IndexOf(expected, StringComparison.Ordinal) >= 0,
                    "a string containing " + expected
                );
            }
        }

        private static T NewUiComponent<T>(
            Transform parent,
            string name) where T : Component
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            return item.AddComponent<T>();
        }

        private static byte[] CreateManifestBytes()
        {
            var builder = new StringBuilder(12288);
            builder.Append("{\"schema_version\":1,\"entries\":[");
            bool first = true;
            foreach (string sentenceId in PhaseSentenceRanges.AllSentenceIds)
            {
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                builder.Append("{\"phase_id\":");
                builder.Append(PhaseSentenceRanges.GetPhaseId(sentenceId));
                builder.Append(",\"sentence_id\":\"");
                builder.Append(sentenceId);
                builder.Append("\",\"signer_id\":\"wang\",");
                builder.Append("\"take_id\":\"take_001\",");
                builder.Append(
                    "\"completed_utc\":\"2026-08-25T00:00:00Z\","
                );
                builder.Append("\"take_index\":1,\"pose_path\":\"");
                builder.Append("wang/sentence_");
                builder.Append(sentenceId);
                builder.Append("/take_001.pose.jsonl\",");
                builder.Append("\"pose_sha256\":\"");
                builder.Append(new string('a', 64));
                builder.Append("\"}");
            }
            builder.Append("]}");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static InstructionContentCatalog CreateCatalog()
        {
            var values = new List<InstructionContentReference>();
            foreach (string sentenceId in PhaseSentenceRanges.AllSentenceIds)
            {
                values.Add(new InstructionContentReference(
                    PhaseSentenceRanges.GetPhaseId(sentenceId),
                    sentenceId,
                    InteractionContractV1.PilotSignerId,
                    "take_001",
                    new DateTimeOffset(
                        2026,
                        8,
                        25,
                        0,
                        0,
                        0,
                        TimeSpan.Zero
                    ),
                    1,
                    "wang/sentence_" + sentenceId + "/take_001.pose.jsonl",
                    new string('a', 64)
                ));
            }
            return new InstructionContentCatalog(values);
        }
    }
}
#endif
