using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SignVR.Recording
{
    /// <summary>
    /// Swaps the virtual room for colour passthrough so the teacher can look at
    /// the interpreter standing next to them. The desk and its touchscreen stay
    /// visible, otherwise the teacher would have no way to press the button that
    /// brings the room back.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingPassthroughController : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private OVRManager ovrManager;

        [SerializeField]
        private Camera hmdCamera;

        [Header("Hidden while passthrough is on")]
        [Tooltip("房间、地面、灯光道具等虚拟环境；进入透视时整体隐藏。")]
        [SerializeField]
        private List<GameObject> virtualScenery = new();

        [Tooltip("镜像角色；透视时隐藏，避免遮挡真实视野。")]
        [SerializeField]
        private List<GameObject> virtualCharacters = new();

        [Header("Always kept visible")]
        [Tooltip("桌子与触屏必须保留，否则无法按下恢复场景按钮。")]
        [SerializeField]
        private List<GameObject> keepVisible = new();

        private OVRPassthroughLayer passthroughLayer;
        private CameraClearFlags cachedClearFlags;
        private Color cachedBackgroundColor;
        private Material cachedSkybox;
        private bool cachedFlagsValid;

        public bool IsPassthroughActive { get; private set; }

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            OVRManager manager,
            Camera camera)
        {
            coordinator = recordingCoordinator;
            ovrManager = manager;
            hmdCamera = camera;
        }

        private void Awake()
        {
            if (coordinator == null || hmdCamera == null)
            {
                Debug.LogError(
                    "[RecordingPassthroughController] Scene references are not assigned."
                );
                enabled = false;
                return;
            }

            if (ovrManager == null)
            {
                ovrManager = FindAnyObjectByType<OVRManager>();
            }
        }

        public void TogglePassthrough()
        {
            SetPassthrough(!IsPassthroughActive);
        }

        public void SetPassthrough(bool active)
        {
            if (IsPassthroughActive == active)
            {
                return;
            }

            if (active && !TryEnablePassthroughLayer())
            {
                Debug.LogWarning(
                    "[RecordingPassthroughController] Passthrough is unavailable on this device."
                );
                return;
            }

            IsPassthroughActive = active;

            ApplyCameraClear(active);
            SetGroupActive(virtualScenery, !active);
            SetGroupActive(virtualCharacters, !active);
            SetGroupActive(keepVisible, true);

            if (passthroughLayer != null)
            {
                passthroughLayer.enabled = active;
            }

            // Passthrough doubles as the pause affordance: no take may start while
            // the teacher is looking at the real room.
            coordinator.SetPaused(active);
        }

        private bool TryEnablePassthroughLayer()
        {
            if (ovrManager == null)
            {
                return false;
            }

            ovrManager.isInsightPassthroughEnabled = true;

            if (passthroughLayer == null)
            {
                passthroughLayer = ovrManager.GetComponent<OVRPassthroughLayer>();
                if (passthroughLayer == null)
                {
                    passthroughLayer =
                        ovrManager.gameObject.AddComponent<OVRPassthroughLayer>();
                }

                passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
            }

            return true;
        }

        private void ApplyCameraClear(bool active)
        {
            if (active)
            {
                if (!cachedFlagsValid)
                {
                    cachedClearFlags = hmdCamera.clearFlags;
                    cachedBackgroundColor = hmdCamera.backgroundColor;
                    cachedSkybox = RenderSettings.skybox;
                    cachedFlagsValid = true;
                }

                // The underlay only shows through a fully transparent clear colour.
                hmdCamera.clearFlags = CameraClearFlags.SolidColor;
                hmdCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                RenderSettings.skybox = null;
                return;
            }

            if (cachedFlagsValid)
            {
                hmdCamera.clearFlags = cachedClearFlags;
                hmdCamera.backgroundColor = cachedBackgroundColor;
                RenderSettings.skybox = cachedSkybox;
            }
        }

        private static void SetGroupActive(List<GameObject> group, bool active)
        {
            foreach (GameObject item in group)
            {
                if (item != null)
                {
                    item.SetActive(active);
                }
            }
        }
    }
}
