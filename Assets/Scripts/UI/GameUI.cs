using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
// using Holoville.HOTween; // Removed missing dependency

using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// Prototyping UI builder that spawns a Canvas with a Slider and Button.
/// </summary>
public class GameUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider inkSlider;
    [SerializeField] private Button endTurnButton;
    [SerializeField] private TextMeshProUGUI turnCountText;
    [SerializeField] private RawImage paintingDisplay; // New Reference

    private GameManager gameManager;
    private float targetInkValue = 100f; // For smooth animation

    void Awake()
    {
        // Auto-resolve if missing
        if (inkSlider == null) inkSlider = GetComponentInChildren<Slider>();
        if (endTurnButton == null) endTurnButton = GetComponentInChildren<Button>();
        if (turnCountText == null) turnCountText = GetComponentInChildren<TextMeshProUGUI>();
        if (paintingDisplay == null) paintingDisplay = GetComponentInChildren<RawImage>();
    }

    void Start()
    {
        gameManager = GameManager.Instance;
        
        if (gameManager != null)
        {
            gameManager.OnEnemyTurnStart.AddListener(OnEnemyTurn);
            gameManager.OnPlayerTurnStart.AddListener(OnPlayerTurn);
            gameManager.OnTurnChanged.AddListener(UpdateTurnCount);
            
            // Sync initial state
            UpdateTurnCount(1, gameManager.MaxTurns); // Force init
            
            // Set Painting Texture from Config
            if (paintingDisplay != null && gameManager.CurrentConfig != null && gameManager.CurrentConfig.paintingTexture != null)
            {
                if (paintingDisplay.material != null)
                {
                    paintingDisplay.material.SetTexture("_PaintingTex", gameManager.CurrentConfig.paintingTexture);
                }
            }
        }

        if (endTurnButton != null)
        {
            endTurnButton.onClick.RemoveListener(OnEndTurnClicked); // Prevent doubles
            endTurnButton.onClick.AddListener(OnEndTurnClicked);
        }
        
        if (inkSlider != null)
        {
             // Force start full
             targetInkValue = 100f; // Reset target too
             inkSlider.value = targetInkValue; 
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

    public void UpdateTurnCount(int current, int max)
    {
        if (turnCountText) turnCountText.text = $"{current}/{max}";
    }

    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
