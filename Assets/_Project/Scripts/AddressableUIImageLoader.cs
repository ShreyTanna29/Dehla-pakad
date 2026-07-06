using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using DG.Tweening;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

/// <summary>
/// Loads a sprite for UI Images whose editor reference was cleared for Addressables.
/// Falls back to direct asset load in the Editor so Play Mode works without a full Addressables build.
/// </summary>
[RequireComponent(typeof(Image))]
[DisallowMultipleComponent]
public class AddressableUIImageLoader : MonoBehaviour
{
    [Tooltip("Addressables address (asset path under Assets/).")]
    public string addressableKey;

    [Tooltip("If true, the image will fade in smoothly once the sprite is loaded.")]
    public bool fadeIn = true;
    [Tooltip("Duration of the fade-in effect.")]
    public float fadeDuration = 0.25f;

    Image _image;
AsyncOperationHandle<Sprite> _loadHandle;
    bool _loadStarted;
    Color _originalColor;

    void Awake()
    {
        _image = GetComponent<UnityEngine.UI.Image>();
        if (_image != null)
        {
            _originalColor = _image.color;
            
            // CRITICAL: Hide immediately in Awake to prevent white box flash 
            // if the sprite is null and we have a key to load.
            if (_image.sprite == null && !string.IsNullOrWhiteSpace(addressableKey))
            {
                Color c = _image.color;
                c.a = 0f;
                _image.color = c;
            }
        }
    }

    void OnEnable()
    {
        if (_image == null)
        {
            _image = GetComponent<Image>();
            if (_image != null)
                _originalColor = _image.color;
        }
        TryLoadSprite();
    }

    void Start() => TryLoadSprite();

    void OnDestroy()
    {
        if (_loadHandle.IsValid())
            Addressables.Release(_loadHandle);
    }

    public void EnsureLoaded()
    {
        if (_image == null)
        {
            _image = GetComponent<Image>();
            if (_image != null)
                _originalColor = _image.color;
        }
        if (_image != null && _image.sprite != null)
            return;
        _loadStarted = false;
        TryLoadSprite();
    }

    void TryLoadSprite()
    {
        if (_loadStarted || _image == null)
            return;

        if (_image.sprite != null)
            return;

        if (string.IsNullOrWhiteSpace(addressableKey))
        {
            Debug.LogWarning($"[AddressableUIImageLoader] addressableKey is empty on '{name}'.", this);
            return;
        }

        // Hide image until sprite is loaded to prevent white/gray flash.
        Color c = _image.color;
        c.a = 0f;
        _image.color = c;

        if (!gameObject.activeInHierarchy)
            return;

        _loadStarted = true;

#if UNITY_EDITOR
        if (TryAssignEditorSprite())
        {
            RestoreColor(false); // No fade in editor
            return;
        }
#endif
        StartCoroutine(LoadSpriteRoutine());
    }

    void RestoreColor(bool allowFade = true)
    {
        if (_image == null) return;

        Color target = _originalColor;
        // If the original alpha was 0 (hidden to prevent flash), we force it to 1
        // so the loaded sprite is visible. If it was already non-zero, we keep it.
        if (target.a < 0.01f) target.a = 1f;

        if (allowFade && fadeIn && Application.isPlaying)
        {
            _image.DOKill();
            _image.DOColor(target, fadeDuration).SetUpdate(true);
        }
        else
        {
            _image.color = target;
        }
    }

#if UNITY_EDITOR
    bool TryAssignEditorSprite()
    {
        if (string.IsNullOrWhiteSpace(addressableKey)) return false;

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return false;

        // 1. Try to find the entry by address to get the REAL path.
        AddressableAssetEntry entry = null;
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            entry = group.entries.FirstOrDefault(e => e.address == addressableKey);
            if (entry != null) break;
        }

        string path = "";
        if (entry != null)
        {
            path = AssetDatabase.GUIDToAssetPath(entry.guid);
        }
        else
        {
            // Fallback: assume the key is a path (old behavior)
            path = addressableKey.Replace("\\", "/");
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path.TrimStart('/');
        }

        if (string.IsNullOrEmpty(path)) return false;

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        Sprite picked = null;
        foreach (Object asset in assets)
        {
            if (asset is Sprite sprite)
            {
                picked = sprite;
                break;
            }
        }

        if (picked == null)
            picked = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (picked == null)
            return false;

        _image.sprite = picked;
        Debug.Log($"[AddressableUIImageLoader] Editor direct load '{path}' (Key: {addressableKey}) → '{name}'.", this);
        return true;
    }
#endif

    IEnumerator LoadSpriteRoutine()
    {
        AsyncOperationHandle init = Addressables.InitializeAsync();
        if (!init.IsDone)
            yield return init;

        if (_image == null)
            yield break;

        // Guard: verify the key actually resolves to a location before loading.
        // Use typeof(Object) instead of typeof(Sprite) because if an asset is stored as a Texture2D,
        // it might not show up as a Sprite location, yet LoadAssetAsync<Sprite> can still extract it.
        AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> locHandle =
            Addressables.LoadResourceLocationsAsync(addressableKey, typeof(Object));
        yield return locHandle;

        bool hasLocation = locHandle.Status == AsyncOperationStatus.Succeeded &&
                           locHandle.Result != null && locHandle.Result.Count > 0;
        
        if (locHandle.IsValid())
            Addressables.Release(locHandle);

        if (!hasLocation)
        {
            Debug.LogWarning(
                $"[AddressableUIImageLoader] No Addressables location for key '{addressableKey}' on '{name}' — " +
                "image left blank (asset missing from group or Addressables not built).",
                this);
            yield break;
        }

        if (_image == null)
            yield break;

        if (_loadHandle.IsValid())
            Addressables.Release(_loadHandle);

        Debug.Log($"[AddressableUIImageLoader] Loading '{addressableKey}' for '{name}'...");

        _loadHandle = Addressables.LoadAssetAsync<Sprite>(addressableKey);
        yield return _loadHandle;

        if (_image == null)
            yield break;

        if (_loadHandle.Status == AsyncOperationStatus.Succeeded && _loadHandle.Result != null)
        {
            _image.sprite = _loadHandle.Result;
            RestoreColor();
            Debug.Log($"[AddressableUIImageLoader] Loaded '{addressableKey}' → '{name}'.", this);
            yield break;
        }

        if (_loadHandle.IsValid())
        {
            Addressables.Release(_loadHandle);
            _loadHandle = default;
        }

        // Fallback for sub-assets if primary load failed
        AsyncOperationHandle<IList<Sprite>> listHandle = Addressables.LoadAssetsAsync<Sprite>(
            addressableKey, null, Addressables.MergeMode.None);
        yield return listHandle;

        if (_image == null)
            yield break;

        if (listHandle.Status == AsyncOperationStatus.Succeeded && listHandle.Result != null && listHandle.Result.Count > 0)
        {
            _image.sprite = listHandle.Result[0];
            RestoreColor();
            Debug.Log($"[AddressableUIImageLoader] Loaded sub-sprite '{addressableKey}' → '{name}'.", this);
            Addressables.Release(listHandle);
            yield break;
        }

        if (listHandle.IsValid())
            Addressables.Release(listHandle);

        Debug.LogWarning(
            $"[AddressableUIImageLoader] Failed to load '{addressableKey}' on '{name}' (Status: {_loadHandle.Status}) — image stays blank.",
            this);
    }

}
