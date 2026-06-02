using UnityEngine;
using UnityEngine.EventSystems;

namespace uVegas.Demo
{
    /// <summary>
    /// Adds a simple hover animation and sound effect to a card UI element.
    /// Basically: makes your cards feel *alive* when the player hovers over them.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(AudioSource))]
    public class CardHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Hover Settings")]
        public Vector3 hoverScale = new Vector3(1.2f, 1.2f, 1f); // How much the card scales up when hovered
        public Vector3 hoverRotation = new Vector3(0f, 0f, 5f);  // Slight tilt for that "hover magic"
        public float lerpSpeed = 5f;                             // Smoothness of the animation

        private Vector3 originalScale;       // Scale before hover
        private Quaternion originalRotation; // Rotation before hover

        private Vector3 targetScale;         // Scale we're currently lerping toward
        private Quaternion targetRotation;   // Rotation we're currently lerping toward

        private AudioSource audioSource;     // Audio source used to play hover sound
        private AudioClip audioClip;         // Sound effect to play on hover

        /// <summary>
        /// Caches the card’s original transform values so we can restore them later.
        /// </summary>
        private void Awake()
        {
            originalScale = transform.localScale;
            originalRotation = transform.localRotation;

            targetScale = originalScale;
            targetRotation = originalRotation;
        }

        /// <summary>
        /// Initializes the hover effect with the given audio source and clip.
        /// Usually called right after the card prefab is created.
        /// </summary>
        /// <param name="audioSource">Shared audio source to play the sound from.</param>
        /// <param name="audioClip">The sound effect to play when hovering.</param>
        public void Init(AudioSource audioSource, AudioClip audioClip)
        {
            this.audioSource = audioSource;
            this.audioClip = audioClip;
        }

        /// <summary>
        /// Smoothly interpolates the card’s scale and rotation toward the current target values.
        /// </summary>
        private void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * lerpSpeed);
            transform.localRotation = Quaternion.Lerp(transform.localRotation, targetRotation, Time.deltaTime * lerpSpeed);
        }

        /// <summary>
        /// Triggered when the pointer enters the card area.
        /// Scales and rotates the card slightly, then plays a satisfying hover sound.
        /// </summary>
        /// <param name="eventData">Pointer event data (ignored here, but required by interface).</param>
        public void OnPointerEnter(PointerEventData eventData)
        {
            targetScale = hoverScale;
            targetRotation = Quaternion.Euler(hoverRotation);
            audioSource.PlayOneShot(audioClip);
        }

        /// <summary>
        /// Triggered when the pointer leaves the card area.
        /// Gently resets the card to its original position and scale.
        /// </summary>
        /// <param name="eventData">Pointer event data (ignored here, but required by interface).</param>
        public void OnPointerExit(PointerEventData eventData)
        {
            targetScale = originalScale;
            targetRotation = originalRotation;
        }
    }
}
