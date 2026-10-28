using UnityEngine;

public class PlayerFireball : MonoBehaviour
{
    private Vector3 direction;
    private float speed;
    private float lifetime;

    public void Initialize(Vector3 launchDirection, float launchSpeed, float duration)
    {
        direction = launchDirection.normalized;
        speed = launchSpeed;
        lifetime = duration;
        transform.forward = direction;
    }

    private void Update()
    {
        transform.position += direction * speed * Time.deltaTime;

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleHit(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleHit(collision.gameObject);
    }

    private void HandleHit(GameObject other)
    {
        if (other.GetComponentInParent<MoveController>() != null)
        {
            return;
        }

        GameObject enemy = other.CompareTag("Enemy") ? other : null;
        MoveBallEnemy enemyComponent = other.GetComponentInParent<MoveBallEnemy>();
        if (enemy == null && enemyComponent != null)
        {
            enemy = enemyComponent.gameObject;
        }

        if (enemy != null)
        {
            enemy.SetActive(false);
            Destroy(gameObject);
        }
    }
}
