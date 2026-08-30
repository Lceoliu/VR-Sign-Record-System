using System;
using SignVR.SceneFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Optional integration seam. W5 remains standalone when no sink is
    /// configured; W8 supplies the sink so UI commands pass through W6/W1.
    /// </summary>
    public interface IInteractionInstructionCommandSink
    {
        event Action StateChanged;
        bool CanReplay { get; }
        bool CanGiveUp { get; }
        bool CanAbort { get; }
        void RequestReplay();
        void RequestGiveUp();
        void RequestAbort();
    }

    /// <summary>
    /// Shared auxiliary controls for seated movement, Replay, Give Up Phase,
    /// and Abort Run.
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

        [SerializeField]
        private InteractionSeatedRigMover seatedRigMover;

        [SerializeField]
        [Tooltip(
            "When enabled by W8 scene setup, commands fail closed until the " +
            "authoritative Study Flow sink is installed."
        )]
        private bool requireCommandSink;

        private GameObject visualRoot;
        private Canvas canvas;
        private Button moveButton;
        private Button replayButton;
        private Button giveUpButton;
        private Button abortButton;
        private Button menuToggleButton;
        private TMP_Text menuToggleLabel;
        private InteractionHoldToConfirm abortHold;
        private bool bound;
        private bool menuExpanded = true;
        private IInteractionInstructionCommandSink commandSink;

        public Button MoveButton
        {
            get
            {
                if (moveButton == null)
                {
                    EnsureVisuals(forceRefresh: true);
                }
                ResolveExistingVisualReferences();
                return moveButton;
            }
        }

        public InteractionSeatedRigMover SeatedRigMover => seatedRigMover;

        public Button ReplayButton
        {
            get
            {
                ResolveExistingVisualReferences();
                return replayButton;
            }
        }

        public Button GiveUpButton
        {
            get
            {
                ResolveExistingVisualReferences();
                return giveUpButton;
            }
        }

        public Button AbortButton
        {
            get
            {
                ResolveExistingVisualReferences();
                return abortButton;
            }
        }

        public Button MenuToggleButton
        {
            get
            {
                if (menuToggleButton == null)
                {
                    EnsureVisuals(forceRefresh: true);
                }
                ResolveExistingVisualReferences();
                return menuToggleButton;
            }
        }

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
            AttachToParticipantHmd();
        }

        public void ConfigureSeatedMovement(InteractionSeatedRigMover mover)
        {
            Unbind();
            seatedRigMover = mover;
            EnsureVisuals(forceRefresh: true);
            Bind();
            Refresh();
        }

        public bool RequireCommandSink => requireCommandSink;

        public bool HasLiveCommandSink => ResolveLiveCommandSink() != null;

        public bool OwnsCommandSink(
            IInteractionInstructionCommandSink owner)
        {
            return owner != null &&
                ReferenceEquals(ResolveLiveCommandSink(), owner);
        }

        public bool CanInstallCommandSink(
            IInteractionInstructionCommandSink sink)
        {
            if (!IsLiveCommandSink(sink))
            {
                return false;
            }
            IInteractionInstructionCommandSink current =
                ResolveLiveCommandSink();
            return current == null || ReferenceEquals(current, sink);
        }

        public bool TryInstallCommandSink(
            IInteractionInstructionCommandSink sink)
        {
            if (!IsLiveCommandSink(sink))
            {
                throw new ArgumentNullException(nameof(sink));
            }
            if (!CanInstallCommandSink(sink))
            {
                return false;
            }
            if (ReferenceEquals(ResolveLiveCommandSink(), sink))
            {
                Refresh();
                return true;
            }
            ConfigureCommandSink(sink);
            return true;
        }

        public bool TryClearCommandSink(
            IInteractionInstructionCommandSink owner)
        {
            if (owner == null || !ReferenceEquals(commandSink, owner))
            {
                return false;
            }
            Unbind();
            commandSink = null;
            Bind();
            Refresh();
            return true;
        }

        public void ConfigureCommandSink(
            IInteractionInstructionCommandSink sink)
        {
            Unbind();
            commandSink = IsLiveCommandSink(sink) ? sink : null;
            Bind();
            Refresh();
        }

        public void ConfigureCommandRouting(bool requireSink)
        {
            requireCommandSink = requireSink;
            Refresh();
        }

        private void Awake()
        {
            EnsureVisuals();
            ResolveCamera();
            AttachToParticipantHmd();
        }

        private void OnEnable()
        {
            EnsureVisuals();
            ResolveCamera();
            AttachToParticipantHmd();
            Bind();
            Refresh();
        }

        private void LateUpdate()
        {
            ResolveCamera();
            AttachToParticipantHmd();
        }

        private void AttachToParticipantHmd()
        {
            if (participantHmd == null || participantHmd == transform ||
                participantHmd.IsChildOf(transform))
            {
                return;
            }

            if (transform.parent != participantHmd)
            {
                // A direct hierarchy relationship lets Meta/OpenXR's final
                // before-render HMD pose propagate to the UI automatically.
                // Copying the HMD's earlier world pose in LateUpdate leaves a
                // head-locked panel one tracking update behind and produces
                // visible micro-jitter on device.
                transform.SetParent(participantHmd, worldPositionStays: false);
            }

            Vector3 localViewPosition = new(
                viewOffset.x,
                viewOffset.y,
                viewDistance
            );
            if (transform.localPosition != localViewPosition)
            {
                transform.localPosition = localViewPosition;
            }
            if (localViewPosition.sqrMagnitude <= 0.000001f)
            {
                return;
            }
            Quaternion localViewRotation = Quaternion.LookRotation(
                localViewPosition.normalized,
                Vector3.up
            );
            if (transform.localRotation != localViewRotation)
            {
                transform.localRotation = localViewRotation;
            }
        }

        private void EnsureVisuals(bool forceRefresh = false)
        {
            if (visualRoot != null && !forceRefresh)
            {
                return;
            }
            if (visualRoot == null)
            {
                Transform existing = transform.Find(VisualRootName);
                visualRoot = existing != null
                    ? existing.gameObject
                    : new GameObject(
                        VisualRootName,
                        typeof(RectTransform),
                        typeof(Canvas),
                        typeof(CanvasScaler),
                        typeof(CanvasGroup),
                        typeof(GraphicRaycaster)
                    );
                if (existing == null)
                {
                    visualRoot.transform.SetParent(transform, false);
                }
            }

            RectTransform rect = visualRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(960f, 130f);
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
            if (visualRoot.GetComponent<GraphicRaycaster>() == null)
            {
                visualRoot.AddComponent<GraphicRaycaster>();
            }

            font ??= Resources.Load<TMP_FontAsset>("Fonts/SignVRChinese SDF");
            moveButton = EnsureButton(
                "MoveAlongView",
                new Vector2(-360f, 0f),
                new Color(0.08f, 0.42f, 0.78f, 0.96f),
                "向视线方向移动 20 cm",
                out _
            );
            replayButton = EnsureButton(
                "Replay",
                new Vector2(-120f, 0f),
                new Color(0.08f, 0.42f, 0.68f, 0.96f),
                "播放手语",
                out _
            );
            giveUpButton = EnsureButton(
                "GiveUpPhase",
                new Vector2(120f, 0f),
                new Color(0.62f, 0.39f, 0.08f, 0.96f),
                "放弃当前任务",
                out _
            );
            abortButton = EnsureButton(
                "AbortRun",
                new Vector2(360f, 0f),
                new Color(0.67f, 0.12f, 0.12f, 0.96f),
                "按住中止本轮",
                out TextMeshProUGUI abortLabel
            );
            abortHold = abortButton.GetComponent<InteractionHoldToConfirm>() ??
                abortButton.gameObject.AddComponent<InteractionHoldToConfirm>();
            abortHold.enabled = true;
            abortHold.Configure(abortLabel);
            menuToggleButton = EnsureButton(
                "MenuToggle",
                new Vector2(385f, 68f),
                new Color(0.12f, 0.18f, 0.24f, 0.96f),
                "\u6536\u8d77\u83dc\u5355",
                out TextMeshProUGUI toggleLabel
            );
            menuToggleLabel = toggleLabel;
            RectTransform toggleRect = menuToggleButton.GetComponent<RectTransform>();
            toggleRect.sizeDelta = new Vector2(178f, 52f);
            RectTransform visualRect = visualRoot.GetComponent<RectTransform>();
            visualRect.sizeDelta = new Vector2(960f, 200f);
            ApplyMenuVisibility();

            WorldSpacePokeCanvas pokeCanvas =
                visualRoot.GetComponent<WorldSpacePokeCanvas>() ??
                visualRoot.AddComponent<WorldSpacePokeCanvas>();
            pokeCanvas.Configure(canvas);
        }

        private void ResolveExistingVisualReferences()
        {
            if (visualRoot == null)
            {
                Transform existingRoot = transform.Find(VisualRootName);
                visualRoot = existingRoot != null
                    ? existingRoot.gameObject
                    : null;
            }
            if (visualRoot == null)
            {
                return;
            }

            moveButton ??= visualRoot.transform.Find("MoveAlongView")
                ?.GetComponent<Button>();
            replayButton ??= visualRoot.transform.Find("Replay")
                ?.GetComponent<Button>();
            giveUpButton ??= visualRoot.transform.Find("GiveUpPhase")
                ?.GetComponent<Button>();
            abortButton ??= visualRoot.transform.Find("AbortRun")
                ?.GetComponent<Button>();
            menuToggleButton ??= visualRoot.transform.Find("MenuToggle")
                ?.GetComponent<Button>();
            menuToggleLabel ??= menuToggleButton?.transform.Find("Label")
                ?.GetComponent<TMP_Text>();
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
            if (bound || !isActiveAndEnabled || replayButton == null ||
                moveButton == null)
            {
                return;
            }

            if (controller != null)
            {
                controller.StateChanged += Refresh;
            }
            IInteractionInstructionCommandSink liveSink =
                ResolveLiveCommandSink();
            if (liveSink != null)
            {
                liveSink.StateChanged += Refresh;
            }
            moveButton.onClick.AddListener(HandleMove);
            replayButton.onClick.AddListener(HandleReplay);
            giveUpButton.onClick.AddListener(HandleGiveUp);
            abortHold.Confirmed += HandleAbort;
            menuToggleButton?.onClick.AddListener(HandleMenuToggle);
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
            IInteractionInstructionCommandSink liveSink =
                ResolveLiveCommandSink();
            if (liveSink != null)
            {
                liveSink.StateChanged -= Refresh;
            }
            moveButton?.onClick.RemoveListener(HandleMove);
            replayButton?.onClick.RemoveListener(HandleReplay);
            giveUpButton?.onClick.RemoveListener(HandleGiveUp);
            menuToggleButton?.onClick.RemoveListener(HandleMenuToggle);
            if (abortHold != null)
            {
                abortHold.Confirmed -= HandleAbort;
            }
            bound = false;
        }

        private void Refresh()
        {
            EnsureVisuals();
            TMP_Text replayLabel = replayButton.GetComponentInChildren<
                TMP_Text>(true);
            if (replayLabel != null)
            {
                replayLabel.text = controller != null &&
                    controller.HasStartedFirstPlayback
                        ? "重新播放"
                        : "播放手语";
            }
            IInteractionInstructionCommandSink liveSink =
                ResolveLiveCommandSink();
            moveButton.interactable = seatedRigMover != null &&
                seatedRigMover.isActiveAndEnabled;
            replayButton.interactable =
                liveSink != null
                    ? liveSink.CanReplay
                    : !requireCommandSink && controller != null &&
                        controller.ReplayIsAvailable;
            giveUpButton.interactable =
                liveSink != null
                    ? liveSink.CanGiveUp
                    : !requireCommandSink && controller != null &&
                        controller.GiveUpIsAvailable;
            // Abort Run is a separate safety semantic and has no replay gate.
            abortButton.interactable = liveSink != null
                ? liveSink.CanAbort
                : !requireCommandSink;
            ApplyMenuVisibility();
        }

        private void HandleMenuToggle()
        {
            menuExpanded = !menuExpanded;
            ApplyMenuVisibility();
        }

        private void ApplyMenuVisibility()
        {
            moveButton?.gameObject.SetActive(menuExpanded);
            replayButton?.gameObject.SetActive(menuExpanded);
            giveUpButton?.gameObject.SetActive(menuExpanded);
            abortButton?.gameObject.SetActive(menuExpanded);
            if (menuToggleLabel != null)
            {
                menuToggleLabel.text = menuExpanded
                    ? "\u6536\u8d77\u83dc\u5355"
                    : "\u663e\u793a\u83dc\u5355";
            }
            menuToggleButton?.gameObject.SetActive(true);
        }

        private void HandleMove()
        {
            seatedRigMover?.MoveAlongCurrentView();
        }

        private void HandleReplay()
        {
            IInteractionInstructionCommandSink liveSink =
                ResolveLiveCommandSink();
            if (liveSink != null)
            {
                liveSink.RequestReplay();
                return;
            }
            if (requireCommandSink)
            {
                return;
            }
            controller?.Replay();
        }

        private void HandleGiveUp()
        {
            IInteractionInstructionCommandSink liveSink =
                ResolveLiveCommandSink();
            if (liveSink != null)
            {
                liveSink.RequestGiveUp();
                return;
            }
            if (requireCommandSink)
            {
                return;
            }
            controller?.GiveUpPhase();
        }

        private void HandleAbort()
        {
            IInteractionInstructionCommandSink liveSink =
                ResolveLiveCommandSink();
            if (liveSink != null)
            {
                liveSink.RequestAbort();
                return;
            }
            if (requireCommandSink)
            {
                return;
            }
            controller?.AbortRun();
        }

        private IInteractionInstructionCommandSink ResolveLiveCommandSink()
        {
            if (IsLiveCommandSink(commandSink))
            {
                return commandSink;
            }
            commandSink = null;
            return null;
        }

        private static bool IsLiveCommandSink(
            IInteractionInstructionCommandSink sink)
        {
            if (sink == null)
            {
                return false;
            }
            return !(sink is UnityEngine.Object unityObject) ||
                unityObject != null;
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
