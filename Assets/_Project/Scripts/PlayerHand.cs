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
            // Master is authoritative — room CTA can lag behind SetCustomProperties.
            if (PhotonNetwork.IsMasterClient && _localCurrentTurnActor >= 0)
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
    private readonly HashSet<long> botRetryKeys = new HashSet<long>();

    static long BotRetryKey(int actorNumber, int trickCount) =>
        ((long)actorNumber << 32) | (uint)trickCount;

    bool ActorInCurrentTrick(int actorNumber) =>
        currentTrick != null && currentTrick.Exists(c => c.actorNumber == actorNumber);

    void ClearTrickPlayLocks()
    {
        actorsPlayedThisTrick.Clear();
        botActorsThinking.Clear();
        botRetryKeys.Clear();
    }

    public void RestoreTableCardsFromRoom()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TC", out object tcObj))
        {
            int[] interleaved = (int[])tcObj;
            
            Transform tableCenter = GetTableCenterTransform();
            if (tableCenter != null)
            {
                foreach (Transform child in tableCenter)
                {
                    if (child.gameObject.name.Contains("(Clone)")) Object.Destroy(child.gameObject);
                }
            }
            currentTrick.Clear();

            for (int i = 0; i < interleaved.Length / 3; i++)
            {
                int actor = interleaved[i * 3];
                int suit = interleaved[i * 3 + 1];
                int rank = interleaved[i * 3 + 2];
                SpawnCardOnTableLocal(actor, suit, rank);
            }

            if (isDealingComplete)
            {
                ApplyRules(PhotonNetwork.LocalPlayer.ActorNumber == currentTurnActor);
            }
        }
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
        int seatIndex = GetSeatIndex(senderActorNum);
        Transform center = GetTableCenterTransform();
        GameObject cardObj = Object.Instantiate(cardUIPrefab, GetSeatSpawnPosition(seatIndex), Quaternion.identity, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex });
        Vector3 offsetPos = seatIndex == 0 ? new Vector3(0, -120f, 0) : seatIndex == 1 ? new Vector3(-180f, 0, 0) : seatIndex == 2 ? new Vector3(0, 120f, 0) : new Vector3(180f, 0, 0);
        
        cardObj.transform.localPosition = offsetPos;
        cardObj.transform.localRotation = Quaternion.identity;

        currentTrick.Add(new TrickCard { actorNumber = senderActorNum, suit = (CardSuit)suitIndex, rankValue = rankIndex, cardObject = cardObj });
    }

    public List<TrickCard> currentTrick = new List<TrickCard>();

    public static bool isResolvingTrick;
    public static bool isCleaningTable;
    public static bool IsTrickLocked => isResolvingTrick || isCleaningTable;

    static bool _handRevealRunning;
    public static bool IsGameplayInputBlocked =>
        IsDealAnimationRunning || _handRevealRunning || IsTrickLocked ||
        CardInteract.isPlayingCard || GameFlowState.Current == GameFlowPhase.GameFinished;

    private int _localCurrentTurnActor = -1;
    private bool _determineTrickRoutineRunning;
    private Coroutine _determineTrickCoroutine;

    private int lastTrickWinnerActor = -1;
    private bool isDealingComplete = false;
    private int _cutsInMatch = 0;
    private bool cut1TrumpAlreadySet = false;
    private static bool _resultPanelShown = false;
    private readonly List<int> tableTurnOrder = new List<int>(4);
    private readonly List<GameObject> opponentBackCards = new List<GameObject>();

    private void HandleTrumpModeAfterCardAdded(CardData playedCard, int actorNumber)
    {
        if (GameSettings.Instance == null) return;
        if (currentTrick == null) return;
        if (currentTrick.Count < 2) return;

        GameModeType mode = GameSettings.Instance.currentMode;
        CardSuit ledSuit = currentTrick[0].suit;

        bool isCut = playedCard.cardSuit != ledSuit;
        if (!isCut) return;

        if (mode == GameModeType.Cut1Trump)
        {
            if (cut1TrumpAlreadySet) return;
            cut1TrumpAlreadySet = true;
            if (PhotonNetwork.IsMasterClient && TrumpManager.Instance != null)
                TrumpManager.Instance.SetTrumpSuit(playedCard.cardSuit, true, true);
            PlayerHand.currentTrumpSuit = playedCard.cardSuit;
            PlayerHand.isTrumpRevealed = true;
        }
        else if (mode == GameModeType.Cut2Trump)
        {
            if (PhotonNetwork.IsMasterClient && TrumpManager.Instance != null)
                TrumpManager.Instance.SetTrumpSuit(playedCard.cardSuit, true, true);
            PlayerHand.currentTrumpSuit = playedCard.cardSuit;
            PlayerHand.isTrumpRevealed = true;
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
        foreach (Transform child in handAreaTransform) { child.DOKill(); Object.Destroy(child.gameObject); }
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
        _determineTrickRoutineRunning = false;
        isResolvingTrick = false;
        isCleaningTable = false;
        myCards.Clear();
        _cutsInMatch = 0;
        cut1TrumpAlreadySet = false;
        totalTricksPlayed = 0;
ClearAllTableCardClones();
        currentTrick.Clear();
        lastTrickWinnerActor = -1;
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
        botRetryKeys.Clear();
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

    void ClearAllTableCardClones()
    {
        Transform tableCenter = GetTableCenterTransform();
        if (tableCenter == null) return;

        foreach (Transform child in tableCenter)
        {
            if (!child.gameObject.name.Contains("(Clone)")) continue;
            child.DOKill();
            Object.Destroy(child.gameObject);
        }
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

    void ProcessTurn(int actorNumber)
    {
        if (!IsDealingReadyForPlay()) return;
        if (IsTrickLocked || _determineTrickRoutineRunning) return;
        if (GameFlowState.Current != GameFlowPhase.InGame && GameFlowState.Current != GameFlowPhase.InRoom) return;

        int trickCount = currentTrick?.Count ?? 0;

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

        if (PhotonNetwork.IsMasterClient) currentTurnActor = actorNumber;

        if (TurnManager.Instance != null && PhotonNetwork.IsMasterClient)
            TurnManager.Instance.StartTurn(actorNumber);

        bool isMyTurn = (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber);
        CardInteract.canPlayCards = isMyTurn && !IsGameplayInputBlocked && !ActorInCurrentTrick(actorNumber) && !actorsPlayedThisTrick.Contains(actorNumber);
        CardInteract.isPlayingCard = false;

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

    void PlayBotCard(int actorNumber, CardData card)
    {
        if (ActorInCurrentTrick(actorNumber)) return;
        if (photonView == null) return;
        photonView.RPC("RPC_PlayCard", RpcTarget.All, actorNumber, (int)card.cardSuit, (int)card.cardRank);
    }

    IEnumerator BotPlayRoutine(int actorNumber)
    {
        yield return new WaitForSeconds(0.8f);

        bool cardActuallyPlayed = false;
        try
        {
            if (actorNumber != GetAuthoritativeTurnActor() || IsTrickLocked || ActorInCurrentTrick(actorNumber) || actorsPlayedThisTrick.Contains(actorNumber))
                yield break;

            if (DehlaPakadAI.Instance == null || DeckManager.Instance == null) yield break;

            if (!DeckManager.Instance.botHands.TryGetValue(actorNumber, out List<CardData> hand) || hand == null || hand.Count == 0) yield break;

            CardData botCard = DehlaPakadAI.Instance.ThinkAndSelectCard(hand, currentTrick, currentTrumpSuit, isTrumpRevealed, actorNumber);
            PlayBotCard(actorNumber, botCard);
            cardActuallyPlayed = true;
        }
        finally
        {
            botActorsThinking.Remove(actorNumber);

            if (!cardActuallyPlayed && PhotonNetwork.IsMasterClient && IsBotActor(actorNumber)
                && actorNumber == GetAuthoritativeTurnActor()
                && !ActorInCurrentTrick(actorNumber) && !IsTrickLocked && !_determineTrickRoutineRunning)
            {
                long retryKey = BotRetryKey(actorNumber, currentTrick?.Count ?? 0);
                if (botRetryKeys.Add(retryKey))
                    TriggerBotTurnIfApplicable(actorNumber);
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
        if (IsTrickLocked || !isMyTurn)
        {
            EndTurnCardVisuals();
            return;
        }
        CardInteract.ClearGlobalSelection();
        HighlightPlayableCards();
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
        RefreshHandUI(animate: false);
        if (photonView != null) photonView.RPC("RPC_PlayCard", RpcTarget.All, PhotonNetwork.LocalPlayer.ActorNumber, (int)cardData.cardSuit, (int)cardData.cardRank);
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
        if (actorAlreadyPlayed)
        {
            if (PhotonNetwork.LocalPlayer != null && senderActorNum == PhotonNetwork.LocalPlayer.ActorNumber)
                _hasPendingLocalPlayStart = false;
            return;
        }

        if (!CanAcceptCardPlay(senderActorNum, suitIndex, rankIndex))
        {
            if (PhotonNetwork.LocalPlayer != null && senderActorNum == PhotonNetwork.LocalPlayer.ActorNumber)
                _hasPendingLocalPlayStart = false;
            return;
        }

        CardData playedCard = new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex };

        LockTrickPlayInput();

        if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
        {
            DeckManager.Instance.UpdateCachedHandOnMaster(senderActorNum, (CardSuit)suitIndex, (CardRank)rankIndex);
            if (IsBotActor(senderActorNum) && DeckManager.Instance.botHands.TryGetValue(senderActorNum, out List<CardData> botHand))
                RemoveOneCardFromHand(botHand, (CardSuit)suitIndex, (CardRank)rankIndex);
        }

        int seatIndex = GetSeatIndex(senderActorNum);
        Transform center = GetTableCenterTransform();
        GameObject cardObj = Object.Instantiate(cardUIPrefab, GetSeatSpawnPosition(seatIndex), Quaternion.identity, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(playedCard);

        if (PhotonNetwork.LocalPlayer != null && senderActorNum == PhotonNetwork.LocalPlayer.ActorNumber && _hasPendingLocalPlayStart && suitIndex == _pendingLocalPlaySuitIndex && rankIndex == _pendingLocalPlayRankIndex)
        {
            cardObj.transform.position = _pendingLocalPlayStartWorldPos;
            _hasPendingLocalPlayStart = false;
        }

        Vector3 offsetPos = seatIndex == 0 ? new Vector3(0, -120f, 0) : seatIndex == 1 ? new Vector3(-180f, 0, 0) : seatIndex == 2 ? new Vector3(0, 120f, 0) : new Vector3(180f, 0, 0);
        cardObj.transform.DOLocalMove(offsetPos, 0.35f).SetEase(Ease.OutCubic);
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
        yield return new WaitForSeconds(1.5f);

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
        lastTrickWinnerActor = winnerCard.actorNumber;
        int winnerSeat = GetSeatIndex(winnerCard.actorNumber);
        if (ResultManager.Instance != null)
        {
            int dehlas = 0;
            foreach (TrickCard tc in trickSnapshot)
                if (tc.rankValue == (int)CardRank.Ten) dehlas++;
            ResultManager.Instance.OnTrickWon(winnerSeat, dehlas);
        }

        isCleaningTable = true;
        Transform winnerTransform = playerPositions != null && winnerSeat < playerPositions.Length
            ? playerPositions[winnerSeat]
            : transform;

        foreach (TrickCard tc in trickSnapshot)
        {
            if (tc.cardObject != null)
                tc.cardObject.transform.DOMove(winnerTransform.position, 0.4f).SetEase(Ease.InBack);
        }

        yield return new WaitForSeconds(0.5f);

        DestroyTrickCardObjects(trickSnapshot);
        ClearAllTableCardClones();
        currentTrick.Clear();
        ClearTrickPlayLocks();
        _lastProcessTurnActor = -1;
        _lastProcessTurnTrickCount = -1;

        isCleaningTable = false;
        isResolvingTrick = false;
        _determineTrickRoutineRunning = false;
        _determineTrickCoroutine = null;
        Debug.Log("[Trick] Cleanup complete");

        if (PhotonNetwork.IsMasterClient)
            SyncCurrentTrickToRoom();

        EndTurnCardVisuals();
        CardInteract.isPlayingCard = false;

        if (PhotonNetwork.IsMasterClient)
        {
            totalTricksPlayed++;
            Debug.Log($"[Trick] Count: {totalTricksPlayed}/{tricksToWin}");

            if (totalTricksPlayed >= tricksToWin)
            {
                Debug.Log("Game Finished");
                GameFlowState.SetPhase(GameFlowPhase.GameFinished, forceRecovery: true);
                botActorsThinking.Clear();
                CardInteract.canPlayCards = false;
                if (photonView != null)
                    photonView.RPC("RPC_ShowGameResult", RpcTarget.All);
                else
                    ShowGameResultLocal();
                yield break;
            }

            GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
            Debug.Log($"[Trick] Winner actor {lastTrickWinnerActor} starts next trick.");
            ProcessTurn(lastTrickWinnerActor);
        }
        else
        {
            GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
        }
    }

    public int GetSeatIndex(int actorNum)
    {
        if (tableTurnOrder.Count < 4) BuildTableTurnOrder();
        int idx = tableTurnOrder.IndexOf(actorNum);
        return idx >= 0 ? idx : 0;
    }

    private bool _isDealAnimRunning = false;
    public static bool IsDealAnimationRunning { get; private set; }
    const float DealFlyTargetBlend = 0.5f;

    public void PlayDealAnimationOnly(int cardsInBatch)
    {
        Debug.Log($"[Deal] Started batch — {cardsInBatch} cards per seat");
        GameFlowState.SetPhase(GameFlowPhase.Dealing, forceRecovery: true);
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;
        botActorsThinking.Clear();
        if (!IsDealAnimationRunning && handAreaTransform != null)
            ClearHandUI();
        StartCoroutine(DealAnimationOnlyRoutine(cardsInBatch));
    }

    public const float DealFlyDuration = 0.25f;
    public const float DealCardLaunchGap = 0.03f;
    public const float DealPacketCardSpread = 12f;
    public const float DealSeatPause = 0.04f; // Reduced pause between players
    public const float DealRoundSettlePause = 0.04f; // Reduced pause between batches

    public static float GetDealBatchDuration(int cardsInBatch)
    {
        float perSeat = (cardsInBatch - 1) * DealCardLaunchGap + DealFlyDuration + 0.12f;
        return 4f * perSeat + DealRoundSettlePause + DealFlyDuration * 0.5f;
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
        if (seatIndex == 0 && handAreaTransform != null)
            return handAreaTransform;
        if (playerPositions != null && seatIndex >= 0 && seatIndex < playerPositions.Length && playerPositions[seatIndex] != null)
            return playerPositions[seatIndex];
        return null;
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
                cardRt.DOAnchorPos(target, DealFlyDuration).SetEase(Ease.Linear).OnComplete(() => {
                    if (flyingCard != null) Object.Destroy(flyingCard);
                });

                yield return new WaitForSeconds(DealCardLaunchGap);
            }

            yield return new WaitForSeconds(DealFlyDuration + 0.12f);
        }

        yield return new WaitForSeconds(DealFlyDuration * 0.5f);
        _isDealAnimRunning = false;
        IsDealAnimationRunning = false;
        Debug.Log("[Deal] Batch finished");
    }

    private int SuitOrder(CardSuit suit)
    {
        switch (suit)
        {
            case CardSuit.Spades: return 0;
            case CardSuit.Hearts: return 1;
            case CardSuit.Clubs: return 2;
            case CardSuit.Diamonds: return 3;
            default: return 99;
        }
    }

    private int RankOrder(CardRank rank)
    {
        return -(int)rank; // Ace highest if enum Two=0 ... Ace=12
    }

    void RefreshHandUI(bool animate = true)
    {
        if (handAreaTransform == null) return;
        if (IsDealAnimationRunning || !isDealingComplete)
            return;

        myCards = myCards.OrderBy(c => SuitOrder(c.cardSuit)).ThenBy(c => RankOrder(c.cardRank)).ToList();
        HorizontalLayoutGroup hlg = handAreaTransform.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) hlg.enabled = false;

        float handWidthPx = HandLayoutHelper.GetHandAreaWidth(handAreaTransform as RectTransform);
        float prefabWidth = HandLayoutHelper.GetPrefabCardWidth(cardUIPrefab);
        HandLayoutConfig layout = HandLayoutHelper.GetLayout(myCards.Count, handWidthPx, prefabWidth);
        float startX = HandLayoutHelper.ComputeStartX(layout, myCards.Count);

        foreach (Transform child in handAreaTransform) { child.DOKill(); Object.Destroy(child.gameObject); }

        for (int i = 0; i < myCards.Count; i++)
        {
            GameObject newCardUI = Object.Instantiate(cardUIPrefab, handAreaTransform);
            newCardUI.GetComponent<CardDisplay>()?.SetCardData(myCards[i]);
            RectTransform rt = newCardUI.GetComponent<RectTransform>();
            float targetX = startX + i * (layout.prefabCardWidth + layout.spacing);
            if (animate) rt.anchoredPosition = new Vector2(targetX, -12f);
            else rt.anchoredPosition = new Vector2(targetX, 0f);
        }

        if (animate && myCards.Count > 0)
        {
            Sequence popSeq = DOTween.Sequence();
            int idx = 0;
            foreach (Transform child in handAreaTransform)
            {
                RectTransform rt = child.GetComponent<RectTransform>();
                if (rt == null) continue;
                float targetX = startX + idx * (layout.prefabCardWidth + layout.spacing);
                popSeq.Insert(idx * 0.02f, rt.DOAnchorPos(new Vector2(targetX, 0f), 0.25f).SetEase(Ease.OutBack));
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

        if (isDealingComplete)
            RefreshHandUI(animate: false);
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
        lastTrickWinnerActor = starterActor;

        float turnDelay = matchInProgress ? 0.15f : 1.1f;
        StartCoroutine(HandRevealThenStartGame(starterActor, turnDelay, matchInProgress));
    }

    IEnumerator HandRevealThenStartGame(int starterActor, float turnDelay, bool matchInProgress)
    {
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false;

        while (IsDealAnimationRunning || _isDealAnimRunning)
            yield return null;

        yield return new WaitForSeconds(DealFlyDuration + 0.1f);

        if (handAreaTransform != null)
            ClearHandUI();

        _handRevealRunning = true;
        yield return AnimateHandSpreadReveal();
        _handRevealRunning = false;
        isDealingComplete = true;

        ShowOpponentFansWithAnimation();
        yield return new WaitForSeconds(turnDelay);

        GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
        BuildTableTurnOrder();
        if (PhotonNetwork.IsMasterClient) currentTurnActor = starterActor;
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
