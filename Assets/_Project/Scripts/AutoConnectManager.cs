using UnityEngine;
using Photon.Pun;

public class AutoConnectManager : MonoBehaviourPunCallbacks
{
    void Start()
    {
        // Photon connect is started by NetworkManager.TryConnectPhotonAtStartup().
        if (PhotonNetwork.OfflineMode) return;

        if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InLobby)
            PhotonNetwork.JoinLobby();
    }

    public override void OnConnectedToMaster()
    {
        if (PhotonNetwork.OfflineMode) return;

        Debug.Log("Auto-Connected to Photon! Ready for Multiplayer & Friends.");

        if (!PhotonNetwork.InLobby)
            PhotonNetwork.JoinLobby();
    }
}
