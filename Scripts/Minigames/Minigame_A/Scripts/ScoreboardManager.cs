using UnityEngine;
using System.Collections;
using Unity.Netcode;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class ScoreboardManager : NetworkBehaviour
{
    public TMP_Text scoreText;
    private ScorePlayerScript scoreScript;

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
        UpdateScoreUI();

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
        foreach (var kvp in allScores.OrderByDescending(kvp => kvp.Value))
        {
            ulong clientId = kvp.Key;
            int score = kvp.Value;

            // ✅ ดึงชื่อผู้เล่นจาก ScorePlayerScript
            string displayName = scoreScript.GetPlayerName(clientId);

            txt += $"{displayName}: {score}\n";
        }
        scoreText.text = txt;
    }
}
