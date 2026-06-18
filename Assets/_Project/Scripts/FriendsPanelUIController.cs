using System.Collections.Generic;
using System.Linq;
using Firebase.Database;
using Firebase.Extensions;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wooden Friends panel matching the approved mockup: two tabs (FRIENDS / REQUESTS).
/// Rows are built programmatically — circular avatar, name, and circular icon buttons
/// (red person-x = decline/remove, green person-plus = accept/invite). No row prefabs needed.
/// </summary>
public class FriendsPanelUIController : MonoBehaviour
{
    public static FriendsPanelUIController Instance;

    public enum PanelTab
    {
        Friends,
        Requests
    }

    [Header("Tabs")]
    public Button friendsTabButton;
    public Button requestsTabButton;
    public Image friendsTabBg;
    public Image requestsTabBg;
    public GameObject requestsBadge;
    public TMP_Text requestsBadgeText;

    [Header("Content Roots")]
    public GameObject friendsContent;
    public GameObject requestsContent;
    public Transform friendsListContainer;
    public Transform requestsListContainer;
    public GameObject friendsEmptyLabel;
    public GameObject requestsEmptyLabel;

    [Header("Search (in Friends tab)")]
    public TMP_InputField searchInputField;
    public Button searchButton;
    public Button searchClearButton;

    [Header("Theme (auto-loaded in editor if empty)")]
    public Sprite circleFrameSprite;
    public Sprite circleButtonSprite;
    public Sprite acceptIconSprite;
    public Sprite declineIconSprite;
    public List<Sprite> avatarPool = new List<Sprite>();
    public TMP_FontAsset customFont;

    // Reference mockup colors
    static readonly Color GreenBtn = Hex("#1ab26a");
    static readonly Color RedBtn = Hex("#df0007");
    static readonly Color ActiveTabTint = Color.white;
    static readonly Color InactiveTabTint = new Color(0.55f, 0.55f, 0.55f, 1f);
    static readonly Color DividerColor = new Color(0f, 0f, 0f, 0.28f);
    static readonly Color HighlightLineColor = new Color(1f, 1f, 1f, 0.10f);

    const string FirebaseDatabaseUrl = "https://dehla-pakad-a7859-default-rtdb.firebaseio.com/";

