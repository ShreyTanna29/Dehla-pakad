using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Handles the Android hardware Back button (mapped to Escape) so it never silently quits the app or
/// throws during ad-hoc room teardown. Priority: (1) close an open Settings panel, (2) close the
/// Player Profile panel, (3) while in a match, show the in-game "Exit Game?" confirmation. On the
/// home screen with nothing open it does nothing (prevents accidental quit). Self-creates after the
/// scene loads — no scene wiring required.
/// </summary>
public class AndroidBackHandler : MonoBehaviour
{
    static AndroidBackHandler _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (_instance != null) return;
        var go = new GameObject("AndroidBackHandler");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<AndroidBackHandler>();
    }

    float _lastBackTime;

    void Update()
    {
        bool back = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) back = true;
#endif
        if (!back) return;

        // debounce (hardware back can double-fire)
        if (Time.unscaledTime - _lastBackTime < 0.4f) return;
        _lastBackTime = Time.unscaledTime;

        HandleBack();
    }

    void HandleBack()
    {
        // 1. Settings panel open -> close it
        GameObject settings = FindActive("Panel_Settings");
        if (settings != null)
        {
            var c = settings.GetComponent<HomeSettingsController>();
            if (c != null) c.Close(); else settings.SetActive(false);
            return;
        }

        // 2. Player profile / profile setup open -> close
        foreach (string n in new[] { "Panel_PlayerProfile", "Panel_ProfileSetup" })
        {
            GameObject p = FindActive(n);
            if (p != null) { p.SetActive(false); return; }
        }

        // 3. In a match -> show exit confirm (never hard-quit / double-leave)
        if (InGameSettingsController.Instance != null && InMatch())
        {
            InGameSettingsController.Instance.RequestExitFromBack();
            return;
        }

        // 4. Home with nothing open: intentionally no-op (avoids accidental app quit).
    }

    static bool InMatch()
    {
        switch (GameFlowState.Current)
        {
            case GameFlowPhase.InRoom:
            case GameFlowPhase.Dealing:
            case GameFlowPhase.InGame:
            case GameFlowPhase.ResolvingTrick:
            case GameFlowPhase.GameFinished:
                return true;
            default:
                return Photon.Pun.PhotonNetwork.InRoom;
        }
    }

    static GameObject FindActive(string name)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == name && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy)
                return t.gameObject;
        return null;
    }
}
