using UnityEngine;
using Unity.Netcode;

public class Crystal : NetworkBehaviour
{
    public int pointValue;
    private bool gameEnded = false;

    public void StopCrystalCollection() => gameEnded = true;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var playerNetworkObject = other.GetComponent<NetworkObject>();
        if (playerNetworkObject == null) return;

        // 🛠 ซ่อน Crystal ทันทีที่ Client ชน (ให้รู้สึกเหมือนหายไปไว)
        gameObject.SetActive(false);

        // ให้ Client ส่งคำขอไปยัง Server เพื่อเก็บ Crystal
        RequestCollectCrystalServerRpc(playerNetworkObject.OwnerClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCollectCrystalServerRpc(ulong playerId)
    {
        if (!IsServer || gameEnded) return;

        // หา ScorePlayerScript ตรง ๆ
        var scoreScript = FindObjectOfType<ScorePlayerScript>();
        if (scoreScript != null)
        {
            scoreScript.AddScoreServerRpc(pointValue, playerId);
        }

        GetComponent<NetworkObject>().Despawn(true);
    }
}
