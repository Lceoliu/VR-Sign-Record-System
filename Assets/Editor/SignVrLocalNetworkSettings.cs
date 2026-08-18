using UnityEditor;
using UnityEngine;

public static class SignVrLocalNetworkSettings
{
    [MenuItem("SignVR/Apply Local Recording Settings")]
    public static void Apply()
    {
        PlayerSettings.insecureHttpOption =
            InsecureHttpOption.AlwaysAllowed;

        AssetDatabase.SaveAssets();
        Debug.Log(
            "[SignVR] Local recording settings applied: " +
            "plain HTTP is allowed for the trusted recording LAN."
        );
    }
}
