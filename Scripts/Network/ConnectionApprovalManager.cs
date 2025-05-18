using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement; // Needed for checking scene name potentially
using System.Collections.Generic;

// Namespace อาจจะไม่จำเป็นถ้าไม่ได้ใช้อยู่แล้ว
namespace FourDucktion.Network
{
    [System.Serializable]
    public class SceneSpawnData
    {
        public string sceneName;
        public Vector3[] spawnPositions;
    }

    public class ConnectionApprovalManager : MonoBehaviour
    {
        [Header("Scene Spawn Settings")]
        [Tooltip("กำหนดตำแหน่ง Spawn สำหรับ Scene ต่างๆ โดยเฉพาะ LobbyScene")]
        [SerializeField] private SceneSpawnData[] sceneSpawnSettings;

        private Vector3[] lobbySpawnPositions; // เก็บตำแหน่ง Spawn สำหรับ Lobby

        private void Start()
        {
            var netMan = NetworkManager.Singleton;
            if (netMan == null)
            {
                Debug.LogError("[ConnectionApprovalManager] ไม่สามารถหา NetworkManager.Singleton ได้ในฟังก์ชัน Start()!");
                enabled = false; // ปิดการทำงานของ Script นี้ถ้าไม่มี NetworkManager
                return;
            }

            // ค้นหาตำแหน่ง Spawn สำหรับ LobbyScene จาก sceneSpawnSettings
            string lobbySceneName = "LobbyScene"; // ชื่อ Scene ของ Lobby
            bool foundSettings = false;
            foreach (var setting in sceneSpawnSettings)
            {
                if (setting.sceneName == lobbySceneName)
                {
                    lobbySpawnPositions = setting.spawnPositions;
                    foundSettings = true;
                    Debug.Log($"[ConnectionApprovalManager] พบตำแหน่ง Spawn {lobbySpawnPositions.Length} จุดสำหรับ '{lobbySceneName}'.");
                    break;
                }
            }

            // ถ้าไม่พบตำแหน่ง Spawn ที่กำหนดสำหรับ Lobby หรือไม่มีตำแหน่งเลย ให้ใช้ค่าเริ่มต้น
            if (!foundSettings || lobbySpawnPositions == null || lobbySpawnPositions.Length == 0)
            {
                 Debug.LogWarning($"[ConnectionApprovalManager] ไม่พบตำแหน่ง Spawn ที่กำหนดสำหรับ '{lobbySceneName}' ใน sceneSpawnSettings. จะใช้ตำแหน่งเริ่มต้น Vector3.zero.");
                 lobbySpawnPositions = new Vector3[] { Vector3.zero }; // กำหนดตำแหน่ง Default เป็นจุดกำเนิด
            }

            // --- Modification Start: ตั้งค่า Callback อย่างมั่นใจ ---
            // ตั้งค่า Callback การตรวจสอบการเชื่อมต่อ โดยอาจจะเขียนทับ Callback ที่มีอยู่ (ถ้ามี)
            // ควรแน่ใจว่า Script นี้เป็น Script เดียวที่ควรจัดการ Connection Approval
            if (netMan.ConnectionApprovalCallback != null && netMan.ConnectionApprovalCallback != ApprovalCheck)
            {
                // แจ้งเตือนหากมีการเขียนทับ Callback ของ Script อื่น
                Debug.LogWarning($"[ConnectionApprovalManager] กำลังเขียนทับ Connection Approval Callback ที่ถูกตั้งค่าโดย Script อื่นอยู่ ตรวจสอบให้แน่ใจว่านี่คือสิ่งที่ต้องการ");
            }

            netMan.NetworkConfig.ConnectionApproval = true; // เปิดใช้งาน Connection Approval เสมอ
            netMan.ConnectionApprovalCallback = ApprovalCheck; // กำหนด Callback ของเรา
            Debug.Log("[ConnectionApprovalManager] ตั้งค่า Connection Approval Callback สำเร็จ.");
            // --- Modification End ---


            // สมัคร event เมื่อ client disconnect เพื่อจัดการข้อมูลที่เกี่ยวข้อง (ถ้ามี)
            // เช่น การคืนตำแหน่ง Spawn ที่เคยใช้ไป (ถ้าจำเป็น)
            netMan.OnClientDisconnectCallback += OnClientDisconnect;
            Debug.Log("[ConnectionApprovalManager] สมัครติดตาม OnClientDisconnectCallback.");
        }

        private void OnDestroy()
        {
            // ยกเลิกการติดตาม event และ Callback เมื่อ Object ถูกทำลาย
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;

                // --- Modification: ยกเลิก Callback เฉพาะถ้าเราเป็นคนตั้งค่าไว้ ---
                // ตรวจสอบให้แน่ใจว่า Callback ปัจจุบันยังเป็นของเราก่อนที่จะลบ
                if (NetworkManager.Singleton.ConnectionApprovalCallback == ApprovalCheck)
                {
                    NetworkManager.Singleton.ConnectionApprovalCallback = null; // ล้าง Callback
                    Debug.Log("[ConnectionApprovalManager] ยกเลิกการตั้งค่า Connection Approval Callback ของตัวเอง.");
                }
                // --- End Modification ---
            }
        }

