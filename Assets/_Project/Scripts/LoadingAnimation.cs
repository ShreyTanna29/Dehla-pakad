using UnityEngine;
using DG.Tweening;
using TMPro;

public class LoadingAnimation : MonoBehaviour
{
    public TextMeshProUGUI tipText;
    public UnityEngine.UI.Slider loadingSlider;
    private float progress = 0f;
    private string[] tips = {
        "Dehla (10) capture karna sabse zaroori hai!",
        "Hukum (Spades) suit sabse bada hota hai.",
        "Teamwork se game jeetna aasaan ho jata hai.",
        "Saare 4 Dehle pakadne par 'KOT' hota hai!",
        "Apne partner ke patto par dhayan dein."
    };

    public bool shouldRotate = false;
    public bool shouldPulse = false;

    void OnEnable()
    {
        progress = 0f;
        if (loadingSlider != null) loadingSlider.value = 0f;
    }

    void Start()
    {
        // Continuous rotation
        if (shouldRotate)
        {
            transform.DORotate(new Vector3(0, 0, -360), 3f, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Incremental)
                .SetEase(Ease.Linear);
        }
            
        // Slight pulsing
        if (shouldPulse)
        {
            transform.DOScale(1.1f, 1.2f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine);
        }

        if (tipText != null)
        {
            tipText.text = "Tip: " + tips[Random.Range(0, tips.Length)];
            tipText.DOFade(0.5f, 2f).SetLoops(-1, LoopType.Yoyo);
        }
    }

    void Update()
    {
        if (loadingSlider != null)
        {
            progress += Time.deltaTime * 0.2f; // Simulate 5 seconds loading
            if (progress > 1f) progress = 0f; // Reset for looping animation effect
            loadingSlider.value = progress;
        }
    }
}
