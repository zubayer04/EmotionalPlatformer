using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class LevelRunLog
{
    public const int SchemaVersion = 1;

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
        public float difficultyPreferenceStrength;
        public bool useTwoStepMarkov;
        public bool useGeneratedBlueprintChunks;
        public float generatedChunkReplacementChance;
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
        public string selectedPrimaryTag;
        public int selectedDifficulty = -1;
        public bool selectedHasHazard;
        public int selectedEstimatedJumps;
        public Vector2 selectedExitDelta;

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
        public string generatedBlueprintName;

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

        public bool cleanRun;
        public bool tooHard;
        public bool tooEasySingleRun;
        public bool tooEasyByStreak;
        public int cleanRunStreakBefore;
        public int cleanRunStreakAfter;

        public float hardDeathsPerChunkThreshold;
        public float slowTimePerChunkThreshold;
        public float easyDeathsPerChunkThreshold;
        public float fastTimePerChunkThreshold;
        public float targetDifficultyStep;
        public int cleanRunStreakThreshold;
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
