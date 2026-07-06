using System;
using UnityEngine;

/// <summary>
/// Compile-safe ads facade. The full LevelPlay implementation lives in git commit 562d6cd
/// (Ads Integration) and requires the Unity package com.unity.services.levelplay to be resolved
/// in Package Manager. Until that package downloads, all ad calls safely no-op so the game builds.
/// </summary>
public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance { get; private set; }

    const string LevelPlayPackageHint =
        "Install/resolve com.unity.services.levelplay in Unity Package Manager, then restore AdsManager from git commit 562d6cd if you need live ads.";

    bool _initRequested;

    public bool IsInitialized => false;

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
        Debug.Log("[AdsManager] Stub active (LevelPlay package not loaded). " + LevelPlayPackageHint);
    }

    void Start()
    {
        if (_initRequested) return;
        _initRequested = true;
        Debug.LogWarning("[AdsManager] LevelPlay SDK unavailable — ads disabled. " + LevelPlayPackageHint);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void LoadBanner() => LogStub(nameof(LoadBanner));
    public void ShowBanner() => LogStub(nameof(ShowBanner));
    public void HideBanner() => LogStub(nameof(HideBanner));

    public void LoadInterstitial() => LogStub(nameof(LoadInterstitial));
    public void ShowInterstitial() => LogStub(nameof(ShowInterstitial));

    public void ShowInterstitialThenRun(Action onClosed)
    {
        LogStub(nameof(ShowInterstitialThenRun));
        onClosed?.Invoke();
    }

    public bool IsInterstitialReady() => false;

    public void ShowBestEffortFullscreenAd(Action onClosed)
    {
        LogStub(nameof(ShowBestEffortFullscreenAd));
        onClosed?.Invoke();
    }

    public void PreloadAds() => LogStub(nameof(PreloadAds));

    public bool IsRewardedAdReady() => false;
    public void ShowRewardedAd() => LogStub(nameof(ShowRewardedAd));
    public void LoadRewardedAd() => LogStub(nameof(LoadRewardedAd));

    static void LogStub(string method)
    {
        Debug.Log($"[AdsManager] {method}() skipped — LevelPlay not loaded.");
    }
}
