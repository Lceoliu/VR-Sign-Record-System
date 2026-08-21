using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Meta.XR.Movement;
using Meta.XR.Movement.Editor;
using Meta.XR.Movement.Retargeting;
using Meta.XR.Movement.Retargeting.Editor;
using SignVR.Demo;
using TMPro;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SignVR.Demo.Editor
{
    public static class AvatarPreviewMetaPoseSetup
    {
        private const string ScenePath =
            "Assets/RealtimeTranslationDemo/Scenes/AvatarPreview.unity";
        private const string MetadataPath =
            "Assets/RealtimeTranslationDemo/Preview/PoseSamples/take_001.meta.json";
        private const string PosePath =
            "Assets/RealtimeTranslationDemo/Preview/PoseSamples/take_001.pose.bytes";
        private const string DriverRoot = "ANRI_Preview";

        private static readonly (string SceneRoot, string AssetPath)[] Avatars =
        {
            ("HAOLAN_Preview", "Assets/RealtimeTranslationDemo/AvatarCandidates/HAOLAN.fbx"),
            ("Nemu_Preview", "Assets/RealtimeTranslationDemo/AvatarCandidates/nemu.fbx"),
            ("ANRI_Preview", "Assets/RealtimeTranslationDemo/AvatarCandidates/Anri/Anri_v1.fbx"),
            ("Mike_Preview", "Assets/RealtimeTranslationDemo/AvatarCandidates/Mike/Mike.fbx"),
            ("ASTeT_Preview", "Assets/RealtimeTranslationDemo/AvatarCandidates/ASTeT/ASTeT.fbx"),
            ("Alu_Preview", "Assets/RealtimeTranslationDemo/AvatarCandidates/Alu/Alu_Ver2.02.fbx"),
        };

        [MenuItem("SignVR/Demo/Setup Avatar Preview Meta Pose Playback")]
        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before running Meta Pose setup. Current scene: {scene.path}"
                );
            }

            TextAsset metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(MetadataPath);
            TextAsset pose = AssetDatabase.LoadAssetAtPath<TextAsset>(PosePath);
            if (metadata == null || pose == null)
            {
                throw new InvalidOperationException(
                    $"Pose sample assets are missing. Expected {MetadataPath} and {PosePath}."
                );
            }

            AvatarPreviewMetaPosePlayer player = EnsurePlayer();
            (Button button, TMP_Text label) = EnsureUi();
            GameObject driver = GameObject.Find(DriverRoot);
            string driverAssetPath = Avatars.Single(avatar =>
                avatar.SceneRoot == DriverRoot
            ).AssetPath;
            GameObject driverAsset = AssetDatabase.LoadAssetAtPath<GameObject>(driverAssetPath);
            if (driver == null || driverAsset == null)
            {
                throw new InvalidOperationException(
                    $"Cannot configure the Meta Pose driver {DriverRoot}."
                );
            }

            Animator driverAnimator = driver.GetComponent<Animator>();
            MSDKUtilityEditorMetadata retargetingMetadata =
                MSDKUtilityEditor.RunDefaultRetargetingSetup(driverAsset);
            TextAsset retargetingConfig = retargetingMetadata != null
                ? retargetingMetadata.ConfigJson
                : null;
            if (retargetingConfig == null)
            {
                throw new InvalidOperationException(
                    $"Meta Movement could not generate a retargeting config for {DriverRoot}."
                );
            }

            RecordedMetaPoseProvider provider =
                GetOrAddComponent<RecordedMetaPoseProvider>(driver);
            CharacterRetargeter retargeter =
                GetOrAddComponent<CharacterRetargeter>(driver);
            Undo.RecordObject(retargeter, "Configure Meta Pose Driver");
            retargeter.ConfigAsset = retargetingConfig;
            CharacterRetargeterConfigEditor.LoadConfig(
                new SerializedObject(retargeter),
                retargeter
            );
            retargeter.SkeletonRetargeter.ApplyRootScale = false;
            retargeter.enabled = true;
            provider.Configure(player);
            EditorUtility.SetDirty(provider);
            EditorUtility.SetDirty(retargeter);

            var followers = new List<Animator>(Avatars.Length - 1);

            foreach ((string sceneRoot, string assetPath) in Avatars)
            {
                GameObject character = GameObject.Find(sceneRoot);
                if (character == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot configure {sceneRoot}; model asset: {assetPath}."
                    );
                }

                Animator animator = character.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    throw new InvalidOperationException($"{sceneRoot} is not a valid Humanoid.");
                }

                Undo.RecordObject(animator, "Configure Preview Animator");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                EditorUtility.SetDirty(animator);

                if (sceneRoot == DriverRoot)
                {
                    continue;
                }

                RecordedMetaPoseProvider oldProvider =
                    character.GetComponent<RecordedMetaPoseProvider>();
                CharacterRetargeter oldRetargeter =
                    character.GetComponent<CharacterRetargeter>();
                if (oldRetargeter != null)
                {
                    Undo.DestroyObjectImmediate(oldRetargeter);
                }
                if (oldProvider != null)
                {
                    Undo.DestroyObjectImmediate(oldProvider);
                }
                followers.Add(animator);
            }

            AvatarPreviewHumanoidPoseBroadcaster broadcaster =
                GetOrAddComponent<AvatarPreviewHumanoidPoseBroadcaster>(player.gameObject);
            broadcaster.Configure(driverAnimator, followers.ToArray());
            EditorUtility.SetDirty(broadcaster);

            player.Configure(
                metadata,
                pose,
                new[] { provider },
                new[] { retargeter },
                button,
                label
            );
            EditorUtility.SetDirty(player);

            button.onClick.RemoveAllListeners();
            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }
            UnityEventTools.AddPersistentListener(button.onClick, player.PlayOrRestart);
            EditorUtility.SetDirty(button);

            EnsureAnriAccessoriesFollowHead();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player.gameObject;
            Debug.Log(
                $"[AvatarPreviewPoseSetup] Configured {DriverRoot} as the Meta Pose driver " +
                $"with {followers.Count} Unity Humanoid followers."
            );
        }

        private static TextAsset BuildHumanoidRetargetingConfig(
            GameObject character,
            GameObject modelAsset,
            string configName)
        {
            const string configFolder =
                "Assets/RealtimeTranslationDemo/Preview/RetargetingConfigs";
            EnsureAssetFolder(configFolder);

            SkeletonData sourceData =
                MSDKUtilityEditor.FindSourceSkeletonData("OVRSkeletonData");
            if (sourceData == null)
            {
                throw new InvalidOperationException(
                    "Meta Movement OVRSkeletonData could not be loaded."
                );
            }

            GameObject configCharacter = UnityEngine.Object.Instantiate(modelAsset);
            configCharacter.name = modelAsset.name;
            configCharacter.hideFlags = HideFlags.HideAndDontSave;
            configCharacter.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            string configJson = string.Empty;
            string skeletonDiagnostics = string.Empty;
            try
            {
                configJson = CreateConfigForHierarchy(
                    configCharacter,
                    configCharacter.GetComponent<Animator>(),
                    sourceData,
                    configName,
                    out skeletonDiagnostics
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configCharacter);
            }

            if (string.IsNullOrEmpty(configJson))
            {
                GameObject proxyRoot = BuildHumanoidProxy(character);
                try
                {
                    configJson = CreateConfigForHierarchy(
                        proxyRoot,
                        character.GetComponent<Animator>(),
                        sourceData,
                        configName,
                        out skeletonDiagnostics
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(proxyRoot);
                }
            }

            if (string.IsNullOrEmpty(configJson))
            {
                throw new InvalidOperationException(
                    $"Meta Movement could not generate a Humanoid retargeting config for " +
                    $"{configName}: {skeletonDiagnostics}"
                );
            }

            string configPath = $"{configFolder}/{configName}.json";
            string absolutePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", configPath)
            );
            File.WriteAllText(absolutePath, configJson, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                configPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
            );

            TextAsset configAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(configPath);
            if (configAsset == null)
            {
                throw new InvalidOperationException(
                    $"Generated retargeting config could not be imported: {configPath}."
                );
            }

            return configAsset;
        }

        private static string CreateConfigForHierarchy(
            GameObject hierarchyRoot,
            Animator animator,
            SkeletonData sourceData,
            string configName,
            out string diagnostics)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            string rootName = hips.parent != null ? hips.parent.name : hierarchyRoot.name;
            SkeletonData targetData = SkeletonData.CreateFromTransform(hierarchyRoot.transform);
            int invalidTransforms = targetData.TPoseArray.Count(transform =>
                float.IsNaN(transform.Position.x) ||
                float.IsNaN(transform.Position.y) ||
                float.IsNaN(transform.Position.z) ||
                float.IsInfinity(transform.Position.x) ||
                float.IsInfinity(transform.Position.y) ||
                float.IsInfinity(transform.Position.z) ||
                float.IsNaN(transform.Orientation.x) ||
                float.IsNaN(transform.Orientation.y) ||
                float.IsNaN(transform.Orientation.z) ||
                float.IsNaN(transform.Orientation.w) ||
                float.IsInfinity(transform.Orientation.x) ||
                float.IsInfinity(transform.Orientation.y) ||
                float.IsInfinity(transform.Orientation.z) ||
                float.IsInfinity(transform.Orientation.w)
            );
            string knownJoints = string.Join(
                ", ",
                BuildKnownJointNames(animator, rootName).Select((joint, index) =>
                    $"{(MSDKUtility.KnownJointType)index}={joint}"
                )
            );
            diagnostics =
                $"target joints={targetData.JointCount}, invalid transforms={invalidTransforms}, " +
                $"known joints: {knownJoints}";
            Debug.Log($"[AvatarPreviewPoseSetup] {configName}: {diagnostics}");

            return CreateRetargetingConfig(
                sourceData,
                targetData,
                animator,
                rootName,
                configName
            );
        }

        private static string CreateRetargetingConfig(
            SkeletonData sourceData,
            SkeletonData targetData,
            Animator animator,
            string rootName,
            string configName)
        {
            NativeArray<MSDKUtility.JointMapping> minMappings = new(
                0,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            NativeArray<MSDKUtility.JointMappingEntry> minEntries = new(
                0,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            NativeArray<MSDKUtility.JointMapping> maxMappings = new(
                0,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            NativeArray<MSDKUtility.JointMappingEntry> maxEntries = new(
                0,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );

            MSDKUtility.SkeletonInitParams targetInit = targetData.FillConfigInitParams();
            targetInit.BlendShapeNames = Array.Empty<string>();
            targetInit.OptionalKnownSourceJointNamesById = BuildKnownJointNames(
                animator,
                rootName
            );
            var initParams = new MSDKUtility.ConfigInitParams
            {
                SourceSkeleton = sourceData.FillConfigInitParams(),
                TargetSkeleton = targetInit,
            };
            initParams.MinMappings.Mappings = minMappings;
            initParams.MinMappings.MappingEntries = minEntries;
            initParams.MaxMappings.Mappings = maxMappings;
            initParams.MaxMappings.MappingEntries = maxEntries;

            if (!MSDKUtility.CreateOrUpdateUtilityConfig(
                    configName,
                    initParams,
                    out ulong configHandle))
            {
                return string.Empty;
            }

            try
            {
                if (!MSDKUtility.AlignTargetToSource(
                        configName,
                        MSDKUtility.AlignmentFlags.All,
                        configHandle,
                        MSDKUtility.SkeletonType.SourceSkeleton,
                        configHandle,
                        out configHandle) ||
                    !MSDKUtility.GenerateMappings(
                        configHandle,
                        MSDKUtility.AutoMappingFlags.EmptyFlag) ||
                    !MSDKUtility.WriteConfigDataToJson(configHandle, out string configJson))
                {
                    return string.Empty;
                }

                return configJson;
            }
            finally
            {
                MSDKUtility.DestroyHandle(configHandle);
            }
        }

        private static string[] BuildKnownJointNames(Animator animator, string rootName)
        {
            var knownJoints = new string[(int)MSDKUtility.KnownJointType.KnownJointCount];
            knownJoints[(int)MSDKUtility.KnownJointType.Root] = rootName;
            knownJoints[(int)MSDKUtility.KnownJointType.Hips] =
                GetBoneName(animator, HumanBodyBones.Hips);
            knownJoints[(int)MSDKUtility.KnownJointType.RightUpperArm] =
                GetBoneName(animator, HumanBodyBones.RightUpperArm);
            knownJoints[(int)MSDKUtility.KnownJointType.LeftUpperArm] =
                GetBoneName(animator, HumanBodyBones.LeftUpperArm);
            knownJoints[(int)MSDKUtility.KnownJointType.RightWrist] =
                GetBoneName(animator, HumanBodyBones.RightHand);
            knownJoints[(int)MSDKUtility.KnownJointType.LeftWrist] =
                GetBoneName(animator, HumanBodyBones.LeftHand);
            knownJoints[(int)MSDKUtility.KnownJointType.Chest] =
                GetBoneName(animator, HumanBodyBones.Chest, HumanBodyBones.Spine);
            knownJoints[(int)MSDKUtility.KnownJointType.Neck] =
                GetBoneName(animator, HumanBodyBones.Neck, HumanBodyBones.Head);
            knownJoints[(int)MSDKUtility.KnownJointType.RightUpperLeg] =
                GetBoneName(animator, HumanBodyBones.RightUpperLeg);
            knownJoints[(int)MSDKUtility.KnownJointType.LeftUpperLeg] =
                GetBoneName(animator, HumanBodyBones.LeftUpperLeg);
            knownJoints[(int)MSDKUtility.KnownJointType.RightAnkle] =
                GetBoneName(animator, HumanBodyBones.RightFoot);
            knownJoints[(int)MSDKUtility.KnownJointType.LeftAnkle] =
                GetBoneName(animator, HumanBodyBones.LeftFoot);
            return knownJoints;
        }

        private static string GetBoneName(
            Animator animator,
            HumanBodyBones bone,
            HumanBodyBones fallback = HumanBodyBones.LastBone)
        {
            Transform transform = animator.GetBoneTransform(bone);
            if (transform == null && fallback != HumanBodyBones.LastBone)
            {
                transform = animator.GetBoneTransform(fallback);
            }
            if (transform == null)
            {
                throw new InvalidOperationException(
                    $"{animator.name} has no mapped {bone} Humanoid bone."
                );
            }
            return transform.name;
        }

        private static GameObject BuildHumanoidProxy(GameObject character)
        {
            Animator animator = character.GetComponent<Animator>();
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                throw new InvalidOperationException($"{character.name} has no Humanoid hips bone.");
            }

            var sourceBones = new List<Transform>();
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                Transform sourceBone = animator.GetBoneTransform(bone);
                if (sourceBone != null && !sourceBones.Contains(sourceBone))
                {
                    sourceBones.Add(sourceBone);
                }
            }

            string duplicateName = sourceBones
                .GroupBy(bone => bone.name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (duplicateName != null)
            {
                throw new InvalidOperationException(
                    $"{character.name} maps multiple Humanoid bones to '{duplicateName}'."
                );
            }

            sourceBones.Sort((left, right) => GetDepth(left).CompareTo(GetDepth(right)));

            string rootName = hips.parent != null ? hips.parent.name : character.name;
            var proxyRoot = new GameObject(rootName)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var proxies = new Dictionary<Transform, Transform>(sourceBones.Count);

            foreach (Transform sourceBone in sourceBones)
            {
                Transform sourceParent = sourceBone.parent;
                while (sourceParent != null && !proxies.ContainsKey(sourceParent))
                {
                    sourceParent = sourceParent.parent;
                }

                Transform proxyParent = sourceParent != null
                    ? proxies[sourceParent]
                    : proxyRoot.transform;
                var proxyBone = new GameObject(sourceBone.name)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                proxyBone.transform.SetParent(proxyParent, false);
                proxyBone.transform.position = character.transform.InverseTransformPoint(
                    sourceBone.position
                );
                proxyBone.transform.rotation =
                    Quaternion.Inverse(character.transform.rotation) * sourceBone.rotation;
                proxies.Add(sourceBone, proxyBone.transform);
            }

            return proxyRoot;
        }

        private static int GetDepth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string current = "Assets";
            foreach (string segment in folderPath.Split('/').Skip(1))
            {
                string next = $"{current}/{segment}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segment);
                }
                current = next;
            }
        }

        private static AvatarPreviewMetaPosePlayer EnsurePlayer()
        {
            GameObject root = GameObject.Find("AvatarPreviewPosePlayback");
            if (root == null)
            {
                root = new GameObject("AvatarPreviewPosePlayback");
                Undo.RegisterCreatedObjectUndo(root, "Create Meta Pose Playback");
            }

            return GetOrAddComponent<AvatarPreviewMetaPosePlayer>(root);
        }

        private static (Button Button, TMP_Text Label) EnsureUi()
        {
            GameObject canvasObject = GameObject.Find("AvatarPreviewPoseUI");
            if (canvasObject == null)
            {
                canvasObject = new GameObject(
                    "AvatarPreviewPoseUI",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster)
                );
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create Meta Pose UI");
            }

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Transform existingButton = canvasObject.transform.Find("PlayMetaPoseButton");
            GameObject buttonObject;
            if (existingButton == null)
            {
                buttonObject = new GameObject(
                    "PlayMetaPoseButton",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button),
                    typeof(Outline)
                );
                Undo.RegisterCreatedObjectUndo(buttonObject, "Create Meta Pose Button");
                buttonObject.transform.SetParent(canvasObject.transform, false);
            }
            else
            {
                buttonObject = existingButton.gameObject;
            }

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 42f);
            buttonRect.sizeDelta = new Vector2(280f, 56f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.055f, 0.075f, 0.105f, 0.96f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.72f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(0.42f, 0.82f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Outline outline = buttonObject.GetComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.82f, 0.92f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            Transform existingLabel = buttonObject.transform.Find("Label");
            GameObject labelObject;
            if (existingLabel == null)
            {
                labelObject = new GameObject(
                    "Label",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI)
                );
                Undo.RegisterCreatedObjectUndo(labelObject, "Create Meta Pose Button Label");
                labelObject.transform.SetParent(buttonObject.transform, false);
            }
            else
            {
                labelObject = existingLabel.gameObject;
            }

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "PLAY META POSE";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 19f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.9f, 0.98f, 1f, 1f);
            label.raycastTarget = false;

            EnsureEventSystem();
            EditorUtility.SetDirty(canvasObject);
            EditorUtility.SetDirty(buttonObject);
            EditorUtility.SetDirty(labelObject);
            return (button, label);
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject eventObject = new GameObject(
                    "AvatarPreviewPoseEventSystem",
                    typeof(EventSystem),
                    typeof(InputSystemUIInputModule)
                );
                Undo.RegisterCreatedObjectUndo(eventObject, "Create Meta Pose EventSystem");
                eventSystem = eventObject.GetComponent<EventSystem>();
            }

            InputSystemUIInputModule inputModule =
                eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = Undo.AddComponent<InputSystemUIInputModule>(eventSystem.gameObject);
            }
            inputModule.AssignDefaultActions();
            EditorUtility.SetDirty(inputModule);
        }

        private static void EnsureAnriAccessoriesFollowHead()
        {
            GameObject anri = GameObject.Find("ANRI_Preview");
            Animator animator = anri != null ? anri.GetComponent<Animator>() : null;
            Transform head = animator != null
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            if (head == null)
            {
                throw new InvalidOperationException("ANRI Head bone was not found.");
            }

            AttachAccessory("ANRI_Hair_Preview", anri.transform, head);
            AttachAccessory("ANRI_Kemomimi_Preview", anri.transform, head);
        }

        private static void AttachAccessory(
            string accessoryName,
            Transform characterRoot,
            Transform head)
        {
            Transform accessory = FindSceneTransform(accessoryName);
            if (accessory == null)
            {
                throw new InvalidOperationException($"{accessoryName} was not found.");
            }

            if (accessory.parent == head)
            {
                return;
            }

            Undo.RecordObject(accessory, "Align ANRI Accessory");
            accessory.SetPositionAndRotation(characterRoot.position, characterRoot.rotation);
            Undo.SetTransformParent(accessory, head, "Attach ANRI Accessory To Head");
        }

        private static Transform FindSceneTransform(string objectName)
        {
            Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
            foreach (Transform candidate in transforms)
            {
                if (candidate.name == objectName && candidate.gameObject.scene.IsValid())
                {
                    return candidate;
                }
            }

            return null;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target);
        }
    }
}
