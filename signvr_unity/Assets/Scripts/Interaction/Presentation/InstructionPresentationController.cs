using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Thin Unity coordinator for W5 presentation only. W7 remains the Run and
    /// task authority; this component never disables phase interactions.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class InstructionPresentationController : MonoBehaviour
    {
        [SerializeField]
        private InstructionGhostPlayer ghostPlayer;

        [SerializeField]
        private InteractionPromptPresenter promptPresenter;

        [SerializeField]
        private GhostPointingDetector pointingDetector;

        [SerializeField]
        private InteractionInstructionControls controls;

        private readonly InstructionPhasePresentationState presentationState =
            new();
        private RunPhasePlan currentPhasePlan;
        private bool bound;
        private bool endingPhase;
        private bool cleanupHadPhase;
        private bool presentationStateCleanupComplete = true;
        private bool promptCleanupComplete = true;
        private bool pointingCleanupComplete = true;
        private bool ghostCleanupComplete = true;
        private bool phasePlanCleanupComplete = true;
        private bool finalNotificationComplete = true;

        public event Action StateChanged;
        public event Action<RunPhasePlan> PhasePresentationBegan;
        public event Action<InstructionPlaybackPass, double>
            InstructionPlaybackStarted;
        public event Action<InstructionPlaybackPass, double>
            InstructionPlaybackCompleted;
        public event Action<double> ReplayAvailable;
        public event Action<double> ReplayUsed;
        public event Action<double> BubbleShown;
        public event Action<double> BubbleHidden;
        public event Action GiveUpPhaseRequested;
        public event Action AbortRunRequested;
        public event Action<string> PresentationFaulted;

        public RunPhasePlan CurrentPhasePlan => currentPhasePlan;

        public AssistanceCondition Condition => presentationState.Condition;

        public bool PhaseActive => presentationState.PhaseActive;

        public bool ReplayIsAvailable => presentationState.ReplayAvailable;

        public bool ReplayWasConsumed => presentationState.ReplayConsumed;

        public bool GiveUpIsAvailable => presentationState.GiveUpAvailable;

        public bool BubbleIsVisible => presentationState.BubbleVisible;

        public bool InteractionsEnabled =>
            presentationState.InteractionsEnabled;

        public InstructionGhostPlayer GhostPlayer => ghostPlayer;

        public InteractionPromptPresenter PromptPresenter => promptPresenter;

        public GhostPointingDetector PointingDetector => pointingDetector;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal bool LifecycleSubscriptionsBoundForTests => bound;
#endif

        public void Configure(
            InstructionGhostPlayer player,
            InteractionPromptPresenter prompt,
            GhostPointingDetector detector,
            InteractionInstructionControls instructionControls = null)
        {
            if (HasActiveOrUnsettledCleanupCycle)
            {
                throw new InvalidOperationException(
                    "Instruction presentation dependencies cannot be replaced " +
                    "until EndPhase completes the current cleanup cycle."
                );
            }
            Unbind();
            ghostPlayer = player;
            promptPresenter = prompt;
            pointingDetector = detector;
            controls = instructionControls;
            pointingDetector?.ConfigurePlayer(ghostPlayer);
            controls?.Configure(this);
            Bind();
        }

        public void BeginPhase(
            RunPhasePlan phasePlan,
            AssistanceCondition assistanceCondition)
        {
            if (phasePlan == null)
            {
                throw new ArgumentNullException(nameof(phasePlan));
            }
            EnsureDependencies();

            if (HasActiveOrUnsettledCleanupCycle)
            {
                EndPhase();
                if (HasActiveOrUnsettledCleanupCycle)
                {
                    throw new InvalidOperationException(
                        "A new phase cannot begin before the previous " +
                        "presentation cleanup cycle converges."
                    );
                }
            }

            BeginCleanupCycle();
            currentPhasePlan = phasePlan;
            presentationState.BeginPhase(assistanceCondition);
            promptPresenter.SetPhase(phasePlan);
            promptPresenter.SetVisible(false);
            pointingDetector.ConfigurePhase(assistanceCondition, phasePlan);
            ghostPlayer.Load(phasePlan);
            PhasePresentationBegan?.Invoke(phasePlan);
            StateChanged?.Invoke();
        }

        /// <summary>
        /// Permanently consumes the allowance before asking the player to start.
        /// </summary>
        public bool Replay()
        {
            if (!presentationState.TryConsumeReplay())
            {
                return false;
            }

            if (!ghostPlayer.Replay())
            {
                HandlePlayerFailed(
                    "Replay was consumed but the frozen artifact could not start."
                );
                return false;
            }
            return true;
        }

        public bool GiveUpPhase()
        {
            if (!presentationState.GiveUpAvailable)
            {
                return false;
            }

            GiveUpPhaseRequested?.Invoke();
            return true;
        }

        /// <summary>
        /// Whole-Run safety request. It is intentionally independent from the
        /// phase Give Up gate and remains callable outside playback.
        /// </summary>
        public void AbortRun()
        {
            AbortRunRequested?.Invoke();
        }

        public void EndPhase()
        {
            bool hadPhase = cleanupHadPhase ||
                presentationState.PhaseActive || currentPhasePlan != null;
            if (!hadPhase && TerminalCleanupComplete)
            {
                return;
            }
            var failures = new List<Exception>();

            endingPhase = true;
            try
            {
                TryCleanupStage(
                    presentationState.EndPhase,
                    () => !presentationState.PhaseActive &&
                        !presentationState.BubbleVisible,
                    ref presentationStateCleanupComplete,
                    failures
                );
            }
            finally
            {
                endingPhase = false;
            }
            TryCleanupStage(
                () => promptPresenter?.SetVisible(false),
                () => promptPresenter == null || !promptPresenter.IsVisible,
                ref promptCleanupComplete,
                failures
            );
            TryCleanupStage(
                () => pointingDetector?.StopPointing(),
                () => pointingDetector == null ||
                    (!pointingDetector.PhaseConfigured &&
                     !pointingDetector.IsPointingVisible),
                ref pointingCleanupComplete,
                failures
            );
            TryCleanupStage(
                () => ghostPlayer?.Stop(),
                () => ghostPlayer == null ||
                    (!ghostPlayer.IsLoading &&
                     ghostPlayer.LoadedContent == null &&
                     !ghostPlayer.IsPlaying &&
                     ghostPlayer.LoadedFrameCount == 0),
                ref ghostCleanupComplete,
                failures
            );
            if (!phasePlanCleanupComplete)
            {
                currentPhasePlan = null;
                phasePlanCleanupComplete = true;
            }
            if (hadPhase && !finalNotificationComplete)
            {
                InvokeEveryHandler(StateChanged, failures);
                // Every subscriber was attempted independently. A subscriber
                // failure is reported once but must not replay subscribers that
                // already observed this terminal transition.
                finalNotificationComplete = true;
            }
            if (TerminalCleanupComplete)
            {
                cleanupHadPhase = false;
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Instruction presentation cleanup failed after every " +
                    "resource cleanup step was attempted.",
                    failures
                );
            }
        }

        private void Awake()
        {
            Bind();
            controls?.Configure(this);
        }

        private void OnEnable()
        {
            Bind();
            controls?.Configure(this);
        }

        private void Update()
        {
            if (presentationState.PhaseActive)
            {
                presentationState.Tick(
                    Time.realtimeSinceStartupAsDouble
                );
            }
        }

        private void Bind()
        {
            if (bound)
            {
                return;
            }

            presentationState.Changed += HandleStateChanged;
            presentationState.BubbleShown += HandleBubbleShown;
            presentationState.BubbleHidden += HandleBubbleHidden;
            presentationState.ReplayBecameAvailable +=
                HandleReplayAvailable;
            presentationState.ReplayUsed += HandleReplayUsed;

            if (ghostPlayer != null)
            {
                ghostPlayer.Loaded += HandlePlayerLoaded;
                ghostPlayer.PlaybackStarted += HandlePlaybackStarted;
                ghostPlayer.Completed += HandlePlaybackCompleted;
                ghostPlayer.Failed += HandlePlayerFailed;
            }
            bound = true;
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }

            presentationState.Changed -= HandleStateChanged;
            presentationState.BubbleShown -= HandleBubbleShown;
            presentationState.BubbleHidden -= HandleBubbleHidden;
            presentationState.ReplayBecameAvailable -=
                HandleReplayAvailable;
            presentationState.ReplayUsed -= HandleReplayUsed;

            if (ghostPlayer != null)
            {
                ghostPlayer.Loaded -= HandlePlayerLoaded;
                ghostPlayer.PlaybackStarted -= HandlePlaybackStarted;
                ghostPlayer.Completed -= HandlePlaybackCompleted;
                ghostPlayer.Failed -= HandlePlayerFailed;
            }
            bound = false;
        }

        private void HandlePlayerLoaded(InstructionContentReference content)
        {
            if (!presentationState.PhaseActive || currentPhasePlan == null ||
                !ReferenceEquals(content, currentPhasePlan.Content))
            {
                ghostPlayer.Stop();
                return;
            }

            if (!ghostPlayer.Play())
            {
                HandlePlayerFailed(
                    "The loaded first instruction playback could not start."
                );
            }
        }

        private void HandlePlaybackStarted(InstructionPlaybackPass pass)
        {
            InstructionPlaybackStarted?.Invoke(
                pass,
                Time.realtimeSinceStartupAsDouble
            );
            StateChanged?.Invoke();
        }

        private void HandlePlaybackCompleted(InstructionPlaybackPass pass)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (pass == InstructionPlaybackPass.First)
            {
                presentationState.FirstPlaybackCompleted(now);
            }
            else if (pass == InstructionPlaybackPass.Replay)
            {
                presentationState.ReplayCompleted();
            }
            else
            {
                HandlePlayerFailed(
                    "Instruction playback completed without a valid pass."
                );
                return;
            }

            InstructionPlaybackCompleted?.Invoke(pass, now);
            StateChanged?.Invoke();
        }

        private void HandleBubbleShown()
        {
            promptPresenter?.SetVisible(true);
            BubbleShown?.Invoke(Time.realtimeSinceStartupAsDouble);
        }

        private void HandleBubbleHidden()
        {
            promptPresenter?.SetVisible(false);
            BubbleHidden?.Invoke(Time.realtimeSinceStartupAsDouble);
        }

        private void HandleReplayAvailable()
        {
            ReplayAvailable?.Invoke(Time.realtimeSinceStartupAsDouble);
        }

        private void HandleReplayUsed()
        {
            ReplayUsed?.Invoke(Time.realtimeSinceStartupAsDouble);
        }

        private void HandleStateChanged()
        {
            if (!endingPhase)
            {
                StateChanged?.Invoke();
            }
        }

        private void HandlePlayerFailed(string error)
        {
            PresentationFaulted?.Invoke(error);
            StateChanged?.Invoke();
        }

        private void EnsureDependencies()
        {
            if (ghostPlayer == null || promptPresenter == null ||
                pointingDetector == null)
            {
                throw new InvalidOperationException(
                    "Instruction presentation dependencies are not configured."
                );
            }
        }

        private static void TryCleanup(
            Action cleanup,
            ICollection<Exception> failures)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private static void TryCleanupStage(
            Action cleanup,
            Func<bool> isComplete,
            ref bool complete,
            ICollection<Exception> failures)
        {
            if (complete)
            {
                return;
            }

            try
            {
                cleanup();
                complete = true;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                try
                {
                    complete = isComplete();
                }
                catch (Exception inspectionFailure)
                {
                    failures.Add(inspectionFailure);
                    complete = false;
                }
            }
        }

        private bool ResourceCleanupComplete =>
            presentationStateCleanupComplete && promptCleanupComplete &&
            pointingCleanupComplete && ghostCleanupComplete &&
            phasePlanCleanupComplete;

        private bool TerminalCleanupComplete =>
            ResourceCleanupComplete && finalNotificationComplete;

        private bool HasActiveOrUnsettledCleanupCycle =>
            presentationState.PhaseActive || currentPhasePlan != null ||
            cleanupHadPhase || !TerminalCleanupComplete;

        private void BeginCleanupCycle()
        {
            cleanupHadPhase = true;
            presentationStateCleanupComplete = false;
            promptCleanupComplete = false;
            pointingCleanupComplete = false;
            ghostCleanupComplete = false;
            phasePlanCleanupComplete = false;
            finalNotificationComplete = false;
        }

        private static void InvokeEveryHandler(
            Action handlers,
            ICollection<Exception> failures)
        {
            if (handlers == null)
            {
                return;
            }

            Delegate[] invocationList = handlers.GetInvocationList();
            for (int index = 0; index < invocationList.Length; index++)
            {
                TryCleanup((Action)invocationList[index], failures);
            }
        }

        private void OnDisable()
        {
            try
            {
                EndPhase();
            }
            finally
            {
                Unbind();
            }
        }
    }
}
