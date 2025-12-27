using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(LineRenderer))]
public class LassoPainter : MonoBehaviour
{
    [Header("Stabilizer Settings")]
    [Range(0f, 0.5f)]
    public float smoothTime = 0.05f; // Higher = "Lazier" brush (more stable)
    public float minPointDistance = 0.1f;

    [Header("Lasso Settings")]
    public float closeThreshold = 0.5f; // Distance to close loop on release
    public float maxInk = 100f;
    public float inkConsumptionRate = 10f;

    [Header("Visual Settings")]
    public Color fillColor = new Color(1, 0, 0, 0.5f);
    public int blurIterations = 1; // Set to 0 for sharp, 1-2 for soft edges
    public RawImage outputDisplay;

    [Header("Performance")]
    [Range(0.1f, 1f)]
    public float resolutionScale = 0.5f;

    // Internal State
    private LineRenderer lr;
    private List<Vector2> worldPoints = new List<Vector2>();
    private Texture2D drawTexture;
    private Color[] clearColors;
    private bool isDrawing = false;
    private Camera mainCam;

    // Stabilizer vars
    private Vector2 currentSmoothPos;
    private Vector2 smoothVelocity;

    // Cached dimensions
    private int texWidth;
    private int texHeight;

    void Start()
    {
        lr = GetComponent<LineRenderer>();
        mainCam = Camera.main;
        InitializeTexture();
    }

