using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Raises and clears a help flag on the operator console.
    ///
    /// Inside the headset a deaf teacher has no way to call out: they cannot be
    /// heard, and taking the headset off loses their position and calibration.
    /// This button is their one channel for asking the operator to step in
    /// without ending the session.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingHelpController : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private QuestDeviceGateway gateway;

        public bool IsHelpRequested => coordinator != null && coordinator.IsHelpRequested;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            QuestDeviceGateway deviceGateway)
        {
            coordinator = recordingCoordinator;
            gateway = deviceGateway;
        }

        private void Awake()
        {
            if (coordinator == null || gateway == null)
            {
                Debug.LogError(
                    "[RecordingHelpController] Scene references are not assigned."
                );
                enabled = false;
            }
        }

        public void ToggleHelp()
        {
            SetHelp(!IsHelpRequested);
        }

        public void SetHelp(bool requested)
        {
            coordinator.SetHelpRequested(requested);
            gateway.SendHelpSignal(requested);
        }
    }
}
