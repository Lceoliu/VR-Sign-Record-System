using System;
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

    [Header("Locomotion")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 1.8f;
    [SerializeField, Min(0.1f)] private float gravity = 9.81f;
    [SerializeField, Range(0f, 0.49f)] private float deadZone = 0.15f;
    [SerializeField] private bool useLeftThumbstick = true;
    [SerializeField] private bool allowKeyboardFallback = true;

    [Header("Body")]
    [SerializeField, Min(0.05f)] private float bodyRadius = 0.23f;
    [SerializeField, Min(0.5f)] private float bodyHeight = 1.75f;
    [SerializeField, Min(0.05f)] private float skinWidth = 0.03f;

    private CharacterController characterController;
    private float verticalVelocity;
    private bool initialized;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    public static VRPlayerRig Instance { get; private set; }
    public Transform XROrigin => xrOrigin;
    public Transform Head => head;
    public Transform SpawnPoint => spawnPoint;
    public Vector3 SpawnPosition => spawnPosition;
    public Quaternion SpawnRotation => spawnRotation;
    public bool IsInitialized => initialized;
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
        characterController = GetComponent<CharacterController>();
        ConfigureController();
        ResolveReferences();
        CaptureSpawnPose();
    }

    private void Start()
    {
        ApplySpawnPose();
        Invoke(nameof(LogRuntimeView), 1f);
    }

    private void LogRuntimeView()
    {
        Camera camera = head != null ? head.GetComponent<Camera>() : null;
        Vector3 eyePosition = head != null ? head.position : transform.position;
        Vector3 eyeForward = head != null ? head.forward : transform.forward;
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
            $"forward={eyeForward:F3}, visibleRenderers={visibleRenderers}."
        );
    }

    private void Update()
    {
        if (!initialized || characterController == null)
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

    /// <summary>Captures the authored spawn marker, or the current player pose.</summary>
    public void CaptureSpawnPose()
    {
        if (spawnPoint != null)
        {
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
        }
        else
        {
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

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
        if (characterController != null)
        {
            characterController.enabled = controllerWasEnabled;
        }
        verticalVelocity = 0f;
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
    /// Sets scene-authored references and writes the controller defaults so the
    /// scene is usable before the first runtime frame as well as after Awake.
    /// </summary>
    public void ConfigureSceneReferences(Transform origin, Transform eyes)
    {
        xrOrigin = origin;
        head = eyes;
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
        characterController.detectCollisions = true;
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

        Vector3 before = transform.position;
        Vector3 requestedWorldOffset = transform.TransformVector(localOffset);
        characterController.Move(requestedWorldOffset);
        Vector3 appliedWorldOffset = transform.position - before;

        // Keep the tracked head at its real-world pose while the collision
        // capsule follows room-scale movement. Any blocked remainder stays in
        // the tracking rig; hand penetration is handled by the hand limiter.
        xrOrigin.position -= appliedWorldOffset;
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
