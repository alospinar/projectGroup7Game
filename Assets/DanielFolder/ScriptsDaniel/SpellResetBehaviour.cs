using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SpellResetBehaviour : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Image image;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private MenuManager menuManager;

    [Header("Texto")]
    [TextArea(2, 5)]
    [SerializeField] private string message;
    [SerializeField] private string messageWin;

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

    public void PlaySequence(bool won)
    {
        if (sequenceCoroutine != null)
            StopCoroutine(sequenceCoroutine);

        string messageToShow = won ? messageWin : message;
        sequenceCoroutine = StartCoroutine(PlaySequenceCoroutine(messageToShow));
    }

    private IEnumerator PlaySequenceCoroutine(string messageToShow)
    {
        imageTransform.localScale = Vector3.zero;
        messageText.text = "";

        float textDuration = messageToShow.Length * secondsPerCharacter;
        float imageDuration = textDuration - 2f;

        if (imageDuration > 0f)
            StartCoroutine(AnimateImage(imageDuration));

        foreach (char character in messageToShow)
        {
            messageText.text += character;
            yield return new WaitForSeconds(secondsPerCharacter);
        }

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
        menuManager.PlayGame();
    }
}
