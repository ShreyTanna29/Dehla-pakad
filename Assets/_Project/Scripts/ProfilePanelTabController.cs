using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Drives the left-sidebar navigation of the Player Profile panel. Each <see cref="Tab"/> pairs a
/// nav button with the content screen it shows. Clicking a nav button activates that screen and
/// deactivates the others, and updates the active/inactive highlight. Self-wires on enable so it
/// works every time the panel is opened.
/// </summary>
public class ProfilePanelTabController : MonoBehaviour
{
    [System.Serializable]
    public class Tab
    {
        public string id;
        public UnityEngine.UI.Button navButton;
        public GameObject screen;
        public TMP_Text label;
        public UnityEngine.UI.Image navBg;
    }

    public List<Tab> tabs = new List<Tab>();
    public string defaultTab = "Profile";

    [Header("Appearance")]
    [Tooltip("When enabled, nav sprites and label colors set in the Editor are never overwritten at runtime.")]
    [SerializeField] private bool preserveManualNavAppearance = true;

    bool _wired;
    string _current;
    static TMP_FontAsset _regularFontCache;

    void OnEnable()
    {
        Wire();
        ShowTab(string.IsNullOrEmpty(_current) ? defaultTab : _current);
        ApplySelectiveBoldText();
    }

    void Wire()
    {
        if (_wired) return;
        foreach (Tab t in tabs)
        {
            if (t == null || t.navButton == null) continue;
            string id = t.id;
            t.navButton.onClick.RemoveAllListeners();
            t.navButton.onClick.AddListener(() => ShowTab(id));
        }
        _wired = true;
    }

    public void ShowTab(string id)
    {
        _current = id;
        foreach (Tab t in tabs)
        {
            if (t == null) continue;
            bool active = t.id == id;
            if (t.screen != null) t.screen.SetActive(active);

            if (t.label != null)
                t.label.color = active
                    ? Color.white
                    : Color.black;

            // Keep the authored button/background look when requested, but still enforce the
            // selected-tab label color so every active tab matches the intended white state.
            if (preserveManualNavAppearance)
                continue;
        }
    }

    /// <summary>
    /// Only Side_Name, Synced-with Title, and Email stay bold. Every other profile TMP is forced
    /// back to normal weight (and off a Bold font asset when one was assigned).
    /// </summary>
    void ApplySelectiveBoldText()
    {
        TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
        TMP_FontAsset regular = ResolveRegularFont(labels);

        for (int i = 0; i < labels.Length; i++)
        {
            TMP_Text tmp = labels[i];
            if (tmp == null) continue;

            if (ShouldStayBold(tmp))
            {
                tmp.fontStyle = FontStyles.Bold;
                continue;
            }

            tmp.fontStyle = FontStyles.Normal;
            if (regular != null && IsBoldFontAsset(tmp.font))
                tmp.font = regular;
        }
    }

    static bool ShouldStayBold(TMP_Text tmp)
    {
        string n = tmp.gameObject.name;
        if (n == "Side_Name" || n == "Email")
            return true;

        // "Synced with" label under SyncedWith
        if (n == "Title" && tmp.transform.parent != null && tmp.transform.parent.name == "SyncedWith")
            return true;

        return false;
    }

    static bool IsBoldFontAsset(TMP_FontAsset font)
    {
        if (font == null || string.IsNullOrEmpty(font.name)) return false;
        return font.name.IndexOf("Bold", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static TMP_FontAsset ResolveRegularFont(TMP_Text[] labels)
    {
        if (_regularFontCache != null) return _regularFontCache;

        _regularFontCache = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (_regularFontCache != null) return _regularFontCache;

        // Fallback: any authored non-bold body font on this panel (skip display fonts).
        for (int i = 0; i < labels.Length; i++)
        {
            TMP_Text tmp = labels[i];
            if (tmp == null || tmp.font == null) continue;
            if (ShouldStayBold(tmp)) continue;
            if (IsBoldFontAsset(tmp.font)) continue;
            if (tmp.font.name.IndexOf("BlackOps", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            _regularFontCache = tmp.font;
            return _regularFontCache;
        }

        return null;
    }
}
