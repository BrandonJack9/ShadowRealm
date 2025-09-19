using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(NetworkObject))]
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum RoundState { Idle, Playing, RoundEnded, Defeat }
    [Header("Managers")]
    [SerializeField] private GhostManager ghostManager;
    [Header("Prefabs & Scene References")]
    // [SerializeField] private List<GameObject> ghostPrefabs = new();
    // [SerializeField] private List<Transform> ghostSpawnPoints = new();
    //[SerializeField] private List<PatrolRoute> patrolRoutes = new();
    [SerializeField] private GameObject nextRoundConsolePrefab;
    [SerializeField] private Transform nextRoundConsoleSpawn;

    [Header("Player Spawns")] // ✅ NEW
    [SerializeField] private List<Transform> playerSpawnPoints = new();

    [Header("UI References")]
    [SerializeField] private GameObject lobbyCanvas;
    [SerializeField] private GameObject hudCanvas;
    [SerializeField] private TMPro.TMP_Text joinCodeText;    // Lives on Lobby
    [SerializeField] private TMPro.TMP_Text hudJoinCodeText; // Lives on HUD

    [Header("Round Scaling")]
    [SerializeField] private float baseTimeSeconds = 120f;
    [SerializeField] private float timePerRound = 30f;
    [SerializeField] private int basePlasmaThreshold = 10;
    [SerializeField] private int plasmaPerRound = 10;

    public NetworkVariable<int> Round = new(1);
    public NetworkVariable<int> PlasmaThisRound = new(0);
    public NetworkVariable<int> PlasmaThreshold = new(0);
    public NetworkVariable<float> TimeRemaining = new(0);
    public NetworkVariable<RoundState> State = new(RoundState.Idle);
    
    //private readonly List<NetworkObject> spawnedGhosts = new();
    private NetworkObject nextRoundConsole;
    private HashSet<ulong> knockedOutClients = new();
    private Coroutine timerRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        //ghostManager.SetRoundValue(Round.Value);
    }

    public override void OnNetworkSpawn()
    {

        // SERVER: initialize round and start
        if (IsServer)
        {
            ApplyRoundScaling();
            StartRoundServer();
        }

        // CLIENT: keep local UI/HUD in sync with NVs
        if (IsClient)
        {
            ApplyLocalUIFromState(State.Value);

            PlasmaThisRound.OnValueChanged += (_, __) => SafeRefreshHUD();
            PlasmaThreshold.OnValueChanged += (_, __) => SafeRefreshHUD();
            Round.OnValueChanged += (_, __) => SafeRefreshHUD();
            TimeRemaining.OnValueChanged += (_, __) => SafeRefreshHUD();

            State.OnValueChanged += (_, newState) => ApplyLocalUIFromState(newState);

            SafeRefreshHUD();
        }
    }

    private void ApplyRoundScaling()
    {
        PlasmaThreshold.Value = basePlasmaThreshold + plasmaPerRound * (Round.Value - 1);
        TimeRemaining.Value = baseTimeSeconds + timePerRound * (Round.Value - 1);
    }

    // -------------------- ROUND LIFECYCLE (SERVER) --------------------

    private void StartRoundServer()
    {
        CleanupRoundServer();

        PlasmaThisRound.Value = 0;
        State.Value = RoundState.Playing;

        // Flip all clients to gameplay UI
        var code = joinCodeText != null ? joinCodeText.text : string.Empty;
        EnterGameplayUIClientRpc(code);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
        
        // ✅ NEW: place all players at spawn points at round start
        PositionPlayersAtSpawnsServer(alsoHeal: false);
        ghostManager.StartRoundServer(Round.Value);
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

        ghostManager.DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();

        if (victory) ShowEndOfRoundPanelClientRpc();
    }

    private void DefeatServer()
    {
        if (State.Value == RoundState.Defeat) return;
        State.Value = RoundState.Defeat;

        if (timerRoutine != null) { StopCoroutine(timerRoutine); timerRoutine = null; }

        ghostManager.DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();

        ShowDefeatPanelClientRpc();
    }

    private void CleanupRoundServer()
    {
        ghostManager.DespawnAllGhostsServer();
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

    // private void SpawnGhostsServer(int count)
    // {
    //     if (ghostPrefabs.Count == 0 || ghostSpawnPoints.Count == 0) return;
    //
    //     for (int i = 0; i < count; i++)
    //     {
    //         // var prefab = ghostPrefabs[Random.Range(0, ghostPrefabs.Count)];
    //         // var spawn = ghostSpawnPoints[Random.Range(0, ghostSpawnPoints.Count)];
    //         //
    //         // var go = Instantiate(prefab, spawn.position, spawn.rotation);
    //         // var ghostAI = go.GetComponent<GhostAI>();
    //
    //         if (patrolRoutes.Count > 0 && ghostAI != null)
    //         {
    //             PatrolRoute route = patrolRoutes[Random.Range(0, patrolRoutes.Count)];
    //             ghostAI.SetPatrolPath(route.Points);
    //         }
    //
    //         var netObj = go.GetComponent<NetworkObject>();
    //         if (netObj == null) { Destroy(go); continue; }
    //
    //         netObj.Spawn(true);
    //         spawnedGhosts.Add(netObj);
    //     }
    // }

    // private void DespawnAllGhostsServer()
    // {
    //     for (int i = spawnedGhosts.Count - 1; i >= 0; i--)
    //     {
    //         var netObj = spawnedGhosts[i];
    //         if (netObj != null && netObj.IsSpawned)
    //             netObj.Despawn(true);
    //     }
    //     spawnedGhosts.Clear();
    // }

    // -------------------- SCORE / EVENTS (SERVER) --------------------

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

    [ServerRpc(RequireOwnership = false)]
    public void RequestEndRoundServerRpc()
    {
        if (State.Value == RoundState.Playing)
            EndRoundServer(victory: true);
    }

    [ServerRpc(RequireOwnership = false)]
    public void HostStartNextRoundServerRpc()
    {
        Debug.Log("[GameManager] HostStartNextRoundServerRpc called");
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

        Round.Value = 1;
        ApplyRoundScaling();
        PlasmaThisRound.Value = 0;
        State.Value = RoundState.Idle;

        ghostManager.DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();

        // Flip UI on all clients
        ReturnToLobbyUIClientRpc();

        HideAllPanelsClientRpc();
        RefreshHUDClientRpc();
    }

    // ✅ NEW: restart from defeat → heal, move to spawns, start at Round 1
    [ServerRpc(RequireOwnership = false)]
    public void HostRestartGameServerRpc()
    {
        if (!IsServer) return;

        Debug.Log("[GameManager] Restarting game...");

        Round.Value = 1;
        PlasmaThisRound.Value = 0;
        ApplyRoundScaling();

        ghostManager.DespawnAllGhostsServer();
        DespawnNextRoundConsoleIfAny();
        knockedOutClients.Clear();

        // Place + fully heal everyone
        PositionPlayersAtSpawnsServer(alsoHeal: true);

        // Fresh round
        StartRoundServer();

        HideAllPanelsClientRpc();
        RefreshHUDClientRpc();
    }

    // -------------------- CLIENT RPCs (UI) --------------------

    [ClientRpc]
    private void EnterGameplayUIClientRpc(string joinCode)
    {
        if (lobbyCanvas != null) lobbyCanvas.SetActive(false);
        if (hudCanvas != null) hudCanvas.SetActive(true);

        if (hudJoinCodeText != null)
            hudJoinCodeText.text = joinCode ?? string.Empty;

        // Clear popups and paint HUD immediately
        UIManager.Instance?.HideAllPanels();
        SafeRefreshHUD();
    }

    [ClientRpc]
    private void ReturnToLobbyUIClientRpc()
    {
        if (lobbyCanvas != null) lobbyCanvas.SetActive(true);
        if (hudCanvas != null) hudCanvas.SetActive(false);

        UIManager.Instance?.HideAllPanels();
    }

    [ClientRpc] private void ShowDefeatPanelClientRpc() => UIManager.Instance?.ShowDefeatPanel();
    [ClientRpc] private void ShowEndOfRoundPanelClientRpc() => UIManager.Instance?.ShowEndOfRoundPanel();
    [ClientRpc] private void HideAllPanelsClientRpc() => UIManager.Instance?.HideAllPanels();

    [ClientRpc]
    private void RefreshHUDClientRpc() { SafeRefreshHUD(); }

    // -------------------- LOCAL UI HELPERS (CLIENT) --------------------

    private void ApplyLocalUIFromState(RoundState state)
    {
        // ✅ FIX: keep HUD active on Defeat so the Defeat panel can show
        switch (state)
        {
            case RoundState.Playing:
            case RoundState.RoundEnded:
            case RoundState.Defeat: // <- was showing Lobby before (bug)
                if (lobbyCanvas != null) lobbyCanvas.SetActive(false);
                if (hudCanvas != null) hudCanvas.SetActive(true);
                break;

            case RoundState.Idle:
            default:
                if (lobbyCanvas != null) lobbyCanvas.SetActive(true);
                if (hudCanvas != null) hudCanvas.SetActive(false);
                break;
        }
    }

    private void SafeRefreshHUD()
    {
        UIManager.Instance?.RefreshHUD(
            PlasmaThisRound.Value,
            PlasmaThreshold.Value,
            Round.Value,
            TimeRemaining.Value
        );
    }

    // -------------------- PLAYER SPAWN HELPERS (SERVER) --------------------
    // ✅ NEW: deterministic assignment (sorted ClientIds) + owner-side teleport
    private void PositionPlayersAtSpawnsServer(bool alsoHeal)
    {
        if (!IsServer) return;

        // Build stable order
        var ids = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        ids.Sort();

        for (int i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var client)) continue;
            var playerObj = client.PlayerObject;
            if (playerObj == null) continue;

            var playerNet = playerObj.GetComponent<PlayerNetwork>();
            if (playerNet == null) continue;

            Transform sp = (playerSpawnPoints != null && playerSpawnPoints.Count > 0)
                ? playerSpawnPoints[i % playerSpawnPoints.Count]
                : null;

            Vector3 pos = sp != null ? sp.position : Vector3.zero;
            Quaternion rot = sp != null ? sp.rotation : Quaternion.identity;

            // Set on server AND tell the owner to snap locally (works even w/o NetworkTransform)
            playerNet.ResetTransformServerRpc(pos, rot);

            if (alsoHeal)
            {
                var ph = playerObj.GetComponent<PlayerHealth>();
                if (ph != null) ph.ServerFullHeal();
            }
        }
    }
}
