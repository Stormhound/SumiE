using UnityEngine;
using UnityEngine.SceneManagement;

public class NextLevel : MonoBehaviour
{
    public void UnlockNewLevel()
    {
        if(PlayerPrefs.GetInt("Config", 0) < GameManager.Instance.availableConfigs.Count - 1)
        {
            PlayerPrefs.SetInt("ReachedIndex", PlayerPrefs.GetInt("ReachedIndex") + 1);
            PlayerPrefs.SetInt("Config", PlayerPrefs.GetInt("Config", 0) + 1);

            Debug.LogWarning($"Next Level: {PlayerPrefs.GetInt("Config", 0) + 1}");

            PlayerPrefs.SetInt("UnlockedLevel", PlayerPrefs.GetInt("UnlockedLevel", 1) + 1);
            PlayerPrefs.Save();
        }
        else
        {
            Debug.LogWarning($"Baþa Dönüldü");

            PlayerPrefs.SetInt("Config", 0);
            PlayerPrefs.Save();
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
