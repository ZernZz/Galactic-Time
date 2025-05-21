// File: Scripts/Lobby/LobbyManager.cs
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq; // Required for Linq queries
using System.Threading.Tasks; // Required for Task
using Unity.Services.Lobbies; // Required for UGS Lobby
using Unity.Services.Lobbies.Models; // Required for Lobby Models
using Unity.Services.Authentication;
using Unity.Collections; // Needed for FixedString in ReadyStatus

// Struct ReadyStatus remains unchanged
public struct ReadyStatus : INetworkSerializable, System.IEquatable<ReadyStatus>
{
    public ulong clientId;
    public bool isReady;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref isReady);
    }

    public bool Equals(ReadyStatus other)
    {
        return clientId == other.clientId && isReady == other.isReady;
    }

    public override int GetHashCode() => clientId.GetHashCode() ^ isReady.GetHashCode();
    public override bool Equals(object obj) => obj is ReadyStatus other && Equals(other);
}


public class LobbyManager : NetworkBehaviour
{
    // --- Singleton Pattern ---
    public static LobbyManager Instance { get; private set; }
    // -------------------------

    [SerializeField] private LobbyUI lobbyUI;

    [Header("Minigame Destinations")]
    [Tooltip("จับคู่ Planet Scene (สำหรับ Cutscene) กับ Minigame Scene จริง")]
    [SerializeField] private List<MinigameDestination> minigameDestinations = new List<MinigameDestination>();

    // Switched to NetworkList for better event handling
    public NetworkList<ReadyStatus> readyStatusList = new NetworkList<ReadyStatus>(
        null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    [Header("Matchmaking Settings")]
    public NetworkVariable<bool> isRoomPublic = new NetworkVariable<bool>(
        true, // Default to Public now based on user feedback/RoomList usage
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // --- UGS Lobby Variables ---
    private Lobby currentLobby; // Stores the UGS Lobby object for the host
    private float heartbeatTimer;
    private const float HEARTBEAT_INTERVAL = 15.0f; // Send heartbeat every 15 seconds
    private bool isCreatingOrJoiningLobby = false; // Prevent race conditions
    private bool isShuttingDown = false; // Flag to prevent actions during shutdown

    // --- Custom Data Keys ---
    public const string KEY_JOIN_CODE = "JoinCode"; // Key for Relay Join Code in Lobby data
    public const string KEY_HOST_NAME = "HostName"; // Key for Host Name in Lobby data (Optional)

    private void Awake()
    {
        // --- Singleton Setup ---
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[LobbyManager] Instance already exists on {Instance.gameObject.name}. Destroying duplicate on {gameObject.name}.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Consider DontDestroyOnLoad if LobbyManager needs to persist across scenes
        // DontDestroyOnLoad(gameObject);


        if (lobbyUI == null)
        {
            lobbyUI = FindObjectOfType<LobbyUI>();
            // It's okay if lobbyUI is null initially if this object persists and moves to the Lobby scene later
        }
    }

    // --- OnNetworkSpawn ---
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isShuttingDown = false; // Reset shutdown flag

        // Find UI again in case it wasn't found in Awake (e.g., persistent manager)
        if (lobbyUI == null)
        {
             lobbyUI = FindObjectOfType<LobbyUI>();
             if (lobbyUI == null && SceneManager.GetActiveScene().name == "LobbyScene")
             {
                  Debug.LogError("[LobbyManager] LobbyUI not found in LobbyScene on NetworkSpawn!");
             }
        }


        if (IsServer)
        {
             Debug.Log("[LobbyManager] OnNetworkSpawn (Server)");
             NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
             NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
             readyStatusList.OnListChanged += ServerOnReadyListChanged; // Subscribe to list changes
             isRoomPublic.OnValueChanged += OnRoomPrivacyChanged; // Server listens for changes

             if(NetworkManager.Singleton.SceneManager != null)
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += ServerOnSceneLoadComplete;
             else
                Debug.LogError("[LobbyManager] SceneManager is null! Cannot subscribe to OnLoadEventCompleted.");


             // --- UGS Lobby Creation / Update ---
             if (currentLobby == null && ConnectionManager.Instance != null && !string.IsNullOrEmpty(ConnectionManager.Instance.JoinCode))
             {
                 Debug.Log("[LobbyManager] Server: Detected fresh start or missing lobby. Attempting to create UGS Lobby.");
                 _ = CreateLobbyAsync(ConnectionManager.Instance.JoinCode, isRoomPublic.Value);
             }
             else if (currentLobby != null)
             {
                  Debug.Log($"[LobbyManager] Server: Returning to lobby with existing UGS Lobby ID: {currentLobby.Id}");
                  _ = UpdateLobbyPlayerDataAsync();
                  heartbeatTimer = 0f;
             }
             // ------------------------------------

             InitializeOrResetReadyList(); // Initialize/reset list for current clients

             Debug.Log($"[LobbyManager] Initial Ready List Count (Server): {readyStatusList.Count}");
             UpdateAllClientsReadyState(); // Check initial start button state
             UpdateLobbyPlayerCountUI();   // Update player count display
             if (lobbyUI != null) lobbyUI.UpdatePrivacyButton(isRoomPublic.Value);
        }
        if (IsClient)
        {
             Debug.Log($"[LobbyManager] OnNetworkSpawn (Client {NetworkManager.Singleton?.LocalClientId}) - Initial List Count: {readyStatusList.Count}");
             readyStatusList.OnListChanged += ClientOnReadyListChanged; // Client subscribes too
             isRoomPublic.OnValueChanged += OnRoomPrivacyChanged; // Client listens for changes
             UpdateLobbyPlayerCountUI();
             ClientUpdateUIFromList(); // Update ready icons based on current list
             if (lobbyUI != null) lobbyUI.UpdatePrivacyButton(isRoomPublic.Value);
        }
    }

     // --- Update for Host Heartbeat ---
     private void Update()
     {
         if (IsServer && currentLobby != null && !isShuttingDown)
         {
             heartbeatTimer += Time.deltaTime;
             if (heartbeatTimer >= HEARTBEAT_INTERVAL)
             {
                 heartbeatTimer = 0f;
                 SendHeartbeatAsync(); // Send heartbeat to keep lobby alive
             }
         }
     }


    // --- OnNetworkDespawn ---
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        Debug.Log($"[LobbyManager] OnNetworkDespawn (IsServer={IsServer}, IsClient={IsClient})");
        isShuttingDown = true; // Set shutdown flag

        // --- UGS Lobby Cleanup for Host ---
        if (IsServer && currentLobby != null)
        {
            // Assume NetworkManager shutdown triggers despawn, safe to delete lobby
             Debug.Log($"[LobbyManager] Server despawning. Attempting to delete UGS Lobby: {currentLobby.Id}");
             DeleteLobbyAsync(); // Attempt to delete the lobby
             currentLobby = null; // Clear reference
        }
        // ----------------------------------

        // --- Unsubscribe from Events ---
        if (NetworkManager.Singleton != null) {
            if (IsServer) {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                 if(NetworkManager.Singleton.SceneManager != null)
                     NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= ServerOnSceneLoadComplete;
            }
        }
        readyStatusList.OnListChanged -= ServerOnReadyListChanged;
        readyStatusList.OnListChanged -= ClientOnReadyListChanged;
        isRoomPublic.OnValueChanged -= OnRoomPrivacyChanged;
        // ---------------------------
    }

