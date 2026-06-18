using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;

public class PlayWithFriendsManager : MonoBehaviourPunCallbacks
{
    public static PlayWithFriendsManager Instance;

    [Header("PIN UI Components")]
    public TMP_InputField pinInputField;
    public TMP_Text generatedPinText;
    public GameObject pinCreationPanel;
    public TMP_Text errorText;

    [Header("Lobby Buttons & Panels")]
    public GameObject startGameButton;
    public GameObject modesPanel;
    public TMP_Text clientWaitingText;

    [Header("Online Matchmaking (shared seat panel)")]
    [Tooltip("Countdown / status text shown only while this panel is used as the online matchmaking lobby.")]
    public TMP_Text matchmakingTimerText;
    [Tooltip("Wooden plaque holding the timer text (shown only in online matchmaking mode).")]
    public GameObject matchmakingTimerPlaque;
    // When true the seat panel acts as the ONLINE matchmaking lobby (public room):
    // timer is shown, PIN / Create / manual Start / Bots controls are hidden, and the
    // match auto-starts (driven by DeckManager) once the table fills or the timer ends.
    bool _onlineMode;
    public bool IsOnlineMode => _onlineMode;

    [Header("Live Player List UI")]
    public TMP_Text[] playerSlotsText;
    [Tooltip("Avatar Image under each chair, parallel index to playerSlotsText.")]
    public UnityEngine.UI.Image[] playerSlotsAvatar;

    [Header("Room Creation / PIN Display")]
    [Tooltip("CREATE ROOM button shown until the private room exists.")]
    public GameObject createRoomButton;
    [Tooltip("Wooden plaque holding the ROOM ID text, shown once the room exists.")]
    public GameObject roomIdPlaque;

    [Header("Toggle Bot Settings")]
    public GameObject includeBotsButton;
    public TMP_Text includeBotsBtnText;
    bool areBotsIncluded;

    [Header("Game Table UI")]
    public GameObject homeMenuPanel;
    public GameObject gameTablePanel;

    [Header("Friends UI Slots")]
    public TMP_Text myUserIdText;
    public TMP_InputField addFriendInput;
    public Transform friendsListContainer;
    public GameObject friendUIPrefab;

    [Header("Friend Requests UI")]
    [Tooltip("Prefab for an incoming friend request row (must contain AcceptButton and DeclineButton).")]
    public GameObject friendRequestRowPrefab;

    // Incoming friend requests: fromUserId -> fromName
    readonly Dictionary<string, string> incomingRequests = new Dictionary<string, string>();
    DatabaseReference requestDbRef;
    DatabaseReference acceptDbRef;
    bool _requestListenerStarted;
    bool _acceptListenerStarted;

    [Header("Friends List Storage")]
    private const string FriendsPrefsKey = "SavedFriendsList";
    private const string FriendNamesPrefsKey = "SavedFriendsNames";
    private const string FirebaseDatabaseUrl = "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/";
    public List<string> myFriends = new List<string>();
    readonly Dictionary<string, string> friendDisplayNames = new Dictionary<string, string>();
    readonly Dictionary<string, FriendInfo> friendPhotonStatus = new Dictionary<string, FriendInfo>();
    PhotonView _photonView;
    DatabaseReference inviteDbRef;
    string _pendingInviteFriendId;
    string _pendingInviteFriendName;
    bool _inviteListenerStarted;
    string _listenersBoundUserId;
    readonly HashSet<string> _gameInvitesSent = new HashSet<string>();
    bool _pendingCreatePrivateRoom;
    Coroutine _createRoomCoroutine;

    public IReadOnlyList<string> MyFriends => myFriends;
    public IReadOnlyDictionary<string, string> IncomingRequests => incomingRequests;

    /// <summary>Fires whenever the incoming friend-request list changes (added/removed/accepted/declined).
    /// In-game panels subscribe to live-refresh their Accept/Decline rows.</summary>
    public event System.Action RequestsChanged;
    void NotifyRequestsChanged() => RequestsChanged?.Invoke();

    public string GetFriendDisplayName(string friendId) => GetFriendDisplayNameInternal(friendId);

    public FriendInfo GetFriendPhotonInfo(string friendId) =>
        friendPhotonStatus.TryGetValue(friendId, out FriendInfo info) ? info : null;

    public void MarkGameInviteSent(string friendUserId)
    {
        if (string.IsNullOrEmpty(friendUserId)) return;
        _gameInvitesSent.Add(friendUserId);
    }

