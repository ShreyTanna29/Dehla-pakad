using UnityEngine;

/// <summary>
/// Central persistent settings used by the Home settings panel (and shared with the in-game
/// settings). Stores Sound (SFX), Music, Language, Appearance and Push-Notification preferences in
/// PlayerPrefs and applies the audio ones immediately. Sound reuses the existing "SoundMuted" key so
/// it stays in sync with <see cref="InGameSettingsController"/>.
/// </summary>
public static class SettingsService
{
    public const string PREF_SOUND_MUTED = "SoundMuted";   // 1 = muted (shared with in-game settings)
    public const string PREF_MUSIC_MUTED = "MusicMuted";   // 1 = muted
    public const string PREF_LANGUAGE = "LanguageIndex";
    public const string PREF_APPEARANCE = "AppearanceIndex";
    public const string PREF_PUSH = "PushNotifications";    // 1 = on

    public static readonly string[] Languages = { "English", "Hindi", "Español", "Français", "Deutsch" };
    public static readonly string[] Appearances = { "Modern", "Classic" };

    // ---- Sound (SFX / master) ----
    public static bool SoundOn
    {
        get => PlayerPrefs.GetInt(PREF_SOUND_MUTED, 0) == 0;
        set
        {
            PlayerPrefs.SetInt(PREF_SOUND_MUTED, value ? 0 : 1);
            PlayerPrefs.Save();
            ApplyAudio();
        }
    }

    // ---- Music ----
    public static bool MusicOn
    {
        get => PlayerPrefs.GetInt(PREF_MUSIC_MUTED, 0) == 0;
        set
        {
            PlayerPrefs.SetInt(PREF_MUSIC_MUTED, value ? 0 : 1);
            PlayerPrefs.Save();
            ApplyAudio();
        }
    }

    public static int LanguageIndex
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(PREF_LANGUAGE, 0), 0, Languages.Length - 1);
        set { PlayerPrefs.SetInt(PREF_LANGUAGE, Mathf.Clamp(value, 0, Languages.Length - 1)); PlayerPrefs.Save(); }
    }
    public static string LanguageName => Languages[LanguageIndex];

    public static int AppearanceIndex
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(PREF_APPEARANCE, 0), 0, Appearances.Length - 1);
        set { PlayerPrefs.SetInt(PREF_APPEARANCE, Mathf.Clamp(value, 0, Appearances.Length - 1)); PlayerPrefs.Save(); }
    }
    public static string AppearanceName => Appearances[AppearanceIndex];

    public static bool PushNotifications
    {
        get => PlayerPrefs.GetInt(PREF_PUSH, 1) == 1;
        set { PlayerPrefs.SetInt(PREF_PUSH, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// Applies the audio prefs. Master/SFX go through AudioListener.volume. Music is applied to any
    /// AudioSource that looks like background music (looping). New music players can also query
    /// <see cref="MusicOn"/> directly.
    /// </summary>
    public static void ApplyAudio()
    {
        AudioListener.volume = SoundOn ? 1f : 0f;

        bool musicOn = MusicOn;
        var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        foreach (var src in sources)
        {
            if (src == null) continue;
            // Treat looping sources as background music.
            if (src.loop)
                src.mute = !musicOn;
        }
    }

    /// <summary>Restores all settings to defaults.</summary>
    public static void ResetToDefaults()
    {
        PlayerPrefs.SetInt(PREF_SOUND_MUTED, 0);
        PlayerPrefs.SetInt(PREF_MUSIC_MUTED, 0);
        PlayerPrefs.SetInt(PREF_LANGUAGE, 0);
        PlayerPrefs.SetInt(PREF_APPEARANCE, 0);
        PlayerPrefs.SetInt(PREF_PUSH, 1);
        PlayerPrefs.Save();
        ApplyAudio();
    }
}
