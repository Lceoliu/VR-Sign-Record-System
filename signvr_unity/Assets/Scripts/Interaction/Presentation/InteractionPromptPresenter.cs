using System;
using SignVR.Interaction.Core;
using SignVR.Recording;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.Interaction.Presentation
{
    /// <summary>
    /// Interaction-only transcript bubble. It reuses the Recorder's canonical
    /// sentence catalog and visual resources but has no recording-state UI.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    public sealed class InteractionPromptPresenter : MonoBehaviour
    {
        private const string VisualRootName = "InstructionBubbleCanvas";
        private const string OverlayShaderResource =
            "Shaders/RecordingUiOverlay";
        private const string OverlayShaderName =
            "SignVR/Recording UI Overlay";
        private const string TextOverlayMaterialResource =
            "Fonts & Materials/LiberationSans SDF - Overlay";
        private const string TextOverlayShaderName =
            "TextMeshPro/Mobile/Distance Field Overlay";

        // This is the one Recorder-owned canonical table, not a second list of
        // Interaction subtitle strings.
        private static readonly RecordingSentence[] CanonicalSentences =
            RecordingPointingSentenceCatalog.CreateSentences();

        [Header("Stable world anchors")]
        [SerializeField]
        [Tooltip("InstructionSignerAnchor root; never assign a moving head bone.")]
        private Transform signerRoot;

        [SerializeField]
        private Transform participantHmd;

        [SerializeField]
        [Min(1f)]
        private float heightAboveSignerRoot = 2.15f;

        [Header("Canonical Recorder appearance")]
        [SerializeField]
        private TMP_FontAsset font;

        [SerializeField]
        private Color borderColor = new(0.32f, 0.38f, 0.42f, 0.98f);

        [SerializeField]
        private Color backgroundColor = new(0.025f, 0.032f, 0.038f, 0.97f);

        [SerializeField]
        private Color promptColor = new(0.96f, 0.97f, 0.98f, 1f);

        [SerializeField]
        [Range(8f, 64f)]
        private float cornerRadius = 34f;

        private GameObject visualRoot;
        private Canvas bubbleCanvas;
        private CanvasGroup canvasGroup;
        private TextMeshProUGUI promptLabel;
        private Material uiOverlayMaterial;
        private Material textOverlayMaterial;
        private string sentenceId = string.Empty;
        private bool visible;

        public event Action Shown;
        public event Action Hidden;

        public Transform SignerRoot => signerRoot;

        public Transform ParticipantHmd => participantHmd;

        public string SentenceId => sentenceId;

        public string PromptText => promptLabel != null
            ? promptLabel.text
            : string.Empty;

        public bool IsVisible => visible;

        public Canvas BubbleCanvas => bubbleCanvas;

        public TMP_Text PromptLabel => promptLabel;

        public void Configure(Transform stableSignerRoot, Transform hmd)
        {
            signerRoot = stableSignerRoot;
            participantHmd = hmd;
            EnsureVisuals();
            ResolveCamera();
            UpdateBillboard();
        }

        public void SetPhase(RunPhasePlan phasePlan)
        {
            if (phasePlan == null)
            {
                throw new ArgumentNullException(nameof(phasePlan));
            }
            SetSentence(phasePlan.SentenceId);
        }

        public void SetSentence(string canonicalSentenceId)
        {
            string prompt = ResolveCanonicalPrompt(canonicalSentenceId);
            sentenceId = canonicalSentenceId;
            EnsureVisuals();
            promptLabel.text = prompt;
            SetVisible(false);
        }

        public void SetVisible(bool shouldShow)
        {
            EnsureVisuals();
            bool resolvedVisibility = shouldShow &&
                !string.IsNullOrWhiteSpace(sentenceId) &&
                !string.IsNullOrWhiteSpace(promptLabel.text);
            if (visible == resolvedVisibility)
            {
                return;
            }

            visible = resolvedVisibility;
            visualRoot.SetActive(visible);
            if (visible)
            {
                Shown?.Invoke();
            }
            else
            {
                Hidden?.Invoke();
            }
        }

        private void Awake()
        {
            EnsureVisuals();
            ResolveCamera();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (!visible)
            {
                return;
            }

            ResolveCamera();
            UpdateBillboard();
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

            RectTransform canvasRect =
                visualRoot.GetComponent<RectTransform>();
            bubbleCanvas = visualRoot.GetComponent<Canvas>();
            CanvasScaler scaler = visualRoot.GetComponent<CanvasScaler>();
            canvasGroup = visualRoot.GetComponent<CanvasGroup>();

            canvasRect.sizeDelta = new Vector2(620f, 180f);
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.001f;
            bubbleCanvas.renderMode = RenderMode.WorldSpace;
            bubbleCanvas.overrideSorting = true;
            bubbleCanvas.sortingOrder = 30000;
            bubbleCanvas.additionalShaderChannels =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 100f;
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Transform existingPanel = visualRoot.transform.Find("Bubble");
            GameObject panel = existingPanel != null
                ? existingPanel.gameObject
                : CreateUiObject("Bubble", visualRoot.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            Stretch(panelRect);

            InteractionRoundedRectangleGraphic border =
                panel.GetComponent<InteractionRoundedRectangleGraphic>() ??
                panel.AddComponent<InteractionRoundedRectangleGraphic>();
            border.raycastTarget = false;
            border.color = borderColor;
            border.CornerRadius = cornerRadius;
            border.CornerSegments = 10;

            Shadow shadow = panel.GetComponent<Shadow>() ??
                panel.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(0f, -6f);
            shadow.useGraphicAlpha = true;

            Transform existingSurface = panel.transform.Find("Surface");
            GameObject surface = existingSurface != null
                ? existingSurface.gameObject
                : CreateUiObject("Surface", panel.transform);
            RectTransform surfaceRect = surface.GetComponent<RectTransform>();
            Stretch(surfaceRect);
            surfaceRect.offsetMin = new Vector2(4f, 4f);
            surfaceRect.offsetMax = new Vector2(-4f, -4f);
            InteractionRoundedRectangleGraphic background =
                surface.GetComponent<InteractionRoundedRectangleGraphic>() ??
                surface.AddComponent<InteractionRoundedRectangleGraphic>();
            background.raycastTarget = false;
            background.color = backgroundColor;
            background.CornerRadius = Mathf.Max(8f, cornerRadius - 4f);
            background.CornerSegments = 10;

            Transform existingPrompt = panel.transform.Find("Prompt");
            GameObject promptObject = existingPrompt != null
                ? existingPrompt.gameObject
                : new GameObject(
                    "Prompt",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI)
                );
            if (existingPrompt == null)
            {
                promptObject.transform.SetParent(panel.transform, false);
            }
            promptLabel = promptObject.GetComponent<TextMeshProUGUI>();
            RectTransform promptRect = promptLabel.rectTransform;
            Stretch(promptRect);
            promptRect.offsetMin = new Vector2(32f, 24f);
            promptRect.offsetMax = new Vector2(-32f, -24f);
            promptLabel.raycastTarget = false;
            promptLabel.richText = false;
            promptLabel.fontStyle = FontStyles.Normal;
            promptLabel.fontSize = 40f;
            promptLabel.enableAutoSizing = true;
            promptLabel.fontSizeMin = 20f;
            promptLabel.fontSizeMax = 40f;
            promptLabel.alignment = TextAlignmentOptions.Center;
            promptLabel.color = promptColor;
            promptLabel.textWrappingMode = TextWrappingModes.Normal;
            promptLabel.overflowMode = TextOverflowModes.Ellipsis;

            font ??= Resources.Load<TMP_FontAsset>("Fonts/SignVRChinese SDF");
            if (font != null)
            {
                promptLabel.font = font;
            }
            EnsureOverlayMaterials(border, background);
            visualRoot.SetActive(visible);
        }

        private void EnsureOverlayMaterials(
            InteractionRoundedRectangleGraphic border,
            InteractionRoundedRectangleGraphic background)
        {
            Shader shader = Resources.Load<Shader>(OverlayShaderResource) ??
                Shader.Find(OverlayShaderName);
            if (shader != null && uiOverlayMaterial == null)
            {
                uiOverlayMaterial = new Material(shader)
                {
                    name = "Interaction Prompt UI Overlay",
                    hideFlags = HideFlags.DontSave
                };
            }
            border.material = uiOverlayMaterial;
            background.material = uiOverlayMaterial;

            Material packaged = Resources.Load<Material>(
                TextOverlayMaterialResource
            );
            Shader textShader = packaged != null
                ? packaged.shader
                : Shader.Find(TextOverlayShaderName);
            Material source = promptLabel.fontSharedMaterial;
            if (textShader == null || source == null)
            {
                return;
            }

            textOverlayMaterial = new Material(textShader)
            {
                name = "Interaction Prompt Text Overlay",
                hideFlags = HideFlags.DontSave
            };
            textOverlayMaterial.CopyPropertiesFromMaterial(source);
            textOverlayMaterial.shaderKeywords = source.shaderKeywords;
            textOverlayMaterial.renderQueue = 5000;
            promptLabel.fontSharedMaterial = textOverlayMaterial;
        }

        private void ResolveCamera()
        {
            if (participantHmd == null)
            {
                Camera camera = Camera.main;
                participantHmd = camera != null ? camera.transform : null;
            }

            if (bubbleCanvas != null)
            {
                bubbleCanvas.worldCamera = participantHmd != null
                    ? participantHmd.GetComponent<Camera>()
                    : null;
            }
        }

        private void UpdateBillboard()
        {
            if (signerRoot != null)
            {
                transform.position = signerRoot.position +
                    Vector3.up * heightAboveSignerRoot;
            }

            if (participantHmd == null)
            {
                return;
            }

            Vector3 awayFromViewer = transform.position -
                participantHmd.position;
            if (awayFromViewer.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    awayFromViewer.normalized,
                    Vector3.up
                );
            }
        }

        private static string ResolveCanonicalPrompt(string sentenceId)
        {
            if (!PhaseSentenceRanges.TryParseSentenceNumber(
                    sentenceId,
                    out int number))
            {
                throw new ArgumentException(
                    "Interaction sentence IDs must be 001 through 031.",
                    nameof(sentenceId)
                );
            }

            string recordingId = $"sentence_{number:D3}";
            for (int index = 0; index < CanonicalSentences.Length; index++)
            {
                RecordingSentence sentence = CanonicalSentences[index];
                if (sentence != null && string.Equals(
                        sentence.SentenceId,
                        recordingId,
                        StringComparison.Ordinal))
                {
                    return sentence.Text;
                }
            }

            throw new InvalidOperationException(
                $"Recorder canonical sentence {recordingId} is missing."
            );
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var result = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer)
            );
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private void OnDisable()
        {
            if (visualRoot != null)
            {
                SetVisible(false);
            }
            else
            {
                visible = false;
            }
        }

        private void OnDestroy()
        {
            DestroyMaterial(uiOverlayMaterial);
            DestroyMaterial(textOverlayMaterial);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            heightAboveSignerRoot = Mathf.Max(1f, heightAboveSignerRoot);
            cornerRadius = Mathf.Clamp(cornerRadius, 8f, 64f);
        }
#endif
    }
}
