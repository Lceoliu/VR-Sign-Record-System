using System;
using System.Linq;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.PhaseAdapters;
using SignVR.Interaction.Presentation;
using SignVR.SceneFlow;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SignVR.Editor.Interaction
{
    public static class CoinInteractionLabSetup
    {
        public const string ScenePath =
            "Assets/Scenes/CoinInteractionLab.unity";
        private const string SurfaceName = "CoinInteractionLabSurface";
        private const string ControllerName = "CoinInteractionLabController";

        [MenuItem("Tools/SignVR/Interaction/Generate Coin Interaction Lab")]
        public static void GenerateAndSaveFromMenu()
        {
            GenerateAndSaveForAutomation();
        }

        [MenuItem("Tools/SignVR/Interaction/Validate Coin Interaction Lab")]
        public static void ValidateFromMenu()
        {
            ValidateForAutomation();
        }

        public static void GenerateAndSaveForAutomation()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    InteractionLabContract.ScenePath))
            {
                throw new InvalidOperationException(
                    "Generate the canonical InteractionLab before its coin lab."
                );
            }
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) &&
                !AssetDatabase.CopyAsset(
                    InteractionLabContract.ScenePath,
                    ScenePath
                ))
            {
                throw new InvalidOperationException(
                    "Unity could not copy InteractionLab into the coin lab."
                );
            }

            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single
            );
            SetupLoadedScene(scene);
            ValidateLoadedScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException(
                    "Unity could not save CoinInteractionLab."
                );
            }
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[CoinInteractionLabSetup] Generation and validation completed."
            );
        }

        public static void ValidateForAutomation()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single
                );
            }
            ValidateLoadedScene(scene);
            Debug.Log("[CoinInteractionLabSetup] Validation passed.");
        }

        public static void ValidateLoadedScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(scene.path, ScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The loaded CoinInteractionLab scene is required."
                );
            }

            CoinInteractionLabController[] controllers = scene
                .GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<
                    CoinInteractionLabController>(true))
                .ToArray();
            if (controllers.Length != 1)
            {
                throw new InvalidOperationException(
                    "CoinInteractionLab requires exactly one lab controller."
                );
            }
            CoinInteractionLabController controller = controllers[0];
            if (controller.Coordinator == null ||
                controller.StatusLabel == null ||
                controller.CoinButtons.Count != 3 ||
                controller.CoinButtons.Any(item => item == null) ||
                controller.PlateButtons.Count != 3 ||
                controller.PlateButtons.Any(item => item == null) ||
                controller.ResetButton == null)
            {
                throw new InvalidOperationException(
                    "CoinInteractionLab controller/UI wiring is incomplete."
                );
            }

            InteractionTargetBinding[] coinBindings = scene
                .GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<
                    InteractionTargetBinding>(true))
                .Where(item => item.TargetId == "coin_dragon" ||
                    item.TargetId == "coin_a" || item.TargetId == "coin_b")
                .ToArray();
            if (coinBindings.Length != 3 || coinBindings.Any(item =>
                    item.PhaseId != 2 ||
                    !item.EnablesMovablePhysicsWhenAvailable ||
                    item.InputColliders.Count == 0 ||
                    item.InteractionBehaviours.Count == 0 ||
                    !item.AvailabilityObjects.Any(value =>
                        value != null && value.name ==
                            "ISDK_HandGrabInteraction")))
            {
                throw new InvalidOperationException(
                    "CoinInteractionLab requires all three movable W7 coins."
                );
            }

            InteractionPlacementBinding[] placements = scene
                .GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<
                    InteractionPlacementBinding>(true))
                .ToArray();
            if (placements.Length != 3 || placements.Any(item =>
                    item.Adapter == null || item.Adapter.PhaseId != 2 ||
                    item.PlacementCollider == null || item.SnapPoint == null))
            {
                throw new InvalidOperationException(
                    "CoinInteractionLab requires all three plate placements."
                );
            }

            Transform uiAnchor = FindRequiredPath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName +
                "/InteractionUiAnchor"
            );
            Transform surface = uiAnchor.Find(SurfaceName);
            if (surface == null || !surface.gameObject.activeSelf ||
                surface.GetComponent<Canvas>() == null ||
                surface.GetComponent<WorldSpacePokeCanvas>() == null)
            {
                throw new InvalidOperationException(
                    "CoinInteractionLab requires its active hand UI surface."
                );
            }
            Transform studySurface = uiAnchor.Find("W8StudyStartSurface");
            if (studySurface != null && studySurface.gameObject.activeSelf)
            {
                throw new InvalidOperationException(
                    "The formal Study start surface must be hidden in coin lab."
                );
            }
            if (FindSceneComponents<InteractionStudyFlowController>(scene)
                .Any(item => item.enabled) ||
                FindSceneComponents<InteractionStudyFlowControls>(scene)
                    .Any(item => item.enabled) ||
                FindSceneComponents<InstructionPresentationController>(scene)
                    .Any(item => item.enabled))
            {
                throw new InvalidOperationException(
                    "Formal Study orchestration must be disabled in coin lab."
                );
            }
        }

        private static void SetupLoadedScene(Scene scene)
        {
            RefreshMovableCoinSnapshots(scene);
            InteractionPhaseCoordinator coordinator =
                FindSceneComponents<InteractionPhaseCoordinator>(scene)
                    .SingleOrDefault() ??
                throw new InvalidOperationException(
                    "Coin lab source is missing its W7 coordinator."
                );
            foreach (Behaviour behaviour in
                     FindSceneComponents<InteractionStudyFlowController>(scene)
                         .Cast<Behaviour>()
                         .Concat(FindSceneComponents<
                             InteractionStudyFlowControls>(scene))
                         .Concat(FindSceneComponents<
                             InstructionPresentationController>(scene)))
            {
                Undo.RecordObject(behaviour, "Disable formal Study in coin lab");
                behaviour.enabled = false;
                EditorUtility.SetDirty(behaviour);
            }

            Transform runtimeAnchor = FindRequiredPath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.RuntimeSystemsAnchorName
            );
            Transform controllerTransform = runtimeAnchor.Find(ControllerName);
            GameObject controllerObject = controllerTransform == null
                ? CreateObject(runtimeAnchor, ControllerName, false)
                : controllerTransform.gameObject;
            CoinInteractionLabController controller =
                GetOrAdd<CoinInteractionLabController>(controllerObject);

            Transform uiAnchor = FindRequiredPath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName +
                "/InteractionUiAnchor"
            );
            Transform studySurface = uiAnchor.Find("W8StudyStartSurface");
            if (studySurface != null)
            {
                Undo.RecordObject(
                    studySurface.gameObject,
                    "Hide formal Study UI in coin lab"
                );
                studySurface.gameObject.SetActive(false);
            }

            GameObject surface = EnsureSurface(uiAnchor, scene);
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(
                "Fonts/SignVRChinese SDF"
            ) ?? TMP_Settings.defaultFontAsset;
            TMP_Text title = EnsureText(
                surface.transform,
                "Title",
                "金币抓取与放盘测试",
                font,
                34f
            );
            SetRect(title.rectTransform, new Vector2(0f, 290f),
                new Vector2(820f, 60f));
            TMP_Text coinHeading = EnsureText(
                surface.transform, "CoinHeading", "选择目标金币", font, 24f
            );
            SetRect(coinHeading.rectTransform, new Vector2(0f, 220f),
                new Vector2(820f, 45f));
            Button[] coinButtons = CreateButtonRow(
                surface.transform,
                "Coin",
                new[] { "龙纹金币", "金币 A", "金币 B" },
                140f,
                font,
                new Color(0.16f, 0.45f, 0.82f, 1f)
            );
            TMP_Text plateHeading = EnsureText(
                surface.transform, "PlateHeading", "选择目标盘子", font, 24f
            );
            SetRect(plateHeading.rectTransform, new Vector2(0f, 65f),
                new Vector2(820f, 45f));
            Button[] plateButtons = CreateButtonRow(
                surface.transform,
                "Plate",
                new[] { "龙纹盘", "盘子 A", "盘子 B" },
                -15f,
                font,
                new Color(0.55f, 0.34f, 0.75f, 1f)
            );
            Button reset = EnsureButton(
                surface.transform,
                "ResetTrial",
                "重置本轮",
                font,
                new Color(0.78f, 0.34f, 0.12f, 1f)
            );
            SetRect(reset.GetComponent<RectTransform>(),
                new Vector2(0f, -145f), new Vector2(360f, 78f));
            TMP_Text status = EnsureText(
                surface.transform,
                "Status",
                "进入 Play Mode 后自动开始第二阶段。",
                font,
                23f
            );
            status.textWrappingMode = TextWrappingModes.Normal;
            SetRect(status.rectTransform, new Vector2(0f, -265f),
                new Vector2(820f, 120f));

            Undo.RecordObject(controller, "Wire Coin Interaction Lab");
            controller.Configure(
                coordinator,
                status,
                coinButtons,
                plateButtons,
                reset
            );
            EditorUtility.SetDirty(controller);
        }

        private static void RefreshMovableCoinSnapshots(Scene scene)
        {
            InteractionTargetBinding[] movableCoins = scene
                .GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<
                    InteractionTargetBinding>(true))
                .Where(item => item.EnablesMovablePhysicsWhenAvailable)
                .ToArray();
            foreach (InteractionTargetBinding binding in movableCoins)
            {
                Undo.RecordObject(
                    binding,
                    "Refresh Coin Interaction Lab authored state"
                );
                binding.ConfigureMovable(
                    binding.TargetId,
                    binding.Adapter,
                    binding.InteractionBehaviours.ToArray(),
                    binding.InputColliders.ToArray(),
                    binding.AvailabilityObjects.ToArray()
                );
                EditorUtility.SetDirty(binding);
            }
        }

        private static GameObject EnsureSurface(Transform parent, Scene scene)
        {
            Transform existing = parent.Find(SurfaceName);
            GameObject root;
            if (existing == null)
            {
                root = new GameObject(
                    SurfaceName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster),
                    typeof(WorldSpacePokeCanvas)
                );
                Undo.RegisterCreatedObjectUndo(
                    root,
                    "Create Coin Interaction Lab surface"
                );
                root.transform.SetParent(parent, false);
            }
            else
            {
                root = existing.gameObject;
            }
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, 720f);
            rect.localPosition = new Vector3(0f, 0.20f, 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.001f;

            Canvas canvas = GetOrAdd<Canvas>(root);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 29992;
            Camera hmd = FindSceneComponents<Camera>(scene)
                .Single(item => item.CompareTag("MainCamera"));
            canvas.worldCamera = hmd;
            CanvasScaler scaler = GetOrAdd<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 100f;
            GetOrAdd<GraphicRaycaster>(root);
            WorldSpacePokeCanvas poke = GetOrAdd<WorldSpacePokeCanvas>(root);
            poke.Configure(canvas);
            poke.EnsurePokeInteraction();

            Image background = GetOrAdd<Image>(EnsureUiObject(
                root.transform, "Background"));
            background.color = new Color(0.025f, 0.04f, 0.07f, 0.96f);
            background.raycastTarget = false;
            Stretch(background.rectTransform, 0f);
            return root;
        }

        private static Button[] CreateButtonRow(
            Transform parent,
            string prefix,
            string[] labels,
            float y,
            TMP_FontAsset font,
            Color color)
        {
            var result = new Button[labels.Length];
            for (int index = 0; index < labels.Length; index++)
            {
                result[index] = EnsureButton(
                    parent,
                    prefix + index,
                    labels[index],
                    font,
                    color
                );
                SetRect(
                    result[index].GetComponent<RectTransform>(),
                    new Vector2((index - 1) * 270f, y),
                    new Vector2(240f, 78f)
                );
            }
            return result;
        }

        private static Button EnsureButton(
            Transform parent,
            string name,
            string label,
            TMP_FontAsset font,
            Color color)
        {
            GameObject root = EnsureUiObject(parent, name);
            Image image = GetOrAdd<Image>(root);
            image.color = color;
            Button button = GetOrAdd<Button>(root);
            button.targetGraphic = image;
            TMP_Text text = EnsureText(
                root.transform, "Label", label, font, 24f
            );
            Stretch(text.rectTransform, 8f);
            return button;
        }

        private static TMP_Text EnsureText(
            Transform parent,
            string name,
            string value,
            TMP_FontAsset font,
            float fontSize)
        {
            GameObject root = EnsureUiObject(parent, name);
            TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(root);
            text.text = value;
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = false;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private static GameObject EnsureUiObject(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            return existing == null
                ? CreateUiObject(parent, name)
                : existing.gameObject;
        }

        private static GameObject CreateUiObject(Transform parent, string name)
        {
            return CreateObject(parent, name, true);
        }

        private static GameObject CreateObject(
            Transform parent,
            string name,
            bool rectTransform)
        {
            GameObject value = rectTransform
                ? new GameObject(name, typeof(RectTransform))
                : new GameObject(name);
            Undo.RegisterCreatedObjectUndo(value, "Create Coin Interaction Lab");
            value.transform.SetParent(parent, false);
            return value;
        }

        private static T GetOrAdd<T>(GameObject gameObject)
            where T : Component
        {
            return gameObject.GetComponent<T>() ?? Undo.AddComponent<T>(gameObject);
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private static Transform FindRequiredPath(Scene scene, string path)
        {
            Transform result = scene.GetRootGameObjects()
                .Select(item => item.transform)
                .Select(root => path == root.name
                    ? root
                    : path.StartsWith(root.name + "/", StringComparison.Ordinal)
                        ? root.Find(path.Substring(root.name.Length + 1))
                        : null)
                .SingleOrDefault(item => item != null);
            return result ?? throw new InvalidOperationException(
                "Coin lab source is missing '" + path + "'."
            );
        }

        private static T[] FindSceneComponents<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<T>(true))
                .ToArray();
        }
    }
}
