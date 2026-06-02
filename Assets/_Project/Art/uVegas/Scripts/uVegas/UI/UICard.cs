using UnityEngine;
using UnityEngine.UI;
using uVegas.Core.Cards;

namespace uVegas.UI
{
    /// <summary>
    /// Handles the visual representation of a playing card in the UI.
    /// Think of this as the card's wardrobe: it dresses the card based on its type and theme.
    /// </summary>
    public class UICard : MonoBehaviour
    {
        [SerializeField] private Image baseImage;  // The card's background image
        [SerializeField] private Image rankImage;  // The image representing the card's rank (A, 2, 3, …)
        [SerializeField] private Image suitImage;  // The image representing the card's suit (hearts, spades, etc.)
        [SerializeField] private Card currentCard; // The card currently displayed

        private CardTheme currentTheme; // The current theme/style applied to this card

        /// <summary>
        /// Initialize the UI card with a card data and a theme.
        /// This sets up the visuals according to the card type and chosen theme.
        /// </summary>
        /// <param name="card">The card data to display.</param>
        /// <param name="theme">The visual theme to apply.</param>
        public void Init(Card card, CardTheme theme)
        {
            currentCard = card;
            currentTheme = theme;

            UpdateTheme(); // Apply the visuals immediately
        }

        /// <summary>
        /// Updates the card's visuals based on the current card and theme.
        /// Handles hidden cards, jokers, and normal playing cards.
        /// </summary>
        private void UpdateTheme()
        {
            if (currentCard == null) return;

            // Handle hidden cards (e.g., face down)
            if (currentCard.suit == Suit.Hidden)
            {
                suitImage.gameObject.SetActive(false);

                baseImage.sprite = currentTheme.baseImage;
                rankImage.sprite = currentTheme.backImage;

                baseImage.color = currentTheme.frontColor;
                rankImage.color = currentTheme.backColor;

                return;
            }

            // Handle jokers (special cards)
            if (currentCard.suit == Suit.Joker)
            {
                suitImage.gameObject.SetActive(false);

                baseImage.sprite = currentTheme.baseImage;
                baseImage.color = currentTheme.frontColor;

                rankImage.sprite = currentTheme.jokerImage;
                rankImage.color = currentTheme.jokerColor;

                return;
            }

            // Normal cards (hearts, spades, etc.)
            baseImage.sprite = currentTheme.baseImage;
            baseImage.color = currentTheme.frontColor;

            RankEntry? rankEntry = currentTheme.GetRank(currentCard.rank);
            SuitEntry? suitEntry = currentTheme.GetSuit(currentCard.suit);

            if (rankEntry.HasValue)
            {
                RankEntry rank = rankEntry.Value;
                rankImage.sprite = rank.image;
                rankImage.color = currentTheme.rankColor; // Standard color for ranks
            }

            if (suitEntry.HasValue)
            {
                SuitEntry suit = suitEntry.Value;
                suitImage.sprite = suit.image;
                suitImage.color = suit.color; // Each suit can have its own color
            }
        }

        /// <summary>
        /// Reveal a hidden card by initializing it with new card data.
        /// If the card is already visible, this does nothing.
        /// </summary>
        /// <param name="card">The card to reveal.</param>
        public void Reveal(Card card)
        {
            if (currentCard.suit == Suit.Hidden)
            {
                Init(card, currentTheme);
            }
        }
    }
}
