using System.Collections;
using UnityEngine;

/// <summary>
/// Background music for Home and Modes only — fades out when gameplay starts and resumes on return.
/// </summary>
public class BGAudioManager : MonoBehaviour
{
    public static BGAudioManager Instance { get; private set; }

    public AudioSource bgAudioSource;
    public float fadeDuration = 0.35f;

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
        if (bgAudioSource.volume > 0.01f)
            _maxVolume = bgAudioSource.volume;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Home or Modes panel became visible — play / resume from paused position.</summary>
    public void OnMenuScreenShown() => FadeInMenuMusic();

    public void FadeInMenuMusic(float duration = -1f)
    {
        if (bgAudioSource == null) return;

        _pausedForGameplay = false;

        if (!SettingsService.MusicOn)
        {
            bgAudioSource.Pause();
            return;
        }

        Debug.Log("[Audio] Menu music fade in");

        bgAudioSource.UnPause();
        if (!bgAudioSource.isPlaying)
            bgAudioSource.Play();

        if (bgAudioSource.volume >= _maxVolume - 0.01f)
        {
            bgAudioSource.volume = _maxVolume;
            return;
        }

        RestartFade(FadeInCoroutine(duration > 0f ? duration : fadeDuration));
    }

    /// <summary>Player left menu flow (Start clicked or game table shown) — fade out and pause.</summary>
    public void OnGameplayStarting() => FadeOutMenuMusic();

    public void FadeOutMenuMusic(float duration = -1f)
    {
        if (bgAudioSource == null) return;
        if (_pausedForGameplay && !bgAudioSource.isPlaying && bgAudioSource.volume <= 0.01f)
            return;

        _pausedForGameplay = true;
        Debug.Log("[Audio] Menu music fade out");
        RestartFade(FadeOutCoroutine(duration > 0f ? duration : fadeDuration));
    }

    public void FadeOutAndPause() => FadeOutMenuMusic();

    public void ResumeAndFadeIn() => OnMenuScreenShown();

    void RestartFade(IEnumerator routine)
    {
        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(routine);
    }

    IEnumerator FadeOutCoroutine(float duration)
    {
        float startVol = bgAudioSource.volume;
        if (startVol <= 0f)
        {
            bgAudioSource.Pause();
            _fadeRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            bgAudioSource.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }

        bgAudioSource.volume = 0f;
        bgAudioSource.Pause();
        _fadeRoutine = null;
    }

    IEnumerator FadeInCoroutine(float duration)
    {
        float startVol = bgAudioSource.volume;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            bgAudioSource.volume = Mathf.Lerp(startVol, _maxVolume, elapsed / duration);
            yield return null;
        }

        bgAudioSource.volume = _maxVolume;
        _fadeRoutine = null;
    }
}
