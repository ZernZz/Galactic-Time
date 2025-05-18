using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class MinigameBManager : NetworkBehaviour
{
    public static MinigameBManager Instance;

    [Header("Game Settings")]
    public int totalBoxes = 4;
    public float gameDuration = 60f;
    public string nextSceneName = "LobbyScene";

    [Header("UI Panels")]
    public GameObject howToPlayPanel;
    public GameObject inGameUIPanel;
    public GameObject EndGameUIPanel;

    [Header("In-Game UI Elements")]
    public TMP_Text timerText;
    public TMP_Text boxStatusText;
    public TMP_Text countdownText;

    private float remainingTime;
    private int boxesDelivered = 0;

    private NetworkVariable<float> syncedTime = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    private NetworkVariable<int> syncedDelivered = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> canMove = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        remainingTime = gameDuration;

        if (howToPlayPanel != null) howToPlayPanel.SetActive(true);
        if (inGameUIPanel != null) inGameUIPanel.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (EndGameUIPanel != null) EndGameUIPanel.SetActive(false);
    }

    void Update()
    {
        if (!IsServer || !canMove.Value) return;

        remainingTime -= Time.deltaTime;
        if (remainingTime <= 0f)
        {
            EndMinigame();
        }

        syncedTime.Value = remainingTime;
        syncedDelivered.Value = boxesDelivered;
    }

    void LateUpdate()
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        if (timerText != null)
            timerText.text = "Time: " + Mathf.CeilToInt(syncedTime.Value);

        if (boxStatusText != null)
            boxStatusText.text = $"Delivered: {syncedDelivered.Value}/{totalBoxes}";
    }

    public void AddDeliveredBox(NetworkObject boxNetworkObject)
    {
        if (!IsServer) return;

        boxNetworkObject.Despawn();
        boxesDelivered++;
        syncedDelivered.Value = boxesDelivered;

        if (boxesDelivered >= totalBoxes)
        {
            Invoke(nameof(EndMinigame), 1.5f);
        }
    }

    void EndMinigame()
    {
        boxesDelivered = 0;

        var scoreScript = FindObjectOfType<ScorePlayerScript>();
        if (scoreScript != null)
        {
            var results = scoreScript.GetFinalScoreList().ToArray(); // ✅ Array
            ShowEndingPanelClientRpc(results);
        }
    }

    [ClientRpc]
    void ShowEndingPanelClientRpc(PlayerResult[] results)
    {
        FindObjectOfType<ScoreManagerMiniB>().ShowEndingPanel(results.ToList());
    }


    

    // เรียกจากปุ่ม “เข้าใจแล้ว”
    public void OnHowToPlayConfirmed()
    {
        OnHowToPlayConfirmedServerRpc(); // ส่งไปให้ Server จัดการ
    }
    
    [ServerRpc(RequireOwnership = false)]
    void OnHowToPlayConfirmedServerRpc(ServerRpcParams rpcParams = default)
    {
        // เมื่อ Host ได้รับคำสั่ง ให้ส่งไป Client ทุกคน
        ShowCountdownClientRpc();
    }

    [ClientRpc]
    void ShowCountdownClientRpc()
    {
        // ปิด Panel + เปิด In-Game UI + เริ่มนับถอยหลัง
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
        if (inGameUIPanel != null) inGameUIPanel.SetActive(true);
        if (countdownText != null) countdownText.gameObject.SetActive(true);

        StartCoroutine(CountdownCoroutine());
    }


    private IEnumerator CountdownCoroutine()
    {
        int count = 5;
        while (count > 0)
        {
            if (countdownText != null)
                countdownText.text = "Starting in: " + count;

            yield return new WaitForSeconds(1f);
            count--;
        }

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);

        if (IsServer)
        {
            canMove.Value = true; // อนุญาตให้เล่น
        }
    }

}
