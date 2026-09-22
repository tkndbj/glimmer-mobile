using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Progression;
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
            EnsureSpritePacking();
            ContentValidation.ValidateMenu();
            ValidateArt();
            Debug.Log("[Glimmer] project setup complete");
        }

        /// <summary>
        /// Turns Sprite Atlas V2 on, because the grove's shop is drawn out of atlases.
        ///
        /// <para>
        /// A project setting rather than something the atlas generator turns on for itself,
        /// for <c>m_BuildAddressablesWithPlayerBuild</c>'s reason: it has to be the same on CI
        /// and on every teammate's machine, and a per-machine preference is how two people get
        /// different builds out of the same commit. It ships in
        /// <c>ProjectSettings/EditorSettings.asset</c>.
        /// </para>
        /// <para>
        /// <b>Enabled, not "Enabled for Builds".</b> The build-only mode leaves sprites
        /// resolving from their source textures in the Editor, so <c>SpriteAtlas.GetSprite</c>
        /// returns nothing in play mode — the shop would be an empty grid in the Editor and
        /// correct on the device, which is the worst possible way round. It was
        /// <see cref="SpritePackerMode.Disabled"/> here, which produces no
        /// <c>SpriteAtlas</c> artifact at all: the <c>.spriteatlas</c> files import as editor
        /// data and nothing can load them.
        /// </para>
        /// </summary>
        static void EnsureSpritePacking()
        {
            if (EditorSettings.spritePackerMode == SpritePackerMode.SpriteAtlasV2)
            {
                Debug.Log("[Glimmer] sprite packing is already Sprite Atlas V2");
                return;
            }

            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
            AssetDatabase.SaveAssets();
            Debug.Log("[Glimmer] sprite packing switched to Sprite Atlas V2; the grove's shop " +
                      "atlases cannot be loaded without it");
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
            expected.AddRange(AssetManifest.ChestAssets(ProgressionRules.Table.Tasks));

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

            // The import rules as well as the addresses. An asset can be present, addressed and
            // loadable and still be three times the texture memory it should be, which no other
            // check here can see — see ArtImportRules.Audit.
            var drift = ArtImportRules.Audit();
            foreach (var d in drift) Debug.LogError("[Glimmer] " + d);
            if (drift.Count == 0) Debug.Log("[Glimmer] art import rules: every texture agrees");
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
    /// most about 500 screen pixels on a 1080-wide phone — a floor tile is 220 wide and a
    /// piece is drawn at about 1.15 art pixels per screen pixel — so 512 is the honest
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
            ("/Art/Companions/", 512),  // portraits, drawn at 320
            ("/Art/Critters/", 256),    // flipbook frames, drawn small and there are many
            ("/Art/Prism/", 512),       // Prismvale: its floor, its gems, its lanterns and its cast
            // **The hill before the rest of the folder, because the loop takes the first match.**
            // A ground is not a prop: it is the single biggest thing on a siege screen, drawn about
            // 1190 across on a phone, and at the folder's own 512 it imported at 410x512 and was
            // blown up 2.55x - which is half of what "the tiles look low quality" was (the other
            // half was the stretch, see `SiegeView.Ground`). A chapter holds ten of these resident
            // at once (`SiegeMode.ArtFor`), which is why this is 1024 and not a backdrop's 2048.
            ("/Art/Siege/hill", 1024),
            ("/Art/Siege/", 512),       // Thornwatch: its ward line, its gems and the raid
            ("/Art/Fx/", 512),          // explosions, drawn at ~2 cells and mostly soft
            // **Before the rest of the folder, because the loop takes the first match** - the
            // hill's rule, one folder over. A rank badge is drawn at 96 in the map's chrome and
            // at 132 down the ranks page, so 256 is already a comfortable margin; at the
            // folder's own 1024 the seven of them would be 3.3 MB of resident global art for
            // pictures nothing ever draws larger than a thumb. The PNGs are cut at 256 by
            // `Tools/make_rank_art.py`, so this rule binds nothing today and is what stops a
            // re-cut at source size shipping quietly (invariant 7d).
            ("/Art/Ui/Rank/", 256),

            ("/Art/Ui/", 1024),

            // A name frame is the one piece of UI art drawn at the whole width of a card and
            // sold on how it looks, so it keeps the backdrop's cap rather than the UI folder's
            // — written down rather than left to the default below, because "unlisted" is not
            // a decision anybody can read. One is resident at a time (`AssetManifest.FrameRoot`).
            ("/Art/Frames/", 2048),
        };

        internal static int CapFor(string path)
        {
            foreach (var cap in Caps)
                if (path.Contains(cap.Folder)) return cap.Max;

            return 2048;                // backdrops and map strips, which really are large
        }

        /// <summary>
        /// Folder under <c>Art/</c> to the compression grade it imports at. Anything unlisted
        /// takes <see cref="TextureImporterCompression.Compressed"/>.
        ///
        /// <para>
        /// <b>A grade is a block size, and the block is absolute rather than relative to the
        /// texture.</b> On Android with ASTC pinned (<c>DevBuild.PinTextureCompression</c>),
        /// <c>Compressed</c> is ASTC 6x6 at 3.56 bpp and <c>CompressedHQ</c> is ASTC 4x4 at 8
        /// bpp — so a 6x6 block eats a far larger share of a 256-pixel flipbook frame than of a
        /// 2048 backdrop. That is why the two small folders below are graded up while the large
        /// ones are not: it is the *cap* that decides, not the subject.
        /// </para>
        /// <para>
        /// <b>Measured, never argued</b> (44b), with
        /// <c>Tools/compare_texture_formats.py --measure --contact</c>, source against ASTC,
        /// worst file per folder: backdrops 49.5 dB, Ui/Hud 52.2, Fx/Victory 44.3, Map 43.7, Ui
        /// 43.0, Chests 42.3 — all at or above the 41.1 dB the turrets already ship at and were
        /// judged clean on a phone. <c>Companions</c> came out at 39.5 and <c>Critters</c> at
        /// <b>33.8</b>, visibly blockier on the contact sheet, and both are small and
        /// soft-edged. At 4x4 they measure 48–56 dB, which is effectively lossless, and the two
        /// folders together are 19 MB — so the grade costs about 2 MB against the alternative of
        /// shipping the one folder anybody could fault.
        /// </para>
        /// </summary>
        static readonly (string Folder, TextureImporterCompression Grade)[] Grades =
        {
            // **A stencil is not a picture, and it is the one thing on this list that cannot
            // be compressed at any grade.** The publisher card's wordmark is drawn twice — once
            // as a Unity `Mask` whose graphic is never painted, so the shape lives entirely in
            // its alpha (`StudioIdent`) — and uGUI's mask clips at an alpha of 0.001, which is
            // to say *any* non-zero texel is a hole in the sheet. Block compression does not
            // round a transparent texel to transparent: measured through
            // `astc-encoder`, this file comes back with **3,555** lit texels outside the
            // lettering at ASTC 6x6, 2,612 at 5x5 and still **467** at 4x4 — every one of them
            // a pinprick of the neon sweep showing through the black, inside the counters of
            // the O and the D and scattered round the word. Reported, correctly, as the mark
            // being corrupted or speckled with dots. There is no grade that fixes it, because
            // the fault is not that the alpha is *approximate* — it is that the threshold it is
            // read against is zero. 1,800x161 at RGBA32 is 1.16 MB, on a scope the launch
            // screen drops when it goes (`SplashScreen._art`).
            //
            // **A file rather than a folder, for `/Art/Siege/hill`'s reason** — the loop takes
            // the first match, so this must stand ahead of any rule for `/Art/Bg/`. Nothing
            // else in that folder is a mask.
            ("/Art/Bg/ident_word", TextureImporterCompression.Uncompressed),

            ("/Art/Critters/", TextureImporterCompression.CompressedHQ),
            ("/Art/Companions/", TextureImporterCompression.CompressedHQ),

            // **The worst-measuring folder in the game, and it is not close.** The rank badges
            // are faceted gemstones: saturated, high-contrast, and made almost entirely of the
            // hard specular edges block compression is least able to hold.
            // `compare_texture_formats.py --measure` puts them at **30.3 dB** RGB at ASTC 6x6,
            // against 39.4 for the turrets and 33.8 for `Critters`, which was already judged
            // visibly blocky on a contact sheet. At 4x4 they come back at 36.4. Seven textures
            // capped at 256 is 448 KB at 4x4 against 200 KB at 6x6, so the grade costs a quarter
            // of a megabyte for the one picture on the map a player is invited to look at.
            ("/Art/Ui/Rank/", TextureImporterCompression.CompressedHQ),
        };

        internal static TextureImporterCompression GradeFor(string path)
        {
            foreach (var grade in Grades)
                if (path.Contains(grade.Folder)) return grade.Grade;

            return TextureImporterCompression.Compressed;
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

                if (Disagrees(importer, path)) stale.Add(path);
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
                    importer.textureCompression = GradeFor(path);
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

        /// <summary>
        /// Whether this texture's importer has drifted from the rules above.
        ///
        /// One predicate, shared by <see cref="Reapply"/> and <see cref="Audit"/> on purpose:
        /// a repair and the gate that proves the repair happened must not be able to disagree
        /// about what "correct" is, or the gate goes green on a file the repair skips.
        /// </summary>
        static bool Disagrees(TextureImporter importer, string path)
            => importer.maxTextureSize != CapFor(path)
            || importer.textureCompression != GradeFor(path);

        /// <summary>
        /// Proves no texture under <c>Art/</c> ships against the rules, and is wired into the
        /// build gate rather than left to a menu item.
        ///
        /// <para>
        /// <b>This is invariant 7a's second half.</b> The preprocessor makes the fault
        /// unlikely; it cannot make it impossible, because a preprocessor fires on first import
        /// only — so art that landed before a rule changed keeps what it was given, silently,
        /// and silence is exactly how 175 MB of uncompressed textures came to be resident on
        /// every device. Making an error unlikely is not proving it did not happen.
        /// </para>
        /// <para>
        /// An <b>error</b> rather than a warning, and the repair is one menu item, because the
        /// two ways this fails are a build too large for Play's ceiling and a process the OS
        /// kills after a video ad. Neither announces itself, and neither is visible in a
        /// screenshot, a compile or a content validation.
        /// </para>
        /// <para>
        /// Deliberately scoped to <c>Assets/Game/Art</c>, so anything generated under
        /// <c>Assets/Game/Generated</c> is outside this walk and outside the preprocessor's.
        /// </para>
        /// </summary>
        public static List<string> Audit()
        {
            var errors = new List<string>();
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Game/Art" });
            int drifted = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                if (!Disagrees(importer, path)) continue;

                drifted++;

                // Only the first few by name. A rule change touches hundreds at once, and a
                // console with four hundred identical errors in it is one nobody reads to the
                // end — the count and the repair are what the reader actually needs.
                if (drifted <= 5)
                    errors.Add($"art import rules: '{path}' imports at " +
                               $"{importer.maxTextureSize}/{importer.textureCompression} " +
                               $"where the rule says {CapFor(path)}/{GradeFor(path)}");
            }

            if (drifted > 5)
                errors.Add($"art import rules: {drifted - 5} further texture(s) disagree, not listed");

            if (drifted > 0)
                errors.Add($"art import rules: {drifted} of {guids.Length} texture(s) disagree with " +
                           "the folder rules; run Glimmer Grove ▸ Reapply Art Import Rules");

            return errors;
        }

        void OnPreprocessTexture()
        {
            var p = assetPath.Replace('\\', '/');
            if (!p.Contains("/Game/Resources/Art/") && !p.Contains("/Game/Art/")) return;
            var ti = (TextureImporter)assetImporter;

            // The size cap is applied even to a texture already marked as a sprite, because
            // that is the case this rule exists for: art imported before the cap existed.
            ti.maxTextureSize = CapFor(p);

            // **And the grade, for the same reason and a worse history.** Everything below the
            // early return is skipped for a texture Unity already calls a Sprite, which is most
            // of them — so for as long as compression was set down there it was set on almost
            // nothing, and 382 files kept whatever their `.meta` happened to carry. What they
            // carried was `Uncompressed`: 6% of the art library holding 49% of its texture
            // memory, 175 MB where ASTC wants 20. `DevBuild.PinTextureCompression` could never
            // have caught it either — pinning the *format family* to ASTC does nothing to a
            // texture that has asked not to be compressed at all.
            ti.textureCompression = GradeFor(p);

            if (ti.textureType == TextureImporterType.Sprite) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
        }
    }
}
