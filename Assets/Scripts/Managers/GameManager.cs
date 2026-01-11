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
    [Tooltip("Percentage of enemy coverage that causes a loss.")]
    public float loseThreshold = 90f;
    [Tooltip("Minimum percentage of player ink required to stay alive.")]
    public float minPlayerInkThreshold = 0.5f;
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
    public int enemyCount = 3;
    public int maxTurns = 20;
    public int enemyStartRadius = 30;
    public float enemyExpansionPerTurn = 10f;
    public Color32 enemyColor = new Color32(255, 0, 0, 255);
    public UnityEvent OnPlayerTurnStart;
    public UnityEvent OnEnemyTurnStart;
    public UnityEvent<int, int> OnTurnChanged;
    public UnityEvent OnGameLost;

    private int currentTurnCount = 1;

    void Start()
    {
        LassoPainter painter = FindFirstObjectByType<LassoPainter>();
        if (painter != null)
        {
            // Defer slightly to ensure texture init
            StartCoroutine(InitEnemiesRoutine(painter));
        }

        // Initialize UI with turn count
        OnTurnChanged?.Invoke(currentTurnCount, maxTurns);
    }
    
    System.Collections.IEnumerator InitEnemiesRoutine(LassoPainter painter)
    {
        yield return null; // Wait for Painter Start
        painter.InitializeEnemies(enemyCount, enemyStartRadius);
    }

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

        // Enemy Action: Expand existing points
        LassoPainter painter = FindFirstObjectByType<LassoPainter>();
        if (painter != null)
        {
            yield return StartCoroutine(painter.ExpandEnemiesRoutine(enemyExpansionPerTurn));
        }

        // End Enemy Turn
        yield return new WaitForSeconds(0.5f);
        
        // CHECK TURN LIMIT
        currentTurnCount++;
        OnTurnChanged?.Invoke(currentTurnCount, maxTurns);

        if (currentTurnCount > maxTurns)
        {
            Debug.Log("Game Over: Max Turns Reached");
            OnGameLost?.Invoke();
            // Do not switch back to player turn
            yield break;
        }

        currentTurn = TurnState.Player;
        
        // Refresh Player
        if (painter != null) painter.RefillInk();
        OnPlayerTurnStart?.Invoke();
    }



    public void UpdateGameState(int totalPixels, int filledPixels, int enemyPixels)
    {
        if (totalPixels == 0) return;

        // 1. Calculate the true target
        float ratio = (float)filledPixels / totalPixels;
        targetPercentage = ratio * 100f;

        // 2. Check Win Condition
        if (!levelCompleteTriggered && targetPercentage >= winThreshold)
        {
            levelCompleteTriggered = true;
            OnLevelComplete?.Invoke();
            return;
        }

        // 3. Check Lose Condition (Enemy Area)
        float enemyRatio = (float)enemyPixels / totalPixels;
        float enemyPercentage = enemyRatio * 100f;
        
        if (!levelCompleteTriggered && enemyPercentage >= loseThreshold)
        {
            levelCompleteTriggered = true;
            Debug.Log($"Game Over: Enemy covered {enemyPercentage:F1}% (Threshold: {loseThreshold}%)");
            OnGameLost?.Invoke();
            return;
        }

        // 4. Check Lose Condition (Minimum Player Ink)
        // We only check this after the game has "started" (enemyPixels > 0) to avoid frame 0 issues.
        if (!levelCompleteTriggered && targetPercentage < minPlayerInkThreshold && enemyPixels > 0)
        {
            levelCompleteTriggered = true;
            Debug.Log($"Game Over: Player Ink dropped below {minPlayerInkThreshold}% ({targetPercentage:F1}%)");
            OnGameLost?.Invoke();
        }
    }


}