using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// In-game "ADD FRIEND" panel. Opens from a top-right button during a match and lists the
/// current real table opponents (excludes the local player and bots). Each row has an avatar,
/// name and an ADD button that sends a friend request through PlayWithFriendsManager.
/// The panel is built programmatically with the same wooden theme used by ResultManager.
/// </summary>
public class InGameAddFriendController : MonoBehaviour
{
    public static InGameAddFriendController Instance;

    [Header("Trigger")]
    [Tooltip("The top-right button in Panel_Game that opens this panel.")]
    public Button openButton;

    [Header("UI References (Assign if already in hierarchy)")]
    public GameObject panelRoot;
    public CanvasGroup panelGroup;
    public Transform mainFrame;
    public Transform rowsContent;
    public Image dimOverlay;

    [Header("Theme (auto-loaded in editor if empty)")]
    public Sprite woodBoardSprite;
    public Sprite circleFrameSprite;
    public TMP_FontAsset customFont;

    // Theme colors (match ResultManager wooden style)
    static readonly Color WoodTint = Color.white;
    static readonly Color PanelFallbackBg = new Color(0.25f, 0.15f, 0.05f, 0.98f);
    static readonly Color BrownBox = new Color(0.35f, 0.22f, 0.12f, 1f);
    static readonly Color GreenBtn = new Color(0.30f, 0.62f, 0.22f, 1f);
    static readonly Color SentBtn = new Color(0.45f, 0.45f, 0.45f, 1f);

