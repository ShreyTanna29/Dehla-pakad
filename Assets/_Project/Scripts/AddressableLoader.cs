using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Loads a Sprite from Addressables and assigns it to a UI <see cref="Image"/>.
/// Attach to any GameObject and wire <see cref="targetImage"/> + the asset address in the Inspector.
/// </summary>
public class AddressableLoader : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private Image targetImage;

    [Header("Load On Start (optional)")]
    [SerializeField] private bool loadOnStart = true;
    [SerializeField] private string addressOnStart = "MyCard";

    [Tooltip("When enabled, an Image that already has a sprite in the Editor is left unchanged.")]
    [SerializeField] private bool preserveManualSprite = true;

    AsyncOperationHandle<Sprite> _loadHandle;
    string _pendingAddress;

    void Start()
    {
        if (!loadOnStart || string.IsNullOrWhiteSpace(addressOnStart))
            return;

        LoadAddressableSprite(addressOnStart);
    }

    void OnDestroy()
    {
        if (_loadHandle.IsValid())
            Addressables.Release(_loadHandle);
    }

    /// <summary>Loads a Sprite by its Addressables address and applies it to <see cref="targetImage"/>.</summary>
    public void LoadAddressableSprite(string addressName)
    {
        if (string.IsNullOrWhiteSpace(addressName))
        {
            Debug.LogError("[AddressableLoader] Address name is empty.", this);
            return;
        }

        if (targetImage == null)
        {
            Debug.LogError("[AddressableLoader] targetImage is not assigned.", this);
            return;
        }

        if (preserveManualSprite && targetImage.sprite != null)
            return;

        if (_loadHandle.IsValid())
            Addressables.Release(_loadHandle);

        _pendingAddress = addressName;

        // Guard: verify the key resolves to a location before loading. LoadAssetAsync with an
        // unregistered/unbuilt key throws InvalidKeyException, so check locations first.
        AsyncOperationHandle<System.Collections.Generic.IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> locHandle =
            Addressables.LoadResourceLocationsAsync(addressName, typeof(Sprite));
        locHandle.Completed += locOp =>
        {
            bool hasLocation = locOp.Status == AsyncOperationStatus.Succeeded &&
                               locOp.Result != null && locOp.Result.Count > 0;
            Addressables.Release(locOp);

            if (!hasLocation)
            {
                Debug.LogWarning(
                    $"[AddressableLoader] No Addressables location for '{addressName}' — skipping load " +
                    "(asset missing or Addressables not built).", this);
                return;
            }

            if (this == null || targetImage == null)
                return;

            _loadHandle = Addressables.LoadAssetAsync<Sprite>(addressName);
            _loadHandle.Completed += OnAssetLoaded;
        };
    }

    void OnAssetLoaded(AsyncOperationHandle<Sprite> handle)
    {
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError(
                $"[AddressableLoader] Failed to load '{_pendingAddress}': {handle.OperationException?.Message}",
                this);
            return;
        }

        if (targetImage == null)
        {
            Debug.LogWarning("[AddressableLoader] Sprite loaded but targetImage was destroyed.", this);
            return;
        }

        targetImage.sprite = handle.Result;
        targetImage.preserveAspect = true;
        Debug.Log($"[AddressableLoader] Asset loaded successfully: '{handle.Result.name}'.", this);
    }
}
