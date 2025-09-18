using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(GravityBody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Planet Movement")]
    public float moveSpeed = 6f;
    public float acceleration = 14f;
    public float airControlMultiplier = 0.5f;

    [Header("Jump")]
    public float jumpForce = 6f;
    public float jumpHoldForce = 18f;
    public float jumpHoldDuration = 0.25f;

    [Header("Jetpack")]
    public float jetpackThrust = 12f;   // up/down thrust force
    public float jetpackFuel = 100f;    // optional fuel system
    public float fuelRegenRate = 15f;

    [Header("Space (FPS-style jetpack)")]
    public float spaceAcceleration = 10f;
    public float spaceMaxSpeed = 10f;
    public float spaceDamping = 1.0f;
    public float spaceVerticalDamping = 2f;

    private Rigidbody rb;
    private GravityBody grav;

    private Vector3 desiredVelPlanetWS = Vector3.zero;
    private Vector3 desiredVelSpaceWS = Vector3.zero;

    private bool isJumping;
    private float jumpTime;

    // Input jetpack
    private bool thrustUp;
    private bool thrustDown;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        grav = GetComponent<GravityBody>();
    }

    public void SetDesiredVelocityPlanet(Vector3 worldVelocity) => desiredVelPlanetWS = worldVelocity;
    public void SetDesiredVelocitySpace(Vector3 worldVelocity) => desiredVelSpaceWS = worldVelocity;

    public void SetJetpackInputs(bool up, bool down)
    {
        thrustUp = up;
        thrustDown = down;
    }

    public void Jump()
    {
        if (!grav.IsGrounded()) return;

        Vector3 up = grav.GetSurfaceNormal();
        Vector3 v = rb.linearVelocity;
        v -= Vector3.Project(v, up);
        rb.linearVelocity = v;

        rb.AddForce(up * jumpForce, ForceMode.VelocityChange);

        isJumping = true;
        jumpTime = 0f;
        grav.TemporarilyDisableGravity(jumpHoldDuration);
    }

    public void HoldJump(bool held)
    {
        if (!held || !isJumping) return;
        if (jumpTime >= jumpHoldDuration) return;

        Vector3 up = grav.GetSurfaceNormal();
        rb.AddForce(up * jumpHoldForce * Time.fixedDeltaTime, ForceMode.Acceleration);
        jumpTime += Time.fixedDeltaTime;
    }

    void FixedUpdate()
    {
        if (grav.IsGrounded()) isJumping = false;

        if (grav.IsInGravity())
        {
            // Tangent locomotion
            Vector3 up = grav.GetSurfaceNormal();
            Vector3 lateral = Vector3.ProjectOnPlane(rb.linearVelocity, up);
            Vector3 delta = desiredVelPlanetWS - lateral;
            float control = grav.IsGrounded() ? 1f : airControlMultiplier;
            rb.AddForce(delta * acceleration * control, ForceMode.Acceleration);

            // Jetpack while airborne
            if (!grav.IsGrounded())
            {
                if (thrustUp) rb.AddForce(up * jetpackThrust, ForceMode.Acceleration);
                if (thrustDown) rb.AddForce(-up * jetpackThrust, ForceMode.Acceleration);
            }
        }
        else
        {
            // Space jetpack (FPS-style)
            Vector3 velXZ = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
            Vector3 deltaXZ = desiredVelSpaceWS - velXZ;
            rb.AddForce(deltaXZ * spaceAcceleration, ForceMode.Acceleration);

            if (desiredVelSpaceWS.sqrMagnitude < 0.0001f)
                rb.AddForce(-velXZ * spaceDamping, ForceMode.Acceleration);

            float vy = rb.linearVelocity.y;
            rb.AddForce(Vector3.down * vy * spaceVerticalDamping, ForceMode.Acceleration);

            // Jetpack in space
            if (thrustUp) rb.AddForce(Vector3.up * jetpackThrust, ForceMode.Acceleration);
            if (thrustDown) rb.AddForce(Vector3.down * jetpackThrust, ForceMode.Acceleration);

            float spd = rb.linearVelocity.magnitude;
            if (spd > spaceMaxSpeed) rb.linearVelocity *= (spaceMaxSpeed / spd);
        }
    }
}
