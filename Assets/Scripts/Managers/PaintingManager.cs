using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PaintingManager : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Vector2 domainBounds;
    [SerializeField] private float minDistBetweenPoints;
    [SerializeField] private int safeIterationLimit = 30;
    [SerializeField] private bool DEBUG_SPAWNPOINT = false;

    private List<Vector2> randomPoints = new List<Vector2>();

    private int pointCount = 3;

    void Start()
    {
        CalcRandomPointInPainting();
    }

    private void Update()
    {
        if (DEBUG_SPAWNPOINT)
        {
            DEBUG_SPAWNPOINT = false;

            CalcRandomPointInPainting();
        }
    }

    private void CalcRandomPointInPainting()
    {
        randomPoints.Clear();

        Vector2 domainHalf = domainBounds * 0.5f;

        Vector2 point = new Vector2(
            Random.Range(-domainHalf.x, domainHalf.x), 
            Random.Range(-domainHalf.y, domainHalf.y));

        for (int i = 0; i < pointCount; i++)
        {
            if (randomPoints.Count > 0)
            {
                int count = randomPoints.Where(x => Vector2.Distance(x, point) < minDistBetweenPoints).Count();
                int iterationCount = 0;

                do
                {
                    if (safeIterationLimit == iterationCount) { Debug.LogWarning("Safe limit has been reached, breaking the loop"); break; }

                    point = new Vector2(
                        Random.Range(-domainHalf.x, domainHalf.x),
                        Random.Range(-domainHalf.y, domainHalf.y));

                    count = randomPoints.Where(x => Vector2.Distance(x, point) < minDistBetweenPoints).Count();

                    iterationCount++;
                }
                while (count > 0);

                if (count == 0) randomPoints.Add(point);
            }
            else { randomPoints.Add(point); }
        }

        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        Vector2 randPosition1 = randomPoints.Count > 0 ? randomPoints[0] : Vector2.positiveInfinity;
        Vector2 randPosition2 = randomPoints.Count > 1 ? randomPoints[1] : Vector2.positiveInfinity;
        Vector2 randPosition3 = randomPoints.Count > 2 ? randomPoints[2] : Vector2.positiveInfinity;
        Vector2 randPosition4 = randomPoints.Count > 3 ? randomPoints[3] : Vector2.positiveInfinity;

        Vector4 position1 = new Vector4(randPosition1.x, randPosition1.y, randPosition2.x, randPosition2.y);
        Vector4 position2 = new Vector4(randPosition3.x, randPosition3.y, randPosition4.x, randPosition4.y);

        Debug.Log($"Generated Locations : {position1.x}:{position1.y}\n{position1.z}:{position1.w}\n{position2.x}:{position2.y}\n{position2.z}:{position2.w}\n");

        propertyBlock.SetVector("_Position1", position1);
        propertyBlock.SetVector("_Position2", position2);

        sr.SetPropertyBlock(propertyBlock);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;

        Gizmos.DrawWireCube(transform.position, domainBounds);

        if (randomPoints.Count > 0)
        {
            Gizmos.color = Color.red;

            for (int i = 0; i < randomPoints.Count; i++)
            {
                Gizmos.DrawWireSphere(randomPoints[i], 0.15f);
            }
        }
    }
}
