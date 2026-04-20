using System.Collections.Generic;
using UnityEngine;

public static class SimpleChunkBlueprintGenerator
{
    public static ChunkBlueprint Generate(ChunkGenerationRequest request)
    {
        if (request == null)
        {
            Debug.LogWarning("SimpleChunkBlueprintGenerator: Request is null.");
            return null;
        }

        switch (request.requestedPrimaryTag)
        {
            case ChunkTag.Rest:
            case ChunkTag.Safe:
                return GenerateRestLike(request);

            case ChunkTag.Gap:
                return GenerateGapLike(request);

            case ChunkTag.Spikes:
                return GenerateSpikesLike(request);

            case ChunkTag.Vertical:
                return GenerateVerticalLike(request);

            case ChunkTag.Precision:
                return GeneratePrecisionLike(request);

            default:
                return GenerateRestLike(request);
        }
    }

    private static int GetPreferredWidth(ChunkGenerationRequest request, int minimum)
    {
        return Mathf.Max(minimum, request.preferredWidth);
    }

    private static int GetPreferredHeight(ChunkGenerationRequest request, int minimum)
    {
        return Mathf.Max(minimum, request.preferredHeight);
    }

    private static ChunkBlueprint GenerateRestLike(ChunkGenerationRequest request)
    {
        int width = GetPreferredWidth(request, 5);
        int variant = Random.Range(0, 2);

        List<string> rows = new List<string>();

        if (variant == 0)
        {
            rows.Add(new string('.', width));
            rows.Add("E" + new string('#', width - 2) + "X");
        }
        else
        {
            // Slightly broken-up but still safe flat chunk
            rows.Add(new string('.', width));
            rows.Add("E" + new string('#', width - 3) + ".X");
        }

        return new ChunkBlueprint
        {
            chunkName = "Generated_Rest",
            width = width,
            height = 2,
            rows = rows,
            entryCell = new Vector2Int(0, 1),
            exitCell = new Vector2Int(width - 1, 1),
            primaryTag = ChunkTag.Rest,
            difficultyRating = Mathf.Clamp(request.targetDifficulty, 1, 2),
            hasHazard = false,
            estimatedJumps = 0,
            tags = new ChunkTag[] { ChunkTag.Safe, ChunkTag.Rest }
        };
    }

    private static ChunkBlueprint GenerateGapLike(ChunkGenerationRequest request)
    {
        int width = GetPreferredWidth(request, 7);

        int gapSize;
        if (request.targetDifficulty <= 2) gapSize = 2;
        else if (request.targetDifficulty <= 4) gapSize = 3;
        else gapSize = 4;

        int leftSize = Random.Range(2, 4); // 2 or 3 support tiles
        int rightSize = width - 2 - gapSize - leftSize; // minus E and X

        if (rightSize < 1)
        {
            rightSize = 1;
            width = leftSize + gapSize + rightSize + 2;
        }

        string leftPart = "E" + new string('#', leftSize);
        string gapPart = new string('.', gapSize);
        string rightPart = new string('#', rightSize) + "X";
        string bottomRow = leftPart + gapPart + rightPart;

        bool hazardGap = request.requireHazard && request.targetDifficulty >= 3;

        if (!hazardGap)
        {
            return new ChunkBlueprint
            {
                chunkName = "Generated_Gap",
                width = width,
                height = 2,
                rows = new List<string>
                {
                    new string('.', width),
                    bottomRow
                },
                entryCell = new Vector2Int(0, 1),
                exitCell = new Vector2Int(width - 1, 1),
                primaryTag = ChunkTag.Gap,
                difficultyRating = Mathf.Clamp(request.targetDifficulty, 2, 5),
                hasHazard = false,
                estimatedJumps = 1,
                tags = new ChunkTag[] { ChunkTag.Gap }
            };
        }

        // Hazard gap variant:
        // Put spikes on the right landing platform instead of floating above the gap.
        string topRow =
            new string('.', leftPart.Length + gapPart.Length) +
            new string('S', rightSize) +
            ".";

        return new ChunkBlueprint
        {
            chunkName = "Generated_Gap_Hazard",
            width = width,
            height = 3,
            rows = new List<string>
            {
                new string('.', width),
                topRow,
                bottomRow
            },
            entryCell = new Vector2Int(0, 2),
            exitCell = new Vector2Int(width - 1, 2),
            primaryTag = ChunkTag.Gap,
            difficultyRating = Mathf.Clamp(request.targetDifficulty, 3, 6),
            hasHazard = true,
            estimatedJumps = 1,
            tags = new ChunkTag[] { ChunkTag.Gap, ChunkTag.Spikes }
        };
    }