    public bool IsGameInviteSent(string friendUserId) =>
        !string.IsNullOrEmpty(friendUserId) && _gameInvitesSent.Contains(friendUserId);

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        LoadFriends();
        if (myFriends == null) myFriends = new List<string>();
        EnsurePhotonUserId();
        EnsureNickname();
        EnsurePhotonView();
        PhotonNetwork.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        // Keep receiving room property updates while this panel is hidden.
    }

    void EnsureNickname()
    {
        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
        {
            PhotonNetwork.NickName = "Player_" + Random.Range(100, 999);
            Debug.Log("My Random Name Set To: " + PhotonNetwork.NickName);
        }
    }

    void EnsurePhotonView()
    {
        if (_photonView == null)
            _photonView = GetComponent<PhotonView>();

        if (_photonView == null)
        {
            _photonView = gameObject.AddComponent<PhotonView>();
            _photonView.Synchronization = ViewSynchronization.Off;
        }
    }

    void OnDestroy()
    {
        PhotonNetwork.RemoveCallbackTarget(this);

        if (requestDbRef != null)
        {
            requestDbRef.ChildAdded -= OnFriendRequestAdded;
            requestDbRef.ChildRemoved -= OnFriendRequestRemoved;
        }
        if (acceptDbRef != null)
            acceptDbRef.ChildAdded -= OnFriendAcceptAdded;
        if (inviteDbRef != null)
            inviteDbRef.ChildAdded -= OnIncomingInviteAdded;

        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);
        if (clientWaitingText != null) clientWaitingText.gameObject.SetActive(false);
        if (includeBotsButton != null) includeBotsButton.SetActive(false);
        ClearPlayerListUI();
        EnsureFriendServicesStarted();

        // If this panel was activated as the online matchmaking lobby, do not touch the
        // friends-only Start button — ShowOnlineMatchmakingLobby() already configured it.
        if (_onlineMode) return;

        // New flow: the Start button is always visible on the seat panel but stays
        // greyed/disabled until the table is full. Re-apply correct state for any room.
        if (startGameButton != null)
        {
            startGameButton.SetActive(true);
            SetStartButtonInteractable(false);
        }
        CheckPlayerCountAndToggleStart();
    }

    /// <summary>Call after login / Photon ready so Firebase listeners use the real user id.</summary>
    public void EnsureFriendServicesStarted()
    {
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;

        if (_listenersBoundUserId != myId)
        {
            StopFriendListeners();
            _listenersBoundUserId = myId;
        }

        DisplayMyID();
        StartFriendRequestListener();
        StartFriendAcceptListener();
        StartInviteListener();
        RefreshFriendsListUI();
        CheckFriendsOnlineStatus();
    }

    void StopFriendListeners()
    {
        if (requestDbRef != null)
        {
            requestDbRef.ChildAdded -= OnFriendRequestAdded;
            requestDbRef.ChildRemoved -= OnFriendRequestRemoved;
            requestDbRef = null;
        }
        if (acceptDbRef != null)
        {
            acceptDbRef.ChildAdded -= OnFriendAcceptAdded;
            acceptDbRef = null;
        }
        if (inviteDbRef != null)
        {
            inviteDbRef.ChildAdded -= OnIncomingInviteAdded;
            inviteDbRef = null;
        }

        _requestListenerStarted = false;
        _acceptListenerStarted = false;
        _inviteListenerStarted = false;
        incomingRequests.Clear();
    }

    void EnsurePhotonUserId()
    {
        if (PhotonNetwork.AuthValues == null)
            PhotonNetwork.AuthValues = new AuthenticationValues();

        if (string.IsNullOrEmpty(PhotonNetwork.AuthValues.UserId))
        {
            string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("PhotonUserId", uid);
            PhotonNetwork.AuthValues.UserId = uid;
        }
    }

    // ==========================================
    // 1. HOST: CREATE PRIVATE ROOM (modes later)
    // ==========================================

    public void CreatePrivateRoom()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (PhotonNetwork.InRoom)
        {
            ShowUIError("Leave the current room first.");
            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            _pendingCreatePrivateRoom = false;
            if (_createRoomCoroutine != null)
            {
                StopCoroutine(_createRoomCoroutine);
                _createRoomCoroutine = null;
            }
            DoCreatePrivateRoom();
            return;
        }

        if (!NetworkManager.HasInternet())
        {
            ShowUIError("No internet connection.");
            return;
        }

        _pendingCreatePrivateRoom = true;

        if (NetworkManager.IsPhotonConnectingOrConnected())
            ShowUIError("Connecting... please wait.");
        else
        {
            ShowUIError("Connecting to server...");
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ConnectToPhoton();
        }

        if (_createRoomCoroutine != null)
            StopCoroutine(_createRoomCoroutine);
        _createRoomCoroutine = StartCoroutine(WaitAndCreatePrivateRoomRoutine());
    }

    void DoCreatePrivateRoom()
    {
        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.InRoom) return;

        string newPin = Random.Range(10000, 99999).ToString();
        Debug.Log("Generating PIN: " + newPin);

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = 4,
            IsVisible = false,
            IsOpen = true,
            // Required so players can read each other's account id (Player.UserId) in-game,
            // used by the friend / stats popup.
            PublishUserId = true
        };

        PhotonNetwork.CreateRoom(newPin, roomOptions);
    }

    IEnumerator WaitAndCreatePrivateRoomRoutine()
    {
        float timeout = 20f;
        while (timeout > 0f && _pendingCreatePrivateRoom)
        {
            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
            {
                _pendingCreatePrivateRoom = false;
                _createRoomCoroutine = null;
                if (errorText != null) errorText.gameObject.SetActive(false);
                DoCreatePrivateRoom();
                yield break;
            }

            if (!NetworkManager.IsPhotonConnectingOrConnected() && NetworkManager.HasInternet()
                && NetworkManager.Instance != null)
            {
                NetworkManager.Instance.ConnectToPhoton();
            }

            yield return new WaitForSeconds(0.25f);
            timeout -= 0.25f;
        }

        _pendingCreatePrivateRoom = false;
        _createRoomCoroutine = null;
        if (!PhotonNetwork.IsConnectedAndReady)
            ShowUIError("Could not connect. Try again.");
    }

    public override void OnConnectedToMaster()
    {
        if (_pendingCreatePrivateRoom && PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
        {
            if (_createRoomCoroutine != null)
            {
                StopCoroutine(_createRoomCoroutine);
                _createRoomCoroutine = null;
            }
            _pendingCreatePrivateRoom = false;
            if (errorText != null) errorText.gameObject.SetActive(false);
            DoCreatePrivateRoom();
        }
    }

    // ==========================================
    // 2. CLIENT: JOIN ROOM WITH PIN
    // ==========================================

    public void JoinRoomWithPIN()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (pinInputField == null || string.IsNullOrEmpty(pinInputField.text))
        {
            ShowUIError("Enter valid PIN!");
            return;
        }

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            if (!NetworkManager.HasInternet())
            {
                ShowUIError("No internet connection.");
                return;
            }
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ConnectToPhoton();
            ShowUIError("Connecting... please wait.");
            return;
        }

        PhotonNetwork.JoinRoom(pinInputField.text.Trim());
    }

    /// <summary>
    /// Joins a private room using a PIN supplied directly (used by the in-Modes JOIN TABLE panel,
    /// which has its own input field separate from the Play-with-Friends panel).
    /// </summary>
    public void JoinRoomWithPINText(string pin)
    {
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (string.IsNullOrEmpty(pin) || string.IsNullOrWhiteSpace(pin))
        {
            ShowUIError("Enter valid PIN!");
            return;
        }

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            if (!NetworkManager.HasInternet())
            {
                ShowUIError("No internet connection.");
                return;
            }

            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ConnectToPhoton();
            ShowUIError("Connecting... please wait.");
            return;
        }

        PhotonNetwork.JoinRoom(pin.Trim());
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError("Room Join Failed: " + message);
        ShowUIError("Invalid PIN or Room Full!");
    }

    void ShowUIError(string errorMsg)
    {
        if (errorText == null) return;
        errorText.text = errorMsg;
        errorText.gameObject.SetActive(true);
    }

    // ==========================================
    // 3. WHEN ANYONE JOINS THE ROOM
    // ==========================================

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        // Online matchmaking: this panel is the lobby — fill seats with real players.
        if (_onlineMode)
        {
            UpdatePlayerListUI();
            return;
        }

        if (!PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
        {
            Debug.Log("Private Room Joined. Waiting in Lobby...");
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.StayInPrivateLobbyUI();
            ShowPrivateRoomLobbyUI();
            TrySendPendingInvite();
            return;
        }
    }

    void ShowPrivateRoomLobbyUI()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        // New flow: ensure the seat panel itself is visible (a client may have joined
        // via the JoinTable panel on the Modes screen) and hide the Modes panel.
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (modesPanel != null) modesPanel.SetActive(false);

        // Friends mode: show PIN/Room ID plaque, hide online timer.
        _onlineMode = false;
        ApplyModeControls(false);
        SetSeatPanelTitle("SELECT CHAIRS");

        if (pinCreationPanel != null)
        {
            pinCreationPanel.SetActive(true);
            pinCreationPanel.transform.SetAsLastSibling();
        }
        if (generatedPinText != null) generatedPinText.text = "ROOM ID :- " + PhotonNetwork.CurrentRoom.Name;
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj))
            ApplyBotsIncludedState((bool)botsObj);
        else
            ApplyBotsIncludedState(false);

        UpdatePlayerListUI();
        CheckPlayerCountAndToggleStart();
    }

    public void ToggleBots()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

        bool newState = !areBotsIncluded;
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
        props["BotsIncluded"] = newState;
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    void ApplyBotsIncludedState(bool included)
    {
        areBotsIncluded = included;
        if (includeBotsBtnText != null)
            includeBotsBtnText.text = areBotsIncluded ? "Remove Bots" : "Include Bots";
    }

    void UpdatePlayerListUI()
    {
        if (!PhotonNetwork.InRoom || playerSlotsText == null || playerSlotsText.Length == 0) return;

        Player[] currentPlayers = PhotonRoomPlayers.GetSorted();
        int realPlayerCount = currentPlayers.Length;

        for (int i = 0; i < playerSlotsText.Length; i++)
        {
            if (playerSlotsText[i] == null) continue;

            if (i < realPlayerCount)
            {
                Player p = currentPlayers[i];
                string hostTag = p.IsMasterClient ? " (Host)" : "";
                playerSlotsText[i].text = p.NickName + hostTag;
                playerSlotsText[i].color = Color.white;
                SetSeatAvatar(i, GetAvatarIndexForPlayer(p), true);
            }
            else if (areBotsIncluded)
            {
                playerSlotsText[i].text = realPlayerCount == 3 && i == realPlayerCount
                    ? "DehlaBot"
                    : "AI Bot " + (i - realPlayerCount + 1);
                playerSlotsText[i].color = new Color(0.4f, 1f, 0.4f, 1f);
                SetSeatAvatar(i, -1, true); // fallback bot avatar
            }
            else
            {
                playerSlotsText[i].text = _onlineMode ? "Waiting..." : "Waiting for Friend...";
                playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
                SetSeatAvatar(i, -1, false); // empty seat
            }
        }
    }

    // ==========================================
    // SEAT AVATARS (real selected profile images)
    // ==========================================

    Sprite[] _avatarPoolCache;

    /// <summary>Canonical avatar sprite pool (same list profile indices were chosen from).</summary>
    Sprite[] GetAvatarPool()
    {
        if (PlayerProfileManager.Instance != null
            && PlayerProfileManager.Instance.profileSprites != null
            && PlayerProfileManager.Instance.profileSprites.Length > 0)
        {
            _avatarPoolCache = PlayerProfileManager.Instance.profileSprites;
            return _avatarPoolCache;
        }
        if (_avatarPoolCache != null && _avatarPoolCache.Length > 0) return _avatarPoolCache;
        if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
            _avatarPoolCache = MatchmakingManager.GlobalProfileSprites.ToArray();
        return _avatarPoolCache;
    }

    /// <summary>Avatar index a player selected: local uses PlayerPrefs, remote uses synced custom property.</summary>
    int GetAvatarIndexForPlayer(Player p)
    {
        if (p == null) return -1;
        if (PhotonNetwork.LocalPlayer != null && p.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            int local = PlayerProfileManager.GetSavedAvatarIndex();
            if (local >= 0) return local;
        }
        if (p.CustomProperties != null
            && p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val) && val != null)
        {
            if (val is int vi) return vi;
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
        }
        return -1;
    }

    /// <summary>Assigns the avatar sprite for a seat. occupied=false dims the slot (empty seat).</summary>
    void SetSeatAvatar(int seatIndex, int avatarIndex, bool occupied)
    {
        if (playerSlotsAvatar == null || seatIndex < 0 || seatIndex >= playerSlotsAvatar.Length) return;
        UnityEngine.UI.Image img = playerSlotsAvatar[seatIndex];
        if (img == null) return;

        Sprite[] pool = GetAvatarPool();
        if (pool != null && pool.Length > 0)
        {
            int idx = avatarIndex;
            if (idx < 0 || idx >= pool.Length) idx = Mathf.Abs(seatIndex + 1) % pool.Length;
            img.sprite = pool[idx];
            img.preserveAspect = true;
        }
        // Dim empty seats, full colour for occupied ones.
        img.color = occupied ? Color.white : new Color(1f, 1f, 1f, 0.25f);
    }

    void ClearPlayerListUI()
    {
        if (playerSlotsText == null) return;

        for (int i = 0; i < playerSlotsText.Length; i++)
        {
            if (playerSlotsText[i] == null) continue;
            playerSlotsText[i].text = "Waiting for Friend...";
            playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
        }
    }

    void CheckPlayerCountAndToggleStart()
    {
        // Online matchmaking auto-starts (DeckManager-driven) and has no manual Start button.
        if (_onlineMode) return;

        if (startGameButton == null)
            UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        if (includeBotsButton != null)
            includeBotsButton.SetActive(PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount < DeckManager.MaxTableSeats);

        if (!PhotonNetwork.IsMasterClient)
        {
            if (startGameButton != null) startGameButton.SetActive(false);
            return;
        }

        if (startGameButton == null) return;

        // Host always sees the Start button, but it stays greyed/disabled until the
        // table is full (4 seats) or bots are included.
        startGameButton.SetActive(true);
        bool canStart = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;
        SetStartButtonInteractable(canStart);
    }

    void SetStartButtonInteractable(bool on)
    {
        if (startGameButton == null) return;

        Button btn = startGameButton.GetComponent<Button>();
        if (btn != null) btn.interactable = on;

        CanvasGroup cg = startGameButton.GetComponent<CanvasGroup>();
        if (cg == null) cg = startGameButton.AddComponent<CanvasGroup>();
        cg.alpha = on ? 1f : 0.5f;
        cg.interactable = on;
        cg.blocksRaycasts = on;
    }

    /// <summary>
    /// Called when the seat/lobby panel is opened (host taps Play on the modes screen).
    /// Resets the player list and shows the Start button greyed-out until the table fills.
    /// </summary>
    public void OnSeatPanelOpened()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);
        ClearPlayerListUI();

        if (startGameButton == null)
            UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

        if (startGameButton != null)
        {
            startGameButton.SetActive(true);
            SetStartButtonInteractable(false);
        }

        // Friends mode: ensure online controls are off and PIN plaque is shown.
        _onlineMode = false;
        ApplyModeControls(false);
        SetSeatPanelTitle("SELECT CHAIRS");

        // New flow: the Create Room button is hidden on the seat panel, so the host
        // automatically creates the private room as soon as this panel opens. Friends
        // join from the Modes screen's JOIN TABLE panel using the shown ROOM ID.
        if (!PhotonNetwork.InRoom)
            CreatePrivateRoom();

        CheckPlayerCountAndToggleStart();
    }

    // ==========================================
    // ONLINE MATCHMAKING (shared seat panel)
    // ==========================================

    /// <summary>
    /// Shows this seat panel as the ONLINE matchmaking lobby. Hides PIN / Create / manual
    /// Start / Bots controls, shows the countdown timer, and fills seats with real players
    /// as they join the public room. The match auto-starts (driven by DeckManager) once the
    /// table is full or the timer expires.
    /// </summary>
    public void ShowOnlineMatchmakingLobby()
    {
        _onlineMode = true;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        transform.SetAsLastSibling();

        if (errorText != null) errorText.gameObject.SetActive(false);
        if (modesPanel != null) modesPanel.SetActive(false);
        if (startGameButton != null) startGameButton.SetActive(false); // online auto-starts

        ApplyModeControls(true);
        SetSeatPanelTitle("FINDING PLAYERS");

        if (matchmakingTimerText != null) matchmakingTimerText.text = "Finding players...";

        ClearPlayerListUI();
        if (PhotonNetwork.InRoom) UpdatePlayerListUI();
    }

    /// <summary>Forwarded from DeckManager's matchmaking countdown (players found + seconds left).</summary>
    public void UpdateOnlineTimer(int playersFound, int countdown)
    {
        if (!_onlineMode) return;

        if (matchmakingTimerText != null)
        {
            matchmakingTimerText.text = playersFound >= DeckManager.MaxTableSeats
                ? "Starting game..."
                : $"Players: {playersFound}/{DeckManager.MaxTableSeats}    Starting in {Mathf.Max(0, countdown)}s";
        }

        if (PhotonNetwork.InRoom) UpdatePlayerListUI();
    }

    /// <summary>Hides the seat panel (used on match found / cancel for the online flow).</summary>
    public void HideLobby()
    {
        _onlineMode = false;
        ApplyModeControls(false);
        HidePrivateFriendsLobbyUI();
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// <summary>Toggles friends-only vs online-only seat-panel controls.</summary>
    void ApplyModeControls(bool online)
    {
        GameObject createBtn = createRoomButton;
        if (createBtn == null)
        {
            Transform t = transform.Find("ContentArea/Host Section/Btn_CreateRoom");
            if (t != null) createBtn = t.gameObject;
        }
        if (createBtn != null) createBtn.SetActive(false); // room auto-creates in both flows

        Transform join = transform.Find("ContentArea/Join Section");
        if (join != null) join.gameObject.SetActive(false); // join handled on the modes screen

        GameObject plaque = roomIdPlaque;
        if (plaque == null)
        {
            Transform t = transform.Find("RoomIdPlaque");
            if (t != null) plaque = t.gameObject;
        }
        if (plaque != null) plaque.SetActive(!online); // PIN/Room ID only for friends

        if (online && includeBotsButton != null) includeBotsButton.SetActive(false);

        if (matchmakingTimerPlaque != null) matchmakingTimerPlaque.SetActive(online);
        if (matchmakingTimerText != null) matchmakingTimerText.gameObject.SetActive(online);
    }

    void SetSeatPanelTitle(string text)
    {
        Transform t = transform.Find("TitlePlaque/Title");
        if (t == null) t = transform.Find("Title");
        if (t != null)
        {
            TMP_Text label = t.GetComponent<TMP_Text>();
            if (label != null) label.text = text;
        }
    }

    /// <summary>
    /// Seat-panel BACK button. In online matchmaking it cancels the search; in friends
    /// mode it leaves the private room and returns to the modes screen.
    /// </summary>
    public void OnSeatPanelBackClicked()
    {
        if (_onlineMode)
        {
            if (MatchmakingManager.Instance != null)
                MatchmakingManager.Instance.OnCancelClicked();
            else
                HideLobby();
            return;
        }

        LeavePrivateRoomIfAny();
        HidePrivateFriendsLobbyUI();
        gameObject.SetActive(false);
        if (ModeManager.Instance != null)
            ModeManager.Instance.OnClick_ClosePlayWithFriends();
    }

    /// <summary>Leaves the private (invisible) room if we are currently in one.</summary>
    public void LeavePrivateRoomIfAny()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LeaveRoom();
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        // Online matchmaking lobby: seat the newly joined real player.
        if (_onlineMode)
        {
            UpdatePlayerListUI();
            return;
        }

        if (PhotonNetwork.CurrentRoom.IsVisible) return;

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats && areBotsIncluded)
        {
            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
            {
                { "BotsIncluded", false }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        ShowPrivateRoomLobbyUI();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (_onlineMode)
        {
            UpdatePlayerListUI();
            return;
        }

        if (PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
        {
            UpdatePlayerListUI();
            CheckPlayerCountAndToggleStart();
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        // Refresh seat avatars when a player's selected avatar arrives/changes.
        if (changedProps != null && changedProps.ContainsKey(PlayerProfileManager.PROP_AVATAR)
            && gameObject.activeInHierarchy && PhotonNetwork.InRoom)
        {
            UpdatePlayerListUI();
        }
    }

    // ==========================================
    // SHARE PIN LOGIC
    // ==========================================

    public void ShareRoomPIN()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible)
            return;

        string pin = PhotonNetwork.CurrentRoom.Name;
        string shareMessage = $"Aaja Dehla Pakad khelte hain! Mera Private Room PIN hai: {pin}. Jaldi join kar!";

        GUIUtility.systemCopyBuffer = shareMessage;
        Debug.Log("Copied to clipboard: " + shareMessage);

        if (errorText != null)
        {
            errorText.text = "PIN Copied!";
            errorText.gameObject.SetActive(true);
        }
    }

    // ==========================================
    // HOST CLICKS START: OPENS MODES PANEL & HIDES FRIENDS PANEL
    // ==========================================

    public void OpenModesPanelForHost()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ResolveModesPanel();
        if (modesPanel != null)
        {
            modesPanel.SetActive(true);
            CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }

        // RPC pehle — panel band karne se pehle clients ko notify karo
        photonView.RPC(nameof(RPC_ShowModesPanelToClients), RpcTarget.Others);

        if (PhotonNetwork.CurrentRoom != null)
            PhotonNetwork.CurrentRoom.IsOpen = false;

        gameObject.SetActive(false);
    }

    [PunRPC]
    void RPC_ShowModesPanelToClients()
    {
        ResolveModesPanel();
        if (modesPanel != null)
        {
            modesPanel.SetActive(true);
            CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
        }

        if (clientWaitingText != null)
        {
            clientWaitingText.gameObject.SetActive(true);
            clientWaitingText.text = "Host is selecting game modes...";
        }

        if (ModeManager.Instance != null)
            ModeManager.Instance.ApplyLiveModesFromRoomIfPresent();

        gameObject.SetActive(false);
    }

    // Live sync: 1 Sar=1, 2 Sar=2
    public void HostSelectedGameMode(int modeIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "GameMode", modeIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // Live sync: 1 Taash=1, 2 Taash=2
    public void HostSelectedTaashMode(int taashIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "TaashMode", taashIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // Live sync: Spades=1, 13th Card=2, Cut to Trump=3, Cut2Trump=4
    public void HostSelectedTrumpMode(int trumpIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "TrumpMode", trumpIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // Live sync: Logic A=1, Logic B=2, Logic C=3
    public void HostSelectedLogicMode(int logicIndex)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "LogicMode", logicIndex }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // New flow: modes are chosen BEFORE the seat panel opens, so the seat panel's
    // Start button now starts the game directly instead of re-opening the modes panel.
    public void OpenModesPanel() => OnHostStartFriendsGame();

    // Backward-compatible alias for Btn_StartPrivateGame
    public void StartPrivateGame() => OnHostStartFriendsGame();

    /// <summary>
    /// Host pressed Start on the seat panel. Only proceeds when the table is full
    /// (4 players) or bots are included, then routes through the single ModeManager
    /// start router which performs the private-friends final start.
    /// </summary>
    public void OnHostStartFriendsGame()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            return;

        bool full = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;
        if (!full)
        {
            ShowUIError("Need 4 players to start!");
            return;
        }

        if (ModeManager.Instance != null)
            ModeManager.Instance.StartGameFromModePanel();
        else
            FinalStartWithSelectedModes();
    }

    void ResolveModesPanel()
    {
        if (modesPanel == null && ModeManager.Instance != null)
            modesPanel = ModeManager.Instance.panelModes;
    }

    void ResolveHomeMenuPanel()
    {
        if (homeMenuPanel != null) return;
        if (NetworkManager.Instance != null)
            homeMenuPanel = NetworkManager.Instance.homeMenuPanel;
        else if (ModeManager.Instance != null)
            homeMenuPanel = ModeManager.Instance.panelHomeScreen;
    }

    void ResolveGameTablePanel()
    {
        if (gameTablePanel != null) return;
        if (NetworkManager.Instance != null)
            gameTablePanel = NetworkManager.Instance.gameTablePanel;
    }

    // ==========================================
    // TRAFFIC POLICE: MASTER START BUTTON ROUTER
    // ==========================================

    // The Mode Panel Start button must ALWAYS go through the single clean router in ModeManager.
    // PlayWithFriendsManager must never decide Play Online / Play Bots routing itself.
    public void OnModePanelStartClicked()
    {
        if (ModeManager.Instance != null)
            ModeManager.Instance.StartGameFromModePanel();
        else
            Debug.LogError("[StartRoute] ModeManager.Instance missing — cannot route Mode Panel Start.");
    }

    public void OnStartButtonClick() => OnModePanelStartClicked();

    // ==========================================
    // FINAL CONFIRM & PLAY (HOST PRESSES START ON MODES PANEL)
    // ==========================================

    public void FinalStartWithSelectedModes()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        if (GameSettings.Instance == null) return;

        Debug.Log("Host pressed Final Start! Telling everyone to start the game...");

        ExitGames.Client.Photon.Hashtable customRoomProperties = new ExitGames.Client.Photon.Hashtable();

        if (ModeManager.Instance != null)
        {
            customRoomProperties["TM"] = ModeManager.Instance.currentTrickMode;
            customRoomProperties["RM"] = ModeManager.Instance.currentTrumpMode;
            customRoomProperties["SM"] = ModeManager.Instance.currentSarMode;
            customRoomProperties["LM"] = ModeManager.Instance.currentLogicMode;
        }
        else
        {
            customRoomProperties["TM"] = GameSettings.Instance.taashCategory;
            customRoomProperties["RM"] = 3;
            customRoomProperties["SM"] = GameSettings.Instance.currentSarMode == SarModeType.TwoSar ? 2 : 1;
            customRoomProperties["LM"] = 1;
        }

        customRoomProperties["ModesLocked"] = true;
        customRoomProperties["BotsIncluded"] = areBotsIncluded;
        PhotonNetwork.CurrentRoom.SetCustomProperties(customRoomProperties);

        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        photonView.RPC(nameof(RPC_StartGameForEveryone), RpcTarget.All);
    }

    [PunRPC]
    void RPC_StartGameForEveryone()
    {
        Debug.Log("[GameStart] Friends RPC_StartGameForEveryone received");

        ResolveModesPanel();
        if (modesPanel != null) modesPanel.SetActive(false);
        HidePrivateFriendsLobbyUI();

        if (ModeManager.Instance != null)
            ModeManager.Instance.SyncModesFromRoom();

        if (TrumpManager.Instance != null)
            TrumpManager.ApplyTrumpForCurrentGameMode(false);

        DeckManager.botActorNumbers.Clear();

        if (PhotonNetwork.IsMasterClient)
        {
            int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
            int botsNeeded = DeckManager.MaxTableSeats - realPlayerCount;

            for (int i = 0; i < botsNeeded; i++)
                DeckManager.botActorNumbers.Add(100 + i);

            Debug.Log($"[Bot System] {botsNeeded} Bots added for private match!");

            if (botsNeeded > 0 && DeckManager.Instance != null)
            {
                DeckManager.Instance.photonView.RPC(
                    "RPC_SyncBotsOnly",
                    RpcTarget.All,
                    DeckManager.botActorNumbers.ToArray());
            }
        }

        // Single shared, guarded entry: hides menus, shows game scene, ensures local
        // NetworkPlayer exists, initializes gameplay UI, and (master only) starts dealing once.
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.BeginGameAfterRoomReady();
        else
            Debug.LogError("[GameStart ERROR] NetworkManager.Instance missing — cannot start friends game.");

        // Hide this lobby manager LAST so the work above runs while it is still active.
        gameObject.SetActive(false);
    }

    void HidePlayWithFriendsLobbyPanel()
    {
        if (pinCreationPanel != null) pinCreationPanel.SetActive(false);
        if (startGameButton != null) startGameButton.SetActive(false);
    }

    public void HidePrivateFriendsLobbyUI()
    {
        if (modesPanel != null) modesPanel.SetActive(false);
        HidePlayWithFriendsLobbyPanel();
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.IsVisible) return;

        if (propertiesThatChanged.ContainsKey("ModesLocked")
            && propertiesThatChanged["ModesLocked"] is bool locked
            && locked)
        {
            if (ModeManager.Instance != null)
                ModeManager.Instance.SyncModesFromRoom();
            HidePrivateFriendsLobbyUI();
            if (TrumpManager.Instance != null)
                TrumpManager.ApplyTrumpForCurrentGameMode(false);
            Debug.Log("Host locked the modes!");
            return;
        }

        if (ModeManager.Instance == null) return;

        if (propertiesThatChanged.ContainsKey("GameMode"))
        {
            int selectedMode = (int)propertiesThatChanged["GameMode"];
            ModeManager.Instance.OnClick_SarMode(selectedMode, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("TrumpMode"))
        {
            int selectedTrump = (int)propertiesThatChanged["TrumpMode"];
            ModeManager.Instance.OnClick_TrumpMode(selectedTrump, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("TaashMode"))
        {
            int selectedTaash = (int)propertiesThatChanged["TaashMode"];
            ModeManager.Instance.OnClick_TrickMode(selectedTaash, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("LogicMode"))
        {
            int selectedLogic = (int)propertiesThatChanged["LogicMode"];
            ModeManager.Instance.OnClick_LogicMode(selectedLogic, broadcastToRoom: false);
        }

        if (propertiesThatChanged.ContainsKey("BotsIncluded"))
        {
            ApplyBotsIncludedState((bool)propertiesThatChanged["BotsIncluded"]);
            UpdatePlayerListUI();
            CheckPlayerCountAndToggleStart();
        }
    }

    // ==========================================
    // 6. FRIENDS LIST LOGIC
    // ==========================================

    public void DisplayMyID()
    {
        ResolveMyUserIdText();
        if (myUserIdText == null) return;

        // Show the short public UID (PUBG / Free Fire style). Tap to copy it.
        string uid = GameUidService.LocalGameUid;
        UidUI.BindCopyLabel(myUserIdText, uid, "My UID: ");
    }

    void ResolveMyUserIdText()
    {
        if (myUserIdText != null) return;

        // The Friends panel header has a "Text_MyID" label that may not be wired in the inspector.
        if (FriendsPanelUIController.Instance != null)
        {
            foreach (Transform t in FriendsPanelUIController.Instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Text_MyID")
                {
                    myUserIdText = t.GetComponent<TMP_Text>();
                    break;
                }
            }
        }
    }

    public void UI_AddFriendBtnClicked()
    {
        if (addFriendInput == null) return;

        string newFriendId = addFriendInput.text.Trim();
        if (string.IsNullOrEmpty(newFriendId)) return;

        SendFriendRequest(newFriendId, null);
        addFriendInput.text = "";
    }

    public void AddFriend(string friendUserId, string displayName = null)
    {
        if (string.IsNullOrEmpty(friendUserId)) return;

        string myId = PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId ?? "";
        if (!string.IsNullOrEmpty(myId) && friendUserId == myId)
        {
            ShowUIError("You cannot add yourself!");
            return;
        }

        if (myFriends.Contains(friendUserId))
        {
            ShowUIError("Already in friends list.");
            return;
        }

        myFriends.Add(friendUserId);
        if (!string.IsNullOrEmpty(displayName))
            friendDisplayNames[friendUserId] = displayName;
        else if (!friendDisplayNames.ContainsKey(friendUserId))
            friendDisplayNames[friendUserId] = friendUserId;

        SaveFriends();
        RefreshFriendsListUI();
        CheckFriendsOnlineStatus();
        Debug.Log($"[Friends] Added {friendDisplayNames[friendUserId]} ({friendUserId})");
    }

    public void CheckFriendsOnlineStatus()
    {
        EnsurePhotonUserId();
        if (myFriends.Count > 0 && PhotonNetwork.IsConnectedAndReady)
            PhotonNetwork.FindFriends(myFriends.ToArray());
    }

    /// <summary>
    /// Removes a user from the local friends list (used by the in-game player-stats popup
    /// REMOVE action). Persists the change and refreshes any friends UI.
    /// </summary>
    public void RemoveFriend(string friendUserId)
    {
        if (string.IsNullOrEmpty(friendUserId)) return;
        if (!myFriends.Remove(friendUserId)) return;

        friendDisplayNames.Remove(friendUserId);
        _gameInvitesSent.Remove(friendUserId);

        SaveFriends();
        RefreshFriendsListUI();
        CheckFriendsOnlineStatus();
        Debug.Log($"[Friends] Removed {friendUserId}");
    }

    /// <summary>True if the given user id is already in the local friends list.</summary>
    public bool IsFriend(string friendUserId) =>
        !string.IsNullOrEmpty(friendUserId) && myFriends.Contains(friendUserId);

    // ==========================================
    // FRIEND REQUEST SYSTEM (Accept / Decline)
    // ==========================================

    string MyUserId => PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId ?? "";
    string MyDisplayName => string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Player" : PhotonNetwork.NickName;

    /// <summary>Sends a friend request to the target user (they get Accept/Decline).</summary>
    public void SendFriendRequest(string targetUserId, string targetName)
    {
        if (string.IsNullOrEmpty(targetUserId)) return;
        targetUserId = targetUserId.Trim();

        // The whole friend system keys on the account id (Firebase uid / Photon UserId).
        // But the UID users see and type is the short 10-digit public GameUid. If the caller
        // passed a GameUid (e.g. from the home "Add by UID" box), resolve it to the account id
        // first — otherwise the request is written to a path nobody listens on and is lost.
        if (GameUidService.LooksLikeUid(targetUserId))
        {
            GameUidService.ResolveFirebaseUid(targetUserId, resolved =>
            {
                if (string.IsNullOrEmpty(resolved))
                {
                    ShowUIError("No player found with that UID.");
                    return;
                }
                SendFriendRequest(resolved, targetName);
            });
            return;
        }

        string myId = MyUserId;
        if (!string.IsNullOrEmpty(myId) && targetUserId == myId)
        {
            ShowUIError("You cannot add yourself!");
            return;
        }

        if (myFriends.Contains(targetUserId))
        {
            ShowUIError("Already in your friends list.");
            return;
        }

        if (incomingRequests.ContainsKey(targetUserId))
        {
            AcceptFriendRequest(targetUserId, incomingRequests[targetUserId]);
            return;
        }

        if (string.IsNullOrEmpty(myId))
        {
            ShowUIError("Not connected yet. Try again.");
            return;
        }

        // Remember name locally so it shows correctly once accepted.
        if (!string.IsNullOrEmpty(targetName))
            friendDisplayNames[targetUserId] = targetName;

        var requestData = new Dictionary<string, object>
        {
            { "fromName", MyDisplayName },
            { "createdAt", System.DateTime.UtcNow.Ticks }
        };

        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
            .Child("friend_requests").Child(targetUserId).Child(myId)
            .SetValueAsync(requestData).ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("[FriendReq] Send failed: " + task.Exception);
                    ShowUIError("Request failed. Try again.");
                    return;
                }
                ShowUIError(string.IsNullOrEmpty(targetName) ? "Friend request sent!" : $"Request sent to {targetName}!");
                Debug.Log($"[FriendReq] Sent request to {targetUserId}");
            });
    }

    public void StartFriendRequestListener()
    {
        if (_requestListenerStarted) return;
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;

        requestDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
            .Child("friend_requests").Child(myId);
        requestDbRef.ChildAdded += OnFriendRequestAdded;
        requestDbRef.ChildRemoved += OnFriendRequestRemoved;
        _requestListenerStarted = true;
        Debug.Log("[FriendReq] Listening for friend requests on " + myId);

        requestDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

            foreach (DataSnapshot child in task.Result.Children)
            {
                string fromId = child.Key;
                if (string.IsNullOrEmpty(fromId) || myFriends.Contains(fromId)) continue;
                string fromName = child.Child("fromName").Value?.ToString() ?? fromId;
                incomingRequests[fromId] = fromName;
            }

            RefreshFriendsListUI();
        });
    }

    void OnFriendRequestAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;

        string fromId = args.Snapshot.Key;
        if (string.IsNullOrEmpty(fromId) || myFriends.Contains(fromId)) return;

        string fromName = args.Snapshot.Child("fromName").Value?.ToString() ?? fromId;
        incomingRequests[fromId] = fromName;
        Debug.Log($"[FriendReq] Incoming request from {fromName} ({fromId})");
        RefreshFriendsListUI();
        NotifyRequestsChanged();
    }

    void OnFriendRequestRemoved(object sender, ChildChangedEventArgs args)
    {
        if (args.Snapshot == null) return;
        string fromId = args.Snapshot.Key;
        if (!string.IsNullOrEmpty(fromId) && incomingRequests.Remove(fromId))
            RefreshFriendsListUI();
    }

    public void AcceptFriendRequest(string fromUserId, string fromName)
    {
        if (string.IsNullOrEmpty(fromUserId)) return;

        // Add them to MY friends list locally.
        AddFriend(fromUserId, fromName);

        // Tell the requester that I accepted so they add me back.
        string myId = MyUserId;
        if (!string.IsNullOrEmpty(myId))
        {
            var acceptData = new Dictionary<string, object>
            {
                { "name", MyDisplayName },
                { "createdAt", System.DateTime.UtcNow.Ticks }
            };
            FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
                .Child("friend_accepts").Child(fromUserId).Child(myId)
                .SetValueAsync(acceptData);

            // Remove the pending request from my inbox.
            FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
                .Child("friend_requests").Child(myId).Child(fromUserId)
                .RemoveValueAsync();
        }

        incomingRequests.Remove(fromUserId);
        ShowUIError($"You and {fromName} are now friends!");
        RefreshFriendsListUI();
    }

    public void DeclineFriendRequest(string fromUserId)
    {
        if (string.IsNullOrEmpty(fromUserId)) return;

        string myId = MyUserId;
        if (!string.IsNullOrEmpty(myId))
        {
            FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
                .Child("friend_requests").Child(myId).Child(fromUserId)
                .RemoveValueAsync();
        }

        incomingRequests.Remove(fromUserId);
        RefreshFriendsListUI();
        NotifyRequestsChanged();
    }

    public void StartFriendAcceptListener()
    {
        if (_acceptListenerStarted) return;
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;

        acceptDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
            .Child("friend_accepts").Child(myId);
        acceptDbRef.ChildAdded += OnFriendAcceptAdded;
        _acceptListenerStarted = true;
        Debug.Log("[FriendReq] Listening for friend acceptances on " + myId);

        acceptDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

            foreach (DataSnapshot child in task.Result.Children)
            {
                if (!child.Exists) continue;
                string accepterId = child.Key;
                string accepterName = child.Child("name").Value?.ToString() ?? accepterId;
                if (!string.IsNullOrEmpty(accepterId) && !myFriends.Contains(accepterId))
                    AddFriend(accepterId, accepterName);
                child.Reference.RemoveValueAsync();
            }

            RefreshFriendsListUI();
        });
    }

    void OnFriendAcceptAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;

        string accepterId = args.Snapshot.Key;
        if (string.IsNullOrEmpty(accepterId)) return;

        string accepterName = args.Snapshot.Child("name").Value?.ToString() ?? accepterId;
        AddFriend(accepterId, accepterName);
        ShowUIError($"{accepterName} accepted your request!");

        // Consume the acceptance notice.
        args.Snapshot.Reference.RemoveValueAsync();
    }

    public override void OnFriendListUpdate(List<FriendInfo> friendList)
    {
        friendPhotonStatus.Clear();
        foreach (FriendInfo friend in friendList)
            friendPhotonStatus[friend.UserId] = friend;

        RefreshFriendsListUI();
    }

    public void RefreshFriendsListUI()
    {
        if (FriendsPanelUIController.Instance != null)
        {
            FriendsPanelUIController.Instance.RefreshAll();
            return;
        }

        RefreshFriendsListLegacy();
    }

    void RefreshFriendsListLegacy()
    {
        if (friendsListContainer == null || friendUIPrefab == null) return;

        foreach (Transform child in friendsListContainer)
            Destroy(child.gameObject);

        foreach (var kvp in incomingRequests)
        {
            if (string.IsNullOrEmpty(kvp.Key)) continue;
            SpawnRequestRow(kvp.Key, kvp.Value);
        }

        // 2) Then the accepted friends (with status + Invite).
        foreach (string friendId in myFriends)
        {
            if (string.IsNullOrEmpty(friendId)) continue;
            friendPhotonStatus.TryGetValue(friendId, out FriendInfo photonInfo);
            SpawnFriendRow(friendId, GetFriendDisplayNameInternal(friendId), photonInfo);
        }
    }

    void SpawnRequestRow(string fromId, string fromName)
    {
        GameObject prefab = friendRequestRowPrefab != null ? friendRequestRowPrefab : friendUIPrefab;
        if (prefab == null || friendsListContainer == null) return;

        GameObject row = Instantiate(prefab, friendsListContainer);

        TMP_Text infoText = FindPrimaryLabel(row.transform);
        if (infoText != null)
            infoText.text = $"{fromName}\n<size=18><color=#FFD479>wants to be friends</color></size>";

        Button acceptBtn = FindChildButton(row.transform, "AcceptButton");
        Button declineBtn = FindChildButton(row.transform, "DeclineButton");

        // Fallback: if named buttons not found, assume first=accept, second=decline.
        if (acceptBtn == null || declineBtn == null)
        {
            Button[] buttons = row.GetComponentsInChildren<Button>(true);
            if (buttons.Length >= 2)
            {
                acceptBtn = acceptBtn ?? buttons[0];
                declineBtn = declineBtn ?? buttons[1];
            }
        }

        if (acceptBtn != null)
        {
            acceptBtn.onClick.RemoveAllListeners();
            acceptBtn.onClick.AddListener(() => AcceptFriendRequest(fromId, fromName));
        }
        if (declineBtn != null)
        {
            declineBtn.onClick.RemoveAllListeners();
            declineBtn.onClick.AddListener(() => DeclineFriendRequest(fromId));
        }
    }

    string GetFriendDisplayNameInternal(string friendId)
    {
        if (friendDisplayNames.TryGetValue(friendId, out string name) && !string.IsNullOrEmpty(name))
            return name;
        return friendId;
    }

    void SpawnFriendRow(string friendId, string displayName, FriendInfo photonInfo)
    {
        GameObject row = Instantiate(friendUIPrefab, friendsListContainer);

        TMP_Text friendText = FindPrimaryLabel(row.transform);
        string status = "🔴 Offline";
        if (photonInfo != null)
            status = photonInfo.IsOnline ? (photonInfo.IsInRoom ? "🎮 In Game" : "🟢 Online") : "🔴 Offline";

        if (friendText != null)
        {
            friendText.text = $"{displayName}\n{status}";
            friendText.color = photonInfo != null && photonInfo.IsOnline ? Color.green : Color.gray;
        }

        Button inviteBtn = FindChildButton(row.transform, "InviteButton");
        if (inviteBtn == null)
        {
            Button[] buttons = row.GetComponentsInChildren<Button>(true);
            inviteBtn = buttons.Length > 0 ? buttons[buttons.Length - 1] : null;
        }

        if (inviteBtn != null)
        {
            inviteBtn.onClick.RemoveAllListeners();
            inviteBtn.onClick.AddListener(() => InviteFriendToGame(friendId, displayName));
            TMP_Text inviteLabel = inviteBtn.GetComponentInChildren<TMP_Text>();
            if (inviteLabel != null) inviteLabel.text = "Invite";
        }
    }

    static TMP_Text FindPrimaryLabel(Transform root)
    {
        TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i].GetComponentInParent<Button>() == null)
                return labels[i];
        }
        return labels.Length > 0 ? labels[0] : null;
    }

    static Button FindChildButton(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName)
                return t.GetComponent<Button>();
        }
        return null;
    }

    public void InviteFriendToGame(string friendUserId, string friendDisplayName = null)
    {
        if (string.IsNullOrEmpty(friendUserId)) return;
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            ShowUIError("Server not ready. Wait for connection...");
            return;
        }

        _pendingInviteFriendId = friendUserId;
        _pendingInviteFriendName = string.IsNullOrEmpty(friendDisplayName)
            ? GetFriendDisplayNameInternal(friendUserId)
            : friendDisplayName;

        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
        {
            TrySendPendingInvite();
            return;
        }

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();

        CreatePrivateRoom();
        ShowUIError("Creating room for invite...");
    }

    void TrySendPendingInvite()
    {
        if (string.IsNullOrEmpty(_pendingInviteFriendId)) return;
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible) return;

        SendFirebaseInvite(_pendingInviteFriendId, PhotonNetwork.CurrentRoom.Name, _pendingInviteFriendName);
        _pendingInviteFriendId = null;
        _pendingInviteFriendName = null;
    }

    void SendFirebaseInvite(string targetUserId, string roomPin, string friendName)
    {
        if (string.IsNullOrEmpty(targetUserId) || string.IsNullOrEmpty(roomPin)) return;

        string fromId = PhotonNetwork.AuthValues?.UserId ?? "";
        string fromName = PhotonNetwork.NickName ?? "Friend";

        var inviteData = new Dictionary<string, object>
        {
            { "roomPin", roomPin },
            { "fromUserId", fromId },
            { "fromName", fromName },
            { "createdAt", System.DateTime.UtcNow.Ticks }
        };

        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
            .Child("invites").Child(targetUserId).Child(roomPin)
            .SetValueAsync(inviteData).ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("[Invite] Firebase send failed: " + task.Exception);
                    ShowUIError("Invite failed. Try again.");
                    return;
                }

                MarkGameInviteSent(targetUserId);
                RefreshFriendsListUI();
                ShowUIError($"Invite sent to {friendName}!");
                Debug.Log($"[Invite] Sent room {roomPin} to {targetUserId}");
            });
    }

    public void StartInviteListener()
    {
        if (_inviteListenerStarted) return;

        string myId = PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId ?? "";
        if (string.IsNullOrEmpty(myId)) return;

        inviteDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("invites").Child(myId);
        inviteDbRef.ChildAdded += OnIncomingInviteAdded;
        _inviteListenerStarted = true;
        Debug.Log("[Invite] Listening for invites on " + myId);

        inviteDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

            DataSnapshot latest = null;
            long latestTicks = 0;
            foreach (DataSnapshot child in task.Result.Children)
            {
                if (!child.Exists) continue;
                long ticks = 0;
                if (child.Child("createdAt").Exists)
                    long.TryParse(child.Child("createdAt").Value?.ToString(), out ticks);
                if (latest == null || ticks >= latestTicks)
                {
                    latest = child;
                    latestTicks = ticks;
                }
            }

            if (latest == null) return;
            string roomPin = latest.Child("roomPin").Value?.ToString();
            string fromName = latest.Child("fromName").Value?.ToString() ?? "Friend";
            if (!string.IsNullOrEmpty(roomPin))
            {
                ShowIncomingInvite(fromName, roomPin);
                latest.Reference.RemoveValueAsync();
            }
        });
    }

    void OnIncomingInviteAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;

        string roomPin = args.Snapshot.Child("roomPin").Value?.ToString();
        string fromName = args.Snapshot.Child("fromName").Value?.ToString() ?? "Friend";
        if (string.IsNullOrEmpty(roomPin)) return;

        ShowIncomingInvite(fromName, roomPin);
        args.Snapshot.Reference.RemoveValueAsync();
    }

    void ShowIncomingInvite(string fromName, string roomPin)
    {
        if (pinInputField != null)
            pinInputField.text = roomPin;

        if (errorText != null)
        {
            errorText.text = $"{fromName} invited you! PIN: {roomPin}";
            errorText.gameObject.SetActive(true);
        }

        if (FriendsDrawerController.Instance != null)
            FriendsDrawerController.Instance.OpenDrawer();

        Debug.Log($"[Invite] Incoming from {fromName} — room {roomPin}");
    }

    void SaveFriends()
    {
        PlayerPrefs.SetString(FriendsPrefsKey, string.Join(",", myFriends));

        var namePairs = new List<string>();
        foreach (string id in myFriends)
        {
            if (friendDisplayNames.TryGetValue(id, out string name))
                namePairs.Add(id + "|" + name);
        }
        PlayerPrefs.SetString(FriendNamesPrefsKey, string.Join(",", namePairs));
        PlayerPrefs.Save();
    }

    void LoadFriends()
    {
        if (myFriends == null) myFriends = new List<string>();
        string data = PlayerPrefs.GetString(FriendsPrefsKey, "");
        myFriends.Clear();
        if (!string.IsNullOrEmpty(data))
        {
            foreach (string id in data.Split(','))
            {
                if (!string.IsNullOrEmpty(id) && !myFriends.Contains(id))
                    myFriends.Add(id);
            }
        }

        friendDisplayNames.Clear();
        string namesData = PlayerPrefs.GetString(FriendNamesPrefsKey, "");
        if (!string.IsNullOrEmpty(namesData))
        {
            foreach (string pair in namesData.Split(','))
            {
                int sep = pair.IndexOf('|');
                if (sep <= 0) continue;
                string id = pair.Substring(0, sep);
                string name = pair.Substring(sep + 1);
                if (!string.IsNullOrEmpty(id))
                    friendDisplayNames[id] = name;
            }
        }
    }
}
