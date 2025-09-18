using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach this to player prefab. Handles taking damage, KO, revive.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(KnockoutReporter))]
public class PlayerHealth : NetworkBehaviour
{
    [SerializeField] private float maxHealth = 100f;

    private float currentHealth;
    private KnockoutReporter knockout;

    public bool IsKO => currentHealth <= 0;

    private void Awake()
    {
        currentHealth = maxHealth;
        knockout = GetComponent<KnockoutReporter>();
    }

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float dmg)
    {
        if (IsKO) return;

        currentHealth = Mathf.Max(0, currentHealth - dmg);

        if (currentHealth <= 0)
        {
            knockout.SetKO(true);
            OnKOClientRpc();
        }
    }

    [ClientRpc]
    private void OnKOClientRpc()
    {
        // TODO: play KO animation, disable controls, etc.
        Debug.Log($"{name} was knocked out!");
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReviveServerRpc()
    {
        if (!IsKO) return;

        currentHealth = maxHealth;
        knockout.SetKO(false);
        OnReviveClientRpc();
    }

    [ClientRpc]
    private void OnReviveClientRpc()
    {
        // TODO: play revive animation
        Debug.Log($"{name} revived!");
    }
}
