using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private CharacterController controller;
    private Animator animator;

    [Header("Movimiento")]
    public float walkSpeed = 4f;
    public float runSpeed = 8f;
    public float rotationSpeed = 15f; // Velocidad para que el giro sea suave

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // Detectar si presiona Shift para correr
        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        // Capturar los ejes
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        // Vector de movimiento (.normalized evita que el personaje corra más rápido en diagonal)
        Vector3 move = new Vector3(moveX, 0, moveZ).normalized;
        
        // Mover al personaje
        controller.Move(move * currentSpeed * Time.deltaTime);

        // Gravedad simple para mantenerlo pegado al suelo
        if (controller.isGrounded == false)
        {
            controller.Move(Vector3.down * 9.81f * Time.deltaTime);
        }

        // Pasar la velocidad al Animator (0 = Idle, 0.5 = Walk, 1 = Run)
        float animationSpeed = move.magnitude * (isRunning ? 1f : 0.5f);
        
        if (animator != null)
        {
            animator.SetFloat("Speed", animationSpeed);
        }

        // Rotación suave hacia donde se dirige
        if (move != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }
}