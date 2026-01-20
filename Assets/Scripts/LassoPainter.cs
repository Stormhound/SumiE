using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
    public Color centerColor => GameManager.Instance && GameManager.Instance.CurrentConfig ? GameManager.Instance.CurrentConfig.centerColor : new Color(1, 0, 0, 1f);
    [Range(0, 10)] public int blurPasses = 3;
    public int blurRadius = 2;
    [Range(0.1f, 1f)] public float resolutionScale = 0.5f;
    public Color32 enemyColor => GameManager.Instance && GameManager.Instance.CurrentConfig ? GameManager.Instance.CurrentConfig.enemyColor : new Color32(255, 0, 0, 255);

    [Tooltip("Distance in pixels from the edge where the ink becomes fully opaque.")]
    public float gradientWidth = 24.0f;
    [Tooltip("Controls the falloff curve. 1 = Linear, <1 = Softer, >1 = Sharper")]
    [Range(0.1f, 3.0f)] public float smoothness = 1.0f;

    [Header("Interaction")]
    [Range(0f, 0.5f)] public float smoothTime = 0.05f;
    public float minPointDistance = 0.1f;
    public float closeThreshold = 0.5f;

    [Header("Ink Settings")]
    public float maxInk => GameManager.Instance && GameManager.Instance.CurrentConfig ? GameManager.Instance.CurrentConfig.maxInk : 100f;
    public float inkConsumptionRate => GameManager.Instance && GameManager.Instance.CurrentConfig ? GameManager.Instance.CurrentConfig.inkConsumptionRate : 1.0f;
    [SerializeField] private float currentInk;

    [Header("Animation")]
    [Range(0.1f, 2.0f)] public float fillDuration = 0.5f;
    [Range(0.1f, 5.0f)] public float enemyExpansionDuration = 1.0f;

    // Internal State
    private LineRenderer lr;
    private List<Vector2> worldPoints = new List<Vector2>();
    private Texture2D drawTexture;
    private bool isDrawing = false;
    private bool isCalculating = false;
    private bool hasDrawnThisTurn = false; // NEW: One draw limit
    private Vector2 currentSmoothPos;
    private Vector2 smoothVelocity;

    // Cache
    private int texWidth, texHeight;
    private NativeArray<Color32> backupTextureState;
    private NativeList<PixelSortData> activeSortKeys; // Only persist this for animation
    
    // Enemy State
    public struct EnemySeed
    {
        public int2 pos;
        public float radius;
        public int active; // 1 = true, 0 = false
    }
    private NativeList<EnemySeed> enemySeeds;

    void OnDisable()
    {
        if (activeSortKeys.IsCreated) activeSortKeys.Dispose();
    }

    void OnDestroy() 
    { 
        if (activeSortKeys.IsCreated) activeSortKeys.Dispose();
        if (backupTextureState.IsCreated) backupTextureState.Dispose(); 
        if (drawTexture != null) Destroy(drawTexture);
    }

    void Start()
    {
        lr = GetComponent<LineRenderer>();
        if (cam == null) cam = Camera.main;
        
        // Start full
        currentInk = maxInk;
        
        // Auto-spawn UI
        if (FindFirstObjectByType<GameUI>() == null) gameObject.AddComponent<GameUI>();

        InitializeTexture();
        RefillInk();
    }

    void InitializeTexture()
    {
        Rect r = outputDisplay.rectTransform.rect;
        // Ensure accurate size from Rect, fallback to screen if rect is zero (e.g. layout not ready)
        float baseW = r.width > 0 ? r.width : Screen.width;
        float baseH = r.height > 0 ? r.height : Screen.height;

        texWidth = Mathf.RoundToInt(baseW * resolutionScale);
        texHeight = Mathf.RoundToInt(baseH * resolutionScale);

        // Create the master texture
        drawTexture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        drawTexture.filterMode = FilterMode.Bilinear;

        // 1. Clear it first
        var pixels = drawTexture.GetRawTextureData<Color32>();
        new ClearTextureJob { pixelData = pixels }.Run(pixels.Length);
        drawTexture.Apply();

        // 2. Assign to UI
        if (outputDisplay != null) outputDisplay.texture = drawTexture;

        // 3. Ask ScatterPoints to draw initial blobs
        if (scatterScript != null)
        {
            scatterScript.GenerateAndRenderToTexture(drawTexture, texWidth, texHeight);
        }

        // 4. Calculate initial progress (blobs count as filled area)
        CalculateAndReportProgress();
        
        // 5. Initialize Enemies (managed by GameManager usually, but we can do it here if count is passed, 
        // effectively we expect InitializeEnemies to be called explicitly or we default it)
    }

    public void InitializeEnemies(int count, int startRadius)
    {
        if (enemySeeds.IsCreated) enemySeeds.Dispose();
        enemySeeds = new NativeList<EnemySeed>(count, Allocator.Persistent);

        if (drawTexture == null) return;
        
        Debug.Log($"[LassoPainter] Spawning {count} Enemy Seeds...");

        // Smart Spawn: Don't spawn on top of existing ink (which scatter points made)
        // Actually, ScatterPoints might not have run yet if Start order is racey.
        // Simple random for now.
        
        // Use a job or main thread rng? Main thread is fine for init.
        NativeArray<Color32> pixels = drawTexture.GetRawTextureData<Color32>();
        
        int spawned = 0;
        int attempts = 0;
        while (spawned < count && attempts < count * 10)
        {
            attempts++;
            int x = UnityEngine.Random.Range(eraseMargin, texWidth - eraseMargin);
            int y = UnityEngine.Random.Range(eraseMargin, texHeight - eraseMargin);
            
            // Check if occupied? (Optional, but good game feel)
            // Color32 c = pixels[y * texWidth + x];
            // if (c.a > 0) continue; 
            
            enemySeeds.Add(new EnemySeed { pos = new int2(x, y), radius = startRadius, active = 1 });
            
            // Burn the initial hole
            spawned++;
        }
        
        // Perform initial erase
        var job = new ExpandActiveSeedsJob
        {
            pixelData = pixels,
            seeds = enemySeeds,
            width = texWidth,
            height = texHeight,
            fillColor = enemyColor
        };
        job.Schedule(pixels.Length, 64).Complete();
        drawTexture.Apply();

        // Report
        // GameManager.Instance.UpdateEnemyCount(enemySeeds.Length);
        
        // Update Physics
        ScatterPoints sp = FindFirstObjectByType<ScatterPoints>();
        if (sp) sp.UpdateColliderFromTexture(drawTexture, enemyColor);
    } 
    
    private const int eraseMargin = 20;

    void Update()
    {
        if (isCalculating) return;

        if (Input.GetMouseButtonDown(0)) StartStroke();
        if (Input.GetMouseButton(0) && isDrawing) UpdateStroke();
        if (Input.GetMouseButtonUp(0)) EndStroke();
    }

    void StartStroke()
    {
        // 0. Turn Check
        // 0. Turn Check
        if (GameManager.Instance != null && GameManager.Instance.currentTurn != GameManager.TurnState.Player) return;
        
        // 0.1 One Draw Limit
        if (hasDrawnThisTurn) return;

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
                
                // Update UI
                GameUI ui = FindFirstObjectByType<GameUI>();
                if (ui) ui.UpdateInk(currentInk, maxInk);
            }
            else
            {
                currentInk = 0;
                EndStroke();
                
                // Auto End Turn
                if (GameManager.Instance != null && GameManager.Instance.currentTurn == GameManager.TurnState.Player)
                {
                    GameManager.Instance.EndPlayerTurn();
                }
            }
        }
    }

    void EndStroke()
    {
        if (!isDrawing) return;

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
            Debug.Log($"[LassoPainter] Closing shape! Points: {worldPoints.Count}. Start: {startPoint}, End: {lastPoint}, Release: {releasePos}");
            hasDrawnThisTurn = true; // Mark turn as used
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

        // Resolve UI Camera for coordinate conversion
        Canvas uiCanvas = outputDisplay.canvas;
        Camera uiCam = null;
        if (uiCanvas.renderMode == RenderMode.ScreenSpaceCamera || uiCanvas.renderMode == RenderMode.WorldSpace)
            uiCam = uiCanvas.worldCamera;

        Rect rectBounds = outputDisplay.rectTransform.rect;
        float rX = rectBounds.x;
        float rY = rectBounds.y;
        float rW = rectBounds.width;
        float rH = rectBounds.height;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            // 1. World -> Screen
            Vector2 screenPos = cam.WorldToScreenPoint(worldPoints[i]);
            
            // 2. Screen -> Local UI Space
            Vector2 localPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(outputDisplay.rectTransform, screenPos, uiCam, out localPos);

            // 3. Local -> UV (0..1)
            // LocalPos is relative to Pivot. rectBounds.x/y is the bottom-left offset from Pivot.
            float u = (localPos.x - rX) / rW;
            float v = (localPos.y - rY) / rH;

            // 4. UV -> Texture Pixel
            int x = Mathf.Clamp(Mathf.RoundToInt(u * texWidth), 0, texWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * texHeight), 0, texHeight - 1);

            pointBuffer[i] = new int2(x, y);

            // Debug Bounds
            if (i==0) { minX=x; maxX=x; minY=y; maxY=y; }
            else { minX=Mathf.Min(minX,x); maxX=Mathf.Max(maxX,x); minY=Mathf.Min(minY,y); maxY=Mathf.Max(maxY,y); }
        }

        Debug.Log($"[LassoPainter] Texture: {texWidth}x{texHeight}. Drawn Bounds: X[{minX}-{maxX}], Y[{minY}-{maxY}]. Scale: {resolutionScale}");

        int jobW = texWidth;
        int jobH = texHeight;

        Debug.Log($"[LassoPainter] Scheduling Jobs. Texture: {texWidth}x{texHeight}, Points: {worldPoints.Count}. Scale: {resolutionScale}");


        // Temp Buffers - Dispose immediately after calculation!

        NativeArray<float> distGrid = new NativeArray<float>(jobW * jobH, Allocator.TempJob);
        NativeArray<Color32> finalFillColors = new NativeArray<Color32>(jobW * jobH, Allocator.TempJob);
        // Removed BlurBuffer to prevent expansion loop
        
        // Persistent buffer for animation - cleaned up in OnDisable/OnDestroy/NextRun
        if (activeSortKeys.IsCreated) activeSortKeys.Dispose();
        activeSortKeys = new NativeList<PixelSortData>(jobW * jobH, Allocator.Persistent);

        try 
        {
            // Jobs
            // Seed the grid with the EXISTING distance field recovered from alpha
            // This prevents the expansion loop and preserves smooth gradients
            var maskJob = new MaskFromTextureJob { 
                texturePixels = backupTextureState, 
                outputGrid = distGrid, 
                width = jobW, 
                height = jobH, 
                offsetX = 0, 
                offsetY = 0,
                enemyColor = enemyColor
            };
            JobHandle handle = maskJob.Schedule(jobW * jobH, 64);
            
            // NEW: Rasterize the stroke edges explicitly to ensure the shape outline is 100% accurate
            // This prevents "thin" areas from being skipped by the scanline logic.
            // We use 'gradientWidth' as the radius to ensure the User's Path is the SOLID CORE (d=24), preventing shrinkage.
            var strokeJob = new StrokeRasterJob 
            { 
                points = pointBuffer, 
                outputGrid = distGrid, 
                width = jobW, 
                height = jobH, 
                offsetX = 0, 
                offsetY = 0,
                radius = gradientWidth
            };
            handle = strokeJob.Schedule(pointBuffer.Length, 64, handle);

            var scanlineJob = new ScanlineJob { points = pointBuffer, outputGrid = distGrid, width = jobW, height = jobH, offsetX = 0, offsetY = 0 };
            handle = scanlineJob.Schedule(handle);

            // Removed CapJob - The ScanlineJob handles the closed polygon. 
            // This ensures precise "straight line" closure without blobs.


            handle = new ChamferJobPass1 { grid = distGrid, width = jobW, height = jobH }.Schedule(handle);
            handle = new ChamferJobPass2 { grid = distGrid, width = jobW, height = jobH }.Schedule(handle);

            handle = new GradientJob { grid = distGrid, outputColors = finalFillColors, gradientWidth = gradientWidth, smoothness = smoothness, centerColor = centerColor }.Schedule(jobW * jobH, 64, handle);

            // Removed Blur Loop. GradientJob provides sufficient softness.
            // Extra blur causes the "Recovered Distance" to miscalculate and expand the shape.

            handle = new CollectSortDataJob { colors = finalFillColors, distGrid = distGrid, sortList = activeSortKeys, globalOffsetX = 0, globalOffsetY = 0, totalCanvasWidth = texWidth, jobWidth = jobW }.Schedule(handle);
            handle = new SortPixelJob { list = activeSortKeys }.Schedule(handle);
            
            handle.Complete();
            Debug.Log($"[LassoPainter] Jobs Completed. Total Pixels to Fill: {activeSortKeys.Length}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LassoPainter] Job scheduling failed: {e.Message}\n{e.StackTrace}");
            isCalculating = false;
            // Clean up temps in finally block if we were using one, but here we just ensure we return
            pointBuffer.Dispose();
            distGrid.Dispose();
            finalFillColors.Dispose();
            // blurBuffer.Dispose();
            yield break;
        }

        // DISPOSE TEMPS IMMEDIATELY
        pointBuffer.Dispose();
        distGrid.Dispose();
        finalFillColors.Dispose();

        // Animate
        int totalPixels = activeSortKeys.Length;
        float startTime = Time.time;
        int processedCount = 0;
        NativeArray<Color32> liveTexture = drawTexture.GetRawTextureData<Color32>();

        while (processedCount < totalPixels)
        {
            float t = (Time.time - startTime) / fillDuration;
            int targetCount = Mathf.Clamp(Mathf.FloorToInt(t * totalPixels), 0, totalPixels);

            while (processedCount < targetCount)
            {
                if (!activeSortKeys.IsCreated) break; // Safety check
                PixelSortData data = activeSortKeys[processedCount];
                liveTexture[data.globalIndex] = data.color;
                processedCount++;
            }
            drawTexture.Apply();
            yield return null;
        }

        // Recalculate Colliders
        if (scatterScript != null)
        {
            scatterScript.AddLassoCollider(worldPoints);
        }

        // Calculate and Report Progress to Game Manager
        CalculateAndReportProgress();

        // Cleanup
        if (activeSortKeys.IsCreated) activeSortKeys.Dispose();
        if (backupTextureState.IsCreated) backupTextureState.Dispose();
        
        isCalculating = false;
        
        // 5. Update Physics from Texture
        ScatterPoints sp = FindFirstObjectByType<ScatterPoints>();
        if (sp) sp.UpdateColliderFromTexture(drawTexture, enemyColor);
    }

    public void RefillInk()
    {
        currentInk = maxInk;
        hasDrawnThisTurn = false; // Reset turn limit
        
        GameUI ui = FindFirstObjectByType<GameUI>();
        if (ui) ui.UpdateInk(currentInk, maxInk);
    }

    Vector2 GetMouseWorldPos()
    {
        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = -cam.transform.position.z;
        return cam.ScreenToWorldPoint(mouseScreen);
    }

    // --- PROGRESS CALCULATION HELPER ---
    void CalculateAndReportProgress()
    {
        if (GameManager.Instance == null) return;

        NativeArray<Color32> currentPixels = drawTexture.GetRawTextureData<Color32>();
        NativeReference<int> filledCount = new NativeReference<int>(Allocator.TempJob);
        NativeReference<int> enemyCount = new NativeReference<int>(Allocator.TempJob);

        var countJob = new CountPixelsJob
        {
            pixelData = currentPixels,
            coloredCount = filledCount,
            enemyFilledCount = enemyCount,
            enemyColor = enemyColor
        };

        countJob.Schedule().Complete();

        GameManager.Instance.UpdateGameState(currentPixels.Length, filledCount.Value, enemyCount.Value);
        filledCount.Dispose();
        enemyCount.Dispose();
    }

    public IEnumerator ExpandEnemiesRoutine(float expansionAmount)
    {
        if (drawTexture == null || !enemySeeds.IsCreated || enemySeeds.Length == 0) yield break;

        NativeArray<Color32> pixels = drawTexture.GetRawTextureData<Color32>();

        // 1. CHECK CAPTURES
        var captureJob = new CheckCapturesJob
        {
            pixelData = pixels,
            seeds = enemySeeds.AsArray(),
            width = texWidth,
            enemyColor = enemyColor
        };
        captureJob.Schedule(enemySeeds.Length, 32).Complete();
        
        // 2. SHRINK RADII (Collision with Player Ink)
        // If the enemy overlaps player ink, shrink the radius to the contact point.
        // This ensures they expand "from" the conflict line, rather than jumping over it.
        var shrinkJob = new ShrinkRadiiJob
        {
            pixelData = pixels,
            seeds = enemySeeds.AsArray(),
            width = texWidth,
            height = texHeight,
            enemyColor = enemyColor
        };
        shrinkJob.Schedule(enemySeeds.Length, 4).Complete();


        // Update Count UI & Check Win
        int activeCount = 0;
        for (int i=0; i<enemySeeds.Length; i++) if (enemySeeds[i].active == 1) activeCount++;
        if (activeCount == 0)
        {
            Debug.Log("[LassoPainter] All enemies captured! Filling canvas...");
            yield return StartCoroutine(FillCanvasRoutine(centerColor));
            yield break;
        }

        // 3. ANIMATE EXPANSION
        float startTime = Time.time;
        
        NativeArray<float> startRadii = new NativeArray<float>(enemySeeds.Length, Allocator.Persistent);
        
        try
        {
            for(int i=0; i<enemySeeds.Length; i++) startRadii[i] = enemySeeds[i].radius;

            while (Time.time < startTime + enemyExpansionDuration)
            {
                float t = (Time.time - startTime) / enemyExpansionDuration;
                float smoothT = 1f - (1f - t) * (1f - t); // EaseOut

                for (int i=0; i<enemySeeds.Length; i++)
                {
                    if (enemySeeds[i].active == 1)
                    {
                        EnemySeed s = enemySeeds[i];
                        s.radius = startRadii[i] + (expansionAmount * smoothT);
                        enemySeeds[i] = s;
                    }
                }

                var job = new ExpandActiveSeedsJob
                {
                    pixelData = pixels,
                    seeds = enemySeeds,
                    width = texWidth,
                    height = texHeight,
                    fillColor = enemyColor
                };
                
                job.Schedule(pixels.Length, 64).Complete();
                drawTexture.Apply();
                yield return null;
            }
            
            // Ensure final value set
            for (int i=0; i<enemySeeds.Length; i++)
            {
                if (enemySeeds[i].active == 1)
                {
                    EnemySeed s = enemySeeds[i];
                    s.radius = startRadii[i] + expansionAmount;
                    enemySeeds[i] = s;
                }
            }
            
            // Final Pass
            new ExpandActiveSeedsJob
            {
                pixelData = pixels,
                seeds = enemySeeds,
                width = texWidth,
                height = texHeight,
                fillColor = enemyColor
            }.Schedule(pixels.Length, 64).Complete();

            drawTexture.Apply();
        }
        finally
        {
            if (startRadii.IsCreated) startRadii.Dispose();
        }

        // 4. Update Physics & Game State
        ScatterPoints sp = FindFirstObjectByType<ScatterPoints>();
        if (sp) sp.UpdateColliderFromTexture(drawTexture, enemyColor);

        CalculateAndReportProgress();
    }

    public IEnumerator FillCanvasRoutine(Color32 fillTargetColor)
    {
        if (drawTexture == null) yield break;

        NativeArray<Color32> pixels = drawTexture.GetRawTextureData<Color32>();
        
        // 1. Prepare Sort Keys
        if (!activeSortKeys.IsCreated) activeSortKeys = new NativeList<PixelSortData>(texWidth * texHeight, Allocator.Persistent);
        activeSortKeys.Clear();

        // 2. Collect Empty Pixels
        var collectJob = new CollectEmptyPixelsJob
        {
            pixelData = pixels,
            sortList = activeSortKeys,
            targetColor = fillTargetColor,
            seed = (uint)UnityEngine.Random.Range(1, 100000)
        };
        collectJob.Schedule().Complete();
        
        // 3. Sort (Randomize order)
        new SortPixelJob { list = activeSortKeys }.Schedule().Complete();

        // 4. Animate Fill
        int totalToFill = activeSortKeys.Length;
        float startTime = Time.time;
        int processedCount = 0;
        
        while (processedCount < totalToFill)
        {
            float t = (Time.time - startTime) / fillDuration; // Reuse standard fill speed
            int targetCount = Mathf.Clamp(Mathf.FloorToInt(t * totalToFill), 0, totalToFill);

            while (processedCount < targetCount)
            {
                if (!activeSortKeys.IsCreated) yield break; // Safety exit
                PixelSortData data = activeSortKeys[processedCount];
                pixels[data.globalIndex] = data.color;
                processedCount++;
            }
            drawTexture.Apply();
            yield return null;
        }

        // Update Physics (Full box)
        ScatterPoints sp = FindFirstObjectByType<ScatterPoints>();
        if (sp) sp.UpdateColliderFromTexture(drawTexture, enemyColor);
        
        CalculateAndReportProgress();
    }
}

