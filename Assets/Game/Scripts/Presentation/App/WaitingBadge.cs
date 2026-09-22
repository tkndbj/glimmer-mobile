using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A count on the corner of a box saying how many things behind it are waiting to be
    /// taken: the hub's task pack wears one, the streak and the season boxes wear one, and
    /// the invite door on the profile and the shop wears one.
    ///
    /// <para>
    /// <b>One class, because it used to be a nested one in <c>HomeScreen</c> and the third
    /// screen to want it would have written a fourth copy.</b> Two cuts of the same idea: a
    /// <see cref="Burst"/> (the kit's starburst with a <c>+N</c>, the shape a pack's corner
    /// says) and a <see cref="Disc"/> (a plain gold disc with the number alone). Both are
    /// built dark and painted; a badge rebuilt on every event would pop on every event.
    /// </para>
    /// <para>
    /// <b>Built last on whatever card wears it</b>, so it sits over the card's own tap area
    /// and over any mask cut into the card — a badge inside a <c>Mask</c> is cropped by it.
    /// </para>
    /// <para>
    /// <b>Painted, never drawn</b> (invariant 44j's rule about readouts): a count written at
    /// build time is a photograph, and a badge that says <em>2</em> after the second chest was
    /// opened is a badge that has stopped being true. Whoever builds one subscribes to the
    /// event that moves the count and calls <see cref="Paint"/> from it.
    /// </para>
    /// </summary>
    public sealed class WaitingBadge
    {
        public RectTransform Root { get; private set; }
        Text _count;
        string _prefix = string.Empty;
        bool _shown;

        WaitingBadge() { }

        /// <summary>
        /// The kit's starburst with a <c>+N</c> on it. <paramref name="anchor"/> and
        /// <paramref name="pos"/> say which corner: the hub's pack wears it top-left, the invite
        /// door top-right.
        /// </summary>
        public static WaitingBadge Burst(Transform card, Vector2 anchor, Vector2 pos)
        {
            var burst = UIKit.Img("Waiting", card, Art.S("Ui/" + Skins.Badge), Pal.Gold,
                                  new Vector2(104f, 104f), anchor, pos);

            var count = UIKit.Shrinkable(
                UIKit.Titled("N", burst.transform, "+0", 30, new Color(.17f, .11f, .02f),
                             TextAnchor.MiddleCenter, new Vector2(80f, 50f),
                             new Vector2(.5f, .5f), new Vector2(0f, 2f), 0f, 0f), 18);

            UIKit.Halo(burst.transform, Pal.Gold, 190f, .40f);
            burst.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            burst.gameObject.SetActive(false);

            return new WaitingBadge { Root = (RectTransform)burst.transform, _count = count, _prefix = "+" };
        }

        /// <summary>The starburst on a card's top-left corner, where the hub's pack wears it.</summary>
        public static WaitingBadge BurstTopLeft(Transform card)
            => Burst(card, new Vector2(0f, 1f), new Vector2(46f, -44f));

        /// <summary>The starburst on a card's top-right corner, where the invite door wears it.</summary>
        public static WaitingBadge BurstTopRight(Transform card)
            => Burst(card, new Vector2(1f, 1f), new Vector2(-46f, -44f));

        /// <summary>A gold disc with the bare number, on a card's top-right corner.</summary>
        public static WaitingBadge Disc(Transform card)
        {
            var badge = UIKit.Img("Waiting", card, Art.Disc(64), Pal.Gold,
                                  new Vector2(66f, 66f), new Vector2(1f, 1f), new Vector2(-30f, -28f));

            var rim = UIKit.Img("Rim", badge.transform, Art.Ring(64, 7f), new Color(.16f, .12f, .04f, .95f));
            UIKit.StretchTo((RectTransform)rim.transform, 0, 0, 0, 0);

            // Shrinkable, because this is not a one-digit field: a player who is away for a
            // fortnight comes back to two figures.
            var count = UIKit.Shrinkable(
                UIKit.Titled("N", badge.transform, "0", 36, new Color(.17f, .11f, .02f),
                             TextAnchor.MiddleCenter, new Vector2(50f, 50f),
                             new Vector2(.5f, .5f), Vector2.zero, 0f, 0f), 22);

            UIKit.Halo(badge.transform, Pal.Gold, 146f, .45f);
            badge.gameObject.SetActive(false);

            return new WaitingBadge { Root = (RectTransform)badge.transform, _count = count };
        }

        /// <summary>
        /// Writes the count. Nought hides the badge; the first positive count pops it and
        /// starts it breathing; a later count only rewrites the number, so a repaint in the
        /// middle of a breath leaves the breath alone.
        /// </summary>
        public void Paint(int n)
        {
            if (!Root) return;

            if (n <= 0)
            {
                Root.gameObject.SetActive(false);
                _shown = false;
                return;
            }

            if (_count) _count.text = _prefix + n;
            if (_shown) return;

            _shown = true;
            Root.gameObject.SetActive(true);
            Tween.KillChannel(Root, "breathe");
            Root.localScale = Vector3.zero;
            var root = Root;
            Tween.Pop(root, 0f, .5f, .18f)
                 .OnDone(() => { if (root) Tween.Breathe(root, .10f, 1.3f); });
        }
    }
}
