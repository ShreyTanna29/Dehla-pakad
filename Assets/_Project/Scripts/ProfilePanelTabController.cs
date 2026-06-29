using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Drives the left-sidebar navigation of the Player Profile panel. Each <see cref="Tab"/> pairs a
/// nav button with the content screen it shows. Clicking a nav button activates that screen and
/// deactivates the others, and updates the active/inactive highlight. Self-wires on enable so it
/// works every time the panel is opened.
/// </summary>
public class ProfilePanelTabController : MonoBehaviour
{
    [System.Serializable]
    public class Tab
    {
        public string id;
        public UnityEngine.UI.Button navButton;
        public GameObject screen;
        public TMP_Text label;
        public UnityEngine.UI.Image navBg;
    }

    public List<Tab> tabs = new List<Tab>();
    public string defaultTab = "Profile";

    [Header("Appearance")]
    [Tooltip("When enabled, nav sprites and label colors set in the Editor are never overwritten at runtime.")]
    [SerializeField] private bool preserveManualNavAppearance = true;

    bool _wired;
    string _current;

    void OnEnable()
    {
        Wire();
        ShowTab(string.IsNullOrEmpty(_current) ? defaultTab : _current);
    }

    void Wire()
    {
        if (_wired) return;
        foreach (Tab t in tabs)
        {
            if (t == null || t.navButton == null) continue;
            string id = t.id;
            t.navButton.onClick.RemoveAllListeners();
            t.navButton.onClick.AddListener(() => ShowTab(id));
        }
        _wired = true;
    }

    public void ShowTab(string id)
    {
        _current = id;
        foreach (Tab t in tabs)
        {
            if (t == null) continue;
            bool active = t.id == id;
            if (t.screen != null) t.screen.SetActive(active);

            // Only switch which screen is visible — images and text colors stay as authored in the scene.
            if (preserveManualNavAppearance)
                continue;

            if (t.label != null)
                t.label.color = active
                    ? new Color(0.984f, 0.937f, 0.851f, 1f)
                    : new Color(0.227f, 0.141f, 0.071f, 1f);
        }
    }
}
