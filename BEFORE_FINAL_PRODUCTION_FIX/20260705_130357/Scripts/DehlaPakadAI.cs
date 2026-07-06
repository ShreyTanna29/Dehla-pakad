using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class DehlaPakadAI : MonoBehaviour
{
    public static DehlaPakadAI Instance;

    const int DehlaRank = (int)CardRank.Ten;
    const float DehlaCaptureValue = 185f;
    const float TrickWinValue = 42f;
    const float LeadDehlaPenalty = 220f;
    const float WasteHighCardPenalty = 22f;
    const float WasteTrumpPenalty = 28f;
    const float TakeTrickBonus = 78f;

    static readonly HashSet<long> PlayedCards = new HashSet<long>();
    static int LastTrackedTrickSize = -1;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public CardData ThinkAndSelectCard(List<CardData> botHand, List<PlayerHand.TrickCard> currentTrick, CardSuit trumpSuit, bool isTrumpRevealed, int botActorNumber)
    {
        if (botHand == null || botHand.Count == 0) return new CardData();

        GameModeType mode = GameSettings.Instance != null
            ? GameSettings.Instance.currentMode
            : GameModeType.TrumpSpades;
        bool isTwoTaash = TaashRules.IsTwoTaashMode;
        bool isDoubleSar = GameSettings.IsDoubleSarActive;
        bool isLeading = currentTrick == null || currentTrick.Count == 0;

        SyncPlayedCardMemory(botHand, currentTrick);

        HandContext ctx = BuildContext(botHand, currentTrick, trumpSuit, isTrumpRevealed, botActorNumber, mode, isTwoTaash, isDoubleSar, isLeading);

        List<CardData> legalMoves = PlayerHand.GetValidCards(botHand, currentTrick, botActorNumber);

        if (legalMoves.Count == 0)
            return botHand[0];

        CardData best = PickBestMove(legalMoves, ctx);
        if (!IsCardInList(botHand, best) && legalMoves.Count > 0)
            best = legalMoves[0];
        RecordCard(best.cardSuit, Rank(best));
        return best;
    }

    static bool IsCardInList(List<CardData> hand, CardData card) =>
        hand.Exists(c => c.cardSuit == card.cardSuit && c.cardRank == card.cardRank);

    static CardData PickBestMove(List<CardData> legalMoves, HandContext ctx)
    {
        if (ctx.IsLastToPlay && !ctx.IsLeading)
        {
            CardData? minWinner = FindMinimumWinningCard(legalMoves, ctx);
            if (minWinner.HasValue)
                return minWinner.Value;
        }

        if (!ctx.IsLeading && !ctx.PartnerWinning && legalMoves.Count > 1)
        {
            CardData? seizeTrick = FindBestLeadAfterPlayCard(legalMoves, ctx);
            if (seizeTrick.HasValue && (ctx.DehlaOnTable || ctx.TableValue >= 7f))
                return seizeTrick.Value;
        }

        CardData best = legalMoves[0];
        float bestScore = float.MinValue;

        foreach (CardData candidate in legalMoves)
        {
            float score = ScoreMove(candidate, ctx);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    static CardData? FindBestLeadAfterPlayCard(List<CardData> legalMoves, HandContext ctx)
    {
        CardData? best = null;
        float bestScore = float.MinValue;

        foreach (CardData card in legalMoves)
        {
            CardSuit evalTrump = ResolveTrumpAfterPlay(card, ctx.LeadSuit, ctx.Trump, ctx.TrumpRevealed, ctx.Mode);
            if (!WouldLeadAfterPlay(ctx.Trick, card, ctx.BotActor, evalTrump))
                continue;

            float score = ScoreMove(card, ctx) + TakeTrickBonus + ctx.TableValue;
            if (score > bestScore)
            {
                bestScore = score;
                best = card;
            }
        }

        return best;
    }

    static CardData? FindMinimumWinningCard(List<CardData> legalMoves, HandContext ctx)
    {
        CardData? best = null;
        int bestRank = int.MaxValue;

        foreach (CardData card in legalMoves)
        {
            CardSuit evalTrump = ResolveTrumpAfterPlay(card, ctx.LeadSuit, ctx.Trump, ctx.TrumpRevealed, ctx.Mode);
            if (!WouldWinIfLast(ctx.Trick, card, ctx.BotActor, evalTrump))
                continue;

            int rank = Rank(card);
            if (rank < bestRank)
            {
                bestRank = rank;
                best = card;
            }
        }

        return best;
    }

    static float ScoreMove(CardData card, HandContext ctx)
    {
        if (ctx.IsLeading)
            return ScoreLead(card, ctx);

        if (ctx.HasLeadSuit && card.cardSuit == ctx.LeadSuit)
            return ScoreFollowSuit(card, ctx);

        return ScoreVoidPlay(card, ctx);
    }

    static float ScoreLead(CardData card, HandContext ctx)
    {
        float score = 0f;
        int rank = Rank(card);
        bool isTrump = card.cardSuit == ctx.Trump;
        bool isDehla = IsDehla(card);
        int suitLen = ctx.SuitLength(card.cardSuit);
        int suitHigh = ctx.HighestInHand(card.cardSuit);

        if (isDehla && ctx.HandSize > 3)
            score -= LeadDehlaPenalty;
        else if (isDehla && ctx.HandSize <= 3)
            score += 35f + ctx.DehlasHeld * 10f;

        if (isTrump && ctx.TrumpRevealed)
            score -= rank * 2.5f + WasteTrumpPenalty;

        if (!isTrump && !isDehla)
        {
            bool controlsSuit = ctx.ControlsSuit(card.cardSuit);
            int unplayedHigher = ctx.CountUnplayedHigherInSuit(card.cardSuit, rank);

            if (controlsSuit && rank >= (int)CardRank.King)
                score += 48f + suitLen * 6f;
            else if (controlsSuit && rank >= (int)CardRank.Ten)
                score += 30f + suitLen * 4f;
            else if (suitLen == 1)
                score += 38f - rank + ctx.VoidCount * 2f;
            else if (suitLen == 2 && rank <= (int)CardRank.Seven)
                score += 24f - rank;
            else if (suitLen >= 4 && rank <= (int)CardRank.Six)
                score += 28f - rank + suitLen;
            else if (unplayedHigher == 0 && rank >= (int)CardRank.Ten)
                score += 20f + rank;
            else
                score += 14f - rank * 0.55f;

            if (rank == suitHigh && unplayedHigher > 2)
                score -= 15f;
        }

        if (ctx.IsTwoTaash && ctx.DuplicateCount(card) > 1)
        {
            if (rank >= (int)CardRank.Queen)
                score -= 30f;
            else
                score += 8f;
        }

        if (IsHiddenTrumpMode(ctx.Mode) && !ctx.TrumpRevealed && card.cardSuit == CardSuit.Spades)
            score -= 25f;

        if (ctx.IsDoubleSar || ctx.Mode == GameModeType.Cut2Trump)
        {
            if (suitLen == ctx.LongestSuitLength && !isDehla && rank <= (int)CardRank.Eight)
                score += 14f;
        }

        if (ctx.TrumpRevealed && ctx.TrumpCount >= 3 && isTrump && rank <= (int)CardRank.Nine)
            score += 18f - rank;

        if (ctx.TricksRemaining <= 4)
            score += EndgameLeadBonus(card, ctx);

        return score;
    }

    static float EndgameLeadBonus(CardData card, HandContext ctx)
    {
        float bonus = 0f;
        if (IsDehla(card))
            bonus += 25f;
        if (ctx.ControlsSuit(card.cardSuit) && Rank(card) >= (int)CardRank.King)
            bonus += 15f;
        return bonus;
    }

    static float ScoreFollowSuit(CardData card, HandContext ctx)
    {
        int rank = Rank(card);
        CardSuit evalTrump = ResolveTrumpAfterPlay(card, ctx.LeadSuit, ctx.Trump, ctx.TrumpRevealed, ctx.Mode);
        bool winsIfLast = ctx.IsLastToPlay && WouldWinIfLast(ctx.Trick, card, ctx.BotActor, evalTrump);
        bool leadingAfterPlay = WouldLeadAfterPlay(ctx.Trick, card, ctx.BotActor, evalTrump);
        float tableValue = ctx.TableValue;

        if (ctx.PartnerWinning && !ctx.DehlaOnTable && tableValue < 14f)
            return DumpScore(card, ctx);

        if (ctx.DehlaOnTable)
        {
            if (winsIfLast)
                return DehlaCaptureValue + TrickWinValue - rank;
            if (leadingAfterPlay)
                return DehlaCaptureValue * 0.92f + tableValue - rank * 0.35f;
            return DumpScore(card, ctx) - (IsDehla(card) ? 50f : 0f);
        }

        if (winsIfLast)
            return TakeTrickBonus + 12f + tableValue - OverkillPenalty(card, ctx, evalTrump) - rank * 0.08f;

        if (leadingAfterPlay)
        {
            if (ctx.OpponentTrumpLikely && tableValue < 8f)
                return DumpScore(card, ctx) - 3f;

            return TakeTrickBonus - 8f + tableValue - OverkillPenalty(card, ctx, evalTrump) - rank * 0.35f;
        }

        float dump = DumpScore(card, ctx);
        if (tableValue >= 18f && rank >= (int)CardRank.Queen)
            dump -= 20f;
        return dump;
    }

    static float ScoreVoidPlay(CardData card, HandContext ctx)
    {
        int rank = Rank(card);
        bool isTrumpCard = card.cardSuit == ctx.Trump;
        CardSuit evalTrump = ResolveTrumpAfterPlay(card, ctx.LeadSuit, ctx.Trump, ctx.TrumpRevealed, ctx.Mode);
        bool winsIfLast = ctx.IsLastToPlay && WouldWinIfLast(ctx.Trick, card, ctx.BotActor, evalTrump);
        bool leadingAfterPlay = WouldLeadAfterPlay(ctx.Trick, card, ctx.BotActor, evalTrump);
        float tableValue = ctx.TableValue;

        if (ctx.PartnerWinning && !ctx.DehlaOnTable && tableValue < 14f)
            return DumpScore(card, ctx);

        if (ctx.DehlaOnTable)
        {
            if (winsIfLast)
                return DehlaCaptureValue + TrickWinValue - rank - (isTrumpCard ? 5f : 0f);

            if (leadingAfterPlay && (isTrumpCard || IsCutTrumpMode(ctx.Mode)))
                return DehlaCaptureValue * 0.75f + tableValue * 0.5f - rank;

            if (IsCutTrumpMode(ctx.Mode) && !isTrumpCard)
            {
                int suitLen = ctx.SuitLength(card.cardSuit);
                if (suitLen >= ctx.LongestSuitLength - 1)
                    return DehlaCaptureValue * 0.45f + suitLen * 7f - rank;
            }

            return DumpScore(card, ctx);
        }

        if (winsIfLast && (isTrumpCard || tableValue >= 8f))
            return TakeTrickBonus + 10f + tableValue - rank - (isTrumpCard ? rank * 0.35f : 0f);

        if (leadingAfterPlay && isTrumpCard)
        {
            if (tableValue >= 7f)
                return TakeTrickBonus - 5f + tableValue - rank;
            return DumpScore(card, ctx) - WasteTrumpPenalty * 0.6f;
        }

        float dump = DumpScore(card, ctx);

        if (IsCutTrumpMode(ctx.Mode) && card.cardSuit != ctx.LeadSuit && !isTrumpCard
            && tableValue < 10f && ctx.SuitLength(card.cardSuit) >= 4)
            dump += 10f - rank * 0.15f;

        if (isTrumpCard && !ctx.DehlaOnTable && tableValue < 8f)
            dump -= WasteTrumpPenalty;

        return dump;
    }

    static float OverkillPenalty(CardData card, HandContext ctx, CardSuit evalTrump)
    {
        if (!ctx.IsLastToPlay) return 0f;
        int rank = Rank(card);
        int minNeeded = MinRankToWin(ctx.Trick, ctx.BotActor, evalTrump, ctx.LeadSuit);
        if (minNeeded < 0) return 0f;
        return Mathf.Max(0f, (rank - minNeeded) * 4f);
    }

    static int MinRankToWin(List<PlayerHand.TrickCard> trick, int botActor, CardSuit trump, CardSuit leadSuit)
    {
        int best = int.MaxValue;
        for (int r = (int)CardRank.Two; r <= (int)CardRank.Ace; r++)
        {
            var test = new CardData { cardSuit = leadSuit, cardRank = (CardRank)r };
            if (WouldWinIfLast(trick, test, botActor, trump))
                best = Mathf.Min(best, r);
        }
        return best == int.MaxValue ? -1 : best;
    }

    static float DumpScore(CardData card, HandContext ctx)
    {
        float score = 32f - Rank(card);
        if (IsDehla(card)) score -= 30f;
        if (card.cardRank == CardRank.Ace) score -= 12f;
        else if (card.cardRank == CardRank.King) score -= 6f;
        if (card.cardSuit == ctx.Trump && ctx.TrumpRevealed) score -= 8f;
        if (ctx.IsVoid(card.cardSuit)) score += 4f;
        return score;
    }

    struct HandContext
    {
        public List<CardData> Hand;
        public List<PlayerHand.TrickCard> Trick;
        public CardSuit Trump;
        public CardSuit LeadSuit;
        public bool TrumpRevealed;
        public int BotActor;
        public int PartnerActor;
        public GameModeType Mode;
        public bool IsTwoTaash;
        public bool IsDoubleSar;
        public bool IsLeading;
        public bool IsLastToPlay;
        public bool HasLeadSuit;
        public bool DehlaOnTable;
        public bool PartnerWinning;
        public bool OpponentTrumpLikely;
        public int HandSize;
        public int TricksRemaining;
        public int TrumpCount;
        public int DehlasHeld;
        public int VoidCount;
        public int LongestSuitLength;
        public float TableValue;

        public int SuitLength(CardSuit suit) => Hand.Count(c => c.cardSuit == suit);
        public bool IsVoid(CardSuit suit) => SuitLength(suit) == 0;
        public int HighestInHand(CardSuit suit)
        {
            var cards = Hand.Where(c => c.cardSuit == suit).ToList();
            return cards.Count == 0 ? -1 : cards.Max(c => Rank(c));
        }
        public int DuplicateCount(CardData card) =>
            Hand.Count(c => c.cardSuit == card.cardSuit && c.cardRank == card.cardRank);

        public bool ControlsSuit(CardSuit suit)
        {
            if (suit == Trump) return false;
            int high = HighestInHand(suit);
            if (high < 0) return false;
            return CountUnplayedHigherInSuit(suit, high) == 0 && SuitLength(suit) >= 2;
        }

        public int CountUnplayedHigherInSuit(CardSuit suit, int rank)
        {
            int count = 0;
            for (int r = rank + 1; r <= (int)CardRank.Ace; r++)
            {
                if (!IsPlayed(suit, r) && !Holds(suit, r))
                    count++;
            }
            return count;
        }

        bool Holds(CardSuit suit, int rank) =>
            Hand.Any(c => c.cardSuit == suit && Rank(c) == rank);
    }

    static HandContext BuildContext(
        List<CardData> hand,
        List<PlayerHand.TrickCard> trick,
        CardSuit trump,
        bool trumpRevealed,
        int botActor,
        GameModeType mode,
        bool isTwoTaash,
        bool isDoubleSar,
        bool isLeading)
    {
        if (hand == null) hand = new List<CardData>();
        if (trick == null) trick = new List<PlayerHand.TrickCard>();

        int partner = GetPartnerActor(botActor);
        CardSuit leadSuit = isLeading ? CardSuit.Spades : trick[0].suit;
        bool hasLead = !isLeading && hand.Any(c => c.cardSuit == leadSuit);
        PlayerHand.TrickCard leader = isLeading ? default : GetCurrentWinner(trick, trump);
        bool partnerWinning = partner >= 0 && !isLeading && leader.actorNumber == partner;
        bool dehlaOnTable = !isLeading && trick.Any(t => t.rankValue == DehlaRank);
        int trickCount = trick?.Count ?? 0;

        int cardsPerSuit = isTwoTaash ? 26 : 13;
        int trumpOut = EstimateTrumpStillOut(hand, trump, cardsPerSuit);
        float tableVal = isLeading ? 0f : TrickTableValue(trick);

        var ctx = new HandContext
        {
            Hand = hand,
            Trick = trick,
            Trump = trump,
            LeadSuit = leadSuit,
            TrumpRevealed = trumpRevealed,
            BotActor = botActor,
            PartnerActor = partner,
            Mode = mode,
            IsTwoTaash = isTwoTaash,
            IsDoubleSar = isDoubleSar,
            IsLeading = isLeading,
            IsLastToPlay = trickCount == 3,
            HasLeadSuit = hasLead,
            DehlaOnTable = dehlaOnTable,
            PartnerWinning = partnerWinning,
            OpponentTrumpLikely = trumpRevealed && trumpOut >= 1 && trickCount <= 2 && tableVal >= 6f,
            HandSize = hand.Count,
            TricksRemaining = hand.Count - 1,
            TrumpCount = hand.Count(c => c.cardSuit == trump),
            DehlasHeld = hand.Count(IsDehla),
            VoidCount = CountVoids(hand),
            LongestSuitLength = hand.GroupBy(c => c.cardSuit).Max(g => g.Count()),
            TableValue = tableVal
        };
        return ctx;
    }

    static int CountVoids(List<CardData> hand)
    {
        int voids = 0;
        foreach (CardSuit suit in System.Enum.GetValues(typeof(CardSuit)))
        {
            if (!hand.Any(c => c.cardSuit == suit))
                voids++;
        }
        return voids;
    }

    static int EstimateTrumpStillOut(List<CardData> hand, CardSuit trump, int cardsPerSuit)
    {
        int inHand = hand.Count(c => c.cardSuit == trump);
        int playedInTrump = CountPlayedInSuit(trump);
        return Mathf.Max(0, cardsPerSuit - inHand - playedInTrump);
    }

    static int CountPlayedInSuit(CardSuit suit)
    {
        int count = 0;
        foreach (long key in PlayedCards)
        {
            if ((CardSuit)(key >> 8) == suit)
                count++;
        }
        return count;
    }

    static void SyncPlayedCardMemory(List<CardData> hand, List<PlayerHand.TrickCard> trick)
    {
        int trickSize = trick?.Count ?? 0;

        if (hand.Count >= TaashRules.CardsPerPlayer && trickSize == 0 && PlayedCards.Count > 0)
            PlayedCards.Clear();

        if (trickSize == 0 && LastTrackedTrickSize > 0)
            LastTrackedTrickSize = 0;

        if (trick != null)
        {
            foreach (PlayerHand.TrickCard t in trick)
                RecordCard(t.suit, t.rankValue);
        }

        LastTrackedTrickSize = trickSize;
    }

    static void RecordCard(CardSuit suit, int rank) => PlayedCards.Add(PackCard(suit, rank));

    static bool IsPlayed(CardSuit suit, int rank) => PlayedCards.Contains(PackCard(suit, rank));

    static long PackCard(CardSuit suit, int rank) => ((long)suit << 8) | (uint)rank;

    static float TrickTableValue(List<PlayerHand.TrickCard> trick)
    {
        float value = 0f;
        foreach (PlayerHand.TrickCard t in trick)
        {
            if (t.rankValue == DehlaRank) value += 20f;
            else value += t.rankValue;
        }
        return value;
    }

    static PlayerHand.TrickCard GetCurrentWinner(List<PlayerHand.TrickCard> trick, CardSuit trump)
    {
        if (trick == null || trick.Count == 0) return default;
        return TaashRules.DetermineTrickWinner(new List<PlayerHand.TrickCard>(trick), trump);
    }

    static bool WouldWinIfLast(List<PlayerHand.TrickCard> trick, CardData card, int botActor, CardSuit trump)
    {
        var sim = new List<PlayerHand.TrickCard>(trick);
        sim.Add(new PlayerHand.TrickCard
        {
            actorNumber = botActor,
            suit = card.cardSuit,
            rankValue = Rank(card)
        });
        return TaashRules.DetermineTrickWinner(sim, trump).actorNumber == botActor;
    }

    static bool WouldLeadAfterPlay(List<PlayerHand.TrickCard> trick, CardData card, int botActor, CardSuit trump)
    {
        var sim = new List<PlayerHand.TrickCard>(trick);
        sim.Add(new PlayerHand.TrickCard
        {
            actorNumber = botActor,
            suit = card.cardSuit,
            rankValue = Rank(card)
        });
        return GetCurrentWinner(sim, trump).actorNumber == botActor;
    }

    static CardSuit ResolveTrumpAfterPlay(CardData card, CardSuit ledSuit, CardSuit trump, bool trumpRevealed, GameModeType mode)
    {
        if (card.cardSuit == ledSuit || card.cardSuit == trump)
            return trump;

        if (mode == GameModeType.Cut1Trump && !trumpRevealed)
            return card.cardSuit;

        if (mode == GameModeType.Cut2Trump)
            return card.cardSuit;

        return trump;
    }

    static bool IsCutTrumpMode(GameModeType mode) =>
        mode == GameModeType.Cut1Trump || mode == GameModeType.Cut2Trump;

    static bool IsHiddenTrumpMode(GameModeType mode) =>
        mode == GameModeType.ThirteenthCardTrump || mode == GameModeType.Cut2Trump
        || mode == GameModeType.HiddenTrump;

    static int GetPartnerActor(int actor)
    {
        // 2v2 partnerships exist ONLY in the friends private room. In bots/online (1v1v1v1)
        // every bot plays for itself, so there is no partner and no cooperative play.
        if (!IsFriendsTeamMode()) return -1;
        if (DeckManager.Instance == null) return -1;
        List<int> seats = DeckManager.Instance.GetActiveSeatActorsSorted();
        if (seats == null || seats.Count != 4) return -1;
        int idx = seats.IndexOf(actor);
        if (idx < 0) return -1;
        // Partner sits ACROSS the table (player 1 & 3 are a team, player 2 & 4 are a team).
        return seats[(idx + 2) % 4];
    }

    /// <summary>
    /// True only when this is a FRIENDS private room running 2v2 (logic mode 2). Bots (offline) and
    /// Online (visible room) always return false so the AI keeps playing 1v1v1v1.
    /// </summary>
    static bool IsFriendsTeamMode()
    {
        if (!DeckManager.IsPrivateFriendsRoom()) return false;
        int logic = ModeManager.Instance != null ? ModeManager.Instance.currentLogicMode : 1;
        return logic == 2;
    }

    static bool IsDehla(CardData card) => card.cardRank == CardRank.Ten;

    static int Rank(CardData card) => (int)card.cardRank;
}
