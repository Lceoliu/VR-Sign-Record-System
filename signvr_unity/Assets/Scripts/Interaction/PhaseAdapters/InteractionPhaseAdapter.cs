using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;
using UnityEngine.Events;

namespace SignVR.Interaction.PhaseAdapters
{
    public abstract class InteractionPhaseAdapter : MonoBehaviour
    {
        [SerializeField]
        private InteractionPhaseCoordinator coordinator;

        [Header("Per-adapter audit events")]
        [SerializeField]
        private UnityEvent onAccepted = new UnityEvent();

        [SerializeField]
        private UnityEvent onInteractionError = new UnityEvent();

        [SerializeField]
        private UnityEvent onTaskProgressReset = new UnityEvent();

        [SerializeField]
        private UnityEvent onCompleted = new UnityEvent();

        private RunPlan plan;
        private bool resultSubscribed;

        public event Action<bool> AvailabilityChanged;
        public event Action ResetPerformed;
        public event Action<ValidationResult> InputAccepted;
        public event Action<ValidationResult> InteractionError;
        public event Action<ValidationResult> TaskProgressReset;
        public event Action<ValidationResult> Completed;

        public abstract int PhaseId { get; }

        public bool IsEnabled { get; private set; }

        public RunPlan Plan => plan;

        public InteractionPhaseCoordinator Coordinator => coordinator;

        public ValidationResult LastResult { get; private set; }

        public IReadOnlyList<string> PlannedTargetIds => plan == null
            ? Array.Empty<string>()
            : plan.Phases[PhaseId - 1].TaskVariant.TargetIds;

        public virtual void Configure(
            InteractionPhaseCoordinator targetCoordinator,
            RunPlan runPlan)
        {
            if (targetCoordinator == null)
            {
                throw new ArgumentNullException(nameof(targetCoordinator));
            }
            if (runPlan == null)
            {
                throw new ArgumentNullException(nameof(runPlan));
            }
            if (runPlan.Phases[PhaseId - 1].PhaseId != PhaseId)
            {
                throw new ArgumentException(
                    "Run Plan phase does not match this adapter.",
                    nameof(runPlan)
                );
            }

            bool manageRuntimeSubscription =
                Application.isPlaying && isActiveAndEnabled;
            if (manageRuntimeSubscription)
            {
                Unsubscribe();
            }
            coordinator = targetCoordinator;
            plan = runPlan;
            if (manageRuntimeSubscription)
            {
                Subscribe();
            }
            Reset();
        }

        public virtual void Reset()
        {
            LastResult = null;
            Disable();
            ResetPerformed?.Invoke();
        }

        public virtual void Enable()
        {
            bool currentTaskAuthorityAllowsInput =
                object.ReferenceEquals(coordinator, null) ||
                (coordinator != null &&
                 coordinator.CurrentPhaseId.HasValue &&
                 coordinator.CurrentPhaseId.Value == PhaseId &&
                 coordinator.IsEnabled);
            SetAvailability(
                isActiveAndEnabled && currentTaskAuthorityAllowsInput
            );
        }

        public virtual void Disable()
        {
            SetAvailability(false);
        }

        public virtual ValidationResult AcceptInput(PhaseInput input)
        {
            if (coordinator == null || plan == null)
            {
                throw new InvalidOperationException(
                    "Configure the phase adapter before accepting input."
                );
            }

            return coordinator.AcceptInput(PhaseId, input);
        }

        public ValidationResult AcceptTarget(string targetId)
        {
            return AcceptInput(PhaseInput.Target(targetId));
        }

        private void SetAvailability(bool value)
        {
            if (IsEnabled == value)
            {
                return;
            }

            IsEnabled = value;
            AvailabilityChanged?.Invoke(value);
        }

        private void HandleResult(ValidationResult result)
        {
            if (!isActiveAndEnabled || result == null ||
                result.PhaseId != PhaseId)
            {
                return;
            }

            LastResult = result;
            if (result.Accepted)
            {
                InputAccepted?.Invoke(result);
                onAccepted.Invoke();
            }
            if (result.InteractionError)
            {
                InteractionError?.Invoke(result);
                onInteractionError.Invoke();
            }
            if (result.ProgressReset)
            {
                TaskProgressReset?.Invoke(result);
                onTaskProgressReset.Invoke();
            }
            if (result.PhaseCompleted)
            {
                Completed?.Invoke(result);
                onCompleted.Invoke();
            }
        }

        private void Unsubscribe()
        {
            if (coordinator != null && resultSubscribed)
            {
                coordinator.ResultProduced -= HandleResult;
            }
            resultSubscribed = false;
        }

        private void Subscribe()
        {
            if (!Application.isPlaying || coordinator == null ||
                resultSubscribed ||
                !isActiveAndEnabled)
            {
                return;
            }
            coordinator.ResultProduced += HandleResult;
            resultSubscribed = true;
        }

        protected virtual void OnEnable()
        {
            Subscribe();
            if (coordinator != null && coordinator.IsEnabled &&
                coordinator.CurrentPhaseId == PhaseId)
            {
                SetAvailability(true);
            }
        }

        protected virtual void OnDisable()
        {
            Disable();
            Unsubscribe();
        }

        protected virtual void OnDestroy()
        {
            Unsubscribe();
        }
    }
}
