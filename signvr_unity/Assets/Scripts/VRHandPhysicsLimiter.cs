using System;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// Adds physical capsules to a Meta SyntheticHand and feeds collision-aware
/// wrist poses back into that same hand data stream. The rendered hand is
/// therefore blocked by real scene colliders even when the tracked hand would
/// otherwise move through them.
/// </summary>
[DisallowMultipleComponent]
public sealed class VRHandPhysicsLimiter : MonoBehaviour
{
    [SerializeField] private SyntheticHand syntheticHand;
    [SerializeField] private HandVisual handVisual;
    [SerializeField] private JointsRadiusFeature jointsRadiusFeature;
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField, Min(0.01f)] private float wristRadius = 0.08f;
    [SerializeField, Min(0.01f)] private float sweepPadding = 0.015f;
    [SerializeField, Range(0f, 1f)] private float wristConstraintStrength = 1f;
    [SerializeField] private bool generateFingerCapsules = true;

    private HandPhysicsCapsules physicsCapsules;
    private IHand sourceHand;
    private Vector3 lastSafeWristPosition;
    private bool hasSafeWrist;
    private bool wristLockedByLimiter;
    private bool subscribed;
    private Vector3[] constrainedJointPositions;
    private Vector3[] desiredJointPositions;
    private bool hasConstrainedJoints;
    private readonly RaycastHit[] sweepHits = new RaycastHit[64];

    public SyntheticHand SyntheticHand => syntheticHand;
    public HandPhysicsCapsules PhysicsCapsules => physicsCapsules;
    public bool IsReady => syntheticHand != null && handVisual != null;
    public event Action<Vector3, Vector3> WristBlocked;

    private void Awake()
    {
        ResolveReferences();
        if (generateFingerCapsules && IsReady)
        {
            ConfigurePhysicsCapsules();
        }
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (syntheticHand != null && wristLockedByLimiter)
        {
            syntheticHand.FreeWrist(SyntheticHand.WristLockMode.Position);
        }
        wristLockedByLimiter = false;
        hasSafeWrist = false;
        hasConstrainedJoints = false;
    }

    public void Configure(
        SyntheticHand hand,
        HandVisual visual,
        JointsRadiusFeature radiusFeature = null
    )
    {
        Unsubscribe();

        syntheticHand = hand;
        handVisual = visual;
        jointsRadiusFeature = radiusFeature;
        ResolveReferences();
        ConfigurePhysicsCapsules();

        if (isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    private void ResolveReferences()
    {
        if (syntheticHand == null)
        {
            syntheticHand = GetComponent<SyntheticHand>() ??
                            GetComponentInParent<SyntheticHand>() ??
                            GetComponentInChildren<SyntheticHand>(true);
        }
        if (handVisual == null)
        {
            handVisual = GetComponent<HandVisual>() ??
                         GetComponentInParent<HandVisual>() ??
                         GetComponentInChildren<HandVisual>(true);
        }
        if (jointsRadiusFeature == null)
        {
            Hand sourceComponent =
                syntheticHand?.ModifyDataFromSource as Hand;
            jointsRadiusFeature = sourceComponent?.GetComponent<JointsRadiusFeature>() ??
                                   GetComponentInParent<JointsRadiusFeature>() ??
                                   GetComponentInChildren<JointsRadiusFeature>(true) ??
                                   syntheticHand?.transform.parent?.GetComponentInChildren<JointsRadiusFeature>(true);
        }
        sourceHand = syntheticHand?.ModifyDataFromSource as IHand;
    }

    private void ConfigurePhysicsCapsules()
    {
        if (!IsReady)
        {
            return;
        }

        if (jointsRadiusFeature == null)
        {
            Debug.LogWarning(
                $"[SignVR] No JointsRadiusFeature found for {name}; " +
                "wrist collision remains active but finger capsules were skipped.",
                this
            );
            return;
        }
        physicsCapsules = handVisual.GetComponent<HandPhysicsCapsules>() ??
                          handVisual.gameObject.AddComponent<HandPhysicsCapsules>();
        physicsCapsules.InjectAllOVRHandPhysicsCapsules(
            syntheticHand,
            false,
            gameObject.layer
        );
        physicsCapsules.InjectJointsRadiusFeature(jointsRadiusFeature);
        // All is intentionally used here so the component works with both the
        // legacy OVR hand-joint enum and the OpenXR hand-joint enum.
        physicsCapsules.InjectMask(HandFingerJointFlags.All);
    }

    private void Subscribe()
    {
        if (subscribed || syntheticHand == null)
        {
            return;
        }

        IHand updateHand = sourceHand ?? syntheticHand;
        updateHand.WhenHandUpdated += HandleHandUpdated;
        if (handVisual != null)
        {
            handVisual.WhenHandVisualUpdated += HandleHandVisualUpdated;
        }
        if (physicsCapsules != null)
        {
            physicsCapsules.WhenCapsulesGenerated += IgnorePlayerBodyCollision;
        }
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        IHand updateHand = sourceHand ?? syntheticHand;
        if (updateHand != null)
        {
            updateHand.WhenHandUpdated -= HandleHandUpdated;
        }
        if (handVisual != null)
        {
            handVisual.WhenHandVisualUpdated -= HandleHandVisualUpdated;
        }
        if (physicsCapsules != null)
        {
            physicsCapsules.WhenCapsulesGenerated -= IgnorePlayerBodyCollision;
        }
        subscribed = false;
    }

    private void HandleHandUpdated()
    {
        IHand trackedHand = sourceHand ?? syntheticHand;
        if (!IsReady || trackedHand == null || !trackedHand.IsTrackedDataValid ||
            !trackedHand.GetRootPose(out Pose desiredWrist))
        {
            ReleaseWristLock();
            hasSafeWrist = false;
            return;
        }

        Vector3 trackedPosition = desiredWrist.position;
        if (!hasSafeWrist)
        {
            lastSafeWristPosition = trackedPosition;
            hasSafeWrist = true;
        }

        Vector3 constrainedPosition = ResolvePosition(
            lastSafeWristPosition,
            trackedPosition,
            wristRadius + sweepPadding
        );
        bool blocked =
            (constrainedPosition - trackedPosition).sqrMagnitude > 0.000001f;
        if (blocked)
        {
            WristBlocked?.Invoke(trackedPosition, constrainedPosition);
            Pose constrained = new Pose(constrainedPosition, desiredWrist.rotation);
            syntheticHand.LockWristPose(
                constrained,
                wristConstraintStrength,
                SyntheticHand.WristLockMode.Position,
                true,
                true
            );
            if (sourceHand != null)
            {
                syntheticHand.MarkInputDataRequiresUpdate();
            }
            wristLockedByLimiter = true;
        }
        else if (wristLockedByLimiter)
        {
            ReleaseWristLock();
        }

        lastSafeWristPosition = blocked ? constrainedPosition : trackedPosition;
    }

    private void ReleaseWristLock()
    {
        if (!wristLockedByLimiter || syntheticHand == null)
        {
            return;
        }

        syntheticHand.FreeWrist(SyntheticHand.WristLockMode.Position);
        if (sourceHand != null)
        {
            syntheticHand.MarkInputDataRequiresUpdate();
        }
        wristLockedByLimiter = false;
    }

    private void HandleHandVisualUpdated()
    {
        if (!IsReady || !syntheticHand.IsTrackedDataValid ||
            handVisual.Joints == null)
        {
            hasConstrainedJoints = false;
            return;
        }

        int count = Mathf.Min(
            handVisual.Joints.Count,
            (int)HandJointId.HandEnd
        );
        EnsureJointBuffers(count);

        for (int i = 0; i < count; i++)
        {
            Transform joint = handVisual.Joints[i];
            desiredJointPositions[i] =
                joint != null ? joint.position : constrainedJointPositions[i];
        }

        if (!hasConstrainedJoints)
        {
            Array.Copy(desiredJointPositions, constrainedJointPositions, count);
            hasConstrainedJoints = true;
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Transform joint = handVisual.Joints[i];
            if (joint == null)
            {
                continue;
            }

            float radius = jointsRadiusFeature != null
                ? jointsRadiusFeature.GetJointRadius((HandJointId)i)
                : wristRadius * 0.35f;
            Vector3 resolved = ResolvePosition(
                constrainedJointPositions[i],
                desiredJointPositions[i],
                Mathf.Max(0.004f, radius + sweepPadding * 0.25f)
            );
            joint.position = resolved;
            constrainedJointPositions[i] = resolved;
        }
    }

    private void EnsureJointBuffers(int count)
    {
        if (constrainedJointPositions != null &&
            constrainedJointPositions.Length == count)
        {
            return;
        }

        constrainedJointPositions = new Vector3[count];
        desiredJointPositions = new Vector3[count];
        hasConstrainedJoints = false;
    }

    private Vector3 ResolvePosition(Vector3 from, Vector3 target, float radius)
    {
        Vector3 delta = target - from;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return target;
        }

        Vector3 direction = delta / distance;
        int hitCount = Physics.SphereCastNonAlloc(
            from,
            radius,
            direction,
            sweepHits,
            distance,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = sweepHits[i];
            if (ShouldIgnore(hit.collider) || hit.distance >= nearestDistance)
            {
                continue;
            }
            nearestDistance = hit.distance;
        }

        return float.IsPositiveInfinity(nearestDistance)
            ? target
            : from + direction * Mathf.Max(0f, nearestDistance - sweepPadding);
    }

    private bool ShouldIgnore(Collider candidate)
    {
        if (candidate == null)
        {
            return true;
        }

        Transform candidateTransform = candidate.transform;
        if (candidateTransform.IsChildOf(transform) ||
            candidateTransform.IsChildOf(handVisual.transform))
        {
            return true;
        }

        VRPlayerRig player = VRPlayerRig.Instance;
        return player != null && candidateTransform.IsChildOf(player.transform);
    }

    private void IgnorePlayerBodyCollision()
    {
        VRPlayerRig player = VRPlayerRig.Instance;
        CharacterController body =
            player != null ? player.GetComponent<CharacterController>() : null;
        if (body == null || physicsCapsules?.Capsules == null)
        {
            return;
        }

        foreach (BoneCapsule capsule in physicsCapsules.Capsules)
        {
            if (capsule?.CapsuleCollider != null)
            {
                Physics.IgnoreCollision(capsule.CapsuleCollider, body, true);
            }
        }
    }
}
