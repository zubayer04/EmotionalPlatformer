using System;
using UnityEngine;

public static class AdaptiveDifficultyController
{
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
        public bool tooHard;
        public bool tooEasySingleRun;
        public bool tooEasyByStreak;
        public bool actualDifficultyOvershoot;
        public bool actualDifficultyUndershoot;
        public bool increaseBlockedByActualOvershoot;

        public int cleanRunStreakBefore;
        public int cleanRunStreakAfter;
    }

    public static Decision Evaluate(Input input)
    {
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
            settings.slowTimePerChunkThreshold);

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

        bool easyPerformance =
            input.deathsPerChunk <= settings.easyDeathsPerChunkThreshold &&
            input.timePerChunk < settings.fastTimePerChunkThreshold;

        bool tooEasySingleRun =
            easyPerformance &&
            lowPressure &&
            actualUndershoot;

        int requiredCleanRunStreak = Mathf.Max(1, settings.cleanRunStreakThreshold);
        int cleanRunStreakAfter = cleanRun
            ? input.cleanRunStreakBefore + 1
            : 0;

        bool tooEasyByStreak =
            cleanRunStreakAfter >= requiredCleanRunStreak &&
            lowPressure &&
            !actualOvershoot;

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
            cleanRunStreakAfter = 0;
            newTarget -= settings.targetDifficultyStep;
            decisionCode = "decrease_content_overshoot";
            decisionText = "Delivered level overshot target under pressure -> decrease target";
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
            decisionText = $"Clean low-strain streak ({requiredCleanRunStreak}) -> increase target";
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

        return new Decision
        {
            targetBefore = targetBefore,
            targetAfter = newTarget,
            decisionCode = decisionCode,
            decisionText = decisionText,
            evidenceSummary =
                $"strain={performanceStrain:0.00}, smoothed={smoothedStrain:0.00}, actual-target={actualTargetDelta:+0.00;-0.00;0.00}",
            performanceStrain = performanceStrain,
            smoothedStrain = smoothedStrain,
            actualTargetDelta = actualTargetDelta,
            cleanRun = cleanRun,
            tooHard = tooHard,
            tooEasySingleRun = tooEasySingleRun,
            tooEasyByStreak = tooEasyByStreak,
            actualDifficultyOvershoot = actualOvershoot,
            actualDifficultyUndershoot = actualUndershoot,
            increaseBlockedByActualOvershoot = increaseBlockedByActualOvershoot,
            cleanRunStreakBefore = input.cleanRunStreakBefore,
            cleanRunStreakAfter = cleanRunStreakAfter
        };
    }

    private static float CalculatePerformanceStrain(
        float deathsPerChunk,
        float timePerChunk,
        float hardDeathsPerChunkThreshold,
        float slowTimePerChunkThreshold)
    {
        float deathPressure = hardDeathsPerChunkThreshold > 0f
            ? deathsPerChunk / hardDeathsPerChunkThreshold
            : (deathsPerChunk > 0f ? 1f : 0f);

        float timePressure = slowTimePerChunkThreshold > 0f
            ? timePerChunk / slowTimePerChunkThreshold
            : (timePerChunk > 0f ? 1f : 0f);

        return Mathf.Clamp01(Mathf.Max(deathPressure, timePressure));
    }
}
