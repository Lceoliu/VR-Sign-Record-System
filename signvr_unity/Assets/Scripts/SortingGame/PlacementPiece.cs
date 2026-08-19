using System;
using Oculus.Interaction;
using UnityEngine;

namespace SignVR.SortingGame
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlacementPiece : MonoBehaviour
    {
        [SerializeField]
        private string placementId = string.Empty;

        [SerializeField]
        private Rigidbody pieceRigidbody;

        [SerializeField]
        private Grabbable grabbable;

        [SerializeField]
        private Behaviour[] interactionBehaviours = Array.Empty<Behaviour>();

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private bool spawnPoseCaptured;

        public string PlacementId => placementId;
        public bool IsPlaced { get; private set; }
        public bool IsGrabbed =>
            grabbable != null && grabbable.SelectingPointsCount > 0;

        private void Awake()
        {
            CacheComponents();
            CaptureSpawnPose();
        }

        public void Configure(
            string id,
            Rigidbody targetRigidbody,
            Grabbable targetGrabbable,
            Behaviour[] targetInteractionBehaviours
        )
        {
            placementId = id ?? string.Empty;
            pieceRigidbody = targetRigidbody;
            grabbable = targetGrabbable;
            interactionBehaviours = targetInteractionBehaviours ??
                Array.Empty<Behaviour>();
            CaptureSpawnPose();
        }

        public void CaptureSpawnPose()
        {
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            spawnPoseCaptured = true;
        }

        public bool TryPlaceAt(Transform snapPoint)
        {
            if (snapPoint == null || IsPlaced || IsGrabbed)
            {
                return false;
            }

            CacheComponents();
            IsPlaced = true;

            StopMotion();
            transform.SetPositionAndRotation(
                snapPoint.position,
                snapPoint.rotation
            );

            pieceRigidbody.isKinematic = true;
            SetInteractionEnabled(false);
            return true;
        }

        public void ResetToSpawn()
        {
            CacheComponents();

            if (!spawnPoseCaptured)
            {
                CaptureSpawnPose();
            }

            SetInteractionEnabled(false);
            pieceRigidbody.isKinematic = true;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            pieceRigidbody.isKinematic = false;
            // Unity does not accept velocity writes while a Rigidbody is
            // kinematic, so clear throw state after restoring dynamics.
            StopMotion();
            pieceRigidbody.WakeUp();

            IsPlaced = false;
            SetInteractionEnabled(true);
        }

        private void CacheComponents()
        {
            if (pieceRigidbody == null)
            {
                pieceRigidbody = GetComponent<Rigidbody>();
            }

            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }
        }

        private void StopMotion()
        {
            pieceRigidbody.linearVelocity = Vector3.zero;
            pieceRigidbody.angularVelocity = Vector3.zero;
        }

        private void SetInteractionEnabled(bool value)
        {
            foreach (Behaviour interactionBehaviour in interactionBehaviours)
            {
                if (interactionBehaviour != null)
                {
                    interactionBehaviour.enabled = value;
                }
            }

            if (grabbable != null)
            {
                grabbable.enabled = value;
            }
        }
    }
}
