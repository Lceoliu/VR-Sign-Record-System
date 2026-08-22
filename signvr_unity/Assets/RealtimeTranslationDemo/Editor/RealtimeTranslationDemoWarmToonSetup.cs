using System;
using Meta.XR.Movement.Retargeting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SignVR.Demo.Editor
{
    public static class RealtimeTranslationDemoWarmToonSetup
    {
        private const string DemoScenePath =
            "Assets/RealtimeTranslationDemo/Scenes/RealtimeTranslationDemo.unity";
        private const string DemoRootPath = "_RealtimeTranslationDemo";
        private const string AnriSourceMaterialFolder =
            "Assets/RealtimeTranslationDemo/AvatarCandidates/Anri/Materials";
        private const string AnriDemoMaterialFolder =
            "Assets/RealtimeTranslationDemo/Materials/Characters/ANRI";
        private const string VolumeProfilePath =
            "Assets/RealtimeTranslationDemo/Settings/RealtimeTranslationDemoVolume.asset";
        private const string AvatarPreviewScenePath =
            "Assets/RealtimeTranslationDemo/Scenes/AvatarPreview.unity";
        private const string AnriDemoPrefabFolder =
            "Assets/RealtimeTranslationDemo/Prefabs/Characters";
        private const string AnriDemoPrefabPath =
            AnriDemoPrefabFolder + "/ANRI_DemoCharacter.prefab";
        private const string CharactersPath = "Characters (Replace These)";
        private const string SignerAnchorPath = "Replacement Anchors/Signer Avatar Root";
        private const string HearingAnchorPath = "Replacement Anchors/Hearing Avatar Root";
        private const string TranslationVisualizationName = "Translation Visualization";
        private const string SignerMannequinName = "Deaf Signer Mannequin";
        private const string HearingMannequinName = "Hearing Speaker Mannequin";
        private const string AnriSignerName = "ANRI - Deaf Signer";
        private const string AsteTSpeakerName = "ASTeT - Hearing Speaker";
        private const string AsteTModelPath =
            "Assets/RealtimeTranslationDemo/AvatarCandidates/ASTeT/ASTeT.fbx";
        private const string AsteTMaterialFolder =
            "Assets/RealtimeTranslationDemo/AvatarCandidates/ASTeT/Materials";

        private static readonly string[] AnriMaterialNames =
        {
            "Anri_Body_URP",
            "Anri_Eyes_URP",
            "Anri_Hair_URP",
            "Anri_Wear_URP",
            "Anri_Cardigan_URP",
            "Anri_Sub_URP",
            "Anri_Swimwear_URP",
            "Anri_Boots_URP",
        };

        [MenuItem("SignVR/Demo/Apply Warm VRChat Toon Look")]
        public static void ApplyWarmVrChatToonLook()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {DemoScenePath} before applying the warm Toon look. " +
                    $"Current scene: {scene.path}"
                );
            }

            EnsureAssetFolder(AnriDemoMaterialFolder);
            foreach (string materialName in AnriMaterialNames)
            {
                CreateOrRefreshAnriDemoMaterial(materialName);
            }

            ConfigureWarmEnvironment();
            ConfigureWarmLights();
            ConfigureWarmCinematicVolume();

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[RealtimeTranslationDemo] Warm VRChat Toon look applied. " +
                "ANRI received isolated UTS3 demo materials; the Recording scene and " +
                "mobile render pipeline were not modified."
            );
        }

        [MenuItem("SignVR/Demo/Create ANRI Demo Character Prefab")]
        public static void CreateAnriDemoCharacterPrefab()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != AvatarPreviewScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {AvatarPreviewScenePath} before creating the ANRI demo prefab. " +
                    $"Current scene: {scene.path}"
                );
            }

            GameObject source = GameObject.Find("ANRI_Preview")
                ?? throw new InvalidOperationException("ANRI_Preview was not found.");
            EnsureAssetFolder(AnriDemoMaterialFolder);
            foreach (string materialName in AnriMaterialNames)
            {
                CreateOrRefreshAnriDemoMaterial(materialName);
            }
            EnsureAssetFolder(AnriDemoPrefabFolder);

            GameObject clone = UnityEngine.Object.Instantiate(source);
            try
            {
                clone.name = "ANRI_DemoCharacter";
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clone.transform.localScale = Vector3.one;

                SignVR.Demo.RecordedMetaPoseProvider recordedProvider =
                    clone.GetComponent<SignVR.Demo.RecordedMetaPoseProvider>();
                if (recordedProvider != null)
                {
                    UnityEngine.Object.DestroyImmediate(recordedProvider);
                }

                CharacterRetargeter previewRetargeter = clone.GetComponent<CharacterRetargeter>();
                if (previewRetargeter != null)
                {
                    UnityEngine.Object.DestroyImmediate(previewRetargeter);
                }

                Animator animator = clone.GetComponent<Animator>()
                    ?? throw new MissingComponentException("ANRI has no Animator component.");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                AssignWarmAnriMaterials(clone);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, AnriDemoPrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save ANRI demo prefab: {AnriDemoPrefabPath}"
                    );
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }

            AssignWarmAnriMaterials(source);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeObject = RequireAsset<GameObject>(AnriDemoPrefabPath);
            Debug.Log(
                "[RealtimeTranslationDemo] Created ANRI_DemoCharacter.prefab with attached " +
                "hair/kemomimi and isolated warm UTS3 materials. Role placement remains explicit."
            );
        }

        [MenuItem("SignVR/Demo/Install ANRI As Left Signer")]
        public static void InstallAnriAsLeftSigner()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {DemoScenePath} before installing ANRI. Current scene: {scene.path}"
                );
            }

            Transform demoRoot = GameObject.Find(DemoRootPath)?.transform
                ?? throw new InvalidOperationException($"Demo root was not found: {DemoRootPath}");
            Transform characters = demoRoot.Find(CharactersPath)
                ?? throw new InvalidOperationException($"Character root was not found: {CharactersPath}");
            Transform signerAnchor = characters.Find(SignerAnchorPath)
                ?? throw new InvalidOperationException($"Signer anchor was not found: {SignerAnchorPath}");

            Transform translationVisualization = demoRoot.Find(TranslationVisualizationName);
            if (translationVisualization != null)
            {
                Undo.DestroyObjectImmediate(translationVisualization.gameObject);
            }

            Transform mannequin = characters.Find(SignerMannequinName);
            if (mannequin != null)
            {
                Undo.DestroyObjectImmediate(mannequin.gameObject);
            }

            Transform existingSigner = characters.Find(AnriSignerName);
            if (existingSigner != null)
            {
                Undo.DestroyObjectImmediate(existingSigner.gameObject);
            }

            GameObject prefab = RequireAsset<GameObject>(AnriDemoPrefabPath);
            GameObject signer = (GameObject)PrefabUtility.InstantiatePrefab(prefab, characters);
            Undo.RegisterCreatedObjectUndo(signer, "Install ANRI As Left Signer");
            signer.name = AnriSignerName;
            signer.transform.SetPositionAndRotation(
                signerAnchor.position,
                Quaternion.Euler(0f, 128f, 0f)
            );
            signer.transform.localScale = Vector3.one;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = signer;
            Debug.Log(
                "[RealtimeTranslationDemo] Removed Translation Visualization and installed " +
                "ANRI as the left-side deaf signer."
            );
        }

        [MenuItem("SignVR/Demo/Install ASTeT As Right Hearing Speaker")]
        public static void InstallAsteTAsRightHearingSpeaker()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {DemoScenePath} before installing ASTeT. Current scene: {scene.path}"
                );
            }

            Transform demoRoot = GameObject.Find(DemoRootPath)?.transform
                ?? throw new InvalidOperationException($"Demo root was not found: {DemoRootPath}");
            Transform characters = demoRoot.Find(CharactersPath)
                ?? throw new InvalidOperationException($"Character root was not found: {CharactersPath}");
            Transform hearingAnchor = characters.Find(HearingAnchorPath)
                ?? throw new InvalidOperationException($"Hearing anchor was not found: {HearingAnchorPath}");

            Transform mannequin = characters.Find(HearingMannequinName);
            if (mannequin != null)
            {
                Undo.DestroyObjectImmediate(mannequin.gameObject);
            }

            Transform existingSpeaker = characters.Find(AsteTSpeakerName);
            GameObject speaker;
            if (existingSpeaker == null)
            {
                GameObject model = RequireAsset<GameObject>(AsteTModelPath);
                speaker = (GameObject)PrefabUtility.InstantiatePrefab(model, characters);
                Undo.RegisterCreatedObjectUndo(speaker, "Install ASTeT As Right Hearing Speaker");
                speaker.name = AsteTSpeakerName;
            }
            else
            {
                speaker = existingSpeaker.gameObject;
                Undo.RecordObject(speaker.transform, "Align ASTeT Hearing Speaker");
            }

            speaker.transform.SetPositionAndRotation(
                hearingAnchor.position,
                Quaternion.Euler(0f, 232f, 0f)
            );
            speaker.transform.localScale = Vector3.one;
            AssignAsteTMaterials(speaker);

            Animator animator = speaker.GetComponent<Animator>()
                ?? throw new MissingComponentException("ASTeT has no Animator component.");
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(animator);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = speaker;
            Debug.Log(
                "[RealtimeTranslationDemo] Installed ASTeT as the right-side hearing speaker " +
                "using the existing UTS Toon materials."
            );
        }

        private static void AssignAsteTMaterials(GameObject characterRoot)
        {
            foreach (Renderer renderer in characterRoot.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index]
                        ?? throw new InvalidOperationException(
                            $"{renderer.name} contains an empty ASTeT material slot."
                        );
                    materials[index] = ResolveAsteTMaterial(source.name);
                }

                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static Material ResolveAsteTMaterial(string sourceName)
        {
            string targetName;
            if (sourceName.Contains("Atex1", StringComparison.OrdinalIgnoreCase))
            {
                targetName = "ASTeT_Atex1_Toon";
            }
            else if (sourceName.Contains("Atex2", StringComparison.OrdinalIgnoreCase))
            {
                targetName = "ASTeT_Atex2_Toon";
            }
            else if (sourceName.Contains("Atex3", StringComparison.OrdinalIgnoreCase))
            {
                targetName = "ASTeT_Atex3_Toon";
            }
            else if (sourceName.Contains("Back", StringComparison.OrdinalIgnoreCase))
            {
                targetName = "ASTeT_Back_Toon";
            }
            else if (sourceName.Contains("jem", StringComparison.OrdinalIgnoreCase))
            {
                targetName = "ASTeT_jem_Toon";
            }
            else if (sourceName.Contains("Metal", StringComparison.OrdinalIgnoreCase))
            {
                targetName = "ASTeT_Metal_Toon";
            }
            else
            {
                throw new InvalidOperationException(
                    $"No UTS Toon material mapping exists for ASTeT material: {sourceName}"
                );
            }

            return RequireAsset<Material>($"{AsteTMaterialFolder}/{targetName}.mat");
        }

        private static void AssignWarmAnriMaterials(GameObject characterRoot)
        {
            foreach (Renderer renderer in characterRoot.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material sourceMaterial = materials[index]
                        ?? throw new InvalidOperationException(
                            $"{renderer.name} contains an empty material slot."
                        );
                    string warmName = sourceMaterial.name.Replace("_URP", "_WarmToon");
                    materials[index] = RequireAsset<Material>(
                        $"{AnriDemoMaterialFolder}/{warmName}.mat"
                    );
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void CreateOrRefreshAnriDemoMaterial(string materialName)
        {
            string sourcePath = $"{AnriSourceMaterialFolder}/{materialName}.mat";
            string targetName = materialName.Replace("_URP", "_WarmToon");
            string targetPath = $"{AnriDemoMaterialFolder}/{targetName}.mat";
            Material source = RequireAsset<Material>(sourcePath);

            if (source.shader == null || source.shader.name != "Toon/Toon")
            {
                throw new InvalidOperationException(
                    $"ANRI source material must use Unity Toon Shader: {sourcePath}"
                );
            }

            Material target = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
            if (target == null)
            {
                if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                {
                    throw new InvalidOperationException($"Could not create {targetPath}.");
                }
                target = RequireAsset<Material>(targetPath);
            }
            else
            {
                Undo.RecordObject(target, "Refresh ANRI Warm Toon Material");
                EditorUtility.CopySerialized(source, target);
            }

            target.name = targetName;
            ConfigureAnriToonMaterial(target, materialName);
            EditorUtility.SetDirty(target);
        }

        private static void ConfigureAnriToonMaterial(Material material, string sourceName)
        {
            bool isEyes = sourceName.Contains("Eyes", StringComparison.Ordinal);
            bool isSkin = sourceName.Contains("Body", StringComparison.Ordinal) || isEyes;
            bool isHair = sourceName.Contains("Hair", StringComparison.Ordinal);

            material.SetFloat("_BaseColor_Step", 0.56f);
            material.SetFloat("_BaseShade_Feather", isSkin ? 0.075f : 0.045f);
            material.SetFloat("_ShadeColor_Step", 0.24f);
            material.SetFloat("_1st2nd_Shades_Feather", isSkin ? 0.06f : 0.035f);
            material.SetFloat("_Set_SystemShadowsToBase", 1f);
            material.SetFloat("_Tweak_SystemShadowsLevel", 0.035f);
            material.SetFloat("_Is_Filter_LightColor", 1f);
            material.SetFloat("_Unlit_Intensity", 0.78f);

            material.SetFloat("_Is_BlendBaseColor", 1f);
            material.SetFloat("_Is_LightColor_Outline", 0f);
            material.SetColor("_Outline_Color", new Color(0.105f, 0.065f, 0.075f, 1f));
            material.SetFloat("_Outline_Width", GetOutlineWidth(sourceName));
            material.SetFloat("_Nearest_Distance", 1.2f);
            material.SetFloat("_Farthest_Distance", 14f);

            material.SetFloat("_RimLight", isHair ? 1f : 0f);
            material.SetColor(
                "_RimLightColor",
                new Color(0.55f, 0.34f, 0.24f, 1f)
            );
            material.SetFloat("_RimLight_Power", 0.82f);
            material.SetFloat("_RimLight_InsideMask", 0.45f);
            material.SetFloat("_RimLight_FeatherOff", 0f);
            material.SetFloat("_Is_LightColor_RimLight", 0f);
            material.SetFloat("_Tweak_RimLightMaskLevel", -0.45f);

            if (isSkin)
            {
                material.SetColor(
                    "_1st_ShadeColor",
                    new Color(0.92f, 0.79f, 0.77f, 1f)
                );
                material.SetColor(
                    "_2nd_ShadeColor",
                    new Color(0.70f, 0.50f, 0.53f, 1f)
                );
            }
            else if (isHair)
            {
                material.SetColor(
                    "_1st_ShadeColor",
                    new Color(0.90f, 0.81f, 0.75f, 1f)
                );
                material.SetColor(
                    "_2nd_ShadeColor",
                    new Color(0.61f, 0.47f, 0.49f, 1f)
                );
            }
            else
            {
                material.SetColor(
                    "_1st_ShadeColor",
                    new Color(0.84f, 0.78f, 0.83f, 1f)
                );
                material.SetColor(
                    "_2nd_ShadeColor",
                    new Color(0.54f, 0.44f, 0.57f, 1f)
                );
            }

            material.enableInstancing = true;
        }

        private static float GetOutlineWidth(string sourceName)
        {
            if (sourceName.Contains("Eyes", StringComparison.Ordinal))
            {
                return 0f;
            }
            if (sourceName.Contains("Body", StringComparison.Ordinal))
            {
                return 0.45f;
            }
            if (sourceName.Contains("Hair", StringComparison.Ordinal))
            {
                return 0.90f;
            }
            if (
                sourceName.Contains("Wear", StringComparison.Ordinal)
                || sourceName.Contains("Cardigan", StringComparison.Ordinal)
            )
            {
                return 0.82f;
            }
            return 0.68f;
        }

        private static void ConfigureWarmEnvironment()
        {
            Material exterior = RequireAsset<Material>(
                "Assets/RealtimeTranslationDemo/Materials/M_Demo_DaylightExterior.mat"
            );
            Undo.RecordObject(exterior, "Warm Demo Exterior");
            exterior.SetFloat("_EmissionStrength", 0.82f);
            exterior.SetColor("_Tint", new Color(0.95f, 0.89f, 0.78f, 1f));
            EditorUtility.SetDirty(exterior);

            RenderSettings.ambientIntensity = 1.02f;
            RenderSettings.reflectionIntensity = 1.02f;
            RenderSettings.ambientSkyColor = new Color(0.68f, 0.66f, 0.62f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.44f, 0.38f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.18f, 0.14f, 1f);
        }

        private static void ConfigureWarmLights()
        {
            ConfigureLight(
                "Late Morning Sun",
                new Color(1f, 0.87f, 0.73f, 1f),
                1.38f
            );
            ConfigureLight(
                "Window Key Fill",
                new Color(1f, 0.84f, 0.70f, 1f),
                3.55f
            );
            ConfigureLight(
                "Soft Interior Fill",
                new Color(0.78f, 0.86f, 0.85f, 1f),
                2.05f
            );
            ConfigureLight(
                "Hero Front Fill",
                new Color(1f, 0.82f, 0.70f, 1f),
                1.58f
            );
            ConfigureLight(
                "Conversation Bounce",
                new Color(1f, 0.76f, 0.60f, 1f),
                0.92f
            );
            ConfigureLight(
                "Signer Teal Rim",
                new Color(0.55f, 0.74f, 0.70f, 1f),
                0.66f
            );
            ConfigureLight(
                "Speaker Amber Rim",
                new Color(1f, 0.70f, 0.47f, 1f),
                0.62f
            );
        }

        private static void ConfigureWarmCinematicVolume()
        {
            VolumeProfile profile = RequireAsset<VolumeProfile>(VolumeProfilePath);
            ColorAdjustments color = RequireVolumeOverride<ColorAdjustments>(profile);
            WhiteBalance whiteBalance = RequireVolumeOverride<WhiteBalance>(profile);
            Bloom bloom = RequireVolumeOverride<Bloom>(profile);
            Vignette vignette = RequireVolumeOverride<Vignette>(profile);

            Undo.RecordObjects(
                new UnityEngine.Object[] { profile, color, whiteBalance, bloom, vignette },
                "Warm Demo Color Grade"
            );
            color.postExposure.Override(0.36f);
            color.contrast.Override(4f);
            color.saturation.Override(2f);
            color.colorFilter.Override(new Color(1f, 0.975f, 0.94f, 1f));
            whiteBalance.temperature.Override(8f);
            whiteBalance.tint.Override(1f);
            bloom.intensity.Override(0.24f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.48f);
            vignette.intensity.Override(0.08f);
            vignette.smoothness.Override(0.40f);

            EditorUtility.SetDirty(color);
            EditorUtility.SetDirty(whiteBalance);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(vignette);
            EditorUtility.SetDirty(profile);
        }

        private static void ConfigureLight(string name, Color color, float intensity)
        {
            Transform transform = RequireSceneTransform($"{DemoRootPath}/Lighting/{name}");
            Light light = transform.GetComponent<Light>()
                ?? throw new MissingComponentException($"{transform.name} has no Light component.");
            Undo.RecordObject(light, "Configure Warm Demo Light");
            light.color = color;
            light.intensity = intensity;
            EditorUtility.SetDirty(light);
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

        private static Transform RequireSceneTransform(string path)
        {
            GameObject target = GameObject.Find(path);
            return target != null
                ? target.transform
                : throw new InvalidOperationException($"Scene object not found: {path}");
        }

        private static T RequireAsset<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null
                ? asset
                : throw new InvalidOperationException($"Asset not found: {path}");
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
}
