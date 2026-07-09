using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;

/// <summary>
/// Loads a Sprite from Addressables and assigns it to a UI <see cref="Image"/>.
/// Uses <see cref="AddressablesSpriteCache"/> for fast, deduplicated loads.
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

    string _pendingAddress;

    void Start()
    {
        if (!loadOnStart || string.IsNullOrWhiteSpace(addressOnStart))
            return;

        LoadAddressableSprite(addressOnStart);
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

        _pendingAddress = addressName;

        if (AddressablesSpriteCache.TryGetCached(addressName, out Sprite cached))
        {
            targetImage.sprite = cached;
            targetImage.preserveAspect = true;
            return;
        }

        AddressablesSpriteCache.GetSprite(addressName, OnSpriteReady);
    }

    void OnSpriteReady(Sprite sprite)
    {
        if (this == null || targetImage == null)
            return;

        if (sprite == null)
        {
            Debug.LogWarning(
                $"[AddressableLoader] No sprite for '{_pendingAddress}' (missing or not built).", this);
            return;
        }

        targetImage.sprite = sprite;
        targetImage.preserveAspect = true;
    }
}
