using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
    [Serializable]
    public sealed class DeterministicHingeBinding
    {
        [SerializeField]
        private Transform movingPart;

        [SerializeField]
        private Transform hinge;

        [SerializeField]
        private Vector3 hingeLocalAxis = Vector3.up;

        [SerializeField]
        private float openAngleDegrees = 90f;

        private Vector3 closedLocalPosition;
        private Quaternion closedLocalRotation;
        private bool closedPoseCaptured;
        private bool opened;

        public Transform MovingPart => movingPart;

        public Transform Hinge => hinge;

        public bool IsConfigured => movingPart != null && hinge != null;

        public void Configure(
            Transform targetMovingPart,
            Transform targetHinge,
            Vector3 localAxis,
            float openAngle)
        {
            Transform nextMovingPart = targetMovingPart ??
                throw new ArgumentNullException(nameof(targetMovingPart));
            Transform nextHinge = targetHinge ??
                throw new ArgumentNullException(nameof(targetHinge));
            Reset();
            movingPart = nextMovingPart;
            hinge = nextHinge;
            hingeLocalAxis = localAxis;
            openAngleDegrees = openAngle;
            CaptureClosedPose();
        }

        public void CaptureClosedPose()
        {
            if (!IsConfigured)
            {
                closedPoseCaptured = false;
                return;
            }

            closedLocalPosition = movingPart.localPosition;
            closedLocalRotation = movingPart.localRotation;
            closedPoseCaptured = true;
            opened = false;
        }

        public void Reset()
        {
            if (!IsConfigured || !closedPoseCaptured)
            {
                return;
            }

            movingPart.SetLocalPositionAndRotation(
                closedLocalPosition,
                closedLocalRotation
            );
            opened = false;
        }

        public void Open()
        {
            if (!IsConfigured || opened)
            {
                return;
            }
            if (!closedPoseCaptured)
            {
                CaptureClosedPose();
            }

            Reset();
            Vector3 localAxis = hingeLocalAxis.sqrMagnitude > 0.0001f
                ? hingeLocalAxis.normalized
                : Vector3.up;
            movingPart.RotateAround(
                hinge.position,
                hinge.TransformDirection(localAxis),
                openAngleDegrees
            );
            opened = true;
        }
    }

    [Serializable]
    public sealed class DeterministicTargetStateBinding
    {
        [SerializeField]
        private string targetId = string.Empty;

        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 activatedLocalEulerOffset =
            new Vector3(-14f, 0f, 0f);

        private Vector3 idleLocalPosition;
        private Quaternion idleLocalRotation;
        private bool idlePoseCaptured;

        public string TargetId => targetId;

        public Transform Target => target;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(targetId) && target != null;

        public void Configure(
            string stableTargetId,
            Transform targetTransform,
            Vector3 localEulerOffset)
        {
            string nextTargetId = string.IsNullOrWhiteSpace(stableTargetId)
                ? throw new ArgumentException(
                    "A stable target ID is required.",
                    nameof(stableTargetId)
                )
                : stableTargetId.Trim();
            Transform nextTarget = targetTransform ??
                throw new ArgumentNullException(nameof(targetTransform));
            Reset();
            targetId = nextTargetId;
            target = nextTarget;
            activatedLocalEulerOffset = localEulerOffset;
            CaptureIdlePose();
        }

        public void CaptureIdlePose()
        {
            if (target == null)
            {
                idlePoseCaptured = false;
                return;
            }
            idleLocalPosition = target.localPosition;
            idleLocalRotation = target.localRotation;
            idlePoseCaptured = true;
        }

        public void Activate()
        {
            if (!IsConfigured)
            {
                return;
            }
            if (!idlePoseCaptured)
            {
                CaptureIdlePose();
            }
            target.SetLocalPositionAndRotation(
                idleLocalPosition,
                idleLocalRotation * Quaternion.Euler(
                    activatedLocalEulerOffset
                )
            );
        }

        public void Reset()
        {
            if (!IsConfigured || !idlePoseCaptured)
            {
                return;
            }
            target.SetLocalPositionAndRotation(
                idleLocalPosition,
                idleLocalRotation
            );
        }
    }

    [Serializable]
    public sealed class PlannedKeyReleaseBinding
    {
        [SerializeField]
        private string targetId = string.Empty;

        [SerializeField]
        private GameObject targetRoot;

        [SerializeField]
        private Rigidbody[] bodies = Array.Empty<Rigidbody>();

        [SerializeField]
        private Behaviour[] interactionBehaviours = Array.Empty<Behaviour>();

        [SerializeField]
        private Collider[] interactionColliders = Array.Empty<Collider>();

        private bool authoredActive;
        private bool authoredStateCaptured;
        private Vector3 authoredRootLocalPosition;
        private Quaternion authoredRootLocalRotation;
        private bool[] authoredBodyKinematic = Array.Empty<bool>();
        private bool[] authoredBodyGravity = Array.Empty<bool>();
        private Vector3[] authoredBodyLocalPositions = Array.Empty<Vector3>();
        private Quaternion[] authoredBodyLocalRotations =
            Array.Empty<Quaternion>();
        private Vector3[] authoredBodyLinearVelocities =
            Array.Empty<Vector3>();
        private Vector3[] authoredBodyAngularVelocities =
            Array.Empty<Vector3>();
        private bool[] authoredBehaviourEnabled = Array.Empty<bool>();
        private bool[] authoredColliderEnabled = Array.Empty<bool>();

        public string TargetId => targetId;

        public GameObject TargetRoot => targetRoot;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(targetId) && targetRoot != null;

        public void Configure(
            string stableTargetId,
            GameObject root,
            Rigidbody[] targetBodies,
            Behaviour[] behaviours,
            Collider[] colliders)
        {
            string nextTargetId = string.IsNullOrWhiteSpace(stableTargetId)
                ? throw new ArgumentException(
                    "A stable key target ID is required.",
                    nameof(stableTargetId)
                )
                : stableTargetId.Trim();
            GameObject nextTargetRoot = root ??
                throw new ArgumentNullException(nameof(root));
            Rigidbody[] nextBodies = targetBodies ?? Array.Empty<Rigidbody>();
            Behaviour[] nextBehaviours = behaviours ??
                Array.Empty<Behaviour>();
            Collider[] nextColliders = colliders ?? Array.Empty<Collider>();
            RestoreAuthoredState();
            targetId = nextTargetId;
            targetRoot = nextTargetRoot;
            bodies = nextBodies;
            interactionBehaviours = nextBehaviours;
            interactionColliders = nextColliders;
            authoredStateCaptured = false;
            CaptureAuthoredState();
        }

        public void PrepareForRun(bool isPlanned)
        {
            if (!IsConfigured)
            {
                return;
            }

            CaptureAuthoredState();
            targetRoot.SetActive(isPlanned);
            SetReleased(false);
        }

        public void Release()
        {
            if (!IsConfigured)
            {
                return;
            }
            targetRoot.SetActive(true);
            SetReleased(true);
        }

        public void RestoreAuthoredState()
        {
            if (!IsConfigured || !authoredStateCaptured)
            {
                return;
            }

            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                StopAndLock(body);
            }
            targetRoot.transform.SetLocalPositionAndRotation(
                authoredRootLocalPosition,
                authoredRootLocalRotation
            );
            int bodyCount = Math.Min(
                bodies.Length,
                Math.Min(
                    authoredBodyKinematic.Length,
                    Math.Min(
                        authoredBodyGravity.Length,
                        Math.Min(
                            authoredBodyLocalPositions.Length,
                            Math.Min(
                                authoredBodyLocalRotations.Length,
                                Math.Min(
                                    authoredBodyLinearVelocities.Length,
                                    authoredBodyAngularVelocities.Length
                                )
                            )
                        )
                    )
                )
            );
            for (int index = 0; index < bodyCount; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                body.transform.SetLocalPositionAndRotation(
                    authoredBodyLocalPositions[index],
                    authoredBodyLocalRotations[index]
                );
                body.useGravity = authoredBodyGravity[index];
                if (authoredBodyKinematic[index])
                {
                    body.isKinematic = true;
                }
                else
                {
                    body.isKinematic = false;
                    body.linearVelocity =
                        authoredBodyLinearVelocities[index];
                    body.angularVelocity =
                        authoredBodyAngularVelocities[index];
                }
            }
            for (int index = 0;
                index < interactionBehaviours.Length;
                index++)
            {
                Behaviour behaviour = interactionBehaviours[index];
                if (behaviour != null)
                {
                    behaviour.enabled = authoredBehaviourEnabled[index];
                }
            }
            for (int index = 0;
                index < interactionColliders.Length;
                index++)
            {
                Collider collider = interactionColliders[index];
                if (collider != null)
                {
                    collider.enabled = authoredColliderEnabled[index];
                }
            }
            targetRoot.SetActive(authoredActive);
        }

        private void CaptureAuthoredState()
        {
            if (!IsConfigured || authoredStateCaptured)
            {
                return;
            }

            authoredActive = targetRoot.activeSelf;
            authoredRootLocalPosition = targetRoot.transform.localPosition;
            authoredRootLocalRotation = targetRoot.transform.localRotation;
            authoredBodyKinematic = new bool[bodies.Length];
            authoredBodyGravity = new bool[bodies.Length];
            authoredBodyLocalPositions = new Vector3[bodies.Length];
            authoredBodyLocalRotations = new Quaternion[bodies.Length];
            authoredBodyLinearVelocities = new Vector3[bodies.Length];
            authoredBodyAngularVelocities = new Vector3[bodies.Length];
            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                authoredBodyKinematic[index] = body.isKinematic;
                authoredBodyGravity[index] = body.useGravity;
                authoredBodyLocalPositions[index] =
                    body.transform.localPosition;
                authoredBodyLocalRotations[index] =
                    body.transform.localRotation;
                authoredBodyLinearVelocities[index] = body.linearVelocity;
                authoredBodyAngularVelocities[index] = body.angularVelocity;
            }

            authoredBehaviourEnabled =
                new bool[interactionBehaviours.Length];
            for (int index = 0;
                index < interactionBehaviours.Length;
                index++)
            {
                Behaviour behaviour = interactionBehaviours[index];
                authoredBehaviourEnabled[index] =
                    behaviour != null && behaviour.enabled;
            }

            authoredColliderEnabled = new bool[interactionColliders.Length];
            for (int index = 0;
                index < interactionColliders.Length;
                index++)
            {
                Collider collider = interactionColliders[index];
                authoredColliderEnabled[index] =
                    collider != null && collider.enabled;
            }
            authoredStateCaptured = true;
        }

        private void SetReleased(bool released)
        {
            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                if (released)
                {
                    body.isKinematic = false;
                    body.useGravity = true;
                }
                else
                {
                    StopAndLock(body);
                    body.useGravity = false;
                }
            }

            for (int index = 0;
                index < interactionBehaviours.Length;
                index++)
            {
                Behaviour behaviour = interactionBehaviours[index];
                if (behaviour != null)
                {
                    behaviour.enabled = released;
                }
            }

            for (int index = 0;
                index < interactionColliders.Length;
                index++)
            {
                Collider collider = interactionColliders[index];
                if (collider != null)
                {
                    collider.enabled = released;
                }
            }
        }

        private static void StopAndLock(Rigidbody body)
        {
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
    }

    [DisallowMultipleComponent]
    public sealed class InteractionDeterministicPresentation : MonoBehaviour
    {
        [SerializeField]
        private InteractionPhaseCoordinator coordinator;

        [Header("Completion motions")]
        [SerializeField]
        private DeterministicHingeBinding safeDoor =
            new DeterministicHingeBinding();

        [SerializeField]
        private DeterministicHingeBinding chestLid =
            new DeterministicHingeBinding();

        [SerializeField]
        private DeterministicHingeBinding cabinetLeftDoor =
            new DeterministicHingeBinding();

        [SerializeField]
        private DeterministicHingeBinding cabinetRightDoor =
            new DeterministicHingeBinding();

        [SerializeField]
        private DeterministicHingeBinding finalLeftDoor =
            new DeterministicHingeBinding();

        [Header("Deterministic task progress visuals")]
        [SerializeField]
        private DeterministicTargetStateBinding[] chestButtons =
            Array.Empty<DeterministicTargetStateBinding>();

        [SerializeField]
        private DeterministicTargetStateBinding[] cabinetButtons =
            Array.Empty<DeterministicTargetStateBinding>();

        [SerializeField]
        private DeterministicTargetStateBinding[] breakers =
            Array.Empty<DeterministicTargetStateBinding>();

        [SerializeField]
        private PlannedKeyReleaseBinding[] plannedKeys =
            Array.Empty<PlannedKeyReleaseBinding>();

        private bool subscribed;

#if UNITY_INCLUDE_TESTS
        private readonly InteractionSubscriptionDiagnostic
            subscriptionDiagnostic =
                new InteractionSubscriptionDiagnostic();
#endif

        public DeterministicHingeBinding SafeDoor => safeDoor;
        public DeterministicHingeBinding ChestLid => chestLid;
        public DeterministicHingeBinding CabinetLeftDoor => cabinetLeftDoor;
        public DeterministicHingeBinding CabinetRightDoor => cabinetRightDoor;
        public DeterministicHingeBinding FinalLeftDoor => finalLeftDoor;
        public IReadOnlyList<DeterministicTargetStateBinding> ChestButtons =>
            chestButtons;
        public IReadOnlyList<DeterministicTargetStateBinding> CabinetButtons =>
            cabinetButtons;
        public IReadOnlyList<DeterministicTargetStateBinding> Breakers =>
            breakers;
        public IReadOnlyList<PlannedKeyReleaseBinding> PlannedKeys =>
            plannedKeys;
        public InteractionPhaseCoordinator Coordinator => coordinator;

#if UNITY_INCLUDE_TESTS
        public InteractionSubscriptionDiagnostic SubscriptionDiagnostic =>
            subscriptionDiagnostic;
#endif

        private void Awake()
        {
            CaptureAuthoredState();
            Bind();
        }

        private void OnEnable()
        {
            Bind();
            RebuildFromAuthority();
        }

        public void Configure(
            InteractionPhaseCoordinator targetCoordinator,
            DeterministicHingeBinding safeDoorBinding,
            DeterministicHingeBinding chestLidBinding,
            DeterministicHingeBinding cabinetLeftBinding,
            DeterministicHingeBinding cabinetRightBinding,
            DeterministicHingeBinding finalLeftBinding,
            DeterministicTargetStateBinding[] chestButtonBindings,
            DeterministicTargetStateBinding[] cabinetButtonBindings,
            DeterministicTargetStateBinding[] breakerBindings,
            PlannedKeyReleaseBinding[] keyBindings)
        {
            InteractionPhaseCoordinator nextCoordinator = targetCoordinator ??
                throw new ArgumentNullException(nameof(targetCoordinator));
            DeterministicHingeBinding nextSafeDoor = safeDoorBinding ??
                new DeterministicHingeBinding();
            DeterministicHingeBinding nextChestLid = chestLidBinding ??
                new DeterministicHingeBinding();
            DeterministicHingeBinding nextCabinetLeft = cabinetLeftBinding ??
                new DeterministicHingeBinding();
            DeterministicHingeBinding nextCabinetRight = cabinetRightBinding ??
                new DeterministicHingeBinding();
            DeterministicHingeBinding nextFinalLeft = finalLeftBinding ??
                new DeterministicHingeBinding();
            DeterministicTargetStateBinding[] nextChestButtons =
                chestButtonBindings ??
                Array.Empty<DeterministicTargetStateBinding>();
            DeterministicTargetStateBinding[] nextCabinetButtons =
                cabinetButtonBindings ??
                Array.Empty<DeterministicTargetStateBinding>();
            DeterministicTargetStateBinding[] nextBreakers = breakerBindings ??
                Array.Empty<DeterministicTargetStateBinding>();
            PlannedKeyReleaseBinding[] nextKeys = keyBindings ??
                Array.Empty<PlannedKeyReleaseBinding>();
            bool manageRuntimeSubscriptions =
                Application.isPlaying && isActiveAndEnabled;
            if (manageRuntimeSubscriptions)
            {
                Unbind();
            }
            ResetPresentation();
            coordinator = nextCoordinator;
            safeDoor = nextSafeDoor;
            chestLid = nextChestLid;
            cabinetLeftDoor = nextCabinetLeft;
            cabinetRightDoor = nextCabinetRight;
            finalLeftDoor = nextFinalLeft;
            chestButtons = nextChestButtons;
            cabinetButtons = nextCabinetButtons;
            breakers = nextBreakers;
            plannedKeys = nextKeys;
            CaptureAuthoredState();
            if (manageRuntimeSubscriptions)
            {
                Bind();
            }
            RebuildFromAuthority();
        }

        /// <summary>
        /// Reconstructs deterministic visuals from the current W1 lifecycle
        /// snapshot and W7 task-progress snapshot. No lifecycle state is
        /// inferred or advanced here.
        /// </summary>
        public void RebuildFromAuthority()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ResetPresentation();
            RunPlan plan = coordinator?.Plan;
            if (plan == null)
            {
                return;
            }

            PrepareKeysForPlan(plan);
            if (coordinator.LifecycleSnapshot == null)
            {
                return;
            }

            InteractionTaskPresentationSnapshot snapshot =
                coordinator.PresentationSnapshot;
            if (snapshot == null)
            {
                return;
            }

            ActivateStates(
                chestButtons,
                snapshot.ChestButtonTargetIds
            );
            ActivateStates(
                cabinetButtons,
                snapshot.CabinetButtonTargetIds
            );
            ActivateStates(breakers, snapshot.BreakerTargetIds);

            if (snapshot.SafeDoorOpened)
            {
                safeDoor.Open();
            }
            if (snapshot.ChestOpened)
            {
                chestLid.Open();
                ReleasePlannedKey(snapshot.ReleasedKeyTargetId);
            }
            if (snapshot.CabinetUnlocked)
            {
                cabinetLeftDoor.Open();
                cabinetRightDoor.Open();
            }
            if (snapshot.FinalDoorOpened)
            {
                finalLeftDoor.Open();
            }
        }

        public void ResetPresentation()
        {
            safeDoor.Reset();
            chestLid.Reset();
            cabinetLeftDoor.Reset();
            cabinetRightDoor.Reset();
            finalLeftDoor.Reset();
            ResetStates(chestButtons);
            ResetStates(cabinetButtons);
            ResetStates(breakers);
            for (int index = 0; index < plannedKeys.Length; index++)
            {
                plannedKeys[index]?.RestoreAuthoredState();
            }
        }

        private void CaptureAuthoredState()
        {
            safeDoor.CaptureClosedPose();
            chestLid.CaptureClosedPose();
            cabinetLeftDoor.CaptureClosedPose();
            cabinetRightDoor.CaptureClosedPose();
            finalLeftDoor.CaptureClosedPose();
            CaptureStates(chestButtons);
            CaptureStates(cabinetButtons);
            CaptureStates(breakers);
        }

        private void Bind()
        {
            if (!Application.isPlaying || subscribed || coordinator == null ||
                !isActiveAndEnabled)
            {
                return;
            }
            coordinator.RunConfigured += HandleRunConfigured;
            coordinator.RunReset += HandleRunReset;
            coordinator.ResultProduced += HandleResult;
            subscribed = true;
        }

        private void Unbind()
        {
            if (subscribed && coordinator != null)
            {
                coordinator.RunConfigured -= HandleRunConfigured;
                coordinator.RunReset -= HandleRunReset;
                coordinator.ResultProduced -= HandleResult;
            }
            subscribed = false;
        }

        private void HandleRunConfigured(RunPlan plan)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordRunConfigured();
