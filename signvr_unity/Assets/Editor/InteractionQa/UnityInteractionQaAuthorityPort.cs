using System;
using System.Collections.Generic;
using System.Linq;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;
using UnityEditor;

namespace SignVR.Editor.Interaction.Qa
{
    /// <summary>
    /// Editor-only adapter over the existing public Interaction authorities.
    /// No private field access or W1 lifecycle mutation exists in this type.
    /// </summary>
    public sealed class UnityInteractionQaAuthorityPort :
        IInteractionQaAuthorityPort
    {
        private readonly InteractionStudyFlowController flowController;
        private readonly InteractionPhaseCoordinator phaseCoordinator;
        private readonly InteractionDeterministicPresentation presentation;
        private readonly Func<bool> isPlaying;
        private readonly GhostPointingDetector pointingDetector;

        public UnityInteractionQaAuthorityPort(
            InteractionStudyFlowController flowController,
            InteractionPhaseCoordinator phaseCoordinator,
            InteractionDeterministicPresentation presentation,
            Func<bool> isPlaying = null,
            GhostPointingDetector pointingDetector = null)
        {
            this.flowController = flowController;
            this.phaseCoordinator = phaseCoordinator;
            this.presentation = presentation;
            this.isPlaying = isPlaying ?? (() => EditorApplication.isPlaying);
            this.pointingDetector = pointingDetector;
        }

        public InteractionQaSnapshot ReadSnapshot()
        {
            bool playing = isPlaying();
            InteractionStudyFlowSnapshot flowSnapshot =
                flowController?.Snapshot;
            RunPlan plan = phaseCoordinator?.Plan ??
                flowController?.RunController?.Plan;
            int? phaseId = flowSnapshot?.PhaseId ??
                phaseCoordinator?.CurrentPhaseId ??
                flowController?.RunController?.CurrentPhaseId;
            ValidationResult lastResult = phaseCoordinator?.LastResult;

            IReadOnlyList<string> plannedTargets =
                ResolvePlannedTargets(plan, phaseId);
            IReadOnlyList<string> acceptedTargets =
                ResolveAcceptedTargets(phaseId);
            int progress = flowSnapshot?.Progress ??
                lastResult?.Progress ?? acceptedTargets.Count;
            int requiredProgress = flowSnapshot?.RequiredProgress ??
                lastResult?.RequiredProgress ?? plannedTargets.Count;
            RunState? runState = flowSnapshot != null
                ? flowSnapshot.RunState
                : flowController?.RunController?.State;
            string status = flowSnapshot?.Status;
            if (string.IsNullOrWhiteSpace(status))
            {
                status = !playing
                    ? "Enter Play Mode to operate the Interaction Run."
                    : flowController == null
                        ? "InteractionStudyFlowController was not found."
                        : "Study Flow has not produced a snapshot yet.";
            }

            return new InteractionQaSnapshot(
                playing,
                runState,
                phaseId,
                progress,
                requiredProgress,
                flowSnapshot?.CanStart ?? false,
                flowSnapshot?.CanReplay ?? false,
                flowSnapshot?.CanGiveUp ?? false,
                flowSnapshot?.AbortInProgress ?? false,
                status,
                plan,
                plannedTargets,
                acceptedTargets,
                Describe(lastResult),
                ReadPointingDiagnostics()
            );
        }

        public InteractionQaActionResult TryStart()
        {
            return ExecuteFlow(
                "Start",
                controller => controller.TryStart()
            );
        }

        public InteractionQaActionResult TryReplay()
        {
            return ExecuteFlow(
                "Replay",
                controller => controller.TryReplay()
            );
        }

        public InteractionQaActionResult TryGiveUp()
        {
            return ExecuteFlow(
                "Give Up",
                controller => controller.TryGiveUp()
            );
        }

        public InteractionQaActionResult TryAbort(string reason)
        {
            return ExecuteFlow(
                "Abort",
                controller => controller.TryAbort(reason)
            );
        }

        public InteractionQaActionResult TryConfirmResult()
        {
            return ExecuteFlow(
                "Confirm Result",
                controller => controller.TryAcknowledgeResult()
            );
        }

        public InteractionQaActionResult TrySubmitInput(PhaseInput input)
        {
            if (!TryRequirePlayMode(out InteractionQaActionResult failure))
            {
                return failure;
            }
            if (phaseCoordinator == null)
            {
                return Missing("InteractionPhaseCoordinator");
            }
            if (input == null)
            {
                return InteractionQaActionResult.Failure(
                    "A phase input is required."
                );
            }
            int? phaseId = phaseCoordinator.CurrentPhaseId;
            if (!phaseId.HasValue)
            {
                return InteractionQaActionResult.Failure(
                    "The phase authority exposes no current phase."
                );
            }

            try
            {
                ValidationResult result = phaseCoordinator.AcceptInput(
                    phaseId.Value,
                    input
                );
                bool reachedTaskRule = result.Accepted ||
                    result.InteractionError;
                string message = reachedTaskRule
                    ? $"Phase {result.PhaseId} authority produced " +
                        $"{result.Error}; progress " +
                        $"{result.Progress}/{result.RequiredProgress}."
                    : $"Phase {result.PhaseId} lifecycle gate rejected QA " +
                        $"input with {result.Error}.";
                return reachedTaskRule
                    ? InteractionQaActionResult.Success(message, result)
                    : InteractionQaActionResult.Failure(message);
            }
            catch (Exception exception)
            {
                return InteractionQaActionResult.Failure(
                    "Phase input failed safely: " + exception.Message
                );
            }
        }

        public InteractionQaActionResult TryRebuildPresentation()
        {
            return ExecutePresentation(
                "Rebuild",
                value => value.RebuildFromAuthority()
            );
        }

        public InteractionQaActionResult TryResetPresentation()
        {
            return ExecutePresentation(
                "Reset",
                value => value.ResetPresentation()
            );
        }

        private InteractionQaActionResult ExecuteFlow(
            string label,
            Func<InteractionStudyFlowController,
                InteractionStudyFlowCommandResult> command)
        {
            if (!TryRequirePlayMode(out InteractionQaActionResult failure))
            {
                return failure;
            }
            if (flowController == null)
            {
                return Missing("InteractionStudyFlowController");
            }

            try
            {
                InteractionStudyFlowCommandResult result =
                    command(flowController);
                return result.Succeeded
                    ? InteractionQaActionResult.Success(label + " accepted.")
                    : InteractionQaActionResult.Failure(
                        label + " rejected: " + result.Error
                    );
            }
            catch (Exception exception)
            {
                return InteractionQaActionResult.Failure(
                    label + " failed safely: " + exception.Message
                );
            }
        }

        private InteractionQaActionResult ExecutePresentation(
            string label,
            Action<InteractionDeterministicPresentation> action)
        {
            if (!TryRequirePlayMode(out InteractionQaActionResult failure))
            {
                return failure;
            }
            if (presentation == null)
            {
                return Missing("InteractionDeterministicPresentation");
            }

            try
            {
                action(presentation);
                return InteractionQaActionResult.Success(
                    label + " presentation accepted."
                );
            }
            catch (Exception exception)
            {
                return InteractionQaActionResult.Failure(
                    label + " presentation failed safely: " +
                    exception.Message
                );
            }
        }

        private bool TryRequirePlayMode(
            out InteractionQaActionResult failure)
        {
            if (!isPlaying())
            {
                failure = InteractionQaActionResult.Failure(
                    "Enter Play Mode before using Interaction QA actions."
                );
                return false;
            }
            failure = null;
            return true;
        }

        private static InteractionQaActionResult Missing(string component)
        {
            return InteractionQaActionResult.Failure(
                component + " was not found in the loaded scene."
            );
        }

        private IReadOnlyList<string> ResolveAcceptedTargets(int? phaseId)
        {
            InteractionTaskPresentationSnapshot snapshot =
                phaseCoordinator?.PresentationSnapshot;
            if (snapshot == null || !phaseId.HasValue)
            {
                return Array.Empty<string>();
            }
            if (phaseId.Value == 5)
            {
                return snapshot.CabinetButtonTargetIds.ToArray();
            }
            if (phaseId.Value == 6)
            {
                return snapshot.BreakerTargetIds.ToArray();
            }
            return Array.Empty<string>();
        }

        private static IReadOnlyList<string> ResolvePlannedTargets(
            RunPlan plan,
            int? phaseId)
        {
            if (plan == null || !phaseId.HasValue ||
                phaseId.Value < 1 ||
                phaseId.Value > PhaseSentenceRanges.PhaseCount)
            {
                return Array.Empty<string>();
            }
            TaskVariant variant = plan.Phases[phaseId.Value - 1].TaskVariant;
            return (phaseId.Value == 6 &&
                    variant.OrderedTargetIds.Count > 0
                    ? variant.OrderedTargetIds
                    : variant.TargetIds).ToArray();
        }

        private InteractionQaPointingDiagnostics ReadPointingDiagnostics()
        {
            if (pointingDetector == null)
            {
                return InteractionQaPointingDiagnostics.Missing();
            }
            return new InteractionQaPointingDiagnostics(
                true,
                pointingDetector.PhaseConfigured,
                pointingDetector.HasCompleteFingerRig,
                pointingDetector.PointingAllowed,
                pointingDetector.IsPointingVisible,
                pointingDetector.CurrentHitTargetId,
                pointingDetector.PointingExposureSeconds
            );
        }

        private static string Describe(ValidationResult result)
        {
            if (result == null)
            {
                return "No result.";
            }
            return $"Phase {result.PhaseId}: accepted={result.Accepted}, " +
                $"interaction_error={result.InteractionError}, " +
                $"error={result.Error}, progress=" +
                $"{result.Progress}/{result.RequiredProgress}, " +
                $"completed={result.PhaseCompleted}.";
        }
    }
}
