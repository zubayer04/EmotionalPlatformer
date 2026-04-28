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
        if (request.hasSourceContext && request.requestedPrimaryTag == ChunkTag.Safe && request.sourceExitDelta.y < -0.1f)
            return GenerateSafeDropRestLike(request);

        return GenerateFlatRestLike(request);
    }

    private static ChunkBlueprint GenerateFlatRestLike(ChunkGenerationRequest request)
    {
        int width = GetPreferredWidth(request, 5);
        if (request.hasSourceContext && Mathf.Abs(request.sourceExitDelta.x) > 0.1f)
        {
            int sourceEquivalentWidth = Mathf.CeilToInt(Mathf.Abs(request.sourceExitDelta.x)) + 2;
            width = Mathf.Max(width, sourceEquivalentWidth);
        }

        List<string> rows = new List<string>
        {
            new string('.', width),
            "E" + new string('#', width - 2) + "X"
        };

        return new ChunkBlueprint
        {
            chunkName = "Generated_Rest",
            width = width,
            height = 2,
            rows = rows,
            entryCell = new Vector2Int(0, 1),
            exitCell = new Vector2Int(width - 1, 1),
            primaryTag = request.hasSourceContext ? request.requestedPrimaryTag : ChunkTag.Rest,
            difficultyRating = request.hasSourceContext ? request.sourceDifficulty : Mathf.Clamp(request.targetDifficulty, 1, 2),
            hasHazard = request.hasSourceContext ? request.sourceHasHazard : false,
            estimatedJumps = request.hasSourceContext ? Mathf.Max(0, request.sourceEstimatedJumps) : 0,
            tags = new ChunkTag[] { ChunkTag.Safe, ChunkTag.Rest }
        };
    }

    private static ChunkBlueprint GenerateSafeDropRestLike(ChunkGenerationRequest request)
    {
        int variant = Random.Range(0, 5);
        string chunkName;
        List<string> rows;
        Vector2Int entryCell;
        Vector2Int exitCell;
        int difficultyRating = request.sourceDifficulty;

        switch (variant)
        {
            case 0:
                chunkName = "Generated_Safe_DropRest_Box2";
                rows = new List<string>
                {
                    "E##B...",
                    "...B...",
                    "...###X"
                };
                entryCell = new Vector2Int(0, 0);
                exitCell = new Vector2Int(6, 2);
                break;

            case 1:
                chunkName = "Generated_Safe_DropRest_Box3";
                rows = new List<string>
                {
                    "E##B...",
                    "...B...",
                    "...B...",
                    "...###X"
                };
                entryCell = new Vector2Int(0, 0);
                exitCell = new Vector2Int(6, 3);
                break;

            case 2:
                chunkName = "Generated_Safe_RiseRest_Box1";
                rows = new List<string>
                {
                    "...B##X",
                    "E###..."
                };
                entryCell = new Vector2Int(0, 1);
                exitCell = new Vector2Int(6, 0);
                break;

            case 3:
                chunkName = "Generated_Safe_RiseRest_Box2";
                rows = new List<string>
                {
                    "...B##X",
                    "...B...",
                    "E###..."
                };
                entryCell = new Vector2Int(0, 2);
                exitCell = new Vector2Int(6, 0);
                difficultyRating = 2;
                break;

            default:
                chunkName = "Generated_Safe_RiseRest_Box3";
                rows = new List<string>
                {
                    "...B##X",
                    "...B...",
                    "...B...",
                    "E###..."
                };
                entryCell = new Vector2Int(0, 3);
                exitCell = new Vector2Int(6, 0);
                difficultyRating = 2;
                break;
        }

        return new ChunkBlueprint
        {
            chunkName = chunkName,
            width = 7,
            height = rows.Count,
            rows = rows,
            entryCell = entryCell,
            exitCell = exitCell,
            primaryTag = ChunkTag.Safe,
            difficultyRating = difficultyRating,
            hasHazard = request.sourceHasHazard,
            estimatedJumps = Mathf.Max(0, request.sourceEstimatedJumps),
            tags = new ChunkTag[] { ChunkTag.Safe, ChunkTag.Rest }
        };
    }

    private static ChunkBlueprint GenerateGapLike(ChunkGenerationRequest request)
    {
        int width = GetPreferredWidth(request, 7);
        if (request.hasSourceContext && Mathf.Abs(request.sourceExitDelta.x) > 0.1f)
        {
            // Entry/exit markers sit just inside the support tiles, so a flat gap blueprint
            // needs roughly source exitDelta + 2 cells to preserve traversal distance.
            int sourceEquivalentWidth = Mathf.CeilToInt(Mathf.Abs(request.sourceExitDelta.x)) + 2;
            width = Mathf.Max(width, sourceEquivalentWidth);
        }

        int gapSize = request.hasSourceContext && request.sourceMaxGapWidth > 0
            ? request.sourceMaxGapWidth
            : ChooseGapSize(request.targetDifficulty);
        int supportBudget = width - 2 - gapSize; // minus E and X
        const int preferredMinimumLanding = 2;

        if (supportBudget < preferredMinimumLanding * 2)
        {
            supportBudget = preferredMinimumLanding * 2;
            width = supportBudget + gapSize + 2;
        }

        int leftSize = ChooseLeftSupportSize(supportBudget, preferredMinimumLanding);
        int rightSize = supportBudget - leftSize;

        string leftPart = "E" + new string('#', leftSize);
        string gapPart = new string('.', gapSize);
        string rightPart = new string('#', rightSize) + "X";
        string bottomRow = leftPart + gapPart + rightPart;
        string variantName = GetGapVariantName(leftSize, rightSize);
        int maxLandingYOffset = GetMaxLandingYOffset(request);
        int landingYOffset = Random.Range(-maxLandingYOffset, maxLandingYOffset + 1);

        bool hazardGap = request.requireHazard && request.targetDifficulty >= 3;

        if (!hazardGap)
        {
            int entryRow;
            int exitRow;
            List<string> rows = BuildGapRows(width, leftSize, gapSize, rightSize, bottomRow, landingYOffset, out entryRow, out exitRow);

            return new ChunkBlueprint
            {
                chunkName = $"{variantName}_{GetLandingOffsetName(landingYOffset)}",
                width = width,
                height = rows.Count,
                rows = rows,
                entryCell = new Vector2Int(0, entryRow),
                exitCell = new Vector2Int(width - 1, exitRow),
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
            chunkName = variantName + "_Hazard",
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

    private static List<string> BuildGapRows(
        int width,
        int leftSize,
        int gapSize,
        int rightSize,
        string flatTraversalRow,
        int landingYOffset,
        out int entryRow,
        out int exitRow)
    {
        if (landingYOffset == 0)
        {
            entryRow = 1;
            exitRow = 1;
            return new List<string>
            {
                new string('.', width),
                flatTraversalRow
            };
        }

        char[] entryChars = new string('.', width).ToCharArray();
        entryChars[0] = 'E';
        for (int i = 0; i < leftSize; i++)
            entryChars[1 + i] = '#';

        char[] exitChars = new string('.', width).ToCharArray();
        int rightStart = 1 + leftSize + gapSize;
        for (int i = 0; i < rightSize; i++)
            exitChars[rightStart + i] = '#';
        exitChars[width - 1] = 'X';

        if (landingYOffset > 0)
        {
            entryRow = landingYOffset;
            exitRow = 0;
            List<string> rows = new List<string> { new string(exitChars) };
            for (int i = 1; i < landingYOffset; i++)
                rows.Add(new string('.', width));
            rows.Add(new string(entryChars));
            rows.Add(new string('.', width));
            return rows;
        }

        int dropAmount = Mathf.Abs(landingYOffset);
        entryRow = 1;
        exitRow = 1 + dropAmount;
        List<string> dropRows = new List<string>
        {
            new string('.', width),
            new string(entryChars)
        };
        for (int i = 1; i < dropAmount; i++)
            dropRows.Add(new string('.', width));
        dropRows.Add(new string(exitChars));
        return dropRows;
    }

    private static string GetLandingOffsetName(int landingYOffset)
    {
        int magnitude = Mathf.Abs(landingYOffset);

        if (landingYOffset > 0)
            return magnitude > 1 ? $"Rise{magnitude}" : "Rise";

        if (landingYOffset < 0)
            return magnitude > 1 ? $"Drop{magnitude}" : "Drop";

        return "Flat";
    }

    private static int GetMaxLandingYOffset(ChunkGenerationRequest request)
    {
        if (request != null &&
            request.hasSourceContext &&
            request.requestedPrimaryTag == ChunkTag.Gap &&
            request.sourceMaxGapWidth > 0 &&
            request.sourceMaxGapWidth <= 4)
        {
            return 2;
        }

        return 1;
    }

    private static int ChooseGapSize(int targetDifficulty)
    {
        if (targetDifficulty <= 2)
            return 2;

        if (targetDifficulty <= 4)
            return Random.Range(0, 2) == 0 ? 2 : 3;

        return Random.Range(0, 2) == 0 ? 3 : 4;
    }

    private static int ChooseLeftSupportSize(int supportBudget, int minimumLanding)
    {
        int minLeft = Mathf.Clamp(minimumLanding, 1, supportBudget - 1);
        int maxLeft = Mathf.Max(minLeft, supportBudget - minimumLanding);

        if (maxLeft <= minLeft)
            return minLeft;

        int variant = Random.Range(0, 3);
        if (variant == 0)
            return minLeft;

        if (variant == 1)
            return maxLeft;

        return Random.Range(minLeft, maxLeft + 1);
    }

    private static string GetGapVariantName(int leftSize, int rightSize)
    {
        if (leftSize < rightSize)
            return "Generated_Gap_Early";

        if (leftSize > rightSize)
            return "Generated_Gap_Late";

        return "Generated_Gap_Centered";
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
        if (request.hasSourceContext && request.sourceChunkName.Contains("Chunk_ElevatedPlatform_Tilemap"))
            return GenerateElevatedPlatformPrecisionLike(request);

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

    private static ChunkBlueprint GenerateElevatedPlatformPrecisionLike(ChunkGenerationRequest request)
    {
        int variant = Random.Range(0, 5);
        string chunkName;
        List<string> rows;

        switch (variant)
        {
            case 0:
                chunkName = "Generated_Precision_ElevatedPlatform_MidDrop2";
                rows = new List<string>
                {
                    ".............",
                    ".............",
                    "E##.......##X",
                    ".............",
                    "......P......"
                };
                break;

            case 1:
                chunkName = "Generated_Precision_ElevatedPlatform_ExitRise1";
                rows = new List<string>
                {
                    ".............",
                    "..........##X",
                    "E##...P......",
                    ".............",
                    "............."
                };
                break;

            case 2:
                chunkName = "Generated_Precision_ElevatedPlatform_ExitRise2";
                rows = new List<string>
                {
                    "..........##X",
                    ".............",
                    "E##...P......",
                    ".............",
                    "............."
                };
                break;

            case 3:
                chunkName = "Generated_Precision_ElevatedPlatform_ExitDrop1";
                rows = new List<string>
                {
                    ".............",
                    ".............",
                    "E##...P......",
                    "..........##X",
                    "............."
                };
                break;

            default:
                chunkName = "Generated_Precision_ElevatedPlatform_ExitDrop2";
                rows = new List<string>
                {
                    ".............",
                    ".............",
                    "E##...P......",
                    ".............",
                    "..........##X"
                };
                break;
        }

        return new ChunkBlueprint
        {
            chunkName = chunkName,
            width = 13,
            height = 5,
            rows = rows,
            entryCell = new Vector2Int(0, 2),
            exitCell = FindExitCell(rows),
            primaryTag = ChunkTag.Precision,
            difficultyRating = request.sourceDifficulty,
            hasHazard = request.sourceHasHazard,
            estimatedJumps = Mathf.Max(0, request.sourceEstimatedJumps),
            tags = new ChunkTag[] { ChunkTag.Gap, ChunkTag.Precision }
        };
    }

    private static Vector2Int FindExitCell(List<string> rows)
    {
        if (rows == null)
            return Vector2Int.zero;

        for (int y = 0; y < rows.Count; y++)
        {
            int x = rows[y].IndexOf('X');
            if (x >= 0)
                return new Vector2Int(x, y);
        }

        return Vector2Int.zero;
    }
}
