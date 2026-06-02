using System.Collections.Generic;
using UnityEngine;

namespace uVegas.Core.Cards
{
    /// <summary>
    /// Represents a single rank entry with its corresponding sprite.
    /// Basically, it says: "This is how the 'Ace', '2', 'King', etc. should look."
    /// </summary>
    [System.Serializable]
    public struct RankEntry
    {
        public Rank rank;    // The rank this entry corresponds to
        public Sprite image; // The sprite to display for this rank
    }

    /// <summary>
    /// Represents a suit entry with its sprite and color.
    /// Think of this as the outfit for hearts, spades, clubs, or diamonds.
    /// </summary>
    [System.Serializable]
    public struct SuitEntry
    {
        public Suit suit;    // The suit this entry corresponds to
        public Sprite image; // The sprite representing the suit
        public Color color;  // The color associated with this suit (red, black, etc.)
    }

    /// <summary>
    /// ScriptableObject that defines the visual theme for all cards.
    /// You can swap themes to instantly change how the deck looks.
    /// </summary>
    [CreateAssetMenu(fileName = "Card Theme", menuName = "uVegas/Cards/Card Theme")]
    public class CardTheme : ScriptableObject
    {
        [Header("Front and Back")]
        public Sprite baseImage; // Base image for the card
        public Sprite backImage; // Image for the back of the card
        public Color frontColor = Color.white; // Color overlay for the front
        public Color backColor = Color.white;  // Color overlay for the back

        [Header("Joker")]
        public Sprite jokerImage; // Special sprite for jokers
        public Color jokerColor = Color.white; // Joker color overlay

        [Header("Rank")]
        public Color rankColor = Color.black; // Default color for ranks (A, K, Q, …)

        [Header("Ranks")]
        public List<RankEntry> ranks = new(); // List of all rank sprites

        [Header("Suits")]
        public List<SuitEntry> suits = new(); // List of all suit sprites

        /// <summary>
        /// Returns the RankEntry for a given rank, if it exists.
        /// Otherwise returns null. Handy for checking visuals on the fly.
        /// </summary>
        /// <param name="rank">The rank you want the sprite for.</param>
        /// <returns>The corresponding RankEntry, or null if not found.</returns>
        public RankEntry? GetRank(Rank rank)
        {
            foreach (var r in ranks)
            {
                if (r.rank == rank)
                    return r;
            }
            return null;
        }

        /// <summary>
        /// Returns the SuitEntry for a given suit, if it exists.
        /// Null if the suit isn't defined in this theme.
        /// </summary>
        /// <param name="suit">The suit you want the sprite for.</param>
        /// <returns>The corresponding SuitEntry, or null if not found.</returns>
        public SuitEntry? GetSuit(Suit suit)
        {
            foreach (var s in suits)
            {
                if (s.suit == suit)
                    return s;
            }
            return null;
        }
    }
}
