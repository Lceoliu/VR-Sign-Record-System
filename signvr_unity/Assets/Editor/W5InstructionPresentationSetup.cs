using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Meta.XR.Movement.Retargeting;
using SignVR.Interaction.Core;
using SignVR.Interaction.Presentation;
using SignVR.Recording;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// W5-only, idempotent wiring for the W4 name-based anchors. This tool marks
    /// the open scene dirty but deliberately never saves InteractionLab.
    /// </summary>
    public static class W5InstructionPresentationSetup
    {
        private const string GhostRigName = "InstructionGhostRig";
        private const string GhostPrefabPath =
            "Packages/com.meta.xr.sdk.movement/Shared/Prefabs/Character/" +
            "StylizedCharacter.prefab";

        [MenuItem(
            "Tools/SignVR/Interaction/W5 Configure Instruction Presentation (Unsaved)"
        )]
        public static void ConfigureOpenSceneFromMenu()
        {
            ConfigureOpenSceneForAutomation();
        }

        [MenuItem(
            "Tools/SignVR/Interaction/W5 Validate Instruction Presentation"
        )]
        public static void ValidateFromMenu()
        {
            ValidateConfiguredSceneForAutomation();
        }

        /// <summary>
        /// Adds/repairs W5 objects in the currently open InteractionLab and
        /// leaves the result unsaved for Orchestrator review.
        /// </summary>
        public static void ConfigureOpenSceneForAutomation()
        {
            Scene scene = SceneManager.GetActiveScene();
            RequireInteractionScene(scene);

            Transform runtimeAnchor = RequirePath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.RuntimeSystemsAnchorName
            );
            string anchorsRoot = InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName + "/";
            Transform signerAnchor = RequirePath(
                scene,
                anchorsRoot + "InstructionSignerAnchor"
            );
            Transform bubbleAnchor = RequirePath(
                scene,
                anchorsRoot + "InstructionBubbleAnchor"
            );
            Transform uiAnchor = RequirePath(
                scene,
                anchorsRoot + "InteractionUiAnchor"
            );

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure W5 Instruction Presentation");
            try
            {
                GameObject ghostRig = EnsureGhostRig(scene, signerAnchor);
                CharacterRetargeter retargeter =
                    ghostRig.GetComponentInChildren<CharacterRetargeter>(true);
                if (retargeter == null)
                {
                    throw new InvalidOperationException(
                        $"{GhostPrefabPath} contains no CharacterRetargeter."
                    );
                }

                InstructionGhostPlayer player = GetOrAdd<InstructionGhostPlayer>(
                    signerAnchor.gameObject
                );
                GhostPointingDetector detector = GetOrAdd<GhostPointingDetector>(
                    signerAnchor.gameObject
                );
                InteractionTargetHighlightVisual highlight =
                    GetOrAdd<InteractionTargetHighlightVisual>(
                        signerAnchor.gameObject
                    );
                InteractionPromptPresenter prompt =
                    GetOrAdd<InteractionPromptPresenter>(
                        bubbleAnchor.gameObject
                    );
                InteractionInstructionControls controls =
                    GetOrAdd<InteractionInstructionControls>(
                        uiAnchor.gameObject
                    );
                InstructionPresentationController controller =
                    GetOrAdd<InstructionPresentationController>(
                        runtimeAnchor.gameObject
                    );

                Camera hmdCamera = FindSceneHmdCamera(scene);
                Transform hmd = hmdCamera != null
                    ? hmdCamera.transform
                    : null;
                GhostPointingTargetBinding[] targetBindings =
                    BuildTargetBindings(scene);

                Undo.RecordObjects(
                    new UnityEngine.Object[]
                    {
                        player,
                        detector,
                        highlight,
                        prompt,
                        controls,
                        controller
                    },
                    "Wire W5 Instruction Presentation"
                );
                player.Configure(retargeter);
                detector.ConfigurePlayer(player);
                detector.ConfigureHighlight(highlight);
                detector.ConfigureTargetBindings(targetBindings);
                if (!detector.TryConfigureFingerBones(ghostRig.transform))
                {
                    Debug.LogWarning(
                        "[W5InstructionPresentationSetup] Could not resolve " +
                        "distal-to-tip index bones from the signer prefab. " +
                        "Assign them before saving.",
                        detector
                    );
                }
                prompt.Configure(signerAnchor, hmd);
                controls.ConfigureHmd(hmd);
                controller.Configure(player, prompt, detector, controls);

                EditorUtility.SetDirty(player);
                EditorUtility.SetDirty(detector);
                EditorUtility.SetDirty(highlight);
                EditorUtility.SetDirty(prompt);
                EditorUtility.SetDirty(controls);
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(scene);

                ValidateSourceContractForAutomation();
                ValidateLoadedScene(scene, requireSavedScene: false);
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            Debug.Log(
                "[W5InstructionPresentationSetup] W5 wiring is valid in the " +
                "open InteractionLab. The scene remains deliberately unsaved."
            );
        }

        public static void ValidateSourceContractForAutomation()
        {
            var failures = new List<string>();
            string[] conditionNames = Enum.GetNames(
                typeof(AssistanceCondition)
            );
            string[] expectedConditions =
            {
                "TextAndPointing",
                "TextOnly",
                "SignOnly"
            };
            if (!conditionNames.SequenceEqual(expectedConditions) ||
                conditionNames.Contains("PointingOnly"))
            {
                failures.Add(
                    "AssistanceCondition is not the frozen three-value enum."
                );
            }

            RecordingSentence[] sentences =
                RecordingPointingSentenceCatalog.CreateSentences();
            if (sentences.Length != PhaseSentenceRanges.TotalSentenceCount ||
                sentences.Any(sentence =>
                    sentence == null ||
                    string.IsNullOrWhiteSpace(sentence.SentenceId) ||
                    string.IsNullOrWhiteSpace(sentence.Text)))
            {
                failures.Add(
                    "The reused Recorder sentence catalog is not complete 001-031."
                );
            }

            try
            {
                BuildLogicalToSceneTargetMap();
            }
            catch (Exception exception)
            {
                failures.Add(
                    "Task Variant to Recorder target mapping is inconsistent: " +
                    exception.Message
                );
            }

            ValidatePlayerApi(failures);
            foreach (Type type in typeof(InstructionGhostPlayer).Assembly
                         .GetTypes()
                         .Where(candidate => candidate.Namespace ==
                             "SignVR.Interaction.Presentation"))
            {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Instance |
                             BindingFlags.Public |
                             BindingFlags.NonPublic))
                {
                    if ((field.FieldType.FullName ?? string.Empty).Contains(
                            "RecordingCoordinator"))
                    {
                        failures.Add(
                            $"{type.Name}.{field.Name} depends on " +
                            "RecordingCoordinator."
                        );
                    }
                }
            }

            ThrowIfInvalid("W5 source contract", failures);
        }

        public static void ValidateConfiguredSceneForAutomation()
        {
            ValidateSourceContractForAutomation();
            Scene loaded = SceneManager.GetSceneByPath(
                InteractionLabContract.ScenePath
            );
            if (loaded.IsValid() && loaded.isLoaded)
            {
                ValidateLoadedScene(loaded, requireSavedScene: true);
                return;
            }

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(
                    InteractionLabContract.ScenePath,
                    OpenSceneMode.Single
                );
                ValidateLoadedScene(scene, requireSavedScene: true);
            }
            finally
            {
                RestoreSceneSetupSafely(setup);
            }
        }

        private static void ValidateLoadedScene(
            Scene scene,
            bool requireSavedScene)
        {
            var failures = new List<string>();
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal))
            {
                failures.Add("InteractionLab is not the loaded validation scene.");
                ThrowIfInvalid("W5 scene wiring", failures);
            }
            if (requireSavedScene && scene.isDirty)
            {
                failures.Add(
                    "InteractionLab has unsaved W5 changes; validate after " +
                    "Orchestrator review and save."
                );
            }

            string anchorsRoot = InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.AnchorsRootName + "/";
            Transform runtimeAnchor = TryFindPath(
                scene,
                InteractionLabContract.SceneRootName + "/" +
                InteractionLabContract.RuntimeSystemsAnchorName
            );
            Transform signerAnchor = TryFindPath(
                scene,
                anchorsRoot + "InstructionSignerAnchor"
            );
            Transform bubbleAnchor = TryFindPath(
                scene,
                anchorsRoot + "InstructionBubbleAnchor"
            );
            Transform uiAnchor = TryFindPath(
                scene,
                anchorsRoot + "InteractionUiAnchor"
            );
            if (runtimeAnchor == null || signerAnchor == null ||
                bubbleAnchor == null || uiAnchor == null)
            {
                failures.Add("One or more W4 W5 anchor paths are missing.");
                ThrowIfInvalid("W5 scene wiring", failures);
            }

            InstructionPresentationController controller =
                RequireSingleAt<InstructionPresentationController>(
                    scene,
                    runtimeAnchor,
                    failures
                );
            InstructionGhostPlayer player = RequireSingleAt<InstructionGhostPlayer>(
                scene,
                signerAnchor,
                failures
            );
            GhostPointingDetector detector = RequireSingleAt<GhostPointingDetector>(
                scene,
                signerAnchor,
                failures
            );
            InteractionTargetHighlightVisual highlight =
                RequireSingleAt<InteractionTargetHighlightVisual>(
                    scene,
                    signerAnchor,
                    failures
                );
            InteractionPromptPresenter prompt =
                RequireSingleAt<InteractionPromptPresenter>(
                    scene,
                    bubbleAnchor,
                    failures
                );
            InteractionInstructionControls controls =
                RequireSingleAt<InteractionInstructionControls>(
                    scene,
                    uiAnchor,
                    failures
                );

            Transform ghostRig = signerAnchor.Find(GhostRigName);
            if (ghostRig == null)
            {
                failures.Add($"{GhostRigName} is missing below signer anchor.");
            }
            if (player != null && (player.Retargeter == null ||
                ghostRig == null || !player.Retargeter.transform.IsChildOf(
                    ghostRig)))
            {
                failures.Add(
                    "InstructionGhostPlayer is not bound to the independent " +
                    "signer rig CharacterRetargeter."
                );
            }
            if (detector != null && !detector.HasCompleteFingerRig)
            {
                failures.Add(
                    "GhostPointingDetector lacks left/right index distal-tip bones."
                );
            }

            Dictionary<string, string> expectedTargets =
                BuildLogicalToSceneTargetMap();
            if (detector != null)
            {
                IReadOnlyList<GhostPointingTargetBinding> bindings =
                    detector.TargetBindings;
                var boundIds = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < bindings.Count; index++)
                {
                    GhostPointingTargetBinding binding = bindings[index];
                    if (binding == null || binding.TargetRoot == null ||
                        !boundIds.Add(binding.TargetId))
                    {
                        failures.Add(
                            "Pointing target bindings contain null or duplicates."
                        );
                        break;
                    }
                }
                if (!boundIds.SetEquals(expectedTargets.Keys))
                {
                    failures.Add(
                        "Pointing bindings do not cover every W1 logical target."
                    );
                }
            }

            if (prompt != null && prompt.SignerRoot != signerAnchor)
            {
                failures.Add(
                    "InteractionPromptPresenter must follow the stable signer " +
                    "root, not a head bone."
                );
            }
            if (ghostRig != null && bubbleAnchor.IsChildOf(ghostRig))
            {
                failures.Add(
                    "InstructionBubbleAnchor must not be parented into the " +
                    "animated signer skeleton."
                );
            }
            if (controls != null && (controls.ReplayButton == null ||
                controls.GiveUpButton == null || controls.AbortButton == null ||
                controls.GiveUpButton == controls.AbortButton))
            {
                failures.Add(
                    "Replay, Give Up Phase, and Abort Run controls are not " +
                    "three distinct controls."
                );
            }
            if (controller != null && (controller.GhostPlayer != player ||
                controller.PromptPresenter != prompt ||
                controller.PointingDetector != detector))
            {
                failures.Add(
                    "InstructionPresentationController references are incomplete."
                );
            }
            if (highlight == null)
            {
                failures.Add("Actual-hit target highlight is missing.");
            }

            ThrowIfInvalid("W5 scene wiring", failures);
            Debug.Log(
                "[W5InstructionPresentationSetup] W5 saved scene wiring is valid."
            );
        }

        private static void ValidatePlayerApi(ICollection<string> failures)
        {
            Type type = typeof(InstructionGhostPlayer);
            if (type.GetMethod(
                    "Load",
                    new[] { typeof(RunPhasePlan) }) == null ||
                type.GetMethod("Play", Type.EmptyTypes)?.ReturnType !=
                    typeof(bool) ||
                type.GetMethod("Replay", Type.EmptyTypes)?.ReturnType !=
                    typeof(bool) ||
                type.GetMethod("Stop", Type.EmptyTypes)?.ReturnType !=
                    typeof(void) ||
                type.GetEvent("Completed") == null)
            {
                failures.Add(
                    "InstructionGhostPlayer lacks Load/Play/Completed/Stop/Replay API."
                );
            }
        }

        private static GameObject EnsureGhostRig(
            Scene scene,
            Transform signerAnchor)
        {
            Transform existing = signerAnchor.Find(GhostRigName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                GhostPrefabPath
            );
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Meta Movement signer prefab is missing: {GhostPrefabPath}."
                );
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(
                prefab,
                scene
            ) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the independent signer prefab."
                );
            }

            Undo.RegisterCreatedObjectUndo(instance, "Create Instruction Ghost Rig");
            Undo.SetTransformParent(
                instance.transform,
                signerAnchor,
                "Parent Instruction Ghost Rig"
            );
            instance.name = GhostRigName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static GhostPointingTargetBinding[] BuildTargetBindings(
            Scene scene)
        {
            Dictionary<string, string> mapping =
                BuildLogicalToSceneTargetMap();
            var result = new List<GhostPointingTargetBinding>(mapping.Count);
            foreach (KeyValuePair<string, string> pair in mapping.OrderBy(
                         candidate => candidate.Key,
                         StringComparer.Ordinal))
            {
                Transform target = ResolveSceneTarget(scene, pair.Value);
                if (target == null)
                {
                    throw new InvalidOperationException(
                        $"Scene target '{pair.Value}' for logical ID " +
                        $"'{pair.Key}' is missing."
                    );
                }
                result.Add(new GhostPointingTargetBinding(pair.Key, target));
            }
            return result.ToArray();
        }

        private static Dictionary<string, string>
            BuildLogicalToSceneTargetMap()
        {
            RecordingSentence[] sentences =
                RecordingPointingSentenceCatalog.CreateSentences();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TaskVariant variant in TaskVariantCatalog.All)
            {
                int sentenceIndex =
                    PhaseSentenceRanges.ParseSentenceNumber(
                        variant.SentenceId
                    ) - 1;
                RecordingSentence sentence = sentences[sentenceIndex];
                string expectedRecordingId =
                    $"sentence_{variant.SentenceId}";
                if (sentence == null || !string.Equals(
                        sentence.SentenceId,
                        expectedRecordingId,
                        StringComparison.Ordinal) ||
                    sentence.HighlightTargetIds.Length !=
                        variant.TargetIds.Count)
                {
                    throw new InvalidOperationException(
                        $"Sentence {variant.SentenceId} target arity or identity " +
                        "does not match the canonical Recorder catalog."
                    );
                }

                for (int index = 0; index < variant.TargetIds.Count; index++)
                {
                    string logicalId = variant.TargetIds[index];
                    string sceneId = sentence.HighlightTargetIds[index];
                    if (result.TryGetValue(
                            logicalId,
                            out string existingSceneId) &&
                        !string.Equals(
                            existingSceneId,
                            sceneId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Logical target '{logicalId}' maps to both " +
                            $"'{existingSceneId}' and '{sceneId}'."
                        );
                    }
                    result[logicalId] = sceneId;
                }
            }
            return result;
        }

        private static Transform ResolveSceneTarget(Scene scene, string path)
        {
            string[] segments = path.Split('/');
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(
                candidate => string.Equals(
                    candidate.name,
                    segments[0],
                    StringComparison.Ordinal
                )
            );
            return segments.Length == 1
                ? root?.transform
                : root?.transform.Find(string.Join("/", segments.Skip(1)));
        }

        private static Camera FindSceneHmdCamera(Scene scene)
        {
            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include
            );
            Camera first = null;
            for (int index = 0; index < cameras.Length; index++)
            {
                if (cameras[index].gameObject.scene != scene)
                {
                    continue;
                }
                first ??= cameras[index];
                if (cameras[index].CompareTag("MainCamera"))
                {
                    return cameras[index];
                }
            }
            return first;
        }

        private static T GetOrAdd<T>(GameObject gameObject)
            where T : Component
        {
            return gameObject.GetComponent<T>() ?? Undo.AddComponent<T>(gameObject);
        }

        private static T RequireSingleAt<T>(
            Scene scene,
            Transform expectedAnchor,
            ICollection<string> failures)
            where T : Component
        {
            T[] components = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
            if (components.Length != 1)
            {
                failures.Add(
                    $"Expected exactly one {typeof(T).Name}, found " +
                    $"{components.Length}."
                );
                return components.FirstOrDefault();
            }
            if (components[0].transform != expectedAnchor)
            {
                failures.Add(
                    $"{typeof(T).Name} is not attached to " +
                    $"{expectedAnchor.name}."
                );
            }
            return components[0];
        }

        private static Transform RequirePath(Scene scene, string path)
        {
            return TryFindPath(scene, path) ?? throw new InvalidOperationException(
                $"Required W4 anchor is missing: {path}."
            );
        }

        private static Transform TryFindPath(Scene scene, string path)
        {
            string[] segments = path.Split('/');
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(
                candidate => string.Equals(
                    candidate.name,
                    segments[0],
                    StringComparison.Ordinal
                )
            );
            return segments.Length == 1
                ? root?.transform
                : root?.transform.Find(string.Join("/", segments.Skip(1)));
        }

        private static void RequireInteractionScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Open {InteractionLabContract.ScenePath} before running " +
                    "the W5 setup."
                );
            }
        }

        private static void RestoreSceneSetupSafely(SceneSetup[] setup)
        {
            if (setup != null && setup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            else
            {
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single
                );
            }
        }

        private static void ThrowIfInvalid(
            string label,
            IReadOnlyCollection<string> failures)
        {
            if (failures.Count == 0)
            {
                return;
            }
            throw new InvalidOperationException(
                $"{label} failed:{Environment.NewLine}- " +
                string.Join(Environment.NewLine + "- ", failures)
            );
        }
    }
}
