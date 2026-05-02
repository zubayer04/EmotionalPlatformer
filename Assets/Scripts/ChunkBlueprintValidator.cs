using UnityEngine;

public static class ChunkBlueprintValidator
{
    public static ChunkBlueprintValidationResult Validate(ChunkBlueprint blueprint)
    {
        ChunkBlueprintValidationResult result = new ChunkBlueprintValidationResult();

        if (blueprint == null)
        {
            result.AddError("Blueprint is null.");
            return result;
        }

        if (blueprint.width <= 0)
            result.AddError("Width must be greater than 0.");

        if (blueprint.height <= 0)
            result.AddError("Height must be greater than 0.");

        if (blueprint.rows == null)
        {
            result.AddError("Rows list is null.");
            return result;
        }

        if (blueprint.rows.Count != blueprint.height)
            result.AddError($"Row count ({blueprint.rows.Count}) does not match height ({blueprint.height}).");

        int entryCount = 0;
        int exitCount = 0;
        int hazardCount = 0;
        int solidCount = 0;

        Vector2Int foundEntry = new Vector2Int(-1, -1);
        Vector2Int foundExit = new Vector2Int(-1, -1);

        for (int y = 0; y < blueprint.rows.Count; y++)
        {
            string row = blueprint.rows[y];

            if (string.IsNullOrEmpty(row))
            {
                result.AddError($"Row {y} is null or empty.");
                continue;
            }

            if (row.Length != blueprint.width)
                result.AddError($"Row {y} length ({row.Length}) does not match width ({blueprint.width}).");

            for (int x = 0; x < row.Length; x++)
            {
                char cell = row[x];

                switch (cell)
                {
                    case '.':
                        break;

                    case 'D':
                        if (!HasGroundSupportDirectlyBelow(blueprint, x, y))
                            result.AddError($"Decoration 'D' at ({x}, {y}) must sit directly above ground '#'.");
                        break;

                    case '#':
                    case 'B':
                    case 'P':
                        solidCount++;
                        break;

                    case 'S':
                    case 'M':
                        hazardCount++;
                        break;

                    case 'E':
                        entryCount++;
                        foundEntry = new Vector2Int(x, y);
                        break;

                    case 'X':
                        exitCount++;
                        foundExit = new Vector2Int(x, y);
                        break;

                    default:
                        result.AddError($"Invalid character '{cell}' at ({x}, {y}).");
                        break;
                }
            }
        }

        if (entryCount != 1)
            result.AddError($"Blueprint must contain exactly 1 'E', but found {entryCount}.");

        if (exitCount != 1)
            result.AddError($"Blueprint must contain exactly 1 'X', but found {exitCount}.");

        bool entryInsideBounds =
            blueprint.entryCell.x >= 0 && blueprint.entryCell.x < blueprint.width &&
            blueprint.entryCell.y >= 0 && blueprint.entryCell.y < blueprint.height;

        bool exitInsideBounds =
            blueprint.exitCell.x >= 0 && blueprint.exitCell.x < blueprint.width &&
            blueprint.exitCell.y >= 0 && blueprint.exitCell.y < blueprint.height;

        if (!entryInsideBounds)
            result.AddError($"entryCell {blueprint.entryCell} is outside blueprint bounds.");

        if (!exitInsideBounds)
            result.AddError($"exitCell {blueprint.exitCell} is outside blueprint bounds.");

        if (entryCount == 1 && blueprint.entryCell != foundEntry)
            result.AddError($"entryCell {blueprint.entryCell} does not match actual E position {foundEntry}.");

        if (exitCount == 1 && blueprint.exitCell != foundExit)
            result.AddError($"exitCell {blueprint.exitCell} does not match actual X position {foundExit}.");

        if (solidCount == 0 && hazardCount == 0)
            result.AddError("Blueprint contains no solid or hazard tiles.");

        if (blueprint.hasHazard && hazardCount == 0)
            result.AddError("Blueprint hasHazard is true, but no hazard tiles (S or M) were found.");

        if (!blueprint.hasHazard && hazardCount > 0)
            result.AddError("Blueprint hasHazard is false, but hazard tiles (S or M) were found.");

        return result;
    }

    private static bool HasGroundSupportDirectlyBelow(ChunkBlueprint blueprint, int x, int y)
    {
        if (blueprint == null || blueprint.rows == null)
            return false;

        int belowY = y + 1;
        if (belowY < 0 || belowY >= blueprint.rows.Count)
            return false;

        string belowRow = blueprint.rows[belowY];
        if (string.IsNullOrEmpty(belowRow) || x < 0 || x >= belowRow.Length)
            return false;

        return belowRow[x] == '#';
    }
}
