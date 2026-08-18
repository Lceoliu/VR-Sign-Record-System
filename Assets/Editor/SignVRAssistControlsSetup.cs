using System.Collections.Generic;
using System.Linq;
using SignVR.Recording;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SignVR.EditorTools
{
    /// <summary>
    /// Wires the assist controls (passthrough, help, boundary glow) into the
    /// recording scene. Re-runnable: existing components and references are
    /// updated in place rather than duplicated.
    /// </summary>
    public static class SignVRAssistControlsSetup
    {
        private const string RecordingRootName = "_Recording";
        private const string ControlsPath =
            "Environment/TouchScreenDevice_03/ScreenArea/SignVRControls";
        private const string GlowName = "BoundaryGlow";
        private const string OverlayLayerName = "Overlay UI";

        [MenuItem("SignVR/Setup/Wire Assist Controls")]
        public static void WireAssistControls()
        {
            GameObject recordingRoot = GameObject.Find(RecordingRootName);
            if (recordingRoot == null)
            {
                Debug.LogError($"[SignVRAssistControlsSetup] '{RecordingRootName}' not found.");
                return;
            }

            var coordinator = recordingRoot.GetComponent<RecordingCoordinator>();
            var gateway = recordingRoot.GetComponent<QuestDeviceGateway>();
            var monitor = recordingRoot.GetComponent<HandCaptureBoundaryMonitor>();
            var presenter = recordingRoot.GetComponent<RecordingTouchscreenPresenter>();
            if (coordinator == null || gateway == null || monitor == null || presenter == null)
            {
                Debug.LogError("[SignVRAssistControlsSetup] Recording components are missing.");
                return;
            }

            var passthrough = EnsureComponent<RecordingPassthroughController>(recordingRoot);
            var help = EnsureComponent<RecordingHelpController>(recordingRoot);

            Camera centerEye = FindCenterEyeCamera();
            OVRManager ovrManager = Object.FindAnyObjectByType<OVRManager>();

            WirePassthrough(passthrough, coordinator, ovrManager, centerEye);
            WireHelp(help, coordinator, gateway);

            GameObject exitButton = GameObject.Find($"{ControlsPath}/ExitImmerse");
            GameObject helpButton = GameObject.Find($"{ControlsPath}/HelpToggle");
            if (exitButton == null || helpButton == null)
            {
                Debug.LogError(
                    "[SignVRAssistControlsSetup] ExitImmerse / HelpToggle not found under SignVRControls."
                );
                return;
            }

            WirePokeAction(exitButton, RecordingPokeAction.ActionType.TogglePassthrough, passthrough, help);
            WirePokeAction(helpButton, RecordingPokeAction.ActionType.ToggleHelp, passthrough, help);

            RecordingBoundaryGlow glow = EnsureBoundaryGlow();
            WireMonitor(monitor, glow);
            WirePresenter(presenter, passthrough, help, exitButton, helpButton);

            SetReference(gateway, "boundaryMonitor", monitor);

            var recorder = Object.FindAnyObjectByType<MetaBodyMotionRecorder>();
            if (recorder != null)
            {
                SetReference(recorder, "boundaryMonitor", monitor);
            }

            EditorUtility.SetDirty(recordingRoot);
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[SignVRAssistControlsSetup] Assist controls wired.");
        }

        private static void WirePassthrough(
            RecordingPassthroughController passthrough,
            RecordingCoordinator coordinator,
            OVRManager ovrManager,
            Camera centerEye)
        {
            var serialized = new SerializedObject(passthrough);
            serialized.FindProperty("coordinator").objectReferenceValue = coordinator;
            serialized.FindProperty("ovrManager").objectReferenceValue = ovrManager;
            serialized.FindProperty("hmdCamera").objectReferenceValue = centerEye;
            serialized.FindProperty("passthroughLayer").objectReferenceValue =
                EnsurePassthroughLayer(ovrManager);

            // Everything under Environment disappears except the desk and its
            // touchscreen: those carry the button that brings the room back.
            var scenery = new List<GameObject>();
            Transform environment = GameObject.Find("Environment")?.transform;
            if (environment != null)
            {
                foreach (Transform child in environment)
                {
                    // The desk and touchscreen carry the button back to the room,
                    // and the lighting rig has to stay on or they would be lit by
                    // nothing and read as black silhouettes over the passthrough.
                    if (child.name is "TouchScreenDevice_03" or "Table_01A" or "SignVR Lighting")
                    {
                        continue;
                    }
                    scenery.Add(child.gameObject);
                }
            }

            var characters = new List<GameObject>();
            AddIfFound(characters, "MirroredObjects");

            var keep = new List<GameObject>();
            AddIfFound(keep, "Environment/Table_01A");
            AddIfFound(keep, "Environment/TouchScreenDevice_03");

            FillList(serialized, "virtualScenery", scenery);
            FillList(serialized, "virtualCharacters", characters);
            FillList(serialized, "keepVisible", keep);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Places the Underlay layer in the scene ahead of time, disabled. Adding
        /// it at runtime is unreliable, and leaving it enabled would blend the whole
        /// frame from startup and wash out the HMD UI.
        /// </summary>
        private static OVRPassthroughLayer EnsurePassthroughLayer(OVRManager ovrManager)
        {
            if (ovrManager == null)
            {
                Debug.LogError("[SignVRAssistControlsSetup] OVRManager not found.");
                return null;
            }

            var layer = ovrManager.GetComponent<OVRPassthroughLayer>();
            if (layer == null)
            {
                layer = ovrManager.gameObject.AddComponent<OVRPassthroughLayer>();
            }

            var serialized = new SerializedObject(layer);
            SerializedProperty overlayType = serialized.FindProperty("projectionSurfaceType");
            if (overlayType != null)
            {
                overlayType.enumValueIndex = 0; // Reconstructed
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            layer.overlayType = OVROverlay.OverlayType.Underlay;
            layer.enabled = false;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static void WireHelp(
            RecordingHelpController help,
            RecordingCoordinator coordinator,
            QuestDeviceGateway gateway)
        {
            var serialized = new SerializedObject(help);
            serialized.FindProperty("coordinator").objectReferenceValue = coordinator;
            serialized.FindProperty("gateway").objectReferenceValue = gateway;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WirePokeAction(
            GameObject button,
            RecordingPokeAction.ActionType action,
            RecordingPassthroughController passthrough,
            RecordingHelpController help)
        {
            var poke = button.GetComponent<RecordingPokeAction>();
            if (poke == null)
            {
                poke = button.AddComponent<RecordingPokeAction>();
            }

            var serialized = new SerializedObject(poke);
            serialized.FindProperty("action").enumValueIndex = (int)action;
            serialized.FindProperty("passthroughController").objectReferenceValue = passthrough;
            serialized.FindProperty("helpController").objectReferenceValue = help;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireMonitor(
            HandCaptureBoundaryMonitor monitor,
            RecordingBoundaryGlow glow)
        {
            var serialized = new SerializedObject(monitor);
            serialized.FindProperty("boundaryGlow").objectReferenceValue = glow;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WirePresenter(
            RecordingTouchscreenPresenter presenter,
            RecordingPassthroughController passthrough,
            RecordingHelpController help,
            GameObject exitButton,
            GameObject helpButton)
        {
            var serialized = new SerializedObject(presenter);
            serialized.FindProperty("passthroughController").objectReferenceValue = passthrough;
            serialized.FindProperty("helpController").objectReferenceValue = help;
            serialized.FindProperty("passthroughButton").objectReferenceValue = exitButton;
            serialized.FindProperty("helpButton").objectReferenceValue = helpButton;
            serialized.FindProperty("passthroughLabel").objectReferenceValue =
                exitButton.GetComponentInChildren<TMP_Text>(true);
            serialized.FindProperty("helpLabel").objectReferenceValue =
                helpButton.GetComponentInChildren<TMP_Text>(true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The glow lives on the HMD-locked TeacherUI canvas and covers the whole
        /// view, so its edges land in the teacher's periphery.
        /// </summary>
        private static RecordingBoundaryGlow EnsureBoundaryGlow()
        {
            Transform teacherUI = FindTeacherUI();
            if (teacherUI == null)
            {
                Debug.LogError("[SignVRAssistControlsSetup] TeacherUI canvas not found.");
                return null;
            }

            Transform existing = teacherUI.Find(GlowName);
            GameObject glowObject;
            if (existing != null)
            {
                glowObject = existing.gameObject;
            }
            else
            {
                glowObject = new GameObject(GlowName, typeof(RectTransform));
                glowObject.transform.SetParent(teacherUI, false);
                glowObject.transform.SetAsFirstSibling();
            }

            int overlayLayer = LayerMask.NameToLayer(OverlayLayerName);
            if (overlayLayer >= 0)
            {
                glowObject.layer = overlayLayer;
            }

            var rect = glowObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localPosition = new Vector3(0f, 0f, rect.localPosition.z);

            var glow = glowObject.GetComponent<RecordingBoundaryGlow>();
            if (glow == null)
            {
                glow = glowObject.AddComponent<RecordingBoundaryGlow>();
            }

            glow.color = new Color(1f, 0.13f, 0.10f, 1f);
            glow.raycastTarget = false;
            glowObject.SetActive(false);
            return glow;
        }

        private static Transform FindTeacherUI()
        {
            RecordingTeacherUI ui = Object.FindAnyObjectByType<RecordingTeacherUI>(
                FindObjectsInactive.Include
            );
            return ui != null ? ui.transform : null;
        }

        private static Camera FindCenterEyeCamera()
        {
            return Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
                .FirstOrDefault(camera => camera.name == "CenterEyeAnchor");
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void AddIfFound(List<GameObject> list, string path)
        {
            GameObject found = GameObject.Find(path);
            if (found != null)
            {
                list.Add(found);
            }
        }

        private static void FillList(
            SerializedObject serialized,
            string propertyName,
            List<GameObject> values)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            property.ClearArray();
            for (int index = 0; index < values.Count; index++)
            {
                property.InsertArrayElementAtIndex(index);
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }
        }

        private static void SetReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning(
                    $"[SignVRAssistControlsSetup] '{propertyName}' not found on {target.name}."
                );
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
