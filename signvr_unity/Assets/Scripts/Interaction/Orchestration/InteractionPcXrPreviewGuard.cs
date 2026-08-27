using UnityEngine;

namespace SignVR.Interaction.Orchestration
{
    /// <summary>
    /// Keeps a Windows Editor XR preview at the runtime's native swapchain
    /// size. Quest player builds retain the scene's dynamic-resolution value.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OVRManager))]
    public sealed class InteractionPcXrPreviewGuard : MonoBehaviour
    {
#if UNITY_EDITOR_WIN && UNITY_ANDROID
        private void Awake()
        {
            OVRManager ovrManager = GetComponent<OVRManager>();
            if (ovrManager == null)
            {
                Debug.LogError(
                    "[SignVR PC XR Preview] OVRManager is required.",
                    this);
                enabled = false;
                return;
            }

            if (!ovrManager.enableDynamicResolution)
            {
                return;
            }

            ovrManager.enableDynamicResolution = false;
            Debug.Log(
                "[SignVR PC XR Preview] Disabled OVRManager dynamic " +
                "resolution before XR initialization.",
                this);
        }
#endif
    }
}
