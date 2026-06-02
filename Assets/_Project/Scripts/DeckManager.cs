using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;

public class DeckManager : MonoBehaviourPunCallbacks
{
    public static DeckManager Instance;

    [Header("Game Settings")]
    public int minPlayersToStartOnline = 4;
    public int requiredPlayersToStart = 4; 
    public float matchmakingTimeout = 20f; 

    [Header("Bot Tracking")]
    public static List<int> botActorNumbers = new List<int>();
    public Dictionary<int, List<CardData>> botHands = new Dictionary<int, List<CardData>>();
    private Dictionary<int, int> masterTrackingCounts = new Dictionary<int, int>();

    public int currentDealBatch = 0;
    public bool IsDealingComplete { get; private set; }

    private List<Vector2Int> masterDeck = new List<Vector2Int>();
    private int deckIndex = 0;
    
    private bool gameStarted = false;
    private bool isDealCoroutineRunning = false;
    private Coroutine matchmakingCoroutine;

    private static readonly int[] DealAnimationBatches = { 5, 4, 4 };
    private const int MaxCardsPerPlayer = 13;

    void Awake() 
    {
        if(Instance == null) Instance = this;
        else Destroy(gameObject);
        masterTrackingCounts.Clear();
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[DeckManager] OnJoinedRoom. InRoom: {PhotonNetwork.InRoom}, OfflineMode: {PhotonNetwork.OfflineMode}, Master: {PhotonNetwork.IsMasterClient}");
        
        if (TrumpManager.Instance != null)
        {
            TrumpManager.Instance.SetTrumpSuit(CardSuit.Spades, false);
        }

        botActorNumbers.Clear();
        botHands.Clear();
        masterTrackingCounts.Clear();
        gameStarted = false;
        currentDealBatch = 0;
        IsDealingComplete = false;
        deckIndex = 0;

        if (PlayerHand.LocalInstance != null)
        {
            Debug.Log("[DeckManager] Resetting local hand.");
            PlayerHand.LocalInstance.ResetHand();
        }
        else
        {
            Debug.LogWarning("[DeckManager] PlayerHand.LocalInstance is NULL during OnJoinedRoom!");
        }

        if (PhotonNetwork.IsMasterClient) 
        {
            if (matchmakingCoroutine != null) StopCoroutine(matchmakingCoroutine);

            if (PhotonNetwork.OfflineMode)
            {
                Debug.Log("🤖 [Bot Mode] Offline Mode Detected. Starting Match with 3 Bots Instantly.");
                FillBotsAndStart();
            }
            else
            {
                OnRoomJoinedCheckStart();
            }
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient && gameStarted && IsDealingComplete)
        {
            // Sync logic could go here
        }
        else if (PhotonNetwork.IsMasterClient)
        {
            OnRoomJoinedCheckStart();
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (PhotonNetwork.IsMasterClient && !gameStarted)
        {
            OnRoomJoinedCheckStart();
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (newMasterClient.IsLocal && !gameStarted)
        {
            OnRoomJoinedCheckStart();
        }

        if (newMasterClient.IsLocal && gameStarted && IsDealingComplete)
        {
            if (TurnManager.Instance != null && PlayerHand.LocalInstance != null)
            {
                TurnManager.Instance.StartTurn(PlayerHand.LocalInstance.currentTurnActor);
            }
        }
    }

    public void OnRoomJoinedCheckStart()
    {
        if (!PhotonNetwork.InRoom || gameStarted) return;

        if (PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.IsMasterClient) FillBotsAndStart();
            return;
        }

        if (!PhotonNetwork.IsMasterClient) return;

        int humanCount = PhotonNetwork.CurrentRoom.PlayerCount;
        if (humanCount >= requiredPlayersToStart)
        {
            if (matchmakingCoroutine != null) { StopCoroutine(matchmakingCoroutine); matchmakingCoroutine = null; }
            FillBotsAndStart();
        }
        else if (matchmakingCoroutine == null)
        {
            matchmakingCoroutine = StartCoroutine(WaitForOpponentRoutine());
        }
    }

    IEnumerator WaitForOpponentRoutine()
    {
        float timer = matchmakingTimeout;
        while (timer > 0 && !gameStarted && PhotonNetwork.InRoom)
        {
            int currentPlayers = PhotonNetwork.CurrentRoom.PlayerCount;
            
            // Sync status to all clients
            photonView.RPC("RPC_UpdateMatchmakingUI", RpcTarget.All, currentPlayers, (int)timer);

            if (currentPlayers >= requiredPlayersToStart)
            {
                FillBotsAndStart();
                yield break;
            }
            yield return new WaitForSeconds(1f);
            timer--;
        }

        if (!gameStarted && PhotonNetwork.InRoom)
        {
            Debug.Log($"⏰ Matchmaking Timeout! Filling bots for {PhotonNetwork.CurrentRoom.PlayerCount} players.");
            FillBotsAndStart();
        }
        matchmakingCoroutine = null;
    }

    [PunRPC]
    void RPC_UpdateMatchmakingUI(int playersFound, int countdown)
    {
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.UpdateMatchmakingStatus(playersFound, countdown);
        }
    }

