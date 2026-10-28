using UnityEngine;

public class MoveBallEnemy : MonoBehaviour
{
    [SerializeField] private float speed = 5f;
    private bool isReflected;

    private void OnEnable()
    {
        isReflected = false;
    }

    private void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    public void SetDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.01f)
        {
            transform.forward = direction.normalized;
        }
    }

    public void Reflect(Vector3 playerPosition)
    {
        if (isReflected)
        {
            return;
        }

        Vector3 direction = transform.position - playerPosition;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.01f)
        {
            direction = -transform.forward;
        }

        transform.forward = direction.normalized;
        isReflected = true;
        Debug.Log("Bola reflejada.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        DestroyIfPlayer(other.gameObject);
    }

    private void DestroyIfPlayer(GameObject other)
    {
        bool isPlayer = !isReflected
            && (other.CompareTag("Player")
            || other.GetComponentInParent<CharacterController>() != null);

        if (isPlayer)
        {
            gameObject.SetActive(false);
        }
    }
}
