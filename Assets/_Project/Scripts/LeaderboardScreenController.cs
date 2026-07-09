using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the Leaderboard screen. The World / Country / Friends tabs, the Vs Bots / Online and
/// Skill / High Score toggles and the period dropdown all change which ranking is shown. Rows are a
/// fixed pool of pre-built row objects that this controller fills. The local player's row is pinned
/// at the bottom with their real value from <see cref="ProfileStatsStore"/>. Friends are sourced from
/// <see cref="PlayWithFriendsManager"/>; World/Country use representative sample data (no global
/// backend exists yet). The "Ends in" countdown ticks to the next weekly reset.
/// </summary>
public class LeaderboardScreenController : MonoBehaviour
{
    [Tooltip("When enabled, tab/toggle colors set in the Editor are not overwritten at runtime.")]
    [SerializeField] private bool preserveManualAppearance = true;

    static readonly Color TabActive = new Color(0xB0 / 255f, 0x45 / 255f, 0x1C / 255f, 1f);
    static readonly Color TabInactive = new Color(0f, 0f, 0f, 100f / 255f);
    static readonly Color LabelActive = Color.white;
    static readonly Color LabelInactive = new Color(1f, 0.93f, 0.82f, 0.62f);
    static readonly Color ToggleActive = new Color(0.69f, 0.27f, 0.11f, 1f);
    static readonly Color ToggleInactiveBg = new Color(0f, 0f, 0f, 0.18f);
    static readonly Color ToggleInactiveText = new Color(0.40f, 0.22f, 0.08f, 1f);

    class Entry { public string name; public float skill; public float high; public int region; }

    // tabs
    Button _tWorld, _tCountry, _tFriends;
    Image _tWorldBg, _tCountryBg, _tFriendsBg;
    TMP_Text _tWorldL, _tCountryL, _tFriendsL;
    // toggles
    Button _btnBots, _btnOnline, _btnSkill, _btnHigh;
    // Phase 12: optional close (X) button — auto-wired if present in the scene as "LB_Btn_Close".
    Button _btnClose;
    Image _botsBg, _onlineBg, _skillBg, _highBg;
    TMP_Text _botsL, _onlineL, _skillL, _highL;
    // dropdown
    SimpleDropdown _period;
    // rows + pinned
    readonly List<Transform> _rows = new List<Transform>();
    Transform _pinned;
    TMP_Text _endsIn;

    string _tab = "World";
    bool _vsBots = true;
    bool _bySkill = false; // false = High Score (matches reference default)
    bool _resolved, _wired;

    static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    void Awake() { Resolve(); }

    void OnEnable()
    {
        Resolve();
        Wire();
        ApplyTabStyles();
        if (!preserveManualAppearance)
            Refresh();
    }

    void Update()
    {
        if (preserveManualAppearance || _endsIn == null) return;
        TimeSpan left = NextWeeklyReset() - DateTime.UtcNow;
        if (left.Ticks < 0) left = TimeSpan.Zero;
        _endsIn.text = $"Ends in : {left.Days}d {left.Hours}h {left.Minutes}m {left.Seconds}s";
    }

    static DateTime NextWeeklyReset()
    {
        DateTime now = DateTime.UtcNow;
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return now.Date.AddDays(daysUntilMonday);
    }

    void Resolve()
    {
        if (_resolved) return;
        _tWorld = B("Tab_LB_World"); _tCountry = B("Tab_LB_Country"); _tFriends = B("Tab_LB_Friends");
        _tWorldBg = I("Tab_LB_World"); _tCountryBg = I("Tab_LB_Country"); _tFriendsBg = I("Tab_LB_Friends");
        _tWorldL = L("Tab_LB_World"); _tCountryL = L("Tab_LB_Country"); _tFriendsL = L("Tab_LB_Friends");

        _btnBots = B("LB_Btn_VsBots"); _btnOnline = B("LB_Btn_Online");
        _botsBg = I("LB_Btn_VsBots"); _onlineBg = I("LB_Btn_Online");
        _botsL = L("LB_Btn_VsBots"); _onlineL = L("LB_Btn_Online");

        _btnSkill = B("LB_Btn_Skill"); _btnHigh = B("LB_Btn_HighScore");
        _skillBg = I("LB_Btn_Skill"); _highBg = I("LB_Btn_HighScore");
        _skillL = L("LB_Btn_Skill"); _highL = L("LB_Btn_HighScore");

        _btnClose = B("LB_Btn_Close"); // Phase 12

        Transform dd = Find("LB_Dropdown_Period");
        if (dd != null) _period = dd.GetComponent<SimpleDropdown>();

        _rows.Clear();
        Transform rowsRoot = Find("LB_Rows");
        if (rowsRoot != null)
            for (int i = 0; i < rowsRoot.childCount; i++)
                if (rowsRoot.GetChild(i).name.StartsWith("Row_"))
                    _rows.Add(rowsRoot.GetChild(i));

        _pinned = Find("LB_PinnedRow");
        Transform ei = Find("LB_EndsIn");
        if (ei != null) _endsIn = ei.GetComponent<TMP_Text>();
        _resolved = true;
    }

