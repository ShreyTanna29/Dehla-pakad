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
    private static bool _hasPendingLocalPlayStart;
    private static Vector3 _pendingLocalPlayStartWorldPos;
    private static int _pendingLocalPlaySuitIndex = -1;
    private static int _pendingLocalPlayRankIndex = -1;
    private static bool _awaitingOwnPlayRpc;
    private Coroutine _unlockPlayFailsafeCoroutine;

    void Awake()
    {
        EnsureHandListsInitialized();

        // Only the local human NetworkPlayer may own gameplay/UI state (multiplayer-safe).
        if (photonView != null && photonView.IsMine)
            LocalInstance = this;
    }

    void EnsureHandListsInitialized()
    {
        if (myCards == null) myCards = new List<CardData>();
        if (currentTrick == null) currentTrick = new List<TrickCard>();
    }

    public static PlayerHand ResolveLocalHand()
    {
        if (LocalInstance != null && LocalInstance.photonView != null && LocalInstance.photonView.IsMine)
            return LocalInstance;

        if (PhotonNetwork.IsConnected)
        {
            foreach (PhotonView view in PhotonNetwork.PhotonViewCollection)
            {
                if (view == null || !view.IsMine) continue;
                if (!view.gameObject.name.Contains("NetworkPlayer")) continue;

                PlayerHand hand = view.GetComponent<PlayerHand>();
                if (hand != null)
                {
                    LocalInstance = hand;
                    return hand;
                }
            }
        }

        PlayerHand[] hands = Object.FindObjectsByType<PlayerHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PlayerHand hand in hands)
        {
            if (hand != null && hand.photonView != null && hand.photonView.IsMine)
            {
                LocalInstance = hand;
                return hand;
            }
        }

        if (PhotonNetwork.OfflineMode)
        {
            foreach (PlayerHand hand in hands)
            {
                if (hand != null)
                {
                    LocalInstance = hand;
                    return hand;
                }
            }
        }

        return LocalInstance;
    }

    private void OnDestroy()
    {
        // Stop any in-flight deal/animation coroutines and kill tweens targeting this object or its
        // children, so queued OnComplete callbacks never run on the destroyed NetworkPlayer (which
        // caused MissingReferenceExceptions when leaving a match or reconnecting).
        StopAllCoroutines();
        DG.Tweening.DOTween.Kill(transform);
        Transform[] owned = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < owned.Length; i++)
            if (owned[i] != null) DG.Tweening.DOTween.Kill(owned[i]);

        if (LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    [Header("UI Setup")]
    public GameObject cardUIPrefab; 
    public Transform handAreaTransform;
    [Tooltip("Gameplay canvas root — used to resolve table/hand refs without GameObject.Find.")]
    public Transform gameUiSearchRoot;
    public Transform tableCenterTransform;

    [Header("Table Center Trick Layout")]
    [SerializeField] float centerCardSpacing = 130f;
    [SerializeField] float centerCardYOffset = 30f;
    [SerializeField] float centerCardMoveDuration = 0.15f;
    [SerializeField] private float centerCardScale = 0.85f;

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
    public static bool isHiddenCardActive = false;
    public static CardData hiddenTrumpCard;
    public static int hiddenCardOwnerActor = -1;

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
        // Trick/table state lives only on the local human hand — ignore on remote NetworkPlayer copies.
        PlayerHand handler = ResolveLocalHand();
        if (handler != null && handler != this) return;
        if (handler == null && photonView != null && !photonView.IsMine) return;

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

    void ClearAllTableCardClones(List<TrickCard> specificTrick = null)
    {
        if (specificTrick != null)
        {
            foreach (var tc in specificTrick)
            {
                if (tc != null && tc.cardObject != null)
                {
                    tc.cardObject.transform.DOKill();
                    Object.Destroy(tc.cardObject);
                    tc.cardObject = null;
                }
            }
            return;
        }

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
        float gap = centerCardSpacing;
        float yLift = centerCardYOffset;

        switch (seat)
        {
            case 0: return new Vector3(0f, -gap + yLift, 0f);
            case 1: return new Vector3(-gap, yLift, 0f);
            case 2: return new Vector3(0f, gap + yLift, 0f);
            case 3: return new Vector3(gap, yLift, 0f);
            default: return new Vector3(0f, yLift, 0f);
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
        cardObj.transform.localScale = cardUIPrefab != null
            ? cardUIPrefab.transform.localScale * centerCardScale
            : Vector3.one * centerCardScale;

        Vector3 targetLocal = GetFinalPositionForSeat(seat);
        cardObj.transform.DOLocalMove(targetLocal, centerCardMoveDuration).SetEase(Ease.InOutSine);
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

    public static bool IsGameplayInputBlocked =>
        IsDealAnimationRunning || IsTrickLocked ||
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
            ApplyTrumpChange(playedCard.cardSuit);
            return;
        }

        if (mode == GameModeType.HiddenTrump && !isTrumpRevealed && currentTrick.Count >= 2)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                if (photonView != null)
                    photonView.RPC(nameof(RPC_RevealHiddenTrump), RpcTarget.All, (int)hiddenTrumpCard.cardSuit);
                else
                    RPC_RevealHiddenTrump((int)hiddenTrumpCard.cardSuit);
            }
            else if (!PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode)
            {
                RPC_RevealHiddenTrump((int)hiddenTrumpCard.cardSuit);
            }
        }
    }

    [PunRPC]
    void RPC_SyncTrump(int suitIndex)
    {
        if (hasTrumpBeenSetOnce) return;
        hasTrumpBeenSetOnce = true;
        ApplyTrumpChange((CardSuit)suitIndex);
    }

    public static void ApplyHiddenTrumpInfo(int ownerActor, int suit, int rank)
    {
        isHiddenCardActive = true;
        hiddenCardOwnerActor = ownerActor;
        hiddenTrumpCard = new CardData { cardSuit = (CardSuit)suit, cardRank = (CardRank)rank };
        isTrumpRevealed = false;
        currentTrumpSuit = CardSuit.Spades;
        Debug.Log($"[Hidden Trump] Card hidden for actor {ownerActor} ({hiddenTrumpCard.cardRank} of {hiddenTrumpCard.cardSuit}).");
    }

    [PunRPC]
    public void RPC_SetHiddenTrumpInfo(int ownerActor, int suit, int rank)
    {
        ApplyHiddenTrumpInfo(ownerActor, suit, rank);
    }

    [PunRPC]
    public void RPC_RevealHiddenTrump(int suitIndex)
    {
        if (isTrumpRevealed) return;

        CardSuit suit = isHiddenCardActive ? hiddenTrumpCard.cardSuit : (CardSuit)suitIndex;
        isTrumpRevealed = true;
        isHiddenCardActive = true;
        currentTrumpSuit = suit;
        hasTrumpBeenSetOnce = true;

        if (TrumpManager.Instance != null)
            TrumpManager.Instance.RevealHiddenTrump(suit);
        else
            ApplyTrumpChange(suit);

        if (LocalInstance != null)
        {
            LocalInstance.RefreshHandUI(true, true);
            bool myTurn = PhotonNetwork.LocalPlayer != null
                && PhotonNetwork.LocalPlayer.ActorNumber == LocalInstance.currentTurnActor;
            LocalInstance.ApplyRules(myTurn);
        }

        Debug.Log($"[Hidden Trump] Unlocked on cut — trump is {suit}");
    }

    void ApplyTrumpChange(CardSuit newSuit)
    {
        currentTrumpSuit = newSuit;
        isTrumpRevealed = true;

        if (TrumpManager.Instance != null)
            TrumpManager.Instance.SetTrumpSuit(newSuit, true, true);
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
        PlayerHand local = ResolveLocalHand();
        if (local != null && local != this)
        {
            local.RPC_ResetHand();
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
        isHiddenCardActive = false;
        hiddenCardOwnerActor = -1;
        hiddenTrumpCard = default;
        hasTrumpBeenSetOnce = false;
        currentTrumpSuit = CardSuit.Spades;
        isTrumpRevealed = true;
        totalTricksPlayed = 0;
        ClearAllTableCardClones();
        ClearTwoSarState();
        currentTrick.Clear();
        _localCurrentTurnActor = -1;
        _lastHandledTurnActor = -1;
        _lastProcessTurnActor = -1;
        _lastProcessTurnTrickCount = -1;
        _resultPanelShown = false;
        _autoLastCardScheduledActor = -1;
        dealRevealLimit = -1;
        dealAnimateFromIndex = 0;
        // Task 23: reset the per-card deal-animation dedup set each round. Without this it keeps the
        // previous round's card identities, so any new-round card sharing a suit+rank with a card the
        // player held before would be treated as "already dealt" and snap in without its fly-in —
        // producing an inconsistent (half-animated) deal on rounds 2+.
        _dealtCardKeys.Clear();
        isDealingComplete = false;
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

    /// <summary>Destroys runtime card UI from the table/hand areas when leaving a match.</summary>
    public static void CleanupRuntimeCardUi()
    {
        if (LocalInstance != null)
        {
            LocalInstance.ResetHand();
            return;
        }

        string[] areas =
        {
            "Player_Hand_Area", "Table_Center",
            "Opponent_Left", "Opponent_Top", "Opponent_Right"
        };

        foreach (string area in areas)
            DestroyRuntimeCardsUnder(area);
    }

    static void DestroyRuntimeCardsUnder(string uiName)
    {
        if (!UiSafeLookup.TryGet(uiName, out GameObject areaGo) || areaGo == null) return;

        Transform area = areaGo.transform;
        for (int i = area.childCount - 1; i >= 0; i--)
        {
            Transform child = area.GetChild(i);
            if (child == null) continue;
            if (child.GetComponent<CardDisplay>() == null && !child.name.Contains("Card"))
                continue;
            child.DOKill();
            Object.Destroy(child.gameObject);
        }

        Transform fan = area.Find("CardFan");
        if (fan != null)
        {
            for (int i = fan.childCount - 1; i >= 0; i--)
            {
                Transform child = fan.GetChild(i);
                child.DOKill();
                Object.Destroy(child.gameObject);
            }
            fan.gameObject.SetActive(false);
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

    /// <summary>
    /// Public hook to rebuild the seat/turn order after a mid-game seat change
    /// (e.g. a bot seat handed off to a newly invited player).
    /// </summary>
    public void RebuildSeatOrderPublic() => BuildTableTurnOrder();

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
        if (IsDealAnimationRunning) return false;
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
        
        // If we are resolving a full trick (4 cards), we can still accept the 1st card of the NEXT trick.
        if (_determineTrickRoutineRunning && currentTrick != null && currentTrick.Count >= 4)
        {
            // Allow if it's the turn of the player who should lead next
            if (IsActorsTurnToPlay(senderActorNum)) return true;
        }

        if (IsTrickLocked || _determineTrickRoutineRunning) return false;
        if (currentTrick == null || currentTrick.Count >= 4) return false;
        if (ActorInCurrentTrick(senderActorNum)) return false;

        if (_awaitingOwnPlayRpc
            && PhotonNetwork.LocalPlayer != null
            && senderActorNum == PhotonNetwork.LocalPlayer.ActorNumber
            && suitIndex == _pendingLocalPlaySuitIndex
            && rankIndex == _pendingLocalPlayRankIndex)
            return true;

        if (!IsActorsTurnToPlay(senderActorNum)) return false;
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
        PlayerHand local = ResolveLocalHand();
        if (local != null && local != this)
        {
            local.RPC_SyncGameState(turnActor, dealingDone);
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
            // Task 10: never stop the timer just because a player is flagged inactive.
            // Always run the 18s timer so that on timeout a valid card is auto-played,
            // instead of stalling the match or dropping the player "offline".
            TurnManager.Instance.StartTurn(actorNumber);
        }

        bool isMyTurn = (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber);

        if (isMyTurn && !IsGameplayInputBlocked && !ActorInCurrentTrick(actorNumber) && !actorsPlayedThisTrick.Contains(actorNumber))
        {
            CardInteract.canPlayCards = true;
            CardInteract.isPlayingCard = false;
        }

        ApplyRules(isMyTurn);

        if (isMyTurn)
            AutoPlayLastCardIfApplicable(actorNumber);

        if (PhotonNetwork.IsMasterClient && IsBotActor(actorNumber) && !ActorInCurrentTrick(actorNumber))
            TriggerBotTurnIfApplicable(actorNumber);
    }

    // Task 21: when the local player has exactly one card left and it becomes their turn,
    // automatically play that final card (no manual tap needed).
    private int _autoLastCardScheduledActor = -1;

    void AutoPlayLastCardIfApplicable(int actorNumber)
    {
        if (PhotonNetwork.LocalPlayer == null || PhotonNetwork.LocalPlayer.ActorNumber != actorNumber) return;
        if (myCards == null || myCards.Count == 0) return;
        if (IsGameplayInputBlocked || IsTrickLocked) return;
        if (ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber)) return;
        if (_autoLastCardScheduledActor == actorNumber) return;

        // Task 21: auto-play when the player has no real choice — either it is the final card in the
        // hand, OR exactly one legal card remains for the current trick. In both cases tapping is
        // pointless and forcing the player to tap can stall the trick/round, so we play it for them.
        bool finalCard = myCards.Count == 1;
        if (!finalCard)
        {
            List<CardData> legal = GetValidCards(myCards, currentTrick, actorNumber);
            if (legal == null || legal.Count != 1) return;
        }

        _autoLastCardScheduledActor = actorNumber;
        StartCoroutine(AutoPlayLastCardRoutine(actorNumber));
    }

    IEnumerator AutoPlayLastCardRoutine(int actorNumber)
    {
        // Brief pause so the player can see the final card before it is played automatically.
        yield return new WaitForSeconds(0.6f);

        _autoLastCardScheduledActor = -1;

        if (PhotonNetwork.LocalPlayer == null || PhotonNetwork.LocalPlayer.ActorNumber != actorNumber) yield break;
        if (currentTurnActor != actorNumber) yield break;
        if (myCards == null || myCards.Count == 0) yield break;
        if (IsTrickLocked || IsGameplayInputBlocked || CardInteract.isPlayingCard) yield break;
        if (ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber)) yield break;

        // Re-resolve the card to play after the delay (hand/legality may have changed): play the
        // final card if only one remains, otherwise the single legal option (Task 21).
        CardData toPlay;
        if (myCards.Count == 1)
        {
            toPlay = myCards[0];
        }
        else
        {
            List<CardData> legal = GetValidCards(myCards, currentTrick, actorNumber);
            if (legal == null || legal.Count != 1) yield break; // a real choice re-appeared — let the player tap
            toPlay = legal[0];
        }

        GameObject cardObj = FindLocalCardObject(toPlay);
        if (cardObj == null) yield break;

        CardInteract.canPlayCards = true;
        CardInteract.isPlayingCard = true;
        OnLocalPlayerPlayedCard(toPlay, cardObj);
    }

    GameObject FindLocalCardObject(CardData card)
    {
        if (handAreaTransform == null) return null;
        foreach (Transform child in handAreaTransform)
        {
            CardDisplay disp = child.GetComponent<CardDisplay>();
            if (disp == null) disp = child.GetComponentInChildren<CardDisplay>(true);
            if (disp != null
                && disp.myCardData.cardSuit == card.cardSuit
                && disp.myCardData.cardRank == card.cardRank)
            {
                CardInteract interact = child.GetComponent<CardInteract>();
                if (interact == null) interact = child.GetComponentInChildren<CardInteract>(true);
                if (interact != null && interact.isPlayed) continue;
                return child.gameObject;
            }
        }
        return null;
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

    public static bool IsHiddenTrumpCard(CardData card) =>
        isHiddenCardActive && !isTrumpRevealed
        && card.cardSuit == hiddenTrumpCard.cardSuit && card.cardRank == hiddenTrumpCard.cardRank;

    static bool IsCardLegalPlay(List<CardData> hand, List<TrickCard> trick, CardData card, int forActorNumber) =>
        IsCardInHand(GetValidCards(hand, trick, forActorNumber), card);

    static CardData SanitizeBotCardChoice(List<CardData> hand, List<TrickCard> trick, CardData choice, int forActorNumber)
    {
        List<CardData> legal = GetValidCards(hand, trick, forActorNumber);
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

        int suit = (int)card.cardSuit;
        int rank = (int)card.cardRank;

        // Apply on master immediately — RpcTarget.All is not synchronous, so retry/watchdog used to false-fail.
        if (PhotonNetwork.IsMasterClient)
        {
            RPC_PlayCard(actorNumber, suit, rank, default);
            if (!actorsPlayedThisTrick.Contains(actorNumber))
                return false;

            if (!PhotonNetwork.OfflineMode)
                photonView.RPC("RPC_PlayCard", RpcTarget.Others, actorNumber, suit, rank);
            return true;
        }

        photonView.RPC("RPC_PlayCard", RpcTarget.All, actorNumber, suit, rank);
        return actorsPlayedThisTrick.Contains(actorNumber);
    }

    public void ForceBotPlayImmediate(int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient || !IsBotActor(actorNumber)) return;
        if (IsTrickLocked || _determineTrickRoutineRunning) return;
        if (ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber)) return;
        if (actorNumber != GetAuthoritativeTurnActor()) return;
        if (DeckManager.Instance == null) return;

        // After a master switch / reconnect the hand may only exist in room properties.
        // Restore it from there before giving up so the watchdog can recover the stuck bot.
        if (!DeckManager.Instance.botHands.TryGetValue(actorNumber, out List<CardData> hand)
            || hand == null || hand.Count == 0)
        {
            DeckManager.Instance.EnsureHandCachedForBot(actorNumber);
            if (!DeckManager.Instance.botHands.TryGetValue(actorNumber, out hand)
                || hand == null || hand.Count == 0)
                return;
        }

        botActorsThinking.Remove(actorNumber);

        List<CardData> legal = GetValidCards(hand, currentTrick, actorNumber);
        if (legal.Count == 0) legal = new List<CardData>(hand);

        CardData preferred = legal[0];
        if (DehlaPakadAI.Instance != null)
            preferred = SanitizeBotCardChoice(hand, currentTrick, DehlaPakadAI.Instance.ThinkAndSelectCard(
                hand, currentTrick, currentTrumpSuit, isTrumpRevealed, actorNumber), actorNumber);

        if (PlayBotCard(actorNumber, preferred)) return;

        foreach (CardData fallback in legal)
        {
            if (PlayBotCard(actorNumber, fallback)) return;
        }

        Debug.LogError($"[Bot] Force play failed for actor {actorNumber} — hand={hand.Count} legal={legal.Count}");
    }

    public void EnsureBotWatchdogRunning()
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
            botCard = SanitizeBotCardChoice(hand, currentTrick, botCard, actorNumber);
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

        EnsureHandListsInitialized();
        List<CardData> availableHand = new List<CardData>(myCards);

        if (isHiddenCardActive && !isTrumpRevealed
            && PhotonNetwork.LocalPlayer != null
            && PhotonNetwork.LocalPlayer.ActorNumber == hiddenCardOwnerActor)
        {
            int hiddenIndex = availableHand.FindIndex(c =>
                c.cardSuit == hiddenTrumpCard.cardSuit && c.cardRank == hiddenTrumpCard.cardRank);
            if (hiddenIndex >= 0)
                availableHand.RemoveAt(hiddenIndex);
        }

        bool isLeading = currentTrick == null || currentTrick.Count == 0;
        List<CardData> validPlayableCards = isLeading
            ? new List<CardData>(availableHand)
            : GetValidCards(availableHand, currentTrick);

        var usedValidMatches = new Dictionary<(CardSuit, CardRank), int>();
        CardInteract[] interacts = handAreaTransform.GetComponentsInChildren<CardInteract>();
        bool hiddenCardBlockedUI = false;
        int lastHandSibling = -1;
        foreach (CardInteract c2 in interacts)
        {
            if (c2 == null || c2.isPlayed) continue;
            lastHandSibling = Mathf.Max(lastHandSibling, c2.transform.GetSiblingIndex());
        }

        foreach (CardInteract ci in interacts)
        {
            if (ci == null || ci.isPlayed) continue;
            CardDisplay d = ci.GetComponentInParent<CardDisplay>();
            if (d == null) continue;

            bool isThisCardHidden = false;
            if (isHiddenCardActive && !isTrumpRevealed && !hiddenCardBlockedUI
                && PhotonNetwork.LocalPlayer != null
                && PhotonNetwork.LocalPlayer.ActorNumber == hiddenCardOwnerActor
                && d.myCardData.cardSuit == hiddenTrumpCard.cardSuit
                && d.myCardData.cardRank == hiddenTrumpCard.cardRank
                && ci.transform.GetSiblingIndex() == lastHandSibling)
            {
                isThisCardHidden = true;
                hiddenCardBlockedUI = true;
            }

            bool isValid = false;
            if (!isThisCardHidden)
            {
                if (isLeading)
                    isValid = availableHand.Exists(c =>
                        c.cardSuit == d.myCardData.cardSuit && c.cardRank == d.myCardData.cardRank);
                else
                    isValid = IsCardPlayableForUi(d.myCardData, validPlayableCards, usedValidMatches);
            }

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

    public static List<CardData> GetValidCards(List<CardData> hand, List<TrickCard> trick, int forActorNumber = -1)
    {
        if (hand == null || hand.Count == 0) return new List<CardData>();

        List<CardData> availableHand = new List<CardData>(hand);

        if (isHiddenCardActive && !isTrumpRevealed && forActorNumber == hiddenCardOwnerActor)
        {
            int hiddenIndex = availableHand.FindIndex(c =>
                c.cardSuit == hiddenTrumpCard.cardSuit && c.cardRank == hiddenTrumpCard.cardRank);
            if (hiddenIndex >= 0)
                availableHand.RemoveAt(hiddenIndex);
        }

        if (trick == null || trick.Count == 0) return availableHand;

        CardSuit ledSuit = trick[0].suit;
        List<CardData> ledSuitInHand = availableHand.FindAll(c => c.cardSuit == ledSuit);

        if (ledSuitInHand.Count > 0) return ledSuitInHand;

        return availableHand;
    }

    public void OnLocalPlayerPlayedCard(CardData cardData, GameObject cardObj)
    {
        PlayerHand local = ResolveLocalHand();
        if (local != null && local != this)
        {
            local.OnLocalPlayerPlayedCard(cardData, cardObj);
            return;
        }

        if (isHiddenCardActive && !isTrumpRevealed
            && cardData.cardSuit == hiddenTrumpCard.cardSuit && cardData.cardRank == hiddenTrumpCard.cardRank)
        {
            EnsureHandListsInitialized();
            int matchingCardsLeft = myCards.Count(c =>
                c.cardSuit == cardData.cardSuit && c.cardRank == cardData.cardRank);

            if (matchingCardsLeft == 1 && myCards.Count > 1)
            {
                Debug.LogWarning("[Hidden Trump] Hidden patta abhi nahi khel sakte!");
                AbortLocalCardPlay(cardObj);            // un-stick: keep the card live
                RefreshHandUI(false, true);
                ApplyRules(true);                        // re-raise valid cards (still my turn)
                return;
            }
        }

        int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (currentTrick != null && currentTrick.Any(c => c.actorNumber == localActor))
        {
            AbortLocalCardPlay(cardObj);                 // already played this trick — un-stick the card
            return;
        }
        if (!CanAcceptCardPlay(localActor, (int)cardData.cardSuit, (int)cardData.cardRank))
        {
            AbortLocalCardPlay(cardObj);
            // Re-apply rules so the card re-raises if it is still our turn.
            ApplyRules(PhotonNetwork.LocalPlayer.ActorNumber == currentTurnActor);
            return;
        }
        if (!IsCardLegalPlay(myCards, currentTrick, cardData, localActor))
        {
            Debug.LogWarning("[Play] Blocked hidden or illegal card.");
            AbortLocalCardPlay(cardObj);
            ApplyRules(true);
            return;
        }

        CardInteract.isPlayingCard = true;
        EndTurnCardVisuals();

        _hasPendingLocalPlayStart = cardObj != null;
        if (cardObj != null) _pendingLocalPlayStartWorldPos = cardObj.transform.position;
        _pendingLocalPlaySuitIndex = (int)cardData.cardSuit;
        _pendingLocalPlayRankIndex = (int)cardData.cardRank;
        _awaitingOwnPlayRpc = true;

        Destroy(cardObj);
        RemoveOneCardFromHand(myCards, cardData.cardSuit, cardData.cardRank);
        RefreshHandUI(false, true);
        if (photonView != null)
            photonView.RPC("RPC_PlayCard", RpcTarget.All, localActor, (int)cardData.cardSuit, (int)cardData.cardRank);

        StartUnlockPlayFailsafe();
    }

    /// <summary>
    /// Un-sticks a card whose local play attempt was rejected/aborted. CardInteract.PlayThisCard()
    /// optimistically sets isPlayed = true BEFORE the play is validated; if any guard rejects the
    /// play we must clear that flag again. Otherwise the card stays permanently dim &amp; non-playable
    /// because BOTH RefreshHandUI's destroy loop AND HighlightPlayableCards skip isPlayed cards —
    /// this is the "first tapped card won't raise / is non-playable" bug.
    /// </summary>
    void AbortLocalCardPlay(GameObject cardObj)
    {
        CardInteract.isPlayingCard = false;

        if (cardObj != null)
        {
            CardInteract ci = cardObj.GetComponent<CardInteract>()
                ?? cardObj.GetComponentInChildren<CardInteract>()
                ?? cardObj.GetComponentInParent<CardInteract>();
            if (ci != null)
            {
                ci.isPlayed = false;        // critical: return the card to a live, playable state
                ci.isValidToPlay = false;   // re-evaluated by ApplyRules / HighlightPlayableCards
            }
        }

        CardInteract.ClearGlobalSelection();
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
        PlayerHand handler = ResolveLocalHand();
        if (handler == null) return;
        if (handler != this)
        {
            handler.RPC_PlayCard(senderActorNum, suitIndex, rankIndex, info);
            return;
        }

        if (DeckManager.Instance != null && DeckManager.Instance.IsActorBotControlled(senderActorNum))
        {
            if (info.Sender != null && !info.Sender.IsMasterClient)
                return;
        }

        bool actorAlreadyPlayed = currentTrick != null && currentTrick.Any(c => c.actorNumber == senderActorNum);

        bool isLocalSender = PhotonNetwork.LocalPlayer != null
            && senderActorNum == PhotonNetwork.LocalPlayer.ActorNumber;

        if (isLocalSender)
        {
            StopUnlockPlayFailsafe();
            CardInteract.isPlayingCard = false;
        }

        if (actorAlreadyPlayed) return;
        if (!CanAcceptCardPlay(senderActorNum, suitIndex, rankIndex))
        {
            if (isLocalSender)
            {
                _awaitingOwnPlayRpc = false;
                _hasPendingLocalPlayStart = false;
            }
            return;
        }

        if (isLocalSender)
            _awaitingOwnPlayRpc = false;

        CardData playedCard = new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex };

        LockTrickPlayInput();

        if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
            DeckManager.Instance.UpdateCachedHandOnMaster(senderActorNum, (CardSuit)suitIndex, (CardRank)rankIndex);

        int seat = GetSeatIndex(senderActorNum);
        Transform center = GetTableCenterTransform();
        
        GameObject cardObj = Object.Instantiate(cardUIPrefab, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(playedCard);

        Vector3 startPos = GetPlayerPositionForSeat(seat);
        if (isLocalSender && _hasPendingLocalPlayStart)
        {
            startPos = _pendingLocalPlayStartWorldPos;
            _hasPendingLocalPlayStart = false;
        }

        RectTransform cardRt = cardObj.GetComponent<RectTransform>();
        if (cardRt != null)
        {
            cardRt.SetAsLastSibling();
            if (center is RectTransform centerRt)
            {
                Vector3 localStart = centerRt.InverseTransformPoint(startPos);
                cardRt.localPosition = localStart;
            }
            else
                cardObj.transform.position = startPos;

            cardRt.localScale = cardUIPrefab != null
                ? cardUIPrefab.transform.localScale * centerCardScale
                : Vector3.one * centerCardScale;
            Vector3 targetLocal = GetFinalPositionForSeat(seat);
            cardRt.DOLocalMove(targetLocal, centerCardMoveDuration).SetEase(Ease.InOutSine).SetUpdate(true);
            cardRt.DOLocalRotate(Vector3.zero, centerCardMoveDuration).SetEase(Ease.InOutSine).SetUpdate(true);
        }
        else
        {
            cardObj.transform.position = startPos;
            cardObj.transform.localScale = cardUIPrefab != null
                ? cardUIPrefab.transform.localScale * centerCardScale
                : Vector3.one * centerCardScale;
            Vector3 targetLocal = GetFinalPositionForSeat(seat);
            cardObj.transform.DOLocalMove(targetLocal, centerCardMoveDuration).SetEase(Ease.InOutSine).SetUpdate(true);
            cardObj.transform.DORotate(Vector3.zero, centerCardMoveDuration).SetEase(Ease.InOutSine).SetUpdate(true);
        }

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

        // Update turn actor immediately so the winner can play while we clean the table
        if (PhotonNetwork.IsMasterClient)
            currentTurnActor = winnerActor;
        else
            _localCurrentTurnActor = winnerActor;

        bool isLastTrickOfGame = PhotonNetwork.IsMasterClient && totalTricksPlayed + 1 >= tricksToWin;

        isCleaningTable = true;
        HandleTrickWinner(winnerActor, trickSnapshot, isLastTrickOfGame);

        yield return new WaitForSeconds(0.5f);

        foreach (TrickCard tc in trickSnapshot)
            tc.cardObject = null;

        ClearAllTableCardClones(trickSnapshot);
        
        // Remove only the cards that were part of the finished trick
        foreach (var tc in trickSnapshot)
            currentTrick.Remove(tc);

        ClearTrickPlayLocks();
        _lastProcessTurnActor = -1;
        _lastProcessTurnTrickCount = -1;
        // FIX: reset the de-dupe guard so ProcessTurn(winnerActor) below is not swallowed
        // on non-master clients when the winner was also the last actor to play in the trick.
        // Previously this left the winner's hand permanently disabled until the 15s auto-play fired.
        _lastHandledTurnActor = -1;

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
            // Use a locally computed value for the end-of-game check.
            // Re-reading totalTricksPlayed here would read the Photon room property "TP",
            // which is updated asynchronously (server round-trip) and would still hold the
            // previous value online — causing the final trick check to fail and the
            // leaderboard to never appear in online multiplayer.
            int newTrickCount = _localTotalTricksPlayed + 1;
            totalTricksPlayed = newTrickCount;

            if (newTrickCount >= tricksToWin)
            {
                botActorsThinking.Clear();
                CardInteract.canPlayCards = false;
                Debug.Log($"[PlayerHand] Round complete (trick {newTrickCount}/{tricksToWin}).");
                if (ResultManager.Instance != null)
                    ResultManager.Instance.TriggerRoundCompletedFromMaster();
                else if (photonView != null)
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

    // Tasks 23/26: progressive deal reveal. dealRevealLimit caps how many of the local player's
    // (sorted) cards are currently rendered; -1 means render all. dealAnimateFromIndex animates
    // only the newly revealed cards in a batch (earlier cards snap to their final position so the
    // hand fills in left-to-right without re-running the whole deal animation = no flicker).
    private int dealRevealLimit = -1;
    private int dealAnimateFromIndex = 0;
    // Task 23: identities (suit+rank) of cards whose deal fly-in has already played this round.
    // Cards are destroyed/re-instantiated on every RefreshHandUI, so a per-instance flag cannot
    // survive — this hand-level set guarantees the deal Tween runs strictly once per card.
    private readonly System.Collections.Generic.HashSet<string> _dealtCardKeys = new System.Collections.Generic.HashSet<string>();

    public void PlayDealAnimationOnly(int cardsInBatch, int revealUpTo)
    {
        if (isDealingComplete) return;

        GameFlowState.SetPhase(GameFlowPhase.Dealing, forceRecovery: true);
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;
        botActorsThinking.Clear();
        // Only clear the hand before the first batch (nothing revealed yet). Later batches keep
        // the already-dealt cards visible and append the new ones — no full re-deal / flicker.
        if (dealRevealLimit <= 0 && handAreaTransform != null)
            ClearHandUI();
        StartCoroutine(DealAnimationOnlyRoutine(cardsInBatch, revealUpTo));
    }

    public const float DealFlyDuration = 0.2f;
    public const float DealFlyDestroyDelay = 0.22f;
    public const float DealCardLaunchGap = 0.04f;
    public const float DealShrinkDuration = 0.2f;
    public const float DealPacketCardSpread = 12f;
    public const float DealSeatSettlePause = 0.12f;
    public const float DealRoundSettlePause = 0.12f;

    public static float GetDealBatchDuration(int cardsInBatch)
    {
        // Real per-seat runtime of DealAnimationOnlyRoutine: cardsInBatch launch-gaps + the 0.2s
        // per-seat settle pause; final 0.2s tail at the end. This matches the actual animation
        // so callers can add a precise inter-batch gap on top (see DeckManager FullDealingSequenceRoutine).
        float perSeat = cardsInBatch * DealCardLaunchGap + DealSeatSettlePause;
        return 4f * perSeat + DealRoundSettlePause;
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

    Transform GetDealAnimationParent()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            return NetworkManager.Instance.gameCanvasGroup.transform;

        EnsureGameplayUiRefs();
        if (gameUiSearchRoot != null)
            return gameUiSearchRoot;

        return canvasTransform;
    }

    void PlaceDealCardBehindOverlays(Transform cardTransform)
    {
        Transform parent = cardTransform != null ? cardTransform.parent : null;
        if (parent == null) return;

        int targetIndex = parent.childCount - 1;

        if (InGameSettingsController.Instance != null && InGameSettingsController.Instance.settingsPanel != null)
        {
            Transform settings = InGameSettingsController.Instance.settingsPanel.transform;
            if (settings.parent == parent)
                targetIndex = Mathf.Min(targetIndex, settings.GetSiblingIndex());
        }
        else if (UiSafeLookup.TryGet("Panel_GameSettings", out GameObject settingsGo)
                 && settingsGo.transform.parent == parent)
        {
            targetIndex = Mathf.Min(targetIndex, settingsGo.transform.GetSiblingIndex());
        }

        cardTransform.SetSiblingIndex(targetIndex);
    }

    IEnumerator DealAnimationOnlyRoutine(int cardsInBatch, int revealUpTo)
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

        Transform dealParent = GetDealAnimationParent();
        if (dealParent == null)
        {
            _isDealAnimRunning = false;
            IsDealAnimationRunning = false;
            yield break;
        }

        RectTransform canvasRect = dealParent as RectTransform;
        if (canvasRect == null)
            canvasRect = dealParent.GetComponent<RectTransform>();

        Vector2 deckAnchor = GetAnchorInCanvas(canvasRect, centerPos != null ? centerPos.position : dealParent.position);

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
                GameObject flyingCard = Object.Instantiate(dummyCardPrefab, dealParent);
                CardDisplay flyingCardDisplay = flyingCard.GetComponent<CardDisplay>();
                if (flyingCardDisplay != null)
                    flyingCardDisplay.SetHiddenState(true);
                else
                {
                    Image flyingImage = flyingCard.GetComponent<Image>();
                    if (flyingImage != null && GameManager.Instance != null && GameManager.Instance.cardBackSprite != null)
                        flyingImage.sprite = GameManager.Instance.cardBackSprite;
                }
                PlaceDealCardBehindOverlays(flyingCard.transform);
                RectTransform cardRt = flyingCard.GetComponent<RectTransform>();
                cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);

                float spreadOffset = (i - (cardsInBatch - 1) * 0.5f) * DealPacketCardSpread;
                cardRt.anchoredPosition = deckAnchor + new Vector2(spreadOffset * 0.35f, 0f);

                Vector2 target = midwayAnchor + new Vector2(spreadOffset * 0.5f, 0f);

                cardRt.DOAnchorPos(target, DealFlyDuration).SetEase(Ease.OutCubic);
                cardRt.DOScale(new Vector3(0.5f, 0.5f, 1f), DealShrinkDuration).SetEase(Ease.OutBack);
                Object.Destroy(flyingCard, DealFlyDestroyDelay);

                yield return new WaitForSeconds(DealCardLaunchGap);
            }

            yield return new WaitForSeconds(DealSeatSettlePause);
        }

        yield return new WaitForSeconds(DealRoundSettlePause);

        // Task 26: progressively reveal the local player's hand as each batch is dealt — the
        // newly dealt cards fan in horizontally beside the previously dealt ones.
        if (revealUpTo > 0 && !isDealingComplete)
        {
            int prevRevealed = dealRevealLimit < 0 ? 0 : dealRevealLimit;
            dealRevealLimit = revealUpTo;
            dealAnimateFromIndex = prevRevealed;
            // force:true so the reveal runs even though the deal animation flag is still set.
            RefreshHandUI(animate: true, force: true);
            dealAnimateFromIndex = 0;
        }

        _isDealAnimRunning = false;
        IsDealAnimationRunning = false;
    }

    static readonly Dictionary<CardSuit, int> SingleDeckSuitWeights = new Dictionary<CardSuit, int>
    {
        { CardSuit.Spades, 1 },
        { CardSuit.Hearts, 2 },
        { CardSuit.Clubs, 3 },
        { CardSuit.Diamonds, 4 }
    };

    /// <summary>
    /// Sorts a player hand in-place. Single-deck (1 Taash): Spades, Hearts, Clubs, Diamonds.
    /// Double-deck (2 Taash): preserves the legacy suit order (Hearts, Clubs, Spades, Diamonds).
    /// Within each suit, rank is descending (Ace high).
    /// </summary>
    public static void SortPlayerHand(List<CardData> playerHand, bool isSingleDeckMode)
    {
        if (playerHand == null || playerHand.Count <= 1) return;

        if (isSingleDeckMode)
        {
            playerHand.Sort((a, b) =>
            {
                int suitCmp = SingleDeckSuitWeights[a.cardSuit].CompareTo(SingleDeckSuitWeights[b.cardSuit]);
                return suitCmp != 0 ? suitCmp : CompareRankDescending(a.cardRank, b.cardRank);
            });
        }
        else
        {
            playerHand.Sort((a, b) =>
            {
                int suitCmp = LegacyTwoTaashSuitOrder(a.cardSuit).CompareTo(LegacyTwoTaashSuitOrder(b.cardSuit));
                return suitCmp != 0 ? suitCmp : CompareRankDescending(a.cardRank, b.cardRank);
            });
        }
    }

    static int LegacyTwoTaashSuitOrder(CardSuit suit)
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

    static int CompareRankDescending(CardRank a, CardRank b) => -((int)a).CompareTo((int)b);

    private int SuitOrder(CardSuit suit) => LegacyTwoTaashSuitOrder(suit);

    private int RankOrder(CardRank rank) => -((int)rank);

    public void RefreshHandUI(bool animate = true, bool force = false)
    {
        EnsureHandListsInitialized();
        if (handAreaTransform == null) return;
        if (!force && (IsDealAnimationRunning || !isDealingComplete)) return;

        UnityEngine.UI.LayoutGroup lg = handAreaTransform.GetComponent<UnityEngine.UI.LayoutGroup>();
        if (lg != null) lg.enabled = false;

        for (int i = handAreaTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = handAreaTransform.GetChild(i);
            CardInteract interact = child.GetComponent<CardInteract>();
            if (interact != null && interact.isPlayed) continue;
            child.DOKill();
            Object.DestroyImmediate(child.gameObject);
        }

        // Hidden trump owner: keep hidden card at the end until unlocked; then sort normally.
        bool isLocalHiddenOwner = isHiddenCardActive
            && PhotonNetwork.LocalPlayer != null
            && PhotonNetwork.LocalPlayer.ActorNumber == hiddenCardOwnerActor;

        List<CardData> sortedCards;
        if (isLocalHiddenOwner && !isTrumpRevealed)
        {
            sortedCards = new List<CardData>();
            CardData? pinnedHidden = null;

            foreach (CardData c in myCards)
            {
                if (!pinnedHidden.HasValue
                    && c.cardSuit == hiddenTrumpCard.cardSuit
                    && c.cardRank == hiddenTrumpCard.cardRank)
                {
                    pinnedHidden = c;
                }
                else
                {
                    sortedCards.Add(c);
                }
            }

            SortPlayerHand(sortedCards, !TaashRules.IsTwoTaashMode);
            if (pinnedHidden.HasValue)
                sortedCards.Add(pinnedHidden.Value);
        }
        else
        {
            sortedCards = new List<CardData>(myCards);
            SortPlayerHand(sortedCards, !TaashRules.IsTwoTaashMode);
        }

        myCards = sortedCards;

        bool isTwoRows = TaashRules.IsTwoTaashMode && myCards.Count > HandLayoutHelper.CardsPerRow;
        const int cardsPerRow = HandLayoutHelper.CardsPerRow;
        float handWidthPx = HandLayoutHelper.GetHandAreaWidth(handAreaTransform as RectTransform);
        float prefabWidth = HandLayoutHelper.GetPrefabCardWidth(cardUIPrefab);
        bool hiddenCardUIProcessed = false;
        bool revealedHiddenProcessed = false;

        HandLayoutConfig row0Layout = HandLayoutHelper.GetLayout(
            isTwoRows ? Mathf.Min(cardsPerRow, myCards.Count) : myCards.Count,
            handWidthPx,
            prefabWidth);
        HandLayoutConfig row1Layout = default;
        if (isTwoRows)
        {
            int row1Count = Mathf.Max(0, myCards.Count - cardsPerRow);
            row1Layout = HandLayoutHelper.GetLayout(row1Count, handWidthPx, prefabWidth);
        }

        // Task 26: during the initial deal, only render the cards dealt so far. Layout positions
        // are still computed from the full hand count below, so revealed cards keep their final
        // slots and newly dealt cards simply appear in the next free slots (left-to-right fan).
        int renderCount = myCards.Count;
        if (dealRevealLimit >= 0 && dealRevealLimit < myCards.Count)
            renderCount = dealRevealLimit;

        for (int i = 0; i < renderCount; i++)
        {
            GameObject newCardUI = Object.Instantiate(cardUIPrefab, handAreaTransform);
            CardDisplay display = newCardUI.GetComponent<CardDisplay>();
            if (display != null) display.SetCardData(myCards[i]);

            // Sirf EK patte ko UI mein grey aur hidden karna hai
            bool isThisCardHidden = false;
            if (isLocalHiddenOwner && !isTrumpRevealed && !hiddenCardUIProcessed
                && myCards[i].cardSuit == hiddenTrumpCard.cardSuit
                && myCards[i].cardRank == hiddenTrumpCard.cardRank
                && i == myCards.Count - 1)
            {
                isThisCardHidden = true;
                hiddenCardUIProcessed = true;
            }

            bool isRevealedHiddenCard = false;
            if (isHiddenCardActive && isTrumpRevealed && !revealedHiddenProcessed
                && PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == hiddenCardOwnerActor
                && myCards[i].cardSuit == hiddenTrumpCard.cardSuit
                && myCards[i].cardRank == hiddenTrumpCard.cardRank)
            {
                isRevealedHiddenCard = true;
                revealedHiddenProcessed = true;
            }

            if (display != null)
            {
                if (isThisCardHidden)
                    display.SetHiddenState(true);
                else if (isRevealedHiddenCard)
                {
                    display.SetHiddenState(false);
                    if (display.cardBackgroundImage != null)
                        display.cardBackgroundImage.color = new Color(1.0f, 0.95f, 0.82f, 1.0f);
                }
                else
                {
                    display.SetHiddenState(false);
                    if (display.cardBackgroundImage != null)
                        display.cardBackgroundImage.color = Color.white;
                }
            }

            CardInteract cardInteract = newCardUI.GetComponentInChildren<CardInteract>();
            if (cardInteract != null && isThisCardHidden)
            {
                cardInteract.isValidToPlay = false;
                cardInteract.ApplyBlockedOnTurnVisual();
            }

            RectTransform rt = newCardUI.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;

            int row = i / cardsPerRow;
            int col = i % cardsPerRow;
            int cardsInThisRow = Mathf.Min(cardsPerRow, myCards.Count - row * cardsPerRow);

            HandLayoutConfig layout = row == 0 ? row0Layout : row1Layout;
            float startX = HandLayoutHelper.ComputeStartX(layout, cardsInThisRow);
            float xPos = startX + col * (layout.prefabCardWidth + layout.spacing);
            float yPos = HandLayoutHelper.GetRowY(row, isTwoRows);

            rt.anchoredPosition = new Vector2(xPos, yPos);

            // Task 23: each card's deal fly-in runs strictly once. Track by card identity
            // (suit+rank) so a later RefreshHandUI cannot replay the Tween (the flicker bug).
            string dealKey = ((int)myCards[i].cardSuit) + "_" + ((int)myCards[i].cardRank);
            bool alreadyDealt = _dealtCardKeys.Contains(dealKey);

            if (animate && i >= dealAnimateFromIndex && !alreadyDealt)
            {
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, yPos - 100f);
                int animOrder = i - dealAnimateFromIndex;
                rt.DOAnchorPosY(yPos, 0.2f).SetEase(Ease.OutBack).SetDelay(animOrder * 0.015f).SetUpdate(true);
                _dealtCardKeys.Add(dealKey);
                if (cardInteract != null) cardInteract.isDealt = true;
            }
            else
            {
                // Already dealt, or a non-animated render: card is already at its final slot —
                // no replay. Keep it marked dealt so future refreshes never re-animate it.
                _dealtCardKeys.Add(dealKey);
                if (cardInteract != null) cardInteract.isDealt = true;
            }

            if (cardInteract != null)
                cardInteract.ResetVisualOffset();
        }
    }

    public void AssignFullHandLocal(int targetActor, int[] suitIndices, int[] rankIndices)
    {
        if (PhotonNetwork.LocalPlayer.ActorNumber != targetActor) return;
        myCards.Clear();
        if (suitIndices == null || rankIndices == null || suitIndices.Length != rankIndices.Length) return;
        for (int i = 0; i < suitIndices.Length; i++)
            myCards.Add(new CardData { cardSuit = (CardSuit)suitIndices[i], cardRank = (CardRank)rankIndices[i] });

        if (!isDealingComplete)
        {
            // Fresh deal: the full hand is assigned up-front but kept hidden. The per-batch deal
            // animation reveals the cards progressively (Task 26) and prevents the double-deal
            // flicker (Task 23). Render 0 cards now so the hand area starts empty.
            dealRevealLimit = 0;
            dealAnimateFromIndex = 0;
            if (!IsDealAnimationRunning && !_isDealAnimRunning)
                RefreshHandUI(animate: false, force: true);
            return;
        }

        if (!IsDealAnimationRunning && !_isDealAnimRunning)
            RefreshHandUI(animate: false, force: true);
    }

    public void OnDealingComplete(int starterActor)
    {
        if (LocalInstance != null && LocalInstance != this) { LocalInstance.OnDealingComplete(starterActor); return; }

        // Reconnect / re-sync path: dealing was already marked complete (e.g. RPC_SyncGameState
        // set the flag before the hand-restore RPC arrived). Don't replay the deal animation —
        // just rebuild turn ownership and re-enable card input against the restored hand so the
        // reconnecting player can interact with their cards again. Without this the player gets
        // stuck with every card un-playable (isValidToPlay == false) and the game looks frozen.
        if (isDealingComplete)
        {
            ResumePlayAfterReconnect(starterActor);
            return;
        }

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

    /// <summary>
    /// Re-enables gameplay for the local player after a reconnect when dealing was already
    /// complete. Rebuilds the seat order, restores the current turn actor, refreshes the hand
    /// UI and re-applies per-card play rules so the player can play cards again. Idempotent —
    /// safe to call multiple times as restore RPCs arrive in any order.
    /// </summary>
    public void ResumePlayAfterReconnect(int starterActor)
    {
        if (GameFlowState.Current != GameFlowPhase.InGame)
            GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);

        BuildTableTurnOrder();
        currentTurnActor = starterActor;

        if (!IsDealAnimationRunning && !_isDealAnimRunning)
            RefreshHandUI(animate: false, force: true);

        bool isMyTurn = PhotonNetwork.LocalPlayer.ActorNumber == starterActor;
        CardInteract.isPlayingCard = false;
        CardInteract.canPlayCards = isMyTurn;
        ApplyRules(isMyTurn);

        // Master must still drive bot turns / the turn timer for the active actor.
        if (PhotonNetwork.IsMasterClient)
            ProcessTurn(starterActor);

        Debug.Log($"[Reconnect] Resumed play after rejoin. Starter actor: {starterActor}, My turn: {isMyTurn}");
    }

    /// <summary>
    /// Final reconnect step: read authoritative turn from room props and resume local play.
    /// Safe to call after hand/table restore RPCs or room property sync.
    /// </summary>
    public void FinishReconnectFromRoom()
    {
        int starterActor = currentTurnActor;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CTA", out object cta))
            starterActor = (int)cta;

        if (starterActor < 0)
            return;

        OnDealingComplete(starterActor);
    }

    IEnumerator HandRevealThenStartGame(int starterActor, float turnDelay, bool matchInProgress)
    {
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;

        while (IsDealAnimationRunning || _isDealAnimRunning)
            yield return null;

        isDealingComplete = true;

        // The hand was already revealed progressively per deal batch. Just ensure the full hand
        // is shown (snap, no animation) so we don't replay the whole deal animation = no flicker.
        dealRevealLimit = -1;
        dealAnimateFromIndex = 0;
        RefreshHandUI(animate: false, force: true);

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

    [PunRPC]
    void RPC_ShowGameResult()
    {
        ShowGameResultLocal();
    }

    public void ShowGameResultLocal()
    {
        if (_resultPanelShown)
        {
            Debug.Log("[PlayerHand] ShowGameResultLocal skipped — result already shown this game.");
            return;
        }
        _resultPanelShown = true;

        botActorsThinking.Clear();
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = true;
        if (TurnManager.Instance != null) TurnManager.Instance.StopTimer();

        GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);

        if (ResultManager.Instance != null)
        {
            Debug.Log("[PlayerHand] Showing result/leaderboard panel.");
            ResultManager.Instance.ShowResult();
        }
        else
        {
            Debug.LogError("[PlayerHand] ResultManager.Instance is null — cannot show leaderboard panel! Ensure a ResultManager exists in the scene.");
        }
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
