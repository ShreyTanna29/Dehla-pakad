// using System;
// using System.Collections;
// using UnityEngine;
// using UnityEngine.UI;
// using Photon.Pun;
// using Photon.Realtime;
// using TMPro;
// using System.Collections.Generic;
// using Firebase.Database;
// using Firebase.Extensions;
// using Firebase.Auth;
// using DG.Tweening;

// public class PlayWithFriendsManager : MonoBehaviourPunCallbacks
// {
//     public static PlayWithFriendsManager Instance;

//     /// <summary>PIN queued while Photon is still connecting (invite accept / join table).</summary>
//     public static string PendingJoinPin { get; set; }

//     [Header("PIN UI Components")]
//     public TMP_InputField pinInputField;
//     public TMP_Text generatedPinText;
//     public GameObject pinCreationPanel;
//     public TMP_Text errorText;

//     [Header("Lobby Buttons & Panels")]
//     public GameObject startGameButton;
//     public GameObject modesPanel;
//     public TMP_Text clientWaitingText;
//     [Tooltip("Optional spinner icon shown beside the waiting label (auto-created if empty).")]
//     public RectTransform clientWaitingSpinner;
//     [Tooltip("Font size for 'Waiting for Host...' on joining clients.")]
//     [SerializeField] float clientWaitingFontSize = 34f;

//     Tween _waitingSpinnerTween;

//     [Header("Online Matchmaking (shared seat panel)")]
//     [Tooltip("Countdown / status text shown only while this panel is used as the online matchmaking lobby.")]
//     public TMP_Text matchmakingTimerText;
//     [Tooltip("Wooden plaque holding the timer text (shown only in online matchmaking mode).")]
//     public GameObject matchmakingTimerPlaque;
//     // When true the seat panel acts as the ONLINE matchmaking lobby (public room):
//     // timer is shown, PIN / Create / manual Start / Bots controls are hidden, and the
//     // match auto-starts (driven by DeckManager) once the table fills or the timer ends.
//     bool _onlineMode;
//     public bool IsOnlineMode => _onlineMode;

//     [Header("Live Player List UI")]
//     public TMP_Text[] playerSlotsText;
//     [Tooltip("Avatar Image under each chair, parallel index to playerSlotsText.")]
//     public UnityEngine.UI.Image[] playerSlotsAvatar;

//     [Header("Room Creation / PIN Display")]
//     [Tooltip("CREATE ROOM button shown until the private room exists.")]
//     public GameObject createRoomButton;
//     [Tooltip("Wooden plaque holding the ROOM ID text, shown once the room exists.")]
//     public GameObject roomIdPlaque;

//     [Header("Toggle Bot Settings")]
//     public GameObject includeBotsButton;
//     public TMP_Text includeBotsBtnText;
//     bool areBotsIncluded;

//     [Header("Game Table UI")]
//     public GameObject homeMenuPanel;
//     public GameObject gameTablePanel;

//     [Header("Friends UI Slots")]
//     public TMP_Text myUserIdText;
//     public TMP_InputField addFriendInput;
//     public Transform friendsListContainer;
//     public GameObject friendUIPrefab;

//     [Header("Friend Requests UI")]
//     [Tooltip("Prefab for an incoming friend request row (must contain AcceptButton and DeclineButton).")]
//     public GameObject friendRequestRowPrefab;

//     // Incoming friend requests: fromUserId -> fromName
//     readonly Dictionary<string, string> incomingRequests = new Dictionary<string, string>();
//     DatabaseReference requestDbRef;
//     DatabaseReference acceptDbRef;
//     bool _requestListenerStarted;
//     bool _acceptListenerStarted;

//     [Header("Friends List Storage")]
//     private const string FriendsPrefsKey = "SavedFriendsList";
//     private const string FriendNamesPrefsKey = "SavedFriendsNames";
//     private const string FirebaseDatabaseUrl = "https://dehlapakad-c207c-default-rtdb.firebaseio.com/";
//     public List<string> myFriends = new List<string>();
//     readonly Dictionary<string, string> friendDisplayNames = new Dictionary<string, string>();
//     readonly Dictionary<string, FriendInfo> friendPhotonStatus = new Dictionary<string, FriendInfo>();
//     readonly Dictionary<string, long> friendFirebaseLastActiveMs = new Dictionary<string, long>();
//     readonly Dictionary<string, bool> friendFirebaseOnlineFlag = new Dictionary<string, bool>();
//     readonly Dictionary<string, (DatabaseReference Ref, EventHandler<ValueChangedEventArgs> Handler)> _presenceListeners =
//         new Dictionary<string, (DatabaseReference, EventHandler<ValueChangedEventArgs>)>();
//     readonly Dictionary<string, PendingGameInvite> _pendingGameInvites = new Dictionary<string, PendingGameInvite>();
//     Coroutine _presenceHeartbeatCoroutine;
//     const long FirebaseOnlineThresholdMs = 120_000;

//     struct PendingGameInvite
//     {
//         public string InviteId;
//         public string RoomPin;
//         public string FromName;
//         public string FromUserId;
//     }
//     PhotonView _photonView;
//     DatabaseReference inviteDbRef;
//     const long InviteExpirySeconds = 15;
//     string _pendingInviteFriendId;
//     string _pendingInviteFriendName;
//     bool _inviteListenerStarted;
//     string _listenersBoundUserId;
//     readonly HashSet<string> _gameInvitesSent = new HashSet<string>();
//     bool _pendingCreatePrivateRoom;
//     bool _isLeavingFriendsFlow;

//     public static bool IsFriendsPrivateRoomCreatePending()
//     {
//         return Instance != null
//             && Instance._pendingCreatePrivateRoom
//             && !Instance._isLeavingFriendsFlow;
//     }

//     /// <summary>User backed out of PlayFriends — abort eager room create and block ghost lobby UI.</summary>
//     public void AbortPendingFriendsRoomCreation()
//     {
//         _isLeavingFriendsFlow = true;
//         _pendingCreatePrivateRoom = false;
//         _pendingSeatLobbyOpen = false;
//         _creatingPrivateRoom = false;
//         SuppressSeatLobbyOnJoin = false;

//         if (_createRoomCoroutine != null)
//         {
//             StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
//         }

//         Debug.Log("[Friends] Pending room creation aborted (back / leave).");
//     }

//     public void BeginFriendsFlow()
//     {
//         _isLeavingFriendsFlow = false;
//     }

//     public bool IsLeavingFriendsFlow => _isLeavingFriendsFlow;

//     public void TryFlushPendingPrivateRoomCreate()
//     {
//         if (_isLeavingFriendsFlow || !_pendingCreatePrivateRoom || PhotonNetwork.InRoom) return;
//         if (!NetworkManager.IsPhotonMasterReadyForRooms()) return;

//         if (_createRoomCoroutine != null)
//         {
//             StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
//         }

//         _pendingCreatePrivateRoom = false;
//         if (errorText != null) errorText.gameObject.SetActive(false);
//         DoCreatePrivateRoom();
//     }

//     /// <summary>Queue private-room create after leaving a public online room.</summary>
//     public void RequestPrivateRoomCreateAfterLeave()
//     {
//         BeginFriendsFlow();
//         _pendingCreatePrivateRoom = true;
//         SuppressSeatLobbyOnJoin = true;
//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.MarkReturnToFriendsModesAfterLeave();
//         Debug.Log("[Friends] Private room create queued after leave.");
//     }

//     /// <summary>Clears online-only seat panel state without hiding the friends panel.</summary>
//     public void ClearOnlineModeOnly()
//     {
//         _onlineMode = false;
//         _previewBotsInOnlineLobby = false;
//         ApplyModeControls(false);
//         if (matchmakingTimerText != null)
//             matchmakingTimerText.text = string.Empty;
//     }
//     bool _joinInProgress;
//     bool _handlingJoinFailure;
//     int _joinAttemptToken;
//     Coroutine _joinTimeoutCoroutine;
//     MonoBehaviour _joinTimeoutRunner;
//     JoinTablePanelController _joinTableController;
//     Coroutine _lobbyPlayerRefreshCoroutine;
//     MonoBehaviour _lobbyPlayerRefreshRunner;

//     // BUG1 (instant invites): true while a private room is created EAGERLY on entering the
//     // friends flow (before the host taps Play) so invites can be sent immediately. While set,
//     // join-time handlers must NOT pull the host out of the Modes panel into the seat lobby.
//     // Cleared when the host opens the seat panel (taps Play) or leaves the private room.
//     public bool SuppressSeatLobbyOnJoin;

//     Coroutine _createRoomCoroutine;
//     MonoBehaviour _createRoomRunner;
//     Coroutine _retryFriendServicesCoroutine;
//     Coroutine _findFriendsCoroutine;
//     Coroutine _smoothGameStartCoroutine;
//     bool _firebaseAuthHooked;
//     bool _friendsGameStartTriggered;
//     bool _hostConfirmedSeatStart;
//     bool _pendingSeatLobbyOpen;
//     bool _isLeavingRoom;

//     // Runtime-created "INVITE FRIENDS" button shown on the friends seat/lobby panel.
//     GameObject _lobbyInviteButton;

//     // PIN / private-room creation reliability: track that WE are creating a private room so
//     // OnCreateRoomFailed can retry with a fresh PIN (e.g. a rare 5-digit PIN collision).
//     bool _creatingPrivateRoom;
//     int _createRoomRetries;
//     const int MaxCreateRoomRetries = 5;

//     public IReadOnlyList<string> MyFriends => myFriends;
//     public bool IsJoinInProgress => _joinInProgress;
//     public IReadOnlyDictionary<string, string> IncomingRequests => incomingRequests;

//     /// <summary>Fires whenever the incoming friend-request list changes (added/removed/accepted/declined).
//     /// In-game panels subscribe to live-refresh their Accept/Decline rows.</summary>
//     public event System.Action RequestsChanged;
//     void NotifyRequestsChanged() => RequestsChanged?.Invoke();

//     /// <summary>TASK 18/25: fires whenever a friend's online/in-game presence changes (Firebase
//     /// presence ValueChanged or a status re-poll). Open in-game friend panels subscribe to this so
//     /// they repaint with the correct Online/Offline state once the async presence read completes —
//     /// otherwise rows built synchronously on panel-open show everyone as "Offline".</summary>
//     public event System.Action FriendsStatusChanged;
//     void NotifyFriendsStatusChanged() => FriendsStatusChanged?.Invoke();

//     public string GetFriendDisplayName(string friendId) => GetFriendDisplayNameInternal(friendId);

//     /// <summary>Firebase account id used for friend requests / invites (same as MyUserId).</summary>
//     public string GetAccountUserId() => MyUserId;

//     public FriendInfo GetFriendPhotonInfo(string friendId) =>
//         friendPhotonStatus.TryGetValue(friendId, out FriendInfo info) ? info : null;

//     /// <summary>Online when Photon reports it, or Firebase presence was updated recently (works in-room).</summary>
//     public bool IsFriendOnline(string friendId)
//     {
//         if (string.IsNullOrEmpty(friendId)) return false;

//         if (friendPhotonStatus.TryGetValue(friendId, out FriendInfo photonInfo) && photonInfo != null && photonInfo.IsOnline)
//             return true;

//         if (friendFirebaseOnlineFlag.TryGetValue(friendId, out bool firebaseOnline) && firebaseOnline)
//             return true;

//         if (friendFirebaseLastActiveMs.TryGetValue(friendId, out long lastMs) && lastMs > 0)
//         {
//             long age = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastMs;
//             return age >= 0 && age <= FirebaseOnlineThresholdMs;
//         }

//         return false;
//     }

//     public bool IsFriendInGame(string friendId)
//     {
//         if (friendPhotonStatus.TryGetValue(friendId, out FriendInfo info) && info != null)
//             return info.IsOnline && info.IsInRoom;
//         return false;
//     }

//     public void MarkGameInviteSent(string friendUserId)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;
//         _gameInvitesSent.Add(friendUserId);
//     }

//     /// <summary>
//     /// Live friend presence sync: attaches Firebase ValueChanged listeners per friend, publishes
//     /// our own heartbeat, polls Photon FindFriends when on the master server, and repaints UI.
//     /// </summary>
//     public void SyncFriendStatus()
//     {
//         EnsurePhotonUserId();
//         PublishOwnPresence();

//         if (myFriends == null || myFriends.Count == 0)
//         {
//             TearDownPresenceListeners();
//             RefreshFriendsListUI();
//             return;
//         }

//         TearDownPresenceListeners();

//         DatabaseReference root = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference;
//         foreach (string friendId in myFriends)
//         {
//             if (string.IsNullOrEmpty(friendId)) continue;

//             string capturedId = friendId;
//             DatabaseReference presenceRef = root.Child("users").Child(capturedId).Child("presence");

//             EventHandler<ValueChangedEventArgs> handler = (_, args) => OnFriendPresenceChanged(capturedId, args);
//             presenceRef.ValueChanged += handler;
//             _presenceListeners[capturedId] = (presenceRef, handler);

//             presenceRef.GetValueAsync().ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted || task.Result == null) return;
//                 ApplyPresenceSnapshot(capturedId, task.Result);
//                 RefreshFriendsListUI();
//             });
//         }

//         if (CanCallFindFriends())
//             PhotonNetwork.FindFriends(myFriends.ToArray());
//         else if (!PhotonNetwork.InRoom)
//             ScheduleFindFriendsWhenReady();

//         RefreshFriendsListUI();
//     }

//     public void RefreshFriendsStatus() => SyncFriendStatus();

//     public void CheckFriendsOnlineStatus() => SyncFriendStatus();

//     void OnFriendPresenceChanged(string friendId, ValueChangedEventArgs args)
//     {
//         if (args.DatabaseError != null) return;
//         ApplyPresenceSnapshot(friendId, args.Snapshot);
//         RefreshFriendsListUI();
//     }

//     void ApplyPresenceSnapshot(string friendId, DataSnapshot snapshot)
//     {
//         if (string.IsNullOrEmpty(friendId)) return;

//         if (snapshot == null || !snapshot.Exists)
//         {
//             friendFirebaseOnlineFlag[friendId] = false;
//             friendFirebaseLastActiveMs.Remove(friendId);
//             return;
//         }

//         if (snapshot.Child("online").Exists)
//         {
//             object onlineVal = snapshot.Child("online").Value;
//             bool online = onlineVal is bool b && b
//                 || (onlineVal != null && onlineVal.ToString().Equals("true", System.StringComparison.OrdinalIgnoreCase));
//             friendFirebaseOnlineFlag[friendId] = online;
//         }

//         if (snapshot.Child("lastActive").Exists
//             && long.TryParse(snapshot.Child("lastActive").Value?.ToString(), out long lastMs))
//         {
//             friendFirebaseLastActiveMs[friendId] = lastMs;
//         }
//     }

//     void TearDownPresenceListeners()
//     {
//         foreach (var entry in _presenceListeners)
//         {
//             if (entry.Value.Ref != null && entry.Value.Handler != null)
//                 entry.Value.Ref.ValueChanged -= entry.Value.Handler;
//         }
//         _presenceListeners.Clear();
//     }

//     /// <summary>
//     /// Tasks 9/18/25 — Public entry the friends UI invite button should call. Wraps
//     /// InviteFriendToGame and relies on SendFirebaseInvite to mark the invite "sent" ONLY in its
//     /// success callback — so a failed invite no longer permanently greys out the button.
//     /// </summary>
//     public void SendGameInvite(string friendId)
//     {
//         if (string.IsNullOrEmpty(friendId)) return;
//         InviteFriendToGame(friendId, GetFriendDisplayNameInternal(friendId));
//     }

//     public bool IsGameInviteSent(string friendUserId) =>
//         !string.IsNullOrEmpty(friendUserId) && _gameInvitesSent.Contains(friendUserId);

//     void Awake()
//     {
//         if (Instance == null) Instance = this;
//         else if (Instance != this)
//         {
//             Destroy(this);
//             return;
//         }

//         LoadFriends();
//         if (myFriends == null) myFriends = new List<string>();
//         EnsurePhotonUserId();
//         EnsureNickname();
//         EnsurePhotonView();
//         PhotonNetwork.AddCallbackTarget(this);
//         TryHookFirebaseAuth();
//     }

//     public override void OnEnable()
//     {
//         base.OnEnable();
//         TryHookFirebaseAuth();
//     }

//     public override void OnDisable()
//     {
//         base.OnDisable();
//         UnhookFirebaseAuth();
//         if (_retryFriendServicesCoroutine != null)
//         {
//             StopCoroutine(_retryFriendServicesCoroutine);
//             _retryFriendServicesCoroutine = null;
//         }
//     }

//     void TryHookFirebaseAuth()
//     {
//         if (_firebaseAuthHooked) return;
//         if (FirebaseAuth.DefaultInstance == null) return;

//         FirebaseAuth.DefaultInstance.StateChanged += OnFirebaseAuthStateChanged;
//         _firebaseAuthHooked = true;

//         if (FirebaseAuth.DefaultInstance.CurrentUser != null)
//             EnsureFriendServicesStarted();
//     }

//     void UnhookFirebaseAuth()
//     {
//         if (!_firebaseAuthHooked || FirebaseAuth.DefaultInstance == null) return;
//         FirebaseAuth.DefaultInstance.StateChanged -= OnFirebaseAuthStateChanged;
//         _firebaseAuthHooked = false;
//     }

//     void OnFirebaseAuthStateChanged(object sender, System.EventArgs e)
//     {
//         if (FirebaseAuth.DefaultInstance?.CurrentUser != null)
//         {
//             EnsurePhotonUserId();
//             EnsureFriendServicesStarted();
//         }
//     }

//     void EnsureNickname()
//     {
//         string profileName = PlayerPrefs.GetString("PlayerUsername", string.Empty).Trim();
//         if (!string.IsNullOrEmpty(profileName))
//         {
//             if (PhotonNetwork.NickName != profileName)
//                 PhotonNetwork.NickName = profileName;
//             return;
//         }

//         if (string.IsNullOrEmpty(PhotonNetwork.NickName))
//         {
//             PhotonNetwork.NickName = "Player_" + UnityEngine.Random.Range(100, 999);
//             Debug.Log("My Random Name Set To: " + PhotonNetwork.NickName);
//         }
//     }

//     public void EnsureNicknamePublic() => EnsureNickname();

//     void EnsurePhotonView()
//     {
//         if (_photonView == null)
//             _photonView = GetComponent<PhotonView>();
//     }

//     /// <summary>
//     /// PlayWithFriendsPanel is often inactive, so its scene PhotonView may stay at ViewID 0.
//     /// Route friend-lobby RPCs through DeckManager's always-active scene view instead.
//     /// </summary>
//     static PhotonView GetReliableRpcView()
//     {
//         if (DeckManager.Instance != null)
//         {
//             PhotonView deckPv = DeckManager.Instance.photonView;
//             if (deckPv != null && deckPv.ViewID > 0)
//                 return deckPv;
//         }

//         PlayWithFriendsManager mgr = Instance != null ? Instance : ResolveManagerInstance();
//         if (mgr == null) return null;

//         PhotonView localPv = mgr.photonView;
//         if (localPv == null) return null;
//         if (localPv.ViewID > 0) return localPv;

//         if (localPv.sceneViewId > 0)
//         {
//             localPv.ViewID = localPv.sceneViewId;
//             if (localPv.ViewID > 0)
//                 return localPv;
//         }

//         return null;
//     }

//     static PlayWithFriendsManager ResolveManagerInstance()
//     {
//         var all = Resources.FindObjectsOfTypeAll<PlayWithFriendsManager>();
//         foreach (var m in all)
//         {
//             if (m == null || !m.gameObject.scene.IsValid()) continue;
//             return m;
//         }
//         return null;
//     }

//     void SendFriendsRpc(string methodName, RpcTarget target)
//     {
//         PhotonView rpcView = GetReliableRpcView();
//         if (rpcView == null || rpcView.ViewID < 1)
//         {
//             Debug.LogError($"[Friends] Cannot send {methodName}: no valid PhotonView (panel view id is 0).");
//             return;
//         }

//         rpcView.RPC(methodName, target);
//     }

//     void OnDestroy()
//     {
//         UnhookFirebaseAuth();
//         PhotonNetwork.RemoveCallbackTarget(this);

//         if (requestDbRef != null)
//         {
//             requestDbRef.ChildAdded -= OnFriendRequestAdded;
//             requestDbRef.ChildRemoved -= OnFriendRequestRemoved;
//         }
//         if (acceptDbRef != null)
//             acceptDbRef.ChildAdded -= OnFriendAcceptAdded;
//         if (inviteDbRef != null)
//             inviteDbRef.ChildAdded -= OnIncomingInviteAdded;

//         TearDownPresenceListeners();
//         _waitingSpinnerTween?.Kill();
//         if (Instance == this) Instance = null;
//     }

//     void Start()
//     {
//         if (errorText != null) errorText.gameObject.SetActive(false);
//         HideClientWaitingPresentation();
//         if (includeBotsButton != null) includeBotsButton.SetActive(false);

//         if (_onlineMode)
//         {
//             ShowLocalPlayerInOnlineMatchmaking();
//             return;
//         }

//         ClearPlayerListUI();
//         EnsureFriendServicesStarted();

//         // If this panel was activated as the online matchmaking lobby, do not touch the
//         // friends-only Start button — ShowOnlineMatchmakingLobby() already configured it.
//         if (_onlineMode) return;

//         // New flow: the Start button is always visible on the seat panel but stays
//         // greyed/disabled until the table is full. Re-apply correct state for any room.
//         if (startGameButton != null)
//         {
//             startGameButton.SetActive(true);
//             SetStartButtonInteractable(false);
//         }
//         CheckPlayerCountAndToggleStart();
//     }

//     /// <summary>Call after login / Photon ready so Firebase listeners use the real user id.</summary>
//     public void EnsureFriendServicesStarted()
//     {
//         EnsurePhotonUserId();

//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId))
//         {
//             // StartCoroutine throws if this panel's GameObject is inactive (headless boot).
//             // In that case SocialServiceBootstrap drives the retry instead.
//             if (isActiveAndEnabled && _retryFriendServicesCoroutine == null)
//                 _retryFriendServicesCoroutine = StartCoroutine(RetryFriendServicesWhenReady());
//             return;
//         }

//         if (_retryFriendServicesCoroutine != null)
//         {
//             StopCoroutine(_retryFriendServicesCoroutine);
//             _retryFriendServicesCoroutine = null;
//         }

//         if (_listenersBoundUserId != myId)
//         {
//             StopFriendListeners();
//             _listenersBoundUserId = myId;
//         }

//         DisplayMyID();
//         StartFriendRequestListener();
//         StartFriendAcceptListener();
//         StartInviteListener();
//         StartPresenceHeartbeat();
//         SyncFriendStatus();

//         // Phase 5 — pull the persisted friends list from Firebase right after login. Guarded so
//         // it runs once per signed-in user, but re-runs if the account changes.
//         if (Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser != null && _friendsLoadedForUser != myId)
//         {
//             _friendsLoadedForUser = myId;
//             LoadFriendsFromFirebase();
//         }
//     }

//     bool _headlessFriendsLoaded;

//     /// <summary>
//     /// Brings the Firebase friend-request / accept / invite listeners online even though this
//     /// panel's GameObject is INACTIVE on the home screen. Without this, a player who never opens
//     /// the matchmaking / play-with-friends panel never binds the listeners, so incoming friend
//     /// requests and game invites are silently dropped (and the manager's Instance stays null, so
//     /// the "Add Friend" buttons do nothing / throw). Called by SocialServiceBootstrap; safe to
//     /// call repeatedly — every internal bind is guarded.
//     /// </summary>
//     public void StartSocialServicesHeadless()
//     {
//         if (Instance == null) Instance = this;

//         if (!_headlessFriendsLoaded)
//         {
//             LoadFriends();
//             if (myFriends == null) myFriends = new List<string>();
//             _headlessFriendsLoaded = true;
//         }

//         // The panel GameObject is INACTIVE on the home screen, so MonoBehaviourPunCallbacks'
//         // OnEnable (which registers Photon callbacks) never runs. Register them here so callbacks
//         // like OnConnectedToMaster / OnJoinedRoom fire for the eager private-room creation even
//         // before the player opens the seat panel. Also pre-set the nickname / view headless so
//         // the player's own name shows the moment the room is created.
//         EnsurePhotonCallbacks();
//         EnsurePhotonView();
//         EnsureNickname();

//         // Subscribe to Firebase auth so the listeners rebind to the real account id once the
//         // user finishes signing in (the normal Awake/OnEnable hook never runs while inactive).
//         TryHookFirebaseAuth();
//         EnsureFriendServicesStarted();
//     }

//     /// <summary>
//     /// Registers this manager as a Photon callback target. Safe to call repeatedly (PUN dedupes
//     /// by target). Needed because the panel is often inactive, so the base OnEnable registration
//     /// does not run on the home screen.
//     /// </summary>
//     void EnsurePhotonCallbacks() => PhotonNetwork.AddCallbackTarget(this);

//     IEnumerator RetryFriendServicesWhenReady()
//     {
//         const int maxAttempts = 30;
//         for (int i = 0; i < maxAttempts; i++)
//         {
//             yield return new WaitForSeconds(0.5f);
//             if (FirebaseAuth.DefaultInstance?.CurrentUser != null || !string.IsNullOrEmpty(MyUserId))
//             {
//                 _retryFriendServicesCoroutine = null;
//                 EnsureFriendServicesStarted();
//                 yield break;
//             }
//         }

//         _retryFriendServicesCoroutine = null;
//         Debug.LogWarning("[Friends] Could not start friend services — no Firebase user id yet.");
//     }

//     void StopFriendListeners()
//     {
//         if (requestDbRef != null)
//         {
//             requestDbRef.ChildAdded -= OnFriendRequestAdded;
//             requestDbRef.ChildRemoved -= OnFriendRequestRemoved;
//             requestDbRef = null;
//         }
//         if (acceptDbRef != null)
//         {
//             acceptDbRef.ChildAdded -= OnFriendAcceptAdded;
//             acceptDbRef = null;
//         }
//         if (inviteDbRef != null)
//         {
//             inviteDbRef.ChildAdded -= OnIncomingInviteAdded;
//             inviteDbRef = null;
//         }

//         _requestListenerStarted = false;
//         _acceptListenerStarted = false;
//         _inviteListenerStarted = false;
//         incomingRequests.Clear();
//     }

//     void EnsurePhotonUserId()
//     {
//         if (PhotonNetwork.AuthValues == null)
//             PhotonNetwork.AuthValues = new AuthenticationValues();

//         string firebaseUid = FirebaseAuth.DefaultInstance?.CurrentUser?.UserId;
//         if (!string.IsNullOrEmpty(firebaseUid))
//         {
//             if (PhotonNetwork.AuthValues.UserId != firebaseUid)
//             {
//                 PhotonNetwork.AuthValues.UserId = firebaseUid;
//                 PlayerPrefs.SetString("PhotonUserId", firebaseUid);
//                 PlayerPrefs.Save();
//             }
//             return;
//         }

//         if (string.IsNullOrEmpty(PhotonNetwork.AuthValues.UserId))
//         {
//             string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
//             PlayerPrefs.SetString("PhotonUserId", uid);
//             PlayerPrefs.Save();
//             PhotonNetwork.AuthValues.UserId = uid;
//         }
//     }

//     // ==========================================
//     // 1. HOST: CREATE PRIVATE ROOM (modes later)
//     // ==========================================

//     public void CreatePrivateRoom()
//     {
//         if (errorText != null) errorText.gameObject.SetActive(false);

//         // The panel may be inactive (eager create from the Modes screen). Make sure our Photon
//         // callbacks are registered so OnConnectedToMaster fires and creates the room once the
//         // cold connection completes — otherwise the very first attempt silently does nothing.
//         EnsurePhotonCallbacks();

//         if (PhotonNetwork.InRoom)
//         {
//             ShowUIError("Leave the current room first.");
//             return;
//         }

//         if (NetworkManager.IsPhotonMasterReadyForRooms())
//         {
//             _pendingCreatePrivateRoom = false;
//             StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
//             DoCreatePrivateRoom();
//             return;
//         }

//         if (PhotonNetwork.IsConnectedAndReady)
//         {
//             _pendingCreatePrivateRoom = true;
//             TryFlushPendingPrivateRoomCreate();
//             if (PhotonNetwork.InRoom || !_pendingCreatePrivateRoom)
//                 return;
//         }

//         if (!NetworkManager.HasInternet())
//         {
//             ShowUIError("No internet connection.");
//             return;
//         }

//         _pendingCreatePrivateRoom = true;

//         if (NetworkManager.IsPhotonConnectingOrConnected())
//             ShowUIError("Connecting... please wait.");
//         else
//         {
//             ShowUIError("Connecting to server...");
//             if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.ConnectToPhoton();
//         }

//         // The poll-and-create coroutine can only run on an ACTIVE GameObject. When the panel is
//         // inactive (eager create from the Modes screen) we use NetworkManager as the runner.
//         StartFriendsCoroutine(WaitAndCreatePrivateRoomRoutine(), ref _createRoomCoroutine, ref _createRoomRunner);
//     }

//     void DoCreatePrivateRoom()
//     {
//         if (PhotonNetwork.InRoom) return;

//         if (!NetworkManager.IsPhotonMasterReadyForRooms())
//         {
//             _pendingCreatePrivateRoom = true;
//             Debug.Log("[Friends] CreateRoom deferred — Photon not ready on Master (e.g. JoiningLobby).");
//             return;
//         }

//         // Fresh PIN every time a room is created. Always 5 digits (10000-99999) so the leading
//         // digit is never 0 and the PIN is easy to read / type.
//         string newPin = GenerateRoomPin();
//         Debug.Log("Generating PIN: " + newPin);

//         _creatingPrivateRoom = true;

//         RoomOptions roomOptions = new RoomOptions
//         {
//             MaxPlayers = 4,
//             IsVisible = false,
//             IsOpen = true,
//             // Required so players can read each other's account id (Player.UserId) in-game,
//             // used by the friend / stats popup.
//             PublishUserId = true
//         };

//         PhotonNetwork.CreateRoom(newPin, roomOptions);
//         Debug.Log("[Friends] Room created with PIN: " + newPin);
//     }

//     /// <summary>Generates a fresh, easy-to-read 5-digit room PIN.</summary>
//     static string GenerateRoomPin() => UnityEngine.Random.Range(10000, 100000).ToString();

//     IEnumerator WaitAndCreatePrivateRoomRoutine()
//     {
//         float timeout = 20f;
//         while (timeout > 0f && _pendingCreatePrivateRoom)
//         {
//             if (NetworkManager.IsPhotonMasterReadyForRooms() && !PhotonNetwork.InRoom)
//             {
//                 _pendingCreatePrivateRoom = false;
//                 _createRoomCoroutine = null;
//                 if (errorText != null) errorText.gameObject.SetActive(false);
//                 DoCreatePrivateRoom();
//                 yield break;
//             }

//             if (!NetworkManager.IsPhotonConnectingOrConnected() && NetworkManager.HasInternet()
//                 && NetworkManager.Instance != null)
//             {
//                 NetworkManager.Instance.ConnectToPhoton();
//             }

//             yield return new WaitForSeconds(0.25f);
//             timeout -= 0.25f;
//         }

//         _pendingCreatePrivateRoom = false;
//         _createRoomCoroutine = null;
//         if (!PhotonNetwork.IsConnectedAndReady)
//             ShowUIError("Could not connect. Try again.");
//     }

//     public override void OnConnectedToMaster()
//     {
//         TryFlushPendingPrivateRoomCreate();
//         TryFlushPendingJoin();
//         CheckFriendsOnlineStatus();
//     }

//     public override void OnJoinedLobby()
//     {
//         TryFlushPendingPrivateRoomCreate();
//         TryFlushPendingJoin();
//         CheckFriendsOnlineStatus();
//     }

//     public void TryFlushPendingJoin()
//     {
//         if (!string.IsNullOrEmpty(PendingJoinPin) && !PhotonNetwork.InRoom)
//         {
//             if (!NetworkManager.IsPhotonMasterReadyForRooms())
//             {
//                 Debug.Log("[Friends] Client not ready yet (JoiningLobby). Deferring PIN join.");
//                 return;
//             }

//             string pin = PendingJoinPin;
//             PendingJoinPin = null;

//             Debug.Log($"[Friends] Photon ready — joining queued room '{pin}'");

//             if (!UiFlowManager.IsPlayFriendsJoinFlow())
//                 _joinAttemptToken = UiFlowManager.BeginPinJoinAttempt();

//             if (ModeManager.Instance != null)
//                 ModeManager.Instance.MarkFriendsPinJoinFlow();

//             if (!_joinInProgress)
//             {
//                 _joinInProgress = true;
//                 SetJoinButtonInteractable(false);
//                 StartJoinTimeout();
//                 if (NetworkManager.Instance != null)
//                     NetworkManager.Instance.CancelPinJoinUiOverlays();
//             }

//             if (!PhotonNetwork.JoinRoom(pin))
//                 RestoreJoinPanelAfterFailedJoin(0, "JoinRoom rejected");
//         }
//     }

//     void CacheJoinTableController()
//     {
//         if (_joinTableController == null)
//             _joinTableController = FindAnyObjectByType<JoinTablePanelController>();
//     }

//     public void SetJoinButtonInteractable(bool interactable)
//     {
//         CacheJoinTableController();
//         if (_joinTableController != null)
//             _joinTableController.SetJoinInteractable(interactable);
//         if (pinInputField != null)
//             pinInputField.interactable = interactable;
//     }

//     void StartJoinTimeout()
//     {
//         StartFriendsCoroutine(JoinTimeoutRoutine(), ref _joinTimeoutCoroutine, ref _joinTimeoutRunner);
//     }

//     void StopJoinTimeout()
//     {
//         StopFriendsCoroutineSlot(ref _joinTimeoutCoroutine, ref _joinTimeoutRunner);
//     }

//     IEnumerator JoinTimeoutRoutine()
//     {
//         yield return new WaitForSecondsRealtime(10f);
//         _joinTimeoutCoroutine = null;
//         if (!_joinInProgress) yield break;

//         // GLITCH FIX: if a match started while this timeout was pending, don't force the Join Table
//         // / Modes panels open over active play.
//         if (GameFlowState.IsActivelyPlaying)
//         {
//             Debug.Log("[Friends] Join timeout ignored — match actively in progress.");
//             yield break;
//         }

//         Debug.LogWarning("[Friends] PIN join timed out — restoring Join Table.");
//         RestoreJoinPanelAfterFailedJoin(0, "Join timed out. Try again.");
//     }

//     // ==========================================
//     // 2. CLIENT: JOIN ROOM WITH PIN
//     // ==========================================

//     public void JoinRoomWithPIN()
//     {
//         if (_joinInProgress) return;

//         Debug.Log("[Friends] Joining room by PIN");
//         if (errorText != null) errorText.gameObject.SetActive(false);

//         if (pinInputField == null || string.IsNullOrEmpty(pinInputField.text))
//         {
//             ShowUIError("Enter valid PIN!");
//             return;
//         }

//         BeginPinJoin(pinInputField.text.Trim());
//     }

//     /// <summary>
//     /// Joins a private room using a PIN supplied directly (used by the in-Modes JOIN TABLE panel,
//     /// which has its own input field separate from the Play-with-Friends panel).
//     /// </summary>
//     public void JoinRoomWithPINText(string pin)
//     {
//         if (_joinInProgress)
//         {
//             Debug.Log("[Friends] Join PIN ignored — isJoiningRoom=true");
//             return;
//         }

//         if (errorText != null) errorText.gameObject.SetActive(false);

//         if (string.IsNullOrEmpty(pin) || string.IsNullOrWhiteSpace(pin))
//         {
//             ShowUIError("Enter valid PIN!");
//             return;
//         }

//         string trimmed = pin.Trim();
//         Debug.Log($"[Friends] Join PIN clicked | pin='{trimmed}' | isJoiningRoom={_joinInProgress}");
//         BeginPinJoin(trimmed);
//     }

//     void BeginPinJoin(string targetPin)
//     {
//         Debug.Log($"[Friends] JoinRoom requested | room='{targetPin}' | isJoiningRoom={_joinInProgress}");

//         _onlineMode = false;
//         _previewBotsInOnlineLobby = false;
//         if (MatchmakingManager.Instance != null)
//         {
//             MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: false);
//             MatchmakingManager.Instance.HideMatchmakingPanel();
//         }

//         _joinAttemptToken = UiFlowManager.BeginPinJoinAttempt();

//         // A fresh, user-initiated PIN join is NOT a disconnect rejoin. Clear any stale rejoin
//         // state so a wrong-PIN failure routes to the JoinTable restore path, not the rejoin path.
//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ClearRejoinState();

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.MarkFriendsPinJoinFlow();

//         _joinInProgress = true;
//         SetJoinButtonInteractable(false);
//         StartJoinTimeout();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.CancelPinJoinUiOverlays();

//         if (!PhotonNetwork.IsConnectedAndReady)
//         {
//             if (!NetworkManager.HasInternet())
//             {
//                 RestoreJoinPanelAfterFailedJoin(0, "No internet connection.");
//                 return;
//             }
//             PendingJoinPin = targetPin;
//             if (NetworkManager.Instance != null) NetworkManager.Instance.ConnectToPhoton();
//             return;
//         }

//         if (PhotonNetwork.InRoom)
//         {
//             if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.Name == targetPin)
//             {
//                 _joinInProgress = false;
//                 StopJoinTimeout();
//                 SetJoinButtonInteractable(true);
//                 if (NetworkManager.Instance != null)
//                     NetworkManager.Instance.CancelPinJoinUiOverlays();
//                 ShowPrivateRoomLobbyUI();
//                 return;
//             }
//             SuppressSeatLobbyOnJoin = false;
//             PendingJoinPin = targetPin;
//             PhotonNetwork.LeaveRoom();
//             return;
//         }

//         if (!NetworkManager.IsPhotonMasterReadyForRooms())
//         {
//             PendingJoinPin = targetPin;
//             return;
//         }

//         PendingJoinPin = null;
//         Debug.Log($"[Friends] Photon JoinRoom requested for '{targetPin}'");
//         if (!PhotonNetwork.JoinRoom(targetPin))
//             RestoreJoinPanelAfterFailedJoin(0, "JoinRoom rejected");
//     }

//     void RestoreJoinPanelAfterFailedJoin(short returnCode, string message)
//     {
//         if (_handlingJoinFailure) return;
//         _handlingJoinFailure = true;

//         StopJoinTimeout();

//         Debug.Log($"[Friends] RestoreJoinPanelAfterFailedJoin | code={returnCode} | {message}");

//         EmergencyUnlockUI();

//         GameFlowState.SetPhase(GameFlowPhase.ModeSelection);

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.RestoreJoinTableScreenAfterFailedPin();

//         SetJoinButtonInteractable(true);

//         string userMsg = returnCode == 32758 || (message != null && message.Contains("does not exist"))
//             ? "Room not found. Check PIN."
//             : "Invalid PIN or Room Full!";
//         ShowUIError(userMsg);

//         GameObject joinTable = ModeManager.Instance != null ? ModeManager.Instance.ResolveJoinTablePanel() : null;
//         if (joinTable != null)
//         {
//             CanvasGroup jcg = joinTable.GetComponent<CanvasGroup>();
//             Debug.Log($"[Friends] Join panel after failure | active={joinTable.activeSelf} | alpha={(jcg != null ? jcg.alpha.ToString() : "n/a")} | blocksRaycasts={(jcg != null && jcg.blocksRaycasts)}");
//         }

//         _handlingJoinFailure = false;
//     }

//     void EmergencyUnlockUI()
//     {
//         _joinInProgress = false;
//         PendingJoinPin = null;

//         if (modesPanel == null && ModeManager.Instance != null)
//             modesPanel = ModeManager.Instance.panelModes;

//         // 1. Force unlock THIS panel
//         CanvasGroup localCg = GetComponent<CanvasGroup>();
//         if (localCg != null) { localCg.interactable = true; localCg.blocksRaycasts = true; }

//         // 2. Force unlock the Modes Panel
//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(true);
//             CanvasGroup modeCg = modesPanel.GetComponent<CanvasGroup>();
//             if (modeCg != null)
//             {
//                 modeCg.DOKill();
//                 modeCg.alpha = 1f;
//                 modeCg.interactable = true;
//                 modeCg.blocksRaycasts = true;
//             }
//         }

//         // 3. Force unlock ModeManager panels if they exist
//         if (ModeManager.Instance != null && ModeManager.Instance.panelModes != null)
//         {
//             GameObject mmPanel = ModeManager.Instance.panelModes;
//             mmPanel.SetActive(true);
//             CanvasGroup mmCg = mmPanel.GetComponent<CanvasGroup>();
//             if (mmCg != null)
//             {
//                 mmCg.DOKill();
//                 mmCg.alpha = 1f;
//                 mmCg.interactable = true;
//                 mmCg.blocksRaycasts = true;
//             }
//         }

//         // 4. Force unlock Join Table panel (PIN entry lives here)
//         if (ModeManager.Instance != null)
//         {
//             GameObject joinTable = ModeManager.Instance.ResolveJoinTablePanel();
//             if (joinTable != null)
//             {
//                 joinTable.SetActive(true);
//                 CanvasGroup joinCg = joinTable.GetComponent<CanvasGroup>();
//                 if (joinCg != null)
//                 {
//                     joinCg.DOKill();
//                     joinCg.alpha = 1f;
//                     joinCg.interactable = true;
//                     joinCg.blocksRaycasts = true;
//                 }
//             }
//         }

//         // 5. Brute-force nuke loading / cover overlays in NetworkManager
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.ForceClearBlackOverlay();
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ClearUiInputBlockers();

//             foreach (Transform child in NetworkManager.Instance.transform)
//             {
//                 string childName = child.name.ToLower();
//                 if (childName.Contains("loading") || childName.Contains("cover") || childName.Contains("block"))
//                 {
//                     child.gameObject.SetActive(false);
//                     CanvasGroup childCg = child.GetComponent<CanvasGroup>();
//                     if (childCg != null)
//                     {
//                         childCg.DOKill();
//                         childCg.blocksRaycasts = false;
//                         childCg.interactable = false;
//                     }
//                 }
//             }
//         }

//         NukeInvisibleRaycastBlockers();

//         Debug.Log("[Emergency] UI Unlocked aggressively after failure!");
//     }

//     static void NukeInvisibleRaycastBlockers()
//     {
//         Canvas rootCanvas = null;
//         if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
//             rootCanvas = NetworkManager.Instance.gameCanvasGroup.GetComponentInParent<Canvas>();
//         if (rootCanvas == null)
//             rootCanvas = FindAnyObjectByType<Canvas>();
//         if (rootCanvas == null) return;

//         foreach (CanvasGroup cg in rootCanvas.GetComponentsInChildren<CanvasGroup>(true))
//         {
//             if (cg == null) continue;

//             string n = cg.gameObject.name.ToLower();
//             bool isKnownOverlay = n.Contains("loading") || n.Contains("cover") || n.Contains("block")
//                 || n.Contains("black") || n.Contains("transition") || n.Contains("reconnect");

//             if (isKnownOverlay)
//             {
//                 cg.DOKill();
//                 cg.blocksRaycasts = false;
//                 cg.interactable = false;
//                 if (cg.alpha < 0.15f)
//                     cg.gameObject.SetActive(false);
//                 continue;
//             }

//             if (cg.gameObject.activeSelf && cg.alpha < 0.05f && cg.blocksRaycasts)
//             {
//                 cg.DOKill();
//                 cg.blocksRaycasts = false;
//                 cg.interactable = false;
//             }
//         }
//     }

//     public void ShowJoinError(string errorMsg)
//     {
//         EmergencyUnlockUI();
//         SetJoinButtonInteractable(true);
//         ShowUIError(errorMsg);
//     }

//     public void CancelPinJoinUiState()
//     {
//         _joinInProgress = false;
//         PendingJoinPin = null;
//         StopJoinTimeout();
//         SetJoinButtonInteractable(true);
//     }

//     public void ApplyPinJoinFailureUi(short returnCode, string message)
//     {
//         Debug.LogWarning($"[UI] OnJoinRoomFailed PlayFriendsJoin | code={returnCode} | {message}");
//         CancelPinJoinUiState();
//         UiFlowManager.HideAllOverlays();

//         if (ModeManager.Instance != null)
//         {
//             ModeManager.Instance.MarkFriendsPinJoinFlow();
//             ModeManager.Instance.HidePlayWithFriendsPanel();
//             ModeManager.Instance.RestoreJoinTableScreenAfterFailedPin();
//         }

//         string userMsg = returnCode == 32758 || (message != null && message.Contains("does not exist"))
//             ? "Room not found. Check PIN."
//             : "Invalid PIN! Try again.";
//         ShowUIError(userMsg);
//         Debug.Log("[UI] Restored JoinTable after failed PIN");
//     }

//     public override void OnJoinRoomFailed(short returnCode, string message)
//     {
//         if (!UiFlowManager.IsJoinAttemptCurrent(_joinAttemptToken))
//         {
//             Debug.LogWarning($"[Friends] Stale OnJoinRoomFailed ignored | code={returnCode}");
//             return;
//         }

//         if (!UiFlowManager.ShouldAcceptPhotonUiCallback())
//         {
//             Debug.LogWarning($"[Friends] OnJoinRoomFailed ignored — user left menu | code={returnCode}");
//             CancelPinJoinUiState();
//             UiFlowManager.HideAllOverlays();
//             return;
//         }

//         Debug.LogWarning($"[Friends] OnJoinRoomFailed | code={returnCode} | {message}");
//         UiFlowManager.HandlePinJoinFailed(returnCode, message);

//         // SAFETY NET: guarantees _joinInProgress resets, every blocker clears, and the user lands
//         // back on Modes/JoinTable — regardless of what UiFlowManager.HandlePinJoinFailed does above.
//         // Reuses the exact same proven path already used for synchronous JoinRoom() failures.
//         RestoreJoinPanelAfterFailedJoin(returnCode, message);
//     }

//     /// <summary>
//     /// Reliability fix: if creating OUR private friends room fails (e.g. a rare PIN collision —
//     /// Photon ErrorCode.GameIdAlreadyExists — or a transient state), regenerate a fresh PIN and
//     /// retry a few times so the room/PIN is always created. Ignored for non-private-room creates
//     /// (online / bots), which ModeManager handles.
//     /// </summary>
//     public override void OnCreateRoomFailed(short returnCode, string message)
//     {
//         if (!_creatingPrivateRoom) return;

//         Debug.LogWarning($"[Friends] Private room create failed ({returnCode}): {message}");

//         if (_createRoomRetries < MaxCreateRoomRetries && !PhotonNetwork.InRoom)
//         {
//             if (!NetworkManager.IsPhotonMasterReadyForRooms())
//             {
//                 _pendingCreatePrivateRoom = true;
//                 return;
//             }

//             _createRoomRetries++;
//             Debug.Log($"[Friends] Retrying private room creation with a new PIN (attempt {_createRoomRetries}/{MaxCreateRoomRetries}).");
//             DoCreatePrivateRoom();
//             return;
//         }

//         _creatingPrivateRoom = false;
//         _createRoomRetries = 0;
//         _pendingSeatLobbyOpen = false;
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ForceClearBlackOverlay();
//         }
//         if (ModeManager.Instance != null)
//             ModeManager.Instance.ShowModesScreenOnly();
//         ShowUIError("Could not create room. Please try again.");
//     }

//     void ShowUIError(string errorMsg)
//     {
//         if (string.IsNullOrEmpty(errorMsg)) return;

//         if (errorText != null)
//         {
//             errorText.text = errorMsg;
//             errorText.gameObject.SetActive(true);
//             return;
//         }

//         Debug.LogWarning("[Friends] " + errorMsg);
//     }

//     // ==========================================
//     // 3. WHEN ANYONE JOINS THE ROOM
//     // ==========================================

//     public override void OnJoinedRoom()
//     {
//         _friendsGameStartTriggered = false;
//         _joinInProgress = false;
//         StopJoinTimeout();
//         SetJoinButtonInteractable(true);
//         if (PhotonNetwork.CurrentRoom == null) return;

//         if (!UiFlowManager.ShouldAcceptPhotonUiCallback())
//         {
//             Debug.LogWarning("[Friends] OnJoinedRoom ignored — stale callback (user on Home).");
//             return;
//         }

//         bool isPrivateFriendsRoomEarly = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;
//         bool allowJoinedRoom = UiFlowManager.IsJoinAttemptCurrent(_joinAttemptToken)
//             || _onlineMode
//             || UiFlowManager.IsOnlineMatchmakingFlow()
//             || _pendingSeatLobbyOpen
//             || (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient)
//             || (UiFlowManager.IsPlayFriendsJoinFlow() && isPrivateFriendsRoomEarly)
//             || (UiFlowManager.IsPlayFriendsLobbyFlow() && isPrivateFriendsRoomEarly);

//         if (!allowJoinedRoom)
//         {
//             Debug.LogWarning("[Friends] OnJoinedRoom ignored — stale join attempt token.");
//             return;
//         }

//         if (_isLeavingFriendsFlow)
//         {
//             Debug.Log("[Friends] OnJoinedRoom ignored — user left friends flow; leaving room.");
//             if (PhotonNetwork.InRoom)
//                 PhotonNetwork.LeaveRoom();
//             return;
//         }

//         bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

//         // Private friends PIN room always wins over stale online matchmaking flags.
//         if (isPrivateFriendsRoom)
//         {
//             _onlineMode = false;
//             _previewBotsInOnlineLobby = false;

//             if (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient && !_pendingSeatLobbyOpen)
//             {
//                 Debug.Log("[Friends] Host eager room joined — staying on modes panel");
//                 TrySendPendingInvite();
//                 RefreshRoomIdPlaque();
//                 return;
//             }

//             Debug.Log($"[Friends] OnJoinedRoom PlayFriends | room={PhotonNetwork.CurrentRoom.Name} | players={PhotonNetwork.CurrentRoom.PlayerCount} | master={PhotonNetwork.MasterClient?.NickName} | localIsMaster={PhotonNetwork.IsMasterClient}");
//             EnsureHostActorRoomProperty();
//             if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsOnJoin))
//                 ApplyBotsIncludedState((bool)botsOnJoin);
//             if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.CancelPinJoinUiOverlays();
//             TrySendPendingInvite();

//             UiFlowManager.MarkPlayFriendsLobby();

//             if (_pendingSeatLobbyOpen)
//                 PresentSeatLobbyUI();
//             else
//             {
//                 if (ModeManager.Instance != null)
//                     ModeManager.Instance.HideJoinTablePanel();
//                 ShowPrivateRoomLobbyUI();
//             }
//             UpdatePlayerListUI();
//             return;
//         }

//         // Online matchmaking: this panel is the lobby — fill seats with real players.
//         if (_onlineMode || UiFlowManager.IsOnlineMatchmakingFlow())
//         {
//             Debug.Log($"[UI] OnJoinedRoom OnlineMatchmaking | room={PhotonNetwork.CurrentRoom.Name} | players={PhotonNetwork.CurrentRoom.PlayerCount}");
//             if (!_onlineMode)
//                 ShowOnlineMatchmakingLobby();
//             UpdatePlayerListUI();
//             return;
//         }
//     }

//     MonoBehaviour GetCoroutineRunner()
//     {
//         if (isActiveAndEnabled)
//             return this;
//         if (NetworkManager.Instance != null && NetworkManager.Instance.isActiveAndEnabled)
//             return NetworkManager.Instance;
//         return null;
//     }

//     static void StopCoroutineOnRunner(MonoBehaviour runner, Coroutine coroutine)
//     {
//         if (coroutine == null || runner == null) return;
//         if (runner.isActiveAndEnabled)
//             runner.StopCoroutine(coroutine);
//     }

//     void StopFriendsCoroutineSlot(ref Coroutine slot, ref MonoBehaviour runner)
//     {
//         if (slot == null) return;
//         StopCoroutineOnRunner(runner, slot);
//         slot = null;
//         runner = null;
//     }

//     Coroutine StartFriendsCoroutine(IEnumerator routine, ref Coroutine slot, ref MonoBehaviour runnerSlot)
//     {
//         StopFriendsCoroutineSlot(ref slot, ref runnerSlot);

//         MonoBehaviour runner = GetCoroutineRunner();
//         if (runner == null)
//             return null;

//         runnerSlot = runner;
//         slot = runner.StartCoroutine(routine);
//         return slot;
//     }

//     /// <summary>Polls player names until Photon syncs nicknames for all seated players.</summary>
//     public void BeginLobbyPlayerListRefresh()
//     {
//         StartFriendsCoroutine(LobbyPlayerListRefreshRoutine(), ref _lobbyPlayerRefreshCoroutine, ref _lobbyPlayerRefreshRunner);
//         if (_lobbyPlayerRefreshCoroutine == null)
//             UpdatePlayerListUI();
//     }

//     IEnumerator LobbyPlayerListRefreshRoutine()
//     {
//         for (int i = 0; i < 20; i++)
//         {
//             if (!PhotonNetwork.InRoom)
//                 yield break;

//             UpdatePlayerListUI();
//             yield return new WaitForSecondsRealtime(0.25f);
//         }
//         Debug.Log("[Friends] Player list updated");
//         _lobbyPlayerRefreshCoroutine = null;
//     }

//     static string GetPlayerDisplayName(Player p)
//     {
//         if (p == null) return "Player";
//         if (!string.IsNullOrWhiteSpace(p.NickName)) return p.NickName.Trim();
//         if (!string.IsNullOrWhiteSpace(p.UserId)) return p.UserId;
//         return "Player " + p.ActorNumber;
//     }

//     static int GetRoomHostActorNumber()
//     {
//         if (PhotonNetwork.InRoom
//             && PhotonNetwork.CurrentRoom != null
//             && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("HAN", out object hanObj)
//             && hanObj != null
//             && int.TryParse(hanObj.ToString(), out int storedHost))
//             return storedHost;

//         return PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : -1;
//     }

//     public static bool IsLocalRoomHost()
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return false;
//         return PhotonNetwork.LocalPlayer.ActorNumber == GetRoomHostActorNumber();
//     }

//     static void EnsureHostActorRoomProperty()
//     {
//         if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;
//         if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("HAN")) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "HAN", PhotonNetwork.LocalPlayer.ActorNumber }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     /// <summary>Host pressed Start on the seat panel — allows ModeManager to launch the match.</summary>
//     public void ConfirmHostSeatStart() => _hostConfirmedSeatStart = true;

//     /// <summary>Returns true once when the host confirmed start from the seat panel.</summary>
//     public bool ConsumeHostSeatStartConfirmation()
//     {
//         if (!_hostConfirmedSeatStart) return false;
//         _hostConfirmedSeatStart = false;
//         return true;
//     }

//     /// <summary>Resets async menu-flow flags without tearing down seat UI.</summary>
//     public void ResetMenuFlowFlags()
//     {
//         _joinInProgress = false;
//         _isLeavingRoom = false;
//         _pendingSeatLobbyOpen = false;
//         _creatingPrivateRoom = false;
//         _createRoomRetries = 0;
//     }

//     /// <summary>Full local reset when leaving / abandoning a private friends lobby.</summary>
//     public void ResetLobbyStateForLeave()
//     {
//         ResetMenuFlowFlags();
//         _hostConfirmedSeatStart = false;
//         _friendsGameStartTriggered = false;
//         _pendingSeatLobbyOpen = false;
//         if (!_pendingCreatePrivateRoom)
//             SuppressSeatLobbyOnJoin = false;
//         _onlineMode = false;

//         if (_lobbyPlayerRefreshCoroutine != null)
//             StopFriendsCoroutineSlot(ref _lobbyPlayerRefreshCoroutine, ref _lobbyPlayerRefreshRunner);

//         ResetSeatPanelUI();
//         HidePlayWithFriendsLobbyPanel();
//         HideClientWaitingPresentation();

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.HidePlayWithFriendsPanel();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ClearUiInputBlockers();
//     }

//     void SyncRoomLobbyUIForRole()
//     {
//         if (!PhotonNetwork.InRoom) return;

//         bool isHost = IsLocalRoomHost();

//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(false);
//             CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
//             if (cg != null)
//             {
//                 cg.interactable = false;
//                 cg.blocksRaycasts = false;
//             }
//         }

//         if (startGameButton != null)
//             startGameButton.SetActive(isHost);

//         ApplyClientWaitingPresentation(!isHost, "Waiting for Host...");

//         CheckPlayerCountAndToggleStart();
//     }

//     void ApplyClientWaitingPresentation(bool show, string message = "Waiting for Host...")
//     {
//         if (clientWaitingText != null)
//         {
//             if (show)
//             {
//                 clientWaitingText.fontSize = clientWaitingFontSize;
//                 clientWaitingText.fontStyle = FontStyles.Bold;
//                 clientWaitingText.text = message;
//                 clientWaitingText.gameObject.SetActive(true);
//             }
//             else
//             {
//                 clientWaitingText.gameObject.SetActive(false);
//             }
//         }

//         EnsureClientWaitingSpinner();
//         if (clientWaitingSpinner == null) return;

//         _waitingSpinnerTween?.Kill();
//         clientWaitingSpinner.gameObject.SetActive(show);

//         if (!show) return;

//         clientWaitingSpinner.localRotation = Quaternion.identity;
//         _waitingSpinnerTween = clientWaitingSpinner
//             .DORotate(new Vector3(0f, 0f, -360f), 1.1f, RotateMode.FastBeyond360)
//             .SetLoops(-1, LoopType.Restart)
//             .SetEase(Ease.Linear)
//             .SetUpdate(true);
//     }

//     void EnsureClientWaitingSpinner()
//     {
//         if (clientWaitingSpinner != null || clientWaitingText == null) return;

//         Transform parent = clientWaitingText.transform.parent;
//         if (parent == null) return;

//         Transform existing = parent.Find("WaitingSpinner");
//         if (existing != null)
//         {
//             clientWaitingSpinner = existing as RectTransform;
//             return;
//         }

//         var go = new GameObject("WaitingSpinner", typeof(RectTransform), typeof(Image));
//         go.transform.SetParent(parent, false);
//         clientWaitingSpinner = go.GetComponent<RectTransform>();

//         var textRt = clientWaitingText.rectTransform;
//         clientWaitingSpinner.anchorMin = clientWaitingSpinner.anchorMax = textRt.anchorMin;
//         clientWaitingSpinner.pivot = new Vector2(1f, 0.5f);
//         clientWaitingSpinner.sizeDelta = new Vector2(44f, 44f);
//         clientWaitingSpinner.anchoredPosition = textRt.anchoredPosition + new Vector2(-18f, 0f);

//         var img = go.GetComponent<Image>();
//         img.color = new Color(1f, 0.92f, 0.55f, 0.95f);
//         img.raycastTarget = false;

//         var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
//         ring.transform.SetParent(go.transform, false);
//         var ringRt = ring.GetComponent<RectTransform>();
//         ringRt.anchorMin = Vector2.zero;
//         ringRt.anchorMax = Vector2.one;
//         ringRt.offsetMin = new Vector2(6f, 6f);
//         ringRt.offsetMax = new Vector2(-6f, -6f);
//         var ringImg = ring.GetComponent<Image>();
//         ringImg.color = new Color(0.35f, 0.22f, 0.12f, 0.35f);
//         ringImg.raycastTarget = false;
//     }

//     void HideClientWaitingPresentation()
//     {
//         ApplyClientWaitingPresentation(false);
//     }

//     // Public (BUG 2 fix): NetworkManager.HandleJoinedRoomDeferred re-shows this as the
//     // single source of truth for the joining client's seat lobby panel.
//     public void ShowPrivateRoomLobbyUI()
//     {
//         if (_isLeavingFriendsFlow) return;
//         if (PhotonNetwork.CurrentRoom == null) return;

//         Debug.Log($"[Friends] Showing RoomLobby after join success | room={PhotonNetwork.CurrentRoom.Name} | players={PhotonNetwork.CurrentRoom.PlayerCount}");

//         GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);

//         if (ModeManager.Instance != null)
//         {
//             ModeManager.Instance.HideJoinTablePanel();
//             if (ModeManager.Instance.panelModes != null && !PhotonNetwork.IsMasterClient)
//                 ModeManager.Instance.panelModes.SetActive(false);
//             ModeManager.Instance.ShowPlayWithFriendsPanel();
//         }

//         if (!gameObject.activeInHierarchy)
//         {
//             gameObject.SetActive(true);
//             transform.SetAsLastSibling();
//         }

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ResetRoomLobbyCanvasGroup();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ForceClearBlackOverlay();

//         if (modesPanel != null && PhotonNetwork.IsMasterClient)
//             modesPanel.SetActive(false);

//         // Friends mode: show PIN/Room ID plaque, hide online timer.
//         _onlineMode = false;
//         ApplyModeControls(false);
//         SetSeatPanelTitle("SELECT CHAIRS");

//         if (pinCreationPanel != null)
//         {
//             pinCreationPanel.SetActive(true);
//             pinCreationPanel.transform.SetAsLastSibling();
//         }
//         StartRoomIdPlaqueWatch();
//         if (errorText != null) errorText.gameObject.SetActive(false);

//         if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj) && botsObj is bool botsObjVal)
//             ApplyBotsIncludedState(botsObjVal);
//         else
//             ApplyBotsIncludedState(false);

//         UpdatePlayerListUI();
//         EnsureLobbyInviteButton(true);
//         SyncRoomLobbyUIForRole();
//         BeginLobbyPlayerListRefresh();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.HideLoadingInstant();
//     }

//     public void ToggleBots()
//     {
//         if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

//         bool newState = !areBotsIncluded;
//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
//         props["BotsIncluded"] = newState;
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     void ApplyBotsIncludedState(bool included)
//     {
//         areBotsIncluded = included;
//         if (includeBotsBtnText != null)
//             includeBotsBtnText.text = areBotsIncluded ? "Remove Bots" : "Include Bots";
//     }

//     void UpdatePlayerListUI()
//     {
//         if (playerSlotsText == null || playerSlotsText.Length == 0) return;

//         if (_onlineMode && !PhotonNetwork.InRoom)
//         {
//             ShowLocalPlayerInOnlineMatchmaking();
//             return;
//         }

//         if (!PhotonNetwork.InRoom) return;

//         Player[] currentPlayers = PhotonRoomPlayers.GetSorted();
//         int realPlayerCount = currentPlayers.Length;
//         int displaySlotsFilled = realPlayerCount;
//         if (areBotsIncluded || (_onlineMode && _previewBotsInOnlineLobby))
//             displaySlotsFilled = DeckManager.MaxTableSeats;

//         RefreshLobbyPlayerCountLabel(realPlayerCount, displaySlotsFilled);

//         for (int i = 0; i < playerSlotsText.Length; i++)
//         {
//             if (playerSlotsText[i] == null) continue;

//             if (i < realPlayerCount)
//             {
//                 Player p = currentPlayers[i];
//                 int hostActor = GetRoomHostActorNumber();
//                 bool isRoomHost = hostActor > 0 && p.ActorNumber == hostActor;
//                 string hostTag = isRoomHost ? " (Host)" : "";
//                 playerSlotsText[i].text = GetPlayerDisplayName(p) + hostTag;
//                 playerSlotsText[i].color = Color.white;
//                 SetSeatAvatar(i, GetAvatarIndexForPlayer(p), true);
//             }
//             else if (areBotsIncluded || (_onlineMode && _previewBotsInOnlineLobby))
//             {
//                 playerSlotsText[i].text = realPlayerCount == 3 && i == realPlayerCount
//                     ? "DehlaBot"
//                     : "AI Bot " + (i - realPlayerCount + 1);
//                 playerSlotsText[i].color = new Color(0.4f, 1f, 0.4f, 1f);
//                 SetSeatAvatar(i, -1, true); // fallback bot avatar
//             }
//             else
//             {
//                 playerSlotsText[i].text = _onlineMode ? "Waiting..." : "Waiting for Friend...";
//                 playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
//                 SetSeatAvatar(i, -1, false); // empty seat
//             }
//         }
//     }

//     void RefreshLobbyPlayerCountLabel(int realPlayers, int displayFilled)
//     {
//         if (_onlineMode) return;

//         if (matchmakingTimerText == null) return;

//         bool inPrivateLobby = PhotonNetwork.InRoom
//             && PhotonNetwork.CurrentRoom != null
//             && !PhotonNetwork.CurrentRoom.IsVisible
//             && !PhotonNetwork.OfflineMode;

//         if (!inPrivateLobby)
//         {
//             matchmakingTimerText.gameObject.SetActive(false);
//             return;
//         }

//         matchmakingTimerText.gameObject.SetActive(true);
//         matchmakingTimerText.text = areBotsIncluded
//             ? $"Players: {displayFilled}/{DeckManager.MaxTableSeats}"
//             : $"Players: {realPlayers}/{DeckManager.MaxTableSeats}";
//     }

//     // ==========================================
//     // SEAT AVATARS (real selected profile images)
//     // ==========================================

//     Sprite[] _avatarPoolCache;

//     /// <summary>Canonical avatar sprite pool (same list profile indices were chosen from).</summary>
//     Sprite[] GetAvatarPool()
//     {
//         if (PlayerProfileManager.Instance != null
//             && PlayerProfileManager.Instance.profileSprites != null
//             && PlayerProfileManager.Instance.profileSprites.Length > 0)
//         {
//             _avatarPoolCache = PlayerProfileManager.Instance.profileSprites;
//             return _avatarPoolCache;
//         }
//         if (_avatarPoolCache != null && _avatarPoolCache.Length > 0) return _avatarPoolCache;
//         if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
//             _avatarPoolCache = MatchmakingManager.GlobalProfileSprites.ToArray();
//         return _avatarPoolCache;
//     }

//     /// <summary>Avatar index a player selected: local uses PlayerPrefs, remote uses synced custom property.</summary>
//     int GetAvatarIndexForPlayer(Player p)
//     {
//         if (p == null) return -1;
//         if (PhotonNetwork.LocalPlayer != null && p.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
//         {
//             int local = PlayerProfileManager.GetSavedAvatarIndex();
//             if (local >= 0) return local;
//         }
//         if (p.CustomProperties != null
//             && p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val) && val != null)
//         {
//             if (val is int vi) return vi;
//             if (int.TryParse(val.ToString(), out int parsed)) return parsed;
//         }
//         return -1;
//     }

//     /// <summary>Assigns the avatar sprite for a seat. occupied=false dims the slot (empty seat).</summary>
//     void SetSeatAvatar(int seatIndex, int avatarIndex, bool occupied)
//     {
//         if (playerSlotsAvatar == null || seatIndex < 0 || seatIndex >= playerSlotsAvatar.Length) return;
//         UnityEngine.UI.Image img = playerSlotsAvatar[seatIndex];
//         if (img == null) return;

//         Sprite[] pool = GetAvatarPool();
//         if (pool != null && pool.Length > 0)
//         {
//             int idx = avatarIndex;
//             if (idx < 0 || idx >= pool.Length) idx = Mathf.Abs(seatIndex + 1) % pool.Length;
//             img.sprite = pool[idx];
//             img.preserveAspect = true;
//         }
//         // Dim empty seats, full colour for occupied ones.
//         img.color = occupied ? Color.white : new Color(1f, 1f, 1f, 0.25f);
//     }

//     void ShowLocalPlayerInOnlineMatchmaking()
//     {
//         EnsureNickname();

//         for (int i = 0; i < playerSlotsText.Length; i++)
//         {
//             if (playerSlotsText[i] == null) continue;

//             if (i == 0)
//             {
//                 playerSlotsText[i].text = MyDisplayName;
//                 playerSlotsText[i].color = Color.white;
//                 int avatarIdx = PhotonNetwork.LocalPlayer != null
//                     ? GetAvatarIndexForPlayer(PhotonNetwork.LocalPlayer)
//                     : PlayerProfileManager.GetSavedAvatarIndex();
//                 SetSeatAvatar(0, avatarIdx, true);
//             }
//             else
//             {
//                 playerSlotsText[i].text = "Waiting...";
//                 playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
//                 SetSeatAvatar(i, -1, false);
//             }
//         }
//     }

//     void ClearPlayerListUI()
//     {
//         if (playerSlotsText == null) return;

//         for (int i = 0; i < playerSlotsText.Length; i++)
//         {
//             if (playerSlotsText[i] == null) continue;
//             playerSlotsText[i].text = "Waiting for Friend...";
//             playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
//         }
//     }

//     void CheckPlayerCountAndToggleStart()
//     {
//         // Online matchmaking auto-starts (DeckManager-driven) and has no manual Start button.
//         if (_onlineMode) return;

//         if (startGameButton == null)
//             UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

//         if (includeBotsButton != null)
//             includeBotsButton.SetActive(PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount < DeckManager.MaxTableSeats);

//         if (!PhotonNetwork.IsMasterClient)
//         {
//             if (startGameButton != null) startGameButton.SetActive(false);
//             return;
//         }

//         if (startGameButton == null) return;

//         // Host always sees the Start button, but it stays greyed/disabled until the
//         // table is full (4 seats) or bots are included.
//         startGameButton.SetActive(true);
//         bool canStart = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;
//         SetStartButtonInteractable(canStart);
//     }

//     void SetStartButtonInteractable(bool on)
//     {
//         if (startGameButton == null) return;

//         Button btn = startGameButton.GetComponent<Button>();
//         if (btn != null) btn.interactable = on;

//         CanvasGroup cg = startGameButton.GetComponent<CanvasGroup>();
//         if (cg == null) cg = startGameButton.AddComponent<CanvasGroup>();
//         cg.alpha = on ? 1f : 0.5f;
//         cg.interactable = on;
//         cg.blocksRaycasts = on;
//     }

//     Coroutine _roomIdRefreshCoroutine;
//     MonoBehaviour _roomIdRefreshRunner;

//     /// <summary>
//     /// Sets the ROOM ID / PIN plaque text from the current private room. Resolves the TMP label
//     /// by name if it was not wired, and ensures the plaque is visible. Shows a placeholder while
//     /// the room is still being created so the plaque never gets stuck on the editor default.
//     /// </summary>
//     void RefreshRoomIdPlaque()
//     {
//         if (generatedPinText == null)
//         {
//             if (UiSafeLookup.TryGet("Txt_GeneratedPIN", out GameObject pinGo) && pinGo != null)
//                 generatedPinText = pinGo.GetComponent<TMP_Text>();
//         }
//         if (generatedPinText == null) return;

//         if (roomIdPlaque != null && !roomIdPlaque.activeSelf)
//             roomIdPlaque.SetActive(true);
//         if (!generatedPinText.gameObject.activeSelf)
//             generatedPinText.gameObject.SetActive(true);

//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
//             generatedPinText.text = "ROOM ID :- " + PhotonNetwork.CurrentRoom.Name;
//         else
//             generatedPinText.text = "ROOM ID :- ...";
//     }

//     /// <summary>Refreshes the PIN plaque now and keeps retrying briefly until the room exists.</summary>
//     void StartRoomIdPlaqueWatch()
//     {
//         RefreshRoomIdPlaque();
//         StartFriendsCoroutine(RoomIdPlaqueWatchRoutine(), ref _roomIdRefreshCoroutine, ref _roomIdRefreshRunner);
//     }

//     IEnumerator RoomIdPlaqueWatchRoutine()
//     {
//         float timeout = 15f;
//         while (timeout > 0f)
//         {
//             RefreshRoomIdPlaque();
//             if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
//                 break;
//             yield return new WaitForSeconds(0.3f);
//             timeout -= 0.3f;
//         }
//         _roomIdRefreshCoroutine = null;
//         _roomIdRefreshRunner = null;
//     }

//     /// <summary>
//     /// Shows a friend-invite button on the friends seat/lobby panel that is a CLONE of the home
//     /// screen's invite button (same look, same anchored position). Tapping it opens the same
//     /// friends list (FRIENDS / REQUESTS + per-friend INVITE) over the lobby so the player can
//     /// invite friends straight into this room. Hidden during online matchmaking.
//     /// </summary>
//     void EnsureLobbyInviteButton(bool visible)
//     {
//         if (_lobbyInviteButton == null)
//             BuildLobbyInviteButton();

//         if (_lobbyInviteButton == null) return;

//         _lobbyInviteButton.SetActive(visible);
//         if (visible) _lobbyInviteButton.transform.SetAsLastSibling();
//     }

//     void BuildLobbyInviteButton()
//     {
//         UnityEngine.UI.Button homeBtn = ResolveHomeInviteButton();

//         if (homeBtn != null)
//         {
//             // Clone the EXACT home invite button so it looks identical, place it at the same
//             // anchored position, and rewire its click to open the friends list over the lobby.
//             GameObject go = Instantiate(homeBtn.gameObject, transform);
//             go.name = "FRIEND_INVITE_BUTTON";

//             RectTransform src = homeBtn.GetComponent<RectTransform>();
//             RectTransform rt = go.GetComponent<RectTransform>();
//             if (src != null && rt != null)
//             {
//                 rt.anchorMin = src.anchorMin;
//                 rt.anchorMax = src.anchorMax;
//                 rt.pivot = src.pivot;
//                 rt.sizeDelta = src.sizeDelta;
//                 rt.anchoredPosition = src.anchoredPosition;
//                 rt.localScale = src.localScale;
//             }

//             UnityEngine.UI.Button btn = go.GetComponent<UnityEngine.UI.Button>();
//             if (btn != null)
//             {
//                 btn.onClick.RemoveAllListeners();
//                 btn.onClick.AddListener(OpenLobbyFriendInvite);
//             }

//             go.SetActive(false);
//             _lobbyInviteButton = go;
//             return;
//         }

//         // Fallback: a simple labelled button if the home button could not be found.
//         GameObject fb = new GameObject("FRIEND_INVITE_BUTTON",
//             typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button));
//         fb.transform.SetParent(transform, false);

//         RectTransform frt = fb.GetComponent<RectTransform>();
//         frt.anchorMin = frt.anchorMax = new Vector2(1f, 0.5f);
//         frt.pivot = new Vector2(1f, 0.5f);
//         frt.anchoredPosition = Vector2.zero;
//         frt.sizeDelta = new Vector2(100f, 250f);

//         UnityEngine.UI.Image img = fb.GetComponent<UnityEngine.UI.Image>();
//         img.color = new Color(0.18f, 0.55f, 0.30f, 1f);

//         Button fbBtn = fb.GetComponent<Button>();
//         fbBtn.targetGraphic = img;
//         fbBtn.onClick.AddListener(OpenLobbyFriendInvite);

//         GameObject labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
//         labelGo.transform.SetParent(fb.transform, false);
//         RectTransform lrt = labelGo.GetComponent<RectTransform>();
//         lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
//         lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
//         TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
//         label.text = "FRIENDS";
//         label.fontSize = 28f;
//         label.fontStyle = FontStyles.Bold;
//         label.alignment = TextAlignmentOptions.Center;
//         label.color = Color.white;
//         label.raycastTarget = false;

//         fb.SetActive(false);
//         _lobbyInviteButton = fb;
//     }

//     /// <summary>Resolves the home-screen invite button (the one that opens the friends drawer).</summary>
//     UnityEngine.UI.Button ResolveHomeInviteButton()
//     {
//         if (FriendsDrawerController.Instance != null
//             && FriendsDrawerController.Instance.inviteFriendsButton != null)
//             return FriendsDrawerController.Instance.inviteFriendsButton;

//         FriendsDrawerController drawer = FindFirstObjectByType<FriendsDrawerController>(FindObjectsInactive.Include);
//         if (drawer != null && drawer.inviteFriendsButton != null)
//             return drawer.inviteFriendsButton;

//         return null;
//     }

//     /// <summary>
//     /// Opens the SAME home friends list (FRIENDS / REQUESTS tabs + per-friend INVITE) over the
//     /// room lobby. Inviting a friend from here sends them a Firebase invite carrying this room's
//     /// PIN; when they accept they join this room, when they decline the invite is removed.
//     /// </summary>
//     public void OpenLobbyFriendInvite()
//     {
//         FriendsDrawerController drawer = FriendsDrawerController.Instance;
//         if (drawer == null)
//             drawer = FindFirstObjectByType<FriendsDrawerController>(FindObjectsInactive.Include);

//         if (drawer == null)
//         {
//             ShowUIError("Friends list unavailable.");
//             return;
//         }

//         // The lobby lives on the active main Canvas; pass it explicitly so the drawer is
//         // re-parented onto a VISIBLE root (gameCanvasGroup/Panel_Game is inactive here).
//         Canvas canvas = GetComponentInParent<Canvas>();
//         Transform overlayRoot = canvas != null ? canvas.transform : transform.root;
//         drawer.OpenDrawerDuringGame(overlayRoot);

//         // Make sure the friends list is freshly populated with live status.
//         if (FriendsPanelUIController.Instance != null)
//             FriendsPanelUIController.Instance.RefreshAll();
//         RefreshFriendsStatus();
//     }

//     /// <summary>
//     /// Opens the seat lobby once the Photon private room exists. Shows loading until joined.
//     /// </summary>
//     public void OpenSeatLobbyWhenReady()
//     {
//         if (_isLeavingRoom) return;

//         Debug.Log("[Friends] OpenSeatLobbyWhenReady");
//         BeginFriendsFlow();
//         SuppressSeatLobbyOnJoin = false;
//         _pendingSeatLobbyOpen = true;

//         if (PhotonNetwork.InRoom)
//         {
//             PresentSeatLobbyUI();
//             return;
//         }

//         Debug.Log("[Friends] Seat lobby deferred — waiting for room create/join");
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.ShowLoading("Creating room...");
//             NetworkManager.Instance.AnimateLoadingSlider(NetworkManager.GameStartLoadingDelaySeconds);
//         }

//         CreatePrivateRoom();
//     }

//     void PresentSeatLobbyUI()
//     {
//         _pendingSeatLobbyOpen = false;
//         Debug.Log("[Friends] PresentSeatLobbyUI");

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ResetRoomLobbyCanvasGroup();
//         }

//         OnSeatPanelOpened();
//     }

//     /// <summary>
//     /// Called when the seat/lobby panel is opened (host taps Play on the modes screen).
//     /// Resets the player list and shows the Start button greyed-out until the table fills.
//     /// </summary>
//     public void OnSeatPanelOpened()
//     {
//         Debug.Log("[Friends] Seat panel opened");
//         BeginFriendsFlow();
//         SuppressSeatLobbyOnJoin = false;

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.ShowPlayWithFriendsPanel();
//         else if (!gameObject.activeInHierarchy)
//         {
//             gameObject.SetActive(true);
//             transform.SetAsLastSibling();
//         }

//         if (errorText != null) errorText.gameObject.SetActive(false);
//         ClearPlayerListUI();

//         if (startGameButton == null)
//             UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

//         if (startGameButton != null)
//         {
//             startGameButton.SetActive(true);
//             SetStartButtonInteractable(false);
//         }

//         // Friends mode: ensure online controls are off and PIN plaque is shown.
//         _onlineMode = false;
//         ApplyModeControls(false);
//         SetSeatPanelTitle("SELECT CHAIRS");

//         // New flow: the Create Room button is hidden on the seat panel, so the host
//         // automatically creates the private room as soon as this panel opens. Friends
//         // join from the Modes screen's JOIN TABLE panel using the shown ROOM ID.
//         if (pinCreationPanel != null)
//         {
//             pinCreationPanel.SetActive(true);
//             pinCreationPanel.transform.SetAsLastSibling();
//         }

//         if (!PhotonNetwork.InRoom)
//         {
//             Debug.Log("[Friends] Seat panel opened before room ready — create still pending");
//             return;
//         }

//         UpdatePlayerListUI();

//         StartRoomIdPlaqueWatch();
//         CheckPlayerCountAndToggleStart();
//         EnsureLobbyInviteButton(true);
//     }

//     // ==========================================
//     // ONLINE MATCHMAKING (shared seat panel)
//     // ==========================================

//     /// <summary>
//     /// Shows this seat panel as the ONLINE matchmaking lobby. Hides PIN / Create / manual
//     /// Start / Bots controls, shows the countdown timer, and fills seats with real players
//     /// as they join the public room. The match auto-starts (driven by DeckManager) once the
//     /// table is full or the timer expires.
//     /// </summary>
//     public void ShowOnlineMatchmakingLobby()
//     {
//         _onlineMode = true;
//         _previewBotsInOnlineLobby = false;

//         if (ModeManager.Instance != null)
//         {
//             ModeManager.Instance.SetFriendsMatchMode(false);
//             if (ModeManager.Instance.panelHomeScreen != null)
//                 ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelHomeScreen, false);
//             if (ModeManager.Instance.panelModes != null)
//                 ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelModes, false);
//             ModeManager.Instance.HideJoinTablePanel();
//         }

//         UiFlowManager.BeginOnlineMatchmaking();

//         ModeManager.EnsurePanelHierarchyActivePublic(gameObject);

//         if (!gameObject.activeSelf)
//             gameObject.SetActive(true);

//         transform.SetAsLastSibling();

//         CanvasGroup cg = GetComponent<CanvasGroup>();
//         if (cg != null)
//         {
//             cg.DOKill();
//             cg.alpha = 1f;
//             cg.interactable = true;
//             cg.blocksRaycasts = true;
//         }

//         RectTransform rt = transform as RectTransform;
//         if (rt != null)
//         {
//             rt.localScale = Vector3.one;
//             if (Mathf.Abs(rt.anchoredPosition.y) > 5000f)
//                 rt.anchoredPosition = Vector2.zero;
//         }

//         if (errorText != null) errorText.gameObject.SetActive(false);
//         if (modesPanel != null) modesPanel.SetActive(false);
//         if (startGameButton != null) startGameButton.SetActive(false); // online auto-starts

//         ApplyModeControls(true);
//         SetSeatPanelTitle("FINDING PLAYERS");

//         if (matchmakingTimerText != null)
//         {
//             matchmakingTimerText.gameObject.SetActive(true);
//             matchmakingTimerText.text = "Finding players...";
//         }

//         ClearPlayerListUI();
//         ShowLocalPlayerInOnlineMatchmaking();
//         EnsureLobbyInviteButton(false);
//         if (PhotonNetwork.InRoom) UpdatePlayerListUI();
//     }

//     bool _previewBotsInOnlineLobby;

//     /// <summary>Forwarded from DeckManager's matchmaking countdown (players found + seconds left).</summary>
//     public void UpdateOnlineTimer(int playersFound, int countdown)
//     {
//         if (!_onlineMode) return;

//         _previewBotsInOnlineLobby = countdown <= 2 && playersFound < DeckManager.MaxTableSeats;
//         int displayCount = playersFound;
//         if (_previewBotsInOnlineLobby)
//             displayCount = DeckManager.MaxTableSeats;

//         if (matchmakingTimerText != null)
//         {
//             matchmakingTimerText.text = playersFound >= DeckManager.MaxTableSeats
//                 ? "Starting game..."
//                 : $"Players: {displayCount}/{DeckManager.MaxTableSeats}    Starting in {Mathf.Max(0, countdown)}s";
//         }

//         if (PhotonNetwork.InRoom) UpdatePlayerListUI();
//     }

//     /// <summary>Hides the seat panel (used on match found / cancel for the online flow).</summary>
//     public void HideLobby()
//     {
//         _onlineMode = false;
//         ApplyModeControls(false);
//         HidePrivateFriendsLobbyUI();

//         CanvasGroup cg = GetComponent<CanvasGroup>();
//         if (cg != null)
//         {
//             cg.DOKill();
//             cg.alpha = 0f;
//             cg.interactable = false;
//             cg.blocksRaycasts = false;
//         }

//         if (gameObject.activeSelf) gameObject.SetActive(false);
//     }

//     /// <summary>Toggles friends-only vs online-only seat-panel controls.</summary>
//     void ApplyModeControls(bool online)
//     {
//         GameObject createBtn = createRoomButton;
//         if (createBtn == null)
//         {
//             Transform t = transform.Find("ContentArea/Host Section/Btn_CreateRoom");
//             if (t != null) createBtn = t.gameObject;
//         }
//         if (createBtn != null) createBtn.SetActive(false); // room auto-creates in both flows

//         Transform join = transform.Find("ContentArea/Join Section");
//         if (join != null) join.gameObject.SetActive(false); // join handled on the modes screen

//         GameObject plaque = roomIdPlaque;
//         if (plaque == null)
//         {
//             Transform t = transform.Find("RoomIdPlaque");
//             if (t != null) plaque = t.gameObject;
//         }
//         if (plaque != null) plaque.SetActive(!online); // PIN/Room ID only for friends

//         if (online && includeBotsButton != null) includeBotsButton.SetActive(false);

//         if (matchmakingTimerPlaque != null) matchmakingTimerPlaque.SetActive(online);
//         if (matchmakingTimerText != null) matchmakingTimerText.gameObject.SetActive(online);
//     }

//     void SetSeatPanelTitle(string text)
//     {
//         Transform t = transform.Find("TitlePlaque/Title");
//         if (t == null) t = transform.Find("Title");
//         if (t != null)
//         {
//             TMP_Text label = t.GetComponent<TMP_Text>();
//             if (label != null) label.text = text;
//         }
//     }

//     /// <summary>
//     /// Seat-panel BACK button. In online matchmaking it cancels the search; in friends
//     /// mode it leaves the private room and returns to the modes screen.
//     /// </summary>
//     public void OnSeatPanelBackClicked()
//     {
//         bool onlineLobby = _onlineMode
//             || (MatchmakingManager.Instance != null && MatchmakingManager.Instance.IsSearching)
//             || GameFlowState.Current == GameFlowPhase.Matchmaking;

//         if (onlineLobby)
//         {
//             if (MatchmakingManager.Instance != null)
//                 MatchmakingManager.Instance.OnCancelClicked();
//             else
//                 HideLobby();
//             return;
//         }

//         LeaveCurrentRoom();
//     }

//     // ==========================================
//     // PROPER LEAVE ROOM (Back button) + UI RESET
//     // ==========================================

//     void StopFriendsGameStartCoroutine()
//     {
//         if (NetworkManager.Instance != null && _smoothGameStartCoroutine != null)
//         {
//             NetworkManager.Instance.StopCoroutine(_smoothGameStartCoroutine);
//             _smoothGameStartCoroutine = null;
//         }
//         _friendsGameStartTriggered = false;
//     }

//     /// <summary>
//     /// Back-button entry point. Host disband in private lobby; otherwise leave and reset UI.
//     /// </summary>
//     public void LeaveCurrentRoom()
//     {
//         if (_isLeavingRoom) return;

//         if (ShouldDisbandPrivateLobbyAsHost())
//         {
//             DisbandPrivateRoomAsHost();
//             return;
//         }

//         PerformLeaveCurrentRoom();
//     }

//     bool IsPrivateFriendsLobby()
//     {
//         return PhotonNetwork.InRoom
//             && PhotonNetwork.CurrentRoom != null
//             && !PhotonNetwork.CurrentRoom.IsVisible
//             && !PhotonNetwork.OfflineMode
//             && !_onlineMode;
//     }

//     bool IsFriendsMatchStarted()
//     {
//         if (_friendsGameStartTriggered) return true;

//         if (PhotonNetwork.CurrentRoom != null)
//         {
//             if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("ModesLocked", out object ml)
//                 && ml is bool locked && locked)
//                 return true;

//             if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs)
//                 && gs is bool inGame && inGame)
//                 return true;
//         }

//         return GameFlowState.Current == GameFlowPhase.InGame
//             || GameFlowState.Current == GameFlowPhase.Dealing;
//     }

//     bool ShouldDisbandPrivateLobbyAsHost()
//     {
//         return PhotonNetwork.IsMasterClient
//             && IsPrivateFriendsLobby()
//             && !IsFriendsMatchStarted();
//     }

//     void DisbandPrivateRoomAsHost()
//     {
//         Debug.Log("[Friends] Host leaving lobby — disbanding room for all players.");

//         SendFriendsRpc("RPC_Friends_RoomDisbandedByHost", RpcTarget.Others);

//         if (PhotonNetwork.CurrentRoom != null)
//         {
//             PhotonNetwork.CurrentRoom.IsOpen = false;
//             PhotonNetwork.CurrentRoom.SetCustomProperties(
//                 new ExitGames.Client.Photon.Hashtable { { "Disbanded", true } });
//         }

//         PerformLeaveCurrentRoom();
//     }

//     [PunRPC]
//     void RPC_Friends_RoomDisbandedByHost() => HandleRoomDisbandedByHost();

//     void HandleRoomDisbandedByHost()
//     {
//         if (_isLeavingRoom) return;

//         Debug.Log("[Friends] Host disbanded the room — returning Home.");
//         UiFlowManager.MarkReturningHome();
//         StopFriendsGameStartCoroutine();
//         AbortPendingFriendsRoomCreation();
//         PendingJoinPin = null;
//         _pendingSeatLobbyOpen = false;
//         _isLeavingRoom = true;

//         if (FriendsDrawerController.Instance != null)
//             FriendsDrawerController.Instance.CloseDrawer();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.LeaveRoomAndCleanup();
//         else if (PhotonNetwork.InRoom)
//             PhotonNetwork.LeaveRoom();
//         else if (ModeManager.Instance != null)
//             ModeManager.Instance.ReturnToHomeClean();
//     }

//     void PerformLeaveCurrentRoom()
//     {
//         Debug.Log("[UI] BackFromRoom called");

//         AbortPendingFriendsRoomCreation();
//         StopFriendsGameStartCoroutine();
//         PendingJoinPin = null;
//         _pendingSeatLobbyOpen = false;
//         _isLeavingRoom = true;

//         if (FriendsDrawerController.Instance != null)
//             FriendsDrawerController.Instance.CloseDrawer();

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.LeaveRoomAndCleanup();
//             return;
//         }

//         _isLeavingRoom = false;
//         ResetLobbyStateForLeave();
//         if (PhotonNetwork.InRoom)
//             PhotonNetwork.LeaveRoom();
//         else if (ModeManager.Instance != null)
//             ModeManager.Instance.ReturnToHomeClean();
//     }

//     /// <summary>
//     /// Photon callback — fired when WE leave the room. Resets this panel's UI so no ghost state
//     /// (occupied chairs, "Remove Bots" button, stale Room ID) persists into the next session.
//     /// Navigation Home is owned by NetworkManager.OnLeftRoom. Skipped during a leave->join
//     /// transition (accepting an invite / joining a friend's room via PIN).
//     /// </summary>
//     public override void OnLeftRoom()
//     {
//         if (!string.IsNullOrEmpty(PendingJoinPin)) return;
//         _isLeavingFriendsFlow = false;
//         _isLeavingRoom = false;
//         _pendingSeatLobbyOpen = false;
//         ResetSeatPanelUI();
//     }

//     /// <summary>
//     /// Completely resets the seat/lobby UI to its empty state: placeholder Room ID, hidden
//     /// "Remove Bots" button, disabled Start, all chairs emptied, hidden friend-invite button.
//     /// Safe to call repeatedly (idempotent).
//     /// </summary>
//     public void ResetSeatPanelUI()
//     {
//         _onlineMode = false;
//         _friendsGameStartTriggered = false;

//         StopFriendsCoroutineSlot(ref _roomIdRefreshCoroutine, ref _roomIdRefreshRunner);

//         // Room ID back to placeholder (resolve the label by name if it was never wired).
//         if (generatedPinText == null
//             && UiSafeLookup.TryGet("Txt_GeneratedPIN", out GameObject pinGo) && pinGo != null)
//             generatedPinText = pinGo.GetComponent<TMP_Text>();
//         if (generatedPinText != null) generatedPinText.text = "ROOM ID :- ...";

//         // Reset + hide the Include/Remove Bots button.
//         ApplyBotsIncludedState(false);
//         if (includeBotsButton != null) includeBotsButton.SetActive(false);

//         // Disable Start.
//         if (startGameButton != null) SetStartButtonInteractable(false);

//         // Empty all chairs locally (text + avatars).
//         ClearPlayerListUI();
//         ClearSeatAvatars();

//         // Hide the lobby friend-invite button + any error text.
//         EnsureLobbyInviteButton(false);
//         if (errorText != null) errorText.gameObject.SetActive(false);
//     }

//     /// <summary>Dims/empties every seat avatar slot.</summary>
//     void ClearSeatAvatars()
//     {
//         if (playerSlotsAvatar == null) return;
//         for (int i = 0; i < playerSlotsAvatar.Length; i++)
//             SetSeatAvatar(i, -1, false);
//     }

//     /// <summary>Leaves the private (invisible) room if we are currently in one.</summary>
//     public void LeavePrivateRoomIfAny()
//     {
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.LeaveRoomAndCleanup();
//             return;
//         }

//         SuppressSeatLobbyOnJoin = false;
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
//             && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
//         {
//             PhotonNetwork.LeaveRoom();
//         }
//     }

//     public override void OnPlayerEnteredRoom(Player newPlayer)
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

//         bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

//         if (isPrivateFriendsRoom)
//         {
//             _onlineMode = false;
//             _previewBotsInOnlineLobby = false;

//             if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats && areBotsIncluded)
//             {
//                 ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//                 {
//                     { "BotsIncluded", false }
//                 };
//                 PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//             }

//             if (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient)
//             {
//                 Debug.Log($"[Friends] Player joined eager invite-room: {newPlayer.NickName} | count={PhotonNetwork.CurrentRoom.PlayerCount}");
//                 UpdatePlayerListUI();
//                 return;
//             }

//             if (!gameObject.activeSelf) gameObject.SetActive(true);
//             Debug.Log($"[Friends] OnPlayerEnteredRoom | {newPlayer.NickName} | count={PhotonNetwork.CurrentRoom.PlayerCount} | master={PhotonNetwork.MasterClient?.NickName}");
//             UpdatePlayerListUI();
//             CheckPlayerCountAndToggleStart();
//             BeginLobbyPlayerListRefresh();
//             return;
//         }

//         if (_onlineMode)
//         {
//             UpdatePlayerListUI();
//         }
//     }

//     public override void OnPlayerLeftRoom(Player otherPlayer)
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

//         bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

//         if (isPrivateFriendsRoom)
//         {
//             _onlineMode = false;
//             UpdatePlayerListUI();
//             CheckPlayerCountAndToggleStart();
//             return;
//         }

//         if (_onlineMode)
//             UpdatePlayerListUI();
//     }

//     public override void OnMasterClientSwitched(Player newMasterClient)
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;

//         if (IsPrivateFriendsLobby() && !IsFriendsMatchStarted())
//         {
//             Debug.Log("[Friends] Host left before start — disbanding lobby for remaining players.");
//             HandleRoomDisbandedByHost();
//             return;
//         }

//         Debug.Log($"[Friends] MasterClient switched → {newMasterClient?.NickName}");
//         UpdatePlayerListUI();
//         SyncRoomLobbyUIForRole();
//         CheckPlayerCountAndToggleStart();
//     }

//     public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
//     {
//         // Refresh seat avatars when a player's selected avatar arrives/changes.
//         if (changedProps != null && changedProps.ContainsKey(PlayerProfileManager.PROP_AVATAR)
//             && gameObject.activeInHierarchy && PhotonNetwork.InRoom)
//         {
//             UpdatePlayerListUI();
//         }
//     }

//     // ==========================================
//     // SHARE PIN LOGIC
//     // ==========================================

//     public void ShareRoomPIN()
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible)
//             return;

//         string pin = PhotonNetwork.CurrentRoom.Name;
//         string shareMessage = $"Aaja Dehla Pakad khelte hain! Mera Private Room PIN hai: {pin}. Jaldi join kar!";

//         GUIUtility.systemCopyBuffer = shareMessage;
//         Debug.Log("Copied to clipboard: " + shareMessage);

//         if (errorText != null)
//         {
//             errorText.text = "PIN Copied!";
//             errorText.gameObject.SetActive(true);
//         }
//     }

//     // ==========================================
//     // HOST CLICKS START: OPENS MODES PANEL & HIDES FRIENDS PANEL
//     // ==========================================

//     public void OpenModesPanelForHost()
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ResolveModesPanel();
//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(true);
//             CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
//             if (cg != null)
//             {
//                 cg.interactable = true;
//                 cg.blocksRaycasts = true;
//             }
//         }

//         // RPC pehle — panel band karne se pehle clients ko notify karo.
//         // BUG 3 fix: send the DeckManager relay method (the PhotonView we route through
//         // lives on DeckManager, so the target [PunRPC] must exist on that GameObject).
//         SendFriendsRpc("RPC_ShowModesPanelToClients", RpcTarget.Others);

//         if (PhotonNetwork.CurrentRoom != null)
//             PhotonNetwork.CurrentRoom.IsOpen = false;

//         gameObject.SetActive(false);
//     }

//     [PunRPC]
//     void RPC_ShowModesPanelToClients() => ExecuteShowModesPanelToClients();

//     public void ExecuteShowModesPanelToClients()
//     {
//         ResolveModesPanel();
//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(true);
//             CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
//             if (cg != null)
//             {
//                 cg.interactable = false;
//                 cg.blocksRaycasts = false;
//             }
//         }

//         ApplyClientWaitingPresentation(true, "Host is selecting game modes...");

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.ApplyLiveModesFromRoomIfPresent();

//         gameObject.SetActive(false);
//     }

//     // Live sync: 1 Sar=1, 2 Sar=2
//     public void HostSelectedGameMode(int modeIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "GameMode", modeIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // Live sync: 1 Taash=1, 2 Taash=2
//     public void HostSelectedTaashMode(int taashIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "TaashMode", taashIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // Live sync: Spades=1, 13th Card=2, Cut to Trump=3, Cut2Trump=4
//     public void HostSelectedTrumpMode(int trumpIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "TrumpMode", trumpIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // Live sync: Logic A=1, Logic B=2, Logic C=3
//     public void HostSelectedLogicMode(int logicIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "LogicMode", logicIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // New flow: modes are chosen BEFORE the seat panel opens, so the seat panel's
//     // Start button now starts the game directly instead of re-opening the modes panel.
//     public void OpenModesPanel() => OnHostStartFriendsGame();

//     // Backward-compatible alias for Btn_StartPrivateGame
//     public void StartPrivateGame() => OnHostStartFriendsGame();

//     /// <summary>
//     /// Host pressed Start on the seat panel. Only proceeds when the table is full
//     /// (4 players) or bots are included, then routes through the single ModeManager
//     /// start router which performs the private-friends final start.
//     /// </summary>
//     public void OnHostStartFriendsGame()
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
//             return;

//         bool full = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;
//         if (!full)
//         {
//             ShowUIError("Need 4 players to start!");
//             return;
//         }

//         ConfirmHostSeatStart();

//         // CRITICAL FIX: Directly start the game. Modes were already selected before this screen.
//         FinalStartWithSelectedModes();
//     }

//     void ResolveModesPanel()
//     {
//         if (modesPanel == null && ModeManager.Instance != null)
//             modesPanel = ModeManager.Instance.panelModes;
//     }

//     void ResolveHomeMenuPanel()
//     {
//         if (homeMenuPanel != null) return;
//         if (NetworkManager.Instance != null)
//             homeMenuPanel = NetworkManager.Instance.homeMenuPanel;
//         else if (ModeManager.Instance != null)
//             homeMenuPanel = ModeManager.Instance.panelHomeScreen;
//     }

//     void ResolveGameTablePanel()
//     {
//         if (gameTablePanel != null) return;
//         if (NetworkManager.Instance != null)
//             gameTablePanel = NetworkManager.Instance.gameTablePanel;
//         if (gameTablePanel != null) return;
//         if (UiSafeLookup.TryGet("Panel_Game", out GameObject panelGo))
//             gameTablePanel = panelGo;
//         else if (UiSafeLookup.TryGet("[Panel_Game]", out GameObject bracketGo))
//             gameTablePanel = bracketGo;
//         if (gameTablePanel != null && NetworkManager.Instance != null)
//             NetworkManager.Instance.gameTablePanel = gameTablePanel;
//     }

//     // ==========================================
//     // TRAFFIC POLICE: MASTER START BUTTON ROUTER
//     // ==========================================

//     // The Mode Panel Start button must ALWAYS go through the single clean router in ModeManager.
//     // PlayWithFriendsManager must never decide Play Online / Play Bots routing itself.
//     public void OnModePanelStartClicked()
//     {
//         if (ModeManager.Instance != null)
//             ModeManager.Instance.StartGameFromModePanel();
//         else
//             Debug.LogError("[StartRoute] ModeManager.Instance missing — cannot route Mode Panel Start.");
//     }

//     public void OnStartButtonClick() => OnModePanelStartClicked();

//     // ==========================================
//     // FINAL CONFIRM & PLAY (HOST PRESSES START ON MODES PANEL)
//     // ==========================================

//     public void FinalStartWithSelectedModes()
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
//         if (GameSettings.Instance == null) return;
//         if (_friendsGameStartTriggered) return;

//         if (startGameButton != null) SetStartButtonInteractable(false);

//         Debug.Log("Host pressed Final Start! Telling everyone to start the game...");

//         ExitGames.Client.Photon.Hashtable customRoomProperties = new ExitGames.Client.Photon.Hashtable();

//         if (ModeManager.Instance != null)
//         {
//             customRoomProperties["TM"] = ModeManager.Instance.currentTrickMode;
//             customRoomProperties["RM"] = ModeManager.Instance.currentTrumpMode;
//             customRoomProperties["SM"] = ModeManager.Instance.currentSarMode;
//             customRoomProperties["LM"] = ModeManager.Instance.currentLogicMode;
//         }
//         else
//         {
//             customRoomProperties["TM"] = GameSettings.Instance.taashCategory;
//             customRoomProperties["RM"] = 3;
//             customRoomProperties["SM"] = GameSettings.Instance.currentSarMode == SarModeType.TwoSar ? 2 : 1;
//             customRoomProperties["LM"] = 1;
//         }

//         customRoomProperties["ModesLocked"] = true;
//         customRoomProperties["GS"] = true;
//         customRoomProperties["BotsIncluded"] = areBotsIncluded;
//         customRoomProperties["HAN"] = PhotonNetwork.MasterClient.ActorNumber;

//         int realPlayers = PhotonNetwork.CurrentRoom.PlayerCount;
//         int botsNeeded = DeckManager.MaxTableSeats - realPlayers;
//         DeckManager.botActorNumbers.Clear();
//         for (int i = 0; i < botsNeeded; i++)
//             DeckManager.botActorNumbers.Add(100 + i);

//         int deckSeed = UnityEngine.Random.Range(1, int.MaxValue);
//         customRoomProperties["DS"] = deckSeed;
//         DeckManager.SetSharedDeckSeed(deckSeed);
//         customRoomProperties["BS"] = DeckManager.botActorNumbers.ToArray();

//         int[] realActorNumbers = new int[realPlayers];
//         Player[] sortedPlayers = PhotonRoomPlayers.GetSorted();
//         for (int i = 0; i < realPlayers && i < sortedPlayers.Length; i++)
//             realActorNumbers[i] = sortedPlayers[i].ActorNumber;
//         customRoomProperties["RPA"] = realActorNumbers;

//         if (DeckManager.Instance != null)
//             customRoomProperties["SMP"] = DeckManager.Instance.BuildActiveSeatList().ToArray();

//         PhotonNetwork.CurrentRoom.SetCustomProperties(customRoomProperties);

//         PhotonNetwork.CurrentRoom.IsOpen = false;
//         PhotonNetwork.CurrentRoom.IsVisible = false;

//         if (botsNeeded > 0 && DeckManager.Instance != null)
//         {
//             DeckManager.Instance.photonView.RPC(
//                 "RPC_SyncBotsOnly",
//                 RpcTarget.All,
//                 DeckManager.botActorNumbers.ToArray());
//         }

//         Debug.Log($"[Friends] Host Start | room={PhotonNetwork.CurrentRoom.Name} | realPlayers={realPlayers} | bots={botsNeeded}");

//         SendFriendsRpc("RPC_StartGameForEveryone", RpcTarget.All);
//         ExecuteFriendsGameStart();
//     }

//     [PunRPC]
//     void RPC_StartGameForEveryone() => ExecuteFriendsGameStart();

//     void ApplyFriendsStartFromRoomProperties()
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;

//         DeckManager.SyncBotSeatsFromRoomProperties();

//         if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BS", out object bsObj)
//             && bsObj is int[] bs
//             && bs.Length > 0
//             && DeckManager.botActorNumbers.Count == 0)
//         {
//             for (int i = 0; i < bs.Length; i++)
//                 DeckManager.botActorNumbers.Add(bs[i]);
//         }

//         if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DS", out object dsObj)
//             && dsObj != null && int.TryParse(dsObj.ToString(), out int ds) && ds != 0)
//             DeckManager.SetSharedDeckSeed(ds);
//     }

//     public void ExecuteFriendsGameStart()
//     {
//         if (_friendsGameStartTriggered) return;
//         _friendsGameStartTriggered = true;

//         Debug.Log("[GameStart] Friends RPC_StartGameForEveryone received");

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.SyncModesFromRoom();

//         ApplyFriendsStartFromRoomProperties();

//         if (TrumpManager.Instance != null)
//         {
//             if (DeckManager.IsPrivateFriendsRoom())
//                 TrumpManager.Instance.RefreshFromRoomProperties(false);
//             else
//                 TrumpManager.ApplyTrumpForCurrentGameMode(false);
//         }

//         if (PhotonNetwork.IsMasterClient)
//         {
//             DeckManager.botActorNumbers.Clear();

//             if (PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj)
//                 && botsObj is bool botsOn)
//                 areBotsIncluded = botsOn;

//             if (PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BS", out object bsObj)
//                 && bsObj is int[] bsFromRoom)
//             {
//                 for (int i = 0; i < bsFromRoom.Length; i++)
//                     DeckManager.botActorNumbers.Add(bsFromRoom[i]);
//             }
//             else
//             {
//                 int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
//                 int botsNeeded = DeckManager.MaxTableSeats - realPlayerCount;
//                 for (int i = 0; i < botsNeeded; i++)
//                     DeckManager.botActorNumbers.Add(100 + i);
//             }

//             Debug.Log($"[Bot System] Master sync — real={PhotonNetwork.CurrentRoom.PlayerCount}, bots={DeckManager.botActorNumbers.Count}");
//         }
//         else
//         {
//             if (PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object inc)
//                 && inc is bool included)
//                 areBotsIncluded = included;

//             if (DeckManager.botActorNumbers.Count == 0)
//                 ApplyFriendsStartFromRoomProperties();
//         }

//         GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
//         UiFlowManager.MarkInGame();

//         // Run on NetworkManager (persistent) — disabling this panel must not kill the coroutine.
//         if (NetworkManager.Instance != null)
//         {
//             if (_smoothGameStartCoroutine != null)
//             {
//                 NetworkManager.Instance.StopCoroutine(_smoothGameStartCoroutine);
//                 _smoothGameStartCoroutine = null;
//             }
//             _smoothGameStartCoroutine = NetworkManager.Instance.StartCoroutine(SmoothGameStartRoutine());
//         }
//     }

//     IEnumerator SmoothGameStartRoutine()
//     {
//         const float waitDuration = 1.5f;

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.ShowLoading("Starting Game...");
//             NetworkManager.Instance.AnimateLoadingSlider(waitDuration);
//         }

//         ResolveGameTablePanel();
//         ResolveModesPanel();
//         if (modesPanel != null) modesPanel.SetActive(false);
//         HidePrivateFriendsLobbyUI();
//         if (ModeManager.Instance != null)
//             ModeManager.Instance.HidePlayWithFriendsPanel();

//         yield return new WaitForSeconds(waitDuration);

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.CompleteLoadingSlider();
//             NetworkManager.Instance.ResetGameStartGuards();
//             NetworkManager.Instance.EnsureLocalNetworkPlayer();
//             PlayerHand.ResolveLocalHand();
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ForceClearBlackOverlay();
//             NetworkManager.Instance.BeginGameAfterRoomReady(showLoadingOverlay: false);
//         }

//         Debug.Log("[Friends] Game scene loaded smoothly!");
//         _smoothGameStartCoroutine = null;
//     }

//     void HidePlayWithFriendsLobbyPanel()
//     {
//         if (pinCreationPanel != null) pinCreationPanel.SetActive(false);
//         if (startGameButton != null) startGameButton.SetActive(false);
//     }

//     public void HidePrivateFriendsLobbyUI()
//     {
//         HidePlayWithFriendsLobbyPanel();
//         if (errorText != null) errorText.gameObject.SetActive(false);
//     }

//     public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
//     {
//         if (propertiesThatChanged == null || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible) return;

//         if (propertiesThatChanged.ContainsKey("ModesLocked")
//             && propertiesThatChanged["ModesLocked"] is bool locked
//             && locked)
//         {
//             // Backup path if the RPC was missed; ExecuteFriendsGameStart is idempotent.
//             ExecuteFriendsGameStart();
//             Debug.Log("Host locked the modes!");
//             return;
//         }

//         if (propertiesThatChanged.ContainsKey("Disbanded")
//             && propertiesThatChanged["Disbanded"] is bool disbanded
//             && disbanded)
//         {
//             HandleRoomDisbandedByHost();
//             return;
//         }

//         if (propertiesThatChanged.ContainsKey("HAN")
//             || propertiesThatChanged.ContainsKey("BS")
//             || propertiesThatChanged.ContainsKey("BotsIncluded")
//             || propertiesThatChanged.ContainsKey("DS"))
//         {
//             ApplyFriendsStartFromRoomProperties();
//             UpdatePlayerListUI();
//             SyncRoomLobbyUIForRole();
//             CheckPlayerCountAndToggleStart();
//         }

//         if (ModeManager.Instance == null) return;

//         if (propertiesThatChanged.TryGetValue("GameMode", out object gameModeObj) && gameModeObj is int selectedMode)
//         {
//             ModeManager.Instance.OnClick_SarMode(selectedMode, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("TrumpMode", out object trumpModeObj) && trumpModeObj is int selectedTrump)
//         {
//             ModeManager.Instance.OnClick_TrumpMode(selectedTrump, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("TaashMode", out object taashModeObj) && taashModeObj is int selectedTaash)
//         {
//             ModeManager.Instance.OnClick_TrickMode(selectedTaash, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("LogicMode", out object logicModeObj) && logicModeObj is int selectedLogic)
//         {
//             ModeManager.Instance.OnClick_LogicMode(selectedLogic, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("BotsIncluded", out object botsChangedObj) && botsChangedObj is bool botsChangedVal)
//         {
//             ApplyBotsIncludedState(botsChangedVal);
//             UpdatePlayerListUI();
//             CheckPlayerCountAndToggleStart();
//         }

//         if (propertiesThatChanged.ContainsKey("BS"))
//             ApplyFriendsStartFromRoomProperties();

//         if (propertiesThatChanged.ContainsKey("HAN"))
//             UpdatePlayerListUI();

//         if (propertiesThatChanged.ContainsKey("GS")
//             && propertiesThatChanged["GS"] is bool started
//             && started
//             && !_friendsGameStartTriggered)
//         {
//             ExecuteFriendsGameStart();
//         }
//     }

//     // ==========================================
//     // 6. FRIENDS LIST LOGIC
//     // ==========================================

//     public void DisplayMyID()
//     {
//         ResolveMyUserIdText();
//         if (myUserIdText == null) return;

//         // Show the short public UID (PUBG / Free Fire style). Tap to copy it.
//         string uid = GameUidService.LocalGameUid;
//         UidUI.BindCopyLabel(myUserIdText, uid, "My UID: ");
//     }

//     void ResolveMyUserIdText()
//     {
//         if (myUserIdText != null) return;

//         // The Friends panel header has a "Text_MyID" label that may not be wired in the inspector.
//         if (FriendsPanelUIController.Instance != null)
//         {
//             foreach (Transform t in FriendsPanelUIController.Instance.GetComponentsInChildren<Transform>(true))
//             {
//                 if (t.name == "Text_MyID")
//                 {
//                     myUserIdText = t.GetComponent<TMP_Text>();
//                     break;
//                 }
//             }
//         }
//     }

//     public void UI_AddFriendBtnClicked()
//     {
//         if (addFriendInput == null) return;

//         string newFriendId = addFriendInput.text.Trim();
//         if (string.IsNullOrEmpty(newFriendId)) return;

//         SendFriendRequest(newFriendId, null);
//         addFriendInput.text = "";
//     }

//     public void AddFriend(string friendUserId, string displayName = null)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;

//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId) && friendUserId == myId)
//         {
//             ShowUIError("You cannot add yourself!");
//             return;
//         }

//         if (myFriends.Contains(friendUserId))
//         {
//             ShowUIError("Already in friends list.");
//             return;
//         }

//         myFriends.Add(friendUserId);
//         if (!string.IsNullOrEmpty(displayName))
//             friendDisplayNames[friendUserId] = displayName;
//         else if (!friendDisplayNames.ContainsKey(friendUserId))
//             friendDisplayNames[friendUserId] = friendUserId;

//         SaveFriends();
//         RefreshFriendsListUI();
//         CheckFriendsOnlineStatus();
//         Debug.Log($"[Friends] Added {friendDisplayNames[friendUserId]} ({friendUserId})");
//     }

//     /// <summary>
//     /// Task 24 — After a player is replaced/kicked, re-poll friend status a few times so the
//     /// replaced player stops showing "In Game" promptly, instead of waiting for the 45s heartbeat.
//     /// "In game" is derived from Photon room membership (FindFriends IsInRoom), so a few quick
//     /// re-polls reflect the leave as soon as the kick propagates server-side.
//     /// </summary>
//     public void RefreshInGameStatusSoon()
//     {
//         if (isActiveAndEnabled)
//             StartCoroutine(RefreshInGameStatusRoutine());
//         else if (SocialServiceBootstrap.Instance != null)
//             SocialServiceBootstrap.Instance.StartCoroutine(RefreshInGameStatusRoutine());
//     }

//     IEnumerator RefreshInGameStatusRoutine()
//     {
//         for (int i = 0; i < 3; i++)
//         {
//             yield return new WaitForSeconds(1f);
//             CheckFriendsOnlineStatus();
//         }
//     }

//     void StartPresenceHeartbeat()
//     {
//         PublishOwnPresence();
//         if (_presenceHeartbeatCoroutine != null) return;

//         if (isActiveAndEnabled)
//             _presenceHeartbeatCoroutine = StartCoroutine(PresenceHeartbeatRoutine());
//         else if (SocialServiceBootstrap.Instance != null)
//             _presenceHeartbeatCoroutine = SocialServiceBootstrap.Instance.StartCoroutine(PresenceHeartbeatRoutine());
//     }

//     IEnumerator PresenceHeartbeatRoutine()
//     {
//         var wait = new WaitForSeconds(45f);
//         while (true)
//         {
//             yield return wait;
//             PublishOwnPresence();
//         }
//     }

//     void PublishOwnPresence()
//     {
//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
//         var data = new Dictionary<string, object>
//         {
//             { "lastActive", now },
//             { "online", true }
//         };

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("users").Child(myId).Child("presence")
//             .UpdateChildrenAsync(data);
//     }

//     static bool CanCallFindFriends()
//     {
//         if (PhotonNetwork.OfflineMode) return false;
//         if (!PhotonNetwork.IsConnectedAndReady) return false;
//         if (PhotonNetwork.Server != ServerConnection.MasterServer) return false;

//         ClientState state = PhotonNetwork.NetworkClientState;
//         return state == ClientState.ConnectedToMasterServer || state == ClientState.JoinedLobby;
//     }

//     void ScheduleFindFriendsWhenReady()
//     {
//         if (_findFriendsCoroutine != null) return;

//         if (isActiveAndEnabled)
//             _findFriendsCoroutine = StartCoroutine(WaitForPhotonThenFindFriends());
//         else if (SocialServiceBootstrap.Instance != null)
//             _findFriendsCoroutine = SocialServiceBootstrap.Instance.StartCoroutine(WaitForPhotonThenFindFriends());
//     }

//     IEnumerator WaitForPhotonThenFindFriends()
//     {
//         var wait = new WaitForSeconds(0.25f);
//         for (int i = 0; i < 80; i++)
//         {
//             if (CanCallFindFriends())
//             {
//                 _findFriendsCoroutine = null;
//                 if (myFriends != null && myFriends.Count > 0)
//                     PhotonNetwork.FindFriends(myFriends.ToArray());
//                 yield break;
//             }
//             yield return wait;
//         }

//         _findFriendsCoroutine = null;
//     }

//     /// <summary>
//     /// Removes a user from the local friends list (used by the in-game player-stats popup
//     /// REMOVE action). Persists the change and refreshes any friends UI.
//     /// </summary>
//     public void RemoveFriend(string friendUserId)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;
//         if (!myFriends.Remove(friendUserId)) return;

//         friendDisplayNames.Remove(friendUserId);
//         _gameInvitesSent.Remove(friendUserId);

//         SaveFriends();
//         RefreshFriendsListUI();
//         CheckFriendsOnlineStatus();
//         Debug.Log($"[Friends] Removed {friendUserId}");
//     }

//     /// <summary>True if the given user id is already in the local friends list.</summary>
//     public bool IsFriend(string friendUserId) =>
//         !string.IsNullOrEmpty(friendUserId) && myFriends.Contains(friendUserId);

//     // ==========================================
//     // FRIEND REQUEST SYSTEM (Accept / Decline)
//     // ==========================================

//     string MyUserId
//     {
//         get
//         {
//             if (FirebaseAuth.DefaultInstance?.CurrentUser != null)
//                 return FirebaseAuth.DefaultInstance.CurrentUser.UserId;

//             return PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId ?? "";
//         }
//     }

//     string MyDisplayName
//     {
//         get
//         {
//             string savedName = PlayerPrefs.GetString("PlayerUsername", "");
//             if (!string.IsNullOrEmpty(savedName)) return savedName;

//             return string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Player" : PhotonNetwork.NickName;
//         }
//     }

//     /// <summary>Sends a friend request to the target user (they get Accept/Decline).</summary>
//     public void SendFriendRequest(string targetUserId, string targetName, System.Action<bool> onComplete = null)
//     {
//         if (string.IsNullOrEmpty(targetUserId))
//         {
//             onComplete?.Invoke(false);
//             return;
//         }
//         targetUserId = targetUserId.Trim();

//         if (FirebaseAuth.DefaultInstance?.CurrentUser == null && !Application.isEditor)
//         {
//             ShowUIError("Sign in required to send friend requests.");
//             onComplete?.Invoke(false);
//             return;
//         }

//         EnsurePhotonUserId();
//         EnsureFriendServicesStarted();

//         // The whole friend system keys on the account id (Firebase uid / Photon UserId).
//         // But the UID users see and type is the short 10-digit public GameUid. If the caller
//         // passed a GameUid (e.g. from the home "Add by UID" box), resolve it to the account id
//         // first — otherwise the request is written to a path nobody listens on and is lost.
//         if (GameUidService.LooksLikeUid(targetUserId))
//         {
//             GameUidService.ResolveFirebaseUid(targetUserId, resolved =>
//             {
//                 if (string.IsNullOrEmpty(resolved))
//                 {
//                     ShowUIError("No player found with that UID.");
//                     onComplete?.Invoke(false);
//                     return;
//                 }
//                 SendFriendRequest(resolved, targetName, onComplete);
//             });
//             return;
//         }

//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId) && targetUserId == myId)
//         {
//             ShowUIError("You cannot add yourself!");
//             onComplete?.Invoke(false);
//             return;
//         }

//         if (myFriends.Contains(targetUserId))
//         {
//             ShowUIError("Already in your friends list.");
//             onComplete?.Invoke(false);
//             return;
//         }

//         if (incomingRequests.ContainsKey(targetUserId))
//         {
//             AcceptFriendRequest(targetUserId, incomingRequests[targetUserId]);
//             onComplete?.Invoke(true);
//             return;
//         }

//         if (string.IsNullOrEmpty(myId))
//         {
//             ShowUIError("Not connected yet. Try again.");
//             onComplete?.Invoke(false);
//             return;
//         }

//         // Remember name locally so it shows correctly once accepted.
//         if (!string.IsNullOrEmpty(targetName))
//             friendDisplayNames[targetUserId] = targetName;

//         var requestData = new Dictionary<string, object>
//         {
//             { "fromUserId", myId },
//             { "fromName", MyDisplayName },
//             { "createdAt", System.DateTime.UtcNow.Ticks }
//         };

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("friend_requests").Child(targetUserId).Child(myId)
//             .SetValueAsync(requestData).ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted)
//                 {
//                     Debug.LogError("[FriendReq] Send failed: " + task.Exception);
//                     ShowUIError("Request failed. Try again.");
//                     onComplete?.Invoke(false);
//                     return;
//                 }
//                 ShowUIError(string.IsNullOrEmpty(targetName) ? "Friend request sent!" : $"Request sent to {targetName}!");
//                 Debug.Log($"[FriendReq] Sent request to {targetUserId} from {myId}");
//                 onComplete?.Invoke(true);
//             });
//     }

//     public void StartFriendRequestListener()
//     {
//         if (_requestListenerStarted) return;
//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         requestDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("friend_requests").Child(myId);
//         requestDbRef.ChildAdded += OnFriendRequestAdded;
//         requestDbRef.ChildRemoved += OnFriendRequestRemoved;
//         _requestListenerStarted = true;
//         Debug.Log("[FriendReq] Listening for friend requests on " + myId);

//         requestDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

//             foreach (DataSnapshot child in task.Result.Children)
//             {
//                 string fromId = child.Key;
//                 if (string.IsNullOrEmpty(fromId) || myFriends.Contains(fromId)) continue;
//                 string fromName = child.Child("fromName").Value?.ToString();
//                 if (string.IsNullOrEmpty(fromName))
//                     fromName = child.Child("fromUserId").Value?.ToString() ?? fromId;
//                 if (string.IsNullOrEmpty(fromName))
//                     fromName = fromId;
//                 incomingRequests[fromId] = fromName;
//             }

//             RefreshFriendsListUI();
//         });
//     }

//     void OnFriendRequestAdded(object sender, ChildChangedEventArgs args)
//     {
//         if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;

//         string fromId = args.Snapshot.Key;
//         if (string.IsNullOrEmpty(fromId) || myFriends.Contains(fromId)) return;

//         string fromName = args.Snapshot.Child("fromName").Value?.ToString();
//         if (string.IsNullOrEmpty(fromName))
//             fromName = args.Snapshot.Child("fromUserId").Value?.ToString() ?? fromId;
//         if (string.IsNullOrEmpty(fromName))
//             fromName = fromId;
//         incomingRequests[fromId] = fromName;
//         Debug.Log($"[FriendReq] Incoming request from {fromName} ({fromId})");
//         RefreshFriendsListUI();
//         NotifyRequestsChanged();

//         if (FriendsPanelUIController.Instance != null)
//             FriendsPanelUIController.Instance.ShowTab(FriendsPanelUIController.PanelTab.Requests);
//     }

//     void OnFriendRequestRemoved(object sender, ChildChangedEventArgs args)
//     {
//         if (args.Snapshot == null) return;
//         string fromId = args.Snapshot.Key;
//         if (!string.IsNullOrEmpty(fromId) && incomingRequests.Remove(fromId))
//             RefreshFriendsListUI();
//     }

//     public void AcceptFriendRequest(string fromUserId, string fromName)
//     {
//         if (string.IsNullOrEmpty(fromUserId)) return;

//         // Add them to MY friends list locally.
//         AddFriend(fromUserId, fromName);

//         // Phase 5 — persist this friendship to Firebase so it survives re-login.
//         WriteFriendToFirebase(fromUserId, fromName);

//         // Tell the requester that I accepted so they add me back.
//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId))
//         {
//             var acceptData = new Dictionary<string, object>
//             {
//                 { "name", MyDisplayName },
//                 { "createdAt", System.DateTime.UtcNow.Ticks }
//             };
//             FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//                 .Child("friend_accepts").Child(fromUserId).Child(myId)
//                 .SetValueAsync(acceptData);

//             // Remove the pending request from my inbox.
//             FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//                 .Child("friend_requests").Child(myId).Child(fromUserId)
//                 .RemoveValueAsync();
//         }

//         incomingRequests.Remove(fromUserId);
//         ShowUIError($"You and {fromName} are now friends!");
//         RefreshFriendsListUI();
//     }

//     public void DeclineFriendRequest(string fromUserId)
//     {
//         if (string.IsNullOrEmpty(fromUserId)) return;

//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId))
//         {
//             FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//                 .Child("friend_requests").Child(myId).Child(fromUserId)
//                 .RemoveValueAsync();
//         }

//         incomingRequests.Remove(fromUserId);
//         RefreshFriendsListUI();
//         NotifyRequestsChanged();
//     }

//     public void StartFriendAcceptListener()
//     {
//         if (_acceptListenerStarted) return;
//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         acceptDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("friend_accepts").Child(myId);
//         acceptDbRef.ChildAdded += OnFriendAcceptAdded;
//         _acceptListenerStarted = true;
//         Debug.Log("[FriendReq] Listening for friend acceptances on " + myId);

//         acceptDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

//             foreach (DataSnapshot child in task.Result.Children)
//             {
//                 if (!child.Exists) continue;
//                 string accepterId = child.Key;
//                 string accepterName = child.Child("name").Value?.ToString() ?? accepterId;
//                 if (!string.IsNullOrEmpty(accepterId) && !myFriends.Contains(accepterId))
//                 {
//                     AddFriend(accepterId, accepterName);
//                     // Phase 5 — persist this friendship to Firebase (requester side).
//                     WriteFriendToFirebase(accepterId, accepterName);
//                 }
//                 child.Reference.RemoveValueAsync();
//             }

//             RefreshFriendsListUI();
//         });
//     }

//     void OnFriendAcceptAdded(object sender, ChildChangedEventArgs args)
//     {
//         if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;

//         string accepterId = args.Snapshot.Key;
//         if (string.IsNullOrEmpty(accepterId)) return;

//         string accepterName = args.Snapshot.Child("name").Value?.ToString() ?? accepterId;
//         AddFriend(accepterId, accepterName);
//         // Phase 5 — persist this friendship to Firebase (requester side).
//         WriteFriendToFirebase(accepterId, accepterName);
//         ShowUIError($"{accepterName} accepted your request!");

//         // Consume the acceptance notice.
//         args.Snapshot.Reference.RemoveValueAsync();
//     }

//     public override void OnFriendListUpdate(List<FriendInfo> friendList)
//     {
//         friendPhotonStatus.Clear();
//         foreach (FriendInfo friend in friendList)
//             friendPhotonStatus[friend.UserId] = friend;

//         RefreshFriendsListUI();
//     }

//     public void RefreshFriendsListUI()
//     {
//         // TASK 18/25: notify any open in-game friend panels so they repaint with live presence.
//         NotifyFriendsStatusChanged();

//         if (FriendsPanelUIController.Instance != null)
//         {
//             FriendsPanelUIController.Instance.RefreshAll();
//             return;
//         }

//         RefreshFriendsListLegacy();
//     }

//     void RefreshFriendsListLegacy()
//     {
//         if (friendsListContainer == null || friendUIPrefab == null) return;

//         foreach (Transform child in friendsListContainer)
//             Destroy(child.gameObject);

//         foreach (var kvp in incomingRequests)
//         {
//             if (string.IsNullOrEmpty(kvp.Key)) continue;
//             SpawnRequestRow(kvp.Key, kvp.Value);
//         }

//         // 2) Then the accepted friends (with status + Invite).
//         foreach (string friendId in myFriends)
//         {
//             if (string.IsNullOrEmpty(friendId)) continue;
//             friendPhotonStatus.TryGetValue(friendId, out FriendInfo photonInfo);
//             SpawnFriendRow(friendId, GetFriendDisplayNameInternal(friendId), photonInfo);
//         }
//     }

//     void SpawnRequestRow(string fromId, string fromName)
//     {
//         GameObject prefab = friendRequestRowPrefab != null ? friendRequestRowPrefab : friendUIPrefab;
//         if (prefab == null || friendsListContainer == null) return;

//         GameObject row = Instantiate(prefab, friendsListContainer);

//         TMP_Text infoText = FindPrimaryLabel(row.transform);
//         if (infoText != null)
//             infoText.text = $"{fromName}\n<size=18><color=#FFD479>wants to be friends</color></size>";

//         Button acceptBtn = FindChildButton(row.transform, "AcceptButton");
//         Button declineBtn = FindChildButton(row.transform, "DeclineButton");

//         // Fallback: if named buttons not found, assume first=accept, second=decline.
//         if (acceptBtn == null || declineBtn == null)
//         {
//             Button[] buttons = row.GetComponentsInChildren<Button>(true);
//             if (buttons.Length >= 2)
//             {
//                 acceptBtn = acceptBtn ?? buttons[0];
//                 declineBtn = declineBtn ?? buttons[1];
//             }
//         }

//         if (acceptBtn != null)
//         {
//             acceptBtn.onClick.RemoveAllListeners();
//             acceptBtn.onClick.AddListener(() => AcceptFriendRequest(fromId, fromName));
//         }
//         if (declineBtn != null)
//         {
//             declineBtn.onClick.RemoveAllListeners();
//             declineBtn.onClick.AddListener(() => DeclineFriendRequest(fromId));
//         }
//     }

//     string GetFriendDisplayNameInternal(string friendId)
//     {
//         if (friendDisplayNames.TryGetValue(friendId, out string name) && !string.IsNullOrEmpty(name))
//             return name;
//         return friendId;
//     }

//     void SpawnFriendRow(string friendId, string displayName, FriendInfo photonInfo)
//     {
//         GameObject row = Instantiate(friendUIPrefab, friendsListContainer);

//         TMP_Text friendText = FindPrimaryLabel(row.transform);
//         bool online = IsFriendOnline(friendId);
//         bool inGame = IsFriendInGame(friendId);
//         string status = "🔴 Offline";
//         if (online)
//             status = inGame ? "🎮 In Game" : "🟢 Online";

//         if (friendText != null)
//         {
//             friendText.text = $"{displayName}\n{status}";
//             friendText.color = online ? Color.green : Color.gray;
//         }

//         Button inviteBtn = FindChildButton(row.transform, "InviteButton");
//         if (inviteBtn == null)
//         {
//             Button[] buttons = row.GetComponentsInChildren<Button>(true);
//             inviteBtn = buttons.Length > 0 ? buttons[buttons.Length - 1] : null;
//         }

//         if (inviteBtn != null)
//         {
//             inviteBtn.onClick.RemoveAllListeners();
//             inviteBtn.onClick.AddListener(() => InviteFriendToGame(friendId, displayName));
//             TMP_Text inviteLabel = inviteBtn.GetComponentInChildren<TMP_Text>();
//             if (inviteLabel != null) inviteLabel.text = "Invite";
//         }
//     }

//     static TMP_Text FindPrimaryLabel(Transform root)
//     {
//         TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
//         for (int i = 0; i < labels.Length; i++)
//         {
//             if (labels[i].GetComponentInParent<Button>() == null)
//                 return labels[i];
//         }
//         return labels.Length > 0 ? labels[0] : null;
//     }

//     static Button FindChildButton(Transform root, string childName)
//     {
//         foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
//         {
//             if (t.name == childName)
//                 return t.GetComponent<Button>();
//         }
//         return null;
//     }

//     public void InviteFriendToGame(string friendUserId, string friendDisplayName = null)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;
//         if (!PhotonNetwork.IsConnectedAndReady)
//         {
//             ShowUIError("Server not ready. Wait for connection...");
//             return;
//         }

//         _pendingInviteFriendId = friendUserId;
//         _pendingInviteFriendName = string.IsNullOrEmpty(friendDisplayName)
//             ? GetFriendDisplayNameInternal(friendUserId)
//             : friendDisplayName;

//         // Offline practice tables are local-only — a real friend can never join them.
//         if (PhotonNetwork.OfflineMode)
//         {
//             ShowUIError("Can't invite friends in practice mode.");
//             _pendingInviteFriendId = null;
//             _pendingInviteFriendName = null;
//             return;
//         }

//         // Already seated at a real Photon table (online matchmaking room OR a private friends
//         // room): invite the friend straight into THIS table. We must NOT leave the room here —
//         // LeaveRoom fires OnLeftRoom -> ReturnToHomeScreen and drops the host back to the home
//         // screen. That was the old REPLACE -> homepage bug.
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
//         {
//             // The table may have been closed when the match started; reopen it so the invited
//             // friend can join and take a bot seat (DeckManager.OnPlayerEnteredRoom hands it over).
//             if (!PhotonNetwork.CurrentRoom.IsOpen)
//                 PhotonNetwork.CurrentRoom.IsOpen = true;

//             SendFirebaseInvite(_pendingInviteFriendId, PhotonNetwork.CurrentRoom.Name, _pendingInviteFriendName);
//             _pendingInviteFriendId = null;
//             _pendingInviteFriendName = null;
//             return;
//         }

//         // Not in any room (inviting from the home screen): spin up a private room. The pending
//         // invite is sent automatically once we join it (OnJoinedRoom -> TrySendPendingInvite).
//         CreatePrivateRoom();
//         ShowUIError("Creating room for invite...");
//     }

//     void TrySendPendingInvite()
//     {
//         if (string.IsNullOrEmpty(_pendingInviteFriendId)) return;
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible) return;

//         SendFirebaseInvite(_pendingInviteFriendId, PhotonNetwork.CurrentRoom.Name, _pendingInviteFriendName);
//         _pendingInviteFriendId = null;
//         _pendingInviteFriendName = null;
//     }

//     void SendFirebaseInvite(string targetUserId, string roomPin, string friendName)
//     {
//         if (string.IsNullOrEmpty(targetUserId) || string.IsNullOrEmpty(roomPin)) return;

//         string fromId = MyUserId;
//         string fromName = MyDisplayName;

//         var inviteData = new Dictionary<string, object>
//         {
//             { "roomPin", roomPin },
//             { "fromUserId", fromId },
//             { "fromName", fromName },
//             { "timestamp", System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
//         };

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("invites").Child(targetUserId).Child(roomPin)
//             .SetValueAsync(inviteData).ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted)
//                 {
//                     Debug.LogError("[Invite] Firebase send failed: " + task.Exception);
//                     ShowUIError("Invite failed. Try again.");
//                     return;
//                 }

//                 MarkGameInviteSent(targetUserId);
//                 RefreshFriendsListUI();
//                 ShowUIError($"Invite sent to {friendName}!");
//                 Debug.Log($"[Invite] Sent room {roomPin} to {targetUserId}");
//             });
//     }

//     public void StartInviteListener()
//     {
//         if (_inviteListenerStarted) return;

//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         inviteDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("invites").Child(myId);
//         inviteDbRef.ChildAdded += OnIncomingInviteAdded;
//         _inviteListenerStarted = true;
//         Debug.Log("[Invite] Listening for invites on " + myId);

//         inviteDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

//             foreach (DataSnapshot child in task.Result.Children)
//                 TryRegisterIncomingInviteSnapshot(child);
//         });
//     }

//     void TryRegisterIncomingInviteSnapshot(DataSnapshot snapshot)
//     {
//         if (snapshot == null || !snapshot.Exists) return;

//         string inviteId = snapshot.Key;
//         string roomPin = snapshot.Child("roomPin").Value?.ToString();
//         string fromName = snapshot.Child("fromName").Value?.ToString() ?? "Friend";
//         string fromUserId = snapshot.Child("fromUserId").Value?.ToString();
//         if (string.IsNullOrEmpty(roomPin)) roomPin = inviteId;
//         if (string.IsNullOrEmpty(inviteId)) inviteId = roomPin;
//         if (string.IsNullOrEmpty(roomPin)) return;

//         if (IsInviteExpired(snapshot))
//         {
//             Debug.Log($"[Invite] Invite expired ({InviteExpirySeconds}s) — popup skipped for '{inviteId}'.");
//             RemoveInviteFromFirebase(inviteId);
//             return;
//         }

//         RegisterPendingInvite(inviteId, roomPin, fromName, fromUserId);
//         ShowIncomingInvite(fromName, roomPin, inviteId);
//     }

//     static bool IsInviteExpired(DataSnapshot snapshot)
//     {
//         long inviteTimestamp = ReadInviteTimestamp(snapshot);
//         if (inviteTimestamp <= 0) return false;

//         // Milliseconds from newer clients — normalize to seconds for comparison.
//         if (inviteTimestamp > 100_000_000_000L)
//             inviteTimestamp /= 1000;

//         long currentTimeSeconds = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
//         long diffSeconds = currentTimeSeconds - inviteTimestamp;

//         // Expired after 15s, or clock skew / bad legacy data (negative drift).
//         return diffSeconds > InviteExpirySeconds || diffSeconds < -InviteExpirySeconds;
//     }

//     static long ReadInviteTimestamp(DataSnapshot snapshot)
//     {
//         if (snapshot == null) return 0;

//         DataSnapshot tsNode = snapshot.Child("timestamp");
//         if (tsNode.Exists && tsNode.Value != null
//             && long.TryParse(tsNode.Value.ToString(), out long unixTs))
//             return unixTs;

//         // Legacy field from older builds (Ticks or unix seconds).
//         DataSnapshot createdNode = snapshot.Child("createdAt");
//         if (!createdNode.Exists || createdNode.Value == null) return 0;
//         if (!long.TryParse(createdNode.Value.ToString(), out long raw)) return 0;

//         if (raw > 1_000_000_000_000L)
//             return new System.DateTimeOffset(new System.DateTime(raw, System.DateTimeKind.Utc)).ToUnixTimeSeconds();

//         return raw;
//     }

//     void OnIncomingInviteAdded(object sender, ChildChangedEventArgs args)
//     {
//         if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;
//         TryRegisterIncomingInviteSnapshot(args.Snapshot);
//     }

//     void RegisterPendingInvite(string inviteId, string roomPin, string fromName, string fromUserId)
//     {
//         if (string.IsNullOrEmpty(inviteId) || string.IsNullOrEmpty(roomPin)) return;

//         _pendingGameInvites[inviteId] = new PendingGameInvite
//         {
//             InviteId = inviteId,
//             RoomPin = roomPin,
//             FromName = fromName,
//             FromUserId = fromUserId
//         };
//     }

//     /// <summary>Accepts a pending game invite and joins the inviter's private room.</summary>
//     public void AcceptInvite(string inviteId)
//     {
//         if (string.IsNullOrEmpty(inviteId)) return;

//         if (!_pendingGameInvites.TryGetValue(inviteId, out PendingGameInvite invite))
//         {
//             invite = new PendingGameInvite
//             {
//                 InviteId = inviteId,
//                 RoomPin = inviteId
//             };
//         }

//         string roomPin = invite.RoomPin;
//         RemoveInviteFromFirebase(invite.InviteId);
//         _pendingGameInvites.Remove(invite.InviteId);
//         IncomingInvitePopup.Dismiss();

//         if (string.IsNullOrEmpty(roomPin))
//         {
//             ShowUIError("Invite expired.");
//             return;
//         }

//         if (PhotonNetwork.InRoom)
//         {
//             // Block only if we are in an ACTIVE game (GS == true). If we are merely sitting in a
//             // lobby / our own eager private room, JoinRoomWithPINText leaves it then joins.
//             bool inActiveGame = PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gsObj)
//                 && gsObj is bool gsBool && gsBool;
//             if (inActiveGame)
//             {
//                 ShowUIError("Leave your current game first.");
//                 return;
//             }
//         }

//         Debug.Log($"[Invite] Accepting invite '{invite.InviteId}' -> room '{roomPin}'");

//         // TASK 7 fix: the invitee is almost always ALREADY sitting in their OWN eagerly-created
//         // private room (created when they entered the friends flow). PhotonNetwork.JoinRoom returns
//         // false WITHOUT raising OnJoinRoomFailed when you are already in a room, so the loading
//         // overlay shown by BeginJoinRoomWithLoadingFade would never hide and the invitee gets stuck
//         // on the loading screen forever. Leave our current room first, queue the PIN, and let
//         // NetworkManager.OnLeftRoom join the friend's room once we are back on the master server.
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
//         {
//             if (PhotonNetwork.CurrentRoom.Name == roomPin)
//                 return; // already in the friend's room — nothing to do.

//             SuppressSeatLobbyOnJoin = false;
//             PendingJoinPin = roomPin;
//             if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.ShowLoading("Joining friend's table...");
//             PhotonNetwork.LeaveRoom();
//             return;
//         }

//         JoinRoomWithPINText(roomPin);
//     }

//     /// <summary>Declines a pending game invite and removes it from Firebase.</summary>
//     public void DeclineInvite(string inviteId)
//     {
//         if (string.IsNullOrEmpty(inviteId)) return;

//         RemoveInviteFromFirebase(inviteId);
//         _pendingGameInvites.Remove(inviteId);
//         IncomingInvitePopup.Dismiss();
//         Debug.Log($"[Invite] Declined invite '{inviteId}'");
//     }

//     void RemoveInviteFromFirebase(string inviteId)
//     {
//         if (string.IsNullOrEmpty(inviteId)) return;

//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("invites").Child(myId).Child(inviteId)
//             .RemoveValueAsync();
//     }

//     void ShowIncomingInvite(string fromName, string roomPin, string inviteId)
//     {
//         if (pinInputField != null)
//             pinInputField.text = roomPin;

//         IncomingInvitePopup.ShowInvite(fromName, roomPin, inviteId);
//         Debug.Log($"[Invite] Incoming from {fromName} — room {roomPin} (id={inviteId})");
//     }

//     void SaveFriends()
//     {
//         PlayerPrefs.SetString(FriendsPrefsKey, string.Join(",", myFriends));

//         var namePairs = new List<string>();
//         foreach (string id in myFriends)
//         {
//             if (friendDisplayNames.TryGetValue(id, out string name))
//                 namePairs.Add(id + "|" + name);
//         }
//         PlayerPrefs.SetString(FriendNamesPrefsKey, string.Join(",", namePairs));
//         PlayerPrefs.Save();
//     }

//     void LoadFriends()
//     {
//         if (myFriends == null) myFriends = new List<string>();
//         string data = PlayerPrefs.GetString(FriendsPrefsKey, "");
//         myFriends.Clear();
//         if (!string.IsNullOrEmpty(data))
//         {
//             foreach (string id in data.Split(','))
//             {
//                 if (!string.IsNullOrEmpty(id) && !myFriends.Contains(id))
//                     myFriends.Add(id);
//             }
//         }

//         friendDisplayNames.Clear();
//         string namesData = PlayerPrefs.GetString(FriendNamesPrefsKey, "");
//         if (!string.IsNullOrEmpty(namesData))
//         {
//             foreach (string pair in namesData.Split(','))
//             {
//                 int sep = pair.IndexOf('|');
//                 if (sep <= 0) continue;
//                 string id = pair.Substring(0, sep);
//                 string name = pair.Substring(sep + 1);
//                 if (!string.IsNullOrEmpty(id))
//                     friendDisplayNames[id] = name;
//             }
//         }
//     }

//     // ==========================================
//     // Phase 5 — Firebase friends persistence
//     // ==========================================

//     /// <summary>Tracks which signed-in user id we've already fetched Firebase friends for,
//     /// so the fetch runs once per login but re-runs if the user changes accounts.</summary>
//     string _friendsLoadedForUser;

//     /// <summary>
//     /// Phase 5 — Persist a single established friendship to Firebase so it survives re-login:
//     /// users/{myUid}/friends/{friendUid} = displayName. Guarded by a real signed-in Firebase user
//     /// so the generated GUID fallback is never used as a key.
//     /// </summary>
//     void WriteFriendToFirebase(string friendUid, string displayName)
//     {
//         if (string.IsNullOrEmpty(friendUid)) return;

//         Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
//         if (user == null)
//         {
//             Debug.LogWarning("[Friends] Skipped Firebase friend write — no signed-in user.");
//             return;
//         }

//         string myUid = user.UserId;
//         if (string.IsNullOrEmpty(myUid)) return;

//         string nameToStore = string.IsNullOrEmpty(displayName) ? friendUid : displayName;
//         Firebase.Database.FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("users").Child(myUid).Child("friends").Child(friendUid)
//             .SetValueAsync(nameToStore);
//         Debug.Log($"[Friends] Persisted friend to Firebase: users/{myUid}/friends/{friendUid} = {nameToStore}");
//     }

//     /// <summary>
//     /// Phase 5 — Loads the friends list from Firebase (users/{myUid}/friends) once after login and
//     /// merges each child (key=friendUid, value=displayName) into the local list/cache (dedupe),
//     /// then persists the local PlayerPrefs cache and refreshes the UI. The PlayerPrefs offline
//     /// fallback keeps working; this only augments it.
//     /// </summary>
//     public void LoadFriendsFromFirebase()
//     {
//         Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
//         if (user == null)
//         {
//             Debug.LogWarning("[Friends] LoadFriendsFromFirebase skipped — no signed-in user (offline). Using local cache.");
//             return;
//         }

//         string myUid = user.UserId;
//         if (string.IsNullOrEmpty(myUid)) return;

//         if (myFriends == null) myFriends = new List<string>();

//         Debug.Log($"[Friends] Loading friends from Firebase: users/{myUid}/friends");
//         Firebase.Database.FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("users").Child(myUid).Child("friends")
//             .GetValueAsync().ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted || task.IsCanceled)
//                 {
//                     Debug.LogWarning("[Friends] Failed to load friends from Firebase — keeping local cache.");
//                     return;
//                 }

//                 Firebase.Database.DataSnapshot snap = task.Result;
//                 if (snap == null || !snap.Exists)
//                 {
//                     Debug.Log("[Friends] No Firebase friends node yet — nothing to merge.");
//                     return;
//                 }

//                 int mergedCount = 0;
//                 foreach (Firebase.Database.DataSnapshot child in snap.Children)
//                 {
//                     string friendUid = child.Key;
//                     if (string.IsNullOrEmpty(friendUid)) continue;

//                     string displayName = child.Value?.ToString() ?? friendUid;
//                     if (!myFriends.Contains(friendUid))
//                     {
//                         myFriends.Add(friendUid);
//                         mergedCount++;
//                     }
//                     friendDisplayNames[friendUid] = displayName;
//                 }

//                 SaveFriends();
//                 FriendsPanelUIController.Instance?.RefreshAll();
//                 RefreshFriendsListUI();
//                 Debug.Log($"[Friends] Loaded friends from Firebase — merged {mergedCount} new, total {myFriends.Count}.");
//             });
//     }
// }

// using System;
// using System.Collections;
// using UnityEngine;
// using UnityEngine.UI;
// using Photon.Pun;
// using Photon.Realtime;
// using TMPro;
// using System.Collections.Generic;
// using Firebase.Database;
// using Firebase.Extensions;
// using Firebase.Auth;
// using DG.Tweening;

// public class PlayWithFriendsManager : MonoBehaviourPunCallbacks
// {
//     public static PlayWithFriendsManager Instance;

//     /// <summary>PIN queued while Photon is still connecting (invite accept / join table).</summary>
//     public static string PendingJoinPin { get; set; }

//     [Header("PIN UI Components")]
//     public TMP_InputField pinInputField;
//     public TMP_Text generatedPinText;
//     public GameObject pinCreationPanel;
//     public TMP_Text errorText;

//     [Header("Lobby Buttons & Panels")]
//     public GameObject startGameButton;
//     public GameObject modesPanel;
//     public TMP_Text clientWaitingText;
//     [Tooltip("Optional spinner icon shown beside the waiting label (auto-created if empty).")]
//     public RectTransform clientWaitingSpinner;
//     [Tooltip("Font size for 'Waiting for Host...' on joining clients.")]
//     [SerializeField] float clientWaitingFontSize = 34f;

//     Tween _waitingSpinnerTween;

//     [Header("Online Matchmaking (shared seat panel)")]
//     [Tooltip("Countdown / status text shown only while this panel is used as the online matchmaking lobby.")]
//     public TMP_Text matchmakingTimerText;
//     [Tooltip("Wooden plaque holding the timer text (shown only in online matchmaking mode).")]
//     public GameObject matchmakingTimerPlaque;
//     // When true the seat panel acts as the ONLINE matchmaking lobby (public room):
//     // timer is shown, PIN / Create / manual Start / Bots controls are hidden, and the
//     // match auto-starts (driven by DeckManager) once the table fills or the timer ends.
//     bool _onlineMode;
//     public bool IsOnlineMode => _onlineMode;

//     [Header("Live Player List UI")]
//     public TMP_Text[] playerSlotsText;
//     [Tooltip("Avatar Image under each chair, parallel index to playerSlotsText.")]
//     public UnityEngine.UI.Image[] playerSlotsAvatar;

//     [Header("Room Creation / PIN Display")]
//     [Tooltip("CREATE ROOM button shown until the private room exists.")]
//     public GameObject createRoomButton;
//     [Tooltip("Wooden plaque holding the ROOM ID text, shown once the room exists.")]
//     public GameObject roomIdPlaque;

//     [Header("Toggle Bot Settings")]
//     public GameObject includeBotsButton;
//     public TMP_Text includeBotsBtnText;
//     bool areBotsIncluded;

//     [Header("Game Table UI")]
//     public GameObject homeMenuPanel;
//     public GameObject gameTablePanel;

//     [Header("Friends UI Slots")]
//     public TMP_Text myUserIdText;
//     public TMP_InputField addFriendInput;
//     public Transform friendsListContainer;
//     public GameObject friendUIPrefab;

//     [Header("Friend Requests UI")]
//     [Tooltip("Prefab for an incoming friend request row (must contain AcceptButton and DeclineButton).")]
//     public GameObject friendRequestRowPrefab;

//     // Incoming friend requests: fromUserId -> fromName
//     readonly Dictionary<string, string> incomingRequests = new Dictionary<string, string>();
//     DatabaseReference requestDbRef;
//     DatabaseReference acceptDbRef;
//     bool _requestListenerStarted;
//     bool _acceptListenerStarted;

//     [Header("Friends List Storage")]
//     private const string FriendsPrefsKey = "SavedFriendsList";
//     private const string FriendNamesPrefsKey = "SavedFriendsNames";
//     private const string FirebaseDatabaseUrl = "https://dehlapakad-c207c-default-rtdb.firebaseio.com/";
//     public List<string> myFriends = new List<string>();
//     readonly Dictionary<string, string> friendDisplayNames = new Dictionary<string, string>();
//     readonly Dictionary<string, FriendInfo> friendPhotonStatus = new Dictionary<string, FriendInfo>();
//     readonly Dictionary<string, long> friendFirebaseLastActiveMs = new Dictionary<string, long>();
//     readonly Dictionary<string, bool> friendFirebaseOnlineFlag = new Dictionary<string, bool>();
//     readonly Dictionary<string, (DatabaseReference Ref, EventHandler<ValueChangedEventArgs> Handler)> _presenceListeners =
//         new Dictionary<string, (DatabaseReference, EventHandler<ValueChangedEventArgs>)>();
//     readonly Dictionary<string, PendingGameInvite> _pendingGameInvites = new Dictionary<string, PendingGameInvite>();
//     Coroutine _presenceHeartbeatCoroutine;
//     const long FirebaseOnlineThresholdMs = 120_000;

//     struct PendingGameInvite
//     {
//         public string InviteId;
//         public string RoomPin;
//         public string FromName;
//         public string FromUserId;
//     }
//     PhotonView _photonView;
//     DatabaseReference inviteDbRef;
//     const long InviteExpirySeconds = 15;
//     string _pendingInviteFriendId;
//     string _pendingInviteFriendName;
//     bool _inviteListenerStarted;
//     string _listenersBoundUserId;
//     readonly HashSet<string> _gameInvitesSent = new HashSet<string>();
//     bool _pendingCreatePrivateRoom;
//     bool _isLeavingFriendsFlow;

//     public static bool IsFriendsPrivateRoomCreatePending()
//     {
//         return Instance != null
//             && Instance._pendingCreatePrivateRoom
//             && !Instance._isLeavingFriendsFlow;
//     }

//     /// <summary>User backed out of PlayFriends — abort eager room create and block ghost lobby UI.</summary>
//     public void AbortPendingFriendsRoomCreation()
//     {
//         _isLeavingFriendsFlow = true;
//         _pendingCreatePrivateRoom = false;
//         _pendingSeatLobbyOpen = false;
//         _creatingPrivateRoom = false;
//         SuppressSeatLobbyOnJoin = false;

//         if (_createRoomCoroutine != null)
//         {
//             StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
//         }

//         Debug.Log("[Friends] Pending room creation aborted (back / leave).");
//     }

//     public void BeginFriendsFlow()
//     {
//         _isLeavingFriendsFlow = false;
//     }

//     public bool IsLeavingFriendsFlow => _isLeavingFriendsFlow;

//     public void TryFlushPendingPrivateRoomCreate()
//     {
//         if (_isLeavingFriendsFlow || !_pendingCreatePrivateRoom || PhotonNetwork.InRoom) return;
//         if (!NetworkManager.IsPhotonMasterReadyForRooms()) return;

//         if (_createRoomCoroutine != null)
//         {
//             StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
//         }

//         _pendingCreatePrivateRoom = false;
//         if (errorText != null) errorText.gameObject.SetActive(false);
//         DoCreatePrivateRoom();
//     }

//     /// <summary>Queue private-room create after leaving a public online room.</summary>
//     public void RequestPrivateRoomCreateAfterLeave()
//     {
//         BeginFriendsFlow();
//         _pendingCreatePrivateRoom = true;
//         SuppressSeatLobbyOnJoin = true;
//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.MarkReturnToFriendsModesAfterLeave();
//         Debug.Log("[Friends] Private room create queued after leave.");
//     }

//     /// <summary>Clears online-only seat panel state without hiding the friends panel.</summary>
//     public void ClearOnlineModeOnly()
//     {
//         _onlineMode = false;
//         _previewBotsInOnlineLobby = false;
//         ApplyModeControls(false);
//         if (matchmakingTimerText != null)
//             matchmakingTimerText.text = string.Empty;
//     }
//     bool _joinInProgress;
//     bool _handlingJoinFailure;
//     int _joinAttemptToken;
//     Coroutine _joinTimeoutCoroutine;
//     MonoBehaviour _joinTimeoutRunner;
//     JoinTablePanelController _joinTableController;
//     Coroutine _lobbyPlayerRefreshCoroutine;
//     MonoBehaviour _lobbyPlayerRefreshRunner;

//     // BUG1 (instant invites): true while a private room is created EAGERLY on entering the
//     // friends flow (before the host taps Play) so invites can be sent immediately. While set,
//     // join-time handlers must NOT pull the host out of the Modes panel into the seat lobby.
//     // Cleared when the host opens the seat panel (taps Play) or leaves the private room.
//     public bool SuppressSeatLobbyOnJoin;

//     Coroutine _createRoomCoroutine;
//     MonoBehaviour _createRoomRunner;
//     Coroutine _retryFriendServicesCoroutine;
//     Coroutine _findFriendsCoroutine;
//     Coroutine _smoothGameStartCoroutine;
//     bool _firebaseAuthHooked;
//     bool _friendsGameStartTriggered;
//     bool _hostConfirmedSeatStart;
//     bool _pendingSeatLobbyOpen;
//     bool _isLeavingRoom;

//     // Runtime-created "INVITE FRIENDS" button shown on the friends seat/lobby panel.
//     GameObject _lobbyInviteButton;

//     // PIN / private-room creation reliability: track that WE are creating a private room so
//     // OnCreateRoomFailed can retry with a fresh PIN (e.g. a rare 5-digit PIN collision).
//     bool _creatingPrivateRoom;
//     int _createRoomRetries;
//     const int MaxCreateRoomRetries = 5;

//     public IReadOnlyList<string> MyFriends => myFriends;
//     public bool IsJoinInProgress => _joinInProgress;
//     public IReadOnlyDictionary<string, string> IncomingRequests => incomingRequests;

//     /// <summary>Fires whenever the incoming friend-request list changes (added/removed/accepted/declined).
//     /// In-game panels subscribe to live-refresh their Accept/Decline rows.</summary>
//     public event System.Action RequestsChanged;
//     void NotifyRequestsChanged() => RequestsChanged?.Invoke();

//     /// <summary>TASK 18/25: fires whenever a friend's online/in-game presence changes (Firebase
//     /// presence ValueChanged or a status re-poll). Open in-game friend panels subscribe to this so
//     /// they repaint with the correct Online/Offline state once the async presence read completes —
//     /// otherwise rows built synchronously on panel-open show everyone as "Offline".</summary>
//     public event System.Action FriendsStatusChanged;
//     void NotifyFriendsStatusChanged() => FriendsStatusChanged?.Invoke();

//     public string GetFriendDisplayName(string friendId) => GetFriendDisplayNameInternal(friendId);

//     /// <summary>Firebase account id used for friend requests / invites (same as MyUserId).</summary>
//     public string GetAccountUserId() => MyUserId;

//     public FriendInfo GetFriendPhotonInfo(string friendId) =>
//         friendPhotonStatus.TryGetValue(friendId, out FriendInfo info) ? info : null;

//     /// <summary>Online when Photon reports it, or Firebase presence was updated recently (works in-room).</summary>
//     public bool IsFriendOnline(string friendId)
//     {
//         if (string.IsNullOrEmpty(friendId)) return false;

//         if (friendPhotonStatus.TryGetValue(friendId, out FriendInfo photonInfo) && photonInfo != null && photonInfo.IsOnline)
//             return true;

//         if (friendFirebaseOnlineFlag.TryGetValue(friendId, out bool firebaseOnline) && firebaseOnline)
//             return true;

//         if (friendFirebaseLastActiveMs.TryGetValue(friendId, out long lastMs) && lastMs > 0)
//         {
//             long age = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastMs;
//             return age >= 0 && age <= FirebaseOnlineThresholdMs;
//         }

//         return false;
//     }

//     public bool IsFriendInGame(string friendId)
//     {
//         if (friendPhotonStatus.TryGetValue(friendId, out FriendInfo info) && info != null)
//             return info.IsOnline && info.IsInRoom;
//         return false;
//     }

//     public void MarkGameInviteSent(string friendUserId)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;
//         _gameInvitesSent.Add(friendUserId);
//     }

//     /// <summary>
//     /// Live friend presence sync: attaches Firebase ValueChanged listeners per friend, publishes
//     /// our own heartbeat, polls Photon FindFriends when on the master server, and repaints UI.
//     /// </summary>
//     public void SyncFriendStatus()
//     {
//         EnsurePhotonUserId();
//         PublishOwnPresence();

//         if (myFriends == null || myFriends.Count == 0)
//         {
//             TearDownPresenceListeners();
//             RefreshFriendsListUI();
//             return;
//         }

//         TearDownPresenceListeners();

//         DatabaseReference root = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference;
//         foreach (string friendId in myFriends)
//         {
//             if (string.IsNullOrEmpty(friendId)) continue;

//             string capturedId = friendId;
//             DatabaseReference presenceRef = root.Child("users").Child(capturedId).Child("presence");

//             EventHandler<ValueChangedEventArgs> handler = (_, args) => OnFriendPresenceChanged(capturedId, args);
//             presenceRef.ValueChanged += handler;
//             _presenceListeners[capturedId] = (presenceRef, handler);

//             presenceRef.GetValueAsync().ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted || task.Result == null) return;
//                 ApplyPresenceSnapshot(capturedId, task.Result);
//                 RefreshFriendsListUI();
//             });
//         }

//         if (CanCallFindFriends())
//             PhotonNetwork.FindFriends(myFriends.ToArray());
//         else if (!PhotonNetwork.InRoom)
//             ScheduleFindFriendsWhenReady();

//         RefreshFriendsListUI();
//     }

//     public void RefreshFriendsStatus() => SyncFriendStatus();

//     public void CheckFriendsOnlineStatus() => SyncFriendStatus();

//     void OnFriendPresenceChanged(string friendId, ValueChangedEventArgs args)
//     {
//         if (args.DatabaseError != null) return;
//         ApplyPresenceSnapshot(friendId, args.Snapshot);
//         RefreshFriendsListUI();
//     }

//     void ApplyPresenceSnapshot(string friendId, DataSnapshot snapshot)
//     {
//         if (string.IsNullOrEmpty(friendId)) return;

//         if (snapshot == null || !snapshot.Exists)
//         {
//             friendFirebaseOnlineFlag[friendId] = false;
//             friendFirebaseLastActiveMs.Remove(friendId);
//             return;
//         }

//         if (snapshot.Child("online").Exists)
//         {
//             object onlineVal = snapshot.Child("online").Value;
//             bool online = onlineVal is bool b && b
//                 || (onlineVal != null && onlineVal.ToString().Equals("true", System.StringComparison.OrdinalIgnoreCase));
//             friendFirebaseOnlineFlag[friendId] = online;
//         }

//         if (snapshot.Child("lastActive").Exists
//             && long.TryParse(snapshot.Child("lastActive").Value?.ToString(), out long lastMs))
//         {
//             friendFirebaseLastActiveMs[friendId] = lastMs;
//         }
//     }

//     void TearDownPresenceListeners()
//     {
//         foreach (var entry in _presenceListeners)
//         {
//             if (entry.Value.Ref != null && entry.Value.Handler != null)
//                 entry.Value.Ref.ValueChanged -= entry.Value.Handler;
//         }
//         _presenceListeners.Clear();
//     }

//     /// <summary>
//     /// Tasks 9/18/25 — Public entry the friends UI invite button should call. Wraps
//     /// InviteFriendToGame and relies on SendFirebaseInvite to mark the invite "sent" ONLY in its
//     /// success callback — so a failed invite no longer permanently greys out the button.
//     /// </summary>
//     public void SendGameInvite(string friendId)
//     {
//         if (string.IsNullOrEmpty(friendId)) return;
//         InviteFriendToGame(friendId, GetFriendDisplayNameInternal(friendId));
//     }

//     public bool IsGameInviteSent(string friendUserId) =>
//         !string.IsNullOrEmpty(friendUserId) && _gameInvitesSent.Contains(friendUserId);

//     void Awake()
//     {
//         if (Instance == null) Instance = this;
//         else if (Instance != this)
//         {
//             Destroy(this);
//             return;
//         }

//         LoadFriends();
//         if (myFriends == null) myFriends = new List<string>();
//         EnsurePhotonUserId();
//         EnsureNickname();
//         EnsurePhotonView();
//         PhotonNetwork.AddCallbackTarget(this);
//         TryHookFirebaseAuth();
//     }

//     public override void OnEnable()
//     {
//         base.OnEnable();
//         TryHookFirebaseAuth();
//     }

//     public override void OnDisable()
//     {
//         base.OnDisable();
//         UnhookFirebaseAuth();
//         if (_retryFriendServicesCoroutine != null)
//         {
//             StopCoroutine(_retryFriendServicesCoroutine);
//             _retryFriendServicesCoroutine = null;
//         }
//     }

//     void TryHookFirebaseAuth()
//     {
//         if (_firebaseAuthHooked) return;
//         if (FirebaseAuth.DefaultInstance == null) return;

//         FirebaseAuth.DefaultInstance.StateChanged += OnFirebaseAuthStateChanged;
//         _firebaseAuthHooked = true;

//         if (FirebaseAuth.DefaultInstance.CurrentUser != null)
//             EnsureFriendServicesStarted();
//     }

//     void UnhookFirebaseAuth()
//     {
//         if (!_firebaseAuthHooked || FirebaseAuth.DefaultInstance == null) return;
//         FirebaseAuth.DefaultInstance.StateChanged -= OnFirebaseAuthStateChanged;
//         _firebaseAuthHooked = false;
//     }

//     void OnFirebaseAuthStateChanged(object sender, System.EventArgs e)
//     {
//         if (FirebaseAuth.DefaultInstance?.CurrentUser != null)
//         {
//             EnsurePhotonUserId();
//             EnsureFriendServicesStarted();
//         }
//     }

//     void EnsureNickname()
//     {
//         string profileName = PlayerPrefs.GetString("PlayerUsername", string.Empty).Trim();
//         if (!string.IsNullOrEmpty(profileName))
//         {
//             if (PhotonNetwork.NickName != profileName)
//                 PhotonNetwork.NickName = profileName;
//             return;
//         }

//         if (string.IsNullOrEmpty(PhotonNetwork.NickName))
//         {
//             PhotonNetwork.NickName = "Player_" + UnityEngine.Random.Range(100, 999);
//             Debug.Log("My Random Name Set To: " + PhotonNetwork.NickName);
//         }
//     }

//     public void EnsureNicknamePublic() => EnsureNickname();

//     void EnsurePhotonView()
//     {
//         if (_photonView == null)
//             _photonView = GetComponent<PhotonView>();
//     }

//     /// <summary>
//     /// PlayWithFriendsPanel is often inactive, so its scene PhotonView may stay at ViewID 0.
//     /// Route friend-lobby RPCs through DeckManager's always-active scene view instead.
//     /// </summary>
//     static PhotonView GetReliableRpcView()
//     {
//         if (DeckManager.Instance != null)
//         {
//             PhotonView deckPv = DeckManager.Instance.photonView;
//             if (deckPv != null && deckPv.ViewID > 0)
//                 return deckPv;
//         }

//         PlayWithFriendsManager mgr = Instance != null ? Instance : ResolveManagerInstance();
//         if (mgr == null) return null;

//         PhotonView localPv = mgr.photonView;
//         if (localPv == null) return null;
//         if (localPv.ViewID > 0) return localPv;

//         if (localPv.sceneViewId > 0)
//         {
//             localPv.ViewID = localPv.sceneViewId;
//             if (localPv.ViewID > 0)
//                 return localPv;
//         }

//         return null;
//     }

//     static PlayWithFriendsManager ResolveManagerInstance()
//     {
//         var all = Resources.FindObjectsOfTypeAll<PlayWithFriendsManager>();
//         foreach (var m in all)
//         {
//             if (m == null || !m.gameObject.scene.IsValid()) continue;
//             return m;
//         }
//         return null;
//     }

//     void SendFriendsRpc(string methodName, RpcTarget target)
//     {
//         PhotonView rpcView = GetReliableRpcView();
//         if (rpcView == null || rpcView.ViewID < 1)
//         {
//             Debug.LogError($"[Friends] Cannot send {methodName}: no valid PhotonView (panel view id is 0).");
//             return;
//         }

//         rpcView.RPC(methodName, target);
//     }

//     void OnDestroy()
//     {
//         UnhookFirebaseAuth();
//         PhotonNetwork.RemoveCallbackTarget(this);

//         if (requestDbRef != null)
//         {
//             requestDbRef.ChildAdded -= OnFriendRequestAdded;
//             requestDbRef.ChildRemoved -= OnFriendRequestRemoved;
//         }
//         if (acceptDbRef != null)
//             acceptDbRef.ChildAdded -= OnFriendAcceptAdded;
//         if (inviteDbRef != null)
//             inviteDbRef.ChildAdded -= OnIncomingInviteAdded;

//         TearDownPresenceListeners();
//         _waitingSpinnerTween?.Kill();
//         if (Instance == this) Instance = null;
//     }

//     void Start()
//     {
//         if (errorText != null) errorText.gameObject.SetActive(false);
//         HideClientWaitingPresentation();
//         if (includeBotsButton != null) includeBotsButton.SetActive(false);

//         if (_onlineMode)
//         {
//             ShowLocalPlayerInOnlineMatchmaking();
//             return;
//         }

//         ClearPlayerListUI();
//         EnsureFriendServicesStarted();

//         // If this panel was activated as the online matchmaking lobby, do not touch the
//         // friends-only Start button — ShowOnlineMatchmakingLobby() already configured it.
//         if (_onlineMode) return;

//         // New flow: the Start button is always visible on the seat panel but stays
//         // greyed/disabled until the table is full. Re-apply correct state for any room.
//         if (startGameButton != null)
//         {
//             startGameButton.SetActive(true);
//             SetStartButtonInteractable(false);
//         }
//         CheckPlayerCountAndToggleStart();
//     }

//     /// <summary>Call after login / Photon ready so Firebase listeners use the real user id.</summary>
//     public void EnsureFriendServicesStarted()
//     {
//         EnsurePhotonUserId();

//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId))
//         {
//             // StartCoroutine throws if this panel's GameObject is inactive (headless boot).
//             // In that case SocialServiceBootstrap drives the retry instead.
//             if (isActiveAndEnabled && _retryFriendServicesCoroutine == null)
//                 _retryFriendServicesCoroutine = StartCoroutine(RetryFriendServicesWhenReady());
//             return;
//         }

//         if (_retryFriendServicesCoroutine != null)
//         {
//             StopCoroutine(_retryFriendServicesCoroutine);
//             _retryFriendServicesCoroutine = null;
//         }

//         if (_listenersBoundUserId != myId)
//         {
//             StopFriendListeners();
//             _listenersBoundUserId = myId;
//         }

//         DisplayMyID();
//         StartFriendRequestListener();
//         StartFriendAcceptListener();
//         StartInviteListener();
//         StartPresenceHeartbeat();
//         SyncFriendStatus();

//         // Phase 5 — pull the persisted friends list from Firebase right after login. Guarded so
//         // it runs once per signed-in user, but re-runs if the account changes.
//         if (Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser != null && _friendsLoadedForUser != myId)
//         {
//             _friendsLoadedForUser = myId;
//             LoadFriendsFromFirebase();
//         }
//     }

//     bool _headlessFriendsLoaded;

//     /// <summary>
//     /// Brings the Firebase friend-request / accept / invite listeners online even though this
//     /// panel's GameObject is INACTIVE on the home screen. Without this, a player who never opens
//     /// the matchmaking / play-with-friends panel never binds the listeners, so incoming friend
//     /// requests and game invites are silently dropped (and the manager's Instance stays null, so
//     /// the "Add Friend" buttons do nothing / throw). Called by SocialServiceBootstrap; safe to
//     /// call repeatedly — every internal bind is guarded.
//     /// </summary>
//     public void StartSocialServicesHeadless()
//     {
//         if (Instance == null) Instance = this;

//         if (!_headlessFriendsLoaded)
//         {
//             LoadFriends();
//             if (myFriends == null) myFriends = new List<string>();
//             _headlessFriendsLoaded = true;
//         }

//         // The panel GameObject is INACTIVE on the home screen, so MonoBehaviourPunCallbacks'
//         // OnEnable (which registers Photon callbacks) never runs. Register them here so callbacks
//         // like OnConnectedToMaster / OnJoinedRoom fire for the eager private-room creation even
//         // before the player opens the seat panel. Also pre-set the nickname / view headless so
//         // the player's own name shows the moment the room is created.
//         EnsurePhotonCallbacks();
//         EnsurePhotonView();
//         EnsureNickname();

//         // Subscribe to Firebase auth so the listeners rebind to the real account id once the
//         // user finishes signing in (the normal Awake/OnEnable hook never runs while inactive).
//         TryHookFirebaseAuth();
//         EnsureFriendServicesStarted();
//     }

//     /// <summary>
//     /// Registers this manager as a Photon callback target. Safe to call repeatedly (PUN dedupes
//     /// by target). Needed because the panel is often inactive, so the base OnEnable registration
//     /// does not run on the home screen.
//     /// </summary>
//     void EnsurePhotonCallbacks() => PhotonNetwork.AddCallbackTarget(this);

//     IEnumerator RetryFriendServicesWhenReady()
//     {
//         const int maxAttempts = 30;
//         for (int i = 0; i < maxAttempts; i++)
//         {
//             yield return new WaitForSeconds(0.5f);
//             if (FirebaseAuth.DefaultInstance?.CurrentUser != null || !string.IsNullOrEmpty(MyUserId))
//             {
//                 _retryFriendServicesCoroutine = null;
//                 EnsureFriendServicesStarted();
//                 yield break;
//             }
//         }

//         _retryFriendServicesCoroutine = null;
//         Debug.LogWarning("[Friends] Could not start friend services — no Firebase user id yet.");
//     }

//     void StopFriendListeners()
//     {
//         if (requestDbRef != null)
//         {
//             requestDbRef.ChildAdded -= OnFriendRequestAdded;
//             requestDbRef.ChildRemoved -= OnFriendRequestRemoved;
//             requestDbRef = null;
//         }
//         if (acceptDbRef != null)
//         {
//             acceptDbRef.ChildAdded -= OnFriendAcceptAdded;
//             acceptDbRef = null;
//         }
//         if (inviteDbRef != null)
//         {
//             inviteDbRef.ChildAdded -= OnIncomingInviteAdded;
//             inviteDbRef = null;
//         }

//         _requestListenerStarted = false;
//         _acceptListenerStarted = false;
//         _inviteListenerStarted = false;
//         incomingRequests.Clear();
//     }

//     void EnsurePhotonUserId()
//     {
//         if (PhotonNetwork.AuthValues == null)
//             PhotonNetwork.AuthValues = new AuthenticationValues();

//         string firebaseUid = FirebaseAuth.DefaultInstance?.CurrentUser?.UserId;
//         if (!string.IsNullOrEmpty(firebaseUid))
//         {
//             if (PhotonNetwork.AuthValues.UserId != firebaseUid)
//             {
//                 PhotonNetwork.AuthValues.UserId = firebaseUid;
//                 PlayerPrefs.SetString("PhotonUserId", firebaseUid);
//                 PlayerPrefs.Save();
//             }
//             return;
//         }

//         if (string.IsNullOrEmpty(PhotonNetwork.AuthValues.UserId))
//         {
//             string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
//             PlayerPrefs.SetString("PhotonUserId", uid);
//             PlayerPrefs.Save();
//             PhotonNetwork.AuthValues.UserId = uid;
//         }
//     }

//     // ==========================================
//     // 1. HOST: CREATE PRIVATE ROOM (modes later)
//     // ==========================================

//     public void CreatePrivateRoom()
//     {
//         if (errorText != null) errorText.gameObject.SetActive(false);

//         // The panel may be inactive (eager create from the Modes screen). Make sure our Photon
//         // callbacks are registered so OnConnectedToMaster fires and creates the room once the
//         // cold connection completes — otherwise the very first attempt silently does nothing.
//         EnsurePhotonCallbacks();

//         if (PhotonNetwork.InRoom)
//         {
//             ShowUIError("Leave the current room first.");
//             return;
//         }

//         if (NetworkManager.IsPhotonMasterReadyForRooms())
//         {
//             _pendingCreatePrivateRoom = false;
//             StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
//             DoCreatePrivateRoom();
//             return;
//         }

//         if (PhotonNetwork.IsConnectedAndReady)
//         {
//             _pendingCreatePrivateRoom = true;
//             TryFlushPendingPrivateRoomCreate();
//             if (PhotonNetwork.InRoom || !_pendingCreatePrivateRoom)
//                 return;
//         }

//         if (!NetworkManager.HasInternet())
//         {
//             ShowUIError("No internet connection.");
//             return;
//         }

//         _pendingCreatePrivateRoom = true;

//         if (NetworkManager.IsPhotonConnectingOrConnected())
//             ShowUIError("Connecting... please wait.");
//         else
//         {
//             ShowUIError("Connecting to server...");
//             if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.ConnectToPhoton();
//         }

//         // The poll-and-create coroutine can only run on an ACTIVE GameObject. When the panel is
//         // inactive (eager create from the Modes screen) we use NetworkManager as the runner.
//         StartFriendsCoroutine(WaitAndCreatePrivateRoomRoutine(), ref _createRoomCoroutine, ref _createRoomRunner);
//     }

//     void DoCreatePrivateRoom()
//     {
//         if (PhotonNetwork.InRoom) return;

//         if (!NetworkManager.IsPhotonMasterReadyForRooms())
//         {
//             _pendingCreatePrivateRoom = true;
//             Debug.Log("[Friends] CreateRoom deferred — Photon not ready on Master (e.g. JoiningLobby).");
//             return;
//         }

//         // Fresh PIN every time a room is created. Always 5 digits (10000-99999) so the leading
//         // digit is never 0 and the PIN is easy to read / type.
//         string newPin = GenerateRoomPin();
//         Debug.Log("Generating PIN: " + newPin);

//         _creatingPrivateRoom = true;

//         RoomOptions roomOptions = new RoomOptions
//         {
//             MaxPlayers = 4,
//             IsVisible = false,
//             IsOpen = true,
//             // Required so players can read each other's account id (Player.UserId) in-game,
//             // used by the friend / stats popup.
//             PublishUserId = true
//         };

//         PhotonNetwork.CreateRoom(newPin, roomOptions);
//         Debug.Log("[Friends] Room created with PIN: " + newPin);
//     }

//     /// <summary>Generates a fresh, easy-to-read 5-digit room PIN.</summary>
//     static string GenerateRoomPin() => UnityEngine.Random.Range(10000, 100000).ToString();

//     IEnumerator WaitAndCreatePrivateRoomRoutine()
//     {
//         float timeout = 20f;
//         while (timeout > 0f && _pendingCreatePrivateRoom)
//         {
//             if (NetworkManager.IsPhotonMasterReadyForRooms() && !PhotonNetwork.InRoom)
//             {
//                 _pendingCreatePrivateRoom = false;
//                 _createRoomCoroutine = null;
//                 if (errorText != null) errorText.gameObject.SetActive(false);
//                 DoCreatePrivateRoom();
//                 yield break;
//             }

//             if (!NetworkManager.IsPhotonConnectingOrConnected() && NetworkManager.HasInternet()
//                 && NetworkManager.Instance != null)
//             {
//                 NetworkManager.Instance.ConnectToPhoton();
//             }

//             yield return new WaitForSeconds(0.25f);
//             timeout -= 0.25f;
//         }

//         _pendingCreatePrivateRoom = false;
//         _createRoomCoroutine = null;
//         if (!PhotonNetwork.IsConnectedAndReady)
//             ShowUIError("Could not connect. Try again.");
//     }

//     public override void OnConnectedToMaster()
//     {
//         TryFlushPendingPrivateRoomCreate();
//         TryFlushPendingJoin();
//         CheckFriendsOnlineStatus();
//     }

//     public override void OnJoinedLobby()
//     {
//         TryFlushPendingPrivateRoomCreate();
//         TryFlushPendingJoin();
//         CheckFriendsOnlineStatus();
//     }

//     public void TryFlushPendingJoin()
//     {
//         if (!string.IsNullOrEmpty(PendingJoinPin) && !PhotonNetwork.InRoom)
//         {
//             if (!NetworkManager.IsPhotonMasterReadyForRooms())
//             {
//                 Debug.Log("[Friends] Client not ready yet (JoiningLobby). Deferring PIN join.");
//                 return;
//             }

//             string pin = PendingJoinPin;
//             PendingJoinPin = null;

//             Debug.Log($"[Friends] Photon ready — joining queued room '{pin}'");

//             if (!UiFlowManager.IsPlayFriendsJoinFlow())
//                 _joinAttemptToken = UiFlowManager.BeginPinJoinAttempt();

//             if (ModeManager.Instance != null)
//                 ModeManager.Instance.MarkFriendsPinJoinFlow();

//             if (!_joinInProgress)
//             {
//                 _joinInProgress = true;
//                 SetJoinButtonInteractable(false);
//                 StartJoinTimeout();
//                 if (NetworkManager.Instance != null)
//                     NetworkManager.Instance.CancelPinJoinUiOverlays();
//             }

//             if (!PhotonNetwork.JoinRoom(pin))
//                 RestoreJoinPanelAfterFailedJoin(0, "JoinRoom rejected");
//         }
//     }

//     void CacheJoinTableController()
//     {
//         if (_joinTableController == null)
//             _joinTableController = FindAnyObjectByType<JoinTablePanelController>();
//     }

//     public void SetJoinButtonInteractable(bool interactable)
//     {
//         CacheJoinTableController();
//         if (_joinTableController != null)
//             _joinTableController.SetJoinInteractable(interactable);
//         if (pinInputField != null)
//             pinInputField.interactable = interactable;
//     }

//     void StartJoinTimeout()
//     {
//         StartFriendsCoroutine(JoinTimeoutRoutine(), ref _joinTimeoutCoroutine, ref _joinTimeoutRunner);
//     }

//     void StopJoinTimeout()
//     {
//         StopFriendsCoroutineSlot(ref _joinTimeoutCoroutine, ref _joinTimeoutRunner);
//     }

//     IEnumerator JoinTimeoutRoutine()
//     {
//         yield return new WaitForSecondsRealtime(10f);
//         _joinTimeoutCoroutine = null;
//         if (!_joinInProgress) yield break;

//         // GLITCH FIX: if a match started while this timeout was pending, don't force the Join Table
//         // / Modes panels open over active play.
//         if (GameFlowState.IsActivelyPlaying)
//         {
//             Debug.Log("[Friends] Join timeout ignored — match actively in progress.");
//             yield break;
//         }

//         Debug.LogWarning("[Friends] PIN join timed out — restoring Join Table.");
//         RestoreJoinPanelAfterFailedJoin(0, "Join timed out. Try again.");
//     }

//     // ==========================================
//     // 2. CLIENT: JOIN ROOM WITH PIN
//     // ==========================================

//     public void JoinRoomWithPIN()
//     {
//         if (_joinInProgress) return;

//         Debug.Log("[Friends] Joining room by PIN");
//         if (errorText != null) errorText.gameObject.SetActive(false);

//         if (pinInputField == null || string.IsNullOrEmpty(pinInputField.text))
//         {
//             ShowUIError("Enter valid PIN!");
//             return;
//         }

//         BeginPinJoin(pinInputField.text.Trim());
//     }

//     /// <summary>
//     /// Joins a private room using a PIN supplied directly (used by the in-Modes JOIN TABLE panel,
//     /// which has its own input field separate from the Play-with-Friends panel).
//     /// </summary>
//     public void JoinRoomWithPINText(string pin)
//     {
//         if (_joinInProgress)
//         {
//             Debug.Log("[Friends] Join PIN ignored — isJoiningRoom=true");
//             return;
//         }

//         if (errorText != null) errorText.gameObject.SetActive(false);

//         if (string.IsNullOrEmpty(pin) || string.IsNullOrWhiteSpace(pin))
//         {
//             ShowUIError("Enter valid PIN!");
//             return;
//         }

//         string trimmed = pin.Trim();
//         Debug.Log($"[Friends] Join PIN clicked | pin='{trimmed}' | isJoiningRoom={_joinInProgress}");
//         BeginPinJoin(trimmed);
//     }

//     void BeginPinJoin(string targetPin)
//     {
//         Debug.Log($"[Friends] JoinRoom requested | room='{targetPin}' | isJoiningRoom={_joinInProgress}");

//         _onlineMode = false;
//         _previewBotsInOnlineLobby = false;
//         if (MatchmakingManager.Instance != null)
//         {
//             MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: false);
//             MatchmakingManager.Instance.HideMatchmakingPanel();
//         }

//         _joinAttemptToken = UiFlowManager.BeginPinJoinAttempt();

//         // A fresh, user-initiated PIN join is NOT a disconnect rejoin. Clear any stale rejoin
//         // state so a wrong-PIN failure routes to the JoinTable restore path, not the rejoin path.
//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ClearRejoinState();

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.MarkFriendsPinJoinFlow();

//         _joinInProgress = true;
//         SetJoinButtonInteractable(false);
//         StartJoinTimeout();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.CancelPinJoinUiOverlays();

//         if (!PhotonNetwork.IsConnectedAndReady)
//         {
//             if (!NetworkManager.HasInternet())
//             {
//                 RestoreJoinPanelAfterFailedJoin(0, "No internet connection.");
//                 return;
//             }
//             PendingJoinPin = targetPin;
//             if (NetworkManager.Instance != null) NetworkManager.Instance.ConnectToPhoton();
//             return;
//         }

//         if (PhotonNetwork.InRoom)
//         {
//             if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.Name == targetPin)
//             {
//                 _joinInProgress = false;
//                 StopJoinTimeout();
//                 SetJoinButtonInteractable(true);
//                 if (NetworkManager.Instance != null)
//                     NetworkManager.Instance.CancelPinJoinUiOverlays();
//                 ShowPrivateRoomLobbyUI();
//                 return;
//             }
//             SuppressSeatLobbyOnJoin = false;
//             PendingJoinPin = targetPin;
//             PhotonNetwork.LeaveRoom();
//             return;
//         }

//         if (!NetworkManager.IsPhotonMasterReadyForRooms())
//         {
//             PendingJoinPin = targetPin;
//             return;
//         }

//         PendingJoinPin = null;
//         Debug.Log($"[Friends] Photon JoinRoom requested for '{targetPin}'");
//         if (!PhotonNetwork.JoinRoom(targetPin))
//             RestoreJoinPanelAfterFailedJoin(0, "JoinRoom rejected");
//     }

//     void RestoreJoinPanelAfterFailedJoin(short returnCode, string message)
//     {
//         if (_handlingJoinFailure) return;
//         _handlingJoinFailure = true;

//         StopJoinTimeout();

//         Debug.Log($"[Friends] RestoreJoinPanelAfterFailedJoin | code={returnCode} | {message}");

//         EmergencyUnlockUI();

//         GameFlowState.SetPhase(GameFlowPhase.ModeSelection);

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.RestoreJoinTableScreenAfterFailedPin();

//         SetJoinButtonInteractable(true);

//         string userMsg = returnCode == 32758 || (message != null && message.Contains("does not exist"))
//             ? "Room not found. Check PIN."
//             : "Invalid PIN or Room Full!";
//         ShowUIError(userMsg);

//         GameObject joinTable = ModeManager.Instance != null ? ModeManager.Instance.ResolveJoinTablePanel() : null;
//         if (joinTable != null)
//         {
//             CanvasGroup jcg = joinTable.GetComponent<CanvasGroup>();
//             Debug.Log($"[Friends] Join panel after failure | active={joinTable.activeSelf} | alpha={(jcg != null ? jcg.alpha.ToString() : "n/a")} | blocksRaycasts={(jcg != null && jcg.blocksRaycasts)}");
//         }

//         _handlingJoinFailure = false;
//     }

//     void EmergencyUnlockUI()
//     {
//         _joinInProgress = false;
//         PendingJoinPin = null;

//         if (modesPanel == null && ModeManager.Instance != null)
//             modesPanel = ModeManager.Instance.panelModes;

//         // 1. Force unlock THIS panel
//         CanvasGroup localCg = GetComponent<CanvasGroup>();
//         if (localCg != null) { localCg.interactable = true; localCg.blocksRaycasts = true; }

//         // 2. Force unlock the Modes Panel
//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(true);
//             CanvasGroup modeCg = modesPanel.GetComponent<CanvasGroup>();
//             if (modeCg != null)
//             {
//                 modeCg.DOKill();
//                 modeCg.alpha = 1f;
//                 modeCg.interactable = true;
//                 modeCg.blocksRaycasts = true;
//             }
//         }

//         // 3. Force unlock ModeManager panels if they exist
//         if (ModeManager.Instance != null && ModeManager.Instance.panelModes != null)
//         {
//             GameObject mmPanel = ModeManager.Instance.panelModes;
//             mmPanel.SetActive(true);
//             CanvasGroup mmCg = mmPanel.GetComponent<CanvasGroup>();
//             if (mmCg != null)
//             {
//                 mmCg.DOKill();
//                 mmCg.alpha = 1f;
//                 mmCg.interactable = true;
//                 mmCg.blocksRaycasts = true;
//             }
//         }

//         // 4. Force unlock Join Table panel (PIN entry lives here)
//         if (ModeManager.Instance != null)
//         {
//             GameObject joinTable = ModeManager.Instance.ResolveJoinTablePanel();
//             if (joinTable != null)
//             {
//                 joinTable.SetActive(true);
//                 CanvasGroup joinCg = joinTable.GetComponent<CanvasGroup>();
//                 if (joinCg != null)
//                 {
//                     joinCg.DOKill();
//                     joinCg.alpha = 1f;
//                     joinCg.interactable = true;
//                     joinCg.blocksRaycasts = true;
//                 }
//             }
//         }

//         // 5. Brute-force nuke loading / cover overlays in NetworkManager
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.ForceClearBlackOverlay();
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ClearUiInputBlockers();

//             foreach (Transform child in NetworkManager.Instance.transform)
//             {
//                 string childName = child.name.ToLower();
//                 if (childName.Contains("loading") || childName.Contains("cover") || childName.Contains("block"))
//                 {
//                     child.gameObject.SetActive(false);
//                     CanvasGroup childCg = child.GetComponent<CanvasGroup>();
//                     if (childCg != null)
//                     {
//                         childCg.DOKill();
//                         childCg.blocksRaycasts = false;
//                         childCg.interactable = false;
//                     }
//                 }
//             }
//         }

//         NukeInvisibleRaycastBlockers();

//         Debug.Log("[Emergency] UI Unlocked aggressively after failure!");
//     }

//     static void NukeInvisibleRaycastBlockers()
//     {
//         Canvas rootCanvas = null;
//         if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
//             rootCanvas = NetworkManager.Instance.gameCanvasGroup.GetComponentInParent<Canvas>();
//         if (rootCanvas == null)
//             rootCanvas = FindAnyObjectByType<Canvas>();
//         if (rootCanvas == null) return;

//         foreach (CanvasGroup cg in rootCanvas.GetComponentsInChildren<CanvasGroup>(true))
//         {
//             if (cg == null) continue;

//             string n = cg.gameObject.name.ToLower();
//             bool isKnownOverlay = n.Contains("loading") || n.Contains("cover") || n.Contains("block")
//                 || n.Contains("black") || n.Contains("transition") || n.Contains("reconnect");

//             if (isKnownOverlay)
//             {
//                 cg.DOKill();
//                 cg.blocksRaycasts = false;
//                 cg.interactable = false;
//                 if (cg.alpha < 0.15f)
//                     cg.gameObject.SetActive(false);
//                 continue;
//             }

//             if (cg.gameObject.activeSelf && cg.alpha < 0.05f && cg.blocksRaycasts)
//             {
//                 cg.DOKill();
//                 cg.blocksRaycasts = false;
//                 cg.interactable = false;
//             }
//         }
//     }

//     public void ShowJoinError(string errorMsg)
//     {
//         EmergencyUnlockUI();
//         SetJoinButtonInteractable(true);
//         ShowUIError(errorMsg);
//     }

//     public void CancelPinJoinUiState()
//     {
//         _joinInProgress = false;
//         PendingJoinPin = null;
//         StopJoinTimeout();
//         SetJoinButtonInteractable(true);
//     }

//     public void ApplyPinJoinFailureUi(short returnCode, string message)
//     {
//         Debug.LogWarning($"[UI] OnJoinRoomFailed PlayFriendsJoin | code={returnCode} | {message}");
//         CancelPinJoinUiState();
//         UiFlowManager.HideAllOverlays();

//         if (ModeManager.Instance != null)
//         {
//             ModeManager.Instance.MarkFriendsPinJoinFlow();
//             ModeManager.Instance.HidePlayWithFriendsPanel();
//             ModeManager.Instance.RestoreJoinTableScreenAfterFailedPin();
//         }

//         string userMsg = returnCode == 32758 || (message != null && message.Contains("does not exist"))
//             ? "Room not found. Check PIN."
//             : "Invalid PIN! Try again.";
//         ShowUIError(userMsg);
//         Debug.Log("[UI] Restored JoinTable after failed PIN");
//     }

//     public override void OnJoinRoomFailed(short returnCode, string message)
//     {
//         if (!UiFlowManager.IsJoinAttemptCurrent(_joinAttemptToken))
//         {
//             Debug.LogWarning($"[Friends] Stale OnJoinRoomFailed ignored | code={returnCode}");
//             return;
//         }

//         if (!UiFlowManager.ShouldAcceptPhotonUiCallback())
//         {
//             Debug.LogWarning($"[Friends] OnJoinRoomFailed ignored — user left menu | code={returnCode}");
//             CancelPinJoinUiState();
//             UiFlowManager.HideAllOverlays();
//             return;
//         }

//         Debug.LogWarning($"[Friends] OnJoinRoomFailed | code={returnCode} | {message}");
//         UiFlowManager.HandlePinJoinFailed(returnCode, message);

//         // SAFETY NET: guarantees _joinInProgress resets, every blocker clears, and the user lands
//         // back on Modes/JoinTable — regardless of what UiFlowManager.HandlePinJoinFailed does above.
//         // Reuses the exact same proven path already used for synchronous JoinRoom() failures.
//         RestoreJoinPanelAfterFailedJoin(returnCode, message);
//     }

//     /// <summary>
//     /// Reliability fix: if creating OUR private friends room fails (e.g. a rare PIN collision —
//     /// Photon ErrorCode.GameIdAlreadyExists — or a transient state), regenerate a fresh PIN and
//     /// retry a few times so the room/PIN is always created. Ignored for non-private-room creates
//     /// (online / bots), which ModeManager handles.
//     /// </summary>
//     public override void OnCreateRoomFailed(short returnCode, string message)
//     {
//         if (!_creatingPrivateRoom) return;

//         Debug.LogWarning($"[Friends] Private room create failed ({returnCode}): {message}");

//         if (_createRoomRetries < MaxCreateRoomRetries && !PhotonNetwork.InRoom)
//         {
//             if (!NetworkManager.IsPhotonMasterReadyForRooms())
//             {
//                 _pendingCreatePrivateRoom = true;
//                 return;
//             }

//             _createRoomRetries++;
//             Debug.Log($"[Friends] Retrying private room creation with a new PIN (attempt {_createRoomRetries}/{MaxCreateRoomRetries}).");
//             DoCreatePrivateRoom();
//             return;
//         }

//         _creatingPrivateRoom = false;
//         _createRoomRetries = 0;
//         _pendingSeatLobbyOpen = false;
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ForceClearBlackOverlay();
//         }
//         if (ModeManager.Instance != null)
//             ModeManager.Instance.ShowModesScreenOnly();
//         ShowUIError("Could not create room. Please try again.");
//     }

//     void ShowUIError(string errorMsg)
//     {
//         if (string.IsNullOrEmpty(errorMsg)) return;

//         if (errorText != null)
//         {
//             errorText.text = errorMsg;
//             errorText.gameObject.SetActive(true);
//             return;
//         }

//         Debug.LogWarning("[Friends] " + errorMsg);
//     }

//     // ==========================================
//     // 3. WHEN ANYONE JOINS THE ROOM
//     // ==========================================

//     public override void OnJoinedRoom()
//     {
//         _friendsGameStartTriggered = false;
//         _joinInProgress = false;
//         StopJoinTimeout();
//         SetJoinButtonInteractable(true);
//         if (PhotonNetwork.CurrentRoom == null) return;

//         if (!UiFlowManager.ShouldAcceptPhotonUiCallback())
//         {
//             Debug.LogWarning("[Friends] OnJoinedRoom ignored — stale callback (user on Home).");
//             return;
//         }

//         bool isPrivateFriendsRoomEarly = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;
//         bool allowJoinedRoom = UiFlowManager.IsJoinAttemptCurrent(_joinAttemptToken)
//             || _onlineMode
//             || UiFlowManager.IsOnlineMatchmakingFlow()
//             || _pendingSeatLobbyOpen
//             || (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient)
//             || (UiFlowManager.IsPlayFriendsJoinFlow() && isPrivateFriendsRoomEarly)
//             || (UiFlowManager.IsPlayFriendsLobbyFlow() && isPrivateFriendsRoomEarly);

//         if (!allowJoinedRoom)
//         {
//             Debug.LogWarning("[Friends] OnJoinedRoom ignored — stale join attempt token.");
//             return;
//         }

//         if (_isLeavingFriendsFlow)
//         {
//             Debug.Log("[Friends] OnJoinedRoom ignored — user left friends flow; leaving room.");
//             if (PhotonNetwork.InRoom)
//                 PhotonNetwork.LeaveRoom();
//             return;
//         }

//         bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

//         // Private friends PIN room always wins over stale online matchmaking flags.
//         if (isPrivateFriendsRoom)
//         {
//             _onlineMode = false;
//             _previewBotsInOnlineLobby = false;

//             if (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient && !_pendingSeatLobbyOpen)
//             {
//                 Debug.Log("[Friends] Host eager room joined — staying on modes panel");
//                 TrySendPendingInvite();
//                 RefreshRoomIdPlaque();
//                 return;
//             }

//             Debug.Log($"[Friends] OnJoinedRoom PlayFriends | room={PhotonNetwork.CurrentRoom.Name} | players={PhotonNetwork.CurrentRoom.PlayerCount} | master={PhotonNetwork.MasterClient?.NickName} | localIsMaster={PhotonNetwork.IsMasterClient}");
//             EnsureHostActorRoomProperty();
//             if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsOnJoin))
//                 ApplyBotsIncludedState((bool)botsOnJoin);
//             if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.CancelPinJoinUiOverlays();
//             TrySendPendingInvite();

//             UiFlowManager.MarkPlayFriendsLobby();

//             if (_pendingSeatLobbyOpen)
//                 PresentSeatLobbyUI();
//             else
//             {
//                 if (ModeManager.Instance != null)
//                     ModeManager.Instance.HideJoinTablePanel();
//                 ShowPrivateRoomLobbyUI();
//             }
//             UpdatePlayerListUI();
//             return;
//         }

//         // Online matchmaking: this panel is the lobby — fill seats with real players.
//         if (_onlineMode || UiFlowManager.IsOnlineMatchmakingFlow())
//         {
//             Debug.Log($"[UI] OnJoinedRoom OnlineMatchmaking | room={PhotonNetwork.CurrentRoom.Name} | players={PhotonNetwork.CurrentRoom.PlayerCount}");
//             if (!_onlineMode)
//                 ShowOnlineMatchmakingLobby();
//             UpdatePlayerListUI();
//             return;
//         }
//     }

//     MonoBehaviour GetCoroutineRunner()
//     {
//         if (isActiveAndEnabled)
//             return this;
//         if (NetworkManager.Instance != null && NetworkManager.Instance.isActiveAndEnabled)
//             return NetworkManager.Instance;
//         return null;
//     }

//     static void StopCoroutineOnRunner(MonoBehaviour runner, Coroutine coroutine)
//     {
//         if (coroutine == null || runner == null) return;
//         if (runner.isActiveAndEnabled)
//             runner.StopCoroutine(coroutine);
//     }

//     void StopFriendsCoroutineSlot(ref Coroutine slot, ref MonoBehaviour runner)
//     {
//         if (slot == null) return;
//         StopCoroutineOnRunner(runner, slot);
//         slot = null;
//         runner = null;
//     }

//     Coroutine StartFriendsCoroutine(IEnumerator routine, ref Coroutine slot, ref MonoBehaviour runnerSlot)
//     {
//         StopFriendsCoroutineSlot(ref slot, ref runnerSlot);

//         MonoBehaviour runner = GetCoroutineRunner();
//         if (runner == null)
//             return null;

//         runnerSlot = runner;
//         slot = runner.StartCoroutine(routine);
//         return slot;
//     }

//     /// <summary>Polls player names until Photon syncs nicknames for all seated players.</summary>
//     public void BeginLobbyPlayerListRefresh()
//     {
//         StartFriendsCoroutine(LobbyPlayerListRefreshRoutine(), ref _lobbyPlayerRefreshCoroutine, ref _lobbyPlayerRefreshRunner);
//         if (_lobbyPlayerRefreshCoroutine == null)
//             UpdatePlayerListUI();
//     }

//     IEnumerator LobbyPlayerListRefreshRoutine()
//     {
//         for (int i = 0; i < 20; i++)
//         {
//             if (!PhotonNetwork.InRoom)
//                 yield break;

//             UpdatePlayerListUI();
//             yield return new WaitForSecondsRealtime(0.25f);
//         }
//         Debug.Log("[Friends] Player list updated");
//         _lobbyPlayerRefreshCoroutine = null;
//     }

//     static string GetPlayerDisplayName(Player p)
//     {
//         if (p == null) return "Player";
//         if (!string.IsNullOrWhiteSpace(p.NickName)) return p.NickName.Trim();
//         if (!string.IsNullOrWhiteSpace(p.UserId)) return p.UserId;
//         return "Player " + p.ActorNumber;
//     }

//     static int GetRoomHostActorNumber()
//     {
//         if (PhotonNetwork.InRoom
//             && PhotonNetwork.CurrentRoom != null
//             && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("HAN", out object hanObj)
//             && hanObj != null
//             && int.TryParse(hanObj.ToString(), out int storedHost))
//             return storedHost;

//         return PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : -1;
//     }

//     public static bool IsLocalRoomHost()
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return false;
//         return PhotonNetwork.LocalPlayer.ActorNumber == GetRoomHostActorNumber();
//     }

//     static void EnsureHostActorRoomProperty()
//     {
//         if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;
//         if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("HAN")) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "HAN", PhotonNetwork.LocalPlayer.ActorNumber }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     /// <summary>Host pressed Start on the seat panel — allows ModeManager to launch the match.</summary>
//     public void ConfirmHostSeatStart() => _hostConfirmedSeatStart = true;

//     /// <summary>Returns true once when the host confirmed start from the seat panel.</summary>
//     public bool ConsumeHostSeatStartConfirmation()
//     {
//         if (!_hostConfirmedSeatStart) return false;
//         _hostConfirmedSeatStart = false;
//         return true;
//     }

//     /// <summary>Resets async menu-flow flags without tearing down seat UI.</summary>
//     public void ResetMenuFlowFlags()
//     {
//         _joinInProgress = false;
//         _isLeavingRoom = false;
//         _pendingSeatLobbyOpen = false;
//         _creatingPrivateRoom = false;
//         _createRoomRetries = 0;
//     }

//     /// <summary>Full local reset when leaving / abandoning a private friends lobby.</summary>
//     public void ResetLobbyStateForLeave()
//     {
//         ResetMenuFlowFlags();
//         _hostConfirmedSeatStart = false;
//         _friendsGameStartTriggered = false;
//         _pendingSeatLobbyOpen = false;
//         if (!_pendingCreatePrivateRoom)
//             SuppressSeatLobbyOnJoin = false;
//         _onlineMode = false;

//         if (_lobbyPlayerRefreshCoroutine != null)
//             StopFriendsCoroutineSlot(ref _lobbyPlayerRefreshCoroutine, ref _lobbyPlayerRefreshRunner);

//         ResetSeatPanelUI();
//         HidePlayWithFriendsLobbyPanel();
//         HideClientWaitingPresentation();

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.HidePlayWithFriendsPanel();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ClearUiInputBlockers();
//     }

//     void SyncRoomLobbyUIForRole()
//     {
//         if (!PhotonNetwork.InRoom) return;

//         bool isHost = IsLocalRoomHost();

//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(false);
//             CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
//             if (cg != null)
//             {
//                 cg.interactable = false;
//                 cg.blocksRaycasts = false;
//             }
//         }

//         if (startGameButton != null)
//             startGameButton.SetActive(isHost);

//         ApplyClientWaitingPresentation(!isHost, "Waiting for Host...");

//         CheckPlayerCountAndToggleStart();
//     }

//     void ApplyClientWaitingPresentation(bool show, string message = "Waiting for Host...")
//     {
//         if (clientWaitingText != null)
//         {
//             if (show)
//             {
//                 clientWaitingText.fontSize = clientWaitingFontSize;
//                 clientWaitingText.fontStyle = FontStyles.Bold;
//                 clientWaitingText.text = message;
//                 clientWaitingText.gameObject.SetActive(true);
//             }
//             else
//             {
//                 clientWaitingText.gameObject.SetActive(false);
//             }
//         }

//         EnsureClientWaitingSpinner();
//         if (clientWaitingSpinner == null) return;

//         _waitingSpinnerTween?.Kill();
//         clientWaitingSpinner.gameObject.SetActive(show);

//         if (!show) return;

//         clientWaitingSpinner.localRotation = Quaternion.identity;
//         _waitingSpinnerTween = clientWaitingSpinner
//             .DORotate(new Vector3(0f, 0f, -360f), 1.1f, RotateMode.FastBeyond360)
//             .SetLoops(-1, LoopType.Restart)
//             .SetEase(Ease.Linear)
//             .SetUpdate(true);
//     }

//     void EnsureClientWaitingSpinner()
//     {
//         if (clientWaitingSpinner != null || clientWaitingText == null) return;

//         Transform parent = clientWaitingText.transform.parent;
//         if (parent == null) return;

//         Transform existing = parent.Find("WaitingSpinner");
//         if (existing != null)
//         {
//             clientWaitingSpinner = existing as RectTransform;
//             return;
//         }

//         var go = new GameObject("WaitingSpinner", typeof(RectTransform), typeof(Image));
//         go.transform.SetParent(parent, false);
//         clientWaitingSpinner = go.GetComponent<RectTransform>();

//         var textRt = clientWaitingText.rectTransform;
//         clientWaitingSpinner.anchorMin = clientWaitingSpinner.anchorMax = textRt.anchorMin;
//         clientWaitingSpinner.pivot = new Vector2(1f, 0.5f);
//         clientWaitingSpinner.sizeDelta = new Vector2(44f, 44f);
//         clientWaitingSpinner.anchoredPosition = textRt.anchoredPosition + new Vector2(-18f, 0f);

//         var img = go.GetComponent<Image>();
//         img.color = new Color(1f, 0.92f, 0.55f, 0.95f);
//         img.raycastTarget = false;

//         var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
//         ring.transform.SetParent(go.transform, false);
//         var ringRt = ring.GetComponent<RectTransform>();
//         ringRt.anchorMin = Vector2.zero;
//         ringRt.anchorMax = Vector2.one;
//         ringRt.offsetMin = new Vector2(6f, 6f);
//         ringRt.offsetMax = new Vector2(-6f, -6f);
//         var ringImg = ring.GetComponent<Image>();
//         ringImg.color = new Color(0.35f, 0.22f, 0.12f, 0.35f);
//         ringImg.raycastTarget = false;
//     }

//     void HideClientWaitingPresentation()
//     {
//         ApplyClientWaitingPresentation(false);
//     }

//     // Public (BUG 2 fix): NetworkManager.HandleJoinedRoomDeferred re-shows this as the
//     // single source of truth for the joining client's seat lobby panel.
//     public void ShowPrivateRoomLobbyUI()
//     {
//         if (_isLeavingFriendsFlow) return;
//         if (PhotonNetwork.CurrentRoom == null) return;

//         Debug.Log($"[Friends] Showing RoomLobby after join success | room={PhotonNetwork.CurrentRoom.Name} | players={PhotonNetwork.CurrentRoom.PlayerCount}");

//         GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);

//         if (ModeManager.Instance != null)
//         {
//             ModeManager.Instance.HideJoinTablePanel();
//             if (ModeManager.Instance.panelModes != null && !PhotonNetwork.IsMasterClient)
//                 ModeManager.Instance.panelModes.SetActive(false);
//             ModeManager.Instance.ShowPlayWithFriendsPanel();
//         }

//         if (!gameObject.activeInHierarchy)
//         {
//             gameObject.SetActive(true);
//             transform.SetAsLastSibling();
//         }

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ResetRoomLobbyCanvasGroup();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ForceClearBlackOverlay();

//         if (modesPanel != null && PhotonNetwork.IsMasterClient)
//             modesPanel.SetActive(false);

//         // Friends mode: show PIN/Room ID plaque, hide online timer.
//         _onlineMode = false;
//         ApplyModeControls(false);
//         SetSeatPanelTitle("SELECT CHAIRS");

//         if (pinCreationPanel != null)
//         {
//             pinCreationPanel.SetActive(true);
//             pinCreationPanel.transform.SetAsLastSibling();
//         }
//         StartRoomIdPlaqueWatch();
//         if (errorText != null) errorText.gameObject.SetActive(false);

//         if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj) && botsObj is bool botsObjVal)
//             ApplyBotsIncludedState(botsObjVal);
//         else
//             ApplyBotsIncludedState(false);

//         UpdatePlayerListUI();
//         EnsureLobbyInviteButton(true);
//         SyncRoomLobbyUIForRole();
//         BeginLobbyPlayerListRefresh();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.HideLoadingInstant();
//     }

//     public void ToggleBots()
//     {
//         if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;

//         bool newState = !areBotsIncluded;
//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
//         props["BotsIncluded"] = newState;
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     void ApplyBotsIncludedState(bool included)
//     {
//         areBotsIncluded = included;
//         if (includeBotsBtnText != null)
//             includeBotsBtnText.text = areBotsIncluded ? "Remove Bots" : "Include Bots";
//     }

//     void UpdatePlayerListUI()
//     {
//         if (playerSlotsText == null || playerSlotsText.Length == 0) return;

//         if (_onlineMode && !PhotonNetwork.InRoom)
//         {
//             ShowLocalPlayerInOnlineMatchmaking();
//             return;
//         }

//         if (!PhotonNetwork.InRoom) return;

//         Player[] currentPlayers = PhotonRoomPlayers.GetSorted();
//         int realPlayerCount = currentPlayers.Length;
//         int displaySlotsFilled = realPlayerCount;
//         if (areBotsIncluded || (_onlineMode && _previewBotsInOnlineLobby))
//             displaySlotsFilled = DeckManager.MaxTableSeats;

//         RefreshLobbyPlayerCountLabel(realPlayerCount, displaySlotsFilled);

//         for (int i = 0; i < playerSlotsText.Length; i++)
//         {
//             if (playerSlotsText[i] == null) continue;

//             if (i < realPlayerCount)
//             {
//                 Player p = currentPlayers[i];
//                 int hostActor = GetRoomHostActorNumber();
//                 bool isRoomHost = hostActor > 0 && p.ActorNumber == hostActor;
//                 string hostTag = isRoomHost ? " (Host)" : "";
//                 playerSlotsText[i].text = GetPlayerDisplayName(p) + hostTag;
//                 playerSlotsText[i].color = Color.white;
//                 SetSeatAvatar(i, GetAvatarIndexForPlayer(p), true);
//             }
//             else if (areBotsIncluded || (_onlineMode && _previewBotsInOnlineLobby))
//             {
//                 playerSlotsText[i].text = realPlayerCount == 3 && i == realPlayerCount
//                     ? "DehlaBot"
//                     : "AI Bot " + (i - realPlayerCount + 1);
//                 playerSlotsText[i].color = new Color(0.4f, 1f, 0.4f, 1f);
//                 SetSeatAvatar(i, -1, true); // fallback bot avatar
//             }
//             else
//             {
//                 playerSlotsText[i].text = _onlineMode ? "Waiting..." : "Waiting for Friend...";
//                 playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
//                 SetSeatAvatar(i, -1, false); // empty seat
//             }
//         }
//     }

//     void RefreshLobbyPlayerCountLabel(int realPlayers, int displayFilled)
//     {
//         if (_onlineMode) return;

//         if (matchmakingTimerText == null) return;

//         bool inPrivateLobby = PhotonNetwork.InRoom
//             && PhotonNetwork.CurrentRoom != null
//             && !PhotonNetwork.CurrentRoom.IsVisible
//             && !PhotonNetwork.OfflineMode;

//         if (!inPrivateLobby)
//         {
//             matchmakingTimerText.gameObject.SetActive(false);
//             return;
//         }

//         matchmakingTimerText.gameObject.SetActive(true);
//         matchmakingTimerText.text = areBotsIncluded
//             ? $"Players: {displayFilled}/{DeckManager.MaxTableSeats}"
//             : $"Players: {realPlayers}/{DeckManager.MaxTableSeats}";
//     }

//     // ==========================================
//     // SEAT AVATARS (real selected profile images)
//     // ==========================================

//     Sprite[] _avatarPoolCache;

//     /// <summary>Canonical avatar sprite pool (same list profile indices were chosen from).</summary>
//     Sprite[] GetAvatarPool()
//     {
//         if (PlayerProfileManager.Instance != null
//             && PlayerProfileManager.Instance.profileSprites != null
//             && PlayerProfileManager.Instance.profileSprites.Length > 0)
//         {
//             _avatarPoolCache = PlayerProfileManager.Instance.profileSprites;
//             return _avatarPoolCache;
//         }
//         if (_avatarPoolCache != null && _avatarPoolCache.Length > 0) return _avatarPoolCache;
//         if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
//             _avatarPoolCache = MatchmakingManager.GlobalProfileSprites.ToArray();
//         return _avatarPoolCache;
//     }

//     /// <summary>Avatar index a player selected: local uses PlayerPrefs, remote uses synced custom property.</summary>
//     int GetAvatarIndexForPlayer(Player p)
//     {
//         if (p == null) return -1;
//         if (PhotonNetwork.LocalPlayer != null && p.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
//         {
//             int local = PlayerProfileManager.GetSavedAvatarIndex();
//             if (local >= 0) return local;
//         }
//         if (p.CustomProperties != null
//             && p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val) && val != null)
//         {
//             if (val is int vi) return vi;
//             if (int.TryParse(val.ToString(), out int parsed)) return parsed;
//         }
//         return -1;
//     }

//     /// <summary>Assigns the avatar sprite for a seat. occupied=false dims the slot (empty seat).</summary>
//     void SetSeatAvatar(int seatIndex, int avatarIndex, bool occupied)
//     {
//         if (playerSlotsAvatar == null || seatIndex < 0 || seatIndex >= playerSlotsAvatar.Length) return;
//         UnityEngine.UI.Image img = playerSlotsAvatar[seatIndex];
//         if (img == null) return;

//         Sprite[] pool = GetAvatarPool();
//         if (pool != null && pool.Length > 0)
//         {
//             int idx = avatarIndex;
//             if (idx < 0 || idx >= pool.Length) idx = Mathf.Abs(seatIndex + 1) % pool.Length;
//             img.sprite = pool[idx];
//             img.preserveAspect = true;
//         }
//         // Dim empty seats, full colour for occupied ones.
//         img.color = occupied ? Color.white : new Color(1f, 1f, 1f, 0.25f);
//     }

//     void ShowLocalPlayerInOnlineMatchmaking()
//     {
//         EnsureNickname();

//         for (int i = 0; i < playerSlotsText.Length; i++)
//         {
//             if (playerSlotsText[i] == null) continue;

//             if (i == 0)
//             {
//                 playerSlotsText[i].text = MyDisplayName;
//                 playerSlotsText[i].color = Color.white;
//                 int avatarIdx = PhotonNetwork.LocalPlayer != null
//                     ? GetAvatarIndexForPlayer(PhotonNetwork.LocalPlayer)
//                     : PlayerProfileManager.GetSavedAvatarIndex();
//                 SetSeatAvatar(0, avatarIdx, true);
//             }
//             else
//             {
//                 playerSlotsText[i].text = "Waiting...";
//                 playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
//                 SetSeatAvatar(i, -1, false);
//             }
//         }
//     }

//     void ClearPlayerListUI()
//     {
//         if (playerSlotsText == null) return;

//         for (int i = 0; i < playerSlotsText.Length; i++)
//         {
//             if (playerSlotsText[i] == null) continue;
//             playerSlotsText[i].text = "Waiting for Friend...";
//             playerSlotsText[i].color = new Color(1f, 1f, 1f, 0.4f);
//         }
//     }

//     void CheckPlayerCountAndToggleStart()
//     {
//         // Online matchmaking auto-starts (DeckManager-driven) and has no manual Start button.
//         if (_onlineMode) return;

//         if (startGameButton == null)
//             UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

//         if (includeBotsButton != null)
//             includeBotsButton.SetActive(PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount < DeckManager.MaxTableSeats);

//         if (!PhotonNetwork.IsMasterClient)
//         {
//             if (startGameButton != null) startGameButton.SetActive(false);
//             return;
//         }

//         if (startGameButton == null) return;

//         // Host always sees the Start button, but it stays greyed/disabled until the
//         // table is full (4 seats) or bots are included.
//         startGameButton.SetActive(true);
//         bool canStart = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;
//         SetStartButtonInteractable(canStart);
//     }

//     void SetStartButtonInteractable(bool on)
//     {
//         if (startGameButton == null) return;

//         Button btn = startGameButton.GetComponent<Button>();
//         if (btn != null) btn.interactable = on;

//         CanvasGroup cg = startGameButton.GetComponent<CanvasGroup>();
//         if (cg == null) cg = startGameButton.AddComponent<CanvasGroup>();
//         cg.alpha = on ? 1f : 0.5f;
//         cg.interactable = on;
//         cg.blocksRaycasts = on;
//     }

//     Coroutine _roomIdRefreshCoroutine;
//     MonoBehaviour _roomIdRefreshRunner;

//     /// <summary>
//     /// Sets the ROOM ID / PIN plaque text from the current private room. Resolves the TMP label
//     /// by name if it was not wired, and ensures the plaque is visible. Shows a placeholder while
//     /// the room is still being created so the plaque never gets stuck on the editor default.
//     /// </summary>
//     void RefreshRoomIdPlaque()
//     {
//         if (generatedPinText == null)
//         {
//             if (UiSafeLookup.TryGet("Txt_GeneratedPIN", out GameObject pinGo) && pinGo != null)
//                 generatedPinText = pinGo.GetComponent<TMP_Text>();
//         }
//         if (generatedPinText == null) return;

//         if (roomIdPlaque != null && !roomIdPlaque.activeSelf)
//             roomIdPlaque.SetActive(true);
//         if (!generatedPinText.gameObject.activeSelf)
//             generatedPinText.gameObject.SetActive(true);

//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
//             generatedPinText.text = "ROOM ID :- " + PhotonNetwork.CurrentRoom.Name;
//         else
//             generatedPinText.text = "ROOM ID :- ...";
//     }

//     /// <summary>Refreshes the PIN plaque now and keeps retrying briefly until the room exists.</summary>
//     void StartRoomIdPlaqueWatch()
//     {
//         RefreshRoomIdPlaque();
//         StartFriendsCoroutine(RoomIdPlaqueWatchRoutine(), ref _roomIdRefreshCoroutine, ref _roomIdRefreshRunner);
//     }

//     IEnumerator RoomIdPlaqueWatchRoutine()
//     {
//         float timeout = 15f;
//         while (timeout > 0f)
//         {
//             RefreshRoomIdPlaque();
//             if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible)
//                 break;
//             yield return new WaitForSeconds(0.3f);
//             timeout -= 0.3f;
//         }
//         _roomIdRefreshCoroutine = null;
//         _roomIdRefreshRunner = null;
//     }

//     /// <summary>
//     /// Shows a friend-invite button on the friends seat/lobby panel that is a CLONE of the home
//     /// screen's invite button (same look, same anchored position). Tapping it opens the same
//     /// friends list (FRIENDS / REQUESTS + per-friend INVITE) over the lobby so the player can
//     /// invite friends straight into this room. Hidden during online matchmaking.
//     /// </summary>
//     void EnsureLobbyInviteButton(bool visible)
//     {
//         if (_lobbyInviteButton == null)
//             BuildLobbyInviteButton();

//         if (_lobbyInviteButton == null) return;

//         _lobbyInviteButton.SetActive(visible);
//         if (visible) _lobbyInviteButton.transform.SetAsLastSibling();
//     }

//     void BuildLobbyInviteButton()
//     {
//         UnityEngine.UI.Button homeBtn = ResolveHomeInviteButton();

//         if (homeBtn != null)
//         {
//             // Clone the EXACT home invite button so it looks identical, place it at the same
//             // anchored position, and rewire its click to open the friends list over the lobby.
//             GameObject go = Instantiate(homeBtn.gameObject, transform);
//             go.name = "FRIEND_INVITE_BUTTON";

//             RectTransform src = homeBtn.GetComponent<RectTransform>();
//             RectTransform rt = go.GetComponent<RectTransform>();
//             if (src != null && rt != null)
//             {
//                 rt.anchorMin = src.anchorMin;
//                 rt.anchorMax = src.anchorMax;
//                 rt.pivot = src.pivot;
//                 rt.sizeDelta = src.sizeDelta;
//                 rt.anchoredPosition = src.anchoredPosition;
//                 rt.localScale = src.localScale;
//             }

//             UnityEngine.UI.Button btn = go.GetComponent<UnityEngine.UI.Button>();
//             if (btn != null)
//             {
//                 btn.onClick.RemoveAllListeners();
//                 btn.onClick.AddListener(OpenLobbyFriendInvite);
//             }

//             go.SetActive(false);
//             _lobbyInviteButton = go;
//             return;
//         }

//         // Fallback: a simple labelled button if the home button could not be found.
//         GameObject fb = new GameObject("FRIEND_INVITE_BUTTON",
//             typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button));
//         fb.transform.SetParent(transform, false);

//         RectTransform frt = fb.GetComponent<RectTransform>();
//         frt.anchorMin = frt.anchorMax = new Vector2(1f, 0.5f);
//         frt.pivot = new Vector2(1f, 0.5f);
//         frt.anchoredPosition = Vector2.zero;
//         frt.sizeDelta = new Vector2(100f, 250f);

//         UnityEngine.UI.Image img = fb.GetComponent<UnityEngine.UI.Image>();
//         img.color = new Color(0.18f, 0.55f, 0.30f, 1f);

//         Button fbBtn = fb.GetComponent<Button>();
//         fbBtn.targetGraphic = img;
//         fbBtn.onClick.AddListener(OpenLobbyFriendInvite);

//         GameObject labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
//         labelGo.transform.SetParent(fb.transform, false);
//         RectTransform lrt = labelGo.GetComponent<RectTransform>();
//         lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
//         lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
//         TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
//         label.text = "FRIENDS";
//         label.fontSize = 28f;
//         label.fontStyle = FontStyles.Bold;
//         label.alignment = TextAlignmentOptions.Center;
//         label.color = Color.white;
//         label.raycastTarget = false;

//         fb.SetActive(false);
//         _lobbyInviteButton = fb;
//     }

//     /// <summary>Resolves the home-screen invite button (the one that opens the friends drawer).</summary>
//     UnityEngine.UI.Button ResolveHomeInviteButton()
//     {
//         if (FriendsDrawerController.Instance != null
//             && FriendsDrawerController.Instance.inviteFriendsButton != null)
//             return FriendsDrawerController.Instance.inviteFriendsButton;

//         FriendsDrawerController drawer = FindFirstObjectByType<FriendsDrawerController>(FindObjectsInactive.Include);
//         if (drawer != null && drawer.inviteFriendsButton != null)
//             return drawer.inviteFriendsButton;

//         return null;
//     }

//     /// <summary>
//     /// Opens the SAME home friends list (FRIENDS / REQUESTS tabs + per-friend INVITE) over the
//     /// room lobby. Inviting a friend from here sends them a Firebase invite carrying this room's
//     /// PIN; when they accept they join this room, when they decline the invite is removed.
//     /// </summary>
//     public void OpenLobbyFriendInvite()
//     {
//         FriendsDrawerController drawer = FriendsDrawerController.Instance;
//         if (drawer == null)
//             drawer = FindFirstObjectByType<FriendsDrawerController>(FindObjectsInactive.Include);

//         if (drawer == null)
//         {
//             ShowUIError("Friends list unavailable.");
//             return;
//         }

//         // The lobby lives on the active main Canvas; pass it explicitly so the drawer is
//         // re-parented onto a VISIBLE root (gameCanvasGroup/Panel_Game is inactive here).
//         Canvas canvas = GetComponentInParent<Canvas>();
//         Transform overlayRoot = canvas != null ? canvas.transform : transform.root;
//         drawer.OpenDrawerDuringGame(overlayRoot);

//         // Make sure the friends list is freshly populated with live status.
//         if (FriendsPanelUIController.Instance != null)
//             FriendsPanelUIController.Instance.RefreshAll();
//         RefreshFriendsStatus();
//     }

//     /// <summary>
//     /// Opens the seat lobby once the Photon private room exists. Shows loading until joined.
//     /// </summary>
//     public void OpenSeatLobbyWhenReady()
//     {
//         if (_isLeavingRoom) return;

//         Debug.Log("[Friends] OpenSeatLobbyWhenReady");
//         BeginFriendsFlow();
//         SuppressSeatLobbyOnJoin = false;
//         _pendingSeatLobbyOpen = true;

//         if (PhotonNetwork.InRoom)
//         {
//             PresentSeatLobbyUI();
//             return;
//         }

//         Debug.Log("[Friends] Seat lobby deferred — waiting for room create/join");
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.ShowLoading("Creating room...");
//             NetworkManager.Instance.AnimateLoadingSlider(NetworkManager.GameStartLoadingDelaySeconds);
//         }

//         CreatePrivateRoom();
//     }

//     void PresentSeatLobbyUI()
//     {
//         _pendingSeatLobbyOpen = false;
//         Debug.Log("[Friends] PresentSeatLobbyUI");

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ResetRoomLobbyCanvasGroup();
//         }

//         OnSeatPanelOpened();
//     }

//     /// <summary>
//     /// Called when the seat/lobby panel is opened (host taps Play on the modes screen).
//     /// Resets the player list and shows the Start button greyed-out until the table fills.
//     /// </summary>
//     public void OnSeatPanelOpened()
//     {
//         Debug.Log("[Friends] Seat panel opened");
//         BeginFriendsFlow();
//         SuppressSeatLobbyOnJoin = false;

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.ShowPlayWithFriendsPanel();
//         else if (!gameObject.activeInHierarchy)
//         {
//             gameObject.SetActive(true);
//             transform.SetAsLastSibling();
//         }

//         if (errorText != null) errorText.gameObject.SetActive(false);
//         ClearPlayerListUI();

//         if (startGameButton == null)
//             UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);

//         if (startGameButton != null)
//         {
//             startGameButton.SetActive(true);
//             SetStartButtonInteractable(false);
//         }

//         // Friends mode: ensure online controls are off and PIN plaque is shown.
//         _onlineMode = false;
//         ApplyModeControls(false);
//         SetSeatPanelTitle("SELECT CHAIRS");

//         // New flow: the Create Room button is hidden on the seat panel, so the host
//         // automatically creates the private room as soon as this panel opens. Friends
//         // join from the Modes screen's JOIN TABLE panel using the shown ROOM ID.
//         if (pinCreationPanel != null)
//         {
//             pinCreationPanel.SetActive(true);
//             pinCreationPanel.transform.SetAsLastSibling();
//         }

//         if (!PhotonNetwork.InRoom)
//         {
//             Debug.Log("[Friends] Seat panel opened before room ready — create still pending");
//             return;
//         }

//         UpdatePlayerListUI();

//         StartRoomIdPlaqueWatch();
//         CheckPlayerCountAndToggleStart();
//         EnsureLobbyInviteButton(true);
//     }

//     // ==========================================
//     // ONLINE MATCHMAKING (shared seat panel)
//     // ==========================================

//     /// <summary>
//     /// Shows this seat panel as the ONLINE matchmaking lobby. Hides PIN / Create / manual
//     /// Start / Bots controls, shows the countdown timer, and fills seats with real players
//     /// as they join the public room. The match auto-starts (driven by DeckManager) once the
//     /// table is full or the timer expires.
//     /// </summary>
//     public void ShowOnlineMatchmakingLobby()
//     {
//         _onlineMode = true;
//         _previewBotsInOnlineLobby = false;

//         if (ModeManager.Instance != null)
//         {
//             ModeManager.Instance.SetFriendsMatchMode(false);
//             if (ModeManager.Instance.panelHomeScreen != null)
//                 ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelHomeScreen, false);
//             if (ModeManager.Instance.panelModes != null)
//                 ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelModes, false);
//             ModeManager.Instance.HideJoinTablePanel();
//         }

//         UiFlowManager.BeginOnlineMatchmaking();

//         ModeManager.EnsurePanelHierarchyActivePublic(gameObject);

//         if (!gameObject.activeSelf)
//             gameObject.SetActive(true);

//         transform.SetAsLastSibling();

//         CanvasGroup cg = GetComponent<CanvasGroup>();
//         if (cg != null)
//         {
//             cg.DOKill();
//             cg.alpha = 1f;
//             cg.interactable = true;
//             cg.blocksRaycasts = true;
//         }

//         RectTransform rt = transform as RectTransform;
//         if (rt != null)
//         {
//             rt.localScale = Vector3.one;
//             if (Mathf.Abs(rt.anchoredPosition.y) > 5000f)
//                 rt.anchoredPosition = Vector2.zero;
//         }

//         if (errorText != null) errorText.gameObject.SetActive(false);
//         if (modesPanel != null) modesPanel.SetActive(false);
//         if (startGameButton != null) startGameButton.SetActive(false); // online auto-starts

//         ApplyModeControls(true);
//         SetSeatPanelTitle("FINDING PLAYERS");

//         if (matchmakingTimerText != null)
//         {
//             matchmakingTimerText.gameObject.SetActive(true);
//             matchmakingTimerText.text = "Finding players...";
//         }

//         ClearPlayerListUI();
//         ShowLocalPlayerInOnlineMatchmaking();
//         EnsureLobbyInviteButton(false);
//         if (PhotonNetwork.InRoom) UpdatePlayerListUI();
//     }

//     bool _previewBotsInOnlineLobby;

//     /// <summary>Forwarded from DeckManager's matchmaking countdown (players found + seconds left).</summary>
//     public void UpdateOnlineTimer(int playersFound, int countdown)
//     {
//         if (!_onlineMode) return;

//         _previewBotsInOnlineLobby = countdown <= 2 && playersFound < DeckManager.MaxTableSeats;
//         int displayCount = playersFound;
//         if (_previewBotsInOnlineLobby)
//             displayCount = DeckManager.MaxTableSeats;

//         if (matchmakingTimerText != null)
//         {
//             matchmakingTimerText.text = playersFound >= DeckManager.MaxTableSeats
//                 ? "Starting game..."
//                 : $"Players: {displayCount}/{DeckManager.MaxTableSeats}    Starting in {Mathf.Max(0, countdown)}s";
//         }

//         if (PhotonNetwork.InRoom) UpdatePlayerListUI();
//     }

//     /// <summary>Hides the seat panel (used on match found / cancel for the online flow).</summary>
//     public void HideLobby()
//     {
//         _onlineMode = false;
//         ApplyModeControls(false);
//         HidePrivateFriendsLobbyUI();

//         CanvasGroup cg = GetComponent<CanvasGroup>();
//         if (cg != null)
//         {
//             cg.DOKill();
//             cg.alpha = 0f;
//             cg.interactable = false;
//             cg.blocksRaycasts = false;
//         }

//         if (gameObject.activeSelf) gameObject.SetActive(false);
//     }

//     /// <summary>Toggles friends-only vs online-only seat-panel controls.</summary>
//     void ApplyModeControls(bool online)
//     {
//         GameObject createBtn = createRoomButton;
//         if (createBtn == null)
//         {
//             Transform t = transform.Find("ContentArea/Host Section/Btn_CreateRoom");
//             if (t != null) createBtn = t.gameObject;
//         }
//         if (createBtn != null) createBtn.SetActive(false); // room auto-creates in both flows

//         Transform join = transform.Find("ContentArea/Join Section");
//         if (join != null) join.gameObject.SetActive(false); // join handled on the modes screen

//         GameObject plaque = roomIdPlaque;
//         if (plaque == null)
//         {
//             Transform t = transform.Find("RoomIdPlaque");
//             if (t != null) plaque = t.gameObject;
//         }
//         if (plaque != null) plaque.SetActive(!online); // PIN/Room ID only for friends

//         if (online && includeBotsButton != null) includeBotsButton.SetActive(false);

//         if (matchmakingTimerPlaque != null) matchmakingTimerPlaque.SetActive(online);
//         if (matchmakingTimerText != null) matchmakingTimerText.gameObject.SetActive(online);
//     }

//     void SetSeatPanelTitle(string text)
//     {
//         Transform t = transform.Find("TitlePlaque/Title");
//         if (t == null) t = transform.Find("Title");
//         if (t != null)
//         {
//             TMP_Text label = t.GetComponent<TMP_Text>();
//             if (label != null) label.text = text;
//         }
//     }

//     /// <summary>
//     /// Seat-panel BACK button. In online matchmaking it cancels the search; in friends
//     /// mode it leaves the private room and returns to the modes screen.
//     /// </summary>
//     public void OnSeatPanelBackClicked()
//     {
//         bool onlineLobby = _onlineMode
//             || (MatchmakingManager.Instance != null && MatchmakingManager.Instance.IsSearching)
//             || GameFlowState.Current == GameFlowPhase.Matchmaking;

//         if (onlineLobby)
//         {
//             if (MatchmakingManager.Instance != null)
//                 MatchmakingManager.Instance.OnCancelClicked();
//             else
//                 HideLobby();
//             return;
//         }

//         LeaveCurrentRoom();
//     }

//     // ==========================================
//     // PROPER LEAVE ROOM (Back button) + UI RESET
//     // ==========================================

//     void StopFriendsGameStartCoroutine()
//     {
//         if (NetworkManager.Instance != null && _smoothGameStartCoroutine != null)
//         {
//             NetworkManager.Instance.StopCoroutine(_smoothGameStartCoroutine);
//             _smoothGameStartCoroutine = null;
//         }
//         _friendsGameStartTriggered = false;
//     }

//     /// <summary>
//     /// Back-button entry point. Host disband in private lobby; otherwise leave and reset UI.
//     /// </summary>
//     public void LeaveCurrentRoom()
//     {
//         if (_isLeavingRoom) return;

//         if (ShouldDisbandPrivateLobbyAsHost())
//         {
//             DisbandPrivateRoomAsHost();
//             return;
//         }

//         PerformLeaveCurrentRoom();
//     }

//     bool IsPrivateFriendsLobby()
//     {
//         return PhotonNetwork.InRoom
//             && PhotonNetwork.CurrentRoom != null
//             && !PhotonNetwork.CurrentRoom.IsVisible
//             && !PhotonNetwork.OfflineMode
//             && !_onlineMode;
//     }

//     bool IsFriendsMatchStarted()
//     {
//         if (_friendsGameStartTriggered) return true;

//         if (PhotonNetwork.CurrentRoom != null)
//         {
//             if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("ModesLocked", out object ml)
//                 && ml is bool locked && locked)
//                 return true;

//             if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs)
//                 && gs is bool inGame && inGame)
//                 return true;
//         }

//         return GameFlowState.Current == GameFlowPhase.InGame
//             || GameFlowState.Current == GameFlowPhase.Dealing;
//     }

//     bool ShouldDisbandPrivateLobbyAsHost()
//     {
//         return PhotonNetwork.IsMasterClient
//             && IsPrivateFriendsLobby()
//             && !IsFriendsMatchStarted();
//     }

//     void DisbandPrivateRoomAsHost()
//     {
//         Debug.Log("[Friends] Host leaving lobby — disbanding room for all players.");

//         SendFriendsRpc("RPC_Friends_RoomDisbandedByHost", RpcTarget.Others);

//         if (PhotonNetwork.CurrentRoom != null)
//         {
//             PhotonNetwork.CurrentRoom.IsOpen = false;
//             PhotonNetwork.CurrentRoom.SetCustomProperties(
//                 new ExitGames.Client.Photon.Hashtable { { "Disbanded", true } });
//         }

//         PerformLeaveCurrentRoom();
//     }

//     [PunRPC]
//     void RPC_Friends_RoomDisbandedByHost() => HandleRoomDisbandedByHost();

//     void HandleRoomDisbandedByHost()
//     {
//         if (_isLeavingRoom) return;

//         Debug.Log("[Friends] Host disbanded the room — returning Home.");
//         UiFlowManager.MarkReturningHome();
//         StopFriendsGameStartCoroutine();
//         AbortPendingFriendsRoomCreation();
//         PendingJoinPin = null;
//         _pendingSeatLobbyOpen = false;
//         _isLeavingRoom = true;

//         if (FriendsDrawerController.Instance != null)
//             FriendsDrawerController.Instance.CloseDrawer();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.LeaveRoomAndCleanup();
//         else if (PhotonNetwork.InRoom)
//             PhotonNetwork.LeaveRoom();
//         else if (ModeManager.Instance != null)
//             ModeManager.Instance.ReturnToHomeClean();
//     }

//     void PerformLeaveCurrentRoom()
//     {
//         Debug.Log("[UI] BackFromRoom called");

//         AbortPendingFriendsRoomCreation();
//         StopFriendsGameStartCoroutine();
//         PendingJoinPin = null;
//         _pendingSeatLobbyOpen = false;
//         _isLeavingRoom = true;

//         if (FriendsDrawerController.Instance != null)
//             FriendsDrawerController.Instance.CloseDrawer();

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.LeaveRoomAndCleanup();
//             return;
//         }

//         _isLeavingRoom = false;
//         ResetLobbyStateForLeave();
//         if (PhotonNetwork.InRoom)
//             PhotonNetwork.LeaveRoom();
//         else if (ModeManager.Instance != null)
//             ModeManager.Instance.ReturnToHomeClean();
//     }

//     /// <summary>
//     /// Photon callback — fired when WE leave the room. Resets this panel's UI so no ghost state
//     /// (occupied chairs, "Remove Bots" button, stale Room ID) persists into the next session.
//     /// Navigation Home is owned by NetworkManager.OnLeftRoom. Skipped during a leave->join
//     /// transition (accepting an invite / joining a friend's room via PIN).
//     /// </summary>
//     public override void OnLeftRoom()
//     {
//         if (!string.IsNullOrEmpty(PendingJoinPin)) return;
//         _isLeavingFriendsFlow = false;
//         _isLeavingRoom = false;
//         _pendingSeatLobbyOpen = false;
//         ResetSeatPanelUI();
//     }

//     /// <summary>
//     /// Completely resets the seat/lobby UI to its empty state: placeholder Room ID, hidden
//     /// "Remove Bots" button, disabled Start, all chairs emptied, hidden friend-invite button.
//     /// Safe to call repeatedly (idempotent).
//     /// </summary>
//     public void ResetSeatPanelUI()
//     {
//         _onlineMode = false;
//         _friendsGameStartTriggered = false;

//         StopFriendsCoroutineSlot(ref _roomIdRefreshCoroutine, ref _roomIdRefreshRunner);

//         // Room ID back to placeholder (resolve the label by name if it was never wired).
//         if (generatedPinText == null
//             && UiSafeLookup.TryGet("Txt_GeneratedPIN", out GameObject pinGo) && pinGo != null)
//             generatedPinText = pinGo.GetComponent<TMP_Text>();
//         if (generatedPinText != null) generatedPinText.text = "ROOM ID :- ...";

//         // Reset + hide the Include/Remove Bots button.
//         ApplyBotsIncludedState(false);
//         if (includeBotsButton != null) includeBotsButton.SetActive(false);

//         // Disable Start.
//         if (startGameButton != null) SetStartButtonInteractable(false);

//         // Empty all chairs locally (text + avatars).
//         ClearPlayerListUI();
//         ClearSeatAvatars();

//         // Hide the lobby friend-invite button + any error text.
//         EnsureLobbyInviteButton(false);
//         if (errorText != null) errorText.gameObject.SetActive(false);
//     }

//     /// <summary>Dims/empties every seat avatar slot.</summary>
//     void ClearSeatAvatars()
//     {
//         if (playerSlotsAvatar == null) return;
//         for (int i = 0; i < playerSlotsAvatar.Length; i++)
//             SetSeatAvatar(i, -1, false);
//     }

//     /// <summary>Leaves the private (invisible) room if we are currently in one.</summary>
//     public void LeavePrivateRoomIfAny()
//     {
//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.LeaveRoomAndCleanup();
//             return;
//         }

//         SuppressSeatLobbyOnJoin = false;
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
//             && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode)
//         {
//             PhotonNetwork.LeaveRoom();
//         }
//     }

//     public override void OnPlayerEnteredRoom(Player newPlayer)
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

//         bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

//         if (isPrivateFriendsRoom)
//         {
//             _onlineMode = false;
//             _previewBotsInOnlineLobby = false;

//             if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats && areBotsIncluded)
//             {
//                 ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//                 {
//                     { "BotsIncluded", false }
//                 };
//                 PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//             }

//             if (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient)
//             {
//                 Debug.Log($"[Friends] Player joined eager invite-room: {newPlayer.NickName} | count={PhotonNetwork.CurrentRoom.PlayerCount}");
//                 UpdatePlayerListUI();
//                 return;
//             }

//             if (!gameObject.activeSelf) gameObject.SetActive(true);
//             Debug.Log($"[Friends] OnPlayerEnteredRoom | {newPlayer.NickName} | count={PhotonNetwork.CurrentRoom.PlayerCount} | master={PhotonNetwork.MasterClient?.NickName}");
//             UpdatePlayerListUI();
//             CheckPlayerCountAndToggleStart();
//             BeginLobbyPlayerListRefresh();
//             return;
//         }

//         if (_onlineMode)
//         {
//             UpdatePlayerListUI();
//         }
//     }

//     public override void OnPlayerLeftRoom(Player otherPlayer)
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

//         bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

//         if (isPrivateFriendsRoom)
//         {
//             _onlineMode = false;
//             UpdatePlayerListUI();
//             CheckPlayerCountAndToggleStart();
//             return;
//         }

//         if (_onlineMode)
//             UpdatePlayerListUI();
//     }

//     public override void OnMasterClientSwitched(Player newMasterClient)
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;

//         if (IsPrivateFriendsLobby() && !IsFriendsMatchStarted())
//         {
//             Debug.Log("[Friends] Host left before start — disbanding lobby for remaining players.");
//             HandleRoomDisbandedByHost();
//             return;
//         }

//         Debug.Log($"[Friends] MasterClient switched → {newMasterClient?.NickName}");
//         UpdatePlayerListUI();
//         SyncRoomLobbyUIForRole();
//         CheckPlayerCountAndToggleStart();
//     }

//     public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
//     {
//         // Refresh seat avatars when a player's selected avatar arrives/changes.
//         if (changedProps != null && changedProps.ContainsKey(PlayerProfileManager.PROP_AVATAR)
//             && gameObject.activeInHierarchy && PhotonNetwork.InRoom)
//         {
//             UpdatePlayerListUI();
//         }
//     }

//     // ==========================================
//     // SHARE PIN LOGIC
//     // ==========================================

//     public void ShareRoomPIN()
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible)
//             return;

//         string pin = PhotonNetwork.CurrentRoom.Name;
//         string shareMessage = $"Aaja Dehla Pakad khelte hain! Mera Private Room PIN hai: {pin}. Jaldi join kar!";

//         GUIUtility.systemCopyBuffer = shareMessage;
//         Debug.Log("Copied to clipboard: " + shareMessage);

//         if (errorText != null)
//         {
//             errorText.text = "PIN Copied!";
//             errorText.gameObject.SetActive(true);
//         }
//     }

//     // ==========================================
//     // HOST CLICKS START: OPENS MODES PANEL & HIDES FRIENDS PANEL
//     // ==========================================

//     public void OpenModesPanelForHost()
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ResolveModesPanel();
//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(true);
//             CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
//             if (cg != null)
//             {
//                 cg.interactable = true;
//                 cg.blocksRaycasts = true;
//             }
//         }

//         // RPC pehle — panel band karne se pehle clients ko notify karo.
//         // BUG 3 fix: send the DeckManager relay method (the PhotonView we route through
//         // lives on DeckManager, so the target [PunRPC] must exist on that GameObject).
//         SendFriendsRpc("RPC_ShowModesPanelToClients", RpcTarget.Others);

//         if (PhotonNetwork.CurrentRoom != null)
//             PhotonNetwork.CurrentRoom.IsOpen = false;

//         gameObject.SetActive(false);
//     }

//     [PunRPC]
//     void RPC_ShowModesPanelToClients() => ExecuteShowModesPanelToClients();

//     public void ExecuteShowModesPanelToClients()
//     {
//         ResolveModesPanel();
//         if (modesPanel != null)
//         {
//             modesPanel.SetActive(true);
//             CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>();
//             if (cg != null)
//             {
//                 cg.interactable = false;
//                 cg.blocksRaycasts = false;
//             }
//         }

//         ApplyClientWaitingPresentation(true, "Host is selecting game modes...");

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.ApplyLiveModesFromRoomIfPresent();

//         gameObject.SetActive(false);
//     }

//     // Live sync: 1 Sar=1, 2 Sar=2
//     public void HostSelectedGameMode(int modeIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "GameMode", modeIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // Live sync: 1 Taash=1, 2 Taash=2
//     public void HostSelectedTaashMode(int taashIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "TaashMode", taashIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // Live sync: Spades=1, 13th Card=2, Cut to Trump=3, Cut2Trump=4
//     public void HostSelectedTrumpMode(int trumpIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "TrumpMode", trumpIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // Live sync: Logic A=1, Logic B=2, Logic C=3
//     public void HostSelectedLogicMode(int logicIndex)
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

//         ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
//         {
//             { "LogicMode", logicIndex }
//         };
//         PhotonNetwork.CurrentRoom.SetCustomProperties(props);
//     }

//     // New flow: modes are chosen BEFORE the seat panel opens, so the seat panel's
//     // Start button now starts the game directly instead of re-opening the modes panel.
//     public void OpenModesPanel() => OnHostStartFriendsGame();

//     // Backward-compatible alias for Btn_StartPrivateGame
//     public void StartPrivateGame() => OnHostStartFriendsGame();

//     /// <summary>
//     /// Host pressed Start on the seat panel. Only proceeds when the table is full
//     /// (4 players) or bots are included, then routes through the single ModeManager
//     /// start router which performs the private-friends final start.
//     /// </summary>
//     public void OnHostStartFriendsGame()
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
//             return;

//         bool full = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;
//         if (!full)
//         {
//             ShowUIError("Need 4 players to start!");
//             return;
//         }

//         ConfirmHostSeatStart();

//         // CRITICAL FIX: Directly start the game. Modes were already selected before this screen.
//         FinalStartWithSelectedModes();
//     }

//     void ResolveModesPanel()
//     {
//         if (modesPanel == null && ModeManager.Instance != null)
//             modesPanel = ModeManager.Instance.panelModes;
//     }

//     void ResolveHomeMenuPanel()
//     {
//         if (homeMenuPanel != null) return;
//         if (NetworkManager.Instance != null)
//             homeMenuPanel = NetworkManager.Instance.homeMenuPanel;
//         else if (ModeManager.Instance != null)
//             homeMenuPanel = ModeManager.Instance.panelHomeScreen;
//     }

//     void ResolveGameTablePanel()
//     {
//         if (gameTablePanel != null) return;
//         if (NetworkManager.Instance != null)
//             gameTablePanel = NetworkManager.Instance.gameTablePanel;
//         if (gameTablePanel != null) return;
//         if (UiSafeLookup.TryGet("Panel_Game", out GameObject panelGo))
//             gameTablePanel = panelGo;
//         else if (UiSafeLookup.TryGet("[Panel_Game]", out GameObject bracketGo))
//             gameTablePanel = bracketGo;
//         if (gameTablePanel != null && NetworkManager.Instance != null)
//             NetworkManager.Instance.gameTablePanel = gameTablePanel;
//     }

//     // ==========================================
//     // TRAFFIC POLICE: MASTER START BUTTON ROUTER
//     // ==========================================

//     // The Mode Panel Start button must ALWAYS go through the single clean router in ModeManager.
//     // PlayWithFriendsManager must never decide Play Online / Play Bots routing itself.
//     public void OnModePanelStartClicked()
//     {
//         if (ModeManager.Instance != null)
//             ModeManager.Instance.StartGameFromModePanel();
//         else
//             Debug.LogError("[StartRoute] ModeManager.Instance missing — cannot route Mode Panel Start.");
//     }

//     public void OnStartButtonClick() => OnModePanelStartClicked();

//     // ==========================================
//     // FINAL CONFIRM & PLAY (HOST PRESSES START ON MODES PANEL)
//     // ==========================================

//     public void FinalStartWithSelectedModes()
//     {
//         if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
//         if (GameSettings.Instance == null) return;
//         if (_friendsGameStartTriggered) return;

//         if (startGameButton != null) SetStartButtonInteractable(false);

//         Debug.Log("Host pressed Final Start! Telling everyone to start the game...");

//         ExitGames.Client.Photon.Hashtable customRoomProperties = new ExitGames.Client.Photon.Hashtable();

//         if (ModeManager.Instance != null)
//         {
//             customRoomProperties["TM"] = ModeManager.Instance.currentTrickMode;
//             customRoomProperties["RM"] = ModeManager.Instance.currentTrumpMode;
//             customRoomProperties["SM"] = ModeManager.Instance.currentSarMode;
//             customRoomProperties["LM"] = ModeManager.Instance.currentLogicMode;
//         }
//         else
//         {
//             customRoomProperties["TM"] = GameSettings.Instance.taashCategory;
//             customRoomProperties["RM"] = 3;
//             customRoomProperties["SM"] = GameSettings.Instance.currentSarMode == SarModeType.TwoSar ? 2 : 1;
//             customRoomProperties["LM"] = 1;
//         }

//         customRoomProperties["ModesLocked"] = true;
//         customRoomProperties["GS"] = true;
//         customRoomProperties["BotsIncluded"] = areBotsIncluded;
//         customRoomProperties["HAN"] = PhotonNetwork.MasterClient.ActorNumber;

//         int realPlayers = PhotonNetwork.CurrentRoom.PlayerCount;
//         int botsNeeded = DeckManager.MaxTableSeats - realPlayers;
//         DeckManager.botActorNumbers.Clear();
//         for (int i = 0; i < botsNeeded; i++)
//             DeckManager.botActorNumbers.Add(100 + i);

//         int deckSeed = UnityEngine.Random.Range(1, int.MaxValue);
//         customRoomProperties["DS"] = deckSeed;
//         DeckManager.SetSharedDeckSeed(deckSeed);
//         customRoomProperties["BS"] = DeckManager.botActorNumbers.ToArray();

//         int[] realActorNumbers = new int[realPlayers];
//         Player[] sortedPlayers = PhotonRoomPlayers.GetSorted();
//         for (int i = 0; i < realPlayers && i < sortedPlayers.Length; i++)
//             realActorNumbers[i] = sortedPlayers[i].ActorNumber;
//         customRoomProperties["RPA"] = realActorNumbers;

//         if (DeckManager.Instance != null)
//             customRoomProperties["SMP"] = DeckManager.Instance.BuildActiveSeatList().ToArray();

//         PhotonNetwork.CurrentRoom.SetCustomProperties(customRoomProperties);

//         PhotonNetwork.CurrentRoom.IsOpen = false;
//         PhotonNetwork.CurrentRoom.IsVisible = false;

//         if (botsNeeded > 0 && DeckManager.Instance != null)
//         {
//             DeckManager.Instance.photonView.RPC(
//                 "RPC_SyncBotsOnly",
//                 RpcTarget.All,
//                 DeckManager.botActorNumbers.ToArray());
//         }

//         Debug.Log($"[Friends] Host Start | room={PhotonNetwork.CurrentRoom.Name} | realPlayers={realPlayers} | bots={botsNeeded}");

//         SendFriendsRpc("RPC_StartGameForEveryone", RpcTarget.All);
//         ExecuteFriendsGameStart();
//     }

//     [PunRPC]
//     void RPC_StartGameForEveryone() => ExecuteFriendsGameStart();

//     void ApplyFriendsStartFromRoomProperties()
//     {
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;

//         DeckManager.SyncBotSeatsFromRoomProperties();

//         if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BS", out object bsObj)
//             && bsObj is int[] bs
//             && bs.Length > 0
//             && DeckManager.botActorNumbers.Count == 0)
//         {
//             for (int i = 0; i < bs.Length; i++)
//                 DeckManager.botActorNumbers.Add(bs[i]);
//         }

//         if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DS", out object dsObj)
//             && dsObj != null && int.TryParse(dsObj.ToString(), out int ds) && ds != 0)
//             DeckManager.SetSharedDeckSeed(ds);
//     }

//     public void ExecuteFriendsGameStart()
//     {
//         if (_friendsGameStartTriggered) return;
//         _friendsGameStartTriggered = true;

//         Debug.Log("[GameStart] Friends RPC_StartGameForEveryone received");

//         if (ModeManager.Instance != null)
//             ModeManager.Instance.SyncModesFromRoom();

//         ApplyFriendsStartFromRoomProperties();

//         if (TrumpManager.Instance != null)
//         {
//             if (DeckManager.IsPrivateFriendsRoom())
//                 TrumpManager.Instance.RefreshFromRoomProperties(false);
//             else
//                 TrumpManager.ApplyTrumpForCurrentGameMode(false);
//         }

//         if (PhotonNetwork.IsMasterClient)
//         {
//             DeckManager.botActorNumbers.Clear();

//             if (PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj)
//                 && botsObj is bool botsOn)
//                 areBotsIncluded = botsOn;

//             if (PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BS", out object bsObj)
//                 && bsObj is int[] bsFromRoom)
//             {
//                 for (int i = 0; i < bsFromRoom.Length; i++)
//                     DeckManager.botActorNumbers.Add(bsFromRoom[i]);
//             }
//             else
//             {
//                 int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
//                 int botsNeeded = DeckManager.MaxTableSeats - realPlayerCount;
//                 for (int i = 0; i < botsNeeded; i++)
//                     DeckManager.botActorNumbers.Add(100 + i);
//             }

//             Debug.Log($"[Bot System] Master sync — real={PhotonNetwork.CurrentRoom.PlayerCount}, bots={DeckManager.botActorNumbers.Count}");
//         }
//         else
//         {
//             if (PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object inc)
//                 && inc is bool included)
//                 areBotsIncluded = included;

//             if (DeckManager.botActorNumbers.Count == 0)
//                 ApplyFriendsStartFromRoomProperties();
//         }

//         GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
//         UiFlowManager.MarkInGame();

//         // Run on NetworkManager (persistent) — disabling this panel must not kill the coroutine.
//         if (NetworkManager.Instance != null)
//         {
//             if (_smoothGameStartCoroutine != null)
//             {
//                 NetworkManager.Instance.StopCoroutine(_smoothGameStartCoroutine);
//                 _smoothGameStartCoroutine = null;
//             }
//             _smoothGameStartCoroutine = NetworkManager.Instance.StartCoroutine(SmoothGameStartRoutine());
//         }
//     }

//     IEnumerator SmoothGameStartRoutine()
//     {
//         const float waitDuration = 1.5f;

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.ShowLoading("Starting Game...");
//             NetworkManager.Instance.AnimateLoadingSlider(waitDuration);
//         }

//         ResolveGameTablePanel();
//         ResolveModesPanel();
//         if (modesPanel != null) modesPanel.SetActive(false);
//         HidePrivateFriendsLobbyUI();
//         if (ModeManager.Instance != null)
//             ModeManager.Instance.HidePlayWithFriendsPanel();

//         yield return new WaitForSeconds(waitDuration);

//         if (NetworkManager.Instance != null)
//         {
//             NetworkManager.Instance.CompleteLoadingSlider();
//             NetworkManager.Instance.ResetGameStartGuards();
//             NetworkManager.Instance.EnsureLocalNetworkPlayer();
//             PlayerHand.ResolveLocalHand();
//             NetworkManager.Instance.HideLoadingInstant();
//             NetworkManager.Instance.ForceClearBlackOverlay();
//             NetworkManager.Instance.BeginGameAfterRoomReady(showLoadingOverlay: false);
//         }

//         Debug.Log("[Friends] Game scene loaded smoothly!");
//         _smoothGameStartCoroutine = null;
//     }

//     void HidePlayWithFriendsLobbyPanel()
//     {
//         if (pinCreationPanel != null) pinCreationPanel.SetActive(false);
//         if (startGameButton != null) startGameButton.SetActive(false);
//     }

//     public void HidePrivateFriendsLobbyUI()
//     {
//         HidePlayWithFriendsLobbyPanel();
//         if (errorText != null) errorText.gameObject.SetActive(false);
//     }

//     public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
//     {
//         if (propertiesThatChanged == null || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
//         if (PhotonNetwork.CurrentRoom.IsVisible) return;

//         if (propertiesThatChanged.ContainsKey("ModesLocked")
//             && propertiesThatChanged["ModesLocked"] is bool locked
//             && locked)
//         {
//             // Backup path if the RPC was missed; ExecuteFriendsGameStart is idempotent.
//             ExecuteFriendsGameStart();
//             Debug.Log("Host locked the modes!");
//             return;
//         }

//         if (propertiesThatChanged.ContainsKey("Disbanded")
//             && propertiesThatChanged["Disbanded"] is bool disbanded
//             && disbanded)
//         {
//             HandleRoomDisbandedByHost();
//             return;
//         }

//         if (propertiesThatChanged.ContainsKey("HAN")
//             || propertiesThatChanged.ContainsKey("BS")
//             || propertiesThatChanged.ContainsKey("BotsIncluded")
//             || propertiesThatChanged.ContainsKey("DS"))
//         {
//             ApplyFriendsStartFromRoomProperties();
//             UpdatePlayerListUI();
//             SyncRoomLobbyUIForRole();
//             CheckPlayerCountAndToggleStart();
//         }

//         if (ModeManager.Instance == null) return;

//         if (propertiesThatChanged.TryGetValue("GameMode", out object gameModeObj) && gameModeObj is int selectedMode)
//         {
//             ModeManager.Instance.OnClick_SarMode(selectedMode, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("TrumpMode", out object trumpModeObj) && trumpModeObj is int selectedTrump)
//         {
//             ModeManager.Instance.OnClick_TrumpMode(selectedTrump, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("TaashMode", out object taashModeObj) && taashModeObj is int selectedTaash)
//         {
//             ModeManager.Instance.OnClick_TrickMode(selectedTaash, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("LogicMode", out object logicModeObj) && logicModeObj is int selectedLogic)
//         {
//             ModeManager.Instance.OnClick_LogicMode(selectedLogic, broadcastToRoom: false);
//         }

//         if (propertiesThatChanged.TryGetValue("BotsIncluded", out object botsChangedObj) && botsChangedObj is bool botsChangedVal)
//         {
//             ApplyBotsIncludedState(botsChangedVal);
//             UpdatePlayerListUI();
//             CheckPlayerCountAndToggleStart();
//         }

//         if (propertiesThatChanged.ContainsKey("BS"))
//             ApplyFriendsStartFromRoomProperties();

//         if (propertiesThatChanged.ContainsKey("HAN"))
//             UpdatePlayerListUI();

//         if (propertiesThatChanged.ContainsKey("GS")
//             && propertiesThatChanged["GS"] is bool started
//             && started
//             && !_friendsGameStartTriggered)
//         {
//             ExecuteFriendsGameStart();
//         }
//     }

//     // ==========================================
//     // 6. FRIENDS LIST LOGIC
//     // ==========================================

//     public void DisplayMyID()
//     {
//         ResolveMyUserIdText();
//         if (myUserIdText == null) return;

//         // Show the short public UID (PUBG / Free Fire style). Tap to copy it.
//         string uid = GameUidService.LocalGameUid;
//         UidUI.BindCopyLabel(myUserIdText, uid, "My UID: ");
//     }

//     void ResolveMyUserIdText()
//     {
//         if (myUserIdText != null) return;

//         // The Friends panel header has a "Text_MyID" label that may not be wired in the inspector.
//         if (FriendsPanelUIController.Instance != null)
//         {
//             foreach (Transform t in FriendsPanelUIController.Instance.GetComponentsInChildren<Transform>(true))
//             {
//                 if (t.name == "Text_MyID")
//                 {
//                     myUserIdText = t.GetComponent<TMP_Text>();
//                     break;
//                 }
//             }
//         }
//     }

//     public void UI_AddFriendBtnClicked()
//     {
//         if (addFriendInput == null) return;

//         string newFriendId = addFriendInput.text.Trim();
//         if (string.IsNullOrEmpty(newFriendId)) return;

//         SendFriendRequest(newFriendId, null);
//         addFriendInput.text = "";
//     }

//     public void AddFriend(string friendUserId, string displayName = null)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;

//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId) && friendUserId == myId)
//         {
//             ShowUIError("You cannot add yourself!");
//             return;
//         }

//         if (myFriends.Contains(friendUserId))
//         {
//             ShowUIError("Already in friends list.");
//             return;
//         }

//         myFriends.Add(friendUserId);
//         if (!string.IsNullOrEmpty(displayName))
//             friendDisplayNames[friendUserId] = displayName;
//         else if (!friendDisplayNames.ContainsKey(friendUserId))
//             friendDisplayNames[friendUserId] = friendUserId;

//         SaveFriends();
//         RefreshFriendsListUI();
//         CheckFriendsOnlineStatus();
//         Debug.Log($"[Friends] Added {friendDisplayNames[friendUserId]} ({friendUserId})");
//     }

//     /// <summary>
//     /// Task 24 — After a player is replaced/kicked, re-poll friend status a few times so the
//     /// replaced player stops showing "In Game" promptly, instead of waiting for the 45s heartbeat.
//     /// "In game" is derived from Photon room membership (FindFriends IsInRoom), so a few quick
//     /// re-polls reflect the leave as soon as the kick propagates server-side.
//     /// </summary>
//     public void RefreshInGameStatusSoon()
//     {
//         if (isActiveAndEnabled)
//             StartCoroutine(RefreshInGameStatusRoutine());
//         else if (SocialServiceBootstrap.Instance != null)
//             SocialServiceBootstrap.Instance.StartCoroutine(RefreshInGameStatusRoutine());
//     }

//     IEnumerator RefreshInGameStatusRoutine()
//     {
//         for (int i = 0; i < 3; i++)
//         {
//             yield return new WaitForSeconds(1f);
//             CheckFriendsOnlineStatus();
//         }
//     }

//     void StartPresenceHeartbeat()
//     {
//         PublishOwnPresence();
//         if (_presenceHeartbeatCoroutine != null) return;

//         if (isActiveAndEnabled)
//             _presenceHeartbeatCoroutine = StartCoroutine(PresenceHeartbeatRoutine());
//         else if (SocialServiceBootstrap.Instance != null)
//             _presenceHeartbeatCoroutine = SocialServiceBootstrap.Instance.StartCoroutine(PresenceHeartbeatRoutine());
//     }

//     IEnumerator PresenceHeartbeatRoutine()
//     {
//         var wait = new WaitForSeconds(45f);
//         while (true)
//         {
//             yield return wait;
//             PublishOwnPresence();
//         }
//     }

//     void PublishOwnPresence()
//     {
//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
//         var data = new Dictionary<string, object>
//         {
//             { "lastActive", now },
//             { "online", true }
//         };

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("users").Child(myId).Child("presence")
//             .UpdateChildrenAsync(data);
//     }

//     static bool CanCallFindFriends()
//     {
//         if (PhotonNetwork.OfflineMode) return false;
//         if (!PhotonNetwork.IsConnectedAndReady) return false;
//         if (PhotonNetwork.Server != ServerConnection.MasterServer) return false;

//         ClientState state = PhotonNetwork.NetworkClientState;
//         return state == ClientState.ConnectedToMasterServer || state == ClientState.JoinedLobby;
//     }

//     void ScheduleFindFriendsWhenReady()
//     {
//         if (_findFriendsCoroutine != null) return;

//         if (isActiveAndEnabled)
//             _findFriendsCoroutine = StartCoroutine(WaitForPhotonThenFindFriends());
//         else if (SocialServiceBootstrap.Instance != null)
//             _findFriendsCoroutine = SocialServiceBootstrap.Instance.StartCoroutine(WaitForPhotonThenFindFriends());
//     }

//     IEnumerator WaitForPhotonThenFindFriends()
//     {
//         var wait = new WaitForSeconds(0.25f);
//         for (int i = 0; i < 80; i++)
//         {
//             if (CanCallFindFriends())
//             {
//                 _findFriendsCoroutine = null;
//                 if (myFriends != null && myFriends.Count > 0)
//                     PhotonNetwork.FindFriends(myFriends.ToArray());
//                 yield break;
//             }
//             yield return wait;
//         }

//         _findFriendsCoroutine = null;
//     }

//     /// <summary>
//     /// Removes a user from the local friends list (used by the in-game player-stats popup
//     /// REMOVE action). Persists the change and refreshes any friends UI.
//     /// </summary>
//     public void RemoveFriend(string friendUserId)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;
//         if (!myFriends.Remove(friendUserId)) return;

//         friendDisplayNames.Remove(friendUserId);
//         _gameInvitesSent.Remove(friendUserId);

//         SaveFriends();
//         RefreshFriendsListUI();
//         CheckFriendsOnlineStatus();
//         Debug.Log($"[Friends] Removed {friendUserId}");
//     }

//     /// <summary>True if the given user id is already in the local friends list.</summary>
//     public bool IsFriend(string friendUserId) =>
//         !string.IsNullOrEmpty(friendUserId) && myFriends.Contains(friendUserId);

//     // ==========================================
//     // FRIEND REQUEST SYSTEM (Accept / Decline)
//     // ==========================================

//     string MyUserId
//     {
//         get
//         {
//             if (FirebaseAuth.DefaultInstance?.CurrentUser != null)
//                 return FirebaseAuth.DefaultInstance.CurrentUser.UserId;

//             return PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId ?? "";
//         }
//     }

//     string MyDisplayName
//     {
//         get
//         {
//             string savedName = PlayerPrefs.GetString("PlayerUsername", "");
//             if (!string.IsNullOrEmpty(savedName)) return savedName;

//             return string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Player" : PhotonNetwork.NickName;
//         }
//     }

//     /// <summary>Sends a friend request to the target user (they get Accept/Decline).</summary>
//     public void SendFriendRequest(string targetUserId, string targetName, System.Action<bool> onComplete = null)
//     {
//         if (string.IsNullOrEmpty(targetUserId))
//         {
//             onComplete?.Invoke(false);
//             return;
//         }
//         targetUserId = targetUserId.Trim();

//         if (FirebaseAuth.DefaultInstance?.CurrentUser == null && !Application.isEditor)
//         {
//             ShowUIError("Sign in required to send friend requests.");
//             onComplete?.Invoke(false);
//             return;
//         }

//         EnsurePhotonUserId();
//         EnsureFriendServicesStarted();

//         // The whole friend system keys on the account id (Firebase uid / Photon UserId).
//         // But the UID users see and type is the short 10-digit public GameUid. If the caller
//         // passed a GameUid (e.g. from the home "Add by UID" box), resolve it to the account id
//         // first — otherwise the request is written to a path nobody listens on and is lost.
//         if (GameUidService.LooksLikeUid(targetUserId))
//         {
//             GameUidService.ResolveFirebaseUid(targetUserId, resolved =>
//             {
//                 if (string.IsNullOrEmpty(resolved))
//                 {
//                     ShowUIError("No player found with that UID.");
//                     onComplete?.Invoke(false);
//                     return;
//                 }
//                 SendFriendRequest(resolved, targetName, onComplete);
//             });
//             return;
//         }

//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId) && targetUserId == myId)
//         {
//             ShowUIError("You cannot add yourself!");
//             onComplete?.Invoke(false);
//             return;
//         }

//         if (myFriends.Contains(targetUserId))
//         {
//             ShowUIError("Already in your friends list.");
//             onComplete?.Invoke(false);
//             return;
//         }

//         if (incomingRequests.ContainsKey(targetUserId))
//         {
//             AcceptFriendRequest(targetUserId, incomingRequests[targetUserId]);
//             onComplete?.Invoke(true);
//             return;
//         }

//         if (string.IsNullOrEmpty(myId))
//         {
//             ShowUIError("Not connected yet. Try again.");
//             onComplete?.Invoke(false);
//             return;
//         }

//         // Remember name locally so it shows correctly once accepted.
//         if (!string.IsNullOrEmpty(targetName))
//             friendDisplayNames[targetUserId] = targetName;

//         var requestData = new Dictionary<string, object>
//         {
//             { "fromUserId", myId },
//             { "fromName", MyDisplayName },
//             { "createdAt", System.DateTime.UtcNow.Ticks }
//         };

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("friend_requests").Child(targetUserId).Child(myId)
//             .SetValueAsync(requestData).ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted)
//                 {
//                     Debug.LogError("[FriendReq] Send failed: " + task.Exception);
//                     ShowUIError("Request failed. Try again.");
//                     onComplete?.Invoke(false);
//                     return;
//                 }
//                 ShowUIError(string.IsNullOrEmpty(targetName) ? "Friend request sent!" : $"Request sent to {targetName}!");
//                 Debug.Log($"[FriendReq] Sent request to {targetUserId} from {myId}");
//                 onComplete?.Invoke(true);
//             });
//     }

//     public void StartFriendRequestListener()
//     {
//         if (_requestListenerStarted) return;
//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         requestDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("friend_requests").Child(myId);
//         requestDbRef.ChildAdded += OnFriendRequestAdded;
//         requestDbRef.ChildRemoved += OnFriendRequestRemoved;
//         _requestListenerStarted = true;
//         Debug.Log("[FriendReq] Listening for friend requests on " + myId);

//         requestDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

//             foreach (DataSnapshot child in task.Result.Children)
//             {
//                 string fromId = child.Key;
//                 if (string.IsNullOrEmpty(fromId) || myFriends.Contains(fromId)) continue;
//                 string fromName = child.Child("fromName").Value?.ToString();
//                 if (string.IsNullOrEmpty(fromName))
//                     fromName = child.Child("fromUserId").Value?.ToString() ?? fromId;
//                 if (string.IsNullOrEmpty(fromName))
//                     fromName = fromId;
//                 incomingRequests[fromId] = fromName;
//             }

//             RefreshFriendsListUI();
//         });
//     }

//     void OnFriendRequestAdded(object sender, ChildChangedEventArgs args)
//     {
//         if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;

//         string fromId = args.Snapshot.Key;
//         if (string.IsNullOrEmpty(fromId) || myFriends.Contains(fromId)) return;

//         string fromName = args.Snapshot.Child("fromName").Value?.ToString();
//         if (string.IsNullOrEmpty(fromName))
//             fromName = args.Snapshot.Child("fromUserId").Value?.ToString() ?? fromId;
//         if (string.IsNullOrEmpty(fromName))
//             fromName = fromId;
//         incomingRequests[fromId] = fromName;
//         Debug.Log($"[FriendReq] Incoming request from {fromName} ({fromId})");
//         RefreshFriendsListUI();
//         NotifyRequestsChanged();

//         if (FriendsPanelUIController.Instance != null)
//             FriendsPanelUIController.Instance.ShowTab(FriendsPanelUIController.PanelTab.Requests);
//     }

//     void OnFriendRequestRemoved(object sender, ChildChangedEventArgs args)
//     {
//         if (args.Snapshot == null) return;
//         string fromId = args.Snapshot.Key;
//         if (!string.IsNullOrEmpty(fromId) && incomingRequests.Remove(fromId))
//             RefreshFriendsListUI();
//     }

//     public void AcceptFriendRequest(string fromUserId, string fromName)
//     {
//         if (string.IsNullOrEmpty(fromUserId)) return;

//         // Add them to MY friends list locally.
//         AddFriend(fromUserId, fromName);

//         // Phase 5 — persist this friendship to Firebase so it survives re-login.
//         WriteFriendToFirebase(fromUserId, fromName);

//         // Tell the requester that I accepted so they add me back.
//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId))
//         {
//             var acceptData = new Dictionary<string, object>
//             {
//                 { "name", MyDisplayName },
//                 { "createdAt", System.DateTime.UtcNow.Ticks }
//             };
//             FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//                 .Child("friend_accepts").Child(fromUserId).Child(myId)
//                 .SetValueAsync(acceptData);

//             // Remove the pending request from my inbox.
//             FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//                 .Child("friend_requests").Child(myId).Child(fromUserId)
//                 .RemoveValueAsync();
//         }

//         incomingRequests.Remove(fromUserId);
//         ShowUIError($"You and {fromName} are now friends!");
//         RefreshFriendsListUI();
//     }

//     public void DeclineFriendRequest(string fromUserId)
//     {
//         if (string.IsNullOrEmpty(fromUserId)) return;

//         string myId = MyUserId;
//         if (!string.IsNullOrEmpty(myId))
//         {
//             FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//                 .Child("friend_requests").Child(myId).Child(fromUserId)
//                 .RemoveValueAsync();
//         }

//         incomingRequests.Remove(fromUserId);
//         RefreshFriendsListUI();
//         NotifyRequestsChanged();
//     }

//     public void StartFriendAcceptListener()
//     {
//         if (_acceptListenerStarted) return;
//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         acceptDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("friend_accepts").Child(myId);
//         acceptDbRef.ChildAdded += OnFriendAcceptAdded;
//         _acceptListenerStarted = true;
//         Debug.Log("[FriendReq] Listening for friend acceptances on " + myId);

//         acceptDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

//             foreach (DataSnapshot child in task.Result.Children)
//             {
//                 if (!child.Exists) continue;
//                 string accepterId = child.Key;
//                 string accepterName = child.Child("name").Value?.ToString() ?? accepterId;
//                 if (!string.IsNullOrEmpty(accepterId) && !myFriends.Contains(accepterId))
//                 {
//                     AddFriend(accepterId, accepterName);
//                     // Phase 5 — persist this friendship to Firebase (requester side).
//                     WriteFriendToFirebase(accepterId, accepterName);
//                 }
//                 child.Reference.RemoveValueAsync();
//             }

//             RefreshFriendsListUI();
//         });
//     }

//     void OnFriendAcceptAdded(object sender, ChildChangedEventArgs args)
//     {
//         if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;

//         string accepterId = args.Snapshot.Key;
//         if (string.IsNullOrEmpty(accepterId)) return;

//         string accepterName = args.Snapshot.Child("name").Value?.ToString() ?? accepterId;
//         AddFriend(accepterId, accepterName);
//         // Phase 5 — persist this friendship to Firebase (requester side).
//         WriteFriendToFirebase(accepterId, accepterName);
//         ShowUIError($"{accepterName} accepted your request!");

//         // Consume the acceptance notice.
//         args.Snapshot.Reference.RemoveValueAsync();
//     }

//     public override void OnFriendListUpdate(List<FriendInfo> friendList)
//     {
//         friendPhotonStatus.Clear();
//         foreach (FriendInfo friend in friendList)
//             friendPhotonStatus[friend.UserId] = friend;

//         RefreshFriendsListUI();
//     }

//     public void RefreshFriendsListUI()
//     {
//         // TASK 18/25: notify any open in-game friend panels so they repaint with live presence.
//         NotifyFriendsStatusChanged();

//         if (FriendsPanelUIController.Instance != null)
//         {
//             FriendsPanelUIController.Instance.RefreshAll();
//             return;
//         }

//         RefreshFriendsListLegacy();
//     }

//     void RefreshFriendsListLegacy()
//     {
//         if (friendsListContainer == null || friendUIPrefab == null) return;

//         foreach (Transform child in friendsListContainer)
//             Destroy(child.gameObject);

//         foreach (var kvp in incomingRequests)
//         {
//             if (string.IsNullOrEmpty(kvp.Key)) continue;
//             SpawnRequestRow(kvp.Key, kvp.Value);
//         }

//         // 2) Then the accepted friends (with status + Invite).
//         foreach (string friendId in myFriends)
//         {
//             if (string.IsNullOrEmpty(friendId)) continue;
//             friendPhotonStatus.TryGetValue(friendId, out FriendInfo photonInfo);
//             SpawnFriendRow(friendId, GetFriendDisplayNameInternal(friendId), photonInfo);
//         }
//     }

//     void SpawnRequestRow(string fromId, string fromName)
//     {
//         GameObject prefab = friendRequestRowPrefab != null ? friendRequestRowPrefab : friendUIPrefab;
//         if (prefab == null || friendsListContainer == null) return;

//         GameObject row = Instantiate(prefab, friendsListContainer);

//         TMP_Text infoText = FindPrimaryLabel(row.transform);
//         if (infoText != null)
//             infoText.text = $"{fromName}\n<size=18><color=#FFD479>wants to be friends</color></size>";

//         Button acceptBtn = FindChildButton(row.transform, "AcceptButton");
//         Button declineBtn = FindChildButton(row.transform, "DeclineButton");

//         // Fallback: if named buttons not found, assume first=accept, second=decline.
//         if (acceptBtn == null || declineBtn == null)
//         {
//             Button[] buttons = row.GetComponentsInChildren<Button>(true);
//             if (buttons.Length >= 2)
//             {
//                 acceptBtn = acceptBtn ?? buttons[0];
//                 declineBtn = declineBtn ?? buttons[1];
//             }
//         }

//         if (acceptBtn != null)
//         {
//             acceptBtn.onClick.RemoveAllListeners();
//             acceptBtn.onClick.AddListener(() => AcceptFriendRequest(fromId, fromName));
//         }
//         if (declineBtn != null)
//         {
//             declineBtn.onClick.RemoveAllListeners();
//             declineBtn.onClick.AddListener(() => DeclineFriendRequest(fromId));
//         }
//     }

//     string GetFriendDisplayNameInternal(string friendId)
//     {
//         if (friendDisplayNames.TryGetValue(friendId, out string name) && !string.IsNullOrEmpty(name))
//             return name;
//         return friendId;
//     }

//     void SpawnFriendRow(string friendId, string displayName, FriendInfo photonInfo)
//     {
//         GameObject row = Instantiate(friendUIPrefab, friendsListContainer);

//         TMP_Text friendText = FindPrimaryLabel(row.transform);
//         bool online = IsFriendOnline(friendId);
//         bool inGame = IsFriendInGame(friendId);
//         string status = "🔴 Offline";
//         if (online)
//             status = inGame ? "🎮 In Game" : "🟢 Online";

//         if (friendText != null)
//         {
//             friendText.text = $"{displayName}\n{status}";
//             friendText.color = online ? Color.green : Color.gray;
//         }

//         Button inviteBtn = FindChildButton(row.transform, "InviteButton");
//         if (inviteBtn == null)
//         {
//             Button[] buttons = row.GetComponentsInChildren<Button>(true);
//             inviteBtn = buttons.Length > 0 ? buttons[buttons.Length - 1] : null;
//         }

//         if (inviteBtn != null)
//         {
//             inviteBtn.onClick.RemoveAllListeners();
//             inviteBtn.onClick.AddListener(() => InviteFriendToGame(friendId, displayName));
//             TMP_Text inviteLabel = inviteBtn.GetComponentInChildren<TMP_Text>();
//             if (inviteLabel != null) inviteLabel.text = "Invite";
//         }
//     }

//     static TMP_Text FindPrimaryLabel(Transform root)
//     {
//         TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
//         for (int i = 0; i < labels.Length; i++)
//         {
//             if (labels[i].GetComponentInParent<Button>() == null)
//                 return labels[i];
//         }
//         return labels.Length > 0 ? labels[0] : null;
//     }

//     static Button FindChildButton(Transform root, string childName)
//     {
//         foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
//         {
//             if (t.name == childName)
//                 return t.GetComponent<Button>();
//         }
//         return null;
//     }

//     public void InviteFriendToGame(string friendUserId, string friendDisplayName = null)
//     {
//         if (string.IsNullOrEmpty(friendUserId)) return;
//         if (!PhotonNetwork.IsConnectedAndReady)
//         {
//             ShowUIError("Server not ready. Wait for connection...");
//             return;
//         }

//         _pendingInviteFriendId = friendUserId;
//         _pendingInviteFriendName = string.IsNullOrEmpty(friendDisplayName)
//             ? GetFriendDisplayNameInternal(friendUserId)
//             : friendDisplayName;

//         // Offline practice tables are local-only — a real friend can never join them.
//         if (PhotonNetwork.OfflineMode)
//         {
//             ShowUIError("Can't invite friends in practice mode.");
//             _pendingInviteFriendId = null;
//             _pendingInviteFriendName = null;
//             return;
//         }

//         // Already seated at a real Photon table (online matchmaking room OR a private friends
//         // room): invite the friend straight into THIS table. We must NOT leave the room here —
//         // LeaveRoom fires OnLeftRoom -> ReturnToHomeScreen and drops the host back to the home
//         // screen. That was the old REPLACE -> homepage bug.
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
//         {
//             // The table may have been closed when the match started; reopen it so the invited
//             // friend can join and take a bot seat (DeckManager.OnPlayerEnteredRoom hands it over).
//             if (!PhotonNetwork.CurrentRoom.IsOpen)
//                 PhotonNetwork.CurrentRoom.IsOpen = true;

//             SendFirebaseInvite(_pendingInviteFriendId, PhotonNetwork.CurrentRoom.Name, _pendingInviteFriendName);
//             _pendingInviteFriendId = null;
//             _pendingInviteFriendName = null;
//             return;
//         }

//         // Not in any room (inviting from the home screen): spin up a private room. The pending
//         // invite is sent automatically once we join it (OnJoinedRoom -> TrySendPendingInvite).
//         CreatePrivateRoom();
//         ShowUIError("Creating room for invite...");
//     }

//     void TrySendPendingInvite()
//     {
//         if (string.IsNullOrEmpty(_pendingInviteFriendId)) return;
//         if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible) return;

//         SendFirebaseInvite(_pendingInviteFriendId, PhotonNetwork.CurrentRoom.Name, _pendingInviteFriendName);
//         _pendingInviteFriendId = null;
//         _pendingInviteFriendName = null;
//     }

//     void SendFirebaseInvite(string targetUserId, string roomPin, string friendName)
//     {
//         if (string.IsNullOrEmpty(targetUserId) || string.IsNullOrEmpty(roomPin)) return;

//         string fromId = MyUserId;
//         string fromName = MyDisplayName;

//         var inviteData = new Dictionary<string, object>
//         {
//             { "roomPin", roomPin },
//             { "fromUserId", fromId },
//             { "fromName", fromName },
//             { "timestamp", System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
//         };

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("invites").Child(targetUserId).Child(roomPin)
//             .SetValueAsync(inviteData).ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted)
//                 {
//                     Debug.LogError("[Invite] Firebase send failed: " + task.Exception);
//                     ShowUIError("Invite failed. Try again.");
//                     return;
//                 }

//                 MarkGameInviteSent(targetUserId);
//                 RefreshFriendsListUI();
//                 ShowUIError($"Invite sent to {friendName}!");
//                 Debug.Log($"[Invite] Sent room {roomPin} to {targetUserId}");
//             });
//     }

//     public void StartInviteListener()
//     {
//         if (_inviteListenerStarted) return;

//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         inviteDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("invites").Child(myId);
//         inviteDbRef.ChildAdded += OnIncomingInviteAdded;
//         _inviteListenerStarted = true;
//         Debug.Log("[Invite] Listening for invites on " + myId);

//         inviteDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
//         {
//             if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;

//             foreach (DataSnapshot child in task.Result.Children)
//                 TryRegisterIncomingInviteSnapshot(child);
//         });
//     }

//     void TryRegisterIncomingInviteSnapshot(DataSnapshot snapshot)
//     {
//         if (snapshot == null || !snapshot.Exists) return;

//         string inviteId = snapshot.Key;
//         string roomPin = snapshot.Child("roomPin").Value?.ToString();
//         string fromName = snapshot.Child("fromName").Value?.ToString() ?? "Friend";
//         string fromUserId = snapshot.Child("fromUserId").Value?.ToString();
//         if (string.IsNullOrEmpty(roomPin)) roomPin = inviteId;
//         if (string.IsNullOrEmpty(inviteId)) inviteId = roomPin;
//         if (string.IsNullOrEmpty(roomPin)) return;

//         if (IsInviteExpired(snapshot))
//         {
//             Debug.Log($"[Invite] Invite expired ({InviteExpirySeconds}s) — popup skipped for '{inviteId}'.");
//             RemoveInviteFromFirebase(inviteId);
//             return;
//         }

//         RegisterPendingInvite(inviteId, roomPin, fromName, fromUserId);
//         ShowIncomingInvite(fromName, roomPin, inviteId);
//     }

//     static bool IsInviteExpired(DataSnapshot snapshot)
//     {
//         long inviteTimestamp = ReadInviteTimestamp(snapshot);
//         if (inviteTimestamp <= 0) return false;

//         // Milliseconds from newer clients — normalize to seconds for comparison.
//         if (inviteTimestamp > 100_000_000_000L)
//             inviteTimestamp /= 1000;

//         long currentTimeSeconds = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
//         long diffSeconds = currentTimeSeconds - inviteTimestamp;

//         // Expired after 15s, or clock skew / bad legacy data (negative drift).
//         return diffSeconds > InviteExpirySeconds || diffSeconds < -InviteExpirySeconds;
//     }

//     static long ReadInviteTimestamp(DataSnapshot snapshot)
//     {
//         if (snapshot == null) return 0;

//         DataSnapshot tsNode = snapshot.Child("timestamp");
//         if (tsNode.Exists && tsNode.Value != null
//             && long.TryParse(tsNode.Value.ToString(), out long unixTs))
//             return unixTs;

//         // Legacy field from older builds (Ticks or unix seconds).
//         DataSnapshot createdNode = snapshot.Child("createdAt");
//         if (!createdNode.Exists || createdNode.Value == null) return 0;
//         if (!long.TryParse(createdNode.Value.ToString(), out long raw)) return 0;

//         if (raw > 1_000_000_000_000L)
//             return new System.DateTimeOffset(new System.DateTime(raw, System.DateTimeKind.Utc)).ToUnixTimeSeconds();

//         return raw;
//     }

//     void OnIncomingInviteAdded(object sender, ChildChangedEventArgs args)
//     {
//         if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;
//         TryRegisterIncomingInviteSnapshot(args.Snapshot);
//     }

//     void RegisterPendingInvite(string inviteId, string roomPin, string fromName, string fromUserId)
//     {
//         if (string.IsNullOrEmpty(inviteId) || string.IsNullOrEmpty(roomPin)) return;

//         _pendingGameInvites[inviteId] = new PendingGameInvite
//         {
//             InviteId = inviteId,
//             RoomPin = roomPin,
//             FromName = fromName,
//             FromUserId = fromUserId
//         };
//     }

//     /// <summary>Accepts a pending game invite and joins the inviter's private room.</summary>
//     public void AcceptInvite(string inviteId)
//     {
//         if (string.IsNullOrEmpty(inviteId)) return;

//         if (!_pendingGameInvites.TryGetValue(inviteId, out PendingGameInvite invite))
//         {
//             invite = new PendingGameInvite
//             {
//                 InviteId = inviteId,
//                 RoomPin = inviteId
//             };
//         }

//         string roomPin = invite.RoomPin;
//         RemoveInviteFromFirebase(invite.InviteId);
//         _pendingGameInvites.Remove(invite.InviteId);
//         IncomingInvitePopup.Dismiss();

//         if (string.IsNullOrEmpty(roomPin))
//         {
//             ShowUIError("Invite expired.");
//             return;
//         }

//         if (PhotonNetwork.InRoom)
//         {
//             // Block only if we are in an ACTIVE game (GS == true). If we are merely sitting in a
//             // lobby / our own eager private room, JoinRoomWithPINText leaves it then joins.
//             bool inActiveGame = PhotonNetwork.CurrentRoom != null
//                 && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gsObj)
//                 && gsObj is bool gsBool && gsBool;
//             if (inActiveGame)
//             {
//                 ShowUIError("Leave your current game first.");
//                 return;
//             }
//         }

//         Debug.Log($"[Invite] Accepting invite '{invite.InviteId}' -> room '{roomPin}'");

//         // TASK 7 fix: the invitee is almost always ALREADY sitting in their OWN eagerly-created
//         // private room (created when they entered the friends flow). PhotonNetwork.JoinRoom returns
//         // false WITHOUT raising OnJoinRoomFailed when you are already in a room, so the loading
//         // overlay shown by BeginJoinRoomWithLoadingFade would never hide and the invitee gets stuck
//         // on the loading screen forever. Leave our current room first, queue the PIN, and let
//         // NetworkManager.OnLeftRoom join the friend's room once we are back on the master server.
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
//         {
//             if (PhotonNetwork.CurrentRoom.Name == roomPin)
//                 return; // already in the friend's room — nothing to do.

//             SuppressSeatLobbyOnJoin = false;
//             PendingJoinPin = roomPin;
//             if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.ShowLoading("Joining friend's table...");
//             PhotonNetwork.LeaveRoom();
//             return;
//         }

//         JoinRoomWithPINText(roomPin);
//     }

//     /// <summary>Declines a pending game invite and removes it from Firebase.</summary>
//     public void DeclineInvite(string inviteId)
//     {
//         if (string.IsNullOrEmpty(inviteId)) return;

//         RemoveInviteFromFirebase(inviteId);
//         _pendingGameInvites.Remove(inviteId);
//         IncomingInvitePopup.Dismiss();
//         Debug.Log($"[Invite] Declined invite '{inviteId}'");
//     }

//     void RemoveInviteFromFirebase(string inviteId)
//     {
//         if (string.IsNullOrEmpty(inviteId)) return;

//         string myId = MyUserId;
//         if (string.IsNullOrEmpty(myId)) return;

//         FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("invites").Child(myId).Child(inviteId)
//             .RemoveValueAsync();
//     }

//     void ShowIncomingInvite(string fromName, string roomPin, string inviteId)
//     {
//         if (pinInputField != null)
//             pinInputField.text = roomPin;

//         IncomingInvitePopup.ShowInvite(fromName, roomPin, inviteId);
//         Debug.Log($"[Invite] Incoming from {fromName} — room {roomPin} (id={inviteId})");
//     }

//     void SaveFriends()
//     {
//         PlayerPrefs.SetString(FriendsPrefsKey, string.Join(",", myFriends));

//         var namePairs = new List<string>();
//         foreach (string id in myFriends)
//         {
//             if (friendDisplayNames.TryGetValue(id, out string name))
//                 namePairs.Add(id + "|" + name);
//         }
//         PlayerPrefs.SetString(FriendNamesPrefsKey, string.Join(",", namePairs));
//         PlayerPrefs.Save();
//     }

//     void LoadFriends()
//     {
//         if (myFriends == null) myFriends = new List<string>();
//         string data = PlayerPrefs.GetString(FriendsPrefsKey, "");
//         myFriends.Clear();
//         if (!string.IsNullOrEmpty(data))
//         {
//             foreach (string id in data.Split(','))
//             {
//                 if (!string.IsNullOrEmpty(id) && !myFriends.Contains(id))
//                     myFriends.Add(id);
//             }
//         }

//         friendDisplayNames.Clear();
//         string namesData = PlayerPrefs.GetString(FriendNamesPrefsKey, "");
//         if (!string.IsNullOrEmpty(namesData))
//         {
//             foreach (string pair in namesData.Split(','))
//             {
//                 int sep = pair.IndexOf('|');
//                 if (sep <= 0) continue;
//                 string id = pair.Substring(0, sep);
//                 string name = pair.Substring(sep + 1);
//                 if (!string.IsNullOrEmpty(id))
//                     friendDisplayNames[id] = name;
//             }
//         }
//     }

//     // ==========================================
//     // Phase 5 — Firebase friends persistence
//     // ==========================================

//     /// <summary>Tracks which signed-in user id we've already fetched Firebase friends for,
//     /// so the fetch runs once per login but re-runs if the user changes accounts.</summary>
//     string _friendsLoadedForUser;

//     /// <summary>
//     /// Phase 5 — Persist a single established friendship to Firebase so it survives re-login:
//     /// users/{myUid}/friends/{friendUid} = displayName. Guarded by a real signed-in Firebase user
//     /// so the generated GUID fallback is never used as a key.
//     /// </summary>
//     void WriteFriendToFirebase(string friendUid, string displayName)
//     {
//         if (string.IsNullOrEmpty(friendUid)) return;

//         Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
//         if (user == null)
//         {
//             Debug.LogWarning("[Friends] Skipped Firebase friend write — no signed-in user.");
//             return;
//         }

//         string myUid = user.UserId;
//         if (string.IsNullOrEmpty(myUid)) return;

//         string nameToStore = string.IsNullOrEmpty(displayName) ? friendUid : displayName;
//         Firebase.Database.FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("users").Child(myUid).Child("friends").Child(friendUid)
//             .SetValueAsync(nameToStore);
//         Debug.Log($"[Friends] Persisted friend to Firebase: users/{myUid}/friends/{friendUid} = {nameToStore}");
//     }

//     /// <summary>
//     /// Phase 5 — Loads the friends list from Firebase (users/{myUid}/friends) once after login and
//     /// merges each child (key=friendUid, value=displayName) into the local list/cache (dedupe),
//     /// then persists the local PlayerPrefs cache and refreshes the UI. The PlayerPrefs offline
//     /// fallback keeps working; this only augments it.
//     /// </summary>
//     public void LoadFriendsFromFirebase()
//     {
//         Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
//         if (user == null)
//         {
//             Debug.LogWarning("[Friends] LoadFriendsFromFirebase skipped — no signed-in user (offline). Using local cache.");
//             return;
//         }

//         string myUid = user.UserId;
//         if (string.IsNullOrEmpty(myUid)) return;

//         if (myFriends == null) myFriends = new List<string>();

//         Debug.Log($"[Friends] Loading friends from Firebase: users/{myUid}/friends");
//         Firebase.Database.FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference
//             .Child("users").Child(myUid).Child("friends")
//             .GetValueAsync().ContinueWithOnMainThread(task =>
//             {
//                 if (task.IsFaulted || task.IsCanceled)
//                 {
//                     Debug.LogWarning("[Friends] Failed to load friends from Firebase — keeping local cache.");
//                     return;
//                 }

//                 Firebase.Database.DataSnapshot snap = task.Result;
//                 if (snap == null || !snap.Exists)
//                 {
//                     Debug.Log("[Friends] No Firebase friends node yet — nothing to merge.");
//                     return;
//                 }

//                 int mergedCount = 0;
//                 foreach (Firebase.Database.DataSnapshot child in snap.Children)
//                 {
//                     string friendUid = child.Key;
//                     if (string.IsNullOrEmpty(friendUid)) continue;

//                     string displayName = child.Value?.ToString() ?? friendUid;
//                     if (!myFriends.Contains(friendUid))
//                     {
//                         myFriends.Add(friendUid);
//                         mergedCount++;
//                     }
//                     friendDisplayNames[friendUid] = displayName;
//                 }

//                 SaveFriends();
//                 FriendsPanelUIController.Instance?.RefreshAll();
//                 RefreshFriendsListUI();
//                 Debug.Log($"[Friends] Loaded friends from Firebase — merged {mergedCount} new, total {myFriends.Count}.");
//             });
//     }
// }


using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using Firebase.Auth;
using DG.Tweening;

public class PlayWithFriendsManager : MonoBehaviourPunCallbacks
{
    public static PlayWithFriendsManager Instance;

    public static string PendingJoinPin { get; set; }

    [Header("PIN UI Components")]
    public TMP_InputField pinInputField;
    public TMP_Text generatedPinText;
    public GameObject pinCreationPanel;
    public TMP_Text errorText;

    [Header("Lobby Buttons & Panels")]
    public GameObject startGameButton;
    public GameObject modesPanel;
    public TMP_Text clientWaitingText;
    [SerializeField] float clientWaitingFontSize = 34f;
    public RectTransform clientWaitingSpinner;

    Tween _waitingSpinnerTween;

    [Header("Online Matchmaking (shared seat panel)")]
    public TMP_Text matchmakingTimerText;
    public GameObject matchmakingTimerPlaque;
    bool _onlineMode;
    public bool IsOnlineMode => _onlineMode;

    [Header("Live Player List UI")]
    // UNITY EDITOR: set BOTH arrays size to 5 (0-3 chairs, 4 = Spectate)
    public TMP_Text[] playerSlotsText;
    public UnityEngine.UI.Image[] playerSlotsAvatar;

    const int PlayingSeatCount = 4;
    const int TotalSeatSlots = 5;
    const int SpectateSeatIndex = 4;
    const string SeatMapPropKey = "SeatMap";
    bool _seatClickHandlersWired;

    [Header("Room Creation / PIN Display")]
    public GameObject createRoomButton;
    public GameObject roomIdPlaque;

    [Header("Toggle Bot Settings")]
    public GameObject includeBotsButton;
    public TMP_Text includeBotsBtnText;
    bool areBotsIncluded;

    [Header("Game Table UI")]
    public GameObject homeMenuPanel;
    public GameObject gameTablePanel;

    [Header("Card Back Styles (Inventory Classic / Modern)")]
    [Tooltip("Red classic card back — used when inventory Classic is equipped.")]
    public Sprite classicCardBackSprite;
    [Tooltip("Blue modern card back — used when inventory Modern is equipped.")]
    public Sprite modernCardBackSprite;

    [Header("Friends UI Slots")]
    public TMP_Text myUserIdText;
    public TMP_InputField addFriendInput;
    public Transform friendsListContainer;
    public GameObject friendUIPrefab;

    [Header("Friend Requests UI")]
    public GameObject friendRequestRowPrefab;

    readonly Dictionary<string, string> incomingRequests = new Dictionary<string, string>();
    DatabaseReference requestDbRef;
    DatabaseReference acceptDbRef;
    bool _requestListenerStarted;
    bool _acceptListenerStarted;

    private const string FriendsPrefsKey = "SavedFriendsList";
    private const string FriendNamesPrefsKey = "SavedFriendsNames";
    // Old Firebase RTDB (mindi-kot) — kept for rollback:
    // private const string FirebaseDatabaseUrl = "https://dehla-pakad-mindi-kot-c0645-default-rtdb.firebaseio.com/";
    private const string FirebaseDatabaseUrl = "https://dehlapakad-c207c-default-rtdb.firebaseio.com/";
    public List<string> myFriends = new List<string>();
    readonly Dictionary<string, string> friendDisplayNames = new Dictionary<string, string>();
    readonly Dictionary<string, FriendInfo> friendPhotonStatus = new Dictionary<string, FriendInfo>();
    readonly Dictionary<string, long> friendFirebaseLastActiveMs = new Dictionary<string, long>();
    readonly Dictionary<string, bool> friendFirebaseOnlineFlag = new Dictionary<string, bool>();
    readonly Dictionary<string, (DatabaseReference Ref, EventHandler<ValueChangedEventArgs> Handler)> _presenceListeners =
        new Dictionary<string, (DatabaseReference, EventHandler<ValueChangedEventArgs>)>();
    readonly Dictionary<string, PendingGameInvite> _pendingGameInvites = new Dictionary<string, PendingGameInvite>();
    Coroutine _presenceHeartbeatCoroutine;
    const long FirebaseOnlineThresholdMs = 120_000;

    struct PendingGameInvite
    {
        public string InviteId;
        public string RoomPin;
        public string FromName;
        public string FromUserId;
    }
    PhotonView _photonView;
    DatabaseReference inviteDbRef;
    
    const long InviteExpirySeconds = 15;
    
    string _pendingInviteFriendId;
    string _pendingInviteFriendName;
    bool _inviteListenerStarted;
    string _listenersBoundUserId;
    readonly HashSet<string> _gameInvitesSent = new HashSet<string>();
    bool _pendingCreatePrivateRoom;
    bool _isLeavingFriendsFlow;

    public static bool IsFriendsPrivateRoomCreatePending()
    {
        return Instance != null
            && Instance._pendingCreatePrivateRoom
            && !Instance._isLeavingFriendsFlow;
    }

    public bool IsAwaitingFriendsSeatLobby() => _pendingSeatLobbyOpen;

    public void AbortPendingFriendsRoomCreation()
    {
        _isLeavingFriendsFlow = true;
        _pendingCreatePrivateRoom = false;
        _pendingSeatLobbyOpen = false;
        _creatingPrivateRoom = false;
        SuppressSeatLobbyOnJoin = false;

        if (ModeManager.Instance != null)
            ModeManager.Instance.EndFriendsRoomCreationFlow();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.EndProtectedLoading(NetworkManager.ProtectedLoadingFlow.FriendsCreatingRoom);
            NetworkManager.Instance.EndProtectedLoading(NetworkManager.ProtectedLoadingFlow.FriendsLobby);
        }

        if (_createRoomCoroutine != null)
        {
            StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
        }
    }

    public void BeginFriendsFlow()
    {
        _isLeavingFriendsFlow = false;
    }

    public bool IsLeavingFriendsFlow => _isLeavingFriendsFlow;

    public void TryFlushPendingPrivateRoomCreate()
    {
        if (_isLeavingFriendsFlow || !_pendingCreatePrivateRoom || PhotonNetwork.InRoom) return;
        if (!NetworkManager.IsPhotonMasterReadyForRooms()) return;

        if (_createRoomCoroutine != null) StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);

        _pendingCreatePrivateRoom = false;
        if (errorText != null) errorText.gameObject.SetActive(false);
        DoCreatePrivateRoom();
    }

    public void RequestPrivateRoomCreateAfterLeave()
    {
        BeginFriendsFlow();
        _pendingCreatePrivateRoom = true;
        SuppressSeatLobbyOnJoin = true;
        if (NetworkManager.Instance != null) NetworkManager.Instance.MarkReturnToFriendsModesAfterLeave();
    }

    public void ClearOnlineModeOnly()
    {
        _onlineMode = false;
        _previewBotsInOnlineLobby = false;
        ApplyModeControls(false);
        if (matchmakingTimerText != null) matchmakingTimerText.text = string.Empty;
    }
    
    bool _joinInProgress;
    bool _handlingJoinFailure;
    int _joinAttemptToken;
    Coroutine _joinTimeoutCoroutine;
    MonoBehaviour _joinTimeoutRunner;
    JoinTablePanelController _joinTableController;
    Coroutine _lobbyPlayerRefreshCoroutine;
    MonoBehaviour _lobbyPlayerRefreshRunner;

    public bool SuppressSeatLobbyOnJoin;

    Coroutine _createRoomCoroutine;
    MonoBehaviour _createRoomRunner;
    Coroutine _retryFriendServicesCoroutine;
    Coroutine _findFriendsCoroutine;
    Coroutine _smoothGameStartCoroutine;
    bool _firebaseAuthHooked;
    bool _friendsGameStartTriggered;
    bool _hostConfirmedSeatStart;
    bool _pendingSeatLobbyOpen;
    bool _isLeavingRoom;
    GameObject _lobbyInviteButton;
    bool _creatingPrivateRoom;
    int _createRoomRetries;
    const int MaxCreateRoomRetries = 5;

    public IReadOnlyList<string> MyFriends => myFriends;
    public bool IsJoinInProgress => _joinInProgress;
    public IReadOnlyDictionary<string, string> IncomingRequests => incomingRequests;

    public event System.Action RequestsChanged;
    void NotifyRequestsChanged() => RequestsChanged?.Invoke();

    public event System.Action FriendsStatusChanged;
    void NotifyFriendsStatusChanged() => FriendsStatusChanged?.Invoke();

    public string GetFriendDisplayName(string friendId) => GetFriendDisplayNameInternal(friendId);
    public string GetAccountUserId() => MyUserId;

    public FriendInfo GetFriendPhotonInfo(string friendId) =>
        friendPhotonStatus.TryGetValue(friendId, out FriendInfo info) ? info : null;

    public bool IsFriendOnline(string friendId)
    {
        if (string.IsNullOrEmpty(friendId)) return false;

        if (friendPhotonStatus.TryGetValue(friendId, out FriendInfo photonInfo) && photonInfo != null && photonInfo.IsOnline)
            return true;

        if (friendFirebaseOnlineFlag.TryGetValue(friendId, out bool firebaseOnline) && firebaseOnline)
            return true;

        if (friendFirebaseLastActiveMs.TryGetValue(friendId, out long lastMs) && lastMs > 0)
        {
            long age = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastMs;
            return age >= 0 && age <= FirebaseOnlineThresholdMs;
        }

        return false;
    }

    public bool IsFriendInGame(string friendId)
    {
        if (friendPhotonStatus.TryGetValue(friendId, out FriendInfo info) && info != null)
            return info.IsOnline && info.IsInRoom;
        return false;
    }

    public void MarkGameInviteSent(string friendUserId)
    {
        if (string.IsNullOrEmpty(friendUserId)) return;
        _gameInvitesSent.Add(friendUserId);
    }

    public void SyncFriendStatus()
    {
        EnsurePhotonUserId();
        PublishOwnPresence();

        if (myFriends == null || myFriends.Count == 0)
        {
            TearDownPresenceListeners();
            RefreshFriendsListUI();
            return;
        }

        TearDownPresenceListeners();

        DatabaseReference root = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference;
        foreach (string friendId in myFriends)
        {
            if (string.IsNullOrEmpty(friendId)) continue;
            string capturedId = friendId;
            DatabaseReference presenceRef = root.Child("users").Child(capturedId).Child("presence");

            EventHandler<ValueChangedEventArgs> handler = (_, args) => OnFriendPresenceChanged(capturedId, args);
            presenceRef.ValueChanged += handler;
            _presenceListeners[capturedId] = (presenceRef, handler);

            presenceRef.GetValueAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.Result == null) return;
                ApplyPresenceSnapshot(capturedId, task.Result);
                RefreshFriendsListUI();
            });
        }

        if (CanCallFindFriends()) PhotonNetwork.FindFriends(myFriends.ToArray());
        else if (!PhotonNetwork.InRoom) ScheduleFindFriendsWhenReady();

        RefreshFriendsListUI();
    }

    public void RefreshFriendsStatus() => SyncFriendStatus();
    public void CheckFriendsOnlineStatus() => SyncFriendStatus();

    void OnFriendPresenceChanged(string friendId, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null) return;
        ApplyPresenceSnapshot(friendId, args.Snapshot);
        RefreshFriendsListUI();
    }

    void ApplyPresenceSnapshot(string friendId, DataSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(friendId)) return;

        if (snapshot == null || !snapshot.Exists)
        {
            friendFirebaseOnlineFlag[friendId] = false;
            friendFirebaseLastActiveMs.Remove(friendId);
            return;
        }

        if (snapshot.Child("online").Exists)
        {
            object onlineVal = snapshot.Child("online").Value;
            bool online = onlineVal is bool b && b || (onlineVal != null && onlineVal.ToString().Equals("true", System.StringComparison.OrdinalIgnoreCase));
            friendFirebaseOnlineFlag[friendId] = online;
        }

        if (snapshot.Child("lastActive").Exists && long.TryParse(snapshot.Child("lastActive").Value?.ToString(), out long lastMs))
        {
            friendFirebaseLastActiveMs[friendId] = lastMs;
        }
    }

    void TearDownPresenceListeners()
    {
        foreach (var entry in _presenceListeners)
        {
            if (entry.Value.Ref != null && entry.Value.Handler != null)
                entry.Value.Ref.ValueChanged -= entry.Value.Handler;
        }
        _presenceListeners.Clear();
    }

    public void SendGameInvite(string friendId)
    {
        if (string.IsNullOrEmpty(friendId)) return;
        InviteFriendToGame(friendId, GetFriendDisplayNameInternal(friendId));
    }

    public bool IsGameInviteSent(string friendUserId) =>
        !string.IsNullOrEmpty(friendUserId) && _gameInvitesSent.Contains(friendUserId);

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(this); return; }

        LoadFriends();
        if (myFriends == null) myFriends = new List<string>();
        EnsurePhotonUserId();
        EnsureNickname();
        EnsurePhotonView();
        PhotonNetwork.AddCallbackTarget(this);
        TryHookFirebaseAuth();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        TryHookFirebaseAuth();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        UnhookFirebaseAuth();
        if (_retryFriendServicesCoroutine != null)
        {
            StopCoroutine(_retryFriendServicesCoroutine);
            _retryFriendServicesCoroutine = null;
        }
    }

    void TryHookFirebaseAuth()
    {
        if (_firebaseAuthHooked) return;
        if (FirebaseAuth.DefaultInstance == null) return;

        FirebaseAuth.DefaultInstance.StateChanged += OnFirebaseAuthStateChanged;
        _firebaseAuthHooked = true;

        if (FirebaseAuth.DefaultInstance.CurrentUser != null) EnsureFriendServicesStarted();
    }

    void UnhookFirebaseAuth()
    {
        if (!_firebaseAuthHooked || FirebaseAuth.DefaultInstance == null) return;
        FirebaseAuth.DefaultInstance.StateChanged -= OnFirebaseAuthStateChanged;
        _firebaseAuthHooked = false;
    }

    void OnFirebaseAuthStateChanged(object sender, System.EventArgs e)
    {
        if (FirebaseAuth.DefaultInstance?.CurrentUser != null)
        {
            EnsurePhotonUserId();
            EnsureFriendServicesStarted();
        }
    }

    void EnsureNickname()
    {
        string profileName = PlayerPrefs.GetString("PlayerUsername", string.Empty).Trim();
        if (!string.IsNullOrEmpty(profileName))
        {
            if (PhotonNetwork.NickName != profileName) PhotonNetwork.NickName = profileName;
            return;
        }
        if (string.IsNullOrEmpty(PhotonNetwork.NickName)) PhotonNetwork.NickName = "Player_" + UnityEngine.Random.Range(100, 999);
    }

    public void EnsureNicknamePublic() => EnsureNickname();

    void EnsurePhotonView()
    {
        if (_photonView == null) _photonView = GetComponent<PhotonView>();
    }

    static PhotonView GetReliableRpcView()
    {
        if (DeckManager.Instance != null)
        {
            PhotonView deckPv = DeckManager.Instance.photonView;
            if (deckPv != null && deckPv.ViewID > 0) return deckPv;
        }

        PlayWithFriendsManager mgr = Instance != null ? Instance : ResolveManagerInstance();
        if (mgr == null) return null;

        PhotonView localPv = mgr.photonView;
        if (localPv == null) return null;
        if (localPv.ViewID > 0) return localPv;
        if (localPv.sceneViewId > 0)
        {
            localPv.ViewID = localPv.sceneViewId;
            if (localPv.ViewID > 0) return localPv;
        }
        return null;
    }

    static PlayWithFriendsManager ResolveManagerInstance()
    {
        var all = Resources.FindObjectsOfTypeAll<PlayWithFriendsManager>();
        foreach (var m in all)
        {
            if (m == null || !m.gameObject.scene.IsValid()) continue;
            return m;
        }
        return null;
    }

    void SendFriendsRpc(string methodName, RpcTarget target)
    {
        PhotonView rpcView = GetReliableRpcView();
        if (rpcView == null || rpcView.ViewID < 1) return;
        rpcView.RPC(methodName, target);
    }

    void OnDestroy()
    {
        UnhookFirebaseAuth();
        PhotonNetwork.RemoveCallbackTarget(this);

        if (requestDbRef != null) { requestDbRef.ChildAdded -= OnFriendRequestAdded; requestDbRef.ChildRemoved -= OnFriendRequestRemoved; }
        if (acceptDbRef != null) acceptDbRef.ChildAdded -= OnFriendAcceptAdded;
        if (inviteDbRef != null) inviteDbRef.ChildAdded -= OnIncomingInviteAdded;

        TearDownPresenceListeners();
        _waitingSpinnerTween?.Kill();
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);
        HideClientWaitingPresentation();
        if (includeBotsButton != null) includeBotsButton.SetActive(false);
        EnsureLobbyChrome();

        if (_onlineMode)
        {
            ShowLocalPlayerInOnlineMatchmaking();
            return;
        }

        ClearPlayerListUI();
        EnsureFriendServicesStarted();

        if (_onlineMode) return;

        if (startGameButton != null)
        {
            startGameButton.SetActive(true);
            SetStartButtonInteractable(false);
        }
        CheckPlayerCountAndToggleStart();
    }

    public void EnsureFriendServicesStarted()
    {
        EnsurePhotonUserId();
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId))
        {
            if (isActiveAndEnabled && _retryFriendServicesCoroutine == null)
                _retryFriendServicesCoroutine = StartCoroutine(RetryFriendServicesWhenReady());
            return;
        }

        if (_retryFriendServicesCoroutine != null)
        {
            StopCoroutine(_retryFriendServicesCoroutine);
            _retryFriendServicesCoroutine = null;
        }

        if (_listenersBoundUserId != myId)
        {
            StopFriendListeners();
            _listenersBoundUserId = myId;
        }

        DisplayMyID();
        StartFriendRequestListener();
        StartFriendAcceptListener();
        StartInviteListener();
        StartPresenceHeartbeat();
        SyncFriendStatus();

        if (Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser != null && _friendsLoadedForUser != myId)
        {
            _friendsLoadedForUser = myId;
            LoadFriendsFromFirebase();
        }
    }

    bool _headlessFriendsLoaded;
    public void StartSocialServicesHeadless()
    {
        if (Instance == null) Instance = this;
        if (!_headlessFriendsLoaded)
        {
            LoadFriends();
            if (myFriends == null) myFriends = new List<string>();
            _headlessFriendsLoaded = true;
        }

        EnsurePhotonCallbacks();
        EnsurePhotonView();
        EnsureNickname();
        TryHookFirebaseAuth();
        EnsureFriendServicesStarted();
    }

    void EnsurePhotonCallbacks() => PhotonNetwork.AddCallbackTarget(this);

    IEnumerator RetryFriendServicesWhenReady()
    {
        const int maxAttempts = 30;
        for (int i = 0; i < maxAttempts; i++)
        {
            yield return new WaitForSeconds(0.5f);
            if (FirebaseAuth.DefaultInstance?.CurrentUser != null || !string.IsNullOrEmpty(MyUserId))
            {
                _retryFriendServicesCoroutine = null;
                EnsureFriendServicesStarted();
                yield break;
            }
        }
        _retryFriendServicesCoroutine = null;
    }

    void StopFriendListeners()
    {
        if (requestDbRef != null) { requestDbRef.ChildAdded -= OnFriendRequestAdded; requestDbRef.ChildRemoved -= OnFriendRequestRemoved; requestDbRef = null; }
        if (acceptDbRef != null) { acceptDbRef.ChildAdded -= OnFriendAcceptAdded; acceptDbRef = null; }
        if (inviteDbRef != null) { inviteDbRef.ChildAdded -= OnIncomingInviteAdded; inviteDbRef = null; }
        _requestListenerStarted = false;
        _acceptListenerStarted = false;
        _inviteListenerStarted = false;
        incomingRequests.Clear();
    }

    void EnsurePhotonUserId()
    {
        if (PhotonNetwork.AuthValues == null) PhotonNetwork.AuthValues = new AuthenticationValues();
        string firebaseUid = FirebaseAuth.DefaultInstance?.CurrentUser?.UserId;
        if (!string.IsNullOrEmpty(firebaseUid))
        {
            if (PhotonNetwork.AuthValues.UserId != firebaseUid)
            {
                PhotonNetwork.AuthValues.UserId = firebaseUid;
                PlayerPrefs.SetString("PhotonUserId", firebaseUid);
                PlayerPrefs.Save();
            }
            return;
        }
        if (string.IsNullOrEmpty(PhotonNetwork.AuthValues.UserId))
        {
            string uid = PlayerPrefs.GetString("PhotonUserId", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("PhotonUserId", uid);
            PlayerPrefs.Save();
            PhotonNetwork.AuthValues.UserId = uid;
        }
    }

    public void CreatePrivateRoom()
    {
        if (errorText != null) errorText.gameObject.SetActive(false);
        EnsurePhotonCallbacks();

        if (PhotonNetwork.InRoom) { ShowUIError("Leave the current room first."); return; }

        if (NetworkManager.IsPhotonMasterReadyForRooms())
        {
            _pendingCreatePrivateRoom = false;
            StopFriendsCoroutineSlot(ref _createRoomCoroutine, ref _createRoomRunner);
            DoCreatePrivateRoom();
            return;
        }

        if (PhotonNetwork.IsConnectedAndReady)
        {
            _pendingCreatePrivateRoom = true;
            TryFlushPendingPrivateRoomCreate();
            if (PhotonNetwork.InRoom || !_pendingCreatePrivateRoom) return;
        }

        if (!NetworkManager.HasInternet()) { ShowUIError("No internet connection."); return; }

        _pendingCreatePrivateRoom = true;
        if (NetworkManager.IsPhotonConnectingOrConnected()) ShowUIError("Connecting... please wait.");
        else { ShowUIError("Connecting to server..."); if (NetworkManager.Instance != null) NetworkManager.Instance.ConnectToPhoton(); }

        StartFriendsCoroutine(WaitAndCreatePrivateRoomRoutine(), ref _createRoomCoroutine, ref _createRoomRunner);
    }

    void DoCreatePrivateRoom()
    {
        if (PhotonNetwork.InRoom) return;
        if (!NetworkManager.IsPhotonMasterReadyForRooms()) { _pendingCreatePrivateRoom = true; return; }

        string newPin = GenerateRoomPin();
        _creatingPrivateRoom = true;

        int[] initialSeats = CreateEmptySeatMap();
        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = TotalSeatSlots,
            IsVisible = false,
            IsOpen = true,
            PublishUserId = true,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable { { SeatMapPropKey, initialSeats } }
        };
        PhotonNetwork.CreateRoom(newPin, roomOptions);
    }

    static string GenerateRoomPin() => UnityEngine.Random.Range(10000, 100000).ToString();

    static int[] CreateEmptySeatMap() => new int[TotalSeatSlots] { -1, -1, -1, -1, -1 };

    static int[] CloneSeatMap(int[] source)
    {
        int[] copy = CreateEmptySeatMap();
        if (source == null) return copy;
        int len = Mathf.Min(copy.Length, source.Length);
        for (int i = 0; i < len; i++) copy[i] = source[i];
        return copy;
    }

    static bool TryGetSeatMap(out int[] seatMap)
    {
        seatMap = null;
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return false;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(SeatMapPropKey, out object mapObj) || mapObj == null)
            return false;
        if (mapObj is int[] arr)
        {
            seatMap = CloneSeatMap(arr);
            return true;
        }
        return false;
    }

    void EnsureSeatMapExists()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(SeatMapPropKey)) return;
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new ExitGames.Client.Photon.Hashtable { { SeatMapPropKey, CreateEmptySeatMap() } });
    }

    void TryAutoSitLocalPlayer()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        EnsureSeatMapExists();
        if (!TryGetSeatMap(out int[] seatMap)) return;

        int myId = PhotonNetwork.LocalPlayer.ActorNumber;
        for (int i = 0; i < seatMap.Length; i++)
        {
            if (seatMap[i] == myId) return;
        }

        for (int i = 0; i < seatMap.Length; i++)
        {
            if (seatMap[i] != -1) continue;
            seatMap[i] = myId;
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new ExitGames.Client.Photon.Hashtable { { SeatMapPropKey, seatMap } });
            return;
        }
    }

    public void OnClick_ChangeSeat(int targetSeatIndex)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        if (targetSeatIndex < 0 || targetSeatIndex >= TotalSeatSlots) return;
        if (IsFriendsMatchStarted()) return;

        EnsureSeatMapExists();
        if (!TryGetSeatMap(out int[] currentSeats)) return;
        if (currentSeats[targetSeatIndex] != -1) return;

        int myActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
        for (int i = 0; i < currentSeats.Length; i++)
        {
            if (currentSeats[i] == myActorNumber)
                currentSeats[i] = -1;
        }

        currentSeats[targetSeatIndex] = myActorNumber;
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new ExitGames.Client.Photon.Hashtable { { SeatMapPropKey, currentSeats } });
    }

    void EnsureSeatClickHandlers()
    {
        if (_seatClickHandlersWired) return;
        _seatClickHandlersWired = true;

        for (int i = 0; i < TotalSeatSlots; i++)
        {
            Transform chair = FindSeatTransform(i);
            if (chair == null) continue;

            Button btn = chair.GetComponent<Button>();
            if (btn == null) btn = chair.gameObject.AddComponent<Button>();

            Image img = chair.GetComponent<Image>();
            if (img != null)
            {
                img.raycastTarget = true;
                if (btn.targetGraphic == null) btn.targetGraphic = img;
            }

            int seatIndex = i;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnClick_ChangeSeat(seatIndex));
            btn.transition = Selectable.Transition.None;
        }
    }

    Transform FindSeatTransform(int seatIndex)
    {
        string[] names = seatIndex == SpectateSeatIndex
            ? new[] { "Chair_4", "Spectate", "SpectateSeat", "Chair_Spectate" }
            : new[] { "Chair_" + seatIndex };

        foreach (string name in names)
        {
            Transform found = transform.Find(name);
            if (found != null) return found;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == name) return t;
            }
        }
        return null;
    }

    static int CountPlayingSeats(int[] seatMap)
    {
        if (seatMap == null) return 0;
        int count = 0;
        int limit = Mathf.Min(PlayingSeatCount, seatMap.Length);
        for (int i = 0; i < limit; i++)
        {
            if (seatMap[i] != -1) count++;
        }
        return count;
    }

    IEnumerator WaitAndCreatePrivateRoomRoutine()
    {
        float timeout = 25f;
        while (timeout > 0f && _pendingCreatePrivateRoom)
        {
            if (PhotonNetwork.OfflineMode || (PhotonNetwork.InRoom && PhotonNetwork.OfflineMode))
            {
                if (PhotonNetwork.InRoom)
                {
                    Debug.Log("[PhotonState] Leaving offline bot room before private room create...");
                    PhotonNetwork.LeaveRoom();
                }
                else
                {
                    PhotonNetwork.OfflineMode = false;
                    if (NetworkManager.Instance != null)
                        NetworkManager.Instance.ConnectToPhotonForOnlinePlay();
                }
            }

            if (NetworkManager.IsPhotonMasterReadyForRooms() && !PhotonNetwork.InRoom)
            {
                Debug.Log("[Friends] Photon ready, creating private room now");
                _pendingCreatePrivateRoom = false;
                _createRoomCoroutine = null;
                if (errorText != null) errorText.gameObject.SetActive(false);
                DoCreatePrivateRoom();
                yield break;
            }

            if (!NetworkManager.IsPhotonConnectingOrConnected() && NetworkManager.HasInternet() && NetworkManager.Instance != null)
                NetworkManager.Instance.ConnectToPhotonForOnlinePlay();

            yield return new WaitForSeconds(0.25f);
            timeout -= 0.25f;
        }
        _pendingCreatePrivateRoom = false;
        _createRoomCoroutine = null;
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EndProtectedLoading(NetworkManager.ProtectedLoadingFlow.FriendsCreatingRoom);
        if (!PhotonNetwork.IsConnectedAndReady) ShowUIError("Could not connect. Try again.");
    }

    public override void OnConnectedToMaster() { TryFlushPendingPrivateRoomCreate(); TryFlushPendingJoin(); CheckFriendsOnlineStatus(); }
    public override void OnJoinedLobby() { TryFlushPendingPrivateRoomCreate(); TryFlushPendingJoin(); CheckFriendsOnlineStatus(); }

    public void TryFlushPendingJoin()
    {
        if (!string.IsNullOrEmpty(PendingJoinPin) && !PhotonNetwork.InRoom)
        {
            if (!NetworkManager.IsPhotonMasterReadyForRooms()) return;

            string pin = PendingJoinPin;
            PendingJoinPin = null;

            if (!UiFlowManager.IsPlayFriendsJoinFlow()) _joinAttemptToken = UiFlowManager.BeginPinJoinAttempt();
            if (ModeManager.Instance != null) ModeManager.Instance.MarkFriendsPinJoinFlow();

            if (!_joinInProgress)
            {
                _joinInProgress = true;
                SetJoinButtonInteractable(false);
                StartJoinTimeout();
                if (NetworkManager.Instance != null) NetworkManager.Instance.CancelPinJoinUiOverlays();
            }

            if (!PhotonNetwork.JoinRoom(pin)) RestoreJoinPanelAfterFailedJoin(0, "JoinRoom rejected");
        }
    }

    void CacheJoinTableController() { if (_joinTableController == null) _joinTableController = FindAnyObjectByType<JoinTablePanelController>(); }

    public void SetJoinButtonInteractable(bool interactable)
    {
        CacheJoinTableController();
        if (_joinTableController != null) _joinTableController.SetJoinInteractable(interactable);
        if (pinInputField != null) pinInputField.interactable = interactable;
    }

    void StartJoinTimeout() { StartFriendsCoroutine(JoinTimeoutRoutine(), ref _joinTimeoutCoroutine, ref _joinTimeoutRunner); }
    void StopJoinTimeout() { StopFriendsCoroutineSlot(ref _joinTimeoutCoroutine, ref _joinTimeoutRunner); }

    IEnumerator JoinTimeoutRoutine()
    {
        yield return new WaitForSecondsRealtime(10f);
        _joinTimeoutCoroutine = null;
        if (!_joinInProgress) yield break;

        if (GameFlowState.IsActivelyPlaying) yield break;
        RestoreJoinPanelAfterFailedJoin(0, "Join timed out. Try again.");
    }

    // 🚨 BUG FIX: Join Table Button Unresponsive Issue fixed here
    public void JoinRoomWithPIN()
    {
        // Agar pehle error aaya ho aur join flag fasa ho, toh usko bypass kardo if not connecting.
        if (_joinInProgress && !NetworkManager.IsPhotonConnectingOrConnected()) 
            _joinInProgress = false; 

        if (_joinInProgress) return; 

        if (errorText != null) errorText.gameObject.SetActive(false);
        if (pinInputField == null || string.IsNullOrEmpty(pinInputField.text)) { ShowUIError("Enter valid PIN!"); return; }
        BeginPinJoin(pinInputField.text.Trim());
    }

    public void JoinRoomWithPINText(string pin)
    {
        if (_joinInProgress && !NetworkManager.IsPhotonConnectingOrConnected()) 
            _joinInProgress = false;

        if (_joinInProgress) return;

        if (errorText != null) errorText.gameObject.SetActive(false);
        if (string.IsNullOrEmpty(pin) || string.IsNullOrWhiteSpace(pin)) { ShowUIError("Enter valid PIN!"); return; }
        BeginPinJoin(pin.Trim());
    }

    void BeginPinJoin(string targetPin)
    {
        _onlineMode = false;
        _previewBotsInOnlineLobby = false;
        if (MatchmakingManager.Instance != null) { MatchmakingManager.Instance.ResetMatchmakingState(cancelledByUser: false); MatchmakingManager.Instance.HideMatchmakingPanel(); }
        _joinAttemptToken = UiFlowManager.BeginPinJoinAttempt();
        if (NetworkManager.Instance != null) NetworkManager.Instance.ClearRejoinState();
        if (ModeManager.Instance != null) ModeManager.Instance.MarkFriendsPinJoinFlow();

        _joinInProgress = true;
        SetJoinButtonInteractable(false);
        StartJoinTimeout();
        if (NetworkManager.Instance != null) NetworkManager.Instance.CancelPinJoinUiOverlays();

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            if (!NetworkManager.HasInternet()) { RestoreJoinPanelAfterFailedJoin(0, "No internet connection."); return; }
            PendingJoinPin = targetPin;
            if (NetworkManager.Instance != null) NetworkManager.Instance.ConnectToPhoton();
            return;
        }

        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.Name == targetPin)
            {
                _joinInProgress = false; StopJoinTimeout(); SetJoinButtonInteractable(true);
                if (NetworkManager.Instance != null) NetworkManager.Instance.CancelPinJoinUiOverlays();
                ShowPrivateRoomLobbyUI();
                return;
            }
            SuppressSeatLobbyOnJoin = false;
            PendingJoinPin = targetPin;
            PhotonNetwork.LeaveRoom();
            return;
        }

        if (!NetworkManager.IsPhotonMasterReadyForRooms()) { PendingJoinPin = targetPin; return; }

        PendingJoinPin = null;
        if (!PhotonNetwork.JoinRoom(targetPin)) RestoreJoinPanelAfterFailedJoin(0, "JoinRoom rejected");
    }

    void RestoreJoinPanelAfterFailedJoin(short returnCode, string message)
    {
        if (_handlingJoinFailure) return;
        _handlingJoinFailure = true;
        StopJoinTimeout();
        EmergencyUnlockUI();
        GameFlowState.SetPhase(GameFlowPhase.ModeSelection);
        if (ModeManager.Instance != null) ModeManager.Instance.RestoreJoinTableScreenAfterFailedPin();
        SetJoinButtonInteractable(true);
        string userMsg = returnCode == 32758 || (message != null && message.Contains("does not exist")) ? "Room not found. Check PIN." : "Invalid PIN or Room Full!";
        ShowUIError(userMsg);
        _handlingJoinFailure = false;
    }

    void EmergencyUnlockUI()
    {
        _joinInProgress = false;
        PendingJoinPin = null;

        if (modesPanel == null && ModeManager.Instance != null) modesPanel = ModeManager.Instance.panelModes;
        CanvasGroup localCg = GetComponent<CanvasGroup>();
        if (localCg != null) { localCg.interactable = true; localCg.blocksRaycasts = true; }

        if (modesPanel != null)
        {
            modesPanel.SetActive(true);
            CanvasGroup modeCg = modesPanel.GetComponent<CanvasGroup>();
            if (modeCg != null) { modeCg.DOKill(); modeCg.alpha = 1f; modeCg.interactable = true; modeCg.blocksRaycasts = true; }
        }

        if (ModeManager.Instance != null && ModeManager.Instance.panelModes != null)
        {
            GameObject mmPanel = ModeManager.Instance.panelModes;
            mmPanel.SetActive(true);
            CanvasGroup mmCg = mmPanel.GetComponent<CanvasGroup>();
            if (mmCg != null) { mmCg.DOKill(); mmCg.alpha = 1f; mmCg.interactable = true; mmCg.blocksRaycasts = true; }
        }

        if (ModeManager.Instance != null)
        {
            GameObject joinTable = ModeManager.Instance.ResolveJoinTablePanel();
            if (joinTable != null)
            {
                joinTable.SetActive(true);
                CanvasGroup joinCg = joinTable.GetComponent<CanvasGroup>();
                if (joinCg != null) { joinCg.DOKill(); joinCg.alpha = 1f; joinCg.interactable = true; joinCg.blocksRaycasts = true; }
            }
        }

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ForceClearBlackOverlay();
            NetworkManager.Instance.HideLoadingInstant();
            NetworkManager.Instance.ClearUiInputBlockers();
            foreach (Transform child in NetworkManager.Instance.transform)
            {
                string childName = child.name.ToLower();
                if (childName.Contains("loading") || childName.Contains("cover") || childName.Contains("block"))
                {
                    child.gameObject.SetActive(false);
                    CanvasGroup childCg = child.GetComponent<CanvasGroup>();
                    if (childCg != null) { childCg.DOKill(); childCg.blocksRaycasts = false; childCg.interactable = false; }
                }
            }
        }

        NukeInvisibleRaycastBlockers();
    }

    static void NukeInvisibleRaycastBlockers()
    {
        Canvas rootCanvas = null;
        if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null) rootCanvas = NetworkManager.Instance.gameCanvasGroup.GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindAnyObjectByType<Canvas>();
        if (rootCanvas == null) return;

        foreach (CanvasGroup cg in rootCanvas.GetComponentsInChildren<CanvasGroup>(true))
        {
            if (cg == null) continue;
            string n = cg.gameObject.name.ToLower();
            bool isKnownOverlay = n.Contains("loading") || n.Contains("cover") || n.Contains("block") || n.Contains("black") || n.Contains("transition") || n.Contains("reconnect");

            if (isKnownOverlay) { cg.DOKill(); cg.blocksRaycasts = false; cg.interactable = false; if (cg.alpha < 0.15f) cg.gameObject.SetActive(false); continue; }
            if (cg.gameObject.activeSelf && cg.alpha < 0.05f && cg.blocksRaycasts) { cg.DOKill(); cg.blocksRaycasts = false; cg.interactable = false; }
        }
    }

    public void ShowJoinError(string errorMsg) { EmergencyUnlockUI(); SetJoinButtonInteractable(true); ShowUIError(errorMsg); }
    public void CancelPinJoinUiState() { _joinInProgress = false; PendingJoinPin = null; StopJoinTimeout(); SetJoinButtonInteractable(true); }

    public void ApplyPinJoinFailureUi(short returnCode, string message)
    {
        CancelPinJoinUiState();
        UiFlowManager.HideAllOverlays();
        if (ModeManager.Instance != null) { ModeManager.Instance.MarkFriendsPinJoinFlow(); ModeManager.Instance.HidePlayWithFriendsPanel(); ModeManager.Instance.RestoreJoinTableScreenAfterFailedPin(); }
        string userMsg = returnCode == 32758 || (message != null && message.Contains("does not exist")) ? "Room not found. Check PIN." : "Invalid PIN! Try again.";
        ShowUIError(userMsg);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        if (!UiFlowManager.IsJoinAttemptCurrent(_joinAttemptToken)) return;
        if (!UiFlowManager.ShouldAcceptPhotonUiCallback()) { CancelPinJoinUiState(); UiFlowManager.HideAllOverlays(); return; }
        UiFlowManager.HandlePinJoinFailed(returnCode, message);
        RestoreJoinPanelAfterFailedJoin(returnCode, message);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        if (!_creatingPrivateRoom) return;
        if (_createRoomRetries < MaxCreateRoomRetries && !PhotonNetwork.InRoom)
        {
            if (!NetworkManager.IsPhotonMasterReadyForRooms()) { _pendingCreatePrivateRoom = true; return; }
            _createRoomRetries++; DoCreatePrivateRoom(); return;
        }

        _creatingPrivateRoom = false; _createRoomRetries = 0; _pendingSeatLobbyOpen = false;
        if (ModeManager.Instance != null)
        {
            ModeManager.Instance.EndFriendsRoomCreationFlow();
            ModeManager.Instance.ResetStartGuard();
        }
        if (NetworkManager.Instance != null) { NetworkManager.Instance.HideLoadingInstant(); NetworkManager.Instance.ForceClearBlackOverlay(); }
        if (ModeManager.Instance != null) ModeManager.Instance.ShowModesScreenOnly();
        ShowUIError("Could not create room. Please try again.");
    }

    void ShowUIError(string errorMsg)
    {
        if (string.IsNullOrEmpty(errorMsg)) return;
        if (errorText != null) { errorText.text = errorMsg; errorText.gameObject.SetActive(true); return; }
    }

    public override void OnJoinedRoom()
    {
        _friendsGameStartTriggered = false; _joinInProgress = false; StopJoinTimeout(); SetJoinButtonInteractable(true);
        if (PhotonNetwork.CurrentRoom == null) return;
        if (!UiFlowManager.ShouldAcceptPhotonUiCallback()) return;

        bool isPrivateFriendsRoomEarly = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;
        bool allowJoinedRoom = UiFlowManager.IsJoinAttemptCurrent(_joinAttemptToken) || _onlineMode || UiFlowManager.IsOnlineMatchmakingFlow() || _pendingSeatLobbyOpen || (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient) || (UiFlowManager.IsPlayFriendsJoinFlow() && isPrivateFriendsRoomEarly) || (UiFlowManager.IsPlayFriendsLobbyFlow() && isPrivateFriendsRoomEarly);

        if (!allowJoinedRoom) return;
        if (_isLeavingFriendsFlow) { if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom(); return; }

        bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;
        if (isPrivateFriendsRoom)
        {
            _onlineMode = false; _previewBotsInOnlineLobby = false;
            EnsureSeatMapExists();
            TryAutoSitLocalPlayer();
            if (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient && !_pendingSeatLobbyOpen)
            {
                TrySendPendingInvite(); RefreshRoomIdPlaque(); return;
            }
            EnsureHostActorRoomProperty();
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsOnJoin)) ApplyBotsIncludedState((bool)botsOnJoin);
            if (NetworkManager.Instance != null) NetworkManager.Instance.CancelPinJoinUiOverlays();
            TrySendPendingInvite();
            UiFlowManager.MarkPlayFriendsLobby();
            if (_pendingSeatLobbyOpen) PresentSeatLobbyUI();
            else { if (ModeManager.Instance != null) ModeManager.Instance.HideJoinTablePanel(); ShowPrivateRoomLobbyUI(); }
            UpdatePlayerListUI();
            return;
        }

        if (_onlineMode || UiFlowManager.IsOnlineMatchmakingFlow())
        {
            if (!_onlineMode) ShowOnlineMatchmakingLobby();
            UpdatePlayerListUI(); return;
        }
    }

    MonoBehaviour GetCoroutineRunner()
    {
        if (isActiveAndEnabled) return this;
        if (NetworkManager.Instance != null && NetworkManager.Instance.isActiveAndEnabled) return NetworkManager.Instance;
        return null;
    }

    static void StopCoroutineOnRunner(MonoBehaviour runner, Coroutine coroutine)
    {
        if (coroutine == null || runner == null) return;
        if (runner.isActiveAndEnabled) runner.StopCoroutine(coroutine);
    }

    void StopFriendsCoroutineSlot(ref Coroutine slot, ref MonoBehaviour runner)
    {
        if (slot == null) return;
        StopCoroutineOnRunner(runner, slot); slot = null; runner = null;
    }

    Coroutine StartFriendsCoroutine(IEnumerator routine, ref Coroutine slot, ref MonoBehaviour runnerSlot)
    {
        StopFriendsCoroutineSlot(ref slot, ref runnerSlot);
        MonoBehaviour runner = GetCoroutineRunner();
        if (runner == null) return null;
        runnerSlot = runner; slot = runner.StartCoroutine(routine);
        return slot;
    }

    public void BeginLobbyPlayerListRefresh()
    {
        StartFriendsCoroutine(LobbyPlayerListRefreshRoutine(), ref _lobbyPlayerRefreshCoroutine, ref _lobbyPlayerRefreshRunner);
        if (_lobbyPlayerRefreshCoroutine == null) UpdatePlayerListUI();
    }

    IEnumerator LobbyPlayerListRefreshRoutine()
    {
        for (int i = 0; i < 20; i++)
        {
            if (!PhotonNetwork.InRoom) yield break;
            UpdatePlayerListUI(); yield return new WaitForSecondsRealtime(0.25f);
        }
        _lobbyPlayerRefreshCoroutine = null;
    }

    static string GetPlayerDisplayName(Player p)
    {
        if (p == null) return "Player";
        if (!string.IsNullOrWhiteSpace(p.NickName)) return p.NickName.Trim();
        if (!string.IsNullOrWhiteSpace(p.UserId)) return p.UserId;
        return "Player " + p.ActorNumber;
    }

    static int GetRoomHostActorNumber()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("HAN", out object hanObj) && hanObj != null && int.TryParse(hanObj.ToString(), out int storedHost)) return storedHost;
        return PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : -1;
    }

    public static bool IsLocalRoomHost()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return false;
        return PhotonNetwork.LocalPlayer.ActorNumber == GetRoomHostActorNumber();
    }

    static void EnsureHostActorRoomProperty()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("HAN")) return;
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "HAN", PhotonNetwork.LocalPlayer.ActorNumber } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public void ConfirmHostSeatStart() => _hostConfirmedSeatStart = true;
    public bool ConsumeHostSeatStartConfirmation() { if (!_hostConfirmedSeatStart) return false; _hostConfirmedSeatStart = false; return true; }

    public void ResetMenuFlowFlags() { _joinInProgress = false; _isLeavingRoom = false; _pendingSeatLobbyOpen = false; _creatingPrivateRoom = false; _createRoomRetries = 0; }

    public void ResetLobbyStateForLeave()
    {
        ResetMenuFlowFlags(); _hostConfirmedSeatStart = false; _friendsGameStartTriggered = false; _pendingSeatLobbyOpen = false;
        if (!_pendingCreatePrivateRoom) SuppressSeatLobbyOnJoin = false;
        _onlineMode = false;
        if (_lobbyPlayerRefreshCoroutine != null) StopFriendsCoroutineSlot(ref _lobbyPlayerRefreshCoroutine, ref _lobbyPlayerRefreshRunner);
        ResetSeatPanelUI(); HidePlayWithFriendsLobbyPanel(); HideClientWaitingPresentation();
        if (ModeManager.Instance != null) ModeManager.Instance.HidePlayWithFriendsPanel();
        if (NetworkManager.Instance != null) NetworkManager.Instance.ClearUiInputBlockers();
    }

    void SyncRoomLobbyUIForRole()
    {
        if (!PhotonNetwork.InRoom) return;
        bool isHost = IsLocalRoomHost();
        if (modesPanel != null) { modesPanel.SetActive(false); CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>(); if (cg != null) { cg.interactable = false; cg.blocksRaycasts = false; } }
        if (startGameButton != null) startGameButton.SetActive(isHost);
        ApplyClientWaitingPresentation(!isHost, "Waiting for Host...");
        CheckPlayerCountAndToggleStart();
    }

    void ApplyClientWaitingPresentation(bool show, string message = "Waiting for Host...")
    {
        if (clientWaitingText != null)
        {
            if (show) { clientWaitingText.fontSize = clientWaitingFontSize; clientWaitingText.fontStyle = FontStyles.Bold; clientWaitingText.text = message; clientWaitingText.gameObject.SetActive(true); }
            else clientWaitingText.gameObject.SetActive(false);
        }
        EnsureClientWaitingSpinner();
        if (clientWaitingSpinner == null) return;
        _waitingSpinnerTween?.Kill();
        clientWaitingSpinner.gameObject.SetActive(show);
        if (!show) return;
        clientWaitingSpinner.localRotation = Quaternion.identity;
        _waitingSpinnerTween = clientWaitingSpinner.DORotate(new Vector3(0f, 0f, -360f), 1.1f, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetUpdate(true);
    }

    void EnsureClientWaitingSpinner()
    {
        if (clientWaitingSpinner != null || clientWaitingText == null) return;
        Transform parent = clientWaitingText.transform.parent;
        if (parent == null) return;
        Transform existing = parent.Find("WaitingSpinner");
        if (existing != null) { clientWaitingSpinner = existing as RectTransform; return; }

        var go = new GameObject("WaitingSpinner", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        clientWaitingSpinner = go.GetComponent<RectTransform>();
        var textRt = clientWaitingText.rectTransform;
        clientWaitingSpinner.anchorMin = clientWaitingSpinner.anchorMax = textRt.anchorMin;
        clientWaitingSpinner.pivot = new Vector2(1f, 0.5f);
        clientWaitingSpinner.sizeDelta = new Vector2(44f, 44f);
        clientWaitingSpinner.anchoredPosition = textRt.anchoredPosition + new Vector2(-18f, 0f);

        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 0.92f, 0.55f, 0.95f);
        img.raycastTarget = false;

        var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
        ring.transform.SetParent(go.transform, false);
        var ringRt = ring.GetComponent<RectTransform>();
        ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
        ringRt.offsetMin = new Vector2(6f, 6f); ringRt.offsetMax = new Vector2(-6f, -6f);
        var ringImg = ring.GetComponent<Image>();
        ringImg.color = new Color(0.35f, 0.22f, 0.12f, 0.35f);
        ringImg.raycastTarget = false;
    }

    void HideClientWaitingPresentation() { ApplyClientWaitingPresentation(false); }

    public void ShowPrivateRoomLobbyUI()
    {
        if (_isLeavingFriendsFlow) return;
        if (PhotonNetwork.CurrentRoom == null) return;

        GameFlowState.SetPhase(GameFlowPhase.InRoom, forceRecovery: true);
        if (ModeManager.Instance != null) { ModeManager.Instance.HideJoinTablePanel(); if (ModeManager.Instance.panelModes != null && !PhotonNetwork.IsMasterClient) ModeManager.Instance.panelModes.SetActive(false); ModeManager.Instance.ShowPlayWithFriendsPanel(); }
        if (!gameObject.activeInHierarchy) { gameObject.SetActive(true); transform.SetAsLastSibling(); }
        if (NetworkManager.Instance != null) { NetworkManager.Instance.ResetRoomLobbyCanvasGroup(); NetworkManager.Instance.ForceClearBlackOverlay(); }
        if (modesPanel != null && PhotonNetwork.IsMasterClient) modesPanel.SetActive(false);

        _onlineMode = false; ApplyModeControls(false); SetSeatPanelTitle("SELECT CHAIRS");
        if (pinCreationPanel != null) { pinCreationPanel.SetActive(true); pinCreationPanel.transform.SetAsLastSibling(); }
        StartRoomIdPlaqueWatch();
        if (errorText != null) errorText.gameObject.SetActive(false);
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj) && botsObj is bool botsObjVal) ApplyBotsIncludedState(botsObjVal); else ApplyBotsIncludedState(false);
        UpdatePlayerListUI(); EnsureLobbyInviteButton(true); SyncRoomLobbyUIForRole(); BeginLobbyPlayerListRefresh();
        EnsureSeatClickHandlers();
        EnsureLobbyChrome();
        if (NetworkManager.Instance != null) NetworkManager.Instance.HideLoadingInstant();
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
        if (includeBotsBtnText != null) includeBotsBtnText.text = areBotsIncluded ? "Remove Bots" : "Include Bots";
        EnsureIncludeBotsBackground();
    }

    void EnsureLobbyChrome()
    {
        EnsureIncludeBotsBackground();
        EnsureStartButtonBackground();
        EnsurePinInputContrast();
        SetVsDividerVisible(!_onlineMode);
    }

    void EnsureIncludeBotsBackground()
    {
        if (includeBotsButton == null) return;

        Image bg = includeBotsButton.GetComponent<Image>();
        if (bg != null)
            bg.color = Color.white;

        if (includeBotsBtnText != null)
        {
            includeBotsBtnText.color = Color.white;
            includeBotsBtnText.fontStyle = FontStyles.Bold;
        }
    }

    void EnsureStartButtonBackground()
    {
        if (startGameButton == null)
            UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);
        if (startGameButton == null) return;

        Image img = startGameButton.GetComponent<Image>();
        if (img != null)
        {
            if (img.sprite == null)
            {
                Transform backBtn = transform.Find("BACK") ?? FindDeepChild(transform, "Btn_Back");
                if (backBtn != null)
                {
                    Image backImg = backBtn.GetComponent<Image>();
                    if (backImg != null && backImg.sprite != null)
                    {
                        img.sprite = backImg.sprite;
                        img.type = backImg.type;
                    }
                }
            }
            img.color = Color.white;
        }

        TMP_Text label = startGameButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.color = Color.white;
            label.fontStyle = FontStyles.Bold;
        }
    }

    void EnsurePinInputContrast()
    {
        if (pinInputField == null) return;

        Color pinText = new Color(0.12f, 0.06f, 0.02f, 1f);
        if (pinInputField.textComponent != null)
        {
            pinInputField.textComponent.color = pinText;
            pinInputField.textComponent.fontStyle = FontStyles.Bold;
        }

        if (pinInputField.placeholder is TMP_Text placeholder)
            placeholder.color = new Color(0.35f, 0.22f, 0.12f, 0.9f);

        pinInputField.contentType = TMP_InputField.ContentType.IntegerNumber;
        pinInputField.keyboardType = TouchScreenKeyboardType.NumberPad;
        pinInputField.characterLimit = 5;
        pinInputField.shouldHideMobileInput = true;
        pinInputField.caretColor = pinText;
        pinInputField.customCaretColor = true;
    }

    void UpdatePlayerListUI()
    {
        if (playerSlotsText == null || playerSlotsText.Length == 0) return;
        if (_onlineMode && !PhotonNetwork.InRoom) { ShowLocalPlayerInOnlineMatchmaking(); return; }
        if (!PhotonNetwork.InRoom) return;

        bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

        if (isPrivateFriendsRoom && TryGetSeatMap(out int[] seatMap))
        {
            int displaySlotsFilled = CountPlayingSeats(seatMap);
            if (areBotsIncluded) displaySlotsFilled = DeckManager.MaxTableSeats;

            RefreshLobbyPlayerCountLabel(displaySlotsFilled, displaySlotsFilled);

            for (int i = 0; i < playerSlotsText.Length; i++)
            {
                if (playerSlotsText[i] == null) continue;
                if (i >= seatMap.Length) break;

                int actorId = seatMap[i];
                if (actorId != -1)
                {
                    Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorId);
                    if (p != null)
                    {
                        bool isRoomHost = p.ActorNumber == GetRoomHostActorNumber();
                        string hostTag = isRoomHost ? " (Host)" : "";
                        playerSlotsText[i].text = GetPlayerDisplayName(p) + hostTag;
                        playerSlotsText[i].color = Color.black;
                        SetSeatAvatar(i, GetAvatarIndexForPlayer(p), true);
                    }
                    else
                    {
                        playerSlotsText[i].text = i == SpectateSeatIndex ? "Spectate" : "Waiting for Friend...";
                        playerSlotsText[i].color = new Color(0f, 0f, 0f, 0.55f);
                        SetSeatAvatar(i, -1, false);
                    }
                }
                else if (areBotsIncluded && i < PlayingSeatCount)
                {
                    playerSlotsText[i].text = "AI Bot";
                    playerSlotsText[i].color = new Color(0.4f, 1f, 0.4f, 1f);
                    SetSeatAvatar(i, -1, true);
                }
                else
                {
                    playerSlotsText[i].text = i == SpectateSeatIndex ? "Spectate" : "Waiting for Friend...";
                    playerSlotsText[i].color = new Color(0f, 0f, 0f, 0.55f);
                    SetSeatAvatar(i, -1, false);
                }
            }
            return;
        }

        Player[] currentPlayers = PhotonRoomPlayers.GetSorted();
        int realPlayerCount = currentPlayers.Length;
        int displaySlotsFilledOnline = realPlayerCount;
        if (areBotsIncluded || (_onlineMode && _previewBotsInOnlineLobby)) displaySlotsFilledOnline = DeckManager.MaxTableSeats;

        RefreshLobbyPlayerCountLabel(realPlayerCount, displaySlotsFilledOnline);

        for (int i = 0; i < playerSlotsText.Length; i++)
        {
            if (playerSlotsText[i] == null) continue;
            if (i < realPlayerCount)
            {
                Player p = currentPlayers[i];
                int hostActor = GetRoomHostActorNumber();
                bool isRoomHost = hostActor > 0 && p.ActorNumber == hostActor;
                string hostTag = isRoomHost ? " (Host)" : "";
                playerSlotsText[i].text = GetPlayerDisplayName(p) + hostTag;
                playerSlotsText[i].color = Color.black;
                SetSeatAvatar(i, GetAvatarIndexForPlayer(p), true);
            }
            else if (areBotsIncluded || (_onlineMode && _previewBotsInOnlineLobby))
            {
                playerSlotsText[i].text = realPlayerCount == 3 && i == realPlayerCount ? "DehlaBot" : "AI Bot " + (i - realPlayerCount + 1);
                playerSlotsText[i].color = new Color(0.4f, 1f, 0.4f, 1f);
                SetSeatAvatar(i, -1, true);
            }
            else
            {
                playerSlotsText[i].text = _onlineMode
                    ? "Waiting..."
                    : (i == SpectateSeatIndex ? "Spectate" : "Waiting for Friend...");
                playerSlotsText[i].color = new Color(0f, 0f, 0f, 0.55f);
                SetSeatAvatar(i, -1, false);
            }
        }
    }

    void RefreshLobbyPlayerCountLabel(int realPlayers, int displayFilled)
    {
        if (_onlineMode) return;
        if (matchmakingTimerText == null) return;
        bool inPrivateLobby = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;
        if (!inPrivateLobby) { matchmakingTimerText.gameObject.SetActive(false); return; }
        matchmakingTimerText.gameObject.SetActive(true);
        matchmakingTimerText.text = areBotsIncluded ? $"Players: {displayFilled}/{DeckManager.MaxTableSeats}" : $"Players: {realPlayers}/{DeckManager.MaxTableSeats}";
    }

    Sprite[] _avatarPoolCache;
    Sprite[] GetAvatarPool()
    {
        if (PlayerProfileManager.Instance != null && PlayerProfileManager.Instance.profileSprites != null && PlayerProfileManager.Instance.profileSprites.Length > 0)
        {
            _avatarPoolCache = PlayerProfileManager.Instance.profileSprites;
            return _avatarPoolCache;
        }
        if (_avatarPoolCache != null && _avatarPoolCache.Length > 0) return _avatarPoolCache;
        if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
            _avatarPoolCache = MatchmakingManager.GlobalProfileSprites.ToArray();
        return _avatarPoolCache;
    }

    int GetAvatarIndexForPlayer(Player p)
    {
        if (p == null) return -1;
        if (PhotonNetwork.LocalPlayer != null && p.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            int local = PlayerProfileManager.GetSavedAvatarIndex();
            if (local >= 0) return local;
        }
        if (p.CustomProperties != null && p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val) && val != null)
        {
            if (val is int vi) return vi;
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
        }
        return -1;
    }

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
        img.color = occupied ? Color.white : new Color(1f, 1f, 1f, 0.25f);
    }

    void ShowLocalPlayerInOnlineMatchmaking()
    {
        EnsureNickname();
        for (int i = 0; i < playerSlotsText.Length; i++)
        {
            if (playerSlotsText[i] == null) continue;
            if (i == 0)
            {
                playerSlotsText[i].text = MyDisplayName;
                playerSlotsText[i].color = Color.black;
                int avatarIdx = PhotonNetwork.LocalPlayer != null ? GetAvatarIndexForPlayer(PhotonNetwork.LocalPlayer) : PlayerProfileManager.GetSavedAvatarIndex();
                SetSeatAvatar(0, avatarIdx, true);
            }
            else
            {
                playerSlotsText[i].text = "Waiting...";
                playerSlotsText[i].color = new Color(0f, 0f, 0f, 0.55f);
                SetSeatAvatar(i, -1, false);
            }
        }
    }

    void ClearPlayerListUI()
    {
        if (playerSlotsText == null) return;
        for (int i = 0; i < playerSlotsText.Length; i++)
        {
            if (playerSlotsText[i] == null) continue;
            playerSlotsText[i].text = i == SpectateSeatIndex ? "Spectate" : "Waiting for Friend...";
            playerSlotsText[i].color = new Color(0f, 0f, 0f, 0.55f);
        }
    }

    void CheckPlayerCountAndToggleStart()
    {
        if (_onlineMode) return;
        if (startGameButton == null) UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (includeBotsButton != null) includeBotsButton.SetActive(PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount < DeckManager.MaxTableSeats);
        if (!PhotonNetwork.IsMasterClient) { if (startGameButton != null) startGameButton.SetActive(false); return; }
        if (startGameButton == null) return;
        startGameButton.SetActive(true);

        bool canStart;
        if (TryGetSeatMap(out int[] seatMap))
            canStart = CountPlayingSeats(seatMap) == PlayingSeatCount || areBotsIncluded;
        else
            canStart = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;

        EnsureStartButtonBackground();
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

    Coroutine _roomIdRefreshCoroutine;
    MonoBehaviour _roomIdRefreshRunner;

    void RefreshRoomIdPlaque()
    {
        if (generatedPinText == null)
        {
            if (UiSafeLookup.TryGet("Txt_GeneratedPIN", out GameObject pinGo) && pinGo != null)
                generatedPinText = pinGo.GetComponent<TMP_Text>();
        }
        if (generatedPinText == null) return;
        if (roomIdPlaque != null && !roomIdPlaque.activeSelf) roomIdPlaque.SetActive(true);
        if (!generatedPinText.gameObject.activeSelf) generatedPinText.gameObject.SetActive(true);
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible) generatedPinText.text = "ROOM ID :- " + PhotonNetwork.CurrentRoom.Name;
        else generatedPinText.text = "ROOM ID :- ...";
    }

    void StartRoomIdPlaqueWatch() { RefreshRoomIdPlaque(); StartFriendsCoroutine(RoomIdPlaqueWatchRoutine(), ref _roomIdRefreshCoroutine, ref _roomIdRefreshRunner); }

    IEnumerator RoomIdPlaqueWatchRoutine()
    {
        float timeout = 15f;
        while (timeout > 0f)
        {
            RefreshRoomIdPlaque();
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible) break;
            yield return new WaitForSeconds(0.3f); timeout -= 0.3f;
        }
        _roomIdRefreshCoroutine = null; _roomIdRefreshRunner = null;
    }

    void EnsureLobbyInviteButton(bool visible)
    {
        if (_lobbyInviteButton == null) BuildLobbyInviteButton();
        if (_lobbyInviteButton == null) return;
        _lobbyInviteButton.SetActive(visible);
        if (visible) _lobbyInviteButton.transform.SetAsLastSibling();
    }

    void BuildLobbyInviteButton()
    {
        UnityEngine.UI.Button homeBtn = ResolveHomeInviteButton();
        if (homeBtn != null)
        {
            GameObject go = Instantiate(homeBtn.gameObject, transform);
            go.name = "FRIEND_INVITE_BUTTON";
            RectTransform src = homeBtn.GetComponent<RectTransform>();
            RectTransform rt = go.GetComponent<RectTransform>();
            if (src != null && rt != null) { rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot; rt.sizeDelta = src.sizeDelta; rt.anchoredPosition = src.anchoredPosition; rt.localScale = src.localScale; }
            UnityEngine.UI.Button btn = go.GetComponent<UnityEngine.UI.Button>();
            if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(OpenLobbyFriendInvite); }
            go.SetActive(false); _lobbyInviteButton = go; return;
        }

        GameObject fb = new GameObject("FRIEND_INVITE_BUTTON", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button));
        fb.transform.SetParent(transform, false);
        RectTransform frt = fb.GetComponent<RectTransform>();
        frt.anchorMin = frt.anchorMax = new Vector2(1f, 0.5f);
        frt.pivot = new Vector2(1f, 0.5f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(100f, 250f);
        UnityEngine.UI.Image img = fb.GetComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.18f, 0.55f, 0.30f, 1f);
        Button fbBtn = fb.GetComponent<Button>();
        fbBtn.targetGraphic = img;
        fbBtn.onClick.AddListener(OpenLobbyFriendInvite);
        GameObject labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(fb.transform, false);
        RectTransform lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "FRIENDS"; label.fontSize = 28f; label.fontStyle = FontStyles.Bold; label.alignment = TextAlignmentOptions.Center; label.color = Color.white; label.raycastTarget = false;
        fb.SetActive(false); _lobbyInviteButton = fb;
    }

    UnityEngine.UI.Button ResolveHomeInviteButton()
    {
        if (FriendsDrawerController.Instance != null && FriendsDrawerController.Instance.inviteFriendsButton != null) return FriendsDrawerController.Instance.inviteFriendsButton;
        FriendsDrawerController drawer = FindFirstObjectByType<FriendsDrawerController>(FindObjectsInactive.Include);
        if (drawer != null && drawer.inviteFriendsButton != null) return drawer.inviteFriendsButton;
        return null;
    }

    public void OpenLobbyFriendInvite()
    {
        FriendsDrawerController drawer = FriendsDrawerController.Instance;
        if (drawer == null) drawer = FindFirstObjectByType<FriendsDrawerController>(FindObjectsInactive.Include);
        if (drawer == null) { ShowUIError("Friends list unavailable."); return; }

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform overlayRoot = canvas != null ? canvas.transform : transform.root;
        drawer.OpenDrawerDuringGame(overlayRoot);

        if (FriendsPanelUIController.Instance != null) FriendsPanelUIController.Instance.RefreshAll();
        RefreshFriendsStatus();
    }

    public void OpenSeatLobbyWhenReady()
    {
        if (_isLeavingRoom) return;
        BeginFriendsFlow(); SuppressSeatLobbyOnJoin = false; _pendingSeatLobbyOpen = true;
        if (PhotonNetwork.InRoom) { PresentSeatLobbyUI(); return; }
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.BeginProtectedLoading(
                NetworkManager.ProtectedLoadingFlow.FriendsCreatingRoom,
                "Creating room...");
            NetworkManager.Instance.AnimateLoadingSlider(NetworkManager.GameStartLoadingDelaySeconds);
        }
        CreatePrivateRoom();
    }

    void PresentSeatLobbyUI()
    {
        _pendingSeatLobbyOpen = false;
        // Show lobby first so Modes never flashes between loading hide and lobby open.
        OnSeatPanelOpened();
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.EndProtectedLoading(NetworkManager.ProtectedLoadingFlow.FriendsCreatingRoom);
            NetworkManager.Instance.EndProtectedLoading(NetworkManager.ProtectedLoadingFlow.FriendsLobby);
            NetworkManager.Instance.HideLoadingInstant();
            NetworkManager.Instance.ResetRoomLobbyCanvasGroup();
        }
        if (ModeManager.Instance != null)
        {
            ModeManager.Instance.EndFriendsRoomCreationFlow();
            ModeManager.Instance.ResetStartGuard();
        }
    }

    public void OnSeatPanelOpened()
    {
        BeginFriendsFlow(); SuppressSeatLobbyOnJoin = false;
        if (ModeManager.Instance != null) ModeManager.Instance.ShowPlayWithFriendsPanel();
        else if (!gameObject.activeInHierarchy) { gameObject.SetActive(true); transform.SetAsLastSibling(); }
        if (errorText != null) errorText.gameObject.SetActive(false);
        ClearPlayerListUI();
        if (startGameButton == null) UiSafeLookup.TryGet("Btn_StartPrivateGame", out startGameButton);
        if (startGameButton != null) { startGameButton.SetActive(true); SetStartButtonInteractable(false); }
        EnsureLobbyChrome();

        _onlineMode = false; ApplyModeControls(false); SetSeatPanelTitle("SELECT CHAIRS");
        if (pinCreationPanel != null) { pinCreationPanel.SetActive(true); pinCreationPanel.transform.SetAsLastSibling(); }
        if (!PhotonNetwork.InRoom) return;
        UpdatePlayerListUI(); StartRoomIdPlaqueWatch(); CheckPlayerCountAndToggleStart(); EnsureLobbyInviteButton(true);
        EnsureSeatClickHandlers();
    }

    public void ShowOnlineMatchmakingLobby()
    {
        _onlineMode = true; _previewBotsInOnlineLobby = false;
        if (ModeManager.Instance != null)
        {
            ModeManager.Instance.SetFriendsMatchMode(false);
            if (ModeManager.Instance.panelHomeScreen != null) ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelHomeScreen, false);
            if (ModeManager.Instance.panelModes != null) ModeManager.SetPanelVisiblePublic(ModeManager.Instance.panelModes, false);
            ModeManager.Instance.HideJoinTablePanel();
        }
        UiFlowManager.BeginOnlineMatchmaking();
        ModeManager.EnsurePanelHierarchyActivePublic(gameObject);
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        transform.SetAsLastSibling();
        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg != null) { cg.DOKill(); cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }
        RectTransform rt = transform as RectTransform;
        if (rt != null) { rt.localScale = Vector3.one; if (Mathf.Abs(rt.anchoredPosition.y) > 5000f) rt.anchoredPosition = Vector2.zero; }
        if (errorText != null) errorText.gameObject.SetActive(false);
        if (modesPanel != null) modesPanel.SetActive(false);
        if (startGameButton != null) startGameButton.SetActive(false);
        ApplyModeControls(true); SetSeatPanelTitle("FINDING PLAYERS");
        if (matchmakingTimerText != null) { matchmakingTimerText.gameObject.SetActive(true); matchmakingTimerText.text = "Finding players..."; }
        ClearPlayerListUI(); ShowLocalPlayerInOnlineMatchmaking(); EnsureLobbyInviteButton(false);
        if (PhotonNetwork.InRoom) UpdatePlayerListUI();
    }

    bool _previewBotsInOnlineLobby;
    public void UpdateOnlineTimer(int playersFound, int countdown)
    {
        if (!_onlineMode) return;
        _previewBotsInOnlineLobby = countdown <= 2 && playersFound < DeckManager.MaxTableSeats;
        int displayCount = playersFound;
        if (_previewBotsInOnlineLobby) displayCount = DeckManager.MaxTableSeats;
        if (matchmakingTimerText != null)
            matchmakingTimerText.text = playersFound >= DeckManager.MaxTableSeats ? "Starting game..." : $"Players: {displayCount}/{DeckManager.MaxTableSeats}    Starting in {Mathf.Max(0, countdown)}s";
        if (PhotonNetwork.InRoom) UpdatePlayerListUI();
    }

    public void HideLobby()
    {
        _onlineMode = false; ApplyModeControls(false); HidePrivateFriendsLobbyUI();
        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg != null) { cg.DOKill(); cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false; }
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    void ApplyModeControls(bool online)
    {
        GameObject createBtn = createRoomButton;
        if (createBtn == null) { Transform t = transform.Find("ContentArea/Host Section/Btn_CreateRoom"); if (t != null) createBtn = t.gameObject; }
        if (createBtn != null) createBtn.SetActive(false);
        Transform join = transform.Find("ContentArea/Join Section");
        if (join != null) join.gameObject.SetActive(false);
        GameObject plaque = roomIdPlaque;
        if (plaque == null) { Transform t = transform.Find("RoomIdPlaque"); if (t != null) plaque = t.gameObject; }
        if (plaque != null) plaque.SetActive(!online);
        if (online && includeBotsButton != null) includeBotsButton.SetActive(false);
        if (matchmakingTimerPlaque != null) matchmakingTimerPlaque.SetActive(online);
        if (matchmakingTimerText != null) matchmakingTimerText.gameObject.SetActive(online);

        // Spectate seat UI is friends-only — hide for public online matchmaking.
        SetSpectatePanelVisible(!online);
        // VS divider is friends 2v2 only — hide for online (1v1v1v1 chairs).
        SetVsDividerVisible(!online);
    }

    void SetSpectatePanelVisible(bool visible)
    {
        Transform spectate = FindDeepChild(transform, "SpectatePanel");
        if (spectate != null && spectate.gameObject.activeSelf != visible)
            spectate.gameObject.SetActive(visible);
    }

    void SetVsDividerVisible(bool visible)
    {
        Transform vs = FindDeepChild(transform, "VS");
        if (vs != null && vs.gameObject.activeSelf != visible)
            vs.gameObject.SetActive(visible);
    }

    static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    void SetSeatPanelTitle(string text)
    {
        Transform t = transform.Find("TitlePlaque/Title");
        if (t == null) t = transform.Find("Title");
        if (t != null) { TMP_Text label = t.GetComponent<TMP_Text>(); if (label != null) label.text = text; }
    }

    public void OnSeatPanelBackClicked()
    {
        bool onlineLobby = _onlineMode || (MatchmakingManager.Instance != null && MatchmakingManager.Instance.IsSearching) || GameFlowState.Current == GameFlowPhase.Matchmaking;
        if (onlineLobby) { if (MatchmakingManager.Instance != null) MatchmakingManager.Instance.OnCancelClicked(); else HideLobby(); return; }
        LeaveCurrentRoom();
    }

    void StopFriendsGameStartCoroutine()
    {
        if (NetworkManager.Instance != null && _smoothGameStartCoroutine != null)
        {
            NetworkManager.Instance.StopCoroutine(_smoothGameStartCoroutine);
            _smoothGameStartCoroutine = null;
        }
        _friendsGameStartTriggered = false;
    }

    public void LeaveCurrentRoom()
    {
        if (_isLeavingRoom) return;
        if (ShouldDisbandPrivateLobbyAsHost()) { DisbandPrivateRoomAsHost(); return; }
        PerformLeaveCurrentRoom();
    }

    bool IsPrivateFriendsLobby()
    {
        return PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode && !_onlineMode;
    }

    bool IsFriendsMatchStarted()
    {
        if (_friendsGameStartTriggered) return true;
        if (PhotonNetwork.CurrentRoom != null)
        {
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("ModesLocked", out object ml) && ml is bool locked && locked) return true;
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs) && gs is bool inGame && inGame) return true;
        }
        return GameFlowState.Current == GameFlowPhase.InGame || GameFlowState.Current == GameFlowPhase.Dealing;
    }

    bool ShouldDisbandPrivateLobbyAsHost() => PhotonNetwork.IsMasterClient && IsPrivateFriendsLobby() && !IsFriendsMatchStarted();

    void DisbandPrivateRoomAsHost()
    {
        SendFriendsRpc("RPC_Friends_RoomDisbandedByHost", RpcTarget.Others);
        if (PhotonNetwork.CurrentRoom != null) { PhotonNetwork.CurrentRoom.IsOpen = false; PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "Disbanded", true } }); }
        PerformLeaveCurrentRoom();
    }

    [PunRPC]
    void RPC_Friends_RoomDisbandedByHost() => HandleRoomDisbandedByHost();

    void HandleRoomDisbandedByHost()
    {
        if (_isLeavingRoom) return;
        UiFlowManager.MarkReturningHome(); StopFriendsGameStartCoroutine(); AbortPendingFriendsRoomCreation(); PendingJoinPin = null; _pendingSeatLobbyOpen = false; _isLeavingRoom = true;
        if (FriendsDrawerController.Instance != null) FriendsDrawerController.Instance.CloseDrawer();
        if (NetworkManager.Instance != null) NetworkManager.Instance.LeaveRoomAndCleanup();
        else if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        else if (ModeManager.Instance != null) ModeManager.Instance.ReturnToHomeClean();
    }

    void PerformLeaveCurrentRoom()
    {
        AbortPendingFriendsRoomCreation(); StopFriendsGameStartCoroutine(); PendingJoinPin = null; _pendingSeatLobbyOpen = false; _isLeavingRoom = true;
        if (FriendsDrawerController.Instance != null) FriendsDrawerController.Instance.CloseDrawer();
        if (NetworkManager.Instance != null) { NetworkManager.Instance.LeaveRoomAndCleanup(); return; }
        _isLeavingRoom = false; ResetLobbyStateForLeave();
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom(); else if (ModeManager.Instance != null) ModeManager.Instance.ReturnToHomeClean();
    }

    public override void OnLeftRoom()
    {
        if (!string.IsNullOrEmpty(PendingJoinPin)) return;
        _isLeavingFriendsFlow = false; _isLeavingRoom = false; _pendingSeatLobbyOpen = false; ResetSeatPanelUI();
    }

    public void ResetSeatPanelUI()
    {
        _onlineMode = false; _friendsGameStartTriggered = false;
        StopFriendsCoroutineSlot(ref _roomIdRefreshCoroutine, ref _roomIdRefreshRunner);
        if (generatedPinText == null && UiSafeLookup.TryGet("Txt_GeneratedPIN", out GameObject pinGo) && pinGo != null) generatedPinText = pinGo.GetComponent<TMP_Text>();
        if (generatedPinText != null) generatedPinText.text = "ROOM ID :- ...";
        ApplyBotsIncludedState(false); if (includeBotsButton != null) includeBotsButton.SetActive(false);
        if (startGameButton != null) SetStartButtonInteractable(false);
        ClearPlayerListUI(); ClearSeatAvatars(); EnsureLobbyInviteButton(false);
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    void ClearSeatAvatars()
    {
        if (playerSlotsAvatar == null) return;
        for (int i = 0; i < playerSlotsAvatar.Length; i++) SetSeatAvatar(i, -1, false);
    }

    public void LeavePrivateRoomIfAny()
    {
        if (NetworkManager.Instance != null) { NetworkManager.Instance.LeaveRoomAndCleanup(); return; }
        SuppressSeatLobbyOnJoin = false;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode) PhotonNetwork.LeaveRoom();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;
        if (isPrivateFriendsRoom)
        {
            _onlineMode = false; _previewBotsInOnlineLobby = false;
            EnsureSeatMapExists();
            TryAutoSitLocalPlayer();
            if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats && areBotsIncluded)
            {
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "BotsIncluded", false } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }
            if (SuppressSeatLobbyOnJoin && PhotonNetwork.IsMasterClient) { UpdatePlayerListUI(); return; }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            UpdatePlayerListUI(); CheckPlayerCountAndToggleStart(); BeginLobbyPlayerListRefresh(); return;
        }
        if (_onlineMode) UpdatePlayerListUI();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        bool isPrivateFriendsRoom = !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;
        if (isPrivateFriendsRoom)
        {
            if (PhotonNetwork.IsMasterClient && otherPlayer != null && TryGetSeatMap(out int[] seatMap))
            {
                bool changed = false;
                for (int i = 0; i < seatMap.Length; i++)
                {
                    if (seatMap[i] != otherPlayer.ActorNumber) continue;
                    seatMap[i] = -1;
                    changed = true;
                }
                if (changed)
                {
                    PhotonNetwork.CurrentRoom.SetCustomProperties(
                        new ExitGames.Client.Photon.Hashtable { { SeatMapPropKey, seatMap } });
                }
            }

            _onlineMode = false;
            UpdatePlayerListUI();
            CheckPlayerCountAndToggleStart();
            return;
        }
        if (_onlineMode) UpdatePlayerListUI();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;
        if (IsPrivateFriendsLobby() && !IsFriendsMatchStarted()) { HandleRoomDisbandedByHost(); return; }
        UpdatePlayerListUI(); SyncRoomLobbyUIForRole(); CheckPlayerCountAndToggleStart();
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        if (changedProps != null && changedProps.ContainsKey(PlayerProfileManager.PROP_AVATAR) && gameObject.activeInHierarchy && PhotonNetwork.InRoom) UpdatePlayerListUI();
    }

    public void ShareRoomPIN()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible) return;
        string pin = PhotonNetwork.CurrentRoom.Name;
        string shareMessage = $"Aaja Dehla Pakad khelte hain! Mera Private Room PIN hai: {pin}. Jaldi join kar!";
        GUIUtility.systemCopyBuffer = shareMessage;
        if (errorText != null) { errorText.text = "PIN Copied!"; errorText.gameObject.SetActive(true); }
    }

    public void OpenModesPanelForHost()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        ResolveModesPanel();
        if (modesPanel != null) { modesPanel.SetActive(true); CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>(); if (cg != null) { cg.interactable = true; cg.blocksRaycasts = true; } }
        SendFriendsRpc("RPC_ShowModesPanelToClients", RpcTarget.Others);
        if (PhotonNetwork.CurrentRoom != null) PhotonNetwork.CurrentRoom.IsOpen = false;
        gameObject.SetActive(false);
    }

    [PunRPC]
    void RPC_ShowModesPanelToClients() => ExecuteShowModesPanelToClients();

    public void ExecuteShowModesPanelToClients()
    {
        ResolveModesPanel();
        if (modesPanel != null) { modesPanel.SetActive(true); CanvasGroup cg = modesPanel.GetComponent<CanvasGroup>(); if (cg != null) { cg.interactable = false; cg.blocksRaycasts = false; } }
        ApplyClientWaitingPresentation(true, "Host is selecting game modes...");
        if (ModeManager.Instance != null) ModeManager.Instance.ApplyLiveModesFromRoomIfPresent();
        gameObject.SetActive(false);
    }

    public void HostSelectedGameMode(int modeIndex) { if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "GameMode", modeIndex } }); }
    public void HostSelectedTaashMode(int taashIndex) { if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "TaashMode", taashIndex } }); }
    public void HostSelectedTrumpMode(int trumpIndex) { if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "TrumpMode", trumpIndex } }); }
    public void HostSelectedLogicMode(int logicIndex) { if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "LogicMode", logicIndex } }); }

    public void OpenModesPanel() => OnHostStartFriendsGame();
    public void StartPrivateGame() => OnHostStartFriendsGame();

    public void OnHostStartFriendsGame()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        bool full;
        if (TryGetSeatMap(out int[] seatMap))
            full = CountPlayingSeats(seatMap) == PlayingSeatCount || areBotsIncluded;
        else
            full = PhotonNetwork.CurrentRoom.PlayerCount == DeckManager.MaxTableSeats || areBotsIncluded;

        if (!full) { ShowUIError("Need 4 players to start!"); return; }
        ConfirmHostSeatStart();
        FinalStartWithSelectedModes();
    }

    void ResolveModesPanel() { if (modesPanel == null && ModeManager.Instance != null) modesPanel = ModeManager.Instance.panelModes; }
    void ResolveHomeMenuPanel() { if (homeMenuPanel != null) return; if (NetworkManager.Instance != null) homeMenuPanel = NetworkManager.Instance.homeMenuPanel; else if (ModeManager.Instance != null) homeMenuPanel = ModeManager.Instance.panelHomeScreen; }
    void ResolveGameTablePanel() { if (gameTablePanel != null) return; if (NetworkManager.Instance != null) gameTablePanel = NetworkManager.Instance.gameTablePanel; if (gameTablePanel != null) return; if (UiSafeLookup.TryGet("Panel_Game", out GameObject panelGo)) gameTablePanel = panelGo; else if (UiSafeLookup.TryGet("[Panel_Game]", out GameObject bracketGo)) gameTablePanel = bracketGo; if (gameTablePanel != null && NetworkManager.Instance != null) NetworkManager.Instance.gameTablePanel = gameTablePanel; }

    public void OnModePanelStartClicked() { if (ModeManager.Instance != null) ModeManager.Instance.StartGameFromModePanel(); }
    public void OnStartButtonClick() => OnModePanelStartClicked();

    public void FinalStartWithSelectedModes()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        if (GameSettings.Instance == null) return;
        if (_friendsGameStartTriggered) return;

        if (startGameButton != null) SetStartButtonInteractable(false);

        ExitGames.Client.Photon.Hashtable customRoomProperties = new ExitGames.Client.Photon.Hashtable();
        if (ModeManager.Instance != null) { customRoomProperties["TM"] = ModeManager.Instance.currentTrickMode; customRoomProperties["RM"] = ModeManager.Instance.currentTrumpMode; customRoomProperties["SM"] = ModeManager.Instance.currentSarMode; customRoomProperties["LM"] = ModeManager.Instance.currentLogicMode; }
        else { customRoomProperties["TM"] = GameSettings.Instance.taashCategory; customRoomProperties["RM"] = 3; customRoomProperties["SM"] = GameSettings.Instance.currentSarMode == SarModeType.TwoSar ? 2 : 1; customRoomProperties["LM"] = 1; }

        customRoomProperties["ModesLocked"] = true; customRoomProperties["GS"] = true; customRoomProperties["BotsIncluded"] = areBotsIncluded; customRoomProperties["HAN"] = PhotonNetwork.MasterClient.ActorNumber;

        List<int> playingActors = new List<int>(PlayingSeatCount);
        int spectatorActor = -1;
        if (TryGetSeatMap(out int[] seatMap))
        {
            for (int i = 0; i < PlayingSeatCount && i < seatMap.Length; i++)
            {
                if (seatMap[i] != -1) playingActors.Add(seatMap[i]);
            }
            if (seatMap.Length > SpectateSeatIndex) spectatorActor = seatMap[SpectateSeatIndex];
        }
        else
        {
            Player[] sortedPlayers = PhotonRoomPlayers.GetSorted();
            for (int i = 0; i < sortedPlayers.Length && playingActors.Count < PlayingSeatCount; i++)
            {
                if (sortedPlayers[i] != null) playingActors.Add(sortedPlayers[i].ActorNumber);
            }
        }

        int botsNeeded = DeckManager.MaxTableSeats - playingActors.Count;
        DeckManager.botActorNumbers.Clear();
        for (int i = 0; i < botsNeeded; i++) DeckManager.botActorNumbers.Add(100 + i);

        int deckSeed = UnityEngine.Random.Range(1, int.MaxValue);
        customRoomProperties["DS"] = deckSeed; DeckManager.SetSharedDeckSeed(deckSeed); customRoomProperties["BS"] = DeckManager.botActorNumbers.ToArray();

        customRoomProperties["RPA"] = playingActors.ToArray();
        customRoomProperties["SpectatorActor"] = spectatorActor;

        var activeSeats = new List<int>(DeckManager.MaxTableSeats);
        activeSeats.AddRange(playingActors);
        for (int i = 0; i < DeckManager.botActorNumbers.Count && activeSeats.Count < DeckManager.MaxTableSeats; i++)
            activeSeats.Add(DeckManager.botActorNumbers[i]);
        customRoomProperties["SMP"] = activeSeats.ToArray();

        PhotonNetwork.CurrentRoom.SetCustomProperties(customRoomProperties);
        PhotonNetwork.CurrentRoom.IsOpen = false; PhotonNetwork.CurrentRoom.IsVisible = false;

        if (botsNeeded > 0 && DeckManager.Instance != null) DeckManager.Instance.photonView.RPC("RPC_SyncBotsOnly", RpcTarget.All, DeckManager.botActorNumbers.ToArray());
        SendFriendsRpc("RPC_StartGameForEveryone", RpcTarget.All);
        ExecuteFriendsGameStart();
    }

    [PunRPC]
    void RPC_StartGameForEveryone() => ExecuteFriendsGameStart();

    void ApplyFriendsStartFromRoomProperties()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.IsVisible || PhotonNetwork.OfflineMode) return;
        DeckManager.SyncBotSeatsFromRoomProperties();
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BS", out object bsObj) && bsObj is int[] bs && bs.Length > 0 && DeckManager.botActorNumbers.Count == 0)
        {
            for (int i = 0; i < bs.Length; i++) DeckManager.botActorNumbers.Add(bs[i]);
        }
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DS", out object dsObj) && dsObj != null && int.TryParse(dsObj.ToString(), out int ds) && ds != 0) DeckManager.SetSharedDeckSeed(ds);
    }

    public void ExecuteFriendsGameStart()
    {
        if (_friendsGameStartTriggered) return;
        _friendsGameStartTriggered = true;

        if (ModeManager.Instance != null) ModeManager.Instance.SyncModesFromRoom();
        ApplyFriendsStartFromRoomProperties();

        if (TrumpManager.Instance != null)
        {
            if (DeckManager.IsPrivateFriendsRoom()) TrumpManager.Instance.RefreshFromRoomProperties(false);
            else TrumpManager.ApplyTrumpForCurrentGameMode(false);
        }

        if (PhotonNetwork.IsMasterClient)
        {
            DeckManager.botActorNumbers.Clear();
            if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object botsObj) && botsObj is bool botsOn) areBotsIncluded = botsOn;
            if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BS", out object bsObj) && bsObj is int[] bsFromRoom)
            {
                for (int i = 0; i < bsFromRoom.Length; i++) DeckManager.botActorNumbers.Add(bsFromRoom[i]);
            }
            else
            {
                if (TryGetSeatMap(out int[] seatMap))
                {
                    int playingCount = CountPlayingSeats(seatMap);
                    int botsNeeded = DeckManager.MaxTableSeats - playingCount;
                    for (int i = 0; i < botsNeeded; i++) DeckManager.botActorNumbers.Add(100 + i);
                }
                else
                {
                    int realPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
                    int botsNeeded = DeckManager.MaxTableSeats - realPlayerCount;
                    for (int i = 0; i < botsNeeded; i++) DeckManager.botActorNumbers.Add(100 + i);
                }
            }
        }
        else
        {
            if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BotsIncluded", out object inc) && inc is bool included) areBotsIncluded = included;
            if (DeckManager.botActorNumbers.Count == 0) ApplyFriendsStartFromRoomProperties();
        }

        GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
        UiFlowManager.MarkInGame();

        if (NetworkManager.Instance != null)
        {
            if (_smoothGameStartCoroutine != null) { NetworkManager.Instance.StopCoroutine(_smoothGameStartCoroutine); _smoothGameStartCoroutine = null; }
            _smoothGameStartCoroutine = NetworkManager.Instance.StartCoroutine(SmoothGameStartRoutine());
        }
    }

    IEnumerator SmoothGameStartRoutine()
    {
        const float waitDuration = 1.5f;
        if (NetworkManager.Instance != null) { NetworkManager.Instance.ShowLoading("Starting Game..."); NetworkManager.Instance.AnimateLoadingSlider(waitDuration); }
        ResolveGameTablePanel(); ResolveModesPanel(); if (modesPanel != null) modesPanel.SetActive(false); HidePrivateFriendsLobbyUI();
        if (ModeManager.Instance != null) ModeManager.Instance.HidePlayWithFriendsPanel();

        yield return new WaitForSeconds(waitDuration);

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.CompleteLoadingSlider(); NetworkManager.Instance.ResetGameStartGuards(); NetworkManager.Instance.EnsureLocalNetworkPlayer(); PlayerHand.ResolveLocalHand(); NetworkManager.Instance.HideLoadingInstant(); NetworkManager.Instance.ForceClearBlackOverlay(); NetworkManager.Instance.BeginGameAfterRoomReady(showLoadingOverlay: false);
        }
        _smoothGameStartCoroutine = null;
    }

    void HidePlayWithFriendsLobbyPanel() { if (pinCreationPanel != null) pinCreationPanel.SetActive(false); if (startGameButton != null) startGameButton.SetActive(false); }
    public void HidePrivateFriendsLobbyUI() { HidePlayWithFriendsLobbyPanel(); if (errorText != null) errorText.gameObject.SetActive(false); }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.IsVisible) return;

        if (propertiesThatChanged.ContainsKey("ModesLocked") && propertiesThatChanged["ModesLocked"] is bool locked && locked) { ExecuteFriendsGameStart(); return; }
        if (propertiesThatChanged.ContainsKey("Disbanded") && propertiesThatChanged["Disbanded"] is bool disbanded && disbanded) { HandleRoomDisbandedByHost(); return; }
        if (propertiesThatChanged.ContainsKey(SeatMapPropKey)) { UpdatePlayerListUI(); CheckPlayerCountAndToggleStart(); return; }

        if (propertiesThatChanged.ContainsKey("HAN") || propertiesThatChanged.ContainsKey("BS") || propertiesThatChanged.ContainsKey("BotsIncluded") || propertiesThatChanged.ContainsKey("DS"))
        {
            ApplyFriendsStartFromRoomProperties(); UpdatePlayerListUI(); SyncRoomLobbyUIForRole(); CheckPlayerCountAndToggleStart();
        }

        if (ModeManager.Instance == null) return;

        if (propertiesThatChanged.TryGetValue("GameMode", out object gameModeObj) && gameModeObj is int selectedMode) ModeManager.Instance.OnClick_SarMode(selectedMode, broadcastToRoom: false);
        if (propertiesThatChanged.TryGetValue("TrumpMode", out object trumpModeObj) && trumpModeObj is int selectedTrump) ModeManager.Instance.OnClick_TrumpMode(selectedTrump, broadcastToRoom: false);
        if (propertiesThatChanged.TryGetValue("TaashMode", out object taashModeObj) && taashModeObj is int selectedTaash) ModeManager.Instance.OnClick_TrickMode(selectedTaash, broadcastToRoom: false);
        if (propertiesThatChanged.TryGetValue("LogicMode", out object logicModeObj) && logicModeObj is int selectedLogic) ModeManager.Instance.OnClick_LogicMode(selectedLogic, broadcastToRoom: false);
        if (propertiesThatChanged.TryGetValue("BotsIncluded", out object botsChangedObj) && botsChangedObj is bool botsChangedVal) { ApplyBotsIncludedState(botsChangedVal); UpdatePlayerListUI(); CheckPlayerCountAndToggleStart(); }

        if (propertiesThatChanged.ContainsKey("BS")) ApplyFriendsStartFromRoomProperties();
        if (propertiesThatChanged.ContainsKey("HAN")) UpdatePlayerListUI();
        if (propertiesThatChanged.ContainsKey("GS") && propertiesThatChanged["GS"] is bool started && started && !_friendsGameStartTriggered) ExecuteFriendsGameStart();
    }

    public void DisplayMyID()
    {
        ResolveMyUserIdText();
        if (myUserIdText == null) return;
        string uid = GameUidService.LocalGameUid;
        UidUI.BindCopyLabel(myUserIdText, uid, "My UID: ");
    }

    void ResolveMyUserIdText()
    {
        if (myUserIdText != null) return;
        if (FriendsPanelUIController.Instance != null)
        {
            foreach (Transform t in FriendsPanelUIController.Instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Text_MyID") { myUserIdText = t.GetComponent<TMP_Text>(); break; }
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
        string myId = MyUserId;
        if (!string.IsNullOrEmpty(myId) && friendUserId == myId) { ShowUIError("You cannot add yourself!"); return; }
        if (myFriends.Contains(friendUserId)) { ShowUIError("Already in friends list."); return; }

        myFriends.Add(friendUserId);
        if (!string.IsNullOrEmpty(displayName)) friendDisplayNames[friendUserId] = displayName;
        else if (!friendDisplayNames.ContainsKey(friendUserId)) friendDisplayNames[friendUserId] = friendUserId;

        SaveFriends(); RefreshFriendsListUI(); CheckFriendsOnlineStatus();
    }

    public void RefreshInGameStatusSoon()
    {
        if (isActiveAndEnabled) StartCoroutine(RefreshInGameStatusRoutine());
        else if (SocialServiceBootstrap.Instance != null) SocialServiceBootstrap.Instance.StartCoroutine(RefreshInGameStatusRoutine());
    }

    IEnumerator RefreshInGameStatusRoutine()
    {
        for (int i = 0; i < 3; i++) { yield return new WaitForSeconds(1f); CheckFriendsOnlineStatus(); }
    }

    void StartPresenceHeartbeat()
    {
        PublishOwnPresence();
        if (_presenceHeartbeatCoroutine != null) return;
        if (isActiveAndEnabled) _presenceHeartbeatCoroutine = StartCoroutine(PresenceHeartbeatRoutine());
        else if (SocialServiceBootstrap.Instance != null) _presenceHeartbeatCoroutine = SocialServiceBootstrap.Instance.StartCoroutine(PresenceHeartbeatRoutine());
    }

    IEnumerator PresenceHeartbeatRoutine()
    {
        var wait = new WaitForSeconds(45f);
        while (true) { yield return wait; PublishOwnPresence(); }
    }

    void PublishOwnPresence()
    {
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = new Dictionary<string, object> { { "lastActive", now }, { "online", true } };
        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("users").Child(myId).Child("presence").UpdateChildrenAsync(data);
    }

    static bool CanCallFindFriends()
    {
        if (PhotonNetwork.OfflineMode) return false;
        if (!PhotonNetwork.IsConnectedAndReady) return false;
        if (PhotonNetwork.Server != ServerConnection.MasterServer) return false;
        ClientState state = PhotonNetwork.NetworkClientState;
        return state == ClientState.ConnectedToMasterServer || state == ClientState.JoinedLobby;
    }

    void ScheduleFindFriendsWhenReady()
    {
        if (_findFriendsCoroutine != null) return;
        if (isActiveAndEnabled) _findFriendsCoroutine = StartCoroutine(WaitForPhotonThenFindFriends());
        else if (SocialServiceBootstrap.Instance != null) _findFriendsCoroutine = SocialServiceBootstrap.Instance.StartCoroutine(WaitForPhotonThenFindFriends());
    }

    IEnumerator WaitForPhotonThenFindFriends()
    {
        var wait = new WaitForSeconds(0.25f);
        for (int i = 0; i < 80; i++)
        {
            if (CanCallFindFriends())
            {
                _findFriendsCoroutine = null;
                if (myFriends != null && myFriends.Count > 0) PhotonNetwork.FindFriends(myFriends.ToArray());
                yield break;
            }
            yield return wait;
        }
        _findFriendsCoroutine = null;
    }

    public void RemoveFriend(string friendUserId)
    {
        if (string.IsNullOrEmpty(friendUserId)) return;
        if (!myFriends.Remove(friendUserId)) return;
        friendDisplayNames.Remove(friendUserId); _gameInvitesSent.Remove(friendUserId);
        SaveFriends(); RefreshFriendsListUI(); CheckFriendsOnlineStatus();
    }

    public bool IsFriend(string friendUserId) => !string.IsNullOrEmpty(friendUserId) && myFriends.Contains(friendUserId);

    string MyUserId
    {
        get
        {
            if (FirebaseAuth.DefaultInstance?.CurrentUser != null) return FirebaseAuth.DefaultInstance.CurrentUser.UserId;
            return PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId ?? "";
        }
    }

    string MyDisplayName
    {
        get
        {
            string savedName = PlayerPrefs.GetString("PlayerUsername", "");
            if (!string.IsNullOrEmpty(savedName)) return savedName;
            return string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Player" : PhotonNetwork.NickName;
        }
    }

    public void SendFriendRequest(string targetUserId, string targetName, System.Action<bool> onComplete = null)
    {
        if (string.IsNullOrEmpty(targetUserId)) { onComplete?.Invoke(false); return; }
        targetUserId = targetUserId.Trim();
        if (FirebaseAuth.DefaultInstance?.CurrentUser == null && !Application.isEditor) { ShowUIError("Sign in required to send friend requests."); onComplete?.Invoke(false); return; }

        EnsurePhotonUserId(); EnsureFriendServicesStarted();

        if (GameUidService.LooksLikeUid(targetUserId))
        {
            GameUidService.ResolveFirebaseUid(targetUserId, resolved =>
            {
                if (string.IsNullOrEmpty(resolved)) { ShowUIError("No player found with that UID."); onComplete?.Invoke(false); return; }
                SendFriendRequest(resolved, targetName, onComplete);
            });
            return;
        }

        string myId = MyUserId;
        if (!string.IsNullOrEmpty(myId) && targetUserId == myId) { ShowUIError("You cannot add yourself!"); onComplete?.Invoke(false); return; }
        if (myFriends.Contains(targetUserId)) { ShowUIError("Already in your friends list."); onComplete?.Invoke(false); return; }
        if (incomingRequests.ContainsKey(targetUserId)) { AcceptFriendRequest(targetUserId, incomingRequests[targetUserId]); onComplete?.Invoke(true); return; }
        if (string.IsNullOrEmpty(myId)) { ShowUIError("Not connected yet. Try again."); onComplete?.Invoke(false); return; }

        if (!string.IsNullOrEmpty(targetName)) friendDisplayNames[targetUserId] = targetName;
        var requestData = new Dictionary<string, object> { { "fromUserId", myId }, { "fromName", MyDisplayName }, { "createdAt", System.DateTime.UtcNow.Ticks } };

        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("friend_requests").Child(targetUserId).Child(myId).SetValueAsync(requestData).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted) { ShowUIError("Request failed. Try again."); onComplete?.Invoke(false); return; }
            ShowUIError(string.IsNullOrEmpty(targetName) ? "Friend request sent!" : $"Request sent to {targetName}!");
            onComplete?.Invoke(true);
        });
    }

    public void StartFriendRequestListener()
    {
        if (_requestListenerStarted) return;
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;

        requestDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("friend_requests").Child(myId);
        requestDbRef.ChildAdded += OnFriendRequestAdded;
        requestDbRef.ChildRemoved += OnFriendRequestRemoved;
        _requestListenerStarted = true;

        requestDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;
            foreach (DataSnapshot child in task.Result.Children)
            {
                string fromId = child.Key;
                if (string.IsNullOrEmpty(fromId) || myFriends.Contains(fromId)) continue;
                string fromName = child.Child("fromName").Value?.ToString();
                if (string.IsNullOrEmpty(fromName)) fromName = child.Child("fromUserId").Value?.ToString() ?? fromId;
                if (string.IsNullOrEmpty(fromName)) fromName = fromId;
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

        string fromName = args.Snapshot.Child("fromName").Value?.ToString();
        if (string.IsNullOrEmpty(fromName)) fromName = args.Snapshot.Child("fromUserId").Value?.ToString() ?? fromId;
        if (string.IsNullOrEmpty(fromName)) fromName = fromId;
        incomingRequests[fromId] = fromName;
        RefreshFriendsListUI(); NotifyRequestsChanged();
        if (FriendsPanelUIController.Instance != null) FriendsPanelUIController.Instance.ShowTab(FriendsPanelUIController.PanelTab.Requests);
    }

    void OnFriendRequestRemoved(object sender, ChildChangedEventArgs args)
    {
        if (args.Snapshot == null) return;
        string fromId = args.Snapshot.Key;
        if (!string.IsNullOrEmpty(fromId) && incomingRequests.Remove(fromId)) RefreshFriendsListUI();
    }

    // 🚨 BUG FIX: TWO WAY SYNC! Ab dono ke devices mein friends dikhenge!
    public void AcceptFriendRequest(string fromUserId, string fromName)
    {
        if (string.IsNullOrEmpty(fromUserId)) return;
        
        AddFriend(fromUserId, fromName);
        WriteFriendToFirebase(fromUserId, fromName);

        string myId = MyUserId;
        if (!string.IsNullOrEmpty(myId))
        {
            // Apne server par accept notify karna
            var acceptData = new Dictionary<string, object> { { "name", MyDisplayName }, { "createdAt", System.DateTime.UtcNow.Ticks } };
            FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("friend_accepts").Child(fromUserId).Child(myId).SetValueAsync(acceptData);
            
            // 🚨 NAYI LINE: Direct friend ke account me khud ko dalna (Offline hone par bhi sync hoga)
            FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("users").Child(fromUserId).Child("friends").Child(myId).SetValueAsync(MyDisplayName);
            
            // Inbox se hatao
            FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("friend_requests").Child(myId).Child(fromUserId).RemoveValueAsync();
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
            FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("friend_requests").Child(myId).Child(fromUserId).RemoveValueAsync();
        }
        incomingRequests.Remove(fromUserId); RefreshFriendsListUI(); NotifyRequestsChanged();
    }

    public void StartFriendAcceptListener()
    {
        if (_acceptListenerStarted) return;
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;

        acceptDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("friend_accepts").Child(myId);
        acceptDbRef.ChildAdded += OnFriendAcceptAdded;
        _acceptListenerStarted = true;

        acceptDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;
            foreach (DataSnapshot child in task.Result.Children)
            {
                if (!child.Exists) continue;
                string accepterId = child.Key;
                string accepterName = child.Child("name").Value?.ToString() ?? accepterId;
                if (!string.IsNullOrEmpty(accepterId) && !myFriends.Contains(accepterId))
                {
                    AddFriend(accepterId, accepterName);
                    WriteFriendToFirebase(accepterId, accepterName);
                }
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
        WriteFriendToFirebase(accepterId, accepterName);
        ShowUIError($"{accepterName} accepted your request!");
        args.Snapshot.Reference.RemoveValueAsync();
    }

    public override void OnFriendListUpdate(List<FriendInfo> friendList)
    {
        friendPhotonStatus.Clear();
        foreach (FriendInfo friend in friendList) friendPhotonStatus[friend.UserId] = friend;
        RefreshFriendsListUI();
    }

    public void RefreshFriendsListUI()
    {
        NotifyFriendsStatusChanged();
        if (FriendsPanelUIController.Instance != null) { FriendsPanelUIController.Instance.RefreshAll(); return; }
        RefreshFriendsListLegacy();
    }

    void RefreshFriendsListLegacy()
    {
        if (friendsListContainer == null || friendUIPrefab == null) return;
        foreach (Transform child in friendsListContainer) Destroy(child.gameObject);
        foreach (var kvp in incomingRequests) { if (string.IsNullOrEmpty(kvp.Key)) continue; SpawnRequestRow(kvp.Key, kvp.Value); }
        foreach (string friendId in myFriends) { if (string.IsNullOrEmpty(friendId)) continue; friendPhotonStatus.TryGetValue(friendId, out FriendInfo photonInfo); SpawnFriendRow(friendId, GetFriendDisplayNameInternal(friendId), photonInfo); }
    }

    void SpawnRequestRow(string fromId, string fromName)
    {
        GameObject prefab = friendRequestRowPrefab != null ? friendRequestRowPrefab : friendUIPrefab;
        if (prefab == null || friendsListContainer == null) return;
        GameObject row = Instantiate(prefab, friendsListContainer);

        TMP_Text infoText = FindPrimaryLabel(row.transform);
        if (infoText != null) infoText.text = $"{fromName}\n<size=18><color=#FFD479>wants to be friends</color></size>";

        Button acceptBtn = FindChildButton(row.transform, "AcceptButton");
        Button declineBtn = FindChildButton(row.transform, "DeclineButton");
        if (acceptBtn == null || declineBtn == null) { Button[] buttons = row.GetComponentsInChildren<Button>(true); if (buttons.Length >= 2) { acceptBtn = acceptBtn ?? buttons[0]; declineBtn = declineBtn ?? buttons[1]; } }
        if (acceptBtn != null) { acceptBtn.onClick.RemoveAllListeners(); acceptBtn.onClick.AddListener(() => AcceptFriendRequest(fromId, fromName)); }
        if (declineBtn != null) { declineBtn.onClick.RemoveAllListeners(); declineBtn.onClick.AddListener(() => DeclineFriendRequest(fromId)); }
    }

    string GetFriendDisplayNameInternal(string friendId)
    {
        if (friendDisplayNames.TryGetValue(friendId, out string name) && !string.IsNullOrEmpty(name)) return name;
        return friendId;
    }

    void SpawnFriendRow(string friendId, string displayName, FriendInfo photonInfo)
    {
        GameObject row = Instantiate(friendUIPrefab, friendsListContainer);
        TMP_Text friendText = FindPrimaryLabel(row.transform);
        bool online = IsFriendOnline(friendId);
        bool inGame = IsFriendInGame(friendId);
        string status = "🔴 Offline";
        if (online) status = inGame ? "🎮 In Game" : "🟢 Online";

        if (friendText != null) { friendText.text = $"{displayName}\n{status}"; friendText.color = online ? Color.green : Color.gray; }

        Button inviteBtn = FindChildButton(row.transform, "InviteButton");
        if (inviteBtn == null) { Button[] buttons = row.GetComponentsInChildren<Button>(true); inviteBtn = buttons.Length > 0 ? buttons[buttons.Length - 1] : null; }
        if (inviteBtn != null) { inviteBtn.onClick.RemoveAllListeners(); inviteBtn.onClick.AddListener(() => InviteFriendToGame(friendId, displayName)); TMP_Text inviteLabel = inviteBtn.GetComponentInChildren<TMP_Text>(); if (inviteLabel != null) inviteLabel.text = "Invite"; }
    }

    static TMP_Text FindPrimaryLabel(Transform root)
    {
        TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++) { if (labels[i].GetComponentInParent<Button>() == null) return labels[i]; }
        return labels.Length > 0 ? labels[0] : null;
    }

    static Button FindChildButton(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { if (t.name == childName) return t.GetComponent<Button>(); }
        return null;
    }

    public void InviteFriendToGame(string friendUserId, string friendDisplayName = null)
    {
        if (string.IsNullOrEmpty(friendUserId)) return;
        if (!PhotonNetwork.IsConnectedAndReady) { ShowUIError("Server not ready. Wait for connection..."); return; }

        _pendingInviteFriendId = friendUserId;
        _pendingInviteFriendName = string.IsNullOrEmpty(friendDisplayName) ? GetFriendDisplayNameInternal(friendUserId) : friendDisplayName;

        if (PhotonNetwork.OfflineMode) { ShowUIError("Can't invite friends in practice mode."); _pendingInviteFriendId = null; _pendingInviteFriendName = null; return; }

        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            if (!PhotonNetwork.CurrentRoom.IsOpen) PhotonNetwork.CurrentRoom.IsOpen = true;
            SendFirebaseInvite(_pendingInviteFriendId, PhotonNetwork.CurrentRoom.Name, _pendingInviteFriendName);
            _pendingInviteFriendId = null; _pendingInviteFriendName = null;
            return;
        }

        CreatePrivateRoom();
        ShowUIError("Creating room for invite...");
    }

    void TrySendPendingInvite()
    {
        if (string.IsNullOrEmpty(_pendingInviteFriendId)) return;
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.IsVisible) return;
        SendFirebaseInvite(_pendingInviteFriendId, PhotonNetwork.CurrentRoom.Name, _pendingInviteFriendName);
        _pendingInviteFriendId = null; _pendingInviteFriendName = null;
    }

    void SendFirebaseInvite(string targetUserId, string roomPin, string friendName)
    {
        if (string.IsNullOrEmpty(targetUserId) || string.IsNullOrEmpty(roomPin)) return;
        string fromId = MyUserId;
        string fromName = MyDisplayName;

        var inviteData = new Dictionary<string, object>
        {
            { "roomPin", roomPin },
            { "fromUserId", fromId },
            { "fromName", fromName },
            { "timestamp", System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
        };

        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("invites").Child(targetUserId).Child(roomPin).SetValueAsync(inviteData).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted) { ShowUIError("Invite failed. Try again."); return; }
            MarkGameInviteSent(targetUserId); RefreshFriendsListUI(); ShowUIError($"Invite sent to {friendName}!");
        });
    }

    public void StartInviteListener()
    {
        if (_inviteListenerStarted) return;
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;

        inviteDbRef = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("invites").Child(myId);
        inviteDbRef.ChildAdded += OnIncomingInviteAdded;
        _inviteListenerStarted = true;

        inviteDbRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled || task.Result == null || !task.Result.Exists) return;
            foreach (DataSnapshot child in task.Result.Children) TryRegisterIncomingInviteSnapshot(child);
        });
    }

    void TryRegisterIncomingInviteSnapshot(DataSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.Exists) return;

        string inviteId = snapshot.Key;
        string roomPin = snapshot.Child("roomPin").Value?.ToString();
        string fromName = snapshot.Child("fromName").Value?.ToString() ?? "Friend";
        string fromUserId = snapshot.Child("fromUserId").Value?.ToString();
        if (string.IsNullOrEmpty(roomPin)) roomPin = inviteId;
        if (string.IsNullOrEmpty(inviteId)) inviteId = roomPin;
        if (string.IsNullOrEmpty(roomPin)) return;

        if (IsInviteExpired(snapshot))
        {
            RemoveInviteFromFirebase(inviteId);
            return;
        }

        RegisterPendingInvite(inviteId, roomPin, fromName, fromUserId);
        ShowIncomingInvite(fromName, roomPin, inviteId);
    }

    static bool IsInviteExpired(DataSnapshot snapshot)
    {
        long inviteTimestamp = ReadInviteTimestamp(snapshot);
        if (inviteTimestamp <= 0) return false;

        if (inviteTimestamp > 100000000000L) 
            inviteTimestamp /= 1000;

        long currentTimeSeconds = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long diffSeconds = currentTimeSeconds - inviteTimestamp;

        if (diffSeconds > 15 || diffSeconds < -15) 
            return true;

        return false;
    }

    static long ReadInviteTimestamp(DataSnapshot snapshot)
    {
        if (snapshot == null) return 0;
        DataSnapshot tsNode = snapshot.Child("timestamp");
        if (tsNode.Exists && tsNode.Value != null && long.TryParse(tsNode.Value.ToString(), out long unixTs)) return unixTs;
        DataSnapshot createdNode = snapshot.Child("createdAt");
        if (!createdNode.Exists || createdNode.Value == null) return 0;
        if (!long.TryParse(createdNode.Value.ToString(), out long raw)) return 0;
        if (raw > 1_000_000_000_000L) return new System.DateTimeOffset(new System.DateTime(raw, System.DateTimeKind.Utc)).ToUnixTimeSeconds();
        return raw;
    }

    void OnIncomingInviteAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;
        TryRegisterIncomingInviteSnapshot(args.Snapshot);
    }

    void RegisterPendingInvite(string inviteId, string roomPin, string fromName, string fromUserId)
    {
        if (string.IsNullOrEmpty(inviteId) || string.IsNullOrEmpty(roomPin)) return;
        _pendingGameInvites[inviteId] = new PendingGameInvite { InviteId = inviteId, RoomPin = roomPin, FromName = fromName, FromUserId = fromUserId };
    }

    public void AcceptInvite(string inviteId)
    {
        if (string.IsNullOrEmpty(inviteId)) return;
        if (!_pendingGameInvites.TryGetValue(inviteId, out PendingGameInvite invite))
            invite = new PendingGameInvite { InviteId = inviteId, RoomPin = inviteId };

        string roomPin = invite.RoomPin;
        RemoveInviteFromFirebase(invite.InviteId);
        _pendingGameInvites.Remove(invite.InviteId);
        IncomingInvitePopup.Dismiss();

        if (string.IsNullOrEmpty(roomPin)) { ShowUIError("Invite expired."); return; }

        if (PhotonNetwork.InRoom)
        {
            bool inActiveGame = PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gsObj) && gsObj is bool gsBool && gsBool;
            if (inActiveGame) { ShowUIError("Leave your current game first."); return; }
        }

        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            if (PhotonNetwork.CurrentRoom.Name == roomPin) return; 
            SuppressSeatLobbyOnJoin = false;
            PendingJoinPin = roomPin;
            if (NetworkManager.Instance != null) NetworkManager.Instance.ShowLoading("Joining friend's table...");
            PhotonNetwork.LeaveRoom();
            return;
        }

        JoinRoomWithPINText(roomPin);
    }

    public void DeclineInvite(string inviteId)
    {
        if (string.IsNullOrEmpty(inviteId)) return;
        RemoveInviteFromFirebase(inviteId);
        _pendingGameInvites.Remove(inviteId);
        IncomingInvitePopup.Dismiss();
    }

    void RemoveInviteFromFirebase(string inviteId)
    {
        if (string.IsNullOrEmpty(inviteId)) return;
        string myId = MyUserId;
        if (string.IsNullOrEmpty(myId)) return;
        FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("invites").Child(myId).Child(inviteId).RemoveValueAsync();
    }

    void ShowIncomingInvite(string fromName, string roomPin, string inviteId)
    {
        if (pinInputField != null) pinInputField.text = roomPin;
        IncomingInvitePopup.ShowInvite(fromName, roomPin, inviteId);
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

    string _friendsLoadedForUser;

    void WriteFriendToFirebase(string friendUid, string displayName)
    {
        if (string.IsNullOrEmpty(friendUid)) return;
        Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null) return;
        string myUid = user.UserId;
        if (string.IsNullOrEmpty(myUid)) return;

        string nameToStore = string.IsNullOrEmpty(displayName) ? friendUid : displayName;
        Firebase.Database.FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("users").Child(myUid).Child("friends").Child(friendUid).SetValueAsync(nameToStore);
    }

    public void LoadFriendsFromFirebase()
    {
        Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
        if (user == null) return;
        string myUid = user.UserId;
        if (string.IsNullOrEmpty(myUid)) return;
        if (myFriends == null) myFriends = new List<string>();

        Firebase.Database.FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("users").Child(myUid).Child("friends").GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled) return;
            Firebase.Database.DataSnapshot snap = task.Result;
            if (snap == null || !snap.Exists) return;

            foreach (Firebase.Database.DataSnapshot child in snap.Children)
            {
                string friendUid = child.Key;
                if (string.IsNullOrEmpty(friendUid)) continue;
                string displayName = child.Value?.ToString() ?? friendUid;
                if (!myFriends.Contains(friendUid)) myFriends.Add(friendUid);
                friendDisplayNames[friendUid] = displayName;
            }
            SaveFriends();
            FriendsPanelUIController.Instance?.RefreshAll();
            RefreshFriendsListUI();
        });
    }
}