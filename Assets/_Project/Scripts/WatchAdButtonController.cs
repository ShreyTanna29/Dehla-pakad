using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Home-screen "Watch Ad" button — shows a rewarded ad and plays a gentle pulse loop to draw attention.
/// </summary>
[RequireComponent(typeof(Button))]
public class WatchAdButtonController : MonoBehaviour
{
    [Header("Pulse Animation")]
    [Tooltip("Transform that scales (use icon child so Button layout stays stable).")]
    [SerializeField] RectTransform pulseTarget;
    [SerializeField] float pulseScale = 1.12f;
    [SerializeField] float pulseDuration = 0.55f;

    Button _button;
    Tween _pulseTween;
    Vector3 _baseScale = Vector3.one;

    void Awake()
    {
        _button = GetComponent<Button>();
        if (pulseTarget == null)
            pulseTarget = transform as RectTransform;

        if (pulseTarget != null)
            _baseScale = pulseTarget.localScale;
    }

    void OnEnable()
    {
        StartPulse();
    }

    void OnDisable()
    {
        StopPulse();
    }

    void Start()
    {
        _button.onClick.AddListener(OnClickWatchAd);
    }

    void OnDestroy()
    {
        StopPulse();
        if (_button != null)
            _button.onClick.RemoveListener(OnClickWatchAd);
    }

    public void StartPulse()
    {
        if (pulseTarget == null) return;

        StopPulse();
        pulseTarget.localScale = _baseScale;
        _pulseTween = pulseTarget
            .DOScale(_baseScale * pulseScale, pulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    public void StopPulse()
    {
        _pulseTween?.Kill();
        _pulseTween = null;
        if (pulseTarget != null)
            pulseTarget.localScale = _baseScale;
    }

    public void OnClickWatchAd()
    {
        if (AdsManager.Instance == null)
        {
            Debug.LogError("[WatchAdButton] AdsManager.Instance is null.");
            ShowAdToast("Ads not ready.");
            return;
        }

        if (AdsManager.Instance.IsRewardedAdReady())
        {
            Debug.Log("[WatchAdButton] Showing rewarded ad.");
            StopPulse();
            AdsManager.Instance.ShowRewardedAd();
            return;
        }

        Debug.LogWarning("[WatchAdButton] Rewarded ad not ready — loading.");
        ShowAdToast("Ad loading… please wait.");
        AdsManager.Instance.LoadRewardedAd();
    }

    void ShowAdToast(string message)
    {
        Transform root = transform;
        if (GoogleLogin.Instance != null && GoogleLogin.Instance.homePanel != null)
            root = GoogleLogin.Instance.homePanel.transform;

        ProfileToast.ShowCompact(root, message);
    }
}