    private static ChunkBlueprint GenerateSpikesLike(ChunkGenerationRequest request)
    {
        int width = GetPreferredWidth(request, 5);
        int supportWidth = Mathf.Max(3, width - 2);

        string topRow = "." + new string('S', supportWidth) + ".";
        string bottomRow = "E" + new string('#', supportWidth) + "X";

        return new ChunkBlueprint
        {
            chunkName = "Generated_Spikes",
            width = width,
            height = 2,
            rows = new List<string>
            {
                topRow,
                bottomRow
            },
            entryCell = new Vector2Int(0, 1),
            exitCell = new Vector2Int(width - 1, 1),
            primaryTag = ChunkTag.Spikes,
            difficultyRating = Mathf.Clamp(request.targetDifficulty, 3, 6),
            hasHazard = true,
            estimatedJumps = 1,
            tags = new ChunkTag[] { ChunkTag.Spikes }
        };
    }

    private static ChunkBlueprint GenerateVerticalLike(ChunkGenerationRequest request)
    {
        int difficulty = Mathf.Clamp(request.targetDifficulty, 1, 10);
        int variant = Random.Range(0, 3);

        if (variant == 0)
        {
            // Stairs up
            return new ChunkBlueprint
            {
                chunkName = "Generated_Vertical_StairsUp",
                width = 4,
                height = 3,
                rows = new List<string>
                {
                    "...#",
                    "..##",
                    "E##X"
                },
                entryCell = new Vector2Int(0, 2),
                exitCell = new Vector2Int(3, 2),
                primaryTag = ChunkTag.Vertical,
                difficultyRating = Mathf.Clamp(difficulty, 2, 4),
                hasHazard = false,
                estimatedJumps = 1,
                tags = new ChunkTag[] { ChunkTag.Safe, ChunkTag.Vertical }
            };
        }

        if (variant == 1)
        {
            // Offset rise
            return new ChunkBlueprint
            {
                chunkName = "Generated_Vertical_OffsetRise",
                width = 5,
                height = 3,
                rows = new List<string>
                {
                    "....#",
                    "..###",
                    "E##.X"
                },
                entryCell = new Vector2Int(0, 2),
                exitCell = new Vector2Int(4, 2),
                primaryTag = ChunkTag.Vertical,
                difficultyRating = Mathf.Clamp(difficulty, 2, 5),
                hasHazard = false,
                estimatedJumps = 1,
                tags = new ChunkTag[] { ChunkTag.Safe, ChunkTag.Vertical }
            };
        }

        // Step bridge
        return new ChunkBlueprint
        {
            chunkName = "Generated_Vertical_StepBridge",
            width = 5,
            height = 3,
            rows = new List<string>
            {
                "...##",
                ".###.",
                "E##.X"
            },
            entryCell = new Vector2Int(0, 2),
            exitCell = new Vector2Int(4, 2),
            primaryTag = ChunkTag.Vertical,
            difficultyRating = Mathf.Clamp(difficulty + 1, 3, 5),
            hasHazard = false,
            estimatedJumps = 2,
            tags = new ChunkTag[] { ChunkTag.Vertical, ChunkTag.Precision }
        };
    }

    private static ChunkBlueprint GeneratePrecisionLike(ChunkGenerationRequest request)
    {
        int difficulty = Mathf.Clamp(request.targetDifficulty, 1, 10);

        int gapSize;
        if (difficulty <= 3) gapSize = 1;
        else if (difficulty <= 6) gapSize = 2;
        else gapSize = 3;

        int startSupport = 2;
        int endSupport = 2;
        int landingWidth = 1;

        int minimumWidth = 1 + startSupport + gapSize + landingWidth + gapSize + endSupport + 1;
        int width = Mathf.Max(GetPreferredWidth(request, minimumWidth), minimumWidth);

        // Any extra width goes into the side supports, not the landing.
        int extraWidth = width - minimumWidth;
        startSupport += extraWidth / 2;
        endSupport += extraWidth - (extraWidth / 2);

        string bottomRow =
            "E" +
            new string('#', startSupport) +
            new string('.', gapSize) +
            "#" +
            new string('.', gapSize) +
            new string('#', endSupport) +
            "X";

        return new ChunkBlueprint
        {
            chunkName = "Generated_Precision_BoundaryLanding",
            width = width,
            height = 2,
            rows = new List<string>
            {
                new string('.', width),
                bottomRow
            },
            entryCell = new Vector2Int(0, 1),
            exitCell = new Vector2Int(width - 1, 1),
            primaryTag = ChunkTag.Precision,
            difficultyRating = Mathf.Clamp(difficulty + 1, 3, 7),
            hasHazard = false,
            estimatedJumps = 2,
            tags = new ChunkTag[] { ChunkTag.Gap, ChunkTag.Precision }
        };
    }
}