    Canvas _canvas;
    CanvasGroup _panelGroup;
    Transform _mainFrame;
    Transform _rowsContent;
    Image _dimOverlay;
    bool _built;
    readonly List<GameObject> _rows = new List<GameObject>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(this); return; }

        ResolveThemeAssets();

        if (panelRoot != null)
        {
            _panelGroup = panelGroup;
            _mainFrame = mainFrame;
            _rowsContent = rowsContent;
            _dimOverlay = dimOverlay;
            _built = true;
            panelRoot.SetActive(false);
        }

        if (openButton != null)
{
            openButton.onClick.RemoveListener(Open);
            openButton.onClick.AddListener(Open);
        }
    }

    void Start()
    {
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.RequestsChanged += OnRequestsChanged;
    }

    void OnDestroy()
    {
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.RequestsChanged -= OnRequestsChanged;
        if (Instance == this) Instance = null;
    }

    /// <summary>Live-refresh the list if a request arrives/leaves while the panel is open.</summary>
    void OnRequestsChanged()
    {
        if (_panelGroup != null && _panelGroup.gameObject.activeSelf)
            PopulateRows();
    }

    void ResolveThemeAssets()
    {
#if UNITY_EDITOR
        if (woodBoardSprite == null)
            woodBoardSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Images/BG_Buttons.png");
        if (circleFrameSprite == null)
            circleFrameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/2D Cards Game Art Pack/Sprites/Characters/frame_circle.png");
#endif
    }

    public void Toggle()
    {
        if (_panelGroup != null && _panelGroup.gameObject.activeSelf && _panelGroup.alpha > 0.5f)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (!_built) BuildPanel();
        if (_panelGroup == null) return;

        PopulateRows();

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.CheckFriendsOnlineStatus();

        if (AdsManager.Instance != null)
            AdsManager.Instance.ShowBanner();

        _panelGroup.gameObject.SetActive(true);
        _panelGroup.transform.SetAsLastSibling();
        _panelGroup.DOKill();
        _panelGroup.alpha = 0f;
        _panelGroup.interactable = true;
        _panelGroup.blocksRaycasts = true;
        _panelGroup.DOFade(1f, 0.3f).SetUpdate(true);

        if (_dimOverlay != null)
        {
            Color c = _dimOverlay.color; c.a = 0f; _dimOverlay.color = c;
            _dimOverlay.DOFade(0.6f, 0.3f).SetUpdate(true);
        }

        if (_mainFrame != null)
        {
            _mainFrame.DOKill();
            _mainFrame.localScale = Vector3.one * 0.92f;
            _mainFrame.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }
    }

    public void Close()
    {
        if (AdsManager.Instance != null)
            AdsManager.Instance.HideBanner();

        if (_panelGroup == null) return;
        _panelGroup.DOKill();
        _panelGroup.interactable = false;
        _panelGroup.blocksRaycasts = false;
        _panelGroup.DOFade(0f, 0.2f).SetUpdate(true).OnComplete(() =>
        {
            if (_panelGroup != null) _panelGroup.gameObject.SetActive(false);
        });
    }

    // ============================================================
    // PANEL CONSTRUCTION
    // ============================================================
    void BuildPanel()
    {
        if (_canvas == null)
        {
            _canvas = openButton != null ? openButton.GetComponentInParent<Canvas>() : null;
            if (_canvas == null) _canvas = Object.FindAnyObjectByType<Canvas>();
        }
        if (_canvas == null) { Debug.LogError("[AddFriend] No Canvas found."); return; }

        // Root (full-screen)
        GameObject root = NewRect("Panel_InGameAddFriend", _canvas.transform);
        Stretch(root.GetComponent<RectTransform>());
        _panelGroup = root.AddComponent<CanvasGroup>();

        // Dim overlay (click to close)
        GameObject overlay = NewRect("Overlay", root.transform);
        Stretch(overlay.GetComponent<RectTransform>());
        _dimOverlay = overlay.AddComponent<Image>();
        _dimOverlay.color = new Color(0, 0, 0, 0.6f);
        Button overlayBtn = overlay.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.AddListener(Close);

        // Main wooden frame
        GameObject frame = NewRect("MainFrame", root.transform);
        RectTransform frameRt = frame.GetComponent<RectTransform>();
        frameRt.anchorMin = frameRt.anchorMax = new Vector2(0.5f, 0.5f);
        frameRt.pivot = new Vector2(0.5f, 0.5f);
        frameRt.sizeDelta = new Vector2(820, 1000);
        frameRt.anchoredPosition = new Vector2(0, 20);
        _mainFrame = frame.transform;
        Image frameImg = frame.AddComponent<Image>();
        if (woodBoardSprite != null) { frameImg.sprite = woodBoardSprite; frameImg.type = Image.Type.Simple; frameImg.color = WoodTint; }
        else frameImg.color = PanelFallbackBg;
        Shadow sh = frame.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.45f);
        sh.effectDistance = new Vector2(6, -6);

        // Title
        GameObject title = NewRect("Title", frame.transform);
        RectTransform titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 1f); titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(600, 90);
        titleRt.anchoredPosition = new Vector2(0, -40);
        AddTmp(title.transform, "ADD FRIEND", Color.white, 52, TextAlignmentOptions.Center, FontStyles.Bold);

        // Title underline
        GameObject underline = NewRect("Underline", frame.transform);
        RectTransform ulRt = underline.GetComponent<RectTransform>();
        ulRt.anchorMin = new Vector2(0.5f, 1f); ulRt.anchorMax = new Vector2(0.5f, 1f);
        ulRt.pivot = new Vector2(0.5f, 1f);
        ulRt.sizeDelta = new Vector2(700, 4);
        ulRt.anchoredPosition = new Vector2(0, -135);
        underline.AddComponent<Image>().color = new Color(1, 1, 1, 0.25f);

        // Close (X) button — top right
        GameObject closeGo = NewRect("CloseButton", frame.transform);
        RectTransform closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 1f); closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.sizeDelta = new Vector2(72, 72);
        closeRt.anchoredPosition = new Vector2(-25, -25);
        Image closeImg = closeGo.AddComponent<Image>();
        closeImg.color = BrownBox;
        // Task 20: keep the close button ROUNDED — use the circular frame if available, otherwise
        // fall back to Unity's built-in rounded (9-sliced) UISprite so it stays rounded in builds too.
        if (circleFrameSprite != null)
        {
            closeImg.sprite = circleFrameSprite;
        }
        else
        {
            Sprite roundedFallback = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            if (roundedFallback != null) { closeImg.sprite = roundedFallback; closeImg.type = Image.Type.Sliced; }
        }
        Button closeBtn = closeGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(Close);
        var xTxt = AddTmp(closeGo.transform, "X", Color.white, 34, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(xTxt.rectTransform);

        // Scroll view (player list)
        BuildScrollView(frame.transform);

        // Task 19: bottom banner-ad placeholder stretched FULL SCREEN WIDTH (matches the leaderboard banner).
        // Parented to the full-screen root and anchored edge-to-edge along the bottom, exactly like
        // ResultManager.CreateBannerAd (anchors (0,0)-(1,0), flush to screen bottom, 110px tall).
        GameObject banner = NewRect("BannerAdPlaceholder", root.transform);
        RectTransform bnRt = banner.GetComponent<RectTransform>();
        bnRt.anchorMin = new Vector2(0f, 0f); bnRt.anchorMax = new Vector2(1f, 0f);
        bnRt.pivot = new Vector2(0.5f, 0f);
        bnRt.offsetMin = new Vector2(0f, 0f);
        bnRt.offsetMax = new Vector2(0f, 110f);
        banner.AddComponent<Image>().color = new Color(0, 0, 0, 0.35f);
        var bnLabel = AddTmp(banner.transform, "BANNER AD PLACEMENT (FULL WIDTH)", new Color(1, 1, 1, 0.6f), 24, TextAlignmentOptions.Center, FontStyles.Bold);
        var bnLblRt = bnLabel.rectTransform;
        bnLblRt.anchorMin = Vector2.zero; bnLblRt.anchorMax = Vector2.one;
        bnLblRt.offsetMin = Vector2.zero; bnLblRt.offsetMax = Vector2.zero;

        _built = true;
        root.SetActive(false);
    }

    void BuildScrollView(Transform parent)
    {
        GameObject scrollGo = NewRect("Scroll_Players", parent);
        RectTransform scRt = scrollGo.GetComponent<RectTransform>();
        scRt.anchorMin = new Vector2(0.5f, 0.5f); scRt.anchorMax = new Vector2(0.5f, 0.5f);
        scRt.pivot = new Vector2(0.5f, 0.5f);
        scRt.sizeDelta = new Vector2(740, 620);
        scRt.anchoredPosition = new Vector2(0, 30);
        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        // Viewport (masked)
        GameObject viewport = NewRect("Viewport", scrollGo.transform);
        Stretch(viewport.GetComponent<RectTransform>());
        Image vpImg = viewport.AddComponent<Image>();
        vpImg.color = new Color(1, 1, 1, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = viewport.GetComponent<RectTransform>();

        // Content
        GameObject content = NewRect("Content", viewport.transform);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f); contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0, 0);
        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 18;
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRt;

        _rowsContent = content.transform;
    }

    // ============================================================
    // ROW POPULATION
    // ============================================================
    void PopulateRows()
    {
        if (_rowsContent == null) return;

        for (int i = _rows.Count - 1; i >= 0; i--)
            if (_rows[i] != null) Destroy(_rows[i]);
        _rows.Clear();

        // 1) Incoming friend requests (Accept / Decline) shown at the top.
        int requestCount = 0;
        if (PlayWithFriendsManager.Instance != null)
        {
            // Copy to a list first — accepting/declining mutates the source dictionary.
            var requests = new List<KeyValuePair<string, string>>(PlayWithFriendsManager.Instance.IncomingRequests);
            if (requests.Count > 0)
            {
                _rows.Add(CreateSectionHeader("FRIEND REQUESTS"));
                foreach (var req in requests)
                {
                    _rows.Add(CreateRequestRow(req.Key, req.Value));
                    requestCount++;
                }
            }
        }

        // 2) Saved friends (online) — invite to the current private room when host.
        int friendCount = 0;
        if (PlayWithFriendsManager.Instance != null)
        {
            var mgr = PlayWithFriendsManager.Instance;
            bool canInvite = PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
                && DeckManager.Instance != null && DeckManager.IsPrivateFriendsRoom();

            foreach (string friendId in mgr.MyFriends)
            {
                if (string.IsNullOrEmpty(friendId)) continue;
                if (friendCount == 0)
                {
                    if (requestCount > 0) _rows.Add(CreateSectionHeader("YOUR FRIENDS"));
                    else _rows.Add(CreateSectionHeader("YOUR FRIENDS"));
                }

                string display = mgr.GetFriendDisplayName(friendId);
                bool online = mgr.IsFriendOnline(friendId);
                bool sent = mgr.IsGameInviteSent(friendId);
                _rows.Add(CreateFriendInviteRow(friendId, display, online, canInvite, sent));
                friendCount++;
            }
        }

        // 3) Current table opponents you can add.
        List<Player> opponents = GetTableOpponents();
        if (opponents.Count > 0)
        {
            if (requestCount > 0 || friendCount > 0) _rows.Add(CreateSectionHeader("PLAYERS AT TABLE"));
            foreach (Player p in opponents)
                _rows.Add(CreatePlayerRow(p));
        }

        if (requestCount == 0 && friendCount == 0 && opponents.Count == 0)
        {
            GameObject empty = NewRect("EmptyRow", _rowsContent);
            empty.AddComponent<LayoutElement>().preferredHeight = 120;
            AddTmp(empty.transform, "No players to add right now.", new Color(1, 1, 1, 0.7f), 26, TextAlignmentOptions.Center, FontStyles.Italic);
            _rows.Add(empty);
        }
    }

    GameObject CreateSectionHeader(string text)
    {
        GameObject header = NewRect("SectionHeader", _rowsContent);
        header.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 44);
        LayoutElement le = header.AddComponent<LayoutElement>();
        le.preferredHeight = 44;
        le.preferredWidth = 700;
        var txt = AddTmp(header.transform, text, new Color(1f, 0.86f, 0.45f, 1f), 26, TextAlignmentOptions.Left, FontStyles.Bold);
        var rt = txt.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(18, 0); rt.offsetMax = new Vector2(-18, 0);
        return header;
    }

    GameObject CreateRequestRow(string fromUserId, string fromName)
    {
        GameObject row = NewRect("RequestRow", _rowsContent);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(700, 96);
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 96;
        le.preferredWidth = 700;
        Image rowBg = row.AddComponent<Image>();
        rowBg.color = new Color(0.30f, 0.62f, 0.22f, 0.18f);

        // Name box
        GameObject nameBox = NewRect("NameBox", row.transform);
        RectTransform nbRt = nameBox.GetComponent<RectTransform>();
        nbRt.anchorMin = nbRt.anchorMax = new Vector2(0f, 0.5f);
        nbRt.pivot = new Vector2(0f, 0.5f);
        nbRt.sizeDelta = new Vector2(330, 76);
        nbRt.anchoredPosition = new Vector2(20, 0);
        string displayName = string.IsNullOrEmpty(fromName) ? "Player" : fromName;
        var nameTxt = AddTmp(nameBox.transform, displayName, Color.white, 30, TextAlignmentOptions.Left, FontStyles.Bold);
        var nameRt = nameTxt.rectTransform;
        nameRt.anchorMin = Vector2.zero; nameRt.anchorMax = Vector2.one;
        nameRt.offsetMin = new Vector2(10, 0); nameRt.offsetMax = new Vector2(-10, 0);
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;

        // Decline (X) button
        GameObject declineGo = NewRect("DeclineButton", row.transform);
        RectTransform dcRt = declineGo.GetComponent<RectTransform>();
        dcRt.anchorMin = dcRt.anchorMax = new Vector2(1f, 0.5f);
        dcRt.pivot = new Vector2(1f, 0.5f);
        dcRt.sizeDelta = new Vector2(78, 70);
        dcRt.anchoredPosition = new Vector2(-15, 0);
        Image dcImg = declineGo.AddComponent<Image>();
        dcImg.color = new Color(0.62f, 0.26f, 0.18f, 1f);
        if (woodBoardSprite != null) { dcImg.sprite = woodBoardSprite; dcImg.type = Image.Type.Sliced; }
        Button dcBtn = declineGo.AddComponent<Button>();
        dcBtn.targetGraphic = dcImg;
        var dcLabel = AddTmp(declineGo.transform, "\u2715", Color.white, 30, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(dcLabel.rectTransform);

        // Accept (✓) button
        GameObject acceptGo = NewRect("AcceptButton", row.transform);
        RectTransform acRt = acceptGo.GetComponent<RectTransform>();
        acRt.anchorMin = acRt.anchorMax = new Vector2(1f, 0.5f);
        acRt.pivot = new Vector2(1f, 0.5f);
        acRt.sizeDelta = new Vector2(150, 70);
        acRt.anchoredPosition = new Vector2(-103, 0);
        Image acImg = acceptGo.AddComponent<Image>();
        acImg.color = GreenBtn;
        if (woodBoardSprite != null) { acImg.sprite = woodBoardSprite; acImg.type = Image.Type.Sliced; }
        Button acBtn = acceptGo.AddComponent<Button>();
        acBtn.targetGraphic = acImg;
        var acLabel = AddTmp(acceptGo.transform, "ACCEPT", Color.white, 26, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(acLabel.rectTransform);

        string id = fromUserId;
        string nm = displayName;
        acBtn.onClick.AddListener(() =>
        {
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.AcceptFriendRequest(id, nm);
            PopulateRows();
        });
        dcBtn.onClick.AddListener(() =>
        {
            if (PlayWithFriendsManager.Instance != null)
                PlayWithFriendsManager.Instance.DeclineFriendRequest(id);
            PopulateRows();
        });

        return row;
    }

    GameObject CreateFriendInviteRow(string friendId, string displayName, bool online, bool canInvite, bool inviteSent)
    {
        GameObject row = NewRect("FriendRow", _rowsContent);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(700, 96);
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 96;
        le.preferredWidth = 700;
        row.AddComponent<Image>().color = new Color(0, 0, 0, 0.18f);

        GameObject nameBox = NewRect("NameBox", row.transform);
        RectTransform nbRt = nameBox.GetComponent<RectTransform>();
        nbRt.anchorMin = nbRt.anchorMax = new Vector2(0f, 0.5f);
        nbRt.pivot = new Vector2(0f, 0.5f);
        nbRt.sizeDelta = new Vector2(380, 76);
        nbRt.anchoredPosition = new Vector2(20, 0);
        string status = online ? "Online" : "Offline";
        var nameTxt = AddTmp(nameBox.transform, $"{displayName}\n<size=22><color=#C8E6C9>{status}</color></size>",
            Color.white, 28, TextAlignmentOptions.Left, FontStyles.Bold);
        var nameRt = nameTxt.rectTransform;
        nameRt.anchorMin = Vector2.zero; nameRt.anchorMax = Vector2.one;
        nameRt.offsetMin = new Vector2(10, 0); nameRt.offsetMax = new Vector2(-10, 0);

        GameObject actionGo = NewRect("InviteButton", row.transform);
        RectTransform acRt = actionGo.GetComponent<RectTransform>();
        acRt.anchorMin = acRt.anchorMax = new Vector2(1f, 0.5f);
        acRt.pivot = new Vector2(1f, 0.5f);
        acRt.sizeDelta = new Vector2(180, 70);
        acRt.anchoredPosition = new Vector2(-15, 0);
        Image acImg = actionGo.AddComponent<Image>();
        bool enabled = canInvite && online && !inviteSent;
        acImg.color = inviteSent ? SentBtn : (enabled ? GreenBtn : SentBtn);
        if (woodBoardSprite != null) { acImg.sprite = woodBoardSprite; acImg.type = Image.Type.Sliced; }
        Button acBtn = actionGo.AddComponent<Button>();
        acBtn.targetGraphic = acImg;
        acBtn.interactable = enabled;
        string label = inviteSent ? "SENT" : (canInvite ? "INVITE" : "FRIEND");
        var acLabel = AddTmp(actionGo.transform, label, Color.white, 26, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(acLabel.rectTransform);

        if (enabled)
        {
            string id = friendId;
            string nm = displayName;
            acBtn.onClick.AddListener(() =>
            {
                if (PlayWithFriendsManager.Instance == null) return;
                PlayWithFriendsManager.Instance.InviteFriendToGame(id, nm);
                PlayWithFriendsManager.Instance.MarkGameInviteSent(id);
                PopulateRows();
            });
        }

        return row;
    }

    List<Player> GetTableOpponents()
    {
        var list = new List<Player>();
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return list;

        foreach (Player p in PhotonRoomPlayers.GetSorted())
        {
            if (p == null) continue;
            if (p.IsLocal) continue;                       // not myself
            if (string.IsNullOrEmpty(p.UserId)) continue;  // bots/phantoms have no real UserId
            list.Add(p);
        }
        return list;
    }

    GameObject CreatePlayerRow(Player player)
    {
        GameObject row = NewRect("PlayerRow", _rowsContent);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(700, 96);
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 96;
        le.preferredWidth = 700;
        Image rowBg = row.AddComponent<Image>();
        rowBg.color = new Color(0, 0, 0, 0.22f);

        // Avatar circle frame
        GameObject avatarFrame = NewRect("AvatarFrame", row.transform);
        RectTransform afRt = avatarFrame.GetComponent<RectTransform>();
        afRt.anchorMin = afRt.anchorMax = new Vector2(0f, 0.5f);
        afRt.pivot = new Vector2(0f, 0.5f);
        afRt.sizeDelta = new Vector2(76, 76);
        afRt.anchoredPosition = new Vector2(15, 0);
        Image afImg = avatarFrame.AddComponent<Image>();
        afImg.color = BrownBox;
        if (circleFrameSprite != null) afImg.sprite = circleFrameSprite;

        GameObject avatar = NewRect("Avatar", avatarFrame.transform);
        RectTransform avRt = avatar.GetComponent<RectTransform>();
        avRt.anchorMin = avRt.anchorMax = new Vector2(0.5f, 0.5f);
        avRt.pivot = new Vector2(0.5f, 0.5f);
        avRt.sizeDelta = new Vector2(68, 68);
        Image avImg = avatar.AddComponent<Image>();
        avImg.color = Color.white;
        avImg.preserveAspect = true;
        avImg.sprite = GetAvatarSprite(player.ActorNumber);

        // Name box (brown rounded)
        GameObject nameBox = NewRect("NameBox", row.transform);
        RectTransform nbRt = nameBox.GetComponent<RectTransform>();
        nbRt.anchorMin = nbRt.anchorMax = new Vector2(0f, 0.5f);
        nbRt.pivot = new Vector2(0f, 0.5f);
        nbRt.sizeDelta = new Vector2(330, 64);
        nbRt.anchoredPosition = new Vector2(110, 0);
        Image nbImg = nameBox.AddComponent<Image>();
        nbImg.color = BrownBox;
        if (woodBoardSprite != null) { nbImg.sprite = woodBoardSprite; nbImg.type = Image.Type.Sliced; }
string displayName = string.IsNullOrEmpty(player.NickName) ? ("Player " + player.ActorNumber) : player.NickName;
        var nameTxt = AddTmp(nameBox.transform, displayName, Color.white, 30, TextAlignmentOptions.Center, FontStyles.Bold);
        var nameRt = nameTxt.rectTransform;
        nameRt.anchorMin = Vector2.zero; nameRt.anchorMax = Vector2.one;
        nameRt.offsetMin = new Vector2(15, 0); nameRt.offsetMax = new Vector2(-15, 0);
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;

        // ADD button (green)
        GameObject addGo = NewRect("AddButton", row.transform);
        RectTransform addRt = addGo.GetComponent<RectTransform>();
        addRt.anchorMin = addRt.anchorMax = new Vector2(1f, 0.5f);
        addRt.pivot = new Vector2(1f, 0.5f);
        addRt.sizeDelta = new Vector2(180, 70);
        addRt.anchoredPosition = new Vector2(-15, 0);
        Image addImg = addGo.AddComponent<Image>();
        addImg.color = GreenBtn;
        if (woodBoardSprite != null) { addImg.sprite = woodBoardSprite; addImg.type = Image.Type.Sliced; }
        Button addBtn = addGo.AddComponent<Button>();
addBtn.targetGraphic = addImg;
        var addLabel = AddTmp(addGo.transform, "ADD", Color.white, 28, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(addLabel.rectTransform);

        bool alreadyFriend = PlayWithFriendsManager.Instance != null
            && IsAlreadyFriend(player.UserId);

        if (alreadyFriend)
        {
            addLabel.text = "FRIEND";
            addImg.color = SentBtn;
            addBtn.interactable = false;
        }
        else
        {
            string targetId = player.UserId;
            string targetName = displayName;
            addBtn.onClick.AddListener(() =>
            {
                if (PlayWithFriendsManager.Instance == null)
                {
                    Debug.LogWarning("[AddFriend] PlayWithFriendsManager missing.");
                    return;
                }
                PlayWithFriendsManager.Instance.SendFriendRequest(targetId, targetName);
                addLabel.text = "SENT";
                addImg.color = SentBtn;
                addBtn.interactable = false;
            });
        }

        return row;
    }

    static bool IsAlreadyFriend(string userId)
    {
        if (string.IsNullOrEmpty(userId) || PlayWithFriendsManager.Instance == null) return false;
        IReadOnlyList<string> friends = PlayWithFriendsManager.Instance.MyFriends;
        for (int i = 0; i < friends.Count; i++)
            if (friends[i] == userId) return true;
        return false;
    }

    Sprite GetAvatarSprite(int actorNumber)
    {
        List<Sprite> pool = MatchmakingManager.GlobalProfileSprites;
        if (pool == null || pool.Count == 0) return null;
        int idx = Mathf.Abs(actorNumber) % pool.Count;
        return pool[idx];
    }

    // ============================================================
    // HELPERS
    // ============================================================
    static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    TextMeshProUGUI AddTmp(Transform parent, string text, Color color, int size, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 50);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.fontStyle = style;
        tmp.raycastTarget = false;
        if (customFont != null) tmp.font = customFont;
        return tmp;
    }
}
