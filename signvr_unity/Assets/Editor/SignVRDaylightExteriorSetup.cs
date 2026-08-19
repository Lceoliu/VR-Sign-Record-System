using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class SignVRDaylightExteriorSetup
{
    private const string RecordingScenePath = "Assets/Scenes/Recording.unity";
    private const string ImportedEnvironmentPath = "Environment/ImportedEnvironment";
    private const string ExteriorShaderPath = "Assets/Shaders/SignVRExteriorCubemap.shader";
    private const string MetaSkyboxSourcePath =
        "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Materials/SkyboxGradient.mat";
    private const string SkyboxMaterialPath =
        "Assets/Materials/M_SignVR_DaylightSkybox.mat";
    private const string WorkspaceMaterialPath =
        "Assets/Materials/M_SignVR_Outside_WorkSpace.mat";
    private const string EntranceMaterialPath =
        "Assets/Materials/M_SignVR_Outside_Entrance.mat";
    private const string WorkspaceCubemapPath =
        "Assets/UnityJapanOffice/Textures/Office/Outside_WorkSpace.exr";
    private const string EntranceCubemapPath =
        "Assets/UnityJapanOffice/Textures/Office/Outside_Entrance.exr";

    [MenuItem("SignVR/Rendering/Install Daylight Sky and Exteriors")]
    public static void InstallDaylightSkyAndExteriors()
    {
        RequireRecordingScene();

        Material skybox = CopyAndConfigureSkybox();
        Shader exteriorShader = RequireAsset<Shader>(ExteriorShaderPath);
        Material workspaceMaterial = ConfigureExteriorMaterial(
            WorkspaceMaterialPath,
            exteriorShader,
            RequireAsset<Cubemap>(WorkspaceCubemapPath),
            0.52f
        );
        Material entranceMaterial = ConfigureExteriorMaterial(
            EntranceMaterialPath,
            exteriorShader,
            RequireAsset<Cubemap>(EntranceCubemapPath),
            0.48f
        );

        Transform environment = GameObject.Find(ImportedEnvironmentPath)?.transform;
        if (environment == null)
        {
            throw new InvalidOperationException(
                $"Recording environment not found: {ImportedEnvironmentPath}"
            );
        }

        int workspaceRenderers = AssignExteriorMaterial(
            environment,
            "Outside_WorkSpace",
            workspaceMaterial
        );
        int entranceRenderers = AssignExteriorMaterial(
            environment,
            "Outside_Entrance",
            entranceMaterial
        );

        RenderSettings.skybox = skybox;
        DynamicGI.UpdateEnvironment();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[SignVRDaylightExteriorSetup] Daylight sky and lightweight exteriors installed. " +
            $"WorkspaceRenderers={workspaceRenderers}, EntranceRenderers={entranceRenderers}. " +
            "Existing Trilight ambient settings were preserved."
        );
    }

    [MenuItem("SignVR/Rendering/Bake Reflection Probes Only")]
    public static void BakeReflectionProbesOnly()
    {
        RequireRecordingScene();
        if (Lightmapping.isRunning)
        {
            throw new InvalidOperationException("A lighting bake is already running.");
        }

        ReflectionProbe[] probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        ).Where(probe => probe.mode == ReflectionProbeMode.Baked).ToArray();
        if (probes.Length == 0)
        {
            throw new InvalidOperationException("No baked Reflection Probe exists in Recording.");
        }

        MethodInfo bakeMethod = typeof(Lightmapping).GetMethod(
            "BakeAllReflectionProbesSnapshots",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        if (bakeMethod == null)
        {
            throw new MissingMethodException(
                "UnityEditor.Lightmapping.BakeAllReflectionProbesSnapshots"
            );
        }

        bool success = (bool)bakeMethod.Invoke(null, null);
        if (!success)
        {
            throw new InvalidOperationException("Unity could not bake Reflection Probes.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log(
            $"[SignVRDaylightExteriorSetup] Baked {probes.Length} Reflection Probe(s) " +
            "without clearing or rebaking Lightmaps."
        );
    }

    private static Material CopyAndConfigureSkybox()
    {
        Material source = RequireAsset<Material>(MetaSkyboxSourcePath);
        Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
        if (skybox == null)
        {
            if (!AssetDatabase.CopyAsset(MetaSkyboxSourcePath, SkyboxMaterialPath))
            {
                throw new InvalidOperationException(
                    $"Could not copy Meta skybox to {SkyboxMaterialPath}"
                );
            }
            skybox = RequireAsset<Material>(SkyboxMaterialPath);
        }

        skybox.shader = source.shader;
        skybox.SetColor("_TopColor", new Color(0.39f, 0.48f, 0.57f, 1f));
        skybox.SetColor("_MiddleColor", new Color(0.69f, 0.72f, 0.73f, 1f));
        skybox.SetColor("_BottomColor", new Color(0.50f, 0.48f, 0.44f, 1f));
        skybox.SetFloat("_DitherStrength", 16f);
        EditorUtility.SetDirty(skybox);
        return skybox;
    }

    private static Material ConfigureExteriorMaterial(
        string path,
        Shader shader,
        Cubemap cubemap,
        float emissionStrength)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path)
            };
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetTexture("_Cubemap", cubemap);
        material.SetColor("_Tint", new Color(0.82f, 0.84f, 0.84f, 1f));
        material.SetFloat("_EmissionStrength", emissionStrength);
        material.SetFloat("_Rotation", 0f);
        material.enableInstancing = true;
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static int AssignExteriorMaterial(
        Transform environment,
        string objectName,
        Material material)
    {
        Renderer[] renderers = environment
            .GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.gameObject.name == objectName)
            .ToArray();
        foreach (Renderer renderer in renderers)
        {
            Undo.RecordObject(renderer, "Assign SignVR exterior material");
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = material;
            }
            renderer.sharedMaterials = slots;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            GameObjectUtility.SetStaticEditorFlags(
                renderer.gameObject,
                flags & ~StaticEditorFlags.ContributeGI
            );
            EditorUtility.SetDirty(renderer);
        }
        return renderers.Length;
    }

    private static void RequireRecordingScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != RecordingScenePath)
        {
            throw new InvalidOperationException(
                $"Open {RecordingScenePath} before running this command. " +
                $"Current scene: {activeScene.path}"
            );
        }
    }

    private static T RequireAsset<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            throw new InvalidOperationException($"Required asset not found: {path}");
        }
        return asset;
    }
}
