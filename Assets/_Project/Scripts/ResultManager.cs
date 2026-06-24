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
using System.Reflection;

public class ResultManager : MonoBehaviourPunCallbacks
{
    public static ResultManager Instance;

    [Header("UI Root")]
    public CanvasGroup resultPanel;
    [Tooltip("Optional root to find Panel_Winning without GameObject.Find (e.g. game canvas).")]
    public Transform resultPanelSearchRoot;
    public TMP_FontAsset customFont;

    [Header("Optional — wired in scene or auto-built")]
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public Button homeButton;
    public Button restartButton;
    public Transform scoreboardContainer;

    [Header("Leaderboard Theme (assign in scene)")]
    [Tooltip("Rounded wooden board sprite used for the panel background (e.g. BG_Buttons).")]
    public Sprite woodBoardSprite;
    [Tooltip("Settings gear icon sprite (e.g. settings_button).")]
    public Sprite gearButtonSprite;
    [Tooltip("Fixed avatar shown for every player column in the leaderboard. Assign once here — this exact sprite is what appears in game (no runtime randomization).")]
    public Sprite playerAvatarSprite;

    [System.Serializable]
    public class PlayerResult
    {
        public string name;
        public int actorNumber;
        public int bid; // Restored
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
    /// <summary>5 for Bots/Online, -1 for unlimited Friends matches.</summary>
    public int maxRounds = 5;
    public List<RoundResult> roundHistory = new List<RoundResult>();

    const int MaxRoundsBotsOnline = 5;
    // Task 13: keep the leaderboard visible for 5 seconds after a round before auto-closing.
    const float InterRoundLeaderboardSeconds = 5f;
    const float MatchEndLeaderboardSeconds = 10f;

    private PlayerResult[] playerResults = new PlayerResult[4];
    private Transform _builtRoot;
    private Image _dimOverlay;
    private readonly List<GameObject> _dynamicRows = new List<GameObject>();
    // Runtime-only extra round rows created when a Friends match runs past the static row count.
    private readonly List<GameObject> _overflowRows = new List<GameObject>();
    private bool _isShowingResult;
    private bool _statsRecorded;
    private bool _resultActionTaken;
    private bool _autoTransitionMode;
    private bool _roundTransitionRunning;
    private ScrollRect _roundScrollRect;
    private static bool _resultPanelResolveWarned;

    /// <summary>
    /// Records the local player's finished match into <see cref="ProfileStatsStore"/>, split by
    /// Vs Bots / Vs Online. Guarded so it only counts once per match.
    /// </summary>
    void RecordMatchStats()
    {
        if (_statsRecorded) return;
        _statsRecorded = true;

        PlayerResult me = playerResults != null && playerResults.Length > 0 ? playerResults[0] : null;
        if (me == null) return;

        bool vsBots = PhotonNetwork.OfflineMode ||
                      (DeckManager.botActorNumbers != null && DeckManager.botActorNumbers.Count > 0);
        int rank = me.rank <= 0 ? 4 : me.rank;
        bool kot = me.dehlasCollected >= GetKotThreshold();

        ProfileStatsStore.RecordCompletedGame(vsBots, rank, me.score, me.bid, kot);
        Debug.Log($"[Stats] Recorded {(vsBots ? "VsBots" : "Online")} game: rank={rank} score={me.score} bid={me.bid} kot={kot}");
    }

    const int KotDehlasOneTaash = 4;
    const int KotDehlasTwoTaash = 8;

    // Professional Theme Colors
    static readonly Color PanelBgColor = new Color(0.25f, 0.15f, 0.05f, 0.95f); // Wooden Dark
    static readonly Color FrameColor = new Color(0.45f, 0.28f, 0.15f, 1f);     // Wooden Frame
    static readonly Color RowBgColor = new Color(0f, 0f, 0f, 0.35f);           // Semi-transparent rows
    static readonly Color WinnerGoldColor = new Color(1f, 0.84f, 0f, 1f);      // Gold highlight
    static readonly Color TextWhiteColor = Color.white;
    static readonly Color TextGoldColor = new Color(1f, 0.92f, 0.5f, 1f);
    static readonly Color ScoreDarkColor = new Color(0.16f, 0.09f, 0.04f, 1f);

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
        if (homeButton != null)
        {
            EnableButtonVisuals(homeButton);
            homeButton.onClick.RemoveAllListeners();
            homeButton.onClick.AddListener(OnHomeClicked);
        }
        if (restartButton != null)
        {
            EnableButtonVisuals(restartButton);
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(OnRestartClicked);
        }
    }

    static void ShowLeaderboardBanner()
    {
        if (AdsManager.Instance == null) return;
        AdsManager.Instance.LoadBanner();
        AdsManager.Instance.ShowBanner();
    }

    static void HideLeaderboardBanner()
    {
        if (AdsManager.Instance == null) return;
        AdsManager.Instance.HideBanner();
    }

    void HideResultPanelImmediate()
    {
        _isShowingResult = false;
        HideLeaderboardBanner();
        if (!ResolveResultPanel()) return;
        resultPanel.DOKill();
        resultPanel.alpha = 0;
        resultPanel.interactable = false;
        resultPanel.blocksRaycasts = false;
        resultPanel.gameObject.SetActive(false);
        if (_dimOverlay != null)
            _dimOverlay.color = new Color(0f, 0f, 0f, 0f);
    }

    bool ResolveResultPanel()
    {
        if (resultPanel != null) return true;

        Transform root = resultPanelSearchRoot;
        if (root == null)
        {
            Canvas canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas != null)
                root = canvas.transform.root;
        }

        if (root != null)
        {
            UiSafeLookup.SetSearchRoot(root);
            if (UiSafeLookup.TryGet("Panel_Winning", out GameObject panelGo) && panelGo != null)
            {
                resultPanel = panelGo.GetComponent<CanvasGroup>();
                if (resultPanel == null)
                    resultPanel = panelGo.AddComponent<CanvasGroup>();
                Debug.Log("[ResultManager] Resolved Panel_Winning under canvas hierarchy.");
                return true;
            }
        }