    void Wire()
    {
        if (_wired) return;
        if (_tWorld) { _tWorld.onClick.RemoveAllListeners(); _tWorld.onClick.AddListener(() => SetTab("World")); }
        if (_tCountry) { _tCountry.onClick.RemoveAllListeners(); _tCountry.onClick.AddListener(() => SetTab("Country")); }
        if (_tFriends) { _tFriends.onClick.RemoveAllListeners(); _tFriends.onClick.AddListener(() => SetTab("Friends")); }

        if (_btnBots) { _btnBots.onClick.RemoveAllListeners(); _btnBots.onClick.AddListener(() => { _vsBots = true; if (!preserveManualAppearance) Refresh(); }); }
        if (_btnOnline) { _btnOnline.onClick.RemoveAllListeners(); _btnOnline.onClick.AddListener(() => { _vsBots = false; if (!preserveManualAppearance) Refresh(); }); }
        if (_btnSkill) { _btnSkill.onClick.RemoveAllListeners(); _btnSkill.onClick.AddListener(() => { _bySkill = true; if (!preserveManualAppearance) Refresh(); }); }
        if (_btnHigh) { _btnHigh.onClick.RemoveAllListeners(); _btnHigh.onClick.AddListener(() => { _bySkill = false; if (!preserveManualAppearance) Refresh(); }); }

        if (_period != null) _period.OnSelected = (i, v) => { if (!preserveManualAppearance) Refresh(); };

        // Phase 12: wire the leaderboard close (X) button if it exists.
        if (_btnClose) { _btnClose.onClick.RemoveAllListeners(); _btnClose.onClick.AddListener(CloseLeaderboard); }
        _wired = true;
    }

    /// <summary>Phase 12: closes the Leaderboard and returns to the Profile screen so the player is
    /// never trapped. Only affects the leaderboard tab — the profile board stays open. Falls back to
    /// simply hiding this screen if no tab controller is present.</summary>
    public void CloseLeaderboard()
    {
        var tabs = GetComponentInParent<ProfilePanelTabController>(true);
        if (tabs != null) { tabs.ShowTab(tabs.defaultTab); return; }
        gameObject.SetActive(false);
    }

    void SetTab(string tab)
    {
        _tab = tab;
        ApplyTabStyles();
        if (!preserveManualAppearance)
            Refresh();
    }

    void ApplyTabStyles()
    {
        Style(_tWorldBg, _tWorldL, _tab == "World");
        Style(_tCountryBg, _tCountryL, _tab == "Country");
        Style(_tFriendsBg, _tFriendsL, _tab == "Friends");
    }

    void Refresh()
    {
        ApplyTabStyles();
        if (!preserveManualAppearance)
        {
            StyleSeg(_botsBg, _botsL, _vsBots);
            StyleSeg(_onlineBg, _onlineL, !_vsBots);
            StyleSeg(_skillBg, _skillL, _bySkill);
            StyleSeg(_highBg, _highL, !_bySkill);
        }

        List<Entry> list = BuildEntries();
        list.Sort((a, b) => Value(b).CompareTo(Value(a)));

        for (int i = 0; i < _rows.Count; i++)
        {
            if (i < list.Count) { _rows[i].gameObject.SetActive(true); FillRow(_rows[i], i + 1, list[i]); }
            else _rows[i].gameObject.SetActive(false);
        }

        FillPinned(list);
    }

