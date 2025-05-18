using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using System.Collections.Generic;

public class PlayerSkinManager : NetworkBehaviour
{
    public NetworkVariable<int> currentSkinIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );

    [Header("Skin Prefabs")]
    [SerializeField] private GameObject[] skinPrefabs;

    [Header("UI References")]
    [SerializeField] private GameObject changeSkinCanvas;
    [SerializeField] private Button changeSkinsButton;
    [SerializeField] private GameObject changeSkinPanel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private List<Button> skinButtons; // ✅ ปุ่มสกินแต่ละอัน

    private int selectedSkinIndex = 0;
    private GameObject currentModelInstance;

    private void OnEnable() => SceneManager.activeSceneChanged += OnActiveSceneChanged;
    private void OnDisable() => SceneManager.activeSceneChanged -= OnActiveSceneChanged;

    private void Start()
    {
        if (IsOwner)
        {
            if (changeSkinsButton != null)
                changeSkinsButton.onClick.AddListener(OnChangeSkinsButtonClicked);
            if (confirmButton != null)
                confirmButton.onClick.AddListener(OnConfirmButtonClicked);
            if (cancelButton != null)
                cancelButton.onClick.AddListener(OnCancelButtonClicked);

            // ✅ ตั้งค่าให้ปุ่มเปลี่ยนสกินทำงาน
            for (int i = 0; i < skinButtons.Count; i++)
            {
                int index = i;
                skinButtons[i].onClick.AddListener(() => SetSelectedSkin(index));
            }
        }
        else
        {
            if (changeSkinsButton != null) changeSkinsButton.gameObject.SetActive(false);
            if (changeSkinCanvas != null) changeSkinCanvas.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        currentSkinIndex.OnValueChanged += OnSkinIndexChanged;
        InstantiateSkin(currentSkinIndex.Value);
        UpdateButtonStates(currentSkinIndex.Value); // ✅ อัปเดตปุ่มเมื่อเริ่มเกม
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        currentSkinIndex.OnValueChanged -= OnSkinIndexChanged;
    }

    private void OnSkinIndexChanged(int oldValue, int newValue)
    {
        InstantiateSkin(newValue);
        UpdateButtonStates(newValue); // ✅ อัปเดต UI ปุ่มสกิน
    }

    private void InstantiateSkin(int skinIndex)
    {
        if (currentModelInstance != null)
        {
            Destroy(currentModelInstance);
            currentModelInstance = null;
        }

        if (skinPrefabs == null || skinPrefabs.Length == 0 || skinIndex < 0 || skinIndex >= skinPrefabs.Length)
            return;

        GameObject prefabToUse = skinPrefabs[skinIndex];
        if (prefabToUse != null)
        {
            currentModelInstance = Instantiate(prefabToUse, transform);
            currentModelInstance.transform.localPosition = Vector3.zero;
            currentModelInstance.transform.localRotation = Quaternion.identity;

            var playerController = GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.RefreshAnimator();
            }
        }
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (!IsOwner) return;
        bool isLobby = (newScene.name == "LobbyScene");
        if (changeSkinsButton != null)
            changeSkinsButton.gameObject.SetActive(isLobby);
        if (!isLobby && changeSkinPanel != null)
            changeSkinPanel.SetActive(false);
    }

    private void OnChangeSkinsButtonClicked()
    {
        if (changeSkinPanel != null)
        {
            changeSkinPanel.SetActive(true);
            selectedSkinIndex = currentSkinIndex.Value;
            UpdateButtonStates(selectedSkinIndex); // ✅ อัปเดตปุ่มที่เลือก
        }
    }

    private void OnConfirmButtonClicked()
    {
        RequestChangeSkinServerRpc(selectedSkinIndex);
        if (changeSkinPanel != null)
            changeSkinPanel.SetActive(false);
    }

    private void OnCancelButtonClicked()
    {
        if (changeSkinPanel != null)
            changeSkinPanel.SetActive(false);
    }

    public void SetSelectedSkin(int skinIndex)
    {
        selectedSkinIndex = skinIndex;
        UpdateButtonStates(skinIndex); // ✅ ทำให้ปุ่มที่เลือกยุบลง
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestChangeSkinServerRpc(int newSkinIndex, ServerRpcParams rpcParams = default)
    {
        currentSkinIndex.Value = newSkinIndex;
    }

    // ✅ อัปเดตสถานะของปุ่มให้ดูเหมือนปุ่มที่ถูกเลือก
    private void UpdateButtonStates(int selectedIndex)
    {
        for (int i = 0; i < skinButtons.Count; i++)
        {
            if (i == selectedIndex)
            {
                skinButtons[i].transform.localScale = new Vector3(0.17f, 0.17f, 1f); // ✅ ย่อขนาดลง 10%
                skinButtons[i].GetComponent<Image>().color = new Color(0.7f, 0.7f, 0.7f); // ✅ เปลี่ยนสีให้ดูถูกเลือก
            }
            else
            {
                skinButtons[i].transform.localScale = new Vector3(0.1816216f, 0.1816216f, 1f); // ✅ คืนขนาดปกติ
                skinButtons[i].GetComponent<Image>().color = Color.white; // ✅ คืนสีปกติ
            }
        }
    }
}
