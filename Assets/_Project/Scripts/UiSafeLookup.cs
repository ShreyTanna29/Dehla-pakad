using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Finds UI objects under an assigned root without GameObject.Find (avoids inactive-object assertions).</summary>
public static class UiSafeLookup
{
    static Transform _searchRoot;
    static readonly Dictionary<string, GameObject> _cacheByName = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, GameObject> _cacheByPath = new Dictionary<string, GameObject>();
    static readonly HashSet<string> _warnedKeys = new HashSet<string>();

    public static void SetSearchRoot(Transform root)
    {
        if (_searchRoot == root) return;
        _searchRoot = root;
        _cacheByName.Clear();
        _cacheByPath.Clear();
    }

    public static bool TryGet(string objectName, out GameObject go)
    {
        go = null;
        if (string.IsNullOrEmpty(objectName)) return false;

        if (_cacheByName.TryGetValue(objectName, out go) && go != null)
            return true;

        // 1. Try search root
        if (_searchRoot != null)
        {
            foreach (Transform t in _searchRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == objectName)
                {
                    go = t.gameObject;
                    _cacheByName[objectName] = go;
                    return true;
                }
            }
        }

        // 2. Fallback: Try Canvas (common for UI)
        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas != null && canvas.transform != _searchRoot)
        {
            foreach (Transform t in canvas.transform.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == objectName)
                {
                    go = t.gameObject;
                    _cacheByName[objectName] = go;
                    return true;
                }
            }
        }

        // 3. Last resort: Find inactive objects globally (costly, but safer than failing)
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var obj in allObjects)
        {
            if (obj.hideFlags != HideFlags.None || obj.name != objectName) continue;
            if (!IsRuntimeSceneObject(obj)) continue;

            go = obj;
            _cacheByName[objectName] = go;
            return true;
        }

        return false;
    }

    public static bool TryGetPath(string hierarchyPath, out GameObject go)
    {
        go = null;
        if (string.IsNullOrEmpty(hierarchyPath)) return false;

        if (_cacheByPath.TryGetValue(hierarchyPath, out go) && go != null)
            return true;

        string[] parts = hierarchyPath.Split('/');
        
        // Try current search root
        if (_searchRoot != null && TryResolvePathRecursive(_searchRoot, parts, out go))
        {
            _cacheByPath[hierarchyPath] = go;
            return true;
        }

        // Fallback: Try Canvas root
        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas != null && canvas.transform != _searchRoot && TryResolvePathRecursive(canvas.transform, parts, out go))
        {
            _cacheByPath[hierarchyPath] = go;
            return true;
        }

        return false;
    }

    private static bool TryResolvePathRecursive(Transform root, string[] parts, out GameObject result)
    {
        result = null;
        Transform current = FindFirstNamed(root, parts[0]);
        if (current == null) return false;

        for (int i = 1; i < parts.Length; i++)
        {
            Transform next = FindDirectChild(current, parts[i]);
            if (next == null) return false;
            current = next;
        }

        result = current.gameObject;
        return true;
    }

    public static bool TryGetImage(string objectName, out Image image)
    {
        image = null;
        if (!TryGet(objectName, out GameObject go) || go == null) return false;
        image = go.GetComponent<Image>();
        return image != null;
    }

    public static bool TryGetButton(string objectName, out Button button)
    {
        button = null;
        if (!TryGet(objectName, out GameObject go) || go == null) return false;
        button = go.GetComponent<Button>();
        return button != null;
    }

    static Transform FindFirstNamed(Transform root, string objectName)
    {
        if (root.name == objectName)
            return root;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == objectName)
                return t;
        }

        return null;
    }

    static Transform FindDirectChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;
        }
        return null;
    }

    static void WarnOnce(string key, string detail)
    {
        if (_warnedKeys.Add(key))
            Debug.LogWarning($"[UI Lookup] {key} — {detail}");
    }

    static bool IsRuntimeSceneObject(GameObject obj)
    {
#if UNITY_EDITOR
        return !EditorUtility.IsPersistent(obj);
#else
        return obj.scene.IsValid();
#endif
    }
}
