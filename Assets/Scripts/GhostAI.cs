using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Networked Ghost AI with patrol, chase, attack, KO (physics fall), pickup & lab conversion.
/// Collider stays active as trigger while carried so lab can detect it.
/// Requires: NetworkObject, NavMeshAgent, Rigidbody, Collider.
/// </summary>
public class GhostAI : NetworkBehaviour
{
    public enum State { Idle, Patrolling, Chasing, Attacking, Unconscious, Carried }

    [Header("Tier & Health")]
    [SerializeField] private int tier = 1; // plasma reward
    [SerializeField] private float maxHealth = 100f;
    [SerializeField, Range(0f, 1f)] private float unconsciousThreshold = 0.25f;
    [Header("Debug")]
    [SerializeField] public float currentHealth; // visible in Inspector

    [Header("Perception & Combat")]
    [SerializeField] private float detectionRange = 12f;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private float attackDamage = 10f;

    [Header("Patrol")]
    [SerializeField] private float waypointTolerance = 1f;

    [Header("Wander Fallback (if no patrol route injected)")]
    [SerializeField] private bool enableWanderFallback = true;
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float wanderInterval = 5f;

    [Header("Pickup / Carry")]
    [SerializeField] private float pickupRange = 2f;
    [SerializeField] private Vector3 carryLocalOffset = new(0f, 1f, 1f);

    [Header("NavMesh Safety")]
    [SerializeField] private float snapToNavmeshMaxDistance = 5f;

    private NavMeshAgent agent;
    private Animator animator;
    private Collider hitCollider;
    private Rigidbody rb;

    // Server-authoritative state
    private State currentState = State.Idle;
    private readonly List<Vector3> patrolPoints = new();
    private int patrolIndex = 0;

    private float lastAttackTime = -999f;
    private PlayerHealth targetPlayer; // server target
    private NetworkObject carrier;     // server carrier

    private Vector3 homePosition;
    private float nextWanderTime;

    // ---------------- Unity ----------------
    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        hitCollider = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();

        // Rigidbody setup (defaults while alive)
        rb.isKinematic = true;
        rb.useGravity = false;

        currentHealth = maxHealth;

        // Ensure NavMeshAgent has sane defaults
        if (agent.speed <= 0.01f) agent.speed = 3.5f;
        if (agent.acceleration <= 0.01f) agent.acceleration = 8f;
        if (agent.angularSpeed <= 0.01f) agent.angularSpeed = 720f;
        agent.stoppingDistance = Mathf.Clamp(attackRange * 0.6f, 0f, attackRange - 0.05f);
        agent.updateRotation = true;
        agent.updatePosition = true;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        homePosition = transform.position;

        if (!EnsureOnNavMesh())
        {
            Debug.LogWarning($"[GhostAI:{name}] Not on NavMesh. Will stay idle until repositioned.");
            currentState = State.Idle;
            return;
        }

