using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Fast shared sprite cache for Addressables UI loads.
/// - Initializes Addressables once at boot
/// - Deduplicates in-flight loads (many Images, one IO request)
/// - Skips the extra LoadResourceLocationsAsync round-trip per asset
/// </summary>
public static class AddressablesSpriteCache
{
    static AsyncOperationHandle? _initHandle;
    static bool _initDone;

    static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    static readonly Dictionary<string, AsyncOperationHandle<Sprite>> _handles =
        new Dictionary<string, AsyncOperationHandle<Sprite>>(StringComparer.Ordinal);
    static readonly Dictionary<string, List<Action<Sprite>>> _waiters =
        new Dictionary<string, List<Action<Sprite>>>(StringComparer.Ordinal);
    static readonly HashSet<string> _failedKeys = new HashSet<string>(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void WarmUpOnBoot() => EnsureInitialized();

    /// <summary>Starts Addressables initialization as early as possible (non-blocking).</summary>
    public static void EnsureInitialized()
    {
        if (_initDone) return;

        if (!_initHandle.HasValue || !_initHandle.Value.IsValid())
            _initHandle = Addressables.InitializeAsync();
        else if (_initHandle.Value.IsDone)
            _initDone = true;
    }

    public static IEnumerator WaitUntilReady()
    {
        EnsureInitialized();
        if (_initHandle.HasValue && !_initHandle.Value.IsDone)
            yield return _initHandle.Value;
        _initDone = true;
    }

    public static bool TryGetCached(string key, out Sprite sprite)
    {
        sprite = null;
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _sprites.TryGetValue(key, out sprite) && sprite != null;
    }

    /// <summary>Loads a sprite (instant if cached). Callback (optional) runs on the main thread.</summary>
    public static void GetSprite(string key, Action<Sprite> onComplete)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            onComplete?.Invoke(null);
            return;
        }

        if (_sprites.TryGetValue(key, out Sprite cached) && cached != null)
        {
            onComplete?.Invoke(cached);
            return;
        }

        if (_failedKeys.Contains(key))
        {
            onComplete?.Invoke(null);
            return;
        }

        if (_waiters.TryGetValue(key, out List<Action<Sprite>> list))
        {
            if (onComplete != null) list.Add(onComplete);
            return;
        }

        var callbacks = new List<Action<Sprite>>();
        if (onComplete != null) callbacks.Add(onComplete);
        _waiters[key] = callbacks;
        EnsureInitialized();
        BeginLoad(key);
    }

    /// <summary>Starts caching a key without a callback (fire-and-forget warm-up).</summary>
    public static void Preload(string key) => GetSprite(key, null);

    /// <summary>
    /// Boot par project ke SAARE addressable sprites ko RAM mein preload karta hai.
    /// Isse gameplay mein har card / UI sprite instant milega (thodi zyada RAM use hoti hai).
    /// </summary>
    public static IEnumerator PreloadAllSpritesRoutine()
    {
        yield return WaitUntilReady();

        // Har locator ke saare keys ikattha karo (addresses + labels + guids).
        var allKeys = new List<object>();
        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator == null || locator.Keys == null) continue;
            foreach (object k in locator.Keys)
                allKeys.Add(k);
        }

        if (allKeys.Count == 0) yield break;

        // Union merge → sirf Sprite type ke locations, duplicate h-key ek hi baar.
        AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> locHandle =
            Addressables.LoadResourceLocationsAsync(allKeys, Addressables.MergeMode.Union, typeof(Sprite));
        yield return locHandle;

        if (locHandle.Status != AsyncOperationStatus.Succeeded || locHandle.Result == null)
        {
            if (locHandle.IsValid()) Addressables.Release(locHandle);
            yield break;
        }

        // Primary key se dedupe karke har unique sprite ko background mein load karo.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var loc in locHandle.Result)
        {
            if (loc == null) continue;
            string pk = loc.PrimaryKey;
            if (string.IsNullOrEmpty(pk)) continue;
            if (!seen.Add(pk)) continue;
            Preload(pk);
        }

        Addressables.Release(locHandle);
    }

    /// <summary>Preloads unique keys in parallel — use before showing a UI panel with many loaders.</summary>
    public static IEnumerator PreloadKeysRoutine(IEnumerable<string> keys)
    {
        if (keys == null) yield break;

        yield return WaitUntilReady();

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (_sprites.ContainsKey(key) || _failedKeys.Contains(key)) continue;
            unique.Add(key);
        }

        if (unique.Count == 0) yield break;

        int remaining = unique.Count;
        foreach (string key in unique)
        {
            GetSprite(key, _ =>
            {
                remaining--;
            });
        }

        while (remaining > 0)
            yield return null;
    }

    static void BeginLoad(string key)
    {
        AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(key);
        _handles[key] = handle;
        handle.Completed += op => OnLoadComplete(key, op);
    }

    static void OnLoadComplete(string key, AsyncOperationHandle<Sprite> op)
    {
        Sprite sprite = op.Status == AsyncOperationStatus.Succeeded ? op.Result : null;

        if (sprite != null)
            _sprites[key] = sprite;
        else
            _failedKeys.Add(key);

        if (!_waiters.TryGetValue(key, out List<Action<Sprite>> callbacks))
            return;

        _waiters.Remove(key);
        for (int i = 0; i < callbacks.Count; i++)
            callbacks[i]?.Invoke(sprite);
    }
}
