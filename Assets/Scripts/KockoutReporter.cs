using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Put this on your Player root (same object that has the Player's NetworkObject).
/// Call SetKO(true/false) from your health/anim code when the player is knocked out or revived.
/// It notifies the server (GameManager) to track defeat conditions.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class KnockoutReporter : NetworkBehaviour
{
    public bool IsKO { get; private set; }

    public void SetKO(bool ko)
    {
        if (IsKO == ko) return;
        IsKO = ko;

        if (IsOwner) // only the owning client reports own state
        {
            if (ko)
                GameManager.Instance.NotifyPlayerKOdServerRpc(OwnerClientId);
            else
                GameManager.Instance.NotifyPlayerRevivedServerRpc(OwnerClientId);
        }
    }
}
