using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Loads Addressable sprites and prefabs at runtime.
/// Sprites use <see cref="AddressablesSpriteCache"/> for fast, deduplicated loads.
/// </summary>
public class DynamicAssetLoader : MonoBehaviour
{
    public static DynamicAssetLoader Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        AddressablesSpriteCache.EnsureInitialized();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Fast sprite load: agar RAM cache mein hai to 0ms instant, warna async load + cache.
    /// In-flight dedupe bhi — same key ki multiple requests par sirf ek hi disk/network load hota hai.
    /// </summary>
    public void LoadAddressableSpriteFast(string addressableKey, Image targetImage)
    {
        if (targetImage == null || string.IsNullOrWhiteSpace(addressableKey))
            return;

        // 1. RAM (cache) mein hai → turant lagao.
        if (AddressablesSpriteCache.TryGetCached(addressableKey, out Sprite cached))
        {
            targetImage.sprite = cached;
            return;
        }

        // 2. Warna async load; result cache mein save ho jayega agli baar ke liye.
        AddressablesSpriteCache.GetSprite(addressableKey, sprite =>
        {
            if (targetImage == null) return;
            if (sprite != null)
                targetImage.sprite = sprite;
            else
                Debug.LogError($"[Addressables] Failed to load asset: {addressableKey}");
        });
    }

    /// <summary>Loads an Addressable <see cref="Sprite"/> and assigns it to <paramref name="targetImage"/>.</summary>
    public void LoadSpriteDynamically(string addressableKey, Image targetImage)
    {
        if (string.IsNullOrWhiteSpace(addressableKey))
        {
            Debug.LogError("[DynamicAssetLoader] Sprite load failed — addressable key is empty.");
            return;
        }

        if (targetImage == null)
        {
            Debug.LogError($"[DynamicAssetLoader] Sprite load failed for '{addressableKey}' — target Image is null.");
            return;
        }

        if (AddressablesSpriteCache.TryGetCached(addressableKey, out Sprite cached))
        {
            targetImage.sprite = cached;
            return;
        }

        AddressablesSpriteCache.GetSprite(addressableKey, sprite =>
        {
            if (targetImage == null) return;
            if (sprite != null)
                targetImage.sprite = sprite;
            else
                Debug.LogWarning($"[DynamicAssetLoader] No sprite for '{addressableKey}'.");
        });
    }

    /// <summary>Instantiates an Addressable prefab under <paramref name="parent"/>.</summary>
    public void LoadPrefabDynamically(string addressableKey, Transform parent)
    {
        if (string.IsNullOrWhiteSpace(addressableKey))
        {
            Debug.LogError("[DynamicAssetLoader] Prefab load failed — addressable key is empty.");
            return;
        }

        AddressablesSpriteCache.EnsureInitialized();
        AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(addressableKey, parent);
        handle.Completed += operation =>
        {
            if (operation.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError(
                    $"[DynamicAssetLoader] Failed to load prefab '{addressableKey}': {operation.OperationException?.Message}");
            }
        };
    }
}
