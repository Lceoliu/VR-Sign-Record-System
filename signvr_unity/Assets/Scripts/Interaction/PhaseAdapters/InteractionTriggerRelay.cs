using System;
using System.Collections.Generic;
using SignVR.Interaction.Core;
using UnityEngine;

namespace SignVR.Interaction.PhaseAdapters
{
#if UNITY_EDITOR
    /// <summary>
    /// Editor-only contract used by the transactional setup/strip tooling to
    /// release authored scene state immediately before DestroyImmediate.
    /// Study Player builds do not contain this API.
    /// </summary>
    public interface IInteractionOwnedStateTeardown
    {
        void ReleaseOwnedStateForEditorTeardown();
    }
#endif

    public interface IInteractionTriggerInput
    {
        bool IsInputAvailable { get; }

        ValidationResult AcceptInput();
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class InteractionTriggerRelay : MonoBehaviour
#if UNITY_EDITOR
        , IInteractionOwnedStateTeardown
#endif
    {
        [SerializeField]
        private MonoBehaviour inputReceiver;

        [SerializeField]
        private Transform[] allowedInteractorRoots = Array.Empty<Transform>();

        [SerializeField, Min(0.05f)]
        private float debounceSeconds = 0.3f;

        private float lastTriggerTime = float.NegativeInfinity;

        [SerializeField, HideInInspector]
        private Collider authoredTriggerCollider;

        [SerializeField, HideInInspector]
        private bool authoredTriggerIsTrigger;

        [SerializeField, HideInInspector]
        private bool authoredTriggerStateCaptured;

        public MonoBehaviour InputReceiver => inputReceiver;

        public IReadOnlyList<Transform> AllowedInteractorRoots =>
            allowedInteractorRoots;

        public Collider TriggerCollider => GetComponent<Collider>();

        public bool IsConfigured =>
            inputReceiver is IInteractionTriggerInput &&
            allowedInteractorRoots != null &&
            allowedInteractorRoots.Length > 0 &&
            TriggerCollider != null &&
            TriggerCollider.isTrigger;

        public void Configure(
            MonoBehaviour receiver,
            Transform[] configuredAllowedRoots,
            float triggerDebounceSeconds = 0.3f)
        {
            if (!(receiver is IInteractionTriggerInput))
            {
                throw new ArgumentException(
                    "The trigger receiver must implement " +
                    "IInteractionTriggerInput.",
                    nameof(receiver)
                );
            }
            if (configuredAllowedRoots == null ||
                configuredAllowedRoots.Length == 0)
            {
                throw new ArgumentException(
                    "At least one bare-hand interactor root is required.",
                    nameof(configuredAllowedRoots)
                );
            }

            var unique = new HashSet<Transform>();
            var copy = new List<Transform>(configuredAllowedRoots.Length);
            for (int index = 0;
                index < configuredAllowedRoots.Length;
                index++)
            {
                Transform root = configuredAllowedRoots[index];
                if (root == null || !unique.Add(root))
                {
                    throw new ArgumentException(
                        "Allowed interactor roots must be non-null and unique.",
                        nameof(configuredAllowedRoots)
                    );
                }
                copy.Add(root);
            }

            inputReceiver = receiver;
            allowedInteractorRoots = copy.ToArray();
            debounceSeconds = Mathf.Max(0.05f, triggerDebounceSeconds);
            Collider trigger = TriggerCollider;
            CaptureTriggerOwnership(trigger);
            trigger.isTrigger = true;
            lastTriggerTime = float.NegativeInfinity;
        }

        public ValidationResult AcceptInput()
        {
            IInteractionTriggerInput receiver =
                inputReceiver as IInteractionTriggerInput;
            if (receiver == null)
            {
                throw new InvalidOperationException(
                    $"{name} has no valid trigger input receiver."
                );
            }
            return receiver.AcceptInput();
        }

        public bool IsAllowedInteractor(Collider other)
        {
            if (other == null || allowedInteractorRoots == null)
            {
                return false;
            }

            Transform candidate = other.transform;
            for (int index = 0;
                index < allowedInteractorRoots.Length;
                index++)
            {
                Transform root = allowedInteractorRoots[index];
                if (root != null &&
                    (candidate == root || candidate.IsChildOf(root)))
                {
                    return true;
                }
            }
            return false;
        }

        public ValidationResult AcceptTrigger(Collider other)
        {
            IInteractionTriggerInput receiver =
                inputReceiver as IInteractionTriggerInput;
            if (receiver == null || !receiver.IsInputAvailable ||
                !IsAllowedInteractor(other) ||
                Time.unscaledTime - lastTriggerTime < debounceSeconds)
            {
                return null;
            }

            lastTriggerTime = Time.unscaledTime;
            return receiver.AcceptInput();
        }

        private void OnTriggerEnter(Collider other)
        {
            AcceptTrigger(other);
        }

        private void OnDisable()
        {
            lastTriggerTime = float.NegativeInfinity;
        }

        private void CaptureTriggerOwnership(Collider trigger)
        {
            if (authoredTriggerStateCaptured || trigger == null)
            {
                return;
            }
            authoredTriggerCollider = trigger;
            authoredTriggerIsTrigger = trigger.isTrigger;
            authoredTriggerStateCaptured = true;
        }

        private void ReleaseTriggerOwnership()
        {
            if (authoredTriggerStateCaptured &&
                authoredTriggerCollider != null)
            {
                authoredTriggerCollider.isTrigger =
                    authoredTriggerIsTrigger;
            }
            authoredTriggerCollider = null;
            authoredTriggerStateCaptured = false;
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
            inputReceiver = null;
            allowedInteractorRoots = Array.Empty<Transform>();
            ReleaseTriggerOwnership();
        }
#endif

        private void OnDestroy()
        {
            ReleaseTriggerOwnership();
        }
    }
}
