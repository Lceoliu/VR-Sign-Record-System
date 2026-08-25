using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SignVR.Editor
{
    /// <summary>
    /// Provides a stable Android build entry point for local automation and CI.
    /// Pass -buildPath to override the ignored Builds/ output location.
    /// </summary>
    public static class CommandLineBuild
    {
        private const string BuildPathArgument = "-buildPath";
        private const string DefaultBuildPath =
            "Builds/SignVR_Unity_Local.apk";

        [MenuItem("SignVR/Build/Android APK")]
        public static void BuildAndroid()
        {
            PrepareQuestBuild();

            string projectRoot = Directory.GetParent(Application.dataPath)!
                .FullName;
            string requestedPath =
                GetArgument(BuildPathArgument) ?? DefaultBuildPath;
            string outputPath = Path.GetFullPath(
                Path.IsPathRooted(requestedPath)
                    ? requestedPath
                    : Path.Combine(projectRoot, requestedPath)
            );

            string outputDirectory = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(outputDirectory);

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new BuildFailedException(
                    "No enabled scenes were found in Editor Build Settings."
                );
            }

            Debug.Log(
                "[CommandLineBuild] Building Android player to " +
                outputPath
            );

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log(
                "[CommandLineBuild] Result=" + summary.result +
                ", errors=" + summary.totalErrors +
                ", warnings=" + summary.totalWarnings +
                ", size=" + summary.totalSize +
                ", duration=" + summary.totalTime
            );

            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Android build failed with result " + summary.result +
                    ". See the Unity build log for details."
                );
            }
        }

        private static void PrepareQuestBuild()
        {
            SignVR.EditorTools.SignVRReleaseSettings.ApplyProductIdentity();
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP
            );
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARM64;
            PlayerSettings.insecureHttpOption =
                InsecureHttpOption.AlwaysAllowed;

            ConfigureVRRoomPlayer.ValidateSceneForAutomation();
            ConfigurePointingRecording.ValidateSceneForAutomation();
            SignVR.EditorTools.SignVRReleaseSettings.ValidateProductIdentity();
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[CommandLineBuild] Quest build settings and scene " +
                "contracts validated."
            );
        }

        private static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(
                    arguments[i],
                    name,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    return arguments[i + 1];
                }
            }

            return null;
        }
    }
}
