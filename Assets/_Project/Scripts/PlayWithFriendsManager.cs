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

    [Header("Live Player List UI")]
    public TMP_Text[] playerSlotsText;

    [Header("Toggle Bot Settings")]
    public GameObject includeBotsButton;
    public TMP_Text includeBotsBtnText;
    bool areBotsIncluded;

    [Header("Game Table UI")]
    public GameObject homeMenuPanel;
    public GameObject gameTablePanel;

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
        EnsureNickname();
        EnsurePhotonView();
        PhotonNetwork.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        // Keep receiving room property updates while this panel is hidden.
    }

    void EnsureNickname()
    {
        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
        {
            PhotonNetwork.NickName = "Player_" + Random.Range(100, 999);
            Debug.Log("My Random Name Set To: " + PhotonNetwork.NickName);
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
        PhotonNetwork.RemoveCallbackTarget(this);
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);
        if (startGameButton != null) startGameButton.SetActive(false);
        if (clientWaitingText != null) clientWaitingText.gameObject.SetActive(false);
        if (includeBotsButton != null) includeBotsButton.SetActive(false);
        ClearPlayerListUI();
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

        if (!PhotonNetwork.IsConnectedAndReady || (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer))
        {
            if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToNameServer)
            {
                 Debug.Log("[Photon] Still on NameServer. Waiting...");
                 ShowUIError("Connecting to Master Server...");
                 return;
            }
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

        if (pinCreationPanel != null)
        {
            pinCreationPanel.SetActive(true);
            pinCreationPanel.transform.SetAsLastSibling();
        }
        if (generatedPinText != null) generatedPinText.text = "Room PIN: " + PhotonNetwork.CurrentRoom.Name;
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (modesPanel != null && !PhotonNetwork.IsMasterClient)
            modesPanel.SetActive(false);

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj))
            ApplyBotsIncludedState((bool)botsObj);
        else
            ApplyBotsIncludedState(false);

        UpdatePlayerListUI();
        CheckPlayerCountAndToggleStart();
    }

    public void ToggleBots()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

        bool newState = !areBotsIncluded;
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
        props["BotsIncluded"] = newState;
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    void ApplyBotsIncludedState(bool included)
    {
        areBotsIncluded = included;
        if (includeBotsBtnText != null)
            includeBotsBtnText.text = areBotsIncluded ? "Remove Bots" : "Include Bots";
    }

    void UpdatePlayerListUI()
    {
        if (!PhotonNetwork.InRoom || playerSlotsText == null || playerSlotsText.Length == 0) return;

        Player[] currentPlayers = PhotonNetwork.PlayerList;
        int realPlayerCount = currentPlayers.Length;

        for (int i = 0; i < playerSlotsText.Length; i++)
        {
            if (playerSlotsText[i] == null) continue;

            if (i < realPlayerCount)
            {
                string hostTag = currentPlayers[i].IsMasterClient ? " (Host)" : "";
                playerSlotsText[i].text = currentPlayers[i].NickName + hostTag;
                playerSlotsText[i].color = Color.white;
            }
            else if (areBotsIncluded)
            {
                playerSlotsText[i].text = realPlayerCount == 3 && i == realPlayerCount
                    ? "DehlaBot"
                    : "AI Bot " + (i - realPlayerCount + 1);
                playerSlotsText[i].color = new Color(0.4f, 1f, 0.4f, 1f);
            }
            else
            {
                playerSlotsText[i].text = "Waiting for Friend...";
                playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
            }
        }
    }

    void ClearPlayerListUI()
    {
        if (playerSlotsText == null) return;

        for (int i = 0; i < playerSlotsText.Length; i++)
        {
            if (playerSlotsText[i] == null) continue;
            playerSlotsText[i].text = "Waiting for Friend...";
            playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
        }
    }

    void CheckPlayerCountAndToggleStart()
    {
        if (startGameButton == null)
            UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        if (includeBotsButton != null)
            includeBotsButton.SetActive(PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount < DeckManager.MaxTableSeats);

        if (!PhotonNetwork.IsMasterClient)
        {
            if (startGameButton != null) startGameButton.SetActive(false);
            return;
        }

        if (startGameButton == null) return;

        bool canStart = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;
        startGameButton.SetActive(canStart);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible) return;

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats && areBotsIncluded)
        {
            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
            {
                { "BotsIncluded", false }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        ShowPrivateRoomLobbyUI();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
        {
            UpdatePlayerListUI();
            CheckPlayerCountAndToggleStart();
        }
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

    // Live sync: 1 Sar=1, 2 Sar=2
    public void HostSelectedGameMode(int modeIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "GameMode", modeIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // Live sync: 1 Taash=1, 2 Taash=2
    public void HostSelectedTaashMode(int taashIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "TaashMode", taashIndex }
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

    void ResolveHomeMenuPanel()
    {
        if (homeMenuPanel != null) return;
        if (NetworkManager.Instance != null)
            homeMenuPanel = NetworkManager.Instance.homeMenuPanel;
        else if (ModeManager.Instance != null)
            homeMenuPanel = ModeManager.Instance.panelHomeScreen;
    }

    void ResolveGameTablePanel()
    {
        if (gameTablePanel != null) return;
        if (NetworkManager.Instance != null)
            gameTablePanel = NetworkManager.Instance.gameTablePanel;
    }

    // ==========================================
    // TRAFFIC POLICE: MASTER START BUTTON ROUTER
    // ==========================================

    // The Mode Panel Start button must ALWAYS go through the single clean router in ModeManager.
    // PlayWithFriendsManager must never decide Play Online / Play Bots routing itself.
    public void OnModePanelStartClicked()
    {
        if (ModeManager.Instance != null)
            ModeManager.Instance.StartGameFromModePanel();
        else
            Debug.LogError("[StartRoute] ModeManager.Instance missing — cannot route Mode Panel Start.");
    }

    public void OnStartButtonClick() => OnModePanelStartClicked();

    // ==========================================
    // FINAL CONFIRM & PLAY (HOST PRESSES START ON MODES PANEL)
    // ==========================================

    public void FinalStartWithSelectedModes()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        if (GameSettings.Instance == null) return;

        Debug.Log("Host pressed Final Start! Telling everyone to start the game...");

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
        customRoomProperties["BotsIncluded"] = areBotsIncluded;
        PhotonNetwork.CurrentRoom.SetCustomProperties(customRoomProperties);

        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        photonView.RPC(nameof(RPC_StartGameForEveryone), RpcTarget.All);
    }

    [PunRPC]
    void RPC_StartGameForEveryone()
    {
        Debug.Log("[GameStart] Friends RPC_StartGameForEveryone received");

        ResolveModesPanel();
        if (modesPanel != null) modesPanel.SetActive(false);
        HidePrivateFriendsLobbyUI();

        if (ModeManager.Instance != null)
            ModeManager.Instance.SyncModesFromRoom();

        if (TrumpManager.Instance != null)
            TrumpManager.ApplyTrumpForCurrentGameMode(false);

        DeckManager.botActorNumbers.Clear();

        if (PhotonNetwork.IsMasterClient)
        {
            int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
            int botsNeeded = DeckManager.MaxTableSeats - realPlayerCount;

            for (int i = 0; i < botsNeeded; i++)
                DeckManager.botActorNumbers.Add(100 + i);

            Debug.Log($"[Bot System] {botsNeeded} Bots added for private match!");

            if (botsNeeded > 0 && DeckManager.Instance != null)
            {
                DeckManager.Instance.photonView.RPC(
                    "RPC_SyncBotsOnly",
                    RpcTarget.All,
                    DeckManager.botActorNumbers.ToArray());
            }
        }

        // Single shared, guarded entry: hides menus, shows game scene, ensures local
        // NetworkPlayer exists, initializes gameplay UI, and (master only) starts dealing once.
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.BeginGameAfterRoomReady();
        else
            Debug.LogError("[GameStart ERROR] NetworkManager.Instance missing — cannot start friends game.");

        // Hide this lobby manager LAST so the work above runs while it is still active.
        gameObject.SetActive(false);
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

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.IsVisible) return;

        if (propertiesThatChanged.ContainsKey("ModesLocked")
            && propertiesThatChanged["ModesLocked"] is bool locked
            && locked)
        {
            if (ModeManager.Instance != null)
                ModeManager.Instance.SyncModesFromRoom();
            HidePrivateFriendsLobbyUI();
            if (TrumpManager.Instance != null)
                TrumpManager.ApplyTrumpForCurrentGameMode(false);
            Debug.Log("Host locked the modes!");
            return;
        }

        if (ModeManager.Instance == null) return;

        if (propertiesThatChanged.ContainsKey("GameMode"))
        {
            int selectedMode = (int)propertiesThatChanged["GameMode"];
            ModeManager.Instance.OnClick_SarMode(selectedMode, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("TrumpMode"))
        {
            int selectedTrump = (int)propertiesThatChanged["TrumpMode"];
            ModeManager.Instance.OnClick_TrumpMode(selectedTrump, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("TaashMode"))
        {
            int selectedTaash = (int)propertiesThatChanged["TaashMode"];
            ModeManager.Instance.OnClick_TrickMode(selectedTaash, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("LogicMode"))
        {
            int selectedLogic = (int)propertiesThatChanged["LogicMode"];
            ModeManager.Instance.OnClick_LogicMode(selectedLogic, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("BotsIncluded"))
        {
            ApplyBotsIncludedState((bool)propertiesThatChanged["BotsIncluded"]);
            UpdatePlayerListUI();
            CheckPlayerCountAndToggleStart();
        }
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
