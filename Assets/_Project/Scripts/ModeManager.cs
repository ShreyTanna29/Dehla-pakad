using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class ModeManager : MonoBehaviourPunCallbacks
{
    public static ModeManager Instance;

    [Header("UI Panels")]
    public GameObject panelModes; 
    public GameObject panelHomeScreen;
    public GameObject panelPlayWithFriends;
    [Tooltip("Canvas or parent for home/mode buttons. If empty, uses panel root.")]
    public Transform uiSearchRoot;

    [Header("Game Modes Settings")]
    public int currentTrickMode = 1;
    public int currentTrumpMode = 3;
    public int currentSarMode = 1;
    public int currentLogicMode = 1;

    [Header("UI References")]
    public Image btn1Taash;
    public Image btn2Taash;
    public Image btn1Sar;
    public Image btn2Sar;
    public Image btnFriends;
    public Image btnPresetTrump;
    public Image btn13thCard;
    public Image btnFirstCut;
    public Image btnCut2Trump;
    public Image btnLogicA;
    public Image btnLogicB;
    public Image btnLogicC;

    private bool findMatchAfterLobby = false;
    private bool isFriendsMatchMode = false;

    public void ScheduleMatchmakingAfterLobby()
    {
        findMatchAfterLobby = true;
        Debug.Log("[Photon] Matchmaking will resume after lobby join");
    }

    public void CancelPendingMatchmaking()
    {
        Debug.Log("[ModeManager] CancelPendingMatchmaking called");
        findMatchAfterLobby = false;
    }

    const string PrefsTrickMode = "DehlaPakad_TrickMode";
    const string PrefsTrumpMode = "DehlaPakad_TrumpMode";
    const string PrefsSarMode = "DehlaPakad_SarMode";
    const string PrefsLogicMode = "DehlaPakad_LogicMode";

    void EnsureUiSearchRoot()
    {
        if (uiSearchRoot != null) return;
        if (panelHomeScreen != null)
            uiSearchRoot = panelHomeScreen.transform.root;
        else if (panelModes != null)
            uiSearchRoot = panelModes.transform.root;
        else
            uiSearchRoot = transform.root;
        UiSafeLookup.SetSearchRoot(uiSearchRoot);
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        EnsureUiSearchRoot();
        RestoreSavedModes();
        SetupModeButtonHoverEffects();
        WirePlayFriendsButton();
        WireCut2TrumpButton();
        UpdateFriendsOverlay();
        ApplyHomeScreenButtonColors();
        UpdateModeSelectionUIColors();
    }

    void WirePlayFriendsButton()
    {
        EnsureUiSearchRoot();
        if (!UiSafeLookup.TryGet("Button_PlayFriends", out GameObject go) || go == null) return;

        if (btnFriends == null)
            btnFriends = go.GetComponent<Image>();

        Button btn = go.GetComponent<Button>();
        if (btn == null) return;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnClick_PlayFriends);
    }

    void SetupModeButtonHoverEffects()
    {
        EnsureUiSearchRoot();
        string[] buttonNames =
        {
            "Button_PlayFriends",
            "Button_Play1Taash",
            "Button_Play2Taash",
            "Button_PlayTrumpMode",
            "Button_Play13CardMode",
            "Button_PlayCut2Trump",
            "Button_Play1Sar",
            "Button_Play2Sar",
            "Button_LogicA",
            "Button_LogicB",
            "Button_LogicC",
            "Button_BackToHome",
            "Play",
            "Button_InviteFriends"
        };

        foreach (string name in buttonNames)
        {
            if (!UiSafeLookup.TryGet(name, out GameObject go) || go == null) continue;
            Button btn = go.GetComponent<Button>();
            if (btn != null)
                UIButtonHoverUtility.SetupHoverScale(btn);
        }
    }

    void RestoreSavedModes()
    {
        if (PlayerPrefs.HasKey(PrefsTrickMode))
            currentTrickMode = PlayerPrefs.GetInt(PrefsTrickMode, 1);
        if (PlayerPrefs.HasKey(PrefsTrumpMode))
            currentTrumpMode = PlayerPrefs.GetInt(PrefsTrumpMode, 3);
        if (PlayerPrefs.HasKey(PrefsSarMode))
            currentSarMode = PlayerPrefs.GetInt(PrefsSarMode, 1);
        if (PlayerPrefs.HasKey(PrefsLogicMode))
            currentLogicMode = PlayerPrefs.GetInt(PrefsLogicMode, 1);
        ApplyModesToGameSettings();
    }

    void SaveSelectedModes()
    {
        PlayerPrefs.SetInt(PrefsTrickMode, currentTrickMode);
        PlayerPrefs.SetInt(PrefsTrumpMode, currentTrumpMode);
        PlayerPrefs.SetInt(PrefsSarMode, currentSarMode);
        PlayerPrefs.SetInt(PrefsLogicMode, currentLogicMode);
        PlayerPrefs.Save();
        ApplyModesToGameSettings();
        Debug.Log($"[GameFlow] Modes saved TM={currentTrickMode} RM={currentTrumpMode} SM={currentSarMode} LM={currentLogicMode}");
    }

    void ApplyModesToGameSettings()
    {
        if (GameSettings.Instance == null) return;
        GameSettings.Instance.taashCategory = currentTrickMode;
        GameSettings.Instance.currentSarMode = currentSarMode == 2 ? SarModeType.TwoSar : SarModeType.OneSar;
        switch (currentTrumpMode)
        {
            case 1: GameSettings.Instance.currentMode = GameModeType.TrumpSpades; break;
            case 2: GameSettings.Instance.currentMode = GameModeType.ThirteenthCardTrump; break;
            case 3: GameSettings.Instance.currentMode = GameModeType.Cut1Trump; break;
            case 4: GameSettings.Instance.currentMode = GameModeType.Cut2Trump; break;
        }
    }

    public void OpenModePanelFromHome()
    {
        Debug.Log("[UI] Button Clicked: Open Modes (no loading screen)");
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        if (panelHomeScreen != null && panelHomeScreen.activeSelf) panelHomeScreen.SetActive(false);
        if (panelModes != null && !panelModes.activeSelf) panelModes.SetActive(true);

        SetupModeButtonHoverEffects();
        WireCut2TrumpButton();
        UpdateModeSelectionUIColors();
    }

    public void OnClick_BackToHome()
    {
        Debug.Log("[UI] Button Clicked: Back to Home");
        GameFlowState.SetPhase(GameFlowPhase.Home);

        if (panelModes != null && panelModes.activeSelf) panelModes.SetActive(false);
        if (panelHomeScreen != null && !panelHomeScreen.activeSelf) panelHomeScreen.SetActive(true);

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.UpdateUIState(true);

        ApplyHomeScreenButtonColors();
    }

    public void ApplyHomeScreenButtonColors()
    {
        Color bright = Color.white;
        SetButtonImageColor("Button_PlayOnline", bright);
        SetButtonImageColor("Button_PlayBots", bright);
        SetButtonImageColor("Button_PlayFriends", bright);
        SetButtonImageColor("Button_InviteFriends", bright);
        SetButtonImageColor("Button_Shop", bright);
        SetButtonImageColor("Button_Settings", bright);
        SetButtonImageColor("Button_Share", bright);
        SetButtonImageColor("Button_NoADS", bright);
    }

    void SetButtonImageColor(string objectName, Color color)
    {
        EnsureUiSearchRoot();
        if (!UiSafeLookup.TryGetImage(objectName, out Image img) || img == null) return;
        img.color = color;
    }

    public void OnClick_TrickMode(int mode)
    {
        isFriendsMatchMode = false;
        currentTrickMode = mode;
        SaveSelectedModes();
        UpdateFriendsOverlay();
        UpdateModeSelectionUIColors();

        if (IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedGameMode(mode == 1 ? 3 : 4);
    }

    public void OnClick_SarMode(int mode)
    {
        currentSarMode = mode;
        SaveSelectedModes();
        UpdateModeSelectionUIColors();

        if (IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedGameMode(mode);
    }

    public void OnClick_PlayFriends()
    {
        Debug.Log("[UI] Button Clicked: Play With Friends");
        isFriendsMatchMode = true;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.PlayWithFriends;
        UpdateFriendsOverlay();
        ApplyHomeScreenButtonColors();
        UpdateModeSelectionUIColors();

        if (panelPlayWithFriends != null)
            panelPlayWithFriends.SetActive(true);
    }

    public void OnClick_ClosePlayWithFriends()
    {
        if (panelPlayWithFriends != null)
            panelPlayWithFriends.SetActive(false);
    }

    void UpdateFriendsOverlay()
    {
        EnsureUiSearchRoot();
        if (!UiSafeLookup.TryGet("Button_PlayFriends", out GameObject friendsBtn) || friendsBtn == null) return;

        Transform overlay = friendsBtn.transform.Find("PlayFriends");
        if (overlay == null) return;
        if (overlay.gameObject.activeSelf != isFriendsMatchMode)
            overlay.gameObject.SetActive(isFriendsMatchMode);
    }

    public void OnClick_TrumpMode(int mode)
    {
        currentTrumpMode = mode;
        SaveSelectedModes();
        UpdateModeSelectionUIColors();

        if (IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedTrumpMode(mode);
    }

    public void OnClick_LogicMode(int mode)
    {
        currentLogicMode = mode;
        SaveSelectedModes();
        UpdateModeSelectionUIColors();

        if (IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedLogicMode(mode);
    }

    public void ApplyRemoteSarModeVisual(int mode)
    {
        currentSarMode = mode;
        UpdateModeSelectionUIColors();
    }

    public void ApplyRemoteTrumpModeVisual(int mode)
    {
        currentTrumpMode = mode;
        UpdateModeSelectionUIColors();
    }

    public void ApplyRemoteLogicModeVisual(int mode)
    {
        currentLogicMode = mode;
        UpdateModeSelectionUIColors();
    }

    public void ApplyLiveModesFromRoomIfPresent()
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        if (props.ContainsKey("GameMode"))
            ApplyLiveGameModeButtonIndex((int)props["GameMode"]);
        if (props.ContainsKey("TrumpMode"))
            ApplyRemoteTrumpModeVisual((int)props["TrumpMode"]);
        if (props.ContainsKey("LogicMode"))
            ApplyRemoteLogicModeVisual((int)props["LogicMode"]);
    }

    public void ApplyLiveGameModeButtonIndex(int index)
    {
        switch (index)
        {
            case 1: currentSarMode = 1; break;
            case 2: currentSarMode = 2; break;
            case 3:
                isFriendsMatchMode = false;
                currentTrickMode = 1;
                break;
            case 4:
                isFriendsMatchMode = false;
                currentTrickMode = 2;
                break;
            default:
                Debug.LogWarning($"[Live Sync] Unknown GameMode index: {index}");
                return;
        }
        UpdateModeSelectionUIColors();
    }

    static bool IsPrivateFriendsHost()
    {
        return PlayWithFriendsManager.Instance != null
            && PhotonNetwork.InRoom
            && PhotonNetwork.IsMasterClient
            && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.IsVisible;
    }

    void WireCut2TrumpButton()
    {
        EnsureUiSearchRoot();
        if (!UiSafeLookup.TryGet("Button_PlayCut2Trump", out GameObject go) || go == null) return;

        if (btnCut2Trump == null)
            btnCut2Trump = go.GetComponent<Image>();

        Button btn = go.GetComponent<Button>();
        if (btn == null) return;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OnClick_TrumpMode(4));
    }

    void ResolveModeButtonImages()
    {
        EnsureUiSearchRoot();

        if (btn1Taash == null)
            UiSafeLookup.TryGetImage("Button_Play1Taash", out btn1Taash);

        if (btn2Taash == null)
            UiSafeLookup.TryGetImage("Button_Play2Taash", out btn2Taash);

        if (btn1Sar == null)
            UiSafeLookup.TryGetImage("Button_Play1Sar", out btn1Sar);

        if (btn2Sar == null)
            UiSafeLookup.TryGetImage("Button_Play2Sar", out btn2Sar);

        if (btnPresetTrump == null)
            UiSafeLookup.TryGetImage("Button_PlayTrumpMode", out btnPresetTrump);

        if (btn13thCard == null)
            UiSafeLookup.TryGetImage("Button_Play13CardMode", out btn13thCard);

        if (btnFirstCut == null)
            UiSafeLookup.TryGetImage("Button_PlayFirstCut", out btnFirstCut);

        if (btnCut2Trump == null)
            UiSafeLookup.TryGetImage("Button_PlayCut2Trump", out btnCut2Trump);

        if (btnLogicA == null)
            UiSafeLookup.TryGetImage("Button_LogicA", out btnLogicA);

        if (btnLogicB == null)
            UiSafeLookup.TryGetImage("Button_LogicB", out btnLogicB);

        if (btnLogicC == null)
            UiSafeLookup.TryGetImage("Button_LogicC", out btnLogicC);
    }

    void UpdateModeSelectionUIColors()
    {
        ResolveModeButtonImages();

        Color selectedColor = Color.white;
        Color unselectedColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        if (btn1Taash != null)
            btn1Taash.color = !isFriendsMatchMode && currentTrickMode == 1 ? selectedColor : unselectedColor;

        if (btn2Taash != null)
            btn2Taash.color = !isFriendsMatchMode && currentTrickMode == 2 ? selectedColor : unselectedColor;

        if (btn1Sar != null && SarModeSelector.Instance == null)
            btn1Sar.color = currentSarMode == 1 ? selectedColor : unselectedColor;

        if (btn2Sar != null && SarModeSelector.Instance == null)
            btn2Sar.color = currentSarMode == 2 ? selectedColor : unselectedColor;

        if (btnPresetTrump != null)
            btnPresetTrump.color = currentTrumpMode == 1 ? selectedColor : unselectedColor;

        if (btn13thCard != null)
            btn13thCard.color = currentTrumpMode == 2 ? selectedColor : unselectedColor;

        if (btnFirstCut != null)
            btnFirstCut.color = currentTrumpMode == 3 ? selectedColor : unselectedColor;

        if (btnCut2Trump != null)
            btnCut2Trump.color = currentTrumpMode == 4 ? selectedColor : unselectedColor;

        if (btnLogicA != null)
            btnLogicA.color = currentLogicMode == 1 ? selectedColor : unselectedColor;

        if (btnLogicB != null)
            btnLogicB.color = currentLogicMode == 2 ? selectedColor : unselectedColor;

        if (btnLogicC != null)
            btnLogicC.color = currentLogicMode == 3 ? selectedColor : unselectedColor;

        Debug.Log($"[Mode UI] Trump Mode={currentTrumpMode} | Logic Mode={currentLogicMode} | Cut2Trump image assigned={btnCut2Trump != null}");

        if (SarModeSelector.Instance != null)
            SarModeSelector.Instance.UpdateButtonVisuals();
    }

    public void OnClick_FindMatch()
    {
        Debug.Log("[UI] Button Clicked: Find Match");
        SaveSelectedModes();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        bool isBots = NetworkManager.Instance != null && NetworkManager.Instance.isPlayBotsMode;
        if (GameSettings.Instance != null)
        {
            if (isBots)
                GameSettings.Instance.currentMatchType = MatchType.OfflineBots;
            else if (isFriendsMatchMode)
                GameSettings.Instance.currentMatchType = MatchType.PlayWithFriends;
            else
                GameSettings.Instance.currentMatchType = MatchType.OnlinePhoton;
        }

        if (panelModes != null) panelModes.SetActive(false);

        if (isBots)
        {
            Debug.Log("[Bot Mode] Skipping Photon matchmaking — offline instant start");
            GameFlowState.SetPhase(GameFlowPhase.InRoom);

            if (NetworkManager.Instance != null)
                NetworkManager.Instance.StartOfflineMatchRequest();
            else
                StartLocalMatch();
            return;
        }

        GameFlowState.SetPhase(GameFlowPhase.Matchmaking);

        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.StartSearching();

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.Log("[Photon] Not connected — connecting then matchmaking");
            findMatchAfterLobby = true;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ConnectToPhoton();
            return;
        }

        StartSmartMatchmaking();
    }

    public void StartLocalMatch()
    {
        Debug.Log("[Bot Mode] Attempt Create Room (offline)");
        PhotonNetwork.OfflineMode = true;

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.UpdateUIState(false);
            NetworkManager.Instance.HideLoading();
        }

        string roomName = "Local_Bot_" + Random.Range(1000, 9999);
        PhotonNetwork.CreateRoom(roomName, BuildRoomOptions());
    }

    public void StartSmartMatchmakingFromNetwork()
    {
        StartSmartMatchmaking();
    }

    void StartSmartMatchmaking()
    {
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[ModeManager] StartSmartMatchmaking blocked because user cancelled");
            findMatchAfterLobby = false;
            return;
        }

        if (!PhotonNetwork.InLobby)
        {
            Debug.Log("[Photon] Attempt Join Lobby (before matchmaking)");
            findMatchAfterLobby = true;
            PhotonNetwork.JoinLobby();
            return;
        }

        findMatchAfterLobby = false;

        if (isFriendsMatchMode)
        {
            string roomName = "Friends_" + currentTrickMode + "_" + currentTrumpMode + "_" + currentSarMode + "_" + Random.Range(1000, 9999);
            Debug.Log($"[Photon] Friends room — create {roomName}");
            PhotonNetwork.CreateRoom(roomName, BuildRoomOptions(friendsRoom: true));
            return;
        }

        Debug.Log("[Photon] Attempt Join Room (JoinRandomRoom)");
        Hashtable expected = new Hashtable { { "TM", currentTrickMode }, { "RM", currentTrumpMode }, { "SM", currentSarMode } };
        PhotonNetwork.JoinRandomRoom(expected, 4);
    }

    RoomOptions BuildRoomOptions(bool friendsRoom = false)
    {
        Hashtable roomProperties = new Hashtable { { "TM", currentTrickMode }, { "RM", currentTrumpMode }, { "SM", currentSarMode } };
        return new RoomOptions
        {
            MaxPlayers = 4,
            IsOpen = true,
            IsVisible = !PhotonNetwork.OfflineMode && !friendsRoom,
            CustomRoomProperties = roomProperties,
            CustomRoomPropertiesForLobby = new string[] { "TM", "RM", "SM" },
            PlayerTtl = 30000,
            EmptyRoomTtl = 30000
        };
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("[Photon] JoinedLobby (ModeManager)");
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[Photon] JoinedLobby ignored because user cancelled matchmaking");
            findMatchAfterLobby = false;
            return;
        }

        if (findMatchAfterLobby)
            StartSmartMatchmaking();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log($"[Photon] JoinRandomFailed | {returnCode} | {message}");
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[Photon] JoinRandomFailed ignored because user cancelled matchmaking");
            findMatchAfterLobby = false;
            return;
        }

        Debug.Log("[Photon] Attempt Create Room");
        PhotonNetwork.CreateRoom("Room_" + Random.Range(1000, 9999), BuildRoomOptions());
    }

    public override void OnCreatedRoom()
    {
        Debug.Log($"[Photon] CreatedRoom | {PhotonNetwork.CurrentRoom?.Name}");
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[Photon] CreateRoomFailed | {returnCode} | {message}");
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            GameFlowState.SetPhase(GameFlowPhase.Home, true);
            MatchmakingManager.Instance.StopSearching(false);
            return;
        }

        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StopSearching(false);
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
    }

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
        {
            bool rejoining = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs1) && (bool)gs1;
            if (!rejoining)
            {
                Debug.Log("Private Room Joined. Waiting in Lobby...");
                GameFlowState.SetPhase(GameFlowPhase.InRoom);
                return;
            }
        }

        Debug.Log($"[Photon] Joined Room | {PhotonNetwork.CurrentRoom?.Name} | Players: {PhotonNetwork.CurrentRoom?.PlayerCount}/4");

        if (PhotonNetwork.IsMasterClient)
        {
            SaveSelectedModes();
        }
        else
        {
            SyncModesFromRoom();

            PhotonView pv = GetComponent<PhotonView>();
            if (pv != null)
                pv.RPC(nameof(RPC_RequestGameStateSync), RpcTarget.MasterClient);
        }

        bool matchInProgress = PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs2)
            && (bool)gs2;

        GameFlowState.SetPhase(
            matchInProgress ? GameFlowPhase.InGame : GameFlowPhase.InRoom,
            forceRecovery: matchInProgress);
    }

    [PunRPC]
    void RPC_RequestGameStateSync(PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null) return;

        PhotonView pv = GetComponent<PhotonView>();
        if (pv == null) return;

        int trickMode = currentTrickMode;
        int trumpMode = currentTrumpMode;
        int sarMode = currentSarMode;
        if (PhotonNetwork.CurrentRoom != null)
        {
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("TM"))
                trickMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["TM"];
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("RM"))
                trumpMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["RM"];
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("SM"))
                sarMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["SM"];
        }

        pv.RPC(nameof(RPC_ReceiveGameStateSync), info.Sender, trickMode, trumpMode, sarMode);
    }

    [PunRPC]
    void RPC_ReceiveGameStateSync(int trickMode, int trumpMode, int sarMode)
    {
        currentTrickMode = trickMode;
        currentTrumpMode = trumpMode;
        currentSarMode = sarMode;
        ApplyModesToGameSettings();
        UpdateModeSelectionUIColors();

        if (TrumpManager.Instance != null)
            TrumpManager.ApplyTrumpForCurrentGameMode(false);

        Debug.Log($"[Sync] Mode synced: TM={trickMode} RM={trumpMode} SM={sarMode} -> {GameSettings.Instance?.currentMode}");

        bool matchInProgress = PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs)
            && (bool)gs;

        GameFlowState.SetPhase(
            matchInProgress ? GameFlowPhase.InGame : GameFlowPhase.InRoom,
            forceRecovery: matchInProgress);
    }

    public void SyncModesFromRoom()
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("TM"))
            currentTrickMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["TM"];
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("RM"))
            currentTrumpMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["RM"];
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("SM"))
            currentSarMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["SM"];
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("LM"))
            currentLogicMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["LM"];
        ApplyModesToGameSettings();
        UpdateModeSelectionUIColors();
        Debug.Log($"[Photon] Synced modes from room TM={currentTrickMode} RM={currentTrumpMode} SM={currentSarMode} LM={currentLogicMode}");
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[Photon] JoinRoomFailed | {returnCode} | {message}");
        GameFlowState.SetPhase(GameFlowPhase.Matchmaking);
        if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StopSearching(false);
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.IsVisible) return;

        if (propertiesThatChanged.ContainsKey("ModesLocked")
            && propertiesThatChanged["ModesLocked"] is bool locked
            && locked)
        {
            SyncModesFromRoom();
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.HidePrivateFriendsLobbyUI();
            if (TrumpManager.Instance != null)
                TrumpManager.ApplyTrumpForCurrentGameMode(false);
            Debug.Log("Host locked the modes!");
            return;
        }

        if (propertiesThatChanged.ContainsKey("GameMode"))
        {
            int selectedMode = (int)propertiesThatChanged["GameMode"];
            ApplyLiveGameModeButtonIndex(selectedMode);
            Debug.Log("Live Sync: Game Mode changed to ID " + selectedMode);
        }

        if (propertiesThatChanged.ContainsKey("TrumpMode"))
        {
            int selectedTrump = (int)propertiesThatChanged["TrumpMode"];
            ApplyRemoteTrumpModeVisual(selectedTrump);
            Debug.Log("Live Sync: Trump Mode changed to ID " + selectedTrump);
        }

        if (propertiesThatChanged.ContainsKey("LogicMode"))
        {
            int selectedLogic = (int)propertiesThatChanged["LogicMode"];
            ApplyRemoteLogicModeVisual(selectedLogic);
            Debug.Log("Live Sync: Logic Mode changed to ID " + selectedLogic);
        }
    }
}
