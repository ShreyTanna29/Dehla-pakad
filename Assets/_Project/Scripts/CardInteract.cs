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

    private CardDisplay myDisplay;
    private RectTransform visualRect; 
    private float originalY;
    private bool isInitialized = false;

    void Init()
    {
        if (isInitialized) return;
        
        // 🚀 FIX: Root component ko prefer karenge taaki visuals wala dummy component na uthaye
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

        if (visualRect != null)
        {
            originalY = visualRect.anchoredPosition.y;
        }
        isInitialized = true;
    }

    void Start() { Init(); }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"[CardInteract] Clicked on {gameObject.name}. isPlayed: {isPlayed}, canPlayCards: {canPlayCards}, isValidToPlay: {isValidToPlay}");

        if (isPlayed || !canPlayCards || !isValidToPlay) 
        {
            return;
        }

        isPlayed = true;
        isValidToPlay = false;
        canPlayCards = false;

        if (visualRect != null) visualRect.DOKill();

        PlayerHand pHand = PlayerHand.LocalInstance;
        if (pHand != null && myDisplay != null)
        {
            Debug.Log($"[CardInteract] Playing card: {myDisplay.myCardData.cardRank} of {myDisplay.myCardData.cardSuit}");
            pHand.OnLocalPlayerPlayedCard(myDisplay.myCardData, myDisplay.gameObject);
        }
    }

    public void SetCardRuleState(bool valid, bool autoRaise)
    {
        Init(); 

        isValidToPlay = valid;
        if (visualRect != null) visualRect.DOKill();

        Image targetImage = null;
        if (myDisplay != null && myDisplay.cardBackgroundImage != null)
        {
            targetImage = myDisplay.cardBackgroundImage;
        }

        if (targetImage != null)
        {
            // Soft Light-White color valid ke liye aur dark locked ke liye
            targetImage.color = valid ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.5f, 0.5f, 0.5f, 1f);
        }

        if (visualRect != null)
        {
            if (!valid)
            {
                visualRect.DOAnchorPosY(originalY, 0.2f).SetEase(Ease.OutSine);
            }
            else
            {
                if (autoRaise)
                    visualRect.DOAnchorPosY(originalY + 25f, 0.3f).SetEase(Ease.OutBack); // Halka sa premium pop-up
                else
                    visualRect.DOAnchorPosY(originalY, 0.2f).SetEase(Ease.OutSine);
            }
        }
    }

    void OnDestroy()
    {
        if (visualRect != null) visualRect.DOKill();
    }
}