     // --- OnDestroy ---
     private void OnDestroy()
     {
        // Primarily handle singleton instance cleanup. Lobby deletion is better handled in Despawn.
        if (Instance == this)
        {
           // Debug.Log("[LobbyManager] Clearing static Instance reference in OnDestroy.");
           Instance = null;
        }
        // Optional: Add a final check for lobby deletion if OnNetworkDespawn might not have run
        // if (IsServer && currentLobby != null) {
        //     Debug.LogWarning($"[LobbyManager] OnDestroy called with active lobby ({currentLobby.Id}). Attempting last-resort delete.");
        //     DeleteLobbyAsync(); // Be cautious with async in OnDestroy
        // }
     }

     // Helper to check application quit status
     // (Keep IsApplicationQuitting helper as defined previously if needed, but rely more on isShuttingDown flag)


    // --- HandleClientConnected / HandleClientDisconnected ---
    private void HandleClientConnected(ulong clientId) {
        if (!IsServer || isShuttingDown) return;
        Debug.Log($"[LobbyManager] HandleClientConnected: {clientId}");

        if (clientId != NetworkManager.ServerClientId)
        {
            // Use try-catch for safety during list modification
            try
            {
                 bool found = false;
                 for(int i=0; i < readyStatusList.Count; i++)
                 {
                     if (readyStatusList[i].clientId == clientId)
                     {
                         found = true;
                         ReadyStatus status = readyStatusList[i];
                         if (status.isReady)
                         {
                             status.isReady = false;
                             readyStatusList[i] = status;
                             Debug.Log($"[LobbyManager] Client {clientId} reconnected/found in list. Resetting ready status.");
                         }
                         break;
                     }
                 }
                 if (!found)
                 {
                     readyStatusList.Add(new ReadyStatus { clientId = clientId, isReady = false });
                     Debug.Log($"[LobbyManager] Client {clientId} connected and added to ready list.");
                 }
            } catch (System.Exception e) {
                 Debug.LogError($"[LobbyManager] Exception during HandleClientConnected list modification for {clientId}: {e}");
            }
        }
        UpdateLobbyPlayerCountUI();
        _ = UpdateLobbyPlayerDataAsync(); // Update UGS Lobby
     }

