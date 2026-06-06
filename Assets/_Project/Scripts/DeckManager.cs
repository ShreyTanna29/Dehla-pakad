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

    public const int MaxTableSeats = 4;
    private const int PhantomBotActorBase = 100;

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

    public bool IsActorBotControlled(int actorNumber) => botActorNumbers.Contains(actorNumber);

    public static int GetActiveHumanPlayerCount()
    {
        if (!PhotonNetwork.InRoom)
            return PhotonNetwork.OfflineMode ? 1 : 0;

        int count = 0;
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            if (!p.IsInactive)
                count++;
        }
        return count;
    }

    /// <summary>Real (active) humans in room. Bots are not counted.</summary>
    public static int GetRealPlayerCountInRoom()
    {
        int count = GetActiveHumanPlayerCount();
        if (count < 1 && PhotonNetwork.InRoom)
            count = 1;
        return count;
    }

    /// <summary>Phantom bots needed so real players + bots = 4.</summary>
    public static int GetRequiredPhantomBotCount()
    {
        return Mathf.Max(0, MaxTableSeats - GetRealPlayerCountInRoom());
    }

    public static bool IsPhantomBotActor(int actorNumber) => actorNumber >= PhantomBotActorBase;

    public List<int> BuildActiveSeatList()
    {
        var seats = new List<int>(MaxTableSeats);
        var used = new HashSet<int>();

        if (PhotonNetwork.InRoom)
        {
            foreach (Player p in PhotonNetwork.PlayerList)
            {
                if (p.IsInactive) continue;
                if (used.Add(p.ActorNumber))
                {
                    seats.Add(p.ActorNumber);
                }
            }

            foreach (int botActor in botActorNumbers)
            {
                if (used.Contains(botActor)) continue;
                if (seats.Count >= MaxTableSeats) break;

                if (used.Add(botActor))
                {
                    seats.Add(botActor);
                }
            }

            // Fallback: If still under 4, add more phantom bots
            while (seats.Count < MaxTableSeats)
            {
                int nextBotId = PhantomBotActorBase + seats.Count; // Simple unique ID strategy
                while (used.Contains(nextBotId)) nextBotId++;
                if (used.Add(nextBotId))
                {
                    seats.Add(nextBotId);
                    if (!botActorNumbers.Contains(nextBotId))
                        botActorNumbers.Add(nextBotId);
                }
            }
        }
        else if (PhotonNetwork.OfflineMode)
        {
            used.Add(PhotonNetwork.LocalPlayer.ActorNumber);
            seats.Add(PhotonNetwork.LocalPlayer.ActorNumber);
            foreach (int botActor in botActorNumbers)
            {
                if (used.Add(botActor))
                    seats.Add(botActor);
            }
            while (seats.Count < MaxTableSeats)
            {
                 int nextBotId = PhantomBotActorBase + seats.Count;
                 while (used.Contains(nextBotId)) nextBotId++;
                 if (used.Add(nextBotId))
                 {
                    seats.Add(nextBotId);
                    if (!botActorNumbers.Contains(nextBotId))
                        botActorNumbers.Add(nextBotId);
                 }
            }
        }

        Debug.Log($"[MULTIPLAYER SEATS] Real={GetActiveHumanPlayerCount()}, Bots={botActorNumbers.Count}, Total={seats.Count}");
        return seats;
    }

    public List<int> GetActiveSeatActorsSorted()
    {
        List<int> seats = BuildActiveSeatList();
        seats.Sort();
        return seats;
    }

    public bool IsActiveSeatActor(int actorNumber) => BuildActiveSeatList().Contains(actorNumber);

    public bool TryGetHumanHandOnMaster(int actorNumber, out List<CardData> hand)
    {
        hand = null;
        return humanHandsOnMaster.TryGetValue(actorNumber, out hand) && hand != null && hand.Count > 0;
    }

    void LogSeatDiagnostics(int activeHumans, List<int> seats)
    {
        string actorList = seats.Count > 0 ? string.Join(", ", seats) : "(empty)";
        string botList = botActorNumbers.Count > 0 ? string.Join(", ", botActorNumbers) : "(empty)";

        Debug.Log(
            $"[Seat Validation]\n" +
            $"Real Player Count: {activeHumans}\n" +
            $"Bot Count: {botActorNumbers.Count}\n" +
            $"Total Seat Count: {seats.Count}\n" +
            $"Actor List: {actorList}\n" +
            $"Bot Actor List: {botList}");

        if (seats.Count != MaxTableSeats)
            Debug.LogError($"[Seat Validation] Total Seat Count != {MaxTableSeats}. Match must not start until fixed.");
    }

    /// <summary>Clear old bots, add exactly (4 - realPlayerCount) phantom bots for empty seats.</summary>
    bool AssignBotsToFillEmptySeats(out List<int> seats, out int realPlayerCount)
    {
        realPlayerCount = GetRealPlayerCountInRoom();
        int requiredBots = Mathf.Max(0, MaxTableSeats - realPlayerCount);

        botActorNumbers.Clear();
        botHands.Clear();
        RemoveUnusedPhantomBotHands();

        for (int i = 0; i < requiredBots; i++)
        {
            int botID = PhantomBotActorBase + i;
            if (!botActorNumbers.Contains(botID))
                botActorNumbers.Add(botID);
        }

        Debug.Log($"[BotFill] Real players={realPlayerCount} | requiredBots={requiredBots} | formula: {MaxTableSeats}-{realPlayerCount}");

        seats = BuildActiveSeatList();
        LogSeatDiagnostics(realPlayerCount, seats);

        if (seats.Count == MaxTableSeats)
            return true;

        TrimExcessPhantomBots();
        while (botActorNumbers.Count < requiredBots)
        {
            int nextId = PhantomBotActorBase + botActorNumbers.Count;
            if (!botActorNumbers.Contains(nextId))
                botActorNumbers.Add(nextId);
        }

        seats = BuildActiveSeatList();
        LogSeatDiagnostics(realPlayerCount, seats);
        return seats.Count == MaxTableSeats;
    }

    void RemoveUnusedPhantomBotHands()
    {
        var toRemove = new List<int>();
        foreach (int key in botHands.Keys)
        {
            if (IsPhantomBotActor(key) && !botActorNumbers.Contains(key))
                toRemove.Add(key);
        }
        foreach (int key in toRemove)
            botHands.Remove(key);
    }

    void RemovePhantomBotsWhileOverSeatCap()
    {
        while (BuildActiveSeatList().Count > MaxTableSeats)
        {
            int removed = -1;
            for (int id = PhantomBotActorBase + MaxTableSeats - 1; id >= PhantomBotActorBase; id--)
            {
                if (!botActorNumbers.Contains(id)) continue;
                removed = id;
                botActorNumbers.Remove(id);
                botHands.Remove(id);
                break;
            }
            if (removed < 0) break;
            Debug.LogWarning($"[BotFill] Trimmed phantom bot {removed} — keeping 4 seats after disconnect takeover.");
        }
    }

    void TrimExcessPhantomBots() => RemovePhantomBotsWhileOverSeatCap();

    void ReconcileExcessPhantomBots()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!gameStarted) return;

        int realPlayers = GetRealPlayerCountInRoom();
        int takeoverBots = 0;
        foreach (int b in botActorNumbers)
        {
            if (IsPhantomBotActor(b)) continue;
            takeoverBots++;
        }

        int maxPhantoms = Mathf.Max(0, MaxTableSeats - realPlayers - takeoverBots);
        var phantomsToRemove = new List<int>();
        int phantomIndex = 0;
        foreach (int b in botActorNumbers)
        {
            if (!IsPhantomBotActor(b)) continue;
            if (phantomIndex >= maxPhantoms)
                phantomsToRemove.Add(b);
            phantomIndex++;
        }
        foreach (int id in phantomsToRemove)
        {
            botActorNumbers.Remove(id);
            botHands.Remove(id);
            Debug.Log($"[BotFill] Removed excess phantom bot {id} (seat taken by human/takeover).");
        }

        TrimExcessPhantomBots();
        DedupeBotActorNumbers();
        SyncBotsToRoom();
    }

    void DedupeBotActorNumbers()
    {
        var unique = new List<int>();
        foreach (int botActor in botActorNumbers)
        {
            if (!unique.Contains(botActor))
                unique.Add(botActor);
        }
        botActorNumbers.Clear();
        botActorNumbers.AddRange(unique);
    }

    static void ApplySyncedBotActorList(int[] bots)
    {
        botActorNumbers.Clear();
        if (bots == null) return;
        foreach (int botActor in bots)
        {
            if (!botActorNumbers.Contains(botActor))
                botActorNumbers.Add(botActor);
        }
    }

    [PunRPC]
    void RPC_MarkPlayerAsBot(int actorNumber)
    {
        if (botActorNumbers.Contains(actorNumber))
            return;

        botActorNumbers.Add(actorNumber);
        DedupeBotActorNumbers();

        if (!IsPhantomBotActor(actorNumber)) RemovePhantomBotsWhileOverSeatCap();

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
        {
            ReconcileExcessPhantomBots();
            if (botHands.TryGetValue(actorNumber, out List<CardData> botH))
                PersistHandToRoom(actorNumber, botH);
        }

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
        if (humanHandsOnMaster.ContainsKey(actorNumber) && humanHandsOnMaster[actorNumber].Count > 0) return;
        if (botHands.TryGetValue(actorNumber, out List<CardData> botHand) && botHand.Count > 0) return;

        if (PhotonNetwork.CurrentRoom != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("H" + actorNumber, out object roomHandObj))
        {
            humanHandsOnMaster[actorNumber] = ParseInterleavedHand((int[])roomHandObj);
            if (IsActorBotControlled(actorNumber))
                botHands[actorNumber] = new List<CardData>(humanHandsOnMaster[actorNumber]);
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
        if (player == null || !player.CustomProperties.TryGetValue("Hand", out object handObj)) return false;
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
        ApplySyncedBotActorList(bots);
        if (PhotonNetwork.IsMasterClient)
            ReconcileExcessPhantomBots();
        Debug.Log($"[Sync] Restored {botActorNumbers.Count} bot seat(s) from room.");
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
        if (gameStarted)
        {
            StartCoroutine(RejoinStateRoutine());
            return;
        }

        bool rejoining = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs) && (bool)gs;
        if (!rejoining) ResetMatchState();
        if (TrumpManager.Instance != null) TrumpManager.ApplyTrumpForCurrentGameMode(false);

        if (PhotonNetwork.IsMasterClient)
        {
            if (matchmakingCoroutine != null) StopCoroutine(matchmakingCoroutine);
            if (PhotonNetwork.OfflineMode) FillBotsAndStart();
            else OnRoomJoinedCheckStart();
        }
    }

    IEnumerator RejoinStateRoutine()
    {
        RestoreBotsFromRoom();
        if (PlayerProfileSync.Instance != null) PlayerProfileSync.Instance.UpdateAllNames();

        float timeout = 2.0f;
        while (PlayerHand.LocalInstance == null && timeout > 0)
        {
            yield return null;
            timeout -= Time.deltaTime;
        }

        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.RestoreTableCardsFromRoom();

            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("H" + PhotonNetwork.LocalPlayer.ActorNumber, out object handObj))
            {
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

            if (PhotonNetwork.IsMasterClient)
            {
                foreach (Player p in PhotonNetwork.PlayerList) EnsureHandCachedForActor(p.ActorNumber);
                foreach (int bot in botActorNumbers) EnsureHandCachedForActor(bot);
            }
        }

        if (TrumpManager.Instance != null) TrumpManager.Instance.RefreshFromRoomProperties(false);

        yield return new WaitForSeconds(0.6f);

        if (PhotonNetwork.IsMasterClient && IsDealingComplete && PlayerHand.LocalInstance != null)
        {
            int currentActor = PlayerHand.LocalInstance.currentTurnActor;
            PlayerHand.LocalInstance.OnDealingComplete(currentActor);
            if (TurnManager.Instance != null) TurnManager.Instance.StartTurn(currentActor);
        }
    }

    public override void OnLeftRoom() { ResetMatchState(); }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (botActorNumbers.Contains(newPlayer.ActorNumber))
        {
            if (PhotonNetwork.IsMasterClient)
            {
                botActorNumbers.Remove(newPlayer.ActorNumber);
                SyncBotsToRoom();

                if (botHands.TryGetValue(newPlayer.ActorNumber, out List<CardData> bHand))
                {
                    humanHandsOnMaster[newPlayer.ActorNumber] = new List<CardData>(bHand);
                    botHands.Remove(newPlayer.ActorNumber);
                }

                if (gameStarted && IsDealingComplete)
                    SyncReconnectingPlayer(newPlayer);
            }
            if (PlayerProfileSync.Instance != null)
                PlayerProfileSync.Instance.UpdateAllNames();
            return;
        }

        if (PhotonNetwork.IsMasterClient && gameStarted && IsDealingComplete)
            SyncReconnectingPlayer(newPlayer);
        else if (PhotonNetwork.IsMasterClient)
            OnRoomJoinedCheckStart();
    }

    void SyncReconnectingPlayer(Player p)
    {
        photonView.RPC("RPC_SyncBotsOnly", p, botActorNumbers.ToArray());

        EnsureHandCachedForActor(p.ActorNumber);

        if (humanHandsOnMaster.TryGetValue(p.ActorNumber, out List<CardData> hand) && hand != null)
        {
            int[] suits = new int[hand.Count];
            int[] ranks = new int[hand.Count];
            for (int i = 0; i < hand.Count; i++)
            {
                suits[i] = (int)hand[i].cardSuit;
                ranks[i] = (int)hand[i].cardRank;
            }
            photonView.RPC("RPC_AssignFullHand", p, p.ActorNumber, suits, ranks);
        }

        if (TurnManager.Instance != null && PlayerHand.LocalInstance != null)
        {
            int remaining = (int)typeof(TurnManager).GetField("currentTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(TurnManager.Instance);
            int currentTurnActor = PlayerHand.LocalInstance.currentTurnActor;
            TurnManager.Instance.photonView.RPC("RPC_SyncTimerState", p, currentTurnActor, remaining);
        }

        if (PlayerHand.LocalInstance != null)
        {
            int currentActor = PlayerHand.LocalInstance.currentTurnActor;
            photonView.RPC("RPC_DealingComplete", p, currentActor);
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (gameStarted)
        {
            if (PhotonNetwork.IsMasterClient)
                EnsureHandCachedForActor(otherPlayer.ActorNumber);

            if (otherPlayer.IsInactive)
            {
                if (PlayerProfileSync.Instance != null)
                    PlayerProfileSync.Instance.UpdateAllNames();
            }
            else if (PhotonNetwork.IsMasterClient)
            {
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
            if (IsDealingComplete && TurnManager.Instance != null && PlayerHand.LocalInstance != null)
            {
                int currentTurn = PlayerHand.LocalInstance.currentTurnActor;
                TurnManager.Instance.StartTurn(currentTurn);

                if (IsActorBotControlled(currentTurn))
                    PlayerHand.LocalInstance.TriggerBotTurnIfApplicable(currentTurn);
            }
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

        int humanCount = GetRealPlayerCountInRoom();
        if (humanCount >= MaxTableSeats)
        {
            if (matchmakingCoroutine != null) { StopCoroutine(matchmakingCoroutine); matchmakingCoroutine = null; }
            Debug.Log($"[BotFill] Lobby full ({humanCount} real players) — starting with 0 bots.");
            FillBotsAndStart();
        }
        else if (matchmakingCoroutine == null)
        {
            Debug.Log($"[BotFill] Matchmaking wait started — {humanCount} real player(s), need {MaxTableSeats - humanCount} bot(s) after timeout.");
            matchmakingCoroutine = StartCoroutine(WaitForOpponentRoutine());
        }
    }

    IEnumerator WaitForOpponentRoutine()
    {
        float timer = matchmakingTimeout;
        while (timer > 0 && !gameStarted && PhotonNetwork.InRoom)
        {
            int currentPlayers = GetRealPlayerCountInRoom();
            int botsIfStartNow = Mathf.Max(0, MaxTableSeats - currentPlayers);

            photonView.RPC("RPC_UpdateMatchmakingUI", RpcTarget.All, currentPlayers, (int)timer);

            if (currentPlayers >= MaxTableSeats)
            {
                Debug.Log($"[BotFill] {currentPlayers} real players joined — starting with 0 bots.");
                FillBotsAndStart();
                yield break;
            }
            yield return new WaitForSeconds(1f);
            timer--;
        }

        if (!gameStarted && PhotonNetwork.InRoom)
        {
            int realPlayers = GetRealPlayerCountInRoom();
            int requiredBots = Mathf.Max(0, MaxTableSeats - realPlayers);
            Debug.Log($"[BotFill] Matchmaking timeout — realPlayers={realPlayers}, adding {requiredBots} bot(s), total seats=4.");
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

        if (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            Debug.LogError("[BotFill] FillBotsAndStart aborted — not in a room.");
            return;
        }

        if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.OfflineMode)
        {
            Debug.LogWarning("[BotFill] FillBotsAndStart ignored on non-master client.");
            return;
        }

        masterTrackingCounts.Clear();
        humanHandsOnMaster.Clear();

        if (!AssignBotsToFillEmptySeats(out List<int> seats, out int realPlayers))
        {
            Debug.LogError($"[BotFill] Aborted — could not build 4 seats (real={realPlayers}, bots={botActorNumbers.Count}, seats={seats?.Count ?? 0}).");
            return;
        }

        GameStabilityAudit.ValidateSeatCountForMatchStart();

        Debug.Log($"[Matchmaking] Real players: {realPlayers}");
        Debug.Log($"[Matchmaking] Bots added: {botActorNumbers.Count} [{string.Join(", ", botActorNumbers)}]");
        Debug.Log($"[Matchmaking] Total seats: {seats.Count}");
        Debug.Log($"[BotFill] Match ready — realPlayers={realPlayers}, bots={botActorNumbers.Count}, totalSeats={seats.Count}.");
        gameStarted = true;
        if (PhotonNetwork.InRoom)
            PhotonNetwork.CurrentRoom.IsOpen = false;

        if (PhotonNetwork.IsMasterClient)
            SyncBotsToRoom();

        if (photonView == null)
        {
            Debug.LogError("[BotFill] photonView missing — cannot RPC_InitializeMatch.");
            gameStarted = false;
            return;
        }

        Debug.Log($"[BotFill] RPC_InitializeMatch — bot actors: [{string.Join(", ", botActorNumbers)}]");
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
        ApplySyncedBotActorList(bots);
        List<int> seats = BuildActiveSeatList();
        int humans = GetActiveHumanPlayerCount();
        LogSeatDiagnostics(humans, seats);
        Debug.Log($"[Seats] Real players: {humans}");
        Debug.Log($"[Seats] Bots: {botActorNumbers.Count} [{string.Join(", ", botActorNumbers)}]");
        Debug.Log($"[Seats] Total: {seats.Count}");
        if (seats.Count != MaxTableSeats)
            Debug.LogError($"[DeckManager] RPC_InitializeMatch: invalid seat count {seats.Count}, expected {MaxTableSeats}.");

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowGameScene();
        else
            NetworkManager.InitializeGameplayScene();

        Debug.Log($"🤖 [Bot Mode] RPC Match initialized with {botActorNumbers.Count} bot actor(s).");
        RPC_ResetAllHands();
    }

    [PunRPC]
    void RPC_SyncBotsOnly(int[] bots)
    {
        ApplySyncedBotActorList(bots);
        if (PhotonNetwork.IsMasterClient)
            ReconcileExcessPhantomBots();
        Debug.Log($"🤖 [Sync] Bots list synced for reconnect: {botActorNumbers.Count} bot(s).");

        if (PhotonNetwork.IsMasterClient)
        {
            foreach (int actor in botActorNumbers)
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
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowGameScene();
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
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowGameScene();
        Debug.Log("[Deal] Started");
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

        List<int> seats = GetActiveSeatActorsSorted();
        if (seats.Count == MaxTableSeats)
        {
            IsDealingComplete = true;
            int myIdx = seats.IndexOf(PhotonNetwork.LocalPlayer.ActorNumber);
            if (myIdx < 0) myIdx = 0;
            int starterActor = seats[(myIdx + 3) % MaxTableSeats];

            yield return new WaitForSeconds(0.15f);
            photonView.RPC("RPC_DealingComplete", RpcTarget.All, starterActor);
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

        List<int> seats = GetActiveSeatActorsSorted();
        if (seats.Count != MaxTableSeats)
        {
            Debug.LogError($"[DeckManager] DistributeAllHandsInternal aborted — seat count {seats.Count}, expected {MaxTableSeats}.");
            return;
        }
        
        bool isThirteenthMode = GameSettings.Instance != null && GameSettings.Instance.currentMode == GameModeType.ThirteenthCardTrump;
        CardSuit thirteenthTrump = CardSuit.Spades;

        int playerIdx = 0;
        foreach (int seatActor in seats)
        {
            int cardsPerPlayer = CardsPerPlayer;
            List<CardData> hand = DrawCards(cardsPerPlayer);
            
            if (isThirteenthMode && playerIdx == 0)
            {
                thirteenthTrump = hand[cardsPerPlayer - 1].cardSuit;
                Debug.Log($"[Mode 2] 13th Card is {hand[cardsPerPlayer - 1].cardRank} of {thirteenthTrump}. Setting as Trump.");
            }

            if (IsActorBotControlled(seatActor))
            {
                botHands[seatActor] = hand;
                masterTrackingCounts[seatActor] = hand.Count;
                PersistHandToRoom(seatActor, hand);
            }
            else
            {
                int[] suits = new int[cardsPerPlayer];
                int[] ranks = new int[cardsPerPlayer];
                for (int i = 0; i < cardsPerPlayer; i++) { suits[i] = (int)hand[i].cardSuit; ranks[i] = (int)hand[i].cardRank; }
                masterTrackingCounts[seatActor] = cardsPerPlayer;
                humanHandsOnMaster[seatActor] = new List<CardData>(hand);
                photonView.RPC("RPC_AssignFullHand", RpcTarget.All, seatActor, suits, ranks);
            }

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
            PersistHandToRoom(actorNum, hand);
        }
        else if (botHands.TryGetValue(actorNum, out List<CardData> bHand))
        {
            for (int i = 0; i < bHand.Count; i++)
            {
                if (bHand[i].cardSuit == suit && bHand[i].cardRank == rank)
                {
                    bHand.RemoveAt(i);
                    break;
                }
            }
            masterTrackingCounts[actorNum] = bHand.Count;
            PersistHandToRoom(actorNum, bHand);
        }
    }

    bool ValidateAllHands()
    {
        if (masterTrackingCounts.Count != MaxTableSeats)
            return false;

        int expected = CardsPerPlayer;
        foreach (var entry in masterTrackingCounts)
            if (entry.Value != expected) return false;
        return true;
    }

    public void AuditHandCounts(string source)
    {
        int expected = CardsPerPlayer;
        string taash = TaashRules.IsTwoTaashMode ? "2 Taash (26)" : "1 Taash (13)";

        if (masterTrackingCounts.Count != MaxTableSeats)
            Debug.LogError($"[Cards] {source} — tracked seats {masterTrackingCounts.Count}/{MaxTableSeats} ({taash})");

        foreach (var entry in masterTrackingCounts)
        {
            if (entry.Value != expected)
                Debug.LogError($"[Cards] {source} — actor {entry.Key} has {entry.Value} cards, expected {expected} ({taash})");
        }

        if (ValidateAllHands())
            Debug.Log($"[Cards] {source} — all {MaxTableSeats} seats have {expected} cards ({taash})");
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
        if (IsDealingComplete) return;

        GameFlowState.SetPhase(GameFlowPhase.Dealing, forceRecovery: true);
        if (PlayerHand.LocalInstance != null)
            PlayerHand.LocalInstance.PlayDealAnimationOnly(cardsInBatch);
    }

    [PunRPC]
    public void RPC_AssignFullHand(int targetActor, int[] suitIndices, int[] rankIndices)
    {
        if (PlayerHand.LocalInstance != null)
            PlayerHand.LocalInstance.AssignFullHandLocal(targetActor, suitIndices, rankIndices);
    }
}