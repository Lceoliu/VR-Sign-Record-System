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
                controls.enabled = false;
                EditorUtility.SetDirty(controls);
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