    // --- แก้ไข HandleClientDisconnected ---
    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer || isShuttingDown)
        {
            // Log why we might be skipping
            // if (isShuttingDown) Debug.Log($"[LobbyManager] Skipping HandleClientDisconnected for {clientId} because manager is shutting down.");
            // else if (!IsServer) Debug.Log($"[LobbyManager] Skipping HandleClientDisconnected for {clientId} because not server.");
            return;
        }
        Debug.Log($"[LobbyManager] HandleClientDisconnected: {clientId}");

        // Remove client from the ready list
        if (clientId != NetworkManager.ServerClientId)
        {
            // ใช้ try-catch เพื่อดักจับ Error ที่อาจเกิดขึ้นตอนแก้ไข List ขณะ Shutdown
            try
            {
                bool removed = false;
                for (int i = readyStatusList.Count - 1; i >= 0; i--)
                {
                    // ตรวจสอบ Index ก่อนเข้าถึง (Safety check)
                    if (i < 0 || i >= readyStatusList.Count) {
                        Debug.LogWarning($"[LobbyManager] Invalid index {i} while removing client {clientId}. List count: {readyStatusList.Count}");
                        continue;
                    }

                    // ตรวจสอบ Object ก่อนเข้าถึง (Safety check for potential destruction race condition)
                    // 'this' check might not be sufficient if list operations cause issues deeper in Netcode
                    // Relying on the isShuttingDown flag at the start is generally safer.
                    // if (this == null || !this.isActiveAndEnabled) {
                    //      Debug.LogWarning($"[LobbyManager] LobbyManager is being destroyed while trying to remove client {clientId}. Aborting list modification.");
                    //      break; // หยุดถ้า Object กำลังถูกทำลาย
                    // }

                    if (readyStatusList[i].clientId == clientId)
                    {
                        // Check if the NetworkManager still considers this client connected.
                        // If not, it's safer to remove. If it somehow still is, maybe log a warning.
                        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) {
                            readyStatusList.RemoveAt(i);
                            removed = true;
                            Debug.Log($"[LobbyManager] Client {clientId} disconnected and removed from ready list.");
                        } else {
                            Debug.LogWarning($"[LobbyManager] Client {clientId} triggered disconnect callback, but might still be in ConnectedClients list OR NetworkManager is null? Skipping removal for now.");
                            // Potentially force removal if needed:
                            // readyStatusList.RemoveAt(i);
                            // removed = true;
                        }
                        break; // Exit loop once found
                    }
                }
                 if (!removed) {
                      Debug.LogWarning($"[LobbyManager] Client {clientId} disconnected but was not found in the ready list.");
                 }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LobbyManager] Exception during readyStatusList modification for client {clientId} disconnect: {e}\n{e.StackTrace}");
                // Consider logging the state of the list or network manager here
            }
        }

        // Update UI and UGS Lobby (should ideally be wrapped in try-catch)
         try {
            UpdateLobbyPlayerCountUI();
            _ = UpdateLobbyPlayerDataAsync(); // Update player count in UGS Lobby
         } catch (System.Exception e) {
              Debug.LogError($"[LobbyManager] Exception during UI/UGS update after client {clientId} disconnect: {e}");
         }

        // --- UGS Specific: Remove player from Lobby ---
        if (currentLobby != null)
        {
             // Add null checks and wrap in try-catch
             try
             {
                 // --- การลบ Player ออกจาก UGS Lobby ---
                 // You still need a reliable way to map Netcode ClientID to UGS Auth ID.
                 // If not implemented, this part will likely fail or be skipped.
                 // Placeholder for where the logic would go:
                 /*
                 if (clientIdToAuthIdMap.TryGetValue(clientId, out string authPlayerId))
                 {
                     Debug.Log($"[LobbyManager] Removing player {authPlayerId} (Client {clientId}) from UGS Lobby {currentLobby.Id}");
                     await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, authPlayerId);
                 } else {
                     Debug.LogWarning($"[LobbyManager] Cannot remove Client {clientId} from UGS Lobby: Authentication ID mapping not found.");
                 }
                 */
             }
             catch (LobbyServiceException e) { Debug.LogError($"[LobbyManager] Error removing player (Client {clientId}) from UGS Lobby: {e}"); }
             catch (System.Exception e) { Debug.LogError($"[LobbyManager] Unexpected error removing player (Client {clientId}) from UGS Lobby: {e}"); }
        }
    }
    // --- สิ้นสุดการแก้ไข HandleClientDisconnected ---


    // --- Server logic for handling changes in the ready list ---
    private void ServerOnReadyListChanged(NetworkListEvent<ReadyStatus> changeEvent)
    {
        if (isShuttingDown) return;
        // Debug.Log($"[LobbyManager] Server: Ready list changed. Type: {changeEvent.Type}, Value: {changeEvent.Value} at Index: {changeEvent.Index}");
        UpdateAllClientsReadyState(); // Re-evaluate host's start button state
    }

    // --- Client logic for handling ready status changes ---
    [ServerRpc(RequireOwnership = false)] // Client calls this
    public void SetReadyStatusServerRpc(bool isReady, ServerRpcParams rpcParams = default)
    {
        if (!IsServer || isShuttingDown) return; // Should only run on Server

        ulong clientId = rpcParams.Receive.SenderClientId;
        // Debug.Log($"[LobbyManager] Server: Received ready status update from Client {clientId}: {isReady}");

        // Use try-catch for safety
        try
        {
            for (int i = 0; i < readyStatusList.Count; i++)
            {
                if (readyStatusList[i].clientId == clientId)
                {
                    ReadyStatus updatedStatus = readyStatusList[i];
                     if (updatedStatus.isReady != isReady) // Only update if changed
                     {
                        updatedStatus.isReady = isReady;
                        readyStatusList[i] = updatedStatus; // Update the item in the list
                         Debug.Log($"[LobbyManager] Server: Updated Client {clientId}'s ready status in the list to {isReady}.");
                     }
                    return; // Exit once found
                }
            }
            Debug.LogWarning($"[LobbyManager] Server: Could not find Client {clientId} in the ready list to update status.");
        } catch (System.Exception e) {
             Debug.LogError($"[LobbyManager] Exception during SetReadyStatusServerRpc list modification for {clientId}: {e}");
        }
    }

    // --- Server logic to check if all clients are ready ---
    public void UpdateAllClientsReadyState()
    {
        if (!IsServer || isShuttingDown || NetworkManager.Singleton == null) return;

        int actualClientCount = 0;
        try { // Wrap client ID access in try-catch
             foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
             {
                 if (clientId != NetworkManager.ServerClientId) actualClientCount++;
             }
        } catch (System.Exception e) {
             Debug.LogError($"[LobbyManager] Error counting connected clients: {e}. Aborting readiness check.");
             if (lobbyUI != null) lobbyUI.UpdatePartyReadyState(false); // Ensure start is disabled
             return;
        }

        if (actualClientCount == 0)
        {
            // Debug.Log("[LobbyManager] No clients connected. Host cannot start game yet.");
            if (lobbyUI != null) lobbyUI.UpdatePartyReadyState(false);
            return;
        }

        int readyCount = 0;
        try { // Wrap list access in try-catch
             foreach (var status in readyStatusList)
             {
                 if (status.clientId != NetworkManager.ServerClientId && status.isReady)
                 {
                     readyCount++;
                 }
             }
        } catch (System.Exception e) {
            Debug.LogError($"[LobbyManager] Error reading readyStatusList: {e}. Aborting readiness check.");
            if (lobbyUI != null) lobbyUI.UpdatePartyReadyState(false); // Ensure start is disabled
             return;
        }

        bool allReady = (readyCount == actualClientCount);
        // Debug.Log($"[LobbyManager] Checking readiness: Ready Clients = {readyCount}, Total Clients = {actualClientCount}. All Ready = {allReady}");

        if (lobbyUI != null) lobbyUI.UpdatePartyReadyState(allReady);
    }

    // --- StartGame ---
    public void StartGame()
    {
         if (!IsServer || isShuttingDown) { Debug.LogWarning("[LobbyManager] StartGame called but not server or shutting down."); return; }

         // --- Re-check readiness just before starting ---
          int actualClientCount = 0;
          if (NetworkManager.Singleton == null) { Debug.LogError("NetworkManager is null in StartGame!"); return; }
          try {
               foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
               {
                   if (clientId != NetworkManager.ServerClientId) actualClientCount++;
               }
          } catch (System.Exception e) { Debug.LogError($"Error counting clients in StartGame: {e}"); return; }

         bool canStart = false;
         if (actualClientCount > 0)
         {
             int readyCountInList = 0;
             try {
                  foreach (var status in readyStatusList)
                  {
                      if (status.clientId != NetworkManager.ServerClientId && status.isReady)
                      {
                          readyCountInList++;
                      }
                  }
             } catch (System.Exception e) { Debug.LogError($"Error reading ready list in StartGame: {e}"); return; }
             canStart = (readyCountInList == actualClientCount);
             // Debug.Log($"[LobbyManager] StartGame Final Check: Ready={readyCountInList}, Total={actualClientCount}. Can Start={canStart}");
         }
         else
         {
              // Allow host to start if no clients? Or enforce minimum players?
              // For now, requires clients based on previous logic.
              Debug.LogWarning("[LobbyManager] Host cannot start game because there are no clients.");
              canStart = false;
         }

         if (!canStart)
         {
             Debug.LogWarning("[LobbyManager] Host clicked Start Game, but not all clients are ready or no clients present!");
             if (lobbyUI != null) lobbyUI.UpdatePartyReadyState(false); // Ensure button state is correct
             return;
         }
        // --- End Re-check ---


         if (minigameDestinations == null || minigameDestinations.Count == 0) { Debug.LogError("[LobbyManager] Cannot start game - Minigame Destinations list is empty or null!"); return; }

         // --- UGS Lobby: Make Lobby Private before starting ---
         if (currentLobby != null && !currentLobby.IsPrivate)
         {
             Debug.Log("[LobbyManager] Setting UGS Lobby to private before starting game...");
             _ = UpdateLobbyPrivacyAsync(true); // Make it private
         }
         // ------------------------------------------------------

         int index = Random.Range(0, minigameDestinations.Count);
         MinigameDestination selectedDestination = minigameDestinations[index];
         if (string.IsNullOrEmpty(selectedDestination.planetSceneName) || string.IsNullOrEmpty(selectedDestination.minigameSceneName)) { Debug.LogError($"[LobbyManager] Destination at Index {index} has invalid scene names!"); return; }

         if (ConnectionManager.Instance != null)
         {
             ConnectionManager.Instance.NextMinigameSceneToLoad = selectedDestination.minigameSceneName;
             // Debug.Log($"[LobbyManager] Stored next scene: {ConnectionManager.Instance.NextMinigameSceneToLoad}");
         }
         else { Debug.LogError("[LobbyManager] ConnectionManager.Instance is null! Cannot store next scene."); return; }

         string sceneToLoad = selectedDestination.planetSceneName;
         Debug.Log($"[LobbyManager] Host starting game! Loading Planet Scene: {sceneToLoad} (Next Minigame: {selectedDestination.minigameSceneName})");

         if (NetworkManager.Singleton.SceneManager != null)
         {
              var status = NetworkManager.Singleton.SceneManager.LoadScene(sceneToLoad, LoadSceneMode.Single);
               if (status != SceneEventProgressStatus.Started) {
                   Debug.LogWarning($"[LobbyManager] LoadScene status was not 'Started': {status}. Scene change might be delayed or fail.");
               }
         }
         else
         {
             Debug.LogError("[LobbyManager] Cannot load scene, NetworkManager.SceneManager is null!");
         }
    }

    // --- Client-side logic for handling list changes ---
    private void ClientOnReadyListChanged(NetworkListEvent<ReadyStatus> changeEvent)
    {
        if (isShuttingDown) return;
        // Debug.Log($"[LobbyManager] Client {NetworkManager.Singleton?.LocalClientId}: Ready list changed. Type: {changeEvent.Type}");
        ClientUpdateUIFromList(); // Update local UI based on the new list state
        UpdateLobbyPlayerCountUI(); // Update player count display
    }

    // --- Client-side UI update based on the full list ---
    private void ClientUpdateUIFromList()
    {
        if (lobbyUI == null || !IsClient || isShuttingDown) return;

        // This method currently doesn't do much in LobbyUI as there's no display
        // for *other* players' ready status. If that UI is added, call it here.
        // lobbyUI.UpdatePlayerReadyIcons(clientReadyStates); // Example call
        // Debug.Log("[LobbyManager] Client: Updating UI based on ready list (no specific other-player UI to update).");
    }

    // --- Update Player Count UI (Called by both Host and Client) ---
    private void UpdateLobbyPlayerCountUI()
    {
        // Added null check for lobbyUI
        if (lobbyUI != null && !isShuttingDown)
        {
            lobbyUI.UpdateLobbyPlayerCountUI(); // Delegate to LobbyUI
        }
    }

    // --- Server: Handle scene load completion ---
    private void ServerOnSceneLoadComplete(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
         if (!IsServer || isShuttingDown) return;

         Debug.Log($"[LobbyManager] Server: Scene '{sceneName}' load complete event received.");

         // Specific logic for returning to the LobbyScene
         if (sceneName == "LobbyScene") {
            Debug.Log($"[LobbyManager] Server: LobbyScene load complete. Repositioning players and resetting states.");
            RepositionPlayersToSpawnPoints();

             // --- UGS Lobby: Make Lobby Public again if needed ---
             if (currentLobby != null && currentLobby.IsPrivate && isRoomPublic.Value) // Check NetworkVar too
             {
                 Debug.Log("[LobbyManager] Setting UGS Lobby back to public.");
                 _ = UpdateLobbyPrivacyAsync(false);
             }
             // --- Reset Player Count & Heartbeat ---
             _ = UpdateLobbyPlayerDataAsync();
             heartbeatTimer = 0f;
             // -----------------------------------------

            // Reset ready statuses for all clients in the list
             // Use try-catch for safety
             try {
                 bool changed = false;
                 for (int i = 0; i < readyStatusList.Count; i++)
                 {
                     ReadyStatus status = readyStatusList[i];
                     if (status.isReady)
                     {
                         status.isReady = false;
                         readyStatusList[i] = status;
                         changed = true;
                     }
                 }
                 if (changed)
                 {
                     Debug.Log("[LobbyManager] Server: Reset all client ready statuses upon returning to Lobby.");
                 }
                 // Ensure host button state is correct regardless of changes
                 UpdateAllClientsReadyState();
             } catch (System.Exception e) {
                  Debug.LogError($"[LobbyManager] Error resetting ready list on Lobby scene load: {e}");
             }

             UpdateLobbyPlayerCountUI(); // Update UI count
          }
         else {
             // Debug.Log($"[LobbyManager] Server: Non-Lobby scene '{sceneName}' load complete.");
             // Add logic for other scenes if needed
         }
    }

    // --- Server: Reposition players based on stored spawn points ---
    private void RepositionPlayersToSpawnPoints()
    {
        if (!IsServer || isShuttingDown || ConnectionManager.Instance == null || NetworkManager.Singleton == null) return;
        Debug.Log("[LobbyManager] Repositioning players to their designated Lobby spawn points.");

        // Use try-catch for safety when accessing client objects
        try {
             foreach (var clientPair in NetworkManager.Singleton.ConnectedClients)
             {
                 ulong clientId = clientPair.Key;
                 NetworkObject playerObject = clientPair.Value?.PlayerObject; // Null check

                 if (playerObject != null)
                 {
                     Vector3 targetPosition = ConnectionManager.Instance.GetPlayerSpawnPosition(clientId);
                     Quaternion targetRotation = Quaternion.identity;

                     PlayerController pc = playerObject.GetComponent<PlayerController>();
                     if (pc != null)
                     {
                          // Debug.Log($"[LobbyManager] Sending ForceRepositionClientRpc to Client {clientId} -> Position: {targetPosition}");
                          pc.ForceRepositionClientRpc(targetPosition, targetRotation);
                     }
                     else
                     {
                          Debug.LogWarning($"[LobbyManager] Cannot reposition Client {clientId}: PlayerObject missing PlayerController component.");
                          // Fallback (less ideal):
                          // playerObject.transform.position = targetPosition;
                          // playerObject.transform.rotation = targetRotation;
                     }
                 }
                 else
                 {
                      Debug.LogWarning($"[LobbyManager] Cannot reposition Client {clientId}: PlayerObject is null.");
                 }
             }
         } catch (System.Exception e) {
              Debug.LogError($"[LobbyManager] Exception during RepositionPlayersToSpawnPoints: {e}");
         }
    }

     // --- Helper to Initialize or Reset the Ready List ---
     private void InitializeOrResetReadyList()
     {
         if (!IsServer || isShuttingDown || NetworkManager.Singleton == null) return;

         // Use try-catch for safety
         try {
             List<ulong> currentClientIds = NetworkManager.Singleton.ConnectedClientsIds
                                                 .Where(id => id != NetworkManager.ServerClientId)
                                                 .ToList();
             bool listChanged = false;

             // Remove statuses for clients no longer connected
             for (int i = readyStatusList.Count - 1; i >= 0; i--)
             {
                 if (i >= readyStatusList.Count) continue; // Bounds check
                 if (!currentClientIds.Contains(readyStatusList[i].clientId))
                 {
                     readyStatusList.RemoveAt(i);
                     listChanged = true;
                 }
             }

             // Add/Update statuses for currently connected clients
             foreach (ulong clientId in currentClientIds)
             {
                 bool found = false;
                 for (int i = 0; i < readyStatusList.Count; i++)
                 {
                     if (readyStatusList[i].clientId == clientId)
                     {
                         found = true;
                         ReadyStatus status = readyStatusList[i];
                         if (status.isReady) // Only change if currently ready
                         {
                             status.isReady = false;
                             readyStatusList[i] = status;
                             listChanged = true;
                         }
                         break;
                     }
                 }
                 if (!found)
                 {
                     readyStatusList.Add(new ReadyStatus { clientId = clientId, isReady = false });
                     listChanged = true;
                 }
             }

             if (listChanged)
             {
                 // Debug.Log("[LobbyManager] Initialized/Reset Ready List on Server.");
             }
         } catch (System.Exception e) {
              Debug.LogError($"[LobbyManager] Exception during InitializeOrResetReadyList: {e}");
         }
     }


    // --- Matchmaking Specific Methods ---

    [ServerRpc(RequireOwnership = true)] // Changed to RequireOwnership=true for security
    public void ToggleRoomPrivacyServerRpc(ServerRpcParams rpcParams = default)
    {
        // Ensure only the host can toggle privacy (already enforced by RequireOwnership=true)
        if (!IsServer || isShuttingDown) return;

        isRoomPublic.Value = !isRoomPublic.Value;
        Debug.Log($"[LobbyManager] Host toggled room privacy. New state: {(isRoomPublic.Value ? "Public" : "Private")}");

        // --- UGS Lobby: Update Privacy ---
        _ = UpdateLobbyPrivacyAsync(!isRoomPublic.Value); // UGS IsPrivate is opposite
    }

    // Called on both Server and Clients when the NetworkVariable changes
    private void OnRoomPrivacyChanged(bool previousValue, bool newValue)
    {
        if (isShuttingDown) return;
        // Debug.Log($"[LobbyManager] Privacy changed from {previousValue} to {newValue} (IsServer: {IsServer}, IsClient: {IsClient})");
        if (lobbyUI != null)
        {
            lobbyUI.UpdatePrivacyButton(newValue); // Update UI button text/state
        }

        // --- UGS Lobby Sync (Server Side Check) ---
        if (IsServer && currentLobby != null)
        {
            bool targetUgsIsPrivate = !newValue;
            if (currentLobby.IsPrivate != targetUgsIsPrivate)
            {
                 // Debug.Log($"[LobbyManager] NetworkVariable privacy changed. Syncing UGS Lobby IsPrivate to: {targetUgsIsPrivate}");
                 _ = UpdateLobbyPrivacyAsync(targetUgsIsPrivate); // Update UGS Lobby
            }
        }
    }

    // Helper method for easy access if needed elsewhere
    public bool IsRoomCurrentlyPublic()
    {
        return isRoomPublic.Value;
    }


    // ========================================
    // === UGS Lobby Specific Async Methods ===
    // ========================================

    private async Task CreateLobbyAsync(string relayJoinCode, bool startPublic)
    {
        if (!IsServer || isCreatingOrJoiningLobby || currentLobby != null || isShuttingDown) return;
        isCreatingOrJoiningLobby = true;

        // Add check for Authentication sign-in
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            Debug.LogError("[LobbyManager] Cannot create UGS Lobby: Not signed in!");
            isCreatingOrJoiningLobby = false;
            return;
        }


        try
        {
            string hostName = "Unnamed Host";
            PlayerNameManager nameManager = null;
             if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null)
             {
                  nameManager = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerNameManager>();
             }
             // Wait briefly for name to potentially sync if needed
             if (nameManager != null && nameManager.playerName.Value.IsEmpty) {
                 await Task.Delay(200); // Wait a short time
             }
             if (nameManager != null && !nameManager.playerName.Value.IsEmpty) {
                  hostName = nameManager.playerName.Value.ToString();
             }


            int maxPlayers = ConnectionManager.Instance != null ? ConnectionManager.Instance.MaxPlayers : 4;
            string lobbyName = $"{hostName}'s Game"; // Example Lobby name            // Get current player count (should be 1 for a new lobby host)
            int currentPlayerCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
            
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = !startPublic,
                Data = new Dictionary<string, DataObject>()
                {
                    // --- Visibility Change ---
                    // Make Join Code Public for Room List functionality
                    { KEY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode) },
                    // Host Name remains Public
                    { KEY_HOST_NAME, new DataObject(DataObject.VisibilityOptions.Public, hostName) },
                    // --- Add Player Count Information ---
                    { KEY_CURRENT_PLAYERS, new DataObject(DataObject.VisibilityOptions.Public, currentPlayerCount.ToString()) },
                    { KEY_MAX_PLAYERS, new DataObject(DataObject.VisibilityOptions.Public, maxPlayers.ToString()) },
                    // ------------------------
                }
            };

            Debug.Log($"[LobbyManager] Creating UGS Lobby '{lobbyName}' (Public: {startPublic}, MaxPlayers: {maxPlayers}, JoinCode: {relayJoinCode}, Host: {hostName})");

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
            currentLobby = lobby; // Store the created lobby

            Debug.Log($"[LobbyManager] UGS Lobby Created Successfully! Lobby ID: {currentLobby.Id}, Lobby Code: {currentLobby.LobbyCode}");

            heartbeatTimer = 0f;

        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyManager] Failed to create UGS lobby: {e}");
            if (lobbyUI != null) lobbyUI.UpdatePrivacyButton(startPublic);
        }
        catch (System.Exception e) // Catch other potential errors
        {
             Debug.LogError($"[LobbyManager] Unexpected error creating UGS lobby: {e}");
        }
        finally
        {
             isCreatingOrJoiningLobby = false;
        }
    }

    private async void SendHeartbeatAsync()
    {
        if (!IsServer || currentLobby == null || isShuttingDown) return;

        try
        {
             await LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
             // Debug.Log($"[LobbyManager] Heartbeat sent for Lobby ID: {currentLobby.Id}");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyManager] Failed to send heartbeat for lobby {currentLobby.Id}: {e}");
            // Lobby might expire - Consider notifying host or attempting recovery
            currentLobby = null; // Assume lobby is lost
            // Maybe transition back to main menu? Shutdown?
        }
         catch (System.Exception e) {
             Debug.LogError($"[LobbyManager] Unexpected error sending heartbeat: {e}");
             currentLobby = null;
         }
    }

    private async Task UpdateLobbyPrivacyAsync(bool isPrivate) // Parameter is UGS 'IsPrivate'
    {
        if (!IsServer || currentLobby == null || isShuttingDown) return;
        if (currentLobby.IsPrivate == isPrivate) return; // No change needed

        try
        {
             // Debug.Log($"[LobbyManager] Updating UGS Lobby {currentLobby.Id} privacy to IsPrivate = {isPrivate}");
             currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, new UpdateLobbyOptions { IsPrivate = isPrivate });
             // Debug.Log($"[LobbyManager] UGS Lobby privacy updated successfully.");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyManager] Failed to update lobby privacy: {e}");
            // Revert NetworkVariable if UGS update fails?
            if (isRoomPublic.Value == isPrivate) { // If NetworkVar matches failed target state
                isRoomPublic.Value = !isPrivate; // Revert it
                Debug.LogWarning("[LobbyManager] Reverted NetworkVariable 'isRoomPublic' due to UGS update failure.");
            }
        }
        catch (System.Exception e) {
             Debug.LogError($"[LobbyManager] Unexpected error updating lobby privacy: {e}");
        }
    }    // Made public so PlayerNameManager can call it when host name changes    // Constants for player count data keys
    private const string KEY_CURRENT_PLAYERS = "CurrentPlayers";
    private const string KEY_MAX_PLAYERS = "MaxPlayers";

    public async Task UpdateLobbyPlayerDataAsync()
    {
        if (!IsServer || currentLobby == null || isShuttingDown) return;

        try
        {
             string hostName = "Unnamed Host";
             PlayerNameManager nameManager = null;
              if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null)
              {
                   nameManager = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerNameManager>();
              }
              if (nameManager != null && nameManager.playerName.Value.IsEmpty) {
                  await Task.Delay(100); // Brief delay for name sync
              }
              if (nameManager != null && !nameManager.playerName.Value.IsEmpty) {
                   hostName = nameManager.playerName.Value.ToString();
              }

             Debug.Log($"[LobbyManager] UpdateLobbyPlayerDataAsync checking host name: '{hostName}'");
             
             // Prepare data object, only include fields that might change
             Dictionary<string, DataObject> dataToUpdate = new Dictionary<string, DataObject>();
             bool changed = false;

             // Check Host Name - Force update if the name exists in the player manager
             // This ensures host name updates even when alone in room
             if (nameManager != null && !nameManager.playerName.Value.IsEmpty)
             {
                 // Always update the host name when we have a valid name
                 dataToUpdate[KEY_HOST_NAME] = new DataObject(DataObject.VisibilityOptions.Public, hostName);
                 changed = true;
                 Debug.Log($"[LobbyManager] Updating host name to: '{hostName}'");
             }
             else if (!currentLobby.Data.TryGetValue(KEY_HOST_NAME, out var currentHostNameData) || currentHostNameData.Value != hostName)
             {
                 dataToUpdate[KEY_HOST_NAME] = new DataObject(DataObject.VisibilityOptions.Public, hostName);
                 changed = true;
             }

             // Always update player count information, regardless of change
             int currentPlayerCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
             int maxPlayerCount = ConnectionManager.Instance != null ? ConnectionManager.Instance.MaxPlayers : 4;
             
             // Update current player count
             string currentPlayerCountStr = currentPlayerCount.ToString();
             if (!currentLobby.Data.TryGetValue(KEY_CURRENT_PLAYERS, out var currentPlayersData) || 
                 currentPlayersData.Value != currentPlayerCountStr)
             {
                 dataToUpdate[KEY_CURRENT_PLAYERS] = new DataObject(DataObject.VisibilityOptions.Public, currentPlayerCountStr);
                 changed = true;
                 Debug.Log($"[LobbyManager] Updating player count to: {currentPlayerCount}/{maxPlayerCount}");
             }
             
             // Update max player count
             string maxPlayerCountStr = maxPlayerCount.ToString();
             if (!currentLobby.Data.TryGetValue(KEY_MAX_PLAYERS, out var maxPlayersData) || 
                 maxPlayersData.Value != maxPlayerCountStr)
             {
                 dataToUpdate[KEY_MAX_PLAYERS] = new DataObject(DataObject.VisibilityOptions.Public, maxPlayerCountStr);
                 changed = true;
             }

             if (changed)
             {
                  UpdateLobbyOptions options = new UpdateLobbyOptions { Data = dataToUpdate };
                  Debug.Log($"[LobbyManager] Updating UGS Lobby ({currentLobby.Id}) Data with host name and player count");
                  currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, options);
                  Debug.Log($"[LobbyManager] UGS Lobby data updated successfully.");
             } else {
                  Debug.Log("[LobbyManager] No lobby data changes detected. Skipping UGS update.");
             }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[LobbyManager] Failed to update lobby data: {e}");
        }
        catch (System.Exception e) {
             Debug.LogError($"[LobbyManager] Unexpected error updating lobby data: {e}");
        }
    }


    private async void DeleteLobbyAsync()
    {
        if (!IsServer || currentLobby == null) return; // No need to check isShuttingDown, this is called *during* shutdown

        string lobbyIdToDelete = currentLobby.Id;
        currentLobby = null; // Clear instance reference *before* await

        // Ensure signed in before trying to delete
        if (!AuthenticationService.Instance.IsSignedIn)
        {
             Debug.LogWarning($"[LobbyManager] Cannot delete lobby {lobbyIdToDelete}: Not signed in.");
             return;
        }

        try
        {
            Debug.Log($"[LobbyManager] Deleting UGS Lobby: {lobbyIdToDelete}");
            await LobbyService.Instance.DeleteLobbyAsync(lobbyIdToDelete);
            Debug.Log($"[LobbyManager] UGS Lobby {lobbyIdToDelete} deleted successfully.");
        }
        catch (LobbyServiceException e)
        {
            // Common errors: 404 (Not Found), 403 (Forbidden)
            Debug.LogWarning($"[LobbyManager] Failed to delete lobby {lobbyIdToDelete} (might be already gone or error): {e.Reason} ({e.ErrorCode})");
        }
        catch (System.Exception e) // Catch potential null refs if service disappears during await
        {
             Debug.LogError($"[LobbyManager] Unexpected error deleting lobby {lobbyIdToDelete}: {e}");
        }
    }
}