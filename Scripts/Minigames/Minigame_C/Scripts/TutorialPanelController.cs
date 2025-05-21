using UnityEngine;

public class TutorialPanelController : MonoBehaviour
{
    public GameObject tutorialPanel;

    public void CloseTutorial()
    {
        tutorialPanel.SetActive(false);
    }
}
