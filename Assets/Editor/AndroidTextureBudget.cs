using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Acrophobia.EditorTools
{
    /// <summary>
    /// Puts an Android size and format budget on the photoscanned city textures.
    ///
    /// Assets/vrbn_studios holds 976 textures totalling ~9.9 GB, many of them 4K-8K
    /// uncompressed TGA. Switching the project to Android makes Unity decode and
    /// re-encode every one of them, which is what made a 24 GB machine unusable for
    /// the better part of an hour and never finished.
    ///
    /// It would also be wrong even on a fast machine: an APK carrying textures at that
    /// size runs to gigabytes, which is impractical on a Quest and dreadful to load.
    /// The city is scenery seen from thirty to a hundred metres away across a street,
    /// so 512 with ASTC is indistinguishable in a headset and roughly two orders of
    /// magnitude cheaper.
    ///
    /// Run this BEFORE switching platform. The override only applies to the Android
    /// import, so with it already in place the switch imports straight to 512 instead
    /// of importing at full size and then being redone.
    ///
    /// Deliberately does NOT call SaveAndReimport: that would force a reimport for the
    /// *current* platform too, paying the expensive cost immediately and for nothing.
    /// Writing the meta is enough - Android picks it up when the switch happens.
    /// </summary>
    public static class AndroidTextureBudget
    {
        private const string CityRoot = "Assets/vrbn_studios";
        private const string AndroidPlatform = "Android";

        [MenuItem("Acrophobia/Android/Apply city texture budget (512, ASTC)")]
        public static void Apply()
        {
            ApplyWithSize(512);
        }

        [MenuItem("Acrophobia/Android/Apply city texture budget (1024, ASTC)")]
        public static void ApplyLarger()
        {
            ApplyWithSize(1024);
        }

        private static void ApplyWithSize(int maxSize)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CityRoot });
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Nothing found",
                    "No textures under " + CityRoot + ".", "OK");
                return;
            }

            bool go = EditorUtility.DisplayDialog(
                "Apply Android texture budget",
                guids.Length + " textures under " + CityRoot + " will be capped at "
                + maxSize + "px with ASTC 6x6 for Android only.\n\n"
                + "Windows import settings are untouched, so the editor view does not change.\n\n"
                + "Run this while still on the Windows target, then switch to Android.",
                "Apply", "Cancel");
            if (!go) return;

            var changed = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Android texture budget",
                            i + " / " + guids.Length + "   " + System.IO.Path.GetFileName(path),
                            i / (float)guids.Length))
                    {
                        break;
                    }

                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;

                    var settings = importer.GetPlatformTextureSettings(AndroidPlatform);

                    bool alreadyRight = settings.overridden
                                        && settings.maxTextureSize == maxSize
                                        && settings.format == TextureImporterFormat.ASTC_6x6;
                    if (alreadyRight) continue;

                    settings.overridden = true;
                    settings.maxTextureSize = maxSize;
                    settings.format = TextureImporterFormat.ASTC_6x6;
                    settings.textureCompression = TextureImporterCompression.Compressed;
                    // Fast rather than best: this is background scenery and the encode
                    // time across 976 textures is the whole problem being solved.
                    settings.compressionQuality = 0;
                    settings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;

                    importer.SetPlatformTextureSettings(settings);

                    EditorUtility.SetDirty(importer);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                    changed.Add(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();

            Debug.Log("[AndroidTextureBudget] Updated " + changed.Count + " of " + guids.Length
                      + " textures to " + maxSize + "px ASTC 6x6 for Android. "
                      + "Switch platform now - the Android import will use these.");

            EditorUtility.DisplayDialog("Done",
                "Updated " + changed.Count + " of " + guids.Length + " textures.\n\n"
                + "Now switch the build target to Android. The import will still take a "
                + "while, but at a fraction of the previous cost.",
                "OK");
        }

        [MenuItem("Acrophobia/Android/Report city texture cost")]
        public static void Report()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CityRoot });
            long bytes = 0;
            int overridden = 0;

            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var info = new System.IO.FileInfo(path);
                if (info.Exists) bytes += info.Length;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.GetPlatformTextureSettings(AndroidPlatform).overridden) overridden++;
            }

            Debug.Log("[AndroidTextureBudget] " + guids.Length + " textures, "
                      + (bytes / 1073741824f).ToString("F2") + " GB on disk, "
                      + overridden + " already have an Android override.");
        }
    }
}
