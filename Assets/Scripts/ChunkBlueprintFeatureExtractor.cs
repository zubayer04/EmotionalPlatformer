using System;
using UnityEngine;
using UnityEngine.Tilemaps;

[Serializable]
public class ChunkBlueprintFeatures
{
    public int width;
    public int height;
    public int solidCount;
    public int hazardCount;
    public int gapCount;
    public int maxGapWidth;
    public int minLandingWidth;
    public Vector2 estimatedExitDelta;

    public string ToSummary()
    {
        return $"size={width}x{height}, gaps={gapCount}, maxGap={maxGapWidth}, minLanding={minLandingWidth}, solids={solidCount}, hazards={hazardCount}, exitDelta=({estimatedExitDelta.x:0.##},{estimatedExitDelta.y:0.##})";
    }
}

public static class ChunkBlueprintFeatureExtractor
{
    public static ChunkBlueprintFeatures Analyze(ChunkBlueprint blueprint)
    {
        ChunkBlueprintFeatures features = new ChunkBlueprintFeatures();
        if (blueprint == null)
            return features;

        features.width = blueprint.width;
        features.height = blueprint.height;
        features.estimatedExitDelta = EstimateExitDelta(blueprint);

        if (blueprint.rows == null || blueprint.rows.Count == 0)
            return features;

        for (int y = 0; y < blueprint.rows.Count; y++)
        {
            string row = blueprint.rows[y];
            if (string.IsNullOrEmpty(row))
                continue;

            for (int x = 0; x < row.Length; x++)
            {
                char cell = row[x];
                if (cell == '#' || cell == 'B' || cell == 'P')
                    features.solidCount++;
                else if (cell == 'S' || cell == 'M')
                    features.hazardCount++;
            }
        }

        AnalyzeTraversalRow(blueprint, features);
        return features;
    }

    public static string RowsToInlineText(ChunkBlueprint blueprint)
    {
        if (blueprint == null || blueprint.rows == null || blueprint.rows.Count == 0)
            return string.Empty;

        return string.Join(" / ", blueprint.rows);
    }

    public static int EstimateSourceMaxGapWidth(GameObject sourceChunkPrefab)
    {
        if (sourceChunkPrefab == null)
            return -1;

        Tilemap tilemap = sourceChunkPrefab.GetComponentInChildren<Tilemap>();
        if (tilemap == null)
            return -1;

        BoundsInt bounds = tilemap.cellBounds;
        int bestRow = bounds.yMin;
        int bestSolidCount = -1;

        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            int solidCount = 0;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                if (tilemap.HasTile(new Vector3Int(x, y, 0)))
                    solidCount++;
            }

