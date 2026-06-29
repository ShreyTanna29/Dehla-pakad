using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Loads a sprite from Addressables at runtime and applies it to the sibling <see cref="Image"/>.
/// Use with <see cref="AddressableReferenceCleaner"/> to break hard scene references and shrink APK size.
/// </summary>
[RequireComponent(typeof(Image))]
[DisallowMultipleComponent]
public class AddressableUIImageLoader : MonoBehaviour
{
    [Tooltip("Addressables address (copied automatically by the Editor cleaner tool).")]
    public string addressableKey;

    Image _image;
    AsyncOperationHandle<Sprite> _loadHandle;

    void Awake()
    {
        _image = GetComponent<Image>();
    }

    void Start()
    {
        if (_image == null)
            _image = GetComponent<Image>();

        if (_image == null)
        {
            Debug.LogError($"[AddressableUIImageLoader] No Image on '{name}'.", this);
            return;
        }

        // Respect a sprite that was deliberately assigned in the editor.
        // If the Image already has a sprite, keep exactly what was set and do not
        // override it from Addressables at runtime. Only blank (null) Images load
        // their sprite from the addressableKey.
        if (_image.sprite != null)
            return;

        if (string.IsNullOrWhiteSpace(addressableKey))
        {
            Debug.LogWarning($"[AddressableUIImageLoader] addressableKey is empty on '{name}'.", this);
            return;
        }

        if (_loadHandle.IsValid())
            Addressables.Release(_loadHandle);

        Debug.Log($"[AddressableUIImageLoader] Loading '{addressableKey}' for '{name}'...");
        _loadHandle = Addressables.LoadAssetAsync<Sprite>(addressableKey);
        _loadHandle.Completed += OnSpriteLoaded;
    }

    void OnDestroy()
    {
        if (_loadHandle.IsValid())
            Addressables.Release(_loadHandle);
    }

    void OnSpriteLoaded(AsyncOperationHandle<Sprite> operation)
    {
        if (operation.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError(
                $"[AddressableUIImageLoader] Failed to load '{addressableKey}' on '{name}': {operation.OperationException?.Message}",
                this);
            return;
        }

        if (_image == null)
        {
            Debug.LogWarning(
                $"[AddressableUIImageLoader] Sprite '{addressableKey}' loaded, but Image on '{name}' was destroyed.",
                this);
            return;
        }

        _image.sprite = operation.Result;
        Debug.Log($"[AddressableUIImageLoader] Loaded '{addressableKey}' → '{name}'.", this);
    }
}
