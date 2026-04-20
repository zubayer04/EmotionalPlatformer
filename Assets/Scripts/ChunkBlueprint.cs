using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ChunkBlueprint
{
    public string chunkName;
    public int width;
    public int height;

    [Tooltip("Top-to-bottom rows of the chunk grid.")]
    public List<string> rows = new List<string>();

    public Vector2Int entryCell;
    public Vector2Int exitCell;

    public ChunkTag primaryTag = ChunkTag.Safe;
    [Range(0, 10)] public int difficultyRating = 1;
    public bool hasHazard = false;
    public int estimatedJumps = 0;
    public ChunkTag[] tags;

    public bool IsValidBasicSize()
    {
        if (width <= 0 || height <= 0) return false;
        if (rows == null || rows.Count != height) return false;

        for (int i = 0; i < rows.Count; i++)
        {
            if (string.IsNullOrEmpty(rows[i]) || rows[i].Length != width)
                return false;
        }

        return true;
    }

    public char GetCell(int x, int y)
    {
        if (!IsValidBasicSize()) return '?';
        if (x < 0 || x >= width || y < 0 || y >= height) return '?';

        return rows[y][x];
    }
}