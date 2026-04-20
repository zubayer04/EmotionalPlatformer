using UnityEngine;

public class Hazard : MonoBehaviour
{
    private ChunkData parentChunk;

    private void Awake()
    {
        parentChunk = GetComponentInParent<ChunkData>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        if (LevelManager.Instance != null)
            LevelManager.Instance.OnPlayerDied(parentChunk, "Hazard");
        else
            Debug.LogWarning("Hazard: No LevelManager.Instance found.");
    }
}