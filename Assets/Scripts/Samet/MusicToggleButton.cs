using UnityEngine;
using UnityEngine.UI;

public class MusicToggleButton : MonoBehaviour
{
    [Header("Button")]
    [SerializeField] private Button toggleButton;
    
    [Header("Icons")]
    [SerializeField] private Image buttonImage;
    [SerializeField] private Sprite musicOnIcon;  // Ses açık ikonu
    [SerializeField] private Sprite musicOffIcon; // Ses kapalı ikonu
    
    [Header("Audio")]
    [SerializeField] private AudioSource musicSource;
    
    private bool isMusicOn = true;
    
    private void Start()
    {
        // Kayıtlı ayarı yükle
        isMusicOn = PlayerPrefs.GetInt("MusicEnabled", 1) == 1;
        
        // Butona tıklama eventi ekle
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleMusic);
        }
        
        // Başlangıç durumunu ayarla
        UpdateMusicState();
    }
    
    private void ToggleMusic()
    {
        isMusicOn = !isMusicOn;
        UpdateMusicState();
        
        // Ayarı kaydet
        PlayerPrefs.SetInt("MusicEnabled", isMusicOn ? 1 : 0);
        PlayerPrefs.Save();
        
        Debug.Log($"Music toggled: {(isMusicOn ? "ON" : "OFF")}");
    }
    
    private void UpdateMusicState()
    {
        // Müziği aç/kapat
        if (musicSource != null)
        {
            if (isMusicOn)
            {
                if (!musicSource.isPlaying)
                    musicSource.Play();
            }
            else
            {
                musicSource.Pause();
            }
        }
        
        // İkonu değiştir
        if (buttonImage != null)
        {
            buttonImage.sprite = isMusicOn ? musicOnIcon : musicOffIcon;
        }
    }
    
    private void OnDestroy()
    {
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(ToggleMusic);
        }
    }
}
