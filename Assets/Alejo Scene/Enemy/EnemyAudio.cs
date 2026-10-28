
using UnityEngine;

public class EnemyAudio : MonoBehaviour
{
    [Header("Configuración de Sonidos")]
    public AudioSource audioSource;
    public AudioClip attackSound;
    public AudioClip idleSound;
    public AudioClip deathSound;

    void Start()
    {
        // Si no asignaste el AudioSource en el inspector, lo busca automáticamente
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    // Método para reproducir el sonido de ataque
    public void PlayAttackSound()
    {
        if (audioSource != null && attackSound != null)
        {
            audioSource.PlayOneShot(attackSound);
        }
    }

    // Método para reproducir un gruñido o sonido en Idle
    public void PlayIdleSound()
    {
        if (audioSource != null && idleSound != null)
        {
            audioSource.PlayOneShot(idleSound);
        }
    }
    // Método para reproducir la muerte y detener otros sonidos
    public void PlayDeathSound()
    {
        if (audioSource != null)
        {
            audioSource.Stop(); // Detiene la respiración en bucle
            if (deathSound != null)
            {
                audioSource.PlayOneShot(deathSound);
            }
        }
    }
}