namespace uVegas.Core.Cards
{
    /// <summary>
    /// Represents the suit of a playing card.
    /// Includes the usual suspects plus a hidden and joker option for special cases.
    /// </summary>
    public enum Suit
    {
        Hearts,   // Classic red heart
        Diamonds, // Classic red diamond
        Clubs,    // Classic black club
        Spades,   // Classic black spade
        Hidden,   // Used when the card is face down
        Joker     // Special card suit: Joker
    }
}
