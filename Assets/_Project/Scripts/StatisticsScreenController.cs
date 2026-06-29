using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the Statistics screen: the Vs Bots / Vs Online toggle, the period dropdown, the 10 stat
/// cards and the Total Games donut. Reads from <see cref="ProfileStatsStore"/> so the numbers shown
/// are the player's real, stored stats for the selected category. Self-resolves its child widgets by
/// name and refreshes every time the screen is shown.
/// </summary>
public class StatisticsScreenController : MonoBehaviour
{
    [Tooltip("When enabled, toggle/donut colors set in the Editor are not overwritten at runtime.")]
    [SerializeField] private bool preserveManualAppearance = true;

    static readonly Color ToggleActive = new Color(0.69f, 0.27f, 0.11f, 1f); // #B0461C
    static readonly Color ToggleInactiveText = new Color(0.54f, 0.35f, 0.17f, 1f);
    static readonly Color ToggleActiveText = Color.white;

    static readonly string[] Periods = { "All Time", "This Month", "This Week", "Today" };

    Button _btnVsBots, _btnOnline, _btnPeriod;
    SimpleDropdown _periodDropdown;
    Image _vsBotsImg, _onlineImg;
    TMP_Text _vsBotsLabel, _onlineLabel, _periodLabel;
    TMP_Text[] _cardValues = new TMP_Text[10];
    Image[] _segs = new Image[5];
    TMP_Text _donutTotal;
    TMP_Text[] _legendCounts = new TMP_Text[5];

    bool _vsBots = true;
    int _periodIndex;
    bool _resolved;

    static readonly Color[] DonutColors = new Color[5];

    void Awake() { Resolve(); }

    void OnEnable()
    {
        Resolve();
        Wire();
        if (!preserveManualAppearance)
            Refresh();
    }

    void Resolve()
    {
        if (_resolved) return;
        ColorUtility.TryParseHtmlString("#F4C20D", out DonutColors[0]);
        ColorUtility.TryParseHtmlString("#C9CDD2", out DonutColors[1]);
        ColorUtility.TryParseHtmlString("#E0A36A", out DonutColors[2]);
        ColorUtility.TryParseHtmlString("#E07B1A", out DonutColors[3]);
        ColorUtility.TryParseHtmlString("#1E140C", out DonutColors[4]);

        _btnVsBots = Find<Button>("Btn_VsBots");
        _btnOnline = Find<Button>("Btn_Online");
        _btnPeriod = Find<Button>("Dropdown_Period");
        Transform ddT = FindDeep(transform, "Dropdown_Period");
        if (ddT != null) _periodDropdown = ddT.GetComponent<SimpleDropdown>();
        _vsBotsImg = Find<Image>("Btn_VsBots");
        _onlineImg = Find<Image>("Btn_Online");
        _vsBotsLabel = FindLabel("Btn_VsBots");
        _onlineLabel = FindLabel("Btn_Online");
        _periodLabel = FindLabel("Dropdown_Period");

        for (int i = 0; i < 10; i++)
        {
            Transform card = FindDeep(transform, "Card_" + i);
            if (card != null)
            {
                Transform v = card.Find("Value");
                if (v != null) _cardValues[i] = v.GetComponent<TMP_Text>();
            }
        }

        for (int i = 0; i < 5; i++)
        {
            Transform seg = FindDeep(transform, "Seg_" + i);
            if (seg != null) _segs[i] = seg.GetComponent<Image>();
        }

        Transform total = FindDeep(transform, "Total");
        if (total != null) _donutTotal = total.GetComponent<TMP_Text>();

        string[] legend = { "Legend_First", "Legend_Second", "Legend_Third", "Legend_Fourth", "Legend_Canceled" };
        for (int i = 0; i < 5; i++)
        {
            Transform row = FindDeep(transform, legend[i]);
            if (row != null)
            {
                Transform c = row.Find("Count");
                if (c != null) _legendCounts[i] = c.GetComponent<TMP_Text>();
            }
        }
        _resolved = true;
    }

