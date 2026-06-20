using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Central store for the local player's soft-currency (coins) and owned/equipped cosmetic items.
/// Holds an authoritative in-memory copy and mirrors it to Firebase Realtime Database under
/// <c>users/{userId}/coins</c>, <c>users/{userId}/inventory</c> and <c>users/{userId}/equipped</c>.
///
/// UI (e.g. <see cref="InventoryUIController"/>) and IAP (<see cref="ShopIAPManager"/>) talk to this
/// manager only — they never touch Firebase directly. Subscribe to <see cref="OnCoinsChanged"/> and
/// <see cref="OnInventoryChanged"/> to refresh the UI in real time.
///
/// Standalone manager: it does not modify any existing gameplay scripts.
/// </summary>
public class CurrencyAndInventoryManager : MonoBehaviour
{
    public static CurrencyAndInventoryManager Instance { get; private set; }

    // Must match the URL used elsewhere in the project (GoogleLogin, PlayerProfileManager, etc.).
    private const string FirebaseDatabaseUrl = "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/";

    // Coins granted automatically the first time a brand-new account loads (no coins node in Firebase yet).
    private const int DefaultNewAccountCoins = 100;

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private int coins;
    [SerializeField] private List<string> ownedItems = new List<string>();

    // Category -> equipped ItemID (e.g. "Cards" -> "card_blue").
    private readonly Dictionary<string, string> equippedItems = new Dictionary<string, string>();

    private bool _dataLoaded;
    private bool _authHooked;

    /// <summary>Fired whenever the coin balance changes (after Add/Deduct).</summary>
    public event Action OnCoinsChanged;
    /// <summary>Fired whenever owned or equipped items change, or after a fresh load.</summary>
    public event Action OnInventoryChanged;

    public int Coins => coins;
    public bool IsDataLoaded => _dataLoaded;
    public IReadOnlyList<string> OwnedItems => ownedItems;

    /// <summary>Current Firebase user id, or null if no one is signed in / Firebase not ready.</summary>
    public static string UserId
    {
        get
        {
            try { return FirebaseAuth.DefaultInstance.CurrentUser?.UserId; }
            catch { return null; }
        }
    }

    #region Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        HookAuthState();

