using System;
using System.Collections.Generic;
using UnityEngine;

public enum LevelGenerationMode
{
    Constrained,
    NaiveRandom
}

public class LevelGenerator : MonoBehaviour
{
    [Header("Chunks")]
    [Tooltip("Prefabs that can be used as pieces of the level (after the starting chunk).")]
    public GameObject[] chunkPrefabs;

    [Tooltip("Chunk used for the very first platform (so the player always spawns safely).")]
    public GameObject startingChunkPrefab;

    [Tooltip("Total number of chunks INCLUDING the starting chunk.")]
    public int totalChunks = 8;

    [Header("Start")]
    [Tooltip("Where the first chunk's Entry will be snapped to. If null, uses this object's position.")]
    public Transform startPoint;

    [Header("End Point")]
    [Tooltip("Prefab that marks the end of the level. Should have a trigger collider + EndPoint.cs.")]
    public GameObject endPointPrefab;

    [Tooltip("Optional: small offset applied when spawning the endpoint (e.g., lift it slightly).")]
    public Vector3 endPointOffset = Vector3.zero;

    [Header("Behaviour")]
    [Tooltip("If true, GenerateLevel() will first clear any previously spawned chunks.")]
    public bool clearBeforeGenerate = true;

    [Header("Evaluation Mode")]
    [Tooltip("Constrained uses the full sequencing system. NaiveRandom randomly selects from the same eligible candidate pool with only basic hard playability constraints.")]
    public LevelGenerationMode generationMode = LevelGenerationMode.Constrained;

    [Header("Generated Blueprint Integration")]
    [Tooltip("If enabled, some selected chunks may be replaced by runtime-generated blueprint chunks.")]
    public bool useGeneratedBlueprintChunks = true;

    [Tooltip("If enabled, validated blueprint variants join the sequencing candidate pool before selection.")]
    public bool useGeneratedBlueprintCandidateSelection = true;

    [Tooltip("Builder used to convert valid blueprints into runtime chunk GameObjects.")]
    [SerializeField] private ChunkBlueprintRuntimeBuilder blueprintRuntimeBuilder;

    [Tooltip("Chance that an eligible handcrafted chunk is replaced with a generated blueprint version.")]
    [Range(0f, 1f)] public float generatedChunkReplacementChance = 0.25f;

    [Tooltip("Maximum validated generated variants offered per eligible source chunk during sequencing.")]
    [Range(1, 5)] public int generatedCandidateVariantsPerSource = 2;

    [Tooltip("Total source-family weight shared by generated variants from one source chunk.")]
    [Range(0f, 2f)] public float generatedCandidateFamilyWeight = 0.75f;

    [Tooltip("Only these chunk families are eligible for generated replacement.")]
    public bool allowGeneratedGap = true;
    public bool allowGeneratedPrecision = true;
    public bool allowGeneratedVertical = false;
    public bool allowGeneratedSpikes = false;
    public bool allowGeneratedSafeRest = false;

    [Header("2-Step Markov Sequencing")]
    [Tooltip("If enabled, chunk selection uses previous TWO chunk states plus target difficulty.")]
    public bool useTwoStepMarkov = true;

    [Tooltip("Learnable Markov weight table. Created automatically if null.")]
    private MarkovWeightTable markovWeightTable;

    [Header("Bounded Lookahead Sequencing")]
    [Tooltip("If enabled, selection scores short future sequences before committing to the next chunk.")]
    public bool useLookaheadSequencePlanning = true;

    [Tooltip("Number of generated slots considered, including the immediate next chunk.")]
    [Range(1, 3)] public int lookaheadDepth = 2;

    [Tooltip("How many partial future sequences are kept at each lookahead step.")]
    [Range(1, 6)] public int lookaheadBeamWidth = 4;

    [Header("Structural Budget Awareness")]
    [Tooltip("Softly down-weight candidates when hazards, jumps, and vertical chunks are already above the target-appropriate whole-level budget.")]
    public bool useStructuralBudgetPenalty = true;

    [Tooltip("Extra additive structural score tolerated beyond the target-derived budget before penalties become strong.")]
    [Range(0f, 2f)] public float structuralBudgetSlack = 0.6f;

    [Tooltip("How strongly candidates are down-weighted once the soft whole-level structural budget is exceeded.")]
    [Range(0f, 2f)] public float structuralBudgetPenaltyStrength = 0.8f;

    [Tooltip("Avoid spawning the exact same prefab twice in a row.")]
    public bool avoidSamePrefabBackToBack = true;

    [Tooltip("Maximum number of chunks with the same primaryTag allowed in a row.")]
    [Range(1, 3)] public int maxSamePrimaryTagStreak = 2;

    [Header("Targeted Difficulty")]
    [Tooltip("Desired end-of-level chunk difficulty target (0-10).")]
    [Range(0f, 10f)] public float targetDifficulty = 5f;

    [Tooltip("The level starts easier than the target, then ramps up toward it.")]
    [Range(0f, 10f)] public float startDifficultyBias = 2f;

    [Tooltip("How strongly to prefer chunks close to the slot target difficulty. Higher = more strict.")]
    [Range(0.1f, 5f)] public float difficultyPreferenceStrength = 1.6f;

    [Tooltip("Extra weight for variety (reduces repeating the same primary tag back-to-back).")]
    [Range(0f, 2f)] public float varietyBonus = 0.25f;

    [Tooltip("Penalty multiplier for hard chunks very early in the level.")]
    [Range(0.1f, 1f)] public float earlyHardPenalty = 0.45f;

    [Header("Difficulty Scoring (tweakable weights)")]
    [Tooltip("Weight applied to the average of chunk difficultyRating.")]
    public float wAvgDifficulty = 1.0f;

    [Tooltip("Extra score added per hazard chunk.")]
    public float wHazardChunk = 0.75f;

    [Tooltip("Extra score added per estimated jump across the level.")]
    public float wEstimatedJump = 0.15f;

    [Tooltip("Extra score added per chunk tagged Vertical.")]
    public float wVerticalChunk = 0.35f;

    [Tooltip("Clamp the final score to this max (set 0 to disable clamping).")]
    public float clampMaxScore = 10f;

    // spawned objects cleared before the next generated level
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();

    // spawned chunk data used for scoring
    private readonly List<ChunkData> spawnedChunkData = new List<ChunkData>();

    private readonly Dictionary<ChunkData, LevelRunLog.SlotRecord> activeSlotRecordsByChunk =
        new Dictionary<ChunkData, LevelRunLog.SlotRecord>();

    private LevelRunLog.RunRecord currentRunLog;
    private int currentRunSeed;
    private int generatedRunCounter;

    private class ChunkSelectionCandidate
    {
        public GameObject sourcePrefab;
        public ChunkBlueprint blueprint;
        public ChunkBlueprintFeatures blueprintFeatures;
        public string validationReason = "none";
        public float sourceFamilyWeight = 1f;
        public bool lookaheadUsed;
        public int lookaheadDepthUsed;
        public float lookaheadBestScore;
        public float lookaheadSelectionWeight;
        public string lookaheadDecisionSummary = "none";
        public float structuralBudgetWeight = 1f;
        public float structuralBudgetProjectedLoad;
        public float structuralBudgetAllowedLoad;

        public bool IsGenerated => blueprint != null;

        public string CandidateType => IsGenerated ? "generated_blueprint" : "handcrafted";

        public string DisplayName
        {
            get
            {
                if (IsGenerated && !string.IsNullOrEmpty(blueprint.chunkName))
                    return blueprint.chunkName;

                return sourcePrefab != null ? sourcePrefab.name : "MissingCandidate";
            }
        }

        public string SourceName => sourcePrefab != null ? sourcePrefab.name : "MissingSource";

        public ChunkData SourceData => sourcePrefab != null ? sourcePrefab.GetComponent<ChunkData>() : null;

        public ChunkTag PrimaryTag
        {
            get
            {
                if (IsGenerated)
                    return blueprint.primaryTag;

                ChunkData data = SourceData;
                return data != null ? data.primaryTag : ChunkTag.Rest;
            }
        }

        public int Difficulty
        {
            get
            {
                if (IsGenerated)
                    return blueprint.difficultyRating;

                ChunkData data = SourceData;
                return data != null ? data.difficultyRating : 0;
            }
        }

        public bool HasHazard
        {
            get
            {
                if (IsGenerated)
                    return blueprint.hasHazard;

                ChunkData data = SourceData;
                return data != null && data.hasHazard;
            }
        }

        public int EstimatedJumps
        {
            get
            {
                if (IsGenerated)
                    return blueprint.estimatedJumps;

                ChunkData data = SourceData;
                return data != null ? data.estimatedJumps : 0;
            }
        }

        public Vector2 ExitDelta
        {
            get
            {
                if (IsGenerated && blueprintFeatures != null)
                    return blueprintFeatures.estimatedExitDelta;

                ChunkData data = SourceData;
                return data != null ? data.exitDelta : Vector2.zero;
            }
        }

        public bool HasTag(ChunkTag tag)
        {
            if (PrimaryTag == tag)
                return true;

            if (IsGenerated && blueprint.tags != null)
            {
                for (int i = 0; i < blueprint.tags.Length; i++)
                {
                    if (blueprint.tags[i] == tag)
                        return true;
                }

                return false;
            }

            ChunkData data = SourceData;
            if (data == null || data.tags == null)
                return false;

            for (int i = 0; i < data.tags.Length; i++)
            {
                if (data.tags[i] == tag)
                    return true;
            }

            return false;
        }
    }

    // exposed for level manager and runtime helpers
    public Vector3 FirstEntryWorld { get; private set; }
    public Vector3 LastExitWorld { get; private set; }

    // level bounds for kill zone sizing and placement
    public Bounds LevelWorldBounds { get; private set; }
    public float LowestSolidY { get; private set; }

    // difficulty stats
    public float LevelDifficultyScore { get; private set; }
    public float AvgChunkDifficulty { get; private set; }
    public int HazardChunkCount { get; private set; }
    public int TotalEstimatedJumps { get; private set; }
    public int VerticalChunkCount { get; private set; }
    public int ChunkCountThisLevel { get; private set; }
    public LevelRunLog.RunRecord CurrentRunLog => currentRunLog;
    public int CurrentRunSeed => currentRunSeed;

