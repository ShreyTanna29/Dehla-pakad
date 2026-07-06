using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Builds a Callbreak-style circular radial turn timer over each player's profile picture, fully via
/// code. One "CircularTimer" Image is created as a child of each seat profile (seat 0 = You, 1 = Left,
/// 2 = Top, 3 = Right) using a radial-360 filled Image. The old horizontal timer bar is disabled.
///
/// IMPORTANT — it does NOT run its own countdown. In this project the turn timer is authoritative and
/// network-synced by <see cref="TurnManager"/> (RPC_UpdateTime / currentActorTurn). A standalone local
/// 18s coroutine would desync from the real timer, ignore pauses, and light the wrong seat. Instead
/// TurnManager drives <see cref="ShowForSeat"/> each tick, and we smoothly tween the radial fill
/// between ticks so it visually drains 1 -> 0 over the 18s turn.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DynamicTimerSetup : MonoBehaviour
{
    public static DynamicTimerSetup Instance;

    [Header("Player profile roots, seat-indexed (0=You, 1=Left, 2=Top, 3=Right).")]
    [Tooltip("Leave empty to auto-resolve by name: You / Opponent_Left / Opponent_Top / Opponent_Right.")]
    public Transform[] playerProfiles = new Transform[4];

    [Header("Old horizontal timer to disable")]
    [Tooltip("Leave empty to auto-resolve the legacy 'TimerFill' object.")]
    public GameObject oldHorizontalTimer;

    [Header("Appearance")]
    [Tooltip("Pixels the ring extends beyond the profile on every side, so it reads as a border.")]
    public float ringExpand = 10f;
    public Color fullColor = Color.green;
    public Color midColor = Color.yellow;
    public Color lowColor = Color.red;
    [Tooltip("Seconds the fill tweens between network ticks (keep ~1s = one tick).")]
    public float tweenPerTick = 1f;

    static readonly string[] DefaultProfileNames = { "You", "Opponent_Left", "Opponent_Top", "Opponent_Right" };

    readonly UnityEngine.UI.Image[] _timers = new UnityEngine.UI.Image[4];
    int _activeSeat = -1;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }
    }

    void Start()
    {
        ResolveProfiles();
        BuildTimers();
        DisableOldTimer();
        HideAll();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============================================================
    // SETUP
    // ============================================================

    void ResolveProfiles()
    {
        if (playerProfiles == null || playerProfiles.Length < 4)
        {
            var grown = new Transform[4];
            if (playerProfiles != null)
                for (int i = 0; i < playerProfiles.Length && i < 4; i++) grown[i] = playerProfiles[i];
            playerProfiles = grown;
        }

        for (int seat = 0; seat < 4; seat++)
        {
            if (playerProfiles[seat] != null) continue;
            if (UiSafeLookup.TryGet(DefaultProfileNames[seat], out GameObject go) && go != null)
                playerProfiles[seat] = go.transform;
            else
                Debug.LogWarning($"[DynamicTimerSetup] Could not resolve profile for seat {seat} ('{DefaultProfileNames[seat]}'). Assign it in the inspector.");
        }
    }

    void BuildTimers()
    {
        Sprite ringSprite = GetRingSprite();

        for (int seat = 0; seat < 4; seat++)
        {
            Transform profile = playerProfiles[seat];
            if (profile == null) continue;

            // Reuse if a CircularTimer already exists (e.g. scene reload), else create one.
            Transform existing = profile.Find("CircularTimer");
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject("CircularTimer", typeof(RectTransform));
            if (existing == null) go.transform.SetParent(profile, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Slightly LARGER than the profile on every side -> the rim shows as a border.
            rt.offsetMin = new Vector2(-ringExpand, -ringExpand);
            rt.offsetMax = new Vector2(ringExpand, ringExpand);

            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img == null) img = go.AddComponent<UnityEngine.UI.Image>();
            img.sprite = ringSprite;
            img.color = fullColor;
            img.type = UnityEngine.UI.Image.Type.Filled;
            img.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            img.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = 1f;
            img.preserveAspect = true;
            img.raycastTarget = false;

            // Behind the avatar so the centre is covered and only the radial rim reads as a border.
            go.transform.SetAsFirstSibling();

            _timers[seat] = img;
            go.SetActive(false);
        }
    }

    static Sprite _ringSprite;

    /// <summary>
    /// Procedurally generates a clean anti-aliased ring sprite. We do NOT use Unity's built-in
    /// "Knob"/"Background" UI sprites: those are editor-only "builtin extra" resources and resolve to
    /// null at runtime / in builds (verified — the radial Image would render nothing on device). A
    /// generated ring works everywhere and reads as a Callbreak-style circular border timer.
    /// </summary>
    static Sprite GetRingSprite()
    {
        if (_ringSprite != null) return _ringSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "CircularTimerRing"
        };

        float center = (size - 1) * 0.5f;
        float outer = size * 0.5f - 1f;     // outer radius (leave 1px margin)
        float inner = outer * 0.74f;        // inner radius -> ring thickness
        const float edge = 1.5f;            // anti-alias softness in pixels

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float aOuter = Mathf.Clamp01((outer - d) / edge);
                float aInner = Mathf.Clamp01((d - inner) / edge);
                float a = Mathf.Clamp01(Mathf.Min(aOuter, aInner));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);

        _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _ringSprite.name = "CircularTimerRing";
        return _ringSprite;
    }

    void DisableOldTimer()
    {
        if (oldHorizontalTimer == null && UiSafeLookup.TryGet("TimerFill", out GameObject fillGo))
            oldHorizontalTimer = fillGo;

        if (oldHorizontalTimer != null)
            oldHorizontalTimer.SetActive(false);
    }

    // ============================================================
    // DRIVEN BY TurnManager (authoritative, network-synced)
    // ============================================================

    /// <summary>
    /// Shows the radial timer on <paramref name="seatIndex"/> and smoothly tweens its fill toward
    /// <paramref name="normalized"/> (1 = full turn, 0 = time up). Hides every other seat. Called by
    /// TurnManager once per networked tick, so the fill visually drains 1 -> 0 across the turn.
    /// </summary>
    public void ShowForSeat(int seatIndex, float normalized)
    {
        if (seatIndex < 0 || seatIndex > 3) { HideAll(); return; }
        normalized = Mathf.Clamp01(normalized);

        if (_activeSeat != seatIndex)
        {
            HideAll();
            _activeSeat = seatIndex;
        }

        UnityEngine.UI.Image timer = _timers[seatIndex];
        if (timer == null) return;

        if (!timer.gameObject.activeSelf)
        {
            timer.gameObject.SetActive(true);
            timer.fillAmount = normalized;   // start exactly, no tween-in jump
        }

        float from = timer.fillAmount;
        timer.DOKill();
        DOTween.To(() => timer.fillAmount, x => timer.fillAmount = x, normalized, Mathf.Max(0.05f, tweenPerTick))
               .SetEase(Ease.Linear)
               .SetUpdate(true);
        timer.color = ColorFor(normalized);
    }

    /// <summary>Hides all radial timers and resets them to full. Call when no turn is active.</summary>
    public void HideAll()
    {
        for (int i = 0; i < 4; i++)
        {
            UnityEngine.UI.Image timer = _timers[i];
            if (timer == null) continue;
            timer.DOKill();
            timer.fillAmount = 1f;
            timer.color = fullColor;
            if (timer.gameObject.activeSelf) timer.gameObject.SetActive(false);
        }
        _activeSeat = -1;
    }

    Color ColorFor(float normalized)
        => normalized > 0.6f ? fullColor : (normalized > 0.3f ? midColor : lowColor);
}
