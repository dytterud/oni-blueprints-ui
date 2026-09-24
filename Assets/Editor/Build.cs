using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlueprintsUi.Editor
{
    /// <summary>
    /// Batch-mode entry point: rebuild the prefabs from spec/, then build the blueprints_ui bundle
    /// for all three platforms into out/{windows,mac,linux}/. build.ps1 calls this.
    /// </summary>
    public static class Build
    {
        public const string BundleName = "blueprints_ui";

        static readonly (BuildTarget Target, string Folder)[] Targets =
        {
            (BuildTarget.StandaloneWindows64, "windows"),
            (BuildTarget.StandaloneOSX, "mac"),
            (BuildTarget.StandaloneLinux64, "linux"),
        };

        [MenuItem("Blueprints UI/Build prefabs and bundles")]
        public static void All()
        {
            try
            {
                AssetDatabase.ImportAsset("Assets/Sprites", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
                PrefabBuilder.BuildAll();
                foreach (string p in PrefabBuilder.Problems)
                    Debug.LogError($"[BlueprintsUi] {p}");
                if (PrefabBuilder.Problems.Count > 0)
                    throw new Exception($"{PrefabBuilder.Problems.Count} problem(s) building prefabs; see above");
                Bundles();
                Debug.Log("[BlueprintsUi] build succeeded");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BlueprintsUi] build failed: {e}");
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        static void Bundles()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabBuilder.OutputDir }))
                AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)).assetBundleName = BundleName;
            AssetDatabase.SaveAssets();

            foreach (var (target, folder) in Targets)
            {
                string staging = Path.Combine("Build", folder);
                Directory.CreateDirectory(staging);
                // BuildAssetBundleOptions.None is LZMA, one compressed block: the format the mod
                // has always shipped.
                var manifest = BuildPipeline.BuildAssetBundles(staging, BuildAssetBundleOptions.None, target);
                if (manifest == null)
                    throw new Exception($"BuildAssetBundles failed for {target} - is its Build Support module installed?");

                string output = Path.Combine("out", folder);
                Directory.CreateDirectory(output);
                File.Copy(Path.Combine(staging, BundleName), Path.Combine(output, BundleName), true);
                Debug.Log($"[BlueprintsUi] {target} -> {output}/{BundleName}");
            }
        }
    }
}
