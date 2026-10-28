using UnityEngine;
using UnityEngine.InputSystem;

public class EnemyDeath : MonoBehaviour
{
    private Animator animator;
    private EnemyAudio enemyAudio;
    private bool isDead = false;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private InputAction inputAction;


    //private void OnEnable()
    //{
    //    inputAction.performed += OnInputPerformed;
    //    inputAction.Enable();
    //    isDead = false;

    //    foreach (Collider col in GetComponentsInChildren<Collider>())
    //    {
    //        col.enabled = true;
    //    }
    //}

    private void OnDisable()
    {
        if (inputAction == null)
        {
            return;
        }

        inputAction.performed -= OnInputPerformed;
        inputAction.Disable();
    }

    private void OnInputPerformed(InputAction.CallbackContext context)
    {
        Die();
    }

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        enemyAudio = GetComponentInChildren<EnemyAudio>();
    }

    private void OnEnable()
    {
        isDead = false;

        if (inputAction != null)
        {
            inputAction.performed -= OnInputPerformed;
            inputAction.performed += OnInputPerformed;
            inputAction.Enable();
        }

        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            col.enabled = true;
        }
    }

    // Llama a este método cuando la vida del enemigo llegue a 0
    [ContextMenu("Test Die")] // esto es para probar
    public void Die()
    {
        if (isDead) return;
        isDead = true;
        if (gameManager != null)
        {
            gameManager.EndGameBecauseEnemyDied();
        }
        else
        {
            Debug.LogError("EnemyDeath: falta asignar GameManager.", this);
        }

        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        if (enemyAudio != null)
        {
            enemyAudio.PlayDeathSound();
        }

        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }
    }
}
