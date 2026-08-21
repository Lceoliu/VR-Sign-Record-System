using System;
using System.Linq;
using SignVR.Demo;
using SignVR.RealtimeTranslationDemo.EditorTools;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SignVR.Demo.Editor
{
    public static class RealtimeTranslationDemoPcCinematicSetup
    {
        private const string DemoScenePath =
            "Assets/RealtimeTranslationDemo/Scenes/RealtimeTranslationDemo.unity";
        private const string DemoRootName = "_RealtimeTranslationDemo";
        private const string LoungeRootName = "Cozy Fireplace Lounge";
        private const string VolumeProfilePath =
            "Assets/RealtimeTranslationDemo/Settings/RealtimeTranslationDemoVolume.asset";
        private const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string CozyMaterialFolder =
            "Assets/RealtimeTranslationDemo/Materials/CozyLounge";
        private const string NightCityHdriPath =
            "Assets/RealtimeTranslationDemo/ThirdParty/PolyHaven/HDRI/shanghai_bund_4k.exr";
        private const string NightCitySkyboxMaterialPath =
            "Assets/RealtimeTranslationDemo/Materials/CozyLounge/M_PC_ShanghaiBundNight_Skybox.mat";
        private const string RainyWindowShaderPath =
            "Assets/RealtimeTranslationDemo/Shaders/PC_RainyWindowGlass.shader";
        private const string RainyWindowMaterialPath =
            "Assets/RealtimeTranslationDemo/Materials/CozyLounge/M_PC_RainyWindowGlass.mat";
        private const string VfxPackageName = "com.unity.visualeffectgraph";
        private const string VfxPackageVersion = "17.5.0";
        private const string VfxSampleName = "Visual Effect Graph Additions";
        private const string VfxSampleRoot =
            "Assets/Samples/Visual Effect Graph/17.5.0/Visual Effect Graph Additions";
        private const string CinematicVfxRootName = "PC Cinematic VFX Fire";

        [MenuItem("SignVR/Demo/Import Official PC VFX Fire")]
        public static void ImportOfficialPcVfxFire()
        {
            Sample sample = Sample.FindByPackage(VfxPackageName, VfxPackageVersion)
                .Single(item => item.displayName == VfxSampleName);
            if (!sample.isImported)
            {
                bool imported = sample.Import(
                    Sample.ImportOptions.HideImportWindow |
                    Sample.ImportOptions.OverridePreviousImports
                );
                if (!imported)
                {
                    throw new InvalidOperationException(
                        $"Unity could not import the {VfxSampleName} sample."
                    );
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureFlameFlipbookImport();
            RequireAsset<Texture2D>(VfxSampleRoot + "/Textures/Fire/Flame03_Temp_16x5.png");
            Debug.Log(
                "[RealtimeTranslationDemo] Imported Unity's official Visual Effect Graph " +
                "Additions sample for the PC cinematic fireplace."
            );
        }

        [MenuItem("SignVR/Demo/Apply PC Cinematic Fire And Lighting")]
        public static void ApplyPcCinematicFireAndLighting()
        {
            RequireDemoScene();
            ImportOfficialPcVfxFire();
            RealtimeTranslationDemoSceneBuilder.ConfigurePcCinematicRendering();

            Transform demoRoot = GameObject.Find(DemoRootName)?.transform
                ?? throw new InvalidOperationException($"Demo root was not found: {DemoRootName}");
            Transform lounge = demoRoot.Find(LoungeRootName)
                ?? throw new InvalidOperationException($"Lounge root was not found: {LoungeRootName}");

            RealtimeTranslationDemoWarmToonSetup.InstallAsteTAsRightHearingSpeaker();
            ConfigureNightCityEnvironment(demoRoot);
            ConfigureRainyWindows(demoRoot);
            ConfigureStaticGlobalIllumination(lounge);
            ConfigureLoungeLights(demoRoot, lounge);
            ConfigureFireplace(lounge);
            ConfigurePcRendererQuality();
            ConfigureCinematicPostProcessing();
            ConfigureReflectionProbes(demoRoot);
            RealtimeTranslationDemoSceneBuilder.ConfigureRealtimeTranslationGpuLightmapper();

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[RealtimeTranslationDemo] PC cinematic pass applied: official GPU VFX flames, " +
                "smoke and sparks; mixed practical lighting; lightmapped static lounge geometry; " +
                "high-sample SSAO; high-quality bloom/DOF; ACES and film grain."
            );
        }

        [MenuItem("SignVR/Demo/Bake PC Cinematic Lighting (GPU)")]
        public static void BakePcCinematicLightingGpu()
        {
            ApplyPcCinematicFireAndLighting();
            ReleaseMemoryBeforePcBake();
            RealtimeTranslationDemoSceneBuilder.BakeRealtimeTranslationLighting();
        }

        [MenuItem("SignVR/Demo/Apply Rainy Night Fireplace Tuning")]
        public static void ApplyRainyNightFireplaceTuning()
        {
            RequireDemoScene();
            Transform demoRoot = GameObject.Find(DemoRootName)?.transform
                ?? throw new InvalidOperationException($"Demo root was not found: {DemoRootName}");
            Transform lounge = demoRoot.Find(LoungeRootName)
                ?? throw new InvalidOperationException($"Lounge root was not found: {LoungeRootName}");

            ConfigureNightCityEnvironment(demoRoot);
            ConfigureRainyWindows(demoRoot);
            ConfigureFireplace(lounge);
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[RealtimeTranslationDemo] Applied calm contained fireplace VFX and " +
                "high-density rainy-night window treatment without rebuilding fireplace geometry."
            );
        }

        [MenuItem("SignVR/Demo/Release Memory Before PC Bake")]
        public static void ReleaseMemoryBeforePcBake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before releasing bake memory.");
            }

            EditorUtility.UnloadUnusedAssetsImmediate(true);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Debug.Log("[RealtimeTranslationDemo] Released unused Editor assets before GPU bake.");
        }

        private static void ConfigureFireplace(Transform lounge)
        {
            Transform fireplace = lounge.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "Animated Linear Fireplace");

            if (!HasCompleteFireplaceGeometry(fireplace))
            {
                ConfigureFireplaceGeometry(fireplace);
            }

            Transform legacyFlames = fireplace.Find("Looping Flames");
            if (legacyFlames != null)
            {
                legacyFlames.gameObject.SetActive(false);
            }

            Transform legacyEmbers = fireplace.Find("Rising Embers");
            if (legacyEmbers != null)
            {
                legacyEmbers.gameObject.SetActive(false);
            }

            Transform vfxRoot = fireplace.Find(CinematicVfxRootName);
            if (vfxRoot == null)
            {
                Transform floor = fireplace.Find("Firebox Floor");
                vfxRoot = new GameObject(CinematicVfxRootName).transform;
                vfxRoot.SetParent(fireplace, false);
                vfxRoot.localPosition = floor.localPosition + new Vector3(
                    0f,
                    Mathf.Abs(floor.localScale.y) * 0.5f + 0.0175f,
                    0f
                );
            }

            Texture2D flameFlipbook = RequireAsset<Texture2D>(
                VfxSampleRoot + "/Textures/Fire/Flame03_Temp_16x5.png"
            );
            Texture2D smokeFlipbook = RequireAsset<Texture2D>(
                VfxSampleRoot + "/Textures/Smoke/WispySmoke03b_8x8.png"
            );
            Material flameMaterial = CreateParticleMaterial(
                "M_PC_Cinematic_FlameFlipbook",
                flameFlipbook,
                true
            );
            Material smokeMaterial = CreateParticleMaterial(
                "M_PC_Cinematic_SmokeFlipbook",
                smokeFlipbook,
                false
            );

            ParticleSystem flameCore = CreateFlameParticleSystem(
                vfxRoot,
                "Cinematic Flame Core",
                flameMaterial,
                28f,
                new Vector2(0.70f, 1.05f),
                new Vector2(0.15f, 0.21f),
                new Vector2(0.07f, 0.12f),
                0.36f,
                new Vector3(-0.52f, 0.015f, 0.015f),
                0.018f,
                new Color(1f, 0.92f, 0.54f, 0.92f),
                new Color(1f, 0.36f, 0.055f, 0.84f)
            );
            ParticleSystem flameBody = CreateFlameParticleSystem(
                vfxRoot,
                "Cinematic Flame Body",
                flameMaterial,
                38f,
                new Vector2(0.80f, 1.18f),
                new Vector2(0.18f, 0.27f),
                new Vector2(0.08f, 0.14f),
                0.60f,
                new Vector3(0.08f, 0f, 0.035f),
                0.022f,
                new Color(1f, 0.66f, 0.16f, 0.88f),
                new Color(0.94f, 0.10f, 0.018f, 0.72f)
            );
            ParticleSystem flameLicks = CreateFlameParticleSystem(
                vfxRoot,
                "Cinematic Flame Licks",
                flameMaterial,
                12f,
                new Vector2(0.92f, 1.35f),
                new Vector2(0.12f, 0.20f),
                new Vector2(0.09f, 0.15f),
                0.24f,
                new Vector3(0.72f, 0.025f, 0.055f),
                0.028f,
                new Color(1f, 0.42f, 0.07f, 0.72f),
                new Color(0.72f, 0.035f, 0.006f, 0.52f)
            );
            ParticleSystem flamePocket = CreateFlameParticleSystem(
                vfxRoot,
                "Cinematic Flame Pocket",
                flameMaterial,
                10f,
                new Vector2(0.72f, 1.12f),
                new Vector2(0.12f, 0.17f),
                new Vector2(0.07f, 0.12f),
                0.16f,
                new Vector3(-0.94f, 0.005f, 0.045f),
                0.024f,
                new Color(1f, 0.74f, 0.22f, 0.86f),
                new Color(0.82f, 0.08f, 0.012f, 0.62f)
            );
            ParticleSystem smoke = CreateSmokeParticleSystem(vfxRoot, smokeMaterial);
            ClampFireVfxInsideFirebox(fireplace, flameCore, flameBody, flameLicks, flamePocket, smoke);

            Light[] fireLights = fireplace.GetComponentsInChildren<Light>(true)
                .Where(light => light.name.StartsWith("Fire Glow", StringComparison.Ordinal))
                .OrderBy(light => light.name)
                .ToArray();
            fireLights[0].intensity = 0.72f;
            fireLights[0].range = 2.45f;
            fireLights[1].intensity = 0.50f;
            fireLights[1].range = 2.25f;
            foreach (Light light in fireLights)
            {
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.shadows = LightShadows.None;
            }

            FireplaceLightFlicker flicker = fireplace.GetComponent<FireplaceLightFlicker>();
            ParticleSystem[] fireSystems = { flameCore, flameBody, flameLicks, flamePocket, smoke };
            flicker.Configure(fireLights, fireSystems, 0.16f, 1.8f);
        }

        private static bool HasCompleteFireplaceGeometry(Transform fireplace)
        {
            string[] requiredChildren =
            {
                "Fireplace Surround Top",
                "Fireplace Surround Bottom",
                "Fireplace Surround Left",
                "Fireplace Surround Right",
                "Firebox Back",
                "Firebox Floor",
                "Firebox Ceiling",
                "Firebox Left Wall",
                "Firebox Right Wall",
                "Ember Base",
            };
            return requiredChildren.All(childName => fireplace.Find(childName) != null);
        }

        private static void ClampFireVfxInsideFirebox(
            Transform fireplace,
            params ParticleSystem[] particleSystems)
        {
            Bounds floorBounds = fireplace.Find("Firebox Floor").GetComponent<Renderer>().bounds;
            Bounds ceilingBounds = fireplace.Find("Firebox Ceiling").GetComponent<Renderer>().bounds;
            Bounds leftBounds = fireplace.Find("Firebox Left Wall").GetComponent<Renderer>().bounds;
            Bounds rightBounds = fireplace.Find("Firebox Right Wall").GetComponent<Renderer>().bounds;
            Bounds backBounds = fireplace.Find("Firebox Back").GetComponent<Renderer>().bounds;

            float minimumY = floorBounds.max.y + 0.024f;
            float maximumY = ceilingBounds.min.y - 0.19f;
            float minimumZ = floorBounds.min.z + 0.045f;
            float maximumZ = backBounds.min.z - 0.035f;
            foreach (ParticleSystem particles in particleSystems)
            {
                ParticleSystem.ShapeModule shape = particles.shape;
                ParticleSystem.MainModule main = particles.main;
                float horizontalPadding = Mathf.Min(
                    0.16f,
                    shape.scale.x * 0.5f + main.startSize.constantMax * 0.22f
                );
                Vector3 position = particles.transform.position;
                position.x = Mathf.Clamp(
                    position.x,
                    leftBounds.max.x + horizontalPadding,
                    rightBounds.min.x - horizontalPadding
                );
                position.y = Mathf.Clamp(position.y, minimumY, maximumY);
                position.z = Mathf.Clamp(position.z, minimumZ, maximumZ);
                particles.transform.position = position;
            }
        }

        private static void ConfigureFireplaceGeometry(Transform fireplace)
        {
            string[] replaceableChildren =
            {
                "Fireplace Outer Frame",
                "Fireplace Recess",
                "Ember Bed",
                "Fireplace Surround Top",
                "Fireplace Surround Bottom",
                "Fireplace Surround Left",
                "Fireplace Surround Right",
                "Firebox Back",
                "Firebox Floor",
                "Firebox Ceiling",
                "Firebox Left Wall",
                "Firebox Right Wall",
                "Ember Base",
                "Irregular Ember Coals",
            };
            foreach (string childName in replaceableChildren)
            {
                Transform child = fireplace.Find(childName);
                if (child != null)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            Material darkMetal = RequireAsset<Material>($"{CozyMaterialFolder}/M_Cozy_DarkMetal.mat");
            Material firebox = RequireAsset<Material>($"{CozyMaterialFolder}/M_Cozy_Firebox.mat");
            Material ember = RequireAsset<Material>($"{CozyMaterialFolder}/M_Cozy_EmberBed.mat");

            CreateFireplacePrimitive("Fireplace Surround Top", PrimitiveType.Cube, fireplace,
                new Vector3(0f, 1.285f, 2.68f), new Vector3(2.52f, 0.095f, 0.11f), darkMetal, true);
            CreateFireplacePrimitive("Fireplace Surround Bottom", PrimitiveType.Cube, fireplace,
                new Vector3(0f, 0.795f, 2.68f), new Vector3(2.52f, 0.095f, 0.11f), darkMetal, true);
            CreateFireplacePrimitive("Fireplace Surround Left", PrimitiveType.Cube, fireplace,
                new Vector3(-1.215f, 1.04f, 2.68f), new Vector3(0.09f, 0.40f, 0.11f), darkMetal, true);
            CreateFireplacePrimitive("Fireplace Surround Right", PrimitiveType.Cube, fireplace,
                new Vector3(1.215f, 1.04f, 2.68f), new Vector3(0.09f, 0.40f, 0.11f), darkMetal, true);

            CreateFireplacePrimitive("Firebox Back", PrimitiveType.Cube, fireplace,
                new Vector3(0f, 1.04f, 2.895f), new Vector3(2.34f, 0.40f, 0.035f), firebox, false);
            CreateFireplacePrimitive("Firebox Floor", PrimitiveType.Cube, fireplace,
                new Vector3(0f, 0.835f, 2.79f), new Vector3(2.34f, 0.035f, 0.22f), firebox, false);
            CreateFireplacePrimitive("Firebox Ceiling", PrimitiveType.Cube, fireplace,
                new Vector3(0f, 1.245f, 2.79f), new Vector3(2.34f, 0.035f, 0.22f), firebox, false);
            CreateFireplacePrimitive("Firebox Left Wall", PrimitiveType.Cube, fireplace,
                new Vector3(-1.145f, 1.04f, 2.79f), new Vector3(0.045f, 0.40f, 0.22f), firebox, false);
            CreateFireplacePrimitive("Firebox Right Wall", PrimitiveType.Cube, fireplace,
                new Vector3(1.145f, 1.04f, 2.79f), new Vector3(0.045f, 0.40f, 0.22f), firebox, false);
            CreateFireplacePrimitive("Ember Base", PrimitiveType.Cube, fireplace,
                new Vector3(0f, 0.875f, 2.78f), new Vector3(2.02f, 0.025f, 0.16f), firebox, false);

            var coals = new GameObject("Irregular Ember Coals").transform;
            coals.SetParent(fireplace, true);
            float[] coalX = { -0.96f, -0.81f, -0.64f, -0.45f, -0.28f, -0.08f, 0.13f, 0.31f, 0.49f, 0.68f, 0.84f, 0.96f };
            for (int index = 0; index < coalX.Length; index++)
            {
                float width = 0.10f + (index % 4) * 0.025f;
                float height = 0.030f + (index % 3) * 0.013f;
                float z = 2.745f + (index % 5) * 0.019f;
                Material coalMaterial = index % 3 == 0 ? firebox : ember;
                CreateFireplacePrimitive(
                    $"Ember Coal {index + 1:00}",
                    PrimitiveType.Sphere,
                    coals,
                    new Vector3(coalX[index], 0.89f + height * 0.45f, z),
                    new Vector3(width, height, 0.075f + (index % 2) * 0.026f),
                    coalMaterial,
                    false
                );
            }
        }

        private static GameObject CreateFireplacePrimitive(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool castShadows)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, true);
            gameObject.transform.SetPositionAndRotation(position, Quaternion.identity);
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

        private static ParticleSystem CreateFlameParticleSystem(
            Transform parent,
            string name,
            Material material,
            float rate,
            Vector2 lifetime,
            Vector2 size,
            Vector2 speed,
            float width,
            Vector3 localPosition,
            float noiseStrength,
            Color startColor,
            Color endColor)
        {
            Transform existing = parent.Find(name);
            GameObject gameObject = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
            {
                gameObject.transform.SetParent(parent, false);
                gameObject.transform.localPosition = localPosition;
                gameObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            ParticleSystem particles = gameObject.GetComponent<ParticleSystem>()
                ?? gameObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 4.2f + Mathf.Abs(gameObject.transform.localPosition.x) * 0.45f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(size.x * 0.72f, size.y * 0.72f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(size.x * 1.25f, size.y * 1.25f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(startColor, endColor);
            main.gravityModifier = 0f;
            main.maxParticles = 220;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(
                rate,
                new AnimationCurve(
                    new Keyframe(0f, 0.72f),
                    new Keyframe(0.24f, 0.94f),
                    new Keyframe(0.52f, 0.80f),
                    new Keyframe(0.78f, 0.98f),
                    new Keyframe(1f, 0.74f)
                )
            );
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, 0.010f, 0.018f);
            shape.randomDirectionAmount = 0f;
            shape.sphericalDirectionAmount = 0f;

            ParticleSystem.TextureSheetAnimationModule sheet = particles.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 16;
            sheet.numTilesY = 5;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.Linear(0f, 0f, 1f, 1f)
            );
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            sheet.cycleCount = 1;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.42f),
                    new Keyframe(0.18f, 1f),
                    new Keyframe(1f, 0.20f)
                )
            );

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = CreateFireFadeGradient();

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = noiseStrength * 0.22f;
            noise.strengthY = noiseStrength * 0.12f;
            noise.strengthZ = noiseStrength;
            noise.frequency = 0.20f + Mathf.Abs(gameObject.transform.localPosition.x) * 0.035f;
            noise.scrollSpeed = 0.055f;
            noise.damping = true;

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.pivot = new Vector3(0f, 0.44f, 0f);
            renderer.allowRoll = false;
            renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.YoungestInFront;
            renderer.sortingFudge = -0.12f;
            return particles;
        }

        private static ParticleSystem CreateSmokeParticleSystem(Transform parent, Material material)
        {
            const string name = "Cinematic Wispy Smoke";
            Transform existing = parent.Find(name);
            GameObject gameObject = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
            {
                gameObject.transform.SetParent(parent, false);
                gameObject.transform.localPosition = new Vector3(-0.08f, 0.10f, 0.035f);
                gameObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            ParticleSystem particles = gameObject.GetComponent<ParticleSystem>()
                ?? gameObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 4f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.13f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.24f, 0.20f, 0.18f, 0.09f),
                new Color(0.12f, 0.10f, 0.10f, 0.04f)
            );
            main.maxParticles = 48;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 1.1f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.12f, 0.012f, 0.025f);
            shape.randomDirectionAmount = 0f;

            ParticleSystem.TextureSheetAnimationModule sheet = particles.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 8;
            sheet.numTilesY = 8;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.Linear(0f, 0f, 1f, 1f)
            );
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            var smokeFade = new Gradient();
            smokeFade.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.48f, 0.44f), 0f),
                    new GradientColorKey(new Color(0.22f, 0.20f, 0.20f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.10f, 0.22f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            color.color = smokeFade;

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = 0.004f;
            noise.strengthY = 0.002f;
            noise.strengthZ = 0.014f;
            noise.frequency = 0.18f;
            noise.scrollSpeed = 0.04f;
            noise.damping = true;

            ParticleSystemRenderer renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.sortingFudge = 0.18f;
            return particles;
        }

        private static Material CreateParticleMaterial(string name, Texture2D texture, bool additive)
        {
            const string folder = "Assets/RealtimeTranslationDemo/Materials/CozyLounge";
            string path = $"{folder}/{name}.mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? throw new InvalidOperationException("URP particle shader was not found.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 1f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat(
                "_DstBlend",
                (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha)
            );
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureNightCityEnvironment(Transform demoRoot)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(NightCityHdriPath);
            bool importerChanged =
                importer.textureShape != TextureImporterShape.Texture2D ||
                importer.mipmapEnabled == false ||
                importer.wrapMode != TextureWrapMode.Repeat ||
                importer.filterMode != FilterMode.Trilinear ||
                importer.maxTextureSize != 4096 ||
                importer.textureCompression != TextureImporterCompression.Uncompressed;
            if (importerChanged)
            {
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Texture2D nightCity = RequireAsset<Texture2D>(NightCityHdriPath);
            Shader panoramic = Shader.Find("Skybox/Panoramic")
                ?? throw new InvalidOperationException("Skybox/Panoramic shader was not found.");
            Material skybox = AssetDatabase.LoadAssetAtPath<Material>(NightCitySkyboxMaterialPath);
            if (skybox == null)
            {
                skybox = new Material(panoramic) { name = "M_PC_ShanghaiBundNight_Skybox" };
                AssetDatabase.CreateAsset(skybox, NightCitySkyboxMaterialPath);
            }
            skybox.shader = panoramic;
            skybox.SetTexture("_MainTex", nightCity);
            skybox.SetColor("_Tint", new Color(0.68f, 0.76f, 0.92f, 1f));
            skybox.SetFloat("_Exposure", 0.32f);
            skybox.SetFloat("_Rotation", 180f);
            EditorUtility.SetDirty(skybox);

            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.02f;
            RenderSettings.reflectionIntensity = 1.15f;

            Transform daylightDome = demoRoot.Find("Environment/Exterior/Daylight Exterior Dome");
            daylightDome.gameObject.SetActive(false);
            Transform skyline = demoRoot.Find("Environment/Exterior/Distant Skyline");
            skyline.gameObject.SetActive(false);

            Light sun = demoRoot.Find("Lighting/Late Morning Sun").GetComponent<Light>();
            sun.color = new Color(0.30f, 0.41f, 0.68f, 1f);
            sun.intensity = 0.15f;
            sun.bounceIntensity = 0.70f;
            sun.transform.rotation = Quaternion.Euler(34f, -18f, 0f);
            RenderSettings.sun = sun;

            SetSceneLight(demoRoot, "Window Key Fill", new Color(0.42f, 0.56f, 0.90f, 1f), 1.35f);
            SetSceneLight(demoRoot, "Soft Interior Fill", new Color(0.90f, 0.86f, 0.82f, 1f), 3.20f);
            SetSceneLight(demoRoot, "Hero Front Fill", new Color(1f, 0.84f, 0.72f, 1f), 5.10f);
            SetSceneLight(demoRoot, "Conversation Bounce", new Color(1f, 0.56f, 0.30f, 1f), 0.82f);
            SetSceneLight(demoRoot, "Signer Teal Rim", new Color(0.32f, 0.62f, 0.88f, 1f), 0.92f);
            SetSceneLight(demoRoot, "Speaker Amber Rim", new Color(1f, 0.54f, 0.25f, 1f), 1.05f);

            DynamicGI.UpdateEnvironment();
        }

        private static void ConfigureRainyWindows(Transform demoRoot)
        {
            Shader rainShader = RequireAsset<Shader>(RainyWindowShaderPath);
            Material rainMaterial = AssetDatabase.LoadAssetAtPath<Material>(RainyWindowMaterialPath);
            if (rainMaterial == null)
            {
                rainMaterial = new Material(rainShader) { name = "M_PC_RainyWindowGlass" };
                AssetDatabase.CreateAsset(rainMaterial, RainyWindowMaterialPath);
            }

            rainMaterial.shader = rainShader;
            rainMaterial.SetColor("_GlassTint", new Color(0.62f, 0.70f, 0.84f, 1f));
            rainMaterial.SetFloat("_BaseOpacity", 0.20f);
            rainMaterial.SetFloat("_DropletDensity", 112f);
            rainMaterial.SetFloat("_StreakDensity", 26f);
            rainMaterial.SetFloat("_FlowSpeed", 0.042f);
            rainMaterial.SetFloat("_Distortion", 0.012f);
            rainMaterial.SetFloat("_NormalStrength", 5.8f);
            rainMaterial.SetFloat("_HighlightStrength", 1.42f);
            rainMaterial.SetFloat("_WetDarkening", 0.12f);
            rainMaterial.enableInstancing = true;
            EditorUtility.SetDirty(rainMaterial);

            Transform windows = demoRoot.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "Curved Daylight Window");
            Renderer[] glassPanels = windows.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.StartsWith("Glass Panel ", StringComparison.Ordinal))
                .ToArray();
            foreach (Renderer glassPanel in glassPanels)
            {
                glassPanel.sharedMaterial = rainMaterial;
                glassPanel.shadowCastingMode = ShadowCastingMode.Off;
                glassPanel.receiveShadows = false;
            }
        }

        private static void SetSceneLight(
            Transform demoRoot,
            string lightName,
            Color color,
            float intensity)
        {
            Light light = demoRoot.Find("Lighting/" + lightName).GetComponent<Light>();
            light.color = color;
            light.intensity = intensity;
            EditorUtility.SetDirty(light);
        }

        private static void ConfigureStaticGlobalIllumination(Transform lounge)
        {
            StaticEditorFlags flags =
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.ReflectionProbeStatic;

            foreach (MeshRenderer renderer in lounge.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
                renderer.receiveGI = ReceiveGI.Lightmaps;
                renderer.scaleInLightmap = renderer.name.Contains("Wall", StringComparison.Ordinal)
                    ? 1.25f
                    : 1f;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void ConfigureLoungeLights(Transform demoRoot, Transform lounge)
        {
            foreach (Light light in lounge.GetComponentsInChildren<Light>(true))
            {
                if (light.name.StartsWith("Fire Glow", StringComparison.Ordinal))
                {
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                    continue;
                }

                light.lightmapBakeType = LightmapBakeType.Mixed;
                if (light.name == "Pendant Warm Downlight")
                {
                    light.intensity = 5.30f;
                    light.shadows = LightShadows.Soft;
                    light.shadowStrength = 0.52f;
                    light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
                }
                else if (light.name == "Pendant Left Fill" || light.name == "Pendant Right Fill")
                {
                    light.intensity = 1.55f;
                }
                EditorUtility.SetDirty(light);
            }

            Light ceilingLeft = demoRoot.Find("Lighting/Ceiling Softbox Left").GetComponent<Light>();
            Light ceilingRight = demoRoot.Find("Lighting/Ceiling Softbox Right").GetComponent<Light>();
            ceilingLeft.intensity = 4.60f;
            ceilingRight.intensity = 4.60f;
            EditorUtility.SetDirty(ceilingLeft);
            EditorUtility.SetDirty(ceilingRight);
        }

        private static void ConfigurePcRendererQuality()
        {
            ScriptableRendererData rendererData = RequireAsset<ScriptableRendererData>(PcRendererPath);
            ScriptableRendererFeature ssao = rendererData.rendererFeatures
                .Single(feature => feature != null && feature.name == "ScreenSpaceAmbientOcclusion");
            var serialized = new SerializedObject(ssao);
            serialized.FindProperty("m_Settings.Downsample").boolValue = false;
            serialized.FindProperty("m_Settings.Source").enumValueIndex = 1;
            serialized.FindProperty("m_Settings.NormalSamples").enumValueIndex = 2;
            serialized.FindProperty("m_Settings.Intensity").floatValue = 0.48f;
            serialized.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.12f;
            serialized.FindProperty("m_Settings.Radius").floatValue = 0.32f;
            serialized.FindProperty("m_Settings.Samples").enumValueIndex = 0;
            serialized.FindProperty("m_Settings.BlurQuality").enumValueIndex = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(true);
            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(rendererData);
        }

        private static void ConfigureCinematicPostProcessing()
        {
            VolumeProfile profile = RequireAsset<VolumeProfile>(VolumeProfilePath);
            ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
            color.active = true;
            color.postExposure.Override(1.08f);
            color.contrast.Override(2f);
            color.saturation.Override(2f);
            color.colorFilter.Override(new Color(1f, 0.975f, 0.94f, 1f));

            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.active = true;
            bloom.highQualityFiltering.Override(true);
            bloom.downscale.Override(BloomDownscaleMode.Half);
            bloom.maxIterations.Override(7);
            bloom.intensity.Override(0.38f);
            bloom.threshold.Override(0.84f);

            DepthOfField depthOfField = GetOrAdd<DepthOfField>(profile);
            depthOfField.active = true;
            depthOfField.highQualitySampling.Override(true);
            depthOfField.focusDistance.Override(6.2f);
            depthOfField.aperture.Override(8f);

            FilmGrain grain = GetOrAdd<FilmGrain>(profile);
            grain.active = true;
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.075f);
            grain.response.Override(0.78f);

            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.Override(0.07f);
            vignette.smoothness.Override(0.40f);

            foreach (VolumeComponent component in profile.components)
            {
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(profile);
        }

        private static void ConfigureReflectionProbes(Transform demoRoot)
        {
            foreach (ReflectionProbe probe in demoRoot.GetComponentsInChildren<ReflectionProbe>(true))
            {
                probe.mode = ReflectionProbeMode.Baked;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.resolution = 512;
                probe.hdr = true;
                probe.boxProjection = true;
                probe.importance = 2;
                probe.gameObject.SetActive(true);
                EditorUtility.SetDirty(probe);
            }
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return profile.TryGet(out T component) ? component : profile.Add<T>(true);
        }

        private static void ConfigureFlameFlipbookImport()
        {
            string[] paths =
            {
                VfxSampleRoot + "/Textures/Fire/Flame03_Temp_16x5.png",
                VfxSampleRoot + "/Textures/Smoke/WispySmoke03b_8x8.png",
            };
            foreach (string path in paths)
            {
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool changed =
                    importer.alphaSource != TextureImporterAlphaSource.FromGrayScale ||
                    !importer.alphaIsTransparency ||
                    importer.mipmapEnabled ||
                    importer.wrapMode != TextureWrapMode.Clamp;
                if (!changed)
                {
                    continue;
                }

                importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static Gradient CreateFireFadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.72f, 0.70f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            return gradient;
        }

        private static void RequireDemoScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before configuring the PC demo.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {DemoScenePath} before configuring the PC demo. Current scene: {scene.path}"
                );
            }
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null
                ? asset
                : throw new InvalidOperationException($"Required asset was not found: {path}");
        }
    }
}
