using System;
using Oculus.Interaction;
using UnityEngine;

namespace SignVR.CoopRelay
{
    /// <summary>
    /// A physical relay item that can be grabbed, thrown, docked and restored
    /// to its authored spawn pose.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CoopRelayItem : MonoBehaviour
    {
        [SerializeField]
        private string itemId = string.Empty;

        [SerializeField]
        private Rigidbody itemRigidbody;

        [SerializeField]
        private Grabbable grabbable;

        [SerializeField]
        private Behaviour[] interactionBehaviours = Array.Empty<Behaviour>();

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private bool spawnPoseCaptured;

        public string ItemId => itemId;
        public bool IsDocked { get; private set; }
        public bool IsGrabbed =>
            grabbable != null && grabbable.SelectingPointsCount > 0;
        public Rigidbody ItemRigidbody => itemRigidbody;
        public Vector3 SpawnPosition => spawnPosition;
        public Quaternion SpawnRotation => spawnRotation;

        private void Awake()
        {
            CacheComponents();
            ApplyPhysicsConfiguration();
            CaptureSpawnPose();
        }

        public void Configure(
            string id,
            Rigidbody targetRigidbody,
            Grabbable targetGrabbable,
            Behaviour[] targetInteractionBehaviours
        )
        {
            itemId = id ?? string.Empty;
            itemRigidbody = targetRigidbody;
            grabbable = targetGrabbable;
            interactionBehaviours = targetInteractionBehaviours ??
                Array.Empty<Behaviour>();

            CacheComponents();
            ApplyPhysicsConfiguration();
            CaptureSpawnPose();
        }

        public void CaptureSpawnPose()
        {
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            spawnPoseCaptured = true;
        }

        public void ApplyPhysicsConfiguration()
        {
            CacheComponents();

            itemRigidbody.useGravity = true;
            itemRigidbody.isKinematic = false;
            itemRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            itemRigidbody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;

            if (grabbable == null)
            {
                return;
            }

            grabbable.InjectOptionalTargetTransform(transform);
            grabbable.InjectOptionalRigidbody(itemRigidbody);
            grabbable.InjectOptionalKinematicWhileSelected(true);
            grabbable.InjectOptionalThrowWhenUnselected(true);
            grabbable.ForceKinematicDisabled = true;
        }

        public bool TryDock(Transform snapPoint)
        {
            if (snapPoint == null || IsDocked || IsGrabbed)
            {
                return false;
            }

            CacheComponents();
            SetInteractionEnabled(false);

            if (itemRigidbody.isKinematic)
            {
                itemRigidbody.isKinematic = false;
            }

            StopMotion();
            transform.SetPositionAndRotation(
                snapPoint.position,
                snapPoint.rotation
            );
            itemRigidbody.isKinematic = true;
            IsDocked = true;
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
            itemRigidbody.isKinematic = true;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            itemRigidbody.isKinematic = false;
            StopMotion();
            itemRigidbody.WakeUp();

            IsDocked = false;
            SetInteractionEnabled(true);
        }

        private void CacheComponents()
        {
            if (itemRigidbody == null)
            {
                itemRigidbody = GetComponent<Rigidbody>();
            }

            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }
        }

        private void StopMotion()
        {
            itemRigidbody.linearVelocity = Vector3.zero;
            itemRigidbody.angularVelocity = Vector3.zero;
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
