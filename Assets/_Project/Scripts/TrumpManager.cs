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

    private CardSuit currentTrumpSuit = CardSuit.Spades;
    private bool isTrumpRevealed = true;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (trumpChangePopup != null) trumpChangePopup.SetActive(false);
        
        // Initial Trump is usually Spades unless Mode 2 sets it later
        SetTrumpSuit(CardSuit.Spades, false, true);
    }

    public CardSuit GetCurrentTrumpSuit()
    {
        return currentTrumpSuit;
    }

    public void SetTrumpSuit(CardSuit newSuit, bool showPopup, bool isRevealed = true)
    {
        Debug.Log($"[TrumpManager] Trump Set To: {newSuit} (Revealed: {isRevealed})");
        currentTrumpSuit = newSuit;
        isTrumpRevealed = isRevealed;
        
        PlayerHand.currentTrumpSuit = newSuit; // Sync with PlayerHand static variable
        PlayerHand.isTrumpRevealed = isRevealed;

        UpdateTrumpUI(newSuit);

        if (showPopup)
        {
            Debug.Log($"[TrumpManager] Trump Changed To: {newSuit}");
            ShowTrumpChangePopup(newSuit);
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
            {
                photonView.RPC("RPC_SetTrumpSuit", RpcTarget.All, (int)newSuit, showPopup, isRevealed);
            }
            else
            {
                Debug.LogWarning("[TrumpManager] photonView is null! Falling back to local set.");
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
        if (trumpIcon != null)
        {
            if (!isTrumpRevealed)
            {
                trumpIcon.sprite = hiddenTrumpSprite;
            }
            else
            {
                switch (newTrump)
                {
                    case CardSuit.Spades: trumpIcon.sprite = spadeSprite; break;
                    case CardSuit.Hearts: trumpIcon.sprite = heartSprite; break;
                    case CardSuit.Diamonds: trumpIcon.sprite = diamondSprite; break;
                    case CardSuit.Clubs: trumpIcon.sprite = clubSprite; break;
                }
            }
        }

        if (trumpSuitText != null)
        {
            trumpSuitText.text = !isTrumpRevealed ? "Trump: ?" : "Trump: " + newTrump.ToString();
        }
    }

    void ShowTrumpChangePopup(CardSuit newSuit)
    {
        if (trumpChangePopup == null) return;

        trumpChangePopup.SetActive(true);
        if (trumpChangeText != null)
        {
            trumpChangeText.text = "Trump Changed to " + newSuit.ToString() + "!";
        }

        trumpChangePopup.transform.localScale = Vector3.zero;
        trumpChangePopup.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);

        Sequence seq = DOTween.Sequence();
        seq.AppendInterval(2f);
        seq.Append(trumpChangePopup.transform.DOScale(0f, 0.5f).SetEase(Ease.InBack));
        seq.OnComplete(() => trumpChangePopup.SetActive(false));
    }
}
