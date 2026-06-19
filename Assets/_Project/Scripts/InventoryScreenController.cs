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

    static readonly Color TabActive = new Color(0.72f, 0.27f, 0.12f, 1f);   // dark red
    static readonly Color TabInactive = new Color(0.95f, 0.66f, 0.36f, 1f); // light orange
    static readonly Color TabActiveText = Color.white;
    static readonly Color TabInactiveText = new Color(0.30f, 0.16f, 0.05f, 1f);

    string _current;
    bool _wired;

    void OnEnable()
    {
        Wire();
        ShowSub(string.IsNullOrEmpty(_current) ? defaultSubTab : _current);
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
            if (t.tabLabel != null) t.tabLabel.color = active ? TabActiveText : TabInactiveText;
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
}
