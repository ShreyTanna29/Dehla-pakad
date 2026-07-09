// using UnityEngine;
// using TMPro;
// using UnityEngine.UI;
// using DG.Tweening;
// using Photon.Pun;
// using Photon.Realtime;
// using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
// using System.Collections;
// using System.Collections.Generic;
// using System.Linq;
// using System.Reflection;

// public class ResultManager : MonoBehaviourPunCallbacks
// {
//     public static ResultManager Instance;

//     [Header("UI Root")]
//     public CanvasGroup resultPanel;
//     [Tooltip("Optional root to find Panel_Winning without GameObject.Find (e.g. game canvas).")]
//     public Transform resultPanelSearchRoot;
//     public TMP_FontAsset customFont;

//     [Header("Optional — wired in scene or auto-built")]
//     public TMP_Text titleText;
//     public TMP_Text descriptionText;
//     public Button homeButton;
//     public Button restartButton;
//     public Transform scoreboardContainer;

//     [Header("Leaderboard Theme (assign in scene)")]
//     [Tooltip("Rounded wooden board sprite used for the panel background (e.g. BG_Buttons).")]
//     public Sprite woodBoardSprite;
//     [Tooltip("Settings gear icon sprite (e.g. settings_button).")]
//     public Sprite gearButtonSprite;
//     [Tooltip("Fixed avatar shown for every player column in the leaderboard. Assign once here — this exact sprite is what appears in game (no runtime randomization).")]
//     public Sprite playerAvatarSprite;

//     [System.Serializable]
//     public class PlayerResult
//     {
//         public string name;
//         public int actorNumber;
//         public int bid; // Restored
//         public int tricksWon;
//         public int dehlasCollected;
//         public bool isCompleted;
//         public float score;
//         public int rank;
//     }

//     [System.Serializable]
//     public class RoundResult
//     {
//         public int roundNumber;
//         public int[] dehlasPerSeat = new int[4];
//         public int[] tricksPerSeat = new int[4];
//     }

//     public int currentRound = 1;
//     /// <summary>5 for Bots/Online, -1 for unlimited Friends matches.</summary>
//     public int maxRounds = 5;
//     public List<RoundResult> roundHistory = new List<RoundResult>();

//     const int MaxRoundsBotsOnline = 5;
//     // Task 13: keep the leaderboard visible for 5 seconds after a round before auto-closing.
//     const float InterRoundLeaderboardSeconds = 5f;
//     const float MatchEndLeaderboardSeconds = 10f;

//     // Task 15: full-screen-bottom banner ad reserve. The board is lifted clear of this band so the
//     // leaderboard never overlaps the bottom banner ad. Kept in one place so the banner placeholder
//     // and the board lift stay in sync.
//     const float BannerAdHeightPx = 110f;
//     const float BannerAdSafeMarginPx = 24f;

//     private PlayerResult[] playerResults = new PlayerResult[4];
//     private Transform _builtRoot;
//     private Image _dimOverlay;
//     private readonly List<GameObject> _dynamicRows = new List<GameObject>();
//     // Runtime-only extra round rows created when a Friends match runs past the static row count.
//     private readonly List<GameObject> _overflowRows = new List<GameObject>();
//     private bool _isShowingResult;
//     private bool _statsRecorded;
//     // Phase 10: stable id for the finished match, reused as the Firebase pastGames key so a
//     // re-entry into RecordMatchStats can never create a duplicate cloud record.
//     private string _matchId;
//     private bool _resultActionTaken;
//     private bool _autoTransitionMode;
//     private bool _roundTransitionRunning;
//     private ScrollRect _roundScrollRect;
//     private static bool _resultPanelResolveWarned;

//     /// <summary>
//     /// Records the local player's finished match into <see cref="ProfileStatsStore"/>, split by
//     /// Vs Bots / Vs Online. Guarded so it only counts once per match.
//     /// </summary>
//     void RecordMatchStats()
//     {
//         if (_statsRecorded) return;
//         _statsRecorded = true;

//         PlayerResult me = playerResults != null && playerResults.Length > 0 ? playerResults[0] : null;
//         if (me == null) return;

//         bool vsBots = PhotonNetwork.OfflineMode ||
//                       (DeckManager.botActorNumbers != null && DeckManager.botActorNumbers.Count > 0);
//         int rank = me.rank <= 0 ? 4 : me.rank;
//         bool kot = me.dehlasCollected >= GetKotThreshold();

//         ProfileStatsStore.RecordCompletedGame(vsBots, rank, me.score, me.bid, kot);
//         Debug.Log($"[Stats] Recorded {(vsBots ? "VsBots" : "Online")} game: rank={rank} score={me.score} bid={me.bid} kot={kot}");

//         // Phase 10: in addition to the local PlayerPrefs write above (offline fallback), mirror this
//         // finished match to Firebase under the signed-in user so the Past Games screen can load from
//         // the cloud across devices. Uses matchId as the KEY so a re-entry cannot create a duplicate.
//         SaveMatchToFirebase(vsBots, rank, me.score);
//     }

//     /// <summary>
//     /// Phase 10 — Writes the just-finished match to <c>users/{uid}/pastGames/{matchId}</c> in Firebase
//     /// Realtime Database. matchId is the Photon room name (online/friends) or an <c>offline_{ticks}</c>
//     /// id (bots/offline), cached in <see cref="_matchId"/> so the same key is reused. No-op (with a log)
//     /// when no user is signed in — the local PlayerPrefs history remains the offline fallback.
//     /// </summary>
//     void SaveMatchToFirebase(bool vsBots, int rank, float score)
//     {
//         if (string.IsNullOrEmpty(_matchId))
//         {
//             string roomName = Photon.Pun.PhotonNetwork.CurrentRoom != null
//                 ? Photon.Pun.PhotonNetwork.CurrentRoom.Name
//                 : null;
//             _matchId = string.IsNullOrEmpty(roomName)
//                 ? $"offline_{System.DateTime.UtcNow.Ticks}"
//                 : roomName;
//         }

//         Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance != null
//             ? Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser
//             : null;
//         if (user == null || string.IsNullOrEmpty(user.UserId))
//         {
//             Debug.Log("[Stats] Skipping Firebase past-game save — no signed-in user (offline). Local history kept.");
//             return;
//         }

//         string uid = user.UserId;

//         var record = new System.Collections.Generic.Dictionary<string, object>
//         {
//             { "timeTicks", System.DateTime.UtcNow.Ticks },
//             { "vsBots", vsBots },
//             { "rank", rank },
//             { "score", score },
//             { "canceled", false }
//         };
//         if (GameSettings.Instance != null)
//             record["mode"] = GameSettings.Instance.currentMode.ToString();

//         Firebase.Database.DatabaseReference pastGameRef =
//             Firebase.Database.FirebaseDatabase
//                 .GetInstance("https://dehlapakad-c207c-default-rtdb.firebaseio.com/")
//                 .RootReference
//                 .Child("users").Child(uid).Child("pastGames").Child(_matchId);

//         Firebase.Extensions.TaskExtension.ContinueWithOnMainThread(
//             pastGameRef.SetValueAsync(record),
//             task =>
//             {
//                 if (task.IsFaulted || task.IsCanceled)
//                     Debug.LogWarning($"[Stats] Firebase past-game save FAILED (users/{uid}/pastGames/{_matchId}): {task.Exception}");
//                 else
//                     Debug.Log($"[Stats] Saved past game to Firebase: users/{uid}/pastGames/{_matchId}");
//             });
//     }

//     const int KotDehlasOneTaash = 4;
//     const int KotDehlasTwoTaash = 8;

//     // Professional Theme Colors
//     static readonly Color PanelBgColor = new Color(0.25f, 0.15f, 0.05f, 0.95f); // Wooden Dark
//     static readonly Color FrameColor = new Color(0.45f, 0.28f, 0.15f, 1f);     // Wooden Frame
//     static readonly Color RowBgColor = new Color(0f, 0f, 0f, 0.35f);           // Semi-transparent rows
//     static readonly Color WinnerGoldColor = new Color(1f, 0.84f, 0f, 1f);      // Gold highlight
//     static readonly Color TextWhiteColor = Color.white;
//     static readonly Color TextGoldColor = new Color(1f, 0.92f, 0.5f, 1f);
//     static readonly Color ScoreDarkColor = new Color(0.16f, 0.09f, 0.04f, 1f);

//     void Awake()
//     {
//         if (Instance == null) Instance = this;
//         else Destroy(gameObject);

//         for (int i = 0; i < 4; i++)
//             playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };

//         HideResultPanelImmediate();
//         WireButtons();
//     }

//     void WireButtons()
//     {
//         if (homeButton != null)
//         {
//             EnableButtonVisuals(homeButton);
//             homeButton.onClick.RemoveAllListeners();
//             homeButton.onClick.AddListener(OnHomeClicked);
//         }
//         if (restartButton != null)
//         {
//             EnableButtonVisuals(restartButton);
//             restartButton.onClick.RemoveAllListeners();
//             restartButton.onClick.AddListener(OnRestartClicked);
//         }
//     }

//     static void ShowLeaderboardBanner()
//     {
//         if (AdsManager.Instance == null) return;
//         AdsManager.Instance.LoadBanner();
//         AdsManager.Instance.ShowBanner();
//     }

//     static void HideLeaderboardBanner()
//     {
//         if (AdsManager.Instance == null) return;
//         AdsManager.Instance.HideBanner();
//     }

//     void HideResultPanelImmediate()
//     {
//         _isShowingResult = false;
//         HideLeaderboardBanner();
//         if (!ResolveResultPanel()) return;
//         resultPanel.DOKill();
//         resultPanel.alpha = 0;
//         resultPanel.interactable = false;
//         resultPanel.blocksRaycasts = false;
//         resultPanel.gameObject.SetActive(false);
//         if (_dimOverlay != null)
//             _dimOverlay.color = new Color(0f, 0f, 0f, 0f);
//     }

//     bool ResolveResultPanel()
//     {
//         if (resultPanel != null) return true;

//         Transform root = resultPanelSearchRoot;
//         if (root == null)
//         {
//             Canvas canvas = Object.FindAnyObjectByType<Canvas>();
//             if (canvas != null)
//                 root = canvas.transform.root;
//         }

//         if (root != null)
//         {
//             UiSafeLookup.SetSearchRoot(root);
//             if (UiSafeLookup.TryGet("Panel_Winning", out GameObject panelGo) && panelGo != null)
//             {
//                 resultPanel = panelGo.GetComponent<CanvasGroup>();
//                 if (resultPanel == null)
//                     resultPanel = panelGo.AddComponent<CanvasGroup>();
//                 Debug.Log("[ResultManager] Resolved Panel_Winning under canvas hierarchy.");
//                 return true;
//             }
//         }

//         if (!_resultPanelResolveWarned)
//         {
//             _resultPanelResolveWarned = true;
//             Debug.LogWarning("[ResultManager] resultPanel not found — assign resultPanel or Panel_Winning under resultPanelSearchRoot.");
//         }
//         return false;
//     }

//     void EnsurePanelHierarchyActive()
//     {
//         if (resultPanel == null) return;

//         Transform t = resultPanel.transform;
//         while (t != null)
//         {
//             if (!t.gameObject.activeSelf)
//                 t.gameObject.SetActive(true);
//             t = t.parent;
//         }

//         Canvas rootCanvas = resultPanel.GetComponentInParent<Canvas>();
//         if (rootCanvas != null)
//             rootCanvas.gameObject.SetActive(true);

//         resultPanel.transform.SetAsLastSibling();
//     }

//     void EnsureDimOverlay()
//     {
//         if (resultPanel == null) return;

//         Transform existing = resultPanel.transform.Find("Overlay");
//         if (existing != null)
//         {
//             _dimOverlay = existing.GetComponent<Image>();
//             if (_dimOverlay == null)
//                 _dimOverlay = existing.gameObject.AddComponent<Image>();
//         }
//         else
//         {
//             GameObject overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
//             overlayGo.transform.SetParent(resultPanel.transform, false);
//             overlayGo.transform.SetAsFirstSibling();
//             RectTransform rt = overlayGo.GetComponent<RectTransform>();
//             rt.anchorMin = Vector2.zero;
//             rt.anchorMax = Vector2.one;
//             rt.offsetMin = Vector2.zero;
//             rt.offsetMax = Vector2.zero;
//             _dimOverlay = overlayGo.GetComponent<Image>();
//             _dimOverlay.raycastTarget = true;
//         }

//         _dimOverlay.color = new Color(0f, 0f, 0f, 0.55f);
//         _dimOverlay.raycastTarget = true;

//         // Task 16: tapping outside the board (on the full-screen overlay) closes the leaderboard —
//         // but ONLY when the panel is shown manually. Task 13: during an automatic inter-round /
//         // match-end transition the leaderboard must stay up for its full duration, so we do NOT wire
//         // tap-to-close while _autoTransitionMode is active (a stray tap was dismissing it instantly).
//         var overlayBtn = _dimOverlay.GetComponent<Button>();
//         if (overlayBtn == null) overlayBtn = _dimOverlay.gameObject.AddComponent<Button>();
//         overlayBtn.transition = Selectable.Transition.None;
//         overlayBtn.onClick.RemoveAllListeners();
//         if (!_autoTransitionMode)
//             overlayBtn.onClick.AddListener(CloseResult);
//     }

//     string GetInitialPlayerName(int i)=> i == 0 ? "You" : "Dehla_AI_" + i;

//     public void SetBid(int seatIndex, int bidValue)
//     {
//         if (seatIndex >= 0 && seatIndex < 4)
//             playerResults[seatIndex].bid = bidValue;
//     }

//     public void OnTrickWon(int winnerSeatIndex, int dehlaCount)
//     {
//         if (winnerSeatIndex < 0 || winnerSeatIndex >= 4) return;
//         playerResults[winnerSeatIndex].tricksWon++;
//         playerResults[winnerSeatIndex].dehlasCollected += dehlaCount;

//         if (PhotonNetwork.IsMasterClient)
//             SyncScoresToRoomProperties();
//     }

//     void SyncScoresToRoomProperties()
//     {
//         if (!PhotonNetwork.InRoom) return;
//         int[] tricks = new int[4];
//         int[] dehlas = new int[4];
//         for (int i = 0; i < 4; i++)
//         {
//             tricks[i] = playerResults[i].tricksWon;
//             dehlas[i] = playerResults[i].dehlasCollected;
//         }
//         PhotonNetwork.CurrentRoom.SetCustomProperties(
//             new PhotonHashtable { { "SW", tricks }, { "DL", dehlas } });
//     }

//     public override void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
//     {
//         if (propertiesThatChanged == null) return;
//         if (propertiesThatChanged.ContainsKey("CR") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CR", out object crObj))
//             currentRound = (int)crObj;
//         if (propertiesThatChanged.ContainsKey("MR") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("MR", out object mrObj))
//             maxRounds = (int)mrObj;
//         if (propertiesThatChanged.ContainsKey("SW") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("SW", out object tricksObj))
//         {
//             int[] tricks = tricksObj as int[];
//             for (int i = 0; tricks != null && i < 4 && i < tricks.Length; i++)
//                 playerResults[i].tricksWon = tricks[i];
//         }
//         if (propertiesThatChanged.ContainsKey("DL") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DL", out object dehlaObj))
//         {
//             int[] dehlas = dehlaObj as int[];
//             for (int i = 0; dehlas != null && i < 4 && i < dehlas.Length; i++)
//                 playerResults[i].dehlasCollected = dehlas[i];
//         }
//     }

//     public void InitializeForMatch()
//     {
//         bool unlimited = GameSettings.Instance != null
//             && GameSettings.Instance.currentMatchType == MatchType.PlayWithFriends;
//         if (!unlimited && DeckManager.IsPrivateFriendsRoom())
//             unlimited = true;

//         maxRounds = unlimited ? -1 : MaxRoundsBotsOnline;
//         currentRound = 1;
//         roundHistory.Clear();
//         _roundTransitionRunning = false;
//         _statsRecorded = false;
//         _matchId = null;
//         ResetRoundPlayerStats();

//         if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
//             SyncRoundConfigToRoom();
//     }

//     void SyncRoundConfigToRoom()
//     {
//         if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
//         PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable
//         {
//             { "CR", currentRound },
//             { "MR", maxRounds }
//         });
//     }

//     public bool IsMatchOver() => maxRounds != -1 && currentRound >= maxRounds;

//     /// <summary>Master-only entry when the 13th trick of a round completes.</summary>
//     public void TriggerRoundCompletedFromMaster()
//     {
//         if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.OfflineMode) return;
//         if (DeckManager.Instance != null && DeckManager.Instance.photonView != null)
//             DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_OnRoundCompleted), RpcTarget.All);
//         else
//             OnRoundCompleted();
//     }

//     public void OnRoundCompleted()
//     {
//         if (_roundTransitionRunning) return;

//         if (TurnManager.Instance != null)
//             TurnManager.Instance.StopTimer();

//         // Task 17 root cause: the next-round trigger used to be scheduled AFTER the (heavy, fragile)
//         // scoring + Panel_Winning UI build. Any exception thrown while computing scores or building
//         // the leaderboard aborted OnRoundCompleted before BeginRoundEndSequence/RoundTransitionRoutine
//         // was ever reached — so the next round never started. We now compute scores and render the
//         // leaderboard defensively (try/catch) and mark the transition as running FIRST, guaranteeing
//         // the next-round deal is always scheduled regardless of any UI/scoring failure.
//         try
//         {
//             EnsurePlayerResults();
//             RefreshPlayerNamesAndActors();
//             CalculateScores();
//             AssignRanks();
//             FinalizeCurrentRoundScores();
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError($"[Result] Round scoring failed (continuing round lifecycle): {e}");
//         }

//         bool matchOver = IsMatchOver();
//         if (matchOver)
//             GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);

//         // Set BEFORE the leaderboard render so an exception below can never block the trigger.
//         _roundTransitionRunning = true;
//         bool authoritative = PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

//         try
//         {
//             ShowRoundLeaderboard(matchOver);
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError($"[Result] Leaderboard render failed (continuing round lifecycle): {e}");
//         }

//         if (matchOver)
//             StartCoroutine(RoundTransitionRoutine(matchOver, authoritative));
//         else if (GameManager.Instance != null)
//             GameManager.Instance.BeginRoundEndSequence(authoritative);
//         else
//             StartCoroutine(RoundTransitionRoutine(matchOver, authoritative));
//     }

//     /// <summary>Called by <see cref="GameManager"/> once the leaderboard window and hide are complete.</summary>
//     public void NotifyRoundEndSequenceComplete()
//     {
//         HideResultPanelImmediate();
//         _roundTransitionRunning = false;
//     }

//     IEnumerator RoundTransitionRoutine(bool matchOver, bool authoritative)
//     {
//         float wait = matchOver ? MatchEndLeaderboardSeconds : InterRoundLeaderboardSeconds;
//         yield return new WaitForSecondsRealtime(wait);

//         HideResultPanelImmediate();
//         _roundTransitionRunning = false;

//         if (!authoritative)
//             yield break;

//         if (matchOver)
//         {
//             AssignMatchRanksFromHistory();
//             RecordMatchStats();
//             if (DeckManager.Instance != null)
//                 DeckManager.Instance.ResetMatchState();

//             if (PhotonNetwork.InRoom)
//                 PhotonNetwork.LeaveRoom();
//             else if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.ReturnToHomeScreen();
//             yield break;
//         }

//         int nextRound = currentRound + 1;
//         if (DeckManager.Instance != null)
//         {
//             if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
//                 DeckManager.Instance.ResetRoundStateForNextRound();
//             if (DeckManager.Instance.photonView != null)
//                 DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_BeginNextRound), RpcTarget.AllBuffered, nextRound);
//         }
//     }

//     public void ApplyNextRoundStart(int newRound)
//     {
//         currentRound = newRound;
//         ResetRoundPlayerStats();
//         if (PhotonNetwork.IsMasterClient)
//             SyncRoundConfigToRoom();
//     }

//     void ShowRoundLeaderboard(bool matchOver)
//     {
//         ShowResultInternal(autoTransition: true, matchOver: matchOver);
//     }

//     public void ToggleLeaderboard()
//     {
//         if (!ResolveResultPanel() || resultPanel == null) return;
//         if (GameFlowState.Current == GameFlowPhase.Dealing) return;

//         CanvasGroup cg = resultPanel.GetComponent<CanvasGroup>();
//         bool isActuallyVisible = resultPanel.gameObject.activeSelf && cg != null && cg.alpha > 0.1f;

//         if (isActuallyVisible)
//         {
//             CloseResult();
//         }
//         else
//         {
//             resultPanel.gameObject.SetActive(true);
//             resultPanel.transform.SetAsLastSibling();
//             if (cg != null)
//             {
//                 DOTween.Kill(cg);
//                 cg.alpha = 1f;
//                 cg.interactable = true;
//                 cg.blocksRaycasts = true;
//             }
//         }
//     }

//     public void CloseResult()
//     {
//         // Player kisi bhi time leaderboard close kar sakta hai.
//         // Background mein cards deal hoti rehni chahiye — close sirf panel hide karta hai.
//         if (_autoTransitionMode && _roundTransitionRunning)
//             _roundTransitionRunning = false;

//         HideResultPanelImmediate();
//     }

//     /// <summary>
//     /// Hard, immediate leaderboard teardown callable from the deal pipeline. Guarantees that NO
//     /// client begins rendering the next deal while its leaderboard is still on screen (the per-client
//     /// 5s hide timers can drift, so the dealing RPC could otherwise arrive before a client hides).
//     /// </summary>
//     public void ForceHideLeaderboardNow()
//     {
//         if (_roundTransitionRunning)
//         {
//             StopAllCoroutines();          // cancel this client's own pending 5s hide
//             _roundTransitionRunning = false;
//         }
//         HideResultPanelImmediate();       // instant hide (no tween)
//     }

//     void EnsurePlayerResults()
//     {
//         if (playerResults == null || playerResults.Length < 4)
//             playerResults = new PlayerResult[4];

//         for (int i = 0; i < 4; i++)
//         {
//             if (playerResults[i] == null)
//                 playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };
//         }
//     }

//     [ContextMenu("Show Test Result")]
//     public void ShowResult()
//     {
//         ShowResultInternal(autoTransition: false, matchOver: false);
//     }

//     void ShowResultInternal(bool autoTransition, bool matchOver)
//     {
//         if (_isShowingResult)
//         {
//             Debug.LogWarning("[Result] ShowResult ignored — already showing.");
//             return;
//         }
//         if (!ResolveResultPanel())
//         {
//             Debug.LogError("[Result] ShowResult aborted — result panel reference missing.");
//             return;
//         }

//         _isShowingResult = true;
//         _resultActionTaken = false;
//         _autoTransitionMode = autoTransition;
//         Debug.Log(autoTransition
//             ? $"[Result] Round {currentRound} leaderboard (matchOver={matchOver})"
//             : "Result Panel Opening");
//         EnsurePlayerResults();
//         EnsurePanelHierarchyActive();
//         EnsureDimOverlay();

//         if (!autoTransition)
//         {
//             RefreshPlayerNamesAndActors();
//             CalculateScores();
//             AssignRanks();
//             RecordMatchStats();
//         }

//         PlayerResult winner = playerResults.OrderBy(p => p.rank).FirstOrDefault();
//         if (winner != null)
//             Debug.Log($"Winner Determined: {winner.name} (Rank #{winner.rank}, Score {winner.score})");

//         BuildResultPanelUI();
//         SetActionButtonsVisible(!autoTransition);
//         StartCoroutine(ScrollLeaderboardToBottom());

//         resultPanel.gameObject.SetActive(true);
//         ShowLeaderboardBanner();
//         ResetPanelOpenStateInstant();
//         Debug.Log("Result Panel Opened");

//         CreateBannerAd();
//         HideMatchFinishedLabel();
//     }

//     /// <summary>No open animation — panel and MainFrame stay at full scene-authored size/scale.</summary>
//     void ResetPanelOpenStateInstant()
//     {
//         if (resultPanel == null) return;

//         resultPanel.DOKill(complete: true);
//         resultPanel.alpha = 1f;
//         resultPanel.interactable = true;
//         resultPanel.blocksRaycasts = true;

//         Transform root = resultPanel.transform;
//         root.DOKill(complete: true);
//         root.localScale = Vector3.one;

//         if (_dimOverlay != null)
//         {
//             _dimOverlay.DOKill(complete: true);
//             Color c = _dimOverlay.color;
//             c.a = 0.55f;
//             _dimOverlay.color = c;
//         }

//         Transform frame = root.Find("MainFrame");
//         if (frame != null)
//         {
//             frame.DOKill(complete: true);
//             frame.localScale = Vector3.one;
//             frame.SetAsLastSibling();

//             // Task 15: lift the board fully above the bottom banner ad (no shrinking / no
//             // offsetMin / sizeDelta hacks). The board bottom must clear the banner band
//             // (BannerAdHeightPx) plus a small safe margin so the two never overlap.
//             var frt = frame as RectTransform;
//             if (frt != null)
//             {
//                 Vector2 pos = frt.anchoredPosition;
//                 float minY = BannerAdHeightPx + BannerAdSafeMarginPx; // ~134px above center baseline
//                 if (pos.y < minY) pos.y = minY;
//                 frt.anchoredPosition = pos;
//             }
//         }
//     }

//     IEnumerator ScrollLeaderboardToBottom()
//     {
//         yield return null;
//         yield return null;
//         Canvas.ForceUpdateCanvases();
//         if (_roundScrollRect != null)
//             _roundScrollRect.verticalNormalizedPosition = 0f;
//     }

//     /// <summary>
//     /// Recursive by-name lookup. PlayerRowsContainer now lives under a ScrollRect viewport
//     /// (RoundScrollView/Viewport/PlayerRowsContainer), so the old direct <c>Transform.Find</c>
//     /// (children-only) no longer resolves it. This walks the whole subtree.
//     /// </summary>
//     static Transform FindDeepChild(Transform root, string childName)
//     {
//         if (root == null) return null;
//         Transform direct = root.Find(childName);
//         if (direct != null) return direct;
//         foreach (Transform child in root)
//         {
//             Transform found = FindDeepChild(child, childName);
//             if (found != null) return found;
//         }
//         return null;
//     }

//     void SetActionButtonsVisible(bool visible)
//     {
//         if (homeButton != null) homeButton.gameObject.SetActive(visible);
//         if (restartButton != null) restartButton.gameObject.SetActive(visible);

//         Transform mainFrame = resultPanel != null ? resultPanel.transform.Find("MainFrame") : null;
//         if (mainFrame != null)
//         {
//             Transform btnContainer = mainFrame.Find("ButtonsContainer");
//             if (btnContainer != null) btnContainer.gameObject.SetActive(visible);

//             // Close button HAMESHA visible — player leaderboard kabhi bhi band kar sake.
//             Transform closeBtn = mainFrame.Find("CloseButton");
//             if (closeBtn != null) closeBtn.gameObject.SetActive(true);
//         }
//     }

//     void RefreshPlayerNamesAndActors()
//     {
//         for (int seat = 0; seat < 4; seat++)
//         {
//             playerResults[seat].name = GetSeatDisplayName(seat);
//             playerResults[seat].actorNumber = GetActorNumberBySeat(seat);
//         }
//     }

//     int GetActorNumberBySeat(int seatIndex)
//     {
//         if (PlayerHand.LocalInstance == null) return seatIndex; 
        
//         // tableTurnOrder is indexed by visual seat (0-3).
//         var field = typeof(PlayerHand).GetField("tableTurnOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
//         if (field == null) return -1;
        
//         var turnOrder = (List<int>)field.GetValue(PlayerHand.LocalInstance);
//         if (turnOrder != null && seatIndex < turnOrder.Count)
//             return turnOrder[seatIndex];
            
//         return -1;
//     }

//     string GetSeatDisplayName(int seatIndex)
//     {
//         if (seatIndex == 0)
//             return PlayerProfileSync.GetLocalProfileDisplayName();

//         if (PlayerProfileSync.Instance != null)
//         {
//             switch (seatIndex)
//             {
//                 case 1 when PlayerProfileSync.Instance.txtLeftName != null:
//                     return CleanName(PlayerProfileSync.Instance.txtLeftName.text);
//                 case 2 when PlayerProfileSync.Instance.txtTopName != null:
//                     return CleanName(PlayerProfileSync.Instance.txtTopName.text);
//                 case 3 when PlayerProfileSync.Instance.txtRightName != null:
//                     return CleanName(PlayerProfileSync.Instance.txtRightName.text);
//             }
//         }
//         return "Player " + (seatIndex + 1);
//     }

//     static string CleanName(string raw)
//     {
//         if (string.IsNullOrEmpty(raw)) return "Player";
//         return raw.Split('\n')[0].Trim();
//     }

//     void FinalizeCurrentRoundScores()
//     {
//         var result = new RoundResult { roundNumber = currentRound };
//         for (int seat = 0; seat < 4; seat++)
//         {
//             result.dehlasPerSeat[seat] = playerResults[seat].dehlasCollected;
//             result.tricksPerSeat[seat] = playerResults[seat].tricksWon;
//         }

//         if (roundHistory.Count > 0 && roundHistory[roundHistory.Count - 1].roundNumber == currentRound)
//             roundHistory[roundHistory.Count - 1] = result;
//         else
//             roundHistory.Add(result);

//         Debug.Log($"[Result] Round R{currentRound} finalized: " +
//                   string.Join(", ", Enumerable.Range(0, 4).Select(i => $"{playerResults[i].name}={result.dehlasPerSeat[i]}")));
//     }

//     void ResetRoundPlayerStats()
//     {
//         for (int i = 0; i < 4; i++)
//         {
//             playerResults[i].tricksWon = 0;
//             playerResults[i].dehlasCollected = 0;
//             playerResults[i].score = 0;
//             playerResults[i].isCompleted = false;
//             playerResults[i].rank = 0;
//         }

//         if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
//         {
//             PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable
//             {
//                 { "SW", new int[4] },
//                 { "DL", new int[4] },
//                 { "TP", 0 }
//             });
//         }
//     }

//     void AssignMatchRanksFromHistory()
//     {
//         int[] totals = new int[4];
//         foreach (RoundResult round in roundHistory)
//             for (int i = 0; i < 4; i++)
//                 totals[i] += round.dehlasPerSeat[i];

//         for (int i = 0; i < 4; i++)
//         {
//             playerResults[i].dehlasCollected = totals[i];
//             playerResults[i].score = totals[i];
//         }

//         // Task 6: friends 2v2 — final standings are by TEAM (combined dehlas), not 4 individuals.
//         if (IsFriendsTeamMode())
//         {
//             int teamA = totals[0] + totals[2];
//             int teamB = totals[1] + totals[3];
//             playerResults[0].score = playerResults[2].score = teamA;
//             playerResults[1].score = playerResults[3].score = teamB;
//             AssignTeamRanks();
//             return;
//         }

//         var ranked = totals.Select((value, index) => (value, index)).OrderByDescending(x => x.value).ToList();
//         for (int r = 0; r < ranked.Count; r++)
//             playerResults[ranked[r].index].rank = r + 1;
//     }

//     static int GetKotThreshold()
//     {
//         return TaashRules.IsTwoTaashMode ? KotDehlasTwoTaash : KotDehlasOneTaash;
//     }

//     static string FormatDehlaScore(int dehlas)
//     {
//         int kotThreshold = GetKotThreshold();
//         return dehlas == kotThreshold ? $"{dehlas} (KOT)" : dehlas.ToString();
//     }

//     static int SumRound(int[] roundScores)
//     {
//         int total = 0;
//         for (int i = 0; i < roundScores.Length; i++)
//             total += roundScores[i];
//         return total;
//     }

//     void CalculateScores()
//     {
//         // Task 28: a player's leaderboard score is their CUMULATIVE Dehlas captured across every
//         // round played so far (Dehla Pakad's actual scoring metric) — NOT a per-round trick/dehla
//         // blend. This makes the inter-round ranks reflect the true running standings and stay
//         // consistent with the match-end ranking (AssignMatchRanksFromHistory), which also sums
//         // dehlas per seat. Tricks won are retained only as a deterministic tiebreak in
//         // CompareForLeaderboard. The current (not-yet-finalized) round is added from the live
//         // playerResults, while previous rounds come from roundHistory.
//         for (int seat = 0; seat < playerResults.Length; seat++)
//         {
//             PlayerResult p = playerResults[seat];
//             if (p == null) continue;

//             int cumulativeDehlas = p.dehlasCollected; // current round (not yet in roundHistory)
//             foreach (RoundResult rr in roundHistory)
//             {
//                 // Skip the current round if it has already been finalized into history, so it is
//                 // never double-counted (e.g. when the panel is re-shown).
//                 if (rr == null || rr.roundNumber == currentRound) continue;
//                 if (rr.dehlasPerSeat != null && seat < rr.dehlasPerSeat.Length)
//                     cumulativeDehlas += rr.dehlasPerSeat[seat];
//             }

//             p.score = cumulativeDehlas;
//             p.isCompleted = true;
//         }

//         // Task 6: in FRIENDS 2v2 each partnership shares a single score (combined dehlas), so the
//         // leaderboard ranks/wins by TEAM instead of 4 individuals. 1v1v1v1 (bots/online) is untouched.
//         if (IsFriendsTeamMode())
//             ApplyTeamScores();
//     }

