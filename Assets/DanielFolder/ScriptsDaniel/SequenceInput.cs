using UnityEngine;
using UnityEngine.InputSystem;

public class SequenceInput : MonoBehaviour
{
    [SerializeField] private SpellResetBehaviour spellRestart;
    [SerializeField] private InputAction inputAction;

    private void OnEnable()
    {
        inputAction.performed += OnInputPerformed;
        inputAction.Enable();
    }

    private void OnDisable()
    {
        inputAction.performed -= OnInputPerformed;
        inputAction.Disable();
    }

    private void OnInputPerformed(InputAction.CallbackContext context)
    {
        spellRestart.PlaySequence();
    }
}