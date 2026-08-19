using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// The grove's residents, which are the profile's companions — one roster, two places
    /// it is used.
    ///
    /// <para>
    /// <b>Why they are the same thing.</b> They were not, and the duplication was the whole
    /// problem: five creatures lived in <c>homestead.json</c>, were earned by clearing five
    /// named glades, and had nothing to do with the thirty-one companions in the manifest that
    /// a player levels towards and pays for. So the game had two rosters of creatures with two
    /// unlock rules, two prices (one of them permanently zero), two art conventions and two
    /// screens that could disagree about what somebody owned — and a player who bought Coral
    /// on the profile could not stand Coral in their village. One roster removes all of that
    /// by construction. A companion is somebody you have; wearing them is what you do on the
    /// profile and housing them is what you do in the grove, and neither reads the other.
    /// </para>
    /// <para>
    /// <b>Projection rather than authoring.</b> A resident is <em>derived</em> from an
    /// <see cref="AvatarDefinition"/> every time the roster is published, so nothing about a
    /// companion is written down twice. A drop that adds a companion adds a resident with no
    /// second row to remember, no second price to keep in step, and no way for the two to
    /// drift — which is the same bargain <c>AssetManifest</c> makes about chapter art and for
    /// the same reason. It also means the grove catalog is no longer where the roster's size
    /// is decided, which is what stops the shop and the profile from ever showing different
    /// counts.
    /// </para>
    /// <para>
    /// <b>What a projected resident inherits, and what it does not.</b> Its id is the
    /// companion's id, permanently — invariant 1 applies in full, and it is written into
    /// <c>homesteadPlaced</c> the moment somebody stands it on an island. Its price and its
    /// keeper-level gate are the companion's, unchanged, so the two screens quote one number.
    /// What it does <em>not</em> carry is the worn state: <c>wallet.avatarId</c> is a
    /// preference about the profile and this file never reads it, so wearing somebody changes
    /// nothing in the grove and housing them changes nothing on the profile.
    /// </para>
    /// </summary>
    public static class GroveResidents
    {
        /// <summary>
        /// How large a resident draws, and how far up its slot it sits.
        ///
        /// Constants rather than content, because these are facts about how the art is drawn
        /// rather than about any one creature — every portrait in the pack is cut the same way,
        /// standing on the bottom edge of its own frame. A per-companion override would be a
        /// number nobody could author correctly from the manifest, which is
        /// <c>GroveFloor</c>'s lesson: geometry an author cannot see from the JSON is derived.
        /// </summary>
        public const float Scale = .95f;

        /// <summary>See <see cref="Scale"/>. Half the sprite's height above the slot's point.</summary>
        public const float Lift = .45f;

        /// <summary>Where a companion's still art lives, relative to <c>Art/</c>.</summary>
        public const string PortraitFolder = "Companions/";

        /// <summary>Where a companion's flipbook lives, relative to <c>Art/</c>. Global art.</summary>
        public const string CritterFolder = "Critters/";

        /// <summary>
        /// What a resident's piece id looks like: the companion's id, prefixed.
        ///
        /// <para>
        /// <b>Two permanent id spaces must never be merged by accident.</b> Companion ids and
        /// grove piece ids were minted independently, years apart, by different people — and
        /// they already collided: <c>pebble</c> is a decor rock and a companion. Both are
        /// written into save files, so neither could be renamed, and letting one win would have
        /// meant a companion silently absent from the shelf that is supposed to hold every one
        /// of them. The prefix makes the collision unrepresentable rather than merely detected,
        /// which is the shape this project reaches for whenever the alternative is a rule
        /// somebody has to keep checking.
        /// </para>
        /// <para>
        /// It is itself permanent — a placed resident writes <c>friend_coral</c> into
        /// <c>homesteadPlaced</c>, so invariant 1 covers it in full — and the prefix is reserved:
        /// <c>ContentValidation</c> fails the build on an authored piece that starts with it.
        /// </para>
        /// </summary>
        public const string Prefix = "friend_";

        /// <summary>The grove's id for a companion.</summary>
        public static string PieceId(string companionId)
            => string.IsNullOrEmpty(companionId) ? string.Empty : Prefix + companionId;

        /// <summary>
        /// The companion a resident piece id names, or empty when the id is not a resident's.
        ///
        /// A pure function of the id, which is what lets <see cref="HomesteadPiece.NameKey"/>
        /// stay derived and lets a support tool read a slot out of a save file without the
        /// catalog — invariant 5a's property, kept.
        /// </summary>
        public static string CompanionIdOf(string pieceId)
            => !string.IsNullOrEmpty(pieceId) && pieceId.StartsWith(Prefix, StringComparison.Ordinal)
                ? pieceId.Substring(Prefix.Length)
                : string.Empty;

        /// <summary>True for a piece id minted by this file. See <see cref="Prefix"/>.</summary>
        public static bool IsResidentId(string pieceId)
            => !string.IsNullOrEmpty(pieceId) && pieceId.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>The companion behind a resident piece, or an invalid one.</summary>
        public static AvatarDefinition CompanionOf(HomesteadPiece piece)
            => piece.IsResident ? AvatarCatalog.Find(CompanionIdOf(piece.Id)) : default;

        /// <summary>
        /// One companion as a thing that can stand on an island.
        ///
        /// <para>
        /// A companion with a flipbook is drawn animated, and one without is drawn as its
        /// portrait. That is not a compromise: the five with flipbooks are the board's own
        /// critters, whose frames are already global art the game has paid for, so animating
        /// them costs nothing — and a resident that breathes is worth more than a still one on
        /// a screen whose whole subject is that somebody lives here. The rest stand still,
        /// which is what every companion added by a future drop will do, and it is why
        /// <c>HomesteadPiece.Animated</c> is a field rather than something inferred from the
        /// kind.
        /// </para>
        /// </summary>
        public static HomesteadPiece From(AvatarDefinition companion)
        {
            if (!companion.IsValid) return default;

            bool animated = companion.HasAnimation;
            string art = animated
                ? CritterFolder + companion.Animated
                : PortraitFolder + companion.Portrait;

            return new HomesteadPiece(PieceId(companion.Id), art, animated, HomesteadPieceKind.Resident,
                                      companion.UnlockCost, LevelId.None, ChapterId.None,
                                      Scale, Lift, HomesteadSlotKind.Ground, 0,
                                      companion.UnlockLevel);
        }

        /// <summary>The whole roster, as pieces. Invalid entries are dropped, never projected.</summary>
        public static List<HomesteadPiece> From(IReadOnlyList<AvatarDefinition> roster)
        {
            var pieces = new List<HomesteadPiece>(roster?.Count ?? 0);
            if (roster == null) return pieces;

            foreach (var companion in roster)
            {
                var piece = From(companion);
                if (piece.IsValid) pieces.Add(piece);
            }

            return pieces;
        }

        // ------------------------------------------------------------- retirement
        /// <summary>
        /// The five creatures <c>homestead.json</c> used to author, and the companion each one
        /// became.
        ///
        /// <para>
        /// <b>Why a map and not a deletion.</b> These ids are in live save files, standing in
        /// slots people arranged. A retired id resolves to nothing, which would leave a hole
        /// that <em>still counts as occupied</em> — so an island would read "10 of 10" with a
        /// gap in it, and the player would have lost part of something they built for a reason
        /// no screen could explain. Rewriting the placement is the only outcome that keeps the
        /// grove looking like the grove they made.
        /// </para>
        /// <para>
        /// <b>The mapping is not arbitrary.</b> Each retired resident drew one of the board's
        /// five critter flipbooks, and exactly one companion in the roster draws that same
        /// flipbook — so the creature standing on the island after the rewrite is the same
        /// creature that was standing there before it. Nothing is granted: this moves an
        /// <em>arrangement</em>, which the save file has always allowed to name a piece the
        /// player does not hold (see <c>HomesteadLayout.Place</c>), and a forged arrangement
        /// costs a picture on a screen nobody else sees.
        /// </para>
        /// <para>
        /// It is a permanent table, not a migration step to delete later. A save that has not
        /// been opened since the change can arrive at any time — from a device left in a
        /// drawer, or from a cloud document written before it — so the rewrite has to happen on
        /// every load, for ever, which is exactly what <c>LegacyPlayerPrefsImport</c> learned.
        /// </para>
        /// </summary>
        static readonly Dictionary<string, string> Retired = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "sunmote", Prefix + "puff"     },   // both draw Critters/c1
            { "ripple",  Prefix + "timber"   },   // c2
            { "prism",   Prefix + "sprocket" },   // c3
            { "burr",    Prefix + "thistle"  },   // c4
            { "dusk",    Prefix + "monarch"  },   // c5
        };

        /// <summary>
        /// The companion a retired resident id became, or null when the id is not retired.
        ///
        /// Every reader of a stored piece id goes through <see cref="Rename"/>; this is the
        /// table behind it, exposed so the Editor can prove the mapping still resolves.
        /// </summary>
        public static bool TryRename(string pieceId, out string becomes)
            => Retired.TryGetValue(pieceId ?? string.Empty, out becomes);

        /// <summary>
        /// A stored piece id, as this build knows it. Anything not retired is handed straight
        /// back, including ids this build has never heard of — a save written by a newer build
        /// must survive a trip through an older one untouched.
        /// </summary>
        public static string Rename(string pieceId)
            => Retired.TryGetValue(pieceId ?? string.Empty, out var renamed) ? renamed : pieceId;

        /// <summary>Every retired id, for the Editor's check that each one still maps home.</summary>
        public static IReadOnlyCollection<string> RetiredIds => Retired.Keys;

        /// <summary>The companion a retired id maps to, for the same check.</summary>
        public static IReadOnlyCollection<string> RetiredTargets => Retired.Values;
    }
}