//     /// <summary>
//     /// Task 6 — Friends 2v2 team format. Active ONLY in a private friends room running logic mode 2
//     /// (LogicB / 2v2, synced via room property "LM"). Bots/Online (1v1v1v1, logic mode 1) return
//     /// false so their per-individual scoring is left completely unchanged.
//     /// </summary>
//     bool IsFriendsTeamMode()
//     {
//         if (!DeckManager.IsPrivateFriendsRoom()) return false;
//         return GetLogicMode() == 2;
//     }

//     /// <summary>Reads the active logic mode (1 = 1v1v1v1, 2 = 2v2). Prefers the synced room prop "LM".</summary>
//     static int GetLogicMode()
//     {
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null &&
//             PhotonNetwork.CurrentRoom.CustomProperties != null &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("LM", out object lm) && lm is int li)
//             return li;
//         if (ModeManager.Instance != null) return ModeManager.Instance.currentLogicMode;
//         return 1;
//     }

//     /// <summary>
//     /// Task 6 — Partners sit ACROSS the table: visual seats {0,2} = Team A, seats {1,3} = Team B
//     /// (seat 0 is always the local player). Each member's leaderboard SCORE becomes their team's
//     /// combined dehlas so ranking groups partners together and produces a team win. Individual
//     /// dehlasCollected / tricksWon are intentionally left intact for per-seat display and tiebreaks.
//     /// </summary>
//     void ApplyTeamScores()
//     {
//         float teamA = playerResults[0].score + playerResults[2].score;
//         float teamB = playerResults[1].score + playerResults[3].score;
//         playerResults[0].score = playerResults[2].score = teamA;
//         playerResults[1].score = playerResults[3].score = teamB;
//     }

//     /// <summary>
//     /// Task 6 — Assigns a TEAM rank: both members of the higher-scoring partnership get rank 1, the
//     /// other pair rank 2. Assumes <see cref="ApplyTeamScores"/> has already written team totals into
//     /// each member's score. Tie-break is client-independent (lowest actorNumber in the team wins) so
//     /// every client agrees on the winning team.
//     /// </summary>
//     void AssignTeamRanks()
//     {
//         float teamA = playerResults[0].score; // == playerResults[2].score
//         float teamB = playerResults[1].score; // == playerResults[3].score

//         int rankA;
//         if (teamA != teamB)
//         {
//             rankA = teamA > teamB ? 1 : 2;
//         }
//         else
//         {
//             int minActorA = Mathf.Min(playerResults[0].actorNumber, playerResults[2].actorNumber);
//             int minActorB = Mathf.Min(playerResults[1].actorNumber, playerResults[3].actorNumber);
//             rankA = minActorA <= minActorB ? 1 : 2;
//         }
//         int rankB = rankA == 1 ? 2 : 1;

//         playerResults[0].rank = playerResults[2].rank = rankA;
//         playerResults[1].rank = playerResults[3].rank = rankB;
//     }

//     void AssignRanks()
//     {
//         EnsurePlayerResults();

//         // Task 6: friends 2v2 ranks by team (rank 1/1, 2/2). Bots/online keep per-individual ranks.
//         if (IsFriendsTeamMode())
//         {
//             AssignTeamRanks();
//             return;
//         }

//         UpdateAndSortLeaderboard(new List<PlayerResult>(playerResults));
//     }

//     /// <summary>
//     /// Task 28 — Robust leaderboard ranking. Sorts players strictly by Score (descending), then
//     /// breaks ties deterministically: more Dehlas (KOT cards) first, then more Tricks won, then a
//     /// stable fallback on actorNumber so the order never flickers between equal players. Null-safe:
//     /// silently skips players that disconnected/were removed right before the leaderboard is built,
//     /// so a missing player can never break the round-end game loop. Ranks are written back onto the
//     /// surviving PlayerResult objects (rank 1 = winner).
//     /// </summary>
//     public void UpdateAndSortLeaderboard(List<PlayerResult> currentPlayers)
//     {
//         if (currentPlayers == null) return;

//         // Drop any null entries (a player object can be missing if they left right at round end).
//         List<PlayerResult> valid = currentPlayers.Where(p => p != null).ToList();
//         if (valid.Count == 0) return;

//         valid.Sort(CompareForLeaderboard);

//         for (int i = 0; i < valid.Count; i++)
//             valid[i].rank = i + 1;
//     }

//     /// <summary>
//     /// Leaderboard comparator: Score desc -> Dehlas (KOT) desc -> Tricks desc -> actorNumber asc.
//     /// Returns negative when <paramref name="a"/> should rank ABOVE <paramref name="b"/>.
//     /// </summary>
//     static int CompareForLeaderboard(PlayerResult a, PlayerResult b)
//     {
//         if (a == null && b == null) return 0;
//         if (a == null) return 1;   // nulls sink to the bottom
//         if (b == null) return -1;

//         int byScore = b.score.CompareTo(a.score);            // higher score first
//         if (byScore != 0) return byScore;

//         int byDehlas = b.dehlasCollected.CompareTo(a.dehlasCollected); // KOT/Dehla tiebreak
//         if (byDehlas != 0) return byDehlas;

//         int byTricks = b.tricksWon.CompareTo(a.tricksWon);   // tricks (wins) tiebreak
//         if (byTricks != 0) return byTricks;

//         return a.actorNumber.CompareTo(b.actorNumber);       // stable, deterministic fallback
//     }

//     void BuildResultPanelUI()
//     {
//         // The Panel_Winning leaderboard skeleton (header + avatars + name plates + R1..R5 rows +
//         // dividers) lives in the scene hierarchy and is fully editable. At runtime we only ensure it
//         // exists, then fill score data into it — we never recreate the header/avatars/name plates so
//         // the authored layout, fonts and text stay exactly as set in the Editor.
//         ClearDynamicUI();

//         if (resultPanel == null) return;

//         Transform mainFrame = resultPanel.transform.Find("MainFrame");
//         if (mainFrame == null)
//         {
//             Debug.LogError("[Result] MainFrame not found under Panel_Winning. Cannot fill result UI.");
//             return;
//         }

//         mainFrame.localScale = Vector3.one;

//         // Task 14: reduce the board's rounded-corner radius (9-sliced sprite + higher PPU shrinks the corners).
//         var mainFrameImg = mainFrame.GetComponent<Image>();
//         if (mainFrameImg != null)
//         {
//             mainFrameImg.type = Image.Type.Sliced;
//             mainFrameImg.pixelsPerUnitMultiplier = 1.8f;
//         }

//         Transform rowsContainer = scoreboardContainer != null
//             ? scoreboardContainer
//             : FindDeepChild(mainFrame, "PlayerRowsContainer");

//         // PlayFriends runs unlimited rounds. The round rows live in a ScrollRect (RoundScrollView)
//         // added to the scene so the list can scroll instead of growing off-screen. Resolve it once
//         // so ScrollLeaderboardToBottom() can pin the newest ~5 rounds into view by default.
//         if (_roundScrollRect == null)
//             _roundScrollRect = mainFrame.GetComponentInChildren<ScrollRect>(true);

//         if (rowsContainer != null)
//         {
//             EnsureStaticLeaderboard(rowsContainer);
//             RefreshLeaderboardHeader(rowsContainer);
//             UpdateLeaderboardUI(rowsContainer);
//         }
//         else
//         {
//             Debug.LogWarning("[Result] PlayerRowsContainer not found under MainFrame — scores not filled.");
//         }

//         // Optional round-progress title, only if the user wired a titleText field.
//         string title = maxRounds == -1
//             ? $"Round {currentRound} Complete"
//             : $"Round {currentRound} / {maxRounds}";
//         if (titleText != null) titleText.text = title;

//         // Wire the existing CloseButton from the hierarchy (do not create/restyle it).
//         Transform closeT = mainFrame.Find("CloseButton");
//         if (closeT != null)
//         {
//             var closeBtn = closeT.GetComponent<Button>();
//             if (closeBtn != null)
//             {
//                 EnableButtonVisuals(closeBtn);
//                 closeBtn.onClick.RemoveAllListeners();
//                 closeBtn.onClick.AddListener(CloseResult);
//             }
//         }

//         EnsureSceneButtons(mainFrame);
//     }

//     // Decorative (non-animated) leaderboard elements, cleared together with the rows.
//     private readonly List<GameObject> _dynamicDecor = new List<GameObject>();
//     // Rounded button sprite reused for the name plate, pulled from the existing hand-made buttons.
//     private Sprite _roundedSprite;

//     static readonly Color LeaderLabelColor = new Color(1f, 0.92f, 0.5f, 1f);  // gold ROUNDS / TOTAL headers
//     static readonly Color NameBoxColor = new Color(0.36f, 0.20f, 0.10f, 1f);  // brown name plate

//     /// <summary>Number of round rows materialised as persistent, editable scene objects.</summary>
//     const int StaticLeaderboardRows = 5;

//     /// <summary>Builds the editable leaderboard skeleton once if it isn't already in the hierarchy.</summary>
//     void EnsureStaticLeaderboard(Transform container)
//     {
//         if (container == null) return;
//         if (container.Find("HeaderRow") == null)
//             BuildStaticLeaderboard(container, StaticLeaderboardRows);

//         // Additive, non-destructive: older scenes that already have a HeaderRow but no persistent
//         // TOTAL row (it used to be runtime-only) get the editable TotalsRow created once here.
//         if (container.Find("TotalsRow") == null)
//             BuildEditableTotalsRow(container, ComputeInnerWidth(container), 64f);
//     }

//     /// <summary>
//     /// Creates the persistent, hand-editable "TotalsRow" GameObject (6 TMP cells: TOTAL + 4 player
//     /// totals + grand total). Default styling is applied ONCE on creation so it looks correct out of
//     /// the box; afterwards the runtime fill never restyles it, so your Inspector font/color edits show
//     /// in game exactly as authored.
//     /// </summary>
//     void BuildEditableTotalsRow(Transform container, float innerW, float rowH)
//     {
//         if (container == null || container.Find("TotalsRow") != null) return;

//         string[] totalCells = new string[6];
//         totalCells[0] = "TOTAL";
//         for (int s = 1; s < 6; s++) totalCells[s] = "0";

//         GameObject rowGo = CreateScoreRow("TotalsRow", container, totalCells, innerW, rowH, ScoreDarkColor, true);
//         rowGo.transform.SetAsLastSibling();
//     }

//     /// <summary>
//     /// PUBLIC helper to materialise the editable TOTAL row into the scene at edit time without
//     /// rebuilding the rest of the skeleton (non-destructive). Resolves the panel/container itself,
//     /// so it can be invoked standalone (e.g. from a one-off editor command). Returns true if the row
//     /// now exists.
//     /// </summary>
//     public bool EnsureEditableTotalRow()
//     {
//         if (!ResolveResultPanel() || resultPanel == null)
//         {
//             Debug.LogError("[Result] EnsureEditableTotalRow — Panel_Winning / resultPanel could not be resolved.");
//             return false;
//         }

//         Transform mainFrame = resultPanel.transform.Find("MainFrame");
//         if (mainFrame == null) { Debug.LogError("[Result] EnsureEditableTotalRow — MainFrame missing."); return false; }

//         Transform container = scoreboardContainer != null ? scoreboardContainer : FindDeepChild(mainFrame, "PlayerRowsContainer");
//         if (container == null) { Debug.LogError("[Result] EnsureEditableTotalRow — PlayerRowsContainer missing."); return false; }

//         BuildEditableTotalsRow(container, ComputeInnerWidth(container), 64f);
//         return container.Find("TotalsRow") != null;
//     }

//     /// <summary>
//     /// Builds the persistent leaderboard skeleton: a header row (ROUNDS + four avatar/name columns +
//     /// TOTAL), <paramref name="rowCount"/> round rows (R1..Rn) and the two vertical dividers. These
//     /// objects are NOT tracked as dynamic, so they survive between shows and can be hand-edited in the
//     /// scene (positions, fonts, text, avatars, name plates). Runtime only fills the score cells.
//     /// </summary>
//     public void BuildStaticLeaderboard(Transform container, int rowCount)
//     {
//         if (container == null) return;
//         ResolveThemeSprites();

//         float innerW = ComputeInnerWidth(container);
//         const float headerH = 120f;
//         const float rowH = 64f;

//         // Header: ROUNDS | avatar x4 (fixed) | TOTAL
//         CreateHeaderRow("HeaderRow", container, innerW, headerH);

//         // One editable row per round slot (blank until scores are filled at runtime).
//         for (int r = 1; r <= rowCount; r++)
//         {
//             string[] cells = new string[6];
//             cells[0] = "R" + r;
//             for (int s = 1; s < 6; s++) cells[s] = "";
//             CreateScoreRow("RoundRow_" + r, container, cells, innerW, rowH, TextWhiteColor, false);
//         }

//         // Persistent, EDITABLE "TOTAL" row. It now lives in the scene skeleton (exactly like the
//         // RoundRow_N rows) so its font / color / size can be hand-edited in the Inspector and will
//         // SURVIVE at runtime. The runtime fill (BuildOrUpdateTotalsRow) only writes the number
//         // values into this authored row — it never restyles it.
//         BuildEditableTotalsRow(container, innerW, rowH);

//         // Faint vertical dividers behind the table: after ROUNDS and before TOTAL.
//         BuildVerticalDividers(container, innerW);
//     }

//     float ComputeInnerWidth(Transform container)
//     {
//         var crt = container as RectTransform;
//         var vlg = container.GetComponent<VerticalLayoutGroup>();
//         float innerW = 1040f;
//         if (crt != null && crt.rect.width > 1f)
//         {
//             innerW = crt.rect.width;
//             if (vlg != null) innerW -= (vlg.padding.left + vlg.padding.right);
//         }
//         return innerW;
//     }

//     /// <summary>
//     /// Fills score values into the existing static round rows. Played rounds are filled, future rounds
//     /// stay blank (matching the mock-up). Extra rows are only created at runtime when a Friends match
//     /// runs past the static row count. The header row (avatars + names) is left untouched so it stays
//     /// exactly as authored in the scene.
//     /// </summary>
//     /// <summary>Fills round rows and the TOTAL row. Total-row formatting is locked to match normal rows.</summary>
//     public void UpdateLeaderboardUI(Transform container)
//     {
//         FillLeaderboardData(container);
//     }

//     void FillLeaderboardData(Transform container)
//     {
//         _dynamicRows.Clear();

//         // Task 6: FRIENDS 2v2 shows a 4-column board (ROUNDS | Team1 | Team2 | TOTAL). Each team
//         // header stacks its two partners' names (P1 over P3, P2 over P4) and every round/total value
//         // is that partnership's COMBINED dehlas. Bots/Online (1v1v1v1) keep the authored 6-column
//         // board untouched. The authored scene rows are simply hidden in friends mode and restored
//         // otherwise, so nothing in the scene is destroyed.
//         if (IsFriendsTeamMode())
//         {
//             SetAuthoredLeaderboardRowsActive(container, false);
//             BuildFriendsTeamLeaderboard(container);
//         }
//         else
//         {
//             SetAuthoredLeaderboardRowsActive(container, true);
//             FillIndividualLeaderboardRows(container);
//         }

//         // Authored column positions turant lock karo (HLG disable + ignoreLayout) taaki cells
//         // pehle frame mein hi apni jagah par baithein.
//         ApplyAllLeaderboardPositions(container);

//         // Unity's nested layout groups do not always solve in the same frame the panel is shown
//         // (rects are still zero-sized when the cells are created). Defer one frame, flush the canvas,
//         // then force an immediate rebuild so every column resolves to its share and lines up with the
//         // vertical dividers.
//         if (container is RectTransform containerRect)
//             StartCoroutine(RebuildLeaderboardLayout(containerRect));
//     }

//     /// <summary>
//     /// The original 1v1v1v1 fill: ROUNDS + four player columns + TOTAL, using the authored scene
//     /// rows (R1..Rn + TotalsRow). Overflow round rows are only created if a match runs past the
//     /// static row count.
//     /// </summary>
//     void FillIndividualLeaderboardRows(Transform container)
//     {
//         Transform header = container.Find("HeaderRow");
//         if (header != null) _dynamicRows.Add(header.gameObject);

//         int slots = maxRounds > 0 ? maxRounds : Mathf.Max(roundHistory.Count, 1);
//         int total = Mathf.Max(slots, StaticLeaderboardRows);

//         for (int r = 1; r <= total; r++)
//         {
//             Transform rowT = container.Find("RoundRow_" + r);
//             GameObject rowGo;
//             if (rowT != null)
//             {
//                 rowGo = rowT.gameObject;
//             }
//             else
//             {
//                 // Overflow round row (beyond the static rows) — created at runtime, cleared each show.
//                 string[] blank = new string[6];
//                 blank[0] = "R" + r;
//                 for (int s = 1; s < 6; s++) blank[s] = "";
//                 rowGo = CreateScoreRow("RoundRow_" + r, container, blank, ComputeInnerWidth(container), 64f, TextWhiteColor, false);
//                 _overflowRows.Add(rowGo);
//             }

//             rowGo.SetActive(true);
//             rowGo.transform.localScale = Vector3.one;
//             var rowCg = rowGo.GetComponent<CanvasGroup>();
//             if (rowCg != null) rowCg.alpha = 1f;
//             FillRowCells(rowGo, r);
//             _dynamicRows.Add(rowGo);
//         }

//         // Task 28: cumulative standings row so the actual ranking is visible at a glance.
//         BuildOrUpdateTotalsRow(container);
//     }

//     /// <summary>
//     /// Enables/disables the authored 6-column scene rows (HeaderRow, RoundRow_1..n, TotalsRow) and
//     /// their two authored vertical dividers. Friends mode hides them and draws its own 4-column board;
//     /// individual mode re-enables them. Never destroys — the authored layout must persist.
//     /// </summary>
//     void SetAuthoredLeaderboardRowsActive(Transform container, bool active)
//     {
//         if (container == null) return;

//         SetChildActiveByName(container, "HeaderRow", active);
//         SetChildActiveByName(container, "TotalsRow", active);
//         for (int r = 1; r <= StaticLeaderboardRows; r++)
//             SetChildActiveByName(container, "RoundRow_" + r, active);

//         // Authored 6-column dividers only (friends dividers are named "FriendsVDivider").
//         foreach (Transform child in container)
//             if (child.name == "VDivider") child.gameObject.SetActive(active);
//     }

//     static void SetChildActiveByName(Transform parent, string childName, bool active)
//     {
//         Transform t = parent.Find(childName);
//         if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
//     }

//     /// <summary>Forces the leaderboard layout to resolve after the rows have been generated.</summary>
//     IEnumerator RebuildLeaderboardLayout(RectTransform containerTransform)
//     {
//         // Wait one frame so the container/cell RectTransforms have valid sizes.
//         yield return null;
//         if (containerTransform == null) yield break;
//         Canvas.ForceUpdateCanvases();
//         UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(containerTransform);

//         // Ab jab VerticalLayoutGroup ne row heights set kar diye, cells ko final authored
//         // X/Y par re-apply karo (row height se vertical centering sahi ho jaye).
//         ApplyAllLeaderboardPositions(containerTransform);
//     }

//     /// <summary>
//     /// Task 28: builds a bottom "TOTAL" row showing each player's cumulative dehlas across all rounds,
//     /// highlighting the current leader. Tracked as an overflow row so it is rebuilt cleanly each show.
//     /// </summary>
//     void BuildOrUpdateTotalsRow(Transform container)
//     {
//         if (container == null) return;

//         int[] totals = new int[4];
//         foreach (RoundResult rr in roundHistory)
//             for (int s = 0; s < 4 && s < rr.dehlasPerSeat.Length; s++)
//                 totals[s] += rr.dehlasPerSeat[s];
//         int grand = totals[0] + totals[1] + totals[2] + totals[3];

//         string[] cellTexts = new string[6];
//         cellTexts[0] = "TOTAL";
//         for (int s = 0; s < 4; s++) cellTexts[s + 1] = totals[s].ToString();
//         cellTexts[5] = grand.ToString();

//         // Prefer the PERSISTENT, authored "TotalsRow" from the scene skeleton so your Inspector
//         // font / color / size survive. Only fall back to a runtime-created row if (very old scene)
//         // none exists yet.
//         Transform existing = container.Find("TotalsRow");
//         bool authored = existing != null;

//         GameObject rowGo = authored
//             ? existing.gameObject
//             : CreateScoreRow("TotalsRow", container, cellTexts, ComputeInnerWidth(container), 64f, ScoreDarkColor, true);

//         // Only the runtime fallback row is tracked for destruction. The authored row must persist.
//         if (!authored)
//             _overflowRows.Add(rowGo);

//         rowGo.transform.SetAsLastSibling();
//         rowGo.SetActive(true);
//         _dynamicRows.Add(rowGo);

//         var tmps = new List<TextMeshProUGUI>();
//         foreach (Transform child in rowGo.transform)
//         {
//             var t = child.GetComponent<TextMeshProUGUI>();
//             if (t != null) tmps.Add(t);
//         }
//         if (tmps.Count < 6) return;

//         for (int s = 0; s < 6; s++)
//         {
//             TextMeshProUGUI totalRowText = tmps[s];

//             // Always write the freshly computed value (rich-text-free) into the cell.
//             totalRowText.text = StripRichTextTags(cellTexts[s]);

//             // STYLING POLICY:
//             //  - Authored row  -> DO NOT touch font / size / color / alignment. Whatever you set in
//             //                     the Inspector is exactly what shows in game (your request).
//             //  - Fallback row  -> apply safe defaults so an unstyled runtime row still looks correct.
//             if (!authored)
//             {
//                 totalRowText.fontSize = LeaderboardCellFontSize;
//                 totalRowText.color = Color.black;
//                 totalRowText.enableAutoSizing = false;
//                 totalRowText.alignment = s == 0
//                     ? TextAlignmentOptions.MidlineLeft
//                     : TextAlignmentOptions.Center;
//             }
//         }
//     }

//     /// <summary>
//     /// Task 6 — Friends 2v2 only. Builds the whole 4-column board at runtime: ROUNDS | Team1 | Team2 |
//     /// TOTAL. Team1 = seats {0,2} (Player 1 over Player 3), Team2 = seats {1,3} (Player 2 over Player
//     /// 4). Each round cell is the partnership's COMBINED dehlas for that round, the TOTAL row is the
//     /// combined dehlas across all rounds. Everything is tracked as overflow so it is destroyed and
//     /// rebuilt cleanly each show. No-op in 1v1v1v1 (bots/online).
//     /// </summary>
//     void BuildFriendsTeamLeaderboard(Transform container)
//     {
//         if (container == null) return;

//         ResolveThemeSprites();
//         float innerW = ComputeInnerWidth(container);
//         const float headerH = 120f;
//         const float rowH = 64f;

//         // Header: ROUNDS | Team1 (P1 over P3) | Team2 (P2 over P4) | TOTAL
//         GameObject headerGo = NewRow("FriendsHeaderRow", container, innerW, headerH);
//         AddSideHeaderLabel(headerGo.transform, "ROUNDS", true, LeaderLabelColor, 30, FontStyles.Bold);
//         CreateFriendsTeamHeaderCell(headerGo.transform, 0, 2); // Team 1
//         CreateFriendsTeamHeaderCell(headerGo.transform, 1, 3); // Team 2
//         AddCellLabel(headerGo.transform, "TOTAL", LeaderLabelColor, 30, FontStyles.Bold);
//         AddRowDashedLine(headerGo, innerW, headerH);
//         RegisterFriendsRow(headerGo);

//         // Per-round combined team scores.
//         int slots = maxRounds > 0 ? maxRounds : Mathf.Max(roundHistory.Count, 1);
//         int totalRows = Mathf.Max(slots, StaticLeaderboardRows);
//         int grandA = 0, grandB = 0;

//         for (int r = 1; r <= totalRows; r++)
//         {
//             RoundResult round = roundHistory.Find(rr => rr.roundNumber == r);
//             string aVal = "", bVal = "", rowTotal = "";
//             if (round != null && round.dehlasPerSeat != null && round.dehlasPerSeat.Length >= 4)
//             {
//                 int a = round.dehlasPerSeat[0] + round.dehlasPerSeat[2];
//                 int b = round.dehlasPerSeat[1] + round.dehlasPerSeat[3];
//                 aVal = a.ToString();
//                 bVal = b.ToString();
//                 rowTotal = (a + b).ToString();
//                 grandA += a;
//                 grandB += b;
//             }

//             string[] cells = { "R" + r, aVal, bVal, rowTotal };
//             GameObject rowGo = CreateScoreRow("FriendsRoundRow_" + r, container, cells, innerW, rowH, Color.black, true);
//             StyleFriendsRowCells(rowGo, Color.black);
//             RegisterFriendsRow(rowGo);
//         }

//         // Combined TOTAL row.
//         string[] totalCells = { "TOTAL", grandA.ToString(), grandB.ToString(), (grandA + grandB).ToString() };
//         GameObject totalsGo = CreateScoreRow("FriendsTotalsRow", container, totalCells, innerW, rowH, ScoreDarkColor, true);
//         StyleFriendsRowCells(totalsGo, ScoreDarkColor);
//         RegisterFriendsRow(totalsGo);

//         // Two dividers matching the 4-column grid: after ROUNDS and before TOTAL.
//         BuildFriendsDividers(container, innerW);
//     }

//     /// <summary>One team header cell with two stacked player plates: top seat (e.g. Player 1) above,
//     /// bottom seat (e.g. Player 3) slightly below — each an avatar beside a brown name plate.</summary>
//     void CreateFriendsTeamHeaderCell(Transform rowParent, int topSeat, int bottomSeat)
//     {
//         var cell = new GameObject("FriendsTeamHeaderCell", typeof(RectTransform));
//         cell.transform.SetParent(rowParent, false);
//         MakeEqualColumn(cell);

//         CreateStackedNamePlate(cell.transform, topSeat, 28f);     // upper player
//         CreateStackedNamePlate(cell.transform, bottomSeat, -28f); // lower player
//     }

//     /// <summary>A small avatar + brown name plate mini-row at vertical offset <paramref name="y"/>.</summary>
//     void CreateStackedNamePlate(Transform cellParent, int seatIndex, float y)
//     {
//         var group = CreateRect("PlayerPlate", cellParent, new Vector2(0f, y), new Vector2(196f, 44f));

//         var avatarGo = CreateRect("AvatarImage", group.transform, new Vector2(-80f, 0f), new Vector2(40f, 40f));
//         var avatarImg = AddImage(avatarGo, Color.white);
//         avatarImg.preserveAspect = true;
//         avatarImg.raycastTarget = false;
//         Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
//         if (avatar != null) avatarImg.sprite = avatar;
//         else if (playerAvatarSprite != null) avatarImg.sprite = playerAvatarSprite;

//         var nameBox = CreateRect("NameBox", group.transform, new Vector2(22f, 0f), new Vector2(140f, 36f));
//         var nameImg = AddImage(nameBox, NameBoxColor);
//         if (_roundedSprite != null) { nameImg.sprite = _roundedSprite; nameImg.type = UnityEngine.UI.Image.Type.Sliced; }
//         nameImg.raycastTarget = false;

//         var nameTxt = AddTmp(nameBox.transform, GetSeatDisplayName(seatIndex), Color.white, 16, TextAlignmentOptions.Center, FontStyles.Bold);
//         nameTxt.rectTransform.anchorMin = Vector2.zero;
//         nameTxt.rectTransform.anchorMax = Vector2.one;
//         nameTxt.rectTransform.offsetMin = new Vector2(6, 2);
//         nameTxt.rectTransform.offsetMax = new Vector2(-6, -2);
//         nameTxt.overflowMode = TextOverflowModes.Ellipsis;
//         nameTxt.enableAutoSizing = true;
//         nameTxt.fontSizeMin = 9;
//         nameTxt.fontSizeMax = 16;
//     }

//     /// <summary>Locks every text cell in a runtime friends row to the shared cell style + a color.</summary>
//     void StyleFriendsRowCells(GameObject rowGo, Color color)
//     {
//         if (rowGo == null) return;
//         int i = 0;
//         foreach (Transform child in rowGo.transform)
//         {
//             var tmp = child.GetComponent<TextMeshProUGUI>();
//             if (tmp == null) continue;
//             ApplyCellStyle(tmp, i == 0); // index 0 = the ROUNDS/R#/TOTAL label (left aligned)
//             tmp.color = color;
//             i++;
//         }
//     }

//     /// <summary>Registers a runtime friends row for destruction-each-show and the reveal animation.</summary>
//     void RegisterFriendsRow(GameObject rowGo)
//     {
//         if (rowGo == null) return;
//         _overflowRows.Add(rowGo);
//         rowGo.transform.SetAsLastSibling();
//         rowGo.SetActive(true);
//         rowGo.transform.localScale = Vector3.one;
//         var cg = rowGo.GetComponent<CanvasGroup>();
//         if (cg != null) cg.alpha = 1f;
//         _dynamicRows.Add(rowGo);
//     }

//     /// <summary>Two faint vertical dividers for the friends 4-column grid: after ROUNDS, before TOTAL.</summary>
//     void BuildFriendsDividers(Transform container, float innerW)
//     {
//         var crt = container as RectTransform;
//         float h = (crt != null && crt.rect.height > 1f) ? crt.rect.height - 40f : 540f;
//         float col = innerW / 4f;
//         AddFriendsVerticalDivider(container, -innerW / 2f + col, h);        // after ROUNDS
//         AddFriendsVerticalDivider(container, -innerW / 2f + col * 3f, h);   // before TOTAL
//     }

//     void AddFriendsVerticalDivider(Transform container, float x, float height)
//     {
//         var go = CreateRect("FriendsVDivider", container, new Vector2(x, 0f), new Vector2(3f, height));
//         var le = go.AddComponent<LayoutElement>();
//         le.ignoreLayout = true;
//         var img = AddImage(go, new Color(0.12f, 0.06f, 0.02f, 0.45f));
//         img.raycastTarget = false;
//         go.transform.SetAsFirstSibling();
//         _overflowRows.Add(go); // destroyed/rebuilt each show like the friends rows
//     }

//     static string StripRichTextTags(string value)
//     {
//         if (string.IsNullOrEmpty(value)) return value;
//         return System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty);
//     }

//     /// <summary>Writes the round number, per-player dehla scores and the row total into a row's text cells.</summary>
//     void FillRowCells(GameObject rowGo, int roundNumber)
//     {
//         var cells = new List<TextMeshProUGUI>();
//         foreach (Transform child in rowGo.transform)
//         {
//             var tmp = child.GetComponent<TextMeshProUGUI>();
//             if (tmp != null) cells.Add(tmp);
//         }
//         if (cells.Count < 6) return;

//         RoundResult round = roundHistory.Find(rr => rr.roundNumber == roundNumber);
//         cells[0].text = "R" + roundNumber;
//         if (round != null)
//         {
//             for (int s = 0; s < 4; s++) cells[s + 1].text = FormatDehlaScore(round.dehlasPerSeat[s]);
//             cells[5].text = SumRound(round.dehlasPerSeat).ToString();
//         }
//         else
//         {
//             for (int s = 1; s < 6; s++) cells[s].text = "";
//         }

//         // EVERY round row (R1..R5), label + score values, uses pure BLACK text.
//         // No golden/green/brown per-round highlighting — every round is styled identically.
//         for (int s = 0; s < 6; s++)
//         {
//             ApplyCellStyle(cells[s], s == 0); // fixed size + Bold (shared with the TOTAL row)
//             cells[s].color = Color.black;     // black for every round score
//         }
//     }

//     /// <summary>
//     /// Large bold cells. The row label (ROUNDS / R1.. / TOTAL) is left-aligned like the header's
//     /// "ROUNDS" cell; every value cell is CENTER-aligned so each number sits exactly under its
//     /// centered player avatar (and the grand total under the centered "TOTAL" header).
//     /// </summary>
//     /// <summary>
//     /// Fixed font size shared by EVERY leaderboard cell (R1..Rn rows AND the TOTAL row) so they
//     /// render at an identical size/weight. Auto-sizing is intentionally disabled: because rows can
//     /// have different RectTransform heights, auto-sizing produced mismatched rendered sizes (the
//     /// TOTAL row looked bigger/smaller than the R1 row). Locking the size fixes that from code only.
//     /// </summary>
//     const int LeaderboardCellFontSize = 34;

//     void ApplyCellStyle(TextMeshProUGUI cell, bool isRowLabel)
//     {
//         if (cell == null) return;
//         // Lock size from code so the TOTAL row exactly matches the R1..Rn rows.
//         cell.enableAutoSizing = false;               // deterministic: no height-driven size drift
//         cell.fontSize = LeaderboardCellFontSize;
//         cell.overflowMode = TextOverflowModes.Overflow;
//         cell.fontStyle = FontStyles.Normal;          // preserve hierarchy look — never bold at runtime
//         if (isRowLabel)
//         {
//             cell.alignment = TextAlignmentOptions.MidlineLeft;
//             cell.margin = new Vector4(18f, 0f, 0f, 0f);
//         }
//         else
//         {
//             cell.alignment = TextAlignmentOptions.Center;
//             cell.margin = Vector4.zero;
//         }
//     }

