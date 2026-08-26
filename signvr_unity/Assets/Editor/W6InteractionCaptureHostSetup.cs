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
            ConfigureLoadedSceneUnsavedForAutomation(null);
            Debug.Log(
                "[W6InteractionCaptureHostSetup] Capture/Host wiring is valid. " +
                "The scene is intentionally left unsaved for Orchestrator review."
            );
        }

        public static void ConfigureLoadedSceneUnsavedForAutomation(
            Action afterWiring)
        {
            ConfigureSceneUnsavedForAutomation(
                SceneManager.GetActiveScene(),
                true,
                afterWiring
            );
        }

        public static void ConfigureSceneUnsavedForAutomation(
            Scene scene,
            bool requireCanonicalScenePath,
            Action afterWiring)
        {
            PreflightLoadedSceneStructure(scene, requireCanonicalScenePath);
            ExecuteUndoGroupForAutomation(
                "W6 Configure Capture and Host",
                () => ConfigureLoadedSceneStructure(
                    scene,
                    requireCanonicalScenePath,
                    afterWiring
                )
            );
        }

        [MenuItem("Tools/SignVR/Interaction/W6 Validate Capture Structure")]
        public static void ValidateLoadedSceneFromMenu()
        {
            ValidateLoadedSceneStructure();
            Debug.Log(
                "[W6InteractionCaptureHostSetup] Structural validation passed."
            );
        }

        [MenuItem("Tools/SignVR/Interaction/W6 Validate Study Capture Readiness")]
        public static void ValidateStudyReadinessFromMenu()
        {
            ValidateLoadedSceneStudyReadiness();
            Debug.Log(
                "[W6InteractionCaptureHostSetup] Strict Study readiness passed."
            );
        }

        public static void ValidateLoadedSceneForAutomation()
        {
            ValidateLoadedSceneStructure();
        }

        public static void ValidateStudyReadinessForAutomation()
        {
            ValidateLoadedSceneStudyReadiness();
        }

        // Compatibility seam now intentionally means structural validation.
        public static void ValidateLoadedScene()
        {
            ValidateLoadedSceneStructure();
        }

        public static void ExecuteUndoGroupForAutomation(
            string operationName,
            Action operation)
        {
            if (string.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException(
                    "Undo operation name is required.",
                    nameof(operationName)
                );
            }
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(operationName.Trim());
            try
            {
                operation();
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static void ConfigureLoadedSceneStructure(
            Scene scene,
            bool requireCanonicalScenePath,
            Action afterWiring)
        {
            Transform runtimeAnchor = RequireTransform(scene, RuntimeAnchorPath);
            Transform captureAnchor = RequireTransform(scene, CaptureAnchorPath);

            InteractionHostClient client = GetOrAdd<InteractionHostClient>(
                runtimeAnchor.gameObject
            );
            InteractionRunController controller =
                GetOrAdd<InteractionRunController>(runtimeAnchor.gameObject);
            InteractionCaptureSampler sampler =
                GetOrAdd<InteractionCaptureSampler>(captureAnchor.gameObject);

            SetObjectReference(
                controller,
                "hostClient",
                client
            );
            SetObjectReference(
                controller,
                "captureSampler",
                sampler
            );
            SetObjectReference(sampler, "controller", controller);
            afterWiring?.Invoke();
            ValidateSceneStructure(scene, requireCanonicalScenePath);
        }

        public static void ValidateLoadedSceneStructure()
        {
            Scene scene = SceneManager.GetActiveScene();
            ValidateSceneStructure(scene, true);
        }

        private static void ValidateSceneStructure(
            Scene scene,
            bool requireCanonicalScenePath)
        {
            PreflightLoadedSceneStructure(scene, requireCanonicalScenePath);
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
            InteractionCaptureSetupPolicy.ValidateStructure(
                clients.Length,
                controllers.Length,
                samplers.Length,
                controllers[0].HostClient == clients[0] &&
                    controllers[0].CaptureSampler == samplers[0] &&
                    samplers[0].Controller == controllers[0],
                controllers[0].RunMode,
                controllers[0].DebugOverridesActive,
                controllers[0].RequireHostForStart
            );

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

        public static void ValidateLoadedSceneStudyReadiness()
        {
            ValidateLoadedSceneStructure();
            Scene scene = SceneManager.GetActiveScene();
            Transform captureAnchor = RequireTransform(scene, CaptureAnchorPath);
            InteractionCaptureSampler sampler =
                captureAnchor.GetComponent<InteractionCaptureSampler>();
            if (!sampler.IsStudyCaptureReady(out string captureReason))
            {
                throw new InvalidOperationException(
                    "W6 Study capture sources are incomplete: " +
                    captureReason +
                    " W8 must inject the XR Rig and at least one key-object probe."
                );
            }
            InteractionCaptureSetupPolicy.ValidateStudyReadiness(
                sampler.HmdReady,
                sampler.LeftHandDataSourceReady,
                sampler.RightHandDataSourceReady,
                sampler.ConfiguredObjectProbeCount
            );
        }

        private static void PreflightLoadedSceneStructure(
            Scene scene,
            bool requireCanonicalScenePath)
        {
            RequireInteractionScene(scene, requireCanonicalScenePath);
            Transform runtimeAnchor = RequireTransform(scene, RuntimeAnchorPath);
            Transform captureAnchor = RequireTransform(scene, CaptureAnchorPath);
            InteractionHostClient[] clients =
                EnumerateSceneComponents<InteractionHostClient>(scene).ToArray();
            InteractionRunController[] controllers =
                EnumerateSceneComponents<InteractionRunController>(scene).ToArray();
            InteractionCaptureSampler[] samplers =
                EnumerateSceneComponents<InteractionCaptureSampler>(scene).ToArray();
            if (clients.Length > 1 || controllers.Length > 1 ||
                samplers.Length > 1)
            {
                throw new InvalidOperationException(
                    "W6 setup preflight requires zero or one existing client, " +
                    "controller, and sampler in the target scene."
                );
            }
            if ((clients.Length == 1 &&
                 clients[0].transform != runtimeAnchor) ||
                (controllers.Length == 1 &&
                 controllers[0].transform != runtimeAnchor) ||
                (samplers.Length == 1 &&
                 samplers[0].transform != captureAnchor))
            {
                throw new InvalidOperationException(
                    "Existing W6 components are not on their canonical anchors."
                );
            }
            if (controllers.Length == 1 &&
                (controllers[0].RunMode != InteractionRunMode.StandaloneStudy ||
                 controllers[0].DebugOverridesActive ||
                 !controllers[0].RequireHostForStart))
            {
                throw new InvalidOperationException(
                    "Existing W6 controller is not the default Study configuration."
                );
            }
            if (clients.Length == 1)
            {
                var serialized = new SerializedObject(clients[0]);
                SerializedProperty url = serialized.FindProperty("hostBaseUrl");
                if (url == null)
                {
                    throw new InvalidOperationException(
                        "InteractionHostClient hostBaseUrl field is missing."
                    );
                }
                InteractionHostClient.NormalizeHttpBaseUrl(url.stringValue);
            }
        }

        private static void SetObjectReference(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object value)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            Undo.RecordObject(target, "W6 Wire Capture and Host");
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null ||
                property.propertyType != SerializedPropertyType.ObjectReference)
            {
                throw new InvalidOperationException(
                    target.GetType().Name + " is missing object reference " +
                    propertyName + "."
                );
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
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

        private static void RequireInteractionScene(
            Scene scene,
            bool requireCanonicalScenePath)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                (requireCanonicalScenePath && !string.Equals(
                    scene.path,
                    InteractionLabContract.ScenePath,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    requireCanonicalScenePath
                        ? "Load Assets/Scenes/InteractionLab.unity before using W6 setup."
                        : "W6 automation target scene is invalid or not loaded."
                );
            }
        }

        private static Transform RequireTransform(Scene scene, string path)
        {
            string[] segments = path.Split('/');
            GameObject[] roots = scene.GetRootGameObjects().Where(
                item => string.Equals(
                    item.name,
                    segments[0],
                    StringComparison.Ordinal
                )
            ).ToArray();
            if (roots.Length != 1)
            {
                throw new InvalidOperationException(
                    "InteractionLab anchor root is missing or ambiguous: " +
                    segments[0] + "."
                );
            }
            Transform current = roots[0].transform;
            for (int index = 1; index < segments.Length; index++)
            {
                Transform[] matches = current.Cast<Transform>().Where(
                    child => string.Equals(
                        child.name,
                        segments[index],
                        StringComparison.Ordinal
                    )
                ).ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidOperationException(
                        "InteractionLab anchor is missing or ambiguous: " +
                        path + "."
                    );
                }
                current = matches[0];
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
