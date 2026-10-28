using UnityEngine;

public class ChasingBallEnemy : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float speed = 3f;
    [SerializeField] private float lifetime = 8f;

    private float elapsedTime;

    public void SetPlayer(Transform target)
    {
        player = target;
    }

    private void OnEnable()
    {
        elapsedTime = 0f;
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        Vector3 direction = player.position - transform.position;
        

        if (direction.sqrMagnitude > 0.01f)
        {
            transform.position += direction.normalized * speed * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(direction);
        }

        elapsedTime += Time.deltaTime;

        if (elapsedTime >= lifetime)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")
            || other.GetComponentInParent<CharacterController>() != null)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player")
            || collision.gameObject.GetComponentInParent<CharacterController>() != null)
        {
            gameObject.SetActive(false);
        }
    }
}