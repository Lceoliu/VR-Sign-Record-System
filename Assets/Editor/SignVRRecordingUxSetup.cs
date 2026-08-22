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
    private const string RedButtonModelPath =
        "Assets/ThirdParty/PolyPizza/BigRedButton/BigRedButton.obj";
    private const string RedButtonBaseMaterialPath =
        "Assets/Materials/RecordingRedButtonBase.mat";
    private const string RedButtonCapMaterialPath =
        "Assets/Materials/RecordingRedButtonCap.mat";
    private const string ConsoleMaterialPath =
        "Assets/Materials/RecordingDeskConsole.mat";
    private const string TrackingGuideMaterialPath =
        "Assets/Materials/QuestHandTrackingGuide.mat";

    private const string BoundaryWallMaterialPath =
        "Assets/Materials/SignVRBoundaryWall.mat";

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
        var ovrManager = FindSceneComponent<OVRManager>(
            "[BuildingBlock] Camera Rig"
        );
        Transform mirroredCharacter = FindTransformByPath(
            "MirroredObjects/StylizedCharacterMirrored"
        );

        GameObject recordingRoot = coordinator.gameObject;
        ConfigureWideMotionTracking(ovrManager);
        RecordingReplayController replay =
            GetOrAdd<RecordingReplayController>(recordingRoot);
        replay.Configure(coordinator, retargeter, leftHand, rightHand);

        SetupTutorial(recordingRoot, coordinator, centerEye, font);
        // TouchScreenDevice_03 is a user-authored scene area. Do not create,
        // hide, move, relabel, or re-parent anything under it from setup code.
        Material trackingGuideMaterial = EnsureTrackingGuideMaterial();
        SetupTeacherUi(teacherUI, replay, coordinator, centerEye, font);
        HandCaptureBoundaryMonitor handBoundaryMonitor =
            SetupHandCaptureWarning(
                recordingRoot,
                coordinator,
                centerEye,
                teacherUI.transform,
                leftHand,
                rightHand,
                trackingGuideMaterial,
                font
            );
        SetupPreviewCamera(previewStreamer, retargeter.transform);

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
            "[SignVRRecordingUxSetup] Head-locked status HUD, desk hologram, " +
            "measured hand-capture boundary, and fixed upper-body " +
            "preview are configured."
        );
    }

    [MenuItem("SignVR/Upgrade Recording Prompt Navigation")]
    public static void UpgradeRecordingPromptNavigation()
    {
        if (EditorSceneManager.GetActiveScene().path != RecordingScenePath)
        {
            throw new InvalidOperationException(
                $"Open {RecordingScenePath} before running this upgrade."
            );
        }

        TMP_FontAsset font = EnsureChineseFont();
        var coordinator = FindSceneComponent<RecordingCoordinator>("_Recording");
        var gateway = FindSceneComponent<QuestDeviceGateway>("_Recording");
        Transform centerEye = FindTransformByPath(
            "[BuildingBlock] Camera Rig/TrackingSpace/CenterEyeAnchor"
        );
        Transform environment = FindTransformByPath("Environment");
        RectTransform deskPrompt = environment.Find("DeskPromptCanvas") as RectTransform;
        if (deskPrompt == null)
        {
            throw new InvalidOperationException("Environment/DeskPromptCanvas was not found.");
        }

        FreezeDeskPrompt(deskPrompt);
        SetupSidePrompt(environment, deskPrompt, centerEye, coordinator, font);
        SetupDeskNavigation(environment, centerEye, gateway, font);
        ApplyFontToScene(font);

        EditorUtility.SetDirty(coordinator);
        EditorUtility.SetDirty(gateway);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log(
            "[SignVRRecordingUxSetup] Fixed desk prompt, left lyric prompt, and " +
            "two independent desk navigation buttons are configured. " +
            "TouchScreenDevice_03 was not modified."
        );
    }

    private static void FreezeDeskPrompt(RectTransform deskPrompt)
    {
        Transform drag = deskPrompt.Find("HologramPanel/PromptHeightDrag");
        if (drag != null)
        {
            Undo.DestroyObjectImmediate(drag.gameObject);
        }
        RemoveComponent<RecordingPromptBoard>(deskPrompt.gameObject);
    }

    private static void SetupSidePrompt(
        Transform environment,
        RectTransform deskPrompt,
        Transform centerEye,
        RecordingCoordinator coordinator,
        TMP_FontAsset font)
    {
        RectTransform canvasRect = environment.Find("SidePromptCanvas") as RectTransform;
        bool created = canvasRect == null;
        if (created)
        {
            var canvasObject = new GameObject(
                "SidePromptCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler)
            );
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create side prompt canvas");
            canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.SetParent(environment, false);

            Vector3 horizontalRight = centerEye.right;
            horizontalRight.y = 0f;
            horizontalRight.Normalize();
            Vector3 worldPosition = deskPrompt.position - horizontalRight * 0.58f + Vector3.up * 0.28f;
            Vector3 facing = worldPosition - centerEye.position;
            facing.y = 0f;
            canvasRect.SetPositionAndRotation(
                worldPosition,
                Quaternion.LookRotation(facing.normalized, Vector3.up)
            );
        }

        canvasRect.sizeDelta = new Vector2(680f, 430f);
        canvasRect.localScale = Vector3.one * 0.001f;
        Canvas canvas = GetOrAdd<Canvas>(canvasRect.gameObject);
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = GetOrAdd<CanvasScaler>(canvasRect.gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 2f;

        RectTransform panel = canvasRect.Find("LyricPanel") as RectTransform ??
                              CreateStretchRect(canvasRect, "LyricPanel", Vector2.zero, Vector2.zero);
        Image panelImage = GetOrAdd<Image>(panel.gameObject);
        panelImage.color = new Color(0.025f, 0.075f, 0.085f, 0.88f);
        panelImage.raycastTarget = false;
        Outline outline = GetOrAdd<Outline>(panel.gameObject);
        outline.effectColor = new Color(0.25f, 0.72f, 0.72f, 0.75f);
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_Text progress = EnsureUiText(
            panel, "ProgressText", "等待主机同步句子", 23f,
            new Color(0.70f, 0.90f, 0.89f, 1f), font,
            TextAlignmentOptions.Left, new Vector2(-115f, 184f), new Vector2(390f, 42f)
        );
        progress.fontStyle = FontStyles.Bold;
        TMP_Text mode = EnsureUiText(
            panel, "ModeText", string.Empty, 28f,
            new Color(1f, 0.78f, 0.30f, 1f), font,
            TextAlignmentOptions.Right, new Vector2(220f, 184f), new Vector2(180f, 42f)
        );
        mode.fontStyle = FontStyles.Bold;
        TMP_Text previous = EnsureUiText(
            panel, "PreviousText", "—", 25f,
            new Color(0.58f, 0.70f, 0.70f, 1f), font,
            TextAlignmentOptions.Center, new Vector2(0f, 103f), new Vector2(610f, 68f)
        );
        TMP_Text current = EnsureUiText(
            panel, "CurrentText", "请准备录制当前句子的手语动作", 42f,
            new Color(0.94f, 1f, 0.98f, 1f), font,
            TextAlignmentOptions.Center, new Vector2(0f, 4f), new Vector2(620f, 118f)
        );
        current.enableAutoSizing = true;
        current.fontSizeMin = 30f;
        current.fontSizeMax = 42f;
        current.fontStyle = FontStyles.Bold;
        TMP_Text next = EnsureUiText(
            panel, "NextText", "—", 25f,
            new Color(0.58f, 0.70f, 0.70f, 1f), font,
            TextAlignmentOptions.Center, new Vector2(0f, -111f), new Vector2(610f, 68f)
        );
        ConfigureHologramLine(panel, "CurrentTopLine", new Vector2(0f, 61f), 610f);
        ConfigureHologramLine(panel, "CurrentBottomLine", new Vector2(0f, -62f), 610f);

        RectTransform noticeRoot = panel.Find("ModeSwitchNotice") as RectTransform ??
                                   CreateRect(panel, "ModeSwitchNotice", new Vector2(630f, 185f), Vector2.zero);
        Image noticeBackground = GetOrAdd<Image>(noticeRoot.gameObject);
        noticeBackground.color = new Color(0.45f, 0.19f, 0.025f, 0.97f);
        noticeBackground.raycastTarget = false;
        TMP_Text noticeText = EnsureUiText(
            noticeRoot, "NoticeText", string.Empty, 42f, Color.white, font,
            TextAlignmentOptions.Center, Vector2.zero, new Vector2(580f, 155f)
        );
        noticeText.fontStyle = FontStyles.Bold;
        noticeRoot.gameObject.SetActive(false);

        RecordingSidePromptPresenter presenter =
            GetOrAdd<RecordingSidePromptPresenter>(coordinator.gameObject);
        presenter.Configure(
            coordinator,
            previous,
            current,
            next,
            progress,
            mode,
            noticeRoot.gameObject,
            noticeText
        );
        EditorUtility.SetDirty(presenter);

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(canvasRect.gameObject, overlayLayer);
        }
        canvasRect.gameObject.SetActive(true);
    }

    private static void SetupDeskNavigation(
        Transform environment,
        Transform centerEye,
        QuestDeviceGateway gateway,
        TMP_FontAsset font)
    {
        Transform table = FindTransformByPath("Environment/Table_01A");
        Renderer[] renderers = table.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException("Environment/Table_01A has no renderer bounds.");
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        Transform root = EnsureTransform(environment, "DeskNavigationButtons");
        Vector3 towardTeacher = centerEye.position - bounds.center;
        towardTeacher.y = 0f;
        towardTeacher.Normalize();
        Vector3 right = centerEye.right;
        right.y = 0f;
        right.Normalize();
        Vector3 basePosition = new(
            bounds.center.x,
            bounds.max.y + 0.003f,
            bounds.center.z
        );
        basePosition += towardTeacher * Mathf.Min(0.22f, bounds.extents.z * 0.45f);
        Quaternion rotation = Quaternion.LookRotation(Vector3.up, towardTeacher);

        SetupNavigationButton(
            root, "PreviousSentence", "← 上一句", basePosition - right * 0.15f,
            rotation, towardTeacher, centerEye, false, gateway, font
        );
        SetupNavigationButton(
            root, "NextSentence", "下一句 →", basePosition + right * 0.15f,
            rotation, towardTeacher, centerEye, true, gateway, font
        );

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(root.gameObject, overlayLayer);
        }
    }

    private static void SetupNavigationButton(
        Transform parent,
        string name,
        string label,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 towardTeacher,
        Transform centerEye,
        bool next,
        QuestDeviceGateway gateway,
        TMP_FontAsset font)
    {
        Bounds visualBounds = SetupDownloadedButtonModel(
            parent,
            name + "Model",
            worldPosition,
            towardTeacher
        );

        Transform existing = parent.Find(name);
        GameObject button;
        if (existing == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PokeButtonPrefabPath);
            button = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            button.name = name;
        }
        else
        {
            button = existing.gameObject;
        }

        Vector3 interactionPosition = new(
            worldPosition.x,
            visualBounds.max.y - 0.004f,
            worldPosition.z
        );
        button.transform.SetPositionAndRotation(interactionPosition, worldRotation);
        button.transform.localScale = new Vector3(0.095f, 0.05f, 0.095f);

        foreach (Renderer renderer in button.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponent<TMP_Text>() == null)
            {
                renderer.enabled = false;
            }
        }

        foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
        {
            text.text = label;
            text.font = font;
            text.color = Color.white;
        }

        PointableUnityEventWrapper wrapper =
            button.GetComponentInChildren<PointableUnityEventWrapper>(true);
        RecordingPokeAction action = GetOrAdd<RecordingPokeAction>(button);
        action.ConfigureNavigation(wrapper, gateway, next);
        EditorUtility.SetDirty(action);

        SetupNavigationButtonLabel(
            parent,
            name + "Label",
            label,
            worldPosition + towardTeacher * 0.115f + Vector3.up * 0.055f,
            centerEye,
            font
        );
    }

    private static Bounds SetupDownloadedButtonModel(
        Transform parent,
        string name,
        Vector3 baseWorldPosition,
        Vector3 towardTeacher)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            RedButtonModelPath
        );
        if (modelAsset == null)
        {
            throw new InvalidOperationException(
                $"Downloaded button model was not imported: {RedButtonModelPath}"
            );
        }

        Transform existing = parent.Find(name);
        GameObject model;
        if (existing == null)
        {
            model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, parent);
            model.name = name;
            Undo.RegisterCreatedObjectUndo(model, "Create downloaded red button model");
        }
        else
        {
            model = existing.gameObject;
        }

        model.transform.SetPositionAndRotation(
            baseWorldPosition,
            Quaternion.LookRotation(towardTeacher, Vector3.up)
        );
        model.transform.localScale = Vector3.one;

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException("Downloaded red button has no renderers.");
        }

        Material baseMaterial = EnsureRedButtonMaterial(false);
        Material capMaterial = EnsureRedButtonMaterial(true);
        foreach (Renderer renderer in renderers)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            renderer.sharedMaterials = meshFilter != null &&
                meshFilter.sharedMesh != null &&
                meshFilter.sharedMesh.subMeshCount == 2
                    ? new[] { capMaterial, baseMaterial }
                    : new[] { baseMaterial };
            renderer.enabled = true;
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        float scale = 0.18f / footprint;
        model.transform.localScale = Vector3.one * scale;

        bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        model.transform.position += Vector3.up * (baseWorldPosition.y - bounds.min.y);

        bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }

    private static void SetupNavigationButtonLabel(
        Transform parent,
        string name,
        string value,
        Vector3 worldPosition,
        Transform centerEye,
        TMP_FontAsset font)
    {
        RectTransform canvasRect = parent.Find(name) as RectTransform;
        if (canvasRect == null)
        {
            var canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler)
            );
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create desk button label");
            canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.SetParent(parent, false);
        }

        canvasRect.sizeDelta = new Vector2(360f, 80f);
        canvasRect.localScale = Vector3.one * 0.00055f;
        Vector3 facing = worldPosition - centerEye.position;
        canvasRect.SetPositionAndRotation(
            worldPosition,
            Quaternion.LookRotation(facing.normalized, Vector3.up)
        );

        Canvas canvas = GetOrAdd<Canvas>(canvasRect.gameObject);
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = GetOrAdd<CanvasScaler>(canvasRect.gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 2f;

        RectTransform background = canvasRect.Find("Background") as RectTransform ??
                                   CreateStretchRect(canvasRect, "Background", Vector2.zero, Vector2.zero);
        Image image = GetOrAdd<Image>(background.gameObject);
        image.color = new Color(0.025f, 0.028f, 0.032f, 0.94f);
        image.raycastTarget = false;

        TMP_Text text = EnsureUiText(
            background,
            "Text",
            value,
            36f,
            Color.white,
            font,
            TextAlignmentOptions.Center,
            Vector2.zero,
            new Vector2(340f, 70f)
        );
        text.fontStyle = FontStyles.Bold;
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
        RecordingCoordinator coordinator,
        Transform centerEye,
        TMP_FontAsset font)
    {
        RectTransform root = (RectTransform)teacherUI.transform;
        Undo.SetTransformParent(root, centerEye, "Attach teacher HUD to HMD");
        root.localPosition = new Vector3(0f, 0f, 0.75f);
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * 0.001f;
        root.sizeDelta = new Vector2(1000f, 600f);

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
        boardRoot.gameObject.SetActive(false);

        TMP_Text deskPromptText = SetupDeskPrompt(coordinator, centerEye, font);

        RectTransform statusPanel = root.Find("StatusPanel") as RectTransform;
        statusPanel.anchoredPosition = new Vector2(0f, 252f);
        statusPanel.sizeDelta = new Vector2(560f, 76f);
        Image statusBackground = statusPanel.GetComponent<Image>();
        statusBackground.color = new Color(Ink.r, Ink.g, Ink.b, 0.82f);

        RectTransform statusIndicator =
            statusPanel.Find("StatusIndicator") as RectTransform;
        statusIndicator.anchoredPosition = new Vector2(-244f, 0f);
        statusIndicator.sizeDelta = new Vector2(30f, 30f);

        TMP_Text statusText =
            statusPanel.Find("StatusText").GetComponent<TMP_Text>();
        RectTransform statusTextRect = (RectTransform)statusText.transform;
        statusTextRect.anchoredPosition = new Vector2(18f, 0f);
        statusTextRect.sizeDelta = new Vector2(470f, 66f);
        statusText.fontSize = 34f;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = Color.white;

        TMP_Text takeText =
            statusPanel.Find("TakeText").GetComponent<TMP_Text>();
        takeText.text = string.Empty;
        takeText.gameObject.SetActive(false);

        Transform guidanceTransform = root.Find("GuidanceText");
        if (guidanceTransform != null)
        {
            Undo.DestroyObjectImmediate(guidanceTransform.gameObject);
        }

        RectTransform frameRect = root.Find("RecordingViewportFrame") as RectTransform;
        if (frameRect == null)
        {
            frameRect = CreateRect(
                root,
                "RecordingViewportFrame",
                new Vector2(970f, 570f),
                Vector2.zero
            );
        }
        frameRect.sizeDelta = new Vector2(970f, 570f);
        RecordingViewportFrameGraphic frame =
            GetOrAdd<RecordingViewportFrameGraphic>(frameRect.gameObject);
        frame.raycastTarget = false;
        frame.Thickness = 6f;
        frameRect.SetAsLastSibling();

        TMP_Text countdown =
            root.Find("CountdownText")?.GetComponent<TMP_Text>();
        if (countdown != null)
        {
            countdown.text = string.Empty;
            countdown.gameObject.SetActive(false);
        }

        RectTransform resetRoot = root.Find("ResetProgressRoot") as RectTransform;
        if (resetRoot != null)
        {
            resetRoot.anchoredPosition = Vector2.zero;
            resetRoot.sizeDelta = new Vector2(280f, 280f);
        }
        TMP_Text resetLabel = teacherUI.transform.Find(
            "ResetProgressRoot/ResetProgressLabel"
        )?.GetComponent<TMP_Text>();
        if (resetLabel != null)
        {
            resetLabel.fontSize = 28f;
        }

        teacherUI.ConfigureEnhancements(
            font,
            null,
            resetLabel,
            frame,
            replay
        );
        teacherUI.ConfigurePromptText(deskPromptText);

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(root.gameObject, overlayLayer);
        }
    }

    private static TMP_Text SetupDeskPrompt(
        RecordingCoordinator coordinator,
        Transform centerEye,
        TMP_FontAsset font)
    {
        Transform environment = FindTransformByPath("Environment");
        Transform table = FindTransformByPath("Environment/Table_01A");
        Renderer[] tableRenderers =
            table.GetComponentsInChildren<Renderer>(true);
        if (tableRenderers.Length == 0)
        {
            throw new InvalidOperationException(
                "Environment/Table_01A has no renderer bounds."
            );
        }

        Bounds tableBounds = tableRenderers[0].bounds;
        for (int index = 1; index < tableRenderers.Length; index++)
        {
            tableBounds.Encapsulate(tableRenderers[index].bounds);
        }

        RectTransform canvasRect =
            environment.Find("DeskPromptCanvas") as RectTransform;
        bool createdCanvas = canvasRect == null;
        if (canvasRect == null)
        {
            var canvasObject = new GameObject(
                "DeskPromptCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler)
            );
            Undo.RegisterCreatedObjectUndo(
                canvasObject,
                "Create desk prompt hologram"
            );
            canvasRect = (RectTransform)canvasObject.transform;
        }

        Undo.SetTransformParent(
            canvasRect,
            environment,
            "Attach desk prompt hologram"
        );
        canvasRect.sizeDelta = new Vector2(860f, 210f);
        canvasRect.localScale = Vector3.one * 0.001f;

        if (createdCanvas)
        {
            Vector3 worldPosition = new(
                tableBounds.center.x,
                tableBounds.max.y + 0.19f,
                tableBounds.center.z
            );
            Vector3 facing = worldPosition - centerEye.position;
            facing.y = 0f;
            canvasRect.SetPositionAndRotation(
                worldPosition,
                Quaternion.LookRotation(facing.normalized, Vector3.up)
            );
        }

        Canvas canvas = GetOrAdd<Canvas>(canvasRect.gameObject);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = false;
        CanvasScaler scaler = GetOrAdd<CanvasScaler>(canvasRect.gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 2f;

        RectTransform panel =
            canvasRect.Find("HologramPanel") as RectTransform ??
            CreateStretchRect(canvasRect, "HologramPanel", Vector2.zero, Vector2.zero);
        Image panelImage = GetOrAdd<Image>(panel.gameObject);
        panelImage.color = new Color(0.025f, 0.085f, 0.095f, 0.78f);
        panelImage.raycastTarget = false;
        Outline outline = GetOrAdd<Outline>(panel.gameObject);
        outline.effectColor = new Color(0.30f, 0.68f, 0.70f, 0.72f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        TMP_Text header = EnsureUiText(
            panel,
            "Header",
            "本句提示",
            20f,
            new Color(0.53f, 0.82f, 0.82f, 1f),
            font,
            TextAlignmentOptions.Center,
            new Vector2(0f, 78f),
            new Vector2(130f, 30f)
        );
        header.fontStyle = FontStyles.Bold;
        header.fontSharedMaterial = font.material;

        TMP_Text prompt = EnsureUiText(
            panel,
            "PromptText",
            "请准备录制当前句子的手语动作",
            44f,
            new Color(0.90f, 0.98f, 0.97f, 1f),
            font,
            TextAlignmentOptions.Center,
            new Vector2(0f, -8f),
            new Vector2(790f, 132f)
        );
        prompt.enableAutoSizing = true;
        prompt.fontSizeMin = 28f;
        prompt.fontSizeMax = 44f;
        prompt.fontStyle = FontStyles.Bold;
        prompt.fontSharedMaterial = font.material;

        ConfigureHologramLine(panel, "TopLine", new Vector2(0f, 96f));
        ConfigureHologramLine(panel, "BottomLine", new Vector2(0f, -96f));

        Transform dragTarget = panel.Find("PromptHeightDrag");
        if (dragTarget != null)
        {
            Undo.DestroyObjectImmediate(dragTarget.gameObject);
        }
        RemoveComponent<RecordingPromptBoard>(canvasRect.gameObject);
        panel.SetAsFirstSibling();

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(canvasRect.gameObject, overlayLayer);
        }
        canvasRect.gameObject.SetActive(true);
        return prompt;
    }

    private static void ConfigureHologramLine(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        float width = 790f)
    {
        RectTransform line = parent.Find(name) as RectTransform;
        if (line == null)
        {
            line = CreateRect(
                parent,
                name,
                new Vector2(width, 3f),
                anchoredPosition
            );
        }
        else
        {
            line.sizeDelta = new Vector2(width, 3f);
            line.anchoredPosition = anchoredPosition;
        }

        Image image = GetOrAdd<Image>(line.gameObject);
        image.color = new Color(0.30f, 0.68f, 0.70f, 0.72f);
        image.raycastTarget = false;
    }

    private static HandCaptureBoundaryMonitor SetupHandCaptureWarning(
        GameObject recordingRoot,
        RecordingCoordinator coordinator,
        Transform centerEye,
        Transform teacherUiRoot,
        OVRHand leftHand,
        OVRHand rightHand,
        Material trackingGuideMaterial,
        TMP_FontAsset font)
    {
        RectTransform warningRoot =
            teacherUiRoot.Find("HandTrackingWarningRoot") as RectTransform;
        if (warningRoot == null)
        {
            warningRoot = CreateRect(
                teacherUiRoot,
                "HandTrackingWarningRoot",
                new Vector2(800f, 66f),
                new Vector2(0f, -252f)
            );
        }

        warningRoot.anchoredPosition = new Vector2(0f, -252f);
        warningRoot.sizeDelta = new Vector2(800f, 66f);
        Image warningBackground = GetOrAdd<Image>(warningRoot.gameObject);
        warningBackground.color = new Color(0.10f, 0.08f, 0.055f, 0.78f);
        warningBackground.raycastTarget = false;

        TMP_Text warningLabel = EnsureUiText(
            warningRoot,
            "WarningText",
            "双手追踪区域正常",
            29f,
            Color.white,
            font,
            TextAlignmentOptions.Center,
            Vector2.zero,
            new Vector2(760f, 58f)
        );
        warningLabel.fontStyle = FontStyles.Bold;
        warningLabel.raycastTarget = false;
        warningRoot.SetAsLastSibling();

        int overlayLayer = LayerMask.NameToLayer("Overlay UI");
        if (overlayLayer >= 0)
        {
            SetLayerRecursively(warningRoot.gameObject, overlayLayer);
        }

        QuestHandTrackingBoundaryGuide trackingGuide =
            GetOrAdd<QuestHandTrackingBoundaryGuide>(recordingRoot);
        trackingGuide.Configure(
            coordinator,
            centerEye,
            leftHand,
            rightHand,
            trackingGuideMaterial,
            EnsureBoundaryWallMaterial()
        );

        HandCaptureBoundaryMonitor monitor =
            GetOrAdd<HandCaptureBoundaryMonitor>(recordingRoot);
        monitor.Configure(
            coordinator,
            centerEye,
            leftHand,
            rightHand,
            warningRoot.gameObject,
            warningLabel,
            trackingGuide
        );
        EditorUtility.SetDirty(trackingGuide);
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
        OVROverlayCanvas screenOverlay =
            screenUi.GetComponent<OVROverlayCanvas>();
        if (screenOverlay != null)
        {
            Undo.DestroyObjectImmediate(screenOverlay);
        }
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
        Transform character)
    {
        Transform head = character
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Head");
        Transform hips = character
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Hips");

        if (head == null || hips == null)
        {
            throw new InvalidOperationException(
                "The character Head or Hips transform was not found."
            );
        }

        Vector3 framingCenter = (head.position + hips.position) * 0.5f +
                                Vector3.up * 0.12f;
        Vector3 cameraPosition = new Vector3(-0.47f, 1.4f, -1.65f);

        previewStreamer.ConfigureView(
            head,
            hips,
            cameraPosition,
            60f,
            true
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

    private static Material EnsureRedButtonMaterial(bool cap)
    {
        string path = cap
            ? RedButtonCapMaterialPath
            : RedButtonBaseMaterialPath;
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = cap ? "RecordingRedButtonCap" : "RecordingRedButtonBase"
            };
            AssetDatabase.CreateAsset(material, path);
        }

        Color color = cap
            ? new Color(0.72f, 0.018f, 0.022f, 1f)
            : new Color(0.035f, 0.04f, 0.045f, 1f);
        material.color = color;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", cap ? 0.12f : 0.58f);
        material.SetFloat("_Smoothness", cap ? 0.62f : 0.38f);
        if (cap)
        {
            material.SetColor("_EmissionColor", color * 0.2f);
            material.EnableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material EnsureTrackingGuideMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(
            TrackingGuideMaterialPath
        );
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var material = new Material(shader)
        {
            name = "QuestHandTrackingGuide",
            color = Color.white,
            renderQueue = 3000
        };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        AssetDatabase.CreateAsset(material, TrackingGuideMaterialPath);
        return material;
    }

    /// <summary>
    /// Material for the translucent frustum sides. A wireframe reads as a
    /// diagram; these surfaces give the boundary a body the teacher can touch.
    /// </summary>
    private static Material EnsureBoundaryWallMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(
            BoundaryWallMaterialPath
        );
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("SignVR/Boundary Wall");
        if (shader == null)
        {
            Debug.LogError(
                "[SignVRRecordingUxSetup] Shader SignVR/Boundary Wall not found."
            );
            return null;
        }

        var material = new Material(shader) { name = "SignVRBoundaryWall" };
        AssetDatabase.CreateAsset(material, BoundaryWallMaterialPath);
        return material;
    }

    private static void ConfigureWideMotionTracking(OVRManager ovrManager)
    {
        // Keep native passthrough initialized for the lifetime of the app. The
        // in-world button only shows or hides its layer, avoiding compositor
        // reinitialization during immersive/passthrough transitions.
        ovrManager.isInsightPassthroughEnabled = true;
        ovrManager.wideMotionModeHandPosesEnabled = true;
        ovrManager.enableDynamicResolution = false;
        var managerSerialized = new SerializedObject(ovrManager);
        managerSerialized.FindProperty(
            "requestBodyTrackingPermissionOnStartup"
        ).boolValue = true;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ovrManager);

        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (projectConfig == null)
        {
            throw new InvalidOperationException(
                "OculusProjectConfig is unavailable; cannot enable Quest 3 WMM."
            );
        }

        projectConfig.bodyTrackingSupport =
            OVRProjectConfig.FeatureSupport.Required;
        OVRProjectConfig.CommitProjectConfig(projectConfig);
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
