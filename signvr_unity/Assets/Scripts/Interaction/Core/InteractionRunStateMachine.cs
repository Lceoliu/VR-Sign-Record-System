using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SignVR.Interaction.Core
{
    public sealed class PhaseExecutionSnapshot
    {
        internal PhaseExecutionSnapshot(
            int phaseId,
            PhaseState state,
            PhaseResult? result,
            bool firstPlaybackCompleted,
            bool replayUsed,
            bool timeoutRecorded,
            int taskProgress,
            int interactionErrorCount,
            TimeSpan? firstPlaybackStartedAt)
        {
            PhaseId = phaseId;
            State = state;
            Result = result;
            FirstPlaybackCompleted = firstPlaybackCompleted;
            ReplayUsed = replayUsed;
            TimeoutRecorded = timeoutRecorded;
            TaskProgress = taskProgress;
            InteractionErrorCount = interactionErrorCount;
            FirstPlaybackStartedAt = firstPlaybackStartedAt;
        }

        public int PhaseId { get; }

        public PhaseState State { get; }

        public PhaseResult? Result { get; }

        public bool FirstPlaybackCompleted { get; }

        public bool ReplayUsed { get; }

        public bool TimeoutRecorded { get; }

        public int TaskProgress { get; }

        public int InteractionErrorCount { get; }

        public TimeSpan? FirstPlaybackStartedAt { get; }

        public bool ReplayAvailable =>
            State == PhaseState.Active &&
            FirstPlaybackCompleted &&
            !ReplayUsed;

        public bool GiveUpAvailable =>
            State == PhaseState.Active && ReplayUsed;

        public bool InteractionsEnabled =>
            State == PhaseState.FirstPlayback ||
            State == PhaseState.Active ||
            State == PhaseState.ReplayPlayback;
    }

    public sealed class InteractionRunResult
    {
        private readonly ReadOnlyCollection<PhaseExecutionSnapshot> phases;

        internal InteractionRunResult(
            RunResult result,
            RunPlan plan,
            IEnumerable<PhaseExecutionSnapshot> phases,
            string abortReason,
            string faultReason)
        {
            CoreGuard.DefinedEnum(result, nameof(result));
            Result = result;
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (phases == null)
            {
                throw new ArgumentNullException(nameof(phases));
            }

            var copy = phases.ToList();
            if (copy.Count != PhaseSentenceRanges.PhaseCount)
            {
                throw new ArgumentException(
                    "A Run Result must retain all six phase snapshots.",
                    nameof(phases)
                );
            }

            this.phases = copy.AsReadOnly();
            AbortReason = abortReason;
            FaultReason = faultReason;
        }

        public RunResult Result { get; }

        public RunPlan Plan { get; }

        public IReadOnlyList<PhaseExecutionSnapshot> Phases => phases;

        public string AbortReason { get; }

        public string FaultReason { get; }

        public int CompletedPhaseCount => phases.Count(
            phase => phase.Result == PhaseResult.Completed
        );

        public int StuckPhaseCount => phases.Count(
            phase => phase.Result == PhaseResult.Stuck
        );
    }

    /// <summary>
    /// Pure C# authority for one six-phase Interaction Run. It validates event
    /// ordering and returns immutable snapshots; Unity presentation, capture,
    /// networking, and event serialization remain adapters outside this seam.
    /// </summary>
    public sealed class InteractionRunStateMachine
    {
        public static readonly TimeSpan PhaseTimeout = TimeSpan.FromSeconds(180);

        private readonly AssistanceBlockAllocator conditionAllocator;
        private readonly RunPlanGenerator planGenerator;
        private readonly MutablePhase[] phases;

        private RunPlan plan;
        private int currentPhaseIndex = -1;
        private DateTimeOffset? scheduledStartUtc;
        private string abortReason;
        private string faultReason;
        private InteractionRunResult lastResult;

        public InteractionRunStateMachine(
            AssistanceBlockAllocator conditionAllocator,
            RunPlanGenerator planGenerator)
        {
            this.conditionAllocator = conditionAllocator ??
                throw new ArgumentNullException(nameof(conditionAllocator));
            this.planGenerator = planGenerator ??
                throw new ArgumentNullException(nameof(planGenerator));
            phases = Enumerable.Range(1, PhaseSentenceRanges.PhaseCount)
                .Select(phaseId => new MutablePhase(phaseId))
                .ToArray();
            State = RunState.PreStart;
        }

        public RunState State { get; private set; }

        public RunPlan Plan => plan;

        public DateTimeOffset? ScheduledStartUtc => scheduledStartUtc;

        public int? CurrentPhaseId => currentPhaseIndex >= 0
            ? phases[currentPhaseIndex].PhaseId
            : (int?)null;

        public PhaseExecutionSnapshot CurrentPhase => currentPhaseIndex >= 0
            ? phases[currentPhaseIndex].Snapshot()
            : null;

        public IReadOnlyList<PhaseExecutionSnapshot> PhaseSnapshots => phases
            .Select(phase => phase.Snapshot())
            .ToList()
            .AsReadOnly();

        public InteractionRunResult LastResult => lastResult;

        public RunPlan Start(RunPlanGenerationRequest request)
        {
            EnsureState(RunState.PreStart);
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            RunPlan generatedPlan = conditionAllocator.AllocateNext(
                assignment => planGenerator.Generate(request, assignment)
            );

            ResetPhaseExecutions();
            plan = generatedPlan;
            scheduledStartUtc = null;
            currentPhaseIndex = -1;
            abortReason = null;
            faultReason = null;
            State = RunState.AwaitingHost;
            return plan;
        }

        public void HostScheduled(DateTimeOffset startAtUtc)
        {
            EnsureState(RunState.AwaitingHost);
            scheduledStartUtc = startAtUtc.ToUniversalTime();
            State = RunState.Scheduled;
        }

        public void RunStarted(TimeSpan monotonicTime)
        {
            EnsureState(RunState.Scheduled);
            ValidateMonotonicTime(monotonicTime, nameof(monotonicTime));
            State = RunState.Running;
            currentPhaseIndex = 0;
            phases[currentPhaseIndex].BeginFirstPlayback(monotonicTime);
        }

        public void FirstPlaybackCompleted()
        {
            MutablePhase phase = RequireCurrentPhase(PhaseState.FirstPlayback);
            phase.FirstPlaybackCompleted = true;
            phase.State = PhaseState.Active;
        }

        public void ReplayInstruction()
        {
            MutablePhase phase = RequireCurrentPhase(PhaseState.Active);
            if (!phase.FirstPlaybackCompleted)
            {
                throw new InvalidOperationException(
                    "Replay is unavailable before first playback completes."
                );
            }

            if (phase.ReplayUsed)
            {
                throw new InvalidOperationException(
                    "Each phase permits at most one replay."
                );
            }

            phase.ReplayUsed = true;
            phase.State = PhaseState.ReplayPlayback;
        }

        public void ReplayPlaybackCompleted()
        {
            MutablePhase phase = RequireCurrentPhase(
                PhaseState.ReplayPlayback
            );
            phase.State = PhaseState.Active;
        }

        public int AdvanceTaskProgress()
        {
            MutablePhase phase = RequireInteractionEnabledPhase();
            phase.TaskProgress = checked(phase.TaskProgress + 1);
            return phase.TaskProgress;
        }

        public void RecordInteractionError()
        {
            MutablePhase phase = RequireInteractionEnabledPhase();
            phase.InteractionErrorCount = checked(
                phase.InteractionErrorCount + 1
            );
            phase.TaskProgress = 0;
        }

        /// <summary>
        /// Returns true exactly once when the current phase first reaches the
        /// 180-second threshold. It does not change Run or Phase state.
        /// </summary>
        public bool TryRecordPhaseTimeout(TimeSpan monotonicTime)
        {
            MutablePhase phase = RequireInteractionEnabledPhase();
            ValidateMonotonicTime(monotonicTime, nameof(monotonicTime));
            TimeSpan elapsed = monotonicTime -
                phase.FirstPlaybackStartedAt.GetValueOrDefault();
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTime),
                    "Monotonic time cannot precede first playback start."
                );
            }

            if (elapsed < PhaseTimeout || phase.TimeoutRecorded)
            {
                return false;
            }

            phase.TimeoutRecorded = true;
            return true;
        }

        /// <summary>
        /// Completes the current phase. For phases 1-5, the timestamp is the
        /// actual monotonic start of the next phase's first playback and becomes
        /// that phase's timeout origin. For phase 6 it is the completion time.
        /// </summary>
        public void CompletePhase(TimeSpan nextPlaybackStartTime)
        {
            MutablePhase phase = RequireInteractionEnabledPhase();
            ValidatePhaseTime(phase, nextPlaybackStartTime);
            phase.State = PhaseState.Completed;
            phase.Result = PhaseResult.Completed;
            AdvanceAfterPhase(nextPlaybackStartTime);
        }

        /// <summary>
        /// Records Stuck and advances. For phases 1-5, the timestamp is the
        /// actual monotonic start of the next phase's first playback and becomes
        /// that phase's timeout origin. For phase 6 it is the give-up time.
        /// </summary>
        public void GiveUpPhase(TimeSpan nextPlaybackStartTime)
        {
            MutablePhase phase = RequireCurrentPhase(PhaseState.Active);
            if (!phase.FirstPlaybackCompleted || !phase.ReplayUsed)
            {
                throw new InvalidOperationException(
                    "Give Up is available only after the one allowed replay completes."
                );
            }

            ValidatePhaseTime(phase, nextPlaybackStartTime);
            phase.State = PhaseState.Stuck;
            phase.Result = PhaseResult.Stuck;
            AdvanceAfterPhase(nextPlaybackStartTime);
        }

        public void MarkRunCompleted()
        {
            EnsureState(RunState.Completing);
            State = RunState.Completed;
            lastResult = BuildResult(RunResult.Completed);
        }

        public void AbortRun(string reason)
        {
            if (State != RunState.AwaitingHost &&
                State != RunState.Scheduled &&
                State != RunState.Running &&
                State != RunState.Completing)
            {
                throw new InvalidOperationException(
                    $"Run cannot begin aborting from state {State}."
                );
            }

            abortReason = CoreGuard.Required(reason, nameof(reason));
            State = RunState.Aborting;
        }

        public void MarkRunAborted()
        {
            EnsureState(RunState.Aborting);
            State = RunState.Aborted;
            lastResult = BuildResult(RunResult.Aborted);
        }

        public void FaultRun(string reason)
        {
            if (plan == null ||
                State == RunState.PreStart ||
                State == RunState.Completed ||
                State == RunState.Aborted ||
                State == RunState.Faulted)
            {
                throw new InvalidOperationException(
                    $"Run cannot fault from state {State}."
                );
            }

            faultReason = CoreGuard.Required(reason, nameof(reason));
            State = RunState.Faulted;
            lastResult = BuildResult(RunResult.Faulted);
        }

        public void ResetToPreStart()
        {
            if (State != RunState.Completed &&
                State != RunState.Aborted &&
                State != RunState.Faulted)
            {
                throw new InvalidOperationException(
                    $"Run cannot reset to PreStart from state {State}."
                );
            }

            plan = null;
            scheduledStartUtc = null;
            currentPhaseIndex = -1;
            abortReason = null;
            faultReason = null;
            ResetPhaseExecutions();
            State = RunState.PreStart;
        }

        private void AdvanceAfterPhase(TimeSpan nextPlaybackStartTime)
        {
            if (currentPhaseIndex == phases.Length - 1)
            {
                currentPhaseIndex = -1;
                State = RunState.Completing;
                return;
            }

            currentPhaseIndex++;
            phases[currentPhaseIndex].BeginFirstPlayback(nextPlaybackStartTime);
        }

        private MutablePhase RequireCurrentPhase(params PhaseState[] allowedStates)
        {
            EnsureState(RunState.Running);
            if (currentPhaseIndex < 0)
            {
                throw new InvalidOperationException("No phase is currently active.");
            }

            MutablePhase phase = phases[currentPhaseIndex];
            if (allowedStates == null ||
                allowedStates.Length == 0 ||
                !allowedStates.Contains(phase.State))
            {
                string expected = allowedStates == null
                    ? string.Empty
                    : string.Join(", ", allowedStates);
                throw new InvalidOperationException(
                    $"Phase {phase.PhaseId} is {phase.State}; expected {expected}."
                );
            }

            return phase;
        }

        private MutablePhase RequireInteractionEnabledPhase()
        {
            return RequireCurrentPhase(
                PhaseState.FirstPlayback,
                PhaseState.Active,
                PhaseState.ReplayPlayback
            );
        }

        private void EnsureState(RunState expected)
        {
            if (State != expected)
            {
                throw new InvalidOperationException(
                    $"Run is {State}; expected {expected}."
                );
            }
        }

        private static void ValidatePhaseTime(
            MutablePhase phase,
            TimeSpan monotonicTime)
        {
            ValidateMonotonicTime(monotonicTime, nameof(monotonicTime));
            if (!phase.FirstPlaybackStartedAt.HasValue ||
                monotonicTime < phase.FirstPlaybackStartedAt.Value)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicTime),
                    "Phase completion cannot precede first playback start."
                );
            }
        }

        private static void ValidateMonotonicTime(
            TimeSpan monotonicTime,
            string parameterName)
        {
            if (monotonicTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private InteractionRunResult BuildResult(RunResult result)
        {
            return new InteractionRunResult(
                result,
                plan,
                phases.Select(phase => phase.Snapshot()),
                abortReason,
                faultReason
            );
        }

        private void ResetPhaseExecutions()
        {
            for (int index = 0; index < phases.Length; index++)
            {
                phases[index].Reset();
            }
        }

        private sealed class MutablePhase
        {
            public MutablePhase(int phaseId)
            {
                PhaseId = phaseId;
                Reset();
            }

            public int PhaseId { get; }

            public PhaseState State { get; set; }

            public PhaseResult? Result { get; set; }

            public bool FirstPlaybackCompleted { get; set; }

            public bool ReplayUsed { get; set; }

            public bool TimeoutRecorded { get; set; }

            public int TaskProgress { get; set; }

            public int InteractionErrorCount { get; set; }

            public TimeSpan? FirstPlaybackStartedAt { get; set; }

            public void BeginFirstPlayback(TimeSpan monotonicTime)
            {
                State = PhaseState.FirstPlayback;
                FirstPlaybackStartedAt = monotonicTime;
            }

            public void Reset()
            {
                State = PhaseState.Inactive;
                Result = null;
                FirstPlaybackCompleted = false;
                ReplayUsed = false;
                TimeoutRecorded = false;
                TaskProgress = 0;
                InteractionErrorCount = 0;
                FirstPlaybackStartedAt = null;
            }

            public PhaseExecutionSnapshot Snapshot()
            {
                return new PhaseExecutionSnapshot(
                    PhaseId,
                    State,
                    Result,
                    FirstPlaybackCompleted,
                    ReplayUsed,
                    TimeoutRecorded,
                    TaskProgress,
                    InteractionErrorCount,
                    FirstPlaybackStartedAt
                );
            }
        }
    }
}
