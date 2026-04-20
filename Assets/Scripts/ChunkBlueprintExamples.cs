using System.Collections.Generic;
using UnityEngine;

public class ChunkBlueprintExamples : MonoBehaviour
{
    [TextArea(10, 30)]
    public string notes =
        "Legend:\n" +
        ". = empty\n" +
        "# = solid\n" +
        "S = spike\n" +
        "M = moving hazard marker\n" +
        "E = entry\n" +
        "X = exit";

    public ChunkBlueprint testFlatChunk = new ChunkBlueprint
    {
        chunkName = "BP_Flat_Test",
        width = 8,
        height = 4,
        rows = new List<string>
        {
            "........",
            "........",
            "........",
            "E######X"
        },
        entryCell = new Vector2Int(0, 3),
        exitCell = new Vector2Int(7, 3),
        primaryTag = ChunkTag.Rest,
        difficultyRating = 1,
        hasHazard = false,
        estimatedJumps = 0,
        tags = new ChunkTag[] { ChunkTag.Safe, ChunkTag.Rest }
    };

    public ChunkBlueprint testGapChunk = new ChunkBlueprint
    {
        chunkName = "BP_Gap_Test",
        width = 8,
        height = 4,
        rows = new List<string>
        {
            "........",
            "........",
            "........",
            "E###..#X"
        },
        entryCell = new Vector2Int(0, 3),
        exitCell = new Vector2Int(7, 3),
        primaryTag = ChunkTag.Gap,
        difficultyRating = 2,
        hasHazard = false,
        estimatedJumps = 1,
        tags = new ChunkTag[] { ChunkTag.Gap }
    };
}