    public void GenerateLevel()
    {
        GenerateLevelWithSeed(CreateNextRunSeed());
    }

    public void RecordDeathForChunk(ChunkData chunk, string source, float timeOfDeathSeconds)
    {
        // links deaths back to the active slot record for evaluation.
        if (currentRunLog == null)
            return;

        LevelRunLog.DeathEventRecord deathEvent = new LevelRunLog.DeathEventRecord
        {
            source = source,
            chunkName = chunk != null ? LevelRunLog.CleanName(chunk.name) : "None",
            primaryTag = chunk != null ? chunk.primaryTag.ToString() : "None",
            timeOfDeathSeconds = timeOfDeathSeconds,
            slotIndex = -1
        };

        if (chunk != null && activeSlotRecordsByChunk.TryGetValue(chunk, out LevelRunLog.SlotRecord slotRecord))
        {
            slotRecord.deathsAttributedToSlot++;
            deathEvent.slotIndex = slotRecord.sequenceIndex;
        }

        currentRunLog.deathEvents.Add(deathEvent);
    }

    public ChunkData GetBestChunkForWorldPosition(Vector3 worldPos)
    {
        if (spawnedChunkData.Count == 0) return null;

        ChunkData containingChunk = null;
        float closestDistanceSq = float.PositiveInfinity;
        ChunkData closestChunk = null;

        for (int i = 0; i < spawnedChunkData.Count; i++)
        {
            ChunkData cd = spawnedChunkData[i];
            if (cd == null) continue;

            Collider2D[] cols = cd.GetComponentsInChildren<Collider2D>(true);
            bool containsPoint = false;
            Bounds combined = new Bounds(cd.transform.position, Vector3.zero);
            bool hasBounds = false;

            for (int c = 0; c < cols.Length; c++)
            {
                Collider2D col = cols[c];
                if (col == null) continue;
                if (col.isTrigger) continue;

                if (!hasBounds)
                {
                    combined = col.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(col.bounds);
                }

                if (col.OverlapPoint(worldPos))
                {
                    containsPoint = true;
                }
            }

            if (containsPoint)
            {
                containingChunk = cd;
                break;
            }

            if (hasBounds)
            {
                Vector3 closestPoint = combined.ClosestPoint(worldPos);
                float distSq = (closestPoint - worldPos).sqrMagnitude;

                if (distSq < closestDistanceSq)
                {
                    closestDistanceSq = distSq;
                    closestChunk = cd;
                }
            }
        }

        return containingChunk != null ? containingChunk : closestChunk;
    }

    private void GenerateLevelWithSeed(int runSeed)
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;

