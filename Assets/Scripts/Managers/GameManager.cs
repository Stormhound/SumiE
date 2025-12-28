using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Game Progress")]
    [Tooltip("Current visual value (0% to 100%)")]
    [Range(0f, 100f)] public float filledPercentage = 0f;

    [Header("Settings")]
    [Tooltip("Percentage required to win the level.")]
    public float winThreshold = 95f;
    [Tooltip("How fast the progress bar catches up (Higher = Faster).")]
    public float animationSpeed = 5.0f; // NEW: Controls smoothness

    [Header("Events")]
    public UnityEvent<float> OnProgressChanged;
    public UnityEvent OnLevelComplete;

    private bool levelCompleteTriggered = false;
    private float targetPercentage = 0f; // Stores the actual "Truth"

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        // Smoothly animate current value towards the target value
        if (Mathf.Abs(filledPercentage - targetPercentage) > 0.01f)
        {
            filledPercentage = Mathf.Lerp(filledPercentage, targetPercentage, Time.deltaTime * animationSpeed);
            OnProgressChanged?.Invoke(filledPercentage);
        }
    }

    public void UpdateGameState(int totalPixels, int filledPixels)
    {
        if (totalPixels == 0) return;

        // 1. Calculate the true target
        float ratio = (float)filledPixels / totalPixels;
        targetPercentage = ratio * 100f;

        // 2. Check Win Condition
        // We check against 'targetPercentage' so the logic triggers immediately,
        // even if the UI bar is still animating up.
        if (!levelCompleteTriggered && targetPercentage >= winThreshold)
        {
            levelCompleteTriggered = true;
            Debug.Log($"Level Complete! Covered: {targetPercentage:F1}%");
            OnLevelComplete?.Invoke();
        }
    }
}