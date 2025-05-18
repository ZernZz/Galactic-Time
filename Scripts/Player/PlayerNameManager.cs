using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.Netcode;
using Unity.Collections;

public class PlayerNameManager : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(
        "Unnamed",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Button changeNameButton;
    [SerializeField] private GameObject changeNamePanel;
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private Button confirmNameButton;
    [SerializeField] private Button cancelButton; // Cancel button to close the Change Name Canvas

    [Header("Billboard Settings")]
    [SerializeField] private Transform billboardTransform;

    [Header("Canvas (Optional)")]
    [SerializeField] private Canvas worldSpaceCanvas;

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void Start()
    {
        if (worldSpaceCanvas != null && Camera.main != null)
        {
            worldSpaceCanvas.worldCamera = Camera.main;
        }

        if (!IsOwner)
        {
            if (changeNameButton != null)
                changeNameButton.gameObject.SetActive(false);

            if (changeNamePanel != null)
                changeNamePanel.SetActive(false);
            return;
        }

        bool isLobby = (SceneManager.GetActiveScene().name == "LobbyScene");

        if (changeNameButton != null)
            changeNameButton.gameObject.SetActive(isLobby);

        if (changeNamePanel != null)
            changeNamePanel.SetActive(false);

        if (changeNameButton != null)
            changeNameButton.onClick.AddListener(OnChangeNameButtonClicked);

        if (confirmNameButton != null)
            confirmNameButton.onClick.AddListener(OnConfirmNameButtonClicked);

        // Connect event for the cancel button
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelButtonClicked);

        if (nameInputField != null)
        {
            nameInputField.characterLimit = 12;
            // Set up input validation to allow only English letters, digits, and '_' characters.
            nameInputField.onValidateInput = ValidateNameInput;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        playerName.OnValueChanged += OnPlayerNameChanged;

        if (nameText != null)
        {
            nameText.text = playerName.Value.ToString();
        }

        Debug.Log($"[PlayerNameManager] OnNetworkSpawn (Owner={OwnerClientId}, Local={NetworkManager.LocalClientId}) Name={playerName.Value}");
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        playerName.OnValueChanged -= OnPlayerNameChanged;
    }

    private void OnPlayerNameChanged(FixedString32Bytes oldName, FixedString32Bytes newName)
    {
        if (nameText != null)
        {
            nameText.text = newName.ToString();
        }
        Debug.Log($"[PlayerNameManager] OnPlayerNameChanged from {oldName} to {newName}");
    }

    private void LateUpdate()
    {
        if (billboardTransform != null && Camera.main != null)
        {
            billboardTransform.LookAt(
                billboardTransform.position + Camera.main.transform.rotation * Vector3.forward,
                Camera.main.transform.rotation * Vector3.up
            );
        }
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (!IsOwner) return;

        bool isLobby = newScene.name == "LobbyScene";
        if (changeNameButton != null)
        {
            changeNameButton.gameObject.SetActive(isLobby);
        }
        if (!isLobby && changeNamePanel != null)
        {
            changeNamePanel.SetActive(false);
        }
        if (worldSpaceCanvas != null && Camera.main != null)
        {
            worldSpaceCanvas.worldCamera = Camera.main;
        }
    }

    private void OnChangeNameButtonClicked()
    {
        if (changeNamePanel != null)
        {
            changeNamePanel.SetActive(true);
            if (nameInputField != null)
            {
                nameInputField.text = playerName.Value.ToString();
            }
        }
    }

    private void OnConfirmNameButtonClicked()
    {
        if (nameInputField != null)
        {
            string newNameStr = nameInputField.text.Trim();

            // Update regex to allow only English letters, digits, and '_' without spaces (1-12 characters)
            if (Regex.IsMatch(newNameStr, "^[A-Za-z0-9_]{1,12}$"))
            {
                UpdatePlayerNameOnServerRpc(new FixedString32Bytes(newNameStr));
            }
            else
            {
                Debug.LogWarning("Invalid name! Name must be 1-12 characters long, only English letters, digits, and '_' are allowed without spaces.");
            }

            if (changeNamePanel != null)
                changeNamePanel.SetActive(false);
        }
    }

    // Function to close the Change Name Canvas when the cancel button is clicked
    private void OnCancelButtonClicked()
    {
        if (changeNamePanel != null)
            changeNamePanel.SetActive(false);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestChangeNameServerRpc(FixedString32Bytes newName, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"[PlayerNameManager][ServerRpc] Client={rpcParams.Receive.SenderClientId} requests name={newName}");

        if (newName.Length < 1 || newName.Length > 12)
        {
            Debug.LogWarning("Invalid name length.");
            return;
        }
        // Update regex to allow only English letters, digits, and '_' characters
        if (!Regex.IsMatch(newName.ToString(), "^[A-Za-z0-9_]+$"))
        {
            Debug.LogWarning("Name contains invalid characters.");
            return;
        }
        foreach (var clientPair in NetworkManager.Singleton.ConnectedClients)
        {
            var client = clientPair.Value;
            if (client?.PlayerObject != null)
            {
                var otherPlayer = client.PlayerObject.GetComponent<PlayerNameManager>();
                if (otherPlayer != null && otherPlayer.playerName.Value.ToString().Equals(newName.ToString(), System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"Name {newName} is already in use.");
                    return;
                }
            }
        }

        playerName.Value = newName;
        Debug.Log($"Name changed successfully to {newName}");
    }

    // Validate input character for the name input field.
    // Only English letters, digits, and '_' are allowed.
    private char ValidateNameInput(string text, int charIndex, char addedChar)
    {
        if (Regex.IsMatch(addedChar.ToString(), "[A-Za-z0-9_]"))
        {
            return addedChar;
        }
        // If the character doesn't match the allowed set, return '\0' so it won't be input.
        return '\0';
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdatePlayerNameOnServerRpc(FixedString32Bytes newName, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        Debug.Log($"[PlayerNameManager] Updating name for Client {clientId} to {newName}");

        if (newName.Length < 1 || newName.Length > 12)
        {
            Debug.LogWarning("Invalid name length.");
            return;
        }

        playerName.Value = newName;

        // 📢 แจ้ง `ScorePlayerScript` ว่ามีการเปลี่ยนชื่อ
        var scoreScript = FindObjectOfType<ScorePlayerScript>();
        if (scoreScript != null)
        {
            scoreScript.UpdatePlayerName(clientId, newName.ToString());
        }
    }

    
}
