using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What a grove is worth, and the stars that has earned: a corner readout that celebrates a
    /// star the moment it is won.
    ///
    /// <para>
    /// <b>A widget rather than another hundred lines on the screen, because it holds state.</b>
    /// The rest of what came out of <c>HomesteadScreen</c> is construction or coordination and
    /// went into partials; this keeps <see cref="_shown"/> — the last figure drawn — which is
    /// the whole of what makes a fanfare a fanfare rather than a number appearing. State plus
    /// the drawing that owns it is an object, and having it apart means the screen can no longer
    /// reach into the middle of the rule by accident.
    /// </para>
    /// <para>
    /// <b>It is a readout and not a control, and every graphic in it is non-interactive.</b> The
    /// Grovement is panned and pinched, so a box in the corner that swallowed a drag would break
    /// the one gesture the whole page is built on — and the corner it sits in is where a right
    /// thumb rests. <see cref="UIKit.Img"/> and <see cref="UIKit.Label"/> both leave
    /// <c>raycastTarget</c> off, so a drag begun on top of this reaches the field exactly as if
    /// the box were not there.
    /// </para>
    /// <para>
    /// The star row's size comes from the ladder's length rather than from a constant, because
    /// the ladder is content (<c>GroveScoreTable</c>) and a drop may lengthen it. Five stars at
    /// the shipped spacing, eight packed a little tighter, and neither draws off the side.
    /// </para>
    /// </summary>
    public sealed class GroveScoreBox
    {
        /// <summary>Widest the star row may grow before it is packed tighter.</summary>
        const float StarsWidth = 292f;

        static readonly Vector2 BoxSize = new Vector2(340f, 196f);
        static readonly Vector2 BoxAnchor = new Vector2(1f, 0f);

        Text _value, _next;
        StarRow _stars;

        /// <summary>
        /// Stars drawn last, so a star won while the player is standing here arrives as
        /// something rather than as a number that was already different.
        ///
        /// Session-local and deliberately not stored: the save already knows everything the
        /// score is derived from, and a "stars last seen" field would be a stored count of
        /// exactly the shape invariant 11b forbids — merged across devices it could only ever
        /// re-celebrate or silently swallow. -1 means nothing has been drawn yet, which is what
        /// makes the first paint of a screen quiet.
        /// </summary>
        int _shown = -1;

        public static GroveScoreBox Attach(Transform parent)
        {
            var box = new GroveScoreBox();
            box.Build(parent);
            return box;
        }

        void Build(Transform parent)
        {
            var plate = UIKit.Img("Score", parent, Art.Round(28), new Color(.06f, .12f, .17f, .74f),
                                  BoxSize, BoxAnchor, UIKit.Corner(BoxSize, BoxAnchor, 28f, 28f));
            var rt = (RectTransform)plate.transform;

            var edge = UIKit.Img("Edge", rt, Art.RoundOutline(28, 3f), new Color(1f, 1f, 1f, .13f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Shrinkable(
                UIKit.Titled("Label", rt, Loc.Get("ui.grove.score").ToUpperInvariant(), 22,
                             new Color(1f, .96f, .88f, .70f), TextAnchor.MiddleCenter,
                             new Vector2(300f, 30f), new Vector2(.5f, 1f), new Vector2(0f, -26f),
                             outline: 3f, shadow: 0f), 16);

            _value = UIKit.Shrinkable(
                UIKit.Titled("Value", rt, string.Empty, 44, Pal.Gold, TextAnchor.MiddleCenter,
                             new Vector2(300f, 56f), new Vector2(.5f, 1f), new Vector2(0f, -74f),
                             outline: 4f, shadow: 4f), 26);

            int rungs = Mathf.Max(1, HomesteadCatalog.Current.Scores.StarCount);
            float spacing = Mathf.Min(40f, StarsWidth / rungs);

            _stars = StarRow.Create(rt, new Vector2(.5f, 1f), new Vector2(0f, -128f),
                                    spacing * .82f, spacing, 0, false, rungs);

            _next = UIKit.Shrinkable(
                UIKit.Titled("Next", rt, string.Empty, 22, new Color(1f, .96f, .88f, .58f),
                             TextAnchor.MiddleCenter, new Vector2(310f, 28f), new Vector2(.5f, 1f),
                             new Vector2(0f, -168f), outline: 3f, shadow: 0f), 15);

            rt.localScale = Vector3.zero;
            Tween.Pop(rt, 0f, .5f, .18f);

            // Drawn once here so the box never appears blank. The catalog is a body and may not
            // have arrived, in which case this is an honest zero that the first repaint replaces
            // — see Paint for why that first real reading does not celebrate.
            Paint();
        }

        /// <summary>
        /// Redraws the standing, and celebrates a star that was not there a moment ago.
        ///
        /// <para>
        /// The whole reading is taken in one call (<see cref="GroveScore.Of"/>) so the number and
        /// the stars can never come from two different moments — the mistake the victory panel's
        /// separately derived reward row spent a version proving is real.
        /// </para>
        /// <para>
        /// A star gained while the screen is open re-runs the row's fanfare rather than appearing.
        /// That is the point of drawing this at all: buying land or a companion happens in the
        /// shop, so without it the reward for a purchase would be a number that had quietly
        /// changed by the time the player came back.
        /// </para>
        /// </summary>
        public void Paint()
        {
            if (!_value) return;

            var standing = GroveScore.Of(HomesteadCatalog.Current);

            _value.text = Compact.Number(standing.Score);

            if (_next)
                _next.text = standing.IsTopped
                    ? Loc.Get("ui.grove.score_top")
                    : Loc.Format("ui.grove.score_next", Compact.Number(standing.ToNext));

            if (!_stars) return;

            // A ladder can be re-published under an open screen — the catalog is a body and a
            // content refresh swaps it whole — so a row built for five rungs may be looking at
            // six. Rebuilding it is not worth a frame's work; drawing what it can hold is honest,
            // and the next visit builds the right row.
            int stars = Mathf.Min(standing.Stars, _stars.Count);

            // The baseline is only taken once there is a real catalog to compare against. Without
            // that the empty grove drawn before the body arrives would be the baseline, and every
            // visit would open with a fanfare for stars the player won weeks ago — which is the
            // fastest way to make a celebration mean nothing.
            bool settled = HomesteadCatalog.IsLoaded;

            if (settled && _shown >= 0 && stars > _shown) _stars.Reveal(stars, .1f, .3f);
            else _stars.SetInstant(stars);

            if (settled) _shown = stars;
        }

        /// <summary>
        /// Celebrates against a reading taken somewhere else — the star a land purchase earned
        /// while the player was still in the shop.
        ///
        /// <para>
        /// Measured against <paramref name="before"/> rather than against <see cref="_shown"/>,
        /// which this screen's first paint has already moved to the new figure — quietly, and
        /// deliberately, because a baseline taken on a blank grove is how a celebration comes to
        /// mean nothing.
        /// </para>
        /// </summary>
        public void Celebrate(int before)
        {
            if (before < 0 || _stars == null || !HomesteadCatalog.IsLoaded) return;

            int stars = Mathf.Min(GroveScore.Of(HomesteadCatalog.Current).Stars, _stars.Count);
            if (stars > before) _stars.Reveal(stars, .12f, .32f);
        }
    }
}
