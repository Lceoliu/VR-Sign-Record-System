#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;
using UnityEditor;
using UnityEngine;

namespace SignVR.Editor.Interaction.Qa
{
    public static class InteractionQaConsoleTestDriver
    {
        public static void ActionsUseOnlyTheApprovedQaAuthorityPort()
        {
            var port = new RecordingAuthorityPort();
            var service = new InteractionQaActionService(port);

            Require(service.Start().Succeeded, "Start was not dispatched.");
            Require(service.Replay().Succeeded, "Replay was not dispatched.");
            Require(service.GiveUp().Succeeded, "GiveUp was not dispatched.");
            Require(service.Abort().Succeeded, "Abort was not dispatched.");
            Require(
                service.ConfirmResult().Succeeded,
                "Confirm was not dispatched."
            );
            Require(
                service.SubmitInput(PhaseInput.Target("box_stool")).Succeeded,
                "Phase input was not dispatched."
            );
            Require(
                service.RebuildPresentation().Succeeded,
                "Presentation rebuild was not dispatched."
            );
            Require(
                service.ResetPresentation().Succeeded,
                "Presentation reset was not dispatched."
            );

            string actual = string.Join(",", port.Calls);
            const string expected =
                "Start,Replay,GiveUp,Abort:editor_qa_requested_abort," +
                "Confirm,Input:Target:box_stool,Rebuild,Reset";
            Require(
                string.Equals(actual, expected, StringComparison.Ordinal),
                $"Unexpected authority calls: {actual}"
            );
        }

