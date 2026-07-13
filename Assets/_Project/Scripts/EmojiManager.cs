using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game emoji reactions synced over Photon. Shows a short-lived sprite on the sender's seat avatar.
/// Wire emoji picker buttons to <see cref="SendEmoji"/> (index 0..N-1).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class EmojiManager : MonoBehaviourPun
{
    public static EmojiManager Instance { get; private set; }

    const string EmojiSheetPath = "Assets/_Project/Art/Sprites/Images/NEW/Emojies.png";

    [Header("UI")]
    public GameObject emojiPanel;
    public Button openEmojiButton;
    public Button closePanelButton;

    [Header("Emoji Sprites (slices from Emojies.png)")]
    public Sprite[] emojiSprites;

    [Header("Seat Overlays (optional — auto-created on avatars if empty)")]
    public Image[] seatEmojiDisplays = new Image[4];

    [Header("Settings")]
    public float displayDuration = 3f;

    static readonly string[] SeatEmojiLookupPaths =
    {
        "You/SeatEmoji",
        "Opponent_Left/SeatEmoji",
        "Opponent_Top/SeatEmoji",
        "Opponent_Right/SeatEmoji"
    };

    static readonly string[] SeatAvatarLookupPaths =
    {
        "You/You_Avatar",
        "Opponent_Left/Playe2_Avatar",
        "Opponent_Top/Player3_Avatar",
        "Opponent_Right/Playe4_Avatar"
    };

    PhotonView _view;
    GameObject _autoBlocker;
    GameObject _runtimePanel;
    readonly Coroutine[] _hideRoutines = new Coroutine[4];

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        _view = GetComponent<PhotonView>();
        TryLoadEmojiSpritesFromSheet();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        if (openEmojiButton != null)
            openEmojiButton.onClick.AddListener(OpenPanel);
        if (closePanelButton != null)
            closePanelButton.onClick.AddListener(ClosePanel);

        HideAllSeatEmojis();
        ClosePanel();
    }

    public void InitializeGameScene()
    {
        ResolveSeatEmojiDisplays();
        HideAllSeatEmojis();
        RefreshSpectatorUi();
    }

    public void RefreshSpectatorUi()
    {
        bool show = !DeckManager.ShouldHideInGameEmoteButtons();
        if (openEmojiButton != null)
            openEmojiButton.gameObject.SetActive(show);
        else if (UiSafeLookup.TryGet("Button_Emojies", out GameObject emojiBtn) && emojiBtn != null)
            emojiBtn.SetActive(show);

        if (!show)
            ClosePanel();
    }

    public void OpenPanel()
    {
        if (DeckManager.ShouldHideInGameEmoteButtons())
            return;

        EnsureRuntimeEmojiPanel();
        EnsureAutoBlocker();

        if (_autoBlocker != null)
            _autoBlocker.SetActive(true);
        if (closePanelButton != null)
            closePanelButton.gameObject.SetActive(true);

        GameObject panel = emojiPanel != null ? emojiPanel : _runtimePanel;
        if (panel != null)
        {
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
        }
    }

    public void ClosePanel()
    {
        if (emojiPanel != null)
            emojiPanel.SetActive(false);
        if (_runtimePanel != null)
            _runtimePanel.SetActive(false);
        if (closePanelButton != null)
            closePanelButton.gameObject.SetActive(false);
        if (_autoBlocker != null)
            _autoBlocker.SetActive(false);
    }

    /// <summary>Hook this to each emoji button in the picker (0 = first sprite, etc.).</summary>
    public void SendEmoji(int emojiIndex)
    {
        if (DeckManager.ShouldHideInGameEmoteButtons())
            return;

        ClosePanel();

        if (emojiSprites == null || emojiIndex < 0 || emojiIndex >= emojiSprites.Length)
            return;

        int actor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
        ShowEmojiOnSeat(actor, emojiIndex);

        if (!PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
            return;

        if (_view != null && _view.ViewID > 0)
            _view.RPC(nameof(RPC_ShowEmoji), RpcTarget.Others, actor, emojiIndex);
    }

    [PunRPC]
    void RPC_ShowEmoji(int senderActor, int emojiIndex)
    {
        ShowEmojiOnSeat(senderActor, emojiIndex);
    }

    void ShowEmojiOnSeat(int actorNumber, int emojiIndex)
    {
        if (emojiSprites == null || emojiIndex < 0 || emojiIndex >= emojiSprites.Length)
            return;

        ResolveSeatEmojiDisplays();

        int seat = ResolveSeatIndex(actorNumber);
        if (seat < 0 || seat >= seatEmojiDisplays.Length)
            return;

        Image img = seatEmojiDisplays[seat];
        if (img == null)
            return;

        img.preserveAspect = true;

        if (_hideRoutines[seat] != null)
            StopCoroutine(_hideRoutines[seat]);
        _hideRoutines[seat] = StartCoroutine(AnimateEmojiAlpha(seat, img, emojiSprites[emojiIndex]));
    }

    private IEnumerator AnimateEmojiAlpha(int seat, Image emojiDisplay, Sprite selectedSprite)
    {
        // 1. Setup and ensure it starts fully transparent
        emojiDisplay.sprite = selectedSprite;
        emojiDisplay.gameObject.SetActive(true);

        Color c = emojiDisplay.color;
        c.a = 0f;
        emojiDisplay.color = c;

        float fadeSpeed = 3.5f; // Speed of the fade

        // 2. Smooth Fade In
        while (emojiDisplay.color.a < 1f)
        {
            c.a += Time.deltaTime * fadeSpeed;
            if (c.a > 1f) c.a = 1f;
            emojiDisplay.color = c;
            yield return null;
        }

        // 3. Wait for exactly 2 seconds while fully visible
        yield return new WaitForSeconds(2f);

        // 4. Smooth Fade Out
        while (emojiDisplay.color.a > 0f)
        {
            c.a -= Time.deltaTime * fadeSpeed;
            if (c.a < 0f) c.a = 0f;
            emojiDisplay.color = c;
            yield return null;
        }

        // 5. Cleanup
        emojiDisplay.gameObject.SetActive(false);

        // Reset alpha to 1 for next time (safety measure)
        c.a = 1f;
        emojiDisplay.color = c;

        _hideRoutines[seat] = null;
    }

    void HideAllSeatEmojis()
    {
        if (seatEmojiDisplays == null)
            return;

        for (int i = 0; i < seatEmojiDisplays.Length; i++)
        {
            if (_hideRoutines[i] != null)
            {
                StopCoroutine(_hideRoutines[i]);
                _hideRoutines[i] = null;
            }

            if (seatEmojiDisplays[i] != null)
            {
                Color c = seatEmojiDisplays[i].color;
                c.a = 1f;
                seatEmojiDisplays[i].color = c;
                seatEmojiDisplays[i].gameObject.SetActive(false);
            }
        }
    }

    void ResolveSeatEmojiDisplays()
    {
        if (seatEmojiDisplays == null || seatEmojiDisplays.Length < 4)
            seatEmojiDisplays = new Image[4];

        Transform searchRoot = null;
        if (NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            searchRoot = NetworkManager.Instance.gameCanvasGroup.transform;
        if (searchRoot != null)
            UiSafeLookup.SetSearchRoot(searchRoot);

        for (int seat = 0; seat < 4; seat++)
        {
            if (seatEmojiDisplays[seat] != null)
                continue;

            if (UiSafeLookup.TryGetPath(SeatEmojiLookupPaths[seat], out GameObject existing))
            {
                seatEmojiDisplays[seat] = existing.GetComponent<Image>();
                continue;
            }

            Transform avatarParent = null;
            if (UiSafeLookup.TryGetPath(SeatAvatarLookupPaths[seat], out GameObject avatarGo))
                avatarParent = avatarGo.transform.parent;

            if (avatarParent != null)
                seatEmojiDisplays[seat] = CreateSeatEmojiOverlay(avatarParent);
        }
    }

    static Image CreateSeatEmojiOverlay(Transform parent)
    {
        var go = new GameObject("SeatEmoji", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(10f, 10f);
        rt.sizeDelta = new Vector2(52f, 52f);

        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        go.SetActive(false);
        return img;
    }

    int ResolveSeatIndex(int actorNumber)
    {
        if (PlayerHand.LocalInstance != null)
            return PlayerHand.LocalInstance.GetSeatIndex(actorNumber);

        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.LocalPlayer == null)
            return 0;

        int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
        return (actorNumber - localActor + 4) % 4;
    }

    void EnsureAutoBlocker()
    {
        if (_autoBlocker != null)
            return;

        GameObject panel = emojiPanel != null ? emojiPanel : _runtimePanel;
        if (panel == null)
            return;

        Transform parent = panel.transform.parent;
        if (parent == null)
            return;

        _autoBlocker = new GameObject("EmojiBlocker_Auto");
        _autoBlocker.transform.SetParent(parent, false);
        _autoBlocker.transform.SetSiblingIndex(panel.transform.GetSiblingIndex());

        var rt = _autoBlocker.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = _autoBlocker.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;

        var btn = _autoBlocker.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(ClosePanel);
        _autoBlocker.SetActive(false);
    }

    void EnsureRuntimeEmojiPanel()
    {
        if (emojiPanel != null || _runtimePanel != null || emojiSprites == null || emojiSprites.Length == 0)
            return;

        Transform parent = openEmojiButton != null ? openEmojiButton.transform.parent : null;
        if (parent == null && NetworkManager.Instance != null && NetworkManager.Instance.gameCanvasGroup != null)
            parent = NetworkManager.Instance.gameCanvasGroup.transform;
        if (parent == null)
            return;

        _runtimePanel = new GameObject("Panel_Emoji_Runtime", typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup));
        _runtimePanel.transform.SetParent(parent, false);

        var rt = _runtimePanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(150f, 340f);
        rt.sizeDelta = new Vector2(360f, 280f);

        var bg = _runtimePanel.GetComponent<Image>();
        bg.color = new Color(0.12f, 0.14f, 0.2f, 0.95f);

        var grid = _runtimePanel.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(72f, 72f);
        grid.spacing = new Vector2(12f, 12f);
        grid.padding = new RectOffset(16, 16, 16, 16);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;

        for (int i = 0; i < emojiSprites.Length; i++)
        {
            if (emojiSprites[i] == null)
                continue;

            int index = i;
            var btnGo = new GameObject("Emoji_" + i, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(_runtimePanel.transform, false);

            var btnImg = btnGo.GetComponent<Image>();
            btnImg.sprite = emojiSprites[i];
            btnImg.preserveAspect = true;

            var btn = btnGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(() => SendEmoji(index));
        }

        _runtimePanel.SetActive(false);
    }

    void TryLoadEmojiSpritesFromSheet()
    {
        if (emojiSprites != null && emojiSprites.Length > 0)
            return;

#if UNITY_EDITOR
        var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(EmojiSheetPath);
        if (assets == null || assets.Length == 0)
            return;

        var list = new System.Collections.Generic.List<Sprite>();
        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is Sprite sprite)
                list.Add(sprite);
        }

        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        emojiSprites = list.ToArray();
#endif
    }

#if UNITY_EDITOR
    [ContextMenu("Reload Emoji Sprites From Sheet")]
    void EditorReloadEmojiSprites()
    {
        emojiSprites = null;
        TryLoadEmojiSpritesFromSheet();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
