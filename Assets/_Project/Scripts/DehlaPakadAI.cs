using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class DehlaPakadAI : MonoBehaviour
{
    public static DehlaPakadAI Instance;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    static int RankValue(CardData card) => (int)card.cardRank;

    public CardData ThinkAndSelectCard(List<CardData> botHand, List<PlayerHand.TrickCard> currentTrick, CardSuit trumpSuit, bool isTrumpRevealed, int botActorNumber)
    {
        if (botHand == null || botHand.Count == 0) return new CardData();

        List<CardData> validCards = PlayerHand.GetValidCards(botHand, currentTrick);
        if (validCards.Count == 0)
        {
            Debug.LogError($"[AI] No valid cards found for bot {botActorNumber}! Using first available.");
            return botHand[0];
        }

        if (currentTrick == null || currentTrick.Count == 0)
            return ChooseLeadCard(validCards);

        bool twoTaash = IsActiveTwoTaash(botHand);
        bool trickHasDehla = TrickContainsDehla(currentTrick);
        bool isCutMode = GameSettings.Instance != null &&
            (GameSettings.Instance.currentMode == GameModeType.Cut1Trump ||
             GameSettings.Instance.currentMode == GameModeType.Cut2Trump);

        CardSuit ledSuit = currentTrick[0].suit;
        bool mustFollowSuit = validCards.All(c => c.cardSuit == ledSuit);

        if (isCutMode && !mustFollowSuit)
            return ChooseCutWhenVoid(validCards, trickHasDehla);

        List<CardData> winningPlays = GetWinningPlays(validCards, currentTrick, trumpSuit, botActorNumber, twoTaash);

        if (winningPlays.Count > 0)
            return PickCheapestWinningCard(winningPlays);

        if (trickHasDehla && !mustFollowSuit)
        {
            List<CardData> trumpPlays = validCards.Where(c => c.cardSuit == trumpSuit).ToList();
            List<CardData> trumpWinners = GetWinningPlays(trumpPlays, currentTrick, trumpSuit, botActorNumber, twoTaash);
            if (trumpWinners.Count > 0)
                return PickCheapestWinningCard(trumpWinners);
        }

        return ChooseLowestDump(validCards, currentTrick, trumpSuit, botActorNumber, mustFollowSuit, twoTaash);
    }

    static CardData ChooseLeadCard(List<CardData> validCards)
    {
        var suitGroups = validCards
            .GroupBy(c => c.cardSuit)
            .Select(g => new
            {
                Suit = g.Key,
                Count = g.Count(),
                Strength = g.Sum(c => RankValue(c)),
                Cards = g.OrderByDescending(c => RankValue(c)).ToList()
            })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Strength)
            .ToList();

        var best = suitGroups[0];
        List<CardData> suitCards = best.Cards;

        if (suitCards.Count >= 2 && suitCards[0].cardRank == CardRank.Ace)
            return suitCards[1];

        return suitCards[0];
    }

    static CardData ChooseCutWhenVoid(List<CardData> validCards, bool trickHasDehla)
    {
        var suitGroups = validCards
            .GroupBy(c => c.cardSuit)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(c => RankValue(c)))
            .ToList();

        var bestSuit = suitGroups.First().Key;
        List<CardData> suitCards = validCards.Where(c => c.cardSuit == bestSuit).ToList();

        if (trickHasDehla)
            return suitCards.OrderByDescending(c => RankValue(c)).First();

        return suitCards.OrderBy(c => RankValue(c)).First();
    }

    /// <summary>
    /// All legal plays that win the trick (includes 2 Taash duplicate-over-current-winner).
    /// </summary>
    static List<CardData> GetWinningPlays(List<CardData> candidates, List<PlayerHand.TrickCard> trick, CardSuit trumpSuit, int botActorNumber, bool twoTaash)
    {
        var winners = new List<CardData>();
        foreach (CardData card in candidates)
        {
            if (WouldBotWinTrick(card, trick, trumpSuit, botActorNumber, twoTaash))
                winners.Add(card);
        }
        return winners;
    }

    /// <summary>
    /// Whether this card wins if the bot plays it next. Uses the same rules as gameplay,
    /// including 2 Taash: a later identical suit+rank beats the current trick winner.
    /// </summary>
    static bool WouldBotWinTrick(CardData card, List<PlayerHand.TrickCard> trick, CardSuit trumpSuit, int botActorNumber, bool twoTaash)
    {
        if (trick == null || trick.Count == 0) return false;

        if (twoTaash && CanWinByMatchingCurrentWinner(card, trick, trumpSuit))
            return true;

        var simulated = new List<PlayerHand.TrickCard>(trick);
        simulated.Add(new PlayerHand.TrickCard
        {
            actorNumber = botActorNumber,
            suit = card.cardSuit,
            rankValue = RankValue(card)
        });

        PlayerHand.TrickCard winner = EvaluateTrickWinner(simulated, trumpSuit, twoTaash);
        return winner.actorNumber == botActorNumber;
    }

    /// <summary>
    /// 2 Taash: playing the same suit+rank as the current winner wins (bot plays last).
    /// </summary>
    static bool CanWinByMatchingCurrentWinner(CardData card, List<PlayerHand.TrickCard> trick, CardSuit trumpSuit)
    {
        PlayerHand.TrickCard currentWinner = EvaluateTrickWinner(trick, trumpSuit, twoTaash: true);
        return card.cardSuit == currentWinner.suit && RankValue(card) == currentWinner.rankValue;
    }

    /// <summary>
    /// Bot-side trick winner (mirrors TaashRules.DetermineTrickWinner with explicit 2 Taash flag).
    /// </summary>
    static PlayerHand.TrickCard EvaluateTrickWinner(List<PlayerHand.TrickCard> trick, CardSuit trumpSuit, bool twoTaash)
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
            else if (twoTaash &&
                     challenger.suit == winner.suit &&
                     challenger.rankValue == winner.rankValue)
                winnerIdx = i;
        }

        return trick[winnerIdx];
    }

    static bool IsActiveTwoTaash(List<CardData> botHand)
    {
        if (TaashRules.IsTwoTaashMode) return true;
        return botHand != null && botHand.Count > 13;
    }

    static bool TrickContainsDehla(List<PlayerHand.TrickCard> trick)
    {
        if (trick == null) return false;
        foreach (PlayerHand.TrickCard tc in trick)
        {
            if (tc.rankValue == (int)CardRank.Ten)
                return true;
        }
        return false;
    }

    static CardData PickCheapestWinningCard(List<CardData> cards) =>
        cards
            .GroupBy(c => (c.cardSuit, c.cardRank))
            .Select(g => g.First())
            .OrderBy(c => RankValue(c))
            .First();

    static CardData ChooseLowestDump(
        List<CardData> validCards,
        List<PlayerHand.TrickCard> trick,
        CardSuit trumpSuit,
        int botActorNumber,
        bool mustFollowSuit,
        bool twoTaash)
    {
        IEnumerable<CardData> dumpPool = validCards.Where(c =>
            !WouldBotWinTrick(c, trick, trumpSuit, botActorNumber, twoTaash));

        if (!dumpPool.Any())
            dumpPool = validCards;

        if (!mustFollowSuit)
        {
            List<CardData> nonTrump = dumpPool.Where(c => c.cardSuit != trumpSuit).ToList();
            if (nonTrump.Count > 0)
                dumpPool = nonTrump;
        }

        List<CardData> pool = dumpPool.ToList();
        List<CardData> withoutPremium = pool
            .Where(c => c.cardRank != CardRank.Ace && c.cardRank != CardRank.King)
            .ToList();
        if (withoutPremium.Count > 0)
            pool = withoutPremium;

        return pool.OrderBy(c => RankValue(c)).First();
    }
}
