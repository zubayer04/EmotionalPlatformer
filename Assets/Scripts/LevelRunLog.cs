using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class LevelRunLog
{
    public const int SchemaVersion = 13;

    [Serializable]
    public class RunRecord
    {
        public int schemaVersion = SchemaVersion;
        public string runId;
        public string generatedAtUtc;
        public int runSeed;
        public bool isReplay;
        public int replaySourceSeed;

        public float targetDifficultyBeforeRun;
        public float startDifficultyBias;
        public string generationMode;
        public float difficultyPreferenceStrength;
        public bool useTwoStepMarkov;
        public bool useLookaheadSequencePlanning;
        public int lookaheadDepth;
        public int lookaheadBeamWidth;
        public bool useStructuralBudgetPenalty;
        public float structuralBudgetSlack;
        public float structuralBudgetPenaltyStrength;
        public bool useGeneratedBlueprintChunks;
        public bool useGeneratedBlueprintCandidateSelection;
        public float generatedChunkReplacementChance;
        public int generatedCandidateVariantsPerSource;
        public float generatedCandidateFamilyWeight;
        public int totalChunksConfigured;
        public int chunkCountThisLevel;

        public float levelTimeSeconds;
        public int deathsThisLevel;
        public float deathsPerChunk;
        public float timePerChunk;
        public float actualLevelDifficultyScore;
        public float avgChunkDifficulty;
        public int hazardChunkCount;
        public int totalEstimatedJumps;
        public int verticalChunkCount;
        public int transitionPressureCount;
        public int highPressureTransitionCount;
        public float transitionPressureScore;

        public List<SlotRecord> slots = new List<SlotRecord>();
        public List<DeathEventRecord> deathEvents = new List<DeathEventRecord>();
        public AdaptationRecord adaptation = new AdaptationRecord();
    }

    [Serializable]
    public class SlotRecord
    {
        public int sequenceIndex;
        public int generatedSlotIndex = -1;
        public bool isStartingChunk;
        public bool hasSlotTargetDifficulty;
        public float slotTargetDifficulty;

        public string selectedPrefabName;
        public string selectedCandidateType;
        public string selectedSourcePrefabName;
        public string selectedGeneratedBlueprintName;
        public string selectedPrimaryTag;
        public int selectedDifficulty = -1;
        public bool selectedHasHazard;
        public int selectedEstimatedJumps;
        public Vector2 selectedExitDelta;
        public bool lookaheadUsed;
        public int lookaheadDepthUsed;
        public float lookaheadBestScore;
        public float lookaheadSelectionWeight;
        public string lookaheadDecisionSummary;
        public float structuralBudgetWeight = 1f;
        public float structuralBudgetProjectedLoad;
        public float structuralBudgetAllowedLoad;

        public bool spawnSucceeded;
        public string spawnedChunkName;
        public string spawnedPrimaryTag;
        public int spawnedDifficulty = -1;
        public bool spawnedHasHazard;
        public int spawnedEstimatedJumps;
        public Vector2 spawnedExitDelta;

        public bool replacementAttempted;
        public bool replacementSucceeded;
        public string replacementMode;
        public string replacementReason;
        public string generatedRejectionReason;
        public string generatedBlueprintName;
        public string generatedBlueprintRows;
        public string generatedBlueprintFeatureSummary;
        public int generatedBlueprintWidth;
        public int generatedBlueprintHeight;
        public int generatedBlueprintGapCount;
        public int generatedBlueprintMaxGapWidth;
        public int generatedBlueprintMinLandingWidth;
        public int generatedBlueprintSolidCount;
        public int generatedBlueprintHazardCount;
        public Vector2 generatedBlueprintEstimatedExitDelta;

        public bool hasPreviousTransition;
        public string previousSpawnedChunkName;
        public float transitionPressureMultiplier = 1f;
        public bool transitionPressurePenalized;
        public string transitionPressureReason;
        public string transitionPressureSeverity;
        public float transitionPressureScore;

        public int deathsAttributedToSlot;
    }

    [Serializable]
    public class DeathEventRecord
    {
        public string source;
        public string chunkName;
        public string primaryTag;
        public int slotIndex = -1;
        public float timeOfDeathSeconds;
    }

    [Serializable]
    public class AdaptationRecord
    {
        public bool adaptiveEnabled;
        public float targetBefore;
        public float targetAfter;
        public string decisionCode;
        public string decisionText;

        public float deathsPerChunk;
        public float timePerChunk;
        public float actualDifficulty;
        public float actualTargetDelta;
        public float performanceStrain;
        public float smoothedStrain;

        public bool cleanRun;
        public bool comfortRun;
        public bool lowSignalDeathRun;
        public bool tooHard;
        public bool tooEasySingleRun;
        public bool tooEasyByStreak;
        public bool tooEasyComfortStreak;
        public bool actualDifficultyOvershoot;
        public bool actualDifficultyUndershoot;
        public bool increaseBlockedByActualOvershoot;
        public bool minorErrorGuardApplied;
        public int cleanRunStreakBefore;
        public int cleanRunStreakAfter;

        public string controllerName;
        public string evidenceSummary;
        public float hardDeathsPerChunkThreshold;
        public float slowTimePerChunkThreshold;
        public float easyDeathsPerChunkThreshold;
        public float fastTimePerChunkThreshold;
        public float targetDifficultyStep;
        public int cleanRunStreakThreshold;
        public float actualDifficultyOvershootTolerance;
        public float actualDifficultyUndershootTolerance;
        public float strainSmoothing;

        // Behavioural signals
        public float hesitationScore;
        public float momentumFluidity;
        public float directionReversalRate;
        public float avgRetryDelay;
        public float deathClusteringRatio;
        public float engagementScore;
        public int behaviourChunksTraversed;
        public int behaviourTraversalFrames;

        // Markov learning audit
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

    public static string GetLogDirectoryPath()
    {
        return Path.Combine(Application.persistentDataPath, "RunLogs");
    }

    public static string GetLogFilePath()
    {
        return Path.Combine(GetLogDirectoryPath(), "level_runs.jsonl");
    }

    public static bool TryAppendRun(RunRecord run, out string message)
    {
        try
        {
            string directory = GetLogDirectoryPath();
            Directory.CreateDirectory(directory);

            string path = GetLogFilePath();
            string json = JsonUtility.ToJson(run);

            File.AppendAllText(path, json + Environment.NewLine);
            message = path;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public static string CleanName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return "Unknown";

        return rawName.Replace("(Clone)", string.Empty).Trim();
    }
}
