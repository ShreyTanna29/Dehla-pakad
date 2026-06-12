using UnityEngine;

public enum GameModeType
{
    TrumpSpades,
    ThirteenthCardTrump,
    Cut1Trump,
    Cut2Trump,
    HiddenTrump
}

public enum SarModeType
{
    OneSar,
    TwoSar
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

    [Header("Game Modes")]
    public GameModeType currentMode = GameModeType.TrumpSpades;
    public SarModeType currentSarMode = SarModeType.OneSar;
    public MatchType currentMatchType = MatchType.OnlinePhoton;

    [Header("Deck Settings")]
    public int taashCategory = 1;

    public static bool IsDoubleSarActive =>
        Instance != null && Instance.currentSarMode == SarModeType.TwoSar;

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
