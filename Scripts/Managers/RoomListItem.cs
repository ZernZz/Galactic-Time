// File: Scripts/Managers/RoomListItem.cs
// In Scripts/Managers/RoomListItem.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomListItem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI hostNameText; // Can display Lobby Name or Host Name
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Button joinButton;

    private RoomInfo currentRoomInfo;
    private RoomListManager roomListManager; // Reference to the manager

    // Call this to populate the item's UI
    public void Setup(RoomInfo roomInfo, RoomListManager manager)
    {
        currentRoomInfo = roomInfo;
        roomListManager = manager; // Store the manager reference

        if (hostNameText != null)
        {
            // Use the hostName from RoomInfo (which might be Lobby Name or custom data)
            hostNameText.text = string.IsNullOrWhiteSpace(roomInfo.hostName) ? "Unnamed Room" : roomInfo.hostName;
        }
        else { Debug.LogWarning("[RoomListItem] Host Name Text not assigned.", gameObject); }

        if (playerCountText != null)
        {
            playerCountText.text = $"{roomInfo.currentPlayers}/{roomInfo.maxPlayers}";
        }
        else { Debug.LogWarning("[RoomListItem] Player Count Text not assigned.", gameObject); }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners(); // Clear previous listeners
            joinButton.onClick.AddListener(OnJoinButtonClicked);            // --- IMPORTANT: Disable Join button if Join Code is not available or "N/A" OR room is full ---
            // This relies on the Join Code being retrieved correctly (check RoomListManager notes on visibility)
            bool hasValidJoinCode = !(string.IsNullOrEmpty(roomInfo.joinCode) || roomInfo.joinCode == "N/A");
            bool isRoomFull = roomInfo.currentPlayers >= roomInfo.maxPlayers;
            bool canJoin = hasValidJoinCode && !isRoomFull;
            joinButton.interactable = canJoin;

            // Optionally change button text based on joinability
            TextMeshProUGUI buttonText = joinButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null) {
                if (!hasValidJoinCode) {
                    buttonText.text = "Info"; // No join code available
                } else if (isRoomFull) {
                    buttonText.text = "Full"; // Room is full
                } else {
                    buttonText.text = "Join"; // Can join
                }
            }
            // ------------------------------------------------------------------------------
        }
        else
        {
            Debug.LogError("[RoomListItem] Join Button reference not set!", gameObject);
        }

        // --- Add Vertical Layout Logic (If NOT using VerticalLayoutGroup) ---
        // This part is usually handled by Unity's UI Layout system.
        // Manually setting position like this is less flexible.
        // If you *must* do it via script:
        /*
        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            int itemIndex = transform.GetSiblingIndex(); // Get order in the parent
            float yOffset = itemIndex * -220f; // Calculate Y position based on index and spacing
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, yOffset);
            Debug.Log($"[RoomListItem] Set {gameObject.name} position to Y: {yOffset}");
        }
        else { Debug.LogError("[RoomListItem] Missing RectTransform!", gameObject); }
        */
        // --- End Manual Layout Logic ---
    }

    private void OnJoinButtonClicked()
    {
        // Double-check interactable status and references
        if (roomListManager != null && currentRoomInfo != null && joinButton != null && joinButton.interactable)
        {
            Debug.Log($"[RoomListItem] Join button clicked for room. Attempting join via Relay Code: {currentRoomInfo.joinCode} (Lobby ID: {currentRoomInfo.lobbyId})");
            joinButton.interactable = false; // Prevent double-clicking while joining
            // Tell the RoomListManager to handle the join request
            roomListManager.JoinRoomFromList(currentRoomInfo.joinCode);
        }
        else if (joinButton != null && !joinButton.interactable)
        {
             Debug.LogWarning($"[RoomListItem] Join button clicked, but joining is not possible (likely missing or invalid Join Code). Lobby ID: {currentRoomInfo?.lobbyId}");
             // Optionally show a message to the user or re-enable the button after a delay
        }
        else
        {
             Debug.LogError("[RoomListItem] Cannot join room - RoomListManager or RoomInfo is missing!");
             // Ensure button is interactable if the attempt fails immediately due to missing refs
             if(joinButton != null) joinButton.interactable = true;
        }
    }
}