using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;



public class SoundManager : MonoBehaviour
{
    [SerializeField] private AudioMixer MyMixer;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider SFXSlider;
    [SerializeField] private Slider MasterSlider;


    public void SetMusicVolume()
    {
        float volume = musicSlider.value;
        MyMixer.SetFloat("music", Mathf.Log10(volume) * 20);
    }

    public void SetSFXVolume()
    {
        float volume = SFXSlider.value;
        MyMixer.SetFloat("SFX", Mathf.Log10(volume) * 20);
    }

    public void SetMasterVolume()
    {
        float volume = MasterSlider.value;
        MyMixer.SetFloat("master", Mathf.Log10(volume) * 20);
    }

}
