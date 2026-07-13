using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Extensions;

/// <summary>
/// Central store for the local player's soft-currency (coins) and owned/equipped cosmetic items.
/// Holds an authoritative in-memory copy and mirrors it to Firestore under
/// <c>users/{userId}</c> fields: <c>coins</c>, <c>inventory</c>, <c>equipped</c>.
///
/// UI (e.g. <see cref="InventoryUIController"/>) and IAP (<see cref="ShopIAPManager"/>) talk to this
/// manager only — they never touch Firebase directly. Subscribe to <see cref="OnCoinsChanged"/> and
/// <see cref="OnInventoryChanged"/> to refresh the UI in real time.
/// </summary>
public class CurrencyAndInventoryManager : MonoBehaviour
{
    public static CurrencyAndInventoryManager Instance { get; private set; }

    private const int DefaultNewAccountCoins = 100;

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private int coins;
    [SerializeField] private List<string> ownedItems = new List<string>();

    private readonly Dictionary<string, string> equippedItems = new Dictionary<string, string>();

    private bool _dataLoaded;
    private bool _authHooked;

    public event Action OnCoinsChanged;
    public event Action OnInventoryChanged;

    public int Coins => coins;
    public bool IsDataLoaded => _dataLoaded;
    public IReadOnlyList<string> OwnedItems => ownedItems;

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

    #region Coins

    public void ProcessRewardsLogic()
    {
        const string SignupRewardKey = "Reward_SignupGranted";

        if (PlayerPrefs.GetInt(SignupRewardKey, 0) == 1)
            return;

        AddCoins(DefaultNewAccountCoins);
        GrantDefaultFreeVoicePacks();
        PlayerPrefs.SetInt(SignupRewardKey, 1);
        PlayerPrefs.Save();
        Debug.Log($"[Rewards] Signup bonus granted: +{DefaultNewAccountCoins} coins.");
    }

    public void GrantDefaultFreeVoicePacks()
    {
        bool changed = false;
        foreach (string id in DefaultFreeVoicePackIds)
        {
            if (HasItem(id)) continue;
            ownedItems.Add(id);
            changed = true;
        }
        if (changed)
            SyncInventory();
    }

    static readonly string[] DefaultFreeVoicePackIds =
    {
        "voice_default_1",
        "voice_default_2"
    };

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
        string uid = UserId;
        if (string.IsNullOrEmpty(uid)) return;

        FirestoreUsersService.MergeUser(uid, new Dictionary<string, object>
        {
            { "coins", coins }
        }, ok =>
        {
            if (!ok) Debug.LogError("[Currency] Failed to sync coins to Firestore.");
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

        if (cost > 0 && !DeductCoins(cost))
        {
            Debug.LogWarning($"[Currency] Purchase failed (insufficient coins): {itemId}");
            return;
        }

        ownedItems.Add(itemId);
        SyncInventory();
        EquipItem(itemId, category);
        Debug.Log($"[Currency] Bought {itemId} for {cost} coins (category: {category}).");
    }

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
        string uid = UserId;
        if (string.IsNullOrEmpty(uid)) return;

        var map = new Dictionary<string, object>();
        foreach (string id in ownedItems)
            map[id] = true;

        FirestoreUsersService.MergeUser(uid, new Dictionary<string, object>
        {
            { "inventory", map }
        }, ok =>
        {
            if (!ok) Debug.LogError("[Currency] Failed to sync inventory to Firestore.");
        });
    }

    private void SyncEquipped()
    {
        string uid = UserId;
        if (string.IsNullOrEmpty(uid)) return;

        var map = new Dictionary<string, object>();
        foreach (KeyValuePair<string, string> kv in equippedItems)
            map[kv.Key] = kv.Value;

        FirestoreUsersService.MergeUser(uid, new Dictionary<string, object>
        {
            { "equipped", map }
        }, ok =>
        {
            if (!ok) Debug.LogError("[Currency] Failed to sync equipped items to Firestore.");
        });
    }

    #endregion

    #region Load

    public void LoadUserData()
    {
        string uid = UserId;
        if (string.IsNullOrEmpty(uid)) return;

        FirestoreUsersService.GetUser(uid, snap =>
        {
            bool isNewAccount = ApplySnapshot(snap);

            if (isNewAccount)
                ProcessRewardsLogic();
            else
                GrantDefaultFreeVoicePacks();

            _dataLoaded = true;
            OnCoinsChanged?.Invoke();
            OnInventoryChanged?.Invoke();
            Debug.Log($"[Currency] Loaded economy (Firestore): {coins} coins, {ownedItems.Count} items, {equippedItems.Count} equipped.");
        });
    }

    /// <summary>Returns true when the account has no coins field yet (brand-new).</summary>
    private bool ApplySnapshot(DocumentSnapshot snap)
    {
        coins = 0;
        ownedItems.Clear();
        equippedItems.Clear();

        if (snap == null || !snap.Exists)
            return true;

        bool hasCoinsData = snap.ContainsField("coins");
        if (hasCoinsData)
        {
            try { coins = Convert.ToInt32(snap.GetValue<object>("coins")); }
            catch { coins = 0; }
        }

        if (snap.ContainsField("inventory"))
        {
            try
            {
                Dictionary<string, object> inv = snap.GetValue<Dictionary<string, object>>("inventory");
                if (inv != null)
                {
                    foreach (string key in inv.Keys)
                        if (!string.IsNullOrEmpty(key) && !ownedItems.Contains(key))
                            ownedItems.Add(key);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Currency] inventory parse failed: " + e.Message);
            }
        }

        if (snap.ContainsField("equipped"))
        {
            try
            {
                Dictionary<string, object> eq = snap.GetValue<Dictionary<string, object>>("equipped");
                if (eq != null)
                {
                    foreach (KeyValuePair<string, object> kv in eq)
                        if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                            equippedItems[kv.Key] = kv.Value.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Currency] equipped parse failed: " + e.Message);
            }
        }

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
