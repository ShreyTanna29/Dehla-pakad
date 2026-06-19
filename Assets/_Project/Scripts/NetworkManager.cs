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

    [Header("UI Panels (Canvas Groups)")]
    public CanvasGroup homeCanvasGroup; 
    public CanvasGroup gameCanvasGroup;
    public CanvasGroup loadingCanvasGroup;

    [Header("Game Table UI")]
    public GameObject homeMenuPanel;
    public GameObject gameTablePanel;

    [Header("UI Texts")]
    public TMP_Text loadingText;

    [Header("Buttons Setup")]
    public Button playOnlineButton; 
    public Button playBotsButton;

    [Header("Transition Settings")]
    public float transitionTime = 0.5f; 

    public bool isPlayBotsMode = false;
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

    private bool isAttemptingRejoin = false;
    private bool _localMatchAbandoned;
    private Coroutine _disconnectAbandonCoroutine;
    private string storedRoomName;
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

        PhotonNetwork.KeepAliveInBackground = 300f;
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

        if (HasInternet())
            ConnectToPhoton();
    }

    void Start()
    {
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

    System.Collections.IEnumerator EnsurePhotonReadyRoutine()
    {
        var wait = new WaitForSecondsRealtime(1.5f);
        while (true)
        {
            if (!PhotonNetwork.OfflineMode && HasInternet())
            {
                if (!PhotonNetwork.IsConnectedAndReady)
                    ConnectToPhoton();
                else
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
            if (!IsPhotonConnectingOrConnected() && HasInternet())
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

    void RefreshPlayOnlineButtonState()
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
                    && !_pendingPhotonReconnectAfterAuth)
                {
                    ConnectToPhoton();
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
            if (reconnectionLostStatusText != null)
                reconnectionLostStatusText.text = $"Internet lost.\nReconnecting... please wait ({Mathf.CeilToInt(timeLeft)}s)";

            yield return new WaitForSeconds(1f);
            timeLeft -= 1f;
        }

        if (isAttemptingRejoin)
        {
            _localMatchAbandoned = true;
            isAttemptingRejoin = false;
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
        StopDisconnectAbandonCoroutine();
        GameFlowState.SetPhase(GameFlowPhase.Disconnected, forceRecovery: true);
        CleanUpLocalNetworkPlayer();
        ShowReconnectionLostPanel($"Internet lost.\nReconnecting... please wait ({DisconnectAbandonHomeSeconds}s)");
        isAttemptingRejoin = true;
        _disconnectAbandonCoroutine = StartCoroutine(AbandonMatchAfterDisconnectRoutine());
        StartCoroutine(AutoReconnectRoutine());
    }

    System.Collections.IEnumerator AutoReconnectRoutine()
    {
        while (isAttemptingRejoin)
        {
            if (!PhotonNetwork.IsConnectedAndReady && PhotonNetwork.NetworkClientState == ClientState.Disconnected)
            {
                if (Application.internetReachability != NetworkReachability.NotReachable)
                {
                    Debug.Log("[Photon] Internet is back! Attempting ReconnectAndRejoin...");
                    PhotonNetwork.ReconnectAndRejoin();
                }
            }
            yield return new WaitForSeconds(3f);
        }
    }

    public void ConnectToPhoton()
    {
        if (PhotonNetwork.OfflineMode) return;

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
        if (homeCanvasGroup == null) return;

        homeCanvasGroup.gameObject.SetActive(true);
        homeCanvasGroup.DOKill();
        homeCanvasGroup.alpha = 1f;
        homeCanvasGroup.interactable = true;
        homeCanvasGroup.blocksRaycasts = true;
    }

    public void UpdateUIState(bool isHome)
    {
        if (isHome && !GoogleLogin.HasCompletedLoginFlow)
        {
            HideHomeUntilLogin();
            return;
        }

        if (isHome)
            ShowHomeUI();
        else
            ShowGameScene();
    }

    public void ShowGameScene()
    {
        Debug.Log("[GameStart] ShowGameScene");
        if (gameCanvasGroup == null)
            Debug.LogError("[GameStart ERROR] Missing gameCanvasGroup");

        if (gameCanvasGroup != null)
        {
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
        }

        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.DOKill();
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.gameObject.SetActive(false);
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

        // Only initialize gameplay logic if we are actually in a room and have a player.
        // Otherwise, this will be called again in OnJoinedRoom.
        if (PhotonNetwork.InRoom)
        {
            InitializeGameplayScene();
        }
        
        Debug.Log("[GameInit] Game scene visible");
    }

    public void StayInPrivateLobbyUI()
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

        Debug.Log("[GameFlow] Waiting for friends. Home stays visible.");
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

        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOKill();
            gameCanvasGroup.DOFade(0, transitionTime).SetUpdate(true);
            gameCanvasGroup.interactable = false;
            gameCanvasGroup.blocksRaycasts = false;
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

    public void ReturnToHomeScreen()
    {
        Debug.Log("[GameFlow] ReturnToHomeScreen");
        _localMatchAbandoned = false;
        StopDisconnectAbandonCoroutine();
        GameFlowState.SetPhase(GameFlowPhase.Home);
        HideLoading();
        HideReconnectPanels();
        isAttemptingRejoin = false;
        isPlayBotsMode = false;
        gameStartInProgress = false;
        dealingStarted = false;
        UpdateUIState(true);
        if (ModeManager.Instance != null)
        {
            ModeManager.Instance.ResetStartGuard();
            if (ModeManager.Instance.panelModes != null && ModeManager.Instance.panelModes.activeSelf)
                ModeManager.Instance.panelModes.SetActive(false);
            if (ModeManager.Instance.panelHomeScreen != null && !ModeManager.Instance.panelHomeScreen.activeSelf)
                ModeManager.Instance.panelHomeScreen.SetActive(true);
            ModeManager.Instance.ApplyHomeScreenButtonColors();
        }

        if (!PhotonNetwork.OfflineMode && HasInternet() && !IsPhotonConnectingOrConnected())
            ConnectToPhoton();

        RefreshPlayOnlineButtonState();
    }

    public void StartOfflineMatchRequest()
    {
        Debug.Log("🤖 [Bot Mode] Requesting Offline Match...");
        pendingOfflineMatch = true;
        isPlayBotsMode = true;

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
        // Never CreateRoom from inside OnDisconnected — wait until Photon finishes dispatch.
        yield return null;
        EnterOfflineModeAndStart();
    }

    private void EnterOfflineModeAndStart()
    {
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
        if (loadingText != null) loadingText.text = message;
        if (loadingCanvasGroup == null) return;

        loadingCanvasGroup.gameObject.SetActive(true);
        loadingCanvasGroup.DOKill();
        loadingCanvasGroup.alpha = 1;
        loadingCanvasGroup.interactable = false;
        loadingCanvasGroup.blocksRaycasts = false;
        loadingCanvasGroup.transform.SetAsLastSibling();
        lastStatusMessage = message;
    }

    public void HideLoading()
    {
        if (_showingNoInternetOverlay && !HasInternet()) return;

        _showingNoInternetOverlay = false;
        if (loadingCanvasGroup == null) return;

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

    public void LogError(string msg)
    {
        lastErrorMessage = msg;
        Debug.LogError($"[Photon] {msg}");
    }

    public override void OnConnectedToMaster() 
    { 
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
        // PhotonNetwork.ReconnectAndRejoin() and drop the match. Let OnJoinedRoom resume the game.
        if (isAttemptingRejoin)
        {
            Debug.Log("[Photon] Connected during rejoin — waiting for OnJoinedRoom to resume the match.");
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

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"[Photon] Disconnected! Cause: {cause}");
        lastErrorMessage = cause.ToString();

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

        if (!_showingNoInternetOverlay)
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
            bool wasInMatch = GameFlowState.Current == GameFlowPhase.InGame || 
                              GameFlowState.Current == GameFlowPhase.InRoom ||
                              (gameCanvasGroup != null && gameCanvasGroup.alpha > 0.1f);

            if (GameFlowState.Current == GameFlowPhase.Matchmaking)
            {
                Debug.Log("[Photon] Disconnected during matchmaking — reconnecting");
                if (ModeManager.Instance != null) ModeManager.Instance.ScheduleMatchmakingAfterLobby();
                HideLoading();
                StartCoroutine(ReconnectForMatchmakingRoutine());
            }
            else if (wasInMatch)
            {
                Debug.Log("[Photon] Disconnected during match — abandoning and returning home.");
                BeginInMatchDisconnectFlow();
            }
        }

        RefreshPlayOnlineButtonState();
    }

    System.Collections.IEnumerator ReconnectForMatchmakingRoutine()
    {
        yield return new WaitForSeconds(1f);
        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();

        float wait = 0f;
        while (wait < 25f && GameFlowState.Current == GameFlowPhase.Matchmaking)
        {
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

    IEnumerator HandleJoinedRoomDeferred()
    {
        yield return null;

        if (_localMatchAbandoned)
        {
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
                storedRoomName = PhotonNetwork.CurrentRoom.Name;
                GameFlowState.SetPhase(GameFlowPhase.InRoom);
                StopDisconnectAbandonCoroutine();
                _localMatchAbandoned = false;
                StayInPrivateLobbyUI();
                HideReconnectPanels();
                isAttemptingRejoin = false;
                yield break;
            }
        }

        if (PhotonNetwork.CurrentRoom != null)
            storedRoomName = PhotonNetwork.CurrentRoom.Name;

        rejoiningActiveGame = PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs2)
            && (bool)gs2;

        if (rejoiningActiveGame)
        {
            GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
            UpdateUIState(false);
        }
        else
        {
            GameFlowState.SetPhase(GameFlowPhase.InRoom);
        }

        StopDisconnectAbandonCoroutine();
        _localMatchAbandoned = false;
        HideReconnectPanels();
        HideLoading();
        isAttemptingRejoin = false;

        EnsureLocalNetworkPlayer();

        if (DeckManager.Instance != null && !DeckManager.IsPrivateFriendsRoom())
            DeckManager.Instance.OnRoomJoinedCheckStart();

        InitializeGameplayScene();
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
    public void BeginGameAfterRoomReady()
    {
        Debug.Log("[GameStart] Room ready");

        if (gameStartInProgress)
        {
            Debug.Log("[GameStart] Duplicate start blocked");
        }
        gameStartInProgress = true;

        // --- Validate critical references (log, don't hard-crash) ---
        if (gameCanvasGroup == null)
            Debug.LogError("[GameStart ERROR] Missing gameCanvasGroup");
        ResolveGameTablePanel();
        if (gameTablePanel == null)
            Debug.LogError("[GameStart ERROR] Missing Panel_Game");
        if (DeckManager.Instance == null)
            Debug.LogError("[GameStart ERROR] Missing DeckManager");

        // 1. Hide loading
        HideLoading();

        // 2. Hide home panel
        ResolveHomeMenuPanel();
        if (homeMenuPanel != null) homeMenuPanel.SetActive(false);
        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOKill();
            homeCanvasGroup.alpha = 0f;
            homeCanvasGroup.interactable = false;
            homeCanvasGroup.blocksRaycasts = false;
        }

        // 3. Hide mode panel + 4. Hide friends panel
        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null)
                ModeManager.Instance.panelModes.SetActive(false);
            if (ModeManager.Instance.panelPlayWithFriends != null)
                ModeManager.Instance.panelPlayWithFriends.SetActive(false);
        }
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.HidePrivateFriendsLobbyUI();

        // 5. Hide matchmaking panel
        if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.matchmakingPanel != null)
        {
            CanvasGroup mp = MatchmakingManager.Instance.matchmakingPanel;
            mp.DOKill();
            mp.alpha = 0f;
            mp.interactable = false;
            mp.blocksRaycasts = false;
            mp.gameObject.SetActive(false);
        }

        // Make sure the local NetworkPlayer exists BEFORE init/deal so PlayerHand.LocalInstance is valid.
        EnsureLocalNetworkPlayer();
        PlayerHand.ResolveLocalHand();

        // 6. Show gameCanvasGroup + 7. Activate Panel_Game (+ SetAsLastSibling)
        // 8. Initialize PlayerHand, PlayerProfileSync, TrumpManager (done inside ShowGameScene).
        ShowGameScene();

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
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Photon] LeftRoom");

        if (pendingOfflineMatch)
        {
            Debug.Log("[Bot Mode] Left online room, transitioning to offline room...");
            return;
        }

        isPlayBotsMode = false;
        gameStartInProgress = false;
        dealingStarted = false;
        PhotonNetwork.OfflineMode = false;
        ReturnToHomeScreen();
        StartCoroutine(EnsureLobbyAfterLeaveRoom());
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

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        LogError($"CreateRoomFailed | {returnCode} | {message}");
        HideLoading();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        LogError($"JoinRoomFailed | {returnCode} | {message}");
        HideLoading();
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