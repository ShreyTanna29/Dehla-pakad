namespace uVegas.Core.Cards
{
    /// <summary>
    /// Represents the rank of a playing card.
    /// Numbers are straightforward, face cards are obvious, plus we have special entries.
    /// </summary>
    public enum Rank
    {
        Two = 2,   // The number 2 card
        Three,     // The number 3 card
        Four,      // The number 4 card
        Five,      // The number 5 card
        Six,       // The number 6 card
        Seven,     // The number 7 card
        Eight,     // The number 8 card
        Nine,      // The number 9 card
        Ten,       // The number 10 card
        Jack,      // Face card: Jack
        Queen,     // Face card: Queen
        King,      // Face card: King
        Ace,       // High card: Ace
        None,      // Represents "no rank", handy for empty placeholders
        Joker      // Special card: Joker
    }
}
