using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class UICrosshairFollower : MonoBehaviour
{
    public GameObject crosshairPrefab;
    public Canvas canvas;

    private Dictionary<ulong, GameObject> crosshairs = new();

    void Update()
    {
        if (crosshairPrefab == null || canvas == null) return;
        if (!NetworkManager.Singleton.IsConnectedClient) return;
        if (Camera.main == null) return;

        foreach (var obj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (obj.TryGetComponent<MeteorShooterPlayer>(out var player))
            {
                ulong clientId = player.OwnerClientId;

                // ✅ สร้าง Crosshair ของทุกคนทันทีที่พบ
                if (!crosshairs.ContainsKey(clientId))
                {
                    GameObject c = Instantiate(crosshairPrefab, canvas.transform);
                    c.name = $"Crosshair_{clientId}";
                    crosshairs[clientId] = c;
                }

                // ✅ Crosshair ตัวเอง = ใช้ mousePosition โดยตรง
                if (clientId == NetworkManager.Singleton.LocalClientId)
                {
                    crosshairs[clientId].transform.position = Input.mousePosition;
                }
                else
                {
                    // ✅ Crosshair คนอื่น = ใช้ WorldToScreenPoint
                    Vector3 world = player.crosshairWorldPos.Value;
                    Vector3 screen = Camera.main.WorldToScreenPoint(world);

                    if (screen.z < 0)
                        crosshairs[clientId].SetActive(false);
                    else
                    {
                        crosshairs[clientId].SetActive(true);
                        crosshairs[clientId].transform.position = screen;
                    }
                }
            }
        }
    }
}
