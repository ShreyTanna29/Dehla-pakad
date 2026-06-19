using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Keeps the Player Profile panel's display in sync with the saved profile every time the panel is
/// shown. <see cref="PlayerProfileManager"/> only updates the account-card name/avatar; this binder
/// additionally refreshes the prominent sidebar name + avatar and the "Synced with" email (from the
/// signed-in Google/Firebase account). Self-resolves children by name, so it survives panel rebuilds.
/// </summary>
public class PlayerProfilePanelBinder : MonoBehaviour
{
    const string PREFS_USERNAME = "PlayerUsername";
    const string PREFS_AVATAR_INDEX = "PlayerAvatarIndex";
    const string PREFS_EMAIL = "PlayerEmail";

    void OnEnable() { Refresh(); }

    public void Refresh()
    {
        string username = PlayerPrefs.GetString(PREFS_USERNAME, "Player");
        int avatarIndex = PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, 0);
        Sprite avatar = ResolveAvatar(avatarIndex);

        // Sidebar
        SetText("Side_Name", username);
        SetChildSprite("Side_AvatarFrame", "Img", avatar);

        // Account card (redundant with manager but guarantees freshness on reopen)
        SetText("Text_ProfileName", username);
        SetSprite("Img_CurrentAvatar", avatar);

        // Email ("Synced with")
        SetText("Email", ResolveEmail());
    }

    Sprite ResolveAvatar(int index)
    {
        var pm = PlayerProfileManager.Instance;
        if (pm != null && pm.profileSprites != null && index >= 0 && index < pm.profileSprites.Length)
            return pm.profileSprites[index];
        return null;
    }

    string ResolveEmail()
    {
        // Prefer the live Firebase account email; fall back to the cached one from login.
        try
        {
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance != null
                ? Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser : null;
            if (user != null && !string.IsNullOrEmpty(user.Email))
            {
                PlayerPrefs.SetString(PREFS_EMAIL, user.Email);
                return user.Email;
            }
        }
        catch { /* Firebase not ready in some contexts — fall back below */ }

        string cached = PlayerPrefs.GetString(PREFS_EMAIL, "");
        return string.IsNullOrEmpty(cached) ? "Not signed in" : cached;
    }

    // ---- helpers ----
    void SetText(string childName, string value)
    {
        Transform t = FindDeep(transform, childName);
        if (t == null) return;
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp != null) tmp.text = value;
    }

    void SetSprite(string childName, Sprite sprite)
    {
        if (sprite == null) return;
        Transform t = FindDeep(transform, childName);
        if (t == null) return;
        var img = t.GetComponent<Image>();
        if (img != null) { img.sprite = sprite; img.preserveAspect = true; }
    }

    void SetChildSprite(string parentName, string childName, Sprite sprite)
    {
        if (sprite == null) return;
        Transform p = FindDeep(transform, parentName);
        if (p == null) return;
        Transform c = p.Find(childName);
        if (c == null) c = FindDeep(p, childName);
        if (c == null) return;
        var img = c.GetComponent<Image>();
        if (img != null) { img.sprite = sprite; img.preserveAspect = true; }
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
