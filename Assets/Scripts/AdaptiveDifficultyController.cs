using System;
using UnityEngine;

public static class AdaptiveDifficultyController
{
    private const float MinorOvershootGrace = 0.5f;
    private const float ComfortIncreaseOvershootGrace = 0.75f;

    [Serializable]
    public struct Settings
    {
        public float minTargetDifficulty;
        public float maxTargetDifficulty;
        public float targetDifficultyStep;
        public float hardDeathsPerChunkThreshold;
        public float slowTimePerChunkThreshold;
        public float easyDeathsPerChunkThreshold;
        public float fastTimePerChunkThreshold;
        public int cleanRunStreakThreshold;
        public float actualDifficultyOvershootTolerance;
        public float actualDifficultyUndershootTolerance;
        public float strainSmoothing;
    }

    public struct Input
    {
        public Settings settings;
        public float targetBefore;
        public float actualDifficulty;
        public int deathsThisLevel;
        public float deathsPerChunk;
        public float timePerChunk;
        public int cleanRunStreakBefore;
        public bool hasPreviousSmoothedStrain;
        public float previousSmoothedStrain;
        public BehaviourSummary behaviour;
    }

    public struct Decision
    {
        public float targetBefore;
        public float targetAfter;
        public string decisionCode;
        public string decisionText;
        public string evidenceSummary;

        public float performanceStrain;
        public float smoothedStrain;
        public float actualTargetDelta;

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
    }

    public static Decision Evaluate(Input input)
    {
        // turns completed-run evidence into the next target difficulty.
        Settings settings = input.settings;
        float targetBefore = input.targetBefore;
        float newTarget = targetBefore;

        float actualTargetDelta = input.actualDifficulty - targetBefore;
        bool actualOvershoot = actualTargetDelta > settings.actualDifficultyOvershootTolerance;
        bool actualUndershoot = actualTargetDelta < -settings.actualDifficultyUndershootTolerance;

        float performanceStrain = CalculatePerformanceStrain(
            input.deathsPerChunk,
            input.timePerChunk,
            settings.hardDeathsPerChunkThreshold,
            settings.slowTimePerChunkThreshold,
            input.behaviour);

        float smoothing = Mathf.Clamp01(settings.strainSmoothing);
        float smoothedStrain = input.hasPreviousSmoothedStrain
            ? Mathf.Lerp(input.previousSmoothedStrain, performanceStrain, smoothing)
            : performanceStrain;

        bool tooHard = performanceStrain >= 1f || smoothedStrain >= 0.9f;
        bool cleanRun =
            input.deathsThisLevel == 0 &&
            input.timePerChunk < settings.fastTimePerChunkThreshold;

        bool lowPressure =
            performanceStrain <= 0.35f &&
            smoothedStrain <= 0.45f;

        float lowSignalDeathThreshold = Mathf.Max(
            settings.easyDeathsPerChunkThreshold,
            settings.hardDeathsPerChunkThreshold * 0.5f);
        bool lowSignalDeathRun =
            input.deathsThisLevel == 1 &&
            input.deathsPerChunk <= lowSignalDeathThreshold &&
            input.timePerChunk < settings.fastTimePerChunkThreshold &&
            lowPressure;
        bool comfortRun = cleanRun || lowSignalDeathRun;

        bool easyPerformance =
            input.deathsPerChunk <= settings.easyDeathsPerChunkThreshold &&
            input.timePerChunk < settings.fastTimePerChunkThreshold;

        bool tooEasySingleRun =
            easyPerformance &&
            lowPressure &&
            actualUndershoot;

        int requiredCleanRunStreak = Mathf.Max(1, settings.cleanRunStreakThreshold);
        int cleanRunStreakAfter = comfortRun
            ? input.cleanRunStreakBefore + 1
            : 0;

        bool tooEasyByStreak =
            cleanRunStreakAfter >= requiredCleanRunStreak &&
            lowPressure &&
            !actualOvershoot;

        bool tooEasyComfortStreak =
            cleanRunStreakAfter >= requiredCleanRunStreak &&
            lowPressure &&
            actualOvershoot &&
            actualTargetDelta <= settings.actualDifficultyOvershootTolerance + ComfortIncreaseOvershootGrace;

        bool minorErrorGuardApplied =
            actualOvershoot &&
            input.deathsThisLevel == 1 &&
            lowPressure &&
            actualTargetDelta <= settings.actualDifficultyOvershootTolerance + MinorOvershootGrace;

        bool increaseBlockedByActualOvershoot = false;
        string decisionCode;
        string decisionText;

        if (tooHard)
        {
            cleanRunStreakAfter = 0;
            newTarget -= settings.targetDifficultyStep;
            decisionCode = "decrease_high_strain";
            decisionText = "High strain -> decrease target";
        }
        else if (actualOvershoot && !cleanRun)
        {
            if (minorErrorGuardApplied)
            {
                decisionCode = "keep_minor_error_content_overshot";
                decisionText = "Single low-strain death with mild content overshoot -> keep target and preserve comfort streak";
            }
            else
            {
                cleanRunStreakAfter = 0;
                newTarget -= settings.targetDifficultyStep;
                decisionCode = "decrease_content_overshoot";
                decisionText = "Delivered level overshot target under pressure -> decrease target";
            }
        }
        else if (tooEasySingleRun)
        {
            cleanRunStreakAfter = 0;
            newTarget += settings.targetDifficultyStep;
            decisionCode = "increase_low_strain_undershoot";
            decisionText = "Low strain and delivered level below target -> increase target";
        }
        else if (tooEasyByStreak)
        {
            cleanRunStreakAfter = 0;
            newTarget += settings.targetDifficultyStep;
            decisionCode = "increase_clean_streak";
            decisionText = $"Low-strain comfort streak ({requiredCleanRunStreak}) -> increase target";
        }
        else if (tooEasyComfortStreak)
        {
            cleanRunStreakAfter = 0;
            newTarget += settings.targetDifficultyStep;
            decisionCode = "increase_comfort_streak_mild_overshoot";
            decisionText = "Sustained low-strain comfort despite mild content overshoot -> increase target";
        }
        else
        {
            if (actualOvershoot && cleanRun && easyPerformance)
                increaseBlockedByActualOvershoot = true;

            decisionCode = increaseBlockedByActualOvershoot
                ? "keep_content_overshot"
                : "keep_about_right";
            decisionText = increaseBlockedByActualOvershoot
                ? "Clean run, but delivered level already overshot target -> keep target"
                : "Evidence near target -> keep target";
        }

        newTarget = Mathf.Clamp(newTarget, settings.minTargetDifficulty, settings.maxTargetDifficulty);

        bool hasBehaviourData = HasBehaviourData(input.behaviour);
        float engagement = input.behaviour.EngagementScore();
        string behaviourSummary = hasBehaviourData
            ? $", engagement={engagement:0.00}, hesitation={input.behaviour.hesitationScore:0.00}, momentum={input.behaviour.momentumFluidity:0.00}"
            : ", behaviour=unavailable";

        return new Decision
        {
            targetBefore = targetBefore,
            targetAfter = newTarget,
            decisionCode = decisionCode,
            decisionText = decisionText,
            evidenceSummary =
                $"strain={performanceStrain:0.00}, smoothed={smoothedStrain:0.00}, actual-target={actualTargetDelta:+0.00;-0.00;0.00}" + behaviourSummary,
            performanceStrain = performanceStrain,
            smoothedStrain = smoothedStrain,
            actualTargetDelta = actualTargetDelta,
            cleanRun = cleanRun,
            comfortRun = comfortRun,
            lowSignalDeathRun = lowSignalDeathRun,
            tooHard = tooHard,
            tooEasySingleRun = tooEasySingleRun,
            tooEasyByStreak = tooEasyByStreak,
            tooEasyComfortStreak = tooEasyComfortStreak,
            actualDifficultyOvershoot = actualOvershoot,
            actualDifficultyUndershoot = actualUndershoot,
            increaseBlockedByActualOvershoot = increaseBlockedByActualOvershoot,
            minorErrorGuardApplied = minorErrorGuardApplied,
            cleanRunStreakBefore = input.cleanRunStreakBefore,
            cleanRunStreakAfter = cleanRunStreakAfter
        };
    }

