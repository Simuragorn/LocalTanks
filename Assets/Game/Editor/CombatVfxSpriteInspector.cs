using System;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Reports what Unity actually holds for a generated sprite: its format, and, after a temporary
    /// reimport as readable, the colour of its pixels. Used to tell a texture that lost its colour from a
    /// material or tint that renders it wrong.
    /// </summary>
    public static class CombatVfxSpriteInspector
    {
        [MenuItem("Local Tanks/Diagnostics/Inspect Combat Vfx Sprites")]
        public static void Inspect()
        {
            foreach (string textureName in VfxTextureLibrary.All)
            {
                string assetPath = CombatVfxAssetBuilder.TextureAssetPath(textureName);
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"[vfxsprite] {textureName}: no importer at {assetPath}");
                    continue;
                }

                bool wasReadable = importer.isReadable;
                try
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    Report(textureName, texture);
                }
                finally
                {
                    importer.isReadable = wasReadable;
                    importer.SaveAndReimport();
                }
            }
        }

        private static void Report(string textureName, Texture2D texture)
        {
            if (texture == null)
            {
                Debug.LogWarning($"[vfxsprite] {textureName}: texture missing");
                return;
            }

            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            Color32 centre = pixels[(texture.height / 2) * width + width / 2];
            Color32 opaqueSample = centre;
            int brightest = 0;
            foreach (Color32 pixel in pixels)
            {
                if (pixel.a > brightest)
                {
                    brightest = pixel.a;
                    opaqueSample = pixel;
                }
            }

            Debug.Log($"[vfxsprite] {textureName}: format={texture.format} size={width}x{texture.height} " +
                      $"centre=({centre.r},{centre.g},{centre.b},{centre.a}) " +
                      $"mostOpaque=({opaqueSample.r},{opaqueSample.g},{opaqueSample.b},{opaqueSample.a})");
        }
    }
}
