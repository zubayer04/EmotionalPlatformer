using System;
using UnityEngine;

[Serializable]
public class ChunkGenerationRequest
{
    public ChunkTag requestedPrimaryTag = ChunkTag.Rest;
    [Range(0, 10)] public int targetDifficulty = 1;
    public bool requireHazard = false;
    public int preferredWidth = 8;
    public int preferredHeight = 2;

    public bool hasSourceContext = false;
    public string sourceChunkName = string.Empty;
    public int sourceDifficulty = -1;
    public bool sourceHasHazard = false;
    public int sourceEstimatedJumps = -1;
    public Vector2 sourceExitDelta = Vector2.zero;
    public int sourceMaxGapWidth = -1;

    public bool forceGapHazardAccent = false;
    public bool gapHazardAccentOnExitSide = false;
}
