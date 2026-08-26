using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SignVR.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// Validates the product/build seam and the saved InteractionLab scene
    /// without referencing types owned by the parallel Interaction Core work.
    /// </summary>
    public static class InteractionLabValidator
    {
        [MenuItem("Tools/SignVR/Interaction/Validate Build and Scene Contract")]
        public static void ValidateAllFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ValidateAllForAutomation();
        }

        /// <summary>
        /// Batchmode entry:
        /// -executeMethod SignVR.Editor.Interaction.InteractionLabValidator.
        /// ValidateAllForAutomation
        /// </summary>
        public static void ValidateAllForAutomation()
        {
            ValidateBuildContractForAutomation();
            ValidateSceneForAutomation();
            Debug.Log(
                "[InteractionLabValidator] Dual-build and InteractionLab " +
                "contracts are valid."
            );
        }

        public static void ValidateBuildContractForAutomation()
        {
            var failures = new List<string>();

            ValidateEntryPoint(
                nameof(CommandLineBuild.BuildRecorderAndroid),
                failures
            );
            ValidateEntryPoint(
                nameof(CommandLineBuild.BuildInteractionAndroid),
                failures
            );

            string[] recorderScenes = CommandLineBuild.GetScenesForValidation(
                SignVRProduct.Recorder
            );
            string[] interactionScenes = CommandLineBuild.GetScenesForValidation(
                SignVRProduct.Interaction
            );
            ValidateSingleScene(
                "Recorder",
                recorderScenes,
                InteractionLabContract.RecorderScenePath,
                failures
            );
            ValidateSingleScene(
                "Interaction",
                interactionScenes,
                InteractionLabContract.ScenePath,
                failures
            );

            string recorderScene = recorderScenes.Length == 1
                ? recorderScenes[0]
                : null;
            string interactionScene = interactionScenes.Length == 1
                ? interactionScenes[0]
                : null;
            if (recorderScene != null &&
                string.Equals(
                    recorderScene,
                    interactionScene,
                    StringComparison.Ordinal
                ))
            {
                failures.Add(
                    "Recorder and Interaction build entries resolve to the " +
                    "same scene."
                );
            }

            if (!string.Equals(
                    SignVRReleaseSettings.ApplicationIdentifier,
                    "com.signvr.recorder",
                    StringComparison.Ordinal
                ))
            {
                failures.Add(
                    "Recorder application identifier must remain " +
                    "com.signvr.recorder."
                );
            }
            if (!string.Equals(
                    SignVRReleaseSettings.InteractionApplicationIdentifier,
                    "com.signvr.interaction",
                    StringComparison.Ordinal
                ))
            {
                failures.Add(
                    "Interaction application identifier must be " +
                    "com.signvr.interaction."
                );
            }
            if (string.Equals(
                    SignVRReleaseSettings.ApplicationIdentifier,
                    SignVRReleaseSettings.InteractionApplicationIdentifier,
                    StringComparison.Ordinal
                ))
            {
                failures.Add(
                    "Recorder and Interaction application identifiers are not " +
                    "independent."
                );
            }
            if (string.Equals(
                    SignVRReleaseSettings.ProductName,
                    SignVRReleaseSettings.InteractionProductName,
                    StringComparison.Ordinal
                ))
            {
                failures.Add(
                    "Recorder and Interaction product names are not independent."
                );
            }
            if (string.Equals(
                    CommandLineBuild.GetDefaultBuildPathForValidation(
                        SignVRProduct.Recorder
                    ),
                    CommandLineBuild.GetDefaultBuildPathForValidation(
                        SignVRProduct.Interaction
                    ),
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                failures.Add(
                    "Recorder and Interaction default APK paths collide."
                );
            }

            ThrowIfInvalid("dual Android build", failures);
            Debug.Log(
                "[InteractionLabValidator] Dual Android build contract is valid."
            );
        }

        public static void ValidateSceneForAutomation()
        {
            string generatorEntryPoint =
                nameof(InteractionLabSceneTool) + "." +
                nameof(
                    InteractionLabSceneTool
                        .GenerateOrUpdateSceneForAutomation
                );
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                InteractionLabContract.ScenePath
            );
            if (sceneAsset == null)
            {
                throw new InvalidOperationException(
                    $"Interaction scene is missing: " +
                    $"{InteractionLabContract.ScenePath}. Run " +
                    $"{generatorEntryPoint} first."
                );
            }

            Scene loaded = SceneManager.GetSceneByPath(
                InteractionLabContract.ScenePath
            );
            if (loaded.IsValid() && loaded.isLoaded)
            {
                ValidateLoadedScene(loaded);
                return;
            }

            EnsureLoadedScenesAreSaved();
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(
                    InteractionLabContract.ScenePath,
                    OpenSceneMode.Single
                );
                ValidateLoadedScene(scene);
            }
            finally
            {
                InteractionLabSceneTool.RestoreSceneManagerSetupSafely(setup);
            }
        }

        public static void ValidateLoadedScene(Scene scene)
        {
            ValidateLoadedScene(scene, requireSavedScene: true);
        }

        internal static void ValidateLoadedSceneForGeneration(Scene scene)
        {
            ValidateLoadedScene(scene, requireSavedScene: false);
        }

        private static void ValidateLoadedScene(
            Scene scene,
            bool requireSavedScene)
        {
            var failures = new List<string>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                failures.Add("InteractionLab is not a valid loaded scene.");
                ThrowIfInvalid("InteractionLab scene", failures);
            }
            if (requireSavedScene && scene.isDirty)
            {
                failures.Add(
                    "InteractionLab has unsaved changes; validation must run " +
                    "against the saved scene asset."
                );
            }
            if (!string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal
                ))
            {
                failures.Add(
                    $"Scene path is '{scene.path}', expected " +
                    $"'{InteractionLabContract.ScenePath}'."
                );
            }
            if (string.Equals(scene.name, "VRroom", StringComparison.Ordinal))
            {
                failures.Add(
                    "InteractionLab must not use the VRroom scene name that " +
                    "activates RecordingRuntimeBootstrap."
                );
            }

            string sourceGuid = AssetDatabase.AssetPathToGUID(
                InteractionLabContract.RecorderScenePath
            );
            string interactionGuid = AssetDatabase.AssetPathToGUID(
                InteractionLabContract.ScenePath
            );
            if (string.IsNullOrEmpty(interactionGuid))
            {
                failures.Add("InteractionLab has no imported scene GUID.");
            }
            else if (string.Equals(
                         sourceGuid,
                         interactionGuid,
                         StringComparison.Ordinal
                     ))
            {
                failures.Add(
                    "VRroom and InteractionLab unexpectedly share an asset GUID."
                );
            }

            GameObject[] gameObjects =
                InteractionLabSceneTool.EnumerateGameObjects(scene).ToArray();
            ValidateRequiredHierarchy(scene, gameObjects, failures);
            ValidateReusableRoomAndRig(scene, gameObjects, failures);
            ValidateRecordingResponsibilities(gameObjects, failures);
            ValidateMissingScripts(gameObjects, failures);

            ThrowIfInvalid("InteractionLab scene", failures);
            Debug.Log(
                $"[InteractionLabValidator] {InteractionLabContract.ScenePath} " +
                "is clean and valid."
            );
        }

        private static void ValidateEntryPoint(
            string methodName,
            ICollection<string> failures)
        {
            MethodInfo method = typeof(CommandLineBuild).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            if (method == null || method.ReturnType != typeof(void) ||
                method.GetParameters().Length != 0)
            {
                failures.Add(
                    $"CommandLineBuild.{methodName} must be a public static " +
                    "parameterless void entry point."
                );
            }
        }

        private static void ValidateSingleScene(
            string product,
            IReadOnlyList<string> scenes,
            string expectedScene,
            ICollection<string> failures)
        {
            if (scenes.Count != 1 ||
                !string.Equals(
                    scenes.Count == 1 ? scenes[0] : null,
                    expectedScene,
                    StringComparison.Ordinal
                ))
            {
                failures.Add(
                    $"{product} build entry must explicitly contain only " +
                    $"{expectedScene}."
                );
            }
        }

        private static void ValidateRequiredHierarchy(
            Scene scene,
            IReadOnlyCollection<GameObject> gameObjects,
            ICollection<string> failures)
        {
            GameObject[] sceneRoots = scene.GetRootGameObjects()
                .Where(
                    root => root.name == InteractionLabContract.SceneRootName
                )
                .ToArray();
            if (sceneRoots.Length != 1)
            {
                failures.Add(
                    $"Expected exactly one scene-root " +
                    $"{InteractionLabContract.SceneRootName}; found " +
                    $"{sceneRoots.Length}."
                );
            }

            foreach (string anchorPath in
                     InteractionLabContract.RequiredAnchorPaths)
            {
                int matches = gameObjects.Count(
                    gameObject => string.Equals(
                        InteractionLabSceneTool.GetHierarchyPath(
                            gameObject.transform
                        ),
                        anchorPath,
                        StringComparison.Ordinal
                    )
                );
                if (matches != 1)
                {
                    failures.Add(
                        $"Required anchor '{anchorPath}' must occur exactly " +
                        $"once; found {matches}."
                    );
                }
            }
        }

        private static void ValidateReusableRoomAndRig(
            Scene scene,
            IReadOnlyCollection<GameObject> gameObjects,
            ICollection<string> failures)
        {
            RequireUniqueRoot(scene, "room", failures);
            RequireUniqueRoot(scene, "VRPlayer", failures);
            RequireUniqueRoot(scene, "PlayerSpawnPoint", failures);
            RequireUniqueRoot(scene, "VRFloorCollision", failures);

            Component[] playerRigs = FindComponentsByTypeName(
                gameObjects,
                "VRPlayerRig"
            );
            if (playerRigs.Length == 0)
            {
                failures.Add("Reusable VRPlayerRig is missing.");
            }
            else if (playerRigs.Length > 1)
            {
                failures.Add(
                    $"Expected exactly one VRPlayerRig; found " +
                    $"{playerRigs.Length}."
                );
            }
            else
            {
                var serializedPlayer = new SerializedObject(playerRigs[0]);
                SerializedProperty recordingMode =
                    serializedPlayer.FindProperty("recordingMode");
                SerializedProperty preserveRuntimeTrackingOrigin =
                    serializedPlayer.FindProperty(
                        "preserveRuntimeTrackingOrigin"
                    );
                if (recordingMode == null || !recordingMode.boolValue)
                {
                    failures.Add(
                        "InteractionLab VRPlayerRig must start in fixed " +
                        "recording mode so gravity cannot lower the study " +
                        "viewpoint."
                    );
                }
                if (preserveRuntimeTrackingOrigin == null ||
                    !preserveRuntimeTrackingOrigin.boolValue)
                {
                    failures.Add(
                        "InteractionLab VRPlayerRig must leave the live " +
                        "Meta/OpenXR tracking origin runtime-owned so the " +
                        "viewpoint does not alternate between two heights."
                    );
                }
            }
            if (!HasComponentType(gameObjects, "OVRCameraRig"))
            {
                failures.Add("OVRCameraRig is missing.");
            }

            int trackedHands = CountComponentType(gameObjects, "OVRHand");
            if (trackedHands < 2)
            {
                failures.Add(
                    $"Expected left and right OVRHand components; found " +
                    $"{trackedHands}."
                );
            }

            int mainCameras = gameObjects.Count(
                gameObject => gameObject.CompareTag("MainCamera") &&
                              gameObject.GetComponent<Camera>() != null
            );
            if (mainCameras != 1)
            {
                failures.Add(
                    $"Expected exactly one tagged MainCamera; found " +
                    $"{mainCameras}."
                );
            }

            GameObject room = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "room");
            if (room != null &&
                room.GetComponentsInChildren<MeshCollider>(true).Length == 0)
            {
                failures.Add("Reusable room mesh colliders are missing.");
            }
        }

        private static void ValidateRecordingResponsibilities(
            IReadOnlyCollection<GameObject> gameObjects,
            ICollection<string> failures)
        {
            foreach (GameObject gameObject in gameObjects)
            {
                string path = InteractionLabSceneTool.GetHierarchyPath(
                    gameObject.transform
                );
                if (InteractionLabContract.RecordingOwnedObjectNames.Contains(
                        gameObject.name
                    ))
                {
                    failures.Add(
                        $"Recorder-owned object remains at '{path}'."
                    );
                }

                foreach (Component component in
                         gameObject.GetComponents<Component>())
                {
                    if (component == null || component is Transform)
                    {
                        continue;
                    }

                    Type type = component.GetType();
                    string fullName = type.FullName ?? type.Name;
                    string typeNamespace = type.Namespace ?? string.Empty;
                    if (typeNamespace.Equals(
                            "SignVR.Recording",
                            StringComparison.Ordinal
                        ) ||
                        typeNamespace.StartsWith(
                            "SignVR.Recording.",
                            StringComparison.Ordinal
                        ) ||
                        InteractionLabContract.ForbiddenComponentTypeNames
                            .Contains(fullName))
                    {
                        failures.Add(
                            $"Forbidden Recorder component {fullName} remains " +
                            $"at '{path}'."
                        );
                    }

                    if (InteractionLabSceneTool.UsesLegacyStreamerPort(component))
                    {
                        failures.Add(
                            $"Legacy port " +
                            $"{InteractionLabContract.LegacyStreamerPort} is " +
                            $"serialized on {fullName} at '{path}'."
                        );
                    }
                }
            }
        }

        private static void ValidateMissingScripts(
            IEnumerable<GameObject> gameObjects,
            ICollection<string> failures)
        {
            int missingScripts = gameObjects
                .Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
            if (missingScripts > 0)
            {
                failures.Add(
                    $"Scene contains {missingScripts} missing MonoBehaviour " +
                    "script reference(s)."
                );
            }
        }

        private static void RequireUniqueRoot(
            Scene scene,
            string rootName,
            ICollection<string> failures)
        {
            int matches = scene.GetRootGameObjects()
                .Count(root => root.name == rootName);
            if (matches != 1)
            {
                failures.Add(
                    $"Required reusable scene root '{rootName}' must occur " +
                    $"exactly once; found {matches}."
                );
            }
        }

        private static bool HasComponentType(
            IEnumerable<GameObject> gameObjects,
            string typeName)
        {
            return CountComponentType(gameObjects, typeName) > 0;
        }

        private static int CountComponentType(
            IEnumerable<GameObject> gameObjects,
            string typeName)
        {
            return FindComponentsByTypeName(gameObjects, typeName).Length;
        }

        private static Component[] FindComponentsByTypeName(
            IEnumerable<GameObject> gameObjects,
            string typeName)
        {
            return gameObjects
                .SelectMany(gameObject => gameObject.GetComponents<Component>())
                .Where(
                    component => component != null &&
                                 string.Equals(
                                     component.GetType().Name,
                                     typeName,
                                     StringComparison.Ordinal
                                 )
                )
                .ToArray();
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
                        "before validating InteractionLab."
                    );
                }
            }
        }

        private static void ThrowIfInvalid(
            string contractName,
            IEnumerable<string> failures)
        {
            string[] distinctFailures = failures
                .Where(failure => !string.IsNullOrWhiteSpace(failure))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctFailures.Length == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Invalid {contractName}:\n- " +
                string.Join("\n- ", distinctFailures)
            );
        }
    }
}
