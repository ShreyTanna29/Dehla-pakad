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
    bool _pausedBySettings;
    int _settingsPauseTimeSamples;
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

    /// <summary>
    /// Settings music toggle: pause keeps playback position; on resumes from the same point.
    /// </summary>
    public void ApplyMusicSettingFromSettings()
    {
        if (bgAudioSource == null) return;

        if (!SettingsService.MusicOn)
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            // Save playhead before pause — Unity 6 AudioResource + UnPause is unreliable alone.
            try { _settingsPauseTimeSamples = bgAudioSource.timeSamples; }
            catch (System.Exception) { /* ignore */ }

            bgAudioSource.Pause();
            _pausedBySettings = true;
            return;
        }

        // User turned music ON from Settings — always resume menu BGM (even if a stale
        // gameplay-pause flag was left set while still on Home/Modes).
        if (_pausedForGameplay && !_pausedBySettings)
        {
            // Still in an active match fade-out: don't restart menu music mid-game.
            if (!IsLikelyOnMenu())
                return;
            _pausedForGameplay = false;
        }

        ResumeMenuMusicFromSettingsPause();
    }

    void ResumeMenuMusicFromSettingsPause()
    {
        if (bgAudioSource == null) return;

        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }

        bgAudioSource.mute = false;
        float vol = _maxVolume > 0.01f ? _maxVolume : 1f;
        bgAudioSource.volume = vol;

        // UnPause first (keeps position when it works).
        bgAudioSource.UnPause();

        if (!bgAudioSource.isPlaying)
        {
            // Fallback: Play + restore saved samples (handles Unity Pause/UnPause quirks).
            bgAudioSource.Play();
            RestoreSavedTimeSamples();
        }

        // Final safety: if still silent/not playing, force Play again.
        if (!bgAudioSource.isPlaying)
        {
            bgAudioSource.Stop();
            bgAudioSource.Play();
            RestoreSavedTimeSamples();
        }

        _pausedBySettings = false;
    }

    void RestoreSavedTimeSamples()
    {
        if (bgAudioSource == null || _settingsPauseTimeSamples <= 0) return;
        try
        {
            // clip may be null on Unity 6 AudioResource; timeSamples still works while playing.
            bgAudioSource.timeSamples = _settingsPauseTimeSamples;
        }
        catch (System.Exception)
        {
            // Ignore invalid sample seeks on short/unloaded clips.
        }
    }

    static bool IsLikelyOnMenu()
    {
        // Home settings / modes: game table not the active flow.
        if (NetworkManager.Instance == null) return true;
        var table = NetworkManager.Instance.gameTablePanel;
        return table == null || !table.activeInHierarchy;
    }

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
