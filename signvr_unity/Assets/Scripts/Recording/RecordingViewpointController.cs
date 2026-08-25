using System;
using System.Collections.Generic;
using UnityEngine;

namespace SignVR.Recording
{
    /// <summary>
    /// Describes one authored recording viewpoint. A Camera is useful while
    /// placing and previewing the pose, but its Transform is only a pose marker
    /// on Quest. The tracked center-eye camera remains the XR render camera.
    /// </summary>
    [Serializable]
    public sealed class RecordingViewpoint
    {
        [SerializeField]
        private string id = "viewpoint-001";

        [SerializeField]
        private string displayName = "Viewpoint 1";

        [SerializeField]
        [Tooltip("Optional world-space pose marker. It takes precedence over Reference Camera.")]
        private Transform pose;

        [SerializeField]
        [Tooltip("Optional authored camera. Its Transform is used when Pose is empty.")]
        private Camera referenceCamera;

        public RecordingViewpoint()
        {
        }

        public RecordingViewpoint(
            string viewpointId,
            string viewpointDisplayName,
            Transform viewpointPose,
            Camera viewpointCamera = null)
        {
            id = viewpointId;
            displayName = viewpointDisplayName;
            pose = viewpointPose;
            referenceCamera = viewpointCamera;
        }

        public string Id => id ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? Id
            : displayName;
        public Transform Pose => pose != null
            ? pose
            : referenceCamera != null
                ? referenceCamera.transform
                : null;
        public Camera ReferenceCamera => referenceCamera;
    }