// -----------------------------------------------------------------------------
// BURST JOBS
// -----------------------------------------------------------------------------



// [BurstCompile] struct FillTextureJob ... (removed)


[BurstCompile]
struct CollectEmptyPixelsJob : IJob
{
    [ReadOnly] public NativeArray<Color32> pixelData;
    public NativeList<PixelSortData> sortList;
    public Color32 targetColor;
    public uint seed;

    public void Execute()
    {
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(seed);
        for (int i = 0; i < pixelData.Length; i++)
        {
            Color32 c = pixelData[i];
            
            // Generic Logic: Overwrite anything that is NOT the target color
            // This works for "Win" (Target=Player, overwrites Enemy & Empty)
            // And for "Lose" (Target=Enemy, overwrites Player & Empty)
            // Assuming Target has alpha=255.
            
            bool isTarget = (c.r == targetColor.r && c.g == targetColor.g && c.b == targetColor.b && c.a == 255);
            
            if (!isTarget)
            {
                sortList.Add(new PixelSortData 
                { 
                    globalIndex = i, 
                    color = targetColor, 
                    sortKey = rng.NextFloat() 
                });
            }
        }
    }
}

[BurstCompile]
struct CheckCapturesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<Color32> pixelData;
    public NativeArray<LassoPainter.EnemySeed> seeds;
    public int width;
    public Color32 enemyColor;

    public void Execute(int index)
    {
        // Check if the CENTER of the seed is covered by ink
        LassoPainter.EnemySeed s = seeds[index];
        if (s.active == 0) return;

        int idx = s.pos.y * width + s.pos.x;
        if (idx >= 0 && idx < pixelData.Length)
        {
            Color32 c = pixelData[idx];
            bool isEnemy = (c.r == enemyColor.r && c.g == enemyColor.g && c.b == enemyColor.b);

            // Captured if ink present AND not enemy ink
            if (c.a > 0 && !isEnemy)
            {
                // Captured!
                s.active = 0;
                seeds[index] = s;
            }
        }
    }
}

