using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Photon.Pun;

/// <summary>
/// Free-Fire / PUBG style incoming GAME INVITE popup that slides in from the left edge with
/// ACCEPT / DECLINE buttons. Tapping ACCEPT joins the inviter's table immediately; the popup
/// also auto-hides after a few seconds if ignored.
///
/// Fully self-contained: it builds its own UI under the top-most Canvas the first time it is
/// shown (no scene wiring required). Driven by PlayWithFriendsManager.ShowIncomingInvite via
/// the static <see cref="ShowInvite"/> entry point.
/// </summary>
public class IncomingInvitePopup : MonoBehaviour
{
    public static IncomingInvitePopup Instance;

    const float AutoHideSeconds = 15f;

    static readonly Color CardBg = new Color(0.16f, 0.10f, 0.04f, 0.98f);
    static readonly Color GreenBtn = new Color(0.16f, 0.70f, 0.42f, 1f);
    static readonly Color RedBtn = new Color(0.78f, 0.12f, 0.13f, 1f);
    static readonly Color TitleGold = new Color(1f, 0.85f, 0.42f, 1f);

    RectTransform _card;
    TMP_Text _messageText;
    string _roomPin;
    Coroutine _autoHide;

    // ============================================================
    // STATIC ENTRY
    // ============================================================
    public static void ShowInvite(string fromName, string roomPin)
    {
        EnsureInstance();
        if (Instance != null) Instance.Show(fromName, roomPin);
    }

    static void EnsureInstance()
    {
        if (Instance != null) return;

        Canvas canvas = ResolveTopCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[InvitePopup] No Canvas found — cannot show invite popup.");
            return;
        }

        var go = new GameObject("IncomingInvitePopup", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        Instance = go.AddComponent<IncomingInvitePopup>();
        Instance.Build();
    }

