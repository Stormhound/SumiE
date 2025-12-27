using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

[RequireComponent(typeof(LineRenderer))]
public class LassoPainter : MonoBehaviour
{
    [Header("References")]
    public ScatterPoints scatterScript;
    public Camera cam;
    public LayerMask inkLayer;
    public RawImage outputDisplay;

    [Header("Visual Settings")]
    public Color centerColor = new Color(1, 0, 0, 1f);
    [Range(0, 10)] public int blurPasses = 3;
    public int blurRadius = 2;
    [Range(0.1f, 1f)] public float resolutionScale = 0.5f;

    [Tooltip("Distance in pixels from the edge where the ink becomes fully opaque.")]
    public float gradientWidth = 24.0f;
    [Tooltip("Controls the falloff curve. 1 = Linear, <1 = Softer, >1 = Sharper")]
    [Range(0.1f, 3.0f)] public float smoothness = 1.0f;

    [Header("Interaction")]
    [Range(0f, 0.5f)] public float smoothTime = 0.05f;
    public float minPointDistance = 0.1f;
    public float closeThreshold = 0.5f;

    [Header("Ink Settings")]
    public float maxInk = 100f;
    public float inkConsumptionRate = 1.0f;
    [SerializeField] private float currentInk;

    [Header("Animation")]
    [Range(0.1f, 2.0f)] public float fillDuration = 0.5f;

    // Internal State
    private LineRenderer lr;
    private List<Vector2> worldPoints = new List<Vector2>();
    private Texture2D drawTexture;
    private bool isDrawing = false;
    private bool isCalculating = false;
    private Vector2 currentSmoothPos;
    private Vector2 smoothVelocity;

    // Cache
    private int texWidth, texHeight;
    private NativeArray<Color32> backupTextureState;

    void Start()
    {
        lr = GetComponent<LineRenderer>();
        if (cam == null) cam = Camera.main;
        currentInk = maxInk;
        InitializeTexture();
    }

    void InitializeTexture()
    {
        texWidth = Mathf.RoundToInt(Screen.width * resolutionScale);
        texHeight = Mathf.RoundToInt(Screen.height * resolutionScale);

        drawTexture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        drawTexture.filterMode = FilterMode.Bilinear;

        var pixels = drawTexture.GetRawTextureData<Color32>();
        new ClearTextureJob { pixelData = pixels }.Run(pixels.Length);
        drawTexture.Apply();

        if (outputDisplay != null) outputDisplay.texture = drawTexture;

        if (scatterScript != null)
        {
            scatterScript.GenerateAndRenderToTexture(drawTexture, texWidth, texHeight);
        }
    }

    void Update()
    {
        if (isCalculating) return;

        if (Input.GetMouseButtonDown(0)) StartStroke();
        if (Input.GetMouseButton(0) && isDrawing) UpdateStroke();
        if (Input.GetMouseButtonUp(0)) EndStroke();
    }

    void StartStroke()
    {
        if (currentInk <= 0) return;

        Vector2 mousePos = GetMouseWorldPos();
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero, float.MaxValue, inkLayer);

