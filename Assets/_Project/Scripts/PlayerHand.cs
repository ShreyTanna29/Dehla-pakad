using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun; 
using Photon.Realtime;
using UnityEngine.UI; 
using System.Linq; 
using DG.Tweening; 
using TMPro;

public class PlayerHand : MonoBehaviourPunCallbacks 
{
    public static PlayerHand LocalInstance;
    public List<CardData> myCards = new List<CardData>(); 

    // For local human play-card visuals: animate from the exact clicked card world position.
    private bool _hasPendingLocalPlayStart;
    private Coroutine _unlockPlayFailsafeCoroutine;
    private Vector3 _pendingLocalPlayStartWorldPos;
    private int _pendingLocalPlaySuitIndex;
    private int _pendingLocalPlayRankIndex;

    void Awake()
    {
        if (photonView.IsMine)
        {
            LocalInstance = this;
        }
    }

    [Header("UI Setup")]
    public GameObject cardUIPrefab; 
    public Transform handAreaTransform;
    [Tooltip("Gameplay canvas root — used to resolve table/hand refs without GameObject.Find.")]
    public Transform gameUiSearchRoot;
    public Transform tableCenterTransform;

    [Header("AAA Dealing Animation")]
    [UnityEngine.Serialization.FormerlySerializedAs("flyingCardPrefab")]
    public GameObject dummyCardPrefab;
    private Transform canvasTransform;
    private Transform centerPos; 
    private Transform[] playerPositions;
    private Transform _resolvedTableCenter;
    private static bool _gameplayUiWarned;

