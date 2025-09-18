using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum RoundState { Idle, Playing, RoundEnded, Defeat }

    [Header("UI References")]
    [SerializeField] private GameObject lobbyCanvas;  // assign in inspector

    [Header("Prefabs & Scene References")]
    [SerializeField] private List<GameObject> ghostPrefabs = new();
    [SerializeField] private List<Transform> ghostSpawnPoints = new();
    [SerializeField] private List<PatrolRoute> patrolRoutes = new();
    [SerializeField] private GameObject nextRoundConsolePrefab;
    [SerializeField] private Transform nextRoundConsoleSpawn;

    [Header("Round Scaling")]
    [SerializeField] private int baseGhosts = 6;
    [SerializeField] private int ghostsPerRound = 3;
    [SerializeField] private float baseTimeSeconds = 120f;
    [SerializeField] private float timePerRound = 30f;
    [SerializeField] private int basePlasmaThreshold = 10;
    [SerializeField] private int plasmaPerRound = 10;

    // ---------------- Networked state ----------------
    public NetworkVariable<int> Round = new(1);
    public NetworkVariable<int> PlasmaThisRound = new(0);
    public NetworkVariable<int> PlasmaThreshold = new(0);
    public NetworkVariable<float> TimeRemaining = new(0);
    public NetworkVariable<RoundState> State = new(RoundState.Idle);

    // ---------------- Runtime ----------------
    private readonly List<NetworkObject> spawnedGhosts = new();
    private NetworkObject nextRoundConsole;
    private HashSet<ulong> knockedOutClients = new();
    private Coroutine timerRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ApplyRoundScaling();
            StartRoundServer();
        }
    }

    // ---------------- Round Flow ----------------
    private void ApplyRoundScaling()
    {
        PlasmaThreshold.Value = basePlasmaThreshold + plasmaPerRound * (Round.Value - 1);
        TimeRemaining.Value = baseTimeSeconds + timePerRound * (Round.Value - 1);
    }

    private void StartRoundServer()
    {
        CleanupRoundServer();

        PlasmaThisRound.Value = 0;
        State.Value = RoundState.Playing;

        // ✅ Disable the lobby canvas so its input fields/buttons can't steal focus
        if (lobbyCanvas != null)
            lobbyCanvas.SetActive(false);

        int ghostCount = baseGhosts + ghostsPerRound * (Round.Value - 1);
        SpawnGhostsServer(ghostCount);

        if (timerRoutine != null) StopCoroutine(timerRoutine);
        timerRoutine = StartCoroutine(RoundTimerRoutine());

        HideAllPanelsClientRpc();
        RefreshHUDClientRpc();
    }

    private void EndRoundServer(bool victory)
    {
        if (State.Value == RoundState.Defeat) return;
        State.Value = RoundState.RoundEnded;

        if (timerRoutine != null) { StopCoroutine(timerRoutine); timerRoutine = null; }

        DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();

        if (victory)
            ShowEndOfRoundPanelClientRpc();
    }

    private void DefeatServer()
    {
        if (State.Value == RoundState.Defeat) return;
        State.Value = RoundState.Defeat;

        if (timerRoutine != null) { StopCoroutine(timerRoutine); timerRoutine = null; }

        DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();

        ShowDefeatPanelClientRpc();
    }

    private void CleanupRoundServer()
    {
        DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();
        knockedOutClients.Clear();
    }

    private IEnumerator RoundTimerRoutine()
    {
        while (TimeRemaining.Value > 0 && State.Value == RoundState.Playing)
        {
            yield return new WaitForSeconds(1f);
            TimeRemaining.Value--;
            RefreshHUDClientRpc();

            if (AllPlayersKOdServer())
            {
                DefeatServer();
                yield break;
            }
        }

        if (State.Value == RoundState.Playing)
            EndRoundServer(victory: true);
    }

    // ---------------- Ghost Spawning ----------------
    private void SpawnGhostsServer(int count)
    {
        if (ghostPrefabs.Count == 0 || ghostSpawnPoints.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            var prefab = ghostPrefabs[Random.Range(0, ghostPrefabs.Count)];
            var spawn = ghostSpawnPoints[Random.Range(0, ghostSpawnPoints.Count)];

            var go = Instantiate(prefab, spawn.position, spawn.rotation);
            var ghostAI = go.GetComponent<GhostAI>();

            if (patrolRoutes.Count > 0)
            {
                PatrolRoute route = patrolRoutes[Random.Range(0, patrolRoutes.Count)];
                ghostAI.SetPatrolPath(route.Points);
            }

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null) { Destroy(go); continue; }

            netObj.Spawn(true);
            spawnedGhosts.Add(netObj);
        }
    }

    private void DespawnAllGhostsServer()
    {
        for (int i = spawnedGhosts.Count - 1; i >= 0; i--)
        {
            var netObj = spawnedGhosts[i];
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(true);
        }
        spawnedGhosts.Clear();
    }

    // ---------------- Plasma & Next Round ----------------
    [ServerRpc(RequireOwnership = false)]
    public void AddPlasmaServerRpc(int points)
    {
        if (State.Value != RoundState.Playing && State.Value != RoundState.RoundEnded) return;

        PlasmaThisRound.Value += Mathf.Max(0, points);
        RefreshHUDClientRpc();

        if (PlasmaThisRound.Value >= PlasmaThreshold.Value)
            TrySpawnNextRoundConsole();
    }

    private void TrySpawnNextRoundConsole()
    {
        if (nextRoundConsole != null && nextRoundConsole.IsSpawned) return;
        if (nextRoundConsolePrefab == null || nextRoundConsoleSpawn == null) return;

        var go = Instantiate(nextRoundConsolePrefab, nextRoundConsoleSpawn.position, nextRoundConsoleSpawn.rotation);
        var no = go.GetComponent<NetworkObject>();
        if (no == null) { Destroy(go); return; }

        no.Spawn(true);
        nextRoundConsole = no;
    }

    private void DespawnNextRoundConsoleIfAny()
    {
        if (nextRoundConsole != null && nextRoundConsole.IsSpawned)
            nextRoundConsole.Despawn(true);
        nextRoundConsole = null;
    }

    // ---------------- Player KO / Defeat ----------------
    [ServerRpc(RequireOwnership = false)]
    public void NotifyPlayerKOdServerRpc(ulong clientId)
    {
        knockedOutClients.Add(clientId);
        if (AllPlayersKOdServer()) DefeatServer();
    }

    [ServerRpc(RequireOwnership = false)]
    public void NotifyPlayerRevivedServerRpc(ulong clientId)
    {
        knockedOutClients.Remove(clientId);
    }

    private bool AllPlayersKOdServer()
    {
        var active = NetworkManager.Singleton.ConnectedClientsIds;
        if (active.Count == 0) return false;

        foreach (var id in active)
            if (!knockedOutClients.Contains(id))
                return false;

        return true;
    }

    // ---------------- Round Transitions ----------------
    [ServerRpc(RequireOwnership = false)]
    public void RequestEndRoundServerRpc()
    {
        if (State.Value == RoundState.Playing)
            EndRoundServer(victory: true);
    }

    [ServerRpc(RequireOwnership = false)]
    public void HostStartNextRoundServerRpc()
    {
        if (!IsServer) return;
        if (State.Value != RoundState.RoundEnded) return;

        Round.Value++;
        ApplyRoundScaling();
        StartRoundServer();
    }

    [ServerRpc(RequireOwnership = false)]
    public void HostReturnToLobbyServerRpc()
    {
        if (!IsServer) return;

        if (lobbyCanvas != null)
            lobbyCanvas.SetActive(true);

        Round.Value = 1;
        ApplyRoundScaling();
        PlasmaThisRound.Value = 0;
        State.Value = RoundState.Idle;

        DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();

        HideAllPanelsClientRpc();
        RefreshHUDClientRpc();
    }

    // ---------------- UI RPCs ----------------
    [ClientRpc]
    private void ShowDefeatPanelClientRpc()
    {
        Debug.Log("[UI] Show Defeat Panel");
        UIManager.Instance?.ShowDefeatPanel();
    }

    [ClientRpc]
    private void ShowEndOfRoundPanelClientRpc()
    {
        Debug.Log("[UI] Show End Of Round Panel");
        UIManager.Instance?.ShowEndOfRoundPanel();
    }

    [ClientRpc]
    private void HideAllPanelsClientRpc()
    {
        UIManager.Instance?.HideAllPanels();
    }

    [ClientRpc]
    private void RefreshHUDClientRpc()
    {
        Debug.Log($"[UI] RefreshHUD → Plasma {PlasmaThisRound.Value}/{PlasmaThreshold.Value}, Round {Round.Value}, Time {TimeRemaining.Value}");
        UIManager.Instance?.RefreshHUD(
            PlasmaThisRound.Value,
            PlasmaThreshold.Value,
            Round.Value,
            TimeRemaining.Value
        );
    }
}
