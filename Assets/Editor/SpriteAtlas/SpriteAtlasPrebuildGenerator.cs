// [UNITY-SKILL:SPRITEATLAS]
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using System.IO;

public class SpriteAtlasPrebuildGenerator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // 1. Enable Sprite Atlas V2
        EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;

        // 2. Define Atlases
        // Core UI and Cards
        GenerateAtlasByFolder("Assets/2D Cards Game Art Pack/Sprites", "Assets/Atlases/GameArt.spriteatlasv2");
        GenerateAtlasByFolder("Assets/_Project/Art/uVegas/Images/Cards", "Assets/Atlases/Cards.spriteatlasv2");
    }

    private void GenerateAtlasByFolder(string folderPath, string atlasPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Debug.LogWarning($"[SpriteAtlas] Folder not found: {folderPath}");
            return;
        }

        string atlasDir = Path.GetDirectoryName(atlasPath);
        if (!Directory.Exists(atlasDir)) Directory.CreateDirectory(atlasDir);

        SpriteAtlasAsset atlasAsset = SpriteAtlasAsset.Load(atlasPath);
        if (atlasAsset == null)
        {
            atlasAsset = new SpriteAtlasAsset();
            AssetDatabase.CreateAsset(atlasAsset, atlasPath);
            AssetDatabase.SaveAssets();
        }

        // Add folder to packables
        Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(folderPath);
        if (folderObj != null)
        {
            SpriteAtlas runtimeAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            bool alreadyAdded = false;
            if (runtimeAtlas != null)
            {
                Object[] currentPackables = runtimeAtlas.GetPackables();
                foreach (var p in currentPackables)
                {
                    if (p == folderObj)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }
            }

            if (!alreadyAdded)
            {
                atlasAsset.Add(new Object[] { folderObj });
            }
        }

        // Configure Importer
        SpriteAtlasImporter importer = AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
        if (importer != null)
        {
            var packingSettings = importer.packingSettings;
            packingSettings.enableRotation = false;
            packingSettings.enableTightPacking = false;
            packingSettings.padding = 4;
            importer.packingSettings = packingSettings;

            var textureSettings = importer.textureSettings;
            textureSettings.generateMipMaps = false;
            textureSettings.filterMode = FilterMode.Bilinear;
            importer.textureSettings = textureSettings;

            // Platform settings for Android
            TextureImporterPlatformSettings androidSettings = importer.GetPlatformSettings("Android");
            androidSettings.overridden = true;
            androidSettings.maxTextureSize = 2048;
            androidSettings.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformSettings(androidSettings);

            importer.SaveAndReimport();
        }

        AssetDatabase.SaveAssets();
    }
}
