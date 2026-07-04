using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using UnityEditor;
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

    Image _image;
    AsyncOperationHandle<Sprite> _loadHandle;
    bool _loadStarted;

    void Awake()
    {
        _image = GetComponent<Image>();
    }

    void OnEnable()
    {
        if (_image == null)
            _image = GetComponent<Image>();
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
            _image = GetComponent<Image>();
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

        _loadStarted = true;

#if UNITY_EDITOR
        if (TryAssignEditorSprite())
            return;
#endif
        StartCoroutine(LoadSpriteRoutine());
    }

#if UNITY_EDITOR
    bool TryAssignEditorSprite()
    {
        string path = addressableKey.Replace("\\", "/");
        if (!path.StartsWith("Assets/"))
            path = "Assets/" + path.TrimStart('/');

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
        Debug.Log($"[AddressableUIImageLoader] Editor direct load '{path}' → '{name}'.", this);
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
            Debug.Log($"[AddressableUIImageLoader] Loaded '{addressableKey}' → '{name}'.", this);
            yield break;
        }

        if (_loadHandle.IsValid())
        {
            Addressables.Release(_loadHandle);
            _loadHandle = default;
        }

        AsyncOperationHandle<IList<Sprite>> listHandle = Addressables.LoadAssetsAsync<Sprite>(
            addressableKey, null, Addressables.MergeMode.None);
        yield return listHandle;

        if (_image == null)
            yield break;

        if (listHandle.Status == AsyncOperationStatus.Succeeded && listHandle.Result != null && listHandle.Result.Count > 0)
        {
            _image.sprite = listHandle.Result[0];
            Debug.Log($"[AddressableUIImageLoader] Loaded sub-sprite '{addressableKey}' → '{name}'.", this);
            Addressables.Release(listHandle);
            yield break;
        }

        if (listHandle.IsValid())
            Addressables.Release(listHandle);

        Debug.LogWarning(
            $"[AddressableUIImageLoader] Failed to load '{addressableKey}' on '{name}' — image stays blank until restored.",
            this);
    }
}
