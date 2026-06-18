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

        Debug.Log("Auto-Connected to Photon! Ready for Multiplayer & Friends.");

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EnsureJoinLobby();
    }
}
