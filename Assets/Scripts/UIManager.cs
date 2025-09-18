using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD")]
    [SerializeField] private TMP_Text plasmaText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text joinCodeHUD; // optional

    [Header("Panels")]
    [SerializeField] private GameObject defeatPanel;
    [SerializeField] private GameObject endOfRoundPanel;

    [Header("Buttons")]
    public Button NextRoundButton;
    public Button ReturnToLobbyButton;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ---------------- HUD ----------------
    public void RefreshHUD(int plasma, int threshold, int round, float timeRemaining)
    {
        if (plasmaText != null)
            plasmaText.text = $"Plasma: {plasma}/{threshold}";

        if (timerText != null)
            timerText.text = $"Time: {Mathf.CeilToInt(timeRemaining)}s";

        if (roundText != null)
            roundText.text = $"Round {round}";
    }

    public void UpdateJoinCode(string code)
    {
        if (joinCodeHUD != null)
            joinCodeHUD.text = $"Code: {code}";
    }

    // ---------------- Panels ----------------
    public void ShowDefeatPanel()
    {
        if (defeatPanel != null)
        {
            defeatPanel.SetActive(true);
            // Cursor unlocking is handled by PlayerNetwork based on game state now.
        }
    }

    public void ShowEndOfRoundPanel()
    {
        if (endOfRoundPanel != null)
        {
            endOfRoundPanel.SetActive(true);
            // Cursor unlocking is handled by PlayerNetwork based on game state now.
        }
    }

    public void HideAllPanels()
    {
        if (defeatPanel != null) defeatPanel.SetActive(false);
        if (endOfRoundPanel != null) endOfRoundPanel.SetActive(false);
    }

    // ---------------- Button Handlers ----------------
    public void OnClick_NextRound()
    {
        Debug.Log("[UIManager] Next Round button clicked (local)");
        if (GameManager.Instance != null)
            GameManager.Instance.HostStartNextRoundServerRpc();
    }

    public void OnClick_ReturnToLobby()
    {
        Debug.Log("[UIManager] Return to Lobby button clicked (local)");
        if (GameManager.Instance != null)
            GameManager.Instance.HostReturnToLobbyServerRpc();
    }
}