        if (!_resultPanelResolveWarned)
        {
            _resultPanelResolveWarned = true;
            Debug.LogWarning("[ResultManager] resultPanel not found — assign resultPanel or Panel_Winning under resultPanelSearchRoot.");
        }
        return false;
    }

    void EnsurePanelHierarchyActive()
    {
        if (resultPanel == null) return;

        Transform t = resultPanel.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }

        Canvas rootCanvas = resultPanel.GetComponentInParent<Canvas>();
        if (rootCanvas != null)
            rootCanvas.gameObject.SetActive(true);

        resultPanel.transform.SetAsLastSibling();
    }

    void EnsureDimOverlay()
    {
        if (resultPanel == null) return;

        Transform existing = resultPanel.transform.Find("Overlay");
        if (existing != null)
        {
            _dimOverlay = existing.GetComponent<Image>();
            if (_dimOverlay == null)
                _dimOverlay = existing.gameObject.AddComponent<Image>();
        }
        else
        {
            GameObject overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            overlayGo.transform.SetParent(resultPanel.transform, false);
            overlayGo.transform.SetAsFirstSibling();
            RectTransform rt = overlayGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _dimOverlay = overlayGo.GetComponent<Image>();
            _dimOverlay.raycastTarget = true;
        }

        _dimOverlay.color = new Color(0f, 0f, 0f, 0.55f);
        _dimOverlay.raycastTarget = true;

        // Task 16: tapping outside the board (on the full-screen overlay) closes the leaderboard.
        var overlayBtn = _dimOverlay.GetComponent<Button>();
        if (overlayBtn == null) overlayBtn = _dimOverlay.gameObject.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.RemoveAllListeners();
        overlayBtn.onClick.AddListener(CloseResult);
    }

    string GetInitialPlayerName(int i)=> i == 0 ? "You" : "Dehla_AI_" + i;

    public void SetBid(int seatIndex, int bidValue)
    {
        if (seatIndex >= 0 && seatIndex < 4)
            playerResults[seatIndex].bid = bidValue;
    }

    public void OnTrickWon(int winnerSeatIndex, int dehlaCount)
    {
        if (winnerSeatIndex < 0 || winnerSeatIndex >= 4) return;
        playerResults[winnerSeatIndex].tricksWon++;
        playerResults[winnerSeatIndex].dehlasCollected += dehlaCount;

        if (PhotonNetwork.IsMasterClient)
            SyncScoresToRoomProperties();
    }

    void SyncScoresToRoomProperties()
    {
        if (!PhotonNetwork.InRoom) return;
        int[] tricks = new int[4];
        int[] dehlas = new int[4];
        for (int i = 0; i < 4; i++)
        {
            tricks[i] = playerResults[i].tricksWon;
            dehlas[i] = playerResults[i].dehlasCollected;
        }
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new PhotonHashtable { { "SW", tricks }, { "DL", dehlas } });
    }

    public override void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null) return;
        if (propertiesThatChanged.ContainsKey("CR") &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CR", out object crObj))
            currentRound = (int)crObj;
        if (propertiesThatChanged.ContainsKey("MR") &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("MR", out object mrObj))
            maxRounds = (int)mrObj;
        if (propertiesThatChanged.ContainsKey("SW") &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("SW", out object tricksObj))
        {
            int[] tricks = tricksObj as int[];
            for (int i = 0; tricks != null && i < 4 && i < tricks.Length; i++)
                playerResults[i].tricksWon = tricks[i];
        }
        if (propertiesThatChanged.ContainsKey("DL") &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DL", out object dehlaObj))
        {
            int[] dehlas = dehlaObj as int[];
            for (int i = 0; dehlas != null && i < 4 && i < dehlas.Length; i++)
                playerResults[i].dehlasCollected = dehlas[i];
        }
    }

    public void InitializeForMatch()
    {
        bool unlimited = GameSettings.Instance != null
            && GameSettings.Instance.currentMatchType == MatchType.PlayWithFriends;
        if (!unlimited && DeckManager.IsPrivateFriendsRoom())
            unlimited = true;

        maxRounds = unlimited ? -1 : MaxRoundsBotsOnline;
        currentRound = 1;
        roundHistory.Clear();
        _roundTransitionRunning = false;
        _statsRecorded = false;
        ResetRoundPlayerStats();

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
            SyncRoundConfigToRoom();
    }

    void SyncRoundConfigToRoom()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable
        {
            { "CR", currentRound },
            { "MR", maxRounds }
        });
    }

    public bool IsMatchOver() => maxRounds != -1 && currentRound >= maxRounds;

    /// <summary>Master-only entry when the 13th trick of a round completes.</summary>
    public void TriggerRoundCompletedFromMaster()
    {
        if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.OfflineMode) return;
        if (DeckManager.Instance != null && DeckManager.Instance.photonView != null)
            DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_OnRoundCompleted), RpcTarget.All);
        else
            OnRoundCompleted();
    }

    public void OnRoundCompleted()
    {
        if (_roundTransitionRunning) return;

        if (TurnManager.Instance != null)
            TurnManager.Instance.StopTimer();

        EnsurePlayerResults();
        RefreshPlayerNamesAndActors();
        CalculateScores();
        AssignRanks();
        FinalizeCurrentRoundScores();

        bool matchOver = IsMatchOver();
        if (matchOver)
            GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);

        ShowRoundLeaderboard(matchOver);

        _roundTransitionRunning = true;
        bool authoritative = PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode;
        StartCoroutine(RoundTransitionRoutine(matchOver, authoritative));
    }

    IEnumerator RoundTransitionRoutine(bool matchOver, bool authoritative)
    {
        float wait = matchOver ? MatchEndLeaderboardSeconds : InterRoundLeaderboardSeconds;
        yield return new WaitForSecondsRealtime(wait);

        HideResultPanelImmediate();
        _roundTransitionRunning = false;

        if (!authoritative)
            yield break;

        if (matchOver)
        {
            AssignMatchRanksFromHistory();
            RecordMatchStats();
            if (DeckManager.Instance != null)
                DeckManager.Instance.ResetMatchState();

            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();
            else if (NetworkManager.Instance != null)
                NetworkManager.Instance.ReturnToHomeScreen();
            yield break;
        }

        int nextRound = currentRound + 1;
        if (DeckManager.Instance != null)
        {
            if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
                DeckManager.Instance.ResetRoundStateForNextRound();
            if (DeckManager.Instance.photonView != null)
                DeckManager.Instance.photonView.RPC(nameof(DeckManager.RPC_BeginNextRound), RpcTarget.All, nextRound);
        }
    }

    public void ApplyNextRoundStart(int newRound)
    {
        currentRound = newRound;
        ResetRoundPlayerStats();
        if (PhotonNetwork.IsMasterClient)
            SyncRoundConfigToRoom();
    }

    void ShowRoundLeaderboard(bool matchOver)
    {
        ShowResultInternal(autoTransition: true, matchOver: matchOver);
    }

    public void CloseResult()
    {
        HideResultPanelImmediate();
    }

    void EnsurePlayerResults()
    {
        if (playerResults == null || playerResults.Length < 4)
            playerResults = new PlayerResult[4];

        for (int i = 0; i < 4; i++)
        {
            if (playerResults[i] == null)
                playerResults[i] = new PlayerResult { name = GetInitialPlayerName(i) };
        }
    }

    [ContextMenu("Show Test Result")]
    public void ShowResult()
    {
        ShowResultInternal(autoTransition: false, matchOver: false);
    }

    void ShowResultInternal(bool autoTransition, bool matchOver)
    {
        if (_isShowingResult)
        {
            Debug.LogWarning("[Result] ShowResult ignored — already showing.");
            return;
        }
        if (!ResolveResultPanel())
        {
            Debug.LogError("[Result] ShowResult aborted — result panel reference missing.");
            return;
        }

        _isShowingResult = true;
        _resultActionTaken = false;
        _autoTransitionMode = autoTransition;
        Debug.Log(autoTransition
            ? $"[Result] Round {currentRound} leaderboard (matchOver={matchOver})"
            : "Result Panel Opening");
        EnsurePlayerResults();
        EnsurePanelHierarchyActive();
        EnsureDimOverlay();

        if (!autoTransition)
        {
            RefreshPlayerNamesAndActors();
            CalculateScores();
            AssignRanks();
            RecordMatchStats();
        }

        PlayerResult winner = playerResults.OrderBy(p => p.rank).FirstOrDefault();
        if (winner != null)
            Debug.Log($"Winner Determined: {winner.name} (Rank #{winner.rank}, Score {winner.score})");

        BuildResultPanelUI();
        SetActionButtonsVisible(!autoTransition);
        StartCoroutine(ScrollLeaderboardToBottom());

        resultPanel.gameObject.SetActive(true);
        ShowLeaderboardBanner();
        ResetPanelOpenStateInstant();
        Debug.Log("Result Panel Opened");

        CreateBannerAd();
        HideMatchFinishedLabel();
    }

    /// <summary>No open animation — panel and MainFrame stay at full scene-authored size/scale.</summary>
    void ResetPanelOpenStateInstant()
    {
        if (resultPanel == null) return;

        resultPanel.DOKill(complete: true);
        resultPanel.alpha = 1f;
        resultPanel.interactable = true;
        resultPanel.blocksRaycasts = true;

        Transform root = resultPanel.transform;
        root.DOKill(complete: true);
        root.localScale = Vector3.one;

        if (_dimOverlay != null)
        {
            _dimOverlay.DOKill(complete: true);
            Color c = _dimOverlay.color;
            c.a = 0.55f;
            _dimOverlay.color = c;
        }

        Transform frame = root.Find("MainFrame");
        if (frame != null)
        {
            frame.DOKill(complete: true);
            frame.localScale = Vector3.one;
            frame.SetAsLastSibling();

            // Lift the board above the banner without shrinking it (no offsetMin / sizeDelta hacks).
            var frt = frame as RectTransform;
            if (frt != null)
            {
                Vector2 pos = frt.anchoredPosition;
                if (pos.y < 55f) pos.y = 55f;
                frt.anchoredPosition = pos;
            }
        }
    }

    IEnumerator ScrollLeaderboardToBottom()
    {
        yield return null;
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (_roundScrollRect != null)
            _roundScrollRect.verticalNormalizedPosition = 0f;
    }

    void SetActionButtonsVisible(bool visible)
    {
        if (homeButton != null) homeButton.gameObject.SetActive(visible);
        if (restartButton != null) restartButton.gameObject.SetActive(visible);

        Transform mainFrame = resultPanel != null ? resultPanel.transform.Find("MainFrame") : null;
        if (mainFrame != null)
        {
            Transform btnContainer = mainFrame.Find("ButtonsContainer");
            if (btnContainer != null) btnContainer.gameObject.SetActive(visible);
            Transform closeBtn = mainFrame.Find("CloseButton");
            if (closeBtn != null) closeBtn.gameObject.SetActive(visible);
        }
    }

    void RefreshPlayerNamesAndActors()
    {
        for (int seat = 0; seat < 4; seat++)
        {
            playerResults[seat].name = GetSeatDisplayName(seat);
            playerResults[seat].actorNumber = GetActorNumberBySeat(seat);
        }
    }

    int GetActorNumberBySeat(int seatIndex)
    {
        if (PlayerHand.LocalInstance == null) return seatIndex; 
        
        // tableTurnOrder is indexed by visual seat (0-3).
        var field = typeof(PlayerHand).GetField("tableTurnOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null) return -1;
        
        var turnOrder = (List<int>)field.GetValue(PlayerHand.LocalInstance);
        if (turnOrder != null && seatIndex < turnOrder.Count)
            return turnOrder[seatIndex];
            
        return -1;
    }

    string GetSeatDisplayName(int seatIndex)
    {
        if (seatIndex == 0)
            return PlayerProfileSync.GetLocalProfileDisplayName();

        if (PlayerProfileSync.Instance != null)
        {
            switch (seatIndex)
            {
                case 1 when PlayerProfileSync.Instance.txtLeftName != null:
                    return CleanName(PlayerProfileSync.Instance.txtLeftName.text);
                case 2 when PlayerProfileSync.Instance.txtTopName != null:
                    return CleanName(PlayerProfileSync.Instance.txtTopName.text);
                case 3 when PlayerProfileSync.Instance.txtRightName != null:
                    return CleanName(PlayerProfileSync.Instance.txtRightName.text);
            }
        }
        return "Player " + (seatIndex + 1);
    }

    static string CleanName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Player";
        return raw.Split('\n')[0].Trim();
    }

    void FinalizeCurrentRoundScores()
    {
        var result = new RoundResult { roundNumber = currentRound };
        for (int seat = 0; seat < 4; seat++)
        {
            result.dehlasPerSeat[seat] = playerResults[seat].dehlasCollected;
            result.tricksPerSeat[seat] = playerResults[seat].tricksWon;
        }

        if (roundHistory.Count > 0 && roundHistory[roundHistory.Count - 1].roundNumber == currentRound)
            roundHistory[roundHistory.Count - 1] = result;
        else
            roundHistory.Add(result);

        Debug.Log($"[Result] Round R{currentRound} finalized: " +
                  string.Join(", ", Enumerable.Range(0, 4).Select(i => $"{playerResults[i].name}={result.dehlasPerSeat[i]}")));
    }

    void ResetRoundPlayerStats()
    {
        for (int i = 0; i < 4; i++)
        {
            playerResults[i].tricksWon = 0;
            playerResults[i].dehlasCollected = 0;
            playerResults[i].score = 0;
            playerResults[i].isCompleted = false;
            playerResults[i].rank = 0;
        }

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new PhotonHashtable
            {
                { "SW", new int[4] },
                { "DL", new int[4] },
                { "TP", 0 }
            });
        }
    }

    void AssignMatchRanksFromHistory()
    {
        int[] totals = new int[4];
        foreach (RoundResult round in roundHistory)
            for (int i = 0; i < 4; i++)
                totals[i] += round.dehlasPerSeat[i];

        for (int i = 0; i < 4; i++)
        {
            playerResults[i].dehlasCollected = totals[i];
            playerResults[i].score = totals[i];
        }

        var ranked = totals.Select((value, index) => (value, index)).OrderByDescending(x => x.value).ToList();
        for (int r = 0; r < ranked.Count; r++)
            playerResults[ranked[r].index].rank = r + 1;
    }

    static int GetKotThreshold()
    {
        return TaashRules.IsTwoTaashMode ? KotDehlasTwoTaash : KotDehlasOneTaash;
    }

    static string FormatDehlaScore(int dehlas)
    {
        int kotThreshold = GetKotThreshold();
        return dehlas == kotThreshold ? $"{dehlas} (KOT)" : dehlas.ToString();
    }

    static int SumRound(int[] roundScores)
    {
        int total = 0;
        for (int i = 0; i < roundScores.Length; i++)
            total += roundScores[i];
        return total;
    }

    void CalculateScores()
    {
        // Simple scoring based on Dehlas and tricks
        foreach (var p in playerResults)
        {
            p.score = (p.tricksWon * 10) + (p.dehlasCollected * 20);
            p.isCompleted = true;
        }
    }

    void AssignRanks()
    {
        EnsurePlayerResults();
        UpdateAndSortLeaderboard(new List<PlayerResult>(playerResults));
    }

    /// <summary>
    /// Task 28 — Robust leaderboard ranking. Sorts players strictly by Score (descending), then
    /// breaks ties deterministically: more Dehlas (KOT cards) first, then more Tricks won, then a
    /// stable fallback on actorNumber so the order never flickers between equal players. Null-safe:
    /// silently skips players that disconnected/were removed right before the leaderboard is built,
    /// so a missing player can never break the round-end game loop. Ranks are written back onto the
    /// surviving PlayerResult objects (rank 1 = winner).
    /// </summary>
    public void UpdateAndSortLeaderboard(List<PlayerResult> currentPlayers)
    {
        if (currentPlayers == null) return;

        // Drop any null entries (a player object can be missing if they left right at round end).
        List<PlayerResult> valid = currentPlayers.Where(p => p != null).ToList();
        if (valid.Count == 0) return;

        valid.Sort(CompareForLeaderboard);

        for (int i = 0; i < valid.Count; i++)
            valid[i].rank = i + 1;
    }

    /// <summary>
    /// Leaderboard comparator: Score desc -> Dehlas (KOT) desc -> Tricks desc -> actorNumber asc.
    /// Returns negative when <paramref name="a"/> should rank ABOVE <paramref name="b"/>.
    /// </summary>
    static int CompareForLeaderboard(PlayerResult a, PlayerResult b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;   // nulls sink to the bottom
        if (b == null) return -1;

        int byScore = b.score.CompareTo(a.score);            // higher score first
        if (byScore != 0) return byScore;

        int byDehlas = b.dehlasCollected.CompareTo(a.dehlasCollected); // KOT/Dehla tiebreak
        if (byDehlas != 0) return byDehlas;

        int byTricks = b.tricksWon.CompareTo(a.tricksWon);   // tricks (wins) tiebreak
        if (byTricks != 0) return byTricks;

        return a.actorNumber.CompareTo(b.actorNumber);       // stable, deterministic fallback
    }

    void BuildResultPanelUI()
    {
        // The Panel_Winning leaderboard skeleton (header + avatars + name plates + R1..R5 rows +
        // dividers) lives in the scene hierarchy and is fully editable. At runtime we only ensure it
        // exists, then fill score data into it — we never recreate the header/avatars/name plates so
        // the authored layout, fonts and text stay exactly as set in the Editor.
        ClearDynamicUI();

        if (resultPanel == null) return;

        Transform mainFrame = resultPanel.transform.Find("MainFrame");
        if (mainFrame == null)
        {
            Debug.LogError("[Result] MainFrame not found under Panel_Winning. Cannot fill result UI.");
            return;
        }

        mainFrame.localScale = Vector3.one;

        // Task 14: reduce the board's rounded-corner radius (9-sliced sprite + higher PPU shrinks the corners).
        var mainFrameImg = mainFrame.GetComponent<Image>();
        if (mainFrameImg != null)
        {
            mainFrameImg.type = Image.Type.Sliced;
            mainFrameImg.pixelsPerUnitMultiplier = 1.8f;
        }

        Transform rowsContainer = scoreboardContainer != null
            ? scoreboardContainer
            : mainFrame.Find("PlayerRowsContainer");

        if (rowsContainer != null)
        {
            EnsureStaticLeaderboard(rowsContainer);
            RefreshLeaderboardHeader(rowsContainer);
            FillLeaderboardData(rowsContainer);
        }
        else
        {
            Debug.LogWarning("[Result] PlayerRowsContainer not found under MainFrame — scores not filled.");
        }

        // Optional round-progress title, only if the user wired a titleText field.
        string title = maxRounds == -1
            ? $"Round {currentRound} Complete"
            : $"Round {currentRound} / {maxRounds}";
        if (titleText != null) titleText.text = title;

        // Wire the existing CloseButton from the hierarchy (do not create/restyle it).
        Transform closeT = mainFrame.Find("CloseButton");
        if (closeT != null)
        {
            var closeBtn = closeT.GetComponent<Button>();
            if (closeBtn != null)
            {
                EnableButtonVisuals(closeBtn);
                closeBtn.onClick.RemoveAllListeners();
                closeBtn.onClick.AddListener(CloseResult);
            }
        }

        EnsureSceneButtons(mainFrame);
    }

    // Decorative (non-animated) leaderboard elements, cleared together with the rows.
    private readonly List<GameObject> _dynamicDecor = new List<GameObject>();
    // Rounded button sprite reused for the name plate, pulled from the existing hand-made buttons.
    private Sprite _roundedSprite;

    static readonly Color LeaderLabelColor = new Color(1f, 0.92f, 0.5f, 1f);  // gold ROUNDS / TOTAL headers
    static readonly Color NameBoxColor = new Color(0.36f, 0.20f, 0.10f, 1f);  // brown name plate

    /// <summary>Number of round rows materialised as persistent, editable scene objects.</summary>
    const int StaticLeaderboardRows = 5;

    /// <summary>Builds the editable leaderboard skeleton once if it isn't already in the hierarchy.</summary>
    void EnsureStaticLeaderboard(Transform container)
    {
        if (container == null) return;
        if (container.Find("HeaderRow") != null) return; // already authored / built earlier
        BuildStaticLeaderboard(container, StaticLeaderboardRows);
    }

    /// <summary>
    /// Builds the persistent leaderboard skeleton: a header row (ROUNDS + four avatar/name columns +
    /// TOTAL), <paramref name="rowCount"/> round rows (R1..Rn) and the two vertical dividers. These
    /// objects are NOT tracked as dynamic, so they survive between shows and can be hand-edited in the
    /// scene (positions, fonts, text, avatars, name plates). Runtime only fills the score cells.
    /// </summary>
    public void BuildStaticLeaderboard(Transform container, int rowCount)
    {
        if (container == null) return;
        ResolveThemeSprites();

        float innerW = ComputeInnerWidth(container);
        const float headerH = 120f;
        const float rowH = 64f;

        // Header: ROUNDS | avatar x4 (fixed) | TOTAL
        CreateHeaderRow("HeaderRow", container, innerW, headerH);

        // One editable row per round slot (blank until scores are filled at runtime).
        for (int r = 1; r <= rowCount; r++)
        {
            string[] cells = new string[6];
            cells[0] = "R" + r;
            for (int s = 1; s < 6; s++) cells[s] = "";
            CreateScoreRow("RoundRow_" + r, container, cells, innerW, rowH, TextWhiteColor, false);
        }

        // Faint vertical dividers behind the table: after ROUNDS and before TOTAL.
        BuildVerticalDividers(container, innerW);
    }

    float ComputeInnerWidth(Transform container)
    {
        var crt = container as RectTransform;
        var vlg = container.GetComponent<VerticalLayoutGroup>();
        float innerW = 1040f;
        if (crt != null && crt.rect.width > 1f)
        {
            innerW = crt.rect.width;
            if (vlg != null) innerW -= (vlg.padding.left + vlg.padding.right);
        }
        return innerW;
    }

    /// <summary>
    /// Fills score values into the existing static round rows. Played rounds are filled, future rounds
    /// stay blank (matching the mock-up). Extra rows are only created at runtime when a Friends match
    /// runs past the static row count. The header row (avatars + names) is left untouched so it stays
    /// exactly as authored in the scene.
    /// </summary>
    void FillLeaderboardData(Transform container)
    {
        _dynamicRows.Clear();

        Transform header = container.Find("HeaderRow");
        if (header != null) _dynamicRows.Add(header.gameObject);

        int slots = maxRounds > 0 ? maxRounds : Mathf.Max(roundHistory.Count, 1);
        int total = Mathf.Max(slots, StaticLeaderboardRows);

        for (int r = 1; r <= total; r++)
        {
            Transform rowT = container.Find("RoundRow_" + r);
            GameObject rowGo;
            if (rowT != null)
            {
                rowGo = rowT.gameObject;
            }
            else
            {
                // Overflow round row (beyond the static rows) — created at runtime, cleared each show.
                string[] blank = new string[6];
                blank[0] = "R" + r;
                for (int s = 1; s < 6; s++) blank[s] = "";
                rowGo = CreateScoreRow("RoundRow_" + r, container, blank, ComputeInnerWidth(container), 64f, TextWhiteColor, false);
                _overflowRows.Add(rowGo);
            }

            rowGo.SetActive(true);
            rowGo.transform.localScale = Vector3.one;
            var rowCg = rowGo.GetComponent<CanvasGroup>();
            if (rowCg != null) rowCg.alpha = 1f;
            FillRowCells(rowGo, r);
            _dynamicRows.Add(rowGo);
        }

        // Task 28: cumulative standings row so the actual ranking is visible at a glance.
        BuildOrUpdateTotalsRow(container);

        // Unity's nested layout groups do not always solve in the same frame the panel is shown
        // (rects are still zero-sized when the cells are created). Defer one frame, flush the canvas,
        // then force an immediate rebuild so every column resolves to its innerW/6 share and lines up
        // with the vertical dividers.
        if (container is RectTransform containerRect)
            StartCoroutine(RebuildLeaderboardLayout(containerRect));
    }

    /// <summary>Forces the leaderboard layout to resolve after the rows have been generated.</summary>
    IEnumerator RebuildLeaderboardLayout(RectTransform containerTransform)
    {
        // Wait one frame so the container/cell RectTransforms have valid sizes.
        yield return null;
        if (containerTransform == null) yield break;
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(containerTransform);
    }

    /// <summary>
    /// Task 28: builds a bottom "TOTAL" row showing each player's cumulative dehlas across all rounds,
    /// highlighting the current leader. Tracked as an overflow row so it is rebuilt cleanly each show.
    /// </summary>
    void BuildOrUpdateTotalsRow(Transform container)
    {
        if (container == null) return;

        int[] totals = new int[4];
        foreach (RoundResult rr in roundHistory)
            for (int s = 0; s < 4 && s < rr.dehlasPerSeat.Length; s++)
                totals[s] += rr.dehlasPerSeat[s];
        int grand = totals[0] + totals[1] + totals[2] + totals[3];

        string[] cellTexts = new string[6];
        cellTexts[0] = "TOTAL";
        for (int s = 0; s < 4; s++) cellTexts[s + 1] = totals[s].ToString();
        cellTexts[5] = grand.ToString();

        GameObject rowGo = CreateScoreRow("TotalsRow", container, cellTexts, ComputeInnerWidth(container), 64f, ScoreDarkColor, true);
        _overflowRows.Add(rowGo);
        rowGo.transform.SetAsLastSibling();
        rowGo.SetActive(true);
        _dynamicRows.Add(rowGo);

        var tmps = new List<TextMeshProUGUI>();
        foreach (Transform child in rowGo.transform)
        {
            var t = child.GetComponent<TextMeshProUGUI>();
            if (t != null) tmps.Add(t);
        }
        if (tmps.Count < 6) return;

        for (int s = 0; s < 6; s++)
        {
            ApplyCellStyle(tmps[s], s == 0);
            tmps[s].color = ScoreDarkColor;
            tmps[s].fontStyle = FontStyles.Bold;
        }
    }

    /// <summary>Writes the round number, per-player dehla scores and the row total into a row's text cells.</summary>
    void FillRowCells(GameObject rowGo, int roundNumber)
    {
        var cells = new List<TextMeshProUGUI>();
        foreach (Transform child in rowGo.transform)
        {
            var tmp = child.GetComponent<TextMeshProUGUI>();
            if (tmp != null) cells.Add(tmp);
        }
        if (cells.Count < 6) return;

        RoundResult round = roundHistory.Find(rr => rr.roundNumber == roundNumber);
        cells[0].text = "R" + roundNumber;
        if (round != null)
        {
            for (int s = 0; s < 4; s++) cells[s + 1].text = FormatDehlaScore(round.dehlasPerSeat[s]);
            cells[5].text = SumRound(round.dehlasPerSeat).ToString();
        }
        else
        {
            for (int s = 1; s < 6; s++) cells[s].text = "";
        }

        bool isLatest = round != null && round.roundNumber == currentRound;
        // Dark, high-contrast text on the light-wood board (bold readable per design).
        Color rowColor = isLatest ? new Color(0.10f, 0.42f, 0.18f, 1f) : new Color(0.16f, 0.09f, 0.04f, 1f);
        for (int s = 0; s < 6; s++)
        {
            // Task 13 / 31: large, BOLD score cells — round label left, numbers right-aligned.
            ApplyCellStyle(cells[s], s == 0);
            cells[s].color = rowColor;
        }
    }

    /// <summary>Task 13/31: large bold cells — round label left-aligned, score numbers right-aligned.</summary>
    void ApplyCellStyle(TextMeshProUGUI cell, bool isRowLabel)
    {
        if (cell == null) return;
        cell.fontStyle = FontStyles.Bold;
        cell.enableAutoSizing = true;
        cell.fontSizeMin = 18;
        cell.fontSizeMax = 42;
        cell.overflowMode = TextOverflowModes.Overflow;
        if (isRowLabel)
        {
            cell.alignment = TextAlignmentOptions.MidlineLeft;
            cell.margin = new Vector4(18f, 0f, 0f, 0f);
        }
        else
        {
            cell.alignment = TextAlignmentOptions.MidlineRight;
            cell.margin = new Vector4(0f, 0f, 18f, 0f);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor helper: clears and regenerates the static leaderboard skeleton under PlayerRowsContainer
    /// so it can be hand-edited in the scene. Right-click the ResultManager component → this menu item.
    /// </summary>
    [ContextMenu("Rebuild Static Leaderboard")]
    void RebuildStaticLeaderboardEditor()
    {
        if (!ResolveResultPanel() || resultPanel == null)
        {
            Debug.LogError("[Result] Cannot build — Panel_Winning / resultPanel could not be resolved.");
            return;
        }
        Transform mainFrame = resultPanel.transform.Find("MainFrame");
        if (mainFrame == null) { Debug.LogError("[Result] MainFrame missing under Panel_Winning."); return; }
        Transform container = scoreboardContainer != null ? scoreboardContainer : mainFrame.Find("PlayerRowsContainer");
        if (container == null) { Debug.LogError("[Result] PlayerRowsContainer missing under MainFrame."); return; }

        for (int i = container.childCount - 1; i >= 0; i--)
            DestroyImmediate(container.GetChild(i).gameObject);

        BuildStaticLeaderboard(container, StaticLeaderboardRows);
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Debug.Log("[Result] Static leaderboard rebuilt under PlayerRowsContainer.");
    }
#endif

    /// <summary>Caches the rounded button sprite from the existing buttons so the name plate gets rounded corners in builds too.</summary>
    void ResolveThemeSprites()
    {
        if (resultPanel == null || _roundedSprite != null) return;
        var btnImg = FindImageDeep(resultPanel.transform, "HomeButton")
                  ?? FindImageDeep(resultPanel.transform, "RestartButton")
                  ?? FindImageDeep(resultPanel.transform, "NameBox")
                  ?? FindImageDeep(resultPanel.transform, "CloseButton");
        if (btnImg != null) _roundedSprite = btnImg.sprite;
    }

    static UnityEngine.UI.Image FindImageDeep(Transform root, string name)
    {
        Transform t = FindDeepByName(root, name);
        return t != null ? t.GetComponent<UnityEngine.UI.Image>() : null;
    }

    static Transform FindDeepByName(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform r = FindDeepByName(parent.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>Header row with a "ROUNDS" label, four fixed-avatar player columns and a "TOTAL" label.</summary>
    GameObject CreateHeaderRow(string name, Transform parent, float width, float height)
    {
        var rowGo = NewRow(name, parent, width, height);
        AddSideHeaderLabel(rowGo.transform, "ROUNDS", alignLeft: true, LeaderLabelColor, 30, FontStyles.Bold);
        for (int s = 0; s < 4; s++) CreateAvatarHeaderCell(rowGo.transform, s);
        AddCellLabel(rowGo.transform, "TOTAL", LeaderLabelColor, 30, FontStyles.Bold);
        AddRowDashedLine(rowGo, width, height);
        return rowGo;
    }

    /// <summary>ROUNDS / TOTAL header text nudged toward the outer edge so it clears the column dividers.</summary>
    void AddSideHeaderLabel(Transform rowParent, string text, bool alignLeft, Color color, int maxSize, FontStyles style)
    {
        var cellGo = new GameObject("Cell", typeof(RectTransform));
        cellGo.transform.SetParent(rowParent, false);
        MakeEqualColumn(cellGo);
        var tmp = cellGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = alignLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
        tmp.fontStyle = style;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 12;
        tmp.fontSizeMax = maxSize;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        if (customFont != null) tmp.font = customFont;

        var rt = tmp.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        const float edgePad = 18f;
        if (alignLeft)
            rt.offsetMin = new Vector2(edgePad, 0);
        else
            rt.offsetMax = new Vector2(-edgePad, 0);
    }

    /// <summary>One player column: the fixed avatar portrait on top, a brown name plate beneath.</summary>
    void CreateAvatarHeaderCell(Transform rowParent, int seatIndex)
    {
        var cell = new GameObject("PlayerHeaderCell", typeof(RectTransform));
        cell.transform.SetParent(rowParent, false);

        // Player profile avatar (same index / Photon sync as in-game seats).
        var avatarGo = CreateRect("AvatarImage", cell.transform, new Vector2(0, 26f), new Vector2(82, 82));
        var avatarImg = AddImage(avatarGo, Color.white);
        avatarImg.preserveAspect = true;
        avatarImg.raycastTarget = false;
        Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
        if (avatar != null)
            avatarImg.sprite = avatar;
        else if (playerAvatarSprite != null)
            avatarImg.sprite = playerAvatarSprite;

        // Brown name plate.
        var nameBox = CreateRect("NameBox", cell.transform, new Vector2(0, -36f), new Vector2(150, 34));
        var nameImg = AddImage(nameBox, NameBoxColor);
        if (_roundedSprite != null) { nameImg.sprite = _roundedSprite; nameImg.type = UnityEngine.UI.Image.Type.Sliced; }
        nameImg.raycastTarget = false;

        var nameTxt = AddTmp(nameBox.transform, GetSeatDisplayName(seatIndex), Color.white, 18, TextAlignmentOptions.Center, FontStyles.Bold);
        nameTxt.rectTransform.anchorMin = Vector2.zero;
        nameTxt.rectTransform.anchorMax = Vector2.one;
        nameTxt.rectTransform.offsetMin = new Vector2(8, 2);
        nameTxt.rectTransform.offsetMax = new Vector2(-8, -2);
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;
        nameTxt.enableAutoSizing = true;
        nameTxt.fontSizeMin = 10;
        nameTxt.fontSizeMax = 18;
    }

    /// <summary>A data row of evenly-distributed text cells (ROUND | values | TOTAL) with a dashed line beneath.</summary>
    GameObject CreateScoreRow(string name, Transform parent, string[] cells, float width, float height, Color color, bool bold)
    {
        var rowGo = NewRow(name, parent, width, height);
        for (int i = 0; i < cells.Length; i++)
            AddCellLabel(rowGo.transform, cells[i], color, bold ? 30 : 28, bold ? FontStyles.Bold : FontStyles.Normal);
        AddRowDashedLine(rowGo, width, height);
        return rowGo;
    }

    /// <summary>Creates the row container: CanvasGroup (for the reveal animation) + an even 6-column HorizontalLayoutGroup.</summary>
    GameObject NewRow(string name, Transform parent, float width, float height)
    {
        var rowGo = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        rowGo.transform.SetParent(parent, false);
        var rrt = rowGo.GetComponent<RectTransform>();
        rrt.sizeDelta = new Vector2(width, height);

        var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.MiddleCenter;

        var le = rowGo.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        le.preferredWidth = width;
        return rowGo;
    }

    /// <summary>
    /// Forces a HorizontalLayoutGroup child to take an equal 1/N share of the row width so every
    /// column lines up exactly with the fixed innerW/6 vertical dividers. Without this, cells size to
    /// their content (text preferred width / collapsed avatars) and clump toward the centre.
    /// </summary>
    static void MakeEqualColumn(GameObject cell)
    {
        var le = cell.GetComponent<LayoutElement>();
        if (le == null) le = cell.AddComponent<LayoutElement>();
        le.minWidth = 0f;
        le.preferredWidth = 0f;
        le.flexibleWidth = 1f;
    }

    /// <summary>A single centered text cell inside a row's HorizontalLayoutGroup.</summary>
    void AddCellLabel(Transform rowParent, string text, Color color, int maxSize, FontStyles style)
    {
        var cellGo = new GameObject("Cell", typeof(RectTransform));
        cellGo.transform.SetParent(rowParent, false);
        MakeEqualColumn(cellGo);
        var tmp = cellGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = style;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 12;
        tmp.fontSizeMax = maxSize;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        if (customFont != null) tmp.font = customFont;
    }

    /// <summary>Adds a horizontal dashed ledger line pinned to the bottom edge of a row (ignored by layout).</summary>
    void AddRowDashedLine(GameObject rowGo, float width, float height)
    {
        var line = CreateDashedLine("DashedLine_Row", rowGo.transform, width - 8f, new Vector2(0f, -height / 2f + 2f), true);
        var lrt = line.GetComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0f, -height / 2f + 2f);
    }

    /// <summary>Two faint vertical dividers behind the table separating ROUNDS | players | TOTAL.</summary>
    void BuildVerticalDividers(Transform container, float innerW)
    {
        var crt = container as RectTransform;
        float h = (crt != null && crt.rect.height > 1f) ? crt.rect.height - 40f : 540f;
        float col = innerW / 6f;
        AddVerticalDivider(container, -innerW / 2f + col, h);       // after ROUNDS
        AddVerticalDivider(container, -innerW / 2f + col * 5f, h);  // before TOTAL
    }

    void AddVerticalDivider(Transform container, float x, float height)
    {
        var go = CreateRect("VDivider", container, new Vector2(x, 0f), new Vector2(3f, height));
        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var img = AddImage(go, new Color(0.12f, 0.06f, 0.02f, 0.45f));
        img.raycastTarget = false;
        go.transform.SetAsFirstSibling();
        _dynamicDecor.Add(go);
    }

    void ClearDynamicMainFrameContent(Transform mainFrame)
    {
        if (mainFrame == null) return;

        var toDestroy = new List<GameObject>();
        foreach (Transform child in mainFrame)
        {
            string childName = child.name;
            if (childName == "ScorecardTable" || childName == "CloseButton"
                || childName == "RoundScrollView" || childName == "ScorecardHeader"
                || childName == "RoundTitle")
                toDestroy.Add(child.gameObject);
            else if (childName.StartsWith("SeparatorLine_") || childName.StartsWith("DashedLine_"))
                toDestroy.Add(child.gameObject);
        }

        foreach (GameObject go in toDestroy)
            DestroyObjectSafe(go);
    }

    void EnsureSceneButtons(Transform mainFrame)
    {
        // Action buttons (HOME / RESTART) are intentionally removed from the result panel:
        // at match end the leaderboard auto-returns to the Home screen after
        // MatchEndLeaderboardSeconds. If the ButtonsContainer has been deleted from the
        // scene we do NOT recreate it (no fallback buttons).
        Transform btnContainer = mainFrame.Find("ButtonsContainer");
        if (btnContainer == null) return;

        WireSceneButton(ref restartButton, btnContainer, "RestartButton", OnRestartClicked);
        WireSceneButton(ref homeButton, btnContainer, "HomeButton", OnHomeClicked);
    }

    void WireSceneButton(ref Button btn, Transform container, string childName, UnityEngine.Events.UnityAction action)
    {
        if (btn == null)
        {
            Transform existing = container.Find(childName);
            if (existing != null)
                btn = existing.GetComponent<Button>();
        }

        if (btn == null)
        {
            Color fallbackColor = childName == "RestartButton"
                ? new Color(0.12f, 0.65f, 0.28f)
                : new Color(0.85f, 0.35f, 0.15f);
            string label = childName == "RestartButton" ? "PLAY AGAIN" : "HOME";
            EnsureFallbackButton(ref btn, container, childName, label, fallbackColor, action);
            return;
        }

        if (btn.transform.parent != container)
            btn.transform.SetParent(container, false);

        EnableButtonVisuals(btn);
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    static void EnableButtonVisuals(Button btn)
    {
        if (btn == null) return;
        btn.enabled = true;
        btn.interactable = true;
        btn.gameObject.SetActive(true);

        var img = btn.GetComponent<Image>();
        if (img != null)
            img.enabled = true;
    }

    void EnsureFallbackButton(ref Button btn, Transform parent, string goName, string label, Color bgColor, UnityEngine.Events.UnityAction action)
    {
        var go = CreateRect(goName, parent, Vector2.zero, new Vector2(280, 90));
        AddImage(go, bgColor);
        btn = go.AddComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        var newTmp = AddTmp(go.transform, label, Color.white, 30, TextAlignmentOptions.Center, FontStyles.Bold);
        newTmp.rectTransform.sizeDelta = new Vector2(280, 90);

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);

        var colors = btn.colors;
        colors.highlightedColor = bgColor * 1.2f;
        colors.pressedColor = bgColor * 0.8f;
        btn.colors = colors;
    }

    GameObject CreateColumn(string name, Transform parent, float width, float contentHeight, float spacing)
    {
        var col = CreateRect(name, parent, Vector2.zero, new Vector2(width, contentHeight));
        col.AddComponent<CanvasGroup>(); // For fading/animation
        var vlg = col.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = spacing;
        vlg.childControlWidth = false;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        return col;
    }

    void CreateLabelCell(string text, Transform parent, float width, float height, bool isBold)
    {
        var cell = CreateRect("LabelCell", parent, Vector2.zero, new Vector2(width, height));
        // Push label towards the bottom so it aligns with name boxes
        var txt = AddTmp(cell.transform, text, Color.white, 28, TextAlignmentOptions.Bottom, isBold ? FontStyles.Bold : FontStyles.Normal);
        txt.rectTransform.anchoredPosition = new Vector2(0, -25f);
        txt.rectTransform.sizeDelta = new Vector2(width, height);
    }

    void CreatePlayerHeaderCell(int seatIndex, Transform parent, float width, float height)
    {
        var cell = CreateRect("PlayerHeaderCell", parent, Vector2.zero, new Vector2(width, height));

        // 1. Avatar Border/Frame (Circle)
        var avatarFrameGo = CreateRect("AvatarFrame", cell.transform, new Vector2(0, 35f), new Vector2(75, 75));
        var borderImg = AddImage(avatarFrameGo, new Color(0.35f, 0.22f, 0.12f, 0.85f));
        Sprite circleSprite = null;
#if UNITY_EDITOR
        circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
#endif
        if (circleSprite != null) borderImg.sprite = circleSprite;

        // 2. Avatar Inside Image
        var avatarImgGo = CreateRect("AvatarImage", avatarFrameGo.transform, Vector2.zero, new Vector2(68, 68));
        var avatarImg = AddImage(avatarImgGo, Color.white);
        avatarImg.sprite = GetAvatarSprite(GetActorNumberBySeat(seatIndex));
        avatarImg.preserveAspect = true;

        // 3. Name BG Box (Brown Rounded)
        var nameBoxGo = CreateRect("NameBox", cell.transform, new Vector2(0, -25f), new Vector2(140, 32));
        var nameBoxImg = AddImage(nameBoxGo, new Color(0.35f, 0.22f, 0.12f, 1f));
        if (woodBoardSprite != null)
        {
            nameBoxImg.sprite = woodBoardSprite;
            nameBoxImg.type = Image.Type.Simple;
        }

        // 4. Name Text
        string name = GetSeatDisplayName(seatIndex);
        var nameTxt = AddTmp(nameBoxGo.transform, name, Color.white, 16, TextAlignmentOptions.Center, FontStyles.Bold);
        nameTxt.rectTransform.anchoredPosition = Vector2.zero;
        nameTxt.rectTransform.sizeDelta = new Vector2(130, 26);
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;
    }

    void CreateValueCell(string text, Transform parent, float width, float height, bool isBold, bool isWinner = false, bool isCurrentRound = false)
    {
        var cell = CreateRect("ValueCell", parent, Vector2.zero, new Vector2(width, height));
        
        // Define colors
        Color textColor = Color.white; // Default readable white on wood
        int fontSize = 28;
        FontStyles style = isBold ? FontStyles.Bold : FontStyles.Normal;

        if (isWinner)
        {
            textColor = new Color(1f, 0.82f, 0.2f, 1f); // Golden Highlight for Winner
            style = FontStyles.Bold;
            fontSize = 30;
        }
        else if (isCurrentRound)
        {
            textColor = new Color(0.45f, 1f, 0.5f, 1f); // Bright green for current active round
            style = FontStyles.Bold;
        }

        var txt = AddTmp(cell.transform, text, textColor, fontSize, TextAlignmentOptions.Center, style);
        txt.rectTransform.anchoredPosition = Vector2.zero;
        txt.rectTransform.sizeDelta = new Vector2(width, height);
    }

    void CreateCloseButton(Transform parent)
    {
        var btnGo = CreateRect("CloseButton", parent, new Vector2(540, 330), new Vector2(64, 64));
        
        var bgImg = AddImage(btnGo, new Color(0.35f, 0.22f, 0.12f, 1f));
        Sprite circleSprite = null;
#if UNITY_EDITOR
        circleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
#endif
        if (circleSprite != null) bgImg.sprite = circleSprite;
        
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.onClick.AddListener(CloseResult);

        var txt = AddTmp(btnGo.transform, "X", Color.white, 26, TextAlignmentOptions.Center, FontStyles.Bold);
        txt.rectTransform.anchoredPosition = Vector2.zero;
        txt.rectTransform.sizeDelta = new Vector2(50, 50);
        
        var colors = btn.colors;
        colors.normalColor = new Color(0.35f, 0.22f, 0.12f, 1f);
        colors.highlightedColor = new Color(0.5f, 0.3f, 0.15f, 1f);
        colors.pressedColor = new Color(0.2f, 0.1f, 0.05f, 1f);
        btn.colors = colors;
    }

    Sprite GetAvatarSprite(int actorNumber)
    {
        Sprite[] pool = GetProfileSpritePool();
        if (pool == null || pool.Length == 0) return null;

        int spriteIndex = ResolveAvatarIndexForActor(actorNumber);
        if (spriteIndex < 0 || spriteIndex >= pool.Length)
            spriteIndex = Mathf.Abs(actorNumber) % pool.Length;

        return pool[spriteIndex];
    }

    static Sprite[] GetProfileSpritePool()
    {
        if (PlayerProfileManager.Instance != null &&
            PlayerProfileManager.Instance.profileSprites != null &&
            PlayerProfileManager.Instance.profileSprites.Length > 0)
            return PlayerProfileManager.Instance.profileSprites;

        if (MatchmakingManager.GlobalProfileSprites != null && MatchmakingManager.GlobalProfileSprites.Count > 0)
            return MatchmakingManager.GlobalProfileSprites.ToArray();

        return null;
    }

    static int ResolveAvatarIndexForActor(int actorNumber)
    {
        if (PhotonNetwork.LocalPlayer != null && actorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            int local = PlayerProfileManager.GetSavedAvatarIndex();
            if (local >= 0) return local;
        }

        if (PhotonNetwork.CurrentRoom != null)
        {
            Player p = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);
            if (p != null && p.CustomProperties != null &&
                p.CustomProperties.TryGetValue(PlayerProfileManager.PROP_AVATAR, out object val))
            {
                if (val != null)
                {
                    if (val is int vi) return vi;
                    if (int.TryParse(val.ToString(), out int parsed)) return parsed;
                }
            }
        }

        return -1;
    }

    GameObject CreateRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return go;
    }

    // ============================================================
    // LEDGER LINES & BANNER (1v1v1v1 leaderboard mockup)
    // ============================================================

    static readonly Color LedgerLineColor = new Color(0.12f, 0.06f, 0.02f, 0.6f);

    /// <summary>Builds a horizontal dashed line from small dash segments (no sprite needed).</summary>
    GameObject CreateDashedLine(string name, Transform parent, float width, Vector2 anchoredPos, bool ignoreLayout)
    {
        var line = CreateRect(name, parent, anchoredPos, new Vector2(width, 4f));
        if (ignoreLayout)
        {
            var le = line.AddComponent<LayoutElement>();
            le.ignoreLayout = true; // keep parent layout groups from repositioning the line
        }

        const float dashW = 20f;
        const float gap = 14f;
        const float step = dashW + gap;
        int count = Mathf.Max(1, Mathf.FloorToInt(width / step));
        float used = (count * step) - gap;
        float startX = (-used / 2f) + (dashW / 2f);

        for (int i = 0; i < count; i++)
        {
            var dash = CreateRect("Dash", line.transform, new Vector2(startX + (i * step), 0f), new Vector2(dashW, 4f));
            var img = AddImage(dash, LedgerLineColor);
            img.raycastTarget = false;
        }
        return line;
    }

    /// <summary>Solid vertical column divider spanning [bottomY, topY] at the given x (board-local).</summary>
    void CreateVerticalSeparator(string name, Transform parent, float x, float topY, float bottomY)
    {
        float h = Mathf.Abs(topY - bottomY);
        float cy = (topY + bottomY) / 2f;
        var line = CreateRect(name, parent, new Vector2(x, cy), new Vector2(3f, h));
        var img = AddImage(line, new Color(LedgerLineColor.r, LedgerLineColor.g, LedgerLineColor.b, 0.5f));
        img.raycastTarget = false;
    }

    /// <summary>
    /// Full-width banner ad placeholder pinned to the bottom of the screen (below the board).
    /// Lives on the full-screen result panel so it stretches edge-to-edge. Reused across rebuilds.
    /// </summary>
    void CreateBannerAd()
    {
        if (resultPanel == null) return;

        Transform existing = resultPanel.transform.Find("BannerAdPlacement");
        GameObject banner = existing != null ? existing.gameObject : null;
        if (banner == null)
        {
            banner = new GameObject("BannerAdPlacement", typeof(RectTransform));
            banner.transform.SetParent(resultPanel.transform, false);
        }

        var rt = banner.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(0f, 0f);
        rt.offsetMax = new Vector2(0f, 110f); // full width, 110px tall, flush to screen bottom

        ResolveThemeSprites();
        var bg = AddImage(banner, new Color(0.96f, 0.94f, 0.90f, 0.97f));
        if (_roundedSprite != null) { bg.sprite = _roundedSprite; bg.type = UnityEngine.UI.Image.Type.Sliced; }
        bg.raycastTarget = true;

        // Subtle top highlight strip for an engraved, AAA edge.
        Transform topT = banner.transform.Find("TopEdge");
        GameObject top = topT != null ? topT.gameObject : CreateRect("TopEdge", banner.transform, Vector2.zero, new Vector2(0f, 3f));
        var topRt = top.GetComponent<RectTransform>();
        topRt.anchorMin = new Vector2(0f, 1f);
        topRt.anchorMax = new Vector2(1f, 1f);
        topRt.pivot = new Vector2(0.5f, 1f);
        topRt.offsetMin = new Vector2(0f, -3f);
        topRt.offsetMax = Vector2.zero;
        var topImg = AddImage(top, new Color(1f, 0.85f, 0.5f, 0.18f));
        topImg.raycastTarget = false;

        // Placeholder label.
        Transform lblT = banner.transform.Find("Label");
        TextMeshProUGUI lbl;
        if (lblT != null)
            lbl = lblT.GetComponent<TextMeshProUGUI>();
        else
        {
            lbl = AddTmp(banner.transform, "BANNER AD PLACEMENT (FULL WIDTH)", new Color(0.15f, 0.10f, 0.06f, 1f),
                30, TextAlignmentOptions.Center, FontStyles.Bold);
            lbl.gameObject.name = "Label";
        }
        var lrt = lbl.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        banner.transform.SetAsLastSibling(); // above the dim overlay
    }

    /// <summary>Updates avatar portraits and name plates in the scene-authored HeaderRow.</summary>
    void RefreshLeaderboardHeader(Transform container)
    {
        if (container == null) return;

        Transform header = container.Find("HeaderRow");
        if (header == null) return;

        RefreshPlayerNamesAndActors();

        // Task 13/31: enlarge the header column labels (ROUNDS / TOTAL).
        foreach (Transform child in header)
        {
            if (child.name != "Cell") continue;
            var lbl = child.GetComponent<TextMeshProUGUI>();
            if (lbl == null) continue;
            lbl.fontStyle = FontStyles.Bold;
            lbl.enableAutoSizing = true;
            lbl.fontSizeMin = 16;
            lbl.fontSizeMax = 40;
        }

        var headerCells = new List<Transform>();
        for (int i = 0; i < header.childCount; i++)
        {
            Transform child = header.GetChild(i);
            if (child.name == "PlayerHeaderCell")
                headerCells.Add(child);
        }

        for (int seat = 0; seat < headerCells.Count && seat < 4; seat++)
        {
            Transform cell = headerCells[seat];
            string displayName = GetSeatDisplayName(seat);

            Transform nameBox = cell.Find("NameBox");
            if (nameBox != null)
            {
                var nameTmp = nameBox.GetComponentInChildren<TextMeshProUGUI>(true);
                if (nameTmp != null)
                {
                    nameTmp.text = displayName;
                    nameTmp.fontStyle = FontStyles.Bold;
                    nameTmp.enableAutoSizing = true;
                    nameTmp.fontSizeMin = 14;
                    nameTmp.fontSizeMax = 26;
                }
            }

            Transform avatarT = cell.Find("AvatarImage") ?? FindDeepByName(cell, "AvatarImage");
            if (avatarT != null)
            {
                var avatarImg = avatarT.GetComponent<Image>();
                if (avatarImg != null)
                {
                    Sprite avatar = GetAvatarSprite(GetActorNumberBySeat(seat));
                    if (avatar != null)
                        avatarImg.sprite = avatar;
                }
            }
        }
    }

    /// <summary>Hides the old mock-up status pill if it was created in a previous session.</summary>
    void HideMatchFinishedLabel()
    {
        if (resultPanel == null) return;
        Transform tag = resultPanel.transform.Find("MatchFinishedTag");
        if (tag != null)
            tag.gameObject.SetActive(false);
    }

    static Image AddImage(GameObject go, Color c)
    {
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = c;
        return img;
    }

    TextMeshProUGUI AddTmp(Transform parent, string text, Color color, int size, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 50); // Default size, usually overridden by layout
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.fontStyle = style;
        tmp.raycastTarget = false;
        if (customFont != null) tmp.font = customFont;
        return tmp;
    }

    void ClearDynamicUI()
    {
        // Only destroy runtime-created overflow rows. The static leaderboard skeleton
        // (HeaderRow, RoundRow_1..5, dividers) is authored in the scene and must persist.
        foreach (var go in _overflowRows)
            if (go != null) DestroyObjectSafe(go);
        _overflowRows.Clear();
    }

    void DestroyObjectSafe(GameObject go)
    {
        if (go == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(go);
            return;
        }
#endif
        Destroy(go);
    }

    void OnHomeClicked()
    {
        if (_resultActionTaken) return;   // ignore rapid double-taps
        _resultActionTaken = true;

        Debug.Log("[UI] Button Clicked: Home (from results)");
        HideResultPanelImmediate();
        ResetMatchStats();

        bool leaving = PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Leaving;
        if (PhotonNetwork.InRoom && !leaving)
            PhotonNetwork.LeaveRoom();
        else if (PhotonNetwork.OfflineMode)
        {
            if (!leaving) PhotonNetwork.LeaveRoom();
            PhotonNetwork.OfflineMode = false;
        }

        if (DeckManager.Instance != null)
            DeckManager.Instance.ResetMatchState();

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ReturnToHomeScreen();
    }

    void OnRestartClicked()
    {
        if (_resultActionTaken) return;   // ignore rapid double-taps
        if (!PhotonNetwork.OfflineMode && !(PhotonNetwork.InRoom && PhotonNetwork.IsConnectedAndReady))
        {
            Debug.LogWarning("[Result] Play Again ignored — not in a valid room state.");
            return;
        }
        _resultActionTaken = true;

        Debug.Log("[UI] Button Clicked: Play Again");
        HideResultPanelImmediate();
        ResetMatchStats();

        if (DeckManager.Instance != null)
            DeckManager.Instance.ResetMatchState();

        GameFlowState.SetPhase(GameFlowPhase.InRoom);

        if (PhotonNetwork.OfflineMode)
        {
            if (DeckManager.Instance != null && PhotonNetwork.IsMasterClient)
                DeckManager.Instance.FillBotsAndStart();
        }
        else if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
        {
            DeckManager.Instance.FillBotsAndStart();
        }
    }

    void ResetMatchStats()
    {
        _statsRecorded = false;
        _roundTransitionRunning = false;
        currentRound = 1;
        maxRounds = MaxRoundsBotsOnline;
        roundHistory.Clear();
        ResetRoundPlayerStats();

        for (int i = 0; i < 4; i++)
        {
            playerResults[i].bid = 0;
            playerResults[i].name = GetInitialPlayerName(i);
        }
    }
}