    void Wire()
    {
        if (_btnVsBots != null) { _btnVsBots.onClick.RemoveAllListeners(); _btnVsBots.onClick.AddListener(() => SetCategory(true)); }
        if (_btnOnline != null) { _btnOnline.onClick.RemoveAllListeners(); _btnOnline.onClick.AddListener(() => SetCategory(false)); }
        // The period dropdown handles its own open/close; we only react to a selection.
        if (_periodDropdown != null)
        {
            _periodDropdown.OnSelected = (idx, val) =>
            {
                _periodIndex = idx;
                if (!preserveManualAppearance)
                    Refresh();
            };
            if (!preserveManualAppearance)
                _periodDropdown.SetValueSilent(_periodIndex);
        }
        else if (_btnPeriod != null)
        {
            // Fallback if the dropdown component is missing: cycle on click.
            _btnPeriod.onClick.RemoveAllListeners();
            _btnPeriod.onClick.AddListener(CyclePeriod);
        }
    }

    void SetCategory(bool vsBots)
    {
        _vsBots = vsBots;
        if (!preserveManualAppearance)
            Refresh();
    }

    void CyclePeriod()
    {
        _periodIndex = (_periodIndex + 1) % Periods.Length;
        if (!preserveManualAppearance)
        {
            if (_periodLabel != null) _periodLabel.text = Periods[_periodIndex];
            Refresh();
        }
    }

    void Refresh()
    {
        if (!preserveManualAppearance)
        {
            if (_vsBotsImg != null) _vsBotsImg.color = _vsBots ? ToggleActive : new Color(0, 0, 0, 0);
            if (_onlineImg != null) _onlineImg.color = _vsBots ? new Color(0, 0, 0, 0) : ToggleActive;
            if (_vsBotsLabel != null) _vsBotsLabel.color = _vsBots ? ToggleActiveText : ToggleInactiveText;
            if (_onlineLabel != null) _onlineLabel.color = _vsBots ? ToggleInactiveText : ToggleActiveText;
        }

        ProfileStatsStore.CategoryStats s = ProfileStatsStore.Get(_vsBots);
        bool any = s.gamesPlayed > 0;

        // 0 Skill, 1 Lowest Score to Win, 2 High Score, 3 Best Winning Streak, 4 Win Rate,
        // 5 Worst Losing Streak, 6 Average Score, 7 Completion Rate, 8 Highest Bid, 9 Response Time
        float skill = ProfileStatsStore.Skill(s);
        Set(0, skill < 0f ? "-" : skill.ToString("0.0"));
        Set(1, s.lowestWinScore < 0f ? "-" : ((int)s.lowestWinScore).ToString());
        Set(2, any ? s.highScore.ToString("0.#") : "-");
        Set(3, any ? s.bestWinStreak.ToString() : "-");
        Set(4, any ? ProfileStatsStore.WinRate(s).ToString("0") + "%" : "-");
        Set(5, any ? s.worstLoseStreak.ToString() : "-");
        Set(6, any ? ProfileStatsStore.AverageScore(s).ToString("0.0") : "-");
        Set(7, any ? ProfileStatsStore.CompletionRate(s).ToString("0") + "%" : "-");
        Set(8, any ? s.highestBid.ToString() : "-");
        Set(9, "-"); // response time not tracked yet

        // donut
        int[] counts = { s.first, s.second, s.third, s.fourth, s.canceled };
        int total = 0; foreach (int c in counts) total += c;
        if (_donutTotal != null) _donutTotal.text = total.ToString();

        float cum = 0f;
        for (int i = 0; i < 5; i++)
        {
            cum += counts[i];
            float frac = total > 0 ? cum / total : 0f;
            if (_segs[i] != null)
            {
                if (!preserveManualAppearance)
                {
                    _segs[i].color = DonutColors[i];
                    _segs[i].fillAmount = frac;
                    _segs[i].gameObject.SetActive(total > 0);
                }
            }
            if (_legendCounts[i] != null) _legendCounts[i].text = counts[i].ToString();
        }
    }

    void Set(int index, string value)
    {
        if (index >= 0 && index < _cardValues.Length && _cardValues[index] != null)
            _cardValues[index].text = value;
    }

    // ---- helpers ----
    T Find<T>(string name) where T : Component
    {
        Transform t = FindDeep(transform, name);
        return t != null ? t.GetComponent<T>() : null;
    }
    TMP_Text FindLabel(string parentName)
    {
        Transform p = FindDeep(transform, parentName);
        if (p == null) return null;
        Transform l = p.Find("Label");
        return l != null ? l.GetComponent<TMP_Text>() : null;
    }
    static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform r = FindDeep(parent.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
