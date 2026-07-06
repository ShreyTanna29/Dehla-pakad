using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Activates the remaining profile-panel buttons that previously did nothing: the trophy
/// World/Country tabs, the Info and Share buttons, and the Inventory cart / magic / "Get more items"
/// buttons. Self-resolves by name and wires runtime behaviour (with toast feedback) every time the
/// panel is enabled, so no fragile inspector wiring is needed.
/// </summary>
public class ProfileMiscButtons : MonoBehaviour
{
    [Tooltip("When enabled, tab colors and button graphics set in the Editor are never changed at runtime.")]
    [SerializeField] private bool preserveManualAppearance = true;

    bool _wired;

    void OnEnable() { Wire(); }

    void Wire()
    {
        if (_wired) return;

        // Trophy region toggle: World / Country
        Button world = Find<Button>("Tab_World");
        Button country = Find<Button>("Tab_Country");
        if (world != null) { world.onClick.RemoveAllListeners(); world.onClick.AddListener(() => SelectTrophyTab(true)); }
        if (country != null) { country.onClick.RemoveAllListeners(); country.onClick.AddListener(() => SelectTrophyTab(false)); }

        // Info / Share
        WireToast("Btn_Info", "Your profile, stats & trophies — tap a deck in Inventory to equip it.");
        WireShare("Btn_Share");

        // Inventory actions
        WireToast("Btn_Cart", "Shop is coming soon!");
        WireToast("Btn_Magic", "Customization is coming soon!");
        WireToast("Item_More", "More items coming soon!");
        WireDeleteAccount();

        _wired = true;
    }

    void SelectTrophyTab(bool world)
    {
        if (!preserveManualAppearance)
        {
            Image wImg = Find<Image>("Tab_World");
            Image cImg = Find<Image>("Tab_Country");
            TMP_Text wL = FindLabel("Tab_World");
            TMP_Text cL = FindLabel("Tab_Country");
            if (wImg != null) wImg.color = world ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            if (cImg != null) cImg.color = world ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
            if (wL != null) wL.color = world ? Color.white : new Color(1f, 0.93f, 0.82f, 0.62f);
            if (cL != null) cL.color = world ? new Color(1f, 0.93f, 0.82f, 0.62f) : Color.white;
        }

        ProfileToast.Show(transform, world ? "Showing World rankings" : "Showing Country rankings");
    }

    void WireDeleteAccount()
    {
        Transform t = FindDeep(transform, "DeleteAccount");
        if (t == null) return;

        Button btn = t.GetComponent<Button>();
        if (btn == null)
        {
            if (preserveManualAppearance) return;

            btn = t.gameObject.AddComponent<Button>();
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.raycastTarget = true;
                btn.targetGraphic = tmp;
            }
            else
            {
                var img = t.GetComponent<Image>();
                if (img == null) img = t.gameObject.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.01f);
                btn.targetGraphic = img;
            }
            btn.transition = Selectable.Transition.ColorTint;
        }

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnDeleteAccountClicked);
    }

    void OnDeleteAccountClicked()
    {
        if (LogoutManager.Instance != null)
        {
            LogoutManager.Instance.DeleteAccount();
            return;
        }

        if (PlayerProfileManager.Instance != null)
        {
            PlayerProfileManager.Instance.DeleteAccount((ok, err) =>
            {
                if (ok)
                {
                    if (PlayerProfileManager.Instance.panelPlayerProfile != null)
                        PlayerProfileManager.Instance.panelPlayerProfile.SetActive(false);
                    ProfileToast.Show(transform, "Account deleted.");
                }
                else
                    ProfileToast.Show(transform, string.IsNullOrEmpty(err) ? "Could not delete account." : err);
            });
            return;
        }

        if (GoogleLogin.Instance != null)
            GoogleLogin.Instance.SignOut();
        ProfileToast.Show(transform, "Signed out.");
    }

    void WireToast(string name, string message)
    {
        Button b = Find<Button>(name);
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => ProfileToast.Show(transform, message));
    }

    void WireShare(string name)
    {
        Button b = Find<Button>(name);
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() =>
        {
            string uid = GameUidService.LocalGameUid;
            if (!string.IsNullOrEmpty(uid))
            {
                GUIUtility.systemCopyBuffer = uid;
                ProfileToast.Show(transform, "UID " + uid + " copied — share it with friends!");
            }
            else ProfileToast.Show(transform, "Share your profile with friends!");
        });
    }

    // ---- helpers ----
    T Find<T>(string name) where T : Component
    {
        Transform t = FindDeep(transform, name);
        return t != null ? t.GetComponent<T>() : null;
    }
    TMP_Text FindLabel(string parentName)
    {
        Transform p = FindDeep(transform, parentName);
        if (p == null) return null;
        Transform l = p.Find("Label");
        return l != null ? l.GetComponent<TMP_Text>() : null;
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
}
