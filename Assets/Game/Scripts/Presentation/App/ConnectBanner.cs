using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The standing sentence on a page whose chests cannot be opened yet: connect once, and
    /// they open offline for ever after.
    ///
    /// <para>
    /// <b>Why it has to be a banner and not a refusal on a control.</b> A chest is rolled from
    /// the account id so the server can recompute it, so before the first sign-in there is
    /// nothing honest to open (<see cref="Progression.RewardSeed.IsAdjudicable"/>). Both pages
    /// already said so — the tasks page in a row's hint, the season in a toast on a rung tap —
    /// and on the install where it matters most <b>neither of them ever spoke</b>: a hint hangs
    /// off a task that is <em>ready</em> and a fresh account has finished nothing, and a season
    /// rung needs marks, which come only from chests. The page a new player actually meets was
    /// silent, and silence about a thing that is refusing them is the fault this whole file
    /// exists to prevent. <b>A sentence that only appears once there is something to claim is
    /// not a sentence about why nothing can be claimed.</b>
    /// </para>
    /// <para>
    /// <b>Shown while the gate is shut, which is once in an account's life.</b> Not "once per
    /// session" and not dismissible: the condition is <em>never having been online</em>, and it
    /// ends permanently the first time a device reaches the network for a few seconds. So the
    /// banner is its own expiry — there is nothing to remember, nothing stored, and no way for
    /// it to come back and nag somebody who has already done what it asks.
    /// </para>
    /// <para>
    /// <b>One widget because two pages draw it</b>, which is invariant 16l: a design assembled
    /// at each call site is two designs a week later, and these two are the same sentence about
    /// the same gate. The plate, the mark and the wording live here; a caller says only where.
    /// </para>
    /// </summary>
    public sealed class ConnectBanner
    {
        /// <summary>
        /// The plate's height. Two lines of a translated sentence at the floor size, with the
        /// mark beside them — <c>UIKit.Shrinkable</c> truncates what will not fit and does it
        /// silently (invariant 19n), so the box is sized for the worst case rather than for
        /// English.
        /// </summary>
        public const float Height = 104f;

        /// <summary>Breathing room under it, matching the pass banner's own.</summary>
        public const float Gap = 14f;

        readonly RectTransform _root;

        ConnectBanner(RectTransform root) => _root = root;

        /// <summary>
        /// Builds it at <paramref name="y"/> — a downward offset from the top of the stack, the
        /// way every other band on these two pages is placed.
        ///
        /// <para>
        /// Amber, because it is news rather than an alarm: the same register as the shop's
        /// unreachable line and the streak board's CONNECT ONCE pill, and deliberately not the
        /// rose this game uses for something that has gone wrong. Nothing here has gone wrong —
        /// the player simply has not been online yet.
        /// </para>
        /// <para>
        /// A key rather than a padlock, for the same reason the shelf's strip says
        /// <c>Level 26</c> rather than LOCKED (42e): a padlock says a player cannot have this
        /// and says nothing about what would change it, and the whole point of this plate is
        /// that what would change it is small and permanent.
        /// </para>
        /// </summary>
        public static ConnectBanner Build(Transform parent, float width, float y)
        {
            var plate = UIKit.Img("Connect", parent, Art.S("Ui/" + Skins.PlateOrange),
                                  Color.white, new Vector2(width, Height),
                                  new Vector2(.5f, 1f), new Vector2(0f, -(y + Height * .5f)));

            UIKit.Img("Glow", plate.transform, Art.Glow(128, 2f), Pal.A(Pal.Sun, .26f),
                      new Vector2(200f, 200f), new Vector2(0f, .5f), new Vector2(92f, 0f));

            var mark = UIKit.Img("Mark", plate.transform, Art.S("Ui/ic_key"), Pal.Cream,
                                 new Vector2(64f, 64f), new Vector2(0f, .5f),
                                 new Vector2(92f, 0f));
            mark.preserveAspect = true;

            // The box is the room between the mark and the far margin, measured rather than
            // guessed: the mark ends at 124 and the plate's own inset is 40, so a sentence may
            // have `width - 184`. A Unity label that overflows is not clipped and nothing says
            // so (invariant 37n), which is why this is a box and a fitter rather than a size.
            float textW = width - 184f;

            UIKit.Shrinkable(
                UIKit.Titled("Line", plate.transform, Loc.Get("ui.chest.connect_once"), 26,
                             Pal.Cream, TextAnchor.MiddleLeft, new Vector2(textW, Height - 24f),
                             new Vector2(0f, .5f), new Vector2(160f + textW * .5f, 0f),
                             3f, 3f, wrap: true), 17);

            return new ConnectBanner((RectTransform)plate.transform);
        }

        /// <summary>
        /// Takes the banner down when the gate opens under it.
        ///
        /// <para>
        /// <b>It leaves its own gap, deliberately.</b> The band above and the list below are
        /// placed against an absolute offset computed when the page was built, so genuinely
        /// reclaiming the room means laying the page out again. That trade is worth naming: the
        /// only way to reach this at all is to be standing on one of these two pages at the
        /// exact moment a first-ever connection lands, and what the player gets for the strip
        /// of empty ground is that the page stops telling them to do a thing they have just
        /// done. <b>A stale sentence is a worse fault than a gap</b>, and the gap is gone the
        /// next time the page is opened.
        /// </para>
        /// </summary>
        public void Show(bool on)
        {
            if (_root && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }
    }
}
