using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The charms: the stone a charmed gem is, and what each of them looks like going off.
    ///
    /// <para>
    /// <b>Its own file because a charm is drawn in three places at once</b> — on the gem, across
    /// the field, and up at the ward line — where every other thing on this board belongs to one
    /// of those. Folding it into <c>SiegeView.Field</c> would have put the hill's half of a
    /// stormglass inside the file about the jewels.
    /// </para>
    /// <para>
    /// <b>Every drawing here is a replay of what the model already decided</b> (invariant 30i).
    /// A lance's cross arrives as cells in <c>SiegeBeat.Cleared</c> and the charm that took them
    /// arrives as a <see cref="SiegeSpark"/>; a stormglass's volley arrives, a beat later, as
    /// ordinary <see cref="SiegeBolt"/>s in <c>SiegeReport.Charmed</c>. Nothing here works anything
    /// out, so nothing here can disagree with the board about what happened.
    /// </para>
    /// <para>
    /// <b>The whole of this file is the second cut, and the first one's verdict was one word.</b>
    /// What was wrong was not one number; it was four things, and only one of them was a taste.
    /// <b>One</b>: a charmed gem was an ordinary gem with a white glyph printed on it — answered
    /// by cutting a different stone in the same colour (<c>CharmFace</c>). <b>Two</b>: a charm
    /// detonated in <c>hit_{c}</c>, the ward impact, which is framed at 192 pixels because a lit
    /// line lands eighteen of them a second — drawn here at four and a half cells, so the biggest
    /// moment on the field was a small reel blown up two and a half times
    /// (<c>SiegeShotBake.Charms</c>). <b>Three</b>: <see cref="Shockwave"/>'s third argument is a
    /// <em>scale</em>, and every call in this file passed it <c>Cell * n</c> — so every charm fired
    /// a ring scaled to five hundred times a cell, which is a white flash over the whole screen
    /// rather than an impact. <b>Four</b>: the beam a lance drew was a still gradient bar, and the
    /// volley a stormglass loosed came out of the turrets rather than out of the gem.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the stone
        /// <summary>How much of a cell a charm's halo covers.</summary>
        const float RingInset = 1.18f;

        /// <summary>
        /// How much of a cell a charmed gem is drawn at, against <c>GemInset</c> for a plain one.
        ///
        /// <b>A shade larger, because both charmed cuts are pointed or round where the four are
        /// broad.</b> Fitted to the same box a star and an orb draw visibly smaller than the stones
        /// beside them, which would say a charm is a lesser gem — and it is the opposite. The art
        /// tool already cuts them at 0.94 of their canvas against a plain gem's 0.88; this is the
        /// other half of the same correction, applied where the cell is measured.
        /// </summary>
        const float CharmInset = 1.06f;

        /// <summary>
        /// Raised the first time each kind of charm is put on this field, with the kind.
        ///
        /// <b>Once a run and once a kind</b>, because what it is for is a lesson — and a lesson
        /// holds the board (<c>RunHold.Teaching</c>), so one raised on every refill would stop a
        /// siege dead every few seconds. The screen decides what to do with it; this only says
        /// that one has arrived (invariant 6a).
        /// </summary>
        public System.Action<SiegeCharm> Dealt;

        readonly HashSet<SiegeCharm> _announced = new HashSet<SiegeCharm>();

        /// <summary>
        /// Kinds that have arrived on the field and have not been announced yet.
        ///
        /// <b>Held rather than raised where the charm is put on the gem</b>, which is invariant
        /// 6b's whole rule and the one the bomber's lesson was already rebuilt for. A refill is
        /// minted <em>mid-fall</em>: the gem is above the board, the cell it is falling into still
        /// holds whatever was there before, and <see cref="LiveCharm"/> asked at that moment rings
        /// a gem that is somewhere else or nothing at all. So the arrival is remembered and
        /// announced by <see cref="Flush"/>, which the repaint that settles the board calls.
        /// </summary>
        readonly List<SiegeCharm> _newly = new List<SiegeCharm>(3);

        /// <summary>
        /// Announces any charm that has arrived since the last time the board came to rest.
        ///
        /// <b>Called by <c>Repaint</c> and by nothing else</b>, because a repaint <em>is</em> the
        /// board settling: it dresses and places every cell, so by the time this runs the gem a
        /// lesson has to ring is in its socket at its final position. Every other path that could
        /// raise one — the mint, the fall, the cascade — has the gem in the air.
        /// </summary>
        void Flush()
        {
            if (_newly.Count == 0 || Dealt == null) return;

            for (int i = _newly.Count - 1; i >= 0; i--)
            {
                // **Held back until there is one to ring, rather than spent on a board that has
                // none.** A charm dealt into a refill can be taken by the very cascade that
                // dealt it, and a lesson is offered once in a player's life (the cog's own rule) -
                // so announcing one with nothing to point at spends it on a panel that teaches
                // about a thing the player is not looking at. It keeps until the next time the
                // board settles with one standing, which on a field that deals them is a handful
                // of matches away.
                if (LiveCharm(_newly[i]) == null) continue;

                Dealt(_newly[i]);
                _newly.RemoveAt(i);
            }
        }

        /// <summary>
        /// A gem carrying this kind of charm, <b>standing in its own socket</b>, for a lesson to
        /// ring — or null when none is.
        ///
        /// <para>
        /// <b>Asked at the moment the tip goes up rather than remembered</b>, which is
        /// <see cref="LiveBomb"/>'s rule: a charm can be matched away between the hook firing and
        /// the panel opening, and a null here is a tip that teaches without pointing rather than a
        /// ring drawn round the wrong gem.
        /// </para>
        /// <para>
        /// <b>And settled, which is the half that is easy to leave out.</b> A gem on this board is
        /// falling most of the time — a refill is minted above the field and moved into its socket
        /// over the next third of a second — so the board holding a charm at a cell says nothing
        /// about where its picture is. A ring drawn round a gem in the air is a ring that starts
        /// beside the board and then slides, which is worse than no ring at all. The announcement
        /// is already deferred to a repaint (<see cref="Flush"/>); this is the same rule said where
        /// the anchor is chosen, so a caller that ever raises one early cannot produce that
        /// picture.
        /// </para>
        /// </summary>
        public RectTransform LiveCharm(SiegeCharm charm)
        {
            if (_board == null) return null;

            for (int i = 0; i < _gems.Count && i < Width * Height; i++)
            {
                if (_board.CharmAt(i) != charm) continue;

                var gem = _gems[i];
                if (gem == null || gem.Img == null) continue;

                // A tenth of a cell, because a tween that has finished lands exactly and one that
                // has not is most of a cell away. Nothing here is trying to catch the last frame
                // of a fall; it is trying not to ring something halfway up the screen.
                var rt = gem.Img.rectTransform;
                if ((rt.anchoredPosition - CentreOf(i)).sqrMagnitude > Cell * Cell * .01f) continue;

                return rt;
            }

            return null;
        }

        /// <summary>
        /// Shows, hides or changes the stone and the halo on one gem.
        ///
        /// <para>
        /// <b>The face is the charm, which is what changed.</b> A lance and a stormglass are
        /// <em>gems of their own</em> in the colour they are worth, so what this does to a gem that
        /// gains one is swap its sprite — not hang a glyph off it. When the charm goes the plain
        /// face comes back, which is the case a mark never had to handle and this does: a cascade
        /// can leave a charm's colour standing where the charm itself has been taken.
        /// </para>
        /// <para>
        /// <b>The halo is minted lazily and then kept</b>, exactly as the weaver's web was: most
        /// cells never carry a charm, and a field that does gains and loses them all run. It is a
        /// child of the gem, so it falls with it and nothing has to keep a second list in step —
        /// which is the same bargain the board strikes by carrying <c>_charms</c> through
        /// <c>Collapse</c>.
        /// </para>
        /// <para>
        /// <b>And it is tinted to the gem's own colour and never to white.</b> A charm is still
        /// worth the colour it is (invariant 37f), and a white glow round a red gem is the one
        /// reading that would make a player think it had stopped being red. The prism is the
        /// exception and takes cream, because it has no colour to agree with.
        /// </para>
        /// </summary>
        void Charmed(Gem gem, SiegeCharm charm)
        {
            if (gem == null || gem.Img == null) return;

            bool fresh = gem.Charm != charm;
            gem.Charm = charm;

            // **Remembered here and announced when the board settles** (`Flush`). This runs while
            // the gem is still falling - a refill is minted above the field and moved into its
            // socket over the next third of a second - so a lesson raised from here would ring a
            // cell that does not hold it yet. Invariant 6b, which the bomber's tip already paid
            // for once: it used to ring the bomber rather than the bomb, and arrived seconds
            // before the thing it told the player to tap existed.
            if (charm != SiegeCharm.None && _announced.Add(charm)) _newly.Add(charm);

            bool lit = charm != SiegeCharm.None;

            // **The stone itself.** `CharmFace` answers null for a prism and for no charm at all,
            // which is the right shape rather than a gap in one: a prism is already a face in
            // `GemArt` (it is the one gem here that is not a colour) and a plain cell is whatever
            // its colour says. So a null means "the face `Dress` chose is the right one" and this
            // only ever overrides it.
            var face = CharmFace(gem.Colour, charm);
            if (face != null) gem.Img.sprite = face;
            else if (fresh && !lit) gem.Img.sprite = GemArt(gem.Colour);

            // A charmed stone is drawn a shade larger - see `CharmInset` - and a cog is drawn
            // smaller, which is why this asks the face rather than assuming a cell.
            //
            // **Compared rather than gated on `fresh`.** `Dress` resizes a cell whenever its
            // *colour* moves and knows nothing about charms, so a shuffle that carries a charm onto
            // a new colour (`SiegeBoard.Trade`) leaves a charmed stone at a plain stone's size -
            // which is a star drawn a twentieth small, visible only as a board that does not quite
            // line up.
            if (gem.Colour != CogColour)
            {
                float side = Cell * GemInset * (face != null ? CharmInset : 1f);
                if (!Mathf.Approximately(gem.Img.rectTransform.sizeDelta.x, side))
                    gem.Img.rectTransform.sizeDelta = new Vector2(side, side);
            }

            if (gem.Ring == null)
            {
                if (!lit) return;

                // Behind the jewel: a halo drawn over a gem would wash out the face that says what
                // the cell is worth. `SetAsFirstSibling` rather than a second layer, because the
                // gem is already the only child this node has.
                gem.Ring = UIKit.Img("Charm halo", (RectTransform)gem.Img.transform,
                                     Piece("charm_ring"), Color.white,
                                     new Vector2(Cell * RingInset, Cell * RingInset));
                gem.Ring.raycastTarget = false;
                gem.Ring.preserveAspect = true;
                gem.Ring.rectTransform.SetAsFirstSibling();
            }

            var tint = gem.Colour >= 0 && gem.Colour < SiegeLayout.Letters.Length
                     ? TintOf(gem.Colour)
                     : Pal.Cream;

            gem.Ring.color = Pal.A(Pal.Lift(tint, .55f), lit ? .85f : 0f);
            gem.Ring.enabled = lit;

            // **The arrival is drawn and the departure is not**, which is the web's rule read
            // across: a charm landing is worth a beat, and one leaving is already the biggest thing
            // on the screen (see `Sprung`).
            //
            // **And it is the gem that pops, never the halo**, which is not a taste: `Breathe`
            // writes the halo's scale on every frame, so a tween on the same transform would be
            // overwritten before it drew - a pop that silently does nothing, which is worse than
            // no pop at all.
            if (lit && fresh) Tween.Pop(gem.Img.transform, 0f, .34f);
        }

        /// <summary>
        /// The soft breathing a charmed gem does while it is standing there.
        ///
        /// <b>On the halo and never on the gem</b>, so a charm is the one thing on this field that
        /// moves while nobody is playing — which is what makes the eye find it — without the jewel
        /// under it changing size, because a gem that grows and shrinks is a gem whose socket the
        /// player is no longer sure about.
        /// </summary>
        void Breathe(float dt)
        {
            _breath += dt;

            float k = .78f + Mathf.Sin(_breath * 3.1f) * .16f;

            for (int i = 0; i < _gems.Count; i++)
            {
                var gem = _gems[i];
                if (gem == null || gem.Ring == null || !gem.Ring.enabled) continue;

                var c = gem.Ring.color;
                gem.Ring.color = new Color(c.r, c.g, c.b, k);
                gem.Ring.transform.localScale = Vector3.one * (.95f + (k - .78f) * .55f);
            }
        }

        float _breath;

        // ------------------------------------------------------------------ the wavefront
        /// <summary>
        /// The cell a charm sprang from this beat, or -1.
        ///
        /// <b>It exists so the gems come apart in the order the light reaches them.</b>
        /// <c>SiegeBeat.Cleared</c> is a list and the view used to stagger it by <em>index</em>,
        /// which for an ordinary match is exactly right — three touching cells in any order look
        /// the same. A lance takes a whole row and a whole column at once, and index order across
        /// that is an arbitrary scatter: what the player has to see is a beam leaving the stone and
        /// the row failing behind it. So the beat asks <see cref="ClearDelay"/> instead, and it
        /// answers by distance from here.
        /// </summary>
        int _sprang = -1;

        /// <summary>
        /// How long the wavefront takes to cross one cell.
        ///
        /// <b>Two and a half times what it was, and the beat waits for it</b> — see
        /// <see cref="_holdUntil"/>. Eight cells at this is .60s of travel, which is long enough to
        /// watch a row fail from one end to the other rather than long enough to notice that it
        /// did.
        /// </summary>
        const float WaveStep = .075f;

        /// <summary>
        /// The moment the board may refill, as a real-time stamp — or a stamp in the past.
        ///
        /// <para>
        /// <b>A real hold, paired with a real slowdown, which is what makes it affordable.</b> The
        /// first cut of the charms documented a hold and never implemented one, and the note
        /// afterwards said it must never be implemented — because this clock does not stop for a
        /// cascade, so a beat held a second longer is a second of free hill. That argument is
        /// answered rather than repeated: <see cref="Dilate"/> slows the run's own clock for
        /// exactly the same window, so the board is held and the hill is held with it. The model
        /// is handed fewer seconds, not the same seconds later.
        /// </para>
        /// <para>
        /// <b>A deadline rather than a duration, and that is not a style choice.</b> A stormglass
        /// books its volley <c>FuelLands</c> into the future, so how long the fall must wait is not
        /// known when the stone goes off — it is known when the bolts arrive, which is a frame or
        /// twenty later and often while the beat is already waiting. A duration taken once and
        /// counted down cannot be extended from there; a deadline can, and an extension that
        /// arrives after everything has finished is simply a time already past. It also means an
        /// empty hill — a stormglass matched with nothing to shoot at — holds the board for the
        /// charge alone rather than for a barrage that never happens.
        /// </para>
        /// <para>
        /// The later of two wins, for <c>Felling</c>'s reason: a cascade can spring two charms and
        /// the pair is one event lasting as long as its longest part.
        /// </para>
        /// </summary>
        float _holdUntil;

        /// <summary>Holds the fall for at least this long, and never shortens a hold already set.</summary>
        void Holding(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            if (until > _holdUntil) _holdUntil = until;
        }

        /// <summary>How much of the hold is left, or nought.</summary>
        float HoldLeft => Mathf.Max(0f, _holdUntil - Time.unscaledTime);

        /// <summary>
        /// When the gem in <paramref name="cell"/> should come apart, given it is the
        /// <paramref name="index"/>th of this beat's clear.
        ///
        /// <b>Bounded by what the beat is holding for rather than by the beat.</b> The fall used
        /// to start halfway through a <c>SiegeTuning.BeatFor</c>, so a wavefront that ran past that
        /// was gems shattering into cells already refilling — which is why this used to clamp at
        /// a fifth of a second and the wave had to cross eight cells inside it. The beat now waits
        /// for <see cref="_holdUntil"/> before it refills, so the clamp is that instead: the wave
        /// may take the whole window and the board still cannot drop into it.
        /// </summary>
        float ClearDelay(int cell, int index)
        {
            if (_sprang < 0) return index * .012f;

            int ax = _sprang % Width, ay = _sprang / Width;
            int bx = cell % Width, by = cell / Width;

            // Chebyshev rather than Manhattan: a lance's cross is a row *and* a column, so the
            // arm a cell is on should not decide how late it is - only how far along it is.
            int far = Mathf.Max(Mathf.Abs(ax - bx), Mathf.Abs(ay - by));

            return Mathf.Min(far * WaveStep, Mathf.Max(SiegeTuning.BeatFor * .5f, HoldLeft));
        }

        // ------------------------------------------------------------------ going off
        /// <summary>
        /// A charm going off, drawn where it stood.
        ///
        /// <b>Handed the spark rather than the cell</b>, because a prism is paid as the colour of
        /// the run it completed and the letter it was carrying underneath is not that colour —
        /// see <c>SiegeLayout.Runs</c>. What the player has to see is the colour they bought.
        /// </summary>
        void Sprung(SiegeSpark spark)
        {
            var at = CentreOf(spark.Cell);
            var tint = spark.Colour >= 0 ? TintOf(spark.Colour) : Pal.Cream;

            _sprang = spark.Cell;

            switch (spark.Charm)
            {
                case SiegeCharm.Lance:
                    Lanced(spark.Cell, spark.Colour, tint);
                    break;

                case SiegeCharm.Storm:
                    Storming(spark.Cell, spark.Colour, tint);
                    break;

                case SiegeCharm.Furnace:
                    Forging(spark.Cell, spark.Colour, tint);
                    break;

                case SiegeCharm.Hourglass:
                    Sanding(spark.Cell, spark.Colour, tint);
                    break;
                case SiegeCharm.Anvil:
                    Anviling(spark.Cell, spark.Colour, tint);
                    break;

                case SiegeCharm.Prism:
                    // The wild's own moment: it has no second effect, so what it gets is the
                    // biggest burst on the field in the colour it just chose. A prism that looked
                    // like an ordinary match would be a decision the player could not see they had
                    // made (invariant 26f, from the other side).
                    //
                    // **The one charm that is not slowed, and that is deliberate.** A prism has no
                    // second act - nothing travels, nothing is fired - so there is nothing for a
                    // dilated clock to make legible, and a run that crawled for a tenth of a
                    // second several times a minute would read as a stutter rather than as an
                    // effect. It still holds the fall briefly, because a burst drawn into a board
                    // that is already refilling is a burst behind falling gems.
                    Charge(at, tint, PrismCharge);
                    Holding(PrismFor);
                    Detonate(at, spark.Colour, tint, 4.6f, PrismCharge);

                    // **A bell rather than anything in the spark family**, which is invariant
                    // 39e's rule about a firepot not sharing a raider's death: everything else on
                    // this field is a `spark` clip and half of them play several times a second,
                    // so a charm cut from that set would be a cascade at a different pitch. A bell
                    // is the one material this board never uses, which is what makes two or three
                    // of them a run read as news.
                    Audio.Sfx("chime", .58f, 1.28f);
                    break;

                case SiegeCharm.None:
                default:
                    break;
            }
        }

        // ------------------------------------------------------------------ the pieces
        /// <summary>
        /// The breath a charm draws before it goes off: the stone swells, a ring closes on it and
        /// the light gathers.
        ///
        /// <para>
        /// <b>Every charm here starts with this, and it is the half that makes the payoff
        /// readable.</b> A detonation with nothing in front of it is a frame the eye arrives after;
        /// a tenth of a second of something tightening is what makes the player already be looking
        /// at the cell when it goes. It is invariant 37ac's rule — spend the window, do not shorten
        /// it — applied to the front of one rather than the back.
        /// </para>
        /// <para>
        /// <b>The ring closes rather than opening</b>, which is the whole of why it reads as a
        /// charge and not as an impact: everything else on this board throws a ring outward, so an
        /// inward one is the only shape here that says something is <em>about</em> to happen.
        /// </para>
        /// </summary>
        void Charge(Vector2 at, Color tint, float over)
        {
            var ring = UIKit.Img("Charm charge", _fx, Art.Ring(128, 8f),
                                 Pal.A(Pal.Lift(tint, .6f), 0f), new Vector2(Cell, Cell));
            ring.raycastTarget = false;
            ring.rectTransform.anchoredPosition = at;

            var rt = ring.rectTransform;

            Tween.Run(over, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(3.4f, .9f, t);
                ring.color = Pal.A(Pal.Lift(tint, .6f), t < .5f ? t * 2f : 1f);
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });

            Burst.Sparks(_fx, at, tint, 10, Cell * 2.6f, Cell * .22f, over * 1.6f);
            // `lit` rather than a slot of its own: it is the set's one rising note, which is
            // what a wind-up is, and `sfx.tsv` is a fixed vocabulary (`sfxnames.py` proves the
            // code asks for no other name).
            Audio.SfxVaried("lit", .40f, .05f);
        }

        /// <summary>
        /// The detonation itself: the bought pack's own burst, cut for this and nothing else.
        ///
        /// <para>
        /// <b>Its own reel rather than the ward impact it used to borrow.</b> <c>hit_{c}</c> is
        /// framed at 192 pixels because a lit line lands eighteen of them a second; drawn here at
        /// four and a half cells it was a small picture blown up two and a half times, which is
        /// what "the animations are horrendous" is once it is measured. <c>charm_blast_{c}</c> is
        /// baked at 320 over fourteen frames for this moment alone — see
        /// <c>SiegeShotBake.Charms</c>.
        /// </para>
        /// <para>
        /// <b>Eighteen frames a second rather than thirty</b>, which is the same sentence the
        /// window answers — <em>they shouldn't be super fast so we can see them</em> — said about
        /// the drawing rather than about the clock. Fourteen frames at 18fps is .78s of burst,
        /// against .40 at the rate a bolt's impact plays, and the board really does wait for it:
        /// the fall is held and the run's clock is slowed or stopped for the same window
        /// (<c>Dilate</c>).
        /// </para>
        /// <para>
        /// <b>Guarded rather than assumed</b>, which is <c>Shatter</c>'s own idiom and the reason
        /// it exists: an <c>Image</c> with no sprite is a white rectangle rather than a blank
        /// (invariant 7b), and a scope that has not finished arriving is the ordinary case on the
        /// first second of a run.
        /// </para>
        /// </summary>
        void Detonate(Vector2 at, int colour, Color tint, float cells, float after)
        {
            Tween.After(after, () =>
            {
                var frames = CharmBlast(colour);

                if (frames != null && frames.Length > 0)
                {
                    var img = UIKit.Img("Charm blast", _fx, frames[0], Color.white,
                                        new Vector2(Cell * cells, Cell * cells));
                    img.raycastTarget = false;
                    img.preserveAspect = true;
                    img.rectTransform.anchoredPosition = at;
                    img.rectTransform.localRotation =
                        Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                    var book = Flipbook.Attach(img, frames, CharmFps, false);

                    if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
                    else Tween.After(.9f, () => { if (img) Destroy(img.gameObject); });
                }

                // The white core under it, which is what makes a burst read as *light* rather than
                // as a picture of fire: the reel is graded to one of the four gem colours, so the
                // one thing it cannot carry is the part that is hotter than its own colour.
                Pop(at, Pal.Cream, cells * .62f, .22f);

                // **A scale, not a size.** `ProtoView.Shockwave` tweens `localScale` on a
                // cell-sized ring, and every call in the old cut passed it `Cell * n` - a ring
                // scaled five hundred times, which is a white flash over the whole screen and not
                // an impact. Two thirds of the blast, so the ring runs just ahead of the fire.
                Shockwave(at, Pal.Lift(tint, .6f), cells * .72f, .40f);

                Burst.Sparks(_fx, at, tint, 24, Cell * cells * 1.15f, Cell * .30f, .70f);

                ShakeBoard(Cell * .085f);
                Audio.Sfx("boom", .62f, 1.06f);
            });
        }

        /// <summary>
        /// How fast a charm's reel plays.
        ///
        /// <b>Slower than everything else on this field, deliberately.</b> A bolt's impact runs at
        /// 30 because it has to be over before the next one lands; a charm happens a handful of
        /// times in a run and is the thing the player is meant to watch. Eighteen over fourteen
        /// frames is .78s of burst, which is most of the window the beat is held for — and the
        /// window is real, because the run's own clock is slowed or stopped for exactly as long
        /// (<c>Dilate</c>).
        /// </b>
        /// </summary>
        const float CharmFps = 18f;

        // ------------------------------------------------------------------ a lance
        /// <summary>
        /// A lance: a beam down the row and a beam down the column, and the row failing behind
        /// them.
        ///
        /// <para>
        /// <b>Three beats rather than one frame</b>, which is the whole of what makes this read as
        /// something happening. The stone charges (<see cref="Charge"/>); two beams snap open along
        /// the cells the model took; and then the cross <em>fails</em> — a burst on every cell, in
        /// order of distance from the stone, so the destruction visibly travels out from where the
        /// player put it. The gems themselves come apart on the same wavefront, because
        /// <see cref="ClearDelay"/> staggers them by distance whenever a charm sprang this beat.
        /// </para>
        /// <para>
        /// <b>The bursts are the bought pack at the size it was cut for.</b> Each cell gets a ward
        /// impact — 192 pixels drawn at 1.7 cells, which is about native — rather than one big reel
        /// stretched across eight of them. That is the same finding as <see cref="Detonate"/> read
        /// the other way: the reason to bake a bigger reel for the middle is the reason not to blow
        /// the small one up along the arms.
        /// </para>
        /// <para>
        /// <b>The strokes are the cells the model took and nothing else.</b> A cross is a whole row
        /// and a whole column (<c>SiegeBoard.Cross</c>), so the drawn extent is the field's own
        /// width and height — the drawn thing and the played thing are the same two integers,
        /// which is invariant 33g at its strongest.
        /// </para>
        /// </summary>
        void Lanced(int cell, int colour, Color tint)
        {
            var at = CentreOf(cell);

            // **The clock first, so the slowdown is already running when the beams open.** Taken
            // before anything is drawn rather than after: a dilation that arrives a frame into its
            // own effect shows the first frame at full speed, and that is the frame the eye uses to
            // decide how fast the rest of it is going.
            Dilate(LancePace, LanceFor);
            Holding(LanceFor);

            Charge(at, tint, LanceCharge);

            // The two strokes, and they open from the stone outward rather than appearing at
            // length: what a lance does is *go* somewhere, and a bar that is simply there says
            // nothing about where it started.
            Stroke(at, new Vector2(Cell * Width, Cell * BeamThick), tint, LanceCharge);
            Stroke(at, new Vector2(Cell * BeamThick, Cell * Height), tint, LanceCharge);

            Detonate(at, colour, tint, 4.2f, LanceCharge + .04f);

            // **The row failing, one cell at a time, outward.** The far end of an eight-wide field
            // is `7 * WaveStep` behind the stone, which is a fifth of a second - long enough to
            // read as travelling and short enough to be over inside the beat.
            int ax = cell % Width, ay = cell / Width;

            for (int x = 0; x < Width; x++) Ruin(ay * Width + x, colour, tint, Mathf.Abs(x - ax));
            for (int y = 0; y < Height; y++) if (y != ay) Ruin(y * Width + ax, colour, tint, Mathf.Abs(y - ay));

            // Two materials, a beat apart on purpose - a sweep for the strokes travelling and a
            // bell on top of it for what it was worth. Far enough apart not to be a flam
            // (invariant 37q), which is 60 ms at this tempo and not 16.
            Audio.SfxVaried("whoosh", .52f, .06f);
            Tween.After(LanceCharge + .06f, () => Audio.Sfx("chime", .52f, .84f));
        }

        /// <summary>
        /// How long a lance holds before it fires, and how thick its beam is drawn.
        ///
        /// <b>The charge is a fifth of a second.</b> Under about .08 it reads as a stutter rather
        /// than as a wind-up; this is longer than that on purpose, because it is the beat the
        /// slowdown arrives on and a dilation nobody has time to notice is one that only costs
        /// wall-clock. The thickness is two thirds of a cell: the beam has to cover the gems it is
        /// taking without hiding the row either side of it.
        /// </summary>
        const float LanceCharge = .20f, BeamThick = .66f;

        /// <summary>
        /// How slowly the run's clock runs while a lance is going off.
        ///
        /// <para>
        /// <b>Slow rather than stopped, because what was asked for is the hill <em>moving</em>
        /// slowly.</b> A stop is the stormglass's answer (see <see cref="Volley"/>) and the two
        /// must not read alike: one charm bends time and the other takes it away.
        /// </para>
        /// <para>
        /// <b>A fifth, which is about as far as it can go before the raiders read as frozen.</b>
        /// Under about .15 a walk cycle is visibly playing over a body that is not moving, which is
        /// the fault a stun already had to be drawn around (<c>SiegeView.Follow</c>) — there it is
        /// answered by draining the colour out of the raider, and here there is nothing to say it
        /// with, because the hill has not been stopped, it is only slow.
        /// </para>
        /// </summary>
        const float LancePace = .22f;

        /// <summary>
        /// How long a lance's whole sequence runs, in real seconds — and therefore both how long
        /// the clock is slowed and how long the beat waits before the board refills.
        ///
        /// <b>Measured off its own parts rather than typed</b>: the charge, then the wavefront
        /// crossing the widest arm of this field, then the last cell finishing burning. A typed
        /// number would be right until somebody authored a wider board, and the failure would be a
        /// row still coming apart into gems already falling through it.
        /// </summary>
        float LanceFor => LanceCharge + (Mathf.Max(Width, Height) - 1) * WaveStep + LanceTail;

        /// <summary>How long the last cell of a cross is still burning after the wave reaches it.</summary>
        const float LanceTail = .34f;

        /// <summary>A prism's own two: no slowdown, and a short hold so its burst is not fallen into.</summary>
        const float PrismCharge = .12f, PrismFor = .40f;

        /// <summary>
        /// One cell of a lance's cross coming apart, <paramref name="far"/> cells out from the
        /// stone.
        ///
        /// <b>Drawn whether or not a gem is standing there</b>, and that is deliberate: the cross
        /// is a fact about the board's geometry, so a hole in it where a cog or an empty column
        /// happened to be would read as the beam having missed.
        /// </summary>
        void Ruin(int cell, int colour, Color tint, int far)
        {
            if (cell < 0 || cell >= Width * Height) return;

            var at = CentreOf(cell);
            float beat = LanceCharge + Mathf.Min(far * WaveStep, SiegeTuning.BeatFor * .5f);

            Tween.After(beat, () =>
            {
                var frames = CharmBurst(colour);

                if (frames != null && frames.Length > 0)
                {
                    var img = UIKit.Img("Lance burst", _fx, frames[0], Color.white,
                                        new Vector2(Cell * 1.7f, Cell * 1.7f));
                    img.raycastTarget = false;
                    img.preserveAspect = true;
                    img.rectTransform.anchoredPosition = at;
                    img.rectTransform.localRotation =
                        Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                    // Slower than a ward's impact, like the charm's own reel: this is playing
                    // inside a dilated clock and the whole point of the window is that it can be
                    // watched.
                    var book = Flipbook.Attach(img, frames, 20f, false);

                    if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
                    else Tween.After(.6f, () => { if (img) Destroy(img.gameObject); });
                }

                // The shards. Thrown along the arm rather than in a circle, so the row reads as
                // having been *struck* from one side.
                Burst.Sparks(_fx, at, tint, 7, Cell * 2.4f, Cell * .20f, .40f);
            });
        }

        /// <summary>
        /// The burst one cell of a lance's cross goes off in: the ward impact, in the colour the
        /// charm was paid.
        ///
        /// <para>
        /// <b>Borrowed on purpose here, where borrowing it for the middle was the bug.</b> Four
        /// elemental impacts are already cut, addressed and resident on every siege because every
        /// bolt that lands uses one — and at 1.7 cells they are drawn at about the size they were
        /// framed for. What the charm's own reel is for is the four-and-a-half-cell detonation in
        /// the middle, which is the one thing these cannot be stretched to.
        /// </para>
        /// <para>
        /// <b>Written out one literal at a time</b>, for invariant 6's rule read across to art:
        /// <c>Tools/verify/artnames.py</c> reads the name off the call site, so a key built from
        /// an index would be four reels nothing checks.
        /// </para>
        /// </summary>
        static Sprite[] CharmBurst(int colour)
        {
            switch (colour)
            {
                case 0: return Blast("hit_r");
                case 1: return Blast("hit_g");
                case 2: return Blast("hit_b");
                case 3: return Blast("hit_y");
                default: return null;
            }
        }

        /// <summary>
        /// One stroke of a lance: the beam reel, opened from the middle out and held.
        ///
        /// <para>
        /// <b>Two layers, because a tint can only darken.</b> <c>Image.color</c> is a multiply
        /// (invariant 37l), so a white sprite tinted to a gem colour is that colour everywhere and
        /// the hot filament the art tool drew is gone. So the reel is drawn twice — a wide body in
        /// the charm's colour and a thin core left white over it — which is the three-rung ladder
        /// the strike bake already uses in a bake, done here in two draws because the length is not
        /// known until the field is measured.
        /// </para>
        /// <para>
        /// <b>Stretched along its own length, which is the one place in this project stretching is
        /// right.</b> Invariant 37au forbids scaling a picture of a <em>place</em> in x and y
        /// independently; a beam is not a place, it is a thing of variable length, and its sprite
        /// is drawn with no edge along that length precisely so it can be.
        /// </para>
        /// </summary>
        void Stroke(Vector2 at, Vector2 size, Color tint, float after)
        {
            Body(at, size, Pal.A(Pal.Lift(tint, .62f), 1f), 1f, after);
            Body(at, size, new Color(1f, 1f, 1f, .92f), .34f, after);
        }

        /// <summary>One layer of one stroke — see <see cref="Stroke"/>.</summary>
        void Body(Vector2 at, Vector2 size, Color colour, float thick, float after)
        {
            var frames = Reel("beam");
            if (frames == null || frames.Length == 0) return;

            bool across = size.x > size.y;
            var wide = across ? new Vector2(size.x, size.y * thick)
                              : new Vector2(size.x * thick, size.y);

            var img = UIKit.Img("Lance", _fx, frames[0], Pal.A(colour, 0f), wide);
            img.raycastTarget = false;
            img.rectTransform.anchoredPosition = at;

            // The vertical stroke is the same sprite turned a quarter, because the reel is drawn
            // along x - a sprite stretched the other way would be the filament squashed into a
            // bar.
            if (!across)
            {
                img.rectTransform.sizeDelta = new Vector2(size.y, size.x * thick);
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            Flipbook.Attach(img, frames, 30f, true);

            var rt = img.rectTransform;
            rt.localScale = new Vector3(.04f, 1f, 1f);

            // Opened from the middle out rather than faded in, because what a lance does is *go*
            // somewhere - and a bar that simply appears at full length says nothing about where it
            // started.
            Tween.Run(BeamOpen, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = new Vector3(Mathf.Lerp(.04f, 1f, t), 1f, 1f);
                img.color = Pal.A(colour, t);
            }, img, "open").Delay(after).OnDone(() =>
            {
                if (!img) return;

                // **Held before it goes out.** The whole complaint about the first cut was that
                // there was nothing to look at; a beam that starts fading the frame it finishes
                // opening is a beam nobody sees at full brightness.
                Tween.After(BeamHold, () =>
                {
                    if (!img) return;

                    Tween.Fade(img, 0f, BeamFade, Ease.InQuad).OnDone(() =>
                    {
                        if (img) Destroy(img.gameObject);
                    });
                });
            });
        }

        /// <summary>
        /// How a lance's beam is paced: open, hold, go out.
        ///
        /// <b>Three numbers rather than one, and the middle one is the answer to the complaint.</b>
        /// The first cut opened in .16s and began fading immediately, so the stroke existed at full
        /// brightness for exactly no time — which is a flash, not a beam. This opens fast because a
        /// beam arriving slowly reads as a wipe, <b>holds for as long as the cross takes to fail</b>
        /// — the row is being taken by the light, so the light has to still be there while it
        /// happens — and goes out over a third of a second after that.
        /// </summary>
        const float BeamOpen = .12f, BeamHold = .62f, BeamFade = .34f;

        // ------------------------------------------------------------------ a stormglass
        /// <summary>
        /// Where the stormglass that loosed the volley stood.
        ///
        /// <b>Remembered because the bolts arrive a tick later than the stone does.</b> The model
        /// books a stormglass's bolts exactly as it books a match's fuel (invariant 37s), so they
        /// come through <c>SiegeReport.Charmed</c> on a later frame — by which time the gem has been
        /// cleared and its cell holds something else. The volley is fired <em>from the stone</em>,
        /// so the one thing the drawing needs is where the stone was.
        /// </summary>
        Vector2 _stormAt;
        bool _stormKnown;

        /// <summary>
        /// A stormglass going off: the line pours into the stone, and the stone fires.
        ///
        /// <para>
        /// <b>The arrow points the other way from the first cut, and that is the whole fix.</b> It
        /// used to draw light leaving the field for the wards and then the wards shooting — which
        /// is what the model does and is not what the player did. The owner's sentence was exact:
        /// <em>when a stormglass is matched, that gem shoots lasers or fireballs at the enemies on
        /// the field</em>. So the wards charge the stone (they are still what pays for it, which is
        /// invariant 37cg and the reason a stormglass is worth more to somebody who has bought
        /// turrets) and the stone throws the volley (<see cref="Volley"/>).
        /// </para>
        /// <para>
        /// <b>Nothing about the model moved.</b> Every bolt is still that ward's own bolt at that
        /// ward's own weight, carrying that ward's turret art in that ward's colour, and the gold
        /// figure on the raider still says which ward was strong against what. What changed is
        /// where the projectile leaves from — which is a drawing, and the one drawing the player
        /// asked for.
        /// </para>
        /// </summary>
        void Storming(int cell, int colour, Color tint)
        {
            var at = CentreOf(cell);

            _stormAt = at;
            _stormKnown = true;

            // **The charge is not dilated, and that is a fact about the model rather than a
            // taste.** The bolts are booked `SiegeTuning.FuelLands` into the future *in model
            // seconds* - exactly as a match's fuel is (invariant 37s) - so slowing the clock
            // through the wind-up would stretch the wait for them by the same factor: at a sixth,
            // a .58s booking becomes three and a half seconds of staring at a charged stone. The
            // stop belongs to the volley, and it is taken in `Volley`, which is where the model
            // says the bolts exist.
            //
            // **Held for the charge and no further, and extended by the volley when it lands.**
            // How long the barrage takes is not known here - the bolts do not exist yet - and a
            // stormglass matched with an empty hill books none at all, so a hold taken for the
            // whole thing up front would be a second and a half of nothing on exactly the board
            // where nothing is what happens. See `_holdUntil`.
            Holding(StormCharge + StormSettle);

            Charge(at, tint, StormCharge);

            // **The line pouring in, three times over.** One thread per standing ward, in that
            // ward's own colour, so what arrives at the stone is visibly the whole line and not
            // one turret - and repeated across the charge rather than drawn once, because a
            // wind-up has to *build*. One pass reads as a transfer; three accelerating passes read
            // as something filling up.
            int passes = Mathf.Max(2, Mathf.RoundToInt(StormCharge / .18f));

            for (int pass = 0; pass < passes; pass++)
            {
                float over = Mathf.Lerp(.22f, .11f, pass / (float)Mathf.Max(1, passes - 1));
                float when = pass * (StormCharge / passes);

                for (int w = 0; w < _posts.Length; w++)
                {
                    var ward = w < _board.Wards.Count ? _board.Wards[w] : null;
                    if (ward == null || !ward.Alive) continue;

                    var post = new Vector2(PostX(w), _lineY + Cell * .9f);
                    Streak(post, at, TintOf(ward.Colour), when + w * .03f, over);
                }
            }

            // And the stone swelling as it takes it: a bloom that grows for the whole charge, so
            // the thing about to go off is visibly getting brighter rather than merely waiting.
            var swell = UIKit.Img("Storm swell", _fx, Art.Glow(96, 2.0f),
                                  Pal.A(Pal.Lift(tint, .55f), 0f),
                                  new Vector2(Cell * 1.2f, Cell * 1.2f));
            swell.raycastTarget = false;
            swell.rectTransform.anchoredPosition = at;

            var swellRt = swell.rectTransform;

            Tween.Run(StormCharge, Ease.InQuad, t =>
            {
                if (!swellRt) return;
                swellRt.localScale = Vector3.one * Mathf.Lerp(.6f, 4.4f, t * t);
                swell.color = Pal.A(Pal.Lift(tint, .55f), Mathf.Min(1f, t * 1.6f) * .85f);
            }, swell).OnDone(() => { if (swell) Destroy(swell.gameObject); });

            Detonate(at, colour, tint, 4.8f, StormCharge);

            // The gathering, and it is deliberately the lowest thing this board plays: what
            // follows it is the whole line firing at once, so the charm itself has to read as a
            // breath drawn in rather than as the payoff.
            Audio.Sfx("whoosh", .55f, .62f);
            Tween.After(StormCharge, () => Audio.Sfx("chime", .5f, .58f));
        }

        /// <summary>
        /// How long a stormglass holds while the line pours into it.
        ///
        /// <b>Longer than a lance's, because there is more to arrive.</b> Four threads staggered by
        /// 35 ms each need about .12s to all be in the air, and the stone should not go off before
        /// the last one has reached it.
        /// </summary>
        /// <summary>
        /// How long a stormglass draws the line into itself before it fires.
        ///
        /// <b>The model's own figure and never a typed one.</b> <c>SiegeBoard.Break</c> books the
        /// volley <c>FuelLands</c> into the future, exactly as it books a match's fuel, so this is
        /// how long the bolts really take to exist. Typed, the two would drift and the stone would
        /// either fire before the charge finished or sit lit with nothing happening — which is the
        /// same class of fault as a mote that lands out of step with its own fuel.
        /// </b>
        /// </summary>
        static float StormCharge => SiegeTuning.FuelLands(0);

        /// <summary>
        /// The beat after the charge that the fall waits out whatever else happens.
        ///
        /// <b>Enough for the stone's own detonation to be seen on a hill with nothing on it.</b>
        /// Every other case is extended by the volley; this is the floor under all of them, so a
        /// stormglass taken with the hill already cleared still reads as a payoff rather than as a
        /// gem going out.
        /// </summary>
        const float StormSettle = .30f;

        /// <summary>
        /// One thread of light crossing the board, opened along its own length and travelling.
        ///
        /// <b>Two layers rather than one, for <see cref="Beam"/>'s reason and at a fraction of its
        /// weight.</b> This is the wind-up, so it has to read as the same <em>material</em> as the
        /// barrage that follows without competing with it: a coloured body and a thin white
        /// filament, both a third of a beam's thickness.
        /// </summary>
        void Streak(Vector2 from, Vector2 to, Color tint, float delay, float over)
        {
            var dir = to - from;
            float far = dir.magnitude;
            if (far < 1f) return;

            var frames = Reel("beam");
            if (frames == null || frames.Length == 0) return;

            float lean = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var mid = from + dir * .5f;

            Thread(frames, mid, far, Cell * .34f, lean, Pal.Lift(tint, .65f), delay, over);
            Thread(frames, mid, far, Cell * .11f, lean, Color.white, delay, over);
        }

        /// <summary>One layer of one thread — see <see cref="Streak"/>.</summary>
        void Thread(Sprite[] frames, Vector2 at, float far, float thick, float lean, Color colour,
                    float delay, float over)
        {
            var img = UIKit.Img("Streak", _fx, frames[0], Pal.A(colour, 0f),
                                new Vector2(far, thick));
            img.raycastTarget = false;

            var rt = img.rectTransform;
            rt.anchoredPosition = at;
            rt.localRotation = Quaternion.Euler(0f, 0f, lean);
            rt.localScale = new Vector3(.08f, 1f, 1f);

            Flipbook.Attach(img, frames, 30f, true);

            Tween.Run(over, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = new Vector3(Mathf.Lerp(.08f, 1f, t), 1f, 1f);
                img.color = Pal.A(colour, t < .6f ? t / .6f : 1f - (t - .6f) / .4f);
            }, img).Delay(delay).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>
        /// The volley a stormglass loosed, arriving — <b>thrown by the stone</b>.
        ///
        /// <para>
        /// <b>One projectile per ward and raider, flown from the gem, and that is the change.</b>
        /// The first cut drew a thin ray from each turret to each raider: fifty hairlines inside
        /// half a second, which is not a volley, it is static. Each bolt here is the ward's own
        /// comet — the same reel that ward fires all run, at the same size — launched from where
        /// the stone stood and landing with the same impact every other hit on this hill lands
        /// with.
        /// </para>
        /// <para>
        /// <b>Gathered by which ward threw at which raider, because the model can throw twice.</b>
        /// The ward wearing the charm's colour fires <c>CharmVolleyOwn</c> times
        /// (<c>SiegeBoard.Volley</c>), and two comets down one line a frame apart are one comet at
        /// twice the brightness — invariant 37q's flam said about a drawing. What the player has to
        /// read is *that ward answered that raider*, once, for this much.
        /// </para>
        /// <para>
        /// <b>The damage figures are still the board's own</b> — the same <c>Land</c> path
        /// every other hit on this hill goes through, so a gold number still means what it means
        /// everywhere else: this ward was strong against that.
        /// </para>
        /// </summary>
        void Volley(IReadOnlyList<SiegeBolt> bolts)
        {
            if (bolts == null || bolts.Count == 0) return;

            var gathered = new Dictionary<int, SiegeBolt>(bolts.Count);
            var order = new List<int>(bolts.Count);

            for (int i = 0; i < bolts.Count; i++)
            {
                var bolt = bolts[i];
                if (bolt.Ward < 0 || bolt.Ward >= _posts.Length) continue;

                int key = bolt.Ward * 100003 + bolt.Raider;

                if (gathered.TryGetValue(key, out var running))
                {
                    gathered[key] = new SiegeBolt(bolt.Ward, bolt.Raider,
                                                  running.Damage + bolt.Damage,
                                                  running.Weak || bolt.Weak,
                                                  running.Killed || bolt.Killed);
                    continue;
                }

                gathered[key] = bolt;
                order.Add(key);
            }

            // **The middle of the field when nothing was remembered**, which can only happen if a
            // volley ever arrives without a stone having sprung on this screen. It cannot today;
            // an anchor that answers the centre is still better than one that answers the origin,
            // which is the bottom-left corner of the board.
            var from = _stormKnown ? _stormAt : CentreOf(Width * Height / 2);
            _stormKnown = false;

            // **Everything stops.** A stormglass is the one thing in this mode that takes the
            // whole line and throws it at the whole hill at once, and it resolves in the model in
            // a single instant - so without this the player sees a rattle of light over a hill
            // that is still walking through it. `Dilate(0)` stops the hill, the wards, the muster
            // and the fuel together, because all four are consequences of the one number
            // `SiegeBoard.Advance` is handed.
            //
            // **It is affordable for the same reason the lance's crawl is**: the model advances by
            // delivered seconds, so a stopped clock is not free time, it is no time. Nothing here
            // reaches the hold simulation, because nothing here changes what the board is told.
            Dilate(0f, StormVolleyFor);

            // **And the fall waits for the barrage, pushed out from here.** The deadline can be
            // moved while the beat is already waiting on it, which is the whole reason it is a
            // deadline: how long this takes is known now and was not known when the stone went
            // off. A volley that lands after the turn has finished pushes a deadline nobody is
            // waiting on, which costs nothing.
            Holding(StormVolleyFor);

            // **The corpses are claimed before the first bolt leaves**, or `Reap` takes the whole
            // hill down on the next frame and the rest of the volley falls on empty ground. This
            // is the stormcall's own problem (`_striking`) met a second time by the one other
            // thing in this mode that kills a dozen raiders in one instant and draws it over a
            // second and a half - so it is answered the same way, in a list of its own so the two
            // cannot clear each other's claims.
            _volleying.Clear();
            for (int i = 0; i < order.Count; i++)
                if (gathered[order[i]].Killed) _volleying.Add(gathered[order[i]].Raider);

            // **The stone opening fire, and it is drawn as the biggest single thing on the
            // board.** The charge already detonated it once; this is the second beat, when the
            // light it gathered goes back out - so it is a white core over a wide bloom rather
            // than another coloured burst, because what leaves here is every ward at once and no
            // one colour may own it.
            Pop(from, Pal.Cream, 7.2f, .26f);
            Shockwave(from, Pal.Cream, 4.6f, .40f);
            Shockwave(from, Pal.Gold, 7.0f, .58f);
            Burst.Sparks(_fx, from, Pal.Cream, 30, Cell * 7.0f, Cell * .34f, .80f);
            ShakeBoard(Cell * .11f);

            // **Spread across the window rather than a fixed step per bolt, and the window is
            // measured off what one beam costs.** A line of four against a hill of a dozen is up
            // to fifty beams: a fixed step either overruns the freeze or fires them all at once.
            // So the firing window is whatever is left of the freeze once the last beam's own
            // life is taken off it, which is what keeps the barrage inside the stop whatever the
            // hill is carrying.
            float fire = Mathf.Max(.10f, StormVolleyFor - (BeamSnap + BeamLive + BeamOut));

            float step = order.Count > 1
                       ? Mathf.Clamp(fire / (order.Count - 1), .020f, .085f)
                       : 0f;

            for (int i = 0; i < order.Count; i++)
            {
                var bolt = gathered[order[i]];

                var ward = _board.Wards[bolt.Ward];
                var tint = TintOf(ward.Colour);

                Mob mob = null;
                for (int m = 0; m < _mob.Count; m++) if (_mob[m].Id == bolt.Raider) mob = _mob[m];
                if (mob == null || mob.Node == null) continue;

                var to = mob.Node.anchoredPosition;
                var dir = to - from;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

                float beat = i * step;

                var hit = bolt;
                var body = mob;
                var end = to;

                int ordinal = i;

                Tween.After(beat, () =>
                {
                    if (body == null || body.Node == null) return;

                    // The muzzle is the stone, so the ward's own flash is drawn there. It is the
                    // one thing left that says *which turret* this beam belongs to besides its
                    // colour, now that nothing crosses the hill to carry a silhouette.
                    Flash(from, angle, ward.Model, ward.Colour, tint, 1f);

                    // **A beam rather than a comet, which is the change.** A projectile crossing
                    // the hill is a thing that *travels*; a laser is a thing that is suddenly
                    // *there*, and the difference is the whole of what was asked for. It also
                    // means the whole line's fire is on the screen at once instead of strung out
                    // along fifty separate flights - twelve beams standing together is the
                    // picture, where twelve comets in a row is a queue.
                    Beam(from, body.Node.anchoredPosition, tint);

                    // Every third, because a shake per beam is a shake that stops reading and a
                    // shake on one is a fault.
                    if (ordinal % 3 == 0) ShakeBoard(Cell * .05f);

                    // **A voice every third beam and never every beam.** Fifty of anything inside
                    // a second is the ten-voice pool cutting itself off (invariant 37q about a
                    // sound), and the barrage's own weight is carried by the `boom` under all of
                    // it - this is the crackle on top.
                    if (ordinal % 3 == 0) Audio.SfxVaried("zap", .30f, .10f);

                    // The beam is instant, so the impact is a beat behind it rather than a flight
                    // behind it: long enough for the eye to follow the line out, short enough that
                    // the beam is still standing when the raider comes apart.
                    Tween.After(BeamSnap + .05f, () =>
                    {
                        // **The claim is released where the beam lands and nowhere else**, so a
                        // raider stays standing for exactly as long as the thing that killed it
                        // takes to arrive. Released before the death is drawn, because `Land`
                        // wants `Reap` to be allowed to take the body on the next frame.
                        _volleying.Remove(hit.Raider);

                        var land = body != null && body.Node != null
                                 ? body.Node.anchoredPosition : end;

                        // **The impact is the charm's own, not a bolt's.** A ward's landing is
                        // cut small because it happens eighteen times a second; this is the one
                        // moment the whole line arrives at once, so every beam ends in a real
                        // burst at nearly two cells with the beam's own colour under it.
                        Scorch(land, ward.Colour, tint);

                        // **The ordinary landing for a kill and a light one for everything
                        // else.** A kill is the beat worth marking, exactly as it is for a bolt.
                        if (hit.Killed) Land(land, tint, ward.Model, ward.Colour, angle, hit);
                        else
                        {
                            Pop(land, hit.Weak ? Pal.Gold : tint, hit.Weak ? 1.9f : 1.4f, .26f);
                            Number(hit.Raider, land, hit.Damage, hit.Weak);

                            if (body != null && body.Body != null)
                                Tween.Punch(body.Body.transform, .11f, .14f);
                        }
                    });
                });
            }

            // **The volley's own voice, once, whatever it threw.** A lit line already lands
            // about eighteen hits a second and this is up to fifty inside half a second, so a
            // sound per bolt would be the ten-voice pool cutting itself off for the length of the
            // biggest moment in the mode. `boom` rather than `zap`, low, because what the player
            // has to hear is the line answering together rather than a turret shooting.
            Audio.Sfx("boom", .7f, .58f);
        }

        /// <summary>
        /// One laser: three layers of light along one line, snapped open and held.
        ///
        /// <para>
        /// <b>Three rungs far apart, which is what makes a thing look <em>made of</em> light
        /// rather than painted it</b> — the strike bake's own ladder (white core, coloured body,
        /// wide haze) done here in three draws because the length is not known until the hill is
        /// measured. Two would read as a thick line; one reads as a highlighter.
        /// </para>
        /// <para>
        /// <b>Snapped rather than flown, and that is the definition.</b> A projectile is a thing
        /// that travels and a laser is a thing that is suddenly there: it opens along its own
        /// length in <see cref="BeamSnap"/> — fast enough to read as arrival rather than as a wipe
        /// — then <em>holds</em>, which is the part that was missing, and then goes out by
        /// thinning rather than by fading, because a beam that dims uniformly reads as a light
        /// being turned down and one that narrows reads as a beam closing.
        /// </para>
        /// <para>
        /// <b>The reel is the lance's</b> (<c>make_siege_art.beam</c>): ten frames of a crawling
        /// filament, drawn white so the view can tint it. <c>Image.color</c> is a multiply
        /// (invariant 37l), so the white core is a second draw at full white rather than a
        /// brighter tint — a tint can only ever darken.
        /// </para>
        /// </summary>
        void Beam(Vector2 from, Vector2 to, Color tint)
        {
            var frames = Reel("laser");
            if (frames == null || frames.Length == 0) return;

            var dir = to - from;
            float far = dir.magnitude;
            if (far < 1f) return;

            float lean = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var mid = from + dir * .5f;

            // Run a little past the raider, so a beam ends *in* what it hit rather than at its
            // feet - a laser that stops exactly on a silhouette reads as having been blocked.
            float length = far + Cell * .55f;
            var seat = from + dir.normalized * (length * .5f);

            Layer(frames, seat, length, Cell * HazeThick, lean,
                  Pal.A(Pal.Lift(tint, .35f), .42f));
            Layer(frames, seat, length, Cell * BodyThick, lean,
                  Pal.A(Pal.Lift(tint, .80f), .92f));
            Layer(frames, seat, length, Cell * CoreThick, lean,
                  new Color(1f, 1f, 1f, .96f));

            // The bloom where it leaves the stone. Drawn per beam rather than once, because what
            // makes a barrage read as a barrage is that the muzzle keeps flaring.
            var flare = UIKit.Img("Beam flare", _fx, Art.Glow(96, 2.0f),
                                  Pal.A(Pal.Lift(tint, .7f), .9f),
                                  new Vector2(Cell * 1.9f, Cell * 1.9f));
            flare.raycastTarget = false;
            flare.rectTransform.anchoredPosition = from;

            Tween.Scale(flare.transform, .35f, BeamSnap + BeamLive, Ease.OutQuad);
            Tween.Fade(flare, 0f, BeamSnap + BeamLive, Ease.InQuad)
                 .OnDone(() => { if (flare) Destroy(flare.gameObject); });
        }

        /// <summary>One layer of one beam — see <see cref="Beam"/>.</summary>
        void Layer(Sprite[] frames, Vector2 at, float length, float thick, float lean, Color colour)
        {
            var img = UIKit.Img("Beam", _fx, frames[0], Pal.A(colour, 0f),
                                new Vector2(length, thick));
            img.raycastTarget = false;

            var rt = img.rectTransform;
            rt.anchoredPosition = at;
            rt.localRotation = Quaternion.Euler(0f, 0f, lean);
            rt.localScale = new Vector3(.06f, 1f, 1f);

            Flipbook.Attach(img, frames, 30f, true);

            Tween.Run(BeamSnap, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = new Vector3(Mathf.Lerp(.06f, 1f, t), 1f, 1f);
                img.color = Pal.A(colour, t);
            }, img).OnDone(() =>
            {
                if (!img) return;

                // **Held, flickering, and then closed.** The flicker is what stops a held beam
                // reading as a drawn rectangle: it is on the thickness rather than on the alpha,
                // because a beam that pulses in brightness reads as a fault in the screen and one
                // that pulses in width reads as power going down it.
                float t0 = Time.unscaledTime;

                Tween.Run(BeamLive, Ease.Linear, t =>
                {
                    if (!rt) return;
                    float k = 1f + Mathf.Sin((Time.unscaledTime - t0) * 46f) * .13f;
                    rt.localScale = new Vector3(1f, k, 1f);
                }, img).OnDone(() =>
                {
                    if (!img) return;

                    Tween.Run(BeamOut, Ease.InQuad, t =>
                    {
                        if (!rt) return;
                        rt.localScale = new Vector3(1f, Mathf.Lerp(1f, .05f, t), 1f);
                        img.color = Pal.A(colour, 1f - t);
                    }, img).OnDone(() => { if (img) Destroy(img.gameObject); });
                });
            });
        }

        /// <summary>
        /// Where a beam lands: the charm's own burst rather than a bolt's.
        ///
        /// <b>Bigger than a ward impact on purpose.</b> `Land` is cut for something that happens
        /// eighteen times a second; this happens once or twice in a run, at the end of every beam
        /// of a frozen barrage, and it is the far end of the thing the player is being shown.
        /// </summary>
        void Scorch(Vector2 at, int colour, Color tint)
        {
            var frames = CharmBurst(colour);

            if (frames != null && frames.Length > 0)
            {
                var img = UIKit.Img("Beam hit", _fx, frames[0], Color.white,
                                    new Vector2(Cell * 1.9f, Cell * 1.9f));
                img.raycastTarget = false;
                img.preserveAspect = true;
                img.rectTransform.anchoredPosition = at;
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var book = Flipbook.Attach(img, frames, 24f, false);

                if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
                else Tween.After(.6f, () => { if (img) Destroy(img.gameObject); });
            }

            Shockwave(at, Pal.Lift(tint, .6f), 1.9f, .28f);
            Burst.Sparks(_fx, at, tint, 9, Cell * 2.6f, Cell * .22f, .42f);
        }

        /// <summary>
        /// How thick each of a beam's three layers is drawn, in cells.
        ///
        /// <b>Far apart rather than near</b>: a haze wider than a gem, a body a third of one and a
        /// filament an eighth. Close together they blur into one fat line, which is the reading
        /// this replaced.
        /// </summary>
        const float HazeThick = 1.90f, BodyThick = .82f, CoreThick = .13f;

        /// <summary>
        /// How a beam is paced: snap, hold, close.
        ///
        /// <b>The hold is the number that matters and the snap is the one that must stay small.</b>
        /// A laser that takes a tenth of a second to reach its target is a projectile; five
        /// hundredths is arrival. The hold is what the player is actually given to look at, and it
        /// is affordable because the board and the hill are both stopped for it.
        /// </summary>
        const float BeamSnap = .05f, BeamLive = .30f, BeamOut = .22f;

        /// <summary>
        /// How long the board is stopped for a stormglass's volley.
        ///
        /// <para>
        /// <b>The longest anything in this mode holds the run.</b> It is bounded by nothing in the
        /// rules — the model booked every one of these bolts before the first was drawn, so a
        /// slower barrage cannot kill anything later than the board already said it died, and the
        /// clock is stopped rather than merely ignored so the hill does not walk through it.
        /// </para>
        /// <para>
        /// <b>What it may not become is the ordinary case.</b> A stormglass is the rarest of three
        /// charms and only the third chapter deals it, so this is on screen once or twice in a run
        /// — which is what buys it a second and a half. A payoff this length on anything the player
        /// meets every few seconds would be the mode watching itself.
        /// </para>
        /// </summary>
        const float StormVolleyFor = 1.7f;
    }
}
