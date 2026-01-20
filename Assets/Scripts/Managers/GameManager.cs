using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Configuration")]
    public System.Collections.Generic.List<LevelConfiguration> availableConfigs;
    [SerializeField] private LevelConfiguration currentLevelConfig;
    public LevelConfiguration CurrentConfig => currentLevelConfig;

    [Header("Game Progress")]
    [Tooltip("Current visual value (0% to 100%)")]
    [Range(0f, 100f)] public float filledPercentage = 0f;

    [Header("Events")]
    public UnityEvent<float> OnProgressChanged;
    public UnityEvent OnLevelComplete;

    private bool levelCompleteTriggered = false;
    private float targetPercentage = 0f; // Stores the actual "Truth"
    private float animationSpeed = 5.0f; // Can also be in config if desired

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Load Config from PlayerPrefs
        int configIndex = PlayerPrefs.GetInt("Config", 1); // Default to 1 (assuming 0 might be tutorial or empty, or user preference)
        
        // Let's assume indices match the list. If list empty, skip.
        if (availableConfigs != null && availableConfigs.Count > 0)
        {
            // For safety, clamp or modulus
            // If user wants specific index:
            if (configIndex >= 0 && configIndex < availableConfigs.Count)
            {
                currentLevelConfig = availableConfigs[configIndex];
            }
            else
            {
                Debug.LogWarning($"[GameManager] Config index {configIndex} out of range (count: {availableConfigs.Count}). Using index 0.");
                currentLevelConfig = availableConfigs[0];
            }
        }
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
    
    [Header("Turn Events")]
    public UnityEvent OnPlayerTurnStart;
    public UnityEvent OnEnemyTurnStart;
    public UnityEvent<int, int> OnTurnChanged;
    public UnityEvent OnGameLost;

    private int currentTurnCount = 1;

    // --- PROPERTIES (Fallback to defaults if config null) ---
    public float WinThreshold => currentLevelConfig ? currentLevelConfig.winThreshold : 95f;
    public float LoseThreshold => currentLevelConfig ? currentLevelConfig.loseThreshold : 90f;
    public float MinPlayerInkThreshold => currentLevelConfig ? currentLevelConfig.minPlayerInkThreshold : 0.5f;
    public int MaxTurns => currentLevelConfig ? currentLevelConfig.maxTurns : 20;

    public int EnemyCount => currentLevelConfig ? currentLevelConfig.enemyCount : 3;
    public int EnemyStartRadius => currentLevelConfig ? currentLevelConfig.enemyStartRadius : 30;
    public float EnemyExpansionPerTurn => currentLevelConfig ? currentLevelConfig.enemyExpansionPerTurn : 10f;
    public Color32 EnemyColor => currentLevelConfig ? currentLevelConfig.enemyColor : new Color32(255, 0, 0, 255);


    void Start()
    {
        if (currentLevelConfig == null)
        {
            Debug.LogWarning("[GameManager] No LevelConfiguration assigned! Using hardcoded defaults.");
        }

        LassoPainter painter = FindFirstObjectByType<LassoPainter>();
        if (painter != null)
        {
            // Defer slightly to ensure texture init
            StartCoroutine(InitEnemiesRoutine(painter));
        }

        // Initialize UI with turn count
        OnTurnChanged?.Invoke(currentTurnCount, MaxTurns);
    }
    
    System.Collections.IEnumerator InitEnemiesRoutine(LassoPainter painter)
    {
        yield return null; // Wait for Painter Start
        painter.InitializeEnemies(EnemyCount, EnemyStartRadius);
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
            yield return StartCoroutine(painter.ExpandEnemiesRoutine(EnemyExpansionPerTurn));
        }

        // End Enemy Turn
        yield return new WaitForSeconds(0.5f);
        
        // CHECK TURN LIMIT
        currentTurnCount++;
        OnTurnChanged?.Invoke(currentTurnCount, MaxTurns);

        if (currentTurnCount > MaxTurns)
        {
            Debug.Log("Game Over: Max Turns Reached");
            // Trigger Lose Sequence
            levelCompleteTriggered = true;
            if (painter != null) yield return StartCoroutine(painter.FillCanvasRoutine(EnemyColor));
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
        if (!levelCompleteTriggered && targetPercentage >= WinThreshold)
        {
            levelCompleteTriggered = true;
            OnLevelComplete?.Invoke();
            return;
        }

        // 3. Check Lose Condition (Enemy Area)
        float enemyRatio = (float)enemyPixels / totalPixels;
        float enemyPercentage = enemyRatio * 100f;
        
        if (!levelCompleteTriggered && enemyPercentage >= LoseThreshold)
        {
            levelCompleteTriggered = true;
            Debug.Log($"Game Over: Enemy covered {enemyPercentage:F1}% (Threshold: {LoseThreshold}%)");
            
            // Trigger Animation
            LassoPainter painter = FindFirstObjectByType<LassoPainter>();
            if (painter != null) StartCoroutine(TriggerLoseSequence(painter));
            else OnGameLost?.Invoke();

            return;
        }

        // 4. Check Lose Condition (Minimum Player Ink)
        // We only check this after the game has "started" (enemyPixels > 0) to avoid frame 0 issues.
        if (!levelCompleteTriggered && targetPercentage < MinPlayerInkThreshold && enemyPixels > 0)
        {
            levelCompleteTriggered = true;
            Debug.Log($"Game Over: Player Ink dropped below {MinPlayerInkThreshold}% ({targetPercentage:F1}%)");
            
            // Trigger Animation
            LassoPainter painter = FindFirstObjectByType<LassoPainter>();
            if (painter != null) StartCoroutine(TriggerLoseSequence(painter));
            else OnGameLost?.Invoke();
        }
    }

    private System.Collections.IEnumerator TriggerLoseSequence(LassoPainter painter)
    {
        yield return StartCoroutine(painter.FillCanvasRoutine(EnemyColor));
        OnGameLost?.Invoke();
    }


}