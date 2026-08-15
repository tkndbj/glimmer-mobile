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
        };

        /// <summary>Map furniture used by every chapter, unlike the strips themselves.</summary>
        static readonly string[] MapSprites =
        {
            "node_open", "node_lock", "node_s0", "node_s1", "node_s2", "node_s3", "pointer",
            "rock_grass", "rock_tall", "rock_wide", "rock_chip", "rock_plain",
            "rock_sand", "rock_palm", "rock_wood",
            "palm", "boulder", "stump", "boat", "post",
        };

        /// <summary>
        /// Backdrops that belong to screens rather than to any chapter.
        ///
        /// The <c>streak_*</c> trio is the grove after dark — the same islands the hub
        /// stands on, lit by a moon. Global rather than scoped like chapter art for the
        /// reason the flame is: three files is a fixed cost that does not grow with the
        /// catalog, and the page is one tap off the hub, so a scope would spend a frame
        /// loading on a navigation players make daily.
        /// </summary>
        static readonly string[] ScreenBackdrops =
        {
            "grove_far", "grove_near", "grove_light", "splash_far",
            "home_sky", "home_ground", "home_deco",
            "map_sky", "map_ground", "map_deco",
            "streak_sky", "streak_ground", "streak_deco",
        };

        static readonly string[] Sfxs =
        {
            "click", "back", "press", "rotate_a", "rotate_b", "blocked", "unlock", "shatter",
            "pop", "pop2", "whoosh", "chest", "win", "star", "tick", "tock", "bell",
            "lit", "nope", "chime", "chime2",
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
