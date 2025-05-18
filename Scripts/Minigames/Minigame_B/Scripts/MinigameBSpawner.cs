using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

public class MinigameBSpawner : NetworkBehaviour 
{
    [Header("Prefabs")]
    public GameObject boxPrefab;
    public GameObject goalPrefab;

    [Header("Spawn Points")]
    public Transform[] boxSpawnPoints;
    public Transform[] goalSpawnPoints;

    private void Start()
    {
        
        if (IsServer)
        {
            SpawnBoxesAndGoal();
        }
    }

    void SpawnBoxesAndGoal()
    {
        int boxCount = MinigameBManager.Instance.totalBoxes;

        List<Transform> availableBoxPoints = new List<Transform>(boxSpawnPoints);

        for (int i = 0; i < boxCount; i++)
        {
            int index = Random.Range(0, availableBoxPoints.Count);
            Transform point = availableBoxPoints[index];

            
            GameObject box = Instantiate(boxPrefab, point.position, Quaternion.identity);
            box.GetComponent<NetworkObject>().Spawn();

            availableBoxPoints.RemoveAt(index);
        }

        int goalIndex = Random.Range(0, goalSpawnPoints.Length);
        Transform goalPoint = goalSpawnPoints[goalIndex];

        GameObject goal = Instantiate(goalPrefab, goalPoint.position, Quaternion.identity);
        goal.GetComponent<NetworkObject>().Spawn();
    }
}