using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    // ✅ เปลี่ยนชื่อซีนเริ่มต้นเป็น MainMenuScene
    [SerializeField] private string sceneName = "MainMenuScene";

    private void Start()
    {
        Invoke(nameof(ChangeScene), 3f);
    }

    private void ChangeScene()
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("❌ ไม่ได้กำหนดชื่อ Scene ใน Inspector!");
        }
    }
}