//     // ============================================================
//     // AUTHORED COLUMN POSITIONS (winning panel / leaderboard)
//     //
//     // Har row par HorizontalLayoutGroup + LayoutRebuilder runtime par cells ki
//     // manual positions ko reset kar deta hai. In values ko code se lock karke
//     // (LayoutElement.ignoreLayout + HLG disable) hum ensure karte hain ke panel
//     // har mode (Bots / Online 1v1v1v1 aur Friends 2v2) mein same aligned rahe.
//     //
//     // Values user ne di thi:
//     //   HeaderRow  : ROUNDS X=40 Y=-60 | Player1..4 X=340/550/760/970 Y=-50
//     //   Data rows  : Player1..4 X=340/550/760/970 (R1..R5 label left me)
//     // TOTAL column X=1180 (players ke consistent 210px spacing se derived).
//     // ============================================================
//     const float LbRoundsColumnX = 40f;
//     const float LbTotalColumnX = 1180f;
//     static readonly float[] LbPlayerColumnX = { 340f, 550f, 760f, 970f };
//     // Friends 2v2 (4-col): ROUNDS | Team1 | Team2 | TOTAL — teams apne 2-player span ke center par.
//     static readonly float[] LbFriendsColumnX = { 40f, 445f, 865f, 1180f };
//     const float LbHeaderRoundsY = -60f;
//     const float LbHeaderPlayerY = -50f;
//     const float LbHeaderHeight = 120f;
//     const float LbDataRowHeight = 64f;

//     /// <summary>Row ke score/header cells order mein (dashed lines / dividers skip karke).</summary>
//     static List<RectTransform> GetOrderedLeaderboardCells(Transform row)
//     {
//         var list = new List<RectTransform>();
//         if (row == null) return list;
//         for (int i = 0; i < row.childCount; i++)
//         {
//             Transform c = row.GetChild(i);
//             if (c.name == "Cell" || c.name == "PlayerHeaderCell" || c.name == "FriendsTeamHeaderCell")
//                 list.Add(c as RectTransform);
//         }
//         return list;
//     }

//     /// <summary>Ek cell ko fixed anchor (top-left) par lock karta hai taaki layout group use hila na sake.</summary>
//     static void LockLeaderboardCell(RectTransform rt, float x, float y, Vector2 size, Vector2 pivot)
//     {
//         if (rt == null) return;

//         var le = rt.GetComponent<LayoutElement>();
//         if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
//         le.ignoreLayout = true;

//         rt.anchorMin = new Vector2(0f, 1f);
//         rt.anchorMax = new Vector2(0f, 1f);
//         rt.pivot = pivot;
//         rt.sizeDelta = size;
//         rt.anchoredPosition = new Vector2(x, y);
//     }

//     /// <summary>Poore container (header + saari rows) ke cells ko authored X/Y par set karta hai.</summary>
//     void ApplyAllLeaderboardPositions(Transform container)
//     {
//         if (container == null) return;

//         foreach (Transform row in container)
//         {
//             string nm = row.name;
//             if (nm == "HeaderRow" || nm == "FriendsHeaderRow")
//                 ApplyLeaderboardRowLayout(row, isHeader: true);
//             else if (nm.StartsWith("RoundRow_") || nm == "TotalsRow"
//                      || nm.StartsWith("FriendsRoundRow_") || nm == "FriendsTotalsRow")
//                 ApplyLeaderboardRowLayout(row, isHeader: false);
//         }
//     }

//     void ApplyLeaderboardRowLayout(Transform row, bool isHeader)
//     {
//         if (row == null) return;

//         // HLG band karo warna ye cells ko wapas equal-columns par kheench dega.
//         var hlg = row.GetComponent<HorizontalLayoutGroup>();
//         if (hlg != null) hlg.enabled = false;

//         List<RectTransform> cells = GetOrderedLeaderboardCells(row);
//         int n = cells.Count;
//         if (n < 2) return;

//         var rowRt = row as RectTransform;
//         float rowH = (rowRt != null && rowRt.rect.height > 1f)
//             ? rowRt.rect.height
//             : (isHeader ? LbHeaderHeight : LbDataRowHeight);

//         // Individual (6-col) vs Friends (4-col) column X list.
//         bool friends = n <= 4;

//         for (int i = 0; i < n; i++)
//         {
//             RectTransform cell = cells[i];
//             bool isRoundsCol = (i == 0);
//             bool isTotalCol = (i == n - 1);

//             float x;
//             if (friends)
//                 x = LbFriendsColumnX[Mathf.Clamp(i, 0, LbFriendsColumnX.Length - 1)];
//             else if (isRoundsCol)
//                 x = LbRoundsColumnX;
//             else if (isTotalCol)
//                 x = LbTotalColumnX;
//             else
//                 x = LbPlayerColumnX[Mathf.Clamp(i - 1, 0, LbPlayerColumnX.Length - 1)];

//             float y;
//             Vector2 pivot;
//             Vector2 size;

//             if (isHeader)
//             {
//                 if (isRoundsCol)
//                 {
//                     y = LbHeaderRoundsY;
//                     pivot = new Vector2(0f, 0.5f);      // left pivot → X = left edge
//                     size = new Vector2(260f, 80f);
//                 }
//                 else if (isTotalCol)
//                 {
//                     y = LbHeaderRoundsY;
//                     pivot = new Vector2(0.5f, 0.5f);
//                     size = new Vector2(240f, 80f);
//                 }
//                 else
//                 {
//                     y = LbHeaderPlayerY;
//                     pivot = new Vector2(0.5f, 0.5f);    // avatar+name plate cell centered on X
//                     size = new Vector2(180f, LbHeaderHeight);
//                 }
//             }
//             else
//             {
//                 y = -rowH / 2f;                         // vertically centered in the row
//                 if (isRoundsCol)
//                 {
//                     pivot = new Vector2(0f, 0.5f);
//                     size = new Vector2(220f, rowH);
//                 }
//                 else
//                 {
//                     pivot = new Vector2(0.5f, 0.5f);
//                     size = new Vector2(180f, rowH);
//                 }
//             }

//             LockLeaderboardCell(cell, x, y, size, pivot);

//             // Text alignment: R1..R5 / ROUNDS label LEFT, baaki cells center.
//             var tmp = cell.GetComponent<TextMeshProUGUI>();
//             if (tmp != null)
//             {
//                 tmp.enableWordWrapping = false;
//                 tmp.overflowMode = TextOverflowModes.Overflow;
//                 tmp.margin = Vector4.zero;
//                 tmp.alignment = isRoundsCol
//                     ? TextAlignmentOptions.MidlineLeft
//                     : TextAlignmentOptions.Center;
//             }
//         }
//     }

// #if UNITY_EDITOR
//     /// <summary>
//     /// Editor helper: clears and regenerates the static leaderboard skeleton under PlayerRowsContainer
//     /// so it can be hand-edited in the scene. Right-click the ResultManager component → this menu item.
//     /// </summary>
//     [ContextMenu("Rebuild Static Leaderboard")]
//     void RebuildStaticLeaderboardEditor()
//     {
//         if (!ResolveResultPanel() || resultPanel == null)
//         {
//             Debug.LogError("[Result] Cannot build — Panel_Winning / resultPanel could not be resolved.");
//             return;
//         }
//         Transform mainFrame = resultPanel.transform.Find("MainFrame");
//         if (mainFrame == null) { Debug.LogError("[Result] MainFrame missing under Panel_Winning."); return; }
//         Transform container = scoreboardContainer != null ? scoreboardContainer : FindDeepChild(mainFrame, "PlayerRowsContainer");
//         if (container == null) { Debug.LogError("[Result] PlayerRowsContainer missing under MainFrame."); return; }

//         for (int i = container.childCount - 1; i >= 0; i--)
//             DestroyImmediate(container.GetChild(i).gameObject);

//         BuildStaticLeaderboard(container, StaticLeaderboardRows);
//         UnityEditor.EditorUtility.SetDirty(this);
//         UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
//         Debug.Log("[Result] Static leaderboard rebuilt under PlayerRowsContainer.");
//     }
// #endif

//     /// <summary>Caches the rounded button sprite from the existing buttons so the name plate gets rounded corners in builds too.</summary>
//     void ResolveThemeSprites()
//     {
//         if (resultPanel == null || _roundedSprite != null) return;
//         var btnImg = FindImageDeep(resultPanel.transform, "HomeButton")
//                   ?? FindImageDeep(resultPanel.transform, "RestartButton")
//                   ?? FindImageDeep(resultPanel.transform, "NameBox")
//                   ?? FindImageDeep(resultPanel.transform, "CloseButton");
//         if (btnImg != null) _roundedSprite = btnImg.sprite;
//     }

//     static UnityEngine.UI.Image FindImageDeep(Transform root, string name)
//     {
//         Transform t = FindDeepByName(root, name);
//         return t != null ? t.GetComponent<UnityEngine.UI.Image>() : null;
//     }

//     static Transform FindDeepByName(Transform parent, string name)
//     {
//         if (parent.name == name) return parent;
//         for (int i = 0; i < parent.childCount; i++)
//         {
//             Transform r = FindDeepByName(parent.GetChild(i), name);
//             if (r != null) return r;
//         }
//         return null;
//     }

//     /// <summary>Header row with a "ROUNDS" label, four fixed-avatar player columns and a "TOTAL" label.</summary>
//     GameObject CreateHeaderRow(string name, Transform parent, float width, float height)
//     {
//         var rowGo = NewRow(name, parent, width, height);
//         AddSideHeaderLabel(rowGo.transform, "ROUNDS", alignLeft: true, LeaderLabelColor, 30, FontStyles.Bold);
//         for (int s = 0; s < 4; s++) CreateAvatarHeaderCell(rowGo.transform, s);
//         AddCellLabel(rowGo.transform, "TOTAL", LeaderLabelColor, 30, FontStyles.Bold);
//         AddRowDashedLine(rowGo, width, height);
//         return rowGo;
//     }

//     /// <summary>ROUNDS / TOTAL header text nudged toward the outer edge so it clears the column dividers.</summary>
//     void AddSideHeaderLabel(Transform rowParent, string text, bool alignLeft, Color color, int maxSize, FontStyles style)
//     {
//         var cellGo = new GameObject("Cell", typeof(RectTransform));
//         cellGo.transform.SetParent(rowParent, false);
//         MakeEqualColumn(cellGo);
//         var tmp = cellGo.AddComponent<TextMeshProUGUI>();
//         tmp.text = text;
//         tmp.color = color;
//         tmp.alignment = alignLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
//         tmp.fontStyle = style;
//         tmp.enableAutoSizing = true;
//         tmp.fontSizeMin = 12;
//         tmp.fontSizeMax = maxSize;
//         tmp.overflowMode = TextOverflowModes.Ellipsis;
//         tmp.raycastTarget = false;
//         if (customFont != null) tmp.font = customFont;

//         var rt = tmp.rectTransform;
//         rt.anchorMin = Vector2.zero;
//         rt.anchorMax = Vector2.one;
//         const float edgePad = 18f;
//         if (alignLeft)
//             rt.offsetMin = new Vector2(edgePad, 0);
//         else
//             rt.offsetMax = new Vector2(-edgePad, 0);
//     }

//     /// <summary>One player column: the fixed avatar portrait on top, a brown name plate beneath.</summary>
//     void CreateAvatarHeaderCell(Transform rowParent, int seatIndex)
//     {
//         var cell = new GameObject("PlayerHeaderCell", typeof(RectTransform));
//         cell.transform.SetParent(rowParent, false);

//         // Player profile avatar (same index / Photon sync as in-game seats).
//         var avatarGo = CreateRect("AvatarImage", cell.transform, new Vector2(0, 26f), new Vector2(82, 82));
//         var avatarImg = AddImage(avatarGo, Color.white);
//         avatarImg.preserveAspect = true;
//         avatarImg.raycastTarget = false;
//         Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
//         if (avatar != null)
//             avatarImg.sprite = avatar;
//         else if (playerAvatarSprite != null)
//             avatarImg.sprite = playerAvatarSprite;

//         // Brown name plate.
//         var nameBox = CreateRect("NameBox", cell.transform, new Vector2(0, -36f), new Vector2(150, 34));
//         var nameImg = AddImage(nameBox, NameBoxColor);
//         if (_roundedSprite != null) { nameImg.sprite = _roundedSprite; nameImg.type = UnityEngine.UI.Image.Type.Sliced; }
//         nameImg.raycastTarget = false;

//         var nameTxt = AddTmp(nameBox.transform, GetSeatDisplayName(seatIndex), Color.white, 18, TextAlignmentOptions.Center, FontStyles.Bold);
//         nameTxt.rectTransform.anchorMin = Vector2.zero;
//         nameTxt.rectTransform.anchorMax = Vector2.one;
//         nameTxt.rectTransform.offsetMin = new Vector2(8, 2);
//         nameTxt.rectTransform.offsetMax = new Vector2(-8, -2);
//         nameTxt.overflowMode = TextOverflowModes.Ellipsis;
//         nameTxt.enableAutoSizing = true;
//         nameTxt.fontSizeMin = 10;
//         nameTxt.fontSizeMax = 18;
//     }

//     /// <summary>A data row of evenly-distributed text cells (ROUND | values | TOTAL) with a dashed line beneath.</summary>
//     GameObject CreateScoreRow(string name, Transform parent, string[] cells, float width, float height, Color color, bool bold)
//     {
//         var rowGo = NewRow(name, parent, width, height);
//         for (int i = 0; i < cells.Length; i++)
//             AddCellLabel(rowGo.transform, cells[i], color, bold ? 30 : 28, bold ? FontStyles.Bold : FontStyles.Normal);
//         AddRowDashedLine(rowGo, width, height);
//         return rowGo;
//     }

//     /// <summary>Creates the row container: CanvasGroup (for the reveal animation) + an even 6-column HorizontalLayoutGroup.</summary>
//     GameObject NewRow(string name, Transform parent, float width, float height)
//     {
//         var rowGo = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
//         rowGo.transform.SetParent(parent, false);
//         var rrt = rowGo.GetComponent<RectTransform>();
//         rrt.sizeDelta = new Vector2(width, height);

//         var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
//         hlg.childControlWidth = true;
//         hlg.childControlHeight = true;
//         hlg.childForceExpandWidth = true;
//         hlg.childForceExpandHeight = true;
//         hlg.childAlignment = TextAnchor.MiddleCenter;

//         var le = rowGo.AddComponent<LayoutElement>();
//         le.preferredHeight = height;
//         le.minHeight = height;
//         le.preferredWidth = width;
//         return rowGo;
//     }

//     /// <summary>
//     /// Forces a HorizontalLayoutGroup child to take an equal 1/N share of the row width so every
//     /// column lines up exactly with the fixed innerW/6 vertical dividers. Without this, cells size to
//     /// their content (text preferred width / collapsed avatars) and clump toward the centre.
//     /// </summary>
//     static void MakeEqualColumn(GameObject cell)
//     {
//         var le = cell.GetComponent<LayoutElement>();
//         if (le == null) le = cell.AddComponent<LayoutElement>();
//         le.minWidth = 0f;
//         le.preferredWidth = 0f;
//         le.flexibleWidth = 1f;
//     }

//     /// <summary>A single centered text cell inside a row's HorizontalLayoutGroup.</summary>
//     void AddCellLabel(Transform rowParent, string text, Color color, int maxSize, FontStyles style)
//     {
//         var cellGo = new GameObject("Cell", typeof(RectTransform));
//         cellGo.transform.SetParent(rowParent, false);
//         MakeEqualColumn(cellGo);
//         var tmp = cellGo.AddComponent<TextMeshProUGUI>();
//         tmp.text = text;
//         tmp.color = color;
//         tmp.alignment = TextAlignmentOptions.Center;
//         tmp.fontStyle = style;
//         tmp.enableAutoSizing = true;
//         tmp.fontSizeMin = 12;
//         tmp.fontSizeMax = maxSize;
//         tmp.overflowMode = TextOverflowModes.Ellipsis;
//         tmp.raycastTarget = false;
//         if (customFont != null) tmp.font = customFont;
//     }

//     /// <summary>Adds a horizontal dashed ledger line pinned to the bottom edge of a row (ignored by layout).</summary>
//     void AddRowDashedLine(GameObject rowGo, float width, float height)
//     {
//         var line = CreateDashedLine("DashedLine_Row", rowGo.transform, width - 8f, new Vector2(0f, -height / 2f + 2f), true);
//         var lrt = line.GetComponent<RectTransform>();
//         lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
//         lrt.pivot = new Vector2(0.5f, 0.5f);
//         lrt.anchoredPosition = new Vector2(0f, -height / 2f + 2f);
//     }

//     /// <summary>Two faint vertical dividers behind the table separating ROUNDS | players | TOTAL.</summary>
//     void BuildVerticalDividers(Transform container, float innerW)
//     {
//         var crt = container as RectTransform;
//         float h = (crt != null && crt.rect.height > 1f) ? crt.rect.height - 40f : 540f;
//         float col = innerW / 6f;
//         AddVerticalDivider(container, -innerW / 2f + col, h);       // after ROUNDS
//         AddVerticalDivider(container, -innerW / 2f + col * 5f, h);  // before TOTAL
//     }

//     void AddVerticalDivider(Transform container, float x, float height)
//     {
//         var go = CreateRect("VDivider", container, new Vector2(x, 0f), new Vector2(3f, height));
//         var le = go.AddComponent<LayoutElement>();
//         le.ignoreLayout = true;
//         var img = AddImage(go, new Color(0.12f, 0.06f, 0.02f, 0.45f));
//         img.raycastTarget = false;
//         go.transform.SetAsFirstSibling();
//         _dynamicDecor.Add(go);
//     }

//     void ClearDynamicMainFrameContent(Transform mainFrame)
//     {
//         if (mainFrame == null) return;

//         var toDestroy = new List<GameObject>();
//         foreach (Transform child in mainFrame)
//         {
//             string childName = child.name;
//             if (childName == "ScorecardTable" || childName == "CloseButton"
//                 || childName == "RoundScrollView" || childName == "ScorecardHeader"
//                 || childName == "RoundTitle")
//                 toDestroy.Add(child.gameObject);
//             else if (childName.StartsWith("SeparatorLine_") || childName.StartsWith("DashedLine_"))
//                 toDestroy.Add(child.gameObject);
//         }

//         foreach (GameObject go in toDestroy)
//             DestroyObjectSafe(go);
//     }

//     void EnsureSceneButtons(Transform mainFrame)
//     {
//         // Action buttons (HOME / RESTART) are intentionally removed from the result panel:
//         // at match end the leaderboard auto-returns to the Home screen after
//         // MatchEndLeaderboardSeconds. If the ButtonsContainer has been deleted from the
//         // scene we do NOT recreate it (no fallback buttons).
//         Transform btnContainer = mainFrame.Find("ButtonsContainer");
//         if (btnContainer == null) return;

//         WireSceneButton(ref restartButton, btnContainer, "RestartButton", OnRestartClicked);
//         WireSceneButton(ref homeButton, btnContainer, "HomeButton", OnHomeClicked);
//     }

//     void WireSceneButton(ref Button btn, Transform container, string childName, UnityEngine.Events.UnityAction action)
//     {
//         if (btn == null)
//         {
//             Transform existing = container.Find(childName);
//             if (existing != null)
//                 btn = existing.GetComponent<Button>();
//         }

//         if (btn == null)
//         {
//             Color fallbackColor = childName == "RestartButton"
//                 ? new Color(0.12f, 0.65f, 0.28f)
//                 : new Color(0.85f, 0.35f, 0.15f);
//             string label = childName == "RestartButton" ? "PLAY AGAIN" : "HOME";
//             EnsureFallbackButton(ref btn, container, childName, label, fallbackColor, action);
//             return;
//         }

//         if (btn.transform.parent != container)
//             btn.transform.SetParent(container, false);

//         EnableButtonVisuals(btn);
//         btn.onClick.RemoveAllListeners();
//         btn.onClick.AddListener(action);
//     }

//     static void EnableButtonVisuals(Button btn)
//     {
//         if (btn == null) return;
//         btn.enabled = true;
//         btn.interactable = true;
//         btn.gameObject.SetActive(true);

//         var img = btn.GetComponent<Image>();
//         if (img != null)
//             img.enabled = true;
//     }

//     void EnsureFallbackButton(ref Button btn, Transform parent, string goName, string label, Color bgColor, UnityEngine.Events.UnityAction action)
//     {
//         var go = CreateRect(goName, parent, Vector2.zero, new Vector2(280, 90));
//         AddImage(go, bgColor);
//         btn = go.AddComponent<Button>();
//         btn.targetGraphic = go.GetComponent<Image>();
//         var newTmp = AddTmp(go.transform, label, Color.white, 30, TextAlignmentOptions.Center, FontStyles.Bold);
//         newTmp.rectTransform.sizeDelta = new Vector2(280, 90);

//         btn.onClick.RemoveAllListeners();
//         btn.onClick.AddListener(action);

//         var colors = btn.colors;
//         colors.highlightedColor = bgColor * 1.2f;
//         colors.pressedColor = bgColor * 0.8f;
//         btn.colors = colors;
//     }

//     GameObject CreateColumn(string name, Transform parent, float width, float contentHeight, float spacing)
//     {
//         var col = CreateRect(name, parent, Vector2.zero, new Vector2(width, contentHeight));
//         col.AddComponent<CanvasGroup>(); // For fading/animation
//         var vlg = col.AddComponent<VerticalLayoutGroup>();
//         vlg.childAlignment = TextAnchor.UpperCenter;
//         vlg.spacing = spacing;
//         vlg.childControlWidth = false;
//         vlg.childControlHeight = false;
//         vlg.childForceExpandWidth = false;
//         vlg.childForceExpandHeight = false;
//         return col;
//     }

//     void CreateLabelCell(string text, Transform parent, float width, float height, bool isBold)
//     {
//         var cell = CreateRect("LabelCell", parent, Vector2.zero, new Vector2(width, height));
//         // Push label towards the bottom so it aligns with name boxes
//         var txt = AddTmp(cell.transform, text, Color.white, 28, TextAlignmentOptions.Bottom, isBold ? FontStyles.Bold : FontStyles.Normal);
//         txt.rectTransform.anchoredPosition = new Vector2(0, -25f);
//         txt.rectTransform.sizeDelta = new Vector2(width, height);
//     }

//     void CreatePlayerHeaderCell(int seatIndex, Transform parent, float width, float height)
//     {
//         var cell = CreateRect("PlayerHeaderCell", parent, Vector2.zero, new Vector2(width, height));

//         // 1. Avatar Border/Frame (Circle)
//         var avatarFrameGo = CreateRect("AvatarFrame", cell.transform, new Vector2(0, 35f), new Vector2(75, 75));
//         var borderImg = AddImage(avatarFrameGo, new Color(0.35f, 0.22f, 0.12f, 0.85f));
//         Sprite circleSprite = null;
// #if UNITY_EDITOR
//         circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
// #endif
//         if (circleSprite != null) borderImg.sprite = circleSprite;

//         // 2. Avatar Inside Image
//         var avatarImgGo = CreateRect("AvatarImage", avatarFrameGo.transform, Vector2.zero, new Vector2(68, 68));
//         var avatarImg = AddImage(avatarImgGo, Color.white);
//         avatarImg.sprite = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
//         avatarImg.preserveAspect = true;

//         // 3. Name BG Box (Brown Rounded)
//         var nameBoxGo = CreateRect("NameBox", cell.transform, new Vector2(0, -25f), new Vector2(140, 32));
//         var nameBoxImg = AddImage(nameBoxGo, new Color(0.35f, 0.22f, 0.12f, 1f));
//         if (woodBoardSprite != null)
//         {
//             nameBoxImg.sprite = woodBoardSprite;
//             nameBoxImg.type = Image.Type.Simple;
//         }

//         // 4. Name Text
//         string name = GetSeatDisplayName(seatIndex);
//         var nameTxt = AddTmp(nameBoxGo.transform, name, Color.white, 16, TextAlignmentOptions.Center, FontStyles.Bold);
//         nameTxt.rectTransform.anchoredPosition = Vector2.zero;
//         nameTxt.rectTransform.sizeDelta = new Vector2(130, 26);
//         nameTxt.overflowMode = TextOverflowModes.Ellipsis;
//     }

//     void CreateValueCell(string text, Transform parent, float width, float height, bool isBold, bool isWinner = false, bool isCurrentRound = false)
//     {
//         var cell = CreateRect("ValueCell", parent, Vector2.zero, new Vector2(width, height));
        
//         // NOTE: this helper is NOT used by the live leaderboard (BuildResultPanelUI ->
//         // FillRowCells / BuildOrUpdateTotalsRow). The golden "winner" highlight and green
//         // "current round" color have been removed so NO code path can reintroduce golden
//         // score text. isWinner / isCurrentRound are kept only for signature compatibility.
//         Color textColor = Color.black;                 // all values black — no golden, no green
//         int fontSize = LeaderboardCellFontSize;        // same fixed size as the live rows
//         FontStyles style = FontStyles.Bold;            // matches the R1 / TOTAL weight

//         var txt = AddTmp(cell.transform, text, textColor, fontSize, TextAlignmentOptions.Center, style);
//         txt.rectTransform.anchoredPosition = Vector2.zero;
//         txt.rectTransform.sizeDelta = new Vector2(width, height);
//     }

//     void CreateCloseButton(Transform parent)
//     {
//         var btnGo = CreateRect("CloseButton", parent, new Vector2(540, 330), new Vector2(64, 64));
        
//         var bgImg = AddImage(btnGo, new Color(0.35f, 0.22f, 0.12f, 1f));
//         Sprite circleSprite = null;
// #if UNITY_EDITOR
//         circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
// #endif
//         if (circleSprite != null) bgImg.sprite = circleSprite;
        
//         var btn = btnGo.AddComponent<Button>();
//         btn.targetGraphic = bgImg;
//         btn.onClick.AddListener(CloseResult);

//         var txt = AddTmp(btnGo.transform, "X", Color.white, 26, TextAlignmentOptions.Center, FontStyles.Bold);
//         txt.rectTransform.anchoredPosition = Vector2.zero;
//         txt.rectTransform.sizeDelta = new Vector2(50, 50);
        
//         var colors = btn.colors;
//         colors.normalColor = new Color(0.35f, 0.22f, 0.12f, 1f);
//         colors.highlightedColor = new Color(0.5f, 0.3f, 0.15f, 1f);
//         colors.pressedColor = new Color(0.2f, 0.1f, 0.05f, 1f);
//         btn.colors = colors;
//     }

//     Sprite GetAvatarSprite(int actorNumber)
//     {
//         Sprite[] pool = GetProfileSpritePool();
//         if (pool == null || pool.Length == 0) return null;

//         int spriteIndex = ResolveAvatarIndexForActor(actorNumber);
//         if (spriteIndex < 0 || spriteIndex >= pool.Length)
//             spriteIndex = Mathf.Abs(actorNumber) % pool.Length;

//         return pool[spriteIndex];
//     }

//     static Sprite[] GetProfileSpritePool()
//     {
//         if (PlayerProfileManager.Instance != null &&
//             PlayerProfileManager.Instance.profileSprites != null &&
//             PlayerProfileManager.Instance.profileSprites.Length > 0)
//             return PlayerProfileManager.Instance.profileSprites;

//         if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
//             return MatchmakingManager.GlobalProfileSprites.ToArray();

//         return null;
//     }

//     static int ResolveAvatarIndexForActor(int actorNumber)
//     {
//         if (PhotonNetwork.LocalPlayer != null && actorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
//         {
//             int local = PlayerProfileManager.GetSavedAvatarIndex();
//             if (local >= 0) return local;
//         }

//         if (PhotonNetwork.CurrentRoom != null)
//         {
//             Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);
//             if (p != null && p.CustomProperties != null &&
//                 p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val))
//             {
//                 if (val != null)
//                 {
//                     if (val is int vi) return vi;
//                     if (int.TryParse(val.ToString(), out int parsed)) return parsed;
//                 }
//             }
//         }

//         return -1;
//     }

//     GameObject CreateRect(string name, Transform parent, Vector2 pos, Vector2 size)
//     {
//         var go = new GameObject(name, typeof(RectTransform));
//         go.transform.SetParent(parent, false);
//         var rt = go.GetComponent<RectTransform>();
//         rt.sizeDelta = size;
//         rt.anchoredPosition = pos;
//         return go;
//     }

//     // ============================================================
//     // LEDGER LINES & BANNER (1v1v1v1 leaderboard mockup)
//     // ============================================================

//     static readonly Color LedgerLineColor = new Color(0.12f, 0.06f, 0.02f, 0.6f);

//     /// <summary>Builds a horizontal dashed line from small dash segments (no sprite needed).</summary>
//     GameObject CreateDashedLine(string name, Transform parent, float width, Vector2 anchoredPos, bool ignoreLayout)
//     {
//         var line = CreateRect(name, parent, anchoredPos, new Vector2(width, 4f));
//         if (ignoreLayout)
//         {
//             var le = line.AddComponent<LayoutElement>();
//             le.ignoreLayout = true; // keep parent layout groups from repositioning the line
//         }

//         const float dashW = 20f;
//         const float gap = 14f;
//         const float step = dashW + gap;
//         int count = Mathf.Max(1, Mathf.FloorToInt(width / step));
//         float used = (count * step) - gap;
//         float startX = (-used / 2f) + (dashW / 2f);

//         for (int i = 0; i < count; i++)
//         {
//             var dash = CreateRect("Dash", line.transform, new Vector2(startX + (i * step), 0f), new Vector2(dashW, 4f));
//             var img = AddImage(dash, LedgerLineColor);
//             img.raycastTarget = false;
//         }
//         return line;
//     }

//     /// <summary>Solid vertical column divider spanning [bottomY, topY] at the given x (board-local).</summary>
//     void CreateVerticalSeparator(string name, Transform parent, float x, float topY, float bottomY)
//     {
//         float h = Mathf.Abs(topY - bottomY);
//         float cy = (topY + bottomY) / 2f;
//         var line = CreateRect(name, parent, new Vector2(x, cy), new Vector2(3f, h));
//         var img = AddImage(line, new Color(LedgerLineColor.r, LedgerLineColor.g, LedgerLineColor.b, 0.5f));
//         img.raycastTarget = false;
//     }

//     /// <summary>
//     /// Full-width banner ad placeholder pinned to the bottom of the screen (below the board).
//     /// Lives on the full-screen result panel so it stretches edge-to-edge. Reused across rebuilds.
//     /// </summary>
//     void CreateBannerAd()
//     {
//         if (resultPanel == null) return;

//         Transform existing = resultPanel.transform.Find("BannerAdPlacement");
//         GameObject banner = existing != null ? existing.gameObject : null;
//         if (banner == null)
//         {
//             banner = new GameObject("BannerAdPlacement", typeof(RectTransform));
//             banner.transform.SetParent(resultPanel.transform, false);
//         }

//         var rt = banner.GetComponent<RectTransform>();
//         rt.anchorMin = new Vector2(0f, 0f);
//         rt.anchorMax = new Vector2(1f, 0f);
//         rt.pivot = new Vector2(0.5f, 0f);
//         rt.offsetMin = new Vector2(0f, 0f);
//         rt.offsetMax = new Vector2(0f, BannerAdHeightPx); // full width banner band, flush to screen bottom (Task 15)

//         ResolveThemeSprites();
//         var bg = AddImage(banner, new Color(0.96f, 0.94f, 0.90f, 0.97f));
//         if (_roundedSprite != null) { bg.sprite = _roundedSprite; bg.type = UnityEngine.UI.Image.Type.Sliced; }
//         bg.raycastTarget = false;
//         // The real LevelPlay banner ad is shown as a native overlay at the bottom of the screen.
//         // Disable the placeholder background so the space is left empty for the live ad.
//         bg.enabled = false;

//         // Subtle top highlight strip for an engraved, AAA edge.
//         Transform topT = banner.transform.Find("TopEdge");
//         GameObject top = topT != null ? topT.gameObject : CreateRect("TopEdge", banner.transform, Vector2.zero, new Vector2(0f, 3f));
//         var topRt = top.GetComponent<RectTransform>();
//         topRt.anchorMin = new Vector2(0f, 1f);
//         topRt.anchorMax = new Vector2(1f, 1f);
//         topRt.pivot = new Vector2(0.5f, 1f);
//         topRt.offsetMin = new Vector2(0f, -3f);
//         topRt.offsetMax = Vector2.zero;
//         var topImg = AddImage(top, new Color(1f, 0.85f, 0.5f, 0.18f));
//         topImg.raycastTarget = false;
//         topImg.enabled = false;

//         // Placeholder label.
//         Transform lblT = banner.transform.Find("Label");
//         TextMeshProUGUI lbl;
//         if (lblT != null)
//             lbl = lblT.GetComponent<TextMeshProUGUI>();
//         else
//         {
//             lbl = AddTmp(banner.transform, "BANNER AD PLACEMENT (FULL WIDTH)", new Color(0.15f, 0.10f, 0.06f, 1f),
//                 30, TextAlignmentOptions.Center, FontStyles.Bold);
//             lbl.gameObject.name = "Label";
//         }
//         var lrt = lbl.rectTransform;
//         lrt.anchorMin = Vector2.zero;
//         lrt.anchorMax = Vector2.one;
//         lrt.offsetMin = Vector2.zero;
//         lrt.offsetMax = Vector2.zero;
//         // Hide the "BANNER AD PLACEMENT" placeholder text — the live ad fills the band instead.
//         lbl.gameObject.SetActive(false);

//         banner.transform.SetAsLastSibling(); // above the dim overlay
//     }

//     /// <summary>Updates avatar portraits and name plates in the scene-authored HeaderRow.</summary>
//     void RefreshLeaderboardHeader(Transform container)
//     {
//         if (container == null) return;

//         Transform header = container.Find("HeaderRow");
//         if (header == null) return;

//         RefreshPlayerNamesAndActors();

