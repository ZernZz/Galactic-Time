using Unity.Netcode;
using UnityEngine;

public class MeteorShooterPlayer : NetworkBehaviour
{
    public GameObject bulletPrefab;
    public float bulletSpawnDistance = 1f;

    public NetworkVariable<Vector3> crosshairWorldPos = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    void Update()
    {
        Debug.Log($"[Client {NetworkManager.Singleton.LocalClientId}] Update() กำลังทำงาน");
        if (!IsOwner)
        {
            Debug.Log("❌ ไม่ใช่ Owner เลยไม่ยิงได้");
            return; 
        }
        if (Camera.main == null) return; // ป้องกันกรณีกล้องยังไม่พร้อม

        // 🎯 ใช้ Camera.main ยิง Ray แทน fpsCamera
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            crosshairWorldPos.Value = hit.point;
        }

        // 🟢 ยิงกระสุนเมื่อคลิกซ้าย (เฉพาะตอนที่ยิงได้)
        if (Input.GetMouseButtonDown(0) && MeteorSpawner.CanShoot)
        {
            Debug.Log("👈 CLICK! Client กดเมาส์");

            Vector3 spawnPos = ray.origin + ray.direction * bulletSpawnDistance;
            Debug.Log("📤 เรียก ShootServerRpc");
            ShootServerRpc(spawnPos, ray.direction);
        }

    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 spawnPos, Vector3 direction, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"📡 ServerRpc ถูกเรียกโดย ClientId: {rpcParams.Receive.SenderClientId}");
        GameObject bullet = Instantiate(bulletPrefab, spawnPos, Quaternion.LookRotation(direction));
        Debug.Log("🧨 สร้าง Bullet แล้ว");
        var netObj = bullet.GetComponent<NetworkObject>();
        var bulletScript = bullet.GetComponent<Bullet>();

        bulletScript.ownerClientId = rpcParams.Receive.SenderClientId;

        netObj.Spawn();
        Debug.Log("🚀 Bullet Spawn เรียบร้อย");
    }
}
