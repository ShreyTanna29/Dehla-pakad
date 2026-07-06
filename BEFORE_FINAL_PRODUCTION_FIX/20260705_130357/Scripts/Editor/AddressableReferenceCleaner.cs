using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Replaces hard <see cref="Image.sprite"/> references to Addressable assets with
/// <see cref="AddressableUIImageLoader"/> + a null sprite (breaks base-APK duplication).
/// </summary>
public static class AddressableReferenceCleaner
{
    const string MenuPath = "Tools/Dehla Pakad/Clean Addressable Hard References (Active Scene)";

    [MenuItem(MenuPath)]
    static void CleanActiveScene()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog(
                "Addressables Not Configured",
                "AddressableAssetSettings could not be found.\n\n" +
                "Open Window → Asset Management → Addressables → Groups and create settings first.",
                "OK");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("No Active Scene", "Open the scene you want to clean, then run this tool again.", "OK");
            return;
        }

        Image[] images = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        int scanned = 0;
        int converted = 0;
        int skipped = 0;
        var convertedObjects = new List<string>();

        Undo.SetCurrentGroupName("Clean Addressable Hard References");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (Image image in images)
        {
            if (image == null)
                continue;

            scanned++;

            Sprite sprite = image.sprite;
            if (sprite == null)
                continue;

            if (!TryResolveAddressableEntry(settings, sprite, out AddressableAssetEntry entry, out string resolveNote))
            {
                skipped++;
                continue;
            }

            GameObject go = image.gameObject;

            AddressableUIImageLoader loader = go.GetComponent<AddressableUIImageLoader>();
            if (loader == null)
                loader = Undo.AddComponent<AddressableUIImageLoader>(go);

            Undo.RecordObject(loader, "Set Addressable Key");
            loader.addressableKey = entry.address;

            Undo.RecordObject(image, "Clear Hard Sprite Reference");
            image.sprite = null;

            EditorUtility.SetDirty(go);
            converted++;
            convertedObjects.Add($"{GetHierarchyPath(go.transform)}  →  {entry.address}  ({resolveNote})");
        }

        if (converted > 0)
            EditorSceneManager.MarkSceneDirty(scene);

        Undo.CollapseUndoOperations(undoGroup);

        string summary =
            $"Scene: {scene.name}\n\n" +
            $"Images scanned: {scanned}\n" +
            $"Converted (hard ref broken): {converted}\n" +
            $"Skipped (not addressable / no sprite): {skipped}\n\n" +
            "Save the scene (Ctrl+S), then rebuild Addressables and your APK.";

        if (convertedObjects.Count > 0)
        {
            int logLimit = Mathf.Min(convertedObjects.Count, 40);
            Debug.Log($"[AddressableReferenceCleaner] Converted {converted} Image(s) in '{scene.name}':\n" +
                        string.Join("\n", convertedObjects.GetRange(0, logLimit)) +
                        (convertedObjects.Count > logLimit ? $"\n... and {convertedObjects.Count - logLimit} more." : ""));
        }
        else
        {
            Debug.Log($"[AddressableReferenceCleaner] No addressable hard references found in '{scene.name}'.");
        }

        EditorUtility.DisplayDialog("Addressable Reference Cleaner", summary, "OK");
    }

    static bool TryResolveAddressableEntry(
        AddressableAssetSettings settings,
        Sprite sprite,
        out AddressableAssetEntry entry,
        out string resolveNote)
    {
        entry = null;
        resolveNote = string.Empty;

        if (sprite == null || settings == null)
            return false;

        // Direct asset path (single-sprite textures, atlases, etc.).
        string assetPath = AssetDatabase.GetAssetPath(sprite);
        if (!string.IsNullOrEmpty(assetPath))
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            entry = settings.FindAssetEntry(guid);
            if (entry != null)
            {
                resolveNote = "main asset";
                return true;
            }
        }

        // Sub-object sprite inside a texture (e.g. multi-sprite sheet).
        if (!string.IsNullOrEmpty(assetPath))
        {
            Object[] subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            foreach (Object sub in subAssets)
            {
                if (sub != sprite)
                    continue;

                string subPath = AssetDatabase.GetAssetPath(sub);
                string subGuid = AssetDatabase.AssetPathToGUID(subPath);
                entry = settings.FindAssetEntry(subGuid);
                if (entry != null)
                {
                    resolveNote = "sub-asset";
                    return true;
                }
            }
        }

        // Fallback: walk all addressable entries and match by asset path.
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null)
                continue;

            foreach (AddressableAssetEntry candidate in group.entries)
            {
                if (candidate == null)
                    continue;

                string entryPath = AssetDatabase.GUIDToAssetPath(candidate.guid);
                if (string.IsNullOrEmpty(entryPath))
                    continue;

                if (entryPath == assetPath)
                {
                    entry = candidate;
                    resolveNote = "group scan";
                    return true;
                }

                Object[] reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(entryPath);
                foreach (Object rep in reps)
                {
                    if (rep == sprite)
                    {
                        entry = candidate;
                        resolveNote = "group sub-asset";
                        return true;
                    }
                }
            }
        }

        return false;
    }

    static string GetHierarchyPath(Transform t)
    {
        if (t == null)
            return string.Empty;

        var parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}