//         // Task 13/31: enlarge the header column labels (ROUNDS / TOTAL).
//         foreach (Transform child in header)
//         {
//             if (child.name != "Cell") continue;
//             var lbl = child.GetComponent<TextMeshProUGUI>();
//             if (lbl == null) continue;
//             lbl.enableAutoSizing = true;
//             lbl.fontSizeMin = 16;
//             lbl.fontSizeMax = 40;
//         }

//         var headerCells = new List<Transform>();
//         for (int i = 0; i < header.childCount; i++)
//         {
//             Transform child = header.GetChild(i);
//             if (child.name == "PlayerHeaderCell")
//                 headerCells.Add(child);
//         }

//         for (int seat = 0; seat < headerCells.Count && seat < 4; seat++)
//         {
//             Transform cell = headerCells[seat];
//             string displayName = GetSeatDisplayName(seat);

//             Transform nameBox = cell.Find("NameBox");
//             if (nameBox != null)
//             {
//                 var nameTmp = nameBox.GetComponentInChildren<TextMeshProUGUI>(true);
//                 if (nameTmp != null)
//                 {
//                     nameTmp.text = displayName;
//                     nameTmp.enableAutoSizing = true;
//                     nameTmp.fontSizeMin = 14;
//                     nameTmp.fontSizeMax = 26;
//                 }
//             }

//             Transform avatarT = cell.Find("AvatarImage") ?? FindDeepByName(cell, "AvatarImage");
//             if (avatarT != null)
//             {
//                 var avatarImg = avatarT.GetComponent<Image>();
//                 if (avatarImg != null)
//                 {
//                     Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seat));
//                     if (avatar != null)
//                         avatarImg.sprite = avatar;
//                 }
//             }
//         }
//     }

//     /// <summary>Hides the old mock-up status pill if it was created in a previous session.</summary>
//     void HideMatchFinishedLabel()
//     {
//         if (resultPanel == null) return;
//         Transform tag = resultPanel.transform.Find("MatchFinishedTag");
//         if (tag != null)
//             tag.gameObject.SetActive(false);
//     }

//     static Image AddImage(GameObject go, Color c)
//     {
//         var img = go.GetComponent<Image>();
//         if (img == null) img = go.AddComponent<Image>();
//         img.color = c;
//         return img;
//     }

//     TextMeshProUGUI AddTmp(Transform parent, string text, Color color, int size, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
//     {
//         var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
//         go.transform.SetParent(parent, false);
//         var rt = go.GetComponent<RectTransform>();
//         rt.sizeDelta = new Vector2(200, 50); // Default size, usually overridden by layout
//         var tmp = go.GetComponent<TextMeshProUGUI>();
//         tmp.text = text;
//         tmp.color = color;
//         tmp.fontSize = size;
//         tmp.alignment = align;
//         tmp.fontStyle = style;
//         tmp.raycastTarget = false;
//         if (customFont != null) tmp.font = customFont;
//         return tmp;
//     }

//     void ClearDynamicUI()
//     {
//         // Only destroy runtime-created overflow rows. The static leaderboard skeleton
//         // (HeaderRow, RoundRow_1..5, dividers) is authored in the scene and must persist.
//         foreach (var go in _overflowRows)
//             if (go != null) DestroyObjectSafe(go);
//         _overflowRows.Clear();
//     }

//     void DestroyObjectSafe(GameObject go)
//     {
//         if (go == null) return;
// #if UNITY_EDITOR
//         if (!Application.isPlaying)
//         {
//             DestroyImmediate(go);
//             return;
//         }
// #endif
//         Destroy(go);
//     }

//     void OnHomeClicked()
//     {
//         if (_resultActionTaken) return;   // ignore rapid double-taps
//         _resultActionTaken = true;

//         Debug.Log("[UI] Button Clicked: Home (from results)");
//         HideResultPanelImmediate();
//         ResetMatchStats();

//         bool leaving = PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Leaving;
//         if (PhotonNetwork.InRoom && !leaving)
//             PhotonNetwork.LeaveRoom();
//         else if (PhotonNetwork.OfflineMode)
//         {
//             if (!leaving) PhotonNetwork.LeaveRoom();
//             PhotonNetwork.OfflineMode = false;
//         }

//         if (DeckManager.Instance != null)
//             DeckManager.Instance.ResetMatchState();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ReturnToHomeScreen();
//     }

//     void OnRestartClicked()
//     {
//         if (_resultActionTaken) return;   // ignore rapid double-taps
//         if (!PhotonNetwork.OfflineMode && !(PhotonNetwork.InRoom && PhotonNetwork.IsConnectedAndReady))
//         {
//             Debug.LogWarning("[Result] Play Again ignored — not in a valid room state.");
//             return;
//         }
//         _resultActionTaken = true;

//         Debug.Log("[UI] Button Clicked: Play Again");
//         HideResultPanelImmediate();
//         ResetMatchStats();

//         if (DeckManager.Instance != null)
//             DeckManager.Instance.ResetMatchState();

//         GameFlowState.SetPhase(GameFlowPhase.InRoom);

//         if (PhotonNetwork.OfflineMode)
//         {
//             if (DeckManager.Instance != null && PhotonNetwork.IsMasterClient)
//                 DeckManager.Instance.FillBotsAndStart();
//         }
//         else if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
//         {
//             DeckManager.Instance.FillBotsAndStart();
//         }
//     }

//     void ResetMatchStats()
//     {
//         _statsRecorded = false;
//         _matchId = null;
//         _roundTransitionRunning = false;
//         currentRound = 1;
//         maxRounds = MaxRoundsBotsOnline;
//         roundHistory.Clear();
//         ResetRoundPlayerStats();

//         for (int i = 0; i < 4; i++)
//         {
//             playerResults[i].bid = 0;
//             playerResults[i].name = GetInitialPlayerName(i);
//         }
//     }
// }

// using UnityEngine;
// using TMPro;
// using UnityEngine.UI;
// using DG.Tweening;
// using Photon.Pun;
// using Photon.Realtime;
// using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
// using System.Collections;
// using System.Collections.Generic;
// using System.Linq;
// using System.Reflection;

// public class ResultManager : MonoBehaviourPunCallbacks
// {
//     public static ResultManager Instance;

//     [Header("UI Root")]
//     public CanvasGroup resultPanel;
//     [Tooltip("Optional root to find Panel_Winning without GameObject.Find (e.g. game canvas).")]
//     public Transform resultPanelSearchRoot;
//     public TMP_FontAsset customFont;

//     [Header("Optional — wired in scene or auto-built")]
//     public TMP_Text titleText;
//     public TMP_Text descriptionText;
//     public Button homeButton;
//     public Button restartButton;
//     public Transform scoreboardContainer;

//     [Header("Leaderboard Theme (assign in scene)")]
//     [Tooltip("Rounded wooden board sprite used for the panel background (e.g. BG_Buttons).")]
//     public Sprite woodBoardSprite;
//     [Tooltip("Settings gear icon sprite (e.g. settings_button).")]
//     public Sprite gearButtonSprite;
//     [Tooltip("Fixed avatar shown for every player column in the leaderboard. Assign once here — this exact sprite is what appears in game (no runtime randomization).")]
//     public Sprite playerAvatarSprite;

//     [System.Serializable]
//     public class PlayerResult
//     {
//         public string name;
//         public int actorNumber;
//         public int bid; // Restored
//         public int tricksWon;
//         public int dehlasCollected;
//         public bool isCompleted;
//         public float score;
//         public int rank;
//     }

//     [System.Serializable]
//     public class RoundResult
//     {
//         public int roundNumber;
//         public int[] dehlasPerSeat = new int[4];
//         public int[] tricksPerSeat = new int[4];
//     }

//     public int currentRound = 1;
//     /// <summary>5 for Bots/Online, -1 for unlimited Friends matches.</summary>
//     public int maxRounds = 5;
//     public List<RoundResult> roundHistory = new List<RoundResult>();

//     const int MaxRoundsBotsOnline = 5;
//     // Task 13: keep the leaderboard visible for 5 seconds after a round before auto-closing.
//     const float InterRoundLeaderboardSeconds = 5f;
//     const float MatchEndLeaderboardSeconds = 10f;

//     // Task 15: full-screen-bottom banner ad reserve. The board is lifted clear of this band so the
//     // leaderboard never overlaps the bottom banner ad. Kept in one place so the banner placeholder
//     // and the board lift stay in sync.
//     const float BannerAdHeightPx = 110f;
//     const float BannerAdSafeMarginPx = 24f;

//     private PlayerResult[] playerResults = new PlayerResult[4];
//     private Transform _builtRoot;
//     private Image _dimOverlay;
//     private readonly List<GameObject> _dynamicRows = new List<GameObject>();
//     // Runtime-only extra round rows created when a Friends match runs past the static row count.
//     private readonly List<GameObject> _overflowRows = new List<GameObject>();
//     private bool _isShowingResult;
//     private bool _statsRecorded;
//     // Phase 10: stable id for the finished match, reused as the Firebase pastGames key so a
//     // re-entry into RecordMatchStats can never create a duplicate cloud record.
//     private string _matchId;
//     private bool _resultActionTaken;
//     private bool _autoTransitionMode;
//     private bool _roundTransitionRunning;
//     private ScrollRect _roundScrollRect;
//     private static bool _resultPanelResolveWarned;

//     /// <summary>
//     /// Records the local player's finished match into <see cref="ProfileStatsStore"/>, split by
//     /// Vs Bots / Vs Online. Guarded so it only counts once per match.
//     /// </summary>
//     void RecordMatchStats()
//     {
//         if (_statsRecorded) return;
//         _statsRecorded = true;

//         PlayerResult me = playerResults != null && playerResults.Length > 0 ? playerResults[0] : null;
//         if (me == null) return;

//         bool vsBots = PhotonNetwork.OfflineMode ||
//                       (DeckManager.botActorNumbers != null && DeckManager.botActorNumbers.Count > 0);
//         int rank = me.rank <= 0 ? 4 : me.rank;
//         bool kot = me.dehlasCollected >= GetKotThreshold();

//         ProfileStatsStore.RecordCompletedGame(vsBots, rank, me.score, me.bid, kot);
//         Debug.Log($"[Stats] Recorded {(vsBots ? "VsBots" : "Online")} game: rank={rank} score={me.score} bid={me.bid} kot={kot}");

//         // Phase 10: in addition to the local PlayerPrefs write above (offline fallback), mirror this
//         // finished match to Firebase under the signed-in user so the Past Games screen can load from
//         // the cloud across devices. Uses matchId as the KEY so a re-entry cannot create a duplicate.
//         SaveMatchToFirebase(vsBots, rank, me.score);
//     }

//     /// <summary>
//     /// Phase 10 — Writes the just-finished match to <c>users/{uid}/pastGames/{matchId}</c> in Firebase
//     /// Realtime Database. matchId is the Photon room name (online/friends) or an <c>offline_{ticks}</c>
//     /// id (bots/offline), cached in <see cref="_matchId"/> so the same key is reused. No-op (with a log)
//     /// when no user is signed in — the local PlayerPrefs history remains the offline fallback.
//     /// </summary>
//     void SaveMatchToFirebase(bool vsBots, int rank, float score)
//     {
//         if (string.IsNullOrEmpty(_matchId))
//         {
//             string roomName = Photon.Pun.PhotonNetwork.CurrentRoom != null
//                 ? Photon.Pun.PhotonNetwork.CurrentRoom.Name
//                 : null;
//             _matchId = string.IsNullOrEmpty(roomName)
//                 ? $"offline_{System.DateTime.UtcNow.Ticks}"
//                 : roomName;
//         }

//         Firebase.Auth.FirebaseUser user = Firebase.Auth.FirebaseAuth.DefaultInstance != null
//             ? Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser
//             : null;
//         if (user == null || string.IsNullOrEmpty(user.UserId))
//         {
//             Debug.Log("[Stats] Skipping Firebase past-game save — no signed-in user (offline). Local history kept.");
//             return;
//         }

//         string uid = user.UserId;

//         var record = new System.Collections.Generic.Dictionary<string, object>
//         {
//             { "timeTicks", System.DateTime.UtcNow.Ticks },
//             { "vsBots", vsBots },
//             { "rank", rank },
//             { "score", score },
//             { "canceled", false }
//         };
//         if (GameSettings.Instance != null)
//             record["mode"] = GameSettings.Instance.currentMode.ToString();

//         Firebase.Database.DatabaseReference pastGameRef =
//             Firebase.Database.FirebaseDatabase
//                 .GetInstance("https://dehlapakad-c207c-default-rtdb.firebaseio.com/")
//                 .RootReference
//                 .Child("users").Child(uid).Child("pastGames").Child(_matchId);

//         Firebase.Extensions.TaskExtension.ContinueWithOnMainThread(
//             pastGameRef.SetValueAsync(record),
//             task =>
//             {
//                 if (task.IsFaulted || task.IsCanceled)
//                     Debug.LogWarning($"[Stats] Firebase past-game save FAILED (users/{uid}/pastGames/{_matchId}): {task.Exception}");
//                 else
//                     Debug.Log($"[Stats] Saved past game to Firebase: users/{uid}/pastGames/{_matchId}");
//             });
//     }

//     const int KotDehlasOneTaash = 4;
//     const int KotDehlasTwoTaash = 8;

//     // Professional Theme Colors
//     static readonly Color PanelBgColor = new Color(0.25f, 0.15f, 0.05f, 0.95f); // Wooden Dark
//     static readonly Color FrameColor = new Color(0.45f, 0.28f, 0.15f, 1f);     // Wooden Frame
//     static readonly Color RowBgColor = new Color(0f, 0f, 0f, 0.35f);           // Semi-transparent rows
//     static readonly Color WinnerGoldColor = new Color(1f, 0.84f, 0f, 1f);      // Gold highlight
//     static readonly Color TextWhiteColor = Color.white;
//     static readonly Color TextGoldColor = new Color(1f, 0.92f, 0.5f, 1f);
//     static readonly Color ScoreDarkColor = new Color(0.16f, 0.09f, 0.04f, 1f);

//     void Awake()
//     {
//         if (Instance == null) Instance = this;
//         else Destroy(gameObject);

//         for (int i = 0; i < 4; i++)
//             playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };

//         HideResultPanelImmediate();
//         WireButtons();
//     }

//     void WireButtons()
//     {
//         if (homeButton != null)
//         {
//             EnableButtonVisuals(homeButton);
//             homeButton.onClick.RemoveAllListeners();
//             homeButton.onClick.AddListener(OnHomeClicked);
//         }
//         if (restartButton != null)
//         {
//             EnableButtonVisuals(restartButton);
//             restartButton.onClick.RemoveAllListeners();
//             restartButton.onClick.AddListener(OnRestartClicked);
//         }
//     }

//     static void ShowLeaderboardBanner()
//     {
//         if (AdsManager.Instance == null) return;
//         AdsManager.Instance.LoadBanner();
//         AdsManager.Instance.ShowBanner();
//     }

//     static void HideLeaderboardBanner()
//     {
//         if (AdsManager.Instance == null) return;
//         AdsManager.Instance.HideBanner();
//     }

//     void HideResultPanelImmediate()
//     {
//         _isShowingResult = false;
//         HideLeaderboardBanner();
//         if (!ResolveResultPanel()) return;
//         resultPanel.DOKill();
//         resultPanel.alpha = 0;
//         resultPanel.interactable = false;
//         resultPanel.blocksRaycasts = false;
//         resultPanel.gameObject.SetActive(false);
//         if (_dimOverlay != null)
//             _dimOverlay.color = new Color(0f, 0f, 0f, 0f);
//     }

//     bool ResolveResultPanel()
//     {
//         if (resultPanel != null) return true;

//         Transform root = resultPanelSearchRoot;
//         if (root == null)
//         {
//             Canvas canvas = Object.FindAnyObjectByType<Canvas>();
//             if (canvas != null)
//                 root = canvas.transform.root;
//         }

//         if (root != null)
//         {
//             UiSafeLookup.SetSearchRoot(root);
//             if (UiSafeLookup.TryGet("Panel_Winning", out GameObject panelGo) && panelGo != null)
//             {
//                 resultPanel = panelGo.GetComponent<CanvasGroup>();
//                 if (resultPanel == null)
//                     resultPanel = panelGo.AddComponent<CanvasGroup>();
//                 Debug.Log("[ResultManager] Resolved Panel_Winning under canvas hierarchy.");
//                 return true;
//             }
//         }

//         if (!_resultPanelResolveWarned)
//         {
//             _resultPanelResolveWarned = true;
//             Debug.LogWarning("[ResultManager] resultPanel not found — assign resultPanel or Panel_Winning under resultPanelSearchRoot.");
//         }
//         return false;
//     }

//     void EnsurePanelHierarchyActive()
//     {
//         if (resultPanel == null) return;

//         Transform t = resultPanel.transform;
//         while (t != null)
//         {
//             if (!t.gameObject.activeSelf)
//                 t.gameObject.SetActive(true);
//             t = t.parent;
//         }

//         Canvas rootCanvas = resultPanel.GetComponentInParent<Canvas>();
//         if (rootCanvas != null)
//             rootCanvas.gameObject.SetActive(true);

//         resultPanel.transform.SetAsLastSibling();
//     }

//     void EnsureDimOverlay()
//     {
//         if (resultPanel == null) return;

//         Transform existing = resultPanel.transform.Find("Overlay");
//         if (existing != null)
//         {
//             _dimOverlay = existing.GetComponent<Image>();
//             if (_dimOverlay == null)
//                 _dimOverlay = existing.gameObject.AddComponent<Image>();
//         }
//         else
//         {
//             GameObject overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
//             overlayGo.transform.SetParent(resultPanel.transform, false);
//             overlayGo.transform.SetAsFirstSibling();
//             RectTransform rt = overlayGo.GetComponent<RectTransform>();
//             rt.anchorMin = Vector2.zero;
//             rt.anchorMax = Vector2.one;
//             rt.offsetMin = Vector2.zero;
//             rt.offsetMax = Vector2.zero;
//             _dimOverlay = overlayGo.GetComponent<Image>();
//             _dimOverlay.raycastTarget = true;
//         }

//         _dimOverlay.color = new Color(0f, 0f, 0f, 0.55f);
//         _dimOverlay.raycastTarget = true;

//         // Task 16: tapping outside the board (on the full-screen overlay) closes the leaderboard —
//         // but ONLY when the panel is shown manually. Task 13: during an automatic inter-round /
//         // match-end transition the leaderboard must stay up for its full duration, so we do NOT wire
//         // tap-to-close while _autoTransitionMode is active (a stray tap was dismissing it instantly).
//         var overlayBtn = _dimOverlay.GetComponent<Button>();
//         if (overlayBtn == null) overlayBtn = _dimOverlay.gameObject.AddComponent<Button>();
//         overlayBtn.transition = Selectable.Transition.None;
//         overlayBtn.onClick.RemoveAllListeners();
//         if (!_autoTransitionMode)
//             overlayBtn.onClick.AddListener(CloseResult);
//     }

//     string GetInitialPlayerName(int i)=> i == 0 ? "You" : "Dehla_AI_" + i;

//     public void SetBid(int seatIndex, int bidValue)
//     {
//         if (seatIndex >= 0 && seatIndex < 4)
//             playerResults[seatIndex].bid = bidValue;
//     }

//     public void OnTrickWon(int winnerSeatIndex, int dehlaCount)
//     {
//         if (winnerSeatIndex < 0 || winnerSeatIndex >= 4) return;
//         playerResults[winnerSeatIndex].tricksWon++;
//         playerResults[winnerSeatIndex].dehlasCollected += dehlaCount;

//         if (PhotonNetwork.IsMasterClient)
//             SyncScoresToRoomProperties();
//     }

//     void SyncScoresToRoomProperties()
//     {
//         if (!PhotonNetwork.InRoom) return;
//         int[] tricks = new int[4];
//         int[] dehlas = new int[4];
//         for (int i = 0; i < 4; i++)
//         {
//             tricks[i] = playerResults[i].tricksWon;
//             dehlas[i] = playerResults[i].dehlasCollected;
//         }
//         PhotonNetwork.CurrentRoom.SetCustomProperties(
//             new PhotonHashtable { { "SW", tricks }, { "DL", dehlas } });
//     }

//     public override void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
//     {
//         if (propertiesThatChanged == null) return;
//         if (propertiesThatChanged.ContainsKey("CR") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CR", out object crObj))
//             currentRound = (int)crObj;
//         if (propertiesThatChanged.ContainsKey("MR") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("MR", out object mrObj))
//             maxRounds = (int)mrObj;
//         if (propertiesThatChanged.ContainsKey("SW") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("SW", out object tricksObj))
//         {
//             int[] tricks = tricksObj as int[];
//             for (int i = 0; tricks != null && i < 4 && i < tricks.Length; i++)
//                 playerResults[i].tricksWon = tricks[i];
//         }
//         if (propertiesThatChanged.ContainsKey("DL") &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DL", out object dehlaObj))
//         {
//             int[] dehlas = dehlaObj as int[];
//             for (int i = 0; dehlas != null && i < 4 && i < dehlas.Length; i++)
//                 playerResults[i].dehlasCollected = dehlas[i];
//         }
//     }

//     public void InitializeForMatch()
//     {
//         bool unlimited = GameSettings.Instance != null
//             && GameSettings.Instance.currentMatchType == MatchType.PlayWithFriends;
//         if (!unlimited && DeckManager.IsPrivateFriendsRoom())
//             unlimited = true;

//         maxRounds = unlimited ? -1 : MaxRoundsBotsOnline;
//         currentRound = 1;
//         roundHistory.Clear();
//         _roundTransitionRunning = false;
//         _statsRecorded = false;
//         _matchId = null;
//         ResetRoundPlayerStats();

//         if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
//             SyncRoundConfigToRoom();
//     }

//     void SyncRoundConfigToRoom()
//     {
//         if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
//         PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable
//         {
//             { "CR", currentRound },
//             { "MR", maxRounds }
//         });
//     }

//     public bool IsMatchOver() => maxRounds != -1 && currentRound >= maxRounds;

//     /// <summary>Master-only entry when the 13th trick of a round completes.</summary>
//     public void TriggerRoundCompletedFromMaster()
//     {
//         if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.OfflineMode) return;
//         if (DeckManager.Instance != null && DeckManager.Instance.photonView != null)
//             DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_OnRoundCompleted), RpcTarget.All);
//         else
//             OnRoundCompleted();
//     }

//     public void OnRoundCompleted()
//     {
//         if (_roundTransitionRunning) return;

//         if (TurnManager.Instance != null)
//             TurnManager.Instance.StopTimer();

//         // Task 17 root cause: the next-round trigger used to be scheduled AFTER the (heavy, fragile)
//         // scoring + Panel_Winning UI build. Any exception thrown while computing scores or building
//         // the leaderboard aborted OnRoundCompleted before BeginRoundEndSequence/RoundTransitionRoutine
//         // was ever reached — so the next round never started. We now compute scores and render the
//         // leaderboard defensively (try/catch) and mark the transition as running FIRST, guaranteeing
//         // the next-round deal is always scheduled regardless of any UI/scoring failure.
//         try
//         {
//             EnsurePlayerResults();
//             RefreshPlayerNamesAndActors();
//             CalculateScores();
//             AssignRanks();
//             FinalizeCurrentRoundScores();
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError($"[Result] Round scoring failed (continuing round lifecycle): {e}");
//         }

//         bool matchOver = IsMatchOver();
//         if (matchOver)
//             GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);

//         // Set BEFORE the leaderboard render so an exception below can never block the trigger.
//         _roundTransitionRunning = true;
//         bool authoritative = PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

//         try
//         {
//             ShowRoundLeaderboard(matchOver);
//         }
//         catch (System.Exception e)
//         {
//             Debug.LogError($"[Result] Leaderboard render failed (continuing round lifecycle): {e}");
//         }

//         if (matchOver)
//             StartCoroutine(RoundTransitionRoutine(matchOver, authoritative));
//         else if (GameManager.Instance != null)
//             GameManager.Instance.BeginRoundEndSequence(authoritative);
//         else
//             StartCoroutine(RoundTransitionRoutine(matchOver, authoritative));
//     }

//     /// <summary>Called by <see cref="GameManager"/> once the leaderboard window and hide are complete.</summary>
//     public void NotifyRoundEndSequenceComplete()
//     {
//         HideResultPanelImmediate();
//         _roundTransitionRunning = false;
//     }

//     IEnumerator RoundTransitionRoutine(bool matchOver, bool authoritative)
//     {
//         float wait = matchOver ? MatchEndLeaderboardSeconds : InterRoundLeaderboardSeconds;
//         yield return new WaitForSecondsRealtime(wait);

//         HideResultPanelImmediate();
//         _roundTransitionRunning = false;

//         if (!authoritative)
//             yield break;

//         if (matchOver)
//         {
//             AssignMatchRanksFromHistory();
//             RecordMatchStats();
//             if (DeckManager.Instance != null)
//                 DeckManager.Instance.ResetMatchState();

//             if (PhotonNetwork.InRoom)
//                 PhotonNetwork.LeaveRoom();
//             else if (NetworkManager.Instance != null)
//                 NetworkManager.Instance.ReturnToHomeScreen();
//             yield break;
//         }

//         int nextRound = currentRound + 1;
//         if (DeckManager.Instance != null)
//         {
//             if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
//                 DeckManager.Instance.ResetRoundStateForNextRound();
//             if (DeckManager.Instance.photonView != null)
//                 DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_BeginNextRound), RpcTarget.AllBuffered, nextRound);
//         }
//     }

//     public void ApplyNextRoundStart(int newRound)
//     {
//         currentRound = newRound;
//         ResetRoundPlayerStats();
//         if (PhotonNetwork.IsMasterClient)
//             SyncRoundConfigToRoom();
//     }

//     void ShowRoundLeaderboard(bool matchOver)
//     {
//         ShowResultInternal(autoTransition: true, matchOver: matchOver);
//     }

//     public void ToggleLeaderboard()
//     {
//         if (!ResolveResultPanel() || resultPanel == null) return;
//         if (GameFlowState.Current == GameFlowPhase.Dealing) return;

//         CanvasGroup cg = resultPanel.GetComponent<CanvasGroup>();
//         bool isActuallyVisible = resultPanel.gameObject.activeSelf && cg != null && cg.alpha > 0.1f;

//         if (isActuallyVisible)
//         {
//             CloseResult();
//         }
//         else
//         {
//             resultPanel.gameObject.SetActive(true);
//             resultPanel.transform.SetAsLastSibling();
//             if (cg != null)
//             {
//                 DOTween.Kill(cg);
//                 cg.alpha = 1f;
//                 cg.interactable = true;
//                 cg.blocksRaycasts = true;
//             }
//         }
//     }

//     public void CloseResult()
//     {
//         // Player kisi bhi time leaderboard close kar sakta hai.
//         // Background mein cards deal hoti rehni chahiye — close sirf panel hide karta hai.
//         if (_autoTransitionMode && _roundTransitionRunning)
//             _roundTransitionRunning = false;

//         HideResultPanelImmediate();
//     }

//     /// <summary>
//     /// Hard, immediate leaderboard teardown callable from the deal pipeline. Guarantees that NO
//     /// client begins rendering the next deal while its leaderboard is still on screen (the per-client
//     /// 5s hide timers can drift, so the dealing RPC could otherwise arrive before a client hides).
//     /// </summary>
//     public void ForceHideLeaderboardNow()
//     {
//         if (_roundTransitionRunning)
//         {
//             StopAllCoroutines();          // cancel this client's own pending 5s hide
//             _roundTransitionRunning = false;
//         }
//         HideResultPanelImmediate();       // instant hide (no tween)
//     }

//     void EnsurePlayerResults()
//     {
//         if (playerResults == null || playerResults.Length < 4)
//             playerResults = new PlayerResult[4];

//         for (int i = 0; i < 4; i++)
//         {
//             if (playerResults[i] == null)
//                 playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };
//         }
//     }

//     [ContextMenu("Show Test Result")]
//     public void ShowResult()
//     {
//         ShowResultInternal(autoTransition: false, matchOver: false);
//     }

//     void ShowResultInternal(bool autoTransition, bool matchOver)
//     {
//         if (_isShowingResult)
//         {
//             Debug.LogWarning("[Result] ShowResult ignored — already showing.");
//             return;
//         }
//         if (!ResolveResultPanel())
//         {
//             Debug.LogError("[Result] ShowResult aborted — result panel reference missing.");
//             return;
//         }

//         _isShowingResult = true;
//         _resultActionTaken = false;
//         _autoTransitionMode = autoTransition;
//         Debug.Log(autoTransition
//             ? $"[Result] Round {currentRound} leaderboard (matchOver={matchOver})"
//             : "Result Panel Opening");
//         EnsurePlayerResults();
//         EnsurePanelHierarchyActive();
//         EnsureDimOverlay();

//         if (!autoTransition)
//         {
//             RefreshPlayerNamesAndActors();
//             CalculateScores();
//             AssignRanks();
//             RecordMatchStats();
//         }

//         PlayerResult winner = playerResults.OrderBy(p => p.rank).FirstOrDefault();
//         if (winner != null)
//             Debug.Log($"Winner Determined: {winner.name} (Rank #{winner.rank}, Score {winner.score})");

//         BuildResultPanelUI();
//         SetActionButtonsVisible(!autoTransition);
//         StartCoroutine(ScrollLeaderboardToBottom());

//         resultPanel.gameObject.SetActive(true);
//         ShowLeaderboardBanner();
//         ResetPanelOpenStateInstant();
//         Debug.Log("Result Panel Opened");

//         CreateBannerAd();
//         HideMatchFinishedLabel();
//     }

//     /// <summary>No open animation — panel and MainFrame stay at full scene-authored size/scale.</summary>
//     void ResetPanelOpenStateInstant()
//     {
//         if (resultPanel == null) return;

//         resultPanel.DOKill(complete: true);
//         resultPanel.alpha = 1f;
//         resultPanel.interactable = true;
//         resultPanel.blocksRaycasts = true;

//         Transform root = resultPanel.transform;
//         root.DOKill(complete: true);
//         root.localScale = Vector3.one;

//         if (_dimOverlay != null)
//         {
//             _dimOverlay.DOKill(complete: true);
//             Color c = _dimOverlay.color;
//             c.a = 0.55f;
//             _dimOverlay.color = c;
//         }

//         Transform frame = root.Find("MainFrame");
//         if (frame != null)
//         {
//             frame.DOKill(complete: true);
//             frame.localScale = Vector3.one;
//             frame.SetAsLastSibling();

//             // Task 15: lift the board fully above the bottom banner ad (no shrinking / no
//             // offsetMin / sizeDelta hacks). The board bottom must clear the banner band
//             // (BannerAdHeightPx) plus a small safe margin so the two never overlap.
//             var frt = frame as RectTransform;
//             if (frt != null)
//             {
//                 Vector2 pos = frt.anchoredPosition;
//                 float minY = BannerAdHeightPx + BannerAdSafeMarginPx; // ~134px above center baseline
//                 if (pos.y < minY) pos.y = minY;
//                 frt.anchoredPosition = pos;
//             }
//         }
//     }

//     IEnumerator ScrollLeaderboardToBottom()
//     {
//         yield return null;
//         yield return null;
//         Canvas.ForceUpdateCanvases();
//         if (_roundScrollRect != null)
//             _roundScrollRect.verticalNormalizedPosition = 0f;
//     }

//     /// <summary>
//     /// Recursive by-name lookup. PlayerRowsContainer now lives under a ScrollRect viewport
//     /// (RoundScrollView/Viewport/PlayerRowsContainer), so the old direct <c>Transform.Find</c>
//     /// (children-only) no longer resolves it. This walks the whole subtree.
//     /// </summary>
//     static Transform FindDeepChild(Transform root, string childName)
//     {
//         if (root == null) return null;
//         Transform direct = root.Find(childName);
//         if (direct != null) return direct;
//         foreach (Transform child in root)
//         {
//             Transform found = FindDeepChild(child, childName);
//             if (found != null) return found;
//         }
//         return null;
//     }

//     void SetActionButtonsVisible(bool visible)
//     {
//         if (homeButton != null) homeButton.gameObject.SetActive(visible);
//         if (restartButton != null) restartButton.gameObject.SetActive(visible);

//         Transform mainFrame = resultPanel != null ? resultPanel.transform.Find("MainFrame") : null;
//         if (mainFrame != null)
//         {
//             Transform btnContainer = mainFrame.Find("ButtonsContainer");
//             if (btnContainer != null) btnContainer.gameObject.SetActive(visible);

//             // Close button HAMESHA visible — player leaderboard kabhi bhi band kar sake.
//             Transform closeBtn = mainFrame.Find("CloseButton");
//             if (closeBtn != null) closeBtn.gameObject.SetActive(true);
//         }
//     }

//     void RefreshPlayerNamesAndActors()
//     {
//         for (int seat = 0; seat < 4; seat++)
//         {
//             playerResults[seat].name = GetSeatDisplayName(seat);
//             playerResults[seat].actorNumber = GetActorNumberBySeat(seat);
//         }
//     }

//     int GetActorNumberBySeat(int seatIndex)
//     {
//         if (PlayerHand.LocalInstance == null) return seatIndex; 
        
//         // tableTurnOrder is indexed by visual seat (0-3).
//         var field = typeof(PlayerHand).GetField("tableTurnOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
//         if (field == null) return -1;
        
//         var turnOrder = (List<int>)field.GetValue(PlayerHand.LocalInstance);
//         if (turnOrder != null && seatIndex < turnOrder.Count)
//             return turnOrder[seatIndex];
            
