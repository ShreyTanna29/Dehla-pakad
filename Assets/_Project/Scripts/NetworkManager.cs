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

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        PhotonNetwork.KeepAliveInBackground = 300f;
        Application.runInBackground = true;
    }

    void Start()
    {
        UpdateUIState(true); // 🚀 Start at Home

        EnsureLoadingDoesNotBlockUI();

        if (playOnlineButton != null) playOnlineButton.interactable = true;
        if (playBotsButton != null) playBotsButton.interactable = true;

        SetupButtonAnimations();

        ShowLoading("Connecting to Server...");
        Debug.Log("[Photon] ConnectUsingSettings");
        PhotonNetwork.ConnectUsingSettings();
    }

    public void UpdateUIState(bool isHome)
    {
        if (homeCanvasGroup != null)
        {
            homeCanvasGroup.DOFade(isHome ? 1 : 0, transitionTime).SetUpdate(true);
            homeCanvasGroup.interactable = isHome;
            homeCanvasGroup.blocksRaycasts = isHome;
        }

        if (gameCanvasGroup != null)
        {
            gameCanvasGroup.DOFade(isHome ? 0 : 1, transitionTime).SetUpdate(true);
            gameCanvasGroup.interactable = !isHome;
            gameCanvasGroup.blocksRaycasts = !isHome;
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
    }

    public void LogError(string msg)
    {
        lastErrorMessage = msg;
        Debug.LogError($"[Photon] {msg}");
    }

    public override void OnConnectedToMaster() 
    { 
        Debug.Log("[Photon] ConnectedToMaster");
        lastStatusMessage = "Connected to Master";

        if (PhotonNetwork.OfflineMode)
        {
            HideLoading();
            return;
        }

        Debug.Log("[Photon] JoinLobby");
        PhotonNetwork.JoinLobby();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"[Photon] Disconnected | Cause: {cause}");
        lastErrorMessage = cause.ToString();
        HideLoading();

        if (pendingOfflineMatch)
        {
            EnterOfflineModeAndStart();
        }
    }

    public override void OnLeftLobby()
    {
        Debug.Log("[Photon] LeftLobby");
    }

    public override void OnJoinedLobby() 
    { 
        Debug.Log("[Photon] JoinedLobby");
        lastStatusMessage = "Joined lobby";
        HideLoading();
    }

    public override void OnCreatedRoom()
    {
        Debug.Log($"[Photon] CreatedRoom | {PhotonNetwork.CurrentRoom?.Name}");
        LogRoomInfo("CreatedRoom");
    }

    public override void OnJoinedRoom()
    {
        Debug.Log("[Photon] Joined Room");
        LogRoomInfo("JoinedRoom");
        HideLoading();

        // 🚀 SPANNING: Create player object. Critical for both Online and Offline modes.
        if (PlayerHand.LocalInstance == null)
        {
            PhotonNetwork.Instantiate("NetworkPlayer", Vector3.zero, Quaternion.identity);
        }

        if (DeckManager.Instance != null)
            DeckManager.Instance.OnRoomJoinedCheckStart();
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Photon] LeftRoom");
        isPlayBotsMode = false;
        PhotonNetwork.OfflineMode = false;

        UpdateUIState(true);
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

        Vector3 originalScale = btn.transform.localScale;
        btn.interactable = true;

        var helper = btn.gameObject.GetComponent<ButtonEventHelper>();
        if (helper == null) helper = btn.gameObject.AddComponent<ButtonEventHelper>();

        helper.OnPointerEnterAction = () => {
            if (btn.interactable) btn.transform.DOScale(originalScale * 1.1f, 0.15f).SetUpdate(true); 
        };
        
        helper.OnPointerExitAction = () => {
            btn.transform.DOScale(originalScale, 0.15f).SetUpdate(true); 
        };

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => {
            Debug.Log($"[UI] Button Clicked: {(isBots ? "Play Bots" : "Play Online")}");
            btn.transform.DOPunchScale(new Vector3(-0.1f, -0.1f, 0f), 0.15f, 1, 0.5f).SetUpdate(true);
            isPlayBotsMode = isBots;

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