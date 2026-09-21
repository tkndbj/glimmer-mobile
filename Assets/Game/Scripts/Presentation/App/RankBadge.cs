using GlimmerGrove.Localization;
using GlimmerGrove.Ranks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The rank a keeper holds, on the map: the badge, its name, and the way to the page that
    /// explains every one of them.
    ///
    /// <para>
    /// <b>A readout, which is to say watched rather than drawn</b> — invariant 44j's rule, which
    /// is about a balance and is about this for the same reason. A rank moves while this screen
    /// is standing: a run finishes and returns to the map, a merge lands another device's
    /// battles, a content push retunes the ladder under a player who is looking at it. Built as
    /// a snapshot it would be a photograph — correct when the map opened and quietly wrong from
    /// then on, which compiles, draws, and passes every fixture because nothing moves during a
    /// test. So it subscribes to <see cref="RankLedger.Changed"/> and repaints.
    /// </para>
    /// <para>
    /// <b>And it is a button, because a badge with no requirements beside it is a puzzle.</b>
    /// The one question a rank invites is "what do I have to do for the next one", and this is
    /// the only place on the map that can be asked it. The whole block is the target rather than
    /// a chip inside it: it is small already, and there is nothing else up here to hit by
    /// mistake.
    /// </para>
    /// <para>
    /// <b>It draws the first rung, unearned, for an account below it</b> rather than an empty
    /// seat. A player who has not reached Cinderling is exactly the player this is for, and a
    /// blank corner invites nobody. <b>It draws it solid</b>, at the owner's instruction: the
    /// faded stand-in read as art that had failed to load rather than as a rank not yet earned,
    /// which is the very reading <see cref="RankLook.Ghost"/>'s own remark makes about a faded
    /// card. What says <em>not yet</em> here is the word under it — <c>Unranked</c>, in a
    /// quieter cream than a rank's gold — which is one solid thing instead of one faint one.
    /// The profile's medallion and the ranks page's hero still ghost theirs; only the map's
    /// corner changed.
    /// </para>
    /// <para>
    /// <b>The map draws it on the ranked lane only.</b> <see cref="LevelsScreen"/> decides that,
    /// not this type: a rank is a reading of play and every lane feeds it, but the corner it
    /// sits in belongs to whichever ladder is being looked at, and on the ordinary one it was a
    /// badge beside a chapter map that says nothing about ranks. Nothing here is per-lane, so a
    /// second caller that wants it somewhere else costs one line.
    /// </para>
    /// <para>
    /// <b>It shows nothing at all when the ladder is empty</b>, which is the honest answer to a
    /// content file that carries no <c>ranks</c> block: no built-in ladder stands in for it
    /// (<see cref="RankLadder"/>), so there is nothing true to draw.
    /// </para>
    /// </summary>
    public sealed class RankBadge : MonoBehaviour
    {
        /// <summary>
        /// Builds the badge under <paramref name="parent"/> and watches it for as long as it
        /// lives.
        ///
        /// <para>
        /// <b>Safe to call more than once</b>, for <c>BoostReadout.Attach</c>'s reason: a screen
        /// that rebuilds its chrome calls this again from the same place, and a second component
        /// would be a second subscription drawing the same badge twice.
        /// </para>
        /// </summary>
        public static RankBadge Attach(Component host, RectTransform parent,
                                       Vector2 anchor, Vector2 position)
        {
            if (host == null || parent == null) return null;

            var badge = host.GetComponent<RankBadge>();
            if (badge != null) { badge.Repaint(); return badge; }

            badge = host.gameObject.AddComponent<RankBadge>();
            badge.Build(parent, anchor, position);

            // AddComponent has already run OnEnable against a null root, so the subscription is
            // standing and this is the paint that matters.
            badge.Hook();
            badge.Repaint();

            return badge;
        }

        // ------------------------------------------------------------------ the parts
        RectTransform _root;

        /// <summary>
        /// The layer this was built into, kept for the promotion toast and for nothing else.
        ///
        /// <b>A toast may not be parented to the badge.</b> `Scenery.Toast` anchors at the foot
        /// of whatever it is given and is drawn at its own full width, so hung off this
        /// 192-unit block it would be a screen-wide bar centred on the left margin. The layer
        /// is the screen's safe area, which is what every other toast in the game is given.
        /// </summary>
        RectTransform _layer;

        Image _mark;
        Image _halo;
        Text _name;

        bool _hooked;
        string _shown;

        void Build(RectTransform parent, Vector2 anchor, Vector2 position)
        {
            _layer = parent;
            _root = UIKit.Box("RankBadge", parent, new Vector2(Width, Height), anchor, position);

            // The whole block is the target. `Art.Pixel` at full transparency is the house way
            // of making a region tappable without drawing anything over it.
            var tap = UIKit.Button("Tap", _root, Art.Pixel, new Vector2(Width, Height),
                                   new Vector2(.5f, .5f), Vector2.zero, Open);
            tap.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            _halo = UIKit.Img("Halo", _root, Art.Glow(128, 2.1f), Pal.A(Pal.Sun, 0f),
                              Vector2.one * (MarkSize * 1.5f), new Vector2(.5f, 1f),
                              new Vector2(0f, -MarkSize * .5f));

            _mark = UIKit.Img("Mark", _root, null, Color.white,
                              Vector2.one * MarkSize, new Vector2(.5f, 1f),
                              new Vector2(0f, -MarkSize * .5f));
            _mark.preserveAspect = true;
            _mark.raycastTarget = false;

            _name = UIKit.Titled("Name", _root, string.Empty, 32, Pal.Gold,
                                 TextAnchor.MiddleCenter,
                                 new Vector2(Width, NameHeight), new Vector2(.5f, 1f),
                                 new Vector2(0f, -(MarkSize + NameHeight * .5f)),
                                 outline: 0f, shadow: 2f);
            _name.raycastTarget = false;
            UIKit.Shrinkable(_name, 18);
        }

        static void Open()
        {
            if (Flow.HasModal || Flow.Busy) return;
            Flow.Go<RanksScreen>();
        }

        // ------------------------------------------------------------------ the watch
        void OnEnable() { Hook(); Repaint(); }

        void OnDisable()
        {
            if (!_hooked) return;

            RankLedger.Changed -= Repaint;
            RankLedger.Promoted -= OnPromoted;
            _hooked = false;
        }

        void Hook()
        {
            if (_hooked) return;

            RankLedger.Changed += Repaint;
            RankLedger.Promoted += OnPromoted;
            _hooked = true;
        }

        /// <summary>
        /// A rung reached while the map is standing: a rank moved by something that is not a
        /// run — a season chest claimed, a merge landing another device's play, a content push.
        /// A pop and a halo rather than a modal, because nothing was claimed and there is
        /// nothing to dismiss.
        ///
        /// <para>
        /// <b>The sentence is <see cref="RankCeremony"/>'s when it is going to say it</b>, which
        /// since the ceremony shipped is nearly always: a rung reached anywhere gets its
        /// ceremony at the end of the next run. Two announcements of one promotion is one
        /// announcement too many, and the quieter of the two is the one that gives way — the
        /// toast would also spoil the ceremony, which is the whole reason the order matters
        /// rather than merely the count.
        /// </para>
        /// <para>
        /// <b>The pop and the halo stay either way.</b> This is a readout (invariant 44j) and a
        /// readout draws the truth the moment the truth moves; what it gives up is the
        /// <em>telling</em>, not the drawing. And when the ceremony is not going to speak — a
        /// build with nothing hooked, a ladder retune, a reading that belongs to another account
        /// — the toast is still the only thing that would say so, so it is asked rather than
        /// deleted.
        /// </para>
        /// </summary>
        void OnPromoted(RankDefinition rung)
        {
            Repaint();
            if (_root == null || !_root.gameObject.activeSelf) return;

            Tween.Pop(_mark.transform, 0f, .55f, 0f);

            if (_halo != null)
            {
                _halo.color = Pal.A(Pal.Sun, .55f);
                Tween.Tint(_halo, Pal.A(Pal.Sun, 0f), 1.4f);
            }

            if (_layer != null && !RankCeremony.Speaks(rung))
                Scenery.Toast(_layer, Loc.Format("ui.ranks.promoted", rung.Name), Pal.Gold, 2.4f);
        }

        /// <summary>
        /// Draws the state: which badge, whether it is earned, and what it is called.
        ///
        /// Raised by the subscription, so it must be cheap and must never assume it is being
        /// called because something changed — every screen's readouts repaint when a merge
        /// lands. The guard is the badge's own id plus whether it is held, which is the whole
        /// of what is drawn.
        /// </summary>
        void Repaint()
        {
            if (_root == null) return;

            var ladder = RankLedger.Ladder;
            bool drawn = !ladder.IsEmpty;

            if (_root.gameObject.activeSelf != drawn) _root.gameObject.SetActive(drawn);
            if (!drawn) { _shown = null; return; }

            var held = RankLedger.Held;
            var rung = held ?? ladder.At(1);
            if (rung == null) { _root.gameObject.SetActive(false); return; }

            string state = rung.Id + (held != null ? "+" : "-");
            if (state == _shown) return;
            _shown = state;

            _mark.sprite = Art.S(rung.Icon);

            _name.text = held != null ? rung.Name : Loc.Get("ui.ranks.unranked");
            _name.color = held != null ? Pal.Gold : Pal.A(Pal.Cream, .62f);
        }

        // ------------------------------------------------------------------ the layout
        /// <summary>
        /// Written down rather than scattered, for <c>BoostReadout</c>'s reason: absolute offsets
        /// inside a build method are how two blocks come to print through one another.
        /// </summary>
        /// <summary>
        /// <b>Twice the size it was cut at, at the owner's instruction.</b> A rank badge is a
        /// silhouette rather than a face — the argument <c>LeaderboardScreen</c> makes for a
        /// 132-unit badge on a 176-unit row — so at 92 in a corner the seven of them were one
        /// smudge, and the one thing the map could say about a keeper said nothing. The width is
        /// what bounds the growth: the block is centred on <c>LevelsScreen.CornerX</c>, which is
        /// 96 from the safe area's own edge, so a box wider than 192 hangs a centred rank name
        /// off the side of the screen.
        /// </summary>
        const float MarkSize = 184f, NameHeight = 44f;
        const float Width = 192f;

        /// <summary>
        /// The whole block's height, public for <c>BoostReadout.Height</c>'s reason: whoever
        /// places this has to know it, because <c>UIKit.Box</c> always pivots at centre, and a
        /// caller typing its own copy would go on placing it against the size it used to be.
        /// </summary>
        public const float Height = MarkSize + NameHeight;

    }
}
