using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Registers every art sprite/texture under the project's art folders into the "Art Assets"
/// Addressables group, using the project convention address == asset path.
/// Run this after (re)importing the card art pack so all cards become Addressable again.
/// Then run "Tools/Dehla Pakad/Clean Addressable Hard References (Active Scene)" to make the
/// scene load those sprites from Addressables.
/// </summary>
public static class ArtAddressablesRegistrar
{
    const string MenuPath = "Tools/Dehla Pakad/Register All Art To Addressables";
    const string GroupName = "Art Assets";

    static readonly string[] ArtFolders =
    {
        "Assets/2D Cards Game Art Pack",
        "Assets/_Project/Art"
    };

    [MenuItem(MenuPath)]
    static void RegisterAllArt()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog("Addressables Not Configured",
                "AddressableAssetSettings could not be found. Open Window > Asset Management > Addressables > Groups first.", "OK");
            return;
        }

        AddressableAssetGroup group = settings.FindGroup(GroupName);
        if (group == null)
        {
            EditorUtility.DisplayDialog("Group Missing",
                "Addressables group '" + GroupName + "' was not found.", "OK");
            return;
        }

        // Only search folders that actually exist (card pack may be mid-reimport).
        var existing = new List<string>();
        foreach (string f in ArtFolders)
            if (AssetDatabase.IsValidFolder(f))
                existing.Add(f);

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", existing.ToArray());
        int added = 0, already = 0, skipped = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") { skipped++; continue; }

            if (settings.FindAssetEntry(guid) != null) { already++; continue; }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry != null)
            {
                entry.address = path; // convention: address == asset path
                added++;
            }
        }

        EditorUtility.SetDirty(group);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        string summary =
            "Group: " + GroupName + "\n\n" +
            "Newly added: " + added + "\n" +
            "Already registered: " + already + "\n" +
            "Non-image skipped: " + skipped + "\n\n" +
            "Next: run 'Tools/Dehla Pakad/Clean Addressable Hard References (Active Scene)' and save the scene.";
        Debug.Log("[ArtAddressablesRegistrar] " + summary.Replace("\n", " "));
        EditorUtility.DisplayDialog("Register All Art To Addressables", summary, "OK");
    }
}
