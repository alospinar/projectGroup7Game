using UnityEngine;
using UnityEngine.InputSystem;

public class MoveController : MonoBehaviour
{
    private CharacterController controller;
    private Animator animator;

    [SerializeField] private Transform cameraTransform;

    [Header("Movimiento")]
    public float walkSpeed = 4f;
    public float runSpeed = 8f;
    [SerializeField] private float bookRunSpeed = 12f;

    [Header("Rotación")]
    [Tooltip("Velocidad de giro en grados por segundo")]
    public float turnSpeed = 720f;

    [Header("Muerte")]
    [SerializeField] private string deathTrigger = "Death";

    [Header("Animación")]
    [SerializeField, Min(0f)] private float idleTransitionTime = 0.15f;

    private bool isDead;
    private bool hasBook;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        hasBook = PlayerPrefs.GetInt(BookPosition.Book1Key, 0) == 1;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        if (isDead)
        {
            return;
        }

        if (Keyboard.current == null)
        {
            return;
        }

        bool isRunning = Keyboard.current.leftShiftKey.isPressed
            || Keyboard.current.rightShiftKey.isPressed;
        float currentRunSpeed = hasBook ? bookRunSpeed : runSpeed;
        float currentSpeed = isRunning ? currentRunSpeed : walkSpeed;
        Vector3 movement = GetMovement();

        RotateTowards(movement);
        controller.Move(movement * currentSpeed * Time.deltaTime);

        ApplyGravity();
        UpdateAnimation(movement.magnitude, isRunning);
    }

    private void OnCollisionEnter(Collision collision)
    {
        DieIfHitByBall(collision.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        DieIfHitByBall(other.gameObject);
    }

    private void DieIfHitByBall(GameObject other)
    {
        bool isBall = other.CompareTag("Enemy")
            || other.GetComponentInParent<MoveBallEnemy>() != null;

        if (!isBall || isDead)
        {
            return;
        }

        Die();
    }

    public void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        controller.enabled = false;

        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.SetTrigger(deathTrigger);
        }
    }

    public void CollectBook()
    {
        hasBook = true;
        PlayerPrefs.SetInt(BookPosition.Book1Key, 1);
        PlayerPrefs.Save();
        Debug.Log("Libro book1 agregado al inventario. Carrera rápida activada.", this);
    }

    public void ResetBook()
    {
        hasBook = false;
    }

    private Vector3 GetMovement()
    {
        Keyboard keyboard = Keyboard.current;
        float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
            - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
            - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return (forward * vertical + right * horizontal).normalized;
    }

    private void RotateTowards(Vector3 movement)
    {
        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.deltaTime);
        }
    }

    private void ApplyGravity()
    {
        if (!controller.isGrounded)
        {
            controller.Move(Vector3.down * 9.81f * Time.deltaTime);
        }
    }

    private void UpdateAnimation(float movementAmount, bool isRunning)
    {
        if (animator == null)
        {
            return;
        }

        float animationSpeed = movementAmount * (isRunning ? 1f : 0.5f);

        if (movementAmount == 0f)
        {
            animator.SetFloat("Speed", animationSpeed, idleTransitionTime, Time.deltaTime);
            return;
        }

        animator.SetFloat("Speed", animationSpeed);
    }
}
