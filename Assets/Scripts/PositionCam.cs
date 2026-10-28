using Unity.Cinemachine;
using UnityEngine;

public class PositionCam : MonoBehaviour
{
    [SerializeField] private float fixedDistance = 10f;

    private CinemachineOrbitalFollow orbital;

    private void Awake()
    {
        CinemachineCamera cameraComponent = GetComponent<CinemachineCamera>();
        orbital = cameraComponent.GetComponent<CinemachineOrbitalFollow>();
        SetFixedDistance();
    }

    private void LateUpdate()
    {
        SetFixedDistance();
    }

    private void SetFixedDistance()
    {
        if (orbital != null)
        {
            orbital.Radius = fixedDistance;
        }
    }
}
