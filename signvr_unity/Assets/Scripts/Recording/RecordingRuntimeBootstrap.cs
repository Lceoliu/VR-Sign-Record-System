using System;
using System.Linq;
using System.Reflection;
using Meta.XR.Movement.Retargeting;
using Oculus.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SignVR.Recording
{
    /// <summary>
    /// Installs the take-aware recording stack around the recorder that is
    /// already authored in a scene.  This is intentionally additive: the
    /// existing OVR rig, hand sources and body provider remain untouched.
    /// Scenes without a MetaBodyMotionRecorder are left alone.
    /// </summary>
    internal static class RecordingRuntimeBootstrap
    {
        private const string VrRoomScenePath = "Assets/Scenes/VRroom.unity";
        private const string VrRoomSceneName = "VRroom";
        private const string RecordingSourceName = "RecordingSource";
        private static bool registered;
        private static readonly FieldInfo HideRayWithoutInteractableField =
            typeof(RayInteractorRayVisual).GetField(
                "_hideWhenNoInteractable",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallActiveScene()
        {
            // Player startup can complete the first scene load before another
            // subsystem subscribes to sceneLoaded. Always inspect the active
            // scene once as well; TryInstall is idempotent.
            TryInstall(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstall(scene);
        }

        private static void TryInstall(Scene scene)
        {
            if (scene.path != VrRoomScenePath && scene.name != VrRoomSceneName)
            {
                return;
            }

            // The desktop operator must be able to use the host console while
            // Unity keeps counting down, sampling, and receiving UDP commands.
            Application.runInBackground = true;

            GameObject recordingSource = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == RecordingSourceName);
            MetaBodyMotionRecorder recorder = recordingSource != null
                ? recordingSource.GetComponent<MetaBodyMotionRecorder>()
                : null;
            if (recorder == null ||
                FindInScene<RecordingCoordinator>(scene) != null)
            {
                return;
            }

            MetaSourceDataProvider provider = recorder.SourceDataProvider ??
                FindRecordingProvider(scene);
            if (provider == null)
            {
                Debug.LogWarning(
                    "[RecordingRuntimeBootstrap] A recorder exists in " +
                    $"{scene.path}, but no MetaSourceDataProvider is available. " +
                    "The take stack was not created."
                );
                return;
            }

            recorder.ConfigureSource(provider);
            recorder.ConfigureManagedRecording();
            provider.DebugDrawSkeleton = false;

            MetaBodyMotionStreamer streamer =
                recordingSource.GetComponent<MetaBodyMotionStreamer>();
            if (streamer == null)
            {
                streamer = recordingSource.AddComponent<MetaBodyMotionStreamer>();
            }
            streamer.ConfigureSource(provider);

            GameObject rootObject = new GameObject("_Recording");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            rootObject.SetActive(false);

            RecordingCoordinator coordinator =
                rootObject.AddComponent<RecordingCoordinator>();
            QuestTakeUploader uploader =
                rootObject.AddComponent<QuestTakeUploader>();
            QuestPreviewStreamer preview =
                rootObject.AddComponent<QuestPreviewStreamer>();
            preview.ConfigureDisabled();

            VRPlayerRig playerRig = FindInScene<VRPlayerRig>(scene);
            RecordingViewpointController viewpointController =
                FindInScene<RecordingViewpointController>(scene);

            Transform hmd = FindHead(scene);
            TMP_FontAsset chineseFont = Resources.Load<TMP_FontAsset>(
                "Fonts/SignVRChinese SDF"
            );
            if (chineseFont == null)
            {
                Debug.LogWarning(
                    "[RecordingRuntimeBootstrap] Chinese TMP font was not " +
                    "found; prompt glyphs may be missing."
                );
            }
            OVRHand leftHand;
            OVRHand rightHand;
            FindHands(scene, out leftHand, out rightHand);

            HandCaptureBoundaryMonitor boundaryMonitor = null;
            if (hmd != null && leftHand != null && rightHand != null)
            {
                WarningUi warningUi = CreateWarningUi(
                    hmd,
                    scene,
                    chineseFont
                );
                boundaryMonitor =
                    rootObject.AddComponent<HandCaptureBoundaryMonitor>();
                boundaryMonitor.Configure(
                    coordinator,
                    hmd,
                    leftHand,
                    rightHand,
                    warningUi.Root,
                    warningUi.Label,
                    null
                );
                recorder.ConfigureBoundaryMonitor(boundaryMonitor);
            }

            coordinator.Configure(recorder);
            uploader.Configure(recorder);

            if (viewpointController != null)
            {
                viewpointController.Configure(coordinator, playerRig);
                coordinator.ConfigureViewpointController(viewpointController);
            }
            else
            {
                Debug.LogError(
                    "[RecordingRuntimeBootstrap] No recording viewpoint " +
                    "controller is authored in VRroom. Run Tools/SignVR/" +
                    "Configure Pointing Recording."
                );
            }

            RecordingSentenceSequence sentenceSequence =
                FindInScene<RecordingSentenceSequence>(scene) ??
                rootObject.AddComponent<RecordingSentenceSequence>();
            sentenceSequence.Configure(
                coordinator,
                viewpointController,
                advanceAutomatically: false,
                useHostAuthority: true
            );

            RecordingSpatialMetadataProvider spatialMetadata =
                rootObject.AddComponent<RecordingSpatialMetadataProvider>();
            spatialMetadata.Configure(
                viewpointController,
                playerRig,
                playerRig != null ? playerRig.FloorCollider : null,
                "VRroom-world-v1"
            );
            recorder.ConfigureSpatialMetadataProvider(spatialMetadata);

            RecordingModeController recordingMode =
                rootObject.AddComponent<RecordingModeController>();
            recordingMode.Configure(playerRig);

            RecordingTargetPoseLock targetPoseLock =
                rootObject.AddComponent<RecordingTargetPoseLock>();
            targetPoseLock.Configure(ResolvePointingTargets(scene));

            HandVisual[] handVisuals = FindAllInScene<HandVisual>(scene);
            RestoreTrackedHandVisuals(handVisuals);
            ConfigureNativeHandRays(scene);

            Camera hmdCamera = hmd != null ? hmd.GetComponent<Camera>() : null;
            RecordingTargetVisualCues targetVisualCues =
                rootObject.AddComponent<RecordingTargetVisualCues>();
            targetVisualCues.Configure(coordinator, hmdCamera);

            RecordingPointingTargetController targetController =
                rootObject.AddComponent<RecordingPointingTargetController>();
            targetController.Configure(sentenceSequence, targetVisualCues);

            QuestDeviceGateway gateway =
                rootObject.AddComponent<QuestDeviceGateway>();
            gateway.Configure(
                coordinator,
                recorder,
                streamer,
                uploader,
                preview,
                boundaryMonitor,
                sentenceSequence,
                viewpointController
            );

            RecordingDebugInput debugInput =
                rootObject.AddComponent<RecordingDebugInput>();
            debugInput.Configure(coordinator, null, sentenceSequence);

            rootObject.SetActive(true);
            Debug.Log(
                "[RecordingRuntimeBootstrap] Meta tracked hand visuals and " +
                "native hand-ray interactors are enabled."
            );
            if (hmd != null)
            {
                RecordingPromptBubble.EnsureCreated(
                    hmd,
                    coordinator,
                    sentenceSequence,
                    chineseFont,
                    viewpointController
                );
            }
            DisableLegacyRecorderCanvas(scene);
            Debug.Log(
                "[RecordingRuntimeBootstrap] Installed take recording stack in " +
                scene.path + ". Six fixed viewpoints, local sequence, prompt " +
                "bubble, target cues, native hand rays, and physics isolation are " +
                "active. Sentence selection is host-only. UDP control=5006, " +
                "pose/announce=5005."
            );
        }

        private static void DisableLegacyRecorderCanvas(Scene scene)
        {
            MetaMotionRecorderUI legacyUi = FindInScene<MetaMotionRecorderUI>(scene);
            if (legacyUi == null)
            {
                return;
            }

            legacyUi.DetachControls();
            legacyUi.enabled = false;
            Canvas legacyCanvas = legacyUi.GetComponent<Canvas>();
            if (legacyCanvas != null)
            {
                legacyCanvas.enabled = false;
            }
            GraphicRaycaster raycaster = legacyUi.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false;
            }
        }

        private static Transform FindHead(Scene scene)
        {
            // Prefer the active OVR eye anchor.  Scenes can retain an inactive
            // legacy MainCamera, and attaching guidance to that camera leaves
            // the warning outside the user's tracked view.
            OVRCameraRig cameraRig = FindInScene<OVRCameraRig>(scene);
            if (cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                return cameraRig.centerEyeAnchor;
            }

            Transform eyeAnchor = FindInScene<Transform>(scene, "CenterEyeAnchor");
            if (eyeAnchor != null)
            {
                return eyeAnchor;
            }

            Camera camera = FindAllInScene<Camera>(scene)
                .FirstOrDefault(candidate => candidate.isActiveAndEnabled);
            return camera != null ? camera.transform : null;
        }

        private static void FindHands(
            Scene scene,
            out OVRHand left,
            out OVRHand right)
        {
            left = null;
            right = null;
            foreach (OVRHand hand in FindAllInScene<OVRHand>(scene)
                         .OrderByDescending(item =>
                             item.isActiveAndEnabled &&
                             item.gameObject.activeInHierarchy))
            {
                OVRPlugin.Hand handedness = hand.GetHand();
                string path = GetPath(hand.transform).ToLowerInvariant();
                bool isLeft = handedness == OVRPlugin.Hand.HandLeft ||
                              path.Contains("lefthandanchor") ||
                              path.Contains("hand tracking left");
                bool isRight = handedness == OVRPlugin.Hand.HandRight ||
                               path.Contains("righthandanchor") ||
                               path.Contains("hand tracking right");
                if (isLeft && left == null)
                {
                    left = hand;
                }
                else if (isRight && right == null)
                {
                    right = hand;
                }
            }
        }

        private readonly struct WarningUi
        {
            public WarningUi(GameObject root, TMP_Text label)
            {
                Root = root;
                Label = label;
            }

            public GameObject Root { get; }
            public TMP_Text Label { get; }
        }

        private static WarningUi CreateWarningUi(
            Transform hmd,
            Scene scene,
            TMP_FontAsset chineseFont)
        {
            GameObject canvasObject = new GameObject(
                "RecordingBoundaryWarning",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler)
            );
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.transform.SetParent(hmd, false);
            canvasObject.transform.localPosition = new Vector3(0f, -0.2f, 0.8f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;

            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(640f, 80f);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            GameObject labelObject = new GameObject(
                "WarningText",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );
            labelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            if (chineseFont != null)
            {
                label.font = chineseFont;
            }
            label.fontSize = 24f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.4f, 0.2f, 1f);
            label.raycastTarget = false;
            return new WarningUi(canvasObject, label);
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            return FindAllInScene<T>(scene).FirstOrDefault();
        }

        private static void RestoreTrackedHandVisuals(HandVisual[] handVisuals)
        {
            if (handVisuals == null)
            {
                return;
            }

            foreach (HandVisual visual in handVisuals)
            {
                if (visual == null)
                {
                    continue;
                }

                visual.enabled = true;
                visual.ForceOffVisibility = false;
            }
        }

        private static void ConfigureNativeHandRays(Scene scene)
        {
            RayInteractor[] interactors = FindAllInScene<RayInteractor>(scene)
                .Where(interactor => GetPath(interactor.transform).IndexOf(
                    "HandRayInteractor",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0)
                .ToArray();

            foreach (RayInteractor interactor in interactors)
            {
                interactor.enabled = true;
                foreach (RayInteractorRayVisual visual in
                         interactor.GetComponentsInChildren<RayInteractorRayVisual>(
                             true
                         ))
                {
                    visual.enabled = true;
                    visual.RayVisualStartOffset = 0.025f;
                    visual.RayVisualEndOffset = 0.02f;
                    visual.MaxRayVisualLength = 1.5f;
                    HideRayWithoutInteractableField?.SetValue(visual, false);
                }
            }

            if (interactors.Length < 2)
            {
                Debug.LogWarning(
                    "[RecordingRuntimeBootstrap] Expected two Meta native " +
                    $"HandRayInteractors, found {interactors.Length}."
                );
            }
        }

        private static Transform[] ResolvePointingTargets(Scene scene)
        {
            return RecordingPointingSentenceCatalog.CreateSentences()
                .SelectMany(sentence => sentence.HighlightTargetIds)
                .Distinct(StringComparer.Ordinal)
                .Select(id => ResolveSceneTarget(scene, id))
                .Where(target => target != null)
                .ToArray();
        }

        private static Transform ResolveSceneTarget(Scene scene, string id)
        {
            string[] path = id.Split('/');
            Transform root = scene.GetRootGameObjects()
                .FirstOrDefault(candidate => candidate.name == path[0])
                ?.transform;
            return path.Length == 1
                ? root
                : root?.Find(string.Join("/", path.Skip(1)));
        }

        private static MetaSourceDataProvider FindRecordingProvider(Scene scene)
        {
            MetaSourceDataProvider named = FindAllInScene<MetaSourceDataProvider>(scene)
                .FirstOrDefault(provider =>
                    provider.name == "MetaBodyTrackingSource" ||
                    GetPath(provider.transform).IndexOf(
                        "/MetaBodyTrackingSource",
                        StringComparison.Ordinal
                    ) >= 0);
            return named ?? FindInScene<MetaSourceDataProvider>(scene);
        }

        private static T FindInScene<T>(Scene scene, string name)
            where T : Component
        {
            return FindAllInScene<T>(scene)
                .FirstOrDefault(component => component.name == name);
        }

        private static T[] FindAllInScene<T>(Scene scene) where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(
                    FindObjectsInactive.Include
                )
                .Where(component => component.gameObject.scene == scene)
                .ToArray();
        }

        private static string GetPath(Transform current)
        {
            var names = new System.Collections.Generic.List<string>();
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
