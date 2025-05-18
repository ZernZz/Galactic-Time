using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro; // เพิ่มเข้ามาเผื่อใช้กับ Text บนปุ่ม

public class BackToLobbyManager : NetworkBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button backToPartyClientButton; // ปุ่มสำหรับ Client
    [SerializeField] private Button backToPartyHostButton;   // ปุ่มสำหรับ Host
    [SerializeField] private TextMeshProUGUI hostButtonText; // (Optional) Text บนปุ่ม Host
    [SerializeField] private TextMeshProUGUI clientButtonText; // (Optional) Text บนปุ่ม Client

    // NetworkList เพื่อติดตาม Client ที่พร้อมกลับ Lobby แล้ว (เก็บ ClientId)
    private NetworkList<ulong> readyClients = new NetworkList<ulong>(
        null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    // NetworkVariable เพื่อบอกว่า Host สามารถกดปุ่มได้หรือยัง
    private NetworkVariable<bool> canHostReturn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // ซ่อนปุ่มทั้งหมดก่อน แล้วค่อยแสดงตาม Role (Host/Client)
        if (backToPartyClientButton != null) backToPartyClientButton.gameObject.SetActive(false);
        if (backToPartyHostButton != null) backToPartyHostButton.gameObject.SetActive(false);

        if (IsClient && !IsHost) // ถ้าเป็น Client เท่านั้น
        {
            if (backToPartyClientButton != null)
            {
                backToPartyClientButton.gameObject.SetActive(true);
                backToPartyClientButton.interactable = true; // เริ่มต้นให้กดได้
                backToPartyClientButton.onClick.AddListener(OnClientReadyButtonClicked);
                if (clientButtonText != null) clientButtonText.text = "Back to Party"; // ตั้งค่า Text เริ่มต้น
            }
            else
            {
                Debug.LogError("[BackToLobbyManager] Client Button is not assigned!");
            }
        }
        else if (IsHost) // ถ้าเป็น Host
        {
            if (backToPartyHostButton != null)
            {
                backToPartyHostButton.gameObject.SetActive(true);
                // เริ่มต้น Host กดไม่ได้ ต้องรอ Client พร้อมก่อน
                backToPartyHostButton.interactable = canHostReturn.Value;
                backToPartyHostButton.onClick.AddListener(OnHostReturnButtonClicked);
                 if (hostButtonText != null) hostButtonText.text = "Waiting for Players..."; // ตั้งค่า Text เริ่มต้น
            }
            else
            {
                Debug.LogError("[BackToLobbyManager] Host Button is not assigned!");
            }

            // Host ต้อง Subscribe Event การเปลี่ยนแปลงของ List และ Variable
            readyClients.OnListChanged += ServerOnReadyClientsChanged;
            canHostReturn.OnValueChanged += OnCanHostReturnChanged;

            // Server ต้องเคลียร์ List เมื่อเริ่ม (เผื่อกรณีย้อนกลับมาซีน Minigame ซ้ำ)
             if(IsServer)
             {
                 readyClients.Clear();
                 canHostReturn.Value = false; // รีเซ็ตสถานะ Host
                 CheckIfAllClientsReady(); // เช็คสถานะเผื่อมี Client คนเดียว (หรือไม่มีเลย)
             }
        }

         // Client ต้อง Subscribe Event การเปลี่ยนแปลงของ Variable เพื่ออัพเดทปุ่ม Host (ถ้าจำเป็นต้องแสดงสถานะ)
         // แต่ใน Requirement แค่ให้ Host กดได้เมื่อพร้อม เลยไม่จำเป็นต้องทำอะไรที่ Client ตอน canHostReturn เปลี่ยน
         // if (!IsHost)
         // {
         //     canHostReturn.OnValueChanged += OnCanHostReturnChanged;
         // }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        // Unsubscribe events เพื่อป้องกัน memory leak
        if (IsHost)
        {
            readyClients.OnListChanged -= ServerOnReadyClientsChanged;
            canHostReturn.OnValueChanged -= OnCanHostReturnChanged;
        }
        // if (!IsHost)
        // {
        //     canHostReturn.OnValueChanged -= OnCanHostReturnChanged;
        // }

        // ล้าง Listener ของปุ่ม (ถ้ายังอยู่)
        if (backToPartyClientButton != null) backToPartyClientButton.onClick.RemoveAllListeners();
        if (backToPartyHostButton != null) backToPartyHostButton.onClick.RemoveAllListeners();
    }

    // --- Client Logic ---

    private void OnClientReadyButtonClicked()
    {
        if (!IsClient || IsHost) return; // Client เท่านั้นที่กดปุ่มนี้

        Debug.Log($"[BackToLobbyManager] Client {NetworkManager.Singleton.LocalClientId} clicked ready.");

        // ทำให้ปุ่มกดซ้ำไม่ได้ และเปลี่ยนเป็นสีจาง (หรือเปลี่ยน Text)
        if (backToPartyClientButton != null)
        {
            backToPartyClientButton.interactable = false;
            if (clientButtonText != null) clientButtonText.text = "Ready!";
            // เพิ่มการเปลี่ยนสี ถ้าต้องการ
            // var colors = backToPartyClientButton.colors;
            // colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 0.5f); // ตั้งค่าสีจางๆ ตอน disable
            // backToPartyClientButton.colors = colors;
        }

        // ส่ง Request ไปให้ Server ว่าเราพร้อมแล้ว
        ClientReadyServerRpc();
    }

    [ServerRpc(RequireOwnership = false)] // Client เรียก Server
    private void ClientReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!readyClients.Contains(clientId))
        {
            readyClients.Add(clientId);
            Debug.Log($"[BackToLobbyManager] Server: Client {clientId} marked as ready. Ready count: {readyClients.Count}");
            // ไม่ต้องเรียก CheckIfAllClientsReady() ที่นี่ เพราะ OnListChanged จะถูก Trigger เอง
        }
        else
        {
             Debug.LogWarning($"[BackToLobbyManager] Server: Client {clientId} already marked as ready.");
        }
    }

    // --- Host Logic ---

    private void OnHostReturnButtonClicked()
    {
        if (!IsHost || !canHostReturn.Value) return; // Host เท่านั้นที่กด และต้องกดได้

        Debug.Log("[BackToLobbyManager] Host clicked return to lobby.");

        // ป้องกันการกดซ้ำ
        if(backToPartyHostButton != null) backToPartyHostButton.interactable = false;
        if (hostButtonText != null) hostButtonText.text = "Returning...";

        // สั่งให้ Server โหลด LobbyScene สำหรับทุกคน
        ReturnToLobbyServerRpc();
    }

    [ServerRpc(RequireOwnership = false)] // Host เรียก Server (ตัวเอง)
    private void ReturnToLobbyServerRpc()
    {
        if (!IsServer) return; // ต้องเป็น Server เท่านั้น

        // ตรวจสอบอีกครั้งว่า Client พร้อมจริงๆ ไหม (เผื่อกรณี Race Condition)
        if (!CheckIfAllClientsReadyInternal())
        {
             Debug.LogWarning("[BackToLobbyManager] Server: Host tried to return, but not all clients are ready. Aborting.");
              // อาจจะ Reset ปุ่ม Host ให้กดได้อีกครั้งถ้าต้องการ
             canHostReturn.Value = false; // บังคับให้ host กดไม่ได้
             return;
        }

        Debug.Log("[BackToLobbyManager] Server: All clients ready. Loading LobbyScene...");

        // ทำการโหลด LobbyScene
        // การใช้ LoadSceneMode.Single จะทำลาย Object ใน Scene ปัจจุบัน (รวมถึง Player Prefab ถ้าไม่ได้ตั้งค่า DontDestroyOnLoad)
        // และจะสร้าง Object ใหม่ใน LobbyScene ตาม Prefab ที่กำหนดใน NetworkManager
        // ConnectionApprovalManager จะทำงานอีกครั้ง (ถ้า Player Object ถูกสร้างใหม่) เพื่อกำหนดตำแหน่ง Spawn
        NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);

        // ไม่จำเป็นต้อง Clear readyClients หรือ Reset canHostReturn ที่นี่
        // เพราะเมื่อโหลด Scene ใหม่ OnNetworkSpawn จะทำงานและจัดการเอง
    }

    // --- Server Logic ---

    private void ServerOnReadyClientsChanged(NetworkListEvent<ulong> changeEvent)
    {
        // ถูกเรียกบน Server ทุกครั้งที่ List มีการเปลี่ยนแปลง (เพิ่ม/ลบ)
         Debug.Log($"[BackToLobbyManager] Server: Ready client list changed. Type: {changeEvent.Type}");
        CheckIfAllClientsReady();
    }

     private void CheckIfAllClientsReady()
    {
        if (!IsServer) return;
        canHostReturn.Value = CheckIfAllClientsReadyInternal();
    }

    private bool CheckIfAllClientsReadyInternal()
    {
        // นับจำนวน Client จริงๆ (ไม่รวม Host)
        int connectedClientCount = 0;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                connectedClientCount++;
            }
        }

         // ถ้าไม่มี Client เลย Host ก็กดกลับได้เลย
         if(connectedClientCount == 0)
         {
             Debug.Log("[BackToLobbyManager] Server: No clients connected. Host can return immediately.");
             return true;
         }

        // ตรวจสอบว่าจำนวน Client ที่พร้อม เท่ากับจำนวน Client ทั้งหมดหรือไม่
        bool allReady = readyClients.Count == connectedClientCount;
        Debug.Log($"[BackToLobbyManager] Server: Checking if all clients ready... Ready: {readyClients.Count}, Total Clients: {connectedClientCount}. Result: {allReady}");
        return allReady;
    }


    // --- UI Update Logic ---

    private void OnCanHostReturnChanged(bool oldValue, bool newValue)
    {
        // ถูกเรียกบน Host เมื่อค่า canHostReturn เปลี่ยน
        if (IsHost)
        {
            if (backToPartyHostButton != null)
            {
                backToPartyHostButton.interactable = newValue;
                if (hostButtonText != null)
                {
                    hostButtonText.text = newValue ? "Back to Party" : "Waiting for Players...";
                }
            }
        }
        // ถ้า Client ต้องอัพเดทอะไรบางอย่างเกี่ยวกับปุ่ม Host ก็ทำที่นี่
        // else
        // {
        //     // Client UI update based on host readiness (if needed)
        // }
    }
}