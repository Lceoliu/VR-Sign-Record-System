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
        private bool reportRemoteStartResult;

        public event Action PresentationChanged;
        public event Action<RecordingTakeContext, bool, long, string> TakeStartResolved;

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
        public bool IsPaused { get; private set; }

        public bool IsHelpRequested { get; private set; }

        public RecordingFlowState State => stateMachine.State;
        public string SessionId { get; private set; } = string.Empty;
        public string SentenceId { get; private set; } = string.Empty;
        public string PromptText { get; private set; } = string.Empty;
        public string PreviousPromptText { get; private set; } = string.Empty;
        public string NextPromptText { get; private set; } = string.Empty;
        public int SentenceIndex { get; private set; }
        public int TotalSentences { get; private set; }
        public string SigningMode { get; private set; } = string.Empty;
        public string ModeSwitchNotice { get; private set; } = string.Empty;
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
            stateMachine = new RecordingFlowStateMachine();
            stateMachine.StateChanged += HandleStateChanged;

            if (recorder == null)
            {
                LastError = "Meta Body Motion Recorder is not assigned.";
                Debug.LogError("[RecordingCoordinator] " + LastError);
                stateMachine.Fail();
                enabled = false;
                return;
            }

            recorder.RecordingFinalized += HandleRecordingFinalized;

            if (teacherUI != null)
            {
                teacherUI.Bind(this);
            }

            if (loadInitialPromptOnAwake)
            {
                LoadPrompt(
                    initialSessionId,
                    initialSentenceId,
                    initialPromptText,
                    initialTakeIndex
                );
            }
        }

        public bool LoadPrompt(
            string sessionId,
            string sentenceId,
            string promptText,
            int startingTakeIndex = 1)
        {
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

        public void ApplyPromptContext(
            string previousPrompt,
            string prompt,
            string nextPrompt,
            int sentenceIndex,
            int totalSentences,
            string signingMode,
            string modeSwitchNotice)
        {
            PreviousPromptText = previousPrompt ?? string.Empty;
            PromptText = prompt ?? string.Empty;
            NextPromptText = nextPrompt ?? string.Empty;
            SentenceIndex = Mathf.Max(0, sentenceIndex);
            TotalSentences = Mathf.Max(0, totalSentences);
            SigningMode = signingMode ?? string.Empty;
            ModeSwitchNotice = modeSwitchNotice ?? string.Empty;
            NotifyPresentationChanged();
        }

        public bool BeginCurrentTake()
        {
            RecordingTakeContext take = RecordingTakeContext.CreateLocal(
                SessionId,
                SentenceId,
                PromptText,
                nextTakeIndex
            );

            return BeginTake(take, countdownSeconds, false);
        }

        public bool BeginRemoteTake(
            RecordingTakeContext take,
            float countdownDurationSeconds)
        {
            if (!take.IsValid)
            {
                throw new ArgumentException("Remote take is invalid.", nameof(take));
            }

            return BeginTake(take, countdownDurationSeconds, true);
        }

        private bool BeginTake(
            RecordingTakeContext take,
            float delaySeconds,
            bool notifyRemoteResult)
        {
            if (IsPaused)
            {
                LastError = "已暂停，请先恢复场景";
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
            reportRemoteStartResult = notifyRemoteResult;
            activeRoutine = StartCoroutine(CountdownRoutine());
            return true;
        }

        public bool StopCurrentTake()
        {
            if (State == RecordingFlowState.Reviewing)
            {
                return EndReview();
            }

            if (State == RecordingFlowState.Countdown)
            {
                StopActiveRoutine();
                CountdownRemaining = 0f;
                ReportTakeStartResult(false, "countdown_cancelled");
                CurrentTake = default;
                return stateMachine.CancelCountdown();
            }

            if (!stateMachine.BeginFinalizing())
            {
                return false;
            }

            recorder.StopRecording();
            StopActiveRoutine();
            activeRoutine = StartCoroutine(CompleteFinalizingNextFrame());
            return true;
        }

        public bool StopCurrentTakeAsInterrupted(string reason)
        {
            if (State == RecordingFlowState.Countdown)
            {
                StopActiveRoutine();
                CountdownRemaining = 0f;
                LastError = reason ?? string.Empty;
                ReportTakeStartResult(false, LastError);
                CurrentTake = default;
                return stateMachine.CancelCountdown();
            }

            if (State != RecordingFlowState.Recording || !stateMachine.BeginFinalizing())
            {
                return false;
            }

            LastError = reason ?? string.Empty;
            recorder.StopRecordingAsInterrupted();
            StopActiveRoutine();
            activeRoutine = StartCoroutine(CompleteFinalizingNextFrame());
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
            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;

            // Leaving a take half-recorded is worse than losing it: stop cleanly
            // so the partial take is still saved and traceable.
            if (paused && State == RecordingFlowState.Recording)
            {
                StopCurrentTake();
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

        public void ResetCurrentPrompt()
        {
            StopActiveRoutine();
            stateMachine.BeginReset();

            if (recorder.IsRecording)
            {
                recorder.StopRecordingAsInterrupted();
            }

            CurrentTake = default;
            CountdownRemaining = 0f;
            reportRemoteStartResult = false;
            LastError = string.Empty;
            activeRoutine = StartCoroutine(CompleteResetNextFrame());
        }

        private IEnumerator CountdownRoutine()
        {
            // Let the gateway return the scheduling ACK before a zero-delay take can
            // resolve on this same Unity frame.
            yield return null;

            while (CountdownRemaining > 0f)
            {
                NotifyPresentationChanged();
                yield return null;
                CountdownRemaining = Mathf.Max(
                    0f,
                    CountdownRemaining - Time.unscaledDeltaTime
                );
            }

            RecordingTakeContext take = CurrentTake;

            if (!recorder.TryStartRecording(take))
            {
                LastError = "Body tracking is not ready.";
                ReportTakeStartResult(false, "body_tracking_not_ready");
                CurrentTake = default;
                stateMachine.CancelCountdown();
                activeRoutine = null;
                yield break;
            }

            CurrentTake = take;
            nextTakeIndex = Mathf.Max(nextTakeIndex, take.TakeIndex + 1);
            recordingStartedAt = Time.realtimeSinceStartupAsDouble;
            stateMachine.BeginRecording();
            ReportTakeStartResult(true, "recording_started");
            activeRoutine = null;
        }

        private void ReportTakeStartResult(bool started, string message)
        {
            if (!reportRemoteStartResult)
            {
                return;
            }

            RecordingTakeContext take = CurrentTake;
            reportRemoteStartResult = false;
            // The host is the clock authority. Zero tells it to retain its own
            // scheduled/start timestamp instead of trusting the Quest wall clock.
            TakeStartResolved?.Invoke(take, started, 0L, message);
        }

        private IEnumerator CompleteFinalizingNextFrame()
        {
            yield return null;
            stateMachine.CompleteFinalizing();
            activeRoutine = null;
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
            NotifyPresentationChanged();
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

            if (recorder != null)
            {
                recorder.RecordingFinalized -= HandleRecordingFinalized;
            }
        }
    }
}
