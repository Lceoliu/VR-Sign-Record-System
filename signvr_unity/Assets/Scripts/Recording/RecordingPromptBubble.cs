using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Recording
{
    /// <summary>
    /// A non-interactive prompt surface that stays in the tracked HMD's
    /// upper-left field of view. It is created at runtime and needs no prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-8000)]
    public sealed class RecordingPromptBubble : MonoBehaviour
    {
        private const string RuntimeObjectName = "RecordingPromptBubbleCanvas";

        [Header("Dependencies")]
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private RecordingSentenceSequence sequence;

        [SerializeField]
        private RecordingViewpointController viewpointController;

        [SerializeField]
        private TMP_FontAsset font;

        [Header("View Placement")]
        [SerializeField]
        [Min(0.35f)]
        private float viewDistance = 0.8f;

        [SerializeField]
        [Min(500f)]
        private float referenceHeight = 1000f;

        [SerializeField]
        [Range(0.25f, 0.6f)]
        private float viewportWidthFraction = 0.46f;

        [SerializeField]
        [Min(0f)]
        private float edgeMargin = 42f;

        [SerializeField]
        [Min(120f)]
        private float bubbleHeight = 188f;

        [SerializeField]
        [Min(240f)]
        private float maximumBubbleWidth = 620f;

        [Header("Appearance")]
        [SerializeField]
        private Color backgroundColor = new(0.035f, 0.045f, 0.055f, 0.92f);

        [SerializeField]
        private Color promptColor = new(0.96f, 0.97f, 0.98f, 1f);

        [SerializeField]
        private Color secondaryTextColor = new(0.72f, 0.76f, 0.8f, 1f);

        [SerializeField]
        private Color readyColor = new(0.19f, 0.78f, 0.48f, 1f);

        [SerializeField]
        private Color recordingColor = new(0.95f, 0.22f, 0.2f, 1f);

        [SerializeField]
        private Color busyColor = new(1f, 0.68f, 0.22f, 1f);

        [SerializeField]
        private Color errorColor = new(0.98f, 0.33f, 0.29f, 1f);

        [SerializeField]
        [Range(8f, 64f)]
        private float cornerRadius = 34f;

        private Canvas bubbleCanvas;
        private CanvasGroup canvasGroup;
        private RectTransform canvasRect;
        private RectTransform bubbleRect;
        private RecordingRoundedRectangleGraphic background;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI progressText;
        private TextMeshProUGUI promptText;
        private Camera hmdCamera;
        private Transform trackedHmd;
        private bool bound;
        private bool canvasRenderBound;
        private float nextTimedRefresh;
        private float lastAspect = float.NaN;
        private float lastFieldOfView = float.NaN;
        private float lastViewDistance = float.NaN;
        private float lastReferenceHeight = float.NaN;

        public Canvas BubbleCanvas => bubbleCanvas;
        public TMP_Text PromptLabel => promptText;
        public TMP_Text StatusLabel => statusText;

        /// <summary>
        /// Finds or creates one bubble directly below the supplied HMD anchor.
        /// Passing a font is optional; a supplied font is applied to every label.
        /// </summary>
        public static RecordingPromptBubble EnsureCreated(
            Transform hmd,
            RecordingCoordinator recordingCoordinator,
            RecordingSentenceSequence sentenceSequence = null,
            TMP_FontAsset textFont = null,
            RecordingViewpointController recordingViewpoints = null)
        {
            if (hmd == null)
            {
                throw new ArgumentNullException(nameof(hmd));
            }
            if (recordingCoordinator == null)
            {
                throw new ArgumentNullException(nameof(recordingCoordinator));
            }

            RecordingPromptBubble bubble = null;
            foreach (RecordingPromptBubble candidate in
                     hmd.GetComponentsInChildren<RecordingPromptBubble>(true))
            {
                if (candidate.transform.parent == hmd)
                {
                    bubble = candidate;
                    break;
                }
            }

            if (bubble == null)
            {
                GameObject root = new(
                    RuntimeObjectName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(CanvasGroup)
                );
                root.SetActive(false);
                root.transform.SetParent(hmd, false);
                bubble = root.AddComponent<RecordingPromptBubble>();
                bubble.trackedHmd = hmd;
                bubble.Configure(
                    recordingCoordinator,
                    sentenceSequence,
                    textFont,
                    recordingViewpoints
                );
                root.SetActive(true);
            }
            else
            {
                bubble.trackedHmd = hmd;
                bubble.Configure(
                    recordingCoordinator,
                    sentenceSequence,
                    textFont,
                    recordingViewpoints
                );
            }

            return bubble;
        }

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            RecordingSentenceSequence sentenceSequence = null,
            TMP_FontAsset textFont = null,
            RecordingViewpointController recordingViewpoints = null)
        {
            if (recordingCoordinator == null)
            {
                throw new ArgumentNullException(nameof(recordingCoordinator));
            }

            Unbind();
            coordinator = recordingCoordinator;
            sequence = sentenceSequence;
            viewpointController = recordingViewpoints;
            trackedHmd ??= transform.parent;
            if (textFont != null)
            {
                font = textFont;
            }

            ResolveHmdCamera();
            EnsureVisuals();
            ApplyFont();
            UpdateViewportLayout(true);
            Refresh();

            if (isActiveAndEnabled)
            {
                Bind();
            }
        }

        private void OnEnable()
        {
            BindCanvasRenderCallback();
            UpdateEditorSimulationParent(true);
            ResolveHmdCamera();
            Bind();
            UpdateViewportLayout(true);
            Refresh();
        }

        private void Update()
        {
            UpdateEditorSimulationParent(false);
            UpdateViewportLayout(false);
            if (coordinator == null || Time.unscaledTime < nextTimedRefresh)
            {
                return;
            }

            nextTimedRefresh = Time.unscaledTime + 0.1f;
            Refresh();
        }

        private void ResolveHmdCamera()
        {
            hmdCamera = transform.parent != null
                ? transform.parent.GetComponent<Camera>()
                : null;
            if (hmdCamera == null)
            {
                hmdCamera = GetComponentInParent<Camera>();
            }

            if (bubbleCanvas != null)
            {
                bubbleCanvas.worldCamera = hmdCamera;
                if (hmdCamera != null)
                {
                    bubbleCanvas.targetDisplay = hmdCamera.targetDisplay;
                }
            }
        }

        private void UpdateEditorSimulationParent(bool forceLayout)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || viewpointController == null)
            {
                return;
            }

            Camera preview = viewpointController.CurrentViewpoint?.ReferenceCamera;
            Transform desiredParent = preview != null && preview.enabled
                ? preview.transform
                : trackedHmd;
            if (desiredParent == null)
            {
                return;
            }

            bool parentChanged = transform.parent != desiredParent;
            if (!parentChanged && !forceLayout)
            {
                return;
            }

            if (parentChanged)
            {
                transform.SetParent(desiredParent, false);
            }

            ResetCanvasRectTransform();
            ResolveHmdCamera();
            InvalidateViewportLayout();
            UpdateViewportLayout(true);
