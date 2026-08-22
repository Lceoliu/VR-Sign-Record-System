using UnityEngine;
using UnityEngine.XR.Management;

namespace SignVR
{
    internal static class QuestXrRuntimeBootstrap
    {
#if UNITY_ANDROID
        private static XRManagerSettings manager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Initialize()
        {
            if (Application.isEditor)
            {
                return;
            }

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                Debug.LogError("Quest XR settings are unavailable; XR cannot initialize.");
                return;
            }

            manager = settings.Manager;
            manager.InitializeLoaderSync();
            Application.quitting += Shutdown;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Start()
        {
            if (Application.isEditor || manager == null)
            {
                return;
            }

            if (manager.activeLoader == null)
            {
                Debug.LogError("Quest XR loader failed to initialize.");
                return;
            }

            manager.StartSubsystems();
        }

        private static void Shutdown()
        {
            Application.quitting -= Shutdown;
            if (manager == null || manager.activeLoader == null)
            {
                return;
            }

            manager.StopSubsystems();
            manager.DeinitializeLoader();
            manager = null;
        }
#endif
    }
}
