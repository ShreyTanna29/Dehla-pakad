using System.Collections;
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
/// Uses <see cref="AddressablesSpriteCache"/> for fast, deduplicated loads.
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
    bool _loadStarted;
    Color _originalColor;

    void Awake()
    {
        _image = GetComponent<Image>();
        if (_image != null)
        {
            _originalColor = _image.color;

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

    void OnDestroy()
    {
        // Sprites are owned by AddressablesSpriteCache — no per-component release.
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

        // Cache hit — assign immediately (no coroutine / no extra async hop).
        if (AddressablesSpriteCache.TryGetCached(addressableKey, out Sprite cached))
        {
            _image.sprite = cached;
            RestoreColor();
            _loadStarted = true;
            return;
        }

        Color c = _image.color;
        c.a = 0f;
        _image.color = c;

        if (!gameObject.activeInHierarchy)
            return;

        _loadStarted = true;

#if UNITY_EDITOR
        if (TryAssignEditorSprite())
        {
            RestoreColor(false);
            return;
        }
#endif
        StartCoroutine(LoadSpriteRoutine());
    }

    void RestoreColor(bool allowFade = true)
    {
        if (_image == null) return;

        Color target = _originalColor;
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

        AddressableAssetEntry entry = null;
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            entry = group.entries.FirstOrDefault(e => e.address == addressableKey);
            if (entry != null) break;
        }

        string path = "";
        if (entry != null)
            path = AssetDatabase.GUIDToAssetPath(entry.guid);
        else
        {
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
        return true;
    }
#endif

    IEnumerator LoadSpriteRoutine()
    {
        yield return AddressablesSpriteCache.WaitUntilReady();

        if (_image == null)
            yield break;

        bool done = false;
        Sprite loaded = null;
        AddressablesSpriteCache.GetSprite(addressableKey, sprite =>
        {
            loaded = sprite;
            done = true;
        });

        while (!done)
            yield return null;

        if (_image == null)
            yield break;

        if (loaded != null)
        {
            _image.sprite = loaded;
            RestoreColor();
            yield break;
        }

        // Rare fallback: texture with multiple sub-sprites.
        AsyncOperationHandle<System.Collections.Generic.IList<Sprite>> listHandle =
            Addressables.LoadAssetsAsync<Sprite>(addressableKey, null, Addressables.MergeMode.None);
        yield return listHandle;

        if (_image == null)
            yield break;

        if (listHandle.Status == AsyncOperationStatus.Succeeded
            && listHandle.Result != null
            && listHandle.Result.Count > 0)
        {
            _image.sprite = listHandle.Result[0];
            RestoreColor();
        }

        if (listHandle.IsValid())
            Addressables.Release(listHandle);
    }
}
