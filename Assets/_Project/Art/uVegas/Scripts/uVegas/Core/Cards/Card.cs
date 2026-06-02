using System;

namespace uVegas.Core.Cards
{
    /// <summary>
    /// Represents a single playing card with a suit and a rank.
    /// Basically the core data behind what you see on screen.
    /// </summary>
    [Serializable]
    public class Card
    {
        public Suit suit; // The card's suit (Hearts, Spades, etc.)
        public Rank rank; // The card's rank (Ace, 2, King, etc.)

        /// <summary>
        /// Creates a new card with the given suit and rank.
        /// </summary>
        /// <param name="suit">The suit of the card.</param>
        /// <param name="rank">The rank of the card.</param>
        public Card(Suit suit, Rank rank)
        {
            this.suit = suit;
            this.rank = rank;
        }

        /// <summary>
        /// Updates this card's data to match another card.
        /// Handy if you want to "flip" or replace a card without creating a new instance.
        /// </summary>
        /// <param name="card">The card to copy data from.</param>
        public void Update(Card card)
        {
            suit = card.suit;
            rank = card.rank;
        }

        /// <summary>
        /// Returns a friendly string representation of the card, e.g., "Ace of Spades".
        /// Great for debugging or logging.
        /// </summary>
        /// <returns>A string describing the card.</returns>
        public override string ToString() => $"{rank} of {suit}";
    }
}
