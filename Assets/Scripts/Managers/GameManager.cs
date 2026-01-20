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

        // --- LEVEL GENERATION ---
        if (availableConfigs == null || availableConfigs.Count == 0)
        {
            Debug.Log("[GameManager] No levels found. Generating default levels...");
            availableConfigs = new System.Collections.Generic.List<LevelConfiguration>();

            // Level 1: Standard (Not too easy)
            // 2 Enemies, decent start size. Standard consumption.
            availableConfigs.Add(CreateLevel("Level 1 (Standard)", 
                win: 70f, lose: 95f, turns: 25, 
                ink: 120f, enemies: 2, expansion: 6f, radius: 25, consumption: 1.0f));

            // Level 2: Challenge
            // 3 Enemies, growing faster. Slightly higher consumption.
            availableConfigs.Add(CreateLevel("Level 2 (Challenge)", 
                win: 80f, lose: 85f, turns: 22, 
                ink: 100f, enemies: 3, expansion: 9f, radius: 30, consumption: 1.1f));

            // Level 3: Hard (High Pressure)
            // 4 Enemies, fast growth, easier to lose.
            availableConfigs.Add(CreateLevel("Level 3 (Hard)", 
                win: 90f, lose: 75f, turns: 18, 
                ink: 90f, enemies: 4, expansion: 12f, radius: 35, consumption: 1.25f));

            // Level 4: Expert (Precision)
            // 5 Enemies, very expensive ink, very fast growth. Low lose threshold.
            availableConfigs.Add(CreateLevel("Level 4 (Expert)", 
                win: 95f, lose: 60f, turns: 15, 
                ink: 80f, enemies: 5, expansion: 15f, radius: 40, consumption: 1.5f));
        }

        // Load Config from PlayerPrefs
        int configIndex = PlayerPrefs.GetInt("Config", 0); // Default to 0 (First Level)
        
        if (availableConfigs != null && availableConfigs.Count > 0)
        {
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

    private LevelConfiguration CreateLevel(string name, float win, float lose, int turns, float ink, int enemies, float expansion, int radius, float consumption)
    {
        LevelConfiguration config = ScriptableObject.CreateInstance<LevelConfiguration>();
        config.name = name;
        config.winThreshold = win;
        config.loseThreshold = lose;
        config.maxTurns = turns;
        config.maxInk = ink;
        config.enemyCount = enemies;
        config.enemyExpansionPerTurn = expansion;
        config.enemyStartRadius = radius;
        config.inkConsumptionRate = consumption;
        
        // Defaults / Scaled
        config.minPlayerInkThreshold = 0.5f;
        config.enemyColor = new Color32(255, 0, 0, 255);
        config.centerColor = new Color(1, 0, 0, 1f);
        
        return config;
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