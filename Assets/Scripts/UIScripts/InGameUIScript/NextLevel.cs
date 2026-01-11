using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class NextLevel : MonoBehaviour
{
    public void UnlockNewLevel()
    {
        if(SceneManager.GetActiveScene().buildIndex >= PlayerPrefs.GetInt("ReachedIndex"))
        {
            PlayerPrefs.SetInt("ReachedIndex", PlayerPrefs.GetInt("ReachedIndex") + 1);
            PlayerPrefs.SetInt("Config", Mathf.Clamp(PlayerPrefs.GetInt("UnlockedLevel", 1), 0, 3));
            PlayerPrefs.SetInt("UnlockedLevel", PlayerPrefs.GetInt("UnlockedLevel", 1) + 1);
            PlayerPrefs.Save();
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
