using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls the "JOIN TABLE" panel embedded in the Modes screen.
/// Reuses the existing Play-with-Friends join logic: takes the PIN typed here
/// and forwards it to PlayWithFriendsManager.JoinRoomWithPINText.
/// Raises only JoinTablePanel while the soft keyboard is open (Modes stays put).
/// </summary>
public class JoinTablePanelController : MonoBehaviour
{
    [Header("References")]
    public TMP_InputField pinInput;
    public Button joinButton;

    [Header("Keyboard Avoidance")]
    [Tooltip("Extra pixels above the keyboard / fallback lift when keyboard height is unknown.")]
    public float keyboardLiftFallback = 320f;
    public float keyboardLiftPadding = 48f;

    RectTransform _panelRect;
    HorizontalLayoutGroup _parentRowLayout;
    Vector2 _defaultPanelPos;
    bool _defaultPosCaptured;
    bool _shiftedForKeyboard;
    bool _pinFocused;
    bool _parentLayoutWasEnabled;
    Coroutine _capturePosRoutine;

    static readonly Color PinTextColor = new Color(0.12f, 0.06f, 0.02f, 1f);
    static readonly Color PinPlaceholderColor = new Color(0.35f, 0.22f, 0.12f, 0.9f);

    void Awake()
    {
        _panelRect = transform as RectTransform;
        if (_panelRect != null && _panelRect.parent != null)
            _parentRowLayout = _panelRect.parent.GetComponent<HorizontalLayoutGroup>();
    }

    void OnEnable()
    {
        EnsurePinInputVisible();
        if (_capturePosRoutine != null)
            StopCoroutine(_capturePosRoutine);
        _capturePosRoutine = StartCoroutine(CaptureDefaultPosAfterLayout());
    }

    void OnDisable()
    {
        if (_capturePosRoutine != null)
        {
            StopCoroutine(_capturePosRoutine);
            _capturePosRoutine = null;
        }
        RestorePanelPosition();
        _pinFocused = false;
    }

    void Start()
    {
        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(OnJoinClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
        }
    }

    void Update()
    {
        bool keyboardVisible = IsSoftKeyboardVisible();
        bool shouldLift = _pinFocused || keyboardVisible;

        if (shouldLift && !_shiftedForKeyboard)
            ShiftPanelForKeyboard(true);
        else if (!shouldLift && _shiftedForKeyboard)
            ShiftPanelForKeyboard(false);
        else if (shouldLift && _shiftedForKeyboard)
            RefreshShiftAmount();
    }

    IEnumerator CaptureDefaultPosAfterLayout()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (_panelRect != null && !_shiftedForKeyboard)
        {
            _defaultPanelPos = _panelRect.anchoredPosition;
            _defaultPosCaptured = true;
        }
        _capturePosRoutine = null;
    }

    void EnsurePinInputVisible()
    {
        if (pinInput == null) return;

        ApplyPinTextColors();

        pinInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        pinInput.keyboardType = TouchScreenKeyboardType.NumberPad;
        pinInput.characterLimit = 5;
        pinInput.shouldHideMobileInput = true;
        pinInput.caretColor = PinTextColor;
        pinInput.customCaretColor = true;
        pinInput.selectionColor = new Color(0.45f, 0.7f, 1f, 0.45f);

        pinInput.onSelect.RemoveListener(OnPinSelected);
        pinInput.onSelect.AddListener(OnPinSelected);
        pinInput.onDeselect.RemoveListener(OnPinDeselected);
        pinInput.onDeselect.AddListener(OnPinDeselected);
        pinInput.onEndEdit.RemoveListener(OnPinDeselected);
        pinInput.onEndEdit.AddListener(OnPinDeselected);
        pinInput.onValueChanged.RemoveListener(OnPinValueChanged);
        pinInput.onValueChanged.AddListener(OnPinValueChanged);
    }

    void ApplyPinTextColors()
    {
        if (pinInput == null) return;

        if (pinInput.textComponent != null)
        {
            pinInput.textComponent.color = PinTextColor;
            pinInput.textComponent.fontStyle = FontStyles.Bold;
            pinInput.textComponent.ForceMeshUpdate(true);
        }

        if (pinInput.placeholder is TMP_Text placeholder)
        {
            placeholder.color = PinPlaceholderColor;
            placeholder.ForceMeshUpdate(true);
        }
    }

    void OnPinValueChanged(string _) => ApplyPinTextColors();

    void OnPinSelected(string _)
    {
        _pinFocused = true;
        ApplyPinTextColors();
        ShiftPanelForKeyboard(true);
    }

    void OnPinDeselected(string _)
    {
        _pinFocused = false;
        if (!IsSoftKeyboardVisible())
            ShiftPanelForKeyboard(false);
    }

    static bool IsSoftKeyboardVisible() => TouchScreenKeyboard.visible;

    float ResolveLiftAmount()
    {
        float lift = keyboardLiftFallback;

#if !UNITY_EDITOR
        Rect kb = TouchScreenKeyboard.area;
        if (TouchScreenKeyboard.visible && kb.height > 10f)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            lift = (kb.height / scale) + keyboardLiftPadding;
        }
#endif
        return Mathf.Clamp(lift, 180f, 520f);
    }

    void ShiftPanelForKeyboard(bool keyboardOpen)
    {
        if (_panelRect == null) return;

        if (keyboardOpen)
        {
            if (!_shiftedForKeyboard)
            {
                // Freeze MainRow layout so Modes keeps its current X/Y while we move only JoinTable.
                _defaultPanelPos = _panelRect.anchoredPosition;
                _defaultPosCaptured = true;

                if (_parentRowLayout != null)
                {
                    _parentLayoutWasEnabled = _parentRowLayout.enabled;
                    _parentRowLayout.enabled = false;
                }
            }

            _panelRect.anchoredPosition = _defaultPanelPos + new Vector2(0f, ResolveLiftAmount());
            _shiftedForKeyboard = true;
        }
        else
        {
            RestorePanelPosition();
        }
    }

    void RefreshShiftAmount()
    {
        if (_panelRect == null || !_shiftedForKeyboard) return;
        _panelRect.anchoredPosition = _defaultPanelPos + new Vector2(0f, ResolveLiftAmount());
    }

    void RestorePanelPosition()
    {
        if (!_shiftedForKeyboard) return;

        if (_panelRect != null && _defaultPosCaptured)
            _panelRect.anchoredPosition = _defaultPanelPos;

        // Re-enable MainRow layout — Modes was never moved; JoinTable returns to layout slot.
        if (_parentRowLayout != null)
            _parentRowLayout.enabled = _parentLayoutWasEnabled;

        _shiftedForKeyboard = false;

        if (_panelRect != null && _panelRect.parent is RectTransform parent)
            LayoutRebuilder.MarkLayoutForRebuild(parent);
    }

    public void OnJoinClicked()
    {
        if (PlayWithFriendsManager.Instance == null)
        {
            Debug.LogWarning("[JoinTable] PlayWithFriendsManager.Instance missing — cannot join.");
            return;
        }

        if (PlayWithFriendsManager.Instance.IsJoinInProgress)
        {
            Debug.Log("[JoinTable] Join ignored — already joining.");
            return;
        }

        string pin = pinInput != null ? pinInput.text : null;
        PlayWithFriendsManager.Instance.JoinRoomWithPINText(pin);
    }

    public void SetJoinInteractable(bool interactable)
    {
        if (joinButton != null)
            joinButton.interactable = interactable;
    }
}
