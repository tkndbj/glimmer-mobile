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
    /// and over any mask cut into the card - a badge inside a <c>Mask</c> is cropped by it.
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
            => Burst(card, anchor, pos, Pal.Gold);

        /// <summary>
        /// The same starburst in another colour. <paramref name="tint"/> multiplies the kit's
        /// white-drawn burst (`Hud/burst` is cut white so a tint reaches it, unlike the buttons -
        /// invariant 44g) and lights the halo; the count stays dark ink on both.
        /// </summary>
        public static WaitingBadge Burst(Transform card, Vector2 anchor, Vector2 pos, Color tint)
            => Burst(card, anchor, pos, tint, 1f);

        /// <summary>
        /// The starburst at a multiple of its size. The hub's four feature boxes wear it at
        /// <see cref="HubScale"/>; the size is built in rather than scaled on the root, because
        /// <see cref="Paint"/> pops and breathes the root's own scale.
        /// </summary>
        public static WaitingBadge Burst(Transform card, Vector2 anchor, Vector2 pos, Color tint, float scale)
        {
            var burst = UIKit.Img("Waiting", card, Art.S("Ui/" + Skins.Badge), tint,
                                  new Vector2(104f, 104f) * scale, anchor, pos);

            var count = UIKit.Shrinkable(
                UIKit.Titled("N", burst.transform, "+0", Mathf.RoundToInt(30 * scale), new Color(.17f, .11f, .02f),
                             TextAnchor.MiddleCenter, new Vector2(80f, 50f) * scale,
                             new Vector2(.5f, .5f), new Vector2(0f, 2f * scale), 0f, 0f), 18);

            UIKit.Halo(burst.transform, tint, 190f * scale, .40f);
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

        /// <summary>The starburst on a card's top-right corner in a colour of the caller's - the hub's name card wears it green.</summary>
        public static WaitingBadge BurstTopRight(Transform card, Color tint)
            => Burst(card, new Vector2(1f, 1f), new Vector2(-46f, -44f), tint);

        /// <summary>
        /// How much larger the hub's feature boxes wear the starburst (tasks, streak, season,
        /// daily challenges), at the owner's instruction on 2026-09-28: one shape on all four,
        /// a little bigger than the invite door's.
        /// </summary>
        public const float HubScale = 1.2f;

        /// <summary>The hub's starburst on a card's top-left corner.</summary>
        public static WaitingBadge HubTopLeft(Transform card, Color tint)
            => Burst(card, new Vector2(0f, 1f), new Vector2(46f, -44f), tint, HubScale);

        /// <summary>The hub's starburst on a card's top-right corner.</summary>
        public static WaitingBadge HubTopRight(Transform card, Color tint)
            => Burst(card, new Vector2(1f, 1f), new Vector2(-46f, -44f), tint, HubScale);

        /// <summary>
        /// The starburst carrying the shop's gift box (`Ui/ic_gift`, the Bundles tab's mark)
        /// instead of a count, at the owner's instruction on 2026-09-28: the streak, the season
        /// and the keeper card say "something is waiting" with a picture. The count still
        /// decides whether it shows - <see cref="Paint"/> hides it at nought.
        /// </summary>
        public static WaitingBadge Gift(Transform card, Vector2 anchor, Vector2 pos, Color tint, float scale)
        {
            var badge = Burst(card, anchor, pos, tint, scale);
            badge._count.gameObject.SetActive(false);
            badge._count = null;

            var gift = UIKit.Img("Gift", badge.Root, Art.S("Ui/ic_gift"), Color.white,
                                 new Vector2(66f, 66f) * scale, new Vector2(.5f, .5f), Vector2.zero);
            gift.preserveAspect = true;
            gift.raycastTarget = false;
            // Upright, whatever the burst's own tilt.
            gift.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            return badge;
        }

        /// <summary>The hub's gift starburst on a card's top-left corner - the tasks pack.</summary>
        public static WaitingBadge HubGiftTopLeft(Transform card, Color tint)
            => Gift(card, new Vector2(0f, 1f), new Vector2(46f, -44f), tint, HubScale);

        /// <summary>The hub's gift starburst on a card's top-right corner.</summary>
        public static WaitingBadge HubGiftTopRight(Transform card, Color tint)
            => Gift(card, new Vector2(1f, 1f), new Vector2(-46f, -44f), tint, HubScale);

        /// <summary>The gift starburst at its ordinary size on a card's top-right corner - the hub's name card.</summary>
        public static WaitingBadge GiftTopRight(Transform card, Color tint)
            => Gift(card, new Vector2(1f, 1f), new Vector2(-46f, -44f), tint, 1f);

        /// <summary>
        /// The Daily Challenges list's starburst: a touch smaller than the hub's, with the
        /// <c>+N</c> in white over a black outline (the owner, 2026-09-28).
        /// </summary>
        public static WaitingBadge ListTopRight(Transform card, Color tint)
            => Burst(card, new Vector2(1f, 1f), new Vector2(-46f, -44f), tint, 1.08f).WhiteInk();

        /// <summary>
        /// The hub's Daily Challenges door: the hub's starburst, top left, with the list's white
        /// <c>+N</c> over a black outline.
        /// </summary>
        public static WaitingBadge HubInkedTopLeft(Transform card, Color tint)
            => HubTopLeft(card, tint).WhiteInk();

        /// <summary>The same, on the card's top-right corner - the hub's welcome door (invariant 58).</summary>
        public static WaitingBadge HubInkedTopRight(Transform card, Color tint)
            => HubTopRight(card, tint).WhiteInk();

        /// <summary>
        /// The purple a live shop deal wears (invariant 60): the shop tab's alert on the bottom
        /// bar, at the owner's instruction on 2026-10-09 - purple, with white <c>!!!</c> over a
        /// black outline. Multiplied onto the white-cut burst, so it is the burst's own purple
        /// rather than a recoloured warm piece (44g).
        /// </summary>
        public static readonly Color DealPurple = new Color(.56f, .25f, 1f);

        /// <summary>
        /// What the deal alert says. Three marks rather than a word, so no language has to fit
        /// one into a burst the size of a thumbnail; punctuation is the same in every table.
        /// </summary>
        public const string AlertMarks = "!!!";

        /// <summary>
        /// The starburst carrying a word in white over a black outline, hidden until
        /// <see cref="Paint(string)"/> gives it one - the shop tab's deal alert.
        /// </summary>
        public static WaitingBadge Alert(Transform card, Vector2 anchor, Vector2 pos, Color tint, float scale)
            => Burst(card, anchor, pos, tint, scale).WhiteInk();

        /// <summary>Re-inks the count white over a black outline.</summary>
        WaitingBadge WhiteInk()
        {
            _count.color = Color.white;
            var ink = _count.gameObject.AddComponent<Outline>();
            ink.effectColor = new Color(0f, 0f, 0f, .95f);
            ink.effectDistance = new Vector2(2.5f, 2.5f);
            ink.useGraphicAlpha = true;
            return this;
        }

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

            Show(_prefix + n);
        }

        /// <summary>
        /// Writes a word instead of a count - the welcome door says NEW until a turret is
        /// waiting, then the count. An empty word hides the badge, as nought does.
        /// </summary>
        public void Paint(string word)
        {
            if (!Root) return;

            if (string.IsNullOrEmpty(word))
            {
                Root.gameObject.SetActive(false);
                _shown = false;
                return;
            }

            Show(word);
        }

        void Show(string text)
        {
            if (_count) _count.text = text;
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
