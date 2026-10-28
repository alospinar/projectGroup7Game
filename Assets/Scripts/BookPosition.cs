using UnityEngine;

public class BookPosition : MonoBehaviour
{
    [SerializeField] private Transform positionContainer;
    [SerializeField, Min(0.1f)] private float changeInterval = 10f;

    private float timer;

    private void Start()
    {
        MoveToRandomPosition();
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= changeInterval)
        {
            timer = 0f;
            MoveToRandomPosition();
        }
    }

    private void MoveToRandomPosition()
    {
        if (positionContainer == null || positionContainer.childCount == 0)
        {
            enabled = false;
            return;
        }

        Transform randomPosition = positionContainer.GetChild(
            Random.Range(0, positionContainer.childCount));

        transform.position = randomPosition.position;
    }

    private void OnTriggerEnter(Collider other)
    {
        MoveController player = other.GetComponentInParent<MoveController>();
        if (player == null)
        {
            return;
        }

        player.CollectBook();
        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            gameManager.AddBookTime();
        }

        gameObject.SetActive(false);
    }
}