    /// <summary>Immutable snapshot emitted after a viewpoint is applied.</summary>
    public readonly struct RecordingViewpointSelection
    {
        public RecordingViewpointSelection(
            int index,
            string id,
            string displayName,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            Index = index;
            Id = id;
            DisplayName = displayName;
            WorldPosition = worldPosition;
            WorldRotation = worldRotation;
        }

        public int Index { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public Vector3 WorldPosition { get; }
        public Quaternion WorldRotation { get; }
    }

    /// <summary>
    /// Switches among scene-authored eye poses without replacing the XR camera.
    /// In a Quest build the reference Camera components are always disabled and
    /// the XR origin is aligned so CenterEyeAnchor reaches the chosen pose.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    public sealed class RecordingViewpointController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField]
        private RecordingCoordinator coordinator;

        [SerializeField]
        private VRPlayerRig playerRig;

        [SerializeField]
        [Tooltip("Fallback XR origin used when no VRPlayerRig is assigned.")]
        private Transform xrOrigin;

        [SerializeField]
        [Tooltip("Fallback tracked head used when no VRPlayerRig is assigned.")]
        private Transform trackedHead;

        [Header("Viewpoints")]
        [SerializeField]
        private RecordingViewpoint[] viewpoints = Array.Empty<RecordingViewpoint>();

        [SerializeField]
        [Min(0)]
        private int initialViewpointIndex;

        [SerializeField]
        private bool applyInitialViewpointOnStart = true;

        [SerializeField]
        private bool wrapAround = true;

        [Header("Recording Safety")]
        [SerializeField]
        [Tooltip("Only allow movement while recording is disconnected, ready, completed, or in error.")]
        private bool requireIdleRecordingState = true;

        [SerializeField]
        [Tooltip("Remember the last request made while busy and apply it once recording becomes idle.")]
        private bool queueRequestWhileBusy = true;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Maximum tracked-head position error allowed when a Take starts.")]
        private float takeStartPositionTolerance = 0.08f;

        [Header("Editor Simulation")]
        [SerializeField]
        [Tooltip("In Play Mode inside the Editor, render through the selected reference Camera.")]
        private bool previewReferenceCameraInEditor = true;

        [SerializeField]
        [Tooltip("Editor only: stop HMD camera rendering while a static preview Camera is active. Tracking remains active.")]
        private bool suppressHmdRenderingDuringEditorPreview = true;

        private int currentIndex = -1;
        private int queuedIndex = -1;
        private bool coordinatorBound;
        private Camera hmdCamera;
        private bool hmdCameraWasEnabled;
        private bool hmdCameraStateCaptured;

        public event Action<RecordingViewpointSelection> ViewpointChanged;
        public event Action<int, string> ViewpointSwitchQueued;
        public event Action<int, string> ViewpointSwitchRejected;

        public IReadOnlyList<RecordingViewpoint> Viewpoints => viewpoints;
        public int ViewpointCount => viewpoints?.Length ?? 0;
        public int CurrentIndex => currentIndex;
        public int QueuedIndex => queuedIndex;
        public bool HasCurrentViewpoint => IsValidIndex(currentIndex);
        public bool HasQueuedViewpoint => IsValidIndex(queuedIndex);
        public RecordingViewpoint CurrentViewpoint => HasCurrentViewpoint
            ? viewpoints[currentIndex]
            : null;
        public string CurrentViewpointId => CurrentViewpoint?.Id ?? string.Empty;
        public bool IsAlignmentPending =>
            playerRig != null && playerRig.IsSpawnAlignmentPending;
        public float TakeStartPositionTolerance =>
            Mathf.Max(0.01f, takeStartPositionTolerance);
        public bool CanSwitchNow => !requireIdleRecordingState ||
                                    coordinator == null ||
                                    IsIdle(coordinator.State);

        private void Awake()
        {
            ResolveDependencies();
            CaptureHmdCameraState();
            DisableReferenceCameras();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            BindCoordinator();
        }

        private void Start()
        {
            // Runtime-created recording dependencies exist by Start even when
            // this controller was authored in the scene before bootstrap ran.
            ResolveDependencies();
            BindCoordinator();
            CaptureHmdCameraState();
            ValidateConfiguration(true);

            if (applyInitialViewpointOnStart &&
                !HasCurrentViewpoint &&
                ViewpointCount > 0)
            {
                TrySelectViewpoint(Mathf.Clamp(
                    initialViewpointIndex,
                    0,
                    ViewpointCount - 1
                ));
            }
            else
            {
                RefreshCameraRendering();
            }
        }

        /// <summary>Supplies runtime dependencies without changing viewpoints.</summary>
        public void Configure(
            RecordingCoordinator recordingCoordinator,
            VRPlayerRig recordingPlayerRig)
        {
            UnbindCoordinator();
            coordinator = recordingCoordinator;
            playerRig = recordingPlayerRig;
            ResolveDependencies();
            BindCoordinator();
            CaptureHmdCameraState();
        }

        /// <summary>Replaces the authored list, primarily for runtime setup.</summary>
        public void ConfigureViewpoints(
            RecordingViewpoint[] configuredViewpoints,
            int configuredInitialIndex = 0)
        {
            viewpoints = configuredViewpoints ?? Array.Empty<RecordingViewpoint>();
            initialViewpointIndex = Mathf.Max(0, configuredInitialIndex);
            currentIndex = -1;
            queuedIndex = -1;
            DisableReferenceCameras();
            ValidateConfiguration(true);
        }

        public bool TrySelectViewpoint(string viewpointId)
        {
            if (string.IsNullOrWhiteSpace(viewpointId))
            {
                Reject(-1, "A viewpoint ID is required.");
                return false;
            }

            for (int index = 0; index < ViewpointCount; index++)
            {
                RecordingViewpoint candidate = viewpoints[index];
                if (candidate != null && string.Equals(
                        candidate.Id,
                        viewpointId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return TrySelectViewpoint(index);
                }
            }

            Reject(-1, $"Viewpoint '{viewpointId}' was not found.");
            return false;
        }

        public bool TrySelectViewpoint(int index)
        {
            if (!TryGetUsableViewpoint(index, out RecordingViewpoint viewpoint,
                    out Transform pose, out string error))
            {
                Reject(index, error);
                return false;
            }

            if (index == currentIndex)
            {
                // A start command repeats the sentence viewpoint. Reapplying
                // the XR origin here would visibly teleport the wearer every
                // time recording starts and can place the eyes inside geometry.
                return true;
            }

            if (!CanSwitchNow)
            {
                if (queueRequestWhileBusy)
                {
                    queuedIndex = index;
                    ViewpointSwitchQueued?.Invoke(index, viewpoint.Id);
                }
                else
                {
                    Reject(index, $"Recording is busy ({coordinator.State}).");
                }
                return false;
            }

            if (!TryAlignTrackedView(
                    pose,
                    viewpoint.ReferenceCamera,
                    out error))
            {
                Reject(index, error);
                return false;
            }

            currentIndex = index;
            queuedIndex = -1;
            RefreshCameraRendering();
            ViewpointChanged?.Invoke(new RecordingViewpointSelection(
                index,
                viewpoint.Id,
                viewpoint.DisplayName,
                pose.position,
                pose.rotation
            ));
            return true;
        }

        public bool TrySelectNext()
        {
            return TrySelectRelative(1);
        }

        public bool TrySelectPrevious()
        {
            return TrySelectRelative(-1);
        }

        public bool ReapplyCurrentViewpoint()
        {
            if (!TryGetUsableViewpoint(currentIndex, out RecordingViewpoint viewpoint,
                    out Transform pose, out string error))
            {
                Reject(currentIndex, error);
                return false;
            }

            if (!CanSwitchNow)
            {
                Reject(currentIndex, $"Recording is busy ({coordinator.State}).");
                return false;
            }

            if (!TryAlignTrackedView(
                    pose,
                    viewpoint.ReferenceCamera,
                    out error))
            {
                Reject(currentIndex, error);
                return false;
            }

            RefreshCameraRendering();
            return true;
        }

        public bool PrepareCurrentViewpointForTake(out string error)
        {
            error = string.Empty;
            if (!HasCurrentViewpoint)
            {
                error = "No recording viewpoint is selected.";
                return false;
            }

            // Viewpoint alignment belongs to sentence/scene selection. A Take
            // only locks the already selected world frame; it must never move
            // the tracked HMD again when the operator presses Start.
            playerRig?.ReassertFixedWorldFrame();
            return true;
        }

        public bool TryValidateCurrentViewpointForTake(out string error)
        {
            error = string.Empty;
            if (!TryGetUsableViewpoint(
                    currentIndex,
                    out _,
                    out Transform pose,
                    out error))
            {
                return false;
            }

            if (IsAlignmentPending)
            {
                error = "固定视角仍在对齐，请稍候";
                return false;
            }

            ResolveDependencies();
            if (playerRig == null)
            {
                error = "XR 固定视角控制器尚未就绪";
                return false;
            }

            playerRig.ReassertFixedWorldFrame();
            if (!playerRig.LastSpawnAlignmentSucceeded ||
                !playerRig.HasFixedRecordingOriginPose)
            {
                error = "固定视角对齐失败，请重新选择当前句";
                return false;
            }
#if !UNITY_EDITOR
            if (!OVRPlugin.positionTracked)
            {
                error = "头显位置追踪已丢失";
                return false;
            }
#endif
            if (trackedHead == null)
            {
                error = "头显追踪尚未就绪";
                return false;
            }

            // The authored pose defines the nominal scene viewpoint, not a
            // head restraint. Natural head motion during signing is valid and
            // is recorded relative to the fixed XR origin.
            return IsFinite(trackedHead.position) && IsFinite(trackedHead.rotation);
        }

        public void SetEditorCameraPreview(bool enabled)
        {
            previewReferenceCameraInEditor = enabled;
            RefreshCameraRendering();
        }

        public bool ValidateConfiguration(bool logProblems = false)
        {
            bool valid = ViewpointCount > 0;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!valid && logProblems)
            {
                Debug.LogWarning(
                    "[RecordingViewpointController] No viewpoints are configured.",
                    this
                );
            }

            for (int index = 0; index < ViewpointCount; index++)
            {
                RecordingViewpoint viewpoint = viewpoints[index];
                string issue = null;
                if (viewpoint == null)
                {
                    issue = "entry is null";
                }
                else if (string.IsNullOrWhiteSpace(viewpoint.Id))
                {
                    issue = "ID is empty";
                }
                else if (!ids.Add(viewpoint.Id))
                {
                    issue = $"ID '{viewpoint.Id}' is duplicated";
                }
                else if (viewpoint.Pose == null)
                {
                    issue = "neither Pose nor Reference Camera is assigned";
                }
                else if (IsInsideTrackedRig(viewpoint.Pose))
                {
                    issue = "pose is inside the tracked rig instead of world space";
                }
                else if (!IsFinite(viewpoint.Pose.position) ||
                         !IsFinite(viewpoint.Pose.rotation))
                {
                    issue = "pose contains a non-finite value";
                }

                if (issue == null)
                {
                    continue;
                }

                valid = false;
                if (logProblems)
                {
                    Debug.LogWarning(
                        $"[RecordingViewpointController] Viewpoint {index}: {issue}.",
                        this
                    );
                }
            }

            return valid;
        }

        private bool TrySelectRelative(int offset)
        {
            if (ViewpointCount == 0)
            {
                Reject(-1, "No viewpoints are configured.");
                return false;
            }

            int baseIndex = HasCurrentViewpoint ? currentIndex : initialViewpointIndex;
            int next = baseIndex + offset;
            if (wrapAround)
            {
                next = (next % ViewpointCount + ViewpointCount) % ViewpointCount;
            }
            else
            {
                next = Mathf.Clamp(next, 0, ViewpointCount - 1);
            }
            return TrySelectViewpoint(next);
        }

        private bool TryGetUsableViewpoint(
            int index,
            out RecordingViewpoint viewpoint,
            out Transform pose,
            out string error)
        {
            viewpoint = null;
            pose = null;
            error = string.Empty;

            if (!IsValidIndex(index))
            {
                error = $"Viewpoint index {index} is outside the configured range.";
                return false;
            }

            viewpoint = viewpoints[index];
            if (viewpoint == null)
            {
                error = $"Viewpoint {index} is null.";
                return false;
            }

            pose = viewpoint.Pose;
            if (pose == null)
            {
                error = $"Viewpoint '{viewpoint.Id}' has no pose source.";
                return false;
            }

            if (IsInsideTrackedRig(pose))
            {
                error = $"Viewpoint '{viewpoint.Id}' must be placed in world space, outside the XR rig.";
                return false;
            }

            if (!IsFinite(pose.position) || !IsFinite(pose.rotation))
            {
                error = $"Viewpoint '{viewpoint.Id}' contains a non-finite pose.";
                return false;
            }

            return true;
        }

        private bool TryAlignTrackedView(
            Transform target,
            Camera editorPreviewCamera,
            out string error)
        {
            error = string.Empty;
            ResolveDependencies();

            if (playerRig != null)
            {
                // VRPlayerRig waits for a usable tracked pose, then applies yaw
                // and position so the physical HMD reaches this world-space pose.
                playerRig.SetSpawnPoint(target, true);
                return true;
            }

            if (xrOrigin == null || trackedHead == null || xrOrigin == trackedHead)
            {
#if UNITY_EDITOR
                if (previewReferenceCameraInEditor &&
                    editorPreviewCamera != null)
                {
                    return true;
                }
#endif
                error = "No VRPlayerRig, or XR origin and tracked head, is available.";
                return false;
            }

            Vector3 currentForward = Vector3.ProjectOnPlane(
                trackedHead.forward,
                Vector3.up
            );
            Vector3 targetForward = Vector3.ProjectOnPlane(
                target.forward,
                Vector3.up
            );
            if (currentForward.sqrMagnitude > 0.000001f &&
                targetForward.sqrMagnitude > 0.000001f)
            {
                float yaw = Vector3.SignedAngle(
                    currentForward,
                    targetForward,
                    Vector3.up
                );
                xrOrigin.RotateAround(trackedHead.position, Vector3.up, yaw);
            }

            xrOrigin.position += target.position - trackedHead.position;
            if (!IsFinite(xrOrigin.position) || !IsFinite(xrOrigin.rotation))
            {
                error = "XR origin alignment produced a non-finite pose.";
                return false;
            }
            return true;
        }

        private void ResolveDependencies()
        {
            if (coordinator == null)
            {
                coordinator = FindInOwnScene<RecordingCoordinator>();
            }

            if (playerRig == null)
            {
                VRPlayerRig instance = VRPlayerRig.Instance;
                playerRig = instance != null &&
                            instance.gameObject.scene == gameObject.scene
                    ? instance
                    : FindInOwnScene<VRPlayerRig>();
            }

            if (playerRig != null)
            {
                xrOrigin = playerRig.XROrigin;
                trackedHead = playerRig.Head;
            }

            if (trackedHead == null)
            {
                foreach (Camera camera in FindAllInOwnScene<Camera>())
                {
                    if (camera.name == "CenterEyeAnchor")
                    {
                        trackedHead = camera.transform;
                        break;
                    }
                }
            }

            if (hmdCamera == null && trackedHead != null)
            {
                hmdCamera = trackedHead.GetComponent<Camera>();
            }
        }

        private void BindCoordinator()
        {
            if (coordinatorBound || coordinator == null)
            {
                return;
            }

            coordinator.PresentationChanged += HandleCoordinatorPresentationChanged;
            coordinatorBound = true;
        }

        private void UnbindCoordinator()
        {
            if (!coordinatorBound || coordinator == null)
            {
                coordinatorBound = false;
                return;
            }

            coordinator.PresentationChanged -= HandleCoordinatorPresentationChanged;
            coordinatorBound = false;
        }

        private void HandleCoordinatorPresentationChanged()
        {
            if (!HasQueuedViewpoint || !CanSwitchNow)
            {
                return;
            }

            int request = queuedIndex;
            queuedIndex = -1;
            TrySelectViewpoint(request);
        }

        private void DisableReferenceCameras()
        {
            for (int index = 0; index < ViewpointCount; index++)
            {
                Camera camera = viewpoints[index]?.ReferenceCamera;
                if (camera == null || IsHmdCamera(camera))
                {
                    continue;
                }

                camera.enabled = false;
                if (camera.TryGetComponent(out AudioListener listener))
                {
                    listener.enabled = false;
                }
            }
        }

        private void RefreshCameraRendering()
        {
            DisableReferenceCameras();

#if UNITY_EDITOR
            Camera preview = HasCurrentViewpoint
                ? CurrentViewpoint.ReferenceCamera
                : null;
            bool previewActive = Application.isPlaying &&
                                 previewReferenceCameraInEditor &&
                                 preview != null &&
                                 preview.gameObject.activeInHierarchy &&
                                 !IsHmdCamera(preview);
            if (previewActive)
            {
                preview.enabled = true;
            }

            RestoreOrSuppressHmdCamera(
                previewActive && suppressHmdRenderingDuringEditorPreview
            );
#else
            RestoreOrSuppressHmdCamera(false);
#endif
        }

        private void CaptureHmdCameraState()
        {
            if (hmdCameraStateCaptured || hmdCamera == null)
            {
                return;
            }

            hmdCameraWasEnabled = hmdCamera.enabled;
            hmdCameraStateCaptured = true;
        }

        private void RestoreOrSuppressHmdCamera(bool suppress)
        {
            if (hmdCamera == null)
            {
                return;
            }

            CaptureHmdCameraState();
            hmdCamera.enabled = suppress ? false : hmdCameraWasEnabled;
        }

        private bool IsInsideTrackedRig(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (xrOrigin != null &&
                (candidate == xrOrigin || candidate.IsChildOf(xrOrigin)))
            {
                return true;
            }

            return playerRig != null &&
                   (candidate == playerRig.transform ||
                    candidate.IsChildOf(playerRig.transform));
        }

        private bool IsHmdCamera(Camera candidate)
        {
            return candidate != null &&
                   (candidate == hmdCamera ||
                    (trackedHead != null && candidate.transform == trackedHead));
        }

        private bool IsValidIndex(int index)
        {
            return viewpoints != null && index >= 0 && index < viewpoints.Length;
        }

        private static bool IsIdle(RecordingFlowState state)
        {
            return state == RecordingFlowState.Disconnected ||
                   state == RecordingFlowState.Ready ||
                   state == RecordingFlowState.Completed ||
                   state == RecordingFlowState.Error;
        }

        private void Reject(int index, string reason)
        {
            Debug.LogWarning(
                $"[RecordingViewpointController] {reason}",
                this
            );
            ViewpointSwitchRejected?.Invoke(index, reason);
        }

        private T FindInOwnScene<T>() where T : Component
        {
            T[] candidates = FindObjectsByType<T>(FindObjectsInactive.Include);
            foreach (T candidate in candidates)
            {
                if (candidate.gameObject.scene == gameObject.scene)
                {
                    return candidate;
                }
            }
            return null;
        }

        private T[] FindAllInOwnScene<T>() where T : Component
        {
            T[] candidates = FindObjectsByType<T>(FindObjectsInactive.Include);
            var matches = new List<T>();
            foreach (T candidate in candidates)
            {
                if (candidate.gameObject.scene == gameObject.scene)
                {
                    matches.Add(candidate);
                }
            }
            return matches.ToArray();
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z) &&
                   float.IsFinite(value.w);
        }

        private void OnDisable()
        {
            UnbindCoordinator();
            DisableReferenceCameras();
            RestoreOrSuppressHmdCamera(false);
        }

        private void OnDestroy()
        {
            UnbindCoordinator();
            RestoreOrSuppressHmdCamera(false);
        }

        private void OnValidate()
        {
            initialViewpointIndex = Mathf.Max(0, initialViewpointIndex);
            takeStartPositionTolerance = Mathf.Max(
                0.01f,
                takeStartPositionTolerance
            );
        }

        private void OnDrawGizmosSelected()
        {
            if (viewpoints == null)
            {
                return;
            }

            for (int index = 0; index < viewpoints.Length; index++)
            {
                Transform pose = viewpoints[index]?.Pose;
                if (pose == null)
                {
                    continue;
                }

                Gizmos.color = index == currentIndex
                    ? new Color(0.1f, 0.85f, 0.45f, 1f)
                    : new Color(0.2f, 0.65f, 1f, 0.8f);
                Gizmos.DrawWireSphere(pose.position, 0.06f);
                Gizmos.DrawLine(
                    pose.position,
                    pose.position + pose.forward * 0.35f
                );
            }
        }
    }
}
