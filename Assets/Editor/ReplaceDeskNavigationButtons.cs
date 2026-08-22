using Oculus.Interaction;
using SignVR.Recording;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ReplaceDeskNavigationButtons
{
    private const string ModelPath =
        "Assets/ThirdParty/PolyPizza/ZoeButtonSwitch/model.obj";
    private const string PokeButtonPrefabPath =
        "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Prefabs/OculusInteractionSamplePokeButton.prefab";
    private const string CapMaterialPath = "Assets/Materials/RecordingRedButtonCap.mat";
    private const string BaseMaterialPath = "Assets/Materials/RecordingRedButtonBase.mat";
    private const float PressTravelWorld = 0.006f;

    [MenuItem("SignVR/Repair Desk Navigation Poke Only")]
    public static void RepairOnlyButtonPokeInteraction()
    {
        RepairExistingButton(
            "Environment/DeskNavigationButtons/PreviousSentenceModel"
        );
        RepairExistingButton(
            "Environment/DeskNavigationButtons/NextSentenceModel"
        );

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(
            "[SignVR] Repaired only the two existing desk-button poke interactions; button root transforms and labels were preserved."
        );
    }

    [MenuItem("SignVR/Replace Desk Navigation Button Models Only")]
    public static void ReplaceOnlyButtonModelsAndInteraction()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        GameObject pokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PokeButtonPrefabPath);
        Material capMaterial = AssetDatabase.LoadAssetAtPath<Material>(CapMaterialPath);
        Material baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
        QuestDeviceGateway gateway = UnityEngine.Object.FindFirstObjectByType<QuestDeviceGateway>();

        ReplaceButton(
            "Environment/DeskNavigationButtons/PreviousSentenceModel",
            false,
            modelAsset,
            pokePrefab,
            capMaterial,
            baseMaterial,
            gateway
        );
        ReplaceButton(
            "Environment/DeskNavigationButtons/NextSentenceModel",
            true,
            modelAsset,
            pokePrefab,
            capMaterial,
            baseMaterial,
            gateway
        );

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(
            "[SignVR] Replaced only the two desk button models and their poke interaction; root transforms were preserved."
        );
    }

    private static void ReplaceButton(
        string path,
        bool next,
        GameObject modelAsset,
        GameObject pokePrefab,
        Material capMaterial,
        Material baseMaterial,
        QuestDeviceGateway gateway)
    {
        GameObject root = GameObject.Find(path);
        Vector3 preservedPosition = root.transform.position;
        Quaternion preservedRotation = root.transform.rotation;
        Vector3 preservedScale = root.transform.localScale;

        Transform oldVisual = root.transform.Find("ButtonVisual") ?? root.transform.Find("default");
        Bounds targetBounds = GetBounds(oldVisual.gameObject);
        Undo.DestroyObjectImmediate(oldVisual.gameObject);

        Transform oldInteraction = root.transform.Find("PokeInteraction");
        if (oldInteraction != null)
        {
            Undo.DestroyObjectImmediate(oldInteraction.gameObject);
        }

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
        Undo.RegisterCreatedObjectUndo(visual, "Replace desk button model");
        visual.name = "ButtonVisual";
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        visual.transform.localScale = Vector3.one;

        Bounds visualBounds = GetBounds(visual);
        float targetFootprint = Mathf.Max(targetBounds.size.x, targetBounds.size.z);
        float visualFootprint = Mathf.Max(visualBounds.size.x, visualBounds.size.z);
        visual.transform.localScale *= targetFootprint / visualFootprint;
        visualBounds = GetBounds(visual);
        visual.transform.position += new Vector3(
            targetBounds.center.x - visualBounds.center.x,
            targetBounds.min.y - visualBounds.min.y,
            targetBounds.center.z - visualBounds.center.z
        );
        SetLayerRecursively(visual, root.layer);

        Transform buttonCap = visual.transform.Find("mesh114495047");

        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = renderer.transform == buttonCap ||
                                      renderer.transform.IsChildOf(buttonCap)
                ? capMaterial
                : baseMaterial;
        }

        visualBounds = GetBounds(visual);
        GameObject interaction = (GameObject)PrefabUtility.InstantiatePrefab(
            pokePrefab,
            root.transform
        );
        Undo.RegisterCreatedObjectUndo(interaction, "Create desk button poke interaction");
        interaction.name = "PokeInteraction";
        interaction.transform.SetPositionAndRotation(
            new Vector3(
                visualBounds.center.x,
                visualBounds.max.y - PressTravelWorld,
                visualBounds.center.z
            ),
            Quaternion.LookRotation(Vector3.down, root.transform.forward)
        );
        float inverseRootScale = 1f / root.transform.lossyScale.x;
        interaction.transform.localScale = new Vector3(0.095f, 0.05f, 0.095f) * inverseRootScale;
        SetLayerRecursively(interaction, root.layer);

        foreach (Renderer renderer in interaction.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }
        foreach (TMP_Text text in interaction.GetComponentsInChildren<TMP_Text>(true))
        {
            text.enabled = false;
        }

        PointableUnityEventWrapper wrapper =
            interaction.GetComponentInChildren<PointableUnityEventWrapper>(true);
        ConfigurePhysicalPoke(interaction.GetComponent<PokeInteractable>());
        RecordingPokeAction action = GetOrAdd<RecordingPokeAction>(root);
        action.ConfigureNavigation(wrapper, gateway, next);
        ConfigureNativeCapPress(root, visual.transform, buttonCap, interaction.transform);

        root.transform.position = preservedPosition;
        root.transform.rotation = preservedRotation;
        root.transform.localScale = preservedScale;
        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(action);
    }

    private static void RepairExistingButton(string path)
    {
        GameObject root = GameObject.Find(path);
        Transform visual = root.transform.Find("ButtonVisual");
        Transform interaction = root.transform.Find("PokeInteraction");
        Transform pressVisual = visual.Find("CapPressVisual");
        Transform buttonCap = pressVisual != null
            ? pressVisual.Find("mesh114495047")
            : visual.Find("mesh114495047");

        Vector3 preservedPosition = root.transform.position;
        Quaternion preservedRotation = root.transform.rotation;
        Vector3 preservedScale = root.transform.localScale;

        ConfigurePhysicalPoke(interaction.GetComponent<PokeInteractable>());
        ConfigureNativeCapPress(root, visual, buttonCap, interaction);

        root.transform.position = preservedPosition;
        root.transform.rotation = preservedRotation;
        root.transform.localScale = preservedScale;
        EditorUtility.SetDirty(root);
    }

    private static void ConfigureNativeCapPress(
        GameObject root,
        Transform visual,
        Transform buttonCap,
        Transform interaction)
    {
        Bounds capBounds = GetBounds(buttonCap.gameObject);
        Vector3 releasedContactPoint = new Vector3(
            capBounds.center.x,
            capBounds.max.y,
            capBounds.center.z
        );
        Quaternion pressOrientation = Quaternion.LookRotation(
            Vector3.down,
            root.transform.forward
        );

        GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(
            buttonCap.gameObject
        );
        if (prefabRoot != null && prefabRoot.transform.IsChildOf(root.transform))
        {
            PrefabUtility.UnpackPrefabInstance(
                prefabRoot,
                PrefabUnpackMode.OutermostRoot,
                InteractionMode.AutomatedAction
            );
        }

        Transform pressVisual = visual.Find("CapPressVisual");
        if (pressVisual == null)
        {
            GameObject pressVisualObject = new GameObject("CapPressVisual");
            Undo.RegisterCreatedObjectUndo(
                pressVisualObject,
                "Create native desk button press visual"
            );
            pressVisual = pressVisualObject.transform;
            pressVisual.SetParent(visual, false);
        }

        pressVisual.SetPositionAndRotation(releasedContactPoint, pressOrientation);
        pressVisual.localScale = Vector3.one;

        if (buttonCap.parent != pressVisual)
        {
            Undo.SetTransformParent(
                buttonCap,
                pressVisual,
                "Attach desk button cap to native poke visual"
            );
        }

        interaction.SetPositionAndRotation(
            releasedContactPoint + Vector3.down * PressTravelWorld,
            pressOrientation
        );

        PokeInteractable pokeInteractable = interaction.GetComponent<PokeInteractable>();
        Transform buttonBase = interaction.Find("Model/Surface");
        PokeInteractableVisual nativeVisual = GetOrAdd<PokeInteractableVisual>(
            pressVisual.gameObject
        );
        nativeVisual.InjectAllPokeInteractableVisual(pokeInteractable, buttonBase);

        PokeInteractableVisual hiddenSampleVisual =
            interaction.Find("Visuals/ButtonVisual")
                .GetComponent<PokeInteractableVisual>();
        hiddenSampleVisual.enabled = false;

        SetLayerRecursively(pressVisual.gameObject, root.layer);
        EditorUtility.SetDirty(pressVisual);
        EditorUtility.SetDirty(nativeVisual);
        EditorUtility.SetDirty(hiddenSampleVisual);
        EditorUtility.SetDirty(interaction);
    }

    private static void ConfigurePhysicalPoke(PokeInteractable pokeInteractable)
    {
        Undo.RecordObject(pokeInteractable, "Tune desk button poke interaction");
        SerializedObject serialized = new SerializedObject(pokeInteractable);
        serialized.FindProperty("_enterHoverNormal").floatValue = 0.025f;
        serialized.FindProperty("_enterHoverTangent").floatValue = 0f;
        serialized.FindProperty("_exitHoverNormal").floatValue = 0.025f;
        serialized.FindProperty("_exitHoverTangent").floatValue = 0f;
        serialized.FindProperty("_cancelSelectNormal").floatValue = 0.3f;
        serialized.FindProperty("_cancelSelectTangent").floatValue = 0.03f;
        serialized.FindProperty("_closeDistanceThreshold").floatValue = 0.001f;

        SerializedProperty minThresholds = serialized.FindProperty("_minThresholds");
        minThresholds.FindPropertyRelative("Enabled").boolValue = true;
        minThresholds.FindPropertyRelative("MinNormal").floatValue = 0.025f;

        SerializedProperty positionPinning = serialized.FindProperty("_positionPinning");
        positionPinning.FindPropertyRelative("Enabled").boolValue = true;
        positionPinning.FindPropertyRelative("MaxPinDistance").floatValue = 0f;
        serialized.ApplyModifiedProperties();
    }

    private static Bounds GetBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.GetComponent<T>() ?? Undo.AddComponent<T>(target);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
