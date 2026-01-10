using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
// using Holoville.HOTween; // Removed missing dependency

/// <summary>
/// Prototyping UI builder that spawns a Canvas with a Slider and Button.
/// </summary>
public class GameUI : MonoBehaviour
{
    private Slider inkSlider;
    private Button endTurnButton;
    private GameManager gameManager;
    
    private Text enemyCountText;
    
    private float targetInkValue = 100f; // For smooth animation

    void Start()
    {
        // ... (Previous setup code remains implicitly valid if not touched, but we need to ensure the class structure is valid)
        // To be safe, I will just rewrite the necessary parts of the class logic or just patch the specific methods.
        // But since I am replacing the whole file content in my head, I should be careful.
        // Actually, I can just replace specific blocks.
        
        gameManager = GameManager.Instance;
        if (gameManager == null) return;

        // Build UI Hierarchy
        GameObject canvasObj = new GameObject("GameCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Ink Slider
        GameObject sliderObj = new GameObject("InkSlider");
        sliderObj.transform.SetParent(canvasObj.transform, false);
        inkSlider = sliderObj.AddComponent<Slider>();
        
        // Setup RectTransform (Top Left)
        RectTransform sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0, 1);
        sliderRect.anchorMax = new Vector2(0, 1);
        sliderRect.pivot = new Vector2(0, 1);
        sliderRect.anchoredPosition = new Vector2(20, -20);
        sliderRect.sizeDelta = new Vector2(300, 30);

        // Slider Background & Fill (Basic Setup)
        CreateSliderVisuals(sliderObj);

        // End Turn Button
        GameObject buttonObj = new GameObject("EndTurnButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);
        endTurnButton = buttonObj.AddComponent<Button>();
        Image btnImg = buttonObj.AddComponent<Image>();
        btnImg.color = Color.gray;

        // Setup RectTransform (Bottom Right)
        RectTransform btnRect = buttonObj.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(1, 0);
        btnRect.anchorMax = new Vector2(1, 0);
        btnRect.pivot = new Vector2(1, 0);
        btnRect.anchoredPosition = new Vector2(-20, 20);
        btnRect.sizeDelta = new Vector2(150, 50);

        // Button Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        Text btnText = textObj.AddComponent<Text>();
        btnText.text = "End Turn";
        btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.black;
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // Logic
        endTurnButton.onClick.AddListener(OnEndTurnClicked);
        
        // Enemy Counter Text (Top Center)
        GameObject enemyTextObj = new GameObject("EnemyCounter");
        enemyTextObj.transform.SetParent(canvasObj.transform, false);
        enemyCountText = enemyTextObj.AddComponent<Text>();
        enemyCountText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // Changed: Was Arial
        enemyCountText.alignment = TextAnchor.MiddleCenter;
        enemyCountText.fontSize = 24;
        enemyCountText.color = Color.white;
        
        RectTransform enemyRect = enemyTextObj.GetComponent<RectTransform>();
        enemyRect.anchorMin = new Vector2(0.5f, 1);
        enemyRect.anchorMax = new Vector2(0.5f, 1);
        enemyRect.pivot = new Vector2(0.5f, 1);
        enemyRect.anchoredPosition = new Vector2(0, -20);
        enemyRect.sizeDelta = new Vector2(300, 50);

        // Subscribe to Events
        if (gameManager != null)
        {
            gameManager.OnEnemyTurnStart.AddListener(OnEnemyTurn);
            gameManager.OnPlayerTurnStart.AddListener(OnPlayerTurn);
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

    void CreateSliderVisuals(GameObject sliderObj)
    {
        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(sliderObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1, 0.75f);
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-5, 0);

        // Fill
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.cyan;
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        inkSlider.targetGraphic = bgImg;
        inkSlider.fillRect = fillRect;
        inkSlider.direction = Slider.Direction.LeftToRight;
        inkSlider.minValue = 0;
        inkSlider.maxValue = 100;
        inkSlider.value = 100;
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
