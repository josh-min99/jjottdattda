using System.Collections.Generic;
using UnityEngine;

public class HexGridHelper : MonoBehaviour
{
    public static HexGridHelper Instance;

    // 이웃까지의 기본 거리 (타일 간 중심 거리)
    float neighborBaseDist = -1f;

    void Awake()
    {
        Instance = this;
    }

    void EnsureNeighborDist()
    {
        if (neighborBaseDist > 0f) return;

        if (MapGenerator.Instance == null || MapGenerator.Instance.allTiles.Count < 2)
        {
            Debug.LogWarning("[HexGridHelper] 타일이 부족해서 neighborBaseDist를 계산할 수 없습니다.");
            neighborBaseDist = 1f;
            return;
        }

        neighborBaseDist = float.MaxValue;

        // 타일들 사이에서 가장 가까운 거리(0 제외)를 기본 거리로 사용
        var values = new List<HexTile>(MapGenerator.Instance.allTiles.Values);
        for (int i = 0; i < values.Count; i++)
        {
            for (int j = i + 1; j < values.Count; j++)
            {
                float d = Vector2.Distance(
                    values[i].transform.position,
                    values[j].transform.position
                );
                if (d > 0.01f && d < neighborBaseDist)
                    neighborBaseDist = d;
            }
        }

        // 여유 조금 줌
        neighborBaseDist *= 1.2f;
        Debug.Log($"[HexGridHelper] neighborBaseDist = {neighborBaseDist}");
    }

    /// <summary>
    /// 중심 타일과 "가장 가까운 거리" 안에 있는 타일들을 이웃으로 판단
    /// </summary>
    public List<HexTile> GetNeighbors(HexTile center)
    {
        EnsureNeighborDist();
        List<HexTile> result = new List<HexTile>();
        if (center == null || MapGenerator.Instance == null) return result;

        Vector2 cpos = center.transform.position;
        float r = neighborBaseDist;

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile == center) continue;

            float d = Vector2.Distance(cpos, tile.transform.position);
            if (d <= r)
            {
                result.Add(tile);
            }
        }

        return result;
    }

    /// <summary>
    /// range 칸 안의 타일들 (사거리 계산용, 대충 거리로 처리)
    /// </summary>
    public List<HexTile> GetTilesInRange(HexTile center, int range)
    {
        EnsureNeighborDist();
        List<HexTile> result = new List<HexTile>();
        if (center == null || MapGenerator.Instance == null) return result;

        Vector2 cpos = center.transform.position;

        // 한 칸 거리 * range 정도까지
        float maxDist = neighborBaseDist * (range + 0.5f);

        foreach (var tile in MapGenerator.Instance.allTiles.Values)
        {
            if (tile == center) continue;

            float d = Vector2.Distance(cpos, tile.transform.position);
            if (d <= maxDist)
            {
                result.Add(tile);
            }
        }

        return result;
    }
}
