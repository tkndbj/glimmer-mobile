using GlimmerGrove.Localization;
using GlimmerGrove.Ranks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Drawing somebody else's rank: a rung id off a board row or a public card, turned into a
    /// badge and a name.
    ///
    /// <para>
    /// <b>This is the stranger's half of <see cref="RankBadge"/>.</b> That one is a readout of
    /// the player's own rank and watches the ledger for it; this one is handed an id by the
    /// server and has no ledger to ask. What they share is the ladder, which is the only thing
    /// that can say whether an id means anything to this build.
    /// </para>
    /// <para>
    /// <b>An id this build has never heard of draws nothing at all, and that is the whole
    /// reason this file exists.</b> A badge address is built from a rung id (invariant 7c's
    /// shape, and <c>RankDefinition.Icon</c>), so an unknown id composes an address as happily
    /// as a known one and hands <c>Art.S</c> a name with nothing behind it — which is a white
    /// rectangle where a badge should be (invariant 7b), on a hundred rows at once. That is
    /// not a hypothetical: a newer content pack can add a rung, and the server publishes what
    /// it derived rather than what any particular client ships. So the id is resolved through
    /// the ladder first and only a rung this build actually carries is ever drawn.
    /// </para>
    /// <para>
    /// <b>Empty is the ordinary answer, not a fault.</b> Every account below the first rung
    /// publishes no rung at all, as does every card written before the server learned to derive
    /// one. Both arrive here as an empty string and both draw as an absent badge rather than as
    /// a placeholder — a keeper who has not earned one has not earned one, and saying so with a
    /// greyed picture of somebody else's badge would be a sentence the row does not mean.
    /// </para>
    /// </summary>
    public static class RankArt
    {
        /// <summary>
        /// The rung an id names, or null when this build does not carry it — which covers an
        /// empty id, an unknown one, and a build whose content has no ladder at all.
        /// </summary>
        public static RankDefinition Rung(string rungId)
            => string.IsNullOrEmpty(rungId) ? null : RankLedger.Ladder.Find(rungId);

        /// <summary>The badge, or null. Null is drawn as nothing; see the class remarks.</summary>
        public static Sprite Badge(string rungId)
        {
            var rung = Rung(rungId);
            return rung == null ? null : Art.S(rung.Icon);
        }

        /// <summary>What the rung is called, or empty when this build cannot name it.</summary>
        public static string Name(string rungId)
        {
            var rung = Rung(rungId);
            return rung == null ? string.Empty : rung.Name;
        }

        /// <summary>
        /// Paints a badge into an <see cref="Image"/> and answers whether anything was drawn.
        ///
        /// <para>
        /// <b>The node is switched off rather than given a null sprite</b>, because those are
        /// not the same thing: an <c>Image</c> with no sprite is a white rectangle at the
        /// graphic's own colour, which is invariant 7b's fault and the one this whole file is
        /// written around. Callers use the answer to close up the space the badge would have
        /// taken.
        /// </para>
        /// </summary>
        public static bool Paint(Image image, string rungId)
        {
            if (image == null) return false;

            var sprite = Badge(rungId);
            bool drawn = sprite != null;

            if (image.enabled != drawn) image.enabled = drawn;
            image.sprite = sprite;
            image.color = Color.white;

            return drawn;
        }

        /// <summary>
        /// The honorific a keeper level earns, which is what a row falls back to saying when
        /// there is no badge. Kept here beside the badge so the two answers to "who is this"
        /// live together rather than being composed differently on each screen.
        /// </summary>
        public static string Unranked => Loc.Get("ui.ranks.unranked");
    }
}
