using System;
using System.IO;
using System.Linq;
using SignVR.Recording;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Authors the six fixed eye poses used by the pointing-data protocol. The
/// Camera components are editable scene references; Quest rendering continues
/// through CenterEyeAnchor after the XR origin is aligned to the selected pose.
/// </summary>
internal static class ConfigurePointingRecording
{
    internal const string ScenePath = "Assets/Scenes/VRroom.unity";
    private const string ViewpointRootName = "RecordingViewpoints";

    private readonly struct ViewpointSpec
    {
        public ViewpointSpec(
            string id,
            string name,
            string displayName,
            Vector3 eye,
            Vector3 target)
        {
            Id = id;
            Name = name;
            DisplayName = displayName;
            Eye = eye;
            Target = target;
        }

        public string Id { get; }
        public string Name { get; }
        public string DisplayName { get; }
        public Vector3 Eye { get; }
        public Vector3 Target { get; }
    }

    private static readonly ViewpointSpec[] Specs =
    {
        new(
            "state_01",
            "RecordingCamera_State01_Box",
            "Box and keypad",
            new Vector3(-3.2f, 1.55f, -3.7f),
            new Vector3(-4.3f, 1.2f, -5.2f)
        ),
        new(
            "state_02",
            "RecordingCamera_State02_CoinPlate",
            "Coin and plates",
            new Vector3(-4.1f, 1.55f, -3.5f),
            new Vector3(-4.55f, 1.15f, -5.1f)
        ),
        new(
            "state_03",
            "RecordingCamera_State03_Picture",
            "Picture frame",
            new Vector3(-0.9f, 1.55f, -3.75f),
            new Vector3(-0.888f, 2.15f, -6f)
        ),
        new(
            "state_04",
            "RecordingCamera_State04_Chest",
            "Chest and key",
            new Vector3(3.6f, 1.55f, -3.65f),
            new Vector3(4.35f, 1.1f, -5.15f)
        ),
        new(
            "state_05",
            "RecordingCamera_State05_Closet",
            "Closet and button",
            new Vector3(4.25f, 1.55f, -2.5f),
            new Vector3(5.78f, 1.2f, -3f)
        ),
        new(
            "state_06",
            "RecordingCamera_State06_Switches",
            "Electrical switches",
            new Vector3(4.3f, 1.6f, -0.9f),
            new Vector3(6.03f, 1.65f, -0.75f)
        )
    };

