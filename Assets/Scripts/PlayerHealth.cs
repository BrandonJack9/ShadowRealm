using Unity.Netcode;
using UnityEngine;

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

    [Tooltip("Mirror of server health for inspector/debug. Do not change at runtime.")]
    public float CurrentHealth;

    public bool IsKO => health.Value <= 0f;
    public float MaxHealth => maxHealth;

    private Coroutine hudInitRoutine;

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

        UpdateMirrorAndKOState(health.Value);

        health.OnValueChanged += (_, newVal) =>
        {
            UpdateMirrorAndKOState(newVal);
        };

        if (IsOwner) StartOwnerHudInitRoutine();
    }

    public override void OnGainedOwnership()
    {
        StartOwnerHudInitRoutine();
    }

    private void StartOwnerHudInitRoutine()
    {
        if (hudInitRoutine != null) StopCoroutine(hudInitRoutine);
        hudInitRoutine = StartCoroutine(InitHUDWhenReady());
    }

    private System.Collections.IEnumerator InitHUDWhenReady()
    {
        // Wait until UIManager exists this frame (scene load order safe)
        while (UIManager.Instance == null) yield return null;

        UIManager.Instance.SetHealthBarVisible(true, prefillFull: true); // show + full immediately
        UIManager.Instance.UpdateHealthBar(health.Value, maxHealth);     // push real value (likely full)
    }

    // (kept to match your prior intent)
    private new void OnDestroy()
    {
        health.OnValueChanged -= (_, __) => { };
        if (hudInitRoutine != null) StopCoroutine(hudInitRoutine);

        if (IsOwner)
        {
            UIManager.Instance?.SetHealthBarVisible(false);
        }
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

        if (IsOwner)
        {
            UIManager.Instance?.UpdateHealthBar(CurrentHealth, maxHealth);
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
        }
    }

    [ClientRpc]
    private void OnKOClientRpc()
    {
        if (IsOwner)
        {
            var net = GetComponent<PlayerNetwork>();
            if (net != null)
                net.SetInputEnabled(false);
        }
    }

    // ---------------- REVIVE ----------------
    public void ServerReviveImmediate()
    {
        if (!IsServer || !IsKO) return;

        health.Value = Mathf.Clamp(maxHealth * 0.5f, 1f, maxHealth);
        GameManager.Instance?.NotifyPlayerRevivedServerRpc(OwnerClientId);
        OnReviveClientRpc();
    }

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

            UIManager.Instance?.UpdateHealthBar(health.Value, maxHealth);
        }
    }
}