    void InitializeTexture()
    {
        texWidth = Mathf.RoundToInt(Screen.width * resolutionScale);
        texHeight = Mathf.RoundToInt(Screen.height * resolutionScale);

        drawTexture = new Texture2D(texWidth, texHeight);
        drawTexture.filterMode = FilterMode.Bilinear; // Helps smooth scaling

        clearColors = new Color[texWidth * texHeight];
        for (int i = 0; i < clearColors.Length; i++) clearColors[i] = Color.clear;

        drawTexture.SetPixels(clearColors);
        drawTexture.Apply();

        if (outputDisplay != null)
        {
            outputDisplay.texture = drawTexture;
            RectTransform rt = outputDisplay.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    void Update()
    {
        // 1. Mouse Down
        if (Input.GetMouseButtonDown(0))
        {
            StartStroke();
        }

        // 2. Mouse Hold
        if (Input.GetMouseButton(0) && isDrawing)
        {
            UpdateStroke();
        }

        // 3. Mouse Up (NOW handles the closing logic)
        if (Input.GetMouseButtonUp(0))
        {
            EndStroke();
        }
    }

    void StartStroke()
    {
        isDrawing = true;
        worldPoints.Clear();
        lr.positionCount = 0;

        // Reset Texture
        drawTexture.SetPixels(clearColors);
        drawTexture.Apply();

        // Initialize stabilizer to current mouse pos so it doesn't "jump"
        currentSmoothPos = GetMouseWorldPos();
        AddPoint(currentSmoothPos);
    }

    void UpdateStroke()
    {
        // STABILIZER LOGIC
        Vector2 targetPos = GetMouseWorldPos();
        // SmoothDamp creates that "dragged by a string" feeling
        currentSmoothPos = Vector2.SmoothDamp(currentSmoothPos, targetPos, ref smoothVelocity, smoothTime);

        // Only add point if moved enough
        if (worldPoints.Count > 0 && Vector2.Distance(worldPoints[worldPoints.Count - 1], currentSmoothPos) > minPointDistance)
        {
            AddPoint(currentSmoothPos);
        }
    }

    void EndStroke()
    {
        isDrawing = false;

        // CHECK CLOSURE ON RELEASE
        if (worldPoints.Count > 5)
        {
            // Compare the very last smooth point with the start point
            if (Vector2.Distance(worldPoints[worldPoints.Count - 1], worldPoints[0]) < closeThreshold)
            {
                // It's a loop! Fill it.
                FillShape(worldPoints);
            }
        }

        // Optional: Hide line immediately or fade it out
        lr.positionCount = 0;
    }

    void AddPoint(Vector2 pos)
    {
        worldPoints.Add(pos);
        lr.positionCount = worldPoints.Count;
        lr.SetPosition(worldPoints.Count - 1, pos);
    }

    void FillShape(List<Vector2> points)
    {
        List<Vector2Int> pixelPoints = new List<Vector2Int>();

        // Convert to Coords
        foreach (Vector2 p in points)
        {
            Vector3 screenPos = mainCam.WorldToScreenPoint(p);
            int x = Mathf.RoundToInt(screenPos.x * resolutionScale);
            int y = Mathf.RoundToInt(screenPos.y * resolutionScale);
            pixelPoints.Add(new Vector2Int(x, y));
        }

        Color[] pixels = drawTexture.GetPixels();

        // Track bounds for faster blurring
        int minX = texWidth, maxX = 0;
        int minY = texHeight, maxY = 0;

        foreach (var p in pixelPoints)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        // Clamp bounds
        minX = Mathf.Clamp(minX, 0, texWidth - 1);
        maxX = Mathf.Clamp(maxX, 0, texWidth - 1);
        minY = Mathf.Clamp(minY, 0, texHeight - 1);
        maxY = Mathf.Clamp(maxY, 0, texHeight - 1);

        // --- SCANLINE FILL ---
        for (int y = minY; y <= maxY; y++)
        {
            List<int> nodes = new List<int>();
            int j = pixelPoints.Count - 1;
            for (int i = 0; i < pixelPoints.Count; i++)
            {
                if ((pixelPoints[i].y < y && pixelPoints[j].y >= y) || (pixelPoints[j].y < y && pixelPoints[i].y >= y))
                {
                    float intersectX = pixelPoints[i].x + (float)(y - pixelPoints[i].y) / (pixelPoints[j].y - pixelPoints[i].y) * (pixelPoints[j].x - pixelPoints[i].x);
                    nodes.Add((int)intersectX);
                }
                j = i;
            }
            nodes.Sort();
            for (int k = 0; k < nodes.Count; k += 2)
            {
                if (k + 1 >= nodes.Count) break;
                int startX = Mathf.Clamp(nodes[k], 0, texWidth - 1);
                int endX = Mathf.Clamp(nodes[k + 1], 0, texWidth - 1);
                for (int x = startX; x < endX; x++)
                {
                    pixels[y * texWidth + x] = fillColor;
                }
            }
        }

        // --- BLUR PASS (Anti-Aliasing) ---
        if (blurIterations > 0)
        {
            pixels = ApplyBoxBlur(pixels, minX, maxX, minY, maxY, blurIterations);
        }

        drawTexture.SetPixels(pixels);
        drawTexture.Apply();
    }

    // A fast Box Blur that only runs inside the modified bounds
    Color[] ApplyBoxBlur(Color[] sourcePixels, int minX, int maxX, int minY, int maxY, int iterations)
    {
        // Expand bounds slightly to avoid clipping the blur
        int pad = iterations * 2;
        minX = Mathf.Clamp(minX - pad, 0, texWidth - 1);
        maxX = Mathf.Clamp(maxX + pad, 0, texWidth - 1);
        minY = Mathf.Clamp(minY - pad, 0, texHeight - 1);
        maxY = Mathf.Clamp(maxY + pad, 0, texHeight - 1);

        int w = texWidth;

        // Temporary buffer
        Color[] buffer = (Color[])sourcePixels.Clone();

        for (int it = 0; it < iterations; it++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Simple 3x3 Box Kernel
                    Color sum = Color.clear;
                    int count = 0;

                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int px = x + kx;
                            int py = y + ky;

                            if (px >= 0 && px < w && py >= 0 && py < texHeight)
                            {
                                sum += sourcePixels[py * w + px];
                                count++;
                            }
                        }
                    }
                    buffer[y * w + x] = sum / count;
                }
            }
            // Swap buffers for next iteration
            System.Array.Copy(buffer, sourcePixels, buffer.Length);
        }
        return sourcePixels;
    }

    Vector2 GetMouseWorldPos()
    {
        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -mainCam.transform.position.z;
        return mainCam.ScreenToWorldPoint(mouseScreen);
    }
}