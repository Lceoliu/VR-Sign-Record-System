using System;
using System.Collections;
using System.IO;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Test-scene-only navigator for the complete 31-item instruction catalog.
    /// Every selection still goes through the production presentation stack so
    /// signer playback, canonical text, pointing detection, and highlighting
    /// are exercised together.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-90)]
    public sealed class InteractionSignSequenceTestController : MonoBehaviour
    {
        public const int EntryCount = PhaseSentenceRanges.TotalSentenceCount;

        [SerializeField]
        private InstructionPresentationController presentationController;

        [SerializeField]
        private InteractionPhaseCoordinator phaseCoordinator;

        [SerializeField]
        private string contentManifestRelativePath =
            "InstructionContent/instruction-content-manifest.json";

        [SerializeField]
        [Range(0, EntryCount - 1)]
        private int initialIndex;

        private InstructionContentCatalog contentCatalog;
        private Coroutine manifestRoutine;
        private int currentIndex;

        public event Action StateChanged;

        public int CurrentIndex => currentIndex;
        public int Count => EntryCount;
        public bool IsReady => contentCatalog != null;
        public bool CanMovePrevious => IsReady && currentIndex > 0;
        public bool CanMoveNext => IsReady && currentIndex < EntryCount - 1;
        public bool CanReplay => IsReady;
        public string CurrentSentenceId =>
            PhaseSentenceRanges.FormatSentenceId(currentIndex + 1);
        public string Status { get; private set; } = "正在加载 31 个手语动作…";

        public void Configure(
            InstructionPresentationController presentation)
        {
            Configure(presentation, null);
        }

        public void Configure(
            InstructionPresentationController presentation,
            InteractionPhaseCoordinator coordinator)
        {
            presentationController = presentation ??
                throw new ArgumentNullException(nameof(presentation));
            phaseCoordinator = coordinator;
        }

        public bool TryMovePrevious()
        {
            return TrySelect(currentIndex - 1);
        }

        public bool TryMoveNext()
        {
            return TrySelect(currentIndex + 1);
        }

        public bool ReplayCurrent()
        {
            return IsReady && PresentCurrent();
        }

        public bool TrySelect(int index)
        {
            if (!IsReady || index < 0 || index >= EntryCount)
            {
                return false;
            }

            currentIndex = index;
            return PresentCurrent();
        }

        private void Awake()
        {
            currentIndex = Mathf.Clamp(initialIndex, 0, EntryCount - 1);
            ResolvePhaseCoordinatorIfNeeded();
        }

        private void OnEnable()
        {
            if (presentationController == null)
            {
                SetFailure("测试场景缺少 InstructionPresentationController。");
                return;
            }

            presentationController.StateChanged +=
                HandlePresentationControllerStateChanged;
            if (presentationController.GhostPlayer != null)
            {
                presentationController.GhostPlayer.Loaded +=
                    HandleGhostLoaded;
            }
            presentationController.InstructionPlaybackStarted +=
                HandlePresentationStateChanged;
            presentationController.InstructionPlaybackCompleted +=
                HandlePresentationStateChanged;
            presentationController.PresentationFaulted +=
                HandlePresentationFaulted;
            manifestRoutine = StartCoroutine(LoadManifest());
        }

        private void Start()
        {
            ResolvePhaseCoordinatorIfNeeded();
            foreach (InteractionStudyFlowControls controls in
                FindObjectsByType<InteractionStudyFlowControls>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                ))
            {
                controls.enabled = false;
                GameObject preStartRoot = controls.PreStartRoot;
                if (preStartRoot != null)
                {
                    preStartRoot.SetActive(false);
                }
            }
        }

        private IEnumerator LoadManifest()
        {
            Status = "正在加载 31 个手语动作…";
            StateChanged?.Invoke();

            string requestUri;
            try
            {
                requestUri = BuildStreamingAssetUri(
                    contentManifestRelativePath
                );
            }
            catch (Exception exception)
            {
                SetFailure("手语清单路径无效：" + exception.Message);
                yield break;
            }

            using UnityWebRequest request = UnityWebRequest.Get(requestUri);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetFailure("手语清单加载失败：" + request.error);
                yield break;
            }

            try
            {
                contentCatalog = InteractionInstructionContentManifestReader
                    .Read(request.downloadHandler.data);
            }
            catch (Exception exception)
            {
                SetFailure("手语清单解析失败：" + exception.Message);
                yield break;
            }

            manifestRoutine = null;
            if (PresentCurrent())
            {
                Debug.Log(
                    $"[InteractionSignSequenceTest] Loaded " +
                    $"{contentCatalog.Entries.Count} entries and presented " +
                    $"sentence {CurrentSentenceId}.",
                    this
                );
            }
        }

        private bool PresentCurrent()
        {
            try
            {
                presentationController.EndPhase();
                string sentenceId = CurrentSentenceId;
                InstructionContentReference content =
                    contentCatalog.ForSentence(sentenceId);
                TaskVariant variant = TaskVariantCatalog.ForSentence(
                    sentenceId
                );
                var phasePlan = new RunPhasePlan(
                    content.PhaseId,
                    content,
                    variant
                );
                SynchronizeSceneInteractions(phasePlan);
                presentationController.BeginPhase(
                    phasePlan,
                    AssistanceCondition.TextAndPointing
                );
                Status = $"动作 {currentIndex + 1:D2} / {EntryCount} · " +
                    $"句子 {sentenceId}";
                StateChanged?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                SetFailure("手语动作无法播放：" + exception.Message);
                Debug.LogException(exception, this);
                return false;
            }
        }

        private void HandlePresentationStateChanged(
            InstructionPlaybackPass _,
            double __)
        {
            TryStartInitialPlayback();
            StateChanged?.Invoke();
        }

        private void HandlePresentationControllerStateChanged()
        {
            TryStartInitialPlayback();
            StateChanged?.Invoke();
        }

        private void HandleGhostLoaded(InstructionContentReference content)
        {
            Debug.Log(
                $"[InteractionSignSequenceTest] Pose loaded for " +
                $"{content?.SentenceId}; starting first playback.",
                this
            );
            TryStartInitialPlayback();
            StateChanged?.Invoke();
        }

        private void TryStartInitialPlayback()
        {
            if (!IsReady || presentationController == null ||
                !presentationController.PhaseActive ||
                presentationController.HasStartedFirstPlayback ||
                !presentationController.ReplayIsAvailable)
            {
                return;
            }

            // Show the selected signer as soon as its staged pose artifact is
            // ready. The Replay button remains available for the allowed replay.
            bool started = presentationController.Replay();
            Debug.Log(
                $"[InteractionSignSequenceTest] Initial playback " +
                $"started={started}, status=" +
                $"{presentationController.GhostPlayer?.Status}, " +
                $"frames={presentationController.GhostPlayer?.LoadedFrameCount}, " +
                $"error={presentationController.GhostPlayer?.LastError}",
                this
            );
        }

        private void HandlePresentationFaulted(string error)
        {
            SetFailure("手语播放失败：" + error);
        }

        private void SetFailure(string error)
        {
            Status = error;
            StateChanged?.Invoke();
            Debug.LogError("[InteractionSignSequenceTest] " + error, this);
        }

        private void SynchronizeSceneInteractions(RunPhasePlan selectedPhase)
        {
            ResolvePhaseCoordinatorIfNeeded();
            if (phaseCoordinator == null || contentCatalog == null ||
                selectedPhase == null)
            {
                return;
            }

            var phasePlans = new RunPhasePlan[PhaseSentenceRanges.PhaseCount];
            for (int phaseId = 1;
                phaseId <= PhaseSentenceRanges.PhaseCount;
                phaseId++)
            {
                InstructionContentReference content =
                    phaseId == selectedPhase.PhaseId
                        ? selectedPhase.Content
                        : FindFirstContentForPhase(phaseId);
                phasePlans[phaseId - 1] = new RunPhasePlan(
                    phaseId,
                    content,
                    TaskVariantCatalog.ForSentence(content.SentenceId)
                );
            }

            var plan = new RunPlan(
                "sign-sequence-test",
                "engineering-lab",
                "sign_sequence_test_" + Guid.NewGuid().ToString("N"),
                "sign-sequence-test-session",
                DateTimeOffset.UtcNow,
                Application.version,
                "sign-sequence-test",
                31,
                new AssistanceAssignment(
                    AssistanceCondition.TextAndPointing,
                    0,
                    0,
                    AssistanceAssignmentMode.ForcedTextAndPointing
                ),
                new SafePassword(new[] { 1, 2, 3, 4 }),
                new ChestButtonOrder(
                    new[] { "blue", "red", "yellow", "green" }
                ),
                phasePlans
            );

            phaseCoordinator.Abort();
            phaseCoordinator.Configure(plan);
            phaseCoordinator.Synchronize(
                PhaseExecutionSnapshot.CreateEngineeringLabActivePhase(
                    selectedPhase.PhaseId
                )
            );
            phaseCoordinator.Enable();
        }

        private void ResolvePhaseCoordinatorIfNeeded()
        {
            if (phaseCoordinator != null)
            {
                return;
            }

            InteractionPhaseCoordinator[] candidates = FindObjectsByType<
                InteractionPhaseCoordinator>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );
            if (candidates.Length == 1)
            {
                phaseCoordinator = candidates[0];
            }
        }

        private InstructionContentReference FindFirstContentForPhase(int phaseId)
        {
            for (int index = 0; index < contentCatalog.Entries.Count; index++)
            {
                InstructionContentReference candidate =
                    contentCatalog.Entries[index];
                if (candidate.PhaseId == phaseId)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"The instruction catalog has no content for phase {phaseId}."
            );
        }

        private static string BuildStreamingAssetUri(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException(
                    "Manifest relative path is required.",
                    nameof(relativePath)
                );
            }

            string combined = Path.Combine(
                Application.streamingAssetsPath,
                relativePath.Trim().TrimStart('/', '\\')
            ).Replace('\\', '/');
            if (combined.StartsWith("jar:", StringComparison.OrdinalIgnoreCase) ||
                combined.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                combined.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                combined.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return combined;
            }

            return new Uri(combined, UriKind.Absolute).AbsoluteUri;
        }

        private void OnDisable()
        {
            if (manifestRoutine != null)
            {
                StopCoroutine(manifestRoutine);
                manifestRoutine = null;
            }
            if (presentationController != null)
            {
                presentationController.StateChanged -=
                    HandlePresentationControllerStateChanged;
                if (presentationController.GhostPlayer != null)
                {
                    presentationController.GhostPlayer.Loaded -=
                        HandleGhostLoaded;
                }
                presentationController.InstructionPlaybackStarted -=
                    HandlePresentationStateChanged;
                presentationController.InstructionPlaybackCompleted -=
                    HandlePresentationStateChanged;
                presentationController.PresentationFaulted -=
                    HandlePresentationFaulted;
                presentationController.EndPhase();
            }
        }
    }

    /// <summary>
    /// Reuses the copied scene's auxiliary controls and turns them into Move,
    /// Previous, Replay, and Next controls for the test navigator.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionSignSequenceTestControlsBase : MonoBehaviour
    {
        [SerializeField]
        private InteractionSignSequenceTestController sequenceController;

        [SerializeField]
        private InteractionInstructionControls sourceControls;

        private Button previousButton;
        private Button replayButton;
        private Button nextButton;
        private Button moveButton;
        private Button menuToggleButton;
        private TMP_Text statusLabel;
        private bool bound;
        private bool menuExpanded = true;

        public Button MoveButton => moveButton;
        public Button PreviousButton => previousButton;
        public Button ReplayButton => replayButton;
        public Button NextButton => nextButton;

        // Build-time scene validation runs outside play mode, so Unity does
        // not invoke Awake/OnEnable to populate the copied UGUI references.
        // Keep this explicit and idempotent so validation inspects the same
        // controls that runtime initialization uses.
        public void EnsureVisualsForValidation()
        {
            EnsureVisuals();
        }

        public void Configure(
            InteractionSignSequenceTestController controller,
            InteractionInstructionControls copiedControls)
        {
            Unbind();
            sequenceController = controller ??
                throw new ArgumentNullException(nameof(controller));
            sourceControls = copiedControls ??
                throw new ArgumentNullException(nameof(copiedControls));
            sourceControls.enabled = false;
            menuExpanded = true;
            EnsureVisuals();
            Bind();
            Refresh();
        }

        private void Awake()
        {
            if (sourceControls != null)
            {
                sourceControls.enabled = false;
            }
            EnsureVisuals();
        }

        private void OnEnable()
        {
            EnsureVisuals();
            Bind();
            Refresh();
        }

        private void EnsureVisuals()
        {
            if (sourceControls == null)
            {
                return;
            }

            moveButton ??= sourceControls.MoveButton;
            previousButton ??= sourceControls.GiveUpButton;
            replayButton ??= sourceControls.ReplayButton;
            nextButton ??= sourceControls.AbortButton;
            if (moveButton == null || previousButton == null || replayButton == null ||
                nextButton == null)
            {
                return;
            }

            DisableHoldToConfirm(nextButton);
            // Keep the copied production object names intact. The disabled
            // source component still runs Awake when its GameObject loads and
            // uses these names to resolve (rather than recreate) its visuals.
            ConfigureButton(
                moveButton,
                "MoveAlongView",
                "\u5411\u89c6\u7ebf\u65b9\u5411\u79fb\u52a8 20 cm",
                -360f
            );
            ConfigureButton(previousButton, "GiveUpPhase", "\u4e0a\u4e00\u4e2a", -120f);
            ConfigureButton(replayButton, "Replay", "\u91cd\u64ad", 120f);
            ConfigureButton(nextButton, "AbortRun", "\u4e0b\u4e00\u4e2a", 360f);
            EnsureStatusLabel(replayButton.transform.parent);
            EnsureMenuToggle(replayButton.transform.parent);
            ApplyMenuVisibility();
        }

        private void EnsureStatusLabel(Transform canvasRoot)
        {
            Transform existing = canvasRoot.Find("SequenceStatus");
            GameObject statusObject = existing != null
                ? existing.gameObject
                : new GameObject(
                    "SequenceStatus",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI)
                );
            if (existing == null)
            {
                statusObject.transform.SetParent(canvasRoot, false);
            }

            statusLabel = statusObject.GetComponent<TextMeshProUGUI>();
            RectTransform rect = statusLabel.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(680f, 34f);
            rect.anchoredPosition = new Vector2(0f, 67f);
            statusLabel.font = FindButtonLabel(replayButton)?.font;
            statusLabel.fontSize = 22f;
            statusLabel.enableAutoSizing = true;
            statusLabel.fontSizeMin = 14f;
            statusLabel.fontSizeMax = 22f;
            statusLabel.alignment = TextAlignmentOptions.Center;
            statusLabel.color = Color.white;
            statusLabel.raycastTarget = false;
            statusLabel.richText = false;

            RectTransform canvasRect = canvasRoot as RectTransform;
            if (canvasRect != null)
            {
                canvasRect.sizeDelta = new Vector2(960f, 180f);
            }
        }

        private void EnsureMenuToggle(Transform canvasRoot)
        {
            Transform existing = canvasRoot.Find("MenuToggle");
            GameObject toggleObject = existing != null
                ? existing.gameObject
                : new GameObject(
                    "MenuToggle",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(InteractionRoundedRectangleGraphic),
                    typeof(Button)
                );
            if (existing == null)
            {
                toggleObject.transform.SetParent(canvasRoot, false);
            }

            RectTransform rect = toggleObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(178f, 52f);
            rect.anchoredPosition = new Vector2(385f, 58f);

            InteractionRoundedRectangleGraphic graphic =
                toggleObject.GetComponent<InteractionRoundedRectangleGraphic>();
            graphic.color = new Color(0.12f, 0.18f, 0.24f, 0.96f);
            graphic.CornerRadius = 16f;
            graphic.CornerSegments = 8;

            menuToggleButton = toggleObject.GetComponent<Button>();
            menuToggleButton.targetGraphic = graphic;
            ColorBlock colors = menuToggleButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.colorMultiplier = 1f;
            menuToggleButton.colors = colors;

            Transform existingLabel = toggleObject.transform.Find("Label");
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
                labelObject.transform.SetParent(toggleObject.transform, false);
            }

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 6f);
            labelRect.offsetMax = new Vector2(-8f, -6f);
            label.font = FindButtonLabel(replayButton)?.font;
            label.fontSize = 20f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f;
            label.fontSizeMax = 20f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.richText = false;
        }

        private void ApplyMenuVisibility()
        {
            if (menuToggleButton == null)
            {
                return;
            }

            moveButton?.gameObject.SetActive(menuExpanded);
            previousButton?.gameObject.SetActive(menuExpanded);
            replayButton?.gameObject.SetActive(menuExpanded);
            nextButton?.gameObject.SetActive(menuExpanded);
            statusLabel?.gameObject.SetActive(menuExpanded);

            TMP_Text label = FindButtonLabel(menuToggleButton);
            if (label != null)
            {
                label.text = menuExpanded
                    ? "\u6536\u8d77\u83dc\u5355"
                    : "\u663e\u793a\u83dc\u5355";
            }
            menuToggleButton.gameObject.SetActive(true);
        }

        private static void DisableHoldToConfirm(Button button)
        {
            InteractionHoldToConfirm hold =
                button.GetComponent<InteractionHoldToConfirm>();
            if (hold != null)
            {
                hold.enabled = false;
            }
        }

        private static void ConfigureButton(
            Button button,
            string objectName,
            string labelText,
            float x)
        {
            button.name = objectName;
            button.onClick.RemoveAllListeners();
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, 0f);
            TMP_Text label = FindButtonLabel(button);
            if (label != null)
            {
                label.text = labelText;
            }
        }

        private static TMP_Text FindButtonLabel(Button button)
        {
            return button.transform.Find("Label")?.GetComponent<TMP_Text>();
        }

        private void Bind()
        {
            if (bound || !isActiveAndEnabled || sequenceController == null ||
                moveButton == null || previousButton == null || replayButton == null ||
                nextButton == null)
            {
                return;
            }

            sequenceController.StateChanged += Refresh;
            moveButton.onClick.AddListener(HandleMove);
            previousButton.onClick.AddListener(HandlePrevious);
            replayButton.onClick.AddListener(HandleReplay);
            nextButton.onClick.AddListener(HandleNext);
            menuToggleButton.onClick.AddListener(HandleMenuToggle);
            bound = true;
        }

        private void Refresh()
        {
            if (sequenceController == null || moveButton == null || previousButton == null)
            {
                return;
            }

            moveButton.interactable = sourceControls != null &&
                sourceControls.SeatedRigMover != null &&
                sourceControls.SeatedRigMover.isActiveAndEnabled;
            previousButton.interactable = sequenceController.CanMovePrevious;
            replayButton.interactable = sequenceController.CanReplay;
            nextButton.interactable = sequenceController.CanMoveNext;
            if (statusLabel != null)
            {
                statusLabel.text = sequenceController.Status;
            }
            ApplyMenuVisibility();
        }

        private void HandleMenuToggle()
        {
            menuExpanded = !menuExpanded;
            ApplyMenuVisibility();
        }

        private void HandlePrevious()
        {
            sequenceController.TryMovePrevious();
        }

        private void HandleMove()
        {
            InteractionSeatedRigMover mover = sourceControls?.SeatedRigMover;
            if (mover == null)
            {
                return;
            }

            Vector3 before = mover.transform.position;
            mover.MoveAlongCurrentView();
            Debug.Log(
                $"[InteractionSignSequenceTest] Move20cm " +
                $"before={before} after={mover.transform.position} " +
                $"delta={(mover.transform.position - before).magnitude:F3}m",
                this
            );
        }

        private void HandleReplay()
        {
            sequenceController.ReplayCurrent();
        }

        private void HandleNext()
        {
            sequenceController.TryMoveNext();
        }

        private void Unbind()
        {
            if (!bound)
            {
                return;
            }
            if (sequenceController != null)
            {
                sequenceController.StateChanged -= Refresh;
            }
            moveButton?.onClick.RemoveListener(HandleMove);
            previousButton?.onClick.RemoveListener(HandlePrevious);
            replayButton?.onClick.RemoveListener(HandleReplay);
            nextButton?.onClick.RemoveListener(HandleNext);
            menuToggleButton?.onClick.RemoveListener(HandleMenuToggle);
            bound = false;
        }

        private void OnDisable()
        {
            Unbind();
        }
    }
}
