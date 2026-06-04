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
