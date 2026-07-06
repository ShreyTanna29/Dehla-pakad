using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Photon.Pun;

/// <summary>Central validation and audit logging — safeguards only, no gameplay changes.</summary>
public static class GameStabilityAudit
{
    public static void LogSnapshot(string source)
    {
        int trickCount = PlayerHand.LocalInstance != null && PlayerHand.LocalInstance.currentTrick != null
            ? PlayerHand.LocalInstance.currentTrick.Count
            : 0;
        int turnActor = PlayerHand.LocalInstance != null ? PlayerHand.LocalInstance.currentTurnActor : -1;
        int botCount = DeckManager.botActorNumbers != null ? DeckManager.botActorNumbers.Count : 0;
        int humanCount = DeckManager.GetActiveHumanPlayerCount();
        int seatCount = DeckManager.Instance != null ? DeckManager.Instance.BuildActiveSeatList().Count : 0;
        int localCards = PlayerHand.LocalInstance != null ? PlayerHand.LocalInstance.myCards.Count : 0;
        int expectedCards = TaashRules.CardsPerPlayer;

        var sb = new StringBuilder();
        sb.AppendLine($"[Stability] Snapshot — {source}");
        sb.AppendLine($"[Stability] Game State: {GameFlowState.Current}");
        sb.AppendLine($"[Stability] Turn Actor: {turnActor}");
        sb.AppendLine($"[Stability] Trick Count: {trickCount}");
        sb.AppendLine($"[Stability] Trump Suit: {PlayerHand.currentTrumpSuit} (revealed={PlayerHand.isTrumpRevealed})");
        sb.AppendLine($"[Stability] Player Count (active humans): {humanCount}");
        sb.AppendLine($"[Stability] Bot Count: {botCount}");
        sb.AppendLine($"[Stability] Seat Count: {seatCount}");
        sb.AppendLine($"[Stability] Card Count (local): {localCards} / expected {expectedCards}");
        sb.AppendLine($"[Stability] Trick Locked: {PlayerHand.IsTrickLocked} | Deal Anim: {PlayerHand.IsDealAnimationRunning}");
        Debug.Log(sb.ToString());
    }

    public static void LogTurn(string source, int actor, int trickCount)
    {
        Debug.Log($"[Turn] {source} | actor={actor} | trickCount={trickCount} | state={GameFlowState.Current}");
    }

    public static void LogTrick(string source, int count)
    {
        if (count > 4)
            Debug.LogError($"[Trick] {source} — INVALID trick count {count} (max 4)");
        else
            Debug.Log($"[Trick] {source} | count={count}");
    }

    public static bool ValidateTrickCount(int count, string source)
    {
        if (count < 0 || count > 4)
        {
            Debug.LogError($"[Trick] {source} — invalid count {count}");
            return false;
        }
        return true;
    }

    public static bool ValidateSeatCountForMatchStart()
    {
        if (DeckManager.Instance == null)
        {
            Debug.LogError("[Seat] DeckManager missing — cannot validate seats.");
            return false;
        }

        List<int> seats = DeckManager.Instance.BuildActiveSeatList();
        int humans = DeckManager.GetActiveHumanPlayerCount();
        int bots = DeckManager.botActorNumbers != null ? DeckManager.botActorNumbers.Count : 0;

        if (seats.Count != DeckManager.MaxTableSeats)
        {
            Debug.LogError($"[Seat] Match start blocked — seats={seats.Count}, humans={humans}, bots={bots}, need {DeckManager.MaxTableSeats}");
            return false;
        }

        var seen = new HashSet<int>();
        foreach (int a in seats)
        {
            if (!seen.Add(a))
            {
                Debug.LogError($"[Seat] Duplicate actor in seat list: {a}");
                return false;
            }
        }

        Debug.Log($"[Seat] OK — humans={humans}, bots={bots}, seats=[{string.Join(", ", seats)}]");
        return true;
    }

    public static bool ValidateLocalHandCount(string source)
    {
        if (PlayerHand.LocalInstance == null) return true;
        int expected = TaashRules.CardsPerPlayer;
        int actual = PlayerHand.LocalInstance.myCards.Count;
        if (actual > expected)
        {
            Debug.LogError($"[Cards] {source} — local hand {actual} exceeds expected {expected}");
            return false;
        }
        return true;
    }

    public static void AuditHandCountsAfterDeal(string source)
    {
        if (DeckManager.Instance != null)
            DeckManager.Instance.AuditHandCounts(source);
    }

    public static bool ValidateTrump(string source)
    {
        CardSuit suit = PlayerHand.currentTrumpSuit;
        if (suit < CardSuit.Spades || suit > CardSuit.Clubs)
        {
            Debug.LogWarning($"[Trump] {source} — invalid suit {suit}, defaulting to Spades");
            PlayerHand.currentTrumpSuit = CardSuit.Spades;
            if (TrumpManager.Instance != null)
                TrumpManager.ApplyTrumpForCurrentGameMode(false);
            return false;
        }

        if (TrumpManager.Instance == null)
        {
            Debug.LogWarning($"[Trump] {source} — TrumpManager.Instance is null");
            return false;
        }

        return true;
    }

    public static bool CanStartTurn()
    {
        if (DeckManager.Instance != null && !DeckManager.Instance.IsDealingComplete)
            return false;
        if (PlayerHand.IsDealAnimationRunning)
            return false;
        if (PlayerHand.IsTrickLocked)
            return false;
        if (GameFlowState.Current != GameFlowPhase.InGame && GameFlowState.Current != GameFlowPhase.InRoom)
            return false;
        return true;
    }

    public static bool CanAcceptPlayerInput()
    {
        if (!CanStartTurn()) return false;
        if (GameFlowState.Current != GameFlowPhase.InGame) return false;
        return true;
    }
}
