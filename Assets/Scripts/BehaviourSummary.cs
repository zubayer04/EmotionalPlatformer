using System;
using UnityEngine;

[Serializable]
public struct BehaviourSummary
{
    // compact behaviour evidence passed from tracking into adaptation and logs.
    [Tooltip("Fraction of grounded chunk time spent near-stationary (|vx| < threshold). Higher = more anxious.")]
    public float hesitationScore;

    [Tooltip("Average |velocity.x| / maxSpeed during chunk traversal. Higher = more confident/fluid.")]
    public float momentumFluidity;

    [Tooltip("Direction-sign flips per second of chunk traversal time. Higher = more indecisive.")]
    public float directionReversalRate;

    [Tooltip("Average seconds from respawn to first meaningful movement after deaths. Lower = more engaged. -1 if no deaths.")]
    public float avgRetryDelay;

    [Tooltip("Fraction of total deaths concentrated on the single deadliest chunk. Higher = focused challenge, lower = spread overwhelm.")]
    public float deathClusteringRatio;

    [Tooltip("Number of chunks the player traversed (used for per-chunk normalization).")]
    public int chunksTraversed;

    [Tooltip("Total direction reversals observed across all chunks.")]
    public int totalDirectionReversals;

    [Tooltip("Total hesitation frames observed across all chunks.")]
    public int totalHesitationFrames;

    [Tooltip("Total traversal frames observed across all chunks.")]
    public int totalTraversalFrames;

    // safe fallback when no behaviour traversal data was recorded.
    public static BehaviourSummary Empty => new BehaviourSummary
    {
        hesitationScore = 0f,
        momentumFluidity = 1f,
        directionReversalRate = 0f,
        avgRetryDelay = -1f,
        deathClusteringRatio = 0f,
        chunksTraversed = 0,
        totalDirectionReversals = 0,
        totalHesitationFrames = 0,
        totalTraversalFrames = 0
    };

    public float EngagementScore()
    {
        // lightweight heuristic score, not direct emotion detection.
        float hesitationPenalty = Mathf.Clamp01(hesitationScore);
        float reversalPenalty = Mathf.Clamp01(directionReversalRate / 3f);
        float momentumReward = Mathf.Clamp01(momentumFluidity);

        float retryReward = 0.5f;
        if (avgRetryDelay >= 0f)
            retryReward = Mathf.Clamp01(1f - (avgRetryDelay / 3f));

        return (momentumReward * 0.35f) +
               (retryReward * 0.25f) +
               ((1f - hesitationPenalty) * 0.25f) +
               ((1f - reversalPenalty) * 0.15f);
    }

    public override string ToString()
    {
        return $"hesitation={hesitationScore:F2}, momentum={momentumFluidity:F2}, " +
               $"reversals/s={directionReversalRate:F2}, retryDelay={avgRetryDelay:F2}, " +
               $"deathClustering={deathClusteringRatio:F2}, engagement={EngagementScore():F2}";
    }
}
