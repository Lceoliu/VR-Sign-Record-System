using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Recording
{
    /// <summary>
    /// Adapts the small recorder canvas that already ships with the project to
    /// the take-aware coordinator.  The original canvas called the low-level
    /// recorder directly, which bypassed countdowns, metadata and retry-safe
    /// take IDs.  This bridge keeps the authored buttons and replaces only
    /// their action target.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingCoordinatorUIBridge : MonoBehaviour
    {
        [SerializeField] private RecordingCoordinator coordinator;
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text promptText;

        private bool bound;
        private float nextRefresh;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            Button start,
            Button stop,
            TMP_Text status,
            TMP_Text prompt = null)
        {
            Unbind();
            coordinator = recordingCoordinator;
            startButton = start;
            stopButton = stop;
            statusText = status;
            promptText = prompt;
            Bind();
            Refresh();
        }

        private void Awake()
        {
            Bind();
        }

        private void Update()
        {
            if (coordinator == null)
            {
                return;
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                Refresh();
                nextRefresh = Time.unscaledTime + 0.15f;
            }
        }

        private void Bind()
        {
            if (bound || coordinator == null)
            {
                return;
            }

            coordinator.PresentationChanged += Refresh;
            startButton?.onClick.AddListener(StartTake);
            stopButton?.onClick.AddListener(StopTake);
            bound = true;
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }

            coordinator.PresentationChanged -= Refresh;
            startButton?.onClick.RemoveListener(StartTake);
            stopButton?.onClick.RemoveListener(StopTake);
            bound = false;
        }

        public void StartTake()
        {
            if (coordinator == null)
            {
                return;
            }

            if (coordinator.State == RecordingFlowState.Ready)
            {
                coordinator.BeginCurrentTake();
            }
            else if (coordinator.State == RecordingFlowState.Completed)
            {
                coordinator.LoadPrompt(
                    coordinator.SessionId,
                    coordinator.SentenceId,
                    coordinator.PromptText,
                    1
                );
                coordinator.BeginCurrentTake();
            }

            Refresh();
        }

        public void StopTake()
        {
            coordinator?.StopCurrentTake();
            Refresh();
        }

        private void Refresh()
        {
            if (coordinator == null)
            {
                return;
            }

            if (promptText != null)
            {
                promptText.text = coordinator.PromptText;
            }

            bool busy = coordinator.State == RecordingFlowState.Countdown ||
                        coordinator.State == RecordingFlowState.Recording ||
                        coordinator.State == RecordingFlowState.Finalizing;
            if (startButton != null)
            {
                startButton.interactable = !busy;
            }
            if (stopButton != null)
            {
                stopButton.interactable = busy;
            }
            if (statusText != null)
            {
                string state = coordinator.State.ToString();
                string detail = coordinator.State == RecordingFlowState.Recording
                    ? $"  {coordinator.RecordingElapsedSeconds:F1}s"
                    : coordinator.State == RecordingFlowState.Countdown
                        ? $"  {Mathf.CeilToInt(coordinator.CountdownRemaining)}"
                        : string.Empty;
                statusText.text = $"Recording: {state}{detail}";
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
