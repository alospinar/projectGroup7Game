using UnityEngine;

public class DashBook : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        MoveController player = other.GetComponentInParent<MoveController>();
        if (player == null)
        {
            return;
        }

        player.UnlockDash();
        AudioManager.Instance?.PlayBookSFX();
        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            gameManager.AddBookTime();
        }

        gameObject.SetActive(false);
    }
}
