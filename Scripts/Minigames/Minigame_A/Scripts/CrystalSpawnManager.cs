using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class CrystalSpawnManager : NetworkBehaviour
{
    [Header("Setup")]
    public List<GameObject> crystalPrefabs;
    public List<Transform> spawnPoints;

    public int maxCrystals = 600;
    public float spawnInterval = 0.3f;
    public float despawnCooldown = 2f; 

    private int currentCrystalCount = 0;
    private bool gameEnded = false;

    private Dictionary<GameObject, float> crystalSpawnRates = new Dictionary<GameObject, float>();
    private Dictionary<Transform, float> spawnCooldowns = new Dictionary<Transform, float>(); 

    private HashSet<Transform> activeSpawnPoints = new HashSet<Transform>(); // ✅ เก็บจุดที่มี Crystal อยู่

    private void Awake()
    {
        if (crystalPrefabs.Count >= 3)
        {
            crystalSpawnRates[crystalPrefabs[0]] = 0.50f; // Crystal สีฟ้า 50%
            crystalSpawnRates[crystalPrefabs[1]] = 0.35f; // Crystal สีม่วง 35%
            crystalSpawnRates[crystalPrefabs[2]] = 0.15f; // Crystal สีเหลือง 15%
        }
        else
        {
            Debug.LogError("❌ Crystal Prefabs ไม่ครบ 3 ชิ้น! กรุณาใส่ให้ครบ");
        }

        foreach (var point in spawnPoints)
        {
            spawnCooldowns[point] = 0f;
        }
    }

    public void StartSpawningCrystals()
    {
        if (IsServer)
        {
            StartCoroutine(SpawnCrystalsOverTime());
        }
    }

    public void StopSpawning()
    {
        gameEnded = true;
        StopAllCoroutines();
    }

    public void DespawnAllCrystals()
    {
        foreach (var crystal in FindObjectsOfType<Crystal>())
        {
            if (crystal.TryGetComponent<NetworkObject>(out NetworkObject netObj))
            {
                if (netObj.IsSpawned) netObj.Despawn(true);
            }
        }
        currentCrystalCount = 0;
        activeSpawnPoints.Clear();
    }

    private IEnumerator SpawnCrystalsOverTime()
    {
        while (NetworkManager.Singleton.IsListening)
        {
            if (gameEnded) yield break;

            if (currentCrystalCount < maxCrystals)
            {
                SpawnCrystal();
            }
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private void SpawnCrystal()
    {
        if (!NetworkManager.Singleton.IsListening) return;
        if (spawnPoints.Count == 0 || crystalSpawnRates.Count == 0) return;

        Transform point = GetAvailableSpawnPoint();
        if (point == null) return; // ❌ ไม่มีจุดที่ Spawn ได้

        GameObject prefab = GetRandomCrystalByRate();
        if (prefab == null) return;

        GameObject newCrystal = Instantiate(prefab, point.position, Quaternion.identity);
        var netObj = newCrystal.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(true);
            currentCrystalCount++;

            activeSpawnPoints.Add(point); // ✅ บันทึกว่าจุดนี้มีคริสตัลแล้ว

            StartCoroutine(DespawnAfterSeconds(netObj, 5f, point));
        }
    }

    private Transform GetAvailableSpawnPoint()
    {
        List<Transform> availablePoints = spawnPoints
            .Where(point => !activeSpawnPoints.Contains(point) && Time.time >= spawnCooldowns[point])
            .ToList();

        if (availablePoints.Count == 0) return null; // ❌ ไม่มีจุดที่ว่าง
        return availablePoints[Random.Range(0, availablePoints.Count)];
    }

    private GameObject GetRandomCrystalByRate()
    {
        float randomValue = Random.Range(0f, 1f);
        float cumulative = 0f;

        foreach (var pair in crystalSpawnRates)
        {
            cumulative += pair.Value;
            if (randomValue <= cumulative)
            {
                return pair.Key;
            }
        }
        return null;
    }

    private IEnumerator DespawnAfterSeconds(NetworkObject crystalObj, float seconds, Transform spawnPoint)
    {
        yield return new WaitForSeconds(seconds);

        if (crystalObj != null && crystalObj.IsSpawned)
        {
            crystalObj.Despawn(true);
            currentCrystalCount--;

            activeSpawnPoints.Remove(spawnPoint); // ✅ ลบจุดนี้ออกจากจุดที่มีคริสตัล
            spawnCooldowns[spawnPoint] = Time.time + despawnCooldown; // ✅ ตั้ง cooldown
        }
    }
}
