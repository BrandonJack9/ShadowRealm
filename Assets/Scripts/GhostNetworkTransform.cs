using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public class GhostNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return true; // 👈 Server controls ghosts
    }
}
