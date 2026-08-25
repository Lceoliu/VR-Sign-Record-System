using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SignVR.Editor.Interaction;
using SignVR.EditorTools;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Editor
{
    /// <summary>
    /// Stable, product-specific Android build entry points for local automation
    /// and CI. Both products pass an explicit one-scene list to BuildPipeline;
    /// Editor Build Settings are never used as an implicit source of scenes.
    /// </summary>
    public static class CommandLineBuild
    {
        private const string BuildPathArgument = "-buildPath";
        private const string RecorderBuildPathArgument = "-recorderBuildPath";
        private const string InteractionBuildPathArgument =
            "-interactionBuildPath";
        private const string DefaultRecorderBuildPath =
            "Builds/SignVR_Unity_Local.apk";
        private const string DefaultInteractionBuildPath =
            "Builds/SignVR_Interaction_Local.apk";
        private const string RecorderScenePath =
            "Assets/Scenes/VRroom.unity";

        private readonly struct AndroidBuildProfile
        {
            public AndroidBuildProfile(
                SignVRProduct product,
                string scenePath,
                string productBuildPathArgument,
                string defaultBuildPath)
            {
                Product = product;
                ScenePath = scenePath;
                ProductBuildPathArgument = productBuildPathArgument;
                DefaultBuildPath = defaultBuildPath;
            }

            public SignVRProduct Product { get; }
            public string ScenePath { get; }
            public string ProductBuildPathArgument { get; }
            public string DefaultBuildPath { get; }
        }

        private sealed class BuildStateSnapshot
        {
            private readonly string companyName;
            private readonly string productName;
            private readonly string bundleVersion;
            private readonly string applicationIdentifier;
            private readonly int androidVersionCode;
            private readonly ScriptingImplementation scriptingBackend;
            private readonly AndroidArchitecture targetArchitectures;
            private readonly InsecureHttpOption insecureHttpOption;
            private readonly EditorBuildSettingsScene[] buildScenes;
            private readonly SceneSetup[] sceneSetup;

            private BuildStateSnapshot()
            {
                companyName = PlayerSettings.companyName;
                productName = PlayerSettings.productName;
                bundleVersion = PlayerSettings.bundleVersion;
                applicationIdentifier =
                    PlayerSettings.GetApplicationIdentifier(
                        NamedBuildTarget.Android
                    );
                androidVersionCode = PlayerSettings.Android.bundleVersionCode;
                scriptingBackend = PlayerSettings.GetScriptingBackend(
                    NamedBuildTarget.Android
                );
                targetArchitectures = PlayerSettings.Android.targetArchitectures;
                insecureHttpOption = PlayerSettings.insecureHttpOption;
                buildScenes = CloneBuildScenes(EditorBuildSettings.scenes);
                sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            }

            public static BuildStateSnapshot Capture()
            {
                return new BuildStateSnapshot();
            }

            public void Restore()
            {
                var failures = new List<Exception>();

                TryRestore(
                    () => PlayerSettings.companyName = companyName,
                    "company name",
                    failures
                );
                TryRestore(
                    () => PlayerSettings.productName = productName,
                    "product name",
                    failures
                );
                TryRestore(
                    () => PlayerSettings.bundleVersion = bundleVersion,
                    "bundle version",
                    failures
                );
                TryRestore(
                    () => PlayerSettings.SetApplicationIdentifier(
                        NamedBuildTarget.Android,
                        applicationIdentifier
                    ),
                    "Android application identifier",
                    failures
                );
                TryRestore(
                    () => PlayerSettings.Android.bundleVersionCode =
                        androidVersionCode,
                    "Android version code",
                    failures
                );
                TryRestore(
                    () => PlayerSettings.SetScriptingBackend(
                        NamedBuildTarget.Android,
                        scriptingBackend
                    ),
                    "Android scripting backend",
                    failures
                );
                TryRestore(
                    () => PlayerSettings.Android.targetArchitectures =
                        targetArchitectures,
                    "Android target architectures",
                    failures
                );
                TryRestore(
                    () => PlayerSettings.insecureHttpOption = insecureHttpOption,
                    "insecure HTTP setting",
                    failures
                );

                if (!BuildScenesEqual(EditorBuildSettings.scenes, buildScenes))
                {
                    TryRestore(
                        () => EditorBuildSettings.scenes =
                            CloneBuildScenes(buildScenes),
                        "Editor Build Settings scenes",
                        failures
                    );
                }

                TryRestore(
                    () => EditorSceneManager.RestoreSceneManagerSetup(sceneSetup),
                    "open scene setup",
                    failures
                );

                if (failures.Count > 0)
                {
                    throw new AggregateException(
                        "One or more temporary build settings could not be restored.",
                        failures
                    );
                }
            }

            private static void TryRestore(
                Action restore,
                string settingName,
                ICollection<Exception> failures)
            {
                try
                {
                    restore();
                }
                catch (Exception exception)
                {
                    failures.Add(
                        new InvalidOperationException(
                            $"Could not restore {settingName}.",
                            exception
                        )
                    );
                }
            }
        }

        // Retained as a compatibility alias for existing automation. It is
        // intentionally Recorder-only now that Interaction has its own entry.
        [MenuItem("SignVR/Build/Android APK")]
        public static void BuildAndroid()
        {
            BuildRecorderAndroid();
        }

        [MenuItem("SignVR/Build/Recorder/Android APK")]
        public static void BuildRecorderAndroid()
        {
            BuildAndroid(CreateProfile(SignVRProduct.Recorder));
        }

        [MenuItem("SignVR/Build/Interaction/Android APK")]
        public static void BuildInteractionAndroid()
        {
            BuildAndroid(CreateProfile(SignVRProduct.Interaction));
        }

        internal static string[] GetScenesForValidation(SignVRProduct product)
        {
            return new[] { CreateProfile(product).ScenePath };
        }

        internal static string GetDefaultBuildPathForValidation(
            SignVRProduct product)
        {
            return CreateProfile(product).DefaultBuildPath;
        }

        private static void BuildAndroid(AndroidBuildProfile profile)
        {
            EnsureLoadedScenesAreSaved();
            BuildStateSnapshot snapshot = BuildStateSnapshot.Capture();
            Exception buildFailure = null;

            try
            {
                PrepareQuestBuild(profile);

                string outputPath = ResolveOutputPath(profile);
                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    throw new BuildFailedException(
                        $"Build output has no parent directory: {outputPath}"
                    );
                }
                Directory.CreateDirectory(outputDirectory);

                string[] scenes = { profile.ScenePath };
                Debug.Log(
                    $"[CommandLineBuild] Building {profile.Product} Android " +
                    $"player from {scenes[0]} to {outputPath}."
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
                        $"{profile.Product} Android build failed with result " +
                        $"{summary.result}. See the Unity build log for details."
                    );
                }
            }
            catch (Exception exception)
            {
                buildFailure = exception;
                throw;
            }
            finally
            {
                try
                {
                    snapshot.Restore();
                    Debug.Log(
                        "[CommandLineBuild] Restored PlayerSettings, Editor " +
                        "Build Settings scenes, and the previous scene setup."
                    );
                }
                catch (Exception restoreFailure)
                {
                    Debug.LogException(restoreFailure);
                    if (buildFailure == null)
                    {
                        throw new BuildFailedException(
                            "The build finished, but temporary project settings " +
                            "could not be restored. See the Unity log."
                        );
                    }
                }
            }
        }

        private static void PrepareQuestBuild(AndroidBuildProfile profile)
        {
            SignVRReleaseSettings.ApplyProductIdentity(profile.Product);
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP
            );
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARM64;
            PlayerSettings.insecureHttpOption =
                InsecureHttpOption.AlwaysAllowed;

            switch (profile.Product)
            {
                case SignVRProduct.Recorder:
                    ConfigureVRRoomPlayer.ValidateSceneForAutomation();
                    ConfigurePointingRecording.ValidateSceneForAutomation();
                    break;
                case SignVRProduct.Interaction:
                    InteractionLabValidator.ValidateBuildContractForAutomation();
                    InteractionLabSceneTool.GenerateOrUpdateSceneForAutomation();
                    InteractionLabValidator.ValidateLoadedScene(
                        SceneManager.GetSceneByPath(
                            InteractionLabContract.ScenePath
                        )
                    );
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(profile),
                        profile.Product,
                        "Unknown SignVR build product."
                    );
            }

            SignVRReleaseSettings.ValidateProductIdentity(profile.Product);
            Debug.Log(
                $"[CommandLineBuild] {profile.Product} Quest settings and " +
                $"single-scene contract validated ({profile.ScenePath})."
            );
        }

        private static AndroidBuildProfile CreateProfile(SignVRProduct product)
        {
            switch (product)
            {
                case SignVRProduct.Recorder:
                    return new AndroidBuildProfile(
                        product,
                        RecorderScenePath,
                        RecorderBuildPathArgument,
                        DefaultRecorderBuildPath
                    );
                case SignVRProduct.Interaction:
                    return new AndroidBuildProfile(
                        product,
                        InteractionLabContract.ScenePath,
                        InteractionBuildPathArgument,
                        DefaultInteractionBuildPath
                    );
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(product),
                        product,
                        "Unknown SignVR build product."
                    );
            }
        }

        private static string ResolveOutputPath(AndroidBuildProfile profile)
        {
            DirectoryInfo projectDirectory = Directory.GetParent(
                Application.dataPath
            );
            if (projectDirectory == null)
            {
                throw new BuildFailedException(
                    $"Could not resolve the Unity project root from " +
                    $"{Application.dataPath}."
                );
            }

            string requestedPath =
                GetArgument(profile.ProductBuildPathArgument) ??
                GetArgument(BuildPathArgument) ??
                profile.DefaultBuildPath;
            return Path.GetFullPath(
                Path.IsPathRooted(requestedPath)
                    ? requestedPath
                    : Path.Combine(projectDirectory.FullName, requestedPath)
            );
        }

        private static void EnsureLoadedScenesAreSaved()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty)
                {
                    throw new BuildFailedException(
                        $"Save or discard changes in scene '{scene.name}' " +
                        "before starting a product build."
                    );
                }
            }
        }

        private static EditorBuildSettingsScene[] CloneBuildScenes(
            IEnumerable<EditorBuildSettingsScene> scenes)
        {
            return scenes
                .Select(
                    scene => new EditorBuildSettingsScene(
                        scene.path,
                        scene.enabled
                    )
                )
                .ToArray();
        }

        private static bool BuildScenesEqual(
            IReadOnlyList<EditorBuildSettingsScene> left,
            IReadOnlyList<EditorBuildSettingsScene> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int index = 0; index < left.Count; index++)
            {
                if (left[index].enabled != right[index].enabled ||
                    !string.Equals(
                        left[index].path,
                        right[index].path,
                        StringComparison.Ordinal
                    ))
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();

            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                    arguments[index],
                    name,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
