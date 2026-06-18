using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SarModeSelector : MonoBehaviour
{
    public static SarModeSelector Instance;

    public Button oneSarButton;
    public Button twoSarButton;

    // Unselected buttons tint (hex: 6D360D)
    public Color normalColor = ModeManager.ModeUnselectedColor;
    public Color hoverColor = new Color(0.9f, 0.9f, 0.9f);
    // Selected buttons tint (hex: CBA06B)
    public Color selectedColor = ModeManager.ModeSelectedColor;

    Image _oneSarImage;
    Image _twoSarImage;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        ResolveButtons();
        WireClickListeners();
        SetupButtonHover(oneSarButton, _oneSarImage, 1);
        SetupButtonHover(twoSarButton, _twoSarImage, 2);
        UpdateButtonVisuals();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void ResolveButtons()
    {
        if (oneSarButton == null && UiSafeLookup.TryGet("Button_Play1Sar", out GameObject oneGo))
            oneSarButton = oneGo.GetComponent<Button>();

        if (twoSarButton == null && UiSafeLookup.TryGet("Button_Play2Sar", out GameObject twoGo))
            twoSarButton = twoGo.GetComponent<Button>();

        if (oneSarButton != null)
            _oneSarImage = oneSarButton.GetComponent<Image>();

        if (twoSarButton != null)
            _twoSarImage = twoSarButton.GetComponent<Image>();
    }

    void WireClickListeners()
    {
        if (oneSarButton != null)
        {
            oneSarButton.onClick.RemoveListener(SelectOneSar);
            oneSarButton.onClick.AddListener(SelectOneSar);
        }

        if (twoSarButton != null)
        {
            twoSarButton.onClick.RemoveListener(SelectTwoSar);
            twoSarButton.onClick.AddListener(SelectTwoSar);
        }
    }

    void SetupButtonHover(Button btn, Image img, int mode)
    {
        if (btn == null || img == null) return;

        ButtonEventHelper helper = btn.gameObject.GetComponent<ButtonEventHelper>();
        if (helper == null)
            helper = btn.gameObject.AddComponent<ButtonEventHelper>();

        helper.OnPointerEnterAction = () =>
        {
            // Dark/light hover tint removed:
            // Keep color driven ONLY by selection state (UpdateButtonVisuals()).
            if (!btn.interactable) return;
            if (IsModeSelected(mode))
                img.color = selectedColor;
        };

        helper.OnPointerExitAction = () => UpdateButtonVisuals();
    }

    public void SelectOneSar()
    {
        if (ModeManager.Instance != null)
            ModeManager.Instance.OnClick_SarMode(1);
        else if (GameSettings.Instance != null)
            GameSettings.Instance.currentSarMode = SarModeType.OneSar;

        UpdateButtonVisuals();
    }

    public void SelectTwoSar()
    {
        if (ModeManager.Instance != null)
            ModeManager.Instance.OnClick_SarMode(2);
        else if (GameSettings.Instance != null)
            GameSettings.Instance.currentSarMode = SarModeType.TwoSar;

        UpdateButtonVisuals();
    }

    bool IsModeSelected(int mode)
    {
        if (ModeManager.Instance != null)
            return ModeManager.Instance.currentSarMode == mode;

        if (GameSettings.Instance == null) return false;
        return mode == 1
            ? GameSettings.Instance.currentSarMode == SarModeType.OneSar
            : GameSettings.Instance.currentSarMode == SarModeType.TwoSar;
    }

    public void UpdateButtonVisuals()
    {
        ResolveButtons();

        if (_oneSarImage != null)
        {
            _oneSarImage.color = IsModeSelected(1) ? selectedColor : normalColor;
            if (oneSarButton != null)
                oneSarButton.transition = Selectable.Transition.None;
        }

        if (_twoSarImage != null)
        {
            _twoSarImage.color = IsModeSelected(2) ? selectedColor : normalColor;
            if (twoSarButton != null)
                twoSarButton.transition = Selectable.Transition.None;
        }
    }
}
