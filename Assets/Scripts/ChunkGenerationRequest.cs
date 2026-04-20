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
}