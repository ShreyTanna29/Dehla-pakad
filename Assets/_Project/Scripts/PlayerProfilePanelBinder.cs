using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Optional profile-panel data binder. When <see cref="preserveManualLayout"/> is enabled (default),
/// the scene-authored Player Profile UI is never modified at runtime.
/// </summary>
public class PlayerProfilePanelBinder : MonoBehaviour
{
    [Tooltip("When enabled, text, images, and visibility set in the Editor are kept exactly as authored.")]
    [SerializeField] private bool preserveManualLayout = true;

    const string PREFS_USERNAME = "PlayerUsername";
    const string PREFS_AVATAR_INDEX = "PlayerAvatarIndex";
    const string PREFS_EMAIL = "PlayerEmail";

    void OnEnable()
    {
        if (!preserveManualLayout)
            Refresh();
    }

    public void Refresh()
    {
        if (preserveManualLayout) return;

        string username = PlayerPrefs.GetString(PREFS_USERNAME, "Player");
        int avatarIndex = PlayerPrefs.GetInt(PREFS_AVATAR_INDEX, 0);
        Sprite avatar = ResolveAvatar(avatarIndex);

        SetText("Side_Name", username);
        SetChildSprite("Side_AvatarFrame", "Img", avatar);
        SetText("Text_ProfileName", username);
        SetSprite("Img_CurrentAvatar", avatar);
        UpdateSyncedWithSection();
    }

    void UpdateSyncedWithSection()
    {
        bool isGuest = GoogleLogin.IsGuestSession();

        Transform email = FindDeep(transform, "Email");
        Transform linkBtn = FindDeep(transform, "Btn_LinkGoogle");

        if (isGuest)
        {
            if (email != null) email.gameObject.SetActive(false);
            if (linkBtn != null)
            {
                linkBtn.gameObject.SetActive(true);
                WireLinkButton(linkBtn);
            }
        }
        else
        {
            if (linkBtn != null) linkBtn.gameObject.SetActive(false);
            if (email != null) email.gameObject.SetActive(true);
            SetText("Email", ResolveEmail());
        }
    }

    void WireLinkButton(Transform linkBtn)
    {
        Button btn = linkBtn.GetComponent<Button>();
        if (btn == null) return;
        btn.onClick.RemoveListener(OnLinkGoogleClicked);
        btn.onClick.AddListener(OnLinkGoogleClicked);
    }

    void OnLinkGoogleClicked()
    {
        if (GoogleLogin.Instance != null)
            GoogleLogin.Instance.LinkGuestWithGoogle();
        else
            Debug.LogWarning("[ProfileBinder] GoogleLogin.Instance is null — cannot bind Google.");
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
        catch { }

        string cached = PlayerPrefs.GetString(PREFS_EMAIL, "");
        return string.IsNullOrEmpty(cached) ? "Not signed in" : cached;
    }

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
