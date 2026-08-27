using System;
using System.Collections;
using System.IO;
using SignVR.Interaction.CaptureHost;
using SignVR.Interaction.Core;
using SignVR.Interaction.Orchestration;
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
            presentationController = presentation ??
                throw new ArgumentNullException(nameof(presentation));
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
        }

        private void OnEnable()
        {
            if (presentationController == null)
            {
                SetFailure("测试场景缺少 InstructionPresentationController。");
                return;
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
            PresentCurrent();
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
            StateChanged?.Invoke();
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
    /// Reuses the copied scene's three peer control buttons and turns them into
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
        private TMP_Text statusLabel;
        private bool bound;

        public Button PreviousButton => previousButton;
        public Button ReplayButton => replayButton;
        public Button NextButton => nextButton;

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
            if (sourceControls == null || previousButton != null)
            {
                return;
            }

            previousButton = sourceControls.GiveUpButton;
            replayButton = sourceControls.ReplayButton;
            nextButton = sourceControls.AbortButton;
            if (previousButton == null || replayButton == null ||
                nextButton == null)
            {
                return;
            }

            DisableHoldToConfirm(nextButton);
            // Keep the copied production object names intact. The disabled
            // source component still runs Awake when its GameObject loads and
            // uses these names to resolve (rather than recreate) its visuals.
            ConfigureButton(previousButton, "GiveUpPhase", "上一个", -240f);
            ConfigureButton(replayButton, "Replay", "重播", 0f);
            ConfigureButton(nextButton, "AbortRun", "下一个", 240f);
            EnsureStatusLabel(replayButton.transform.parent);
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
                canvasRect.sizeDelta = new Vector2(720f, 180f);
            }
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
                previousButton == null || replayButton == null ||
                nextButton == null)
            {
                return;
            }

            sequenceController.StateChanged += Refresh;
            previousButton.onClick.AddListener(HandlePrevious);
            replayButton.onClick.AddListener(HandleReplay);
            nextButton.onClick.AddListener(HandleNext);
            bound = true;
        }

        private void Refresh()
        {
            if (sequenceController == null || previousButton == null)
            {
                return;
            }

            previousButton.interactable = sequenceController.CanMovePrevious;
            replayButton.interactable = sequenceController.CanReplay;
            nextButton.interactable = sequenceController.CanMoveNext;
            if (statusLabel != null)
            {
                statusLabel.text = sequenceController.Status;
            }
        }

        private void HandlePrevious()
        {
            sequenceController.TryMovePrevious();
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
            previousButton?.onClick.RemoveListener(HandlePrevious);
            replayButton?.onClick.RemoveListener(HandleReplay);
            nextButton?.onClick.RemoveListener(HandleNext);
            bound = false;
        }

        private void OnDisable()
        {
            Unbind();
        }
    }
}
