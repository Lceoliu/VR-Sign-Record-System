using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
            bool phaseGivenUp = false,
            PhaseInputKind? inputKind = null,
            string inputTargetId = null,
            string inputSecondaryTargetId = null,
            int? inputDigitValue = null)
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
            InputKind = inputKind;
            InputTargetId = inputTargetId;
            InputSecondaryTargetId = inputSecondaryTargetId;
            InputDigitValue = inputDigitValue;
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

        /// <summary>
        /// The original input that produced this result. These fields are
        /// independent from TargetId, which remains the feedback target.
        /// Non-input results such as GiveUp leave all four values null.
        /// </summary>
        public PhaseInputKind? InputKind { get; }

        public string InputTargetId { get; }

        public string InputSecondaryTargetId { get; }

        public int? InputDigitValue { get; }

        internal ValidationResult WithInput(PhaseInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            return new ValidationResult(
                PhaseId,
                Accepted,
                InteractionError,
                ProgressReset,
                PhaseCompleted,
                Progress,
                RequiredProgress,
                FeedbackCue,
                Error,
                TargetId,
                ReleasedTargetId,
                PhaseGivenUp,
                input.Kind,
                input.TargetId,
                input.SecondaryTargetId,
                input.DigitValue
            );
        }
    }

    /// <summary>
    /// Immutable task-presentation state derived from accepted W7 inputs.
    /// It contains no Run or Phase lifecycle transition state; W1 remains the
    /// sole lifecycle authority through PhaseExecutionSnapshot.
    /// </summary>
    public sealed class InteractionTaskPresentationSnapshot
    {
        private readonly ReadOnlyCollection<string> chestButtonTargetIds;
        private readonly ReadOnlyCollection<string> cabinetButtonTargetIds;
        private readonly ReadOnlyCollection<string> breakerTargetIds;

        internal InteractionTaskPresentationSnapshot(
            bool chestOrderVisible,
            bool safeDoorOpened,
            bool chestOpened,
            bool cabinetUnlocked,
            bool finalDoorOpened,
            string releasedKeyTargetId,
            IEnumerable<string> chestButtons,
            IEnumerable<string> cabinetButtons,
            IEnumerable<string> breakers)
        {
            ChestOrderVisible = chestOrderVisible;
            SafeDoorOpened = safeDoorOpened;
            ChestOpened = chestOpened;
            CabinetUnlocked = cabinetUnlocked;
            FinalDoorOpened = finalDoorOpened;
            ReleasedKeyTargetId = releasedKeyTargetId;
            chestButtonTargetIds = Copy(chestButtons);
            cabinetButtonTargetIds = Copy(cabinetButtons);
            breakerTargetIds = Copy(breakers);
        }

        public bool ChestOrderVisible { get; }

        public bool SafeDoorOpened { get; }

        public bool ChestOpened { get; }

        public bool CabinetUnlocked { get; }

        public bool FinalDoorOpened { get; }

        public string ReleasedKeyTargetId { get; }

        public IReadOnlyList<string> ChestButtonTargetIds =>
            chestButtonTargetIds;

        public IReadOnlyList<string> CabinetButtonTargetIds =>
            cabinetButtonTargetIds;

        public IReadOnlyList<string> BreakerTargetIds => breakerTargetIds;

        private static ReadOnlyCollection<string> Copy(
            IEnumerable<string> values)
        {
            return new List<string>(
                values ?? Array.Empty<string>()
            ).AsReadOnly();
        }
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
        private RunPlan plan;
        private PhaseExecutionSnapshot lifecycleSnapshot;
        private bool locallyEnabled;
        private bool phaseTaskLocked;
        private readonly HashSet<string> phaseFiveButtons =
            new HashSet<string>(StringComparer.Ordinal);
        private ValidationResult lastResult;
        private int phaseSixProgress;
        private bool chestOrderVisible;
        private bool safeDoorOpened;
        private bool chestOpened;
        private bool cabinetUnlocked;
        private bool finalDoorOpened;
        private string releasedKeyTargetId;
        private readonly List<string> presentationChestButtons =
            new List<string>();
        private readonly List<string> presentationCabinetButtons =
            new List<string>();
        private readonly List<string> presentationBreakers =
            new List<string>();

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

        public InteractionTaskPresentationSnapshot PresentationSnapshot =>
            new InteractionTaskPresentationSnapshot(
                chestOrderVisible,
                safeDoorOpened,
                chestOpened,
                cabinetUnlocked,
                finalDoorOpened,
                releasedKeyTargetId,
                presentationChestButtons,
                presentationCabinetButtons,
                presentationBreakers
            );

        public void Configure(RunPlan runPlan)
        {
            if (runPlan == null)
            {
                throw new ArgumentNullException(nameof(runPlan));
            }

            ValidatePlanShape(runPlan);
            plan = runPlan;
            ResetAllTaskState();
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
            ResetAllTaskState();
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
                    ResetTaskProgress();
                    phaseTaskLocked = false;
                }
            }
            else
            {
                ResetTaskProgress();
                phaseTaskLocked = false;
            }

            lifecycleSnapshot = snapshot;
            if (snapshot.PhaseId >= 2)
            {
                safeDoorOpened = true;
            }
            if (snapshot.PhaseId >= 5)
            {
                cabinetUnlocked = true;
            }
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

            ResetTaskProgress();
            ResetPresentationProgress(phaseId);
            if (phaseId == 3)
            {
                chestOrderVisible = true;
            }
            if (phaseId == 1)
            {
                safeDoorOpened = true;
            }
            phaseTaskLocked = true;
            string releasedKey = phaseId == 4
                ? plan.Phases[3].TaskVariant.TargetIds[0]
                : null;
            if (phaseId == 4)
            {
                chestOpened = true;
                releasedKeyTargetId = releasedKey;
            }
            PhaseFeedbackCue cue = phaseId == 1
                ? PhaseFeedbackCue.SafeDoorOpened
                : phaseId == 4
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
                return PublishInput(gate, input);
            }

            if (phaseId == 1)
            {
                return PublishInput(AcceptPhaseOne(input), input);
            }

            if (phaseId == 2)
            {
                return PublishInput(AcceptPhaseTwo(input), input);
            }

            if (phaseId == 3)
            {
                return PublishInput(AcceptPhaseThree(input), input);
            }

            if (phaseId == 4)
            {
                return PublishInput(AcceptPhaseFour(input), input);
            }

            if (phaseId == 5)
            {
                return PublishInput(AcceptPhaseFive(input), input);
            }

            if (phaseId == 6)
            {
                return PublishInput(AcceptPhaseSix(input), input);
            }

            return PublishInput(new ValidationResult(
                phaseId,
                false,
                true,
                false,
                false,
                0,
                0,
                PhaseFeedbackCue.None,
                PhaseValidationError.InvalidInput
            ), input);
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
            chestOrderVisible = true;
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
            string plannedKey = plan.Phases[3].TaskVariant.TargetIds[0];
            if (input.Kind != PhaseInputKind.Target ||
                !string.Equals(
                    input.TargetId,
                    plannedKey,
                    StringComparison.Ordinal
                ))
            {
                return new ValidationResult(
                    4,
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

            chestOpened = true;
            chestOrderVisible = false;
            releasedKeyTargetId = plannedKey;
            phaseTaskLocked = true;
            presentationChestButtons.Clear();
            return new ValidationResult(
                4,
                true,
                false,
                false,
                true,
                1,
                1,
                PhaseFeedbackCue.ChestOpened,
                PhaseValidationError.None,
                input.TargetId,
                plannedKey
            );
        }

        private ValidationResult AcceptPhaseFive(PhaseInput input)
        {
            IReadOnlyList<string> plannedButtons =
                plan.Phases[4].TaskVariant.TargetIds;
            int requiredProgress = plannedButtons.Count;

            if (input.Kind != PhaseInputKind.Target ||
                !ContainsOrdinal(plannedButtons, input.TargetId) ||
                !phaseFiveButtons.Add(input.TargetId))
            {
                return PhaseFiveError(input.TargetId, requiredProgress);
            }

            int progress = phaseFiveButtons.Count;
            AddPresentationTarget(
                presentationCabinetButtons,
                input.TargetId
            );
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
            presentationCabinetButtons.Clear();
            return new ValidationResult(
                5,
                false,
                true,
                true,
                false,
                0,
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
                presentationBreakers.Clear();
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
                presentationBreakers.Clear();
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
            AddPresentationTarget(
                presentationBreakers,
                input.TargetId
            );
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
            finalDoorOpened = true;
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
            const int requiredProgress = 1;
            string plannedBox = plan.Phases[0].TaskVariant.TargetIds[0];
            if (input.Kind != PhaseInputKind.Target ||
                !string.Equals(
                    input.TargetId,
                    plannedBox,
                    StringComparison.Ordinal
                ))
            {
                return PhaseOneError(input.TargetId, requiredProgress);
            }

            phaseTaskLocked = true;
            safeDoorOpened = true;
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
            return new ValidationResult(
                1,
                false,
                true,
                false,
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

        private ValidationResult PublishInput(
            ValidationResult result,
            PhaseInput input)
        {
            return Publish(result.WithInput(input));
        }

        private static void AddPresentationTarget(
            ICollection<string> targets,
            string targetId)
        {
            if (!targets.Contains(targetId))
            {
                targets.Add(targetId);
            }
        }

        private void ResetPresentationProgress(int phaseId)
        {
            if (phaseId == 4)
            {
                chestOrderVisible = false;
                presentationChestButtons.Clear();
            }
            else if (phaseId == 5)
            {
                presentationCabinetButtons.Clear();
            }
            else if (phaseId == 6)
            {
                presentationBreakers.Clear();
            }
        }

        private void ResetAllTaskState()
        {
            ResetTaskProgress();
            chestOrderVisible = false;
            safeDoorOpened = false;
            chestOpened = false;
            cabinetUnlocked = false;
            finalDoorOpened = false;
            releasedKeyTargetId = null;
            presentationChestButtons.Clear();
            presentationCabinetButtons.Clear();
            presentationBreakers.Clear();
        }

        private void ResetTaskProgress()
        {
            phaseFiveButtons.Clear();
            phaseSixProgress = 0;
            lastResult = null;
        }
    }
}
