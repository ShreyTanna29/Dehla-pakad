using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

/// <summary>
/// A two-position sliding switch used in the Modes panel for the Deck (ताश) and
/// Hands (सर) options. A wooden knob slides between the "1" position (left) and the
/// "2" position (right). Selecting a value reports it to <see cref="ModeManager"/>.
/// </summary>
public class SlidingModeToggle : MonoBehaviour
{
    public enum ToggleKind { Deck, Hands }

    [Header("Config")]
    public ToggleKind kind = ToggleKind.Deck;

    [Header("References")]
    public Button hitButton;        // Button on the track that toggles the value.
    public RectTransform knob;       // The sliding wooden knob.
    public TMP_Text label1;          // The "1" number label.
    public TMP_Text label2;          // The "2" number label.

    [Header("Layout")]
    public float knobLeftX = -70f;
    public float knobRightX = 70f;

    [Header("Animation")]
    public float animDuration = 0.22f;

    [Header("Colors")]
    public Color activeNumberColor = Color.white;
    public Color inactiveNumberColor = new Color(0.42f, 0.30f, 0.18f, 1f);

    int _currentValue = 1;
    bool _initialized;

    public int CurrentValue => _currentValue;

    void Awake()
    {
        if (hitButton != null)
        {
            hitButton.onClick.RemoveListener(OnToggleClicked);
            hitButton.onClick.AddListener(OnToggleClicked);
        }
    }

    void Start()
    {
        int initial = ReadModeValue();
        SetValue(initial, animate: false, notify: false);
        _initialized = true;
    }

    int ReadModeValue()
    {
        if (ModeManager.Instance == null) return _currentValue;
        return kind == ToggleKind.Deck
            ? ModeManager.Instance.currentTrickMode
            : ModeManager.Instance.currentSarMode;
    }

    void OnToggleClicked()
    {
        int next = _currentValue == 1 ? 2 : 1;
        SetValue(next, animate: true, notify: true);
    }

    /// <summary>
    /// Sets the toggle value, optionally animating the knob and notifying ModeManager.
    /// </summary>
    public void SetValue(int value, bool animate, bool notify)
    {
        value = Mathf.Clamp(value, 1, 2);
        bool changed = value != _currentValue || !_initialized;
        _currentValue = value;

        float targetX = value == 1 ? knobLeftX : knobRightX;

        if (knob != null && (changed || !animate))
        {
            knob.DOKill();
            if (animate)
                knob.DOAnchorPosX(targetX, animDuration).SetEase(Ease.OutBack);
            else
            {
                Vector2 p = knob.anchoredPosition;
                p.x = targetX;
                knob.anchoredPosition = p;
            }
        }

        if (label1 != null) label1.color = value == 1 ? activeNumberColor : inactiveNumberColor;
        if (label2 != null) label2.color = value == 2 ? activeNumberColor : inactiveNumberColor;

        if (notify && ModeManager.Instance != null)
        {
            if (kind == ToggleKind.Deck)
                ModeManager.Instance.OnClick_TrickMode(value);
            else
                ModeManager.Instance.OnClick_SarMode(value);
        }
    }
}