        // If a user is already signed in by the time we start, load immediately.
        if (!string.IsNullOrEmpty(UserId))
            LoadUserData();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnhookAuthState();
    }

    private void HookAuthState()
    {
        if (_authHooked) return;
        try
        {
            FirebaseAuth.DefaultInstance.StateChanged += OnAuthStateChanged;
            _authHooked = true;
        }
        catch (Exception e)
        {
            // Firebase dependencies may not be ready yet; LoadUserData() can still be called manually after login.
            Debug.LogWarning("[Currency] Could not hook Firebase auth state yet: " + e.Message);
        }
    }

    private void UnhookAuthState()
    {
        if (!_authHooked) return;
        try { FirebaseAuth.DefaultInstance.StateChanged -= OnAuthStateChanged; }
        catch { /* ignore */ }
        _authHooked = false;
    }

    private void OnAuthStateChanged(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(UserId))
            LoadUserData();
        else
            ResetLocalState();
    }

    #endregion

    #region Firebase references

    private DatabaseReference UserRef()
    {
        string uid = UserId;
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[Currency] No signed-in user — skipping Firebase sync.");
            return null;
        }
        return FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference.Child("users").Child(uid);
    }

    #endregion

    #region Coins

    /// <summary>Adds coins (e.g. from an IAP) and syncs the new balance to Firebase.</summary>
    public void AddCoins(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("[Currency] AddCoins ignored non-positive amount: " + amount);
            return;
        }

        coins += amount;
        OnCoinsChanged?.Invoke();
        SyncCoins();
        Debug.Log($"[Currency] +{amount} coins (balance: {coins})");
    }

    /// <summary>
    /// Deducts coins if the balance allows. Returns true on success, false if there are not enough coins.
    /// </summary>
    public bool DeductCoins(int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("[Currency] DeductCoins ignored non-positive amount: " + amount);
            return false;
        }

        if (coins < amount)
        {
            Debug.LogWarning($"[Currency] Not enough coins (have {coins}, need {amount}).");
            return false;
        }

        coins -= amount;
        OnCoinsChanged?.Invoke();
        SyncCoins();
        Debug.Log($"[Currency] -{amount} coins (balance: {coins})");
        return true;
    }

    private void SyncCoins()
    {
        DatabaseReference root = UserRef();
        if (root == null) return;

        root.Child("coins").SetValueAsync(coins).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
                Debug.LogError("[Currency] Failed to sync coins: " + task.Exception);
        });
    }

    #endregion

    #region Inventory

    public bool HasItem(string itemId)
    {
        return !string.IsNullOrEmpty(itemId) && ownedItems.Contains(itemId);
    }

    public bool IsEquipped(string itemId, string category)
    {
        return !string.IsNullOrEmpty(category)
               && equippedItems.TryGetValue(category, out string equipped)
               && equipped == itemId;
    }

    public string GetEquipped(string category)
    {
        return equippedItems.TryGetValue(category, out string equipped) ? equipped : null;
    }

    /// <summary>
    /// Spends coins to acquire an item. No-op if already owned or unaffordable.
    /// On success the item is added to the inventory, auto-equipped for its category, and synced.
    /// </summary>
    public void BuyItem(string itemId, int cost, string category)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            Debug.LogWarning("[Currency] BuyItem called with empty itemId.");
            return;
        }

        if (HasItem(itemId))
        {
            Debug.LogWarning($"[Currency] Item already owned: {itemId}");
            return;
        }

        // Free items (cost <= 0) are claimed without spending; paid items must deduct successfully.
        if (cost > 0 && !DeductCoins(cost))
        {
            Debug.LogWarning($"[Currency] Purchase failed (insufficient coins): {itemId}");
            return;
        }

        ownedItems.Add(itemId);
        SyncInventory();

        // Convenience: newly bought cosmetics auto-equip in their category.
        // EquipItem already raises OnInventoryChanged, so we don't raise it again here
        // (doing so would trigger a redundant UI grid rebuild in the same frame).
        EquipItem(itemId, category);

        Debug.Log($"[Currency] Bought {itemId} for {cost} coins (category: {category}).");
    }

    /// <summary>Equips an owned item for a category and syncs the equipped map.</summary>
    public void EquipItem(string itemId, string category)
    {
        if (string.IsNullOrEmpty(category))
        {
            Debug.LogWarning("[Currency] EquipItem called with empty category.");
            return;
        }

        if (!HasItem(itemId))
        {
            Debug.LogWarning($"[Currency] Cannot equip un-owned item: {itemId}");
            return;
        }

        equippedItems[category] = itemId;
        SyncEquipped();
        OnInventoryChanged?.Invoke();
        Debug.Log($"[Currency] Equipped {itemId} for {category}.");
    }

    private void SyncInventory()
    {
        DatabaseReference root = UserRef();
        if (root == null) return;

        // Firebase Realtime Database favours maps over arrays; store {itemId: true}.
        var map = new Dictionary<string, object>();
        foreach (string id in ownedItems)
            map[id] = true;

        root.Child("inventory").SetValueAsync(map).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
                Debug.LogError("[Currency] Failed to sync inventory: " + task.Exception);
        });
    }

    private void SyncEquipped()
    {
        DatabaseReference root = UserRef();
        if (root == null) return;

        var map = new Dictionary<string, object>();
        foreach (KeyValuePair<string, string> kv in equippedItems)
            map[kv.Key] = kv.Value;

        root.Child("equipped").SetValueAsync(map).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
                Debug.LogError("[Currency] Failed to sync equipped items: " + task.Exception);
        });
    }

    #endregion

    #region Load

    /// <summary>
    /// Loads coins, inventory and equipped items from Firebase for the current user.
    /// Safe to call multiple times (e.g. on each login). Fires change events when done.
    /// </summary>
    public void LoadUserData()
    {
        DatabaseReference root = UserRef();
        if (root == null) return;

        root.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[Currency] Failed to load user economy data: " + task.Exception);
                return;
            }

            DataSnapshot snapshot = task.Result;
            bool isNewAccount = ApplySnapshot(snapshot);

            // Brand-new account (no coins stored yet): grant the starting balance and persist it.
            if (isNewAccount)
            {
                coins = DefaultNewAccountCoins;
                SyncCoins();
                Debug.Log($"[Currency] New account detected — granted {DefaultNewAccountCoins} starting coins.");
            }

            _dataLoaded = true;
            OnCoinsChanged?.Invoke();
            OnInventoryChanged?.Invoke();
            Debug.Log($"[Currency] Loaded economy: {coins} coins, {ownedItems.Count} items, {equippedItems.Count} equipped.");
        });
    }

    /// <summary>
    /// Populates local state from a Firebase snapshot. Returns true when the account has no coins
    /// data yet (a brand-new account), so the caller can grant the starting balance.
    /// </summary>
    private bool ApplySnapshot(DataSnapshot snapshot)
    {
        coins = 0;
        ownedItems.Clear();
        equippedItems.Clear();

        if (snapshot == null || !snapshot.Exists)
            return true;

        // Coins
        DataSnapshot coinsSnap = snapshot.Child("coins");
        bool hasCoinsData = coinsSnap.Exists && coinsSnap.Value != null;
        if (hasCoinsData)
        {
            try { coins = Convert.ToInt32(coinsSnap.Value); }
            catch { coins = 0; }
        }

        // Inventory ({itemId: true})
        DataSnapshot invSnap = snapshot.Child("inventory");
        if (invSnap.Exists)
        {
            foreach (DataSnapshot child in invSnap.Children)
                if (!string.IsNullOrEmpty(child.Key) && !ownedItems.Contains(child.Key))
                    ownedItems.Add(child.Key);
        }

        // Equipped ({category: itemId})
        DataSnapshot equipSnap = snapshot.Child("equipped");
        if (equipSnap.Exists)
        {
            foreach (DataSnapshot child in equipSnap.Children)
                if (!string.IsNullOrEmpty(child.Key) && child.Value != null)
                    equippedItems[child.Key] = child.Value.ToString();
        }

        // New account when there was no coins value stored yet.
        return !hasCoinsData;
    }

    private void ResetLocalState()
    {
        coins = 0;
        ownedItems.Clear();
        equippedItems.Clear();
        _dataLoaded = false;
        OnCoinsChanged?.Invoke();
        OnInventoryChanged?.Invoke();
    }

    #endregion
}