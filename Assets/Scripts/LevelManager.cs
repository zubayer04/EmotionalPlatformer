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

    [Header("References")]
    [SerializeField] private LevelGenerator levelGenerator;
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private GameObject player;

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
    [SerializeField] private KeyCode replayKey = KeyCode.R;

    [Header("Debug HUD")]
    [SerializeField] private bool showHud = true;

    // Debug info for the most recently completed level
    private float lastDeathsPerChunk = 0f;
    private float lastTimePerChunk = 0f;
    private float lastTargetBeforeAdapt = 0f;
    private float lastTargetAfterAdapt = 0f;
    private string lastAdaptationDecision = "None yet";

    // Pause / pending-next-level state
    private bool waitingForNextLevelChoice = false;

    // Has the currently-generated level already had its first completion logged/adapted?
    private bool currentGeneratedLevelAlreadyLogged = false;

    // Stored completion snapshot so logging stays visible while paused
    private float completedTime = 0f;
    private int completedDeaths = 0;
    private int completedChunkCount = 0;
    private float completedActualDifficulty = 0f;
    private float completedAvgDifficulty = 0f;
    private int completedHazards = 0;
    private int completedJumps = 0;
    private int completedVertical = 0;

    // Per-level logging
    private readonly Dictionary<ChunkData, ChunkDeathStat> deathsByChunk = new Dictionary<ChunkData, ChunkDeathStat>();
    private readonly List<DeathEventInfo> deathEvents = new List<DeathEventInfo>();

    // Adaptive memory
    private int cleanRunStreak = 0;
    private bool hasRecentStrainScore = false;
    private float recentStrainScore = 0f;

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

        GenerateFreshLevel();
    }

    private void Update()
    {
        if (!waitingForNextLevelChoice)
        {
            levelTimer += Time.deltaTime;
        }

        if (waitingForNextLevelChoice)
        {
            if (Input.GetKeyDown(continueKey))
            {
                ContinueToNextLevel();
            }
            else if (Input.GetKeyDown(replayKey))
            {
                ReplayCurrentLevel();
            }
        }
    }

    public void OnPlayerDied(ChunkData chunk, string source)
    {
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

        Debug.Log(
            chunk != null
                ? $"PLAYER DIED | source={source} | chunk={CleanChunkName(chunk.name)} | tag={chunk.primaryTag} | diff={chunk.difficultyRating} | levelTime={levelTimer:F2}s"
                : $"PLAYER DIED | source={source} | chunk=None | levelTime={levelTimer:F2}s"
        );

        RespawnPlayer(resetVelocity: true);
    }

    public void OnLevelCompleted()
    {
        if (waitingForNextLevelChoice) return;

        int chunkCount = Mathf.Max(1, levelGenerator.ChunkCountThisLevel);

        float deathsPerChunk = deathsThisLevel / (float)chunkCount;
        float timePerChunk = levelTimer / chunkCount;

        // Snapshot completed-level values for display/logging while paused
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
            lastDeathsPerChunk = deathsPerChunk;
            lastTimePerChunk = timePerChunk;
            lastTargetBeforeAdapt = levelGenerator.targetDifficulty;
            LevelRunLog.AdaptationRecord adaptationRecord;

            if (adaptiveDifficultyEnabled)
            {
                adaptationRecord = UpdateTargetDifficulty(deathsPerChunk, timePerChunk);
            }
            else
            {
                lastTargetAfterAdapt = levelGenerator.targetDifficulty;
                lastAdaptationDecision = "Adaptive OFF";
                adaptationRecord = CreateAdaptiveOffRecord(deathsPerChunk, timePerChunk);
            }

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

    private void GenerateFreshLevel()
    {
        Time.timeScale = 1f;
        waitingForNextLevelChoice = false;
        currentGeneratedLevelAlreadyLogged = false;

        levelGenerator.ClearLevel();
        levelGenerator.GenerateLevel();

        RepositionKillZoneToLevel();

        ResetStats();
        RespawnPlayer(resetVelocity: true);
    }

    private void ContinueToNextLevel()
    {
        GenerateFreshLevel();
    }

    private void ReplayCurrentLevel()
    {
        Time.timeScale = 1f;
        waitingForNextLevelChoice = false;

        levelGenerator.ClearLevel();
        levelGenerator.ReplayLastGeneratedLevel();

        RepositionKillZoneToLevel();

        ResetStats();
        RespawnPlayer(resetVelocity: true);
    }

    private LevelRunLog.AdaptationRecord UpdateTargetDifficulty(float deathsPerChunk, float timePerChunk)
    {
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
                previousSmoothedStrain = recentStrainScore
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
            tooHard = decision.tooHard,
            tooEasySingleRun = decision.tooEasySingleRun,
            tooEasyByStreak = decision.tooEasyByStreak,
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

        const float pad = 10f;
        Rect r = new Rect(pad, pad, 620f, waitingForNextLevelChoice ? 450f : 370f);

        float shownActual = waitingForNextLevelChoice ? completedActualDifficulty : levelGenerator.LevelDifficultyScore;
        float shownDelta = shownActual - levelGenerator.targetDifficulty;

        string text =
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
            $"Clean Run Streak: {cleanRunStreak} / {cleanRunStreakThreshold}";

        if (waitingForNextLevelChoice)
        {
            text += $"\n\nPAUSED AFTER LEVEL COMPLETE" +
                    $"\nPress {continueKey} for Next Level" +
                    $"\nPress {replayKey} to Replay Same Level";
        }

        GUI.Box(r, text);
    }

    private void OnDisable()
    {
        if (Time.timeScale == 0f)
            Time.timeScale = 1f;
    }
}
