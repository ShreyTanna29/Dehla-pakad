using System.Collections.Generic;

/// <summary>
/// 1 Taash (13 cards) vs 2 Taash (26 cards, double deck, duplicate-card priority).
/// </summary>
public static class TaashRules
{
    static readonly int[] OneTaashDealBatches = { 5, 4, 4 };
    static readonly int[] TwoTaashDealBatches = { 10, 8, 8 };

    public static bool IsTwoTaashMode =>
        GameSettings.Instance != null && GameSettings.Instance.taashCategory == 2;

    public static int CardsPerPlayer => IsTwoTaashMode ? 26 : 13;

    public static int TricksPerGame => CardsPerPlayer;

    public static int[] GetDealAnimationBatches() =>
        IsTwoTaashMode ? TwoTaashDealBatches : OneTaashDealBatches;

    public static int GetDeckCardCount() => IsTwoTaashMode ? 104 : 52;

    /// <summary>
    /// Standard trick winner + 2 Taash: identical suit/rank played later wins.
    /// </summary>
    public static PlayerHand.TrickCard DetermineTrickWinner(List<PlayerHand.TrickCard> trick, CardSuit trumpSuit)
    {
        if (trick == null || trick.Count == 0)
            return default;

        int winnerIdx = 0;
        CardSuit led = trick[0].suit;

        for (int i = 1; i < trick.Count; i++)
        {
            PlayerHand.TrickCard winner = trick[winnerIdx];
            PlayerHand.TrickCard challenger = trick[i];

            bool challengerTrump = challenger.suit == trumpSuit;
            bool winnerTrump = winner.suit == trumpSuit;

            if (challengerTrump && !winnerTrump)
                winnerIdx = i;
            else if (challengerTrump && winnerTrump && challenger.rankValue > winner.rankValue)
                winnerIdx = i;
            else if (!challengerTrump && !winnerTrump && challenger.suit == led && challenger.rankValue > winner.rankValue)
                winnerIdx = i;
            else if (IsTwoTaashMode &&
                     challenger.suit == winner.suit &&
                     challenger.rankValue == winner.rankValue)
                winnerIdx = i;
        }

        return trick[winnerIdx];
    }
}