    public int currentTurnActor 
    { 
        get {
            if (_localCurrentTurnActor >= 0)
                return _localCurrentTurnActor;
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CTA", out object actor))
                return (int)actor;
            return _localCurrentTurnActor;
        }
        set {
            _localCurrentTurnActor = value;
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "CTA", value } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }
        }
    }

    public int GetAuthoritativeTurnActor() => currentTurnActor;
    public static bool isTrumpRevealed = true;
    public static CardSuit currentTrumpSuit = CardSuit.Spades;
    public static bool hasTrumpBeenSetOnce = false;

    public class TrickCard
    {
        public int actorNumber; 
        public CardSuit suit;
        public int rankValue;
        public GameObject cardObject;
    }

    private int totalTricksPlayed 
    { 
        get {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TP", out object tricks))
                return (int)tricks;
            return _localTotalTricksPlayed;
        }
        set {
            _localTotalTricksPlayed = value;
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "TP", value } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }
        }
    }
    private int _localTotalTricksPlayed = 0;

    public void SyncCurrentTrickToRoom()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;

        int[] interleaved = new int[currentTrick.Count * 3];
        for (int i = 0; i < currentTrick.Count; i++)
        {
            interleaved[i * 3] = currentTrick[i].actorNumber;
            interleaved[i * 3 + 1] = (int)currentTrick[i].suit;
            interleaved[i * 3 + 2] = currentTrick[i].rankValue;
        }

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "TC", interleaved } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey("TC"))
        {
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TC", out object tcObj))
            {
                int[] interleaved = (int[])tcObj;
                if (interleaved.Length == 0)
                {
                    DestroyTrickCardObjects(currentTrick);
                    ClearAllTableCardClones();
                    currentTrick.Clear();
                    isCleaningTable = false;
                    isResolvingTrick = false;
                    _determineTrickRoutineRunning = false;
                    return;
                }

                if (!IsTrickLocked && currentTrick.Count != interleaved.Length / 3)
                    RestoreTableCardsFromRoom();
            }
        }

        // Master drives turns locally — stale room CTA must not re-fire old bot/human turns.
        if (propertiesThatChanged.ContainsKey("CTA") && !IsTrickLocked && !PhotonNetwork.IsMasterClient)
        {
            int actor = (int)propertiesThatChanged["CTA"];
            if (isDealingComplete && GameFlowState.Current == GameFlowPhase.InGame && actor != _lastHandledTurnActor)
                ProcessTurn(actor);
        }
    }

    private int _lastHandledTurnActor = -1;
    private int _lastProcessTurnActor = -1;
    private int _lastProcessTurnTrickCount = -1;
    private readonly HashSet<int> actorsPlayedThisTrick = new HashSet<int>();
    private readonly HashSet<int> botActorsThinking = new HashSet<int>();
    private readonly Dictionary<long, int> botRetryCounts = new Dictionary<long, int>();
    private Coroutine _botWatchdogCoroutine;
    const int MaxBotPlayRetries = 8;
    const float BotThinkDelay = 0.65f;

    static long BotRetryKey(int actorNumber, int trickCount) =>
        ((long)actorNumber << 32) | (uint)trickCount;

    bool ActorInCurrentTrick(int actorNumber) =>
        currentTrick != null && currentTrick.Exists(c => c.actorNumber == actorNumber);

    void ClearTrickPlayLocks()
    {
        actorsPlayedThisTrick.Clear();
        botActorsThinking.Clear();
        botRetryCounts.Clear();
    }

    void ClearAllTableCardClones()
    {
        Transform tableCenter = GetTableCenterTransform();
        if (tableCenter == null) return;

        for (int i = tableCenter.childCount - 1; i >= 0; i--)
        {
            Transform child = tableCenter.GetChild(i);
            if (_accumulatedPileRoot != null && child == _accumulatedPileRoot) continue;
            if (child.GetComponent<CardDisplay>() == null) continue;
            child.DOKill();
            Object.Destroy(child.gameObject);
        }
    }

    static void ClearTwoSarState()
    {
        accumulatedTableCards.Clear();
        lastTrickWinnerActor = -1;

        if (_accumulatedPileRoot != null)
        {
            Object.Destroy(_accumulatedPileRoot.gameObject);
            _accumulatedPileRoot = null;
        }
    }

    Transform GetAccumulatedPileRoot()
    {
        Transform tableCenter = GetTableCenterTransform();
        if (tableCenter == null) return null;

        if (_accumulatedPileRoot == null)
        {
            var pileGo = new GameObject("AccumulatedPile", typeof(RectTransform));
            pileGo.transform.SetParent(tableCenter, false);
            _accumulatedPileRoot = pileGo.transform;
        }

        return _accumulatedPileRoot;
    }

    void MoveCardsToAccumulatedPileVisuals(List<TrickCard> trickCards)
    {
        if (trickCards == null || trickCards.Count == 0) return;

        Transform pileRoot = GetAccumulatedPileRoot();
        int baseIndex = accumulatedTableCards.Count - trickCards.Count;

        for (int i = 0; i < trickCards.Count; i++)
        {
            TrickCard tc = trickCards[i];
            if (tc.cardObject == null || pileRoot == null) continue;

            tc.cardObject.transform.SetParent(pileRoot, true);
            float stackX = (baseIndex + i) % 6 * 4f - 10f;
            float stackY = (baseIndex + i) / 6 * 3f - 6f;
            tc.cardObject.transform.DOLocalMove(new Vector3(stackX, stackY, 0f), 0.45f).SetEase(Ease.InCubic);
            tc.cardObject.transform.DOScale(Vector3.one * 0.55f, 0.45f).SetEase(Ease.InBack);

            CanvasGroup cg = tc.cardObject.GetComponent<CanvasGroup>();
            if (cg == null) cg = tc.cardObject.AddComponent<CanvasGroup>();
            cg.DOFade(0.85f, 0.35f);
        }
    }

    void CaptureCardsForPlayer(int winnerActor, List<TrickCard> cards)
    {
        if (cards == null || cards.Count == 0) return;

        int winnerSeat = GetSeatIndex(winnerActor);
        int dehlas = 0;
        foreach (TrickCard tc in cards)
            if (tc.rankValue == (int)CardRank.Ten) dehlas++;

        if (ResultManager.Instance != null)
            ResultManager.Instance.OnTrickWon(winnerSeat, dehlas);

        Transform winnerTransform = playerPositions != null && winnerSeat >= 0 && winnerSeat < playerPositions.Length
            ? playerPositions[winnerSeat]
            : transform;
        Transform tableCenter = GetTableCenterTransform();

        foreach (TrickCard tc in cards)
        {
            if (tc.cardObject == null) continue;
            GameObject go = tc.cardObject;
            if (tableCenter != null)
                go.transform.SetParent(tableCenter, true);
            go.transform.DOMove(winnerTransform.position, 0.5f).SetEase(Ease.InCubic);
            go.transform.DOScale(Vector3.zero, 0.5f).SetEase(Ease.InBack)
                .OnComplete(() => { if (go != null) Object.Destroy(go); });
        }

        if (_accumulatedPileRoot != null)
        {
            Object.Destroy(_accumulatedPileRoot.gameObject);
            _accumulatedPileRoot = null;
        }
    }

    public void HandleTrickWinner(int currentWinnerActor, List<TrickCard> currentTrickCards, bool isLastTrickOfGame)
    {
        if (currentTrickCards == null || currentTrickCards.Count == 0) return;

        accumulatedTableCards.AddRange(currentTrickCards);

        bool isTwoSarMode = GameSettings.Instance != null && GameSettings.Instance.currentSarMode == SarModeType.TwoSar;

        if (isTwoSarMode)
        {
            if (currentWinnerActor == lastTrickWinnerActor || isLastTrickOfGame)
            {
                Debug.Log($"[2 SAR] BINGO! Player {currentWinnerActor} captured {accumulatedTableCards.Count} cards.");
                CaptureCardsForPlayer(currentWinnerActor, accumulatedTableCards);
                accumulatedTableCards.Clear();
                lastTrickWinnerActor = -1;
            }
            else
            {
                Debug.Log($"[2 SAR] Player {currentWinnerActor} won 1 trick — cards stay on table.");
                lastTrickWinnerActor = currentWinnerActor;
                MoveCardsToAccumulatedPileVisuals(currentTrickCards);
            }
        }
        else
        {
            Debug.Log($"[1 SAR] Player {currentWinnerActor} won the trick.");
            CaptureCardsForPlayer(currentWinnerActor, accumulatedTableCards);
            accumulatedTableCards.Clear();
            lastTrickWinnerActor = -1;
        }
    }

    public void RestoreTableCardsFromRoom()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TC", out object tcObj))
        {
            int[] interleaved = (int[])tcObj;
            ClearAllTableCardClones();
            currentTrick.Clear();
            actorsPlayedThisTrick.Clear();

            for (int i = 0; i < interleaved.Length / 3; i++)
            {
                SpawnCardOnTableLocal(interleaved[i * 3], interleaved[i * 3 + 1], interleaved[i * 3 + 2]);
                actorsPlayedThisTrick.Add(interleaved[i * 3]);
            }

            if (isDealingComplete)
            {
                RefreshHandUI(false, true);

                if (currentTrick.Count >= 4 && PhotonNetwork.IsMasterClient)
                    ProcessTurn(currentTurnActor);
                else
                    ApplyRules(PhotonNetwork.LocalPlayer.ActorNumber == currentTurnActor);
            }
        }
    }

    static Vector3 GetOffsetForSeat(int seatIndex)
    {
        switch (seatIndex)
        {
            case 0: return new Vector3(0, -120f, 0);
            case 1: return new Vector3(-180f, 0, 0);
            case 2: return new Vector3(0, 120f, 0);
            default: return new Vector3(180f, 0, 0);
        }
    }

    public Vector3 GetPlayerOrigin(int actorNumber)
    {
        int seatIndex = GetSeatIndex(actorNumber);
        EnsureGameplayUiRefs();

        if (seatIndex == 0 && handAreaTransform != null)
            return handAreaTransform.position;

        if (playerPositions != null && seatIndex >= 0 && seatIndex < playerPositions.Length && playerPositions[seatIndex] != null)
            return playerPositions[seatIndex].position;

        Transform center = GetTableCenterTransform();
        return center != null ? center.position : transform.position;
    }

    public Vector3 GetFinalPositionForSeat(int seat)
    {
        const float gap = 100f;

        switch (seat)
        {
            case 0: return new Vector3(0f, -gap, 0f);
            case 1: return new Vector3(-gap, 0f, 0f);
            case 2: return new Vector3(0f, gap, 0f);
            case 3: return new Vector3(gap, 0f, 0f);
            default: return Vector3.zero;
        }
    }

    Vector3 GetPlayerPositionForSeat(int seat)
    {
        EnsureGameplayUiRefs();
        if (playerPositions != null && seat >= 0 && seat < playerPositions.Length && playerPositions[seat] != null)
            return playerPositions[seat].position;
        if (seat == 0 && handAreaTransform != null)
            return handAreaTransform.position;
        Transform center = GetTableCenterTransform();
        return center != null ? center.position : transform.position;
    }

    Vector3 GetSeatSpawnPosition(int seatIndex)
    {
        EnsureGameplayUiRefs();
        if (playerPositions != null && seatIndex >= 0 && seatIndex < playerPositions.Length && playerPositions[seatIndex] != null)
            return playerPositions[seatIndex].position;
        return GetTableCenterTransform().position;
    }

    private void SpawnCardOnTableLocal(int senderActorNum, int suitIndex, int rankIndex)
    {
        int seat = GetSeatIndex(senderActorNum);
        Transform center = GetTableCenterTransform();
        
        GameObject cardObj = Object.Instantiate(cardUIPrefab, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex });
        cardObj.transform.position = GetPlayerPositionForSeat(seat);
        cardObj.transform.localScale = Vector3.one;

        Vector3 targetLocal = GetFinalPositionForSeat(seat);
        cardObj.transform.DOScale(0.8f, 0.35f);
        cardObj.transform.DOLocalMove(targetLocal, 0.35f).SetEase(Ease.OutBack);
        cardObj.transform.localRotation = Quaternion.identity;

        currentTrick.Add(new TrickCard { actorNumber = senderActorNum, suit = (CardSuit)suitIndex, rankValue = rankIndex, cardObject = cardObj });
    }

    public List<TrickCard> currentTrick = new List<TrickCard>();

    public static bool isResolvingTrick;
    public static bool isCleaningTable;
    public static bool IsTrickLocked => isResolvingTrick || isCleaningTable;

    // 2 Sar logic memory — shared across clients for pile + turn continuity
    public static int lastTrickWinnerActor = -1;
    public static List<TrickCard> accumulatedTableCards = new List<TrickCard>();
    static Transform _accumulatedPileRoot;

    static bool _handRevealRunning;
    public static bool IsGameplayInputBlocked =>
        IsDealAnimationRunning || _handRevealRunning || IsTrickLocked ||
        CardInteract.isPlayingCard || GameFlowState.Current == GameFlowPhase.GameFinished;

    private int _localCurrentTurnActor = -1;
    private bool _determineTrickRoutineRunning;
    private Coroutine _determineTrickCoroutine;

    private bool isDealingComplete = false;
    private int _cutsInMatch = 0;
    private bool cut1TrumpAlreadySet = false;
