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
    public GameObject dummyCardPrefab; 
    private Transform canvasTransform;
    private Transform centerPos; 
    private Transform[] playerPositions;

    public int currentTurnActor = -1; 
    public static bool isTrumpRevealed = false;
    public static CardSuit currentTrumpSuit = CardSuit.Spades; 

    public class TrickCard
    {
        public int actorNumber; 
        public CardSuit suit;
        public int rankValue;
        public GameObject cardObject;
    }

    public List<TrickCard> currentTrick = new List<TrickCard>();
    
    private int totalTricksPlayed = 0;
    private int lastTrickWinnerActor = -1;
    private bool isDealingComplete = false;
    private readonly List<int> tableTurnOrder = new List<int>(4);
    private readonly List<GameObject> opponentBackCards = new List<GameObject>();

    void Start()
    {
        CardInteract.canPlayCards = false; 

        GameObject handArea = GameObject.Find("Player_Hand_Area");
        if (handArea != null)
        {
            handAreaTransform = handArea.transform;
        }
        else
        {
            Debug.LogError("[PlayerHand] Player_Hand_Area NOT FOUND in scene!");
        }

        canvasTransform = GameObject.Find("Canvas")?.transform;
        if (canvasTransform == null) Debug.LogError("[PlayerHand] Canvas NOT FOUND in scene!");
        
        centerPos = GameObject.Find("Button_Deal")?.transform; 
        if (centerPos == null) Debug.LogWarning("[PlayerHand] Button_Deal NOT FOUND (using screen center for dealing start).");

        playerPositions = new Transform[] {
            handAreaTransform,
            GameObject.Find("Opponent_Left")?.transform,
            GameObject.Find("Opponent_Top")?.transform,
            GameObject.Find("Opponent_Right")?.transform
        };
        
        for (int i = 0; i < playerPositions.Length; i++)
        {
            if (playerPositions[i] == null) Debug.LogError($"[PlayerHand] playerPositions[{i}] NOT FOUND in scene!");
        }

        if (photonView.IsMine)
        {
            ResetHand();
        }
        
        Debug.Log($"[UI Setup] Screen: {Screen.width}x{Screen.height}, Safe Area: {Screen.safeArea}");
    }

    public override void OnJoinedRoom()
    {
        if (photonView.IsMine)
        {
            ResetHand();
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
        myCards.Clear();
        totalTricksPlayed = 0;
        currentTrick.Clear();
        lastTrickWinnerActor = -1;
        isTrumpRevealed = false;
        isDealingComplete = false;
        tableTurnOrder.Clear();
        CardInteract.canPlayCards = false;
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
                CanvasGroup cg = fan.GetComponent<CanvasGroup>();
                if (cg == null) cg = fan.gameObject.AddComponent<CanvasGroup>();
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
                CanvasGroup cg = fan.GetComponent<CanvasGroup>();
                if (cg == null) cg = fan.gameObject.AddComponent<CanvasGroup>();
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

        for (int i = 0; i < 4; i++)
        {
            tableTurnOrder.Add(allActors[(myIndex + i) % 4]);
        }

        Debug.Log($"[Seating] tableTurnOrder built: {string.Join(", ", tableTurnOrder)}");
    }

    public int GetNextTurnActor(int currentActor)
    {
        if (tableTurnOrder.Count < 4) BuildTableTurnOrder();
        if (tableTurnOrder.Count == 0) return currentActor;
        
        int idx = tableTurnOrder.IndexOf(currentActor);
        if (idx < 0) return tableTurnOrder[0];

        // 🔄 CLOCKWISE: 0 -> 3 -> 2 -> 1 -> 0
        // Bottom (0) -> Right (3) -> Top (2) -> Left (1) -> Bottom (0)
        int nextIdx = (idx - 1 + tableTurnOrder.Count) % tableTurnOrder.Count;
        int nextActor = tableTurnOrder[nextIdx];

        Debug.Log($"[Turn Logic] Turn: {GetSeatName(idx)} ({currentActor}) | Next: {GetSeatName(nextIdx)} ({nextActor})");
        
        return nextActor;
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
        currentTurnActor = actorNumber;
        if (TurnManager.Instance != null && PhotonNetwork.IsMasterClient)
            TurnManager.Instance.StartTurn(actorNumber);

        bool isMyTurn = (PhotonNetwork.LocalPlayer.ActorNumber == currentTurnActor);
        
        // 🚀 YOUR TURN VISUAL FEEDBACK
        if (isMyTurn)
        {
            // Lift the whole hand area slightly and pulse it
            if (handAreaTransform != null)
            {
                handAreaTransform.DOKill();
                handAreaTransform.DOScale(1.02f, 0.3f).SetLoops(2, LoopType.Yoyo).SetUpdate(true);
            }
            Debug.Log("<color=yellow>⭐ IT IS YOUR TURN! ⭐</color>");
        }

        CardInteract.canPlayCards = isMyTurn;
        ApplyRules(isMyTurn);

        if (!isMyTurn && PhotonNetwork.IsMasterClient && DeckManager.botActorNumbers.Contains(actorNumber))
            StartCoroutine(BotTurnRoutine(actorNumber));
    }

    IEnumerator BotTurnRoutine(int actorNum)
    {
        Debug.Log($"🤖 [Bot Mode] Bot {actorNum} thinking...");
        yield return new WaitForSeconds(Random.Range(1.2f, 2.2f));
        if (!isDealingComplete) { Debug.LogWarning("[Bot Mode] Bot thinking aborted: dealing not complete."); yield break; }
        if (DehlaPakadAI.Instance == null) { Debug.LogError("[Bot Mode] Bot thinking aborted: AI Instance missing."); yield break; }
        
        if (DeckManager.Instance == null || !DeckManager.Instance.botHands.TryGetValue(actorNum, out List<CardData> hand)) 
        { 
            Debug.LogError($"[Bot Mode] Bot {actorNum} thinking aborted: hand not found in DeckManager."); 
            yield break; 
        }
        
        if (hand.Count == 0) 
        { 
            Debug.LogWarning($"[Bot Mode] Bot {actorNum} thinking aborted: hand is empty."); 
            yield break; 
        }

        CardData botCard = DehlaPakadAI.Instance.ThinkAndSelectCard(hand, currentTrick, currentTrumpSuit, isTrumpRevealed, actorNum);
        Debug.Log($"🤖 [Bot Mode] Bot {actorNum} selected {botCard.cardRank} of {botCard.cardSuit}.");
        
        if (!hand.Remove(botCard)) botCard = hand[0];
        
        photonView.RPC("RPC_PlayCard", RpcTarget.All, actorNum, (int)botCard.cardSuit, (int)botCard.cardRank);
    }

    public static List<CardData> GetValidCards(List<CardData> hand, List<TrickCard> trick)
    {
        if (trick == null || trick.Count == 0) return new List<CardData>(hand);
        CardSuit ledSuit = trick[0].suit;
        List<CardData> matchingSuitCards = hand.FindAll(c => c.cardSuit == ledSuit);
        if (matchingSuitCards.Count > 0) return matchingSuitCards;
        return new List<CardData>(hand);
    }

    public void ApplyRules(bool isMyTurn)
    {
        if (handAreaTransform == null) return;
        CardInteract[] interacts = handAreaTransform.GetComponentsInChildren<CardInteract>();
        if (!isMyTurn)
        {
            foreach (var ci in interacts) { if (ci != null) { ci.isValidToPlay = false; ci.SetCardRuleState(false, false); } }
            return;
        }

        List<CardData> validPlayableCards = GetValidCards(myCards, currentTrick);
        foreach (var ci in interacts)
        {
            if (ci == null || ci.isPlayed) continue;
            CardDisplay d = ci.GetComponentInParent<CardDisplay>();
            if (d == null) continue;
            
            bool isValid = validPlayableCards.Any(c => c.cardSuit == d.myCardData.cardSuit && c.cardRank == d.myCardData.cardRank);
            ci.isValidToPlay = isValid;
            ci.SetCardRuleState(isValid, isValid); 

            // 🚀 VISUAL CUE: Lift valid cards slightly when it's my turn
            RectTransform rt = ci.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.DOKill();
                if (isValid && isMyTurn)
                {
                    rt.DOAnchorPosY(40f, 0.3f).SetEase(Ease.OutBack);
                }
                else
                {
                    rt.DOAnchorPosY(0f, 0.3f).SetEase(Ease.InSine);
                }
            }
}
}

    public void OnLocalPlayerPlayedCard(CardData cardData, GameObject cardObj)
    {
        CardInteract.canPlayCards = false; 
        Destroy(cardObj);
        myCards.RemoveAll(c => c.cardSuit == cardData.cardSuit && c.cardRank == cardData.cardRank); 
        ApplyRules(false);
        if (photonView != null) photonView.RPC("RPC_PlayCard", RpcTarget.All, PhotonNetwork.LocalPlayer.ActorNumber, (int)cardData.cardSuit, (int)cardData.cardRank);
    }

    [PunRPC]
    public void RPC_PlayCard(int senderActorNum, int suitIndex, int rankIndex)
    {
        if (LocalInstance == null) return;
        if (LocalInstance != this)
        {
            LocalInstance.RPC_PlayCard(senderActorNum, suitIndex, rankIndex);
            return;
        }

        if (DeckManager.botActorNumbers.Contains(senderActorNum))
        {
            Debug.Log($"🤖 [Bot Mode] Bot Played Card: {(CardSuit)suitIndex} {(CardRank)rankIndex} (Actor {senderActorNum})");
        }

        int seatIndex = GetSeatIndex(senderActorNum);
        GameObject tableCenter = GameObject.Find("Table_Center");
        Transform center = tableCenter != null ? tableCenter.transform : transform;
        GameObject cardObj = Object.Instantiate(cardUIPrefab, playerPositions[seatIndex].position, Quaternion.identity, center);
        cardObj.GetComponent<CardDisplay>()?.SetCardData(new CardData { cardSuit = (CardSuit)suitIndex, cardRank = (CardRank)rankIndex });
        Vector3 offsetPos = seatIndex == 0 ? new Vector3(0, -120f, 0) : seatIndex == 1 ? new Vector3(-180f, 0, 0) : seatIndex == 2 ? new Vector3(0, 120f, 0) : new Vector3(180f, 0, 0);
        cardObj.transform.DOLocalMove(offsetPos, 0.35f).SetEase(Ease.OutCubic);
        cardObj.transform.DOScale(0.9f, 0.35f);
        cardObj.transform.DORotate(Vector3.zero, 0.35f);

        currentTrick.Add(new TrickCard { actorNumber = senderActorNum, suit = (CardSuit)suitIndex, rankValue = rankIndex, cardObject = cardObj });

        // Mode 3: Cut To Trump
        if (GameSettings.Instance != null && GameSettings.Instance.currentMode == GameModeType.CutToTrump)
        {
            if (currentTrick.Count > 1)
            {
                CardSuit ledSuit = currentTrick[0].suit;
                CardSuit playedSuit = (CardSuit)suitIndex;
                if (playedSuit != ledSuit)
                {
                    // Player cut with a different suit
                    if (TrumpManager.Instance != null && TrumpManager.Instance.GetCurrentTrumpSuit() != playedSuit)
                    {
                        Debug.Log($"[Mode 3] Cut detected! New Trump: {playedSuit}");
                        TrumpManager.Instance.SetTrumpSuit(playedSuit, true);
                    }
                }
            }
        }

        if (currentTrick.Count == 4)
        {
            if (TurnManager.Instance != null) TurnManager.Instance.StopTimer();
            LocalInstance.StartCoroutine(LocalInstance.DetermineTrickWinnerRoutine());
        }
        else 
        {
            int nextActor = GetNextTurnActor(senderActorNum);
            Debug.Log($"[Gameplay] Card played by {senderActorNum}. Next turn: {nextActor}");
            ProcessTurn(nextActor);
        }
    }

    IEnumerator DetermineTrickWinnerRoutine()
    {
        if (currentTrick == null || currentTrick.Count < 4) yield break;
        yield return new WaitForSeconds(1.5f); 
        TrickCard winnerCard = currentTrick[0];
        CardSuit led = currentTrick[0].suit;

        // Note: currentTrumpSuit is static and updated by TrumpManager
        for (int i = 1; i < currentTrick.Count; i++)
        {
            bool isCheckTrump = currentTrick[i].suit == currentTrumpSuit;
            bool isWinnerTrump = winnerCard.suit == currentTrumpSuit;
            if (isCheckTrump && !isWinnerTrump) winnerCard = currentTrick[i];
            else if (isCheckTrump && isWinnerTrump && currentTrick[i].rankValue > winnerCard.rankValue) winnerCard = currentTrick[i];
            else if (!isCheckTrump && !isWinnerTrump && currentTrick[i].suit == led && currentTrick[i].rankValue > winnerCard.rankValue) winnerCard = currentTrick[i];
        }
        
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
                tc.cardObject.transform.DOScale(0.1f, 0.4f);
            }
        }
        yield return new WaitForSeconds(0.5f);
        foreach (var tc in currentTrick) if (tc.cardObject != null) Object.Destroy(tc.cardObject);
        currentTrick.Clear();
        totalTricksPlayed++;
        if (totalTricksPlayed >= 13)
        {
             if (ResultManager.Instance != null) ResultManager.Instance.ShowResult();
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

    public void PlayDealAnimationOnly(int cardsInBatch)
    {
        StartCoroutine(DealAnimationOnlyRoutine(cardsInBatch));
    }

    IEnumerator DealAnimationOnlyRoutine(int cardsInBatch)
    {
        Vector3 startPosition = centerPos != null ? centerPos.position : new Vector3(Screen.width / 2f, Screen.height / 2f, 0);
        for (int p = 0; p < playerPositions.Length; p++)
        {
            if (playerPositions[p] == null) continue;
            Transform targetTransform = playerPositions[p].Find("CardFan");
            if (targetTransform == null) targetTransform = playerPositions[p];
            Vector3 targetPos = targetTransform.position;
            for (int i = 0; i < cardsInBatch; i++)
            {
                if (dummyCardPrefab != null && canvasTransform != null)
                {
                    GameObject flyingCard = Object.Instantiate(dummyCardPrefab, startPosition, Quaternion.identity, canvasTransform);
                    flyingCard.transform.DOMove(targetPos, 0.25f).SetEase(Ease.OutCubic);
                    flyingCard.transform.DOScale(0.2f, 0.25f).OnComplete(() => { Object.Destroy(flyingCard); });
                }
                yield return new WaitForSeconds(0.05f);
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    public void AssignFullHandLocal(int targetActor, int[] suitIndices, int[] rankIndices)
    {
        if (PhotonNetwork.LocalPlayer.ActorNumber != targetActor) return;
        myCards.Clear();
        if (suitIndices == null || rankIndices == null || suitIndices.Length != 13 || rankIndices.Length != 13) return;
        for (int i = 0; i < 13; i++)
            myCards.Add(new CardData { cardSuit = (CardSuit)suitIndices[i], cardRank = (CardRank)rankIndices[i] });
    }

    public bool ValidateHandCount() { return myCards.Count == 13; }

    public void OnDealingComplete(int starterActor)
    {
        if (LocalInstance != null && LocalInstance != this)
        {
            LocalInstance.OnDealingComplete(starterActor);
            return;
        }
        if (!ValidateHandCount()) return;
        isDealingComplete = true;
        BuildTableTurnOrder();
        RefreshHandUI();
        ShowOpponentFansWithAnimation();
        lastTrickWinnerActor = starterActor;
        
        // 🚀 INITIAL TURN ANIMATION
        if (PhotonNetwork.LocalPlayer.ActorNumber == starterActor)
        {
            Invoke(nameof(PlayInitialTurnEffect), 1.2f); // Delay until cards are finished spawning
        }

        ProcessTurn(lastTrickWinnerActor);
    }

    private void PlayInitialTurnEffect()
    {
        Transform myArea = GameObject.Find("Canvas/Panel_Game/You")?.transform;
        if (myArea != null)
        {
            myArea.DOKill();
            myArea.DOPunchPosition(new Vector3(0, 20f, 0), 0.8f, 5, 0.5f).SetUpdate(true);
            
            // Visual Flash
            UnityEngine.UI.Image bg = myArea.Find("You_Avatar")?.GetComponent<UnityEngine.UI.Image>();
            if (bg != null)
            {
                bg.DOColor(new Color(1f, 1f, 0.5f, 1f), 0.4f).SetLoops(4, LoopType.Yoyo).OnComplete(() => bg.color = Color.white);
            }
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

    void RefreshHandUI()
    {
        if (handAreaTransform == null)
        {
            Debug.LogError("[PlayerHand] handAreaTransform is NULL! Cannot refresh UI.");
            return;
        }
        
        Debug.Log($"[PlayerHand] Refreshing UI for {myCards.Count} cards.");
        myCards = myCards.OrderBy(c => c.cardSuit).ThenByDescending(c => c.cardRank).ToList();
        
        HorizontalLayoutGroup hlg = handAreaTransform.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null)
        {
            hlg.spacing = -50f; hlg.childAlignment = TextAnchor.MiddleCenter; 
hlg.childControlWidth = false; hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        }
        
        foreach (Transform child in handAreaTransform) { child.DOKill(); Object.Destroy(child.gameObject); }
        
        Sequence dealSeq = DOTween.Sequence();
        for (int i = 0; i < myCards.Count; i++)
        {
            GameObject newCardUI = Object.Instantiate(cardUIPrefab, handAreaTransform);
            newCardUI.GetComponent<CardDisplay>()?.SetCardData(myCards[i]);
            newCardUI.transform.localScale = Vector3.zero;
            newCardUI.transform.localRotation = Quaternion.identity;
            dealSeq.Append(newCardUI.transform.DOScale(Vector3.one, 0.08f).SetEase(Ease.OutBack));
        }
        Debug.Log("[PlayerHand] Hand UI spawned.");
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (newMasterClient.IsLocal && isDealingComplete && currentTurnActor != -1)
        {
            ProcessTurn(currentTurnActor);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        if (handAreaTransform != null)
        {
            Gizmos.DrawWireCube(handAreaTransform.position, handAreaTransform.GetComponent<RectTransform>().rect.size);
        }

        if (playerPositions != null)
        {
            Gizmos.color = Color.red;
            foreach (var pos in playerPositions)
            {
                if (pos != null)
                {
                    Gizmos.DrawSphere(pos.position, 10f);
                    Transform fan = pos.Find("CardFan");
                    if (fan != null)
                    {
                        Gizmos.color = Color.blue;
                        Gizmos.DrawWireSphere(fan.position, 20f);
                    }
                }
            }
        }
    }
}