// using UnityEngine;
// using UnityEngine.EventSystems;
// using UnityEngine.UI;
// using DG.Tweening;

// public class CardInteract : MonoBehaviour, IPointerClickHandler
// {
//     public bool isPlayed = false;
//     public bool isValidToPlay = false;
//     public static bool canPlayCards = false;

//     private CardDisplay myDisplay;
//     private RectTransform visualRect; 
//     private float originalY;
//     private bool isInitialized = false;

//     void Init()
//     {
//         if (isInitialized) return;
        
//         // 🚀 FIX: Root component ko prefer karenge taaki visuals wala dummy component na uthaye
//         myDisplay = GetComponent<CardDisplay>();
//         if (myDisplay == null) myDisplay = GetComponentInParent<CardDisplay>();
        
//         if (myDisplay != null)
//         {
//             if (myDisplay.cardBackgroundImage != null)
//             {
//                 visualRect = myDisplay.cardBackgroundImage.GetComponent<RectTransform>();
//                 myDisplay.cardBackgroundImage.raycastTarget = true; 
//             }
//             else
//             {
//                 visualRect = myDisplay.GetComponent<RectTransform>();
//             }
//         }
        
//         if (visualRect == null) visualRect = GetComponent<RectTransform>();

//         if (visualRect != null)
//         {
//             originalY = visualRect.anchoredPosition.y;
//         }
//         isInitialized = true;
//     }

//     void Start() { Init(); }

//     public void OnPointerClick(PointerEventData eventData)
//     {
//         Debug.Log($"[CardInteract] Clicked on {gameObject.name}. isPlayed: {isPlayed}, canPlayCards: {canPlayCards}, isValidToPlay: {isValidToPlay}");

//         if (isPlayed || !canPlayCards || !isValidToPlay) 
//         {
//             return;
//         }

//         isPlayed = true;
//         isValidToPlay = false;
//         canPlayCards = false;

//         if (visualRect != null) visualRect.DOKill();

//         PlayerHand pHand = PlayerHand.LocalInstance;
//         if (pHand != null && myDisplay != null)
//         {
//             Debug.Log($"[CardInteract] Playing card: {myDisplay.myCardData.cardRank} of {myDisplay.myCardData.cardSuit}");
//             pHand.OnLocalPlayerPlayedCard(myDisplay.myCardData, myDisplay.gameObject);
//         }
//     }

//     public void SetCardRuleState(bool valid, bool autoRaise)
//     {
//         Init(); 

//         isValidToPlay = valid;
//         if (visualRect != null) visualRect.DOKill();

//         Image targetImage = null;
//         if (myDisplay != null && myDisplay.cardBackgroundImage != null)
//         {
//             targetImage = myDisplay.cardBackgroundImage;
//         }

//         if (targetImage != null)
//         {
//             // Soft Light-White color valid ke liye aur dark locked ke liye
//             targetImage.color = valid ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.5f, 0.5f, 0.5f, 1f);
//         }

//         if (visualRect != null)
//         {
//             if (!valid)
//             {
//                 visualRect.DOAnchorPosY(originalY, 0.2f).SetEase(Ease.OutSine);
//             }
//             else
//             {
//                 if (autoRaise)
//                     visualRect.DOAnchorPosY(originalY + 25f, 0.3f).SetEase(Ease.OutBack); // Halka sa premium pop-up
//                 else
//                     visualRect.DOAnchorPosY(originalY, 0.2f).SetEase(Ease.OutSine);
//             }
//         }
//     }

//     void OnDestroy()
//     {
//         if (visualRect != null) visualRect.DOKill();
//     }
// }

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

public class CardInteract : MonoBehaviour, IPointerClickHandler
{
    public bool isPlayed = false;
    public bool isValidToPlay = false;
    public static bool canPlayCards = false;
    public static bool isPlayingCard = false; // Anti-spam lock

    private static CardInteract currentSelected; // Keep track of the currently selected card globally

    private bool isSelected = false;
    private bool isAutoRaised = false;
    private CardDisplay myDisplay;
    private RectTransform visualRect;
    private CanvasGroup cardCanvasGroup;
    private Image blockedOverlay;
    private Text lockIndicator;
    private Color defaultBgColor = Color.white;
    private float originalY;
    private bool isInitialized = false;
    private bool blockedVisualsCreated = false;

    const float DimmedBrightness = 0.47f;
    const float PlayableBrightness = 1f;

