using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Drives the Home Settings panel: open/close, Language dropdown, Appearance (Modern/Classic),
/// Sound and Music toggles, Push-Notifications switch, Rate Us / Bug Report, and the bottom row
/// (Join WhatsApp, Legal Info, Game Info, Default Settings, Exit Game) with an exit confirmation.
/// All widgets are resolved by name and wired on enable, and reflect <see cref="SettingsService"/>.
/// URLs are public so they can be customised in the inspector.
/// </summary>
public class HomeSettingsController : MonoBehaviour
{
    [Header("Links (customise these)")]
    public string storeUrl = "https://play.google.com/store/apps/details?id=com.dehlapakad.game";
    public string bugReportUrl = "mailto:support@dehlapakad.com?subject=Bug%20Report";
    public string whatsappUrl = "https://chat.whatsapp.com/";
    public string legalUrl = "https://dehlapakad.com/privacy";

    static readonly Color ActiveTint = Color.white;
    static readonly Color InactiveTint = new Color(1f, 1f, 1f, 0.35f);
    static readonly Color OptActive = Color.white;
    static readonly Color OptInactive = new Color(1f, 0.93f, 0.82f, 0.55f);
    static readonly Color PushOnTrack = new Color(0.69f, 0.27f, 0.11f, 1f);
    static readonly Color PushOffTrack = new Color(0.35f, 0.22f, 0.12f, 1f);

    CanvasGroup _cg;
    Graphic _soundIcon, _musicIcon;
    Image _pushTrack;
    RectTransform _pushKnob, _board;
    TMP_Text _appModernL, _appClassicL, _versionText;
    SimpleDropdown _langDropdown;
    GameObject _exitConfirm;
    bool _wired;

    void OnEnable()
    {
        Resolve();
        Wire();
        RefreshAll();
        Animate();
    }

    void Resolve()
    {
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        _board = Find("Board") as RectTransform;
        _soundIcon = Img("Sound_Icon");
        _musicIcon = Img("Music_Icon");
        _pushTrack = Img("Push_Track");
        _pushKnob = Find("Push_Knob") as RectTransform;
        _appModernL = Label("Btn_AppModern");
        _appClassicL = Label("Btn_AppClassic");
        var vt = Find("VersionText");
        if (vt != null) _versionText = vt.GetComponent<TMP_Text>();
        var dd = Find("Dropdown_Language");
        if (dd != null) _langDropdown = dd.GetComponent<SimpleDropdown>();
        var ec = Find("Panel_ExitConfirm");
        _exitConfirm = ec != null ? ec.gameObject : null;
    }

    void Wire()
    {
        if (_wired) return;

        WireBtn("Btn_CloseSettings", Close);
        WireBtn("Btn_Deck", () => ProfileToast.Show(transform, "Card themes — open Inventory to change your deck."));

        // Appearance
        WireBtn("Btn_AppModern", () => { SettingsService.AppearanceIndex = 0; RefreshAll(); });
        WireBtn("Btn_AppClassic", () => { SettingsService.AppearanceIndex = 1; RefreshAll(); });

        // Sound / Music
        WireBtn("Btn_Sound", () => { SettingsService.SoundOn = !SettingsService.SoundOn; RefreshAll(); });
        WireBtn("Btn_Music", () => { SettingsService.MusicOn = !SettingsService.MusicOn; RefreshAll(); });

        // Push notifications
        WireBtn("Btn_Push", () => { SettingsService.PushNotifications = !SettingsService.PushNotifications; RefreshAll(); });

        // Rate / Bug
        WireBtn("Btn_Rate", () => { OpenUrl(storeUrl); ProfileToast.Show(transform, "Opening store page…"); });
        WireBtn("Btn_Bug", () => { OpenUrl(bugReportUrl); ProfileToast.Show(transform, "Opening bug report…"); });

        // Bottom row
        WireBtn("Btn_WhatsApp", () => { OpenUrl(whatsappUrl); ProfileToast.Show(transform, "Opening WhatsApp…"); });
        WireBtn("Btn_Legal", () => { OpenUrl(legalUrl); ProfileToast.Show(transform, "Opening legal info…"); });
        WireBtn("Btn_GameInfo", () => ProfileToast.Show(transform, "Dehla Pakad  •  v" + Application.version + "  •  Unity " + Application.unityVersion));
        WireBtn("Btn_Default", () => { SettingsService.ResetToDefaults(); if (_langDropdown != null) _langDropdown.SetValueSilent(SettingsService.LanguageIndex); RefreshAll(); ProfileToast.Show(transform, "Settings restored to defaults."); });
        WireBtn("Btn_Exit", ShowExitConfirm);

        // Exit confirm
        WireBtn("Btn_ExitYes", DoExit);
        WireBtn("Btn_ExitNo", HideExitConfirm);

        // Language dropdown
        if (_langDropdown != null)
        {
            _langDropdown.SetValueSilent(SettingsService.LanguageIndex);
            _langDropdown.OnSelected = (idx, val) => { SettingsService.LanguageIndex = idx; };
        }

        if (_exitConfirm != null) _exitConfirm.SetActive(false);
        _wired = true;
    }

