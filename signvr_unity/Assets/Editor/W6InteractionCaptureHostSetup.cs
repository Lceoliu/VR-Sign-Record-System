using System;
using System.Linq;
using SignVR.Interaction.CaptureHost;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SignVR.Editor.Interaction
{
    /// <summary>
    /// W6-only idempotent scene wiring. It intentionally never saves a scene;
    /// the Orchestrator reviews the loaded result and owns persistence.
    /// </summary>
    public static class W6InteractionCaptureHostSetup
    {
        private const string RuntimeAnchorPath =
            "InteractionSceneRoot/RuntimeSystemsAnchor";
        private const string CaptureAnchorPath =
            "InteractionSceneRoot/Anchors/ExperimentCaptureAnchor";

        [MenuItem("Tools/SignVR/Interaction/W6 Configure Capture and Host (Unsaved)")]
        public static void ConfigureLoadedSceneUnsaved()
        {
            Scene scene = SceneManager.GetActiveScene();
            RequireInteractionScene(scene);
            Transform runtimeAnchor = RequireTransform(scene, RuntimeAnchorPath);
            Transform captureAnchor = RequireTransform(scene, CaptureAnchorPath);

            InteractionHostClient client = GetOrAdd<InteractionHostClient>(
                runtimeAnchor.gameObject
            );
            InteractionRunController controller =
                GetOrAdd<InteractionRunController>(runtimeAnchor.gameObject);
            InteractionCaptureSampler sampler =
                GetOrAdd<InteractionCaptureSampler>(captureAnchor.gameObject);

            controller.ConfigureHostClient(client);
            controller.ConfigureCaptureSampler(sampler);
            sampler.Configure(controller);
            EditorUtility.SetDirty(client);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(sampler);
            ValidateLoadedScene();
            Debug.Log(
                "[W6InteractionCaptureHostSetup] Capture/Host wiring is valid. " +
                "The scene is intentionally left unsaved for Orchestrator review."
            );
        }

        [MenuItem("Tools/SignVR/Interaction/W6 Validate Capture and Host")]
        public static void ValidateLoadedSceneFromMenu()
        {
            ValidateLoadedScene();
            Debug.Log(
                "[W6InteractionCaptureHostSetup] Loaded-scene validation passed."
            );
        }

        public static void ValidateLoadedSceneForAutomation()
        {
            ValidateLoadedScene();
        }

        public static void ValidateLoadedScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            RequireInteractionScene(scene);
            Transform runtimeAnchor = RequireTransform(scene, RuntimeAnchorPath);
            Transform captureAnchor = RequireTransform(scene, CaptureAnchorPath);

            InteractionHostClient[] clients = runtimeAnchor
                .GetComponents<InteractionHostClient>();
            InteractionRunController[] controllers = runtimeAnchor
                .GetComponents<InteractionRunController>();
            InteractionCaptureSampler[] samplers = captureAnchor
                .GetComponents<InteractionCaptureSampler>();
            if (clients.Length != 1 || controllers.Length != 1 ||
                samplers.Length != 1)
            {
                throw new InvalidOperationException(
                    "W6 requires exactly one Host client and Run controller on " +
                    "RuntimeSystemsAnchor, and one capture sampler on " +
                    "ExperimentCaptureAnchor."
                );
            }
            if (controllers[0].HostClient != clients[0] ||
                controllers[0].CaptureSampler != samplers[0] ||
                samplers[0].Controller != controllers[0])
            {
                throw new InvalidOperationException(
                    "W6 component references are not wired to the anchor-local instances."
                );
            }
            if (!samplers[0].IsStudyCaptureReady(out string captureReason))
            {
                throw new InvalidOperationException(
                    "W6 Study capture sources are incomplete: " +
                    captureReason +
                    " W8 must inject the XR Rig and at least one key-object probe."
                );
            }
            if (controllers[0].RunMode != InteractionRunMode.Study ||
                controllers[0].DebugOverridesActive ||
                !controllers[0].RequireHostForStart)
            {
                throw new InvalidOperationException(
                    "Versioned InteractionLab wiring must default to Study, " +
                    "require Host, and have no debug override active."
                );
            }

            var clientObject = new SerializedObject(clients[0]);
            SerializedProperty url = clientObject.FindProperty("hostBaseUrl");
            if (url == null)
            {
                throw new InvalidOperationException(
                    "InteractionHostClient hostBaseUrl field is missing."
                );
            }
            InteractionHostClient.NormalizeHttpBaseUrl(url.stringValue);

            int controllerCount = EnumerateSceneComponents<InteractionRunController>(
                scene
            ).Count();
            int clientCount = EnumerateSceneComponents<InteractionHostClient>(
                scene
            ).Count();
            int samplerCount = EnumerateSceneComponents<InteractionCaptureSampler>(
                scene
            ).Count();
            if (controllerCount != 1 || clientCount != 1 || samplerCount != 1)
            {
                throw new InvalidOperationException(
                    "InteractionLab must contain exactly one W6 controller, client, " +
                    "and sampler in the whole scene."
                );
            }
        }

        private static T GetOrAdd<T>(GameObject target)
            where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null)
            {
                return component;
            }
            return Undo.AddComponent<T>(target);
        }

        private static void RequireInteractionScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Load Assets/Scenes/InteractionLab.unity before using W6 setup."
                );
            }
        }

        private static Transform RequireTransform(Scene scene, string path)
        {
            string[] segments = path.Split('/');
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(
                item => string.Equals(
                    item.name,
                    segments[0],
                    StringComparison.Ordinal
                )
            );
            Transform current = root == null ? null : root.transform;
            for (int index = 1; current != null && index < segments.Length; index++)
            {
                current = current.Find(segments[index]);
            }
            if (current == null)
            {
                throw new InvalidOperationException(
                    "InteractionLab anchor is missing: " + path + "."
                );
            }
            return current;
        }

        private static System.Collections.Generic.IEnumerable<T>
            EnumerateSceneComponents<T>(Scene scene)
            where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T component in root.GetComponentsInChildren<T>(true))
                {
                    yield return component;
                }
            }
        }
    }
}
