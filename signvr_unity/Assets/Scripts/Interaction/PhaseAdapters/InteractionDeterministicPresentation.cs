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

        [SerializeField]
        private Vector3 closedLocalPosition;

        [SerializeField]
        private Quaternion closedLocalRotation;

        [SerializeField]
        private Vector3 openLocalPosition;

        [SerializeField]
        private Quaternion openLocalRotation;

        [SerializeField]
        private bool closedPoseCaptured;

        [SerializeField]
        private bool explicitClosedPose;

        [SerializeField]
        private bool openInPlace;

        [SerializeField]
        private bool rotateAroundWorldHinge;

        [SerializeField]
        private Vector3 closedWorldPosition;

        [SerializeField]
        private Quaternion closedWorldRotation;

        [SerializeField]
        private Vector3 hingeWorldPosition;

        [SerializeField]
        private Vector3 hingeWorldAxis = Vector3.up;

        private bool opened;
        private bool openingAnimating;
        private double openingStartedAt;
        private double openingDuration;

        public Transform MovingPart => movingPart;

        public Transform Hinge => hinge;

        public bool IsConfigured => movingPart != null && hinge != null;

        public bool IsOpen => opened && !openingAnimating;

        public bool IsOpening => openingAnimating;

        public bool UsesExplicitClosedPose =>
            IsConfigured && closedPoseCaptured && explicitClosedPose;

        public Vector3 ClosedLocalPosition => closedLocalPosition;

        public Quaternion ClosedLocalRotation => closedLocalRotation;

        public Vector3 OpenLocalPosition => openLocalPosition;

        public Quaternion OpenLocalRotation => openLocalRotation;

        public bool UsesWorldHingeArc => rotateAroundWorldHinge;

        public Vector3 HingeWorldPosition => hingeWorldPosition;

        public Vector3 HingeWorldAxis => hingeWorldAxis;

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
            explicitClosedPose = false;
            openInPlace = false;
            rotateAroundWorldHinge = false;
            CaptureClosedPose();
        }

        public void ConfigureAbsolute(
            Transform targetMovingPart,
            Transform targetHinge,
            Vector3 localAxis,
            float openAngle,
            Vector3 absoluteClosedLocalPosition,
            Vector3 absoluteClosedLocalEulerAngles)
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
            openInPlace = false;
            rotateAroundWorldHinge = false;
            closedLocalPosition = absoluteClosedLocalPosition;
            closedLocalRotation = Quaternion.Euler(
                absoluteClosedLocalEulerAngles
            );
            closedPoseCaptured = true;
            explicitClosedPose = true;
            CaptureOpenPoseFromClosed();
            Reset();
        }

        public void ConfigureAbsoluteInPlace(
            Transform targetMovingPart,
            Transform targetHinge,
            Vector3 localAxis,
            float openAngle,
            Vector3 absoluteClosedLocalPosition,
            Vector3 absoluteClosedLocalEulerAngles)
        {
            ConfigureAbsolute(
                targetMovingPart,
                targetHinge,
                localAxis,
                openAngle,
                absoluteClosedLocalPosition,
                absoluteClosedLocalEulerAngles
            );
            openInPlace = true;
            rotateAroundWorldHinge = false;
            CaptureOpenPoseFromClosed();
            Reset();
        }

        public void ConfigureAbsoluteWorldHinge(
            Transform targetMovingPart,
            Transform targetHinge,
            Vector3 worldAxis,
            float openAngle,
            Vector3 absoluteClosedLocalPosition,
            Vector3 absoluteClosedLocalEulerAngles)
        {
            Transform nextMovingPart = targetMovingPart ??
                throw new ArgumentNullException(nameof(targetMovingPart));
            Transform nextHinge = targetHinge ??
                throw new ArgumentNullException(nameof(targetHinge));
            Reset();
            movingPart = nextMovingPart;
            hinge = nextHinge;
            hingeLocalAxis = worldAxis.sqrMagnitude > 0.0001f
                ? worldAxis.normalized
                : Vector3.up;
            openAngleDegrees = openAngle;
            closedLocalPosition = absoluteClosedLocalPosition;
            closedLocalRotation = Quaternion.Euler(
                absoluteClosedLocalEulerAngles
            );
            closedPoseCaptured = true;
            explicitClosedPose = true;
            openInPlace = false;
            rotateAroundWorldHinge = true;
            movingPart.SetLocalPositionAndRotation(
                closedLocalPosition,
                closedLocalRotation
            );
            closedWorldPosition = movingPart.position;
            closedWorldRotation = movingPart.rotation;
            hingeWorldPosition = hinge.position;
            hingeWorldAxis = hingeLocalAxis;
            ApplyWorldHingeRotation(1f);
            openLocalPosition = movingPart.localPosition;
            openLocalRotation = movingPart.localRotation;
            Reset();
        }

        public void CaptureClosedPose()
        {
            if (!IsConfigured)
            {
                closedPoseCaptured = false;
                return;
            }
            if (explicitClosedPose && closedPoseCaptured)
            {
                return;
            }

            closedLocalPosition = movingPart.localPosition;
            closedLocalRotation = movingPart.localRotation;
            closedPoseCaptured = true;
            CaptureOpenPoseFromClosed();
            opened = false;
            openingAnimating = false;
        }

        public void Reset()
        {
            openingAnimating = false;
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
            if (!IsConfigured)
            {
                return;
            }
            if (!closedPoseCaptured)
            {
                CaptureClosedPose();
            }

            ApplyOpeningProgress(1f);
            opened = true;
            openingAnimating = false;
        }

        public void OpenAnimated(double monotonicSeconds, double durationSeconds)
        {
            if (!IsConfigured)
            {
                return;
            }
            if (!closedPoseCaptured)
            {
                CaptureClosedPose();
            }
            movingPart.SetLocalPositionAndRotation(
                closedLocalPosition,
                closedLocalRotation
            );
            opened = true;
            openingAnimating = durationSeconds > 0d;
            openingStartedAt = monotonicSeconds;
            openingDuration = Math.Max(0.0001d, durationSeconds);
            if (!openingAnimating)
            {
                ApplyOpeningProgress(1f);
            }
        }

        public bool TickOpeningAnimation(double monotonicSeconds)
        {
            if (!openingAnimating || !IsConfigured)
            {
                return false;
            }
            float progress = Mathf.Clamp01((float)((monotonicSeconds -
                openingStartedAt) / openingDuration));
            progress = progress * progress * (3f - 2f * progress);
            ApplyOpeningProgress(progress);
            if (progress >= 1f)
            {
                openingAnimating = false;
            }
            return openingAnimating;
        }

        private void CaptureOpenPoseFromClosed()
        {
            movingPart.SetLocalPositionAndRotation(
                closedLocalPosition,
                closedLocalRotation
            );
            Vector3 localAxis = hingeLocalAxis.sqrMagnitude > 0.0001f
                ? hingeLocalAxis.normalized
                : Vector3.up;
            if (openInPlace)
            {
                movingPart.localRotation =
                    Quaternion.AngleAxis(openAngleDegrees, localAxis) *
                    closedLocalRotation;
            }
            else
            {
                movingPart.RotateAround(
                    hinge.position,
                    hinge.TransformDirection(localAxis),
                    openAngleDegrees
                );
            }
            openLocalPosition = movingPart.localPosition;
            openLocalRotation = movingPart.localRotation;
            movingPart.SetLocalPositionAndRotation(
                closedLocalPosition,
                closedLocalRotation
            );
        }

        private void ApplyOpeningProgress(float progress)
        {
            if (rotateAroundWorldHinge)
            {
                ApplyWorldHingeRotation(progress);
                return;
            }
            movingPart.SetLocalPositionAndRotation(
                Vector3.Lerp(closedLocalPosition, openLocalPosition, progress),
                Quaternion.Slerp(closedLocalRotation, openLocalRotation, progress)
            );
        }

        private void ApplyWorldHingeRotation(float progress)
        {
            Quaternion rotation = Quaternion.AngleAxis(
                openAngleDegrees * Mathf.Clamp01(progress),
                hingeWorldAxis
            );
            movingPart.SetPositionAndRotation(
                hingeWorldPosition + rotation *
                    (closedWorldPosition - hingeWorldPosition),
                rotation * closedWorldRotation
            );
        }
    }

    public enum DeterministicTargetVisualState
    {
        Idle = 0,
        Accepted = 1,
        Error = 2
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

        [SerializeField]
        private Vector3 activatedLocalPositionOffset = Vector3.zero;

        [SerializeField]
        private Renderer[] feedbackRenderers = Array.Empty<Renderer>();

        [SerializeField]
        private Color acceptedColor = Color.green;

        [SerializeField]
        private Color errorColor = Color.red;

        [SerializeField]
        private Vector3 idleLocalPosition;

        [SerializeField]
        private Quaternion idleLocalRotation;

        [SerializeField]
        private Vector3 activatedLocalPosition;

        [SerializeField]
        private Quaternion activatedLocalRotation;

        [SerializeField]
        private bool idlePoseCaptured;

        [SerializeField]
        private bool usesExplicitPoses;

        private MaterialPropertyBlock[] idlePropertyBlocks =
            Array.Empty<MaterialPropertyBlock>();
        private DeterministicTargetVisualState visualState;
        private bool activationAnimating;
        private double activationAnimationStartedAt;
        private double activationAnimationDuration;

        public string TargetId => targetId;

        public Transform Target => target;

        public IReadOnlyList<Renderer> FeedbackRenderers => feedbackRenderers;

        public DeterministicTargetVisualState VisualState => visualState;

        public Color FeedbackColor => visualState ==
            DeterministicTargetVisualState.Accepted
                ? acceptedColor
                : visualState == DeterministicTargetVisualState.Error
                    ? errorColor
                    : Color.clear;

        public bool UsesExplicitPoses => usesExplicitPoses;

        public Vector3 IdleLocalPosition => idleLocalPosition;

        public Quaternion IdleLocalRotation => idleLocalRotation;

        public Vector3 ActivatedLocalPosition => activatedLocalPosition;

        public Quaternion ActivatedLocalRotation => activatedLocalRotation;

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
            activatedLocalPositionOffset = Vector3.zero;
            activatedLocalEulerOffset = localEulerOffset;
            feedbackRenderers = Array.Empty<Renderer>();
            usesExplicitPoses = false;
            CaptureIdlePose();
        }

        public void ConfigureFeedback(
            string stableTargetId,
            Transform targetTransform,
            Vector3 localPositionOffset,
            Vector3 localEulerOffset,
            Renderer[] renderers,
            Color acceptedStateColor,
            Color errorStateColor)
        {
            Configure(stableTargetId, targetTransform, localEulerOffset);
            activatedLocalPositionOffset = localPositionOffset;
            feedbackRenderers = renderers ?? Array.Empty<Renderer>();
            acceptedColor = acceptedStateColor;
            errorColor = errorStateColor;
            CaptureRendererState();
        }

        public void ConfigureAbsolute(
            string stableTargetId,
            Transform targetTransform,
            Vector3 absoluteIdleLocalPosition,
            Vector3 absoluteIdleLocalEulerAngles,
            Vector3 absoluteActivatedLocalPosition,
            Vector3 absoluteActivatedLocalEulerAngles)
        {
            Configure(stableTargetId, targetTransform, Vector3.zero);
            usesExplicitPoses = true;
            idleLocalPosition = absoluteIdleLocalPosition;
            idleLocalRotation = Quaternion.Euler(
                absoluteIdleLocalEulerAngles
            );
            activatedLocalPosition = absoluteActivatedLocalPosition;
            activatedLocalRotation = Quaternion.Euler(
                absoluteActivatedLocalEulerAngles
            );
            idlePoseCaptured = true;
            Reset();
        }

        public void CaptureIdlePose()
        {
            if (target == null)
            {
                idlePoseCaptured = false;
                return;
            }
            if (usesExplicitPoses && idlePoseCaptured)
            {
                return;
            }
            idleLocalPosition = target.localPosition;
            idleLocalRotation = target.localRotation;
            activatedLocalPosition = idleLocalPosition +
                activatedLocalPositionOffset;
            activatedLocalRotation = idleLocalRotation * Quaternion.Euler(
                activatedLocalEulerOffset
            );
            idlePoseCaptured = true;
            CaptureRendererState();
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
            if (!usesExplicitPoses)
            {
                activatedLocalPosition = idleLocalPosition +
                    activatedLocalPositionOffset;
                activatedLocalRotation = idleLocalRotation *
                    Quaternion.Euler(activatedLocalEulerOffset);
            }
            target.SetLocalPositionAndRotation(
                activatedLocalPosition,
                activatedLocalRotation
            );
            visualState = DeterministicTargetVisualState.Accepted;
            ApplyFeedbackColor(acceptedColor);
            activationAnimating = false;
        }

        public void ActivateAnimated(
            double monotonicSeconds,
            double durationSeconds)
        {
            Activate();
            if (!IsConfigured || durationSeconds <= 0d)
            {
                return;
            }
            target.SetLocalPositionAndRotation(
                idleLocalPosition,
                idleLocalRotation
            );
            activationAnimating = true;
            activationAnimationStartedAt = monotonicSeconds;
            activationAnimationDuration = durationSeconds;
        }

        public bool TickActivationAnimation(double monotonicSeconds)
        {
            if (!activationAnimating || !IsConfigured)
            {
                return false;
            }
            double elapsed = monotonicSeconds - activationAnimationStartedAt;
            float progress = Mathf.Clamp01((float)(elapsed /
                Math.Max(0.0001d, activationAnimationDuration)));
            progress = progress * progress * (3f - 2f * progress);
            target.SetLocalPositionAndRotation(
                Vector3.Lerp(
                    idleLocalPosition,
                    activatedLocalPosition,
                    progress
                ),
                Quaternion.Slerp(
                    idleLocalRotation,
                    activatedLocalRotation,
                    progress
                )
            );
            if (progress >= 1f)
            {
                activationAnimating = false;
            }
            return activationAnimating;
        }

        public void ShowError()
        {
            if (!IsConfigured)
            {
                return;
            }
            if (!idlePoseCaptured)
            {
                CaptureIdlePose();
            }
            if (!usesExplicitPoses)
            {
                activatedLocalPosition = idleLocalPosition +
                    activatedLocalPositionOffset;
                activatedLocalRotation = idleLocalRotation *
                    Quaternion.Euler(activatedLocalEulerOffset);
            }
            target.SetLocalPositionAndRotation(
                activatedLocalPosition,
                activatedLocalRotation
            );
            activationAnimating = false;
            visualState = DeterministicTargetVisualState.Error;
            ApplyFeedbackColor(errorColor);
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
            activationAnimating = false;
            visualState = DeterministicTargetVisualState.Idle;
            RestoreRendererState();
        }

        private void CaptureRendererState()
        {
            idlePropertyBlocks = new MaterialPropertyBlock[
                feedbackRenderers.Length
            ];
            for (int index = 0; index < feedbackRenderers.Length; index++)
            {
                Renderer renderer = feedbackRenderers[index];
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(block);
                }
                idlePropertyBlocks[index] = block;
            }
        }

        private void ApplyFeedbackColor(Color color)
        {
            for (int index = 0; index < feedbackRenderers.Length; index++)
            {
                Renderer renderer = feedbackRenderers[index];
                if (renderer == null)
                {
                    continue;
                }
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }
        }

        private void RestoreRendererState()
        {
            for (int index = 0; index < feedbackRenderers.Length; index++)
            {
                Renderer renderer = feedbackRenderers[index];
                if (renderer == null)
                {
                    continue;
                }
                renderer.SetPropertyBlock(
                    index < idlePropertyBlocks.Length
                        ? idlePropertyBlocks[index]
                        : null
                );
            }
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
        private GameObject inputProxyRoot;

        [SerializeField]
        private Rigidbody[] bodies = Array.Empty<Rigidbody>();

        [SerializeField]
        private Behaviour[] interactionBehaviours = Array.Empty<Behaviour>();

        [SerializeField]
        private Collider[] interactionColliders = Array.Empty<Collider>();

        private bool authoredActive;
        private bool authoredProxyActive;
        private bool authoredStateCaptured;
        private bool plannedForCurrentRun;
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

        public GameObject InputProxyRoot => inputProxyRoot;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(targetId) && targetRoot != null;

        public void Configure(
            string stableTargetId,
            GameObject root,
            Rigidbody[] targetBodies,
            Behaviour[] behaviours,
            Collider[] colliders)
        {
            ConfigureVisualOnly(
                stableTargetId,
                root,
                null,
                targetBodies,
                behaviours,
                colliders
            );
        }

        public void ConfigureVisualOnly(
            string stableTargetId,
            GameObject visualRoot,
            GameObject proxyRoot,
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
            GameObject nextTargetRoot = visualRoot ??
                throw new ArgumentNullException(nameof(visualRoot));
            Rigidbody[] nextBodies = targetBodies ?? Array.Empty<Rigidbody>();
            Behaviour[] nextBehaviours = behaviours ??
                Array.Empty<Behaviour>();
            Collider[] nextColliders = colliders ?? Array.Empty<Collider>();
            RestoreAuthoredState();
            targetId = nextTargetId;
            targetRoot = nextTargetRoot;
            inputProxyRoot = proxyRoot;
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
            plannedForCurrentRun = isPlanned;
            SetPhaseFourVisible(isPlanned);
            LockAsVisualOnly();
        }

        public void SetPhaseFourVisible(bool visible)
        {
            if (!IsConfigured)
            {
                return;
            }
            targetRoot.SetActive(visible);
            if (inputProxyRoot != null)
            {
                inputProxyRoot.SetActive(visible);
            }
        }

        public void Release()
        {
            if (!IsConfigured)
            {
                return;
            }
            LockAsVisualOnly();
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
            if (inputProxyRoot != null)
            {
                inputProxyRoot.SetActive(authoredProxyActive);
            }
            plannedForCurrentRun = false;
            for (int index = 0; index < bodyCount; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
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
        }

        private void CaptureAuthoredState()
        {
            if (!IsConfigured || authoredStateCaptured)
            {
                return;
            }

            authoredActive = targetRoot.activeSelf;
            authoredProxyActive = inputProxyRoot != null &&
                inputProxyRoot.activeSelf;
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

        private void LockAsVisualOnly()
        {
            for (int index = 0; index < bodies.Length; index++)
            {
                Rigidbody body = bodies[index];
                if (body == null)
                {
                    continue;
                }
                StopAndLock(body);
                body.useGravity = false;
            }

            for (int index = 0;
                index < interactionBehaviours.Length;
                index++)
            {
                Behaviour behaviour = interactionBehaviours[index];
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }

            for (int index = 0;
                index < interactionColliders.Length;
                index++)
            {
                Collider collider = interactionColliders[index];
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }

        private static void StopAndLock(Rigidbody body)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class InteractionDeterministicPresentation : MonoBehaviour
#if UNITY_EDITOR
        , IInteractionOwnedStateTeardown
#endif
    {
        private const double BreakerPullAnimationSeconds = 0.42d;
        public const double ChestLidOpenAnimationSeconds = 1.1d;
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
        private bool phaseFiveErrorResetPending;
        private double feedbackClockSeconds;
        private double phaseFiveErrorResetAt;

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

        public double FeedbackResetDelaySeconds =>
            InteractionPhaseFeedbackTiming
                .PhaseFiveErrorResetDelaySeconds;

#if UNITY_INCLUDE_TESTS
        public InteractionSubscriptionDiagnostic SubscriptionDiagnostic =>
            subscriptionDiagnostic;
#endif

        private void Awake()
        {
            feedbackClockSeconds = Time.realtimeSinceStartupAsDouble;
            CaptureAuthoredState();
            Bind();
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            TickFeedback(now);
            for (int index = 0; index < breakers.Length; index++)
            {
                breakers[index]?.TickActivationAnimation(now);
            }
            chestLid.TickOpeningAnimation(now);
        }

        private void OnEnable()
        {
            feedbackClockSeconds = Time.realtimeSinceStartupAsDouble;
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
            int currentPhaseId = coordinator.CurrentPhaseId ?? 0;
            SetStatesVisible(chestButtons, false);
            SetKeysVisible(currentPhaseId == 4);
            if (currentPhaseId >= 4)
            {
                chestLid.Open();
            }
            if (currentPhaseId >= 5)
            {
                cabinetLeftDoor.Open();
                cabinetRightDoor.Open();
            }
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
            phaseFiveErrorResetPending = false;
            phaseFiveErrorResetAt = 0d;
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
            coordinator.PhaseSynchronized += HandlePhaseSynchronized;
            coordinator.ResultProduced += HandleResult;
            subscribed = true;
        }

        private void Unbind()
        {
            if (subscribed && coordinator != null)
            {
                coordinator.RunConfigured -= HandleRunConfigured;
                coordinator.RunReset -= HandleRunReset;
                coordinator.PhaseSynchronized -= HandlePhaseSynchronized;
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

        private void HandlePhaseSynchronized(
            PhaseExecutionSnapshot snapshot)
        {
            RebuildFromAuthority();
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

        private void SetKeysVisible(bool visible)
        {
            for (int index = 0; index < plannedKeys.Length; index++)
            {
                plannedKeys[index]?.SetPhaseFourVisible(visible);
            }
        }

        private static void SetStatesVisible(
            DeterministicTargetStateBinding[] bindings,
            bool visible)
        {
            if (bindings == null)
            {
                return;
            }
            for (int index = 0; index < bindings.Length; index++)
            {
                Transform target = bindings[index]?.Target;
                if (target != null)
                {
                    target.gameObject.SetActive(visible);
                }
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
            ApplyValidationResult(result);
        }

        public void ApplyValidationResult(ValidationResult result)
        {
            if (!isActiveAndEnabled || result == null)
            {
                return;
            }
            if (result.PhaseId == 5 && result.Accepted &&
                phaseFiveErrorResetPending)
            {
                phaseFiveErrorResetPending = false;
                ResetStates(cabinetButtons);
            }

            if (result.PhaseId == 5 && result.ProgressReset &&
                !result.PhaseGivenUp)
            {
                ShowPhaseFiveError(result.TargetId);
                phaseFiveErrorResetAt = feedbackClockSeconds +
                    InteractionPhaseFeedbackTiming
                        .PhaseFiveErrorResetDelaySeconds;
                phaseFiveErrorResetPending = true;
            }
            else if (result.ProgressReset || result.PhaseGivenUp)
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
                    chestLid.OpenAnimated(
                        feedbackClockSeconds,
                        ChestLidOpenAnimationSeconds
                    );
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

        public void TickFeedback(double monotonicSeconds)
        {
            if (double.IsNaN(monotonicSeconds) ||
                double.IsInfinity(monotonicSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(monotonicSeconds),
                    "Feedback time must be finite."
                );
            }
            feedbackClockSeconds = monotonicSeconds;
            if (!phaseFiveErrorResetPending ||
                feedbackClockSeconds < phaseFiveErrorResetAt)
            {
                return;
            }
            phaseFiveErrorResetPending = false;
            ResetStates(cabinetButtons);
        }

        public void PlayChestOpeningAnimation()
        {
            chestLid.OpenAnimated(
                Time.realtimeSinceStartupAsDouble,
                ChestLidOpenAnimationSeconds
            );
        }

        private void ShowPhaseFiveError(string targetId)
        {
            for (int index = 0; index < cabinetButtons.Length; index++)
            {
                DeterministicTargetStateBinding binding =
                    cabinetButtons[index];
                if (binding != null && string.Equals(
                    binding.TargetId,
                    targetId,
                    StringComparison.Ordinal))
                {
                    binding.ShowError();
                    return;
                }
            }
        }

        private void ActivatePhaseTarget(int phaseId, string targetId)
        {
            DeterministicTargetStateBinding[] bindings = phaseId == 5
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
                    if (phaseId == 6)
                    {
                        binding.ActivateAnimated(
                            feedbackClockSeconds,
                            BreakerPullAnimationSeconds
                        );
                    }
                    else
                    {
                        binding.Activate();
                    }
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

#if UNITY_EDITOR
        void IInteractionOwnedStateTeardown
            .ReleaseOwnedStateForEditorTeardown()
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "Editor teardown is forbidden during Play Mode."
                );
            }
            enabled = false;
            Unbind();
            ResetPresentation();
        }
#endif
    }

}