[BurstCompile]
struct ShrinkRadiiJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<Color32> pixelData;
    public NativeArray<LassoPainter.EnemySeed> seeds;
    public int width, height;
    public Color32 enemyColor;

    public void Execute(int index)
    {
        var s = seeds[index];
        if (s.active == 0) return;

        float validRadius = s.radius;
        int r = (int)Mathf.Ceil(s.radius);
        int2 center = s.pos;
        
        // Scan bounding box
        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                int px = center.x + x;
                int py = center.y + y;
                
                if (px < 0 || px >= width || py < 0 || py >= height) continue;
                
                int pIdx = py * width + px;
                Color32 c = pixelData[pIdx];
                
                // Is this player ink?
                bool isEnemy = (c.r == enemyColor.r && c.g == enemyColor.g && c.b == enemyColor.b);
                if (c.a > 0 && !isEnemy)
                {
                    // Collision found. Check distance.
                    float dist = Mathf.Sqrt(x*x + y*y);
                    if (dist < validRadius)
                    {
                        validRadius = dist;
                    }
                }
            }
        }
        
        s.radius = validRadius;
        seeds[index] = s;
    }
}

[BurstCompile]
struct ExpandActiveSeedsJob : IJobParallelFor
{
    public NativeArray<Color32> pixelData;
    [ReadOnly] public NativeList<LassoPainter.EnemySeed> seeds;
    public int width, height;
    public Color32 fillColor;

