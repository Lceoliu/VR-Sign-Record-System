using System;
using System.Linq;
using UnityEditor;

[InitializeOnLoad]
internal static class GenerateCoopLiftWorkshopOnce
{
    static GenerateCoopLiftWorkshopOnce()
    {
        EditorApplication.delayCall += GenerateIfMissing;
    }

    private static void GenerateIfMissing()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += GenerateIfMissing;
            return;
        }

        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
            CreateCoopLiftWorkshopScene.ScenePath
        );
        bool dependencyMissing = scene != null &&
            new[]
            {
                CreateCoopLiftWorkshopScene.ManagerScriptPath,
                CreateCoopLiftWorkshopScene.SceneFlowScriptPath
            }.Any(required =>
                Array.IndexOf(
                    AssetDatabase.GetDependencies(
                        CreateCoopLiftWorkshopScene.ScenePath,
                        true
                    ),
                    required
                ) < 0
            );

        if (scene == null || dependencyMissing)
        {
            CreateCoopLiftWorkshopScene.CreateSceneForAutomation();
        }
    }
}
