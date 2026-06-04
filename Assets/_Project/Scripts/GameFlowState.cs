using UnityEngine;

/// <summary>High-level UI/network state — prevents duplicate init and guides reconnection.</summary>
public enum GameFlowPhase
{
    Home,
    ModeSelection,
    Matchmaking,
    InRoom,
    InGame,
    GameFinished
}

public static class GameFlowState
{
    public static GameFlowPhase Current { get; private set; } = GameFlowPhase.Home;

    public static void SetPhase(GameFlowPhase phase)
    {
        if (Current == phase) return;
        Debug.Log($"[GameFlow] {Current} → {phase}");
        Current = phase;
    }

    public static bool CanStartMatchmaking =>
        Current == GameFlowPhase.ModeSelection || Current == GameFlowPhase.Home;

    public static bool IsInActiveGame =>
        Current == GameFlowPhase.InGame || Current == GameFlowPhase.InRoom;
}
