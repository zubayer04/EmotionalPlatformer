using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [System.Serializable]
    private class ChunkDeathStat
    {
        public string chunkName;
        public ChunkTag primaryTag;
        public int difficultyRating;
        public int deaths;
    }

    [System.Serializable]
    private class DeathEventInfo
    {
        public string source;
        public string chunkName;
        public ChunkTag? primaryTag;
        public float timeOfDeath;
    }

    private struct MarkovLearningAudit
    {
        public bool markovLearningApplied;
        public bool markovPositiveReinforcementCapped;
        public float markovLearningQuality;
        public float markovDeliveredTargetDelta;
        public int markovTransitionsUpdated;
        public bool pressureAwareMarkovApplied;
        public int pressureAwareMarkovTransitions;
        public int pressureAwareMarkovMaxDeathsOnSlot;
        public float pressureAwareMarkovPenaltyTotal;
        public string pressureAwareMarkovReasons;
    }

    [Header("References")]
    [SerializeField] private LevelGenerator levelGenerator;
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private GameObject player;
    [SerializeField] private PlayerBehaviourTracker behaviourTracker;

    [Header("Markov Learning")]
    [Tooltip("How much each transition quality score nudges the learned weight.")]
    [Range(0f, 0.5f)] [SerializeField] private float markovLearningRate = 0.05f;

    [Tooltip("How much learned weights decay toward baseline after each level (prevents drift).")]
    [Range(0f, 0.2f)] [SerializeField] private float markovDecayRate = 0.02f;

    [Tooltip("Only add an extra Markov penalty when repeated deaths confirm an already pressure-penalized transition.")]
    [SerializeField] private bool pressureAwareMarkovLearningEnabled = true;

    [Tooltip("Deaths on the receiving slot required before pressure-aware Markov learning is applied.")]
    [Range(1, 5)] [SerializeField] private int pressureAwareMarkovDeathThreshold = 3;

    [Tooltip("Extra negative quality applied when pressure-aware Markov learning is triggered.")]
    [Range(0f, 2f)] [SerializeField] private float pressureAwareMarkovPenalty = 0.8f;

    [Header("KillZone")]
    [Tooltip("Assign your KillZone object here (the one with BoxCollider2D + KillZone.cs). If left empty, we'll try to find it.")]
    [SerializeField] private Transform killZoneTransform;

    [Tooltip("How far below the lowest solid ground the KillZone should sit.")]
    [SerializeField] private float killZonePaddingBelowGround = 6f;

    [Tooltip("Extra width added to KillZone collider beyond the level bounds.")]
    [SerializeField] private float killZoneExtraWidth = 20f;

    [Tooltip("Extra horizontal padding beyond the FIRST entry and LAST exit (prevents missing killzone after endpoint).")]
    [SerializeField] private float killZoneExtraBeyondEnds = 30f;

    [Header("Level Stats (read-only while playing)")]
    public int deathsThisLevel = 0;
    public float levelTimer = 0f;

    [Header("Adaptive Difficulty")]
    [SerializeField] private bool adaptiveDifficultyEnabled = true;

    [Range(0f, 10f)] [SerializeField] private float minTargetDifficulty = 1f;
    [Range(0f, 10f)] [SerializeField] private float maxTargetDifficulty = 9f;
    [Range(0.1f, 2f)] [SerializeField] private float targetDifficultyStep = 0.5f;
    [Range(0f, 3f)] [SerializeField] private float hardDeathsPerChunkThreshold = 0.35f;
    [Range(0f, 30f)] [SerializeField] private float slowTimePerChunkThreshold = 10f;
    [Range(0f, 1f)] [SerializeField] private float easyDeathsPerChunkThreshold = 0.02f;
    [Range(0f, 30f)] [SerializeField] private float fastTimePerChunkThreshold = 3.8f;

    [Header("Clean Run Streak")]
    [Tooltip("Number of clean runs in a row required to force a difficulty increase.")]
    [SerializeField] private int cleanRunStreakThreshold = 3;

    [Header("Evidence-Based Adaptation")]
    [Tooltip("How far actual delivered difficulty may exceed target before increases are blocked.")]
    [Range(0f, 3f)] [SerializeField] private float actualDifficultyOvershootTolerance = 1f;

    [Tooltip("How far actual delivered difficulty may sit below target before clean runs are treated as under-challenged.")]
    [Range(0f, 3f)] [SerializeField] private float actualDifficultyUndershootTolerance = 0.75f;

    [Tooltip("How quickly the adaptation controller reacts to the latest run strain.")]
    [Range(0f, 1f)] [SerializeField] private float strainSmoothing = 0.55f;

    [Header("Testing / Pause After Level")]
    [SerializeField] private bool pauseAfterLevelCompletion = true;
    [SerializeField] private KeyCode continueKey = KeyCode.Return;

    [Header("Debug HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private bool advancedHud = true;

    // latest completed-level debug values
    private float lastDeathsPerChunk = 0f;
    private float lastTimePerChunk = 0f;
    private float lastTargetBeforeAdapt = 0f;
    private float lastTargetAfterAdapt = 0f;
    private string lastAdaptationDecision = "None yet";

    // pause and pending-next-level state
    private bool waitingForNextLevelChoice = false;

    // prevents duplicate completion logging for the current generated level
    private bool currentGeneratedLevelAlreadyLogged = false;

    // stored completion snapshot for the paused summary screen
    private float completedTime = 0f;
    private int completedDeaths = 0;
    private int completedChunkCount = 0;
    private float completedActualDifficulty = 0f;
    private float completedAvgDifficulty = 0f;
    private int completedHazards = 0;
    private int completedJumps = 0;
    private int completedVertical = 0;

    // per-level logging
    private readonly Dictionary<ChunkData, ChunkDeathStat> deathsByChunk = new Dictionary<ChunkData, ChunkDeathStat>();
    private readonly List<DeathEventInfo> deathEvents = new List<DeathEventInfo>();

    // adaptive memory
    private int cleanRunStreak = 0;
    private bool hasRecentStrainScore = false;
    private float recentStrainScore = 0f;
    private bool gameplayStarted = false;
    private int sessionLevelNumber = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found;
        }

        if (levelGenerator == null)
        {
            Debug.LogError("LevelManager: levelGenerator reference is missing.");
            return;
        }

        if (player == null)
        {
            Debug.LogError("LevelManager: player reference is missing (and couldn't find a tagged Player).");
            return;
        }

        if (killZoneTransform == null)
        {
            KillZone kz = FindFirstObjectByType<KillZone>();
            if (kz != null) killZoneTransform = kz.transform;
        }

        EnsureBehaviourTracker();

        ShowStartMenu();
    }

    private void Update()
    {
        if (!gameplayStarted)
            return;

        if (!waitingForNextLevelChoice)
        {
            levelTimer += Time.deltaTime;
            ObserveBehaviourChunk();
        }

        if (waitingForNextLevelChoice)
        {
            if (Input.GetKeyDown(continueKey))
            {
                ContinueToNextLevel();
            }
        }
    }

    public void OnPlayerDied(ChunkData chunk, string source)
    {
        // records death evidence before respawning the player.
        if (!gameplayStarted) return;
        if (waitingForNextLevelChoice) return;

        if (chunk == null && player != null && levelGenerator != null)
        {
            chunk = levelGenerator.GetBestChunkForWorldPosition(player.transform.position);
        }

        deathsThisLevel++;

        if (chunk != null)
        {
            if (!deathsByChunk.TryGetValue(chunk, out ChunkDeathStat stat))
            {
                stat = new ChunkDeathStat
                {
                    chunkName = CleanChunkName(chunk.name),
                    primaryTag = chunk.primaryTag,
                    difficultyRating = chunk.difficultyRating,
                    deaths = 0
                };

                deathsByChunk.Add(chunk, stat);
            }

            stat.deaths++;
        }

        deathEvents.Add(new DeathEventInfo
        {
            source = source,
            chunkName = chunk != null ? CleanChunkName(chunk.name) : "None",
            primaryTag = chunk != null ? chunk.primaryTag : null,
            timeOfDeath = levelTimer
        });

        if (levelGenerator != null)
        {
            levelGenerator.RecordDeathForChunk(chunk, source, levelTimer);
        }

        if (behaviourTracker != null)
        {
            string chunkName = chunk != null ? CleanChunkName(chunk.name) : "None";
            behaviourTracker.OnPlayerDied(chunkName);
        }

        Debug.Log(
            chunk != null
                ? $"PLAYER DIED | source={source} | chunk={CleanChunkName(chunk.name)} | tag={chunk.primaryTag} | diff={chunk.difficultyRating} | levelTime={levelTimer:F2}s"
                : $"PLAYER DIED | source={source} | chunk=None | levelTime={levelTimer:F2}s"
        );

        RespawnPlayer(resetVelocity: true);
    }

    public void OnLevelCompleted()
    {
        // closes the run: behaviour summary, adaptation, markov learning, and logging.
        if (!gameplayStarted) return;
        if (waitingForNextLevelChoice) return;

        int chunkCount = Mathf.Max(1, levelGenerator.ChunkCountThisLevel);

        float deathsPerChunk = deathsThisLevel / (float)chunkCount;
        float timePerChunk = levelTimer / chunkCount;

        // snapshot completed-level values for display and logging while paused
        completedTime = levelTimer;
        completedDeaths = deathsThisLevel;
        completedChunkCount = levelGenerator.ChunkCountThisLevel;
        completedActualDifficulty = levelGenerator.LevelDifficultyScore;
        completedAvgDifficulty = levelGenerator.AvgChunkDifficulty;
        completedHazards = levelGenerator.HazardChunkCount;
        completedJumps = levelGenerator.TotalEstimatedJumps;
        completedVertical = levelGenerator.VerticalChunkCount;

        if (!currentGeneratedLevelAlreadyLogged)
        {
            if (behaviourTracker != null)
                behaviourTracker.StopTracking();

            BehaviourSummary behaviourSummary = behaviourTracker != null
                ? behaviourTracker.GetSummary()
                : BehaviourSummary.Empty;

            lastDeathsPerChunk = deathsPerChunk;
            lastTimePerChunk = timePerChunk;
            lastTargetBeforeAdapt = levelGenerator.targetDifficulty;
            LevelRunLog.AdaptationRecord adaptationRecord;

            if (adaptiveDifficultyEnabled)
            {
                adaptationRecord = UpdateTargetDifficulty(deathsPerChunk, timePerChunk, behaviourSummary);
                ApplyMarkovLearningAudit(adaptationRecord, UpdateMarkovWeights(behaviourSummary));
            }
            else
            {
                lastTargetAfterAdapt = levelGenerator.targetDifficulty;
                lastAdaptationDecision = "Adaptive OFF";
                adaptationRecord = CreateAdaptiveOffRecord(deathsPerChunk, timePerChunk);
            }

            adaptationRecord.hesitationScore = behaviourSummary.hesitationScore;
            adaptationRecord.momentumFluidity = behaviourSummary.momentumFluidity;
            adaptationRecord.directionReversalRate = behaviourSummary.directionReversalRate;
            adaptationRecord.avgRetryDelay = behaviourSummary.avgRetryDelay;
            adaptationRecord.deathClusteringRatio = behaviourSummary.deathClusteringRatio;
            adaptationRecord.engagementScore = behaviourSummary.EngagementScore();
            adaptationRecord.behaviourChunksTraversed = behaviourSummary.chunksTraversed;
            adaptationRecord.behaviourTraversalFrames = behaviourSummary.totalTraversalFrames;

            FinalizeAndWriteCurrentRunLog(deathsPerChunk, timePerChunk, adaptationRecord);

            Debug.Log(
                $"LEVEL COMPLETE | time={completedTime:F2}s | deaths={completedDeaths} | " +
                $"deathsPerChunk={deathsPerChunk:F2} | timePerChunk={timePerChunk:F2} | " +
                $"targetBefore={lastTargetBeforeAdapt:F2} | targetAfter={lastTargetAfterAdapt:F2} | " +
                $"actual={completedActualDifficulty:F2} | avg={completedAvgDifficulty:F2} | " +
                $"hazards={completedHazards} | jumps={completedJumps} | vertical={completedVertical} | " +
                $"chunks={completedChunkCount} | decision={lastAdaptationDecision} | cleanRunStreak={cleanRunStreak}"
            );

            LogChunkDeathBreakdown();
            LogDeathEvents();

            currentGeneratedLevelAlreadyLogged = true;
        }

        if (pauseAfterLevelCompletion)
        {
            waitingForNextLevelChoice = true;
            Time.timeScale = 0f;
        }
        else
        {
            ContinueToNextLevel();
        }
    }

    public void StartGameFromMenu(float startingTargetDifficulty, bool showAdvancedStats)
    {
        // menu entry point that resets adaptive memory for a fresh session.
        gameplayStarted = true;
        advancedHud = showAdvancedStats;
        sessionLevelNumber = 0;
        cleanRunStreak = 0;
        hasRecentStrainScore = false;
        recentStrainScore = 0f;

        if (levelGenerator != null)
            levelGenerator.targetDifficulty = Mathf.Clamp(startingTargetDifficulty, minTargetDifficulty, maxTargetDifficulty);

        GenerateFreshLevel();
    }

    private void ShowStartMenu()
    {
        gameplayStarted = false;
        waitingForNextLevelChoice = false;
        Time.timeScale = 0f;
        ResetStats();

        if (levelGenerator != null)
            levelGenerator.ClearLevel();

        GameUIManager uiManager = FindFirstObjectByType<GameUIManager>();
        if (uiManager == null)
        {
            GameObject uiObject = new GameObject("Game UI Manager");
            uiManager = uiObject.AddComponent<GameUIManager>();
        }

        uiManager.Initialize(this);
    }

    private void GenerateFreshLevel()
    {
        // generates the next runtime level and restarts behaviour tracking.
        Time.timeScale = 1f;
        waitingForNextLevelChoice = false;
        currentGeneratedLevelAlreadyLogged = false;
        sessionLevelNumber++;

        levelGenerator.ClearLevel();
        levelGenerator.GenerateLevel();

        RepositionKillZoneToLevel();

        ResetStats();
        RespawnPlayer(resetVelocity: true);

        if (behaviourTracker != null)
            behaviourTracker.StartTracking();
    }

    private void ContinueToNextLevel()
    {
        GenerateFreshLevel();
    }

    private void EnsureBehaviourTracker()
    {
        if (behaviourTracker == null)
            behaviourTracker = FindFirstObjectByType<PlayerBehaviourTracker>();

        if (behaviourTracker == null && player != null)
            behaviourTracker = player.GetComponent<PlayerBehaviourTracker>();

        if (behaviourTracker == null && player != null)
            behaviourTracker = player.AddComponent<PlayerBehaviourTracker>();

        if (behaviourTracker == null || player == null)
            return;

        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller != null)
            behaviourTracker.SetPlayer(controller);
    }

    private void ObserveBehaviourChunk()
    {
        if (behaviourTracker == null || levelGenerator == null || player == null)
            return;

        ChunkData currentChunk = levelGenerator.GetBestChunkForWorldPosition(player.transform.position);
        behaviourTracker.ObserveCurrentChunk(currentChunk);
    }

    private MarkovLearningAudit UpdateMarkovWeights(BehaviourSummary behaviour)
    {
        // nudges learned transition weights using completed-run evidence.
        MarkovLearningAudit audit = new MarkovLearningAudit();

        if (levelGenerator == null) return audit;

        if (behaviour.chunksTraversed <= 0 || behaviour.totalTraversalFrames <= 0)
        {
            Debug.LogWarning("LevelManager: Skipping Markov learning because no behavioural traversal data was recorded.");
            return audit;
        }

        MarkovWeightTable table = levelGenerator.GetWeightTable();
        if (table == null) return audit;

        LevelRunLog.RunRecord runRecord = levelGenerator.CurrentRunLog;
        if (runRecord == null || runRecord.slots == null || runRecord.slots.Count < 2)
            return audit;

        float engagementScore = behaviour.EngagementScore();

        // quality score: centered around 0.5 engagement = neutral
        // high engagement reinforces a transition; low engagement weakens it
        float qualityBase = (engagementScore - 0.5f) * 2f;

        float deliveredTargetDelta = completedActualDifficulty - lastTargetBeforeAdapt;
        bool deliveredContentOvershotTarget = deliveredTargetDelta > actualDifficultyOvershootTolerance;
        audit.markovDeliveredTargetDelta = deliveredTargetDelta;
        audit.markovPositiveReinforcementCapped = deliveredContentOvershotTarget && qualityBase > 0f;

        if (deliveredContentOvershotTarget && qualityBase > 0f)
        {
            // clean, fluent play should not reinforce transitions that already overshot target
            qualityBase = 0f;
        }

        // spread deaths suggest broader overwhelm, so they penalize more
        if (behaviour.deathClusteringRatio < 0.5f && behaviour.deathClusteringRatio > 0f)
            qualityBase -= 0.3f;

        audit.markovLearningApplied = true;
        audit.markovLearningQuality = qualityBase;

        DifficultyBand band = MarkovWeightTable.GetBandForDifficulty(levelGenerator.targetDifficulty);

        ChunkTag prev2 = ChunkTag.Rest;
        ChunkTag prev1 = ChunkTag.Rest;

        for (int i = 0; i < runRecord.slots.Count; i++)
        {
            LevelRunLog.SlotRecord slot = runRecord.slots[i];
            if (slot == null) continue;

            if (!System.Enum.TryParse(slot.spawnedPrimaryTag ?? slot.selectedPrimaryTag, out ChunkTag currentTag))
                continue;

            if (i >= 1)
            {
                // per-slot adjustment: penalize slots that had deaths
                float slotQuality = qualityBase;
                if (slot.deathsAttributedToSlot > 0)
                    slotQuality -= 0.2f * Mathf.Min(slot.deathsAttributedToSlot, 3);

                if (ShouldApplyPressureAwareMarkovPenalty(slot))
                {
                    slotQuality -= pressureAwareMarkovPenalty;
                    audit.pressureAwareMarkovApplied = true;
                    audit.pressureAwareMarkovTransitions++;
                    audit.pressureAwareMarkovPenaltyTotal += pressureAwareMarkovPenalty;
                    audit.pressureAwareMarkovMaxDeathsOnSlot = Mathf.Max(
                        audit.pressureAwareMarkovMaxDeathsOnSlot,
                        slot.deathsAttributedToSlot);
                    AddPressureAwareReason(ref audit, slot.transitionPressureReason);
                }

                table.UpdateWeight(prev2, prev1, currentTag, band, slotQuality, markovLearningRate);
                audit.markovTransitionsUpdated++;
            }

            prev2 = prev1;
            prev1 = currentTag;
        }

        table.DecayTowardBaseline(markovDecayRate);

        if (table.TrySave(out string saveMessage))
            Debug.Log($"LevelManager: Saved learned Markov weights — {saveMessage}");
        else
            Debug.LogWarning($"LevelManager: Failed to save Markov weights — {saveMessage}");

        return audit;
    }

    private bool ShouldApplyPressureAwareMarkovPenalty(LevelRunLog.SlotRecord slot)
    {
        // only penalizes pressure transitions confirmed by repeated deaths.
        if (!pressureAwareMarkovLearningEnabled || pressureAwareMarkovPenalty <= 0f)
            return false;

        if (slot == null || !slot.transitionPressurePenalized)
            return false;

        if (slot.deathsAttributedToSlot < Mathf.Max(1, pressureAwareMarkovDeathThreshold))
            return false;

        return IsStrongOrSeverePressure(slot.transitionPressureSeverity);
    }

    private bool IsStrongOrSeverePressure(string severity)
    {
        return severity == "strong" || severity == "severe";
    }

    private void AddPressureAwareReason(ref MarkovLearningAudit audit, string reason)
    {
        if (string.IsNullOrEmpty(reason))
            reason = "unknown_pressure";

        if (string.IsNullOrEmpty(audit.pressureAwareMarkovReasons))
        {
            audit.pressureAwareMarkovReasons = reason;
            return;
        }

        string paddedReasons = "|" + audit.pressureAwareMarkovReasons + "|";
        if (!paddedReasons.Contains("|" + reason + "|"))
            audit.pressureAwareMarkovReasons += "|" + reason;
    }

    private void ApplyMarkovLearningAudit(LevelRunLog.AdaptationRecord record, MarkovLearningAudit audit)
    {
        if (record == null)
            return;

        record.markovLearningApplied = audit.markovLearningApplied;
        record.markovPositiveReinforcementCapped = audit.markovPositiveReinforcementCapped;
        record.markovLearningQuality = audit.markovLearningQuality;
        record.markovDeliveredTargetDelta = audit.markovDeliveredTargetDelta;
        record.markovTransitionsUpdated = audit.markovTransitionsUpdated;
        record.pressureAwareMarkovApplied = audit.pressureAwareMarkovApplied;
        record.pressureAwareMarkovTransitions = audit.pressureAwareMarkovTransitions;
        record.pressureAwareMarkovMaxDeathsOnSlot = audit.pressureAwareMarkovMaxDeathsOnSlot;
        record.pressureAwareMarkovPenaltyTotal = audit.pressureAwareMarkovPenaltyTotal;
        record.pressureAwareMarkovReasons = audit.pressureAwareMarkovReasons;
    }

    private LevelRunLog.AdaptationRecord UpdateTargetDifficulty(float deathsPerChunk, float timePerChunk, BehaviourSummary behaviour)
    {
        // delegates difficulty decisions to the evidence-based controller.
        AdaptiveDifficultyController.Settings settings = CreateAdaptationSettings();
        AdaptiveDifficultyController.Decision decision = AdaptiveDifficultyController.Evaluate(
            new AdaptiveDifficultyController.Input
            {
                settings = settings,
                targetBefore = levelGenerator.targetDifficulty,
                actualDifficulty = levelGenerator.LevelDifficultyScore,
                deathsThisLevel = deathsThisLevel,
                deathsPerChunk = deathsPerChunk,
                timePerChunk = timePerChunk,
                cleanRunStreakBefore = cleanRunStreak,
                hasPreviousSmoothedStrain = hasRecentStrainScore,
                previousSmoothedStrain = recentStrainScore,
                behaviour = behaviour
            });

        levelGenerator.targetDifficulty = decision.targetAfter;
        cleanRunStreak = decision.cleanRunStreakAfter;
        recentStrainScore = decision.smoothedStrain;
        hasRecentStrainScore = true;

        lastTargetAfterAdapt = decision.targetAfter;
        lastAdaptationDecision = decision.decisionText;

        return new LevelRunLog.AdaptationRecord
        {
            adaptiveEnabled = true,
            targetBefore = decision.targetBefore,
            targetAfter = decision.targetAfter,
            decisionCode = decision.decisionCode,
            decisionText = decision.decisionText,
            deathsPerChunk = deathsPerChunk,
            timePerChunk = timePerChunk,
            actualDifficulty = levelGenerator.LevelDifficultyScore,
            actualTargetDelta = decision.actualTargetDelta,
            performanceStrain = decision.performanceStrain,
            smoothedStrain = decision.smoothedStrain,
            cleanRun = decision.cleanRun,
            comfortRun = decision.comfortRun,
            lowSignalDeathRun = decision.lowSignalDeathRun,
            tooHard = decision.tooHard,
            tooEasySingleRun = decision.tooEasySingleRun,
            tooEasyByStreak = decision.tooEasyByStreak,
            tooEasyComfortStreak = decision.tooEasyComfortStreak,
            actualDifficultyOvershoot = decision.actualDifficultyOvershoot,
            actualDifficultyUndershoot = decision.actualDifficultyUndershoot,
            increaseBlockedByActualOvershoot = decision.increaseBlockedByActualOvershoot,
            minorErrorGuardApplied = decision.minorErrorGuardApplied,
            cleanRunStreakBefore = decision.cleanRunStreakBefore,
            cleanRunStreakAfter = decision.cleanRunStreakAfter,
            controllerName = "EvidenceBasedController",
            evidenceSummary = decision.evidenceSummary,
            hardDeathsPerChunkThreshold = hardDeathsPerChunkThreshold,
            slowTimePerChunkThreshold = slowTimePerChunkThreshold,
            easyDeathsPerChunkThreshold = easyDeathsPerChunkThreshold,
            fastTimePerChunkThreshold = fastTimePerChunkThreshold,
            targetDifficultyStep = targetDifficultyStep,
            cleanRunStreakThreshold = cleanRunStreakThreshold,
            actualDifficultyOvershootTolerance = actualDifficultyOvershootTolerance,
            actualDifficultyUndershootTolerance = actualDifficultyUndershootTolerance,
            strainSmoothing = strainSmoothing
        };
    }

    private AdaptiveDifficultyController.Settings CreateAdaptationSettings()
    {
        return new AdaptiveDifficultyController.Settings
        {
            minTargetDifficulty = minTargetDifficulty,
            maxTargetDifficulty = maxTargetDifficulty,
            targetDifficultyStep = targetDifficultyStep,
            hardDeathsPerChunkThreshold = hardDeathsPerChunkThreshold,
            slowTimePerChunkThreshold = slowTimePerChunkThreshold,
            easyDeathsPerChunkThreshold = easyDeathsPerChunkThreshold,
            fastTimePerChunkThreshold = fastTimePerChunkThreshold,
            cleanRunStreakThreshold = cleanRunStreakThreshold,
            actualDifficultyOvershootTolerance = actualDifficultyOvershootTolerance,
            actualDifficultyUndershootTolerance = actualDifficultyUndershootTolerance,
            strainSmoothing = strainSmoothing
        };
    }

    private LevelRunLog.AdaptationRecord CreateAdaptiveOffRecord(float deathsPerChunk, float timePerChunk)
    {
        // logs evaluation evidence even when adaptation is disabled.
        float actualTargetDelta = levelGenerator.LevelDifficultyScore - lastTargetBeforeAdapt;

        return new LevelRunLog.AdaptationRecord
        {
            adaptiveEnabled = false,
            targetBefore = lastTargetBeforeAdapt,
            targetAfter = levelGenerator.targetDifficulty,
            decisionCode = "adaptive_off",
            decisionText = "Adaptive OFF",
            deathsPerChunk = deathsPerChunk,
            timePerChunk = timePerChunk,
            actualDifficulty = levelGenerator.LevelDifficultyScore,
            actualTargetDelta = actualTargetDelta,
            performanceStrain = 0f,
            smoothedStrain = hasRecentStrainScore ? recentStrainScore : 0f,
            cleanRun = deathsThisLevel == 0 && timePerChunk < fastTimePerChunkThreshold,
            comfortRun = deathsThisLevel == 0 && timePerChunk < fastTimePerChunkThreshold,
            lowSignalDeathRun = false,
            tooHard = false,
            tooEasySingleRun = false,
            tooEasyByStreak = false,
            actualDifficultyOvershoot = actualTargetDelta > actualDifficultyOvershootTolerance,
            actualDifficultyUndershoot = actualTargetDelta < -actualDifficultyUndershootTolerance,
            increaseBlockedByActualOvershoot = false,
            minorErrorGuardApplied = false,
            cleanRunStreakBefore = cleanRunStreak,
            cleanRunStreakAfter = cleanRunStreak,
            controllerName = "AdaptiveOff",
            evidenceSummary = "adaptive disabled",
            hardDeathsPerChunkThreshold = hardDeathsPerChunkThreshold,
            slowTimePerChunkThreshold = slowTimePerChunkThreshold,
            easyDeathsPerChunkThreshold = easyDeathsPerChunkThreshold,
            fastTimePerChunkThreshold = fastTimePerChunkThreshold,
            targetDifficultyStep = targetDifficultyStep,
            cleanRunStreakThreshold = cleanRunStreakThreshold,
            actualDifficultyOvershootTolerance = actualDifficultyOvershootTolerance,
            actualDifficultyUndershootTolerance = actualDifficultyUndershootTolerance,
            strainSmoothing = strainSmoothing
        };
    }

    private void FinalizeAndWriteCurrentRunLog(
        float deathsPerChunk,
        float timePerChunk,
        LevelRunLog.AdaptationRecord adaptationRecord)
    {
        // writes the completed run into the jsonl evaluation log.
        LevelRunLog.RunRecord runRecord = levelGenerator != null ? levelGenerator.CurrentRunLog : null;
        if (runRecord == null)
        {
            Debug.LogWarning("LEVEL RUN LOGGING FAILED | No active run record was available at completion.");
            return;
        }

        runRecord.chunkCountThisLevel = levelGenerator.ChunkCountThisLevel;
        runRecord.levelTimeSeconds = completedTime;
        runRecord.deathsThisLevel = completedDeaths;
        runRecord.deathsPerChunk = deathsPerChunk;
        runRecord.timePerChunk = timePerChunk;
        runRecord.actualLevelDifficultyScore = completedActualDifficulty;
        runRecord.avgChunkDifficulty = completedAvgDifficulty;
        runRecord.hazardChunkCount = completedHazards;
        runRecord.totalEstimatedJumps = completedJumps;
        runRecord.verticalChunkCount = completedVertical;
        runRecord.adaptation = adaptationRecord ?? new LevelRunLog.AdaptationRecord();

        if (LevelRunLog.TryAppendRun(runRecord, out string outputPath))
        {
            Debug.Log(
                $"LEVEL RUN LOGGED | path={outputPath} | runId={runRecord.runId} | seed={runRecord.runSeed} | " +
                $"slots={runRecord.slots.Count} | deaths={runRecord.deathsThisLevel}"
            );
        }
        else
        {
            Debug.LogWarning(
                $"LEVEL RUN LOGGING FAILED | runId={runRecord.runId} | seed={runRecord.runSeed} | error={outputPath}"
            );
        }
    }

    private void ResetStats()
    {
        deathsThisLevel = 0;
        levelTimer = 0f;

        deathsByChunk.Clear();
        deathEvents.Clear();
    }

    private void RespawnPlayer(bool resetVelocity)
    {
        if (player == null) return;

        player.transform.position = (playerSpawnPoint != null) ? playerSpawnPoint.position : Vector3.zero;

        if (resetVelocity)
        {
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
    }

    private void RepositionKillZoneToLevel()
    {
        if (killZoneTransform == null) return;

        float y = levelGenerator.LowestSolidY - killZonePaddingBelowGround;

        float leftX = Mathf.Min(levelGenerator.FirstEntryWorld.x, levelGenerator.LastExitWorld.x) - killZoneExtraBeyondEnds;
        float rightX = Mathf.Max(levelGenerator.FirstEntryWorld.x, levelGenerator.LastExitWorld.x) + killZoneExtraBeyondEnds;

        float centerX = (leftX + rightX) * 0.5f;
        float widthFromEnds = Mathf.Max(10f, rightX - leftX);

        Bounds b = levelGenerator.LevelWorldBounds;
        float widthFromBounds = Mathf.Max(10f, b.size.x + killZoneExtraWidth);

        float finalWidth = Mathf.Max(widthFromEnds, widthFromBounds);

        Vector3 pos = killZoneTransform.position;
        pos.x = centerX;
        pos.y = y;
        killZoneTransform.position = pos;

        BoxCollider2D box = killZoneTransform.GetComponent<BoxCollider2D>();
        if (box != null)
        {
            box.size = new Vector2(finalWidth, box.size.y);
            box.offset = Vector2.zero;
        }
    }

    private void LogChunkDeathBreakdown()
    {
        if (deathsByChunk.Count == 0)
        {
            Debug.Log("CHUNK DEATH BREAKDOWN | No chunk-attributed deaths this level.");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("CHUNK DEATH BREAKDOWN:");

        foreach (var kvp in deathsByChunk)
        {
            ChunkDeathStat stat = kvp.Value;
            sb.AppendLine(
                $"- chunk={stat.chunkName} | tag={stat.primaryTag} | diff={stat.difficultyRating} | deaths={stat.deaths}"
            );
        }

        Debug.Log(sb.ToString());
    }

    private void LogDeathEvents()
    {
        if (deathEvents.Count == 0)
        {
            Debug.Log("DEATH EVENTS | No deaths this level.");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("DEATH EVENTS:");

        for (int i = 0; i < deathEvents.Count; i++)
        {
            DeathEventInfo e = deathEvents[i];
            sb.AppendLine(
                $"- #{i + 1} | source={e.source} | chunk={e.chunkName} | tag={(e.primaryTag.HasValue ? e.primaryTag.Value.ToString() : "None")} | time={e.timeOfDeath:F2}s"
            );
        }

        Debug.Log(sb.ToString());
    }

    private string CleanChunkName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return "UnknownChunk";
        return rawName.Replace("(Clone)", "").Trim();
    }

    private void OnGUI()
    {
        if (!showHud || levelGenerator == null) return;
        if (!gameplayStarted) return;

        if (!advancedHud)
        {
            DrawCompactHud();
            return;
        }

        const float pad = 10f;
        Rect r = new Rect(pad, pad, 620f, waitingForNextLevelChoice ? 450f : 370f);

        float shownActual = waitingForNextLevelChoice ? completedActualDifficulty : levelGenerator.LevelDifficultyScore;
        float shownDelta = shownActual - levelGenerator.targetDifficulty;

        string text =
            $"Level: {sessionLevelNumber}\n" +
            $"Level Time: {levelTimer:F2}s\n" +
            $"Deaths: {deathsThisLevel}\n\n" +
            $"Current Target Difficulty: {levelGenerator.targetDifficulty:F2}\n" +
            $"Shown Actual Difficulty: {shownActual:F2}\n" +
            $"Shown Delta: {shownDelta:+0.00;-0.00;0.00}\n\n" +
            $"Chunks: {(waitingForNextLevelChoice ? completedChunkCount : levelGenerator.ChunkCountThisLevel)}\n" +
            $"Avg Chunk Difficulty: {(waitingForNextLevelChoice ? completedAvgDifficulty : levelGenerator.AvgChunkDifficulty):F2}\n" +
            $"Hazard Chunks: {(waitingForNextLevelChoice ? completedHazards : levelGenerator.HazardChunkCount)}\n" +
            $"Estimated Jumps: {(waitingForNextLevelChoice ? completedJumps : levelGenerator.TotalEstimatedJumps)}\n" +
            $"Vertical Chunks: {(waitingForNextLevelChoice ? completedVertical : levelGenerator.VerticalChunkCount)}\n\n" +
            $"Last Deaths/Chunk: {lastDeathsPerChunk:F2}\n" +
            $"Last Time/Chunk: {lastTimePerChunk:F2}\n" +
            $"Last Target Before: {lastTargetBeforeAdapt:F2}\n" +
            $"Last Target After: {lastTargetAfterAdapt:F2}\n" +
            $"Last Decision: {lastAdaptationDecision}\n" +
            $"Recent Strain: {(hasRecentStrainScore ? recentStrainScore : 0f):F2}\n" +
            $"Comfort Streak: {cleanRunStreak} / {cleanRunStreakThreshold}";

        if (waitingForNextLevelChoice)
        {
            text += $"\n\nPAUSED AFTER LEVEL COMPLETE" +
                    $"\nPress {continueKey} for Next Level";
        }

        GUI.Box(r, text);
    }

    private void DrawCompactHud()
    {
        const float pad = 10f;
        Rect r = new Rect(pad, pad, 280f, waitingForNextLevelChoice ? 150f : 110f);

        string text =
            $"Level: {sessionLevelNumber}\n" +
            $"Difficulty: {GetDifficultyBandLabel(levelGenerator.targetDifficulty)}\n" +
            $"Deaths: {deathsThisLevel}\n" +
            $"Time: {levelTimer:F1}s";

        if (waitingForNextLevelChoice)
        {
            text += $"\n\nLevel Complete" +
                    $"\nEnter: Next";
        }

        GUI.Box(r, text);
    }

    private string GetDifficultyBandLabel(float targetDifficulty)
    {
        if (targetDifficulty < 4.5f)
            return "Easy";

        if (targetDifficulty > 6f)
            return "Hard";

        return "Medium";
    }

    private void OnDisable()
    {
        if (Time.timeScale == 0f)
            Time.timeScale = 1f;
    }
}
