using UnityEditor;

[InitializeOnLoad]
internal static class GenerateVRSortingGameOnce
{
    static GenerateVRSortingGameOnce()
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
            CreateVRSortingGameScene.ScenePath
        );
        bool requiredScriptMissing =
            scene != null &&
            System.Array.IndexOf(
                AssetDatabase.GetDependencies(
                    CreateVRSortingGameScene.ScenePath,
                    true
                ),
                CreateVRSortingGameScene.ResetUiScriptPath
            ) < 0;

        if (scene == null || requiredScriptMissing)
        {
            CreateVRSortingGameScene.CreateSceneForAutomation();
        }
    }
}
