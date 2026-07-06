using UnityEngine;
using TMPro;
using Photon.Pun;

public class PingDisplay : MonoBehaviour
{
    private TMP_Text _pingText;
    private TMP_Text _shadowText;
    private float _updateTimer = 0f;
    private const float UpdateInterval = 1.0f;

    private void Awake()
    {
        _pingText = GetComponent<TMP_Text>();
        if (_pingText == null)
        {
            _pingText = GetComponentInChildren<TMP_Text>();
        }

        Transform shadow = transform.Find("Shadow");
        if (shadow != null)
        {
            _shadowText = shadow.GetComponent<TMP_Text>();
        }
    }

    private void Start()
    {
        UpdatePingDisplay();
    }

    private void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer >= UpdateInterval)
        {
            _updateTimer = 0f;
            UpdatePingDisplay();
        }
    }

    private void UpdatePingDisplay()
    {
        if (_pingText == null) return;

        string pingString = "";
        if (PhotonNetwork.OfflineMode)
        {
            pingString = "<color=#00FF00>Ping: 0ms (Offline)</color>";
        }
        else if (PhotonNetwork.IsConnectedAndReady)
        {
            int ping = PhotonNetwork.GetPing();
            string color = "white";
            if (ping < 100) color = "#00FF00"; // Green
            else if (ping < 200) color = "#FFFF00"; // Yellow
            else color = "#FF0000"; // Red
            
            pingString = $"<color={color}>Ping: {ping}ms</color>";
        }
        else if (PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.ConnectingToMasterServer || 
                 PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.ConnectingToNameServer)
        {
            pingString = "Ping: Connecting...";
        }
        else if (PhotonNetwork.IsConnected)
        {
            pingString = "Ping: ...";
        }
        else
        {
            pingString = "<color=#FF0000>Ping: Disconnected</color>";
        }

        _pingText.text = pingString;
        if (_shadowText != null)
        {
            // Remove color tags for shadow text to avoid double color or weird artifacts
            _shadowText.text = System.Text.RegularExpressions.Regex.Replace(pingString, "<.*?>", "");
        }
    }
}
