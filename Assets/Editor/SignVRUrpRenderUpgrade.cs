using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SignVRUrpRenderUpgrade
{
    private const string RecordingScenePath = "Assets/Scenes/Recording.unity";
    private const string ImportedEnvironmentPath =
        "Environment/ImportedEnvironment";
    private const string LightingRigPath = "Environment/SignVR Lighting";
    private const string OfficeAssetPrefix = "Assets/UnityJapanOffice/";
    private const string LightingSettingsPath =
        "Assets/Settings/SignVRRecordingLightingSettings.lighting";
    private const string VolumeProfilePath =
        "Assets/Settings/SignVRRecordingPostProcess.asset";
    private const string RendererDataPath = "Assets/Settings/PC_Renderer.asset";

    [MenuItem("SignVR/Rendering/Repair Recording Scene for URP")]
    public static void RepairRecordingSceneForUrp()
    {
        RequireRecordingScene();

        SignVRLightingSetup.PrepareRecordingScene();

        Transform importedEnvironment = RequireTransform(ImportedEnvironmentPath);
        Transform lightingRig = RequireTransform(LightingRigPath);
        Bounds environmentBounds = CalculateRendererBounds(importedEnvironment);
        Vector3 captureCenter = CalculateCaptureCenter(environmentBounds);

        MaterialRepairResult materialResult = RepairUsedOfficeMaterials(
            importedEnvironment
        );
        ConfigureQuestUrpQuality();
        ConfigureAdaptiveLighting(
            importedEnvironment,
            lightingRig,
            environmentBounds,
            captureCenter
        );
        ConfigureLightingSettings();
        ConfigureVolume();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[SignVRUrpRenderUpgrade] Repair complete. " +
            $"Renderers={materialResult.rendererCount}, " +
            $"officeMaterials={materialResult.officeMaterialCount}, " +
            $"repaired={materialResult.repairedCount}, " +
            $"alreadyCompatible={materialResult.compatibleCount}, " +
            $"probeBounds={FormatVector(environmentBounds.size)}. " +
            "Run SignVR/Rendering/Bake Repaired Recording Scene next."
        );
    }

    [MenuItem("SignVR/Rendering/Bake Repaired Recording Scene")]
    public static void BakeRepairedRecordingScene()
    {
        RepairRecordingSceneForUrp();
        SignVRLightingSetup.MarkImportedEnvironmentStatic();

        if (Lightmapping.isRunning)
        {
            throw new InvalidOperationException(
                "A lighting bake is already running."
            );
        }

        Lightmapping.Clear();
        Lightmapping.BakeAsync();
        Debug.Log(
            "[SignVRUrpRenderUpgrade] Progressive lightmap bake started."
        );
    }

    private static MaterialRepairResult RepairUsedOfficeMaterials(
        Transform importedEnvironment)
    {
        Renderer[] renderers = importedEnvironment.GetComponentsInChildren<Renderer>(
            true
        );
        Material[] officeMaterials = renderers
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Distinct()
            .Where(IsOfficeMaterial)
            .ToArray();

        int repaired = 0;
        int compatible = 0;
        foreach (Material material in officeMaterials)
        {
            if (NeedsUrpRepair(material))
            {
                RepairMaterial(material);
                repaired++;
            }
            else
            {
                compatible++;
            }
        }

        return new MaterialRepairResult(
            renderers.Length,
            officeMaterials.Length,
            repaired,
            compatible
        );
    }

    private static bool IsOfficeMaterial(Material material)
    {
        string path = AssetDatabase.GetAssetPath(material);
        return path.StartsWith(OfficeAssetPrefix, StringComparison.Ordinal);
    }

    private static bool NeedsUrpRepair(Material material)
    {
        if (material.shader == null || !material.shader.isSupported)
        {
            return true;
        }

        string shaderName = material.shader.name;
        if (
            shaderName.Contains("HDRP", StringComparison.OrdinalIgnoreCase) ||
            shaderName.Contains("High Definition", StringComparison.OrdinalIgnoreCase) ||
            shaderName.Contains("LayeredLit", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        string shaderPath = AssetDatabase.GetAssetPath(material.shader);
        if (
            shaderPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) &&
            shaderPath.StartsWith(OfficeAssetPrefix, StringComparison.Ordinal) &&
            File.ReadAllText(shaderPath).Contains(
                "HDPipeline",
                StringComparison.Ordinal
            )
        )
        {
            return true;
        }

        SavedMaterialProperties saved = ReadSavedProperties(material);
        return shaderName == "Universal Render Pipeline/Lit" &&
            (
                saved.HasTexture("_BaseColorMap") ||
                saved.HasTexture("_MaskMap") ||
                saved.HasTexture("_NormalMap")
            );
    }

    private static void RepairMaterial(Material material)
    {
        SavedMaterialProperties saved = ReadSavedProperties(material);
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            throw new InvalidOperationException("URP Lit shader is unavailable.");
        }

        Undo.RecordObject(material, $"Repair {material.name} for URP");
        material.shader = urpLit;

        SetTexture(
            material,
            "_BaseMap",
            saved.FirstTexture("_BaseColorMap", "_MainTex", "_BaseMap")
        );
        SetTexture(
            material,
            "_BumpMap",
            saved.FirstTexture("_NormalMap", "_BumpMap")
        );

        SavedTexture mask = saved.FirstTexture(
            "_MaskMap",
            "_MetallicGlossMap"
        );
        SetTexture(material, "_MetallicGlossMap", mask);
        SetTexture(
            material,
            "_OcclusionMap",
            saved.FirstTexture("_MaskMap", "_OcclusionMap")
        );
        SetTexture(
            material,
            "_EmissionMap",
            saved.FirstTexture("_EmissiveColorMap", "_EmissionMap")
        );

        Color baseColor = saved.FirstColor(
            Color.white,
            "_BaseColor",
            "_Color"
        );
        material.SetColor("_BaseColor", baseColor);
        material.SetFloat(
            "_Metallic",
            saved.FirstFloat(0f, "_MetallicScale", "_Metallic")
        );
        material.SetFloat(
            "_Smoothness",
            saved.FirstFloat(
                mask.texture != null ? 1f : 0.42f,
                "_SmoothnessRemapMax",
                "_Smoothness"
            )
        );
        material.SetFloat(
            "_BumpScale",
            saved.FirstFloat(1f, "_NormalScale", "_BumpScale")
        );
        material.SetFloat(
            "_OcclusionStrength",
            saved.FirstFloat(
                1f,
                "_AORemapMax",
                "_AOScale_1",
                "_OcclusionStrength"
            )
        );

        Color emission = saved.FirstColor(
            Color.black,
            "_EmissiveColor",
            "_EmissionColor"
        );
        material.SetColor("_EmissionColor", emission);

        bool transparent = saved.FirstFloat(
            0f,
            "_SurfaceType",
            "_Surface"
        ) > 0.5f;
        bool alphaClip = saved.FirstFloat(
            0f,
            "_AlphaCutoffEnable",
            "_AlphaClip"
        ) > 0.5f;
        material.SetFloat("_Surface", transparent ? 1f : 0f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        material.SetFloat(
            "_Cutoff",
            saved.FirstFloat(0.5f, "_AlphaCutoff", "_Cutoff")
        );
        material.SetFloat(
            "_Cull",
            saved.FirstFloat(0f, "_DoubleSidedEnable") > 0.5f ? 0f : 2f
        );
        material.SetFloat("_WorkflowMode", 1f);

        SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap") != null);
        SetKeyword(
            material,
            "_METALLICSPECGLOSSMAP",
            material.GetTexture("_MetallicGlossMap") != null
        );
        SetKeyword(
            material,
            "_OCCLUSIONMAP",
            material.GetTexture("_OcclusionMap") != null
        );
        SetKeyword(
            material,
            "_EMISSION",
            material.GetTexture("_EmissionMap") != null ||
            emission.maxColorComponent > 0.001f
        );

        BaseShaderGUI.SetupMaterialBlendMode(material);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    private static SavedMaterialProperties ReadSavedProperties(Material material)
    {
        var result = new SavedMaterialProperties();
        var serialized = new SerializedObject(material);

        SerializedProperty textures = serialized.FindProperty(
            "m_SavedProperties.m_TexEnvs"
        );
        for (int index = 0; index < textures.arraySize; index++)
        {
            SerializedProperty pair = textures.GetArrayElementAtIndex(index);
            string key = pair.FindPropertyRelative("first").stringValue;
            SerializedProperty value = pair.FindPropertyRelative("second");
            result.textures[key] = new SavedTexture(
                value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture,
                value.FindPropertyRelative("m_Scale").vector2Value,
                value.FindPropertyRelative("m_Offset").vector2Value
            );
        }

        SerializedProperty floats = serialized.FindProperty(
            "m_SavedProperties.m_Floats"
        );
        for (int index = 0; index < floats.arraySize; index++)
        {
            SerializedProperty pair = floats.GetArrayElementAtIndex(index);
            result.floats[pair.FindPropertyRelative("first").stringValue] =
                pair.FindPropertyRelative("second").floatValue;
        }

        SerializedProperty colors = serialized.FindProperty(
            "m_SavedProperties.m_Colors"
        );
        for (int index = 0; index < colors.arraySize; index++)
        {
            SerializedProperty pair = colors.GetArrayElementAtIndex(index);
            result.colors[pair.FindPropertyRelative("first").stringValue] =
                pair.FindPropertyRelative("second").colorValue;
        }

        return result;
    }

    private static void SetTexture(
        Material material,
        string propertyName,
        SavedTexture savedTexture)
    {
        if (savedTexture.texture == null)
        {
            return;
        }

        material.SetTexture(propertyName, savedTexture.texture);
        material.SetTextureScale(propertyName, savedTexture.scale);
        material.SetTextureOffset(propertyName, savedTexture.offset);
    }

    private static void SetKeyword(
        Material material,
        string keyword,
        bool enabled)
    {
        if (enabled)
        {
            material.EnableKeyword(keyword);
        }
        else
        {
            material.DisableKeyword(keyword);
        }
    }

    private static void ConfigureQuestUrpQuality()
    {
        UniversalRenderPipelineAsset asset =
            GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null)
        {
            throw new InvalidOperationException(
                "The active default render pipeline is not URP."
            );
        }

        Undo.RecordObject(asset, "Configure SignVR Quest URP quality");
        asset.supportsCameraDepthTexture = false;
        asset.supportsCameraOpaqueTexture = false;
        asset.supportsHDR = false;
        asset.msaaSampleCount = 4;
        asset.mainLightShadowmapResolution = 2048;
        asset.shadowDistance = 15f;
        asset.shadowCascadeCount = 2;
        var serializedAsset = new SerializedObject(asset);
        serializedAsset.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        serializedAsset.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
        serializedAsset.FindProperty("m_AdditionalLightsRenderingMode").intValue =
            (int)LightRenderingMode.PerPixel;
        serializedAsset.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);

        UniversalRendererData rendererData =
            AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
        if (rendererData == null)
        {
            throw new InvalidOperationException(
                $"URP renderer data not found: {RendererDataPath}"
            );
        }

        Undo.RecordObject(rendererData, "Configure SignVR Quest renderer");
        rendererData.renderingMode = RenderingMode.Forward;
        foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
        {
            if (feature != null && feature.GetType().Name.Contains(
                "ScreenSpaceAmbientOcclusion",
                StringComparison.Ordinal
            ))
            {
                Undo.RecordObject(feature, "Disable SSAO for Quest");
                feature.SetActive(false);
                EditorUtility.SetDirty(feature);
            }
        }
        EditorUtility.SetDirty(rendererData);
    }

    private static void ConfigureAdaptiveLighting(
        Transform importedEnvironment,
        Transform lightingRig,
        Bounds environmentBounds,
        Vector3 captureCenter)
    {
        float floorY = environmentBounds.min.y;
        float ceilingY = Mathf.Min(
            environmentBounds.max.y,
            floorY + 3.1f
        );

        Light main = RequireTransform(
            $"{LightingRigPath}/Main Key (Mixed)"
        ).GetComponent<Light>();
        main.color = new Color(1f, 0.94f, 0.84f, 1f);
        main.intensity = 0.58f;
        main.bounceIntensity = 1f;
        main.shadows = LightShadows.Soft;
        main.shadowStrength = 0.62f;
        main.shadowBias = 0.04f;
        main.shadowNormalBias = 0.35f;
        main.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        EditorUtility.SetDirty(main);

        ConfigureCeilingLight(
            "Ceiling Softbox Left (Baked)",
            captureCenter + new Vector3(-1.35f, ceilingY - captureCenter.y - 0.18f, 0.55f),
            captureCenter
        );
        ConfigureCeilingLight(
            "Ceiling Softbox Right (Baked)",
            captureCenter + new Vector3(1.35f, ceilingY - captureCenter.y - 0.18f, 0.55f),
            captureCenter
        );

        ConfigureLocalLight(importedEnvironment.Find("Spot Light")?.GetComponent<Light>(), 1.5f);
        ConfigureLocalLight(importedEnvironment.Find("DownLight/Point Light")?.GetComponent<Light>(), 0.75f);
        ConfigureLightProbeGrid(lightingRig, environmentBounds, captureCenter, floorY);
        ConfigureReflectionProbe(lightingRig, environmentBounds, captureCenter, floorY);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.29f, 0.33f, 0.38f);
        RenderSettings.ambientEquatorColor = new Color(0.20f, 0.22f, 0.23f);
        RenderSettings.ambientGroundColor = new Color(0.10f, 0.085f, 0.07f);
        RenderSettings.ambientIntensity = 0.68f;
        RenderSettings.reflectionIntensity = 0.72f;
        RenderSettings.fog = false;
    }

    private static void ConfigureCeilingLight(
        string name,
        Vector3 worldPosition,
        Vector3 captureCenter)
    {
        Light light = RequireTransform($"{LightingRigPath}/{name}")
            .GetComponent<Light>();
        light.transform.position = worldPosition;
        light.transform.rotation = Quaternion.LookRotation(
            captureCenter - worldPosition,
            Vector3.forward
        );
        light.type = LightType.Rectangle;
        light.lightmapBakeType = LightmapBakeType.Baked;
        light.color = new Color(1f, 0.90f, 0.76f, 1f);
        light.intensity = 1.65f;
        light.range = 5.5f;
        light.areaSize = new Vector2(2.2f, 1.15f);
        light.shadows = LightShadows.Soft;
        EditorUtility.SetDirty(light);
    }

    private static void ConfigureLocalLight(Light light, float maxIntensity)
    {
        if (light == null)
        {
            return;
        }

        light.lightmapBakeType = LightmapBakeType.Baked;
        light.intensity = Mathf.Min(light.intensity, maxIntensity);
        light.shadows = LightShadows.Soft;
        EditorUtility.SetDirty(light);
    }

    private static void ConfigureLightProbeGrid(
        Transform lightingRig,
        Bounds environmentBounds,
        Vector3 captureCenter,
        float floorY)
    {
        LightProbeGroup group = RequireTransform(
            $"{LightingRigPath}/Light Probe Grid"
        ).GetComponent<LightProbeGroup>();

        float width = Mathf.Clamp(environmentBounds.size.x, 4f, 10f);
        float depth = Mathf.Clamp(environmentBounds.size.z, 4f, 8f);
        int xCount = Mathf.Clamp(Mathf.CeilToInt(width / 1.5f) + 1, 4, 8);
        int zCount = Mathf.Clamp(Mathf.CeilToInt(depth / 1.5f) + 1, 4, 7);
        float[] heights = { 0.35f, 1.25f, 2.15f };
        var worldPositions = new List<Vector3>(xCount * zCount * heights.Length);

        for (int y = 0; y < heights.Length; y++)
        {
            for (int z = 0; z < zCount; z++)
            {
                for (int x = 0; x < xCount; x++)
                {
                    worldPositions.Add(new Vector3(
                        captureCenter.x - width * 0.5f + width * x / (xCount - 1),
                        floorY + heights[y],
                        captureCenter.z - depth * 0.5f + depth * z / (zCount - 1)
                    ));
                }
            }
        }

        group.probePositions = worldPositions
            .Select(group.transform.InverseTransformPoint)
            .ToArray();
        EditorUtility.SetDirty(group);
    }

    private static void ConfigureReflectionProbe(
        Transform lightingRig,
        Bounds environmentBounds,
        Vector3 captureCenter,
        float floorY)
    {
        ReflectionProbe probe = RequireTransform(
            $"{LightingRigPath}/Room Reflection Probe (Baked)"
        ).GetComponent<ReflectionProbe>();
        float width = Mathf.Clamp(environmentBounds.size.x, 5f, 10f);
        float depth = Mathf.Clamp(environmentBounds.size.z, 5f, 8f);

        probe.transform.position = new Vector3(
            captureCenter.x,
            floorY + 1.45f,
            captureCenter.z
        );
        probe.mode = ReflectionProbeMode.Baked;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.resolution = 128;
        probe.size = new Vector3(width, 2.9f, depth);
        probe.center = Vector3.zero;
        probe.boxProjection = true;
        probe.blendDistance = 0.75f;
        probe.hdr = true;
        probe.importance = 1;
        EditorUtility.SetDirty(probe);
    }

    private static void ConfigureLightingSettings()
    {
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
            LightingSettingsPath
        );
        if (settings == null)
        {
            throw new InvalidOperationException(
                $"Lighting settings not found: {LightingSettingsPath}"
            );
        }

        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
        settings.lightmapResolution = 24f;
        settings.lightmapMaxSize = 1024;
        settings.lightmapPadding = 4;
        settings.indirectResolution = 2f;
        settings.maxBounces = 3;
        settings.minBounces = 1;
        settings.directSampleCount = 64;
        settings.indirectSampleCount = 256;
        settings.environmentSampleCount = 128;
        settings.ao = true;
        settings.aoMaxDistance = 1.1f;
        settings.aoExponentDirect = 0.8f;
        settings.aoExponentIndirect = 1.15f;
        Lightmapping.lightingSettings = settings;
        EditorUtility.SetDirty(settings);
    }

    private static void ConfigureVolume()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            VolumeProfilePath
        );
        if (profile == null)
        {
            throw new InvalidOperationException(
                $"Volume profile not found: {VolumeProfilePath}"
            );
        }

        if (!profile.TryGet(out Tonemapping tonemapping))
        {
            tonemapping = profile.Add<Tonemapping>(true);
        }
        tonemapping.mode.Override(TonemappingMode.Neutral);

        if (!profile.TryGet(out ColorAdjustments color))
        {
            color = profile.Add<ColorAdjustments>(true);
        }
        color.postExposure.Override(-0.12f);
        color.contrast.Override(7f);
        color.saturation.Override(-2f);
        color.colorFilter.Override(new Color(1f, 0.99f, 0.97f, 1f));
        EditorUtility.SetDirty(profile);
    }

    private static Bounds CalculateRendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException(
                $"No renderers found under {ImportedEnvironmentPath}."
            );
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }

    private static Vector3 CalculateCaptureCenter(Bounds environmentBounds)
    {
        GameObject mirroredObjects = GameObject.Find("MirroredObjects");
        Renderer[] renderers = mirroredObjects != null
            ? mirroredObjects.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
        if (renderers.Length == 0)
        {
            return new Vector3(
                environmentBounds.center.x,
                environmentBounds.min.y + 1.15f,
                environmentBounds.center.z
            );
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds.center;
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
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F1}, {value.y:F1}, {value.z:F1})";
    }

    private readonly struct MaterialRepairResult
    {
        public readonly int rendererCount;
        public readonly int officeMaterialCount;
        public readonly int repairedCount;
        public readonly int compatibleCount;

        public MaterialRepairResult(
            int rendererCount,
            int officeMaterialCount,
            int repairedCount,
            int compatibleCount)
        {
            this.rendererCount = rendererCount;
            this.officeMaterialCount = officeMaterialCount;
            this.repairedCount = repairedCount;
            this.compatibleCount = compatibleCount;
        }
    }

    private readonly struct SavedTexture
    {
        public readonly Texture texture;
        public readonly Vector2 scale;
        public readonly Vector2 offset;

        public SavedTexture(Texture texture, Vector2 scale, Vector2 offset)
        {
            this.texture = texture;
            this.scale = scale;
            this.offset = offset;
        }
    }

    private sealed class SavedMaterialProperties
    {
        public readonly Dictionary<string, SavedTexture> textures = new();
        public readonly Dictionary<string, float> floats = new();
        public readonly Dictionary<string, Color> colors = new();

        public bool HasTexture(string name)
        {
            return textures.TryGetValue(name, out SavedTexture value) &&
                value.texture != null;
        }

        public SavedTexture FirstTexture(params string[] names)
        {
            foreach (string name in names)
            {
                if (
                    textures.TryGetValue(name, out SavedTexture value) &&
                    value.texture != null
                )
                {
                    return value;
                }
            }
            return default;
        }

        public float FirstFloat(float fallback, params string[] names)
        {
            foreach (string name in names)
            {
                if (floats.TryGetValue(name, out float value))
                {
                    return value;
                }
            }
            return fallback;
        }

        public Color FirstColor(Color fallback, params string[] names)
        {
            foreach (string name in names)
            {
                if (colors.TryGetValue(name, out Color value))
                {
                    return value;
                }
            }
            return fallback;
        }
    }
}
