using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Streaming;
using TMPro;
using UnityEngine;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Dedicated picture-frame-to-chest take. It is deliberately isolated
    /// from the normal phase-4 safe/chest flow so releasing a frame cannot be
    /// converted into an invisible proxy click or reset by phase transitions.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-80)]
    public sealed class FrameChestRecordingSequenceController : MonoBehaviour
    {
        private enum SequenceState
        {
            Idle,
            WaitingForFrame,
            PasswordEntry,
            OpeningChest,
            Completed
        }

        [SerializeField] private InstructionPresentationController presentation;
        [SerializeField] private InteractionPhaseCoordinator phaseCoordinator;
        [SerializeField] private InteractionStudyFlowController studyFlowController;
        [SerializeField] private SpectatorViewStreamer recorder;
        [SerializeField] private InteractionDeterministicPresentation deterministic;
        [SerializeField] private InteractionTargetBinding[] frames = Array.Empty<InteractionTargetBinding>();
        [SerializeField] private FrameChestPasswordButton[] passwordButtons = Array.Empty<FrameChestPasswordButton>();
        [SerializeField] private TMP_Text[] wallPasswordDisplays = Array.Empty<TMP_Text>();
        [SerializeField] private GameObject passwordPanel;
        [SerializeField, Min(0f)] private float recordingTailSeconds = 0.8f;

        private SequenceState state;
        private string activeFrameId = string.Empty;
        private int passwordProgress;
        private double chestCompletionAt = double.NaN;
        private bool eventHubBound;
        private bool recordingArmed;
        private Vector3 lastFramePosition;
        private Vector3 frameGrabStartPosition;
        private Vector3 releaseVelocity;
        private bool frameGrabbed;
        private bool wallPasswordRevealed;
        private Coroutine releaseFrameRoutine;
        private string[] expectedPassword = { "blue", "red", "yellow", "green" };

        public bool IsPasswordInputAvailable => state == SequenceState.PasswordEntry;
        public bool IsRecordingSequenceActive => state != SequenceState.Idle &&
            state != SequenceState.Completed;
        public int PasswordProgress => passwordProgress;
        public string ActiveFrameId => activeFrameId;

        public void Configure(
            InstructionPresentationController configuredPresentation,
            InteractionPhaseCoordinator configuredPhaseCoordinator,
            InteractionStudyFlowController configuredStudyFlowController,
            SpectatorViewStreamer configuredRecorder,
            InteractionDeterministicPresentation configuredDeterministic,
            InteractionTargetBinding[] configuredFrames,
            FrameChestPasswordButton[] configuredButtons,
            TMP_Text[] configuredWallPasswordDisplays,
            GameObject configuredPasswordPanel)
        {
            Unbind();
            presentation = configuredPresentation ?? throw new ArgumentNullException(nameof(configuredPresentation));
            phaseCoordinator = configuredPhaseCoordinator ?? throw new ArgumentNullException(nameof(configuredPhaseCoordinator));
            studyFlowController = configuredStudyFlowController ?? throw new ArgumentNullException(nameof(configuredStudyFlowController));
            recorder = configuredRecorder ?? throw new ArgumentNullException(nameof(configuredRecorder));
            deterministic = configuredDeterministic ?? throw new ArgumentNullException(nameof(configuredDeterministic));
            frames = configuredFrames ?? Array.Empty<InteractionTargetBinding>();
            passwordButtons = configuredButtons ?? Array.Empty<FrameChestPasswordButton>();
            wallPasswordDisplays = configuredWallPasswordDisplays ?? Array.Empty<TMP_Text>();
            passwordPanel = configuredPasswordPanel;
            Bind();
        }

        private void Awake()
        {
            Bind();
            SetPasswordPanel(false);
        }

        private void OnEnable() => Bind();

        private void Update()
        {
            BindEventHubIfNeeded();
            if (frameGrabbed && TryFindActiveFrame(out InteractionTargetBinding frame))
            {
                Vector3 current = frame.transform.position;
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                releaseVelocity = Vector3.Lerp(
                    releaseVelocity,
                    (current - lastFramePosition) / dt,
                    0.35f
                );
                lastFramePosition = current;
                if (!wallPasswordRevealed &&
                    Vector3.Distance(current, frameGrabStartPosition) >= 0.05f)
                {
                    RevealActiveWallPassword();
                }
            }

            if (state == SequenceState.OpeningChest && deterministic != null &&
                !deterministic.ChestLid.IsOpening && deterministic.ChestLid.IsOpen &&
                double.IsNaN(chestCompletionAt))
            {
                chestCompletionAt = Time.realtimeSinceStartupAsDouble +
                    recordingTailSeconds;
            }
            if (!double.IsNaN(chestCompletionAt) &&
                Time.realtimeSinceStartupAsDouble >= chestCompletionAt)
            {
                chestCompletionAt = double.NaN;
                state = SequenceState.Completed;
                recordingArmed = false;
                recorder?.StopLocalRecordingTake();
                Debug.Log(
                    "[FrameChestRecordingSequence] Picture-frame task " +
                    "recording stopped after the chest opened.",
                    this
                );
                if (TryFindActiveFrame(out InteractionTargetBinding completedFrame))
                {
                    // Phase 3 is authoritative only after the entire physical
                    // frame/password/chest sequence has completed.
                    completedFrame.Grab();
                    completedFrame.GrabEnded();
                }
            }
        }

        private void Bind()
        {
            if (!isActiveAndEnabled || presentation == null)
            {
                return;
            }
            presentation.PhasePresentationBegan -= HandlePhasePresentationBegan;
            presentation.PhasePresentationBegan += HandlePhasePresentationBegan;
            presentation.InstructionPlaybackStarted -= HandlePlaybackStarted;
            presentation.InstructionPlaybackStarted += HandlePlaybackStarted;
            BindEventHubIfNeeded();
        }

        private void BindEventHubIfNeeded()
        {
            if (eventHubBound || VRInteractionEvents.Instance == null)
            {
                return;
            }
            VRInteractionEvents.Instance.GrabStarted += HandleGrabStarted;
            VRInteractionEvents.Instance.GrabEnded += HandleGrabEnded;
            eventHubBound = true;
        }

        private void Unbind()
        {
            if (presentation != null)
            {
                presentation.PhasePresentationBegan -= HandlePhasePresentationBegan;
                presentation.InstructionPlaybackStarted -= HandlePlaybackStarted;
            }
            if (eventHubBound && VRInteractionEvents.Instance != null)
            {
                VRInteractionEvents.Instance.GrabStarted -= HandleGrabStarted;
                VRInteractionEvents.Instance.GrabEnded -= HandleGrabEnded;
            }
            eventHubBound = false;
        }

        private void HandlePhasePresentationBegan(RunPhasePlan plan)
        {
            if (plan == null || plan.PhaseId != 3)
            {
                AbortSequence();
                return;
            }
            activeFrameId = plan.TaskVariant.TargetIds.Count > 0
                ? plan.TaskVariant.TargetIds[0]
                : string.Empty;
            passwordProgress = 0;
            IReadOnlyList<string> plannedOrder = phaseCoordinator?.Plan?
                .ChestButtonOrder?.ButtonIds;
            expectedPassword = plannedOrder != null && plannedOrder.Count == 4
                ? new List<string>(plannedOrder).ToArray()
                : new[] { "blue", "red", "yellow", "green" };
            frameGrabbed = false;
            wallPasswordRevealed = false;
            releaseVelocity = Vector3.zero;
            state = SequenceState.WaitingForFrame;
            recordingArmed = true;
            chestCompletionAt = double.NaN;
            SetPasswordPanel(false);
            UpdateWallPasswordDisplays();
        }

        private void HandlePlaybackStarted(
            InstructionPlaybackPass pass,
            double startedAt)
        {
            if (!recordingArmed || pass != InstructionPlaybackPass.First ||
                state != SequenceState.WaitingForFrame)
            {
                return;
            }

            recordingArmed = false;
            recorder?.StartLocalRecordingTake("frame_chest_" + activeFrameId);
            if (recorder == null || !recorder.IsLocalRecordingActive)
            {
                Debug.LogError(
                    "[FrameChestRecordingSequence] Picture-frame recording " +
                    "could not start. " + (recorder?.LastError ??
                        "Recorder reference is missing."),
                    this
                );
                return;
            }
            Debug.Log(
                "[FrameChestRecordingSequence] Picture-frame task recording " +
                $"started with pose playback at {startedAt:F3}.",
                this
            );
        }

        private void HandleGrabStarted(GameObject interactable, GameObject interactor)
        {
            if (state != SequenceState.WaitingForFrame ||
                !MatchesActiveFrame(interactable))
            {
                return;
            }
            frameGrabbed = true;
            if (TryFindActiveFrame(out InteractionTargetBinding frame))
            {
                lastFramePosition = frame.transform.position;
                frameGrabStartPosition = frame.transform.position;
                releaseVelocity = Vector3.zero;
            }
        }

        private void HandleGrabEnded(GameObject interactable, GameObject interactor)
        {
            if (!frameGrabbed || !MatchesActiveFrame(interactable))
            {
                return;
            }
            frameGrabbed = false;
            if (!wallPasswordRevealed)
            {
                RevealActiveWallPassword();
            }
            if (TryFindActiveFrame(out InteractionTargetBinding frame))
            {
                ApplyReleasedFramePhysics(frame, releaseVelocity);
                if (releaseFrameRoutine != null)
                {
                    StopCoroutine(releaseFrameRoutine);
                }
                releaseFrameRoutine = StartCoroutine(
                    ConfirmReleasedFramePhysics(frame, releaseVelocity)
                );
            }
            state = SequenceState.PasswordEntry;
            SetPasswordPanel(true);
        }

        private IEnumerator ConfirmReleasedFramePhysics(
            InteractionTargetBinding frame,
            Vector3 velocity)
        {
            yield return null;
            yield return new WaitForFixedUpdate();
            releaseFrameRoutine = null;
            if (frame != null && state != SequenceState.Idle &&
                state != SequenceState.Completed)
            {
                ApplyReleasedFramePhysics(frame, velocity);
            }
        }

        private static void ApplyReleasedFramePhysics(
            InteractionTargetBinding frame,
            Vector3 velocity)
        {
            Grabbable grabbable = frame.GetComponent<Grabbable>();
            if (grabbable != null)
            {
                grabbable.ForceKinematicDisabled = true;
            }
            Rigidbody body = frame.GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }
            body.isKinematic = false;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.None;
            body.detectCollisions = true;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = Vector3.ClampMagnitude(velocity, 4f);
        }

        public ValidationResult PressPasswordButton(string buttonId)
        {
            if (!IsPasswordInputAvailable || string.IsNullOrWhiteSpace(buttonId))
            {
                return null;
            }
            if (passwordProgress >= expectedPassword.Length)
            {
                return null;
            }
            if (!string.Equals(
                    expectedPassword[passwordProgress],
                    buttonId,
                    StringComparison.OrdinalIgnoreCase))
            {
                passwordProgress = 0;
                ResetButtonVisuals();
                return null;
            }
            passwordProgress++;
            for (int index = 0; index < passwordButtons.Length; index++)
            {
                if (passwordButtons[index] != null &&
                    string.Equals(passwordButtons[index].ButtonId, buttonId, StringComparison.OrdinalIgnoreCase))
                {
                    passwordButtons[index].ShowAccepted();
                }
            }
            if (passwordProgress == expectedPassword.Length)
            {
                state = SequenceState.OpeningChest;
                SetPasswordPanel(false);
                deterministic?.PlayChestOpeningAnimation();
            }
            return null;
        }

        private void SetPasswordPanel(bool visible)
        {
            if (passwordPanel != null)
            {
                passwordPanel.SetActive(visible);
            }
            for (int index = 0; index < passwordButtons.Length; index++)
            {
                FrameChestPasswordButton button = passwordButtons[index];
                if (button != null)
                {
                    button.SetInputAvailable(visible);
                }
            }
        }

        private void ResetButtonVisuals()
        {
            for (int index = 0; index < passwordButtons.Length; index++)
            {
                FrameChestPasswordButton button = passwordButtons[index];
                if (button != null)
                {
                    button.ResetVisual();
                }
            }
        }

        private bool MatchesActiveFrame(GameObject value)
        {
            if (value == null || string.IsNullOrEmpty(activeFrameId))
            {
                return false;
            }
            for (int index = 0; index < frames.Length; index++)
            {
                InteractionTargetBinding frame = frames[index];
                if (frame != null && frame.TargetId == activeFrameId &&
                    (value == frame.gameObject || value.transform.IsChildOf(frame.transform)))
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryFindActiveFrame(out InteractionTargetBinding result)
        {
            for (int index = 0; index < frames.Length; index++)
            {
                if (frames[index] != null && frames[index].TargetId == activeFrameId)
                {
                    result = frames[index];
                    return true;
                }
            }
            result = null;
            return false;
        }

        private void AbortSequence()
        {
            if (releaseFrameRoutine != null)
            {
                StopCoroutine(releaseFrameRoutine);
                releaseFrameRoutine = null;
            }
            if (recorder != null && recorder.IsLocalRecordingActive)
            {
                recorder.StopLocalRecordingTake();
            }
            state = SequenceState.Idle;
            activeFrameId = string.Empty;
            frameGrabbed = false;
            wallPasswordRevealed = false;
            recordingArmed = false;
            chestCompletionAt = double.NaN;
            SetPasswordPanel(false);
            SetWallPasswordDisplaysVisible(false);
        }

        private void UpdateWallPasswordDisplays()
        {
            var digits = new string[expectedPassword.Length];
            for (int index = 0; index < expectedPassword.Length; index++)
            {
                digits[index] = PasswordDigit(expectedPassword[index]);
            }
            string text = string.Join("   ", digits);
            for (int index = 0; index < wallPasswordDisplays.Length; index++)
            {
                TMP_Text display = wallPasswordDisplays[index];
                if (display == null)
                {
                    continue;
                }
                display.text = text;
                display.gameObject.SetActive(false);
            }
        }

        private void RevealActiveWallPassword()
        {
            wallPasswordRevealed = true;
            for (int index = 0; index < wallPasswordDisplays.Length; index++)
            {
                TMP_Text display = wallPasswordDisplays[index];
                bool matches = display != null && index < frames.Length &&
                    frames[index] != null && string.Equals(
                        frames[index].TargetId,
                        activeFrameId,
                        StringComparison.Ordinal
                    );
                if (display != null)
                {
                    display.gameObject.SetActive(matches);
                }
            }
        }

        private static string PasswordDigit(string buttonId)
        {
            return buttonId?.ToLowerInvariant() switch
            {
                "blue" => "0",
                "red" => "1",
                "yellow" => "2",
                "green" => "3",
                _ => "?"
            };
        }

        private void SetWallPasswordDisplaysVisible(bool visible)
        {
            for (int index = 0; index < wallPasswordDisplays.Length; index++)
            {
                if (wallPasswordDisplays[index] != null)
                {
                    wallPasswordDisplays[index].gameObject.SetActive(visible);
                }
            }
        }

        private void OnDisable()
        {
            Unbind();
            AbortSequence();
        }

        private void OnDestroy() => Unbind();
    }
}
