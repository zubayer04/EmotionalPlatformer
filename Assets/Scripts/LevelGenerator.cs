using System;
using System.Collections.Generic;
using UnityEngine;

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

    [Header("Generated Blueprint Integration")]
    [Tooltip("If enabled, some selected chunks may be replaced by runtime-generated blueprint chunks.")]
    public bool useGeneratedBlueprintChunks = true;

    [Tooltip("Builder used to convert valid blueprints into runtime chunk GameObjects.")]
    [SerializeField] private ChunkBlueprintRuntimeBuilder blueprintRuntimeBuilder;

    [Tooltip("Chance that an eligible handcrafted chunk is replaced with a generated blueprint version.")]
    [Range(0f, 1f)] public float generatedChunkReplacementChance = 0.25f;

    [Tooltip("Only these chunk families are eligible for generated replacement.")]
    public bool allowGeneratedGap = true;
    public bool allowGeneratedPrecision = true;
    public bool allowGeneratedVertical = false;
    public bool allowGeneratedSpikes = false;
    public bool allowGeneratedSafeRest = false;

    [Header("2-Step Markov Sequencing")]
    [Tooltip("If enabled, chunk selection uses previous TWO chunk states plus target difficulty.")]
    public bool useTwoStepMarkov = true;

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

    // Track what we spawned so we can delete it later
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();

    // Track chunk data for scoring (endpoint etc. won't be included)
    private readonly List<ChunkData> spawnedChunkData = new List<ChunkData>();

    // Track exact prefab sequence for replay
    private readonly List<GameObject> lastGeneratedSequence = new List<GameObject>();
    private readonly Dictionary<ChunkData, LevelRunLog.SlotRecord> activeSlotRecordsByChunk =
        new Dictionary<ChunkData, LevelRunLog.SlotRecord>();

    private LevelRunLog.RunRecord currentRunLog;
    private int currentRunSeed;
    private int lastGeneratedRunSeed;
    private int generatedRunCounter;

    // Useful for other scripts (LevelManager etc.)
    public Vector3 FirstEntryWorld { get; private set; }
    public Vector3 LastExitWorld { get; private set; }

    // Level bounds for KillZone sizing/placement
    public Bounds LevelWorldBounds { get; private set; }
    public float LowestSolidY { get; private set; }

    // Difficulty stats
    public float LevelDifficultyScore { get; private set; }
    public float AvgChunkDifficulty { get; private set; }
    public int HazardChunkCount { get; private set; }
    public int TotalEstimatedJumps { get; private set; }
    public int VerticalChunkCount { get; private set; }
    public int ChunkCountThisLevel { get; private set; }
    public LevelRunLog.RunRecord CurrentRunLog => currentRunLog;
    public int CurrentRunSeed => currentRunSeed;
    public int LastGeneratedRunSeed => lastGeneratedRunSeed;

    public void GenerateLevel()
    {
        GenerateLevelWithSeed(CreateNextRunSeed(), null, rememberAsReplaySequence: true, isReplay: false, replaySourceSeed: 0);
    }

    public void ReplayLastGeneratedLevel()
    {
        if (generatedRunCounter == 0 && lastGeneratedSequence.Count == 0)
        {
            Debug.LogWarning("LevelGenerator: No stored level sequence to replay. Generating a fresh level instead.");
            GenerateLevel();
            return;
        }

        GenerateLevelWithSeed(
            lastGeneratedRunSeed,
            null,
            rememberAsReplaySequence: false,
            isReplay: true,
            replaySourceSeed: lastGeneratedRunSeed);
    }

    public void RecordDeathForChunk(ChunkData chunk, string source, float timeOfDeathSeconds)
    {
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

    private void GenerateLevelWithSeed(
        int runSeed,
        List<GameObject> sourceSequence,
        bool rememberAsReplaySequence,
        bool isReplay,
        int replaySourceSeed)
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;

        try
        {
            currentRunLog = null;
            currentRunSeed = runSeed;
            UnityEngine.Random.InitState(runSeed);

            List<GameObject> sequence = sourceSequence != null
                ? new List<GameObject>(sourceSequence)
                : BuildFreshSequence();

            if (sequence == null || sequence.Count == 0)
            {
                Debug.LogWarning("LevelGenerator: No sequence was available for generation.");
                return;
            }

            currentRunLog = CreateRunRecord(runSeed, isReplay, replaySourceSeed);
            GenerateLevelFromSequence(sequence, rememberAsReplaySequence);

            if (rememberAsReplaySequence)
                lastGeneratedRunSeed = runSeed;
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

    private LevelRunLog.RunRecord CreateRunRecord(int runSeed, bool isReplay, int replaySourceSeed)
    {
        return new LevelRunLog.RunRecord
        {
            runId = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}_{Mathf.Abs(runSeed)}",
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            runSeed = runSeed,
            isReplay = isReplay,
            replaySourceSeed = replaySourceSeed,
            targetDifficultyBeforeRun = targetDifficulty,
            startDifficultyBias = startDifficultyBias,
            difficultyPreferenceStrength = difficultyPreferenceStrength,
            useTwoStepMarkov = useTwoStepMarkov,
            useGeneratedBlueprintChunks = useGeneratedBlueprintChunks,
            generatedChunkReplacementChance = generatedChunkReplacementChance,
            totalChunksConfigured = totalChunks
        };
    }

    private List<GameObject> BuildFreshSequence()
    {
        List<GameObject> sequence = new List<GameObject>();

        if ((chunkPrefabs == null || chunkPrefabs.Length == 0) && startingChunkPrefab == null)
        {
            Debug.LogWarning("LevelGenerator: No chunk prefabs assigned!");
            return sequence;
        }

        int remainingChunks = totalChunks;

        ChunkTag prev1 = ChunkTag.Rest;
        ChunkTag prev2 = ChunkTag.Rest;

        string previousPrefabName = string.Empty;
        int samePrimaryTagStreak = 0;

        if (startingChunkPrefab != null && remainingChunks > 0)
        {
            sequence.Add(startingChunkPrefab);

            ChunkData startData = startingChunkPrefab.GetComponent<ChunkData>();
            if (startData != null)
            {
                prev1 = startData.primaryTag;
                prev2 = startData.primaryTag;
                previousPrefabName = startingChunkPrefab.name;
                samePrimaryTagStreak = 1;
            }

            remainingChunks -= 1;
        }

        for (int i = 0; i < remainingChunks; i++)
        {
            if (chunkPrefabs == null || chunkPrefabs.Length == 0) break;

            GameObject prefab = SelectNextChunkPrefab(prev2, prev1, previousPrefabName, samePrimaryTagStreak, i);
            if (prefab == null)
            {
                Debug.LogWarning("LevelGenerator: No valid next chunk prefab could be selected.");
                break;
            }

            sequence.Add(prefab);

            ChunkData data = prefab.GetComponent<ChunkData>();
            if (data != null)
            {
                if (data.primaryTag == prev1)
                    samePrimaryTagStreak++;
                else
                    samePrimaryTagStreak = 1;

                prev2 = prev1;
                prev1 = data.primaryTag;
                previousPrefabName = prefab.name;
            }
        }

        return sequence;
    }

    private void GenerateLevelFromSequence(List<GameObject> sequence, bool rememberAsReplaySequence)
    {
        if (clearBeforeGenerate) ClearLevel();

        if (sequence == null || sequence.Count == 0)
        {
            Debug.LogWarning("LevelGenerator: Cannot generate level from empty sequence.");
            return;
        }

        if (rememberAsReplaySequence)
        {
            lastGeneratedSequence.Clear();
            for (int i = 0; i < sequence.Count; i++)
            {
                lastGeneratedSequence.Add(sequence[i]);
            }
        }

        Vector3 nextAttachPoint = (startPoint != null) ? startPoint.position : transform.position;
        bool hasFixedStartingChunk = startingChunkPrefab != null &&
                                     sequence.Count > 0 &&
                                     sequence[0] == startingChunkPrefab;

        FirstEntryWorld = nextAttachPoint;
        LastExitWorld = nextAttachPoint;

        for (int i = 0; i < sequence.Count; i++)
        {
            GameObject prefab = sequence[i];
            if (prefab == null) continue;

            bool isStartingChunk = hasFixedStartingChunk && i == 0;
            int generatedSlotIndex = isStartingChunk ? -1 : (hasFixedStartingChunk ? i - 1 : i);
            LevelRunLog.SlotRecord slotRecord = CreateSelectedSlotRecord(prefab, i, generatedSlotIndex, isStartingChunk);

            SpawnedChunkResult spawnResult = SpawnChunkResultFromPrefabOrBlueprint(prefab, i);
            slotRecord.spawnSucceeded = spawnResult.chunk != null;
            slotRecord.replacementAttempted = spawnResult.replacementAttempted;
            slotRecord.replacementSucceeded = spawnResult.replacementSucceeded;
            slotRecord.replacementMode = spawnResult.replacementMode;
            slotRecord.generatedBlueprintName = spawnResult.generatedBlueprintName;

            GameObject chunk = spawnResult.chunk;
            if (chunk == null)
            {
                if (currentRunLog != null) currentRunLog.slots.Add(slotRecord);
                Debug.LogWarning($"LevelGenerator: Failed to spawn chunk for prefab '{prefab.name}'.");
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

        SpawnEndPointAt(LastExitWorld);
        RecalculateLevelBounds();
        RecalculateDifficultyStats();
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
                generatedBlueprintName = string.Empty
            };
        }

        string generatedBlueprintName;
        GameObject generatedChunk = TryBuildGeneratedChunkFromPrefab(prefab, cd, out generatedBlueprintName);

        if (generatedChunk != null)
        {
            return new SpawnedChunkResult
            {
                chunk = generatedChunk,
                replacementAttempted = true,
                replacementSucceeded = true,
                replacementMode = "generated_success",
                generatedBlueprintName = generatedBlueprintName
            };
        }

        Debug.LogWarning($"LevelGenerator: Falling back to handcrafted prefab for '{prefab.name}'.");
        return new SpawnedChunkResult
        {
            chunk = Instantiate(prefab, Vector3.zero, Quaternion.identity),
            replacementAttempted = true,
            replacementSucceeded = false,
            replacementMode = "generated_fallback",
            generatedBlueprintName = generatedBlueprintName
        };
    }

    private bool ShouldUseGeneratedChunk(ChunkData cd, int slotIndex)
    {
        if (!useGeneratedBlueprintChunks) return false;
        if (blueprintRuntimeBuilder == null) return false;
        if (cd == null) return false;

        // Keep the very first chunk stable/safe
        if (slotIndex == 0) return false;

        if (UnityEngine.Random.value > generatedChunkReplacementChance) return false;

        switch (cd.primaryTag)
        {
            case ChunkTag.Gap:
                return allowGeneratedGap;

            case ChunkTag.Precision:
                return allowGeneratedPrecision;

            case ChunkTag.Vertical:
                return allowGeneratedVertical;

            case ChunkTag.Spikes:
                return allowGeneratedSpikes;

            case ChunkTag.Safe:
            case ChunkTag.Rest:
                return allowGeneratedSafeRest;

            default:
                return false;
        }
    }

    private GameObject TryBuildGeneratedChunkFromPrefab(GameObject prefab, ChunkData cd, out string generatedBlueprintName)
    {
        generatedBlueprintName = string.Empty;

        if (cd == null) return null;

        ChunkGenerationRequest request = new ChunkGenerationRequest
        {
            requestedPrimaryTag = cd.primaryTag,
            targetDifficulty = cd.difficultyRating,
            requireHazard = cd.hasHazard,
            preferredWidth = GetPreferredWidthForTag(cd),
            preferredHeight = GetPreferredHeightForTag(cd)
        };

        ChunkBlueprint blueprint = SimpleChunkBlueprintGenerator.Generate(request);
        if (blueprint == null)
        {
            Debug.LogWarning($"LevelGenerator: Generator returned null for '{prefab.name}'.");
            return null;
        }

        generatedBlueprintName = blueprint.chunkName;

        ChunkBlueprintValidationResult validation = ChunkBlueprintValidator.Validate(blueprint);
        if (!validation.isValid)
        {
            Debug.LogWarning($"LevelGenerator: Generated blueprint invalid for '{prefab.name}'.");
            for (int i = 0; i < validation.errors.Count; i++)
            {
                Debug.LogWarning($"  Error {i + 1}: {validation.errors[i]}");
            }
            return null;
        }

        GameObject built = blueprintRuntimeBuilder.BuildChunk(blueprint, Vector3.zero);
        if (built == null)
        {
            Debug.LogWarning($"LevelGenerator: Runtime builder failed for '{prefab.name}'.");
            return null;
        }

        return built;
    }

    private LevelRunLog.SlotRecord CreateSelectedSlotRecord(GameObject prefab, int sequenceIndex, int generatedSlotIndex, bool isStartingChunk)
    {
        LevelRunLog.SlotRecord record = new LevelRunLog.SlotRecord
        {
            sequenceIndex = sequenceIndex,
            generatedSlotIndex = generatedSlotIndex,
            isStartingChunk = isStartingChunk,
            replacementMode = "none"
        };

        if (!isStartingChunk && generatedSlotIndex >= 0)
        {
            record.hasSlotTargetDifficulty = true;
            record.slotTargetDifficulty = GetSlotTargetDifficultyForGeneratedSlotIndex(generatedSlotIndex);
        }

        if (prefab == null)
        {
            record.selectedPrefabName = "MissingPrefab";
            return record;
        }

        record.selectedPrefabName = LevelRunLog.CleanName(prefab.name);

        ChunkData selectedData = prefab.GetComponent<ChunkData>();
        if (selectedData == null)
            return record;

        record.selectedPrimaryTag = selectedData.primaryTag.ToString();
        record.selectedDifficulty = selectedData.difficultyRating;
        record.selectedHasHazard = selectedData.hasHazard;
        record.selectedEstimatedJumps = selectedData.estimatedJumps;
        record.selectedExitDelta = selectedData.exitDelta;

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

    private GameObject SelectNextChunkPrefab(
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        int samePrimaryTagStreak,
        int slotIndex)
    {
        List<GameObject> candidates = GetCandidates(prev2, prev1, previousPrefabName, samePrimaryTagStreak, true, true);

        if (candidates.Count == 0)
            candidates = GetCandidates(prev2, prev1, previousPrefabName, samePrimaryTagStreak, false, true);

        if (candidates.Count == 0)
            candidates = GetCandidates(prev2, prev1, previousPrefabName, samePrimaryTagStreak, false, false);

        if (candidates.Count == 0)
            return null;

        float progress = Mathf.Clamp01((slotIndex + 1f) / Mathf.Max(1, totalChunks - 1));
        float slotTargetDifficulty = Mathf.Lerp(startDifficultyBias, targetDifficulty, progress);
        DifficultyBand band = GetDifficultyBand(targetDifficulty);

        float totalWeight = 0f;
        List<float> weights = new List<float>(candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            ChunkData cd = candidates[i].GetComponent<ChunkData>();
            if (cd == null)
            {
                weights.Add(0.01f);
                totalWeight += 0.01f;
                continue;
            }

            float transitionWeight = useTwoStepMarkov
                ? GetTransitionWeight(prev2, prev1, cd.primaryTag, band)
                : 1f;

            if (transitionWeight <= 0f)
            {
                weights.Add(0.01f);
                totalWeight += 0.01f;
                continue;
            }

            float diff = Mathf.Abs(cd.difficultyRating - slotTargetDifficulty);
            float difficultyWeight = Mathf.Exp(-difficultyPreferenceStrength * diff * diff);

            float varietyWeight = 1f;
            if (cd.primaryTag != prev1)
                varietyWeight += varietyBonus;

            float pacingWeight = 1f;
            bool isHardType = cd.primaryTag == ChunkTag.Spikes || cd.primaryTag == ChunkTag.Precision;
            if (slotIndex < 2 && isHardType)
                pacingWeight *= earlyHardPenalty;

            if (cd.difficultyRating > slotTargetDifficulty + 2f)
                pacingWeight *= 0.65f;

            float finalWeight = transitionWeight * difficultyWeight * varietyWeight * pacingWeight;
            finalWeight = Mathf.Max(0.01f, finalWeight);

            weights.Add(finalWeight);
            totalWeight += finalWeight;
        }

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

    private List<GameObject> GetCandidates(
        ChunkTag prev2,
        ChunkTag prev1,
        string previousPrefabName,
        int samePrimaryTagStreak,
        bool enforceExactRepeatRule,
        bool enforceTagStreakRule)
    {
        List<GameObject> candidates = new List<GameObject>();

        for (int i = 0; i < chunkPrefabs.Length; i++)
        {
            GameObject prefab = chunkPrefabs[i];
            if (prefab == null) continue;

            ChunkData cd = prefab.GetComponent<ChunkData>();
            if (cd == null) continue;

            if (!IsHardConstraintAllowed(prev2, prev1, cd.primaryTag))
                continue;

            if (enforceExactRepeatRule && avoidSamePrefabBackToBack && prefab.name == previousPrefabName)
                continue;

            if (enforceTagStreakRule && samePrimaryTagStreak >= maxSamePrimaryTagStreak && cd.primaryTag == prev1)
                continue;

            candidates.Add(prefab);
        }

        return candidates;
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

    private enum DifficultyBand { Low, Medium, High }

    private DifficultyBand GetDifficultyBand(float difficulty)
    {
        if (difficulty <= 3.5f) return DifficultyBand.Low;
        if (difficulty <= 6.5f) return DifficultyBand.Medium;
        return DifficultyBand.High;
    }

    private float GetTransitionWeight(ChunkTag prev2, ChunkTag prev1, ChunkTag next, DifficultyBand band)
    {
        if (band == DifficultyBand.Low)
        {
            if (prev1 == ChunkTag.Spikes)
            {
                if (next == ChunkTag.Rest) return 4.0f;
                if (next == ChunkTag.Safe) return 3.0f;
                if (next == ChunkTag.Gap) return 1.0f;
                return 0.1f;
            }

            if (prev1 == ChunkTag.Precision)
            {
                if (next == ChunkTag.Rest) return 4.0f;
                if (next == ChunkTag.Safe) return 3.0f;
                if (next == ChunkTag.Gap) return 1.0f;
                return 0.1f;
            }

            if (prev1 == ChunkTag.Vertical)
            {
                if (next == ChunkTag.Safe) return 3.0f;
                if (next == ChunkTag.Rest) return 2.5f;
                if (next == ChunkTag.Gap) return 1.5f;
                return 0.5f;
            }

            if (prev1 == ChunkTag.Gap)
            {
                if (next == ChunkTag.Safe) return 3.0f;
                if (next == ChunkTag.Rest) return 2.5f;
                if (next == ChunkTag.Precision) return 0.75f;
                return 0.5f;
            }

            if (prev1 == ChunkTag.Rest || prev1 == ChunkTag.Safe)
            {
                if (next == ChunkTag.Safe) return 2.5f;
                if (next == ChunkTag.Gap) return 2.0f;
                if (next == ChunkTag.Vertical) return 1.5f;
                if (next == ChunkTag.Rest) return 1.5f;
                if (next == ChunkTag.Spikes) return 0.75f;
                if (next == ChunkTag.Precision) return 0.5f;
            }
        }

        if (band == DifficultyBand.Medium)
        {
            if (prev1 == ChunkTag.Spikes)
            {
                if (next == ChunkTag.Safe) return 3.0f;
                if (next == ChunkTag.Rest) return 2.5f;
                if (next == ChunkTag.Gap) return 1.5f;
                if (next == ChunkTag.Vertical) return 1.0f;
                return 0.25f;
            }

            if (prev1 == ChunkTag.Precision)
            {
                if (next == ChunkTag.Safe) return 2.5f;
                if (next == ChunkTag.Rest) return 2.0f;
                if (next == ChunkTag.Gap) return 1.75f;
                if (next == ChunkTag.Vertical) return 1.25f;
                if (next == ChunkTag.Spikes) return 1.0f;
                return 0.25f;
            }

            if (prev1 == ChunkTag.Vertical)
            {
                if (next == ChunkTag.Safe) return 2.5f;
                if (next == ChunkTag.Rest) return 2.0f;
                if (next == ChunkTag.Gap) return 1.75f;
                if (next == ChunkTag.Precision) return 1.5f;
                return 0.75f;
            }

            if (prev1 == ChunkTag.Gap)
            {
                if (next == ChunkTag.Safe) return 2.5f;
                if (next == ChunkTag.Rest) return 2.0f;
                if (next == ChunkTag.Precision) return 1.75f;
                if (next == ChunkTag.Vertical) return 1.25f;
                return 0.75f;
            }

            if (prev1 == ChunkTag.Rest || prev1 == ChunkTag.Safe)
            {
                if (next == ChunkTag.Gap) return 2.2f;
                if (next == ChunkTag.Safe) return 2.0f;
                if (next == ChunkTag.Vertical) return 1.75f;
                if (next == ChunkTag.Spikes) return 1.5f;
                if (next == ChunkTag.Precision) return 1.2f;
                if (next == ChunkTag.Rest) return 1.0f;
            }
        }

        if (band == DifficultyBand.High)
        {
            if (prev2 == ChunkTag.Spikes && prev1 == ChunkTag.Rest)
            {
                if (next == ChunkTag.Spikes) return 2.0f;
                if (next == ChunkTag.Precision) return 1.75f;
                if (next == ChunkTag.Gap) return 1.5f;
                if (next == ChunkTag.Safe) return 1.0f;
                if (next == ChunkTag.Rest) return 0.75f;
            }

            if (prev2 == ChunkTag.Vertical && prev1 == ChunkTag.Gap)
            {
                if (next == ChunkTag.Precision) return 2.0f;
                if (next == ChunkTag.Spikes) return 1.5f;
                if (next == ChunkTag.Safe) return 1.0f;
            }

            if (prev1 == ChunkTag.Spikes)
            {
                if (next == ChunkTag.Safe) return 2.0f;
                if (next == ChunkTag.Rest) return 1.5f;
                if (next == ChunkTag.Gap) return 1.75f;
                if (next == ChunkTag.Precision) return 1.5f;
                if (next == ChunkTag.Vertical) return 1.0f;
            }

            if (prev1 == ChunkTag.Precision)
            {
                if (next == ChunkTag.Safe) return 1.75f;
                if (next == ChunkTag.Rest) return 1.25f;
                if (next == ChunkTag.Gap) return 1.75f;
                if (next == ChunkTag.Vertical) return 1.75f;
                if (next == ChunkTag.Spikes) return 1.25f;
            }

            if (prev1 == ChunkTag.Vertical)
            {
                if (next == ChunkTag.Gap) return 2.0f;
                if (next == ChunkTag.Precision) return 1.75f;
                if (next == ChunkTag.Safe) return 1.5f;
                if (next == ChunkTag.Rest) return 1.0f;
                if (next == ChunkTag.Spikes) return 1.25f;
            }

            if (prev1 == ChunkTag.Gap)
            {
                if (next == ChunkTag.Precision) return 2.0f;
                if (next == ChunkTag.Vertical) return 1.75f;
                if (next == ChunkTag.Safe) return 1.5f;
                if (next == ChunkTag.Rest) return 1.0f;
                if (next == ChunkTag.Spikes) return 1.25f;
            }

            if (prev1 == ChunkTag.Rest || prev1 == ChunkTag.Safe)
            {
                if (next == ChunkTag.Gap) return 2.0f;
                if (next == ChunkTag.Vertical) return 1.75f;
                if (next == ChunkTag.Spikes) return 1.75f;
                if (next == ChunkTag.Precision) return 1.5f;
                if (next == ChunkTag.Safe) return 1.0f;
                if (next == ChunkTag.Rest) return 0.75f;
            }
        }

        return 1f;
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
        public string generatedBlueprintName;
    }
}
