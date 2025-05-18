// File: Scripts/Managers/RoomListManager.cs
using UnityEngine;
using UnityEngine.UI; // Required for ScrollRect etc.
using System.Collections.Generic;
using System.Threading.Tasks; // Required for Task
using TMPro; // Required for TextMeshPro
using System.Linq; // Required for Linq operations like Where, OrderBy
using Unity.Netcode; // Required for NetworkManager check
using Unity.Services.Lobbies; // Required for UGS Lobby
using Unity.Services.Lobbies.Models; // Required for Lobby Models
using Unity.Services.Authentication; // For checking auth state
using Unity.Services.Core; // For checking service state


// Represents the data for a single room listing
[System.Serializable]
public class RoomInfo // Keep this structure for UI display
{
    // Add LobbyId if needed for potential future actions (like JoinById)
    public string lobbyId;
    public string hostName; // Could be Lobby Name or Host Name from data
    public string joinCode; // Relay Join Code obtained from Lobby Data (IF PUBLIC)
    public int currentPlayers;
    public int maxPlayers;
    public bool isPublic = true; // All lobbies fetched should be public
}

public class RoomListManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject roomListItemPrefab; // Prefab for each room row
    [SerializeField] private Transform roomListContentParent; // The content object of the ScrollView
    [SerializeField] private TMP_Text statusText; // Text to show "Loading...", "No rooms found", etc.
    [SerializeField] private Button refreshButton; // Optional: Button to manually refresh

    [Header("Manager References")]
    [SerializeField] private RoomPanelManager roomPanelManager; // To update status and handle join actions
    [SerializeField] private LobbyUI lobbyUI; // To initiate the join process

    private List<RoomInfo> currentRoomList = new List<RoomInfo>(); // Holds the fetched room data
    private bool isRefreshing = false; // Prevent concurrent fetches/actions
    private float autoRefreshTimer = 0f;
    private const float AUTO_REFRESH_INTERVAL = 10.0f; // Refresh every 10 seconds

    private void Awake()
    {
        if (roomPanelManager == null) roomPanelManager = FindObjectOfType<RoomPanelManager>();
        if (lobbyUI == null) lobbyUI = FindObjectOfType<LobbyUI>();
    }

    private void Start()
    {
        if(refreshButton != null)
        {
            refreshButton.onClick.AddListener(RefreshRoomList);
        }
    }

    private void OnEnable()
    {
        // Start refresh when the panel becomes active
        RefreshRoomList();
        autoRefreshTimer = AUTO_REFRESH_INTERVAL; // Start timer immediately
    }

     private void Update()
     {
         // Auto-refresh timer (only when parent is active)
         if (roomListContentParent != null && roomListContentParent.gameObject.activeInHierarchy && !isRefreshing) // Prevent timer while refreshing
         {
             autoRefreshTimer -= Time.deltaTime;
             if (autoRefreshTimer <= 0f)
             {
                 autoRefreshTimer = AUTO_REFRESH_INTERVAL;
                 RefreshRoomList();
             }
         }
     }

    // Call this method to fetch and display rooms
    public async void RefreshRoomList()
    {
        // --- เพิ่มการตรวจสอบสถานะ ---
        if (isRefreshing)
        {
             Debug.LogWarning("[RoomListManager] Already refreshing room list.");
             return;
        }
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            Debug.LogError("[RoomListManager] Unity Services not initialized. Cannot fetch lobbies.");
            if (statusText) statusText.text = "Services not ready.";
             // Optional: Add retry logic here if desired
            return; // ออกจากการทำงานถ้า Services ไม่พร้อม
        }
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            Debug.LogError("[RoomListManager] Not signed in to Authentication Service. Cannot fetch lobbies.");
            if (statusText) statusText.text = "Not signed in.";
            // Optional: Add retry logic here if desired
            return; // ออกจากการทำงานถ้ายังไม่ได้ Sign in
        }
         if (roomListContentParent == null || roomListItemPrefab == null)
         {
              Debug.LogError("[RoomListManager] UI References not set!");
              return;
         }
        // --- สิ้นสุดการตรวจสอบ ---

        isRefreshing = true; // เริ่ม Refresh
        if(refreshButton) refreshButton.interactable = false; // Disable refresh button during fetch
        if (statusText) statusText.text = "Loading Room List...";
        ClearRoomListUI(); // Clear immediately before fetching

        // --- Fetch Room Data ---
        List<Lobby> fetchedLobbies = null;
        try
        {
            fetchedLobbies = await FetchLobbiesAsync(); // เรียกใช้ await ที่นี่
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RoomListManager] Error fetching room list: {e.Message}");
            if (statusText) statusText.text = "Error loading rooms.";
            ClearRoomListUI(); // Ensure UI is clear on error
        }
        finally // Use finally to ensure flags/buttons are reset
        {
             isRefreshing = false; // เสร็จสิ้นการ Refresh
             if(refreshButton) refreshButton.interactable = true; // Re-enable refresh button
             autoRefreshTimer = AUTO_REFRESH_INTERVAL; // Reset timer after completion
        }

        // Proceed only if fetching succeeded (fetchedLobbies is not null)
        if (fetchedLobbies == null)
        {
             return; // Exit if fetching failed
        }


        // --- Map Lobbies to RoomInfo ---
        currentRoomList = MapLobbiesToRoomInfo(fetchedLobbies);
        // -------------------------------

        // --- Populate UI (Check parent validity again BEFORE populating) ---
         if (roomListContentParent == null)
         {
              Debug.LogWarning("[RoomListManager] Content parent became null after fetching rooms. Aborting UI update.");
              return;
         }

        // No need to clear again, already done

        if (currentRoomList == null || currentRoomList.Count == 0)
        {
            if (statusText) statusText.text = "No public rooms found.";
        }
        else
        {
            PopulateRoomListUI(currentRoomList);
            if (statusText) statusText.text = ""; // Clear status text if rooms found
        }
    }


    // --- Fetch Lobbies using UGS Lobby Service ---
    // ตรวจสอบว่า KEY_JOIN_CODE ถูกตั้งค่า Visibility เป็น Public หรือ Member
    // ถ้าเป็น Member การดึงค่า Join Code จาก QueryLobbiesAsync จะไม่ได้ผล
    // การ Join ต้องทำผ่าน JoinLobbyByIdAsync แทน แล้วค่อยดึง Join Code หลัง Join สำเร็จ
    private async Task<List<Lobby>> FetchLobbiesAsync()
    {
        Debug.Log("[RoomListManager] Querying UGS Lobbies...");
        try
        {
            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 25,
                Filters = new List<QueryFilter>()
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                    new QueryFilter(QueryFilter.FieldOptions.IsLocked, "0", QueryFilter.OpOptions.EQ) // 0 = Public
                },
                Order = new List<QueryOrder>()
                {
                    new QueryOrder(false, QueryOrder.FieldOptions.AvailableSlots),
                    new QueryOrder(true, QueryOrder.FieldOptions.Created)
                },
                 // --- เพิ่มส่วนดึงข้อมูล Data ---
                 // ระบุ Key ที่ต้องการดึงข้อมูลจาก Data ของ Lobby (เฉพาะที่ Visibility = Public)
                 // options.Data = new Dictionary<string, string> // Property 'Data' on 'QueryLobbiesOptions' is obsolete
                 // Use the QueryFilter method for data filtering if needed, or retrieve all public data by default
                 // Let's assume default behavior retrieves necessary public data like HostName.
                 // We'll check for the key's existence and visibility in MapLobbiesToRoomInfo.
                 // -----------------------------
            };


            QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync(options);

            if (queryResponse == null || queryResponse.Results == null)
            {
                Debug.LogWarning("[RoomListManager] UGS Lobby query returned null response or results.");
                return new List<Lobby>();
            }

            Debug.Log($"[RoomListManager] UGS Lobby Query Successful! Found {queryResponse.Results.Count} matching lobbies.");
            return queryResponse.Results;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"[RoomListManager] Failed to query UGS lobbies: {e}");
            throw;
        }
    }

    // --- Map UGS Lobbies to our RoomInfo structure ---
    private List<RoomInfo> MapLobbiesToRoomInfo(List<Lobby> lobbies)
    {
        if (lobbies == null) return new List<RoomInfo>();

        List<RoomInfo> roomInfoList = new List<RoomInfo>();
        foreach (Lobby lobby in lobbies)
        {
            string relayJoinCode = "N/A"; // Default
            // --- การดึง Join Code ---
            // ถ้า KEY_JOIN_CODE ตั้งค่า Visibility เป็น Public ใน LobbyManager.cs ตอน CreateLobby
            if (lobby.Data != null && lobby.Data.TryGetValue(LobbyManager.KEY_JOIN_CODE, out DataObject joinCodeData) && joinCodeData.Visibility == DataObject.VisibilityOptions.Public)
            {
                 relayJoinCode = joinCodeData.Value;
                 if(string.IsNullOrWhiteSpace(relayJoinCode)) relayJoinCode = "N/A";
                 // Debug.Log($"Lobby {lobby.Id}: Found PUBLIC JoinCode '{relayJoinCode}'");
            }
            // --- ถ้า KEY_JOIN_CODE เป็น Member ---
            // เราจะไม่สามารถดึง Join Code ได้จาก Query นี้
            // ปุ่ม Join ใน RoomListItem ควรจะเปลี่ยนไปทำงานโดยใช้ lobby.Id แทน
            // หรือต้องปรับให้ Join Code เป็น Public (ซึ่งไม่แนะนำด้านความปลอดภัย)
            else if (lobby.Data == null || !lobby.Data.ContainsKey(LobbyManager.KEY_JOIN_CODE))
            {
                 Debug.LogWarning($"Lobby {lobby.Id} ('{lobby.Name}') is missing Relay Join Code ('{LobbyManager.KEY_JOIN_CODE}') in its data OR it's not Public. Cannot be joined from list via code.");
            }
            else // Key exists but is not Public
            {
                 // Debug.LogWarning($"Lobby {lobby.Id} ('{lobby.Name}') has Join Code but it's not Public. Cannot be joined from list via code.");
                 // Still set to N/A as we can't use it directly from the list
                 relayJoinCode = "N/A";
            }
            // ------------------------

            string hostName = lobby.Name; // Default to Lobby Name
            // Check if HostName exists and is Public
            if (lobby.Data != null && lobby.Data.TryGetValue(LobbyManager.KEY_HOST_NAME, out DataObject hostNameData) && hostNameData.Visibility == DataObject.VisibilityOptions.Public)
            {
                if(!string.IsNullOrWhiteSpace(hostNameData.Value)) hostName = hostNameData.Value;
            }

            roomInfoList.Add(new RoomInfo
            {
                lobbyId = lobby.Id,
                hostName = hostName,
                joinCode = relayJoinCode, // Use the retrieved join code (if Public and valid)
                currentPlayers = lobby.Players?.Count ?? 0,
                maxPlayers = lobby.MaxPlayers,
                isPublic = !lobby.IsPrivate
            });
        }
        return roomInfoList;
    }


    // --- ส่วนที่เหลือของ Script (ClearRoomListUI, PopulateRoomListUI, JoinRoomFromList, AttemptQuickMatch) ---
     private void ClearRoomListUI()
     {
         if (roomListContentParent == null) return;
         for (int i = roomListContentParent.childCount - 1; i >= 0; i--)
         {
             // Add null check for child object before destroying
             GameObject childObject = roomListContentParent.GetChild(i)?.gameObject;
             if (childObject != null)
             {
                 Destroy(childObject);
             }
         }
     }

    private void PopulateRoomListUI(List<RoomInfo> rooms)
    {
         if (roomListContentParent == null || roomListItemPrefab == null || rooms == null || rooms.Count == 0) return;

         Debug.Log($"[RoomListManager] Populating UI with {rooms.Count} room items.");
         foreach (RoomInfo room in rooms)
         {
             if (roomListContentParent == null) break; // Check parent validity within loop
             GameObject newItem = Instantiate(roomListItemPrefab, roomListContentParent);
             RoomListItem itemScript = newItem.GetComponent<RoomListItem>();
             if (itemScript != null)
             {
                 // ส่ง RoomInfo และ Manager ไปให้ Item จัดการ UI และการ Join
                 itemScript.Setup(room, this);
             }
             else {
                  Debug.LogError("[RoomListManager] Instantiated RoomListItemPrefab is missing the RoomListItem script!");
                  Destroy(newItem);
             }
         }
    }

     // Called by RoomListItem when its Join button is clicked
     public void JoinRoomFromList(string joinCode) // Logic remains the same - uses Relay Join Code
     {
         if (lobbyUI != null)
         {
             if (string.IsNullOrEmpty(joinCode) || joinCode == "N/A")
             {
                 Debug.LogError("[RoomListManager] Cannot join room: Invalid or missing PUBLIC Join Code.");
                 if (roomPanelManager) roomPanelManager.SetBusyState("Cannot join: Missing info.", 3f);
                 // Optional: Re-enable the specific item's button after a delay if needed
                 return;
             }
             if (roomPanelManager) roomPanelManager.SetBusyState($"Joining Room {joinCode}...");
             _ = lobbyUI.AttemptClientJoinAsync(joinCode); // Delegate join attempt to LobbyUI
         }
         else { Debug.LogError("[RoomListManager] LobbyUI reference is missing, cannot join room!"); }
     }

     // Called by the Quick Match button
     public async void AttemptQuickMatch()
     {
        if (isRefreshing) { Debug.LogWarning("[RoomListManager] Already fetching/joining. Please wait."); return; }

         // --- Prerequisite Checks ---
         if (UnityServices.State != ServicesInitializationState.Initialized) { Debug.LogError("[RoomListManager] Unity Services not initialized. Cannot quick match."); if (roomPanelManager) roomPanelManager.SetBusyState("Services not ready.", 3f); return; }
         if (!AuthenticationService.Instance.IsSignedIn) { Debug.LogError("[RoomListManager] Not signed in. Cannot quick match."); if (roomPanelManager) roomPanelManager.SetBusyState("Not signed in.", 3f); return; }

         isRefreshing = true; // Use the same flag to prevent multiple actions
         if (roomPanelManager) roomPanelManager.SetBusyState("Finding a quick match...");

         List<Lobby> quickMatchLobbies = null;
         try
         {
             quickMatchLobbies = await FetchLobbiesAsync(); // Use the same fetch logic
         }
         catch (System.Exception e)
         {
             Debug.LogError($"[RoomListManager] Quick Match failed during room fetch: {e.Message}");
         }
         finally
         {
             isRefreshing = false; // Reset flag
             autoRefreshTimer = AUTO_REFRESH_INTERVAL; // Reset timer
         }

         if (quickMatchLobbies == null)
         {
             if (roomPanelManager) roomPanelManager.SetBusyState("Error finding games.", 3f);
             return;
         }

         List<RoomInfo> quickMatchRooms = MapLobbiesToRoomInfo(quickMatchLobbies);
         List<RoomInfo> joinableRooms = quickMatchRooms
             .Where(r => !string.IsNullOrEmpty(r.joinCode) && r.joinCode != "N/A") // Filter for valid public join codes
             .ToList();

         if (joinableRooms.Count > 0)
         {
             // Optional: Add better selection logic (e.g., prefer fuller rooms)
             int randomIndex = Random.Range(0, joinableRooms.Count);
             RoomInfo selectedRoom = joinableRooms[randomIndex];
             Debug.Log($"[RoomListManager] Quick Match found joinable room. Attempting to join room with code: {selectedRoom.joinCode}");
             JoinRoomFromList(selectedRoom.joinCode); // Use the same join logic
         }
         else
         {
             Debug.Log("[RoomListManager] Quick Match failed: No suitable rooms found (or missing public join codes).");
             if (roomPanelManager) roomPanelManager.SetBusyState("No rooms available for Quick Match.", 3f);
         }
     }
}