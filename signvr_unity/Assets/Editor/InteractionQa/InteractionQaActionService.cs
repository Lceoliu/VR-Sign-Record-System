using System;
using System.Collections.Generic;
using System.Linq;
using SignVR.Interaction.Core;

namespace SignVR.Editor.Interaction.Qa
{
    public sealed class InteractionQaActionResult
    {
        private InteractionQaActionResult(
            bool succeeded,
            string message,
            ValidationResult validationResult)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            ValidationResult = validationResult;
        }

        public bool Succeeded { get; }

        public string Message { get; }

        public ValidationResult ValidationResult { get; }

        public static InteractionQaActionResult Success(
            string message = "Command accepted.",
            ValidationResult validationResult = null)
        {
            return new InteractionQaActionResult(
                true,
                message,
                validationResult
            );
        }

        public static InteractionQaActionResult Failure(string message)
        {
            return new InteractionQaActionResult(
                false,
                string.IsNullOrWhiteSpace(message)
                    ? "Command rejected."
                    : message.Trim(),
                null
            );
        }
    }

    /// <summary>
    /// The only mutation boundary available to the Editor QA action service.
    /// Its Unity implementation delegates to existing public Interaction
    /// authorities; it never owns Run or Phase lifecycle state.
    /// </summary>
    public interface IInteractionQaAuthorityPort
    {
        InteractionQaSnapshot ReadSnapshot();

        InteractionQaActionResult TryStart();

        InteractionQaActionResult TryReplay();

        InteractionQaActionResult TryGiveUp();

        InteractionQaActionResult TryAbort(string reason);

        InteractionQaActionResult TryConfirmResult();

        InteractionQaActionResult TrySubmitInput(PhaseInput input);

        InteractionQaActionResult TryRebuildPresentation();

        InteractionQaActionResult TryResetPresentation();
    }

    public interface IInteractionQaScreenshotPort
    {
        InteractionQaActionResult CaptureGameView();
    }

    public sealed class InteractionQaActionService
    {
        private readonly IInteractionQaAuthorityPort authority;
        private readonly IInteractionQaScreenshotPort screenshot;

        public InteractionQaActionService(
            IInteractionQaAuthorityPort authority,
            IInteractionQaScreenshotPort screenshot = null)
        {
            this.authority = authority ??
                throw new ArgumentNullException(nameof(authority));
            this.screenshot = screenshot;
        }

        public InteractionQaActionResult Start()
        {
            return authority.TryStart();
        }

        public InteractionQaActionResult Replay()
        {
            return authority.TryReplay();
        }

        public InteractionQaActionResult GiveUp()
        {
            return authority.TryGiveUp();
        }

        public InteractionQaActionResult Abort(
            string reason = "editor_qa_requested_abort")
        {
            return authority.TryAbort(reason);
        }

        public InteractionQaActionResult ConfirmResult()
        {
            return authority.TryConfirmResult();
        }

        public InteractionQaActionResult SubmitInput(PhaseInput input)
        {
            if (input == null)
            {
                return InteractionQaActionResult.Failure(
                    "A QA input is required."
                );
            }
            return authority.TrySubmitInput(input);
        }

        public InteractionQaActionResult RebuildPresentation()
        {
            return authority.TryRebuildPresentation();
        }

        public InteractionQaActionResult ResetPresentation()
        {
            return authority.TryResetPresentation();
        }

        public InteractionQaActionResult CaptureGameView()
        {
            return screenshot == null
                ? InteractionQaActionResult.Failure(
                    "Game View screenshot capture is not configured."
                )
                : screenshot.CaptureGameView();
        }

        public InteractionQaSnapshot ReadSnapshot()
        {
            return authority.ReadSnapshot();
        }

        public InteractionQaActionResult InjectCorrectPhaseInput()
        {
            return Inject(
                InteractionQaTargetResolver.ResolveCorrect
            );
        }

        public InteractionQaActionResult InjectWrongPhaseInput()
        {
            return Inject(
                InteractionQaTargetResolver.ResolveWrong
            );
        }

        private InteractionQaActionResult Inject(
            Func<RunPlan, int, IReadOnlyCollection<string>,
                InteractionQaInputResolution> resolve)
        {
            InteractionQaSnapshot snapshot = authority.ReadSnapshot();
            if (snapshot == null || !snapshot.CurrentPhaseId.HasValue)
            {
                return InteractionQaActionResult.Failure(
                    "No current phase is available for QA input."
                );
            }
            InteractionQaInputResolution resolution = resolve(
                snapshot.Plan,
                snapshot.CurrentPhaseId.Value,
                snapshot.AcceptedTargetIds
            );
            return resolution.Succeeded
                ? authority.TrySubmitInput(resolution.Input)
                : InteractionQaActionResult.Failure(resolution.Error);
        }
    }

    public sealed class InteractionQaPointingDiagnostics
    {
        public InteractionQaPointingDiagnostics(
            bool componentPresent,
            bool phaseConfigured,
            bool fingerRigComplete,
            bool pointingAllowed,
            bool rayVisible,
            string currentHitTargetId,
            double exposureSeconds)
        {
            ComponentPresent = componentPresent;
            PhaseConfigured = phaseConfigured;
            FingerRigComplete = fingerRigComplete;
            PointingAllowed = pointingAllowed;
            RayVisible = rayVisible;
            CurrentHitTargetId = currentHitTargetId ?? string.Empty;
            ExposureSeconds = exposureSeconds;
        }

        public bool ComponentPresent { get; }

        public bool PhaseConfigured { get; }

        public bool FingerRigComplete { get; }

        public bool PointingAllowed { get; }

        public bool RayVisible { get; }

        public string CurrentHitTargetId { get; }

        public double ExposureSeconds { get; }

        public static InteractionQaPointingDiagnostics Missing()
        {
            return new InteractionQaPointingDiagnostics(
                false,
                false,
                false,
                false,
                false,
                string.Empty,
                0d
            );
        }
    }

    public sealed class InteractionQaSnapshot
    {
        public InteractionQaSnapshot(
            bool isPlayMode,
            RunState? runState,
            int? currentPhaseId,
            int progress,
            int requiredProgress,
            bool canStart,
            bool canReplay,
            bool canGiveUp,
            bool abortInProgress,
            string status,
            RunPlan plan,
            IReadOnlyList<string> plannedTargetIds,
            IReadOnlyList<string> acceptedTargetIds,
            string lastResult,
            InteractionQaPointingDiagnostics pointing)
        {
            IsPlayMode = isPlayMode;
            RunState = runState;
            CurrentPhaseId = currentPhaseId;
            Progress = progress;
            RequiredProgress = requiredProgress;
            CanStart = canStart;
            CanReplay = canReplay;
            CanGiveUp = canGiveUp;
            AbortInProgress = abortInProgress;
            Status = status ?? string.Empty;
            Plan = plan;
            PlannedTargetIds = plannedTargetIds ?? Array.Empty<string>();
            AcceptedTargetIds = acceptedTargetIds ?? Array.Empty<string>();
            LastResult = lastResult ?? string.Empty;
            Pointing = pointing ??
                InteractionQaPointingDiagnostics.Missing();
        }

        public bool IsPlayMode { get; }

        public RunState? RunState { get; }

        public int? CurrentPhaseId { get; }

        public int Progress { get; }

        public int RequiredProgress { get; }

        public bool CanStart { get; }

        public bool CanReplay { get; }

        public bool CanGiveUp { get; }

        public bool AbortInProgress { get; }

        public string Status { get; }

        public RunPlan Plan { get; }

        public IReadOnlyList<string> PlannedTargetIds { get; }

        public IReadOnlyList<string> AcceptedTargetIds { get; }

        public string LastResult { get; }

        public InteractionQaPointingDiagnostics Pointing { get; }

        public static InteractionQaSnapshot Unavailable(
            bool isPlayMode,
            string status)
        {
            return new InteractionQaSnapshot(
                isPlayMode,
                null,
                null,
                0,
                0,
                false,
                false,
                false,
                false,
                status,
                null,
                Array.Empty<string>(),
                Array.Empty<string>(),
                "No result.",
                InteractionQaPointingDiagnostics.Missing()
            );
        }
    }

    public sealed class InteractionQaInputResolution
    {
        private InteractionQaInputResolution(
            PhaseInput input,
            string description,
            string error)
        {
            Input = input;
            Description = description ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Succeeded => Input != null;

        public PhaseInput Input { get; }

        public string Description { get; }

        public string Error { get; }

        public static InteractionQaInputResolution Success(
            PhaseInput input,
            string description)
        {
            return new InteractionQaInputResolution(
                input ?? throw new ArgumentNullException(nameof(input)),
                description,
                string.Empty
            );
        }

        public static InteractionQaInputResolution Failure(string error)
        {
            return new InteractionQaInputResolution(
                null,
                string.Empty,
                string.IsNullOrWhiteSpace(error)
                    ? "No QA input can be resolved."
                    : error.Trim()
            );
        }
    }

    public static class InteractionQaTargetResolver
    {
        private const string GuaranteedCrossPhaseWrongTarget = "box_stool";

        public static InteractionQaInputResolution ResolveCorrect(
            RunPlan plan,
            int phaseId,
            IReadOnlyCollection<string> acceptedTargetIds = null)
        {
            if (!TryGetPhase(plan, phaseId, out RunPhasePlan phase,
                    out string error))
            {
                return InteractionQaInputResolution.Failure(error);
            }

            IReadOnlyList<string> targets = phase.TaskVariant.TargetIds;
            if (phaseId == 2)
            {
                if (targets.Count != 2)
                {
                    return InteractionQaInputResolution.Failure(
                        "Phase 2 requires one planned coin and one plate."
                    );
                }
                return InteractionQaInputResolution.Success(
                    PhaseInput.Pair(targets[0], targets[1]),
                    $"correct pair {targets[0]} -> {targets[1]}"
                );
            }

            var accepted = new HashSet<string>(
                acceptedTargetIds ?? Array.Empty<string>(),
                StringComparer.Ordinal
            );
            if (phaseId == 5)
            {
                string next = targets.FirstOrDefault(target =>
                    !accepted.Contains(target));
                return string.IsNullOrEmpty(next)
                    ? InteractionQaInputResolution.Failure(
                        "All planned Phase 5 targets were already accepted."
                    )
                    : InteractionQaInputResolution.Success(
                        PhaseInput.Target(next),
                        "correct target " + next
                    );
            }

            if (phaseId == 6)
            {
                IReadOnlyList<string> order =
                    phase.TaskVariant.OrderedTargetIds;
                int progress = acceptedTargetIds?.Count ?? 0;
                if (order == null || progress < 0 || progress >= order.Count)
                {
                    return InteractionQaInputResolution.Failure(
                        "Phase 6 has no remaining planned breaker."
                    );
                }
                return InteractionQaInputResolution.Success(
                    PhaseInput.Target(order[progress]),
                    "correct ordered target " + order[progress]
                );
            }

            if (targets.Count == 0)
            {
                return InteractionQaInputResolution.Failure(
                    $"Phase {phaseId} has no planned target."
                );
            }
            return InteractionQaInputResolution.Success(
                PhaseInput.Target(targets[0]),
                "correct target " + targets[0]
            );
        }

        public static InteractionQaInputResolution ResolveWrong(
            RunPlan plan,
            int phaseId,
            IReadOnlyCollection<string> acceptedTargetIds = null)
        {
            if (!TryGetPhase(plan, phaseId, out RunPhasePlan phase,
                    out string error))
            {
                return InteractionQaInputResolution.Failure(error);
            }

            IReadOnlyList<string> planned = phase.TaskVariant.TargetIds;
            if (phaseId == 2)
            {
                string wrongCoin = CandidatesForPosition(2, 0)
                    .FirstOrDefault(candidate => !string.Equals(
                        candidate,
                        planned[0],
                        StringComparison.Ordinal
                    ));
                if (string.IsNullOrEmpty(wrongCoin))
                {
                    return InteractionQaInputResolution.Failure(
                        "No incorrect Phase 2 coin is available."
                    );
                }
                return InteractionQaInputResolution.Success(
                    PhaseInput.Pair(wrongCoin, planned[1]),
                    $"wrong pair {wrongCoin} -> {planned[1]}"
                );
            }

            if (phaseId == 6)
            {
                IReadOnlyList<string> order =
                    phase.TaskVariant.OrderedTargetIds;
                int progress = acceptedTargetIds?.Count ?? 0;
                if (order == null || progress < 0 || progress >= order.Count)
                {
                    return InteractionQaInputResolution.Failure(
                        "Phase 6 has no remaining planned breaker."
                    );
                }
                string expected = order[progress];
                string wrongBreaker = planned.FirstOrDefault(candidate =>
                    !string.Equals(
                        candidate,
                        expected,
                        StringComparison.Ordinal
                    ));
                return string.IsNullOrEmpty(wrongBreaker)
                    ? InteractionQaInputResolution.Failure(
                        "No out-of-order Phase 6 breaker is available."
                    )
                    : InteractionQaInputResolution.Success(
                        PhaseInput.Target(wrongBreaker),
                        "wrong ordered target " + wrongBreaker
                    );
            }

            string wrong = CandidateTargets(phaseId).FirstOrDefault(
                candidate => !ContainsOrdinal(planned, candidate)
            );
            if (phaseId == 5 && string.IsNullOrEmpty(wrong))
            {
                wrong = acceptedTargetIds?.FirstOrDefault(target =>
                    ContainsOrdinal(planned, target));
                if (string.IsNullOrEmpty(wrong))
                {
                    wrong = GuaranteedCrossPhaseWrongTarget;
                }
            }

            return string.IsNullOrEmpty(wrong)
                ? InteractionQaInputResolution.Failure(
                    $"No incorrect target is available for Phase {phaseId}."
                )
                : InteractionQaInputResolution.Success(
                    PhaseInput.Target(wrong),
                    "wrong target " + wrong
                );
        }

        private static bool TryGetPhase(
            RunPlan plan,
            int phaseId,
            out RunPhasePlan phase,
            out string error)
        {
            phase = null;
            if (plan == null)
            {
                error = "No active Run Plan is available.";
                return false;
            }
            if (phaseId < 1 || phaseId > PhaseSentenceRanges.PhaseCount)
            {
                error = "The current phase is outside 1-6.";
                return false;
            }
            phase = plan.Phases[phaseId - 1];
            error = string.Empty;
            return true;
        }

        private static IEnumerable<string> CandidateTargets(int phaseId)
        {
            return TaskVariantCatalog.All
                .Where(variant => variant.PhaseId == phaseId)
                .SelectMany(variant => variant.TargetIds)
                .Distinct(StringComparer.Ordinal);
        }

        private static IEnumerable<string> CandidatesForPosition(
            int phaseId,
            int position)
        {
            return TaskVariantCatalog.All
                .Where(variant => variant.PhaseId == phaseId &&
                    variant.TargetIds.Count > position)
                .Select(variant => variant.TargetIds[position])
                .Distinct(StringComparer.Ordinal);
        }

        private static bool ContainsOrdinal(
            IEnumerable<string> values,
            string candidate)
        {
            return values.Any(value => string.Equals(
                value,
                candidate,
                StringComparison.Ordinal
            ));
        }
    }
}
