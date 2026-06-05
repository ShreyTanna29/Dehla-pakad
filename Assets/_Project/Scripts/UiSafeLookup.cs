using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

        if (_searchRoot == null)
        {
            WarnOnce(objectName, "no UI search root assigned");
            return false;
        }

        foreach (Transform t in _searchRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != objectName) continue;
            go = t.gameObject;
            _cacheByName[objectName] = go;
            return true;
        }

        WarnOnce(objectName, $"not found under '{_searchRoot.name}'");
        return false;
    }

    public static bool TryGetPath(string hierarchyPath, out GameObject go)
    {
        go = null;
        if (string.IsNullOrEmpty(hierarchyPath)) return false;

        if (_cacheByPath.TryGetValue(hierarchyPath, out go) && go != null)
            return true;

        if (_searchRoot == null)
        {
            WarnOnce(hierarchyPath, "no UI search root assigned");
            return false;
        }

        string[] parts = hierarchyPath.Split('/');
        Transform current = FindFirstNamed(_searchRoot, parts[0]);
        if (current == null)
        {
            WarnOnce(hierarchyPath, $"missing segment '{parts[0]}'");
            return false;
        }

        for (int i = 1; i < parts.Length; i++)
        {
            Transform next = FindDirectChild(current, parts[i]);
            if (next == null)
            {
                WarnOnce(hierarchyPath, $"missing segment '{parts[i]}'");
                return false;
            }
            current = next;
        }

        go = current.gameObject;
        _cacheByPath[hierarchyPath] = go;
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
}
