using Unity.VectorGraphics;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelManager : MonoBehaviour
{
   [SerializeField] private Button[] LevelButtons;

    private void Awake()
    {
        int unlockedlevel = PlayerPrefs.GetInt("UnlockedLevel", 1);

        for (int i = 0; i < LevelButtons.Length; i++)
        {
            LevelButtons[i].interactable = false;
        }
        for(int i = 0; i < unlockedlevel; i++)
        {
            LevelButtons[i].interactable = true;
        }
    }

    public void LevelPlay(int LevelIndex)
    {
        PlayerPrefs.SetInt("Config", Mathf.Clamp(LevelIndex, 0, 3));
        SceneManager.LoadScene(1);
    }
}