//         return -1;
//     }

//     string GetSeatDisplayName(int seatIndex)
//     {
//         if (seatIndex == 0)
//             return PlayerProfileSync.GetLocalProfileDisplayName();

//         if (PlayerProfileSync.Instance != null)
//         {
//             switch (seatIndex)
//             {
//                 case 1 when PlayerProfileSync.Instance.txtLeftName != null:
//                     return CleanName(PlayerProfileSync.Instance.txtLeftName.text);
//                 case 2 when PlayerProfileSync.Instance.txtTopName != null:
//                     return CleanName(PlayerProfileSync.Instance.txtTopName.text);
//                 case 3 when PlayerProfileSync.Instance.txtRightName != null:
//                     return CleanName(PlayerProfileSync.Instance.txtRightName.text);
//             }
//         }
//         return "Player " + (seatIndex + 1);
//     }

//     static string CleanName(string raw)
//     {
//         if (string.IsNullOrEmpty(raw)) return "Player";
//         return raw.Split('\n')[0].Trim();
//     }

//     void FinalizeCurrentRoundScores()
//     {
//         var result = new RoundResult { roundNumber = currentRound };
//         for (int seat = 0; seat < 4; seat++)
//         {
//             result.dehlasPerSeat[seat] = playerResults[seat].dehlasCollected;
//             result.tricksPerSeat[seat] = playerResults[seat].tricksWon;
//         }

//         if (roundHistory.Count > 0 && roundHistory[roundHistory.Count - 1].roundNumber == currentRound)
//             roundHistory[roundHistory.Count - 1] = result;
//         else
//             roundHistory.Add(result);

//         Debug.Log($"[Result] Round R{currentRound} finalized: " +
//                   string.Join(", ", Enumerable.Range(0, 4).Select(i => $"{playerResults[i].name}={result.dehlasPerSeat[i]}")));
//     }

//     void ResetRoundPlayerStats()
//     {
//         for (int i = 0; i < 4; i++)
//         {
//             playerResults[i].tricksWon = 0;
//             playerResults[i].dehlasCollected = 0;
//             playerResults[i].score = 0;
//             playerResults[i].isCompleted = false;
//             playerResults[i].rank = 0;
//         }

//         if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
//         {
//             PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable
//             {
//                 { "SW", new int[4] },
//                 { "DL", new int[4] },
//                 { "TP", 0 }
//             });
//         }
//     }

//     void AssignMatchRanksFromHistory()
//     {
//         int[] totals = new int[4];
//         foreach (RoundResult round in roundHistory)
//             for (int i = 0; i < 4; i++)
//                 totals[i] += round.dehlasPerSeat[i];

//         for (int i = 0; i < 4; i++)
//         {
//             playerResults[i].dehlasCollected = totals[i];
//             playerResults[i].score = totals[i];
//         }

//         // Task 6: friends 2v2 — final standings are by TEAM (combined dehlas), not 4 individuals.
//         if (IsFriendsTeamMode())
//         {
//             int teamA = totals[0] + totals[2];
//             int teamB = totals[1] + totals[3];
//             playerResults[0].score = playerResults[2].score = teamA;
//             playerResults[1].score = playerResults[3].score = teamB;
//             AssignTeamRanks();
//             return;
//         }

//         var ranked = totals.Select((value, index) => (value, index)).OrderByDescending(x => x.value).ToList();
//         for (int r = 0; r < ranked.Count; r++)
//             playerResults[ranked[r].index].rank = r + 1;
//     }

//     static int GetKotThreshold()
//     {
//         return TaashRules.IsTwoTaashMode ? KotDehlasTwoTaash : KotDehlasOneTaash;
//     }

//     static string FormatDehlaScore(int dehlas)
//     {
//         int kotThreshold = GetKotThreshold();
//         return dehlas == kotThreshold ? $"{dehlas} (KOT)" : dehlas.ToString();
//     }

//     static int SumRound(int[] roundScores)
//     {
//         int total = 0;
//         for (int i = 0; i < roundScores.Length; i++)
//             total += roundScores[i];
//         return total;
//     }

//     void CalculateScores()
//     {
//         // Task 28: a player's leaderboard score is their CUMULATIVE Dehlas captured across every
//         // round played so far (Dehla Pakad's actual scoring metric) — NOT a per-round trick/dehla
//         // blend. This makes the inter-round ranks reflect the true running standings and stay
//         // consistent with the match-end ranking (AssignMatchRanksFromHistory), which also sums
//         // dehlas per seat. Tricks won are retained only as a deterministic tiebreak in
//         // CompareForLeaderboard. The current (not-yet-finalized) round is added from the live
//         // playerResults, while previous rounds come from roundHistory.
//         for (int seat = 0; seat < playerResults.Length; seat++)
//         {
//             PlayerResult p = playerResults[seat];
//             if (p == null) continue;

//             int cumulativeDehlas = p.dehlasCollected; // current round (not yet in roundHistory)
//             foreach (RoundResult rr in roundHistory)
//             {
//                 // Skip the current round if it has already been finalized into history, so it is
//                 // never double-counted (e.g. when the panel is re-shown).
//                 if (rr == null || rr.roundNumber == currentRound) continue;
//                 if (rr.dehlasPerSeat != null && seat < rr.dehlasPerSeat.Length)
//                     cumulativeDehlas += rr.dehlasPerSeat[seat];
//             }

//             p.score = cumulativeDehlas;
//             p.isCompleted = true;
//         }

//         // Task 6: in FRIENDS 2v2 each partnership shares a single score (combined dehlas), so the
//         // leaderboard ranks/wins by TEAM instead of 4 individuals. 1v1v1v1 (bots/online) is untouched.
//         if (IsFriendsTeamMode())
//             ApplyTeamScores();
//     }

//     /// <summary>
//     /// Task 6 — Friends 2v2 team format. Active ONLY in a private friends room running logic mode 2
//     /// (LogicB / 2v2, synced via room property "LM"). Bots/Online (1v1v1v1, logic mode 1) return
//     /// false so their per-individual scoring is left completely unchanged.
//     /// </summary>
//     bool IsFriendsTeamMode()
//     {
//         if (!DeckManager.IsPrivateFriendsRoom()) return false;
//         return GetLogicMode() == 2;
//     }

//     /// <summary>Reads the active logic mode (1 = 1v1v1v1, 2 = 2v2). Prefers the synced room prop "LM".</summary>
//     static int GetLogicMode()
//     {
//         if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null &&
//             PhotonNetwork.CurrentRoom.CustomProperties != null &&
//             PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("LM", out object lm) && lm is int li)
//             return li;
//         if (ModeManager.Instance != null) return ModeManager.Instance.currentLogicMode;
//         return 1;
//     }

//     /// <summary>
//     /// Task 6 — Partners sit ACROSS the table: visual seats {0,2} = Team A, seats {1,3} = Team B
//     /// (seat 0 is always the local player). Each member's leaderboard SCORE becomes their team's
//     /// combined dehlas so ranking groups partners together and produces a team win. Individual
//     /// dehlasCollected / tricksWon are intentionally left intact for per-seat display and tiebreaks.
//     /// </summary>
//     void ApplyTeamScores()
//     {
//         float teamA = playerResults[0].score + playerResults[2].score;
//         float teamB = playerResults[1].score + playerResults[3].score;
//         playerResults[0].score = playerResults[2].score = teamA;
//         playerResults[1].score = playerResults[3].score = teamB;
//     }

//     /// <summary>
//     /// Task 6 — Assigns a TEAM rank: both members of the higher-scoring partnership get rank 1, the
//     /// other pair rank 2. Assumes <see cref="ApplyTeamScores"/> has already written team totals into
//     /// each member's score. Tie-break is client-independent (lowest actorNumber in the team wins) so
//     /// every client agrees on the winning team.
//     /// </summary>
//     void AssignTeamRanks()
//     {
//         float teamA = playerResults[0].score; // == playerResults[2].score
//         float teamB = playerResults[1].score; // == playerResults[3].score

//         int rankA;
//         if (teamA != teamB)
//         {
//             rankA = teamA > teamB ? 1 : 2;
//         }
//         else
//         {
//             int minActorA = Mathf.Min(playerResults[0].actorNumber, playerResults[2].actorNumber);
//             int minActorB = Mathf.Min(playerResults[1].actorNumber, playerResults[3].actorNumber);
//             rankA = minActorA <= minActorB ? 1 : 2;
//         }
//         int rankB = rankA == 1 ? 2 : 1;

//         playerResults[0].rank = playerResults[2].rank = rankA;
//         playerResults[1].rank = playerResults[3].rank = rankB;
//     }

//     void AssignRanks()
//     {
//         EnsurePlayerResults();

//         // Task 6: friends 2v2 ranks by team (rank 1/1, 2/2). Bots/online keep per-individual ranks.
//         if (IsFriendsTeamMode())
//         {
//             AssignTeamRanks();
//             return;
//         }

//         UpdateAndSortLeaderboard(new List<PlayerResult>(playerResults));
//     }

//     /// <summary>
//     /// Task 28 — Robust leaderboard ranking. Sorts players strictly by Score (descending), then
//     /// breaks ties deterministically: more Dehlas (KOT cards) first, then more Tricks won, then a
//     /// stable fallback on actorNumber so the order never flickers between equal players. Null-safe:
//     /// silently skips players that disconnected/were removed right before the leaderboard is built,
//     /// so a missing player can never break the round-end game loop. Ranks are written back onto the
//     /// surviving PlayerResult objects (rank 1 = winner).
//     /// </summary>
//     public void UpdateAndSortLeaderboard(List<PlayerResult> currentPlayers)
//     {
//         if (currentPlayers == null) return;

//         // Drop any null entries (a player object can be missing if they left right at round end).
//         List<PlayerResult> valid = currentPlayers.Where(p => p != null).ToList();
//         if (valid.Count == 0) return;

//         valid.Sort(CompareForLeaderboard);

//         for (int i = 0; i < valid.Count; i++)
//             valid[i].rank = i + 1;
//     }

//     /// <summary>
//     /// Leaderboard comparator: Score desc -> Dehlas (KOT) desc -> Tricks desc -> actorNumber asc.
//     /// Returns negative when <paramref name="a"/> should rank ABOVE <paramref name="b"/>.
//     /// </summary>
//     static int CompareForLeaderboard(PlayerResult a, PlayerResult b)
//     {
//         if (a == null && b == null) return 0;
//         if (a == null) return 1;   // nulls sink to the bottom
//         if (b == null) return -1;

//         int byScore = b.score.CompareTo(a.score);            // higher score first
//         if (byScore != 0) return byScore;

//         int byDehlas = b.dehlasCollected.CompareTo(a.dehlasCollected); // KOT/Dehla tiebreak
//         if (byDehlas != 0) return byDehlas;

//         int byTricks = b.tricksWon.CompareTo(a.tricksWon);   // tricks (wins) tiebreak
//         if (byTricks != 0) return byTricks;

//         return a.actorNumber.CompareTo(b.actorNumber);       // stable, deterministic fallback
//     }

//     void BuildResultPanelUI()
//     {
//         // The Panel_Winning leaderboard skeleton (header + avatars + name plates + R1..R5 rows +
//         // dividers) lives in the scene hierarchy and is fully editable. At runtime we only ensure it
//         // exists, then fill score data into it — we never recreate the header/avatars/name plates so
//         // the authored layout, fonts and text stay exactly as set in the Editor.
//         ClearDynamicUI();

//         if (resultPanel == null) return;

//         Transform mainFrame = resultPanel.transform.Find("MainFrame");
//         if (mainFrame == null)
//         {
//             Debug.LogError("[Result] MainFrame not found under Panel_Winning. Cannot fill result UI.");
//             return;
//         }

//         mainFrame.localScale = Vector3.one;

//         // Task 14: reduce the board's rounded-corner radius (9-sliced sprite + higher PPU shrinks the corners).
//         var mainFrameImg = mainFrame.GetComponent<Image>();
//         if (mainFrameImg != null)
//         {
//             mainFrameImg.type = Image.Type.Sliced;
//             mainFrameImg.pixelsPerUnitMultiplier = 1.8f;
//         }

//         Transform rowsContainer = scoreboardContainer != null
//             ? scoreboardContainer
//             : FindDeepChild(mainFrame, "PlayerRowsContainer");

//         // PlayFriends runs unlimited rounds. The round rows live in a ScrollRect (RoundScrollView)
//         // added to the scene so the list can scroll instead of growing off-screen. Resolve it once
//         // so ScrollLeaderboardToBottom() can pin the newest ~5 rounds into view by default.
//         if (_roundScrollRect == null)
//             _roundScrollRect = mainFrame.GetComponentInChildren<ScrollRect>(true);

//         if (rowsContainer != null)
//         {
//             EnsureStaticLeaderboard(rowsContainer);
//             RefreshLeaderboardHeader(rowsContainer);
//             UpdateLeaderboardUI(rowsContainer);
//         }
//         else
//         {
//             Debug.LogWarning("[Result] PlayerRowsContainer not found under MainFrame — scores not filled.");
//         }

//         // Optional round-progress title, only if the user wired a titleText field.
//         string title = maxRounds == -1
//             ? $"Round {currentRound} Complete"
//             : $"Round {currentRound} / {maxRounds}";
//         if (titleText != null) titleText.text = title;

//         // Wire the existing CloseButton from the hierarchy (do not create/restyle it).
//         Transform closeT = mainFrame.Find("CloseButton");
//         if (closeT != null)
//         {
//             var closeBtn = closeT.GetComponent<Button>();
//             if (closeBtn != null)
//             {
//                 EnableButtonVisuals(closeBtn);
//                 closeBtn.onClick.RemoveAllListeners();
//                 closeBtn.onClick.AddListener(CloseResult);
//             }
//         }

//         EnsureSceneButtons(mainFrame);
//     }

//     // Decorative (non-animated) leaderboard elements, cleared together with the rows.
//     private readonly List<GameObject> _dynamicDecor = new List<GameObject>();
//     // Rounded button sprite reused for the name plate, pulled from the existing hand-made buttons.
//     private Sprite _roundedSprite;

//     static readonly Color LeaderLabelColor = new Color(1f, 0.92f, 0.5f, 1f);  // gold ROUNDS / TOTAL headers
//     static readonly Color NameBoxColor = new Color(0.36f, 0.20f, 0.10f, 1f);  // brown name plate

//     /// <summary>Number of round rows materialised as persistent, editable scene objects.</summary>
//     const int StaticLeaderboardRows = 5;

//     /// <summary>Builds the editable leaderboard skeleton once if it isn't already in the hierarchy.</summary>
//     void EnsureStaticLeaderboard(Transform container)
//     {
//         if (container == null) return;
//         if (container.Find("HeaderRow") == null)
//             BuildStaticLeaderboard(container, StaticLeaderboardRows);

//         // Additive, non-destructive: older scenes that already have a HeaderRow but no persistent
//         // TOTAL row (it used to be runtime-only) get the editable TotalsRow created once here.
//         if (container.Find("TotalsRow") == null)
//             BuildEditableTotalsRow(container, ComputeInnerWidth(container), 64f);
//     }

//     /// <summary>
//     /// Creates the persistent, hand-editable "TotalsRow" GameObject (6 TMP cells: TOTAL + 4 player
//     /// totals + grand total). Default styling is applied ONCE on creation so it looks correct out of
//     /// the box; afterwards the runtime fill never restyles it, so your Inspector font/color edits show
//     /// in game exactly as authored.
//     /// </summary>
//     void BuildEditableTotalsRow(Transform container, float innerW, float rowH)
//     {
//         if (container == null || container.Find("TotalsRow") != null) return;

//         string[] totalCells = new string[6];
//         totalCells[0] = "TOTAL";
//         for (int s = 1; s < 6; s++) totalCells[s] = "0";

//         GameObject rowGo = CreateScoreRow("TotalsRow", container, totalCells, innerW, rowH, ScoreDarkColor, true);
//         rowGo.transform.SetAsLastSibling();
//     }

//     /// <summary>
//     /// PUBLIC helper to materialise the editable TOTAL row into the scene at edit time without
//     /// rebuilding the rest of the skeleton (non-destructive). Resolves the panel/container itself,
//     /// so it can be invoked standalone (e.g. from a one-off editor command). Returns true if the row
//     /// now exists.
//     /// </summary>
//     public bool EnsureEditableTotalRow()
//     {
//         if (!ResolveResultPanel() || resultPanel == null)
//         {
//             Debug.LogError("[Result] EnsureEditableTotalRow — Panel_Winning / resultPanel could not be resolved.");
//             return false;
//         }

//         Transform mainFrame = resultPanel.transform.Find("MainFrame");
//         if (mainFrame == null) { Debug.LogError("[Result] EnsureEditableTotalRow — MainFrame missing."); return false; }

//         Transform container = scoreboardContainer != null ? scoreboardContainer : FindDeepChild(mainFrame, "PlayerRowsContainer");
//         if (container == null) { Debug.LogError("[Result] EnsureEditableTotalRow — PlayerRowsContainer missing."); return false; }

//         BuildEditableTotalsRow(container, ComputeInnerWidth(container), 64f);
//         return container.Find("TotalsRow") != null;
//     }

//     /// <summary>
//     /// Builds the persistent leaderboard skeleton: a header row (ROUNDS + four avatar/name columns +
//     /// TOTAL), <paramref name="rowCount"/> round rows (R1..Rn) and the two vertical dividers. These
//     /// objects are NOT tracked as dynamic, so they survive between shows and can be hand-edited in the
//     /// scene (positions, fonts, text, avatars, name plates). Runtime only fills the score cells.
//     /// </summary>
//     public void BuildStaticLeaderboard(Transform container, int rowCount)
//     {
//         if (container == null) return;
//         ResolveThemeSprites();

//         float innerW = ComputeInnerWidth(container);
//         const float headerH = 120f;
//         const float rowH = 64f;

//         // Header: ROUNDS | avatar x4 (fixed) | TOTAL
//         CreateHeaderRow("HeaderRow", container, innerW, headerH);

//         // One editable row per round slot (blank until scores are filled at runtime).
//         for (int r = 1; r <= rowCount; r++)
//         {
//             string[] cells = new string[6];
//             cells[0] = "R" + r;
//             for (int s = 1; s < 6; s++) cells[s] = "";
//             CreateScoreRow("RoundRow_" + r, container, cells, innerW, rowH, TextWhiteColor, false);
//         }

//         // Persistent, EDITABLE "TOTAL" row. It now lives in the scene skeleton (exactly like the
//         // RoundRow_N rows) so its font / color / size can be hand-edited in the Inspector and will
//         // SURVIVE at runtime. The runtime fill (BuildOrUpdateTotalsRow) only writes the number
//         // values into this authored row — it never restyles it.
//         BuildEditableTotalsRow(container, innerW, rowH);

//         // Faint vertical dividers behind the table: after ROUNDS and before TOTAL.
//         BuildVerticalDividers(container, innerW);
//     }

//     float ComputeInnerWidth(Transform container)
//     {
//         var crt = container as RectTransform;
//         var vlg = container.GetComponent<VerticalLayoutGroup>();
//         float innerW = 1040f;
//         if (crt != null && crt.rect.width > 1f)
//         {
//             innerW = crt.rect.width;
//             if (vlg != null) innerW -= (vlg.padding.left + vlg.padding.right);
//         }
//         return innerW;
//     }

//     /// <summary>
//     /// Fills score values into the existing static round rows. Played rounds are filled, future rounds
//     /// stay blank (matching the mock-up). Extra rows are only created at runtime when a Friends match
//     /// runs past the static row count. The header row (avatars + names) is left untouched so it stays
//     /// exactly as authored in the scene.
//     /// </summary>
//     /// <summary>Fills round rows and the TOTAL row. Total-row formatting is locked to match normal rows.</summary>
//     public void UpdateLeaderboardUI(Transform container)
//     {
//         FillLeaderboardData(container);
//     }

//     void FillLeaderboardData(Transform container)
//     {
//         _dynamicRows.Clear();

//         // Task 6: FRIENDS 2v2 shows a 4-column board (ROUNDS | Team1 | Team2 | TOTAL). Each team
//         // header stacks its two partners' names (P1 over P3, P2 over P4) and every round/total value
//         // is that partnership's COMBINED dehlas. Bots/Online (1v1v1v1) keep the authored 6-column
//         // board untouched. The authored scene rows are simply hidden in friends mode and restored
//         // otherwise, so nothing in the scene is destroyed.
//         if (IsFriendsTeamMode())
//         {
//             SetAuthoredLeaderboardRowsActive(container, false);
//             BuildFriendsTeamLeaderboard(container);
//         }
//         else
//         {
//             SetAuthoredLeaderboardRowsActive(container, true);
//             FillIndividualLeaderboardRows(container);
//         }

//         // Authored column positions turant lock karo (HLG disable + ignoreLayout) taaki cells
//         // pehle frame mein hi apni jagah par baithein.
//         ApplyAllLeaderboardPositions(container);

//         // Unity's nested layout groups do not always solve in the same frame the panel is shown
//         // (rects are still zero-sized when the cells are created). Defer one frame, flush the canvas,
//         // then force an immediate rebuild so every column resolves to its share and lines up with the
//         // vertical dividers.
//         if (container is RectTransform containerRect)
//             StartCoroutine(RebuildLeaderboardLayout(containerRect));
//     }

//     /// <summary>
//     /// The original 1v1v1v1 fill: ROUNDS + four player columns + TOTAL, using the authored scene
//     /// rows (R1..Rn + TotalsRow). Overflow round rows are only created if a match runs past the
//     /// static row count.
//     /// </summary>
//     void FillIndividualLeaderboardRows(Transform container)
//     {
//         Transform header = container.Find("HeaderRow");
//         if (header != null) _dynamicRows.Add(header.gameObject);

//         int slots = maxRounds > 0 ? maxRounds : Mathf.Max(roundHistory.Count, 1);
//         int total = Mathf.Max(slots, StaticLeaderboardRows);

//         for (int r = 1; r <= total; r++)
//         {
//             Transform rowT = container.Find("RoundRow_" + r);
//             GameObject rowGo;
//             if (rowT != null)
//             {
//                 rowGo = rowT.gameObject;
//             }
//             else
//             {
//                 // Overflow round row (beyond the static rows) — created at runtime, cleared each show.
//                 string[] blank = new string[6];
//                 blank[0] = "R" + r;
//                 for (int s = 1; s < 6; s++) blank[s] = "";
//                 rowGo = CreateScoreRow("RoundRow_" + r, container, blank, ComputeInnerWidth(container), 64f, TextWhiteColor, false);
//                 _overflowRows.Add(rowGo);
//             }

//             rowGo.SetActive(true);
//             rowGo.transform.localScale = Vector3.one;
//             var rowCg = rowGo.GetComponent<CanvasGroup>();
//             if (rowCg != null) rowCg.alpha = 1f;
//             FillRowCells(rowGo, r);
//             _dynamicRows.Add(rowGo);
//         }

//         // Task 28: cumulative standings row so the actual ranking is visible at a glance.
//         BuildOrUpdateTotalsRow(container);
//     }

//     /// <summary>
//     /// Enables/disables the authored 6-column scene rows (HeaderRow, RoundRow_1..n, TotalsRow) and
//     /// their two authored vertical dividers. Friends mode hides them and draws its own 4-column board;
//     /// individual mode re-enables them. Never destroys — the authored layout must persist.
//     /// </summary>
//     void SetAuthoredLeaderboardRowsActive(Transform container, bool active)
//     {
//         if (container == null) return;

//         SetChildActiveByName(container, "HeaderRow", active);
//         SetChildActiveByName(container, "TotalsRow", active);
//         for (int r = 1; r <= StaticLeaderboardRows; r++)
//             SetChildActiveByName(container, "RoundRow_" + r, active);

//         // Authored 6-column dividers only (friends dividers are named "FriendsVDivider").
//         foreach (Transform child in container)
//             if (child.name == "VDivider") child.gameObject.SetActive(active);
//     }

//     static void SetChildActiveByName(Transform parent, string childName, bool active)
//     {
//         Transform t = parent.Find(childName);
//         if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
//     }

//     /// <summary>Forces the leaderboard layout to resolve after the rows have been generated.</summary>
//     IEnumerator RebuildLeaderboardLayout(RectTransform containerTransform)
//     {
//         // Wait one frame so the container/cell RectTransforms have valid sizes.
//         yield return null;
//         if (containerTransform == null) yield break;
//         Canvas.ForceUpdateCanvases();
//         UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(containerTransform);

//         // Ab jab VerticalLayoutGroup ne row heights set kar diye, cells ko final authored
//         // X/Y par re-apply karo (row height se vertical centering sahi ho jaye).
//         ApplyAllLeaderboardPositions(containerTransform);
//     }

//     /// <summary>
//     /// Task 28: builds a bottom "TOTAL" row showing each player's cumulative dehlas across all rounds,
//     /// highlighting the current leader. Tracked as an overflow row so it is rebuilt cleanly each show.
//     /// </summary>
//     void BuildOrUpdateTotalsRow(Transform container)
//     {
//         if (container == null) return;

//         int[] totals = new int[4];
//         foreach (RoundResult rr in roundHistory)
//             for (int s = 0; s < 4 && s < rr.dehlasPerSeat.Length; s++)
//                 totals[s] += rr.dehlasPerSeat[s];
//         int grand = totals[0] + totals[1] + totals[2] + totals[3];

//         string[] cellTexts = new string[6];
//         cellTexts[0] = "TOTAL";
//         for (int s = 0; s < 4; s++) cellTexts[s + 1] = totals[s].ToString();
//         cellTexts[5] = grand.ToString();

//         // Prefer the PERSISTENT, authored "TotalsRow" from the scene skeleton so your Inspector
//         // font / color / size survive. Only fall back to a runtime-created row if (very old scene)
//         // none exists yet.
//         Transform existing = container.Find("TotalsRow");
//         bool authored = existing != null;

//         GameObject rowGo = authored
//             ? existing.gameObject
//             : CreateScoreRow("TotalsRow", container, cellTexts, ComputeInnerWidth(container), 64f, ScoreDarkColor, true);

//         // Only the runtime fallback row is tracked for destruction. The authored row must persist.
//         if (!authored)
//             _overflowRows.Add(rowGo);

//         rowGo.transform.SetAsLastSibling();
//         rowGo.SetActive(true);
//         _dynamicRows.Add(rowGo);

//         var tmps = new List<TextMeshProUGUI>();
//         foreach (Transform child in rowGo.transform)
//         {
//             var t = child.GetComponent<TextMeshProUGUI>();
//             if (t != null) tmps.Add(t);
//         }
//         if (tmps.Count < 6) return;

//         for (int s = 0; s < 6; s++)
//         {
//             TextMeshProUGUI totalRowText = tmps[s];

//             // Always write the freshly computed value (rich-text-free) into the cell.
//             totalRowText.text = StripRichTextTags(cellTexts[s]);

//             // STYLING POLICY:
//             //  - Authored row  -> DO NOT touch font / size / color / alignment. Whatever you set in
//             //                     the Inspector is exactly what shows in game (your request).
//             //  - Fallback row  -> apply safe defaults so an unstyled runtime row still looks correct.
//             if (!authored)
//             {
//                 totalRowText.fontSize = LeaderboardCellFontSize;
//                 totalRowText.color = Color.black;
//                 totalRowText.enableAutoSizing = false;
//                 totalRowText.alignment = s == 0
//                     ? TextAlignmentOptions.MidlineLeft
//                     : TextAlignmentOptions.Center;
//             }
//         }
//     }

//     /// <summary>
//     /// Task 6 — Friends 2v2 only. Builds the whole 4-column board at runtime: ROUNDS | Team1 | Team2 |
//     /// TOTAL. Team1 = seats {0,2} (Player 1 over Player 3), Team2 = seats {1,3} (Player 2 over Player
//     /// 4). Each round cell is the partnership's COMBINED dehlas for that round, the TOTAL row is the
//     /// combined dehlas across all rounds. Everything is tracked as overflow so it is destroyed and
//     /// rebuilt cleanly each show. No-op in 1v1v1v1 (bots/online).
//     /// </summary>
//     void BuildFriendsTeamLeaderboard(Transform container)
//     {
//         if (container == null) return;

//         ResolveThemeSprites();
//         float innerW = ComputeInnerWidth(container);
//         const float headerH = 120f;
//         const float rowH = 64f;

//         // Header: ROUNDS | Team1 (P1 over P3) | Team2 (P2 over P4) | TOTAL
//         GameObject headerGo = NewRow("FriendsHeaderRow", container, innerW, headerH);
//         AddSideHeaderLabel(headerGo.transform, "ROUNDS", true, LeaderLabelColor, 30, FontStyles.Bold);
//         CreateFriendsTeamHeaderCell(headerGo.transform, 0, 2); // Team 1
//         CreateFriendsTeamHeaderCell(headerGo.transform, 1, 3); // Team 2
//         AddCellLabel(headerGo.transform, "TOTAL", LeaderLabelColor, 30, FontStyles.Bold);
//         AddRowDashedLine(headerGo, innerW, headerH);
//         RegisterFriendsRow(headerGo);

//         // Per-round combined team scores.
//         int slots = maxRounds > 0 ? maxRounds : Mathf.Max(roundHistory.Count, 1);
//         int totalRows = Mathf.Max(slots, StaticLeaderboardRows);
//         int grandA = 0, grandB = 0;

//         for (int r = 1; r <= totalRows; r++)
//         {
//             RoundResult round = roundHistory.Find(rr => rr.roundNumber == r);
//             string aVal = "", bVal = "", rowTotal = "";
//             if (round != null && round.dehlasPerSeat != null && round.dehlasPerSeat.Length >= 4)
//             {
//                 int a = round.dehlasPerSeat[0] + round.dehlasPerSeat[2];
//                 int b = round.dehlasPerSeat[1] + round.dehlasPerSeat[3];
//                 aVal = a.ToString();
//                 bVal = b.ToString();
//                 rowTotal = (a + b).ToString();
//                 grandA += a;
//                 grandB += b;
//             }

//             string[] cells = { "R" + r, aVal, bVal, rowTotal };
//             GameObject rowGo = CreateScoreRow("FriendsRoundRow_" + r, container, cells, innerW, rowH, Color.black, true);
//             StyleFriendsRowCells(rowGo, Color.black);
//             RegisterFriendsRow(rowGo);
//         }

//         // Combined TOTAL row.
//         string[] totalCells = { "TOTAL", grandA.ToString(), grandB.ToString(), (grandA + grandB).ToString() };
//         GameObject totalsGo = CreateScoreRow("FriendsTotalsRow", container, totalCells, innerW, rowH, ScoreDarkColor, true);
//         StyleFriendsRowCells(totalsGo, ScoreDarkColor);
//         RegisterFriendsRow(totalsGo);

//         // Two dividers matching the 4-column grid: after ROUNDS and before TOTAL.
//         BuildFriendsDividers(container, innerW);
//     }

//     /// <summary>One team header cell with two stacked player plates: top seat (e.g. Player 1) above,
//     /// bottom seat (e.g. Player 3) slightly below — each an avatar beside a brown name plate.</summary>
//     void CreateFriendsTeamHeaderCell(Transform rowParent, int topSeat, int bottomSeat)
//     {
//         var cell = new GameObject("FriendsTeamHeaderCell", typeof(RectTransform));
//         cell.transform.SetParent(rowParent, false);
//         MakeEqualColumn(cell);

//         CreateStackedNamePlate(cell.transform, topSeat, 28f);     // upper player
//         CreateStackedNamePlate(cell.transform, bottomSeat, -28f); // lower player
//     }

//     /// <summary>A small avatar + brown name plate mini-row at vertical offset <paramref name="y"/>.</summary>
//     void CreateStackedNamePlate(Transform cellParent, int seatIndex, float y)
//     {
//         var group = CreateRect("PlayerPlate", cellParent, new Vector2(0f, y), new Vector2(196f, 44f));

//         var avatarGo = CreateRect("AvatarImage", group.transform, new Vector2(-80f, 0f), new Vector2(40f, 40f));
//         var avatarImg = AddImage(avatarGo, Color.white);
//         avatarImg.preserveAspect = true;
//         avatarImg.raycastTarget = false;
//         Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
//         if (avatar != null) avatarImg.sprite = avatar;
//         else if (playerAvatarSprite != null) avatarImg.sprite = playerAvatarSprite;

//         var nameBox = CreateRect("NameBox", group.transform, new Vector2(22f, 0f), new Vector2(140f, 36f));
//         var nameImg = AddImage(nameBox, NameBoxColor);
//         if (_roundedSprite != null) { nameImg.sprite = _roundedSprite; nameImg.type = UnityEngine.UI.Image.Type.Sliced; }
//         nameImg.raycastTarget = false;

//         var nameTxt = AddTmp(nameBox.transform, GetSeatDisplayName(seatIndex), Color.white, 16, TextAlignmentOptions.Center, FontStyles.Bold);
//         nameTxt.rectTransform.anchorMin = Vector2.zero;
//         nameTxt.rectTransform.anchorMax = Vector2.one;
//         nameTxt.rectTransform.offsetMin = new Vector2(6, 2);
//         nameTxt.rectTransform.offsetMax = new Vector2(-6, -2);
//         nameTxt.overflowMode = TextOverflowModes.Ellipsis;
//         nameTxt.enableAutoSizing = true;
//         nameTxt.fontSizeMin = 9;
//         nameTxt.fontSizeMax = 16;
//     }

