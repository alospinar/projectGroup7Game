using UnityEngine;

public class EnemyDeath : MonoBehaviour
{
    private Animator animator;
    private EnemyAudio enemyAudio;
    private bool isDead = false;

    void Start()
    {
        animator = GetComponent<Animator>();
        enemyAudio = GetComponent<EnemyAudio>();
    }

    // Llama a este método cuando la vida del enemigo llegue a 0
    [ContextMenu("Test Die")] // esto es para probar
    public void Die()
    {
        if (isDead) return;
        isDead = true;

        // 1. Activa la animación de muerte
        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        // 2. Reproduce el sonido de muerte y detiene respiración
        if (enemyAudio != null)
        {
            enemyAudio.PlayDeathSound();
        }

        // 3. Opcional: Desactivar colliders para que el jugador no tropiece con el cadáver
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }
}
