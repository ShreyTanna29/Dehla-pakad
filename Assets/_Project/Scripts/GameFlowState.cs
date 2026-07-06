using System.Collections.Generic;
using UnityEngine;

/// <summary>High-level game flow — one active phase at a time; invalid transitions are blocked.</summary>
public enum GameFlowPhase
{
    Home,           // Main menu
    ModeSelection,
    Matchmaking,
    InRoom,         // Photon room / lobby before or between play
    Dealing,
    InGame,
    ResolvingTrick,
    GameFinished,
    Disconnected
}

public static class GameFlowState
{
    public static GameFlowPhase Current { get; private set; } = GameFlowPhase.Home;

    static readonly Dictionary<GameFlowPhase, HashSet<GameFlowPhase>> AllowedTransitions =
        new Dictionary<GameFlowPhase, HashSet<GameFlowPhase>>
        {
            { GameFlowPhase.Home, new HashSet<GameFlowPhase> { GameFlowPhase.ModeSelection, GameFlowPhase.Matchmaking, GameFlowPhase.InRoom, GameFlowPhase.InGame, GameFlowPhase.Disconnected } },
            { GameFlowPhase.ModeSelection, new HashSet<GameFlowPhase> { GameFlowPhase.Home, GameFlowPhase.Matchmaking, GameFlowPhase.InRoom, GameFlowPhase.Disconnected } },
            { GameFlowPhase.Matchmaking, new HashSet<GameFlowPhase> { GameFlowPhase.Home, GameFlowPhase.ModeSelection, GameFlowPhase.InRoom, GameFlowPhase.Dealing, GameFlowPhase.Disconnected } },
            { GameFlowPhase.InRoom, new HashSet<GameFlowPhase> { GameFlowPhase.Home, GameFlowPhase.ModeSelection, GameFlowPhase.Matchmaking, GameFlowPhase.Dealing, GameFlowPhase.InGame, GameFlowPhase.Disconnected } },
            { GameFlowPhase.Dealing, new HashSet<GameFlowPhase> { GameFlowPhase.InRoom, GameFlowPhase.InGame, GameFlowPhase.Home, GameFlowPhase.Disconnected } },
            { GameFlowPhase.InGame, new HashSet<GameFlowPhase> { GameFlowPhase.ResolvingTrick, GameFlowPhase.GameFinished, GameFlowPhase.InRoom, GameFlowPhase.Disconnected, GameFlowPhase.Dealing } },
            { GameFlowPhase.ResolvingTrick, new HashSet<GameFlowPhase> { GameFlowPhase.InGame, GameFlowPhase.GameFinished, GameFlowPhase.Disconnected } },
            { GameFlowPhase.GameFinished, new HashSet<GameFlowPhase> { GameFlowPhase.Home, GameFlowPhase.ModeSelection, GameFlowPhase.InRoom, GameFlowPhase.Matchmaking } },
            { GameFlowPhase.Disconnected, new HashSet<GameFlowPhase> { GameFlowPhase.Home, GameFlowPhase.ModeSelection } },
        };

    public static bool SetPhase(GameFlowPhase phase, bool forceRecovery = false)
    {
        if (Current == phase)
            return true;

        if (!forceRecovery && !IsTransitionAllowed(Current, phase))
        {
            Debug.LogWarning($"[GameFlow] Blocked invalid transition: {Current} → {phase}");
            return false;
        }

        Debug.Log($"[GameFlow] {Current} → {phase}");
        Current = phase;
        GameStabilityAudit.LogSnapshot("GameFlow." + phase);
        return true;
    }

    static bool IsTransitionAllowed(GameFlowPhase from, GameFlowPhase to)
    {
        if (to == GameFlowPhase.Home || to == GameFlowPhase.Disconnected)
            return true;
        if (from == GameFlowPhase.Disconnected && (to == GameFlowPhase.Home || to == GameFlowPhase.ModeSelection))
            return true;
        if (AllowedTransitions.TryGetValue(from, out HashSet<GameFlowPhase> allowed))
            return allowed.Contains(to);
        return false;
    }

    public static bool CanStartMatchmaking =>
        Current == GameFlowPhase.ModeSelection || Current == GameFlowPhase.Home;

    public static bool IsInActiveGame =>
        Current == GameFlowPhase.InGame || Current == GameFlowPhase.ResolvingTrick;

    /// <summary>True while a match is actively in progress (cards being dealt or played).
    /// Excludes InRoom so the seat lobby can still legitimately show before/between games.</summary>
    public static bool IsActivelyPlaying =>
        Current == GameFlowPhase.Dealing
        || Current == GameFlowPhase.InGame
        || Current == GameFlowPhase.ResolvingTrick;

    public static bool AllowsCardPlay =>
        Current == GameFlowPhase.InGame && !PlayerHand.IsTrickLocked && !PlayerHand.IsDealAnimationRunning;
}
