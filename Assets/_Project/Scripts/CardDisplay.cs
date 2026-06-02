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

    public void SetCardData(CardData newData)
    {
        myCardData = newData;
        gameObject.name = myCardData.cardRank + " of " + myCardData.cardSuit;

        // 🚨 Failsafe: Agar Inspector mein images drag karna bhool gaye toh warning dega
        if (centerSuitImage == null || cornerRankImage == null)
        {
            Debug.LogError($"[CardDisplay] {gameObject.name} par Images assign nahi hain! Kripya Card_UI Prefab mein Visuals ke andar se images drag karein.");
            return; // Aage ka code mat chalao jisse game crash na ho
        }

        // 1. Suit set karna
        int suitIndex = (int)myCardData.cardSuit;
        if (suitSprites != null && suitSprites.Length > suitIndex && suitSprites[suitIndex] != null)
        {
            centerSuitImage.sprite = suitSprites[suitIndex];
        }

        // 2. Rank set karna
        int rankIndex = (int)myCardData.cardRank;
        if (rankSprites != null && rankSprites.Length > rankIndex && rankSprites[rankIndex] != null)
        {
            cornerRankImage.sprite = rankSprites[rankIndex];
            
            // Color logic
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
    }
}