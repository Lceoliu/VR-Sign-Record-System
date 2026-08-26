using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// Creates InteractionLab as a Unity-authored copy of VRroom, then removes
    /// Recorder-owned responsibilities and installs stable name-based anchors.
    /// Updating an existing InteractionLab preserves unknown components and
    /// children (including later Interaction Core work) while reapplying the
    /// clean-scene contract.
    /// </summary>
    public static class InteractionLabSceneTool
    {
        [MenuItem(
            "Tools/SignVR/Interaction/Generate or Update InteractionLab"
        )]
        public static void GenerateOrUpdateSceneFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            GenerateOrUpdateSceneForAutomation();
        }

        /// <summary>
        /// Batchmode entry:
        /// -executeMethod SignVR.Editor.Interaction.InteractionLabSceneTool.
        /// GenerateOrUpdateSceneForAutomation
        /// </summary>
        public static void GenerateOrUpdateSceneForAutomation()
        {
            EnsureLoadedScenesAreSaved();
            SceneSetup[] originalSetup =
                EditorSceneManager.GetSceneManagerSetup();
            EnsureSceneAssetsExist();

            bool created = false;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    InteractionLabContract.ScenePath
                ) == null)
            {
                if (!AssetDatabase.CopyAsset(
                        InteractionLabContract.RecorderScenePath,
                        InteractionLabContract.ScenePath
                    ))
                {
                    throw new InvalidOperationException(
                        $"Unity could not copy " +
                        $"{InteractionLabContract.RecorderScenePath} to " +
                        $"{InteractionLabContract.ScenePath}."
                    );
                }

                created = true;
                AssetDatabase.ImportAsset(
                    InteractionLabContract.ScenePath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate
                );
            }

            Scene scene = default;
            try
            {
                scene = EditorSceneManager.OpenScene(
                    InteractionLabContract.ScenePath,
                    OpenSceneMode.Single
                );
                bool changed = SanitizeRecordingResponsibilities(scene);
                changed |= EnsureInteractionContract(scene);

                // Validate the complete in-memory result before it is allowed
                // to replace the saved scene. The saved-scene validator runs
                // again below after persistence.
                InteractionLabValidator.ValidateLoadedSceneForGeneration(scene);

                if (created || changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                    {
                        throw new InvalidOperationException(
                            $"Unity could not save " +
                            $"{InteractionLabContract.ScenePath}."
                        );
                    }
                }

                InteractionLabValidator.ValidateLoadedScene(scene);
                Debug.Log(
                    $"[InteractionLabSceneTool] " +
                    $"{(created ? "Generated" : "Updated")} " +
                    $"{InteractionLabContract.ScenePath}; changed={changed}."
                );
            }
            catch (Exception generationFailure)
            {
                try
                {
                    if (created)
                    {
                        // The copied scene did not exist before this attempt.
                        // Restore the caller's scene layout before deleting the
                        // failed generated asset.
                        RestoreSceneManagerSetupSafely(originalSetup);
                        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                                InteractionLabContract.ScenePath
                            ) != null &&
                            !AssetDatabase.DeleteAsset(
                                InteractionLabContract.ScenePath
                            ))
                        {
                            throw new InvalidOperationException(
                                $"Unity could not remove failed generated scene " +
                                $"{InteractionLabContract.ScenePath}."
                            );
                        }
                    }
                    else if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    {
                        // Existing Interaction work is restored from its last
                        // valid saved state; unsaved sanitizer mutations never
                        // survive a failed pre-save validation.
                        EditorSceneManager.OpenScene(
                            InteractionLabContract.ScenePath,
                            OpenSceneMode.Single
                        );
                    }
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "InteractionLab generation failed and cleanup was incomplete.",
                        generationFailure,
                        cleanupFailure
                    );
                }

                throw;
            }
        }

        private static void EnsureSceneAssetsExist()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    InteractionLabContract.RecorderScenePath
                ) == null)
            {
                throw new InvalidOperationException(
                    $"Recorder source scene is missing: " +
                    $"{InteractionLabContract.RecorderScenePath}."
                );
            }
        }

        private static bool SanitizeRecordingResponsibilities(Scene scene)
        {
            bool changed = false;
            GameObject[] gameObjects = EnumerateGameObjects(scene).ToArray();

            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject == null ||
                    !InteractionLabContract.RecordingOwnedObjectNames.Contains(
                        gameObject.name
                    ))
                {
                    continue;
                }

                Object.DestroyImmediate(gameObject);
                changed = true;
            }

            gameObjects = EnumerateGameObjects(scene).ToArray();
            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject == null)
                {
                    continue;
                }

                Component[] components = gameObject.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (component == null || component is Transform ||
                        !ShouldRemoveComponent(component))
                    {
                        continue;
                    }

                    Debug.Log(
                        $"[InteractionLabSceneTool] Removing " +
                        $"{component.GetType().FullName} from " +
                        $"{GetHierarchyPath(gameObject.transform)}."
                    );
                    Object.DestroyImmediate(component);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ShouldRemoveComponent(Component component)
        {
            Type type = component.GetType();
            string fullName = type.FullName ?? type.Name;
            string typeNamespace = type.Namespace ?? string.Empty;
            bool isStreamer = type.Name.IndexOf(
                "Streamer",
                StringComparison.OrdinalIgnoreCase
            ) >= 0;

            return typeNamespace.Equals(
                       "SignVR.Recording",
                       StringComparison.Ordinal
                   ) ||
                   typeNamespace.StartsWith(
                       "SignVR.Recording.",
                       StringComparison.Ordinal
                   ) ||
                   InteractionLabContract.ForbiddenComponentTypeNames.Contains(
                       fullName
                   ) ||
                   (isStreamer && UsesLegacyStreamerPort(component));
        }

        internal static bool UsesLegacyStreamerPort(Component component)
        {
            if (component == null)
            {
                return false;
            }

            try
            {
                var serializedObject = new SerializedObject(component);
                SerializedProperty property = serializedObject.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.name.IndexOf(
                            "port",
                            StringComparison.OrdinalIgnoreCase
                        ) < 0)
                    {
                        continue;
                    }

                    if (property.propertyType ==
                            SerializedPropertyType.Integer &&
                        property.intValue ==
                            InteractionLabContract.LegacyStreamerPort)
                    {
                        return true;
                    }

                    if (property.propertyType ==
                            SerializedPropertyType.String &&
                        string.Equals(
                            property.stringValue,
                            InteractionLabContract.LegacyStreamerPort.ToString(),
                            StringComparison.Ordinal
                        ))
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[InteractionLabSceneTool] Could not inspect serialized " +
                    $"ports on {component.GetType().FullName}: " +
                    exception.Message
                );
            }

            return false;
        }

        private static bool EnsureInteractionContract(Scene scene)
        {
            bool changed = false;
            GameObject[] matchingRoots = scene.GetRootGameObjects()
                .Where(root => root.name == InteractionLabContract.SceneRootName)
                .ToArray();
            if (matchingRoots.Length > 1)
            {
                throw new InvalidOperationException(
                    $"{InteractionLabContract.ScenePath} contains multiple " +
                    $"{InteractionLabContract.SceneRootName} roots."
                );
            }

            GameObject sceneRoot;
            if (matchingRoots.Length == 0)
            {
                sceneRoot = new GameObject(
                    InteractionLabContract.SceneRootName
                );
                SceneManager.MoveGameObjectToScene(sceneRoot, scene);
                ResetLocalTransform(sceneRoot.transform);
                changed = true;
            }
            else
            {
                sceneRoot = matchingRoots[0];
            }

            Transform runtimeAnchor = EnsureDirectChild(
                sceneRoot.transform,
                InteractionLabContract.RuntimeSystemsAnchorName,
                ref changed
            );
            Transform anchorsRoot = EnsureDirectChild(
                sceneRoot.transform,
                InteractionLabContract.AnchorsRootName,
                ref changed
            );

            // RuntimeSystemsAnchor is intentionally empty. Parallel work may
            // attach Interaction Core components after worktree integration.
            _ = runtimeAnchor;

            bool participantAnchorWasMissing = !Enumerable
                .Range(0, anchorsRoot.childCount)
                .Select(anchorsRoot.GetChild)
                .Any(child => child.name == "ParticipantSpawnAnchor");
            Transform participantAnchor = EnsureDirectChild(
                anchorsRoot,
                "ParticipantSpawnAnchor",
                ref changed
            );
            EnsureDirectChild(
                anchorsRoot,
                "InstructionSignerAnchor",
                ref changed
            );
            EnsureDirectChild(
                anchorsRoot,
                "InstructionBubbleAnchor",
                ref changed
            );
            EnsureDirectChild(
                anchorsRoot,
                "PhaseContentAnchor",
                ref changed
            );
            EnsureDirectChild(
                anchorsRoot,
                "InteractionUiAnchor",
                ref changed
            );
            EnsureDirectChild(
                anchorsRoot,
                "ExperimentCaptureAnchor",
                ref changed
            );

            if (participantAnchorWasMissing &&
                IsDefaultTransform(participantAnchor) &&
                TryFindByName(scene, "PlayerSpawnPoint", out GameObject spawn))
            {
                participantAnchor.SetPositionAndRotation(
                    spawn.transform.position,
                    spawn.transform.rotation
                );
            }

            changed |= EnsureFixedStudyWorldFrame(scene);

            return changed;
        }

        private static bool EnsureFixedStudyWorldFrame(Scene scene)
        {
            Component[] playerRigs = EnumerateGameObjects(scene)
                .SelectMany(gameObject => gameObject.GetComponents<Component>())
                .Where(
                    component => component != null &&
                                 string.Equals(
                                     component.GetType().Name,
                                     "VRPlayerRig",
                                     StringComparison.Ordinal
                                 )
                )
                .ToArray();
            if (playerRigs.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one VRPlayerRig while configuring " +
                    $"the fixed InteractionLab world frame; found " +
                    $"{playerRigs.Length}."
                );
            }

            var serializedPlayer = new SerializedObject(playerRigs[0]);
            SerializedProperty recordingMode =
                serializedPlayer.FindProperty("recordingMode");
            SerializedProperty preserveRuntimeTrackingOrigin =
                serializedPlayer.FindProperty("preserveRuntimeTrackingOrigin");
            if (recordingMode == null || preserveRuntimeTrackingOrigin == null)
            {
                throw new InvalidOperationException(
                    "VRPlayerRig no longer exposes its serialized " +
                    "fixed-study world-frame contract."
                );
            }
            bool changed = false;
            if (!recordingMode.boolValue)
            {
                recordingMode.boolValue = true;
                changed = true;
            }
            if (!preserveRuntimeTrackingOrigin.boolValue)
            {
                preserveRuntimeTrackingOrigin.boolValue = true;
                changed = true;
            }
            if (!changed)
            {
                return false;
            }
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static Transform EnsureDirectChild(
            Transform parent,
            string name,
            ref bool changed)
        {
            Transform[] matches = Enumerable.Range(0, parent.childCount)
                .Select(parent.GetChild)
                .Where(child => child.name == name)
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"{GetHierarchyPath(parent)} contains multiple direct " +
                    $"children named {name}."
                );
            }

            if (matches.Length == 1)
            {
                return matches[0];
            }

            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            ResetLocalTransform(child.transform);
            changed = true;
            return child.transform;
        }

        private static void ResetLocalTransform(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private static bool IsDefaultTransform(Transform transform)
        {
            return transform.localPosition == Vector3.zero &&
                   transform.localRotation == Quaternion.identity &&
                   transform.localScale == Vector3.one;
        }

        private static bool TryFindByName(
            Scene scene,
            string name,
            out GameObject match)
        {
            match = EnumerateGameObjects(scene)
                .FirstOrDefault(gameObject => gameObject.name == name);
            return match != null;
        }

        internal static IEnumerable<GameObject> EnumerateGameObjects(
            Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    yield return transform.gameObject;
                }
            }
        }

        internal static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }

        internal static void RestoreSceneManagerSetupSafely(SceneSetup[] setup)
        {
            bool hasRestorableActiveScene = setup != null &&
                setup.Count(item => item.isLoaded && item.isActive) == 1;
            bool allLoadedScenesAreSaved = hasRestorableActiveScene &&
                setup
                    .Where(item => item.isLoaded)
                    .All(item => !string.IsNullOrEmpty(item.path));

            if (allLoadedScenesAreSaved)
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                return;
            }

            // The EditMode Test Runner can temporarily expose no loaded scene,
            // while Unity's RestoreSceneManagerSetup requires exactly one
            // active scene. Restore an equivalent neutral editor state instead.
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );
        }

        private static void EnsureLoadedScenesAreSaved()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty)
                {
                    throw new InvalidOperationException(
                        $"Save or discard changes in scene '{scene.name}' " +
                        "before generating InteractionLab."
                    );
                }
            }
        }
    }
}
