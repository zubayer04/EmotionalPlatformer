using UnityEngine;

public class ChunkBlueprintBuildTester : MonoBehaviour
{
    [SerializeField] private RealChunkBlueprintLibrary library;
    [SerializeField] private ChunkBlueprintRuntimeBuilder builder;

    [Header("Choose Which Blueprint To Spawn")]
    [SerializeField] private bool spawnFlat = true;
    [SerializeField] private bool spawnGap = false;
    [SerializeField] private bool spawnPrecision = false;
    [SerializeField] private bool spawnSpikes = false;
    [SerializeField] private bool spawnVertical = false;

    private void Start()
    {
        if (library == null || builder == null)
        {
            Debug.LogWarning("ChunkBlueprintBuildTester: Missing library or builder reference.");
            return;
        }

        ChunkBlueprint selected = null;

        if (spawnFlat) selected = library.flatChunk;
        else if (spawnGap) selected = library.gapChunk;
        else if (spawnPrecision) selected = library.precisionChunk;
        else if (spawnSpikes) selected = library.spikesChunk;
        else if (spawnVertical) selected = library.verticalChunk;

        if (selected == null)
        {
            Debug.LogWarning("ChunkBlueprintBuildTester: No blueprint selected.");
            return;
        }

        builder.BuildChunk(selected, transform.position);
    }
}