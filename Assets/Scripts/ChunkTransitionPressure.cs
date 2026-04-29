using UnityEngine;

public static class ChunkTransitionPressure
{
    private const float SeverePenalty = 0.35f;
    private const float StrongPenalty = 0.5f;
    private const float ModeratePenalty = 0.7f;
    private const float LightPenalty = 0.85f;

    private const float Low = 0.2f;
    private const float MediumLow = 0.4f;
    private const float Medium = 0.55f;
    private const float MediumHigh = 0.75f;
    private const float High = 1f;

    public static float GetSelectionWeightMultiplier(
        string previousChunkName,
        ChunkTag previousTag,
        string candidateChunkName,
        ChunkTag candidateTag,
        int candidateDifficulty,
        float slotTargetDifficulty,
        float levelTargetDifficulty)
    {
        if (string.IsNullOrEmpty(previousChunkName))
            return 1f;

        ChunkTransitionProfile previous = GetProfile(previousChunkName, previousTag);
        ChunkTransitionProfile candidate = GetProfile(candidateChunkName, candidateTag);

        float pressureContext = Mathf.Min(slotTargetDifficulty, levelTargetDifficulty);
        bool lowOrMidTarget = pressureContext <= 5f;
        bool lowTarget = pressureContext <= 3.5f;

        float transitionRisk = GetTransitionRisk(previous, candidate);

        if (lowOrMidTarget)
        {
            if (previous.isPrecisionSinglePlatform && candidate.isBasicSpike)
                return lowTarget ? SeverePenalty : StrongPenalty;

            if (previous.isBasicSpike && candidate.isPrecisionSinglePlatform)
                return lowTarget ? SeverePenalty : StrongPenalty;

            if (previous.hasAwkwardExit && candidate.isBasicSpike)
                return lowTarget ? StrongPenalty : ModeratePenalty;

            if (previous.isPrecisionSinglePlatform && candidate.isRecoverableSpike)
                return lowTarget ? StrongPenalty : ModeratePenalty;

            if (transitionRisk >= 0.6f)
                return lowTarget ? StrongPenalty : ModeratePenalty;

            if (transitionRisk >= 0.4f)
                return lowTarget ? ModeratePenalty : LightPenalty;
        }

        if (candidateDifficulty > slotTargetDifficulty + 1.5f && GetExitRisk(previous) >= 0.6f)
            return LightPenalty;

        return 1f;
    }

    public static bool IsPressurePenalizedTransition(
        string previousChunkName,
        ChunkTag previousTag,
        string candidateChunkName,
        ChunkTag candidateTag,
        int candidateDifficulty,
        float slotTargetDifficulty,
        float levelTargetDifficulty)
    {
        return GetSelectionWeightMultiplier(
            previousChunkName,
            previousTag,
            candidateChunkName,
            candidateTag,
            candidateDifficulty,
            slotTargetDifficulty,
            levelTargetDifficulty) < 0.999f;
    }

    public static string GetTransitionReason(
        string previousChunkName,
        ChunkTag previousTag,
        string candidateChunkName,
        ChunkTag candidateTag,
        int candidateDifficulty,
        float slotTargetDifficulty,
        float levelTargetDifficulty)
    {
        if (string.IsNullOrEmpty(previousChunkName))
            return "none";

        ChunkTransitionProfile previous = GetProfile(previousChunkName, previousTag);
        ChunkTransitionProfile candidate = GetProfile(candidateChunkName, candidateTag);

        float pressureContext = Mathf.Min(slotTargetDifficulty, levelTargetDifficulty);
        bool lowOrMidTarget = pressureContext <= 5f;

        if (lowOrMidTarget)
        {
            if (previous.isPrecisionSinglePlatform && candidate.isBasicSpike)
                return "precision_low_recovery_to_spikes";

            if (previous.isBasicSpike && candidate.isPrecisionSinglePlatform)
                return "spikes_to_precision_low_recovery";

            if (previous.hasAwkwardExit && candidate.isBasicSpike)
                return "awkward_exit_to_spikes";

            if (previous.isPrecisionSinglePlatform && candidate.isRecoverableSpike)
                return "precision_to_recoverable_spikes";

            float transitionRisk = GetTransitionRisk(previous, candidate);
            if (transitionRisk >= 0.6f)
                return "low_recovery_to_harsh_entry";

            if (transitionRisk >= 0.4f)
                return "moderate_recovery_pressure";
        }

        if (candidateDifficulty > slotTargetDifficulty + 1.5f && GetExitRisk(previous) >= 0.6f)
            return "pressure_to_over_target_chunk";

        return "none";
    }

