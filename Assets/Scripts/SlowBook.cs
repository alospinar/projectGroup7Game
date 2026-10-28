using UnityEngine;

public class SlowBook : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float slowDuration = 3f;

    private void OnTriggerEnter(Collider other)
    {
        MoveController player = other.GetComponentInParent<MoveController>();
        if (player == null)
        {
            return;
        }

        player.ApplySlow(slowDuration);
        AudioManager.Instance?.PlayBookDefSFX();
        gameObject.SetActive(false);
    }
}
