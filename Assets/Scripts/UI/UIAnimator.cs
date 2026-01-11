using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;

public class UIAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Animation Settings")]
    [SerializeField] private float hoverScale = 1.1f;
    [SerializeField] private float clickScale = 0.95f;
    [SerializeField] private float duration = 0.2f;
    [SerializeField] private Ease easeType = Ease.OutBack;

    [Header("Audio Settings")]
    [SerializeField] private AudioClip hoverSound;
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private AudioSource audioSource;

    private Vector3 originalScale;
    private bool isHovering = false;

    private void Awake()
    {
        originalScale = transform.localScale;
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    private void OnEnable()
    {
        // Ensure we start fresh if re-enabled
        transform.localScale = originalScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        transform.DOScale(originalScale * hoverScale, duration).SetEase(easeType);
        PlaySound(hoverSound);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        transform.DOScale(originalScale, duration).SetEase(easeType);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        transform.DOScale(originalScale * clickScale, duration * 0.5f).SetEase(Ease.OutQuad);
        PlaySound(clickSound);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (isHovering)
        {
            transform.DOScale(originalScale * hoverScale, duration).SetEase(easeType);
        }
        else
        {
            transform.DOScale(originalScale, duration).SetEase(easeType);
        }
    }

    private void OnDisable()
    {
        // Kill any active tweens to prevent errors or stuck states
        transform.DOKill();
        transform.localScale = originalScale;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }
}