#endif
            RebuildFromAuthority();
        }

        private void HandleRunReset()
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordRunReset();
#endif
            ResetPresentation();
        }

        private void PrepareKeysForPlan(RunPlan plan)
        {
            string plannedKey = plan.Phases[3].TaskVariant.TargetIds[0];
            for (int index = 0; index < plannedKeys.Length; index++)
            {
                PlannedKeyReleaseBinding binding = plannedKeys[index];
                if (binding == null)
                {
                    continue;
                }
                binding.PrepareForRun(string.Equals(
                    binding.TargetId,
                    plannedKey,
                    StringComparison.Ordinal
                ));
            }
        }

        private static void ActivateStates(
            DeterministicTargetStateBinding[] bindings,
            IReadOnlyList<string> activeTargetIds)
        {
            if (bindings == null || activeTargetIds == null)
            {
                return;
            }
            for (int targetIndex = 0;
                targetIndex < activeTargetIds.Count;
                targetIndex++)
            {
                string targetId = activeTargetIds[targetIndex];
                for (int bindingIndex = 0;
                    bindingIndex < bindings.Length;
                    bindingIndex++)
                {
                    DeterministicTargetStateBinding binding =
                        bindings[bindingIndex];
                    if (binding != null && string.Equals(
                        binding.TargetId,
                        targetId,
                        StringComparison.Ordinal))
                    {
                        binding.Activate();
                        break;
                    }
                }
            }
        }

        private void HandleResult(ValidationResult result)
        {
#if UNITY_INCLUDE_TESTS
            subscriptionDiagnostic.RecordResultProduced();
#endif
            if (!isActiveAndEnabled || result == null)
            {
                return;
            }
            if (result.ProgressReset || result.PhaseGivenUp)
            {
                ResetPhaseProgress(result.PhaseId);
            }
            else if (result.Accepted &&
                     !string.IsNullOrWhiteSpace(result.TargetId))
            {
                ActivatePhaseTarget(result.PhaseId, result.TargetId);
            }

            switch (result.FeedbackCue)
            {
                case PhaseFeedbackCue.SafeDoorOpened:
                    safeDoor.Open();
                    break;
                case PhaseFeedbackCue.ChestOpened:
                    chestLid.Open();
                    ReleasePlannedKey(result.ReleasedTargetId);
                    break;
                case PhaseFeedbackCue.CabinetUnlocked:
                    cabinetLeftDoor.Open();
                    cabinetRightDoor.Open();
                    break;
                case PhaseFeedbackCue.FinalLeftDoorOpened:
                    finalLeftDoor.Open();
                    break;
            }
        }

        private void ActivatePhaseTarget(int phaseId, string targetId)
        {
            DeterministicTargetStateBinding[] bindings = phaseId == 4
                ? chestButtons
                : phaseId == 5
                    ? cabinetButtons
                    : phaseId == 6
                        ? breakers
                        : null;
            if (bindings == null)
            {
                return;
            }

            for (int index = 0; index < bindings.Length; index++)
            {
                DeterministicTargetStateBinding binding = bindings[index];
                if (binding != null && string.Equals(
                    binding.TargetId,
                    targetId,
                    StringComparison.Ordinal))
                {
                    binding.Activate();
                    return;
                }
            }
        }

        private void ResetPhaseProgress(int phaseId)
        {
            if (phaseId == 4)
            {
                ResetStates(chestButtons);
            }
            else if (phaseId == 5)
            {
                ResetStates(cabinetButtons);
            }
            else if (phaseId == 6)
            {
                ResetStates(breakers);
            }
        }

        private void ReleasePlannedKey(string releasedTargetId)
        {
            for (int index = 0; index < plannedKeys.Length; index++)
            {
                PlannedKeyReleaseBinding binding = plannedKeys[index];
                if (binding != null && string.Equals(
                    binding.TargetId,
                    releasedTargetId,
                    StringComparison.Ordinal))
                {
                    binding.Release();
                }
            }
        }

        private static void CaptureStates(
            DeterministicTargetStateBinding[] bindings)
        {
            if (bindings == null)
            {
                return;
            }
            for (int index = 0; index < bindings.Length; index++)
            {
                bindings[index]?.CaptureIdlePose();
            }
        }

        private static void ResetStates(
            DeterministicTargetStateBinding[] bindings)
        {
            if (bindings == null)
            {
                return;
            }
            for (int index = 0; index < bindings.Length; index++)
            {
                bindings[index]?.Reset();
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
            ResetPresentation();
        }
    }

}
