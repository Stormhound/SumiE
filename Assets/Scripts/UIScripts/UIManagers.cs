using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UIManagers : MonoBehaviour
{
    [SerializeField] private GameObject MainMenu;
    [SerializeField] private GameObject SettingsMenu;
    [SerializeField] private GameObject LevelMenu;



    public void StartButton()
    {
        LevelMenu.SetActive(true);
        MainMenu.SetActive(false);
    }

    public void QuitButton()
    {
        UnityEditor.EditorApplication.isPlaying = false;
    }

    public void SettingsButton()
    {
       MainMenu.SetActive(false);
       SettingsMenu.SetActive(true);
    }

    public void BackButtonSettings()
    {
        SettingsMenu.SetActive(false);
        MainMenu.SetActive(true);        
    }
    public void BackButtonLevel()
    {
        LevelMenu?.SetActive(false);
        MainMenu.SetActive(true);
    }
 
    
}


