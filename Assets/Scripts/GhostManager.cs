using System.Collections.Generic;
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
    [Header("Ghost Position and Initial Movement")]
    [SerializeField] private List<Transform> ghostSpawnPoints = new();
    [SerializeField] private List<PatrolRoute> patrolRoutes = new();
    [Header("Prefabs")] 
    [SerializeField] private List <GameObject> ghostPrefabs;
    
    private readonly List<NetworkObject> spawnedGhosts = new();
    private int currentRound = 0;
    //private int ghostCount = 0;
    public void SetRoundValue(int round)
    {
        currentRound = round;
    }
    private void StartRoundServer()
    {
        int ghostCount = baseGhosts + ghostsPerRound * (currentRound - 1);
        SpawnGhostServer(ghostCount);

    }

    private void SpawnGhostServer(int count)
    {
        Debug.Log("wat");
        if (ghostPrefabs.Count == 0 || ghostSpawnPoints.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            GameObject prefabOfChoice = ghostPrefabs[Random.Range(0, ghostPrefabs.Count)];
            Transform spawnPoint = ghostSpawnPoints[Random.Range(0, ghostSpawnPoints.Count)];
            GameObject currPrefab = Instantiate(prefabOfChoice, spawnPoint.position, spawnPoint.rotation);
            GhostAI currGhostAI = currPrefab.GetComponent<GhostAI>();

            if (patrolRoutes.Count > 0 && currGhostAI != null)
            {
                PatrolRoute route = patrolRoutes[Random.Range(0, patrolRoutes.Count)];
                currGhostAI.SetPatrolPath(route.Points);
            }
            
            NetworkObject netObj = currPrefab.GetComponent<NetworkObject>();
            if (netObj == null) {Destroy(currPrefab); continue;}
            
            netObj.Spawn(true);
            spawnedGhosts.Add(netObj);
            
        }
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
