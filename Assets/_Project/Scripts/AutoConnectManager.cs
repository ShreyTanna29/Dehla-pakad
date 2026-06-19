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
}
