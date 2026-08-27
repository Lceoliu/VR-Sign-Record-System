using SignVR.Interaction.Orchestration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace SignVR.Editor
{
    internal static class SignVRPcXrPreviewGuardSetup
    {
        private const string MenuPath =
            "SignVR/Tools/Configure PC XR Preview Guard";
        private const string OperatorLayerName =
            "XR_APILAYER_METAX_operator";

        [MenuItem(MenuPath)]
        private static void ConfigureOpenScenes()
        {
            bool operatorLayerDisabled =
                DisableUnstableStandaloneOperatorLayer();
            OVRManager[] managers =
                Object.FindObjectsByType<OVRManager>(
                    FindObjectsInactive.Include);
            int addedCount = 0;

            foreach (OVRManager manager in managers)
            {
                if (manager == null ||
                    manager.GetComponent<InteractionPcXrPreviewGuard>() !=
                    null)
                {
                    continue;
                }

                Undo.AddComponent<InteractionPcXrPreviewGuard>(
                    manager.gameObject);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                addedCount++;
            }

            if (addedCount > 0)
            {
                EditorSceneManager.SaveOpenScenes();
            }

            Debug.Log(
                $"[SignVR PC XR Preview] Configured {addedCount} " +
                "OVRManager object(s) in the open scenes; " +
                $"disabled Operator API layer: {operatorLayerDisabled}.");
        }

        private static bool DisableUnstableStandaloneOperatorLayer()
        {
            // Meta XR Operator is experimental and is not needed for Simulator
            // rendering or input. Version 205 can crash Unity in this layer
            // while OpenXR is being torn down on Play Mode exit.
            OpenXRSettings settings =
                OpenXRSettings.GetSettingsForBuildTargetGroup(
                    BuildTargetGroup.Standalone);
            ApiLayersFeature apiLayersFeature =
                settings?.GetFeature<ApiLayersFeature>();
            if (apiLayersFeature == null ||
                !apiLayersFeature.apiLayers.IsEnabled(OperatorLayerName))
            {
                return false;
            }

            apiLayersFeature.apiLayers.SetEnabled(
                OperatorLayerName,
                false);
            EditorUtility.SetDirty(apiLayersFeature);
            AssetDatabase.SaveAssetIfDirty(apiLayersFeature);
            return true;
        }
    }
}
