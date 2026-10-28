using UnityEngine;

public class ReflectBook : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        MoveController player = other.GetComponentInParent<MoveController>();
        if (player == null)
        {
            return;
        }

        player.UnlockReflect();
        gameObject.SetActive(false);
    }
}
