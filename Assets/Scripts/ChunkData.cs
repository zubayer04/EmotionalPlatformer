using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum ChunkTag { Safe, Gap, Spikes, Vertical, Precision, Rest }

public class ChunkData : MonoBehaviour
{
    [Header("Sequencing")]
    [Tooltip("Main chunk category used by the Markov / state-based generator.")]
    public ChunkTag primaryTag = ChunkTag.Safe;

    [Header("Connection Points")]
    [Tooltip("Where the player enters this chunk (used for snapping). If left empty, auto-finds child named 'Entry'.")]
    public Transform entry;

    [Tooltip("Where the player exits this chunk (used for snapping + vertical offsets). If left empty, auto-finds child named 'Exit'.")]
    public Transform exit;

    [Header("Auto-calculated")]
    [Tooltip("Width in world units (optional / debugging). Not required for Entry/Exit snapping.")]
    public float chunkWidth;

    [Tooltip("Exit minus Entry in world units (useful for vertical chaining & debugging).")]
    public Vector2 exitDelta;

    [Header("Optional Tilemap Reference")]
    [Tooltip("If assigned, width is calculated from this Tilemap. If null, finds the first Tilemap in children.")]
    [SerializeField] private Tilemap widthSourceTilemap;

    [Header("Difficulty")]
    [Range(0, 10)] public int difficultyRating = 1;
    public bool hasHazard = false;
    public int estimatedJumps = 0;

    [Header("Tags")]
    [Tooltip("Additional descriptive tags for this chunk. These are useful for scoring, filtering, and future constraints.")]
    public ChunkTag[] tags;

    private void Awake()
    {
        AutoAssignEntryExit();
        CalculateExitDelta();
    }

    private void Start()
    {
        // delay one frame so tilemaps finish internal initialization
        StartCoroutine(DelayedRecalc());
    }

    private IEnumerator DelayedRecalc()
    {
        yield return null;
        CalculateExitDelta();
        CalculateWidthSafe();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoAssignEntryExit();
        CalculateExitDelta();
    }
#endif

    private void AutoAssignEntryExit()
    {
        if (entry == null) entry = FindChildByName(transform, "Entry");
        if (exit == null) exit = FindChildByName(transform, "Exit");
    }

    private void CalculateExitDelta()
    {
        if (entry != null && exit != null)
            exitDelta = (Vector2)(exit.position - entry.position);
        else
            exitDelta = Vector2.zero;
    }

    public void CalculateWidthSafe()
    {
        Tilemap tilemap = widthSourceTilemap != null
            ? widthSourceTilemap
            : GetComponentInChildren<Tilemap>();

        if (tilemap == null)
        {
            chunkWidth = 0f;
            return;
        }

        BoundsInt cb = tilemap.cellBounds;

        Grid grid = tilemap.layoutGrid;
        float cellSizeX = (grid != null) ? grid.cellSize.x : 1f;

        chunkWidth = cb.size.x * cellSizeX;
        if (chunkWidth < 0f) chunkWidth = 0f;
    }

    public bool HasTag(ChunkTag tag)
    {
        if (tags == null) return false;

        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == tag) return true;
        }

        return false;
    }

    private Transform FindChildByName(Transform root, string childName)
    {
        if (root == null) return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == childName)
                return all[i];
        }
        return null;
    }
}
