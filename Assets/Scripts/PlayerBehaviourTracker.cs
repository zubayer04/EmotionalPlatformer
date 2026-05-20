using System.Collections.Generic;
using UnityEngine;

public class PlayerBehaviourTracker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;

    [Header("Thresholds")]
    [Tooltip("Horizontal speed below which a grounded player is considered hesitating.")]
    [SerializeField] private float hesitationSpeedThreshold = 0.5f;

    [Tooltip("Horizontal speed above which post-respawn movement counts as meaningful input.")]
    [SerializeField] private float meaningfulInputSpeed = 1f;

    private Rigidbody2D playerRb;

    // per-chunk accumulators
    private int chunkHesitationFrames;
    private int chunkTraversalFrames;
    private float chunkMomentumSum;
    private int chunkDirectionReversals;
    private float lastNonZeroVelocitySign;

    // per-level accumulators
    private int totalHesitationFrames;
    private int totalTraversalFrames;
    private float totalMomentumSum;
    private int totalDirectionReversals;
    private int chunksTraversed;

    // death tracking
    private readonly Dictionary<string, int> deathsPerChunkName = new Dictionary<string, int>();
    private int totalDeathsTracked;

    // retry delay tracking
    private bool waitingForRetryInput;
    private float respawnTimestamp;
    private float totalRetryDelay;
    private int retryDelayCount;

    private int currentChunkInstanceId;
    private string currentChunkName;
    private bool isTracking;

    private void Awake()
    {
        TryResolveReferences(false);
    }

    public void SetPlayer(PlayerController controller)
    {
        playerController = controller;
        playerRb = playerController != null ? playerController.GetComponent<Rigidbody2D>() : null;
    }

    public void StartTracking()
    {
        // resets run-level behaviour evidence at the start of a level.
        if (!TryResolveReferences(true))
        {
            return;
        }

        ResetAll();
        isTracking = true;
    }

    public void StopTracking()
    {
        isTracking = false;
    }

    public void OnChunkEntered()
    {
        if (!isTracking) return;

        FlushChunkAccumulators();
        ResetChunkAccumulators();
    }

    public void ObserveCurrentChunk(ChunkData chunk)
    {
        // updates chunk context so behaviour and deaths can be attributed.
        if (!isTracking || chunk == null) return;

        int nextChunkId = chunk.GetInstanceID();
        if (currentChunkInstanceId == nextChunkId)
            return;

        if (currentChunkInstanceId != 0)
            FlushChunkAccumulators();

        ResetChunkAccumulators();
        currentChunkInstanceId = nextChunkId;
        currentChunkName = LevelRunLog.CleanName(chunk.name);
    }

    public void OnPlayerDied(string chunkName)
    {
        // records death clustering and starts retry-delay timing.
        if (!isTracking) return;

        totalDeathsTracked++;

        if (string.IsNullOrEmpty(chunkName))
            chunkName = currentChunkName;

        if (!string.IsNullOrEmpty(chunkName))
        {
            if (!deathsPerChunkName.ContainsKey(chunkName))
                deathsPerChunkName[chunkName] = 0;
            deathsPerChunkName[chunkName]++;
        }

        waitingForRetryInput = true;
        respawnTimestamp = Time.time;
    }

    public BehaviourSummary GetSummary()
    {
        // converts accumulated frame data into normalized behaviour metrics.
        FlushChunkAccumulators();

        BehaviourSummary summary = new BehaviourSummary();

        summary.totalHesitationFrames = totalHesitationFrames;
        summary.totalTraversalFrames = totalTraversalFrames;
        summary.totalDirectionReversals = totalDirectionReversals;
        summary.chunksTraversed = chunksTraversed;

        // hesitation: fraction of grounded traversal time spent near-stationary
        summary.hesitationScore = totalTraversalFrames > 0
            ? (float)totalHesitationFrames / totalTraversalFrames
            : 0f;

        // momentum fluidity: average normalized horizontal speed
        float maxSpeed = playerController != null ? playerController.MaxSpeed : 8f;
        summary.momentumFluidity = totalTraversalFrames > 0 && maxSpeed > 0f
            ? Mathf.Clamp01(totalMomentumSum / (totalTraversalFrames * maxSpeed))
            : 1f;

        // direction reversal rate: reversals per second of traversal time
        float traversalSeconds = totalTraversalFrames * Time.fixedDeltaTime;
        summary.directionReversalRate = traversalSeconds > 0.1f
            ? totalDirectionReversals / traversalSeconds
            : 0f;

        // retry delay: average time from respawn to meaningful input
        summary.avgRetryDelay = retryDelayCount > 0
            ? totalRetryDelay / retryDelayCount
            : -1f;

        // death clustering: fraction of deaths on the deadliest chunk
        if (totalDeathsTracked > 0 && deathsPerChunkName.Count > 0)
        {
            int maxDeaths = 0;
            foreach (var kvp in deathsPerChunkName)
            {
                if (kvp.Value > maxDeaths)
                    maxDeaths = kvp.Value;
            }
            summary.deathClusteringRatio = (float)maxDeaths / totalDeathsTracked;
        }
        else
        {
            summary.deathClusteringRatio = 0f;
        }

        return summary;
    }

    private void FixedUpdate()
    {
        // samples movement every physics tick while tracking is active.
        if (!isTracking || playerRb == null || playerController == null) return;

        float vx = playerRb.linearVelocity.x;
        float absVx = Mathf.Abs(vx);
        bool grounded = playerController.IsGrounded;

        chunkTraversalFrames++;

        // hesitation: grounded and near-stationary
        if (grounded && absVx < hesitationSpeedThreshold)
            chunkHesitationFrames++;

        // momentum accumulator
        chunkMomentumSum += absVx;

        // direction reversals
        if (absVx > 0.1f)
        {
            float currentSign = Mathf.Sign(vx);
            if (lastNonZeroVelocitySign != 0f && currentSign != lastNonZeroVelocitySign)
                chunkDirectionReversals++;
            lastNonZeroVelocitySign = currentSign;
        }

        // retry delay detection
        if (waitingForRetryInput && absVx >= meaningfulInputSpeed)
        {
            float delay = Time.time - respawnTimestamp;
            totalRetryDelay += delay;
            retryDelayCount++;
            waitingForRetryInput = false;
        }
    }

    private void FlushChunkAccumulators()
    {
        if (chunkTraversalFrames <= 0) return;

        totalHesitationFrames += chunkHesitationFrames;
        totalTraversalFrames += chunkTraversalFrames;
        totalMomentumSum += chunkMomentumSum;
        totalDirectionReversals += chunkDirectionReversals;
        chunksTraversed++;
    }

    private void ResetChunkAccumulators()
    {
        chunkHesitationFrames = 0;
        chunkTraversalFrames = 0;
        chunkMomentumSum = 0f;
        chunkDirectionReversals = 0;
        lastNonZeroVelocitySign = 0f;
    }

    private void ResetAll()
    {
        ResetChunkAccumulators();

        currentChunkInstanceId = 0;
        currentChunkName = string.Empty;

        totalHesitationFrames = 0;
        totalTraversalFrames = 0;
        totalMomentumSum = 0f;
        totalDirectionReversals = 0;
        chunksTraversed = 0;

        deathsPerChunkName.Clear();
        totalDeathsTracked = 0;

        waitingForRetryInput = false;
        respawnTimestamp = 0f;
        totalRetryDelay = 0f;
        retryDelayCount = 0;
    }

    private bool TryResolveReferences(bool warnIfMissing)
    {
        if (playerController == null)
            playerController = GetComponent<PlayerController>();

        if (playerController == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                playerController = playerObject.GetComponent<PlayerController>();
        }

        if (playerController == null)
        {
            if (warnIfMissing)
                Debug.LogWarning("PlayerBehaviourTracker: No PlayerController assigned or found.");
            return false;
        }

        playerRb = playerController.GetComponent<Rigidbody2D>();
        if (playerRb == null)
        {
            if (warnIfMissing)
                Debug.LogWarning("PlayerBehaviourTracker: No Rigidbody2D on player.");
            return false;
        }

        return true;
    }
}
