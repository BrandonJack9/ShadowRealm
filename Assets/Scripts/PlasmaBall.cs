using Unity.Netcode;
using UnityEngine;

public class PlasmaBall : NetworkBehaviour
{
    public float speed = 10f;
    public float lifetime = 10f;

    private float timer = 0f;

    void Update()
    {
        if (!IsServer) return;

        // Move forward on server
        transform.position += transform.forward * speed * Time.deltaTime;

        // Lifetime countdown
        timer += Time.deltaTime;
        if (timer >= lifetime)
        {
            GetComponent<NetworkObject>().Despawn();
        }
    }
}
