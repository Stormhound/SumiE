using UnityEngine;

[CreateAssetMenu(fileName = "NewLevelConfig", menuName = "SumiE/Level Configuration")]
public class LevelConfiguration : ScriptableObject
{
    [Header("Game Rules")]
    [Tooltip("Percentage required to win the level.")]
    public float winThreshold = 95f;
    [Tooltip("Percentage of enemy coverage that causes a loss.")]
    public float loseThreshold = 90f;
    [Tooltip("Minimum percentage of player ink required to stay alive.")]
    public float minPlayerInkThreshold = 0.5f;
    [Tooltip("Maximum turns allowed.")]
    public int maxTurns = 20;

    [Header("Player Settings")]
    public float maxInk = 100f;
    public float inkConsumptionRate = 1.0f;
    public Color centerColor = new Color(1, 0, 0, 1f); // Used for eraser/enemy center

    [Header("Enemy Settings")]
    public int enemyCount = 3;
    public int enemyStartRadius = 30;
    public float enemyExpansionPerTurn = 10f;
    public Color32 enemyColor = new Color32(255, 0, 0, 255);

    [Header("World Generation")]
    public int pointCount = 20;
    public float minDistance = 0.2f;
    public float pointRadius = 0.15f;
    [Range(0f, 0.2f)] public float edgePadding = 0.01f;
    public Color blobColor = Color.red;

    [Header("Visuals")]
    public Texture2D paintingTexture;
}