    public static string GetSeverityFromMultiplier(float multiplier)
    {
        if (multiplier <= SeverePenalty + 0.001f)
            return "severe";

        if (multiplier <= StrongPenalty + 0.001f)
            return "strong";

        if (multiplier <= ModeratePenalty + 0.001f)
            return "moderate";

        if (multiplier <= LightPenalty + 0.001f)
            return "light";

        return "none";
    }

    public static float GetPressureScoreFromMultiplier(float multiplier)
    {
        if (multiplier <= SeverePenalty + 0.001f)
            return 1f;

        if (multiplier <= StrongPenalty + 0.001f)
            return 0.75f;

        if (multiplier <= ModeratePenalty + 0.001f)
            return 0.5f;

        if (multiplier <= LightPenalty + 0.001f)
            return 0.25f;

        return 0f;
    }

    public static bool IsHighPressure(string chunkName, ChunkTag tag)
    {
        ChunkTransitionProfile profile = GetProfile(chunkName, tag);
        return profile.internalPressure >= MediumHigh ||
               profile.entryPressure >= MediumHigh ||
               profile.exitPressure >= MediumHigh;
    }

    public static bool HasPoorExitRecovery(string chunkName, ChunkTag tag)
    {
        return GetProfile(chunkName, tag).exitRecovery <= MediumLow;
    }

    public static bool HasHarshEntry(string chunkName, ChunkTag tag)
    {
        ChunkTransitionProfile profile = GetProfile(chunkName, tag);
        return profile.entryRecovery <= Medium || profile.entryPressure >= MediumHigh;
    }

    public static bool HasAwkwardExit(string chunkName, ChunkTag tag)
    {
        return GetProfile(chunkName, tag).hasAwkwardExit;
    }

    private static ChunkTransitionProfile GetProfile(string chunkName, ChunkTag tag)
    {
        string normalized = NormalizeName(chunkName);

        if (normalized.Contains("Chunk_Flat_Tilemap"))
            return Profile(High, High, 0f, 0f, 0f);

        if (normalized.Contains("Chunk_Gap_Tilemap") || normalized.Contains("Generated_Gap"))
            return Profile(High, High, Low, Low, Low);

        if (normalized.Contains("Chunk_GapMedium_Tilemap"))
            return Profile(High, High, Low, Low, Low);

        if (normalized.Contains("Chunk_GapHard_Tilemap"))
            return Profile(High, High, Low, Low, Low);

        if (normalized.Contains("Chunk_DashJump_Tilemap"))
            return Profile(High, High, Medium, Medium, Medium);

        if (normalized.Contains("Chunk_Spikes_Tilemap"))
            return Profile(Low, Low, High, High, High, isBasicSpike: true);

        if (normalized.Contains("Chunk_SpikesMedium_Tilemap"))
            return Profile(Medium, Low, MediumHigh, High, High, isRecoverableSpike: true);

        if (normalized.Contains("Chunk_MovingSpikes_Tilemap"))
            return Profile(Medium, Medium, Medium, Medium, MediumHigh);

        if (normalized.Contains("Chunk_ElevatedPlatform_Tilemap"))
            return Profile(High, High, Low, Low, Low);

        if (normalized.Contains("Chunk_ElevatedDoublePlatform_Tilemap"))
            return Profile(High, High, MediumLow, Low, MediumLow);

        if (normalized.Contains("Chunk_PrecisionJump_Tilemap"))
            return Profile(Low, Low, MediumHigh, MediumHigh, MediumHigh, isPrecisionSinglePlatform: true);

        if (normalized.Contains("Chunk_MovingClimbSpikes_Tilemap"))
            return Profile(High, Low, High, High, High);

        if (normalized.Contains("Chunk_SpikeDash_Tilemap"))
            return Profile(High, High, High, High, High);

        if (normalized.Contains("Chunk_StairsUp_Tilemap"))
            return Profile(MediumHigh, MediumLow, Low, Low, Low, hasAwkwardExit: true);

        if (normalized.Contains("Chunk_VerticalDown_Tilemap"))
            return Profile(MediumHigh, MediumHigh, Low, Low, Low);

        if (normalized.Contains("Chunk_VerticalUp_Tilemap"))
            return Profile(MediumHigh, MediumLow, Low, Low, Low, hasAwkwardExit: true);

        return GetFallbackProfile(tag);
    }

