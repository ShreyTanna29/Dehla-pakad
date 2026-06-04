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

    [Header("AAA Dealing Animation")]
    [UnityEngine.Serialization.FormerlySerializedAs("flyingCardPrefab")]
    public GameObject dummyCardPrefab; 
    private Transform canvasTransform;
    private Transform centerPos; 
    private Transform[] playerPositions;

    public int currentTurnActor 
    { 
        get {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CTA", out object actor))
                return (int)actor;
            return -1;
        }
        set {
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "CTA", value } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }
        }
    }
    public static bool isTrumpRevealed = false;
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
                if (currentTrick.Count != interleaved.Length / 3)
                {
                    RestoreTableCardsFromRoom();
                }
            }
        }
        
        if (propertiesThatChanged.ContainsKey("CTA"))
        {
             int actor = (int)propertiesThatChanged["CTA"];
             if (isDealingComplete && actor != _lastHandledTurnActor) ProcessTurn(actor);
        }
    }

    private int _lastHandledTurnActor = -1;
    private HashSet<int> _activeBotsThinking = new HashSet<int>();

    public void RestoreTableCardsFromRoom()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TC", out object tcObj))
        {
            Debug.Log("[Sync] Restoring Table Cards from Room Properties...");
            int[] interleaved = (int[])tcObj;
            
            GameObject tableCenter = GameObject.Find("Table_Center");
            if (tableCenter != null)
            {
                foreach (Transform child in tableCenter.transform)
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

    private void SpawnCardOnTableLocal(int senderActorNum, int suitIndex, int rankIndex)
    {
        int seatIndex = GetSeatIndex(senderActorNum);
        GameObject tableCenter = GameObject.Find("Table_Center");
        Transform center = tableCenter != null ? tableCenter.transform : transform;
        GameObject cardObj = Object.Instantiate(cardUIPrefab, playerPositions[seatIndex].position, Quaternion.identity, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex });
        Vector3 offsetPos = seatIndex == 0 ? new Vector3(0, -120f, 0) : seatIndex == 1 ? new Vector3(-180f, 0, 0) : seatIndex == 2 ? new Vector3(0, 120f, 0) : new Vector3(180f, 0, 0);
        
        cardObj.transform.localPosition = offsetPos;
        cardObj.transform.localRotation = Quaternion.identity;

        currentTrick.Add(new TrickCard { actorNumber = senderActorNum, suit = (CardSuit)suitIndex, rankValue = rankIndex, cardObject = cardObj });
    }

    public List<TrickCard> currentTrick = new List<TrickCard>();
    
    private int lastTrickWinnerActor = -1;
    private bool isDealingComplete = false;
    private int _cutsInMatch = 0;
    private static bool _resultPanelShown = false;
    private readonly List<int> tableTurnOrder = new List<int>(4);
    private readonly List<GameObject> opponentBackCards = new List<GameObject>();

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

    void Start()
    {
        CardInteract.canPlayCards = false; 

        GameObject handArea = GameObject.Find("Player_Hand_Area");
        if (handArea != null) handAreaTransform = handArea.transform;

        canvasTransform = GameObject.Find("Canvas")?.transform;
        centerPos = GameObject.Find("Button_Deal")?.transform; 

        playerPositions = new Transform[] {
            handAreaTransform,
            GameObject.Find("Opponent_Left")?.transform,
            GameObject.Find("Opponent_Top")?.transform,
            GameObject.Find("Opponent_Right")?.transform
        };
        
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
        myCards.Clear();
        _cutsInMatch = 0;
        totalTricksPlayed = 0;
        currentTrick.Clear();
        lastTrickWinnerActor = -1;
        isTrumpRevealed = false;
        _resultPanelShown = false;
        isDealingComplete = false;
        tableTurnOrder.Clear();
        CardInteract.canPlayCards = false;
        CardInteract.isPlayingCard = false; // Reset lock
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
        List<int> allActors = new List<int>();
        foreach (Player p in PhotonNetwork.PlayerList) allActors.Add(p.ActorNumber);
        if (DeckManager.Instance != null) allActors.AddRange(DeckManager.botActorNumbers);
        allActors.Sort();

        if (allActors.Count < 4) return;

        int myIndex = allActors.IndexOf(PhotonNetwork.LocalPlayer.ActorNumber);
        if (myIndex == -1) return;

        for (int i = 0; i < 4; i++) tableTurnOrder.Add(allActors[(myIndex + i) % 4]);
    }

    public int GetNextTurnActor(int currentActor)
    {
        if (tableTurnOrder.Count < 4) BuildTableTurnOrder();
        if (tableTurnOrder.Count == 0) return currentActor;
        
        int idx = tableTurnOrder.IndexOf(currentActor);
        if (idx < 0) return tableTurnOrder[0];

        int nextIdx = (idx - 1 + tableTurnOrder.Count) % tableTurnOrder.Count;
        return tableTurnOrder[nextIdx];
    }

    private string GetSeatName(int seatIndex)
    {
        switch (seatIndex)
        {
            case 0: return "Bottom";
            case 1: return "Left";
            case 2: return "Top";
            case 3: return "Right";
            default: return "Unknown";
        }
    }

    void ProcessTurn(int actorNumber)
    {
        if (!isDealingComplete) return;
        if (actorNumber == _lastHandledTurnActor && !PhotonNetwork.IsMasterClient) return;
        _lastHandledTurnActor = actorNumber;
        
        if (PhotonNetwork.IsMasterClient) currentTurnActor = actorNumber; 

        if (TurnManager.Instance != null && PhotonNetwork.IsMasterClient)
            TurnManager.Instance.StartTurn(actorNumber);

        bool isMyTurn = (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber);
        CardInteract.canPlayCards = isMyTurn;
        CardInteract.isPlayingCard = false; // Reset interaction lock when turn starts

        ApplyRules(isMyTurn);

        if (PhotonNetwork.IsMasterClient) TriggerBotTurnIfApplicable(actorNumber);
    }

    public void TriggerBotTurnIfApplicable(int actorNumber)
    {
        if (DeckManager.botActorNumbers.Contains(actorNumber))
        {
            if (_activeBotsThinking.Contains(actorNumber)) return;
            StartCoroutine(BotTurnRoutine(actorNumber));
        }
    }

    IEnumerator BotTurnRoutine(int actorNum)
    {
        _activeBotsThinking.Add(actorNum);
        yield return new WaitForSeconds(Random.Range(1.2f, 2.2f));
        
        if (!isDealingComplete || currentTurnActor != actorNum) 
        {
            _activeBotsThinking.Remove(actorNum);
            yield break;
        }

        if (DehlaPakadAI.Instance == null || DeckManager.Instance == null) 
        {
            _activeBotsThinking.Remove(actorNum);
            yield break;
        }
        
        if (!DeckManager.Instance.botHands.TryGetValue(actorNum, out List<CardData> hand) || hand.Count == 0) 
        { 
            _activeBotsThinking.Remove(actorNum);
            yield break; 
        }

        CardData botCard = DehlaPakadAI.Instance.ThinkAndSelectCard(hand, currentTrick, currentTrumpSuit, isTrumpRevealed, actorNum);
        if (!hand.Remove(botCard)) botCard = hand[0];
        
        _activeBotsThinking.Remove(actorNum);
        photonView.RPC("RPC_PlayCard", RpcTarget.All, actorNum, (int)botCard.cardSuit, (int)botCard.cardRank);
    }

    void ApplyNotMyTurnVisualState()
    {
        if (handAreaTransform == null) return;
        Debug.Log("Applied NotMyTurn Visual State");
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
        Debug.Log("Exited My Turn");
        CardInteract.canPlayCards = false;
        ApplyNotMyTurnVisualState();
    }

    void HighlightPlayableCards()
    {
        if (handAreaTransform == null) return;
        Debug.Log("Applied Playable Highlights");

        bool isLeading = currentTrick == null || currentTrick.Count == 0;
        List<CardData> validPlayableCards = isLeading ? new List<CardData>(myCards) : GetValidCards(myCards, currentTrick);
        var playableInHand = new List<CardData>();
        var blockedInHand = new List<CardData>();
        var usedValidMatches = new Dictionary<(CardSuit, CardRank), int>();
        CardInteract[] interacts = handAreaTransform.GetComponentsInChildren<CardInteract>();

        foreach (CardInteract ci in interacts)
        {
            if (ci == null || ci.isPlayed) continue;
            CardDisplay d = ci.GetComponentInParent<CardDisplay>();
            if (d == null) continue;

            bool isValid = isLeading || IsCardPlayableForUi(d.myCardData, validPlayableCards, usedValidMatches);
            ci.isValidToPlay = isValid;
            if (isValid) playableInHand.Add(d.myCardData);
            else blockedInHand.Add(d.myCardData);

            if (isValid)
                ci.ApplyPlayableVisual(true);
            else
                ci.ApplyBlockedOnTurnVisual();
        }

        if (!isLeading && currentTrick != null && currentTrick.Count > 0)
            LogCardRulesDebug(currentTrick, validPlayableCards, playableInHand, blockedInHand);
    }

    public void ApplyRules(bool isMyTurn)
    {
        if (handAreaTransform == null) return;
        if (!isMyTurn)
        {
            EndTurnCardVisuals();
            return;
        }

        Debug.Log("Entered My Turn");
        CardInteract.ClearGlobalSelection();
        HighlightPlayableCards();
    }

    static void LogCardRulesDebug(List<TrickCard> trick, List<CardData> validFromRules, List<CardData> playableUi, List<CardData> blockedUi)
    {
        CardSuit lead = trick[0].suit;
        string winning = GetCurrentWinningCardLabel(trick);
        string playable = playableUi.Count > 0
            ? string.Join(", ", playableUi.Select(GetCardLabel))
            : string.Join(", ", validFromRules.Select(GetCardLabel));
        string blocked = blockedUi.Count > 0
            ? string.Join(", ", blockedUi.Select(GetCardLabel))
            : "(none)";

        Debug.Log(
            $"[CardRules] Lead Suit: {lead}\n" +
            $"[CardRules] Current Winning Card: {winning}\n" +
            $"[CardRules] Playable Cards: {playable}\n" +
            $"[CardRules] Blocked Cards: {blocked}");
    }

    static string GetCurrentWinningCardLabel(List<TrickCard> trick)
    {
        if (trick == null || trick.Count == 0) return "(none)";
        TrickCard w = trick[0];
        CardSuit led = trick[0].suit;
        for (int i = 1; i < trick.Count; i++)
        {
            bool challengerTrump = trick[i].suit == currentTrumpSuit;
            bool winnerTrump = w.suit == currentTrumpSuit;
            if (challengerTrump && !winnerTrump) w = trick[i];
            else if (challengerTrump && winnerTrump && trick[i].rankValue > w.rankValue) w = trick[i];
            else if (!challengerTrump && !winnerTrump && trick[i].suit == led && trick[i].rankValue > w.rankValue) w = trick[i];
        }
        return $"{(CardRank)w.rankValue} of {w.suit}";
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

    static int GetHighestLedSuitRankInTrick(List<TrickCard> trick, CardSuit ledSuit)
    {
        int highest = -1;
        foreach (TrickCard tc in trick)
        {
            if (tc.suit == ledSuit && tc.rankValue > highest)
                highest = tc.rankValue;
        }
        return highest;
    }

    static string GetCardLabel(CardData c) => $"{c.cardRank} of {c.cardSuit}";

    static string ExplainInvalidCard(CardData card, List<CardData> validList, List<TrickCard> trick, List<CardData> fullHand)
    {
        if (trick == null || trick.Count == 0) return "not your turn or unknown";

        CardSuit ledSuit = trick[0].suit;
        bool hasLedSuit = fullHand != null && fullHand.Any(c => c.cardSuit == ledSuit);

        if (hasLedSuit && card.cardSuit != ledSuit)
            return $"must follow {ledSuit}; {GetCardLabel(card)} is wrong suit";

        if (card.cardSuit == ledSuit)
        {
            int highLed = GetHighestLedSuitRankInTrick(trick, ledSuit);
            if ((int)card.cardRank <= highLed && validList.Any(v => (int)v.cardRank > highLed))
                return $"must beat highest {ledSuit} on table (rank {highLed}); lower {ledSuit} cards blocked";
        }

        return "not a legal play for current trick";
    }

    public void OnLocalPlayerPlayedCard(CardData cardData, GameObject cardObj)
    {
        CardInteract.isPlayingCard = true;
        EndTurnCardVisuals();

        Destroy(cardObj);
        RemoveOneCardFromHand(myCards, cardData.cardSuit, cardData.cardRank);
        RefreshHandUI(animate: false); // Ensure spacing is updated immediately
        if (photonView != null) photonView.RPC("RPC_PlayCard", RpcTarget.All, PhotonNetwork.LocalPlayer.ActorNumber, (int)cardData.cardSuit, (int)cardData.cardRank);
    }

    [PunRPC]
    public void RPC_PlayCard(int senderActorNum, int suitIndex, int rankIndex)
    {
        if (LocalInstance == null) return;
        if (LocalInstance != this) { LocalInstance.RPC_PlayCard(senderActorNum, suitIndex, rankIndex); return; }

        if (PhotonNetwork.IsMasterClient && DeckManager.Instance != null)
            DeckManager.Instance.UpdateCachedHandOnMaster(senderActorNum, (CardSuit)suitIndex, (CardRank)rankIndex);

        int seatIndex = GetSeatIndex(senderActorNum);
        GameObject tableCenter = GameObject.Find("Table_Center");
        Transform center = tableCenter != null ? tableCenter.transform : transform;
        GameObject cardObj = Object.Instantiate(cardUIPrefab, playerPositions[seatIndex].position, Quaternion.identity, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex });
        Vector3 offsetPos = seatIndex == 0 ? new Vector3(0, -120f, 0) : seatIndex == 1 ? new Vector3(-180f, 0, 0) : seatIndex == 2 ? new Vector3(0, 120f, 0) : new Vector3(180f, 0, 0);
        cardObj.transform.DOLocalMove(offsetPos, 0.35f).SetEase(Ease.OutCubic);
        cardObj.transform.DORotate(Vector3.zero, 0.35f);

        currentTrick.Add(new TrickCard { actorNumber = senderActorNum, suit = (CardSuit)suitIndex, rankValue = rankIndex, cardObject = cardObj });
        if (PhotonNetwork.IsMasterClient) SyncCurrentTrickToRoom();

        bool isCutMode = GameSettings.Instance != null && (GameSettings.Instance.currentMode == GameModeType.Cut1Trump || GameSettings.Instance.currentMode == GameModeType.Cut2Trump);
        if (isCutMode && currentTrick.Count > 1 && TrumpManager.Instance != null && !TrumpManager.Instance.IsTrumpRevealed())
        {
            CardSuit ledSuit = currentTrick[0].suit;
            CardSuit playedSuit = (CardSuit)suitIndex;
            if (playedSuit != ledSuit)
            {
                if (GameSettings.Instance.currentMode == GameModeType.Cut1Trump)
                    TrumpManager.Instance.SyncTrumpSuit(playedSuit, true, true);
                else if (!isTrumpRevealed && currentTrumpSuit == CardSuit.Spades)
                    TrumpManager.Instance.SyncTrumpSuit(playedSuit, true, false);
                else
                    TrumpManager.Instance.SyncTrumpSuit(playedSuit, true, true);
            }
        }

        if (currentTrick.Count == 4)
        {
            if (TurnManager.Instance != null) TurnManager.Instance.StopTimer();
            StartCoroutine(DetermineTrickWinnerRoutine());
        }
        else 
        {
            int nextActor = GetNextTurnActor(senderActorNum);
            ProcessTurn(nextActor);
        }
    }

    IEnumerator DetermineTrickWinnerRoutine()
    {
        if (currentTrick == null || currentTrick.Count < 4) yield break;
        yield return new WaitForSeconds(1.5f); 
        TrickCard winnerCard = TaashRules.DetermineTrickWinner(currentTrick, currentTrumpSuit);
        int tricksToWin = TaashRules.TricksPerGame;
        
        lastTrickWinnerActor = winnerCard.actorNumber;
        int winnerSeat = GetSeatIndex(winnerCard.actorNumber);
        if (ResultManager.Instance != null) 
        {
            int dehlas = 0;
            foreach(var tc in currentTrick) if(tc.rankValue == (int)CardRank.Ten) dehlas++;
            ResultManager.Instance.OnTrickWon(winnerSeat, dehlas);
        }

        Transform winnerTransform = playerPositions[winnerSeat];
        foreach (var tc in currentTrick)
        {
            if (tc.cardObject != null)
            {
                tc.cardObject.transform.DOMove(winnerTransform.position, 0.4f).SetEase(Ease.InBack);
            }
        }
        yield return new WaitForSeconds(0.5f);
        foreach (var tc in currentTrick) if (tc.cardObject != null) Object.Destroy(tc.cardObject);
        currentTrick.Clear();
        EndTurnCardVisuals();

        if (PhotonNetwork.IsMasterClient)
        {
            SyncCurrentTrickToRoom();
            totalTricksPlayed++;
            if (totalTricksPlayed >= tricksToWin)
            {
                Debug.Log($"[PlayerHand] Game finished — {tricksToWin} tricks. (Master: {PhotonNetwork.IsMasterClient})");
                if (PhotonNetwork.IsMasterClient)
                {
                    if (photonView != null)
                        photonView.RPC("RPC_ShowGameResult", RpcTarget.All);
                    else
                        ShowGameResultLocal();
                }
                yield break;
            }
        }

        if (totalTricksPlayed >= tricksToWin)
        {
            yield break;
        }
        ProcessTurn(lastTrickWinnerActor);
    }

    public int GetSeatIndex(int actorNum)
    {
        if (tableTurnOrder.Count < 4) BuildTableTurnOrder();
        int idx = tableTurnOrder.IndexOf(actorNum);
        return idx >= 0 ? idx : 0;
    }

    private bool _isDealAnimRunning = false;

    public void PlayDealAnimationOnly(int cardsInBatch)
    {
        Debug.Log($"[PlayerHand] PlayDealAnimationOnly — round size {cardsInBatch} (5-4-4 pattern)");
        StartCoroutine(DealAnimationOnlyRoutine(cardsInBatch));
    }

    public const float DealFlyDuration = 0.25f;
    public const float DealCardLaunchGap = 0.03f;
    public const float DealPacketCardSpread = 12f;
    public const float DealSeatPause = 0.04f; // Reduced pause between players
    public const float DealRoundSettlePause = 0.04f; // Reduced pause between batches

    public static float GetDealBatchDuration(int cardsInBatch)
    {
        float perPlayer = (cardsInBatch - 1) * DealCardLaunchGap + DealFlyDuration + DealSeatPause;
        return 4f * perPlayer + DealRoundSettlePause;
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

        if (canvasTransform == null)
        {
            Canvas rootCanvas = Object.FindAnyObjectByType<Canvas>();
            canvasTransform = rootCanvas != null ? rootCanvas.transform : GameObject.Find("Canvas")?.transform;
        }

        if (canvasTransform == null)
        {
            _isDealAnimRunning = false;
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

            for (int i = 0; i < cardsInBatch; i++)
            {
                GameObject flyingCard = Object.Instantiate(dummyCardPrefab, canvasTransform);
                RectTransform cardRt = flyingCard.GetComponent<RectTransform>();
                cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);

                float spreadOffset = (i - (cardsInBatch - 1) * 0.5f) * DealPacketCardSpread;
                cardRt.anchoredPosition = deckAnchor + new Vector2(spreadOffset * 0.35f, 0f);

                Vector2 target = seatAnchor + new Vector2(spreadOffset, 0f);
                cardRt.DOAnchorPos(target, DealFlyDuration).SetEase(Ease.Linear).OnComplete(() => {
                    if (flyingCard != null) Object.Destroy(flyingCard);
                });

                yield return new WaitForSeconds(DealCardLaunchGap);
            }
            
            // Short pause before moving to the next player
            yield return new WaitForSeconds(0.12f);
        }

        _isDealAnimRunning = false;
    }

    void RefreshHandUI(bool animate = true)
    {
        if (handAreaTransform == null) return;
        myCards = myCards.OrderBy(c => c.cardSuit).ThenByDescending(c => c.cardRank).ToList();

        HorizontalLayoutGroup hlg = handAreaTransform.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null) hlg.enabled = false;

        float handWidthPx = HandLayoutHelper.GetHandAreaWidth(handAreaTransform as RectTransform);
        float prefabWidth = HandLayoutHelper.GetPrefabCardWidth(cardUIPrefab);
        HandLayoutConfig layout = HandLayoutHelper.GetLayout(myCards.Count, handWidthPx, prefabWidth);
        float startX = HandLayoutHelper.ComputeStartX(layout, myCards.Count);
        Debug.Log(
            $"[PlayerHand.RefreshHandUI] Card Count: {myCards.Count}\n" +
            $"[PlayerHand.RefreshHandUI] Spacing Used: {layout.spacing}\n" +
            $"[PlayerHand.RefreshHandUI] Hand Width: {handWidthPx}\n" +
            $"[PlayerHand.RefreshHandUI] HorizontalLayoutGroup enabled: {(hlg != null && hlg.enabled)}");

        foreach (Transform child in handAreaTransform) { child.DOKill(); Object.Destroy(child.gameObject); }

        for (int i = 0; i < myCards.Count; i++)
        {
            GameObject newCardUI = Object.Instantiate(cardUIPrefab, handAreaTransform);
            newCardUI.GetComponent<CardDisplay>()?.SetCardData(myCards[i]);
            RectTransform rt = newCardUI.GetComponent<RectTransform>();
            float targetX = startX + i * (layout.prefabCardWidth + layout.spacing);
            if (animate)
                rt.anchoredPosition = new Vector2(targetX, -12f);
            else
                rt.anchoredPosition = new Vector2(targetX, 0f);

            HandLayoutHelper.LogCardSizeIntegrity(cardUIPrefab, rt, $"RefreshHandUI card {i + 1}/{myCards.Count}");
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
    }

    public void OnDealingComplete(int starterActor)
    {
        if (isDealingComplete) return; 
        if (LocalInstance != null && LocalInstance != this) { LocalInstance.OnDealingComplete(starterActor); return; }
        
        Debug.Log($"[PlayerHand] OnDealingComplete. Starter: {starterActor}");

        bool matchInProgress = false;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object started))
            matchInProgress = (bool)started;

        isDealingComplete = true;
        GameFlowState.SetPhase(GameFlowPhase.InGame);
        BuildTableTurnOrder();
        lastTrickWinnerActor = starterActor;

        float turnDelay = matchInProgress ? 0.15f : 1.1f;
        StartCoroutine(HandRevealThenStartGame(starterActor, turnDelay, matchInProgress));
    }

    IEnumerator HandRevealThenStartGame(int starterActor, float turnDelay, bool matchInProgress)
    {
        if (!matchInProgress)
            yield return AnimateHandSpreadReveal();
        else
            RefreshHandUI(animate: false);

        ShowOpponentFansWithAnimation();

        yield return new WaitForSeconds(turnDelay);
        ProcessTurn(starterActor);
    }

    IEnumerator AnimateHandSpreadReveal()
    {
        if (handAreaTransform == null || cardUIPrefab == null) yield break;

        yield return new WaitForSeconds(0.38f);

        myCards = myCards.OrderBy(c => c.cardSuit).ThenByDescending(c => c.cardRank).ToList();
        ClearHandUI();

        HorizontalLayoutGroup revealHlg = handAreaTransform.GetComponent<HorizontalLayoutGroup>();
        if (revealHlg != null) revealHlg.enabled = false;

        float handWidthPx = HandLayoutHelper.GetHandAreaWidth(handAreaTransform as RectTransform);
        float prefabWidth = HandLayoutHelper.GetPrefabCardWidth(cardUIPrefab);
        HandLayoutConfig layout = HandLayoutHelper.GetLayout(myCards.Count, handWidthPx, prefabWidth);
        float startX = HandLayoutHelper.ComputeStartX(layout, myCards.Count);
        Debug.Log(
            $"[PlayerHand.AnimateHandSpreadReveal] Card Count: {myCards.Count}\n" +
            $"[PlayerHand.AnimateHandSpreadReveal] Spacing Used: {layout.spacing}\n" +
            $"[PlayerHand.AnimateHandSpreadReveal] Hand Width: {handWidthPx}\n" +
            $"[PlayerHand.AnimateHandSpreadReveal] HorizontalLayoutGroup enabled: {(revealHlg != null && revealHlg.enabled)}");

        var cardRects = new List<RectTransform>();

        for (int i = 0; i < myCards.Count; i++)
        {
            GameObject card = Object.Instantiate(cardUIPrefab, handAreaTransform);
            card.GetComponent<CardDisplay>()?.SetCardData(myCards[i]);
            RectTransform rt = card.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0f, -6f) + new Vector2(i * 2f, i * 1.5f);
            HandLayoutHelper.LogCardSizeIntegrity(cardUIPrefab, rt, $"AnimateHandSpreadReveal stack card {i + 1}/{myCards.Count}");
            cardRects.Add(rt);
        }

        yield return new WaitForSeconds(0.35f);

        Sequence spreadSeq = DOTween.Sequence();
        for (int i = 0; i < cardRects.Count; i++)
        {
            float targetX = startX + i * (layout.prefabCardWidth + layout.spacing);
            spreadSeq.Insert(i * 0.04f, cardRects[i].DOAnchorPos(new Vector2(targetX, 0f), 0.46f).SetEase(Ease.OutBack));
        }
        yield return spreadSeq.WaitForCompletion();
    }

    public void ShowGameResultLocal()
    {
        if (_resultPanelShown) return;
        _resultPanelShown = true;

        Debug.Log("[PlayerHand] ShowGameResultLocal — opening result panel");
        GameFlowState.SetPhase(GameFlowPhase.GameFinished);
        if (ResultManager.Instance != null)
            ResultManager.Instance.ShowResult();
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
