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

    // Card ki value nikalne ke liye
    int GetRealValue(CardData card)
    {
        return (int)card.cardRank; 
    }

    public CardData ThinkAndSelectCard(List<CardData> botHand, List<PlayerHand.TrickCard> currentTrick, CardSuit trumpSuit, bool isTrumpRevealed, int botActorNumber)
    {
        if (botHand == null || botHand.Count == 0) return new CardData();

        List<CardData> validCards = PlayerHand.GetValidCards(botHand, currentTrick);

        if (validCards.Count == 0)
        {
            Debug.LogError($"[AI] No valid cards found for bot {botActorNumber}! Using first available.");
            return botHand[0];
        }

        // If leading
        if (currentTrick == null || currentTrick.Count == 0)
        {
            // Lead with highest card
            return validCards.OrderByDescending(c => GetRealValue(c)).First();
        }

        CardSuit ledSuit = currentTrick[0].suit;
        bool hasLedSuit = validCards.Any(c => c.cardSuit == ledSuit);

        if (hasLedSuit)
        {
            // Try to win the trick with a card higher than the current winner if possible
            SimpleTrickCard currentWinner = GetCurrentWinner(currentTrick, trumpSuit);
            var winningCards = validCards.Where(c => c.cardSuit == ledSuit && GetRealValue(c) > currentWinner.rankValue).ToList();
            if (winningCards.Count > 0)
            {
                // Play lowest winning card
                return winningCards.OrderBy(c => GetRealValue(c)).First();
            }
            else
            {
                // Cannot win, play lowest card of led suit
                return validCards.Where(c => c.cardSuit == ledSuit).OrderBy(c => GetRealValue(c)).First();
            }
        }
        else
        {
            // Cannot follow suit. Must decide what to throw or cut.
            // If Mode 3: Cut To Trump, any card played becomes the new trump.
            if (GameSettings.Instance != null && GameSettings.Instance.currentMode == GameModeType.CutToTrump)
            {
                // Strategy: Cut with a suit we have many of, and preferably high ones.
                var suitCounts = botHand.GroupBy(c => c.cardSuit).OrderByDescending(g => g.Count());
                CardSuit bestSuitToCut = suitCounts.First().Key;
                return botHand.Where(c => c.cardSuit == bestSuitToCut).OrderByDescending(c => GetRealValue(c)).First();
            }
            else
            {
                // Other modes: Use trump if possible to win, or throw junk.
                var trumps = validCards.Where(c => c.cardSuit == trumpSuit).ToList();
                if (trumps.Count > 0)
                {
                    SimpleTrickCard currentWinner = GetCurrentWinner(currentTrick, trumpSuit);
                    if (currentWinner.suit != trumpSuit)
                    {
                        // Current winner is not trump, any trump wins. Play lowest trump.
                        return trumps.OrderBy(c => GetRealValue(c)).First();
                    }
                    else
                    {
                        // Current winner is trump. Need to play higher trump to win.
                        var winningTrumps = trumps.Where(c => GetRealValue(c) > currentWinner.rankValue).ToList();
                        if (winningTrumps.Count > 0)
                        {
                            return winningTrumps.OrderBy(c => GetRealValue(c)).First();
                        }
                    }
                }
                
                // Cannot win or no trump, play lowest junk card
                return validCards.OrderBy(c => GetRealValue(c)).First();
            }
        }
    }

    private struct SimpleTrickCard
    {
        public CardSuit suit;
        public int rankValue;
    }

    private SimpleTrickCard GetCurrentWinner(List<PlayerHand.TrickCard> currentTrick, CardSuit trumpSuit)
    {
        SimpleTrickCard winner = new SimpleTrickCard { suit = currentTrick[0].suit, rankValue = currentTrick[0].rankValue };
        CardSuit ledSuit = currentTrick[0].suit;

        for (int i = 1; i < currentTrick.Count; i++)
        {
            PlayerHand.TrickCard challenger = currentTrick[i];
            bool challengerIsTrump = challenger.suit == trumpSuit;
            bool winnerIsTrump = winner.suit == trumpSuit;

            if (challengerIsTrump && !winnerIsTrump)
            {
                winner.suit = challenger.suit;
                winner.rankValue = challenger.rankValue;
            }
            else if (challengerIsTrump && winnerIsTrump)
            {
                if (challenger.rankValue > winner.rankValue)
                {
                    winner.suit = challenger.suit;
                    winner.rankValue = challenger.rankValue;
                }
            }
            else if (!challengerIsTrump && !winnerIsTrump)
            {
                if (challenger.suit == ledSuit && challenger.rankValue > winner.rankValue)
                {
                    winner.suit = challenger.suit;
                    winner.rankValue = challenger.rankValue;
                }
            }
        }
        return winner;
    }
}
