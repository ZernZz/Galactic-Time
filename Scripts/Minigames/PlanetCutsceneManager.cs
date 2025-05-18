using UnityEngine;
using Unity.Netcode;
using System.Collections; // สำหรับ Coroutine
using UnityEngine.SceneManagement; // สำหรับ LoadSceneMode

// ต้องมี NetworkObject บน GameObject ที่ใส่ Script นี้ด้วย
public class PlanetCutsceneManager : NetworkBehaviour
{
    [Header("Cutscene Settings")]
    [SerializeField] private float cutsceneDuration = 5.0f; // ระยะเวลา Cutscene (วินาที) ปรับใน Inspector ได้

    [Header("Optional: Fading Effect")]
    [SerializeField] private CanvasGroup fadeCanvasGroup; // (Optional) Canvas Group สำหรับ Fade out
    [SerializeField] private float fadeOutTime = 0.5f; // (Optional) เวลาที่ใช้ Fade out

    private bool isLoadingNextScene = false; // ป้องกันการโหลดซ้ำซ้อน

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // ให้ Server เท่านั้นที่เริ่มนับเวลาและโหลด Scene ถัดไป
        if (IsServer)
        {
            Debug.Log($"[PlanetCutsceneManager] Server starting cutscene timer ({cutsceneDuration}s) in scene {gameObject.scene.name}.");
            StartCoroutine(StartMinigameLoadTimer());
        }

        // เริ่ม Fade In ถ้ามีการตั้งค่า
        if (fadeCanvasGroup != null)
        {
            StartCoroutine(FadeEffect(1f, 0f, fadeOutTime)); // Fade In
        }
    }

    private IEnumerator StartMinigameLoadTimer()
    {
        yield return new WaitForSeconds(cutsceneDuration);

        // ดึงชื่อ Minigame Scene ที่ต้องโหลดต่อจาก ConnectionManager
        string nextScene = string.Empty;
        if (ConnectionManager.Instance != null)
        {
            nextScene = ConnectionManager.Instance.NextMinigameSceneToLoad;
        }
        else
        {
            Debug.LogError("[PlanetCutsceneManager] ConnectionManager.Instance is null! Cannot determine next scene.");
            yield break; // หยุด Coroutine ถ้าหา ConnectionManager ไม่เจอ
        }

        if (string.IsNullOrEmpty(nextScene))
        {
            Debug.LogError("[PlanetCutsceneManager] NextMinigameSceneToLoad is empty! Cannot load next scene.");
            yield break; // หยุด Coroutine ถ้าชื่อ Scene ว่าง
        }

        if (!isLoadingNextScene)
        {
            isLoadingNextScene = true;
            Debug.Log($"[PlanetCutsceneManager] Cutscene timer finished. Preparing to load Minigame Scene: {nextScene}");

            // (Optional) เริ่ม Fade Out ก่อนโหลด Scene
            if (fadeCanvasGroup != null)
            {
                yield return StartCoroutine(FadeEffect(0f, 1f, fadeOutTime)); // Fade Out
            }

            // Server โหลด Minigame Scene สำหรับทุกคน
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                 NetworkManager.Singleton.SceneManager.LoadScene(nextScene, LoadSceneMode.Single);
                 // พิจารณาเคลียร์ค่าใน ConnectionManager ที่นี่ หรือหลังจาก Minigame โหลดเสร็จจริงๆ
                 // ConnectionManager.Instance.NextMinigameSceneToLoad = string.Empty;
            } else {
                 Debug.LogError("[PlanetCutsceneManager] NetworkManager or SceneManager is null! Cannot load next scene.");
                 isLoadingNextScene = false; // Reset flag ถ้าโหลดไม่ได้
            }
        }
    }

     // (Optional) Coroutine สำหรับ Fade Effect
     private IEnumerator FadeEffect(float startAlpha, float endAlpha, float duration)
     {
         if (fadeCanvasGroup == null) yield break;

         float timer = 0f;
         fadeCanvasGroup.alpha = startAlpha;

         while (timer < duration)
         {
             timer += Time.deltaTime;
             fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, timer / duration);
             yield return null;
         }
         fadeCanvasGroup.alpha = endAlpha;
     }
}