    void Init()
    {
        if (isInitialized) return;
        
        myDisplay = GetComponent<CardDisplay>();
        if (myDisplay == null) myDisplay = GetComponentInParent<CardDisplay>();
        
        if (myDisplay != null)
        {
            if (myDisplay.cardBackgroundImage != null)
            {
                visualRect = myDisplay.cardBackgroundImage.GetComponent<RectTransform>();
                myDisplay.cardBackgroundImage.raycastTarget = true; 
            }
            else
            {
                visualRect = myDisplay.GetComponent<RectTransform>();
            }
        }
        
        if (visualRect == null) visualRect = GetComponent<RectTransform>();

        cardCanvasGroup = GetComponent<CanvasGroup>();
        if (cardCanvasGroup == null)
            cardCanvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (myDisplay != null && myDisplay.cardBackgroundImage != null)
            defaultBgColor = myDisplay.cardBackgroundImage.color;

        if (visualRect != null)
            originalY = visualRect.anchoredPosition.y;

        EnsureBlockedVisuals();
        isInitialized = true;
    }

    static Color GetSuitIconColor(CardSuit suit, float brightness)
    {
        brightness = Mathf.Clamp01(brightness);
        if (suit == CardSuit.Hearts || suit == CardSuit.Diamonds)
            return new Color(1f * brightness, 0f, 0f, 1f);
        float b = 0.12f * brightness;
        return new Color(b, b, b, 1f);
    }

    static Color GetCardBackgroundColor(float brightness)
    {
        brightness = Mathf.Clamp01(brightness);
        return new Color(brightness, brightness, brightness, 1f);
    }

    void ApplyBrightnessVisual(float brightness, bool interactable, bool raisePlayable)
    {
        Init();
        isAutoRaised = raisePlayable;

        if (visualRect != null)
            visualRect.DOKill();

        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.alpha = 1f;
            cardCanvasGroup.interactable = interactable;
            cardCanvasGroup.blocksRaycasts = interactable;
        }

        if (blockedOverlay != null) blockedOverlay.enabled = false;
        if (lockIndicator != null) lockIndicator.enabled = false;

        SetRaycastTargets(interactable);

        if (myDisplay != null)
        {
            if (myDisplay.cardBackgroundImage != null)
                myDisplay.cardBackgroundImage.color = GetCardBackgroundColor(brightness);

            Color iconColor = GetSuitIconColor(myDisplay.myCardData.cardSuit, brightness);
            if (myDisplay.cornerRankImage != null)
                myDisplay.cornerRankImage.color = iconColor;
            if (myDisplay.centerSuitImage != null)
                myDisplay.centerSuitImage.color = iconColor;
        }

