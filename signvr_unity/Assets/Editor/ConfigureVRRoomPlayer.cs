using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Editor;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Meta.XR.Movement.Retargeting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Installs the reusable VR player and physics contract into VRroom.unity.
/// The operation is additive and idempotent, so it is safe to run again after
/// adding more room meshes or props.
/// </summary>
[InitializeOnLoad]
internal static class ConfigureVRRoomPlayer
{
    internal const string ScenePath = "Assets/Scenes/VRroom.unity";
    private const string CameraRigGuid =
        "126d619cf4daa52469682f85c1378b4a";
    private const string InteractionRigGuid =
        "0a7d2469f24041c4284c66706f84c45e";
    private const float AuthoredWorldScale = 0.1f;

    private static readonly HashSet<string> AuthoredWorldRootNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "room",
            "safe",
            "safe (1)",
            "dragon_coin",
            "golden_coin",
            "golden_coin (1)",
            "plate",
            "dragon_plate",
            "plate (1)",
            "picture_frame",
            "white_photo_frame",
            "elevator_button_-_lift",
            "box",
            "door",
            "industrial_button",
            "gold_lock_improved",
            "master_lock",
            "red_button",
            "alarm_button",
            "key",
            "key (1)",
            "motorbike_key"
        };

    private static readonly HashSet<string> MovableNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dragon_coin",
            "golden_coin",
            "golden_coin (1)",
            "key",
            "key (1)",
            "motorbike_key",
            "box",
            "plate",
            "plate (1)"
        };

    static ConfigureVRRoomPlayer()
    {
        EditorApplication.delayCall += ConfigureIfActiveRoomIsMissing;
    }

    [MenuItem("Tools/SignVR/Configure VRroom Player and Physics")]
    private static void ConfigureFromMenu()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"Open {ScenePath} before running this command."
            );
        }

        ConfigureScene(activeScene, saveScene: true);
    }

    // Also exposed for Unity's headless -executeMethod automation and CI.
    public static void ConfigureSceneForAutomation()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        ConfigureScene(scene, saveScene: true);
    }

    [MenuItem("Tools/SignVR/Validate VRroom Player and Physics")]
    private static void ValidateFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"Open {ScenePath} before running this command."
            );
        }

        ValidateScene(scene);
    }

    // Also exposed for Unity's headless -executeMethod automation and CI.
    public static void ValidateSceneForAutomation()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ValidateScene(scene);
    }

    private static void ValidateScene(Scene scene)
    {
        VRPlayerRig player = FindInScene<VRPlayerRig>(scene);
        if (player == null)
        {
            throw new InvalidOperationException("VRPlayerRig is missing.");
        }

        if (player.GetComponent<CharacterController>() == null)
        {
            throw new InvalidOperationException(
                "VRPlayerRig has no CharacterController."
            );
        }

        if (FindInScene<VRInteractionEvents>(scene) == null)
        {
            throw new InvalidOperationException(
                "VRInteractionEvents event hub is missing."
            );
        }

        if (player.XROrigin == null || player.Head == null)
        {
            throw new InvalidOperationException(
                "VRPlayerRig camera-origin references are missing."
            );
        }

        GameObject recordingObject = FindRootByName(scene, "RecordingSource");
        Transform sourceTransform = player.transform.Find(
            "MetaBodyTrackingSource"
        );
        MetaSourceDataProvider provider = sourceTransform != null
            ? sourceTransform.GetComponent<MetaSourceDataProvider>()
            : null;
        MetaBodyMotionRecorder recorder = recordingObject != null
            ? recordingObject.GetComponent<MetaBodyMotionRecorder>()
            : null;
        MetaBodyMotionStreamer streamer = recordingObject != null
            ? recordingObject.GetComponent<MetaBodyMotionStreamer>()
            : null;
        if (recorder == null || streamer == null || provider == null ||
            recorder.SourceDataProvider != provider ||
            streamer.SourceDataProvider != provider)
        {
            throw new InvalidOperationException(
                "VRroom Meta body recording source is missing or not wired."
            );
        }

        Transform spawn = FindRootByName(scene, "PlayerSpawnPoint")?.transform;
        if (spawn == null || spawn.parent != null)
        {
            throw new InvalidOperationException(
                "PlayerSpawnPoint must be a fixed scene-root transform."
            );
        }

        GameObject room = FindRootByName(scene, "room");
        if (room == null || room.GetComponentsInChildren<MeshCollider>(true).Length == 0)
        {
            throw new InvalidOperationException("Room colliders are missing.");
        }

        Bounds roomBounds = CombineRendererBounds(room);
        if (roomBounds.size.x > 20f || roomBounds.size.y > 10f ||
            roomBounds.size.z > 20f)
        {
            throw new InvalidOperationException(
                $"VRroom is not in meter scale. Bounds are {roomBounds.size}."
            );
        }

        if (!TryFindRoomFloor(room, spawn.position, out RaycastHit floorHit) ||
            Mathf.Abs(spawn.position.y - (floorHit.point.y + 0.05f)) > 0.2f)
        {
            throw new InvalidOperationException(
                $"PlayerSpawnPoint is not grounded inside VRroom: {spawn.position}."
            );
        }

        if (!HasRoomSurfaceInView(room, spawn))
        {
            throw new InvalidOperationException(
                "PlayerSpawnPoint does not face visible room geometry."
            );
        }

        if (player.transform.localScale != Vector3.one ||
            player.XROrigin.localScale != Vector3.one)
        {
            throw new InvalidOperationException(
                "VRPlayer and its XR origin must remain at meter scale."
            );
        }

        OVRCameraRig cameraRig = FindInScene<OVRCameraRig>(scene);
        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            throw new InvalidOperationException("OVRCameraRig/center eye is missing.");
        }

        SyntheticHand[] hands =
            cameraRig.GetComponentsInChildren<SyntheticHand>(true);
        int configuredHands = 0;
        foreach (SyntheticHand hand in hands)
        {
            HandVisual visual = hand.GetComponentInChildren<HandVisual>(true);
            VRHandPhysicsLimiter limiter =
                hand.GetComponent<VRHandPhysicsLimiter>();
            HandPhysicsCapsules capsules =
                visual != null
                    ? visual.GetComponent<HandPhysicsCapsules>()
                    : null;
            if (visual == null || limiter == null ||
                capsules == null || !HasSolidCapsules(capsules))
            {
                continue;
            }
            configuredHands++;
            Debug.Log(
                $"[SignVR] Hand validated: {GetHierarchyPath(hand.transform)} " +
                $"visualActive={visual.gameObject.activeInHierarchy}"
            );
        }

        if (configuredHands < 2)
        {
            throw new InvalidOperationException(
                $"Expected at least two configured physical hands, found {configuredHands}."
            );
        }

        Debug.Log("[SignVR] VRroom player and physics validation passed.");
    }

    private static bool HasSolidCapsules(HandPhysicsCapsules capsules)
    {
        SerializedObject serialized = new SerializedObject(capsules);
        SerializedProperty asTriggers = serialized.FindProperty("_asTriggers");
        return asTriggers == null || !asTriggers.boolValue;
    }

    private static string GetHierarchyPath(Transform current)
    {
        List<string> names = new List<string>();
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static void ConfigureIfActiveRoomIsMissing()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += ConfigureIfActiveRoomIsMissing;
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.path != ScenePath ||
            FindInScene<VRPlayerRig>(activeScene) != null)
        {
            return;
        }

        ConfigureScene(activeScene, saveScene: true);
    }

    private static void ConfigureScene(Scene scene, bool saveScene)
    {
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            throw new InvalidOperationException(
                $"The active scene must be {ScenePath}."
            );
        }

        GameObject room = FindRootByName(scene, "room");
        if (room == null)
        {
            throw new InvalidOperationException(
                "The VRroom scene does not contain the imported room object."
            );
        }

        NormalizeAuthoredWorld(scene);
        EnsureRoomColliders(room);
        ConfigureProps(scene, room);

        GameObject playerObject = FindRootByName(scene, "VRPlayer");
        if (playerObject == null)
        {
            playerObject = new GameObject("VRPlayer");
            SceneManager.MoveGameObjectToScene(playerObject, scene);
            Undo.RegisterCreatedObjectUndo(playerObject, "Create VR player");
        }

        VRInteractionEvents eventHub =
            GetOrAddComponent<VRInteractionEvents>(playerObject);
        VRPlayerRig player = GetOrAddComponent<VRPlayerRig>(playerObject);

        Transform spawnPoint = EnsureSpawnPoint(scene, room);
        player.SetSpawnPoint(spawnPoint, false);

        OVRCameraRig cameraRig = EnsureCameraRig(scene, playerObject.transform);
        player.ConfigureSceneReferences(
            cameraRig.transform,
            cameraRig.centerEyeAnchor
        );
        EnsureInteractionRig(scene, cameraRig);
        ConfigureTrackingOrigin(cameraRig);
        ConfigureHandVisualsAndPhysics(cameraRig);
        ConfigureRecording(scene, playerObject, cameraRig);

        player.SetSpawnPoint(spawnPoint, false);
        player.CaptureSpawnPose();
        ConfigureBuildSettings();

        EditorUtility.SetDirty(eventHub);
        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene && !EditorSceneManager.SaveScene(scene))
        {
            throw new IOException($"Unity could not save {ScenePath}.");
        }

        Debug.Log(
            "[SignVR] VRroom configured: player movement, fixed spawn, " +
            "room/prop colliders, hand visuals, hand physics, event hub, " +
            "and Meta body recording source."
        );
    }

    private static void ConfigureRecording(
        Scene scene,
        GameObject playerObject,
        OVRCameraRig cameraRig)
    {
        MetaSourceDataProvider provider =
            FindRecordingProvider(scene, playerObject);
        if (provider == null)
        {
            GameObject sourceObject = new GameObject("MetaBodyTrackingSource");
            SceneManager.MoveGameObjectToScene(sourceObject, scene);
            Undo.RegisterCreatedObjectUndo(
                sourceObject,
                "Create Meta body tracking source"
            );
            sourceObject.transform.SetParent(playerObject.transform, false);
            provider = Undo.AddComponent<MetaSourceDataProvider>(sourceObject);
        }
        provider.ProvidedSkeletonType = OVRPlugin.BodyJointSet.FullBody;

        GameObject recordingObject = FindRootByName(scene, "RecordingSource");
        if (recordingObject == null)
        {
            recordingObject = new GameObject("RecordingSource");
            SceneManager.MoveGameObjectToScene(recordingObject, scene);
            Undo.RegisterCreatedObjectUndo(
                recordingObject,
                "Create recording source"
            );
        }

        MetaBodyMotionRecorder recorder =
            GetOrAddComponent<MetaBodyMotionRecorder>(recordingObject);
        MetaBodyMotionStreamer streamer =
            GetOrAddComponent<MetaBodyMotionStreamer>(recordingObject);
        recorder.ConfigureSource(provider);
        recorder.ConfigureManagedRecording();
        streamer.ConfigureSource(provider);

        OVRManager manager = cameraRig.GetComponent<OVRManager>();
        if (manager != null)
        {
            SerializedObject serialized = new SerializedObject(manager);
            SerializedProperty permission = serialized.FindProperty(
                "requestBodyTrackingPermissionOnStartup"
            );
            if (permission != null)
            {
                permission.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(manager);
        }

        OVRRuntimeSettings runtimeSettings =
            OVRRuntimeSettings.GetRuntimeSettings();
        runtimeSettings.BodyTrackingJointSet = OVRPlugin.BodyJointSet.FullBody;
        runtimeSettings.BodyTrackingFidelity =
            OVRPlugin.BodyTrackingFidelity2.High;
        EditorUtility.SetDirty(runtimeSettings);

        EditorUtility.SetDirty(provider);
        EditorUtility.SetDirty(recorder);
        EditorUtility.SetDirty(streamer);
    }

    private static OVRCameraRig EnsureCameraRig(Scene scene, Transform player)
    {
        OVRCameraRig cameraRig = FindInScene<OVRCameraRig>(scene);
        if (cameraRig == null)
        {
            GameObject prefab =
                LoadPrefabByGuid(CameraRigGuid, "Meta OVRCameraRig");

            GameObject instance =
                PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Unity could not instantiate the Meta OVRCameraRig."
                );
            }

            instance.name = "[BuildingBlock] Camera Rig";
            Undo.RegisterCreatedObjectUndo(instance, "Create XR camera rig");
            cameraRig = instance.GetComponentInChildren<OVRCameraRig>(true);
        }

        if (cameraRig == null)
        {
            throw new InvalidOperationException(
                "The XR camera rig prefab has no OVRCameraRig component."
            );
        }

        Transform rigRoot = cameraRig.transform;
        if (rigRoot.parent != player)
        {
            Undo.SetTransformParent(
                rigRoot,
                player,
                "Parent XR rig to VR player"
            );
            rigRoot.localPosition = Vector3.zero;
            rigRoot.localRotation = Quaternion.identity;
            rigRoot.localScale = Vector3.one;
        }

        Camera centerEye = cameraRig.centerEyeAnchor?.GetComponent<Camera>();
        if (centerEye != null)
        {
            centerEye.tag = "MainCamera";
        }

        GameObject legacyCameraObject = FindRootByName(scene, "MainCamera");
        Camera legacyCamera = legacyCameraObject?.GetComponent<Camera>();
        if (legacyCameraObject != null && legacyCamera != null)
        {
            legacyCamera.enabled = false;
            legacyCameraObject.SetActive(false);
            AudioListener listener = legacyCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
            EditorUtility.SetDirty(legacyCamera);
        }

        return cameraRig;
    }

    private static void EnsureInteractionRig(Scene scene, OVRCameraRig cameraRig)
    {
        OVRCameraRigRef rigRef = FindInScene<OVRCameraRigRef>(scene);
        if (rigRef == null)
        {
            GameObject prefab =
                LoadPrefabByGuid(
                    InteractionRigGuid,
                    "Meta comprehensive interaction rig"
                );

            GameObject instance =
                PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Unity could not instantiate the Meta interaction rig."
                );
            }

            instance.name = "OVRComprehensiveInteractionRig";
            Undo.RegisterCreatedObjectUndo(instance, "Create interaction rig");
            instance.transform.SetParent(cameraRig.transform, false);
            UnityObjectAddedBroadcaster.HandleObjectWasAdded(instance);
            rigRef = instance.GetComponentInChildren<OVRCameraRigRef>(true);
        }

        if (rigRef == null)
        {
            throw new InvalidOperationException(
                "The interaction rig has no OVRCameraRigRef component."
            );
        }

        rigRef.InjectInteractionOVRCameraRig(cameraRig);
        DisableDuplicateCameraRigHandVisuals(cameraRig);
        EditorUtility.SetDirty(rigRef);
    }

    private static void ConfigureTrackingOrigin(OVRCameraRig cameraRig)
    {
        OVRManager manager = cameraRig.GetComponent<OVRManager>();
        if (manager == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("_trackingOriginType");
        if (property != null)
        {
            property.intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        manager.controllerDrivenHandPosesType =
            OVRManager.ControllerDrivenHandPosesType.ConformingToController;
        EditorUtility.SetDirty(manager);
    }

    private static void ConfigureHandVisualsAndPhysics(OVRCameraRig cameraRig)
    {
        SyntheticHand[] hands =
            cameraRig.GetComponentsInChildren<SyntheticHand>(true);
        foreach (SyntheticHand hand in hands)
        {
            HandVisual visual = hand.GetComponentInChildren<HandVisual>(true);
            if (visual == null)
            {
                continue;
            }

            visual.enabled = true;
            foreach (SkinnedMeshRenderer renderer in
                     visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                renderer.enabled = true;
                EditorUtility.SetDirty(renderer);
            }

            JointsRadiusFeature radiusFeature =
                EnsureJointsRadiusFeature(hand);
            VRHandPhysicsLimiter limiter =
                GetOrAddComponent<VRHandPhysicsLimiter>(hand.gameObject);
            limiter.Configure(hand, visual, radiusFeature);
            EditorUtility.SetDirty(limiter);
        }
    }

    private static JointsRadiusFeature EnsureJointsRadiusFeature(
        SyntheticHand syntheticHand
    )
    {
        Hand radiusHand = syntheticHand.ModifyDataFromSource as Hand ??
                           syntheticHand;
        JointsRadiusFeature feature =
            radiusHand.GetComponent<JointsRadiusFeature>() ??
            syntheticHand.GetComponent<JointsRadiusFeature>();
        if (feature == null)
        {
            feature = GetOrAddComponent<JointsRadiusFeature>(
                syntheticHand.gameObject
            );
        }

        SerializedObject serialized = new SerializedObject(feature);
        SerializedProperty handProperty = serialized.FindProperty("_hand");
        if (handProperty != null)
        {
            handProperty.objectReferenceValue = radiusHand;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        feature.enabled = true;
        EditorUtility.SetDirty(feature);
        return feature;
    }

    private static Transform EnsureSpawnPoint(
        Scene scene,
        GameObject room
    )
    {
        Transform marker = FindRootByName(scene, "PlayerSpawnPoint")?.transform;
        bool created = marker == null;
        if (created)
        {
            GameObject markerObject = new GameObject("PlayerSpawnPoint");
            SceneManager.MoveGameObjectToScene(markerObject, scene);
            markerObject.hideFlags = HideFlags.None;
            Undo.RegisterCreatedObjectUndo(
                markerObject,
                "Create fixed VR spawn point"
            );
            marker = markerObject.transform;
        }

        if (created || !IsValidSpawn(room, marker))
        {
            Transform reference = FindRootByName(scene, "MainCamera")?.transform;
            Vector3 referencePosition = reference != null
                ? reference.position
                : CombineRendererBounds(room).center;
            if (!TryFindRoomFloor(room, referencePosition, out RaycastHit floorHit))
            {
                throw new InvalidOperationException(
                    "Could not find a walkable VRroom floor below the reference camera."
                );
            }

            float yaw = reference != null ? reference.eulerAngles.y : 0f;
            marker.SetPositionAndRotation(
                floorHit.point + Vector3.up * 0.05f,
                Quaternion.Euler(0f, yaw, 0f)
            );
            EditorUtility.SetDirty(marker);
        }

        return marker;
    }

    private static bool IsValidSpawn(GameObject room, Transform spawn)
    {
        return TryFindRoomFloor(room, spawn.position, out RaycastHit hit) &&
               Mathf.Abs(spawn.position.y - (hit.point.y + 0.05f)) <= 0.2f &&
               HasRoomSurfaceInView(room, spawn);
    }

    private static bool TryFindRoomFloor(
        GameObject room,
        Vector3 samplePosition,
        out RaycastHit floorHit)
    {
        Physics.SyncTransforms();
        Bounds bounds = CombineRendererBounds(room);
        Vector3 origin = new Vector3(
            samplePosition.x,
            Mathf.Max(bounds.max.y + 1f, samplePosition.y + 1f),
            samplePosition.z
        );
        RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                Mathf.Max(20f, bounds.size.y + 4f),
                ~0,
                QueryTriggerInteraction.Ignore
            )
            .Where(hit => hit.collider.transform.IsChildOf(room.transform) &&
                          hit.normal.y >= 0.65f)
            .OrderByDescending(hit => hit.point.y)
            .ToArray();

        foreach (RaycastHit hit in hits)
        {
            if (GetHierarchyPath(hit.collider.transform)
                .IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                floorHit = hit;
                return true;
            }
        }

        floorHit = hits.FirstOrDefault();
        return floorHit.collider != null;
    }

    private static bool HasRoomSurfaceInView(GameObject room, Transform spawn)
    {
        Vector3 eye = spawn.position + Vector3.up * 1.6f;
        foreach (float yawOffset in new[] { 0f, -30f, 30f, -60f, 60f })
        {
            Vector3 direction =
                Quaternion.Euler(0f, yawOffset, 0f) * spawn.forward;
            RaycastHit[] hits = Physics.RaycastAll(
                eye,
                direction,
                20f,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            if (hits.Any(hit =>
                    hit.collider.transform.IsChildOf(room.transform)))
            {
                return true;
            }
        }
        return false;
    }

    private static void NormalizeAuthoredWorld(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (AuthoredWorldRootNames.Contains(root.name))
            {
                ApplyWorldScale(root, scaleGeometry: true);
            }
        }

        ApplyWorldScale(
            FindRootByName(scene, "MainCamera"),
            scaleGeometry: false
        );

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Light light = root.GetComponent<Light>();
            if (light == null)
            {
                continue;
            }

            float ratio = ApplyWorldScale(root, scaleGeometry: false);
            if (light.type != LightType.Directional &&
                !Mathf.Approximately(ratio, 1f))
            {
                light.range *= ratio;
                EditorUtility.SetDirty(light);
            }
        }
    }

    private static float ApplyWorldScale(
        GameObject root,
        bool scaleGeometry)
    {
        if (root == null)
        {
            return 1f;
        }

        VRWorldScaleMarker marker = root.GetComponent<VRWorldScaleMarker>();
        float previousScale = marker != null && marker.AppliedScale > 0f
            ? marker.AppliedScale
            : 1f;
        float ratio = AuthoredWorldScale / previousScale;
        if (!Mathf.Approximately(ratio, 1f))
        {
            Undo.RecordObject(root.transform, "Normalize VRroom world scale");
            root.transform.position *= ratio;
            if (scaleGeometry)
            {
                root.transform.localScale *= ratio;
            }
            EditorUtility.SetDirty(root.transform);
        }

        marker ??= Undo.AddComponent<VRWorldScaleMarker>(root);
        marker.SetAppliedScale(AuthoredWorldScale);
        EditorUtility.SetDirty(marker);
        return ratio;
    }

    private static void EnsureRoomColliders(GameObject room)
    {
        foreach (MeshFilter meshFilter in room.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.sharedMesh == null ||
                meshFilter.GetComponent<Collider>() != null)
            {
                continue;
            }

            MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            EditorUtility.SetDirty(collider);
        }
    }

    private static void ConfigureProps(Scene scene, GameObject room)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == room || root.name == "VRPlayer" ||
                root.name == "MainCamera" || root.GetComponent<Light>() != null)
            {
                continue;
            }

            if (root.GetComponentInChildren<MeshFilter>(true) == null)
            {
                continue;
            }

            VRPhysicalObject physical =
                GetOrAddComponent<VRPhysicalObject>(root);
            physical.SetObjectId(root.name);

            if (!MovableNames.Contains(root.name))
            {
                EnsureStaticMeshColliders(root);
                EditorUtility.SetDirty(physical);
                continue;
            }

            Rigidbody body = GetOrAddComponent<Rigidbody>(root);
            body.mass = 0.25f;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.08f;
            EnsureConvexBoundsCollider(root);
            EnsureGrabInteraction(root);
            VRGrabEventForwarder grabEvents =
                GetOrAddComponent<VRGrabEventForwarder>(root);
            grabEvents.Configure(physical);
            grabEvents.RefreshInteractables();
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(physical);
            EditorUtility.SetDirty(grabEvents);
        }
    }

    private static void EnsureStaticMeshColliders(GameObject root)
    {
        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.sharedMesh == null ||
                meshFilter.GetComponent<Collider>() != null)
            {
                continue;
            }

            MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            EditorUtility.SetDirty(collider);
        }
    }

    private static void EnsureConvexBoundsCollider(GameObject root)
    {
        Collider existing = root.GetComponent<Collider>();
        if (existing == null)
        {
            Bounds bounds = CombineRendererBounds(root);
            BoxCollider box = root.AddComponent<BoxCollider>();
            Vector3 scale = root.transform.lossyScale;
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                SafeDivide(Mathf.Abs(bounds.size.x), Mathf.Abs(scale.x)),
                SafeDivide(Mathf.Abs(bounds.size.y), Mathf.Abs(scale.y)),
                SafeDivide(Mathf.Abs(bounds.size.z), Mathf.Abs(scale.z))
            );
            existing = box;
        }

        if (existing is MeshCollider meshCollider)
        {
            meshCollider.convex = true;
        }
        else if (existing is BoxCollider boxCollider)
        {
            boxCollider.isTrigger = false;
        }
        EditorUtility.SetDirty(existing);
    }

    private static void EnsureGrabInteraction(GameObject target)
    {
        bool hasHandGrab =
            target.GetComponentInChildren<HandGrabInteractable>(true) != null;
        bool hasControllerGrab =
            target.GetComponentInChildren<GrabInteractable>(true) != null;
        if (!hasHandGrab || !hasControllerGrab)
        {
            QuickActionsAPI.AddGrabInteraction(target);
        }
    }

    private static Bounds CombineRendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.one);
        }

        Bounds result = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            result.Encapsulate(renderers[i].bounds);
        }
        return result;
    }

    private static float SafeDivide(float numerator, float denominator)
    {
        return numerator / Mathf.Max(0.0001f, denominator);
    }

    private static GameObject LoadPrefabByGuid(
        string guid,
        string displayName
    )
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"{displayName} could not be loaded (GUID {guid})."
            );
        }
        return prefab;
    }

    private static void DisableDuplicateCameraRigHandVisuals(
        OVRCameraRig cameraRig
    )
    {
        if (cameraRig.trackingSpace == null)
        {
            return;
        }

        foreach (OVRHand hand in
                 cameraRig.trackingSpace.GetComponentsInChildren<OVRHand>(true))
        {
            SetEnabled(hand.GetComponent<OVRSkeletonRenderer>(), false);
            SetEnabled(hand.GetComponent<OVRMesh>(), false);
            SetEnabled(hand.GetComponent<OVRMeshRenderer>(), false);
            SetEnabled(hand.GetComponent<SkinnedMeshRenderer>(), false);
        }
    }

    private static void SetEnabled(Behaviour behaviour, bool enabled)
    {
        if (behaviour == null)
        {
            return;
        }
        behaviour.enabled = enabled;
        EditorUtility.SetDirty(behaviour);
    }

    private static void SetEnabled(Renderer renderer, bool enabled)
    {
        if (renderer == null)
        {
            return;
        }
        renderer.enabled = enabled;
        EditorUtility.SetDirty(renderer);
    }

    private static void ConfigureBuildSettings()
    {
        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
        if (current.Length == 1 && current[0].enabled &&
            current[0].path == ScenePath)
        {
            return;
        }

        string guid = AssetDatabase.AssetPathToGUID(ScenePath);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        Debug.Log(
            $"[SignVR] Build Settings now contain only {ScenePath} ({guid})."
        );
    }

    private static GameObject FindRootByName(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == name);
    }

    private static MetaSourceDataProvider FindRecordingProvider(
        Scene scene,
        GameObject playerObject)
    {
        if (playerObject != null)
        {
            Transform source = playerObject.transform.Find(
                "MetaBodyTrackingSource"
            );
            MetaSourceDataProvider local = source != null
                ? source.GetComponent<MetaSourceDataProvider>()
                : null;
            if (local != null)
            {
                return local;
            }
        }

        return null;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }
        return null;
    }

    private static T GetOrAddComponent<T>(GameObject target)
        where T : Component
    {
        T existing = target.GetComponent<T>();
        return existing != null ? existing : Undo.AddComponent<T>(target);
    }
}