        // เมธอด Callback หลักที่ NetworkManager จะเรียกใช้เมื่อมี Client พยายามเชื่อมต่อ
        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // --- ตรวจสอบ NetworkManager ก่อน ---
            if (NetworkManager.Singleton == null)
            {
                 Debug.LogError("[ConnectionApprovalManager] NetworkManager หายไปขณะกำลังตรวจสอบการเชื่อมต่อ!");
                 response.Approved = false; // ไม่อนุมัติ
                 response.Reason = "Internal server error."; // เหตุผล (แสดงผลฝั่ง Client ได้)
                 return;
            }

            // --- ตรวจสอบจำนวนผู้เล่น ---
            int currentPlayerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
            // ดึงค่า MaxPlayers จาก ConnectionManager เพื่อให้แน่ใจว่าใช้ค่าเดียวกันเสมอ
            int maxPlayers = (ConnectionManager.Instance != null) ? ConnectionManager.Instance.MaxPlayers : 4; // ใช้ค่า Default เป็น 4 ถ้าหา ConnectionManager ไม่เจอ

            if (currentPlayerCount >= maxPlayers)
            {
                Debug.LogWarning($"[ConnectionApprovalManager] ปฏิเสธการเชื่อมต่อ Client {request.ClientNetworkId}. ห้องเต็ม ({currentPlayerCount}/{maxPlayers}).");
                response.Approved = false;          // ไม่อนุมัติ
                response.CreatePlayerObject = false;// ไม่ต้องสร้าง Player Prefab ให้ Client นี้
                response.Reason = "Room is full.";  // เหตุผล
                return;
            }

            // --- ถ้าทุกอย่างผ่าน: อนุมัติการเชื่อมต่อ ---
            response.Approved = true;           // อนุมัติ
            response.CreatePlayerObject = true; // สร้าง Player Prefab ที่กำหนดไว้ใน NetworkManager ให้ Client นี้

            // --- กำหนดตำแหน่ง Spawn ---
            // ใช้จำนวนผู้เล่นปัจจุบันเป็น Index สำหรับหาตำแหน่ง Spawn ใน Lobby
            // ใช้ Modulo (%) เพื่อวนใช้ตำแหน่ง Spawn ถ้าจำนวนคนมากกว่าจำนวนจุดที่เตรียมไว้
            int spawnIndex = currentPlayerCount % lobbySpawnPositions.Length;
            Vector3 calculatedSpawnPosition = lobbySpawnPositions[spawnIndex];

            response.Position = calculatedSpawnPosition; // กำหนดตำแหน่ง
            response.Rotation = Quaternion.identity;     // กำหนดการหมุน (หันหน้าไปทางแกน Z)

            // --- บันทึกตำแหน่ง Spawn ที่กำหนดให้ Client นี้ (สำคัญสำหรับตอนกลับมา Lobby) ---
            if (ConnectionManager.Instance != null)
            {
                // ใช้ ConnectionManager ที่เป็น Singleton ในการเก็บข้อมูลตำแหน่ง Spawn ของแต่ละ ClientId
                ConnectionManager.Instance.RegisterPlayerSpawnPosition(request.ClientNetworkId, calculatedSpawnPosition);
            }
            else
            {
                 // ควรจะมี ConnectionManager เสมอ ถ้าไม่มีแสดงว่ามีปัญหาในการ Setup
                 Debug.LogError("[ConnectionApprovalManager] ไม่พบ ConnectionManager.Instance! ไม่สามารถบันทึกตำแหน่ง Spawn ของ Client ได้.");
            }

            Debug.Log($"[ConnectionApprovalManager] อนุมัติ Client {request.ClientNetworkId}. จำนวนผู้เล่นปัจจุบัน: {currentPlayerCount + 1}/{maxPlayers}. กำหนดตำแหน่ง Spawn ที่ Index {spawnIndex} ({calculatedSpawnPosition}).");
        }

        // เมธอดที่ถูกเรียกเมื่อมี Client ตัดการเชื่อมต่อ (จาก Event ที่ Subscribe ไว้)
        private void OnClientDisconnect(ulong clientId)
        {
             Debug.Log($"[ConnectionApprovalManager] Client {clientId} ตัดการเชื่อมต่อ.");
             // แจ้ง ConnectionManager ให้ลบข้อมูลตำแหน่ง Spawn ของ Client ที่หลุดไป
             // เพื่อให้ตำแหน่งนั้นกลับมาใช้งานได้ หรือเพื่อเคลียร์ข้อมูลเก่า
             if (ConnectionManager.Instance != null)
             {
                 ConnectionManager.Instance.HandleClientDisconnect(clientId);
             }
             else
             {
                  // ควรจะมี ConnectionManager เสมอ
                  Debug.LogWarning("[ConnectionApprovalManager] ไม่พบ ConnectionManager.Instance ตอน Client Disconnect.");
             }

             // อาจจะมีการทำงานอื่นๆ ที่ต้องทำเมื่อ Client หลุด เช่น อัพเดท UI, ตรวจสอบสถานะเกม (ถ้าจำเป็น)
        }
    }
}