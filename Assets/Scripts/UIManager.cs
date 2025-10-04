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
    [SerializeField] private TMP_Text joinCodeHUD;

    [Header("Panels")]
    [SerializeField] private GameObject defeatPanel;
    [SerializeField] private GameObject endOfRoundPanel;

    [Header("Buttons")]
    public Button NextRoundButton;
    public Button ReturnToLobbyButton;
    public Button RestartButton; // present

    [Header("Revive UI")]
    [SerializeField] private GameObject revivePrompt;
    [SerializeField] private TMP_Text reviveText;
    [SerializeField] private Slider reviveSlider;

    [Header("Health UI (Filled Image)")]
    [Tooltip("The foreground fill Image using your 2D sprite. Image.type should be Filled, Horizontal.")]
    [SerializeField] private Image playerHealthFill;
    [Tooltip("Optional number text like '85/100'. Leave null to skip.")]
    [SerializeField] private TMP_Text playerHealthText;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // IMPORTANT: Do NOT force-hide here; leave your scene's initial active state as-is.
        // The owner will explicitly show & initialize from PlayerHealth when ready.
        // (If you want it hidden in editor, set it inactive on the Image in the prefab.)
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
            defeatPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ShowEndOfRoundPanel()
    {
        if (endOfRoundPanel != null)
            endOfRoundPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void HideAllPanels()
    {
        if (defeatPanel != null) defeatPanel.SetActive(false);
        if (endOfRoundPanel != null) endOfRoundPanel.SetActive(false);
    }

    // ---------------- Revive Prompt ----------------
    public void ShowReviveMessageOnly()
    {
        if (revivePrompt == null) return;
        revivePrompt.SetActive(true);

        if (reviveText != null)
            reviveText.text = "Hold E to Revive";

        if (reviveSlider != null)
            reviveSlider.gameObject.SetActive(false);
    }

    public void UpdateReviveProgress(float progress)
    {
        if (revivePrompt == null) return;
        revivePrompt.SetActive(true);

        if (reviveText != null)
            reviveText.text = "Hold E to Revive";

        if (reviveSlider != null)
        {
            reviveSlider.gameObject.SetActive(true);
            reviveSlider.value = Mathf.Clamp01(progress);
        }
    }

    public void HideRevivePrompt()
    {
        if (revivePrompt != null)
            revivePrompt.SetActive(false);
    }

    // ---------------- Health (Filled Image) ----------------
    public void SetHealthBarVisible(bool visible, bool prefillFull = false)
    {
        if (playerHealthFill != null)
        {
            if (visible)
            {
                ActivateParents(playerHealthFill.gameObject);

                if (playerHealthFill.type != Image.Type.Filled)
                    playerHealthFill.type = Image.Type.Filled;
                if (playerHealthFill.fillMethod != Image.FillMethod.Horizontal)
                    playerHealthFill.fillMethod = Image.FillMethod.Horizontal;

                if (prefillFull)
                {
                    playerHealthFill.fillAmount = 1f;   // show full immediately
                    playerHealthFill.SetVerticesDirty(); // force redraw this frame
                }
            }

            playerHealthFill.gameObject.SetActive(visible);
        }

        if (playerHealthText != null)
        {
            if (visible)
                ActivateParents(playerHealthText.gameObject);

            playerHealthText.gameObject.SetActive(visible);
        }
    }

    public void UpdateHealthBar(float current, float max)
    {
        if (playerHealthFill != null)
        {
            if (playerHealthFill.type != Image.Type.Filled)
                playerHealthFill.type = Image.Type.Filled;
            if (playerHealthFill.fillMethod != Image.FillMethod.Horizontal)
                playerHealthFill.fillMethod = Image.FillMethod.Horizontal;

            if (!playerHealthFill.gameObject.activeInHierarchy)
                SetHealthBarVisible(true); // ensure visible

            playerHealthFill.fillAmount = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;
            playerHealthFill.SetVerticesDirty(); // ensure the change is rendered now
        }

        if (playerHealthText != null)
        {
            if (!playerHealthText.gameObject.activeInHierarchy)
                SetHealthBarVisible(true);

            playerHealthText.text = $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
        }
    }

    private void ActivateParents(GameObject leaf)
    {
        var t = leaf.transform;
        while (t != null)
        {
            var go = t.gameObject;
            if (!go.activeSelf)
                go.SetActive(true);
            t = t.parent;
        }
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

    public void OnClick_RestartGame()
    {
        Debug.Log("[UIManager] Restart button clicked (local)");
        if (GameManager.Instance != null)
            GameManager.Instance.HostRestartGameServerRpc();
    }
}
