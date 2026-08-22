using System;
using SignVR.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SignVR.Demo.Editor
{
    public static class RealtimeTranslationDemoCozyLoungeSetup
    {
        private const string DemoScenePath =
            "Assets/RealtimeTranslationDemo/Scenes/RealtimeTranslationDemo.unity";
        private const string DemoRootName = "_RealtimeTranslationDemo";
        private const string LoungeRootName = "Cozy Fireplace Lounge";
        private const string MaterialFolder =
            "Assets/RealtimeTranslationDemo/Materials/CozyLounge";
        private const string TextureFolder =
            "Assets/RealtimeTranslationDemo/ThirdParty/PolyHaven/Textures/CozyLounge";
        private const string VolumeProfilePath =
            "Assets/RealtimeTranslationDemo/Settings/RealtimeTranslationDemoVolume.asset";
        private const string ExteriorMaterialPath =
            "Assets/RealtimeTranslationDemo/Materials/M_Demo_DaylightExterior.mat";
        private const string SofaPath = "Furniture/Background Sofa (Replaceable)";
        private const string TablePath = "Furniture/Conversation Table (Replaceable)";
        private const string PlantPath = "Furniture/Table Plant (Replaceable)";
        private const string CurtainPrefabPath =
            "Assets/UnityJapanOffice/Prefabs/Furnitures/Curtain.prefab";
        private const string CushionAPath =
            "Assets/UnityJapanOffice/Prefabs/Furnitures/Cussion_01A.prefab";
        private const string CushionBPath =
            "Assets/UnityJapanOffice/Prefabs/Furnitures/Cussion_01B.prefab";

        private sealed class LoungeMaterials
        {
            public Material Marble;
            public Material Walnut;
            public Material DarkMetal;
            public Material Screen;
            public Material Firebox;
            public Material EmberBed;
            public Material WarmEmission;
            public Material Fabric;
            public Material Curtain;
            public Material Flame;
            public Material Ember;
            public Material DuskSkybox;
        }

        [MenuItem("SignVR/Demo/Apply Cozy Fireplace Lounge")]
        public static void ApplyCozyFireplaceLounge()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before changing the demo lounge.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {DemoScenePath} before applying the cozy lounge. Current scene: {scene.path}"
                );
            }

            Transform demoRoot = GameObject.Find(DemoRootName)?.transform
                ?? throw new InvalidOperationException($"Demo root was not found: {DemoRootName}");

            EnsureAssetFolder(MaterialFolder);
            LoungeMaterials materials = CreateMaterials();

            Transform previous = demoRoot.Find(LoungeRootName);
            if (previous != null)
            {
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }

            Transform translationVisualization = demoRoot.Find("Translation Visualization");
            if (translationVisualization != null)
            {
                UnityEngine.Object.DestroyImmediate(translationVisualization.gameObject);
            }

            Transform lounge = CreateEmpty(LoungeRootName, demoRoot);
            ConfigureExistingFurniture(demoRoot, lounge);
            BuildFeatureWall(lounge, materials);
            BuildCoffeeTable(lounge, materials);
            BuildRingPendant(lounge, materials);
            BuildSoftDecor(lounge, materials);
            BuildPracticalLights(lounge, materials);
            ConfigureDuskEnvironment(demoRoot, materials);

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = lounge.gameObject;

            Debug.Log(
                "[RealtimeTranslationDemo] Cozy fireplace lounge applied. " +
                "The ring pendant emits real light, the fireplace uses looping particles " +
                "and runtime light flicker, and the Recording scene was not modified."
            );
        }

        private static LoungeMaterials CreateMaterials()
        {
            Shader lit = RequireShader("Universal Render Pipeline/Lit");
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? RequireShader("Universal Render Pipeline/Unlit");
            Shader skyboxShader = RequireShader("Skybox/Procedural");

            Texture2D marbleAlbedo = RequireAsset<Texture2D>(
                TextureFolder + "/marble_01_diff_2k.jpg"
            );
            Texture2D marbleNormal = RequireAsset<Texture2D>(
                TextureFolder + "/marble_01_nor_dx_2k.jpg"
            );
            Texture2D woodAlbedo = RequireAsset<Texture2D>(
                TextureFolder + "/wood_plank_wall_diff_2k.jpg"
            );
            Texture2D woodNormal = RequireAsset<Texture2D>(
                TextureFolder + "/wood_plank_wall_nor_dx_2k.jpg"
            );
            Texture2D fireTexture = CreateOrRefreshFireTexture();

            var materials = new LoungeMaterials
            {
                Marble = ConfigureLitMaterial(
                    "M_Cozy_CreamMarble",
                    lit,
                    new Color(0.90f, 0.86f, 0.78f, 1f),
                    0f,
                    0.58f,
                    marbleAlbedo,
                    marbleNormal,
                    new Vector2(1.15f, 1.15f),
                    0.68f
                ),
                Walnut = ConfigureLitMaterial(
                    "M_Cozy_DarkWalnut",
                    lit,
                    new Color(0.36f, 0.24f, 0.16f, 1f),
                    0f,
                    0.48f,
                    woodAlbedo,
                    woodNormal,
                    new Vector2(1.25f, 0.90f),
                    0.82f
                ),
                DarkMetal = ConfigureLitMaterial(
                    "M_Cozy_DarkMetal",
                    lit,
                    new Color(0.025f, 0.028f, 0.032f, 1f),
                    0.78f,
                    0.62f,
                    null,
                    null,
                    Vector2.one,
                    0f
                ),
                Screen = ConfigureLitMaterial(
                    "M_Cozy_BlankDisplay",
                    lit,
                    new Color(0.008f, 0.010f, 0.014f, 1f),
                    0.18f,
                    0.38f,
                    null,
                    null,
                    Vector2.one,
                    0f
                ),
                Firebox = ConfigureLitMaterial(
                    "M_Cozy_Firebox",
                    lit,
                    new Color(0.012f, 0.009f, 0.008f, 1f),
                    0.22f,
                    0.20f,
                    null,
                    null,
                    Vector2.one,
                    0f
                ),
                Fabric = ConfigureLitMaterial(
                    "M_Cozy_WarmFabric",
                    lit,
                    new Color(0.73f, 0.61f, 0.48f, 1f),
                    0f,
                    0.24f,
                    null,
                    null,
                    Vector2.one,
                    0f
                ),
                Curtain = ConfigureLitMaterial(
                    "M_Cozy_Curtain",
                    lit,
                    new Color(0.28f, 0.20f, 0.18f, 1f),
                    0f,
                    0.18f,
                    null,
                    null,
                    Vector2.one,
                    0f
                ),
                WarmEmission = ConfigureEmissiveMaterial(
                    "M_Cozy_WarmEmission",
                    lit,
                    new Color(1f, 0.47f, 0.14f, 1f),
                    5.2f
                ),
                EmberBed = ConfigureEmissiveMaterial(
                    "M_Cozy_EmberBed",
                    lit,
                    new Color(1f, 0.16f, 0.025f, 1f),
                    0.72f
                ),
                Flame = ConfigureParticleMaterial(
                    "M_Cozy_Flame",
                    particleShader,
                    Color.white,
                    fireTexture,
                    false
                ),
                Ember = ConfigureParticleMaterial(
                    "M_Cozy_Ember",
                    particleShader,
                    Color.white,
                    fireTexture,
                    true
                ),
            };

            materials.DuskSkybox = LoadOrCreateMaterial("M_Cozy_DuskSkybox", skyboxShader);
            materials.DuskSkybox.shader = skyboxShader;
            materials.DuskSkybox.SetColor("_SkyTint", new Color(0.17f, 0.20f, 0.30f, 1f));
            materials.DuskSkybox.SetColor("_GroundColor", new Color(0.10f, 0.075f, 0.065f, 1f));
            materials.DuskSkybox.SetFloat("_Exposure", 0.42f);
            materials.DuskSkybox.SetFloat("_AtmosphereThickness", 0.55f);
            materials.DuskSkybox.SetFloat("_SunSize", 0.025f);
            materials.DuskSkybox.SetFloat("_SunSizeConvergence", 4f);
            EditorUtility.SetDirty(materials.DuskSkybox);
            return materials;
        }

        private static void ConfigureExistingFurniture(Transform demoRoot, Transform lounge)
        {
            SetActiveIfFound(demoRoot, TablePath, false);
            SetActiveIfFound(demoRoot, PlantPath, false);

            Transform sofa = demoRoot.Find(SofaPath)
                ?? throw new InvalidOperationException($"Background sofa was not found: {SofaPath}");
            sofa.gameObject.SetActive(true);
            sofa.SetPositionAndRotation(
                new Vector3(-3.15f, 0.115f, 1.90f),
                Quaternion.Euler(0f, 142f, 0f)
            );

            GameObject rightSofa = UnityEngine.Object.Instantiate(sofa.gameObject, lounge);
            rightSofa.name = "Right Lounge Sofa";
            rightSofa.transform.SetPositionAndRotation(
                new Vector3(3.15f, 0.115f, 1.90f),
                Quaternion.Euler(0f, 218f, 0f)
            );
        }

        private static void BuildFeatureWall(Transform parent, LoungeMaterials materials)
        {
            Transform wall = CreateEmpty("Fireplace Feature Wall", parent);
            CreatePrimitive(
                "Marble Center",
                PrimitiveType.Cube,
                wall,
                new Vector3(0f, 1.78f, 2.92f),
                new Vector3(3.35f, 3.26f, 0.18f),
                Vector3.zero,
                materials.Marble,
                true
            );
            CreatePrimitive(
                "Walnut Left",
                PrimitiveType.Cube,
                wall,
                new Vector3(-2.29f, 1.78f, 2.94f),
                new Vector3(1.22f, 3.26f, 0.22f),
                Vector3.zero,
                materials.Walnut,
                true
            );
            CreatePrimitive(
                "Walnut Right",
                PrimitiveType.Cube,
                wall,
                new Vector3(2.29f, 1.78f, 2.94f),
                new Vector3(1.22f, 3.26f, 0.22f),
                Vector3.zero,
                materials.Walnut,
                true
            );
            CreatePrimitive(
                "Wall Base",
                PrimitiveType.Cube,
                wall,
                new Vector3(0f, 0.18f, 2.78f),
                new Vector3(5.80f, 0.18f, 0.18f),
                Vector3.zero,
                materials.DarkMetal,
                true
            );

            Transform display = CreateEmpty("Wall Display", wall);
            CreatePrimitive(
                "Display Backplate",
                PrimitiveType.Cube,
                display,
                new Vector3(0f, 2.32f, 2.78f),
                new Vector3(2.28f, 1.36f, 0.10f),
                Vector3.zero,
                materials.DarkMetal,
                true
            );
            CreatePrimitive(
                "Blank Display Surface",
                PrimitiveType.Cube,
                display,
                new Vector3(0f, 2.32f, 2.715f),
                new Vector3(2.12f, 1.19f, 0.035f),
                Vector3.zero,
                materials.Screen,
                false
            );

            BuildFireplace(wall, materials);
        }

        private static void BuildFireplace(Transform wall, LoungeMaterials materials)
        {
            Transform fireplace = CreateEmpty("Animated Linear Fireplace", wall);
            CreatePrimitive(
                "Fireplace Outer Frame",
                PrimitiveType.Cube,
                fireplace,
                new Vector3(0f, 1.04f, 2.765f),
                new Vector3(2.48f, 0.53f, 0.13f),
                Vector3.zero,
                materials.DarkMetal,
                true
            );
            CreatePrimitive(
                "Fireplace Recess",
                PrimitiveType.Cube,
                fireplace,
                new Vector3(0f, 1.04f, 2.688f),
                new Vector3(2.25f, 0.36f, 0.055f),
                Vector3.zero,
                materials.Firebox,
                false
            );
            CreatePrimitive(
                "Ember Bed",
                PrimitiveType.Cube,
                fireplace,
                new Vector3(0f, 0.905f, 2.645f),
                new Vector3(1.92f, 0.028f, 0.072f),
                Vector3.zero,
                materials.EmberBed,
                false
            );

            ParticleSystem flames = CreateFlameParticles(fireplace, materials.Flame);
            ParticleSystem embers = CreateEmberParticles(fireplace, materials.Ember);

            Light leftFire = CreatePointLight(
                "Fire Glow Left",
                fireplace,
                new Vector3(-0.66f, 1.08f, 2.30f),
                new Color(1f, 0.28f, 0.055f, 1f),
                0.62f,
                2.6f,
                LightShadows.None
            );
            Light rightFire = CreatePointLight(
                "Fire Glow Right",
                fireplace,
                new Vector3(0.66f, 1.08f, 2.30f),
                new Color(1f, 0.44f, 0.10f, 1f),
                0.56f,
                2.45f,
                LightShadows.None
            );

            FireplaceLightFlicker flicker = fireplace.gameObject.AddComponent<FireplaceLightFlicker>();
            flicker.Configure(
                new[] { leftFire, rightFire },
                new[] { flames, embers },
                0.24f,
                7.8f
            );
        }

        private static void BuildCoffeeTable(Transform parent, LoungeMaterials materials)
        {
            Transform table = CreateEmpty("Large Low Walnut Coffee Table", parent);
            CreatePrimitive(
                "Oval Walnut Top",
                PrimitiveType.Cylinder,
                table,
                new Vector3(0f, 0.43f, 0.62f),
                new Vector3(2.25f, 0.070f, 1.05f),
                Vector3.zero,
                materials.Walnut,
                true
            );
            CreatePrimitive(
                "Dark Undertray",
                PrimitiveType.Cylinder,
                table,
                new Vector3(0f, 0.345f, 0.62f),
                new Vector3(1.98f, 0.042f, 0.83f),
                Vector3.zero,
                materials.DarkMetal,
                true
            );
            CreatePrimitive(
                "Pedestal Left",
                PrimitiveType.Cylinder,
                table,
                new Vector3(-0.56f, 0.22f, 0.62f),
                new Vector3(0.48f, 0.17f, 0.42f),
                Vector3.zero,
                materials.DarkMetal,
                true
            );
            CreatePrimitive(
                "Pedestal Right",
                PrimitiveType.Cylinder,
                table,
                new Vector3(0.56f, 0.22f, 0.62f),
                new Vector3(0.48f, 0.17f, 0.42f),
                Vector3.zero,
                materials.DarkMetal,
                true
            );
        }

        private static void BuildRingPendant(Transform parent, LoungeMaterials materials)
        {
            Transform pendant = CreateEmpty("Glowing Ring Pendant", parent);
            pendant.position = new Vector3(0f, 3.03f, 0.72f);
            CreateRing("Black Ring Body", pendant, 1.04f, 0.082f, materials.DarkMetal);
            CreateRing("Warm Inner Light", pendant, 0.99f, 0.030f, materials.WarmEmission);

            Vector3 ceiling = new Vector3(0f, 3.57f, 0.72f);
            Vector3[] ringPoints =
            {
                new Vector3(-0.72f, 3.03f, 0.22f),
                new Vector3(0.72f, 3.03f, 0.22f),
                new Vector3(-0.72f, 3.03f, 1.22f),
                new Vector3(0.72f, 3.03f, 1.22f),
            };
            for (int index = 0; index < ringPoints.Length; index++)
            {
                CreateCylinderBetween(
                    $"Pendant Cable {index + 1}",
                    pendant,
                    ringPoints[index],
                    ceiling,
                    0.012f,
                    materials.DarkMetal
                );
            }

            Light center = CreateSpotLight(
                "Pendant Warm Downlight",
                pendant,
                new Vector3(0f, 2.96f, 0.72f),
                new Vector3(0f, 0.55f, 0.42f),
                new Color(1f, 0.73f, 0.47f, 1f),
                2.65f,
                7.2f,
                72f,
                LightShadows.Soft
            );
            center.shadowStrength = 0.45f;
            center.shadowBias = 0.055f;

            CreateSpotLight(
                "Pendant Left Fill",
                pendant,
                new Vector3(-0.58f, 2.95f, 0.72f),
                new Vector3(-1.25f, 1.05f, 0.30f),
                new Color(1f, 0.62f, 0.32f, 1f),
                0.72f,
                5.0f,
                58f,
                LightShadows.None
            );
            CreateSpotLight(
                "Pendant Right Fill",
                pendant,
                new Vector3(0.58f, 2.95f, 0.72f),
                new Vector3(1.25f, 1.05f, 0.30f),
                new Color(1f, 0.62f, 0.32f, 1f),
                0.72f,
                5.0f,
                58f,
                LightShadows.None
            );
        }

        private static void BuildSoftDecor(Transform parent, LoungeMaterials materials)
        {
            GameObject leftCurtain = InstantiateAndFitPrefab(
                CurtainPrefabPath,
                "Left Dusk Curtain",
                parent,
                new Vector3(-4.40f, 0.12f, 2.02f),
                19f,
                2.92f
            );
            AssignMaterial(leftCurtain, materials.Curtain);

            GameObject rightCurtain = InstantiateAndFitPrefab(
                CurtainPrefabPath,
                "Right Dusk Curtain",
                parent,
                new Vector3(4.40f, 0.12f, 2.02f),
                -19f,
                2.92f
            );
            AssignMaterial(rightCurtain, materials.Curtain);

            GameObject leftCushion = InstantiateAndFitPrefab(
                CushionAPath,
                "Left Warm Cushion",
                parent,
                new Vector3(-3.08f, 0.54f, 1.80f),
                142f,
                0.34f
            );
            AssignMaterial(leftCushion, materials.Fabric);

            GameObject rightCushion = InstantiateAndFitPrefab(
                CushionBPath,
                "Right Warm Cushion",
                parent,
                new Vector3(3.08f, 0.54f, 1.80f),
                218f,
                0.34f
            );
            AssignMaterial(rightCushion, materials.Fabric);
        }

        private static void BuildPracticalLights(Transform parent, LoungeMaterials materials)
        {
            BuildSideLamp(parent, materials, -4.08f, "Left");
            BuildSideLamp(parent, materials, 4.08f, "Right");
        }

        private static void BuildSideLamp(
            Transform parent,
            LoungeMaterials materials,
            float x,
            string side)
        {
            Transform lamp = CreateEmpty($"{side} Practical Lamp", parent);
            CreatePrimitive(
                "Side Table",
                PrimitiveType.Cylinder,
                lamp,
                new Vector3(x, 0.38f, 1.18f),
                new Vector3(0.72f, 0.055f, 0.72f),
                Vector3.zero,
                materials.Walnut,
                true
            );
            CreatePrimitive(
                "Lamp Base",
                PrimitiveType.Cylinder,
                lamp,
                new Vector3(x, 0.57f, 1.18f),
                new Vector3(0.18f, 0.16f, 0.18f),
                Vector3.zero,
                materials.DarkMetal,
                true
            );
            CreatePrimitive(
                "Warm Globe",
                PrimitiveType.Sphere,
                lamp,
                new Vector3(x, 0.81f, 1.18f),
                new Vector3(0.25f, 0.25f, 0.25f),
                Vector3.zero,
                materials.WarmEmission,
                false
            );
            CreatePointLight(
                "Warm Practical Light",
                lamp,
                new Vector3(x, 0.83f, 1.16f),
                new Color(1f, 0.58f, 0.29f, 1f),
                0.62f,
                3.0f,
                LightShadows.None
            );
        }

        private static void ConfigureDuskEnvironment(
            Transform demoRoot,
            LoungeMaterials materials)
        {
            RenderSettings.skybox = materials.DuskSkybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 0.76f;
            RenderSettings.reflectionIntensity = 0.82f;
            RenderSettings.ambientSkyColor = new Color(0.19f, 0.22f, 0.31f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.23f, 0.18f, 0.18f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.075f, 0.055f, 0.052f, 1f);

            Material exterior = RequireAsset<Material>(ExteriorMaterialPath);
            if (exterior.HasProperty("_EmissionStrength"))
            {
                exterior.SetFloat("_EmissionStrength", 0.34f);
            }
            if (exterior.HasProperty("_Tint"))
            {
                exterior.SetColor("_Tint", new Color(0.41f, 0.46f, 0.62f, 1f));
            }
            EditorUtility.SetDirty(exterior);

            ConfigureLight(demoRoot, "Late Morning Sun", new Color(0.52f, 0.58f, 0.75f, 1f), 0.22f);
            ConfigureLight(demoRoot, "Window Key Fill", new Color(0.52f, 0.60f, 0.80f, 1f), 0.48f);
            ConfigureLight(demoRoot, "Soft Interior Fill", new Color(0.82f, 0.80f, 0.86f, 1f), 1.15f);
            ConfigureLight(demoRoot, "Hero Front Fill", new Color(1f, 0.83f, 0.72f, 1f), 2.02f);
            ConfigureLight(demoRoot, "Conversation Bounce", new Color(1f, 0.55f, 0.31f, 1f), 0.32f);
            ConfigureLight(demoRoot, "Signer Teal Rim", new Color(0.48f, 0.65f, 0.72f, 1f), 0.42f);
            ConfigureLight(demoRoot, "Speaker Amber Rim", new Color(1f, 0.55f, 0.28f, 1f), 0.48f);

            VolumeProfile profile = RequireAsset<VolumeProfile>(VolumeProfilePath);
            ConfigureVolume(profile);
        }

        private static void ConfigureVolume(VolumeProfile profile)
        {
            ColorAdjustments color = RequireVolumeOverride<ColorAdjustments>(profile);
            WhiteBalance whiteBalance = RequireVolumeOverride<WhiteBalance>(profile);
            Bloom bloom = RequireVolumeOverride<Bloom>(profile);
            Vignette vignette = RequireVolumeOverride<Vignette>(profile);

            color.postExposure.Override(0.12f);
            color.contrast.Override(8f);
            color.saturation.Override(-2f);
            color.colorFilter.Override(new Color(1f, 0.965f, 0.92f, 1f));
            whiteBalance.temperature.Override(6f);
            whiteBalance.tint.Override(1f);
            bloom.intensity.Override(0.34f);
            bloom.threshold.Override(0.88f);
            bloom.scatter.Override(0.56f);
            vignette.intensity.Override(0.11f);
            vignette.smoothness.Override(0.42f);

            EditorUtility.SetDirty(color);
            EditorUtility.SetDirty(whiteBalance);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(vignette);
            EditorUtility.SetDirty(profile);
        }

        private static void ConfigureLight(Transform demoRoot, string name, Color color, float intensity)
        {
            Transform lightTransform = demoRoot.Find("Lighting/" + name)
                ?? throw new InvalidOperationException($"Demo light was not found: {name}");
            Light light = lightTransform.GetComponent<Light>()
                ?? throw new MissingComponentException($"{name} has no Light component.");
            light.color = color;
            light.intensity = intensity;
            EditorUtility.SetDirty(light);
        }

        private static ParticleSystem CreateFlameParticles(Transform parent, Material material)
        {
            GameObject gameObject = new GameObject("Looping Flames");
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = new Vector3(0f, 1.00f, 2.59f);
            ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1.8f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.52f, 0.88f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.62f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.55f, 0.12f, 0.94f),
                new Color(1f, 0.18f, 0.035f, 0.86f)
            );
            main.maxParticles = 220;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 112f;
            emission.SetBursts(
                new[]
                {
                    new ParticleSystem.Burst(0f, 36),
                    new ParticleSystem.Burst(0.72f, 20),
                }
            );
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.82f, 0.020f, 0.048f);

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.86f, 0.34f), 0f),
                    new GradientColorKey(new Color(1f, 0.30f, 0.055f), 0.48f),
                    new GradientColorKey(new Color(0.48f, 0.035f, 0.01f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.92f, 0.12f),
                    new GradientAlphaKey(0.72f, 0.62f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.20f),
                    new Keyframe(0.16f, 1f),
                    new Keyframe(1f, 0.12f)
                )
            );

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.23f;
            noise.frequency = 0.85f;
            noise.scrollSpeed = 0.42f;

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.sortingFudge = -0.2f;
            return particles;
        }

        private static ParticleSystem CreateEmberParticles(Transform parent, Material material)
        {
            GameObject gameObject = new GameObject("Rising Embers");
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = new Vector3(0f, 0.96f, 2.56f);
            ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 2.2f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.38f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.032f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.92f, 0.42f, 1f),
                new Color(1f, 0.32f, 0.06f, 1f)
            );
            main.maxParticles = 90;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 16f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.72f, 0.02f, 0.04f);

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.78f, 0.24f), 0f),
                    new GradientColorKey(new Color(1f, 0.18f, 0.02f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            color.color = gradient;

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            return particles;
        }

        private static Texture2D CreateOrRefreshFireTexture()
        {
            const int textureSize = 256;
            string path = MaterialFolder + "/T_Cozy_SoftFlame.asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(
                    textureSize,
                    textureSize,
                    TextureFormat.RGBA32,
                    true,
                    true)
                {
                    name = "T_Cozy_SoftFlame",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                AssetDatabase.CreateAsset(texture, path);
            }
            else if (texture.width != textureSize || texture.height != textureSize)
            {
                texture.Reinitialize(textureSize, textureSize, TextureFormat.RGBA32, true);
            }

            var pixels = new Color[textureSize * textureSize];
            for (int y = 0; y < textureSize; y++)
            {
                float v = y / (textureSize - 1f);
                float halfWidth = Mathf.Lerp(0.90f, 0.12f, Mathf.Pow(v, 0.72f));
                for (int x = 0; x < textureSize; x++)
                {
                    float u = x / (textureSize - 1f) * 2f - 1f;
                    float horizontal = Mathf.Abs(u) / halfWidth;
                    float vertical = Mathf.Abs(v - 0.39f) / 0.72f;
                    float alpha = Mathf.Clamp01(1f - horizontal * horizontal - vertical * vertical);
                    alpha = Mathf.Pow(alpha, 1.10f) * Mathf.SmoothStep(0f, 1f, v * 6f);
                    pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Material ConfigureLitMaterial(
            string name,
            Shader shader,
            Color color,
            float metallic,
            float smoothness,
            Texture2D albedo,
            Texture2D normal,
            Vector2 tiling,
            float normalStrength)
        {
            Material material = LoadOrCreateMaterial(name, shader);
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", normalStrength);
            if (normal != null)
            {
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
            }
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureEmissiveMaterial(
            string name,
            Shader shader,
            Color color,
            float intensity)
        {
            Material material = ConfigureLitMaterial(
                name,
                shader,
                color,
                0f,
                0.48f,
                null,
                null,
                Vector2.one,
                0f
            );
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * intensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureParticleMaterial(
            string name,
            Shader shader,
            Color color,
            Texture2D texture,
            bool additive)
        {
            Material material = LoadOrCreateMaterial(name, shader);
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 1f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_BlendOp", (float)BlendOp.Add);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string name, Shader shader)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        private static Transform CreateEmpty(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.transform;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Vector3 rotation,
            Material material,
            bool castShadows)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, true);
            gameObject.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            gameObject.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<Collider>());
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.receiveGI = ReceiveGI.Lightmaps;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            GameObjectUtility.SetStaticEditorFlags(
                gameObject,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.ReflectionProbeStatic
            );
            return gameObject;
        }

        private static void CreateRing(
            string name,
            Transform parent,
            float radius,
            float width,
            Material material)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            LineRenderer line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 80;
            line.widthMultiplier = width;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int index = 0; index < line.positionCount; index++)
            {
                float angle = index / (float)line.positionCount * Mathf.PI * 2f;
                line.SetPosition(index, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        private static void CreateCylinderBetween(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float radius,
            Material material)
        {
            Vector3 direction = end - start;
            GameObject cylinder = CreatePrimitive(
                name,
                PrimitiveType.Cylinder,
                parent,
                (start + end) * 0.5f,
                new Vector3(radius, direction.magnitude * 0.5f, radius),
                Vector3.zero,
                material,
                false
            );
            cylinder.transform.up = direction.normalized;
        }

        private static Light CreatePointLight(
            string name,
            Transform parent,
            Vector3 position,
            Color color,
            float intensity,
            float range,
            LightShadows shadows)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, true);
            gameObject.transform.position = position;
            Light light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows;
            light.lightmapBakeType = LightmapBakeType.Realtime;
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
            float angle,
            LightShadows shadows)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, true);
            gameObject.transform.position = position;
            gameObject.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
            Light light = gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.58f;
            light.shadows = shadows;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            return light;
        }

        private static GameObject InstantiateAndFitPrefab(
            string assetPath,
            string name,
            Transform parent,
            Vector3 basePosition,
            float yaw,
            float targetHeight)
        {
            GameObject prefab = RequireAsset<GameObject>(assetPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.SetPositionAndRotation(basePosition, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one;

            Bounds bounds = CalculateBounds(instance);
            float scale = targetHeight / bounds.size.y;
            instance.transform.localScale = Vector3.one * scale;
            bounds = CalculateBounds(instance);
            instance.transform.position += Vector3.up * (basePosition.y - bounds.min.y);
            return instance;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException($"{root.name} has no renderer bounds.");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        private static void AssignMaterial(GameObject root, Material material)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++)
                {
                    slots[index] = material;
                }
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static void SetActiveIfFound(Transform root, string path, bool active)
        {
            Transform found = root.Find(path);
            if (found != null)
            {
                found.gameObject.SetActive(active);
            }
        }

        private static T RequireVolumeOverride<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                throw new InvalidOperationException(
                    $"{profile.name} is missing required {typeof(T).Name} override."
                );
            }
            return component;
        }

        private static Shader RequireShader(string name)
        {
            return Shader.Find(name)
                ?? throw new InvalidOperationException($"Required shader was not found: {name}");
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path)
                ?? throw new InvalidOperationException($"Required asset was not found: {path}");
        }

        private static void EnsureAssetFolder(string folder)
        {
            string[] segments = folder.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }
                current = next;
            }
        }
    }
}
