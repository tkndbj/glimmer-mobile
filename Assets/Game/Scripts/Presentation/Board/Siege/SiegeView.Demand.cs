using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The two readouts that turn "which colour is wanted" from a parse into a glance: what each
    /// turret is the answer to <em>now</em>, and what is coming <em>next</em>.
    ///
    /// <para>
    /// <b>They exist because the colour lock gives the question stakes and does nothing about the
    /// cost of answering it.</b> Reading the hill meant separating a dozen small moving bodies in
    /// four colours four hundred points away — two seconds of work under a clock that gives none,
    /// so the eye correctly refused and the mode played as "take the biggest match". Both of these
    /// put the answer where the eye already passes: the demand light is on the turret directly
    /// above the gems, and the forecast is drawn on the one stretch of hill that is empty at
    /// exactly the moment it matters.
    /// </para>
    /// <para>
    /// <b>Neither decides anything.</b> Both read <c>SiegeBoard</c>'s own answers
    /// (<c>DemandOf</c>, <c>Busiest</c>, <c>Coming</c>, <c>Resting</c>), because a screen that
    /// counted raiders itself would be a second opinion about the one question this mode is about
    /// and the two would drift the first time either moved.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>
        /// Paints every turret's demand light: how much of what is standing on the hill this
        /// turret is the answer to.
        ///
        /// <para>
        /// <b>A light rather than a number, and it is the busiest one that is loud.</b> Four
        /// figures would be four things to read and compare, which is the parse this exists to
        /// replace; one turret visibly burning hotter than its neighbours is an answer with no
        /// reading in it at all. The dimmer three still carry a share, so the ordering is there
        /// for anybody who wants it.
        /// </para>
        /// <para>
        /// <b>A fallen turret shows nothing, whatever the hill is sending.</b> Its colour cannot
        /// be answered any more, so a light saying "feed this" would be pointing at a wall — and
        /// a wall with an alarm on it is worse than a wall.
        /// </para>
        /// </summary>
        void Wanted()
        {
            if (_posts == null || _board == null) return;

            int most = _board.Busiest;

            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                if (post == null || post.Want == null) continue;

                var ward = _board.Wards[i];

                float share = most <= 0 || !ward.Alive
                            ? 0f
                            : Mathf.Clamp01(_board.DemandOf(i) / (float)most);

                // Only the loudest pulses. Squared so the second-busiest is visibly quieter than
                // the busiest rather than merely a little dimmer - the whole job of this widget is
                // to be answerable without comparing anything.
                float lit = share * share;

                bool loudest = share >= .999f && most > 0;

                float beat = loudest
                           ? 1f + Mathf.Sin(Time.unscaledTime * 5.2f) * .10f
                           : 1f;

                // **A halo on the tube rather than a wash under the turret.** It was two and a
                // third cells across at nearly three quarters of an alpha, and a render said what
                // that is: four coloured puddles overlapping under the line, which reads as
                // scenery rather than as a meter. What a readout wants is to be tight and bright
                // on the thing it is about.
                post.Want.color = Pal.A(TintOf(ward.Colour), lit * .88f);
                post.Want.rectTransform.localScale = Vector3.one * (.9f + lit * .22f) * beat;
            }
        }

        // ------------------------------------------------------------------ the forecast
        RectTransform _forecast;
        CanvasGroup _forecastGroup;
        Text _forecastTitle, _forecastClock;

        /// <summary>What the band's title last said, so it is fitted once rather than per frame.</summary>
        string _forecastSaid;
        Image[] _forecastChips;
        Text[] _forecastCounts;

        /// <summary>
        /// Builds the forecast band. Hidden until the hill is clear and something is still coming.
        ///
        /// <para>
        /// <b>Drawn on the hill rather than in the header, and the hill is empty when it shows.</b>
        /// A breather is the one moment in a run when the largest surface on the screen has
        /// nothing on it, so the forecast costs the board nothing and lands exactly where the
        /// player has to look to use it. In the header it would be a fifth readout competing with
        /// four that are always there.
        /// </para>
        /// <para>
        /// <b>One chip per ward rather than one per colour</b>, because the line is what the
        /// player feeds: a level standing three wards forecasts three colours, and a fourth chip
        /// on it would be a colour nothing on that field can even deal.
        /// </para>
        /// </summary>
        void Foresight()
        {
            if (_layout == null) return;

            _forecast = UIKit.Node("Forecast", _hill);
            _forecast.anchorMin = _forecast.anchorMax = new Vector2(.5f, .5f);
            _forecast.sizeDelta = new Vector2(Span.x, Cell * 2.4f);
            _forecast.anchoredPosition = new Vector2(0f, (_hillTop + _hillFoot) * .5f);

            _forecastGroup = UIKit.Group(_forecast);
            _forecastGroup.alpha = 0f;
            _forecastGroup.blocksRaycasts = false;

            _forecastTitle = UIKit.Label("Title", _forecast, Loc.Get("mode.siege.next"),
                                         Mathf.RoundToInt(Cell * .34f), Pal.Cream,
                                         TextAnchor.MiddleCenter,
                                         new Vector2(Span.x, Cell * BandTitleBox));
            _forecastTitle.rectTransform.anchoredPosition =
                new Vector2(0f, Cell * BandTitleRise);

            int seats = _layout.Wards.Length;

            _forecastChips = new Image[seats];
            _forecastCounts = new Text[seats];

            float step = Cell * 1.35f;
            float first = -(seats - 1) * step * .5f;

            for (int i = 0; i < seats; i++)
            {
                int colour = SiegeLayout.Letters.IndexOf(_layout.Wards[i]);

                var slot = UIKit.Node("Chip", _forecast);
                slot.anchorMin = slot.anchorMax = new Vector2(.5f, .5f);
                slot.sizeDelta = new Vector2(Cell, Cell);
                slot.anchoredPosition = new Vector2(first + i * step, 0f);

                _forecastChips[i] = UIKit.Img("Gem", slot, GemArt(colour), Color.white,
                                              new Vector2(Cell * .72f, Cell * .72f));
                _forecastChips[i].preserveAspect = true;
                _forecastChips[i].raycastTarget = false;

                _forecastCounts[i] = UIKit.Label("Count", slot, string.Empty,
                                                 Mathf.RoundToInt(Cell * .40f), Pal.Cream,
                                                 TextAnchor.MiddleCenter,
                                                 new Vector2(Cell, Cell * .5f));
                _forecastCounts[i].rectTransform.anchoredPosition = new Vector2(0f, -Cell * .62f);
            }

            _forecastClock = UIKit.Label("Clock", _forecast, string.Empty,
                                         Mathf.RoundToInt(Cell * .54f), Pal.Gold,
                                         TextAnchor.MiddleCenter,
                                         new Vector2(Span.x, Cell * BandClockBox));
            _forecastClock.rectTransform.anchoredPosition =
                new Vector2(0f, -Cell * BandClockDrop);
        }

        /// <summary>
        /// Paints the forecast, and shows or hides it.
        ///
        /// <para>
        /// <b>Shown only while the hill is clear</b>, which is <c>SiegeBoard.Resting</c>: a
        /// forecast over a hill that still has raiders on it would be a second thing to read at
        /// the one moment there is already too much, and the wave it names is not the one the
        /// player is fighting.
        /// </para>
        /// <para>
        /// <b>A colour with nothing coming is dimmed rather than hidden</b>, because the row is
        /// how a player finds their colour: a chip that comes and goes would move the other three
        /// under a finger that had learnt where they were.
        /// </para>
        /// <para>
        /// <b>Before a boss the row comes off entirely and the band names the boss instead.</b>
        /// Reported from play as confusing, and it is: the forecast answers <em>which colour do I
        /// bank</em>, and before a boss the answer is not a colour — it is that a boss is coming,
        /// which is different news and the loudest this mode has. A blightcaller and a warbringer
        /// ride the head of their last authored wave (invariant 37ad), so such a wave really does
        /// carry ordinary raiders whose counts are given up; that is the trade, taken deliberately,
        /// because a player who reads four gem counts and then meets a warlord has been told the
        /// wrong thing rather than half of the right one.
        /// </para>
        /// </summary>
        /// <summary>
        /// Whether the forecast band may be drawn at all.
        ///
        /// <b>A static, so the one thing a picture cannot answer can be swept</b> - the same
        /// bargain <c>Bands.Of</c> and <c>Captions.Of</c> make. Whether two captions overlap is
        /// arithmetic (<c>SiegeView.Captions</c>), and on every shape this mode is played at the
        /// band and a banner do; what keeps them apart is this predicate rather than a placement,
        /// so it is the predicate a fixture has to be able to reach.
        /// </summary>
        public static bool BandShows(bool resting, bool over, bool chaining, bool speaking)
            => resting && !over && !chaining && !speaking;

        void Foretell()
        {
            if (_forecastGroup == null || _board == null) return;

            // **And never while a chain banner is standing** — the other half of `Chain`'s rule,
            // which is that the hill holds one wide caption at a time and whichever is already up
            // keeps it. The case this half covers is a cascade that runs on into the breather it
            // started before: without it the band would fade in underneath a banner that has a
            // second still to go. Asked of the banner itself rather than of a flag, so there is
            // nothing to clear — it nulls its own field when it is destroyed.
            // **And never while the hill is speaking**, which is the other half of the same
            // rule and the half that was missing. The band is pinned to the middle of the hill
            // and a banner floats up into it: measured (`SiegeView.Captions`), the two share
            // 1.31 cells of row on a 19.5:9 phone and 2.17 on a tablet, so "THE GRAVEMAW FALLS"
            // was drawn through the gem counts every time a boss fell - the fall clears the hill,
            // which is the same frame the breather starts.
            //
            // **The banner wins and the band waits**, which is the opposite way round from the
            // chain above and is the same principle: what is transient cannot be deferred and a
            // readout can. Nothing is lost - a breather is four seconds and the longest banner is
            // 2.4 - and no countdown ever blinks out mid-count, because the only banner that can
            // arrive over a standing band is a wave's, which ends the breather anyway.
            bool speaking = Speaking;
            bool show = BandShows(_board.Resting, Over, _chain != null, speaking);

            // Taken down at once rather than faded while a banner stands, so there is no frame
            // with both of them half drawn on the same row. Everywhere else it fades.
            _forecastGroup.alpha = speaking
                                 ? 0f
                                 : Mathf.MoveTowards(_forecastGroup.alpha, show ? 1f : 0f,
                                                     Time.unscaledDeltaTime * 5f);

            if (!show) return;

            var coming = _board.Coming;

            // **A boss wave is announced as itself and never as a row of gem counts.** Reported
            // from play as confusing, and it is: the forecast's whole job is to say which colour
            // to bank, and the answer before a boss is not a colour at all - it is *that a boss is
            // coming*, which is a different kind of news and the loudest thing this mode has. A
            // blightcaller and a warbringer ride the head of their last authored wave (invariant
            // 37ad), so such a wave really does carry ordinary raiders too; their colours are what
            // is given up, and the trade is deliberate.
            bool boss = coming.HasBoss;

            string say = Loc.Get(boss ? BossKey(coming.Boss) : "mode.siege.next");

            _forecastTitle.text = say;
            // **A boss's name is said in white, never in the boss's own colour.** Half this
            // cast casts in something a caption cannot carry - a shackler's slate
            // (`Pal.Dormant`) and a colossus's dust (`Pal.Thorn`) are both darker than the
            // hill behind them - so the loudest news the mode has arrived unreadable
            // (reported from play, 2026-09-18). The colour still says which boss it is
            // everywhere it *can* be read: the body, the bolt, the flash and the bar.
            _forecastTitle.color = boss ? Color.white : Pal.Cream;

            // **Sized and fitted together, and only when the sentence changes.** `Foretell`
            // repaints every frame the band is up and a fit is a measure of the string against
            // the face (`UIKit.OneLineLabel`): cheap once, wasteful sixty times a second. The two
            // are one statement because setting the authored size on every pass while fitting on
            // some of them is a fit that lasts exactly one frame - the size is the thing the fit
            // *writes*. The sentence carries the size with it, because the only thing that
            // changes it is whether a boss is being named.
            if (say != _forecastSaid)
            {
                _forecastSaid = say;
                _forecastTitle.fontSize = Mathf.RoundToInt(Cell * (boss ? .52f : .34f));

                UIKit.OneLineLabel(_forecastTitle, CaptionRoom,
                                   Mathf.RoundToInt(Cell * CaptionFloor));
            }

            // **The name stands where the chips were**, rather than staying up at the caption's
            // height with a hole under it: with the row hidden the band would otherwise read as a
            // title, a gap and a clock. One thing in the middle over a countdown is the shape this
            // moment actually is.
            _forecastTitle.rectTransform.anchoredPosition =
                new Vector2(0f, Cell * (boss ? BandBossRise : BandTitleRise));

            for (int i = 0; i < _forecastChips.Length; i++)
            {
                _forecastChips[i].enabled = !boss;
                _forecastCounts[i].enabled = !boss;

                if (boss) continue;

                int colour = SiegeLayout.Letters.IndexOf(_layout.Wards[i]);
                int many = coming.Of(colour);

                // **A colour with nothing coming is dimmed and never hidden**, and it has to stay
                // findable: the row is how a player locates their colour, so a chip that faded to
                // nothing would move the other three under a finger that had learnt where they
                // were. A render said .22 was very nearly invisible.
                _forecastChips[i].color = many > 0
                                        ? Color.white
                                        : new Color(1f, 1f, 1f, .38f);

                float weight = coming.Most <= 0 ? 0f : many / (float)coming.Most;
                float size = Cell * (.54f + weight * .30f);

                _forecastChips[i].rectTransform.sizeDelta = new Vector2(size, size);

                _forecastCounts[i].text = many > 0 ? many.ToString() : "-";
                _forecastCounts[i].color = many > 0 ? Pal.Cream : new Color(1f, 1f, 1f, .3f);
            }

            // Whole seconds, counted up from the floor: a number that ticks is a clock, and one
            // that runs to two decimal places is a readout nobody can use.
            int left = Mathf.Max(0, Mathf.CeilToInt(_board.Rest));

            _forecastClock.text = left.ToString();

            // White with a boss coming, which is what the title above it is wearing too - one
            // thing said in two places rather than two things, and the band's own colour has
            // to be one the band can carry (see the title).
            _forecastClock.color = boss ? Color.white
                                 : left <= 3 ? Pal.Ember : Pal.Gold;
        }
    }
}