//     /// <summary>Locks every text cell in a runtime friends row to the shared cell style + a color.</summary>
//     void StyleFriendsRowCells(GameObject rowGo, Color color)
//     {
//         if (rowGo == null) return;
//         int i = 0;
//         foreach (Transform child in rowGo.transform)
//         {
//             var tmp = child.GetComponent<TextMeshProUGUI>();
//             if (tmp == null) continue;
//             ApplyCellStyle(tmp, i == 0); // index 0 = the ROUNDS/R#/TOTAL label (left aligned)
//             tmp.color = color;
//             i++;
//         }
//     }

//     /// <summary>Registers a runtime friends row for destruction-each-show and the reveal animation.</summary>
//     void RegisterFriendsRow(GameObject rowGo)
//     {
//         if (rowGo == null) return;
//         _overflowRows.Add(rowGo);
//         rowGo.transform.SetAsLastSibling();
//         rowGo.SetActive(true);
//         rowGo.transform.localScale = Vector3.one;
//         var cg = rowGo.GetComponent<CanvasGroup>();
//         if (cg != null) cg.alpha = 1f;
//         _dynamicRows.Add(rowGo);
//     }

//     /// <summary>Two faint vertical dividers for the friends 4-column grid: after ROUNDS, before TOTAL.</summary>
//     void BuildFriendsDividers(Transform container, float innerW)
//     {
//         var crt = container as RectTransform;
//         float h = (crt != null && crt.rect.height > 1f) ? crt.rect.height - 40f : 540f;
//         float col = innerW / 4f;
//         AddFriendsVerticalDivider(container, -innerW / 2f + col, h);        // after ROUNDS
//         AddFriendsVerticalDivider(container, -innerW / 2f + col * 3f, h);   // before TOTAL
//     }

//     void AddFriendsVerticalDivider(Transform container, float x, float height)
//     {
//         var go = CreateRect("FriendsVDivider", container, new Vector2(x, 0f), new Vector2(3f, height));
//         var le = go.AddComponent<LayoutElement>();
//         le.ignoreLayout = true;
//         var img = AddImage(go, new Color(0.12f, 0.06f, 0.02f, 0.45f));
//         img.raycastTarget = false;
//         go.transform.SetAsFirstSibling();
//         _overflowRows.Add(go); // destroyed/rebuilt each show like the friends rows
//     }

//     static string StripRichTextTags(string value)
//     {
//         if (string.IsNullOrEmpty(value)) return value;
//         return System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty);
//     }

//     /// <summary>Writes the round number, per-player dehla scores and the row total into a row's text cells.</summary>
//     void FillRowCells(GameObject rowGo, int roundNumber)
//     {
//         var cells = new List<TextMeshProUGUI>();
//         foreach (Transform child in rowGo.transform)
//         {
//             var tmp = child.GetComponent<TextMeshProUGUI>();
//             if (tmp != null) cells.Add(tmp);
//         }
//         if (cells.Count < 6) return;

//         RoundResult round = roundHistory.Find(rr => rr.roundNumber == roundNumber);
//         cells[0].text = "R" + roundNumber;
//         if (round != null)
//         {
//             for (int s = 0; s < 4; s++) cells[s + 1].text = FormatDehlaScore(round.dehlasPerSeat[s]);
//             cells[5].text = SumRound(round.dehlasPerSeat).ToString();
//         }
//         else
//         {
//             for (int s = 1; s < 6; s++) cells[s].text = "";
//         }

//         // EVERY round row (R1..R5), label + score values, uses pure BLACK text.
//         // No golden/green/brown per-round highlighting — every round is styled identically.
//         for (int s = 0; s < 6; s++)
//         {
//             ApplyCellStyle(cells[s], s == 0); // fixed size + Bold (shared with the TOTAL row)
//             cells[s].color = Color.black;     // black for every round score
//         }
//     }

//     /// <summary>
//     /// Large bold cells. The row label (ROUNDS / R1.. / TOTAL) is left-aligned like the header's
//     /// "ROUNDS" cell; every value cell is CENTER-aligned so each number sits exactly under its
//     /// centered player avatar (and the grand total under the centered "TOTAL" header).
//     /// </summary>
//     /// <summary>
//     /// Fixed font size shared by EVERY leaderboard cell (R1..Rn rows AND the TOTAL row) so they
//     /// render at an identical size/weight. Auto-sizing is intentionally disabled: because rows can
//     /// have different RectTransform heights, auto-sizing produced mismatched rendered sizes (the
//     /// TOTAL row looked bigger/smaller than the R1 row). Locking the size fixes that from code only.
//     /// </summary>
//     const int LeaderboardCellFontSize = 34;

//     void ApplyCellStyle(TextMeshProUGUI cell, bool isRowLabel)
//     {
//         if (cell == null) return;
//         // Lock size from code so the TOTAL row exactly matches the R1..Rn rows.
//         cell.enableAutoSizing = false;               // deterministic: no height-driven size drift
//         cell.fontSize = LeaderboardCellFontSize;
//         cell.overflowMode = TextOverflowModes.Overflow;
//         cell.fontStyle = FontStyles.Normal;          // preserve hierarchy look — never bold at runtime
//         if (isRowLabel)
//         {
//             cell.alignment = TextAlignmentOptions.MidlineLeft;
//             cell.margin = new Vector4(18f, 0f, 0f, 0f);
//         }
//         else
//         {
//             cell.alignment = TextAlignmentOptions.Center;
//             cell.margin = Vector4.zero;
//         }
//     }

//     // ============================================================
//     // AUTHORED COLUMN POSITIONS (winning panel / leaderboard)
//     //
//     // Har row par HorizontalLayoutGroup + LayoutRebuilder runtime par cells ki
//     // manual positions ko reset kar deta hai. In values ko code se lock karke
//     // (LayoutElement.ignoreLayout + HLG disable) hum ensure karte hain ke panel
//     // har mode (Bots / Online 1v1v1v1 aur Friends 2v2) mein same aligned rahe.
//     //
//     // Values user ne di thi:
//     //   HeaderRow  : ROUNDS X=40 Y=-60 | Player1..4 X=340/550/760/970 Y=-50
//     //   Data rows  : Player1..4 X=340/550/760/970 (R1..R5 label left me)
//     // TOTAL column X=1180 (players ke consistent 210px spacing se derived).
//     // ============================================================
//     const float LbRoundsColumnX = 40f;
//     const float LbTotalColumnX = 1180f;
//     static readonly float[] LbPlayerColumnX = { 340f, 550f, 760f, 970f };
//     // Friends 2v2 (4-col): ROUNDS | Team1 | Team2 | TOTAL — teams apne 2-player span ke center par.
//     static readonly float[] LbFriendsColumnX = { 40f, 445f, 865f, 1180f };
//     const float LbHeaderRoundsY = -60f;
//     const float LbHeaderPlayerY = -50f;
//     const float LbHeaderHeight = 120f;
//     const float LbDataRowHeight = 64f;

//     /// <summary>Row ke score/header cells order mein (dashed lines / dividers skip karke).</summary>
//     static List<RectTransform> GetOrderedLeaderboardCells(Transform row)
//     {
//         var list = new List<RectTransform>();
//         if (row == null) return list;
//         for (int i = 0; i < row.childCount; i++)
//         {
//             Transform c = row.GetChild(i);
//             if (c.name == "Cell" || c.name == "PlayerHeaderCell" || c.name == "FriendsTeamHeaderCell")
//                 list.Add(c as RectTransform);
//         }
//         return list;
//     }

//     /// <summary>Ek cell ko fixed anchor (top-left) par lock karta hai taaki layout group use hila na sake.</summary>
//     static void LockLeaderboardCell(RectTransform rt, float x, float y, Vector2 size, Vector2 pivot)
//     {
//         if (rt == null) return;

//         var le = rt.GetComponent<LayoutElement>();
//         if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
//         le.ignoreLayout = true;

//         rt.anchorMin = new Vector2(0f, 1f);
//         rt.anchorMax = new Vector2(0f, 1f);
//         rt.pivot = pivot;
//         rt.sizeDelta = size;
//         rt.anchoredPosition = new Vector2(x, y);
//     }

//     /// <summary>Poore container (header + saari rows) ke cells ko authored X/Y par set karta hai.</summary>
//     void ApplyAllLeaderboardPositions(Transform container)
//     {
//         if (container == null) return;

//         foreach (Transform row in container)
//         {
//             string nm = row.name;
//             if (nm == "HeaderRow" || nm == "FriendsHeaderRow")
//                 ApplyLeaderboardRowLayout(row, isHeader: true);
//             else if (nm.StartsWith("RoundRow_") || nm == "TotalsRow"
//                      || nm.StartsWith("FriendsRoundRow_") || nm == "FriendsTotalsRow")
//                 ApplyLeaderboardRowLayout(row, isHeader: false);
//         }
//     }

//     void ApplyLeaderboardRowLayout(Transform row, bool isHeader)
//     {
//         if (row == null) return;

//         // HLG band karo warna ye cells ko wapas equal-columns par kheench dega.
//         var hlg = row.GetComponent<HorizontalLayoutGroup>();
//         if (hlg != null) hlg.enabled = false;

//         List<RectTransform> cells = GetOrderedLeaderboardCells(row);
//         int n = cells.Count;
//         if (n < 2) return;

//         var rowRt = row as RectTransform;
//         float rowH = (rowRt != null && rowRt.rect.height > 1f)
//             ? rowRt.rect.height
//             : (isHeader ? LbHeaderHeight : LbDataRowHeight);

//         // Individual (6-col) vs Friends (4-col) column X list.
//         bool friends = n <= 4;

//         for (int i = 0; i < n; i++)
//         {
//             RectTransform cell = cells[i];
//             bool isRoundsCol = (i == 0);
//             bool isTotalCol = (i == n - 1);

//             float x;
//             if (friends)
//                 x = LbFriendsColumnX[Mathf.Clamp(i, 0, LbFriendsColumnX.Length - 1)];
//             else if (isRoundsCol)
//                 x = LbRoundsColumnX;
//             else if (isTotalCol)
//                 x = LbTotalColumnX;
//             else
//                 x = LbPlayerColumnX[Mathf.Clamp(i - 1, 0, LbPlayerColumnX.Length - 1)];

//             float y;
//             Vector2 pivot;
//             Vector2 size;

//             if (isHeader)
//             {
//                 if (isRoundsCol)
//                 {
//                     y = LbHeaderRoundsY;
//                     pivot = new Vector2(0f, 0.5f);      // left pivot → X = left edge
//                     size = new Vector2(260f, 80f);
//                 }
//                 else if (isTotalCol)
//                 {
//                     y = LbHeaderRoundsY;
//                     pivot = new Vector2(0.5f, 0.5f);
//                     size = new Vector2(240f, 80f);
//                 }
//                 else
//                 {
//                     y = LbHeaderPlayerY;
//                     pivot = new Vector2(0.5f, 0.5f);    // avatar+name plate cell centered on X
//                     size = new Vector2(180f, LbHeaderHeight);
//                 }
//             }
//             else
//             {
//                 y = -rowH / 2f;                         // vertically centered in the row
//                 if (isRoundsCol)
//                 {
//                     pivot = new Vector2(0f, 0.5f);
//                     size = new Vector2(220f, rowH);
//                 }
//                 else
//                 {
//                     pivot = new Vector2(0.5f, 0.5f);
//                     size = new Vector2(180f, rowH);
//                 }
//             }

//             LockLeaderboardCell(cell, x, y, size, pivot);

//             // Text alignment: R1..R5 / ROUNDS label LEFT, baaki cells center.
//             var tmp = cell.GetComponent<TextMeshProUGUI>();
//             if (tmp != null)
//             {
//                 tmp.enableWordWrapping = false;
//                 tmp.overflowMode = TextOverflowModes.Overflow;
//                 tmp.margin = Vector4.zero;
//                 tmp.alignment = isRoundsCol
//                     ? TextAlignmentOptions.MidlineLeft
//                     : TextAlignmentOptions.Center;
//             }
//         }
//     }

// #if UNITY_EDITOR
//     /// <summary>
//     /// Editor helper: clears and regenerates the static leaderboard skeleton under PlayerRowsContainer
//     /// so it can be hand-edited in the scene. Right-click the ResultManager component → this menu item.
//     /// </summary>
//     [ContextMenu("Rebuild Static Leaderboard")]
//     void RebuildStaticLeaderboardEditor()
//     {
//         if (!ResolveResultPanel() || resultPanel == null)
//         {
//             Debug.LogError("[Result] Cannot build — Panel_Winning / resultPanel could not be resolved.");
//             return;
//         }
//         Transform mainFrame = resultPanel.transform.Find("MainFrame");
//         if (mainFrame == null) { Debug.LogError("[Result] MainFrame missing under Panel_Winning."); return; }
//         Transform container = scoreboardContainer != null ? scoreboardContainer : FindDeepChild(mainFrame, "PlayerRowsContainer");
//         if (container == null) { Debug.LogError("[Result] PlayerRowsContainer missing under MainFrame."); return; }

//         for (int i = container.childCount - 1; i >= 0; i--)
//             DestroyImmediate(container.GetChild(i).gameObject);

//         BuildStaticLeaderboard(container, StaticLeaderboardRows);
//         UnityEditor.EditorUtility.SetDirty(this);
//         UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
//         Debug.Log("[Result] Static leaderboard rebuilt under PlayerRowsContainer.");
//     }
// #endif

//     /// <summary>Caches the rounded button sprite from the existing buttons so the name plate gets rounded corners in builds too.</summary>
//     void ResolveThemeSprites()
//     {
//         if (resultPanel == null || _roundedSprite != null) return;
//         var btnImg = FindImageDeep(resultPanel.transform, "HomeButton")
//                   ?? FindImageDeep(resultPanel.transform, "RestartButton")
//                   ?? FindImageDeep(resultPanel.transform, "NameBox")
//                   ?? FindImageDeep(resultPanel.transform, "CloseButton");
//         if (btnImg != null) _roundedSprite = btnImg.sprite;
//     }

//     static UnityEngine.UI.Image FindImageDeep(Transform root, string name)
//     {
//         Transform t = FindDeepByName(root, name);
//         return t != null ? t.GetComponent<UnityEngine.UI.Image>() : null;
//     }

//     static Transform FindDeepByName(Transform parent, string name)
//     {
//         if (parent.name == name) return parent;
//         for (int i = 0; i < parent.childCount; i++)
//         {
//             Transform r = FindDeepByName(parent.GetChild(i), name);
//             if (r != null) return r;
//         }
//         return null;
//     }

//     /// <summary>Header row with a "ROUNDS" label, four fixed-avatar player columns and a "TOTAL" label.</summary>
//     GameObject CreateHeaderRow(string name, Transform parent, float width, float height)
//     {
//         var rowGo = NewRow(name, parent, width, height);
//         AddSideHeaderLabel(rowGo.transform, "ROUNDS", alignLeft: true, LeaderLabelColor, 30, FontStyles.Bold);
//         for (int s = 0; s < 4; s++) CreateAvatarHeaderCell(rowGo.transform, s);
//         AddCellLabel(rowGo.transform, "TOTAL", LeaderLabelColor, 30, FontStyles.Bold);
//         AddRowDashedLine(rowGo, width, height);
//         return rowGo;
//     }

//     /// <summary>ROUNDS / TOTAL header text nudged toward the outer edge so it clears the column dividers.</summary>
//     void AddSideHeaderLabel(Transform rowParent, string text, bool alignLeft, Color color, int maxSize, FontStyles style)
//     {
//         var cellGo = new GameObject("Cell", typeof(RectTransform));
//         cellGo.transform.SetParent(rowParent, false);
//         MakeEqualColumn(cellGo);
//         var tmp = cellGo.AddComponent<TextMeshProUGUI>();
//         tmp.text = text;
//         tmp.color = color;
//         tmp.alignment = alignLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
//         tmp.fontStyle = style;
//         tmp.enableAutoSizing = true;
//         tmp.fontSizeMin = 12;
//         tmp.fontSizeMax = maxSize;
//         tmp.overflowMode = TextOverflowModes.Ellipsis;
//         tmp.raycastTarget = false;
//         if (customFont != null) tmp.font = customFont;

//         var rt = tmp.rectTransform;
//         rt.anchorMin = Vector2.zero;
//         rt.anchorMax = Vector2.one;
//         const float edgePad = 18f;
//         if (alignLeft)
//             rt.offsetMin = new Vector2(edgePad, 0);
//         else
//             rt.offsetMax = new Vector2(-edgePad, 0);
//     }

//     /// <summary>One player column: the fixed avatar portrait on top, a brown name plate beneath.</summary>
//     void CreateAvatarHeaderCell(Transform rowParent, int seatIndex)
//     {
//         var cell = new GameObject("PlayerHeaderCell", typeof(RectTransform));
//         cell.transform.SetParent(rowParent, false);

//         // Player profile avatar (same index / Photon sync as in-game seats).
//         var avatarGo = CreateRect("AvatarImage", cell.transform, new Vector2(0, 26f), new Vector2(82, 82));
//         var avatarImg = AddImage(avatarGo, Color.white);
//         avatarImg.preserveAspect = true;
//         avatarImg.raycastTarget = false;
//         Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
//         if (avatar != null)
//             avatarImg.sprite = avatar;
//         else if (playerAvatarSprite != null)
//             avatarImg.sprite = playerAvatarSprite;

//         // Brown name plate.
//         var nameBox = CreateRect("NameBox", cell.transform, new Vector2(0, -36f), new Vector2(150, 34));
//         var nameImg = AddImage(nameBox, NameBoxColor);
//         if (_roundedSprite != null) { nameImg.sprite = _roundedSprite; nameImg.type = UnityEngine.UI.Image.Type.Sliced; }
//         nameImg.raycastTarget = false;

//         var nameTxt = AddTmp(nameBox.transform, GetSeatDisplayName(seatIndex), Color.white, 18, TextAlignmentOptions.Center, FontStyles.Bold);
//         nameTxt.rectTransform.anchorMin = Vector2.zero;
//         nameTxt.rectTransform.anchorMax = Vector2.one;
//         nameTxt.rectTransform.offsetMin = new Vector2(8, 2);
//         nameTxt.rectTransform.offsetMax = new Vector2(-8, -2);
//         nameTxt.overflowMode = TextOverflowModes.Ellipsis;
//         nameTxt.enableAutoSizing = true;
//         nameTxt.fontSizeMin = 10;
//         nameTxt.fontSizeMax = 18;
//     }

//     /// <summary>A data row of evenly-distributed text cells (ROUND | values | TOTAL) with a dashed line beneath.</summary>
//     GameObject CreateScoreRow(string name, Transform parent, string[] cells, float width, float height, Color color, bool bold)
//     {
//         var rowGo = NewRow(name, parent, width, height);
//         for (int i = 0; i < cells.Length; i++)
//             AddCellLabel(rowGo.transform, cells[i], color, bold ? 30 : 28, bold ? FontStyles.Bold : FontStyles.Normal);
//         AddRowDashedLine(rowGo, width, height);
//         return rowGo;
//     }

//     /// <summary>Creates the row container: CanvasGroup (for the reveal animation) + an even 6-column HorizontalLayoutGroup.</summary>
//     GameObject NewRow(string name, Transform parent, float width, float height)
//     {
//         var rowGo = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
//         rowGo.transform.SetParent(parent, false);
//         var rrt = rowGo.GetComponent<RectTransform>();
//         rrt.sizeDelta = new Vector2(width, height);

//         var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
//         hlg.childControlWidth = true;
//         hlg.childControlHeight = true;
//         hlg.childForceExpandWidth = true;
//         hlg.childForceExpandHeight = true;
//         hlg.childAlignment = TextAnchor.MiddleCenter;

//         var le = rowGo.AddComponent<LayoutElement>();
//         le.preferredHeight = height;
//         le.minHeight = height;
//         le.preferredWidth = width;
//         return rowGo;
//     }

//     /// <summary>
//     /// Forces a HorizontalLayoutGroup child to take an equal 1/N share of the row width so every
//     /// column lines up exactly with the fixed innerW/6 vertical dividers. Without this, cells size to
//     /// their content (text preferred width / collapsed avatars) and clump toward the centre.
//     /// </summary>
//     static void MakeEqualColumn(GameObject cell)
//     {
//         var le = cell.GetComponent<LayoutElement>();
//         if (le == null) le = cell.AddComponent<LayoutElement>();
//         le.minWidth = 0f;
//         le.preferredWidth = 0f;
//         le.flexibleWidth = 1f;
//     }

//     /// <summary>A single centered text cell inside a row's HorizontalLayoutGroup.</summary>
//     void AddCellLabel(Transform rowParent, string text, Color color, int maxSize, FontStyles style)
//     {
//         var cellGo = new GameObject("Cell", typeof(RectTransform));
//         cellGo.transform.SetParent(rowParent, false);
//         MakeEqualColumn(cellGo);
//         var tmp = cellGo.AddComponent<TextMeshProUGUI>();
//         tmp.text = text;
//         tmp.color = color;
//         tmp.alignment = TextAlignmentOptions.Center;
//         tmp.fontStyle = style;
//         tmp.enableAutoSizing = true;
//         tmp.fontSizeMin = 12;
//         tmp.fontSizeMax = maxSize;
//         tmp.overflowMode = TextOverflowModes.Ellipsis;
//         tmp.raycastTarget = false;
//         if (customFont != null) tmp.font = customFont;
//     }

//     /// <summary>Adds a horizontal dashed ledger line pinned to the bottom edge of a row (ignored by layout).</summary>
//     void AddRowDashedLine(GameObject rowGo, float width, float height)
//     {
//         var line = CreateDashedLine("DashedLine_Row", rowGo.transform, width - 8f, new Vector2(0f, -height / 2f + 2f), true);
//         var lrt = line.GetComponent<RectTransform>();
//         lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
//         lrt.pivot = new Vector2(0.5f, 0.5f);
//         lrt.anchoredPosition = new Vector2(0f, -height / 2f + 2f);
//     }

//     /// <summary>Two faint vertical dividers behind the table separating ROUNDS | players | TOTAL.</summary>
//     void BuildVerticalDividers(Transform container, float innerW)
//     {
//         var crt = container as RectTransform;
//         float h = (crt != null && crt.rect.height > 1f) ? crt.rect.height - 40f : 540f;
//         float col = innerW / 6f;
//         AddVerticalDivider(container, -innerW / 2f + col, h);       // after ROUNDS
//         AddVerticalDivider(container, -innerW / 2f + col * 5f, h);  // before TOTAL
//     }

//     void AddVerticalDivider(Transform container, float x, float height)
//     {
//         var go = CreateRect("VDivider", container, new Vector2(x, 0f), new Vector2(3f, height));
//         var le = go.AddComponent<LayoutElement>();
//         le.ignoreLayout = true;
//         var img = AddImage(go, new Color(0.12f, 0.06f, 0.02f, 0.45f));
//         img.raycastTarget = false;
//         go.transform.SetAsFirstSibling();
//         _dynamicDecor.Add(go);
//     }

//     void ClearDynamicMainFrameContent(Transform mainFrame)
//     {
//         if (mainFrame == null) return;

//         var toDestroy = new List<GameObject>();
//         foreach (Transform child in mainFrame)
//         {
//             string childName = child.name;
//             if (childName == "ScorecardTable" || childName == "CloseButton"
//                 || childName == "RoundScrollView" || childName == "ScorecardHeader"
//                 || childName == "RoundTitle")
//                 toDestroy.Add(child.gameObject);
//             else if (childName.StartsWith("SeparatorLine_") || childName.StartsWith("DashedLine_"))
//                 toDestroy.Add(child.gameObject);
//         }

//         foreach (GameObject go in toDestroy)
//             DestroyObjectSafe(go);
//     }

//     void EnsureSceneButtons(Transform mainFrame)
//     {
//         // Action buttons (HOME / RESTART) are intentionally removed from the result panel:
//         // at match end the leaderboard auto-returns to the Home screen after
//         // MatchEndLeaderboardSeconds. If the ButtonsContainer has been deleted from the
//         // scene we do NOT recreate it (no fallback buttons).
//         Transform btnContainer = mainFrame.Find("ButtonsContainer");
//         if (btnContainer == null) return;

//         WireSceneButton(ref restartButton, btnContainer, "RestartButton", OnRestartClicked);
//         WireSceneButton(ref homeButton, btnContainer, "HomeButton", OnHomeClicked);
//     }

//     void WireSceneButton(ref Button btn, Transform container, string childName, UnityEngine.Events.UnityAction action)
//     {
//         if (btn == null)
//         {
//             Transform existing = container.Find(childName);
//             if (existing != null)
//                 btn = existing.GetComponent<Button>();
//         }

//         if (btn == null)
//         {
//             Color fallbackColor = childName == "RestartButton"
//                 ? new Color(0.12f, 0.65f, 0.28f)
//                 : new Color(0.85f, 0.35f, 0.15f);
//             string label = childName == "RestartButton" ? "PLAY AGAIN" : "HOME";
//             EnsureFallbackButton(ref btn, container, childName, label, fallbackColor, action);
//             return;
//         }

//         if (btn.transform.parent != container)
//             btn.transform.SetParent(container, false);

//         EnableButtonVisuals(btn);
//         btn.onClick.RemoveAllListeners();
//         btn.onClick.AddListener(action);
//     }

//     static void EnableButtonVisuals(Button btn)
//     {
//         if (btn == null) return;
//         btn.enabled = true;
//         btn.interactable = true;
//         btn.gameObject.SetActive(true);

//         var img = btn.GetComponent<Image>();
//         if (img != null)
//             img.enabled = true;
//     }

//     void EnsureFallbackButton(ref Button btn, Transform parent, string goName, string label, Color bgColor, UnityEngine.Events.UnityAction action)
//     {
//         var go = CreateRect(goName, parent, Vector2.zero, new Vector2(280, 90));
//         AddImage(go, bgColor);
//         btn = go.AddComponent<Button>();
//         btn.targetGraphic = go.GetComponent<Image>();
//         var newTmp = AddTmp(go.transform, label, Color.white, 30, TextAlignmentOptions.Center, FontStyles.Bold);
//         newTmp.rectTransform.sizeDelta = new Vector2(280, 90);

//         btn.onClick.RemoveAllListeners();
//         btn.onClick.AddListener(action);

//         var colors = btn.colors;
//         colors.highlightedColor = bgColor * 1.2f;
//         colors.pressedColor = bgColor * 0.8f;
//         btn.colors = colors;
//     }

//     GameObject CreateColumn(string name, Transform parent, float width, float contentHeight, float spacing)
//     {
//         var col = CreateRect(name, parent, Vector2.zero, new Vector2(width, contentHeight));
//         col.AddComponent<CanvasGroup>(); // For fading/animation
//         var vlg = col.AddComponent<VerticalLayoutGroup>();
//         vlg.childAlignment = TextAnchor.UpperCenter;
//         vlg.spacing = spacing;
//         vlg.childControlWidth = false;
//         vlg.childControlHeight = false;
//         vlg.childForceExpandWidth = false;
//         vlg.childForceExpandHeight = false;
//         return col;
//     }

//     void CreateLabelCell(string text, Transform parent, float width, float height, bool isBold)
//     {
//         var cell = CreateRect("LabelCell", parent, Vector2.zero, new Vector2(width, height));
//         // Push label towards the bottom so it aligns with name boxes
//         var txt = AddTmp(cell.transform, text, Color.white, 28, TextAlignmentOptions.Bottom, isBold ? FontStyles.Bold : FontStyles.Normal);
//         txt.rectTransform.anchoredPosition = new Vector2(0, -25f);
//         txt.rectTransform.sizeDelta = new Vector2(width, height);
//     }

//     void CreatePlayerHeaderCell(int seatIndex, Transform parent, float width, float height)
//     {
//         var cell = CreateRect("PlayerHeaderCell", parent, Vector2.zero, new Vector2(width, height));

//         // 1. Avatar Border/Frame (Circle)
//         var avatarFrameGo = CreateRect("AvatarFrame", cell.transform, new Vector2(0, 35f), new Vector2(75, 75));
//         var borderImg = AddImage(avatarFrameGo, new Color(0.35f, 0.22f, 0.12f, 0.85f));
//         Sprite circleSprite = null;
// #if UNITY_EDITOR
//         circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
// #endif
//         if (circleSprite != null) borderImg.sprite = circleSprite;

//         // 2. Avatar Inside Image
//         var avatarImgGo = CreateRect("AvatarImage", avatarFrameGo.transform, Vector2.zero, new Vector2(68, 68));
//         var avatarImg = AddImage(avatarImgGo, Color.white);
//         avatarImg.sprite = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
//         avatarImg.preserveAspect = true;

//         // 3. Name BG Box (Brown Rounded)
//         var nameBoxGo = CreateRect("NameBox", cell.transform, new Vector2(0, -25f), new Vector2(140, 32));
//         var nameBoxImg = AddImage(nameBoxGo, new Color(0.35f, 0.22f, 0.12f, 1f));
//         if (woodBoardSprite != null)
//         {
//             nameBoxImg.sprite = woodBoardSprite;
//             nameBoxImg.type = Image.Type.Simple;
//         }

//         // 4. Name Text
//         string name = GetSeatDisplayName(seatIndex);
//         var nameTxt = AddTmp(nameBoxGo.transform, name, Color.white, 16, TextAlignmentOptions.Center, FontStyles.Bold);
//         nameTxt.rectTransform.anchoredPosition = Vector2.zero;
//         nameTxt.rectTransform.sizeDelta = new Vector2(130, 26);
//         nameTxt.overflowMode = TextOverflowModes.Ellipsis;
//     }

//     void CreateValueCell(string text, Transform parent, float width, float height, bool isBold, bool isWinner = false, bool isCurrentRound = false)
//     {
//         var cell = CreateRect("ValueCell", parent, Vector2.zero, new Vector2(width, height));
        
//         // NOTE: this helper is NOT used by the live leaderboard (BuildResultPanelUI ->
//         // FillRowCells / BuildOrUpdateTotalsRow). The golden "winner" highlight and green
//         // "current round" color have been removed so NO code path can reintroduce golden
//         // score text. isWinner / isCurrentRound are kept only for signature compatibility.
//         Color textColor = Color.black;                 // all values black — no golden, no green
//         int fontSize = LeaderboardCellFontSize;        // same fixed size as the live rows
//         FontStyles style = FontStyles.Bold;            // matches the R1 / TOTAL weight

//         var txt = AddTmp(cell.transform, text, textColor, fontSize, TextAlignmentOptions.Center, style);
//         txt.rectTransform.anchoredPosition = Vector2.zero;
//         txt.rectTransform.sizeDelta = new Vector2(width, height);
//     }

//     void CreateCloseButton(Transform parent)
//     {
//         var btnGo = CreateRect("CloseButton", parent, new Vector2(540, 330), new Vector2(64, 64));
        
//         var bgImg = AddImage(btnGo, new Color(0.35f, 0.22f, 0.12f, 1f));
//         Sprite circleSprite = null;
// #if UNITY_EDITOR
//         circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
// #endif
//         if (circleSprite != null) bgImg.sprite = circleSprite;
        
//         var btn = btnGo.AddComponent<Button>();
//         btn.targetGraphic = bgImg;
//         btn.onClick.AddListener(CloseResult);

//         var txt = AddTmp(btnGo.transform, "X", Color.white, 26, TextAlignmentOptions.Center, FontStyles.Bold);
//         txt.rectTransform.anchoredPosition = Vector2.zero;
//         txt.rectTransform.sizeDelta = new Vector2(50, 50);
        
//         var colors = btn.colors;
//         colors.normalColor = new Color(0.35f, 0.22f, 0.12f, 1f);
//         colors.highlightedColor = new Color(0.5f, 0.3f, 0.15f, 1f);
//         colors.pressedColor = new Color(0.2f, 0.1f, 0.05f, 1f);
//         btn.colors = colors;
//     }

//     Sprite GetAvatarSprite(int actorNumber)
//     {
//         Sprite[] pool = GetProfileSpritePool();
//         if (pool == null || pool.Length == 0) return null;

//         int spriteIndex = ResolveAvatarIndexForActor(actorNumber);
//         if (spriteIndex < 0 || spriteIndex >= pool.Length)
//             spriteIndex = Mathf.Abs(actorNumber) % pool.Length;

//         return pool[spriteIndex];
//     }

//     static Sprite[] GetProfileSpritePool()
//     {
//         if (PlayerProfileManager.Instance != null &&
//             PlayerProfileManager.Instance.profileSprites != null &&
//             PlayerProfileManager.Instance.profileSprites.Length > 0)
//             return PlayerProfileManager.Instance.profileSprites;

//         if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
//             return MatchmakingManager.GlobalProfileSprites.ToArray();

//         return null;
//     }

//     static int ResolveAvatarIndexForActor(int actorNumber)
//     {
//         if (PhotonNetwork.LocalPlayer != null && actorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
//         {
//             int local = PlayerProfileManager.GetSavedAvatarIndex();
//             if (local >= 0) return local;
//         }

//         if (PhotonNetwork.CurrentRoom != null)
//         {
//             Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);
//             if (p != null && p.CustomProperties != null &&
//                 p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val))
//             {
//                 if (val != null)
//                 {
//                     if (val is int vi) return vi;
//                     if (int.TryParse(val.ToString(), out int parsed)) return parsed;
//                 }
//             }
//         }

//         return -1;
//     }

