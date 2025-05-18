// File: Scripts/Network/ConnectionManager.cs
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine.SceneManagement; // Needed for scene checks


public class ConnectionManager : NetworkBehaviour
{
    public static ConnectionManager Instance { get; private set; }
    public string JoinCode { get; set; } // Relay Join Code

    [Header("Maximum Players (including Host)")]
    [SerializeField] private int maxPlayers = 4;
    public int MaxPlayers => maxPlayers;

    // Stores spawn positions assigned by ConnectionApprovalManager
    public Dictionary<ulong, Vector3> PlayerLobbySpawnPositions { get; private set; } = new Dictionary<ulong, Vector3>();
    // Stores the specific minigame scene to load after the planet cutscene
    public string NextMinigameSceneToLoad { get; set; } = string.Empty;

    private bool servicesInitialized = false; // Track initialization state

    private async void Start()
    {
        // Ensure this runs only once, ideally after Awake sets the singleton
        if (Instance != this) return; // If this is a duplicate, don't initialize services again

        await InitializeServicesAsync();
    }

    private async Task InitializeServicesAsync()
    {
         // --- Ensure Unity Services are initialized ---
         // Add checks to prevent multiple initializations
         if (servicesInitialized) return;

         try {
             if (UnityServices.State == ServicesInitializationState.Uninitialized)
             {
                 await UnityServices.InitializeAsync();
                 Debug.Log("[ConnectionManager] Unity Services Initialized.");
             } else if (UnityServices.State == ServicesInitializationState.Initializing) {
                 Debug.LogWarning("[ConnectionManager] Unity Services are already initializing. Waiting...");
                 // Wait until initialized or timeout (optional)
                 float timer = 0f;
                 while (UnityServices.State == ServicesInitializationState.Initializing && timer < 5f) {
                     await Task.Delay(100);
                     timer += 0.1f;
                 }
                 if (UnityServices.State != ServicesInitializationState.Initialized) {
                     Debug.LogError("[ConnectionManager] Unity Services failed to initialize after waiting.");
                     return; // Exit if still not initialized
                 }
             }

             if (!AuthenticationService.Instance.IsSignedIn)
             {
                 AuthenticationService.Instance.SignedIn += () => {
                     //Debug.Log($"[ConnectionManager] Signed in anonymously as Player ID: {AuthenticationService.Instance.PlayerId}");
                 };
                 AuthenticationService.Instance.SignInFailed += (err) => {
                      Debug.LogError($"[ConnectionManager] Anonymous sign-in failed: {err.Message}");
                 };
                 await AuthenticationService.Instance.SignInAnonymouslyAsync();
                 // Wait a frame to ensure SignedIn event propagates if needed
                 await Task.Yield();
             }

             // If initialization and sign-in succeeded:
             if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn)
             {
                 servicesInitialized = true;
                 Debug.Log($"[ConnectionManager] Services Initialized & Signed In (Player ID: {AuthenticationService.Instance.PlayerId})");
             }

        } catch (System.Exception e) {
             Debug.LogError($"[ConnectionManager] Error during service initialization or sign-in: {e.Message}\n{e.StackTrace}");
             servicesInitialized = false; // Ensure flag is false on error
        }
    }

    private void Awake()
    {
        // --- เพิ่ม Log ตรวจสอบ Parent ก่อน Singleton Check ---
        Debug.Log($"[ConnectionManager] Awake called for GameObject: {gameObject.name} (Instance Name: {this.GetInstanceID()}, Parent: {(transform.parent == null ? "None" : transform.parent.name)}, Scene: {gameObject.scene.name})");
        // -----------------------------------------------

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ConnectionManager] Instance already exists on {Instance.gameObject.name} (ID: {Instance.GetInstanceID()}). Destroying duplicate on {gameObject.name} (ID: {this.GetInstanceID()}).");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log($"[ConnectionManager] Assigning Singleton Instance (ID: {Instance.GetInstanceID()}) to GameObject: {gameObject.name}");

        // --- ตรวจสอบ Parent และ Unparent ถ้าจำเป็น ---
        // Ensure this happens *before* DontDestroyOnLoad
        if (transform.parent != null)
        {
            Debug.LogWarning($"[ConnectionManager] GameObject '{gameObject.name}' is parented under '{transform.parent.name}'. Unparenting to ensure DontDestroyOnLoad works correctly.");
            transform.SetParent(null); // ทำให้เป็น Root object
        }
        // ---------------------------------------------

        // Apply DontDestroyOnLoad only *after* ensuring it's a root object and the singleton is set
        Debug.Log($"[ConnectionManager] Applying DontDestroyOnLoad to GameObject: {gameObject.name} (ID: {this.GetInstanceID()})");
        DontDestroyOnLoad(gameObject);
    }


    // --- ตรวจสอบ OnDestroy เพิ่มเติม ---
     private void OnDestroy()
     {
          Debug.Log($"[ConnectionManager] OnDestroy called for GameObject: {gameObject.name} (ID: {this.GetInstanceID()}, Scene: {gameObject.scene.name})");

          if (Instance == this) // เช็คว่าเป็น Singleton ตัวจริงที่กำลังถูกทำลายหรือไม่
          {
              // Log เป็น Error ถ้า Singleton ตัวจริงถูกทำลาย (ไม่ควรเกิดขึ้นถ้า DontDestroyOnLoad สำเร็จ)
              // ยกเว้นกรณีปิด Application
              if (!IsApplicationQuitting()) // Use a helper to check if the app is closing normally
              {
                   Debug.LogError($"[ConnectionManager] OnDestroy called for the ACTIVE Singleton (ID: {this.GetInstanceID()})! This indicates DontDestroyOnLoad failed or was bypassed somehow. Clearing Instance reference.");
              } else {
                   Debug.Log($"[ConnectionManager] OnDestroy called for ACTIVE Singleton (ID: {this.GetInstanceID()}) during application quit.");
              }
              Instance = null; // สำคัญ: ต้องเคลียร์ Instance ด้วย
          }
          else
          {
               // Log เป็น Info ถ้าเป็นแค่ตัว Duplicate ที่ถูกทำลายใน Awake
               // Debug.Log($"[ConnectionManager] OnDestroy called for a non-singleton or duplicate instance: {gameObject.name} (ID: {this.GetInstanceID()})");
          }

          // ยกเลิกการ Subscribe Event ต่างๆ ถ้ามี
          // Example: If subscribed to NetworkManager events elsewhere
          // if (NetworkManager.Singleton != null) {
          //     NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect; // Assuming HandleClientDisconnect exists
          // }
     }
    // --- สิ้นสุดการตรวจสอบ OnDestroy ---

     // Helper to check if application is quitting
     private static bool _appQuitting = false;
     private void OnApplicationQuit() { _appQuitting = true; }
     public static bool IsApplicationQuitting() { return _appQuitting; }


    // --- Relay and Connection Methods ---
    public async Task StartHostAsync()
    {
        if (!servicesInitialized) {
             Debug.LogWarning("[ConnectionManager] Services not ready. Attempting initialization again...");
             await InitializeServicesAsync();
             if (!servicesInitialized) {
                  throw new System.InvalidOperationException("Unity Services could not be initialized or signed in.");
             }
        }

        if (NetworkManager.Singleton == null) {
             Debug.LogError("[ConnectionManager] StartHostAsync failed: NetworkManager.Singleton is null.");
             throw new System.InvalidOperationException("NetworkManager is not available.");
        }
        try {
            // Use MaxPlayers field, allocation is for clients (MaxPlayers - 1)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[ConnectionManager] Host: Relay Allocation created. Join Code = {JoinCode}");

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null) {
                 Debug.LogError("[ConnectionManager] StartHostAsync failed: UnityTransport not found on NetworkManager.");
                 throw new System.InvalidOperationException("UnityTransport is missing.");
            }
            RelayServerData relayServerData = new RelayServerData(allocation, "dtls"); // Use "dtls" for encryption
            transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost(); // Start hosting
            Debug.Log("[ConnectionManager] NetworkManager.StartHost() called.");

            // Optionally: Update LobbyManager's public status here if needed immediately
             if (LobbyManager.Instance != null) {
                  // LobbyManager.Instance.isRoomPublic.Value = true; // Set initial public state if desired
             }


        } catch (System.Exception e) {
             Debug.LogError($"[ConnectionManager] StartHostAsync Exception: {e.Message}\n{e.StackTrace}");
             JoinCode = null; // Reset JoinCode on failure
             throw; // Re-throw the exception so the caller knows it failed
        }
    }

    public async Task StartClientAsync(string joinCode)
    {
         if (!servicesInitialized) {
             Debug.LogWarning("[ConnectionManager] Services not ready. Attempting initialization again...");
             await InitializeServicesAsync();
             if (!servicesInitialized) {
                  throw new System.InvalidOperationException("Unity Services could not be initialized or signed in.");
             }
         }

         if (NetworkManager.Singleton == null) {
              Debug.LogError("[ConnectionManager] StartClientAsync failed: NetworkManager.Singleton is null.");
              throw new System.InvalidOperationException("NetworkManager is not available.");
         }
         if (string.IsNullOrWhiteSpace(joinCode)) {
              Debug.LogError("[ConnectionManager] StartClientAsync failed: Join Code is empty.");
               throw new System.ArgumentException("Join Code cannot be empty.", nameof(joinCode));
         }
         try {
            Debug.Log($"[ConnectionManager] Client: Attempting to join allocation with code: {joinCode}");
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            Debug.Log("[ConnectionManager] Client: Joined Relay Allocation successfully.");

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
             if (transport == null) {
                  Debug.LogError("[ConnectionManager] StartClientAsync failed: UnityTransport not found on NetworkManager.");
                  throw new System.InvalidOperationException("UnityTransport is missing.");
             }
            RelayServerData relayServerData = new RelayServerData(allocation, "dtls"); // Use "dtls"
            transport.SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient(); // Start client
             Debug.Log("[ConnectionManager] NetworkManager.StartClient() called.");

        } catch (System.Exception e) {
             Debug.LogError($"[ConnectionManager] StartClientAsync Exception: {e.Message}\n{e.StackTrace}");
             throw; // Re-throw the exception
        }
    }

    // --- Shutdown Method ---
    public void Shutdown()
    {
        // Add safety check for Instance
        if (Instance != this && Instance != null) {
             Debug.LogWarning($"[ConnectionManager] Shutdown called on a non-singleton instance ({this.GetInstanceID()}). The active instance is {Instance.GetInstanceID()}. Ignoring shutdown request for this instance.");
             return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log($"[ConnectionManager] Shutdown() called for instance {this.GetInstanceID()}. Shutting down NetworkManager.");
            NetworkManager.Singleton.Shutdown(); // Stop network activity
        } else if (NetworkManager.Singleton == null) {
             Debug.LogWarning($"[ConnectionManager] Shutdown() called for instance {this.GetInstanceID()}, but NetworkManager.Singleton is null.");
        } else {
             //Debug.LogWarning($"[ConnectionManager] Shutdown() called for instance {this.GetInstanceID()}, but NetworkManager is not listening (already shut down?).");
        }

        // Clear local data associated with the session only if this is the active instance
        // (Though Shutdown should ideally only be called on the active instance)
        PlayerLobbySpawnPositions.Clear();
        NextMinigameSceneToLoad = string.Empty;
        JoinCode = null;
        Debug.Log($"[ConnectionManager] Cleared local data for instance {this.GetInstanceID()} on shutdown.");

        // Consider resetting servicesInitialized flag if appropriate for your flow
        // servicesInitialized = false;
    }

    // --- Player Spawn Position Handling ---
     public void RegisterPlayerSpawnPosition(ulong clientId, Vector3 position)
    {
        // Allow registration even if called on non-singleton? Maybe not ideal.
        // if (Instance != this) return;

        if (PlayerLobbySpawnPositions.ContainsKey(clientId))
        {
            PlayerLobbySpawnPositions[clientId] = position;
            // Debug.Log($"[ConnectionManager] Updated Lobby Spawn Position for Client {clientId} to {position}");
        }
        else
        {
            PlayerLobbySpawnPositions.Add(clientId, position);
            // Debug.Log($"[ConnectionManager] Registered Lobby Spawn Position for Client {clientId} at {position}");
        }
    }

    public Vector3 GetPlayerSpawnPosition(ulong clientId)
    {
        // Allow reading even if called on non-singleton?
        // if (Instance != this) return Vector3.zero;

        if (PlayerLobbySpawnPositions.TryGetValue(clientId, out Vector3 position))
        {
            return position;
        }
        else
        {
            Debug.LogWarning($"[ConnectionManager] Could not find Lobby Spawn Position for Client {clientId}. Returning Vector3.zero.");
            return Vector3.zero; // Return default spawn point
        }
    }

    // Called by LobbyManager or ConnectionApprovalManager when a client disconnects
    public void HandleClientDisconnect(ulong clientId)
    {
        // Allow handling even if called on non-singleton?
        // if (Instance != this) return;

        if (PlayerLobbySpawnPositions.ContainsKey(clientId))
        {
            PlayerLobbySpawnPositions.Remove(clientId);
            Debug.Log($"[ConnectionManager] Removed Lobby Spawn Position for disconnected Client {clientId}.");
        }
    }
}