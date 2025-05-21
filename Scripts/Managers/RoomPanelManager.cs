using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Threading.Tasks; // Required for Task

// This script primarily manages the UI interactions within the RoomPanel in the MainMenuScene.
// It delegates network actions to ConnectionManager and room list logic to RoomListManager.
public class RoomPanelManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinByCodeButton;
    [SerializeField] private Button quickMatchButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private GameObject joinCodePanel;
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private Button clientPrivateButton; // Join button inside code panel
    [SerializeField] private Button cancelJoinCodeButton;
    [SerializeField] private TextMeshProUGUI statusText; // Optional status text within the panel

    [Header("Manager References")]
    [SerializeField] private LobbyUI lobbyUI; // To call join logic and status updates
    [SerializeField] private RoomListManager roomListManager; // To initiate quick match

    private void Start()
    {
        // Assign listeners - LobbyUI might already do this, ensure no duplicates if LobbyUI manages these buttons directly
        if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
        if (joinByCodeButton != null) joinByCodeButton.onClick.AddListener(OnJoinByCodeClicked);
        if (quickMatchButton != null) quickMatchButton.onClick.AddListener(OnQuickMatchClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelRoomPanelClicked);
        if (clientPrivateButton != null) clientPrivateButton.onClick.AddListener(OnClientPrivateJoinClicked);
        if (cancelJoinCodeButton != null) cancelJoinCodeButton.onClick.AddListener(OnCancelJoinCodePanelClicked);

        if (joinCodeInputField != null)
        {
            joinCodeInputField.characterLimit = 6;
            joinCodeInputField.contentType = TMP_InputField.ContentType.Alphanumeric;
            joinCodeInputField.onValidateInput += ValidateJoinCodeChar;
            joinCodeInputField.onValueChanged.AddListener(OnJoinCodeValueChanged);
            if (clientPrivateButton != null) clientPrivateButton.interactable = false; // Start disabled
        }

        if (lobbyUI == null) lobbyUI = FindObjectOfType<LobbyUI>(); // Fallback
        if (roomListManager == null) roomListManager = FindObjectOfType<RoomListManager>(); // Fallback
    }    private void OnEnable()
    {
        // When the panel is enabled, reset its state
        if (joinCodePanel != null) joinCodePanel.SetActive(false);
        ResetButtons(true);
        if(statusText) statusText.text = ""; // Clear status
        
        // เริ่มต้น QuickMatch ปุ่มเป็นปิด จนกว่าจะพบห้องที่เข้าร่วมได้
        if (quickMatchButton != null)
        {
            quickMatchButton.interactable = false;
        }
        
        roomListManager?.RefreshRoomList(); // Refresh list when panel opens
    }

    // --- Button Handlers ---

    private async void OnHostClicked()
    {
        if (ConnectionManager.Instance == null || (hostButton != null && !hostButton.interactable)) return;
        SetBusyState("Creating Room...");
        try
        {
            await ConnectionManager.Instance.StartHostAsync();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                // Scene transition is automatic
            }
            else
            {
                 SetBusyState("Failed to create room.", 3f);
                 ResetButtons(true);
            }
        }
        catch (System.Exception e)
        {
            SetBusyState($"Failed: {e.Message}", 5f);
            ResetButtons(true);
        }
        // No scene load needed here
    }

    private void OnJoinByCodeClicked()
    {
        if (joinCodePanel != null) joinCodePanel.SetActive(true);
        // Disable main room panel buttons while code panel is open
        if(hostButton) hostButton.interactable = false;
        if(joinByCodeButton) joinByCodeButton.interactable = false;
        if(quickMatchButton) quickMatchButton.interactable = false;
    }

    private void OnQuickMatchClicked()
    {
         if (roomListManager != null)
         {
             SetBusyState("Searching for games...");
             roomListManager.AttemptQuickMatch(); // RoomListManager handles the async operation
         }
         else
         {
             SetBusyState("Matchmaking service unavailable.", 3f);
         }
    }

    private void OnCancelRoomPanelClicked()
    {
        gameObject.SetActive(false); // Deactivate this panel
        lobbyUI?.ResetMainMenuButtons(true); // Notify LobbyUI to re-enable main buttons
        // Assuming LobbyUI handles activating the MainMenuPanel itself
    }

    // --- Join Code Panel Handlers ---

    private void OnClientPrivateJoinClicked()
    {
         if (lobbyUI != null && joinCodeInputField != null)
         {
             SetBusyState("Joining Room...");
             // Delegate the join attempt to LobbyUI's method
             _ = lobbyUI.AttemptClientJoinAsync(joinCodeInputField.text);
         }
    }

    private void OnCancelJoinCodePanelClicked()
    {
        if (joinCodePanel != null) joinCodePanel.SetActive(false);
        // Re-enable main room panel buttons
        ResetButtons(true);
    }

    // --- UI Helpers ---    // เก็บข้อความสถานะเพื่อใช้ตรวจสอบว่าเป็นสถานะ Quick Match หรือไม่
    private string currentStatusMessage = "";

    public void SetBusyState(string message, float clearAfter = 0f) // Public for RoomListManager callbacks
    {
        if (statusText != null) statusText.text = message;
        currentStatusMessage = message; // บันทึกข้อความสถานะปัจจุบัน
        ResetButtons(false); // Disable buttons
        if (clearAfter > 0) Invoke(nameof(ClearBusyState), clearAfter);
    }

    private void ClearBusyState()
    {
        if (statusText != null) statusText.text = "";
        
        // เราจะเปิดใช้งานปุ่มทั้งหมดยกเว้น QuickMatch ที่ต้องการการตรวจสอบพิเศษ
        ResetButtons(true);
        
        // รีเซ็ตข้อความสถานะ
        currentStatusMessage = "";
    }

    public void ResetButtons(bool enable) // Public for RoomListManager callbacks
    {
        if (hostButton != null) hostButton.interactable = enable;
        if (joinByCodeButton != null) joinByCodeButton.interactable = enable;
        if (quickMatchButton != null) quickMatchButton.interactable = enable;
        if (cancelButton != null) cancelButton.interactable = enable;

        // Handle join code panel if it's active
        if (joinCodePanel != null && joinCodePanel.activeSelf)
        {
            if (clientPrivateButton != null && enable && joinCodeInputField != null)
                clientPrivateButton.interactable = (joinCodeInputField.text.Trim().Length == 6);
            else if (clientPrivateButton != null && !enable)
                clientPrivateButton.interactable = false;

            if (cancelJoinCodeButton != null) cancelJoinCodeButton.interactable = enable;
        }
        // If join code panel is NOT active, but we are resetting, ensure its join button is disabled
        else if (clientPrivateButton != null && enable)
        {
             clientPrivateButton.interactable = false; // Disable if panel not active
        }
    }


    private char ValidateJoinCodeChar(string text, int charIndex, char addedChar)
    {
        if (char.IsLetterOrDigit(addedChar)) return char.ToUpper(addedChar);
        return '\0';
    }

    private void OnJoinCodeValueChanged(string input)
    {
        if (clientPrivateButton != null)
        {
            clientPrivateButton.interactable = (input.Trim().Length == 6);
        }
    }
    
    // เมธอดใหม่เพื่อควบคุมสถานะปุ่ม QuickMatch
    public void UpdateQuickMatchButtonState(bool hasJoinableRooms)
    {
        if (quickMatchButton != null)
        {
            quickMatchButton.interactable = hasJoinableRooms;
        }
    }
}