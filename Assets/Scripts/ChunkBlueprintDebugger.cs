using UnityEngine;

public class ChunkBlueprintDebugger : MonoBehaviour
{
    [SerializeField] private ChunkBlueprintExamples examples;

    private void Start()
    {
        if (examples == null)
        {
            Debug.LogWarning("ChunkBlueprintDebugger: No ChunkBlueprintExamples reference assigned.");
            return;
        }

        DebugBlueprint("Flat", examples.testFlatChunk);
        DebugBlueprint("Gap", examples.testGapChunk);
    }

    private void DebugBlueprint(string label, ChunkBlueprint blueprint)
    {
        if (blueprint == null)
        {
            Debug.LogWarning($"ChunkBlueprintDebugger: {label} blueprint is null.");
            return;
        }

        Debug.Log($"--- {label} Blueprint ---");
        Debug.Log($"Name: {blueprint.chunkName}");
        Debug.Log($"Size: {blueprint.width} x {blueprint.height}");
        Debug.Log($"Entry: {blueprint.entryCell}");
        Debug.Log($"Exit: {blueprint.exitCell}");
        Debug.Log($"PrimaryTag: {blueprint.primaryTag}");
        Debug.Log($"Difficulty: {blueprint.difficultyRating}");
        Debug.Log($"HasHazard: {blueprint.hasHazard}");
        Debug.Log($"EstimatedJumps: {blueprint.estimatedJumps}");
        Debug.Log($"BasicSizeValid: {blueprint.IsValidBasicSize()}");

        if (blueprint.rows != null)
        {
            for (int i = 0; i < blueprint.rows.Count; i++)
            {
                Debug.Log($"Row {i}: {blueprint.rows[i]}");
            }
        }

        bool entryInside =
            blueprint.entryCell.x >= 0 && blueprint.entryCell.x < blueprint.width &&
            blueprint.entryCell.y >= 0 && blueprint.entryCell.y < blueprint.height;

        bool exitInside =
            blueprint.exitCell.x >= 0 && blueprint.exitCell.x < blueprint.width &&
            blueprint.exitCell.y >= 0 && blueprint.exitCell.y < blueprint.height;

        Debug.Log($"EntryInsideBounds: {entryInside}");
        Debug.Log($"ExitInsideBounds: {exitInside}");
    }
}