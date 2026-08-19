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
using SignVR.CoopRelay;
using SignVR.SceneFlow;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class CreateCoopLiftWorkshopScene
{
    internal const string ScenePath =
        "Assets/Scenes/CoopLiftWorkshop.unity";
    internal const string ManagerScriptPath =
        "Assets/Scripts/CoopRelay/CoopRelayGameManager.cs";
    internal const string SceneFlowScriptPath =
        "Assets/Scripts/SceneFlow/SceneSwitchPanel.cs";

    private const string SortingScenePath =
        "Assets/Scenes/VRSortingGame.unity";
    private const string AssetRoot = "Assets/VRCoopRelay";
    private const string MaterialRoot = AssetRoot + "/Materials";
    private const string CameraRigGuid =
        "126d619cf4daa52469682f85c1378b4a";
    private const string InteractionRigGuid =
        "0a7d2469f24041c4284c66706f84c45e";

    private const string StoneModelPath =
        "Assets/Resources/stone/source/Stein.fbx";
    private const string KeyModelPath =
        "Assets/Resources/key/source/key.fbx";
    private const string ThermosModelPath =
        "Assets/Resources/Thermos2/Thermos2.fbx";
    private const string ButtonModelPath =
        "Assets/Resources/elevator-button-lift/source/BUTON.fbx";
    private const string ArrowModelPath =
        "Assets/Resources/direction-arrow/source/3D RightArrow.fbx";

    private static readonly Color Teal =
        new Color(0.08f, 0.68f, 0.64f, 1f);
    private static readonly Color Amber =
        new Color(0.96f, 0.62f, 0.1f, 1f);
    private static readonly Color Green =
        new Color(0.32f, 0.72f, 0.34f, 1f);
    private static readonly Color Coral =
        new Color(0.93f, 0.28f, 0.23f, 1f);
    private static readonly Color Ice =
        new Color(0.44f, 0.76f, 0.96f, 1f);

    private sealed class Palette
    {
        public Material Floor;
        public Material Wall;
        public Material Metal;
        public Material Dark;
        public Material Light;
        public Material Teal;
        public Material Amber;
        public Material Green;
        public Material Coral;
        public Material Ice;
    }

    [MenuItem("Tools/SignVR/Create Co-op Lift Workshop Scene")]
    private static void CreateFromMenu()
    {
        if (
            File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog(
                "Rebuild Co-op Lift Workshop",
                "This replaces CoopLiftWorkshop.unity with the generated " +
                "version. Continue?",
                "Rebuild",
                "Cancel"
            )
        )
        {
            return;
        }

        CreateSceneForAutomation();
    }

    [MenuItem("Tools/SignVR/Validate Co-op Lift Workshop Scene")]
    private static void ValidateFromMenu()
    {
        ValidateSceneForAutomation();
        Debug.Log(
            "[SignVR] CoopLiftWorkshop.unity passed scene and Build " +
            "Settings validation."
        );
    }

    public static void CreateSceneForAutomation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before generating the co-op scene."
            );
        }

        Scene loaded = SceneManager.GetSceneByPath(ScenePath);

        if (loaded.IsValid() && loaded.isLoaded)
        {
            throw new InvalidOperationException(
                "Close CoopLiftWorkshop.unity before rebuilding it."
            );
        }

        ValidateExternalModels();
        EnsureAssetFolders();
        Scene originalActiveScene = SceneManager.GetActiveScene();
        bool hasSavedActiveScene =
            originalActiveScene.IsValid() &&
            !string.IsNullOrEmpty(originalActiveScene.path);

        if (!hasSavedActiveScene && !Application.isBatchMode)
        {
            throw new InvalidOperationException(
                "Save the active scene before generating the co-op scene."
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
                throw new IOException($"Unity could not save {ScenePath}.");
            }

            AddScenesToBuildSettings();
            ValidateBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[SignVR] Created CoopLiftWorkshop.unity: local two-role " +
                "relay scene, five physical props, side service panel, " +
                "and no networking or spectator streaming."
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
        Debug.Log("[SignVR] Co-op scene validation: begin.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
        Scene originalActiveScene = SceneManager.GetActiveScene();

        if (openedForValidation)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new FileNotFoundException(
                    "Generate the co-op lift scene before validating it.",
                    ScenePath
                );
            }

            scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive
            );
            Debug.Log("[SignVR] Co-op scene validation: scene opened.");
        }

        try
        {
            Debug.Log("[SignVR] Co-op scene validation: checking scene.");
            ValidateScene(scene);
            Debug.Log("[SignVR] Co-op scene validation: checking build settings.");
            ValidateBuildSettings();
            Debug.Log("[SignVR] Co-op scene validation: passed.");
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
        OVRCameraRig cameraRig = CreateVrRig(scene);
        cameraRig.transform.position = new Vector3(0f, 0f, -0.42f);
        CreatePointableEventSystem(scene);

        Transform environment = new GameObject("Environment").transform;
        Transform stationA = new GameObject("Station A - Load").transform;
        Transform stationB = new GameObject("Station B - Control").transform;
        Transform transfer = new GameObject("Transfer Bay").transform;
        Transform systems = new GameObject("Systems").transform;
        Transform presentation = new GameObject("Presentation").transform;
        Transform lighting = new GameObject("Lighting").transform;

        Palette palette = CreatePalette();
        CreateRoom(environment, palette);
        CreateStations(environment, stationA, stationB, transfer, palette);
        Transform[] liftDoors = CreateLiftDoor(environment, palette);
        CreateLighting(lighting);

        GameObject managerObject = new GameObject("Coop Relay Manager");
        managerObject.transform.SetParent(systems, false);
        CoopRelayGameManager manager =
            managerObject.AddComponent<CoopRelayGameManager>();

        TMP_Text statusLabel = CreateStatusDisplay(
            presentation,
            palette
        );
        CreateRoleLabels(presentation, palette);
        CreatePrivateSequenceDisplay(stationB, palette);

        CoopRelayItem[] items =
        {
            CreateRelayItem(
                stationA,
                "weight_teal",
                "Teal Counterweight",
                StoneModelPath,
                new Vector3(-1.08f, 0.93f, 0.28f),
                0.19f,
                ColliderKind.Sphere,
                new Vector3(0.19f, 0.19f, 0.19f),
                0.34f,
                palette.Teal
            ),
            CreateRelayItem(
                stationA,
                "weight_amber",
                "Amber Counterweight",
                StoneModelPath,
                new Vector3(-0.75f, 0.93f, 0.28f),
                0.19f,
                ColliderKind.Sphere,
                new Vector3(0.19f, 0.19f, 0.19f),
                0.34f,
                palette.Amber
            ),
            CreateRelayItem(
                stationA,
                "weight_green",
                "Green Counterweight",
                StoneModelPath,
                new Vector3(-0.42f, 0.93f, 0.28f),
                0.19f,
                ColliderKind.Sphere,
                new Vector3(0.19f, 0.19f, 0.19f),
                0.34f,
                palette.Green
            ),
            CreateRelayItem(
                transfer,
                "access_key",
                "Lift Access Key",
                KeyModelPath,
                new Vector3(-0.22f, 0.875f, 0.18f),
                0.17f,
                ColliderKind.Box,
                new Vector3(0.18f, 0.055f, 0.075f),
                0.1f,
                palette.Coral
            ),
            CreateRelayItem(
                transfer,
                "coolant_canister",
                "Coolant Canister",
                ThermosModelPath,
                new Vector3(0.18f, 0.98f, 0.18f),
                0.31f,
                ColliderKind.Capsule,
                new Vector3(0.12f, 0.31f, 0.12f),
                0.58f,
                palette.Ice
            )
        };

        CoopRelaySocket[] sockets =
        {
            CreateSocket(
                stationA,
                manager,
                "weight_teal",
                "Sequence Socket 1 - Teal",
                0,
                new Vector3(-1.08f, 0.805f, 0.7f),
                new Vector3(0.24f, 0.23f, 0.24f),
                0.105f,
                palette.Teal,
                palette
            ),
            CreateSocket(
                stationA,
                manager,
                "weight_amber",
                "Sequence Socket 2 - Amber",
                1,
                new Vector3(-0.75f, 0.805f, 0.7f),
                new Vector3(0.24f, 0.23f, 0.24f),
                0.105f,
                palette.Amber,
                palette
            ),
            CreateSocket(
                stationA,
                manager,
                "weight_green",
                "Sequence Socket 3 - Green",
                2,
                new Vector3(-0.42f, 0.805f, 0.7f),
                new Vector3(0.24f, 0.23f, 0.24f),
                0.105f,
                palette.Green,
                palette
            ),
            CreateSocket(
                stationB,
                manager,
                "access_key",
                "Key Reader",
                3,
                new Vector3(0.57f, 0.805f, 0.58f),
                new Vector3(0.22f, 0.19f, 0.20f),
                0.055f,
                palette.Coral,
                palette
            ),
            CreateSocket(
                stationB,
                manager,
                "coolant_canister",
                "Coolant Dock",
                4,
                new Vector3(0.98f, 0.805f, 0.58f),
                new Vector3(0.22f, 0.36f, 0.22f),
                0.165f,
                palette.Ice,
                palette
            )
        };

        manager.Configure(
            items,
            sockets,
            statusLabel,
            allowSoloDebug: true,
            standardReadyWindowSeconds: 6f,
            soloReadyWindowSeconds: 20f
        );

        CoopRelayPresentation relayPresentation =
            managerObject.AddComponent<CoopRelayPresentation>();
        relayPresentation.Configure(
            manager,
            liftDoors[0],
            liftDoors[1]
        );
        EditorUtility.SetDirty(relayPresentation);

        CreateStationAction(
            stationB,
            manager,
            CoopRelayAction.ConfirmConsole,
            "CONFIRM LOAD",
            new Vector3(1.2f, 1.15f, 0.37f),
            Quaternion.Euler(0f, -18f, 0f),
            palette.Amber,
            palette
        );
        CreateStationAction(
            stationA,
            manager,
            CoopRelayAction.ReadyLeft,
            "A  READY",
            new Vector3(-1.25f, 1.15f, 0.38f),
            Quaternion.Euler(0f, 18f, 0f),
            palette.Teal,
            palette
        );
        CreateStationAction(
            stationB,
            manager,
            CoopRelayAction.ReadyRight,
            "B  READY",
            new Vector3(1.25f, 1.15f, 0.73f),
            Quaternion.Euler(0f, -18f, 0f),
            palette.Green,
            palette
        );

        CreateServicePanel(presentation, manager, palette);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private enum ColliderKind
    {
        Sphere,
        Box,
        Capsule
    }

    private static CoopRelayItem CreateRelayItem(
        Transform parent,
        string id,
        string displayName,
        string modelPath,
        Vector3 position,
        float visualLongestSide,
        ColliderKind colliderKind,
        Vector3 colliderSize,
        float mass,
        Material identityMaterial
    )
    {
        GameObject root = new GameObject(displayName);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        AddCollider(root, colliderKind, colliderSize);
        GameObject visual = InstantiateModelVisual(
            root.transform,
            modelPath,
            "Model",
            visualLongestSide
        );

        GameObject band = CreatePrimitive(
            "Identity Marker",
            PrimitiveType.Cylinder,
            root.transform,
            colliderKind == ColliderKind.Capsule
                ? new Vector3(0f, -0.13f, 0f)
                : new Vector3(0f, -colliderSize.y * 0.45f, 0f),
            colliderKind == ColliderKind.Capsule
                ? new Vector3(0.07f, 0.008f, 0.07f)
                : new Vector3(0.085f, 0.008f, 0.085f),
            identityMaterial,
            useLocalTransform: true
        );
        UnityEngine.Object.DestroyImmediate(band.GetComponent<Collider>());
        band.transform.SetSiblingIndex(visual.transform.GetSiblingIndex() + 1);

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = mass;
        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        body.linearDamping = 0.06f;
        body.angularDamping = 0.1f;

        QuickActionsAPI.AddGrabInteraction(root);
        Grabbable grabbable = root.GetComponent<Grabbable>();

        if (grabbable == null)
        {
            throw new InvalidOperationException(
                $"Meta grab setup failed for {displayName}."
            );
        }

        grabbable.MaxGrabPoints = 2;
        grabbable.InjectOptionalTargetTransform(root.transform);
        grabbable.InjectOptionalRigidbody(body);
        grabbable.InjectOptionalKinematicWhileSelected(true);
        grabbable.InjectOptionalThrowWhenUnselected(true);
        grabbable.ForceKinematicDisabled = true;

        Behaviour[] interactions = root
            .GetComponentsInChildren<Behaviour>(true)
            .Where(component =>
                component is Grabbable ||
                component is HandGrabInteractable ||
                component is GrabInteractable
            )
            .ToArray();

        CoopRelayItem item = root.AddComponent<CoopRelayItem>();
        item.Configure(id, body, grabbable, interactions);
        EditorUtility.SetDirty(body);
        EditorUtility.SetDirty(grabbable);
        EditorUtility.SetDirty(item);
        return item;
    }

    private static void AddCollider(
        GameObject root,
        ColliderKind kind,
        Vector3 size
    )
    {
        switch (kind)
        {
            case ColliderKind.Sphere:
                SphereCollider sphere = root.AddComponent<SphereCollider>();
                sphere.radius = Mathf.Max(size.x, size.y, size.z) * 0.5f;
                break;

            case ColliderKind.Capsule:
                CapsuleCollider capsule =
                    root.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = Mathf.Max(size.x, size.z) * 0.5f;
                capsule.height = size.y;
                break;

            default:
                BoxCollider box = root.AddComponent<BoxCollider>();
                box.size = size;
                break;
        }
    }

    private static CoopRelaySocket CreateSocket(
        Transform parent,
        CoopRelayGameManager manager,
        string acceptedId,
        string displayName,
        int sequenceIndex,
        Vector3 position,
        Vector3 triggerSize,
        float snapHeight,
        Material accent,
        Palette palette
    )
    {
        GameObject socketObject = new GameObject(displayName);
        socketObject.transform.SetParent(parent, false);
        socketObject.transform.position = position;

        BoxCollider trigger = socketObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, triggerSize.y * 0.5f, 0f);
        trigger.size = triggerSize;

        GameObject basePlate = CreatePrimitive(
            "Dock Base",
            PrimitiveType.Cylinder,
            socketObject.transform,
            Vector3.zero,
            new Vector3(0.145f, 0.016f, 0.145f),
            palette.Dark,
            useLocalTransform: true
        );
        UnityEngine.Object.DestroyImmediate(
            basePlate.GetComponent<Collider>()
        );

        GameObject indicator = CreatePrimitive(
            "Sequence Indicator",
            PrimitiveType.Cylinder,
            socketObject.transform,
            new Vector3(0f, 0.018f, 0f),
            new Vector3(0.105f, 0.008f, 0.105f),
            accent,
            useLocalTransform: true
        );
        UnityEngine.Object.DestroyImmediate(
            indicator.GetComponent<Collider>()
        );

        Transform snap = CreateChild(socketObject.transform, "Snap Point");
        snap.localPosition = new Vector3(0f, snapHeight, 0f);

        CoopRelaySocket socket =
            socketObject.AddComponent<CoopRelaySocket>();
        socket.Configure(
            acceptedId,
            sequenceIndex,
            snap,
            indicator.GetComponent<Renderer>(),
            manager
        );
        EditorUtility.SetDirty(socket);
        return socket;
    }

    private static void CreateStationAction(
        Transform parent,
        CoopRelayGameManager manager,
        CoopRelayAction action,
        string label,
        Vector3 position,
        Quaternion rotation,
        Material accent,
        Palette palette
    )
    {
        Transform controlRoot = CreateChild(parent, label + " Control");
        controlRoot.position = position;
        controlRoot.rotation = rotation;

        InstantiateModelVisual(
            controlRoot,
            ButtonModelPath,
            "Elevator Button Housing",
            0.32f,
            new Vector3(0f, 0f, 0.045f)
        );

        Canvas canvas = CreateWorldCanvas(
            controlRoot,
            "Poke Surface",
            controlRoot.position,
            controlRoot.rotation,
            new Vector2(300f, 125f),
            0.0012f,
            sortingOrder: 20
        );
        Button button = CreateUiButton(
            canvas.transform,
            "Action",
            label,
            new Vector2(0.08f, 0.13f),
            new Vector2(0.92f, 0.87f),
            MaterialColor(accent),
            38f
        );

        WorldSpacePokeCanvas poke =
            canvas.gameObject.AddComponent<WorldSpacePokeCanvas>();
        poke.Configure(canvas);
        poke.EnsurePokeInteraction();

        CoopRelayActionButton actionButton =
            button.gameObject.AddComponent<CoopRelayActionButton>();
        actionButton.Configure(
            manager,
            action,
            button,
            button.GetComponentInChildren<TMP_Text>(true),
            label
        );
        EditorUtility.SetDirty(actionButton);
    }

    private static void CreateServicePanel(
        Transform parent,
        CoopRelayGameManager manager,
        Palette palette
    )
    {
        Canvas canvas = CreateWorldCanvas(
            parent,
            "Side Service Panel",
            new Vector3(-1.42f, 1.25f, -0.05f),
            Quaternion.Euler(0f, -78f, 0f),
            new Vector2(500f, 350f),
            0.0012f,
            sortingOrder: 30
        );
        Image panel = canvas.gameObject.AddComponent<Image>();
        panel.color = new Color(0.04f, 0.055f, 0.06f, 0.96f);
        panel.sprite = BuiltinUiSprite();
        panel.type = Image.Type.Sliced;

        CreateUiLabel(
            canvas.transform,
            "Service Label",
            "SERVICE",
            new Vector2(0.06f, 0.78f),
            new Vector2(0.94f, 0.97f),
            42f,
            Color.white
        );

        Button resetButton = CreateUiButton(
            canvas.transform,
            "Reset Round",
            "RESET ROUND",
            new Vector2(0.08f, 0.54f),
            new Vector2(0.92f, 0.76f),
            MaterialColor(palette.Coral),
            32f
        );
        Button coopButton = CreateUiButton(
            canvas.transform,
            "Co-op Scene",
            "CO-OP LIFT",
            new Vector2(0.08f, 0.29f),
            new Vector2(0.92f, 0.49f),
            MaterialColor(palette.Teal),
            30f
        );
        Button sortingButton = CreateUiButton(
            canvas.transform,
            "Sorting Scene",
            "SORTING LAB",
            new Vector2(0.08f, 0.05f),
            new Vector2(0.92f, 0.24f),
            MaterialColor(palette.Amber),
            30f
        );

        WorldSpacePokeCanvas poke =
            canvas.gameObject.AddComponent<WorldSpacePokeCanvas>();
        poke.Configure(canvas);
        poke.EnsurePokeInteraction();

        SceneSwitchPanel switchPanel =
            canvas.gameObject.AddComponent<SceneSwitchPanel>();
        switchPanel.Configure(sortingButton, coopButton);

        CoopRelayActionButton resetAction =
            resetButton.gameObject.AddComponent<CoopRelayActionButton>();
        resetAction.Configure(
            manager,
            CoopRelayAction.ResetRound,
            resetButton,
            resetButton.GetComponentInChildren<TMP_Text>(true),
            "RESET ROUND"
        );

        EditorUtility.SetDirty(switchPanel);
        EditorUtility.SetDirty(resetAction);
    }

    private static TMP_Text CreateStatusDisplay(
        Transform parent,
        Palette palette
    )
    {
        CreatePrimitive(
            "Lift Status Board",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 1.58f, 1.84f),
            new Vector3(1.25f, 0.34f, 0.045f),
            palette.Dark,
            withCollider: false
        );
        CreatePrimitive(
            "Status Accent",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 1.35f, 1.81f),
            new Vector3(1.25f, 0.026f, 0.055f),
            palette.Teal,
            withCollider: false
        );

        return CreateWorldText(
            parent,
            "Relay Status",
            "TEAM RELAY  0 / 5\nA: LOAD NEXT CARGO  B: READ ORDER",
            new Vector3(0f, 1.58f, 1.785f),
            Quaternion.identity,
            new Vector2(15f, 4.2f),
            0.085f,
            7.5f,
            Color.white,
            TextAlignmentOptions.Center
        );
    }

    private static void CreateRoleLabels(
        Transform parent,
        Palette palette
    )
    {
        CreatePrimitive(
            "A Role Plate",
            PrimitiveType.Cube,
            parent,
            new Vector3(-0.82f, 1.24f, 1.32f),
            new Vector3(0.72f, 0.2f, 0.035f),
            palette.Teal,
            withCollider: false
        );
        CreateWorldText(
            parent,
            "A Role Label",
            "A  LOAD + PASS",
            new Vector3(-0.82f, 1.24f, 1.276f),
            Quaternion.identity,
            new Vector2(13f, 2.5f),
            0.05f,
            6.5f,
            Color.white,
            TextAlignmentOptions.Center
        );

        CreatePrimitive(
            "B Role Plate",
            PrimitiveType.Cube,
            parent,
            new Vector3(0.82f, 1.24f, 1.32f),
            new Vector3(0.72f, 0.2f, 0.035f),
            palette.Amber,
            withCollider: false
        );
        CreateWorldText(
            parent,
            "B Role Label",
            "B  READ + RECEIVE",
            new Vector3(0.82f, 1.24f, 1.276f),
            Quaternion.identity,
            new Vector2(13f, 2.5f),
            0.05f,
            6.5f,
            new Color(0.08f, 0.08f, 0.07f, 1f),
            TextAlignmentOptions.Center
        );
    }

    private static void CreatePrivateSequenceDisplay(
        Transform parent,
        Palette palette
    )
    {
        Transform display = CreateChild(parent, "B Sequence Display");
        display.position = new Vector3(1.33f, 1.48f, 1.08f);
        display.rotation = Quaternion.Euler(0f, -34f, 0f);

        CreatePrimitive(
            "Sequence Board",
            PrimitiveType.Cube,
            display,
            Vector3.zero,
            new Vector3(0.72f, 0.58f, 0.035f),
            palette.Dark,
            useLocalTransform: true,
            withCollider: false
        );
        CreateWorldText(
            display,
            "Sequence Text",
            "B RUN ORDER\n1 TEAL WEIGHT\n2 AMBER WEIGHT\n3 GREEN WEIGHT\n4 ACCESS KEY\n5 COOLANT",
            new Vector3(0f, 0f, -0.043f),
            Quaternion.identity,
            new Vector2(13f, 10f),
            0.052f,
            4.8f,
            Color.white,
            TextAlignmentOptions.Center,
            useLocalTransform: true
        );

        for (int index = 0; index < 3; index++)
        {
            GameObject arrow = InstantiateModelVisual(
                display,
                ArrowModelPath,
                $"Order Arrow {index + 1}",
                0.12f,
                new Vector3(-0.27f + index * 0.27f, -0.39f, -0.055f)
            );
            arrow.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        CreatePrimitive(
            "Privacy Wing",
            PrimitiveType.Cube,
            parent,
            new Vector3(0.82f, 1.38f, 1.18f),
            new Vector3(0.055f, 0.72f, 0.5f),
            palette.Metal
        );
    }

    private static void CreateStations(
        Transform environment,
        Transform stationA,
        Transform stationB,
        Transform transfer,
        Palette palette
    )
    {
        CreateWorkbench(
            stationA,
            "A Load Bench",
            new Vector3(-0.75f, 0.75f, 0.55f),
            new Vector3(1.18f, 0.1f, 0.83f),
            palette.Dark,
            palette.Metal
        );
        CreateWorkbench(
            stationB,
            "B Control Bench",
            new Vector3(0.8f, 0.75f, 0.55f),
            new Vector3(1.08f, 0.1f, 0.83f),
            palette.Dark,
            palette.Metal
        );

        CreatePrimitive(
            "Center Safety Divider",
            PrimitiveType.Cube,
            environment,
            new Vector3(0.04f, 0.98f, 1.08f),
            new Vector3(0.075f, 1.25f, 0.92f),
            palette.Metal
        );
        CreatePrimitive(
            "Transfer Shelf",
            PrimitiveType.Cube,
            transfer,
            new Vector3(0f, 0.76f, 0.18f),
            new Vector3(0.68f, 0.08f, 0.36f),
            palette.Light
        );
        CreatePrimitive(
            "Transfer Frame Left",
            PrimitiveType.Cube,
            transfer,
            new Vector3(-0.38f, 0.98f, 0.18f),
            new Vector3(0.05f, 0.52f, 0.38f),
            palette.Amber
        );
        CreatePrimitive(
            "Transfer Frame Right",
            PrimitiveType.Cube,
            transfer,
            new Vector3(0.38f, 0.98f, 0.18f),
            new Vector3(0.05f, 0.52f, 0.38f),
            palette.Teal
        );
    }

    private static void CreateWorkbench(
        Transform parent,
        string name,
        Vector3 position,
        Vector3 topScale,
        Material topMaterial,
        Material legMaterial
    )
    {
        CreatePrimitive(
            name,
            PrimitiveType.Cube,
            parent,
            position,
            topScale,
            topMaterial
        );

        float halfX = topScale.x * 0.42f;
        float halfZ = topScale.z * 0.4f;

        foreach (float x in new[] { -halfX, halfX })
        {
            foreach (float z in new[] { -halfZ, halfZ })
            {
                CreatePrimitive(
                    name + " Leg",
                    PrimitiveType.Cube,
                    parent,
                    position + new Vector3(x, -0.37f, z),
                    new Vector3(0.075f, 0.72f, 0.075f),
                    legMaterial
                );
            }
        }
    }

    private static void CreateRoom(Transform parent, Palette palette)
    {
        CreatePrimitive(
            "Floor",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, -0.06f, 0.65f),
            new Vector3(3.4f, 0.12f, 2.7f),
            palette.Floor
        );
        CreatePrimitive(
            "Lift Wall",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 1.3f, 2f),
            new Vector3(3.4f, 2.6f, 0.12f),
            palette.Wall
        );
        CreatePrimitive(
            "Left Wall",
            PrimitiveType.Cube,
            parent,
            new Vector3(-1.7f, 1.3f, 0.65f),
            new Vector3(0.12f, 2.6f, 2.7f),
            palette.Wall
        );
        CreatePrimitive(
            "Right Wall",
            PrimitiveType.Cube,
            parent,
            new Vector3(1.7f, 1.3f, 0.65f),
            new Vector3(0.12f, 2.6f, 2.7f),
            palette.Wall
        );

        for (int index = -2; index <= 2; index++)
        {
            CreatePrimitive(
                "Floor Guide " + index,
                PrimitiveType.Cube,
                parent,
                new Vector3(index * 0.55f, 0.008f, -0.45f),
                new Vector3(0.34f, 0.012f, 0.055f),
                index < 0 ? palette.Teal : palette.Amber,
                withCollider: false
            );
        }
    }

    private static Transform[] CreateLiftDoor(
        Transform parent,
        Palette palette
    )
    {
        GameObject leftDoor = CreatePrimitive(
            "Lift Door Left",
            PrimitiveType.Cube,
            parent,
            new Vector3(-0.48f, 1.03f, 1.91f),
            new Vector3(0.9f, 1.85f, 0.08f),
            palette.Metal
        );
        GameObject rightDoor = CreatePrimitive(
            "Lift Door Right",
            PrimitiveType.Cube,
            parent,
            new Vector3(0.48f, 1.03f, 1.91f),
            new Vector3(0.9f, 1.85f, 0.08f),
            palette.Metal
        );
        CreatePrimitive(
            "Door Center Stripe",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 1.03f, 1.855f),
            new Vector3(0.035f, 1.85f, 0.025f),
            palette.Coral,
            withCollider: false
        );
        CreatePrimitive(
            "Door Header",
            PrimitiveType.Cube,
            parent,
            new Vector3(0f, 2.05f, 1.9f),
            new Vector3(2.1f, 0.2f, 0.15f),
            palette.Dark,
            withCollider: false
        );

        return new[] { leftDoor.transform, rightDoor.transform };
    }

    private static void CreateLighting(Transform parent)
    {
        GameObject keyLight = new GameObject("Directional Work Light");
        keyLight.transform.SetParent(parent, false);
        keyLight.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
        Light directional = keyLight.AddComponent<Light>();
        directional.type = LightType.Directional;
        directional.color = new Color(1f, 0.96f, 0.88f, 1f);
        directional.intensity = 1.05f;
        directional.shadows = LightShadows.Soft;

        CreatePointLight(
            parent,
            "A Station Light",
            new Vector3(-0.9f, 2.12f, 0.55f),
            new Color(0.45f, 0.95f, 0.9f, 1f)
        );
        CreatePointLight(
            parent,
            "B Station Light",
            new Vector3(0.9f, 2.12f, 0.55f),
            new Color(1f, 0.72f, 0.28f, 1f)
        );
    }

    private static void CreatePointLight(
        Transform parent,
        string name,
        Vector3 position,
        Color color
    )
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 1.8f;
        light.range = 2.7f;
        light.shadows = LightShadows.None;
    }

    private static Canvas CreateWorldCanvas(
        Transform parent,
        string name,
        Vector3 position,
        Quaternion rotation,
        Vector2 size,
        float scale,
        int sortingOrder
    )
    {
        GameObject canvasObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(parent, false);
        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.position = position;
        rect.rotation = rotation;
        rect.localScale = Vector3.one * scale;
        rect.sizeDelta = size;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;
        canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 12f;
        return canvas;
    }

    private static Button CreateUiButton(
        Transform parent,
        string name,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color,
        float fontSize
    )
    {
        GameObject buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        image.sprite = BuiltinUiSprite();
        image.type = Image.Type.Sliced;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.26f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.34f);
        colors.disabledColor = new Color(0.18f, 0.2f, 0.21f, 0.7f);
        button.colors = colors;

        CreateUiLabel(
            buttonObject.transform,
            "Label",
            label,
            Vector2.zero,
            Vector2.one,
            fontSize,
            Color.white
        );
        return button;
    }

    private static TMP_Text CreateUiLabel(
        Transform parent,
        string name,
        string text,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float fontSize,
        Color color
    )
    {
        GameObject labelObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        labelObject.transform.SetParent(parent, false);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Max(18f, fontSize * 0.55f);
        label.fontSizeMax = fontSize;
        label.color = color;
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
        {
            label.font = TMP_Settings.defaultFontAsset;
        }

        return label;
    }

    private static TextMeshPro CreateWorldText(
        Transform parent,
        string name,
        string text,
        Vector3 position,
        Quaternion rotation,
        Vector2 rectSize,
        float scale,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        bool useLocalTransform = false
    )
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        if (useLocalTransform)
        {
            textObject.transform.localPosition = position;
            textObject.transform.localRotation = rotation;
        }
        else
        {
            textObject.transform.position = position;
            textObject.transform.rotation = rotation;
        }
        textObject.transform.localScale = Vector3.one * scale;

        TextMeshPro label = textObject.AddComponent<TextMeshPro>();
        label.text = text;
        label.alignment = alignment;
        label.color = color;
        label.fontSize = fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Max(2f, fontSize * 0.55f);
        label.fontSizeMax = fontSize;
        label.rectTransform.sizeDelta = rectSize;

        if (TMP_Settings.defaultFontAsset != null)
        {
            label.font = TMP_Settings.defaultFontAsset;
        }

        return label;
    }

    private static GameObject InstantiateModelVisual(
        Transform parent,
        string assetPath,
        string name,
        float desiredLongestSide,
        Vector3 localCenter = default
    )
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"External model could not be loaded: {assetPath}"
            );
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(
            prefab,
            parent.gameObject.scene
        ) as GameObject;

        if (instance == null)
        {
            throw new InvalidOperationException(
                $"Unity could not instantiate {assetPath}."
            );
        }

        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Renderer[] renderers =
            instance.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
        {
            throw new InvalidOperationException(
                $"External model has no Renderer: {assetPath}"
            );
        }

        Bounds bounds = CalculateBounds(renderers);
        float longest = Mathf.Max(
            bounds.size.x,
            Mathf.Max(bounds.size.y, bounds.size.z)
        );

        if (longest <= 0.00001f)
        {
            throw new InvalidOperationException(
                $"External model has invalid bounds: {assetPath}"
            );
        }

        instance.transform.localScale *= desiredLongestSide / longest;
        bounds = CalculateBounds(renderers);
        Vector3 targetCenter = parent.TransformPoint(localCenter);
        instance.transform.position += targetCenter - bounds.center;
        return instance;
    }

    private static Bounds CalculateBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;

        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static GameObject CreatePrimitive(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        bool useLocalTransform = false,
        bool withCollider = true
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
        if (!withCollider)
        {
            UnityEngine.Object.DestroyImmediate(
                result.GetComponent<Collider>()
            );
        }
        return result;
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static Palette CreatePalette()
    {
        return new Palette
        {
            Floor = CreateOrUpdateMaterial(
                "Floor",
                new Color(0.24f, 0.26f, 0.27f, 1f),
                0.1f,
                0.35f
            ),
            Wall = CreateOrUpdateMaterial(
                "Wall",
                new Color(0.68f, 0.71f, 0.7f, 1f),
                0f,
                0.22f
            ),
            Metal = CreateOrUpdateMaterial(
                "Metal",
                new Color(0.38f, 0.41f, 0.42f, 1f),
                0.75f,
                0.58f
            ),
            Dark = CreateOrUpdateMaterial(
                "Dark",
                new Color(0.1f, 0.12f, 0.13f, 1f),
                0.42f,
                0.48f
            ),
            Light = CreateOrUpdateMaterial(
                "Transfer",
                new Color(0.82f, 0.83f, 0.78f, 1f),
                0.18f,
                0.42f
            ),
            Teal = CreateOrUpdateMaterial("Teal", Teal, 0.16f, 0.55f, true),
            Amber = CreateOrUpdateMaterial("Amber", Amber, 0.12f, 0.5f, true),
            Green = CreateOrUpdateMaterial("Green", Green, 0.1f, 0.48f, true),
            Coral = CreateOrUpdateMaterial("Coral", Coral, 0.08f, 0.45f, true),
            Ice = CreateOrUpdateMaterial("Ice", Ice, 0.2f, 0.64f, true)
        };
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
            material = new Material(shader) { name = name };
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
            material.SetColor("_EmissionColor", color * 0.22f);
        }
        else
        {
            material.DisableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static Color MaterialColor(Material material)
    {
        return material.HasProperty("_BaseColor")
            ? material.GetColor("_BaseColor")
            : Color.white;
    }

    private static Sprite BuiltinUiSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>(
            "UI/Skin/UISprite.psd"
        );
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
        UnityObjectAddedBroadcaster.HandleObjectWasAdded(interactionRigObject);

        OVRCameraRigRef cameraRigRef = interactionRigObject
            .GetComponentInChildren<OVRCameraRigRef>(true);

        if (cameraRigRef == null)
        {
            throw new InvalidOperationException(
                "Meta's interaction rig did not provide OVRCameraRigRef."
            );
        }

        cameraRig.gameObject.name = "OVRCameraRig";
        cameraRigRef.gameObject.name = "OVRComprehensiveInteractionRig";
        cameraRigRef.InjectInteractionOVRCameraRig(cameraRig);
        DisableDuplicateHandVisuals(cameraRig);

        OVRManager ovrManager = cameraRig.GetComponent<OVRManager>();

        if (ovrManager != null)
        {
            SerializedObject data = new SerializedObject(ovrManager);
            SerializedProperty trackingOrigin =
                data.FindProperty("_trackingOriginType");

            if (trackingOrigin != null)
            {
                trackingOrigin.intValue =
                    (int)OVRManager.TrackingOrigin.FloorLevel;
            }

            data.ApplyModifiedPropertiesWithoutUndo();
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
        string path = AssetDatabase.GUIDToAssetPath(guid);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"{displayName} prefab could not be loaded (GUID {guid})."
            );
        }

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

    private static void CreatePointableEventSystem(Scene scene)
    {
        GameObject eventSystemObject = new GameObject(
            "PointableCanvasEventSystem",
            typeof(EventSystem),
            typeof(PointableCanvasModule)
        );
        SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
        eventSystemObject.GetComponent<PointableCanvasModule>().ExclusiveMode =
            true;
    }

    private static void ConfigureRenderSettings()
    {
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor =
            new Color(0.48f, 0.52f, 0.55f, 1f);
        RenderSettings.ambientEquatorColor =
            new Color(0.28f, 0.3f, 0.3f, 1f);
        RenderSettings.ambientGroundColor =
            new Color(0.11f, 0.12f, 0.12f, 1f);
        RenderSettings.reflectionIntensity = 0.62f;
    }

    private static void ValidateExternalModels()
    {
        foreach (
            string path in new[]
            {
                StoneModelPath,
                KeyModelPath,
                ThermosModelPath,
                ButtonModelPath,
                ArrowModelPath
            }
        )
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                throw new FileNotFoundException(
                    "Required external model is missing.",
                    path
                );
            }
        }
    }

    private static void EnsureAssetFolders()
    {
        if (!AssetDatabase.IsValidFolder(AssetRoot))
        {
            AssetDatabase.CreateFolder("Assets", "VRCoopRelay");
        }

        if (!AssetDatabase.IsValidFolder(MaterialRoot))
        {
            AssetDatabase.CreateFolder(AssetRoot, "Materials");
        }
    }

    private static void AddScenesToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            EditorBuildSettings.scenes.ToList();
        scenes.RemoveAll(scene => scene.path == ScenePath);
        int firstEnabledIndex = scenes.FindIndex(scene => scene.enabled);
        int insertionIndex = firstEnabledIndex >= 0
            ? firstEnabledIndex
            : scenes.Count;
        scenes.Insert(
            insertionIndex,
            new EditorBuildSettingsScene(ScenePath, true)
        );

        int sortingIndex = scenes.FindIndex(
            scene => scene.path == SortingScenePath
        );

        if (sortingIndex >= 0)
        {
            EditorBuildSettingsScene sorting = scenes[sortingIndex];
            scenes[sortingIndex] = new EditorBuildSettingsScene(
                sorting.path,
                true
            );
        }
        else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SortingScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(SortingScenePath, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void ValidateBuildSettings()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        EditorBuildSettingsScene startup =
            scenes.FirstOrDefault(scene => scene.enabled);

        if (startup == null || startup.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"{ScenePath} must be the enabled startup scene."
            );
        }

        if (!scenes.Any(scene =>
                scene.enabled && scene.path == SortingScenePath))
        {
            throw new InvalidOperationException(
                "VRSortingGame.unity must remain enabled for scene switching."
            );
        }
    }

    private static void ValidateScene(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        CoopRelayGameManager[] managers = FindAll<CoopRelayGameManager>(roots);
        CoopRelayItem[] items = FindAll<CoopRelayItem>(roots);
        CoopRelaySocket[] sockets = FindAll<CoopRelaySocket>(roots);
        SceneSwitchPanel[] switchPanels = FindAll<SceneSwitchPanel>(roots);
        CoopRelayPresentation[] presentations =
            FindAll<CoopRelayPresentation>(roots);
        CoopRelayActionButton[] actionButtons =
            FindAll<CoopRelayActionButton>(roots);
        OVRCameraRig[] cameraRigs = FindAll<OVRCameraRig>(roots);
        OVRCameraRigRef[] cameraRigRefs = FindAll<OVRCameraRigRef>(roots);

        if (managers.Length != 1 || items.Length != 5 || sockets.Length != 5)
        {
            throw new InvalidOperationException(
                "The co-op scene requires one manager, five physical " +
                "items, and five ordered sockets."
            );
        }

        if (
            presentations.Length != 1 ||
            presentations[0].Manager != managers[0] ||
            presentations[0].LeftDoor == null ||
            presentations[0].RightDoor == null
        )
        {
            throw new InvalidOperationException(
                "The co-op manager must drive one non-locomoting lift-door " +
                "presentation."
            );
        }

        string[] expectedIds =
        {
            "weight_teal",
            "weight_amber",
            "weight_green",
            "access_key",
            "coolant_canister"
        };
        string[] itemIds = items.Select(item => item.ItemId)
            .OrderBy(id => id)
            .ToArray();
        string[] socketIds = sockets.Select(socket => socket.AcceptedItemId)
            .OrderBy(id => id)
            .ToArray();

        if (
            !itemIds.SequenceEqual(expectedIds.OrderBy(id => id)) ||
            !socketIds.SequenceEqual(expectedIds.OrderBy(id => id)) ||
            !sockets.Select(socket => socket.SequenceIndex)
                .OrderBy(index => index)
                .SequenceEqual(Enumerable.Range(0, 5))
        )
        {
            throw new InvalidOperationException(
                "Co-op item IDs and socket sequence must be complete and unique."
            );
        }

        foreach (CoopRelayItem item in items)
        {
            Rigidbody body = item.GetComponent<Rigidbody>();
            Grabbable grabbable = item.GetComponent<Grabbable>();

            if (
                body == null ||
                !body.useGravity ||
                body.isKinematic ||
                body.collisionDetectionMode !=
                    CollisionDetectionMode.ContinuousDynamic ||
                grabbable == null
            )
            {
                throw new InvalidOperationException(
                    $"{item.name} must be a dynamic, throwable Meta grab item."
                );
            }

            SerializedObject data = new SerializedObject(grabbable);
            SerializedProperty throwOnRelease =
                data.FindProperty("_throwWhenUnselected");
            SerializedProperty forceDynamic =
                data.FindProperty("_forceKinematicDisabled");

            if (
                throwOnRelease == null ||
                !throwOnRelease.boolValue ||
                forceDynamic == null ||
                !forceDynamic.boolValue
            )
            {
                throw new InvalidOperationException(
                    $"{item.name} must preserve release momentum."
                );
            }
        }

        if (
            switchPanels.Length != 1 ||
            Mathf.Abs(switchPanels[0].transform.position.x) < 1.15f ||
            switchPanels[0].SortingSceneButton == null ||
            switchPanels[0].CoopSceneButton == null
        )
        {
            throw new InvalidOperationException(
                "Scene switching must be on one side-mounted service panel."
            );
        }

        CoopRelayAction[] requiredActions =
        {
            CoopRelayAction.ConfirmConsole,
            CoopRelayAction.ReadyLeft,
            CoopRelayAction.ReadyRight,
            CoopRelayAction.ResetRound
        };

        if (
            actionButtons.Length != requiredActions.Length ||
            requiredActions.Any(required =>
                actionButtons.Count(button => button.Action == required) != 1)
        )
        {
            throw new InvalidOperationException(
                "Confirm, two station-ready, and side reset actions are required."
            );
        }

        if (
            cameraRigs.Length != 1 ||
            cameraRigRefs.Length != 1 ||
            cameraRigRefs[0].CameraRig != cameraRigs[0]
        )
        {
            throw new InvalidOperationException(
                "The scene requires one correctly wired Meta interaction rig."
            );
        }

        ValidateCollisionGeometry(roots, items, sockets);

        bool hasSpectator = roots
            .SelectMany(root =>
                root.GetComponentsInChildren<MonoBehaviour>(true))
            .Any(component =>
                component != null &&
                component.GetType().Name == "SpectatorViewStreamer");

        if (hasSpectator)
        {
            throw new InvalidOperationException(
                "Spectator streaming must remain disabled in the co-op scene."
            );
        }
    }

    private static void ValidateCollisionGeometry(
        GameObject[] roots,
        CoopRelayItem[] items,
        CoopRelaySocket[] sockets
    )
    {
        foreach (CoopRelayItem item in items)
        {
            Collider[] colliders = item.GetComponents<Collider>();
            Collider physicsCollider = colliders.FirstOrDefault(
                collider => collider != null && !collider.isTrigger
            );

            if (physicsCollider == null || colliders.Length != 1)
            {
                throw new InvalidOperationException(
                    $"{item.name} must have exactly one non-trigger root collider."
                );
            }

            if (item.ItemId.StartsWith("weight_", StringComparison.Ordinal))
            {
                SphereCollider sphere = physicsCollider as SphereCollider;
                if (sphere == null || sphere.radius < 0.08f || sphere.radius > 0.11f)
                {
                    throw new InvalidOperationException(
                        $"{item.name} stone collider is outside the 0.16-0.22m range."
                    );
                }
            }
            else if (item.ItemId == "access_key")
            {
                BoxCollider box = physicsCollider as BoxCollider;
                if (
                    box == null ||
                    box.size.x > 0.22f ||
                    box.size.y > 0.09f ||
                    box.size.z > 0.12f
                )
                {
                    throw new InvalidOperationException(
                        "The key collider must stay close to the key mesh."
                    );
                }
            }
            else if (item.ItemId == "coolant_canister")
            {
                CapsuleCollider capsule = physicsCollider as CapsuleCollider;
                if (
                    capsule == null ||
                    capsule.radius > 0.075f ||
                    capsule.height > 0.36f
                )
                {
                    throw new InvalidOperationException(
                        "The coolant collider must stay close to the canister mesh."
                    );
                }
            }
        }

        Dictionary<string, Vector3> expectedTriggerSizes =
            new Dictionary<string, Vector3>(StringComparer.Ordinal)
            {
                ["weight_teal"] = new Vector3(0.24f, 0.23f, 0.24f),
                ["weight_amber"] = new Vector3(0.24f, 0.23f, 0.24f),
                ["weight_green"] = new Vector3(0.24f, 0.23f, 0.24f),
                ["access_key"] = new Vector3(0.22f, 0.19f, 0.20f),
                ["coolant_canister"] = new Vector3(0.22f, 0.36f, 0.22f)
            };

        foreach (CoopRelaySocket socket in sockets)
        {
            BoxCollider trigger = socket.GetComponent<BoxCollider>();
            if (
                trigger == null ||
                !trigger.isTrigger ||
                !expectedTriggerSizes.TryGetValue(
                    socket.AcceptedItemId,
                    out Vector3 expectedSize
                ) ||
                Vector3.Distance(trigger.size, expectedSize) > 0.001f ||
                Mathf.Abs(trigger.center.y - expectedSize.y * 0.5f) > 0.001f
            )
            {
                throw new InvalidOperationException(
                    $"Socket '{socket.name}' has an unexpected trigger volume."
                );
            }
        }

        WorldSpacePokeCanvas[] pokeCanvases =
            FindAll<WorldSpacePokeCanvas>(roots);
        if (pokeCanvases.Length != 4)
        {
            throw new InvalidOperationException(
                "The co-op scene should contain three station and one service " +
                "poke canvases."
            );
        }

        foreach (WorldSpacePokeCanvas pokeCanvas in pokeCanvases)
        {
            RectTransform canvasRect = pokeCanvas.transform as RectTransform;
            Transform interaction = pokeCanvas.transform.Find(
                "ISDK_PokeCanvasInteraction"
            );
            BoundsClipper clipper = interaction == null
                ? null
                : interaction.GetComponentInChildren<BoundsClipper>(true);
            Selectable[] selectables =
                pokeCanvas.GetComponentsInChildren<Selectable>(true);

            if (canvasRect == null || clipper == null || selectables.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Poke canvas '{pokeCanvas.name}' is missing a bounded " +
                    "button surface."
                );
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

            expectedBounds.Expand(new Vector3(16f, 16f, 0.01f));
            if (
                Vector3.Distance(clipper.Position, expectedBounds.center) > 0.5f ||
                Mathf.Abs(clipper.Size.x - expectedBounds.size.x) > 1f ||
                Mathf.Abs(clipper.Size.y - expectedBounds.size.y) > 1f ||
                clipper.Size.z > 0.02f
            )
            {
                throw new InvalidOperationException(
                    $"Poke canvas '{pokeCanvas.name}' clip volume does not " +
                    "match its buttons."
                );
            }

            float worldWidth = clipper.transform.TransformVector(
                Vector3.right * clipper.Size.x
            ).magnitude;
            float worldHeight = clipper.transform.TransformVector(
                Vector3.up * clipper.Size.y
            ).magnitude;
            if (worldWidth > 0.65f || worldHeight > 0.5f)
            {
                throw new InvalidOperationException(
                    $"Poke canvas '{pokeCanvas.name}' remains too large in world space."
                );
            }
        }
    }

    private static T[] FindAll<T>(GameObject[] roots)
        where T : Component
    {
        return roots
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }
}
