using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MainMenuController : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button BackButtonSettings;
    [SerializeField] private Button BackButtonLevel;
    

    [Header("Animation Settings")]
    [SerializeField] private float buttonHoverScale = 1.1f;
    [SerializeField] private float animationSpeed = 0.2f;

    [Header("SFX Source")]
    [SerializeField] private AudioSource SFXSource;
    [SerializeField] private AudioClip ButtonSound;
    
    private Coroutine currentHoverCoroutine;
    private Coroutine currentClickCoroutine;

    private void Start()
    {
        SetupButtons();    
    }

    private void PlayClickSound()
    {
        SFXSource.PlayOneShot(ButtonSound);
    }

    private void SetupButtons()
    {
        if (startGameButton != null)
        {
            AddButtonHoverEffect(startGameButton);     
        }

        if (quitButton != null)
        {          
            AddButtonHoverEffect(quitButton);      
        }

        if (settingsButton != null)
        {       
            AddButtonHoverEffect(settingsButton);          
        }

        if (BackButtonSettings != null)
        {
            AddButtonHoverEffect(BackButtonSettings);           
        }

        if (BackButtonLevel != null)
        {
            AddButtonHoverEffect(BackButtonLevel);
        }


    }

    private void AddButtonHoverEffect(Button button)
    {
        var eventTrigger = button.gameObject.GetComponent<EventTrigger>();

        if (eventTrigger == null)
        {
            eventTrigger = button.gameObject.AddComponent<EventTrigger>();
        }

        var pointerEnter = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerEnter
        };
        pointerEnter.callback.AddListener((data) => { OnButtonHover(button, true); });
        eventTrigger.triggers.Add(pointerEnter);

        var pointerExit = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerExit
        };
        pointerExit.callback.AddListener((data) => { OnButtonHover(button, false); });
        eventTrigger.triggers.Add(pointerExit);
    }

    private void OnButtonHover(Button button, bool isHovering)
    {      
        if(isHovering)
        {
            PlayClickSound();
        }
        float targetScale = isHovering ? buttonHoverScale : 1f; 

        if (currentHoverCoroutine != null)
            StopCoroutine(currentHoverCoroutine);          
        currentHoverCoroutine = StartCoroutine(ScaleButton(button.transform, targetScale, animationSpeed));       

    }
    
    private System.Collections.IEnumerator ScaleButton(Transform target, float targetScale, float duration)
    {       
        Vector3 startScale = target.localScale;
        Vector3 endScale = Vector3.one * targetScale;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            target.localScale = Vector3.Lerp(startScale, endScale, t);
            yield return null;
        }
        
        target.localScale = endScale;
    }

    private void AnimateButtonClick(Button button, System.Action onComplete)
    {
        if (currentClickCoroutine != null)
            StopCoroutine(currentClickCoroutine);
            
        currentClickCoroutine = StartCoroutine(ButtonClickAnimation(button.transform, onComplete));
    }
    
    private System.Collections.IEnumerator ButtonClickAnimation(Transform target, System.Action onComplete)
    {
        Vector3 originalScale = target.localScale;
        float halfDuration = animationSpeed * 0.5f;
        float elapsed = 0f;
        
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale, originalScale * 0.9f, t);
            yield return null;
        }
        
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale * 0.9f, originalScale, t);
            yield return null;
        }
        
        target.localScale = originalScale;
        onComplete?.Invoke();
    }

 
}
