using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal sealed class SignVRQuestBuildGuard :
    IPreprocessBuildWithReport,
    IPostGenerateGradleAndroidProject
{
    private const string AndroidNamespace =
        "http://schemas.android.com/apk/res/android";

    public int callbackOrder => -1000;

    [InitializeOnLoadMethod]
    private static void ApplyAfterEditorLoad()
    {
        EditorApplication.delayCall += ApplyQuestSettings;
    }

    [MenuItem("Tools/SignVR/Apply Quest Recording Performance Settings")]
    private static void ApplyQuestSettings()
    {
        EditorUserBuildSettings.androidBuildSubtarget =
            MobileTextureSubtarget.ASTC;

        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (projectConfig != null &&
            projectConfig.handTrackingFrequency !=
            OVRProjectConfig.HandTrackingFrequency.HIGH)
        {
            projectConfig.handTrackingFrequency =
                OVRProjectConfig.HandTrackingFrequency.HIGH;
            OVRProjectConfig.CommitProjectConfig(projectConfig);
        }
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        ApplyQuestSettings();
        Debug.Log(
            "[SignVRQuestBuildGuard] Quest build uses ASTC textures and " +
            "Meta high-frequency hand tracking."
        );
    }

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath = Path.Combine(
            path,
            "src",
            "main",
            "AndroidManifest.xml"
        );
        var document = new XmlDocument();
        document.Load(manifestPath);

        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("android", AndroidNamespace);
        XmlElement frequency = document.SelectSingleNode(
            "/manifest/application/meta-data[" +
            "@android:name='com.oculus.handtracking.frequency']",
            namespaces
        ) as XmlElement;

        if (frequency == null)
        {
            XmlElement application = document.SelectSingleNode(
                "/manifest/application"
            ) as XmlElement;
            if (application == null)
            {
                throw new BuildFailedException(
                    "Generated AndroidManifest.xml has no application node."
                );
            }

            frequency = document.CreateElement("meta-data");
            frequency.SetAttribute(
                "name",
                AndroidNamespace,
                "com.oculus.handtracking.frequency"
            );
            application.AppendChild(frequency);
        }

        frequency.SetAttribute("value", AndroidNamespace, "HIGH");
        document.Save(manifestPath);
        Debug.Log(
            "[SignVRQuestBuildGuard] Final Android Manifest hand tracking " +
            "frequency is HIGH."
        );
    }
}
