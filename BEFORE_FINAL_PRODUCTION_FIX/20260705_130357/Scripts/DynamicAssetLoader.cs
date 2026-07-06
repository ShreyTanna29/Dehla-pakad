using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Loads Addressable sprites and prefabs at runtime to keep heavy assets out of the main build.
/// Add this component to a bootstrap GameObject in your first scene (DontDestroyOnLoad).
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
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Loads an Addressable <see cref="Sprite"/> and assigns it to <paramref name="targetImage"/>.
    /// </summary>
    /// <param name="addressableKey">The Addressables address or label for the sprite.</param>
    /// <param name="targetImage">UI Image that will receive the loaded sprite.</param>
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

        Debug.Log($"[DynamicAssetLoader] Loading sprite '{addressableKey}'...");

        AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(addressableKey);
        handle.Completed += operation =>
        {
            if (operation.Status == AsyncOperationStatus.Succeeded)
            {
                if (targetImage == null)
                {
                    Debug.LogWarning(
                        $"[DynamicAssetLoader] Sprite '{addressableKey}' loaded, but the target Image was destroyed before assignment.");
                    return;
                }

                targetImage.sprite = operation.Result;
                Debug.Log($"[DynamicAssetLoader] Sprite loaded successfully: '{addressableKey}'");
                return;
            }

            Debug.LogError(
                $"[DynamicAssetLoader] Failed to load sprite '{addressableKey}': {operation.OperationException?.Message}");
        };
    }

    /// <summary>
    /// Instantiates an Addressable prefab under <paramref name="parent"/>.
    /// </summary>
    /// <param name="addressableKey">The Addressables address or label for the prefab.</param>
    /// <param name="parent">Optional parent transform for the new instance.</param>
    public void LoadPrefabDynamically(string addressableKey, Transform parent)
    {
        if (string.IsNullOrWhiteSpace(addressableKey))
        {
            Debug.LogError("[DynamicAssetLoader] Prefab load failed — addressable key is empty.");
            return;
        }

        Debug.Log($"[DynamicAssetLoader] Loading prefab '{addressableKey}'...");

        AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(addressableKey, parent);
        handle.Completed += operation =>
        {
            if (operation.Status == AsyncOperationStatus.Succeeded)
            {
                GameObject instance = operation.Result;
                if (instance == null)
                {
                    Debug.LogError($"[DynamicAssetLoader] Prefab '{addressableKey}' reported success but instance is null.");
                    return;
                }

                Debug.Log($"[DynamicAssetLoader] Prefab loaded successfully: '{addressableKey}' → {instance.name}");
                return;
            }

            Debug.LogError(
                $"[DynamicAssetLoader] Failed to load prefab '{addressableKey}': {operation.OperationException?.Message}");
        };
    }
}
