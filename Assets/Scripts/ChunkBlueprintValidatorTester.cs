using UnityEngine;

public class ChunkBlueprintValidatorTester : MonoBehaviour
{
    [SerializeField] private RealChunkBlueprintLibrary library;

    private void Start()
    {
        if (library == null)
        {
            Debug.LogWarning("ChunkBlueprintValidatorTester: No RealChunkBlueprintLibrary assigned.");
            return;
        }

        ValidateAndLog("Flat", library.flatChunk);
        ValidateAndLog("Gap", library.gapChunk);
        ValidateAndLog("Precision", library.precisionChunk);
        ValidateAndLog("Spikes", library.spikesChunk);
        ValidateAndLog("Vertical", library.verticalChunk);
    }

    private void ValidateAndLog(string label, ChunkBlueprint blueprint)
    {
        ChunkBlueprintValidationResult result = ChunkBlueprintValidator.Validate(blueprint);

        if (result.isValid)
        {
            Debug.Log($"VALID: {label} -> {blueprint.chunkName}");
        }
        else
        {
            Debug.LogWarning($"INVALID: {label} -> {blueprint.chunkName}");

            for (int i = 0; i < result.errors.Count; i++)
            {
                Debug.LogWarning($"  Error {i + 1}: {result.errors[i]}");
            }
        }
    }
}