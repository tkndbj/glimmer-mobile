using UnityEngine;
using UnityEngine.UI;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;

namespace GlimmerGrove
{
    /// <summary>
    /// The fight, drawn: what a boss looks like arriving, turning a stand and falling.
    ///
    /// <para>
    /// <b>Every beat here is read off the model as a state and drawn on the edge</b>, never
    /// raised by an event. A phase turns on a bolt inside <c>Advance</c> and just as readily on
    /// a firepot, a bomb or an overcharge outside it, where nothing is reporting - so a view that
    /// waited to be told would miss half the turns. <see cref="Fight"/> compares what the board
    /// says with what the widget last drew, once a frame, and draws the difference (a repaint is
    /// a drawing of a state, invariant 48l).
    /// </para>
    /// <para>
    /// <b>What the beats are for.</b> Played, the bosses died on the walk in or before their
    /// first spell, and what the owner saw was "no boss fight". The rules now promise a walk in
    /// that cannot be hurt and a floor under every stand (<see cref="SiegeTuning.BossPhases"/>);
    /// what this file owes those promises is that each of them is <em>visible</em> - and the
    /// whole of what makes that affordable is that the line is <em>firing</em> throughout. A
    /// stand is said by the bar walking down to its notch and resting there while the boss casts,
    /// which is a thing the player is doing; it used to be said by a ring, which was a thing
    /// drawn over a hill where nothing was happening.
    /// </para>
    /// <para>
    /// <b>Slow motion is the one instrument used on every beat</b>, and it is affordable only
    /// because <c>Dilate</c> bends the clock the model is handed (invariant 37cq): the hill, the
    /// wards, the muster and the fuel all slow together and nothing about the run changes.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>How thick a phase mark is, in cells.</summary>
        const float MarkWide = .05f;

        /// <summary>
        /// Draws every boss's fight up to date: the plant and the phase.
        ///
        /// Called once a frame after <c>Follow</c>, on the same unscaled seconds every drawing
        /// here runs on (invariant 30h - a modal freezes <c>Time.timeScale</c>).
        /// </summary>
        void Fight(float dt)
        {
            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || !mob.Boss || mob.Falling || mob.Node == null) continue;

                var raider = _board.Find(mob.Id);
                if (raider == null || !raider.Alive) continue;

                mob.Drawn += dt;

                var fire = Casting(mob.Kind);
                var at = mob.Node.anchoredPosition;

                // **The plant.** The frame it reaches its ground is the frame it stops being a
                // thing walking toward the line and becomes the thing the line is fighting.
                if (!mob.Arrived && raider.InPlace)
                {
                    mob.Arrived = true;
                    Planted(mob, at, fire);
                }

                // **The phase.** Read as state, for the reason at the top of the file. The first
                // phase opens on the plant and is drawn by it; every one after is a turn.
                if (raider.Phase != mob.Phase)
                {
                    int was = mob.Phase;
                    mob.Phase = raider.Phase;
                    if (was >= 0) Turned(mob, raider, was, at, fire);
                }

                // **Nothing here draws a guard, because there is no longer one to draw.** A
                // boss's stand is a floor under its health, and the line fires at it throughout -
                // so what says "this stand is not finished" is the bar sitting on its notch and
                // the boss visibly casting, both of which are already drawn. The ring that used
                // to spin and breathe here is gone in the same change that took the
                // invulnerability out of the rules (invariant 37dn's note in `SiegeBoard.Fight`).

