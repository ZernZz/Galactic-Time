using Unity.Netcode;
using UnityEngine;

public class GoalDetector : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return; 
        
        if (other.CompareTag("BoxMiniB"))
        {
            Debug.Log("Box delivered!");
            MinigameBManager.Instance.AddDeliveredBox(other.GetComponent<NetworkObject>());
        }
    }
}