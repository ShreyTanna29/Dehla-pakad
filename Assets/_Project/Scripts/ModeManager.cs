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
    private bool isFriendsMatchMode = false;

    /// <summary>True while the user is in the Play With Friends / 2v2 flow.</summary>
    public bool IsFriendsMatchMode => isFriendsMatchMode;

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

    void OpenModePanelInternal(bool friendsMode = false)
    {
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        isFriendsMatchMode = friendsMode;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EnsurePersistentBackdrop();

        if (panelModes != null)
        {
            panelModes.SetActive(true);
            panelModes.transform.SetAsLastSibling();
        }

        if (panelHomeScreen != null)
            panelHomeScreen.SetActive(false);

        ResetButtonScales();
        WireCut2TrumpButton();
        ApplyModePanelRules();
        UpdateModeSelectionUIColors();
    }

    /// <summary>
    /// Bots/Online: 1v1v1v1 only, hide Join Table. Friends: 2v2 only, show Join Table.
    /// </summary>
    void ApplyModePanelRules()
    {
        EnsureUiSearchRoot();

        if (UiSafeLookup.TryGet("JoinTablePanel", out GameObject joinTable))
            joinTable.SetActive(isFriendsMatchMode);

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

    public void OnClick_BackToHome()
    {
        Debug.Log("[UI] Button Clicked: Back to Home");
        GameFlowState.SetPhase(GameFlowPhase.Home);
        isFriendsMatchMode = false;
        gameStartInProgress = false;

        // Release any lingering private room before returning Home.
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.LeaveRoomAndCleanup();
        else if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.LeavePrivateRoomIfAny();

        // Show Home FIRST, then hide Modes — guarantees no blank (blue) frame where no panel is active.
        if (panelHomeScreen != null)
        {
            panelHomeScreen.SetActive(true);
            panelHomeScreen.transform.SetAsLastSibling();
        }

        if (panelModes != null)
            panelModes.SetActive(false);

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.UpdateUIState(true);

        ResetButtonScales();
        ApplyHomeScreenButtonColors();
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
        Debug.Log("[UI] Button Clicked: Play With Friends");
        isFriendsMatchMode = true;
        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.PlayWithFriends;
        UpdateFriendsOverlay();
        ApplyHomeScreenButtonColors();

        // New flow: open the Modes panel FIRST. The seat (Play-with-Friends) panel
        // opens later, after the host taps Play on the modes screen.
        OpenModePanelInternal(true);

        // BUG 1 fix: create the private friends room EAGERLY (friends path only) so a
        // Photon room/PIN exists before the host clicks any invite. Previously the room
        // was only created at OnSeatPanelOpened() after mode selection, so early invite
        // clicks were parked in _pendingInviteFriendId and sent only after Play.
        // CreatePrivateRoom() already guards against double-creation (returns early when
        // PhotonNetwork.InRoom). The host's seat panel re-uses the same room later.
        if (PlayWithFriendsManager.Instance != null && !PhotonNetwork.InRoom)
        {
            Debug.Log("[Friends][BUG1] Eagerly creating private room on Play With Friends entry.");
            // Mark this as an eager invite-room so join-time handlers keep the host on the Modes
            // panel (the seat lobby opens later when the host taps Play).
            PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin = true;
            PlayWithFriendsManager.Instance.CreatePrivateRoom();
        }
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
            Debug.Log("[StartRoute] Friends mode -> opening seat panel (modes already chosen)");
            SaveSelectedModes();
            if (panelPlayWithFriends != null)
            {
                panelPlayWithFriends.SetActive(true);
                panelPlayWithFriends.transform.SetAsLastSibling();
            }
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ResetRoomLobbyCanvasGroup();
            if (panelModes != null) panelModes.SetActive(false);
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.OnSeatPanelOpened();
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
                Debug.Log("[StartRoute] Private room active but host has not confirmed from seat panel — showing lobby (no auto-start).");
                if (panelPlayWithFriends != null)
                {
                    panelPlayWithFriends.SetActive(true);
                    panelPlayWithFriends.transform.SetAsLastSibling();
                }
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
        Debug.Log("[StartRoute] Play Online route");
        gameStartInProgress = true;
        StartNormalMatchFromModesPanel();
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
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[ModeManager] StartSmartMatchmaking blocked because user cancelled");
            findMatchAfterLobby = false;
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

        if (isFriendsMatchMode)
        {
            string roomName = "Friends_" + currentTrickMode + "_" + currentTrumpMode + "_" + currentSarMode + "_" + Random.Range(1000, 9999);
            Debug.Log($"[Photon] Friends room — create {roomName}");
            PhotonNetwork.CreateRoom(roomName, BuildRoomOptions(friendsRoom: true));
            return;
        }

        Debug.Log("[Photon] Attempt Join Room (JoinRandomRoom)");
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
        Debug.LogError($"[Photon] JoinRoomFailed | {returnCode} | {message}");
        gameStartInProgress = false;
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
