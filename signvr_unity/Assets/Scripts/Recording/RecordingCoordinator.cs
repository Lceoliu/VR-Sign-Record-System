using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SignVR.Recording
{
    [DefaultExecutionOrder(-10000)]
    public sealed class RecordingCoordinator : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField]
        private MetaBodyMotionRecorder recorder;

        [SerializeField]
        private RecordingTeacherUI teacherUI;

        [SerializeField]
        private RecordingViewpointController viewpointController;

        [Header("Timing")]
        [SerializeField]
        [Min(0f)]
        private float countdownSeconds = 2f;

        [Header("Local Bootstrap")]
        [SerializeField]
        private bool loadInitialPromptOnAwake = true;

        [SerializeField]
        private string initialSessionId = "local-session";

        [SerializeField]
        private string initialSentenceId = "sentence-001";

        [SerializeField]
        [TextArea(2, 4)]
        private string initialPromptText = "Current sentence will appear here.";

        [SerializeField]
        [Min(1)]
        private int initialTakeIndex = 1;

        private RecordingFlowStateMachine stateMachine;
        private Coroutine activeRoutine;
        private int nextTakeIndex;
        private double recordingStartedAt;
        private RecordingFlowState reviewReturnState;
        private bool dependenciesBound;
        private bool hasPendingCompletedArtifact;
        private MetaBodyMotionRecorder.RecordingArtifact pendingCompletedArtifact;
        private bool manualPaused;
        private bool applicationPaused;

        public event Action PresentationChanged;

        /// <summary>
        /// Raised after the recorder has flushed a take and written its metadata.
        /// Interrupted takes are included so upload and diagnostics can observe
        /// every artifact without inferring completion from presentation updates.
        /// </summary>
        public event Action<MetaBodyMotionRecorder.RecordingArtifact>
            RecordingFinalized;

        /// <summary>
        /// Raised only for a normally completed take, after the flow state has
        /// reached Completed. Consumers may safely load the next prompt here.
        /// </summary>
        public event Action<MetaBodyMotionRecorder.RecordingArtifact>
            TakeCompleted;

        /// <summary>
        /// Time of the last pedal press. The teacher cannot hear the pedal click,
        /// so the UI flashes unconditionally on every press to confirm it landed,
        /// independently of whether the press also changed the flow state.
        /// </summary>
        public float LastPedalPulseTime { get; private set; } = float.NegativeInfinity;

        /// <summary>
        /// True while the headset shows passthrough. Recording is suspended so the
        /// teacher can look at the interpreter without ending the session.
        /// </summary>
        public bool IsPaused => manualPaused || applicationPaused;

        public bool IsHelpRequested { get; private set; }

        public RecordingFlowState State => stateMachine != null
            ? stateMachine.State
            : RecordingFlowState.Disconnected;
        public string SessionId { get; private set; } = string.Empty;
        public string SentenceId { get; private set; } = string.Empty;
        public string PromptText { get; private set; } = string.Empty;
        public RecordingTakeContext CurrentTake { get; private set; }
        public float CountdownRemaining { get; private set; }
        public float ResetHoldProgress { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public bool HasLastArtifact { get; private set; }
        public MetaBodyMotionRecorder.RecordingArtifact LastArtifact { get; private set; }

        public float RecordingElapsedSeconds =>
            State == RecordingFlowState.Recording
                ? (float)(Time.realtimeSinceStartupAsDouble - recordingStartedAt)
                : 0f;

        private void Awake()
        {
            InitializeStateMachine();

            BindDependencies();

            if (loadInitialPromptOnAwake && recorder != null)
            {
                LoadPrompt(
                    initialSessionId,
                    initialSentenceId,
                    initialPromptText,
                    initialTakeIndex
                );
            }
        }

        /// <summary>
        /// Runtime bootstrap entry point.  Keeping dependency injection here
        /// lets a scene create the recording stack without duplicating the XR
        /// rig or relying on fragile name-based lookups in every scene.
        /// </summary>
        public void Configure(
            MetaBodyMotionRecorder recordingRecorder,
            RecordingTeacherUI recordingTeacherUI = null,
            bool loadDefaultPromptOnAwake = false)
        {
            if (recordingRecorder == null)
            {
                throw new ArgumentNullException(nameof(recordingRecorder));
            }

            recorder = recordingRecorder;
            teacherUI = recordingTeacherUI;
            loadInitialPromptOnAwake = loadDefaultPromptOnAwake;
            InitializeStateMachine();
            BindDependencies();
        }

        public void ConfigureViewpointController(
            RecordingViewpointController recordingViewpointController)
        {
            viewpointController = recordingViewpointController;
        }

        private void InitializeStateMachine()
        {
            if (stateMachine != null)
            {
                return;
            }

            stateMachine = new RecordingFlowStateMachine();
            stateMachine.StateChanged += HandleStateChanged;
        }

        private void BindDependencies()
        {
            if (dependenciesBound || recorder == null)
            {
                return;
            }

            recorder.RecordingFinalized += HandleRecordingFinalized;
            if (teacherUI != null)
            {
                teacherUI.Bind(this);
            }

            dependenciesBound = true;
        }

        public bool LoadPrompt(
            string sessionId,
            string sentenceId,
            string promptText,
            int startingTakeIndex = 1)
        {
            BindDependencies();
            if (recorder == null)
            {
                LastError = "Meta Body Motion Recorder is not assigned.";
                stateMachine.Fail();
                NotifyPresentationChanged();
                return false;
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(sentenceId))
            {
                throw new ArgumentException("Sentence ID is required.", nameof(sentenceId));
            }

            if (startingTakeIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(startingTakeIndex));
            }

            if (!stateMachine.LoadPrompt())
            {
                return false;
            }

            SessionId = sessionId;
            SentenceId = sentenceId;
            PromptText = promptText ?? string.Empty;
            CurrentTake = default;
            hasPendingCompletedArtifact = false;
            pendingCompletedArtifact = default;
            HasLastArtifact = recorder.TryFindLatestArtifact(
                sessionId,
                sentenceId,
                out MetaBodyMotionRecorder.RecordingArtifact restoredArtifact
            );
            LastArtifact = HasLastArtifact ? restoredArtifact : default;
            nextTakeIndex = HasLastArtifact
                ? Mathf.Max(startingTakeIndex, restoredArtifact.Take.TakeIndex + 1)
                : startingTakeIndex;
            CountdownRemaining = 0f;
            ResetHoldProgress = 0f;
            LastError = string.Empty;
            if (HasLastArtifact)
            {
                Debug.Log(
                    "[RecordingCoordinator] Restored replay candidate " +
                    $"{restoredArtifact.Take.TakeId} from local storage."
                );
            }
            NotifyPresentationChanged();
            return true;
        }

        public bool BeginCurrentTake()
        {
            RecordingTakeContext take = RecordingTakeContext.CreateLocal(
                SessionId,
                SentenceId,
                PromptText,
                nextTakeIndex
            );

            return BeginTake(take, countdownSeconds);
        }

        public bool BeginRemoteTake(
            RecordingTakeContext take,
            float remoteCountdownSeconds)
        {
            if (!take.IsValid)
            {
                throw new ArgumentException("Remote take is invalid.", nameof(take));
            }

            float delaySeconds = remoteCountdownSeconds > 0f
                ? remoteCountdownSeconds
                : countdownSeconds;

            return BeginTake(take, delaySeconds);
        }

        private bool BeginTake(
            RecordingTakeContext take,
            float delaySeconds)
        {
            if (IsPaused)
            {
                LastError = "已暂停，请先恢复场景";
                NotifyPresentationChanged();
                return false;
            }

            if (State != RecordingFlowState.Ready)
            {
                return false;
            }

            if (viewpointController != null &&
                !viewpointController.PrepareCurrentViewpointForTake(
                    out string viewpointError))
            {
                LastError = viewpointError;
                NotifyPresentationChanged();
                return false;
            }

            if (!stateMachine.BeginCountdown())
            {
                return false;
            }

            StopActiveRoutine();
            CurrentTake = take;
            CountdownRemaining = Mathf.Max(0f, delaySeconds);
            LastError = string.Empty;
            activeRoutine = StartCoroutine(CountdownRoutine());
            return true;
        }

        public bool StopCurrentTake(DateTime? stoppedAtUtc = null)
        {
            if (State == RecordingFlowState.Reviewing)
            {
                return EndReview();
            }

            if (State == RecordingFlowState.Countdown)
            {
                StopActiveRoutine();
                CountdownRemaining = 0f;
                CurrentTake = default;
                return stateMachine.CancelCountdown();
            }

            if (!stateMachine.BeginFinalizing())
            {
                return false;
            }

            if (recorder == null)
            {
                LastError = "Meta Body Motion Recorder is not assigned.";
                stateMachine.Fail();
                return false;
            }

            if (stoppedAtUtc.HasValue)
            {
                recorder.StopRecording(stoppedAtUtc.Value);
            }
            else
            {
                recorder.StopRecording();
            }
            StopActiveRoutine();
            if (State == RecordingFlowState.Finalizing)
            {
                activeRoutine = StartCoroutine(
                    CompleteFinalizingNextFrame()
                );
            }
            return true;
        }

        public bool TryBeginReview()
        {
            if (
                !HasLastArtifact ||
                string.IsNullOrWhiteSpace(LastArtifact.PosePath) ||
                !File.Exists(LastArtifact.PosePath)
            )
            {
                return false;
            }

            reviewReturnState = State;
            return stateMachine.BeginReview();
        }

        public bool EndReview()
        {
            return stateMachine.CompleteReview(
                reviewReturnState == RecordingFlowState.Completed
            );
        }

        public void PulsePedal()
        {
            LastPedalPulseTime = Time.unscaledTime;
            NotifyPresentationChanged();
        }

        public void SetPaused(bool paused)
        {
            if (manualPaused == paused)
            {
                return;
            }

            manualPaused = paused;

            if (paused && State == RecordingFlowState.Recording)
            {
                recorder?.StopRecordingForPassthrough();
            }
            else if (paused && State == RecordingFlowState.Countdown)
            {
                StopCurrentTake();
            }

            NotifyPresentationChanged();
        }

        public void SetHelpRequested(bool requested)
        {
            if (IsHelpRequested == requested)
            {
                return;
            }

            IsHelpRequested = requested;
            NotifyPresentationChanged();
        }

        public void SetResetHoldProgress(float progress)
        {
            ResetHoldProgress = Mathf.Clamp01(progress);
            NotifyPresentationChanged();
        }

        public void CancelResetHold()
        {
            if (ResetHoldProgress <= 0f)
            {
                return;
            }

            ResetHoldProgress = 0f;
            NotifyPresentationChanged();
        }

        public void CompleteResetHold()
        {
            ResetHoldProgress = 1f;
            ResetCurrentPrompt();
        }

        public void ResetCurrentPrompt(DateTime? stoppedAtUtc = null)
        {
            StopActiveRoutine();
            stateMachine.BeginReset();
            hasPendingCompletedArtifact = false;
            pendingCompletedArtifact = default;

            if (recorder != null && recorder.IsRecording)
            {
                recorder.StopRecordingAsInterrupted(stoppedAtUtc);
            }

            CurrentTake = default;
            CountdownRemaining = 0f;
            LastError = string.Empty;
            activeRoutine = StartCoroutine(CompleteResetNextFrame());
        }

        private IEnumerator CountdownRoutine()
        {
            while (viewpointController != null &&
                   viewpointController.IsAlignmentPending)
            {
                yield return null;
            }

            if (!ValidateViewpointForTake())
            {
                activeRoutine = null;
                yield break;
            }

            while (CountdownRemaining > 0f)
            {
                NotifyPresentationChanged();
                yield return null;
                CountdownRemaining = Mathf.Max(
                    0f,
                    CountdownRemaining - Time.unscaledDeltaTime
                );
            }

            if (!ValidateViewpointForTake())
            {
                activeRoutine = null;
                yield break;
            }

            RecordingTakeContext take = CurrentTake;

            if (recorder == null || !recorder.TryStartRecording(take))
            {
                LastError = recorder != null &&
                            !string.IsNullOrWhiteSpace(recorder.LastError)
                    ? recorder.LastError
                    : "Body tracking is not ready.";
                CurrentTake = default;
                CountdownRemaining = 0f;
                stateMachine.CancelCountdown();
                NotifyPresentationChanged();
                activeRoutine = null;
                yield break;
            }

            CurrentTake = take;
            nextTakeIndex = Mathf.Max(nextTakeIndex, take.TakeIndex + 1);
            recordingStartedAt = Time.realtimeSinceStartupAsDouble;
            stateMachine.BeginRecording();
            activeRoutine = null;
        }

        private bool ValidateViewpointForTake()
        {
            if (viewpointController == null ||
                viewpointController.TryValidateCurrentViewpointForTake(
                    out string error))
            {
                return true;
            }

            LastError = error;
            CurrentTake = default;
            CountdownRemaining = 0f;
            stateMachine.CancelCountdown();
            NotifyPresentationChanged();
            return false;
        }

        private IEnumerator CompleteFinalizingNextFrame()
        {
            yield return null;
            bool completed = stateMachine.CompleteFinalizing();
            activeRoutine = null;

            if (!completed || !hasPendingCompletedArtifact)
            {
                yield break;
            }

            MetaBodyMotionRecorder.RecordingArtifact artifact =
                pendingCompletedArtifact;
            hasPendingCompletedArtifact = false;
            pendingCompletedArtifact = default;
            TakeCompleted?.Invoke(artifact);
        }

        private IEnumerator CompleteResetNextFrame()
        {
            yield return null;
            ResetHoldProgress = 0f;
            stateMachine.CompleteReset();
            activeRoutine = null;
        }

        private void HandleStateChanged(RecordingFlowState _)
        {
            NotifyPresentationChanged();
        }

        private void NotifyPresentationChanged()
        {
            PresentationChanged?.Invoke();
        }

        private void HandleRecordingFinalized(
            MetaBodyMotionRecorder.RecordingArtifact artifact)
        {
            LastArtifact = artifact;
            HasLastArtifact = true;
            RecordingFinalized?.Invoke(artifact);

            bool belongsToCurrentTake =
                CurrentTake.IsValid &&
                string.Equals(
                    CurrentTake.TakeId,
                    artifact.Take.TakeId,
                    StringComparison.Ordinal
                );
            if (
                State == RecordingFlowState.Finalizing &&
                belongsToCurrentTake &&
                string.Equals(
                    artifact.CaptureStatus,
                    "completed",
                    StringComparison.Ordinal
                )
            )
            {
                pendingCompletedArtifact = artifact;
                hasPendingCompletedArtifact = true;
            }
            else if (belongsToCurrentTake &&
                     !string.Equals(
                         artifact.CaptureStatus,
                         "completed",
                         StringComparison.Ordinal))
            {
                hasPendingCompletedArtifact = false;
                pendingCompletedArtifact = default;
                LastError = BuildRetryMessage(artifact.CaptureStatus);

                if (State == RecordingFlowState.Recording)
                {
                    stateMachine.InterruptRecording();
                }
                else if (State == RecordingFlowState.Finalizing)
                {
                    stateMachine.AbortFinalizing();
                }

                CurrentTake = default;
                CountdownRemaining = 0f;
            }

            NotifyPresentationChanged();
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused && State == RecordingFlowState.Countdown)
            {
                StopCurrentTake();
            }

            NotifyPresentationChanged();
        }

        private static string BuildRetryMessage(string captureStatus)
        {
            switch (captureStatus)
            {
                case "invalid_pose_quality":
                    return "录制无效，请重录当前句";
                case "interrupted_application_pause":
                    return "录制被暂停，请重录当前句";
                case "interrupted_passthrough":
                    return "透视暂停了录制，请重录当前句";
                case "io_error":
                    return "写盘失败，请检查空间后重录";
                case "capture_error":
                    return "姿态采样失败，请重录当前句";
                default:
                    return "录制被中断，请重录当前句";
            }
        }

        private void StopActiveRoutine()
        {
            if (activeRoutine == null)
            {
                return;
            }

            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        private void OnDestroy()
        {
            if (stateMachine != null)
            {
                stateMachine.StateChanged -= HandleStateChanged;
            }

            if (dependenciesBound && recorder != null)
            {
                recorder.RecordingFinalized -= HandleRecordingFinalized;
            }
        }
    }
}
