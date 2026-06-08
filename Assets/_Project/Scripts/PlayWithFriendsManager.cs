using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using System.Collections.Generic;

public class PlayWithFriendsManager : MonoBehaviourPunCallbacks
{
    public static PlayWithFriendsManager Instance;

    [Header("PIN UI Components")]
    public TMP_InputField pinInputField;
    public TMP_Text generatedPinText;
    public GameObject pinCreationPanel;
    public TMP_Text errorText;

    [Header("Lobby Buttons & Panels")]
    public GameObject startGameButton;
    public GameObject modesPanel;
    public TMP_Text clientWaitingText;

    [Header("Friends List Storage")]
    private const string FriendsPrefsKey = "SavedFriendsList";
    public List<string> myFriends = new List<string>();
    PhotonView _photonView;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        LoadFriends();
        EnsurePhotonUserId();
        EnsurePhotonView();
    }

    PhotonView photonView
    {
        get
        {
            EnsurePhotonView();
            return _photonView;
        }
    }

    void EnsurePhotonView()
    {
        if (_photonView == null)
            _photonView = GetComponent<PhotonView>();

        if (_photonView == null)
        {
            _photonView = gameObject.AddComponent<PhotonView>();
            _photonView.Synchronization = ViewSynchronization.Off;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);
        if (startGameButton != null) startGameButton.SetActive(false);
    }

    void EnsurePhotonUserId()
    {
        if (PhotonNetwork.AuthValues == null)
            PhotonNetwork.AuthValues = new AuthenticationValues();

        if (string.IsNullOrEmpty(PhotonNetwork.AuthValues.UserId))
        {
            string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("PhotonUserId", uid);
            PhotonNetwork.AuthValues.UserId = uid;
        }
    }

    // ==========================================
    // 1. HOST: CREATE PRIVATE ROOM (modes later)
    // ==========================================

    public void CreatePrivateRoom()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            ShowUIError("Server not ready. Wait a moment...");
            return;
        }

        string newPin = Random.Range(10000, 99999).ToString();
        Debug.Log("Generating PIN: " + newPin);

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = 4,
            IsVisible = false,
            IsOpen = true
        };

        PhotonNetwork.CreateRoom(newPin, roomOptions);
    }

    // ==========================================
    // 2. CLIENT: JOIN ROOM WITH PIN
    // ==========================================

    public void JoinRoomWithPIN()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (pinInputField == null || string.IsNullOrEmpty(pinInputField.text))
        {
            ShowUIError("Enter valid PIN!");
            return;
        }

        PhotonNetwork.JoinRoom(pinInputField.text.Trim());
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError("Room Join Failed: " + message);
        ShowUIError("Invalid PIN or Room Full!");
    }

    void ShowUIError(string errorMsg)
    {
        if (errorText == null) return;
        errorText.text = errorMsg;
        errorText.gameObject.SetActive(true);
    }

    // ==========================================
    // 3. WHEN ANYONE JOINS THE ROOM
    // ==========================================

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        if (!PhotonNetwork.CurrentRoom.IsVisible)
        {
            Debug.Log("Private Room Joined. Waiting in Lobby...");
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.StayInPrivateLobbyUI();
            ShowPrivateRoomLobbyUI();
            return;
        }
    }

    void ShowPrivateRoomLobbyUI()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        if (pinCreationPanel != null) pinCreationPanel.SetActive(true);
        if (generatedPinText != null) generatedPinText.text = "Room PIN: " + PhotonNetwork.CurrentRoom.Name;
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (modesPanel != null && !PhotonNetwork.IsMasterClient)
            modesPanel.SetActive(false);

        CheckPlayerCountAndToggleStart();
    }

    void CheckPlayerCountAndToggleStart()
    {
        if (startGameButton == null)
            UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || startGameButton == null)
            return;

        bool roomFull = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats;
        startGameButton.SetActive(roomFull);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
            ShowPrivateRoomLobbyUI();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
            CheckPlayerCountAndToggleStart();
    }

    // ==========================================
    // SHARE PIN LOGIC
    // ==========================================

    public void ShareRoomPIN()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible)
            return;

        string pin = PhotonNetwork.CurrentRoom.Name;
        string shareMessage = $"Aaja Dehla Pakad khelte hain! Mera Private Room PIN hai: {pin}. Jaldi join kar!";

        GUIUtility.systemCopyBuffer = shareMessage;
        Debug.Log("Copied to clipboard: " + shareMessage);

        if (errorText != null)
        {
            errorText.text = "PIN Copied!";
            errorText.gameObject.SetActive(true);
        }
    }

    // ==========================================
    // HOST CLICKS START: OPENS MODES PANEL & HIDES FRIENDS PANEL
    // ==========================================

    public void OpenModesPanelForHost()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ResolveModesPanel();
        if (modesPanel != null)
        {
            modesPanel.SetActive(true);
            CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }

        // RPC pehle — panel band karne se pehle clients ko notify karo
        photonView.RPC(nameof(RPC_ShowModesPanelToClients), RpcTarget.Others);

        if (PhotonNetwork.CurrentRoom != null)
            PhotonNetwork.CurrentRoom.IsOpen = false;

        gameObject.SetActive(false);
    }

    [PunRPC]
    void RPC_ShowModesPanelToClients()
    {
        ResolveModesPanel();
        if (modesPanel != null)
        {
            modesPanel.SetActive(true);
            CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
        }

        if (clientWaitingText != null)
        {
            clientWaitingText.gameObject.SetActive(true);
            clientWaitingText.text = "Host is selecting game modes...";
        }

        if (ModeManager.Instance != null)
            ModeManager.Instance.ApplyLiveModesFromRoomIfPresent();

        gameObject.SetActive(false);
    }

    // Live sync: 1 Sar=1, 2 Sar=2, 1 Taash=3, 2 Taash=4
    public void HostSelectedGameMode(int modeIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "GameMode", modeIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // Live sync: Spades=1, 13th Card=2, Cut to Trump=3, Cut2Trump=4
    public void HostSelectedTrumpMode(int trumpIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "TrumpMode", trumpIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // Live sync: Logic A=1, Logic B=2, Logic C=3
    public void HostSelectedLogicMode(int logicIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "LogicMode", logicIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public void OpenModesPanel() => OpenModesPanelForHost();

    // Backward-compatible alias for Btn_StartPrivateGame
    public void StartPrivateGame() => OpenModesPanelForHost();

    void ResolveModesPanel()
    {
        if (modesPanel == null && ModeManager.Instance != null)
            modesPanel = ModeManager.Instance.panelModes;
    }

    // ==========================================
    // FINAL PLAY BUTTON (MODE SELECT HONE KE BAAD)
    // ==========================================

    public void FinalStartWithSelectedModes()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        if (GameSettings.Instance == null) return;

        ExitGames.Client.Photon.Hashtable customRoomProperties = new ExitGames.Client.Photon.Hashtable();

        if (ModeManager.Instance != null)
        {
            customRoomProperties["TM"] = ModeManager.Instance.currentTrickMode;
            customRoomProperties["RM"] = ModeManager.Instance.currentTrumpMode;
            customRoomProperties["SM"] = ModeManager.Instance.currentSarMode;
            customRoomProperties["LM"] = ModeManager.Instance.currentLogicMode;
        }
        else
        {
            customRoomProperties["TM"] = GameSettings.Instance.taashCategory;
            customRoomProperties["RM"] = 3;
            customRoomProperties["SM"] = GameSettings.Instance.currentSarMode == SarModeType.TwoSar ? 2 : 1;
            customRoomProperties["LM"] = 1;
        }

        customRoomProperties["ModesLocked"] = true;

        PhotonNetwork.CurrentRoom.SetCustomProperties(customRoomProperties);
        PhotonNetwork.CurrentRoom.IsOpen = false;

        if (DeckManager.Instance != null)
            DeckManager.Instance.FillBotsAndStart();

        HidePrivateFriendsLobbyUI();
    }

    void HidePlayWithFriendsLobbyPanel()
    {
        if (pinCreationPanel != null) pinCreationPanel.SetActive(false);
        if (startGameButton != null) startGameButton.SetActive(false);
    }

    public void HidePrivateFriendsLobbyUI()
    {
        if (modesPanel != null) modesPanel.SetActive(false);
        HidePlayWithFriendsLobbyPanel();
        if (errorText != null) errorText.gameObject.SetActive(false);
    }


    // ==========================================
    // 6. FRIENDS LIST LOGIC
    // ==========================================

    public void AddFriend(string friendUserId)
    {
        if (string.IsNullOrEmpty(friendUserId) || myFriends.Contains(friendUserId)) return;

        myFriends.Add(friendUserId);
        SaveFriends();
        Debug.Log(friendUserId + " added to friends!");
    }

    public void CheckFriendsOnlineStatus()
    {
        EnsurePhotonUserId();
        if (myFriends.Count > 0 && PhotonNetwork.IsConnectedAndReady)
            PhotonNetwork.FindFriends(myFriends.ToArray());
    }

    public override void OnFriendListUpdate(List<FriendInfo> friendList)
    {
        foreach (FriendInfo friend in friendList)
        {
            Debug.Log($"Friend: {friend.UserId} | Online: {friend.IsOnline} | Room: {friend.Room}");
        }
    }

    void SaveFriends()
    {
        string data = string.Join(",", myFriends);
        PlayerPrefs.SetString(FriendsPrefsKey, data);
        PlayerPrefs.Save();
    }

    void LoadFriends()
    {
        string data = PlayerPrefs.GetString(FriendsPrefsKey, "");
        if (!string.IsNullOrEmpty(data))
            myFriends = new List<string>(data.Split(','));
    }
}
