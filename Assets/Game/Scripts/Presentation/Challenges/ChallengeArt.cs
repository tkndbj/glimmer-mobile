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
    /// <b>A challenge borrows the live mode's art, and Pairs and Push alone cut their own.</b> The gems,
    /// the four starter turrets, their bolt reels, the insects and the first hill are all the
    /// live mode's, resident through the same hold the tutorial takes
    /// (<c>SiegeMode.ArtFor(null)</c>), so nothing there can be a white rectangle that the siege
    /// itself would not also be (invariant 7b). Pairs deals twenty-four different stones on a
    /// card, which the siege has no picture for, so its cards and stones are
    /// <c>Art/Challenge/</c>, cut by <c>Tools/make_pairs_art.py</c> and added to the same hold
    /// for that genre only (56m). Push's keeper - a little cannon out of the same turret kit as
    /// the posts - is <c>Art/Challenge/push_keeper</c>, cut by <c>Tools/make_push_art.py</c> and
    /// held for Push alone. Everything else - a pad, a wall, a ring - is procedural
    /// (<c>Art</c>).
    /// </para>
    /// <para>
    /// <b>Names are written out at the call, one per line</b>, so <c>artnames.py</c> can hold
    /// every one of them to disk. The raider reels are the one built address, and they are
    /// built by <c>SiegeMode.CastAddress</c> - the same one copy the siege draws from.
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

        /// <summary>
        /// The crowned chest on the list page's deal band - the shop pack's, cut by the same
        /// tool as the genre marks, at the owner's instruction on 2026-09-23. Global for the
        /// genre marks' reason. Null until the address is synced, which the band draws as
        /// nothing rather than as a white rectangle (7b).
        /// </summary>
        public static Sprite Chest() => AssetLibrary.Sprite(AssetManifest.Ui(ChestKey));

        public const string ChestKey = "challenge_chest";

        /// <summary>
        /// The stone a deal's row wears on the sheet, keyed on the deal's <em>rung</em> - its
        /// place in the authored order, counted from one - rather than on its id: a retune
        /// that renames a deal keeps its stone, and a deal added at the top takes the next
        /// picture (7c's shape). Null past the pictures that ship, which a row draws as nothing.
        /// </summary>
        public static Sprite DealMark(int rung)
            => rung < 1 ? null : AssetLibrary.Sprite(AssetManifest.Ui(DealMarkKey(rung)));

        /// <summary>The address under <c>Ui/</c>, for the preload test to hold to the manifest.</summary>
        public static string DealMarkKey(int rung) => "challenge_deal_" + rung;

        /// <summary>
        /// The picture the deal sheet's advert row wears - a video that buys a play - cut from
        /// the owner's artwork by <c>Tools/make_challenge_art.py</c>. Global for the stones'
        /// reason. Null until the address is synced, which the row draws as nothing (7b).
        /// </summary>
        public static Sprite AdPlay() => AssetLibrary.Sprite(AssetManifest.Ui(AdPlayKey));

        /// <summary>The address under <c>Ui/</c>; <c>AssetManifest.UiSprites</c> writes it out for <c>artnames.py</c>.</summary>
        public const string AdPlayKey = "challenge_ad_play";

        static Sprite Piece(string key) => AssetLibrary.Sprite(AssetManifest.SiegeArt(key));
        static Sprite Own(string key) => AssetLibrary.Sprite(AssetManifest.ChallengePiece(key));
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

        // ------------------------------------------------------------------ pairs
        /// <summary>The back every Pairs card is dealt face down under: a stone frame and a crown.</summary>
        public static Sprite CardBack() => Own("pairs_back");

        /// <summary>The face a turned card shows its gem on: the same frame, cream inside.</summary>
        public static Sprite CardFace() => Own("pairs_face");

        /// <summary>
        /// The stone a kind is (<see cref="PairsGems"/>), or the cursed one. The address is built
        /// from the kind (7c's shape), so <c>artnames.py</c> cannot see it and
        /// <c>ChallengeTests.EveryPairsGemIsOnDisk</c> walks every kind instead. Null until the
        /// hold lands, which a card draws as nothing rather than as a white rectangle (7b).
        /// </summary>
        public static Sprite PairGem(int kind)
        {
            string key = PairsGems.ArtKey(kind);
            return Own(key);
        }

        // ------------------------------------------------------------------ push
        /// <summary>
        /// Push's keeper: the cannon a player walks round the board, cut barrel up and pivoted on
        /// its hull so a quarter turn keeps it on its cell. Null until the hold lands, which the
        /// board draws as a plain gold disc rather than as a white rectangle (7b).
        /// </summary>
        public static Sprite Keeper() => Own(KeeperKey);

        /// <summary>The address under <c>Art/Challenge/</c>, for the preload test to hold to disk.</summary>
        public const string KeeperKey = "push_keeper";

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
        /// and the starter line, the field's gems and the shared effects - and, for Pairs
        /// alone, its two cards and every stone it can deal, and for Push alone its keeper, so
        /// no genre loads a picture it does not draw.
        /// </summary>
        public static System.Collections.Generic.List<AssetRequest> Requests(ChallengeGenre genre)
        {
            var list = new System.Collections.Generic.List<AssetRequest>();
            var mode = LevelModes.Find(GameMode.Siege);
            if (mode != null) list.AddRange(mode.ArtFor(null));

            if (genre == ChallengeGenre.Pairs)
                foreach (var key in PairsArtKeys())
                    list.Add(AssetRequest.Sprite(AssetManifest.ChallengePiece(key)));

            if (genre == ChallengeGenre.Sokoban)
                list.Add(AssetRequest.Sprite(AssetManifest.ChallengePiece(KeeperKey)));

            return list;
        }

        /// <summary>Every picture Pairs draws, under <c>Art/Challenge/</c>: the two cards, each stone and the curse.</summary>
        public static System.Collections.Generic.List<string> PairsArtKeys()
        {
            var keys = new System.Collections.Generic.List<string> { "pairs_back", "pairs_face" };
            for (int kind = 0; kind < PairsGems.Kinds; kind++) keys.Add(PairsGems.ArtKey(kind));
            keys.Add(PairsGems.ArtKey(PairsGems.Curse));
            return keys;
        }
    }
}
