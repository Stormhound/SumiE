using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [SerializeField] private GameObject MainMenu;
    [SerializeField] private GameObject SettingsMenu;
  


    public void StartButton()
    {
        SceneManager.LoadScene("GameScene");
    }

    public void QuitButton()
    {
        Application.Quit();
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


