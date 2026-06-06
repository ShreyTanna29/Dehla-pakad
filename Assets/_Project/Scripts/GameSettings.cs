using UnityEngine;

public enum GameModeType
{
    TrumpSpades,
    ThirteenthCardTrump,
    Cut1Trump,
    Cut2Trump
}

public enum MatchType
{
    OfflineBots,
    OnlinePhoton,
    PlayWithFriends
}

public class GameSettings : MonoBehaviour
{
    public static GameSettings Instance;

    public GameModeType currentMode = GameModeType.TrumpSpades;
    public MatchType currentMatchType = MatchType.OnlinePhoton;
    public int taashCategory = 1; // 1 or 2 Taash

    /// <summary>Cut-2-trump mode (Double Sar) — used by bot AI aggression.</summary>
    public bool IsDoubleSarMode => currentMode == GameModeType.Cut2Trump;

    public static bool IsDoubleSarActive =>
        Instance != null && Instance.IsDoubleSarMode;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
