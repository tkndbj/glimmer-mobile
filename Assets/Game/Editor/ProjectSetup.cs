using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.AssetPipeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// One-shot project wiring. The game builds its own scene graph at runtime, so
    /// the only thing the scene has to do is exist and be first in the build list.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Game/Scenes/Glimmer.unity";

        [MenuItem("Glimmer Grove/Set Up Project", false, 1)]
        public static void Setup()
        {
            EnsureScene();
            EnsureBuildSettings();
            ContentValidation.ValidateMenu();
            ValidateArt();
            Debug.Log("[Glimmer] project setup complete");
        }

        static void EnsureScene()
        {
            var dir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(ScenePath))
            {
                Debug.Log("[Glimmer] scene already present");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Glimmer] created " + ScenePath);
        }

        static void EnsureBuildSettings()
        {
            var scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = scenes;
            Debug.Log("[Glimmer] build settings point at " + ScenePath);
        }

        /// <summary>
        /// Checks that every asset the game asks for actually exists on disk.
        ///
        /// Existing on disk and being loadable are different things: this proves the
        /// file is there, and Addressables ▸ Audit Addresses proves the game can reach
        /// it. Both run from the build gate, because a file with no address is exactly
        /// as missing as a file that was never drawn.
        ///
        /// The list of expected assets comes from <see cref="AssetManifest"/> and the
        /// catalog — it used to be a hand-typed array here, which meant a content drop
        /// could add a backdrop that nothing ever checked for. It also searches by
        /// address rather than through Resources, so it keeps working after the
        /// Addressables migration moves the files.
        /// </summary>
        [MenuItem("Glimmer Grove/Validate Art", false, 21)]
        public static void ValidateArt()
        {
            var content = EditorContentLoader.Load();

            var expected = AssetManifest.GlobalAssets();
            expected.AddRange(AssetManifest.AllChapterAssets(content.Bodies));

            var present = IndexAssetsByAddress();
            var missing = new List<string>();

            foreach (var request in expected)
            {
                bool found = request.Kind == AssetKind.SpriteSet
                    ? present.Exists(p => p.StartsWith(request.Address + "/", StringComparison.Ordinal))
                    : present.Contains(request.Address);

                if (!found) missing.Add($"{request.Kind}: {request.Address}");
            }

            foreach (var m in missing) Debug.LogError("[Glimmer] missing asset " + m);

            Debug.Log(missing.Count == 0
                ? $"[Glimmer] all {expected.Count} expected asset(s) present"
                : $"[Glimmer] {missing.Count} of {expected.Count} expected asset(s) missing");
        }

        /// <summary>
        /// Every art, audio and font asset under Assets/Game, keyed by the address the
        /// game would use. Location-independent on purpose: assets live under
        /// Resources before the migration and elsewhere after it.
        /// </summary>
        static List<string> IndexAssetsByAddress()
        {
            var addresses = new List<string>();
            var guids = AssetDatabase.FindAssets("t:Texture2D t:AudioClip t:Font", new[] { "Assets/Game" });

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (string.IsNullOrEmpty(path)) continue;

                int cut = -1;
                foreach (var root in new[] { "/Art/", "/Audio/", "/Fonts/" })
                {
                    int i = path.LastIndexOf(root, StringComparison.Ordinal);
                    if (i > cut) cut = i;
                }
                if (cut < 0) continue;

                string address = path.Substring(cut + 1);
                int dot = address.LastIndexOf('.');
                if (dot > 0) address = address.Substring(0, dot);

                addresses.Add(address);
            }
            return addresses;
        }
    }

    /// <summary>
    /// Safety net: every texture under the game's art folders is a UI sprite, whatever
    /// the meta file happens to say. Matches both the pre-migration Resources location
    /// and the Addressables one, so the rule survives the move.
    /// </summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Game/Resources/Art/") && !p.Contains("/Game/Art/")) return;
            var ti = (TextureImporter)assetImporter;
            if (ti.textureType == TextureImporterType.Sprite) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
        }
    }
}
