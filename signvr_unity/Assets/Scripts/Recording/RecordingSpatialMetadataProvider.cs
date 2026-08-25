using System;
using UnityEngine;

namespace SignVR.Recording
{
    [Serializable]
    public struct RecordingVector3Record
    {
        public float x;
        public float y;
        public float z;

        public RecordingVector3Record(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }
    }

    [Serializable]
    public struct RecordingQuaternionRecord
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public RecordingQuaternionRecord(Quaternion value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }
    }

    /// <summary>
    /// World-space context captured when a Take starts. Runtime correction
    /// counters are refreshed when the Take is finalized. Public fields are
    /// intentional so Unity's JsonUtility can persist the snapshot directly.
    /// </summary>
    [Serializable]
    public sealed class RecordingSpatialSnapshot
    {
        public bool valid;
        public string captured_utc;
        public string world_frame_id;

        public bool has_viewpoint;
        public string viewpoint_id;
        public int viewpoint_index;
        public string viewpoint_name;
        public RecordingVector3Record viewpoint_world_position;
        public RecordingQuaternionRecord viewpoint_world_rotation;
        public bool viewpoint_position_aligned;
        public float viewpoint_position_error_meters;
        public float viewpoint_position_tolerance_meters;

        public bool has_xr_origin;
        public RecordingVector3Record xr_origin_world_position;
        public RecordingQuaternionRecord xr_origin_world_rotation;
        public bool xr_origin_world_frame_locked;
        public int xr_origin_correction_count;

        public bool has_tracked_head;
        public bool position_tracking_valid;
        public RecordingVector3Record tracked_head_world_position;
        public RecordingQuaternionRecord tracked_head_world_rotation;

        public bool has_floor;
        public float floor_world_y;
        public string tracking_origin;
        public bool editor_simulation;

        public static RecordingSpatialSnapshot CreateUnavailable(
            string worldFrameId)
        {
            return new RecordingSpatialSnapshot
            {
                valid = false,
                captured_utc = DateTime.UtcNow.ToString("O"),
                world_frame_id = worldFrameId ?? string.Empty,
                viewpoint_index = -1,
                viewpoint_position_error_meters = -1f,
                tracking_origin = "Unknown",
                editor_simulation = Application.isEditor
            };
        }
    }

    /// <summary>
    /// Resolves the selected authored viewpoint and the live XR transforms into
    /// a stable per-Take metadata snapshot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RecordingSpatialMetadataProvider : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField]
        private RecordingViewpointController viewpointController;

        [SerializeField]
        private VRPlayerRig playerRig;

        [SerializeField]
        [Tooltip("Optional override. By default the floor assigned to VRPlayerRig is used.")]
        private Collider floorCollider;

        [Header("World Frame")]
        [SerializeField]
        [Tooltip("Version this whenever the authored room coordinate system changes.")]
        private string worldFrameId = "VRroom-world-v1";

        public RecordingViewpointController ViewpointController =>
            viewpointController;
        public VRPlayerRig PlayerRig => playerRig;
        public Collider FloorCollider => ResolveFloorCollider();
        public string WorldFrameId => ResolveWorldFrameId();

        private void Awake()
        {
            ResolveDependencies();
        }

        public void Configure(
            RecordingViewpointController configuredViewpointController,
            VRPlayerRig configuredPlayerRig,
            Collider configuredFloorCollider = null,
            string configuredWorldFrameId = null)
        {
            viewpointController = configuredViewpointController;
            playerRig = configuredPlayerRig;
            floorCollider = configuredFloorCollider;

            if (!string.IsNullOrWhiteSpace(configuredWorldFrameId))
            {
                worldFrameId = configuredWorldFrameId.Trim();
            }

            ResolveDependencies();
        }

        public RecordingSpatialSnapshot CaptureSnapshot()
        {
            ResolveDependencies();
            playerRig?.ReassertFixedWorldFrame();

            RecordingSpatialSnapshot snapshot =
                RecordingSpatialSnapshot.CreateUnavailable(
                    ResolveWorldFrameId()
                );

            CaptureViewpoint(snapshot);

            Transform xrOrigin = playerRig != null
                ? playerRig.XROrigin
                : null;
            if (xrOrigin != null)
            {
                snapshot.has_xr_origin = true;
                snapshot.xr_origin_world_position =
                    new RecordingVector3Record(xrOrigin.position);
                snapshot.xr_origin_world_rotation =
                    new RecordingQuaternionRecord(xrOrigin.rotation);
                snapshot.xr_origin_world_frame_locked =
                    playerRig == null || playerRig.HasFixedRecordingOriginPose;
                snapshot.xr_origin_correction_count = playerRig != null
                    ? playerRig.RecordingOriginCorrectionCount
                    : 0;
            }

            Transform trackedHead = playerRig != null
                ? playerRig.Head
                : null;
            if (trackedHead != null)
            {
                snapshot.has_tracked_head = true;
                snapshot.tracked_head_world_position =
                    new RecordingVector3Record(trackedHead.position);
                snapshot.tracked_head_world_rotation =
                    new RecordingQuaternionRecord(trackedHead.rotation);
            }

            Collider resolvedFloor = ResolveFloorCollider();
            if (resolvedFloor != null)
            {
                Physics.SyncTransforms();
                snapshot.has_floor = true;
                snapshot.floor_world_y = resolvedFloor.bounds.max.y;
            }

            OVRManager manager = OVRManager.instance;
            snapshot.tracking_origin = manager != null
                ? manager.trackingOriginType.ToString()
                : "Unknown";
            snapshot.position_tracking_valid =
                Application.isEditor || OVRPlugin.positionTracked;
            ValidateViewpointAlignment(snapshot, trackedHead);
            snapshot.valid = snapshot.has_viewpoint &&
                             snapshot.has_xr_origin &&
                             snapshot.has_tracked_head &&
                             snapshot.position_tracking_valid &&
                             (Application.isEditor ||
                              snapshot.xr_origin_world_frame_locked);
            return snapshot;
        }

        public bool TryCaptureValidatedSnapshot(
            out RecordingSpatialSnapshot snapshot,
            out string error)
        {
            snapshot = CaptureSnapshot();
            if (snapshot.valid)
            {
                error = string.Empty;
                return true;
            }

            if (!snapshot.has_viewpoint)
            {
                error = "尚未选择固定录制视角";
            }
            else if (!snapshot.has_xr_origin || !snapshot.has_tracked_head)
            {
                error = "XR 原点或头显追踪尚未就绪";
            }
            else if (!snapshot.position_tracking_valid)
            {
                error = "头显位置追踪已丢失";
            }
            else if (!Application.isEditor &&
                     !snapshot.xr_origin_world_frame_locked)
            {
                error = "XR 世界坐标基准发生变化，请重新对齐";
            }
            else if (viewpointController != null &&
                     viewpointController.IsAlignmentPending)
            {
                error = "固定视角仍在对齐，请稍候";
            }
            else
            {
                error = "固定录制坐标尚未就绪";
            }
            return false;
        }

        /// <summary>
        /// Refreshes values that can change while recording without replacing
        /// the start-of-Take viewpoint and tracked-head pose.
        /// </summary>
        public void RefreshFinalState(RecordingSpatialSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            ResolveDependencies();
            if (playerRig == null)
            {
                return;
            }

            playerRig.ReassertFixedWorldFrame();
            snapshot.xr_origin_world_frame_locked =
                playerRig.HasFixedRecordingOriginPose;
            snapshot.xr_origin_correction_count =
                playerRig.RecordingOriginCorrectionCount;
        }

        private void ValidateViewpointAlignment(
            RecordingSpatialSnapshot snapshot,
            Transform trackedHead)
        {
            float tolerance = viewpointController != null
                ? viewpointController.TakeStartPositionTolerance
                : 0.08f;
            snapshot.viewpoint_position_tolerance_meters = tolerance;
            snapshot.viewpoint_position_error_meters = -1f;
            snapshot.viewpoint_position_aligned = false;

            Transform authoredPose = viewpointController?
                .CurrentViewpoint?
                .Pose;
            if (authoredPose == null || trackedHead == null)
            {
                return;
            }

            float error = Vector3.Distance(
                authoredPose.position,
                trackedHead.position
            );
            snapshot.viewpoint_position_error_meters = error;
            snapshot.viewpoint_position_aligned =
                float.IsFinite(error) &&
                error <= tolerance &&
                (viewpointController == null ||
                 !viewpointController.IsAlignmentPending);
        }

        private void CaptureViewpoint(RecordingSpatialSnapshot snapshot)
        {
            if (viewpointController == null ||
                !viewpointController.HasCurrentViewpoint)
            {
                return;
            }

            RecordingViewpoint viewpoint =
                viewpointController.CurrentViewpoint;
            Transform authoredPose = viewpoint?.Pose;
            if (viewpoint == null || authoredPose == null)
            {
                return;
            }

            snapshot.has_viewpoint = true;
            snapshot.viewpoint_id = viewpoint.Id;
            snapshot.viewpoint_index = viewpointController.CurrentIndex;
            snapshot.viewpoint_name = viewpoint.DisplayName;
            snapshot.viewpoint_world_position =
                new RecordingVector3Record(authoredPose.position);
            snapshot.viewpoint_world_rotation =
                new RecordingQuaternionRecord(authoredPose.rotation);
        }

        private void ResolveDependencies()
        {
            if (viewpointController == null)
            {
                viewpointController =
                    GetComponent<RecordingViewpointController>() ??
                    FindAnyObjectByType<RecordingViewpointController>(
                        FindObjectsInactive.Include
                    );
            }

            if (playerRig == null)
            {
                playerRig = VRPlayerRig.Instance ??
                            FindAnyObjectByType<VRPlayerRig>(
                                FindObjectsInactive.Include
                            );
            }

            if (floorCollider == null && playerRig != null)
            {
                floorCollider = playerRig.FloorCollider;
            }
        }

        private Collider ResolveFloorCollider()
        {
            if (floorCollider != null)
            {
                return floorCollider;
            }

            return playerRig != null
                ? playerRig.FloorCollider
                : null;
        }

        private string ResolveWorldFrameId()
        {
            if (!string.IsNullOrWhiteSpace(worldFrameId))
            {
                return worldFrameId.Trim();
            }

            string sceneName = gameObject.scene.IsValid()
                ? gameObject.scene.name
                : "unknown-scene";
            return sceneName + "-world-v1";
        }
    }
}
