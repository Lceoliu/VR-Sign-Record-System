using System;
using System.Collections.Generic;

namespace SignVR.Interaction.Core
{
    public enum PhaseInputKind
    {
        Target,
        Pair,
        Digit,
        Backspace,
        Submit
    }

    public enum PhaseFeedbackCue
    {
        None,
        SafePasswordRevealed,
        SafeDigitRemoved,
        SafeDoorOpened,
        CoinPlaced,
        ChestOrderUnlocked,
        ChestButtonAccepted,
        ChestOpened,
        CabinetUnlocked,
        CabinetButtonAccepted,
        CabinetTaskCompleted,
        BreakerAccepted,
        FinalLeftDoorOpened,
        PhaseGivenUp
    }

    public enum PhaseValidationError
    {
        None,
        NotConfigured,
        InteractionsDisabled,
        CompletedPhaseLocked,
        FuturePhaseLocked,
        InvalidInput,
        UnexpectedTarget,
        IncorrectCombination,
        IncorrectOrder,
        IncorrectPassword,
        LifecycleSnapshotRequired,
        LifecycleSnapshotMismatch,
        GiveUpUnavailable,
        InvalidPlan
    }

    public sealed class PhaseInput
    {
        private PhaseInput(
            PhaseInputKind kind,
            string targetId,
            string secondaryTargetId,
            int? digit)
        {
            Kind = kind;
            TargetId = targetId;
            SecondaryTargetId = secondaryTargetId;
            DigitValue = digit;
        }

        public PhaseInputKind Kind { get; }

        public string TargetId { get; }

        public string SecondaryTargetId { get; }

        public int? DigitValue { get; }

        public static PhaseInput Target(string targetId)
        {
            return new PhaseInput(
                PhaseInputKind.Target,
                CoreGuard.Required(targetId, nameof(targetId)),
                null,
                null
            );
        }

        public static PhaseInput Pair(
            string firstTargetId,
            string secondTargetId)
        {
            return new PhaseInput(
                PhaseInputKind.Pair,
                CoreGuard.Required(firstTargetId, nameof(firstTargetId)),
                CoreGuard.Required(secondTargetId, nameof(secondTargetId)),
                null
            );
        }

        public static PhaseInput Digit(int digit)
        {
            if (digit < 0 || digit > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(digit));
            }

            return new PhaseInput(PhaseInputKind.Digit, null, null, digit);
        }

        public static PhaseInput Submit()
        {
            return new PhaseInput(PhaseInputKind.Submit, null, null, null);
        }

