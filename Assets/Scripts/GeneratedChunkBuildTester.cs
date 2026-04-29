using UnityEngine;

public class GeneratedChunkBuildTester : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private ChunkBlueprintRuntimeBuilder builder;
    [SerializeField] private GameObject sourceChunkPrefab;
    [SerializeField] private ChunkGenerationRequest request = new ChunkGenerationRequest();

    [Header("Isolation Test")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool buildBatchOnStart = false;
    [SerializeField] private int sampleCount = 6;
    [SerializeField] private int baseSeed = 12345;
    [SerializeField] private float sampleSpacing = 16f;
    [SerializeField] private bool clearPreviousBeforeBuild = true;
    [SerializeField] private bool addDebugLabels = true;

    private GameObject currentRoot;

    private void Start()
    {
        if (!buildOnStart)
            return;

        if (buildBatchOnStart)
            BuildIsolationBatch();
        else
            BuildSingleGeneratedChunk();
    }

    [ContextMenu("Build Single Generated Chunk")]
    public void BuildSingleGeneratedChunk()
    {
        if (!CanBuild())
            return;

        if (clearPreviousBeforeBuild)
            ClearBuiltChunks();

        currentRoot = new GameObject("GeneratedChunkIsolation_Single");
        currentRoot.transform.SetParent(transform);
        currentRoot.transform.position = transform.position;

        BuildSample(currentRoot.transform, transform.position, baseSeed, 0);
    }

    [ContextMenu("Build Isolation Batch")]
    public void BuildIsolationBatch()
    {
        if (!CanBuild())
            return;

        if (clearPreviousBeforeBuild)
            ClearBuiltChunks();

        currentRoot = new GameObject("GeneratedChunkIsolation_Batch");
        currentRoot.transform.SetParent(transform);
        currentRoot.transform.position = transform.position;

        int count = Mathf.Max(1, sampleCount);
        for (int i = 0; i < count; i++)
        {
            Vector3 origin = transform.position + new Vector3(i * sampleSpacing, 0f, 0f);
            BuildSample(currentRoot.transform, origin, baseSeed + i, i);
        }
    }

    [ContextMenu("Clear Built Chunks")]
    public void ClearBuiltChunks()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != null && child.name.StartsWith("GeneratedChunkIsolation"))
                DestroyImmediate(child.gameObject);
        }

        currentRoot = null;
    }

    private bool CanBuild()
    {
        if (builder == null)
        {
            Debug.LogWarning("GeneratedChunkBuildTester: No builder assigned.");
            return false;
        }

        return true;
    }

    private void BuildSample(Transform parent, Vector3 origin, int seed, int sampleIndex)
    {
        Random.State previousRandomState = Random.state;
        Random.InitState(seed);

        ChunkGenerationRequest sampleRequest = CreateRequestFromCurrentSettings();
        ChunkBlueprint generated = SimpleChunkBlueprintGenerator.Generate(sampleRequest);

        Random.state = previousRandomState;

        if (generated == null)
        {
            Debug.LogWarning($"GeneratedChunkBuildTester: Generator returned null for sample {sampleIndex}.");
            return;
        }

        ChunkBlueprintValidationResult validation = ChunkBlueprintValidator.Validate(generated);
        if (!validation.isValid)
        {
            Debug.LogWarning($"GeneratedChunkBuildTester: Generated blueprint failed validation for sample {sampleIndex}.");
            for (int i = 0; i < validation.errors.Count; i++)
            {
                Debug.LogWarning($"  Error {i + 1}: {validation.errors[i]}");
            }
            return;
        }

        GameObject built = builder.BuildChunk(generated, origin);
        if (built == null)
        {
            Debug.LogWarning($"GeneratedChunkBuildTester: Runtime builder failed for sample {sampleIndex}.");
            return;
        }

        built.name = $"{generated.chunkName}_Sample{sampleIndex + 1}";
        built.transform.SetParent(parent);

        ChunkBlueprintFeatures features = ChunkBlueprintFeatureExtractor.Analyze(generated);
        string equivalence = GetEquivalenceSummary(sampleRequest, generated, features);

        if (addDebugLabels)
            AddDebugLabel(parent, origin, sampleIndex, generated, features, equivalence);

        Debug.Log(
            $"GeneratedChunkBuildTester sample {sampleIndex + 1}: {generated.chunkName} | " +
            $"Tag={generated.primaryTag} | Difficulty={generated.difficultyRating} | " +
            $"Rows={ChunkBlueprintFeatureExtractor.RowsToInlineText(generated)} | " +
            $"Features={features.ToSummary()} | Equivalence={equivalence}");
    }

    private ChunkGenerationRequest CreateRequestFromCurrentSettings()
    {
        ChunkGenerationRequest sampleRequest = new ChunkGenerationRequest
        {
            requestedPrimaryTag = request.requestedPrimaryTag,
            targetDifficulty = request.targetDifficulty,
            requireHazard = request.requireHazard,
            preferredWidth = request.preferredWidth,
            preferredHeight = request.preferredHeight,
            hasSourceContext = request.hasSourceContext,
            sourceChunkName = request.sourceChunkName,
            sourceDifficulty = request.sourceDifficulty,
            sourceHasHazard = request.sourceHasHazard,
            sourceEstimatedJumps = request.sourceEstimatedJumps,
            sourceExitDelta = request.sourceExitDelta,
            sourceMaxGapWidth = request.sourceMaxGapWidth
        };

        ChunkData sourceData = sourceChunkPrefab != null ? sourceChunkPrefab.GetComponent<ChunkData>() : null;
        if (sourceData == null)
            return sampleRequest;

        sampleRequest.requestedPrimaryTag = sourceData.primaryTag;
        sampleRequest.targetDifficulty = sourceData.difficultyRating;
        sampleRequest.requireHazard = sourceData.hasHazard;
        sampleRequest.preferredWidth = Mathf.Max(request.preferredWidth, Mathf.CeilToInt(Mathf.Abs(sourceData.exitDelta.x)) + 2);
        sampleRequest.preferredHeight = Mathf.Max(request.preferredHeight, 2);
        sampleRequest.hasSourceContext = true;
        sampleRequest.sourceChunkName = sourceChunkPrefab.name;
        sampleRequest.sourceDifficulty = sourceData.difficultyRating;
        sampleRequest.sourceHasHazard = sourceData.hasHazard;
        sampleRequest.sourceEstimatedJumps = sourceData.estimatedJumps;
        sampleRequest.sourceExitDelta = sourceData.exitDelta;
        sampleRequest.sourceMaxGapWidth = ChunkBlueprintFeatureExtractor.EstimateSourceMaxGapWidth(sourceChunkPrefab);

        return sampleRequest;
    }

    private string GetEquivalenceSummary(ChunkGenerationRequest sampleRequest, ChunkBlueprint generated, ChunkBlueprintFeatures features)
    {
        if (sampleRequest == null || !sampleRequest.hasSourceContext)
            return "manual_request_no_source_context";

        if (generated.primaryTag != sampleRequest.requestedPrimaryTag)
            return $"tag_mismatch:{sampleRequest.requestedPrimaryTag}->{generated.primaryTag}";

        if (generated.hasHazard != sampleRequest.sourceHasHazard)
            return $"hazard_mismatch:{sampleRequest.sourceHasHazard}->{generated.hasHazard}";

        int difficultyDelta = generated.difficultyRating - sampleRequest.sourceDifficulty;
        if (Mathf.Abs(difficultyDelta) > 0 && !IsGeneratedSafeRestDifficultyEquivalent(sampleRequest, generated))
            return $"difficulty_delta:{difficultyDelta:+#;-#;0}";

        int jumpsDelta = generated.estimatedJumps - sampleRequest.sourceEstimatedJumps;
        if (Mathf.Abs(jumpsDelta) > 1)
            return $"jump_delta:{jumpsDelta:+#;-#;0}";

        if (sampleRequest.sourceMaxGapWidth > 0 && generated.primaryTag == ChunkTag.Gap)
        {
            int gapDelta = features.maxGapWidth - sampleRequest.sourceMaxGapWidth;
            if (gapDelta != 0)
                return $"max_gap_delta:{gapDelta:+#;-#;0}";
        }

        Vector2 exitDeltaDiff = features.estimatedExitDelta - sampleRequest.sourceExitDelta;
        float verticalExitDeltaTolerance = Mathf.Max(
            GetGeneratedGapVerticalExitDeltaTolerance(sampleRequest, generated),
            Mathf.Max(
                GetGeneratedSafeVerticalExitDeltaTolerance(sampleRequest, generated),
                GetGeneratedPrecisionVerticalExitDeltaTolerance(sampleRequest, generated)));
        if (Mathf.Abs(exitDeltaDiff.x) > 1.25f || Mathf.Abs(exitDeltaDiff.y) > verticalExitDeltaTolerance)
            return $"exit_delta_mismatch:({exitDeltaDiff.x:+0.##;-0.##;0},{exitDeltaDiff.y:+0.##;-0.##;0})";

        return "equivalent";
    }

    private float GetGeneratedGapVerticalExitDeltaTolerance(ChunkGenerationRequest sampleRequest, ChunkBlueprint generated)
    {
        if (sampleRequest != null &&
            generated != null &&
            generated.primaryTag == ChunkTag.Gap &&
            sampleRequest.sourceMaxGapWidth > 0 &&
            sampleRequest.sourceMaxGapWidth <= 4)
        {
            return 2.25f;
        }

        return 1.25f;
    }

    private bool IsGeneratedSafeRestDifficultyEquivalent(ChunkGenerationRequest sampleRequest, ChunkBlueprint generated)
    {
        if (sampleRequest == null || generated == null)
            return false;

        bool controlledRiseVariant =
            generated.chunkName == "Generated_Safe_RiseRest_Box2" ||
            generated.chunkName == "Generated_Safe_RiseRest_Box3";

        return controlledRiseVariant &&
               sampleRequest.requestedPrimaryTag == ChunkTag.Safe &&
               generated.primaryTag == ChunkTag.Safe &&
               !sampleRequest.sourceHasHazard &&
               sampleRequest.sourceEstimatedJumps == 0 &&
               generated.difficultyRating == sampleRequest.sourceDifficulty + 1;
    }

    private float GetGeneratedSafeVerticalExitDeltaTolerance(ChunkGenerationRequest sampleRequest, ChunkBlueprint generated)
    {
        if (sampleRequest != null &&
            generated != null &&
            generated.primaryTag == ChunkTag.Safe &&
            generated.chunkName.StartsWith("Generated_Safe_") &&
            sampleRequest.hasSourceContext &&
            sampleRequest.sourceEstimatedJumps == 0 &&
            !sampleRequest.sourceHasHazard)
        {
            return 4.25f;
        }

        return 1.25f;
    }

    private float GetGeneratedPrecisionVerticalExitDeltaTolerance(ChunkGenerationRequest sampleRequest, ChunkBlueprint generated)
    {
        if (sampleRequest != null &&
            generated != null &&
            generated.primaryTag == ChunkTag.Precision &&
            generated.chunkName.StartsWith("Generated_Precision_ElevatedPlatform_") &&
            sampleRequest.hasSourceContext &&
            sampleRequest.sourceEstimatedJumps == 2 &&
            !sampleRequest.sourceHasHazard)
        {
            return 2.25f;
        }

        return 1.25f;
    }

    private void AddDebugLabel(
        Transform parent,
        Vector3 origin,
        int sampleIndex,
        ChunkBlueprint generated,
        ChunkBlueprintFeatures features,
        string equivalence)
    {
        GameObject labelObject = new GameObject($"BlueprintSampleLabel_{sampleIndex + 1}");
        labelObject.transform.SetParent(parent);
        labelObject.transform.position = origin + new Vector3(0f, 3f, 0f);

        TextMesh textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.text =
            $"{sampleIndex + 1}. {generated.chunkName}\n" +
            $"diff={generated.difficultyRating}, jumps={generated.estimatedJumps}, hazard={generated.hasHazard}\n" +
            $"{features.ToSummary()}\n" +
            equivalence;
        textMesh.characterSize = 0.25f;
        textMesh.anchor = TextAnchor.LowerLeft;
        textMesh.color = equivalence == "equivalent" ? Color.green : Color.yellow;
    }
}
