using DG.Tweening;
using UnityEngine;

/// <summary>
/// Lightweight UI slide animator for collapsible panels (e.g. the Friends list drawer).
/// Wire <see cref="ToggleFriendListPanel"/> to a Button's OnClick in the Inspector.
/// </summary>
public class UIAnimator : MonoBehaviour
{
    public static UIAnimator Instance;

    [Header("Slide Settings")]
    [SerializeField] private float hiddenAnchoredX = 500f;
    [SerializeField] private float visibleAnchoredX = 0f;
    [SerializeField] private float slideDuration = 0.35f;
    [SerializeField] private Ease slideInEase = Ease.OutCubic;
    [SerializeField] private Ease slideOutEase = Ease.InCubic;

    // Tracks the panel currently being animated so rapid clicks don't stack tweens.
    RectTransform _activePanel;
    Tween _activeTween;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    void OnDestroy()
    {
        KillActiveTween();
    }

    /// <summary>
    /// Slides the friend-list panel off-screen then deactivates it, or activates it and slides in.
    /// Pass the root GameObject that owns the panel's RectTransform.
    /// </summary>
    public void ToggleFriendListPanel(GameObject panel)
    {
        if (panel == null) return;

        RectTransform rt = panel.GetComponent<RectTransform>();
        if (rt == null)
        {
            Debug.LogWarning("[UIAnimator] ToggleFriendListPanel — panel has no RectTransform.");
            return;
        }

        KillActiveTween();
        _activePanel = rt;

        if (panel.activeSelf)
            SlideOutAndDeactivate(panel, rt);
        else
            SlideInAndActivate(panel, rt);
    }

    void SlideOutAndDeactivate(GameObject panel, RectTransform rt)
    {
        float y = rt.anchoredPosition.y;
        _activeTween = rt
            .DOAnchorPos(new Vector2(hiddenAnchoredX, y), slideDuration)
            .SetEase(slideOutEase)
            .OnComplete(() =>
            {
                panel.SetActive(false);
                _activeTween = null;
                _activePanel = null;
            });
    }

    void SlideInAndActivate(GameObject panel, RectTransform rt)
    {
        float y = rt.anchoredPosition.y;
        rt.anchoredPosition = new Vector2(hiddenAnchoredX, y);
        panel.SetActive(true);

        _activeTween = rt
            .DOAnchorPos(new Vector2(visibleAnchoredX, y), slideDuration)
            .SetEase(slideInEase)
            .OnComplete(() =>
            {
                _activeTween = null;
                _activePanel = null;
            });
    }

    void KillActiveTween()
    {
        if (_activeTween != null && _activeTween.IsActive())
            _activeTween.Kill();

        if (_activePanel != null)
            _activePanel.DOKill();

        _activeTween = null;
        _activePanel = null;
    }
}
