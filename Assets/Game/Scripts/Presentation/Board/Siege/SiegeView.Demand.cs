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
                                         new Vector2(Span.x, Cell * .6f));
            _forecastTitle.rectTransform.anchoredPosition = new Vector2(0f, Cell * .85f);

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
                                         new Vector2(Span.x, Cell * .8f));
            _forecastClock.rectTransform.anchoredPosition = new Vector2(0f, -Cell * 1.4f);
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
        /// </summary>
        void Foretell()
        {
            if (_forecastGroup == null || _board == null) return;

            bool show = _board.Resting && !Over;

            _forecastGroup.alpha = Mathf.MoveTowards(_forecastGroup.alpha, show ? 1f : 0f,
                                                     Time.unscaledDeltaTime * 5f);

            if (!show) return;

            var coming = _board.Coming;

            for (int i = 0; i < _forecastChips.Length; i++)
            {
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

            // Gold until the last three, then the boss's own colour if one is riding this wave -
            // which is the only warning a player gets that the next thing out is not a wave.
            _forecastClock.color = coming.HasBoss ? Casting(coming.Boss)
                                 : left <= 3 ? Pal.Ember : Pal.Gold;
        }
    }
}
