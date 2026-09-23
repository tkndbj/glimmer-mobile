using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Challenges;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Every picture a challenge draws, and where it comes from.
    ///
    /// <para>
    /// <b>A challenge cuts no art of its own.</b> The gems, the four starter turrets, their bolt
    /// reels, the insects and the first hill are all the live mode's, resident through the same
    /// hold the tutorial takes (<c>SiegeMode.ArtFor(null)</c>), so nothing here can be a white
    /// rectangle that the siege itself would not also be (invariant 7b). Everything else — a
    /// pipe, a pad, a wall, a mine's number — is procedural (<c>Art</c>).
    /// </para>
    /// <para>
    /// <b>Names are written out at the call, one per line</b>, so <c>artnames.py</c> can hold
    /// every one of them to disk. The raider reels are the one built address, and they are
    /// built by <c>SiegeMode.CastAddress</c> — the same one copy the siege draws from.
    /// </para>
    /// </summary>
    public static class ChallengeArt
    {
        public const string Hold = "challenge";

        public static Color Tint(int colour) => SiegeView.TintOf(colour);

        /// <summary>
        /// The picture a genre's card wears: <c>Ui/challenge_{spelling}</c>, derived from the
        /// spelling (7c's shape) and cut by <c>Tools/make_challenge_art.py</c> from the owner's
        /// artwork. In the global set (<c>AssetManifest.UiSprites</c>), because the list page
        /// is one tap from the hub. Null for a spelling this build has no picture for, which the
        /// card draws as nothing rather than as a white rectangle (7b).
        /// </summary>
        public static Sprite GenreMark(ChallengeGenre genre)
            => AssetLibrary.Sprite(AssetManifest.Ui(GenreMarkKey(genre)));

        /// <summary>The address under <c>Ui/</c>, for the preload test to hold to the manifest.</summary>
        public static string GenreMarkKey(ChallengeGenre genre) => "challenge_" + ChallengeGenres.NameOf(genre);

        static Sprite Piece(string key) => AssetLibrary.Sprite(AssetManifest.SiegeArt(key));
        static Sprite[] Reel(string key) => AssetLibrary.Frames(AssetManifest.SiegeArt(key));
        static Sprite[] Blast(string key) => AssetLibrary.Frames(AssetManifest.SiegeFx(key));

        public static Sprite Gem(int colour)
        {
            switch (colour)
            {
                case 0: return Piece("gem_r");
                case 1: return Piece("gem_g");
                case 2: return Piece("gem_b");
                case 3: return Piece("gem_y");
                default: return null;
            }
        }

        /// <summary>The starter turret in a colour: the one line every challenge is fought on.</summary>
        public static Sprite Ward(int colour)
        {
            switch (colour)
            {
                case 0: return Piece("Wards/bolt_r");
                case 1: return Piece("Wards/bolt_g");
                case 2: return Piece("Wards/bolt_b");
                default: return Piece("Wards/bolt_y");
            }
        }

        public static Sprite[] Fire(int colour)
        {
            switch (colour)
            {
                case 0: return Reel("Wards/bolt_r_fire");
                case 1: return Reel("Wards/bolt_g_fire");
                case 2: return Reel("Wards/bolt_b_fire");
                default: return Reel("Wards/bolt_y_fire");
            }
        }

        public static Sprite[] Shot(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("shot_bolt_r");
                case 1: return Blast("shot_bolt_g");
                case 2: return Blast("shot_bolt_b");
                default: return Blast("shot_bolt_y");
            }
        }

        public static Sprite[] Muzzle(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("muzzle_bolt_r");
                case 1: return Blast("muzzle_bolt_g");
                case 2: return Blast("muzzle_bolt_b");
                default: return Blast("muzzle_bolt_y");
            }
        }

        public static Sprite[] Hit(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("hit_bolt_r");
                case 1: return Blast("hit_bolt_g");
                case 2: return Blast("hit_bolt_b");
                default: return Blast("hit_bolt_y");
            }
        }

        /// <summary>A creeper of the first chapter's cast, in a colour. The one built address.</summary>
        public static Sprite[] Raider(int colour)
            => AssetLibrary.Frames(SiegeMode.CastAddress(SiegeMode.Insects, SiegeKind.Creeper, colour));

        public static Sprite Ground() => Piece("hill1");
        public static Sprite Rampart() => Piece("rampart");
        public static Sprite Socket() => Piece("socket");
        public static Sprite Plate() => Piece("plate");
        public static Sprite WardDown() => Piece("ward_dead");

        /// <summary>
        /// What a challenge holds: the siege with no chapter behind it, which is the insects
        /// and the starter line, the field's gems and the shared effects.
        /// </summary>
        public static System.Collections.Generic.List<AssetRequest> Requests()
        {
            var list = new System.Collections.Generic.List<AssetRequest>();
            var mode = LevelModes.Find(GameMode.Siege);
            if (mode != null) list.AddRange(mode.ArtFor(null));
            return list;
        }
    }
}
