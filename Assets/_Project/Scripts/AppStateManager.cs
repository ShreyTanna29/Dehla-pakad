using UnityEngine;

/// <summary>
/// TASK 22 (Keep App Active in Background) + TASK 44 (Landscape orientation, auto-rotate L/R).
///
/// Owns application-lifecycle policy in one place. Note: NetworkManager.Awake already sets
/// Application.runInBackground = true and Screen.sleepTimeout = NeverSleep, and configures
/// Photon's KeepAliveInBackground. This manager reinforces those settings (idempotent / harmless)
/// and centralizes orientation + pause/focus logging. It does NOT disconnect Photon on pause —
/// the network reconnect logic stays in NetworkManager.OnApplicationPause/Focus.
/// </summary>
[DefaultExecutionOrder(-190)]
public class AppStateManager : MonoBehaviour
{
    public static AppStateManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("AppStateManager");
        go.AddComponent<AppStateManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        ApplyLandscapeOrientation();
    }

    void Start()
    {
        ApplyLandscapeOrientation();
    }

    // ============================================================
    // TASK 44 — ORIENTATION: locked to landscape, auto-rotate between Left & Right
    // ============================================================
    public void ApplyLandscapeOrientation()
    {
        // Allow ONLY the two landscape orientations, then enable auto-rotation so the device
        // can flip between Landscape Left and Landscape Right (but never portrait).
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;

        // Must be set AFTER the allowed flags above for auto-rotation to take effect.
        Screen.orientation = ScreenOrientation.AutoRotation;
    }

    // ============================================================
    // TASK 22 — BACKGROUND / FOREGROUND LIFECYCLE
    // ============================================================
    void OnApplicationPause(bool paused)
    {
        if (!paused)
        {
            Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            ApplyLandscapeOrientation();
        }
        Debug.Log($"[AppStateManager] OnApplicationPause(paused={paused})");
    }

    void OnApplicationFocus(bool hasFocus)
    {
        Debug.Log($"[AppStateManager] OnApplicationFocus(hasFocus={hasFocus})");
    }

    void OnApplicationQuit()
    {
        // Explicit OS/user kill — nothing to keep alive here.
        Debug.Log("[AppStateManager] OnApplicationQuit");
    }
}
