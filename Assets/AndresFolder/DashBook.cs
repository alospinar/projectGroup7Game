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
        gameObject.SetActive(false);
    }
}
