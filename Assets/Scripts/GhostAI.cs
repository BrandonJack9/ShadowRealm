using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class GhostAI : NetworkBehaviour
{
    public enum State { Idle, Patrolling, Chasing, Attacking, Phasing, Unconscious, Carried }

    [Header("Tier & Health")]
    [SerializeField] private int tier = 1;
    [SerializeField] private float maxHealth = 100f;
    [SerializeField, Range(0f, 1f)] private float unconsciousThreshold = 0.25f;

    [Header("Regeneration")]
    [SerializeField] private float regenPerSecondConscious = 2.0f;
    [SerializeField] private float regenPerSecondKO = 1.0f;
    [SerializeField, Range(0f, 1f)] private float wakeAtFraction = 0.5f;

    [Header("Perception & Combat")]
    [SerializeField] private float detectionRange = 12f;
    [SerializeField] private float attackRange = 2.5f;
    [SerializeField] private float attackCooldown = 2.0f;
    [SerializeField] private float attackDamage = 10f;

    [Header("Projectile (Spit)")]
    [SerializeField] private GameObject plasmaProjectilePrefab; // NetworkObject + Rigidbody
    [SerializeField] private float projectileSpeed = 16f;
    [SerializeField] private Transform projectileMuzzle;

    [Header("Phase / Backstab")]
    [SerializeField] private float phaseDelay = 1.0f;
    [SerializeField] private float backstabOffset = 1.5f;
    [SerializeField] private float phaseSinkDepth = 1.2f;
    [SerializeField] private float phaseSinkTime = 0.35f;
    [SerializeField] private float phaseEmergeTime = 0.35f;
    [SerializeField] private float postEmergeInvuln = 0.2f;

    // Randomized reappear around player (fractions of attackRange)
    [SerializeField, Range(0.2f, 0.99f)] private float reappearMinAttackFraction = 0.60f;
    [SerializeField, Range(0.2f, 0.99f)] private float reappearMaxAttackFraction = 0.90f;
    [SerializeField] private int reappearPickTries = 10;
    [SerializeField] private float minSeparationFromStart = 1.0f;

    [Header("Patrol / Wander")]
    [SerializeField] private bool enableWanderFallback = true;
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float wanderInterval = 5f;
    [SerializeField] private float waypointTolerance = 1f;
    [SerializeField] private float spawnScatterRadius = 1.4f;

    // (compat placeholders)
    [SerializeField] private int patrolBootstrapTries = 8;
    [SerializeField] private float patrolTryDelay = 0.2f;
    [SerializeField] private float patrolMinStartDistance = 4f;
    [SerializeField] private int patrolPickAttemptsPerTry = 10;

    [Header("Pickup / Carry")]
    [SerializeField] private float pickupRange = 2f;
    [SerializeField] private Vector3 carryLocalOffset = new(0f, 1f, 1f);

    [Header("NavMesh Safety")]
    [SerializeField] private float snapToNavmeshMaxDistance = 5f;

    [Header("Grounding (emerge)")]
    [SerializeField] private LayerMask groundMask = ~0;  // raycast mask
    [SerializeField] private float groundProbeUp = 4f;
    [SerializeField] private float groundProbeDown = 8f;
    [SerializeField] private float fallbackBottomClearance = 0.02f; // if we can't measure live clearance

    [Header("Visuals (optional)")]
    [SerializeField] private Renderer[] fadeRenderers;
    [SerializeField, Range(0f, 1f)] private float phasedAlpha = 0.35f;

    [Header("Debug")]
    [SerializeField] public float currentHealth;

    // comps
    private NavMeshAgent agent;
    private Animator animator;
    private Collider hitCollider;
    private Rigidbody rb;

    // state
    private State currentState = State.Idle;
    private float lastAttackTime = -999f;
    private PlayerHealth targetPlayer;
    private NetworkObject carrier;

    private Vector3 homePosition;
    private float nextWanderTime;
    private bool invulnerable;
    private bool dead;

    // patrol anti-stuck
    private float repathByTime = 0f;
    private const float STUCK_REPATH_SECONDS = 3.0f;

    // stop distances
    private const float PATROL_STOP_DISTANCE = 0.05f;
    private float combatStopDistance => Mathf.Clamp(attackRange * 0.6f, 0f, Mathf.Max(attackRange - 0.05f, 0f));

    // utils
    private static bool IsFinite(Vector3 v)
        => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
             float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        hitCollider = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.useGravity = false;

        if (fadeRenderers == null || fadeRenderers.Length == 0)
            fadeRenderers = GetComponentsInChildren<Renderer>(true);

        currentHealth = maxHealth;

        if (agent.speed <= 0.01f) agent.speed = 3.5f;
        if (agent.acceleration <= 0.01f) agent.acceleration = 8f;
        if (agent.angularSpeed <= 0.01f) agent.angularSpeed = 720f;
        agent.updateRotation = true;
        agent.updatePosition = true;
        agent.autoRepath = true;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        homePosition = transform.position;
        StartCoroutine(StartPatrolNextFrame());
    }

    private IEnumerator StartPatrolNextFrame()
    {
        yield return null;
        ScatterSlightlyOnSpawn();
        EnsureOnNavMesh();
        StartPatrollingNow();
    }

    private void ScatterSlightlyOnSpawn()
    {
        Vector3 jitter = Random.insideUnitSphere * spawnScatterRadius; jitter.y = 0f;
        Vector3 tryPos = transform.position + jitter;
        if (NavMesh.SamplePosition(tryPos, out var hit, spawnScatterRadius + 0.5f, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    private void StartPatrollingNow()
    {
        if (!EnsureOnNavMesh()) { currentState = State.Idle; return; }
        if (!agent.enabled) agent.enabled = true;

        agent.stoppingDistance = PATROL_STOP_DISTANCE;
        agent.isStopped = false;

        currentState = State.Patrolling;
        SetNextPatrolDestination();
    }

    private void Update()
    {
        // client pickup input (unchanged)
        if (IsClient && Input.GetKeyDown(KeyCode.E))
        {
            var local = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (local != null && Vector3.Distance(local.transform.position, transform.position) <= pickupRange)
                RequestPickupServerRpc(local.OwnerClientId);
        }

        // anim flags
        if (animator != null)
        {
            bool moving = agent.enabled && !agent.isStopped && agent.velocity.sqrMagnitude > 0.05f;
            animator.SetBool("Moving", moving && (currentState == State.Chasing || currentState == State.Patrolling));
            animator.SetBool("Unconscious", currentState == State.Unconscious || currentState == State.Carried);
        }

        if (!IsServer || dead) return;
        ServerTick();
    }

    private void ServerTick()
    {
        // regen
        if (currentState != State.Unconscious && currentState != State.Carried)
        {
            if (currentHealth < maxHealth)
                currentHealth = Mathf.Min(maxHealth, currentHealth + regenPerSecondConscious * Time.deltaTime);
        }
        else if (currentState == State.Unconscious && carrier == null)
        {
            if (currentHealth > 0 && currentHealth < maxHealth * wakeAtFraction)
                currentHealth = Mathf.Min(maxHealth * wakeAtFraction, currentHealth + regenPerSecondKO * Time.deltaTime);

            if (currentHealth >= maxHealth * wakeAtFraction)
            {
                WakeUpServer();
                return;
            }
        }

        switch (currentState)
        {
            case State.Patrolling:
                DoPatrol();
                LookForPlayer();
                break;
            case State.Chasing:
                DoChase();
                break;
            case State.Attacking:
                DoAttackGate();
                break;
            case State.Phasing:
                break;
            case State.Unconscious:
            case State.Carried:
                if (agent.enabled) { agent.isStopped = true; agent.ResetPath(); }
                break;
            case State.Idle:
                if (enableWanderFallback && Time.time >= nextWanderTime)
                {
                    StartPatrollingNow();
                    nextWanderTime = Time.time + 0.6f;
                }
                LookForPlayer();
                break;
        }
    }

    // --------- PATROL ----------
    private void DoPatrol()
    {
        if (!agent.enabled || !agent.isOnNavMesh)
        {
            TryRecoverToNavmesh();
            return;
        }

        bool likelyStuck = (agent.velocity.sqrMagnitude < 0.01f && Time.time > repathByTime);

        if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.pathPending == false)
        {
            if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid ||
                agent.remainingDistance <= agent.stoppingDistance + 0.05f || likelyStuck)
            {
                SetNextPatrolDestination();
                return;
            }
        }
    }

    private void SetNextPatrolDestination()
    {
        nextWanderTime = Time.time + wanderInterval;
        repathByTime = Time.time + STUCK_REPATH_SECONDS;

        Vector3 center = (homePosition.sqrMagnitude > 0.01f) ? homePosition : transform.position;

        const int tries = 6;
        bool set = false;
        for (int i = 0; i < tries; i++)
        {
            Vector3 random = center + Random.insideUnitSphere * wanderRadius;
            random.y = center.y;

            if (NavMesh.SamplePosition(random, out var hit, wanderRadius + 2f, NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
                set = true;
                break;
            }
        }

        if (!set)
        {
            repathByTime = Time.time + 0.5f;
        }
    }

    // ---------- Chase / Attack ----------
    private void DoChase()
    {
        if (targetPlayer == null || targetPlayer.IsKO)
        {
            targetPlayer = null;
            ReturnToRoam();
            return;
        }

        if (!agent.enabled || !agent.isOnNavMesh) { TryRecoverToNavmesh(); return; }

        agent.stoppingDistance = combatStopDistance;

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

    private void ReturnToRoam()
    {
        if (enableWanderFallback)
        {
            agent.stoppingDistance = PATROL_STOP_DISTANCE;
            StartPatrollingNow();
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
            var po = c.PlayerObject; if (!po) continue;
            var ph = po.GetComponent<PlayerHealth>(); if (ph == null || ph.IsKO) continue;

            if (Vector3.Distance(transform.position, po.transform.position) <= detectionRange)
            {
                targetPlayer = ph;
                currentState = State.Chasing;
                return;
            }
        }
    }

    // ---------- Attack loop ----------
    private float lastAttackTimeRecorded => lastAttackTime;
    private Coroutine attackLoopCo;

    private void DoAttackGate()
    {
        if (targetPlayer == null || targetPlayer.IsKO) { targetPlayer = null; ReturnToRoam(); return; }

        float dist = Vector3.Distance(transform.position, targetPlayer.transform.position);
        if (dist > attackRange * 1.25f) { currentState = State.Chasing; return; }

        if (Time.time - lastAttackTime < attackCooldown) { FaceTowards(targetPlayer.transform.position); return; }

        if (attackLoopCo == null) attackLoopCo = StartCoroutine(ServerAttackSequence());
    }

    private IEnumerator ServerAttackSequence()
    {
        currentState = State.Attacking;

        FaceTowards(targetPlayer.transform.position);
        ServerSpawnProjectileToward(targetPlayer.transform.position);
        lastAttackTime = Time.time;

        yield return new WaitForSeconds(phaseDelay);

        if (targetPlayer != null && !targetPlayer.IsKO)
        {
            Vector3 S = transform.position;
            Vector3 P = targetPlayer.transform.position;

            Vector3 E;
            if (!TryPickRandomReappearPointAroundPlayer(P, S, out E))
            {
                Vector3 dir = (P - S);
                float distSP = Mathf.Max(dir.magnitude, backstabOffset);
                E = P + dir.normalized * distSP;
            }

            if (!IsFinite(S) || !IsFinite(P) || !IsFinite(E))
            {
                ExitPhaseServer();
            }
            else
            {
                yield return StartCoroutine(ServerPhaseArc(S, P, E));
            }
        }

        if (targetPlayer != null && !targetPlayer.IsKO)
        {
            FaceTowards(targetPlayer.transform.position);
            ServerSpawnProjectileToward(targetPlayer.transform.position);
            lastAttackTime = Time.time;
        }

        currentState = State.Chasing;
        attackLoopCo = null;
    }

    private bool TryPickRandomReappearPointAroundPlayer(Vector3 playerPos, Vector3 startPos, out Vector3 result)
    {
        float minF = Mathf.Clamp(reappearMinAttackFraction, 0.2f, 0.99f);
        float maxF = Mathf.Clamp(reappearMaxAttackFraction, minF, 0.99f);

        for (int i = 0; i < Mathf.Max(1, reappearPickTries); i++)
        {
            float r = Random.Range(minF * attackRange, maxF * attackRange);

            Vector2 dir2 = Random.insideUnitCircle.normalized;
            if (dir2.sqrMagnitude < 1e-4f) dir2 = new Vector2(1, 0);

            Vector3 offset = new Vector3(dir2.x, 0f, dir2.y) * r;
            Vector3 candidate = playerPos + offset; candidate.y = playerPos.y;

            if (NavMesh.SamplePosition(candidate, out var hit, 1.5f, NavMesh.AllAreas))
            {
                Vector3 hpos = hit.position; hpos.y = playerPos.y;
                Vector3 sflat = startPos; sflat.y = playerPos.y;
                if (Vector3.Distance(hpos, sflat) < minSeparationFromStart) continue;

                if (IsFinite(hit.position))
                {
                    result = hit.position;
                    return true;
                }
            }
        }

        // fallback: cardinals
        float fallbackR = Mathf.Clamp(attackRange * 0.8f, 0.5f, Mathf.Max(attackRange - 0.1f, 0.6f));
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        foreach (var d in dirs)
        {
            Vector3 candidate = playerPos + d * fallbackR;
            if (NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas) && IsFinite(hit.position))
            {
                result = hit.position;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    // --------- PHASE ARC with true final standing height ----------
    private IEnumerator ServerPhaseArc(Vector3 S, Vector3 P, Vector3 E)
    {
        if (!IsFinite(S) || !IsFinite(P) || !IsFinite(E))
            yield break;

        // 1) Figure out the *final standing Y* we need at E (so there's no snap when agent enables)
        float groundYAtE = GetGroundYAt(E);
        float pivotToBottom = GetPivotToBottom();
        float measuredClearance = GetCurrentBottomClearance(); // usually equals (agent.baseOffset - pivotToBottom)
        float finalPivotY = groundYAtE + pivotToBottom + measuredClearance;

        // Safety: if anything went weird, fallback to agent.baseOffset
        if (!float.IsFinite(finalPivotY))
            finalPivotY = groundYAtE + Mathf.Max(agent.baseOffset, pivotToBottom + fallbackBottomClearance);

        Vector3 EFinal = new Vector3(E.x, finalPivotY, E.z);

        // 2) Enter phase (disable agent etc.)
        EnterPhaseServer();

        // 3) Build under-ground control points relative to final standing Y
        Vector3 Sdown = new Vector3(S.x, S.y - phaseSinkDepth, S.z);
        Vector3 Pdown = new Vector3(P.x, Mathf.Min(S.y, EFinal.y) - phaseSinkDepth, P.z);
        Vector3 Edown = new Vector3(EFinal.x, EFinal.y - phaseSinkDepth, EFinal.z);

        if (!IsFinite(Sdown) || !IsFinite(Pdown) || !IsFinite(Edown))
        {
            ExitPhaseServer();
            yield break;
        }

        // 4) Sink and underground travel
        yield return LerpPosition(S, Sdown, phaseSinkTime);
        yield return BezierPosition(Sdown, Pdown, Edown, Mathf.Max(phaseSinkTime, 0.4f));

        // 5) Smooth emerge to exact final pose while facing the player
        Quaternion startRot = transform.rotation;
        Vector3 lookPos = (targetPlayer != null) ? targetPlayer.transform.position : (transform.position + transform.forward);
        Vector3 lookDir = (lookPos - EFinal); lookDir.y = 0f;
        Quaternion targetRot = lookDir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(lookDir.normalized, Vector3.up) : startRot;

        float t = 0f;
        while (t < phaseEmergeTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / phaseEmergeTime);
            Vector3 p = Vector3.Lerp(Edown, EFinal, k);
            if (!IsFinite(p)) break;

            transform.rotation = Quaternion.Slerp(startRot, targetRot, k);
            transform.position = p;
            yield return null;
        }
        transform.SetPositionAndRotation(EFinal, targetRot);

        if (postEmergeInvuln > 0f) yield return new WaitForSeconds(postEmergeInvuln);

        // 6) Finalize without snap — warp agent to THIS exact pose
        ExitPhaseServer_FinalizeAt(EFinal, targetRot);
    }

    private void EnterPhaseServer()
    {
        currentState = State.Phasing;
        invulnerable = true;

        if (agent.enabled) { agent.isStopped = true; agent.ResetPath(); agent.enabled = false; }
        if (hitCollider) hitCollider.isTrigger = true;
        SetPhasedVisualsClientRpc(true, phasedAlpha);
    }

    // used for early bail
    private void ExitPhaseServer()
    {
        if (hitCollider) hitCollider.isTrigger = false;
        invulnerable = false;
        SetPhasedVisualsClientRpc(false, 1f);
    }

    private void ExitPhaseServer_FinalizeAt(Vector3 finalPos, Quaternion finalRot)
    {
        if (hitCollider) hitCollider.isTrigger = false;

        transform.SetPositionAndRotation(finalPos, finalRot);

        if (!agent.enabled) agent.enabled = true;
        agent.Warp(finalPos);
        agent.isStopped = false;
        agent.updateRotation = true;

        invulnerable = false;
        SetPhasedVisualsClientRpc(false, 1f);
    }

    // ---------- movement helpers ----------
    private IEnumerator LerpPosition(Vector3 a, Vector3 b, float tTotal)
    {
        if (!IsFinite(a) || !IsFinite(b)) yield break;

        float t = 0f;
        while (t < tTotal)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / tTotal);
            Vector3 p = Vector3.Lerp(a, b, k);
            if (!IsFinite(p)) yield break;
            transform.position = p;
            yield return null;
        }
        transform.position = b;
    }

    private IEnumerator BezierPosition(Vector3 a, Vector3 c, Vector3 b, float tTotal)
    {
        if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c)) yield break;

        float t = 0f;
        while (t < tTotal)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / tTotal);
            Vector3 p = (1 - k) * (1 - k) * a + 2 * (1 - k) * k * c + k * k * b;

            if (!IsFinite(p)) yield break;
            transform.position = p;
            yield return null;
        }
        transform.position = b;
    }

    private void FaceTowards(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position; to.y = 0;
        if (to.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 0.5f);
    }

    private void ServerSpawnProjectileToward(Vector3 targetPos)
    {
        if (plasmaProjectilePrefab == null) return;

        Vector3 muzzlePos = projectileMuzzle ? projectileMuzzle.position
                                             : (transform.position + transform.forward * 0.5f + Vector3.up * 1.0f);
        Vector3 dir = (targetPos - muzzlePos).normalized;

        GameObject proj = Instantiate(plasmaProjectilePrefab, muzzlePos, Quaternion.LookRotation(dir, Vector3.up));
        var no = proj.GetComponent<NetworkObject>();
        if (no == null) { Destroy(proj); return; }
        no.Spawn(true);

        var prb = proj.GetComponent<Rigidbody>();
        if (prb != null) prb.linearVelocity = dir * projectileSpeed;
    }

    // ---------- ground + bounds helpers ----------
    private float GetGroundYAt(Vector3 pos)
    {
        // Raycast first (handles meshes/terrain), fallback to NavMesh sample
        Vector3 origin = pos + Vector3.up * Mathf.Max(groundProbeUp, 0.1f);
        float maxDist = groundProbeUp + Mathf.Max(groundProbeDown, 0.1f);
        if (Physics.Raycast(origin, Vector3.down, out var hit, maxDist, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        if (NavMesh.SamplePosition(pos, out var navHit, 2.5f, NavMesh.AllAreas))
            return navHit.position.y;

        // last resort: keep current Y
        return pos.y;
    }

    private Bounds GetVisualBounds()
    {
        var rends = (fadeRenderers != null && fadeRenderers.Length > 0) ? fadeRenderers : GetComponentsInChildren<Renderer>(true);
        bool first = true;
        Bounds b = default;
        foreach (var r in rends)
        {
            if (!r) continue;
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        if (first) b = new Bounds(transform.position, Vector3.one * 1f);
        return b;
    }

    private float GetPivotToBottom()
    {
        var b = GetVisualBounds();
        return Mathf.Max(0.001f, transform.position.y - b.min.y); // distance from pivot to bottom of visuals
    }

    private float GetCurrentBottomClearance()
    {
        // bottom clearance = bottomY - groundY
        var b = GetVisualBounds();
        float bottomY = b.min.y;
        float groundY = GetGroundYAt(transform.position);
        float clearance = bottomY - groundY;

        // If agent is enabled/on mesh, this tends to equal (baseOffset - pivotToBottom). Clamp to small+.
        if (!float.IsFinite(clearance)) clearance = fallbackBottomClearance;
        return Mathf.Max(fallbackBottomClearance, clearance);
    }

    // ---------- Damage / KO / Wake / Death ----------
    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float dmg)
    {
        if (dead) return;
        if (invulnerable || currentState == State.Phasing || currentState == State.Carried) return;

        currentHealth = Mathf.Max(0, currentHealth - Mathf.Abs(dmg));

        if (currentState != State.Unconscious && currentHealth <= maxHealth * unconsciousThreshold)
        { GoUnconsciousServer(); return; }

        if (currentState == State.Unconscious && carrier == null && currentHealth <= 0f)
        { DieServer(); }
    }

    private void GoUnconsciousServer()
    {
        if (dead) return;
        currentState = State.Unconscious;

        StopBrain();

        if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }
        if (hitCollider != null) hitCollider.isTrigger = false;

        SetUnconsciousClientRpc();
    }

    [ClientRpc] private void SetUnconsciousClientRpc() { currentState = State.Unconscious; }

    private void WakeUpServer()
    {
        currentState = State.Idle;

        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        if (hitCollider != null) hitCollider.isTrigger = false;

        EnsureOnNavMesh();
        if (agent.enabled && agent.isOnNavMesh) agent.isStopped = false;

        StartPatrollingNow();
        WakeUpClientRpc();
    }

    [ClientRpc] private void WakeUpClientRpc() { if (currentState == State.Unconscious) currentState = State.Idle; }

    private void DieServer()
    {
        if (dead) return; dead = true;
        if (transform.parent) transform.SetParent(null, true);
        if (NetworkObject && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        else Destroy(gameObject);
    }

    // ---------- Pickup / Carry ----------
    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong requesterClientId)
    {
        if (currentState != State.Unconscious) return;

        var playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(requesterClientId);
        if (playerObj == null) return;

        carrier = playerObj;
        currentState = State.Carried;

        StopBrain(); // freeze while carried

        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        if (hitCollider != null) hitCollider.isTrigger = true; // lab needs trigger

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

        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        if (hitCollider != null) hitCollider.isTrigger = true;

        transform.SetParent(playerObj.transform, false);
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.identity;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ConvertToPlasmaServerRpc()
    {
        if (currentState != State.Carried) return;

        GameManager.Instance.AddPlasmaServerRpc(tier);

        transform.SetParent(null, true);
        if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
    }

    public bool IsCarriedServer() => IsServer && currentState == State.Carried;

    // ---------- NavMesh helpers ----------
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
            currentState = State.Idle;
        }
    }

    // ---------- Brain hard-stop ----------
    private void StopBrain()
    {
        StopAllCoroutines();
        attackLoopCo = null;

        if (agent.enabled) { agent.isStopped = true; agent.ResetPath(); agent.enabled = false; }

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
#if UNITY_6_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.linearVelocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }

        targetPlayer = null;
        invulnerable = false;
    }

    // ---------- Visuals ----------
    [ClientRpc]
    private void SetPhasedVisualsClientRpc(bool phased, float targetAlpha)
    {
        var rends = (fadeRenderers != null && fadeRenderers.Length > 0)
            ? fadeRenderers
            : GetComponentsInChildren<Renderer>(true);

        foreach (var r in rends)
        {
            if (!r) continue;
            foreach (var mat in r.materials)
            {
                if (!mat) continue;

                if (phased)
                {
                    mat.SetFloat("_Surface", 1);
                    mat.SetInt("_ZWrite", 0);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                else
                {
                    mat.SetFloat("_Surface", 0);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = -1;
                }

                if (mat.HasProperty("_BaseColor"))
                {
                    var c = mat.GetColor("_BaseColor"); c.a = targetAlpha; mat.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    var c = mat.color; c.a = targetAlpha; mat.color = c;
                }
            }
        }
    }

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
