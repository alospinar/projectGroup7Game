using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MoveController : MonoBehaviour
{
    private CharacterController controller;
    private Animator animator;

    [SerializeField] private Transform cameraTransform;
    [SerializeField] private GameManager gameManager;

    [Header("Movimiento")]
    public float walkSpeed = 4f;
    public float runSpeed = 8f;
    public float slowMultiplier = 0.35f;
    [SerializeField] private float bookRunSpeed = 12f;

    [Header("Dash")]
    [SerializeField] private Key dashKey = Key.E;
    [SerializeField, Min(0.1f)] private float dashDistance = 5f;
    [SerializeField, Min(0.01f)] private float dashDuration = 0.15f;
    [SerializeField, Min(0f)] private float dashCooldown = 2f;

    [Header("Bola de fuego")]
    [SerializeField] private Key reflectKey = Key.F;
    [SerializeField] private GameObject fireballPrefab;
    [SerializeField, Min(0.1f)] private float fireballSpeed = 12f;
    [SerializeField, Min(0.1f)] private float fireballLifetime = 3f;
    [SerializeField, Min(0f)] private float fireballCooldown = 1f;
    [SerializeField, Min(0.1f)] private float fireballSpawnDistance = 1.2f;
    [SerializeField] private float fireballSpawnHeight = 1f;
    [SerializeField, Min(0f)] private float fireballAttackDelay = 0.35f;
    [SerializeField] private string attackTrigger = "Attack";

    [Header("UIManager")]
    [SerializeField] private MenuManager menuManager;

    //[Header("Rotación")]
    //[Tooltip("Velocidad de giro en grados por segundo")]
    private float turnSpeed = 720f;

    //[Header("Muerte")]
    private string deathTrigger = "Death";
    [SerializeField, Min(0f)] private float deathRestartDelay = 2f;

    //[Header("Animación")]
    private float idleTransitionTime = 0.15f;
    private int bookCount;
    private bool isDead;
    public TMP_Text bookCountText;
    private bool hasBook;
    private bool hasDash;
    private bool hasReflect;
    private bool isSlowed;
    private bool isDashing;
    private Coroutine slowRoutine;
    private float dashCooldownTimer;
    private float fireballCooldownTimer;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
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
        dashCooldownTimer -= Time.deltaTime;
        fireballCooldownTimer -= Time.deltaTime;

        bool dashPressed = Keyboard.current[dashKey].wasPressedThisFrame;
        if (dashPressed && !hasDash)
        {
            Debug.Log("Dash bloqueado: recoge Book2 en este intento.", this);
        }

        if (hasDash && dashPressed && dashCooldownTimer <= 0f && !isDashing)
        {
            StartCoroutine(Dash());
        }

        if (hasReflect && Keyboard.current[reflectKey].wasPressedThisFrame
            && fireballCooldownTimer <= 0f && !isDashing)
        {
            StartCoroutine(AttackWithFireball());
        }

        if (isDashing)
        {
            return;
        }

        float currentRunSpeed = hasBook ? bookRunSpeed : runSpeed;
        float currentSpeed = isRunning ? currentRunSpeed : walkSpeed;
        currentSpeed *= isSlowed ? slowMultiplier : 1f;
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

    private IEnumerator AttackWithFireball()
    {
        if (fireballPrefab == null)
        {
            Debug.LogError(
                "MoveController: asigna el prefab de la bola de fuego del jugador.",
                this);

            yield break;
        }

        fireballCooldownTimer = fireballCooldown;

        if (animator != null)
        {
            animator.SetTrigger(attackTrigger);
            yield return null;
            yield return new WaitForSeconds(fireballAttackDelay);
        }

        LaunchFireball();
    }

    private void LaunchFireball()
    {
        Vector3 launchDirection = transform.forward;
        Vector3 spawnPosition = transform.position
            + launchDirection * fireballSpawnDistance
            + Vector3.up * fireballSpawnHeight;
        GameObject fireballObject = Instantiate(fireballPrefab, spawnPosition, Quaternion.LookRotation(launchDirection));
        PlayerFireball fireball = fireballObject.GetComponent<PlayerFireball>();

        if (fireball == null)
        {
            Destroy(fireballObject);
            Debug.LogError("MoveController: el prefab debe tener el componente PlayerFireball.", this);
            return;
        }

        fireball.Initialize(launchDirection, fireballSpeed, fireballLifetime);
        AudioManager.Instance?.PlayFireSFX();
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
        AudioManager.Instance?.PlayDeathSFX();

        if (gameManager != null)
        {
            gameManager.playerIsDead = true;
        }
        else
        {
            Debug.LogError("MoveController: falta asignar GameManager.", this);
        }

        controller.enabled = false;

        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.SetTrigger(deathTrigger);
        }

        Invoke(nameof(RestartAttempt), deathRestartDelay);
    }

    private void RestartAttempt()
    {
        menuManager.PauseGame(true);
    }

    public void CollectBook()
    {
        hasBook = true;
        AudioManager.Instance?.PlayBookSFX();
        Debug.Log("Libro book1 agregado al inventario. Carrera rápida activada.", this);
        bookCount++;
        bookCountText.text = ": " + bookCount.ToString();


    }

    public void ResetBook()
    {
        hasBook = false;
        bookCount = 0;
        bookCountText.text = ": " + bookCount.ToString();
    }

    public void UnlockDash()
    {
        hasDash = true;
        Debug.Log("Book2 agregado al inventario. Dash desbloqueado.", this);
    }

    public void ResetDash()
    {
        hasDash = false;
    }

    public void UnlockReflect()
    {
        hasReflect = true;
        Debug.Log("Book3 agregado al inventario. Reflejo desbloqueado.", this);
    }

    public void ResetReflect()
    {
        hasReflect = false;
    }

    private IEnumerator Dash()
    {
        isDashing = true;
        dashCooldownTimer = dashCooldown;
        Debug.Log("Dash activado.", this);
        AudioManager.Instance?.PlayDashSFX();

        Vector3 dashDirection = GetMovement();
        if (dashDirection == Vector3.zero)
        {
            dashDirection = transform.forward;
        }

        float dashSpeed = dashDistance / dashDuration;
        float elapsedDashTime = 0f;

        while (elapsedDashTime < dashDuration && !isDead && controller.enabled)
        {
            controller.Move(dashDirection * dashSpeed * Time.deltaTime);
            elapsedDashTime += Time.deltaTime;
            yield return null;
        }

        isDashing = false;
    }

    public void ApplySlow(float duration)
    {
        if (slowRoutine != null)
        {
            StopCoroutine(slowRoutine);
        }

        slowRoutine = StartCoroutine(SlowRoutine(duration));
    }

    private IEnumerator SlowRoutine(float duration)
    {
        isSlowed = true;
        yield return new WaitForSeconds(duration);
        isSlowed = false;
        slowRoutine = null;
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
