using System.Collections;
using UnityEngine;
using Photon.Pun; 
using Photon.Realtime; 
using DG.Tweening; 
using UnityEngine.UI; 
using TMPro;

public class NetworkManager : MonoBehaviourPunCallbacks 
{
    public static NetworkManager Instance;

    private static bool isQuitting;

    [Header("UI Panels (Canvas Groups)")]
    public CanvasGroup homeCanvasGroup; 
    public CanvasGroup gameCanvasGroup;
    public CanvasGroup loadingCanvasGroup;

    [Header("Game Table UI")]
    public GameObject homeMenuPanel;
    public GameObject gameTablePanel;

    [Header("Lobby Waiting UI")]
    [SerializeField] private GameObject modePanel;
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private string gameSceneName = "DehlaPakad";

    [Header("UI Texts")]
    public TMP_Text loadingText;

    [Header("Buttons Setup")]
    public Button playOnlineButton; 
    public Button playBotsButton;

    [Header("Transition Settings")]
    public float transitionTime = 0.5f;

    [Header("UI Transition Polish (DOTween)")]
    [SerializeField] private CanvasGroup roomLobbyCanvasGroup;
    [SerializeField] private CanvasGroup blackTransitionCanvasGroup;
    [SerializeField] private float joinLoadingFadeIn = 0.2f;
    [SerializeField] private float joinLoadingFadeOut = 0.3f;
    [SerializeField] private float lobbyFadeIn = 0.3f;
    [SerializeField] private float gameStartBlackFade = 0.5f;
    [SerializeField] private float gameStartBlackFadeOut = 0.4f;

    CanvasGroup _persistentBackdrop;
    static readonly Color ScreenBackdropColor = new Color(0.02f, 0.05f, 0.08f, 1f);

    Coroutine _joinFadeRoutine;
    Coroutine _loadingSliderRoutine;
    Slider _loadingSlider;
    bool _lobbyTransitionRunning;
    bool _returnToFriendsModesAfterLeave;
    bool _isLeavingRoom;
    bool _pendingOnlineMatchmakingAfterLeave;

    const float LeaveLoadingMinSeconds = 2f;
    float _leaveLoadingShownTime = -1f;
    Coroutine _minLeaveLoadingRoutine;
    bool _loginTransitionLoadingActive;

    public bool isPlayBotsMode = false;

    bool IsBotsFlowActive() =>
        isPlayBotsMode || pendingOfflineMatch || PhotonNetwork.OfflineMode;

    public bool IsBotsMatchFlowActive() => IsBotsFlowActive();

    bool IsOfflineBotStickyActive()
    {
        return (isPlayBotsMode || PhotonNetwork.OfflineMode
                || (GameSettings.Instance != null && GameSettings.Instance.currentMatchType == MatchType.OfflineBots))
               && !UiFlowManager.IsReturningHome
               && Time.unscaledTime < _offlineBotRevealStickyUntil;
    }

    void ArmOfflineBotSticky(float seconds = 12f)
    {
        _offlineBotRevealStickyUntil = Time.unscaledTime + Mathf.Max(2f, seconds);
    }

    public int BumpFlowToken(string reason)
    {
        _flowToken++;
        Debug.Log($"[Flow] {reason} token={_flowToken}");
        return _flowToken;
    }

    public bool IsFlowTokenCurrent(int token) => token == _flowToken;

    public void BeginProtectedLoading(ProtectedLoadingFlow flow, string message, int token = -1)
    {
        if (token < 0) token = _flowToken;
        _protectedLoadingFlow = flow;
        _protectedLoadingToken = token;
        Debug.Log($"[Loading] Protected loading started flow={flow} token={token}");
        ShowLoading(message);
        BringLoadingToFront();
    }

    public void EndProtectedLoading(ProtectedLoadingFlow flow, int token = -1)
    {
        if (_protectedLoadingFlow == ProtectedLoadingFlow.None) return;
        if (flow != ProtectedLoadingFlow.None && flow != _protectedLoadingFlow) return;
        if (token >= 0 && token != _protectedLoadingToken) return;
        Debug.Log($"[Loading] Protected loading ended flow={_protectedLoadingFlow} token={_protectedLoadingToken}");
        _protectedLoadingFlow = ProtectedLoadingFlow.None;
        _protectedLoadingToken = 0;
    }

    public bool IsProtectedLoadingActive() => _protectedLoadingFlow != ProtectedLoadingFlow.None;

    private bool pendingOfflineMatch = false;
    private Coroutine _offlineStartCoroutine;
    private Coroutine _offlineCompleteRoutine;
    private int _offlineStartToken;

    private bool gameStartInProgress;
    private bool dealingStarted;

    private string lastStatusMessage = "Initializing...";
    private string lastErrorMessage = "None";

    public static string LastStatus => Instance != null ? Instance.lastStatusMessage : "?";
    public static string LastError => Instance != null ? Instance.lastErrorMessage : "?";

    [Header("Reconnection UI")]
    public GameObject connectionLostPanel;
    const float DisconnectAbandonHomeSeconds = 30f;
    const float ReconnectRetrySeconds = 2f;
    public const float GameStartLoadingDelaySeconds = 1.5f;

    private bool isAttemptingRejoin = false;
    private bool _localMatchAbandoned;
    private Coroutine _disconnectAbandonCoroutine;
    private Coroutine _autoReconnectCoroutine;
    private Coroutine _loadingTimeoutCoroutine;
    private string storedRoomName;
    const string PrefsActiveRoomName = "ActiveMatchRoomName";
    private TMP_Text reconnectingStatusText;
    private TMP_Text reconnectionLostStatusText;
    private GameObject reconnectingSpinner;
    private GameObject reconnectionLostRoot;
    private static bool _connectionLostPanelWarned;

    private Coroutine _offlineLoadingWatchdogRoutine;
    private Coroutine _offlineBotDealRoutine;
    private float _offlineBotRevealStickyUntil = -1f;
    private int _offlineRoomCreateAttempts;
    private Coroutine _offlineCreateRoomRoutine;

    public enum ProtectedLoadingFlow
    {
        None,
        BotStarting,
        FriendsCreatingRoom,
        FriendsLobby
    }

    ProtectedLoadingFlow _protectedLoadingFlow = ProtectedLoadingFlow.None;
    int _protectedLoadingToken;
    int _flowToken = 1;

    bool _showingNoInternetOverlay;
    bool _pendingPhotonReconnectAfterAuth;
    Coroutine _internetMonitorCoroutine;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        GamePerformanceBootstrap.Apply();

