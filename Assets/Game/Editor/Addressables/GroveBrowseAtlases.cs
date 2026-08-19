#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Homestead;
using GlimmerGrove.Progression;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Builds the grove's browse atlases: one texture per shelf, holding a small copy of
    /// everything sold on it.
    ///
    /// <para>
    /// <b>What this is for.</b> A shop grid draws forty pictures from forty textures, and a
    /// texture each is a draw call each — the grid was about forty batches, and it would have
    /// been four hundred. One atlas per shelf makes it one, whatever the catalog grows to.
    /// </para>
    /// <para>
    /// <b>Why it packs copies rather than the shipped art.</b> This is the decision worth not
    /// re-litigating. A sprite may belong to exactly one atlas, and a sprite that belongs to
    /// one stops having a texture of its own — so packing the real pieces would mean the grove
    /// screen, which draws at most a couple of dozen of them at full size, could no longer load
    /// one without dragging in every other piece on its shelf. That is precisely the bound the
    /// grove's scope exists to hold (invariant 7b), and it is the bound that matters most,
    /// because it is the one that grows with the catalog rather than with the screen. Packing
    /// generated thumbnails keeps the two questions separate: browsing costs one shelf's
    /// thumbnails, and an island costs the pieces standing on it.
    /// </para>
    /// <para>
    /// <b>And why the thumbnails are copies rather than resized pixels.</b> A copy plus a
    /// <c>maxTextureSize</c> is a downscale done by Unity's own importer, with its filtering,
    /// its colour space and its alpha handling — none of which a hand-rolled blit gets right
    /// for free. The sources here are mostly under 300 pixels, so most thumbnails are the
    /// original at the original size; what the cap does is stop a 512-pixel portrait being
    /// packed at four times the area a 170-point cell can show.
    /// </para>
    /// <para>
    /// Everything here is <b>derived from the catalog and the roster</b>. Nothing is hand
    /// listed, so a drop that adds twenty pieces is twenty rows of content and a re-run —
    /// which is the same bargain <c>AssetManifest</c> makes and the reason
    /// <c>import_grove_art.py</c> prints this step.
    /// </para>
    /// </summary>
    public static class GroveBrowseAtlases
    {
        /// <summary>Where the generated copies live: outside <c>Art/</c>, so nothing addresses them.</summary>
        public const string ThumbRoot = "Assets/Game/Generated/GroveThumbs/";

        /// <summary>Where the atlases live. Addressed, and in the grove's own bundle.</summary>
        public const string AtlasRoot = "Assets/Game/Art/Grove/";

        /// <summary>
        /// The largest a browse thumbnail may be imported at.
        ///
        /// A cell draws at 170 points, which is about 230 physical pixels on the densest phone
        /// this ships to, so 256 is one step of headroom and no more. Doubling it would
        /// quadruple every atlas page for a sharpness nobody can see.
        /// </summary>
        public const int ThumbMax = 256;

        /// <summary>A tab's emblem is drawn at 64 points, so it needs a fraction of that.</summary>
        public const int TabMax = 128;

        [MenuItem("Glimmer Grove/Addressables/Rebuild Grove Atlases", false, 42)]
        public static void Rebuild()
        {
            // Without V2 packing a .spriteatlas imports as editor data and produces no
            // SpriteAtlas at all, so every atlas written below would be unloadable and the shop
            // would draw an empty grid. Checked here as well as in Set Up Project, because this
            // is the step somebody runs after a drop on a machine that may never have run that
            // one — and the failure is silent everywhere else.
            if (UnityEditor.EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
            {
                Debug.LogError("[Glimmer] sprite packing is '" +
                               UnityEditor.EditorSettings.spritePackerMode +
                               "'; the grove's atlases need Sprite Atlas V2 (Enabled). Run " +
                               "Glimmer Grove ▸ Set Up Project, then this again.");
                return;
            }

            var content = EditorContentLoader.Load();
            var catalog = content.Homestead;

            if (catalog == null || catalog.PieceCount == 0)
            {
                Debug.LogWarning("[Glimmer] no grove catalog; nothing to pack");
                return;
            }

            var plan = Plan(catalog);

            int copied = 0, removed = 0;

            // One import pass for the lot. Per-file SaveAndReimport in a loop is what crashed
            // both import workers and wedged the Editor in a domain reload it could not
            // finish — see ArtImportRules.Reapply. The finally is mandatory.
            AddressableAutoRegister.Suspended = true;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var bucket in plan)
                {
                    Directory.CreateDirectory(FolderOf(bucket.Key));

                    foreach (var thumb in bucket.Value)
                        if (Copy(thumb, bucket.Key)) copied++;

                    removed += Prune(bucket.Key, bucket.Value);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AddressableAutoRegister.Suspended = false;
            }

            AssetDatabase.Refresh();

            // Import settings after the files are in, because a texture has to exist before it
            // has an importer — and in one pass again, for the same reason.
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var bucket in plan)
                    foreach (var thumb in bucket.Value)
                        Configure(PathOf(bucket.Key, thumb.Name),
                                  bucket.Key == TabBucket ? TabMax : ThumbMax);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();

            int atlases = 0;
            foreach (var bucket in plan)
                if (WriteAtlas(bucket.Key, bucket.Value)) atlases++;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SpriteAtlasUtility.PackAllAtlases(EditorUserBuildSettings.activeBuildTarget);

            Debug.Log($"[Glimmer] grove atlases: {atlases} packed, {copied} thumbnail(s) written, " +
                      $"{removed} orphan(s) removed. Run Addressables ▸ Sync All Assets if the " +
                      "atlases are new.");
        }

        // ----------------------------------------------------------------- audit
        /// <summary>
        /// Proves every shelf's atlas actually holds a picture for everything sold on it.
        ///
        /// <para>
        /// <b>Why an address resolving is not enough.</b> These atlases are generated, so the
        /// failure they are exposed to is not a missing file but a <em>step nobody ran</em> —
        /// a drop lands twenty pieces, the catalog and the loc strings and the addresses are
        /// all correct, and the shop draws twenty blank plates because the atlas is the one
        /// from last month. Every other check in this project passes on that build. So this
        /// asks the question the player asks: is there a picture for this piece.
        /// </para>
        /// <para>
        /// It is an <b>error</b>, not a warning, and it runs from <c>Validate Art</c> — which
        /// is in the build gate — for invariant 7a's reason: making a mistake unlikely is not
        /// the same as proving it did not happen, and this project has already shipped one
        /// registration step that quietly rotted into a no-op.
        /// </para>
        /// </summary>
        public static void Audit(HomesteadCatalog catalog)
        {
            if (catalog == null || catalog.PieceCount == 0) return;

            int checkedCount = 0, missing = 0;

            foreach (var shelf in GroveShelves.All)
            {
                if (!GroveShelves.HasAtlas(shelf)) continue;

                string key = GroveShelves.Key(shelf);
                var atlas = AtlasAt(key);

                if (atlas == null)
                {
                    Debug.LogError($"[Glimmer] the grove's '{key}' shelf has no browse atlas at " +
                                   AtlasPath(key) + "; run Addressables ▸ Rebuild Grove Atlases");
                    missing++;
                    continue;
                }

                foreach (var piece in catalog.Pieces)
                {
                    if (GroveShelves.Of(piece) != shelf) continue;

                    checkedCount++;
                    if (atlas.GetSprite(piece.Id) != null) continue;

                    Debug.LogError($"[Glimmer] '{piece.Id}' is on the grove's '{key}' shelf and " +
                                   "its browse atlas has no picture for it; run Addressables ▸ " +
                                   "Rebuild Grove Atlases");
                    missing++;
                }
            }

            var tabs = AtlasAt(TabBucket);
            if (tabs == null)
            {
                Debug.LogError("[Glimmer] the grove shop's tab atlas is missing at " +
                               AtlasPath(TabBucket));
                missing++;
            }
            else
            {
                foreach (var shelf in GroveShelves.All)
                {
                    if (!GroveShelves.HasAtlas(shelf)) continue;

                    // A shelf with nothing on it has no emblem to draw, which the content
                    // validator already warns about — this is only about the atlas being stale.
                    if (!HomesteadCatalog.Emblem(catalog, shelf).IsValid) continue;

                    checkedCount++;
                    if (tabs.GetSprite(GroveShelves.Key(shelf)) != null) continue;

                    Debug.LogError($"[Glimmer] the grove shop's '{GroveShelves.Key(shelf)}' tab " +
                                   "has no emblem in the tab atlas; run Addressables ▸ Rebuild " +
                                   "Grove Atlases");
                    missing++;
                }
            }

            Debug.Log(missing == 0
                ? $"[Glimmer] grove browse atlases: all {checkedCount} picture(s) present"
                : $"[Glimmer] grove browse atlases: {missing} picture(s) missing of {checkedCount}");
        }

        static string AtlasPath(string bucket) => AtlasRoot + "thumbs_" + bucket + ".spriteatlasv2";

        static SpriteAtlas AtlasAt(string bucket)
            => AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath(bucket));

        // ------------------------------------------------------------------ plan
        /// <summary>One thumbnail to make: what to copy, and what to call it.</summary>
        public readonly struct Thumb
        {
            /// <summary>The name the atlas will answer to. A piece id, or a shelf key.</summary>
            public readonly string Name;

            /// <summary>The art key, relative to <c>Art/</c>, this is a copy of.</summary>
            public readonly string Art;

            public Thumb(string name, string art) { Name = name; Art = art; }
        }

        /// <summary>The bucket holding the tab row's emblems. Not a shelf: see <see cref="Plan"/>.</summary>
        public const string TabBucket = "tabs";

        /// <summary>
        /// What every atlas should contain, derived from the catalog.
        ///
        /// <para>
        /// A resident's thumbnail is its <b>portrait</b> even when the piece itself is drawn as
        /// a flipbook on the island. A folder of frames has no single picture to pack, and a
        /// browse grid wants a still one anyway — the portrait and the flipbook are the same
        /// creature, so nothing about the shop disagrees with the island.
        /// </para>
        /// <para>
        /// The tab row gets its own bucket holding a second copy of eight emblems, because a
        /// sprite may belong to only one atlas and a tab has to be drawn before its shelf has
        /// been loaded — which is the whole point of a tab.
        /// </para>
        /// </summary>
        public static Dictionary<string, List<Thumb>> Plan(HomesteadCatalog catalog)
        {
            var plan = new Dictionary<string, List<Thumb>>();
            if (catalog == null) return plan;

            foreach (var shelf in GroveShelves.All)
                if (GroveShelves.HasAtlas(shelf)) plan[GroveShelves.Key(shelf)] = new List<Thumb>();

            plan[TabBucket] = new List<Thumb>();

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsValid) continue;

                string art = BrowseArtOf(piece);
                if (string.IsNullOrEmpty(art)) continue;

                plan[GroveShelves.Key(GroveShelves.Of(piece))].Add(new Thumb(piece.Id, art));
            }

            foreach (var shelf in GroveShelves.All)
            {
                if (!GroveShelves.HasAtlas(shelf)) continue;

                var emblem = HomesteadCatalog.Emblem(catalog, shelf);
                if (!emblem.IsValid) continue;

                string art = BrowseArtOf(emblem);
                if (!string.IsNullOrEmpty(art))
                    plan[TabBucket].Add(new Thumb(GroveShelves.Key(shelf), art));
            }

            return plan;
        }

        /// <summary>The still picture a piece browses as. See <see cref="Plan"/>.</summary>
        public static string BrowseArtOf(HomesteadPiece piece)
        {
            if (!piece.IsResident) return piece.Art;

            var companion = GroveResidents.CompanionOf(piece);
            return companion.IsValid
                ? GroveResidents.PortraitFolder + companion.Portrait
                : string.Empty;
        }

        // ------------------------------------------------------------------ files
        static string FolderOf(string bucket) => ThumbRoot + bucket + "/";

        static string PathOf(string bucket, string name) => FolderOf(bucket) + name + ".png";

        static string SourceOf(string art) => "Assets/Game/Art/" + art + ".png";

        /// <summary>
        /// Copies one source into the thumbnail folder, if it is not already there and current.
        ///
        /// Compared by content length and time rather than blindly re-copied, so a re-run after
        /// a drop that changed nothing writes nothing and dirties no importer — which is what
        /// keeps this safe to print as a routine step.
        /// </summary>
        static bool Copy(Thumb thumb, string bucket)
        {
            string source = SourceOf(thumb.Art);
            string destination = PathOf(bucket, thumb.Name);

            if (!File.Exists(source))
            {
                Debug.LogError($"[Glimmer] grove atlas: '{thumb.Name}' names art '{thumb.Art}', " +
                               $"which is not at {source}");
                return false;
            }

            if (File.Exists(destination) &&
                File.GetLastWriteTimeUtc(destination) >= File.GetLastWriteTimeUtc(source) &&
                new FileInfo(destination).Length == new FileInfo(source).Length)
                return false;

            File.Copy(source, destination, true);
            return true;
        }

        /// <summary>
        /// Deletes thumbnails for pieces the catalog no longer has.
        ///
        /// Safe in a way removing a piece id is not: a thumbnail is a picture, not a name in
        /// anybody's save file, so nothing is lost by regenerating one — which is exactly why
        /// the generated copies live apart from the art they came from.
        /// </summary>
        static int Prune(string bucket, List<Thumb> wanted)
        {
            string folder = FolderOf(bucket);
            if (!Directory.Exists(folder)) return 0;

            var keep = new HashSet<string>();
            foreach (var thumb in wanted) keep.Add(thumb.Name);

            int removed = 0;
            foreach (string path in Directory.GetFiles(folder, "*.png"))
            {
                if (keep.Contains(Path.GetFileNameWithoutExtension(path))) continue;

                AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
                removed++;
            }

            return removed;
        }

        static void Configure(string path, int max)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            // Compared before writing, so a re-run after a drop that changed nothing dirties
            // no importer and queues no reimport — which is what makes this safe as a routine
            // step rather than a several-minute one.
            bool changed = importer.textureType != TextureImporterType.Sprite
                        || importer.spriteImportMode != SpriteImportMode.Single
                        || importer.maxTextureSize != max
                        || importer.mipmapEnabled
                        || !importer.alphaIsTransparency
                        || importer.filterMode != FilterMode.Bilinear
                        || importer.textureCompression != TextureImporterCompression.Uncompressed;

            if (!changed) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = max;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;

            // Uncompressed, because this is the *source* the packer reads: compressing here
            // would be decompressed into the atlas and compressed again, which is one round of
            // artefacts for nothing. The atlas is what ships, and the atlas is compressed.
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            importer.SaveAndReimport();
        }

        // ----------------------------------------------------------------- atlases
        static bool WriteAtlas(string bucket, List<Thumb> thumbs)
        {
            Directory.CreateDirectory(AtlasRoot);

            // ".spriteatlasv2", not ".spriteatlas" — see AtlasPath. The extension is what
            // selects the importer: the old one expects a V1 atlas, so a file written in the V2
            // format imports as editor data with a plain AssetImporter and no SpriteAtlas is
            // ever produced. Every address still resolves and every check still passes; the
            // shop simply draws an empty grid.
            string path = AtlasPath(bucket);

            var sprites = new List<Object>(thumbs.Count);
            foreach (var thumb in thumbs)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PathOf(bucket, thumb.Name));
                if (sprite != null) sprites.Add(sprite);
            }

            if (sprites.Count == 0)
            {
                // A shelf with nothing on it is a content state, not a fault — a catalog can
                // legitimately have no paths yet. An empty atlas is still written, so the
                // address resolves and the screen draws an empty grid rather than nothing.
                Debug.LogWarning($"[Glimmer] grove atlas '{bucket}' has no art");
            }

            // Written from scratch rather than diffed against what is there. The atlas is
            // derived from the catalog, so what it held before is never the question — and a
            // packable left behind by a piece that was renamed is exactly the kind of thing a
            // diff quietly keeps. The importer settings live in the .meta and survive this.
            var asset = new SpriteAtlasAsset();

            // Named for its file, or Unity warns that the main object does not match the
            // filename on every single import — nine warnings per run, which is how a console
            // stops being worth reading.
            asset.name = Path.GetFileNameWithoutExtension(path);

            asset.SetIsVariant(false);
            asset.Add(sprites.ToArray());

            SpriteAtlasAsset.Save(asset, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as SpriteAtlasImporter;
            if (importer != null)
            {
                importer.packingSettings = new SpriteAtlasPackingSettings
                {
                    padding = 4,
                    enableRotation = false,
                    enableTightPacking = false,
                    enableAlphaDilation = true,
                };

                importer.textureSettings = new SpriteAtlasTextureSettings
                {
                    readable = false,
                    generateMipMaps = false,
                    sRGB = true,
                    filterMode = FilterMode.Bilinear,
                };

                importer.includeInBuild = true;
                importer.SaveAndReimport();
            }

            return true;
        }
    }
}

#endif
