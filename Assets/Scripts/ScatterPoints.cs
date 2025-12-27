using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using Random = UnityEngine.Random;

[RequireComponent(typeof(PolygonCollider2D))]
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

    // --- MEMORY STORAGE ---
    private List<float2> scatterBlobLocations = new List<float2>();
    private List<List<Vector2>> lassoPaths = new List<List<Vector2>>();

    void Awake()
    {
        polyCollider = GetComponent<PolygonCollider2D>();
    }

    /// <summary>
    /// Called by LassoPainter to initialize the board.
    /// Clears all history and generates new random blobs.
    /// </summary>
    public void GenerateAndRenderToTexture(Texture2D targetTexture, int width, int height)
    {
        // 1. Clear History
        scatterBlobLocations.Clear();
        lassoPaths.Clear();

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
        pointArray.Dispose();

        // 4. Generate Initial Collider
        RegenerateAllColliders();
    }

    /// <summary>
    /// Adds a new lasso shape to history and rebuilds the collider.
    /// </summary>
    public void AddLassoCollider(List<Vector2> worldPoints)
    {
        if (worldPoints == null || worldPoints.Count < 3) return;

        // Store a copy of the points
        lassoPaths.Add(new List<Vector2>(worldPoints));

        // Rebuild everything
        RegenerateAllColliders();
    }

    /// <summary>
    /// Deletes the old collider and rebuilds it from the stored blobs and lasso paths.
    /// </summary>
    void RegenerateAllColliders()
    {
        LassoPainter manager = FindObjectOfType<LassoPainter>();
        if (manager == null || manager.outputDisplay == null) return;

        // --- 1. SETUP WORLD SPACE MATH ---
        Vector3[] corners = new Vector3[4];
        manager.outputDisplay.rectTransform.GetWorldCorners(corners);

        Vector3 bottomLeft = corners[0];
        Vector3 rightDir = (corners[3] - corners[0]);
        Vector3 upDir = (corners[1] - corners[0]);

        float worldHeight = upDir.magnitude;
        float worldRadius = pointRadius * worldHeight;

        // --- 2. RESET COLLIDER ---
        polyCollider.pathCount = scatterBlobLocations.Count + lassoPaths.Count;

        // --- 3. REBUILD BLOB PATHS ---
        for (int i = 0; i < scatterBlobLocations.Count; i++)
        {
            float2 p = scatterBlobLocations[i];

            // Map 0-1 to World Space
            Vector3 center = bottomLeft + (rightDir * p.x) + (upDir * p.y);

            Vector2[] circlePath = new Vector2[colliderSegments];
            float angleStep = (2f * Mathf.PI) / colliderSegments;

            for (int j = 0; j < colliderSegments; j++)
            {
                float angle = j * angleStep;
                Vector3 worldVertex = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * worldRadius;
                circlePath[j] = transform.InverseTransformPoint(worldVertex);
            }

            polyCollider.SetPath(i, circlePath);
        }

        // --- 4. REBUILD LASSO PATHS ---
        for (int i = 0; i < lassoPaths.Count; i++)
        {
            List<Vector2> pathWorldPoints = lassoPaths[i];
            Vector2[] localPath = new Vector2[pathWorldPoints.Count];

            for (int j = 0; j < pathWorldPoints.Count; j++)
            {
                localPath[j] = transform.InverseTransformPoint(pathWorldPoints[j]);
            }

            // Offset index by the number of blobs we already added
            polyCollider.SetPath(scatterBlobLocations.Count + i, localPath);
        }
    }
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