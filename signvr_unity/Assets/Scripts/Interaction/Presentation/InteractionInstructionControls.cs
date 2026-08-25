using SignVR.SceneFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Minimal independent controls for Replay, Give Up Phase, and Abort Run.
    /// It contains no Recorder countdown, Take progress, or recording action.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionInstructionControls : MonoBehaviour
    {
        private const string VisualRootName = "InstructionControlCanvas";

        [SerializeField]
        private InstructionPresentationController controller;

        [SerializeField]
        private Transform participantHmd;

        [SerializeField]
        [Min(0.4f)]
        private float viewDistance = 0.72f;

        [SerializeField]
        private Vector2 viewOffset = new(0f, -0.22f);

        [SerializeField]
        private TMP_FontAsset font;

        private GameObject visualRoot;
        private Canvas canvas;
        private Button replayButton;
        private Button giveUpButton;
        private Button abortButton;
        private InteractionHoldToConfirm abortHold;
        private bool bound;

        public Button ReplayButton => replayButton;

        public Button GiveUpButton => giveUpButton;

        public Button AbortButton => abortButton;

        public void Configure(InstructionPresentationController presentation)
        {
            Unbind();
            controller = presentation;
            EnsureVisuals();
            Bind();
            Refresh();
        }

        public void ConfigureHmd(Transform hmd)
        {
            participantHmd = hmd;
            ResolveCamera();
        }

        private void Awake()
        {
            EnsureVisuals();
            ResolveCamera();
        }

        private void OnEnable()
        {
            EnsureVisuals();
            Bind();
            Refresh();
        }

        private void LateUpdate()
        {
            ResolveCamera();
            if (participantHmd == null)
            {
                return;
            }

            transform.position = participantHmd.position +
                participantHmd.forward * viewDistance +
                participantHmd.right * viewOffset.x +
                participantHmd.up * viewOffset.y;
            Vector3 awayFromViewer = transform.position -
                participantHmd.position;
            if (awayFromViewer.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    awayFromViewer.normalized,
                    participantHmd.up
                );
            }
        }

        private void EnsureVisuals()
        {
            if (visualRoot != null)
            {
                return;
            }

            Transform existing = transform.Find(VisualRootName);
            visualRoot = existing != null
                ? existing.gameObject
                : new GameObject(
                    VisualRootName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(CanvasGroup)
                );
            if (existing == null)
            {
                visualRoot.transform.SetParent(transform, false);
            }

            RectTransform rect = visualRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(720f, 130f);
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.001f;

            canvas = visualRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 29990;
            CanvasScaler scaler = visualRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 100f;
            CanvasGroup group = visualRoot.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            font ??= Resources.Load<TMP_FontAsset>("Fonts/SignVRChinese SDF");
            replayButton = EnsureButton(
                "Replay",
                new Vector2(-240f, 0f),
                new Color(0.08f, 0.42f, 0.68f, 0.96f),
                "重播手语",
                out _
            );
            giveUpButton = EnsureButton(
                "GiveUpPhase",
                Vector2.zero,
                new Color(0.62f, 0.39f, 0.08f, 0.96f),
                "放弃当前任务",
                out _
            );
            abortButton = EnsureButton(
                "AbortRun",
                new Vector2(240f, 0f),
                new Color(0.67f, 0.12f, 0.12f, 0.96f),
                "按住中止本轮",
                out TextMeshProUGUI abortLabel
            );
            abortHold = abortButton.GetComponent<InteractionHoldToConfirm>() ??
                abortButton.gameObject.AddComponent<InteractionHoldToConfirm>();
            abortHold.Configure(abortLabel);

            WorldSpacePokeCanvas pokeCanvas =
                visualRoot.GetComponent<WorldSpacePokeCanvas>() ??
                visualRoot.AddComponent<WorldSpacePokeCanvas>();
            pokeCanvas.Configure(canvas);
        }

        private Button EnsureButton(
            string name,
            Vector2 position,
            Color color,
            string text,
            out TextMeshProUGUI label)
        {
            Transform existing = visualRoot.transform.Find(name);
            GameObject buttonObject = existing != null
                ? existing.gameObject
                : new GameObject(
                    name,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(InteractionRoundedRectangleGraphic),
                    typeof(Button)
                );
            if (existing == null)
            {
                buttonObject.transform.SetParent(visualRoot.transform, false);
            }

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(220f, 92f);
            rect.anchoredPosition = position;

            InteractionRoundedRectangleGraphic graphic =
                buttonObject.GetComponent<InteractionRoundedRectangleGraphic>();
            graphic.color = color;
            graphic.CornerRadius = 24f;
            graphic.CornerSegments = 8;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = graphic;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.58f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            Transform existingLabel = buttonObject.transform.Find("Label");
            GameObject labelObject = existingLabel != null
                ? existingLabel.gameObject
                : new GameObject(
                    "Label",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI)
                );
            if (existingLabel == null)
            {
                labelObject.transform.SetParent(buttonObject.transform, false);
            }

            label = labelObject.GetComponent<TextMeshProUGUI>();
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 8f);
            labelRect.offsetMax = new Vector2(-12f, -8f);
            label.text = text;
            label.font = font;
            label.fontSize = 27f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = 27f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.richText = false;
            return button;
        }

        private void Bind()
        {
            if (bound || controller == null || replayButton == null)
            {
                return;
            }

            controller.StateChanged += Refresh;
            replayButton.onClick.AddListener(HandleReplay);
            giveUpButton.onClick.AddListener(HandleGiveUp);
            abortHold.Confirmed += HandleAbort;
            bound = true;
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }

            if (controller != null)
            {
                controller.StateChanged -= Refresh;
            }
            replayButton?.onClick.RemoveListener(HandleReplay);
            giveUpButton?.onClick.RemoveListener(HandleGiveUp);
            if (abortHold != null)
            {
                abortHold.Confirmed -= HandleAbort;
            }
            bound = false;
        }

        private void Refresh()
        {
            EnsureVisuals();
            replayButton.interactable =
                controller != null && controller.ReplayIsAvailable;
            giveUpButton.interactable =
                controller != null && controller.GiveUpIsAvailable;
            // Abort Run is a separate safety semantic and has no replay gate.
            abortButton.interactable = true;
        }

        private void HandleReplay()
        {
            controller?.Replay();
        }

        private void HandleGiveUp()
        {
            controller?.GiveUpPhase();
        }

        private void HandleAbort()
        {
            controller?.AbortRun();
        }

        private void ResolveCamera()
        {
            if (participantHmd == null)
            {
                Camera camera = Camera.main;
                participantHmd = camera != null ? camera.transform : null;
            }
            if (canvas != null)
            {
                canvas.worldCamera = participantHmd != null
                    ? participantHmd.GetComponent<Camera>()
                    : null;
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            viewDistance = Mathf.Max(0.4f, viewDistance);
        }
#endif
    }
}
