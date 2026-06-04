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
    private Dictionary<int, List<CardData>> humanHandsOnMaster = new Dictionary<int, List<CardData>>(); // Cache for reconnect
    private Dictionary<int, int> masterTrackingCounts = new Dictionary<int, int>();
    public int currentDealBatch = 0;
    private bool _localIsDealingComplete = false;
    public bool IsDealingComplete 
    { 
        get {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("DC", out object complete))
                return (bool)complete || _localIsDealingComplete;
            return _localIsDealingComplete;
        }
        private set {
            _localIsDealingComplete = value;
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "DC", value } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }
        }
    }

    private List<Vector2Int> masterDeck = new List<Vector2Int>();
    private int deckIndex = 0;
    
    private bool _localGameStarted = false;
    private bool gameStarted 
    { 
        get {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object started))
                return (bool)started || _localGameStarted;
            return _localGameStarted;
        }
        set {
            _localGameStarted = value;
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { "GS", value } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }
        }
    }
    private bool isDealCoroutineRunning = false;
    private Coroutine matchmakingCoroutine;

    int CardsPerPlayer => TaashRules.CardsPerPlayer;

    void Awake() 
    {
        if(Instance == null) Instance = this;
        else Destroy(gameObject);
        masterTrackingCounts.Clear();
        humanHandsOnMaster.Clear();
    }

    void Update()
    {
        if (PhotonNetwork.IsMasterClient && gameStarted)
            EnsureInactivePlayersReplacedByBots();
    }

    public bool IsActorBotControlled(int actorNumber) => botActorNumbers.Contains(actorNumber);

    void EnsureInactivePlayersReplacedByBots()
    {
        if (!PhotonNetwork.InRoom || !gameStarted) return;

        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (!p.IsInactive || botActorNumbers.Contains(p.ActorNumber))
                continue;

            EnsureHandCachedForActor(p.ActorNumber);
            Debug.Log($"🤖 Inactive player {p.ActorNumber} — instant bot takeover.");
            photonView.RPC("RPC_MarkPlayerAsBot", RpcTarget.All, p.ActorNumber);
        }
    }

    [PunRPC]
    void RPC_MarkPlayerAsBot(int actorNumber)
    {
        if (botActorNumbers.Contains(actorNumber))
            return;

        botActorNumbers.Add(actorNumber);
        Debug.Log($"🤖 Player {actorNumber} replaced by bot — same seat, hand, and scores preserved.");

        EnsureHandCachedForActor(actorNumber);

        if (humanHandsOnMaster.TryGetValue(actorNumber, out List<CardData> cachedHand))
        {
            botHands[actorNumber] = new List<CardData>(cachedHand);
            humanHandsOnMaster.Remove(actorNumber);
        }
        else if (botHands.TryGetValue(actorNumber, out List<CardData> existingBotHand))
        {
            botHands[actorNumber] = new List<CardData>(existingBotHand);
        }

        if (PhotonNetwork.IsMasterClient)
            SyncBotsToRoom();

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();

        if (PhotonNetwork.IsMasterClient && PlayerHand.LocalInstance != null &&
            PlayerHand.LocalInstance.currentTurnActor == actorNumber)
        {
            PlayerHand.LocalInstance.TriggerBotTurnIfApplicable(actorNumber);
        }
    }

    void EnsureHandCachedForActor(int actorNumber)
    {
        if (humanHandsOnMaster.ContainsKey(actorNumber) && humanHandsOnMaster[actorNumber].Count > 0)
            return;

        if (botHands.TryGetValue(actorNumber, out List<CardData> botHand) && botHand.Count > 0)
            return;

        Player target = null;
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (p.ActorNumber == actorNumber) { target = p; break; }
        }

        if (target != null && TryParseHandProperty(target, out List<CardData> handFromPlayer))
        {
            humanHandsOnMaster[actorNumber] = handFromPlayer;
            return;
        }

        if (PhotonNetwork.CurrentRoom != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("H" + actorNumber, out object roomHandObj))
        {
            humanHandsOnMaster[actorNumber] = ParseInterleavedHand((int[])roomHandObj);
        }

        if (PhotonNetwork.IsMasterClient &&
            humanHandsOnMaster.TryGetValue(actorNumber, out List<CardData> cached) && cached.Count > 0)
        {
            PersistHandToRoom(actorNumber, cached);
        }
    }

    void PersistHandToRoom(int actorNumber, List<CardData> hand)
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || hand == null) return;
        int[] interleaved = new int[hand.Count * 2];
        for (int i = 0; i < hand.Count; i++)
        {
            interleaved[i * 2] = (int)hand[i].cardSuit;
            interleaved[i * 2 + 1] = (int)hand[i].cardRank;
        }
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new ExitGames.Client.Photon.Hashtable { { "H" + actorNumber, interleaved } });
    }

    static bool TryParseHandProperty(Player player, out List<CardData> hand)
    {
        hand = null;
        if (player == null || !player.CustomProperties.TryGetValue("Hand", out object handObj))
            return false;
        hand = ParseInterleavedHand((int[])handObj);
        return hand != null && hand.Count > 0;
    }

    static List<CardData> ParseInterleavedHand(int[] interleaved)
    {
        if (interleaved == null || interleaved.Length < 2)
            return new List<CardData>();

        var hand = new List<CardData>(interleaved.Length / 2);
        for (int i = 0; i < interleaved.Length / 2; i++)
            hand.Add(new CardData { cardSuit = (CardSuit)interleaved[i * 2], cardRank = (CardRank)interleaved[i * 2 + 1] });
        return hand;
    }

    void SyncBotsToRoom()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom) return;
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new ExitGames.Client.Photon.Hashtable { { "BOTS", botActorNumbers.ToArray() } });
    }

    void RestoreBotsFromRoom()
    {
        if (!PhotonNetwork.InRoom) return;
        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("BOTS", out object botsObj))
            return;

        int[] bots = (int[])botsObj;
        botActorNumbers.Clear();
        botActorNumbers.AddRange(bots);
        Debug.Log($"[Sync] Restored {bots.Length} bot seat(s) from room.");
    }

    [PunRPC]
    void RPC_SetGamePaused(bool paused)
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.SetPaused(paused);
    }

    public void ResetMatchState()
    {
        Debug.Log("[DeckManager] Resetting match state for a fresh game.");
        
        // 🚀 Authoritative Reset
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable 
            { 
                { "GS", false },
                { "DC", false },
                { "TP", 0 },
                { "TC", new int[0] },
                { "CTA", -1 },
                { "SW", new int[4] }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log("[DeckManager] Cleared Room Properties for new match.");
        }

        IsDealingComplete = false;
        deckIndex = 0;
        currentDealBatch = 0;
        
        botActorNumbers.Clear();
        botHands.Clear();
        humanHandsOnMaster.Clear();
        masterTrackingCounts.Clear();

        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.ResetHand();
        }
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[DeckManager] OnJoinedRoom. InRoom: {PhotonNetwork.InRoom}, OfflineMode: {PhotonNetwork.OfflineMode}, Master: {PhotonNetwork.IsMasterClient}");
        
        if (gameStarted)
        {
            Debug.Log("[DeckManager] Rejoin Mode Active");
            Debug.Log("[DeckManager] Skipping Match Initialization | Skipping Deal | Skipping Reset");

            RestoreBotsFromRoom();
            
            if (PlayerProfileSync.Instance != null) PlayerProfileSync.Instance.UpdateAllNames();

            // 🚀 STATE RESTORATION PASS
            if (PlayerHand.LocalInstance != null)
            {
                Debug.Log("[Sync] Restoring Table Cards...");
                PlayerHand.LocalInstance.RestoreTableCardsFromRoom();
                
                // Pull hand from properties if it was lost during reconnect
                if (PlayerHand.LocalInstance.myCards.Count == 0)
                {
                    if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("Hand", out object handObj))
                    {
                        Debug.Log("[Sync] Restoring Hand from Player Properties.");
                        int[] interleaved = (int[])handObj;
                        int[] suits = new int[interleaved.Length / 2];
                        int[] ranks = new int[interleaved.Length / 2];
                        for (int i = 0; i < interleaved.Length / 2; i++)
                        {
                            suits[i] = interleaved[i * 2];
                            ranks[i] = interleaved[i * 2 + 1];
                        }
                        PlayerHand.LocalInstance.AssignFullHandLocal(PhotonNetwork.LocalPlayer.ActorNumber, suits, ranks);
                    }
                }
            }

            if (TrumpManager.Instance != null)
            {
                Debug.Log("[Sync] Restoring Trump Suit...");
                TrumpManager.Instance.RefreshFromRoomProperties(false);
            }

            // 🚀 LOCAL RESTORATION: Re-trigger Turn and UI
            if (IsDealingComplete && PlayerHand.LocalInstance != null)
            {
                Debug.Log("[DeckManager] Restoring Dealing Complete State & Unfreezing Turn Logic.");
                int currentActor = PlayerHand.LocalInstance.currentTurnActor;
                
                // Initialize UI and table order
                PlayerHand.LocalInstance.OnDealingComplete(currentActor);
                Debug.Log("[DeckManager] Turn Restored | Card Input Enabled");

                // If Master Client, restart the timer routine
                if (PhotonNetwork.IsMasterClient && TurnManager.Instance != null)
                {
                    Debug.Log("[Sync] Master Client Rejoined: Restarting Turn Timer.");
                    TurnManager.Instance.StartTurn(currentActor);
                    Debug.Log("[DeckManager] Timer Restored");
                }
            }
            
            Debug.Log("[Sync] Rejoin State Sync Complete. Gameplay Resumed Successfully.");
            Debug.Log("[DeckManager] Gameplay Resumed");
            return;
        }

        bool rejoining = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs) && (bool)gs;
        if (!rejoining)
        {
            Debug.Log("[DeckManager] Fresh join — reset match state.");
            ResetMatchState();
        }

        if (TrumpManager.Instance != null)
            TrumpManager.ApplyTrumpForCurrentGameMode(false);

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

    public override void OnLeftRoom()
    {
        Debug.Log("[DeckManager] Left Room. Clearing Match State.");
        ResetMatchState();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (botActorNumbers.Contains(newPlayer.ActorNumber))
        {
            Debug.Log($"[DeckManager] Actor {newPlayer.ActorNumber} reconnected but seat is bot-controlled — match continues as bot.");
            if (PlayerProfileSync.Instance != null)
                PlayerProfileSync.Instance.UpdateAllNames();
            return;
        }

        if (PhotonNetwork.IsMasterClient && gameStarted && IsDealingComplete)
        {
            SyncReconnectingPlayer(newPlayer);
        }
        else if (PhotonNetwork.IsMasterClient)
        {
            OnRoomJoinedCheckStart();
        }
    }

    void SyncReconnectingPlayer(Player p)
    {
        Debug.Log($"[Sync] Master Pushing data to {p.NickName} (Actor {p.ActorNumber}). BotCount: {botActorNumbers.Count}");
        
        // 1. Resend Bot list via non-destructive RPC
        photonView.RPC("RPC_SyncBotsOnly", p, botActorNumbers.ToArray());

        // 2. Resend their specific hand (since this is not in room properties)
        List<CardData> hand = null;
        if (humanHandsOnMaster.TryGetValue(p.ActorNumber, out var cachedHand)) 
        {
            hand = cachedHand;
            Debug.Log($"[Sync] Found cached hand for {p.ActorNumber} on Master. Cards: {hand.Count}");
        }
        else if (p.CustomProperties.TryGetValue("Hand", out object handObj))
        {
            int[] interleaved = (int[])handObj;
            hand = new List<CardData>();
            for (int i = 0; i < interleaved.Length / 2; i++)
                hand.Add(new CardData { cardSuit = (CardSuit)interleaved[i*2], cardRank = (CardRank)interleaved[i*2+1] });
            Debug.Log($"[Sync] Found hand in Player Properties for {p.ActorNumber}. Cards: {hand.Count}");
        }

        if (hand != null)
        {
            int[] suits = new int[hand.Count];
            int[] ranks = new int[hand.Count];
            for (int i = 0; i < hand.Count; i++) { suits[i] = (int)hand[i].cardSuit; ranks[i] = (int)hand[i].cardRank; }
            photonView.RPC("RPC_AssignFullHand", p, p.ActorNumber, suits, ranks);
        }
        else
        {
            Debug.LogWarning($"[Sync] Hand NOT FOUND for reconnecting player {p.ActorNumber}!");
        }

        // 3. Resend timer state
        if (TurnManager.Instance != null && PlayerHand.LocalInstance != null)
        {
            int remaining = (int)typeof(TurnManager).GetField("currentTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(TurnManager.Instance);
            int currentTurnActor = PlayerHand.LocalInstance.currentTurnActor;
            TurnManager.Instance.photonView.RPC("RPC_SyncTimerState", p, currentTurnActor, remaining);
            Debug.Log($"[Sync] Sent Timer State. Current Turn: {currentTurnActor}, Remaining: {remaining}");
        }

        // 4. Resend dealing complete to trigger UI setup
        if (PlayerHand.LocalInstance != null)
        {
            int currentActor = PlayerHand.LocalInstance.currentTurnActor;
            photonView.RPC("RPC_DealingComplete", p, currentActor);
            Debug.Log($"[Sync] Sent Dealing Complete for Actor {currentActor}.");
        }
        
        Debug.Log("[Sync] Rejoin State Sync Complete.");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (gameStarted)
        {
            if (PhotonNetwork.IsMasterClient)
                EnsureHandCachedForActor(otherPlayer.ActorNumber);

            if (otherPlayer.IsInactive)
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    EnsureHandCachedForActor(otherPlayer.ActorNumber);
                    Debug.Log($"⚠️ Player {otherPlayer.ActorNumber} disconnected — instant bot takeover.");
                    photonView.RPC("RPC_MarkPlayerAsBot", RpcTarget.All, otherPlayer.ActorNumber);
                }

                if (PlayerProfileSync.Instance != null)
                    PlayerProfileSync.Instance.UpdateAllNames();
            }
            else if (PhotonNetwork.IsMasterClient)
            {
                Debug.Log($"❌ Player {otherPlayer.ActorNumber} left permanently — bot takeover, match continues.");
                photonView.RPC("RPC_MarkPlayerAsBot", RpcTarget.All, otherPlayer.ActorNumber);
            }
        }
        else if (PhotonNetwork.IsMasterClient && !gameStarted)
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

        if (newMasterClient.IsLocal && gameStarted)
        {
            EnsureInactivePlayersReplacedByBots();

            if (IsDealingComplete && TurnManager.Instance != null && PlayerHand.LocalInstance != null)
                TurnManager.Instance.StartTurn(PlayerHand.LocalInstance.currentTurnActor);
        }
    }

    public void OnRoomJoinedCheckStart()
    {
        if (!PhotonNetwork.InRoom) return;

        // 🚀 REJOIN CHECK: If we already have a game started, don't reset state
        if (gameStarted)
        {
            Debug.Log("[DeckManager] Rejoined existing match. Maintaining state.");
            return;
        }

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

    public void FillBotsAndStart()
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
        humanHandsOnMaster.Clear();

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
    void RPC_SyncBotsOnly(int[] bots)
    {
        Debug.Log($"🤖 [Sync] Bots list synced for reconnect: {bots.Length} bots.");
        botActorNumbers.Clear();
        botActorNumbers.AddRange(bots);

        if (PhotonNetwork.IsMasterClient)
        {
            foreach (int actor in bots)
                EnsureHandCachedForActor(actor);
        }

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();
    }

    [PunRPC]
    public void RPC_ResetAllHands()
    {
        Debug.Log("[DeckManager] RPC_ResetAllHands triggered.");
        if (PhotonNetwork.IsMasterClient)
        {
            masterTrackingCounts.Clear();
            humanHandsOnMaster.Clear();
        }

        if (MatchmakingManager.Instance != null)
        {
            Debug.Log("[DeckManager] Triggering match transition (StopSearching)");
            MatchmakingManager.Instance.StopSearching(true);
        }
        else if (PhotonNetwork.OfflineMode && PhotonNetwork.IsMasterClient)
        {
            Debug.Log("[Bot Mode] No MatchmakingManager — starting deal directly");
            StartFullDealingSequence();
        }

        botHands.Clear();
        currentDealBatch = 0;
        IsDealingComplete = false;
        isDealCoroutineRunning = false;

        if (PlayerHand.LocalInstance != null) PlayerHand.LocalInstance.RPC_ResetHand();
    }

    public void StartFullDealingSequence()
    {
        Debug.Log($"[DeckManager] StartFullDealingSequence. Master: {PhotonNetwork.IsMasterClient}, Running: {isDealCoroutineRunning}, Complete: {IsDealingComplete}");
        if (!PhotonNetwork.IsMasterClient || isDealCoroutineRunning || IsDealingComplete) return;
        StartCoroutine(FullDealingSequenceRoutine());
    }

    IEnumerator FullDealingSequenceRoutine()
    {
        Debug.Log("[DeckManager] FullDealingSequenceRoutine started.");
        isDealCoroutineRunning = true;
        currentDealBatch = 0;
        float initialWait = PhotonNetwork.OfflineMode ? 0.1f : 0.35f;
        yield return new WaitForSeconds(initialWait);

        int[] dealBatches = TaashRules.GetDealAnimationBatches();
        string taashLabel = TaashRules.IsTwoTaashMode ? "2 Taash (10-8-8)" : "1 Taash (5-4-4)";
        const float pauseBetweenRounds = 0.02f; // Reduced from 0.06f

        for (int batch = 0; batch < dealBatches.Length; batch++)
        {
            currentDealBatch = batch + 1;
            int cardsThisBatch = dealBatches[batch];
            Debug.Log($"[DeckManager] Deal round {currentDealBatch}/{dealBatches.Length} — {cardsThisBatch} cards per player ({taashLabel})");
            photonView.RPC("RPC_PlayDealAnimation", RpcTarget.All, cardsThisBatch);

            yield return new WaitForSeconds(PlayerHand.GetDealBatchDuration(cardsThisBatch));

            if (batch < dealBatches.Length - 1)
                yield return new WaitForSeconds(pauseBetweenRounds);
        }

        Debug.Log("[DeckManager] Animations finished. Distributing cards...");
        BuildAndShuffleDeck();
        DistributeAllHandsInternal();

        if (ValidateAllHands())
        {
            IsDealingComplete = true;
            
            List<int> allActors = new List<int>();
            foreach (Player p in PhotonNetwork.PlayerList) allActors.Add(p.ActorNumber);
            allActors.AddRange(botActorNumbers);
            allActors.Sort();
            
            int myIdx = allActors.IndexOf(PhotonNetwork.LocalPlayer.ActorNumber);
            int starterActor = allActors[(myIdx + 3) % allActors.Count];

            yield return new WaitForSeconds(0.15f);
            Debug.Log($"[DeckManager] Dealing Complete. Starter: {starterActor}");
            photonView.RPC("RPC_DealingComplete", RpcTarget.All, starterActor);
        }
        else
        {
            Debug.LogError($"[DeckManager] Hand validation failed! Expected {CardsPerPlayer} cards per player.");
        }
        isDealCoroutineRunning = false;
    }

    void BuildAndShuffleDeck()
    {
        masterDeck.Clear();
        deckIndex = 0;
        int deckCount = TaashRules.IsTwoTaashMode ? 2 : 1;
        Debug.Log($"[DeckManager] Building deck with {deckCount} pack(s). Total: {deckCount * 52} cards.");
        for (int d = 0; d < deckCount; d++)
            for (int s = 0; s < 4; s++)
                for (int r = 0; r < 13; r++)
                    masterDeck.Add(new Vector2Int(s, r));
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
        humanHandsOnMaster.Clear();
        
        bool isThirteenthMode = GameSettings.Instance != null && GameSettings.Instance.currentMode == GameModeType.ThirteenthCardTrump;
        CardSuit thirteenthTrump = CardSuit.Spades;

        int playerIdx = 0;
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            int cardsPerPlayer = CardsPerPlayer;
            List<CardData> hand = DrawCards(cardsPerPlayer);
            
            if (isThirteenthMode && playerIdx == 0)
            {
                thirteenthTrump = hand[cardsPerPlayer - 1].cardSuit;
                Debug.Log($"[Mode 2] 13th Card is {hand[cardsPerPlayer - 1].cardRank} of {thirteenthTrump}. Setting as Trump.");
            }

            int[] suits = new int[cardsPerPlayer];
            int[] ranks = new int[cardsPerPlayer];
            for (int i = 0; i < cardsPerPlayer; i++) { suits[i] = (int)hand[i].cardSuit; ranks[i] = (int)hand[i].cardRank; }
            masterTrackingCounts[player.ActorNumber] = cardsPerPlayer;
            humanHandsOnMaster[player.ActorNumber] = new List<CardData>(hand); // Cache
            photonView.RPC("RPC_AssignFullHand", RpcTarget.All, player.ActorNumber, suits, ranks);
            playerIdx++;
        }
        
        foreach (int botActor in botActorNumbers)
        {
            int cardsPerPlayer = CardsPerPlayer;
            List<CardData> hand = DrawCards(cardsPerPlayer);
            
            if (isThirteenthMode && playerIdx == 0)
            {
                thirteenthTrump = hand[cardsPerPlayer - 1].cardSuit;
                Debug.Log($"[Mode 2] (Offline) 13th Card is {hand[cardsPerPlayer - 1].cardRank} of {thirteenthTrump}. Setting as Trump.");
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

    public void UpdateCachedHandOnMaster(int actorNum, CardSuit suit, CardRank rank)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (humanHandsOnMaster.TryGetValue(actorNum, out List<CardData> hand))
        {
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i].cardSuit == suit && hand[i].cardRank == rank)
                {
                    hand.RemoveAt(i);
                    break;
                }
            }
            masterTrackingCounts[actorNum] = hand.Count;

            // 🚀 Update Player Property too
            Player target = null;
            foreach (var p in PhotonNetwork.PlayerList) if (p.ActorNumber == actorNum) { target = p; break; }
            
            if (target != null)
            {
                int[] interleaved = new int[hand.Count * 2];
                for (int i = 0; i < hand.Count; i++) { interleaved[i*2] = (int)hand[i].cardSuit; interleaved[i*2+1] = (int)hand[i].cardRank; }
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
                props.Add("Hand", interleaved);
                target.SetCustomProperties(props);
            }
        }
    }

    bool ValidateAllHands()
    {
        int expected = CardsPerPlayer;
        foreach (var entry in masterTrackingCounts)
            if (entry.Value != expected) return false;
        return true;
    }

    [PunRPC]
    void RPC_DealingComplete(int starterActor)
    {
        IsDealingComplete = true;
        gameStarted = true;
        if (TrumpManager.Instance != null)
            TrumpManager.Instance.RefreshFromRoomProperties(false);
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