#endif
        }

        private void InvalidateViewportLayout()
        {
            lastAspect = float.NaN;
            lastFieldOfView = float.NaN;
            lastViewDistance = float.NaN;
            lastReferenceHeight = float.NaN;
        }

        private void BindCanvasRenderCallback()
        {
            if (canvasRenderBound)
            {
                return;
            }

            Canvas.preWillRenderCanvases += HandleCanvasPreRender;
            canvasRenderBound = true;
        }

        private void UnbindCanvasRenderCallback()
        {
            if (!canvasRenderBound)
            {
                return;
            }

            Canvas.preWillRenderCanvases -= HandleCanvasPreRender;
            canvasRenderBound = false;
        }

        private void HandleCanvasPreRender()
        {
            UpdateEditorSimulationParent(false);
            ResolveHmdCamera();
            UpdateViewportLayout(false);
        }

        private void EnsureVisuals()
        {
            canvasRect = transform as RectTransform;
            bubbleCanvas = GetComponent<Canvas>();
            canvasGroup = GetComponent<CanvasGroup>();
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (canvasRect == null || bubbleCanvas == null ||
                canvasGroup == null || scaler == null)
            {
                throw new InvalidOperationException(
                    "RecordingPromptBubble must be created through EnsureCreated."
                );
            }

            bubbleCanvas.renderMode = RenderMode.WorldSpace;
            bubbleCanvas.worldCamera = hmdCamera;
            bubbleCanvas.overrideSorting = true;
            bubbleCanvas.sortingOrder = 30000;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 100f;
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            if (bubbleRect != null)
            {
                return;
            }

            GameObject panelObject = CreateUiObject("Bubble", transform);
            bubbleRect = (RectTransform)panelObject.transform;
            background = panelObject.AddComponent<RecordingRoundedRectangleGraphic>();
            background.raycastTarget = false;
            background.color = backgroundColor;
            background.CornerRadius = cornerRadius;
            background.CornerSegments = 10;

            Shadow shadow = panelObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.36f);
            shadow.effectDistance = new Vector2(0f, -8f);
            shadow.useGraphicAlpha = true;

            statusText = CreateLabel("Status", bubbleRect);
            statusText.fontStyle = FontStyles.Bold;
            statusText.fontSize = 22f;
            statusText.enableAutoSizing = true;
            statusText.fontSizeMin = 16f;
            statusText.fontSizeMax = 22f;
            statusText.alignment = TextAlignmentOptions.TopLeft;
            statusText.overflowMode = TextOverflowModes.Ellipsis;

            progressText = CreateLabel("Progress", bubbleRect);
            progressText.fontSize = 20f;
            progressText.enableAutoSizing = true;
            progressText.fontSizeMin = 14f;
            progressText.fontSizeMax = 20f;
            progressText.alignment = TextAlignmentOptions.TopRight;
            progressText.color = secondaryTextColor;
            progressText.overflowMode = TextOverflowModes.Ellipsis;

            promptText = CreateLabel("Prompt", bubbleRect);
            promptText.fontStyle = FontStyles.Normal;
            promptText.fontSize = 36f;
            promptText.enableAutoSizing = true;
            promptText.fontSizeMin = 16f;
            promptText.fontSizeMax = 36f;
            promptText.alignment = TextAlignmentOptions.TopLeft;
            promptText.color = promptColor;
            promptText.textWrappingMode = TextWrappingModes.Normal;
            promptText.overflowMode = TextOverflowModes.Ellipsis;
            promptText.lineSpacing = 4f;
            promptText.characterSpacing = 0f;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject result = new(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer)
            );
            result.transform.SetParent(parent, false);
            return result;
        }

        private static TextMeshProUGUI CreateLabel(
            string name,
            Transform parent)
        {
            GameObject labelObject = new(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );
            labelObject.transform.SetParent(parent, false);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }

        private void ApplyFont()
        {
            if (font == null)
            {
                return;
            }

            if (statusText != null)
            {
                statusText.font = font;
            }
            if (progressText != null)
            {
                progressText.font = font;
            }
            if (promptText != null)
            {
                promptText.font = font;
            }
        }

        private void UpdateViewportLayout(bool force)
        {
            if (canvasRect == null || bubbleRect == null)
            {
                return;
            }

            float aspect = hmdCamera != null && hmdCamera.aspect > 0f
                ? Mathf.Clamp(hmdCamera.aspect, 0.8f, 2.4f)
                : 1.6f;
            float fieldOfView = hmdCamera != null && hmdCamera.fieldOfView > 0f
                ? Mathf.Clamp(hmdCamera.fieldOfView, 45f, 125f)
                : 90f;
            if (!force &&
                Mathf.Approximately(lastAspect, aspect) &&
                Mathf.Approximately(lastFieldOfView, fieldOfView) &&
                Mathf.Approximately(lastViewDistance, viewDistance) &&
                Mathf.Approximately(lastReferenceHeight, referenceHeight))
            {
                return;
            }

            lastAspect = aspect;
            lastFieldOfView = fieldOfView;
            lastViewDistance = viewDistance;
            lastReferenceHeight = referenceHeight;

            float canvasHeight = Mathf.Max(500f, referenceHeight);
            float canvasWidth = canvasHeight * aspect;
            float safeDistance = Mathf.Max(0.35f, viewDistance);
            float planeHeight = 2f * safeDistance * Mathf.Tan(
                fieldOfView * 0.5f * Mathf.Deg2Rad
            );
            float canvasScale = planeHeight / canvasHeight;

            ResetCanvasRectTransform();
            canvasRect.sizeDelta = new Vector2(canvasWidth, canvasHeight);
            canvasRect.anchoredPosition3D = Vector3.forward * safeDistance;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * canvasScale;

            float availableWidth = Mathf.Max(240f, canvasWidth - edgeMargin * 2f);
            float width = Mathf.Min(
                availableWidth,
                Mathf.Min(maximumBubbleWidth, canvasWidth * viewportWidthFraction)
            );
            float height = Mathf.Min(
                bubbleHeight,
                Mathf.Max(120f, canvasHeight - edgeMargin * 2f)
            );

            bubbleRect.anchorMin = new Vector2(0f, 1f);
            bubbleRect.anchorMax = new Vector2(0f, 1f);
            bubbleRect.pivot = new Vector2(0f, 1f);
            bubbleRect.anchoredPosition = new Vector2(edgeMargin, -edgeMargin);
            bubbleRect.sizeDelta = new Vector2(width, height);
            background.color = backgroundColor;
            background.CornerRadius = Mathf.Min(cornerRadius, height * 0.5f);

            ConfigureHeaderRect(
                statusText.rectTransform,
                new Vector2(28f, -18f),
                new Vector2(width * 0.64f - 28f, 32f),
                new Vector2(0f, 1f)
            );
            ConfigureHeaderRect(
                progressText.rectTransform,
                new Vector2(-28f, -18f),
                new Vector2(width * 0.3f - 28f, 32f),
                new Vector2(1f, 1f)
            );

            RectTransform promptRect = promptText.rectTransform;
            promptRect.anchorMin = Vector2.zero;
            promptRect.anchorMax = Vector2.one;
            promptRect.pivot = new Vector2(0.5f, 0.5f);
            promptRect.offsetMin = new Vector2(28f, 20f);
            promptRect.offsetMax = new Vector2(-28f, -58f);
            canvasRect.ForceUpdateRectTransforms();
        }

        private void ResetCanvasRectTransform()
        {
            if (canvasRect == null)
            {
                return;
            }

            Vector2 center = new(0.5f, 0.5f);
            canvasRect.anchorMin = center;
            canvasRect.anchorMax = center;
            canvasRect.pivot = center;
            canvasRect.anchoredPosition3D = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one;
        }

        private static void ConfigureHeaderRect(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 size,
            Vector2 anchor)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(
                Mathf.Max(40f, size.x),
                Mathf.Max(20f, size.y)
            );
        }

        private void Bind()
        {
            if (bound || coordinator == null)
            {
                return;
            }

            coordinator.PresentationChanged += Refresh;
            if (sequence != null)
            {
                sequence.SentenceChanged += HandleSentenceChanged;
                sequence.SequenceCompleted += HandleSequenceCompleted;
            }
            if (viewpointController != null)
            {
                viewpointController.ViewpointChanged += HandleViewpointChanged;
            }
            bound = true;
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }

            if (coordinator != null)
            {
                coordinator.PresentationChanged -= Refresh;
            }
            if (sequence != null)
            {
                sequence.SentenceChanged -= HandleSentenceChanged;
                sequence.SequenceCompleted -= HandleSequenceCompleted;
            }
            if (viewpointController != null)
            {
                viewpointController.ViewpointChanged -= HandleViewpointChanged;
            }
            bound = false;
        }

        private void HandleSentenceChanged(int _, RecordingSentence __)
        {
            Refresh();
        }

        private void HandleSequenceCompleted()
        {
            Refresh();
        }

        private void HandleViewpointChanged(RecordingViewpointSelection _)
        {
            // The new preview Camera is active when this event is raised. Move
            // the canvas before that frame renders instead of polling one frame late.
            UpdateEditorSimulationParent(true);
        }

        private void Refresh()
        {
            if (coordinator == null || promptText == null)
            {
                return;
            }

            promptText.text = string.IsNullOrWhiteSpace(coordinator.PromptText)
                ? "\u7b49\u5f85\u5f55\u5236\u5185\u5bb9"
                : coordinator.PromptText;
            progressText.text = BuildProgressLabel();
            statusText.text = BuildStatusLabel(out Color statusColor);
            statusText.color = statusColor;
        }

        private string BuildProgressLabel()
        {
            if (sequence != null && sequence.HasCurrentSentence)
            {
                return $"{sequence.CurrentSentenceIndex + 1} / {sequence.SentenceCount}";
            }

            return coordinator != null ? coordinator.SentenceId : string.Empty;
        }

        private string BuildStatusLabel(out Color color)
        {
            color = busyColor;
            if (coordinator == null)
            {
                return "\u7b49\u5f85\u8fde\u63a5";
            }
            if (coordinator.IsHelpRequested)
            {
                return "\u7b49\u5f85\u5e2e\u52a9";
            }
            if (coordinator.IsPaused)
            {
                return "\u5df2\u6682\u505c";
            }

            switch (coordinator.State)
            {
                case RecordingFlowState.Ready:
                    if (!string.IsNullOrWhiteSpace(coordinator.LastError))
                    {
                        color = errorColor;
                        return coordinator.LastError;
                    }
                    color = readyColor;
                    return "\u5f85\u5f55\u5236";
                case RecordingFlowState.Countdown:
                    return $"\u5012\u8ba1\u65f6 {Mathf.Max(1, Mathf.CeilToInt(coordinator.CountdownRemaining))}";
                case RecordingFlowState.Recording:
                    color = recordingColor;
                    return $"REC  {coordinator.RecordingElapsedSeconds:F1}s";
                case RecordingFlowState.Finalizing:
                    return "\u4fdd\u5b58\u4e2d";
                case RecordingFlowState.Completed:
                    color = readyColor;
                    return "\u5df2\u5b8c\u6210";
                case RecordingFlowState.Reviewing:
                    return "\u56de\u770b\u4e2d";
                case RecordingFlowState.Resetting:
                    return "\u91cd\u7f6e\u4e2d";
                case RecordingFlowState.Error:
                    color = errorColor;
                    return string.IsNullOrWhiteSpace(coordinator.LastError)
                        ? "\u5f55\u5236\u9519\u8bef"
                        : coordinator.LastError;
                default:
                    return "\u7b49\u5f85\u8fde\u63a5";
            }
        }

        private void OnDisable()
        {
            UnbindCanvasRenderCallback();
            Unbind();
        }

        private void OnDestroy()
        {
            UnbindCanvasRenderCallback();
            Unbind();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            viewDistance = Mathf.Max(0.35f, viewDistance);
            referenceHeight = Mathf.Max(500f, referenceHeight);
            edgeMargin = Mathf.Max(0f, edgeMargin);
            bubbleHeight = Mathf.Max(120f, bubbleHeight);
            maximumBubbleWidth = Mathf.Max(240f, maximumBubbleWidth);
            cornerRadius = Mathf.Clamp(cornerRadius, 8f, 64f);
            if (Application.isPlaying)
            {
                UpdateViewportLayout(true);
            }
        }
#endif
    }
}
