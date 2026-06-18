using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

public class CardInteract : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public bool isPlayed = false;
    public bool isValidToPlay = false;
    public static bool canPlayCards = false;
    public static bool isPlayingCard = false;

    private static CardInteract currentSelected;

    private bool isSelected = false;
    private bool isDragging = false;
    private bool isAutoRaised = false;
    private CardDisplay myDisplay;
    private RectTransform visualRect;
    private HorizontalLayoutGroup layoutGroup;
    private bool _layoutWasEnabledBeforeDrag;
    private CanvasGroup cardCanvasGroup;
    private Image blockedOverlay;
    private Text lockIndicator;
    private Color defaultBgColor = Color.white;
    private Vector2 originalPos;
    private bool isInitialized = false;
    private bool blockedVisualsCreated = false;

    private Vector2 dragStartPointerLocalPos;
    private Vector2 dragStartCardAnchoredPos;
    private const float SwipePlayThreshold = 100f;
    private const float MaxSwipeUpLimit = 150f;

    const float DimmedBrightness = 0.47f;
    const float PlayableBrightness = 1f;

    void Init()
    {
        if (isInitialized) return;

        myDisplay = GetComponent<CardDisplay>() ?? GetComponentInParent<CardDisplay>();

        // Raise/hover animations must move the inner Visuals rect only — not the hand slot root.
        visualRect = GetComponent<RectTransform>();
        if (visualRect == null && myDisplay != null)
            visualRect = myDisplay.GetComponent<RectTransform>();

        if (myDisplay != null)
        {
            if (myDisplay.cardBackgroundImage != null)
                myDisplay.cardBackgroundImage.raycastTarget = true;
        }

        if (visualRect == null) visualRect = GetComponent<RectTransform>();

        layoutGroup = GetComponentInParent<HorizontalLayoutGroup>();

        cardCanvasGroup = GetComponent<CanvasGroup>();
        if (cardCanvasGroup == null)
            cardCanvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (myDisplay != null && myDisplay.cardBackgroundImage != null)
            defaultBgColor = myDisplay.cardBackgroundImage.color;

        if (visualRect != null)
            originalPos = visualRect.anchoredPosition;

        EnsureBlockedVisuals();
        isInitialized = true;
    }

    static bool UsesHandLayoutGroup()
    {
        return PlayerHand.LocalInstance == null || PlayerHand.LocalInstance.myCards.Count <= HandLayoutHelper.CardsPerRow;
    }

    Vector2 GetRaisedPos(float yOffset) => originalPos + new Vector2(0f, yOffset);

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
            {
                Color bgCol = defaultBgColor * brightness;
                bgCol.a = 1f; // Force full opacity so that dimmed/non-playable cards are not see-through (transparent)
                myDisplay.cardBackgroundImage.color = bgCol;
            }

            Color iconColor = GetSuitIconColor(myDisplay.myCardData.cardSuit, brightness);
            if (myDisplay.cornerRankImage != null)
                myDisplay.cornerRankImage.color = iconColor;
            if (myDisplay.centerSuitImage != null)
                myDisplay.centerSuitImage.color = iconColor;
        }

        if (visualRect != null && !isSelected && !isDragging)
        {
            if (raisePlayable)
                visualRect.DOAnchorPos(GetRaisedPos(30f), 0.28f).SetEase(Ease.OutBack);
            else
                visualRect.DOAnchorPos(originalPos, 0.18f).SetEase(Ease.OutSine);
        }
    }

    public void ResetVisualOffset()
    {
        Init();
        isAutoRaised = false;
        isSelected = false;
        if (visualRect != null)
        {
            visualRect.DOKill();
            visualRect.anchoredPosition = originalPos;
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
        ApplyBrightnessVisual(1f, true, raise);
        isValidToPlay = true;
    }

    public void ApplyBlockedOnTurnVisual()
    {
        isValidToPlay = false;
        if (isSelected) DeselectThisCard();
        ResetVisualOffset();
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

    public void ResetForPool()
    {
        isPlayed = false;
        isValidToPlay = false;
        isSelected = false;
        isDragging = false;
        isAutoRaised = false;
        if (visualRect != null)
        {
            visualRect.DOKill();
            visualRect.localScale = Vector3.one;
            visualRect.localRotation = Quaternion.identity;
        }
        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.alpha = 1f;
            cardCanvasGroup.interactable = true;
            cardCanvasGroup.blocksRaycasts = true;
        }
        if (blockedOverlay != null) blockedOverlay.enabled = false;
        if (lockIndicator != null) lockIndicator.enabled = false;
        
        if (myDisplay != null && myDisplay.cardBackgroundImage != null)
            myDisplay.cardBackgroundImage.color = defaultBgColor;
            
        if (currentSelected == this) currentSelected = null;
    }

    void EnsureBlockedVisuals()
    {
        if (blockedVisualsCreated) return;
        
        // Try to find existing ones first (if they were already part of the prefab)
        Transform overlayT = transform.Find("BlockedOverlay");
        if (overlayT != null)
        {
            blockedOverlay = overlayT.GetComponent<Image>();
        }
        else
        {
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
        }
        blockedOverlay.enabled = false;

        Transform lockT = transform.Find("LockIndicator");
        if (lockT != null)
        {
            lockIndicator = lockT.GetComponent<Text>();
        }
        else
        {
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
        }
        lockIndicator.enabled = false;
        
        blockedVisualsCreated = true;
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

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isPlayed || !canPlayCards || !isValidToPlay || isPlayingCard || PlayerHand.IsGameplayInputBlocked)
            return;

        Init();
        if (visualRect == null) return;

        isDragging = true;
        visualRect.DOKill();
        transform.SetAsLastSibling();

        if (layoutGroup != null)
        {
            _layoutWasEnabledBeforeDrag = layoutGroup.enabled;
            layoutGroup.enabled = false;
        }

        originalPos = visualRect.anchoredPosition;
        dragStartCardAnchoredPos = visualRect.anchoredPosition;
        RectTransform parentRect = (RectTransform)visualRect.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out dragStartPointerLocalPos);

        if (currentSelected != null && currentSelected != this)
            currentSelected.DeselectThisCard();

        currentSelected = this;
        isSelected = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || visualRect == null) return;

        RectTransform parentRect = (RectTransform)visualRect.parent;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out Vector2 currentPointerLocalPos))
        {
            float dragDeltaY = currentPointerLocalPos.y - dragStartPointerLocalPos.y;
            float newY = Mathf.Clamp(dragStartCardAnchoredPos.y + dragDeltaY, originalPos.y, originalPos.y + MaxSwipeUpLimit);
            visualRect.anchoredPosition = new Vector2(dragStartCardAnchoredPos.x, newY);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        isDragging = false;

        if (visualRect != null && visualRect.anchoredPosition.y >= originalPos.y + SwipePlayThreshold)
            PlayThisCard();
        else
            RestoreAfterSwipeCancel();
    }

    void RestoreAfterSwipeCancel()
    {
        if (layoutGroup != null && UsesHandLayoutGroup())
            layoutGroup.enabled = _layoutWasEnabledBeforeDrag;

        isSelected = false;
        if (currentSelected == this) currentSelected = null;

        if (visualRect == null) return;

        visualRect.DOKill();
        Vector2 targetPos = (isAutoRaised && isValidToPlay) ? GetRaisedPos(28f) : originalPos;
        visualRect.DOAnchorPos(targetPos, 0.2f).SetEase(Ease.OutSine);
    }

    public void DeselectThisCard()
    {
        isSelected = false;
        if (currentSelected == this) currentSelected = null;

        if (visualRect != null)
        {
            visualRect.DOKill();
            Vector2 targetPos = (isAutoRaised && isValidToPlay) ? GetRaisedPos(28f) : originalPos;
            visualRect.DOAnchorPos(targetPos, 0.2f).SetEase(Ease.OutSine);
        }
    }

    private void PlayThisCard()
    {
        Init();

        isPlayingCard = true;
        isPlayed = true;

        CardDisplay display = myDisplay != null ? myDisplay : GetComponent<CardDisplay>();
        if (display != null)
            PlayerHand.LocalInstance?.OnLocalPlayerPlayedCard(display.myCardData, gameObject);

        currentSelected = null;
        isSelected = false;
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