    public void Execute(int index)
    {
        int y = index / width; 
        int x = index % width;
        
        for (int i = 0; i < seeds.Length; i++)
        {
            var s = seeds[i];
            if (s.active == 0) continue; // Skip captured enemies

            float dx = x - s.pos.x;
            float dy = y - s.pos.y;
            
            if (dx * dx + dy * dy <= s.radius * s.radius)
            {
                pixelData[index] = fillColor;
                return; 
            }
        }
    }
}

[BurstCompile] struct ClearTextureJob : IJobParallelFor { public NativeArray<Color32> pixelData; public void Execute(int index) => pixelData[index] = new Color32(0, 0, 0, 0); }

[BurstCompile] 
struct MaskFromTextureJob : IJobParallelFor 
{ 
    [ReadOnly] public NativeArray<Color32> texturePixels; 
    public NativeArray<float> outputGrid; 
    public int width, height, offsetX, offsetY;
    public Color32 enemyColor;

    public void Execute(int index) 
    { 
        int y = index / width; 
        int x = index % width; 
        int globalIndex = (y + offsetY) * width + (x + offsetX); 
        
        if (globalIndex >= 0 && globalIndex < texturePixels.Length) 
        {
            Color32 c = texturePixels[globalIndex];
            bool isEnemy = (c.r == enemyColor.r && c.g == enemyColor.g && c.b == enemyColor.b);

            // Binary classification: 0 = Outside (Source), 99999 = Inside (Target for Distance)
            // If it's enemy (Red), it's effectively Outside/Empty for our strokes
            if (c.a > 0 && !isEnemy)
            {
                outputGrid[index] = 99999f;
            }
            else
            {
                outputGrid[index] = 0f;
            }
        }
        else 
        {
            outputGrid[index] = 0f;
        }
    } 
}
[BurstCompile] struct ScanlineJob : IJob { [ReadOnly] public NativeArray<int2> points; public NativeArray<float> outputGrid; public int width, height, offsetX, offsetY; public void Execute() { for (int y = 0; y < height; y++) { int globalY = y + offsetY; NativeList<int> nodes = new NativeList<int>(32, Allocator.Temp); int j = points.Length - 1; for (int i = 0; i < points.Length; i++) { int2 pi = points[i]; int2 pj = points[j]; if ((pi.y < globalY && pj.y >= globalY) || (pj.y < globalY && pi.y >= globalY)) { float intersect = pi.x + (float)(globalY - pi.y) / (pj.y - pi.y) * (pj.x - pi.x); nodes.Add(Mathf.RoundToInt(intersect)); } j = i; } nodes.Sort(); for (int k = 0; k < nodes.Length; k += 2) { if (k + 1 >= nodes.Length) break; int startX = Mathf.Clamp(nodes[k] - offsetX, 0, width - 1); int endX = Mathf.Clamp(nodes[k + 1] - offsetX, 0, width - 1); for (int x = startX; x < endX; x++) outputGrid[y * width + x] = -1f; } nodes.Dispose(); } } }
[BurstCompile] struct ChamferJobPass1 : IJob { public NativeArray<float> grid; public int width, height; public void Execute() { for (int i = 0; i < grid.Length; i++) if (grid[i] < 0) grid[i] = 99999f; for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { int idx = y * width + x; if (grid[idx] > 0) { float minVal = 99999f; if (x > 0) minVal = math.min(minVal, grid[y * width + (x - 1)] + 1.0f); if (y > 0) minVal = math.min(minVal, grid[(y - 1) * width + x] + 1.0f); if (x > 0 && y > 0) minVal = math.min(minVal, grid[(y - 1) * width + (x - 1)] + 1.414f); if (x < width - 1 && y > 0) minVal = math.min(minVal, grid[(y - 1) * width + (x + 1)] + 1.414f); if (minVal < grid[idx]) grid[idx] = minVal; } } } } }
[BurstCompile] struct ChamferJobPass2 : IJob { public NativeArray<float> grid; public int width, height; public void Execute() { for (int y = height - 1; y >= 0; y--) { for (int x = width - 1; x >= 0; x--) { int idx = y * width + x; if (grid[idx] > 0) { float minVal = grid[idx]; if (x < width - 1) minVal = math.min(minVal, grid[y * width + (x + 1)] + 1.0f); if (y < height - 1) minVal = math.min(minVal, grid[(y + 1) * width + x] + 1.0f); if (x < width - 1 && y < height - 1) minVal = math.min(minVal, grid[(y + 1) * width + (x + 1)] + 1.414f); if (x > 0 && y < height - 1) minVal = math.min(minVal, grid[(y + 1) * width + (x - 1)] + 1.414f); grid[idx] = minVal; } } } } }
[BurstCompile] struct GradientJob : IJobParallelFor { [ReadOnly] public NativeArray<float> grid; public NativeArray<Color32> outputColors; public float gradientWidth; public float smoothness; public Color32 centerColor; public void Execute(int index) { float d = grid[index]; if (d <= 0) { outputColors[index] = new Color32(0, 0, 0, 0); return; } float normalized = math.clamp(d / gradientWidth, 0f, 1f); float alpha = math.pow(normalized, smoothness); outputColors[index] = new Color32(centerColor.r, centerColor.g, centerColor.b, (byte)(alpha * 255)); } }
[BurstCompile] struct BlurHorzJob : IJobParallelFor { [ReadOnly] public NativeArray<Color32> source; public NativeArray<Color32> destination; public int width, height, radius; public void Execute(int index) { int y = index / width; int x = index % width; float4 sum = float4.zero; int count = 0; for (int k = -radius; k <= radius; k++) { int px = math.clamp(x + k, 0, width - 1); Color32 c = source[y * width + px]; sum += new float4(c.r, c.g, c.b, c.a); count++; } sum /= count; destination[index] = new Color32((byte)sum.x, (byte)sum.y, (byte)sum.z, (byte)sum.w); } }
[BurstCompile] struct BlurVertJob : IJobParallelFor { [ReadOnly] public NativeArray<Color32> source; public NativeArray<Color32> destination; public int width, height, radius; public void Execute(int index) { int y = index / width; int x = index % width; float4 sum = float4.zero; int count = 0; for (int k = -radius; k <= radius; k++) { int py = math.clamp(y + k, 0, height - 1); Color32 c = source[py * width + x]; sum += new float4(c.r, c.g, c.b, c.a); count++; } sum /= count; destination[index] = new Color32((byte)sum.x, (byte)sum.y, (byte)sum.z, (byte)sum.w); } }
struct PixelSortData : System.IComparable<PixelSortData> { public int globalIndex; public Color32 color; public float sortKey; public int CompareTo(PixelSortData other) => other.sortKey.CompareTo(sortKey); }
[BurstCompile] struct CollectSortDataJob : IJob { [ReadOnly] public NativeArray<Color32> colors; [ReadOnly] public NativeArray<float> distGrid; public NativeList<PixelSortData> sortList; public int globalOffsetX, globalOffsetY, totalCanvasWidth, jobWidth; public void Execute() { for (int i = 0; i < colors.Length; i++) { if (colors[i].a > 0) { int localY = i / jobWidth; int localX = i % jobWidth; int globalIndex = (localY + globalOffsetY) * totalCanvasWidth + (localX + globalOffsetX); sortList.Add(new PixelSortData { globalIndex = globalIndex, color = colors[i], sortKey = distGrid[i] }); } } } }
[BurstCompile] struct SortPixelJob : IJob { public NativeList<PixelSortData> list; public void Execute() => list.Sort(); }



