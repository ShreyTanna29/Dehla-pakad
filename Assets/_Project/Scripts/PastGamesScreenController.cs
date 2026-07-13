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

        // Phase 10: render the LOCAL PlayerPrefs history first so there is no empty flash while the
        // Firebase read (below) is in flight. When signed in, TryLoadFromFirebase re-renders with the
        // authoritative cloud list; offline or on read failure we simply keep this local view.
        Render(ProfileStatsStore.FetchAllPastGames());
        TryLoadFromFirebase();
    }

    /// <summary>Renders the given past-games list into the ScrollView (newest first), reusing the
    /// existing row template, empty-state and Vs Bots / Online split. Clears prior clones first so a
    /// re-render (e.g. after the Firebase read returns) never duplicates rows.</summary>
    void Render(System.Collections.Generic.List<ProfileStatsStore.GameRecord> history)
    {
        if (content == null || rowTemplate == null) return;

        // clear previous clones (keep the template, which is the first child and inactive)
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Transform c = content.GetChild(i);
            if (c.gameObject == rowTemplate) continue;
            Destroy(c.gameObject);
        }

        if (emptyLabel != null) emptyLabel.SetActive(history == null || history.Count == 0);
        if (history == null || history.Count == 0) return;

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

    /// <summary>
    /// Phase 10 — When a user is signed in, reads <c>users/{uid}/pastGames</c> from Firebase and, on
    /// success, re-renders the list from the cloud (newest first). On offline / read failure the
    /// already-rendered local PlayerPrefs history is kept exactly as today. Non-blocking: the local
    /// view is shown first, then replaced when this async read returns (no empty flash).
    /// </summary>
    void TryLoadFromFirebase()
    {
        Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance != null
            ? Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser
            : null;
        if (user == null || string.IsNullOrEmpty(user.UserId))
        {
            Debug.Log("[PastGames] Not signed in — showing local history only.");
            return;
        }

        string uid = user.UserId;

        Firebase.Database.DatabaseReference pastGamesRef =
            Firebase.Database.FirebaseDatabase
                // Old: "https://dehla-pakad-mindi-kot-c0645-default-rtdb.firebaseio.com/"
                // Older: "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/"
                .GetInstance("https://dehlapakad-c207c-default-rtdb.firebaseio.com/")
                .RootReference
                .Child("users").Child(uid).Child("pastGames");

        Firebase.Extensions.TaskExtension.ContinueWithOnMainThread(
            pastGamesRef.GetValueAsync(),
            task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogWarning($"[PastGames] Firebase read failed — keeping local history: {task.Exception}");
                    return;
                }

                // The screen may have been destroyed before this async callback fired.
                if (this == null || content == null || rowTemplate == null) return;

                System.Collections.Generic.List<ProfileStatsStore.GameRecord> cloud = ParseFirebaseHistory(task.Result);
                Debug.Log($"[PastGames] Loaded {cloud.Count} past game(s) from Firebase (users/{uid}/pastGames).");
                Render(cloud);
            });
    }

    /// <summary>Parses the <c>pastGames</c> snapshot into GameRecords, sorted newest-first. Each child
    /// key is a matchId; duplicates are impossible because the writer keys by matchId.</summary>
    static System.Collections.Generic.List<ProfileStatsStore.GameRecord> ParseFirebaseHistory(Firebase.Database.DataSnapshot snapshot)
    {
        var list = new System.Collections.Generic.List<ProfileStatsStore.GameRecord>();
        if (snapshot == null || !snapshot.Exists) return list;

        foreach (Firebase.Database.DataSnapshot child in snapshot.Children)
        {
            var rec = new ProfileStatsStore.GameRecord
            {
                timeTicks = ReadLong(child.Child("timeTicks")),
                vsBots = ReadBool(child.Child("vsBots")),
                rank = (int)ReadLong(child.Child("rank")),
                score = ReadFloat(child.Child("score")),
                canceled = ReadBool(child.Child("canceled"))
            };
            list.Add(rec);
        }

        list.Sort((a, b) => b.timeTicks.CompareTo(a.timeTicks));
        return list;
    }

    static long ReadLong(Firebase.Database.DataSnapshot s)
    {
        if (s == null || s.Value == null) return 0L;
        try { return System.Convert.ToInt64(s.Value); } catch { return 0L; }
    }

    static float ReadFloat(Firebase.Database.DataSnapshot s)
    {
        if (s == null || s.Value == null) return 0f;
        try { return System.Convert.ToSingle(s.Value); } catch { return 0f; }
    }

    static bool ReadBool(Firebase.Database.DataSnapshot s)
    {
        if (s == null || s.Value == null) return false;
        try { return System.Convert.ToBoolean(s.Value); } catch { return false; }
    }
}
