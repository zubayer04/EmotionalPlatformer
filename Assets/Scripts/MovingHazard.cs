using UnityEngine;

public class MovingHazard : MonoBehaviour
{
    [Header("Path")]
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;

    [Header("Movement")]
    [SerializeField] private float speed = 2f;
    [SerializeField] private float pauseTime = 0.15f;

    private Transform target;
    private float pauseTimer;

    private void Start()
    {
        if (pointA == null || pointB == null)
        {
            Debug.LogWarning($"{name}: Missing PointA/PointB references.");
            enabled = false;
            return;
        }

        transform.position = pointA.position;
        target = pointB;
    }

    private void Update()
    {
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.deltaTime;
            return;
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target.position,
            speed * Time.deltaTime
        );

        if (Vector3.Distance(transform.position, target.position) < 0.01f)
        {
            pauseTimer = pauseTime;
            target = (target == pointA) ? pointB : pointA;
        }
    }
}