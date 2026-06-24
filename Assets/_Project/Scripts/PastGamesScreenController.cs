using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Populates the Past Games screen with the player's match history from <see cref="ProfileStatsStore"/>,
/// newest first. Clones <see cref="rowTemplate"/> (a pre-styled hidden row) into <see cref="content"/>
/// inside a ScrollView, filling date, mode (Vs Bots / Online), result and score. Shows
/// <see cref="emptyLabel"/> when there is no history. Rebuilds every time the screen is shown.
/// </summary>
public class PastGamesScreenController : MonoBehaviour
{
    public RectTransform content;
    public GameObject rowTemplate;
    public GameObject emptyLabel;

    static readonly Color Won = new Color(0.10f, 0.70f, 0.42f, 1f);
    static readonly Color Lost = new Color(0.87f, 0.30f, 0.20f, 1f);
    static readonly Color Mid = new Color(0.90f, 0.66f, 0.30f, 1f);
    static readonly Color Cancel = new Color(0.6f, 0.6f, 0.6f, 1f);

    void OnEnable() { Rebuild(); }

    void Rebuild()
    {
        if (content == null || rowTemplate == null) return;

        // clear previous clones (keep the template, which is the first child and inactive)
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Transform c = content.GetChild(i);
            if (c.gameObject == rowTemplate) continue;
            Destroy(c.gameObject);
        }

        var history = ProfileStatsStore.History;
        if (emptyLabel != null) emptyLabel.SetActive(history == null || history.Count == 0);
        if (history == null) return;

        rowTemplate.SetActive(false);

        for (int i = 0; i < history.Count; i++)
        {
            ProfileStatsStore.GameRecord rec = history[i];
            GameObject row = Instantiate(rowTemplate, content);
            row.name = "PG_Row_" + i;
            row.SetActive(true);

            SetText(row.transform, "DateText", FormatDate(rec.timeTicks));
            SetText(row.transform, "ModeText", rec.vsBots ? "Vs Bots" : "Online");

            string resultText;
            Color resultColor;
            if (rec.canceled) { resultText = "Canceled"; resultColor = Cancel; }
            else if (rec.rank == 1) { resultText = "1st • Won"; resultColor = Won; }
            else if (rec.rank == 2) { resultText = "2nd"; resultColor = Mid; }
            else if (rec.rank == 3) { resultText = "3rd"; resultColor = Mid; }
            else { resultText = "4th • Lost"; resultColor = Lost; }
            SetTextColor(row.transform, "ResultText", resultText, resultColor);

            SetText(row.transform, "ScoreText", rec.canceled ? "-" : rec.score.ToString("0"));

            // alternating row tint
            var img = row.GetComponent<Image>();
            if (img != null)
            {
                Color a = new Color(1f, 1f, 1f, 0.06f);
                Color b = new Color(1f, 1f, 1f, 0.12f);
                img.color = (i % 2 == 0) ? b : a;
            }
        }

        if (content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }

    static string FormatDate(long ticks)
    {
        try
        {
            DateTime dt = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            DateTime now = DateTime.Now;
            if (dt.Date == now.Date) return "Today " + dt.ToString("HH:mm");
            if (dt.Date == now.Date.AddDays(-1)) return "Yesterday " + dt.ToString("HH:mm");
            return dt.ToString("dd MMM, HH:mm");
        }
        catch { return ""; }
    }

    void SetText(Transform row, string child, string value)
    {
        Transform t = FindDeep(row, child);
        if (t != null)
        {
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = value;
        }
    }

    void SetTextColor(Transform row, string child, string value, Color color)
    {
        Transform t = FindDeep(row, child);
        if (t != null)
        {
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null) { tmp.text = value; tmp.color = color; }
        }
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
