using System.Collections.Generic;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The bombs a bomber leaves on the hill, and the tap that sets one off.
    ///
    /// <para>
    /// <b>The only thing in this mode the player touches on the enemy's own ground.</b> Every
    /// other input goes into the gem field; a bomb has to be spotted on the hill and hit, which is
    /// what makes the hill something to watch rather than something to glance at.
    /// </para>
    /// <para>
    /// <b>It goes off at once.</b> There is nothing to aim — the bomb is already somewhere, and
    /// asking the player to pick a target after tapping it would be asking them to choose twice
    /// for one decision. The decision is <em>when</em>.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>A bomb's own widget: the picture, its glow, and the button under it.</summary>
        sealed class Fuse
        {
            public int Id;
            public RectTransform Node;
            public Image Body;
            public Image Glow;
        }

        readonly List<Fuse> _fuses = new List<Fuse>(4);

        /// <summary>
        /// What one bomb delivers, handed back so the run can be charged for it.
        ///
        /// <b>The screen owns it, for <c>Fire</c>'s reason</b>: what a blast is worth in matches is
        /// invariant 39's arithmetic and what it costs the grade is the run's, and neither is a
        /// board's business. A bomb differs from a firepot in exactly one way — it spends no stock
        /// — so it gets a hook of its own rather than a flag inside the one every owned utility
        /// goes through.
        /// </summary>
        public System.Func<int, List<SiegeStrike>, SiegeUse> Blew { get; set; }

        /// <summary>
        /// Draws a bomb arriving where a bomber has just died.
        ///
        /// <para>
        /// <b>It lands as wreckage rather than as a prize</b> — it drops the last of the way,
        /// bounces, and the hill shakes — because what just happened is that something the player
        /// killed came apart. The <em>prize</em> reading is carried by what it then does: it sits
        /// there pulsing in its own colour, which is the one thing on the hill that moves while
        /// nothing else is happening.
        /// </para>
        /// </summary>
        void Dropped(SiegeBomb bomb)
        {
            var fuse = new Fuse { Id = bomb.Id };

            fuse.Node = UIKit.Node("Bomb", _fuseLayer);
            fuse.Node.anchorMin = fuse.Node.anchorMax = new Vector2(.5f, .5f);
            fuse.Node.sizeDelta = new Vector2(Cell, Cell);
            fuse.Node.anchoredPosition = BoxAt(bomb.Lane, bomb.Row);

            var tint = TintOf(bomb.Colour);

            fuse.Glow = UIKit.Img("Glow", fuse.Node, Art.Glow(96, 2.1f), Pal.A(tint, .0f),
                                  new Vector2(Cell * 2.0f, Cell * 2.0f));
            fuse.Glow.raycastTarget = false;

            fuse.Body = UIKit.Img("Bomb", fuse.Node, Art.S("Ui/Utility/firepot"), Color.white,
                                  new Vector2(Cell * .92f, Cell * .92f));
            fuse.Body.preserveAspect = true;
            fuse.Body.raycastTarget = true;

            var btn = fuse.Body.gameObject.AddComponent<Btn>();
            btn.PressScale = .92f;
            btn.Setup(() => Tapped(bomb.Id), silent: true);

            _fuses.Add(fuse);

            // Coming apart: it is thrown clear of the wreck and settles.
            fuse.Node.localScale = new Vector3(.3f, .3f, 1f);
            Tween.Scale(fuse.Node.transform, 1f, .34f, Ease.OutBack);
            Tween.Move(fuse.Node, fuse.Node.anchoredPosition, .01f);

            Burst.Sparks(_fx, fuse.Node.anchoredPosition, tint, 9, Cell * 1.5f, .45f);
            Audio.Sfx("land", .5f);
            ShakeBoard(.12f);

            // **And then it asks to be tapped, for as long as it stands.** A still picture of a
            // bomb on a hill full of walking monsters is a picture nobody looks at; the breath and
            // the glow behind it are the whole of how it says it is a control rather than scenery.
            Tween.After(.36f, () =>
            {
                if (fuse.Body == null) return;
                Tween.Breathe(fuse.Body.transform, .09f, 1.15f);
                if (fuse.Glow != null) Tween.Fade(fuse.Glow, .55f, .5f);
            }, this);
        }

        /// <summary>
        /// Sets a bomb off where it stands.
        ///
        /// <para>
        /// <b>Refused rather than spent when it caught nothing</b>, exactly as a firepot is: a
        /// bomb that went off on bare hill and left the player with nothing would read as the game
        /// taking something, so it stays where it is and says no.
        /// </para>
        /// </summary>
        void Tapped(int id)
        {
            if (!Playable || Blew == null) return;

            HideCoach();
            Stir();

            // **Where it stands is read before it goes off**, because the board takes it off the
            // list the moment it lands - and the drawing has to happen at the box it was in.
            int lane = -1, row = -1;
            var bombs = _board.Bombs;
            for (int i = 0; i < bombs.Count; i++)
                if (bombs[i].Id == id) { lane = bombs[i].Lane; row = bombs[i].Row; break; }

            if (lane < 0) return;

            _strikes.Clear();
            var use = Blew(id, _strikes);

            if (!use.Landed)
            {
                var missed = FuseOf(id);
                if (missed != null && missed.Body != null) Refuse(missed.Body.rectTransform);
                Rejected?.Invoke();
                return;
            }

            var fuse = FuseOf(id);

            if (fuse != null)
            {
                _fuses.Remove(fuse);
                if (fuse.Node != null) Destroy(fuse.Node.gameObject);
            }

            Charged(use.Matches);

            // **The firepot's own drawing, because it is the firepot's own blast** — the same
            // scorch over the same boxes, the same burst, the same clip. A player who has thrown
            // one knows what this looks like, and a second explosion for one effect is a second
            // thing to learn about something they already know.
            Firepot(SiegeAim.OnTheHill(lane, row));

            for (int i = 0; i < _strikes.Count; i++) Hurt(_strikes[i]);

            Judge();
        }

        /// <summary>Keeps the drawn bombs in step with the board's, and nothing else.</summary>
        void Fuses()
        {
            for (int i = _fuses.Count - 1; i >= 0; i--)
            {
                var fuse = _fuses[i];
                bool held = false;

                var bombs = _board.Bombs;
                for (int b = 0; b < bombs.Count; b++)
                    if (bombs[b].Id == fuse.Id) { held = true; break; }

                if (held) continue;

                _fuses.RemoveAt(i);
                if (fuse.Node != null) Destroy(fuse.Node.gameObject);
            }
        }

        Fuse FuseOf(int id)
        {
            for (int i = 0; i < _fuses.Count; i++)
                if (_fuses[i].Id == id) return _fuses[i];

            return null;
        }
    }
}
