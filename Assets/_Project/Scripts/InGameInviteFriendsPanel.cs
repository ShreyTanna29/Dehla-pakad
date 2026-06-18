using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Photon.Realtime;

/// <summary>
/// In-game "INVITE FRIEND" panel opened by the host via the REPLACE action on a bot seat.
/// Lists the player's saved friends with an INVITE button each. Inviting sends a Firebase
/// invite carrying the current room PIN (through PlayWithFriendsManager.InviteFriendToGame);
/// when the friend joins, DeckManager hands them the bot's seat. Built programmatically with
/// the same wooden theme used by InGameAddFriendController.
/// </summary>
[DefaultExecutionOrder(-80)]
public class InGameInviteFriendsPanel : MonoBehaviour
{
    public static InGameInviteFriendsPanel Instance;

    [Header("Theme (auto-loaded in editor if empty)")]
    public Sprite woodBoardSprite;
    public Sprite circleFrameSprite;
    public TMP_FontAsset customFont;

    [Header("Pre-built hierarchy (assign in scene to skip runtime build)")]
    [Tooltip("Root of the in-scene INVITE FRIEND panel (a duplicate of Panel_InGameAddFriend).")]
    public GameObject panelRoot;
    public CanvasGroup panelGroup;
    public Transform mainFrame;
    [Tooltip("Scroll Content where the friend rows are listed.")]
    public Transform rowsContent;
    public Image dimOverlay;
    public Button closeButton;
    public Button overlayButton;

    static readonly Color WoodTint = Color.white;
    static readonly Color PanelFallbackBg = new Color(0.25f, 0.15f, 0.05f, 0.98f);
    static readonly Color BrownBox = new Color(0.35f, 0.22f, 0.12f, 1f);
    static readonly Color GreenBtn = new Color(0.30f, 0.62f, 0.22f, 1f);
    static readonly Color SentBtn = new Color(0.45f, 0.45f, 0.45f, 1f);

