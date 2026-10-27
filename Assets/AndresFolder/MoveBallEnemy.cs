using UnityEngine;

public class MoveBallEnemy : MonoBehaviour
{
    [SerializeField] private float speed = 5f;

    private void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;
    }


    private void OnTriggerEnter(Collider other)
    {
        DestroyIfPlayer(other.gameObject);
    }

    private void DestroyIfPlayer(GameObject other)
    {
        bool isPlayer = other.CompareTag("Player")
            || other.GetComponentInParent<CharacterController>() != null;

        if (isPlayer)
        {
            gameObject.SetActive(false);
        }
    }
}
