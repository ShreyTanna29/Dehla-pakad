using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using DG.Tweening;
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

    // Guards to prevent duplicate start/deal calls from the Mode Panel Start button.
    private bool gameStartInProgress;

    /// <summary>Clears the start guard so a new match can be started after returning home / cancelling.</summary>
    public void ResetStartGuard()
    {
        gameStartInProgress = false;
    }

    public void ScheduleMatchmakingAfterLobby()
    {
        findMatchAfterLobby = true;
        Debug.Log("[Photon] Matchmaking will resume after lobby join");
    }

    public void CancelPendingMatchmaking()
    {
        Debug.Log("[ModeManager] CancelPendingMatchmaking called");
        findMatchAfterLobby = false;
        gameStartInProgress = false;
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
        WireModeButtons(); // New wiring for all mode buttons
        UpdateFriendsOverlay();
        ApplyHomeScreenButtonColors();
        UpdateModeSelectionUIColors();
    }

    void WireModeButtons()
    {
        EnsureUiSearchRoot();
        
        // Trick Modes
        WireButton("Button_Play1Taash", () => OnClick_TrickMode(1));
        WireButton("Button_Play2Taash", () => OnClick_TrickMode(2));
        
        // Sar Modes
        WireButton("Button_Play1Sar", () => OnClick_SarMode(1));
        WireButton("Button_Play2Sar", () => OnClick_SarMode(2));
        
        // Trump Modes
        WireButton("Button_PlayTrumpMode", () => OnClick_TrumpMode(1));
        WireButton("Button_Play13CardMode", () => OnClick_TrumpMode(2));
        // Repurposed: the "First Cut" button now selects Hidden Trump (mode 5) instead of Cut1Trump (mode 3).
        WireButton("Button_PlayFirstCut", () => OnClick_TrumpMode(5));
        WireButton("Button_PlayCut2Trump", () => OnClick_TrumpMode(4));
        
        // Logic Modes
        WireButton("Button_LogicA", () => OnClick_LogicMode(1));
        WireButton("Button_LogicB", () => OnClick_LogicMode(2));
        WireButton("Button_LogicC", () => OnClick_LogicMode(3));

        // Start button
        WireButton("Play", OnClick_FindMatch);
        WireButton("Button_BackToHome", OnClick_BackToHome);
    }

    void WireButton(string name, UnityEngine.Events.UnityAction action)
    {
        if (UiSafeLookup.TryGet(name, out GameObject go) && go != null)
        {
            Button btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(action);
            }
        }
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

    void ResetButtonScales()
    {
        EnsureUiSearchRoot();
        string[] buttonNames =
        {
            "Button_PlayFriends", "Button_Play1Taash", "Button_Play2Taash",
            "Button_PlayTrumpMode", "Button_Play13CardMode", "Button_PlayCut2Trump", "Button_PlayFirstCut",
            "Button_Play1Sar", "Button_Play2Sar", "Button_LogicA", "Button_LogicB",
            "Button_LogicC", "Button_BackToHome", "Play", "Button_InviteFriends"
        };

        foreach (string name in buttonNames)
        {
            if (UiSafeLookup.TryGet(name, out GameObject go) && go != null)
            {
                go.transform.DOKill();
                go.transform.localScale = Vector3.one;
            }
        }
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
            "Button_PlayFirstCut",
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
            currentTrumpMode = PlayerPrefs.GetInt(PrefsTrumpMode, 1);
        if (PlayerPrefs.HasKey(PrefsSarMode))
            currentSarMode = PlayerPrefs.GetInt(PrefsSarMode, 1);
        if (PlayerPrefs.HasKey(PrefsLogicMode))
            currentLogicMode = PlayerPrefs.GetInt(PrefsLogicMode, 1);
        ApplyModesToGameSettings();
    }

    public void SaveSelectedModes()
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
            // NOTE: Button_PlayCut2Trump has been repurposed to select Hidden Trump (mode 5).
            // Cut2Trump (mode 4) still exists in code and remains reachable via room sync / saved prefs.
            case 4: GameSettings.Instance.currentMode = GameModeType.Cut2Trump; break;
            case 5: GameSettings.Instance.currentMode = GameModeType.HiddenTrump; break;
        }
    }

    public void OnClick_PlayBots_Home()
    {
        Debug.Log("[UI] Button Clicked: Play With Bots");
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.isPlayBotsMode = true;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.OfflineBots;

        OpenModePanelInternal();
    }

    public void OnClick_PlayOnline_Home()
    {
        if (!NetworkManager.IsPlayOnlineReady())
        {
            Debug.LogWarning("[UI] Play Online blocked — waiting for internet and Photon lobby.");
            return;
        }

        Debug.Log("[UI] Button Clicked: Play Online");
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.isPlayBotsMode = false;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.OnlinePhoton;

        OpenModePanelInternal();
    }

    public void OpenModePanelFromHome() => OnClick_PlayOnline_Home();

    void OpenModePanelInternal()
    {
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        isFriendsMatchMode = false;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        if (panelHomeScreen != null)
            panelHomeScreen.SetActive(false);

        if (panelModes != null)
        {
            panelModes.SetActive(true);
            panelModes.transform.SetAsLastSibling();
        }

        ResetButtonScales();
        WireCut2TrumpButton();
        UpdateModeSelectionUIColors();
    }

    void EnsureUiSearchRootForModes()
    {
        if (panelModes != null)
            uiSearchRoot = panelModes.transform.root;
        else if (panelHomeScreen != null)
            uiSearchRoot = panelHomeScreen.transform.root;
        else
            uiSearchRoot = transform.root;

        UiSafeLookup.SetSearchRoot(uiSearchRoot);
    }

    public void OnClick_BackToHome()
    {
        Debug.Log("[UI] Button Clicked: Back to Home");
        GameFlowState.SetPhase(GameFlowPhase.Home);
        isFriendsMatchMode = false;
        gameStartInProgress = false;

        if (panelModes != null && panelModes.activeSelf)
            panelModes.SetActive(false);

        if (panelHomeScreen != null && !panelHomeScreen.activeSelf)
            panelHomeScreen.SetActive(true);

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.UpdateUIState(true);

        ResetButtonScales();
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

    public void OnClick_TrickMode(int mode, bool broadcastToRoom = true)
    {
        currentTrickMode = mode;
        SaveSelectedModes();
        UpdateFriendsOverlay();
        UpdateModeSelectionUIColors();

        if (broadcastToRoom && IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedTaashMode(mode);
    }

    public void OnClick_SarMode(int mode, bool broadcastToRoom = true)
    {
        currentSarMode = mode;
        SaveSelectedModes();
        UpdateModeSelectionUIColors();

        if (broadcastToRoom && IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedGameMode(mode);
    }

    public void OnClick_PlayFriends()
    {
        Debug.Log("[UI] Button Clicked: Play With Friends");
        isFriendsMatchMode = true;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.PlayWithFriends;
        UpdateFriendsOverlay();
        ResetButtonScales();
        ApplyHomeScreenButtonColors();
        UpdateModeSelectionUIColors();

        if (panelPlayWithFriends != null)
            panelPlayWithFriends.SetActive(true);
    }

    public void OnClick_ClosePlayWithFriends()
    {
        if (panelPlayWithFriends != null)
            panelPlayWithFriends.SetActive(false);

        ResetButtonScales();
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

    public void OnClick_TrumpMode(int mode, bool broadcastToRoom = true)
    {
        currentTrumpMode = mode;
        SaveSelectedModes();
        UpdateModeSelectionUIColors();

        if (broadcastToRoom && IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedTrumpMode(mode);
    }

    public void OnClick_LogicMode(int mode, bool broadcastToRoom = true)
    {
        currentLogicMode = mode;
        SaveSelectedModes();
        UpdateModeSelectionUIColors();

        if (broadcastToRoom && IsPrivateFriendsHost())
            PlayWithFriendsManager.Instance.HostSelectedLogicMode(mode);
    }

    public void ApplyRemoteSarModeVisual(int mode)
    {
        currentSarMode = mode;
        UpdateModeSelectionUIColors();
    }

    public void ApplyRemoteTaashModeVisual(int mode)
    {
        currentTrickMode = mode;
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
            OnClick_SarMode((int)props["GameMode"], broadcastToRoom: false);
        if (props.ContainsKey("TaashMode"))
            OnClick_TrickMode((int)props["TaashMode"], broadcastToRoom: false);
        if (props.ContainsKey("TrumpMode"))
            OnClick_TrumpMode((int)props["TrumpMode"], broadcastToRoom: false);
        if (props.ContainsKey("LogicMode"))
            OnClick_LogicMode((int)props["LogicMode"], broadcastToRoom: false);
    }

    public void ApplyLiveGameModeButtonIndex(int index)
    {
        switch (index)
        {
            case 1: currentSarMode = 1; break;
            case 2: currentSarMode = 2; break;
            case 3:
                currentTrickMode = 1;
                break;
            case 4:
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
            && !PhotonNetwork.CurrentRoom.IsVisible
            && !PhotonNetwork.OfflineMode;
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
        EnsureUiSearchRootForModes();

        UiSafeLookup.TryGetImage("Button_Play1Taash", out btn1Taash);
        UiSafeLookup.TryGetImage("Button_Play2Taash", out btn2Taash);
        UiSafeLookup.TryGetImage("Button_Play1Sar", out btn1Sar);
        UiSafeLookup.TryGetImage("Button_Play2Sar", out btn2Sar);
        UiSafeLookup.TryGetImage("Button_PlayTrumpMode", out btnPresetTrump);
        UiSafeLookup.TryGetImage("Button_Play13CardMode", out btn13thCard);
        UiSafeLookup.TryGetImage("Button_PlayFirstCut", out btnFirstCut);
        UiSafeLookup.TryGetImage("Button_PlayCut2Trump", out btnCut2Trump);
        UiSafeLookup.TryGetImage("Button_LogicA", out btnLogicA);
        UiSafeLookup.TryGetImage("Button_LogicB", out btnLogicB);
        UiSafeLookup.TryGetImage("Button_LogicC", out btnLogicC);
    }

    void UpdateModeSelectionUIColors()
    {
        ResolveModeButtonImages();

        Color selectedColor = Color.white;
        Color unselectedColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        if (btn1Taash != null)
            btn1Taash.color = currentTrickMode == 1 ? selectedColor : unselectedColor;

        if (btn2Taash != null)
            btn2Taash.color = currentTrickMode == 2 ? selectedColor : unselectedColor;

        if (btn1Sar != null && SarModeSelector.Instance == null)
            btn1Sar.color = currentSarMode == 1 ? selectedColor : unselectedColor;

        if (btn2Sar != null && SarModeSelector.Instance == null)
            btn2Sar.color = currentSarMode == 2 ? selectedColor : unselectedColor;

        if (btnPresetTrump != null)
            btnPresetTrump.color = currentTrumpMode == 1 ? selectedColor : unselectedColor;

        if (btn13thCard != null)
            btn13thCard.color = currentTrumpMode == 2 ? selectedColor : unselectedColor;

        // Button_PlayFirstCut is repurposed to Hidden Trump (mode 5).
        if (btnFirstCut != null)
            btnFirstCut.color = currentTrumpMode == 5 ? selectedColor : unselectedColor;

        if (btnCut2Trump != null)
            btnCut2Trump.color = currentTrumpMode == 4 ? selectedColor : unselectedColor;

        if (btnLogicA != null)
            btnLogicA.color = currentLogicMode == 1 ? selectedColor : unselectedColor;

        if (btnLogicB != null)
            btnLogicB.color = currentLogicMode == 2 ? selectedColor : unselectedColor;

        if (btnLogicC != null)
            btnLogicC.color = currentLogicMode == 3 ? selectedColor : unselectedColor;

        Debug.Log($"[Mode UI] Trick={currentTrickMode} | Trump={currentTrumpMode} | Sar={currentSarMode} | Logic={currentLogicMode}");

        if (SarModeSelector.Instance != null)
            SarModeSelector.Instance.UpdateButtonVisuals();
    }

    // Scene Mode-Panel Start button is wired to OnClick_FindMatch.
    public void OnClick_FindMatch() => StartGameFromModePanel();

    // Backward-compatible alias — always routes through the single clean entry point.
    public void OnModePanelStartClicked() => StartGameFromModePanel();

    /// <summary>
    /// SINGLE clean entry point for the Mode Panel Start button.
    /// Routes to exactly one flow based on match type / private-room state:
    ///   1) Private Friends (in an invisible Photon room) -> PlayWithFriends final start.
    ///   2) Play Bots (offline)                            -> offline/bot start.
    ///   3) Play Online (default)                          -> public matchmaking.
    /// PlayWithFriendsManager NEVER controls Play Online or Play Bots.
    /// </summary>
    public void StartGameFromModePanel()
    {
        Debug.Log("[StartRoute] Mode panel Start clicked");

        bool isPrivateFriends = PhotonNetwork.InRoom
            && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.IsVisible
            && !PhotonNetwork.OfflineMode;
        bool isBots = NetworkManager.Instance != null && NetworkManager.Instance.isPlayBotsMode;
        MatchType matchType = GameSettings.Instance != null
            ? GameSettings.Instance.currentMatchType
            : MatchType.OnlinePhoton;

        Debug.Log($"[StartRoute] MatchType = {(isPrivateFriends ? "PlayWithFriends (private room)" : matchType.ToString())} | isPlayBotsMode={isBots}");

        if (gameStartInProgress)
        {
            Debug.Log("[GameStart] Duplicate start blocked");
            return;
        }

        // Always persist the selected modes before any routing.
        SaveSelectedModes();

        // 1) PRIVATE FRIENDS ROUTE -----------------------------------------
        if (isPrivateFriends)
        {
            Debug.Log("[StartRoute] Private Friends route");
            gameStartInProgress = true;

            if (PlayWithFriendsManager.Instance != null)
            {
                // Host saves modes to room props + RPCs everyone to start together.
                PlayWithFriendsManager.Instance.FinalStartWithSelectedModes();
            }
            else
            {
                Debug.LogError("[StartRoute] PlayWithFriendsManager.Instance missing — cannot start private game.");
                gameStartInProgress = false;
            }
            return;
        }

        // 2) PLAY BOTS ROUTE -----------------------------------------------
        if (isBots || matchType == MatchType.OfflineBots)
        {
            Debug.Log("[StartRoute] Play Bots route");
            gameStartInProgress = true;
            // StartNormalMatchFromModesPanel internally detects bot mode and goes offline.
            StartNormalMatchFromModesPanel();
            return;
        }

        // 3) PLAY ONLINE ROUTE ---------------------------------------------
        Debug.Log("[StartRoute] Play Online route");
        gameStartInProgress = true;
        StartNormalMatchFromModesPanel();
    }

    public void StartNormalMatchFromModesPanel()
    {
        Debug.Log("[UI] Button Clicked: Find Match");

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

        if (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
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

        if (panelModes != null && panelModes.activeSelf)
            UpdateModeSelectionUIColors();

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
        gameStartInProgress = false;
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
        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
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
        gameStartInProgress = false;
        GameFlowState.SetPhase(GameFlowPhase.Matchmaking);
        if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StopSearching(false);
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
    }

}
