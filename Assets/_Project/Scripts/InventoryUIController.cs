using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A purchasable / equippable cosmetic item shown in the shop grid.
/// Mock data model — populate <see cref="InventoryUIController.catalog"/> in the inspector
/// (or rely on the built-in sample data) to drive the grid.
/// </summary>
[System.Serializable]
public class ShopItem
{
    public string id;
    public string name;
    public int price;
    public Sprite icon;
    public string category; // "Cards", "Wallpapers", "Avatars"

    public ShopItem() { }

    public ShopItem(string id, string name, int price, string category, Sprite icon = null)
    {
        this.id = id;
        this.name = name;
        this.price = price;
        this.category = category;
        this.icon = icon;
    }
}

/// <summary>
/// Drives the shop content area: Cards / Wallpapers / Avatars tabs and the item grid.
/// Reads ownership and balance from <see cref="CurrencyAndInventoryManager"/> and refreshes itself in
/// real time via that manager's change events. Standalone — it does not modify gameplay scripts.
///
/// Expected item prefab structure (matched by child name, all optional but recommended):
///   • "ItemName"    — TMP_Text   (item display name)
///   • "Icon"        — Image      (item icon)
///   • "ActionButton"— Button     (Buy / Equip / Equipped); its child TMP_Text is used as the label
/// If "ActionButton" is not found, the first Button in the prefab is used; the first TMP_Text under
/// that button is treated as its label.
/// </summary>
public class InventoryUIController : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text coinsText;
    public Transform itemGridContainer;
    public GameObject itemPrefab;
    public Button[] tabButtons;

    [Tooltip("Category for each tab button, index-matched to Tab Buttons.")]
    public string[] tabCategories = { "Cards", "Wallpapers", "Avatars" };

    [Header("Catalog (leave empty to use built-in sample data)")]
    public List<ShopItem> catalog = new List<ShopItem>();

    static readonly Color TabSelectedColor = new Color(0xB8 / 255f, 0x45 / 255f, 0x1F / 255f, 1f);
    static readonly Color TabUnselectedColor = new Color(0xF2 / 255f, 0xA8 / 255f, 0x5C / 255f, 1f);

    private string _currentCategory;
    private bool _subscribed;

    private CurrencyAndInventoryManager Currency => CurrencyAndInventoryManager.Instance;

    #region Lifecycle

    private void Awake()
    {
        if (catalog == null || catalog.Count == 0)
            catalog = BuildSampleCatalog();

        WireTabs();
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshCoins();

        int startIndex = ResolveTabIndex(_currentCategory);
        string startCategory = (tabCategories != null && tabCategories.Length > startIndex)
            ? tabCategories[startIndex]
            : "Cards";
        PopulateGrid(string.IsNullOrEmpty(_currentCategory) ? startCategory : _currentCategory);
        UpdateTabVisuals(startIndex);
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed || Currency == null) return;
        Currency.OnCoinsChanged += RefreshCoins;
        Currency.OnInventoryChanged += RefreshGrid;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || Currency == null) return;
        Currency.OnCoinsChanged -= RefreshCoins;
        Currency.OnInventoryChanged -= RefreshGrid;
        _subscribed = false;
    }

    #endregion

    #region Tabs

    private void WireTabs()
    {
        if (tabButtons == null) return;

        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;
            int index = i; // capture
            tabButtons[i].onClick.RemoveAllListeners();
            tabButtons[i].onClick.AddListener(() => OnTabClicked(index));
        }
    }

    private void OnTabClicked(int index)
    {
        if (tabCategories == null || index < 0 || index >= tabCategories.Length) return;
        UpdateTabVisuals(index);
        PopulateGrid(tabCategories[index]);
    }

    int ResolveTabIndex(string category)
    {
        if (string.IsNullOrEmpty(category) || tabCategories == null)
            return 0;

        for (int i = 0; i < tabCategories.Length; i++)
        {
            if (tabCategories[i] == category)
                return i;
        }

        return 0;
    }

    void UpdateTabVisuals(int selectedIndex)
    {
        if (tabButtons == null) return;

        for (int i = 0; i < tabButtons.Length; i++)
        {
            Button btn = tabButtons[i];
            if (btn == null) continue;

            btn.transition = Selectable.Transition.None;

            Image tabBg = btn.targetGraphic as Image;
            if (tabBg == null)
                tabBg = btn.GetComponent<Image>();

            if (tabBg != null)
                tabBg.color = i == selectedIndex ? TabSelectedColor : TabUnselectedColor;
        }
    }

    #endregion

    #region Grid

    /// <summary>Clears the grid and instantiates a card for every catalog item in the category.</summary>
    public void PopulateGrid(string category)
    {
        _currentCategory = category;

        if (itemGridContainer == null || itemPrefab == null)
        {
            Debug.LogWarning("[InventoryUI] Grid container or item prefab not assigned.");
            return;
        }

        // Clear existing cards.
        for (int i = itemGridContainer.childCount - 1; i >= 0; i--)
            Destroy(itemGridContainer.GetChild(i).gameObject);

        foreach (ShopItem item in catalog)
        {
            if (item == null || item.category != category) continue;

            GameObject card = Instantiate(itemPrefab, itemGridContainer);
            card.SetActive(true);
            ConfigureCard(card, item);
        }
    }

    private void RefreshGrid()
    {
        if (!string.IsNullOrEmpty(_currentCategory))
            PopulateGrid(_currentCategory);
    }

    private void ConfigureCard(GameObject card, ShopItem item)
    {
        // Name
        TMP_Text nameText = FindComponent<TMP_Text>(card.transform, "ItemName");
        if (nameText != null) nameText.text = item.name;

        // Icon
        Image icon = FindComponent<Image>(card.transform, "Icon");
        if (icon != null)
        {
            if (item.icon != null)
            {
                icon.sprite = item.icon;
                icon.color = Color.white; // show the sprite without the placeholder tint
                icon.enabled = true;
            }
            else
            {
                icon.enabled = false;
            }
        }

        // Action button + label
        Button actionButton = FindComponent<Button>(card.transform, "ActionButton");
        if (actionButton == null)
            actionButton = card.GetComponentInChildren<Button>(true);

        if (actionButton == null)
        {
            Debug.LogWarning("[InventoryUI] Item prefab has no Button for the action control.");
            return;
        }

        TMP_Text actionLabel = actionButton.GetComponentInChildren<TMP_Text>(true);
        actionButton.onClick.RemoveAllListeners();

        bool owned = Currency != null && Currency.HasItem(item.id);

        if (owned)
        {
            bool equipped = Currency.IsEquipped(item.id, item.category);
            if (actionLabel != null) actionLabel.text = equipped ? "Equipped" : "Equip";
            actionButton.interactable = !equipped;

            if (!equipped)
            {
                string id = item.id;
                string cat = item.category;
                actionButton.onClick.AddListener(() => OnEquipClicked(id, cat));
            }
        }
        else
        {
            if (actionLabel != null) actionLabel.text = $"Buy for {item.price} Coins";
            actionButton.interactable = true;

            ShopItem captured = item;
            actionButton.onClick.AddListener(() => OnBuyClicked(captured));
        }
    }

    #endregion

    #region Actions

    private void OnBuyClicked(ShopItem item)
    {
        if (Currency == null)
        {
            Debug.LogWarning("[InventoryUI] CurrencyAndInventoryManager not available.");
            return;
        }

        // BuyItem internally checks affordability and ownership, then fires OnInventoryChanged,
        // which calls RefreshGrid() to update this card.
        Currency.BuyItem(item.id, item.price, item.category);
    }

    private void OnEquipClicked(string itemId, string category)
    {
        if (Currency == null) return;
        Currency.EquipItem(itemId, category);
    }

    #endregion

    #region Coins display

    private void RefreshCoins()
    {
        if (coinsText != null && Currency != null)
            coinsText.text = Currency.Coins.ToString();
    }

    #endregion

    #region Helpers

    private static T FindComponent<T>(Transform root, string childName) where T : Component
    {
        Transform t = FindDeep(root, childName);
        return t != null ? t.GetComponent<T>() : null;
    }

    private static Transform FindDeep(Transform parent, string childName)
    {
        if (parent.name == childName) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindDeep(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    private static List<ShopItem> BuildSampleCatalog()
    {
        return new List<ShopItem>
        {
            // Cards
            new ShopItem("card_classic",  "Classic Deck",  0,    "Cards"),
            new ShopItem("card_blue",     "Blue Deck",     100,  "Cards"),
            new ShopItem("card_royal",    "Royal Deck",    250,  "Cards"),
            new ShopItem("card_gold",     "Gold Deck",     500,  "Cards"),

            // Wallpapers
            new ShopItem("wp_green",      "Green Table",   0,    "Wallpapers"),
            new ShopItem("wp_wood",       "Wooden Table",  150,  "Wallpapers"),
            new ShopItem("wp_marble",     "Marble Table",  300,  "Wallpapers"),

            // Avatars
            new ShopItem("avatar_raja",   "Raja",          0,    "Avatars"),
            new ShopItem("avatar_rani",   "Rani",          120,  "Avatars"),
            new ShopItem("avatar_wizard", "Wizard",        400,  "Avatars"),
        };
    }

    #endregion
}