            if (solidCount > bestSolidCount)
            {
                bestSolidCount = solidCount;
                bestRow = y;
            }
        }

        int firstSolidX = int.MaxValue;
        int lastSolidX = int.MinValue;

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            if (tilemap.HasTile(new Vector3Int(x, bestRow, 0)))
            {
                firstSolidX = Mathf.Min(firstSolidX, x);
                lastSolidX = Mathf.Max(lastSolidX, x);
            }
        }

        if (firstSolidX == int.MaxValue || lastSolidX <= firstSolidX)
            return -1;

        int maxGapWidth = 0;
        int currentGapWidth = 0;

        for (int x = firstSolidX; x <= lastSolidX; x++)
        {
            if (tilemap.HasTile(new Vector3Int(x, bestRow, 0)))
            {
                currentGapWidth = 0;
                continue;
            }

            currentGapWidth++;
            maxGapWidth = Mathf.Max(maxGapWidth, currentGapWidth);
        }

        return maxGapWidth > 0 ? maxGapWidth : -1;
    }

    private static void AnalyzeTraversalRow(ChunkBlueprint blueprint, ChunkBlueprintFeatures features)
    {
        if (blueprint.width <= 0 || blueprint.rows == null || blueprint.rows.Count == 0)
            return;

        int currentGap = 0;
        int currentLanding = 0;
        features.minLandingWidth = int.MaxValue;

        for (int x = 0; x < blueprint.width; x++)
        {
            bool occupiedColumn = false;
            bool supportColumn = false;

            for (int y = 0; y < blueprint.rows.Count; y++)
            {
                char cell = GetCell(blueprint, x, y);
                if (cell == '#' || cell == 'B' || cell == 'P' || cell == 'E' || cell == 'X')
                    occupiedColumn = true;
                if (cell == '#' || cell == 'B' || cell == 'P')
                    supportColumn = true;
            }

            if (!occupiedColumn)
            {
                if (currentLanding > 0)
                {
                    features.minLandingWidth = Mathf.Min(features.minLandingWidth, currentLanding);
                    currentLanding = 0;
                }

                currentGap++;
                features.maxGapWidth = Mathf.Max(features.maxGapWidth, currentGap);
            }
            else
            {
                if (currentGap > 0)
                {
                    features.gapCount++;
                    currentGap = 0;
                }

                if (supportColumn)
                    currentLanding++;
            }
        }

        if (currentGap > 0)
            features.gapCount++;

        if (currentLanding > 0)
            features.minLandingWidth = Mathf.Min(features.minLandingWidth, currentLanding);

        if (features.minLandingWidth == int.MaxValue)
            features.minLandingWidth = 0;
    }

    private static Vector2 EstimateExitDelta(ChunkBlueprint blueprint)
    {
        if (blueprint == null)
            return Vector2.zero;

        Vector2 entry = EstimateEntryMarker(blueprint);
        Vector2 exit = EstimateExitMarker(blueprint);
        return exit - entry;
    }

    private static Vector2 EstimateEntryMarker(ChunkBlueprint blueprint)
    {
        Vector2Int cell = blueprint.entryCell;
        int x = cell.x;
        int y = cell.y;

        if (IsSupportCell(blueprint, x + 1, y))
            return TopLeftOfCell(blueprint, x + 1, y);

        if (IsSupportCell(blueprint, x, y + 1))
            return TopLeftOfCell(blueprint, x, y + 1);

        if (IsSupportCell(blueprint, x - 1, y))
            return TopLeftOfCell(blueprint, x - 1, y);

        return CellCenter(blueprint, x, y);
    }

    private static Vector2 EstimateExitMarker(ChunkBlueprint blueprint)
    {
        Vector2Int cell = blueprint.exitCell;
        int x = cell.x;
        int y = cell.y;

        if (IsSupportCell(blueprint, x - 1, y))
            return TopRightOfCell(blueprint, x - 1, y);

        if (IsSupportCell(blueprint, x, y + 1))
            return TopRightOfCell(blueprint, x, y + 1);

        if (IsSupportCell(blueprint, x + 1, y))
            return TopRightOfCell(blueprint, x + 1, y);

        return CellCenter(blueprint, x, y);
    }

    private static bool IsSupportCell(ChunkBlueprint blueprint, int x, int y)
    {
        char cell = GetCell(blueprint, x, y);
        return cell == '#' || cell == 'B' || cell == 'P';
    }

    private static char GetCell(ChunkBlueprint blueprint, int x, int y)
    {
        if (blueprint == null || blueprint.rows == null)
            return '\0';

        if (y < 0 || y >= blueprint.rows.Count)
            return '\0';

        string row = blueprint.rows[y];
        if (string.IsNullOrEmpty(row) || x < 0 || x >= row.Length)
            return '\0';

        return row[x];
    }

    private static Vector2 CellCenter(ChunkBlueprint blueprint, int x, int y)
    {
        return new Vector2(x, blueprint.height - 1 - y);
    }

    private static Vector2 TopLeftOfCell(ChunkBlueprint blueprint, int x, int y)
    {
        return new Vector2(x - 0.5f, blueprint.height - 1 - y + 0.5f);
    }

    private static Vector2 TopRightOfCell(ChunkBlueprint blueprint, int x, int y)
    {
        return new Vector2(x + 0.5f, blueprint.height - 1 - y + 0.5f);
    }
}