        if (hit.collider != null)
        {
            isDrawing = true;
            worldPoints.Clear();
            lr.positionCount = 0;
            currentSmoothPos = mousePos;
            AddPoint(mousePos);
        }
    }

    void UpdateStroke()
    {
        Vector2 targetPos = GetMouseWorldPos();
        currentSmoothPos = Vector2.SmoothDamp(currentSmoothPos, targetPos, ref smoothVelocity, smoothTime);

        float dist = 0f;
        if (worldPoints.Count > 0)
            dist = Vector2.Distance(worldPoints[worldPoints.Count - 1], currentSmoothPos);

        if (dist > minPointDistance)
        {
            float cost = dist * inkConsumptionRate;
            if (currentInk >= cost)
            {
                currentInk -= cost;
                AddPoint(currentSmoothPos);
            }
            else
            {
                currentInk = 0;
                EndStroke();
            }
        }
    }

    void EndStroke()
    {
        if (!isDrawing) { return; }

        isDrawing = false;
        if (worldPoints.Count < 5)
        {
            lr.positionCount = 0;
            return;
        }

        // 1. Departure Check
        float maxDistanceFromStart = 0f;
        Vector2 startPoint = worldPoints[0];
        foreach (var p in worldPoints)
        {
            float d = Vector2.Distance(p, startPoint);
            if (d > maxDistanceFromStart) maxDistanceFromStart = d;
        }

        if (maxDistanceFromStart < closeThreshold * 1.5f)
        {
            lr.positionCount = 0;
            return;
        }

        // 2. Closure Check
        Vector2 releasePos = GetMouseWorldPos();
        Vector2 lastPoint = worldPoints[worldPoints.Count - 1];
        bool shouldClose = false;

        if (Vector2.Distance(releasePos, startPoint) < closeThreshold ||
            Vector2.Distance(lastPoint, startPoint) < closeThreshold)
        {
            shouldClose = true;
        }
        else
        {
            RaycastHit2D hit = Physics2D.Raycast(lastPoint, Vector2.zero, float.MaxValue, inkLayer);
            if (hit.collider != null) shouldClose = true;
        }

        if (shouldClose)
        {
            StartCoroutine(RunJobsAndAnimate());
            lr.positionCount = 0;
        }
        else
        {
            lr.positionCount = 0;
        }
    }

    void AddPoint(Vector2 pos)
    {
        worldPoints.Add(pos);
        lr.positionCount = worldPoints.Count;
        lr.SetPosition(worldPoints.Count - 1, pos);
    }

    IEnumerator RunJobsAndAnimate()
    {
        isCalculating = true;

        // Snapshot
        NativeArray<Color32> currentPixels = drawTexture.GetRawTextureData<Color32>();
        if (backupTextureState.IsCreated) backupTextureState.Dispose();
        backupTextureState = new NativeArray<Color32>(currentPixels, Allocator.Persistent);

        // Prepare
        NativeArray<int2> pointBuffer = new NativeArray<int2>(worldPoints.Count, Allocator.TempJob);
        int minX = 0; int maxX = texWidth - 1;
        int minY = 0; int maxY = texHeight - 1;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 sPos = cam.WorldToScreenPoint(worldPoints[i]);
            int x = Mathf.Clamp(Mathf.RoundToInt(sPos.x * resolutionScale), 0, texWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(sPos.y * resolutionScale), 0, texHeight - 1);
            pointBuffer[i] = new int2(x, y);
        }

        int jobW = texWidth;
        int jobH = texHeight;

        // Job Buffers
        NativeArray<float> distGrid = new NativeArray<float>(jobW * jobH, Allocator.TempJob);
        NativeArray<Color32> finalFillColors = new NativeArray<Color32>(jobW * jobH, Allocator.TempJob);
        NativeList<PixelSortData> sortKeys = new NativeList<PixelSortData>(jobW * jobH, Allocator.TempJob);

        // Jobs
        var maskJob = new MaskFromTextureJob { texturePixels = backupTextureState, outputGrid = distGrid, width = jobW, height = jobH, offsetX = minX, offsetY = minY };
        JobHandle handle = maskJob.Schedule(jobW * jobH, 64);

        var scanlineJob = new ScanlineJob { points = pointBuffer, outputGrid = distGrid, width = jobW, height = jobH, offsetX = minX, offsetY = minY };
        handle = scanlineJob.Schedule(handle);

        handle = new ChamferJobPass1 { grid = distGrid, width = jobW, height = jobH }.Schedule(handle);
        handle = new ChamferJobPass2 { grid = distGrid, width = jobW, height = jobH }.Schedule(handle);

        handle = new GradientJob { grid = distGrid, outputColors = finalFillColors, gradientWidth = gradientWidth, smoothness = smoothness, centerColor = centerColor }.Schedule(jobW * jobH, 64, handle);

        NativeArray<Color32> blurBuffer = new NativeArray<Color32>(jobW * jobH, Allocator.TempJob);
        for (int i = 0; i < blurPasses; i++)
        {
            handle = new BlurHorzJob { source = finalFillColors, destination = blurBuffer, width = jobW, height = jobH, radius = blurRadius }.Schedule(jobW * jobH, 64, handle);
            handle = new BlurVertJob { source = blurBuffer, destination = finalFillColors, width = jobW, height = jobH, radius = blurRadius }.Schedule(jobW * jobH, 64, handle);
        }

        handle = new CollectSortDataJob { colors = finalFillColors, sortList = sortKeys, globalOffsetX = minX, globalOffsetY = minY, totalCanvasWidth = texWidth, jobWidth = jobW }.Schedule(handle);
        handle = new SortPixelJob { list = sortKeys }.Schedule(handle);

        handle.Complete();

        // Animate
        int totalPixels = sortKeys.Length;
        float startTime = Time.time;
        int processedCount = 0;
        NativeArray<Color32> liveTexture = drawTexture.GetRawTextureData<Color32>();

        while (processedCount < totalPixels)
        {
            float t = (Time.time - startTime) / fillDuration;
            int targetCount = Mathf.Clamp(Mathf.FloorToInt(t * totalPixels), 0, totalPixels);

            while (processedCount < targetCount)
            {
                PixelSortData data = sortKeys[processedCount];
                liveTexture[data.globalIndex] = data.color;
                processedCount++;
            }
            drawTexture.Apply();
            yield return null;
        }

        // *** RECALCULATE COLLIDERS HERE (After animation is done) ***
        if (scatterScript != null)
        {
            // We pass the lasso points to the Scatter script to append the new shape
            scatterScript.AddLassoCollider(worldPoints);
        }

        // Cleanup
        pointBuffer.Dispose();
        distGrid.Dispose();
        finalFillColors.Dispose();
        blurBuffer.Dispose();
        sortKeys.Dispose();
        if (backupTextureState.IsCreated) backupTextureState.Dispose();

        isCalculating = false;
    }

    public void RefillInk() { currentInk = maxInk; }

    Vector2 GetMouseWorldPos()
    {
        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -cam.transform.position.z;
        return cam.ScreenToWorldPoint(mouseScreen);
    }

    void OnDestroy() { if (backupTextureState.IsCreated) backupTextureState.Dispose(); }
}

