using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

public class FriendsDrawerController : MonoBehaviour
{
    public static FriendsDrawerController Instance;

    [Header("UI References")]
    public RectTransform friendListPanel;
    public Button inviteFriendsButton;
    public RectTransform arrowIcon;
    [Tooltip("Optional close button on the friend list panel (used during in-game REPLACE).")]
    public Button inGameCloseButton;

    [Header("Animation Settings")]
    [SerializeField] private float openX = 0f;
    [SerializeField] private float closedX = 700f;
    [SerializeField] private float duration = 0.4f;

    private bool isOpen = false;
    Transform _homeParent;
    int _homeSiblingIndex;
    bool _openedFromGame;
    Image _inGameDimOverlay;
    Image _homeDimOverlay;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        if (friendListPanel != null)
            friendListPanel.anchoredPosition = new Vector2(closedX, friendListPanel.anchoredPosition.y);

        isOpen = false;

        if (inviteFriendsButton != null)
        {
            inviteFriendsButton.onClick.RemoveAllListeners();
            inviteFriendsButton.onClick.AddListener(ToggleDrawer);
        }
    }

    public void ToggleDrawer()
    {
        if (friendListPanel == null) return;

        if (DOTween.IsTweening(friendListPanel)) return;

        if (isOpen)
            CloseDrawer();
        else
            OpenDrawerInternal();
    }

    void OpenDrawerInternal()
    {
        friendListPanel.DOKill();
        if (arrowIcon != null) arrowIcon.DOKill();

        friendListPanel.DOAnchorPosX(openX, duration).SetEase(Ease.OutBack);

        if (arrowIcon != null) arrowIcon.DOLocalRotate(new Vector3(0, 0, 180), duration);

        isOpen = true;
        Debug.Log("[Drawer] Friendlist opened. Refreshing status...");

        if (!_openedFromGame)
            ShowHomeDimOverlay();

        if (_openedFromGame)
            ShowInGameCloseUi();

        if (FriendsPanelUIController.Instance != null)
            FriendsPanelUIController.Instance.RefreshAll();

        // Tasks 9/18/25: pull live online/in-game status from Firebase presence + Photon and
        // repaint the rows whenever the drawer opens (works in-room too).
        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.RefreshFriendsStatus();
    }

    public void CloseDrawer()
    {
        if (friendListPanel == null) return;
        if (DOTween.IsTweening(friendListPanel)) return;

        if (!isOpen)
        {
            HideInGameCloseUi();
            if (_openedFromGame)
                RestoreToHomeHierarchy();
            return;
        }

        friendListPanel.DOKill();
        if (arrowIcon != null) arrowIcon.DOKill();

        friendListPanel.DOAnchorPosX(closedX, duration).SetEase(Ease.InQuad);

        if (arrowIcon != null) arrowIcon.DOLocalRotate(new Vector3(0, 0, 0), duration);

        isOpen = false;
        Debug.Log("[Drawer] Friendlist closed.");

        HideHomeDimOverlay();
        HideInGameCloseUi();
        if (_openedFromGame)
            RestoreToHomeHierarchy();
    }

    public void OpenDrawer()
    {
        if (isOpen) return;
        OpenDrawerInternal();
    }

    /// <summary>
    /// Shows the home friends drawer on top of the active game UI (used by in-game REPLACE).
    /// </summary>
    public void OpenDrawerDuringGame()
    {
        if (friendListPanel == null) return;

        if (_homeParent == null)
        {
            _homeParent = friendListPanel.parent;
            _homeSiblingIndex = friendListPanel.GetSiblingIndex();
        }

        Transform gameRoot = ResolveGameUiRoot();
        if (gameRoot != null)
        {
            EnsureInGameDimOverlay(gameRoot);
            if (_inGameDimOverlay != null)
            {
                _inGameDimOverlay.transform.SetParent(gameRoot, false);
                _inGameDimOverlay.transform.SetAsLastSibling();
                _inGameDimOverlay.gameObject.SetActive(true);
            }

            friendListPanel.SetParent(gameRoot, false);
            friendListPanel.SetAsLastSibling();
        }

        friendListPanel.gameObject.SetActive(true);
        _openedFromGame = true;
        EnsureInGameCloseButton();
        OpenDrawer();
    }

    static Transform ResolveGameUiRoot()
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            return NetworkManager.Instance.gameCanvasGroup.transform;

        Canvas gameCanvas = Object.FindAnyObjectByType<Canvas>();
        return gameCanvas != null ? gameCanvas.transform : null;
    }

    void RestoreToHomeHierarchy()
    {
        if (friendListPanel == null || _homeParent == null) return;

        friendListPanel.SetParent(_homeParent, false);
        friendListPanel.SetSiblingIndex(_homeSiblingIndex);
        _openedFromGame = false;
    }

    void EnsureInGameCloseButton()
    {
        if (friendListPanel == null) return;

        if (inGameCloseButton == null)
        {
            Transform existing = friendListPanel.Find("Btn_CloseFriendList");
            if (existing != null)
                inGameCloseButton = existing.GetComponent<Button>();
        }

        if (inGameCloseButton != null) return;

        var go = new GameObject("Btn_CloseFriendList", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(friendListPanel, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-14f, -14f);
        rt.sizeDelta = new Vector2(56f, 56f);

        var bg = go.GetComponent<Image>();
        bg.color = new Color(0.35f, 0.22f, 0.12f, 1f);

        var labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "X";
        label.fontSize = 28;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        TMP_FontAsset closeFont = null;
        if (FriendsPanelUIController.Instance != null)
            closeFont = FriendsPanelUIController.Instance.customFont;
        if (closeFont == null)
            closeFont = TMP_Settings.defaultFontAsset;
        if (closeFont != null)
            label.font = closeFont;

        inGameCloseButton = go.GetComponent<Button>();
        inGameCloseButton.targetGraphic = bg;
        inGameCloseButton.transition = Selectable.Transition.ColorTint;
        inGameCloseButton.onClick.RemoveAllListeners();
        inGameCloseButton.onClick.AddListener(CloseDrawer);
        go.SetActive(false);
    }

    void EnsureInGameDimOverlay(Transform parent)
    {
        if (_inGameDimOverlay != null) return;

        var go = new GameObject("FriendListDimOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _inGameDimOverlay = go.GetComponent<Image>();
        _inGameDimOverlay.color = new Color(0f, 0f, 0f, 0.55f);
        _inGameDimOverlay.raycastTarget = true;

        var dimBtn = go.GetComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.RemoveAllListeners();
        dimBtn.onClick.AddListener(CloseDrawer);

        go.SetActive(false);
    }

    void ShowInGameCloseUi()
    {
        if (!_openedFromGame) return;

        EnsureInGameCloseButton();
        if (inGameCloseButton != null)
            inGameCloseButton.gameObject.SetActive(true);

        if (_inGameDimOverlay != null)
            _inGameDimOverlay.gameObject.SetActive(true);
    }

    void HideInGameCloseUi()
    {
        if (inGameCloseButton != null)
            inGameCloseButton.gameObject.SetActive(false);

        if (_inGameDimOverlay != null)
            _inGameDimOverlay.gameObject.SetActive(false);
    }

    void ShowHomeDimOverlay()
    {
        Transform root = friendListPanel != null ? friendListPanel.parent : transform;
        if (root == null) return;

        if (_homeDimOverlay == null)
        {
            var go = new GameObject("HomeFriendListDimOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(root, false);
            go.transform.SetAsLastSibling();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _homeDimOverlay = go.GetComponent<Image>();
            _homeDimOverlay.color = new Color(0f, 0f, 0f, 0.45f);
            _homeDimOverlay.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(CloseDrawer);
        }

        _homeDimOverlay.transform.SetAsLastSibling();
        if (friendListPanel != null)
            friendListPanel.SetAsLastSibling();
        _homeDimOverlay.gameObject.SetActive(true);
    }

    void HideHomeDimOverlay()
    {
        if (_homeDimOverlay != null)
            _homeDimOverlay.gameObject.SetActive(false);
    }
}
