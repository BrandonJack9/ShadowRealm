using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityBody : MonoBehaviour
{
    public LayerMask groundMask = ~0;
    public float extraGroundDistance = 0.2f;
    [Range(0.5f, 1f)] public float groundRadiusScale = 0.9f;

    private GravitySource currentSource;
    private GravitySource lastSource;
    private float disableGravityUntil;
    private bool grounded;

    private Rigidbody rb;
    private CapsuleCollider capsule;

    // Transition helper
    public bool IsInGravity() => currentSource != null;
    public bool IsGrounded() => grounded;

    public GravitySource GetCurrentSource() => currentSource;

    public void SetCurrentGravitySource(GravitySource source)
    {
        currentSource = source;
        if (source != null) lastSource = source;
    }

    public Vector3 GetSurfaceNormal()
    {
        if (currentSource)
            return (transform.position - currentSource.transform.position).normalized;
        if (lastSource) // fallback for smooth exit
            return (transform.position - lastSource.transform.position).normalized;
        return Vector3.up;
    }

    public void TemporarilyDisableGravity(float duration) => disableGravityUntil = Time.time + duration;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        capsule = GetComponent<CapsuleCollider>();
    }

    void GetCapsuleWorldEnds(out Vector3 a, out Vector3 b, out float radius)
    {
        float scaleY = Mathf.Abs(transform.lossyScale.y);
        float scaleXZ = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float r = (capsule ? capsule.radius : 0.5f) * scaleXZ;
        float h = (capsule ? Mathf.Max(capsule.height * scaleY, 2f * r + 0.001f) : 2f);

        Vector3 up = transform.up;
        Vector3 center = transform.TransformPoint(capsule ? capsule.center : Vector3.zero);
        float half = h * 0.5f - r;

        a = center + up * half;
        b = center - up * half;
        radius = r * groundRadiusScale;
    }

    bool CheckGrounded()
    {
        if (currentSource == null) return false;

        GetCapsuleWorldEnds(out Vector3 top, out Vector3 bottom, out float r);
        Vector3 up = GetSurfaceNormal();
        Vector3 down = -up;
        float castDist = extraGroundDistance + 0.02f;

        return Physics.CapsuleCast(top, bottom, r, down, out _, castDist, groundMask, QueryTriggerInteraction.Ignore);
    }

    void FixedUpdate()
    {
        grounded = CheckGrounded();

        if (currentSource == null || Time.time < disableGravityUntil) return;

        Vector3 dirToCenter = (currentSource.transform.position - transform.position).normalized;
        rb.AddForce(dirToCenter * currentSource.gravityStrength, ForceMode.Acceleration);
    }
}
