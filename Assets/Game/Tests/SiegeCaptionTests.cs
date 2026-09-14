using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Where a siege board's two hill captions sit, over every screen shape one could be drawn
    /// at: the chain banner a cascade raises, and the banner a wave arrives under.
    ///
    /// <para>
    /// <b>This exists for <c>SiegeBandTests</c>' reason and it caught the same class of fault.</b>
    /// <c>Tools/render_siege.py</c> draws whatever canvas it is given and drew neither of these
    /// at all, so what shipped was a wave banner floating up <em>through</em> a chain banner —
    /// about 1.4 cells of shared row on a 19.5:9 phone, a 16:9 sheet and a tablet alike. Each
    /// number was individually reasonable; the pair was wrong everywhere, because one was
    /// measured from the ward line and the other from the hill's foot and those two anchors move
    /// apart as the display changes shape.
    /// </para>
    /// <para>
    /// Both are arithmetic over two numbers now (<c>SiegeView.Captions.Of</c>), so they can be
    /// swept rather than looked at.
    /// </para>
    /// </summary>
    public sealed class SiegeCaptionTests
    {
        /// <summary>
        /// <c>SiegeBandTests</c>' own sweep: a 4:3 tablet at the short end, a 19.5:9 phone at
        /// the tall one, and the sheet the render draws by default in between.
        /// </summary>
        static readonly float[] Spans = { 900f, 1100f, 1356f, 1500f, 1752f, 2000f };

        /// <summary>An 8x5 field at the width, which is every board this chapter ships.</summary>
        const float Cell = 130.5f;
        const int Rows = 5;

        static SiegeView.Captions At(float span)
            => SiegeView.Captions.Of(SiegeView.Bands.Of(span, Cell, Rows).LineY, Cell);

        /// <summary>
        /// The one that would have caught it. Both captions are wide centred lines of text, so
        /// sharing any part of a row means one is drawn over the other.
        /// </summary>
        [Test]
        public void TheWaveBannerNeverSharesARowWithTheChainBanner()
        {
            foreach (var span in Spans)
            {
                var c = At(span);

                Assert.Greater(c.WaveLow, c.ChainHigh,
                               $"the wave banner reaches down to {c.WaveLow:0} and the chain "
                               + $"banner up to {c.ChainHigh:0} at span {span}");
            }
        }

        /// <summary>
        /// And with air to spare, because both of them <em>move</em>: touching at rest is two
        /// captions that meet a quarter of a second later.
        /// </summary>
        [Test]
        public void ThereIsRealAirBetweenThem()
        {
            foreach (var span in Spans)
            {
                var c = At(span);

                Assert.GreaterOrEqual(c.WaveLow - c.ChainHigh, Cell * .2f,
                                      $"only {(c.WaveLow - c.ChainHigh) / Cell:0.00} cells "
                                      + $"between the two captions at span {span}");
            }
        }

        /// <summary>
        /// The wave banner is the one above, which is the way round it has to read: a wave comes
        /// in over the top of the hill and a cascade happened down on the field.
        /// </summary>
        [Test]
        public void TheWaveBannerIsTheUpperOne()
        {
            foreach (var span in Spans)
                Assert.Greater(At(span).Wave, At(span).Chain, $"at span {span}");
        }

        /// <summary>
        /// The chain keeps the place invariant 37k gave it — the empty run of hill just above
        /// the turrets — and the ladder is stacked on top of that rather than instead of it.
        /// </summary>
        [Test]
        public void TheChainBannerStillStandsJustAboveTheWardLine()
        {
            foreach (var span in Spans)
            {
                var bands = SiegeView.Bands.Of(span, Cell, Rows);
                var c = SiegeView.Captions.Of(bands.LineY, Cell);

                Assert.Greater(c.ChainLow, bands.LineY,
                               $"the chain banner is drawn into the ward line at span {span}");
            }
        }

        /// <summary>
        /// On the shape this mode is actually played at, both of them are on the hill.
        ///
        /// <para>
        /// <b>Tall only, and deliberately so.</b> A short board leaves the hill less room than
        /// one of these captions needs, let alone two — so no arrangement fits there and
        /// clamping would only put them back on top of each other, which is what the sweep above
        /// exists to stop. Stated the same way <c>SiegeBandTests</c> states the hill being the
        /// biggest band, so the next person to read a wide render is not surprised by it.
        /// </para>
        /// <para>
        /// <b>Which display that is has moved, and it is the opposite of where anybody would
        /// look.</b> It was the 4:3 tablet, at 3.15 cells of hill; capping the field to a phone's
        /// width (invariant 37cc) gives a tablet 5.6 cells and both banners fit there now. What
        /// is left is the squarest <em>phone</em> — the 16:9 iPhone SE at 4.0 cells, which this
        /// change did not touch and which no cap can help, because its hill is short for the
        /// honest reason that its display is.
        /// </para>
        /// </summary>
        [Test]
        public void BothCaptionsAreOnTheHillOnAnythingPhoneShaped()
        {
            foreach (var span in Spans)
            {
                if (span < 1600f) continue;

                var bands = SiegeView.Bands.Of(span, Cell, Rows);
                var c = SiegeView.Captions.Of(bands.LineY, Cell);

                Assert.LessOrEqual(c.WaveHigh, bands.HillTop,
                                   $"the wave banner floats off the top of the hill at span {span}");
                Assert.GreaterOrEqual(c.ChainLow, bands.HillFoot,
                                      $"the chain banner is drawn below the hill at span {span}");
            }
        }

        /// <summary>
        /// **Every boss this mode sends is announced as itself**, and no two share a banner.
        ///
        /// <para>
        /// <b>Invariant 44e, on the one moment that exists to say "this is not the thing you
        /// fought last time".</b> <c>SiegeView.BossKey</c> ends in a <c>default</c> arm that
        /// returns the warlord's key, which is a real answer — so a boss added without a key of
        /// its own walks on under another boss's name, in the right colour, at the right size,
        /// with every other gate green. Two of them shared one banner once already, while the mode
        /// had two bosses, and the entry that fixed it is the one this checks.
        /// </para>
        /// <para>
        /// It asks about <b>keys</b> rather than about text, for <c>SkinsTests</c>' reason: no
        /// strings are loaded in an offline run, so a lookup would answer the key back whatever the
        /// table says and a check that cannot fail is not a check. That every key here resolves is
        /// <c>Tools/verify/loc.py</c>'s job.
        /// </para>
        /// </summary>
        [Test]
        public void EveryBossIsAnnouncedAsItself()
        {
            var bosses = new List<SiegeKind>();

            foreach (SiegeKind kind in System.Enum.GetValues(typeof(SiegeKind)))
                if (SiegeTuning.IsBoss(kind)) bosses.Add(kind);

            Assert.IsNotEmpty(bosses, "this mode sends no bosses at all any more");

            var seen = new Dictionary<string, SiegeKind>();

            foreach (var kind in bosses)
            {
                string key = SiegeView.BossKey(kind);

                Assert.IsNotEmpty(key, $"a {kind} is announced under no key at all");

                if (seen.TryGetValue(key, out var already))
                    Assert.Fail($"a {kind} and a {already} are both announced as '{key}', so one "
                                + "of them walks on under the other's name");

                seen[key] = kind;
            }
        }

    }
}
