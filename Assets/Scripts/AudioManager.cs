using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("Dash SFX")]
    [SerializeField] AudioClip dashClip;
    [SerializeField] [Range(0,1)] float dashVolume = 1f;

    [Header("Muerte SFX")]
    [SerializeField] AudioClip deathClip;
    [SerializeField] [Range(0,1)] float deathVolume = 1f;

    [Header("Libro SFX")]
    [SerializeField] AudioClip bookClip;
    [SerializeField] [Range(0,1)] float bookVolume = 1f;

    [Header("Fire SFX")]
    [SerializeField] AudioClip fireClip;
    [SerializeField] [Range(0,1)] float fireVolume = 1f;
    [Header("BookDebuff SFX")]
    [SerializeField] AudioClip bookdClip;
    [SerializeField] [Range(0,1)] float bookdVolume = 1f;

    public static AudioManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void PlayDashSFX()
    {
        PlayAudioClip(dashClip,dashVolume);
    }

    public void PlayDeathSFX()
    {
        PlayAudioClip(deathClip, deathVolume);
    }

    public void PlayBookSFX()
    {
        PlayAudioClip(bookClip, bookVolume);
    }

    public void PlayFireSFX()
    {
        PlayAudioClip(fireClip, fireVolume);
    }
    public void PlayBookDefSFX()
    {
        PlayAudioClip(bookdClip, bookdVolume);
    }

    private void PlayAudioClip(AudioClip clip, float volume)
    {
        if (clip != null)
        {
            Vector3 position = Camera.main != null
                ? Camera.main.transform.position
                : transform.position;

            AudioSource.PlayClipAtPoint(clip, position, volume);
        }
    }
}
