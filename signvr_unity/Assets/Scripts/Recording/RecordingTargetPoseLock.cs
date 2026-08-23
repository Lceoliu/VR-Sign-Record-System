using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Keeps pointing targets at their authored poses for an unambiguous take.
    /// Physics and grab drivers are disabled; a cheap late-frame pose check is
    /// retained as a final guard against third-party interaction scripts.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9990)]
    public sealed class RecordingTargetPoseLock : MonoBehaviour
    {
        private readonly struct LockedPose
        {
            public LockedPose(Transform target)
            {
                Target = target;
                Parent = target.parent;
                LocalPosition = target.localPosition;
                LocalRotation = target.localRotation;
                LocalScale = target.localScale;
            }

            public Transform Target { get; }
            public Transform Parent { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
        }

        private readonly List<LockedPose> lockedPoses = new(24);

        public int LockedTargetCount => lockedPoses.Count;

        public void Configure(IEnumerable<Transform> targets)
        {
            lockedPoses.Clear();
            var unique = new HashSet<Transform>();
            if (targets == null)
            {
                return;
            }

            foreach (Transform target in targets)
            {
                if (target == null || !unique.Add(target))
                {
                    continue;
                }

                lockedPoses.Add(new LockedPose(target));
                DisableMotion(target);
            }
        }

        private static void DisableMotion(Transform target)
        {
            foreach (Rigidbody body in
                     target.GetComponentsInChildren<Rigidbody>(true))
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
                body.detectCollisions = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.Sleep();
            }

            foreach (Grabbable grabbable in
                     target.GetComponentsInChildren<Grabbable>(true))
            {
                grabbable.enabled = false;
            }
        }

        private void LateUpdate()
        {
            for (int index = 0; index < lockedPoses.Count; index++)
            {
                LockedPose pose = lockedPoses[index];
                Transform target = pose.Target;
                if (target == null)
                {
                    continue;
                }

                if (target.parent != pose.Parent)
                {
                    target.SetParent(pose.Parent, false);
                }

                if (target.localPosition != pose.LocalPosition ||
                    target.localRotation != pose.LocalRotation ||
                    target.localScale != pose.LocalScale)
                {
                    target.SetLocalPositionAndRotation(
                        pose.LocalPosition,
                        pose.LocalRotation
                    );
                    target.localScale = pose.LocalScale;
                }
            }
        }
    }
}
