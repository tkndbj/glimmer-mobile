using GlimmerGrove.Ranks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The furniture the hall of ranks is built from: five plates cut from the owner's bought
    /// 2D Mobile Game UI Kit and one icon, by <c>Tools/make_rank_kit_art.py</c>, plus the
    /// icon every requirement line wears.
    ///
    /// <para>
    /// <b>Why a kit, and why this one.</b> The page's furniture was drawn procedurally — a
    /// round-rect chip, a round-rect pill, a plate with a generated rim, capsule links — and
    /// then cut from stone tiles, and the owner rejected both on 2026-09-27: neither looked
    /// like a game. What was asked for instead, in so many words, was a <em>proper board</em>
    /// for "what it asks", the ranked badges in proper seats, and a picture on every line —
    /// a star beside "Earn N stars" — with no ticks. The kit the owner bought has all of it:
    /// a notched panel, a dark bar, a rimmed square, a hanging tab, a pill.
    /// </para>
    ///
    /// <para>
    /// <b>Every piece is pre-scaled by the tool and drawn 1:1.</b> The kit is cut for a
    /// 3118-wide sheet; each piece is scaled to the size this page draws it at and its
    /// nine-slice border measured after (invariant 44a), so an <c>Image</c> here is an
    /// ordinary sliced image with no multiplier — the border in the sprite is the border on
    /// screen.
    /// </para>
    ///
    /// <para>
    /// <b>A line's icon is the measure's, and the measure is content</b> (52a). The table
    /// below keys on <see cref="RankMeasure.Id"/> — the permanent id <c>progression.json</c>
    /// names a line by — so a retune that changes a rung's lines redraws with the right
    /// pictures and a measure this build has no picture for wears the trophy rather than a
    /// white rectangle (7b). Every picture but the swords is art the game already ships.
    /// </para>
    ///
    /// <para>
    /// <b>Hand-listed in <c>AssetManifest.UiSprites</c></b> so the global preload carries
    /// them and <c>artnames.py</c> can see them; <c>RankLadderTests</c> holds <see cref="All"/>
    /// to that list and to disk. Until the Editor has addressed a freshly cut piece (7a),
    /// <see cref="Piece"/> falls back to the nearest thing in the interface kit rather than
    /// handing an <c>Image</c> nothing (7b).
    /// </para>
    /// </summary>
    public static class RankKit
    {
        public const string Board = "Rank/kit_board";
        public const string Row = "Rank/kit_row";
        public const string Seat = "Rank/kit_seat";
        public const string Tab = "Rank/kit_tab";
        public const string Chip = "Rank/kit_chip";
        public const string Raiders = "Rank/ic_raiders";

        /// <summary>Every address this page adds, for the gate that holds them to disk.</summary>
        public static readonly string[] All = { Board, Row, Seat, Tab, Chip, Raiders };

        /// <summary>What stands in for a piece the Editor has not addressed yet.</summary>
        static string Fallback(string piece)
        {
            switch (piece)
            {
                case Board: return Skins.Panel;
                case Row: return Skins.Trough;
                case Tab: return Skins.Title;
                case Raiders: return "ic_battle";
                default: return Skins.Slot;
            }
        }

        /// <summary>The piece's sprite, or its stand-in while the address has not been synced.</summary>
        public static Sprite Piece(string piece)
        {
            var sprite = Art.S("Ui/" + piece);
            return sprite != null ? sprite : Art.S("Ui/" + Fallback(piece));
        }

        /// <summary>One plate laid at <paramref name="size"/>, sliced by the border its sprite carries.</summary>
        public static Image Lay(string name, Transform parent, string piece, Vector2 size,
                                Vector2 anchor, Vector2 pos)
        {
            var img = UIKit.Img(name, parent, Piece(piece), Color.white, size, anchor, pos);
            img.type = Image.Type.Sliced;
            return img;
        }

        /// <summary>
        /// The picture beside a requirement line: the sprite's address under <c>Ui/</c>, and
        /// whether it is a white glyph to be drawn in gold rather than a coloured picture drawn
        /// as it is. Pure, so the fixture can walk it without touching a <c>Color</c>.
        /// </summary>
        public static (string address, bool gold) IconFor(RankMeasure measure)
        {
            switch (measure.Id)
            {
                case "stars": return ("star_full", false);
                case "three_stars": return ("ic_stars", true);
                case "keeper_level": return ("ic_xp_boost", false);
                case "best_wave": return ("ic_endless", false);
                case "runs": return ("ic_battle", false);
                case "raiders": return (Raiders, false);
                case "bosses": return ("crest_gold", false);
                case "charms": return ("ic_gem", false);
                case "levels_cleared": return ("ic_trophy", true);
                default: return ("ic_trophy", true);
            }
        }

        /// <summary>The line's icon, painted: <see cref="IconFor"/> resolved, with the swords' stand-in.</summary>
        public static void PaintIcon(Image image, RankMeasure measure)
        {
            if (image == null) return;
            var (address, gold) = IconFor(measure);
            var sprite = address == Raiders ? Piece(Raiders) : Art.S("Ui/" + address);
            image.enabled = sprite != null;
            image.sprite = sprite;
            image.color = gold ? Pal.Gold : Color.white;
        }
    }
}
