using System.Collections;
using UnityEngine;

/// <summary>
/// Background music for Home and Modes only — fades out when gameplay starts and resumes on return.
/// </summary>
public class BGAudioManager : MonoBehaviour
{
    public static BGAudioManager Instance { get; private set; }

    public AudioSource bgAudioSource;
    public float fadeDuration = 1.2f;

    float _maxVolume = 1f;
    bool _pausedForGameplay;
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

        if (bgAudioSource != null)
            bgAudioSource.loop = true;
    }

    void Start()
    {
        if (bgAudioSource == null) return;
        _maxVolume = bgAudioSource.volume;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Home or Modes panel became visible — play / resume from paused position.</summary>
    public void OnMenuScreenShown()
    {
        if (bgAudioSource == null) return;

        _pausedForGameplay = false;

        if (!SettingsService.MusicOn)
        {
            bgAudioSource.Pause();
            return;
        }

        bgAudioSource.UnPause();
        if (!bgAudioSource.isPlaying)
            bgAudioSource.Play();

        if (bgAudioSource.volume >= _maxVolume - 0.01f)
        {
            bgAudioSource.volume = _maxVolume;
            return;
        }

        RestartFade(FadeInCoroutine());
    }

    /// <summary>Player left menu flow (Start clicked or game table shown) — fade out and pause.</summary>
    public void OnGameplayStarting()
    {
        if (bgAudioSource == null) return;
        if (_pausedForGameplay && !bgAudioSource.isPlaying && bgAudioSource.volume <= 0.01f)
            return;

        _pausedForGameplay = true;
        FadeOutAndPause();
    }

    public void FadeOutAndPause()
    {
        if (bgAudioSource == null) return;
        RestartFade(FadeOutCoroutine());
    }

    public void ResumeAndFadeIn() => OnMenuScreenShown();

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
            _fadeRoutine = null;
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
