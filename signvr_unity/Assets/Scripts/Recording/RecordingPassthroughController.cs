using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        [Tooltip("场景预置的 Underlay 层，默认禁用；运行时切换启用，不在运行时 AddComponent。")]
        [SerializeField]
        private OVRPassthroughLayer passthroughLayer;

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

        private CameraClearFlags cachedClearFlags;
        private Color cachedBackgroundColor;
        private Material cachedSkybox;
        private bool cachedFlagsValid;
        private bool targetPassthroughState;
        private Coroutine transitionRoutine;

        private static readonly WaitForEndOfFrame EndOfFrame = new();

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

            // Keep one native passthrough layer alive for the whole app session.
            // Recreating its compositor layer during every VR/MR switch can leave
            // stale tiles in the eye buffer on Quest.
            if (passthroughLayer != null)
            {
                passthroughLayer.textureOpacity = 0f;
                passthroughLayer.enabled = true;
            }

            // This scene has no active post-process effects. Keeping the HMD
            // camera path stable avoids rebuilding URP passes during a switch.
            SetPostProcessing(false);
        }

        public void TogglePassthrough()
        {
            SetPassthrough(!targetPassthroughState);
        }

        public void SetPassthrough(bool active)
        {
            if (
                targetPassthroughState == active &&
                (transitionRoutine != null || IsPassthroughActive == active)
            )
            {
                return;
            }

            if (active && !HasPassthroughSetup())
            {
                Debug.LogWarning(
                    "[SignVRPassthrough] Unavailable: " +
                    $"ovrManager={(ovrManager == null ? "null" : "ok")}, " +
                    $"layer={(passthroughLayer == null ? "null" : "ok")}."
                );
                return;
            }

            targetPassthroughState = active;
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }
            transitionRoutine = StartCoroutine(
                active ? EnterPassthrough() : ExitPassthrough()
            );
        }

        private IEnumerator EnterPassthrough()
        {
            // Pause immediately, but keep the virtual room opaque until the Quest
            // compositor reports that passthrough is actually initialized.
            coordinator.SetPaused(true);
            passthroughLayer.textureOpacity = 0f;
            passthroughLayer.enabled = true;

            while (!OVRManager.IsInsightPassthroughInitialized())
            {
                if (OVRManager.HasInsightPassthroughInitFailed())
                {
                    AbortPassthroughEntry();
                    yield break;
                }

                yield return null;
            }

            passthroughLayer.textureOpacity = 1f;
            yield return EndOfFrame;
            ApplyCameraClear(true);
            SetGroupActive(virtualScenery, false);
            SetGroupActive(virtualCharacters, false);
            SetGroupActive(keepVisible, true);
            yield return EndOfFrame;

            IsPassthroughActive = true;
            transitionRoutine = null;
            LogState();
        }

        private void AbortPassthroughEntry()
        {
            passthroughLayer.textureOpacity = 0f;
            passthroughLayer.enabled = false;
            SetGroupActive(virtualScenery, true);
            SetGroupActive(virtualCharacters, true);
            SetGroupActive(keepVisible, true);
            ApplyCameraClear(false);
            targetPassthroughState = false;
            IsPassthroughActive = false;
            transitionRoutine = null;
            coordinator.SetPaused(false);
            Debug.LogError(
                "[SignVRPassthrough] Quest reported that passthrough initialization failed."
            );
        }

        private IEnumerator ExitPassthrough()
        {
            // Restore a complete opaque virtual frame before stopping the native
            // passthrough stream. This prevents stale compositor tiles from being
            // exposed during the transition back to immersion.
            SetGroupActive(virtualScenery, true);
            SetGroupActive(virtualCharacters, true);
            SetGroupActive(keepVisible, true);
            ApplyCameraClear(false);
            yield return EndOfFrame;
            yield return null;

            passthroughLayer.textureOpacity = 0f;
            IsPassthroughActive = false;
            coordinator.SetPaused(false);
            transitionRoutine = null;
            LogState();
        }

        private bool HasPassthroughSetup()
        {
            return ovrManager != null && passthroughLayer != null;
        }

        private void LogState()
        {
            Debug.Log(
                $"[SignVRPassthrough] active={IsPassthroughActive}, " +
                $"insightEnabled={ovrManager.isInsightPassthroughEnabled}, " +
                $"layerEnabled={passthroughLayer.enabled}, " +
                $"clearFlags={hmdCamera.clearFlags}, bg={hmdCamera.backgroundColor}."
            );
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

        private void SetPostProcessing(bool enabled)
        {
            var cameraData = hmdCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = enabled;
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

        private void OnDisable()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }

            targetPassthroughState = false;
            IsPassthroughActive = false;
            SetGroupActive(virtualScenery, true);
            SetGroupActive(virtualCharacters, true);
            SetGroupActive(keepVisible, true);
            ApplyCameraClear(false);
            if (passthroughLayer != null)
            {
                passthroughLayer.textureOpacity = 0f;
            }
            coordinator?.SetPaused(false);
        }
    }
}