    [MenuItem("Tools/SignVR/Configure Pointing Recording")]
    private static void ConfigureFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"Open {ScenePath} before running this command."
            );
        }

        ConfigureScene(scene, saveScene: true);
    }

    public static void ConfigureSceneForAutomation()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
        }
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        ConfigureScene(scene, saveScene: true);
    }

    [MenuItem("Tools/SignVR/Validate Pointing Recording")]
    private static void ValidateFromMenu()
    {
        ValidateScene(SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/SignVR/Simulation/Toggle Recording")]
    private static void ToggleEditorSimulationRecording()
    {
        if (!Application.isPlaying)
        {
            throw new InvalidOperationException(
                "Enter Play Mode before using the recording simulation command."
            );
        }

        RecordingCoordinator coordinator =
            UnityEngine.Object.FindAnyObjectByType<RecordingCoordinator>();
        if (coordinator == null)
        {
            throw new InvalidOperationException(
                "The runtime recording coordinator is not available."
            );
        }

        bool accepted;
        switch (coordinator.State)
        {
            case RecordingFlowState.Ready:
                accepted = coordinator.BeginCurrentTake();
                break;
            case RecordingFlowState.Countdown:
            case RecordingFlowState.Recording:
            case RecordingFlowState.Reviewing:
                accepted = coordinator.StopCurrentTake();
                break;
            default:
                accepted = false;
                break;
        }

        if (!accepted)
        {
            throw new InvalidOperationException(
                $"Recording toggle was rejected in state {coordinator.State}."
            );
        }

        Debug.Log(
            $"[SignVR] Editor recording toggle accepted; state={coordinator.State}."
        );
    }

    [MenuItem("Tools/SignVR/Simulation/Toggle Recording", true)]
    private static bool CanToggleEditorSimulationRecording()
    {
        return Application.isPlaying;
    }

    public static void ValidateSceneForAutomation()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        ValidateScene(scene);
    }

    private static void ConfigureScene(Scene scene, bool saveScene)
    {
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"The active scene must be {ScenePath}."
            );
        }

        VRPlayerRig player = FindInScene<VRPlayerRig>(scene);
        OVRCameraRig cameraRig = FindInScene<OVRCameraRig>(scene);
        if (player == null || cameraRig == null ||
            cameraRig.centerEyeAnchor == null)
        {
            throw new InvalidOperationException(
                "Configure the VRroom player before the recording viewpoints."
            );
        }

        GameObject root = FindRootByName(scene, ViewpointRootName);
        if (root == null)
        {
            root = new GameObject(ViewpointRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(
                root,
                "Create recording viewpoint cameras"
            );
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
        }

        var entries = new RecordingViewpoint[Specs.Length];
        for (int index = 0; index < Specs.Length; index++)
        {
            ViewpointSpec spec = Specs[index];
            Camera camera = EnsureReferenceCamera(root.transform, spec);
            entries[index] = new RecordingViewpoint(
                spec.Id,
                spec.DisplayName,
                camera.transform,
                camera
            );
        }

        RecordingViewpointController controller =
            root.GetComponent<RecordingViewpointController>() ??
            Undo.AddComponent<RecordingViewpointController>(root);
        controller.ConfigureViewpoints(entries, 0);
        controller.Configure(null, player);
        EditorUtility.SetDirty(controller);

        RecordingSentenceSequence sequence =
            root.GetComponent<RecordingSentenceSequence>() ??
            Undo.AddComponent<RecordingSentenceSequence>(root);
        EditorUtility.SetDirty(sequence);

        ConfigureXrWorldFrame(cameraRig);
        NormalizeCameraTags(scene, cameraRig.centerEyeAnchor.GetComponent<Camera>());

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene && !EditorSceneManager.SaveScene(scene))
        {
            throw new IOException($"Unity could not save {ScenePath}.");
        }

        ValidateScene(scene);
        Debug.Log(
            "[SignVR] Pointing recording configured: six fixed world-space " +
            "camera poses, FloorLevel tracking, and recentering disabled."
        );
    }

    private static Camera EnsureReferenceCamera(
        Transform parent,
        ViewpointSpec spec)
    {
        Transform existing = parent.Find(spec.Name);
        bool created = existing == null;
        GameObject cameraObject;
        if (created)
        {
            cameraObject = new GameObject(spec.Name, typeof(Camera));
            cameraObject.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(
                cameraObject,
                "Create recording reference camera"
            );
        }
        else
        {
            cameraObject = existing.gameObject;
        }

        if (created)
        {
            Undo.RecordObject(cameraObject.transform, "Place recording camera");
            Vector3 direction = spec.Target - spec.Eye;
            cameraObject.transform.SetPositionAndRotation(
                spec.Eye,
                Quaternion.LookRotation(direction.normalized, Vector3.up)
            );
            cameraObject.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(cameraObject.transform);
        }

        cameraObject.tag = "Untagged";

        Camera camera = cameraObject.GetComponent<Camera>() ??
                        Undo.AddComponent<Camera>(cameraObject);
        Undo.RecordObject(camera, "Configure recording reference camera");
        camera.enabled = false;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 100f;
        camera.fieldOfView = 72f;
        camera.depth = 10f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.stereoTargetEye = StereoTargetEyeMask.None;

        AudioListener listener = cameraObject.GetComponent<AudioListener>();
        if (listener != null)
        {
            listener.enabled = false;
            EditorUtility.SetDirty(listener);
        }

        EditorUtility.SetDirty(camera);
        return camera;
    }

    private static void ConfigureXrWorldFrame(OVRCameraRig cameraRig)
    {
        OVRManager manager = cameraRig.GetComponent<OVRManager>();
        if (manager == null)
        {
            throw new InvalidOperationException(
                "The OVRCameraRig has no OVRManager."
            );
        }

        SerializedObject data = new(manager);
        SetIntIfPresent(
            data,
            "_trackingOriginType",
            (int)OVRManager.TrackingOrigin.FloorLevel
        );
        SetBoolIfPresent(data, "resetTrackerOnLoad", false);
        SetBoolIfPresent(data, "AllowRecenter", false);
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }

    private static void NormalizeCameraTags(Scene scene, Camera centerEye)
    {
        foreach (Camera camera in FindAllInScene<Camera>(scene))
        {
            camera.tag = camera == centerEye ? "MainCamera" : "Untagged";
            EditorUtility.SetDirty(camera.gameObject);
        }
    }

    private static void ValidateScene(Scene scene)
    {
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"The active scene must be {ScenePath}."
            );
        }

        RecordingViewpointController controller =
            FindInScene<RecordingViewpointController>(scene);
        if (controller == null || controller.ViewpointCount != Specs.Length ||
            !controller.ValidateConfiguration(true))
        {
            throw new InvalidOperationException(
                "The six recording viewpoints are missing or invalid."
            );
        }

        RecordingSentenceSequence sequence =
            FindInScene<RecordingSentenceSequence>(scene);
        if (sequence == null || sequence.SentenceCount != Specs.Length)
        {
            throw new InvalidOperationException(
                "The editable six-sentence recording sequence is missing."
            );
        }

        for (int index = 0; index < Specs.Length; index++)
        {
            RecordingViewpoint entry = controller.Viewpoints[index];
            if (entry == null || entry.ReferenceCamera == null ||
                entry.ReferenceCamera.enabled ||
                entry.ReferenceCamera.CompareTag("MainCamera") ||
                !string.Equals(entry.Id, Specs[index].Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Recording viewpoint {index + 1} is not configured safely."
                );
            }

            Vector3 position = entry.Pose.position;
            Quaternion rotation = entry.Pose.rotation;
            if (!IsFinite(position.x) || !IsFinite(position.y) ||
                !IsFinite(position.z) || !IsFinite(rotation.x) ||
                !IsFinite(rotation.y) || !IsFinite(rotation.z) ||
                !IsFinite(rotation.w))
            {
                throw new InvalidOperationException(
                    $"Recording viewpoint {entry.Id} has an invalid pose."
                );
            }
        }

        OVRCameraRig cameraRig = FindInScene<OVRCameraRig>(scene);
        Camera centerEye = cameraRig?.centerEyeAnchor?.GetComponent<Camera>();
        Camera[] mainCameras = FindAllInScene<Camera>(scene)
            .Where(camera => camera.CompareTag("MainCamera"))
            .ToArray();
        if (centerEye == null || mainCameras.Length != 1 ||
            mainCameras[0] != centerEye)
        {
            throw new InvalidOperationException(
                "CenterEyeAnchor must be the only MainCamera."
            );
        }

        OVRManager manager = cameraRig.GetComponent<OVRManager>();
        SerializedObject data = new(manager);
        SerializedProperty origin = data.FindProperty("_trackingOriginType");
        SerializedProperty reset = data.FindProperty("resetTrackerOnLoad");
        SerializedProperty recenter = data.FindProperty("AllowRecenter");
        if (origin == null ||
            origin.intValue != (int)OVRManager.TrackingOrigin.FloorLevel ||
            reset == null || reset.boolValue ||
            recenter == null || recenter.boolValue)
        {
            throw new InvalidOperationException(
                "Quest tracking must use FloorLevel with automatic recentering disabled."
            );
        }

        Debug.Log(
            "[SignVR] Pointing recording validation passed: six viewpoints and " +
            "one XR render camera."
        );
    }

    private static void SetBoolIfPresent(
        SerializedObject data,
        string propertyName,
        bool value)
    {
        SerializedProperty property = data.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void SetIntIfPresent(
        SerializedObject data,
        string propertyName,
        int value)
    {
        SerializedProperty property = data.FindProperty(propertyName);
        if (property != null)
        {
            property.intValue = value;
        }
    }

    private static GameObject FindRootByName(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == name);
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        return FindAllInScene<T>(scene).FirstOrDefault();
    }

    private static T[] FindAllInScene<T>(Scene scene) where T : Component
    {
        return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include
            )
            .Where(component => component.gameObject.scene == scene)
            .ToArray();
    }
}
