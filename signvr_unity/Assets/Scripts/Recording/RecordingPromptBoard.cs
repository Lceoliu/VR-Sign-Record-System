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

        [SerializeField]
        private Quaternion defaultLocalRotation = Quaternion.identity;

        [SerializeField]
        private bool heightOnly;

        public void Configure(
            RecordingCoordinator recordingCoordinator,
            GameObject handle,
            Vector3 initialLocalPosition)
        {
            coordinator = recordingCoordinator;
            grabHandle = handle;
            defaultLocalPosition = initialLocalPosition;
            defaultLocalRotation = transform.localRotation;
            heightOnly = false;
        }

        public void ConfigureHeightOnly(
            RecordingCoordinator recordingCoordinator,
            GameObject handle,
            Vector3 initialLocalPosition,
            float minimumLocalY,
            float maximumLocalY,
            Quaternion initialLocalRotation)
        {
            coordinator = recordingCoordinator;
            grabHandle = handle;
            defaultLocalPosition = initialLocalPosition;
            minimumLocalPosition = new Vector3(
                initialLocalPosition.x,
                minimumLocalY,
                initialLocalPosition.z
            );
            maximumLocalPosition = new Vector3(
                initialLocalPosition.x,
                maximumLocalY,
                initialLocalPosition.z
            );
            defaultLocalRotation = initialLocalRotation;
            heightOnly = true;
        }

        public void ResetPlacement()
        {
            transform.localPosition = defaultLocalPosition;
            transform.localRotation = defaultLocalRotation;
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
            if (heightOnly)
            {
                position.x = defaultLocalPosition.x;
                position.z = defaultLocalPosition.z;
            }
            else
            {
                position.x = Mathf.Clamp(
                    position.x,
                    minimumLocalPosition.x,
                    maximumLocalPosition.x
                );
            }
            position.y = Mathf.Clamp(
                position.y,
                minimumLocalPosition.y,
                maximumLocalPosition.y
            );
            if (!heightOnly)
            {
                position.z = Mathf.Clamp(
                    position.z,
                    minimumLocalPosition.z,
                    maximumLocalPosition.z
                );
            }
            transform.localPosition = position;
            transform.localRotation = defaultLocalRotation;
        }
    }
}
