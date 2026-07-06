using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Restores direct Image.sprite references from AddressableUIImageLoader keys (reverses the cleaner tool).
/// </summary>
public static class RestoreDirectSpriteReferences
{
    const string MenuPath = "Tools/Dehla Pakad/Restore Direct Sprite References (Active Scene)";

    [MenuItem(MenuPath)]
    static void RestoreActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("No Active Scene", "Open DehlaPakad.unity first.", "OK");
            return;
        }

        AddressableUIImageLoader[] loaders = Object.FindObjectsByType<AddressableUIImageLoader>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int restored = 0;
        int failed = 0;
        var failedKeys = new List<string>();

        Undo.SetCurrentGroupName("Restore Direct Sprite References");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (AddressableUIImageLoader loader in loaders)
        {
            if (loader == null) continue;

            Image image = loader.GetComponent<Image>();
            if (image == null) continue;

            string path = loader.addressableKey;
            if (string.IsNullOrWhiteSpace(path))
            {
                failed++;
                continue;
            }

            path = path.Replace("\\", "/");
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path.TrimStart('/');

            Sprite sprite = null;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is Sprite s)
                {
                    sprite = s;
                    break;
                }
            }

            if (sprite == null)
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite == null)
            {
                failed++;
                if (!failedKeys.Contains(path))
                    failedKeys.Add(path);
                continue;
            }

            Undo.RecordObject(image, "Restore Sprite");
            image.sprite = sprite;
            EditorUtility.SetDirty(image);

            Undo.DestroyObjectImmediate(loader);
            restored++;
        }

        if (restored > 0)
            EditorSceneManager.MarkSceneDirty(scene);

        Undo.CollapseUndoOperations(undoGroup);

        string msg =
            $"Scene: {scene.name}\n\nRestored: {restored}\nFailed: {failed}\n\nSave the scene (Ctrl+S).";
        if (failedKeys.Count > 0)
            Debug.LogWarning("[RestoreDirectSpriteReferences] Could not resolve:\n" + string.Join("\n", failedKeys));

        Debug.Log($"[RestoreDirectSpriteReferences] Restored {restored}, failed {failed}.");
        EditorUtility.DisplayDialog("Restore Direct Sprite References", msg, "OK");
    }
}
