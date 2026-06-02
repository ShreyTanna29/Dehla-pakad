using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using uVegas.Core.Cards;
using uVegas.UI;

namespace uVegas.Demo
{
    /// <summary>
    /// Manages card themes, deck generation, hand drawing, and UI interactions in the demo.
    /// Basically the "deck wizard" of our scene, handling visuals, sounds, and fun.
    /// </summary>
    public class CardThemeManager : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private GameObject cardPrefab;      // Prefab for the card UI element
        [SerializeField] private GameObject cardPanel;       // Panel where cards are displayed
        [SerializeField] private TMP_Dropdown themeDropdown; // Dropdown to select themes
        [SerializeField] private TMP_Dropdown sizeDropdown;  // Dropdown to select card sizes
        [SerializeField] private Button reloadButton;        // Reload button to reshuffle cards

        [Header("Selectable Themes")]
        [SerializeField] private List<CardTheme> themes;     // Available themes for cards

        [Header("Testing Sizes")]
        [SerializeField] private List<string> sizes;         // Available card sizes

        [Header("Sound and Music")]
        [SerializeField] private AudioClip clickSound;
        [SerializeField] private AudioClip selectSound;
        [SerializeField] private AudioClip hoverSound;
        [SerializeField] private AudioClip winSound;

        [SerializeField] private AudioSource sfxAudioSource;
        [SerializeField] private AudioSource musicAudioSource;

        private int cardCount = 4;            // How many cards to display in hand
        private CardTheme selectedTheme;      // Currently selected card theme
        private string selectedSize;          // Currently selected card size

        private List<Card> deck = new(); // The current deck
        private List<Card> hand = new(); // The current hand

        /// <summary>
        /// Unity Start method. Initializes dropdowns, loads first theme, and starts music.
        /// </summary>
        private void Start()
        {
            selectedTheme = themes[0];
            selectedSize = sizes[0];

            FillThemeDropdown();
            FillSizeDropdown();
            ReloadTheme();

            reloadButton.onClick.RemoveAllListeners();
            reloadButton.onClick.AddListener(OnReloadButtonClicked);

            musicAudioSource.Play();
        }

        /// <summary>
        /// Clears old cards and generates a new hand with the current theme and size.
        /// </summary>
        private void ReloadTheme()
        {
            ClearChilds();
            GenerateDeck();
            hand.Clear();

            string[] sizeParts = selectedSize.Split('x');
            float x = float.Parse(sizeParts[0]);
            float y = float.Parse(sizeParts[1]);
            cardPanel.GetComponent<GridLayoutGroup>().cellSize = new Vector2(x, y);

            for (int i = 0; i < cardCount; i++)
            {
                int index = Random.Range(0, deck.Count);
                Card card = deck[index];
                deck.RemoveAt(index);
                hand.Add(card);

                GameObject go = Instantiate(cardPrefab);
                go.transform.SetParent(cardPanel.transform, false);
                go.GetComponent<UICard>().Init(card, selectedTheme);
                go.AddComponent<CardHoverEffect>().Init(sfxAudioSource, hoverSound);
            }

            // Evaluate the hand for fun
            EvaluateHand(hand);
        }

        /// <summary>
        /// Populates the theme dropdown with all available themes.
        /// </summary>
        private void FillThemeDropdown()
        {
            themeDropdown.ClearOptions();
            List<string> options = new();
            foreach (var theme in themes)
            {
                options.Add(theme.name);
            }

            themeDropdown.AddOptions(options);
            themeDropdown.onValueChanged.AddListener(OnThemeDropdownChanged);
        }

        /// <summary>
        /// Populates the size dropdown with all available sizes.
        /// </summary>
        private void FillSizeDropdown()
        {
            sizeDropdown.ClearOptions();
            List<string> options = new();
            foreach (var size in sizes)
            {
                options.Add(size);
            }

            sizeDropdown.AddOptions(options);
            sizeDropdown.onValueChanged.AddListener(OnSizeDropdownChanged);
        }

        private void OnThemeDropdownChanged(int index)
        {
            sfxAudioSource.PlayOneShot(selectSound);
            selectedTheme = themes[index];
        }

        private void OnSizeDropdownChanged(int index)
        {
            sfxAudioSource.PlayOneShot(selectSound);
            selectedSize = sizes[index];
        }

        private void OnReloadButtonClicked()
        {
            sfxAudioSource.PlayOneShot(clickSound);
            ReloadTheme();
        }

        /// <summary>
        /// Clears all cards from the UI panel.
        /// </summary>
        private void ClearChilds()
        {
            foreach (Transform child in cardPanel.transform)
                Destroy(child.gameObject);
        }

        /// <summary>
        /// Opens a URL when called, with a satisfying click sound.
        /// </summary>
        /// <param name="uri">The URL to visit.</param>
        public void VisitLink(string uri)
        {
            sfxAudioSource.PlayOneShot(clickSound);
            Application.OpenURL(uri);
        }

        /// <summary>
        /// Generates a full deck including special hidden and joker cards.
        /// </summary>
        private void GenerateDeck()
        {
            deck.Clear();

            foreach (Suit s in System.Enum.GetValues(typeof(Suit)))
            {
                if (s == Suit.Hidden || s == Suit.Joker)
                    continue;

                foreach (Rank r in System.Enum.GetValues(typeof(Rank)))
                {
                    if (r == Rank.None || r == Rank.Joker)
                        continue;

                    deck.Add(new Card(s, r));
                }
            }

            deck.Add(new Card(Suit.Hidden, Rank.None));
            deck.Add(new Card(Suit.Joker, Rank.Joker));
        }

        /// <summary>
        /// Evaluates the current hand for pairs, three/four of a kind, and straights.
        /// If you hit something, a satisfying win sound plays.
        /// Easteregg: No cheating, just fun detection for your randomly drawn cards! ;-)
        /// </summary>
        /// <param name="hand">The current hand of cards.</param>
        private void EvaluateHand(List<Card> hand)
        {
            Dictionary<Rank, int> rankCounts = new Dictionary<Rank, int>();
            List<int> rankValues = new();

            foreach (var card in hand)
            {
                if (card.rank == Rank.None || card.rank == Rank.Joker)
                    continue;

                if (!rankCounts.ContainsKey(card.rank))
                    rankCounts[card.rank] = 0;
                rankCounts[card.rank]++;

                rankValues.Add((int)card.rank);
            }

            int pairs = 0;
            int threeOfAKind = 0;
            int fourOfAKind = 0;

            foreach (var count in rankCounts.Values)
            {
                if (count == 2) pairs++;
                if (count == 3) threeOfAKind++;
                if (count == 4) fourOfAKind++;
            }

            bool isStraight = false;
            if (rankValues.Count == 4)
            {
                rankValues.Sort();
                HashSet<int> uniqueRanks = new HashSet<int>(rankValues);
                if (uniqueRanks.Count == 4 && rankValues[3] - rankValues[0] == 3)
                    isStraight = true;
            }

            // If we find a hand pattern, celebrate!
            if (fourOfAKind > 0 || threeOfAKind > 0 || pairs == 2 || pairs == 1 || isStraight)
            {
                sfxAudioSource.PlayOneShot(winSound);
            }
        }
    }
}
