using UnityEngine;
using UnityEngine.InputSystem;

public class CameraSwitcher : MonoBehaviour
{
    [Header("Referencias")]
    public Transform player;          
    public Camera topCamera;          
    public Camera frontalCamera;      

    [Header("Configuración Top")]
    public Vector3 topOffset = new Vector3(0f, 10f, -5f);
    public float positionSmoothSpeed = 8f; // Suavizado de posición
    public float rotationSmoothSpeed = 8f; // Suavizado de rotación al girar con A/D

    private bool isFirstPersonView = false;

    void Start()
    {
        UpdateCameraStates();
    }

    void LateUpdate()
    {
        if (player == null) return;

        if (!isFirstPersonView)
        {
            // 1. Suavizar Posición
            Vector3 targetPosition = player.position + topOffset;
            topCamera.transform.position = Vector3.Lerp(
                topCamera.transform.position, 
                targetPosition, 
                positionSmoothSpeed * Time.deltaTime
            );

            // 2. Suavizar Rotación (Elimina el tirón brusco con A y D)
            Quaternion targetRotation = Quaternion.LookRotation(player.position - topCamera.transform.position);
            topCamera.transform.rotation = Quaternion.Slerp(
                topCamera.transform.rotation, 
                targetRotation, 
                rotationSmoothSpeed * Time.deltaTime
            );
        }

        // Detectar tecla 'C' para alternar vistas
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
        {
            isFirstPersonView = !isFirstPersonView;
            UpdateCameraStates();
        }
    }

    void UpdateCameraStates()
    {
        if (topCamera != null) topCamera.gameObject.SetActive(!isFirstPersonView);
        if (frontalCamera != null) frontalCamera.gameObject.SetActive(isFirstPersonView);
    }
}