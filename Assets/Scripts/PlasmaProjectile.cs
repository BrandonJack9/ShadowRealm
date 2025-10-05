using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative plasma projectile:
/// - Spawns by server (from GhostAI).
/// - Moves via Rigidbody velocity (assigned by spawner).
/// - Damages PlayerHealth on hit (server-side).
/// - Ignores ghosts (won't damage or collide end-effect with them).
/// - Despawns itself after first valid hit or when lifetime expires.
/// 
/// Requirements on prefab:
/// - NetworkObject
/// - Rigidbody (Use Gravity: false recommended; Collision Detection: Continuous)
/// - Collider (set as Trigger recommended; e.g., SphereCollider isTrigger = true)
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public class PlasmaProjectile : NetworkBehaviour
{
    [Header("Damage")]
    [SerializeField] private float damage = 10f;

    [Header("Lifetime")]
    [SerializeField] private float lifetimeSeconds = 6f;

    [Header("Hit Filtering")]
    [Tooltip("Layers considered 'solid' for despawn when no PlayerHealth is hit. Example: Default, Environment.")]
    [SerializeField] private LayerMask environmentMask = ~0; // everything by default
    [Tooltip("Optional: layers that contain ghosts to ignore on trigger enter.")]
    [SerializeField] private LayerMask ghostMask;

    [Header("Impact FX (optional, non-networked)")]
    [SerializeField] private GameObject impactVfxPrefab;
    [SerializeField] private float impactVfxLifetime = 1.5f;

    private Rigidbody rb;
    private Collider myCol;
    private float spawnTime;
    private bool consumed;

    // For fast-movers, do a tiny sphere cast between frames (works even if collider is trigger).
    private Vector3 lastPos;
    [SerializeField] private float sweepRadius = 0.15f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        myCol = GetComponent<Collider>();
        if (rb) rb.interpolation = RigidbodyInterpolation.Interpolate;
        spawnTime = Time.time;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        lastPos = transform.position;
    }

    private void Update()
    {
        if (!IsServer) return;

        // Lifetime
        if (Time.time - spawnTime >= lifetimeSeconds)
        {
            Despawn();
            return;
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Sweep check to catch tunneling (useful when collider is trigger + high velocity)
        Vector3 pos = transform.position;
        Vector3 delta = pos - lastPos;
        float dist = delta.magnitude;

        if (dist > 0.0001f)
        {
            RaycastHit hit;
            if (Physics.SphereCast(lastPos, sweepRadius, delta.normalized, out hit, dist,
                environmentMask, QueryTriggerInteraction.Collide))
            {
                HandleHit(hit.collider, hit.point, hit.normal);
            }
        }

        lastPos = pos;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        HandleHit(other, transform.position, -transform.forward);
    }

    private void HandleHit(Collider other, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (consumed || other == null) return;

        // Ignore self / other projectiles / triggers that are not meaningful
        if (other == myCol) return;

        // Ignore ghosts entirely (no friendly fire on ghosts)
        if (IsInLayerMask(other.gameObject.layer, ghostMask))
        {
            return;
        }

        // Player hit?
        var ph = other.GetComponentInParent<PlayerHealth>();
        if (ph != null && !ph.IsKO)
        {
            ph.TakeDamageServerRpc(damage);
            SpawnImpactFx(hitPoint, hitNormal);
            Despawn();
            return;
        }

        // If it's solid environment (or anything in environmentMask), we pop.
        if (IsInLayerMask(other.gameObject.layer, environmentMask))
        {
            SpawnImpactFx(hitPoint, hitNormal);
            Despawn();
            return;
        }

        // Otherwise ignore (e.g., harmless triggers).
    }

    private void SpawnImpactFx(Vector3 pos, Vector3 normal)
    {
        if (impactVfxPrefab == null) return;

        // VFX is local-only; it's okay if clients don't see identical timing
        // If you want fully synced VFX, spawn a NetworkObject FX instead.
        var vfx = Instantiate(impactVfxPrefab, pos, Quaternion.LookRotation(normal, Vector3.up));
        Destroy(vfx, impactVfxLifetime);
    }

    private void Despawn()
    {
        if (consumed) return;
        consumed = true;

        // Stop moving to avoid extra hits during despawn latency
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject);
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, sweepRadius);
    }
#endif
}
