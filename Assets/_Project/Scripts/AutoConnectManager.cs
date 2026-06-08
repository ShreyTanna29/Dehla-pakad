using UnityEngine;
using Photon.Pun;

public class AutoConnectManager : MonoBehaviourPunCallbacks
{
    void Start()
    {
        if (PhotonNetwork.OfflineMode) return;

        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("Auto-Connecting to Photon Master Server...");
            PhotonNetwork.ConnectUsingSettings();
        }
        else if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }
    }

    public override void OnConnectedToMaster()
    {
        if (PhotonNetwork.OfflineMode) return;

        Debug.Log("Auto-Connected to Photon! Ready for Multiplayer & Friends.");

        if (!PhotonNetwork.InLobby)
            PhotonNetwork.JoinLobby();
    }
}
