using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class LabTrigger : NetworkBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var ghost = other.GetComponentInParent<GhostAI>();
        if (ghost != null && ghost.IsCarriedServer())
        {
            ghost.ConvertToPlasmaServerRpc();
        }
    }
}
