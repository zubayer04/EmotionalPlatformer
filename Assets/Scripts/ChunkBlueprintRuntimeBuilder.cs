using UnityEngine;

public class ChunkBlueprintRuntimeBuilder : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private Vector2 cellSize = Vector2.one;
    [SerializeField] private bool buildOnStart = false;

    [Header("Marker Alignment")]
    [Tooltip("Extra vertical offset added to Entry/Exit markers after snapping to platform surface.")]
    [SerializeField] private float markerVerticalOffset = 0f;

    [Header("Sprite References")]
    [SerializeField] private Sprite solidSprite;
    [SerializeField] private Sprite spikeSprite;

    [Header("Debug Input")]
    [SerializeField] private ChunkBlueprint blueprintToBuild;

    [Header("Layer Settings")]
    [SerializeField] private string solidLayerName = "Ground";

    public GameObject BuildChunk(ChunkBlueprint blueprint, Vector3 origin)
    {
        if (blueprint == null)
        {
            Debug.LogWarning("ChunkBlueprintRuntimeBuilder: Cannot build null blueprint.");
            return null;
        }

        ChunkBlueprintValidationResult validation = ChunkBlueprintValidator.Validate(blueprint);
        if (!validation.isValid)
        {
            Debug.LogWarning($"ChunkBlueprintRuntimeBuilder: Blueprint '{blueprint.chunkName}' is invalid.");
            for (int i = 0; i < validation.errors.Count; i++)
            {
                Debug.LogWarning($"  Error {i + 1}: {validation.errors[i]}");
            }
            return null;
        }

        GameObject root = new GameObject(blueprint.chunkName + "_Runtime");
        root.transform.position = origin;

        ChunkData chunkData = root.AddComponent<ChunkData>();
        chunkData.primaryTag = blueprint.primaryTag;
        chunkData.difficultyRating = blueprint.difficultyRating;
        chunkData.hasHazard = blueprint.hasHazard;
        chunkData.estimatedJumps = blueprint.estimatedJumps;
        chunkData.tags = blueprint.tags;

        for (int y = 0; y < blueprint.height; y++)
        {
            for (int x = 0; x < blueprint.width; x++)
            {
                char cell = blueprint.rows[y][x];
                Vector3 localPos = CellToLocalPosition(x, y, blueprint.height);
                Vector2 size = new Vector2(cellSize.x, cellSize.y);

                if (cell == '#')
                {
                    CreateSolidTile(root.transform, localPos, size);
                }
                else if (cell == 'S')
                {
                    CreateSpikeTile(root.transform, localPos, size);
                }
                else if (cell == 'M')
                {
                    CreateMovingHazardMarker(root.transform, localPos, size);
                }
            }
        }

        Transform entry = CreateEntryMarker(root.transform, blueprint);
        Transform exit = CreateExitMarker(root.transform, blueprint);

        chunkData.entry = entry;
        chunkData.exit = exit;

        return root;
    }

    private Vector3 CellToLocalPosition(int x, int y, int totalHeight)
    {
        float px = x * cellSize.x;
        float py = (totalHeight - 1 - y) * cellSize.y;
        return new Vector3(px, py, 0f);
    }

    private bool IsInside(ChunkBlueprint blueprint, int x, int y)
    {
        return blueprint != null &&
               x >= 0 && x < blueprint.width &&
               y >= 0 && y < blueprint.height;
    }

    private char GetCellSafe(ChunkBlueprint blueprint, int x, int y)
    {
        if (!IsInside(blueprint, x, y)) return '?';
        return blueprint.rows[y][x];
    }

    private bool IsSupportCell(char cell)
    {
        return cell == '#';
    }

    private Vector3 GetTopLeftOfCell(int x, int y, int totalHeight)
    {
        Vector3 center = CellToLocalPosition(x, y, totalHeight);
        return new Vector3(
            center.x - (cellSize.x * 0.5f),
            center.y + (cellSize.y * 0.5f) + markerVerticalOffset,
            0f
        );
    }

    private Vector3 GetTopRightOfCell(int x, int y, int totalHeight)
    {
        Vector3 center = CellToLocalPosition(x, y, totalHeight);
        return new Vector3(
            center.x + (cellSize.x * 0.5f),
            center.y + (cellSize.y * 0.5f) + markerVerticalOffset,
            0f
        );
    }

    private Transform CreateEntryMarker(Transform parent, ChunkBlueprint blueprint)
    {
        Vector2Int e = blueprint.entryCell;
        Vector3 localPos;

        // Prefer first support immediately to the right of E
        if (IsSupportCell(GetCellSafe(blueprint, e.x + 1, e.y)))
        {
            localPos = GetTopLeftOfCell(e.x + 1, e.y, blueprint.height);
        }
        // Fallback: support directly below E
        else if (IsSupportCell(GetCellSafe(blueprint, e.x, e.y + 1)))
        {
            localPos = GetTopLeftOfCell(e.x, e.y + 1, blueprint.height);
        }
        // Fallback: support to the left
        else if (IsSupportCell(GetCellSafe(blueprint, e.x - 1, e.y)))
        {
            localPos = GetTopLeftOfCell(e.x - 1, e.y, blueprint.height);
        }
        else
        {
            localPos = CellToLocalPosition(e.x, e.y, blueprint.height);
        }

        GameObject marker = new GameObject("Entry");
        marker.transform.SetParent(parent);
        marker.transform.localPosition = localPos;
        return marker.transform;
    }

    private Transform CreateExitMarker(Transform parent, ChunkBlueprint blueprint)
    {
        Vector2Int xCell = blueprint.exitCell;
        Vector3 localPos;

        // Prefer last support immediately to the left of X
        if (IsSupportCell(GetCellSafe(blueprint, xCell.x - 1, xCell.y)))
        {
            localPos = GetTopRightOfCell(xCell.x - 1, xCell.y, blueprint.height);
        }
        // Fallback: support directly below X
        else if (IsSupportCell(GetCellSafe(blueprint, xCell.x, xCell.y + 1)))
        {
            localPos = GetTopRightOfCell(xCell.x, xCell.y + 1, blueprint.height);
        }
        // Fallback: support to the right
        else if (IsSupportCell(GetCellSafe(blueprint, xCell.x + 1, xCell.y)))
        {
            localPos = GetTopRightOfCell(xCell.x + 1, xCell.y, blueprint.height);
        }
        else
        {
            localPos = CellToLocalPosition(xCell.x, xCell.y, blueprint.height);
        }

        GameObject marker = new GameObject("Exit");
        marker.transform.SetParent(parent);
        marker.transform.localPosition = localPos;
        return marker.transform;
    }

    private GameObject CreateSolidTile(Transform parent, Vector3 localPos, Vector2 size)
    {
        GameObject go = new GameObject("Solid");
        go.transform.SetParent(parent);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;

        int layerIndex = LayerMask.NameToLayer(solidLayerName);
        if (layerIndex != -1)
            go.layer = layerIndex;
        else
            Debug.LogWarning($"ChunkBlueprintRuntimeBuilder: Layer '{solidLayerName}' not found.");

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = solidSprite != null ? solidSprite : GetDefaultSprite();
        sr.color = Color.white;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = size;

        return go;
    }

    private GameObject CreateSpikeTile(Transform parent, Vector3 localPos, Vector2 size)
    {
        GameObject go = new GameObject("Spike");
        go.transform.SetParent(parent);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = spikeSprite != null ? spikeSprite : GetDefaultSprite();
        sr.color = spikeSprite != null ? Color.white : Color.red;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = size;

        go.AddComponent<Hazard>();

        return go;
    }

    private GameObject CreateMovingHazardMarker(Transform parent, Vector3 localPos, Vector2 size)
    {
        GameObject go = new GameObject("MovingHazardMarker");
        go.transform.SetParent(parent);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = spikeSprite != null ? spikeSprite : GetDefaultSprite();
        sr.color = spikeSprite != null ? Color.white : Color.yellow;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = size;

        go.AddComponent<Hazard>();

        return go;
    }

    private Sprite GetDefaultSprite()
    {
        Texture2D tex = Texture2D.whiteTexture;

        return Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            tex.width
        );
    }

    private void Start()
    {
        if (buildOnStart && blueprintToBuild != null)
        {
            BuildChunk(blueprintToBuild, transform.position);
        }
    }
}