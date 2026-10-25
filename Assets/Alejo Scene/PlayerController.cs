using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private CharacterController controller;
    private Animator animator;

    [Header("Movimiento")]
    public float walkSpeed = 4f;
    public float runSpeed = 8f;
    
    [Header("Rotación")]
    [Tooltip("Velocidad de giro en grados por segundo")]
    public float turnSpeed = 720f; // 720°/s da un giro muy fluido y natural

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        bool isRunning = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        float moveX = 0f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) moveX += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) moveX -= 1f;

        float moveZ = 0f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) moveZ += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) moveZ -= 1f;

        Vector3 move = new Vector3(moveX, 0f, moveZ).normalized;

        // --- ROTACIÓN NATURAL ---
        if (move != Vector3.zero)
        {
            // Calculamos la rotación hacia donde nos movemos
            Quaternion targetRotation = Quaternion.LookRotation(move);

            // Giramos gradualmente en grados por segundo en lugar de usar Slerp brusco
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, 
                targetRotation, 
                turnSpeed * Time.deltaTime
            );
        }

        // --- MOVIMIENTO Y GRAVEDAD ---
        controller.Move(move * currentSpeed * Time.deltaTime);

        if (!controller.isGrounded)
        {
            controller.Move(Vector3.down * 9.81f * Time.deltaTime);
        }

        // --- ANIMACIÓN SUAVE ---
        float targetAnimationSpeed = move.magnitude * (isRunning ? 1f : 0.5f);
        if (animator != null)
        {
            // Usamos DampTime para que las animaciones entre Idle, Walk y Run no entren de golpe
            animator.SetFloat("Speed", targetAnimationSpeed, 0.1f, Time.deltaTime);
        }
    }
}