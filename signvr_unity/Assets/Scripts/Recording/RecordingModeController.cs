using System.Collections.Generic;
using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Keeps the tracked recorder out of scene physics while preserving hand
    /// trigger volumes for local recording controls. Original component states
    /// are restored when recording isolation is disabled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingModeController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VRPlayerRig playerRig;
        [SerializeField] private Transform recorderPhysicsRoot;

        [Header("Isolation")]
        [SerializeField] private bool activateOnEnable = true;
        [SerializeField] private bool disableNonTriggerColliders = true;
        [SerializeField] private bool disableHandPhysicsLimiters = true;
        [SerializeField, Min(0.1f)] private float refreshIntervalSeconds = 0.5f;

        private readonly Dictionary<Collider, bool> colliderStates = new();
        private readonly Dictionary<VRHandPhysicsLimiter, bool> limiterStates = new();
        private bool recordingModeActive;
        private float nextRefreshTime;

        public bool IsRecordingModeActive => recordingModeActive;
        public VRPlayerRig PlayerRig => playerRig;
        public Transform RecorderPhysicsRoot => recorderPhysicsRoot;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (activateOnEnable)
            {
                EnableRecordingMode();
            }
        }

        private void LateUpdate()
        {
            if (!recordingModeActive ||
                Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            RefreshIsolation();
            nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
        }

        /// <summary>
        /// Sets the tracked player whose physical descendants are isolated.
        /// An omitted physics root uses the complete player hierarchy.
        /// </summary>
        public void Configure(
            VRPlayerRig rig,
            Transform physicsRoot = null,
            bool enableImmediately = true)
        {
            if (recordingModeActive)
            {
                DisableRecordingMode();
            }

            playerRig = rig;
            recorderPhysicsRoot = physicsRoot != null
                ? physicsRoot
                : playerRig != null ? playerRig.transform : null;
            activateOnEnable = enableImmediately;

            if (enableImmediately && isActiveAndEnabled)
            {
                EnableRecordingMode();
            }
        }

        public void SetRecordingMode(bool enabled)
        {
            if (enabled)
            {
                EnableRecordingMode();
            }
            else
            {
                DisableRecordingMode();
            }
        }

        public bool EnableRecordingMode()
        {
            ResolveReferences();
            if (playerRig == null)
            {
                Debug.LogWarning(
                    "[RecordingModeController] No VRPlayerRig was found; " +
                    "recorder physics isolation was not enabled.",
                    this
                );
                return false;
            }

            if (!recordingModeActive)
            {
                recordingModeActive = true;
                playerRig.SetRecordingMode(true);
            }

            RefreshIsolation();
            nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;
            return true;
        }

        /// <summary>
        /// Applies isolation to colliders that appeared after startup, such as
        /// hand capsules generated when tracking first becomes available.
        /// </summary>
        public void RefreshIsolation()
        {
            if (!recordingModeActive)
            {
                return;
            }

            ResolveReferences();
            if (recorderPhysicsRoot == null)
            {
                return;
            }

            if (disableHandPhysicsLimiters)
            {
                VRHandPhysicsLimiter[] limiters =
                    recorderPhysicsRoot.GetComponentsInChildren<VRHandPhysicsLimiter>(true);
                foreach (VRHandPhysicsLimiter limiter in limiters)
                {
                    if (limiter == null)
                    {
                        continue;
                    }

                    if (!limiterStates.ContainsKey(limiter))
                    {
                        limiterStates.Add(limiter, limiter.enabled);
                    }
                    limiter.enabled = false;
                }
            }

            if (!disableNonTriggerColliders)
            {
                return;
            }

            Collider[] colliders =
                recorderPhysicsRoot.GetComponentsInChildren<Collider>(true);
            foreach (Collider recorderCollider in colliders)
            {
                if (recorderCollider == null ||
                    recorderCollider is CharacterController ||
                    recorderCollider.isTrigger)
                {
                    continue;
                }

                if (!colliderStates.ContainsKey(recorderCollider))
                {
                    colliderStates.Add(recorderCollider, recorderCollider.enabled);
                }
                recorderCollider.enabled = false;
            }
        }

        public void DisableRecordingMode()
        {
            if (!recordingModeActive)
            {
                return;
            }

            recordingModeActive = false;
            RestoreComponentStates();
            if (playerRig != null)
            {
                playerRig.SetRecordingMode(false);
            }
        }

        private void ResolveReferences()
        {
            if (playerRig == null)
            {
                playerRig = VRPlayerRig.Instance;
            }
            if (playerRig == null)
            {
                playerRig = FindAnyObjectByType<VRPlayerRig>(
                    FindObjectsInactive.Include
                );
            }
            if (recorderPhysicsRoot == null && playerRig != null)
            {
                recorderPhysicsRoot = playerRig.transform;
            }
        }

        private void RestoreComponentStates()
        {
            foreach (KeyValuePair<VRHandPhysicsLimiter, bool> state in limiterStates)
            {
                if (state.Key != null)
                {
                    state.Key.enabled = state.Value;
                }
            }
            limiterStates.Clear();

            foreach (KeyValuePair<Collider, bool> state in colliderStates)
            {
                if (state.Key != null)
                {
                    state.Key.enabled = state.Value;
                }
            }
            colliderStates.Clear();
        }

        private void OnDisable()
        {
            DisableRecordingMode();
        }
    }
}
