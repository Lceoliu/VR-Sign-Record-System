using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Editor;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
using SignVR.SceneFlow;
using SignVR.SortingGame;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class CreateVRSortingGameScene
{
    internal const string ScenePath =
        "Assets/Scenes/VRSortingGame.unity";
    internal const string ResetUiScriptPath =
        "Assets/Scripts/SortingGame/SortingGameResetUI.cs";

    private const string AssetRoot = "Assets/VRSortingGame";
    private const string MaterialRoot = AssetRoot + "/Materials";
    private const string CameraRigGuid =
        "126d619cf4daa52469682f85c1378b4a";
    private const string InteractionRigGuid =
        "0a7d2469f24041c4284c66706f84c45e";

    private static readonly Color Coral =
        new Color(0.94f, 0.25f, 0.22f, 1f);
    private static readonly Color Teal =
        new Color(0.08f, 0.68f, 0.66f, 1f);
    private static readonly Color Yellow =
        new Color(0.98f, 0.72f, 0.12f, 1f);

    [MenuItem("Tools/SignVR/Create VR Sorting Game Scene")]
    private static void CreateFromMenu()
    {
        if (
            File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog(
                "Rebuild VR Sorting Game",
                "This replaces VRSortingGame.unity with the reproducible " +
                "generated version. Continue?",
                "Rebuild",
                "Cancel"
            )
        )
        {
            return;
        }

        CreateSceneForAutomation();
    }

    [MenuItem("Tools/SignVR/Validate VR Sorting Game Scene")]
    private static void ValidateFromMenu()
    {
        ValidateSceneForAutomation();
        Debug.Log(
            "[SignVR] VRSortingGame.unity passed scene and Build Settings validation."
        );
    }

    public static void CreateSceneForAutomation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before generating the VR sorting scene."
            );
        }

        Scene alreadyLoaded = SceneManager.GetSceneByPath(ScenePath);

        if (alreadyLoaded.IsValid() && alreadyLoaded.isLoaded)
        {
            throw new InvalidOperationException(
                "Close VRSortingGame.unity before rebuilding it."
            );
        }

        EnsureAssetFolders();
        Scene originalActiveScene = SceneManager.GetActiveScene();
        bool hasSavedActiveScene =
            originalActiveScene.IsValid() &&
            !string.IsNullOrEmpty(originalActiveScene.path);

        if (!hasSavedActiveScene && !Application.isBatchMode)
        {
            throw new InvalidOperationException(
                "Save the active scene before generating the VR sorting scene."
            );
        }

        Scene generatedScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            hasSavedActiveScene ? NewSceneMode.Additive : NewSceneMode.Single
        );

        try
        {
            SceneManager.SetActiveScene(generatedScene);
            BuildScene(generatedScene);
            ValidateScene(generatedScene);

            if (!EditorSceneManager.SaveScene(generatedScene, ScenePath))
            {
                throw new IOException(
                    $"Unity could not save {ScenePath}."
                );
            }

            AddSceneToBuildSettings();
            ValidateBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[SignVR] Created Assets/Scenes/VRSortingGame.unity with " +
                "three hand/controller-grabbable pieces and matching targets."
            );
        }
        finally
        {
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalActiveScene);
            }

            if (generatedScene.IsValid() && generatedScene.isLoaded)
            {
                EditorSceneManager.CloseScene(generatedScene, true);
            }
        }
    }

    public static void ValidateSceneForAutomation()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
        Scene originalActiveScene = SceneManager.GetActiveScene();

        if (openedForValidation)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new FileNotFoundException(
                    "Generate the VR sorting scene before validating it.",
                    ScenePath
                );
            }

            scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive
            );
        }

        try
        {
            ValidateScene(scene);
            ValidateBuildSettings();
        }
        finally
        {
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalActiveScene);
            }

            if (openedForValidation && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static void BuildScene(Scene scene)
    {
        ConfigureRenderSettings();
            CreateVrRig(scene);

        Transform environment = new GameObject("Environment").transform;
        Transform game = new GameObject("SortingGame").transform;
        Transform piecesRoot = CreateChild(game, "Pieces");
        Transform targetsRoot = CreateChild(game, "Targets");
        Transform lighting = new GameObject("Lighting").transform;
        Transform presentation = new GameObject("Presentation").transform;

        Material floorMaterial = CreateOrUpdateMaterial(
            "Floor",
            new Color(0.32f, 0.35f, 0.37f, 1f),
            0.05f,
            0.42f
        );
        Material wallMaterial = CreateOrUpdateMaterial(
            "Wall",
            new Color(0.73f, 0.76f, 0.77f, 1f),
            0f,
            0.25f
        );
        Material benchMaterial = CreateOrUpdateMaterial(
            "Workbench",
            new Color(0.14f, 0.16f, 0.17f, 1f),
            0.55f,
            0.5f
        );
        Material trimMaterial = CreateOrUpdateMaterial(
            "Trim",
            new Color(0.78f, 0.8f, 0.8f, 1f),
            0.8f,
            0.65f
        );
        Material targetMaterial = CreateOrUpdateMaterial(
            "Target",
            new Color(0.35f, 0.39f, 0.4f, 1f),
            0.25f,
            0.55f,
            emission: true
        );
        Material coralMaterial = CreateOrUpdateMaterial(
            "Piece_Coral",
            Coral,
            0.05f,
            0.58f
        );
        Material tealMaterial = CreateOrUpdateMaterial(
            "Piece_Teal",
            Teal,
            0.15f,
            0.62f
        );
        Material yellowMaterial = CreateOrUpdateMaterial(
            "Piece_Yellow",
            Yellow,
            0.05f,
            0.48f
        );

        CreateRoom(environment, floorMaterial, wallMaterial);
        CreateWorkbench(environment, benchMaterial, trimMaterial);
        CreateLighting(lighting);

        GameObject managerObject = new GameObject("GameManager");
        managerObject.transform.SetParent(game, false);
        SortingGameManager manager =
            managerObject.AddComponent<SortingGameManager>();

        TMP_Text statusLabel = CreateStatusDisplay(
            presentation,
            benchMaterial,
            trimMaterial
        );
        CreateResetControl(presentation, manager);
        CreateSceneSwitchControl(presentation);

        PlacementTarget[] targets =
        {
            CreateTarget(
                targetsRoot,
                manager,
                "coral",
                "Coral Cube Bay",
                new Vector3(-0.36f, 0.8f, 0.72f),
                0.138f,
                Coral,
                targetMaterial,
                trimMaterial
            ),
            CreateTarget(
                targetsRoot,
                manager,
                "teal",
                "Teal Sphere Bay",
                new Vector3(0f, 0.8f, 0.72f),
                0.138f,
                Teal,
                targetMaterial,
                trimMaterial
            ),
            CreateTarget(
                targetsRoot,
                manager,
                "yellow",
                "Yellow Capsule Bay",
                new Vector3(0.36f, 0.8f, 0.72f),
                0.24f,
                Yellow,
                targetMaterial,
                trimMaterial
            )
        };

        PlacementPiece[] pieces =
        {
            CreatePiece(
                piecesRoot,
                "teal",
                "Teal Sphere",
                PrimitiveType.Sphere,
                new Vector3(-0.36f, 0.932f, 0.37f),
                new Vector3(0.24f, 0.24f, 0.24f),
                tealMaterial
            ),
            CreatePiece(
                piecesRoot,
                "yellow",
                "Yellow Capsule",
                PrimitiveType.Capsule,
                new Vector3(0f, 1.032f, 0.37f),
                new Vector3(0.16f, 0.22f, 0.16f),
                yellowMaterial
            ),
            CreatePiece(
                piecesRoot,
                "coral",
                "Coral Cube",
                PrimitiveType.Cube,
                new Vector3(0.36f, 0.932f, 0.37f),
                new Vector3(0.24f, 0.24f, 0.24f),
                coralMaterial
            )
        };

        manager.Configure(pieces, targets, statusLabel);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static OVRCameraRig CreateVrRig(Scene scene)
    {
        GameObject cameraRigObject = InstantiatePrefabInScene(
            CameraRigGuid,
            "OVRCameraRig",
            scene
        );
        OVRCameraRig cameraRig =
            cameraRigObject.GetComponentInChildren<OVRCameraRig>(true);

        if (cameraRig == null)
        {
            throw new InvalidOperationException(
                "Meta's OVRCameraRig prefab has no OVRCameraRig component."
            );
        }

        GameObject interactionRigObject = InstantiatePrefabInScene(
            InteractionRigGuid,
            "OVRComprehensiveInteractionRig",
            scene
        );
        interactionRigObject.transform.SetParent(cameraRig.transform, false);

        // Match Meta's Quick Action setup after forcing both prefabs into the
        // generated scene. Its default rig lookup is global across loaded scenes.
        UnityObjectAddedBroadcaster.HandleObjectWasAdded(interactionRigObject);

        OVRCameraRigRef cameraRigRef = interactionRigObject
            .GetComponentInChildren<OVRCameraRigRef>(true);

        if (cameraRigRef == null)
        {
            throw new InvalidOperationException(
                "Meta's rig prefabs did not provide a camera and interaction rig."
            );
        }

        cameraRig.gameObject.name = "OVRCameraRig";
        cameraRigRef.gameObject.name = "OVRComprehensiveInteractionRig";
        cameraRigRef.InjectInteractionOVRCameraRig(cameraRig);
        DisableDuplicateHandVisuals(cameraRig);

        OVRManager ovrManager = cameraRig.GetComponent<OVRManager>();

        if (ovrManager != null)
        {
            SerializedObject managerData = new SerializedObject(ovrManager);
            SerializedProperty trackingOrigin =
                managerData.FindProperty("_trackingOriginType");

            if (trackingOrigin != null)
            {
                trackingOrigin.intValue =
                    (int)OVRManager.TrackingOrigin.FloorLevel;
            }

            managerData.ApplyModifiedPropertiesWithoutUndo();
            ovrManager.controllerDrivenHandPosesType =
                OVRManager.ControllerDrivenHandPosesType.ConformingToController;
            EditorUtility.SetDirty(ovrManager);
        }

        EditorUtility.SetDirty(cameraRigRef);
        return cameraRig;
    }

    private static GameObject InstantiatePrefabInScene(
        string guid,
        string displayName,
        Scene scene
    )
    {
        GameObject prefab = LoadPrefabByGuid(guid, displayName);
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene)
            as GameObject;

        if (instance == null)
        {
            throw new InvalidOperationException(
                $"Unity could not instantiate the {displayName} prefab."
            );
        }

        instance.name = displayName;
        return instance;
    }

    private static void DisableDuplicateHandVisuals(OVRCameraRig cameraRig)
    {
        foreach (
            OVRHand hand in cameraRig.trackingSpace
                .GetComponentsInChildren<OVRHand>(true)
        )
        {
            SetEnabled(hand.GetComponent<OVRSkeletonRenderer>(), false);
            SetEnabled(hand.GetComponent<OVRMesh>(), false);
            SetEnabled(hand.GetComponent<OVRMeshRenderer>(), false);
            SetEnabled(hand.GetComponent<SkinnedMeshRenderer>(), false);
        }
    }

    private static void SetEnabled(Behaviour behaviour, bool enabled)
    {
        if (behaviour != null)
        {
            behaviour.enabled = enabled;
            EditorUtility.SetDirty(behaviour);
        }
    }

    private static void SetEnabled(Renderer renderer, bool enabled)
    {
        if (renderer != null)
        {
            renderer.enabled = enabled;
            EditorUtility.SetDirty(renderer);
        }
    }

    private static PlacementPiece CreatePiece(
        Transform parent,
        string id,
        string displayName,
        PrimitiveType primitiveType,
        Vector3 position,
        Vector3 scale,
        Material material
    )
    {
        GameObject pieceObject = new GameObject(displayName);
        pieceObject.transform.SetParent(parent, false);
        pieceObject.transform.position = position;
        pieceObject.transform.localRotation = Quaternion.identity;
        pieceObject.transform.localScale = Vector3.one;

        GameObject visual = CreatePrimitive(
            "Visual",
            primitiveType,
            pieceObject.transform,
            Vector3.zero,
            scale,
            material,
            useLocalTransform: true
        );
        Collider visualCollider = visual.GetComponent<Collider>();

        Rigidbody pieceRigidbody = pieceObject.AddComponent<Rigidbody>();
        pieceRigidbody.mass = 0.3f;
        pieceRigidbody.useGravity = true;
        pieceRigidbody.isKinematic = false;
        pieceRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        pieceRigidbody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        pieceRigidbody.linearDamping = 0.05f;
        pieceRigidbody.angularDamping = 0.08f;

        QuickActionsAPI.AddGrabInteraction(pieceObject);

        Grabbable grabbable = pieceObject.GetComponent<Grabbable>();

        if (grabbable == null)
        {
            throw new InvalidOperationException(
                $"Grab setup failed for {displayName}."
            );
        }

        grabbable.MaxGrabPoints = 1;
        grabbable.InjectOptionalTargetTransform(pieceObject.transform);
        grabbable.InjectOptionalRigidbody(pieceRigidbody);
        grabbable.InjectOptionalKinematicWhileSelected(true);
        grabbable.InjectOptionalThrowWhenUnselected(true);
        grabbable.ForceKinematicDisabled = true;

        if (visualCollider == null)
        {
            throw new InvalidOperationException(
                $"The visual for {displayName} has no collider."
            );
        }

        Behaviour[] interactionBehaviours = pieceObject
            .GetComponentsInChildren<Behaviour>(true)
            .Where(component =>
                component is Grabbable ||
                component is HandGrabInteractable ||
                component is GrabInteractable
            )
            .ToArray();

        PlacementPiece piece = pieceObject.AddComponent<PlacementPiece>();
        piece.Configure(
            id,
            pieceRigidbody,
            grabbable,
            interactionBehaviours
        );

        EditorUtility.SetDirty(grabbable);
        EditorUtility.SetDirty(pieceRigidbody);
        EditorUtility.SetDirty(piece);
        return piece;
    }

    private static PlacementTarget CreateTarget(
        Transform parent,
        SortingGameManager manager,
        string id,
        string displayName,
        Vector3 position,
        float snapHeight,
        Color targetColor,
        Material targetMaterial,
        Material trimMaterial
    )
    {
        GameObject targetObject = new GameObject(displayName);
        targetObject.transform.SetParent(parent, false);
        targetObject.transform.position = position;

        BoxCollider trigger = targetObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 0.17f, 0f);
        trigger.size = new Vector3(0.34f, 0.34f, 0.32f);

        GameObject pad = CreatePrimitive(
            "Indicator",
            PrimitiveType.Cylinder,
            targetObject.transform,
            Vector3.zero,
            new Vector3(0.2f, 0.018f, 0.2f),
            targetMaterial,
            useLocalTransform: true
        );
        UnityEngine.Object.DestroyImmediate(pad.GetComponent<Collider>());

        GameObject marker = CreatePrimitive(
            "Marker",
            PrimitiveType.Cylinder,
            targetObject.transform,
            new Vector3(0f, 0.024f, 0f),
            new Vector3(0.145f, 0.008f, 0.145f),
            trimMaterial,
            useLocalTransform: true
        );
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());

        Transform snapPoint = CreateChild(targetObject.transform, "SnapPoint");
        snapPoint.localPosition = new Vector3(0f, snapHeight, 0f);

        PlacementTarget target = targetObject.AddComponent<PlacementTarget>();
        Color idleColor = Color.Lerp(
            targetColor,
            new Color(0.16f, 0.18f, 0.19f, 1f),
            0.48f
        );
        target.Configure(
            id,
            snapPoint,
            pad.GetComponent<Renderer>(),
            idleColor,
            targetColor,
            manager
        );
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void CreateRoom(
        Transform parent,
        Material floorMaterial,
        Material wallMaterial
    )
    {
        CreatePrimitive(
            "Floor",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, -0.06f, 0.55f),
            new Vector3(2.7f, 0.12f, 1.9f),
            floorMaterial
        );
        CreatePrimitive(
            "Back Wall",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 1.2f, 1.48f),
            new Vector3(2.7f, 2.4f, 0.1f),
            wallMaterial
        );
        CreatePrimitive(
            "Left Wall",
            PrimitiveType.Cube,
            parent,
            new Vector3(-1.35f, 1.2f, 0.55f),
            new Vector3(0.1f, 2.4f, 1.8f),
            wallMaterial
        );
        CreatePrimitive(
            "Right Wall",
            PrimitiveType.Cube,
            parent,
            new Vector3(1.35f, 1.2f, 0.55f),
            new Vector3(0.1f, 2.4f, 1.8f),
            wallMaterial
        );
    }

    private static void CreateWorkbench(
        Transform parent,
        Material benchMaterial,
        Material trimMaterial
    )
    {
        CreatePrimitive(
            "Workbench Top",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 0.74f, 0.55f),
            new Vector3(1.2f, 0.1f, 0.8f),
            benchMaterial
        );

        foreach (float x in new[] { -0.5f, 0.5f })
        {
            foreach (float z in new[] { 0.25f, 0.85f })
            {
                CreatePrimitive(
                    $"Leg {x:0.00} {z:0.00}",
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(x, 0.38f, z),
                    new Vector3(0.1f, 0.76f, 0.1f),
                    trimMaterial
                );
            }
        }

        for (int index = 0; index < 3; index++)
        {
            CreatePrimitive(
                $"Source Pad {index + 1}",
                PrimitiveType.Cylinder,
                parent,
                new Vector3(-0.36f + index * 0.36f, 0.8f, 0.37f),
                new Vector3(0.17f, 0.012f, 0.17f),
                trimMaterial
            );
        }
    }

    private static TMP_Text CreateStatusDisplay(
        Transform parent,
        Material boardMaterial,
        Material trimMaterial
    )
    {
        CreatePrimitive(
            "Status Board",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 1.5f, 1.35f),
            new Vector3(1.15f, 0.32f, 0.045f),
            boardMaterial
        );
        CreatePrimitive(
            "Status Accent",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 1.31f, 1.32f),
            new Vector3(1.15f, 0.025f, 0.055f),
            trimMaterial
        );

        GameObject labelObject = new GameObject("Status");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.position = new Vector3(0f, 1.5f, 1.29f);
        labelObject.transform.localScale = Vector3.one * 0.085f;

        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.text = "SORT  0 / 3";
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.94f, 0.96f, 0.96f, 1f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 3f;
        label.fontSizeMax = 8f;
        label.rectTransform.sizeDelta = new Vector2(15f, 3.5f);

        if (TMP_Settings.defaultFontAsset != null)
        {
            label.font = TMP_Settings.defaultFontAsset;
        }

        return label;
    }

    private static SortingGameResetUI CreateResetControl(
        Transform parent,
        SortingGameManager manager
    )
    {
        GameObject canvasObject = new GameObject(
            "Reset Round UI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(parent, false);

        RectTransform canvasRect =
            canvasObject.GetComponent<RectTransform>();
        canvasRect.position = new Vector3(-1.12f, 1.55f, 0.32f);
        OrientPanelTowardPlayer(canvasRect);
        canvasRect.localScale = Vector3.one * 0.00125f;
        canvasRect.sizeDelta = new Vector2(520f, 160f);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        GameObject panelObject = new GameObject(
            "Reset Panel",
            typeof(RectTransform),
            typeof(Image)
        );
        panelObject.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.04f, 0.06f, 0.07f, 0.94f);
        panelImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
            "UI/Skin/UISprite.psd"
        );
        panelImage.type = Image.Type.Sliced;

        GameObject buttonObject = new GameObject(
            "Reset Button",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );
        buttonObject.transform.SetParent(panelObject.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.06f, 0.14f);
        buttonRect.anchorMax = new Vector2(0.94f, 0.86f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.08f, 0.62f, 0.58f, 1f);
        buttonImage.sprite = panelImage.sprite;
        buttonImage.type = Image.Type.Sliced;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.08f, 0.62f, 0.58f, 1f);
        colors.highlightedColor = new Color(0.18f, 0.82f, 0.76f, 1f);
        colors.pressedColor = new Color(0.04f, 0.36f, 0.34f, 1f);
        button.colors = colors;

        GameObject textObject = new GameObject(
            "Reset Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI buttonLabel =
            textObject.GetComponent<TextMeshProUGUI>();
        buttonLabel.text = "RESET ROUND";
        buttonLabel.alignment = TextAlignmentOptions.Center;
        buttonLabel.fontSize = 52f;
        buttonLabel.color = Color.white;
        buttonLabel.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
        {
            buttonLabel.font = TMP_Settings.defaultFontAsset;
        }

        SortingGameResetUI resetUI =
            canvasObject.AddComponent<SortingGameResetUI>();
        resetUI.Configure(manager, button);

        WorldSpacePokeCanvas pokeCanvas =
            canvasObject.AddComponent<WorldSpacePokeCanvas>();
        pokeCanvas.Configure(canvas);
        pokeCanvas.EnsurePokeInteraction();

        EditorUtility.SetDirty(pokeCanvas);
        EditorUtility.SetDirty(resetUI);
        return resetUI;
    }

    private static SceneSwitchPanel CreateSceneSwitchControl(
        Transform parent
    )
    {
        GameObject canvasObject = new GameObject(
            "Scene Switch UI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(parent, false);

        RectTransform canvasRect =
            canvasObject.GetComponent<RectTransform>();
        canvasRect.position = new Vector3(-1.12f, 1.17f, 0.32f);
        OrientPanelTowardPlayer(canvasRect);
        canvasRect.localScale = Vector3.one * 0.0012f;
        canvasRect.sizeDelta = new Vector2(580f, 330f);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        GameObject panelObject = new GameObject(
            "Scene Panel",
            typeof(RectTransform),
            typeof(Image)
        );
        panelObject.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.04f, 0.06f, 0.07f, 0.94f);
        panelImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
            "UI/Skin/UISprite.psd"
        );
        panelImage.type = Image.Type.Sliced;

        Button sortingButton = CreateSceneButton(
            panelObject.transform,
            "Sorting Scene Button",
            "SORTING LAB",
            new Vector2(0.07f, 0.55f),
            new Vector2(0.93f, 0.9f),
            new Color(0.08f, 0.62f, 0.58f, 1f),
            panelImage.sprite
        );
        Button coopButton = CreateSceneButton(
            panelObject.transform,
            "Coop Scene Button",
            "CO-OP LIFT",
            new Vector2(0.07f, 0.1f),
            new Vector2(0.93f, 0.45f),
            new Color(0.88f, 0.3f, 0.24f, 1f),
            panelImage.sprite
        );

        WorldSpacePokeCanvas pokeCanvas =
            canvasObject.AddComponent<WorldSpacePokeCanvas>();
        pokeCanvas.Configure(canvas);
        pokeCanvas.EnsurePokeInteraction();

        SceneSwitchPanel sceneSwitch =
            canvasObject.AddComponent<SceneSwitchPanel>();
        sceneSwitch.Configure(
            sortingButton,
            coopButton,
            null,
            "VRSortingGame",
            "CoopLiftWorkshop"
        );
        EditorUtility.SetDirty(pokeCanvas);
        EditorUtility.SetDirty(sceneSwitch);
        return sceneSwitch;
    }

    private static Button CreateSceneButton(
        Transform parent,
        string objectName,
        string labelText,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color normalColor,
        Sprite sprite
    )
    {
        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );
        buttonObject.transform.SetParent(parent, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = anchorMin;
        buttonRect.anchorMax = anchorMax;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = normalColor;
        buttonImage.sprite = sprite;
        buttonImage.type = Image.Type.Sliced;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        ColorBlock colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = Color.Lerp(normalColor, Color.white, 0.24f);
        colors.pressedColor = Color.Lerp(normalColor, Color.black, 0.38f);
        button.colors = colors;

        GameObject textObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 42f;
        label.color = Color.white;
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
        {
            label.font = TMP_Settings.defaultFontAsset;
        }

        return button;
    }

    private static void OrientPanelTowardPlayer(RectTransform panel)
    {
        Vector3 outward = panel.position - new Vector3(0f, panel.position.y, 0f);

        if (outward.sqrMagnitude > 0.001f)
        {
            panel.rotation = Quaternion.LookRotation(
                outward.normalized,
                Vector3.up
            );
        }
    }

    private static void CreateLighting(Transform parent)
    {
        GameObject keyLight = new GameObject("Key Light");
        keyLight.transform.SetParent(parent, false);
        keyLight.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

        Light directional = keyLight.AddComponent<Light>();
        directional.type = LightType.Directional;
        directional.color = new Color(1f, 0.96f, 0.9f, 1f);
        directional.intensity = 1.15f;
        directional.shadows = LightShadows.Soft;

        GameObject fillLight = new GameObject("Workbench Fill");
        fillLight.transform.SetParent(parent, false);
        fillLight.transform.position = new Vector3(0f, 2.05f, 0.65f);

        Light fill = fillLight.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(0.72f, 0.86f, 1f, 1f);
        fill.intensity = 2.2f;
        fill.range = 3.2f;
        fill.shadows = LightShadows.None;
    }

    private static void ConfigureRenderSettings()
    {
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor =
            new Color(0.42f, 0.48f, 0.55f, 1f);
        RenderSettings.ambientEquatorColor =
            new Color(0.25f, 0.28f, 0.3f, 1f);
        RenderSettings.ambientGroundColor =
            new Color(0.11f, 0.12f, 0.13f, 1f);
        RenderSettings.reflectionIntensity = 0.65f;
    }

    private static Material CreateOrUpdateMaterial(
        string name,
        Color color,
        float metallic,
        float smoothness,
        bool emission = false
    )
    {
        string path = $"{MaterialRoot}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            throw new InvalidOperationException(
                "Universal Render Pipeline/Lit shader was not found."
            );
        }

        if (material == null)
        {
            material = new Material(shader)
            {
                name = name
            };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.enableInstancing = true;

        if (emission)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }
        else
        {
            material.DisableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject CreatePrimitive(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        bool useLocalTransform = false
    )
    {
        GameObject result = GameObject.CreatePrimitive(primitiveType);
        result.name = name;
        result.transform.SetParent(parent, false);

        if (useLocalTransform)
        {
            result.transform.localPosition = position;
        }
        else
        {
            result.transform.position = position;
        }

        result.transform.localRotation = Quaternion.identity;
        result.transform.localScale = scale;
        result.GetComponent<Renderer>().sharedMaterial = material;
        return result;
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static GameObject LoadPrefabByGuid(
        string guid,
        string displayName
    )
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"{displayName} prefab could not be loaded (GUID {guid})."
            );
        }

        return prefab;
    }

    private static void EnsureAssetFolders()
    {
        if (!AssetDatabase.IsValidFolder(AssetRoot))
        {
            AssetDatabase.CreateFolder("Assets", "VRSortingGame");
        }

        if (!AssetDatabase.IsValidFolder(MaterialRoot))
        {
            AssetDatabase.CreateFolder(AssetRoot, "Materials");
        }
    }

    private static void AddSceneToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            EditorBuildSettings.scenes.ToList();
        int sceneIndex = scenes.FindIndex(scene => scene.path == ScenePath);

        if (sceneIndex >= 0)
        {
            scenes[sceneIndex] = new EditorBuildSettingsScene(
                ScenePath,
                true
            );
        }
        else
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void ValidateBuildSettings()
    {
        if (!EditorBuildSettings.scenes.Any(scene =>
                scene.enabled && scene.path == ScenePath))
        {
            throw new InvalidOperationException(
                $"{ScenePath} must be enabled in Build Settings."
            );
        }
    }

    private static void ValidateScene(Scene scene)
    {
        PlacementPiece[] pieces = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<PlacementPiece>(true)
            )
            .ToArray();
        PlacementTarget[] targets = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<PlacementTarget>(true)
            )
            .ToArray();
        SortingGameManager[] managers = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SortingGameManager>(true)
            )
            .ToArray();
        SortingGameResetUI[] resetControls = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SortingGameResetUI>(true)
            )
            .ToArray();
        SceneSwitchPanel[] sceneSwitchPanels = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SceneSwitchPanel>(true)
            )
            .ToArray();
        MonoBehaviour[] spectatorStreamers = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<MonoBehaviour>(true)
            )
            .Where(component =>
                component != null &&
                component.GetType().FullName ==
                    "SignVR.Streaming.SpectatorViewStreamer"
            )
            .ToArray();

        if (pieces.Length != 3 || targets.Length != 3)
        {
            throw new InvalidOperationException(
                $"Expected three pieces and three targets, found " +
                $"{pieces.Length} and {targets.Length}."
            );
        }

        if (managers.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one SortingGameManager, found {managers.Length}."
            );
        }

        if (
            resetControls.Length != 1 ||
            resetControls[0].Manager != managers[0] ||
            resetControls[0].ResetButton == null ||
            !HasBoundPokeSurface(resetControls[0].GetComponent<WorldSpacePokeCanvas>())
        )
        {
            throw new InvalidOperationException(
                "The generated scene must contain one reset button bound " +
                "to the SortingGameManager."
            );
        }

        if (!IsSideMountedAndFacingPlayer(resetControls[0].transform))
        {
            throw new InvalidOperationException(
                "The reset button must remain side-mounted and face the player."
            );
        }

        if (
            sceneSwitchPanels.Length != 1 ||
            sceneSwitchPanels[0].SortingSceneButton == null ||
            sceneSwitchPanels[0].CoopSceneButton == null ||
            sceneSwitchPanels[0].SortingSceneName != "VRSortingGame" ||
            sceneSwitchPanels[0].CoopSceneName != "CoopLiftWorkshop" ||
            !HasBoundPokeSurface(
                sceneSwitchPanels[0].GetComponent<WorldSpacePokeCanvas>()
            ) ||
            !IsSideMountedAndFacingPlayer(sceneSwitchPanels[0].transform)
        )
        {
            throw new InvalidOperationException(
                "The generated scene must contain one side-mounted poke scene " +
                "switcher for VRSortingGame and CoopLiftWorkshop."
            );
        }

        if (spectatorStreamers.Length != 0)
        {
            throw new InvalidOperationException(
                "Spectator streaming must remain disabled in the sorting scene."
            );
        }

        if (managers[0].ResetBelowHeight > -0.25f)
        {
            throw new InvalidOperationException(
                "ResetBelowHeight must be below the floor so pieces can " +
                "settle under gravity."
            );
        }

        foreach (PlacementTarget target in targets)
        {
            Vector2 targetReach = new Vector2(
                target.transform.position.x,
                target.transform.position.z
            );
            BoxCollider trigger = target.GetComponent<BoxCollider>();

            if (
                targetReach.magnitude > 0.85f ||
                trigger == null ||
                trigger.size.x > 0.36f
            )
            {
                throw new InvalidOperationException(
                    $"Target {target.name} must remain inside the compact " +
                    "reach zone without overlapping adjacent triggers."
                );
            }
        }

        foreach (PlacementPiece piece in pieces)
        {
            Vector2 pieceReach = new Vector2(
                piece.transform.position.x,
                piece.transform.position.z
            );
            Rigidbody pieceRigidbody = piece.GetComponent<Rigidbody>();
            Grabbable grabbable = piece.GetComponent<Grabbable>();

            if (
                pieceReach.magnitude > 0.55f ||
                pieceRigidbody == null ||
                !pieceRigidbody.useGravity ||
                pieceRigidbody.isKinematic ||
                pieceRigidbody.collisionDetectionMode !=
                    CollisionDetectionMode.ContinuousDynamic ||
                grabbable == null
            )
            {
                throw new InvalidOperationException(
                    $"Piece {piece.name} must use a dynamic continuous " +
                    "Rigidbody and a Grabbable."
                );
            }

            SerializedObject grabbableData = new SerializedObject(grabbable);
            SerializedProperty throwWhenReleased =
                grabbableData.FindProperty("_throwWhenUnselected");
            SerializedProperty forceDynamicOnRelease =
                grabbableData.FindProperty("_forceKinematicDisabled");

            if (
                throwWhenReleased == null ||
                !throwWhenReleased.boolValue ||
                forceDynamicOnRelease == null ||
                !forceDynamicOnRelease.boolValue
            )
            {
                throw new InvalidOperationException(
                    $"Piece {piece.name} must preserve release velocity."
                );
            }
        }

        if (
            scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<OVRCameraRig>(true)
                )
                .Count() != 1
        )
        {
            throw new InvalidOperationException(
                "The generated scene must contain exactly one OVRCameraRig."
            );
        }

        OVRCameraRig cameraRig = scene.GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<OVRCameraRig>(true)
            )
            .Single();
        OVRCameraRigRef[] cameraRigRefs = scene.GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<OVRCameraRigRef>(true)
            )
            .ToArray();

        if (
            cameraRigRefs.Length != 1 ||
            cameraRigRefs[0].CameraRig != cameraRig
        )
        {
            throw new InvalidOperationException(
                "The interaction rig must reference the scene's OVRCameraRig."
            );
        }

        if (pieces.Select(piece => piece.PlacementId).Distinct().Count() != 3)
        {
            throw new InvalidOperationException(
                "Every sorting piece must have a unique placement ID."
            );
        }

        string[] pieceIds = pieces.Select(piece => piece.PlacementId)
            .OrderBy(id => id)
            .ToArray();
        string[] targetIds = targets.Select(target => target.AcceptedPlacementId)
            .OrderBy(id => id)
            .ToArray();

        if (!pieceIds.SequenceEqual(targetIds))
        {
            throw new InvalidOperationException(
                "Every sorting piece must have exactly one matching target."
            );
        }
    }

    private static bool IsSideMountedAndFacingPlayer(Transform panel)
    {
        Vector3 horizontalPosition = panel.position;
        horizontalPosition.y = 0f;

        if (
            Mathf.Abs(horizontalPosition.x) < 0.85f ||
            horizontalPosition.sqrMagnitude < 0.001f ||
            Vector3.Angle(Vector3.forward, horizontalPosition) < 55f
        )
        {
            return false;
        }

        Vector3 towardPlayer = -horizontalPosition.normalized;
        Vector3 panelFacing = -panel.forward;
        panelFacing.y = 0f;

        return panelFacing.sqrMagnitude > 0.001f &&
            Vector3.Dot(panelFacing.normalized, towardPlayer) > 0.9f;
    }

    private static bool HasBoundPokeSurface(WorldSpacePokeCanvas canvas)
    {
        if (canvas == null)
        {
            return false;
        }

        Transform interaction = canvas.transform.Find(
            "ISDK_PokeCanvasInteraction"
        );
        BoundsClipper clipper = interaction == null
            ? null
            : interaction.GetComponentInChildren<BoundsClipper>(true);

        Selectable[] selectables =
            canvas.GetComponentsInChildren<Selectable>(true);
        RectTransform canvasRect = canvas.transform as RectTransform;
        if (
            clipper == null ||
            canvasRect == null ||
            selectables.Length == 0 ||
            clipper.Size.x <= 0f ||
            clipper.Size.y <= 0f ||
            clipper.Size.z > 0.02f
        )
        {
            return false;
        }

        Bounds expectedBounds = default;
        bool hasBounds = false;
        foreach (Selectable selectable in selectables)
        {
            RectTransform selectableRect =
                selectable.transform as RectTransform;
            if (selectableRect == null)
            {
                continue;
            }

            Bounds candidate =
                RectTransformUtility.CalculateRelativeRectTransformBounds(
                    canvasRect,
                    selectableRect
                );
            if (!hasBounds)
            {
                expectedBounds = candidate;
                hasBounds = true;
            }
            else
            {
                expectedBounds.Encapsulate(candidate);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        expectedBounds.Expand(new Vector3(16f, 16f, 0.01f));
        float worldWidth = clipper.transform.TransformVector(
            Vector3.right * clipper.Size.x
        ).magnitude;
        float worldHeight = clipper.transform.TransformVector(
            Vector3.up * clipper.Size.y
        ).magnitude;

        return Vector3.Distance(clipper.Position, expectedBounds.center) <= 0.5f &&
            Mathf.Abs(clipper.Size.x - expectedBounds.size.x) <= 1f &&
            Mathf.Abs(clipper.Size.y - expectedBounds.size.y) <= 1f &&
            worldWidth <= 0.65f &&
            worldHeight <= 0.5f;
    }
}
