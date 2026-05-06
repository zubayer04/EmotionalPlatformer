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

        ChunkBlueprint blueprint;

        switch (request.requestedPrimaryTag)
        {
            case ChunkTag.Rest:
            case ChunkTag.Safe:
                blueprint = GenerateRestLike(request);
                break;

            case ChunkTag.Gap:
                blueprint = GenerateGapLike(request);
                break;

            case ChunkTag.Spikes:
                blueprint = GenerateSpikesLike(request);
                break;

            case ChunkTag.Vertical:
                blueprint = GenerateVerticalLike(request);
                break;

            case ChunkTag.Precision:
                blueprint = GeneratePrecisionLike(request);
                break;

            default:
                blueprint = GenerateRestLike(request);
                break;
        }

        return AddGroundDecorations(blueprint);
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
            // entry/exit markers sit just inside support tiles, so a flat gap blueprint
            // needs roughly source exit delta + 2 cells to preserve traversal distance
            int sourceEquivalentWidth = Mathf.CeilToInt(Mathf.Abs(request.sourceExitDelta.x)) + 2;
            width = Mathf.Max(width, sourceEquivalentWidth);
        }

        int gapSize = request.hasSourceContext && request.sourceMaxGapWidth > 0
            ? request.sourceMaxGapWidth
            : ChooseGapSize(request.targetDifficulty);
        int supportBudget = width - 2 - gapSize; // minus e and x
        const int preferredMinimumLanding = 2;

        if (supportBudget < preferredMinimumLanding * 2)
        {
            supportBudget = preferredMinimumLanding * 2;
            width = supportBudget + gapSize + 2;
        }

        int leftSize = ChooseLeftSupportSize(supportBudget, preferredMinimumLanding);
        int rightSize = supportBudget - leftSize;

        int maxLandingYOffset = GetMaxLandingYOffset(request);
        int landingYOffset = Random.Range(-maxLandingYOffset, maxLandingYOffset + 1);

        bool hazardGap = ShouldGenerateHazardAccentGap(request);

        if (hazardGap)
        {
            bool placeOnExitSide = request.forceGapHazardAccent
                ? request.gapHazardAccentOnExitSide
                : Random.Range(0, 2) == 0;
            return GenerateHazardAccentGap(request, gapSize, leftSize, rightSize, placeOnExitSide);
        }

        string leftPart = "E" + new string('#', leftSize);
        string gapPart = new string('.', gapSize);
        string rightPart = new string('#', rightSize) + "X";
        string bottomRow = leftPart + gapPart + rightPart;
        string variantName = GetGapVariantName(leftSize, rightSize);
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

    private static bool ShouldGenerateHazardAccentGap(ChunkGenerationRequest request)
    {
        if (request == null ||
            !request.hasSourceContext ||
            request.requestedPrimaryTag != ChunkTag.Gap ||
            request.sourceHasHazard ||
            request.sourceMaxGapWidth <= 0)
        {
            return false;
        }

        if (request.forceGapHazardAccent)
            return true;

        // keep hazard-accented gaps as occasional variety rather than redefining the gap family
        return request.targetDifficulty >= 2 && Random.Range(0, 4) == 0;
    }

    private static ChunkBlueprint GenerateHazardAccentGap(
        ChunkGenerationRequest request,
        int gapSize,
        int baseLeftSize,
        int baseRightSize,
        bool placeOnExitSide)
    {
        const int safeTilesBesideOuterSpike = 3;
        int leftSize = Mathf.Max(2, baseLeftSize);
        int rightSize = Mathf.Max(2, baseRightSize);

        if (placeOnExitSide)
            rightSize = Mathf.Max(rightSize, safeTilesBesideOuterSpike + 1);
        else
            leftSize = Mathf.Max(leftSize, safeTilesBesideOuterSpike + 1);

        int width = 2 + leftSize + gapSize + rightSize;
        char[] hazardRow = new string('.', width).ToCharArray();
        string bottomRow =
            "E" +
            new string('#', leftSize) +
            new string('.', gapSize) +
            new string('#', rightSize) +
            "X";

        int spikeX;
        string sideName;
        if (placeOnExitSide)
        {
            int rightStart = 1 + leftSize + gapSize;
            spikeX = rightStart + rightSize - 1;
            sideName = "ExitOuterSpike";
        }
        else
        {
            spikeX = 1;
            sideName = "EntryOuterSpike";
        }

        hazardRow[spikeX] = 'S';

        return new ChunkBlueprint
        {
            chunkName = $"Generated_GapHazard_{sideName}",
            width = width,
            height = 2,
            rows = new List<string>
            {
                new string(hazardRow),
                bottomRow
            },
            entryCell = new Vector2Int(0, 1),
            exitCell = new Vector2Int(width - 1, 1),
            primaryTag = ChunkTag.Gap,
            difficultyRating = Mathf.Clamp(request.sourceDifficulty, 2, 5),
            hasHazard = true,
            estimatedJumps = Mathf.Max(1, request.sourceEstimatedJumps + 1),
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
            // stairs up
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
            // offset rise
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

        // step bridge
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

        // any extra width goes into the side supports, not the landing
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

    private static ChunkBlueprint AddGroundDecorations(ChunkBlueprint blueprint)
    {
        if (blueprint == null || blueprint.rows == null || blueprint.rows.Count < 2)
            return blueprint;

        List<Vector2Int> candidates = FindDecorationCandidates(blueprint);
        if (candidates.Count == 0)
            return blueprint;

        int maxDecorationCount = Mathf.Min(2, candidates.Count);
        int targetDecorationCount = maxDecorationCount == 1
            ? 1
            : (Random.value < 0.65f ? 1 : 2);

        List<Vector2Int> selected = new List<Vector2Int>();
        while (selected.Count < targetDecorationCount && candidates.Count > 0)
        {
            int index = Random.Range(0, candidates.Count);
            Vector2Int candidate = candidates[index];
            candidates.RemoveAt(index);

            if (!IsFarEnoughFromSelected(candidate, selected))
                continue;

            selected.Add(candidate);
        }

        if (selected.Count == 0)
            return blueprint;

        List<char[]> mutableRows = new List<char[]>(blueprint.rows.Count);
        for (int i = 0; i < blueprint.rows.Count; i++)
            mutableRows.Add(blueprint.rows[i].ToCharArray());

        for (int i = 0; i < selected.Count; i++)
        {
            Vector2Int cell = selected[i];
            mutableRows[cell.y][cell.x] = 'D';
        }

        List<string> decoratedRows = new List<string>(mutableRows.Count);
        for (int i = 0; i < mutableRows.Count; i++)
            decoratedRows.Add(new string(mutableRows[i]));

        blueprint.rows = decoratedRows;
        return blueprint;
    }

    private static List<Vector2Int> FindDecorationCandidates(ChunkBlueprint blueprint)
    {
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int y = 0; y < blueprint.rows.Count - 1; y++)
        {
            string row = blueprint.rows[y];
            string belowRow = blueprint.rows[y + 1];
            if (string.IsNullOrEmpty(row) || string.IsNullOrEmpty(belowRow))
                continue;

            int width = Mathf.Min(row.Length, belowRow.Length);
            for (int x = 0; x < width; x++)
            {
                if (row[x] == '.' && belowRow[x] == '#')
                    candidates.Add(new Vector2Int(x, y));
            }
        }

        return candidates;
    }

    private static bool IsFarEnoughFromSelected(Vector2Int candidate, List<Vector2Int> selected)
    {
        const int minimumHorizontalSpacing = 3;

        for (int i = 0; i < selected.Count; i++)
        {
            if (Mathf.Abs(candidate.x - selected[i].x) < minimumHorizontalSpacing)
                return false;
        }

        return true;
    }
}
