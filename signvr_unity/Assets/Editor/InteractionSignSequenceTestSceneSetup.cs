using System;
using System.Linq;
using SignVR.Interaction.Orchestration;
using SignVR.Interaction.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Editor.Interaction
{
    public static class InteractionSignSequenceTestSceneSetup
    {
        public const string ScenePath =
            "Assets/Scenes/InteractionSignSequenceTest.unity";

        [MenuItem("SignVR/Tests/Configure 31-Sign Sequence Scene")]
        public static void ConfigureScene()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single
            );
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "The sign-sequence test scene could not be opened."
                );
            }

            InstructionPresentationController presentation = FindOne<
                InstructionPresentationController>();
            InteractionInstructionControls copiedControls = FindOne<
                InteractionInstructionControls>();

            foreach (InteractionStudyFlowController flow in
                FindAll<InteractionStudyFlowController>())
            {
                flow.enabled = false;
                EditorUtility.SetDirty(flow);
            }
            foreach (InteractionStudyFlowControls controls in
                FindAll<InteractionStudyFlowControls>())
            {
                DisableProductionStudyUi(controls);
            }
            copiedControls.enabled = false;
            EditorUtility.SetDirty(copiedControls);
            RestoreProductionControlNames(copiedControls.transform);

            Transform existing = copiedControls.transform.Find(
                "SignSequenceTestHarness"
            );
            GameObject harness = existing != null
                ? existing.gameObject
                : new GameObject("SignSequenceTestHarness");
            if (existing == null)
            {
                harness.transform.SetParent(copiedControls.transform, false);
            }
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(harness);

            InteractionSignSequenceTestController sequenceController =
                GetOrAddSingle<InteractionSignSequenceTestController>(harness);
            InteractionSignSequenceTestControls sequenceControls =
                GetOrAddSingle<InteractionSignSequenceTestControls>(harness);
            sequenceController.Configure(presentation);
            sequenceControls.Configure(sequenceController, copiedControls);

            EditorUtility.SetDirty(sequenceController);
            EditorUtility.SetDirty(sequenceControls);
            EditorUtility.SetDirty(harness);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Configured InteractionSignSequenceTest with 31 production " +
                "instruction entries and Previous/Replay/Next controls."
            );
        }

        [MenuItem("SignVR/Tests/Repair 31-Sign Sequence Entry UI")]
        public static void RepairEntryUi()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive
            );
            try
            {
                InteractionStudyFlowControls[] controls =
                    FindSceneComponents<InteractionStudyFlowControls>(scene);
                if (controls.Length != 1)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must contain exactly one " +
                        "InteractionStudyFlowControls."
                    );
                }

                DisableProductionStudyUi(controls[0]);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        public static void ValidateSceneForAutomation()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive
            );
            try
            {
                InteractionStudyFlowController[] flows =
                    FindSceneComponents<InteractionStudyFlowController>(scene);
                InteractionStudyFlowControls[] flowControls =
                    FindSceneComponents<InteractionStudyFlowControls>(scene);
                if (flows.Length != 1 || flows[0].enabled)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must contain one disabled " +
                        "InteractionStudyFlowController."
                    );
                }
                if (flowControls.Length != 1 || flowControls[0].enabled)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene must contain one disabled " +
                        "InteractionStudyFlowControls."
                    );
                }

                var serializedControls = new SerializedObject(
                    flowControls[0]
                );
                SerializedProperty preStartRootProperty = serializedControls
                    .FindProperty("preStartRoot");
                GameObject preStartRoot = preStartRootProperty
                    ?.objectReferenceValue as GameObject;
                if (preStartRoot == null || preStartRoot.activeSelf)
                {
                    throw new InvalidOperationException(
                        "The production study pre-start UI must be inactive " +
                        "when the sign-sequence test scene opens."
                    );
                }

                if (FindSceneComponents<InteractionSignSequenceTestController>(
                        scene).Length != 1 ||
                    FindSceneComponents<InteractionSignSequenceTestControls>(
                        scene).Length != 1)
                {
                    throw new InvalidOperationException(
                        "The sign-sequence scene requires exactly one test " +
                        "controller and one test control panel."
                    );
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static T FindOne<T>() where T : Component
        {
            T[] values = FindAll<T>();
            if (values.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one {typeof(T).Name}, found " +
                    values.Length + "."
                );
            }
            return values[0];
        }

        private static T[] FindAll<T>() where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
                .Where(value => value.gameObject.scene.IsValid())
                .ToArray();
        }

        private static T[] FindSceneComponents<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }

        private static void DisableProductionStudyUi(
            InteractionStudyFlowControls controls)
        {
            controls.enabled = false;

            var serializedControls = new SerializedObject(controls);
            SerializedProperty preStartRootProperty = serializedControls
                .FindProperty("preStartRoot");
            GameObject preStartRoot = preStartRootProperty
                ?.objectReferenceValue as GameObject;
            if (preStartRoot == null)
            {
                throw new InvalidOperationException(
                    "InteractionStudyFlowControls.preStartRoot is missing."
                );
            }

            preStartRoot.SetActive(false);
            EditorUtility.SetDirty(preStartRoot);
            EditorUtility.SetDirty(controls);
        }

        private static T GetOrAddSingle<T>(GameObject target)
            where T : Component
        {
            T[] existing = target.GetComponents<T>();
            T result = existing.FirstOrDefault() ?? target.AddComponent<T>();
            for (int index = 1; index < existing.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(existing[index]);
            }
            return result;
        }

        private static void RestoreProductionControlNames(Transform owner)
        {
            Transform canvas = owner.Find("InstructionControlCanvas");
            if (canvas == null)
            {
                throw new InvalidOperationException(
                    "InstructionControlCanvas is missing from the copied scene."
                );
            }

            RenameIfPresent(canvas, "Previous", "GiveUpPhase");
            RenameIfPresent(canvas, "Next", "AbortRun");
        }

        private static void RenameIfPresent(
            Transform parent,
            string currentName,
            string requiredName)
        {
            Transform required = parent.Find(requiredName);
            if (required != null)
            {
                return;
            }

            Transform current = parent.Find(currentName);
            if (current == null)
            {
                throw new InvalidOperationException(
                    $"Copied control '{currentName}' is missing."
                );
            }
            current.name = requiredName;
            EditorUtility.SetDirty(current.gameObject);
        }
    }
}
