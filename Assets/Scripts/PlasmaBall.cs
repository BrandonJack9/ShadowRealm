using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(Collider))]
public class PlasmaBall : NetworkBehaviour
{
    [SerializeField] private float speed = 22f;
    [SerializeField] private float damage = 20f;
    [SerializeField] private float lifetime = 3f;

    private void Start()
    {
        if (IsServer)
            Invoke(nameof(Despawn), lifetime);
    }

    private void FixedUpdate()
    {
        if (IsServer)
            transform.Translate(Vector3.forward * speed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // ✅ First, check for Ghosts
        var ghost = other.GetComponentInParent<GhostAI>();
        if (ghost != null)
        {
            ghost.TakeDamageServerRpc(damage);
            Despawn();
            return;
        }

        // (Optional) check for players
        /*
        var player = other.GetComponentInParent<PlayerHealth>();
        if (player != null)
        {
            player.TakeDamageServerRpc(damage);
            Despawn();
            return;
        }
        */

        // Anything else we hit -> despawn anyway
        Despawn();
    }

    private void Despawn()
    {
        if (NetworkObject && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }
}