    static Canvas ResolveTopCanvas()
    {
        Canvas best = null;
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c == null || !c.isRootCanvas) continue;
            if (best == null || c.sortingOrder >= best.sortingOrder) best = c;
        }
        if (best == null && canvases.Length > 0) best = canvases[0];
        return best;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============================================================
    // BUILD
    // ============================================================
    void Build()
    {
        var selfRt = GetComponent<RectTransform>();
        selfRt.anchorMin = Vector2.zero;
        selfRt.anchorMax = Vector2.one;
        selfRt.offsetMin = Vector2.zero;
        selfRt.offsetMax = Vector2.zero;

        // The popup itself never blocks the rest of the screen; only its card is interactive.
        var rootGroup = gameObject.AddComponent<CanvasGroup>();
        rootGroup.blocksRaycasts = false;
        rootGroup.interactable = true;

        GameObject cardGo = NewRect("Card", transform);
        _card = cardGo.GetComponent<RectTransform>();
        _card.anchorMin = new Vector2(0f, 0.5f);
        _card.anchorMax = new Vector2(0f, 0.5f);
        _card.pivot = new Vector2(0f, 0.5f);
        _card.sizeDelta = new Vector2(640, 240);
        _card.anchoredPosition = new Vector2(-700f, 0f);

        Image cardImg = cardGo.AddComponent<Image>();
        cardImg.color = CardBg;
        cardImg.raycastTarget = true;
        Shadow shadow = cardGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        shadow.effectDistance = new Vector2(6f, -6f);

        // Left accent strip.
        GameObject strip = NewRect("Accent", cardGo.transform);
        RectTransform stripRt = strip.GetComponent<RectTransform>();
        stripRt.anchorMin = new Vector2(0f, 0f);
        stripRt.anchorMax = new Vector2(0f, 1f);
        stripRt.pivot = new Vector2(0f, 0.5f);
        stripRt.sizeDelta = new Vector2(16f, 0f);
        stripRt.anchoredPosition = Vector2.zero;
        strip.AddComponent<Image>().color = GreenBtn;

        // Title.
        TMP_Text title = AddTmp(cardGo.transform, "GAME INVITE", TitleGold, 32, TextAlignmentOptions.Left, FontStyles.Bold);
        RectTransform tRt = title.rectTransform;
        tRt.anchorMin = new Vector2(0f, 1f);
        tRt.anchorMax = new Vector2(1f, 1f);
        tRt.pivot = new Vector2(0f, 1f);
        tRt.offsetMin = new Vector2(40f, -62f);
        tRt.offsetMax = new Vector2(-20f, -16f);

        // Message.
        _messageText = AddTmp(cardGo.transform, "", Color.white, 26, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        RectTransform mRt = _messageText.rectTransform;
        mRt.anchorMin = new Vector2(0f, 0f);
        mRt.anchorMax = new Vector2(1f, 1f);
        mRt.pivot = new Vector2(0.5f, 0.5f);
        mRt.offsetMin = new Vector2(40f, 92f);
        mRt.offsetMax = new Vector2(-20f, -70f);

        // Decline (bottom-left).
        CreateButton(cardGo.transform, "DECLINE", RedBtn,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 18f), OnDecline);

        // Accept (bottom-right).
        CreateButton(cardGo.transform, "ACCEPT", GreenBtn,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 18f), OnAccept);

        gameObject.SetActive(false);
    }

    // ============================================================
    // SHOW / HIDE
    // ============================================================
    void Show(string fromName, string roomPin)
    {
        _roomPin = roomPin;
        if (string.IsNullOrEmpty(fromName)) fromName = "A friend";

        if (_messageText != null)
            _messageText.text = $"<b>{fromName}</b> is inviting you to play!\nTap ACCEPT to join the table.";

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        float hiddenX = -(_card.sizeDelta.x + 60f);
        _card.DOKill();
        _card.anchoredPosition = new Vector2(hiddenX, _card.anchoredPosition.y);
        _card.DOAnchorPosX(24f, 0.45f).SetEase(Ease.OutBack).SetUpdate(true);

        if (_autoHide != null) StopCoroutine(_autoHide);
        _autoHide = StartCoroutine(AutoHideRoutine());
    }

    IEnumerator AutoHideRoutine()
    {
        yield return new WaitForSecondsRealtime(AutoHideSeconds);
        Hide();
    }

    void Hide()
    {
        if (_autoHide != null) { StopCoroutine(_autoHide); _autoHide = null; }
        if (_card == null) { gameObject.SetActive(false); return; }

        float hiddenX = -(_card.sizeDelta.x + 60f);
        _card.DOKill();
        _card.DOAnchorPosX(hiddenX, 0.3f).SetEase(Ease.InQuad).SetUpdate(true)
            .OnComplete(() => { if (this != null) gameObject.SetActive(false); });
    }

    // ============================================================
    // ACTIONS
    // ============================================================
    void OnAccept()
    {
        string pin = _roomPin;
        Hide();
        if (string.IsNullOrEmpty(pin)) return;

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            if (NetworkManager.Instance != null) NetworkManager.Instance.ConnectToPhoton();
            return;
        }

        // Already seated at a table — joining another would fail. Ignore (accepts normally
        // happen from the home screen).
        if (PhotonNetwork.InRoom) return;

        if (PlayWithFriendsManager.Instance != null)
            PlayWithFriendsManager.Instance.JoinRoomWithPINText(pin);
        else
            PhotonNetwork.JoinRoom(pin);
    }

    void OnDecline() => Hide();

    // ============================================================
    // UI HELPERS
    // ============================================================
    void CreateButton(Transform parent, string label, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = NewRect("Btn_" + label, parent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(anchorMin.x, anchorMin.y);
        rt.sizeDelta = new Vector2(260f, 64f);
        rt.anchoredPosition = anchoredPos;

        Image img = go.AddComponent<Image>();
        img.color = color;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        TMP_Text txt = AddTmp(go.transform, label, Color.white, 28, TextAlignmentOptions.Center, FontStyles.Bold);
        RectTransform txtRt = txt.rectTransform;
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
        txt.raycastTarget = false;
    }

    static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static TMP_Text AddTmp(Transform parent, string text, Color color, int size,
        TextAlignmentOptions align, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.fontStyle = style;
        return tmp;
    }
}
