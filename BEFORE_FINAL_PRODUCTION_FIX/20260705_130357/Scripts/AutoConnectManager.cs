using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class AutoConnectManager : MonoBehaviourPunCallbacks
{
    void Start()
    {
        if (PhotonNetwork.OfflineMode) return;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EnsureJoinLobby();
    }

    public override void OnConnectedToMaster()
    {
        if (PhotonNetwork.OfflineMode) return;
        if (!NetworkManager.CanCallPhotonLobbyOps()) return;

        // Don't fight an in-progress room rejoin or an active match — that would cancel
        // ReconnectAndRejoin and bounce the player to Home (transient-disconnect glitch).
        var nm = NetworkManager.Instance;
        if (nm != null && nm.IsAttemptingRejoin) return;
        if (GameFlowState.Current == GameFlowPhase.Disconnected ||
            GameFlowState.Current == GameFlowPhase.InRoom ||
            GameFlowState.Current == GameFlowPhase.Dealing ||
            GameFlowState.Current == GameFlowPhase.InGame ||
            GameFlowState.Current == GameFlowPhase.ResolvingTrick)
            return;

        Debug.Log("Auto-Connected to Photon! Ready for Multiplayer & Friends.");

        if (nm != null)
            nm.EnsureJoinLobby();
    }

    // F. Reconnection assist — NetworkManager owns in-match 30s rejoin; this covers edge drops.
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning("[Reconnection] Disconnected due to: " + cause);

        if (cause == DisconnectCause.DisconnectByClientLogic
            || cause == DisconnectCause.CustomAuthenticationFailed
            || PhotonNetwork.OfflineMode)
            return;

        var nm = NetworkManager.Instance;
        if (nm == null) return;

        if (NetworkManager.IsFatalDisconnect(cause))
        {
            Debug.LogError("[Reconnection] Fatal disconnect — skipping ReconnectAndRejoin.");
            nm.CancelReconnectUiForMenu();
            return;
        }

        // NetworkManager already runs the full in-match reconnect + 30s abandon flow.
        if (nm.IsAttemptingRejoin
            || GameFlowState.Current == GameFlowPhase.InGame
            || GameFlowState.Current == GameFlowPhase.Dealing
            || GameFlowState.Current == GameFlowPhase.ResolvingTrick
            || GameFlowState.Current == GameFlowPhase.Disconnected)
            return;

        if (!PhotonNetwork.OfflineMode)
        {
            var phase = GameFlowState.Current;
            if (phase == GameFlowPhase.Home
                || phase == GameFlowPhase.ModeSelection
                || phase == GameFlowPhase.Matchmaking)
            {
                Debug.Log("[Reconnection] Menu phase disconnect — skip auto ReconnectAndRejoin.");
                if (nm != null)
                    nm.CancelReconnectUiForMenu();
                return;
            }

            Debug.Log("[Reconnection] Attempting ReconnectAndRejoin within 30s...");
            nm.ShowLoading("Reconnecting...");
            PhotonNetwork.ReconnectAndRejoin();
        }
    }
}