        if (visualRect != null && !isSelected)
        {
            if (raisePlayable)
                visualRect.DOAnchorPosY(originalY + 30f, 0.28f).SetEase(Ease.OutBack);
            else
                visualRect.DOAnchorPosY(originalY, 0.18f).SetEase(Ease.OutSine);
        }
    }

    public void ApplyNotMyTurnVisual()
    {
        isValidToPlay = false;
        if (isSelected) DeselectThisCard();
        ApplyBrightnessVisual(DimmedBrightness, false, false);
    }

    public void ApplyPlayableVisual(bool raise)
    {
        ApplyBrightnessVisual(PlayableBrightness, true, raise);
    }

    public void ApplyBlockedOnTurnVisual()
    {
        // Non-playable cards keep their pre-turn dimmed look — no extra darkening or animation.
        isValidToPlay = false;
    }

    public static void ClearGlobalSelection()
    {
        if (currentSelected != null)
            currentSelected.DeselectThisCard();
        currentSelected = null;
    }

    public void ResetCardVisuals()
    {
        isSelected = false;
        if (currentSelected == this) currentSelected = null;
        ApplyNotMyTurnVisual();
    }

    public void ResetNeutralVisual() => ApplyNotMyTurnVisual();

    void EnsureBlockedVisuals()
    {
        if (blockedVisualsCreated) return;
        blockedVisualsCreated = true;

        GameObject overlayGo = new GameObject("BlockedOverlay", typeof(RectTransform), typeof(Image));
        overlayGo.transform.SetParent(transform, false);
        RectTransform overlayRt = overlayGo.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        overlayRt.SetAsLastSibling();

        blockedOverlay = overlayGo.GetComponent<Image>();
        blockedOverlay.color = new Color(0f, 0f, 0f, 0.08f);
        blockedOverlay.raycastTarget = false;
        blockedOverlay.enabled = false;

        GameObject lockGo = new GameObject("LockIndicator", typeof(RectTransform), typeof(Text));
        lockGo.transform.SetParent(transform, false);
        RectTransform lockRt = lockGo.GetComponent<RectTransform>();
        lockRt.anchorMin = new Vector2(1f, 1f);
        lockRt.anchorMax = new Vector2(1f, 1f);
        lockRt.pivot = new Vector2(1f, 1f);
        lockRt.anchoredPosition = new Vector2(-6f, -6f);
        lockRt.sizeDelta = new Vector2(22f, 22f);

        lockIndicator = lockGo.GetComponent<Text>();
        lockIndicator.text = "\u00D7";
        lockIndicator.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lockIndicator.fontSize = 18;
        lockIndicator.fontStyle = FontStyle.Bold;
        lockIndicator.alignment = TextAnchor.MiddleCenter;
        lockIndicator.color = new Color(0.35f, 0.35f, 0.38f, 0.9f);
        lockIndicator.raycastTarget = false;
        lockIndicator.enabled = false;
    }

    void SetRaycastTargets(bool enabled)
    {
        if (myDisplay == null) return;
        if (myDisplay.cardBackgroundImage != null)
            myDisplay.cardBackgroundImage.raycastTarget = enabled;
        if (myDisplay.cornerRankImage != null)
            myDisplay.cornerRankImage.raycastTarget = enabled;
        if (myDisplay.centerSuitImage != null)
            myDisplay.centerSuitImage.raycastTarget = enabled;
    }

    void Start() { Init(); }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Input Locking & Turn Validation
        if (isPlayed || !canPlayCards || !isValidToPlay || isPlayingCard || PlayerHand.IsGameplayInputBlocked
            || (DeckManager.Instance != null && !DeckManager.Instance.IsDealingComplete)
            || !GameStabilityAudit.CanAcceptPlayerInput())
        {
            if (!isPlayingCard)
                Debug.Log($"[CardInteract] Input Ignored. Played: {isPlayed}, CanPlay: {canPlayCards}, Valid: {isValidToPlay}, PlayingAnim: {isPlayingCard}");
            return;
        }

        if (isSelected)
        {
            PlayThisCard();
        }
        else
        {
            SelectThisCard();
        }
    }

    private void SelectThisCard()
    {
        // Deselect previous
        if (currentSelected != null && currentSelected != this)
        {
            currentSelected.DeselectThisCard();
        }

        currentSelected = this;
        isSelected = true;

        if (visualRect != null)
        {
            visualRect.DOKill();
            visualRect.DOAnchorPosY(originalY + 50f, 0.25f).SetEase(Ease.OutBack);
        }

        Debug.Log($"[CardInteract] Card Selected: {myDisplay.myCardData.cardRank} of {myDisplay.myCardData.cardSuit}");
    }

    public void DeselectThisCard()
    {
        isSelected = false;
        if (currentSelected == this) currentSelected = null;

        if (visualRect != null)
        {
            visualRect.DOKill();
            float targetY = (isAutoRaised && isValidToPlay) ? originalY + 28f : originalY;
            visualRect.DOAnchorPosY(targetY, 0.2f).SetEase(Ease.OutSine);
        }
    }

    private void PlayThisCard()
    {
        Debug.Log($"[CardInteract] PlayThisCard triggered for {myDisplay.myCardData.cardRank} of {myDisplay.myCardData.cardSuit}");

        isPlayingCard = true; // Lock global input
        isPlayed = true;
        isValidToPlay = false;
        canPlayCards = false;

        if (visualRect != null) visualRect.DOKill();

        PlayerHand pHand = PlayerHand.LocalInstance;
        if (pHand != null && myDisplay != null)
        {
            pHand.OnLocalPlayerPlayedCard(myDisplay.myCardData, myDisplay.gameObject);
        }

        currentSelected = null;
    }

    public void SetCardRuleState(bool valid, bool autoRaise)
    {
        isValidToPlay = valid;
        if (valid)
            ApplyPlayableVisual(autoRaise);
    }

    void OnDestroy()
    {
        if (visualRect != null) visualRect.DOKill();
        if (currentSelected == this) currentSelected = null;
    }
}