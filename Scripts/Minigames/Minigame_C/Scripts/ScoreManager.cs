using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

public class ScoreManager : NetworkBehaviour
{
    public static ScoreManager Instance;

    private Dictionary<ulong, int> playerScores = new();

    [Header("UI")]
    public GameObject scoreEntryPrefab;
    public Transform scoreListContainer;

    private Dictionary<ulong, TextMeshProUGUI> scoreEntries = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void AddScoreToPlayer(ulong clientId, int amount)
    {
        if (!IsServer) return;

        if (!playerScores.ContainsKey(clientId))
            playerScores[clientId] = 0;

        playerScores[clientId] += amount;

        UpdateScoreboardClientRpc(clientId, playerScores[clientId]);
    }

    [ClientRpc]
    private void UpdateScoreboardClientRpc(ulong clientId, int newScore)
    {
        if (!scoreEntries.ContainsKey(clientId))
        {
            GameObject entry = Instantiate(scoreEntryPrefab, scoreListContainer);
            scoreEntries[clientId] = entry.GetComponent<TextMeshProUGUI>();

            // ใส่ชื่อผู้เล่น (ถ้ามีระบบ PlayerNameManager แยก)
            string name = $"Player {clientId}";
            scoreEntries[clientId].text = $"{name}: {newScore}";
        }
        else
        {
            scoreEntries[clientId].text = $"Player {clientId}: {newScore}";
        }
    }
}
