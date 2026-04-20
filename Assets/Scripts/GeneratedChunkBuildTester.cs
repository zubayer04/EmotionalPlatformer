using UnityEngine;

public class GeneratedChunkBuildTester : MonoBehaviour
{
    [SerializeField] private ChunkBlueprintRuntimeBuilder builder;
    [SerializeField] private ChunkGenerationRequest request = new ChunkGenerationRequest();

    private void Start()
    {
        if (builder == null)
        {
            Debug.LogWarning("GeneratedChunkBuildTester: No builder assigned.");
            return;
        }

        ChunkBlueprint generated = SimpleChunkBlueprintGenerator.Generate(request);

        if (generated == null)
        {
            Debug.LogWarning("GeneratedChunkBuildTester: Generator returned null.");
            return;
        }

        ChunkBlueprintValidationResult validation = ChunkBlueprintValidator.Validate(generated);

        if (!validation.isValid)
        {
            Debug.LogWarning("GeneratedChunkBuildTester: Generated blueprint failed validation.");
            for (int i = 0; i < validation.errors.Count; i++)
            {
                Debug.LogWarning($"  Error {i + 1}: {validation.errors[i]}");
            }
            return;
        }

        builder.BuildChunk(generated, transform.position);
        Debug.Log($"Generated and built chunk: {generated.chunkName} | Tag={generated.primaryTag} | Difficulty={generated.difficultyRating}");
    }
}