private static bool _resultPanelShown = false;
    private readonly List<int> tableTurnOrder = new List<int>(4);
    private readonly List<GameObject> opponentBackCards = new List<GameObject>();

    private void HandleTrumpModeAfterCardAdded(CardData playedCard, int actorNumber)
    {
        if (GameSettings.Instance == null || currentTrick == null || currentTrick.Count < 1) return;

        GameModeType mode = GameSettings.Instance.currentMode;
        CardSuit ledSuit = currentTrick[0].suit;
        bool isCut = playedCard.cardSuit != ledSuit;

        if (!isCut) return;

        if (mode == GameModeType.Cut1Trump)
        {
            if (cut1TrumpAlreadySet || playedCard.cardSuit == currentTrumpSuit) return;
            cut1TrumpAlreadySet = true;
            ApplyTrumpChange(playedCard.cardSuit);
            return;
        }

        if (mode == GameModeType.Cut2Trump && !hasTrumpBeenSetOnce)
        {
            hasTrumpBeenSetOnce = true;
            currentTrumpSuit = playedCard.cardSuit;
            isTrumpRevealed = true;

            if (TrumpManager.Instance != null)
                TrumpManager.Instance.SetTrumpSuit(currentTrumpSuit, PhotonNetwork.IsMasterClient, true);

            if (PhotonNetwork.IsMasterClient && photonView != null)
                photonView.RPC(nameof(RPC_SyncTrump), RpcTarget.Others, (int)currentTrumpSuit);
        }
    }

    [PunRPC]
    void RPC_SyncTrump(int suitIndex)
    {
        if (hasTrumpBeenSetOnce) return;
        hasTrumpBeenSetOnce = true;
        ApplyTrumpChange((CardSuit)suitIndex);
    }

    void ApplyTrumpChange(CardSuit newSuit)
    {
        currentTrumpSuit = newSuit;
        isTrumpRevealed = true;

        if (TrumpManager.Instance != null)
        {
            bool showPopup = PhotonNetwork.IsMasterClient;
            TrumpManager.Instance.SetTrumpSuit(newSuit, showPopup, true);
        }
    }

    void ClearOpponentCardBacks()
    {
        foreach (GameObject go in opponentBackCards) if (go != null) Object.Destroy(go);
        opponentBackCards.Clear();
    }

    void ClearHandUI()
    {
        if (handAreaTransform == null) return;
        foreach (Transform child in handAreaTransform)
        {
            child.DOKill();
            Object.Destroy(child.gameObject);
        }
    }

    public void InitializeGameScene()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            gameUiSearchRoot = NetworkManager.Instance.gameCanvasGroup.transform;

        playerPositions = null;
        _resolvedTableCenter = null;
        tableCenterTransform = null;

        EnsureGameplayUiRefs();
        ActivateGameplayUiRoots();
        BuildTableTurnOrder();

        int botCount = DeckManager.botActorNumbers != null ? DeckManager.botActorNumbers.Count : 0;
        int humans = DeckManager.GetActiveHumanPlayerCount();
        Debug.Log($"[GameInit] Game scene initialized | humans={humans} bots={botCount} seats={tableTurnOrder.Count}");
    }

    static void ActivateTransformChain(Transform t)
    {
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    void ActivateGameplayUiRoots()
    {
        string[] roots =
        {
            "Player_Hand_Area", "Opponent_Left", "Opponent_Top", "Opponent_Right",
            "Table_Center", "You", "Button_Deal", "TrumpDisplay", "TrumpCardDisplay"
        };

        foreach (string name in roots)
        {
            if (UiSafeLookup.TryGet(name, out GameObject go) && go != null)
                ActivateTransformChain(go.transform);
        }

        if (handAreaTransform != null)
            ActivateTransformChain(handAreaTransform);
    }

    void EnsureGameplayUiRefs()
    {
        if (gameUiSearchRoot == null && NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            gameUiSearchRoot = NetworkManager.Instance.gameCanvasGroup.transform;

        if (gameUiSearchRoot == null)
        {
            if (handAreaTransform != null) gameUiSearchRoot = handAreaTransform.root;
            else gameUiSearchRoot = transform.root;
        }

        UiSafeLookup.SetSearchRoot(gameUiSearchRoot);

        if (handAreaTransform == null && UiSafeLookup.TryGet("Player_Hand_Area", out GameObject handGo))
            handAreaTransform = handGo.transform;

        if (canvasTransform == null)
        {
            Canvas rootCanvas = handAreaTransform != null
                ? handAreaTransform.GetComponentInParent<Canvas>()
                : Object.FindAnyObjectByType<Canvas>();
            if (rootCanvas != null)
                canvasTransform = rootCanvas.transform;
            else if (UiSafeLookup.TryGet("Canvas", out GameObject canvasGo))
                canvasTransform = canvasGo.transform;
        }

        if (centerPos == null && UiSafeLookup.TryGet("Button_Deal", out GameObject dealGo))
            centerPos = dealGo.transform;

        if (playerPositions == null || playerPositions.Length < 4)
        {
            playerPositions = new Transform[4];
            playerPositions[0] = handAreaTransform;
            if (UiSafeLookup.TryGet("Opponent_Left", out GameObject leftGo)) playerPositions[1] = leftGo.transform;
            if (UiSafeLookup.TryGet("Opponent_Top", out GameObject topGo)) playerPositions[2] = topGo.transform;
            if (UiSafeLookup.TryGet("Opponent_Right", out GameObject rightGo)) playerPositions[3] = rightGo.transform;
        }

        if (tableCenterTransform == null && UiSafeLookup.TryGet("Table_Center", out GameObject tableGo))
            tableCenterTransform = tableGo.transform;
        _resolvedTableCenter = tableCenterTransform;

        if (!_gameplayUiWarned && (handAreaTransform == null || _resolvedTableCenter == null))
        {
            _gameplayUiWarned = true;
            Debug.LogWarning("[PlayerHand] Gameplay UI refs incomplete — assign handArea/tableCenter in Inspector or ensure names under game canvas.");
        }
    }

    Transform GetTableCenterTransform()
    {
        if (tableCenterTransform != null) return tableCenterTransform;
        if (_resolvedTableCenter != null) return _resolvedTableCenter;
        EnsureGameplayUiRefs();
        return _resolvedTableCenter != null ? _resolvedTableCenter : transform;
    }

    void Start()
    {
        CardInteract.canPlayCards = false;
        EnsureGameplayUiRefs();
        
        if (photonView.IsMine)
        {
            bool matchInProgress = false;
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object started))
                matchInProgress = (bool)started;

            if (!matchInProgress) ResetHand();
        }
    }

    public override void OnJoinedRoom()
    {
        if (photonView.IsMine)
        {
            bool matchInProgress = false;
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object started))
                matchInProgress = (bool)started;

            if (!matchInProgress) ResetHand();
        }
    }

    [PunRPC]
    public void RPC_ResetHand()
    {
        if (LocalInstance != null && LocalInstance != this)
        {
            LocalInstance.RPC_ResetHand();
            return;
        }

        ResetHand();
        ClearHandUI();
    }

    public void ResetHand()
    {
        HandLayoutHelper.ResetTwoTaashSpacingCache();
        if (_determineTrickCoroutine != null)
        {
            StopCoroutine(_determineTrickCoroutine);
            _determineTrickCoroutine = null;
        }
        StopUnlockPlayFailsafe();
        _determineTrickRoutineRunning = false;
        isResolvingTrick = false;
        isCleaningTable = false;
        myCards.Clear();
        _cutsInMatch = 0;
        cut1TrumpAlreadySet = false;
        hasTrumpBeenSetOnce = false;
        totalTricksPlayed = 0;
        ClearAllTableCardClones();
        ClearTwoSarState();
        currentTrick.Clear();
        _localCurrentTurnActor = -1;
        _lastHandledTurnActor = -1;
        _lastProcessTurnActor = -1;
        _lastProcessTurnTrickCount = -1;
        _resultPanelShown = false;
        isDealingComplete = false;
        _handRevealRunning = false;
        tableTurnOrder.Clear();
        botActorsThinking.Clear();
        actorsPlayedThisTrick.Clear();
        botRetryCounts.Clear();
        StopBotWatchdog();
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;
        if (_isDealAnimRunning)
        {
            StopAllCoroutines();
            _isDealAnimRunning = false;
            IsDealAnimationRunning = false;
        }
        ClearHandUI();
        ClearOpponentCardBacks();
        HideOpponentFansImmediate();
    }

    private void HideOpponentFansImmediate()
    {
        if (playerPositions == null) return;
        for (int i = 1; i < playerPositions.Length; i++)
        {
            if (playerPositions[i] == null) continue;
            Transform fan = playerPositions[i].Find("CardFan");
            if (fan != null)
            {
                UnityEngine.CanvasGroup cg = fan.GetComponent<UnityEngine.CanvasGroup>();
                if (cg == null) cg = fan.gameObject.AddComponent<UnityEngine.CanvasGroup>();
                cg.alpha = 0;
                fan.localScale = Vector3.one * 0.9f;
                fan.gameObject.SetActive(false);
            }
        }
    }

    private void ShowOpponentFansWithAnimation()
    {
        if (playerPositions == null) return;
        for (int i = 1; i < playerPositions.Length; i++)
        {
            if (playerPositions[i] == null) continue;
            Transform fan = playerPositions[i].Find("CardFan");
            if (fan != null)
            {
                fan.gameObject.SetActive(true);
                UnityEngine.CanvasGroup cg = fan.GetComponent<UnityEngine.CanvasGroup>();
                if (cg == null) cg = fan.gameObject.AddComponent<UnityEngine.CanvasGroup>();
                fan.DOKill();
                cg.DOKill();
                cg.DOFade(1, 0.3f).SetEase(Ease.OutSine);
                fan.DOScale(1.0f, 0.3f).SetEase(Ease.OutBack);
            }
        }
    }

    void BuildTableTurnOrder()
    {
        tableTurnOrder.Clear();
        if (DeckManager.Instance == null)
        {
            Debug.LogWarning("[Bot] BuildTableTurnOrder — DeckManager missing.");
            return;
        }

        List<int> seats = DeckManager.Instance.GetActiveSeatActorsSorted();
        if (seats.Count != DeckManager.MaxTableSeats)
        {
            Debug.LogError($"[Bot] BuildTableTurnOrder failed — seat count {seats.Count}, need {DeckManager.MaxTableSeats}.");
            return;
        }

        int myIndex = seats.IndexOf(PhotonNetwork.LocalPlayer.ActorNumber);
        if (myIndex < 0) myIndex = 0;

        for (int i = 0; i < DeckManager.MaxTableSeats; i++)
            tableTurnOrder.Add(seats[(myIndex + i) % DeckManager.MaxTableSeats]);
    }

    bool IsDealingReadyForPlay()
    {
        if (IsDealAnimationRunning || _handRevealRunning) return false;
        if (isDealingComplete) return true;
        return DeckManager.Instance != null && DeckManager.Instance.IsDealingComplete;
    }

    static bool IsBotActor(int actorNum) =>
        DeckManager.botActorNumbers != null && DeckManager.botActorNumbers.Contains(actorNum);

    bool IsExpectedTrickPlayer(int actor, int playIndexInTrick)
    {
        if (tableTurnOrder.Count < 4) BuildTableTurnOrder();
        if (tableTurnOrder.Count == 0) return actor == GetAuthoritativeTurnActor();

        int leader = currentTrick != null && currentTrick.Count > 0
            ? currentTrick[0].actorNumber
            : GetAuthoritativeTurnActor();
        int leaderIdx = tableTurnOrder.IndexOf(leader);
        if (leaderIdx < 0) return actor == GetAuthoritativeTurnActor();

        int expectedIdx = (leaderIdx - playIndexInTrick + tableTurnOrder.Count * 8) % tableTurnOrder.Count;
        return tableTurnOrder[expectedIdx] == actor;
    }

    bool IsActorsTurnToPlay(int senderActorNum)
    {
        if (senderActorNum == GetAuthoritativeTurnActor()) return true;
        int playIndex = currentTrick?.Count ?? 0;
        return IsBotActor(senderActorNum) && IsExpectedTrickPlayer(senderActorNum, playIndex);
    }

    public int GetNextTurnActor(int currentActor)
    {
        if (tableTurnOrder.Count < 4) BuildTableTurnOrder();
        if (tableTurnOrder.Count == 0) return currentActor;

        int idx = tableTurnOrder.IndexOf(currentActor);
        if (idx < 0)
        {
            BuildTableTurnOrder();
            idx = tableTurnOrder.IndexOf(currentActor);
            if (idx < 0) return tableTurnOrder[0];
        }

        int nextIdx = (idx - 1 + tableTurnOrder.Count) % tableTurnOrder.Count;
        return tableTurnOrder[nextIdx];
    }

    bool CanAcceptCardPlay(int senderActorNum, int suitIndex = -1, int rankIndex = -1)
    {
        if (GameFlowState.Current == GameFlowPhase.GameFinished) return false;
        if (!IsDealingReadyForPlay()) return false;
        if (IsTrickLocked || _determineTrickRoutineRunning) return false;
        if (currentTrick == null || currentTrick.Count >= 4) return false;
        if (!IsActorsTurnToPlay(senderActorNum)) return false;
        if (ActorInCurrentTrick(senderActorNum)) return false;
        return true;
    }

    void LockTrickPlayInput()
    {
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = true;
        EndTurnCardVisuals();
    }

    static void DestroyTrickCardObjects(List<TrickCard> trickCards)
    {
        if (trickCards == null) return;
        foreach (TrickCard tc in trickCards)
        {
            if (tc.cardObject == null) continue;
            tc.cardObject.transform.DOKill();
            Object.Destroy(tc.cardObject);
            tc.cardObject = null;
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!isDealingComplete || currentTurnActor != otherPlayer.ActorNumber || !otherPlayer.IsInactive)
            return;

        if (PhotonNetwork.IsMasterClient && TurnManager.Instance != null)
            TurnManager.Instance.StopTimer();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient && photonView != null)
            photonView.RPC("RPC_SyncGameState", newPlayer, currentTurnActor, isDealingComplete);

        if (isDealingComplete && currentTurnActor == newPlayer.ActorNumber
            && PhotonNetwork.IsMasterClient && TurnManager.Instance != null)
            TurnManager.Instance.StartTurn(currentTurnActor);
    }

    [PunRPC]
    public void RPC_SyncGameState(int turnActor, bool dealingDone)
    {
        if (LocalInstance != null && LocalInstance != this)
        {
            LocalInstance.RPC_SyncGameState(turnActor, dealingDone);
            return;
        }

        currentTurnActor = turnActor;
        isDealingComplete = dealingDone;

        RefreshHandUI(false, true);

        bool isMyTurn = PhotonNetwork.LocalPlayer.ActorNumber == currentTurnActor;
        CardInteract.canPlayCards = isMyTurn;
        ApplyRules(isMyTurn);

        Debug.Log($"[Sync] Game state synced. My Turn: {isMyTurn}");
    }

    void ProcessTurn(int actorNumber)
    {
        if (!IsDealingReadyForPlay()) return;
        if (IsTrickLocked || _determineTrickRoutineRunning) return;
        if (GameFlowState.Current != GameFlowPhase.InGame && GameFlowState.Current != GameFlowPhase.InRoom) return;

        int trickCount = currentTrick?.Count ?? 0;

        if (trickCount >= 4)
        {
            if (PhotonNetwork.IsMasterClient && !_determineTrickRoutineRunning && !isResolvingTrick)
            {
                isResolvingTrick = true;
                _determineTrickCoroutine = StartCoroutine(DetermineTrickWinnerRoutine());
            }
            return;
        }

        if (ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber))
        {
            if (PhotonNetwork.IsMasterClient)
            {
                int nextActor = actorNumber;
                int safetyGuard = 0;

                while ((ActorInCurrentTrick(nextActor) || actorsPlayedThisTrick.Contains(nextActor)) && safetyGuard < 5)
                {
                    int prevActor = nextActor;
                    nextActor = GetNextTurnActor(nextActor);

                    if (prevActor == nextActor) break;

                    safetyGuard++;
                }

                if (safetyGuard < 5 && nextActor != actorNumber && !ActorInCurrentTrick(nextActor))
                {
                    currentTurnActor = nextActor;
                    ProcessTurn(nextActor);
                }
                else
                {
                    Debug.LogError("[ProcessTurn] INFINITE LOOP BLOCKED! Zyadatar log patta daal chuke hain ya list empty hai.");

                    if (trickCount > 0 && !_determineTrickRoutineRunning)
                    {
                        isResolvingTrick = true;
                        _determineTrickCoroutine = StartCoroutine(DetermineTrickWinnerRoutine());
                    }
                }
            }
            return;
        }

        if (_lastProcessTurnActor == actorNumber && _lastProcessTurnTrickCount == trickCount && trickCount < 4)
        {
            if (PhotonNetwork.IsMasterClient && IsBotActor(actorNumber) && !ActorInCurrentTrick(actorNumber))
                TriggerBotTurnIfApplicable(actorNumber);
            return;
        }
        _lastProcessTurnActor = actorNumber;
        _lastProcessTurnTrickCount = trickCount;

        if (actorNumber == _lastHandledTurnActor && !PhotonNetwork.IsMasterClient) return;
        _lastHandledTurnActor = actorNumber;

        currentTurnActor = actorNumber;

        if (TurnManager.Instance != null && PhotonNetwork.IsMasterClient)
        {
            Player turnPlayer = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);
            if (turnPlayer != null && turnPlayer.IsInactive)
                TurnManager.Instance.StopTimer();
            else
                TurnManager.Instance.StartTurn(actorNumber);
        }

        bool isMyTurn = (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber);

        if (isMyTurn && !IsGameplayInputBlocked && !ActorInCurrentTrick(actorNumber) && !actorsPlayedThisTrick.Contains(actorNumber))
        {
            CardInteract.canPlayCards = true;
            CardInteract.isPlayingCard = false;
        }

        ApplyRules(isMyTurn);

        if (PhotonNetwork.IsMasterClient && IsBotActor(actorNumber) && !ActorInCurrentTrick(actorNumber))
            TriggerBotTurnIfApplicable(actorNumber);
    }

    public void TriggerBotTurnIfApplicable(int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!IsBotActor(actorNumber)) return;
        if (IsTrickLocked || _determineTrickRoutineRunning) return;

        if (botActorsThinking.Contains(actorNumber) || ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber)) return;
        if (actorNumber != GetAuthoritativeTurnActor()) return;
        if (DeckManager.Instance == null || !DeckManager.Instance.IsActiveSeatActor(actorNumber)) return;
        if (!IsDealingReadyForPlay() || GameFlowState.Current == GameFlowPhase.GameFinished) return;

        botActorsThinking.Add(actorNumber);
        StartCoroutine(BotPlayRoutine(actorNumber));
    }

    static bool IsCardInHand(List<CardData> hand, CardData card) =>
        hand != null && hand.Exists(c => c.cardSuit == card.cardSuit && c.cardRank == card.cardRank);

    static CardData SanitizeBotCardChoice(List<CardData> hand, List<TrickCard> trick, CardData choice)
    {
        bool isLeading = trick == null || trick.Count == 0;
        List<CardData> legal = isLeading ? hand : GetValidCards(hand, trick);
        if (legal.Count == 0) return hand[0];
        if (IsCardInHand(legal, choice)) return choice;
        return legal[0];
    }

    bool PlayBotCard(int actorNumber, CardData card)
    {
        if (ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber)) return false;
        if (!CanAcceptCardPlay(actorNumber, (int)card.cardSuit, (int)card.cardRank))
        {
            Debug.LogWarning($"[Bot] Play rejected — actor={actorNumber} card={card.cardSuit}/{card.cardRank}");
            return false;
        }
        if (photonView == null) return false;
        photonView.RPC("RPC_PlayCard", RpcTarget.All, actorNumber, (int)card.cardSuit, (int)card.cardRank);
        return actorsPlayedThisTrick.Contains(actorNumber);
    }

    public void ForceBotPlayImmediate(int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient || !IsBotActor(actorNumber)) return;
        if (IsTrickLocked || _determineTrickRoutineRunning) return;
        if (ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber)) return;
        if (actorNumber != GetAuthoritativeTurnActor()) return;
        if (DeckManager.Instance == null || !DeckManager.Instance.botHands.TryGetValue(actorNumber, out List<CardData> hand)
            || hand == null || hand.Count == 0)
            return;

        botActorsThinking.Remove(actorNumber);

        bool isLeading = currentTrick == null || currentTrick.Count == 0;
        List<CardData> legal = isLeading ? new List<CardData>(hand) : GetValidCards(hand, currentTrick);
        if (legal.Count == 0) legal = new List<CardData>(hand);

        CardData preferred = legal[0];
        if (DehlaPakadAI.Instance != null)
            preferred = SanitizeBotCardChoice(hand, currentTrick, DehlaPakadAI.Instance.ThinkAndSelectCard(
                hand, currentTrick, currentTrumpSuit, isTrumpRevealed, actorNumber));

        if (PlayBotCard(actorNumber, preferred)) return;

        foreach (CardData fallback in legal)
        {
            if (PlayBotCard(actorNumber, fallback)) return;
        }

        Debug.LogError($"[Bot] Force play failed for actor {actorNumber} — hand={hand.Count} legal={legal.Count}");
    }

    void EnsureBotWatchdogRunning()
    {
        if (!PhotonNetwork.IsMasterClient || _botWatchdogCoroutine != null) return;
        _botWatchdogCoroutine = StartCoroutine(BotTurnWatchdogRoutine());
    }

    void StopBotWatchdog()
    {
        if (_botWatchdogCoroutine == null) return;
        StopCoroutine(_botWatchdogCoroutine);
        _botWatchdogCoroutine = null;
    }

    IEnumerator BotTurnWatchdogRoutine()
    {
        var wait = new WaitForSeconds(2.5f);
        while (true)
        {
            yield return wait;
            if (!PhotonNetwork.IsMasterClient || !IsDealingReadyForPlay()) continue;
            if (IsTrickLocked || _determineTrickRoutineRunning) continue;
            if (GameFlowState.Current != GameFlowPhase.InGame) continue;

            int actor = GetAuthoritativeTurnActor();
            if (!IsBotActor(actor)) continue;
            if (ActorInCurrentTrick(actor) || actorsPlayedThisTrick.Contains(actor)) continue;
            if (botActorsThinking.Contains(actor)) continue;

            Debug.LogWarning($"[BotWatchdog] Stuck turn detected — forcing actor {actor}");
            ForceBotPlayImmediate(actor);
        }
    }

    IEnumerator BotPlayRetryAfterDelay(int actorNumber, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (PhotonNetwork.IsMasterClient && IsBotActor(actorNumber)
            && actorNumber == GetAuthoritativeTurnActor()
            && !ActorInCurrentTrick(actorNumber) && !IsTrickLocked && !_determineTrickRoutineRunning)
        {
            TriggerBotTurnIfApplicable(actorNumber);
        }
    }

    IEnumerator BotPlayRoutine(int actorNumber)
    {
        yield return new WaitForSeconds(BotThinkDelay);

        bool cardActuallyPlayed = false;
        int trickCountAtStart = currentTrick?.Count ?? 0;
        try
        {
            if (actorNumber != GetAuthoritativeTurnActor() || IsTrickLocked || ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber))
                yield break;

            if (DeckManager.Instance == null) yield break;

            if (!DeckManager.Instance.botHands.TryGetValue(actorNumber, out List<CardData> hand) || hand == null || hand.Count == 0)
            {
                Debug.LogWarning($"[Bot] Empty hand for actor {actorNumber} — forcing sync");
                DeckManager.Instance.EnsureHandCachedForBot(actorNumber);
                if (!DeckManager.Instance.botHands.TryGetValue(actorNumber, out hand) || hand == null || hand.Count == 0)
                    yield break;
            }

            CardData botCard = DehlaPakadAI.Instance != null
                ? DehlaPakadAI.Instance.ThinkAndSelectCard(hand, currentTrick, currentTrumpSuit, isTrumpRevealed, actorNumber)
                : hand[0];
            botCard = SanitizeBotCardChoice(hand, currentTrick, botCard);
            cardActuallyPlayed = PlayBotCard(actorNumber, botCard);
        }
        finally
        {
            botActorsThinking.Remove(actorNumber);

            if (!cardActuallyPlayed && PhotonNetwork.IsMasterClient && IsBotActor(actorNumber)
                && actorNumber == GetAuthoritativeTurnActor()
                && !ActorInCurrentTrick(actorNumber) && !IsTrickLocked && !_determineTrickRoutineRunning
                && (currentTrick?.Count ?? 0) == trickCountAtStart)
            {
                long retryKey = BotRetryKey(actorNumber, trickCountAtStart);
                botRetryCounts.TryGetValue(retryKey, out int tries);
                if (tries < MaxBotPlayRetries)
                {
                    botRetryCounts[retryKey] = tries + 1;
                    StartCoroutine(BotPlayRetryAfterDelay(actorNumber, 0.35f + tries * 0.2f));
                }
                else
                {
                    Debug.LogError($"[Bot] Max retries for actor {actorNumber} — force playing");
                    ForceBotPlayImmediate(actorNumber);
                }
            }
        }
    }

    void ApplyNotMyTurnVisualState()
    {
        if (handAreaTransform == null) return;
        CardInteract.ClearGlobalSelection();
        CardInteract[] interacts = handAreaTransform.GetComponentsInChildren<CardInteract>();
        foreach (CardInteract ci in interacts)
        {
            if (ci == null || ci.isPlayed) continue;
            ci.isValidToPlay = false;
            ci.ApplyNotMyTurnVisual();
        }
    }

    void EndTurnCardVisuals()
    {
        CardInteract.canPlayCards = false;
        ApplyNotMyTurnVisualState();
    }

    void HighlightPlayableCards()
    {
        if (handAreaTransform == null) return;

        bool isLeading = currentTrick == null || currentTrick.Count == 0;
        List<CardData> validPlayableCards = isLeading ? new List<CardData>(myCards) : GetValidCards(myCards, currentTrick);
        var usedValidMatches = new Dictionary<(CardSuit, CardRank), int>();
        CardInteract[] interacts = handAreaTransform.GetComponentsInChildren<CardInteract>();

        foreach (CardInteract ci in interacts)
        {
            if (ci == null || ci.isPlayed) continue;
            CardDisplay d = ci.GetComponentInParent<CardDisplay>();
            if (d == null) continue;

            bool isValid = isLeading || IsCardPlayableForUi(d.myCardData, validPlayableCards, usedValidMatches);
            ci.isValidToPlay = isValid;

            if (isValid) ci.ApplyPlayableVisual(true);
            else ci.ApplyBlockedOnTurnVisual();
        }
    }

    public void ApplyRules(bool isMyTurn)
    {
        if (handAreaTransform == null) return;

        if (IsTrickLocked)
        {
            CardInteract.canPlayCards = false;
            EndTurnCardVisuals();
            return;
        }

        if (isMyTurn)
        {
            int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
            if (!IsGameplayInputBlocked && !ActorInCurrentTrick(localActor) && !actorsPlayedThisTrick.Contains(localActor))
            {
                CardInteract.canPlayCards = true;
                CardInteract.isPlayingCard = false;
            }

            CardInteract.ClearGlobalSelection();
            HighlightPlayableCards();
        }
        else
        {
            EndTurnCardVisuals();
        }
    }

    /// <summary>
    /// Dehla Pakad / Call Break follow-suit: must play led suit if possible;
    /// if any led-suit card can beat the highest led-suit on table, only those are playable.
    /// </summary>
    static bool IsCardPlayableForUi(CardData card, List<CardData> validList, Dictionary<(CardSuit, CardRank), int> usedMatches)
    {
        var key = (card.cardSuit, card.cardRank);
        int available = 0;
        foreach (CardData c in validList)
        {
            if (c.cardSuit == key.Item1 && c.cardRank == key.Item2)
                available++;
        }
        if (available == 0) return false;

        usedMatches.TryGetValue(key, out int taken);
        if (taken >= available) return false;
        usedMatches[key] = taken + 1;
        return true;
    }

    static void RemoveOneCardFromHand(List<CardData> hand, CardSuit suit, CardRank rank)
    {
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].cardSuit == suit && hand[i].cardRank == rank)
            {
                hand.RemoveAt(i);
                return;
            }
        }
    }

    public static List<CardData> GetValidCards(List<CardData> hand, List<TrickCard> trick)
    {
        if (hand == null || hand.Count == 0) return new List<CardData>();
        if (trick == null || trick.Count == 0) return new List<CardData>(hand);

        CardSuit ledSuit = trick[0].suit;
        List<CardData> ledSuitInHand = hand.FindAll(c => c.cardSuit == ledSuit);

        if (ledSuitInHand.Count > 0)
        {
            return ledSuitInHand;
        }

        return new List<CardData>(hand);
    }

    public void OnLocalPlayerPlayedCard(CardData cardData, GameObject cardObj)
    {
        int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (currentTrick != null && currentTrick.Any(c => c.actorNumber == localActor))
        {
            CardInteract.isPlayingCard = false;
            return;
        }
        if (!CanAcceptCardPlay(localActor, (int)cardData.cardSuit, (int)cardData.cardRank))
        {
            CardInteract.isPlayingCard = false;
            return;
        }

        CardInteract.isPlayingCard = true;
        EndTurnCardVisuals();

        _hasPendingLocalPlayStart = cardObj != null;
        if (cardObj != null) _pendingLocalPlayStartWorldPos = cardObj.transform.position;
        _pendingLocalPlaySuitIndex = (int)cardData.cardSuit;
        _pendingLocalPlayRankIndex = (int)cardData.cardRank;

        Destroy(cardObj);
        RemoveOneCardFromHand(myCards, cardData.cardSuit, cardData.cardRank);
        RefreshHandUI(false, true);
        if (photonView != null)
            photonView.RPC("RPC_PlayCard", RpcTarget.All, localActor, (int)cardData.cardSuit, (int)cardData.cardRank);

        StartUnlockPlayFailsafe();
    }

    void StartUnlockPlayFailsafe()
    {
        StopUnlockPlayFailsafe();
        _unlockPlayFailsafeCoroutine = StartCoroutine(UnlockPlayFailsafe());
    }

    void StopUnlockPlayFailsafe()
    {
        if (_unlockPlayFailsafeCoroutine == null) return;
        StopCoroutine(_unlockPlayFailsafeCoroutine);
        _unlockPlayFailsafeCoroutine = null;
    }

    IEnumerator UnlockPlayFailsafe()
    {
        yield return new WaitForSeconds(2.0f);
        _unlockPlayFailsafeCoroutine = null;
        if (CardInteract.isPlayingCard)
        {
            CardInteract.isPlayingCard = false;
            RefreshHandUI(false, true);
            ApplyRules(PhotonNetwork.LocalPlayer.ActorNumber == currentTurnActor);
        }
    }

    [PunRPC]
    public void RPC_PlayCard(int senderActorNum, int suitIndex, int rankIndex, PhotonMessageInfo info)
    {
        if (LocalInstance == null) return;
        if (LocalInstance != this) { LocalInstance.RPC_PlayCard(senderActorNum, suitIndex, rankIndex, info); return; }

        if (DeckManager.Instance != null && DeckManager.Instance.IsActorBotControlled(senderActorNum))
        {
            if (info.Sender != null && !info.Sender.IsMasterClient)
                return;
        }

        bool actorAlreadyPlayed = currentTrick != null && currentTrick.Any(c => c.actorNumber == senderActorNum);

        if (PhotonNetwork.LocalPlayer != null && senderActorNum == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            StopUnlockPlayFailsafe();
            CardInteract.isPlayingCard = false;
        }

        if (actorAlreadyPlayed) return;
        if (!CanAcceptCardPlay(senderActorNum, suitIndex, rankIndex)) return;

        CardData playedCard = new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex };

        LockTrickPlayInput();

        if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
            DeckManager.Instance.UpdateCachedHandOnMaster(senderActorNum, (CardSuit)suitIndex, (CardRank)rankIndex);

        int seat = GetSeatIndex(senderActorNum);
        Transform center = GetTableCenterTransform();
        
        GameObject cardObj = Object.Instantiate(cardUIPrefab, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(playedCard);

        Vector3 startPos = GetPlayerPositionForSeat(seat);
        if (PhotonNetwork.LocalPlayer != null && senderActorNum == PhotonNetwork.LocalPlayer.ActorNumber && _hasPendingLocalPlayStart)
        {
            startPos = _pendingLocalPlayStartWorldPos;
            _hasPendingLocalPlayStart = false;
        }

        cardObj.transform.position = startPos;
        cardObj.transform.localScale = Vector3.one;

        Vector3 targetLocal = GetFinalPositionForSeat(seat);
        cardObj.transform.DOScale(0.8f, 0.35f);
        cardObj.transform.DOLocalMove(targetLocal, 0.35f).SetEase(Ease.OutBack);
        cardObj.transform.DORotate(Vector3.zero, 0.35f);

        currentTrick.Add(new TrickCard { actorNumber = senderActorNum, suit = playedCard.cardSuit, rankValue = rankIndex, cardObject = cardObj });
        actorsPlayedThisTrick.Add(senderActorNum);

        if (currentTrick.Count > 4)
        {
            currentTrick.RemoveAt(currentTrick.Count - 1);
            actorsPlayedThisTrick.Remove(senderActorNum);
            Destroy(cardObj);
            CardInteract.isPlayingCard = false;
            return;
        }

        if (PhotonNetwork.IsMasterClient) SyncCurrentTrickToRoom();
        HandleTrumpModeAfterCardAdded(playedCard, senderActorNum);

        if (currentTrick.Count == 4)
        {
            if (TurnManager.Instance != null) TurnManager.Instance.StopTimer();
            botActorsThinking.Clear();
            if (!_determineTrickRoutineRunning)
            {
                isResolvingTrick = true;
                _determineTrickCoroutine = StartCoroutine(DetermineTrickWinnerRoutine());
            }
        }
        else
        {
            CardInteract.isPlayingCard = false;
            int nextActor = GetNextTurnActor(senderActorNum);
            if (PhotonNetwork.IsMasterClient) currentTurnActor = nextActor;
            ProcessTurn(nextActor);
        }
    }

    IEnumerator DetermineTrickWinnerRoutine()
    {
        if (_determineTrickRoutineRunning) yield break;
        GameFlowState.SetPhase(GameFlowPhase.ResolvingTrick, forceRecovery: true);
        _determineTrickRoutineRunning = true;
        isResolvingTrick = true;
        isCleaningTable = false;
        botActorsThinking.Clear();
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = true;
        LockTrickPlayInput();

        if (currentTrick == null || currentTrick.Count < 4)
        {
            isResolvingTrick = false;
            _determineTrickRoutineRunning = false;
            _determineTrickCoroutine = null;
            CardInteract.isPlayingCard = false;
            yield break;
        }

        List<TrickCard> trickSnapshot = new List<TrickCard>(currentTrick);
        yield return new WaitForSeconds(1.2f);

        if (trickSnapshot.Count < 4)
        {
            isResolvingTrick = false;
            _determineTrickRoutineRunning = false;
            _determineTrickCoroutine = null;
            CardInteract.isPlayingCard = false;
            yield break;
        }

        TrickCard winnerCard = TaashRules.DetermineTrickWinner(trickSnapshot, currentTrumpSuit);
        int tricksToWin = TaashRules.TricksPerGame;
        int winnerActor = winnerCard.actorNumber;
        bool isLastTrickOfGame = PhotonNetwork.IsMasterClient && totalTricksPlayed + 1 >= tricksToWin;

        isCleaningTable = true;
        HandleTrickWinner(winnerActor, trickSnapshot, isLastTrickOfGame);

        yield return new WaitForSeconds(0.5f);

        foreach (TrickCard tc in trickSnapshot)
            tc.cardObject = null;

        ClearAllTableCardClones();
        currentTrick.Clear();
        ClearTrickPlayLocks();
        _lastProcessTurnActor = -1;
        _lastProcessTurnTrickCount = -1;

        isCleaningTable = false;
        isResolvingTrick = false;
        _determineTrickRoutineRunning = false;
        _determineTrickCoroutine = null;

        if (PhotonNetwork.IsMasterClient)
            SyncCurrentTrickToRoom();

        EndTurnCardVisuals();
        CardInteract.isPlayingCard = false;

        if (PhotonNetwork.IsMasterClient)
        {
            totalTricksPlayed++;

            if (totalTricksPlayed >= tricksToWin)
            {
                GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);
                botActorsThinking.Clear();
                CardInteract.canPlayCards = false;
                if (photonView != null)
                    photonView.RPC("RPC_ShowGameResult", RpcTarget.All);
                else
                    ShowGameResultLocal();
                yield break;
            }
        }

        GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
        ProcessTurn(winnerActor);
    }

    public int GetSeatIndex(int actorNum)
    {
        if (tableTurnOrder.Count < 4) BuildTableTurnOrder();
        int idx = tableTurnOrder.IndexOf(actorNum);
        return idx >= 0 ? idx : 0;
    }

    private bool _isDealAnimRunning = false;
    public static bool IsDealAnimationRunning { get; private set; }
    const float DealFlyTargetBlend = 1.0f;

    public void PlayDealAnimationOnly(int cardsInBatch)
    {
        if (isDealingComplete) return;

        GameFlowState.SetPhase(GameFlowPhase.Dealing, forceRecovery: true);
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;
        botActorsThinking.Clear();
        if (!IsDealAnimationRunning && handAreaTransform != null)
            ClearHandUI();
        StartCoroutine(DealAnimationOnlyRoutine(cardsInBatch));
    }

    public const float DealFlyDuration = 0.35f;
    public const float DealFlyDestroyDelay = 0.3f;
    public const float DealCardLaunchGap = 0.04f;
    public const float DealShrinkDuration = 0.25f;
    public const float DealPacketCardSpread = 12f;
    public const float DealRoundSettlePause = 0.06f;

    public static float GetDealBatchDuration(int cardsInBatch)
    {
        float perSeat = (cardsInBatch - 1) * DealCardLaunchGap + DealShrinkDuration + 0.2f;
        return 4f * perSeat + DealRoundSettlePause + 0.2f;
    }

    static string GetDealRoundLabel(int cardsInBatch)
    {
        if (TaashRules.IsTwoTaashMode)
        {
            if (cardsInBatch == 10) return "2 Taash Round 1 — 10-card packet per player";
            if (cardsInBatch == 8) return "2 Taash Round 2/3 — 8-card packet per player";
        }
        else
        {
            if (cardsInBatch == 5) return "Round 1 — 5-card packet per player";
            if (cardsInBatch == 4) return "Round 2/3 — 4-card packet per player";
        }
        return $"Deal batch — {cardsInBatch}-card packet per player";
    }

    static string GetSeatDealName(int seat)
    {
        switch (seat)
        {
            case 0: return "Bottom";
            case 1: return "Left";
            case 2: return "Top";
            case 3: return "Right";
            default: return "Player";
        }
    }

    Transform GetPlayerDealTarget(int seatIndex)
    {
        Transform baseTarget = null;
        if (seatIndex == 0 && handAreaTransform != null)
            baseTarget = handAreaTransform;
        else if (playerPositions != null && seatIndex >= 0 && seatIndex < playerPositions.Length)
            baseTarget = playerPositions[seatIndex];

        if (baseTarget == null) return null;

        // More robust search for child targets (avatars, profiles, logos)
        foreach (Transform child in baseTarget)
        {
            string ln = child.name.ToLower();
            if (ln.Contains("avatar") || ln.Contains("logo") || ln.Contains("profile") || ln.Contains("face"))
                return child;
        }

        return baseTarget;
    }

    IEnumerator DealAnimationOnlyRoutine(int cardsInBatch)
    {
        while (_isDealAnimRunning) yield return null;
        _isDealAnimRunning = true;
        IsDealAnimationRunning = true;

        if (canvasTransform == null)
        {
            EnsureGameplayUiRefs();
            if (canvasTransform == null)
            {
                Canvas rootCanvas = Object.FindAnyObjectByType<Canvas>();
                if (rootCanvas != null)
                    canvasTransform = rootCanvas.transform;
            }
        }

        if (canvasTransform == null)
        {
            _isDealAnimRunning = false;
            IsDealAnimationRunning = false;
            yield break;
        }

        RectTransform canvasRect = canvasTransform as RectTransform;
        Vector2 deckAnchor = GetAnchorInCanvas(canvasRect, centerPos != null ? centerPos.position : canvasRect.position);

        // 🚀 User Order: Bottom (0) → Right (3) → Top (2) → Left (1)
        int[] dealOrder = { 0, 3, 2, 1 };

        foreach (int seat in dealOrder)
        {
            Transform seatTarget = GetPlayerDealTarget(seat);
            if (seatTarget == null) continue;

            Vector2 seatAnchor = GetAnchorInCanvas(canvasRect, seatTarget.position);
            Vector2 midwayAnchor = Vector2.Lerp(deckAnchor, seatAnchor, DealFlyTargetBlend);

            for (int i = 0; i < cardsInBatch; i++)
            {
                GameObject flyingCard = Object.Instantiate(dummyCardPrefab, canvasTransform);
                RectTransform cardRt = flyingCard.GetComponent<RectTransform>();
                cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);

                float spreadOffset = (i - (cardsInBatch - 1) * 0.5f) * DealPacketCardSpread;
                cardRt.anchoredPosition = deckAnchor + new Vector2(spreadOffset * 0.35f, 0f);

                Vector2 target = midwayAnchor + new Vector2(spreadOffset * 0.5f, 0f);

                cardRt.DOAnchorPos(target, DealFlyDuration).SetEase(Ease.OutSine);
                cardRt.DOScale(new Vector3(0.5f, 0.5f, 1f), DealShrinkDuration).SetEase(Ease.OutQuad);
                Object.Destroy(flyingCard, DealFlyDestroyDelay);

                yield return new WaitForSeconds(DealCardLaunchGap);
            }

            yield return new WaitForSeconds(0.2f);
        }

        yield return new WaitForSeconds(0.2f);
        _isDealAnimRunning = false;
        IsDealAnimationRunning = false;
    }

    private int SuitOrder(CardSuit suit)
    {
        switch (suit)
        {
            case CardSuit.Hearts: return 0;
            case CardSuit.Clubs: return 1;
            case CardSuit.Spades: return 2;
            case CardSuit.Diamonds: return 3;
            default: return 99;
        }
    }

    private int RankOrder(CardRank rank)
    {
        return -(int)rank; // Ace highest if enum Two=0 ... Ace=12
    }

    public void RefreshHandUI(bool animate = true, bool force = false)
    {
        if (handAreaTransform == null) return;
        if (!force && (IsDealAnimationRunning || !isDealingComplete)) return;

        LayoutGroup lg = handAreaTransform.GetComponent<LayoutGroup>();
        if (lg != null) lg.enabled = false;

        for (int i = handAreaTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = handAreaTransform.GetChild(i);
            CardInteract interact = child.GetComponent<CardInteract>();
            if (interact != null && interact.isPlayed) continue;
            child.DOKill();
            Object.DestroyImmediate(child.gameObject);
        }

        myCards = myCards.OrderBy(c => SuitOrder(c.cardSuit)).ThenBy(c => RankOrder(c.cardRank)).ToList();

        bool isTwoRows = myCards.Count > 13;
        const int cardsPerRow = 13;
        float spacingX = 120f;

        for (int i = 0; i < myCards.Count; i++)
        {
            GameObject newCardUI = Object.Instantiate(cardUIPrefab, handAreaTransform);
            newCardUI.GetComponent<CardDisplay>()?.SetCardData(myCards[i]);

            RectTransform rt = newCardUI.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;

            int row = i / cardsPerRow;
            int col = i % cardsPerRow;
            int cardsInThisRow = Mathf.Min(cardsPerRow, myCards.Count - row * cardsPerRow);

            float startX = -((cardsInThisRow - 1) * spacingX) / 2f;
            float xPos = startX + col * spacingX;

            float yPos = 0f;
            if (isTwoRows)
                yPos = row == 0 ? 50f : -70f;

            rt.anchoredPosition = new Vector2(xPos, yPos);
        }

        if (animate && myCards.Count > 0)
        {
            int idx = 0;
            foreach (Transform child in handAreaTransform)
            {
                RectTransform rt = child.GetComponent<RectTransform>();
                if (rt == null) continue;

                CardInteract interact = child.GetComponent<CardInteract>();
                if (interact != null && interact.isPlayed) continue;

                float targetY = rt.anchoredPosition.y;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, targetY - 100f);
                rt.DOAnchorPosY(targetY, 0.35f).SetEase(Ease.OutBack).SetDelay(idx * 0.02f);
                idx++;
            }
        }
    }

    public void AssignFullHandLocal(int targetActor, int[] suitIndices, int[] rankIndices)
    {
        if (PhotonNetwork.LocalPlayer.ActorNumber != targetActor) return;
        myCards.Clear();
        if (suitIndices == null || rankIndices == null || suitIndices.Length != rankIndices.Length) return;
        for (int i = 0; i < suitIndices.Length; i++)
            myCards.Add(new CardData { cardSuit = (CardSuit)suitIndices[i], cardRank = (CardRank)rankIndices[i] });

        if (!IsDealAnimationRunning && !_isDealAnimRunning)
            RefreshHandUI(animate: false, force: true);
    }

    public void OnDealingComplete(int starterActor)
    {
        if (isDealingComplete) return;
        if (LocalInstance != null && LocalInstance != this) { LocalInstance.OnDealingComplete(starterActor); return; }

        bool matchInProgress = false;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object started))
            matchInProgress = (bool)started;

        if (!matchInProgress)
            GameFlowState.SetPhase(GameFlowPhase.Dealing, forceRecovery: true);
        BuildTableTurnOrder();
        lastTrickWinnerActor = -1;

        float turnDelay = matchInProgress ? 0.15f : 1.1f;
        StartCoroutine(HandRevealThenStartGame(starterActor, turnDelay, matchInProgress));
    }

    IEnumerator HandRevealThenStartGame(int starterActor, float turnDelay, bool matchInProgress)
    {
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;

        while (IsDealAnimationRunning || _isDealAnimRunning)
            yield return null;

        isDealingComplete = true;

        if (!matchInProgress)
        {
            yield return new WaitForSeconds(DealFlyDuration + 0.1f);
            if (handAreaTransform != null)
                ClearHandUI();

            _handRevealRunning = true;
            yield return AnimateHandSpreadReveal();
            _handRevealRunning = false;
        }
        else
        {
            RefreshHandUI(animate: false, force: true);
        }

        ShowOpponentFansWithAnimation();
        yield return new WaitForSeconds(turnDelay);

        GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
        BuildTableTurnOrder();
        if (PhotonNetwork.IsMasterClient)
        {
            currentTurnActor = starterActor;
            EnsureBotWatchdogRunning();
        }
        ProcessTurn(starterActor);
    }

    IEnumerator AnimateHandSpreadReveal()
    {
        if (handAreaTransform == null || cardUIPrefab == null) yield break;
        if (IsDealAnimationRunning || _isDealAnimRunning) yield break;

        myCards = myCards
            .OrderBy(c => SuitOrder(c.cardSuit))
            .ThenBy(c => RankOrder(c.cardRank))
            .ToList();

        ClearHandUI();

        HorizontalLayoutGroup revealHlg = handAreaTransform.GetComponent<HorizontalLayoutGroup>();
        if (revealHlg != null) revealHlg.enabled = false;

        float handWidthPx = HandLayoutHelper.GetHandAreaWidth(handAreaTransform as RectTransform);
        float prefabWidth = HandLayoutHelper.GetPrefabCardWidth(cardUIPrefab);
        HandLayoutConfig layout = HandLayoutHelper.GetLayout(myCards.Count, handWidthPx, prefabWidth);
        float startX = HandLayoutHelper.ComputeStartX(layout, myCards.Count);

        var cardRects = new List<RectTransform>();
        for (int i = 0; i < myCards.Count; i++)
        {
            GameObject card = Object.Instantiate(cardUIPrefab, handAreaTransform);
            card.GetComponent<CardDisplay>()?.SetCardData(myCards[i]);
            RectTransform rt = card.GetComponent<RectTransform>();
            float targetX = startX + i * (layout.prefabCardWidth + layout.spacing);
            rt.anchoredPosition = new Vector2(targetX, 0f);
            rt.localScale = Vector3.one * 0.9f;
            cardRects.Add(rt);
        }

        if (cardRects.Count == 0) yield break;

        Sequence popSeq = DOTween.Sequence();
        for (int i = 0; i < cardRects.Count; i++)
        {
            RectTransform rt = cardRects[i];
            popSeq.Insert(i * 0.03f, rt.DOScale(1.05f, 0.12f).SetEase(Ease.OutQuad));
            popSeq.Insert(i * 0.03f + 0.12f, rt.DOScale(1f, 0.12f).SetEase(Ease.OutBack));
        }
        yield return popSeq.WaitForCompletion();
    }

    [PunRPC]
    void RPC_ShowGameResult()
    {
        ShowGameResultLocal();
    }

    public void ShowGameResultLocal()
    {
        if (_resultPanelShown) return;
        _resultPanelShown = true;

        botActorsThinking.Clear();
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = true;
        if (TurnManager.Instance != null) TurnManager.Instance.StopTimer();

        GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);
        if (ResultManager.Instance != null) ResultManager.Instance.ShowResult();
    }

    static Vector2 GetAnchorInCanvas(RectTransform canvasRect, Vector3 worldPos)
    {
        if (canvasRect == null) return Vector2.zero;
        Camera cam = null;
        Canvas c = canvasRect.GetComponent<Canvas>();
        if (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = c.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            RectTransformUtility.WorldToScreenPoint(cam, worldPos),
            cam,
            out Vector2 local);
        return local;
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (newMasterClient.IsLocal && isDealingComplete && currentTurnActor != -1) ProcessTurn(currentTurnActor);
    }
}
