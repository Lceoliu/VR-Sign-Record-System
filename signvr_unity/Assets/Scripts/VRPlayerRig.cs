using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Runtime player root for room-scale VR. The XR camera/hand rig remains a
/// child of this object, while the CharacterController prevents the virtual
/// player from walking through scene colliders.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class VRPlayerRig : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;
    [SerializeField] private Transform head;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Collider floorCollider;

    [Header("Locomotion")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 1.8f;
    [SerializeField, Min(0.1f)] private float gravity = 9.81f;
    [SerializeField, Range(0f, 0.49f)] private float deadZone = 0.15f;
    [SerializeField] private bool useLeftThumbstick = true;
    [SerializeField] private bool allowKeyboardFallback = true;
    [SerializeField, Min(0.1f)] private float maxTrackedStepDistance = 0.5f;
    [SerializeField, Min(0.5f)] private float fallRecoveryDistance = 1.5f;
    [SerializeField, Min(1f)] private float spawnAlignmentTimeout = 5f;
    [SerializeField, Min(0.005f)] private float spawnAlignmentPositionTolerance = 0.03f;

    [Header("Body")]
    [SerializeField, Min(0.05f)] private float bodyRadius = 0.23f;
    [SerializeField, Min(0.5f)] private float bodyHeight = 1.75f;
    [SerializeField, Min(0.05f)] private float skinWidth = 0.03f;

    [Header("Recording")]
    [SerializeField] private bool recordingMode;
    [SerializeField]
    [Tooltip(
        "Keep the Meta/OpenXR tracking origin runtime-owned while the player " +
        "root remains fixed. Enable this for live Quest study scenes to avoid " +
        "competing floor-height writes."
    )]
    private bool preserveRuntimeTrackingOrigin;

    private CharacterController characterController;
    private float verticalVelocity;
    private bool initialized;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Vector3 spawnViewPosition;
    private Quaternion spawnViewRotation;
    private Vector3 xrOriginBaseLocalPosition;
    private Quaternion xrOriginBaseLocalRotation;
    private bool hasXrOriginBasePose;
    private bool spawnAlignmentPending;
    private float spawnAlignmentDeadline;
    private int stableTrackedPoseFrames;
    private int stableAlignedPoseFrames;
    private bool spawnAlignmentApplied;
    private Vector3 previousTrackedHeadLocalPosition;
    private bool recordingControllerStateCaptured;
    private bool controllerEnabledBeforeRecording;
    private bool controllerDetectCollisionsBeforeRecording;
    private bool hasFixedRecordingOriginPose;
    private Vector3 fixedRecordingOriginLocalPosition;
    private Quaternion fixedRecordingOriginLocalRotation;
    private int recordingOriginCorrectionCount;

    private const int RequiredStableTrackedPoseFrames = 5;

    public static VRPlayerRig Instance { get; private set; }
    public Transform XROrigin => xrOrigin;
    public Transform Head => head;
    public Transform SpawnPoint => spawnPoint;
    public Collider FloorCollider => floorCollider;
    public Vector3 SpawnPosition => spawnPosition;
    public Quaternion SpawnRotation => spawnRotation;
    public Vector3 SpawnViewPosition => spawnViewPosition;
    public Quaternion SpawnViewRotation => spawnViewRotation;
    public bool IsInitialized => initialized;
    public bool IsRecordingMode => recordingMode;
    public bool PreservesRuntimeTrackingOrigin =>
        preserveRuntimeTrackingOrigin;
    public bool IsSpawnAlignmentPending => spawnAlignmentPending;
    public bool LastSpawnAlignmentSucceeded { get; private set; }
    public float SpawnAlignmentPositionError { get; private set; } =
        float.PositiveInfinity;
    public bool HasFixedRecordingOriginPose => hasFixedRecordingOriginPose;
    public int RecordingOriginCorrectionCount => recordingOriginCorrectionCount;
    public event Action<Vector3> BeforeMove;
    public event Action<Vector3> AfterMove;
    public event Action<Vector3, Quaternion> Spawned;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        bool startInRecordingMode = recordingMode;
        recordingMode = false;
        characterController = GetComponent<CharacterController>();
        ConfigureController();
        ResolveReferences();
        CaptureXrOriginBasePose();
        CaptureSpawnPose();
        if (startInRecordingMode)
        {
            SetRecordingMode(true);
        }
    }

    private IEnumerator Start()
    {
        ApplySpawnPose();
        yield return new WaitForSeconds(1f);
        LogRuntimeView();
        yield return new WaitForSeconds(4f);
        LogRuntimeView();
    }

    private void LogRuntimeView()
    {
        Camera camera = head != null ? head.GetComponent<Camera>() : null;
        Vector3 eyePosition = head != null ? head.position : transform.position;
        Vector3 eyeForward = head != null ? head.forward : transform.forward;
        float floorTop = floorCollider != null
            ? floorCollider.bounds.max.y
            : float.NaN;
        bool grounded = characterController != null &&
                        characterController.enabled &&
                        characterController.isGrounded;
        int visibleRenderers = 0;
        if (camera != null)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            foreach (Renderer renderer in FindObjectsByType<Renderer>(
                         FindObjectsInactive.Exclude))
            {
                if (renderer.enabled &&
                    GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                {
                    visibleRenderers++;
                }
            }
        }

        Debug.Log(
            $"[SignVR] Runtime view ready: scene={gameObject.scene.name}, " +
            $"player={transform.position:F3}, eye={eyePosition:F3}, " +
            $"spawnEye={spawnViewPosition:F3}, " +
            $"forward={eyeForward:F3}, visibleRenderers={visibleRenderers}, " +
            $"grounded={grounded}, " +
            $"recordingMode={recordingMode}, " +
            $"preserveRuntimeTrackingOrigin=" +
            $"{preserveRuntimeTrackingOrigin}, " +
            $"floorTop={floorTop:F3}, verticalVelocity={verticalVelocity:F3}."
        );
    }

    private void Update()
    {
        if (!initialized || characterController == null ||
            !characterController.enabled || recordingMode)
        {
            return;
        }

        if (transform.position.y < spawnPosition.y - fallRecoveryDistance)
        {
            Debug.LogWarning(
                $"[SignVR] Player left the walkable floor at " +
                $"{transform.position:F3}; returning to the fixed spawn."
            );
            ApplySpawnPose();
            return;
        }

        if (spawnAlignmentPending)
        {
            return;
        }

        UpdateControllerShape();
        SyncRoomScaleOffset();
        Vector2 input = ReadMoveInput();
        Vector3 planar = GetPlanarDirection(input) * moveSpeed;

        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity -= gravity * Time.deltaTime;
        Vector3 motion = (planar + Vector3.up * verticalVelocity) * Time.deltaTime;
        BeforeMove?.Invoke(motion);
        CollisionFlags flags = characterController.Move(motion);
        if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        AfterMove?.Invoke(motion);
    }

    private void LateUpdate()
    {
        ReassertFixedWorldFrame();

        if (!spawnAlignmentPending || head == null || xrOrigin == null)
        {
            return;
        }

        bool timedOut = Time.unscaledTime >= spawnAlignmentDeadline;
        bool positionTracked = OVRPlugin.positionTracked;
        Vector3 trackedHeadLocal = xrOrigin.InverseTransformPoint(head.position);
        bool usablePose = IsFinite(trackedHeadLocal) &&
                          (Application.isEditor || trackedHeadLocal.y > 0.2f);

        if (!positionTracked && !Application.isEditor && !timedOut)
        {
            return;
        }

        if (!usablePose && !timedOut)
        {
            return;
        }

        if (positionTracked && usablePose)
        {
            if (stableTrackedPoseFrames > 0 &&
                Vector3.Distance(
                    trackedHeadLocal,
                    previousTrackedHeadLocalPosition
                ) <= 0.05f)
            {
                stableTrackedPoseFrames++;
            }
            else
            {
                stableTrackedPoseFrames = 1;
            }

            previousTrackedHeadLocalPosition = trackedHeadLocal;
            if (stableTrackedPoseFrames < RequiredStableTrackedPoseFrames)
            {
                return;
            }
        }
        else if (!Application.isEditor)
        {
            Debug.LogWarning(
                "[SignVR] HMD tracking was not ready before the spawn " +
                "alignment timeout; using the latest available eye pose."
            );
        }

        float tolerance = Mathf.Max(0.005f, spawnAlignmentPositionTolerance);
        float positionError = Vector3.Distance(
            head.position,
            spawnViewPosition
        );
        if (!spawnAlignmentApplied || positionError > tolerance)
        {
            AlignTrackedHeadToSpawnView();
            spawnAlignmentApplied = true;
            stableAlignedPoseFrames = 0;
            positionError = Vector3.Distance(
                head.position,
                spawnViewPosition
            );
        }
        else
        {
            stableAlignedPoseFrames++;
        }

        SpawnAlignmentPositionError = positionError;
        bool aligned = positionError <= tolerance;
        if (!timedOut &&
            (!aligned || stableAlignedPoseFrames < RequiredStableTrackedPoseFrames))
        {
            return;
        }

        LastSpawnAlignmentSucceeded = aligned;
        spawnAlignmentPending = false;
        if (aligned)
        {
            CaptureFixedRecordingOriginPose();
        }
        if (!aligned)
        {
            Debug.LogError(
                "[SignVR] Could not align the tracked head to the authored " +
                $"viewpoint. Remaining error: {positionError:F3} m."
            );
        }
    }

    private void EnforceFixedRecordingRoot()
    {
        bool positionChanged =
            (transform.position - spawnPosition).sqrMagnitude > 0.000001f;
        bool rotationChanged =
            Quaternion.Angle(transform.rotation, spawnRotation) > 0.01f;
        if (positionChanged || rotationChanged)
        {
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }
    }

    /// <summary>
    /// Keeps the authored room frame stable even if the XR runtime updates its
    /// floor-height calibration after a viewpoint has already been applied.
    /// Tracked head and hand motion remain free inside this fixed root frame.
    /// </summary>
    public void ReassertFixedWorldFrame()
    {
        if (!recordingMode || !initialized)
        {
            return;
        }

        EnforceFixedRecordingRoot();

        // Meta/OpenXR owns live tracking-space calibration. InteractionLab
        // freezes locomotion at the player root, but must not overwrite the
        // origin again in LateUpdate: the runtime updates it later in the XR
        // frame and the two writers otherwise render alternating eye heights.
        if (preserveRuntimeTrackingOrigin)
        {
            return;
        }

        // The OVR runtime can rewrite the tracking-space origin when floor
        // calibration or recentering changes. Restore only the origin pose;
        // CenterEyeAnchor and hand anchors remain free to move inside it.
        if (spawnAlignmentPending ||
            !hasFixedRecordingOriginPose ||
            xrOrigin == null)
        {
            return;
        }

        bool positionChanged =
            (xrOrigin.localPosition - fixedRecordingOriginLocalPosition)
            .sqrMagnitude > 0.000001f;
        bool rotationChanged =
            Quaternion.Angle(
                xrOrigin.localRotation,
                fixedRecordingOriginLocalRotation
            ) > 0.01f;
        if (positionChanged || rotationChanged)
        {
            xrOrigin.SetLocalPositionAndRotation(
                fixedRecordingOriginLocalPosition,
                fixedRecordingOriginLocalRotation
            );
            recordingOriginCorrectionCount++;
        }
    }

    /// <summary>Captures the authored spawn marker, or the current player pose.</summary>
    public void CaptureSpawnPose()
    {
        if (spawnPoint != null)
        {
            spawnViewPosition = spawnPoint.position;
            spawnViewRotation = spawnPoint.rotation;
        }
        else
        {
            spawnViewPosition = head != null
                ? head.position
                : transform.position + Vector3.up * bodyHeight;
            spawnViewRotation = head != null
                ? head.rotation
                : transform.rotation;
        }

        Vector3 planarForward = Vector3.ProjectOnPlane(
            spawnViewRotation * Vector3.forward,
            Vector3.up
        );
        spawnRotation = planarForward.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(planarForward.normalized, Vector3.up)
            : transform.rotation;
        spawnPosition = spawnViewPosition;
        GroundSpawnPosition();

        initialized = true;
    }

    /// <summary>Moves the complete XR rig back to the explicit spawn marker.</summary>
    public void ApplySpawnPose()
    {
        if (!initialized)
        {
            CaptureSpawnPose();
        }

        characterController ??= GetComponent<CharacterController>();
        bool controllerWasEnabled =
            characterController != null && characterController.enabled;
        if (controllerWasEnabled)
        {
            characterController.enabled = false;
        }
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        RestoreXrOriginBasePose();
        if (characterController != null)
        {
            characterController.enabled = controllerWasEnabled;
        }
        verticalVelocity = 0f;
        spawnAlignmentPending = true;
        spawnAlignmentDeadline = Time.unscaledTime + spawnAlignmentTimeout;
        stableTrackedPoseFrames = 0;
        stableAlignedPoseFrames = 0;
        spawnAlignmentApplied = false;
        hasFixedRecordingOriginPose = false;
        recordingOriginCorrectionCount = 0;
        LastSpawnAlignmentSucceeded = false;
        SpawnAlignmentPositionError = float.PositiveInfinity;
        previousTrackedHeadLocalPosition = Vector3.zero;
        Spawned?.Invoke(spawnPosition, spawnRotation);
    }

    public void Respawn()
    {
        ApplySpawnPose();
    }

    public void SetSpawnPoint(Transform newSpawnPoint, bool applyImmediately = false)
    {
        spawnPoint = newSpawnPoint;
        CaptureSpawnPose();
        if (applyImmediately)
        {
            ApplySpawnPose();
        }
    }

    /// <summary>
    /// Freezes player-root locomotion and collision response while leaving XR
    /// tracking and pending head-to-viewpoint alignment active.
    /// </summary>
    public void SetRecordingMode(bool enabled)
    {
        if (recordingMode == enabled)
        {
            return;
        }

        characterController ??= GetComponent<CharacterController>();
        recordingMode = enabled;
        verticalVelocity = 0f;

        if (!enabled)
        {
            hasFixedRecordingOriginPose = false;
        }

        if (characterController == null)
        {
            return;
        }

        if (enabled)
        {
            controllerEnabledBeforeRecording = characterController.enabled;
            controllerDetectCollisionsBeforeRecording =
                characterController.detectCollisions;
            recordingControllerStateCaptured = true;
            characterController.detectCollisions = false;
            characterController.enabled = false;
            return;
        }

        if (!recordingControllerStateCaptured)
        {
            return;
        }

        characterController.detectCollisions =
            controllerDetectCollisionsBeforeRecording;
        characterController.enabled = controllerEnabledBeforeRecording;
        recordingControllerStateCaptured = false;
    }

    /// <summary>
    /// Sets scene-authored references and writes the controller defaults so the
    /// scene is usable before the first runtime frame as well as after Awake.
    /// </summary>
    public void ConfigureSceneReferences(
        Transform origin,
        Transform eyes,
        Collider walkableFloor = null)
    {
        xrOrigin = origin;
        head = eyes;
        floorCollider = walkableFloor;
        CaptureXrOriginBasePose();
        characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            ConfigureController();
        }
    }

    private void ResolveReferences()
    {
        if (xrOrigin == null)
        {
            xrOrigin = transform.Find("[BuildingBlock] Camera Rig") ??
                        transform.Find("OVRCameraRig") ??
                        GetComponentInChildren<Camera>(true)?.transform.parent;
        }

        if (head == null)
        {
            head = GetComponentInChildren<Camera>(true)?.transform;
        }
    }

    private void ConfigureController()
    {
        characterController.radius = bodyRadius;
        characterController.height = bodyHeight;
        characterController.center = Vector3.up * (bodyHeight * 0.5f);
        characterController.skinWidth = skinWidth;
        characterController.minMoveDistance = 0f;
        characterController.slopeLimit = 45f;
        characterController.stepOffset = 0.3f;
        characterController.detectCollisions = !recordingMode;
    }

    private void UpdateControllerShape()
    {
        if (head == null)
        {
            return;
        }

        float headLocalY = transform.InverseTransformPoint(head.position).y;
        float desiredHeight = Mathf.Clamp(headLocalY, 1.1f, 2.2f);
        characterController.height = Mathf.Max(desiredHeight, bodyRadius * 2f + 0.05f);
        characterController.center = Vector3.up * (characterController.height * 0.5f);
    }

    private Vector2 ReadMoveInput()
    {
        Vector2 value = Vector2.zero;
        OVRInput.Controller controller = useLeftThumbstick
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;
        value = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
        if (value.sqrMagnitude < deadZone * deadZone && allowKeyboardFallback)
        {
            value = ReadKeyboardInput();
        }
        return value.sqrMagnitude > 1f ? value.normalized : value;
    }

    private static Vector2 ReadKeyboardInput()
    {
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Keyboard keyboard =
            UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float horizontal =
            (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float vertical =
            (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
            (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
        return new Vector2(horizontal, vertical);
#else
        return Vector2.zero;
#endif
    }

    private void SyncRoomScaleOffset()
    {
        if (head == null || xrOrigin == null)
        {
            return;
        }

        Vector3 localHead = transform.InverseTransformPoint(head.position);
        Vector3 localOffset = new Vector3(localHead.x, 0f, localHead.z);
        if (localOffset.sqrMagnitude < 0.000001f)
        {
            return;
        }

        if (localOffset.sqrMagnitude >
            maxTrackedStepDistance * maxTrackedStepDistance)
        {
            CalibrateTrackingSpaceToPlayer();
            return;
        }

        Vector3 before = transform.position;
        Vector3 requestedWorldOffset = transform.TransformVector(localOffset);
        characterController.Move(requestedWorldOffset);
        Vector3 appliedWorldOffset = transform.position - before;

        // Keep the tracked head at its real-world pose while the collision
        // capsule follows room-scale movement. Any blocked remainder stays in
        // the tracking rig; hand penetration is handled by the hand limiter.
        xrOrigin.position -= appliedWorldOffset;
    }

    private void CalibrateTrackingSpaceToPlayer()
    {
        if (head == null || xrOrigin == null)
        {
            return;
        }

        Vector3 localHead = transform.InverseTransformPoint(head.position);
        if (!IsFinite(localHead))
        {
            return;
        }

        Vector3 horizontalOffset = new Vector3(localHead.x, 0f, localHead.z);
        xrOrigin.position -= transform.TransformVector(horizontalOffset);
    }

    private void AlignTrackedHeadToSpawnView()
    {
        if (head == null || xrOrigin == null ||
            !IsFinite(head.position) || !IsFinite(spawnViewPosition))
        {
            return;
        }

        Vector3 currentForward = Vector3.ProjectOnPlane(
            head.forward,
            Vector3.up
        );
        Vector3 targetForward = Vector3.ProjectOnPlane(
            spawnViewRotation * Vector3.forward,
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
            xrOrigin.RotateAround(head.position, Vector3.up, yaw);
        }

        xrOrigin.position += spawnViewPosition - head.position;
    }

    private void CaptureXrOriginBasePose()
    {
        if (xrOrigin == null || hasXrOriginBasePose)
        {
            return;
        }

        xrOriginBaseLocalPosition = xrOrigin.localPosition;
        xrOriginBaseLocalRotation = xrOrigin.localRotation;
        hasXrOriginBasePose = true;
    }

    private void RestoreXrOriginBasePose()
    {
        if (xrOrigin == null || !hasXrOriginBasePose)
        {
            return;
        }

        xrOrigin.SetLocalPositionAndRotation(
            xrOriginBaseLocalPosition,
            xrOriginBaseLocalRotation
        );
    }

    private void CaptureFixedRecordingOriginPose()
    {
        if (xrOrigin == null)
        {
            hasFixedRecordingOriginPose = false;
            return;
        }

        fixedRecordingOriginLocalPosition = xrOrigin.localPosition;
        fixedRecordingOriginLocalRotation = xrOrigin.localRotation;
        hasFixedRecordingOriginPose = true;
    }

    private void GroundSpawnPosition()
    {
        if (floorCollider == null || !floorCollider.enabled ||
            floorCollider.isTrigger)
        {
            return;
        }

        Physics.SyncTransforms();
        Bounds bounds = floorCollider.bounds;
        Vector3 rayOrigin = new Vector3(
            spawnPosition.x,
            bounds.max.y + 2f,
            spawnPosition.z
        );
        if (floorCollider.Raycast(
                new Ray(rayOrigin, Vector3.down),
                out RaycastHit hit,
                bounds.size.y + 4f))
        {
            spawnPosition.y = hit.point.y + skinWidth;
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }

    private Vector3 GetPlanarDirection(Vector2 input)
    {
        Transform reference = head != null ? head : transform;
        Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
        return (right * input.x + forward * input.y);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
