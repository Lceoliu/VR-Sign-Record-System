using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;
using UnityEngine.Events;

namespace SignVR.Interaction.PhaseAdapters
{
    [DisallowMultipleComponent]
    public sealed class InteractionPhaseCoordinator : MonoBehaviour
    {
        [SerializeField]
        private InteractionPhaseAdapter[] phaseAdapters =
            Array.Empty<InteractionPhaseAdapter>();

        [SerializeField]
        private Transform[] allowedInteractorRoots = Array.Empty<Transform>();

        [Header("Auditable result events")]
        [SerializeField]
        private UnityEvent onAccepted = new UnityEvent();

        [SerializeField]
        private UnityEvent onInteractionError = new UnityEvent();

        [SerializeField]
        private UnityEvent onTaskProgressReset = new UnityEvent();

        [SerializeField]
        private UnityEvent onPhaseCompleted = new UnityEvent();

        private InteractionPhaseSession session;
        private bool sessionSubscribed;

        public event Action<RunPlan> RunConfigured;
        public event Action RunReset;
        public event Action<ValidationResult> ResultProduced;
        public event Action<ValidationResult> InputAccepted;
        public event Action<ValidationResult> InteractionError;
        public event Action<ValidationResult> TaskProgressReset;
        public event Action<ValidationResult> PhaseCompleted;
        public event Action<ValidationResult> PhaseGivenUp;

        public RunPlan Plan => session?.Plan;

        public int? CurrentPhaseId => session?.CurrentPhaseId;

        public bool IsEnabled =>
            isActiveAndEnabled && session != null && session.IsEnabled;

        public ValidationResult LastResult => session?.LastResult;

        public PhaseExecutionSnapshot LifecycleSnapshot =>
            session?.LifecycleSnapshot;

        public IReadOnlyList<InteractionPhaseAdapter> PhaseAdapters =>
            phaseAdapters;

        public IReadOnlyList<Transform> AllowedInteractorRoots =>
            allowedInteractorRoots;

        private void Awake()
        {
            EnsureSession();
            ResolveAdaptersIfNeeded();
        }

        private void OnEnable()
        {
            EnsureSession();
            ResolveAdaptersIfNeeded();
        }

        public void ConfigureAdapters(
            InteractionPhaseAdapter[] configuredAdapters)
        {
            phaseAdapters = configuredAdapters ??
                Array.Empty<InteractionPhaseAdapter>();
        }

        public void ConfigureAllowedInteractorRoots(
            Transform[] configuredRoots)
        {
            if (configuredRoots == null)
            {
                throw new ArgumentNullException(nameof(configuredRoots));
            }
            if (configuredRoots.Length != 2)
            {
                throw new ArgumentException(
                    "Exactly the left and right bare-hand interactor roots " +
                    "are required.",
                    nameof(configuredRoots)
                );
            }

            var unique = new HashSet<Transform>();
            var copy = new List<Transform>(configuredRoots.Length);
            for (int index = 0; index < configuredRoots.Length; index++)
            {
                Transform root = configuredRoots[index];
                if (root == null || !unique.Add(root))
                {
                    throw new ArgumentException(
                        "Allowed interactor roots must be non-null and unique.",
                        nameof(configuredRoots)
                    );
                }
                copy.Add(root);
            }
            allowedInteractorRoots = copy.ToArray();
        }

        public void Configure(RunPlan runPlan)
        {
            if (runPlan == null)
            {
                throw new ArgumentNullException(nameof(runPlan));
            }

            EnsureSession();
            ResolveAdaptersIfNeeded();
            ValidateSixAdapters();

            session.Configure(runPlan);
            for (int index = 0; index < phaseAdapters.Length; index++)
            {
                phaseAdapters[index].Configure(this, runPlan);
            }

            UpdateAdapterAvailability();
            RunConfigured?.Invoke(runPlan);
        }

        public void Reset()
        {
            EnsureSession();
            session.Reset();
            ResetAdapters();
            RunReset?.Invoke();
        }

        public void Abort()
        {
            EnsureSession();
            session.Abort();
            ResetAdapters();
            RunReset?.Invoke();
        }

