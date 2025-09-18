using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach this to your networked "Next Round" console prefab.
/// Players walk into its trigger and press E to request end-of-round.
/// Host then presses the UI button to start the next round.
/// </summary>
[RequireComponent(typeof(NetworkObject), typeof(Collider))]
public class NextRoundConsole : NetworkBehaviour
{
    [Tooltip("If true, shows a simple floating prompt gizmo when a local player is in range")]
    [SerializeField] private bool showPromptGizmo = true;

    private readonly HashSet<ulong> _playersInTrigger = new();

    private void OnTriggerEnter(Collider other)
    {
        var no = other.GetComponentInParent<NetworkObject>();
        if (no != null && no.IsPlayerObject)
        {
            _playersInTrigger.Add(no.OwnerClientId);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var no = other.GetComponentInParent<NetworkObject>();
        if (no != null && no.IsPlayerObject)
        {
            _playersInTrigger.Remove(no.OwnerClientId);
        }
    }

    private void Update()
    {
        if (!IsOwnerLocalPlayerInTrigger()) return;
        if (Input.GetKeyDown(KeyCode.E))
        {
            // Any player can request to end round; server validates & moves to End-of-Round
            GameManager.Instance.RequestEndRoundServerRpc();
        }
    }

    private bool IsOwnerLocalPlayerInTrigger()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return false;

        var localId = NetworkManager.Singleton.LocalClientId;
        return _playersInTrigger.Contains(localId);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showPromptGizmo) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.35f);
        Gizmos.DrawCube(Vector3.up * 1.0f, new Vector3(1.5f, 2f, 1.5f));
    }
#endif
}
