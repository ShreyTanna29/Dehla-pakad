using UnityEngine;
using Unity.Services.LevelPlay;

/// <summary>
/// Singleton ads manager for LevelPlay (com.unity.services.levelplay 9.x).
/// Uses the Unity.Services.LevelPlay API — the successor to legacy IronSource.Agent / IronSourceEvents.
/// </summary>
public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance { get; private set; }

    private const string AppKey = "800076896";

    // Ad unit IDs from the LevelPlay dashboard (Android).
    private const string BannerAdUnitId = "Banner_Android";
    private const string InterstitialAdUnitId = "Interstitial_Android";
    private const string RewardedAdUnitId = "Rewarded_Android";

    private LevelPlayBannerAd _bannerAd;
    private LevelPlayInterstitialAd _interstitialAd;
    private LevelPlayRewardedAd _rewardedAd;

    private bool _initRequested;
    private bool _sdkInitialized;
    private bool _adsCreated;
    private System.Action _interstitialClosedCallback;

    public bool IsInitialized => _sdkInitialized;

    #region Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[AdsManager] Singleton created (DontDestroyOnLoad).");
    }

    private void Start()
    {
        if (_initRequested)
            return;

        _initRequested = true;

        Debug.Log($"[AdsManager] Initializing LevelPlay SDK. App Key: {AppKey}");
        Debug.Log("[AdsManager] Running ValidateIntegration()...");
        LevelPlay.ValidateIntegration();

        Debug.Log("[AdsManager] Calling LevelPlay.Init()...");
        LevelPlay.Init(AppKey);
    }

    private void OnEnable()
    {
        LevelPlay.OnInitSuccess += OnSdkInitSuccess;
        LevelPlay.OnInitFailed += OnSdkInitFailed;
        SubscribeAdEvents();
    }

    private void OnDisable()
    {
        LevelPlay.OnInitSuccess -= OnSdkInitSuccess;
        LevelPlay.OnInitFailed -= OnSdkInitFailed;
        UnsubscribeAdEvents();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        DestroyAdObjects();
        Debug.Log("[AdsManager] Destroyed.");
    }

    private void OnApplicationPause(bool isPaused)
    {
        Debug.Log($"[AdsManager] OnApplicationPause({isPaused}) → SetPauseGame({isPaused})");
        LevelPlay.SetPauseGame(isPaused);
    }

    #endregion

    #region Public API — Banner

    /// <summary>Loads the banner ad (LevelPlay 9 equivalent of IronSource.Agent.loadBanner).</summary>
    public void LoadBanner()
    {
        if (!EnsureSdkReady(nameof(LoadBanner)))
            return;

        if (_bannerAd == null)
        {
            Debug.LogError("[AdsManager] Banner ad object is null.");
            return;
        }

        Debug.Log("[AdsManager] LoadBanner() → LoadAd()");
        _bannerAd.LoadAd();
    }

    public void ShowBanner()
    {
        if (!EnsureSdkReady(nameof(ShowBanner)))
            return;

        Debug.Log("[AdsManager] ShowBanner() → ShowAd()");
        _bannerAd?.ShowAd();
    }

    public void HideBanner()
    {
        Debug.Log("[AdsManager] HideBanner() → HideAd()");
        _bannerAd?.HideAd();
    }

    #endregion

    #region Public API — Interstitial

    public void LoadInterstitial()
    {
        if (!EnsureSdkReady(nameof(LoadInterstitial)))
            return;

        if (_interstitialAd == null)
        {
            Debug.LogError("[AdsManager] Interstitial ad object is null.");
            return;
        }

        Debug.Log("[AdsManager] LoadInterstitial() → LoadAd()");
        _interstitialAd.LoadAd();
    }

    public void ShowInterstitial()
    {
        if (!EnsureSdkReady(nameof(ShowInterstitial)))
            return;

        if (_interstitialAd == null)
            return;

        if (!_interstitialAd.IsAdReady())
        {
            Debug.LogWarning("[AdsManager] Interstitial not ready. Triggering LoadInterstitial().");
            LoadInterstitial();
            return;
        }

        Debug.Log("[AdsManager] ShowInterstitial() → ShowAd()");
        _interstitialAd.ShowAd();
    }

    /// <summary>
    /// Shows an interstitial, then runs <paramref name="onClosed"/> when the ad is dismissed
    /// (LevelPlay 9 equivalent of IronSourceEvents.onInterstitialAdClosedEvent).
    /// If the ad cannot be shown, <paramref name="onClosed"/> runs immediately.
    /// </summary>
    public void ShowInterstitialThenRun(System.Action onClosed)
    {
        if (onClosed == null)
        {
            ShowInterstitial();
            return;
        }

        if (!EnsureSdkReady(nameof(ShowInterstitialThenRun)) || _interstitialAd == null)
        {
            Debug.LogWarning("[AdsManager] Interstitial unavailable — running follow-up action immediately.");
            onClosed();
            return;
        }

        if (!_interstitialAd.IsAdReady())
        {
            Debug.LogWarning("[AdsManager] Interstitial not ready — running follow-up action immediately.");
            LoadInterstitial();
            onClosed();
            return;
        }

        _interstitialClosedCallback = onClosed;
        Debug.Log("[AdsManager] ShowInterstitialThenRun() → ShowAd()");
        _interstitialAd.ShowAd();
    }

    void CompletePendingInterstitialCallback()
    {
        System.Action callback = _interstitialClosedCallback;
        _interstitialClosedCallback = null;
        callback?.Invoke();
    }

    public bool IsInterstitialReady()
    {
        return _sdkInitialized && _interstitialAd != null && _interstitialAd.IsAdReady();
    }

    #endregion

    #region Public API — Rewarded

    public bool IsRewardedAdReady()
    {
        return _sdkInitialized && _rewardedAd != null && _rewardedAd.IsAdReady();
    }

    public void ShowRewardedAd()
    {
        if (!EnsureSdkReady(nameof(ShowRewardedAd)))
            return;

        if (_rewardedAd == null)
            return;

        if (!_rewardedAd.IsAdReady())
        {
            Debug.LogWarning("[AdsManager] Rewarded ad not ready.");
            return;
        }

        Debug.Log("[AdsManager] ShowRewardedAd() → ShowAd()");
        _rewardedAd.ShowAd();
    }

    public void LoadRewardedAd()
    {
        if (!EnsureSdkReady(nameof(LoadRewardedAd)))
            return;

        Debug.Log("[AdsManager] LoadRewardedAd() → LoadAd()");
        _rewardedAd?.LoadAd();
    }

    #endregion

    #region SDK Init

    private void OnSdkInitSuccess(LevelPlayConfiguration config)
    {
        _sdkInitialized = true;
        Debug.Log($"[AdsManager] ✅ LevelPlay init SUCCESS. Config: {config}");

        CreateAdObjects();

        if (isActiveAndEnabled)
            SubscribeAdEvents();

        LoadInterstitial();
        LoadRewardedAd();
    }

    private void OnSdkInitFailed(LevelPlayInitError error)
    {
        Debug.LogError($"[AdsManager] ❌ LevelPlay init FAILED: {error}");
    }

    private bool EnsureSdkReady(string caller)
    {
        if (_sdkInitialized)
            return true;

        Debug.LogWarning($"[AdsManager] {caller}() called before SDK initialization completed.");
        return false;
    }

    #endregion

    #region Ad Object Setup

    private void CreateAdObjects()
    {
        if (_adsCreated)
            return;

        Debug.Log("[AdsManager] Creating ad objects...");
        Debug.Log($"[AdsManager]   Banner ad unit:       {BannerAdUnitId}");
        Debug.Log($"[AdsManager]   Interstitial ad unit: {InterstitialAdUnitId}");
        Debug.Log($"[AdsManager]   Rewarded ad unit:     {RewardedAdUnitId}");

        var bannerConfig = new LevelPlayBannerAd.Config.Builder()
            .SetPosition(LevelPlayBannerPosition.BottomCenter)
            .SetSize(LevelPlayAdSize.BANNER)
            .SetDisplayOnLoad(false)
            .Build();

        _bannerAd = new LevelPlayBannerAd(BannerAdUnitId, bannerConfig);
        _interstitialAd = new LevelPlayInterstitialAd(InterstitialAdUnitId);
        _rewardedAd = new LevelPlayRewardedAd(RewardedAdUnitId);

        _adsCreated = true;
        Debug.Log("[AdsManager] Ad objects created.");
    }

    private void DestroyAdObjects()
    {
        UnsubscribeAdEvents();

        _bannerAd?.DestroyAd();
        _interstitialAd?.DestroyAd();
        _rewardedAd?.DestroyAd();

        _bannerAd = null;
        _interstitialAd = null;
        _rewardedAd = null;
        _adsCreated = false;
    }

    #endregion

    #region Event Subscription
    // LevelPlay 9.x per-ad events replace legacy IronSourceEvents callbacks.

    private void SubscribeAdEvents()
    {
        UnsubscribeAdEvents();

        if (_bannerAd != null)
        {
            _bannerAd.OnAdLoaded += OnBannerLoaded;
            _bannerAd.OnAdLoadFailed += OnBannerLoadFailed;
            _bannerAd.OnAdDisplayed += OnBannerDisplayed;
            _bannerAd.OnAdDisplayFailed += OnBannerDisplayFailed;
            _bannerAd.OnAdClicked += OnBannerClicked;
        }

        if (_interstitialAd != null)
        {
            // IronSourceEvents.onInterstitialAdReadyEvent
            _interstitialAd.OnAdLoaded += OnInterstitialLoaded;
            _interstitialAd.OnAdLoadFailed += OnInterstitialLoadFailed;
            _interstitialAd.OnAdDisplayed += OnInterstitialDisplayed;
            _interstitialAd.OnAdDisplayFailed += OnInterstitialDisplayFailed;
            _interstitialAd.OnAdClicked += OnInterstitialClicked;
            // IronSourceEvents.onInterstitialAdClosedEvent
            _interstitialAd.OnAdClosed += OnInterstitialClosed;
        }

        if (_rewardedAd != null)
        {
            _rewardedAd.OnAdLoaded += OnRewardedLoaded;
            _rewardedAd.OnAdLoadFailed += OnRewardedLoadFailed;
            _rewardedAd.OnAdDisplayed += OnRewardedDisplayed;
            _rewardedAd.OnAdDisplayFailed += OnRewardedDisplayFailed;
            // IronSourceEvents.onRewardedVideoAdRewardedEvent
            _rewardedAd.OnAdRewarded += OnRewardedVideoRewarded;
            _rewardedAd.OnAdClicked += OnRewardedClicked;
            _rewardedAd.OnAdClosed += OnRewardedClosed;
        }
    }

    private void UnsubscribeAdEvents()
    {
        if (_bannerAd != null)
        {
            _bannerAd.OnAdLoaded -= OnBannerLoaded;
            _bannerAd.OnAdLoadFailed -= OnBannerLoadFailed;
            _bannerAd.OnAdDisplayed -= OnBannerDisplayed;
            _bannerAd.OnAdDisplayFailed -= OnBannerDisplayFailed;
            _bannerAd.OnAdClicked -= OnBannerClicked;
        }

        if (_interstitialAd != null)
        {
            _interstitialAd.OnAdLoaded -= OnInterstitialLoaded;
            _interstitialAd.OnAdLoadFailed -= OnInterstitialLoadFailed;
            _interstitialAd.OnAdDisplayed -= OnInterstitialDisplayed;
            _interstitialAd.OnAdDisplayFailed -= OnInterstitialDisplayFailed;
            _interstitialAd.OnAdClicked -= OnInterstitialClicked;
            _interstitialAd.OnAdClosed -= OnInterstitialClosed;
        }

        if (_rewardedAd != null)
        {
            _rewardedAd.OnAdLoaded -= OnRewardedLoaded;
            _rewardedAd.OnAdLoadFailed -= OnRewardedLoadFailed;
            _rewardedAd.OnAdDisplayed -= OnRewardedDisplayed;
            _rewardedAd.OnAdDisplayFailed -= OnRewardedDisplayFailed;
            _rewardedAd.OnAdRewarded -= OnRewardedVideoRewarded;
            _rewardedAd.OnAdClicked -= OnRewardedClicked;
            _rewardedAd.OnAdClosed -= OnRewardedClosed;
        }
    }

    #endregion

    #region Banner Callbacks

    private void OnBannerLoaded(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Banner] onAdLoaded: {adInfo}");
    }

    private void OnBannerLoadFailed(LevelPlayAdError error)
    {
        Debug.LogWarning($"[AdsManager] [Banner] onAdLoadFailed: {error}");
    }

    private void OnBannerDisplayed(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Banner] onAdDisplayed: {adInfo}");
    }

    private void OnBannerDisplayFailed(LevelPlayAdInfo adInfo, LevelPlayAdError error)
    {
        Debug.LogWarning($"[AdsManager] [Banner] onAdDisplayFailed: {adInfo}, error: {error}");
    }

    private void OnBannerClicked(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Banner] onAdClicked: {adInfo}");
    }

    #endregion

    #region Interstitial Callbacks

    private void OnInterstitialLoaded(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Interstitial] onInterstitialAdReady: {adInfo}");
    }

    private void OnInterstitialLoadFailed(LevelPlayAdError error)
    {
        Debug.LogWarning($"[AdsManager] [Interstitial] onInterstitialAdLoadFailed: {error}");
    }

    private void OnInterstitialDisplayed(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Interstitial] onInterstitialAdShowSucceeded: {adInfo}");
    }

    private void OnInterstitialDisplayFailed(LevelPlayAdInfo adInfo, LevelPlayAdError error)
    {
        Debug.LogWarning($"[AdsManager] [Interstitial] onInterstitialAdShowFailed: {adInfo}, error: {error}");
        CompletePendingInterstitialCallback();
        LoadInterstitial();
    }

    private void OnInterstitialClicked(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Interstitial] onInterstitialAdClicked: {adInfo}");
    }

    private void OnInterstitialClosed(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Interstitial] onInterstitialAdClosed: {adInfo}");
        CompletePendingInterstitialCallback();
        LoadInterstitial();
    }

    #endregion

    #region Rewarded Callbacks

    private void OnRewardedLoaded(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Rewarded] onRewardedVideoAdAvailable: {adInfo}");
    }

    private void OnRewardedLoadFailed(LevelPlayAdError error)
    {
        Debug.LogWarning($"[AdsManager] [Rewarded] onRewardedVideoAdLoadFailed: {error}");
    }

    private void OnRewardedDisplayed(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Rewarded] onRewardedVideoAdOpened: {adInfo}");
    }

    private void OnRewardedDisplayFailed(LevelPlayAdInfo adInfo, LevelPlayAdError error)
    {
        Debug.LogWarning($"[AdsManager] [Rewarded] onRewardedVideoAdShowFailed: {adInfo}, error: {error}");
    }

    private void OnRewardedVideoRewarded(LevelPlayAdInfo adInfo, LevelPlayReward reward)
    {
        Debug.Log("Reward Player!");
        Debug.Log($"[AdsManager] [Rewarded] onRewardedVideoAdRewarded: ad={adInfo}, reward={reward}");
    }

    private void OnRewardedClicked(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Rewarded] onRewardedVideoAdClicked: {adInfo}");
    }

    private void OnRewardedClosed(LevelPlayAdInfo adInfo)
    {
        Debug.Log($"[AdsManager] [Rewarded] onRewardedVideoAdClosed: {adInfo}");
        LoadRewardedAd();
    }

    #endregion
}