        public void Enable()
        {
            if (!isActiveAndEnabled)
            {
                throw new InvalidOperationException(
                    "A disabled coordinator cannot enable local interactions."
                );
            }
            EnsureSession();
            session.Enable();
            UpdateAdapterAvailability();
        }

        public void Disable()
        {
            EnsureSession();
            session.Disable();
            UpdateAdapterAvailability();
        }

        public void Synchronize(PhaseExecutionSnapshot snapshot)
        {
            EnsureSession();
            session.Synchronize(snapshot);
            UpdateAdapterAvailability();
        }

        public ValidationResult AcceptInput(
            int phaseId,
            PhaseInput input)
        {
            EnsureSession();
            return session.AcceptInput(phaseId, input);
        }

        public ValidationResult GiveUpCurrentPhase(
            PhaseExecutionSnapshot snapshot)
        {
            EnsureSession();
            return session.GiveUpCurrentPhase(snapshot);
        }

        private void EnsureSession()
        {
            if (session == null)
            {
                session = new InteractionPhaseSession();
            }

            if (!sessionSubscribed && isActiveAndEnabled)
            {
                session.ResultProduced += HandleSessionResult;
                sessionSubscribed = true;
            }
        }

        private void ResolveAdaptersIfNeeded()
        {
            if (phaseAdapters != null && phaseAdapters.Length > 0)
            {
                return;
            }

            phaseAdapters = GetComponentsInChildren<InteractionPhaseAdapter>(
                true
            );
            Array.Sort(
                phaseAdapters,
                (left, right) => left.PhaseId.CompareTo(right.PhaseId)
            );
        }

        private void ValidateSixAdapters()
        {
            if (phaseAdapters == null ||
                phaseAdapters.Length != PhaseSentenceRanges.PhaseCount)
            {
                throw new InvalidOperationException(
                    "Exactly six Interaction Phase adapters are required."
                );
            }

            var seen = new HashSet<int>();
            for (int index = 0; index < phaseAdapters.Length; index++)
            {
                InteractionPhaseAdapter adapter = phaseAdapters[index];
                if (adapter == null || !seen.Add(adapter.PhaseId))
                {
                    throw new InvalidOperationException(
                        "Phase adapters must be non-null and uniquely cover " +
                        "phases 1 through 6."
                    );
                }
            }

            for (int phaseId = 1;
                phaseId <= PhaseSentenceRanges.PhaseCount;
                phaseId++)
            {
                if (!seen.Contains(phaseId))
                {
                    throw new InvalidOperationException(
                        $"Phase {phaseId} has no Unity adapter."
                    );
                }
            }
        }

        private void HandleSessionResult(ValidationResult result)
        {
            UpdateAdapterAvailability();
            ResultProduced?.Invoke(result);

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
                PhaseCompleted?.Invoke(result);
                onPhaseCompleted.Invoke();
            }

            if (result.PhaseGivenUp)
            {
                PhaseGivenUp?.Invoke(result);
            }

        }

        private void UpdateAdapterAvailability()
        {
            if (phaseAdapters == null)
            {
                return;
            }

            int? current = session?.CurrentPhaseId;
            bool interactionEnabled =
                isActiveAndEnabled && session != null && session.IsEnabled;
            for (int index = 0; index < phaseAdapters.Length; index++)
            {
                InteractionPhaseAdapter adapter = phaseAdapters[index];
                if (adapter == null)
                {
                    continue;
                }

                if (interactionEnabled &&
                    current.HasValue &&
                    current.Value == adapter.PhaseId)
                {
                    adapter.Enable();
                }
                else
                {
                    adapter.Disable();
                }
            }
        }

        private void ResetAdapters()
        {
            if (phaseAdapters == null)
            {
                return;
            }

            for (int index = 0; index < phaseAdapters.Length; index++)
            {
                phaseAdapters[index]?.Reset();
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.Disable();
            }
            UpdateAdapterAvailability();
            UnsubscribeSession();
        }

        private void OnDestroy()
        {
            UnsubscribeSession();
        }

        private void UnsubscribeSession()
        {
            if (session != null && sessionSubscribed)
            {
                session.ResultProduced -= HandleSessionResult;
                sessionSubscribed = false;
            }
        }
    }

}
