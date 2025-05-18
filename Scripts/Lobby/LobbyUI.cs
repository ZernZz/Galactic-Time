using System.Collections;
using System.Collections.Generic; // Needed for List
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class LobbyUI : MonoBehaviour
{
    // --- Enum and Scene Selection ---
    private enum SceneType { MainMenu, Lobby }
    [Header("Scene Association")]
    [Tooltip("กำหนดว่าเป็น UI สำหรับ MainMenu หรือ Lobby")]
    [SerializeField] private SceneType currentSceneType = SceneType.MainMenu;

    // --- UI References for MainMenuScene ---
    [Header("Main Menu UI (MainMenuScene)")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private Button playButton; // New Play Button
    [SerializeField] private Button optionsButton; // New Options Button
    [SerializeField] private Button quitButton;
    [SerializeField] private GameObject roomPanel; // Panel for matchmaking options
    [SerializeField] private TextMeshProUGUI mainMenuStatusText;

    // --- UI References for RoomPanel (Inside MainMenuScene) ---
    [Header("Room Panel UI (MainMenuScene)")]
    [SerializeField] private Button roomPanel_HostButton;
    [SerializeField] private Button roomPanel_JoinByCodeButton; // Renamed Join Button
    [SerializeField] private Button roomPanel_QuickMatchButton;
    [SerializeField] private Button roomPanel_CancelButton;
    [SerializeField] private GameObject roomPanel_JoinCodePanel; // The existing panel for entering code
    [SerializeField] private TMP_InputField roomPanel_JoinCodeInputField;
    [SerializeField] private Button roomPanel_ClientPrivateButton; // Renamed Client Button inside JoinCodePanel
    [SerializeField] private Button roomPanel_CancelJoinCodeButton; // Cancel button inside JoinCodePanel
    [SerializeField] private GameObject roomPanel_RoomListContent; // Content parent for Room List Items
    [SerializeField] private GameObject roomPanel_RoomListItemPrefab; // Prefab for each room entry

    // --- UI References for LobbyScene ---
    [Header("Lobby UI (LobbyScene)")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private Button leavePartyButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button privacyToggleButton; // New button for Private/Public
    [SerializeField] private TextMeshProUGUI privacyButtonText; // Text on the privacy button
    [SerializeField] private TextMeshProUGUI joinCodeDisplayText;
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private TextMeshProUGUI lobbyStatusText; // Shows Player Count

    // --- References ---
    [Header("Common References")]
    // ConnectionManager is accessed via Singleton: ConnectionManager.Instance
    [SerializeField] private LobbyManager lobbyManager; // Needed in LobbyScene
    [SerializeField] private RoomListManager roomListManager; // Needed in MainMenuScene for Room Panel

    // --- Status Variables ---
    private bool isLeaving = false;
    private bool isLocalReady = false;
    private bool needsJoinCodeCheck = false;

    private void Awake()
    {
        // Find LobbyManager if in LobbyScene
        if (currentSceneType == SceneType.Lobby && lobbyManager == null)
            lobbyManager = FindObjectOfType<LobbyManager>();

        // Find RoomListManager if in MainMenuScene
        if (currentSceneType == SceneType.MainMenu && roomListManager == null)
            roomListManager = FindObjectOfType<RoomListManager>(); // Assuming it's in the scene

        ValidateSceneType();
    }

    private void Start()
    {
        InitializeUIForScene();
        SubscribeToNetworkEvents();
        SetupButtonListeners();

        if (currentSceneType == SceneType.Lobby)
        {
            if (lobbyManager == null) Debug.LogError("[LobbyUI] ไม่พบ LobbyManager ใน LobbyScene!");
            UpdateLobbyUIState();
        }
        else if (currentSceneType == SceneType.MainMenu)
        {
             if (roomListManager == null) Debug.LogWarning("[LobbyUI] RoomListManager not found in MainMenuScene. Room list will not function.");
        }
    }

    private void Update()
    {
        if (currentSceneType == SceneType.Lobby && needsJoinCodeCheck)
        {
             if (ConnectionManager.Instance != null)
             {
                 CheckAndUpdateJoinCode();
             }
             else if (Time.frameCount % 120 == 0)
             {
                 Debug.LogWarning("[LobbyUI] Update: Waiting for ConnectionManager.Instance...");
             }
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
    }

    private void ValidateSceneType()
    {
        string actualSceneName = SceneManager.GetActiveScene().name;
        if (actualSceneName == "MainMenuScene" && currentSceneType != SceneType.MainMenu)
        {
            Debug.LogWarning("[LobbyUI] Correcting SceneType to MainMenu.");
            currentSceneType = SceneType.MainMenu;
        }
        else if (actualSceneName == "LobbyScene" && currentSceneType != SceneType.Lobby)
        {
            Debug.LogWarning("[LobbyUI] Correcting SceneType to Lobby.");
            currentSceneType = SceneType.Lobby;
        }
    }

    private void InitializeUIForScene()
    {
        bool isMainMenu = (currentSceneType == SceneType.MainMenu);

        if (mainMenuPanel != null) mainMenuPanel.SetActive(isMainMenu);
        if (lobbyPanel != null) lobbyPanel.SetActive(!isMainMenu);

        if (isMainMenu)
        {
            if (roomPanel != null) roomPanel.SetActive(false);
            if (roomPanel_JoinCodePanel != null) roomPanel_JoinCodePanel.SetActive(false);
            if (mainMenuStatusText != null) mainMenuStatusText.text = "Status: Disconnected";
            ResetMainMenuButtons(true);
        }
    }

    private void SetupButtonListeners()
    {
        // --- Main Menu Buttons ---
        if (playButton != null) playButton.onClick.AddListener(OnPlayButtonClicked);
        if (optionsButton != null) optionsButton.onClick.AddListener(OnOptionsButtonClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

        // --- Room Panel Buttons (MainMenuScene) ---
        if (roomPanel_HostButton != null) roomPanel_HostButton.onClick.AddListener(OnHostClicked);
        if (roomPanel_JoinByCodeButton != null) roomPanel_JoinByCodeButton.onClick.AddListener(OnJoinByCodeButtonClicked);
        if (roomPanel_QuickMatchButton != null) roomPanel_QuickMatchButton.onClick.AddListener(OnQuickMatchClicked);
        if (roomPanel_CancelButton != null) roomPanel_CancelButton.onClick.AddListener(OnCancelRoomPanelClicked);

        // --- Join Code Panel Buttons (Inside RoomPanel) ---
        if (roomPanel_ClientPrivateButton != null) roomPanel_ClientPrivateButton.onClick.AddListener(OnClientPrivateJoinClicked);
        if (roomPanel_CancelJoinCodeButton != null) roomPanel_CancelJoinCodeButton.onClick.AddListener(OnCancelJoinCodePanelClicked);
        if (roomPanel_JoinCodeInputField != null)
        {
            roomPanel_JoinCodeInputField.characterLimit = 6;
            roomPanel_JoinCodeInputField.contentType = TMP_InputField.ContentType.Alphanumeric;
            roomPanel_JoinCodeInputField.onValidateInput += ValidateJoinCodeChar;
            roomPanel_JoinCodeInputField.onValueChanged.AddListener(OnJoinCodeValueChanged);
            if (roomPanel_ClientPrivateButton != null) roomPanel_ClientPrivateButton.interactable = false;
        }

        // --- Lobby Buttons (LobbyScene) ---
        if (leavePartyButton != null) leavePartyButton.onClick.AddListener(OnLeavePartyButtonClicked);
        if (readyButton != null) readyButton.onClick.AddListener(OnReadyButtonClicked);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);
        if (copyCodeButton != null) copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        if (privacyToggleButton != null) privacyToggleButton.onClick.AddListener(OnPrivacyToggleClicked);
    }

    private void SubscribeToNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
    }

    private void OnPlayButtonClicked()
    {
        if (roomPanel != null)
        {
            roomPanel.SetActive(true);
            if(playButton) playButton.interactable = false;
            if(optionsButton) optionsButton.interactable = false;
            if(quitButton) quitButton.interactable = false;
            roomListManager?.RefreshRoomList();
        }
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    }

    private void OnOptionsButtonClicked()
    {
        Debug.Log("Options Button Clicked - Implement Options Menu Logic Here");
    }

    private void OnQuitClicked()
    {
        if (currentSceneType == SceneType.MainMenu && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient))
        {
            Debug.Log("Quitting Application...");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }
    }

    // --- แก้ไข OnHostClicked ---
    private async void OnHostClicked()
    {
        if (ConnectionManager.Instance == null || (roomPanel_HostButton != null && !roomPanel_HostButton.interactable)) return;

        SetMainMenuBusyState("Creating Room...");
        ResetRoomPanelButtons(false); // Disable buttons while attempting

        try
        {
            // If StartHostAsync fails, it should throw an exception caught below.
            await ConnectionManager.Instance.StartHostAsync();

            // --- MODIFIED PART ---
            // Assume StartHost was successfully initiated if await completes without exception.
            // Netcode handles internal state. Explicitly load the scene now.
            Debug.Log($"[LobbyUI] StartHostAsync likely succeeded (Join Code: {ConnectionManager.Instance?.JoinCode}). Attempting to load Lobby Scene...");
            LoadLobbyScene();
            // --- END MODIFIED PART ---
        }
        catch (System.Exception e) // Catch ALL exceptions for debugging
        {
            // --- เพิ่ม Log ตรงนี้ ---
            Debug.LogError($"[LobbyUI] EXCEPTION CAUGHT during OnHostClicked! Error: {e.Message}\nStackTrace: {e.StackTrace}");
            // --------------------

            // Handle exceptions during Relay setup or StartHost initiation
            SetMainMenuBusyState($"Failed: {e.Message}", 5f); // Show error on UI
            ResetRoomPanelButtons(true); // Re-enable buttons on failure

            // Ensure panel is still visible on failure
            if (roomPanel != null) roomPanel.SetActive(true);
        }
    }
    // --- จบการแก้ไข OnHostClicked ---


    private void OnJoinByCodeButtonClicked()
    {
        if (roomPanel_JoinCodePanel != null) roomPanel_JoinCodePanel.SetActive(true);
        if (roomPanel_HostButton != null) roomPanel_HostButton.interactable = false;
        if (roomPanel_JoinByCodeButton != null) roomPanel_JoinByCodeButton.interactable = false;
        if (roomPanel_QuickMatchButton != null) roomPanel_QuickMatchButton.interactable = false;
    }

     private void OnQuickMatchClicked()
     {
         if (roomListManager != null)
         {
             SetMainMenuBusyState("Searching for games...");
             ResetRoomPanelButtons(false);
             roomListManager.AttemptQuickMatch();
         }
         else
         {
             SetMainMenuBusyState("Matchmaking service unavailable.", 3f);
         }
     }


    private void OnCancelRoomPanelClicked()
    {
        if (roomPanel != null) roomPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        ResetMainMenuButtons(true);
    }

    private void OnClientPrivateJoinClicked()
    {
        _ = AttemptClientJoinAsync(roomPanel_JoinCodeInputField.text);
    }

    private void OnCancelJoinCodePanelClicked()
    {
        if (roomPanel_JoinCodePanel != null) roomPanel_JoinCodePanel.SetActive(false);
        ResetRoomPanelButtons(true);
    }

    public async Task AttemptClientJoinAsync(string joinCode)
    {
        if (ConnectionManager.Instance == null || string.IsNullOrWhiteSpace(joinCode))
        {
             SetMainMenuBusyState("Invalid Join Code.", 3f); ResetRoomPanelButtons(true);
             if (roomPanel_JoinCodePanel != null) roomPanel_JoinCodePanel.SetActive(false);
             return;
        }
        if (joinCode.Trim().Length != 6) {
            SetMainMenuBusyState("Join Code must be 6 characters.", 3f);
            if (roomPanel_JoinCodeInputField != null) roomPanel_JoinCodeInputField.text = "";
            if (roomPanel_ClientPrivateButton != null) roomPanel_ClientPrivateButton.interactable = false;
             ResetRoomPanelButtons(true);
            return;
        }

        ConnectionManager.Instance.JoinCode = joinCode.Trim().ToUpper();
        SetMainMenuBusyState($"Joining Room {ConnectionManager.Instance.JoinCode}...");
        ResetRoomPanelButtons(false);

        bool connected = false;
        try {
            Task clientTask = ConnectionManager.Instance.StartClientAsync(ConnectionManager.Instance.JoinCode);
            await clientTask;

             if (clientTask.IsFaulted) {
                  Debug.LogError($"[LobbyUI] Join failed (Task Faulted): {clientTask.Exception?.InnerException?.Message ?? clientTask.Exception?.Message}");
                  SetMainMenuBusyState($"Join Failed: {clientTask.Exception?.InnerException?.Message ?? clientTask.Exception?.Message}", 5f);
             } else {
                 float connectTimeout = 5.0f; float checkInterval = 0.1f; float timer = 0f;
                 while (timer < connectTimeout) {
                      if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost) {
                          connected = true;
                          Debug.Log("[LobbyUI] Client Connected Successfully!");
                          LoadLobbyScene();
                          break;
                      }
                      await Task.Delay((int)(checkInterval * 1000)); timer += checkInterval;
                 }
                 if (!connected) {
                     Debug.LogWarning("[LobbyUI] Client connection timed out or failed post-task completion.");
                     SetMainMenuBusyState("Failed to Join (Check Code/Room Status/Timeout)", 4f);
                     if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient) {
                         ConnectionManager.Instance?.Shutdown();
                         await Task.Delay(200);
                     }
                 }
             }
        } catch (System.Exception e) {
            Debug.LogError($"[LobbyUI] Error joining room: {e.Message}\n{e.StackTrace}");
            SetMainMenuBusyState($"Join Failed: {e.Message}", 5f);
              if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient) {
                  ConnectionManager.Instance?.Shutdown();
                  await Task.Delay(200);
              }
        }

        if (!connected)
        {
             ResetRoomPanelButtons(true);
             if (roomPanel_JoinCodePanel != null) roomPanel_JoinCodePanel.SetActive(false);
             if (roomPanel != null) roomPanel.SetActive(true);
        }
    }

    private void SetMainMenuBusyState(string status, float clearAfterSeconds = 0f)
    {
        if (mainMenuStatusText != null) mainMenuStatusText.text = $"Status: {status}";
        if (clearAfterSeconds > 0) StartCoroutine(ClearMainMenuStatusAfterDelay(clearAfterSeconds));
    }

    private IEnumerator ClearMainMenuStatusAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (currentSceneType == SceneType.MainMenu && mainMenuStatusText != null && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient))
        {
            mainMenuStatusText.text = "Status: Disconnected";
             if(roomPanel != null && !roomPanel.activeSelf) ResetMainMenuButtons(true);
             else if (roomPanel != null && roomPanel.activeSelf) ResetRoomPanelButtons(true);
        }
    }

    public void ResetMainMenuButtons(bool enable)
    {
        if (playButton != null) playButton.interactable = enable;
        if (optionsButton != null) optionsButton.interactable = enable;
        if (quitButton != null) quitButton.interactable = enable;
    }

     private void ResetRoomPanelButtons(bool enable)
     {
         if (roomPanel_HostButton != null) roomPanel_HostButton.interactable = enable;
         if (roomPanel_JoinByCodeButton != null) roomPanel_JoinByCodeButton.interactable = enable;
         if (roomPanel_QuickMatchButton != null) roomPanel_QuickMatchButton.interactable = enable;
         if (roomPanel_CancelButton != null) roomPanel_CancelButton.interactable = enable;
         if (roomPanel_JoinCodePanel != null && roomPanel_JoinCodePanel.activeSelf)
         {
             if (roomPanel_ClientPrivateButton != null && enable && roomPanel_JoinCodeInputField != null)
                 roomPanel_ClientPrivateButton.interactable = (roomPanel_JoinCodeInputField.text.Trim().Length == 6);
             else if (roomPanel_ClientPrivateButton != null && !enable)
                 roomPanel_ClientPrivateButton.interactable = false;
             if (roomPanel_CancelJoinCodeButton != null) roomPanel_CancelJoinCodeButton.interactable = enable;
         }
         else if (roomPanel_ClientPrivateButton != null && enable)
         {
             roomPanel_ClientPrivateButton.interactable = false;
         }
     }

    private char ValidateJoinCodeChar(string text, int charIndex, char addedChar)
    {
        if (char.IsLetterOrDigit(addedChar)) return char.ToUpper(addedChar);
        return '\0';
    }

    private void OnJoinCodeValueChanged(string input)
    {
        if (currentSceneType == SceneType.MainMenu && roomPanel_ClientPrivateButton != null)
        {
            roomPanel_ClientPrivateButton.interactable = (input.Trim().Length == 6);
        }
    }

    // --- แก้ไข LoadLobbyScene ---
    private void LoadLobbyScene()
    {
        // Check connection status FIRST
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LobbyUI] Cannot load LobbyScene - NetworkManager.Singleton is NULL!");
            SetMainMenuBusyState("Network Error.", 3f);
            if (roomPanel != null && roomPanel.activeSelf) ResetRoomPanelButtons(true); else ResetMainMenuButtons(true);
            return;
        }
         if (!NetworkManager.Singleton.IsConnectedClient) // Host is also a client
        {
            Debug.LogError("[LobbyUI] Cannot load LobbyScene - NetworkManager not connected!");
            SetMainMenuBusyState("Connection Error.", 3f);
            if (roomPanel != null && roomPanel.activeSelf) ResetRoomPanelButtons(true); else ResetMainMenuButtons(true);
             return;
        }

        // Check if Scene Management is enabled
        if (!NetworkManager.Singleton.NetworkConfig.EnableSceneManagement)
        {
             Debug.LogError("[LobbyUI] Cannot load LobbyScene - Scene Management is disabled in NetworkManager!");
             SetMainMenuBusyState("Scene Management disabled.", 3f);
             if (roomPanel != null && roomPanel.activeSelf) ResetRoomPanelButtons(true); else ResetMainMenuButtons(true);
             return; // Stop execution here if scene management is off
        }

        // --- Added Debug Log ---
        Debug.Log($"[LobbyUI] Attempting to explicitly load LobbyScene via SceneManager...");
        // --------------------

        // Load the scene using Netcode's SceneManager
        var status = NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);

        // --- Check LoadScene Status (Optional but helpful) ---
        if (status != SceneEventProgressStatus.Started)
        {
             Debug.LogWarning($"[LobbyUI] NetworkManager.SceneManager.LoadScene did not return 'Started' status. Status: {status}");
             // It might still load, but this indicates potential issues like scene already loading.
        } else {
             Debug.Log("[LobbyUI] NetworkManager.SceneManager.LoadScene call returned 'Started'.");
        }
        // ------------------------------------------------------
    }
    // --- จบการแก้ไข LoadLobbyScene ---


    private void UpdateLobbyUIState()
    {
        if (currentSceneType != SceneType.Lobby || NetworkManager.Singleton == null) return;
        bool isConnected = NetworkManager.Singleton.IsConnectedClient;
        bool isHost = NetworkManager.Singleton.IsHost;
        if (lobbyPanel != null) lobbyPanel.SetActive(isConnected);
        if (!isConnected) return;
        if (leavePartyButton != null) { leavePartyButton.gameObject.SetActive(true); leavePartyButton.interactable = !isLeaving; }
        if (joinCodeDisplayText != null) { joinCodeDisplayText.gameObject.SetActive(true); CheckAndUpdateJoinCode(); }
        if (copyCodeButton != null)
        {
            copyCodeButton.gameObject.SetActive(true);
            copyCodeButton.interactable = 
                !string.IsNullOrEmpty(ConnectionManager.Instance?.JoinCode);
        }
        if (startGameButton != null) { startGameButton.gameObject.SetActive(isHost); if(isHost) startGameButton.interactable = false; }
        if (readyButton != null) { readyButton.gameObject.SetActive(!isHost); }
        if (privacyToggleButton != null) { privacyToggleButton.gameObject.SetActive(isHost); }
        UpdateLobbyPlayerCountUI();
    }
    private void CheckAndUpdateJoinCode()
    {
         if (joinCodeDisplayText == null) return;
         string currentJoinCode = (ConnectionManager.Instance != null) ? ConnectionManager.Instance.JoinCode : null;
         bool isCodeReady = !string.IsNullOrEmpty(currentJoinCode);
         if (isCodeReady) { joinCodeDisplayText.text = $"Code: {currentJoinCode}"; needsJoinCodeCheck = false; if(copyCodeButton != null && NetworkManager.Singleton.IsHost) copyCodeButton.interactable = true; }
         else { joinCodeDisplayText.text = "Code: Generating..."; if(NetworkManager.Singleton.IsHost) needsJoinCodeCheck = true; if(copyCodeButton != null) copyCodeButton.interactable = false; }
    }
    public void UpdateLobbyPlayerCountUI() { if (currentSceneType == SceneType.Lobby && lobbyStatusText != null && NetworkManager.Singleton != null && ConnectionManager.Instance != null) { int currentCount = NetworkManager.Singleton.ConnectedClientsList.Count; int maxPlayers = ConnectionManager.Instance.MaxPlayers; lobbyStatusText.text = $"Players: {currentCount} / {maxPlayers}"; } }
    public void UpdatePartyReadyState(bool allClientsReady)
    {
        if (currentSceneType != SceneType.Lobby || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || startGameButton == null) return;
        bool enoughPlayers = true; // Or NetworkManager.Singleton.ConnectedClientsList.Count > 1;
        bool canStart = allClientsReady && enoughPlayers;
        startGameButton.interactable = canStart;
        TextMeshProUGUI buttonText = startGameButton.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null) { if (canStart) buttonText.text = "Start Game"; else if (!enoughPlayers) buttonText.text = "Waiting..."; else buttonText.text = "Waiting..."; }
    }
    private void OnLeavePartyButtonClicked() { if (isLeaving || ConnectionManager.Instance == null) return; isLeaving = true; if (leavePartyButton != null) leavePartyButton.interactable = false; Debug.Log("Leaving Room..."); ConnectionManager.Instance.Shutdown(); StartCoroutine(FallbackToMainMenuAfterDelay(1.5f)); }
     private IEnumerator FallbackToMainMenuAfterDelay(float delay)
     {
         yield return new WaitForSeconds(delay);
         if (SceneManager.GetActiveScene().name == "LobbyScene") { Debug.LogWarning("[LobbyUI] Disconnect callback didn't trigger scene change. Forcing MainMenu load."); if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) { NetworkManager.Singleton.Shutdown(); yield return new WaitForSeconds(0.1f); } SceneManager.LoadScene("MainMenuScene"); }
         isLeaving = false;
     }
    private void OnReadyButtonClicked()
    {
        if (currentSceneType != SceneType.Lobby || NetworkManager.Singleton == null || NetworkManager.Singleton.IsHost || lobbyManager == null) return;
        isLocalReady = !isLocalReady;
        if (readyButton != null) { TextMeshProUGUI buttonText = readyButton.GetComponentInChildren<TextMeshProUGUI>(); if (buttonText != null) buttonText.text = isLocalReady ? "Cancel" : "Ready"; }
        lobbyManager.SetReadyStatusServerRpc(isLocalReady);
    }
    private void OnStartGameClicked() { if (currentSceneType != SceneType.Lobby || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost || lobbyManager == null) return; if (startGameButton != null && !startGameButton.interactable) return; Debug.Log("Host clicked Start Game!"); if(startGameButton != null) startGameButton.interactable = false; lobbyManager.StartGame(); }
    private void OnCopyCodeClicked() { if (currentSceneType == SceneType.Lobby && ConnectionManager.Instance != null && !string.IsNullOrEmpty(ConnectionManager.Instance.JoinCode)) { GUIUtility.systemCopyBuffer = ConnectionManager.Instance.JoinCode; Debug.Log($"Copied Join Code: {ConnectionManager.Instance.JoinCode}"); } }
    private void OnPrivacyToggleClicked() { if (currentSceneType != SceneType.Lobby || lobbyManager == null || !NetworkManager.Singleton.IsHost) return; Debug.Log("Host clicked Privacy Toggle Button."); lobbyManager.ToggleRoomPrivacyServerRpc(); }
    public void UpdatePrivacyButton(bool isPublic) { if (currentSceneType == SceneType.Lobby && privacyToggleButton != null) { privacyToggleButton.interactable = NetworkManager.Singleton.IsHost; if (privacyButtonText != null) { privacyButtonText.text = isPublic ? "Public" : "Private"; } } }
    private void HandleClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId) { Debug.Log("Disconnected from server! Returning to Main Menu..."); isLeaving = false; if (NetworkManager.Singleton.IsConnectedClient) { ConnectionManager.Instance?.Shutdown(); } StartCoroutine(ReturnToMainMenuAfterDisconnect()); }
        else if (currentSceneType == SceneType.Lobby) { Debug.Log($"Another player (Client {clientId}) disconnected."); UpdateLobbyUIState(); }
    }
     private IEnumerator ReturnToMainMenuAfterDisconnect()
     {
         yield return new WaitForSeconds(0.2f);
         if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient && !isLeaving) { Debug.LogWarning("[LobbyUI] Still connected after disconnect event? Forcing shutdown."); ConnectionManager.Instance?.Shutdown(); yield return new WaitForSeconds(0.5f); }
         if (SceneManager.GetActiveScene().name != "MainMenuScene") { Debug.Log("[LobbyUI] Loading MainMenuScene after disconnect."); SceneManager.LoadScene("MainMenuScene"); }
         else { Debug.Log("[LobbyUI] Already in MainMenuScene after disconnect."); InitializeUIForScene(); ResetMainMenuButtons(true); }
     }
}