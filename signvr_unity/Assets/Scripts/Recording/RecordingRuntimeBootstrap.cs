using System;
using System.Linq;
using Meta.XR.Movement.Retargeting;
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
        private const string RecordingSourceName = "RecordingSource";
        private static bool registered;

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

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstall(scene);
        }

        private static void TryInstall(Scene scene)
        {
            if (scene.path != VrRoomScenePath)
            {
                return;
            }

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

            Transform hmd = FindHead(scene);
            OVRHand leftHand;
            OVRHand rightHand;
            FindHands(scene, out leftHand, out rightHand);

            HandCaptureBoundaryMonitor boundaryMonitor = null;
            if (hmd != null && leftHand != null && rightHand != null)
            {
                WarningUi warningUi = CreateWarningUi(hmd, scene);
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

            QuestDeviceGateway gateway =
                rootObject.AddComponent<QuestDeviceGateway>();
            gateway.Configure(
                coordinator,
                recorder,
                streamer,
                uploader,
                preview,
                boundaryMonitor
            );

            RecordingDebugInput debugInput =
                rootObject.AddComponent<RecordingDebugInput>();
            debugInput.Configure(coordinator);

            coordinator.Configure(recorder);
            uploader.Configure(recorder);

            rootObject.SetActive(true);
            ConfigureExistingCanvas(scene, coordinator);
            Debug.Log(
                "[RecordingRuntimeBootstrap] Installed take recording stack in " +
                scene.path + ". UDP control=5006, pose/announce=5005."
            );
        }

        private static void ConfigureExistingCanvas(
            Scene scene,
            RecordingCoordinator coordinator)
        {
            MetaMotionRecorderUI legacyUi = FindInScene<MetaMotionRecorderUI>(scene);
            Canvas canvas = legacyUi != null
                ? legacyUi.GetComponent<Canvas>()
                : FindInScene<Canvas>(scene);
            if (canvas == null)
            {
                return;
            }

            if (legacyUi != null)
            {
                legacyUi.DetachControls();
            }

            Button start = FindNamed<Button>(canvas.transform, "Start");
            Button stop = FindNamed<Button>(canvas.transform, "Stop");
            TMP_Text status = FindNamed<TMP_Text>(canvas.transform, "Status");
            TMP_Text prompt = FindNamed<TMP_Text>(canvas.transform, "RecordingPrompt");

            if (prompt == null)
            {
                prompt = CreatePromptText(canvas.transform);
            }

            RecordingCoordinatorUIBridge bridge =
                canvas.gameObject.GetComponent<RecordingCoordinatorUIBridge>() ??
                canvas.gameObject.AddComponent<RecordingCoordinatorUIBridge>();
            bridge.Configure(coordinator, start, stop, status, prompt);
        }

        private static TMP_Text CreatePromptText(Transform parent)
        {
            GameObject promptObject = new GameObject(
                "RecordingPrompt",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );
            promptObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)promptObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 66f);
            rect.sizeDelta = new Vector2(560f, 54f);

            TextMeshProUGUI text = promptObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static T FindNamed<T>(Transform root, string token)
            where T : Component
        {
            string normalized = token.ToLowerInvariant();
            return root.GetComponentsInChildren<T>(true)
                .FirstOrDefault(component =>
                    component.name.ToLowerInvariant().Contains(normalized));
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

        private static WarningUi CreateWarningUi(Transform hmd, Scene scene)
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
