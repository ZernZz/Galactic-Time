using Unity.Netcode;
using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class MeteorSpawner : NetworkBehaviour
{
    [System.Serializable]
    public struct MeteorStage
    {
        public int meteorCount;
        public float spawnRate;
        public float meteorSpeed;
    }

    [Header("Stage Settings")]
    public MeteorStage[] stages;
    public float spawnHeight = 30f;

    [Header("References")]
    public GameObject meteorPrefab;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI stageCornerText;
    [SerializeField] private TextMeshProUGUI stageCenterText;

    public GameObject Victorypanel;

    public static bool CanShoot = false;

    private int spawnedThisStage = 0;
    private float spawnTimer;
    private bool canSpawnMeteor = false;
    private bool stageInTransition = false;

    [Header("Spawn Area")]
    public Transform spawnCenter;
    public float spawnRadius = 5f;

    private List<GameObject> activeMeteors = new();

    // ✅ เปลี่ยนเป็น NetworkVariable เพื่อ sync Stage กับทุกคน
    public NetworkVariable<int> currentStageIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Start()
    {
        if (!IsServer) return;
        StartCoroutine(ShowStageUI(currentStageIndex.Value));
    }

    private void OnEnable()
    {
        currentStageIndex.OnValueChanged += OnStageChanged;
    }

    private void OnDisable()
    {
        currentStageIndex.OnValueChanged -= OnStageChanged;
    }

    private void OnStageChanged(int oldStage, int newStage)
    {
        if (stageCornerText != null)
        {
            if (newStage < stages.Length)
                stageCornerText.text = $"Stage: {newStage + 1} / {stages.Length}";
            else
                Victorypanel.SetActive(true);
                stageCornerText.text = $"Stage: COMPLETE!";

            
        }
    }

    private void Update()
    {
        if (!IsServer || !canSpawnMeteor || currentStageIndex.Value >= stages.Length) return;

        spawnTimer -= Time.deltaTime;

        if (spawnTimer <= 0f)
        {
            SpawnMeteor();
            spawnedThisStage++;

            if (spawnedThisStage >= stages[currentStageIndex.Value].meteorCount)
            {
                canSpawnMeteor = false;
            }
            else
            {
                spawnTimer = stages[currentStageIndex.Value].spawnRate;
            }
        }
    }

    private void SpawnMeteor()
    {
        if (spawnCenter == null)
        {
            Debug.LogWarning("❗ spawnCenter ยังไม่ถูกตั้งค่าใน Inspector");
            return;
        }

        // สุ่มตำแหน่งรอบๆ spawnCenter (ในวงกลม)
        Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
        Vector3 target = spawnCenter.position + new Vector3(randomOffset.x, 0f, randomOffset.y);
        Vector3 spawnPos = target + Vector3.up * spawnHeight;

        GameObject meteor = Instantiate(meteorPrefab, spawnPos, Quaternion.identity);

        Meteor meteorScript = meteor.GetComponent<Meteor>();
        if (meteorScript != null)
        {
            meteorScript.moveSpeed = stages[currentStageIndex.Value].meteorSpeed;
            meteorScript.SetTarget(target);
            meteorScript.spawner = this;
        }

        NetworkObject netObj = meteor.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(true);
        }
        else
        {
            Debug.LogError("Meteor prefab missing NetworkObject component!");
        }

        activeMeteors.Add(meteor);
    }



    public void NotifyMeteorDestroyed(GameObject meteor)
    {
        if (activeMeteors.Contains(meteor))
        {
            activeMeteors.Remove(meteor);
        }

        if (!canSpawnMeteor && activeMeteors.Count == 0 && !stageInTransition && IsServer)
        {
            StartCoroutine(NextStageSequence());
        }
    }

    private IEnumerator NextStageSequence()
    {
        stageInTransition = true;

        currentStageIndex.Value++;
        spawnedThisStage = 0;

        if (currentStageIndex.Value < stages.Length)
        {
            yield return ShowStageUI(currentStageIndex.Value);
        }
        else
        {
            if (stageCornerText != null)
                stageCornerText.text = "Stage: COMPLETE!";

            CanShoot = false;

            // ❌ อย่าหยุดเกม ไม่ต้องใช้ Time.timeScale = 0
            // Time.timeScale = 0f;
        }

        stageInTransition = false;
    }

    private IEnumerator ShowStageUI(int stageIndex)
    {
        CanShoot = false;

        if (stageCornerText != null && IsServer)
        {
            stageCornerText.gameObject.SetActive(false);
        }

        // ✅ เรียก ClientRpc เพื่อให้ทุกคนแสดง UI กลาง
        ShowStageUICenterClientRpc(stageIndex);

        yield return new WaitForSeconds(3f); // ตรงกับที่ Client แสดง UI กลาง

        if (stageCornerText != null && IsServer)
        {
            stageCornerText.text = $"Stage: {stageIndex + 1} / {stages.Length}";
            stageCornerText.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(1f);

        spawnTimer = stages[stageIndex].spawnRate;
        canSpawnMeteor = true;
        CanShoot = true;
    }


    [ClientRpc]
    private void ShowStageUICenterClientRpc(int stageIndex)
    {
        if (stageCenterText != null)
        {
            stageCenterText.text = $"Stage {stageIndex + 1}";
            stageCenterText.gameObject.SetActive(true);
        }

        StartCoroutine(HideCenterUIAfterDelay());
    }

    private IEnumerator HideCenterUIAfterDelay()
    {
        yield return new WaitForSeconds(3f);

        if (stageCenterText != null)
            stageCenterText.gameObject.SetActive(false);
    }
}