//     GameObject CreateRect(string name, Transform parent, Vector2 pos, Vector2 size)
//     {
//         var go = new GameObject(name, typeof(RectTransform));
//         go.transform.SetParent(parent, false);
//         var rt = go.GetComponent<RectTransform>();
//         rt.sizeDelta = size;
//         rt.anchoredPosition = pos;
//         return go;
//     }

//     // ============================================================
//     // LEDGER LINES & BANNER (1v1v1v1 leaderboard mockup)
//     // ============================================================

//     static readonly Color LedgerLineColor = new Color(0.12f, 0.06f, 0.02f, 0.6f);

//     /// <summary>Builds a horizontal dashed line from small dash segments (no sprite needed).</summary>
//     GameObject CreateDashedLine(string name, Transform parent, float width, Vector2 anchoredPos, bool ignoreLayout)
//     {
//         var line = CreateRect(name, parent, anchoredPos, new Vector2(width, 4f));
//         if (ignoreLayout)
//         {
//             var le = line.AddComponent<LayoutElement>();
//             le.ignoreLayout = true; // keep parent layout groups from repositioning the line
//         }

//         const float dashW = 20f;
//         const float gap = 14f;
//         const float step = dashW + gap;
//         int count = Mathf.Max(1, Mathf.FloorToInt(width / step));
//         float used = (count * step) - gap;
//         float startX = (-used / 2f) + (dashW / 2f);

//         for (int i = 0; i < count; i++)
//         {
//             var dash = CreateRect("Dash", line.transform, new Vector2(startX + (i * step), 0f), new Vector2(dashW, 4f));
//             var img = AddImage(dash, LedgerLineColor);
//             img.raycastTarget = false;
//         }
//         return line;
//     }

//     /// <summary>Solid vertical column divider spanning [bottomY, topY] at the given x (board-local).</summary>
//     void CreateVerticalSeparator(string name, Transform parent, float x, float topY, float bottomY)
//     {
//         float h = Mathf.Abs(topY - bottomY);
//         float cy = (topY + bottomY) / 2f;
//         var line = CreateRect(name, parent, new Vector2(x, cy), new Vector2(3f, h));
//         var img = AddImage(line, new Color(LedgerLineColor.r, LedgerLineColor.g, LedgerLineColor.b, 0.5f));
//         img.raycastTarget = false;
//     }

//     /// <summary>
//     /// Full-width banner ad placeholder pinned to the bottom of the screen (below the board).
//     /// Lives on the full-screen result panel so it stretches edge-to-edge. Reused across rebuilds.
//     /// </summary>
//     void CreateBannerAd()
//     {
//         if (resultPanel == null) return;

//         Transform existing = resultPanel.transform.Find("BannerAdPlacement");
//         GameObject banner = existing != null ? existing.gameObject : null;
//         if (banner == null)
//         {
//             banner = new GameObject("BannerAdPlacement", typeof(RectTransform));
//             banner.transform.SetParent(resultPanel.transform, false);
//         }

//         var rt = banner.GetComponent<RectTransform>();
//         rt.anchorMin = new Vector2(0f, 0f);
//         rt.anchorMax = new Vector2(1f, 0f);
//         rt.pivot = new Vector2(0.5f, 0f);
//         rt.offsetMin = new Vector2(0f, 0f);
//         rt.offsetMax = new Vector2(0f, BannerAdHeightPx); // full width banner band, flush to screen bottom (Task 15)

//         ResolveThemeSprites();
//         var bg = AddImage(banner, new Color(0.96f, 0.94f, 0.90f, 0.97f));
//         if (_roundedSprite != null) { bg.sprite = _roundedSprite; bg.type = UnityEngine.UI.Image.Type.Sliced; }
//         bg.raycastTarget = false;
//         // The real LevelPlay banner ad is shown as a native overlay at the bottom of the screen.
//         // Disable the placeholder background so the space is left empty for the live ad.
//         bg.enabled = false;

//         // Subtle top highlight strip for an engraved, AAA edge.
//         Transform topT = banner.transform.Find("TopEdge");
//         GameObject top = topT != null ? topT.gameObject : CreateRect("TopEdge", banner.transform, Vector2.zero, new Vector2(0f, 3f));
//         var topRt = top.GetComponent<RectTransform>();
//         topRt.anchorMin = new Vector2(0f, 1f);
//         topRt.anchorMax = new Vector2(1f, 1f);
//         topRt.pivot = new Vector2(0.5f, 1f);
//         topRt.offsetMin = new Vector2(0f, -3f);
//         topRt.offsetMax = Vector2.zero;
//         var topImg = AddImage(top, new Color(1f, 0.85f, 0.5f, 0.18f));
//         topImg.raycastTarget = false;
//         topImg.enabled = false;

//         // Placeholder label.
//         Transform lblT = banner.transform.Find("Label");
//         TextMeshProUGUI lbl;
//         if (lblT != null)
//             lbl = lblT.GetComponent<TextMeshProUGUI>();
//         else
//         {
//             lbl = AddTmp(banner.transform, "BANNER AD PLACEMENT (FULL WIDTH)", new Color(0.15f, 0.10f, 0.06f, 1f),
//                 30, TextAlignmentOptions.Center, FontStyles.Bold);
//             lbl.gameObject.name = "Label";
//         }
//         var lrt = lbl.rectTransform;
//         lrt.anchorMin = Vector2.zero;
//         lrt.anchorMax = Vector2.one;
//         lrt.offsetMin = Vector2.zero;
//         lrt.offsetMax = Vector2.zero;
//         // Hide the "BANNER AD PLACEMENT" placeholder text — the live ad fills the band instead.
//         lbl.gameObject.SetActive(false);

//         banner.transform.SetAsLastSibling(); // above the dim overlay
//     }

//     /// <summary>Updates avatar portraits and name plates in the scene-authored HeaderRow.</summary>
//     void RefreshLeaderboardHeader(Transform container)
//     {
//         if (container == null) return;

//         Transform header = container.Find("HeaderRow");
//         if (header == null) return;

//         RefreshPlayerNamesAndActors();

//         // Task 13/31: enlarge the header column labels (ROUNDS / TOTAL).
//         foreach (Transform child in header)
//         {
//             if (child.name != "Cell") continue;
//             var lbl = child.GetComponent<TextMeshProUGUI>();
//             if (lbl == null) continue;
//             lbl.enableAutoSizing = true;
//             lbl.fontSizeMin = 16;
//             lbl.fontSizeMax = 40;
//         }

//         var headerCells = new List<Transform>();
//         for (int i = 0; i < header.childCount; i++)
//         {
//             Transform child = header.GetChild(i);
//             if (child.name == "PlayerHeaderCell")
//                 headerCells.Add(child);
//         }

//         for (int seat = 0; seat < headerCells.Count && seat < 4; seat++)
//         {
//             Transform cell = headerCells[seat];
//             string displayName = GetSeatDisplayName(seat);

//             Transform nameBox = cell.Find("NameBox");
//             if (nameBox != null)
//             {
//                 var nameTmp = nameBox.GetComponentInChildren<TextMeshProUGUI>(true);
//                 if (nameTmp != null)
//                 {
//                     nameTmp.text = displayName;
//                     nameTmp.enableAutoSizing = true;
//                     nameTmp.fontSizeMin = 14;
//                     nameTmp.fontSizeMax = 26;
//                 }
//             }

//             Transform avatarT = cell.Find("AvatarImage") ?? FindDeepByName(cell, "AvatarImage");
//             if (avatarT != null)
//             {
//                 var avatarImg = avatarT.GetComponent<Image>();
//                 if (avatarImg != null)
//                 {
//                     Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seat));
//                     if (avatar != null)
//                         avatarImg.sprite = avatar;
//                 }
//             }
//         }
//     }

//     /// <summary>Hides the old mock-up status pill if it was created in a previous session.</summary>
//     void HideMatchFinishedLabel()
//     {
//         if (resultPanel == null) return;
//         Transform tag = resultPanel.transform.Find("MatchFinishedTag");
//         if (tag != null)
//             tag.gameObject.SetActive(false);
//     }

//     static Image AddImage(GameObject go, Color c)
//     {
//         var img = go.GetComponent<Image>();
//         if (img == null) img = go.AddComponent<Image>();
//         img.color = c;
//         return img;
//     }

//     TextMeshProUGUI AddTmp(Transform parent, string text, Color color, int size, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
//     {
//         var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
//         go.transform.SetParent(parent, false);
//         var rt = go.GetComponent<RectTransform>();
//         rt.sizeDelta = new Vector2(200, 50); // Default size, usually overridden by layout
//         var tmp = go.GetComponent<TextMeshProUGUI>();
//         tmp.text = text;
//         tmp.color = color;
//         tmp.fontSize = size;
//         tmp.alignment = align;
//         tmp.fontStyle = style;
//         tmp.raycastTarget = false;
//         if (customFont != null) tmp.font = customFont;
//         return tmp;
//     }

//     void ClearDynamicUI()
//     {
//         // Only destroy runtime-created overflow rows. The static leaderboard skeleton
//         // (HeaderRow, RoundRow_1..5, dividers) is authored in the scene and must persist.
//         foreach (var go in _overflowRows)
//             if (go != null) DestroyObjectSafe(go);
//         _overflowRows.Clear();
//     }

//     void DestroyObjectSafe(GameObject go)
//     {
//         if (go == null) return;
// #if UNITY_EDITOR
//         if (!Application.isPlaying)
//         {
//             DestroyImmediate(go);
//             return;
//         }
// #endif
//         Destroy(go);
//     }

//     void OnHomeClicked()
//     {
//         if (_resultActionTaken) return;   // ignore rapid double-taps
//         _resultActionTaken = true;

//         Debug.Log("[UI] Button Clicked: Home (from results)");
//         HideResultPanelImmediate();
//         ResetMatchStats();

//         bool leaving = PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Leaving;
//         if (PhotonNetwork.InRoom && !leaving)
//             PhotonNetwork.LeaveRoom();
//         else if (PhotonNetwork.OfflineMode)
//         {
//             if (!leaving) PhotonNetwork.LeaveRoom();
//             PhotonNetwork.OfflineMode = false;
//         }

//         if (DeckManager.Instance != null)
//             DeckManager.Instance.ResetMatchState();

//         if (NetworkManager.Instance != null)
//             NetworkManager.Instance.ReturnToHomeScreen();
//     }

//     void OnRestartClicked()
//     {
//         if (_resultActionTaken) return;   // ignore rapid double-taps
//         if (!PhotonNetwork.OfflineMode && !(PhotonNetwork.InRoom && PhotonNetwork.IsConnectedAndReady))
//         {
//             Debug.LogWarning("[Result] Play Again ignored — not in a valid room state.");
//             return;
//         }
//         _resultActionTaken = true;

//         Debug.Log("[UI] Button Clicked: Play Again");
//         HideResultPanelImmediate();
//         ResetMatchStats();

//         if (DeckManager.Instance != null)
//             DeckManager.Instance.ResetMatchState();

//         GameFlowState.SetPhase(GameFlowPhase.InRoom);

//         if (PhotonNetwork.OfflineMode)
//         {
//             if (DeckManager.Instance != null && PhotonNetwork.IsMasterClient)
//                 DeckManager.Instance.FillBotsAndStart();
//         }
//         else if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
//         {
//             DeckManager.Instance.FillBotsAndStart();
//         }
//     }

//     void ResetMatchStats()
//     {
//         _statsRecorded = false;
//         _matchId = null;
//         _roundTransitionRunning = false;
//         currentRound = 1;
//         maxRounds = MaxRoundsBotsOnline;
//         roundHistory.Clear();
//         ResetRoundPlayerStats();

