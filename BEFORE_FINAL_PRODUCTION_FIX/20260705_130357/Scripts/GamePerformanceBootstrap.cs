using UnityEngine;
using DG.Tweening;

/// <summary>
/// Mobile-friendly frame pacing and DOTween defaults for smoother UI on device builds.
/// </summary>
public static class GamePerformanceBootstrap
{
    static bool _applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoApply() => Apply();

    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        // DOTween.Init is safe to call even if already initialized.
        DOTween.Init(false, true, LogBehaviour.ErrorsOnly);

        DOTween.useSmoothDeltaTime = true;
        DOTween.SetTweensCapacity(400, 125);
    }

    public static bool IsMobile => Application.isMobilePlatform;

    public static Ease UiEaseOut => IsMobile ? Ease.OutQuad : Ease.OutBack;

    public static float UiDuration(float desktopSeconds)
    {
        return IsMobile ? desktopSeconds * 0.82f : desktopSeconds;
    }

    public static float UiStagger(float desktopSeconds)
    {
        return IsMobile ? desktopSeconds * 0.65f : desktopSeconds;
    }
}
