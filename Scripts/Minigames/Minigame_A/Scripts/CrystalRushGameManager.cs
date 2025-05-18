using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Linq;

public class CrystalRushGameManager : NetworkBehaviour
{
    public static CrystalRushGameManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject loadingPanel;
    public TMP_Text countdownText;
    public TMP_Text gameTimerText; // ✅ แสดงเวลาขณะเล่น
    public GameObject resultPanel;
    public TMP_Text resultScoreText;
    public GameObject scoreUI;

    [Header("Timing Settings")]
    public float loadingDuration = 5f;
    public float preGameCountdown = 5f;
    public float crystalCollectDuration = 60f;

    [Header("Spawn Manager")]
    public CrystalSpawnManager crystalSpawnManager;

    private bool gameEnded = false;

    public NetworkVariable<bool> canMove = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> gameTimeRemaining = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (loadingPanel) loadingPanel.SetActive(true);
        if (countdownText) countdownText.gameObject.SetActive(false);
        if (gameTimerText) gameTimerText.gameObject.SetActive(false); // ✅ ซ่อนก่อน
        if (resultPanel) resultPanel.SetActive(false);
        if (scoreUI) scoreUI.SetActive(false);

        if (crystalSpawnManager) crystalSpawnManager.enabled = false;

        if (IsServer)
        {
            ShowLoadingPanelClientRpc(true);
            StartCoroutine(HideLoadingPanelAfterDelay());
            StartCoroutine(GameFlowRoutine());
        }
    }

    private IEnumerator GameFlowRoutine()
    {
        yield return new WaitForSeconds(loadingDuration);
        ShowLoadingPanelClientRpc(false);

        for (float t = preGameCountdown; t > 0; t--)
        {
            string msg = $"Game Start in {t:0}";
            SetCountdownTextClientRpc(msg, true);
            yield return new WaitForSeconds(1f);
        }
        SetCountdownTextClientRpc("Game Started!", true);
        yield return new WaitForSeconds(1f);
        SetCountdownTextClientRpc("", false);

        canMove.Value = true;
        gameTimeRemaining.Value = crystalCollectDuration; // ✅ ตั้งค่าเวลาเริ่มต้น
        ShowGameTimerClientRpc(true); // ✅ แสดง UI นับเวลาขณะเล่น

        if (crystalSpawnManager)
        {
            crystalSpawnManager.enabled = true;
            crystalSpawnManager.StartSpawningCrystals();
        }
        ShowScoreUIClientRpc(true);

        while (gameTimeRemaining.Value > 0)
        {
            yield return new WaitForSeconds(1f);
            gameTimeRemaining.Value -= 1f;
        }

        EndGame();
    }

    private void EndGame()
    {
        if (gameEnded) return;
        gameEnded = true;

        LockPlayerCameraAndSetIdleClientRpc();
        canMove.Value = false;
        gameTimeRemaining.Value = 0f;
        ShowGameTimerClientRpc(false); // ✅ ซ่อน UI นับเวลาหลังเกมจบ

        if (crystalSpawnManager)
        {
            crystalSpawnManager.StopSpawning();
            crystalSpawnManager.DespawnAllCrystals(); // ✅ Despawn คริสตัลทั้งหมด
        }
        
        foreach (var crystal in FindObjectsOfType<Crystal>())
        {
            crystal.StopCrystalCollection();
        }

        ShowScoreUIClientRpc(false);

        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            if (player.IsOwner) 
            {
                player.SetIdleStateServerRpc(); // ✅ ให้ทุกคนกลับไปเป็น Idle
            }
        }

        var scoreScript = FindObjectOfType<ScorePlayerScript>();
        if (scoreScript != null)
        {
            scoreScript.SyncPlayerNamesToClients();
        }

        string finalScores = CalculateFinalScores();
        UpdateResultScoreClientRpc(finalScores);

        ShowResultPanelClientRpc(true);
    }

    [ClientRpc]
    private void LockPlayerCameraAndSetIdleClientRpc()
    {
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            player.SetIdleAnimationClientRpc();
            player.LockCameraAndSetIdle();
        }
    }

    private string CalculateFinalScores()
    {
        string result = "\n";

        var scoreScript = FindObjectOfType<ScorePlayerScript>();
        if (scoreScript != null)
        {
            var allScores = scoreScript.GetAllScores();
            foreach (var kvp in allScores.OrderByDescending(kvp => kvp.Value))
            {
                string playerName = scoreScript.GetPlayerName(kvp.Key);
                result += $"{playerName}: {kvp.Value} points\n";
            }
        }
        else
        {
            result += "No scores found.";
        }

        return result;
    }

    private IEnumerator HideLoadingPanelAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        ShowLoadingPanelClientRpc(false);
    }

    private void Update()
    {
        if (gameTimerText && gameTimeRemaining.Value > 0)
        {
            gameTimerText.text = $"{gameTimeRemaining.Value:0} s";
        }
    }

    //====== ClientRpc Methods ======//

    [ClientRpc]
    private void ShowLoadingPanelClientRpc(bool show)
    {
        if (loadingPanel) 
        {
            loadingPanel.SetActive(show);
        }
    }

    [ClientRpc]
    private void SetCountdownTextClientRpc(string message, bool active)
    {
        if (!countdownText) return;
        countdownText.gameObject.SetActive(active);
        countdownText.text = message;
    }

    [ClientRpc]
    private void ShowGameTimerClientRpc(bool show)
    {
        if (gameTimerText) gameTimerText.gameObject.SetActive(show);
    }

    [ClientRpc]
    private void ShowScoreUIClientRpc(bool show)
    {
        if (scoreUI) scoreUI.SetActive(show);
    }

    [ClientRpc]
    private void UpdateResultScoreClientRpc(string text)
    {
        if (resultScoreText) resultScoreText.text = text;
    }

    [ClientRpc]
    private void ShowResultPanelClientRpc(bool show)
    {
        if (resultPanel) resultPanel.SetActive(show);
    }
}