    void RefreshAll()
    {
        // Sound / Music icon highlight
        if (_soundIcon != null) _soundIcon.color = SettingsService.SoundOn ? ActiveTint : InactiveTint;
        if (_musicIcon != null) _musicIcon.color = SettingsService.MusicOn ? ActiveTint : InactiveTint;

        // Appearance underline/active
        if (_appModernL != null) _appModernL.color = SettingsService.AppearanceIndex == 0 ? OptActive : OptInactive;
        if (_appClassicL != null) _appClassicL.color = SettingsService.AppearanceIndex == 1 ? OptActive : OptInactive;

        // Push switch
        bool push = SettingsService.PushNotifications;
        if (_pushTrack != null) _pushTrack.color = push ? PushOnTrack : PushOffTrack;
        if (_pushKnob != null)
        {
            _pushKnob.DOKill();
            float x = push ? 34f : -34f;
            _pushKnob.anchoredPosition = new Vector2(x, _pushKnob.anchoredPosition.y);
        }

        if (_versionText != null) _versionText.text = "Dehla Pakad v" + Application.version;
        if (_langDropdown != null && _langDropdown.label != null)
            _langDropdown.label.text = SettingsService.LanguageName;
    }

    void Animate()
    {
        transform.SetAsLastSibling();
        if (_cg != null)
        {
            _cg.DOKill();
            _cg.alpha = 0f; _cg.interactable = true; _cg.blocksRaycasts = true;
            _cg.DOFade(1f, 0.25f).SetUpdate(true);
        }
        if (_board != null)
        {
            _board.DOKill();
            _board.localScale = Vector3.one * 0.92f;
            _board.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }
    }

    public void Open()
    {
        gameObject.SetActive(true);
        OnEnable();
    }

    public void Close()
    {
        if (_cg != null)
        {
            _cg.DOKill();
            _cg.DOFade(0f, 0.2f).SetUpdate(true).OnComplete(() => gameObject.SetActive(false));
            _cg.interactable = false; _cg.blocksRaycasts = false;
        }
        else gameObject.SetActive(false);
    }

    void ShowExitConfirm()
    {
        if (_exitConfirm == null) { DoExit(); return; }
        _exitConfirm.SetActive(true);
        _exitConfirm.transform.SetAsLastSibling();
        _exitConfirm.transform.localScale = Vector3.one * 0.85f;
        _exitConfirm.transform.DOKill();
        _exitConfirm.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);
    }

    void HideExitConfirm()
    {
        if (_exitConfirm == null) return;
        _exitConfirm.transform.DOKill();
        _exitConfirm.transform.DOScale(0.85f, 0.18f).SetEase(Ease.InBack).SetUpdate(true)
            .OnComplete(() => { if (_exitConfirm != null) _exitConfirm.SetActive(false); });
    }

    void DoExit()
    {
        Debug.Log("[Settings] Exit Game requested.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Application.OpenURL(url); }
        catch (System.Exception e) { Debug.LogWarning("[Settings] OpenURL failed: " + e.Message); }
    }

    // ---- helpers ----
    void WireBtn(string name, UnityEngine.Events.UnityAction call)
    {
        var t = Find(name);
        if (t == null) return;
        var b = t.GetComponent<Button>();
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(call);
    }
    Image Img(string name) { var t = Find(name); return t ? t.GetComponent<Image>() : null; }
    Graphic Gfx(string name) { var t = Find(name); return t ? t.GetComponent<Graphic>() : null; }
    TMP_Text Label(string parent) { var p = Find(parent); if (!p) return null; var l = p.Find("Label"); return l ? l.GetComponent<TMP_Text>() : null; }
    Transform Find(string name) => FindDeep(transform, name);
    static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var r = FindDeep(parent.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
