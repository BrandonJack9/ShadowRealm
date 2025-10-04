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

    [Header("Camera Input (optional)")]
    public Behaviour cinemachineInputProvider;

    [Header("Revive")]
    [Tooltip("How close you must be to revive a KO'd teammate.")]
    public float reviveRange = 2.5f;
    [Tooltip("Key to trigger a revive attempt.")]
    public KeyCode reviveKey = KeyCode.E;
    [Tooltip("How long you must hold the revive key.")]
    public float reviveDuration = 2.5f;

    private CharacterController controller;
    private Animator animator;

    private Vector3 velocity;
    private bool isGrounded;
    private float turnSmoothVelocity;
    private NetworkObject currentPlasmaBall;

    private bool inputEnabled = true;

    // Cached per-frame inputs
    Vector2 moveInput;
    bool runHeld;
    bool jumpPressed;
    bool throwPressed;

    // Revive state
    private PlayerHealth currentReviveTarget;
    private float reviveHoldTime;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
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
            bool playing = GameManager.Instance != null &&
                           GameManager.Instance.State.Value == GameManager.RoundState.Playing;
            SetCursorLocked(playing);
            SetCinemachineInputEnabled(playing);
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        var healthCmp = GetComponent<PlayerHealth>();
        if (healthCmp != null && healthCmp.IsKO)
        {
            inputEnabled = false;
            HandleGravityAndJump(false);
            UpdateAnimator(Vector2.zero, false);
            return;
        }

        HandleCursorAndInputMode();
        ReadInputs();

        bool playing = GameManager.Instance != null &&
                       GameManager.Instance.State.Value == GameManager.RoundState.Playing;

        if (playing && inputEnabled)
        {
            HandleReviveInput();
        }
        else
        {
            UIManager.Instance?.HideRevivePrompt();
            reviveHoldTime = 0f;
        }

        HandleMovement(moveInput, runHeld);
        HandleGravityAndJump(jumpPressed);

        if (throwPressed)
            ThrowPlasmaServerRpc();

        UpdateAnimator(moveInput, runHeld);
    }

    // ---------------- Cursor & Input ----------------
    void HandleCursorAndInputMode()
    {
        bool playing = GameManager.Instance != null &&
                       GameManager.Instance.State.Value == GameManager.RoundState.Playing;

        if (playing)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SetCursorLocked(false);

            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
                SetCursorLocked(true);
        }
        else
        {
            if (Cursor.lockState != CursorLockMode.None)
                SetCursorLocked(false);
        }

        inputEnabled = playing && (Cursor.lockState == CursorLockMode.Locked);
        SetCinemachineInputEnabled(inputEnabled);
    }

    void SetCursorLocked(bool locked)
    {
        if (locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void SetCinemachineInputEnabled(bool enabled)
    {
        if (cinemachineInputProvider != null)
            cinemachineInputProvider.enabled = enabled;
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        SetCinemachineInputEnabled(enabled);
    }

    // ---------------- Input ----------------
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

    // ---------------- Movement ----------------
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
        if (groundCheck != null)
        {
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
        }
        else
        {
            isGrounded = controller.isGrounded;
        }

        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        if (isGrounded && wantJump)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    float GetCameraYaw()
    {
        if (Camera.main != null)
            return Camera.main.transform.eulerAngles.y;

        return transform.eulerAngles.y;
    }

    // ---------------- Animator ----------------
    void UpdateAnimator(Vector2 input, bool run)
    {
        bool moving = input.sqrMagnitude >= 0.01f;
        bool walking = isGrounded && moving && !run;
        bool running = isGrounded && moving && run;

        animator.SetBool("isJumping", !isGrounded);
        animator.SetBool("isWalking", walking);
        animator.SetBool("isRunning", running);
    }

    // ---------------- Throw ----------------
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

    // ---------------- Revive (unchanged) ----------------
    private void HandleReviveInput()
    {
        currentReviveTarget = FindNearbyKOTeammate();

        if (currentReviveTarget != null)
        {
            // Always show the revive message when in range
            if (!Input.GetKey(reviveKey))
            {
                reviveHoldTime = 0f;
                UIManager.Instance?.ShowReviveMessageOnly();
            }
            else
            {
                // Holding the key → show progress bar
                reviveHoldTime += Time.deltaTime;
                float progress = reviveHoldTime / reviveDuration;
                UIManager.Instance?.UpdateReviveProgress(progress);

                if (reviveHoldTime >= reviveDuration)
                {
                    var no = currentReviveTarget.GetComponent<NetworkObject>();
                    if (no != null)
                    {
                        TryReviveTargetServerRpc(new NetworkObjectReference(no));
                    }
                    reviveHoldTime = 0f;
                    currentReviveTarget = null;
                    UIManager.Instance?.HideRevivePrompt();
                }
            }
        }
        else
        {
            reviveHoldTime = 0f;
            UIManager.Instance?.HideRevivePrompt();
        }
    }

    private PlayerHealth FindNearbyKOTeammate()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, reviveRange);
        NetworkObject selfNO = GetComponent<NetworkObject>();

        foreach (var h in hits)
        {
            var ph = h.GetComponentInParent<PlayerHealth>();
            if (ph == null || !ph.IsKO) continue;

            var no = ph.GetComponent<NetworkObject>();
            if (no == null || !no.IsSpawned) continue;

            if (selfNO != null && no.NetworkObjectId == selfNO.NetworkObjectId) continue;

            return ph;
        }
        return null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void TryReviveTargetServerRpc(NetworkObjectReference targetRef, ServerRpcParams rpc = default)
    {
        if (!targetRef.TryGet(out NetworkObject targetObj)) return;

        var targetPH = targetObj.GetComponent<PlayerHealth>();
        if (targetPH == null || !targetPH.IsSpawned || !targetPH.IsKO) return;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var reviverClient))
            return;

        var reviverObj = reviverClient.PlayerObject;
        if (reviverObj == null) return;

        float dist = Vector3.Distance(reviverObj.transform.position, targetObj.transform.position);
        if (dist > reviveRange + 0.75f) return;

        targetPH.ServerReviveImmediate();
    }

    // ---------------- Teleport / Reset (NEW) ----------------
    [ServerRpc(RequireOwnership = false)]
    public void ResetTransformServerRpc(Vector3 position, Quaternion rotation, ServerRpcParams rpc = default)
    {
        // Snap on server
        if (controller != null) controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        velocity = Vector3.zero;
        if (controller != null) controller.enabled = true;

        // Also tell the owner to snap locally (important if transform is owner-auth)
        var targetOwner = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { OwnerClientId } }
        };
        TeleportClientRpc(position, rotation, targetOwner);
    }

    [ClientRpc]
    public void TeleportClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams rpc = default)
    {
        if (controller != null) controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        velocity = Vector3.zero;
        if (controller != null) controller.enabled = true;
    }
}
