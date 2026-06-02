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

    [Header("Game Modes Settings")]
    public int currentTrickMode = 1;
    public int currentTrumpMode = 3;

    [Header("UI References")]
    public Image btn1Taash;
    public Image btn2Taash;
    public Image btnPresetTrump;
    public Image btn13thCard;
    public Image btnFirstCut;

    private bool findMatchAfterLobby = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void OpenModePanelFromHome()
    {
        if (panelHomeScreen != null)
        {
            CanvasGroup homeCG = panelHomeScreen.GetComponent<CanvasGroup>();
            if (homeCG == null) homeCG = panelHomeScreen.AddComponent<CanvasGroup>();
            homeCG.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => panelHomeScreen.SetActive(false));
        }

        if (panelModes != null)
        {
            panelModes.SetActive(true);
            CanvasGroup modeCG = panelModes.GetComponent<CanvasGroup>();
            if (modeCG == null) modeCG = panelModes.AddComponent<CanvasGroup>();
            modeCG.alpha = 0;
            modeCG.DOFade(1, 0.3f).SetUpdate(true);
        }
        UpdateUIColors();
    }

    public void OnClick_BackToHome()
    {
        if (panelModes != null)
        {
            CanvasGroup modeCG = panelModes.GetComponent<CanvasGroup>();
            if (modeCG == null) modeCG = panelModes.AddComponent<CanvasGroup>();
            modeCG.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => panelModes.SetActive(false));
        }

        if (panelHomeScreen != null)
        {
            panelHomeScreen.SetActive(true);
            CanvasGroup homeCG = panelHomeScreen.GetComponent<CanvasGroup>();
            if (homeCG == null) homeCG = panelHomeScreen.AddComponent<CanvasGroup>();
            homeCG.alpha = 0;
            homeCG.DOFade(1, 0.3f).SetUpdate(true);
        }
    }

    public void OnClick_TrickMode(int mode)
    {
        currentTrickMode = mode;
        if (GameSettings.Instance != null) GameSettings.Instance.taashCategory = mode;
        UpdateUIColors();
    }

    public void OnClick_TrumpMode(int mode)
    {
        currentTrumpMode = mode;
        if (GameSettings.Instance != null)
        {
            switch (mode)
            {
                case 1: GameSettings.Instance.currentMode = GameModeType.TrumpSpades; break;
                case 2: GameSettings.Instance.currentMode = GameModeType.ThirteenthCardTrump; break;
                case 3: GameSettings.Instance.currentMode = GameModeType.CutToTrump; break;
            }
        }
        UpdateUIColors();
    }

    void UpdateUIColors()
    {
        Color selectedColor = new Color(0.8f, 0.8f, 0.8f); 
        Color unselectedColor = Color.white;

        if (btn1Taash != null && btn2Taash != null)
        {
            btn1Taash.color = currentTrickMode == 1 ? selectedColor : unselectedColor;
            btn2Taash.color = currentTrickMode == 2 ? selectedColor : unselectedColor;
        }
        if (btnPresetTrump != null && btn13thCard != null && btnFirstCut != null)
        {
            btnPresetTrump.color = currentTrumpMode == 1 ? selectedColor : unselectedColor;
            btn13thCard.color = currentTrumpMode == 2 ? selectedColor : unselectedColor;
            btnFirstCut.color = currentTrumpMode == 3 ? selectedColor : unselectedColor;
        }
    }

    public void OnClick_FindMatch()
    {
        Debug.Log("[UI] Button Clicked: Find Match");

        bool isBots = NetworkManager.Instance != null && NetworkManager.Instance.isPlayBotsMode;
        if (GameSettings.Instance != null)
        {
            GameSettings.Instance.currentMatchType = isBots ? MatchType.OfflineBots : MatchType.OnlinePhoton;
        }

        if (panelModes != null)
        {
            CanvasGroup modeCG = panelModes.GetComponent<CanvasGroup>();
            if (modeCG == null) modeCG = panelModes.AddComponent<CanvasGroup>();
            modeCG.DOFade(0, 0.3f).SetUpdate(true).OnComplete(() => panelModes.SetActive(false));
        }

        if (isBots)
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.StartOfflineMatchRequest();
            }
            else
            {
                StartLocalMatch(); // Fallback
            }
            return;
        }

        // Online Match Logic
        if (!PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.OfflineMode)
        {
            Debug.LogError("[Photon] Not connected — cannot find match.");
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowLoading("Not connected. Wait for lobby...");
            return;
        }

        if (MatchmakingManager.Instance != null)
            MatchmakingManager.Instance.StartSearching();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowLoading("Finding Match...");

        StartSmartMatchmaking();
    }

    public void StartLocalMatch()
    {
        Debug.Log("🤖 [Bot Mode] Starting offline room instantly.");
        
        // 🚀 Ensure we are in Offline Mode
        if (!PhotonNetwork.OfflineMode)
        {
             Debug.LogWarning("🤖 [Bot Mode] Not in Offline Mode yet. Forcing it.");
             PhotonNetwork.OfflineMode = true;
        }

        // 🚀 INSTANT: Hide menus and show game scene before creating room
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.UpdateUIState(false);
            NetworkManager.Instance.HideLoading();
        }

        string roomName = "Local_Bot_Room_" + Random.Range(1000, 9999);
        PhotonNetwork.CreateRoom(roomName, BuildRoomOptions());
    }

    void StartSmartMatchmaking()
    {
        if (!PhotonNetwork.InLobby)
        {
            Debug.Log("[Photon] Not in lobby — joining lobby first, then will matchmake...");
            findMatchAfterLobby = true;
            PhotonNetwork.JoinLobby();
            return;
        }

        findMatchAfterLobby = false;
        Debug.Log("[Photon] Attempt Join Room (JoinRandomRoom)");

        Hashtable expectedCustomRoomProperties = new Hashtable();
        expectedCustomRoomProperties.Add("TM", currentTrickMode);
        expectedCustomRoomProperties.Add("RM", currentTrumpMode);

        PhotonNetwork.JoinRandomRoom(expectedCustomRoomProperties, 4);
    }

    RoomOptions BuildRoomOptions()
    {
        Hashtable roomProperties = new Hashtable();
        roomProperties.Add("TM", currentTrickMode);
        roomProperties.Add("RM", currentTrumpMode);

        return new RoomOptions
        {
            MaxPlayers = 4,
            IsOpen = true,
            IsVisible = true,
            CustomRoomProperties = roomProperties,
            CustomRoomPropertiesForLobby = new string[] { "TM", "RM" }
        };
    }

    public override void OnJoinedLobby()
    {
        if (findMatchAfterLobby)
            StartSmartMatchmaking();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log($"[Photon] JoinRandomFailed | {returnCode} | {message}");
        string roomName = "Room_" + Random.Range(1000, 9999);
        PhotonNetwork.CreateRoom(roomName, BuildRoomOptions());
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StopSearching(false);
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
    }

    public override void OnJoinedRoom()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("TM"))
                currentTrickMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["TM"];
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("RM"))
                currentTrumpMode = (int)PhotonNetwork.CurrentRoom.CustomProperties["RM"];
            
            if (GameSettings.Instance != null)
            {
                GameSettings.Instance.taashCategory = currentTrickMode;
                switch (currentTrumpMode)
                {
                    case 1: GameSettings.Instance.currentMode = GameModeType.TrumpSpades; break;
                    case 2: GameSettings.Instance.currentMode = GameModeType.ThirteenthCardTrump; break;
                    case 3: GameSettings.Instance.currentMode = GameModeType.CutToTrump; break;
                }
            }
            UpdateUIColors();
        }

        if (GameSettings.Instance != null)
        {
            Debug.Log($"[ModeManager] Mode Selected: {GameSettings.Instance.currentMode}");
            Debug.Log($"[ModeManager] Category: {GameSettings.Instance.taashCategory} Taash");
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.StopSearching(false);
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoading();
    }
}