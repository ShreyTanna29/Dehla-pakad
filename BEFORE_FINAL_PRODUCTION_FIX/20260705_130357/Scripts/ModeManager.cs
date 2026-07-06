using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class ModeManager : MonoBehaviourPunCallbacks
{
    public static ModeManager Instance;

    // Shared UI tints for modes panel (hex: E3DED7 / 6D360D)
    public static readonly Color ModeSelectedColor = new Color(0xE3 / 255f, 0xDE / 255f, 0xD7 / 255f, 1f);
    public static readonly Color ModeUnselectedColor = new Color(0x6D / 255f, 0x36 / 255f, 0x0D / 255f, 1f);

    [Header("UI Panels")]
    public GameObject panelModes; 
    public GameObject panelHomeScreen;
    public GameObject panelPlayWithFriends;
    [Tooltip("Canvas or parent for home/mode buttons. If empty, uses panel root.")]
    public Transform uiSearchRoot;

    [Header("Game Modes Settings")]
    public int currentTrickMode = 1;
    public int currentTrumpMode = 1;
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

    [Header("Sliding Toggles")]
    public SlidingModeToggle deckToggle;
    public SlidingModeToggle handsToggle;

    private bool findMatchAfterLobby = false;
    private bool _pendingMatchmakingAfterLeave;
    private bool isFriendsMatchMode = false;

    /// <summary>True while the user is in the Play With Friends / 2v2 flow.</summary>
    public bool IsFriendsMatchMode => isFriendsMatchMode;

    public void SetFriendsMatchMode(bool enabled) => isFriendsMatchMode = enabled;

    // Guards to prevent duplicate start/deal calls from the Mode Panel Start button.
    private bool gameStartInProgress;
    Coroutine _connectionBufferRoutine;

    /// <summary>Clears the start guard so a new match can be started after returning home / cancelling.</summary>
    public void ResetStartGuard()
    {
        gameStartInProgress = false;
        if (_connectionBufferRoutine != null)
        {
            StopCoroutine(_connectionBufferRoutine);
            _connectionBufferRoutine = null;
        }
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
        bool hasSavedModes = PlayerPrefs.HasKey(PrefsTrickMode)
            || PlayerPrefs.HasKey(PrefsTrumpMode)
            || PlayerPrefs.HasKey(PrefsSarMode)
            || PlayerPrefs.HasKey(PrefsLogicMode);

        if (!hasSavedModes)
        {
            ApplyDefaultModes();
        }
        else
        {
            if (PlayerPrefs.HasKey(PrefsTrickMode))
                currentTrickMode = PlayerPrefs.GetInt(PrefsTrickMode, 1);
            if (PlayerPrefs.HasKey(PrefsTrumpMode))
                currentTrumpMode = PlayerPrefs.GetInt(PrefsTrumpMode, 1);
            if (PlayerPrefs.HasKey(PrefsSarMode))
                currentSarMode = PlayerPrefs.GetInt(PrefsSarMode, 1);
            if (PlayerPrefs.HasKey(PrefsLogicMode))
                currentLogicMode = PlayerPrefs.GetInt(PrefsLogicMode, 1);
        }

        ApplyModesToGameSettings();
    }

    void ApplyDefaultModes()
    {
        currentTrickMode = 1;   // 1 Taash
        currentTrumpMode = 1;     // Trump Spades
        currentSarMode = 1;       // 1 Hands (Sir)
        currentLogicMode = 1;
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
        PrepareForNewModeFromMenu();

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

        Debug.Log("[UI] PlayOnline clicked");
        PrepareForNewModeFromMenu();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.isPlayBotsMode = false;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.OnlinePhoton;

        isFriendsMatchMode = false;

        ForceHideHomeUi();
        OpenModePanelInternal(false);
    }

    void PrepareForNewModeFromMenu()
    {
        ResetStartGuard();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ResetGameStartGuards();

        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: false);

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.CancelPinJoinUiState();

        if (DeckManager.Instance != null)
            DeckManager.Instance.PrepareForNewMatchFromMenu();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideAllMenuOverlays();
    }

    void ForceHideHomeUi()
    {
        SetPanelVisible(panelHomeScreen, false);
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.HideHomeMenuCanvas();
            NetworkManager.Instance.HideGamePanelsForMenu();
        }

        LogHomeModesState("ForceHideHomeUi");
    }

    void ForceShowHomeUi()
    {
        SetPanelVisible(panelModes, false);
        SetPanelVisible(ResolveJoinTablePanel(), false);
        HidePlayWithFriendsPanel();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideGamePanelsForMenu();

        SetPanelVisible(panelHomeScreen, true);
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowHomeMenuCanvas();

        LogHomeModesState("ForceShowHomeUi");
    }

    void LogHomeModesState(string source)
    {
        bool homeOn = panelHomeScreen != null && panelHomeScreen.activeSelf;
        bool modesOn = panelModes != null && panelModes.activeSelf;
        CanvasGroup homeCg = panelHomeScreen != null ? panelHomeScreen.GetComponent<CanvasGroup>() : null;
        Debug.Log($"[UI] {source} | Home active={homeOn} interactable={(homeCg != null && homeCg.interactable)} | Modes active={modesOn}");
    }

    public void OpenModePanelFromHome() => OnClick_PlayOnline_Home();

    public enum MainMenuScreen { Home, Modes, FriendsLobby }

    GameObject _cachedJoinTablePanel;

    public GameObject ResolveJoinTablePanel()
    {
        if (_cachedJoinTablePanel != null) return _cachedJoinTablePanel;
        if (UiSafeLookup.TryGet("JoinTablePanel", out GameObject joinTable))
            _cachedJoinTablePanel = joinTable;
        return _cachedJoinTablePanel;
    }

    static void SetPanelVisible(GameObject panel, bool visible)
    {
        ApplyPanelVisible(panel, visible);
    }

    /// <summary>Public wrapper for MatchmakingManager panel visibility fixes.</summary>
    public static void SetPanelVisiblePublic(GameObject panel, bool visible) => ApplyPanelVisible(panel, visible);

    static void ApplyPanelVisible(GameObject panel, bool visible)
    {
        if (panel == null) return;

        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (!visible)
        {
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            panel.SetActive(false);
            return;
        }

        panel.SetActive(true);
        if (cg != null)
        {
            cg.DOKill();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
    }

    public void HideJoinTablePanel()
    {
        Debug.Log("[UI] HideJoinTable called");
        SetPanelVisible(ResolveJoinTablePanel(), false);
    }

    public void ShowJoinTablePanel()
    {
        if (!isFriendsMatchMode) return;
        Debug.Log("[UI] ShowJoinTable called");
        UiFlowManager.SetUIState(UIState.JoinTable);
        SetPanelVisible(ResolveJoinTablePanel(), true);
    }

    /// <summary>PIN join from the Modes Join Table panel — ensure friends failure handlers run.</summary>
    public void MarkFriendsPinJoinFlow()
    {
        isFriendsMatchMode = true;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.PlayWithFriends;
    }

    /// <summary>After a failed PIN join — restore Modes + Join Table without ResetMenuUiState side effects.</summary>
    public void RestoreJoinTableScreenAfterFailedPin()
    {
        Debug.Log("[UI] RestoreJoinTableScreenAfterFailedPin");
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection, forceRecovery: true);
        UiFlowManager.SetUIState(UIState.JoinTable);

        ForceHideHomeUi();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.HideHomeMenuCanvas();
            NetworkManager.Instance.HideGamePanelsForMenu();
            NetworkManager.Instance.ForceClearBlackOverlay();
            NetworkManager.Instance.HideLoadingInstant();
        }

        SetPanelVisible(panelHomeScreen, false);
        HidePlayWithFriendsPanel();
        SetPanelVisible(panelModes, true);

        if (panelModes != null)
        {
            panelModes.transform.SetAsLastSibling();
            CanvasGroup modesCg = panelModes.GetComponent<CanvasGroup>();
            if (modesCg != null)
            {
                modesCg.DOKill();
                modesCg.alpha = 1f;
                modesCg.interactable = true;
                modesCg.blocksRaycasts = true;
            }
        }

        ShowJoinTablePanel();
        UiFlowManager.SetUIState(UIState.JoinTable);
        GameObject joinTable = ResolveJoinTablePanel();
        if (joinTable != null)
        {
            joinTable.transform.SetAsLastSibling();
            CanvasGroup joinCg = joinTable.GetComponent<CanvasGroup>();
            if (joinCg != null)
            {
                joinCg.DOKill();
                joinCg.alpha = 1f;
                joinCg.interactable = true;
                joinCg.blocksRaycasts = true;
            }
        }

        LogHomeModesState("Restored JoinTable after failed PIN");
        UiFlowManager.ValidatePanelState();
    }

    /// <summary>Closes overlays and resets menu async flags before any main panel switch.</summary>
    public void ResetMenuUiState()
    {
        Debug.Log("[UI] ResetMenuUiState");

        HideJoinTablePanel();
        HidePlayWithFriendsPanel();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ResetMenuFlowFlags();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideAllMenuOverlays();

        gameStartInProgress = false;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public void ValidateMenuPanels() => ValidateNoPanelOverlap();

    /// <summary>Runtime guard — prevents Home + Modes both visible/interactable.</summary>
    public void ValidateNoPanelOverlap()
    {
        bool homeOn = panelHomeScreen != null && panelHomeScreen.activeSelf;
        bool modesOn = panelModes != null && panelModes.activeSelf;
        GameObject joinTable = ResolveJoinTablePanel();
        bool joinOn = joinTable != null && joinTable.activeSelf;
        GameObject friends = ResolvePlayWithFriendsPanel();
        bool friendsOn = friends != null && friends.activeSelf;

        if (homeOn && (modesOn || joinOn || friendsOn || UiFlowManager.IsOnlineMatchmakingFlow()))
        {
            ForceHideHomeUi();
            homeOn = false;
        }

        if (homeOn && modesOn)
        {
            Debug.LogWarning("[UI] Overlap: Home and Modes are both active — hiding Home.");
            ForceHideHomeUi();
            homeOn = false;
        }

        if (isFriendsMatchMode && joinTable == null)
            Debug.LogWarning("[UI] JoinTablePanel missing during PlayFriends flow.");

        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.IsSearching
            && friendsOn == false && modesOn && homeOn)
        {
            Debug.LogWarning("[UI] Matchmaking active but Home/Modes may be blocking seat panel — hiding Home.");
            ForceHideHomeUi();
        }

        if (modesOn && joinOn && !isFriendsMatchMode)
            Debug.LogWarning("[UI] Overlap: JoinTable active outside Friends mode.");

        if (homeOn && friendsOn)
        {
            Debug.LogWarning("[UI] Overlap: Home and Friends lobby are both active — hiding Home.");
            ForceHideHomeUi();
        }

        if (homeOn && joinOn)
        {
            Debug.LogWarning("[UI] Overlap: Home and JoinTable are both active — hiding Home.");
            ForceHideHomeUi();
        }

        if (joinTable != null)
        {
            CanvasGroup jcg = joinTable.GetComponent<CanvasGroup>();
            if (!joinTable.activeSelf && jcg != null && jcg.blocksRaycasts)
            {
                jcg.blocksRaycasts = false;
                jcg.interactable = false;
            }
        }

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ValidateHiddenOverlayBlockers();
    }

    /// <summary>Activates exactly one main menu layer — Home, Modes, or Friends lobby.</summary>
    public void ShowOnlyMainMenuScreen(MainMenuScreen screen)
    {
        Debug.Log($"[UI] Showing panel: {screen}");
        ResetMenuUiState();

        if (screen == MainMenuScreen.FriendsLobby)
        {
            HideJoinTablePanel();
            ShowPlayWithFriendsPanel();
            ValidateNoPanelOverlap();
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.EnsureNoBlackScreen();
            return;
        }

        HidePlayWithFriendsPanel();

        bool showHome = screen == MainMenuScreen.Home;
        bool showModes = screen == MainMenuScreen.Modes;

        if (showHome)
            HideJoinTablePanel();

        if (showHome)
        {
            SetPanelVisible(panelModes, false);
            ForceShowHomeUi();
        }
        else
        {
            ForceHideHomeUi();
            SetPanelVisible(panelModes, showModes);

            if (showModes)
            {
                panelModes.transform.SetAsLastSibling();
                if (isFriendsMatchMode)
                    ShowJoinTablePanel();
            }
        }

        ValidateNoPanelOverlap();
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EnsureNoBlackScreen();
    }

    public void ShowModesScreenOnly()
    {
        Debug.Log("[UI] ShowModes called");

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.HideLoading();
            NetworkManager.Instance.EnsurePersistentBackdrop();
        }

        ShowOnlyMainMenuScreen(MainMenuScreen.Modes);
        ResetButtonScales();
        WireCut2TrumpButton();
        ApplyModePanelRules();
        UpdateModeSelectionUIColors();

        BGAudioManager.Instance?.OnMenuScreenShown();
    }

    public void ShowHomeScreenOnly()
    {
        ReturnToHomeClean();
    }

    /// <summary>Central Home reset — closes every menu overlay and shows Home only.</summary>
    public void ReturnToHomeClean() => UiFlowManager.ReturnToHomeClean();

    /// <summary>Called only by <see cref="UiFlowManager"/>.</summary>
    public void ReturnToHomeCleanInternal()
    {
        Debug.Log("[UI] ReturnToHomeClean called");

        isFriendsMatchMode = false;
        ResetStartGuard();
        findMatchAfterLobby = false;
        _pendingMatchmakingAfterLeave = false;

        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.HideMatchmakingPanel();

        if (PlayWithFriendsManager.Instance != null)
        {
            PlayWithFriendsManager.Instance.AbortPendingFriendsRoomCreation();
            PlayWithFriendsManager.Instance.ResetMenuFlowFlags();
            PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();
            PlayWithFriendsManager.Instance.CancelPinJoinUiState();
        }

        HideAllMainPanelsAndOverlays();

        if (InGameSettingsController.Instance != null)
            InGameSettingsController.Instance.DismissAllPanels();

        GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true);
        ForceShowHomeUi();
        ResetButtonScales();
        ApplyHomeScreenButtonColors();

        bool modesActive = panelModes != null && panelModes.activeSelf;
        bool homeActive = panelHomeScreen != null && panelHomeScreen.activeSelf;
        Debug.Log($"[UI] ReturnToHomeClean complete | Modes active={modesActive} | Home active={homeActive}");
        ValidateNoPanelOverlap();
        UiFlowManager.CompleteReturnToHome();
    }

    /// <summary>PlayFriends entry — restore original flow: Modes + Join Table, Home hidden.</summary>
    public void ShowModesForPlayFriendsInternal()
    {
        Debug.Log("[UI] ShowModes called (PlayFriends)");
        isFriendsMatchMode = true;
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection, forceRecovery: true);

        ForceHideHomeUi();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.HideLoadingInstant();
            NetworkManager.Instance.ForceClearBlackOverlay();
            NetworkManager.Instance.HideAllMenuOverlays();
        }

        ShowModesScreenOnly();
        LogHomeModesState("ShowModesForPlayFriends");
    }

    void HideAllMainPanelsAndOverlays()
    {
        HideJoinTablePanel();
        HidePlayWithFriendsPanel();
        SetPanelVisible(panelModes, false);
        ForceHideHomeUi();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.HideLoadingInstant();
            NetworkManager.Instance.HideGamePanelsForMenu();
            NetworkManager.Instance.HideAllMenuOverlays();
            NetworkManager.Instance.ClearUiInputBlockers();
            NetworkManager.Instance.ForceClearBlackOverlay();
        }
    }

    void OpenModePanelInternal(bool friendsMode = false)
    {
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        isFriendsMatchMode = friendsMode;

        if (friendsMode)
            UiFlowManager.MarkPlayFriendsCreate();
        else
        {
            UiFlowManager.SetUIState(UIState.Modes);
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.ClearOnlineModeOnly();
        }

        ShowModesScreenOnly();
    }

    /// <summary>
    /// Bots/Online: 1v1v1v1 only, hide Join Table. Friends: 2v2 only, show Join Table.
    /// </summary>
    void ApplyModePanelRules()
    {
        EnsureUiSearchRoot();

        if (!isFriendsMatchMode)
            HideJoinTablePanel();

        if (isFriendsMatchMode)
        {
            currentLogicMode = 2;
            SetLogicButtonVisible("Button_LogicA", false);
            SetLogicButtonVisible("Button_LogicB", true);
            SetLogicButtonVisible("Button_LogicC", false);
        }
        else
        {
            currentLogicMode = 1;
            SetLogicButtonVisible("Button_LogicA", true);
            SetLogicButtonVisible("Button_LogicB", false);
            SetLogicButtonVisible("Button_LogicC", false);
        }

        SaveSelectedModes();
        UpdateModeTitleLabel();
    }

    /// <summary>Bots/Online = 1v1v1v1 (LogicA). Friends = 2v2 (LogicB) only.</summary>
    void EnforceModeLogicRules()
    {
        int required = isFriendsMatchMode ? 2 : 1;
        if (currentLogicMode == required) return;

        Debug.Log($"[Modes] Enforcing logic mode {required} for {(isFriendsMatchMode ? "Friends 2v2" : "Bots/Online 1v1v1v1")}.");
        currentLogicMode = required;
        SaveSelectedModes();
        UpdateModeTitleLabel();
    }

    void SetLogicButtonVisible(string buttonName, bool visible)
    {
        if (UiSafeLookup.TryGet(buttonName, out GameObject go) && go != null)
            go.SetActive(visible);
    }

    /// <summary>
    /// Tasks 27/30 — Update both top-banner labels at once: the Mode label on the Modes screen
    /// (e.g. "MODE: ONLINE") and the Round label next to the Trump display (e.g. "Round 2").
    /// This is a direct, explicit setter; UpdateModeTitleLabel() and TrumpManager.UpdateRoundLabel()
    /// remain the automatic sync paths.
    /// </summary>
    public void UpdateTopBannerTexts(string currentMode, int currentRound)
    {
        // ---- Mode label (Modes screen) ----
        EnsureUiSearchRoot();
        string modeText = "MODE: " + (string.IsNullOrEmpty(currentMode) ? "" : currentMode.ToUpper());

        if (UiSafeLookup.TryGet("Text_ModesTitle", out GameObject titleGo) && titleGo != null)
        {
            var tmp = titleGo.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = modeText;
        }
        if (UiSafeLookup.TryGet("ModesTitle", out GameObject altTitle) && altTitle != null)
        {
            var tmp = altTitle.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = modeText;
        }

        // ---- Round label (next to Trump) ----
        if (TrumpManager.Instance != null && TrumpManager.Instance.roundText != null)
        {
            TrumpManager.Instance.roundText.gameObject.SetActive(true);
            TrumpManager.Instance.roundText.text = $"Round {Mathf.Max(1, currentRound)}";
        }
    }

    void UpdateModeTitleLabel()
    {
        string modeLabel = "MODE: ";
        if (isFriendsMatchMode)
            modeLabel += "FRIENDS";
        else if (NetworkManager.Instance != null && NetworkManager.Instance.isPlayBotsMode)
            modeLabel += "BOTS";
        else
            modeLabel += "ONLINE";

        if (UiSafeLookup.TryGet("Text_ModesTitle", out GameObject titleGo) && titleGo != null)
        {
            var tmp = titleGo.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = modeLabel;
        }

        if (UiSafeLookup.TryGet("ModesTitle", out GameObject altTitle) && altTitle != null)
        {
            var tmp = altTitle.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = modeLabel;
        }
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

    public GameObject ResolvePlayWithFriendsPanel()
    {
        if (panelPlayWithFriends != null)
            return panelPlayWithFriends;

        if (PlayWithFriendsManager.Instance != null)
        {
            panelPlayWithFriends = PlayWithFriendsManager.Instance.gameObject;
            return panelPlayWithFriends;
        }

        if (UiSafeLookup.TryGet("PlayWithFriendsPanel", out GameObject panel))
        {
            panelPlayWithFriends = panel;
            return panelPlayWithFriends;
        }

        return null;
    }

    public void ShowPlayWithFriendsPanel()
    {
        GameObject panel = ResolvePlayWithFriendsPanel();
        if (panel == null)
        {
            Debug.LogError("[Friends] PlayWithFriends panel not found.");
            return;
        }

        Debug.Log("[Friends] Friends panel opened");

        HideJoinTablePanel();
        if (panelHomeScreen != null)
            ForceHideHomeUi();
        if (panelModes != null)
            SetPanelVisible(panelModes, false);

        EnsurePanelHierarchyActive(panel);
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();

        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.DOKill();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
    }

    static void EnsurePanelHierarchyActive(GameObject panel)
    {
        if (panel == null) return;
        Transform t = panel.transform.parent;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    public static void EnsurePanelHierarchyActivePublic(GameObject panel) => EnsurePanelHierarchyActive(panel);

    public void HidePlayWithFriendsPanel()
    {
        SetPanelVisible(ResolvePlayWithFriendsPanel(), false);
    }

    public void OnClick_BackFromJoinTable()
    {
        Debug.Log("[UI] BackFromJoinTable called");
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.CancelPinJoinUiState();
        UiFlowManager.HideAllOverlays();
        HideJoinTablePanel();
        isFriendsMatchMode = true;
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        ShowOnlyMainMenuScreen(MainMenuScreen.Modes);
        LogHomeModesState("BackFromJoinTable");
    }

    public void OnClick_BackToHome()
    {
        Debug.Log("[UI] BackFromModes called");

        if (MatchmakingManager.Instance != null
            && (MatchmakingManager.Instance.IsSearching || GameFlowState.Current == GameFlowPhase.Matchmaking))
        {
            MatchmakingManager.Instance.OnCancelClicked();
            return;
        }

        GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true);
        ReturnToHomeClean();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.LeaveRoomAndCleanup();
        else if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.LeavePrivateRoomIfAny();
    }

    public void ApplyHomeScreenButtonColors()
    {
        // Home buttons keep their scene-authored sprite colors — no runtime tinting.
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
        Debug.Log("[Friends] PlayFriends clicked");
        isFriendsMatchMode = true;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.PlayWithFriends;
        UpdateFriendsOverlay();
        ApplyHomeScreenButtonColors();

        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: false);

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ClearOnlineModeOnly();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ForceClearBlackOverlay();

        if (NetworkManager.Instance != null && !NetworkManager.IsPhotonMasterReadyForRooms())
            NetworkManager.Instance.ConnectToPhoton();

        OpenModePanelInternal(true);
        UiFlowManager.MarkPlayFriendsCreate();
        UiFlowManager.SetUIState(UIState.Modes);
        Debug.Log("[Friends] Friends mode selected — modes panel open");

        if (PlayWithFriendsManager.Instance == null) return;

        PlayWithFriendsManager.Instance.BeginFriendsFlow();

        bool inPublicOnlineRoom = PhotonNetwork.InRoom
            && PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.IsVisible
            && !PhotonNetwork.OfflineMode;

        if (inPublicOnlineRoom)
        {
            Debug.Log("[Friends] Leaving online room before PlayFriends eager create.");
            PlayWithFriendsManager.Instance.RequestPrivateRoomCreateAfterLeave();
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.MarkReturnToFriendsModesAfterLeave();
                NetworkManager.Instance.LeaveRoomAndCleanup();
            }
            return;
        }

        bool alreadyInPrivate = PhotonNetwork.InRoom
            && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.IsVisible
            && !PhotonNetwork.OfflineMode;

        if (!PhotonNetwork.InRoom)
        {
            Debug.Log("[Friends] Creating room (eager, before mode Start)");
            PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin = true;
            PlayWithFriendsManager.Instance.CreatePrivateRoom();
        }
        else if (alreadyInPrivate)
        {
            PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin = true;
        }
    }

    public void OnClick_ClosePlayWithFriends()
    {
        HidePlayWithFriendsPanel();
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

    void ResolveModeToggles()
    {
        if (deckToggle == null && UiSafeLookup.TryGet("DeckToggle", out GameObject dGo) && dGo != null)
            deckToggle = dGo.GetComponent<SlidingModeToggle>();
        if (handsToggle == null && UiSafeLookup.TryGet("HandsToggle", out GameObject hGo) && hGo != null)
            handsToggle = hGo.GetComponent<SlidingModeToggle>();
    }

    void UpdateModeSelectionUIColors()
    {
        ResolveModeButtonImages();
        ResolveModeToggles();

        if (deckToggle != null)
            deckToggle.SetValue(currentTrickMode, animate: true, notify: false);
        if (handsToggle != null)
            handsToggle.SetValue(currentSarMode, animate: true, notify: false);

        // Selected buttons tint (hex: E3DED7), unselected (hex: 6D360D)
        Color selectedColor = ModeSelectedColor;
        Color unselectedColor = ModeUnselectedColor;

        if (btn1Taash != null)
            ApplyModeButtonColor(btn1Taash, currentTrickMode == 1 ? selectedColor : unselectedColor);

        if (btn2Taash != null)
            ApplyModeButtonColor(btn2Taash, currentTrickMode == 2 ? selectedColor : unselectedColor);

        if (btn1Sar != null && SarModeSelector.Instance == null)
            ApplyModeButtonColor(btn1Sar, currentSarMode == 1 ? selectedColor : unselectedColor);

        if (btn2Sar != null && SarModeSelector.Instance == null)
            ApplyModeButtonColor(btn2Sar, currentSarMode == 2 ? selectedColor : unselectedColor);

        if (btnPresetTrump != null)
            ApplyModeButtonColor(btnPresetTrump, currentTrumpMode == 1 ? selectedColor : unselectedColor);

        if (btn13thCard != null)
            ApplyModeButtonColor(btn13thCard, currentTrumpMode == 2 ? selectedColor : unselectedColor);

        // Button_PlayFirstCut is repurposed to Hidden Trump (mode 5).
        if (btnFirstCut != null)
            ApplyModeButtonColor(btnFirstCut, currentTrumpMode == 5 ? selectedColor : unselectedColor);

        if (btnCut2Trump != null)
            ApplyModeButtonColor(btnCut2Trump, currentTrumpMode == 4 ? selectedColor : unselectedColor);

        if (btnLogicA != null)
            ApplyModeButtonColor(btnLogicA, currentLogicMode == 1 ? selectedColor : unselectedColor);

        if (btnLogicB != null)
            ApplyModeButtonColor(btnLogicB, currentLogicMode == 2 ? selectedColor : unselectedColor);

        if (btnLogicC != null)
            ApplyModeButtonColor(btnLogicC, currentLogicMode == 3 ? selectedColor : unselectedColor);

        Debug.Log($"[Mode UI] Trick={currentTrickMode} | Trump={currentTrumpMode} | Sar={currentSarMode} | Logic={currentLogicMode}");

        if (SarModeSelector.Instance != null)
            SarModeSelector.Instance.UpdateButtonVisuals();
    }

    static void ApplyModeButtonColor(Image img, Color color)
    {
        if (img == null) return;
        img.color = color;

        Button btn = img.GetComponent<Button>();
        if (btn != null)
            btn.transition = Selectable.Transition.None;
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

        BGAudioManager.Instance?.OnGameplayStarting();

        EnforceModeLogicRules();

        bool isPrivateFriends = PhotonNetwork.InRoom
            && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.IsVisible
            && !PhotonNetwork.OfflineMode;
        bool isBots = NetworkManager.Instance != null && NetworkManager.Instance.isPlayBotsMode;
        MatchType matchType = GameSettings.Instance != null
            ? GameSettings.Instance.currentMatchType
            : MatchType.OnlinePhoton;

        Debug.Log($"[StartRoute] MatchType = {(isPrivateFriends ? "PlayWithFriends (private room)" : matchType.ToString())} | isPlayBotsMode={isBots}");

        // NEW FLOW: Friends mode and the host has not yet opened the seat/lobby panel ->
        // the host just finished selecting modes. Open the seat/lobby panel so the room
        // (already created eagerly on Play-With-Friends entry) is shown with its PIN and
        // friends can join. The match starts later from that panel's Start button
        // (host only, enabled once the table is full or bots are included).
        //
        // BUG FIX: we must ALSO enter this branch when a private room already exists, because
        // OnClick_PlayFriends() creates the room eagerly (so SuppressSeatLobbyOnJoin stays true
        // until the host opens the lobby). Without this, the first Start click would see
        // isPrivateFriends == true, skip the lobby, and immediately launch a bot game.
        bool seatLobbyNotOpenedYet = PlayWithFriendsManager.Instance != null
            && PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin;
        if (isFriendsMatchMode && (!isPrivateFriends || seatLobbyNotOpenedYet))
        {
            Debug.Log("[Friends] Start clicked — connecting buffer then opening seat lobby");
            SaveSelectedModes();
            if (gameStartInProgress)
            {
                Debug.Log("[GameStart] Duplicate start blocked");
                return;
            }
            gameStartInProgress = true;
            StartOnlineConnectionBuffer(() =>
            {
                if (PlayWithFriendsManager.Instance != null)
                    PlayWithFriendsManager.Instance.OpenSeatLobbyWhenReady();
                else
                    gameStartInProgress = false;
            });
            return;
        }

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
            bool hostConfirmed = PlayWithFriendsManager.Instance != null
                && PlayWithFriendsManager.Instance.ConsumeHostSeatStartConfirmation();

            if (!hostConfirmed)
            {
                Debug.Log("[Friends] Showing lobby (waiting for host seat Start)");
                ShowPlayWithFriendsPanel();
                if (NetworkManager.Instance != null)
                    NetworkManager.Instance.ResetRoomLobbyCanvasGroup();
                if (panelModes != null) panelModes.SetActive(false);
                if (PlayWithFriendsManager.Instance != null)
                    PlayWithFriendsManager.Instance.ShowPrivateRoomLobbyUI();
                return;
            }

            Debug.Log("[StartRoute] Private Friends route (host confirmed from seat panel)");
            gameStartInProgress = true;

            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.FinalStartWithSelectedModes();
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
        Debug.Log("[StartRoute] Play Online route — connecting buffer then matchmaking");
        gameStartInProgress = true;
        StartOnlineConnectionBuffer(StartNormalMatchFromModesPanel);
    }

    void StartOnlineConnectionBuffer(System.Action onBufferComplete)
    {
        if (_connectionBufferRoutine != null)
            StopCoroutine(_connectionBufferRoutine);
        _connectionBufferRoutine = StartCoroutine(OnlineConnectionBufferRoutine(onBufferComplete));
    }

    IEnumerator OnlineConnectionBufferRoutine(System.Action onBufferComplete)
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowLoading("Connecting Online...");
        else if (UiFlowManager.Current != UIState.LoadingGame)
            UiFlowManager.ShowLoadingOnly("Connecting Online...");

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ConnectToPhoton();
            else
                PhotonNetwork.ConnectUsingSettings();
        }

        yield return new WaitForSecondsRealtime(5f);

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.HideLoadingInstant();
            NetworkManager.Instance.ClearUiInputBlockers();
        }

        _connectionBufferRoutine = null;
        onBufferComplete?.Invoke();
    }

    public void StartNormalMatchFromModesPanel()
    {
        Debug.Log("[UI] Button Clicked: Find Match");

        bool isBots = NetworkManager.Instance != null && NetworkManager.Instance.isPlayBotsMode;

        if (NetworkManager.Instance != null)
        {
            if (isBots)
                NetworkManager.Instance.ShowLoading("Loading game...");
            else
                NetworkManager.Instance.HideLoading();
        }

        if (GameSettings.Instance != null)
        {
            if (isBots)
                GameSettings.Instance.currentMatchType = MatchType.OfflineBots;
            else if (isFriendsMatchMode)
                GameSettings.Instance.currentMatchType = MatchType.PlayWithFriends;
            else
                GameSettings.Instance.currentMatchType = MatchType.OnlinePhoton;
        }

        if (panelModes != null)
            SetPanelVisible(panelModes, false);

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

        Debug.Log("[UI] Online Start clicked — starting matchmaking UI");

        isFriendsMatchMode = false;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.OnlinePhoton;

        UiFlowManager.BeginOnlineMatchmaking();

        if (PlayWithFriendsManager.Instance != null)
        {
            PlayWithFriendsManager.Instance.EnsureNicknamePublic();
            PlayWithFriendsManager.Instance.ClearOnlineModeOnly();
        }

        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.PrepareForNewOnlineSearch();

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
            NetworkManager.Instance.ShowLoading("Loading game...");

        string roomName = "Local_Bot_" + Random.Range(1000, 9999);
        PhotonNetwork.CreateRoom(roomName, BuildRoomOptions());
    }

    public void StartSmartMatchmakingFromNetwork()
    {
        StartSmartMatchmaking();
    }

    void StartSmartMatchmaking()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.CancelReconnectUiForMenu();

        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[ModeManager] StartSmartMatchmaking blocked because user cancelled");
            findMatchAfterLobby = false;
            return;
        }

        if (PhotonNetwork.NetworkClientState == ClientState.Leaving)
        {
            Debug.Log("[Photon] Still leaving previous room — will matchmake after leave.");
            findMatchAfterLobby = true;
            return;
        }

        if (PhotonNetwork.InRoom)
        {
            Debug.Log("[Photon] Leaving current room before online matchmaking.");
            _pendingMatchmakingAfterLeave = true;
            findMatchAfterLobby = true;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.MarkPendingOnlineMatchmakingAfterLeave();
            PhotonNetwork.LeaveRoom();
            return;
        }

        if (PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.OfflineMode = false;
            if (NetworkManager.Instance != null && !NetworkManager.Instance.EnsureConnectedForOnlineRoomOps())
            {
                findMatchAfterLobby = true;
                return;
            }
        }

        if (NetworkManager.Instance != null && !NetworkManager.Instance.EnsureConnectedForOnlineRoomOps())
        {
            findMatchAfterLobby = true;
            return;
        }

        if (!PhotonNetwork.InLobby)
        {
            findMatchAfterLobby = true;
            if (PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
            {
                Debug.Log("[Photon] Attempt Join Lobby (before matchmaking)");
                PhotonNetwork.JoinLobby();
            }
            else
            {
                Debug.Log("[Photon] Waiting for lobby join before matchmaking");
            }
            return;
        }

        findMatchAfterLobby = false;

        bool onlineMatchmaking = GameSettings.Instance != null
            && GameSettings.Instance.currentMatchType == MatchType.OnlinePhoton;
        if (onlineMatchmaking || UiFlowManager.IsOnlineMatchmakingFlow())
        {
            isFriendsMatchMode = false;
            UiFlowManager.BeginOnlineMatchmaking();
        }

        if (isFriendsMatchMode && !onlineMatchmaking)
        {
            string roomName = "Friends_" + currentTrickMode + "_" + currentTrumpMode + "_" + currentSarMode + "_" + Random.Range(1000, 9999);
            Debug.Log($"[Photon] Friends room — create {roomName}");
            PhotonNetwork.CreateRoom(roomName, BuildRoomOptions(friendsRoom: true));
            return;
        }

        Debug.Log("[Photon] Attempt Join Room (JoinRandomRoom) for online matchmaking");
        Hashtable expected = new Hashtable { { "TM", currentTrickMode }, { "RM", currentTrumpMode }, { "SM", currentSarMode }, { "LM", currentLogicMode } };
        PhotonNetwork.JoinRandomRoom(expected, 4);
    }

    RoomOptions BuildRoomOptions(bool friendsRoom = false)
    {
        Hashtable roomProperties = new Hashtable { { "TM", currentTrickMode }, { "RM", currentTrumpMode }, { "SM", currentSarMode }, { "LM", currentLogicMode } };
        return new RoomOptions
        {
            MaxPlayers = 4,
            IsOpen = true,
            IsVisible = !PhotonNetwork.OfflineMode && !friendsRoom,
            CustomRoomProperties = roomProperties,
            CustomRoomPropertiesForLobby = new string[] { "TM", "RM", "SM", "LM" },
            PlayerTtl = 30000,
            EmptyRoomTtl = 30000,
            // Required so other players can read each other's AuthValues.UserId (account id).
            // The in-game friend / stats popup uses Player.UserId to identify opponents.
            PublishUserId = true
        };
    }

    public override void OnLeftRoom()
    {
        if (!_pendingMatchmakingAfterLeave) return;

        _pendingMatchmakingAfterLeave = false;
        Debug.Log("[Photon] Resuming online matchmaking after leave.");
        StartCoroutine(ResumeMatchmakingAfterLeave());
    }

    IEnumerator ResumeMatchmakingAfterLeave()
    {
        yield return null;
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
            yield break;
        StartSmartMatchmaking();
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
        Debug.LogWarning($"[Photon] OnJoinRandomFailed | code={returnCode} | {message}");
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[Photon] JoinRandomFailed ignored because user cancelled matchmaking");
            findMatchAfterLobby = false;
            return;
        }

        Debug.Log("[Photon] Attempt Create Room for online matchmaking");
        if (NetworkManager.Instance != null && !NetworkManager.Instance.EnsureConnectedForOnlineRoomOps())
        {
            findMatchAfterLobby = true;
            return;
        }
        isFriendsMatchMode = false;
        PhotonNetwork.CreateRoom("Room_" + Random.Range(1000, 9999), BuildRoomOptions());
    }

    public override void OnCreatedRoom()
    {
        Debug.Log($"[Photon] CreatedRoom | {PhotonNetwork.CurrentRoom?.Name}");
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Photon] CreateRoomFailed | {returnCode} | {message}");
        gameStartInProgress = false;
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            GameFlowState.SetPhase(GameFlowPhase.Home, true);
            MatchmakingManager.Instance.StopSearching(false);
            return;
        }

        if (UiFlowManager.IsOnlineMatchmakingFlow())
        {
            Debug.LogWarning("[Photon] Online create failed — retrying matchmaking.");
            GameFlowState.SetPhase(GameFlowPhase.Matchmaking, forceRecovery: true);
            if (MatchmakingManager.Instance != null)
                MatchmakingManager.Instance.ShowMatchmakingPanel();
            findMatchAfterLobby = true;
            StartSmartMatchmaking();
            return;
        }

        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StopSearching(false);
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
    }

    public override void OnJoinedRoom()
    {
        StartCoroutine(HandleModeJoinedRoomDeferred());
    }

    IEnumerator HandleModeJoinedRoomDeferred()
    {
        yield return null;

        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
        {
            bool rejoining = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs1) && (bool)gs1;
            if (!rejoining)
            {
                // Eager invite-room: host must stay on the Modes panel until they tap Play.
                if (PlayWithFriendsManager.Instance != null
                    && PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin
                    && PhotonNetwork.IsMasterClient)
                {
                    Debug.Log("[ModeManager] Eager invite-room — host keeps Modes panel visible.");
                    if (NetworkManager.Instance != null)
                        NetworkManager.Instance.EnsureFriendsModesPanelVisible();
                    yield break;
                }

                Debug.Log("Private Room Joined. Waiting in Lobby...");
                GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);

                // Lobby fade is owned by NetworkManager.SmoothTransitionToRoomLobby().
                yield break;
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
        gameStartInProgress = false;

        if (UiFlowManager.IsPlayFriendsJoinFlow())
        {
            Debug.LogWarning($"[Photon] JoinRoomFailed (Friends PIN) | {returnCode} | {message}");
            UiFlowManager.HandlePinJoinFailed(returnCode, message);
            return;
        }

        if (isFriendsMatchMode && PlayWithFriendsManager.Instance != null
            && UiFlowManager.Flow == UiFlowKind.PlayFriendsCreate)
        {
            Debug.LogWarning($"[Photon] JoinRoomFailed (Friends create) | {returnCode} | {message}");
            GameFlowState.SetPhase(GameFlowPhase.ModeSelection, forceRecovery: true);
            return;
        }

        if (UiFlowManager.IsOnlineMatchmakingFlow())
        {
            Debug.LogWarning($"[Photon] JoinRoomFailed during online matchmaking | {returnCode} | {message}");
            GameFlowState.SetPhase(GameFlowPhase.Matchmaking, forceRecovery: true);
            if (MatchmakingManager.Instance != null)
                MatchmakingManager.Instance.ShowMatchmakingPanel();
            findMatchAfterLobby = true;
            StartSmartMatchmaking();
            return;
        }

        Debug.LogWarning($"[Photon] JoinRoomFailed | {returnCode} | {message}");
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StopSearching(false);
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnJoinRoomFailedRestoreUi();
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.ShowJoinError("Invalid PIN or Room Full!");
        }
    }

}
