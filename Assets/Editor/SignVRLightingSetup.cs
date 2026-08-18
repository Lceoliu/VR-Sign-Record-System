using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SignVRLightingSetup
{
    private const string RecordingScenePath = "Assets/Scenes/Recording.unity";
    private const string LightingSettingsPath =
        "Assets/Settings/SignVRRecordingLightingSettings.lighting";
    private const string VolumeProfilePath =
        "Assets/Settings/SignVRRecordingPostProcess.asset";
    private const string MaterialFolder = "Assets/Materials/SignVR Environment";

    [MenuItem("SignVR/Lighting/Prepare Recording Scene")]
    public static void PrepareRecordingScene()
    {
        RequireRecordingScene();

        Transform environment = RequireTransform("Environment");
        Transform importedEnvironment = EnsureChild(
            environment,
            "ImportedEnvironment"
        );
        Transform lightingRig = EnsureChild(
            environment,
            "SignVR Lighting"
        );

        DisableLegacyDirectionalLights(environment);
        ConfigureExistingLocalLights(environment);
        EnsureMainLight(lightingRig);
        EnsureBakedCeilingLights(lightingRig);
        EnsureLightProbeGrid(lightingRig);
        DisableLegacyLightProbe(environment);
        EnsureReflectionProbe(lightingRig);
        EnsureGlobalVolume(lightingRig);
        EnsureLightingSettings();
        EnsureEnvironmentMaterials();
        EnablePostProcessingOnHmdCamera();
        ConfigureAmbientLighting();

        EditorSceneManager.MarkSceneDirty(
            EditorSceneManager.GetActiveScene()
        );
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = importedEnvironment.gameObject;

        Debug.Log(
            "[SignVRLightingSetup] Bake-ready lighting is prepared. " +
            "Drag static room assets under Environment/ImportedEnvironment, " +
            "then run SignVR/Lighting/Bake Recording Scene."
        );
    }

    [MenuItem("SignVR/Lighting/Mark Imported Environment Static")]
    public static void MarkImportedEnvironmentStatic()
    {
        RequireRecordingScene();
        Transform importedEnvironment = RequireTransform(
            "Environment/ImportedEnvironment"
        );

        StaticEditorFlags flags =
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.ReflectionProbeStatic;

        foreach (
            Transform child in importedEnvironment.GetComponentsInChildren<Transform>(
                true
            )
        )
        {
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.receiveGI = ReceiveGI.Lightmaps;
                EditorUtility.SetDirty(renderer);
            }
        }

        EditorSceneManager.MarkSceneDirty(
            EditorSceneManager.GetActiveScene()
        );
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log(
            "[SignVRLightingSetup] ImportedEnvironment is marked for " +
            "lightmaps, batching, occlusion, and reflection probes."
        );
    }

    [MenuItem("SignVR/Lighting/Bake Recording Scene")]
    public static void BakeRecordingScene()
    {
        PrepareRecordingScene();
        MarkImportedEnvironmentStatic();

        if (Lightmapping.isRunning)
        {
            throw new InvalidOperationException(
                "A lighting bake is already running."
            );
        }

        Lightmapping.BakeAsync();
        Debug.Log(
            "[SignVRLightingSetup] Lighting bake started. Monitor progress in " +
            "Window > Rendering > Lighting."
        );
    }

    private static void EnsureLightingSettings()
    {
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
            LightingSettingsPath
        );
        if (settings == null)
        {
            settings = new LightingSettings
            {
                name = "SignVR Recording Lighting Settings",
                bakedGI = true,
                realtimeGI = false,
                mixedBakeMode = MixedLightingMode.IndirectOnly,
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                lightmapResolution = 20f,
                lightmapMaxSize = 1024,
                lightmapPadding = 4,
                indirectResolution = 2f,
                maxBounces = 2,
                minBounces = 1,
                directSampleCount = 32,
                indirectSampleCount = 128,
                environmentSampleCount = 64,
                ao = true,
                aoMaxDistance = 1.25f,
                aoExponentDirect = 1f,
                aoExponentIndirect = 1.15f
            };
            AssetDatabase.CreateAsset(settings, LightingSettingsPath);
        }

        Lightmapping.lightingSettings = settings;
        EditorUtility.SetDirty(settings);
    }

    private static void EnsureMainLight(Transform lightingRig)
    {
        Transform transform = EnsureChild(lightingRig, "Main Key (Mixed)");
        Light light = transform.GetComponent<Light>();
        if (light != null)
        {
            return;
        }

        light = Undo.AddComponent<Light>(transform.gameObject);
        transform.gameObject.SetActive(true);
        light.type = LightType.Directional;
        light.lightmapBakeType = LightmapBakeType.Mixed;
        light.color = new Color(1f, 0.94f, 0.84f, 1f);
        light.intensity = 0.85f;
        light.bounceIntensity = 1f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.72f;
        transform.localRotation = Quaternion.Euler(48f, -32f, 0f);
        EditorUtility.SetDirty(light);
    }

    private static void EnsureBakedCeilingLights(Transform lightingRig)
    {
        EnsureBakedAreaLight(
            lightingRig,
            "Ceiling Softbox Left (Baked)",
            new Vector3(-1.4f, 2.65f, 0.9f)
        );
        EnsureBakedAreaLight(
            lightingRig,
            "Ceiling Softbox Right (Baked)",
            new Vector3(1.4f, 2.65f, 0.9f)
        );
    }

    private static void EnsureBakedAreaLight(
        Transform parent,
        string name,
        Vector3 position)
    {
        Transform transform = EnsureChild(parent, name);
        Light light = transform.GetComponent<Light>();
        if (light != null)
        {
            return;
        }

        transform.localPosition = position;
        transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        light = Undo.AddComponent<Light>(transform.gameObject);
        light.type = LightType.Rectangle;
        light.lightmapBakeType = LightmapBakeType.Baked;
        light.color = new Color(1f, 0.91f, 0.78f, 1f);
        light.intensity = 2.2f;
        light.range = 6f;
        light.areaSize = new Vector2(2.4f, 1.2f);
        light.shadows = LightShadows.Soft;
        EditorUtility.SetDirty(light);
    }

    private static void EnsureLightProbeGrid(Transform lightingRig)
    {
        Transform transform = EnsureChild(lightingRig, "Light Probe Grid");
        LightProbeGroup group = transform.GetComponent<LightProbeGroup>();
        if (group != null)
        {
            return;
        }

        group = Undo.AddComponent<LightProbeGroup>(transform.gameObject);

        const int xCount = 5;
        const int yCount = 3;
        const int zCount = 5;
        var positions = new Vector3[xCount * yCount * zCount];
        int index = 0;
        for (int y = 0; y < yCount; y++)
        {
            for (int z = 0; z < zCount; z++)
            {
                for (int x = 0; x < xCount; x++)
                {
                    positions[index++] = new Vector3(
                        -2.5f + x * 1.25f,
                        0.35f + y * 0.85f,
                        -1.5f + z * 1.2f
                    );
                }
            }
        }

        group.probePositions = positions;
        EditorUtility.SetDirty(group);
    }

    private static void EnsureReflectionProbe(Transform lightingRig)
    {
        Transform transform = EnsureChild(
            lightingRig,
            "Room Reflection Probe (Baked)"
        );
        ReflectionProbe probe = transform.GetComponent<ReflectionProbe>();
        if (probe != null)
        {
            return;
        }

        transform.localPosition = new Vector3(0f, 1.35f, 0.8f);

        probe = Undo.AddComponent<ReflectionProbe>(transform.gameObject);
        probe.mode = ReflectionProbeMode.Baked;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.resolution = 128;
        probe.size = new Vector3(6f, 3f, 6f);
        probe.center = Vector3.zero;
        probe.boxProjection = true;
        probe.blendDistance = 0.5f;
        probe.hdr = true;
        EditorUtility.SetDirty(probe);
    }

    private static void EnsureGlobalVolume(Transform lightingRig)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            VolumeProfilePath
        );
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SignVR Recording Post Process";
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0f);
            color.contrast.Override(4f);
            color.saturation.Override(-3f);
            color.colorFilter.Override(Color.white);
        }

        Transform transform = EnsureChild(lightingRig, "Global Volume");
        Volume volume = GetOrAdd<Volume>(transform.gameObject);
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.weight = 1f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
        EditorUtility.SetDirty(profile);
    }

    private static void EnsureEnvironmentMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            throw new InvalidOperationException("URP Lit shader is unavailable.");
        }

        EnsureMaterial(shader, "M_SignVR_Wall", new Color(0.72f, 0.73f, 0.71f), 0f, 0.18f);
        EnsureMaterial(shader, "M_SignVR_Ceiling", new Color(0.82f, 0.82f, 0.79f), 0f, 0.1f);
        EnsureMaterial(shader, "M_SignVR_Floor", new Color(0.28f, 0.23f, 0.18f), 0f, 0.28f);
        EnsureMaterial(shader, "M_SignVR_Plastic", new Color(0.18f, 0.19f, 0.2f), 0f, 0.3f);
        EnsureMaterial(shader, "M_SignVR_Metal", new Color(0.34f, 0.35f, 0.36f), 0.75f, 0.46f);
        EnsureMaterial(shader, "M_SignVR_ScreenOff", new Color(0.025f, 0.03f, 0.035f), 0f, 0.58f);
    }

    private static void EnsureMaterial(
        Shader shader,
        string name,
        Color baseColor,
        float metallic,
        float smoothness)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
        {
            return;
        }

        var material = new Material(shader)
        {
            name = name,
            enableInstancing = true
        };
        material.SetColor("_BaseColor", baseColor);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
    }

    private static void DisableLegacyDirectionalLights(Transform environment)
    {
        string[] names =
        {
            "Directional Light",
            "Directional Light Mirrored",
            "Directional Light Mirrored Rim"
        };

        foreach (string name in names)
        {
            Light light = environment.Find(name)?.GetComponent<Light>();
            if (light != null)
            {
                light.enabled = false;
                EditorUtility.SetDirty(light);
            }
        }
    }

    private static void DisableLegacyLightProbe(Transform environment)
    {
        Transform legacyProbe = environment.Find("LightProbe");
        if (legacyProbe != null)
        {
            legacyProbe.gameObject.SetActive(false);
            EditorUtility.SetDirty(legacyProbe.gameObject);
        }
    }

    private static void ConfigureExistingLocalLights(Transform environment)
    {
        Light spot = environment.Find("Spot Light")?.GetComponent<Light>();
        if (spot != null)
        {
            spot.lightmapBakeType = LightmapBakeType.Mixed;
            spot.shadows = LightShadows.None;
            EditorUtility.SetDirty(spot);
        }

        Light downLight = environment.Find("DownLight/Point Light")
            ?.GetComponent<Light>();
        if (downLight != null)
        {
            downLight.lightmapBakeType = LightmapBakeType.Mixed;
            downLight.shadows = LightShadows.None;
            EditorUtility.SetDirty(downLight);
        }
    }

    private static void EnablePostProcessingOnHmdCamera()
    {
        Transform centerEye = RequireTransform(
            "[BuildingBlock] Camera Rig/TrackingSpace/CenterEyeAnchor"
        );
        Camera camera = centerEye.GetComponent<Camera>();
        UniversalAdditionalCameraData cameraData =
            camera.GetComponent<UniversalAdditionalCameraData>() ??
            Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
        cameraData.renderPostProcessing = true;
        cameraData.renderShadows = true;
        EditorUtility.SetDirty(cameraData);
    }

    private static void ConfigureAmbientLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.44f, 0.49f, 0.55f);
        RenderSettings.ambientEquatorColor = new Color(0.32f, 0.34f, 0.35f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.14f, 0.12f);
        RenderSettings.ambientIntensity = 0.9f;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.fog = false;
    }

    private static Transform EnsureChild(Transform parent, string name)
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

    private static Transform RequireTransform(string path)
    {
        GameObject gameObject = GameObject.Find(path);
        if (gameObject == null)
        {
            throw new InvalidOperationException($"Scene object not found: {path}");
        }
        return gameObject.transform;
    }

    private static void RequireRecordingScene()
    {
        if (EditorSceneManager.GetActiveScene().path != RecordingScenePath)
        {
            throw new InvalidOperationException(
                $"Open {RecordingScenePath} before running this command."
            );
        }

        EnsureAssetFolder("Assets/Settings");
        EnsureAssetFolder(MaterialFolder);
    }

    private static void EnsureAssetFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = $"{current}/{parts[index]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[index]);
            }
            current = next;
        }
    }
}
