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
            expected.AddRange(AssetManifest.CompanionAssets(content.Index.Companions));
            expected.AddRange(AssetManifest.AllGroveAssets(content.Homestead));

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
    ///
    /// <para>
    /// <b>The size cap is per folder, and that is the whole point of it.</b> A texture's
    /// memory is its dimensions, not its file size: one 2048 sprite costs about 16 MB
    /// uncompressed however few pixels of it are painted, so a catalog of a hundred props
    /// imported at the default would be a bundle nobody can ship. The grove's art draws at
    /// most about 500 screen pixels on a 1080-wide phone — see <c>HomesteadMap</c>, where an
    /// island is drawn at roughly 1.27 art pixels per screen pixel — so 512 is the honest
    /// ceiling and anything above it is paying for detail the screen cannot show.
    /// </para>
    /// <para>
    /// Re-runnable by hand from <c>Glimmer Grove ▸ Reapply Art Import Rules</c>, because a
    /// preprocessor only fires on first import: art that landed before a rule changed keeps
    /// whatever it was given, silently, which is exactly the sort of thing that is invisible
    /// until a build is too big.
    /// </para>
    /// </summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        /// <summary>Folder under <c>Art/</c> to the largest texture it may import at.</summary>
        static readonly (string Folder, int Max)[] Caps =
        {
            ("/Art/Homestead/", 512),   // props on an island, ~500px at most
            ("/Art/Companions/", 512),  // portraits, drawn at 320
            ("/Art/Critters/", 256),    // flipbook frames, drawn small and there are many
            ("/Art/Ui/", 1024),
        };

        internal static int CapFor(string path)
        {
            foreach (var cap in Caps)
                if (path.Contains(cap.Folder)) return cap.Max;

            return 2048;                // backdrops and map strips, which really are large
        }

        /// <summary>
        /// Re-imports every art texture whose size cap has drifted from the rule above.
        ///
        /// <para>
        /// A preprocessor fires on first import only, so a cap tightened after the art landed
        /// changes nothing until somebody touches the file — silently, which is exactly the
        /// sort of thing nobody notices until a build is too big. This walks the folders and
        /// re-imports only what actually disagrees, so it is safe to run after any drop and
        /// costs nothing when there is nothing to do.
        /// </para>
        /// <para>
        /// <b>The batch is one import pass, and that is not a nicety.</b> The first version
        /// called <c>SaveAndReimport</c> per texture inside the loop, which is a separate
        /// round trip to Unity's import worker processes each time; three hundred of them back
        /// to back crashed both workers and left the Editor wedged in a domain reload it could
        /// never finish. <c>StartAssetEditing</c>/<c>StopAssetEditing</c> queues the whole set
        /// and imports it once, and the <c>finally</c> is mandatory — an exception between the
        /// two leaves the asset database permanently in editing mode, which looks exactly like
        /// the freeze it is meant to prevent.
        /// </para>
        /// </summary>
        [MenuItem("Glimmer Grove/Reapply Art Import Rules", false, 22)]
        public static void Reapply()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Game/Art" });
            var stale = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                if (importer.maxTextureSize != CapFor(path)) stale.Add(path);
            }

            if (stale.Count == 0)
            {
                Debug.Log($"[Glimmer] art import rules: {guids.Length} texture(s) already correct");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var path in stale)
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.maxTextureSize = CapFor(path);
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Glimmer] art import rules: {stale.Count} of {guids.Length} texture(s) re-imported");
        }

        void OnPreprocessTexture()
        {
            var p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Game/Resources/Art/") && !p.Contains("/Game/Art/")) return;
            var ti = (TextureImporter)assetImporter;

            // The size cap is applied even to a texture already marked as a sprite, because
            // that is the case this rule exists for: art imported before the cap existed.
            ti.maxTextureSize = CapFor(p);

            if (ti.textureType == TextureImporterType.Sprite) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
        }
    }
}
