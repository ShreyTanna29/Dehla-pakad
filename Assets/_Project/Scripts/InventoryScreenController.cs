using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the Inventory screen: the Cards / Wallpapers / Avatars sub-tabs and selectable item cards.
/// The selected card deck is persisted to PlayerPrefs ("InvSelected_Cards" etc.). Self-wires on
/// enable. Item content panels are plain child GameObjects named Sub_Cards / Sub_Wallpapers /
/// Sub_Avatars; selectable items live under each and are tagged via <see cref="RegisterItem"/>.
/// </summary>
public class InventoryScreenController : MonoBehaviour
{
    [System.Serializable]
    public class SubTab
    {
        public string id;
        public Button tabButton;
        public Image tabBg;
        public TMP_Text tabLabel;
        public GameObject content;
    }

    public List<SubTab> subTabs = new List<SubTab>();
    public string defaultSubTab = "Cards";

    // Shop / inventory sub-tab colors — selected #B8451F, unselected #F2A85C.
    static readonly Color TabActive   = new Color(0xB8 / 255f, 0x45 / 255f, 0x1F / 255f, 1f);
    static readonly Color TabInactive = new Color(0xF2 / 255f, 0xA8 / 255f, 0x5C / 255f, 1f);

    [Tooltip("When enabled, tab label text colors are left as set in the Editor.")]
    [SerializeField] private bool preserveManualTabLabels = true;

    string _current;
    bool _wired;

    void OnEnable()
    {
        EnsureResolved();
        Wire();
        WireCardDeckItems();
        ShowSub(string.IsNullOrEmpty(_current) ? defaultSubTab : _current);
    }

    /// <summary>
    /// Repairs the sub-tab list when its serialized object references have been lost (e.g. after a
    /// scene merge). If the list is empty it is rebuilt from the known sub-tab ids; for any entry that
    /// is missing references they are resolved by name (Tab_&lt;Id&gt; button + Sub_&lt;Id&gt; content).
    /// </summary>
    void EnsureResolved()
    {
        if (subTabs == null || subTabs.Count == 0)
        {
            subTabs = new List<SubTab>
            {
                new SubTab { id = "Cards" },
                new SubTab { id = "Wallpapers" },
                new SubTab { id = "Avatars" },
                new SubTab { id = "Voice" },
            };
        }

        foreach (SubTab t in subTabs)
        {
            if (t == null || string.IsNullOrEmpty(t.id)) continue;

            Transform tab = FindDeep(transform, "Tab_" + t.id);
            if (tab != null)
            {
                if (t.tabButton == null) t.tabButton = tab.GetComponent<Button>();
                if (t.tabBg == null) t.tabBg = tab.GetComponent<Image>();
                if (t.tabLabel == null)
                {
                    Transform l = tab.Find("Label");
                    if (l != null) t.tabLabel = l.GetComponent<TMP_Text>();
                }
            }
            if (t.content == null)
            {
                Transform c = FindDeep(transform, "Sub_" + t.id);
                if (c != null) t.content = c.gameObject;
            }
        }
    }

