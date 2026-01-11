using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
// using Holoville.HOTween; // Removed missing dependency

using TMPro;

/// <summary>
/// Prototyping UI builder that spawns a Canvas with a Slider and Button.
/// </summary>
public class GameUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider inkSlider;
    [SerializeField] private Button endTurnButton;
    [SerializeField] private TextMeshProUGUI enemyCountText;

    private GameManager gameManager;
    private float targetInkValue = 100f; // For smooth animation

    void Start()
    {
        gameManager = GameManager.Instance;
        
        if (gameManager != null)
        {
            gameManager.OnEnemyTurnStart.AddListener(OnEnemyTurn);
            gameManager.OnPlayerTurnStart.AddListener(OnPlayerTurn);
        }

        if (endTurnButton != null)
        {
            endTurnButton.onClick.AddListener(OnEndTurnClicked);
        }
        
        if (inkSlider != null)
        {
             targetInkValue = inkSlider.value;
        }
    }

    void Update()
    {
        if (inkSlider != null)
        {
            // Manual smooth damp
            inkSlider.value = Mathf.Lerp(inkSlider.value, targetInkValue, Time.deltaTime * 5f);
        }
    }

    void OnEndTurnClicked()
    {
        if (gameManager) gameManager.EndPlayerTurn();
    }

    void OnEnemyTurn()
    {
        if (endTurnButton) endTurnButton.interactable = false;
    }

    void OnPlayerTurn()
    {
        if (endTurnButton) endTurnButton.interactable = true;
    }

    public void UpdateInk(float current, float max)
    {
        // No HOTween -> Manual Lerp
        targetInkValue = (current / max) * 100f;
    }

    public void UpdateEnemyCount(int count)
    {
        if (enemyCountText) enemyCountText.text = $"Enemies: {count}";
    }
}
