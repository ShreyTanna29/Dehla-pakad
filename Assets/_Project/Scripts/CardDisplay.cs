// using UnityEngine;
// using UnityEngine.UI;

// public class CardDisplay : MonoBehaviour
// {
//     [Header("Card Information")]
//     public CardData myCardData;

//     [Header("UI Components (Drag Images Here)")]
//     public Image cardBackgroundImage;
//     public Image centerSuitImage; 
//     public Image cornerRankImage; 

//     [Header("uVegas Sprites (Fill these arrays)")]
//     public Sprite[] suitSprites; 
//     public Sprite[] rankSprites; 

//     public void SetCardData(CardData newData)
//     {
//         myCardData = newData;
//         gameObject.name = myCardData.cardRank + " of " + myCardData.cardSuit;

//         // 🚨 Failsafe: Agar Inspector mein images drag karna bhool gaye toh warning dega
//         if (centerSuitImage == null || cornerRankImage == null)
//         {
//             Debug.LogError($"[CardDisplay] {gameObject.name} par Images assign nahi hain! Kripya Card_UI Prefab mein Visuals ke andar se images drag karein.");
//             return; // Aage ka code mat chalao jisse game crash na ho
//         }

//         // 1. Suit set karna
//         int suitIndex = (int)myCardData.cardSuit;
//         if (suitSprites != null && suitSprites.Length > suitIndex && suitSprites[suitIndex] != null)
//         {
//             centerSuitImage.sprite = suitSprites[suitIndex];
//         }

//         // 2. Rank set karna
//         int rankIndex = (int)myCardData.cardRank;
//         if (rankSprites != null && rankSprites.Length > rankIndex && rankSprites[rankIndex] != null)
//         {
//             cornerRankImage.sprite = rankSprites[rankIndex];
            
//             // Color logic
//             if (myCardData.cardSuit == CardSuit.Hearts || myCardData.cardSuit == CardSuit.Diamonds)
//             {
//                 cornerRankImage.color = Color.red; 
//                 centerSuitImage.color = Color.red;
//             }
//             else 
//             {
//                 cornerRankImage.color = Color.black; 
//                 centerSuitImage.color = Color.black;
//             }
//         }
//     }
// }

using UnityEngine;
using UnityEngine.UI;

public class CardDisplay : MonoBehaviour
{
    [Header("Card Information")]
    public CardData myCardData;

    [Header("UI Components (Drag Images Here)")]
    public Image cardBackgroundImage;
    public Image centerSuitImage; 
    public Image cornerRankImage; 

    [Header("uVegas Sprites (Fill these arrays)")]
    public Sprite[] suitSprites; 
    public Sprite[] rankSprites; 

    Sprite _faceBackgroundSprite;

    const float UnlockedVisualHeight = 260f;
    const float LockedVisualHeight = 270f;

    void ApplyVisualsHeight(bool isHidden)
    {
        if (cardBackgroundImage == null) return;

        RectTransform visualsRt = cardBackgroundImage.rectTransform;
        Vector2 size = visualsRt.sizeDelta;
        visualsRt.sizeDelta = new Vector2(size.x, isHidden ? LockedVisualHeight : UnlockedVisualHeight);
    }

    void CacheFaceBackgroundSprite()
    {
        if (_faceBackgroundSprite == null && cardBackgroundImage != null)
            _faceBackgroundSprite = cardBackgroundImage.sprite;
    }

    static Sprite ResolveHiddenBackSprite()
    {
        Sprite styled = CardBackStyle.GetBackSprite();
        if (styled != null) return styled;

        if (TrumpManager.Instance != null && TrumpManager.Instance.hiddenTrumpSprite != null)
            return TrumpManager.Instance.hiddenTrumpSprite;
        if (GameManager.Instance != null && GameManager.Instance.cardBackSprite != null)
            return GameManager.Instance.cardBackSprite;

        // Last resort: keep a solid back so the face never stays visible.
        return null;
    }

    public void SetCardData(CardData newData)
    {
        CacheFaceBackgroundSprite();
        myCardData = newData;
        gameObject.name = myCardData.cardRank + " of " + myCardData.cardSuit;

        if (centerSuitImage == null || cornerRankImage == null)
        {
            Debug.LogError($"[CardDisplay] {gameObject.name} par Images assign nahi hain! Kripya Card_UI Prefab mein Visuals ke andar se images drag karein.");
            return;
        }

        int suitIndex = (int)myCardData.cardSuit;
        if (suitSprites != null && suitSprites.Length > suitIndex && suitSprites[suitIndex] != null)
            centerSuitImage.sprite = suitSprites[suitIndex];

        int rankIndex = (int)myCardData.cardRank;
        if (rankSprites != null && rankSprites.Length > rankIndex && rankSprites[rankIndex] != null)
        {
            cornerRankImage.sprite = rankSprites[rankIndex];

            if (myCardData.cardSuit == CardSuit.Hearts || myCardData.cardSuit == CardSuit.Diamonds)
            {
                cornerRankImage.color = Color.red;
                centerSuitImage.color = Color.red;
            }
            else
            {
                cornerRankImage.color = Color.black;
                centerSuitImage.color = Color.black;
            }
        }

        if (cardBackgroundImage != null)
            cardBackgroundImage.color = Color.white;
    }

    public void ApplyTableCenterVisual()
    {
        if (cardBackgroundImage != null)
            cardBackgroundImage.color = Color.white;

        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = GetComponentInParent<CanvasGroup>();
        if (cg != null)
            cg.alpha = 1f;
    }

    public void SetHiddenState(bool isHidden)
    {
        CacheFaceBackgroundSprite();

        if (isHidden)
        {
            Sprite backSprite = ResolveHiddenBackSprite();
            if (cardBackgroundImage != null)
            {
                if (backSprite != null)
                    cardBackgroundImage.sprite = backSprite;
                cardBackgroundImage.color = Color.white;
            }

            if (cornerRankImage != null) cornerRankImage.gameObject.SetActive(false);
            if (centerSuitImage != null) centerSuitImage.gameObject.SetActive(false);
            ApplyVisualsHeight(true);
        }
        else
        {
            if (cardBackgroundImage != null && _faceBackgroundSprite != null)
            {
                cardBackgroundImage.sprite = _faceBackgroundSprite;
                cardBackgroundImage.color = Color.white;
            }

            if (cornerRankImage != null) cornerRankImage.gameObject.SetActive(true);
            if (centerSuitImage != null) centerSuitImage.gameObject.SetActive(true);

            ApplyVisualsHeight(false);
            SetCardData(myCardData);
        }
    }
}