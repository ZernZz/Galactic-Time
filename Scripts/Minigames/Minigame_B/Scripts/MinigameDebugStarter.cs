using UnityEngine;

public class MinigameDebugStarter : MonoBehaviour
{
    [Header("References")]
    public GameObject playerPrefab;
    public Transform playerSpawnPoint;

    //public GameObject boxPrefab;
    //public Transform boxSpawnPoint;

    void Start()
    {
#if UNITY_EDITOR
        // ใน Editor เท่านั้น
        if (FindObjectOfType<Unity.Netcode.NetworkManager>() == null)
        {
            // Spawn player
            Instantiate(playerPrefab, playerSpawnPoint.position, Quaternion.identity);

            // Spawn box
            //(boxPrefab, boxSpawnPoint.position, Quaternion.identity);
        }
#endif
    }
}