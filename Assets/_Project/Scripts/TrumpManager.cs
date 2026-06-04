using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using DG.Tweening;

public class TrumpManager : MonoBehaviourPunCallbacks
{
    public static TrumpManager Instance;

    [Header("UI References")]
    public Image trumpIcon;
    public TMP_Text trumpSuitText;
    public GameObject trumpChangePopup;
    public TMP_Text trumpChangeText;

    [Header("Suit Sprites")]
    public Sprite spadeSprite;
    public Sprite heartSprite;
    public Sprite diamondSprite;
    public Sprite clubSprite;
    public Sprite hiddenTrumpSprite;

    const float TrumpPopupVisibleSeconds = 2f;

    private CardSuit currentTrumpSuit = CardSuit.Spades;
    private bool isTrumpRevealed = true;
    private Tween _popupTween;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (trumpChangePopup != null) trumpChangePopup.SetActive(false);
        ApplyTrumpForCurrentGameMode(false);
    }

    public static void ApplyTrumpForCurrentGameMode(bool showPopup)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[Trump] ApplyTrumpForCurrentGameMode — TrumpManager.Instance is null.");
            return;
        }

        GameModeType mode = GameSettings.Instance != null
            ? GameSettings.Instance.currentMode
            : GameModeType.TrumpSpades;

        switch (mode)
        {
            case GameModeType.TrumpSpades:
                Instance.ApplyTrumpState(CardSuit.Spades, true, showPopup);
                break;
            case GameModeType.ThirteenthCardTrump:
                Instance.ApplyTrumpState(CardSuit.Spades, false, false);
                break;
            case GameModeType.Cut1Trump:
            case GameModeType.Cut2Trump:
                Instance.ApplyTrumpState(CardSuit.Spades, false, false);
                break;
            default:
                Instance.ApplyTrumpState(CardSuit.Spades, true, showPopup);
                break;
        }
    }

    public void RefreshFromRoomProperties(bool showPopupIfChanged)
    {
        CardSuit oldSuit = currentTrumpSuit;
        bool oldRevealed = isTrumpRevealed;

        CardSuit suit = GetCurrentTrumpSuit();
        bool revealed = IsTrumpRevealed();

        currentTrumpSuit = suit;
        isTrumpRevealed = revealed;
        PlayerHand.currentTrumpSuit = suit;
        PlayerHand.isTrumpRevealed = revealed;

        bool changed = suit != oldSuit || revealed != oldRevealed;
        LogTrumpState("RefreshFromRoomProperties", oldSuit, oldRevealed, suit, revealed);

        EnsureTrumpDisplayVisible();
        UpdateTrumpUI(suit);

        if (showPopupIfChanged && changed && revealed)
            ShowTrumpChangePopup(suit);
    }

    void ApplyTrumpState(CardSuit suit, bool revealed, bool showPopup)
    {
        SetTrumpSuit(suit, showPopup && revealed, revealed);
    }

    public CardSuit GetCurrentTrumpSuit()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TS", out object suit))
            return (CardSuit)(int)suit;
        return currentTrumpSuit;
    }

    public bool IsTrumpRevealed()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TR", out object revealed))
            return (bool)revealed;
        return isTrumpRevealed;
    }

    public void SetTrumpSuit(CardSuit newSuit, bool showPopup, bool isRevealed = true)
    {
        CardSuit oldSuit = currentTrumpSuit;
        bool oldRevealed = isTrumpRevealed;

        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
            {
                { "TS", (int)newSuit },
                { "TR", isRevealed }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        currentTrumpSuit = newSuit;
        isTrumpRevealed = isRevealed;
        PlayerHand.currentTrumpSuit = newSuit;
        PlayerHand.isTrumpRevealed = isRevealed;

        LogTrumpState("SetTrumpSuit", oldSuit, oldRevealed, newSuit, isRevealed);

        EnsureTrumpDisplayVisible();
        UpdateTrumpUI(newSuit);

        bool changed = newSuit != oldSuit || isRevealed != oldRevealed;
        if (showPopup && changed && isRevealed)
            ShowTrumpChangePopup(newSuit);
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (!propertiesThatChanged.ContainsKey("TS") && !propertiesThatChanged.ContainsKey("TR"))
            return;

        CardSuit oldSuit = currentTrumpSuit;
        bool oldRevealed = isTrumpRevealed;

        CardSuit suit = GetCurrentTrumpSuit();
        bool revealed = IsTrumpRevealed();

        currentTrumpSuit = suit;
        isTrumpRevealed = revealed;
        PlayerHand.currentTrumpSuit = suit;
        PlayerHand.isTrumpRevealed = revealed;

        bool changed = suit != oldSuit || revealed != oldRevealed;
        LogTrumpState("OnRoomPropertiesUpdate", oldSuit, oldRevealed, suit, revealed);

        EnsureTrumpDisplayVisible();
        UpdateTrumpUI(suit);

        if (changed && revealed)
            ShowTrumpChangePopup(suit);
    }

    [PunRPC]
    public void RPC_SetTrumpSuit(int suitIndex, bool showPopup, bool isRevealed)
    {
        SetTrumpSuit((CardSuit)suitIndex, showPopup, isRevealed);
    }

    public void SyncTrumpSuit(CardSuit newSuit, bool showPopup, bool isRevealed = true)
    {
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            if (photonView != null)
                photonView.RPC("RPC_SetTrumpSuit", RpcTarget.All, (int)newSuit, showPopup, isRevealed);
            else
            {
                Debug.LogWarning("[Trump] photonView is null — applying trump locally only.");
                SetTrumpSuit(newSuit, showPopup, isRevealed);
            }
        }
        else
        {
            SetTrumpSuit(newSuit, showPopup, isRevealed);
        }
    }

    public void UpdateTrumpUI(CardSuit newTrump)
    {
        EnsureTrumpDisplayVisible();

        if (trumpIcon != null)
        {
            Sprite sprite = GetSpriteForTrump(newTrump, isTrumpRevealed);
            if (sprite != null)
            {
                trumpIcon.sprite = sprite;
                trumpIcon.color = isTrumpRevealed ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1f);
                trumpIcon.enabled = true;
            }
            else
                Debug.LogWarning($"[Trump] No sprite for {newTrump} (revealed={isTrumpRevealed}). Assign suit sprites on TrumpManager.");
        }
        else
        {
            Debug.LogWarning("[Trump] trumpIcon is not assigned on TrumpManager.");
        }

        if (trumpSuitText != null)
        {
            trumpSuitText.gameObject.SetActive(true);
            trumpSuitText.text = isTrumpRevealed
                ? "Trump: " + newTrump
                : "Trump: Hidden";
        }
    }

    void EnsureTrumpDisplayVisible()
    {
        if (trumpIcon != null)
        {
            trumpIcon.gameObject.SetActive(true);
            trumpIcon.enabled = true;
        }

        if (trumpSuitText != null)
            trumpSuitText.gameObject.SetActive(true);

        if (trumpIcon == null) return;

        Transform node = trumpIcon.transform;
        while (node != null)
        {
            if (node.name == "TrumpDisplay")
            {
                node.gameObject.SetActive(true);
                break;
            }
            node = node.parent;
        }
    }

    Sprite GetSpriteForTrump(CardSuit suit, bool revealed)
    {
        if (!revealed)
            return hiddenTrumpSprite;

        switch (suit)
        {
            case CardSuit.Spades: return spadeSprite;
            case CardSuit.Hearts: return heartSprite;
            case CardSuit.Diamonds: return diamondSprite;
            case CardSuit.Clubs: return clubSprite;
            default: return spadeSprite;
        }
    }

    void ShowTrumpChangePopup(CardSuit newSuit)
    {
        if (trumpChangePopup == null)
        {
            Debug.LogWarning("[Trump] trumpChangePopup is not assigned.");
            return;
        }

        _popupTween?.Kill();
        trumpChangePopup.SetActive(true);
        trumpChangePopup.transform.DOKill();

        if (trumpChangeText != null)
            trumpChangeText.text = "Trump Changed to " + newSuit + "!";

        trumpChangePopup.transform.localScale = Vector3.zero;
        trumpChangePopup.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);

        _popupTween = DOVirtual.DelayedCall(TrumpPopupVisibleSeconds, () =>
        {
            if (trumpChangePopup == null) return;
            trumpChangePopup.transform.DOScale(0f, 0.5f).SetEase(Ease.InBack)
                .OnComplete(() =>
                {
                    if (trumpChangePopup != null)
                        trumpChangePopup.SetActive(false);
                });
        });
    }

    static void LogTrumpState(string source, CardSuit oldSuit, bool oldRevealed, CardSuit newSuit, bool newRevealed)
    {
        Debug.Log(
            $"[Trump] {source}\n" +
            $"[Trump] Old Trump: {oldSuit} (revealed={oldRevealed})\n" +
            $"[Trump] New Trump: {newSuit} (revealed={newRevealed})\n" +
            $"[Trump] Current Trump: {newSuit}");
    }
}
