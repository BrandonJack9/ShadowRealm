using UnityEngine;

[RequireComponent(typeof(PlayerMovement), typeof(GravityBody), typeof(Rigidbody))]
public class PlanetaryWalker : MonoBehaviour
{
    [Header("Camera")]
    public Transform playerCamera;
    public float mouseSensitivity = 2.2f;
    public float cameraPitchClamp = 80f;

    [Header("Body Rotation")]
    public float bodyRotationSpeed = 6f;          // smoothing for normal body turning
    public float gravityTransitionSpeed = 3f;     // smooth in/out of gravity
    [Tooltip("Duration to smoothly stand upright when ENTERING a planet's gravity.")]
    public float alignOnEnterDuration = 0.35f;    // << smooth upright on entry

    [Header("Space Movement")]
    public float spaceSpeed = 6f;

    private PlayerMovement move;
    private GravityBody grav;
    private Rigidbody rb;

    // Inputs
    float inputH, inputV;
    bool jumpPressed, jumpHeld;
    bool thrustUp, thrustDown;

    // Look state
    float yaw = 0f;     // used mainly for space yaw
    float pitch = 0f;   // camera pitch only

    // Rotation targets
    Quaternion targetBodyRotation;

    // Blending gravity (for feel)
    float gravityBlend = 0f;

    // Transition detection
    bool wasInGravity = false;

    // --- NEW: smooth upright alignment on ENTERING gravity ---
    bool aligningToPlanet = false;
    float alignTimer = 0f;
    Quaternion alignFrom, alignTo;

    void Awake()
    {
        move = GetComponent<PlayerMovement>();
        grav = GetComponent<GravityBody>();
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        yaw = transform.eulerAngles.y;
        pitch = 0f;
        targetBodyRotation = rb.rotation;
    }

    void Update()
    {
        ReadInputs();
        HandleMouseLook();
        UpdateDesiredVelocities();
    }

    void FixedUpdate()
    {
        bool inGravity = grav.IsInGravity();

        // Detect ENTERING gravity: start a timed upright-alignment (smooth)
        if (inGravity && !wasInGravity)
        {
            BeginAlignToPlanet();
        }

        // Smooth blend of "up" feel (gravity vs world)
        float targetBlend = inGravity ? 1f : 0f;
        gravityBlend = Mathf.MoveTowards(gravityBlend, targetBlend, gravityTransitionSpeed * Time.fixedDeltaTime);

        // While aligning on entry, follow the timed Slerp path exactly (no extra smoothing)
        if (aligningToPlanet)
        {
            alignTimer += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(alignTimer / alignOnEnterDuration);
            Quaternion pathRot = Quaternion.Slerp(alignFrom, alignTo, t);

            rb.MoveRotation(pathRot);
            targetBodyRotation = pathRot; // keep target in sync

            if (t >= 1f)
            {
                aligningToPlanet = false;

                // Optional: set yaw to match current world heading for a seamless future exit
                Vector3 fwdXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (fwdXZ.sqrMagnitude > 1e-6f)
                    yaw = Mathf.Atan2(fwdXZ.x, fwdXZ.z) * Mathf.Rad2Deg;
            }
        }
        else
        {
            // Normal rotation follow (smoothed)
            Quaternion next = Quaternion.Slerp(rb.rotation, targetBodyRotation, bodyRotationSpeed * Time.fixedDeltaTime);
            rb.MoveRotation(next);
        }

        // Jump & jetpack (handled in physics)
        if (jumpPressed) move.Jump();
        move.HoldJump(jumpHeld);
        move.SetJetpackInputs(thrustUp, thrustDown);

        wasInGravity = inGravity;
    }

    void ReadInputs()
    {
        inputH = Input.GetAxisRaw("Horizontal");
        inputV = Input.GetAxisRaw("Vertical");
        jumpPressed = Input.GetKeyDown(KeyCode.Space);
        jumpHeld = Input.GetKey(KeyCode.Space);

        thrustUp = Input.GetKey(KeyCode.Space);          // up thrust
        thrustDown = Input.GetKey(KeyCode.LeftControl);  // down thrust
    }

    void HandleMouseLook()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mx;
        pitch = Mathf.Clamp(pitch - my, -cameraPitchClamp, cameraPitchClamp);

        // Camera only pitches locally (prevents body<->camera feedback)
        playerCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // If we're in the middle of the entry alignment, don't overwrite the target
        if (aligningToPlanet && grav.IsInGravity())
            return;

        if (grav.IsInGravity())
        {
            // Planet mode: incremental yaw around the *current* planet up, then make body upright to that up
            Vector3 up = grav.GetSurfaceNormal();

            // Apply yaw as a delta rotation around up to current forward
            Vector3 yawedForward = Quaternion.AngleAxis(mx, up) * rb.transform.forward;

            // Constrain to tangent plane & build an upright rotation
            Vector3 tangentForward = Vector3.ProjectOnPlane(yawedForward, up).normalized;
            if (tangentForward.sqrMagnitude < 1e-6f)
                tangentForward = Vector3.ProjectOnPlane(rb.transform.forward, up).normalized;

            targetBodyRotation = Quaternion.LookRotation(tangentForward, up);
        }
        else
        {
            // Space mode: body faces where the camera is looking (yaw + pitch), staying upright to world-up (no roll)
            // Use the mouse-driven yaw/pitch, not camera world rotation (avoids feedback loops).
            Quaternion camWorldRot = Quaternion.Euler(pitch, yaw, 0f);
            targetBodyRotation = camWorldRot;
        }
    }

    void BeginAlignToPlanet()
    {
        aligningToPlanet = true;
        alignTimer = 0f;
        alignFrom = rb.rotation;

        Vector3 up = grav.GetSurfaceNormal();

        // Prefer to align the body to where the CAMERA is looking along the surface (feels natural on landing)
        Vector3 camFwdOnSurface = Vector3.ProjectOnPlane(playerCamera.forward, up).normalized;

        // Fallback to body's projected forward if camera is near-vertical
        if (camFwdOnSurface.sqrMagnitude < 1e-6f)
            camFwdOnSurface = Vector3.ProjectOnPlane(transform.forward, up).normalized;

        if (camFwdOnSurface.sqrMagnitude < 1e-6f)
            camFwdOnSurface = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;

        alignTo = Quaternion.LookRotation(camFwdOnSurface, up);
    }

    void UpdateDesiredVelocities()
    {
        if (grav.IsInGravity())
        {
            Vector3 up = grav.GetSurfaceNormal();
            Vector3 fwd = Vector3.ProjectOnPlane(playerCamera.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);

            Vector3 desired = right * inputH + fwd * inputV;
            if (desired.sqrMagnitude > 1f) desired.Normalize();
            desired *= move.moveSpeed;

            move.SetDesiredVelocityPlanet(desired);
            move.SetDesiredVelocitySpace(Vector3.zero);
        }
        else
        {
            // Space: camera-relative on world XZ
            Vector3 desired = (playerCamera.right * inputH + playerCamera.forward * inputV);
            if (desired.sqrMagnitude > 1f) desired.Normalize();
            desired *= spaceSpeed;

            move.SetDesiredVelocitySpace(desired);
            move.SetDesiredVelocityPlanet(Vector3.zero);
        }
    }
}
