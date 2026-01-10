using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class ScatterPoints : MonoBehaviour
{
    [Header("Settings")]
    public int pointCount = 20;
    [Tooltip("Minimum distance between points (0-1 space)")]
    public float minDistance = 0.2f;
    [Tooltip("Radius of each blob (0-1 space)")]
    public float pointRadius = 0.15f;
    [Tooltip("How detailed the collider circles are (vertices per circle)")]
    public int colliderSegments = 16;

    [Header("Border Settings")]
    [Range(0f, 0.2f)] public float edgePadding = 0.01f;

    [Header("Visuals")]
    public Color blobColor = Color.red;

    private PolygonCollider2D polyCollider;
    private CompositeCollider2D compositeCollider;
    private Rigidbody2D rb;

    // --- MEMORY STORAGE ---
    private List<float2> scatterBlobLocations = new List<float2>();
    // lassoPaths and eraserPaths are no longer used for collider generation,
    // as we now generate directly from texture.
    
    void Awake()
    {
        // Ensure components exist silently
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();

        compositeCollider = GetComponent<CompositeCollider2D>();
        if (compositeCollider == null) compositeCollider = gameObject.AddComponent<CompositeCollider2D>();

        polyCollider = GetComponent<PolygonCollider2D>();
        if (polyCollider == null) polyCollider = gameObject.AddComponent<PolygonCollider2D>();

        // Configure for Composite
        rb.bodyType = RigidbodyType2D.Static;
        compositeCollider.geometryType = CompositeCollider2D.GeometryType.Polygons;
        polyCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
    }

    void Reset()
    {
        if (GetComponent<Rigidbody2D>() == null) gameObject.AddComponent<Rigidbody2D>();
        if (GetComponent<CompositeCollider2D>() == null) gameObject.AddComponent<CompositeCollider2D>();
        if (GetComponent<PolygonCollider2D>() == null) gameObject.AddComponent<PolygonCollider2D>();
    }

    /// <summary>
    /// Called by LassoPainter to initialize the board.
    /// Clears all history and generates new random blobs.
    /// </summary>
    public void GenerateAndRenderToTexture(Texture2D targetTexture, int width, int height)
    {
        // 1. Clear History
        // 1. Clear History
        scatterBlobLocations.Clear();

        // 2. Generate Blobs (CPU)
        int maxAttempts = 10000;
        int attempts = 0;

        float totalSafeMargin = pointRadius + edgePadding;
        float minPos = totalSafeMargin;
        float maxPos = 1.0f - totalSafeMargin;
        if (minPos >= maxPos) { minPos = 0.5f; maxPos = 0.5f; }

        while (scatterBlobLocations.Count < pointCount && attempts < maxAttempts)
        {
            attempts++;
            float2 candidate = new float2(Random.Range(minPos, maxPos), Random.Range(minPos, maxPos));

            bool valid = true;
            for (int i = 0; i < scatterBlobLocations.Count; i++)
            {
                if (math.distance(candidate, scatterBlobLocations[i]) < minDistance)
                {
                    valid = false;
                    break;
                }
            }

            if (valid) scatterBlobLocations.Add(candidate);
        }

        // 3. Render Visuals (Burst)
        NativeArray<float2> pointArray = new NativeArray<float2>(scatterBlobLocations.ToArray(), Allocator.TempJob);
        NativeArray<Color32> textureData = targetTexture.GetRawTextureData<Color32>();

        try
        {
            var job = new SplatterRenderJob
            {
                points = pointArray,
                output = textureData,
                width = width,
                height = height,
                radius = pointRadius,
                color = blobColor
            };

            job.Schedule(textureData.Length, 64).Complete();
            targetTexture.Apply();
        }
        finally
        {
            if (pointArray.IsCreated) pointArray.Dispose();
        }

        // 4. Generate Initial Collider
        UpdateColliderFromTexture(targetTexture);
    }

    /// <summary>
    /// NOT USED ANYMORE - Legacy Method
    /// </summary>
    public void AddLassoCollider(List<Vector2> worldPoints) { }

    /// <summary>
    /// NOT USED ANYMORE - Legacy Method
    /// </summary>
    public void AddEraserCircle(Vector2 worldCenter, float worldRadius) { }

    /// <summary>
    /// Update Collider directly from the texture.
    /// This ensures visual and physics are 1:1, handling holes/erasers automatically.
    /// </summary>
    public void UpdateColliderFromTexture(Texture2D texture)
    {
        if (texture == null) return;
        
        // Create a sprite from the texture to leverage Unity's internal physics shape generator
        Rect rect = new Rect(0, 0, texture.width, texture.height);
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        
        // NOTE: Sprite.Create might be heavy, but it's robust for 'Holes'.
        // We set pixelsPerUnit to match the world sizing.
        // But wait, the texWidth/Height mapped to World Space...
        // LassoPainter: texWidth = ScreenWidth * ResolutionScale
        // outputDisplay (RawImage) covers the screen or rect.
        
        // We need to map the Sprite Physics path back to local space of this object.
        // Let's create an arbitrary sprite, get paths, and scale them.
        
        Sprite tempSprite = Sprite.Create(texture, rect, pivot, 100.0f, 0, SpriteMeshType.FullRect, Vector4.zero, true);
        
        // Verify we got shapes
        int shapeCount = tempSprite.GetPhysicsShapeCount();
        polyCollider.pathCount = shapeCount;
        
        List<Vector2> pathPoints = new List<Vector2>();
        
        // We need to map [0, width] -> World Space -> Local Space
        LassoPainter manager = FindFirstObjectByType<LassoPainter>();
        if (manager == null || manager.outputDisplay == null) return;
        
        Vector3[] corners = new Vector3[4];
        manager.outputDisplay.rectTransform.GetWorldCorners(corners);
        Vector3 bl = corners[0];
        Vector3 tr = corners[2];
        Vector3 size = tr - bl;
        
        for(int i=0; i<shapeCount; i++)
        {
            pathPoints.Clear();
            tempSprite.GetPhysicsShape(i, pathPoints);
            
            // Transform Points
            // Sprite points are in "Units" relative to pivot?
            // Sprite.Create pivot is (0.5, 0.5).
            // But pixelsPerUnit is 100.
            
            // Let's do raw mapping:
            // Point (x,y) from GetPhysicsShape corresponds to Local Sprite Space.
            // Bounds of Sprite are derived from rect/PPU.
            
            // Actually, simpler:
            // Normalize the point to 0..1 based on Texture Size/PPU.
            // Then map 0..1 to World Size.
            // Map World to Local.
            
            // Sprite bounds:
            // width = tex.width / 100
            // height = tex.height / 100
            // pivot at center.
            
            float spriteW = texture.width / 100f;
            float spriteH = texture.height / 100f;
            
            for(int j=0; j<pathPoints.Count; j++)
            {
                Vector2 p = pathPoints[j];
                // P is relative to center.
                // UV:
                float u = (p.x + (spriteW * 0.5f)) / spriteW;
                float v = (p.y + (spriteH * 0.5f)) / spriteH;
                
                // World
                Vector3 worldPos = bl + new Vector3(size.x * u, size.y * v, 0);
                pathPoints[j] = transform.InverseTransformPoint(worldPos);
            }
            
            polyCollider.SetPath(i, pathPoints);
        }
        
        // Cleanup
        Destroy(tempSprite);
    }
    
    // Stub for old method - can delete later
    void RegenerateAllColliders() { }
}

// -----------------------------------------------------------------------------
// BURST JOBS (Unchanged)
// -----------------------------------------------------------------------------

[BurstCompile]
struct SplatterRenderJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float2> points;
    public NativeArray<Color32> output;
    public int width, height;
    public float radius;
    public Color32 color;

    public void Execute(int index)
    {
        int y = index / width;
        int x = index % width;
        float2 uv = new float2((float)x / width, (float)y / height);
        float aspect = (float)width / height;
        uv.x *= aspect;

        float maxAlpha = 0f;

        for (int i = 0; i < points.Length; i++)
        {
            float2 p = points[i];
            p.x *= aspect;
            float dist = math.distance(uv, p);

            if (dist < radius)
            {
                float normalized = dist / radius;
                float falloff = 1.0f - (normalized * normalized * (3.0f - 2.0f * normalized));
                maxAlpha = math.max(maxAlpha, falloff);
            }
        }

        Color32 existing = output[index];
        byte newAlpha = (byte)(maxAlpha * 255);
        if (newAlpha > existing.a)
        {
            output[index] = new Color32(color.r, color.g, color.b, newAlpha);
        }
    }
}