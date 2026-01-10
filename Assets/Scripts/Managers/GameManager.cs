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

    // --- TURN MANAGEMENT ---
    public enum TurnState { Player, Enemy }
    public TurnState currentTurn = TurnState.Player;
    
    [Header("Turn Settings")]
    public int enemyEraseCount = 3;
    public int enemyEraseRadius = 50;
    public UnityEvent OnPlayerTurnStart;
    public UnityEvent OnEnemyTurnStart;

    public void EndPlayerTurn()
    {
        if (currentTurn != TurnState.Player || levelCompleteTriggered) return;

        currentTurn = TurnState.Enemy;
        OnEnemyTurnStart?.Invoke();
        StartCoroutine(ProcessEnemyTurn());
    }

    private System.Collections.IEnumerator ProcessEnemyTurn()
    {
        // Simulate thinking time
        yield return new WaitForSeconds(1.0f);

        // Enemy Action: Erase random parts
        LassoPainter painter = FindFirstObjectByType<LassoPainter>();
        if (painter != null)
        {
            painter.EraseRandomAreas(enemyEraseCount, enemyEraseRadius);
        }

        // End Enemy Turn
        yield return new WaitForSeconds(0.5f);
        currentTurn = TurnState.Player;
        
        // Refresh Player
        if (painter != null) painter.RefillInk();
        OnPlayerTurnStart?.Invoke();
    }

    public void UpdateGameState(int totalPixels, int filledPixels)
    {
        if (totalPixels == 0) return;

        // 1. Calculate the true target
        float ratio = (float)filledPixels / totalPixels;
        targetPercentage = ratio * 100f;

        // 2. Check Win Condition
        if (!levelCompleteTriggered && targetPercentage >= winThreshold)
        {
            levelCompleteTriggered = true;
            Debug.Log($"Level Complete! Covered: {targetPercentage:F1}%");
            OnLevelComplete?.Invoke();
        }
    }
}