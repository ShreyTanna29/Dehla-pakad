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
        GameStabilityAudit.ValidateTrump("TrumpManager.Start");
    }

    public void InitializeGameScene()
    {
        if (trumpIcon != null)
        {
            Transform t = trumpIcon.transform;
            while (t != null)
            {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                t = t.parent;
            }
        }

        RefreshFromRoomProperties(false);
    }

    public static void ApplyTrumpForCurrentGameMode(bool showPopup)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[Trump] ApplyTrumpForCurrentGameMode — TrumpManager.Instance is null.");
            return;
        }

        GameModeType mode = Instance.GetCurrentGameMode();
        switch (mode)
        {
            case GameModeType.ThirteenthCardTrump:
                Instance.ApplyTrumpState(CardSuit.Spades, false, false);
                break;
            case GameModeType.Cut1Trump:
                Instance.ApplyTrumpState(CardSuit.Spades, false, false);
                break;
            case GameModeType.Cut2Trump:
                Instance.ApplyTrumpState(CardSuit.Spades, true, false);
                break;
            case GameModeType.HiddenTrump:
                Instance.ApplyTrumpState(CardSuit.Spades, false, false);
                break;
            case GameModeType.TrumpSpades:
            default:
                Instance.ApplyTrumpState(CardSuit.Spades, true, showPopup);
                break;
        }
    }

    GameModeType GetCurrentGameMode()
    {
        return GameSettings.Instance != null
            ? GameSettings.Instance.currentMode
            : GameModeType.TrumpSpades;
    }

    bool ShouldDisplayTrumpAsHidden()
    {
        switch (GetCurrentGameMode())
        {
            case GameModeType.ThirteenthCardTrump:
            case GameModeType.Cut2Trump:
            case GameModeType.HiddenTrump:
                return !isTrumpRevealed;
            case GameModeType.TrumpSpades:
            case GameModeType.Cut1Trump:
            default:
                return false;
        }
    }

    CardSuit ResolveTrumpSuitForDisplay(CardSuit suit)
    {
        if (suit >= CardSuit.Spades && suit <= CardSuit.Diamonds)
            return suit;
        return CardSuit.Spades;
    }

    public void RefreshFromRoomProperties(bool showPopupIfChanged)
    {
        CardSuit oldSuit = currentTrumpSuit;
        bool oldRevealed = isTrumpRevealed;

        CardSuit suit = ResolveTrumpSuitForDisplay(GetCurrentTrumpSuit());
        bool revealed = IsTrumpRevealed();

        currentTrumpSuit = suit;
        isTrumpRevealed = revealed;
        PlayerHand.currentTrumpSuit = suit;
        PlayerHand.isTrumpRevealed = revealed;

        bool changed = suit != oldSuit || revealed != oldRevealed;
        LogTrumpState("RefreshFromRoomProperties", oldSuit, oldRevealed, suit, revealed);

        EnsureTrumpDisplayVisible();
        UpdateTrumpUI(suit);

        if (showPopupIfChanged && changed && revealed && !ShouldDisplayTrumpAsHidden())
            ShowTrumpChangePopup(suit);
    }

    void ApplyTrumpState(CardSuit suit, bool revealed, bool showPopup)
    {
        SetTrumpSuit(suit, showPopup && revealed, revealed);
    }

    public CardSuit GetCurrentTrumpSuit()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TS", out object suit))
            return ResolveTrumpSuitForDisplay((CardSuit)(int)suit);
        return ResolveTrumpSuitForDisplay(currentTrumpSuit);
    }

    public bool IsTrumpRevealed()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("TR", out object revealed))
            return (bool)revealed;
        return isTrumpRevealed;
    }

    public void SetTrumpSuit(CardSuit newSuit, bool showPopup, bool isRevealed = true)
    {
        currentTrumpSuit = newSuit;
        isTrumpRevealed = isRevealed;

        PlayerHand.currentTrumpSuit = newSuit;
        PlayerHand.isTrumpRevealed = isRevealed;

        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new ExitGames.Client.Photon.Hashtable
                {
                    { "TS", (int)newSuit },
                    { "TR", isRevealed }
                });
        }

        UpdateTrumpUI(newSuit);

        if (showPopup && isRevealed)
            ShowTrumpChangePopup(newSuit);

        Debug.Log($"[TrumpUI] Updated trump to {newSuit}, revealed={isRevealed}");
    }

    /// <summary>
    /// Reveals the previously hidden trump (Hidden Trump mode). Updates trump state,
    /// refreshes the UI and shows a "Hidden Trump Unlocked!" popup so every player
    /// knows the hidden card has been opened.
    /// </summary>
    public void RevealHiddenTrump(CardSuit newSuit)
    {
        currentTrumpSuit = newSuit;
        isTrumpRevealed = true;

        PlayerHand.currentTrumpSuit = newSuit;
        PlayerHand.isTrumpRevealed = true;

        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            var props = new ExitGames.Client.Photon.Hashtable
            {
                { "TS", (int)newSuit },
                { "TR", true }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        // Force UI update immediately
        UpdateTrumpUI(newSuit);
        ShowHiddenCardUnlockedPopup(newSuit);

        Debug.Log($"[TrumpUI] Hidden trump unlocked — trump is {newSuit}");
    }

    public void ShowHiddenCardUnlockedPopup(CardSuit suit)
    {
        ShowTrumpChangePopup(suit, "Hidden Card Unlocked!\nTrump is " + suit);
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
        {
            if (GetCurrentGameMode() == GameModeType.HiddenTrump && !oldRevealed)
                ShowHiddenCardUnlockedPopup(suit);
            else if (!ShouldDisplayTrumpAsHidden())
                ShowTrumpChangePopup(suit);
        }
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

        CardSuit displaySuit = ResolveTrumpSuitForDisplay(newTrump);
        bool displayHidden = ShouldDisplayTrumpAsHidden();

        if (trumpIcon != null)
        {
            Sprite sprite = GetSpriteForTrump(displaySuit, displayHidden);
            if (sprite != null)
            {
                trumpIcon.sprite = sprite;
                trumpIcon.color = displayHidden ? new Color(0.85f, 0.85f, 0.85f, 1f) : Color.white;
                trumpIcon.enabled = true;
            }
            else
                Debug.LogWarning($"[Trump] No sprite for {displaySuit} (displayHidden={displayHidden}). Assign suit sprites on TrumpManager.");
        }
        else
        {
            Debug.LogWarning("[Trump] trumpIcon is not assigned on TrumpManager.");
        }

        if (trumpSuitText != null)
        {
            trumpSuitText.gameObject.SetActive(true);
            trumpSuitText.text = displayHidden
                ? "Trump: Hidden"
                : "Trump: " + displaySuit;
        }

        Debug.Log($"[TrumpUI] icon updated to {newTrump}");
        Debug.Log(
            $"[TrumpUI] Mode: {GetCurrentGameMode()}\n" +
$"[TrumpUI] Current Trump: {displaySuit} (gameplay revealed={isTrumpRevealed})\n" +
            $"[TrumpUI] Is Hidden: {displayHidden}\n" +
            $"[TrumpUI] Display Updated: {(displayHidden ? "Hidden" : displaySuit.ToString())}");
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

    Sprite GetSpriteForTrump(CardSuit suit, bool displayHidden)
    {
        if (displayHidden)
            return hiddenTrumpSprite != null ? hiddenTrumpSprite : spadeSprite;

        switch (suit)
        {
            case CardSuit.Spades: return spadeSprite;
            case CardSuit.Hearts: return heartSprite;
            case CardSuit.Diamonds: return diamondSprite;
            case CardSuit.Clubs: return clubSprite;
            default: return spadeSprite;
        }
    }

    void EnsureTrumpChangePopupRefs()
    {
        if (trumpChangePopup != null && trumpChangeText != null) return;

        GameObject found = GameObject.Find("TrumpChangePopup");
        if (found == null) return;

        trumpChangePopup = found;
        if (trumpChangeText == null)
            trumpChangeText = found.GetComponentInChildren<TMP_Text>(true);
    }

    void ShowTrumpChangePopup(CardSuit newSuit, string customMessage = null)
    {
        EnsureTrumpChangePopupRefs();

        if (trumpChangePopup == null)
        {
            Debug.LogWarning("[Trump] trumpChangePopup is not assigned.");
            return;
        }

        Transform node = trumpChangePopup.transform;
        while (node != null)
        {
            if (!node.gameObject.activeSelf)
                node.gameObject.SetActive(true);
            node = node.parent;
        }

        trumpChangePopup.transform.SetAsLastSibling();

        _popupTween?.Kill();
        trumpChangePopup.transform.DOKill();
        trumpChangePopup.SetActive(true);

        if (trumpChangeText != null)
        {
            if (!trumpChangeText.gameObject.activeSelf)
                trumpChangeText.gameObject.SetActive(true);
            trumpChangeText.text = customMessage ?? ("Trump Changed to " + newSuit + "!");
            trumpChangeText.color = Color.white;
            trumpChangeText.enableWordWrapping = true;
        }

        CanvasGroup popupCg = trumpChangePopup.GetComponent<CanvasGroup>();
        if (popupCg == null)
            popupCg = trumpChangePopup.AddComponent<CanvasGroup>();
        popupCg.alpha = 1f;
        popupCg.blocksRaycasts = false;
        popupCg.interactable = false;

        trumpChangePopup.transform.localScale = Vector3.one;
        trumpChangePopup.transform.DOPunchScale(new Vector3(0.08f, 0.08f, 0f), 0.35f, 4, 0.5f).SetUpdate(true);

        _popupTween = DOVirtual.DelayedCall(TrumpPopupVisibleSeconds, () =>
        {
            if (trumpChangePopup == null) return;
            trumpChangePopup.transform.DOScale(0f, 0.35f).SetEase(Ease.InBack).SetUpdate(true)
                .OnComplete(() =>
                {
                    if (trumpChangePopup != null)
                    {
                        trumpChangePopup.transform.localScale = Vector3.one;
                        trumpChangePopup.SetActive(false);
                    }
                });
        }).SetUpdate(true);

        Debug.Log($"[TrumpUI] Popup shown: {trumpChangeText?.text ?? customMessage}");
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
