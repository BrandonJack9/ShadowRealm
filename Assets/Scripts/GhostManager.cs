using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class GhostManager : NetworkBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Ghost Spawning Variables")]
    [SerializeField] private int baseGhosts = 6;
    [SerializeField] private int ghostsPerRound = 3;
    [SerializeField] private int ghostsPerRoundRound = 3;
    
    [Header("Prefabs")] [SerializeField] private GameObject ghostPrefab;
    private int currentRound = 0;
    //private int ghostCount = 0;
    public void SetRoundValue(int round)
    {
        currentRound = round;
    }
    private void StartRoundServer()
    {
        int ghostCount = baseGhosts + ghostsPerRound * (currentRound - 1);
        SpawnGhostServer(currentRound);

    }

    public void SpawnGhostServer(int count)
    {
        
    }

    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            StartRoundServer();
        }
    }
    
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
