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

    [Header("UI Texts")]
    public TMP_Text loadingText;

    [Header("Buttons Setup")]
    public Button playOnlineButton; 
    public Button playBotsButton;

    [Header("Transition Settings")]
    public float transitionTime = 0.5f; 

    public bool isPlayBotsMode = false;
    private bool pendingOfflineMatch = false;

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

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        PhotonNetwork.KeepAliveInBackground = 300f;
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // Ensure we have a unique UserId for consistent rejoining
        if (string.IsNullOrEmpty(PhotonNetwork.AuthValues?.UserId))
        {
            string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("PhotonUserId", uid);
            PhotonNetwork.AuthValues = new AuthenticationValues(uid);
            Debug.Log("[Photon] Assigned consistent UserId: " + uid);
        }
    }

    void Start()
    {
        // 🚀 Start with Home UI (after login is handled by GoogleLogin)
        UpdateUIState(true); 

        EnsureLoadingDoesNotBlockUI();

        if (playOnlineButton != null) playOnlineButton.interactable = true;
        if (playBotsButton != null) playBotsButton.interactable = true;

        SetupButtonAnimations();
        ResolveReconnectPanels();
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
            yield return new WaitForSeconds(1f);
            timeLeft -= 1f;
        }

        if (isAttemptingRejoin)
        {
            _localMatchAbandoned = true;
            isAttemptingRejoin = false;
            ShowReconnectionLostPanel("Connection lost permanently.\nReturning to Home...");
            yield return new WaitForSeconds(2.5f);
            Debug.Log("[Photon] Match abandoned by local player — leaving room and returning home.");
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
        ShowReconnectionLostPanel("Internet lost.\nReconnecting... please wait (30s)");
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
        if (PhotonNetwork.IsConnected || PhotonNetwork.NetworkClientState == ClientState.ConnectingToNameServer || PhotonNetwork.NetworkClientState == ClientState.ConnectingToMasterServer)
        {
            Debug.Log($"[Photon] Already connected or connecting ({PhotonNetwork.NetworkClientState}).");
            return;
        }

        // ShowLoading removed per user request - transition to Modes should be direct
        Debug.Log("[Photon] ConnectUsingSettings triggered by User Flow (Background)");
        
        if (playOnlineButton != null) playOnlineButton.interactable = false;
        PhotonNetwork.ConnectUsingSettings();
    }

    public void UpdateUIState(bool isHome)
    {
        if (isHome)
            ShowHomeUI();
        else
            ShowGameScene();
    }

    public void ShowGameScene()
    {
        if (gameCanvasGroup != null)
        {
            if (!gameCanvasGroup.gameObject.activeSelf)
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
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.blocksRaycasts = false;
        }

        InitializeGameplayScene();
        Debug.Log("[GameInit] Game scene visible");
    }

    void ShowHomeUI()
    {
        if (homeCanvasGroup != null)
        {
            if (!homeCanvasGroup.gameObject.activeSelf)
                homeCanvasGroup.gameObject.SetActive(true);
            homeCanvasGroup.DOKill();
            homeCanvasGroup.DOFade(1, transitionTime).SetUpdate(true);
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
        if (PlayerHand.LocalInstance != null)
            PlayerHand.LocalInstance.InitializeGameScene();
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
        UpdateUIState(true);
        if (ModeManager.Instance != null)
        {
            if (ModeManager.Instance.panelModes != null && ModeManager.Instance.panelModes.activeSelf)
                ModeManager.Instance.panelModes.SetActive(false);
            if (ModeManager.Instance.panelHomeScreen != null && !ModeManager.Instance.panelHomeScreen.activeSelf)
                ModeManager.Instance.panelHomeScreen.SetActive(true);
            ModeManager.Instance.ApplyHomeScreenButtonColors();
        }
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
            EnterOfflineModeAndStart();
        }
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

        loadingCanvasGroup.alpha = 1;
        loadingCanvasGroup.interactable = false;
        loadingCanvasGroup.blocksRaycasts = false;
        loadingCanvasGroup.transform.SetAsLastSibling();
        lastStatusMessage = message;
    }

    public void HideLoading()
    {
        if (loadingCanvasGroup == null) return;

        loadingCanvasGroup.alpha = 0;
        loadingCanvasGroup.interactable = false;
        loadingCanvasGroup.blocksRaycasts = false;

        // If we just finished initial loading and are in lobby, show home screen
        if (PhotonNetwork.InLobby && homeCanvasGroup != null && homeCanvasGroup.alpha < 0.1f)
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
            else if (!PhotonNetwork.InLobby)
                PhotonNetwork.JoinLobby();
            return;
        }

        if (GameFlowState.Current == GameFlowPhase.Matchmaking)
        {
            Debug.Log("[Photon] Connected during matchmaking — resuming lobby/match");
            HideLoading();
            if (!PhotonNetwork.InLobby)
                PhotonNetwork.JoinLobby();
            else if (ModeManager.Instance != null)
                ModeManager.Instance.StartSmartMatchmakingFromNetwork();
            return;
        }

        Debug.Log("[Photon] JoinLobby");
        PhotonNetwork.JoinLobby();
    }

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
        HideLoading();

        // 🚀 UI FIX: Re-enable buttons if connection failed
        if (playOnlineButton != null) playOnlineButton.interactable = true;

        if (pendingOfflineMatch)
        {
            EnterOfflineModeAndStart();
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
                if (!PhotonNetwork.InLobby)
                    PhotonNetwork.JoinLobby();
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
        lastStatusMessage = "Joined lobby";
        GameFlowState.SetPhase(GameFlowPhase.Home);
        
        if (playOnlineButton != null) playOnlineButton.interactable = true;
        HideLoading();
    }

    public override void OnLeftLobby()
    {
        Debug.Log("[Photon] LeftLobby triggered.");
    }

    public override void OnCreatedRoom()
    {
        Debug.Log($"[Photon] CreatedRoom | {PhotonNetwork.CurrentRoom?.Name}");
    }

    public override void OnJoinedRoom()
    {
        if (_localMatchAbandoned)
        {
            PhotonNetwork.LeaveRoom();
            return;
        }

        if (PhotonNetwork.CurrentRoom != null)
            storedRoomName = PhotonNetwork.CurrentRoom.Name;

        bool rejoiningActiveGame = DeckManager.Instance != null &&
            PhotonNetwork.CurrentRoom != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs) && (bool)gs;

        if (rejoiningActiveGame)
            GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
        else
            GameFlowState.SetPhase(GameFlowPhase.InRoom);

        StopDisconnectAbandonCoroutine();
        _localMatchAbandoned = false;
        UpdateUIState(false);
        HideReconnectPanels();
        HideLoading();
        isAttemptingRejoin = false;

        bool hasExistingPlayer = false;
        PlayerHand[] allHands = Object.FindObjectsByType<PlayerHand>(FindObjectsSortMode.None);
        foreach (var hand in allHands)
        {
            if (hand.photonView != null && hand.photonView.IsMine)
            {
                PlayerHand.LocalInstance = hand;
                hasExistingPlayer = true;
                break;
            }
        }

        if (!hasExistingPlayer && PlayerHand.LocalInstance == null)
            PhotonNetwork.Instantiate("NetworkPlayer", Vector3.zero, Quaternion.identity);

        if (DeckManager.Instance != null)
            DeckManager.Instance.OnRoomJoinedCheckStart();

        InitializeGameplayScene();
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Photon] LeftRoom");
        isPlayBotsMode = false;
        PhotonNetwork.OfflineMode = false;
        ReturnToHomeScreen();
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
            Debug.Log($"[UI] Button Clicked: {(isBots ? "Play Bots" : "Play Online")}");
            btn.transform.DOPunchScale(new Vector3(-0.1f, -0.1f, 0f), 0.15f, 1, 0.5f).SetUpdate(true);
            isPlayBotsMode = isBots;

            if (!isBots)
            {
                ConnectToPhoton();
            }

            if (ModeManager.Instance != null)
                ModeManager.Instance.OpenModePanelFromHome();
            else
                Debug.LogError("[UI] ModeManager.Instance is null!");
        });
    }
}

public class ButtonEventHelper : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
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