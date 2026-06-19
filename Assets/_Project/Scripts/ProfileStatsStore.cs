using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persists the player's profile statistics, kept separately for "Vs Bots" and "Vs Online" play.
/// Stored locally as JSON in PlayerPrefs. Recorded once per finished match by
/// <see cref="ResultManager"/>, and read by the Statistics screen controller.
/// </summary>
public static class ProfileStatsStore
{
    const string Key = "ProfileStats_v1";

    [System.Serializable]
    public class CategoryStats
    {
        public int gamesPlayed;
        public int first, second, third, fourth, canceled;
        public int wins;
        public float highScore;
        public float sumScore;
        public int highestBid;
        public float lowestWinScore = -1f;
        public int bestWinStreak;
        public int worstLoseStreak;
        public int curWinStreak;
        public int curLoseStreak;
        public int completedGames;
        public int kots;
    }

    [System.Serializable]
    public class GameRecord
    {
        public long timeTicks;
        public bool vsBots;
        public int rank;      // 1..4 ; ignored when canceled
        public float score;
        public bool canceled;
    }

    [System.Serializable]
    class Root
    {
        public CategoryStats bots = new CategoryStats();
        public CategoryStats online = new CategoryStats();
        public List<GameRecord> history = new List<GameRecord>();
    }

    const int MaxHistory = 50;

    static Root _root;

    static Root Data
    {
        get { if (_root == null) Load(); return _root; }
    }

    static void Load()
    {
        string json = PlayerPrefs.GetString(Key, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { _root = JsonUtility.FromJson<Root>(json); } catch { _root = null; }
        }
        if (_root == null) _root = new Root();
        if (_root.bots == null) _root.bots = new CategoryStats();
        if (_root.online == null) _root.online = new CategoryStats();
        if (_root.history == null) _root.history = new List<GameRecord>();
    }

    /// <summary>All recorded games, newest first (capped at the most recent <see cref="MaxHistory"/>).</summary>
    public static List<GameRecord> History => Data.history;

    static void AddHistory(GameRecord rec)
    {
        Data.history.Insert(0, rec);
        if (Data.history.Count > MaxHistory)
            Data.history.RemoveRange(MaxHistory, Data.history.Count - MaxHistory);
    }

    static void Save()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
        PlayerPrefs.Save();
    }

    public static CategoryStats Get(bool vsBots) => vsBots ? Data.bots : Data.online;

    /// <summary>Records a finished match for the local player.</summary>
    public static void RecordCompletedGame(bool vsBots, int rank, float score, int bid, bool kot)
    {
        CategoryStats s = Get(vsBots);
        s.gamesPlayed++;
        s.completedGames++;

        switch (rank)
        {
            case 1: s.first++; break;
            case 2: s.second++; break;
            case 3: s.third++; break;
            default: s.fourth++; break;
        }

        bool won = rank == 1;
        if (won)
        {
            s.wins++;
            s.curWinStreak++;
            s.curLoseStreak = 0;
            if (s.curWinStreak > s.bestWinStreak) s.bestWinStreak = s.curWinStreak;
            if (s.lowestWinScore < 0f || score < s.lowestWinScore) s.lowestWinScore = score;
        }
        else
        {
            s.curLoseStreak++;
            s.curWinStreak = 0;
            if (s.curLoseStreak > s.worstLoseStreak) s.worstLoseStreak = s.curLoseStreak;
        }

        if (score > s.highScore) s.highScore = score;
        s.sumScore += score;
        if (bid > s.highestBid) s.highestBid = bid;
        if (kot) s.kots++;

        AddHistory(new GameRecord
        {
            timeTicks = System.DateTime.UtcNow.Ticks,
            vsBots = vsBots,
            rank = rank,
            score = score,
            canceled = false
        });

        Save();
    }

    /// <summary>Records a match the player abandoned / that was cancelled.</summary>
    public static void RecordCanceledGame(bool vsBots)
    {
        CategoryStats s = Get(vsBots);
        s.gamesPlayed++;
        s.canceled++;
        s.curWinStreak = 0;

        AddHistory(new GameRecord
        {
            timeTicks = System.DateTime.UtcNow.Ticks,
            vsBots = vsBots,
            rank = 0,
            score = 0,
            canceled = true
        });

        Save();
    }

    // ---- derived display helpers ----
    public static int TotalGames(CategoryStats s) => s.first + s.second + s.third + s.fourth + s.canceled;
    public static float WinRate(CategoryStats s) => s.gamesPlayed > 0 ? (s.wins * 100f / s.gamesPlayed) : 0f;
    public static float AverageScore(CategoryStats s) => s.gamesPlayed > 0 ? (s.sumScore / s.gamesPlayed) : 0f;
    public static float CompletionRate(CategoryStats s) => s.gamesPlayed > 0 ? (s.completedGames * 100f / s.gamesPlayed) : 0f;

    public static float Skill(CategoryStats s)
    {
        if (s.completedGames <= 0) return -1f;
        float wr = WinRate(s);
        return Mathf.Round((wr * 0.1f + AverageScore(s) * 0.05f) * 10f) / 10f;
    }

#if UNITY_EDITOR
    public static void DevReset() { _root = new Root(); Save(); }
    public static void DevSeedSample()
    {
        _root = new Root();
        RecordCompletedGame(true, 1, 120, 8, true);
        RecordCompletedGame(true, 1, 90, 6, false);
        RecordCompletedGame(true, 3, 40, 4, false);
        RecordCompletedGame(true, 2, 70, 5, false);
        RecordCompletedGame(false, 2, 80, 7, false);
        RecordCompletedGame(false, 4, 30, 3, false);
        RecordCompletedGame(false, 1, 110, 9, true);
    }
#endif
}