// --- JOBS ---
[BurstCompile] struct ClearTextureJob : IJobParallelFor { public NativeArray<Color32> pixelData; public void Execute(int index) => pixelData[index] = new Color32(0, 0, 0, 0); }
[BurstCompile] struct MaskFromTextureJob : IJobParallelFor { [ReadOnly] public NativeArray<Color32> texturePixels; public NativeArray<float> outputGrid; public int width, height, offsetX, offsetY; public void Execute(int index) { int y = index / width; int x = index % width; int globalIndex = (y + offsetY) * width + (x + offsetX); if (globalIndex >= 0 && globalIndex < texturePixels.Length) outputGrid[index] = (texturePixels[globalIndex].a > 0) ? -1f : 0f; else outputGrid[index] = 0f; } }
[BurstCompile] struct ScanlineJob : IJob { [ReadOnly] public NativeArray<int2> points; public NativeArray<float> outputGrid; public int width, height, offsetX, offsetY; public void Execute() { for (int y = 0; y < height; y++) { int globalY = y + offsetY; NativeList<int> nodes = new NativeList<int>(32, Allocator.Temp); int j = points.Length - 1; for (int i = 0; i < points.Length; i++) { int2 pi = points[i]; int2 pj = points[j]; if ((pi.y < globalY && pj.y >= globalY) || (pj.y < globalY && pi.y >= globalY)) { float intersect = pi.x + (float)(globalY - pi.y) / (pj.y - pi.y) * (pj.x - pi.x); nodes.Add((int)intersect); } j = i; } nodes.Sort(); for (int k = 0; k < nodes.Length; k += 2) { if (k + 1 >= nodes.Length) break; int startX = Mathf.Clamp(nodes[k] - offsetX, 0, width - 1); int endX = Mathf.Clamp(nodes[k + 1] - offsetX, 0, width - 1); for (int x = startX; x < endX; x++) outputGrid[y * width + x] = -1f; } nodes.Dispose(); } } }
[BurstCompile] struct ChamferJobPass1 : IJob { public NativeArray<float> grid; public int width, height; public void Execute() { for (int i = 0; i < grid.Length; i++) if (grid[i] < 0) grid[i] = 99999f; for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { int idx = y * width + x; if (grid[idx] > 0) { float minVal = 99999f; if (x > 0) minVal = math.min(minVal, grid[y * width + (x - 1)] + 1.0f); if (y > 0) minVal = math.min(minVal, grid[(y - 1) * width + x] + 1.0f); if (x > 0 && y > 0) minVal = math.min(minVal, grid[(y - 1) * width + (x - 1)] + 1.414f); if (x < width - 1 && y > 0) minVal = math.min(minVal, grid[(y - 1) * width + (x + 1)] + 1.414f); if (minVal < grid[idx]) grid[idx] = minVal; } } } } }
[BurstCompile] struct ChamferJobPass2 : IJob { public NativeArray<float> grid; public int width, height; public void Execute() { for (int y = height - 1; y >= 0; y--) { for (int x = width - 1; x >= 0; x--) { int idx = y * width + x; if (grid[idx] > 0) { float minVal = grid[idx]; if (x < width - 1) minVal = math.min(minVal, grid[y * width + (x + 1)] + 1.0f); if (y < height - 1) minVal = math.min(minVal, grid[(y + 1) * width + x] + 1.0f); if (x < width - 1 && y < height - 1) minVal = math.min(minVal, grid[(y + 1) * width + (x + 1)] + 1.414f); if (x > 0 && y < height - 1) minVal = math.min(minVal, grid[(y + 1) * width + (x - 1)] + 1.414f); grid[idx] = minVal; } } } } }
[BurstCompile] struct GradientJob : IJobParallelFor { [ReadOnly] public NativeArray<float> grid; public NativeArray<Color32> outputColors; public float gradientWidth; public float smoothness; public Color32 centerColor; public void Execute(int index) { float d = grid[index]; if (d <= 0 || d > 99990f) { outputColors[index] = new Color32(0, 0, 0, 0); return; } float normalized = math.clamp(d / gradientWidth, 0f, 1f); float alpha = math.pow(normalized, smoothness); outputColors[index] = new Color32(centerColor.r, centerColor.g, centerColor.b, (byte)(alpha * 255)); } }
[BurstCompile] struct BlurHorzJob : IJobParallelFor { [ReadOnly] public NativeArray<Color32> source; public NativeArray<Color32> destination; public int width, height, radius; public void Execute(int index) { int y = index / width; int x = index % width; float4 sum = float4.zero; int count = 0; for (int k = -radius; k <= radius; k++) { int px = math.clamp(x + k, 0, width - 1); Color32 c = source[y * width + px]; sum += new float4(c.r, c.g, c.b, c.a); count++; } sum /= count; destination[index] = new Color32((byte)sum.x, (byte)sum.y, (byte)sum.z, (byte)sum.w); } }
[BurstCompile] struct BlurVertJob : IJobParallelFor { [ReadOnly] public NativeArray<Color32> source; public NativeArray<Color32> destination; public int width, height, radius; public void Execute(int index) { int y = index / width; int x = index % width; float4 sum = float4.zero; int count = 0; for (int k = -radius; k <= radius; k++) { int py = math.clamp(y + k, 0, height - 1); Color32 c = source[py * width + x]; sum += new float4(c.r, c.g, c.b, c.a); count++; } sum /= count; destination[index] = new Color32((byte)sum.x, (byte)sum.y, (byte)sum.z, (byte)sum.w); } }
struct PixelSortData : System.IComparable<PixelSortData> { public int globalIndex; public Color32 color; public float sortKey; public int CompareTo(PixelSortData other) => other.sortKey.CompareTo(sortKey); }
[BurstCompile] struct CollectSortDataJob : IJob { [ReadOnly] public NativeArray<Color32> colors; public NativeList<PixelSortData> sortList; public int globalOffsetX, globalOffsetY, totalCanvasWidth, jobWidth; public void Execute() { for (int i = 0; i < colors.Length; i++) { if (colors[i].a > 0) { int localY = i / jobWidth; int localX = i % jobWidth; int globalIndex = (localY + globalOffsetY) * totalCanvasWidth + (localX + globalOffsetX); sortList.Add(new PixelSortData { globalIndex = globalIndex, color = colors[i], sortKey = colors[i].a }); } } } }
[BurstCompile] struct SortPixelJob : IJob { public NativeList<PixelSortData> list; public void Execute() => list.Sort(); }