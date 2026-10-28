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

        EnemyDeath enemyDeath = other.GetComponentInParent<EnemyDeath>();
        if (enemyDeath != null)
        {
            enemyDeath.Die();
            Destroy(gameObject);
            return;
        }

        MoveBallEnemy fireballEnemy = other.GetComponentInParent<MoveBallEnemy>();
        if (fireballEnemy != null)
        {
            fireballEnemy.gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
