using UnityEngine;

public class KillZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        if (LevelManager.Instance != null)
            LevelManager.Instance.OnPlayerDied(null, "KillZone");
    }
}