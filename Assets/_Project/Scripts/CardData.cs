using UnityEngine;

[System.Serializable] 
public struct CardData
{
    public CardSuit cardSuit;
    public CardRank cardRank;
}

// Naam badal kar 'CardSuit' kar diya
public enum CardSuit
{
    Spades, Hearts, Clubs, Diamonds
}

// Naam badal kar 'CardRank' kar diya
public enum CardRank
{
    Two, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King, Ace
}