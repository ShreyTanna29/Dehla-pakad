using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// In-game player profile popup. Makes the 3 opponent seat avatars (Left/Top/Right) clickable
/// during a match. Tapping an opponent's avatar opens a small wooden popup near that seat
/// showing the player's stats (SKILL / WIN RATIO / TOTAL KOT) and an ADD FRIEND / REMOVE toggle
/// that routes through PlayWithFriendsManager. The local player's own avatar ("You") is never
/// made clickable. The popup is built programmatically with the same wooden theme used by the
/// existing in-game ADD FRIEND list.
/// </summary>
[DefaultExecutionOrder(-90)]
public class PlayerStatsPopupController : MonoBehaviour
{
    public static PlayerStatsPopupController Instance;

    // Photon custom-property keys for per-player stats. They do not exist yet (no stats backend),
    // so the popup falls back to a placeholder dash. Wiring a stats system later only needs to
    // set these properties on each player.
    public const string PROP_SKILL = "st_sk";
    public const string PROP_WIN_RATIO = "st_wr";
    public const string PROP_TOTAL_KOT = "st_kot";

    [Header("Theme (auto-loaded in editor if empty)")]
    public Sprite woodBoardSprite;
    public Sprite circleFrameSprite;
    public TMP_FontAsset customFont;

    [Header("Pre-built hierarchy (assign in scene to skip runtime build)")]
    [Tooltip("Root of the in-scene stats popup (full-screen click-catcher).")]
    public GameObject panelRoot;
    public RectTransform cardRoot;
    public CanvasGroup panelGroup;
    public Button catcherButton;
    public TMP_Text nameLabel;
    public TMP_Text valSkill;
    public TMP_Text valWinRatio;
    public TMP_Text valKot;
    public Button friendButton;
    public TMP_Text friendButtonLabel;
    public Image friendButtonImage;

    [Header("Top-right (+) Add-Friend icon")]
    [Tooltip("The top-right + icon button. Clicking sends an in-game friend request to the shown player.")]
    public Button addFriendIconButton;
    public Image addFriendIconImage;
    public TMP_Text addFriendIconGlyph;

    static readonly Color WoodTint = Color.white;
    static readonly Color PanelFallbackBg = new Color(0.25f, 0.15f, 0.05f, 0.98f);
    static readonly Color BrownBox = new Color(0.35f, 0.22f, 0.12f, 1f);
    static readonly Color GreenBtn = new Color(0.30f, 0.62f, 0.22f, 1f);
    static readonly Color RemoveBtn = new Color(0.62f, 0.26f, 0.18f, 1f);
    static readonly Color DisabledBtn = new Color(0.45f, 0.45f, 0.45f, 1f);
    static readonly Color StatValueColor = new Color(1f, 0.86f, 0.45f, 1f);

    const float CardW = 470f;
    const float CardH = 370f;

    Canvas _canvas;
    RectTransform _root;          // fullscreen click-catcher root
    CanvasGroup _group;
    RectTransform _card;          // the wooden popup card
    TMP_Text _valSkill, _valWinRatio, _valKot;
    TMP_Text _nameLabel;
    TMP_Text _addLabel;
    UnityEngine.UI.Image _addImg;
    Button _addBtn;
    bool _built;

