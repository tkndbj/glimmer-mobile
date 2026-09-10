using System.Collections.Generic;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The two raiders that stop on the hill and work on the <em>field</em>: a weaver locking
    /// cells, a thief taking gems away, and the beat where killing one gives it all back.
    ///
    /// <para>
    /// <b>Every one of these is a rule no other gate can see.</b> A level that sends a weaver
    /// parses, validates, indexes and ships whatever the weaver actually does — there is no par to
    /// derive on a siege (invariant 37a) and no search to notice that a locked cell still lines up.
    /// The rules are proved here or nowhere.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class SiegeFieldRaiderTests
    {
        /// <summary>
        /// A field settled, playable, and carrying every colour a ward could want.
        ///
        /// <b>The shipped opening rung's own field</b> (seed 413, ten opening swaps) rather than a
        /// hand-drawn diagonal, which is what this fixture started as: a perfect checkerboard has
        /// no run on it and <em>no legal swap either</em>, so the board re-dealt itself, a weaver
        /// could never find a cell it was allowed to take, and every test here passed or failed
        /// for a reason that had nothing to do with what it was checking.
        /// </summary>
        static SiegeLayout Layout(string[] waves)
            => new SiegeLayout(
                ProtoGridOf(new[]
                {
                    "ryybgyyg",
                    "bybgrgyy",
                    "rbryyggr",
                    "grgrgbbr",
                    "yybgrrbg",
                }),
                "rgby", "rgby", waves, null, 0);

        static ProtoGrid ProtoGridOf(string[] rows)
        {
            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length, SiegeLayout.Cells,
                                            out var grid, out string error), error);
            return grid;
        }

        /// <summary>Steps a board until every raider has stopped where it stops.</summary>
        static void Settle(SiegeBoard board, float seconds)
        {
            for (int i = 0; i < seconds * 60; i++) board.Advance(1f / 60f);
        }

        /// <summary>Every cell a weaver has locked.</summary>
        static List<int> Webs(SiegeBoard board)
        {
            var found = new List<int>();
            for (int i = 0; i < board.Count; i++) if (board.IsWebbed(i)) found.Add(i);
            return found;
        }

        static List<int> Sacks(SiegeBoard board)
        {
            var found = new List<int>();
            for (int i = 0; i < board.Count; i++) if (board.IsSack(i)) found.Add(i);
            return found;
        }

        // ------------------------------------------------------------------ parsing
        /// <summary>
        /// The two modifiers are read as kinds, and a wave's length is still not its raider count.
        ///
        /// <b>The trap the shield already paid for</b> (invariant's own note on
        /// <c>SiegeLayout.Shield</c>): six places used to ask a wave's text how many raiders it
        /// held, and every one of them would have counted a modifier as one.
        /// </summary>
        [Test]
        public void AWaveReadsAWeaverAndAThiefAsKinds()
        {
            var layout = Layout(new[] { "r~g$b" });
            Assert.IsNull(layout.Fault, layout.Fault);

            Assert.AreEqual(3, layout.SizeOf(0), "three raiders, five characters");

            Assert.AreEqual(SiegeKind.Creeper, layout.KindAt(0, 0));
            Assert.AreEqual(SiegeKind.Weaver, layout.KindAt(0, 1));
            Assert.AreEqual(SiegeKind.Thief, layout.KindAt(0, 2));

            Assert.AreEqual(3, layout.RaiderCount);
        }

        /// <summary>A modifier with nothing after it is swept, exactly as a lone shield is.</summary>
        [Test]
        public void AModifierWithNoColourAfterItIsDropped()
        {
            var layout = Layout(new[] { "rg~" });
            Assert.IsNull(layout.Fault, layout.Fault);
            Assert.AreEqual(2, layout.SizeOf(0));
        }

        // ------------------------------------------------------------------ the weaver
        /// <summary>
        /// A weaver locks cells, and a locked cell can neither be moved nor lined up.
        ///
        /// <b>Both halves, because the second is the one that costs the player something.</b> A
        /// lock that only refused a swap would leave the gem underneath still able to complete
        /// somebody else's run, so the web would cost nothing but a little reach.
        /// </summary>
        [Test]
        public void AWeaverLocksCellsAndALockedCellCanNeitherMoveNorMatch()
        {
            var board = SiegeBoard.Build(Layout(new[] { "~r" }));

            Settle(board, 30f);

            var webbed = Webs(board);
            Assert.IsNotEmpty(webbed, "a weaver that has stood on the hill for thirty seconds "
                                    + "has spun nothing");

            int cell = webbed[0];
            Assert.IsFalse(board.Movable(cell), "a webbed gem may be dragged");

            // Nothing can line up through it: `Lines` refuses either end of a swap that touches
            // one, which is the one door every reader of the field goes through.
            int right = cell + 1;
            if (right < board.Count && board.Adjacent(cell, right))
                Assert.IsFalse(board.Lines(cell, right), "a webbed gem was swapped");
        }

        /// <summary>
        /// Killing the last weaver frees every cell it locked, in one beat.
        ///
        /// <b>The payoff rather than the tidy-up</b> (invariant 20m): what a player sees when a
        /// weaver goes down is the board they were promised, all at once.
        /// </summary>
        [Test]
        public void KillingAWeaverFreesEveryCellItLocked()
        {
            var board = SiegeBoard.Build(Layout(new[] { "~r" }));

            Settle(board, 30f);
            Assert.IsNotEmpty(Webs(board));

            // A storm reaches everything on the hill, and a weaver is worth 620.
            var into = new List<SiegeStrike>();
            board.Storm(9999, into);
            board.Advance(1f / 60f);

            Assert.IsEmpty(Webs(board), "the webs outlived the weaver");
        }

        /// <summary>
        /// Two weavers are twice the pressure and one answer: the field comes back when the
        /// <em>last</em> of them is dead and not before.
        ///
        /// Freeing on the first death would make the second weaver worth nothing at all, which is
        /// invariant 5d asked of a raider.
        /// </summary>
        [Test]
        public void TheFieldComesBackOnlyWhenTheLastWeaverIsGone()
        {
            var board = SiegeBoard.Build(Layout(new[] { "~r~g" }));

            Settle(board, 30f);
            Assert.IsNotEmpty(Webs(board));

            // One of the two, by hand. **The box it is standing in is not enough**: a firepot
            // takes the box that was tapped and the four touching it, so the obvious tap can
            // easily take both and this test would pass for the wrong reason. The box is searched
            // for instead - one that reaches this weaver and no other.
            var weaver = FirstOf(board, SiegeKind.Weaver);
            Assert.IsNotNull(weaver);

            int lane = -1, row = -1;

            for (int r = 0; r < SiegeTuning.BlastRows && row < 0; r++)
            {
                for (int l = 0; l < SiegeTuning.Lanes; l++)
                {
                    if (!SiegeTuning.Caught(weaver.Kind, weaver.Lane, weaver.March, l, r)) continue;

                    bool alone = true;

                    foreach (var other in board.Raiders)
                        if (other != weaver && other.Alive && other.OnTheHill
                            && other.Kind == SiegeKind.Weaver
                            && SiegeTuning.Caught(other.Kind, other.Lane, other.March, l, r))
                            alone = false;

                    if (!alone) continue;

                    lane = l;
                    row = r;
                    break;
                }
            }

            Assert.GreaterOrEqual(row, 0, "no firepot reaches one of these weavers on its own");

            var into = new List<SiegeStrike>();
            board.Blast(lane, row, 9999, into);
            board.Advance(1f / 60f);

            Assert.IsNotNull(FirstOf(board, SiegeKind.Weaver), "the fixture killed both");
            Assert.IsNotEmpty(Webs(board), "the webs came off while a weaver was still standing");
        }

        // ------------------------------------------------------------------ the thief
        /// <summary>
        /// A thief takes gems off the field, and what it leaves is not a colour at all.
        ///
        /// <b>Which is the whole difference from a web.</b> A webbed gem is still that colour and
        /// the player can see what they are being denied; a sack can never line up with anything.
        /// </summary>
        [Test]
        public void AThiefLeavesSacksThatAreNotAColour()
        {
            var board = SiegeBoard.Build(Layout(new[] { "$r" }));

            Settle(board, 40f);

            var sacks = Sacks(board);
            Assert.IsNotEmpty(sacks, "a thief that has stood on the hill for forty seconds has "
                                   + "taken nothing");

            int cell = sacks[0];
            Assert.AreEqual(-1, board.ColourAt(cell), "a sack answers as a colour");
            Assert.IsFalse(board.Movable(cell), "a sack may be dragged");
        }

        /// <summary>Killing the thief bursts every sack back into a gem.</summary>
        [Test]
        public void KillingAThiefGivesTheFieldBack()
        {
            var board = SiegeBoard.Build(Layout(new[] { "$r" }));

            Settle(board, 40f);
            Assert.IsNotEmpty(Sacks(board));

            var into = new List<SiegeStrike>();
            board.Storm(9999, into);
            board.Advance(1f / 60f);

            Assert.IsEmpty(Sacks(board), "the sacks outlived the thief");
        }

        // ------------------------------------------------------------------ the bounds
        /// <summary>
        /// Neither can take more of the field than the ceiling allows, however long it stands
        /// there.
        ///
        /// <b>A cap on the board rather than on the raider</b> (<c>SiegeTuning.MostWebs</c>): how
        /// often one is spun is a rate, how many are standing is a fact about the field, and it is
        /// the second one a player experiences.
        /// </summary>
        [Test]
        public void NeitherEverTakesMoreOfTheFieldThanTheCeiling()
        {
            var board = SiegeBoard.Build(Layout(new[] { "~r$g" }));

            Settle(board, 240f);

            Assert.LessOrEqual(Webs(board).Count, SiegeTuning.MostWebs);
            Assert.LessOrEqual(Sacks(board).Count, SiegeTuning.MostSacks);
        }

        /// <summary>
        /// The field is always playable, however much of it has been taken.
        ///
        /// <b>The one thing this mode may never show</b>: its clock does not stop, so a field with
        /// no legal swap is a run the player watches themselves lose. The board refuses a mark
        /// that would leave one and re-deals rather than locking.
        /// </summary>
        [Test]
        public void TheFieldIsAlwaysPlayableHoweverMuchOfItIsTaken()
        {
            var board = SiegeBoard.Build(Layout(new[] { "~r$g~b$y" }));

            for (int i = 0; i < 240 * 60; i++)
            {
                board.Advance(1f / 60f);

                if (i % 600 != 0) continue;

                Assert.IsTrue(board.AnySwap(),
                              $"the field had no legal swap after {i / 60}s, with "
                              + $"{Webs(board).Count} web(s) and {Sacks(board).Count} sack(s)");
            }
        }

        /// <summary>
        /// Neither takes a ward's health, ever.
        ///
        /// <b>What makes them a pressure rather than a threat</b>, and the reason
        /// <c>ModeValidator</c> refuses a wave that holds nothing else: a level whose only raiders
        /// worked on the field could not be lost.
        /// </summary>
        [Test]
        public void NeitherEverTakesAWardsHealth()
        {
            Assert.AreEqual(0, SiegeTuning.BlowOf(SiegeKind.Weaver));
            Assert.AreEqual(0, SiegeTuning.BlowOf(SiegeKind.Thief));

            Assert.IsFalse(SiegeTuning.EndangersTheLine(SiegeKind.Weaver));
            Assert.IsFalse(SiegeTuning.EndangersTheLine(SiegeKind.Thief));

            var board = SiegeBoard.Build(Layout(new[] { "~r$g" }));
            Settle(board, 180f);

            int health = 0;
            foreach (var ward in board.Wards) health += ward.Health;

            Assert.AreEqual(board.Wards.Count * SiegeTuning.WardHealth, health,
                            "the line lost health to a hill that cannot touch it");
        }

        /// <summary>Neither reaches the line: both stop where their kind stops.</summary>
        [Test]
        public void NeitherEverReachesTheLine()
        {
            var board = SiegeBoard.Build(Layout(new[] { "~r$g" }));
            Settle(board, 180f);

            foreach (var raider in board.Raiders)
            {
                if (!raider.Alive) continue;

                Assert.Less(raider.March, 1f, $"a {SiegeTuning.NameOf(raider.Kind)} reached the line");
                Assert.IsFalse(raider.AtTheLine);
            }
        }

        static SiegeRaider FirstOf(SiegeBoard board, SiegeKind kind)
        {
            foreach (var raider in board.Raiders)
                if (raider.Alive && raider.Kind == kind) return raider;

            return null;
        }
    }
}