    void Style(Image bg, TMP_Text label, bool active)
    {
        if (bg) bg.color = active ? TabActive : TabInactive;
        if (label) label.color = active ? LabelActive : LabelInactive;
    }

    // Styles one cell of a two-option segmented control: filled accent when selected,
    // faint translucent when not, with a centered label for clean alignment.
    void StyleSeg(Image bg, TMP_Text label, bool active)
    {
        if (bg) bg.color = active ? ToggleActive : ToggleInactiveBg;
        if (label)
        {
            label.color = active ? LabelActive : ToggleInactiveText;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    float Value(Entry e) => _bySkill ? e.skill : e.high;

    List<Entry> BuildEntries()
    {
        var list = new List<Entry>();
        if (_tab == "Friends")
        {
            if (PlayWithFriendsManager.Instance != null && PlayWithFriendsManager.Instance.MyFriends != null)
            {
                foreach (string fid in PlayWithFriendsManager.Instance.MyFriends)
                {
                    if (string.IsNullOrEmpty(fid)) continue;
                    string nm = PlayWithFriendsManager.Instance.GetFriendDisplayName(fid);
                    list.Add(MakeEntry(string.IsNullOrEmpty(nm) ? fid : nm, 0));
                }
            }
        }
        else
        {
            string[] names = _tab == "Country"
                ? new[] { "MrxDILIP", "RaviK", "Aarav99", "neha.play", "kingS", "DILBAGH", "rohit_7", "S.Mehta" }
                : new[] { "jsisjenw", "daze.4FcLzZN", "eboe.LcTsGe", "misdaub.FSCP", "MrxDILIP", "qwz.knight", "ZenMaster", "p0kerface" };
            int region = _tab == "Country" ? 1 : 0;
            foreach (string n in names) list.Add(MakeEntry(n, region));
        }
        float scale = _vsBots ? 1f : 0.95f;
        foreach (var e in list) { e.skill *= scale; e.high *= scale; }
        return list;
    }

    Entry MakeEntry(string name, int region)
    {
        int h = Mathf.Abs((name ?? "x").GetHashCode());
        float skill = 30f + (h % 90) / 10f;          // 30.0 - 38.9
        float high = 30f + (h % 120) / 10f;          // 30.0 - 41.9
        return new Entry { name = name, skill = skill, high = high, region = region };
    }

    void FillRow(Transform row, int rank, Entry e)
    {
        SetText(row, "Rank", rank.ToString());
        SetText(row, "Name", e.name);
        SetText(row, "Points", Value(e).ToString("0.0"));
    }

    void FillPinned(List<Entry> sorted)
    {
        if (_pinned == null) return;
        string myName = PlayerPrefs.GetString("PlayerUsername", PhotonNetwork() ?? "You");
        ProfileStatsStore.CategoryStats s = ProfileStatsStore.Get(_vsBots);
        bool any = s.gamesPlayed > 0;
        float myValue = _bySkill ? ProfileStatsStore.Skill(s) : s.highScore;

        SetText(_pinned, "Name", myName);
        if (!any || myValue <= 0f)
        {
            SetText(_pinned, "Rank", "-");
            SetText(_pinned, "Points", "-");
            return;
        }

        int rank = 1;
        foreach (var e in sorted) if (Value(e) > myValue) rank++;
        SetText(_pinned, "Rank", rank.ToString());
        SetText(_pinned, "Points", myValue.ToString("0.0"));
    }

    static string PhotonNetwork()
    {
        return string.IsNullOrEmpty(Photon.Pun.PhotonNetwork.NickName) ? null : Photon.Pun.PhotonNetwork.NickName;
    }

    void SetText(Transform row, string child, string value)
    {
        Transform t = row.Find(child);
        if (t == null) t = FindDeepFrom(row, child);
        if (t != null)
        {
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = value;
        }
    }

    // ---- helpers ----
    Button B(string n) { Transform t = Find(n); return t ? t.GetComponent<Button>() : null; }
    Image I(string n) { Transform t = Find(n); return t ? t.GetComponent<Image>() : null; }
    TMP_Text L(string n) { Transform p = Find(n); if (!p) return null; Transform l = p.Find("Label"); return l ? l.GetComponent<TMP_Text>() : null; }
    Transform Find(string n) => FindDeepFrom(transform, n);
    static Transform FindDeepFrom(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform r = FindDeepFrom(parent.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