    PanelTab _activeTab = PanelTab.Friends;
    DatabaseReference _usersDb;
    bool _searchActive;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }
        ResolveThemeAssets();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        try { _usersDb = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference; }
        catch (System.Exception e) { Debug.LogWarning("[FriendsPanel] Firebase not ready: " + e.Message); }
        WireButtons();
        ShowTab(PanelTab.Friends);
    }

    void ResolveThemeAssets()
    {
#if UNITY_EDITOR
        if (circleFrameSprite == null)
            circleFrameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
        if (circleButtonSprite == null)
            circleButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Images/NEW/Circle_Solid.png");
        if (acceptIconSprite == null)
            acceptIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Images/NEW/Icon_AcceptFriend.png");
        if (declineIconSprite == null)
            declineIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Images/NEW/Icon_DeclineFriend.png");
        if (avatarPool == null || avatarPool.Count == 0)
        {
            avatarPool = new List<Sprite>();
            for (int i = 1; i <= 10; i++)
            {
                var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_Project/Art/Sprites/Profile_Images/Profile{i}.png");
                if (s != null) avatarPool.Add(s);
            }
        }
#endif
    }

    void WireButtons()
    {
        if (friendsTabButton != null)
        {
            friendsTabButton.onClick.RemoveAllListeners();
            friendsTabButton.onClick.AddListener(() => ShowTab(PanelTab.Friends));
        }
        if (requestsTabButton != null)
        {
            requestsTabButton.onClick.RemoveAllListeners();
            requestsTabButton.onClick.AddListener(() => ShowTab(PanelTab.Requests));
        }

        if (searchButton != null)
        {
            searchButton.onClick.RemoveAllListeners();
            searchButton.onClick.AddListener(SearchPlayersByName);
        }
        if (searchClearButton != null)
        {
            searchClearButton.onClick.RemoveAllListeners();
            searchClearButton.onClick.AddListener(ClearSearch);
            searchClearButton.gameObject.SetActive(false);
        }
        if (searchInputField != null)
        {
            searchInputField.onSubmit.RemoveAllListeners();
            searchInputField.onSubmit.AddListener(_ => SearchPlayersByName());
        }
    }

    public void ShowTab(PanelTab tab)
    {
        _activeTab = tab;

        // Leaving Friends tab cancels any active search.
        if (tab != PanelTab.Friends && _searchActive)
        {
            _searchActive = false;
            if (searchInputField != null) searchInputField.text = "";
            if (searchClearButton != null) searchClearButton.gameObject.SetActive(false);
        }

        if (friendsContent != null) friendsContent.SetActive(tab == PanelTab.Friends);
        if (requestsContent != null) requestsContent.SetActive(tab == PanelTab.Requests);

        if (friendsTabBg != null) friendsTabBg.color = tab == PanelTab.Friends ? ActiveTabTint : InactiveTabTint;
        if (requestsTabBg != null) requestsTabBg.color = tab == PanelTab.Requests ? ActiveTabTint : InactiveTabTint;

        RefreshAll();
    }

    /// <summary>Rebuild both tab lists from PlayWithFriendsManager data.</summary>
    public void RefreshAll()
    {
        RefreshFriendsList();
        RefreshRequestsList();
        UpdateRequestsBadge();
    }

    void RefreshFriendsList()
    {
        if (friendsListContainer == null) return;
        if (_searchActive) return; // search results are showing; don't overwrite
        ClearContainer(friendsListContainer);

        int count = 0;
        if (PlayWithFriendsManager.Instance != null)
        {
            IReadOnlyList<string> friends = PlayWithFriendsManager.Instance.MyFriends;
            if (friends != null)
            {
                foreach (string friendId in friends)
                {
                    if (string.IsNullOrEmpty(friendId)) continue;
                    string displayName = PlayWithFriendsManager.Instance.GetFriendDisplayName(friendId);
                    FriendInfo info = PlayWithFriendsManager.Instance.GetFriendPhotonInfo(friendId);
                    bool inviteSent = PlayWithFriendsManager.Instance.IsGameInviteSent(friendId);
                    BuildFriendRow(friendId, displayName, info, inviteSent);
                    count++;
                }
            }
        }

        if (friendsEmptyLabel != null) friendsEmptyLabel.SetActive(count == 0);
    }

    void RefreshRequestsList()
    {
        if (requestsListContainer == null) return;
        ClearContainer(requestsListContainer);

        int count = 0;
        if (PlayWithFriendsManager.Instance != null)
        {
            IReadOnlyDictionary<string, string> requests = PlayWithFriendsManager.Instance.IncomingRequests;
            if (requests != null)
            {
                foreach (KeyValuePair<string, string> kvp in requests)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    BuildRequestRow(kvp.Key, kvp.Value);
                    count++;
                }
            }
        }

        if (requestsEmptyLabel != null) requestsEmptyLabel.SetActive(count == 0);
    }

    void UpdateRequestsBadge()
    {
        if (PlayWithFriendsManager.Instance == null) return;
        int count = PlayWithFriendsManager.Instance.IncomingRequests?.Count ?? 0;
        if (requestsBadge != null) requestsBadge.SetActive(count > 0);
        if (requestsBadgeText != null) requestsBadgeText.text = count > 0 ? count.ToString() : "";
    }

    // ============================================================
    // ROW BUILDERS (match approved mockup)
    // ============================================================

    /// <summary>Request row: avatar + name + red decline + green accept.</summary>
    public GameObject BuildRequestRow(string fromId, string fromName)
    {
        string display = string.IsNullOrEmpty(fromName) ? "Player" : fromName;
        GameObject row = BuildBaseRow(requestsListContainer, display, fromId);

        // Green ACCEPT (far right)
        Button accept = CreateCircleButton(row.transform, "AcceptButton", GreenBtn, acceptIconSprite, -16f);
        // Red DECLINE (left of accept)
        Button decline = CreateCircleButton(row.transform, "DeclineButton", RedBtn, declineIconSprite, -116f);

        accept.onClick.RemoveAllListeners();
        accept.onClick.AddListener(() =>
        {
            if (PlayWithFriendsManager.Instance == null) return;
            PlayWithFriendsManager.Instance.AcceptFriendRequest(fromId, display);
            RefreshAll();
        });

        decline.onClick.RemoveAllListeners();
        decline.onClick.AddListener(() =>
        {
            if (PlayWithFriendsManager.Instance == null) return;
            PlayWithFriendsManager.Instance.DeclineFriendRequest(fromId);
            RefreshAll();
        });

        return row;
    }

    /// <summary>Friend row: avatar + name/status + green invite-to-game button.</summary>
    public GameObject BuildFriendRow(string friendId, string displayName, FriendInfo info, bool inviteSent)
    {
        string status = GetOnlineStatusText(info);
        GameObject row = BuildBaseRow(friendsListContainer, displayName, friendId, status);

        Button invite = CreateCircleButton(row.transform, "InviteButton", inviteSent ? new Color(0.45f, 0.45f, 0.45f, 1f) : GreenBtn, acceptIconSprite, -18f);
        invite.interactable = !inviteSent;
        invite.onClick.RemoveAllListeners();
        if (!inviteSent)
        {
            invite.onClick.AddListener(() =>
            {
                if (PlayWithFriendsManager.Instance == null) return;
                PlayWithFriendsManager.Instance.InviteFriendToGame(friendId, displayName);
                PlayWithFriendsManager.Instance.MarkGameInviteSent(friendId);
                RefreshFriendsList();
            });
        }

        return row;
    }

    GameObject BuildBaseRow(Transform parent, string displayName, string idForAvatar, string status = null)
    {
        if (parent == null) parent = friendsListContainer != null ? friendsListContainer : requestsListContainer;

        GameObject row = NewRect("Row", parent);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(0, 116);
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 116;
        le.minHeight = 116;

        // Bottom divider groove — full-width horizontal line under each friend (matches mockup).
        // Extend beyond the row's layout padding (left 16, right 60) to reach the panel inner edges.
        GameObject divider = NewRect("Divider", row.transform);
        RectTransform dvRt = divider.GetComponent<RectTransform>();
        dvRt.anchorMin = new Vector2(0f, 0f); dvRt.anchorMax = new Vector2(1f, 0f);
        dvRt.pivot = new Vector2(0.5f, 0f);
        dvRt.offsetMin = new Vector2(-16f, 1f);
        dvRt.offsetMax = new Vector2(60f, 5f);
        var dvImg = divider.AddComponent<Image>();
        dvImg.color = DividerColor;
        dvImg.raycastTarget = false;

        // Thin highlight just below the groove for an engraved plank edge
        GameObject hi = NewRect("Highlight", row.transform);
        RectTransform hiRt = hi.GetComponent<RectTransform>();
        hiRt.anchorMin = new Vector2(0f, 0f); hiRt.anchorMax = new Vector2(1f, 0f);
        hiRt.pivot = new Vector2(0.5f, 0f);
        hiRt.offsetMin = new Vector2(-16f, -1f);
        hiRt.offsetMax = new Vector2(60f, 1f);
        var hiImg = hi.AddComponent<Image>();
        hiImg.color = HighlightLineColor;
        hiImg.raycastTarget = false;

        // Avatar
        GameObject avatar = NewRect("Avatar", row.transform);
        RectTransform avRt = avatar.GetComponent<RectTransform>();
        avRt.anchorMin = avRt.anchorMax = new Vector2(0f, 0.5f);
        avRt.pivot = new Vector2(0f, 0.5f);
        avRt.sizeDelta = new Vector2(86, 86);
        avRt.anchoredPosition = new Vector2(20, 0);
        var avImg = avatar.AddComponent<Image>();
        avImg.preserveAspect = true;
        avImg.color = Color.white;
        Sprite av = GetAvatar(idForAvatar);
        if (av != null) avImg.sprite = av;
        else if (circleFrameSprite != null) { avImg.sprite = circleFrameSprite; avImg.color = new Color(0.3f, 0.55f, 0.85f, 1f); }

        // Name (+ optional status)
        GameObject nameGo = NewRect("Name", row.transform);
        RectTransform nmRt = nameGo.GetComponent<RectTransform>();
        nmRt.anchorMin = new Vector2(0f, 0.5f); nmRt.anchorMax = new Vector2(0f, 0.5f);
        nmRt.pivot = new Vector2(0f, 0.5f);
        nmRt.sizeDelta = new Vector2(300, 80);
        nmRt.anchoredPosition = new Vector2(125, 0);
        var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
        nameTmp.text = string.IsNullOrEmpty(status) ? displayName : $"{displayName}\n<size=22><color=#FFE6B0>{status}</color></size>";
        nameTmp.color = Color.white;
        nameTmp.fontSize = 34;
        nameTmp.fontStyle = FontStyles.Bold;
        nameTmp.alignment = TextAlignmentOptions.Left;
        nameTmp.overflowMode = TextOverflowModes.Ellipsis;
        nameTmp.enableWordWrapping = false;
        if (customFont != null) nameTmp.font = customFont;

        return row;
    }

    Button CreateCircleButton(Transform rowParent, string name, Color bg, Sprite icon, float xOffset)
    {
        GameObject go = NewRect(name, rowParent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(84, 84);
        rt.anchoredPosition = new Vector2(xOffset, 0);

        var img = go.AddComponent<Image>();
        img.color = bg;
        if (circleButtonSprite != null) { img.sprite = circleButtonSprite; img.type = Image.Type.Simple; }

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        // White icon glyph
        GameObject ic = NewRect("Icon", go.transform);
        RectTransform icRt = ic.GetComponent<RectTransform>();
        icRt.anchorMin = new Vector2(0.5f, 0.5f); icRt.anchorMax = new Vector2(0.5f, 0.5f);
        icRt.pivot = new Vector2(0.5f, 0.5f);
        icRt.sizeDelta = new Vector2(58, 58);
        icRt.anchoredPosition = Vector2.zero;
        var icImg = ic.AddComponent<Image>();
        icImg.color = Color.white;
        icImg.raycastTarget = false;
        icImg.preserveAspect = true;
        if (icon != null) icImg.sprite = icon;

        return btn;
    }

    // ============================================================
    // SEARCH (Friends tab)
    // ============================================================

    /// <summary>Search Firebase users by username and show add-able results in the Friends list area.</summary>
    public void SearchPlayersByName()
    {
        if (searchInputField == null) return;

        string query = searchInputField.text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            ClearSearch();
            return;
        }

        if (_usersDb == null)
        {
            try { _usersDb = FirebaseDatabase.GetInstance(FirebaseDatabaseUrl).RootReference; }
            catch { }
        }
        if (_usersDb == null) return;

        // Make sure we're on the Friends tab where results render.
        if (_activeTab != PanelTab.Friends) ShowTab(PanelTab.Friends);

        _searchActive = true;
        if (searchClearButton != null) searchClearButton.gameObject.SetActive(true);

        ClearContainer(friendsListContainer);
        ShowEmpty(friendsEmptyLabel, false);

        // A full 10-digit input is treated as a UID lookup (PUBG / Free Fire style).
        // Anything else is a username search. UID lookups fall back to username if not found.
        if (GameUidService.LooksLikeUid(query))
            SearchByUid(query);
        else
            SearchByUsername(query);
    }

    /// <summary>Resolve a 10-digit UID to an account and show it as an add-able result.</summary>
    void SearchByUid(string uid)
    {
        GameUidService.ResolveFirebaseUid(uid, firebaseUid =>
        {
            if (!_searchActive) return;

            // Not a real UID — try treating the digits as a username instead.
            if (string.IsNullOrEmpty(firebaseUid))
            {
                SearchByUsername(uid);
                return;
            }

            string localUserId = PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId;
            if (!string.IsNullOrEmpty(localUserId) && firebaseUid == localUserId)
            {
                ClearContainer(friendsListContainer);
                ShowEmpty(friendsEmptyLabel, true, "That's your own UID");
                return;
            }

            _usersDb.Child("users").Child(firebaseUid).Child("username")
                .GetValueAsync().ContinueWithOnMainThread(task =>
                {
                    if (!_searchActive) return;

                    ClearContainer(friendsListContainer);

                    string username = (task.Result != null && task.Result.Exists)
                        ? task.Result.Value?.ToString()
                        : null;
                    if (string.IsNullOrEmpty(username)) username = "Player " + uid;

                    BuildSearchRow(firebaseUid, username);
                    ShowEmpty(friendsEmptyLabel, false);
                });
        });
    }

    /// <summary>Prefix search over usernames.</summary>
    void SearchByUsername(string query)
    {
        if (_usersDb == null) return;

        _usersDb.Child("users").OrderByChild("username")
            .StartAt(query)
            .EndAt(query + "\uf8ff")
            .GetValueAsync().ContinueWithOnMainThread(task =>
            {
                if (!_searchActive) return; // user cleared meanwhile
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError("[FriendsPanel] Search failed.");
                    ShowEmpty(friendsEmptyLabel, true, "Search failed");
                    return;
                }

                ClearContainer(friendsListContainer);

                DataSnapshot snapshot = task.Result;
                string localUserId = PhotonNetwork.AuthValues?.UserId ?? PhotonNetwork.LocalPlayer?.UserId;
                int found = 0;

                if (snapshot != null && snapshot.Exists)
                {
                    foreach (DataSnapshot userSnapshot in snapshot.Children)
                    {
                        string foundUserId = userSnapshot.Key;
                        if (!userSnapshot.Child("username").Exists) continue;
                        string foundUsername = userSnapshot.Child("username").Value?.ToString();
                        if (string.IsNullOrEmpty(foundUsername)) continue;
                        if (!string.IsNullOrEmpty(localUserId) && foundUserId == localUserId) continue;

                        BuildSearchRow(foundUserId, foundUsername);
                        found++;
                    }
                }

                ShowEmpty(friendsEmptyLabel, found == 0, "No players found");
            });
    }

    public void ClearSearch()
    {
        _searchActive = false;
        if (searchInputField != null) searchInputField.text = "";
        if (searchClearButton != null) searchClearButton.gameObject.SetActive(false);
        RefreshFriendsList();
    }

    /// <summary>Search result row: avatar + name + green ADD (sends friend request).</summary>
    GameObject BuildSearchRow(string userId, string displayName)
    {
        GameObject row = BuildBaseRow(friendsListContainer, displayName, userId);

        bool alreadyFriend = PlayWithFriendsManager.Instance != null
            && PlayWithFriendsManager.Instance.MyFriends != null
            && PlayWithFriendsManager.Instance.MyFriends.Contains(userId);

        Button add = CreateCircleButton(row.transform, "AddButton",
            alreadyFriend ? new Color(0.45f, 0.45f, 0.45f, 1f) : GreenBtn, acceptIconSprite, -16f);
        add.interactable = !alreadyFriend;

        add.onClick.RemoveAllListeners();
        if (!alreadyFriend)
        {
            add.onClick.AddListener(() =>
            {
                if (PlayWithFriendsManager.Instance == null) return;
                PlayWithFriendsManager.Instance.SendFriendRequest(userId, displayName);
                // Mark as sent: grey out the button.
                var img = add.GetComponent<Image>();
                if (img != null) img.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                add.interactable = false;
            });
        }

        return row;
    }

    static void ShowEmpty(GameObject label, bool show, string text = null)
    {
        if (label == null) return;
        if (text != null)
        {
            var tmp = label.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = text;
        }
        label.SetActive(show);
    }

    // ============================================================
    // HELPERS
    // ============================================================

    Sprite GetAvatar(string id)
    {
        if (avatarPool == null || avatarPool.Count == 0) return null;
        int hash = string.IsNullOrEmpty(id) ? 0 : Mathf.Abs(id.GetHashCode());
        return avatarPool[hash % avatarPool.Count];
    }

    static string GetOnlineStatusText(FriendInfo info)
    {
        if (info == null) return "Offline";
        if (info.IsInRoom) return "In Game";
        if (info.IsOnline) return "Online";
        return "Offline";
    }

    static void ClearContainer(Transform container)
    {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            GameObject child = container.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: build sample rows so the layout can be previewed without runtime data.</summary>
    public void EditorPreviewPopulate()
    {
        ResolveThemeAssets();
        if (requestsListContainer != null)
        {
            ClearContainer(requestsListContainer);
            string[] names = { "Request_1", "Request_2", "Request_3", "Request_5", "Request_6" };
            _activeTab = PanelTab.Requests;
            foreach (var n in names) BuildRequestRow(n, n);
            if (requestsEmptyLabel != null) requestsEmptyLabel.SetActive(false);
        }
        if (friendsListContainer != null)
        {
            ClearContainer(friendsListContainer);
            string[] fnames = { "Aman", "Rohit", "Priya", "Kabir" };
            _activeTab = PanelTab.Friends;
            foreach (var n in fnames) BuildFriendRow(n, n, null, false);
            if (friendsEmptyLabel != null) friendsEmptyLabel.SetActive(false);
        }
    }

    public void EditorPreviewClear()
    {
        ClearContainer(requestsListContainer);
        ClearContainer(friendsListContainer);
    }
#endif
}