        ApplyPhotonPeerTuning();
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.DOKill();
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.gameObject.SetActive(false);
        }
        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 0f;
            homeCanvasGroup.gameObject.SetActive(false);
        }

        EnsurePersistentBackdrop();
        if (_persistentBackdrop != null)
        {
            _persistentBackdrop.DOKill();
            _persistentBackdrop.alpha = 1f;
            _persistentBackdrop.transform.SetAsLastSibling();
            _persistentBackdrop.blocksRaycasts = true;
        }

        if (string.IsNullOrEmpty(PhotonNetwork.AuthValues?.UserId))
        {
            string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("PhotonUserId", uid);
            PhotonNetwork.AuthValues = new AuthenticationValues(uid);
        }

        HideHomeUntilLogin();
        EnsurePersistentBackdrop();
        EnsureCameraSolidBackground();
    }

    public void FadeOutStartupCover(float duration = 0.5f)
    {
        if (_persistentBackdrop == null) return;
        
        _persistentBackdrop.DOKill();
        _persistentBackdrop.DOFade(0f, duration).OnComplete(() => {
            _persistentBackdrop.transform.SetAsFirstSibling();
            _persistentBackdrop.blocksRaycasts = false;
        });
    }

    void Start()
    {
        EnsurePersistentBackdrop();
        EnsureCameraSolidBackground();
        HideHomeUntilLogin();
        EnsureLoadingDoesNotBlockUI();

        if (playBotsButton != null) playBotsButton.interactable = true;

        SetupButtonAnimations();
        RefreshPlayOnlineButtonState();
        ResolveReconnectPanels();

        _internetMonitorCoroutine = StartCoroutine(MonitorInternetRoutine());
        StartCoroutine(EnsurePhotonReadyRoutine());
        StartCoroutine(WarmLoadingAddressablesRoutine());

        if (!IsPhotonConnectingOrConnected() && HasInternet())
            ConnectToPhoton();
    }

    IEnumerator WarmLoadingAddressablesRoutine()
    {
        if (loadingCanvasGroup == null) yield break;

        var keys = new System.Collections.Generic.List<string>();
        foreach (AddressableUIImageLoader loader in loadingCanvasGroup.GetComponentsInChildren<AddressableUIImageLoader>(true))
        {
            if (loader != null && !string.IsNullOrWhiteSpace(loader.addressableKey))
                keys.Add(loader.addressableKey);
        }

        if (keys.Count == 0) yield break;
        yield return AddressablesSpriteCache.PreloadKeysRoutine(keys);
    }

    public void EnsurePersistentBackdrop()
    {
        if (_persistentBackdrop != null)
        {
            if (!_persistentBackdrop.gameObject.activeSelf)
                _persistentBackdrop.gameObject.SetActive(true);
            _persistentBackdrop.transform.SetAsFirstSibling();
            return;
        }

        var go = new GameObject("PersistentScreenBackdrop",
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        Transform root = ResolveUiOverlayRoot();
        go.transform.SetParent(root, false);
        go.transform.SetAsFirstSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = ScreenBackdropColor;
        img.raycastTarget = false;

        _persistentBackdrop = go.GetComponent<CanvasGroup>();
        _persistentBackdrop.alpha = 1f;
        _persistentBackdrop.interactable = false;
        _persistentBackdrop.blocksRaycasts = false;
    }

    void EnsureCameraSolidBackground()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ScreenBackdropColor;
    }

    public void SnapScreenCover()
    {
        EnsurePersistentBackdrop();
        CanvasGroup black = EnsureBlackTransitionCanvas();
        EnsureOverlayParentActive(black.transform);
        black.gameObject.SetActive(true);
        black.DOKill();
        black.alpha = 1f;
        black.blocksRaycasts = true;
        black.interactable = false;
        black.transform.SetAsLastSibling();
    }

    static void ApplyPhotonPeerTuning()
    {
        PhotonNetwork.KeepAliveInBackground = 300f;
        var peer = PhotonNetwork.NetworkingClient?.LoadBalancingPeer;
        if (peer == null) return;
        peer.DisconnectTimeout = 25000;
        peer.SentCountAllowance = 9;
    }

    void PersistActiveRoomName(string roomName)
    {
        if (string.IsNullOrEmpty(roomName)) return;
        storedRoomName = roomName;
        PlayerPrefs.SetString(PrefsActiveRoomName, roomName);
        PlayerPrefs.Save();
    }

    void RestoreActiveRoomNameIfNeeded()
    {
        if (!string.IsNullOrEmpty(storedRoomName)) return;
        string saved = PlayerPrefs.GetString(PrefsActiveRoomName, "");
        if (!string.IsNullOrEmpty(saved))
            storedRoomName = saved;
    }

    void ClearPersistedActiveRoomName()
    {
        PlayerPrefs.DeleteKey(PrefsActiveRoomName);
    }

    void OnApplicationQuit()
    {
        isQuitting = true;
    }

    System.Collections.IEnumerator EnsurePhotonReadyRoutine()
    {
        var wait = new WaitForSecondsRealtime(1.5f);

        while (true)
        {
            // BOT/OFFLINE FLOW ME PHOTON AUTO-RECONNECT BILKUL NAHI CHALNA CHAHIYE
            if (IsBotsFlowActive())
            {
                RefreshPlayOnlineButtonState();
                yield return wait;
                continue;
            }

            if (!PhotonNetwork.OfflineMode && HasInternet())
            {
                if (!PhotonNetwork.IsConnectedAndReady && !isAttemptingRejoin)
                    ConnectToPhoton();
                else if (!isAttemptingRejoin)
                    EnsureJoinLobby();

                RefreshPlayOnlineButtonState();
            }

            yield return wait;
        }
    }

    public System.Collections.IEnumerator WaitForPhotonReadyRoutine(
        float minDisplaySeconds,
        float maxPhotonSeconds,
        System.Action<string> onStatus = null)
    {
        float start = Time.unscaledTime;
        onStatus?.Invoke("Connecting to server...");

        while (Time.unscaledTime - start < minDisplaySeconds)
            yield return null;

        float photonStart = Time.unscaledTime;
        while (!PhotonNetwork.IsConnectedAndReady && Time.unscaledTime - photonStart < maxPhotonSeconds)
        {
            onStatus?.Invoke("Connecting to Photon...");
            if (!IsPhotonConnectingOrConnected() && HasInternet() && !isAttemptingRejoin)
                ConnectToPhoton();
            yield return new WaitForSecondsRealtime(0.2f);
        }

        if (PhotonNetwork.IsConnectedAndReady
            && !PhotonNetwork.InLobby
            && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby
            && CanCallPhotonLobbyOps()
            && Instance != null)
        {
            Instance.EnsureJoinLobby();
        }

        RefreshPlayOnlineButtonState();
    }

    public static bool HasInternet()
    {
        return Application.internetReachability != NetworkReachability.NotReachable;
    }

    public static bool IsPlayOnlineReady()
    {
        if (PhotonNetwork.OfflineMode) return false;
        if (!HasInternet()) return false;
        if (Instance != null && Instance._showingNoInternetOverlay) return false;

        ClientState state = PhotonNetwork.NetworkClientState;
        if (state == ClientState.ConnectingToNameServer
            || state == ClientState.ConnectingToMasterServer
            || state == ClientState.Authenticating)
            return false;

        return PhotonNetwork.IsConnectedAndReady;
    }

    public void RefreshPlayOnlineButtonState()
    {
        if (playOnlineButton != null)
            playOnlineButton.interactable = true;
    }

    /// <summary>
    /// Clears queued Online/Friends transitions without touching the current Photon room.
    /// Used before switching mode from the menu, so stale callbacks cannot reopen matchmaking
    /// after the user already moved to another flow.
    /// </summary>
    public void CancelQueuedNetworkModeSwitches()
    {
        _pendingOnlineMatchmakingAfterLeave = false;
        _returnToFriendsModesAfterLeave = false;
        _pendingPhotonReconnectAfterAuth = false;
        _localMatchAbandoned = false;
        EndProtectedLoading(ProtectedLoadingFlow.FriendsCreatingRoom);
        EndProtectedLoading(ProtectedLoadingFlow.FriendsLobby);
        EndProtectedLoading(ProtectedLoadingFlow.BotStarting);

        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: false);
            MatchmakingManager.Instance.HideMatchmakingPanel();
        }

        CancelReconnectUiForMenu();
        ResetGameStartGuards();
    }

    /// <summary>
    /// Bot mode is deliberately treated as an offline island.  This method wipes every queued
    /// online/friends intent but does not assume Photon has finished leaving yet.
    /// StartOfflineMatchRequest() will finish the leave/disconnect chain safely.
    /// </summary>
    public void PrepareForBotModeFromMenu()
    {
        CancelQueuedNetworkModeSwitches();
        pendingOfflineMatch = false;
        isPlayBotsMode = true;
        _isLeavingRoom = false;
        _showingNoInternetOverlay = false;
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection, forceRecovery: true);
        HideLoadingInstant();
        ForceClearBlackOverlay();
        ClearUiInputBlockers();
    }

    public void TryConnectPhotonAtStartup()
    {
        if (PhotonNetwork.OfflineMode) return;

        if (!HasInternet())
        {
            ShowNoInternetLoading();
            return;
        }

        ConnectToPhoton();
    }

    public void ApplyPhotonAuthAndConnect(string userId)
    {
        if (string.IsNullOrEmpty(userId) || PhotonNetwork.OfflineMode) return;

        PhotonNetwork.AuthValues = new AuthenticationValues(userId);
        PlayerPrefs.SetString("PhotonUserId", userId);
        PlayerPrefs.Save();

        if (IsPhotonConnectingOrConnected())
        {
            _pendingPhotonReconnectAfterAuth = true;
            PhotonNetwork.Disconnect();
            return;
        }

        ConnectToPhoton();
    }

    public static bool IsPhotonConnectingOrConnected()
    {
        ClientState state = PhotonNetwork.NetworkClientState;
        return PhotonNetwork.IsConnected
            || state == ClientState.ConnectingToNameServer
            || state == ClientState.ConnectingToMasterServer
            || state == ClientState.Authenticating
            || state == ClientState.JoiningLobby;
    }

    public static bool CanCallPhotonLobbyOps()
    {
        if (PhotonNetwork.OfflineMode) return false;

        ClientState state = PhotonNetwork.NetworkClientState;
        if (state == ClientState.Disconnecting
            || state == ClientState.DisconnectingFromMasterServer
            || state == ClientState.DisconnectingFromGameServer
            || state == ClientState.Leaving
            || state == ClientState.Disconnected)
            return false;

        return PhotonNetwork.IsConnected;
    }

    public static bool IsPhotonMasterReadyForRooms()
    {
        if (PhotonNetwork.OfflineMode) return false;
        if (!PhotonNetwork.IsConnectedAndReady) return false;
        if (PhotonNetwork.Server != ServerConnection.MasterServer) return false;

        ClientState state = PhotonNetwork.NetworkClientState;
        return state != ClientState.JoiningLobby
            && state != ClientState.Joining
            && state != ClientState.Authenticating
            && state != ClientState.ConnectingToMasterServer
            && state != ClientState.ConnectingToNameServer
            && state != ClientState.Disconnecting
            && state != ClientState.DisconnectingFromMasterServer
            && state != ClientState.Leaving;
    }

    public void EnsureJoinLobby()
    {
        if (!CanCallPhotonLobbyOps()) return;
        if (!PhotonNetwork.IsConnectedAndReady) return;
        if (PhotonNetwork.InRoom) return;
        if (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
            PhotonNetwork.JoinLobby();
    }

    void ShowNoInternetLoading()
    {
        const string message = "Internet is not connected.\nPlease check your connection.";
        _showingNoInternetOverlay = true;
        if (loadingText != null) loadingText.text = message;
        if (loadingCanvasGroup == null) return;

        loadingCanvasGroup.gameObject.SetActive(true);
        loadingCanvasGroup.DOKill();
        loadingCanvasGroup.alpha = 1f;
        loadingCanvasGroup.blocksRaycasts = true;
        loadingCanvasGroup.interactable = true;
        loadingCanvasGroup.transform.SetAsLastSibling();
        lastStatusMessage = message;
        RefreshPlayOnlineButtonState();
    }

    System.Collections.IEnumerator MonitorInternetRoutine()
    {
        var wait = new WaitForSeconds(1f);

        while (true)
        {
            // BOT/OFFLINE FLOW ME NO-INTERNET OVERLAY YA PHOTON RECONNECT MAT CHALAO
            if (IsBotsFlowActive())
            {
                RefreshPlayOnlineButtonState();
                yield return wait;
                continue;
            }

            if (!HasInternet())
            {
                ShowNoInternetLoading();
            }
            else
            {
                if (_showingNoInternetOverlay)
                {
                    _showingNoInternetOverlay = false;

                    if (loadingCanvasGroup != null)
                    {
                        loadingCanvasGroup.DOKill();
                        loadingCanvasGroup.alpha = 0f;
                        loadingCanvasGroup.blocksRaycasts = false;
                        loadingCanvasGroup.interactable = false;
                        loadingCanvasGroup.gameObject.SetActive(false);
                    }
                }

                if (!PhotonNetwork.OfflineMode
                    && PhotonNetwork.NetworkClientState == ClientState.Disconnected
                    && !_pendingPhotonReconnectAfterAuth
                    && !isAttemptingRejoin)
                {
                    ConnectToPhoton();
                }
                else if (isAttemptingRejoin && PhotonNetwork.NetworkClientState == ClientState.Disconnected)
                {
                    TryReconnectToMatch();
                }
            }

            RefreshPlayOnlineButtonState();
            yield return wait;
        }
    }

    static bool IsUiObjectAlive(GameObject go)
    {
        if (go == null) return false;
        if (!go.scene.IsValid() || !go.scene.isLoaded) return false;
        return true;
    }

    static bool SafeSetActive(GameObject go, bool active, string label)
    {
        if (!IsUiObjectAlive(go))
            return false;

        if (go.activeSelf == active) return true;

        go.SetActive(active);
        return true;
    }

    static bool SafeSetTextActive(TMP_Text text, bool active, string label)
    {
        if (text == null) return false;
        return SafeSetActive(text.gameObject, active, label);
    }

    void ClearReconnectPanelCache()
    {
        reconnectingStatusText = null;
        reconnectionLostStatusText = null;
        reconnectingSpinner = null;
        reconnectionLostRoot = null;
    }

    void InvalidateStaleReconnectReferences()
    {
        if (!IsUiObjectAlive(connectionLostPanel))
        {
            connectionLostPanel = null;
            ClearReconnectPanelCache();
        }
    }

    void ResolveReconnectPanels()
    {
        InvalidateStaleReconnectReferences();

        if (!IsUiObjectAlive(connectionLostPanel)) return;

        Transform root = connectionLostPanel.transform;

        if (!IsUiObjectAlive(reconnectingStatusText?.gameObject))
            reconnectingStatusText = root.Find("Text_Reconnecting")?.GetComponent<TMP_Text>();

        if (!IsUiObjectAlive(reconnectionLostStatusText?.gameObject))
            reconnectionLostStatusText = root.Find("Text_ConnectionLost")?.GetComponent<TMP_Text>();

        if (!IsUiObjectAlive(reconnectingSpinner))
        {
            Transform spinner = root.Find("SpinnerContainer");
            reconnectingSpinner = spinner != null ? spinner.gameObject : null;
        }

        if (!IsUiObjectAlive(reconnectionLostRoot))
        {
            Transform lostChild = root.Find("Reconnection_Lost");
            reconnectionLostRoot = lostChild != null
                ? lostChild.gameObject
                : (IsUiObjectAlive(reconnectionLostStatusText?.gameObject) ? reconnectionLostStatusText.gameObject : null);
        }
    }

    bool TryShowConnectionLostShell()
    {
        ResolveReconnectPanels();
        if (!IsUiObjectAlive(connectionLostPanel)) return false;

        if (!SafeSetActive(connectionLostPanel, true, "connectionLostPanel"))
            return false;

        if (connectionLostPanel.transform.parent != null)
            connectionLostPanel.transform.SetAsLastSibling();

        return true;
    }

    void ShowReconnectingPanel(string message)
    {
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.DOKill();
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.gameObject.SetActive(false);
        }

        bool hasShell = TryShowConnectionLostShell();

        SafeSetActive(reconnectingSpinner, true, "SpinnerContainer");
        if (reconnectingStatusText != null)
        {
            SafeSetTextActive(reconnectingStatusText, true, "Text_Reconnecting");
            reconnectingStatusText.text = message;
        }

        SafeSetTextActive(reconnectionLostStatusText, false, "Text_ConnectionLost");
        if (IsUiObjectAlive(reconnectionLostRoot) &&
            reconnectionLostRoot != reconnectionLostStatusText?.gameObject)
            SafeSetActive(reconnectionLostRoot, false, "Reconnection_Lost");
    }

    void ShowReconnectionLostPanel(string message)
    {
        bool hasShell = TryShowConnectionLostShell();

        SafeSetActive(reconnectingSpinner, false, "SpinnerContainer");
        SafeSetTextActive(reconnectingStatusText, false, "Text_Reconnecting");

        if (reconnectionLostStatusText != null)
        {
            SafeSetTextActive(reconnectionLostStatusText, true, "Text_ConnectionLost");
            reconnectionLostStatusText.text = message;
        }

        if (IsUiObjectAlive(reconnectionLostRoot))
            SafeSetActive(reconnectionLostRoot, true, "Reconnection_Lost");
    }

    void HideReconnectPanels()
    {
        SafeSetActive(reconnectingSpinner, false, "SpinnerContainer");
        SafeSetTextActive(reconnectingStatusText, false, "Text_Reconnecting");
        SafeSetTextActive(reconnectionLostStatusText, false, "Text_ConnectionLost");
        SafeSetActive(reconnectionLostRoot, false, "Reconnection_Lost");
        SafeSetActive(connectionLostPanel, false, "connectionLostPanel");
    }

    void StopDisconnectAbandonCoroutine()
    {
        if (_disconnectAbandonCoroutine != null)
        {
            StopCoroutine(_disconnectAbandonCoroutine);
            _disconnectAbandonCoroutine = null;
        }
    }

    void LeaveMatchAndReturnHome()
    {
        HideReconnectPanels();
        HideLoading();
        isAttemptingRejoin = false;
        StopAutoReconnectRoutine();
        ClearPersistedActiveRoomName();
        GameFlowState.SetPhase(GameFlowPhase.Home);
        
        // 🚨 AUDIO TIME FIX: Unpause game so sounds play!
        Time.timeScale = 1f;

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
        else if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();
        else
            ReturnToHomeScreen();
    }

    System.Collections.IEnumerator AbandonMatchAfterDisconnectRoutine()
    {
        float timeLeft = DisconnectAbandonHomeSeconds;
        while (timeLeft > 0 && isAttemptingRejoin)
        {
            ShowReconnectingPanel($"Reconnecting to your game...\n({Mathf.CeilToInt(timeLeft)}s remaining)");
            yield return new WaitForSeconds(1f);
            timeLeft -= 1f;
        }

        if (isAttemptingRejoin)
        {
            _localMatchAbandoned = true;
            isAttemptingRejoin = false;
            StopAutoReconnectRoutine();
            ShowReconnectionLostPanel("Connection lost permanently.\nReturning to Home...");
            yield return new WaitForSeconds(2.5f);
            LeaveMatchAndReturnHome();
        }

        _disconnectAbandonCoroutine = null;
    }

    // 🚨 ZOMBIE PLAYER CRASH FIX: Old players hamesha sahi tareeqe se mitenge!
    void CleanUpLocalNetworkPlayer()
    {
        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.ResetHand();
            if (PlayerHand.LocalInstance.gameObject != null)
            {
                if (PhotonNetwork.IsConnected && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
                    PhotonNetwork.Destroy(PlayerHand.LocalInstance.gameObject);
                else
                    Destroy(PlayerHand.LocalInstance.gameObject);
            }
            PlayerHand.LocalInstance = null;
        }
    }

    void BeginInMatchDisconnectFlow()
    {
        if (isAttemptingRejoin) return;

        RestoreActiveRoomNameIfNeeded();
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            PersistActiveRoomName(PhotonNetwork.CurrentRoom.Name);

        StopDisconnectAbandonCoroutine();
        StopAutoReconnectRoutine();
        GameFlowState.SetPhase(GameFlowPhase.Disconnected, forceRecovery: true);
        if (TurnManager.Instance != null)
            TurnManager.Instance.SetPaused(true);
        CleanUpLocalNetworkPlayer();
        EnsureConnectionLostPanelVisible();
        ShowReconnectingPanel("Connection lost. Reconnecting...");
        isAttemptingRejoin = true;
        _disconnectAbandonCoroutine = StartCoroutine(AbandonMatchAfterDisconnectRoutine());
        _autoReconnectCoroutine = StartCoroutine(AutoReconnectRoutine());

        if (HasInternet())
            TryReconnectToMatch();
    }

    void EnsureConnectionLostPanelVisible()
    {
        ResolveReconnectPanels();
        SafeSetActive(connectionLostPanel, true, "connectionLostPanel");
    }

    void StopAutoReconnectRoutine()
    {
        if (_autoReconnectCoroutine != null)
        {
            StopCoroutine(_autoReconnectCoroutine);
            _autoReconnectCoroutine = null;
        }
    }

    bool IsPhotonReconnectInProgress()
    {
        ClientState state = PhotonNetwork.NetworkClientState;
        return state == ClientState.ConnectingToNameServer
            || state == ClientState.ConnectingToMasterServer
            || state == ClientState.Authenticating
            || state == ClientState.Joining
            || state == ClientState.JoiningLobby;
    }

    System.Collections.IEnumerator DeferredTryReconnectToMatch()
    {
        yield return null;
        TryReconnectToMatch();
    }

    System.Collections.IEnumerator DeferredBeginInMatchDisconnectFlow()
    {
        yield return null;
        if (!isAttemptingRejoin)
            BeginInMatchDisconnectFlow();
    }

    void TryReconnectToMatch()
    {
        if (!isAttemptingRejoin || !HasInternet()) return;
        if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom) return;
        if (IsPhotonReconnectInProgress()) return;

        ShowReconnectingPanel("Reconnecting to your game...");

        if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom && !string.IsNullOrEmpty(storedRoomName))
        {
            PhotonNetwork.RejoinRoom(storedRoomName);
            return;
        }

        if (PhotonNetwork.NetworkClientState == ClientState.Disconnected)
        {
            if (!PhotonNetwork.ReconnectAndRejoin())
                StartCoroutine(RejoinRoomAfterConnectRoutine());
        }
    }

    bool ShouldKeepLoadingVisibleAfterDisconnect()
    {
        return isAttemptingRejoin
            || GameFlowState.Current == GameFlowPhase.InGame
            || GameFlowState.Current == GameFlowPhase.Dealing
            || GameFlowState.Current == GameFlowPhase.ResolvingTrick
            || GameFlowState.Current == GameFlowPhase.Disconnected
            || (gameCanvasGroup != null && gameCanvasGroup.alpha > 0.1f);
    }

    System.Collections.IEnumerator ReconnectIdleRoutine()
    {
        yield return null;
        if (PhotonNetwork.OfflineMode || isAttemptingRejoin || !HasInternet()) yield break;
        if (PhotonNetwork.IsConnectedAndReady) yield break;

        ConnectToPhoton();
    }

    System.Collections.IEnumerator AutoReconnectRoutine()
    {
        yield return new WaitForSeconds(0.5f);

        while (isAttemptingRejoin)
        {
            if (HasInternet())
            {
                if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom)
                    yield break;

                if (!IsPhotonReconnectInProgress())
                    TryReconnectToMatch();
            }
            yield return new WaitForSeconds(ReconnectRetrySeconds);
        }
        _autoReconnectCoroutine = null;
    }

    System.Collections.IEnumerator RejoinRoomAfterConnectRoutine()
    {
        if (string.IsNullOrEmpty(storedRoomName)) yield break;

        if (PhotonNetwork.NetworkClientState == ClientState.Disconnected)
            PhotonNetwork.ConnectUsingSettings();

        float waited = 0f;
        while (waited < 20f && isAttemptingRejoin)
        {
            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
            {
                PhotonNetwork.RejoinRoom(storedRoomName);
                yield break;
            }

            waited += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
    }

    public static bool IsFatalDisconnect(DisconnectCause cause)
    {
        return cause == DisconnectCause.Exception
            || cause == DisconnectCause.ExceptionOnConnect
            || cause == DisconnectCause.ServerTimeout
            || cause == DisconnectCause.ClientTimeout;
    }

    public void ConnectToPhotonForOnlinePlay()
    {
        pendingOfflineMatch = false;
        isPlayBotsMode = false;

        if (PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.InRoom) return;
            PhotonNetwork.OfflineMode = false;
        }

        ClientState state = PhotonNetwork.NetworkClientState;
        if (state == ClientState.ConnectingToNameServer
            || state == ClientState.ConnectingToMasterServer
            || state == ClientState.Authenticating)
            return;

        if (PhotonNetwork.IsConnectedAndReady)
        {
            EnsureJoinLobby();
            RefreshPlayOnlineButtonState();
            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
            return;
        }

        PhotonNetwork.ConnectUsingSettings();
    }

    public bool EnsureConnectedForOnlineRoomOps()
    {
        pendingOfflineMatch = false;
        isPlayBotsMode = false;
        _offlineStartToken++;
        if (_offlineStartCoroutine != null)
        {
            StopCoroutine(_offlineStartCoroutine);
            _offlineStartCoroutine = null;
        }

        if (PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.InRoom) return false;
            PhotonNetwork.OfflineMode = false;
        }

        if (PhotonNetwork.IsConnectedAndReady && CanCallPhotonLobbyOps())
            return true;

        if (!HasInternet()) return false;

        ConnectToPhotonForOnlinePlay();
        return false;
    }

    public void ConnectToPhoton()
    {
        if (IsBotsFlowActive()) return;
        if (PhotonNetwork.OfflineMode) return;
        if (isAttemptingRejoin) return;
        if (PhotonNetwork.OfflineMode) return;
        if (isAttemptingRejoin) return;

        string authUserId = PlayerPrefs.GetString("PhotonUserId", "");
        if (string.IsNullOrEmpty(authUserId) && PhotonNetwork.AuthValues != null)
            authUserId = PhotonNetwork.AuthValues.UserId;

        if (!string.IsNullOrEmpty(authUserId))
        {
            if (PhotonNetwork.AuthValues == null)
                PhotonNetwork.AuthValues = new AuthenticationValues(authUserId);
            else
                PhotonNetwork.AuthValues.UserId = authUserId;
        }

        ClientState state = PhotonNetwork.NetworkClientState;
        if (state == ClientState.ConnectingToNameServer
            || state == ClientState.ConnectingToMasterServer
            || state == ClientState.Authenticating)
            return;

        if (PhotonNetwork.IsConnectedAndReady)
        {
            EnsureJoinLobby();
            RefreshPlayOnlineButtonState();
            return;
        }

        if (PhotonNetwork.IsConnected) return;

        PhotonNetwork.ConnectUsingSettings();
    }

    public void HideHomeUntilLogin()
    {
        if (homeCanvasGroup == null) return;
        homeCanvasGroup.DOKill();
        homeCanvasGroup.alpha = 0f;
        homeCanvasGroup.interactable = false;
        homeCanvasGroup.blocksRaycasts = false;
        homeCanvasGroup.gameObject.SetActive(false);
    }

    public void EnsureHomeCanvasForModePanel()
    {
        HideHomeMenuCanvas();
    }

    public void HideHomeMenuCanvas()
    {
        ResolveHomeMenuPanel();

        if (ModeManager.Instance != null && ModeManager.Instance.panelHomeScreen != null)
            ModeManager.Instance.panelHomeScreen.SetActive(false);

        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.interactable = false;
            homeCanvasGroup.blocksRaycasts = false;

            homeCanvasGroup.DOFade(0f, 0.3f).SetUpdate(true).OnComplete(() =>
            {
                if (homeMenuPanel != null) homeMenuPanel.SetActive(false);
                homeCanvasGroup.gameObject.SetActive(false);
            });
        }
        else if (homeMenuPanel != null)
        {
            homeMenuPanel.SetActive(false);
        }
    }

    public void ShowHomeMenuCanvas()
    {
        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.gameObject.SetActive(true);
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 1f;
            homeCanvasGroup.interactable = true;
            homeCanvasGroup.blocksRaycasts = true;
        }

        ResolveHomeMenuPanel();
        if (homeMenuPanel != null)
            homeMenuPanel.SetActive(true);
        else if (ModeManager.Instance != null && ModeManager.Instance.panelHomeScreen != null)
            ModeManager.Instance.panelHomeScreen.SetActive(true);

        BGAudioManager.Instance?.OnMenuScreenShown();
    }

    public void UpdateUIState(bool isHome, bool showLoadingOverlay = true)
    {
        if (isHome)
        {
            HideStrayPanelsForMenuReveal();
            ShowHomeUI();
        }
        else
            ShowGameScene(showLoadingOverlay);
    }

    void HideStrayPanelsForMenuReveal()
    {
        HideGameTablePanel();

        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 0f;
            gameCanvasGroup.interactable = false;
            gameCanvasGroup.blocksRaycasts = false;
            gameCanvasGroup.gameObject.SetActive(false);
        }

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);

            GameObject joinTable = ModeManager.Instance.ResolveJoinTablePanel();
            if (joinTable != null)
                joinTable.SetActive(false);

            ModeManager.Instance.HidePlayWithFriendsPanel();
        }
    }

    bool IsLoadingOverlayVisible()
    {
        return loadingCanvasGroup != null
            && loadingCanvasGroup.gameObject.activeSelf
            && loadingCanvasGroup.alpha > 0.01f;
    }

    bool IsHomePreparedBehindLoading()
    {
        ResolveHomeMenuPanel();
        return homeCanvasGroup != null
            && homeMenuPanel != null
            && homeMenuPanel.activeSelf;
    }

    public void CrossfadeLoadingToCanvasGroup(
        CanvasGroup destination,
        float destinationDuration = 0.4f,
        float loadingFadeDuration = 0.25f,
        System.Action onComplete = null)
    {
        StartCoroutine(CrossfadeLoadingToCanvasGroupRoutine(
            destination,
            destinationDuration,
            loadingFadeDuration,
            onComplete));
    }

    IEnumerator CrossfadeLoadingToCanvasGroupRoutine(
        CanvasGroup destination,
        float destinationDuration,
        float loadingFadeDuration,
        System.Action onComplete)
    {
        _loginTransitionLoadingActive = false;
        StopLoadingSafetyTimeout();

        if (destination != null)
        {
            if (!destination.gameObject.activeSelf)
                destination.gameObject.SetActive(true);

            destination.DOKill();
            destination.interactable = true;
            destination.blocksRaycasts = true;

            if (destination.alpha < 0.95f)
            {
                destination.alpha = 0f;
                destination.DOFade(1f, destinationDuration).SetUpdate(true);
            }
        }

        if (IsLoadingOverlayVisible())
        {
            loadingCanvasGroup.DOKill();
            loadingCanvasGroup.DOFade(0f, loadingFadeDuration).SetUpdate(true).OnComplete(() =>
            {
                loadingCanvasGroup.interactable = false;
                loadingCanvasGroup.blocksRaycasts = false;
                loadingCanvasGroup.gameObject.SetActive(false);
                StopLoadingSliderAnimation();
            });
            yield return new WaitForSecondsRealtime(loadingFadeDuration);
        }
        else
        {
            HideLoadingInstant();
        }

        float remain = destination != null
            ? Mathf.Max(0f, destinationDuration - loadingFadeDuration)
            : 0f;
        if (remain > 0f)
            yield return new WaitForSecondsRealtime(remain);

        FadeOutStartupCover(0.35f);
        onComplete?.Invoke();
    }

    public void ShowGameScene(bool showLoadingOverlay = true)
    {
        BGAudioManager.Instance?.OnGameplayStarting();
        EnsurePersistentBackdrop();
        HideReconnectPanels();
        if (AdsManager.Instance != null) AdsManager.Instance.HideBanner();

        if (showLoadingOverlay)
        {
            ShowLoading("Loading game...");
            BringLoadingToFront();
        }

        ForceClearBlackOverlay();

        ResolveGameCanvasGroup();
        if (gameCanvasGroup != null)
        {
            EnsureOverlayParentActive(gameCanvasGroup.transform, bringToFront: false);
            if (!gameCanvasGroup.gameObject.activeSelf) gameCanvasGroup.gameObject.SetActive(true);
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 0f;
            gameCanvasGroup.DOFade(1f, 0.4f).SetUpdate(true); 
            gameCanvasGroup.interactable = true;
            gameCanvasGroup.blocksRaycasts = true;
        }

        ResolveGameTablePanel();
        if (gameTablePanel != null)
        {
            EnsureOverlayParentActive(gameTablePanel.transform, bringToFront: false);
            gameTablePanel.SetActive(true);
        }

        InGameAddFriendController.Instance?.RefreshOpenButtonVisibility();

        if (!showLoadingOverlay && loadingCanvasGroup != null && loadingCanvasGroup.gameObject.activeSelf)
            HideLoadingInstant();

        if (PhotonNetwork.InRoom) InitializeGameplayScene();
    }

    public void MarkPendingOnlineMatchmakingAfterLeave()
    {
        _pendingOnlineMatchmakingAfterLeave = true;
        _returnToFriendsModesAfterLeave = false;
    }

    public void MarkReturnToFriendsModesAfterLeave()
    {
        _returnToFriendsModesAfterLeave = true;
        _pendingOnlineMatchmakingAfterLeave = false;
    }

    public void HideAllMenuOverlays()
    {
        _lobbyTransitionRunning = false;
        _joinFadeRoutine = null;
        ResetGameStartGuards();

        HideLoadingInstant();
        ForceClearBlackOverlay();
        CancelReconnectUiForMenu();

        if (waitingPanel != null)
            waitingPanel.SetActive(false);

        CanvasGroup lobby = ResolveRoomLobbyCanvasGroup();
        if (lobby != null)
        {
            lobby.DOKill();
            lobby.alpha = 0f;
            lobby.interactable = false;
            lobby.blocksRaycasts = false;
        }
    }

    public void CancelReconnectUiForMenu()
    {
        isAttemptingRejoin = false;
        StopAutoReconnectRoutine();
        StopDisconnectAbandonCoroutine();
        HideReconnectPanels();
    }

    public void HideGamePanelsForMenu()
    {
        HideGameTablePanel();
        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 0f;
            gameCanvasGroup.interactable = false;
            gameCanvasGroup.blocksRaycasts = false;
            gameCanvasGroup.gameObject.SetActive(false);
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public void ValidateHiddenOverlayBlockers()
    {
        ValidateCanvasGroupBlocker(loadingCanvasGroup, "Loading");
        ValidateCanvasGroupBlocker(blackTransitionCanvasGroup, "BlackTransition");
        ValidateCanvasGroupBlocker(homeCanvasGroup, "HomeCanvasGroup");
        ValidateCanvasGroupBlocker(ResolveRoomLobbyCanvasGroup(), "RoomLobby");
    }

    static void ValidateCanvasGroupBlocker(CanvasGroup cg, string label)
    {
        if (cg == null) return;
        if (!cg.gameObject.activeSelf && cg.blocksRaycasts)
            Debug.LogWarning($"[UI] {label} hidden but blocksRaycasts=true");
        if (cg.gameObject.activeSelf && cg.alpha < 0.01f && cg.blocksRaycasts)
            Debug.LogWarning($"[UI] {label} invisible but blocksRaycasts=true");
    }

    public void StayInPrivateLobbyUI()
    {
        PrepareForPrivateRoomLobby(showHomeMenu: false);
    }

    public void PrepareForPrivateRoomLobby(bool showHomeMenu)
    {
        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 0f;
            gameCanvasGroup.interactable = false;
            gameCanvasGroup.blocksRaycasts = false;
            gameCanvasGroup.gameObject.SetActive(false);
        }

        HideGameTablePanel();
        HideLoading();

        if (!showHomeMenu)
        {
            if (homeMenuPanel != null)
                homeMenuPanel.SetActive(false);
            if (homeCanvasGroup != null)
            {
                homeCanvasGroup.DOKill();
                homeCanvasGroup.alpha = 0f;
                homeCanvasGroup.interactable = false;
                homeCanvasGroup.blocksRaycasts = false;
            }
            return;
        }

        ResolveHomeMenuPanel();
        if (homeCanvasGroup != null)
        {
            if (!homeCanvasGroup.gameObject.activeSelf)
                homeCanvasGroup.gameObject.SetActive(true);
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 1f;
            homeCanvasGroup.interactable = true;
            homeCanvasGroup.blocksRaycasts = true;
        }

        if (homeMenuPanel != null)
            homeMenuPanel.SetActive(true);
        else if (ModeManager.Instance != null && ModeManager.Instance.panelHomeScreen != null)
            ModeManager.Instance.panelHomeScreen.SetActive(true);
    }

    public void LeaveRoomAndCleanup()
    {
        if (_isLeavingRoom) return;
        
        // 🚨 AUDIO TIME FIX: Unpause game so sounds play!
        Time.timeScale = 1f;

        bool explicitHomeReturn = UiFlowManager.IsReturningHome;
        bool menuFlowSwitch = UiFlowManager.IsOnlineMatchmakingFlow()
            || UiFlowManager.IsPlayFriendsLobbyFlow()
            || UiFlowManager.IsPlayFriendsJoinFlow()
            || GameFlowState.Current == GameFlowPhase.Matchmaking
            || GameFlowState.Current == GameFlowPhase.ModeSelection
            || PlayWithFriendsManager.IsFriendsPrivateRoomCreatePending()
            || _returnToFriendsModesAfterLeave;

        if (!explicitHomeReturn && !menuFlowSwitch)
            UiFlowManager.MarkReturningHome();
        else if (explicitHomeReturn)
            _returnToFriendsModesAfterLeave = false;

        CancelReconnectUiForMenu();

        if (MatchmakingManager.Instance != null)
        {
            bool friendsFlow = ModeManager.Instance != null && ModeManager.Instance.IsFriendsMatchMode;
            MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: !friendsFlow);
        }

        ResetGameStartGuards();
        isPlayBotsMode = false;
        pendingOfflineMatch = false;
        _localMatchAbandoned = false;

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();

        if (ModeManager.Instance != null)
            ModeManager.Instance.ResetStartGuard();

        CleanupRuntimeMatchStateForMenu();

        ShowLoading("Leaving room...");

        if (PhotonNetwork.InRoom)
        {
            _isLeavingRoom = true;
            PhotonNetwork.LeaveRoom();
            return;
        }

        PhotonNetwork.OfflineMode = false;
        _isLeavingRoom = false;

        if (explicitHomeReturn || (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser))
        {
            GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true);
            ReturnToHomeScreen();
        }
        else
            RestoreMenuPanelAfterLeave();

        BGAudioManager.Instance?.OnMenuScreenShown();
        HideLoading();

        if (!PhotonNetwork.IsConnectedAndReady && HasInternet())
            ConnectToPhotonForOnlinePlay();
    }

    public void ResetGameStartGuards()
    {
        gameStartInProgress = false;
        dealingStarted = false;
    }

    public void CleanupRuntimeMatchStateForMenu()
    {
        ResetGameStartGuards();
        Time.timeScale = 1f; // 🚨 TIME FIX

        if (DeckManager.Instance != null)
            DeckManager.Instance.ResetMatchState();
        else
            PlayerHand.CleanupRuntimeCardUi();

        // 🚨 ZOMBIE PLAYER FIX: Hamesha purane avatars hatne chahiye!
        CleanUpLocalNetworkPlayer();

        HideGameTablePanel();
        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 0f;
            gameCanvasGroup.interactable = false;
            gameCanvasGroup.blocksRaycasts = false;
            gameCanvasGroup.gameObject.SetActive(false);
        }

        ForceClearBlackOverlay();
        ClearUiInputBlockers();
        CompleteLoadingSlider();

        if (TurnManager.Instance != null)
            TurnManager.Instance.StopTimer();
    }

    public void FinalizeMenuTransition()
    {
        EnsureNoBlackScreen();
    }

    void ResolveHomeMenuPanel()
    {
        if (homeMenuPanel != null) return;
        if (ModeManager.Instance != null && ModeManager.Instance.panelHomeScreen != null)
            homeMenuPanel = ModeManager.Instance.panelHomeScreen;
        else if (homeCanvasGroup != null)
            homeMenuPanel = homeCanvasGroup.gameObject;
    }

    void ResolveGameTablePanel()
    {
        if (gameTablePanel != null) return;
        if (isQuitting || !Application.isPlaying) return;

        if (PlayWithFriendsManager.Instance != null && PlayWithFriendsManager.Instance.gameTablePanel != null)
        {
            gameTablePanel = PlayWithFriendsManager.Instance.gameTablePanel;
            return;
        }

        gameTablePanel = GameObject.Find("Panel_Game");
        if (gameTablePanel == null)
            gameTablePanel = GameObject.Find("[Panel_Game]");

        if (gameTablePanel == null && UiSafeLookup.TryGet("Panel_Game", out GameObject panelGo))
            gameTablePanel = panelGo;
        if (gameTablePanel == null && UiSafeLookup.TryGet("[Panel_Game]", out GameObject bracketGo))
            gameTablePanel = bracketGo;

        if (gameTablePanel != null && PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.gameTablePanel = gameTablePanel;
    }

    void ResolveGameCanvasGroup()
    {
        if (gameCanvasGroup != null) return;
        if (isQuitting || !Application.isPlaying) return;

        ResolveGameTablePanel();
        if (gameTablePanel != null)
            gameCanvasGroup = gameTablePanel.GetComponentInParent<CanvasGroup>(true);
    }

    void HideGameTablePanel()
    {
        ResolveGameTablePanel();
        if (gameTablePanel != null)
            gameTablePanel.SetActive(false);
    }

    void ShowHomeUI()
    {
        HideGameTablePanel();

        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOKill();
            gameCanvasGroup.DOFade(0f, 0.3f).SetUpdate(true).OnComplete(() =>
            {
                gameCanvasGroup.interactable = false;
                gameCanvasGroup.blocksRaycasts = false;
                gameCanvasGroup.gameObject.SetActive(false);
            });
        }

        ResolveHomeMenuPanel();
        if (homeMenuPanel != null) homeMenuPanel.SetActive(true);

        if (homeCanvasGroup != null)
        {
            if (!homeCanvasGroup.gameObject.activeSelf) homeCanvasGroup.gameObject.SetActive(true);
            homeCanvasGroup.DOKill();
            homeCanvasGroup.interactable = true;
            homeCanvasGroup.blocksRaycasts = true;
            homeCanvasGroup.alpha = 0f;

            if (!IsLoadingOverlayVisible())
                homeCanvasGroup.DOFade(1f, 0.4f).SetUpdate(true);
        }

        BGAudioManager.Instance?.OnMenuScreenShown();
    }

    public static void InitializeGameplayScene()
    {
        if (Instance != null)
        {
            Instance.EnsureLocalNetworkPlayer();
            Instance.StartCoroutine(Instance.InitializeGameplaySceneWhenReady());
            return;
        }

        RunInitializeGameplayScene();
    }

    IEnumerator InitializeGameplaySceneWhenReady()
    {
        EnsureLocalNetworkPlayer();

        float timeout = 3f;
        while (PlayerHand.LocalInstance == null && timeout > 0f)
        {
            PlayerHand.ResolveLocalHand();
            if (PlayerHand.LocalInstance == null)
                EnsureLocalNetworkPlayer();
            yield return null;
            timeout -= Time.deltaTime;
        }

        RunInitializeGameplayScene();
    }

    static void RunInitializeGameplayScene()
    {
        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.InitializeGameScene();
        }

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.InitializeGameScene();
        if (TrumpManager.Instance != null)
            TrumpManager.Instance.InitializeGameScene();
    }

    public void ReturnToFriendsModesScreen()
    {
        EnsurePersistentBackdrop();
        _lobbyTransitionRunning = false;
        _joinFadeRoutine = null;

        CleanupRuntimeMatchStateForMenu();
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        isAttemptingRejoin = false;
        ClearPersistedActiveRoomName();
        isPlayBotsMode = false;

        if (ModeManager.Instance != null)
            ModeManager.Instance.ResetStartGuard();

        if (PlayWithFriendsManager.Instance != null)
        {
            bool pendingCreate = PlayWithFriendsManager.IsFriendsPrivateRoomCreatePending();
            if (!pendingCreate)
                PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin = false;
            PlayWithFriendsManager.Instance.ResetSeatPanelUI();
        }

        if (ModeManager.Instance != null)
            ModeManager.Instance.ShowModesScreenOnly();
        else
            EnsureFriendsModesPanelVisible();

        FinalizeMenuTransition();

        PlayWithFriendsManager.Instance?.TryFlushPendingPrivateRoomCreate();

        if (!PhotonNetwork.OfflineMode && HasInternet() && !IsPhotonConnectingOrConnected())
            ConnectToPhoton();

        RefreshPlayOnlineButtonState();
    }

    public void OnJoinRoomFailedRestoreUi()
    {
        _lobbyTransitionRunning = false;
        _joinFadeRoutine = null;
        ForceClearBlackOverlay();
        HideLoadingInstant();
        ClearUiInputBlockers();

        if (ModeManager.Instance != null && ModeManager.Instance.IsFriendsMatchMode
            && PlayWithFriendsManager.Instance != null
            && (UiFlowManager.IsPlayFriendsJoinFlow() || UiFlowManager.Flow == UiFlowKind.PlayFriendsCreate))
            return;

        if (UiFlowManager.IsPlayFriendsJoinFlow())
        {
            ModeManager.Instance?.RestoreJoinTableScreenAfterFailedPin();
            return;
        }

        RestoreMenuPanelAfterLeave();
    }

    void RestoreMenuPanelAfterLeave()
    {
        HideLoadingInstant();
        ForceClearBlackOverlay();
        ClearUiInputBlockers();

        BGAudioManager.Instance?.OnMenuScreenShown();

        if (UiFlowManager.IsReturningHome
            || (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser))
        {
            ReturnToHomeScreen();
            return;
        }

        if (_returnToFriendsModesAfterLeave || PlayWithFriendsManager.IsFriendsPrivateRoomCreatePending())
        {
            ReturnToFriendsModesScreen();
            return;
        }

        if (GameFlowState.Current == GameFlowPhase.Matchmaking || UiFlowManager.IsOnlineMatchmakingFlow())
        {
            GameFlowState.SetPhase(GameFlowPhase.Matchmaking, forceRecovery: true);
            MatchmakingManager.Instance?.ShowMatchmakingPanel();
            return;
        }

        if (UiFlowManager.IsPlayFriendsJoinFlow())
        {
            ModeManager.Instance?.RestoreJoinTableScreenAfterFailedPin();
            return;
        }

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.IsFriendsMatchMode || GameFlowState.Current == GameFlowPhase.ModeSelection)
                ModeManager.Instance.ShowModesScreenOnly();
            else if (UiFlowManager.IsOnlineMatchmakingFlow())
                MatchmakingManager.Instance?.ShowMatchmakingPanel();
        }
    }

    void ResetModesPanelPresentation()
    {
        if (ModeManager.Instance == null || ModeManager.Instance.panelModes == null) return;

        GameObject modes = ModeManager.Instance.panelModes;
        modes.SetActive(true);
        modes.transform.SetAsLastSibling();

        CanvasGroup cg = modes.GetComponent<CanvasGroup>();
        if (cg == null) cg = modes.AddComponent<CanvasGroup>();
        cg.DOKill();
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    public void ClearUiInputBlockers()
    {
        if (loadingCanvasGroup != null)
        {
            if (ShouldPreserveFriendsRoomCreationLoading())
            {
                Debug.Log($"[Loading] Hide blocked due to protected loading flow={_protectedLoadingFlow} token={_protectedLoadingToken}");
            }
            else
            {
                loadingCanvasGroup.DOKill();
                loadingCanvasGroup.alpha = 0f;
                loadingCanvasGroup.interactable = false;
                loadingCanvasGroup.blocksRaycasts = false;
                loadingCanvasGroup.gameObject.SetActive(false);
            }
        }

        if (blackTransitionCanvasGroup != null)
        {
            blackTransitionCanvasGroup.DOKill();
            blackTransitionCanvasGroup.alpha = 0f;
            blackTransitionCanvasGroup.blocksRaycasts = false;
            blackTransitionCanvasGroup.gameObject.SetActive(false);
        }

        CanvasGroup lobby = ResolveRoomLobbyCanvasGroup();
        if (lobby != null)
        {
            lobby.DOKill();
            lobby.blocksRaycasts = false;
            lobby.interactable = false;
        }

        if (ModeManager.Instance != null)
            ModeManager.Instance.HidePlayWithFriendsPanel();

        if (homeCanvasGroup != null)
        {
            bool homePanelActive = ModeManager.Instance != null
                && ModeManager.Instance.panelHomeScreen != null
                && ModeManager.Instance.panelHomeScreen.activeSelf;

            homeCanvasGroup.DOKill();
            if (homePanelActive && GameFlowState.Current == GameFlowPhase.Home)
            {
                homeCanvasGroup.interactable = true;
                homeCanvasGroup.blocksRaycasts = true;
            }
            else
            {
                homeCanvasGroup.interactable = false;
                homeCanvasGroup.blocksRaycasts = false;
                if (!homePanelActive)
                {
                    homeCanvasGroup.alpha = 0f;
                    if (homeCanvasGroup.gameObject.activeSelf)
                        homeCanvasGroup.gameObject.SetActive(false);
                }
            }
        }

        Canvas rootCanvas = gameCanvasGroup != null ? gameCanvasGroup.GetComponentInParent<Canvas>() : null;
        if (rootCanvas == null) rootCanvas = FindAnyObjectByType<Canvas>();
        if (rootCanvas != null)
        {
            foreach (CanvasGroup cg in rootCanvas.GetComponentsInChildren<CanvasGroup>(true))
            {
                if (cg != null && cg.blocksRaycasts && cg.gameObject.activeInHierarchy && cg.alpha < 0.05f)
                    cg.blocksRaycasts = false;
            }
        }
    }

    public void ReturnToHomeScreen()
    {
        // Stale Online/Friends callbacks can arrive just after Bot mode starts.  Do not let them
        // kick the player back home unless the user explicitly pressed a Home/Back action.
        if (IsOfflineBotStickyActive())
        {
            Debug.LogWarning("[Bot Mode] Ignored stale ReturnToHomeScreen during offline bot start.");
            ForceOfflineGameRevealAndKillLoading();
            return;
        }

        if (pendingOfflineMatch) return;

        if (AdsManager.Instance != null)
            AdsManager.Instance.HideBanner();

        _localMatchAbandoned = false;
        StopDisconnectAbandonCoroutine();
        GameFlowState.SetPhase(GameFlowPhase.Home);
        
        // 🚨 AUDIO TIME FIX
        Time.timeScale = 1f;

        CleanupRuntimeMatchStateForMenu();
        CancelReconnectUiForMenu();
        HideReconnectPanels();
        isAttemptingRejoin = false;
        ClearPersistedActiveRoomName();
        isPlayBotsMode = false;

        if (ModeManager.Instance != null)
        {
            ModeManager.Instance.ResetStartGuard();
            ModeManager.Instance.ReturnToHomeClean();
        }

        FinalizeMenuTransition();

        if (HasInternet() && !IsPhotonConnectingOrConnected() && !PhotonNetwork.IsConnectedAndReady)
            ConnectToPhotonForOnlinePlay();

        RefreshPlayOnlineButtonState();
        HideLoading();
        BGAudioManager.Instance?.OnMenuScreenShown();
    }

    void StopOfflineLoadingWatchdog()
    {
        if (_offlineLoadingWatchdogRoutine != null)
        {
            StopCoroutine(_offlineLoadingWatchdogRoutine);
            _offlineLoadingWatchdogRoutine = null;
        }
    }

    IEnumerator OfflineLoadingWatchdog(int token)
{
    yield return new WaitForSecondsRealtime(8f);

    if (token != _offlineStartToken)
        yield break;

    if (!pendingOfflineMatch && !PhotonNetwork.OfflineMode)
        yield break;

    Debug.LogWarning("🤖 [Bot Mode] Watchdog fired — forcing offline game reveal.");

    if (PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
    {
        ForceOfflineGameRevealAndKillLoading();
        yield break;
    }

    if (pendingOfflineMatch)
    {
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
            PhotonNetwork.Disconnect();

        float wait = 2f;
        while (wait > 0f && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
        {
            wait -= Time.unscaledDeltaTime;
            yield return null;
        }

        EnterOfflineModeAndStart(token);
    }
}

    void ForceOfflineGameRevealAndKillLoading()
    {
        if (isPlayBotsMode || PhotonNetwork.OfflineMode)
            ArmOfflineBotSticky(8f);

        EndProtectedLoading(ProtectedLoadingFlow.BotStarting);

        _loginTransitionLoadingActive = false;
        _showingNoInternetOverlay = false;
        _leaveLoadingShownTime = -1f;

        if (_minLeaveLoadingRoutine != null)
        {
            StopCoroutine(_minLeaveLoadingRoutine);
            _minLeaveLoadingRoutine = null;
        }

        StopLoadingSafetyTimeout();
        StopLoadingSliderAnimation();

        ResolveGameCanvasGroup();
        ResolveGameTablePanel();

        if (gameTablePanel != null)
        {
            EnsureOverlayParentActive(gameTablePanel.transform, bringToFront: false);
            gameTablePanel.SetActive(true);
            gameTablePanel.transform.SetAsLastSibling();
        }

        if (gameCanvasGroup != null)
        {
            EnsureOverlayParentActive(gameCanvasGroup.transform, bringToFront: false);
            gameCanvasGroup.gameObject.SetActive(true);
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 1f;
            gameCanvasGroup.interactable = true;
            gameCanvasGroup.blocksRaycasts = true;
        }

        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 0f;
            homeCanvasGroup.interactable = false;
            homeCanvasGroup.blocksRaycasts = false;
            homeCanvasGroup.gameObject.SetActive(false);
        }

        if (homeMenuPanel != null)
            homeMenuPanel.SetActive(false);

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);

            if (ModeManager.Instance.panelHomeScreen != null)
                ModeManager.Instance.panelHomeScreen.SetActive(false);

            ModeManager.Instance.HidePlayWithFriendsPanel();
            ModeManager.Instance.HideJoinTablePanel();
        }

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.HidePrivateFriendsLobbyUI();

        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.DOKill();
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.gameObject.SetActive(false);
        }

        ForceClearBlackOverlay();
        InGameAddFriendController.Instance?.RefreshOpenButtonVisibility();
    }

    public void StartOfflineMatchRequest()
    {
        Debug.Log("🤖 [Bot Mode] Requesting Offline Match...");

        BumpFlowToken("Home -> BotStarting");
        _offlineStartToken++;
        int token = _offlineStartToken;
        _offlineRoomCreateAttempts = 0;

        StopOfflineLoadingWatchdog();
        ArmOfflineBotSticky(15f);
        BeginProtectedLoading(ProtectedLoadingFlow.BotStarting, "Loading game...", token);

        if (_offlineCompleteRoutine != null)
        {
            StopCoroutine(_offlineCompleteRoutine);
            _offlineCompleteRoutine = null;
        }

        if (_offlineStartCoroutine != null)
        {
            StopCoroutine(_offlineStartCoroutine);
            _offlineStartCoroutine = null;
        }

        pendingOfflineMatch = true;
        isPlayBotsMode = true;
        _pendingOnlineMatchmakingAfterLeave = false;
        _returnToFriendsModesAfterLeave = false;
        _pendingPhotonReconnectAfterAuth = false;
        _localMatchAbandoned = false;
        _isLeavingRoom = false;
        isAttemptingRejoin = false;

        ResetGameStartGuards();
        CancelReconnectUiForMenu();
        ClearPersistedActiveRoomName();

        if (GameSettings.Instance != null)
            GameSettings.Instance.currentMatchType = MatchType.OfflineBots;

        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: false);
            MatchmakingManager.Instance.HideMatchmakingPanel();
        }

        if (PlayWithFriendsManager.Instance != null)
        {
            PlayWithFriendsManager.Instance.AbortPendingFriendsRoomCreation();
            PlayWithFriendsManager.Instance.ResetMenuFlowFlags();
            PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();
            PlayWithFriendsManager.Instance.CancelPinJoinUiState();
            PlayWithFriendsManager.Instance.ClearOnlineModeOnly();
            PlayWithFriendsManager.Instance.HidePrivateFriendsLobbyUI();
        }

        _offlineLoadingWatchdogRoutine = StartCoroutine(OfflineLoadingWatchdog(token));
        _offlineStartCoroutine = StartCoroutine(StartOfflineMatchRoutine(token));
    }

    IEnumerator StartOfflineMatchRoutine(int token)
    {
        ShowLoading("Loading game...");
        BringLoadingToFront();
        ForceClearBlackOverlay();

        // IMPORTANT: YAHAN ClearUiInputBlockers() MAT CHALAO.
        // Wo loadingCanvasGroup ko hide/kill kar deta hai aur bot transition race create karta hai.

        if (waitingPanel != null)
            waitingPanel.SetActive(false);

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);

            if (ModeManager.Instance.panelHomeScreen != null)
                ModeManager.Instance.panelHomeScreen.SetActive(false);

            ModeManager.Instance.HidePlayWithFriendsPanel();
            ModeManager.Instance.HideJoinTablePanel();
        }

        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 0f;
            homeCanvasGroup.interactable = false;
            homeCanvasGroup.blocksRaycasts = false;
            homeCanvasGroup.gameObject.SetActive(false);
        }

        if (homeMenuPanel != null)
            homeMenuPanel.SetActive(false);

        GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);

        yield return new WaitForSecondsRealtime(0.1f);

        if (token != _offlineStartToken)
            yield break;

        if (PhotonNetwork.InRoom)
        {
            Debug.Log("🤖 [Bot Mode] Leaving stale Online/Friends room before offline start...");
            PhotonNetwork.LeaveRoom();

            float leaveTimeout = 6f;
            while (token == _offlineStartToken && PhotonNetwork.InRoom && leaveTimeout > 0f)
            {
                leaveTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (token != _offlineStartToken)
            yield break;

        if (PhotonNetwork.InLobby && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LeaveLobby();

            float lobbyTimeout = 2f;
            while (token == _offlineStartToken && PhotonNetwork.InLobby && lobbyTimeout > 0f)
            {
                lobbyTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (token != _offlineStartToken)
            yield break;

        if (!PhotonNetwork.OfflineMode && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
        {
            Debug.Log($"🤖 [Bot Mode] Disconnecting Photon Cloud before OfflineMode. State={PhotonNetwork.NetworkClientState}");
            PhotonNetwork.Disconnect();

            float disconnectTimeout = 8f;
            while (token == _offlineStartToken
                && PhotonNetwork.NetworkClientState != ClientState.Disconnected
                && disconnectTimeout > 0f)
            {
                disconnectTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (token != _offlineStartToken)
            yield break;

        yield return null;
        EnterOfflineModeAndStart(token);
    }

    IEnumerator EnterOfflineModeDeferred(int token)
    {
        float timeout = 8f;
        while (token == _offlineStartToken
               && PhotonNetwork.NetworkClientState != ClientState.Disconnected
               && timeout > 0f)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (token != _offlineStartToken) yield break;
        yield return null;
        EnterOfflineModeAndStart(token);
    }

    private void EnterOfflineModeAndStart(int token)
    {
        if (token != _offlineStartToken) return;

        // Do not create/spawn anything until the cloud client is fully disconnected.
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.NetworkClientState != ClientState.Disconnected)
        {
            _offlineStartCoroutine = StartCoroutine(EnterOfflineModeDeferred(token));
            return;
        }

        if (_offlineCreateRoomRoutine != null)
            StopCoroutine(_offlineCreateRoomRoutine);
        _offlineCreateRoomRoutine = StartCoroutine(EnterOfflineModeAndCreateRoomRoutine(token));
    }

    IEnumerator EnterOfflineModeAndCreateRoomRoutine(int token)
    {
        if (token != _offlineStartToken) yield break;

        Debug.Log("🤖 [Bot Mode] Entering Offline Mode + creating local room directly from NetworkManager...");

        if (!PhotonNetwork.OfflineMode)
            PhotonNetwork.OfflineMode = true;

        isPlayBotsMode = true;
        pendingOfflineMatch = true;
        _isLeavingRoom = false;

        float readyWait = 10f;
        while (readyWait > 0f && token == _offlineStartToken && !PhotonNetwork.InRoom)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                readyWait -= Time.unscaledDeltaTime;
                yield return null;
                continue;
            }
            break;
        }

        if (token != _offlineStartToken) yield break;

        if (PhotonNetwork.InRoom)
        {
            if (_offlineCompleteRoutine != null)
                StopCoroutine(_offlineCompleteRoutine);
            _offlineCompleteRoutine = StartCoroutine(CompleteOfflineBotRoomStart());
            _offlineCreateRoomRoutine = null;
            yield break;
        }

        TryCreateOfflineBotRoom(token);
        _offlineCreateRoomRoutine = null;
    }

    void TryCreateOfflineBotRoom(int token)
    {
        if (token != _offlineStartToken) return;
        if (PhotonNetwork.InRoom) return;

        _offlineRoomCreateAttempts++;
        Debug.Log($"[BotMode] Creating offline bot room attempt={_offlineRoomCreateAttempts} state={PhotonNetwork.NetworkClientState}");

        string roomName = "Local_Bot_" + System.DateTime.UtcNow.Ticks + "_" + Random.Range(1000, 9999);
        RoomOptions options = new RoomOptions
        {
            MaxPlayers = 4,
            IsOpen = true,
            IsVisible = false,
            PlayerTtl = 0,
            EmptyRoomTtl = 0
        };
        PhotonNetwork.CreateRoom(roomName, options);
    }

    void EnsureLoadingDoesNotBlockUI()
    {
        if (loadingCanvasGroup == null) return;
        loadingCanvasGroup.blocksRaycasts = false;
        loadingCanvasGroup.interactable = false;
    }

    void EnsureLoadingImagesLoaded()
    {
        if (loadingCanvasGroup == null) return;

        AddressableUIImageLoader[] loaders =
            loadingCanvasGroup.GetComponentsInChildren<AddressableUIImageLoader>(true);
        if (loaders == null || loaders.Length == 0) return;

        var keys = new System.Collections.Generic.List<string>(loaders.Length);
        foreach (AddressableUIImageLoader loader in loaders)
        {
            if (loader != null && !string.IsNullOrWhiteSpace(loader.addressableKey))
                keys.Add(loader.addressableKey);
        }

        StartCoroutine(EnsureLoadingImagesLoadedRoutine(loaders, keys));
    }

    IEnumerator EnsureLoadingImagesLoadedRoutine(
        AddressableUIImageLoader[] loaders,
        System.Collections.Generic.List<string> keys)
    {
        if (keys.Count > 0)
            yield return AddressablesSpriteCache.PreloadKeysRoutine(keys);

        foreach (AddressableUIImageLoader loader in loaders)
        {
            if (loader != null)
                loader.EnsureLoaded();
        }
    }

    public void ShowLoading(string message)
    {
        ShowLoadingFadeIn(message, 0f);
    }

    public void BeginLoginTransitionLoading(string message)
    {
        _loginTransitionLoadingActive = true;
        ShowLoadingFadeIn(message, 0f);
    }

    public void EndLoginTransitionLoading()
    {
        _loginTransitionLoadingActive = false;

        if (IsHomePreparedBehindLoading() && IsLoadingOverlayVisible())
        {
            CrossfadeLoadingToCanvasGroup(homeCanvasGroup);
            return;
        }

        HideLoading();
    }

    public void ShowLoading(string message, float sliderDurationSeconds)
    {
        ShowLoadingFadeIn(message, 0f);
        PrepareLoadingSliderReset();
        if (sliderDurationSeconds > 0f)
            AnimateLoadingSlider(sliderDurationSeconds);
        else
            StopLoadingSliderAnimation();
    }

    static float ResolveLoadingSliderDuration(string message)
    {
        if (string.IsNullOrEmpty(message)) return 3f;

        string m = message.ToLowerInvariant();
        if (m.Contains("connecting online")) return 5f;
        if (m.Contains("leaving room")) return 2.5f;
        if (m.Contains("creating room")) return 4f;
        if (m.Contains("joining friend") || m.Contains("joining game")) return 4f;
        if (m.Contains("loading game") || m.Contains("starting game")) return GameStartLoadingDelaySeconds;
        if (m.Contains("reconnect") || m.Contains("signing in") || m.Contains("fetching")
            || m.Contains("profile") || m.Contains("google account"))
            return 0f;

        return 3f;
    }

    void PrepareLoadingSliderReset()
    {
        _loadingSlider = null;
        Slider slider = ResolveLoadingSlider();
        if (slider != null)
            slider.value = 0f;
    }

    void BeginLoadingSlider(string message)
    {
        PrepareLoadingSliderReset();
        float duration = ResolveLoadingSliderDuration(message);
        if (duration > 0f)
            AnimateLoadingSlider(duration);
        else
            StopLoadingSliderAnimation();
    }

    Slider ResolveLoadingSlider()
    {
        if (_loadingSlider != null) return _loadingSlider;
        if (loadingCanvasGroup != null)
            _loadingSlider = loadingCanvasGroup.GetComponentInChildren<Slider>(true);
        if (_loadingSlider == null && UiSafeLookup.TryGetPath("LoadingSlider", out GameObject sliderGo) && sliderGo != null)
            _loadingSlider = sliderGo.GetComponent<Slider>();
        return _loadingSlider;
    }

    void SetLoadingAnimationExternalControl(bool external)
    {
        if (loadingCanvasGroup == null) return;
        LoadingAnimation anim = loadingCanvasGroup.GetComponentInChildren<LoadingAnimation>(true);
        if (anim != null)
            anim.useSimulatedProgress = !external;
    }

    public void AnimateLoadingSlider(float duration)
    {
        if (duration <= 0f) return;
        if (_loadingSliderRoutine != null)
            StopCoroutine(_loadingSliderRoutine);
        SetLoadingAnimationExternalControl(true);
        _loadingSliderRoutine = StartCoroutine(AnimateLoadingSliderRoutine(duration));
    }

    IEnumerator AnimateLoadingSliderRoutine(float duration)
    {
        Slider slider = ResolveLoadingSlider();
        if (slider == null)
        {
            _loadingSliderRoutine = null;
            yield break;
        }

        slider.value = 0f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            slider.value = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }

        slider.value = 1f;
        _loadingSliderRoutine = null;
    }

    void StopLoadingSliderAnimation()
    {
        if (_loadingSliderRoutine != null)
        {
            StopCoroutine(_loadingSliderRoutine);
            _loadingSliderRoutine = null;
        }
        SetLoadingAnimationExternalControl(false);
    }

    public void CompleteLoadingSlider()
    {
        StopLoadingSliderAnimation();
        Slider slider = ResolveLoadingSlider();
        if (slider != null)
            slider.value = 1f;
    }

    void StopLoadingSafetyTimeout()
    {
        if (_loadingTimeoutCoroutine != null)
        {
            StopCoroutine(_loadingTimeoutCoroutine);
            _loadingTimeoutCoroutine = null;
        }
    }

    void StartLoadingSafetyTimeout()
    {
        StopLoadingSafetyTimeout();
        _loadingTimeoutCoroutine = StartCoroutine(LoadingSafetyTimeout());
    }

    IEnumerator LoadingSafetyTimeout()
    {
        yield return new WaitForSeconds(10f);
        _loadingTimeoutCoroutine = null;

        if (loadingCanvasGroup == null || !loadingCanvasGroup.gameObject.activeSelf)
            yield break;

        if (ShouldPreserveFriendsRoomCreationLoading())
            yield break;

        if (IsBotsFlowActive())
        {
            if (PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
                ForceOfflineGameRevealAndKillLoading();

            yield break;
        }

        HideLoadingInstant();
        CancelReconnectUiForMenu();
        pendingOfflineMatch = false;
        isPlayBotsMode = false;
        PhotonNetwork.OfflineMode = false;

        if (GameFlowState.IsActivelyPlaying)
            yield break;

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
        else
            RestoreMenuPanelAfterLeave();
    }

    // 🚨 GHOST STATE FIX: Ab loading screen 100% zaroor dikhegi!
    public void ShowLoadingFadeIn(string message, float duration = 0.2f, System.Action onFadeComplete = null)
    {
        string lowerMsg = message != null ? message.ToLowerInvariant() : "";

        if (!string.IsNullOrEmpty(lowerMsg)
            && (lowerMsg.Contains("connecting") || lowerMsg.Contains("loading game") || lowerMsg.Contains("creating room") || lowerMsg.Contains("leaving room")))
        {
        }

        if (GameFlowState.IsActivelyPlaying && lowerMsg.Contains("loading game"))
        {
            onFadeComplete?.Invoke();
            return;
        }

        HideReconnectPanels();

        if (loadingText != null) loadingText.text = message;
        lastStatusMessage = message;

        bool isLeaveLoading = !string.IsNullOrEmpty(message)
            && message.ToLowerInvariant().Contains("leaving room");
        if (isLeaveLoading)
        {
            _leaveLoadingShownTime = Time.unscaledTime;
        }
        else if (_leaveLoadingShownTime >= 0f)
        {
            _leaveLoadingShownTime = -1f;
            if (_minLeaveLoadingRoutine != null)
            {
                StopCoroutine(_minLeaveLoadingRoutine);
                _minLeaveLoadingRoutine = null;
            }
        }

        if (loadingCanvasGroup == null)
        {
            onFadeComplete?.Invoke();
            return;
        }

        bool alreadyVisible = loadingCanvasGroup.gameObject.activeSelf && loadingCanvasGroup.alpha > 0.9f;

        loadingCanvasGroup.DOKill();
        loadingCanvasGroup.gameObject.SetActive(true);
        loadingCanvasGroup.interactable = true;
        loadingCanvasGroup.blocksRaycasts = true;
        BringLoadingToFront();
        StartLoadingSafetyTimeout();

        EnsureLoadingImagesLoaded();

        if (!string.IsNullOrEmpty(message))
            BeginLoadingSlider(message);

        if (alreadyVisible || duration <= 0f)
        {
            loadingCanvasGroup.alpha = 1f;
            onFadeComplete?.Invoke();
            return;
        }

        loadingCanvasGroup.alpha = 0f;
        loadingCanvasGroup.DOFade(1f, duration).SetUpdate(true).OnComplete(() => onFadeComplete?.Invoke());
    }

    public void BeginJoinRoomWithLoadingFade(string roomPin, string message = "Joining game...")
    {
        if (_joinFadeRoutine != null)
            StopCoroutine(_joinFadeRoutine);
        _joinFadeRoutine = StartCoroutine(BeginJoinRoomWithLoadingFadeRoutine(roomPin, message));
    }

    IEnumerator BeginJoinRoomWithLoadingFadeRoutine(string roomPin, string message)
    {
        if (string.IsNullOrWhiteSpace(roomPin))
            yield break;

        EnsurePersistentBackdrop();
        SnapScreenCover();

        bool fadeDone = false;
        ShowLoadingFadeIn(message, joinLoadingFadeIn, () => fadeDone = true);
        while (!fadeDone)
            yield return null;

        float leaveWait = 12f;
        while (PhotonNetwork.InRoom && leaveWait > 0f)
        {
            leaveWait -= Time.unscaledDeltaTime;
            yield return null;
        }

        float readyWait = 12f;
        while (readyWait > 0f
            && (!PhotonNetwork.IsConnectedAndReady
                || PhotonNetwork.Server != Photon.Realtime.ServerConnection.MasterServer))
        {
            readyWait -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (PhotonNetwork.InRoom)
        {
            ForceClearBlackOverlay();
            OnJoinRoomFailedRestoreUi();
            _joinFadeRoutine = null;
            yield break;
        }

        PlayWithFriendsManager.PendingJoinPin = null;
        PhotonNetwork.JoinRoom(roomPin.Trim());
        _joinFadeRoutine = null;
    }

    public void BeginGameTransitionWithBlackFade(System.Action whileScreenBlack, bool skipFadeIn = false)
    {
        StartCoroutine(GameStartBlackFadeRoutine(whileScreenBlack, skipFadeIn));
    }

    public void ForceClearBlackOverlay()
    {
        if (_joinFadeRoutine != null)
        {
            StopCoroutine(_joinFadeRoutine);
            _joinFadeRoutine = null;
        }

        if (blackTransitionCanvasGroup != null)
        {
            blackTransitionCanvasGroup.DOKill();
            blackTransitionCanvasGroup.alpha = 0f;
            blackTransitionCanvasGroup.blocksRaycasts = false;
            blackTransitionCanvasGroup.interactable = false;
            blackTransitionCanvasGroup.gameObject.SetActive(false);
        }

        GameObject sceneOverlay = GameObject.Find("BlackTransitionOverlay");
        if (sceneOverlay != null && (blackTransitionCanvasGroup == null
            || sceneOverlay != blackTransitionCanvasGroup.gameObject))
        {
            var cg = sceneOverlay.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
            sceneOverlay.SetActive(false);
        }
    }

    public void CancelPinJoinUiOverlays()
    {
        _lobbyTransitionRunning = false;
        ForceClearBlackOverlay();
        HideLoadingInstant();
    }

    public void EnsureNoBlackScreen()
    {
        ForceClearBlackOverlay();

        if (IsAnyMainScreenVisible())
            return;

        RestoreMainScreenForCurrentPhase();
    }

    bool IsAnyMainScreenVisible()
    {
        if (gameCanvasGroup != null && gameCanvasGroup.gameObject.activeSelf && gameCanvasGroup.alpha > 0.85f)
            return true;

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelHomeScreen != null && ModeManager.Instance.panelHomeScreen.activeSelf)
                return true;
            if (ModeManager.Instance.panelModes != null && ModeManager.Instance.panelModes.activeSelf)
                return true;
            GameObject friends = ModeManager.Instance.ResolvePlayWithFriendsPanel();
            if (friends != null && friends.activeSelf)
                return true;
        }

        if (loadingCanvasGroup != null && loadingCanvasGroup.gameObject.activeSelf && loadingCanvasGroup.alpha > 0.15f)
            return true;

        return false;
    }

    void RestoreMainScreenForCurrentPhase()
    {
        HideLoadingInstant();
        CancelReconnectUiForMenu();

        switch (GameFlowState.Current)
        {
            case GameFlowPhase.ModeSelection:
                if (UiFlowManager.IsPlayFriendsJoinFlow())
                    ModeManager.Instance?.RestoreJoinTableScreenAfterFailedPin();
                else if (ModeManager.Instance != null)
                    ModeManager.Instance.ShowModesScreenOnly();
                break;
            case GameFlowPhase.Matchmaking:
                if (MatchmakingManager.Instance != null)
                    MatchmakingManager.Instance.ShowMatchmakingPanel();
                break;
            case GameFlowPhase.InRoom:
                if (IsBotsFlowActive())
                {
                    if (loadingCanvasGroup != null && loadingCanvasGroup.gameObject.activeSelf)
                        return;
                    ShowGameScene(showLoadingOverlay: true);
                    return;
                }
                if (ModeManager.Instance != null && ModeManager.Instance.IsFriendsMatchMode)
                    ModeManager.Instance.ShowPlayWithFriendsPanel();
                else if (UiFlowManager.IsOnlineMatchmakingFlow())
                    MatchmakingManager.Instance?.ShowMatchmakingPanel();
                else if (!UiFlowManager.IsReturningHome)
                    ModeManager.Instance?.ShowModesScreenOnly();
                break;
            case GameFlowPhase.Dealing:
            case GameFlowPhase.InGame:
            case GameFlowPhase.ResolvingTrick:
                ShowGameScene(showLoadingOverlay: false);
                break;
            default:
                if (UiFlowManager.IsReturningHome)
                    ModeManager.Instance?.ShowHomeScreenOnly();
                else if (ModeManager.Instance != null)
                    ModeManager.Instance.ShowModesScreenOnly();
                break;
        }
    }

    void EnsureGameUiVisibleForReveal()
    {
        if (gameCanvasGroup != null)
        {
            EnsureOverlayParentActive(gameCanvasGroup.transform, bringToFront: false);
            if (!gameCanvasGroup.gameObject.activeSelf)
                gameCanvasGroup.gameObject.SetActive(true);
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 1f;
            gameCanvasGroup.interactable = true;
            gameCanvasGroup.blocksRaycasts = true;
        }

        ResolveGameTablePanel();
        if (gameTablePanel != null && !gameTablePanel.activeSelf)
            gameTablePanel.SetActive(true);
    }

    IEnumerator GameStartBlackFadeRoutine(System.Action whileScreenBlack, bool skipFadeIn)
    {
        CanvasGroup black = null;
        try
        {
            EnsurePersistentBackdrop();
            black = EnsureBlackTransitionCanvas();
            EnsureOverlayParentActive(black.transform);
            black.gameObject.SetActive(true);
            black.DOKill();
            black.blocksRaycasts = true;
            black.interactable = false;
            black.transform.SetAsLastSibling();

            if (skipFadeIn)
            {
                black.alpha = 1f;
                yield return null;
            }
            else
            {
                black.alpha = 0f;
                yield return black.DOFade(1f, gameStartBlackFade).SetUpdate(true).WaitForCompletion();
            }

            try { whileScreenBlack?.Invoke(); }
            catch (System.Exception ex) { Debug.LogError("[Friends] Game start callback failed: " + ex.Message); }

            EnsureGameUiVisibleForReveal();

            yield return null;
            yield return new WaitForEndOfFrame();

            float wait = 2f;
            while (wait > 0f && !IsGameSceneReadyForReveal())
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }

            EnsureGameUiVisibleForReveal();

            if (black != null)
            {
                yield return black.DOFade(0f, gameStartBlackFadeOut).SetUpdate(true).WaitForCompletion();
                black.blocksRaycasts = false;
                black.gameObject.SetActive(false);
            }
        }
        finally
        {
            ForceClearBlackOverlay();
        }
    }

    bool IsGameSceneReadyForReveal()
    {
        ResolveGameTablePanel();
        if (gameCanvasGroup == null) return true;
        if (!gameCanvasGroup.gameObject.activeInHierarchy || gameCanvasGroup.alpha < 0.95f) return false;
        if (gameTablePanel == null) return true;
        return gameTablePanel.activeInHierarchy;
    }

    void EnsureOverlayParentActive(Transform overlay, bool bringToFront = true)
    {
        if (overlay == null) return;
        Transform t = overlay.parent;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }
        if (bringToFront)
            overlay.SetAsLastSibling();
    }

    public void EnsureFriendsModesPanelVisible()
    {
        HideLoading();

        if (ModeManager.Instance != null)
            ModeManager.Instance.ShowModesScreenOnly();
        else
            HideHomeMenuCanvas();

        EnsurePersistentBackdrop();

        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 0f;
            gameCanvasGroup.interactable = false;
            gameCanvasGroup.blocksRaycasts = false;
            gameCanvasGroup.gameObject.SetActive(false);
        }
    }

    public void ResetRoomLobbyCanvasGroup()
    {
        CanvasGroup lobby = ResolveRoomLobbyCanvasGroup();
        if (lobby == null) return;

        lobby.DOKill();
        lobby.alpha = 1f;
        lobby.interactable = true;
        lobby.blocksRaycasts = true;
    }

    Transform ResolveUiOverlayRoot()
    {
        if (loadingCanvasGroup != null) return loadingCanvasGroup.transform.parent;
        if (gameCanvasGroup != null) return gameCanvasGroup.transform.parent;
        if (homeCanvasGroup != null) return homeCanvasGroup.transform.parent;

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null) return canvas.transform;

        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas c in canvases)
        {
            if (c != null && c.isActiveAndEnabled && c.gameObject.scene.IsValid())
                return c.transform;
        }

        return transform;
    }

    CanvasGroup EnsureBlackTransitionCanvas()
    {
        if (blackTransitionCanvasGroup != null)
            return blackTransitionCanvasGroup;

        var go = new GameObject("BlackTransitionOverlay", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        Transform root = ResolveUiOverlayRoot();
        go.transform.SetParent(root, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = true;

        blackTransitionCanvasGroup = go.GetComponent<CanvasGroup>();
        blackTransitionCanvasGroup.alpha = 0f;
        blackTransitionCanvasGroup.blocksRaycasts = false;
        go.SetActive(false);
        return blackTransitionCanvasGroup;
    }

    CanvasGroup ResolveRoomLobbyCanvasGroup()
    {
        if (roomLobbyCanvasGroup != null) return roomLobbyCanvasGroup;

        if (ModeManager.Instance != null)
        {
            GameObject lobbyPanel = ModeManager.Instance.ResolvePlayWithFriendsPanel();
            if (lobbyPanel != null)
            {
                roomLobbyCanvasGroup = lobbyPanel.GetComponent<CanvasGroup>();
                if (roomLobbyCanvasGroup == null)
                    roomLobbyCanvasGroup = lobbyPanel.AddComponent<CanvasGroup>();
            }
        }
        return roomLobbyCanvasGroup;
    }

    void PrepareLobbyPanelsForTransition(bool isHost)
    {
        EnsurePersistentBackdrop();

        if (ModeManager.Instance != null)
            ModeManager.Instance.ShowPlayWithFriendsPanel();

        PlayWithFriendsManager pwf = ResolvePlayWithFriendsManager();
        if (pwf != null)
            pwf.ShowPrivateRoomLobbyUI();

        if (waitingPanel != null) waitingPanel.SetActive(false);

        if (modePanel == null && ModeManager.Instance != null)
            modePanel = ModeManager.Instance.panelModes;
        if (modePanel != null) modePanel.SetActive(false);

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);
            if (ModeManager.Instance.panelHomeScreen != null)
                ModeManager.Instance.panelHomeScreen.SetActive(false);
        }

        PrepareForPrivateRoomLobby(showHomeMenu: false);
    }

    public IEnumerator SmoothTransitionToRoomLobby()
    {
        if (_lobbyTransitionRunning) yield break;
        _lobbyTransitionRunning = true;

        if (PhotonNetwork.CurrentRoom != null)
            PersistActiveRoomName(PhotonNetwork.CurrentRoom.Name);

        GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);
        StopDisconnectAbandonCoroutine();
        _localMatchAbandoned = false;

        bool isHost = PhotonNetwork.IsMasterClient;
        PrepareLobbyPanelsForTransition(isHost);

        CanvasGroup lobby = ResolveRoomLobbyCanvasGroup();
        if (lobby != null)
        {
            lobby.gameObject.SetActive(true);
            lobby.DOKill();
            lobby.alpha = 0f;
            lobby.interactable = true;
            lobby.blocksRaycasts = true;
            lobby.transform.SetAsLastSibling();
        }

        if (loadingCanvasGroup != null && loadingCanvasGroup.gameObject.activeSelf)
        {
            loadingCanvasGroup.DOKill();
            Tween loadOut = loadingCanvasGroup.DOFade(0f, joinLoadingFadeOut).SetUpdate(true);
            Tween lobbyIn = lobby != null ? lobby.DOFade(1f, lobbyFadeIn).SetUpdate(true) : null;

            if (lobbyIn != null)
                yield return DOTween.Sequence().Join(loadOut).Join(lobbyIn).WaitForCompletion();
            else
                yield return loadOut.WaitForCompletion();

            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.gameObject.SetActive(false);
        }
        else if (lobby != null)
        {
            yield return lobby.DOFade(1f, lobbyFadeIn).SetUpdate(true).WaitForCompletion();
        }

        HideReconnectPanels();
        isAttemptingRejoin = false;
        _lobbyTransitionRunning = false;

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.BeginLobbyPlayerListRefresh();

        ForceClearBlackOverlay();
    }

    void BringLoadingToFront()
    {
        if (loadingCanvasGroup == null) return;
        loadingCanvasGroup.transform.SetAsLastSibling();
    }

    bool DeferHideForMinimumLeaveDuration(bool instant)
    {
        if (_leaveLoadingShownTime < 0f) return false;

        float elapsed = Time.unscaledTime - _leaveLoadingShownTime;
        if (elapsed >= LeaveLoadingMinSeconds)
        {
            _leaveLoadingShownTime = -1f;
            return false;
        }

        float remaining = LeaveLoadingMinSeconds - elapsed;
        if (_minLeaveLoadingRoutine != null)
            StopCoroutine(_minLeaveLoadingRoutine);
        _minLeaveLoadingRoutine = StartCoroutine(HideAfterMinimumLeaveDuration(remaining, instant));
        return true;
    }

    IEnumerator HideAfterMinimumLeaveDuration(float delay, bool instant)
    {
        yield return new WaitForSecondsRealtime(delay);
        _minLeaveLoadingRoutine = null;
        _leaveLoadingShownTime = -1f;
        if (instant) HideLoadingInstant();
        else HideLoading();
    }

    bool ShouldPreserveFriendsRoomCreationLoading()
    {
        if (IsProtectedLoadingActive()) return true;
        if (ModeManager.Instance != null && ModeManager.Instance.IsFriendsConnectionBufferActive())
            return true;
        if (PlayWithFriendsManager.Instance != null && PlayWithFriendsManager.Instance.IsAwaitingFriendsSeatLobby())
            return true;
        return false;
    }

    public void HideLoading()
    {
        if (_loginTransitionLoadingActive) return;
        if (_showingNoInternetOverlay && !HasInternet()) return;
        if (DeferHideForMinimumLeaveDuration(false)) return;
        if (ShouldPreserveFriendsRoomCreationLoading())
        {
            Debug.Log($"[Loading] Hide blocked due to protected Friends loading token={_protectedLoadingToken}");
            return;
        }

        _showingNoInternetOverlay = false;
        if (loadingCanvasGroup == null) return;

        StopLoadingSafetyTimeout();

        if (IsHomePreparedBehindLoading() && IsLoadingOverlayVisible())
        {
            CrossfadeLoadingToCanvasGroup(homeCanvasGroup);
            return;
        }

        if (gameCanvasGroup != null && gameCanvasGroup.gameObject.activeSelf && gameCanvasGroup.alpha < 0.95f)
        {
            CrossfadeLoadingToCanvasGroup(gameCanvasGroup, 0.35f, 0.25f);
            return;
        }

        loadingCanvasGroup.DOKill();
        loadingCanvasGroup.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => {
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.gameObject.SetActive(false);
            StopLoadingSliderAnimation();
        });

        if (GoogleLogin.HasCompletedLoginFlow && PhotonNetwork.InLobby && homeCanvasGroup != null && homeCanvasGroup.alpha < 0.1f)
            UpdateUIState(true);
    }

    public void HideLoadingInstant()
    {
        _loginTransitionLoadingActive = false; 
        if (DeferHideForMinimumLeaveDuration(true)) return;
        if (ShouldPreserveFriendsRoomCreationLoading())
        {
            Debug.Log($"[Loading] Hide blocked due to protected Friends loading token={_protectedLoadingToken}");
            return;
        }

        StopLoadingSafetyTimeout();
        CompleteLoadingSlider();

        if (loadingCanvasGroup == null) return;
        loadingCanvasGroup.DOKill();
        loadingCanvasGroup.alpha = 0f;
        loadingCanvasGroup.interactable = false;
        loadingCanvasGroup.blocksRaycasts = false;
        loadingCanvasGroup.gameObject.SetActive(false);
    }

    public void LogError(string msg)
    {
        lastErrorMessage = msg;
        Debug.LogError($"[Photon] {msg}");
    }

    public override void OnConnectedToMaster() 
    { 
        ApplyPhotonPeerTuning();
        lastStatusMessage = "Connected to Master";

        if (PhotonNetwork.OfflineMode || IsBotsFlowActive())
        {
            return;
        }

        if (_localMatchAbandoned)
        {
            HideLoading();
            HideReconnectPanels();
            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();
            else if (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
                EnsureJoinLobby();
            return;
        }

        if (GameFlowState.Current == GameFlowPhase.Matchmaking)
        {
            HideLoading();
            if (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
                EnsureJoinLobby();
            else if (ModeManager.Instance != null)
                ModeManager.Instance.StartSmartMatchmakingFromNetwork();
            return;
        }

        if (isAttemptingRejoin)
        {
            if (!PhotonNetwork.InRoom && !string.IsNullOrEmpty(storedRoomName) && PhotonNetwork.NetworkClientState != ClientState.Joining)
                PhotonNetwork.RejoinRoom(storedRoomName);
            return;
        }

        if (PlayWithFriendsManager.IsFriendsPrivateRoomCreatePending())
        {
            PlayWithFriendsManager.Instance?.TryFlushPendingPrivateRoomCreate();
            RefreshPlayOnlineButtonState();
            return;
        }

        EnsureJoinLobby();
        RefreshPlayOnlineButtonState();
    }

    public bool IsAttemptingRejoin => isAttemptingRejoin;

    public void ClearRejoinState() => isAttemptingRejoin = false;

    void HandleFatalDisconnect(DisconnectCause cause)
    {
        if (pendingOfflineMatch)
        {
            StartCoroutine(EnterOfflineModeDeferred(_offlineStartToken));
            return;
        }

        StopLoadingSafetyTimeout();
        HideLoadingInstant();
        CancelReconnectUiForMenu();
        isAttemptingRejoin = false;
        pendingOfflineMatch = false;
        isPlayBotsMode = false;
        PhotonNetwork.OfflineMode = false;
        StopDisconnectAbandonCoroutine();
        StopAutoReconnectRoutine();
        HideReconnectPanels();
        ClearPersistedActiveRoomName();

        bool menuPhase = GameFlowState.Current == GameFlowPhase.Home
            || GameFlowState.Current == GameFlowPhase.ModeSelection
            || GameFlowState.Current == GameFlowPhase.Matchmaking;

        if (menuPhase && !UiFlowManager.IsReturningHome)
        {
            RestoreMenuPanelAfterLeave();
            if (HasInternet())
                StartCoroutine(ReconnectIdleRoutine());
            return;
        }

        GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true);
        ReturnToHomeScreen();

        if (HasInternet())
            PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"[Photon] Disconnected! Cause: {cause}");
        lastErrorMessage = cause.ToString();

        if (!pendingOfflineMatch && !isPlayBotsMode)
            HideLoadingInstant();

        if (pendingOfflineMatch)
        {
            if (_offlineStartCoroutine == null)
                _offlineStartCoroutine = StartCoroutine(EnterOfflineModeDeferred(_offlineStartToken));
            RefreshPlayOnlineButtonState();
            return;
        }

        if (cause == DisconnectCause.DisconnectByClientLogic)
        {
            RefreshPlayOnlineButtonState();
            return;
        }

        if (IsFatalDisconnect(cause))
        {
            HandleFatalDisconnect(cause);
            RefreshPlayOnlineButtonState();
            return;
        }

        if (PhotonNetwork.CurrentRoom != null)
            PersistActiveRoomName(PhotonNetwork.CurrentRoom.Name);
        else
            RestoreActiveRoomNameIfNeeded();

        if (_pendingPhotonReconnectAfterAuth)
        {
            _pendingPhotonReconnectAfterAuth = false;
            if (HasInternet()) ConnectToPhoton(); else ShowNoInternetLoading();
            RefreshPlayOnlineButtonState();
            return;
        }

        if (!_showingNoInternetOverlay && !ShouldKeepLoadingVisibleAfterDisconnect())
            HideLoading();

        if (!HasInternet())
            ShowNoInternetLoading();

        if (pendingOfflineMatch)
        {
            RefreshPlayOnlineButtonState();
            return;
        }
        else if (cause != DisconnectCause.DisconnectByClientLogic && cause != DisconnectCause.None)
        {
            bool wasInMatch = isAttemptingRejoin
                || GameFlowState.Current == GameFlowPhase.InGame
                || GameFlowState.Current == GameFlowPhase.InRoom
                || GameFlowState.Current == GameFlowPhase.Dealing
                || GameFlowState.Current == GameFlowPhase.ResolvingTrick
                || GameFlowState.Current == GameFlowPhase.Disconnected
                || (gameCanvasGroup != null && gameCanvasGroup.alpha > 0.1f);

            if (GameFlowState.Current == GameFlowPhase.Matchmaking)
            {
                if (ModeManager.Instance != null) ModeManager.Instance.ScheduleMatchmakingAfterLobby();
                HideLoading();
                StartCoroutine(ReconnectForMatchmakingRoutine());
            }
            else if (wasInMatch && !isAttemptingRejoin)
            {
                StartCoroutine(DeferredBeginInMatchDisconnectFlow());
            }
            else if (isAttemptingRejoin && HasInternet())
            {
                StartCoroutine(DeferredTryReconnectToMatch());
            }
            else if (!wasInMatch && !isAttemptingRejoin && HasInternet()
                     && (GameFlowState.Current == GameFlowPhase.Home
                         || cause == DisconnectCause.ClientTimeout
                         || cause == DisconnectCause.ServerTimeout))
            {
                StartCoroutine(ReconnectIdleRoutine());
            }
            else if (cause == DisconnectCause.ClientTimeout || cause == DisconnectCause.ServerTimeout)
            {
                StartCoroutine(ReconnectIdleRoutine());
            }
        }

        RefreshPlayOnlineButtonState();
    }

    System.Collections.IEnumerator ReconnectForMatchmakingRoutine()
    {
        yield return new WaitForSeconds(1f);
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
            yield break;

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();

        float wait = 0f;
        while (wait < 25f && GameFlowState.Current == GameFlowPhase.Matchmaking)
        {
            if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
                yield break;

            if (PhotonNetwork.IsConnectedAndReady)
            {
                HideLoading();
                if (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
                    EnsureJoinLobby();
                else if (ModeManager.Instance != null)
                    ModeManager.Instance.StartSmartMatchmakingFromNetwork();
                yield break;
            }
            yield return new WaitForSeconds(1f);
            wait += 1f;
        }
    }

    public override void OnJoinedLobby() 
    { 
        lastStatusMessage = "Joined lobby";

        // BOT MODE ME LATE LOBBY CALLBACK HOME/LOADING STATE KHARAB NA KARE
        if (IsBotsFlowActive())
        {
            RefreshPlayOnlineButtonState();
            return;
        }

        // PlayFriends can be queued immediately after leaving Bot/Offline mode.
        // Do NOT reset to Home here, otherwise the first PlayFriends attempt gets cancelled.
        if (PlayWithFriendsManager.IsFriendsPrivateRoomCreatePending())
        {
            PlayWithFriendsManager.Instance?.TryFlushPendingPrivateRoomCreate();
            RefreshPlayOnlineButtonState();
            return;
        }

        GameFlowState.SetPhase(GameFlowPhase.Home);

        if (!_showingNoInternetOverlay)
            HideLoading();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.EnsureFriendServicesStarted();

        RefreshPlayOnlineButtonState();
    }

    public override void OnLeftLobby() { RefreshPlayOnlineButtonState(); }

    public override void OnCreatedRoom() { Debug.Log($"[Photon] CreatedRoom | {PhotonNetwork.CurrentRoom?.Name}"); }

    public override void OnJoinedRoom()
    {
        BGAudioManager.Instance?.OnGameplayStarting();
        StartCoroutine(HandleJoinedRoomDeferred());
    }

    static bool ShouldForcePrivateRoomLobby()
    {
        if (PhotonNetwork.CurrentRoom == null || PhotonNetwork.OfflineMode) return false;
        if (PhotonNetwork.CurrentRoom.IsVisible) return false;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gsObj) && gsObj is bool gs && gs)
            return false;

        if (PhotonNetwork.IsMasterClient && PlayWithFriendsManager.Instance != null && PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin)
            return false;

        return true;
    }

    public void ForcePrivateRoomLobbyOnJoin()
    {
        GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);
        PrepareLobbyPanelsForTransition(PhotonNetwork.IsMasterClient);

        CanvasGroup lobby = ResolveRoomLobbyCanvasGroup();
        if (lobby != null)
        {
            lobby.gameObject.SetActive(true);
            lobby.alpha = 1f;
            lobby.interactable = true;
            lobby.blocksRaycasts = true;
        }

        HideLoading();
    }

    public void HostStartMatch()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        PhotonNetwork.AutomaticallySyncScene = false;

        if (waitingPanel != null) waitingPanel.SetActive(false);

        bool privateFriendsRoom = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

        if (privateFriendsRoom && PhotonNetwork.CurrentRoom != null)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;

            if (PlayWithFriendsManager.Instance != null)
            {
                PlayWithFriendsManager.Instance.OnHostStartFriendsGame();
                return;
            }
        }

        if (ModeManager.Instance != null)
            ModeManager.Instance.StartGameFromModePanel();
        else
            BeginGameAfterRoomReady();
    }

    IEnumerator HandleJoinedRoomDeferred()
    {
        yield return null;

        bool wasActivelyPlaying = GameFlowState.IsActivelyPlaying;

        if (PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            if (_offlineCompleteRoutine != null)
                StopCoroutine(_offlineCompleteRoutine);
            _offlineCompleteRoutine = StartCoroutine(CompleteOfflineBotRoomStart());
            yield break;
        }

        // 🚨 GHOST STATE FIX: Cancel user check only if it's Online Matchmaking
        if (!IsBotsFlowActive()
            && (UiFlowManager.IsReturningHome
                || (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)))
        {
            Debug.LogWarning("[Photon] Ghost state detected! User cancelled matchmaking but room joined. Leaving immediately.");
            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();
            yield break;
        }

        // If the user switched to Bots while an Online/Friends join callback was still arriving,
        // do NOT let that old online room continue into matchmaking/game-start. Leave it and let
        // StartOfflineMatchRoutine create the real local bot room.
        if (pendingOfflineMatch && !PhotonNetwork.OfflineMode)
        {
            Debug.LogWarning("[Bot Mode] Stale online/friends OnJoinedRoom arrived during bot start — leaving stale room.");
            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();
            yield break;
        }

        if (_localMatchAbandoned)
        {
            PhotonNetwork.LeaveRoom();
            yield break;
        }

        if (PlayWithFriendsManager.Instance != null && PlayWithFriendsManager.Instance.IsLeavingFriendsFlow)
        {
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
            yield break;
        }

        // Offline/Bot room is final destination, not a waiting lobby.
        // Older code only initialized PlayerHand/DeckManager and left the Loading panel alive,
        // so after Online/Friends -> Back -> Bots the game stayed forever on "Loading game...".
        if (PhotonNetwork.OfflineMode)
        {
            pendingOfflineMatch = false;
            _offlineStartCoroutine = null;
            isPlayBotsMode = true;
            _localMatchAbandoned = false;
            _isLeavingRoom = false;
            isAttemptingRejoin = false;

            if (_offlineCompleteRoutine != null)
                StopCoroutine(_offlineCompleteRoutine);
            _offlineCompleteRoutine = StartCoroutine(CompleteOfflineBotRoomStart());
            yield break;
        }

        bool rejoiningActiveGame = false;
        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
        {
            rejoiningActiveGame = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs1) && gs1 is bool gsb1 && gsb1;

            if (!rejoiningActiveGame)
            {
                PersistActiveRoomName(PhotonNetwork.CurrentRoom.Name);
                GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);
                StopDisconnectAbandonCoroutine();
                _localMatchAbandoned = false;

                if (PlayWithFriendsManager.Instance != null)
                    PlayWithFriendsManager.Instance.ClearOnlineModeOnly();
                if (!UiFlowManager.IsPlayFriendsLobbyFlow() && !UiFlowManager.IsPlayFriendsJoinFlow())
                    UiFlowManager.MarkPlayFriendsLobby();

                if (PlayWithFriendsManager.Instance != null && PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient)
                {
                    EnsureFriendsModesPanelVisible();
                    HideReconnectPanels();
                    isAttemptingRejoin = false;
                    yield break;
                }

                if (!wasActivelyPlaying) yield return SmoothTransitionToRoomLobby();
                HideReconnectPanels();
                isAttemptingRejoin = false;
                yield break;
            }
        }

        if (PhotonNetwork.CurrentRoom != null) PersistActiveRoomName(PhotonNetwork.CurrentRoom.Name);

        rejoiningActiveGame = PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs2) && (bool)gs2;

        if (rejoiningActiveGame)
        {
            GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
            ShowLoading("Loading game...");
        }
        else
        {
            GameFlowState.SetPhase(GameFlowPhase.InRoom);
        }

        StopDisconnectAbandonCoroutine();
        StopAutoReconnectRoutine();
        _localMatchAbandoned = false;
        HideReconnectPanels();
        bool offlineBotRoom = PhotonNetwork.OfflineMode;
        if (offlineBotRoom)
        {
            pendingOfflineMatch = false;
            _offlineStartCoroutine = null;
            isPlayBotsMode = true;
        }
        if (!rejoiningActiveGame && !offlineBotRoom) HideLoading();
        else if (offlineBotRoom && !rejoiningActiveGame) ShowLoading("Loading game...");
        isAttemptingRejoin = false;

        EnsureLocalNetworkPlayer();

        if (rejoiningActiveGame) StartCoroutine(CompleteActiveGameRejoin());
        else if (DeckManager.Instance != null && !DeckManager.IsPrivateFriendsRoom())
        {
            if (UiFlowManager.IsOnlineMatchmakingFlow() && MatchmakingManager.Instance != null)
                MatchmakingManager.Instance.ShowMatchmakingPanel();
            DeckManager.Instance.OnRoomJoinedCheckStart();
        }

        if (!rejoiningActiveGame) InitializeGameplayScene();
    }

    IEnumerator CompleteOfflineBotRoomStart()
    {
        Debug.Log("🤖 [Bot Mode] Offline room joined — forcing game table reveal.");
        ArmOfflineBotSticky(15f);

        // Absolute guard: never spawn NetworkPlayer or start dealing until PUN says InRoom.
        float roomWait = 15f;
        while (!PhotonNetwork.InRoom && roomWait > 0f)
        {
            if (roomWait < 12f && !PhotonNetwork.InRoom && PhotonNetwork.OfflineMode && PhotonNetwork.IsConnectedAndReady)
            {
                int token = _offlineStartToken;
                if (_offlineRoomCreateAttempts < 4)
                    TryCreateOfflineBotRoom(token);
            }

            roomWait -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (!PhotonNetwork.InRoom)
        {
            Debug.LogError("[Bot Mode] Offline room was not joined in time. Retrying offline create once more.");
            if (_offlineRoomCreateAttempts < 5)
            {
                TryCreateOfflineBotRoom(_offlineStartToken);
                float retryWait = 8f;
                while (!PhotonNetwork.InRoom && retryWait > 0f)
                {
                    retryWait -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        if (!PhotonNetwork.InRoom)
        {
            Debug.LogError("[Bot Mode] Offline room was not joined in time. Returning to Modes instead of hard-stuck loading.");
            pendingOfflineMatch = false;
            isPlayBotsMode = true;
            EndProtectedLoading(ProtectedLoadingFlow.BotStarting);
            if (ModeManager.Instance != null) ModeManager.Instance.ResetStartGuard();
            HideLoadingInstant();
            ForceClearBlackOverlay();
            if (ModeManager.Instance != null) ModeManager.Instance.ShowModesScreenOnly();
            _offlineCompleteRoutine = null;
            yield break;
        }

        Debug.Log("[BotMode] Offline room joined, starting bot game");
        EndProtectedLoading(ProtectedLoadingFlow.BotStarting);

        pendingOfflineMatch = false;
        _offlineStartCoroutine = null;
        isPlayBotsMode = true;
        _localMatchAbandoned = false;
        _isLeavingRoom = false;
        isAttemptingRejoin = false;
        _pendingOnlineMatchmakingAfterLeave = false;
        _returnToFriendsModesAfterLeave = false;
        _pendingPhotonReconnectAfterAuth = false;

        GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
        UiFlowManager.MarkInGame();

        ShowLoading("Loading game...");
        BringLoadingToFront();
        ForceClearBlackOverlay();

        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.HideMatchmakingPanel();

        if (PlayWithFriendsManager.Instance != null)
        {
            PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();
            PlayWithFriendsManager.Instance.HidePrivateFriendsLobbyUI();
        }

        EnsureLocalNetworkPlayer();

        float playerTimeout = 3f;
        while (PlayerHand.LocalInstance == null && playerTimeout > 0f)
        {
            PlayerHand.ResolveLocalHand();
            if (PlayerHand.LocalInstance == null)
                EnsureLocalNetworkPlayer();
            playerTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        float deckTimeout = 2f;
        while (DeckManager.Instance == null && deckTimeout > 0f)
        {
            deckTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        // Let DeckManager build the same offline/bot match context it normally builds from OnJoinedRoom.
        // Without this, the game table can appear for a second and then another stale callback sends the user home.
        if (DeckManager.Instance != null)
            DeckManager.Instance.OnRoomJoinedCheckStart();

        yield return null;
        yield return new WaitForEndOfFrame();

        ResetGameStartGuards();
        BeginGameAfterRoomReady(showLoadingOverlay: false);

        yield return null;
        yield return new WaitForEndOfFrame();

        ForceOfflineGameRevealAndKillLoading();
        StopOfflineLoadingWatchdog();

        _offlineCompleteRoutine = null;
    }

    IEnumerator CompleteActiveGameRejoin()
    {
        HideReconnectPanels();
        ShowLoading("Loading game...");

        float timeout = 8f;
        while (timeout > 0f && PlayerHand.LocalInstance == null)
        {
            EnsureLocalNetworkPlayer();
            PlayerHand.ResolveLocalHand();
            yield return null;
            timeout -= Time.deltaTime;
        }

        float syncWait = 0f;
        while (syncWait < 5f)
        {
            if (PlayerHand.LocalInstance != null && PlayerHand.LocalInstance.myCards.Count > 0) break;
            yield return null;
            syncWait += Time.deltaTime;
        }

        yield return new WaitForSeconds(0.3f);

        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.FinishReconnectFromRoom();
            if (TurnManager.Instance != null) TurnManager.Instance.SetPaused(false);
        }

        HideLoading();
        UpdateUIState(false, showLoadingOverlay: false);
        InitializeGameplayScene();
    }

    // 🚨 ZOMBIE PLAYER FIX: Forcefully clears old network player views
    public void EnsureLocalNetworkPlayer()
    {
        // PhotonNetwork.Instantiate is valid ONLY after a room is actually joined/created.
        // OfflineMode == true alone is NOT enough; otherwise PUN throws:
        // "Can not Instantiate before the client joined/created a room".
        if (!PhotonNetwork.InRoom)
        {
            Debug.LogWarning($"[NetworkPlayer] Spawn skipped — not in room yet. State={PhotonNetwork.NetworkClientState}, Offline={PhotonNetwork.OfflineMode}");
            return;
        }

        var viewsToRemove = new System.Collections.Generic.List<PhotonView>();
        foreach (var view in PhotonNetwork.PhotonViewCollection)
        {
            if (view != null && view.IsMine && view.gameObject != null && view.gameObject.name.Contains("NetworkPlayer")) 
            {
                if (PlayerHand.LocalInstance != null && PlayerHand.LocalInstance.gameObject == view.gameObject)
                    return;

                viewsToRemove.Add(view);
            }
        }

        foreach (var zombie in viewsToRemove)
        {
            if (PhotonNetwork.InRoom)
                PhotonNetwork.Destroy(zombie.gameObject);
            else
                Destroy(zombie.gameObject);
        }

        GameObject playerObj = PhotonNetwork.Instantiate("NetworkPlayer", Vector3.zero, Quaternion.identity);
        if (playerObj != null)
        {
            PlayerHand hand = playerObj.GetComponent<PlayerHand>();
            if (hand != null && hand.photonView != null && hand.photonView.IsMine)
                PlayerHand.LocalInstance = hand;
        }

        PlayerHand.ResolveLocalHand();
    }

    public void BeginGameAfterRoomReady(bool showLoadingOverlay = true)
    {
        EnsurePersistentBackdrop();

        if (gameStartInProgress) return;
        ResetGameStartGuards();

        if (DeckManager.Instance != null) DeckManager.Instance.EnableMatchRpcs();

        gameStartInProgress = true;

        if (gameCanvasGroup == null) ResolveGameCanvasGroup();
        ResolveGameTablePanel();

        ShowGameScene(showLoadingOverlay);
        HideLoadingInstant();

        ResolveHomeMenuPanel();
        if (homeMenuPanel != null) homeMenuPanel.SetActive(false);
        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 0f;
            homeCanvasGroup.interactable = false;
            homeCanvasGroup.blocksRaycasts = false;
        }

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null) ModeManager.Instance.panelModes.SetActive(false);
            ModeManager.Instance.HidePlayWithFriendsPanel();
        }
        if (PlayWithFriendsManager.Instance != null) PlayWithFriendsManager.Instance.HidePrivateFriendsLobbyUI();

        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.matchmakingPanel != null)
        {
            CanvasGroup mp = MatchmakingManager.Instance.matchmakingPanel;
            mp.DOKill();
            mp.alpha = 0f;
            mp.interactable = false;
            mp.blocksRaycasts = false;
            mp.gameObject.SetActive(false);
        }

        EnsureLocalNetworkPlayer();
        PlayerHand.ResolveLocalHand();

        if (IsBotsFlowActive() && PhotonNetwork.OfflineMode)
        {
            ArmOfflineBotSticky(15f);
            if (_offlineBotDealRoutine == null)
                _offlineBotDealRoutine = StartCoroutine(StartOfflineBotDealingWhenReady());
            ForceClearBlackOverlay();
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            if (!dealingStarted && DeckManager.Instance != null)
            {
                dealingStarted = true;
                DeckManager.Instance.StartFullDealingSequence();
            }
        }

        ForceClearBlackOverlay();
    }

    IEnumerator StartOfflineBotDealingWhenReady()
    {
        float timeout = 10f;

        while (timeout > 0f)
        {
            if (!isPlayBotsMode && !PhotonNetwork.OfflineMode)
                break;

            if (PhotonNetwork.InRoom)
            {
                EnsureLocalNetworkPlayer();
                PlayerHand.ResolveLocalHand();
                InitializeGameplayScene();

                bool contextReady = PlayerHand.LocalInstance != null && DeckManager.Instance != null;
                if (contextReady && DeckManager.Instance != null)
                {
                    try
                    {
                        contextReady = DeckManager.Instance.IsMatchContextReadyForDealingPublic();
                    }
                    catch
                    {
                        // Some older DeckManager versions are ready only after OnRoomJoinedCheckStart.
                        contextReady = PlayerHand.LocalInstance != null;
                    }
                }

                if (contextReady)
                    break;
            }

            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (PhotonNetwork.InRoom && PhotonNetwork.OfflineMode && PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
        {
            try { DeckManager.Instance.EnableMatchRpcs(); } catch { }

            if (!dealingStarted)
            {
                dealingStarted = true;
                Debug.Log("🤖 [Bot Mode] Starting offline bot dealing after room/player/context ready.");
                DeckManager.Instance.StartFullDealingSequence();
            }
        }
        else
        {
            Debug.LogWarning($"[Bot Mode] Dealing skipped. InRoom={PhotonNetwork.InRoom}, Offline={PhotonNetwork.OfflineMode}, Master={PhotonNetwork.IsMasterClient}, Deck={(DeckManager.Instance != null)}");
        }

        ForceOfflineGameRevealAndKillLoading();
        _offlineBotDealRoutine = null;
    }

    public override void OnLeftRoom()
    {
        if (isQuitting || !Application.isPlaying) return;

        if (IsOfflineBotStickyActive())
        {
            Debug.LogWarning("[Bot Mode] Ignored stale OnLeftRoom during offline bot start.");
            if (PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
                ForceOfflineGameRevealAndKillLoading();
            return;
        }

        if (pendingOfflineMatch)
        {
            _isLeavingRoom = false;
            if (_offlineStartCoroutine == null)
                _offlineStartCoroutine = StartCoroutine(StartOfflineMatchRoutine(_offlineStartToken));
            return;
        }

        if (!string.IsNullOrEmpty(PlayWithFriendsManager.PendingJoinPin))
        {
            gameStartInProgress = false; dealingStarted = false; PhotonNetwork.OfflineMode = false; _returnToFriendsModesAfterLeave = false;
            StartCoroutine(JoinPendingRoomAfterLeave());
            return;
        }

        if (_pendingOnlineMatchmakingAfterLeave)
        {
            _pendingOnlineMatchmakingAfterLeave = false; _returnToFriendsModesAfterLeave = false; _isLeavingRoom = false; isPlayBotsMode = false;
            ResetGameStartGuards();
            GameFlowState.SetPhase(GameFlowPhase.Matchmaking, forceRecovery: true);
            if (PlayWithFriendsManager.Instance != null) PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();
            HideLoadingInstant(); ForceClearBlackOverlay();
            if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StartSearching();
            StartCoroutine(EnsureLobbyAfterLeaveRoom());
            return;
        }

        if (GameFlowState.Current == GameFlowPhase.Matchmaking && MatchmakingManager.Instance != null && MatchmakingManager.Instance.IsSearching && !MatchmakingManager.Instance.WasCancelledByUser)
        {
            _isLeavingRoom = false; HideLoadingInstant(); StartCoroutine(EnsureLobbyAfterLeaveRoom());
            return;
        }

        isPlayBotsMode = false; ResetGameStartGuards(); PhotonNetwork.OfflineMode = false; _isLeavingRoom = false;

        if (PlayWithFriendsManager.Instance != null) PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();

        if (UiFlowManager.IsReturningHome || (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser))
        {
            GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true); _returnToFriendsModesAfterLeave = false;
            ReturnToHomeScreen();
        }
        else if (_returnToFriendsModesAfterLeave || PlayWithFriendsManager.IsFriendsPrivateRoomCreatePending())
        {
            _returnToFriendsModesAfterLeave = false; ReturnToFriendsModesScreen();
        }
        else if (GameFlowState.Current == GameFlowPhase.Matchmaking || UiFlowManager.IsOnlineMatchmakingFlow())
        {
            GameFlowState.SetPhase(GameFlowPhase.Matchmaking, forceRecovery: true); HideLoadingInstant(); ForceClearBlackOverlay(); MatchmakingManager.Instance?.ShowMatchmakingPanel();
        }
        else if (GameFlowState.Current == GameFlowPhase.ModeSelection)
        {
            GameFlowState.SetPhase(GameFlowPhase.ModeSelection, forceRecovery: true); ModeManager.Instance?.ShowModesScreenOnly();
        }
        else if (UiFlowManager.IsPlayFriendsJoinFlow()) { ModeManager.Instance?.RestoreJoinTableScreenAfterFailedPin(); }
        else { RestoreMenuPanelAfterLeave(); }

        BGAudioManager.Instance?.OnMenuScreenShown();
        StartCoroutine(EnsureLobbyAfterLeaveRoom());
    }

    IEnumerator JoinPendingRoomAfterLeave()
    {
        float timeout = 12f;
        while (timeout > 0f)
        {
            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom && PhotonNetwork.Server == Photon.Realtime.ServerConnection.MasterServer)
            {
                string pin = PlayWithFriendsManager.PendingJoinPin;
                if (!string.IsNullOrEmpty(pin))
                {
                    if (PlayWithFriendsManager.Instance != null) PlayWithFriendsManager.Instance.TryFlushPendingJoin();
                    else { PlayWithFriendsManager.PendingJoinPin = null; PhotonNetwork.JoinRoom(pin); }
                }
                yield break;
            }

            if (!IsPhotonConnectingOrConnected() && HasInternet()) ConnectToPhoton();
            yield return new WaitForSeconds(0.2f);
            timeout -= 0.2f;
        }

        PlayWithFriendsManager.PendingJoinPin = null;
        ModeManager.Instance?.RestoreJoinTableScreenAfterFailedPin();
    }

    IEnumerator EnsureLobbyAfterLeaveRoom()
    {
        float timeout = 8f;
        while (timeout > 0f)
        {
            if (CanCallPhotonLobbyOps() && PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom) { EnsureJoinLobby(); yield break; }
            if (!IsPhotonConnectingOrConnected() && HasInternet()) ConnectToPhoton();
            yield return new WaitForSeconds(0.25f);
            timeout -= 0.25f;
        }
    }

    public override void OnMasterClientSwitched(Player newMaster) { }
    public override void OnJoinRandomFailed(short returnCode, string message) { }

    public void CreateRoomWithPin(string generatedPin)
    {
        if (string.IsNullOrWhiteSpace(generatedPin) || !PhotonNetwork.IsConnectedAndReady || PhotonNetwork.InRoom) return;
        RoomOptions options = new RoomOptions { MaxPlayers = 4, IsVisible = true, IsOpen = true };
        PhotonNetwork.CreateRoom(generatedPin.Trim(), options);
    }

    public void JoinRoomWithPin(string inputPin)
    {
        if (string.IsNullOrWhiteSpace(inputPin)) return;
        if (PhotonNetwork.IsConnectedAndReady)
        {
            if (PhotonNetwork.InRoom) return;
            BeginJoinRoomWithLoadingFade(inputPin.Trim(), "Joining game...");
            return;
        }
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        LogError($"CreateRoomFailed | {returnCode} | {message}");

        if (pendingOfflineMatch || isPlayBotsMode || PhotonNetwork.OfflineMode)
        {
            if (_offlineRoomCreateAttempts < 5)
            {
                Debug.LogWarning($"[BotMode] Offline create failed — retrying ({_offlineRoomCreateAttempts}/5).");
                TryCreateOfflineBotRoom(_offlineStartToken);
                return;
            }

            pendingOfflineMatch = false;
            EndProtectedLoading(ProtectedLoadingFlow.BotStarting);
            if (ModeManager.Instance != null) ModeManager.Instance.ResetStartGuard();
            HideLoading();
            if (PhotonNetwork.OfflineMode && ModeManager.Instance != null) ModeManager.Instance.ShowModesScreenOnly();
            return;
        }

        pendingOfflineMatch = false;
        if (ModeManager.Instance != null) ModeManager.Instance.ResetStartGuard();
        HideLoading();
        if (PhotonNetwork.OfflineMode && ModeManager.Instance != null) ModeManager.Instance.ShowModesScreenOnly();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        if (isAttemptingRejoin && !UiFlowManager.IsPlayFriendsJoinFlow())
        {
            ShowReconnectingPanel("Rejoin failed. Retrying...");
            return;
        }

        PlayWithFriendsManager.PendingJoinPin = null;

        if (UiFlowManager.IsPlayFriendsJoinFlow())
        {
            CancelPinJoinUiOverlays();
            ClearUiInputBlockers();
            UiFlowManager.HandlePinJoinFailed(returnCode, message);
            return;
        }

        if (ModeManager.Instance != null && ModeManager.Instance.IsFriendsMatchMode && PlayWithFriendsManager.Instance != null && (UiFlowManager.IsPlayFriendsJoinFlow() || UiFlowManager.Flow == UiFlowKind.PlayFriendsCreate))
        {
            CancelPinJoinUiOverlays();
            ClearUiInputBlockers();
            if (ModeManager.Instance != null) ModeManager.Instance.RestoreJoinTableScreenAfterFailedPin();
            return;
        }

        lastErrorMessage = $"JoinRoomFailed | {returnCode} | {message}";
        CancelPinJoinUiOverlays();
        OnJoinRoomFailedRestoreUi();
        if (PlayWithFriendsManager.Instance != null) PlayWithFriendsManager.Instance.ShowJoinError("Invalid PIN or Room Full!");
    }

    void SetupButtonAnimations()
    {
        SetupSingleButton(playOnlineButton, false);
        SetupSingleButton(playBotsButton, true);
    }

    void SetupSingleButton(Button btn, bool isBots)
    {
        if (btn == null) return;
        btn.interactable = true;
        UIButtonHoverUtility.SetupHoverScale(btn);

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => {
            if (!isBots && !IsPlayOnlineReady()) return;

            btn.transform.DOPunchScale(new Vector3(-0.1f, -0.1f, 0f), 0.15f, 1, 0.5f).SetUpdate(true);
            isPlayBotsMode = isBots;

            if (ModeManager.Instance == null) return;

            if (isBots) ModeManager.Instance.OnClick_PlayBots_Home();
            else ModeManager.Instance.OnClick_PlayOnline_Home();
        });
    }
    public static string GetDebugStatusBlock()
    {
        if (Instance == null) return "[Photon] NetworkManager missing";

        string lobby = PhotonNetwork.InLobby ? "In Lobby" : "Not In Lobby";
        string room = PhotonNetwork.InRoom
            ? $"{PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})"
            : "Not In Room";

        return $"[Photon Debug]\nState: {PhotonNetwork.NetworkClientState}\nLobby: {lobby}\nRoom: {room}\nInRoom: {PhotonNetwork.InRoom}\nNick: {PhotonNetwork.NickName}\nMaster: {PhotonNetwork.IsMasterClient}\nStatus: {LastStatus}\nError: {LastError}";
    }

    static PlayWithFriendsManager ResolvePlayWithFriendsManager()
    {
        if (PlayWithFriendsManager.Instance != null)
            return PlayWithFriendsManager.Instance;

        var all = Resources.FindObjectsOfTypeAll<PlayWithFriendsManager>();
        foreach (var m in all)
        {
            if (m != null && m.gameObject.scene.IsValid())
                return m;
        }
        return null;
    }
}

public class ButtonEventHelper : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler
{
    public System.Action OnPointerEnterAction;
    public System.Action OnPointerExitAction;

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        OnPointerEnterAction?.Invoke();
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        OnPointerExitAction?.Invoke();
    }
}
