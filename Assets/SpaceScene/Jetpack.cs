using UnityEngine;

public class Jetpack : MonoBehaviour
{
    public float maxFuel = 100f;
    public float burnRate = 20f;
    public float rechargeRate = 10f;

    public bool Active { get; private set; }
    public float Fuel { get; private set; }

    void Start() => Fuel = maxFuel;

    public void SetActive(bool active)
    {
        Active = active && Fuel > 0f;
    }

    void Update()
    {
        if (Active)
            Fuel = Mathf.Max(0f, Fuel - burnRate * Time.deltaTime);
        else
            Fuel = Mathf.Min(maxFuel, Fuel + rechargeRate * Time.deltaTime);
    }
}