                // **The last phase glows.** A boss in its last third keeps a low light in the
                // glow it gathers spells in, so "enraged" is a thing on the body and not only a
                // word that floated by.
                if (mob.Phase >= SiegeTuning.BossPhases - 1 && mob.Charge != null && !Throwing(mob))
                {
                    float pulse = .22f + Mathf.Sin(mob.Drawn * 4f) * .08f;
                    var glow = mob.Charge;
                    if (glow.color.a < pulse) glow.color = Pal.A(fire, pulse);
                }
            }
        }

        /// <summary>
        /// The frame a boss stands: it plants, the ground answers, and the hill slows for a beat.
        /// </summary>
        void Planted(Mob mob, Vector2 at, Color fire)
        {
            var feet = at + new Vector2(0f, -mob.Height * .38f);

            Shockwave(feet, Pal.Lift(fire, .3f), 6.2f, .46f);
            Burst.Sparks(_fx, feet, Pal.Rope, 16, Cell * 2.4f, Cell * .18f, .5f);

            if (mob.Body != null) Tween.Punch(mob.Body.rectTransform, .14f, .42f);
            if (mob.Crown != null) Tween.Punch(mob.Crown, .06f, .3f);

            ShakeBoard(20f);
            Dilate(.35f, .42f);

            Audio.Sfx("boom", .7f, Pitch(mob.Kind) - .2f);
        }

        /// <summary>
        /// A phase turning: the roar. The mark on the bar bursts, the hill slows, the screen
        /// takes the boss's colour, and the last turn says so.
        ///
        /// <b>Louder each time</b> - the shake, the flash and the pause all grow with the phase -
        /// because the fight climbs (<see cref="SiegeTuning.PhasePaceHundredths"/>) and the
        /// drawing has to climb with it or the third roar is the first one again.
        /// </summary>
        void Turned(Mob mob, SiegeRaider raider, int was, Vector2 at, Color fire)
        {
            int phase = raider.Phase;
            bool last = phase >= SiegeTuning.BossPhases - 1;

            Dilate(.28f, .40f + .08f * phase);
            ShakeBoard(24f + 6f * phase);
            Flow.Flash(Pal.A(fire, 1f), .30f + .08f * phase, .4f);

            Burst.Sparks(_fx, at, fire, 22, Cell * 3.6f, Cell * .26f, .6f);
            Crackle(mob, fire, 2.2f, 4);

            if (mob.Body != null) Tween.Punch(mob.Body.rectTransform, .2f, .45f);

            Notch(mob, was, fire);

            Audio.Sfx("roar", .8f + .1f * phase, Pitch(mob.Kind) + .1f - .06f * phase);

            // **The word is white, and the flash above it is the boss's colour.** Same rule
            // as the arrival banner: a caption in `fire` is unreadable for the half of the
            // cast whose hue is darker than the hill, and this one lands at the moment the
            // screen is already wearing that colour anyway.
            if (last) Announce(Loc.Get("mode.siege.enraged"), Color.white, .62f, 1.4f, true);
        }

        /// <summary>
        /// The marks on a boss's bar, one at every phase threshold, in the trough's ink.
        ///
        /// Placed by the same arithmetic the fill is drawn with (`Follow`), so a mark sits on
        /// the pixel the fill reaches when the phase turns.
        /// </summary>
        Image[] Marks(Mob mob)
        {
            int marks = SiegeTuning.BossPhases - 1;
            if (marks <= 0 || mob.Bar == null) return null;

            var made = new Image[marks];
            float wide = mob.Bar.sizeDelta.x - 4f;

            for (int p = 0; p < marks; p++)
            {
                float share = (SiegeTuning.BossPhases - 1 - p) / (float)SiegeTuning.BossPhases;

                var mark = UIKit.Img("Mark", mob.Bar, Art.Round(4), new Color(0f, 0f, 0f, .78f),
                                     new Vector2(Cell * MarkWide, mob.Bar.sizeDelta.y - 2f));
                mark.raycastTarget = false;
                mark.type = Image.Type.Sliced;
                mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0f, .5f);
                mark.rectTransform.anchoredPosition = new Vector2(2f + wide * share, 0f);

                made[p] = mark;
            }

            return made;
        }

        /// <summary>The mark a blow has just reached bursts, and the bar flashes to it.</summary>
        void Notch(Mob mob, int was, Color fire)
        {
            if (mob.Marks == null || was < 0 || was >= mob.Marks.Length) return;

            var mark = mob.Marks[was];
            if (mark == null || mob.Crown == null || mob.Bar == null) return;

            // Where the mark is on the effects layer: the crown is a child of it, the bar of
            // the crown, and the mark of the bar with its anchor at the bar's left end.
            var spot = mob.Crown.anchoredPosition + mob.Bar.anchoredPosition
                     + new Vector2(mark.rectTransform.anchoredPosition.x - mob.Bar.sizeDelta.x * .5f, 0f);

            Burst.Sparks(_fx, spot, Pal.Lift(fire, .4f), 10, Cell * 1.6f, Cell * .14f, .4f);
            Pop(spot, fire, 1.6f, .3f);

            if (mob.Fill != null)
            {
                var fill = mob.Fill;
                var was_ = fill.color;
                Tween.Run(.35f, Ease.OutQuad, t =>
                {
                    if (!fill) return;
                    fill.color = Color.Lerp(Color.white, was_, t);
                }, fill, "flash");
            }

            Tween.Punch(mob.Crown, .08f, .35f);
        }
    }
}
