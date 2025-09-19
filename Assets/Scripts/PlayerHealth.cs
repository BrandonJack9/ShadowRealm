using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach to player prefab. Handles taking damage, KO, and revive.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(KnockoutReporter))]
public class PlayerHealth : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    // Server-authoritative health; everyone reads, only server writes.
    private NetworkVariable<float> health = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private KnockoutReporter knockout;

    /// <summary>
    /// Shown in the Inspector so you can watch it live.
    /// Mirrors the networked value 'health.Value'. Do not edit at runtime.
    /// </summary>
    [Tooltip("Mirror of server health for inspector/debug. Do not change at runtime.")]
    public float CurrentHealth;

    public bool IsKO => health.Value <= 0f;

    private void Awake()
    {
        knockout = GetComponent<KnockoutReporter>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            health.Value = Mathf.Clamp(health.Value <= 0 ? maxHealth : health.Value, 0f, maxHealth);
        }

        // Keep mirror field and KO state in sync for all peers.
        UpdateMirrorAndKOState(health.Value);
        health.OnValueChanged += (_, newVal) =>
        {
            UpdateMirrorAndKOState(newVal);
        };
    }

    // ✅ FIX: silence CS0114 (we're not overriding Netcode's internal OnDestroy)
    private new void OnDestroy()
    {
        health.OnValueChanged -= (_, __) => { }; // (matches your prior intent)
    }

    private void UpdateMirrorAndKOState(float newVal)
    {
        CurrentHealth = newVal;

        if (newVal <= 0f)
        {
            knockout?.SetKO(true);
            OnKOClientRpc();
        }
        else
        {
            knockout?.SetKO(false);
        }
    }

    // ---------------- DAMAGE ----------------

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float dmg)
    {
        if (IsKO) return;
        health.Value = Mathf.Max(0f, health.Value - Mathf.Max(0f, dmg));

        if (health.Value <= 0f)
        {
            GameManager.Instance?.NotifyPlayerKOdServerRpc(OwnerClientId);
            // UpdateMirrorAndKOState is invoked by OnValueChanged for everyone.
        }
    }

    [ClientRpc]
    private void OnKOClientRpc()
    {
        // Owner loses input when KO.
        if (IsOwner)
        {
            var net = GetComponent<PlayerNetwork>();
            if (net != null)
                net.SetInputEnabled(false);
        }
    }

    // ---------------- REVIVE ----------------
    // Server-side immediate revive (used by PlayerNetwork's server RPC after validation).
    public void ServerReviveImmediate()
    {
        if (!IsServer) return;
        if (!IsKO) return;

        health.Value = Mathf.Clamp(maxHealth * 0.5f, 1f, maxHealth); // bring back at 50%
        GameManager.Instance?.NotifyPlayerRevivedServerRpc(OwnerClientId);
        OnReviveClientRpc();
    }

    // ✅ NEW: used by restart to fully heal players
    public void ServerFullHeal()
    {
        if (!IsServer) return;

        health.Value = maxHealth;
        knockout?.SetKO(false);

        GameManager.Instance?.NotifyPlayerRevivedServerRpc(OwnerClientId);
        OnReviveClientRpc();
    }

    [ClientRpc]
    private void OnReviveClientRpc()
    {
        if (IsOwner)
        {
            var net = GetComponent<PlayerNetwork>();
            if (net != null)
                net.SetInputEnabled(true);
        }
    }
}
