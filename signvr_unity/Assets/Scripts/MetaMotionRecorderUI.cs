using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
    private const string PokeInteractionName = "ISDK_PokeCanvasInteraction";

    [Header("Recorder")]

    [SerializeField]
    private MetaBodyMotionRecorder recorder;

    [SerializeField]
    private MetaBodyMotionStreamer motionStreamer;

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
    private float nextStatusRefreshTime;
    private bool listenersBound;

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

        if (motionStreamer == null)
        {
            motionStreamer =
                FindAnyObjectByType<MetaBodyMotionStreamer>();
        }

        EnsurePokeInteraction();

        if (startButton != null)
        {
            startButton.onClick.AddListener(StartRecording);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopRecording);
        }

        listenersBound = true;

        previousRecordingState = recorder.IsRecording;
        RefreshUI();
    }

    /// <summary>
    /// Makes this world-space UGUI canvas directly touchable by the Meta hand
    /// poke interactors that are already present in the scene.
    /// </summary>
    private void EnsurePokeInteraction()
    {
        Canvas canvas = GetComponent<Canvas>();
        RectTransform canvasRect = transform as RectTransform;

        if (canvas == null || canvasRect == null)
        {
            Debug.LogError(
                "[MetaMotionRecorderUI] A Canvas and RectTransform are required."
            );
            return;
        }

        canvas.renderMode = RenderMode.WorldSpace;

        PointableCanvasModule canvasModule =
            FindAnyObjectByType<PointableCanvasModule>();

        if (canvasModule == null)
        {
            EventSystem eventSystem =
                FindAnyObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                GameObject eventSystemObject =
                    new GameObject("PointableCanvasEventSystem");

                eventSystem =
                    eventSystemObject.AddComponent<EventSystem>();
            }

            canvasModule =
                eventSystem.gameObject.AddComponent<PointableCanvasModule>();

            // The Meta module must own the EventSystem so poke events are
            // processed instead of being shadowed by the desktop input module.
            canvasModule.ExclusiveMode = true;
        }

        if (transform.Find(PokeInteractionName) != null)
        {
            return;
        }

        GameObject interactionObject =
            new GameObject(PokeInteractionName, typeof(RectTransform));

        interactionObject.SetActive(false);
        interactionObject.layer = gameObject.layer;

        RectTransform interactionRect =
            interactionObject.GetComponent<RectTransform>();

        interactionRect.SetParent(transform, false);
        StretchToParent(interactionRect);

        PointableCanvas pointableCanvas =
            interactionObject.AddComponent<PointableCanvas>();

        pointableCanvas.InjectCanvas(canvas);

        GameObject surfaceObject =
            new GameObject("Surface", typeof(RectTransform));

        surfaceObject.layer = gameObject.layer;

        RectTransform surfaceRect =
            surfaceObject.GetComponent<RectTransform>();

        surfaceRect.SetParent(interactionRect, false);
        StretchToParent(surfaceRect);

        PlaneSurface planeSurface =
            surfaceObject.AddComponent<PlaneSurface>();

        planeSurface.Facing = PlaneSurface.NormalFacing.Backward;

        BoundsClipper boundsClipper =
            surfaceObject.AddComponent<BoundsClipper>();

        boundsClipper.Size = new Vector3(
            canvasRect.rect.width,
            canvasRect.rect.height,
            0.01f
        );

        ClippedPlaneSurface clippedSurface =
            surfaceObject.AddComponent<ClippedPlaneSurface>();

        clippedSurface.InjectAllClippedPlaneSurface(
            planeSurface,
            new[] { boundsClipper }
        );

        PokeInteractable pokeInteractable =
            interactionObject.AddComponent<PokeInteractable>();

        pokeInteractable.InjectAllPokeInteractable(clippedSurface);
        pokeInteractable.InjectOptionalPointableElement(pointableCanvas);

        interactionObject.SetActive(true);
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
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

        if (Time.unscaledTime >= nextStatusRefreshTime)
        {
            RefreshStatusText();
            nextStatusRefreshTime = Time.unscaledTime + 0.25f;
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

    /// <summary>
    /// Removes the legacy direct-recorder callbacks when the take-aware
    /// coordinator takes ownership of this authored canvas.
    /// </summary>
    public void DetachControls()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartRecording);
        }
        if (stopButton != null)
        {
            stopButton.onClick.RemoveListener(StopRecording);
        }
        listenersBound = false;
        enabled = false;
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
            RefreshStatusText();
        }

        previousRecordingState = isRecording;
    }

    private void RefreshStatusText()
    {
        if (statusText == null)
        {
            return;
        }

        string recordingStatus;

        if (recorder.IsRecording)
        {
            recordingStatus =
                $"Recording... {recorder.SampleCount} frames";
        }
        else if (recorder.SampleCount > 0)
        {
            recordingStatus =
                $"Saved {recorder.SampleCount} frames";
        }
        else
        {
            recordingStatus = "Ready";
        }

        statusText.text = motionStreamer != null
            ? recordingStatus + "\n" + motionStreamer.ShortDebugStatus
            : recordingStatus + "\nUDP component missing";
    }

    private void OnDestroy()
    {
        if (listenersBound)
        {
            DetachControls();
        }
    }
}
