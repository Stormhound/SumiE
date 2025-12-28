using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UIManagers : MonoBehaviour
{
    [SerializeField] private GameObject MainMenu;
    [SerializeField] private GameObject SettingsMenu;


  
    public void StartButton()
    {
        SceneManager.LoadScene("GameScene");
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

    public void BackButton()
    {
        SettingsMenu.SetActive(false);
        MainMenu.SetActive(true);        
    }
 
    
}


