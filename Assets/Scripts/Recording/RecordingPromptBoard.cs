using UnityEngine;

namespace SignVR.Recording
{
    public sealed class RecordingPromptBoard : MonoBehaviour
    {
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private GameObject grabHandle;

        [SerializeField]
        private Vector3 defaultLocalPosition;

        [SerializeField]
        private Vector3 minimumLocalPosition = new(-500f, -250f, -250f);

        [SerializeField]
        private Vector3 maximumLocalPosition = new(500f, 500f, 400f);

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            GameObject handle,
            Vector3 initialLocalPosition)
        {
            coordinator = recordingCoordinator;
            grabHandle = handle;
            defaultLocalPosition = initialLocalPosition;
        }

        public void ResetPlacement()
        {
            transform.localPosition = defaultLocalPosition;
            transform.localRotation = Quaternion.identity;
        }

        private void Update()
        {
            if (coordinator == null || grabHandle == null)
            {
                return;
            }

            bool canMove =
                coordinator.State == RecordingFlowState.Ready ||
                coordinator.State == RecordingFlowState.Completed;

            if (grabHandle.activeSelf != canMove)
            {
                grabHandle.SetActive(canMove);
            }
        }

        private void LateUpdate()
        {
            Vector3 position = transform.localPosition;
            position.x = Mathf.Clamp(
                position.x,
                minimumLocalPosition.x,
                maximumLocalPosition.x
            );
            position.y = Mathf.Clamp(
                position.y,
                minimumLocalPosition.y,
                maximumLocalPosition.y
            );
            position.z = Mathf.Clamp(
                position.z,
                minimumLocalPosition.z,
                maximumLocalPosition.z
            );
            transform.localPosition = position;
            transform.localRotation = Quaternion.identity;
        }
    }
}