    Canvas _canvas;
    CanvasGroup _group;
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
        TryUsePrebuiltHierarchy();
    }

    /// <summary>
    /// If a panel hierarchy is assigned in the scene (Panel_InGameInviteFriend), use it
    /// instead of building the panel at runtime. Mirrors InGameAddFriendController so the
    /// invite panel is fully editable in the editor with the same design.
    /// </summary>
    void TryUsePrebuiltHierarchy()
    {
        if (panelRoot == null) return;

        _group = panelGroup != null ? panelGroup : panelRoot.GetComponent<CanvasGroup>();
        if (_group == null) _group = panelRoot.AddComponent<CanvasGroup>();
        _mainFrame = mainFrame;
        _rowsContent = rowsContent;
        _dimOverlay = dimOverlay;
        _canvas = panelRoot.GetComponentInParent<Canvas>();

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }
        if (overlayButton != null)
        {
            overlayButton.onClick.RemoveListener(Close);
            overlayButton.onClick.AddListener(Close);
        }

        _built = true;
        panelRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

    public void Open()
    {
        if (!_built) BuildPanel();
        if (_group == null) return;

        PopulateRows();

        _group.gameObject.SetActive(true);
        _group.transform.SetAsLastSibling();
        _group.DOKill();
        _group.alpha = 0f;
        _group.interactable = true;
        _group.blocksRaycasts = true;
        _group.DOFade(1f, 0.3f).SetUpdate(true);

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
        if (_group == null) return;
        _group.DOKill();
        _group.interactable = false;
        _group.blocksRaycasts = false;
        _group.DOFade(0f, 0.2f).SetUpdate(true).OnComplete(() =>
        {
            if (_group != null) _group.gameObject.SetActive(false);
        });
    }

    // ============================================================
    // PANEL CONSTRUCTION
    // ============================================================
    void BuildPanel()
    {
        if (_canvas == null) _canvas = Object.FindAnyObjectByType<Canvas>();
        if (_canvas == null) { Debug.LogError("[InviteFriend] No Canvas found."); return; }

        GameObject root = NewRect("Panel_InGameInviteFriend", _canvas.transform);
        Stretch(root.GetComponent<RectTransform>());
        _group = root.AddComponent<CanvasGroup>();

        GameObject overlay = NewRect("Overlay", root.transform);
        Stretch(overlay.GetComponent<RectTransform>());
        _dimOverlay = overlay.AddComponent<Image>();
        _dimOverlay.color = new Color(0, 0, 0, 0.6f);
        Button overlayBtn = overlay.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.AddListener(Close);

        GameObject frame = NewRect("MainFrame", root.transform);
        RectTransform frameRt = frame.GetComponent<RectTransform>();
        frameRt.anchorMin = frameRt.anchorMax = new Vector2(0.5f, 0.5f);
        frameRt.pivot = new Vector2(0.5f, 0.5f);
        frameRt.sizeDelta = new Vector2(820, 1000);
        frameRt.anchoredPosition = new Vector2(0, 20);
        _mainFrame = frame.transform;
        Image frameImg = frame.AddComponent<Image>();
        if (woodBoardSprite != null) { frameImg.sprite = woodBoardSprite; frameImg.type = Image.Type.Sliced; frameImg.color = WoodTint; }
        else frameImg.color = PanelFallbackBg;
        Shadow sh = frame.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.45f);
        sh.effectDistance = new Vector2(6, -6);

        GameObject title = NewRect("Title", frame.transform);
        RectTransform titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 1f); titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(600, 90);
        titleRt.anchoredPosition = new Vector2(0, -40);
        AddTmp(title.transform, "INVITE FRIEND", Color.white, 52, TextAlignmentOptions.Center, FontStyles.Bold);

        GameObject underline = NewRect("Underline", frame.transform);
        RectTransform ulRt = underline.GetComponent<RectTransform>();
        ulRt.anchorMin = new Vector2(0.5f, 1f); ulRt.anchorMax = new Vector2(0.5f, 1f);
        ulRt.pivot = new Vector2(0.5f, 1f);
        ulRt.sizeDelta = new Vector2(700, 4);
        ulRt.anchoredPosition = new Vector2(0, -135);
        underline.AddComponent<Image>().color = new Color(1, 1, 1, 0.25f);

        GameObject closeGo = NewRect("CloseButton", frame.transform);
        RectTransform closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 1f); closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.sizeDelta = new Vector2(72, 72);
        closeRt.anchoredPosition = new Vector2(-25, -25);
        Image closeImg = closeGo.AddComponent<Image>();
        closeImg.color = BrownBox;
        if (circleFrameSprite != null) closeImg.sprite = circleFrameSprite;
        Button closeBtn = closeGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(Close);
        var xTxt = AddTmp(closeGo.transform, "X", Color.white, 34, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(xTxt.rectTransform);

        BuildScrollView(frame.transform);

        _built = true;
        root.SetActive(false);
    }

    void BuildScrollView(Transform parent)
    {
        GameObject scrollGo = NewRect("Scroll_Friends", parent);
        RectTransform scRt = scrollGo.GetComponent<RectTransform>();
        scRt.anchorMin = new Vector2(0.5f, 0.5f); scRt.anchorMax = new Vector2(0.5f, 0.5f);
        scRt.pivot = new Vector2(0.5f, 0.5f);
        scRt.sizeDelta = new Vector2(740, 720);
        scRt.anchoredPosition = new Vector2(0, -20);
        ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        GameObject viewport = NewRect("Viewport", scrollGo.transform);
        Stretch(viewport.GetComponent<RectTransform>());
        Image vpImg = viewport.AddComponent<Image>();
        vpImg.color = new Color(1, 1, 1, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = viewport.GetComponent<RectTransform>();

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

        PlayWithFriendsManager mgr = PlayWithFriendsManager.Instance;
        IReadOnlyList<string> friends = mgr != null ? mgr.MyFriends : null;

        if (mgr == null || friends == null || friends.Count == 0)
        {
            GameObject empty = NewRect("EmptyRow", _rowsContent);
            empty.AddComponent<LayoutElement>().preferredHeight = 120;
            AddTmp(empty.transform, "No friends yet.\nAdd friends from the home menu.",
                new Color(1, 1, 1, 0.7f), 26, TextAlignmentOptions.Center, FontStyles.Italic);
            _rows.Add(empty);
            return;
        }

        foreach (string friendId in friends)
        {
            if (string.IsNullOrEmpty(friendId)) continue;
            _rows.Add(CreateFriendRow(mgr, friendId));
        }
    }

    GameObject CreateFriendRow(PlayWithFriendsManager mgr, string friendId)
    {
        string displayName = mgr.GetFriendDisplayName(friendId);
        FriendInfo info = mgr.GetFriendPhotonInfo(friendId);

        GameObject row = NewRect("FriendRow", _rowsContent);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(700, 96);
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 96;
        le.preferredWidth = 700;
        Image rowBg = row.AddComponent<Image>();
        rowBg.color = new Color(0, 0, 0, 0.22f);

        // Name + status box.
        GameObject nameBox = NewRect("NameBox", row.transform);
        RectTransform nbRt = nameBox.GetComponent<RectTransform>();
        nbRt.anchorMin = nbRt.anchorMax = new Vector2(0f, 0.5f);
        nbRt.pivot = new Vector2(0f, 0.5f);
        nbRt.sizeDelta = new Vector2(450, 76);
        nbRt.anchoredPosition = new Vector2(20, 0);

        string status = "\u26AB Offline";
        if (info != null) status = info.IsOnline ? (info.IsInRoom ? "\uD83C\uDFAE In Game" : "\uD83D\uDFE2 Online") : "\u26AB Offline";
        var nameTxt = AddTmp(nameBox.transform, $"{displayName}\n<size=20>{status}</size>", Color.white, 30, TextAlignmentOptions.Left, FontStyles.Bold);
        var nameRt = nameTxt.rectTransform;
        nameRt.anchorMin = Vector2.zero; nameRt.anchorMax = Vector2.one;
        nameRt.offsetMin = new Vector2(10, 0); nameRt.offsetMax = new Vector2(-10, 0);
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;

        // INVITE button.
        GameObject inviteGo = NewRect("InviteButton", row.transform);
        RectTransform inRt = inviteGo.GetComponent<RectTransform>();
        inRt.anchorMin = inRt.anchorMax = new Vector2(1f, 0.5f);
        inRt.pivot = new Vector2(1f, 0.5f);
        inRt.sizeDelta = new Vector2(190, 70);
        inRt.anchoredPosition = new Vector2(-15, 0);
        Image inImg = inviteGo.AddComponent<Image>();
        inImg.color = GreenBtn;
        if (woodBoardSprite != null) { inImg.sprite = woodBoardSprite; inImg.type = Image.Type.Sliced; }
        Button inviteBtn = inviteGo.AddComponent<Button>();
        inviteBtn.targetGraphic = inImg;
        var inviteLabel = AddTmp(inviteGo.transform, "INVITE", Color.white, 28, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(inviteLabel.rectTransform);

        bool alreadySent = mgr.IsGameInviteSent(friendId);
        if (alreadySent)
        {
            inviteLabel.text = "SENT";
            inImg.color = SentBtn;
            inviteBtn.interactable = false;
        }
        else
        {
            string targetId = friendId;
            string targetName = displayName;
            inviteBtn.onClick.AddListener(() =>
            {
                if (PlayWithFriendsManager.Instance == null) return;
                PlayWithFriendsManager.Instance.InviteFriendToGame(targetId, targetName);
                inviteLabel.text = "SENT";
                inImg.color = SentBtn;
                inviteBtn.interactable = false;
            });
        }

        return row;
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