    // seatIndex -> avatar rect, wired once.
    readonly Dictionary<int, RectTransform> _seatAvatars = new Dictionary<int, RectTransform>();
    int _currentSeat = -1;
    string _currentUserId;
    string _currentName;
    Player _currentPlayer;
    bool _currentIsBot;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(this); return; }
        ResolveThemeAssets();
        TryUsePrebuiltHierarchy();
    }

    /// <summary>
    /// If a panel hierarchy is assigned in the scene (Panel_PlayerStatsPopup), use it instead
    /// of building the popup at runtime. Lets the popup be edited in the editor while keeping
    /// the dynamic content (name, stats, friend button) driven by code.
    /// </summary>
    void TryUsePrebuiltHierarchy()
    {
        if (panelRoot == null) return;

        _root = panelRoot.GetComponent<RectTransform>();
        _group = panelGroup != null ? panelGroup : panelRoot.GetComponent<CanvasGroup>();
        if (_group == null) _group = panelRoot.AddComponent<CanvasGroup>();
        _card = cardRoot;
        _nameLabel = nameLabel;
        _valSkill = valSkill;
        _valWinRatio = valWinRatio;
        _valKot = valKot;
        _addBtn = friendButton;
        _addLabel = friendButtonLabel;
        _addImg = friendButtonImage;
        _canvas = panelRoot.GetComponentInParent<Canvas>();

        if (catcherButton != null)
        {
            catcherButton.onClick.RemoveListener(Close);
            catcherButton.onClick.AddListener(Close);
        }

        _built = true;
        panelRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        WireSeatAvatars();
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

    // ============================================================
    // SEAT WIRING
    // ============================================================

    /// <summary>Finds the 3 opponent avatars (even while inactive) and makes them clickable.</summary>
    public void WireSeatAvatars()
    {
        WireSeat(1, "Opponent_Left", "Playe2_Avatar");
        WireSeat(2, "Opponent_Top", "Player3_Avatar");
        WireSeat(3, "Opponent_Right", "Playe4_Avatar");
    }

    void WireSeat(int seatIndex, string seatRootName, string avatarName)
    {
        Transform seatRoot = FindInScene(seatRootName);
        if (seatRoot == null) return;

        Transform avatar = seatRoot.Find(avatarName);
        if (avatar == null)
        {
            // Fallback: first child Image.
            var img = seatRoot.GetComponentInChildren<UnityEngine.UI.Image>(true);
            if (img != null) avatar = img.transform;
        }
        if (avatar == null) return;

        var avatarImg = avatar.GetComponent<UnityEngine.UI.Image>();
        if (avatarImg != null) avatarImg.raycastTarget = true;

        Button btn = avatar.GetComponent<Button>();
        if (btn == null) btn = avatar.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        if (avatarImg != null) btn.targetGraphic = avatarImg;

        int captured = seatIndex;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OpenForSeat(captured));

        _seatAvatars[seatIndex] = avatar as RectTransform;

        if (_canvas == null) _canvas = avatar.GetComponentInParent<Canvas>();
    }

    // ============================================================
    // OPEN / CLOSE
    // ============================================================

    public void OpenForSeat(int seatIndex)
    {
        if (!_built) BuildPanel();
        if (_group == null) return;

        _currentSeat = seatIndex;

        Player p = GetPlayerAtSeat(seatIndex);
        bool isBot = p == null || string.IsNullOrEmpty(p.UserId);

        _currentPlayer = p;
        _currentIsBot = isBot;
        _currentUserId = isBot ? null : p.UserId;
        _currentName = ResolveSeatName(seatIndex, p, isBot);

        if (_nameLabel != null) _nameLabel.text = _currentName;

        // Stats: read synced custom properties if a stats backend ever sets them, else placeholder.
        if (_valSkill != null) _valSkill.text = ReadStat(p, PROP_SKILL);
        if (_valWinRatio != null) _valWinRatio.text = ReadStat(p, PROP_WIN_RATIO);
        if (_valKot != null) _valKot.text = ReadStat(p, PROP_TOTAL_KOT);

        ConfigureFriendButton(isBot);
        ConfigureAddFriendIcon(isBot);

        _group.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        PositionCardNearSeat(seatIndex);

        _group.DOKill();
        _group.alpha = 0f;
        _group.interactable = true;
        _group.blocksRaycasts = true;
        _group.DOFade(1f, 0.18f).SetUpdate(true);

        if (_card != null)
        {
            _card.DOKill();
            _card.localScale = Vector3.one * 0.9f;
            _card.DOScale(1f, 0.28f).SetEase(Ease.OutBack).SetUpdate(true);
        }
    }

    public void Close()
    {
        if (_group == null) return;
        _group.DOKill();
        _group.interactable = false;
        _group.blocksRaycasts = false;
        _group.DOFade(0f, 0.15f).SetUpdate(true).OnComplete(() =>
        {
            if (_group != null) _group.gameObject.SetActive(false);
        });
    }

    void ConfigureFriendButton(bool isBot)
    {
        if (_addBtn == null || _addLabel == null || _addImg == null) return;

        _addBtn.onClick.RemoveAllListeners();
        _addBtn.interactable = true;

        bool isHost = PhotonNetwork.IsMasterClient;

        bool friendsPrivate = DeckManager.IsPrivateFriendsRoom();

        // Host seat management (REPLACE / REMOVE) — friends matches only.
        if (isHost && friendsPrivate)
        {
            if (isBot)
            {
                _addLabel.text = "REPLACE";
                _addImg.color = GreenBtn;
                _addBtn.onClick.AddListener(OnReplaceClicked);
            }
            else
            {
                _addLabel.text = "REMOVE";
                _addImg.color = RemoveBtn;
                _addBtn.onClick.AddListener(OnRemovePlayerClicked);
            }
            return;
        }

        if (isHost)
        {
            _addLabel.text = isBot ? "BOT" : "PLAYER";
            _addImg.color = DisabledBtn;
            _addBtn.interactable = false;
            return;
        }

        // ---- NON-HOST: social add-friend ----
        if (isBot || string.IsNullOrEmpty(_currentUserId))
        {
            _addLabel.text = "BOT";
            _addImg.color = DisabledBtn;
            _addBtn.interactable = false;
            return;
        }

        bool isFriend = PlayWithFriendsManager.Instance != null
            && PlayWithFriendsManager.Instance.IsFriend(_currentUserId);

        if (isFriend)
        {
            _addLabel.text = "UNFRIEND";
            _addImg.color = RemoveBtn;
            _addBtn.onClick.AddListener(OnUnfriendClicked);
        }
        else
        {
            _addLabel.text = "ADD FRIEND";
            _addImg.color = GreenBtn;
            _addBtn.onClick.AddListener(OnAddClicked);
        }
    }

    // ---- Host: REPLACE a bot by inviting a friend ----
    void OnReplaceClicked()
    {
        if (!PhotonNetwork.IsMasterClient) { Close(); return; }

        // Reopen the (closed) private room so an invited friend can join the bot seat.
        if (DeckManager.Instance != null)
            DeckManager.Instance.ReopenRoomForReplace();

        // Use the dedicated, game-only invite panel. It has its own top close (X) button and
        // tap-outside-to-close, and it is never parented under the home screen, so the close
        // button can never leak into the home menu.
        if (InGameInviteFriendsPanel.Instance != null)
            InGameInviteFriendsPanel.Instance.Open();
        else if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.RefreshFriendsListUI();
        else
            Debug.LogWarning("[StatsPopup] InGameInviteFriendsPanel missing — cannot open invite list.");

        Close();
    }

    // ---- Host: REMOVE a real player (bot takes over) ----
    void OnRemovePlayerClicked()
    {
        if (!PhotonNetwork.IsMasterClient) { Close(); return; }

        if (_currentPlayer != null && !_currentPlayer.IsLocal)
        {
            // Tasks 8 & 24: reflect the seat->bot swap on the host's UI IMMEDIATELY (local-first),
            // then fire the network kick, then refresh the replaced player's online/in-game status.
            if (DeckManager.Instance != null)
                DeckManager.Instance.HostReplacePlayerWithBot(_currentPlayer);
        }

        Close();
    }

    // ============================================================
    // TOP-RIGHT (+) ADD-FRIEND ICON
    // ============================================================

    /// <summary>
    /// Configures the top-right "+" icon. It sends an in-game friend request to the shown
    /// player. Hidden for bots (you cannot friend a bot). If the player is already a friend
    /// (or a request was already sent) the icon shows a tick and is disabled.
    /// </summary>
    void ConfigureAddFriendIcon(bool isBot)
    {
        if (addFriendIconButton == null) return;

        addFriendIconButton.onClick.RemoveAllListeners();

        // Can't friend a bot or a player with no real account id.
        if (isBot || string.IsNullOrEmpty(_currentUserId))
        {
            addFriendIconButton.gameObject.SetActive(false);
            return;
        }

        addFriendIconButton.gameObject.SetActive(true);

        bool isFriend = PlayWithFriendsManager.Instance != null
            && PlayWithFriendsManager.Instance.IsFriend(_currentUserId);

        if (isFriend)
        {
            SetAddIconState("\u2713", true);   // already friends -> tick, disabled
            addFriendIconButton.interactable = false;
        }
        else
        {
            SetAddIconState("+", false);
            addFriendIconButton.interactable = true;
            addFriendIconButton.onClick.AddListener(OnAddFriendIconClicked);
        }
    }

    void OnAddFriendIconClicked()
    {
        if (PlayWithFriendsManager.Instance == null || string.IsNullOrEmpty(_currentUserId)) return;

        PlayWithFriendsManager.Instance.SendFriendRequest(_currentUserId, _currentName);

        // Reflect the sent state on the icon.
        SetAddIconState("\u2713", true);
        addFriendIconButton.interactable = false;
        addFriendIconButton.onClick.RemoveAllListeners();
    }

    void SetAddIconState(string glyph, bool dim)
    {
        if (addFriendIconGlyph != null) addFriendIconGlyph.text = glyph;
        if (addFriendIconImage != null)
            addFriendIconImage.color = dim ? DisabledBtn : BrownBox;
    }

    // ---- Non-host: social add friend ----
    void OnAddClicked()
    {
        if (PlayWithFriendsManager.Instance == null || string.IsNullOrEmpty(_currentUserId)) return;
        PlayWithFriendsManager.Instance.SendFriendRequest(_currentUserId, _currentName);
        _addLabel.text = "SENT";
        _addImg.color = DisabledBtn;
        _addBtn.interactable = false;
    }

    void OnUnfriendClicked()
    {
        if (PlayWithFriendsManager.Instance == null || string.IsNullOrEmpty(_currentUserId)) return;
        PlayWithFriendsManager.Instance.RemoveFriend(_currentUserId);
        // Refresh button back to ADD state.
        ConfigureFriendButton(_currentIsBot);
    }

    // ============================================================
    // SEAT / PLAYER RESOLUTION
    // ============================================================

    Player GetPlayerAtSeat(int seatIndex)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return null;
        foreach (Player p in PhotonRoomPlayers.GetSorted())
        {
            if (p == null || p.IsLocal) continue;
            if (SeatIndexOf(p.ActorNumber) == seatIndex) return p;
        }
        return null;
    }

    static int SeatIndexOf(int actorNumber)
    {
        if (PlayerHand.LocalInstance != null)
            return PlayerHand.LocalInstance.GetSeatIndex(actorNumber);

        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.LocalPlayer == null) return 0;
        int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
        return (actorNumber - localActor + 4) % 4;
    }

    string ResolveSeatName(int seatIndex, Player p, bool isBot)
    {
        if (p != null && !string.IsNullOrEmpty(p.NickName)) return p.NickName;
        if (isBot)
        {
            // Mirror PlayerProfileSync bot naming when possible.
            if (DeckManager.Instance != null && DeckManager.botActorNumbers != null)
            {
                for (int i = 0; i < DeckManager.botActorNumbers.Count; i++)
                {
                    if (SeatIndexOf(DeckManager.botActorNumbers[i]) == seatIndex)
                        return "Dehla_AI_" + (i + 1);
                }
            }
            return "AI Bot";
        }
        return "Player";
    }

    static string ReadStat(Player p, string key)
    {
        if (p != null && p.CustomProperties != null
            && p.CustomProperties.TryGetValue(key, out object val) && val != null)
            return val.ToString();
        return "—";
    }

    // ============================================================
    // POSITIONING
    // ============================================================

    void PositionCardNearSeat(int seatIndex)
    {
        if (_card == null || _root == null) return;

        Vector2 anchored = Vector2.zero;

        if (_seatAvatars.TryGetValue(seatIndex, out RectTransform avatar) && avatar != null)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, avatar.position);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _root, screenPoint, null, out Vector2 local))
            {
                Vector2 offset = seatIndex switch
                {
                    1 => new Vector2(CardW * 0.5f + 110f, -20f),   // left seat -> popup to the right
                    3 => new Vector2(-(CardW * 0.5f + 110f), -20f), // right seat -> popup to the left
                    2 => new Vector2(0f, -(CardH * 0.5f + 120f)),   // top seat -> popup below
                    _ => Vector2.zero
                };
                anchored = local + offset;
            }
        }

        anchored = ClampInsideRoot(anchored);
        _card.anchoredPosition = anchored;
    }

    Vector2 ClampInsideRoot(Vector2 pos)
    {
        Rect r = _root.rect;
        float halfW = CardW * 0.5f + 20f;
        float halfH = CardH * 0.5f + 20f;
        float minX = r.xMin + halfW, maxX = r.xMax - halfW;
        float minY = r.yMin + halfH, maxY = r.yMax - halfH;
        if (minX < maxX) pos.x = Mathf.Clamp(pos.x, minX, maxX);
        else pos.x = 0f;
        if (minY < maxY) pos.y = Mathf.Clamp(pos.y, minY, maxY);
        else pos.y = 0f;
        return pos;
    }

    // ============================================================
    // PANEL CONSTRUCTION
    // ============================================================

    void BuildPanel()
    {
        if (_canvas == null) _canvas = Object.FindAnyObjectByType<Canvas>();
        if (_canvas == null) { Debug.LogError("[StatsPopup] No Canvas found."); return; }

        // Fullscreen click-catcher root.
        GameObject root = NewRect("Panel_PlayerStatsPopup", _canvas.transform);
        Stretch(root.GetComponent<RectTransform>());
        _root = root.GetComponent<RectTransform>();
        _group = root.AddComponent<CanvasGroup>();

        var catcher = root.AddComponent<UnityEngine.UI.Image>();
        catcher.color = new Color(0, 0, 0, 0.35f);
        Button catcherBtn = root.AddComponent<Button>();
        catcherBtn.transition = Selectable.Transition.None;
        catcherBtn.onClick.AddListener(Close);

        // Wooden card.
        GameObject card = NewRect("Card", root.transform);
        _card = card.GetComponent<RectTransform>();
        _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
        _card.pivot = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(CardW, CardH);
        _card.anchoredPosition = Vector2.zero;
        var cardImg = card.AddComponent<UnityEngine.UI.Image>();
        if (woodBoardSprite != null) { cardImg.sprite = woodBoardSprite; cardImg.type = Image.Type.Sliced; cardImg.color = WoodTint; }
        else cardImg.color = PanelFallbackBg;
        var sh = card.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.45f);
        sh.effectDistance = new Vector2(5, -5);

        // Card swallows clicks so tapping it doesn't close via the catcher.
        var cardBtn = card.AddComponent<Button>();
        cardBtn.transition = Selectable.Transition.None;

        // Player name (top, above stats).
        _nameLabel = AddTmp(card.transform, "Player", Color.white, 32, TextAlignmentOptions.Left, FontStyles.Bold);
        var nameRt = _nameLabel.rectTransform;
        nameRt.anchorMin = nameRt.anchorMax = new Vector2(0f, 1f);
        nameRt.pivot = new Vector2(0f, 1f);
        nameRt.sizeDelta = new Vector2(300, 46);
        nameRt.anchoredPosition = new Vector2(34, -22);
        _nameLabel.overflowMode = TextOverflowModes.Ellipsis;

        // Person circle icon (top-right).
        GameObject icon = NewRect("FriendIcon", card.transform);
        RectTransform iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(1f, 1f);
        iconRt.pivot = new Vector2(1f, 1f);
        iconRt.sizeDelta = new Vector2(82, 82);
        iconRt.anchoredPosition = new Vector2(-26, -20);
        var iconImg = icon.AddComponent<UnityEngine.UI.Image>();
        iconImg.color = BrownBox;
        if (circleFrameSprite != null) iconImg.sprite = circleFrameSprite;
        var glyph = AddTmp(icon.transform, "\u263A", Color.white, 40, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(glyph.rectTransform);

        // Stat rows.
        _valSkill = AddStatRow(card.transform, "SKILL", -112f);
        _valWinRatio = AddStatRow(card.transform, "WIN RATIO", -162f);
        _valKot = AddStatRow(card.transform, "TOTAL KOT", -212f);

        // Friend toggle button (bottom).
        GameObject addGo = NewRect("FriendButton", card.transform);
        RectTransform addRt = addGo.GetComponent<RectTransform>();
        addRt.anchorMin = addRt.anchorMax = new Vector2(0.5f, 0f);
        addRt.pivot = new Vector2(0.5f, 0f);
        addRt.sizeDelta = new Vector2(280, 76);
        addRt.anchoredPosition = new Vector2(0, 26);
        _addImg = addGo.AddComponent<UnityEngine.UI.Image>();
        _addImg.color = GreenBtn;
        if (woodBoardSprite != null) { _addImg.sprite = woodBoardSprite; _addImg.type = Image.Type.Sliced; }
        _addBtn = addGo.AddComponent<Button>();
        _addBtn.targetGraphic = _addImg;
        _addLabel = AddTmp(addGo.transform, "ADD FRIEND", Color.white, 28, TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(_addLabel.rectTransform);

        _built = true;
        root.SetActive(false);
    }

    TMP_Text AddStatRow(Transform card, string label, float y)
    {
        var lbl = AddTmp(card, label, Color.white, 28, TextAlignmentOptions.Left, FontStyles.Bold);
        var lblRt = lbl.rectTransform;
        lblRt.anchorMin = lblRt.anchorMax = new Vector2(0f, 1f);
        lblRt.pivot = new Vector2(0f, 1f);
        lblRt.sizeDelta = new Vector2(250, 44);
        lblRt.anchoredPosition = new Vector2(34, y);

        var val = AddTmp(card, "—", StatValueColor, 28, TextAlignmentOptions.Right, FontStyles.Bold);
        var valRt = val.rectTransform;
        valRt.anchorMin = valRt.anchorMax = new Vector2(1f, 1f);
        valRt.pivot = new Vector2(1f, 1f);
        valRt.sizeDelta = new Vector2(150, 44);
        valRt.anchoredPosition = new Vector2(-30, y);
        return val;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    Transform FindInScene(string name)
    {
        var all = Resources.FindObjectsOfTypeAll<RectTransform>();
        foreach (var rt in all)
        {
            if (rt.name != name) continue;
            if (!rt.gameObject.scene.IsValid()) continue; // skip prefab assets
            return rt.transform;
        }
        return null;
    }

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
