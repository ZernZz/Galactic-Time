using UnityEngine;
using System.Collections;
using Unity.Netcode;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class ScoreManagerMiniB : NetworkBehaviour
{
    public TMP_Text scoreText;
    private ScorePlayerScript scoreScript;
    
    [Header("Ending Panel Elements")]
    [SerializeField] private GameObject endingPanel;
    [SerializeField] private GameObject playerDetailPrefab;
    [SerializeField] private Transform playerListRoot;

    private void Start()
    {
        StartCoroutine(FindScoreScript());
    }

    private IEnumerator FindScoreScript()
    {
        while (scoreScript == null)
        {
            scoreScript = FindObjectOfType<ScorePlayerScript>();
            yield return null;
        }
        // 🚀 ซิงค์คะแนนตอนเริ่มเกมทันที
        UpdateScoreUI();

        // ให้ Client อัปเดต UI อัตโนมัติเมื่อคะแนนเปลี่ยน
        if (!IsServer)
        {
            scoreScript.GetPlayerScores().OnListChanged += (changeEvent) =>
            {
                UpdateScoreUI();
            };
        }
    }

    public void UpdateScoreUI()
    {
        if (!scoreScript || scoreText == null) return;

        Dictionary<ulong, int> allScores = scoreScript.GetAllScores();
        if (allScores.Count == 0)
        {
            scoreText.text = "No Scores Yet...";
            return;
        }

        string txt = "";
        foreach (var kvp in allScores)
        {
            txt += $"Player {kvp.Key}: {kvp.Value}\n";
        }
        scoreText.text = txt;
    }
    
    public void ShowEndingPanel(List<PlayerResult> results)
    {
        if (endingPanel == null || playerDetailPrefab == null || playerListRoot == null) return;

        endingPanel.SetActive(true);

        // ล้างของเก่า
        foreach (Transform child in playerListRoot)
            Destroy(child.gameObject);

        // สร้างใหม่
        foreach (var result in results)
        {
            var go = Instantiate(playerDetailPrefab, playerListRoot);
            var nameText = go.transform.Find("PlayerText")?.GetComponent<TMP_Text>();
            var boxText = go.transform.Find("BoxCountText")?.GetComponent<TMP_Text>();

            if (nameText != null) nameText.text = result.playerName.ToString();
            if (boxText != null) boxText.text = result.boxCount + " Box";
        }
    }
    
    [ClientRpc]
    void ShowEndingClientRpc(PlayerResult[] results)
    {
        var board = FindObjectOfType<ScoreManagerMiniB>();
        if (board != null)
        {
            board.ShowEndingPanel(results.ToList());
        }
    }
    
    
    
}

