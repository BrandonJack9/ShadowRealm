using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine; // Unity 6 + Cinemachine 3.x

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerNetwork : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 3f;
    public float runSpeed = 6f;
    public float rotationSmoothTime = 0.1f;
    public float jumpHeight = 2f;
    public float gravity = -9.81f;

    [Header("Ground Check Settings")]
    public Transform groundCheck;
    public float groundDistance = 0.3f;
    public LayerMask groundMask;

    [Header("Combat Settings")]
    public GameObject plasmaBallPrefab;
    public Transform throwPoint;

    [Header("Camera Settings")]
    public CinemachineCamera playerCamera;
    public Transform cameraFollowTarget;

    private CharacterController controller;
    private Animator animator;

    private Vector3 velocity;     // vertical velocity lives here
    private bool isGrounded;
    private float turnSmoothVelocity;
    private NetworkObject currentPlasmaBall;

    // Input gating
    private bool inputEnabled = true;

    // Cached per-frame inputs (zeroed when input disabled)
    Vector2 moveInput;
    bool runHeld;
    bool jumpPressed;
    bool throwPressed;

    // -----------------------------------------

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        // Per-owner camera activation
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(IsOwner);
            if (IsOwner && cameraFollowTarget != null)
            {
                playerCamera.Follow = cameraFollowTarget;
                playerCamera.LookAt = cameraFollowTarget;
            }
        }

        if (IsOwner)
        {
            LockCursor(true);
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // Cursor lock toggles can happen anytime
        HandleCursorLock();

        // Gather inputs only if enabled; otherwise zeros
        ReadInputs();

        // Horizontal movement responds only when input is enabled
        HandleMovement(moveInput, runHeld);

        // Gravity & landing always run (prevents mid-air freezing)
        HandleGravityAndJump(jumpPressed);

        // Throw only when input enabled
        if (throwPressed)
            ThrowPlasmaServerRpc();

        // Animator driven by actual state, not by input alone
        UpdateAnimator(moveInput, runHeld);
    }


    private void LateUpdate()
    {
        if (!IsOwner) return;

        // Disable input unless game is Playing
        if (GameManager.Instance != null)
        {
            inputEnabled = (GameManager.Instance.State.Value == GameManager.RoundState.Playing);
        }
    }
    // -----------------------------------------
    // Input & Cursor
    // -----------------------------------------

    void ReadInputs()
    {
        if (inputEnabled)
        {
            moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            runHeld = Input.GetKey(KeyCode.LeftShift);
            jumpPressed = Input.GetKeyDown(KeyCode.Space);
            throwPressed = Input.GetMouseButtonDown(0);
        }
        else
        {
            moveInput = Vector2.zero;
            runHeld = false;
            jumpPressed = false;
            throwPressed = false;
        }
    }

    void HandleCursorLock()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            LockCursor(false);

        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);
    }

    void LockCursor(bool locked)
    {
        if (locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            inputEnabled = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            inputEnabled = false;
        }
    }

    // -----------------------------------------
    // Movement & Physics
    // -----------------------------------------

    void HandleMovement(Vector2 input, bool run)
    {
        bool isMoving = input.sqrMagnitude >= 0.01f;
        if (!isMoving) return;

        float camYaw = GetCameraYaw();
        float targetAngle = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg + camYaw;

        float angle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y, targetAngle,
            ref turnSmoothVelocity, rotationSmoothTime
        );
        transform.rotation = Quaternion.Euler(0f, angle, 0f);

        Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        float speed = run ? runSpeed : walkSpeed;
        controller.Move(moveDir.normalized * speed * Time.deltaTime);
    }

    void HandleGravityAndJump(bool wantJump)
    {
        // Ground check
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

        // Stick to ground a bit to ensure controller.IsGrounded-like behavior
        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        // Only allow jump when input is enabled & grounded
        if (isGrounded && wantJump)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        // Always apply gravity so we never "hang" mid-air
        velocity.y += gravity * Time.deltaTime;

        // Apply vertical motion every frame
        controller.Move(velocity * Time.deltaTime);
    }

    float GetCameraYaw()
    {
        if (Camera.main != null)
            return Camera.main.transform.eulerAngles.y;

        return transform.eulerAngles.y; // fallback
    }

    // -----------------------------------------
    // Animator
    // -----------------------------------------

    void UpdateAnimator(Vector2 input, bool run)
    {
        bool moving = input.sqrMagnitude >= 0.01f;
        bool walking = isGrounded && moving && !run;
        bool running = isGrounded && moving && run;

        // Stable jump state: true whenever airborne
        animator.SetBool("isJumping", !isGrounded);

        // Ground locomotion states only when grounded
        animator.SetBool("isWalking", walking);
        animator.SetBool("isRunning", running);
    }

    // -----------------------------------------
    // Throw
    // -----------------------------------------

    [ServerRpc]
    void ThrowPlasmaServerRpc(ServerRpcParams rpcParams = default)
    {
        if (currentPlasmaBall != null && currentPlasmaBall.IsSpawned)
        {
            currentPlasmaBall.Despawn();
            currentPlasmaBall = null;
        }

        GameObject plasmaInstance = Instantiate(plasmaBallPrefab, throwPoint.position, throwPoint.rotation);
        NetworkObject netObj = plasmaInstance.GetComponent<NetworkObject>();
        netObj.SpawnWithOwnership(OwnerClientId);
        currentPlasmaBall = netObj;
    }
}