        try
        {
            currentRunLog = null;
            currentRunSeed = runSeed;
            UnityEngine.Random.InitState(runSeed);

            List<ChunkSelectionCandidate> sequence = BuildFreshSequence();

            if (sequence == null || sequence.Count == 0)
            {
                Debug.LogWarning("LevelGenerator: No sequence was available for generation.");
                return;
            }

            currentRunLog = CreateRunRecord(runSeed);
            GenerateLevelFromSequence(sequence);
        }
        finally
        {
            UnityEngine.Random.state = previousRandomState;
        }
    }

    private int CreateNextRunSeed()
    {
        generatedRunCounter++;
        return unchecked((int)(DateTime.UtcNow.Ticks ^ (generatedRunCounter * 397L)));
    }

    private LevelRunLog.RunRecord CreateRunRecord(int runSeed)
    {
        // captures generation settings before the run is played.
        return new LevelRunLog.RunRecord
        {
            runId = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}_{Mathf.Abs(runSeed)}",
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            runSeed = runSeed,
            targetDifficultyBeforeRun = targetDifficulty,
            startDifficultyBias = startDifficultyBias,
            generationMode = generationMode.ToString(),
            difficultyPreferenceStrength = difficultyPreferenceStrength,
            useTwoStepMarkov = useTwoStepMarkov,
            useLookaheadSequencePlanning = useLookaheadSequencePlanning,
            lookaheadDepth = lookaheadDepth,
            lookaheadBeamWidth = lookaheadBeamWidth,
            useStructuralBudgetPenalty = useStructuralBudgetPenalty,
            structuralBudgetSlack = structuralBudgetSlack,
            structuralBudgetPenaltyStrength = structuralBudgetPenaltyStrength,
            useGeneratedBlueprintChunks = useGeneratedBlueprintChunks,
            useGeneratedBlueprintCandidateSelection = useGeneratedBlueprintCandidateSelection,
            generatedChunkReplacementChance = generatedChunkReplacementChance,
            generatedCandidateVariantsPerSource = generatedCandidateVariantsPerSource,
            generatedCandidateFamilyWeight = generatedCandidateFamilyWeight,
            totalChunksConfigured = totalChunks
        };
    }

    private List<ChunkSelectionCandidate> BuildFreshSequence()
    {
        // plans the chunk sequence before any runtime chunks are spawned.
        List<ChunkSelectionCandidate> sequence = new List<ChunkSelectionCandidate>();

        if ((chunkPrefabs == null || chunkPrefabs.Length == 0) && startingChunkPrefab == null)
        {
            Debug.LogWarning("LevelGenerator: No chunk prefabs assigned!");
            return sequence;
        }

        int remainingChunks = totalChunks;

        ChunkTag prev1 = ChunkTag.Rest;
        ChunkTag prev2 = ChunkTag.Rest;

        string previousPrefabName = string.Empty;
        string previousCandidateName = string.Empty;
        int samePrimaryTagStreak = 0;
        StructuralBudgetState structuralBudgetState = default;

        if (startingChunkPrefab != null && remainingChunks > 0)
        {
            ChunkSelectionCandidate startingCandidate = CreateHandcraftedCandidate(startingChunkPrefab);
            sequence.Add(startingCandidate);
            structuralBudgetState = AddCandidateToStructuralBudget(structuralBudgetState, startingCandidate);

            ChunkData startData = startingChunkPrefab.GetComponent<ChunkData>();
            if (startData != null)
            {
                prev1 = startData.primaryTag;
                prev2 = startData.primaryTag;
                previousPrefabName = startingChunkPrefab.name;
                previousCandidateName = startingChunkPrefab.name;
                samePrimaryTagStreak = 1;
            }

            remainingChunks -= 1;
        }

        for (int i = 0; i < remainingChunks; i++)
        {
            if (chunkPrefabs == null || chunkPrefabs.Length == 0) break;

            ChunkSelectionCandidate candidate = SelectNextChunkCandidate(
                prev2,
                prev1,
                previousPrefabName,
                previousCandidateName,
                samePrimaryTagStreak,
                i,
                structuralBudgetState);
            if (candidate == null)
            {
                Debug.LogWarning("LevelGenerator: No valid next chunk prefab could be selected.");
                break;
            }

            sequence.Add(candidate);
            structuralBudgetState = AddCandidateToStructuralBudget(structuralBudgetState, candidate);

            ChunkTag selectedTag = candidate.PrimaryTag;
            if (selectedTag == prev1)
                samePrimaryTagStreak++;
            else
                samePrimaryTagStreak = 1;

            prev2 = prev1;
            prev1 = selectedTag;
            previousPrefabName = candidate.SourceName;
            previousCandidateName = candidate.DisplayName;
        }

        return sequence;
    }

    private void GenerateLevelFromSequence(List<ChunkSelectionCandidate> sequence)
    {
        // instantiates the planned sequence and snaps each entry to the previous exit.
        if (clearBeforeGenerate) ClearLevel();

        if (sequence == null || sequence.Count == 0)
        {
            Debug.LogWarning("LevelGenerator: Cannot generate level from empty sequence.");
            return;
        }

        Vector3 nextAttachPoint = (startPoint != null) ? startPoint.position : transform.position;
        bool hasFixedStartingChunk = startingChunkPrefab != null &&
                                     sequence.Count > 0 &&
                                     sequence[0] != null &&
                                     sequence[0].sourcePrefab == startingChunkPrefab;

        FirstEntryWorld = nextAttachPoint;
        LastExitWorld = nextAttachPoint;

        for (int i = 0; i < sequence.Count; i++)
        {
            ChunkSelectionCandidate candidate = sequence[i];
            if (candidate == null || candidate.sourcePrefab == null) continue;

            bool isStartingChunk = hasFixedStartingChunk && i == 0;
            int generatedSlotIndex = isStartingChunk ? -1 : (hasFixedStartingChunk ? i - 1 : i);
            LevelRunLog.SlotRecord slotRecord = CreateSelectedSlotRecord(candidate, i, generatedSlotIndex, isStartingChunk);

            SpawnedChunkResult spawnResult = SpawnChunkResultFromCandidate(candidate, i);
            slotRecord.spawnSucceeded = spawnResult.chunk != null;
            slotRecord.replacementAttempted = spawnResult.replacementAttempted;
            slotRecord.replacementSucceeded = spawnResult.replacementSucceeded;
            slotRecord.replacementMode = spawnResult.replacementMode;
            slotRecord.replacementReason = spawnResult.replacementReason;
            slotRecord.generatedRejectionReason = spawnResult.generatedRejectionReason;
            slotRecord.generatedBlueprintName = spawnResult.generatedBlueprintName;
            slotRecord.generatedBlueprintRows = spawnResult.generatedBlueprintRows;
            PopulateGeneratedBlueprintFeatureRecord(slotRecord, spawnResult.generatedBlueprintFeatures);

            GameObject chunk = spawnResult.chunk;
            if (chunk == null)
            {
                if (currentRunLog != null) currentRunLog.slots.Add(slotRecord);
                Debug.LogWarning($"LevelGenerator: Failed to spawn chunk for candidate '{candidate.DisplayName}'.");
                continue;
            }

            PopulateSpawnedSlotRecord(slotRecord, chunk);
            if (currentRunLog != null) currentRunLog.slots.Add(slotRecord);

            spawnedObjects.Add(chunk);
            CacheChunkDataIfPresent(chunk, slotRecord);

            if (!SnapChunkEntryToPoint(chunk, nextAttachPoint))
            {
                Debug.LogWarning($"LevelGenerator: Chunk '{chunk.name}' is missing Entry or Exit.");
                break;
            }

            Transform exit = FindChildByName(chunk.transform, "Exit");
            if (exit != null)
            {
                nextAttachPoint = exit.position;
                LastExitWorld = nextAttachPoint;
            }
            else
            {
                Debug.LogWarning($"LevelGenerator: Chunk '{chunk.name}' is missing Exit.");
                break;
            }
        }

        PopulateTransitionPressureRecords(currentRunLog);
        SpawnEndPointAt(LastExitWorld);
        RecalculateLevelBounds();
        RecalculateDifficultyStats();
    }

    private ChunkSelectionCandidate CreateHandcraftedCandidate(GameObject prefab)
    {
        return new ChunkSelectionCandidate
        {
            sourcePrefab = prefab,
            validationReason = "handcrafted",
            sourceFamilyWeight = 1f
        };
    }

    private SpawnedChunkResult SpawnChunkResultFromCandidate(ChunkSelectionCandidate candidate, int slotIndex)
    {
        if (candidate == null || candidate.sourcePrefab == null) return default;

        if (candidate.IsGenerated)
            return SpawnGeneratedCandidate(candidate);

        return SpawnChunkResultFromPrefabOrBlueprint(candidate.sourcePrefab, slotIndex);
    }

    private SpawnedChunkResult SpawnGeneratedCandidate(ChunkSelectionCandidate candidate)
    {
        if (candidate == null || candidate.sourcePrefab == null || candidate.blueprint == null)
            return default;

        GameObject generatedChunk = blueprintRuntimeBuilder != null
            ? blueprintRuntimeBuilder.BuildChunk(candidate.blueprint, Vector3.zero)
            : null;

        if (generatedChunk != null)
        {
            return new SpawnedChunkResult
            {
                chunk = generatedChunk,
                replacementAttempted = false,
                replacementSucceeded = false,
                replacementMode = "generated_candidate",
                replacementReason = "selected_validated_blueprint_candidate",
                generatedRejectionReason = "accepted",
                generatedBlueprintName = candidate.blueprint.chunkName,
                generatedBlueprintRows = ChunkBlueprintFeatureExtractor.RowsToInlineText(candidate.blueprint),
                generatedBlueprintFeatures = candidate.blueprintFeatures
            };
        }

        Debug.LogWarning($"LevelGenerator: Falling back to handcrafted prefab for generated candidate '{candidate.DisplayName}'.");
        return new SpawnedChunkResult
        {
            chunk = Instantiate(candidate.sourcePrefab, Vector3.zero, Quaternion.identity),
            replacementAttempted = false,
            replacementSucceeded = false,
            replacementMode = "generated_candidate_fallback",
            replacementReason = "selected_validated_blueprint_candidate",
            generatedRejectionReason = "runtime_builder_failed",
            generatedBlueprintName = candidate.blueprint.chunkName,
            generatedBlueprintRows = ChunkBlueprintFeatureExtractor.RowsToInlineText(candidate.blueprint),
            generatedBlueprintFeatures = candidate.blueprintFeatures
        };
    }

    private SpawnedChunkResult SpawnChunkResultFromPrefabOrBlueprint(GameObject prefab, int slotIndex)
    {
        if (prefab == null) return default;

        ChunkData cd = prefab.GetComponent<ChunkData>();
        bool replacementAttempted = ShouldUseGeneratedChunk(cd, slotIndex);

        if (!replacementAttempted)
        {
            return new SpawnedChunkResult
            {
                chunk = Instantiate(prefab, Vector3.zero, Quaternion.identity),
                replacementAttempted = false,
                replacementSucceeded = false,
                replacementMode = "none",
                replacementReason = "none",
                generatedRejectionReason = "none",
                generatedBlueprintName = string.Empty
            };
        }

        string generatedBlueprintName;
        string generatedBlueprintRows;
        ChunkBlueprintFeatures generatedBlueprintFeatures;
        string generatedRejectionReason;
        string replacementReason = GetGeneratedReplacementReason(cd);
        GameObject generatedChunk = TryBuildGeneratedChunkFromPrefab(
            prefab,
            cd,
            out generatedBlueprintName,
            out generatedBlueprintRows,
            out generatedBlueprintFeatures,
            out generatedRejectionReason);

        if (generatedChunk != null)
        {
            return new SpawnedChunkResult
            {
                chunk = generatedChunk,
                replacementAttempted = true,
                replacementSucceeded = true,
                replacementMode = "generated_success",
                replacementReason = replacementReason,
                generatedRejectionReason = generatedRejectionReason,
                generatedBlueprintName = generatedBlueprintName,
                generatedBlueprintRows = generatedBlueprintRows,
                generatedBlueprintFeatures = generatedBlueprintFeatures
            };
        }

        Debug.LogWarning($"LevelGenerator: Falling back to handcrafted prefab for '{prefab.name}'.");
        return new SpawnedChunkResult
        {
            chunk = Instantiate(prefab, Vector3.zero, Quaternion.identity),
            replacementAttempted = true,
            replacementSucceeded = false,
            replacementMode = "generated_fallback",
            replacementReason = replacementReason,
            generatedRejectionReason = generatedRejectionReason,
            generatedBlueprintName = generatedBlueprintName,
            generatedBlueprintRows = generatedBlueprintRows,
            generatedBlueprintFeatures = generatedBlueprintFeatures
        };
    }

    private string GetGeneratedReplacementReason(ChunkData cd)
    {
        if (cd == null)
            return "unknown_generated_replacement";

        return $"random_eligible_{cd.primaryTag.ToString().ToLowerInvariant()}_replacement";
    }

    private bool ShouldUseGeneratedChunk(ChunkData cd, int slotIndex)
    {
        if (!useGeneratedBlueprintChunks) return false;
        if (useGeneratedBlueprintCandidateSelection) return false;
        if (blueprintRuntimeBuilder == null) return false;
        if (cd == null) return false;

        // keep the first chunk stable and safe
        if (slotIndex == 0) return false;

        if (UnityEngine.Random.value > generatedChunkReplacementChance) return false;

        switch (cd.primaryTag)
        {
            case ChunkTag.Gap:
                return allowGeneratedGap;

            case ChunkTag.Precision:
                return allowGeneratedPrecision && IsElevatedPlatformPrecisionSource(cd);

            case ChunkTag.Vertical:
                return allowGeneratedVertical;

            case ChunkTag.Spikes:
                return allowGeneratedSpikes;

            case ChunkTag.Safe:
                return allowGeneratedSafeRest;

            default:
                return false;
        }
    }

    private bool IsElevatedPlatformPrecisionSource(ChunkData cd)
    {
        return cd != null && cd.gameObject.name.Contains("Chunk_ElevatedPlatform_Tilemap");
    }

    private GameObject TryBuildGeneratedChunkFromPrefab(
        GameObject prefab,
        ChunkData cd,
        out string generatedBlueprintName,
        out string generatedBlueprintRows,
        out ChunkBlueprintFeatures generatedBlueprintFeatures,
        out string generatedRejectionReason)
    {
        generatedBlueprintName = string.Empty;
        generatedBlueprintRows = string.Empty;
        generatedBlueprintFeatures = null;
        generatedRejectionReason = "not_attempted";

        if (cd == null) return null;

        ChunkGenerationRequest request = CreateGenerationRequest(prefab, cd);

        ChunkBlueprint blueprint = SimpleChunkBlueprintGenerator.Generate(request);
        if (blueprint == null)
        {
            Debug.LogWarning($"LevelGenerator: Generator returned null for '{prefab.name}'.");
            generatedRejectionReason = "generator_returned_null";
            return null;
        }

        generatedBlueprintName = blueprint.chunkName;
        generatedBlueprintRows = ChunkBlueprintFeatureExtractor.RowsToInlineText(blueprint);
        generatedBlueprintFeatures = ChunkBlueprintFeatureExtractor.Analyze(blueprint);

        ChunkBlueprintValidationResult validation = ChunkBlueprintValidator.Validate(blueprint);
        if (!validation.isValid)
        {
            Debug.LogWarning($"LevelGenerator: Generated blueprint invalid for '{prefab.name}'.");
            for (int i = 0; i < validation.errors.Count; i++)
            {
                Debug.LogWarning($"  Error {i + 1}: {validation.errors[i]}");
            }
            generatedRejectionReason = "validation_failed";
            return null;
        }

        if (!IsGeneratedReplacementAcceptable(cd, request, blueprint, generatedBlueprintFeatures, out generatedRejectionReason))
        {
            Debug.LogWarning($"LevelGenerator: Generated blueprint rejected for '{prefab.name}': {generatedRejectionReason}");
            return null;
        }

        GameObject built = blueprintRuntimeBuilder.BuildChunk(blueprint, Vector3.zero);
        if (built == null)
        {
            Debug.LogWarning($"LevelGenerator: Runtime builder failed for '{prefab.name}'.");
            generatedRejectionReason = "runtime_builder_failed";
            return null;
        }

        generatedRejectionReason = "accepted";
        return built;
    }

    private ChunkGenerationRequest CreateGenerationRequest(GameObject prefab, ChunkData cd)
    {
        if (cd == null)
            return null;

        return new ChunkGenerationRequest
        {
            requestedPrimaryTag = cd.primaryTag,
            targetDifficulty = cd.difficultyRating,
            requireHazard = cd.hasHazard,
            preferredWidth = GetPreferredWidthForTag(cd),
            preferredHeight = GetPreferredHeightForTag(cd),
            hasSourceContext = true,
            sourceChunkName = prefab != null ? prefab.name : string.Empty,
            sourceDifficulty = cd.difficultyRating,
            sourceHasHazard = cd.hasHazard,
            sourceEstimatedJumps = cd.estimatedJumps,
            sourceExitDelta = cd.exitDelta,
            sourceMaxGapWidth = ChunkBlueprintFeatureExtractor.EstimateSourceMaxGapWidth(prefab)
        };
    }

    private bool IsGeneratedReplacementAcceptable(
        ChunkData source,
        ChunkGenerationRequest request,
        ChunkBlueprint generated,
        ChunkBlueprintFeatures features,
        out string rejectionReason)
    {
        rejectionReason = "accepted";

        if (source == null || generated == null || features == null)
        {
            rejectionReason = "missing_source_or_generated_data";
            return false;
        }

        if (generated.primaryTag != source.primaryTag)
        {
            rejectionReason = $"primary_tag_mismatch:{source.primaryTag}->{generated.primaryTag}";
            return false;
        }

        bool controlledHazardAccent = IsGeneratedGapHazardAccent(source, generated);

        if (generated.hasHazard != source.hasHazard && !controlledHazardAccent)
        {
            rejectionReason = $"hazard_mismatch:{source.hasHazard}->{generated.hasHazard}";
            return false;
        }

        int difficultyDelta = generated.difficultyRating - source.difficultyRating;
        if (Mathf.Abs(difficultyDelta) > 0 &&
            !IsGeneratedSafeRestDifficultyEquivalent(source, generated) &&
            !controlledHazardAccent)
        {
            rejectionReason = $"difficulty_delta:{difficultyDelta:+#;-#;0}";
            return false;
        }

        int jumpsDelta = generated.estimatedJumps - source.estimatedJumps;
        if (Mathf.Abs(jumpsDelta) > 1)
        {
            rejectionReason = $"jump_delta:{jumpsDelta:+#;-#;0}";
            return false;
        }

        if (request != null && request.sourceMaxGapWidth > 0 && generated.primaryTag == ChunkTag.Gap)
        {
            int gapDelta = features.maxGapWidth - request.sourceMaxGapWidth;
            if (gapDelta != 0)
            {
                rejectionReason = $"max_gap_delta:{gapDelta:+#;-#;0}";
                return false;
            }
        }

        Vector2 exitDeltaDiff = features.estimatedExitDelta - source.exitDelta;
        const float horizontalExitDeltaTolerance = 1.25f;
        float verticalExitDeltaTolerance = Mathf.Max(
            GetGeneratedGapVerticalExitDeltaTolerance(request, generated),
            Mathf.Max(
                GetGeneratedSafeVerticalExitDeltaTolerance(request, generated),
                GetGeneratedPrecisionVerticalExitDeltaTolerance(request, generated)));
        if (Mathf.Abs(exitDeltaDiff.x) > horizontalExitDeltaTolerance || Mathf.Abs(exitDeltaDiff.y) > verticalExitDeltaTolerance)
        {
            rejectionReason = $"exit_delta_mismatch:({exitDeltaDiff.x:+0.##;-0.##;0},{exitDeltaDiff.y:+0.##;-0.##;0})";
            return false;
        }

        return true;
    }

    private bool IsGeneratedGapHazardAccent(ChunkData source, ChunkBlueprint generated)
    {
        if (source == null || generated == null)
            return false;

        return source.primaryTag == ChunkTag.Gap &&
               generated.primaryTag == ChunkTag.Gap &&
               !source.hasHazard &&
               generated.hasHazard &&
               generated.chunkName.StartsWith("Generated_GapHazard_") &&
               generated.difficultyRating == source.difficultyRating &&
               generated.estimatedJumps == source.estimatedJumps + 1;
    }

    private float GetGeneratedGapVerticalExitDeltaTolerance(ChunkGenerationRequest request, ChunkBlueprint generated)
    {
        if (request != null &&
            generated != null &&
            generated.primaryTag == ChunkTag.Gap &&
            request.sourceMaxGapWidth > 0 &&
            request.sourceMaxGapWidth <= 4)
        {
            return 2.25f;
        }

        return 1.25f;
    }

    private bool IsGeneratedSafeRestDifficultyEquivalent(ChunkData source, ChunkBlueprint generated)
    {
        if (source == null || generated == null)
            return false;

        bool controlledRiseVariant =
            generated.chunkName == "Generated_Safe_RiseRest_Box2" ||
            generated.chunkName == "Generated_Safe_RiseRest_Box3";

        return controlledRiseVariant &&
               source.primaryTag == ChunkTag.Safe &&
               generated.primaryTag == ChunkTag.Safe &&
               !source.hasHazard &&
               source.estimatedJumps == 0 &&
               generated.difficultyRating == source.difficultyRating + 1;
    }

    private float GetGeneratedSafeVerticalExitDeltaTolerance(ChunkGenerationRequest request, ChunkBlueprint generated)
    {
        if (request != null &&
            generated != null &&
            generated.primaryTag == ChunkTag.Safe &&
            generated.chunkName.StartsWith("Generated_Safe_") &&
            request.hasSourceContext &&
            request.sourceEstimatedJumps == 0 &&
            !request.sourceHasHazard)
        {
            return 4.25f;
        }

        return 1.25f;
    }

    private float GetGeneratedPrecisionVerticalExitDeltaTolerance(ChunkGenerationRequest request, ChunkBlueprint generated)
    {
        if (request != null &&
            generated != null &&
            generated.primaryTag == ChunkTag.Precision &&
            generated.chunkName.StartsWith("Generated_Precision_ElevatedPlatform_") &&
            request.hasSourceContext &&
            request.sourceEstimatedJumps == 2 &&
            !request.sourceHasHazard)
        {
            return 2.25f;
        }

        return 1.25f;
    }

    private LevelRunLog.SlotRecord CreateSelectedSlotRecord(ChunkSelectionCandidate candidate, int sequenceIndex, int generatedSlotIndex, bool isStartingChunk)
    {
        LevelRunLog.SlotRecord record = new LevelRunLog.SlotRecord
        {
            sequenceIndex = sequenceIndex,
            generatedSlotIndex = generatedSlotIndex,
            isStartingChunk = isStartingChunk,
            selectedCandidateType = candidate != null ? candidate.CandidateType : "missing",
            replacementMode = "none",
            replacementReason = "none",
            generatedRejectionReason = "none"
        };

        if (!isStartingChunk && generatedSlotIndex >= 0)
        {
            record.hasSlotTargetDifficulty = true;
            record.slotTargetDifficulty = GetSlotTargetDifficultyForGeneratedSlotIndex(generatedSlotIndex);
        }

        if (candidate == null || candidate.sourcePrefab == null)
        {
            record.selectedPrefabName = "MissingPrefab";
            return record;
        }

        record.selectedPrefabName = LevelRunLog.CleanName(candidate.DisplayName);
        record.selectedSourcePrefabName = LevelRunLog.CleanName(candidate.SourceName);
        record.selectedGeneratedBlueprintName = candidate.IsGenerated ? candidate.blueprint.chunkName : string.Empty;
        record.selectedPrimaryTag = candidate.PrimaryTag.ToString();
        record.selectedDifficulty = candidate.Difficulty;
        record.selectedHasHazard = candidate.HasHazard;
        record.selectedEstimatedJumps = candidate.EstimatedJumps;
        record.selectedExitDelta = candidate.ExitDelta;
        record.lookaheadUsed = candidate.lookaheadUsed;
        record.lookaheadDepthUsed = candidate.lookaheadDepthUsed;
        record.lookaheadBestScore = candidate.lookaheadBestScore;
        record.lookaheadSelectionWeight = candidate.lookaheadSelectionWeight;
        record.lookaheadDecisionSummary = candidate.lookaheadDecisionSummary;
        record.structuralBudgetWeight = candidate.structuralBudgetWeight;
        record.structuralBudgetProjectedLoad = candidate.structuralBudgetProjectedLoad;
        record.structuralBudgetAllowedLoad = candidate.structuralBudgetAllowedLoad;

        return record;
    }

    private void PopulateSpawnedSlotRecord(LevelRunLog.SlotRecord slotRecord, GameObject chunk)
    {
        if (slotRecord == null || chunk == null)
            return;

        slotRecord.spawnedChunkName = LevelRunLog.CleanName(chunk.name);

        ChunkData spawnedData = chunk.GetComponent<ChunkData>();
        if (spawnedData == null)
            return;

        slotRecord.spawnedPrimaryTag = spawnedData.primaryTag.ToString();
        slotRecord.spawnedDifficulty = spawnedData.difficultyRating;
        slotRecord.spawnedHasHazard = spawnedData.hasHazard;
        slotRecord.spawnedEstimatedJumps = spawnedData.estimatedJumps;
        slotRecord.spawnedExitDelta = spawnedData.exitDelta;
    }

    private void PopulateGeneratedBlueprintFeatureRecord(LevelRunLog.SlotRecord slotRecord, ChunkBlueprintFeatures features)
    {
        if (slotRecord == null || features == null)
            return;

        slotRecord.generatedBlueprintFeatureSummary = features.ToSummary();
        slotRecord.generatedBlueprintWidth = features.width;
        slotRecord.generatedBlueprintHeight = features.height;
        slotRecord.generatedBlueprintGapCount = features.gapCount;
        slotRecord.generatedBlueprintMaxGapWidth = features.maxGapWidth;
        slotRecord.generatedBlueprintMinLandingWidth = features.minLandingWidth;
        slotRecord.generatedBlueprintSolidCount = features.solidCount;
        slotRecord.generatedBlueprintHazardCount = features.hazardCount;
        slotRecord.generatedBlueprintEstimatedExitDelta = features.estimatedExitDelta;
    }

    private void PopulateTransitionPressureRecords(LevelRunLog.RunRecord runRecord)
    {
        // records risky local transitions after the final spawned sequence is known.
        if (runRecord == null || runRecord.slots == null)
            return;

        runRecord.transitionPressureCount = 0;
        runRecord.highPressureTransitionCount = 0;
        runRecord.transitionPressureScore = 0f;

        for (int i = 0; i < runRecord.slots.Count; i++)
        {
            LevelRunLog.SlotRecord current = runRecord.slots[i];
            if (current == null)
                continue;

            current.hasPreviousTransition = false;
            current.previousSpawnedChunkName = string.Empty;
            current.transitionPressureMultiplier = 1f;
            current.transitionPressurePenalized = false;
            current.transitionPressureReason = "none";
            current.transitionPressureSeverity = "none";
            current.transitionPressureScore = 0f;

            if (i == 0)
                continue;

            LevelRunLog.SlotRecord previous = runRecord.slots[i - 1];
            if (previous == null)
                continue;

            string previousName = GetEffectiveSlotChunkName(previous);
            string currentName = GetEffectiveSlotChunkName(current);
            if (string.IsNullOrEmpty(previousName) || string.IsNullOrEmpty(currentName))
                continue;

            ChunkTag previousTag = GetEffectiveSlotChunkTag(previous);
            ChunkTag currentTag = GetEffectiveSlotChunkTag(current);
            int currentDifficulty = GetEffectiveSlotDifficulty(current);
            float slotTarget = current.hasSlotTargetDifficulty ? current.slotTargetDifficulty : targetDifficulty;

            float multiplier = ChunkTransitionPressure.GetSelectionWeightMultiplier(
                previousName,
                previousTag,
                currentName,
                currentTag,
                currentDifficulty,
                slotTarget,
                targetDifficulty);

            string reason = ChunkTransitionPressure.GetTransitionReason(
                previousName,
                previousTag,
                currentName,
                currentTag,
                currentDifficulty,
                slotTarget,
                targetDifficulty);

            string severity = ChunkTransitionPressure.GetSeverityFromMultiplier(multiplier);
            float pressureScore = ChunkTransitionPressure.GetPressureScoreFromMultiplier(multiplier);

            current.hasPreviousTransition = true;
            current.previousSpawnedChunkName = previousName;
            current.transitionPressureMultiplier = multiplier;
            current.transitionPressurePenalized = pressureScore > 0f;
            current.transitionPressureReason = reason;
            current.transitionPressureSeverity = severity;
            current.transitionPressureScore = pressureScore;

            if (pressureScore > 0f)
            {
                runRecord.transitionPressureCount++;
                runRecord.transitionPressureScore += pressureScore;

                if (severity == "strong" || severity == "severe")
                    runRecord.highPressureTransitionCount++;
            }
        }
    }

    private string GetEffectiveSlotChunkName(LevelRunLog.SlotRecord slot)
    {
        if (slot == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(slot.spawnedChunkName))
            return slot.spawnedChunkName;

        return slot.selectedPrefabName ?? string.Empty;
    }

    private ChunkTag GetEffectiveSlotChunkTag(LevelRunLog.SlotRecord slot)
    {
        if (slot == null)
            return ChunkTag.Rest;

        if (TryParseChunkTag(slot.spawnedPrimaryTag, out ChunkTag spawnedTag))
            return spawnedTag;

        if (TryParseChunkTag(slot.selectedPrimaryTag, out ChunkTag selectedTag))
            return selectedTag;

        return ChunkTag.Rest;
    }

    private int GetEffectiveSlotDifficulty(LevelRunLog.SlotRecord slot)
    {
        if (slot == null)
            return 0;

        if (slot.spawnedDifficulty >= 0)
            return slot.spawnedDifficulty;

        return Mathf.Max(0, slot.selectedDifficulty);
    }

    private bool TryParseChunkTag(string value, out ChunkTag tag)
    {
        if (!string.IsNullOrEmpty(value))
            return Enum.TryParse(value, out tag);

        tag = ChunkTag.Rest;
        return false;
    }

    private float GetSlotTargetDifficultyForGeneratedSlotIndex(int generatedSlotIndex)
    {
        float progress = Mathf.Clamp01((generatedSlotIndex + 1f) / Mathf.Max(1, totalChunks - 1));
        return Mathf.Lerp(startDifficultyBias, targetDifficulty, progress);
    }

    private int GetPreferredWidthForTag(ChunkData cd)
    {
        if (cd == null) return 8;

        switch (cd.primaryTag)
        {
            case ChunkTag.Gap:
                return 8;

            case ChunkTag.Precision:
                return 8;

            case ChunkTag.Vertical:
                return 5;

            case ChunkTag.Spikes:
                return 6;

            case ChunkTag.Safe:
            case ChunkTag.Rest:
                return 6;

            default:
                return 8;
        }
    }

    private int GetPreferredHeightForTag(ChunkData cd)
    {
        if (cd == null) return 3;

        switch (cd.primaryTag)
        {
            case ChunkTag.Gap:
            case ChunkTag.Precision:
            case ChunkTag.Spikes:
            case ChunkTag.Vertical:
                return 3;

            case ChunkTag.Safe:
            case ChunkTag.Rest:
                return 2;

            default:
                return 3;
        }
    }

    private ChunkSelectionCandidate SelectNextChunkCandidate(
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        string previousCandidateName,
        int samePrimaryTagStreak,
        int slotIndex,
        StructuralBudgetState structuralBudgetState)
    {
        // switches between baseline random generation and constrained planning.
        if (generationMode == LevelGenerationMode.NaiveRandom)
            return SelectNaiveRandomCandidate(prev2, prev1, previousPrefabName, samePrimaryTagStreak);

        List<ChunkSelectionCandidate> candidates = GetSelectableCandidates(
            prev2,
            prev1,
            previousPrefabName,
            previousCandidateName,
            samePrimaryTagStreak,
            slotIndex);

        if (candidates.Count == 0)
            return null;

        int plannedGeneratedSlots = Mathf.Max(1, totalChunks - (startingChunkPrefab != null ? 1 : 0));
        int remainingSlots = Mathf.Max(1, plannedGeneratedSlots - slotIndex);
        if (useLookaheadSequencePlanning && lookaheadDepth > 1 && remainingSlots > 1)
        {
            ChunkSelectionCandidate lookaheadCandidate = SelectChunkWithLookahead(
                candidates,
                prev2,
                prev1,
                previousPrefabName,
                previousCandidateName,
                samePrimaryTagStreak,
                slotIndex,
                remainingSlots,
                structuralBudgetState);

            if (lookaheadCandidate != null)
                return lookaheadCandidate;
        }

        return SelectWeightedImmediateCandidate(candidates, prev2, prev1, previousPrefabName, slotIndex, structuralBudgetState);
    }

    private List<ChunkSelectionCandidate> GetSelectableCandidates(
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        string previousCandidateName,
        int samePrimaryTagStreak,
        int slotIndex)
    {
        // starts strict, then relaxes repeat rules only if no candidate survives.
        List<ChunkSelectionCandidate> candidates = GetCandidates(prev2, prev1, previousPrefabName, previousCandidateName, samePrimaryTagStreak, true, true);

        if (candidates.Count == 0)
            candidates = GetCandidates(prev2, prev1, previousPrefabName, previousCandidateName, samePrimaryTagStreak, false, true);

        if (candidates.Count == 0)
            candidates = GetCandidates(prev2, prev1, previousPrefabName, previousCandidateName, samePrimaryTagStreak, false, false);

        if (candidates.Count == 0)
            return candidates;

        float slotTargetDifficulty = GetSlotTargetDifficultyForGeneratedSlotIndex(slotIndex);
        candidates = ApplyExtremeOutlierEligibility(candidates, slotTargetDifficulty, targetDifficulty, slotIndex);
        return candidates;
    }

    private ChunkSelectionCandidate SelectNaiveRandomCandidate(
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        int samePrimaryTagStreak)
    {
        // evaluation baseline: random choice after basic hard constraints.
        List<ChunkSelectionCandidate> candidates = GetCandidates(
            prev2,
            prev1,
            previousPrefabName,
            string.Empty,
            samePrimaryTagStreak,
            enforceExactRepeatRule: false,
            enforceTagStreakRule: false);

        if (candidates.Count == 0)
            return null;

        ChunkSelectionCandidate selected = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        selected.lookaheadUsed = false;
        selected.lookaheadDepthUsed = 1;
        selected.lookaheadBestScore = 0f;
        selected.lookaheadSelectionWeight = 0f;
        selected.lookaheadDecisionSummary = $"naive_random_basic_constraints_options_{candidates.Count}";
        selected.structuralBudgetWeight = 1f;
        selected.structuralBudgetProjectedLoad = 0f;
        selected.structuralBudgetAllowedLoad = 0f;
        return selected;
    }

    private ChunkSelectionCandidate SelectWeightedImmediateCandidate(
        List<ChunkSelectionCandidate> candidates,
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        int slotIndex,
        StructuralBudgetState structuralBudgetState)
    {
        float totalWeight = 0f;
        List<float> weights = new List<float>(candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            ChunkSelectionCandidate candidate = candidates[i];
            float finalWeight = CalculateCandidateSelectionWeight(
                candidate,
                prev2,
                prev1,
                previousPrefabName,
                slotIndex,
                structuralBudgetState);
            weights.Add(finalWeight);
            totalWeight += finalWeight;
        }

        ChunkSelectionCandidate selected = SelectWeightedCandidate(candidates, weights, totalWeight);
        if (selected != null)
        {
            selected.lookaheadUsed = false;
            selected.lookaheadDepthUsed = 1;
            selected.lookaheadBestScore = 0f;
            selected.lookaheadSelectionWeight = 0f;
            selected.lookaheadDecisionSummary = "immediate_weighted_selection";
        }

        return selected;
    }

    private ChunkSelectionCandidate SelectWeightedCandidate(
        List<ChunkSelectionCandidate> candidates,
        List<float> weights,
        float totalWeight)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        if (weights == null || weights.Count != candidates.Count || totalWeight <= 0f)
            return candidates[candidates.Count - 1];

        float roll = UnityEngine.Random.value * totalWeight;
        float running = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            running += weights[i];
            if (roll <= running)
                return candidates[i];
        }

        return candidates[candidates.Count - 1];
    }

    private ChunkSelectionCandidate SelectChunkWithLookahead(
        List<ChunkSelectionCandidate> currentCandidates,
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        string previousCandidateName,
        int samePrimaryTagStreak,
        int slotIndex,
        int remainingSlots,
        StructuralBudgetState structuralBudgetState)
    {
        // bounded beam lookahead checks whether immediate choices lead to good follow-ups.
        int depth = Mathf.Clamp(lookaheadDepth, 1, Mathf.Min(3, remainingSlots));
        int beamWidth = Mathf.Max(1, lookaheadBeamWidth);

        if (depth <= 1 || currentCandidates == null || currentCandidates.Count == 0)
            return null;

        List<LookaheadSequenceState> beam = new List<LookaheadSequenceState>(currentCandidates.Count);
        List<LookaheadCandidateChoice> immediateChoices = new List<LookaheadCandidateChoice>(currentCandidates.Count);
        for (int i = 0; i < currentCandidates.Count; i++)
        {
            ChunkSelectionCandidate candidate = currentCandidates[i];
            float weight = CalculateCandidateSelectionWeight(
                candidate,
                prev2,
                prev1,
                previousPrefabName,
                slotIndex,
                structuralBudgetState);
            float immediateScore = Mathf.Log(Mathf.Max(0.0001f, weight));
            immediateChoices.Add(new LookaheadCandidateChoice
            {
                candidate = candidate,
                score = immediateScore,
                selectionWeight = 0f,
                depthReached = 1
            });
            beam.Add(CreateAdvancedLookaheadState(
                candidate,
                prev2,
                prev1,
                previousPrefabName,
                previousCandidateName,
                samePrimaryTagStreak,
                structuralBudgetState,
                0f,
                candidate,
                weight,
                1));
        }

        SortAndTrimLookaheadBeam(beam, Mathf.Max(beamWidth, Mathf.Min(currentCandidates.Count, beamWidth * 3)));

        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
        try
        {
            for (int depthIndex = 1; depthIndex < depth; depthIndex++)
            {
                int futureSlotIndex = slotIndex + depthIndex;
                List<LookaheadSequenceState> expanded = new List<LookaheadSequenceState>();

                for (int i = 0; i < beam.Count; i++)
                {
                    LookaheadSequenceState state = beam[i];
                    List<ChunkSelectionCandidate> futureCandidates = GetSelectableCandidates(
                        state.prev2,
                        state.prev1,
                        state.previousPrefabName,
                        state.previousCandidateName,
                        state.samePrimaryTagStreak,
                        futureSlotIndex);

                    if (futureCandidates.Count == 0)
                    {
                        expanded.Add(state);
                        continue;
                    }

                    for (int c = 0; c < futureCandidates.Count; c++)
                    {
                        ChunkSelectionCandidate futureCandidate = futureCandidates[c];
                        float futureWeight = CalculateCandidateSelectionWeight(
                            futureCandidate,
                            state.prev2,
                            state.prev1,
                            state.previousPrefabName,
                            futureSlotIndex,
                            state.structuralBudgetState);

                        expanded.Add(CreateAdvancedLookaheadState(
                            state.firstCandidate,
                            state.prev2,
                            state.prev1,
                            state.previousPrefabName,
                            state.previousCandidateName,
                            state.samePrimaryTagStreak,
                            state.structuralBudgetState,
                            state.cumulativeLogScore,
                            futureCandidate,
                            futureWeight,
                            state.selectedCount + 1));
                    }
                }

                if (expanded.Count == 0)
                    break;

                SortAndTrimLookaheadBeam(expanded, beamWidth);
                beam = expanded;
            }
        }
        finally
        {
            UnityEngine.Random.state = previousRandomState;
        }

        if (beam.Count == 0)
            return null;

        List<LookaheadCandidateChoice> choices = BuildLookaheadChoices(immediateChoices, beam);
        if (choices.Count == 0)
            return null;

        float maxScore = choices[0].score;
        for (int i = 1; i < choices.Count; i++)
            maxScore = Mathf.Max(maxScore, choices[i].score);

        float totalWeight = 0f;
        for (int i = 0; i < choices.Count; i++)
        {
            LookaheadCandidateChoice choice = choices[i];
            choice.selectionWeight = Mathf.Max(0.01f, Mathf.Exp(Mathf.Clamp(choice.score - maxScore, -20f, 0f)));
            choices[i] = choice;
            totalWeight += choice.selectionWeight;
        }

        float roll = UnityEngine.Random.value * totalWeight;
        float running = 0f;
        for (int i = 0; i < choices.Count; i++)
        {
            running += choices[i].selectionWeight;
            if (roll <= running)
                return ApplyLookaheadAudit(choices[i], depth, beamWidth, choices.Count);
        }

        return ApplyLookaheadAudit(choices[choices.Count - 1], depth, beamWidth, choices.Count);
    }

    private float CalculateCandidateSelectionWeight(
        ChunkSelectionCandidate candidate,
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        int slotIndex,
        StructuralBudgetState structuralBudgetState)
    {
        // combines difficulty fit, pressure, budget, markov, variety, and pacing.
        if (candidate == null || candidate.sourcePrefab == null)
            return 0.01f;

        float slotTargetDifficulty = GetSlotTargetDifficultyForGeneratedSlotIndex(slotIndex);
        DifficultyBand band = MarkovWeightTable.GetBandForDifficulty(targetDifficulty);

        float transitionWeight = useTwoStepMarkov
            ? GetMarkovWeight(prev2, prev1, candidate.PrimaryTag, band)
            : 1f;

        if (transitionWeight <= 0f)
            return 0.01f;

        float diff = Mathf.Abs(candidate.Difficulty - slotTargetDifficulty);
        float difficultyWeight = Mathf.Exp(-difficultyPreferenceStrength * diff * diff);

        float varietyWeight = 1f;
        if (candidate.PrimaryTag != prev1)
            varietyWeight += varietyBonus;

        float pacingWeight = 1f;
        bool isHardType = candidate.PrimaryTag == ChunkTag.Spikes || candidate.PrimaryTag == ChunkTag.Precision;
        if (slotIndex < 2 && isHardType)
            pacingWeight *= earlyHardPenalty;

        if (candidate.Difficulty > slotTargetDifficulty + 2f)
            pacingWeight *= 0.65f;

        float extremeOutlierWeight = GetExtremeOutlierDifficultyWeight(
            candidate,
            slotTargetDifficulty,
            targetDifficulty);

        float transitionPressureWeight = ChunkTransitionPressure.GetSelectionWeightMultiplier(
            previousPrefabName,
            prev1,
            candidate.DisplayName,
            candidate.PrimaryTag,
            candidate.Difficulty,
            slotTargetDifficulty,
            targetDifficulty);

        float structuralBudgetWeight = GetStructuralBudgetWeight(
            candidate,
            structuralBudgetState,
            out float projectedStructuralLoad,
            out float allowedStructuralLoad);

        candidate.structuralBudgetWeight = structuralBudgetWeight;
        candidate.structuralBudgetProjectedLoad = projectedStructuralLoad;
        candidate.structuralBudgetAllowedLoad = allowedStructuralLoad;

        float finalWeight = transitionWeight * difficultyWeight * varietyWeight * pacingWeight *
                            extremeOutlierWeight * transitionPressureWeight * structuralBudgetWeight *
                            candidate.sourceFamilyWeight;
        return Mathf.Max(0.01f, finalWeight);
    }

    private LookaheadSequenceState CreateAdvancedLookaheadState(
        ChunkSelectionCandidate firstCandidate,
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        string previousCandidateName,
        int samePrimaryTagStreak,
        StructuralBudgetState structuralBudgetState,
        float cumulativeLogScore,
        ChunkSelectionCandidate selected,
        float selectedWeight,
        int selectedCount)
    {
        ChunkTag selectedTag = selected != null ? selected.PrimaryTag : ChunkTag.Rest;
        int nextStreak = selectedTag == prev1 ? samePrimaryTagStreak + 1 : 1;

        return new LookaheadSequenceState
        {
            firstCandidate = firstCandidate,
            prev2 = prev1,
            prev1 = selectedTag,
            previousPrefabName = selected != null ? selected.SourceName : previousPrefabName,
            previousCandidateName = selected != null ? selected.DisplayName : previousCandidateName,
            samePrimaryTagStreak = nextStreak,
            structuralBudgetState = AddCandidateToStructuralBudget(structuralBudgetState, selected),
            cumulativeLogScore = cumulativeLogScore + Mathf.Log(Mathf.Max(0.0001f, selectedWeight)),
            selectedCount = selectedCount
        };
    }

    private void SortAndTrimLookaheadBeam(List<LookaheadSequenceState> beam, int beamWidth)
    {
        if (beam == null)
            return;

        beam.Sort((a, b) => b.cumulativeLogScore.CompareTo(a.cumulativeLogScore));

        int maxCount = Mathf.Max(1, beamWidth);
        if (beam.Count > maxCount)
            beam.RemoveRange(maxCount, beam.Count - maxCount);
    }

    private List<LookaheadCandidateChoice> BuildLookaheadChoices(
        List<LookaheadCandidateChoice> immediateChoices,
        List<LookaheadSequenceState> beam)
    {
        List<LookaheadCandidateChoice> choices = new List<LookaheadCandidateChoice>();

        for (int i = 0; i < immediateChoices.Count; i++)
        {
            ChunkSelectionCandidate candidate = immediateChoices[i].candidate;
            float bestScore = immediateChoices[i].score;
            int bestDepth = immediateChoices[i].depthReached;

            for (int b = 0; b < beam.Count; b++)
            {
                LookaheadSequenceState state = beam[b];
                if (!ReferenceEquals(state.firstCandidate, candidate))
                    continue;

                float averageScore = state.cumulativeLogScore / Mathf.Max(1, state.selectedCount);
                if (averageScore > bestScore)
                {
                    bestScore = averageScore;
                    bestDepth = state.selectedCount;
                }
            }

            choices.Add(new LookaheadCandidateChoice
            {
                candidate = candidate,
                score = bestScore,
                depthReached = bestDepth,
                selectionWeight = 0f
            });
        }

        choices.Sort((a, b) => b.score.CompareTo(a.score));
        return choices;
    }

    private ChunkSelectionCandidate ApplyLookaheadAudit(
        LookaheadCandidateChoice choice,
        int depth,
        int beamWidth,
        int optionCount)
    {
        ChunkSelectionCandidate candidate = choice.candidate;
        if (candidate == null)
            return null;

        candidate.lookaheadUsed = true;
        candidate.lookaheadDepthUsed = choice.depthReached;
        candidate.lookaheadBestScore = choice.score;
        candidate.lookaheadSelectionWeight = choice.selectionWeight;
        candidate.lookaheadDecisionSummary = $"bounded_lookahead_depth_{depth}_beam_{beamWidth}_options_{optionCount}";
        return candidate;
    }

    private List<ChunkSelectionCandidate> ApplyExtremeOutlierEligibility(
        List<ChunkSelectionCandidate> candidates,
        float slotTargetDifficulty,
        float levelTargetDifficulty,
        int slotIndex)
    {
        if (candidates == null || candidates.Count == 0)
            return candidates;

        List<ChunkSelectionCandidate> eligible = new List<ChunkSelectionCandidate>(candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            ChunkSelectionCandidate candidate = candidates[i];
            if (candidate == null)
                continue;

            if (!IsExtremeOutlierDisallowed(candidate, slotTargetDifficulty, levelTargetDifficulty, slotIndex))
                eligible.Add(candidate);
        }

        return eligible.Count > 0 ? eligible : candidates;
    }

    private bool IsExtremeOutlierDisallowed(
        ChunkSelectionCandidate candidate,
        float slotTargetDifficulty,
        float levelTargetDifficulty,
        int slotIndex)
    {
        if (candidate == null)
            return false;

        bool extremeForSlot = candidate.Difficulty >= slotTargetDifficulty + 4f;
        int lateSlotStart = Mathf.Max(0, totalChunks - 3);
        bool beforeLateSlots = slotIndex < lateSlotStart;
        bool extremeEarlyForLevel = levelTargetDifficulty <= 5f && candidate.Difficulty >= 8 && beforeLateSlots;

        return extremeForSlot || extremeEarlyForLevel;
    }

    private float GetExtremeOutlierDifficultyWeight(ChunkSelectionCandidate candidate, float slotTargetDifficulty, float levelTargetDifficulty)
    {
        if (candidate == null)
            return 1f;

        bool extremeForLevel = levelTargetDifficulty <= 5.5f && candidate.Difficulty >= 8;
        bool extremeForSlot = candidate.Difficulty >= slotTargetDifficulty + 3f;

        return extremeForLevel || extremeForSlot ? 0.35f : 1f;
    }

    private float GetStructuralBudgetWeight(
        ChunkSelectionCandidate candidate,
        StructuralBudgetState currentState,
        out float projectedStructuralLoad,
        out float allowedStructuralLoad)
    {
        // softly reduces candidates that would overload the whole level structure.
        StructuralBudgetState projectedState = AddCandidateToStructuralBudget(currentState, candidate);
        projectedStructuralLoad = GetStructuralAdditiveLoad(projectedState);
        allowedStructuralLoad = GetAllowedStructuralAdditiveLoad(projectedState.selectedChunks);

        if (!useStructuralBudgetPenalty || structuralBudgetPenaltyStrength <= 0f)
            return 1f;

        float excess = projectedStructuralLoad - allowedStructuralLoad;
        if (excess <= 0f)
            return 1f;

        float penalty = Mathf.Exp(-structuralBudgetPenaltyStrength * excess * excess);
        return Mathf.Clamp(penalty, 0.2f, 1f);
    }

    private StructuralBudgetState AddCandidateToStructuralBudget(
        StructuralBudgetState state,
        ChunkSelectionCandidate candidate)
    {
        if (candidate == null)
            return state;

        state.selectedChunks++;

        bool hasHazard = candidate.HasHazard;
        bool hasSpikeTag = candidate.HasTag(ChunkTag.Spikes);
        if (hasHazard || hasSpikeTag)
            state.hazardChunks++;

        state.estimatedJumps += Mathf.Max(0, candidate.EstimatedJumps);

        if (candidate.PrimaryTag == ChunkTag.Vertical || candidate.HasTag(ChunkTag.Vertical))
            state.verticalChunks++;

        return state;
    }

    private float GetStructuralAdditiveLoad(StructuralBudgetState state)
    {
        return (wHazardChunk * state.hazardChunks) +
               (wEstimatedJump * state.estimatedJumps) +
               (wVerticalChunk * state.verticalChunks);
    }

    private float GetAllowedStructuralAdditiveLoad(int projectedSelectedChunks)
    {
        int configuredChunks = Mathf.Max(1, totalChunks);
        float progress = Mathf.Clamp01(projectedSelectedChunks / (float)configuredChunks);
        float plannedAverageDifficulty = GetPlannedAverageChunkDifficultyTarget();
        float finalBudget = Mathf.Max(0.5f, targetDifficulty - plannedAverageDifficulty + structuralBudgetSlack);

        // early slack gives the planner room to balance later lower-pressure chunks
        float earlySlack = structuralBudgetSlack * (1f - progress);
        return (finalBudget * progress) + earlySlack;
    }

    private float GetPlannedAverageChunkDifficultyTarget()
    {
        int plannedChunks = 0;
        float sum = 0f;

        if (startingChunkPrefab != null)
        {
            ChunkData startData = startingChunkPrefab.GetComponent<ChunkData>();
            sum += startData != null ? startData.difficultyRating : startDifficultyBias;
            plannedChunks++;
        }

        int generatedSlots = Mathf.Max(0, totalChunks - plannedChunks);
        for (int i = 0; i < generatedSlots; i++)
            sum += GetSlotTargetDifficultyForGeneratedSlotIndex(i);

        plannedChunks += generatedSlots;
        return plannedChunks > 0 ? sum / plannedChunks : targetDifficulty;
    }

    private List<ChunkSelectionCandidate> GetCandidates(
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        string previousCandidateName,
        int samePrimaryTagStreak,
        bool enforceExactRepeatRule,
        bool enforceTagStreakRule)
    {
        List<ChunkSelectionCandidate> candidates = new List<ChunkSelectionCandidate>();

        for (int i = 0; i < chunkPrefabs.Length; i++)
        {
            GameObject prefab = chunkPrefabs[i];
            if (prefab == null) continue;

            ChunkData cd = prefab.GetComponent<ChunkData>();
            if (cd == null) continue;

            if (!IsHardConstraintAllowed(prev2, prev1, cd.primaryTag))
                continue;

            if (IsBlockedVerticalOppositePair(previousPrefabName, prefab.name))
                continue;

            if (IsBlockedConstrainedGeometryPair(previousCandidateName, prefab.name))
                continue;

            if (enforceExactRepeatRule && avoidSamePrefabBackToBack && prefab.name == previousPrefabName)
                continue;

            if (enforceTagStreakRule && samePrimaryTagStreak >= maxSamePrimaryTagStreak && cd.primaryTag == prev1)
                continue;

            ChunkSelectionCandidate handcrafted = CreateHandcraftedCandidate(prefab);
            candidates.Add(handcrafted);
            AddGeneratedBlueprintCandidatesForSource(candidates, prefab, cd);
        }

        return candidates;
    }

    private void AddGeneratedBlueprintCandidatesForSource(
        List<ChunkSelectionCandidate> candidates,
        GameObject sourcePrefab,
        ChunkData sourceData)
    {
        if (candidates == null || sourcePrefab == null || sourceData == null)
            return;

        if (!CanUseGeneratedBlueprintCandidate(sourceData))
            return;

        ChunkGenerationRequest request = CreateGenerationRequest(sourcePrefab, sourceData);
        if (request == null)
            return;

        int desiredCount = Mathf.Max(1, generatedCandidateVariantsPerSource);
        int maxAttempts = desiredCount * 4;
        List<ChunkSelectionCandidate> generated = new List<ChunkSelectionCandidate>(desiredCount);
        HashSet<string> seenBlueprints = new HashSet<string>();
        bool reserveHazardAccent = CanOfferGapHazardAccentCandidate(sourceData, request) && desiredCount > 1;
        int normalDesiredCount = reserveHazardAccent ? desiredCount - 1 : desiredCount;

        for (int attempt = 0; attempt < maxAttempts && generated.Count < normalDesiredCount; attempt++)
        {
            ChunkBlueprint blueprint = SimpleChunkBlueprintGenerator.Generate(request);
            TryAddGeneratedCandidate(generated, seenBlueprints, sourcePrefab, sourceData, request, blueprint);
        }

        if (reserveHazardAccent && generated.Count < desiredCount)
        {
            ChunkGenerationRequest hazardRequest = CreateGenerationRequest(sourcePrefab, sourceData);
            hazardRequest.forceGapHazardAccent = true;
            hazardRequest.gapHazardAccentOnExitSide = UnityEngine.Random.Range(0, 2) == 0;
            ChunkBlueprint hazardBlueprint = SimpleChunkBlueprintGenerator.Generate(hazardRequest);
            TryAddGeneratedCandidate(generated, seenBlueprints, sourcePrefab, sourceData, hazardRequest, hazardBlueprint);
        }

        if (generated.Count == 0)
            return;

        float perCandidateWeight = Mathf.Max(0f, generatedCandidateFamilyWeight) / generated.Count;
        for (int i = 0; i < generated.Count; i++)
        {
            generated[i].sourceFamilyWeight = perCandidateWeight;
            candidates.Add(generated[i]);
        }
    }

    private bool TryAddGeneratedCandidate(
        List<ChunkSelectionCandidate> generated,
        HashSet<string> seenBlueprints,
        GameObject sourcePrefab,
        ChunkData sourceData,
        ChunkGenerationRequest request,
        ChunkBlueprint blueprint)
    {
        if (generated == null || seenBlueprints == null || sourcePrefab == null || sourceData == null || blueprint == null)
            return false;

        string blueprintKey = GetStructuralBlueprintKey(blueprint);
        if (!seenBlueprints.Add(blueprintKey))
            return false;

        ChunkBlueprintFeatures features = ChunkBlueprintFeatureExtractor.Analyze(blueprint);
        ChunkBlueprintValidationResult validation = ChunkBlueprintValidator.Validate(blueprint);
        if (!validation.isValid)
            return false;

        if (!IsGeneratedReplacementAcceptable(sourceData, request, blueprint, features, out string rejectionReason))
            return false;

        if (IsGeneratedCandidateSourceDuplicate(sourcePrefab, sourceData, blueprint))
            return false;

        generated.Add(new ChunkSelectionCandidate
        {
            sourcePrefab = sourcePrefab,
            blueprint = blueprint,
            blueprintFeatures = features,
            validationReason = rejectionReason
        });
        return true;
    }

    private bool CanOfferGapHazardAccentCandidate(ChunkData sourceData, ChunkGenerationRequest request)
    {
        return sourceData != null &&
               request != null &&
               sourceData.primaryTag == ChunkTag.Gap &&
               !sourceData.hasHazard &&
               request.sourceMaxGapWidth > 0;
    }

    private bool IsGeneratedCandidateSourceDuplicate(GameObject sourcePrefab, ChunkData sourceData, ChunkBlueprint blueprint)
    {
        if (sourcePrefab == null || sourceData == null || blueprint == null)
            return false;

        string sourceName = NormalizePrefabName(sourcePrefab.name);
        string blueprintName = blueprint.chunkName ?? string.Empty;

        if (sourceName == "Chunk_Flat_Tilemap" && blueprintName == "Generated_Rest")
            return true;

        if (sourceData.primaryTag == ChunkTag.Gap && blueprintName == "Generated_Gap_Centered_Flat")
            return true;

        return false;
    }

    private string GetStructuralBlueprintKey(ChunkBlueprint blueprint)
    {
        if (blueprint == null)
            return string.Empty;

        string rows = ChunkBlueprintFeatureExtractor.RowsToInlineText(blueprint);
        rows = rows.Replace('D', '.');
        return $"{blueprint.chunkName}|{rows}";
    }

    private bool CanUseGeneratedBlueprintCandidate(ChunkData cd)
    {
        if (!useGeneratedBlueprintChunks) return false;
        if (!useGeneratedBlueprintCandidateSelection) return false;
        if (blueprintRuntimeBuilder == null) return false;
        if (cd == null) return false;

        switch (cd.primaryTag)
        {
            case ChunkTag.Gap:
                return allowGeneratedGap;

            case ChunkTag.Precision:
                return allowGeneratedPrecision && IsElevatedPlatformPrecisionSource(cd);

            case ChunkTag.Safe:
            case ChunkTag.Rest:
                return allowGeneratedSafeRest;

            default:
                return false;
        }
    }

    private bool IsHardConstraintAllowed(ChunkTag prev2, ChunkTag prev1, ChunkTag next)
    {
        if (prev1 == ChunkTag.Spikes && next == ChunkTag.Spikes)
            return false;

        if (prev1 == ChunkTag.Precision && next == ChunkTag.Precision)
            return false;

        if (prev1 == ChunkTag.Vertical && prev2 == ChunkTag.Vertical && next == ChunkTag.Vertical)
            return false;

        return true;
    }

    private bool IsBlockedVerticalOppositePair(string previousPrefabName, string candidatePrefabName)
    {
        string previous = NormalizePrefabName(previousPrefabName);
        string candidate = NormalizePrefabName(candidatePrefabName);

        bool upThenDown =
            previous == "Chunk_VerticalUp_Tilemap" &&
            candidate == "Chunk_VerticalDown_Tilemap";

        bool downThenUp =
            previous == "Chunk_VerticalDown_Tilemap" &&
            candidate == "Chunk_VerticalUp_Tilemap";

        return upThenDown || downThenUp;
    }

    private bool IsBlockedConstrainedGeometryPair(string previousCandidateName, string candidatePrefabName)
    {
        string previous = NormalizePrefabName(previousCandidateName);
        string candidate = NormalizePrefabName(candidatePrefabName);

        bool exitHazardGapIntoMovingClimb =
            previous.StartsWith("Generated_GapHazard_ExitOuterSpike") &&
            candidate == "Chunk_MovingClimbSpikes_Tilemap";

        return exitHazardGapIntoMovingClimb;
    }

    private string NormalizePrefabName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
            return string.Empty;

        return prefabName.Trim();
    }

    private void EnsureMarkovWeightTable()
    {
        if (markovWeightTable != null) return;

        markovWeightTable = new MarkovWeightTable();

        if (markovWeightTable.TryLoad(out string loadMessage))
            Debug.Log($"LevelGenerator: Loaded learned Markov weights — {loadMessage}");
        else
            Debug.Log($"LevelGenerator: Using baseline Markov weights ({loadMessage})");
    }

    private float GetMarkovWeight(ChunkTag prev2, ChunkTag prev1, ChunkTag next, DifficultyBand band)
    {
        EnsureMarkovWeightTable();
        return markovWeightTable.GetWeight(prev2, prev1, next, band);
    }

    public MarkovWeightTable GetWeightTable()
    {
        EnsureMarkovWeightTable();
        return markovWeightTable;
    }

    public void ClearLevel()
    {
        for (int i = 0; i < spawnedObjects.Count; i++)
        {
            if (spawnedObjects[i] != null)
                Destroy(spawnedObjects[i]);
        }

        spawnedObjects.Clear();
        spawnedChunkData.Clear();
        activeSlotRecordsByChunk.Clear();

        LevelWorldBounds = new Bounds(Vector3.zero, Vector3.zero);
        LowestSolidY = 0f;

        LevelDifficultyScore = 0f;
        AvgChunkDifficulty = 0f;
        HazardChunkCount = 0;
        TotalEstimatedJumps = 0;
        VerticalChunkCount = 0;
        ChunkCountThisLevel = 0;
    }

    private void SpawnEndPointAt(Vector3 worldPos)
    {
        if (endPointPrefab == null) return;

        GameObject ep = Instantiate(endPointPrefab, worldPos + endPointOffset, Quaternion.identity);
        spawnedObjects.Add(ep);
    }

    private bool SnapChunkEntryToPoint(GameObject chunk, Vector3 targetPoint)
    {
        if (chunk == null) return false;

        Transform entry = FindChildByName(chunk.transform, "Entry");
        Transform exit = FindChildByName(chunk.transform, "Exit");
        if (entry == null || exit == null) return false;

        Vector3 delta = targetPoint - entry.position;
        chunk.transform.position += delta;

        return true;
    }

    private Transform FindChildByName(Transform root, string childName)
    {
        if (root == null) return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == childName)
                return all[i];
        }

        return null;
    }

    private void CacheChunkDataIfPresent(GameObject chunkInstance, LevelRunLog.SlotRecord slotRecord)
    {
        if (chunkInstance == null) return;

        ChunkData cd = chunkInstance.GetComponent<ChunkData>();
        if (cd == null) return;

        spawnedChunkData.Add(cd);

        if (slotRecord != null)
            activeSlotRecordsByChunk[cd] = slotRecord;
    }

    private void RecalculateLevelBounds()
    {
        bool hasAny = false;
        Bounds combined = new Bounds(Vector3.zero, Vector3.zero);

        float lowestY = float.PositiveInfinity;

        for (int i = 0; i < spawnedObjects.Count; i++)
        {
            GameObject go = spawnedObjects[i];
            if (go == null) continue;

            Collider2D[] cols = go.GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                Collider2D col = cols[c];
                if (col == null) continue;
                if (col.isTrigger) continue;

                Bounds b = col.bounds;

                if (!hasAny)
                {
                    combined = b;
                    hasAny = true;
                }
                else
                {
                    combined.Encapsulate(b);
                }

                if (b.min.y < lowestY)
                    lowestY = b.min.y;
            }
        }

        LevelWorldBounds = hasAny ? combined : new Bounds(FirstEntryWorld, Vector3.one);
        LowestSolidY = hasAny ? lowestY : FirstEntryWorld.y;
    }

    private void RecalculateDifficultyStats()
    {
        int count = 0;
        int sumDifficulty = 0;
        int hazardChunks = 0;
        int sumJumps = 0;
        int verticalChunks = 0;

        for (int i = 0; i < spawnedChunkData.Count; i++)
        {
            ChunkData cd = spawnedChunkData[i];
            if (cd == null) continue;

            count++;
            sumDifficulty += cd.difficultyRating;
            sumJumps += Mathf.Max(0, cd.estimatedJumps);

            if (cd.hasHazard) hazardChunks++;

            if (HasTag(cd, ChunkTag.Vertical)) verticalChunks++;
            if (HasTag(cd, ChunkTag.Spikes) && !cd.hasHazard) hazardChunks++;
        }

        ChunkCountThisLevel = count;

        AvgChunkDifficulty = (count > 0) ? (sumDifficulty / (float)count) : 0f;
        HazardChunkCount = hazardChunks;
        TotalEstimatedJumps = sumJumps;
        VerticalChunkCount = verticalChunks;

        float score =
            (wAvgDifficulty * AvgChunkDifficulty) +
            (wHazardChunk * HazardChunkCount) +
            (wEstimatedJump * TotalEstimatedJumps) +
            (wVerticalChunk * VerticalChunkCount);

        if (clampMaxScore > 0f)
            score = Mathf.Clamp(score, 0f, clampMaxScore);

        LevelDifficultyScore = score;
    }

    private bool HasTag(ChunkData cd, ChunkTag tag)
    {
        if (cd == null || cd.tags == null) return false;

        for (int i = 0; i < cd.tags.Length; i++)
        {
            if (cd.tags[i] == tag) return true;
        }

        return false;
    }

    private struct SpawnedChunkResult
    {
        public GameObject chunk;
        public bool replacementAttempted;
        public bool replacementSucceeded;
        public string replacementMode;
        public string replacementReason;
        public string generatedRejectionReason;
        public string generatedBlueprintName;
        public string generatedBlueprintRows;
        public ChunkBlueprintFeatures generatedBlueprintFeatures;
    }

    private struct LookaheadSequenceState
    {
        public ChunkSelectionCandidate firstCandidate;
        public ChunkTag prev2;
        public ChunkTag prev1;
        public string previousPrefabName;
        public string previousCandidateName;
        public int samePrimaryTagStreak;
        public StructuralBudgetState structuralBudgetState;
        public float cumulativeLogScore;
        public int selectedCount;
    }

    private struct StructuralBudgetState
    {
        public int selectedChunks;
        public int hazardChunks;
        public int estimatedJumps;
        public int verticalChunks;
    }

    private struct LookaheadCandidateChoice
    {
        public ChunkSelectionCandidate candidate;
        public float score;
        public float selectionWeight;
        public int depthReached;
    }
}