    void FillBotsAndStart()
    {
        if (gameStarted)
        {
            Debug.LogWarning("[DeckManager] FillBotsAndStart called but game already started.");
            return;
        }
        
        Debug.Log($"🤖 [Bot Mode] Filling bots. Current Player Count: {PhotonNetwork.CurrentRoom.PlayerCount}");
        gameStarted = true;
        PhotonNetwork.CurrentRoom.IsOpen = false;

        masterTrackingCounts.Clear();
        botHands.Clear();

        int botsNeeded = 4 - PhotonNetwork.CurrentRoom.PlayerCount; 
        botActorNumbers.Clear();
        for (int i = 0; i < botsNeeded; i++) 
        {
            int botID = 100 + i;
            botActorNumbers.Add(botID);
            Debug.Log($"🤖 [Bot Mode] Bot Created: Actor {botID}");
        }

        Debug.Log("🤖 [Bot Mode] Initializing match via RPC...");
        photonView.RPC("RPC_InitializeMatch", RpcTarget.All, botActorNumbers.ToArray());

        if (PlayerProfileSync.Instance != null && botActorNumbers.Count > 0)
        {
            Debug.Log("🤖 [Bot Mode] Updating PlayerProfileSync.");
            PlayerProfileSync.Instance.ShowBotNames(botActorNumbers);
        }

        if (ModeManager.Instance != null && ModeManager.Instance.panelModes != null)
            ModeManager.Instance.panelModes.SetActive(false);

        deckIndex = 0;
        IsDealingComplete = false;

        if (PhotonNetwork.OfflineMode)
        {
            Debug.Log("🤖 [Bot Mode] Triggering dealing sequence...");
            StartFullDealingSequence();
        }
    }

    [PunRPC]
    void RPC_InitializeMatch(int[] bots)
    {
        Debug.Log($"🤖 [Bot Mode] RPC Match initialized with {bots.Length} bots.");
        botActorNumbers.Clear();
        botActorNumbers.AddRange(bots);
        RPC_ResetAllHands();
    }

    [PunRPC]
    public void RPC_ResetAllHands()
    {
        if (PhotonNetwork.IsMasterClient) masterTrackingCounts.Clear();

        if (MatchmakingManager.Instance != null && !PhotonNetwork.OfflineMode)
        {
            MatchmakingManager.Instance.StopSearching(true);
        }

        botHands.Clear();
        currentDealBatch = 0;
        IsDealingComplete = false;
        isDealCoroutineRunning = false;

        if (PlayerHand.LocalInstance != null) PlayerHand.LocalInstance.RPC_ResetHand();
    }

    public void StartFullDealingSequence()
    {
        if (!PhotonNetwork.IsMasterClient || isDealCoroutineRunning || IsDealingComplete) return;
        StartCoroutine(FullDealingSequenceRoutine());
    }

    IEnumerator FullDealingSequenceRoutine()
    {
        isDealCoroutineRunning = true;
        currentDealBatch = 0;
        float initialWait = PhotonNetwork.OfflineMode ? 0.1f : 0.5f;
        yield return new WaitForSeconds(initialWait);

        for (int batch = 0; batch < DealAnimationBatches.Length; batch++)
        {
            currentDealBatch = batch + 1;
            int cardsThisBatch = DealAnimationBatches[batch];
            photonView.RPC("RPC_PlayDealAnimation", RpcTarget.All, cardsThisBatch);
            float animationBuffer = (4 * (cardsThisBatch * 0.05f + 0.1f)) + 0.8f;
            yield return new WaitForSeconds(animationBuffer);
        }

        BuildAndShuffleDeck();
        DistributeAllHandsInternal();

        if (ValidateAllHandsHave13())
        {
            IsDealingComplete = true;
            int firstLeadActor = PhotonNetwork.PlayerList.Length > 0 ? PhotonNetwork.PlayerList[0].ActorNumber : PhotonNetwork.LocalPlayer.ActorNumber;
            yield return new WaitForSeconds(0.15f);
            photonView.RPC("RPC_DealingComplete", RpcTarget.All, firstLeadActor);
        }
        isDealCoroutineRunning = false;
    }

