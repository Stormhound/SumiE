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
    public float minDistance = 0.2f;
    public float pointRadius = 0.15f;
    public int colliderSegments = 16;
    [Range(0f, 0.2f)] public float edgePadding = 0.01f;
    public Color blobColor = Color.red;

    private PolygonCollider2D polyCollider;

    void Awake()
    {
        polyCollider = GetComponent<PolygonCollider2D>();
    }

    public void GenerateAndRenderToTexture(Texture2D targetTexture, int width, int height)
    {
        List<float2> points = new List<float2>();
        int maxAttempts = 10000;
        int attempts = 0;

        float totalSafeMargin = pointRadius + edgePadding;
        float minPos = totalSafeMargin;
        float maxPos = 1.0f - totalSafeMargin;
        if (minPos >= maxPos) { minPos = 0.5f; maxPos = 0.5f; }

        while (points.Count < pointCount && attempts < maxAttempts)
        {
            attempts++;
            float2 candidate = new float2(Random.Range(minPos, maxPos), Random.Range(minPos, maxPos));

            bool valid = true;
            for (int i = 0; i < points.Count; i++)
            {
                if (math.distance(candidate, points[i]) < minDistance) { valid = false; break; }
            }
            if (valid) points.Add(candidate);
        }

        NativeArray<float2> pointArray = new NativeArray<float2>(points.ToArray(), Allocator.TempJob);
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

        GenerateColliders(points);

        pointArray.Dispose();
    }

    void GenerateColliders(List<float2> points)
    {
        LassoPainter manager = FindObjectOfType<LassoPainter>();
        if (manager == null || manager.outputDisplay == null) return;

        Vector3[] corners = new Vector3[4];
        manager.outputDisplay.rectTransform.GetWorldCorners(corners);

        Vector3 bottomLeft = corners[0];
        Vector3 rightDir = (corners[3] - corners[0]);
        Vector3 upDir = (corners[1] - corners[0]);

        float worldHeight = upDir.magnitude;
        float worldRadius = pointRadius * worldHeight;

        polyCollider.pathCount = points.Count;

        for (int i = 0; i < points.Count; i++)
        {
            float2 p = points[i];
            Vector3 center = bottomLeft + (rightDir * p.x) + (upDir * p.y);
            Vector3 localCenter = transform.InverseTransformPoint(center);

            Vector2[] circlePath = new Vector2[colliderSegments];
            float angleStep = (2f * Mathf.PI) / colliderSegments;

            for (int j = 0; j < colliderSegments; j++)
            {
                float angle = j * angleStep;
                float x = Mathf.Cos(angle) * worldRadius;
                float y = Mathf.Sin(angle) * worldRadius;
                circlePath[j] = new Vector2(localCenter.x + x, localCenter.y + y);
            }
            polyCollider.SetPath(i, circlePath);
        }
    }

    // --- NEW METHOD FOR REGENERATING COLLIDER ---
    public void AddLassoCollider(List<Vector2> worldPoints)
    {
        if (polyCollider == null || worldPoints == null || worldPoints.Count < 3) return;

        Vector2[] localPoints = new Vector2[worldPoints.Count];
        for (int i = 0; i < worldPoints.Count; i++)
        {
            localPoints[i] = transform.InverseTransformPoint(worldPoints[i]);
        }

        int newPathIndex = polyCollider.pathCount;
        polyCollider.pathCount++;
        polyCollider.SetPath(newPathIndex, localPoints);
    }
}

// -----------------------------------------------------------------------------
// BURST JOBS (Same as before)
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