        BeginPatrolOrWander();
    }

    private void Update()
    {
        // --- SERVER drives AI ---
        if (IsServer)
        {
            switch (currentState)
            {
                case State.Patrolling: DoPatrol(); break;
                case State.Chasing: DoChase(); break;
                case State.Attacking: DoAttack(); break;
                case State.Unconscious:
                case State.Carried:
                    if (agent.enabled) { agent.isStopped = true; agent.ResetPath(); }
                    break;
                case State.Idle:
                    if (enableWanderFallback && agent.enabled && agent.isOnNavMesh && Time.time >= nextWanderTime)
                    {
                        SetWanderDestination();
                        currentState = State.Patrolling;
                    }
                    break;
            }
        }

        // --- CLIENT pickup attempt ---
        if (IsClient && Input.GetKeyDown(KeyCode.E))
        {
            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                float dist = Vector3.Distance(localPlayer.transform.position, transform.position);
                if (dist <= pickupRange)
                    RequestPickupServerRpc(localPlayer.OwnerClientId);
            }
        }

        // Animator sync
        if (animator != null)
        {
            bool moving = IsServer && agent.enabled && !agent.isStopped && agent.velocity.sqrMagnitude > 0.05f;
            animator.SetBool("Moving", moving && (currentState == State.Chasing || currentState == State.Patrolling));
            animator.SetBool("Unconscious", currentState == State.Unconscious || currentState == State.Carried);
        }
    }

    // ---------------- Patrol / Wander ----------------
    public void SetPatrolPath(IEnumerable<Transform> points)
    {
        patrolPoints.Clear();
        if (points != null) foreach (var t in points) if (t) patrolPoints.Add(t.position);
    }

    public void SetPatrolPath(IEnumerable<Vector3> points)
    {
        patrolPoints.Clear();
        if (points != null) patrolPoints.AddRange(points);
    }

    private void BeginPatrolOrWander()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;

        if (patrolPoints.Count > 0)
        {
            currentState = State.Patrolling;
            patrolIndex = 0;
            agent.isStopped = false;
            agent.SetDestination(patrolPoints[patrolIndex]);
        }
        else if (enableWanderFallback)
        {
            SetWanderDestination();
            currentState = State.Patrolling;
        }
        else
        {
            currentState = State.Idle;
            agent.isStopped = true;
        }
    }

    private void DoPatrol()
    {
        if (!agent.enabled || !agent.isOnNavMesh) { TryRecoverToNavmesh(); return; }

        if (patrolPoints.Count > 0)
        {
            if (!agent.pathPending && agent.remainingDistance <= waypointTolerance)
            {
                patrolIndex = (patrolIndex + 1) % patrolPoints.Count;
                agent.SetDestination(patrolPoints[patrolIndex]);
            }
        }
        else if (enableWanderFallback)
        {
            if (!agent.pathPending && agent.remainingDistance <= waypointTolerance || Time.time >= nextWanderTime)
                SetWanderDestination();
        }

        LookForPlayer();
    }

    private void DoChase()
    {
        if (targetPlayer == null || targetPlayer.IsKO)
        {
            targetPlayer = null;
            ReturnToRoam();
            return;
        }

        if (!agent.enabled || !agent.isOnNavMesh) { TryRecoverToNavmesh(); return; }

        float dist = Vector3.Distance(transform.position, targetPlayer.transform.position);
        if (dist > detectionRange * 1.5f)
        {
            targetPlayer = null;
            ReturnToRoam();
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(targetPlayer.transform.position);

        if (dist <= attackRange)
            currentState = State.Attacking;
    }

    private void DoAttack()
    {
        if (targetPlayer == null || targetPlayer.IsKO)
        {
            targetPlayer = null;
            ReturnToRoam();
            return;
        }

        float dist = Vector3.Distance(transform.position, targetPlayer.transform.position);
        if (dist > attackRange)
        {
            currentState = State.Chasing;
            return;
        }

        if (Time.time - lastAttackTime >= attackCooldown)
        {
            targetPlayer.TakeDamageServerRpc(attackDamage);
            lastAttackTime = Time.time;
        }
    }

    private void ReturnToRoam()
    {
        if (patrolPoints.Count > 0 || enableWanderFallback)
        {
            currentState = State.Patrolling;
            if (patrolPoints.Count > 0)
                agent.SetDestination(patrolPoints[patrolIndex]);
            else
                SetWanderDestination();
        }
        else
        {
            currentState = State.Idle;
            agent.isStopped = true;
        }
    }

    private void LookForPlayer()
    {
        foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerObj = c.PlayerObject;
            if (playerObj == null) continue;

            var ph = playerObj.GetComponent<PlayerHealth>();
            if (ph != null && !ph.IsKO)
            {
                float d = Vector3.Distance(transform.position, playerObj.transform.position);
                if (d <= detectionRange)
                {
                    targetPlayer = ph;
                    currentState = State.Chasing;
                    return;
                }
            }
        }
    }

    // ---------------- Damage / KO ----------------
    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float dmg)
    {
        if (currentState == State.Unconscious || currentState == State.Carried) return;

        currentHealth = Mathf.Max(0, currentHealth - Mathf.Abs(dmg));
        if (currentHealth <= maxHealth * unconsciousThreshold)
            GoUnconsciousServer();
    }

    private void GoUnconsciousServer()
    {
        currentState = State.Unconscious;

        if (agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.enabled = false;
        }

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        if (hitCollider != null)
        {
            hitCollider.isTrigger = false; // solid physics when KO’d
        }

        SetUnconsciousClientRpc();
    }

    [ClientRpc]
    private void SetUnconsciousClientRpc()
    {
        currentState = State.Unconscious;
    }

    // ---------------- Pickup / Carry ----------------
    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong requesterClientId)
    {
        if (currentState != State.Unconscious) return;

        var playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(requesterClientId);
        if (playerObj == null) return;

        carrier = playerObj;
        currentState = State.Carried;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        if (hitCollider != null)
        {
            hitCollider.isTrigger = true; // ✅ keep collider active as trigger for lab
        }

        transform.SetParent(playerObj.transform, false);
        transform.localPosition = carryLocalOffset;
        transform.localRotation = Quaternion.identity;

        BecameCarriedClientRpc(playerObj.OwnerClientId, carryLocalOffset);
    }

    [ClientRpc]
    private void BecameCarriedClientRpc(ulong carrierClientId, Vector3 localOffset)
    {
        currentState = State.Carried;

        var playerObj = NetworkManager.Singleton?.SpawnManager?.GetPlayerNetworkObject(carrierClientId);
        if (playerObj == null) return;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
        }

        transform.SetParent(playerObj.transform, false);
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.identity;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ConvertToPlasmaServerRpc()
    {
        if (currentState != State.Carried) return;

        GameManager.Instance.AddPlasmaServerRpc(tier);

        transform.SetParent(null, true); // detach before despawn

        if (NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }

    public bool IsCarriedServer() => IsServer && currentState == State.Carried;

    // ---------------- NavMesh helpers ----------------
    private bool EnsureOnNavMesh()
    {
        if (!agent.enabled) agent.enabled = true;
        if (agent.isOnNavMesh) return true;

        if (NavMesh.SamplePosition(transform.position, out var hit, snapToNavmeshMaxDistance, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            return true;
        }
        return false;
    }

    private void TryRecoverToNavmesh()
    {
        if (!EnsureOnNavMesh())
        {
            Debug.LogWarning($"[GhostAI:{name}] Off NavMesh, idle.");
            currentState = State.Idle;
        }
    }

    private void SetWanderDestination()
    {
        nextWanderTime = Time.time + wanderInterval;
        Vector3 random = homePosition + Random.insideUnitSphere * wanderRadius;
        random.y = homePosition.y;

        if (NavMesh.SamplePosition(random, out var hit, wanderRadius + 5f, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
        else
        {
            agent.isStopped = false;
            agent.SetDestination(homePosition);
        }
    }

    // ---------------- Debug Gizmos ----------------
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
#endif
}