    private static ChunkTransitionProfile GetFallbackProfile(ChunkTag tag)
    {
        switch (tag)
        {
            case ChunkTag.Rest:
            case ChunkTag.Safe:
                return Profile(High, High, 0f, 0f, 0f);
            case ChunkTag.Gap:
                return Profile(High, High, Low, Low, Low);
            case ChunkTag.Vertical:
                return Profile(MediumHigh, Medium, Low, Low, Low);
            case ChunkTag.Precision:
                return Profile(Medium, Medium, MediumHigh, MediumHigh, MediumHigh);
            case ChunkTag.Spikes:
                return Profile(Medium, MediumLow, MediumHigh, MediumHigh, High);
            default:
                return Profile(Medium, Medium, Medium, Medium, Medium);
        }
    }

    private static ChunkTransitionProfile Profile(
        float entryRecovery,
        float exitRecovery,
        float entryPressure,
        float exitPressure,
        float internalPressure,
        bool isBasicSpike = false,
        bool isRecoverableSpike = false,
        bool isPrecisionSinglePlatform = false,
        bool hasAwkwardExit = false)
    {
        return new ChunkTransitionProfile
        {
            entryRecovery = entryRecovery,
            exitRecovery = exitRecovery,
            entryPressure = entryPressure,
            exitPressure = exitPressure,
            internalPressure = internalPressure,
            isBasicSpike = isBasicSpike,
            isRecoverableSpike = isRecoverableSpike,
            isPrecisionSinglePlatform = isPrecisionSinglePlatform,
            hasAwkwardExit = hasAwkwardExit
        };
    }

    private static float GetTransitionRisk(ChunkTransitionProfile previous, ChunkTransitionProfile candidate)
    {
        return GetExitRisk(previous) * GetEntryRisk(candidate);
    }

    private static float GetExitRisk(ChunkTransitionProfile profile)
    {
        return ((1f - profile.exitRecovery) * 0.7f) + (profile.exitPressure * 0.3f);
    }

    private static float GetEntryRisk(ChunkTransitionProfile profile)
    {
        return ((1f - profile.entryRecovery) * 0.6f) + (profile.entryPressure * 0.4f);
    }

    private static string NormalizeName(string chunkName)
    {
        if (string.IsNullOrEmpty(chunkName))
            return string.Empty;

        return chunkName.Replace("(Clone)", string.Empty).Trim();
    }

    private struct ChunkTransitionProfile
    {
        public float entryRecovery;
        public float exitRecovery;
        public float entryPressure;
        public float exitPressure;
        public float internalPressure;
        public bool isBasicSpike;
        public bool isRecoverableSpike;
        public bool isPrecisionSinglePlatform;
        public bool hasAwkwardExit;
    }
}
