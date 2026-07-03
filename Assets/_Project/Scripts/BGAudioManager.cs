using System.Collections;
using UnityEngine;

/// <summary>
/// Background music with fade out when gameplay starts and fade in when returning home.
/// </summary>
public class BGAudioManager : MonoBehaviour
{
    public static BGAudioManager Instance { get; private set; }

    public AudioSource bgAudioSource;
    public float fadeDuration = 1f;

    float _maxVolume = 1f;
    Coroutine _fadeRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (bgAudioSource == null)
            bgAudioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        if (bgAudioSource == null) return;

        _maxVolume = bgAudioSource.volume;
        if (!bgAudioSource.isPlaying)
            bgAudioSource.Play();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void FadeOutAndPause()
    {
        if (bgAudioSource == null) return;
        RestartFade(FadeOutCoroutine());
    }

    public void ResumeAndFadeIn()
    {
        if (bgAudioSource == null) return;

        if (!SettingsService.MusicOn)
        {
            bgAudioSource.Pause();
            return;
        }

        bgAudioSource.UnPause();
        if (!bgAudioSource.isPlaying)
            bgAudioSource.Play();

        RestartFade(FadeInCoroutine());
    }

    void RestartFade(IEnumerator routine)
    {
        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(routine);
    }

    IEnumerator FadeOutCoroutine()
    {
        float startVol = bgAudioSource.volume;
        if (startVol <= 0f)
        {
            bgAudioSource.Pause();
            yield break;
        }

        while (bgAudioSource.volume > 0f)
        {
            bgAudioSource.volume -= startVol * (Time.unscaledDeltaTime / fadeDuration);
            if (bgAudioSource.volume < 0f)
                bgAudioSource.volume = 0f;
            yield return null;
        }

        bgAudioSource.Pause();
        _fadeRoutine = null;
    }

    IEnumerator FadeInCoroutine()
    {
        while (bgAudioSource.volume < _maxVolume)
        {
            bgAudioSource.volume += _maxVolume * (Time.unscaledDeltaTime / fadeDuration);
            if (bgAudioSource.volume > _maxVolume)
                bgAudioSource.volume = _maxVolume;
            yield return null;
        }

        _fadeRoutine = null;
    }
}