    void BuildAndShuffleDeck()
    {
        masterDeck.Clear();
        deckIndex = 0;
        for (int s = 0; s < 4; s++) for (int r = 0; r < 13; r++) masterDeck.Add(new Vector2Int(s, r));
        for (int i = 0; i < masterDeck.Count; i++)
        {
            Vector2Int temp = masterDeck[i];
            int randomIndex = Random.Range(i, masterDeck.Count);
            masterDeck[i] = masterDeck[randomIndex];
            masterDeck[randomIndex] = temp;
        }
    }

    List<CardData> DrawCards(int count)
    {
        var drawn = new List<CardData>(count);
        for (int i = 0; i < count; i++)
        {
            if (deckIndex >= masterDeck.Count) break;
            Vector2Int c = masterDeck[deckIndex++];
            drawn.Add(new CardData { cardSuit = (CardSuit)c.x, cardRank = (CardRank)c.y });
        }
        return drawn;
    }

    void DistributeAllHandsInternal()
    {
        masterTrackingCounts.Clear();
        botHands.Clear();
        
        bool isThirteenthMode = GameSettings.Instance != null && GameSettings.Instance.currentMode == GameModeType.ThirteenthCardTrump;
        CardSuit thirteenthTrump = CardSuit.Spades;

        int playerIdx = 0;
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            List<CardData> hand = DrawCards(MaxCardsPerPlayer);
            
            // Mode 2: 13th Card Trump
            if (isThirteenthMode && playerIdx == 0)
            {
                thirteenthTrump = hand[MaxCardsPerPlayer - 1].cardSuit;
                Debug.Log($"[Mode 2] 13th Card is {hand[MaxCardsPerPlayer - 1].cardRank} of {thirteenthTrump}. Setting as Trump.");
            }

            int[] suits = new int[MaxCardsPerPlayer];
            int[] ranks = new int[MaxCardsPerPlayer];
            for (int i = 0; i < MaxCardsPerPlayer; i++) { suits[i] = (int)hand[i].cardSuit; ranks[i] = (int)hand[i].cardRank; }
            masterTrackingCounts[player.ActorNumber] = MaxCardsPerPlayer;
            photonView.RPC("RPC_AssignFullHand", RpcTarget.All, player.ActorNumber, suits, ranks);
            playerIdx++;
        }
        
        foreach (int botActor in botActorNumbers)
        {
            List<CardData> hand = DrawCards(MaxCardsPerPlayer);
            
            // If no human player received cards first (e.g. offline mode), first bot's 13th card
            if (isThirteenthMode && playerIdx == 0)
            {
                thirteenthTrump = hand[MaxCardsPerPlayer - 1].cardSuit;
                Debug.Log($"[Mode 2] (Offline) 13th Card is {hand[MaxCardsPerPlayer - 1].cardRank} of {thirteenthTrump}. Setting as Trump.");
            }

            botHands[botActor] = hand;
            masterTrackingCounts[botActor] = hand.Count;
            playerIdx++;
        }

        if (isThirteenthMode && TrumpManager.Instance != null)
        {
            TrumpManager.Instance.SyncTrumpSuit(thirteenthTrump, true);
        }
    }

    bool ValidateAllHandsHave13()
    {
        foreach (var entry in masterTrackingCounts) if (entry.Value != MaxCardsPerPlayer) return false;
        return true;
    }

    [PunRPC]
    void RPC_DealingComplete(int starterActor)
    {
        if (PlayerHand.LocalInstance != null) PlayerHand.LocalInstance.OnDealingComplete(starterActor);
    }

    [PunRPC]
    public void RPC_PlayDealAnimation(int cardsInBatch)
    {
        if (PlayerHand.LocalInstance != null) PlayerHand.LocalInstance.PlayDealAnimationOnly(cardsInBatch);
    }

    [PunRPC]
    public void RPC_AssignFullHand(int targetActor, int[] suitIndices, int[] rankIndices)
    {
        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.AssignFullHandLocal(targetActor, suitIndices, rankIndices);
            if (botActorNumbers.Contains(targetActor))
            {
                Debug.Log($"🤖 [Bot Mode] Bot Received Cards: Actor {targetActor}");
            }
        }
    }
}