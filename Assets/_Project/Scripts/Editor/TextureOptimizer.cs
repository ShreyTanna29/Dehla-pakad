using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-compresses profile avatar textures for Android to reduce APK size.
/// </summary>
public static class TextureOptimizer
{
    const string MenuPath = "Tools/Optimize Profile Images";
    const string ProfileImagesFolder = "Assets/_Project/Art/Sprites/Profile_Images";

    const int AndroidMaxSize = 256;
    const int AndroidCompressionQuality = 50;

    // ASTC is the native, high-quality compressed format on modern Android GPUs.
    // DXT/Crunch targets desktop; avoid for Android UI sprites.
    const TextureImporterFormat AndroidFormat = TextureImporterFormat.ASTC_6x6;

    [MenuItem(MenuPath)]
    static void OptimizeProfileImages()
    {
        if (!AssetDatabase.IsValidFolder(ProfileImagesFolder))
        {
            Debug.LogError($"[TextureOptimizer] Folder not found: {ProfileImagesFolder}");
            EditorUtility.DisplayDialog(
                "Texture Optimizer",
                $"Folder not found:\n{ProfileImagesFolder}",
                "OK");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ProfileImagesFolder });

        int scanned = 0;
        int optimized = 0;
        int skipped = 0;
        int failed = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsRasterImage(assetPath))
                    continue;

                scanned++;

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null)
                {
                    failed++;
                    Debug.LogWarning($"[TextureOptimizer] Skipped (no TextureImporter): {assetPath}");
                    continue;
                }

                if (!ApplyAndroidCompression(importer, assetPath))
                {
                    skipped++;
                    continue;
                }

                importer.SaveAndReimport();
                optimized++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        string message =
            $"Scanned: {scanned}\n" +
            $"Compressed (Android override applied): {optimized}\n" +
            $"Skipped (already optimal): {skipped}\n" +
            $"Failed: {failed}\n\n" +
            $"Settings: Max Size {AndroidMaxSize}, Format {AndroidFormat}, Quality {AndroidCompressionQuality}";

        Debug.Log($"[TextureOptimizer] Profile image compression complete. {message.Replace("\n", " | ")}");
        EditorUtility.DisplayDialog("Texture Optimizer — Profile Images", message, "OK");
    }

    static bool IsRasterImage(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;

        string ext = Path.GetExtension(assetPath).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
    }

    /// <returns>True if settings were changed and a reimport is required.</returns>
    static bool ApplyAndroidCompression(TextureImporter importer, string assetPath)
    {
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");

        bool alreadyOptimal =
            android.overridden &&
            android.maxTextureSize == AndroidMaxSize &&
            android.format == AndroidFormat &&
            android.compressionQuality == AndroidCompressionQuality;

        if (alreadyOptimal)
        {
            Debug.Log($"[TextureOptimizer] Already optimal: {assetPath}");
            return false;
        }

        android.overridden = true;
        android.maxTextureSize = AndroidMaxSize;
        android.format = AndroidFormat;
        android.compressionQuality = AndroidCompressionQuality;

        importer.SetPlatformTextureSettings(android);

        Debug.Log(
            $"[TextureOptimizer] Android override applied to '{assetPath}' " +
            $"(maxSize={AndroidMaxSize}, format={AndroidFormat}, quality={AndroidCompressionQuality}).");

        return true;
    }
}
