using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SignVR.RealtimeTranslationDemo.EditorTools
{
    public static class RealtimeTranslationDemoSceneBuilder
    {
        private const string RootFolder = "Assets/RealtimeTranslationDemo";
        private const string SceneFolder = RootFolder + "/Scenes";
        private const string ScenePath = SceneFolder + "/RealtimeTranslationDemo.unity";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string SettingsFolder = RootFolder + "/Settings";
        private const string PreviewFolder = RootFolder + "/Preview";
        private const string VolumeProfilePath = SettingsFolder + "/RealtimeTranslationDemoVolume.asset";
        private const string LightingSettingsPath = SettingsFolder + "/RealtimeTranslationDemoLighting.lighting";
        private const string PcRenderPipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string PcRendererDataPath = "Assets/Settings/PC_Renderer.asset";
        private const string PolyHavenRoot =
            RootFolder + "/ThirdParty/PolyHaven";

        private const string SkyboxPath = "Assets/Materials/M_SignVR_DaylightSkybox.mat";
        private const string ExteriorShaderPath = "Assets/Shaders/SignVRExteriorCubemap.shader";
        private const string ExteriorCubemapPath =
            "Assets/UnityJapanOffice/Textures/Office/Outside_WorkSpace.exr";

        private const string ChairPrefabPath =
            "Assets/UnityJapanOffice/Prefabs/Furnitures/Chair_04.prefab";
        private const string SofaPrefabPath =
            "Assets/UnityJapanOffice/Prefabs/Furnitures/Sofa.prefab";
        private const string TablePrefabPath =
            "Assets/UnityJapanOffice/Prefabs/Furnitures/Table_03.prefab";
        private const string PlantPrefabPath =
            "Assets/UnityJapanOffice/Prefabs/Props/MossBolls.prefab";
        private const string PottedPlantModelPath =
            PolyHavenRoot + "/Models/potted_plant_02/potted_plant_02_4k.fbx";
        private const string AnriDemoPrefabPath =
            RootFolder + "/Prefabs/Characters/ANRI_DemoCharacter.prefab";

        private static readonly Color WarmStone = new Color(0.56f, 0.51f, 0.44f, 1f);
        private static readonly Color SoftIvory = new Color(0.78f, 0.74f, 0.66f, 1f);
        private static readonly Color Walnut = new Color(0.44f, 0.27f, 0.15f, 1f);
        private static readonly Color MutedTeal = new Color(0.16f, 0.43f, 0.44f, 1f);
        private static readonly Color MutedAmber = new Color(0.92f, 0.55f, 0.20f, 1f);
        private static readonly Color DarkMetal = new Color(0.075f, 0.085f, 0.09f, 1f);
        private static readonly Color WarmSkin = new Color(0.58f, 0.34f, 0.23f, 1f);

        private sealed class DemoMaterials
        {
            public Material Floor;
            public Material Stone;
            public Material Plaster;
            public Material Walnut;
            public Material FabricIvory;
            public Material FabricTeal;
            public Material Carpet;
            public Material DarkMetal;
            public Material Glass;
            public Material WarmLight;
            public Material TealLight;
            public Material AmberLight;
            public Material TealGlow;
            public Material AmberGlow;
            public Material Foliage;
            public Material City;
            public Material Skin;
            public Material Hair;
            public Material Exterior;
            public Material PlantLeaves;
            public Material PlantPot;
            public Material PlantSoil;
            public Material PcSkybox;
        }

        [MenuItem("SignVR/Demo/Build Realtime Translation Scene")]
        public static void BuildRealtimeTranslationScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before building the demo scene.");
            }

            Scene current = SceneManager.GetActiveScene();
            if (current.isDirty)
            {
                throw new InvalidOperationException(
                    $"The current scene has unsaved changes: {current.path}. Save or discard them first."
                );
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                throw new InvalidOperationException(
                    $"The generated scene already exists at {ScenePath}. " +
                    "Delete it explicitly before rebuilding so later hand-edits cannot be overwritten."
                );
            }

            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder(PreviewFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "RealtimeTranslationDemo";

            DemoMaterials materials = CreateMaterials();
            Transform root = CreateEmpty("_RealtimeTranslationDemo", null);
            BuildEnvironment(root, materials);
            BuildFurniture(root, materials);
            BuildCharacters(root, materials);
            BuildLighting(root, materials);
            Camera heroCamera = BuildCameras(root);
            ConfigureRenderSettings(materials);
            ConfigureLightingSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Could not save demo scene: {ScenePath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = heroCamera.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();

            Debug.Log(
                "[RealtimeTranslationDemo] Scene created. The Recording scene and recording " +
                "runtime were not modified. ANRI is installed as the left-side signer; replace " +
                "the remaining hearing mannequin without changing the lighting anchors."
            );
        }

        [MenuItem("SignVR/Demo/Bake Realtime Translation Lighting")]
        public static void BakeRealtimeTranslationLighting()
        {
            RequireDemoScene();
            if (Lightmapping.isRunning)
            {
                throw new InvalidOperationException("A lighting bake is already running.");
            }

            ConfigureLightingSettings();
            RequireGpuLightmapper();
            SetReflectionProbesActive(true);
            Lightmapping.Clear();
            Lightmapping.BakeAsync();
            Debug.Log(
                "[RealtimeTranslationDemo] Progressive GPU lighting bake started. " +
                "The scene remains usable while Unity bakes."
            );
        }

        [MenuItem("SignVR/Demo/Configure GPU Lightmapper")]
        public static void ConfigureRealtimeTranslationGpuLightmapper()
        {
            RequireDemoScene();
            ConfigureLightingSettings();
            RequireGpuLightmapper();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[RealtimeTranslationDemo] Progressive GPU Lightmapper is configured. " +
                "No bake was started."
            );
        }

        [MenuItem("SignVR/Demo/Bake Realtime Translation Reflection Probes")]
        public static void BakeRealtimeTranslationReflectionProbes()
        {
            RequireDemoScene();
            if (Lightmapping.isRunning)
            {
                throw new InvalidOperationException("A lighting bake is already running.");
            }

            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(
                FindObjectsInactive.Include
            ).Where(probe => probe.mode == ReflectionProbeMode.Baked).ToArray();
            if (probes.Length == 0)
            {
                throw new InvalidOperationException("The demo scene has no baked Reflection Probe.");
            }

            foreach (ReflectionProbe probe in probes)
            {
                probe.gameObject.SetActive(true);
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
                throw new InvalidOperationException("Unity could not bake the demo Reflection Probes.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[RealtimeTranslationDemo] Baked {probes.Length} Reflection Probe(s).");
        }

        [MenuItem("SignVR/Demo/Apply Hero Lighting Polish")]
        public static void ApplyHeroLightingPolish()
        {
            RequireDemoScene();

            Material exterior = RequireAsset<Material>(
                MaterialFolder + "/M_Demo_DaylightExterior.mat"
            );
            exterior.SetFloat("_EmissionStrength", 0.92f);
            exterior.SetColor("_Tint", new Color(0.86f, 0.89f, 0.87f, 1f));

            Material foliage = RequireAsset<Material>(
                MaterialFolder + "/M_Demo_Foliage.mat"
            );
            foliage.SetColor("_BaseColor", new Color(0.32f, 0.43f, 0.28f, 1f));

            Material city = RequireAsset<Material>(
                MaterialFolder + "/M_Demo_CitySilhouette.mat"
            );
            city.SetColor("_BaseColor", new Color(0.38f, 0.43f, 0.45f, 1f));

            Material carpet = RequireAsset<Material>(
                MaterialFolder + "/M_Demo_Carpet.mat"
            );
            carpet.SetColor("_BaseColor", new Color(0.43f, 0.39f, 0.34f, 1f));

            Transform skyline = RequireSceneTransform(
                "_RealtimeTranslationDemo/Environment/Exterior/Distant Skyline"
            );
            skyline.gameObject.SetActive(false);
            RequireSceneTransform(
                "_RealtimeTranslationDemo/Environment/Exterior/Daylight Exterior Dome"
            ).gameObject.SetActive(false);
            SetReflectionProbesActive(true);

            SetLightIntensity("Late Morning Sun", 1.55f);
            SetLightIntensity("Window Key Fill", 4.2f);
            SetLightIntensity("Soft Interior Fill", 2.4f);
            SetLightIntensity("Signer Teal Rim", 0.90f);
            SetLightIntensity("Speaker Amber Rim", 0.82f);

            Transform lighting = RequireSceneTransform("_RealtimeTranslationDemo/Lighting");
            Light frontFill = lighting.Find("Hero Front Fill")?.GetComponent<Light>();
            if (frontFill == null)
            {
                frontFill = CreateSpotLight(
                    "Hero Front Fill",
                    lighting,
                    new Vector3(0f, 2.30f, -4.15f),
                    new Vector3(0f, 1.34f, 0.15f),
                    new Color(1f, 0.79f, 0.64f, 1f),
                    1.75f,
                    9.5f,
                    76f
                );
                frontFill.lightmapBakeType = LightmapBakeType.Mixed;
            }

            Light bounce = lighting.Find("Conversation Bounce")?.GetComponent<Light>();
            if (bounce == null)
            {
                bounce = CreatePointLight(
                    "Conversation Bounce",
                    lighting,
                    new Vector3(0f, 2.70f, 0.20f),
                    new Color(1f, 0.76f, 0.58f, 1f),
                    1.18f,
                    5.8f
                );
                bounce.lightmapBakeType = LightmapBakeType.Mixed;
            }

            Camera heroCamera = RequireSceneTransform(
                "_RealtimeTranslationDemo/Cameras/Hero Camera"
            ).GetComponent<Camera>();
            heroCamera.transform.position = new Vector3(0f, 1.67f, -5.80f);
            heroCamera.transform.rotation = LookRotation(
                heroCamera.transform.position,
                new Vector3(0f, 1.43f, 0.38f)
            );
            heroCamera.focalLength = 38f;
            ConfigureHeroCameraForPc(heroCamera);

            VolumeProfile profile = ConfigureCinematicVolumeProfile();
            ColorAdjustments color = GetOrAddOverride<ColorAdjustments>(profile);
            color.postExposure.Override(0.28f);
            color.contrast.Override(8f);
            color.saturation.Override(-4f);
            Bloom bloom = GetOrAddOverride<Bloom>(profile);
            bloom.intensity.Override(0.35f);

            ConfigureRenderSettings(null);
            DynamicGI.UpdateEnvironment();
            EditorUtility.SetDirty(exterior);
            EditorUtility.SetDirty(foliage);
            EditorUtility.SetDirty(city);
            EditorUtility.SetDirty(carpet);
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(heroCamera);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = heroCamera.gameObject;

            Debug.Log(
                "[RealtimeTranslationDemo] Hero lighting polish applied after rendered-frame review."
            );
        }

        [MenuItem("SignVR/Demo/Apply Poly Haven PC Asset Pass")]
        public static void ApplyPolyHavenPcAssetPass()
        {
            RequireDemoScene();

            DemoMaterials materials = CreateMaterials();
            SetRendererMaterial(
                "_RealtimeTranslationDemo/Environment/Architecture/Floor Base",
                materials.Floor
            );
            SetRendererMaterial(
                "_RealtimeTranslationDemo/Environment/Architecture/Conversation Platform",
                materials.Walnut
            );
            SetRendererMaterial(
                "_RealtimeTranslationDemo/Environment/Architecture/Conversation Rug",
                materials.Carpet
            );
            SetRendererMaterial(
                "_RealtimeTranslationDemo/Environment/Architecture/Ceiling Disc",
                materials.Plaster
            );

            RequireSceneTransform(
                "_RealtimeTranslationDemo/Environment/Exterior/Foreground Greenery"
            ).gameObject.SetActive(false);
            RequireSceneTransform(
                "_RealtimeTranslationDemo/Environment/Exterior/Distant Skyline"
            ).gameObject.SetActive(false);
            RequireSceneTransform(
                "_RealtimeTranslationDemo/Environment/Exterior/Daylight Exterior Dome"
            ).gameObject.SetActive(false);

            Transform furniture = RequireSceneTransform("_RealtimeTranslationDemo/Furniture");
            Transform details = furniture.Find("PC Asset Details");
            if (details == null)
            {
                details = CreateEmpty("PC Asset Details", furniture);
            }

            EnsurePottedPlant(
                details,
                "Left Window Potted Plant",
                new Vector3(-3.72f, 0.115f, 2.08f),
                22f,
                1.46f,
                materials
            );
            EnsurePottedPlant(
                details,
                "Right Window Potted Plant",
                new Vector3(3.68f, 0.115f, 2.18f),
                -28f,
                1.34f,
                materials
            );

            ConfigurePcRenderPipeline();
            ConfigureCinematicVolumeProfile();
            ConfigureRenderSettings(materials);
            SetReflectionProbesActive(true);

            Camera heroCamera = RequireSceneTransform(
                "_RealtimeTranslationDemo/Cameras/Hero Camera"
            ).GetComponent<Camera>();
            ConfigureHeroCameraForPc(heroCamera);

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = heroCamera.gameObject;

            Debug.Log(
                "[RealtimeTranslationDemo] Poly Haven PC asset pass applied: PBR surfaces, " +
                "HDR city exterior and high-detail plants. Recording scene and Mobile_RPAsset " +
                "were not modified."
            );
        }

        [MenuItem("SignVR/Demo/Configure PC Cinematic Rendering")]
        public static void ConfigurePcCinematicRendering()
        {
            RequireDemoScene();
            ConfigurePcRenderPipeline();
            ConfigureCinematicVolumeProfile();
            SetReflectionProbesActive(true);

            Camera heroCamera = RequireSceneTransform(
                "_RealtimeTranslationDemo/Cameras/Hero Camera"
            ).GetComponent<Camera>();
            ConfigureHeroCameraForPc(heroCamera);

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[RealtimeTranslationDemo] PC cinematic rendering configured: HDR, TAA, " +
                "depth/opaque textures, SSAO, local reflections, ACES grading and subtle DOF. " +
                "Mobile_RPAsset and the Recording scene were not modified."
            );
        }

        private static DemoMaterials CreateMaterials()
        {
            Shader lit = RequireShader("Universal Render Pipeline/Lit");
            Shader unlit = RequireShader("Universal Render Pipeline/Unlit");

            Texture2D paintAlbedo = LoadTexture(
                "Assets/UnityJapanOffice/Textures/Office/Paint01_Albedo.tif"
            );
            Texture2D paintNormal = LoadTexture(
                "Assets/UnityJapanOffice/Textures/Office/Paint01_Normal.tif"
            );
            Texture2D floorAlbedo = LoadTexture(
                PolyHavenRoot + "/Textures/laminate_floor_02/laminate_floor_02_Diffuse_4k.jpg"
            );
            Texture2D floorNormal = LoadTexture(
                PolyHavenRoot + "/Textures/laminate_floor_02/laminate_floor_02_nor_gl_4k.jpg"
            );
            Texture2D floorMask = LoadTexture(
                PolyHavenRoot + "/Textures/laminate_floor_02/laminate_floor_02_MetallicSmoothness_4k.png"
            );
            Texture2D floorAo = LoadTexture(
                PolyHavenRoot + "/Textures/laminate_floor_02/laminate_floor_02_AO_4k.jpg"
            );
            Texture2D plasterAlbedo = LoadTexture(
                PolyHavenRoot + "/Textures/beige_wall_001/beige_wall_001_Diffuse_4k.jpg"
            );
            Texture2D plasterNormal = LoadTexture(
                PolyHavenRoot + "/Textures/beige_wall_001/beige_wall_001_nor_gl_4k.jpg"
            );
            Texture2D plasterMask = LoadTexture(
                PolyHavenRoot + "/Textures/beige_wall_001/beige_wall_001_MetallicSmoothness_4k.png"
            );
            Texture2D plasterAo = LoadTexture(
                PolyHavenRoot + "/Textures/beige_wall_001/beige_wall_001_AO_4k.jpg"
            );
            Texture2D woodAlbedo = LoadTexture(
                PolyHavenRoot + "/Textures/oak_veneer_01/oak_veneer_01_Diffuse_4k.jpg"
            );
            Texture2D woodNormal = LoadTexture(
                PolyHavenRoot + "/Textures/oak_veneer_01/oak_veneer_01_nor_gl_4k.jpg"
            );
            Texture2D woodMask = LoadTexture(
                PolyHavenRoot + "/Textures/oak_veneer_01/oak_veneer_01_MetallicSmoothness_4k.png"
            );
            Texture2D woodAo = LoadTexture(
                PolyHavenRoot + "/Textures/oak_veneer_01/oak_veneer_01_AO_4k.jpg"
            );
            Texture2D ivoryAlbedo = LoadTexture(
                PolyHavenRoot + "/Textures/curly_teddy_natural/curly_teddy_natural_Diffuse_4k.jpg"
            );
            Texture2D ivoryNormal = LoadTexture(
                PolyHavenRoot + "/Textures/curly_teddy_natural/curly_teddy_natural_nor_gl_4k.jpg"
            );
            Texture2D ivoryMask = LoadTexture(
                PolyHavenRoot + "/Textures/curly_teddy_natural/curly_teddy_natural_MetallicSmoothness_4k.png"
            );
            Texture2D ivoryAo = LoadTexture(
                PolyHavenRoot + "/Textures/curly_teddy_natural/curly_teddy_natural_AO_4k.jpg"
            );
            Texture2D woolAlbedo = LoadTexture(
                PolyHavenRoot + "/Textures/poly_wool_herringbone/poly_wool_herringbone_Diffuse_4k.jpg"
            );
            Texture2D woolNormal = LoadTexture(
                PolyHavenRoot + "/Textures/poly_wool_herringbone/poly_wool_herringbone_nor_gl_4k.jpg"
            );
            Texture2D woolMask = LoadTexture(
                PolyHavenRoot + "/Textures/poly_wool_herringbone/poly_wool_herringbone_MetallicSmoothness_4k.png"
            );
            Texture2D woolAo = LoadTexture(
                PolyHavenRoot + "/Textures/poly_wool_herringbone/poly_wool_herringbone_AO_4k.jpg"
            );
            Texture2D plantLeavesAlbedo = LoadTexture(
                PolyHavenRoot + "/Models/potted_plant_02/potted_plant_02_leaves_BaseAlpha_4k.png"
            );
            Texture2D plantLeavesNormal = LoadTexture(
                PolyHavenRoot + "/Models/potted_plant_02/potted_plant_02_leaves_nor_gl_4k.jpg"
            );
            Texture2D plantLeavesMask = LoadTexture(
                PolyHavenRoot + "/Models/potted_plant_02/potted_plant_02_leaves_MetallicSmoothness_4k.png"
            );
            Texture2D plantPotAlbedo = LoadTexture(
                PolyHavenRoot + "/Models/potted_plant_02/potted_plant_02_pot_diff_4k.jpg"
            );
            Texture2D plantPotNormal = LoadTexture(
                PolyHavenRoot + "/Models/potted_plant_02/potted_plant_02_pot_nor_gl_4k.jpg"
            );
            Texture2D plantPotMask = LoadTexture(
                PolyHavenRoot + "/Models/potted_plant_02/potted_plant_02_pot_MetallicSmoothness_4k.png"
            );

            var materials = new DemoMaterials
            {
                Floor = ConfigurePbrLit(
                    "M_Demo_LaminateFloor",
                    lit,
                    new Color(0.82f, 0.77f, 0.68f, 1f),
                    0f,
                    0.92f,
                    floorAlbedo,
                    floorNormal,
                    floorMask,
                    floorAo,
                    new Vector2(3.6f, 3.6f),
                    0.82f
                ),
                Stone = ConfigureLit(
                    "M_Demo_WarmStone",
                    lit,
                    WarmStone,
                    0f,
                    0.36f,
                    paintAlbedo,
                    paintNormal,
                    null,
                    new Vector2(2.2f, 2.2f)
                ),
                Plaster = ConfigurePbrLit(
                    "M_Demo_SoftIvoryPlaster",
                    lit,
                    new Color(0.92f, 0.90f, 0.86f, 1f),
                    0f,
                    0.86f,
                    plasterAlbedo,
                    plasterNormal,
                    plasterMask,
                    plasterAo,
                    new Vector2(2.8f, 2.8f),
                    0.62f
                ),
                Walnut = ConfigurePbrLit(
                    "M_Demo_Walnut",
                    lit,
                    new Color(0.68f, 0.49f, 0.31f, 1f),
                    0f,
                    0.92f,
                    woodAlbedo,
                    woodNormal,
                    woodMask,
                    woodAo,
                    new Vector2(1.35f, 1.35f),
                    0.78f
                ),
                FabricIvory = ConfigurePbrLit(
                    "M_Demo_FabricIvory",
                    lit,
                    new Color(0.88f, 0.86f, 0.81f, 1f),
                    0f,
                    0.72f,
                    ivoryAlbedo,
                    ivoryNormal,
                    ivoryMask,
                    ivoryAo,
                    new Vector2(3.2f, 3.2f),
                    0.90f
                ),
                FabricTeal = ConfigurePbrLit(
                    "M_Demo_FabricTeal",
                    lit,
                    new Color(0.44f, 0.72f, 0.68f, 1f),
                    0f,
                    0.74f,
                    woolAlbedo,
                    woolNormal,
                    woolMask,
                    woolAo,
                    new Vector2(3.4f, 3.4f),
                    0.82f
                ),
                Carpet = ConfigurePbrLit(
                    "M_Demo_Carpet",
                    lit,
                    new Color(0.62f, 0.60f, 0.58f, 1f),
                    0f,
                    0.70f,
                    woolAlbedo,
                    woolNormal,
                    woolMask,
                    woolAo,
                    new Vector2(5.2f, 5.2f),
                    0.74f
                ),
                DarkMetal = ConfigureLit(
                    "M_Demo_DarkMetal",
                    lit,
                    DarkMetal,
                    0.82f,
                    0.72f,
                    null,
                    null,
                    null,
                    Vector2.one
                ),
                Foliage = ConfigureLit(
                    "M_Demo_Foliage",
                    lit,
                    new Color(0.32f, 0.43f, 0.28f, 1f),
                    0f,
                    0.16f,
                    null,
                    null,
                    null,
                    Vector2.one
                ),
                City = ConfigureLit(
                    "M_Demo_CitySilhouette",
                    lit,
                    new Color(0.38f, 0.43f, 0.45f, 1f),
                    0.05f,
                    0.28f,
                    null,
                    null,
                    null,
                    Vector2.one
                ),
                Skin = ConfigureLit(
                    "M_Demo_WarmSkin",
                    lit,
                    WarmSkin,
                    0f,
                    0.42f,
                    null,
                    null,
                    null,
                    Vector2.one
                ),
                Hair = ConfigureLit(
                    "M_Demo_Hair",
                    lit,
                    new Color(0.035f, 0.025f, 0.02f, 1f),
                    0f,
                    0.25f,
                    null,
                    null,
                    null,
                    Vector2.one
                )
            };

            materials.Glass = ConfigureTransparentLit(
                "M_Demo_ArchitecturalGlass",
                lit,
                new Color(0.67f, 0.79f, 0.80f, 0.13f),
                0.92f
            );
            materials.WarmLight = ConfigureEmissiveLit(
                "M_Demo_WarmCoveLight",
                lit,
                new Color(1f, 0.73f, 0.42f, 1f),
                3.2f
            );
            materials.TealLight = ConfigureEmissiveLit(
                "M_Demo_TealAccent",
                lit,
                MutedTeal,
                2.4f
            );
            materials.AmberLight = ConfigureEmissiveLit(
                "M_Demo_AmberAccent",
                lit,
                MutedAmber,
                2.2f
            );
            materials.TealGlow = ConfigureAdditiveUnlit(
                "M_Demo_TealTranslation",
                unlit,
                new Color(0.18f, 0.72f, 0.75f, 0.62f)
            );
            materials.AmberGlow = ConfigureAdditiveUnlit(
                "M_Demo_AmberTranslation",
                unlit,
                new Color(1f, 0.61f, 0.24f, 0.58f)
            );
            materials.Exterior = ConfigureExteriorMaterial();
            materials.PlantLeaves = ConfigurePbrLit(
                "M_Demo_PottedPlantLeaves",
                lit,
                new Color(0.86f, 0.94f, 0.83f, 1f),
                0f,
                0.88f,
                plantLeavesAlbedo,
                plantLeavesNormal,
                plantLeavesMask,
                null,
                Vector2.one,
                0.94f
            );
            ConfigureAlphaClippedMaterial(materials.PlantLeaves, 0.38f, true);
            materials.PlantPot = ConfigurePbrLit(
                "M_Demo_PottedPlantTerracotta",
                lit,
                new Color(0.90f, 0.82f, 0.72f, 1f),
                0f,
                0.92f,
                plantPotAlbedo,
                plantPotNormal,
                plantPotMask,
                null,
                Vector2.one,
                0.82f
            );
            materials.PlantSoil = ConfigureLit(
                "M_Demo_PottedPlantSoil",
                lit,
                new Color(0.055f, 0.035f, 0.022f, 1f),
                0f,
                0.08f,
                null,
                null,
                null,
                Vector2.one
            );
            materials.PcSkybox = ConfigurePcPanoramicSkybox();
            return materials;
        }

        private static void BuildEnvironment(Transform root, DemoMaterials materials)
        {
            Transform environment = CreateEmpty("Environment", root);
            Transform architecture = CreateEmpty("Architecture", environment);
            Transform exterior = CreateEmpty("Exterior", environment);
            Transform decor = CreateEmpty("Decor", environment);

            CreatePrimitive(
                "Floor Base",
                PrimitiveType.Cylinder,
                architecture,
                new Vector3(0f, -0.10f, 0.35f),
                new Vector3(12.4f, 0.10f, 12.4f),
                Vector3.zero,
                materials.Floor,
                true,
                true,
                true
            );
            CreatePrimitive(
                "Conversation Platform",
                PrimitiveType.Cylinder,
                architecture,
                new Vector3(0f, 0.045f, 0f),
                new Vector3(7.0f, 0.045f, 7.0f),
                Vector3.zero,
                materials.Walnut,
                true,
                true,
                true
            );
            CreatePrimitive(
                "Conversation Rug",
                PrimitiveType.Cylinder,
                architecture,
                new Vector3(0f, 0.102f, 0f),
                new Vector3(5.6f, 0.012f, 5.6f),
                Vector3.zero,
                materials.Carpet,
                true,
                false,
                true
            );
            CreatePrimitive(
                "Ceiling Disc",
                PrimitiveType.Cylinder,
                architecture,
                new Vector3(0f, 3.62f, 0.35f),
                new Vector3(12.3f, 0.06f, 12.3f),
                Vector3.zero,
                materials.Plaster,
                true,
                true,
                true
            );

            BuildWindowArc(architecture, materials);
            BuildFeatureWalls(architecture, materials);
            BuildCeilingCove(architecture, materials);
            BuildFloorInlay(architecture, materials);

            GameObject exteriorDome = CreatePrimitive(
                "Daylight Exterior Dome",
                PrimitiveType.Sphere,
                exterior,
                new Vector3(0f, 4.5f, 1.5f),
                new Vector3(48f, 24f, 48f),
                Vector3.zero,
                materials.Exterior,
                false,
                false,
                false
            );
            exteriorDome.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            exteriorDome.GetComponent<Renderer>().receiveShadows = false;
            exteriorDome.SetActive(false);

            BuildExteriorDepth(exterior, materials);
            BuildDecor(decor, materials);
        }

        private static void BuildWindowArc(Transform parent, DemoMaterials materials)
        {
            Transform windows = CreateEmpty("Curved Daylight Window", parent);
            const int panelCount = 11;
            const float radius = 5.75f;
            const float centerZ = -2.75f;
            const float startAngle = -58f;
            const float endAngle = 58f;
            float step = (endAngle - startAngle) / panelCount;
            float panelWidth = 2f * radius * Mathf.Sin(Mathf.Deg2Rad * step * 0.5f) - 0.045f;

            for (int index = 0; index < panelCount; index++)
            {
                float angle = startAngle + step * (index + 0.5f);
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(
                    Mathf.Sin(radians) * radius,
                    1.72f,
                    centerZ + Mathf.Cos(radians) * radius
                );
                CreatePrimitive(
                    $"Glass Panel {index + 1:00}",
                    PrimitiveType.Cube,
                    windows,
                    position,
                    new Vector3(panelWidth, 3.05f, 0.045f),
                    new Vector3(0f, angle, 0f),
                    materials.Glass,
                    false,
                    false,
                    false
                );
            }

            for (int index = 0; index <= panelCount; index++)
            {
                float angle = startAngle + step * index;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(
                    Mathf.Sin(radians) * radius,
                    1.72f,
                    centerZ + Mathf.Cos(radians) * radius
                );
                CreatePrimitive(
                    $"Window Mullion {index + 1:00}",
                    PrimitiveType.Cube,
                    windows,
                    position,
                    new Vector3(0.055f, 3.18f, 0.11f),
                    new Vector3(0f, angle, 0f),
                    materials.DarkMetal,
                    true,
                    true,
                    true
                );
            }

            CreateArcSegments(
                windows,
                "Window Sill",
                24,
                radius,
                centerZ,
                startAngle,
                endAngle,
                0.18f,
                new Vector3(0.72f, 0.10f, 0.24f),
                materials.Walnut,
                true
            );
            CreateArcSegments(
                windows,
                "Window Header",
                24,
                radius,
                centerZ,
                startAngle,
                endAngle,
                3.30f,
                new Vector3(0.72f, 0.13f, 0.28f),
                materials.Plaster,
                true
            );
        }

        private static void BuildFeatureWalls(Transform parent, DemoMaterials materials)
        {
            Transform left = CreateEmpty("Walnut Feature Wall", parent);
            left.localPosition = new Vector3(-4.65f, 0f, 0.05f);
            left.localRotation = Quaternion.Euler(0f, 21f, 0f);
            CreatePrimitiveLocal(
                "Feature Wall Backing",
                PrimitiveType.Cube,
                left,
                new Vector3(0f, 1.70f, 0f),
                new Vector3(2.15f, 3.40f, 0.20f),
                Vector3.zero,
                materials.Stone,
                true,
                true,
                true
            );
            for (int index = 0; index < 14; index++)
            {
                float x = -0.94f + index * 0.145f;
                CreatePrimitiveLocal(
                    $"Walnut Slat {index + 1:00}",
                    PrimitiveType.Cube,
                    left,
                    new Vector3(x, 1.72f, -0.125f),
                    new Vector3(0.065f, 3.15f, 0.075f),
                    Vector3.zero,
                    materials.Walnut,
                    true,
                    true,
                    true
                );
            }

            Transform right = CreateEmpty("Acoustic Feature Wall", parent);
            right.localPosition = new Vector3(4.65f, 0f, 0.05f);
            right.localRotation = Quaternion.Euler(0f, -21f, 0f);
            CreatePrimitiveLocal(
                "Acoustic Wall Backing",
                PrimitiveType.Cube,
                right,
                new Vector3(0f, 1.70f, 0f),
                new Vector3(2.15f, 3.40f, 0.20f),
                Vector3.zero,
                materials.Stone,
                true,
                true,
                true
            );
            for (int index = 0; index < 5; index++)
            {
                float x = -0.84f + index * 0.42f;
                CreatePrimitiveLocal(
                    $"Acoustic Panel {index + 1:00}",
                    PrimitiveType.Cube,
                    right,
                    new Vector3(x, 1.72f, -0.13f),
                    new Vector3(0.34f, 2.82f, 0.085f),
                    Vector3.zero,
                    index % 2 == 0 ? materials.FabricTeal : materials.FabricIvory,
                    true,
                    true,
                    true
                );
            }
        }

        private static void BuildCeilingCove(Transform parent, DemoMaterials materials)
        {
            Transform cove = CreateEmpty("Ceiling Cove", parent);
            const int segmentCount = 40;
            const float radius = 5.05f;
            float segmentLength = 2f * Mathf.PI * radius / segmentCount * 0.93f;

            for (int index = 0; index < segmentCount; index++)
            {
                float angle = 360f * index / segmentCount;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(
                    Mathf.Sin(radians) * radius,
                    3.48f,
                    0.35f + Mathf.Cos(radians) * radius
                );
                CreatePrimitive(
                    $"Cove Light {index + 1:00}",
                    PrimitiveType.Cube,
                    cove,
                    position,
                    new Vector3(segmentLength, 0.045f, 0.09f),
                    new Vector3(0f, angle, 0f),
                    materials.WarmLight,
                    true,
                    false,
                    true
                );
            }
        }

        private static void BuildFloorInlay(Transform parent, DemoMaterials materials)
        {
            Transform inlay = CreateEmpty("Floor Inlay", parent);
            const int segmentCount = 32;
            const float radius = 3.08f;
            float segmentLength = 2f * Mathf.PI * radius / segmentCount * 0.88f;
            for (int index = 0; index < segmentCount; index++)
            {
                float angle = 360f * index / segmentCount;
                float radians = angle * Mathf.Deg2Rad;
                CreatePrimitive(
                    $"Brass Inlay {index + 1:00}",
                    PrimitiveType.Cube,
                    inlay,
                    new Vector3(
                        Mathf.Sin(radians) * radius,
                        0.116f,
                        Mathf.Cos(radians) * radius
                    ),
                    new Vector3(segmentLength, 0.012f, 0.035f),
                    new Vector3(0f, angle, 0f),
                    materials.AmberLight,
                    true,
                    false,
                    false
                );
            }
        }

        private static void BuildExteriorDepth(Transform parent, DemoMaterials materials)
        {
            Transform greenery = CreateEmpty("Foreground Greenery", parent);
            for (int index = 0; index < 18; index++)
            {
                float x = -5.1f + index * 0.60f;
                float z = 4.10f + Mathf.Abs(x) * 0.15f + Mathf.Sin(index * 1.7f) * 0.22f;
                float y = 0.45f + (index % 3) * 0.12f;
                CreatePrimitive(
                    $"Greenery Mass {index + 1:00}",
                    PrimitiveType.Sphere,
                    greenery,
                    new Vector3(x, y, z),
                    new Vector3(0.85f, 0.68f + (index % 4) * 0.12f, 0.72f),
                    new Vector3(0f, index * 31f, 0f),
                    materials.Foliage,
                    false,
                    false,
                    false
                );
            }

            Transform skyline = CreateEmpty("Distant Skyline", parent);
            float[] heights = { 3.4f, 5.8f, 4.1f, 7.2f, 4.8f, 6.5f, 3.9f, 5.2f, 7.8f, 4.4f };
            for (int index = 0; index < heights.Length; index++)
            {
                float x = -8.1f + index * 1.8f;
                float height = heights[index];
                CreatePrimitive(
                    $"City Mass {index + 1:00}",
                    PrimitiveType.Cube,
                    skyline,
                    new Vector3(x, height * 0.5f, 10f + (index % 3) * 1.4f),
                    new Vector3(1.05f + (index % 2) * 0.45f, height, 1.1f),
                    Vector3.zero,
                    materials.City,
                    false,
                    false,
                    false
                );
            }
            skyline.gameObject.SetActive(false);
        }

        private static void BuildDecor(Transform parent, DemoMaterials materials)
        {
            Transform leftPlinth = CreateEmpty("Left Art Plinth", parent);
            CreatePrimitiveLocal(
                "Plinth",
                PrimitiveType.Cylinder,
                leftPlinth,
                new Vector3(-3.85f, 0.42f, 1.55f),
                new Vector3(0.78f, 0.42f, 0.78f),
                Vector3.zero,
                materials.Stone,
                true,
                true,
                true
            );
            CreatePrimitiveLocal(
                "Sculpture",
                PrimitiveType.Sphere,
                leftPlinth,
                new Vector3(-3.85f, 1.10f, 1.55f),
                new Vector3(0.48f, 0.72f, 0.40f),
                new Vector3(0f, 20f, 12f),
                materials.DarkMetal,
                true,
                true,
                true
            );

            CreatePrimitive(
                "Ambient Accent Left",
                PrimitiveType.Cube,
                parent,
                new Vector3(-4.18f, 1.68f, -0.12f),
                new Vector3(0.035f, 2.45f, 0.035f),
                new Vector3(0f, 21f, 0f),
                materials.TealLight,
                true,
                false,
                false
            );
            CreatePrimitive(
                "Ambient Accent Right",
                PrimitiveType.Cube,
                parent,
                new Vector3(4.18f, 1.68f, -0.12f),
                new Vector3(0.035f, 2.45f, 0.035f),
                new Vector3(0f, -21f, 0f),
                materials.AmberLight,
                true,
                false,
                false
            );
        }

        private static void BuildFurniture(Transform root, DemoMaterials materials)
        {
            Transform furniture = CreateEmpty("Furniture", root);

            GameObject leftChair = InstantiateAndFitPrefab(
                ChairPrefabPath,
                "Signer Lounge Chair (Replaceable)",
                furniture,
                new Vector3(-1.95f, 0.115f, 0.25f),
                90f,
                1.08f
            );
            RetargetFurnitureMaterials(
                leftChair,
                materials.FabricTeal,
                materials.Walnut,
                materials.DarkMetal,
                materials.Glass
            );

            GameObject rightChair = InstantiateAndFitPrefab(
                ChairPrefabPath,
                "Hearing Lounge Chair (Replaceable)",
                furniture,
                new Vector3(1.95f, 0.115f, 0.25f),
                -90f,
                1.08f
            );
            RetargetFurnitureMaterials(
                rightChair,
                materials.FabricIvory,
                materials.Walnut,
                materials.DarkMetal,
                materials.Glass
            );

            GameObject table = InstantiateAndFitPrefab(
                TablePrefabPath,
                "Conversation Table (Replaceable)",
                furniture,
                new Vector3(0f, 0.115f, -0.10f),
                0f,
                0.58f
            );
            RetargetFurnitureMaterials(
                table,
                materials.FabricIvory,
                materials.Walnut,
                materials.DarkMetal,
                materials.Glass
            );

            GameObject sofa = InstantiateAndFitPrefab(
                SofaPrefabPath,
                "Background Sofa (Replaceable)",
                furniture,
                new Vector3(0f, 0.115f, 2.55f),
                180f,
                1.08f
            );
            RetargetFurnitureMaterials(
                sofa,
                materials.FabricIvory,
                materials.Walnut,
                materials.DarkMetal,
                materials.Glass
            );

            GameObject plant = InstantiateAndFitPrefab(
                PlantPrefabPath,
                "Table Plant (Replaceable)",
                furniture,
                new Vector3(0f, 0.72f, -0.10f),
                0f,
                0.28f
            );
            RetargetFurnitureMaterials(
                plant,
                materials.Foliage,
                materials.Walnut,
                materials.DarkMetal,
                materials.Glass
            );
        }

        private static void BuildCharacters(Transform root, DemoMaterials materials)
        {
            Transform characters = CreateEmpty("Characters (Replace These)", root);
            CreateAnriSigner(characters);
            CreateHearingMannequin(characters, materials);

            Transform replacementNotes = CreateEmpty("Replacement Anchors", characters);
            CreateEmpty("Signer Avatar Root", replacementNotes).localPosition =
                new Vector3(-1.62f, 0.115f, 0.18f);
            CreateEmpty("Hearing Avatar Root", replacementNotes).localPosition =
                new Vector3(1.62f, 0.115f, 0.18f);
        }

        private static void CreateAnriSigner(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnriDemoPrefabPath)
                ?? throw new InvalidOperationException(
                    $"Create the ANRI demo prefab before building the scene: {AnriDemoPrefabPath}"
                );
            GameObject signer = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            signer.name = "ANRI - Deaf Signer";
            signer.transform.localPosition = new Vector3(-1.62f, 0.115f, 0.18f);
            signer.transform.localRotation = Quaternion.Euler(0f, 128f, 0f);
            signer.transform.localScale = Vector3.one;
        }

        private static void CreateSignerMannequin(Transform parent, DemoMaterials materials)
        {
            Transform root = CreateEmpty("Deaf Signer Mannequin", parent);
            root.localPosition = new Vector3(-1.62f, 0.115f, 0.18f);

            CreateCapsuleBetween(
                "Torso",
                root,
                new Vector3(0f, 0.86f, 0f),
                new Vector3(0f, 1.50f, 0f),
                0.25f,
                materials.FabricTeal
            );
            CreatePrimitiveLocal(
                "Head",
                PrimitiveType.Sphere,
                root,
                new Vector3(0f, 1.82f, 0f),
                new Vector3(0.40f, 0.50f, 0.42f),
                Vector3.zero,
                materials.Skin,
                false,
                true,
                false
            );
            CreatePrimitiveLocal(
                "Hair",
                PrimitiveType.Sphere,
                root,
                new Vector3(-0.035f, 1.98f, 0f),
                new Vector3(0.40f, 0.26f, 0.43f),
                new Vector3(0f, 0f, 8f),
                materials.Hair,
                false,
                true,
                false
            );
            CreatePrimitiveLocal(
                "Face Direction",
                PrimitiveType.Sphere,
                root,
                new Vector3(0.19f, 1.82f, 0f),
                new Vector3(0.045f, 0.24f, 0.22f),
                Vector3.zero,
                materials.Skin,
                false,
                true,
                false
            );

            CreateCapsuleBetween(
                "Upper Arm Near",
                root,
                new Vector3(0.05f, 1.48f, -0.22f),
                new Vector3(0.35f, 1.32f, -0.28f),
                0.085f,
                materials.FabricTeal
            );
            CreateCapsuleBetween(
                "Forearm Near",
                root,
                new Vector3(0.35f, 1.32f, -0.28f),
                new Vector3(0.69f, 1.48f, -0.23f),
                0.065f,
                materials.Skin
            );
            CreateStylizedHand(
                root,
                "Signing Hand Near",
                new Vector3(0.75f, 1.52f, -0.23f),
                new Vector3(0.28f, 0.12f, 0.02f),
                materials.Skin,
                1f
            );

            CreateCapsuleBetween(
                "Upper Arm Far",
                root,
                new Vector3(0.03f, 1.50f, 0.22f),
                new Vector3(0.35f, 1.57f, 0.17f),
                0.085f,
                materials.FabricTeal
            );
            CreateCapsuleBetween(
                "Forearm Far",
                root,
                new Vector3(0.35f, 1.57f, 0.17f),
                new Vector3(0.68f, 1.68f, 0.08f),
                0.065f,
                materials.Skin
            );
            CreateStylizedHand(
                root,
                "Signing Hand Far",
                new Vector3(0.74f, 1.71f, 0.07f),
                new Vector3(0.20f, 0.22f, -0.04f),
                materials.Skin,
                0.9f
            );

            CreateSeatedLegs(root, 1f, materials.FabricIvory, materials.DarkMetal);
        }

        private static void CreateHearingMannequin(Transform parent, DemoMaterials materials)
        {
            Transform root = CreateEmpty("Hearing Speaker Mannequin", parent);
            root.localPosition = new Vector3(1.62f, 0.115f, 0.18f);

            CreateCapsuleBetween(
                "Torso",
                root,
                new Vector3(0f, 0.86f, 0f),
                new Vector3(0f, 1.50f, 0f),
                0.25f,
                materials.FabricIvory
            );
            CreatePrimitiveLocal(
                "Head",
                PrimitiveType.Sphere,
                root,
                new Vector3(0f, 1.82f, 0f),
                new Vector3(0.40f, 0.50f, 0.42f),
                Vector3.zero,
                materials.Skin,
                false,
                true,
                false
            );
            CreatePrimitiveLocal(
                "Hair",
                PrimitiveType.Sphere,
                root,
                new Vector3(0.035f, 1.98f, 0f),
                new Vector3(0.40f, 0.26f, 0.43f),
                new Vector3(0f, 0f, -8f),
                materials.Hair,
                false,
                true,
                false
            );
            CreatePrimitiveLocal(
                "Face Direction",
                PrimitiveType.Sphere,
                root,
                new Vector3(-0.19f, 1.82f, 0f),
                new Vector3(0.045f, 0.24f, 0.22f),
                Vector3.zero,
                materials.Skin,
                false,
                true,
                false
            );

            CreateCapsuleBetween(
                "Speaking Upper Arm",
                root,
                new Vector3(-0.05f, 1.48f, -0.20f),
                new Vector3(-0.34f, 1.38f, -0.26f),
                0.085f,
                materials.FabricIvory
            );
            CreateCapsuleBetween(
                "Speaking Forearm",
                root,
                new Vector3(-0.34f, 1.38f, -0.26f),
                new Vector3(-0.60f, 1.52f, -0.20f),
                0.065f,
                materials.Skin
            );
            CreatePrimitiveLocal(
                "Speaking Hand",
                PrimitiveType.Sphere,
                root,
                new Vector3(-0.67f, 1.55f, -0.19f),
                new Vector3(0.22f, 0.16f, 0.09f),
                new Vector3(0f, 0f, 15f),
                materials.Skin,
                false,
                true,
                false
            );

            CreateCapsuleBetween(
                "Resting Upper Arm",
                root,
                new Vector3(-0.02f, 1.46f, 0.21f),
                new Vector3(-0.20f, 1.13f, 0.22f),
                0.085f,
                materials.FabricIvory
            );
            CreateCapsuleBetween(
                "Resting Forearm",
                root,
                new Vector3(-0.20f, 1.13f, 0.22f),
                new Vector3(-0.39f, 0.94f, 0.06f),
                0.065f,
                materials.Skin
            );
            CreatePrimitiveLocal(
                "Resting Hand",
                PrimitiveType.Sphere,
                root,
                new Vector3(-0.43f, 0.91f, 0.03f),
                new Vector3(0.20f, 0.13f, 0.08f),
                Vector3.zero,
                materials.Skin,
                false,
                true,
                false
            );

            CreateSeatedLegs(root, -1f, materials.DarkMetal, materials.DarkMetal);
        }

        private static void CreateSeatedLegs(
            Transform root,
            float facing,
            Material trousers,
            Material shoes)
        {
            float side = Mathf.Sign(facing);
            CreateCapsuleBetween(
                "Thigh Near",
                root,
                new Vector3(0.02f, 0.88f, -0.15f),
                new Vector3(0.28f * side, 0.55f, -0.28f),
                0.12f,
                trousers
            );
            CreateCapsuleBetween(
                "Shin Near",
                root,
                new Vector3(0.28f * side, 0.55f, -0.28f),
                new Vector3(0.31f * side, 0.18f, -0.33f),
                0.10f,
                trousers
            );
            CreateCapsuleBetween(
                "Thigh Far",
                root,
                new Vector3(0.02f, 0.88f, 0.15f),
                new Vector3(0.24f * side, 0.55f, 0.19f),
                0.12f,
                trousers
            );
            CreateCapsuleBetween(
                "Shin Far",
                root,
                new Vector3(0.24f * side, 0.55f, 0.19f),
                new Vector3(0.25f * side, 0.18f, 0.15f),
                0.10f,
                trousers
            );
            CreatePrimitiveLocal(
                "Shoe Near",
                PrimitiveType.Sphere,
                root,
                new Vector3(0.35f * side, 0.14f, -0.40f),
                new Vector3(0.30f, 0.12f, 0.18f),
                Vector3.zero,
                shoes,
                false,
                true,
                false
            );
            CreatePrimitiveLocal(
                "Shoe Far",
                PrimitiveType.Sphere,
                root,
                new Vector3(0.30f * side, 0.14f, 0.10f),
                new Vector3(0.30f, 0.12f, 0.18f),
                Vector3.zero,
                shoes,
                false,
                true,
                false
            );
        }

        private static void BuildTranslationVisualization(Transform root, DemoMaterials materials)
        {
            Transform translation = CreateEmpty("Translation Visualization", root);
            Transform signToSpeech = CreateEmpty("Sign to Speech (Teal)", translation);
            Transform speechToCaption = CreateEmpty("Speech to Caption (Amber)", translation);

            Vector3 signStart = new Vector3(-0.86f, 1.60f, -0.04f);
            Vector3 signControl = new Vector3(-0.05f, 2.18f, 0.34f);
            Vector3 signEnd = new Vector3(1.03f, 1.92f, 0.16f);
            for (int index = 0; index < 4; index++)
            {
                float offset = (index - 1.5f) * 0.055f;
                CreateBezierLine(
                    $"Teal Motion Ribbon {index + 1:00}",
                    signToSpeech,
                    signStart + new Vector3(0f, offset, offset),
                    signControl + new Vector3(0f, offset * 0.4f, offset),
                    signEnd + new Vector3(0f, offset * 0.25f, offset),
                    0.012f + index * 0.002f,
                    materials.TealGlow
                );
            }
            CreateWaveform(
                "Teal Speech Waveform",
                signToSpeech,
                new Vector3(0.66f, 2.05f, -0.22f),
                new Vector3(1.40f, 2.05f, -0.22f),
                0.13f,
                0.014f,
                materials.TealGlow,
                0.55f
            );

            Vector3 speechStart = new Vector3(1.38f, 1.91f, 0.02f);
            Vector3 speechControl = new Vector3(0.52f, 2.36f, -0.12f);
            Vector3 speechEnd = new Vector3(-0.72f, 2.12f, -0.12f);
            CreateBezierLine(
                "Amber Voice Arc",
                speechToCaption,
                speechStart,
                speechControl,
                speechEnd,
                0.016f,
                materials.AmberGlow
            );
            CreateWaveform(
                "Amber Caption Waveform",
                speechToCaption,
                new Vector3(-1.28f, 2.16f, -0.16f),
                new Vector3(-0.46f, 2.16f, -0.16f),
                0.16f,
                0.016f,
                materials.AmberGlow,
                0.18f
            );

            CreatePrimitive(
                "Caption Glass Backplate",
                PrimitiveType.Cube,
                speechToCaption,
                new Vector3(-0.87f, 2.16f, -0.10f),
                new Vector3(1.06f, 0.42f, 0.018f),
                Vector3.zero,
                materials.Glass,
                false,
                false,
                false
            );
            CreatePrimitive(
                "Caption Amber Edge",
                PrimitiveType.Cube,
                speechToCaption,
                new Vector3(-0.87f, 1.95f, -0.115f),
                new Vector3(1.08f, 0.018f, 0.018f),
                Vector3.zero,
                materials.AmberLight,
                false,
                false,
                false
            );

            Transform anchors = CreateEmpty("Runtime Integration Anchors", translation);
            CreateEmpty("ASR Caption Anchor", anchors).localPosition =
                new Vector3(-0.87f, 2.16f, -0.13f);
            CreateEmpty("SLT Motion Origin", anchors).localPosition = signStart;
            CreateEmpty("Translated Voice Anchor", anchors).localPosition =
                new Vector3(1.05f, 2.05f, -0.22f);
        }

        private static void BuildLighting(Transform root, DemoMaterials materials)
        {
            Transform lighting = CreateEmpty("Lighting", root);

            Light sun = CreateLight("Late Morning Sun", lighting, LightType.Directional);
            sun.transform.rotation = Quaternion.Euler(46f, -28f, 0f);
            sun.color = new Color(1f, 0.88f, 0.72f, 1f);
            sun.intensity = 1.55f;
            sun.bounceIntensity = 1.15f;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sun.shadowBias = 0.045f;
            sun.shadowNormalBias = 0.30f;
            RenderSettings.sun = sun;

            Light windowKey = CreateSpotLight(
                "Window Key Fill",
                lighting,
                new Vector3(-2.8f, 3.05f, 2.95f),
                new Vector3(-0.9f, 1.25f, 0f),
                new Color(1f, 0.86f, 0.69f, 1f),
                4.2f,
                10f,
                72f
            );
            windowKey.lightmapBakeType = LightmapBakeType.Mixed;

            Light softFill = CreateSpotLight(
                "Soft Interior Fill",
                lighting,
                new Vector3(2.8f, 2.8f, -1.4f),
                new Vector3(0.7f, 1.25f, 0.2f),
                new Color(0.72f, 0.85f, 0.86f, 1f),
                2.4f,
                8f,
                88f
            );
            softFill.lightmapBakeType = LightmapBakeType.Mixed;

            Light frontFill = CreateSpotLight(
                "Hero Front Fill",
                lighting,
                new Vector3(0f, 2.30f, -4.15f),
                new Vector3(0f, 1.34f, 0.15f),
                new Color(1f, 0.79f, 0.64f, 1f),
                1.75f,
                9.5f,
                76f
            );
            frontFill.lightmapBakeType = LightmapBakeType.Mixed;

            Light bounce = CreatePointLight(
                "Conversation Bounce",
                lighting,
                new Vector3(0f, 2.70f, 0.20f),
                new Color(1f, 0.76f, 0.58f, 1f),
                1.18f,
                5.8f
            );
            bounce.lightmapBakeType = LightmapBakeType.Mixed;

            Light signerRim = CreateSpotLight(
                "Signer Teal Rim",
                lighting,
                new Vector3(-3.4f, 2.55f, 1.5f),
                new Vector3(-1.55f, 1.35f, 0.15f),
                new Color(0.34f, 0.67f, 0.66f, 1f),
                0.90f,
                5.5f,
                42f
            );
            signerRim.lightmapBakeType = LightmapBakeType.Realtime;

            Light speakerRim = CreateSpotLight(
                "Speaker Amber Rim",
                lighting,
                new Vector3(3.4f, 2.55f, 1.5f),
                new Vector3(1.55f, 1.35f, 0.15f),
                new Color(1f, 0.62f, 0.31f, 1f),
                0.82f,
                5.5f,
                42f
            );
            speakerRim.lightmapBakeType = LightmapBakeType.Realtime;

            CreateBakedAreaLight(
                "Ceiling Softbox Left",
                lighting,
                new Vector3(-1.85f, 3.38f, 0.05f),
                new Vector3(0f, 0.9f, 0.1f),
                new Color(1f, 0.78f, 0.56f, 1f)
            );
            CreateBakedAreaLight(
                "Ceiling Softbox Right",
                lighting,
                new Vector3(1.85f, 3.38f, 0.05f),
                new Vector3(0f, 0.9f, 0.1f),
                new Color(1f, 0.82f, 0.64f, 1f)
            );

            CreateLightProbeGrid(lighting);
            CreateReflectionProbe(
                "Conversation Reflection Probe",
                lighting,
                new Vector3(0f, 1.55f, 0.35f),
                new Vector3(10.4f, 3.1f, 7.2f),
                256
            );
            CreateReflectionProbe(
                "Window Reflection Probe",
                lighting,
                new Vector3(0f, 1.65f, 2.35f),
                new Vector3(9.2f, 3.0f, 3.2f),
                128
            );
            CreateGlobalVolume(lighting);
        }

        private static Camera BuildCameras(Transform root)
        {
            Transform cameras = CreateEmpty("Cameras", root);
            var heroObject = new GameObject("Hero Camera");
            heroObject.transform.SetParent(cameras, false);
            heroObject.transform.position = new Vector3(0f, 1.67f, -5.80f);
            heroObject.transform.rotation = LookRotation(
                heroObject.transform.position,
                new Vector3(0f, 1.43f, 0.38f)
            );

            Camera camera = heroObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 80f;
            camera.usePhysicalProperties = true;
            camera.sensorSize = new Vector2(36f, 20.25f);
            camera.focalLength = 38f;
            camera.gateFit = Camera.GateFitMode.Vertical;
            camera.depth = 0f;
            heroObject.AddComponent<AudioListener>();

            heroObject.AddComponent<UniversalAdditionalCameraData>();
            ConfigureHeroCameraForPc(camera);

            Transform vrSpawn = CreateEmpty("VR User Spawn", cameras);
            vrSpawn.position = new Vector3(0f, 1.68f, -2.65f);
            vrSpawn.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            CreateEmpty("XR Rig Goes Here", vrSpawn);

            Transform websiteCamera = CreateEmpty("Website Camera Anchor", cameras);
            websiteCamera.position = heroObject.transform.position;
            websiteCamera.rotation = heroObject.transform.rotation;
            return camera;
        }

        private static void ConfigureRenderSettings(DemoMaterials materials)
        {
            Material skybox = materials?.PcSkybox ?? AssetDatabase.LoadAssetAtPath<Material>(
                MaterialFolder + "/M_Demo_ModernBuildingsSkybox.mat"
            );
            if (skybox == null)
            {
                skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            }
            if (skybox != null)
            {
                RenderSettings.skybox = skybox;
            }

            bool usesHdrPanorama = skybox != null && skybox.name == "M_Demo_ModernBuildingsSkybox";
            RenderSettings.ambientMode = usesHdrPanorama ? AmbientMode.Skybox : AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.68f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.43f, 0.40f, 0.36f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.15f, 0.11f, 1f);
            RenderSettings.ambientIntensity = usesHdrPanorama ? 0.82f : 1.05f;
            RenderSettings.reflectionIntensity = usesHdrPanorama ? 1.08f : 0.98f;
            RenderSettings.reflectionBounces = 2;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = usesHdrPanorama ? 256 : 128;
            RenderSettings.fog = false;
            DynamicGI.UpdateEnvironment();
        }

        private static void ConfigureLightingSettings()
        {
            EnsureFolder(SettingsFolder);
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                LightingSettingsPath
            );
            if (settings == null)
            {
                settings = new LightingSettings
                {
                    name = "Realtime Translation Demo Lighting"
                };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution = 24f;
            settings.lightmapMaxSize = 2048;
            settings.lightmapPadding = 4;
            settings.indirectResolution = 2f;
            settings.maxBounces = 4;
            settings.minBounces = 1;
            settings.directSampleCount = 64;
            settings.indirectSampleCount = 256;
            settings.environmentSampleCount = 128;
            settings.ao = true;
            settings.aoMaxDistance = 1.4f;
            settings.aoExponentDirect = 0.85f;
            settings.aoExponentIndirect = 1.18f;
            Lightmapping.lightingSettings = settings;
            EditorUtility.SetDirty(settings);
        }

        private static void RequireGpuLightmapper()
        {
            LightingSettings settings = Lightmapping.lightingSettings;
            if (settings == null || settings.lightmapper != LightingSettings.Lightmapper.ProgressiveGPU)
            {
                throw new InvalidOperationException(
                    "The demo bake is GPU-only, but Progressive GPU Lightmapper is not active."
                );
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders)
            {
                throw new InvalidOperationException(
                    "The demo bake is GPU-only, but Unity cannot access a compute-capable graphics device."
                );
            }

            Debug.Log(
                $"[RealtimeTranslationDemo] GPU-only bake preflight passed: " +
                $"{SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB VRAM, " +
                $"{SystemInfo.graphicsDeviceType}."
            );
        }

        private static void ConfigurePcRenderPipeline()
        {
            UniversalRenderPipelineAsset pipeline = RequireAsset<UniversalRenderPipelineAsset>(
                PcRenderPipelineAssetPath
            );
            pipeline.supportsHDR = true;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1f;
            pipeline.mainLightShadowmapResolution = 4096;
            pipeline.additionalLightsShadowmapResolution = 4096;
            pipeline.shadowDistance = 80f;
            pipeline.shadowCascadeCount = 4;

            SerializedObject pipelineSerialized = new SerializedObject(pipeline);
            pipelineSerialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            pipelineSerialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            pipelineSerialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineSerialized.FindProperty("m_AnyShadowsSupported").boolValue = true;
            SerializedProperty probeBlending = pipelineSerialized.FindProperty(
                "m_ReflectionProbeBlending"
            ) ?? throw new MissingFieldException("UniversalRenderPipelineAsset", "m_ReflectionProbeBlending");
            SerializedProperty probeBoxProjection = pipelineSerialized.FindProperty(
                "m_ReflectionProbeBoxProjection"
            ) ?? throw new MissingFieldException("UniversalRenderPipelineAsset", "m_ReflectionProbeBoxProjection");
            probeBlending.boolValue = true;
            probeBoxProjection.boolValue = true;
            pipelineSerialized.ApplyModifiedPropertiesWithoutUndo();

            ScriptableRendererData renderer = RequireAsset<ScriptableRendererData>(
                PcRendererDataPath
            );
            ScriptableRendererFeature ssao = renderer.rendererFeatures.FirstOrDefault(
                feature => feature != null && feature.GetType().Name == "ScreenSpaceAmbientOcclusion"
            ) ?? throw new InvalidOperationException(
                "PC_Renderer must contain a ScreenSpaceAmbientOcclusion renderer feature."
            );
            ssao.SetActive(true);

            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(pipeline);
        }

        private static void ConfigureHeroCameraForPc(Camera camera)
        {
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.usePhysicalProperties = true;

            UniversalAdditionalCameraData cameraData =
                camera.GetComponent<UniversalAdditionalCameraData>()
                ?? camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.renderShadows = true;
            cameraData.requiresDepthOption = CameraOverrideOption.On;
            cameraData.requiresColorOption = CameraOverrideOption.On;
            cameraData.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            cameraData.stopNaN = true;
            cameraData.dithering = true;

            EditorUtility.SetDirty(cameraData);
            EditorUtility.SetDirty(camera);
        }

        private static VolumeProfile ConfigureCinematicVolumeProfile()
        {
            EnsureFolder(SettingsFolder);
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                VolumeProfilePath
            );
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "Realtime Translation Demo Volume";
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            profile.components.RemoveAll(component => component == null);

            Tonemapping tonemapping = GetOrAddOverride<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            ColorAdjustments color = GetOrAddOverride<ColorAdjustments>(profile);
            color.postExposure.Override(0.28f);
            color.contrast.Override(10f);
            color.saturation.Override(-4f);
            color.colorFilter.Override(new Color(1f, 0.97f, 0.92f, 1f));

            WhiteBalance whiteBalance = GetOrAddOverride<WhiteBalance>(profile);
            whiteBalance.temperature.Override(6f);
            whiteBalance.tint.Override(-2f);

            Bloom bloom = GetOrAddOverride<Bloom>(profile);
            bloom.intensity.Override(0.32f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.52f);

            DepthOfField depthOfField = GetOrAddOverride<DepthOfField>(profile);
            depthOfField.mode.Override(DepthOfFieldMode.Bokeh);
            depthOfField.focusDistance.Override(6.2f);
            depthOfField.aperture.Override(8f);
            depthOfField.focalLength.Override(38f);
            depthOfField.bladeCount.Override(9);
            depthOfField.bladeCurvature.Override(1f);

            Vignette vignette = GetOrAddOverride<Vignette>(profile);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.42f);
            vignette.rounded.Override(false);

            foreach (VolumeComponent component in profile.components)
            {
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void CreateGlobalVolume(Transform parent)
        {
            VolumeProfile profile = ConfigureCinematicVolumeProfile();

            Transform volumeTransform = CreateEmpty("Global Volume", parent);
            Volume volume = volumeTransform.gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(profile);
        }

        private static T GetOrAddOverride<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            profile.components.RemoveAll(component => component == null);
            if (profile.TryGet(out T component))
            {
                return component;
            }

            component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            return component;
        }

        private static Material ConfigureLit(
            string name,
            Shader shader,
            Color baseColor,
            float metallic,
            float smoothness,
            Texture2D albedo,
            Texture2D normal,
            Texture2D mask,
            Vector2 tiling)
        {
            Material material = LoadOrCreateMaterial(name, shader);
            material.shader = shader;
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.renderQueue = (int)RenderQueue.Geometry;
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetTexture("_BumpMap", normal);
            if (normal != null)
            {
                material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_BumpScale", 0.72f);
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
            }

            material.SetTexture("_MetallicGlossMap", mask);
            if (mask != null)
            {
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Smoothness", smoothness);
            }
            else
            {
                material.DisableKeyword("_METALLICSPECGLOSSMAP");
            }

            material.enableInstancing = true;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigurePbrLit(
            string name,
            Shader shader,
            Color baseColor,
            float metallic,
            float smoothness,
            Texture2D albedo,
            Texture2D normal,
            Texture2D mask,
            Texture2D occlusion,
            Vector2 tiling,
            float normalStrength)
        {
            Material material = ConfigureLit(
                name,
                shader,
                baseColor,
                metallic,
                smoothness,
                albedo,
                normal,
                mask,
                tiling
            );
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_BumpScale", normalStrength);
            material.SetTexture("_OcclusionMap", occlusion);
            material.SetFloat("_OcclusionStrength", occlusion == null ? 0f : 0.88f);
            if (occlusion != null)
            {
                material.EnableKeyword("_OCCLUSIONMAP");
            }
            else
            {
                material.DisableKeyword("_OCCLUSIONMAP");
            }
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureAlphaClippedMaterial(
            Material material,
            float cutoff,
            bool doubleSided)
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", cutoff);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", doubleSided ? 0f : 2f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.doubleSidedGI = doubleSided;
            EditorUtility.SetDirty(material);
        }

        private static Material ConfigureTransparentLit(
            string name,
            Shader shader,
            Color color,
            float smoothness)
        {
            Material material = LoadOrCreateMaterial(name, shader);
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0.05f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat(
                "_DstBlend",
                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
            );
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureEmissiveLit(
            string name,
            Shader shader,
            Color color,
            float intensity)
        {
            Material material = ConfigureLit(
                name,
                shader,
                color,
                0f,
                0.58f,
                null,
                null,
                null,
                Vector2.one
            );
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * intensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureAdditiveUnlit(string name, Shader shader, Color color)
        {
            Material material = LoadOrCreateMaterial(name, shader);
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 1f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureExteriorMaterial()
        {
            Shader shader = RequireAsset<Shader>(ExteriorShaderPath);
            Cubemap cubemap = RequireAsset<Cubemap>(ExteriorCubemapPath);
            Material material = LoadOrCreateMaterial("M_Demo_DaylightExterior", shader);
            material.shader = shader;
            material.SetTexture("_Cubemap", cubemap);
            material.SetColor("_Tint", new Color(0.80f, 0.84f, 0.83f, 1f));
            material.SetFloat("_EmissionStrength", 0.92f);
            material.SetFloat("_Rotation", 5f);
            material.enableInstancing = true;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigurePcPanoramicSkybox()
        {
            Shader shader = RequireShader("Skybox/Panoramic");
            Texture2D panorama = RequireAsset<Texture2D>(
                PolyHavenRoot + "/HDRI/modern_buildings_2/modern_buildings_2_4k.hdr"
            );
            Material material = LoadOrCreateMaterial("M_Demo_ModernBuildingsSkybox", shader);
            material.shader = shader;
            material.SetTexture("_MainTex", panorama);
            material.SetColor("_Tint", new Color(0.82f, 0.86f, 0.88f, 1f));
            material.SetFloat("_Exposure", 0.78f);
            material.SetFloat("_Rotation", 0f);
            material.SetFloat("_Mapping", 1f);
            material.SetFloat("_ImageType", 0f);
            material.SetFloat("_MirrorOnBack", 0f);
            material.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            material.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string name, Shader shader)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(shader)
            {
                name = name,
                enableInstancing = true
            };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateArcSegments(
            Transform parent,
            string namePrefix,
            int count,
            float radius,
            float centerZ,
            float startAngle,
            float endAngle,
            float y,
            Vector3 scale,
            Material material,
            bool contributeGI)
        {
            float step = (endAngle - startAngle) / count;
            for (int index = 0; index < count; index++)
            {
                float angle = startAngle + step * (index + 0.5f);
                float radians = angle * Mathf.Deg2Rad;
                CreatePrimitive(
                    $"{namePrefix} {index + 1:00}",
                    PrimitiveType.Cube,
                    parent,
                    new Vector3(
                        Mathf.Sin(radians) * radius,
                        y,
                        centerZ + Mathf.Cos(radians) * radius
                    ),
                    scale,
                    new Vector3(0f, angle, 0f),
                    material,
                    true,
                    true,
                    contributeGI
                );
            }
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Vector3 rotation,
            Material material,
            bool keepCollider,
            bool castShadows,
            bool contributeGI)
        {
            GameObject gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, true);
            gameObject.transform.position = position;
            gameObject.transform.rotation = Quaternion.Euler(rotation);
            gameObject.transform.localScale = scale;
            ConfigurePrimitive(gameObject, material, keepCollider, castShadows, contributeGI);
            return gameObject;
        }

        private static GameObject CreatePrimitiveLocal(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localRotation,
            Material material,
            bool keepCollider,
            bool castShadows,
            bool contributeGI)
        {
            GameObject gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = Quaternion.Euler(localRotation);
            gameObject.transform.localScale = localScale;
            ConfigurePrimitive(gameObject, material, keepCollider, castShadows, contributeGI);
            return gameObject;
        }

        private static void ConfigurePrimitive(
            GameObject gameObject,
            Material material,
            bool keepCollider,
            bool castShadows,
            bool contributeGI)
        {
            Renderer renderer = gameObject.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            renderer.lightProbeUsage = contributeGI
                ? LightProbeUsage.BlendProbes
                : LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            if (renderer is MeshRenderer meshRenderer)
            {
                meshRenderer.receiveGI = contributeGI ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                meshRenderer.scaleInLightmap = contributeGI ? 0.65f : 0f;
            }

            Collider collider = gameObject.GetComponent<Collider>();
            if (!keepCollider && collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            if (contributeGI)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    gameObject,
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.ReflectionProbeStatic
                );
            }
        }

        private static Transform CreateEmpty(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.transform;
        }

        private static GameObject InstantiateAndFitPrefab(
            string prefabPath,
            string name,
            Transform parent,
            Vector3 floorPosition,
            float yaw,
            float targetHeight)
        {
            GameObject prefab = RequireAsset<GameObject>(prefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                SceneManager.GetActiveScene()
            );
            instance.name = name;
            instance.transform.SetParent(parent, true);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Bounds initialBounds = CalculateBounds(instance);
            if (initialBounds.size.y <= 0.001f)
            {
                throw new InvalidOperationException($"Prefab has no renderable bounds: {prefabPath}");
            }

            float scale = targetHeight / initialBounds.size.y;
            instance.transform.localScale *= scale;
            Bounds scaledBounds = CalculateBounds(instance);
            Vector3 correction = floorPosition - new Vector3(
                scaledBounds.center.x,
                scaledBounds.min.y,
                scaledBounds.center.z
            );
            instance.transform.position += correction;

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                if (renderer is MeshRenderer meshRenderer)
                {
                    meshRenderer.receiveGI = ReceiveGI.Lightmaps;
                    meshRenderer.scaleInLightmap = 0.6f;
                }
            }

            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(
                    child.gameObject,
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.ReflectionProbeStatic
                );
            }
            return instance;
        }

        private static void EnsurePottedPlant(
            Transform parent,
            string name,
            Vector3 floorPosition,
            float yaw,
            float targetHeight,
            DemoMaterials materials)
        {
            Transform existing = parent.Find(name);
            GameObject plant = existing == null
                ? InstantiateAndFitPrefab(
                    PottedPlantModelPath,
                    name,
                    parent,
                    floorPosition,
                    yaw,
                    targetHeight
                )
                : existing.gameObject;
            if (existing != null)
            {
                plant.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Bounds currentBounds = CalculateBounds(plant);
                plant.transform.localScale *= targetHeight / currentBounds.size.y;
                Bounds fittedBounds = CalculateBounds(plant);
                plant.transform.position += floorPosition - new Vector3(
                    fittedBounds.center.x,
                    fittedBounds.min.y,
                    fittedBounds.center.z
                );
            }
            RetargetPottedPlantMaterials(plant, materials);
        }

        private static void RetargetPottedPlantMaterials(
            GameObject instance,
            DemoMaterials materials)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                string meshName = filter?.sharedMesh == null
                    ? renderer.gameObject.name.ToLowerInvariant()
                    : filter.sharedMesh.name.ToLowerInvariant();
                Material target = meshName.Contains("leaves")
                    ? materials.PlantLeaves
                    : meshName.Contains("pot")
                        ? materials.PlantPot
                        : materials.PlantSoil;
                int slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                renderer.sharedMaterials = Enumerable.Repeat(target, slotCount).ToArray();
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void RetargetFurnitureMaterials(
            GameObject instance,
            Material fabric,
            Material wood,
            Material metal,
            Material glass)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++)
                {
                    string sourceName = slots[index] == null
                        ? string.Empty
                        : slots[index].name.ToLowerInvariant();
                    if (sourceName.Contains("glass") || sourceName.Contains("water"))
                    {
                        slots[index] = glass;
                    }
                    else if (
                        sourceName.Contains("seat") ||
                        sourceName.Contains("cussion") ||
                        sourceName.Contains("sofa") ||
                        sourceName.Contains("fabric")
                    )
                    {
                        slots[index] = fabric;
                    }
                    else if (
                        sourceName.Contains("wood") ||
                        sourceName.Contains("table") ||
                        sourceName.Contains("frame")
                    )
                    {
                        slots[index] = wood;
                    }
                    else
                    {
                        slots[index] = metal;
                    }
                }
                renderer.sharedMaterials = slots;
            }
        }

        private static void SetRendererMaterial(string path, Material material)
        {
            Renderer renderer = RequireSceneTransform(path).GetComponent<Renderer>();
            if (renderer == null)
            {
                throw new InvalidOperationException($"Scene object has no Renderer: {path}");
            }
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private static void CreateCapsuleBetween(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float radius,
            Material material)
        {
            Vector3 direction = end - start;
            float length = direction.magnitude;
            GameObject capsule = CreatePrimitiveLocal(
                name,
                PrimitiveType.Capsule,
                parent,
                (start + end) * 0.5f,
                new Vector3(radius * 2f, length * 0.5f, radius * 2f),
                Vector3.zero,
                material,
                false,
                true,
                false
            );
            capsule.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        }

        private static void CreateStylizedHand(
            Transform parent,
            string name,
            Vector3 palmPosition,
            Vector3 fingerDirection,
            Material material,
            float spread)
        {
            Transform hand = CreateEmpty(name, parent);
            CreatePrimitiveLocal(
                "Palm",
                PrimitiveType.Sphere,
                hand,
                palmPosition,
                new Vector3(0.22f, 0.16f, 0.075f),
                new Vector3(0f, 0f, 10f),
                material,
                false,
                true,
                false
            );

            Vector3 direction = fingerDirection.normalized;
            for (int index = 0; index < 5; index++)
            {
                float lateral = (index - 2f) * 0.035f * spread;
                float length = 0.10f + (2f - Mathf.Abs(index - 2f)) * 0.018f;
                Vector3 start = palmPosition + new Vector3(0f, lateral, (index - 2f) * 0.012f);
                Vector3 end = start + direction * length + new Vector3(0f, lateral * 0.35f, 0f);
                CreateCapsuleBetween(
                    $"Finger {index + 1}",
                    hand,
                    start,
                    end,
                    0.018f,
                    material
                );
            }
        }

        private static void CreateBezierLine(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float width,
            Material material)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            LineRenderer line = gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 40;
            line.startWidth = width;
            line.endWidth = width * 0.45f;
            line.numCornerVertices = 5;
            line.numCapVertices = 5;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            for (int index = 0; index < line.positionCount; index++)
            {
                float t = index / (line.positionCount - 1f);
                float oneMinusT = 1f - t;
                Vector3 point =
                    oneMinusT * oneMinusT * start +
                    2f * oneMinusT * t * control +
                    t * t * end;
                line.SetPosition(index, point);
            }
        }

        private static void CreateWaveform(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float amplitude,
            float width,
            Material material,
            float phase)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            LineRenderer line = gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 72;
            line.startWidth = width;
            line.endWidth = width;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            Vector3 direction = end - start;
            for (int index = 0; index < line.positionCount; index++)
            {
                float t = index / (line.positionCount - 1f);
                float envelope = Mathf.Sin(Mathf.PI * t);
                float signal =
                    Mathf.Sin((t + phase) * Mathf.PI * 17f) * 0.62f +
                    Mathf.Sin((t + phase) * Mathf.PI * 31f) * 0.26f +
                    Mathf.Sin((t + phase) * Mathf.PI * 7f) * 0.12f;
                Vector3 point = start + direction * t + Vector3.up * signal * amplitude * envelope;
                line.SetPosition(index, point);
            }
        }

        private static Light CreateLight(string name, Transform parent, LightType type)
        {
            Transform transform = CreateEmpty(name, parent);
            Light light = transform.gameObject.AddComponent<Light>();
            light.type = type;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Light CreateSpotLight(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 target,
            Color color,
            float intensity,
            float range,
            float spotAngle)
        {
            Light light = CreateLight(name, parent, LightType.Spot);
            light.transform.position = position;
            light.transform.rotation = LookRotation(position, target);
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = spotAngle * 0.62f;
            light.bounceIntensity = 1f;
            light.shadows = LightShadows.None;
            return light;
        }

        private static void CreateBakedAreaLight(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 target,
            Color color)
        {
            Light light = CreateLight(name, parent, LightType.Rectangle);
            light.transform.position = position;
            light.transform.rotation = LookRotation(position, target);
            light.color = color;
            light.intensity = 2.0f;
            light.range = 6f;
            light.areaSize = new Vector2(2.6f, 1.1f);
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateLightProbeGrid(Transform parent)
        {
            Transform transform = CreateEmpty("Light Probe Grid", parent);
            LightProbeGroup group = transform.gameObject.AddComponent<LightProbeGroup>();
            const int xCount = 6;
            const int yCount = 3;
            const int zCount = 5;
            var positions = new List<Vector3>(xCount * yCount * zCount);
            for (int y = 0; y < yCount; y++)
            {
                for (int z = 0; z < zCount; z++)
                {
                    for (int x = 0; x < xCount; x++)
                    {
                        positions.Add(
                            new Vector3(
                                -3.75f + x * 1.50f,
                                0.35f + y * 0.95f,
                                -1.55f + z * 1.20f
                            )
                        );
                    }
                }
            }
            group.probePositions = positions.ToArray();
        }

        private static void CreateReflectionProbe(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            int resolution)
        {
            Transform transform = CreateEmpty(name, parent);
            transform.position = position;
            ReflectionProbe probe = transform.gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = resolution;
            probe.size = size;
            probe.center = Vector3.zero;
            probe.boxProjection = true;
            probe.blendDistance = 0.65f;
            probe.hdr = true;
            probe.intensity = 0.92f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            transform.gameObject.SetActive(false);
        }

        private static Light CreatePointLight(
            string name,
            Transform parent,
            Vector3 position,
            Color color,
            float intensity,
            float range)
        {
            Light light = CreateLight(name, parent, LightType.Point);
            light.transform.position = position;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.bounceIntensity = 1f;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Quaternion LookRotation(Vector3 origin, Vector3 target)
        {
            return Quaternion.LookRotation((target - origin).normalized, Vector3.up);
        }

        private static Shader RequireShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required shader is unavailable: {shaderName}");
            }
            return shader;
        }

        private static Texture2D LoadTexture(string path)
        {
            return RequireAsset<Texture2D>(path);
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

        private static Transform RequireSceneTransform(string path)
        {
            GameObject gameObject = GameObject.Find(path);
            if (gameObject == null)
            {
                throw new InvalidOperationException($"Scene object not found: {path}");
            }
            return gameObject.transform;
        }

        private static void SetLightIntensity(string name, float intensity)
        {
            Light light = RequireSceneTransform(
                $"_RealtimeTranslationDemo/Lighting/{name}"
            ).GetComponent<Light>();
            light.intensity = intensity;
            EditorUtility.SetDirty(light);
        }

        private static void SetReflectionProbesActive(bool active)
        {
            foreach (
                ReflectionProbe probe in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(
                    FindObjectsInactive.Include
                )
            )
            {
                probe.gameObject.SetActive(active);
            }
        }

        private static void RequireDemoScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before running this command. Current scene: {active.path}"
                );
            }
        }

        private static void EnsureFolder(string path)
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
}
