using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// Declares what to load, and when.
    ///
    /// The global list is hand-written because it genuinely is fixed: buttons,
    /// icons, critters and the font are the same in every chapter forever, and a
    /// list is the clearest way to say so.
    ///
    /// The chapter list is *derived from the catalog* and must never become a
    /// hand-written list. That is the difference that matters: the previous build
    /// hardcoded "play_0, play_1, play_2" in the splash screen, which meant every
    /// content drop required somebody to remember to edit a screen. Now a chapter
    /// declares its own art and this reads it back, so publishing chapter forty
    /// touches no code at all.
    /// </summary>
    public static class AssetManifest
    {
        public const string ArtRoot = "Art/";
        public const string BackdropRoot = ArtRoot + "Bg/";
        public const string MapRoot = ArtRoot + "Map/";
        public const string UiRoot = ArtRoot + "Ui/";
        public const string CompanionRoot = ArtRoot + "Companions/";
        public const string SfxRoot = "Audio/Sfx/";
        public const string MusicRoot = "Audio/Music/";
        public const string FontAddress = "Fonts/GameFont";

        public static string Companion(string key) => CompanionRoot + key;

        public static string Backdrop(string key) => BackdropRoot + key;
        public static string MapArt(string key) => MapRoot + key;
        public static string Ui(string key) => UiRoot + key;
        public static string Sfx(string key) => SfxRoot + key;
        public static string Music(string key) => MusicRoot + key;

        // ---------------------------------------------------------------- splash
        /// <summary>
        /// The launch screen's picture, and the clip it is the first frame of.
        ///
        /// <para>
        /// <b>Deliberately not in <see cref="GlobalAssets"/>.</b> That list is what the game
        /// must hold for the whole session; this is a full-screen texture for the one screen
        /// nobody ever returns to, so the launch screen claims it into a scope of its own and
        /// drops it on the way out (<c>AssetLibrary.SplashScope</c>). It is named here anyway
        /// because this is the one place that knows what the game loads — an address the
        /// manifest does not name is one the audit calls dead weight, and one the build gate
        /// cannot prove resolves.
        /// </para>
        /// <para>
        /// The video is not an address at all. It is read from <c>StreamingAssets</c> by URL,
        /// because it has to be playable before the asset pipeline has been started; it is
        /// named beside its poster so the two cannot drift apart.
        /// </para>
        /// </summary>
        public const string SplashBackdrop = BackdropRoot + "splash_cover";

        /// <summary>The clip, relative to <c>StreamingAssets</c>. See <see cref="SplashBackdrop"/>.</summary>
        public const string SplashVideoFile = "Video/splash.mp4";

        /// <summary>What the launch screen loads, for the audit and the build gate.</summary>
        public static List<AssetRequest> SplashAssets()
            => new List<AssetRequest> { AssetRequest.Sprite(SplashBackdrop) };

        // ---------------------------------------------------------------- global
        static readonly string[] UiSprites =
        {
            "panel_main", "panel_soft", "frame_cream", "banner", "wood_panel", "ribbon_flat",
            "ribbon_green", "ribbon_red", "ribbon_cyan", "ribbon_orange",
            "jelly_gray", "jelly_green", "jelly_teal", "jelly_orange",
            "star_full", "star_empty", "padlock", "badge_star", "shield",
            "btn_green", "btn_blue", "btn_orange", "btn_red", "btn_aqua", "btn_violet", "btn_gray", "btn_dark",
            "sq_green", "sq_blue", "sq_orange", "sq_aqua", "sq_gray", "sq_dark",
            "ic_home", "ic_audio", "ic_music", "ic_trophy", "ic_pause", "ic_restart", "ic_undo",
            "ic_list", "ic_info", "ic_hint", "ic_right", "ic_left", "ic_lock", "ic_star",
            "ic_check", "ic_stars", "ic_gear", "ic_play", "ic_close", "ic_plus", "ic_search",
            "ic_heart", "ic_gem", "ic_chest", "ic_chest_open", "ic_key", "ic_gift", "ic_star3d",
            "ic_profile", "ic_pencil", "ic_power", "ic_heart_boost",
            "seal_gold", "crest_gold", "bar_track", "bar_fill",
            "potion1", "potion2", "potion3", "potion4", "potion5", "potion6",

            // The victory crest, drawn by WinOverlay. Declared here rather than left to the
            // on-demand path for two reasons: the audit is how this project proves no address
            // is unaccounted for, and a sprite first requested during a celebration is a
            // synchronous load at the exact moment nothing should stutter.
            //
            // Global rather than a named scope, which is the judgement invariant 7b asks for:
            // four small sprites shared by no chapter and reachable from any win, exactly like
            // the panel and ribbon art above it. Two of the pack's regions are deliberately
            // absent from the project entirely: the "VICTORY" lettering, because a word painted
            // into a texture cannot be translated (invariant 6), and the herald's horn, because
            // the crest reads better without it and an addressed sprite nothing draws is still
            // built into the bundle and preloaded at every launch.
            "Win/crown", "Win/shield", "Win/banner", "Win/window",

            // The storefront's two money ladders — one painted picture per rung — plus the
            // pouch, which is only the coins tab's glyph. Every other card is still composed
            // from art already on this list: a heart pack and a heart container out of the
            // game's own heart and three of the potion bottles, so the shop's whole art order
            // is these twelve. See `ShopArt` for why the ladders stopped being composed and
            // why hearts did not.
            //
            // Global rather than a named scope, which is the same judgement `Win/*` above
            // asked for and is deliberate here rather than lazy. The shop is one tap from
            // every screen in the game, and it is the one screen where a frame of white
            // rectangles while a scope loads (invariant 7b) costs actual money.
            "Shop/pouch",
            "Shop/coins_1", "Shop/coins_2", "Shop/coins_3",
            "Shop/coins_4", "Shop/coins_5", "Shop/coins_6",
            "Shop/gems_1", "Shop/gems_2", "Shop/gems_3",
            "Shop/gems_4", "Shop/gems_5", "Shop/gems_6",
        };

        /// <summary>Map furniture used by every chapter, unlike the strips themselves.</summary>
        static readonly string[] MapSprites =
        {
            "node_open", "node_lock", "node_s0", "node_s1", "node_s2", "node_s3", "pointer",
            "rock_grass", "rock_tall", "rock_wide", "rock_chip", "rock_plain",
            "rock_sand", "rock_palm", "rock_wood", "rock_lumen", "rock_basin",
            "palm", "boulder", "stump", "boat", "post",
        };

        /// <summary>
        /// Backdrops that belong to screens rather than to any chapter.
        ///
        /// The <c>streak_*</c> trio is the grove after dark — the same islands the hub
        /// stands on, lit by a moon. The <c>event_*</c> trio is the ground those islands
        /// float above, at first light: a different place rather than a third grade of the
        /// same one, because two re-lights of one landscape is a mood and three is a filter.
        /// Both are global rather than scoped like chapter art, for the reason the flame is:
        /// a fixed handful of files that does not grow with the catalog, on pages one tap off
        /// the hub, where a scope would spend a frame loading on a navigation players make
        /// daily.
        /// </summary>
        static readonly string[] ScreenBackdrops =
        {
            "grove_far", "grove_near", "grove_light",
            "home_sky", "home_ground", "home_deco",
            "map_sky", "map_ground", "map_deco",
            "streak_sky", "streak_ground", "streak_deco",
            "event_sky", "event_ground", "event_deco",
        };

        static readonly string[] Sfxs =
        {
            "click", "back", "coin", "rotate_a", "rotate_b", "blocked", "unlock", "shatter",
            "pop", "pop2", "whoosh", "chest", "win", "star", "tick", "tock", "bell",
            "lit", "chime", "chime2",
        };

        /// <summary>Everything the game needs before the menu appears.</summary>
        public static List<AssetRequest> GlobalAssets()
        {
            var list = new List<AssetRequest>(256);

            for (int i = 1; i <= LevelGridParser.CritterVariants; i++)
                list.Add(AssetRequest.SpriteSet($"{ArtRoot}Critters/c{i}"));

            list.Add(AssetRequest.SpriteSet($"{ArtRoot}Fx/Victory"));
            list.Add(AssetRequest.SpriteSet($"{UiRoot}Coin"));

            // The streak flame. Global rather than scoped for the reason the coin is: it is
            // drawn on the hub, which is the first screen after the splash, so a scope
            // would be created and never released and would only add a frame to the one
            // navigation nobody can avoid.
            list.Add(AssetRequest.SpriteSet($"{UiRoot}Flame"));

            foreach (var b in ScreenBackdrops) list.Add(AssetRequest.Sprite(Backdrop(b)));
            foreach (var u in UiSprites) list.Add(AssetRequest.Sprite(Ui(u)));
            foreach (var m in MapSprites) list.Add(AssetRequest.Sprite(MapArt(m)));
            foreach (var s in Sfxs) list.Add(AssetRequest.Clip(Sfx(s)));

            list.Add(AssetRequest.Font(FontAddress));
            return list;
        }

        // The streak page used to bring its own set with it: a camp of isometric blocks,
        // one sprite per night plus a clearing to stand them on, derived from the ladder
        // length so a retune would not need code. It is gone with the scene it drew — the
        // board is built from the same jelly squares and glyphs the rest of the UI uses,
        // which is one fewer set of art to keep in step with the reward table, and the
        // sprites it did need are in UiSprites above where the audit can see them.

        // ------------------------------------------------------------ companions
        /// <summary>
        /// Every companion portrait, for the screens that show the whole roster.
        ///
        /// Deliberately <em>not</em> part of <see cref="GlobalAssets"/>. A portrait is
        /// about 45 KB, which is nothing until the roster is a hundred strong and every
        /// one of them is decoded at every launch to be looked at on one screen. Loaded
        /// into <see cref="AssetLibrary.CompanionScope"/> when a roster screen opens and
        /// dropped when it closes, which is the same bargain chapter art makes.
        ///
        /// Derived from the roster, never hand-listed — a companion added by a content
        /// drop is loadable without anyone editing this file.
        /// </summary>
        public static List<AssetRequest> CompanionAssets(IEnumerable<AvatarDefinition> roster)
        {
            var list = new List<AssetRequest>(32);
            if (roster == null) return list;

            var seen = new HashSet<string>();
            foreach (var companion in roster)
            {
                if (!companion.IsValid) continue;
                if (seen.Add(companion.Portrait)) list.Add(AssetRequest.Sprite(Companion(companion.Portrait)));
            }

            return list;
        }

        /// <summary>
        /// The one companion the player is wearing, for the hub and the profile hero.
        ///
        /// Its animated set is requested when it has one, because the worn companion is
        /// the single place the game can afford a flipbook — and it is already global
        /// art, since board critters use the same sets.
        /// </summary>
        public static List<AssetRequest> WornCompanionAssets(AvatarDefinition companion)
        {
            var list = new List<AssetRequest>(2);
            if (!companion.IsValid) return list;

            list.Add(AssetRequest.Sprite(Companion(companion.Portrait)));
            if (companion.HasAnimation) list.Add(AssetRequest.SpriteSet($"{ArtRoot}Critters/{companion.Animated}"));
            return list;
        }

        // ------------------------------------------------------------- homestead
        /// <summary>
        /// Every plot and every piece of decor the grove can draw.
        ///
        /// <para>
        /// Derived from the catalog, never hand-listed — the rule this file exists to state.
        /// A drop that adds twenty decor pieces is twenty rows in <c>homestead.json</c> and
        /// no code at all, which is the same bargain <see cref="ChapterAssets"/> makes and
        /// the reason the splash screen no longer names backdrops.
        /// </para>
        /// <para>
        /// Residents are included and cost nothing: their art keys point at
        /// <c>Art/Critters/</c>, which <see cref="GlobalAssets"/> already warmed, and
        /// <see cref="AssetLibrary.EnsureScopeAsync"/> leaves an address that is already
        /// global exactly where it is. Asking for them anyway is what keeps this method a
        /// statement about the catalog rather than a statement about which folder a piece's
        /// art happens to sit in today.
        /// </para>
        /// </summary>
        /// <summary>
        /// Every address the grove could ever ask for.
        ///
        /// <b>For the Editor only.</b> The build gate has to prove that every piece in the
        /// catalog is addressable and present, which is a question about the catalog rather
        /// than about any one screen — <c>AddressableAudit</c> and <c>Validate Art</c> both
        /// ask it. Nothing at runtime should call this: loading the whole catalog to draw one
        /// screen is the thing the split below exists to stop.
        /// </summary>
        public static List<AssetRequest> AllGroveAssets(Homestead.HomesteadCatalog catalog)
        {
            var list = new List<AssetRequest>(128);
            if (catalog == null) return list;

            var seen = new HashSet<string>();

            void Add(AssetRequest request)
            {
                if (!string.IsNullOrEmpty(request.Address) && seen.Add(request.Address))
                    list.Add(request);
            }

            AddFloor(catalog, Add);

            foreach (var piece in catalog.Pieces) AddPiece(piece, Add);

            return list;
        }

        /// <summary>
        /// What the grove <em>screen</em> needs: the islands, the home ladder, and whatever
        /// the player has actually put down.
        ///
        /// <para>
        /// <b>Bounded by the grove, not by the catalog.</b> This used to be every piece that
        /// exists, which was fine at forty and is wrong at four hundred: opening the
        /// Grovement would load the whole shop to draw a screen showing at most one piece per
        /// slot. The islands and the home ladder are unavoidable — they are always on screen —
        /// but everything else here is a function of <paramref name="placed"/>, so the cost of
        /// this screen is the size of the player's grove and stays there however large the
        /// catalog grows. That is the difference between a feature that scales with content
        /// and one that scales with the shop.
        /// </para>
        /// <para>
        /// The whole home ladder rather than the rung in use, because it is five sprites and
        /// buying one has to redraw the house in the same frame — see
        /// <c>HomesteadLedger.BestDwelling</c>.
        /// </para>
        /// </summary>
        public static List<AssetRequest> GroveAssets(Homestead.HomesteadCatalog catalog,
                                                     IEnumerable<string> placed)
        {
            var list = new List<AssetRequest>(48);
            if (catalog == null) return list;

            var seen = new HashSet<string>();

            void Add(AssetRequest request)
            {
                if (!string.IsNullOrEmpty(request.Address) && seen.Add(request.Address))
                    list.Add(request);
            }

            AddFloor(catalog, Add);

            foreach (var piece in catalog.Pieces)
                if (piece.IsDwelling) AddPiece(piece, Add);

            if (placed != null)
                foreach (var id in placed)
                    AddPiece(catalog.Find(id), Add);

            return list;
        }

        /// <summary>
        /// One shelf's worth of art for a screen that <em>browses</em>: a shop tab, or the
        /// picker's list for one slot.
        ///
        /// <para>
        /// <b>This is a thumbnail atlas and one sprite per tab, not the shelf's real art.</b>
        /// A browse grid draws a piece at about 170 points; the piece itself is a 512-pixel
        /// texture cut for an island. Loading the real thing to fill a grid means paying, per
        /// tab, sixteen times the pixels the screen can show — and then paying it again in
        /// draw calls, because forty separate textures cannot batch. One atlas per shelf is
        /// therefore both the memory answer and the batching answer, and it is the reason a
        /// shelf is a concept at all (see <c>GroveShelf</c>): the tab, the atlas and this scope
        /// are three mechanisms that have to agree, so they are keyed on one division.
        /// </para>
        /// <para>
        /// The tab row is drawn from the emblem of every shelf, so all eight atlases would be
        /// wanted at once if the tabs used piece art — they do not; a tab draws its emblem out
        /// of the <em>thumbnail</em> atlas it belongs to, and the row therefore costs one extra
        /// sprite per shelf rather than eight atlases. Which is why the emblem sweep that used
        /// to live here is gone.
        /// </para>
        /// </summary>
        public static List<AssetRequest> GroveShelfAssets(Homestead.GroveShelf shelf)
            => new List<AssetRequest>(1)
            {
                AssetRequest.Atlas(BrowseAtlas(Homestead.GroveShelves.HasAtlas(shelf)
                                                   ? shelf
                                                   : Homestead.GroveShelf.Ground)),
            };

        /// <summary>
        /// The tab row's eight emblems, packed together.
        ///
        /// <para>
        /// Their own tiny atlas rather than eight shelf atlases, because a tab has to be drawn
        /// before it is chosen and a row of blank plates is a row nobody can navigate by
        /// (invariant 7b) — and pulling in every shelf to draw eight little pictures would undo
        /// the whole point of paging.
        /// </para>
        /// <para>
        /// <b>Its own request list rather than a line in <see cref="GroveShelfAssets"/>, which
        /// is where it used to be.</b> A shelf's assets are swapped whenever the shelf changes
        /// and the emblems are not — they belong to the row, which outlives every shelf shown
        /// in it. See <c>AssetLibrary.HomesteadTabScope</c> for what that cost when the two
        /// shared a lifetime.
        /// </para>
        /// </summary>
        public static List<AssetRequest> GroveTabAssets()
            => new List<AssetRequest>(1) { AssetRequest.Atlas(TabAtlas) };

        /// <summary>
        /// What the picker draws: every shelf, because every tile of the floor takes everything.
        ///
        /// It used to be two — the slot's own kind and the residents — back when a slot had a
        /// role. Still bounded by the number of shelves rather than by the size of the catalog,
        /// which is the property paging exists to hold, and these are thumbnail pages rather
        /// than shelves of real art.
        /// </summary>
        public static List<AssetRequest> GrovePickerAssets()
        {
            var list = new List<AssetRequest>(9);
            foreach (var shelf in Homestead.GroveShelves.All)
                if (Homestead.GroveShelves.HasAtlas(shelf))
                    list.Add(AssetRequest.Atlas(BrowseAtlas(shelf)));

            return list;
        }

        /// <summary>The tab row's emblems, packed as one. See <see cref="GroveShelfAssets"/>.</summary>
        public static readonly string TabAtlas = GroveRoot + "thumbs_tabs";

        /// <summary>
        /// The address of a shelf's browse atlas: one texture holding every thumbnail on it.
        ///
        /// <para>
        /// Under <c>Art/Grove/</c> rather than <c>Art/Homestead/</c> because it is
        /// <em>generated</em> — rebuilt from the catalog by an Editor step and audited by the
        /// build gate, never edited by hand — and because the residents' shelf packs companion
        /// portraits, which live in a different folder and a different bundle from anything
        /// under Homestead. A generated asset in the same folder as the art it was generated
        /// from is a file somebody eventually edits.
        /// </para>
        /// </summary>
        public static string BrowseAtlas(Homestead.GroveShelf shelf)
            => GroveRoot + "thumbs_" + Homestead.GroveShelves.Key(shelf);

        /// <summary>Where the grove's generated art lives. See <see cref="BrowseAtlas"/>.</summary>
        public const string GroveRoot = ArtRoot + "Grove/";

        /// <summary>
        /// Every browse atlas there is, for the build gate and the Editor's generator.
        ///
        /// <b>Not for runtime.</b> Nothing on a device should hold all of these at once — that
        /// is the thing paging exists to prevent — which is why the two runtime lists above ask
        /// for two apiece.
        /// </summary>
        public static List<AssetRequest> AllBrowseAtlases()
        {
            var list = new List<AssetRequest>(9) { AssetRequest.Atlas(TabAtlas) };

            foreach (var shelf in Homestead.GroveShelves.All)
                if (Homestead.GroveShelves.HasAtlas(shelf))
                    list.Add(AssetRequest.Atlas(BrowseAtlas(shelf)));

            return list;
        }

        /// <summary>One piece's art, for a screen claiming a single thing it has just drawn.</summary>
        public static List<AssetRequest> PieceAssets(Homestead.HomesteadPiece piece)
        {
            var list = new List<AssetRequest>(1);
            AddPiece(piece, list.Add);
            return list;
        }

        /// <summary>
        /// The ground itself: one tile sprite for the whole field.
        ///
        /// <b>One address however large the floor is</b>, which is the quiet win of a tile field
        /// over the islands it replaced - ten islands were ten textures that grew with the
        /// chapter list, where a thousand tiles are one texture drawn a thousand times. A floor
        /// that names no art draws a generated diamond instead, so the grove is never a screenful
        /// of white rectangles while art is still being cut (invariant 7b).
        /// </summary>
        static void AddFloor(Homestead.HomesteadCatalog catalog, System.Action<AssetRequest> add)
        {
            string art = catalog?.Floor?.TileArt;
            if (!string.IsNullOrEmpty(art)) add(AssetRequest.Sprite(ArtRoot + art));
        }

        static void AddPiece(Homestead.HomesteadPiece piece, System.Action<AssetRequest> add)
        {
            if (!piece.IsValid || string.IsNullOrEmpty(piece.Art)) return;

            string address = ArtRoot + piece.Art;
            add(piece.Animated ? AssetRequest.SpriteSet(address) : AssetRequest.Sprite(address));
        }

        // --------------------------------------------------------------- chapter
        /// <summary>
        /// Art owned by one chapter: its map strips, its backdrop, and any backdrop a
        /// level inside it overrides. Read from the content, never hand-listed.
        ///
        /// It takes a loaded <see cref="ChapterBody"/> rather than a catalog because a
        /// chapter's art is only ever needed at the moment its body is read — both are
        /// scoped to entering that chapter, and asking for a body here would hide a
        /// file read inside a method that looks like a lookup.
        /// </summary>
        public static List<AssetRequest> ChapterAssets(ChapterBody chapter)
        {
            var list = new List<AssetRequest>(8);
            if (chapter == null) return list;

            var definition = chapter.Definition;
            var seen = new HashSet<string>();

            void AddSprite(string address)
            {
                if (!string.IsNullOrEmpty(address) && seen.Add(address))
                    list.Add(AssetRequest.Sprite(address));
            }

            foreach (var strip in definition.MapStrips) AddSprite(MapArt(strip));
            AddSprite(Backdrop(definition.Backdrop));

            foreach (var level in chapter.Levels)
                AddSprite(Backdrop(level.Presentation.ResolveBackdrop(definition)));

            return list;
        }

        /// <summary>Every chapter's art, for the Editor's completeness check.</summary>
        public static List<AssetRequest> AllChapterAssets(IEnumerable<ChapterBody> chapters)
        {
            var list = new List<AssetRequest>();
            if (chapters == null) return list;

            foreach (var chapter in chapters)
                list.AddRange(ChapterAssets(chapter));

            return list;
        }
    }
}
