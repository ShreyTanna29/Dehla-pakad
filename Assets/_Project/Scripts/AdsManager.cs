using UnityEngine;
using UnityEngine.Advertisements;
using System;

public class AdsManager : MonoBehaviour, IUnityAdsInitializationListener, IUnityAdsLoadListener, IUnityAdsShowListener
{
    public static AdsManager Instance { get; private set; }

    [Header("Unity Ads Setup")]
    [SerializeField] private string androidGameId = "800080692";
    [SerializeField] private bool testMode = true; // Isko TRUE hi rakhiye jab tak public release na ho

    // Unity Ads Default Placement IDs
    private string interstitialAdUnitId = "Interstitial_Android";
    private string rewardedAdUnitId = "Rewarded_Android";
    private string bannerAdUnitId = "Banner_Android";

    private Action onInterstitialClosed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("AdsManager");
        go.AddComponent<AdsManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        InitializeAds();
    }

    public void InitializeAds()
    {
        if (!Advertisement.isInitialized && Advertisement.isSupported)
        {
            Debug.Log("[UnityAds] Initializing...");
            Advertisement.Initialize(androidGameId, testMode, this);
        }
    }

    public void OnInitializationComplete()
    {
        Debug.Log("[UnityAds] Initialization complete.");
        LoadInterstitial();
        LoadRewardedAd();
        LoadBanner();
    }

    public void OnInitializationFailed(UnityAdsInitializationError error, string message)
    {
        Debug.LogError($"[UnityAds] Init Failed: {error} - {message}");
    }

    // ================= 1 & 2: BANNER ADS (Leaderboard & Exit Panel) =================
    public void LoadBanner()
    {
        if (!Advertisement.isInitialized) return;

        Advertisement.Banner.SetPosition(BannerPosition.BOTTOM_CENTER);
        BannerLoadOptions options = new BannerLoadOptions
        {
            loadCallback = OnBannerLoaded,
            errorCallback = OnBannerError
        };
        Advertisement.Banner.Load(bannerAdUnitId, options);
    }

    void OnBannerLoaded() { Debug.Log("[UnityAds] Banner Loaded"); }
    void OnBannerError(string message) { Debug.LogError($"[UnityAds] Banner Error: {message}"); }

    public void ShowBanner()
    {
        if (Advertisement.isInitialized)
            Advertisement.Banner.Show(bannerAdUnitId);
    }

    public void HideBanner()
    {
        if (Advertisement.isInitialized)
            Advertisement.Banner.Hide();
    }

    // ================= 4: INTERSTITIAL ADS (Game Exit) =================
    public void PreloadAds() => LoadInterstitial();

    public void LoadInterstitial()
    {
        if (Advertisement.isInitialized)
            Advertisement.Load(interstitialAdUnitId, this);
    }

    public bool IsInterstitialReady() => Advertisement.isInitialized;

    public void ShowInterstitial()
    {
        if (Advertisement.isInitialized)
            Advertisement.Show(interstitialAdUnitId, this);
        else
            LoadInterstitial();
    }

    public void ShowInterstitialThenRun(Action onClosed)
    {
        if (Advertisement.isInitialized)
        {
            onInterstitialClosed = onClosed;
            Advertisement.Show(interstitialAdUnitId, this);
        }
        else
        {
            onClosed?.Invoke();
            LoadInterstitial();
        }
    }

    public void ShowBestEffortFullscreenAd(Action onClosed)
    {
        ShowInterstitialThenRun(onClosed);
    }

    // ================= 3: REWARDED ADS (1 Ad = 1 Coin) =================
    public void LoadRewardedAd()
    {
        if (Advertisement.isInitialized)
            Advertisement.Load(rewardedAdUnitId, this);
    }

    public bool IsRewardedAdReady() => Advertisement.isInitialized;

    public void ShowRewardedAd()
    {
        if (Advertisement.isInitialized)
            Advertisement.Show(rewardedAdUnitId, this);
        else
            Debug.LogWarning("Rewarded Ad not ready!");
    }

    // ================= LOAD LISTENERS =================
    public void OnUnityAdsAdLoaded(string adUnitId)
    {
        Debug.Log($"[UnityAds] Ad Loaded: {adUnitId}");
    }

    public void OnUnityAdsFailedToLoad(string adUnitId, UnityAdsLoadError error, string message)
    {
        Debug.LogWarning($"[UnityAds] Error loading {adUnitId}: {error} - {message}");
    }

    // ================= SHOW LISTENERS & REWARD LOGIC =================
    public void OnUnityAdsShowFailure(string adUnitId, UnityAdsShowError error, string message)
    {
        Debug.LogError($"[UnityAds] Error showing {adUnitId}: {error} - {message}");
        ExecuteCallbackAndReset(adUnitId);
    }

    public void OnUnityAdsShowStart(string adUnitId) { }
    public void OnUnityAdsShowClick(string adUnitId) { }

    public void OnUnityAdsShowComplete(string adUnitId, UnityAdsShowCompletionState showCompletionState)
    {
        Debug.Log($"[UnityAds] Ad Completed: {adUnitId} | State: {showCompletionState}");

        // Reward logic: Agar video poora dekha hai, toh 1 Coin add kardo.
        if (adUnitId == rewardedAdUnitId && showCompletionState == UnityAdsShowCompletionState.COMPLETED)
        {
            if (CurrencyAndInventoryManager.Instance != null)
            {
                CurrencyAndInventoryManager.Instance.AddCoins(1);
                Debug.Log("Reward granted: +1 Coin added to Firebase & Local!");
            }
            LoadRewardedAd();
        }
        else if (adUnitId == interstitialAdUnitId)
        {
            LoadInterstitial();
        }

        ExecuteCallbackAndReset(adUnitId);
    }

    private void ExecuteCallbackAndReset(string adUnitId)
    {
        if (adUnitId == interstitialAdUnitId && onInterstitialClosed != null)
        {
            var callback = onInterstitialClosed;
            onInterstitialClosed = null;
            callback.Invoke();
        }
    }
}
