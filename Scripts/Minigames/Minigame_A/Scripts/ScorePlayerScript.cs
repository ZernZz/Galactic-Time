using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;

public class ScorePlayerScript : NetworkBehaviour
{
    private NetworkList<int> playerScores = new NetworkList<int>();
    private Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            for (int i = 0; i < NetworkManager.Singleton.ConnectedClientsIds.Count; i++)
            {
                playerScores.Add(0);
            }

            List<ulong> clientIds = new List<ulong>();
            List<FixedString32Bytes> names = new List<FixedString32Bytes>();

            foreach (var client in NetworkManager.Singleton.ConnectedClients)
            {
                var playerObject = client.Value.PlayerObject;
                if (playerObject != null)
                {
                    var nameManager = playerObject.GetComponent<PlayerNameManager>();
                    if (nameManager != null)
                    {
                        playerNames[client.Key] = nameManager.playerName.Value.ToString();

                        // ใช้ FixedString32Bytes แทน string
                        clientIds.Add(client.Key);
                        names.Add(nameManager.playerName.Value);
                    }
                }
            }

            // แปลง List เป็น Array ก่อนส่ง RPC
            SyncPlayerNamesClientRpc(clientIds.ToArray(), names.ToArray());

            playerScores.OnListChanged += (changeEvent) =>
            {
                UpdateScoreboardClientRpc();
            };

            SyncInitialScoresClientRpc();
        }
    }

    public NetworkList<int> GetPlayerScores()
    {
        return playerScores;
    }

    [ServerRpc(RequireOwnership = false)]
    public void AddScoreServerRpc(int points, ulong playerId)
    {
        if (!IsServer) return;

        int playerIndex = GetPlayerIndex(playerId);
        if (playerIndex != -1)
        {
            playerScores[playerIndex] += points;
            UpdateScoreboardClientRpc();
        }
    }

    // 🚀 ให้ Client ทุกคนอัปเดตชื่อใน Score UI
    [ClientRpc]
    private void SyncPlayerNamesClientRpc(ulong[] clientIds, FixedString32Bytes[] names)
    {
        playerNames.Clear();
        for (int i = 0; i < clientIds.Length; i++)
        {
            playerNames[clientIds[i]] = names[i].ToString();
        }

        // 📢 อัปเดต Score UI ทันที
        var scoreboard = FindObjectOfType<ScoreboardManager>();
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreUI();
        }
    }

    public void SyncPlayerNamesToClients()
    {
        if (IsServer)
        {
            List<ulong> clientIds = new List<ulong>();
            List<FixedString32Bytes> names = new List<FixedString32Bytes>();

            foreach (var pair in playerNames)
            {
                clientIds.Add(pair.Key);
                names.Add(new FixedString32Bytes(pair.Value));
            }

            SyncPlayerNamesClientRpc(clientIds.ToArray(), names.ToArray());
        }
    }


    public Dictionary<ulong, int> GetAllScores()
    {
        Dictionary<ulong, int> scores = new Dictionary<ulong, int>();
        int i = 0;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (i < playerScores.Count)
            {
                scores[clientId] = playerScores[i];
            }
            i++;
        }
        return scores;
    }

    private int GetPlayerIndex(ulong playerId)
    {
        int i = 0;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == playerId)
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    [ClientRpc]
    private void SyncInitialScoresClientRpc()
    {
        UpdateScoreboardClientRpc();
    }

    [ClientRpc]
    private void UpdateScoreboardClientRpc()
    {
        var scoreboard = FindObjectOfType<ScoreboardManager>();
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreUI();
        }
    }

    public void UpdatePlayerName(ulong clientId, string newName)
    {
        if (IsServer)
        {
            Debug.Log($"[ScorePlayerScript] Updating player name for {clientId} -> {newName}");

            if (playerNames.ContainsKey(clientId))
            {
                playerNames[clientId] = newName;
            }
            else
            {
                playerNames.Add(clientId, newName);
            }

            // 📢 แจ้ง Client ทุกคนให้รู้ว่าชื่อนี้เปลี่ยนแล้ว
            SyncSinglePlayerNameClientRpc(clientId, new FixedString32Bytes(newName));
        }
    }


    // 🚀 ส่งชื่อให้ทุก Client (ใช้ FixedString32Bytes[])
    [ClientRpc]
    private void SyncSinglePlayerNameClientRpc(ulong clientId, FixedString32Bytes newName)
    {
        if (playerNames.ContainsKey(clientId))
        {
            playerNames[clientId] = newName.ToString();
        }
        else
        {
            playerNames.Add(clientId, newName.ToString());
        }

        // 📢 อัปเดต Score UI ทันที
        var scoreboard = FindObjectOfType<ScoreboardManager>();
        if (scoreboard != null)
        {
            scoreboard.UpdateScoreUI();
        }
    }

    public string GetPlayerName(ulong clientId)
    {
        return playerNames.TryGetValue(clientId, out string name) ? name : $"Player {clientId}";
    }

    public List<PlayerResult> GetFinalScoreList()
    {
        List<PlayerResult> result = new List<PlayerResult>();

        int i = 0;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            string playerName = "Unknown";

            var playerObj = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            var nameManager = playerObj.GetComponent<PlayerNameManager>();
            if (nameManager != null)
            {
                playerName = nameManager.playerName.Value.ToString();
            }

            int score = (i < playerScores.Count) ? playerScores[i] : 0;
            result.Add(new PlayerResult(playerName, score));
            i++;
        }

        return result;
    }
}