        public static PhaseInput Backspace()
        {
            return new PhaseInput(
                PhaseInputKind.Backspace,
                null,
                null,
                null
            );
        }
    }

    public sealed class ValidationResult
    {
        internal ValidationResult(
            int phaseId,
            bool accepted,
            bool interactionError,
            bool progressReset,
            bool phaseCompleted,
            int progress,
            int requiredProgress,
            PhaseFeedbackCue feedbackCue,
            PhaseValidationError error,
            string targetId = null,
            string releasedTargetId = null,
            bool phaseGivenUp = false)
        {
            PhaseId = phaseId;
            Accepted = accepted;
            InteractionError = interactionError;
            ProgressReset = progressReset;
            PhaseCompleted = phaseCompleted;
            Progress = progress;
            RequiredProgress = requiredProgress;
            FeedbackCue = feedbackCue;
            Error = error;
            TargetId = targetId;
            ReleasedTargetId = releasedTargetId;
            PhaseGivenUp = phaseGivenUp;
        }

        public int PhaseId { get; }

        public bool Accepted { get; }

        public bool InteractionError { get; }

        public bool ProgressReset { get; }

        /// <summary>
        /// True when the current phase's task rule has completed. This is a
        /// request for the W1 InteractionRunStateMachine owner to complete the
        /// phase; it is not itself a lifecycle transition or PhaseResult.
        /// </summary>
        public bool PhaseCompleted { get; }

        public int Progress { get; }

        public int RequiredProgress { get; }

        public PhaseFeedbackCue FeedbackCue { get; }

        public PhaseValidationError Error { get; }

        public string TargetId { get; }

        public string ReleasedTargetId { get; }

        public bool PhaseGivenUp { get; }
    }

    public static class PhaseRuleRouting
    {
        public static int GetPhaseId(TaskVariant taskVariant)
        {
            if (taskVariant == null)
            {
                throw new ArgumentNullException(nameof(taskVariant));
            }

            int phaseId = PhaseSentenceRanges.GetPhaseId(
                taskVariant.SentenceId
            );
            if (phaseId != taskVariant.PhaseId)
            {
                throw new InvalidOperationException(
                    "Task Variant phase does not match its sentence range."
                );
            }

            return phaseId;
        }
    }

    /// <summary>
    /// Pure C# authority for deterministic manipulation progress within the
    /// immutable six-phase Run Plan. Presentation and scene bindings remain in
    /// Unity adapters.
    /// </summary>
    public sealed class InteractionPhaseSession
    {
        private readonly List<int> safeDigits = new List<int>(
            SafePassword.DigitCount
        );

        private RunPlan plan;
        private PhaseExecutionSnapshot lifecycleSnapshot;
        private bool locallyEnabled;
        private bool phaseTaskLocked;
        private bool phaseOneBoxAccepted;
        private int phaseFourProgress;
        private bool phaseFiveKeyAccepted;
        private readonly HashSet<string> phaseFiveButtons =
            new HashSet<string>(StringComparer.Ordinal);
        private ValidationResult lastResult;
        private int phaseSixProgress;

        public event Action<ValidationResult> ResultProduced;

        public RunPlan Plan => plan;

        public int? CurrentPhaseId => lifecycleSnapshot?.PhaseId;

        public bool IsEnabled =>
            locallyEnabled &&
            lifecycleSnapshot != null &&
            lifecycleSnapshot.InteractionsEnabled &&
            !phaseTaskLocked;

        public bool IsLocallyEnabled => locallyEnabled;

        public bool HasSynchronizedPhase => lifecycleSnapshot != null;

        public bool IsPhaseTaskLocked => phaseTaskLocked;

        public PhaseExecutionSnapshot LifecycleSnapshot => lifecycleSnapshot;

        public ValidationResult LastResult => lastResult;

        public void Configure(RunPlan runPlan)
        {
            if (runPlan == null)
            {
                throw new ArgumentNullException(nameof(runPlan));
            }

            ValidatePlanShape(runPlan);
            plan = runPlan;
            ResetProgress();
            lifecycleSnapshot = null;
            locallyEnabled = false;
            phaseTaskLocked = false;
        }

        public void Enable()
        {
            if (plan == null)
            {
                throw new InvalidOperationException(
                    "Configure a Run Plan before enabling interactions."
                );
            }

            locallyEnabled = true;
        }

        public void Disable()
        {
            locallyEnabled = false;
        }

        public void Reset()
        {
            plan = null;
            lifecycleSnapshot = null;
            locallyEnabled = false;
            phaseTaskLocked = false;
            ResetProgress();
        }

        public void Abort()
        {
            Reset();
        }

        /// <summary>
        /// Applies the W1 lifecycle authority's current immutable snapshot.
        /// W7 never creates or advances a phase; it only unlocks task rules for
        /// the phase explicitly identified by this snapshot.
        /// </summary>
        public void Synchronize(PhaseExecutionSnapshot snapshot)
        {
            if (plan == null)
            {
                throw new InvalidOperationException(
                    "Configure a Run Plan before synchronizing lifecycle state."
                );
            }
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (snapshot.PhaseId < 1 ||
                snapshot.PhaseId > PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentOutOfRangeException(nameof(snapshot));
            }
            if (snapshot.State == PhaseState.Inactive)
            {
                throw new ArgumentException(
                    "An inactive phase snapshot cannot activate task rules.",
                    nameof(snapshot)
                );
            }

            if (lifecycleSnapshot != null)
            {
                if (snapshot.PhaseId < lifecycleSnapshot.PhaseId ||
                    snapshot.PhaseId > lifecycleSnapshot.PhaseId + 1)
                {
                    throw new InvalidOperationException(
                        $"W1 phase snapshot drifted from " +
                        $"{lifecycleSnapshot.PhaseId} to {snapshot.PhaseId}."
                    );
                }

                if (snapshot.PhaseId > lifecycleSnapshot.PhaseId)
                {
                    ResetProgress();
                    phaseTaskLocked = false;
                }
            }
            else
            {
                ResetProgress();
                phaseTaskLocked = false;
            }

            lifecycleSnapshot = snapshot;
            if (!snapshot.InteractionsEnabled)
            {
                phaseTaskLocked = true;
            }
        }

        public ValidationResult GiveUpCurrentPhase(
            PhaseExecutionSnapshot snapshot)
        {
            int phaseId = lifecycleSnapshot?.PhaseId ?? 1;
            ValidationResult gate = ValidateGate(phaseId);
            if (gate != null)
            {
                return Publish(gate);
            }
            if (snapshot == null)
            {
                return Publish(GateFailure(
                    phaseId,
                    PhaseValidationError.LifecycleSnapshotRequired
                ));
            }
            if (!MatchesLifecycleSnapshot(snapshot, lifecycleSnapshot))
            {
                return Publish(GateFailure(
                    phaseId,
                    PhaseValidationError.LifecycleSnapshotMismatch
                ));
            }
            if (!snapshot.GiveUpAvailable)
            {
                return Publish(GateFailure(
                    phaseId,
                    PhaseValidationError.GiveUpUnavailable
                ));
            }

            ResetProgress();
            phaseTaskLocked = true;
            string releasedKey = phaseId == 4
                ? plan.Phases[3].TaskVariant.TargetIds[0]
                : null;
            PhaseFeedbackCue cue = phaseId == 4
                ? PhaseFeedbackCue.ChestOpened
                : PhaseFeedbackCue.PhaseGivenUp;
            return Publish(new ValidationResult(
                phaseId,
                true,
                false,
                false,
                false,
                0,
                0,
                cue,
                PhaseValidationError.None,
                releasedTargetId: releasedKey,
                phaseGivenUp: true
            ));
        }

        public ValidationResult AcceptInput(int phaseId, PhaseInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            ValidationResult gate = ValidateGate(phaseId);
            if (gate != null)
            {
                return Publish(gate);
            }

            if (phaseId == 1)
            {
                return Publish(AcceptPhaseOne(input));
            }

            if (phaseId == 2)
            {
                return Publish(AcceptPhaseTwo(input));
            }

            if (phaseId == 3)
            {
                return Publish(AcceptPhaseThree(input));
            }

            if (phaseId == 4)
            {
                return Publish(AcceptPhaseFour(input));
            }

            if (phaseId == 5)
            {
                return Publish(AcceptPhaseFive(input));
            }

            if (phaseId == 6)
            {
                return Publish(AcceptPhaseSix(input));
            }

            return Publish(new ValidationResult(
                phaseId,
                false,
                true,
                false,
                false,
                0,
                0,
                PhaseFeedbackCue.None,
                PhaseValidationError.InvalidInput
            ));
        }

        private ValidationResult AcceptPhaseTwo(PhaseInput input)
        {
            IReadOnlyList<string> plannedTargets =
                plan.Phases[1].TaskVariant.TargetIds;
            if (plannedTargets == null || plannedTargets.Count != 2)
            {
                return InvalidPlanResult(2);
            }
            if (input.Kind != PhaseInputKind.Pair ||
                !string.Equals(
                    input.TargetId,
                    plannedTargets[0],
                    StringComparison.Ordinal
                ) ||
                !string.Equals(
                    input.SecondaryTargetId,
                    plannedTargets[1],
                    StringComparison.Ordinal
                ))
            {
                return new ValidationResult(
                    2,
                    false,
                    true,
                    false,
                    false,
                    0,
                    1,
                    PhaseFeedbackCue.None,
                    PhaseValidationError.IncorrectCombination,
                    input.TargetId
                );
            }

            phaseTaskLocked = true;
            return new ValidationResult(
                2,
                true,
                false,
                false,
                true,
                1,
                1,
                PhaseFeedbackCue.CoinPlaced,
                PhaseValidationError.None,
                input.TargetId
            );
        }

        private ValidationResult AcceptPhaseThree(PhaseInput input)
        {
            string plannedFrame = plan.Phases[2].TaskVariant.TargetIds[0];
            if (input.Kind != PhaseInputKind.Target ||
                !string.Equals(
                    input.TargetId,
                    plannedFrame,
                    StringComparison.Ordinal
                ))
            {
                return new ValidationResult(
                    3,
                    false,
                    true,
                    false,
                    false,
                    0,
                    1,
                    PhaseFeedbackCue.None,
                    PhaseValidationError.UnexpectedTarget,
                    input.TargetId
                );
            }

            phaseTaskLocked = true;
            return new ValidationResult(
                3,
                true,
                false,
                false,
                true,
                1,
                1,
                PhaseFeedbackCue.ChestOrderUnlocked,
                PhaseValidationError.None,
                input.TargetId
            );
        }

        private ValidationResult AcceptPhaseFour(PhaseInput input)
        {
            IReadOnlyList<string> order = plan.ChestButtonOrder.ButtonIds;
            string expectedTarget = order[phaseFourProgress];
            if (input.Kind != PhaseInputKind.Target ||
                !string.Equals(
                    input.TargetId,
                    expectedTarget,
                    StringComparison.Ordinal
                ))
            {
                phaseFourProgress = 0;
                return new ValidationResult(
                    4,
                    false,
                    true,
                    true,
                    false,
                    0,
                    order.Count,
                    PhaseFeedbackCue.None,
                    PhaseValidationError.UnexpectedTarget,
                    input.TargetId
                );
            }

            phaseFourProgress++;
            if (phaseFourProgress < order.Count)
            {
                return new ValidationResult(
                    4,
                    true,
                    false,
                    false,
                    false,
                    phaseFourProgress,
                    order.Count,
                    PhaseFeedbackCue.ChestButtonAccepted,
                    PhaseValidationError.None,
                    input.TargetId
                );
            }

            string releasedKey =
                plan.Phases[3].TaskVariant.TargetIds[0];
            phaseTaskLocked = true;
            phaseFourProgress = 0;
            return new ValidationResult(
                4,
                true,
                false,
                false,
                true,
                order.Count,
                order.Count,
                PhaseFeedbackCue.ChestOpened,
                PhaseValidationError.None,
                input.TargetId,
                releasedKey
            );
        }

        private ValidationResult AcceptPhaseFive(PhaseInput input)
        {
            IReadOnlyList<string> plannedButtons =
                plan.Phases[4].TaskVariant.TargetIds;
            int requiredProgress = plannedButtons.Count + 1;
            string plannedKey = plan.Phases[3].TaskVariant.TargetIds[0];

            if (!phaseFiveKeyAccepted)
            {
                if (input.Kind != PhaseInputKind.Target ||
                    !string.Equals(
                        input.TargetId,
                        plannedKey,
                        StringComparison.Ordinal
                    ))
                {
                    return PhaseFiveError(input.TargetId, requiredProgress);
                }

                phaseFiveKeyAccepted = true;
                return new ValidationResult(
                    5,
                    true,
                    false,
                    false,
                    false,
                    1,
                    requiredProgress,
                    PhaseFeedbackCue.CabinetUnlocked,
                    PhaseValidationError.None,
                    input.TargetId
                );
            }

            if (input.Kind != PhaseInputKind.Target ||
                !ContainsOrdinal(plannedButtons, input.TargetId) ||
                !phaseFiveButtons.Add(input.TargetId))
            {
                return PhaseFiveError(input.TargetId, requiredProgress);
            }

            int progress = phaseFiveButtons.Count + 1;
            if (phaseFiveButtons.Count < plannedButtons.Count)
            {
                return new ValidationResult(
                    5,
                    true,
                    false,
                    false,
                    false,
                    progress,
                    requiredProgress,
                    PhaseFeedbackCue.CabinetButtonAccepted,
                    PhaseValidationError.None,
                    input.TargetId
                );
            }

            phaseTaskLocked = true;
            phaseFiveKeyAccepted = false;
            phaseFiveButtons.Clear();
            return new ValidationResult(
                5,
                true,
                false,
                false,
                true,
                requiredProgress,
                requiredProgress,
                PhaseFeedbackCue.CabinetTaskCompleted,
                PhaseValidationError.None,
                input.TargetId
            );
        }

        private ValidationResult PhaseFiveError(
            string targetId,
            int requiredProgress)
        {
            phaseFiveButtons.Clear();
            return new ValidationResult(
                5,
                false,
                true,
                true,
                false,
                phaseFiveKeyAccepted ? 1 : 0,
                requiredProgress,
                PhaseFeedbackCue.None,
                PhaseValidationError.UnexpectedTarget,
                targetId
            );
        }

        private ValidationResult AcceptPhaseSix(PhaseInput input)
        {
            IReadOnlyList<string> order =
                plan.Phases[5].TaskVariant.OrderedTargetIds;
            if (order == null || order.Count == 0 ||
                phaseSixProgress < 0 || phaseSixProgress >= order.Count)
            {
                phaseSixProgress = 0;
                return InvalidPlanResult(6);
            }
            string expectedTarget = order[phaseSixProgress];
            if (input.Kind != PhaseInputKind.Target ||
                !string.Equals(
                    input.TargetId,
                    expectedTarget,
                    StringComparison.Ordinal
                ))
            {
                phaseSixProgress = 0;
                return new ValidationResult(
                    6,
                    false,
                    true,
                    true,
                    false,
                    0,
                    order.Count,
                    PhaseFeedbackCue.None,
                    PhaseValidationError.IncorrectOrder,
                    input.TargetId
                );
            }

            phaseSixProgress++;
            if (phaseSixProgress < order.Count)
            {
                return new ValidationResult(
                    6,
                    true,
                    false,
                    false,
                    false,
                    phaseSixProgress,
                    order.Count,
                    PhaseFeedbackCue.BreakerAccepted,
                    PhaseValidationError.None,
                    input.TargetId
                );
            }

            phaseSixProgress = 0;
            phaseTaskLocked = true;
            return new ValidationResult(
                6,
                true,
                false,
                false,
                true,
                order.Count,
                order.Count,
                PhaseFeedbackCue.FinalLeftDoorOpened,
                PhaseValidationError.None,
                input.TargetId
            );
        }

        private static bool ContainsOrdinal(
            IReadOnlyList<string> values,
            string candidate)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(
                    values[index],
                    candidate,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private ValidationResult AcceptPhaseOne(PhaseInput input)
        {
            const int requiredProgress = SafePassword.DigitCount + 1;
            string plannedBox = plan.Phases[0].TaskVariant.TargetIds[0];
            if (!phaseOneBoxAccepted)
            {
                if (input.Kind != PhaseInputKind.Target ||
                    !string.Equals(
                        input.TargetId,
                        plannedBox,
                        StringComparison.Ordinal
                    ))
                {
                    return PhaseOneError(input.TargetId, requiredProgress);
                }

                phaseOneBoxAccepted = true;
                return new ValidationResult(
                    1,
                    true,
                    false,
                    false,
                    false,
                    1,
                    requiredProgress,
                    PhaseFeedbackCue.SafePasswordRevealed,
                    PhaseValidationError.None,
                    input.TargetId
                );
            }

            if (input.Kind == PhaseInputKind.Digit &&
                safeDigits.Count < SafePassword.DigitCount)
            {
                safeDigits.Add(input.DigitValue.GetValueOrDefault());
                return new ValidationResult(
                    1,
                    true,
                    false,
                    false,
                    false,
                    safeDigits.Count + 1,
                    requiredProgress,
                    PhaseFeedbackCue.None,
                    PhaseValidationError.None
                );
            }

            if (input.Kind == PhaseInputKind.Backspace)
            {
                if (safeDigits.Count > 0)
                {
                    safeDigits.RemoveAt(safeDigits.Count - 1);
                }
                return new ValidationResult(
                    1,
                    true,
                    false,
                    false,
                    false,
                    safeDigits.Count + 1,
                    requiredProgress,
                    PhaseFeedbackCue.SafeDigitRemoved,
                    PhaseValidationError.None
                );
            }

            if (input.Kind != PhaseInputKind.Submit ||
                safeDigits.Count != SafePassword.DigitCount)
            {
                return PhaseOneError(input.TargetId, requiredProgress);
            }

            for (int index = 0; index < SafePassword.DigitCount; index++)
            {
                if (safeDigits[index] != plan.SafePassword.Digits[index])
                {
                    return PhaseOneError(
                        null,
                        requiredProgress,
                        PhaseValidationError.IncorrectPassword
                    );
                }
            }

            phaseTaskLocked = true;
            return new ValidationResult(
                1,
                true,
                false,
                false,
                true,
                requiredProgress,
                requiredProgress,
                PhaseFeedbackCue.SafeDoorOpened,
                PhaseValidationError.None,
                plannedBox
            );
        }

        private ValidationResult PhaseOneError(
            string targetId,
            int requiredProgress,
            PhaseValidationError error =
                PhaseValidationError.UnexpectedTarget)
        {
            ResetProgress();
            return new ValidationResult(
                1,
                false,
                true,
                true,
                false,
                0,
                requiredProgress,
                PhaseFeedbackCue.None,
                error,
                targetId
            );
        }

        private ValidationResult ValidateGate(int phaseId)
        {
            if (phaseId < 1 || phaseId > PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseId));
            }

            if (plan == null)
            {
                return GateFailure(
                    phaseId,
                    PhaseValidationError.NotConfigured
                );
            }

            if (lifecycleSnapshot == null)
            {
                return GateFailure(
                    phaseId,
                    PhaseValidationError.LifecycleSnapshotRequired
                );
            }

            if (phaseId < lifecycleSnapshot.PhaseId)
            {
                return GateFailure(
                    phaseId,
                    PhaseValidationError.CompletedPhaseLocked
                );
            }

            if (phaseId > lifecycleSnapshot.PhaseId)
            {
                return GateFailure(
                    phaseId,
                    PhaseValidationError.FuturePhaseLocked
                );
            }

            if (phaseTaskLocked)
            {
                return GateFailure(
                    phaseId,
                    PhaseValidationError.CompletedPhaseLocked
                );
            }

            if (!IsEnabled)
            {
                return GateFailure(
                    phaseId,
                    PhaseValidationError.InteractionsDisabled
                );
            }

            return null;
        }

        private static ValidationResult GateFailure(
            int phaseId,
            PhaseValidationError error)
        {
            return new ValidationResult(
                phaseId,
                false,
                false,
                false,
                false,
                0,
                0,
                PhaseFeedbackCue.None,
                error
            );
        }

        private static ValidationResult InvalidPlanResult(int phaseId)
        {
            return new ValidationResult(
                phaseId,
                false,
                false,
                false,
                false,
                0,
                0,
                PhaseFeedbackCue.None,
                PhaseValidationError.InvalidPlan
            );
        }

        private static void ValidatePlanShape(RunPlan runPlan)
        {
            IReadOnlyList<string> phaseTwoTargets =
                runPlan.Phases[1].TaskVariant.TargetIds;
            if (phaseTwoTargets == null || phaseTwoTargets.Count != 2)
            {
                throw new ArgumentException(
                    "Phase 2 requires exactly one coin and one plate target.",
                    nameof(runPlan)
                );
            }

            IReadOnlyList<string> phaseSixOrder =
                runPlan.Phases[5].TaskVariant.OrderedTargetIds;
            if (phaseSixOrder == null || phaseSixOrder.Count == 0)
            {
                throw new ArgumentException(
                    "Phase 6 requires a non-empty ordered target list.",
                    nameof(runPlan)
                );
            }
        }

        private static bool MatchesLifecycleSnapshot(
            PhaseExecutionSnapshot supplied,
            PhaseExecutionSnapshot synchronized)
        {
            return supplied != null &&
                synchronized != null &&
                supplied.PhaseId == synchronized.PhaseId &&
                supplied.State == synchronized.State &&
                supplied.Result == synchronized.Result &&
                supplied.FirstPlaybackCompleted ==
                    synchronized.FirstPlaybackCompleted &&
                supplied.ReplayUsed == synchronized.ReplayUsed &&
                supplied.TimeoutRecorded == synchronized.TimeoutRecorded &&
                supplied.TaskProgress == synchronized.TaskProgress &&
                supplied.InteractionErrorCount ==
                    synchronized.InteractionErrorCount &&
                supplied.FirstPlaybackStartedAt ==
                    synchronized.FirstPlaybackStartedAt &&
                supplied.GiveUpAvailable == synchronized.GiveUpAvailable;
        }

        private ValidationResult Publish(ValidationResult result)
        {
            lastResult = result;
            ResultProduced?.Invoke(result);
            return result;
        }

        private void ResetProgress()
        {
            phaseOneBoxAccepted = false;
            safeDigits.Clear();
            phaseFourProgress = 0;
            phaseFiveKeyAccepted = false;
            phaseFiveButtons.Clear();
            phaseSixProgress = 0;
            lastResult = null;
        }
    }
}
