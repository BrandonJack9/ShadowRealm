using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SphereCollider))]
public class GravitySource : MonoBehaviour
{
    public float gravityStrength = 9.8f;
    private readonly List<GravityBody> bodiesInField = new List<GravityBody>();

    private void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        var body = rb ? rb.GetComponent<GravityBody>() : null;
        if (body != null && !bodiesInField.Contains(body))
        {
            bodiesInField.Add(body);
            body.SetCurrentGravitySource(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        var body = rb ? rb.GetComponent<GravityBody>() : null;
        if (body != null && bodiesInField.Contains(body))
        {
            bodiesInField.Remove(body);
            if (body.GetCurrentSource() == this)
                body.SetCurrentGravitySource(null);
        }
    }
}
