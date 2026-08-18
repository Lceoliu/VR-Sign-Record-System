using System;
using System.Linq;
using Meta.XR.Movement.Retargeting;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using SignVR.Recording;

public static class SignVRRecordingUxSetup
{
    private const string RecordingScenePath = "Assets/Scenes/Recording.unity";
    private const string SourceFontPath =
        "Assets/Fonts/NotoSansSC-VariableFont_wght.ttf";
    private const string FontAssetPath =
        "Assets/Fonts/SignVRChinese SDF.asset";
    private const string PokeButtonPrefabPath =
        "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Prefabs/OculusInteractionSamplePokeButton.prefab";
    private const string ConsoleMaterialPath =
        "Assets/Materials/RecordingDeskConsole.mat";

    private static readonly Color Ink =
        new(0.055f, 0.065f, 0.075f, 1f);
    private static readonly Color Paper =
        new(0.94f, 0.945f, 0.94f, 0.98f);
    private static readonly Color Slate =
        new(0.15f, 0.18f, 0.20f, 1f);
    private static readonly Color MutedBlue =
        new(0.16f, 0.43f, 0.56f, 1f);

    [MenuItem("SignVR/Setup Recording VR UX")]
    public static void SetupRecordingVrUx()
    {
        if (EditorSceneManager.GetActiveScene().path != RecordingScenePath)
        {
            throw new InvalidOperationException(
                $"Open {RecordingScenePath} before running this setup."
            );
        }

        TMP_FontAsset font = EnsureChineseFont();

        var coordinator = FindSceneComponent<RecordingCoordinator>("_Recording");
        var recorder = FindSceneComponent<MetaBodyMotionRecorder>("Objects/MotionRecorder");
        var teacherUI = FindSceneComponentAnywhere<RecordingTeacherUI>();
        var previewStreamer = FindSceneComponent<QuestPreviewStreamer>("_Recording");
        var retargeter = FindSceneComponent<CharacterRetargeter>(
            "Objects/StylizedCharacter"
        );
        Transform centerEye = FindTransformByPath(
            "[BuildingBlock] Camera Rig/TrackingSpace/CenterEyeAnchor"
        );
        var leftHand = FindSceneComponent<OVRHand>(
            "[BuildingBlock] Camera Rig/TrackingSpace/LeftHandAnchor/[BuildingBlock] Hand Tracking left"
        );
        var rightHand = FindSceneComponent<OVRHand>(
            "[BuildingBlock] Camera Rig/TrackingSpace/RightHandAnchor/[BuildingBlock] Hand Tracking right"
        );
        Transform mirroredCharacter = FindTransformByPath(
            "MirroredObjects/StylizedCharacterMirrored"
        );

        GameObject recordingRoot = coordinator.gameObject;
        RecordingReplayController replay =
            GetOrAdd<RecordingReplayController>(recordingRoot);
        replay.Configure(coordinator, retargeter);

        RecordingTutorialController tutorial =
            SetupTutorial(recordingRoot, coordinator, centerEye, font);
        SetupTouchscreenControls(
            recordingRoot,
            coordinator,
            replay,
            tutorial,
            font
        );
        SetupTeacherUi(teacherUI, replay, centerEye, font);
        HandCaptureBoundaryMonitor handBoundaryMonitor =
            SetupHandCaptureWarning(
                recordingRoot,
                coordinator,
                centerEye,
                teacherUI.transform,
                leftHand,
                rightHand,
                font
            );
        SetupPreviewCamera(previewStreamer, mirroredCharacter);

        ApplyFontToScene(font);
        SetInitialChinesePrompt(coordinator);

        EditorUtility.SetDirty(recorder);
        EditorUtility.SetDirty(coordinator);
        EditorUtility.SetDirty(replay);
        EditorUtility.SetDirty(teacherUI);
        EditorUtility.SetDirty(handBoundaryMonitor);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[SignVRRecordingUxSetup] Head-locked HMD HUD, touchscreen " +
            "bare-hand controls, hand-capture warning, and fixed upper-body " +
            "preview are configured."
        );
    }

    private static RecordingPromptBoard SetupPromptBoard(
        Transform teacherUiRoot,
        RecordingCoordinator coordinator,
        TMP_FontAsset font)
    {
        RectTransform promptPanel =
            teacherUiRoot.Find("PromptBoardRoot/PromptPanel") as RectTransform ??
            teacherUiRoot.Find("PromptPanel") as RectTransform;

        if (promptPanel == null)
        {
            throw new InvalidOperationException("TeacherUI/PromptPanel was not found.");
        }

        RectTransform boardRoot =
            teacherUiRoot.Find("PromptBoardRoot") as RectTransform;
        if (boardRoot == null)
        {
            boardRoot = CreateRect(
                teacherUiRoot,
                "PromptBoardRoot",
                new Vector2(1000f, 230f),
                promptPanel.anchoredPosition
            );
        }

        Undo.SetTransformParent(promptPanel, boardRoot, "Move prompt panel");
        promptPanel.anchorMin = new Vector2(0.5f, 0.5f);
        promptPanel.anchorMax = new Vector2(0.5f, 0.5f);
        promptPanel.pivot = new Vector2(0.5f, 0.5f);
        promptPanel.anchoredPosition3D = new Vector3(0f, -15f, 0f);
        promptPanel.sizeDelta = new Vector2(1000f, 180f);

        RectTransform handle =
            boardRoot.Find("PromptGrabHandle") as RectTransform;
        if (handle == null)
        {
            handle = CreateRect(
                boardRoot,
                "PromptGrabHandle",
                new Vector2(420f, 46f),
                new Vector2(0f, 98f)
            );
        }

        Image handleImage = GetOrAdd<Image>(handle.gameObject);
        handleImage.color = Slate;
        handleImage.raycastTarget = false;

        TMP_Text handleLabel = EnsureUiText(
            handle,
            "Label",
            "按住拖动提示板",
            23f,
            Color.white,
            font,
            TextAlignmentOptions.Center,
            Vector2.zero,
            handle.sizeDelta
        );
        handleLabel.fontStyle = FontStyles.Bold;

        Transform oldHandGrab = boardRoot.Find("PromptBoardHandGrab");
        if (oldHandGrab != null)
        {
            Undo.DestroyObjectImmediate(oldHandGrab.gameObject);
        }

        RemoveComponent<Grabbable>(boardRoot.gameObject);
        RemoveComponent<OneGrabTranslateTransformer>(boardRoot.gameObject);
        RemoveComponent<Rigidbody>(boardRoot.gameObject);
        RemoveComponent<BoxCollider>(handle.gameObject);

        Transform dragTarget = handle.Find("PromptBoardPokeDrag");
        if (dragTarget == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PokeButtonPrefabPath
            );
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                handle
            );
            instance.name = "PromptBoardPokeDrag";
            dragTarget = instance.transform;
        }

        dragTarget.localPosition = new Vector3(0f, 0f, -12f);
        dragTarget.localRotation = Quaternion.identity;
        dragTarget.localScale = Vector3.one * 36f;
        foreach (TMP_Text text in dragTarget.GetComponentsInChildren<TMP_Text>(true))
        {
            text.text = "按住拖动";
            text.font = font;
            text.color = Color.white;
        }

        handleLabel.gameObject.SetActive(false);
        PointableUnityEventWrapper dragEvents =
            dragTarget.GetComponentInChildren<PointableUnityEventWrapper>(true);
        PokeInteractable pokeInteractable =
            dragTarget.GetComponentInChildren<PokeInteractable>(true);
        var pokeSerialized = new SerializedObject(pokeInteractable);
        pokeSerialized.FindProperty("_cancelSelectNormal").floatValue = 1f;
        pokeSerialized.FindProperty("_cancelSelectTangent").floatValue = 1f;
        pokeSerialized.ApplyModifiedPropertiesWithoutUndo();

        RecordingPromptBoardPokeDrag pokeDrag =
            GetOrAdd<RecordingPromptBoardPokeDrag>(dragTarget.gameObject);
        pokeDrag.Configure(dragEvents, boardRoot);
        EditorUtility.SetDirty(pokeDrag);

        RecordingPromptBoard promptBoard =
            GetOrAdd<RecordingPromptBoard>(boardRoot.gameObject);
        promptBoard.Configure(
            coordinator,
            handle.gameObject,
            new Vector3(0f, 250f, 0f)
        );

        EditorUtility.SetDirty(promptBoard);
        return promptBoard;
    }

    private static void SetupTeacherUi(
        RecordingTeacherUI teacherUI,
        RecordingReplayController replay,
        Transform centerEye,
        TMP_FontAsset font)
    {
        RectTransform root = (RectTransform)teacherUI.transform;
        Undo.SetTransformParent(root, centerEye, "Attach teacher HUD to HMD");
        root.localPosition = new Vector3(0f, 0f, 1f);
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * 0.001f;
        root.sizeDelta = new Vector2(1100f, 700f);

        Canvas canvas = teacherUI.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform boardRoot =
            root.Find("PromptBoardRoot") as RectTransform;
        RectTransform promptPanel =
            boardRoot.Find("PromptPanel") as RectTransform;
        RectTransform promptTextRect =
            promptPanel.Find("PromptText") as RectTransform;

        Transform dragHandle = boardRoot.Find("PromptGrabHandle");
        if (dragHandle != null)
        {
            Undo.DestroyObjectImmediate(dragHandle.gameObject);
        }
        RemoveComponent<RecordingPromptBoard>(boardRoot.gameObject);

        boardRoot.anchoredPosition = new Vector2(0f, 255f);
        boardRoot.sizeDelta = new Vector2(820f, 120f);
        promptPanel.anchoredPosition = Vector2.zero;
        promptPanel.sizeDelta = new Vector2(820f, 108f);
        promptTextRect.offsetMin = new Vector2(32f, 14f);
        promptTextRect.offsetMax = new Vector2(-32f, -14f);

        Image promptBackground = promptPanel.GetComponent<Image>();
        promptBackground.color = new Color(Paper.r, Paper.g, Paper.b, 0.92f);
        TMP_Text promptText = promptTextRect.GetComponent<TMP_Text>();
        promptText.fontSize = 39f;
        promptText.fontStyle = FontStyles.Bold;
        promptText.color = Ink;

        RectTransform statusPanel = root.Find("StatusPanel") as RectTransform;
        statusPanel.anchoredPosition = new Vector2(-320f, 292f);
        statusPanel.sizeDelta = new Vector2(430f, 64f);
        Image statusBackground = statusPanel.GetComponent<Image>();
        statusBackground.color = new Color(Ink.r, Ink.g, Ink.b, 0.82f);

        RectTransform statusIndicator =
            statusPanel.Find("StatusIndicator") as RectTransform;
        statusIndicator.anchoredPosition = new Vector2(-190f, 0f);
        statusIndicator.sizeDelta = new Vector2(24f, 24f);

        TMP_Text statusText =
            statusPanel.Find("StatusText").GetComponent<TMP_Text>();
        RectTransform statusTextRect = (RectTransform)statusText.transform;
        statusTextRect.anchoredPosition = new Vector2(-45f, 0f);
        statusTextRect.sizeDelta = new Vector2(255f, 54f);
        statusText.fontSize = 25f;
        statusText.color = Color.white;

        TMP_Text takeText =
            statusPanel.Find("TakeText").GetComponent<TMP_Text>();
        RectTransform takeTextRect = (RectTransform)takeText.transform;
        takeTextRect.anchoredPosition = new Vector2(145f, 0f);
        takeTextRect.sizeDelta = new Vector2(120f, 46f);
        takeText.fontSize = 18f;
        takeText.color = new Color(0.76f, 0.82f, 0.84f, 1f);

        TMP_Text guidance = EnsureUiText(
            root,
            "GuidanceText",
            "踩一下外接空格键开始录制",
            22f,
            Color.white,
            font,
            TextAlignmentOptions.Center,
            new Vector2(0f, -292f),
            new Vector2(800f, 42f)
        );
        guidance.fontStyle = FontStyles.Bold;

        RectTransform frameRect = root.Find("RecordingViewportFrame") as RectTransform;
        if (frameRect == null)
        {
            frameRect = CreateRect(
                root,
                "RecordingViewportFrame",
                new Vector2(1050f, 650f),
                Vector2.zero
            );
        }
        frameRect.sizeDelta = new Vector2(1050f, 650f);
        RecordingViewportFrameGraphic frame =
            GetOrAdd<RecordingViewportFrameGraphic>(frameRect.gameObject);
        frame.raycastTarget = false;
        frame.Thickness = 8f;
        frameRect.SetAsLastSibling();

        TMP_Text countdown =
            root.Find("CountdownText")?.GetComponent<TMP_Text>();
        if (countdown != null)
        {
            countdown.fontSize = 112f;
        }

        TMP_Text resetLabel = teacherUI.transform.Find(
            "ResetProgressRoot/ResetProgressLabel"
        )?.GetComponent<TMP_Text>();

        teacherUI.ConfigureEnhancements(
            font,
            guidance,
            resetLabel,
            frame,
            replay
        );

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(root.gameObject, overlayLayer);
        }
    }

    private static HandCaptureBoundaryMonitor SetupHandCaptureWarning(
        GameObject recordingRoot,
        RecordingCoordinator coordinator,
        Transform centerEye,
        Transform teacherUiRoot,
        OVRHand leftHand,
        OVRHand rightHand,
        TMP_FontAsset font)
    {
        RectTransform warningRoot =
            teacherUiRoot.Find("HandTrackingWarningRoot") as RectTransform;
        if (warningRoot == null)
        {
            warningRoot = CreateRect(
                teacherUiRoot,
                "HandTrackingWarningRoot",
                new Vector2(720f, 56f),
                new Vector2(0f, -235f)
            );
        }

        warningRoot.anchoredPosition = new Vector2(0f, -235f);
        warningRoot.sizeDelta = new Vector2(720f, 56f);
        Image warningBackground = GetOrAdd<Image>(warningRoot.gameObject);
        warningBackground.color = new Color(0.10f, 0.08f, 0.055f, 0.88f);
        warningBackground.raycastTarget = false;

        TMP_Text warningLabel = EnsureUiText(
            warningRoot,
            "WarningText",
            "双手追踪区域正常",
            24f,
            Color.white,
            font,
            TextAlignmentOptions.Center,
            Vector2.zero,
            new Vector2(680f, 48f)
        );
        warningLabel.fontStyle = FontStyles.Bold;
        warningLabel.raycastTarget = false;
        warningRoot.SetAsLastSibling();

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(warningRoot.gameObject, overlayLayer);
        }

        HandCaptureBoundaryMonitor monitor =
            GetOrAdd<HandCaptureBoundaryMonitor>(recordingRoot);
        monitor.Configure(
            coordinator,
            centerEye,
            leftHand,
            rightHand,
            warningRoot.gameObject,
            warningLabel
        );
        warningRoot.gameObject.SetActive(false);
        return monitor;
    }

    private static RecordingTutorialController SetupTutorial(
        GameObject recordingRoot,
        RecordingCoordinator coordinator,
        Transform centerEye,
        TMP_FontAsset font)
    {
        RecordingTutorialController controller =
            GetOrAdd<RecordingTutorialController>(recordingRoot);

        Transform tutorialRoot = centerEye.Find("VRTutorialPanel");
        if (tutorialRoot == null)
        {
            tutorialRoot = FindRootTransform("VRTutorialPanel");
        }
        if (tutorialRoot == null)
        {
            var rootObject = new GameObject("VRTutorialPanel");
            Undo.RegisterCreatedObjectUndo(rootObject, "Create VR tutorial");
            tutorialRoot = rootObject.transform;
        }
        Undo.SetTransformParent(tutorialRoot, centerEye, "Attach tutorial to HMD");
        tutorialRoot.localPosition = new Vector3(0f, 0f, 0.96f);
        tutorialRoot.localRotation = Quaternion.identity;
        tutorialRoot.localScale = Vector3.one;

        RectTransform canvasRect =
            tutorialRoot.Find("TutorialCanvas") as RectTransform;
        if (canvasRect == null)
        {
            GameObject canvasObject = new(
                "TutorialCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)
            );
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create tutorial canvas");
            canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.SetParent(tutorialRoot, false);
            canvasRect.sizeDelta = new Vector2(900f, 480f);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
        }
        canvasRect.localPosition = Vector3.zero;
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.001f;

        RectTransform panel =
            canvasRect.Find("Panel") as RectTransform ??
            CreateStretchRect(canvasRect, "Panel", Vector2.zero, Vector2.zero);
        Image panelImage = GetOrAdd<Image>(panel.gameObject);
        panelImage.color = new Color(Ink.r, Ink.g, Ink.b, 0.97f);
        panelImage.raycastTarget = false;

        TMP_Text title = EnsureUiText(
            panel,
            "Title",
            "新手教程",
            42f,
            Color.white,
            font,
            TextAlignmentOptions.Left,
            new Vector2(0f, 160f),
            new Vector2(760f, 70f)
        );
        title.fontStyle = FontStyles.Bold;

        TMP_Text body = EnsureUiText(
            panel,
            "Body",
            string.Empty,
            29f,
            new Color(0.9f, 0.92f, 0.92f, 1f),
            font,
            TextAlignmentOptions.TopLeft,
            new Vector2(0f, 5f),
            new Vector2(760f, 220f)
        );
        body.textWrappingMode = TextWrappingModes.Normal;

        TMP_Text step = EnsureUiText(
            panel,
            "Step",
            "1 / 5",
            24f,
            new Color(0.65f, 0.78f, 0.84f, 1f),
            font,
            TextAlignmentOptions.Center,
            new Vector2(0f, -155f),
            new Vector2(240f, 48f)
        );

        controller.Configure(
            coordinator,
            tutorialRoot.gameObject,
            title,
            body,
            step
        );

        Transform buttonRoot = tutorialRoot.Find("TutorialButtons");
        if (buttonRoot != null)
        {
            buttonRoot.gameObject.SetActive(false);
        }

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(tutorialRoot.gameObject, overlayLayer);
        }

        tutorialRoot.gameObject.SetActive(false);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void SetupTouchscreenControls(
        GameObject recordingRoot,
        RecordingCoordinator coordinator,
        RecordingReplayController replay,
        RecordingTutorialController tutorial,
        TMP_FontAsset font)
    {
        Transform screenArea = FindTransformByPath(
            "Environment/TouchScreenDevice_03/ScreenArea"
        );
        Transform screenUi = screenArea.Find("ScreenUI");
        RectTransform replayReference =
            screenUi.Find("replay") as RectTransform;
        if (replayReference == null)
        {
            throw new InvalidOperationException(
                "TouchScreenDevice_03/ScreenUI/replay was not found."
            );
        }

        Vector3 replayPosition = replayReference.position;
        Quaternion screenRotation = screenUi.rotation;
        Vector3 screenFrontOffset = -screenUi.forward * 0.018f;
        Vector3 tutorialPosition =
            replayPosition - screenUi.up * 0.145f;

        replayReference.gameObject.SetActive(false);

        Transform controlsRoot = EnsureTransform(screenArea, "SignVRControls");
        controlsRoot.localPosition = Vector3.zero;
        controlsRoot.localRotation = Quaternion.identity;
        controlsRoot.localScale = Vector3.one;

        GameObject replayButton = EnsurePokeButton(
            controlsRoot,
            "ReplayToggle",
            "重播动作",
            Vector3.zero,
            RecordingPokeAction.ActionType.ToggleReplay,
            replay,
            null,
            tutorial,
            font
        );
        replayButton.transform.SetPositionAndRotation(
            replayPosition + screenFrontOffset,
            screenRotation
        );
        replayButton.transform.localScale =
            new Vector3(0.032f, 0.024f, 0.032f);

        GameObject tutorialButton = EnsurePokeButton(
            controlsRoot,
            "TutorialToggle",
            "查看教程",
            Vector3.zero,
            RecordingPokeAction.ActionType.ToggleTutorial,
            replay,
            null,
            tutorial,
            font
        );
        tutorialButton.transform.SetPositionAndRotation(
            tutorialPosition + screenFrontOffset,
            screenRotation
        );
        tutorialButton.transform.localScale =
            new Vector3(0.032f, 0.024f, 0.032f);

        TMP_Text replayLabel =
            replayButton.GetComponentsInChildren<TMP_Text>(true).First();
        TMP_Text tutorialLabel =
            tutorialButton.GetComponentsInChildren<TMP_Text>(true).First();

        RecordingTouchscreenPresenter presenter =
            GetOrAdd<RecordingTouchscreenPresenter>(recordingRoot);
        presenter.Configure(
            coordinator,
            replay,
            tutorial,
            replayButton,
            replayLabel,
            tutorialButton,
            tutorialLabel
        );
        EditorUtility.SetDirty(presenter);

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(controlsRoot.gameObject, overlayLayer);
        }

        Transform legacyPanel = FindRootTransform("VRDeskToolPanel");
        if (legacyPanel != null)
        {
            legacyPanel.gameObject.SetActive(false);
        }
    }

    private static void SetupPreviewCamera(
        QuestPreviewStreamer previewStreamer,
        Transform mirroredCharacter)
    {
        Transform head = mirroredCharacter
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Head");
        Transform hips = mirroredCharacter
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Hips");

        if (head == null || hips == null)
        {
            throw new InvalidOperationException(
                "The mirrored character Head or Hips transform was not found."
            );
        }

        previewStreamer.ConfigureView(
            head,
            hips,
            new Vector3(0f, 1.35f, -0.65f),
            50f
        );
        EditorUtility.SetDirty(previewStreamer);
    }

    private static void SetupToolPanel(
        GameObject recordingRoot,
        RecordingCoordinator coordinator,
        RecordingReplayController replay,
        RecordingPromptBoard promptBoard,
        RecordingTutorialController tutorial,
        TMP_FontAsset font,
        Material consoleMaterial)
    {
        Transform panel = FindRootTransform("VRDeskToolPanel");
        if (panel == null)
        {
            var panelObject = new GameObject("VRDeskToolPanel");
            Undo.RegisterCreatedObjectUndo(panelObject, "Create VR desk tool panel");
            panel = panelObject.transform;
            panel.SetPositionAndRotation(
                new Vector3(0f, 1.02f, 0.72f),
                Quaternion.identity
            );
        }

        Transform console = panel.Find("ConsoleSurface");
        if (console == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(cube, "Create console surface");
            cube.name = "ConsoleSurface";
            cube.transform.SetParent(panel, false);
            cube.transform.localPosition = new Vector3(0f, 0f, 0.045f);
            cube.transform.localScale = new Vector3(0.78f, 0.22f, 0.025f);
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = consoleMaterial;
            console = cube.transform;
        }

        Transform defaultControls = EnsureTransform(panel, "DefaultControls");
        Transform replayControls = EnsureTransform(panel, "ReplayControls");

        GameObject replayButton = EnsurePokeButton(
            defaultControls,
            "ReplayLastTake",
            "VR 回看",
            new Vector3(-0.22f, 0f, 0f),
            RecordingPokeAction.ActionType.ReplayLastTake,
            replay,
            promptBoard,
            tutorial,
            font
        );
        EnsurePokeButton(
            defaultControls,
            "StartTutorial",
            "新手教程",
            Vector3.zero,
            RecordingPokeAction.ActionType.StartTutorial,
            replay,
            promptBoard,
            tutorial,
            font
        );
        EnsurePokeButton(
            defaultControls,
            "ResetPromptBoard",
            "提示板归位",
            new Vector3(0.22f, 0f, 0f),
            RecordingPokeAction.ActionType.ResetPromptBoard,
            replay,
            promptBoard,
            tutorial,
            font
        );

        GameObject pauseButton = EnsurePokeButton(
            replayControls,
            "PauseReplay",
            "暂停",
            new Vector3(-0.11f, 0f, 0f),
            RecordingPokeAction.ActionType.ToggleReplayPause,
            replay,
            promptBoard,
            tutorial,
            font
        );
        EnsurePokeButton(
            replayControls,
            "StopReplay",
            "退出回看",
            new Vector3(0.11f, 0f, 0f),
            RecordingPokeAction.ActionType.StopReplay,
            replay,
            promptBoard,
            tutorial,
            font
        );

        TMP_Text pauseLabel =
            pauseButton.GetComponentsInChildren<TMP_Text>(true).First();
        RecordingToolPanelPresenter presenter =
            GetOrAdd<RecordingToolPanelPresenter>(panel.gameObject);
        presenter.Configure(
            coordinator,
            replay,
            defaultControls.gameObject,
            replayControls.gameObject,
            replayButton,
            pauseLabel
        );
        EditorUtility.SetDirty(presenter);
    }

    private static GameObject EnsurePokeButton(
        Transform parent,
        string name,
        string label,
        Vector3 localPosition,
        RecordingPokeAction.ActionType actionType,
        RecordingReplayController replay,
        RecordingPromptBoard promptBoard,
        RecordingTutorialController tutorial,
        TMP_FontAsset font)
    {
        Transform existing = parent.Find(name);
        GameObject button;
        if (existing == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PokeButtonPrefabPath
            );
            button = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            button.name = name;
        }
        else
        {
            button = existing.gameObject;
        }

        button.transform.localPosition = localPosition;
        button.transform.localRotation = Quaternion.identity;

        TMP_Text[] labels = button.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in labels)
        {
            text.text = label;
            text.font = font;
            text.color = Color.white;
        }

        PointableUnityEventWrapper wrapper =
            button.GetComponentInChildren<PointableUnityEventWrapper>(true);
        RecordingPokeAction action = GetOrAdd<RecordingPokeAction>(button);
        action.Configure(
            wrapper,
            actionType,
            replay,
            promptBoard,
            tutorial
        );
        EditorUtility.SetDirty(action);
        return button;
    }

    private static TMP_FontAsset EnsureChineseFont()
    {
        TMP_FontAsset existing =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null)
        {
            return existing;
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            throw new InvalidOperationException(
                $"Chinese source font is missing at {SourceFontPath}."
            );
        }

        FontEngine.InitializeFontEngine();
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            2048,
            2048,
            AtlasPopulationMode.Dynamic,
            true
        );
        fontAsset.name = "SignVRChinese SDF";
        fontAsset.atlasTextures[0].name = "SignVRChinese Atlas 0";
        fontAsset.material.name = "SignVRChinese Atlas Material";
        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

        const string commonText =
            "准备就绪录制中正在保存已完成等待主机连接身体追踪错误" +
            "踩一下外接空格键开始结束请看向镜像角色保持动作稍候" +
            "候选回看暂停继续退出重播新手教程提示板归位按住拖动" +
            "长按重录圆环填满松开取消旧可追溯当前句动作文件" +
            "无论状态持续直到视野默认倒计时秒拇指食指舒适位置" +
            "正式工具按钮自动收起避免手语误触下一步关闭百分比" +
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ·：，。“”%";
        fontAsset.TryAddCharacters(commonText, out string missingCharacters, true);
        if (!string.IsNullOrEmpty(missingCharacters))
        {
            Debug.LogWarning(
                "[SignVRRecordingUxSetup] Missing font characters: " +
                missingCharacters
            );
        }

        for (int i = 1; i < fontAsset.atlasTextures.Length; i++)
        {
            Texture2D texture = fontAsset.atlasTextures[i];
            texture.name = $"SignVRChinese Atlas {i}";
            if (!AssetDatabase.Contains(texture))
            {
                AssetDatabase.AddObjectToAsset(texture, fontAsset);
            }
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    private static Material EnsureConsoleMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(
            ConsoleMaterialPath
        );
        if (existing != null)
        {
            return existing;
        }

        string directory = System.IO.Path.GetDirectoryName(ConsoleMaterialPath);
        if (!AssetDatabase.IsValidFolder(directory))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader)
        {
            name = "RecordingDeskConsole",
            color = Slate
        };
        material.SetFloat("_Smoothness", 0.18f);
        material.SetColor("_BaseColor", Slate);
        AssetDatabase.CreateAsset(material, ConsoleMaterialPath);
        return material;
    }

    private static void ApplyFontToScene(TMP_FontAsset font)
    {
        foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (
                EditorUtility.IsPersistent(text) ||
                text.gameObject.scene != EditorSceneManager.GetActiveScene()
            )
            {
                continue;
            }

            text.font = font;
            EditorUtility.SetDirty(text);
        }
    }

    private static void SetInitialChinesePrompt(RecordingCoordinator coordinator)
    {
        var serialized = new SerializedObject(coordinator);
        serialized.FindProperty("initialPromptText").stringValue =
            "请准备录制当前句子的手语动作";
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TMP_Text EnsureUiText(
        Transform parent,
        string name,
        string value,
        float fontSize,
        Color color,
        TMP_FontAsset font,
        TextAlignmentOptions alignment,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        RectTransform rect = parent.Find(name) as RectTransform;
        if (rect == null)
        {
            rect = CreateRect(parent, name, size, anchoredPosition);
        }
        else
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 anchoredPosition)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
        var rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private static RectTransform CreateStretchRect(
        RectTransform parent,
        string name,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    private static Transform EnsureTransform(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            return existing;
        }

        var gameObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
        gameObject.transform.SetParent(parent, false);
        return gameObject.transform;
    }

    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null
            ? component
            : Undo.AddComponent<T>(gameObject);
    }

    private static void RemoveComponent<T>(GameObject gameObject)
        where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component != null)
        {
            Undo.DestroyObjectImmediate(component);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            transform.gameObject.layer = layer;
        }
    }

    private static T FindSceneComponent<T>(string path) where T : Component
    {
        Transform transform = FindTransformByPath(path);
        T component = transform.GetComponent<T>();
        if (component == null)
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} was not found on {path}."
            );
        }
        return component;
    }

    private static T FindSceneComponentAnywhere<T>() where T : Component
    {
        T component = Resources.FindObjectsOfTypeAll<T>()
            .FirstOrDefault(candidate =>
                !EditorUtility.IsPersistent(candidate) &&
                candidate.gameObject.scene == EditorSceneManager.GetActiveScene()
            );
        if (component == null)
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} was not found in the active scene."
            );
        }
        return component;
    }

    private static Transform FindTransformByPath(string path)
    {
        string[] segments = path.Split('/');
        Transform current = FindRootTransform(segments[0]);
        if (current == null)
        {
            throw new InvalidOperationException($"Scene object {path} was not found.");
        }

        for (int i = 1; i < segments.Length; i++)
        {
            current = current.Find(segments[i]);
            if (current == null)
            {
                throw new InvalidOperationException(
                    $"Scene object {path} was not found."
                );
            }
        }
        return current;
    }

    private static Transform FindRootTransform(string name)
    {
        return EditorSceneManager.GetActiveScene()
            .GetRootGameObjects()
            .Select(gameObject => gameObject.transform)
            .FirstOrDefault(transform => transform.name == name);
    }
}