// --- NEW JOB: COUNT PIXELS ---
[BurstCompile]
struct CountPixelsJob : IJob
{
    [ReadOnly] public NativeArray<Color32> pixelData;
    public NativeReference<int> coloredCount;
    public NativeReference<int> enemyFilledCount;
    public Color32 enemyColor;

    public void Execute()
    {
        int pCount = 0;
        int eCount = 0;
        for (int i = 0; i < pixelData.Length; i++)
        {
            Color32 c = pixelData[i];
            bool isEnemy = (c.r == enemyColor.r && c.g == enemyColor.g && c.b == enemyColor.b);
            
            // Count PLAYER ink
            if (c.a > 0 && !isEnemy) pCount++;
            
            // Count ENEMY ink (Assuming enemy ink is always fully opaque or just by color match)
            if (isEnemy) eCount++;
        }
        coloredCount.Value = pCount;
        enemyFilledCount.Value = eCount;
    }
}



[BurstCompile]
struct StrokeRasterJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int2> points;
    [NativeDisableParallelForRestriction]
    public NativeArray<float> outputGrid;
    public int width, height, offsetX, offsetY;
    public float radius;

    public void Execute(int index)
    {
        int2 center = points[index];
        int globalX = center.x; // Point buffer is already local to texture? No, PointBuffer is Texture Space (0..W)
        int globalY = center.y;
        
        // We need to write to OutputGrid which is 0..W*H.
        // But our "Draw" logic needs to iterate pixels around the center.
        
        int r = (int)math.ceil(radius);
        int r2 = r * r;

        int startX = math.max(globalX - r, offsetX);
        int endX = math.min(globalX + r, offsetX + width - 1);
        int startY = math.max(globalY - r, offsetY);
        int endY = math.min(globalY + r, offsetY + height - 1);

        // Optimization: The job is ParallelFor over Points.
        // Multiple points might write to the same pixel. 
        // We writing -1f (benign race condition, idempotent).
        
        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                int dx = x - globalX;
                int dy = y - globalY;
                if (dx * dx + dy * dy <= r2)
                {
                    int gridIdx = (y - offsetY) * width + (x - offsetX);
                    if (gridIdx >= 0 && gridIdx < outputGrid.Length)
                    {
                        outputGrid[gridIdx] = -1f;
                    }
                }
            }
        }
    }
}