//         for (int i = 0; i < 4; i++)
//         {
//             playerResults[i].bid = 0;
//             playerResults[i].name = GetInitialPlayerName(i);
//         }
//     }
// }
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using DG.Tweening;
using Photon.Pun;
using Photon.Realtime;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class ResultManager : MonoBehaviourPunCallbacks
{
    public static ResultManager Instance;

    [Header("UI Root")]
    public CanvasGroup resultPanel;
    public Transform resultPanelSearchRoot;
    public TMP_FontAsset customFont;

    [Header("Optional — wired in scene or auto-built")]
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public Button homeButton;
    public Button restartButton;
    public Transform scoreboardContainer;

    [Header("Leaderboard Theme (assign in scene)")]
    public Sprite woodBoardSprite;
    public Sprite gearButtonSprite;
    public Sprite playerAvatarSprite;

    [System.Serializable]
    public class PlayerResult
    {
        public string name;
        public int actorNumber;
        public int bid; 
        public int tricksWon;
        public int dehlasCollected;
        public bool isCompleted;
        public float score;
        public int rank;
    }

    [System.Serializable]
    public class RoundResult
    {
        public int roundNumber;
        public int[] dehlasPerSeat = new int[4];
        public int[] tricksPerSeat = new int[4];
    }

    public int currentRound = 1;
    public int maxRounds = 5;
    public List<RoundResult> roundHistory = new List<RoundResult>();

    const int MaxRoundsBotsOnline = 5;
    const float InterRoundLeaderboardSeconds = 5f;
    const float MatchEndLeaderboardSeconds = 10f;
    const float BannerAdHeightPx = 110f;
    const float BannerAdSafeMarginPx = 24f;

    private PlayerResult[] playerResults = new PlayerResult[4];
    private Image _dimOverlay;
    private readonly List<GameObject> _dynamicRows = new List<GameObject>();
    private readonly List<GameObject> _overflowRows = new List<GameObject>();
    private bool _isShowingResult;
    private bool _statsRecorded;
    private string _matchId;
    private bool _resultActionTaken;
    private bool _autoTransitionMode;
    private bool _roundTransitionRunning;
    private ScrollRect _roundScrollRect;
    private static bool _resultPanelResolveWarned;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        for (int i = 0; i < 4; i++)
            playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };

        HideResultPanelImmediate();
        WireButtons();
    }

    void WireButtons()
    {
        if (homeButton != null) { EnableButtonVisuals(homeButton); homeButton.onClick.RemoveAllListeners(); homeButton.onClick.AddListener(OnHomeClicked); }
        if (restartButton != null) { EnableButtonVisuals(restartButton); restartButton.onClick.RemoveAllListeners(); restartButton.onClick.AddListener(OnRestartClicked); }
    }

    static void ShowLeaderboardBanner() { if (AdsManager.Instance != null) { AdsManager.Instance.LoadBanner(); AdsManager.Instance.ShowBanner(); } }
    static void HideLeaderboardBanner() { if (AdsManager.Instance != null) AdsManager.Instance.HideBanner(); }

    void HideResultPanelImmediate()
    {
        _isShowingResult = false;
        HideLeaderboardBanner();
        if (!ResolveResultPanel()) return;
        resultPanel.DOKill(); resultPanel.alpha = 0; resultPanel.interactable = false; resultPanel.blocksRaycasts = false; resultPanel.gameObject.SetActive(false);
        if (_dimOverlay != null) _dimOverlay.color = new Color(0f, 0f, 0f, 0f);
    }

    bool ResolveResultPanel()
    {
        if (resultPanel != null) return true;
        Transform root = resultPanelSearchRoot;
        if (root == null) { Canvas canvas = Object.FindAnyObjectByType<Canvas>(); if (canvas != null) root = canvas.transform.root; }
        if (root != null) { UiSafeLookup.SetSearchRoot(root); if (UiSafeLookup.TryGet("Panel_Winning", out GameObject panelGo) && panelGo != null) { resultPanel = panelGo.GetComponent<CanvasGroup>(); if (resultPanel == null) resultPanel = panelGo.AddComponent<CanvasGroup>(); return true; } }
        if (!_resultPanelResolveWarned) { _resultPanelResolveWarned = true; Debug.LogWarning("[ResultManager] resultPanel not found."); }
        return false;
    }

    void EnsurePanelHierarchyActive()
    {
        if (resultPanel == null) return;
        Transform t = resultPanel.transform;
        while (t != null) { if (!t.gameObject.activeSelf) t.gameObject.SetActive(true); t = t.parent; }
        Canvas rootCanvas = resultPanel.GetComponentInParent<Canvas>();
        if (rootCanvas != null) rootCanvas.gameObject.SetActive(true);
        resultPanel.transform.SetAsLastSibling();
    }

    void EnsureDimOverlay()
    {
        if (resultPanel == null) return;
        Transform existing = resultPanel.transform.Find("Overlay");
        if (existing != null) { _dimOverlay = existing.GetComponent<Image>(); if (_dimOverlay == null) _dimOverlay = existing.gameObject.AddComponent<Image>(); }
        else { GameObject overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(Image)); overlayGo.transform.SetParent(resultPanel.transform, false); overlayGo.transform.SetAsFirstSibling(); RectTransform rt = overlayGo.GetComponent<RectTransform>(); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; _dimOverlay = overlayGo.GetComponent<Image>(); _dimOverlay.raycastTarget = true; }
        _dimOverlay.color = new Color(0f, 0f, 0f, 0.55f); _dimOverlay.raycastTarget = true;
        var overlayBtn = _dimOverlay.GetComponent<Button>(); if (overlayBtn == null) overlayBtn = _dimOverlay.gameObject.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None; overlayBtn.onClick.RemoveAllListeners();
        if (!_autoTransitionMode) overlayBtn.onClick.AddListener(CloseResult);
    }

    string GetInitialPlayerName(int i)=> i == 0 ? "You" : "Dehla_AI_" + i;

    public void SetBid(int seatIndex, int bidValue) { if (seatIndex >= 0 && seatIndex < 4) playerResults[seatIndex].bid = bidValue; }

    public void OnTrickWon(int winnerSeatIndex, int dehlaCount)
    {
        if (winnerSeatIndex < 0 || winnerSeatIndex >= 4) return;
        playerResults[winnerSeatIndex].tricksWon++; playerResults[winnerSeatIndex].dehlasCollected += dehlaCount;
        if (PhotonNetwork.IsMasterClient) SyncScoresToRoomProperties();
    }

    void SyncScoresToRoomProperties()
    {
        if (!PhotonNetwork.InRoom) return;
        int[] tricks = new int[4]; int[] dehlas = new int[4];
        for (int i = 0; i < 4; i++) { tricks[i] = playerResults[i].tricksWon; dehlas[i] = playerResults[i].dehlasCollected; }
        PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable { { "SW", tricks }, { "DL", dehlas } });
    }

    public override void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null) return;
        if (propertiesThatChanged.ContainsKey("CR") && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CR", out object crObj)) currentRound = (int)crObj;
        if (propertiesThatChanged.ContainsKey("MR") && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("MR", out object mrObj)) maxRounds = (int)mrObj;
        if (propertiesThatChanged.ContainsKey("SW") && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("SW", out object tricksObj)) { int[] tricks = tricksObj as int[]; for (int i = 0; tricks != null && i < 4 && i < tricks.Length; i++) playerResults[i].tricksWon = tricks[i]; }
        if (propertiesThatChanged.ContainsKey("DL") && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DL", out object dehlaObj)) { int[] dehlas = dehlaObj as int[]; for (int i = 0; dehlas != null && i < 4 && i < dehlas.Length; i++) playerResults[i].dehlasCollected = dehlas[i]; }
    }

    // 🚨 BUG FIX: Round 5/5 Bot Crash fixed here! Ensure complete hard reset!
    public void InitializeForMatch()
    {
        Debug.Log("[ResultManager] Hard Resetting Match State. Setting Round to 1.");
        bool unlimited = GameSettings.Instance != null && GameSettings.Instance.currentMatchType == MatchType.PlayWithFriends;
        if (!unlimited && DeckManager.IsPrivateFriendsRoom()) unlimited = true;

        maxRounds = unlimited ? -1 : MaxRoundsBotsOnline;
        currentRound = 1; // 🚨 HARD RESET
        roundHistory.Clear();
        _roundTransitionRunning = false;
        _statsRecorded = false;
        _matchId = null;
        ResetRoundPlayerStats();

        // Ensure UI updates if game scene is active
        if (TrumpManager.Instance != null && TrumpManager.Instance.roundText != null)
            TrumpManager.Instance.roundText.text = $"Round 1";

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
            SyncRoundConfigToRoom();
    }

    void SyncRoundConfigToRoom()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable { { "CR", currentRound }, { "MR", maxRounds } });
    }

    public bool IsMatchOver() => maxRounds != -1 && currentRound >= maxRounds;

    public void TriggerRoundCompletedFromMaster()
    {
        if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.OfflineMode) return;
        if (DeckManager.Instance != null && DeckManager.Instance.photonView != null) DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_OnRoundCompleted), RpcTarget.All);
        else OnRoundCompleted();
    }

    public void OnRoundCompleted()
    {
        if (_roundTransitionRunning) return;
        if (TurnManager.Instance != null) TurnManager.Instance.StopTimer();

        try { EnsurePlayerResults(); RefreshPlayerNamesAndActors(); CalculateScores(); AssignRanks(); FinalizeCurrentRoundScores(); }
        catch (System.Exception e) { Debug.LogError($"[Result] Round scoring failed: {e}"); }

        bool matchOver = IsMatchOver();
        if (matchOver) GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);

        _roundTransitionRunning = true;
        bool authoritative = PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;

        try { ShowRoundLeaderboard(matchOver); } catch (System.Exception e) { Debug.LogError($"[Result] Leaderboard render failed: {e}"); }

        if (matchOver) StartCoroutine(RoundTransitionRoutine(matchOver, authoritative));
        else if (GameManager.Instance != null) GameManager.Instance.BeginRoundEndSequence(authoritative);
        else StartCoroutine(RoundTransitionRoutine(matchOver, authoritative));
    }

    public void NotifyRoundEndSequenceComplete() { HideResultPanelImmediate(); _roundTransitionRunning = false; }

    IEnumerator RoundTransitionRoutine(bool matchOver, bool authoritative)
    {
        float wait = matchOver ? MatchEndLeaderboardSeconds : InterRoundLeaderboardSeconds;
        yield return new WaitForSecondsRealtime(wait);
        HideResultPanelImmediate(); _roundTransitionRunning = false;
        if (!authoritative) yield break;

        if (matchOver)
        {
            AssignMatchRanksFromHistory(); RecordMatchStats();
            if (DeckManager.Instance != null) DeckManager.Instance.ResetMatchState();
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom(); else if (NetworkManager.Instance != null) NetworkManager.Instance.ReturnToHomeScreen();
            yield break;
        }

        int nextRound = currentRound + 1;
        if (DeckManager.Instance != null)
        {
            if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode) DeckManager.Instance.ResetRoundStateForNextRound();
            if (DeckManager.Instance.photonView != null) DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_BeginNextRound), RpcTarget.AllBuffered, nextRound);
        }
    }

    public void ApplyNextRoundStart(int newRound)
    {
        currentRound = newRound; ResetRoundPlayerStats();
        if (PhotonNetwork.IsMasterClient) SyncRoundConfigToRoom();
    }

    void ShowRoundLeaderboard(bool matchOver) { ShowResultInternal(autoTransition: true, matchOver: matchOver); }
    public void CloseResult() { if (_autoTransitionMode && _roundTransitionRunning) _roundTransitionRunning = false; HideResultPanelImmediate(); }
    public void ForceHideLeaderboardNow() { if (_roundTransitionRunning) { StopAllCoroutines(); _roundTransitionRunning = false; } HideResultPanelImmediate(); }
    void EnsurePlayerResults() { if (playerResults == null || playerResults.Length < 4) playerResults = new PlayerResult[4]; for (int i = 0; i < 4; i++) { if (playerResults[i] == null) playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) }; } }

    public void ShowResult() { ShowResultInternal(autoTransition: false, matchOver: false); }

    void ShowResultInternal(bool autoTransition, bool matchOver)
    {
        if (_isShowingResult) return;
        if (!ResolveResultPanel()) return;

        _isShowingResult = true; _resultActionTaken = false; _autoTransitionMode = autoTransition;
        EnsurePlayerResults(); EnsurePanelHierarchyActive(); EnsureDimOverlay();

        if (!autoTransition) { RefreshPlayerNamesAndActors(); CalculateScores(); AssignRanks(); RecordMatchStats(); }
        BuildResultPanelUI(); SetActionButtonsVisible(!autoTransition); StartCoroutine(ScrollLeaderboardToBottom());
        resultPanel.gameObject.SetActive(true); ShowLeaderboardBanner(); ResetPanelOpenStateInstant();
        CreateBannerAd(); HideMatchFinishedLabel();
    }

    void ResetPanelOpenStateInstant()
    {
        if (resultPanel == null) return;
        resultPanel.DOKill(complete: true); resultPanel.alpha = 1f; resultPanel.interactable = true; resultPanel.blocksRaycasts = true;
        Transform root = resultPanel.transform; root.DOKill(complete: true); root.localScale = Vector3.one;
        if (_dimOverlay != null) { _dimOverlay.DOKill(complete: true); Color c = _dimOverlay.color; c.a = 0.55f; _dimOverlay.color = c; }
        Transform frame = root.Find("MainFrame");
        if (frame != null)
        {
            frame.DOKill(complete: true); frame.localScale = Vector3.one; frame.SetAsLastSibling();
            var frt = frame as RectTransform; if (frt != null) { Vector2 pos = frt.anchoredPosition; float minY = BannerAdHeightPx + BannerAdSafeMarginPx; if (pos.y < minY) pos.y = minY; frt.anchoredPosition = pos; }
        }
    }

    IEnumerator ScrollLeaderboardToBottom() { yield return null; yield return null; Canvas.ForceUpdateCanvases(); if (_roundScrollRect != null) _roundScrollRect.verticalNormalizedPosition = 0f; }

    static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null) return null; Transform direct = root.Find(childName); if (direct != null) return direct;
        foreach (Transform child in root) { Transform found = FindDeepChild(child, childName); if (found != null) return found; }
        return null;
    }

    void SetActionButtonsVisible(bool visible)
    {
        if (homeButton != null) homeButton.gameObject.SetActive(visible);
        if (restartButton != null) restartButton.gameObject.SetActive(visible);
        Transform mainFrame = resultPanel != null ? resultPanel.transform.Find("MainFrame") : null;
        if (mainFrame != null) { Transform btnContainer = mainFrame.Find("ButtonsContainer"); if (btnContainer != null) btnContainer.gameObject.SetActive(visible); Transform closeBtn = mainFrame.Find("CloseButton"); if (closeBtn != null) closeBtn.gameObject.SetActive(true); }
    }

    void RefreshPlayerNamesAndActors() { for (int seat = 0; seat < 4; seat++) { playerResults[seat].name = GetSeatDisplayName(seat); playerResults[seat].actorNumber = GetActorNumberBySeat(seat); } }

    int GetActorNumberBySeat(int seatIndex)
    {
        if (PlayerHand.LocalInstance == null) return seatIndex; 
        var field = typeof(PlayerHand).GetField("tableTurnOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null) return -1;
        var turnOrder = (List<int>)field.GetValue(PlayerHand.LocalInstance);
        if (turnOrder != null && seatIndex < turnOrder.Count) return turnOrder[seatIndex];
        return -1;
    }

    string GetSeatDisplayName(int seatIndex)
    {
        if (seatIndex == 0) return PlayerProfileSync.GetLocalProfileDisplayName();
        if (PlayerProfileSync.Instance != null) { switch (seatIndex) { case 1 when PlayerProfileSync.Instance.txtLeftName != null: return CleanName(PlayerProfileSync.Instance.txtLeftName.text); case 2 when PlayerProfileSync.Instance.txtTopName != null: return CleanName(PlayerProfileSync.Instance.txtTopName.text); case 3 when PlayerProfileSync.Instance.txtRightName != null: return CleanName(PlayerProfileSync.Instance.txtRightName.text); } }
        return "Player " + (seatIndex + 1);
    }

    static string CleanName(string raw) { if (string.IsNullOrEmpty(raw)) return "Player"; return raw.Split('\n')[0].Trim(); }

    void FinalizeCurrentRoundScores()
    {
        var result = new RoundResult { roundNumber = currentRound };
        for (int seat = 0; seat < 4; seat++) { result.dehlasPerSeat[seat] = playerResults[seat].dehlasCollected; result.tricksPerSeat[seat] = playerResults[seat].tricksWon; }
        if (roundHistory.Count > 0 && roundHistory[roundHistory.Count - 1].roundNumber == currentRound) roundHistory[roundHistory.Count - 1] = result; else roundHistory.Add(result);
    }

    void ResetRoundPlayerStats()
    {
        for (int i = 0; i < 4; i++) { playerResults[i].tricksWon = 0; playerResults[i].dehlasCollected = 0; playerResults[i].score = 0; playerResults[i].isCompleted = false; playerResults[i].rank = 0; }
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) { PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable { { "SW", new int[4] }, { "DL", new int[4] }, { "TP", 0 } }); }
    }

    void AssignMatchRanksFromHistory()
    {
        int[] totals = new int[4]; foreach (RoundResult round in roundHistory) for (int i = 0; i < 4; i++) totals[i] += round.dehlasPerSeat[i];
        for (int i = 0; i < 4; i++) { playerResults[i].dehlasCollected = totals[i]; playerResults[i].score = totals[i]; }
        if (IsFriendsTeamMode()) { int teamA = totals[0] + totals[2]; int teamB = totals[1] + totals[3]; playerResults[0].score = playerResults[2].score = teamA; playerResults[1].score = playerResults[3].score = teamB; AssignTeamRanks(); return; }
        var ranked = totals.Select((value, index) => (value, index)).OrderByDescending(x => x.value).ToList();
        for (int r = 0; r < ranked.Count; r++) playerResults[ranked[r].index].rank = r + 1;
    }

    static int GetKotThreshold() { return TaashRules.IsTwoTaashMode ? 8 : 4; }
    static string FormatDehlaScore(int dehlas) { int kotThreshold = GetKotThreshold(); return dehlas == kotThreshold ? $"{dehlas} (KOT)" : dehlas.ToString(); }
    static int SumRound(int[] roundScores) { int total = 0; for (int i = 0; i < roundScores.Length; i++) total += roundScores[i]; return total; }

    void CalculateScores()
    {
        for (int seat = 0; seat < playerResults.Length; seat++) { PlayerResult p = playerResults[seat]; if (p == null) continue; int cumulativeDehlas = p.dehlasCollected; foreach (RoundResult rr in roundHistory) { if (rr == null || rr.roundNumber == currentRound) continue; if (rr.dehlasPerSeat != null && seat < rr.dehlasPerSeat.Length) cumulativeDehlas += rr.dehlasPerSeat[seat]; } p.score = cumulativeDehlas; p.isCompleted = true; }
        if (IsFriendsTeamMode()) ApplyTeamScores();
    }

    bool IsFriendsTeamMode() { if (!DeckManager.IsPrivateFriendsRoom()) return false; return GetLogicMode() == 2; }
    static int GetLogicMode() { if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("LM", out object lm) && lm is int li) return li; if (ModeManager.Instance != null) return ModeManager.Instance.currentLogicMode; return 1; }

    void ApplyTeamScores() { float teamA = playerResults[0].score + playerResults[2].score; float teamB = playerResults[1].score + playerResults[3].score; playerResults[0].score = playerResults[2].score = teamA; playerResults[1].score = playerResults[3].score = teamB; }

    void AssignTeamRanks()
    {
        float teamA = playerResults[0].score; float teamB = playerResults[1].score; int rankA;
        if (teamA != teamB) { rankA = teamA > teamB ? 1 : 2; } else { int minActorA = Mathf.Min(playerResults[0].actorNumber, playerResults[2].actorNumber); int minActorB = Mathf.Min(playerResults[1].actorNumber, playerResults[3].actorNumber); rankA = minActorA <= minActorB ? 1 : 2; }
        int rankB = rankA == 1 ? 2 : 1; playerResults[0].rank = playerResults[2].rank = rankA; playerResults[1].rank = playerResults[3].rank = rankB;
    }

    void AssignRanks() { EnsurePlayerResults(); if (IsFriendsTeamMode()) { AssignTeamRanks(); return; } UpdateAndSortLeaderboard(new List<PlayerResult>(playerResults)); }

    public void UpdateAndSortLeaderboard(List<PlayerResult> currentPlayers)
    {
        if (currentPlayers == null) return;
        List<PlayerResult> valid = currentPlayers.Where(p => p != null).ToList(); if (valid.Count == 0) return;
        valid.Sort(CompareForLeaderboard);
        for (int i = 0; i < valid.Count; i++) valid[i].rank = i + 1;
    }

    static int CompareForLeaderboard(PlayerResult a, PlayerResult b)
    {
        if (a == null && b == null) return 0; if (a == null) return 1; if (b == null) return -1;
        int byScore = b.score.CompareTo(a.score); if (byScore != 0) return byScore;
        int byDehlas = b.dehlasCollected.CompareTo(a.dehlasCollected); if (byDehlas != 0) return byDehlas;
        int byTricks = b.tricksWon.CompareTo(a.tricksWon); if (byTricks != 0) return byTricks;
        return a.actorNumber.CompareTo(b.actorNumber);
    }

    void BuildResultPanelUI()
    {
        ClearDynamicUI();
        if (resultPanel == null) return;
        Transform mainFrame = resultPanel.transform.Find("MainFrame");
        if (mainFrame == null) return;
        mainFrame.localScale = Vector3.one;

        var mainFrameImg = mainFrame.GetComponent<Image>();
        if (mainFrameImg != null) { mainFrameImg.type = Image.Type.Sliced; mainFrameImg.pixelsPerUnitMultiplier = 1.8f; }

        Transform rowsContainer = scoreboardContainer != null ? scoreboardContainer : FindDeepChild(mainFrame, "PlayerRowsContainer");
        if (_roundScrollRect == null) _roundScrollRect = mainFrame.GetComponentInChildren<ScrollRect>(true);

        if (rowsContainer != null) { EnsureStaticLeaderboard(rowsContainer); RefreshLeaderboardHeader(rowsContainer); UpdateLeaderboardUI(rowsContainer); }
        
        string title = maxRounds == -1 ? $"Round {currentRound} Complete" : $"Round {currentRound} / {maxRounds}";
        if (titleText != null) titleText.text = title;

        Transform closeT = mainFrame.Find("CloseButton");
        if (closeT != null) { var closeBtn = closeT.GetComponent<Button>(); if (closeBtn != null) { EnableButtonVisuals(closeBtn); closeBtn.onClick.RemoveAllListeners(); closeBtn.onClick.AddListener(CloseResult); } }
        EnsureSceneButtons(mainFrame);
    }

    private readonly List<GameObject> _dynamicDecor = new List<GameObject>();
    private Sprite _roundedSprite;

    static readonly Color LeaderLabelColor = Color.black;
    static readonly Color NameBoxColor = new Color(0.36f, 0.20f, 0.10f, 1f);
    static readonly Color TextWhiteColor = Color.white;
    static readonly Color ScoreDarkColor = new Color(0.16f, 0.09f, 0.04f, 1f);

    const int StaticLeaderboardRows = 5;

    void EnsureStaticLeaderboard(Transform container)
    {
        if (container == null) return;
        if (container.Find("HeaderRow") == null) BuildStaticLeaderboard(container, StaticLeaderboardRows);
        if (container.Find("TotalsRow") == null) BuildEditableTotalsRow(container, ComputeInnerWidth(container), 64f);
    }

    void BuildEditableTotalsRow(Transform container, float innerW, float rowH)
    {
        if (container == null || container.Find("TotalsRow") != null) return;
        string[] totalCells = new string[6]; totalCells[0] = "TOTAL"; for (int s = 1; s < 6; s++) totalCells[s] = "0";
        GameObject rowGo = CreateScoreRow("TotalsRow", container, totalCells, innerW, rowH, ScoreDarkColor, true);
        rowGo.transform.SetAsLastSibling();
    }

    public void BuildStaticLeaderboard(Transform container, int rowCount)
    {
        if (container == null) return;
        ResolveThemeSprites(); float innerW = ComputeInnerWidth(container); const float headerH = 120f; const float rowH = 64f;
        CreateHeaderRow("HeaderRow", container, innerW, headerH);
        for (int r = 1; r <= rowCount; r++) { string[] cells = new string[6]; cells[0] = "R" + r; for (int s = 1; s < 6; s++) cells[s] = ""; CreateScoreRow("RoundRow_" + r, container, cells, innerW, rowH, TextWhiteColor, false); }
        BuildEditableTotalsRow(container, innerW, rowH);
        BuildVerticalDividers(container, innerW);
    }

    float ComputeInnerWidth(Transform container)
    {
        var crt = container as RectTransform; var vlg = container.GetComponent<VerticalLayoutGroup>();
        float innerW = 1040f;
        if (crt != null && crt.rect.width > 1f) { innerW = crt.rect.width; if (vlg != null) innerW -= (vlg.padding.left + vlg.padding.right); }
        return innerW;
    }

    public void UpdateLeaderboardUI(Transform container) { FillLeaderboardData(container); }

    void FillLeaderboardData(Transform container)
    {
        _dynamicRows.Clear();
        if (IsFriendsTeamMode()) { SetAuthoredLeaderboardRowsActive(container, false); BuildFriendsTeamLeaderboard(container); }
        else { SetAuthoredLeaderboardRowsActive(container, true); FillIndividualLeaderboardRows(container); }
        ApplyAllLeaderboardPositions(container);
        if (container is RectTransform containerRect) StartCoroutine(RebuildLeaderboardLayout(containerRect));
    }

    void FillIndividualLeaderboardRows(Transform container)
    {
        Transform header = container.Find("HeaderRow"); if (header != null) _dynamicRows.Add(header.gameObject);
        int slots = maxRounds > 0 ? maxRounds : Mathf.Max(roundHistory.Count, 1);
        int total = Mathf.Max(slots, StaticLeaderboardRows);
        for (int r = 1; r <= total; r++)
        {
            Transform rowT = container.Find("RoundRow_" + r); GameObject rowGo;
            if (rowT != null) rowGo = rowT.gameObject;
            else { string[] blank = new string[6]; blank[0] = "R" + r; for (int s = 1; s < 6; s++) blank[s] = ""; rowGo = CreateScoreRow("RoundRow_" + r, container, blank, ComputeInnerWidth(container), 64f, TextWhiteColor, false); _overflowRows.Add(rowGo); }
            rowGo.SetActive(true); rowGo.transform.localScale = Vector3.one; var rowCg = rowGo.GetComponent<CanvasGroup>(); if (rowCg != null) rowCg.alpha = 1f;
            FillRowCells(rowGo, r); _dynamicRows.Add(rowGo);
        }
        BuildOrUpdateTotalsRow(container);
    }

    void SetAuthoredLeaderboardRowsActive(Transform container, bool active)
    {
        if (container == null) return;
        SetChildActiveByName(container, "HeaderRow", active); SetChildActiveByName(container, "TotalsRow", active);
        for (int r = 1; r <= StaticLeaderboardRows; r++) SetChildActiveByName(container, "RoundRow_" + r, active);
        foreach (Transform child in container) if (child.name == "VDivider") child.gameObject.SetActive(active);
    }

    static void SetChildActiveByName(Transform parent, string childName, bool active) { Transform t = parent.Find(childName); if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active); }

    IEnumerator RebuildLeaderboardLayout(RectTransform containerTransform)
    {
        yield return null; if (containerTransform == null) yield break;
        Canvas.ForceUpdateCanvases(); UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(containerTransform);
        ApplyAllLeaderboardPositions(containerTransform);
    }

    void BuildOrUpdateTotalsRow(Transform container)
    {
        if (container == null) return;
        int[] totals = new int[4]; foreach (RoundResult rr in roundHistory) for (int s = 0; s < 4 && s < rr.dehlasPerSeat.Length; s++) totals[s] += rr.dehlasPerSeat[s];
        int grand = totals[0] + totals[1] + totals[2] + totals[3];
        string[] cellTexts = new string[6]; cellTexts[0] = "TOTAL"; for (int s = 0; s < 4; s++) cellTexts[s + 1] = totals[s].ToString(); cellTexts[5] = grand.ToString();
        Transform existing = container.Find("TotalsRow"); bool authored = existing != null;
        GameObject rowGo = authored ? existing.gameObject : CreateScoreRow("TotalsRow", container, cellTexts, ComputeInnerWidth(container), 64f, ScoreDarkColor, true);
        if (!authored) _overflowRows.Add(rowGo);
        rowGo.transform.SetAsLastSibling(); rowGo.SetActive(true); _dynamicRows.Add(rowGo);
        var tmps = new List<TextMeshProUGUI>(); foreach (Transform child in rowGo.transform) { var t = child.GetComponent<TextMeshProUGUI>(); if (t != null) tmps.Add(t); }
        if (tmps.Count < 6) return;
        for (int s = 0; s < 6; s++) { TextMeshProUGUI totalRowText = tmps[s]; totalRowText.text = StripRichTextTags(cellTexts[s]); if (!authored) { totalRowText.fontSize = 34; totalRowText.color = Color.black; totalRowText.enableAutoSizing = false; totalRowText.alignment = s == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center; } }
    }

    void BuildFriendsTeamLeaderboard(Transform container)
    {
        if (container == null) return;
        ResolveThemeSprites(); float innerW = ComputeInnerWidth(container); const float headerH = 120f; const float rowH = 64f;
        GameObject headerGo = NewRow("FriendsHeaderRow", container, innerW, headerH);
        AddSideHeaderLabel(headerGo.transform, "ROUNDS", true, LeaderLabelColor, 30, FontStyles.Bold);
        CreateFriendsTeamHeaderCell(headerGo.transform, 0, 2); CreateFriendsTeamHeaderCell(headerGo.transform, 1, 3);
        AddCellLabel(headerGo.transform, "TOTAL", LeaderLabelColor, 30, FontStyles.Bold);
        AddRowDashedLine(headerGo, innerW, headerH); RegisterFriendsRow(headerGo);
        int slots = maxRounds > 0 ? maxRounds : Mathf.Max(roundHistory.Count, 1); int totalRows = Mathf.Max(slots, StaticLeaderboardRows);
        int grandA = 0, grandB = 0;
        for (int r = 1; r <= totalRows; r++) { RoundResult round = roundHistory.Find(rr => rr.roundNumber == r); string aVal = "", bVal = "", rowTotal = ""; if (round != null && round.dehlasPerSeat != null && round.dehlasPerSeat.Length >= 4) { int a = round.dehlasPerSeat[0] + round.dehlasPerSeat[2]; int b = round.dehlasPerSeat[1] + round.dehlasPerSeat[3]; aVal = a.ToString(); bVal = b.ToString(); rowTotal = (a + b).ToString(); grandA += a; grandB += b; } string[] cells = { "R" + r, aVal, bVal, rowTotal }; GameObject rowGo = CreateScoreRow("FriendsRoundRow_" + r, container, cells, innerW, rowH, Color.black, true); StyleFriendsRowCells(rowGo, Color.black); RegisterFriendsRow(rowGo); }
        string[] totalCells = { "TOTAL", grandA.ToString(), grandB.ToString(), (grandA + grandB).ToString() };
        GameObject totalsGo = CreateScoreRow("FriendsTotalsRow", container, totalCells, innerW, rowH, ScoreDarkColor, true); StyleFriendsRowCells(totalsGo, ScoreDarkColor); RegisterFriendsRow(totalsGo);
        BuildFriendsDividers(container, innerW);
    }

    void CreateFriendsTeamHeaderCell(Transform rowParent, int topSeat, int bottomSeat) { var cell = new GameObject("FriendsTeamHeaderCell", typeof(RectTransform)); cell.transform.SetParent(rowParent, false); MakeEqualColumn(cell); CreateStackedNamePlate(cell.transform, topSeat, 28f); CreateStackedNamePlate(cell.transform, bottomSeat, -28f); }

    void CreateStackedNamePlate(Transform cellParent, int seatIndex, float y)
    {
        var group = CreateRect("PlayerPlate", cellParent, new Vector2(0f, y), new Vector2(196f, 44f));
        var avatarGo = CreateRect("AvatarImage", group.transform, new Vector2(-80f, 0f), new Vector2(40f, 40f));
        var avatarImg = AddImage(avatarGo, Color.white); avatarImg.preserveAspect = true; avatarImg.raycastTarget = false;
        Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seatIndex)); if (avatar != null) avatarImg.sprite = avatar; else if (playerAvatarSprite != null) avatarImg.sprite = playerAvatarSprite;
        var nameBox = CreateRect("NameBox", group.transform, new Vector2(22f, 0f), new Vector2(140f, 36f));
        var nameImg = AddImage(nameBox, NameBoxColor); if (_roundedSprite != null) { nameImg.sprite = _roundedSprite; nameImg.type = UnityEngine.UI.Image.Type.Sliced; } nameImg.raycastTarget = false;
        var nameTxt = AddTmp(nameBox.transform, GetSeatDisplayName(seatIndex), Color.white, 16, TextAlignmentOptions.Center, FontStyles.Bold); nameTxt.rectTransform.anchorMin = Vector2.zero; nameTxt.rectTransform.anchorMax = Vector2.one; nameTxt.rectTransform.offsetMin = new Vector2(6, 2); nameTxt.rectTransform.offsetMax = new Vector2(-6, -2); nameTxt.overflowMode = TextOverflowModes.Ellipsis; nameTxt.enableAutoSizing = true; nameTxt.fontSizeMin = 9; nameTxt.fontSizeMax = 16;
    }

    void StyleFriendsRowCells(GameObject rowGo, Color color) { if (rowGo == null) return; int i = 0; foreach (Transform child in rowGo.transform) { var tmp = child.GetComponent<TextMeshProUGUI>(); if (tmp == null) continue; ApplyCellStyle(tmp, i == 0); tmp.color = color; i++; } }
    void RegisterFriendsRow(GameObject rowGo) { if (rowGo == null) return; _overflowRows.Add(rowGo); rowGo.transform.SetAsLastSibling(); rowGo.SetActive(true); rowGo.transform.localScale = Vector3.one; var cg = rowGo.GetComponent<CanvasGroup>(); if (cg != null) cg.alpha = 1f; _dynamicRows.Add(rowGo); }
    void BuildFriendsDividers(Transform container, float innerW) { var crt = container as RectTransform; float h = (crt != null && crt.rect.height > 1f) ? crt.rect.height - 40f : 540f; float col = innerW / 4f; AddFriendsVerticalDivider(container, -innerW / 2f + col, h); AddFriendsVerticalDivider(container, -innerW / 2f + col * 3f, h); }
    void AddFriendsVerticalDivider(Transform container, float x, float height) { var go = CreateRect("FriendsVDivider", container, new Vector2(x, 0f), new Vector2(3f, height)); var le = go.AddComponent<LayoutElement>(); le.ignoreLayout = true; var img = AddImage(go, new Color(0.12f, 0.06f, 0.02f, 0.45f)); img.raycastTarget = false; go.transform.SetAsFirstSibling(); _overflowRows.Add(go); }
    static string StripRichTextTags(string value) { if (string.IsNullOrEmpty(value)) return value; return System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty); }

    void FillRowCells(GameObject rowGo, int roundNumber)
    {
        var cells = new List<TextMeshProUGUI>(); foreach (Transform child in rowGo.transform) { var tmp = child.GetComponent<TextMeshProUGUI>(); if (tmp != null) cells.Add(tmp); }
        if (cells.Count < 6) return;
        RoundResult round = roundHistory.Find(rr => rr.roundNumber == roundNumber); cells[0].text = "R" + roundNumber;
        if (round != null) { for (int s = 0; s < 4; s++) cells[s + 1].text = FormatDehlaScore(round.dehlasPerSeat[s]); cells[5].text = SumRound(round.dehlasPerSeat).ToString(); } else { for (int s = 1; s < 6; s++) cells[s].text = ""; }
        for (int s = 0; s < 6; s++) { ApplyCellStyle(cells[s], s == 0); cells[s].color = Color.black; }
    }

    void ApplyCellStyle(TextMeshProUGUI cell, bool isRowLabel) { if (cell == null) return; cell.enableAutoSizing = false; cell.fontSize = 34; cell.overflowMode = TextOverflowModes.Overflow; if (isRowLabel) { cell.alignment = TextAlignmentOptions.MidlineLeft; cell.margin = new Vector4(18f, 0f, 0f, 0f); } else { cell.alignment = TextAlignmentOptions.Center; cell.margin = Vector4.zero; } }

    void ApplyAllLeaderboardPositions(Transform container) { if (container == null) return; foreach (Transform row in container) { string nm = row.name; if (nm == "HeaderRow" || nm == "FriendsHeaderRow") ApplyLeaderboardRowLayout(row, isHeader: true); else if (nm.StartsWith("RoundRow_") || nm == "TotalsRow" || nm.StartsWith("FriendsRoundRow_") || nm == "FriendsTotalsRow") ApplyLeaderboardRowLayout(row, isHeader: false); } }
    
    void ApplyLeaderboardRowLayout(Transform row, bool isHeader)
    {
        if (row == null) return; var hlg = row.GetComponent<HorizontalLayoutGroup>(); if (hlg != null) hlg.enabled = false;
        List<RectTransform> cells = new List<RectTransform>(); for (int i = 0; i < row.childCount; i++) { Transform c = row.GetChild(i); if (c.name == "Cell" || c.name == "PlayerHeaderCell" || c.name == "FriendsTeamHeaderCell") cells.Add(c as RectTransform); }
        int n = cells.Count; if (n < 2) return;
        var rowRt = row as RectTransform; float rowH = (rowRt != null && rowRt.rect.height > 1f) ? rowRt.rect.height : (isHeader ? 120f : 64f);
        bool friends = n <= 4;
        float[] LbPlayerColumnX = { 340f, 550f, 760f, 970f }; float[] LbFriendsColumnX = { 40f, 445f, 865f, 1180f };

        for (int i = 0; i < n; i++)
        {
            RectTransform cell = cells[i]; bool isRoundsCol = (i == 0); bool isTotalCol = (i == n - 1);
            float x; if (friends) x = LbFriendsColumnX[Mathf.Clamp(i, 0, LbFriendsColumnX.Length - 1)]; else if (isRoundsCol) x = 40f; else if (isTotalCol) x = 1180f; else x = LbPlayerColumnX[Mathf.Clamp(i - 1, 0, LbPlayerColumnX.Length - 1)];
            float y; Vector2 pivot; Vector2 size;
            if (isHeader) { if (isRoundsCol) { y = -60f; pivot = new Vector2(0f, 0.5f); size = new Vector2(260f, 80f); } else if (isTotalCol) { y = -60f; pivot = new Vector2(0.5f, 0.5f); size = new Vector2(240f, 80f); } else { y = -50f; pivot = new Vector2(0.5f, 0.5f); size = new Vector2(180f, 120f); } }
            else { y = -rowH / 2f; if (isRoundsCol) { pivot = new Vector2(0f, 0.5f); size = new Vector2(220f, rowH); } else { pivot = new Vector2(0.5f, 0.5f); size = new Vector2(180f, rowH); } }
            var le = cell.GetComponent<LayoutElement>(); if (le == null) le = cell.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            cell.anchorMin = new Vector2(0f, 1f); cell.anchorMax = new Vector2(0f, 1f); cell.pivot = pivot; cell.sizeDelta = size; cell.anchoredPosition = new Vector2(x, y);
            var tmp = cell.GetComponent<TextMeshProUGUI>(); if (tmp != null) { tmp.enableWordWrapping = false; tmp.overflowMode = TextOverflowModes.Overflow; tmp.margin = Vector4.zero; tmp.alignment = isRoundsCol ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center; }
        }
    }

    void ResolveThemeSprites()
    {
        if (resultPanel == null || _roundedSprite != null) return;
        Transform root = resultPanel.transform;
        Transform t = FindDeepByName(root, "HomeButton") ?? FindDeepByName(root, "RestartButton") ?? FindDeepByName(root, "NameBox") ?? FindDeepByName(root, "CloseButton");
        if (t != null) _roundedSprite = t.GetComponent<UnityEngine.UI.Image>()?.sprite;
    }
    static Transform FindDeepByName(Transform parent, string name) { if (parent.name == name) return parent; for (int i = 0; i < parent.childCount; i++) { Transform r = FindDeepByName(parent.GetChild(i), name); if (r != null) return r; } return null; }

    GameObject CreateHeaderRow(string name, Transform parent, float width, float height) { var rowGo = NewRow(name, parent, width, height); AddSideHeaderLabel(rowGo.transform, "ROUNDS", true, LeaderLabelColor, 30, FontStyles.Bold); for (int s = 0; s < 4; s++) CreateAvatarHeaderCell(rowGo.transform, s); AddCellLabel(rowGo.transform, "TOTAL", LeaderLabelColor, 30, FontStyles.Bold); AddRowDashedLine(rowGo, width, height); return rowGo; }

    void AddSideHeaderLabel(Transform rowParent, string text, bool alignLeft, Color color, int maxSize, FontStyles style) { var cellGo = new GameObject("Cell", typeof(RectTransform)); cellGo.transform.SetParent(rowParent, false); MakeEqualColumn(cellGo); var tmp = cellGo.AddComponent<TextMeshProUGUI>(); tmp.text = text; tmp.color = color; tmp.alignment = alignLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight; tmp.fontStyle = style; tmp.enableAutoSizing = true; tmp.fontSizeMin = 12; tmp.fontSizeMax = maxSize; tmp.overflowMode = TextOverflowModes.Ellipsis; tmp.raycastTarget = false; if (customFont != null) tmp.font = customFont; var rt = tmp.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; if (alignLeft) rt.offsetMin = new Vector2(18f, 0); else rt.offsetMax = new Vector2(-18f, 0); }

    void CreateAvatarHeaderCell(Transform rowParent, int seatIndex)
    {
        var cell = new GameObject("PlayerHeaderCell", typeof(RectTransform)); cell.transform.SetParent(rowParent, false);
        var avatarGo = CreateRect("AvatarImage", cell.transform, new Vector2(0, 26f), new Vector2(82, 82)); var avatarImg = AddImage(avatarGo, Color.white); avatarImg.preserveAspect = true; avatarImg.raycastTarget = false; Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seatIndex)); if (avatar != null) avatarImg.sprite = avatar; else if (playerAvatarSprite != null) avatarImg.sprite = playerAvatarSprite;
        var nameBox = CreateRect("NameBox", cell.transform, new Vector2(0, -36f), new Vector2(150, 34)); var nameImg = AddImage(nameBox, NameBoxColor); if (_roundedSprite != null) { nameImg.sprite = _roundedSprite; nameImg.type = UnityEngine.UI.Image.Type.Sliced; } nameImg.raycastTarget = false;
        var nameTxt = AddTmp(nameBox.transform, GetSeatDisplayName(seatIndex), Color.white, 18, TextAlignmentOptions.Center, FontStyles.Bold); nameTxt.rectTransform.anchorMin = Vector2.zero; nameTxt.rectTransform.anchorMax = Vector2.one; nameTxt.rectTransform.offsetMin = new Vector2(8, 2); nameTxt.rectTransform.offsetMax = new Vector2(-8, -2); nameTxt.overflowMode = TextOverflowModes.Ellipsis; nameTxt.enableAutoSizing = true; nameTxt.fontSizeMin = 10; nameTxt.fontSizeMax = 18;
    }

    GameObject CreateScoreRow(string name, Transform parent, string[] cells, float width, float height, Color color, bool bold) { var rowGo = NewRow(name, parent, width, height); for (int i = 0; i < cells.Length; i++) AddCellLabel(rowGo.transform, cells[i], color, bold ? 30 : 28, bold ? FontStyles.Bold : FontStyles.Normal); AddRowDashedLine(rowGo, width, height); return rowGo; }
    GameObject NewRow(string name, Transform parent, float width, float height) { var rowGo = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)); rowGo.transform.SetParent(parent, false); var rrt = rowGo.GetComponent<RectTransform>(); rrt.sizeDelta = new Vector2(width, height); var hlg = rowGo.AddComponent<HorizontalLayoutGroup>(); hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true; hlg.childAlignment = TextAnchor.MiddleCenter; var le = rowGo.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minHeight = height; le.preferredWidth = width; return rowGo; }
    static void MakeEqualColumn(GameObject cell) { var le = cell.GetComponent<LayoutElement>(); if (le == null) le = cell.AddComponent<LayoutElement>(); le.minWidth = 0f; le.preferredWidth = 0f; le.flexibleWidth = 1f; }
    void AddCellLabel(Transform rowParent, string text, Color color, int maxSize, FontStyles style) { var cellGo = new GameObject("Cell", typeof(RectTransform)); cellGo.transform.SetParent(rowParent, false); MakeEqualColumn(cellGo); var tmp = cellGo.AddComponent<TextMeshProUGUI>(); tmp.text = text; tmp.color = color; tmp.alignment = TextAlignmentOptions.Center; tmp.fontStyle = style; tmp.enableAutoSizing = true; tmp.fontSizeMin = 12; tmp.fontSizeMax = maxSize; tmp.overflowMode = TextOverflowModes.Ellipsis; tmp.raycastTarget = false; if (customFont != null) tmp.font = customFont; }

    void AddRowDashedLine(GameObject rowGo, float width, float height) { var line = CreateRect("DashedLine_Row", rowGo.transform, new Vector2(0f, -height / 2f + 2f), new Vector2(width - 8f, 4f)); var le = line.AddComponent<LayoutElement>(); le.ignoreLayout = true; const float dashW = 20f; const float gap = 14f; const float step = dashW + gap; int count = Mathf.Max(1, Mathf.FloorToInt((width - 8f) / step)); float used = (count * step) - gap; float startX = (-used / 2f) + (dashW / 2f); for (int i = 0; i < count; i++) { var dash = CreateRect("Dash", line.transform, new Vector2(startX + (i * step), 0f), new Vector2(dashW, 4f)); var img = AddImage(dash, new Color(0.12f, 0.06f, 0.02f, 0.6f)); img.raycastTarget = false; } }

    void BuildVerticalDividers(Transform container, float innerW) { var crt = container as RectTransform; float h = (crt != null && crt.rect.height > 1f) ? crt.rect.height - 40f : 540f; float col = innerW / 6f; AddVerticalDivider(container, -innerW / 2f + col, h); AddVerticalDivider(container, -innerW / 2f + col * 5f, h); }
    void AddVerticalDivider(Transform container, float x, float height) { var go = CreateRect("VDivider", container, new Vector2(x, 0f), new Vector2(3f, height)); var le = go.AddComponent<LayoutElement>(); le.ignoreLayout = true; var img = AddImage(go, new Color(0.12f, 0.06f, 0.02f, 0.45f)); img.raycastTarget = false; go.transform.SetAsFirstSibling(); _dynamicDecor.Add(go); }

    void EnsureSceneButtons(Transform mainFrame)
    {
        Transform btnContainer = mainFrame.Find("ButtonsContainer"); if (btnContainer == null) return;
        WireSceneButton(ref restartButton, btnContainer, "RestartButton", OnRestartClicked); WireSceneButton(ref homeButton, btnContainer, "HomeButton", OnHomeClicked);
    }

    void WireSceneButton(ref Button btn, Transform container, string childName, UnityEngine.Events.UnityAction action)
    {
        if (btn == null) { Transform existing = container.Find(childName); if (existing != null) btn = existing.GetComponent<Button>(); }
        if (btn == null) { Color fallbackColor = childName == "RestartButton" ? new Color(0.12f, 0.65f, 0.28f) : new Color(0.85f, 0.35f, 0.15f); string label = childName == "RestartButton" ? "PLAY AGAIN" : "HOME"; var go = CreateRect(childName, container, Vector2.zero, new Vector2(280, 90)); AddImage(go, fallbackColor); btn = go.AddComponent<Button>(); btn.targetGraphic = go.GetComponent<Image>(); var newTmp = AddTmp(go.transform, label, Color.white, 30, TextAlignmentOptions.Center, FontStyles.Bold); newTmp.rectTransform.sizeDelta = new Vector2(280, 90); var colors = btn.colors; colors.highlightedColor = fallbackColor * 1.2f; colors.pressedColor = fallbackColor * 0.8f; btn.colors = colors; }
        if (btn.transform.parent != container) btn.transform.SetParent(container, false); EnableButtonVisuals(btn); btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(action);
    }

    static void EnableButtonVisuals(Button btn) { if (btn == null) return; btn.enabled = true; btn.interactable = true; btn.gameObject.SetActive(true); var img = btn.GetComponent<Image>(); if (img != null) img.enabled = true; }

    void CreateBannerAd()
    {
        if (resultPanel == null) return;
        Transform existing = resultPanel.transform.Find("BannerAdPlacement"); GameObject banner = existing != null ? existing.gameObject : null;
        if (banner == null) { banner = new GameObject("BannerAdPlacement", typeof(RectTransform)); banner.transform.SetParent(resultPanel.transform, false); }
        var rt = banner.GetComponent<RectTransform>(); rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f); rt.offsetMin = new Vector2(0f, 0f); rt.offsetMax = new Vector2(0f, BannerAdHeightPx);
        ResolveThemeSprites(); var bg = AddImage(banner, new Color(0.96f, 0.94f, 0.90f, 0.97f)); if (_roundedSprite != null) { bg.sprite = _roundedSprite; bg.type = UnityEngine.UI.Image.Type.Sliced; } bg.raycastTarget = false; bg.enabled = false;
        banner.transform.SetAsLastSibling();
    }

    void RefreshLeaderboardHeader(Transform container)
    {
        if (container == null) return; Transform header = container.Find("HeaderRow"); if (header == null) return; RefreshPlayerNamesAndActors();
        var headerCells = new List<Transform>(); for (int i = 0; i < header.childCount; i++) { Transform child = header.GetChild(i); if (child.name == "PlayerHeaderCell") headerCells.Add(child); }
        for (int seat = 0; seat < headerCells.Count && seat < 4; seat++) { Transform cell = headerCells[seat]; string displayName = GetSeatDisplayName(seat); Transform nameBox = cell.Find("NameBox"); if (nameBox != null) { var nameTmp = nameBox.GetComponentInChildren<TextMeshProUGUI>(true); if (nameTmp != null) { nameTmp.text = displayName; } } Transform avatarT = cell.Find("AvatarImage") ?? FindDeepByName(cell, "AvatarImage"); if (avatarT != null) { var avatarImg = avatarT.GetComponent<Image>(); if (avatarImg != null) { Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seat)); if (avatar != null) avatarImg.sprite = avatar; } } }
    }

    void HideMatchFinishedLabel() { if (resultPanel == null) return; Transform tag = resultPanel.transform.Find("MatchFinishedTag"); if (tag != null) tag.gameObject.SetActive(false); }

    static Image AddImage(GameObject go, Color c) { var img = go.GetComponent<Image>(); if (img == null) img = go.AddComponent<Image>(); img.color = c; return img; }
    TextMeshProUGUI AddTmp(Transform parent, string text, Color color, int size, TextAlignmentOptions align, FontStyles style = FontStyles.Normal) { var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(200, 50); var tmp = go.GetComponent<TextMeshProUGUI>(); tmp.text = text; tmp.color = color; tmp.fontSize = size; tmp.alignment = align; tmp.fontStyle = style; tmp.raycastTarget = false; if (customFont != null) tmp.font = customFont; return tmp; }

    void ClearDynamicUI() { foreach (var go in _overflowRows) if (go != null) Destroy(go); _overflowRows.Clear(); }

    void OnHomeClicked()
    {
        if (_resultActionTaken) return; _resultActionTaken = true;
        HideResultPanelImmediate(); ResetMatchStats();
        bool leaving = PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Leaving;
        if (PhotonNetwork.InRoom && !leaving) PhotonNetwork.LeaveRoom(); else if (PhotonNetwork.OfflineMode) { if (!leaving) PhotonNetwork.LeaveRoom(); PhotonNetwork.OfflineMode = false; }
        if (DeckManager.Instance != null) DeckManager.Instance.ResetMatchState();
        if (NetworkManager.Instance != null) NetworkManager.Instance.ReturnToHomeScreen();
    }

    void OnRestartClicked()
    {
        if (_resultActionTaken) return; if (!PhotonNetwork.OfflineMode && !(PhotonNetwork.InRoom && PhotonNetwork.IsConnectedAndReady)) return; _resultActionTaken = true;
        HideResultPanelImmediate(); ResetMatchStats();
        if (DeckManager.Instance != null) DeckManager.Instance.ResetMatchState();
        GameFlowState.SetPhase(GameFlowPhase.InRoom);
        if (PhotonNetwork.OfflineMode) { if (DeckManager.Instance != null && PhotonNetwork.IsMasterClient) DeckManager.Instance.FillBotsAndStart(); }
        else if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null) { DeckManager.Instance.FillBotsAndStart(); }
    }

    void ResetMatchStats()
    {
        _statsRecorded = false; _matchId = null; _roundTransitionRunning = false; currentRound = 1; maxRounds = MaxRoundsBotsOnline; roundHistory.Clear(); ResetRoundPlayerStats();
        for (int i = 0; i < 4; i++) { playerResults[i].bid = 0; playerResults[i].name = GetInitialPlayerName(i); }
    }

    Sprite GetAvatarSprite(int actorNumber)
    {
        Sprite[] pool = null;
        if (PlayerProfileManager.Instance != null && PlayerProfileManager.Instance.profileSprites != null && PlayerProfileManager.Instance.profileSprites.Length > 0) pool = PlayerProfileManager.Instance.profileSprites;
        else if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0) pool = MatchmakingManager.GlobalProfileSprites.ToArray();
        if (pool == null || pool.Length == 0) return null;

        int spriteIndex = -1;
        if (PhotonNetwork.LocalPlayer != null && actorNumber == PhotonNetwork.LocalPlayer.ActorNumber) { int local = PlayerProfileManager.GetSavedAvatarIndex(); if (local >= 0) spriteIndex = local; }
        if (spriteIndex < 0 && PhotonNetwork.CurrentRoom != null) { Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber); if (p != null && p.CustomProperties != null && p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val) && val != null) { if (val is int vi) spriteIndex = vi; else if (int.TryParse(val.ToString(), out int parsed)) spriteIndex = parsed; } }
        
        if (spriteIndex < 0 || spriteIndex >= pool.Length) spriteIndex = Mathf.Abs(actorNumber) % pool.Length;
        return pool[spriteIndex];
    }

    void RecordMatchStats()
    {
        if (_statsRecorded) return; _statsRecorded = true; PlayerResult me = playerResults != null && playerResults.Length > 0 ? playerResults[0] : null; if (me == null) return;
        bool vsBots = PhotonNetwork.OfflineMode || (DeckManager.botActorNumbers != null && DeckManager.botActorNumbers.Count > 0);
        int rank = me.rank <= 0 ? 4 : me.rank; bool kot = me.dehlasCollected >= GetKotThreshold();
        ProfileStatsStore.RecordCompletedGame(vsBots, rank, me.score, me.bid, kot);
    }
    
    GameObject CreateRect(string name, Transform parent, Vector2 pos, Vector2 size) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = size; rt.anchoredPosition = pos; return go; }
}