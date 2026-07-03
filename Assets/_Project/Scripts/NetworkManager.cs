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

    // Set during application quit / scene teardown. Photon callbacks (e.g. OnLeftRoom)
    // can fire from ConnectionHandler.OnDisable while the hierarchy is being destroyed,
    // where calling GameObject.Find triggers a 'go.IsActive()' assertion.
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
    [Tooltip("Seat lobby panel CanvasGroup — assign or auto-resolved from Play With Friends panel.")]
    [SerializeField] private CanvasGroup roomLobbyCanvasGroup;
    [Tooltip("Full-screen black overlay used when transitioning into gameplay.")]
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

    public bool isPlayBotsMode = false;

    bool IsBotsFlowActive() =>
        isPlayBotsMode || pendingOfflineMatch || PhotonNetwork.OfflineMode;

    public bool IsBotsMatchFlowActive() => IsBotsFlowActive();
    private bool pendingOfflineMatch = false;

    // Guards to prevent duplicate game-start / dealing calls.
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

        if (string.IsNullOrEmpty(PhotonNetwork.AuthValues?.UserId))
        {
            string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("PhotonUserId", uid);
            PhotonNetwork.AuthValues = new AuthenticationValues(uid);
            Debug.Log("[Photon] Assigned consistent UserId: " + uid);
        }

        HideHomeUntilLogin();
        EnsurePersistentBackdrop();
        EnsureCameraSolidBackground();
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

        if (!IsPhotonConnectingOrConnected() && HasInternet())
            ConnectToPhoton();
    }

    /// <summary>Always-on full-screen layer so the Unity camera clear color never flashes through.</summary>
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

    /// <summary>Instantly covers the screen — call BEFORE hiding any UI panel.</summary>
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
        // Slightly longer tolerance for mobile UDP jitter before ClientTimeout disconnects.
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

        photonStart = Time.unscaledTime;
        while (PhotonNetwork.IsConnectedAndReady
            && !PhotonNetwork.InLobby
            && Time.unscaledTime - photonStart < 4f)
        {
            onStatus?.Invoke("Joining lobby...");
            yield return new WaitForSecondsRealtime(0.2f);
        }

        Debug.Log($"[Photon] Ready for home | Connected={PhotonNetwork.IsConnectedAndReady} | InLobby={PhotonNetwork.InLobby}");
        RefreshPlayOnlineButtonState();
    }

    public static bool HasInternet()
    {
        return Application.internetReachability != NetworkReachability.NotReachable;
    }

    /// <summary>Play Online when internet is up and Photon master connection is ready.</summary>
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
        Debug.Log("[Photon] Auth updated — connecting as: " + userId);

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

    /// <summary>
    /// True when CreateRoom / JoinRoom can be called (stable Master Server, not mid-lobby join).
    /// IsConnectedAndReady alone is NOT enough — it can be true during JoiningLobby.
    /// </summary>
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
        {
            if (active)
                Debug.LogWarning($"[Reconnect UI] Skipped SetActive(true) — '{label}' is missing, destroyed, or unloaded.");
            return false;
        }

        if (go.activeSelf == active) return true;

        go.SetActive(active);
        return true;
    }

    static bool SafeSetTextActive(TMP_Text text, bool active, string label)
    {
        if (text == null)
        {
            if (active)
                Debug.LogWarning($"[Reconnect UI] Skipped — TMP_Text '{label}' not found.");
            return false;
        }

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
            if (connectionLostPanel != null)
                Debug.LogWarning("[Reconnect UI] connectionLostPanel reference is stale — clearing cache.");
            connectionLostPanel = null;
            ClearReconnectPanelCache();
        }
    }

    void ResolveReconnectPanels()
    {
        InvalidateStaleReconnectReferences();

        if (!IsUiObjectAlive(connectionLostPanel))
        {
            if (!_connectionLostPanelWarned)
            {
                _connectionLostPanelWarned = true;
                Debug.LogWarning("[Reconnect UI] connectionLostPanel not assigned in Inspector — reconnect UI will be skipped.");
            }
            return;
        }

        Transform root = connectionLostPanel.transform;

        if (!IsUiObjectAlive(reconnectingStatusText?.gameObject))
        {
            reconnectingStatusText = root.Find("Text_Reconnecting")?.GetComponent<TMP_Text>();
            if (reconnectingStatusText == null)
                Debug.LogWarning("[Reconnect UI] Text_Reconnecting not found under connectionLostPanel.");
        }

        if (!IsUiObjectAlive(reconnectionLostStatusText?.gameObject))
        {
            reconnectionLostStatusText = root.Find("Text_ConnectionLost")?.GetComponent<TMP_Text>();
            if (reconnectionLostStatusText == null)
                Debug.LogWarning("[Reconnect UI] Text_ConnectionLost not found under connectionLostPanel.");
        }

        if (!IsUiObjectAlive(reconnectingSpinner))
        {
            Transform spinner = root.Find("SpinnerContainer");
            reconnectingSpinner = spinner != null ? spinner.gameObject : null;
            if (reconnectingSpinner == null)
                Debug.LogWarning("[Reconnect UI] SpinnerContainer not found under connectionLostPanel.");
        }

        if (!IsUiObjectAlive(reconnectionLostRoot))
        {
            Transform lostChild = root.Find("Reconnection_Lost");
            reconnectionLostRoot = lostChild != null
                ? lostChild.gameObject
                : (IsUiObjectAlive(reconnectionLostStatusText?.gameObject) ? reconnectionLostStatusText.gameObject : null);
            if (reconnectionLostRoot == null)
                Debug.LogWarning("[Reconnect UI] Reconnection_Lost root not found under connectionLostPanel.");
        }
    }

    bool TryShowConnectionLostShell()
    {
        ResolveReconnectPanels();
        if (!IsUiObjectAlive(connectionLostPanel))
        {
            Debug.LogWarning("[Reconnect UI] No connection lost panel — continuing reconnect without UI.");
            return false;
        }

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
        else if (hasShell)
            Debug.LogWarning("[Reconnect UI] Reconnecting message not shown — Text_Reconnecting missing.");

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
        else if (hasShell)
            Debug.LogWarning($"[Reconnect UI] Lost message not shown — Text_ConnectionLost missing. Message: {message}");

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

    void CleanUpLocalNetworkPlayer()
    {
        if (PlayerHand.LocalInstance != null)
        {
            Debug.Log("[Reconnect] Cleaning up old NetworkPlayer to prevent duplicate view ID.");
            PlayerHand.LocalInstance.ResetHand();
            if (PlayerHand.LocalInstance.gameObject != null)
                Destroy(PlayerHand.LocalInstance.gameObject);
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

        Debug.Log("[Photon] Attempting match reconnect...");
        ShowReconnectingPanel("Reconnecting to your game...");

        // Connected to master but not in room (common after ClientTimeout on ConnectedToMasterServer).
        if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom && !string.IsNullOrEmpty(storedRoomName))
        {
            Debug.Log("[Photon] On master without room — RejoinRoom: " + storedRoomName);
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

        Debug.Log("[Photon] Idle reconnect after timeout/disconnect.");
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
                {
                    Debug.Log("[Photon] Rejoin succeeded while polling.");
                    yield break;
                }

                if (!IsPhotonReconnectInProgress())
                    TryReconnectToMatch();
            }

            yield return new WaitForSeconds(ReconnectRetrySeconds);
        }

        _autoReconnectCoroutine = null;
    }

    System.Collections.IEnumerator RejoinRoomAfterConnectRoutine()
    {
        if (string.IsNullOrEmpty(storedRoomName))
        {
            Debug.LogWarning("[Photon] No stored room name — cannot RejoinRoom fallback.");
            yield break;
        }

        Debug.Log("[Photon] ReconnectAndRejoin unavailable — using Connect + RejoinRoom fallback.");
        if (PhotonNetwork.NetworkClientState == ClientState.Disconnected)
            PhotonNetwork.ConnectUsingSettings();

        float waited = 0f;
        while (waited < 20f && isAttemptingRejoin)
        {
            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
            {
                Debug.Log("[Photon] Connected — calling RejoinRoom(" + storedRoomName + ")");
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

    /// <summary>Clears offline mode and connects to the cloud when starting online play.</summary>
    public void ConnectToPhotonForOnlinePlay()
    {
        pendingOfflineMatch = false;
        isPlayBotsMode = false;

        if (PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.InRoom)
            {
                Debug.LogWarning("[Photon] Still in offline room — leave before connecting online.");
                return;
            }
            PhotonNetwork.OfflineMode = false;
        }

        ClientState state = PhotonNetwork.NetworkClientState;
        if (state == ClientState.ConnectingToNameServer
            || state == ClientState.ConnectingToMasterServer
            || state == ClientState.Authenticating)
        {
            Debug.Log($"[Photon] Online connect already in progress ({state}).");
            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            EnsureJoinLobby();
            RefreshPlayOnlineButtonState();
            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            Debug.Log($"[Photon] Connected but not ready ({state}) — reconnecting fresh for online play.");
            PhotonNetwork.Disconnect();
            return;
        }

        Debug.Log("[Photon] ConnectUsingSettings for online play");
        PhotonNetwork.ConnectUsingSettings();
    }

    /// <summary>Returns true when CreateRoom / JoinRandomRoom can run; otherwise starts reconnect.</summary>
    public bool EnsureConnectedForOnlineRoomOps()
    {
        pendingOfflineMatch = false;
        isPlayBotsMode = false;

        if (PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.InRoom)
                return false;
            PhotonNetwork.OfflineMode = false;
        }

        if (PhotonNetwork.IsConnectedAndReady && CanCallPhotonLobbyOps())
            return true;

        if (!HasInternet())
            return false;

        ConnectToPhotonForOnlinePlay();
        return false;
    }

    public void ConnectToPhoton()
    {
        if (PhotonNetwork.OfflineMode) return;

        if (isAttemptingRejoin)
        {
            Debug.Log("[Photon] ConnectToPhoton skipped — match rejoin in progress.");
            return;
        }

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
        {
            Debug.Log($"[Photon] Already connecting ({state}).");
            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            EnsureJoinLobby();
            RefreshPlayOnlineButtonState();
            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            Debug.Log($"[Photon] Connected but not ready yet ({state}).");
            return;
        }

        Debug.Log("[Photon] ConnectUsingSettings triggered");
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
        if (homeMenuPanel != null && homeMenuPanel.activeSelf)
            homeMenuPanel.SetActive(false);

        if (ModeManager.Instance != null && ModeManager.Instance.panelHomeScreen != null)
            ModeManager.Instance.panelHomeScreen.SetActive(false);

        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 0f;
            homeCanvasGroup.interactable = false;
            homeCanvasGroup.blocksRaycasts = false;
            homeCanvasGroup.gameObject.SetActive(false);
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
    }

    public void UpdateUIState(bool isHome, bool showLoadingOverlay = true)
    {
        if (isHome && !GoogleLogin.HasCompletedLoginFlow)
        {
            HideHomeUntilLogin();
            return;
        }

        if (isHome)
            ShowHomeUI();
        else
            ShowGameScene(showLoadingOverlay);
    }

    public void ShowGameScene(bool showLoadingOverlay = true)
    {
        Debug.Log("[GameStart] ShowGameScene");
        ForceClearBlackOverlay();
        EnsurePersistentBackdrop();
        HideReconnectPanels();

        // Always reveal gameplay UI first — never hide menus before the game layer is visible.
        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.gameObject.SetActive(true);
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 1f;
            gameCanvasGroup.interactable = true;
            gameCanvasGroup.blocksRaycasts = true;
        }

        ResolveGameTablePanel();
        if (gameTablePanel != null)
        {
            gameTablePanel.SetActive(true);
            gameTablePanel.transform.SetAsLastSibling();
        }
        else
        {
            Debug.LogError("[GameStart ERROR] Missing Panel_Game");
        }

        if (showLoadingOverlay)
            ShowLoading("Loading game...");
        else if (loadingCanvasGroup != null && loadingCanvasGroup.gameObject.activeSelf)
            HideLoadingInstant();

        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 0f;
            homeCanvasGroup.interactable = false;
            homeCanvasGroup.blocksRaycasts = false;
        }

        if (showLoadingOverlay)
            BringLoadingToFront();

        if (PhotonNetwork.InRoom)
            InitializeGameplayScene();

        Debug.Log("[GameInit] Game scene visible");
    }

    public void MarkPendingOnlineMatchmakingAfterLeave()
    {
        _pendingOnlineMatchmakingAfterLeave = true;
        _returnToFriendsModesAfterLeave = false;
    }

    public void HideAllMenuOverlays()
    {
        Debug.Log("[UI] HideAllOverlays called");

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

    /// <summary>Stops reconnect UI/coroutines when user is navigating menus (not mid-match reconnect).</summary>
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
        Debug.Log("[GameFlow] Waiting for friends — seat lobby only.");
    }

    /// <summary>
    /// Hides gameplay/loading UI before showing the private friends seat lobby.
    /// When <paramref name="showHomeMenu"/> is false (invited clients), home stays hidden
    /// so only the seat lobby panel is visible.
    /// </summary>
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

    /// <summary>
    /// Back-button / abandon-lobby cleanup. Leaves the Photon room and resets local match state
    /// so the next Play With Friends session cannot accidentally start a bot match.
    /// </summary>
    public void LeaveRoomAndCleanup()
    {
        if (_isLeavingRoom)
        {
            Debug.Log("[NetworkManager] LeaveRoomAndCleanup ignored — already leaving.");
            return;
        }

        Debug.Log("[NetworkManager] LeaveRoomAndCleanup");

        CancelReconnectUiForMenu();

        if (MatchmakingManager.Instance != null)
        {
            bool friendsFlow = ModeManager.Instance != null && ModeManager.Instance.IsFriendsMatchMode;
            MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: !friendsFlow);
        }

        _returnToFriendsModesAfterLeave = false;

        ResetGameStartGuards();
        isPlayBotsMode = false;
        pendingOfflineMatch = false;
        _localMatchAbandoned = false;

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();

        if (ModeManager.Instance != null)
            ModeManager.Instance.ResetStartGuard();

        CleanupRuntimeMatchStateForMenu();

        if (PhotonNetwork.InRoom)
        {
            _isLeavingRoom = true;
            ShowLoading("Leaving room...");
            PhotonNetwork.LeaveRoom();
            return;
        }

        PhotonNetwork.OfflineMode = false;
        _isLeavingRoom = false;
        GameFlowState.SetPhase(GameFlowPhase.Home, forceRecovery: true);
        ReturnToHomeScreen();

        if (!PhotonNetwork.IsConnectedAndReady && HasInternet())
            ConnectToPhotonForOnlinePlay();
    }

    public void ResetGameStartGuards()
    {
        gameStartInProgress = false;
        dealingStarted = false;
    }

    /// <summary>Clears runtime match UI/state when returning to Home/Modes without touching profile/coins.</summary>
    public void CleanupRuntimeMatchStateForMenu()
    {
        Debug.Log("[GameFlow] CleanupRuntimeMatchStateForMenu");
        ResetGameStartGuards();

        if (DeckManager.Instance != null)
            DeckManager.Instance.ResetMatchState();
        else
            PlayerHand.CleanupRuntimeCardUi();

        if (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
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

    /// <summary>Call after menu transitions so game/home/modes is never fully hidden behind backdrop.</summary>
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
        // GameObject.Find is unsafe during application quit / scene teardown
        // (triggers a 'go.IsActive()' assertion when the hierarchy is being destroyed).
        if (isQuitting || !Application.isPlaying) return;
        gameTablePanel = GameObject.Find("Panel_Game");
        if (gameTablePanel == null)
            gameTablePanel = GameObject.Find("[Panel_Game]");
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
            gameCanvasGroup.alpha = 0f;
            gameCanvasGroup.interactable = false;
            gameCanvasGroup.blocksRaycasts = false;
            gameCanvasGroup.gameObject.SetActive(false);
        }

        ResolveHomeMenuPanel();
        if (homeMenuPanel != null)
            homeMenuPanel.SetActive(true);

        if (homeCanvasGroup != null)
        {
            if (!homeCanvasGroup.gameObject.activeSelf)
                homeCanvasGroup.gameObject.SetActive(true);
            homeCanvasGroup.DOKill();
            homeCanvasGroup.DOFade(1, GamePerformanceBootstrap.UiDuration(transitionTime)).SetUpdate(true);
            homeCanvasGroup.interactable = true;
            homeCanvasGroup.blocksRaycasts = true;
        }
    }

    public static void InitializeGameplayScene()
    {
        Debug.Log("[GameStart] InitializeGameplayScene");

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
        else if (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
        {
            Debug.LogWarning("[GameStart] PlayerHand still missing after spawn wait — will retry on next init.");
        }

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.InitializeGameScene();
        if (TrumpManager.Instance != null)
            TrumpManager.Instance.InitializeGameScene();
    }

    /// <summary>Restores the Modes screen after leaving a friends seat lobby (not Home).</summary>
    public void ReturnToFriendsModesScreen()
    {
        Debug.Log("[GameFlow] ReturnToFriendsModesScreen");
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

    /// <summary>After a failed PIN join — unblock UI and return to the Modes screen.</summary>
    public void OnJoinRoomFailedRestoreUi()
    {
        _lobbyTransitionRunning = false;
        _joinFadeRoutine = null;
        ForceClearBlackOverlay();
        HideLoadingInstant();
        ClearUiInputBlockers();

        if (ModeManager.Instance != null && ModeManager.Instance.IsFriendsMatchMode)
            ReturnToFriendsModesScreen();
        else
            ReturnToHomeScreen();
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

    /// <summary>Removes invisible full-screen CanvasGroups that block all clicks.</summary>
    public void ClearUiInputBlockers()
    {
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.DOKill();
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.gameObject.SetActive(false);
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
    }

    public void ReturnToHomeScreen()
    {
        if (pendingOfflineMatch)
        {
            Debug.Log("[GameFlow] ReturnToHomeScreen skipped — offline bot match pending.");
            return;
        }

        Debug.Log("[GameFlow] ReturnToHomeScreen");
        if (AdsManager.Instance != null)
            AdsManager.Instance.HideBanner();

        _localMatchAbandoned = false;
        StopDisconnectAbandonCoroutine();
        GameFlowState.SetPhase(GameFlowPhase.Home);

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
    }

    public void StartOfflineMatchRequest()
    {
        Debug.Log("🤖 [Bot Mode] Requesting Offline Match...");
        pendingOfflineMatch = true;
        isPlayBotsMode = true;
        ShowLoading("Loading game...");

        if (PhotonNetwork.InRoom)
        {
            Debug.Log("🤖 [Bot Mode] Leaving current room before offline start...");
            PhotonNetwork.LeaveRoom();
            return;
        }

        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("🤖 [Bot Mode] Disconnecting from Photon to enter Offline Mode...");
            PhotonNetwork.Disconnect();
        }
        else
        {
            StartCoroutine(EnterOfflineModeDeferred());
        }
    }

    IEnumerator EnterOfflineModeDeferred()
    {
        // BLUE-SCREEN FIX: previously this waited a single frame, but Photon is often still
        // connected or mid-handshake (ConnectingToNameServer) one frame after Disconnect().
        // Setting PhotonNetwork.OfflineMode = true while connected throws
        // "Can't start OFFLINE mode while connected!" and the follow-up CreateRoom fails with
        // "not ready (State: ConnectingToNameServer)" — leaving the player on a blank/blue screen.
        // We now poll until Photon has FULLY disconnected before flipping to offline mode.
        float timeout = 5f;
        while (PhotonNetwork.IsConnected && timeout > 0f)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        // One extra frame so Photon finishes its internal state dispatch after Disconnected.
        yield return null;
        EnterOfflineModeAndStart();
    }

    private void EnterOfflineModeAndStart()
    {
        // Safety guard: if we somehow re-entered while still connected, defer again instead of
        // throwing. This makes the offline (bot) start race-proof regardless of connect timing.
        if (PhotonNetwork.IsConnected)
        {
            Debug.LogWarning("🤖 [Bot Mode] Still connected — deferring offline start another cycle.");
            StartCoroutine(EnterOfflineModeDeferred());
            return;
        }

        Debug.Log("🤖 [Bot Mode] Entering Offline Mode...");
        PhotonNetwork.OfflineMode = true;
        pendingOfflineMatch = false;

        if (ModeManager.Instance != null)
        {
            ModeManager.Instance.StartLocalMatch();
        }
    }

    void EnsureLoadingDoesNotBlockUI()
    {
        if (loadingCanvasGroup == null) return;
        loadingCanvasGroup.blocksRaycasts = false;
        loadingCanvasGroup.interactable = false;
    }

    public static string GetDebugStatusBlock()
    {
        if (Instance == null) return "[Photon] NetworkManager missing";

        string lobby = PhotonNetwork.InLobby ? "In Lobby" : "Not In Lobby";
        string room = PhotonNetwork.InRoom
            ? $"{PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})"
            : "Not In Room";

        return $"[Photon Debug]\n" +
               $"State: {PhotonNetwork.NetworkClientState}\n" +
               $"Lobby: {lobby}\n" +
               $"Room: {room}\n" +
               $"InRoom: {PhotonNetwork.InRoom}\n" +
               $"Nick: {PhotonNetwork.NickName}\n" +
               $"Master: {PhotonNetwork.IsMasterClient}\n" +
               $"Status: {LastStatus}\n" +
               $"Error: {LastError}";
    }

    public void ShowLoading(string message)
    {
        ShowLoadingFadeIn(message, 0f);
        if (ShouldAnimateGameLoadingSlider(message))
            AnimateLoadingSlider(GameStartLoadingDelaySeconds);
    }

    static bool ShouldAnimateGameLoadingSlider(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("Loading game")
            || message.Contains("Starting Game")
            || message.Contains("Joining game")
            || message.Contains("Joining friend's table");
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

        if (IsBotsFlowActive())
        {
            Debug.Log("[Safety] Loading timeout skipped — bot/offline match still starting.");
            yield break;
        }

        Debug.LogError("[Safety] Loading screen timed out. Forcing UI reset.");
        HideLoadingInstant();
        CancelReconnectUiForMenu();
        pendingOfflineMatch = false;
        isPlayBotsMode = false;
        PhotonNetwork.OfflineMode = false;

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
        else
            ReturnToHomeScreen();
    }

    /// <summary>Fades the loading overlay in, then invokes <paramref name="onFadeComplete"/>.</summary>
    public void ShowLoadingFadeIn(string message, float duration = 0.2f, System.Action onFadeComplete = null)
    {
        HideReconnectPanels();

        if (loadingText != null) loadingText.text = message;
        lastStatusMessage = message;

        if (loadingCanvasGroup == null)
        {
            onFadeComplete?.Invoke();
            return;
        }

        loadingCanvasGroup.gameObject.SetActive(true);
        loadingCanvasGroup.DOKill();
        loadingCanvasGroup.interactable = true;
        loadingCanvasGroup.blocksRaycasts = true;
        BringLoadingToFront();
        StartLoadingSafetyTimeout();

        if (duration <= 0f)
        {
            loadingCanvasGroup.alpha = 1f;
            onFadeComplete?.Invoke();
            return;
        }

        loadingCanvasGroup.alpha = 0f;
        loadingCanvasGroup.DOFade(1f, duration).SetUpdate(true).OnComplete(() => onFadeComplete?.Invoke());
    }

    /// <summary>Mask network latency: fade loading in, then join the Photon room.</summary>
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
            Debug.LogWarning("[UI] Join aborted — still inside a room after leave wait.");
            ForceClearBlackOverlay();
            OnJoinRoomFailedRestoreUi();
            _joinFadeRoutine = null;
            yield break;
        }

        PlayWithFriendsManager.PendingJoinPin = null;
        Debug.Log($"[UI] Loading fade complete — joining room '{roomPin.Trim()}'");
        PhotonNetwork.JoinRoom(roomPin.Trim());
        _joinFadeRoutine = null;
    }

    /// <summary>
    /// Single-scene game start: fade to black, run gameplay setup, fade back in.
    /// (Does NOT call LoadLevel — this project uses CanvasGroup scene swapping.)
    /// </summary>
    public void BeginGameTransitionWithBlackFade(System.Action whileScreenBlack, bool skipFadeIn = false)
    {
        StartCoroutine(GameStartBlackFadeRoutine(whileScreenBlack, skipFadeIn));
    }

    public void ForceClearBlackOverlay()
    {
        if (blackTransitionCanvasGroup != null)
        {
            blackTransitionCanvasGroup.DOKill();
            blackTransitionCanvasGroup.alpha = 0f;
            blackTransitionCanvasGroup.blocksRaycasts = false;
            blackTransitionCanvasGroup.interactable = false;
            blackTransitionCanvasGroup.gameObject.SetActive(false);
        }
    }

    /// <summary>If every main panel is hidden, restore the correct screen for the current flow phase.</summary>
    public void EnsureNoBlackScreen()
    {
        ForceClearBlackOverlay();

        if (IsAnyMainScreenVisible())
            return;

        Debug.LogWarning("[UI] Black screen guard — restoring visible UI for phase " + GameFlowState.Current);
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
            case GameFlowPhase.Matchmaking:
                ModeManager.Instance?.ReturnToHomeClean();
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
                else
                    ModeManager.Instance?.ReturnToHomeClean();
                break;
            case GameFlowPhase.Dealing:
            case GameFlowPhase.InGame:
            case GameFlowPhase.ResolvingTrick:
                ShowGameScene(showLoadingOverlay: false);
                break;
            default:
                ModeManager.Instance?.ShowHomeScreenOnly();
                break;
        }
    }

    void EnsureGameUiVisibleForReveal()
    {
        if (gameCanvasGroup != null)
        {
            EnsureOverlayParentActive(gameCanvasGroup.transform);
            if (!gameCanvasGroup.gameObject.activeSelf)
                gameCanvasGroup.gameObject.SetActive(true);
            gameCanvasGroup.DOKill();
            gameCanvasGroup.alpha = 1f;
            gameCanvasGroup.interactable = true;
            gameCanvasGroup.blocksRaycasts = true;
        }

        ResolveGameTablePanel();
        if (gameTablePanel != null)
        {
            if (!gameTablePanel.activeSelf)
                gameTablePanel.SetActive(true);
            gameTablePanel.transform.SetAsLastSibling();
        }
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

        if (gameCanvasGroup == null)
            return true;

        if (!gameCanvasGroup.gameObject.activeInHierarchy || gameCanvasGroup.alpha < 0.95f)
            return false;

        if (gameTablePanel == null)
            return true;

        return gameTablePanel.activeInHierarchy;
    }

    void EnsureOverlayParentActive(Transform overlay)
    {
        if (overlay == null) return;
        Transform t = overlay.parent;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }
        overlay.SetAsLastSibling();
    }

    /// <summary>Keeps the host on the Modes screen after eager private-room creation.</summary>
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

    /// <summary>Resets lobby panel alpha after DOTween fade transitions.</summary>
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
        if (loadingCanvasGroup != null)
            return loadingCanvasGroup.transform.parent;

        if (gameCanvasGroup != null)
            return gameCanvasGroup.transform.parent;

        if (homeCanvasGroup != null)
            return homeCanvasGroup.transform.parent;

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null)
            return canvas.transform;

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
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

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
        if (roomLobbyCanvasGroup != null)
            return roomLobbyCanvasGroup;

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

        // Show destination lobby BEFORE hiding source panels — prevents blue flash.
        if (ModeManager.Instance != null)
            ModeManager.Instance.ShowPlayWithFriendsPanel();

        PlayWithFriendsManager pwf = ResolvePlayWithFriendsManager();
        if (pwf != null)
            pwf.ShowPrivateRoomLobbyUI();

        if (waitingPanel != null)
            waitingPanel.SetActive(false);

        if (modePanel == null && ModeManager.Instance != null)
            modePanel = ModeManager.Instance.panelModes;
        if (modePanel != null)
            modePanel.SetActive(false);

        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);
            if (ModeManager.Instance.panelHomeScreen != null)
                ModeManager.Instance.panelHomeScreen.SetActive(false);
        }

        PrepareForPrivateRoomLobby(showHomeMenu: false);
    }

    /// <summary>Fade out loading overlay while fading in the seat lobby panel.</summary>
    public IEnumerator SmoothTransitionToRoomLobby()
    {
        if (_lobbyTransitionRunning)
            yield break;
        _lobbyTransitionRunning = true;

        Debug.Log("[NetworkManager] SmoothTransitionToRoomLobby");

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
            Tween lobbyIn = lobby != null
                ? lobby.DOFade(1f, lobbyFadeIn).SetUpdate(true)
                : null;

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

    public void HideLoading()
    {
        if (_showingNoInternetOverlay && !HasInternet()) return;

        _showingNoInternetOverlay = false;
        if (loadingCanvasGroup == null) return;

        StopLoadingSafetyTimeout();
        loadingCanvasGroup.DOKill();
        loadingCanvasGroup.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => {
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.gameObject.SetActive(false);
        });

        // Never auto-open home from Photon lobby until the user finishes login + profile setup.
        if (GoogleLogin.HasCompletedLoginFlow
            && PhotonNetwork.InLobby
            && homeCanvasGroup != null
            && homeCanvasGroup.alpha < 0.1f)
        {
            UpdateUIState(true);
        }
    }

    public void HideLoadingInstant()
    {
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
        Debug.Log("[Photon] ConnectedToMaster. Reconnect Success part 1.");
        lastStatusMessage = "Connected to Master";

        if (PhotonNetwork.OfflineMode)
        {
            HideLoading();
            return;
        }

        if (_localMatchAbandoned)
        {
            Debug.Log("[Photon] Connected after abandoning match — staying off table.");
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
            Debug.Log("[Photon] Connected during matchmaking — resuming lobby/match");
            HideLoading();
            if (!PhotonNetwork.InLobby && PhotonNetwork.NetworkClientState != ClientState.JoiningLobby)
                EnsureJoinLobby();
            else if (ModeManager.Instance != null)
                ModeManager.Instance.StartSmartMatchmakingFromNetwork();
            return;
        }

        // A reconnect/rejoin is in flight: do NOT pull the client into the lobby here, or we cancel
        // ReconnectAndRejoin() and drop the match. If we're on master but not in a room yet, try RejoinRoom.
        if (isAttemptingRejoin)
        {
            Debug.Log("[Photon] Connected during rejoin — waiting for room rejoin.");
            if (!PhotonNetwork.InRoom && !string.IsNullOrEmpty(storedRoomName)
                && PhotonNetwork.NetworkClientState != ClientState.Joining)
            {
                Debug.Log("[Photon] OnConnectedToMaster fallback RejoinRoom: " + storedRoomName);
                PhotonNetwork.RejoinRoom(storedRoomName);
            }
            return;
        }

        if (PlayWithFriendsManager.IsFriendsPrivateRoomCreatePending())
        {
            Debug.Log("[Photon] Friends private room pending — skip JoinLobby so CreateRoom can run.");
            PlayWithFriendsManager.Instance?.TryFlushPendingPrivateRoomCreate();
            RefreshPlayOnlineButtonState();
            return;
        }

        EnsureJoinLobby();
        RefreshPlayOnlineButtonState();
    }

    /// <summary>True while a disconnect-during-match reconnect/rejoin attempt is in progress.</summary>
    public bool IsAttemptingRejoin => isAttemptingRejoin;

    void OnApplicationPause(bool paused)
    {
        if (!paused) OnAppResumed();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) OnAppResumed();
    }

    void OnAppResumed()
    {
        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.RefreshUIAfterResume();

        if (isAttemptingRejoin && HasInternet())
        {
            Debug.Log("[Photon] App resumed during match rejoin — retrying reconnect.");
            TryReconnectToMatch();
            return;
        }

        // Stay in an active match when the app returns from background — do not restart matchmaking.
        if (GameFlowState.Current == GameFlowPhase.InGame
            || GameFlowState.Current == GameFlowPhase.Dealing
            || GameFlowState.Current == GameFlowPhase.ResolvingTrick
            || GameFlowState.Current == GameFlowPhase.InRoom)
        {
            Debug.Log("[Photon] App resumed during active session — keeping current game state.");
            return;
        }

        if (GameFlowState.Current == GameFlowPhase.Matchmaking)
        {
            if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InLobby)
            {
                 Debug.Log("[Photon] Still connected during resume — re-triggering matchmaking check");
                 if (ModeManager.Instance != null)
                    ModeManager.Instance.StartSmartMatchmakingFromNetwork();
            }
            else if (!PhotonNetwork.IsConnectedAndReady &&
                     PhotonNetwork.NetworkClientState != ClientState.ConnectingToNameServer &&
                     PhotonNetwork.NetworkClientState != ClientState.ConnectingToMasterServer &&
                     PhotonNetwork.NetworkClientState != ClientState.Authenticating)
            {
                Debug.Log("[Photon] Disconnected during resume — initiating automatic recovery");
                if (ModeManager.Instance != null)
                    ModeManager.Instance.ScheduleMatchmakingAfterLobby();
                StartCoroutine(ReconnectForMatchmakingRoutine());
            }
        }
    }

    void HandleFatalDisconnect(DisconnectCause cause)
    {
        if (pendingOfflineMatch)
        {
            Debug.LogWarning($"[Network] Disconnect during bot transition ({cause}) — continuing offline start.");
            StartCoroutine(EnterOfflineModeDeferred());
            return;
        }

        Debug.LogError($"[Network] Fatal disconnect ({cause}). Returning to Home.");
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

        if (cause == DisconnectCause.DisconnectByClientLogic)
        {
            if (pendingOfflineMatch)
                StartCoroutine(EnterOfflineModeDeferred());
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
            if (HasInternet())
                ConnectToPhoton();
            else
                ShowNoInternetLoading();
            RefreshPlayOnlineButtonState();
            return;
        }

        if (!_showingNoInternetOverlay && !ShouldKeepLoadingVisibleAfterDisconnect())
            HideLoading();

        if (!HasInternet())
            ShowNoInternetLoading();

        if (pendingOfflineMatch)
        {
            StartCoroutine(EnterOfflineModeDeferred());
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
                Debug.Log("[Photon] Disconnected during matchmaking — reconnecting");
                if (ModeManager.Instance != null) ModeManager.Instance.ScheduleMatchmakingAfterLobby();
                HideLoading();
                StartCoroutine(ReconnectForMatchmakingRoutine());
            }
            else if (wasInMatch && !isAttemptingRejoin)
            {
                Debug.Log("[Photon] Disconnected during match — starting rejoin flow.");
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
                Debug.Log($"[Photon] Idle disconnect ({cause}) — scheduling Photon reconnect.");
                StartCoroutine(ReconnectIdleRoutine());
            }
            else if (cause == DisconnectCause.ClientTimeout || cause == DisconnectCause.ServerTimeout)
            {
                Debug.Log($"[Photon] Timeout disconnect ({cause}) — forcing reconnect attempt.");
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
        Debug.Log("[Photon] JoinedLobby");
        Debug.Log("[PhotonFlow] Joined Lobby");
        lastStatusMessage = "Joined lobby";
        GameFlowState.SetPhase(GameFlowPhase.Home);

        if (!_showingNoInternetOverlay)
            HideLoading();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.EnsureFriendServicesStarted();

        RefreshPlayOnlineButtonState();
    }

    public override void OnLeftLobby()
    {
        Debug.Log("[Photon] LeftLobby triggered.");
        RefreshPlayOnlineButtonState();
    }

    public override void OnCreatedRoom()
    {
        Debug.Log($"[Photon] CreatedRoom | {PhotonNetwork.CurrentRoom?.Name}");
    }

    public override void OnJoinedRoom()
    {
        StartCoroutine(HandleJoinedRoomDeferred());
    }

    static bool ShouldForcePrivateRoomLobby()
    {
        if (PhotonNetwork.CurrentRoom == null || PhotonNetwork.OfflineMode) return false;
        if (PhotonNetwork.CurrentRoom.IsVisible) return false;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gsObj)
            && gsObj is bool gs && gs)
            return false;

        if (PhotonNetwork.IsMasterClient
            && PlayWithFriendsManager.Instance != null
            && PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin)
            return false;

        return true;
    }

    /// <summary>Immediate lobby show (fallback / internal prep). Prefer SmoothTransitionToRoomLobby.</summary>
    public void ForcePrivateRoomLobbyOnJoin()
    {
        Debug.Log("[NetworkManager] ForcePrivateRoomLobbyOnJoin (instant fallback)");
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

    /// <summary>
    /// Master Client only — starts the match for every peer.
    ///
    /// IMPORTANT: this project is SINGLE-SCENE. The only scene in Build Settings is
    /// "DehlaPakad"; there is NO separate "GameScene". Gameplay is shown by swapping
    /// CanvasGroups (ShowGameScene / BeginGameAfterRoomReady), NOT by loading a scene.
    /// Calling PhotonNetwork.LoadLevel here reloaded the one and only scene and produced the
    /// blank BLUE screen. We therefore keep scene auto-sync OFF and route through the proven
    /// start pipeline that fans the start out to every client and ends in ShowGameScene().
    /// </summary>
    public void HostStartMatch()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[HostStartMatch] Ignored — only the Master Client can start the match.");
            return;
        }

        // Defensive: ensure no stray scene auto-sync is active (undoes the old LoadLevel regression).
        PhotonNetwork.AutomaticallySyncScene = false;

        if (waitingPanel != null)
            waitingPanel.SetActive(false);

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

        if (_localMatchAbandoned)
        {
            PhotonNetwork.LeaveRoom();
            yield break;
        }

        if (PlayWithFriendsManager.Instance != null && PlayWithFriendsManager.Instance.IsLeavingFriendsFlow)
        {
            Debug.Log("[Friends] HandleJoinedRoomDeferred skipped — user left friends flow.");
            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();
            yield break;
        }

        bool rejoiningActiveGame = false;
        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
        {
            rejoiningActiveGame = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs)
                && (bool)gs;

            if (!rejoiningActiveGame)
            {
                Debug.Log("Private Room Joined. Waiting in Lobby...");
                PersistActiveRoomName(PhotonNetwork.CurrentRoom.Name);
                GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);
                StopDisconnectAbandonCoroutine();
                _localMatchAbandoned = false;

                if (PlayWithFriendsManager.Instance != null
                    && PlayWithFriendsManager.Instance.SuppressSeatLobbyOnJoin
                    && PhotonNetwork.IsMasterClient)
                {
                    Debug.Log("[GameFlow] Eager invite-room — host stays on Modes panel.");
                    EnsureFriendsModesPanelVisible();
                    HideReconnectPanels();
                    isAttemptingRejoin = false;
                    yield break;
                }

                yield return SmoothTransitionToRoomLobby();
                HideReconnectPanels();

                isAttemptingRejoin = false;
                yield break;
            }
        }

        if (PhotonNetwork.CurrentRoom != null)
            PersistActiveRoomName(PhotonNetwork.CurrentRoom.Name);

        rejoiningActiveGame = PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs2)
            && (bool)gs2;

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
        if (!rejoiningActiveGame && !offlineBotRoom)
            HideLoading();
        else if (offlineBotRoom && !rejoiningActiveGame)
            ShowLoading("Loading game...");
        isAttemptingRejoin = false;

        EnsureLocalNetworkPlayer();

        if (rejoiningActiveGame)
            StartCoroutine(CompleteActiveGameRejoin());
        else if (DeckManager.Instance != null && !DeckManager.IsPrivateFriendsRoom())
            DeckManager.Instance.OnRoomJoinedCheckStart();

        if (!rejoiningActiveGame)
            InitializeGameplayScene();
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

        // Let DeckManager.RejoinStateRoutine restore hand / table state from room props.
        float syncWait = 0f;
        while (syncWait < 5f)
        {
            if (PlayerHand.LocalInstance != null && PlayerHand.LocalInstance.myCards.Count > 0)
                break;
            yield return null;
            syncWait += Time.deltaTime;
        }

        yield return new WaitForSeconds(0.3f);

        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.FinishReconnectFromRoom();
            if (TurnManager.Instance != null)
                TurnManager.Instance.SetPaused(false);
        }

        HideLoading();
        UpdateUIState(false, showLoadingOverlay: false);
        InitializeGameplayScene();
        Debug.Log("[Photon] Active game rejoin complete.");
    }

    /// <summary>
    /// Instantiates the local NetworkPlayer (which sets PlayerHand.LocalInstance in its Awake)
    /// if one does not already exist. Safe to call multiple times.
    /// </summary>
    public void EnsureLocalNetworkPlayer()
    {
        if (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            Debug.LogWarning("[GameStart] EnsureLocalNetworkPlayer skipped — not in a room.");
            return;
        }

        foreach (var view in PhotonNetwork.PhotonViewCollection)
        {
            if (view.IsMine && view.gameObject.name.Contains("NetworkPlayer"))
                return; // Already have a local network player.
        }

        Debug.Log("[GameStart] Instantiating local NetworkPlayer");
        GameObject playerObj = PhotonNetwork.Instantiate("NetworkPlayer", Vector3.zero, Quaternion.identity);
        if (playerObj != null)
        {
            PlayerHand hand = playerObj.GetComponent<PlayerHand>();
            if (hand != null && hand.photonView != null && hand.photonView.IsMine)
                PlayerHand.LocalInstance = hand;
        }

        PlayerHand.ResolveLocalHand();
    }

    /// <summary>
    /// SHARED safe entry to transition into the game once the Photon room is ready.
    /// Used by the Play With Friends RPC and any flow that needs a single, guarded
    /// "hide menus -> show game -> initialize -> (master) deal" sequence.
    /// </summary>
    public void BeginGameAfterRoomReady(bool showLoadingOverlay = true)
    {
        Debug.Log("[GameStart] Room ready");
        EnsurePersistentBackdrop();

        ResetGameStartGuards();

        if (DeckManager.Instance != null)
            DeckManager.Instance.EnableMatchRpcs();

        if (gameStartInProgress)
            Debug.Log("[GameStart] Duplicate start blocked");
        gameStartInProgress = true;

        if (gameCanvasGroup == null)
            Debug.LogError("[GameStart ERROR] Missing gameCanvasGroup");
        ResolveGameTablePanel();
        if (gameTablePanel == null)
            Debug.LogError("[GameStart ERROR] Missing Panel_Game");
        if (DeckManager.Instance == null)
            Debug.LogError("[GameStart ERROR] Missing DeckManager");

        // 1. Show game FIRST so no frame exists with every panel hidden.
        ShowGameScene(showLoadingOverlay);

        // 2. Hide menu / lobby layers on top of the now-visible game canvas.
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
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);
            ModeManager.Instance.HidePlayWithFriendsPanel();
        }
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.HidePrivateFriendsLobbyUI();

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

        // 9. If MasterClient and DeckManager exists, start dealing ONCE.
        if (PhotonNetwork.IsMasterClient)
        {
            if (DeckManager.Instance == null)
            {
                Debug.LogError("[GameStart ERROR] Missing DeckManager");
            }
            else if (dealingStarted)
            {
                Debug.Log("[GameStart] Duplicate start blocked");
            }
            else
            {
                dealingStarted = true;
                Debug.Log("[GameStart] StartFullDealingSequence");
                DeckManager.Instance.StartFullDealingSequence();
            }
        }

        ForceClearBlackOverlay();
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Photon] LeftRoom");

        // During application quit / scene teardown this callback can fire from
        // ConnectionHandler.OnDisable. Touching the UI hierarchy here is unsafe.
        if (isQuitting || !Application.isPlaying)
        {
            Debug.Log("[Photon] LeftRoom ignored during teardown.");
            return;
        }

        if (pendingOfflineMatch)
        {
            Debug.Log("[Bot Mode] Left online room, transitioning to offline room...");
            if (PhotonNetwork.IsConnected)
                PhotonNetwork.Disconnect();
            else
                StartCoroutine(EnterOfflineModeDeferred());
            return;
        }

        // A PIN join is queued (the player left their own eager/lobby room to join a friend's
        // room). Don't bounce to Home — join the queued room as soon as we are ready again.
        if (!string.IsNullOrEmpty(PlayWithFriendsManager.PendingJoinPin))
        {
            Debug.Log("[Photon] LeftRoom with a queued PIN join — joining '"
                + PlayWithFriendsManager.PendingJoinPin + "' instead of returning Home.");
            gameStartInProgress = false;
            dealingStarted = false;
            PhotonNetwork.OfflineMode = false;
            _returnToFriendsModesAfterLeave = false;
            StartCoroutine(JoinPendingRoomAfterLeave());
            return;
        }

        if (_pendingOnlineMatchmakingAfterLeave)
        {
            Debug.Log("[Photon] LeftRoom for online matchmaking — resuming seat lobby.");
            _pendingOnlineMatchmakingAfterLeave = false;
            _returnToFriendsModesAfterLeave = false;
            _isLeavingRoom = false;
            isPlayBotsMode = false;
            ResetGameStartGuards();
            GameFlowState.SetPhase(GameFlowPhase.Matchmaking, forceRecovery: true);
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();
            HideLoadingInstant();
            ForceClearBlackOverlay();
            if (MatchmakingManager.Instance != null)
                MatchmakingManager.Instance.StartSearching();
            StartCoroutine(EnsureLobbyAfterLeaveRoom());
            return;
        }

        // Stale leave from a prior room while the user already started a fresh online search.
        if (GameFlowState.Current == GameFlowPhase.Matchmaking
            && MatchmakingManager.Instance != null
            && MatchmakingManager.Instance.IsSearching
            && !MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[Photon] LeftRoom during active matchmaking — keeping search alive.");
            _isLeavingRoom = false;
            HideLoadingInstant();
            StartCoroutine(EnsureLobbyAfterLeaveRoom());
            return;
        }

        isPlayBotsMode = false;
        ResetGameStartGuards();
        PhotonNetwork.OfflineMode = false;
        _isLeavingRoom = false;

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ResetLobbyStateForLeave();

        GameFlowState.SetPhase(
            _returnToFriendsModesAfterLeave ? GameFlowPhase.ModeSelection : GameFlowPhase.Home,
            forceRecovery: true);

        if (_returnToFriendsModesAfterLeave)
        {
            _returnToFriendsModesAfterLeave = false;
            Debug.Log("[UI] OnLeftRoom completed → Modes");
            ReturnToFriendsModesScreen();
        }
        else
        {
            Debug.Log("[UI] OnLeftRoom completed → Home");
            ReturnToHomeScreen();
        }

        StartCoroutine(EnsureLobbyAfterLeaveRoom());
    }

    IEnumerator JoinPendingRoomAfterLeave()
    {
        float timeout = 12f;
        while (timeout > 0f)
        {
            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom
                && PhotonNetwork.Server == Photon.Realtime.ServerConnection.MasterServer)
            {
                string pin = PlayWithFriendsManager.PendingJoinPin;
                PlayWithFriendsManager.PendingJoinPin = null;
                if (!string.IsNullOrEmpty(pin))
                {
                    Debug.Log("[Photon] Joining queued room after leave: " + pin);
                    BeginJoinRoomWithLoadingFade(pin, "Joining game...");
                }
                yield break;
            }

            if (!IsPhotonConnectingOrConnected() && HasInternet())
                ConnectToPhoton();

            yield return new WaitForSeconds(0.2f);
            timeout -= 0.2f;
        }

        // Couldn't get ready in time — clear the pending join and fall back Home.
        PlayWithFriendsManager.PendingJoinPin = null;
        ReturnToHomeScreen();
    }

    IEnumerator EnsureLobbyAfterLeaveRoom()
    {
        float timeout = 8f;
        while (timeout > 0f)
        {
            if (CanCallPhotonLobbyOps() && PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
            {
                EnsureJoinLobby();
                yield break;
            }

            if (!IsPhotonConnectingOrConnected() && HasInternet())
                ConnectToPhoton();

            yield return new WaitForSeconds(0.25f);
            timeout -= 0.25f;
        }
    }

    public override void OnMasterClientSwitched(Player newMaster)
    {
        Debug.Log($"[Photon] OnMasterClientSwitched | {newMaster.NickName}");
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log($"[Photon] JoinRandomFailed | {returnCode} | {message}");
    }

    // ==========================================
    // PIN ROOM — SECURE CREATE / JOIN
    // ==========================================

    /// <summary>Creates a 4-player room using the PIN as the exact Photon room name.</summary>
    public void CreateRoomWithPin(string generatedPin)
    {
        if (string.IsNullOrWhiteSpace(generatedPin))
        {
            Debug.LogError("[NetworkManager] CreateRoomWithPin — PIN is empty.");
            return;
        }

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogError("[NetworkManager] CreateRoomWithPin — Photon is not connected and ready.");
            return;
        }

        if (PhotonNetwork.InRoom)
        {
            Debug.LogError("[NetworkManager] CreateRoomWithPin — already inside a room. Leave first.");
            return;
        }

        string pin = generatedPin.Trim();

        RoomOptions options = new RoomOptions();
        options.MaxPlayers = 4;
        options.IsVisible = true;
        options.IsOpen = true;

        Debug.Log($"[NetworkManager] CreateRoomWithPin — creating room '{pin}'...");
        PhotonNetwork.CreateRoom(pin, options);
    }

    /// <summary>Joins an existing room by PIN (exact room name match).</summary>
    public void JoinRoomWithPin(string inputPin)
    {
        if (string.IsNullOrWhiteSpace(inputPin))
        {
            Debug.LogError("[NetworkManager] JoinRoomWithPin — PIN is empty.");
            return;
        }

        string pin = inputPin.Trim();

        if (PhotonNetwork.IsConnectedAndReady)
        {
            if (PhotonNetwork.InRoom)
            {
                Debug.LogError("[NetworkManager] JoinRoomWithPin — already inside a room. Leave first.");
                return;
            }

            Debug.Log($"[NetworkManager] JoinRoomWithPin — joining room '{pin}'...");
            BeginJoinRoomWithLoadingFade(pin, "Joining game...");
            return;
        }

        Debug.LogError("[NetworkManager] JoinRoomWithPin — Photon is not connected and ready.");
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[NetworkManager] OnCreateRoomFailed | Code: {returnCode} | Reason: {message}");
        LogError($"CreateRoomFailed | {returnCode} | {message}");
        pendingOfflineMatch = false;
        if (ModeManager.Instance != null)
            ModeManager.Instance.ResetStartGuard();
        HideLoading();
        if (PhotonNetwork.OfflineMode && ModeManager.Instance != null)
            ModeManager.Instance.ShowModesScreenOnly();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[NetworkManager] OnJoinRoomFailed | Code: {returnCode} | Reason: {message}");
        LogError($"JoinRoomFailed | {returnCode} | {message}");
        if (isAttemptingRejoin)
        {
            Debug.LogWarning("[Photon] RejoinRoom failed — AutoReconnect will retry.");
            ShowReconnectingPanel("Rejoin failed. Retrying...");
            return;
        }

        PlayWithFriendsManager.PendingJoinPin = null;
        OnJoinRoomFailedRestoreUi();
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ShowJoinError("Invalid PIN or Room Full!");
    }

    void LogRoomInfo(string source)
    {
        if (!PhotonNetwork.InRoom) return;
        Room r = PhotonNetwork.CurrentRoom;
        Debug.Log($"[Photon] {source} | Room Name: {r.Name} | Player Count: {r.PlayerCount}/{r.MaxPlayers}");
        Debug.Log($"[Photon] Local Nickname: {PhotonNetwork.NickName} | Master: {PhotonNetwork.IsMasterClient}");
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
            if (!isBots && !IsPlayOnlineReady())
            {
                Debug.LogWarning("[UI] Play Online blocked — Photon not ready or no internet.");
                return;
            }

            Debug.Log($"[UI] Button Clicked: {(isBots ? "Play Bots" : "Play Online")}");
            btn.transform.DOPunchScale(new Vector3(-0.1f, -0.1f, 0f), 0.15f, 1, 0.5f).SetUpdate(true);
            isPlayBotsMode = isBots;

            if (ModeManager.Instance == null)
            {
                Debug.LogError("[UI] ModeManager.Instance is null!");
                return;
            }

            if (isBots)
                ModeManager.Instance.OnClick_PlayBots_Home();
            else
                ModeManager.Instance.OnClick_PlayOnline_Home();
        });
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