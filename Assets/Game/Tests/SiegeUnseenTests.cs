using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The raiders a strong line kills before anything can draw them.
    ///
    /// <para>
    /// <b>A file of its own because the fault it pins was invisible to every other one, and to
    /// every gate.</b> Reported from play: <em>no raider came in, the hill was visually empty, but
    /// cogs were dropping on the ground</em> — with a line of five-star legendaries. Nothing about
    /// the rules was wrong, and a rules sweep could never have seen it: every raider was mustered,
    /// walked, shot, killed and paid for exactly as authored.
    /// </para>
    /// <para>
    /// <b>What was wrong is that a raider is a <em>state</em> and can have a lifetime shorter than
    /// a frame.</b> <c>SiegeBoard.Advance</c> musters, walks and fires in one call and then sweeps
    /// the dead out of its own list; the view mints a raider's widget from that list, once a step,
    /// afterwards. So a raider mustered and felled inside one call was never in the list at any
    /// moment the view could look — and because every drawing this mode makes about a raider is
    /// keyed on that widget, the bolt, the muzzle flash, the impact and the death burst all went
    /// with the body. The cog was the only thing left, because a cog carries its own coordinates.
    /// </para>
    /// <para>
    /// <b>And it is the ordinary case rather than a rare interleaving</b>, which is the part worth
    /// keeping: a fuelled ward with nothing to shoot at holds its cooldown at nought
    /// (<c>SiegeBoard.Shoot</c>), so between waves the whole line is loaded and waiting, and every
    /// raider of the next wave is answered by four turrets on the frame it steps out.
    /// </para>
    /// <para>
    /// So what is held here is the <em>channel</em>, not the arithmetic: a kill reaches the view as
    /// a <see cref="SiegeReport.Felled"/> record carrying the body, whether or not that body ever
    /// appeared in <c>SiegeBoard.Raiders</c> at a moment anything could read it.
    /// </para>
    /// </summary>
    public sealed class SiegeUnseenTests
    {
        static readonly string[] Field =
        {
            "rgrgyrry",
            "bgygybbg",
            "grrbbgyr",
            "bybbgryg",
            "ryygybrb",
        };

        const float Step = 1f / 60f;

        /// <summary>
        /// <paramref name="cogs"/> is the drop rate in percent, and it is <b>100</b> by default
        /// here on purpose: the fault was reported as <em>cogs dropping on an empty hill</em>, so
        /// the thing the player could still see has to be reproducible in the same run as the
        /// thing they could not.
        /// </summary>
        static SiegeLayout Layout(string[] waves, int cogs = 100)
        {
            Assert.IsTrue(ProtoGrid.TryRead(Field, Field[0].Length, Field.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, "rgby", "rgby", waves, null, cogs);
            Assert.IsNull(layout.Fault, layout.Fault);
            return layout;
        }

        /// <summary>
        /// The line the fault was reported on: the strongest turret on the shelf, at the top of
        /// its ladder, on all four seats.
        ///
        /// <b>Read off the roster rather than named</b>, so a retune or a new top band keeps this
        /// fixture pointed at whatever the hardest-hitting line has become — which is the line
        /// that reproduces this, and the only thing about it that matters is that it fells a
        /// raider with one bolt.
        /// </summary>
        static WardLine Hardest()
        {
            WardModel best = null;

            foreach (var model in WardCatalog.Default.Models)
                if (best == null || model.PowerTenths > best.PowerTenths) best = model;

            Assert.IsNotNull(best, "the turret roster is empty");

            var slots = new List<WardSlot>();
            foreach (char colour in WardLine.Colours) slots.Add(new WardSlot(colour, best.Id));

            return WardLine.Resolve(WardCatalog.Default, slots, (_, __) => true,
                                    (_, __) => WardStars.Most);
        }

        static SiegeBoard Loaded(string[] waves)
        {
            var board = SiegeBoard.Build(Layout(waves), Hardest());

            for (int w = 0; w < board.Wards.Count; w++)
                board.Wards[w].Fuel = board.Wards[w].Capacity;

            return board;
        }

        /// <summary>
        /// Every raider the board felled over a run, and every raider anything watching the board
        /// one step at a time could ever have seen standing on the hill.
        /// </summary>
        static void Play(SiegeBoard board, int steps,
                         out Dictionary<int, SiegeRaider> felled, out HashSet<int> seen,
                         out int cogs)
        {
            felled = new Dictionary<int, SiegeRaider>();
            seen = new HashSet<int>();
            cogs = 0;

            for (int i = 0; i < steps; i++)
            {
                var report = board.Advance(Step);

                for (int k = 0; k < report.Felled.Count; k++)
                    felled[report.Felled[k].Id] = report.Felled[k];

                cogs += report.Cogs.Count;

                // **Exactly what the view can do, and nothing more.** It reads the board's list
                // once a step, after the step - so a body that was not in it at this moment was
                // never available to be drawn, whatever happened to it inside the call.
                for (int k = 0; k < board.Raiders.Count; k++)
                {
                    var raider = board.Raiders[k];
                    if (raider.Alive && raider.OnTheHill) seen.Add(raider.Id);
                }
            }
        }

        // ------------------------------------------------------------------ the channel
        /// <summary>
        /// <b>A raider that lived less than one step is still reported, and that is the whole
        /// fix.</b>
        ///
        /// <para>
        /// The two assertions are one sentence in two halves. The first is that the setup still
        /// reproduces the report — if every raider is visible for at least one step then this
        /// fixture is green about nothing, which is the state it would quietly drift into if a
        /// turret were retuned or the muster gained a beat. The second is the rule: whatever the
        /// board killed, it said so, so <c>SiegeView.Unseen</c> has a body to stand where it fell.
        /// </para>
        /// </summary>
        [Test]
        public void ARaiderFelledInsideOneStepIsStillReported()
        {
            var board = Loaded(new[] { "rrrr", "gggg", "bbbb" });

            Play(board, 60 * 40, out var felled, out var seen, out int cogs);

            Assert.Greater(felled.Count, 0,
                           "four five-star turrets on a fed line killed nothing in forty seconds");

            var unseen = new HashSet<int>(felled.Keys);
            unseen.ExceptWith(seen);

            Assert.Greater(unseen.Count, 0,
                           "every raider this line killed was visible on the board for at least "
                           + "one step, so this fixture is no longer standing over the fault it "
                           + "was written for - a fed ward with no target holds its cooldown at "
                           + "nought, so a wave stepping out should still be answered on the frame "
                           + "it arrives");

            // **The two clauses the fix rests on, asked of every body that was never seen.**
            //
            // The first is that the report really is the only channel: the board has swept the
            // raider, so nothing looking at `SiegeBoard.Raiders` afterwards - which is all the
            // view ever did - can recover it. The second is that the record is *usable*:
            // `SiegeView.Unseen` stands the body where it fell and skips anything still in the
            // wings, so a corpse reported with `OnTheHill` false would be a fix that compiles,
            // runs, and draws exactly as little as before.
            foreach (int id in unseen)
            {
                Assert.IsNull(board.Find(id),
                              $"raider {id} was never seen alive on the hill and the board still "
                              + "holds it, so the run being reproduced here is not the one the "
                              + "fault was reported from");

                Assert.IsTrue(felled[id].OnTheHill,
                              $"raider {id} was reported felled off the hill, and `SiegeView."
                              + "Unseen` draws nothing for a body that never stepped out");
            }

            Assert.Greater(cogs, 0, "nothing this line killed ever dropped a cog, so the report "
                                    + "the player actually saw cannot be reproduced here");
        }

        /// <summary>
        /// <b>Every kill is reported exactly once, whoever made it.</b>
        ///
        /// <para>
        /// The view mints a body for a felled raider it has no widget for and lets <c>Reap</c>
        /// take it down in the same frame, so a raider reported twice would be a body blown apart
        /// twice — and a raider reported and <em>also</em> left in the board's list alive would be
        /// a corpse standing on the hill. Both are cheap to refuse here and impossible to see on a
        /// device without playing the exact run again.
        /// </para>
        /// </summary>
        [Test]
        public void NoKillIsReportedTwiceAndNoReportedKillIsStillAlive()
        {
            var board = Loaded(new[] { "rgby", "rrgg", "bbyy" });

            var counted = new Dictionary<int, int>();

            for (int i = 0; i < 60 * 40; i++)
            {
                var report = board.Advance(Step);

                for (int k = 0; k < report.Felled.Count; k++)
                {
                    var raider = report.Felled[k];

                    Assert.IsFalse(raider.Alive,
                                   $"raider {raider.Id} was reported felled and is still alive");

                    counted.TryGetValue(raider.Id, out int times);
                    counted[raider.Id] = times + 1;
                }
            }

            Assert.Greater(counted.Count, 0, "nothing was killed in forty seconds");

            foreach (var pair in counted)
                Assert.AreEqual(1, pair.Value,
                                $"raider {pair.Key} was reported felled {pair.Value} times, so "
                                + "the view would draw it coming apart that many times");
        }

        /// <summary>
        /// <b>A kill made outside <c>Advance</c> does not arrive here a frame later.</b>
        ///
        /// <para>
        /// A firepot, a utility and an overcharge all kill from a tap, and the view takes their
        /// widgets down itself (<c>SiegeView.Settled</c>). <c>Advance</c> clears the report before
        /// it does anything, so those kills are wiped before any frame reads them — which is what
        /// keeps <c>SiegeView.Unseen</c> from minting a second body for a raider that has already
        /// been blown apart. The clause is one line in <c>Advance</c> and nothing else says it.
        /// </para>
        /// </summary>
        [Test]
        public void AKillMadeOutsideTheStepIsNotReportedByTheNextOne()
        {
            // **The starter line, dry**, and that is the only way to get a raider to stand still
            // long enough to be killed by hand: the loaded line above fells every one of them on
            // the frame it steps out, which is the fault the first case is about.
            var board = SiegeBoard.Build(Layout(new[] { "rrrr" }),
                                         WardLine.Resolve(WardCatalog.Default, null,
                                                          (_, __) => true));

            SiegeRaider standing = null;

            for (int i = 0; i < 60 * 20 && standing == null; i++)
            {
                board.Advance(Step);

                for (int k = 0; k < board.Raiders.Count; k++)
                    if (board.Raiders[k].Alive && board.Raiders[k].OnTheHill)
                    {
                        standing = board.Raiders[k];
                        break;
                    }
            }

            Assert.IsNotNull(standing, "no raider ever stood on the hill");

            // A firepot, which is the ordinary door a player kills through from a tap.
            int id = standing.Id;

            var strikes = new List<SiegeStrike>();
            board.Blast(standing.Lane, SiegeTuning.RowOf(standing.March),
                        standing.MaxHealth * 40, strikes);

            Assert.IsFalse(standing.Alive,
                           "the firepot did not kill the one raider it was dropped on");

            var next = board.Advance(Step);

            for (int k = 0; k < next.Felled.Count; k++)
                Assert.AreNotEqual(id, next.Felled[k].Id,
                                   "a raider felled outside `Advance` was reported by the next "
                                   + "step, so the view would mint a second body for one it has "
                                   + "already taken down");
        }
    }
}