    private static float CalculatePerformanceStrain(
        float deathsPerChunk,
        float timePerChunk,
        float hardDeathsPerChunkThreshold,
        float slowTimePerChunkThreshold,
        BehaviourSummary behaviour)
    {
        // combines concrete performance data with lightweight behaviour proxies.
        // classic strain signals: death and time pressure
        float deathPressure = hardDeathsPerChunkThreshold > 0f
            ? deathsPerChunk / hardDeathsPerChunkThreshold
            : (deathsPerChunk > 0f ? 1f : 0f);

        float timePressure = slowTimePerChunkThreshold > 0f
            ? timePerChunk / slowTimePerChunkThreshold
            : (timePerChunk > 0f ? 1f : 0f);

        float classicStrain = Mathf.Max(deathPressure, timePressure);

        if (!HasBehaviourData(behaviour))
            return Mathf.Clamp01(classicStrain);

        // behavioural strain signals
        // hesitation can indicate caution before upcoming challenges
        float hesitationStrain = Mathf.Clamp01(behaviour.hesitationScore * 1.5f);

        // low momentum fluidity suggests less confident movement
        float momentumStrain = Mathf.Clamp01(1f - behaviour.momentumFluidity);

        // high direction reversals suggest indecision under pressure
        float reversalStrain = Mathf.Clamp01(behaviour.directionReversalRate / 4f);

        // slow retry can suggest fatigue or frustration after death
        float retryStrain = 0f;
        if (behaviour.avgRetryDelay >= 0f)
            retryStrain = Mathf.Clamp01(behaviour.avgRetryDelay / 3f);

        // spread deaths suggest overwhelm rather than one focused challenge
        float overwhelmStrain = 0f;
        if (behaviour.deathClusteringRatio > 0f && behaviour.deathClusteringRatio < 0.5f)
            overwhelmStrain = Mathf.Clamp01((0.5f - behaviour.deathClusteringRatio) * 2f);

        // classic signals dominate, while behavioural proxies add nuance
        float behaviouralStrain = (hesitationStrain * 0.3f) +
                                  (momentumStrain * 0.25f) +
                                  (reversalStrain * 0.15f) +
                                  (retryStrain * 0.15f) +
                                  (overwhelmStrain * 0.15f);

        float combined = (classicStrain * 0.6f) + (behaviouralStrain * 0.4f);
        return Mathf.Clamp01(combined);
    }

    private static bool HasBehaviourData(BehaviourSummary behaviour)
    {
        // avoids using proxy metrics when no traversal evidence was recorded.
        return behaviour.chunksTraversed > 0 && behaviour.totalTraversalFrames > 0;
    }
}
