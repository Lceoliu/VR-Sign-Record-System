using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the motion recorder from VR UI buttons and Quest controller buttons.
///
/// Right controller:
/// A = start recording
/// B = stop recording
/// </summary>
public sealed class MetaMotionRecorderUI : MonoBehaviour
{
    [Header("Recorder")]

    [SerializeField]
    private MetaBodyMotionRecorder recorder;

    [Header("UI")]

    [SerializeField]
    private Button startButton;

    [SerializeField]
    private Button stopButton;

    [SerializeField]
    private TMP_Text statusText;

    [Header("Controller Shortcuts")]

    [SerializeField]
    private bool enableControllerShortcuts = true;

    private bool previousRecordingState;

    private void Awake()
    {
        if (recorder == null)
        {
            Debug.LogError(
                "[MetaMotionRecorderUI] Recorder is not assigned."
            );
            enabled = false;
            return;
        }

        if (startButton != null)
        {
            startButton.onClick.AddListener(StartRecording);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopRecording);
        }

        previousRecordingState = recorder.IsRecording;
        RefreshUI();
    }

    private void Update()
    {
        if (enableControllerShortcuts)
        {
            // Right controller A = Start
            if (OVRInput.GetDown(OVRInput.RawButton.A))
            {
                StartRecording();
            }

            // Right controller B = Stop
            if (OVRInput.GetDown(OVRInput.RawButton.B))
            {
                StopRecording();
            }
        }

        // Recording state changed: refresh buttons + text.
        if (previousRecordingState != recorder.IsRecording)
        {
            RefreshUI();
        }

        // While recording, continuously update frame counter.
        if (recorder.IsRecording && statusText != null)
        {
            statusText.text =
                $"Recording...\n{recorder.SampleCount} frames";
        }
    }

    public void StartRecording()
    {
        if (recorder == null || recorder.IsRecording)
        {
            return;
        }

        recorder.StartRecording();
        RefreshUI();
    }

    public void StopRecording()
    {
        if (recorder == null || !recorder.IsRecording)
        {
            return;
        }

        recorder.StopRecording();
        RefreshUI();
    }

    private void RefreshUI()
    {
        bool isRecording = recorder.IsRecording;

        if (startButton != null)
        {
            startButton.interactable = !isRecording;
        }

        if (stopButton != null)
        {
            stopButton.interactable = isRecording;
        }

        if (statusText != null)
        {
            if (isRecording)
            {
                statusText.text =
                    $"Recording...\n{recorder.SampleCount} frames";
            }
            else if (recorder.SampleCount > 0)
            {
                statusText.text =
                    $"Saved\n{recorder.SampleCount} frames";
            }
            else
            {
                statusText.text = "Ready";
            }
        }

        previousRecordingState = isRecording;
    }

    private void OnDestroy()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartRecording);
        }

        if (stopButton != null)
        {
            stopButton.onClick.RemoveListener(StopRecording);
        }
    }
}