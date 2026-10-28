using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SpellResetBehaviour : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Image image;
    [SerializeField] private TMP_Text messageText;

    [Header("Texto")]
    [TextArea(2, 5)]
    [SerializeField] private string message;

    [SerializeField] private float secondsPerCharacter = 0.05f;

    [Header("Animacion de imagen")]
    [SerializeField] private float targetScale = 3f;
    [SerializeField] private float rotationDegrees = 360f;

    private RectTransform imageTransform;
    private Coroutine sequenceCoroutine;

    private void Awake()
    {
        imageTransform = image.rectTransform;
    }

    public void PlaySequence()
    {
        if (sequenceCoroutine != null)
            StopCoroutine(sequenceCoroutine);

        sequenceCoroutine = StartCoroutine(PlaySequenceCoroutine());
    }

    private IEnumerator PlaySequenceCoroutine()
    {
        // Reiniciar el estado visual
        imageTransform.localScale = Vector3.zero;
        messageText.text = "";

        float textDuration = message.Length * secondsPerCharacter;
        float imageDuration = textDuration - 2f;

        // La imagen debe terminar 2 segundos antes que el texto.
        if (imageDuration > 0f)
            StartCoroutine(AnimateImage(imageDuration));

        // Efecto de maquina de escribir
        foreach (char character in message)
        {
            messageText.text += character;
            yield return new WaitForSeconds(secondsPerCharacter);
        }

        // Ejecutar al terminar la secuencia
        Restart();

        sequenceCoroutine = null;
    }

    private IEnumerator AnimateImage(float duration)
    {
        Vector3 initialScale = Vector3.zero;
        Vector3 finalScale = Vector3.one * targetScale;

        Quaternion initialRotation = imageTransform.localRotation;
        Quaternion finalRotation = initialRotation *
            Quaternion.Euler(0f, 0f, rotationDegrees);

        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsedTime / duration);

            imageTransform.localScale =
                Vector3.Lerp(initialScale, finalScale, progress);

            imageTransform.localRotation =
                Quaternion.Slerp(initialRotation, finalRotation, progress);

            yield return null;
        }

        // Asegurar los valores finales exactos
        imageTransform.localScale = finalScale;
        imageTransform.localRotation = finalRotation;
    }

    private void Restart()
    {
        Debug.Log("La secuencia ha terminado.");

        // Aqui agregaras tu comportamiento.
    }
}
