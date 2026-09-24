using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlueprintsUi.Editor
{
    /// <summary>spec/sprites.json: how each sprite is imported, and where it lives.</summary>
    public static class SpriteSpec
    {
        public const string SpecPath = "spec/sprites.json";

        static Dictionary<string, object> cache;

        public static Dictionary<string, object> All
        {
            get
            {
                cache ??= (Dictionary<string, object>)Json.Parse(File.ReadAllText(SpecPath));
                return cache;
            }
        }

        public static Dictionary<string, object> ForFile(string assetPath)
        {
            foreach (var entry in All.Values)
            {
                var meta = (Dictionary<string, object>)entry;
                if (string.Equals((string)meta["file"], assetPath, StringComparison.OrdinalIgnoreCase))
                    return meta;
            }
            return null;
        }

        public static Sprite Load(string name)
        {
            if (!All.TryGetValue(name, out var entry))
                return null;
            return AssetDatabase.LoadAssetAtPath<Sprite>((string)((Dictionary<string, object>)entry)["file"]);
        }
    }

    /// <summary>
    /// Imports every PNG under Assets/Sprites exactly as spec/sprites.json describes it: a single
    /// full-rect sprite with the source bundle's border, pivot and pixels-per-unit, uncompressed so
    /// the pixels survive the round trip.
    /// </summary>
    public class SpriteImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Sprites/", StringComparison.Ordinal))
                return;
            var meta = SpriteSpec.ForFile(assetPath);
            if (meta == null)
            {
                Debug.LogWarning($"[BlueprintsUi] {assetPath} has no entry in {SpriteSpec.SpecPath}");
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = Convert.ToBoolean(meta["mipmaps"]);
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;

            ///the exact GPU format the source bundle shipped for this texture (DXT5, DXT1, RGB24 or
            ///RGBA32), so the rebuild costs the same video memory. TextureImporterFormat shares
            ///TextureFormat's numbering for all four.
            var format = (TextureImporterFormat)Convert.ToInt32(meta["textureFormat"]);
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = 2048,
                format = format,
                textureCompression = TextureImporterCompression.Compressed,
            });
            importer.filterMode = (FilterMode)Convert.ToInt32(meta["filterMode"]);
            importer.wrapMode = (TextureWrapMode)Convert.ToInt32(meta["wrapMode"]);
            importer.spritePixelsPerUnit = Convert.ToSingle(meta["pixelsPerUnit"]);

            var border = (List<object>)meta["border"];
            importer.spriteBorder = new Vector4(
                Convert.ToSingle(border[0]), Convert.ToSingle(border[1]),
                Convert.ToSingle(border[2]), Convert.ToSingle(border[3]));

            var pivot = (List<object>)meta["pivot"];
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(Convert.ToSingle(pivot[0]), Convert.ToSingle(pivot[1]));
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
        }
    }
}
