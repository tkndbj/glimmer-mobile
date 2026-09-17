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

        /// <summary>
        /// Prismvale's field: its gems, its lanterns, its sleeping critters and its cast.
        ///
        /// <para>
        /// <b>Its own folder rather than a shared one</b>, though every mode's cast is cut from
        /// the same licensed packs and they could have shared every address. Sharing is not free:
        /// an address two chapters ask for belongs to neither
        /// (<c>AddressableAddresses.ChapterOwnership</c>), so it would move into the global group
        /// and each mode's board art would go from chapter-scoped to resident for the whole
        /// session on every device (invariant 7b). It would also weld the modes together, and
        /// this project withdraws modes often enough that being able to delete one without
        /// touching another is worth a folder — which is exactly what deleting Budburst,
        /// Hollowmarch and Emberforge cost.
        /// </para>
        /// </summary>
        public static string PrismArt(string key) => ArtRoot + "Prism/" + key;

        /// <summary>Prismvale's flares, which live under Fx rather than beside the board.</summary>
        public static string PrismFx(string key) => ArtRoot + "Fx/Prism/" + key;

        /// <summary>
        /// Thornwatch's siege: its gems, its wards, the hill and the raiders walking down it.
        ///
        /// Its own folder, for the reason Prismvale's is its own: an address two chapters ask for
        /// belongs to neither, so sharing one would drag another mode's art out of its chapter
        /// scope and into the global group for the whole session (invariant 7b).
        /// </summary>
        public static string SiegeArt(string key) => ArtRoot + "Siege/" + key;

        /// <summary>
        /// One turret's shelf thumbnail. <c>Ui/Wards/{id}</c>.
        ///
        /// <b>Nothing draws these any more</b>, and they are kept rather than deleted. The loadout
        /// used to browse uncoloured pictures on the argument that colour is a property of the
        /// <em>seat</em> and not of the model - which is true, and which left the one screen that
        /// is supposed to be a picture of the line showing four grey turrets a player then met on
        /// the hill in red, green, blue and yellow. It draws <see cref="WardArt"/> now
        /// (see <see cref="WardShelfAssets"/>). They stay on disk and stay listed by
        /// <see cref="AllWardAssets"/> so the audit is not told eighty sprites went unused,
        /// because nothing here has been looked at on a device yet and going back is one line.
        /// </summary>
        public static string WardThumb(string id) => UiRoot + "Wards/" + id;

        /// <summary>
        /// One turret's picture in one of the line's four colours - the very sprite the board
        /// draws, at <c>Art/Siege/Wards/{id}_{c}</c>.
        ///
        /// <para>
        /// <b>A shelf may draw the real art here, and that is a fact about these pictures rather
        /// than a relaxing of invariant 16c.</b> That rule says browsing loads thumbnails because
        /// a grove cell draws at ~170 points against art cut at 512; a turret is cut at 192x240,
        /// which is smaller than the thumbnail it was being browsed with. So the cheap picture and
        /// the true one cost the same, and only one of them is what the player is choosing.
        /// </para>
        /// <para>
        /// <b>Clamped rather than refused</b>, for <c>WardLine.At</c>'s reason: a colour index out
        /// of range is a caller's slip and a turret drawn on the wrong colour is a far better
        /// answer than an <c>Image</c> with no sprite, which is a white rectangle (invariant 7b).
        /// </para>
        /// </summary>
        public static string WardArt(Wards.WardModel model, int colour)
        {
            if (model == null) return string.Empty;

            var colours = Wards.WardLine.Colours;
            int at = colour < 0 || colour >= colours.Length ? 0 : colour;

            return SiegeArt(model.ArtFor(colours[at]));
        }

        /// <summary>
        /// Every address the turret roster could ever ask for: twenty models in four colours,
        /// their recoil reels, and their shelf thumbnails.
        ///
        /// <b>For the Editor only.</b> The audit has to know these exist or it calls eighty
        /// sprites unused and then says nothing when one goes missing — which is the grove
        /// catalog's own argument (<see cref="AllGroveAssets"/>), and it has a sharper edge here:
        /// a missing turret draws a white rectangle two cells tall on the object a player looks at
        /// for a whole run. Nothing at runtime should call this — a run loads four turrets
        /// (<c>WardLine.Art</c>) and the shelf loads twenty thumbnails.
        /// </summary>
        public static List<AssetRequest> AllWardAssets(Wards.WardCatalog catalog)
        {
            var list = new List<AssetRequest>(128);
            if (catalog == null) return list;

            foreach (var model in catalog.Models)
            {
                if (model == null || string.IsNullOrEmpty(model.Id)) continue;

                list.Add(AssetRequest.Sprite(WardThumb(model.Id)));

                for (int i = 0; i < Wards.WardLine.Colours.Length; i++)
                {
                    char colour = Wards.WardLine.Colours[i];
                    list.Add(AssetRequest.Sprite(SiegeArt(model.ArtFor(colour))));
                    list.Add(AssetRequest.SpriteSet(SiegeArt(model.FireFor(colour))));

                    // A legendary wears no colour, so its four "colours" are one address
                    // (`WardModel.ArtFor`). Asked once rather than four times, because what this
                    // list feeds is an audit that would otherwise report three phantom uses.
                    if (model.Colourless) break;
                }

                // Its projectile, flash and impact, in all four ward colours. A model with no
                // ability of its own throws the shared elemental reels instead, which the mode's own
                // cast already names — asking for them here would be a second claim on an address
                // the global set owns (invariant 7b).
                if (!model.OwnShot) continue;

                for (int i = 0; i < Wards.WardLine.Colours.Length; i++)
                {
                    char colour = Wards.WardLine.Colours[i];
                    list.Add(AssetRequest.SpriteSet(SiegeFx(model.ShotFor(colour))));
                    list.Add(AssetRequest.SpriteSet(SiegeFx(model.MuzzleFor(colour))));
                    list.Add(AssetRequest.SpriteSet(SiegeFx(model.HitFor(colour))));

                    // Three reels for the whole band rather than twelve: a legendary throws no
                    // ward colour, so `WardModel.ShotFor` answers one address for all four
                    // (`Tools/make_legend_fx.py`).
                    if (model.Colourless) break;
                }
            }

            return list;
        }

        /// <summary>
        /// Every turret's picture in every ward colour, for the loadout's own scope.
        ///
        /// <para>
        /// <b>All four colours at once rather than the seat being filled</b>, which is the one
        /// decision in here. Scoping a single colour and swapping it when the player taps a
        /// different seat is a quarter of the memory and an <em>asynchronous load on a tap</em>:
        /// the grid would repaint before the sprites arrived, and an <c>Image</c> with no sprite
        /// is a white rectangle rather than a blank (invariant 7b). Eighty pictures at 192x240 is
        /// a few megabytes on a screen with no board behind it, and they leave with the scope.
        /// </para>
        /// <para>
        /// <b>Still a scope and not the global set</b>, for the reason it always was: twenty
        /// turrets resident for the life of a session to draw one screen is memory bounded by how
        /// much content exists rather than by what is on the screen.
        /// </para>
        /// </summary>
        public static List<AssetRequest> WardShelfAssets(
            IEnumerable<Wards.WardModel> models)
        {
            var list = new List<AssetRequest>(96);
            if (models == null) return list;

            foreach (var model in models)
            {
                if (model == null || string.IsNullOrEmpty(model.Id)) continue;

                for (int i = 0; i < Wards.WardLine.Colours.Length; i++)
                {
                    list.Add(AssetRequest.Sprite(WardArt(model, i)));

                    // One picture for the whole band, which is what makes a shelf of thirty cost
                    // the loadout no more than a shelf of twenty did (`WardModel.Colourless`).
                    if (model.Colourless) break;
                }
            }

            return list;
        }

        /// <summary>Thornwatch's explosions, which live under Fx rather than beside the board.</summary>
        public static string SiegeFx(string key) => ArtRoot + "Fx/Siege/" + key;

        /// <summary>
        /// A task chest's opening reel: <c>Chests/{tier}</c>, a folder of frames.
        ///
        /// Its own folder so the reels bundle apart from the global set
        /// (<c>AddressableAddresses.ChestPrefix</c>): the closed icons the hub draws are global
        /// and tiny, the reels are a couple of megabytes wanted only while a chest opens, and a
        /// bundle that carried both would be loaded whole at every launch for the sake of four
        /// small pictures.
        /// </summary>
        public static string ChestArt(string key) => ArtRoot + "Chests/" + key;

        /// <summary>
        /// Every chest tier's opening reel, for the screens that open one and for the Editor's
        /// sweeps. Read from the table rather than listed, so a fifth tier added by content is
        /// labelled, audited and loaded without anyone editing this — and a tier whose reel was
        /// never cut fails the audit rather than drawing a white rectangle over the ceremony.
        /// </summary>
        public static List<AssetRequest> ChestAssets(Tasks.TaskTable table)
        {
            var list = new List<AssetRequest>(8);
            if (table == null) return list;

            foreach (var tier in table.Tiers)
                if (tier != null && !string.IsNullOrEmpty(tier.Id))
                    list.Add(AssetRequest.SpriteSet(ArtRoot + tier.Reel));

            return list;
        }
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
        /// drops it on the way out (<c>AssetHold.Claim</c>). It is named here anyway
        /// because this is the one place that knows what the game loads — an address the
        /// manifest does not name is one the audit calls dead weight, and one the build gate
        /// cannot prove resolves.
        /// </para>
        /// <para>
        /// There was a clip beside it once — the same frame, moving, streamed from
        /// <c>StreamingAssets</c> by URL — and it is gone: a platform decoder, a display-sized
        /// texture and four megabytes of build, on the one screen built at every launch and
        /// returned to never. See <c>SplashScreen</c>.
        /// </para>
        /// </summary>
        public const string SplashBackdrop = BackdropRoot + "splash_cover";

        /// <summary>
        /// The publisher card's wordmark, baked from Orbitron by
        /// <c>Tools/make_ident_art.py</c>. White throughout, with the letters in its alpha,
        /// because the launch screen draws it twice — as the lettering, and as the mask that
        /// clips the neon sweep to it.
        ///
        /// <para>
        /// It is on the splash scope with the cover rather than in the global set: it is drawn
        /// for two seconds at launch and never again, so leaving it resident would be a
        /// full-width texture held for the life of the process for a screen nobody sees twice.
        /// </para>
        /// </summary>
        public const string IdentWord = BackdropRoot + "ident_word";

        /// <summary>What the launch screen loads, for the audit and the build gate.</summary>
        public static List<AssetRequest> SplashAssets()
            => new List<AssetRequest>
            {
                AssetRequest.Sprite(SplashBackdrop),
                AssetRequest.Sprite(IdentWord),
            };

        // ---------------------------------------------------------------- global
        static readonly string[] UiSprites =
        {
            "panel_main", "panel_soft", "frame_cream", "banner", "wood_panel", "ribbon_flat",
            "ribbon_green", "ribbon_red", "ribbon_cyan", "ribbon_orange",
            // `jelly_*` used to be here: four moulded caps the nav bar wore, and nothing else
            // in the game ever drew one. The bar wears the interface kit's caps now, so all
            // four are gone from the project rather than left addressed — an addressed sprite
            // nothing draws is still built into the bundle and still decoded at every launch.
            "star_full", "star_empty", "padlock", "badge_star", "shield",
            "btn_green", "btn_blue", "btn_orange", "btn_red", "btn_aqua", "btn_violet", "btn_gray", "btn_dark",
            "sq_green", "sq_blue", "sq_orange", "sq_aqua", "sq_gray", "sq_dark",
            "ic_home", "ic_audio", "ic_music", "ic_trophy", "ic_pause", "ic_restart", "ic_undo",
            "ic_list", "ic_info", "ic_hint", "ic_right", "ic_left", "ic_lock", "ic_star",
            "ic_check", "ic_stars", "ic_gear", "ic_play", "ic_close", "ic_plus", "ic_search",
            "ic_heart", "ic_gem", "ic_chest", "ic_chest_open", "ic_key", "ic_gift", "ic_star3d",
            "ic_profile", "ic_pencil", "ic_power", "ic_heart_boost",

            // The update wall's mark (invariant 49). **Global rather than scoped**, and
            // this is the clearest case in the list: the wall is raised over whatever
            // screen the player is standing on, by a poll that knows nothing about which
            // scopes are held, so a mark filed with any one screen would be asked for on
            // screens that do not hold it — and an `Image` with no sprite is a white
            // rectangle (invariant 7b) on the one panel a player cannot dismiss.
            "ic_update",

            // The season's crest (`SeasonCrest.Watch`, cut by `Tools/make_season_crest.py`).
            // **Global rather than scoped**, and the hub decides it: the season box is drawn on
            // the first screen after the splash, before any screen scope exists, so a crest
            // filed with the season page would be asked for by the box that opens it — and an
            // `Image` with no sprite is a white rectangle rather than a blank (invariant 7b).
            "ic_season",
            "seal_gold", "crest_gold", "bar_track", "bar_fill",

            // The three marks the Infinite lane's hub reads its lines against
            // (`make_siege_art.HUB_ICONS`). **Global rather than scoped to the mode whose lane
            // draws them**, and it is the map screen that decides it: the hub is drawn there, and
            // the chapter scope is what the map loads *for the chapter*, so an icon filed with the
            // mode would be asked for on a screen that may not hold it yet — and an `Image` with
            // no sprite is a white rectangle rather than a blank (invariant 7b). Three 96-pixel
            // tiles are not worth a scope's two failure modes.
            "ic_endless", "ic_surge", "ic_rank",
            "potion1", "potion2", "potion3", "potion4", "potion5", "potion6",

            // The action bar's three utilities. Global rather than scoped to the one mode that
            // offers them, because the bar is drawn on the board *and* in a shop panel that can
            // open over it, and three 256-pixel icons are not worth a scope's two failure modes
            // (an Image with no sprite is a white rectangle, invariant 7b).
            //
            // **Named here even though which utilities exist is content**, and that is the one
            // place the catalog's reach stops: a content push can retune a price, a strength or
            // which chest drops what, but it cannot ship a picture. So adding a *new* utility is
            // a build, exactly as adding a mode is (invariant 20), and `ContentValidation` errors
            // on a catalog entry whose icon is not one of these rather than letting it draw blank.
            "Utility/firepot", "Utility/mending", "Utility/surge", "Utility/stormcall",

            // The bar's own furniture: the shelf and one cell. Global with the icons,
            // because the bar is drawn on the board and in a shop panel that opens over it.
            "Utility/tray", "Utility/slot",

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
            // is these fourteen. See `ShopArt` for why the ladders stopped being composed,
            // why hearts did not, and why each ladder is exactly as long as its shelf.
            //
            // Global rather than a named scope, which is the same judgement `Win/*` above
            // asked for and is deliberate here rather than lazy. The shop is one tap from
            // every screen in the game, and it is the one screen where a frame of white
            // rectangles while a scope loads (invariant 7b) costs actual money.
            "Shop/pouch",
            "Shop/coins_1", "Shop/coins_2", "Shop/coins_3", "Shop/coins_4",
            "Shop/gems_1", "Shop/gems_2", "Shop/gems_3",
            "Shop/gems_4", "Shop/gems_5", "Shop/gems_6",
            "Shop/bundles_1", "Shop/bundles_2", "Shop/bundles_3",

            // The interface kit every screen's chrome is made of, cut by
            // `Tools/make_hud_kit_art.py`. Global rather than scoped, and that is a judgement
            // rather than laziness: this is what a *screen* is built out of, so it is wanted by
            // the first screen after the splash and by every screen after that. A scope would
            // spend a frame loading on every navigation a player makes, and what an `Image`
            // with no sprite draws while it waits is a white rectangle (invariant 7b) — here
            // that would be the whole hub.
            //
            // The pill and square controls are *not* on this list and do not need to be: they
            // are re-cuts of `Ui/btn_*` and `Ui/sq_*`, which the block above already names. See
            // `Skins` for why the names did not move.
            "Hud/rail_top", "Hud/rail_bottom",
            "Hud/panel", "Hud/card", "Hud/slot",
            "Hud/trough", "Hud/fill", "Hud/title",
            "Hud/cap_on", "Hud/cap_off",
            "Hud/add", "Hud/burst", "Hud/btn_gold",
            "Hud/plate_blue", "Hud/plate_orange", "Hud/plate_violet", "Hud/plate_gold",
            "Hud/plate_navy",
            "ic_nav_home", "ic_nav_shop", "ic_nav_grove",
            "ic_nav_ranks", "ic_nav_profile", "ic_battle", "ic_chest_wood", "ic_streak", "ic_padlock",
            "Hud/lander", "Hud/beam", "Hud/room",

            // The two painted banners, each drawn on the kit's blue plate and each the whole
            // of the control it is (`HomeScreen.BuildChallenges`, `ProfileScreen.BuildInviteCard`).
            // **Global for the same reason as the line above it** — one is on the first screen
            // after the splash and the other is one tap from every screen in the game, and an
            // `Image` whose sprite has not arrived is a white rectangle rather than a blank
            // (invariant 7b), which here would be a 900-unit white bar across a card.
            //
            // Both are 2048x768 sources capped to 1024 by the `/Art/Ui/` folder rule, so the
            // pair costs about 350 KB resident at ASTC 6x6 — which is what makes global the
            // cheap answer rather than the lazy one.
            "challenges", "refer",

            // The task chests, closed, and the goal glyphs the shared icon set has no picture
            // for. Global for the streak flame's reason: the hub draws all four chests on the
            // first screen after the splash, and an `Image` whose sprite has not arrived is a
            // white rectangle (invariant 7b). The *opening* reels are not here — they are a
            // scope of their own (ChestAssets), because sixty-eight frames are only ever
            // wanted while a chest is being opened.
            "Chest/wood", "Chest/silver", "Chest/gold", "Chest/royal",
            "Task/raiders", "Task/boss", "Task/charm", "Task/cog", "Task/wave",
        };

        /// <summary>
        /// Map furniture used by every chapter, unlike the strips themselves.
        ///
        /// <para>
        /// <b>Nine of the ten <c>rock_*</c> perches are gone from here and still on disk.</b> A
        /// node stands on the painting now rather than on a floating tile of its own, and a
        /// preloaded picture nothing draws is resident memory for the life of the game — which
        /// is the cost that matters and the one this pays. The PNGs, their <c>.meta</c> guids
        /// and their Addressables rows are kept until the change has been seen on a device.
        /// </para>
        /// <para>
        /// <b><c>rock_grass</c> stays, because one map still moors.</b> `map1` is an
        /// archipelago and half its chain crosses open water on the current the painting
        /// draws, so those nodes keep a tile under them (<c>LevelsScreen.PerchArt</c>). It is
        /// global rather than a chapter's, exactly as the rest of this row is: the map's
        /// furniture is wanted by whichever chapter is open, and an <c>Image</c> whose sprite
        /// has not arrived is a white rectangle (invariant 7b) — over the sea, on the first
        /// chapter of the game.
        /// </para>
        /// </summary>
        static readonly string[] MapSprites =
        {
            "node_open", "node_lock", "node_s0", "node_s1", "node_s2", "node_s3", "pointer",
            "rock_grass",
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

            // The hub and the storefront's own room, drawn rather than composed of layers —
            // hence one name where the others come in threes. See `Scenery.Room`.
            "hub_room",

            // And the quiet ground behind every screen that is a list rather than a place —
            // the storefront, the boards, the profile, the grove's shop and the tasks page.
            // See `Scenery.Plain`.
            "plain",
        };

        static readonly string[] Sfxs =
        {
            "click", "back", "menu", "tip", "enter", "poke", "wheel", "collect", "reward", "coin", "rotate_a", "rotate_b", "blocked",
            "unlock", "shatter", "burst", "free", "pop", "pop2", "whoosh", "chest", "win", "star",
            "tick", "tock", "bell", "lit", "chime", "chime2",
            "boom", "mend", "land",
            "boss", "roar", "felled",
            "gem", "settle", "shot", "zap", "stand", "wear", "arrive",
            "lift", "stow", "chain",
        };

        /// <summary>Everything the game needs before the menu appears.</summary>
        public static List<AssetRequest> GlobalAssets()
        {
            var list = new List<AssetRequest>(256);

            for (int i = 1; i <= LevelGridParser.CritterVariants; i++)
                list.Add(AssetRequest.SpriteSet($"{ArtRoot}Critters/c{i}"));

            list.Add(AssetRequest.SpriteSet($"{ArtRoot}Fx/Victory"));

            // **The retired Budburst asked for no explosion art at all, and that is the third
            // answer to it.** Two cuts from a licensed VFX pack were shipped and thrown away —
            // the first took the pack's shader utility maps by mistake, the second was a correct
            // cut of a smoke plume and still read as dust on a puzzle grid. The whole set is
            // generated (`Art.Flash`, `Wave`, `Glint`, `Bolt`), so there is nothing here to
            // preload, nothing to address, nothing in the bundle and no frame where an Image is a
            // white rectangle because the art had not arrived. See the explosions block in `Art`.
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
        /// into a hold when a roster screen opens and
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
        /// <see cref="AssetHold.LoadAsync"/> leaves an address that is already
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
        /// in it. See <c>GroveArtLoader.Tabs</c> for what that cost when the two
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

            // A piece that can be *turned* draws its facings out of a folder, exactly as an
            // animated one draws its frames — so it is a sprite set for the same reason and
            // with a sharper edge: a folder of frames has no notion of being a folder, it is
            // addressed by a label its frames share, and one that never gets that label is
            // addressed, audited green and completely unloadable (invariant 37at, which cost a
            // device build of raiders with no bodies). Asking here is what puts it in
            // `AddressableAddresses.FrameFolders`.
            bool folder = piece.Animated || piece.Facings > 1;
            add(folder ? AssetRequest.SpriteSet(address) : AssetRequest.Sprite(address));
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

            // Art the *mode* draws, asked once for the chapter rather than once per level.
            // Every mode but one answers with nothing, so this adds no request to any chapter
            // that shipped before it existed. Asked of the first level's mode because a chapter
            // is one mode by construction - `ChapterModeValidator` errors on any that is not,
            // and a chapter that somehow held two would simply load the first one's cast rather
            // than crashing here.
            if (chapter.Levels.Count > 0)
            {
                var mode = Content.LevelModes.Find(chapter.Levels[0].Mode);
                if (mode != null)
                    foreach (var request in mode.ArtFor(chapter))
                        if (seen.Add(request.Address)) list.Add(request);
            }

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
