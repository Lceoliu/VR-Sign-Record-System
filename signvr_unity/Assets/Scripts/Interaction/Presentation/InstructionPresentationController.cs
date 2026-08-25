using System;
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

        public void Configure(
            InstructionGhostPlayer player,
            InteractionPromptPresenter prompt,
            GhostPointingDetector detector,
            InteractionInstructionControls instructionControls = null)
        {
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

            if (presentationState.PhaseActive || currentPhasePlan != null)
            {
                EndPhase();
            }

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
            bool hadPhase = presentationState.PhaseActive ||
                currentPhasePlan != null;
            presentationState.EndPhase();
            pointingDetector?.StopPointing();
            ghostPlayer?.Stop();
            currentPhasePlan = null;
            if (hadPhase)
            {
                StateChanged?.Invoke();
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
            StateChanged?.Invoke();
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

        private void OnDisable()
        {
            EndPhase();
            Unbind();
        }
    }
}