    static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform r = FindDeep(parent.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    void Wire()
    {
        if (_wired) return;
        foreach (SubTab t in subTabs)
        {
            if (t == null || t.tabButton == null) continue;
            string id = t.id;
            t.tabButton.onClick.RemoveAllListeners();
            t.tabButton.onClick.AddListener(() => ShowSub(id));
        }
        _wired = true;
    }

    public void ShowSub(string id)
    {
        _current = id;
        foreach (SubTab t in subTabs)
        {
            if (t == null) continue;
            bool active = t.id == id;
            if (t.content != null) t.content.SetActive(active);
            if (t.tabBg != null) t.tabBg.color = active ? TabActive : TabInactive;
            if (!preserveManualTabLabels && t.tabLabel != null)
                t.tabLabel.color = active ? Color.white : new Color(0.30f, 0.16f, 0.05f, 1f);
        }
    }

    /// <summary>
    /// Wires a selectable item card. Clicking selects it within its category, persists the choice,
    /// and moves the check mark. <paramref name="checkMark"/> is shown only on the selected item.
    /// </summary>
    public void RegisterItem(string category, string itemId, Button button, GameObject checkMark, List<GameObject> allChecksInCategory, List<string> allItemIdsInCategory)
    {
        if (button == null) return;
        string prefKey = "InvSelected_" + category;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            PlayerPrefs.SetString(prefKey, itemId);
            PlayerPrefs.Save();
            for (int i = 0; i < allChecksInCategory.Count; i++)
                if (allChecksInCategory[i] != null)
                    allChecksInCategory[i].SetActive(allItemIdsInCategory[i] == itemId);
        });
    }

    public static string GetSelected(string category, string fallback)
    {
        return PlayerPrefs.GetString("InvSelected_" + category, fallback);
    }

    /// <summary>
    /// Wires inventory Cards tab Item_Classic / Item_Modern buttons to <see cref="CardBackStyle"/>.
    /// Also fills empty Mini preview images with the matching card-back sprites.
    /// </summary>
    void WireCardDeckItems()
    {
        Transform classic = FindDeep(transform, "Item_Classic");
        Transform modern = FindDeep(transform, "Item_Modern");
        if (classic == null && modern == null) return;

        GameObject classicCheck = classic != null ? FindDeep(classic, "Check")?.gameObject : null;
        GameObject modernCheck = modern != null ? FindDeep(modern, "Check")?.gameObject : null;

        Button classicBtn = classic != null ? classic.GetComponent<Button>() : null;
        Button modernBtn = modern != null ? modern.GetComponent<Button>() : null;

        if (classicBtn != null)
        {
            classicBtn.onClick.RemoveAllListeners();
            classicBtn.onClick.AddListener(() =>
            {
                CardBackStyle.Select(CardBackStyle.ClassicId);
                RefreshCardDeckChecks(classicCheck, modernCheck);
            });
        }

        if (modernBtn != null)
        {
            modernBtn.onClick.RemoveAllListeners();
            modernBtn.onClick.AddListener(() =>
            {
                CardBackStyle.Select(CardBackStyle.ModernId);
                RefreshCardDeckChecks(classicCheck, modernCheck);
            });
        }

        RefreshCardDeckChecks(classicCheck, modernCheck);
        ApplyCardFanPreviewSprites(classic, false);
        ApplyCardFanPreviewSprites(modern, true);
    }

    static void RefreshCardDeckChecks(GameObject classicCheck, GameObject modernCheck)
    {
        bool modern = CardBackStyle.IsModernSelected;
        if (classicCheck != null) classicCheck.SetActive(!modern);
        if (modernCheck != null) modernCheck.SetActive(modern);
    }

    void ApplyCardFanPreviewSprites(Transform itemRoot, bool modern)
    {
        if (itemRoot == null) return;

        Sprite back = null;
        if (PlayWithFriendsManager.Instance != null)
        {
            back = modern
                ? PlayWithFriendsManager.Instance.modernCardBackSprite
                : PlayWithFriendsManager.Instance.classicCardBackSprite;
        }

        if (back == null) return;

        Image[] images = itemRoot.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image img = images[i];
            if (img == null) continue;
            if (!img.gameObject.name.StartsWith("Mini", System.StringComparison.OrdinalIgnoreCase))
                continue;

            img.sprite = back;
            img.color = Color.white;
            img.preserveAspect = true;
        }
    }

    /// <summary>
    /// Opens the Player Profile panel and navigates straight to the Inventory screen.
    /// Used by the settings spades/deck button so players can jump to their decks.
    /// </summary>
    public static void OpenInventoryScreen()
    {
        if (PlayerProfileManager.Instance != null)
            PlayerProfileManager.Instance.OpenPlayerProfile();

        ProfilePanelTabController[] tabs = Resources.FindObjectsOfTypeAll<ProfilePanelTabController>();
        foreach (ProfilePanelTabController c in tabs)
        {
            if (c == null || !c.gameObject.scene.IsValid()) continue;
            if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
            c.transform.SetAsLastSibling();
            c.ShowTab("Inventory");
            return;
        }
    }
}
