using UnityEngine;

public class IdleSoundController : StateMachineBehaviour
{
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        EnemyAudio enemyAudio = animator.GetComponent<EnemyAudio>();
        AudioSource audioSource = animator.GetComponent<AudioSource>();

        if (audioSource != null && enemyAudio != null && enemyAudio.idleSound != null)
        {
            audioSource.clip = enemyAudio.idleSound;
            audioSource.loop = true; // Forzamos el bucle
            
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        AudioSource audioSource = animator.GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }
}