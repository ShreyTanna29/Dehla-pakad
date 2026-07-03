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
    public float matchmakingTimeout = 2f; 

    public const int MaxTableSeats = 4;
    private const int PhantomBotActorBase = 100;

    public static bool IsPrivateFriendsRoom() =>
        PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.IsVisible && !PhotonNetwork.OfflineMode;

    [Header("Bot Tracking")]
    public static List<int> botActorNumbers = new List<int>();
    public Dictionary<int, List<CardData>> botHands = new Dictionary<int, List<CardData>>();
    private Dictionary<int, List<CardData>> humanHandsOnMaster = new Dictionary<int, List<CardData>>(); // Cache for reconnect
    private Dictionary<int, int> masterTrackingCounts = new Dictionary<int, int>();
    public int currentDealBatch = 0;
    private bool _localIsDealingComplete = false;

    // Buffered deal RPCs can arrive before NetworkPlayer exists on slow clients.
    bool _hasPendingLocalHand;
    int _pendingTargetActor;
    int[] _pendingSuits;
    int[] _pendingRanks;
    Coroutine _pendingHandCoroutine;

    bool _hasPendingDealAnim;
    int _pendingDealCardsInBatch;
    int _pendingDealRevealUpTo;
    Coroutine _pendingDealAnimCoroutine;
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
    private Coroutine _dealSequenceCoroutine;
    private Coroutine _joinedRoomCoroutine;
    private int _pendingDealingCompleteStarter = -1;
    private Coroutine _pendingDealingCompleteCoroutine;
    private Coroutine matchmakingCoroutine;
    /// <summary>When true, buffered deal RPCs from a prior match are ignored (menu / leave).</summary>
    private bool _ignoringMatchRpcs;

    int CardsPerPlayer => TaashRules.CardsPerPlayer;

    void Awake() 
    {
        if(Instance == null) Instance = this;
        else Destroy(gameObject);
        masterTrackingCounts.Clear();
        humanHandsOnMaster.Clear();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsActorBotControlled(int actorNumber) => botActorNumbers.Contains(actorNumber);

    public static int GetActiveHumanPlayerCount() => PhotonRoomPlayers.CountActiveHumans();

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
        if (botActorNumbers == null) botActorNumbers = new List<int>();

        var seats = new List<int>(MaxTableSeats);
        var used = new HashSet<int>();

        if (PhotonNetwork.InRoom)
        {
            foreach (Player p in PhotonRoomPlayers.GetSorted())
            {
                if (p == null || p.IsInactive) continue;
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
            // Keep the authoritative "BOTS" room property in sync whenever a seat becomes a bot
            // (e.g. the host REMOVE flow). Without this, a reconnecting or late-joining client that
            // rebuilds bot seats via RestoreBotsFromRoom would not see this actor as a bot -> seat desync.
            SyncBotsToRoom();
        }

        if (PlayerProfileSync.Instance != null)
            PlayerProfileSync.Instance.UpdateAllNames();

        if (PhotonNetwork.IsMasterClient && PlayerHand.LocalInstance != null &&
            PlayerHand.LocalInstance.currentTurnActor == actorNumber)
        {
            PlayerHand.LocalInstance.TriggerBotTurnIfApplicable(actorNumber);
        }
    }

    public void EnsureHandCachedForBot(int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        EnsureHandCachedForActor(actorNumber);
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

    public void StopOnlineMatchmaking()
    {
        if (matchmakingCoroutine != null)
        {
            StopCoroutine(matchmakingCoroutine);
            matchmakingCoroutine = null;
            Debug.Log("[DeckManager] Online matchmaking countdown stopped.");
        }
    }

    public void StopAllDealCoroutines()
    {
        if (_dealSequenceCoroutine != null)
        {
            StopCoroutine(_dealSequenceCoroutine);
            _dealSequenceCoroutine = null;
        }

        if (_joinedRoomCoroutine != null)
        {
            StopCoroutine(_joinedRoomCoroutine);
            _joinedRoomCoroutine = null;
        }

        if (_pendingHandCoroutine != null)
        {
            StopCoroutine(_pendingHandCoroutine);
            _pendingHandCoroutine = null;
        }

        if (_pendingDealAnimCoroutine != null)
        {
            StopCoroutine(_pendingDealAnimCoroutine);
            _pendingDealAnimCoroutine = null;
        }

        if (_pendingDealingCompleteCoroutine != null)
        {
            StopCoroutine(_pendingDealingCompleteCoroutine);
            _pendingDealingCompleteCoroutine = null;
        }

        _hasPendingLocalHand = false;
        _hasPendingDealAnim = false;
        _pendingDealingCompleteStarter = -1;
        isDealCoroutineRunning = false;
    }

    /// <summary>Lightweight reset when opening a new mode from Home without tearing down Photon.</summary>
    public void PrepareForNewMatchFromMenu()
    {
        Debug.Log("[DeckManager] PrepareForNewMatchFromMenu");
        _ignoringMatchRpcs = true;
        StopOnlineMatchmaking();
        StopAllDealCoroutines();
        _localGameStarted = false;
        _localIsDealingComplete = false;
        deckIndex = 0;
        currentDealBatch = 0;
        botActorNumbers.Clear();
        botHands.Clear();
        humanHandsOnMaster.Clear();
        masterTrackingCounts.Clear();
        masterDeck.Clear();
        PlayerHand.CleanupRuntimeCardUi();
    }

    public void ResetMatchState()
    {
        Debug.Log("[DeckManager] Resetting match state for a fresh game.");

        _ignoringMatchRpcs = true;
        StopOnlineMatchmaking();
        StopAllDealCoroutines();
        
        // 🚀 Authoritative Reset — only when fully joined. During a LeaveRoom teardown InRoom is
        // still true but writing properties is rejected/logged, so require ClientState == Joined.
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
            && PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Joined)
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

        _localGameStarted = false;
        _localIsDealingComplete = false;
        IsDealingComplete = false;
        gameStarted = false;
        deckIndex = 0;
        currentDealBatch = 0;
        masterDeck.Clear();
        
        botActorNumbers.Clear();
        botHands.Clear();
        humanHandsOnMaster.Clear();
        masterTrackingCounts.Clear();

        PlayerHand.CleanupRuntimeCardUi();
    }

    public override void OnJoinedRoom()
    {
        if (_joinedRoomCoroutine != null)
            StopCoroutine(_joinedRoomCoroutine);
        _joinedRoomCoroutine = StartCoroutine(HandleDeckJoinedRoomDeferred());
    }

    IEnumerator HandleDeckJoinedRoomDeferred()
    {
        yield return null;

        if (gameStarted)
        {
            StartCoroutine(RejoinStateRoutine());
            yield break;
        }

        bool rejoining = PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GS", out object gs)
            && (bool)gs;

        if (IsPrivateFriendsRoom() && !rejoining)
        {
            // Task 7: if the host already locked modes / started the match before we joined
            // (e.g. a friend accepting a REPLACE invite), don't get stuck on the loading screen —
            // transition straight into the game. ExecuteFriendsGameStart is idempotent.
            if (FriendsMatchAlreadyStarted())
            {
                Debug.Log("[DeckManager] Joined a friends room that already started — entering game.");
                if (PlayWithFriendsManager.Instance != null)
                    PlayWithFriendsManager.Instance.ExecuteFriendsGameStart();
                yield break;
            }

            Debug.Log("Private Room Joined. Waiting in Lobby...");
            yield break;
        }

        if (!rejoining) ResetMatchState();
        if (TrumpManager.Instance != null) TrumpManager.ApplyTrumpForCurrentGameMode(false);

        if (PhotonNetwork.IsMasterClient)
        {
            if (matchmakingCoroutine != null) StopCoroutine(matchmakingCoroutine);
            if (PhotonNetwork.OfflineMode) FillBotsAndStart();
            else OnRoomJoinedCheckStart();
        }

        _joinedRoomCoroutine = null;
    }

    // Task 7: true once the host has locked modes / started the friends match (room property "ModesLocked").
    static bool FriendsMatchAlreadyStarted()
    {
        return PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("ModesLocked", out object ml)
            && ml is bool locked && locked;
    }

    /// <summary>
    /// Task 7 — Always-active backup so friends-mode invitees never get stuck on the loading screen.
    /// DeckManager's PhotonView is always active (unlike PlayWithFriendsManager, which can be
    /// inactive and miss the start RPC / its own property callback). When the host sets
    /// "ModesLocked", this drives the loading->game transition for everyone. Idempotent.
    /// </summary>
    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null || !PhotonNetwork.InRoom) return;
        if (!IsPrivateFriendsRoom()) return;

        if (propertiesThatChanged.ContainsKey("ModesLocked")
            && propertiesThatChanged["ModesLocked"] is bool locked && locked)
        {
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.ExecuteFriendsGameStart();
        }

        // The master changed the authoritative bot-seat list mid-match (a friend replaced a bot,
        // or a player was removed and a bot took the seat). Non-master clients refresh their local
        // bot seats live so seat labels and bot control stay correct without needing a reconnect.
        if (propertiesThatChanged.ContainsKey("BOTS") && !PhotonNetwork.IsMasterClient)
        {
            RestoreBotsFromRoom();
            if (PlayerProfileSync.Instance != null)
                PlayerProfileSync.Instance.UpdateAllNames();
            if (PlayerHand.LocalInstance != null)
                PlayerHand.LocalInstance.RebuildSeatOrderPublic();
        }
    }

    IEnumerator RejoinStateRoutine()
    {
        RestoreBotsFromRoom();
        if (PlayerProfileSync.Instance != null) PlayerProfileSync.Instance.UpdateAllNames();

        float timeout = 5.0f;
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
                foreach (Player p in PhotonRoomPlayers.GetSorted())
                {
                    if (p != null) EnsureHandCachedForActor(p.ActorNumber);
                }
                foreach (int bot in botActorNumbers) EnsureHandCachedForActor(bot);
            }
        }

        if (TrumpManager.Instance != null) TrumpManager.Instance.RefreshFromRoomProperties(false);

        yield return new WaitForSeconds(0.6f);

        // Self-restore play state from authoritative room properties (CTA = current turn actor).
        // Runs on every reconnecting client, not just the master, so a non-master client can
        // re-enable card input even if the master's pushed sync RPCs are delayed or missed.
        // OnDealingComplete is idempotent on reconnect, so overlapping with the master push is safe.
        if (IsDealingComplete && PlayerHand.LocalInstance != null)
        {
            int currentActor = PlayerHand.LocalInstance.currentTurnActor;
            PlayerHand.LocalInstance.OnDealingComplete(currentActor);
            if (PhotonNetwork.IsMasterClient && TurnManager.Instance != null)
                TurnManager.Instance.StartTurn(currentActor);
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
        {
            // A brand-new player joined a match in progress (e.g. host used REPLACE to invite a
            // friend into a bot seat). Hand them an available bot's cards so they take that seat.
            AssignBotSeatToNewPlayer(newPlayer);
            SyncReconnectingPlayer(newPlayer);
        }
        else if (PhotonNetwork.IsMasterClient)
            OnRoomJoinedCheckStart();
    }

    /// <summary>
    /// Master-only. When a new (non-reconnecting) player joins a match already in progress,
    /// transfer an existing bot's hand to that player so they replace the bot in its seat.
    /// Real players are prioritised by BuildActiveSeatList, so the freed bot is trimmed
    /// automatically. Returns true if a bot seat was handed off.
    /// </summary>
    bool AssignBotSeatToNewPlayer(Player newPlayer)
    {
        if (!PhotonNetwork.IsMasterClient || !gameStarted || !IsDealingComplete) return false;
        if (newPlayer == null || botActorNumbers.Count == 0) return false;

        // Already has a hand cached/persisted (true reconnect) — leave it to the normal path.
        EnsureHandCachedForActor(newPlayer.ActorNumber);
        if (humanHandsOnMaster.TryGetValue(newPlayer.ActorNumber, out List<CardData> existing) && existing.Count > 0)
            return false;

        // Prefer a phantom (filler) bot; fall back to any bot.
        int chosenBot = -1;
        foreach (int b in botActorNumbers)
        {
            if (IsPhantomBotActor(b)) { chosenBot = b; break; }
        }
        if (chosenBot < 0) chosenBot = botActorNumbers[0];

        EnsureHandCachedForActor(chosenBot);
        List<CardData> hand = null;
        if (botHands.TryGetValue(chosenBot, out List<CardData> bh) && bh != null)
            hand = new List<CardData>(bh);
        else if (humanHandsOnMaster.TryGetValue(chosenBot, out List<CardData> hh) && hh != null)
            hand = new List<CardData>(hh);

        // Free the bot seat.
        botActorNumbers.Remove(chosenBot);
        botHands.Remove(chosenBot);
        humanHandsOnMaster.Remove(chosenBot);

        if (hand != null)
        {
            humanHandsOnMaster[newPlayer.ActorNumber] = hand;
            PersistHandToRoom(newPlayer.ActorNumber, hand);
        }

        DedupeBotActorNumbers();
        SyncBotsToRoom();

        Debug.Log($"[Replace] New player {newPlayer.ActorNumber} took over bot {chosenBot}'s seat.");

        if (PlayerHand.LocalInstance != null) PlayerHand.LocalInstance.RebuildSeatOrderPublic();
        if (PlayerProfileSync.Instance != null) PlayerProfileSync.Instance.UpdateAllNames();
        return true;
    }

    /// <summary>
    /// Master-only. Reopens the (private, in-progress) room so an invited friend can join to
    /// replace a bot. Called by the in-game REPLACE flow.
    /// </summary>
    public void ReopenRoomForReplace()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
        if (!PhotonNetwork.CurrentRoom.IsOpen)
        {
            PhotonNetwork.CurrentRoom.IsOpen = true;
            Debug.Log("[Replace] Room reopened so an invited friend can take a bot seat.");
        }
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

    /// <summary>
    /// Tasks 8 &amp; 24 — Host removes a real player and gives the seat to a bot.
    /// 1) Marks the seat as a bot LOCALLY first so the host UI updates instantly (no waiting on
    ///    the CloseConnection round-trip). 2) Syncs the swap to other clients. 3) Kicks the player.
    /// 4) Forces a prompt friends-status re-poll so the replaced player stops showing "In Game".
    /// </summary>
    public void HostReplacePlayerWithBot(Player player)
    {
        if (!PhotonNetwork.IsMasterClient || player == null || player.IsLocal) return;

        int actor = player.ActorNumber;

        // Task 8: local-first UI update (direct call runs immediately on the host).
        RPC_MarkPlayerAsBot(actor);

        // Sync the seat->bot swap to the other clients.
        if (photonView != null)
            photonView.RPC("RPC_MarkPlayerAsBot", RpcTarget.Others, actor);

        // Task 24: make the removal RELIABLE on every server tier. PhotonNetwork.CloseConnection
        // requires a dashboard permission that is disabled on this app ("CloseConnection is disabled"),
        // so relying on it alone leaves the replaced player lingering as an active room member — still
        // controllable and still reported as "In Game" to their friends, with their seat painted as a
        // phantom present player on other clients. A targeted RPC tells that specific player to leave
        // the match themselves; CloseConnection is still attempted below as a fast path where allowed.
        if (photonView != null)
            photonView.RPC("RPC_ForceLeaveAfterReplace", player);

        PhotonNetwork.CloseConnection(player);

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.RefreshInGameStatusSoon();
    }

    /// <summary>
    /// Task 24 — Runs on the player the host just replaced with a bot. Leaves the room and returns
    /// home so the replaced player no longer appears in the match (or as "In Game") on any client,
    /// even when PhotonNetwork.CloseConnection is unavailable. Idempotent: a no-op if already left.
    /// </summary>
    [PunRPC]
    void RPC_ForceLeaveAfterReplace()
    {
        if (!PhotonNetwork.InRoom) return;
        Debug.Log("[Replace] Removed by host — leaving the match.");
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.LeaveRoomAndCleanup();
        else
            PhotonNetwork.LeaveRoom();
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

                // The new master inherits bot control — make sure the safety-net watchdog
                // is running here, otherwise a stuck bot turn would never recover after a switch.
                PlayerHand.LocalInstance.EnsureBotWatchdogRunning();

                TurnManager.Instance.StartTurn(currentTurn);

                if (IsActorBotControlled(currentTurn))
                    PlayerHand.LocalInstance.TriggerBotTurnIfApplicable(currentTurn);
            }
        }
    }

    public void OnRoomJoinedCheckStart()
    {
        if (!PhotonNetwork.InRoom) return;

        if (IsPrivateFriendsRoom())
        {
            Debug.Log("[DeckManager] Private room — waiting for host StartPrivateGame().");
            return;
        }

        // 🚀 REJOIN CHECK: If we already have a game started, don't reset state
        if (gameStarted)
        {
            bool validRejoin = IsDealingComplete && GetActiveSeatActorsSorted().Count == MaxTableSeats;
            if (validRejoin)
            {
                Debug.Log("[DeckManager] Rejoined existing match. Maintaining state.");
                return;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                Debug.LogWarning("[DeckManager] Stale gameStarted flag without valid seats — clearing for new match.");
                _localGameStarted = false;
                _localIsDealingComplete = false;
            }
            else
            {
                Debug.Log("[DeckManager] Waiting for master to reset stale match flags.");
                return;
            }
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
            if (MatchmakingManager.Instance != null && MatchmakingManager.Instance.WasCancelledByUser)
            {
                matchmakingCoroutine = null;
                yield break;
            }

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
        _ignoringMatchRpcs = false;

        if (!IsPrivateFriendsRoom()
            && !PhotonNetwork.OfflineMode
            && MatchmakingManager.Instance != null
            && MatchmakingManager.Instance.WasCancelledByUser)
        {
            Debug.Log("[DeckManager] FillBotsAndStart skipped — online matchmaking was cancelled.");
            return;
        }

        if (gameStarted && !IsPrivateFriendsRoom())
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

        Debug.Log("[DeckManager] Starting Match from Private/Bot Lobby...");
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
        _ignoringMatchRpcs = false;

        if (ResultManager.Instance != null)
            ResultManager.Instance.InitializeForMatch();

        ApplySyncedBotActorList(bots);
        List<int> seats = BuildActiveSeatList();
        int humans = GetActiveHumanPlayerCount();
        LogSeatDiagnostics(humans, seats);

        if (seats.Count != MaxTableSeats)
            Debug.LogError($"[DeckManager] RPC_InitializeMatch: invalid seat count {seats.Count}, expected {MaxTableSeats}.");

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.EnsureLocalNetworkPlayer();
        PlayerHand.ResolveLocalHand();
        NetworkManager.InitializeGameplayScene();

        Debug.Log($"🤖 [Bot Mode] RPC Match initialized with {botActorNumbers.Count} bot actor(s).");
        RPC_ResetAllHands();
    }

    [PunRPC]
    void RPC_SetHiddenTrumpInfo(int ownerActor, int suit, int rank)
    {
        PlayerHand.ApplyHiddenTrumpInfo(ownerActor, suit, rank);
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

    // ==========================================================================
    // FRIENDS-LOBBY RPC RELAYS (BUG 3 fix)
    // PlayWithFriendsManager routes its friends RPCs through THIS GameObject's
    // always-active PhotonView (GetReliableRpcView). PUN only resolves an RPC's
    // target method among components on the PhotonView's OWN GameObject, so the
    // [PunRPC] methods declared on PlayWithFriendsManager (a different GameObject)
    // were never found on receivers. These relays live on DeckManager and forward
    // to the singleton so the start/modes signals actually reach every client.
    // ==========================================================================
    [PunRPC]
    public void RPC_Friends_StartGameForEveryone()
    {
        Debug.Log("[DeckManager] Relay RPC_Friends_StartGameForEveryone");
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ExecuteFriendsGameStart();
        else
            RpcStartGameSync();
    }

    /// <summary>Fallback sync when PlayWithFriendsManager is unavailable on a client.</summary>
    [PunRPC]
    public void RpcStartGameSync()
    {
        Debug.Log("[DeckManager] RpcStartGameSync — forcing in-game UI on all clients.");
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SnapScreenCover();
            NetworkManager.Instance.BeginGameTransitionWithBlackFade(() =>
            {
                GameFlowState.SetPhase(GameFlowPhase.InGame, forceRecovery: true);
                NetworkManager.Instance.ResetGameStartGuards();
                NetworkManager.Instance.BeginGameAfterRoomReady(showLoadingOverlay: false);
            }, skipFadeIn: true);
        }
    }

    [PunRPC]
    public void RPC_Friends_ShowModesPanel()
    {
        Debug.Log("[DeckManager] Relay RPC_Friends_ShowModesPanel");
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.ExecuteShowModesPanelToClients();
        else
            Debug.LogWarning("[DeckManager] PWF.Instance null on modes relay");
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

        bool isPrivate = IsPrivateFriendsRoom();

        if (MatchmakingManager.Instance != null && !isPrivate && !PhotonNetwork.OfflineMode)
        {
            Debug.Log("[DeckManager] Triggering match transition (StopSearching)");
            MatchmakingManager.Instance.StopSearching(true);
        }
        else
        {
            // For Offline or Private mode, we bypass Matchmaking UI
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.ShowGameScene();
            else
                PlayerHand.LocalInstance?.InitializeGameScene();

            if (PhotonNetwork.IsMasterClient)
            {
                Debug.Log(isPrivate ? "[Friends Mode] Starting deal directly" : "[Bot Mode] Starting deal directly");
                StartFullDealingSequence();
            }
        }

        botHands.Clear();
        currentDealBatch = 0;
        IsDealingComplete = false;
        isDealCoroutineRunning = false;

        if (PlayerHand.LocalInstance != null) PlayerHand.LocalInstance.RPC_ResetHand();
    }

    public void StartFullDealingSequence()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (isDealCoroutineRunning) return;
        if (_ignoringMatchRpcs)
        {
            Debug.Log("[DeckManager] StartFullDealingSequence blocked — match RPCs ignored (menu/leave).");
            return;
        }
        if (!IsMatchContextReadyForDealing())
        {
            Debug.LogWarning("[DeckManager] StartFullDealingSequence blocked — not in active match context.");
            return;
        }

        PrepareLocalDealingStateForNextRound();
        if (_dealSequenceCoroutine != null)
            StopCoroutine(_dealSequenceCoroutine);
        _dealSequenceCoroutine = StartCoroutine(WaitAndDealCards());
    }

    /// <summary>Clears local dealing flags so the next round can deal (fixes stuck IsDealingComplete).</summary>
    public void PrepareLocalDealingStateForNextRound()
    {
        deckIndex = 0;
        currentDealBatch = 0;
        isDealCoroutineRunning = false;
        _localIsDealingComplete = false;
    }

    /// <summary>
    /// Master waits for all clients to load game UI before sending buffered deal RPCs.
    /// </summary>
    IEnumerator WaitAndDealCards()
    {
        Debug.Log("[DeckManager] WaitAndDealCards — 2s sync buffer for all clients...");

        if (!IsMatchContextReadyForDealing())
        {
            isDealCoroutineRunning = false;
            yield break;
        }

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ShowGameScene();

        yield return new WaitForSeconds(2.0f);

        if (!IsMatchContextReadyForDealing())
        {
            isDealCoroutineRunning = false;
            yield break;
        }

        float timeout = 4f;
        while (PlayerHand.LocalInstance == null && timeout > 0f)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.EnsureLocalNetworkPlayer();
            PlayerHand.ResolveLocalHand();
            yield return null;
            timeout -= Time.deltaTime;
        }

        if (PlayerHand.LocalInstance == null)
        {
            Debug.LogError("[DeckManager] WaitAndDealCards aborted — local PlayerHand missing.");
            isDealCoroutineRunning = false;
            yield break;
        }

        Debug.Log("[DeckManager] WaitAndDealCards complete — starting deal sequence.");
        yield return StartCoroutine(FullDealingSequenceRoutine());
        _dealSequenceCoroutine = null;
    }

    bool IsMatchContextReadyForDealing()
    {
        if (_ignoringMatchRpcs) return false;
        return PhotonNetwork.InRoom || PhotonNetwork.OfflineMode;
    }

    public bool IsMatchContextReadyForDealingPublic() => IsMatchContextReadyForDealing();

    public void EnableMatchRpcs() => _ignoringMatchRpcs = false;

    IEnumerator WaitForSeatsReady(float timeoutSeconds = 8f)
    {
        while (timeoutSeconds > 0f)
        {
            if (!IsMatchContextReadyForDealing())
            {
                Debug.Log("[DeckManager] Seat wait aborted — no active match context.");
                yield break;
            }

            if (GetActiveSeatActorsSorted().Count == MaxTableSeats)
                yield break;

            yield return null;
            timeoutSeconds -= Time.unscaledDeltaTime;
        }

        Debug.LogError($"[DeckManager] Seat wait timed out — seats {GetActiveSeatActorsSorted().Count}/{MaxTableSeats}");
    }

    IEnumerator WaitAndStartDealing()
    {
        yield return StartCoroutine(WaitAndDealCards());
    }

    IEnumerator FullDealingSequenceRoutine()
    {
        Debug.Log("[DeckManager] FullDealingSequenceRoutine started.");
        isDealCoroutineRunning = true;
        currentDealBatch = 0;

        if (!IsMatchContextReadyForDealing())
        {
            Debug.Log("[DeckManager] FullDealingSequenceRoutine aborted — left match.");
            isDealCoroutineRunning = false;
            yield break;
        }

        yield return WaitForSeatsReady();

        if (!IsMatchContextReadyForDealing() || GetActiveSeatActorsSorted().Count != MaxTableSeats)
        {
            Debug.LogError("[DeckManager] FullDealingSequenceRoutine aborted — seats not ready.");
            isDealCoroutineRunning = false;
            yield break;
        }

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        yield return new WaitForSeconds(0.2f);

        // Distribute the full hands up-front so every client already knows its cards. The cards
        // stay hidden and are revealed progressively as each deal batch animation lands (Task 26).
        // Doing this here (instead of after the batches) also removes the redundant extra hand
        // refresh that caused the deal animation to appear to run twice (Task 23).
        Debug.Log("[DeckManager] Distributing cards up-front for progressive reveal...");
        BuildAndShuffleDeck();
        DistributeAllHandsInternal();

        int[] dealBatches = TaashRules.GetDealAnimationBatches();

        int revealedSoFar = 0;
        for (int batch = 0; batch < dealBatches.Length; batch++)
        {
            currentDealBatch = batch + 1;
            int cardsThisBatch = dealBatches[batch];
            revealedSoFar += cardsThisBatch;
            Debug.Log($"[DeckManager] Deal round {currentDealBatch}/{dealBatches.Length} (reveal up to {revealedSoFar})");
            photonView.RPC("RPC_PlayDealAnimation", RpcTarget.AllBuffered, cardsThisBatch, revealedSoFar);

            // FIX: GetDealBatchDuration now equals the real animation runtime; add a fixed 0.5s
            // gap between batches (previously the overestimated duration produced a ~1s idle gap).
            yield return new WaitForSeconds(PlayerHand.GetDealBatchDuration(cardsThisBatch) + 0.2f);
        }

        List<int> seats = GetActiveSeatActorsSorted();
        if (seats.Count == MaxTableSeats)
        {
            int starterActor = seats[0];

            yield return new WaitForSeconds(0.2f);
            if (!IsMatchContextReadyForDealing())
            {
                Debug.Log("[DeckManager] DealingComplete skipped — match ended before finish.");
                isDealCoroutineRunning = false;
                yield break;
            }

            photonView.RPC("RPC_DealingComplete", RpcTarget.AllBuffered, starterActor);
        }
        else
        {
            Debug.LogWarning($"[DeckManager] DealingComplete deferred — seat count {seats.Count}/{MaxTableSeats} (inRoom={PhotonNetwork.InRoom})");
            QueuePendingDealingComplete();
        }
        isDealCoroutineRunning = false;
    }

    void QueuePendingDealingComplete(int starterActor = -1)
    {
        if (starterActor >= 0)
            _pendingDealingCompleteStarter = starterActor;

        if (_pendingDealingCompleteCoroutine == null && isActiveAndEnabled)
            _pendingDealingCompleteCoroutine = StartCoroutine(WaitForPendingDealingCompleteRoutine());
    }

    IEnumerator WaitForPendingDealingCompleteRoutine()
    {
        float timeout = 8f;
        while (timeout > 0f)
        {
            if (!IsMatchContextReadyForDealing())
                yield break;

            List<int> seats = GetActiveSeatActorsSorted();
            if (seats.Count == MaxTableSeats)
            {
                int starter = _pendingDealingCompleteStarter >= 0 ? _pendingDealingCompleteStarter : seats[0];
                _pendingDealingCompleteStarter = -1;
                _pendingDealingCompleteCoroutine = null;
                ApplyDealingComplete(starter);
                yield break;
            }

            yield return null;
            timeout -= Time.unscaledDeltaTime;
        }

        Debug.LogError("[DeckManager] Pending DealingComplete timed out — returning Home.");
        _pendingDealingCompleteStarter = -1;
        _pendingDealingCompleteCoroutine = null;
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ReturnToHomeScreen();
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
        bool isHiddenMode = GameSettings.Instance != null && GameSettings.Instance.currentMode == GameModeType.HiddenTrump;
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
            else if (isHiddenMode && playerIdx == 0)
            {
                CardData hiddenCard = hand[cardsPerPlayer - 1];
                if (photonView != null)
                {
                    photonView.RPC(nameof(RPC_SetHiddenTrumpInfo), RpcTarget.AllBuffered,
                        seatActor, (int)hiddenCard.cardSuit, (int)hiddenCard.cardRank);
                }
                else
                {
                    PlayerHand.ApplyHiddenTrumpInfo(seatActor, (int)hiddenCard.cardSuit, (int)hiddenCard.cardRank);
                }
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
                photonView.RPC("RPC_AssignFullHand", RpcTarget.AllBuffered, seatActor, suits, ranks);
            }

            playerIdx++;
        }

        if (isThirteenthMode && TrumpManager.Instance != null)
        {
            TrumpManager.Instance.SyncTrumpSuit(thirteenthTrump, true);
        }
        else if (isHiddenMode && TrumpManager.Instance != null)
        {
            TrumpManager.Instance.SyncTrumpSuit(CardSuit.Spades, false, false);
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
        if (!IsMatchContextReadyForDealing())
        {
            Debug.Log("[DeckManager] RPC_DealingComplete ignored — not in match.");
            return;
        }

        if (GetActiveSeatActorsSorted().Count != MaxTableSeats)
        {
            Debug.LogWarning($"[DeckManager] RPC_DealingComplete deferred — seats {GetActiveSeatActorsSorted().Count}/{MaxTableSeats}");
            _pendingDealingCompleteStarter = starterActor;
            if (_pendingDealingCompleteCoroutine == null && isActiveAndEnabled)
                _pendingDealingCompleteCoroutine = StartCoroutine(WaitForPendingDealingCompleteRoutine());
            return;
        }

        ApplyDealingComplete(starterActor);
    }

    void ApplyDealingComplete(int starterActor)
    {
        IsDealingComplete = true;
        gameStarted = true;
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();
        if (TrumpManager.Instance != null)
            TrumpManager.Instance.RefreshFromRoomProperties(false);
        if (PlayerHand.LocalInstance != null)
            PlayerHand.LocalInstance.OnDealingComplete(starterActor);
    }

    [PunRPC]
    public void RPC_PlayDealAnimation(int cardsInBatch, int revealUpTo)
    {
        if (!IsMatchContextReadyForDealing()) return;
        if (IsDealingComplete) return;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.HideLoading();

        GameFlowState.SetPhase(GameFlowPhase.Dealing, forceRecovery: true);

        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.PlayDealAnimationOnly(cardsInBatch, revealUpTo);
            return;
        }

        _hasPendingDealAnim = true;
        _pendingDealCardsInBatch = cardsInBatch;
        _pendingDealRevealUpTo = revealUpTo;
        if (_pendingDealAnimCoroutine == null && isActiveAndEnabled)
            _pendingDealAnimCoroutine = StartCoroutine(ApplyPendingDealAnimationRoutine());
    }

    IEnumerator ApplyPendingDealAnimationRoutine()
    {
        float timeout = 8f;
        while (_hasPendingDealAnim && timeout > 0f)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.EnsureLocalNetworkPlayer();
            PlayerHand.ResolveLocalHand();

            if (PlayerHand.LocalInstance != null)
            {
                PlayerHand.LocalInstance.PlayDealAnimationOnly(_pendingDealCardsInBatch, _pendingDealRevealUpTo);
                _hasPendingDealAnim = false;
                _pendingDealAnimCoroutine = null;
                yield break;
            }

            yield return null;
            timeout -= Time.deltaTime;
        }

        Debug.LogError("[DeckManager] Pending deal animation timed out.");
        _pendingDealAnimCoroutine = null;
    }

    public void ResetRoundStateForNextRound()
    {
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom
            && PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Joined)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                { "TP", 0 },
                { "TC", new int[0] },
                { "CTA", -1 },
                { "SW", new int[4] },
                { "DL", new int[4] },
                { "DC", false }
            });
        }

        deckIndex = 0;
        currentDealBatch = 0;
        IsDealingComplete = false;
        isDealCoroutineRunning = false;
    }

    [PunRPC]
    public void RPC_OnRoundCompleted()
    {
        if (ResultManager.Instance != null)
            ResultManager.Instance.OnRoundCompleted();
    }

    [PunRPC]
    public void RPC_BeginNextRound(int newRound)
    {
        // STRICT SEQUENCE GATE: guarantee the leaderboard is gone on THIS client BEFORE any
        // dealing visuals begin. Without this, cross-client 5s-timer skew could let cards deal
        // underneath a still-visible leaderboard on slower peers.
        if (ResultManager.Instance != null)
        {
            ResultManager.Instance.ForceHideLeaderboardNow();
            ResultManager.Instance.ApplyNextRoundStart(newRound);
        }

        PrepareLocalDealingStateForNextRound();
        GameFlowState.SetPhase(GameFlowPhase.Dealing, forceRecovery: true);

        if (PlayerHand.LocalInstance != null)
            PlayerHand.LocalInstance.RPC_ResetHand();

        if (PhotonNetwork.IsMasterClient)
            StartFullDealingSequence();
    }

    [PunRPC]
    void RPC_AssignFullHand(int targetActor, int[] suitIndices, int[] rankIndices)
    {
        if (PhotonNetwork.LocalPlayer == null || targetActor != PhotonNetwork.LocalPlayer.ActorNumber)
            return;

        if (PlayerHand.LocalInstance != null)
        {
            PlayerHand.LocalInstance.AssignFullHandLocal(targetActor, suitIndices, rankIndices);
            return;
        }

        _hasPendingLocalHand = true;
        _pendingTargetActor = targetActor;
        _pendingSuits = suitIndices;
        _pendingRanks = rankIndices;
        if (_pendingHandCoroutine == null && isActiveAndEnabled)
            _pendingHandCoroutine = StartCoroutine(ApplyPendingLocalHandRoutine());
    }

    IEnumerator ApplyPendingLocalHandRoutine()
    {
        float timeout = 8f;
        while (_hasPendingLocalHand && timeout > 0f)
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.EnsureLocalNetworkPlayer();
            PlayerHand.ResolveLocalHand();

            if (PlayerHand.LocalInstance != null)
            {
                PlayerHand.LocalInstance.AssignFullHandLocal(_pendingTargetActor, _pendingSuits, _pendingRanks);
                _hasPendingLocalHand = false;
                _pendingHandCoroutine = null;
                yield break;
            }

            yield return null;
            timeout -= Time.deltaTime;
        }

        Debug.LogError("[DeckManager] Pending hand assignment timed out — client may see empty hand.");
        _pendingHandCoroutine = null;
    }

    // PlayWithFriendsPanel's PhotonView often has ViewID 0 while inactive — forward lobby RPCs here.
    [PunRPC]
    void RPC_StartGameForEveryone()
    {
        PlayWithFriendsManager mgr = PlayWithFriendsManager.Instance;
        if (mgr == null)
        {
            var all = Resources.FindObjectsOfTypeAll<PlayWithFriendsManager>();
            foreach (var m in all)
            {
                if (m == null || !m.gameObject.scene.IsValid()) continue;
                mgr = m;
                break;
            }
        }

        if (mgr != null)
            mgr.ExecuteFriendsGameStart();
        else
            Debug.LogError("[Friends] RPC_StartGameForEveryone received but PlayWithFriendsManager is missing.");
    }

    [PunRPC]
    void RPC_ShowModesPanelToClients()
    {
        PlayWithFriendsManager mgr = PlayWithFriendsManager.Instance;
        if (mgr == null)
        {
            var all = Resources.FindObjectsOfTypeAll<PlayWithFriendsManager>();
            foreach (var m in all)
            {
                if (m == null || !m.gameObject.scene.IsValid()) continue;
                mgr = m;
                break;
            }
        }

        if (mgr != null)
            mgr.ExecuteShowModesPanelToClients();
    }
}