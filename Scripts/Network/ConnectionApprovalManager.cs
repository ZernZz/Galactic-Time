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

        // Dictionary to map client ID to their assigned spawn index in the lobbySpawnPositions array
        private Dictionary<ulong, int> assignedSpawnIndices = new Dictionary<ulong, int>();

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

            // Store the assigned index for our own tracking and for reassignment on disconnect
            assignedSpawnIndices[request.ClientNetworkId] = spawnIndex;
            Debug.Log($"[ConnectionApprovalManager] Assigned spawn index {spawnIndex} to client {request.ClientNetworkId} at position {calculatedSpawnPosition}.");

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
             Debug.Log($"[ConnectionApprovalManager] Client {clientId} attempting to disconnect. Processing spawn reassignment.");

             if (!assignedSpawnIndices.ContainsKey(clientId))
             {
                 Debug.LogWarning($"[ConnectionApprovalManager] Client {clientId} disconnected but was not in assignedSpawnIndices. No specific lobby spawn index to clear or reassign from this manager's perspective.");
                 // Still, let ConnectionManager know for general cleanup.
                 if (ConnectionManager.Instance != null)
                 {
                     ConnectionManager.Instance.HandleClientDisconnect(clientId);
                 }
                 return;
             }

             int leavingIndex = assignedSpawnIndices[clientId];
             assignedSpawnIndices.Remove(clientId);
             Debug.Log($"[ConnectionApprovalManager] Client {clientId} (who had spawn index {leavingIndex}) disconnected. Removed from assignedSpawnIndices.");

             // Notify ConnectionManager to remove its record for the disconnected client.
             if (ConnectionManager.Instance != null)
             {
                 ConnectionManager.Instance.HandleClientDisconnect(clientId);
                 Debug.Log($"[ConnectionApprovalManager] Notified ConnectionManager to handle disconnect for client {clientId}.");
             }

             // Re-index and reposition remaining players who were at a higher index
             var clientsToPotentiallyUpdate = new List<KeyValuePair<ulong, int>>(assignedSpawnIndices); // Copy to avoid modification issues

             foreach (var clientEntry in clientsToPotentiallyUpdate)
             {
                 ulong currentClientId = clientEntry.Key;
                 int currentIndexInDict = clientEntry.Value; // This is the value from the dictionary *before* any updates in this loop

                 if (currentIndexInDict > leavingIndex)
                 {
                     int newIndex = currentIndexInDict - 1;
                     assignedSpawnIndices[currentClientId] = newIndex; // Update the index in our tracking

                     if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                     {
                         if (NetworkManager.Singleton.ConnectedClients.TryGetValue(currentClientId, out var networkClient) && networkClient.PlayerObject != null)
                         {
                             var playerController = networkClient.PlayerObject.GetComponent<PlayerController>();
                             if (playerController != null)
                             {
                                 if (newIndex >= 0 && newIndex < lobbySpawnPositions.Length)
                                 {
                                     Vector3 newPos = lobbySpawnPositions[newIndex];
                                     // Preserve current rotation or use Quaternion.identity if preferred
                                     Quaternion newRot = networkClient.PlayerObject.transform.rotation; 
                                     
                                     playerController.ForceRepositionClientRpc(newPos, newRot);
                                     Debug.Log($"[ConnectionApprovalManager] Repositioned client {currentClientId} from old index {currentIndexInDict} to new index {newIndex} at {newPos}.");

                                     // Update ConnectionManager with the new position for this client
                                     if (ConnectionManager.Instance != null)
                                     {
                                         ConnectionManager.Instance.RegisterPlayerSpawnPosition(currentClientId, newPos);
                                         Debug.Log($"[ConnectionApprovalManager] Updated ConnectionManager with new spawn position for client {currentClientId}.");
                                     }
                                 }
                                 else
                                 {
                                     Debug.LogError($"[ConnectionApprovalManager] Calculated newIndex {newIndex} is out of bounds for lobbySpawnPositions (Length: {lobbySpawnPositions.Length}) for client {currentClientId}. Cannot reposition.");
                                 }
                             }
                             else { Debug.LogWarning($"[ConnectionApprovalManager] PlayerController not found for client {currentClientId} on PlayerObject {networkClient.PlayerObject.name}. Cannot reposition."); }
                         }
                         else { Debug.LogWarning($"[ConnectionApprovalManager] PlayerObject not found for client {currentClientId} in ConnectedClients. Cannot reposition."); }
                     }
                 }
             }
             Debug.Log($"[ConnectionApprovalManager] Finished reassigning spawn positions after client {clientId} disconnected.");
        }
    }
}