        public static void ConfirmResultDelegatesToPublicFlowAuthority()
        {
            var root = new GameObject("QA Confirm Flow");
            try
            {
                InteractionStudyFlowController flow =
                    root.AddComponent<InteractionStudyFlowController>();
                var port = new UnityInteractionQaAuthorityPort(
                    flow,
                    phaseCoordinator: null,
                    presentation: null,
                    isPlaying: () => true
                );

                InteractionQaActionResult result = port.TryConfirmResult();

                Require(
                    !result.Succeeded &&
                    result.Message.Contains("Study Flow is not initialized"),
                    "Confirm did not delegate to the public Study Flow " +
                    "authority."
                );
                Require(
                    !result.Message.Contains("not wired"),
                    "Confirm still uses the temporary QA placeholder."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void CorrectAndWrongTargetResolutionCoversAllSixPhases()
        {
            RunPlan plan = CreatePlan();
            string[] expectedCorrect =
            {
                "box_stool",
                "coin_dragon|plate_dragon",
                "picture_frame_a",
                "key_a",
                "button_a",
                "breaker_a"
            };

            for (int phaseId = 1; phaseId <= 6; phaseId++)
            {
                InteractionQaInputResolution correct =
                    InteractionQaTargetResolver.ResolveCorrect(plan, phaseId);
                InteractionQaInputResolution wrong =
                    InteractionQaTargetResolver.ResolveWrong(plan, phaseId);
                Require(correct.Succeeded, correct.Error);
                Require(wrong.Succeeded, wrong.Error);

                string correctValue = Describe(correct.Input);
                string wrongValue = Describe(wrong.Input);
                Require(
                    string.Equals(
                        correctValue,
                        expectedCorrect[phaseId - 1],
                        StringComparison.Ordinal
                    ),
                    $"Phase {phaseId} correct input was {correctValue}."
                );
                Require(
                    !string.Equals(
                        wrongValue,
                        correctValue,
                        StringComparison.Ordinal
                    ),
                    $"Phase {phaseId} wrong input matched the correct input."
                );
            }

            PhaseInput phaseTwo = InteractionQaTargetResolver
                .ResolveCorrect(plan, 2).Input;
            PhaseInput phaseTwoWrong = InteractionQaTargetResolver
                .ResolveWrong(plan, 2).Input;
            Require(
                phaseTwo.Kind == PhaseInputKind.Pair &&
                phaseTwo.TargetId == "coin_dragon" &&
                phaseTwo.SecondaryTargetId == "plate_dragon",
                "Phase 2 QA input must retain the coin-plate pair."
            );
            Require(
                phaseTwoWrong.Kind == PhaseInputKind.Pair &&
                !string.IsNullOrEmpty(phaseTwoWrong.TargetId) &&
                !string.IsNullOrEmpty(phaseTwoWrong.SecondaryTargetId) &&
                Describe(phaseTwoWrong) != Describe(phaseTwo),
                "Phase 2 wrong QA input must remain a coin-plate pair."
            );

            PhaseInput phaseFiveNext = InteractionQaTargetResolver
                .ResolveCorrect(plan, 5, new[] { "button_a" }).Input;
            Require(
                phaseFiveNext.TargetId == "button_b",
                "Phase 5 must resolve an unaccepted planned button."
            );
            PhaseInput phaseSixNext = InteractionQaTargetResolver
                .ResolveCorrect(plan, 6, new[] { "breaker_a" }).Input;
            Require(
                phaseSixNext.TargetId == "breaker_b",
                "Phase 6 must follow its planned breaker order."
            );
        }

        public static void NotPlayingOrMissingComponentsFailsSafely()
        {
            var notPlaying = new UnityInteractionQaAuthorityPort(
                null,
                null,
                null,
                () => false
            );
            AssertEveryActionFailsSafely(notPlaying);

            var missingComponents = new UnityInteractionQaAuthorityPort(
                null,
                null,
                null,
                () => true
            );
            AssertEveryActionFailsSafely(missingComponents);
        }

        public static void InputInjectionCannotBypassTheLifecycleGate()
        {
            var root = new GameObject("Interaction QA lifecycle gate");
            try
            {
                InteractionPhaseCoordinator coordinator =
                    root.AddComponent<InteractionPhaseCoordinator>();
                InteractionPhaseAdapter[] adapters =
                {
                    root.AddComponent<PhaseOneInteractionAdapter>(),
                    root.AddComponent<PhaseTwoInteractionAdapter>(),
                    root.AddComponent<PhaseThreeInteractionAdapter>(),
                    root.AddComponent<PhaseFourInteractionAdapter>(),
                    root.AddComponent<PhaseFiveInteractionAdapter>(),
                    root.AddComponent<PhaseSixInteractionAdapter>()
                };
                coordinator.ConfigureAdapters(adapters);
                coordinator.Configure(CreatePlan());
                coordinator.Synchronize(
                    PhaseExecutionSnapshot.CreateEngineeringLabActivePhase(1)
                );

                var port = new UnityInteractionQaAuthorityPort(
                    null,
                    coordinator,
                    null,
                    () => true
                );
                InteractionQaActionResult result = port.TrySubmitInput(
                    PhaseInput.Target("box_stool")
                );

                Require(
                    !result.Succeeded,
                    "QA input bypassed the disabled lifecycle gate."
                );
                Require(
                    coordinator.LastResult != null &&
                    coordinator.LastResult.Error ==
                        PhaseValidationError.InteractionsDisabled,
                    "The authoritative coordinator did not produce the gate " +
                    "rejection."
                );
                Require(
                    !coordinator.LastResult.PhaseCompleted &&
                    coordinator.CurrentPhaseId == 1,
                    "A rejected QA input advanced lifecycle state."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void PhaseFiveQaInputHonorsTheFeedbackWindow()
        {
            var root = new GameObject("Interaction QA phase five gate");
            try
            {
                InteractionPhaseCoordinator coordinator =
                    root.AddComponent<InteractionPhaseCoordinator>();
                InteractionPhaseAdapter[] adapters =
                {
                    root.AddComponent<PhaseOneInteractionAdapter>(),
                    root.AddComponent<PhaseTwoInteractionAdapter>(),
                    root.AddComponent<PhaseThreeInteractionAdapter>(),
                    root.AddComponent<PhaseFourInteractionAdapter>(),
                    root.AddComponent<PhaseFiveInteractionAdapter>(),
                    root.AddComponent<PhaseSixInteractionAdapter>()
                };
                coordinator.ConfigureAdapters(adapters);
                coordinator.Configure(CreatePlan());
                coordinator.Enable();
                coordinator.Synchronize(
                    PhaseExecutionSnapshot.CreateEngineeringLabActivePhase(5)
                );

                var port = new UnityInteractionQaAuthorityPort(
                    null,
                    coordinator,
                    null,
                    () => true
                );
                Require(
                    port.TrySubmitInput(
                        PhaseInput.Target("button_a")
                    ).Succeeded,
                    "QA did not accept the first planned button."
                );
                InteractionQaActionResult reset = port.TrySubmitInput(
                    PhaseInput.Target("button_a")
                );
                Require(
                    reset.Succeeded && reset.ValidationResult != null &&
                    reset.ValidationResult.ProgressReset,
                    "QA did not expose the repeated-button reset."
                );
                InteractionQaActionResult blocked = port.TrySubmitInput(
                    PhaseInput.Target("button_b")
                );
                Require(
                    !blocked.Succeeded && blocked.Message.Contains(
                        "temporarily unavailable"
                    ),
                    "QA bypassed the phase-five feedback input window."
                );
                Require(
                    ReferenceEquals(coordinator.LastResult,
                        reset.ValidationResult),
                    "A blocked QA input mutated the phase authority."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void ScreenshotPathCannotEscapeTheIgnoredQaDirectory()
        {
            string assets = Path.Combine(
                Path.GetTempPath(),
                "signvr-qa-path-test",
                "Assets"
            );
            Require(
                InteractionQaScreenshotPath.TryResolve(
                    assets,
                    "interaction-qa-20260828-120000-000.png",
                    out string valid,
                    out string validError
                ),
                validError
            );
            string expectedRoot = Path.GetFullPath(Path.Combine(
                assets,
                "Screenshots"
            ));
            Require(
                string.Equals(
                    Path.GetDirectoryName(valid),
                    expectedRoot,
                    StringComparison.OrdinalIgnoreCase
                ),
                "Valid screenshot did not resolve under Assets/Screenshots."
            );

            string rooted = Path.Combine(
                Path.GetPathRoot(expectedRoot),
                "escape.png"
            );
            string[] rejected =
            {
                ".." + Path.DirectorySeparatorChar + "escape.png",
                "nested" + Path.DirectorySeparatorChar + "escape.png",
                rooted,
                "interaction-qa.txt"
            };
            for (int index = 0; index < rejected.Length; index++)
            {
                Require(
                    !InteractionQaScreenshotPath.TryResolve(
                        assets,
                        rejected[index],
                        out _,
                        out string error
                    ) && !string.IsNullOrWhiteSpace(error),
                    "Unsafe screenshot path was accepted: " +
                    rejected[index]
                );
            }
        }

        public static void ConsoleIsEditorOnlyAndReadsPublicPointingDiagnostics()
        {
            Require(
                typeof(InteractionQaConsoleWindow).Assembly.GetName().Name ==
                    "Assembly-CSharp-Editor",
                "QA Console must compile only into Assembly-CSharp-Editor."
            );
            var menuAttributes = typeof(InteractionQaConsoleWindow)
                .GetMethod(nameof(InteractionQaConsoleWindow.Open))
                ?.GetCustomAttributes(typeof(MenuItem), inherit: false);
            Require(
                menuAttributes != null && menuAttributes.Length == 1,
                "QA Console does not expose its Editor menu entry."
            );

            var root = new GameObject("Interaction QA pointing diagnostics");
            try
            {
                GhostPointingDetector detector =
                    root.AddComponent<GhostPointingDetector>();
                Transform leftDistal = NewChild(root.transform, "LeftDistal");
                Transform leftTip = NewChild(root.transform, "LeftTip");
                Transform rightDistal = NewChild(root.transform, "RightDistal");
                Transform rightTip = NewChild(root.transform, "RightTip");
                detector.ConfigureFingerBones(
                    leftDistal,
                    leftTip,
                    rightDistal,
                    rightTip
                );
                detector.ConfigurePhase(
                    AssistanceCondition.SignOnly,
                    TaskVariantCatalog.ForSentence("001")
                );

                var port = new UnityInteractionQaAuthorityPort(
                    null,
                    null,
                    null,
                    () => true,
                    detector
                );
                InteractionQaPointingDiagnostics diagnostics =
                    port.ReadSnapshot().Pointing;
                Require(
                    diagnostics.ComponentPresent &&
                    diagnostics.PhaseConfigured &&
                    diagnostics.FingerRigComplete,
                    "QA snapshot did not expose public Ghost diagnostics."
                );
                Require(
                    !diagnostics.PointingAllowed &&
                    !diagnostics.RayVisible &&
                    string.IsNullOrEmpty(diagnostics.CurrentHitTargetId),
                    "QA diagnostics fabricated a pointing hit."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform NewChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void AssertEveryActionFailsSafely(
            IInteractionQaAuthorityPort port)
        {
            InteractionQaActionResult[] results =
            {
                port.TryStart(),
                port.TryReplay(),
                port.TryGiveUp(),
                port.TryAbort("qa-test"),
                port.TryConfirmResult(),
                port.TrySubmitInput(PhaseInput.Target("box_stool")),
                port.TryRebuildPresentation(),
                port.TryResetPresentation()
            };
            for (int index = 0; index < results.Length; index++)
            {
                Require(
                    !results[index].Succeeded &&
                    !string.IsNullOrWhiteSpace(results[index].Message),
                    $"Unsafe failure result at action {index}."
                );
            }
        }

        private static string Describe(PhaseInput input)
        {
            return input.Kind == PhaseInputKind.Pair
                ? input.TargetId + "|" + input.SecondaryTargetId
                : input.TargetId;
        }

        private static RunPlan CreatePlan()
        {
            string[] sentenceIds =
            {
                "001", "004", "013", "016", "022", "026"
            };
            var phases = sentenceIds.Select((sentenceId, index) =>
            {
                int phaseId = index + 1;
                var content = new InstructionContentReference(
                    phaseId,
                    sentenceId,
                    InteractionContractV1.PilotSignerId,
                    "take_" + sentenceId,
                    new DateTimeOffset(2026, 8, 28, 0, 0, 0,
                        TimeSpan.Zero),
                    1,
                    "wang/sentence_" + sentenceId + "/pose.jsonl",
                    new string('a', 64)
                );
                return new RunPhasePlan(
                    phaseId,
                    content,
                    TaskVariantCatalog.ForSentence(sentenceId)
                );
            }).ToArray();

            return new RunPlan(
                "qa-batch",
                "qa-participant",
                "qa-run",
                "qa-session",
                new DateTimeOffset(2026, 8, 28, 0, 0, 0,
                    TimeSpan.Zero),
                "qa-version",
                "qa-commit",
                42,
                new AssistanceAssignment(
                    AssistanceCondition.TextAndPointing,
                    0,
                    0
                ),
                new SafePassword(new[] { 1, 2, 3, 4 }),
                new ChestButtonOrder(
                    new[] { "blue", "red", "yellow", "green" }
                ),
                phases
            );
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class RecordingAuthorityPort :
            IInteractionQaAuthorityPort
        {
            public List<string> Calls { get; } = new List<string>();

            public InteractionQaSnapshot ReadSnapshot()
            {
                Calls.Add("Snapshot");
                return InteractionQaSnapshot.Unavailable(
                    true,
                    "Recording port"
                );
            }

            public InteractionQaActionResult TryStart()
            {
                return Record("Start");
            }

            public InteractionQaActionResult TryReplay()
            {
                return Record("Replay");
            }

            public InteractionQaActionResult TryGiveUp()
            {
                return Record("GiveUp");
            }

            public InteractionQaActionResult TryAbort(string reason)
            {
                return Record("Abort:" + reason);
            }

            public InteractionQaActionResult TryConfirmResult()
            {
                return Record("Confirm");
            }

            public InteractionQaActionResult TrySubmitInput(PhaseInput input)
            {
                string value = input.Kind == PhaseInputKind.Target
                    ? input.TargetId
                    : input.Kind.ToString();
                return Record($"Input:{input.Kind}:{value}");
            }

            public InteractionQaActionResult TryRebuildPresentation()
            {
                return Record("Rebuild");
            }

            public InteractionQaActionResult TryResetPresentation()
            {
                return Record("Reset");
            }

            private InteractionQaActionResult Record(string call)
            {
                Calls.Add(call);
                return InteractionQaActionResult.Success(call);
            }
        }
    }
}
#endif
