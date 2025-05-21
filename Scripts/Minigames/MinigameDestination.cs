// สร้างไฟล์ใหม่ชื่อ MinigameDestination.cs หรือจะใส่ไว้ในไฟล์ LobbyManager.cs ก็ได้
using UnityEngine; // ต้องมีถ้าจะใช้ [System.Serializable]

[System.Serializable] // ทำให้แสดงผลและแก้ไขใน Inspector ได้
public class MinigameDestination
{
    public string planetSceneName = "Planet_A"; // ชื่อ Scene ดาวเคราะห์ (สำหรับ Cutscene)
    public string minigameSceneName = "Minigame_A"; // ชื่อ Scene Minigame จริง
}