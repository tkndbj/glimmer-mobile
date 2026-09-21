using System;
using GlimmerGrove.Events;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The three numbers a season keeps in the save, and the join that merges them.
    ///
    /// <para>
    /// Marks grown and a claim floor per track — all monotone, all joined by <c>max</c>,
    /// which is the only shape invariant 11b allows a count to take. What is pinned here is
    /// the file bridge and the join; what a rung is <em>worth</em> lives in the shared
    /// reward vectors, which both halves of the economy run.
    /// </para>
    /// <para>
    /// Claiming itself is deliberately not tested here: it writes the save, rolls a chest
    /// from the account seed and hands currency to the wallet, so a case for it would be a
    /// case about four other systems. <c>MarkVectorTests</c> pins the roll against the
    /// server's copy, and the claim's two guards — the floor and the derived grant id — are
    /// each proved where they live.
    /// </para>
    /// </summary>
    public sealed class SeasonLedgerTests
    {
        static SaveFileDto File(params (string id, int marks, int free, int pass)[] rows)
        {
            var seasons = new EventStateDto[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                seasons[i] = new EventStateDto
                {
                    id = rows[i].id,
                    marks = rows[i].marks,
                    collectedGoal = rows[i].free,
                    premiumGoal = rows[i].pass,
                };

            return new SaveFileDto { events = seasons };
        }

        /// <summary>One season's row, with the pass entitlement on it.</summary>
        static SaveFileDto Held(string id, int marks, int free, int pass)
            => new SaveFileDto
            {
                events = new[]
                {
                    new EventStateDto
                    {
                        id = id, marks = marks, collectedGoal = free,
                        premiumGoal = pass, pass = true,
                    },
                },
            };

        [SetUp]
        public void Reset() => SeasonLedger.ResetForTests();

        [TearDown]
        public void Clear() => SeasonLedger.ResetForTests();

        // ---------------------------------------------------------------- reading
        [Test]
        public void AFileWithNoRowsHasGrownNothing()
        {
            SeasonLedger.LoadFrom(File());

            Assert.AreEqual(0, SeasonLedger.MarksIn("vector_bloom"));
            Assert.AreEqual(0, SeasonLedger.ClaimedGoal("vector_bloom", SeasonTrack.Free));
            Assert.AreEqual(0, SeasonLedger.ClaimedGoal(null, SeasonTrack.Pass));
        }

        [Test]
        public void EachTrackKeepsItsOwnFloor()
        {
            SeasonLedger.LoadFrom(File(("vector_bloom", 30, 20, 10)));

            Assert.AreEqual(30, SeasonLedger.MarksIn("vector_bloom"));
            Assert.AreEqual(20, SeasonLedger.ClaimedGoal("vector_bloom", SeasonTrack.Free));
            Assert.AreEqual(10, SeasonLedger.ClaimedGoal("vector_bloom", SeasonTrack.Pass));
        }

        /// <summary>
        /// A pass is bought optimistically and the debit is what makes it real, so a debit the
        /// server refuses takes the pass back — and nothing else. The paid floor is a record of
        /// chests already opened (47c), so it stays; a later purchase resumes above it. This is
        /// the client half of the day a pass debit reached the server in the wrong currency and
        /// was refused on every sync while the page went on drawing the pass as held.
        /// </summary>
        [Test]
        public void ARefusedPassDebitTakesThePassBackAndNothingElse()
        {
            SeasonLedger.LoadFrom(Held("watch_0000", 28, 25, 5));
            Assert.IsTrue(SeasonLedger.OwnsPass("watch_0000"));

            // A refusal of some other debit, and of a pass for a season this file does not hold,
            // each leave it alone.
            SeasonLedger.OnSpendRejected(Currency.Gems, "continue:s01_lastlight");
            SeasonLedger.OnSpendRejected(Currency.Gems, SpendEntry.SeasonPassId("watch_0001"));
            Assert.IsTrue(SeasonLedger.OwnsPass("watch_0000"));

            SeasonLedger.OnSpendRejected(Currency.Gems, SpendEntry.SeasonPassId("watch_0000"));

            Assert.IsFalse(SeasonLedger.OwnsPass("watch_0000"), "a refused pass is no longer held");
            Assert.AreEqual(28, SeasonLedger.MarksIn("watch_0000"), "the marks are play, not the pass");
            Assert.AreEqual(25, SeasonLedger.ClaimedGoal("watch_0000", SeasonTrack.Free));
            Assert.AreEqual(5, SeasonLedger.ClaimedGoal("watch_0000", SeasonTrack.Pass),
                            "the paid floor is a record of chests opened and stays");

            // Refusing it again is a no-op rather than a second announcement.
            SeasonLedger.OnSpendRejected(Currency.Gems, SpendEntry.SeasonPassId("watch_0000"));
            Assert.IsFalse(SeasonLedger.OwnsPass("watch_0000"));
        }

        [Test]
        public void APassDebitIdNamesItsSeasonAndNothingElseDoes()
        {
            Assert.AreEqual("watch_0000", SpendEntry.SeasonOfPassId(SpendEntry.SeasonPassId("watch_0000")));
            Assert.IsNull(SpendEntry.SeasonOfPassId("continue:s01_lastlight"));
            Assert.IsNull(SpendEntry.SeasonOfPassId("pass:"));
            Assert.IsNull(SpendEntry.SeasonOfPassId(null));
            Assert.IsNull(SpendEntry.SeasonOfPassId(string.Empty));
        }

        /// <summary>
        /// A v27 file has no mark count at all, and <c>JsonUtility</c> writes nought into a
        /// field an older file never had — which is exactly the right answer here, and is why
        /// v28 needed no migration. A stale free floor written under the old meaning is then
        /// clamped to the marks on the first read, whatever it says.
        /// </summary>
        [Test]
        public void AnOlderFilesFloorIsClampedToNothingByAnAbsentBloomCount()
        {
            SeasonLedger.LoadFrom(File(("vector_bloom", 0, 10, 0)));

            var season = new GroveEvent("vector_bloom", 100, 200,
                                        new[] { new EventMilestone(10, "wood", null) });

            Assert.AreEqual(0, SeasonLedger.ProgressOf(season).Free.Claimed);
            Assert.AreEqual(0, SeasonLedger.ProgressOf(season).Rungs);
        }

        [Test]
        public void ANegativeRowReadsAsNothingRatherThanAsDebt()
        {
            SeasonLedger.LoadFrom(File(("vector_bloom", -5, -1, -9)));

            Assert.AreEqual(0, SeasonLedger.MarksIn("vector_bloom"));
            Assert.AreEqual(0, SeasonLedger.ClaimedGoal("vector_bloom", SeasonTrack.Free));
        }

        // ------------------------------------------------------------------- join
        [Test]
        public void JoiningTakesTheLargerOfEveryNumber()
        {
            var joined = SeasonLedger.Join(
                new[] { new EventStateDto { id = "a", marks = 30, collectedGoal = 20, premiumGoal = 0 } },
                new[] { new EventStateDto { id = "a", marks = 12, collectedGoal = 10, premiumGoal = 12 } });

            Assert.AreEqual(30, Field(joined, "a", r => r.marks));
            Assert.AreEqual(20, Field(joined, "a", r => r.collectedGoal));
            Assert.AreEqual(12, Field(joined, "a", r => r.premiumGoal),
                            "a field is joined on its own, not by picking whichever row looks fuller");
        }

        /// <summary>
        /// A join in the strict sense, which is what lets any number of devices sync in any
        /// order. Invariant 11.
        /// </summary>
        [Test]
        public void TheJoinIsIdempotentAndOrderIndependent()
        {
            var mine = new[] { new EventStateDto { id = "a", marks = 12, collectedGoal = 12 } };
            var other = new[] { new EventStateDto { id = "a", marks = 30, premiumGoal = 24 },
                                new EventStateDto { id = "b", marks = 6 } };

            var once = SeasonLedger.Join(mine, other);
            var twice = SeasonLedger.Join(once, other);
            var flipped = SeasonLedger.Join(other, mine);

            foreach (var id in new[] { "a", "b" })
            {
                Assert.AreEqual(Field(once, id, r => r.marks), Field(twice, id, r => r.marks));
                Assert.AreEqual(Field(once, id, r => r.marks), Field(flipped, id, r => r.marks));
                Assert.AreEqual(Field(once, id, r => r.collectedGoal), Field(flipped, id, r => r.collectedGoal));
                Assert.AreEqual(Field(once, id, r => r.premiumGoal), Field(flipped, id, r => r.premiumGoal));
            }
        }

        /// <summary>
        /// A device on last month's content has never heard of this month's season. Dropping
        /// the row it does not recognise would take the chest back off the device that does.
        /// </summary>
        [Test]
        public void ASeasonOnlyOneSideKnowsAboutSurvives()
        {
            var joined = SeasonLedger.Join(
                new[] { new EventStateDto { id = "old", marks = 18, collectedGoal = 12 } },
                new[] { new EventStateDto { id = "new", marks = 6 } });

            Assert.AreEqual(18, Field(joined, "old", r => r.marks));
            Assert.AreEqual(6, Field(joined, "new", r => r.marks));
        }

        [Test]
        public void AnEmptyOrMissingSideKeepsTheOther()
        {
            var mine = new[] { new EventStateDto { id = "a", marks = 24, collectedGoal = 18 } };

            Assert.AreEqual(24, Field(SeasonLedger.Join(mine, null), "a", r => r.marks));
            Assert.AreEqual(24, Field(SeasonLedger.Join(null, mine), "a", r => r.marks));
            Assert.AreEqual(24, Field(SeasonLedger.Join(mine, new EventStateDto[0]), "a", r => r.marks));
            Assert.IsNotNull(SeasonLedger.Join(null, null));
        }

        /// <summary>
        /// Two rows for one season is a malformed file, not two tracks. The larger of each
        /// field wins, for the same reason the merge takes the larger.
        /// </summary>
        [Test]
        public void ADuplicatedRowCollapsesToItsLargestValues()
        {
            var joined = SeasonLedger.Join(
                new[] { new EventStateDto { id = "a", marks = 6, collectedGoal = 6 },
                        new EventStateDto { id = "a", marks = 24, premiumGoal = 18 } },
                null);

            Assert.AreEqual(1, joined.Length);
            Assert.AreEqual(24, Field(joined, "a", r => r.marks));
            Assert.AreEqual(6, Field(joined, "a", r => r.collectedGoal));
            Assert.AreEqual(18, Field(joined, "a", r => r.premiumGoal));
        }

        // ------------------------------------------------------------------ write
        /// <summary>
        /// Written sorted, because <c>SaveDelta</c> walks these in order and
        /// <c>SaveChecksum</c> hashes them. Dictionary order would make an unchanged save
        /// look changed on every launch — a write and an upload, for ever, for nothing.
        /// </summary>
        [Test]
        public void SeasonsAreWrittenSortedById()
        {
            SeasonLedger.LoadFrom(File(("zeta", 1, 0, 0), ("alpha", 2, 0, 0), ("mid", 3, 0, 0)));

            var dto = new SaveFileDto();
            SeasonLedger.WriteInto(dto);

            Assert.AreEqual(3, dto.events.Length);
            Assert.AreEqual("alpha", dto.events[0].id);
            Assert.AreEqual("mid", dto.events[1].id);
            Assert.AreEqual("zeta", dto.events[2].id);
        }

        [Test]
        public void AWriteFollowedByAReadChangesNothing()
        {
            SeasonLedger.LoadFrom(File(("a", 24, 18, 12), ("b", 6, 6, 0)));

            var dto = new SaveFileDto();
            SeasonLedger.WriteInto(dto);

            SeasonLedger.ResetForTests();
            SeasonLedger.LoadFrom(dto);

            Assert.AreEqual(24, SeasonLedger.MarksIn("a"));
            Assert.AreEqual(18, SeasonLedger.ClaimedGoal("a", SeasonTrack.Free));
            Assert.AreEqual(12, SeasonLedger.ClaimedGoal("a", SeasonTrack.Pass));
            Assert.AreEqual(6, SeasonLedger.MarksIn("b"));
        }

        /// <summary>
        /// A season nothing has happened on writes no row at all, so a fresh account does not
        /// carry a calendar's worth of zeroes into every save it ever pushes.
        /// </summary>
        [Test]
        public void ASeasonWithNothingOnItWritesNoRow()
        {
            SeasonLedger.LoadFrom(File(("a", 0, 0, 0), ("b", 6, 0, 0)));

            var dto = new SaveFileDto();
            SeasonLedger.WriteInto(dto);

            Assert.AreEqual(1, dto.events.Length);
            Assert.AreEqual("b", dto.events[0].id);
        }

        [Test]
        public void AnEmptyCalendarIsAnEmptyArrayRatherThanNull()
        {
            SeasonLedger.LoadFrom(File());

            var dto = new SaveFileDto();
            SeasonLedger.WriteInto(dto);

            Assert.IsNotNull(dto.events);
            Assert.AreEqual(0, dto.events.Length);
        }

        // --------------------------------------------------------------- the pass
        /// <summary>
        /// Save state joined by <c>or</c>, because buying is irreversible — the join owned
        /// companions and owned land take (invariant 15), on one season rather than a set.
        /// </summary>
        [Test]
        public void ThePassIsRememberedAndJoinsByOr()
        {
            SeasonLedger.LoadFrom(File(("vector_watch", 30, 0, 0)));
            Assert.IsFalse(SeasonLedger.OwnsPass("vector_watch"));
            Assert.IsFalse(SeasonLedger.OwnsPass(null));

            var joined = SeasonLedger.Join(
                new[] { new EventStateDto { id = "a", marks = 30 } },
                new[] { new EventStateDto { id = "a", marks = 12, pass = true } });

            Assert.IsTrue(joined[0].pass, "a device that bought it wins over one that has not heard");

            var back = SeasonLedger.Join(joined, new[] { new EventStateDto { id = "a" } });
            Assert.IsTrue(back[0].pass, "and nothing takes it away again");
        }

        /// <summary>
        /// A row carrying only the pass still writes: it is the one field of the four that
        /// can be true while every count is nought, and a season bought before a single mark
        /// was earned would otherwise write no row at all.
        /// </summary>
        [Test]
        public void ARowCarryingOnlyThePassIsStillWritten()
        {
            SeasonLedger.LoadFrom(File());
            SeasonLedger.Join(null, null);

            var dto = new SaveFileDto();
            SeasonLedger.LoadFrom(new SaveFileDto
            {
                events = new[] { new EventStateDto { id = "a", pass = true } },
            });
            SeasonLedger.WriteInto(dto);

            Assert.AreEqual(1, dto.events.Length);
            Assert.IsTrue(dto.events[0].pass);
        }

        /// <summary>
        /// The paid column cannot be claimed without the entitlement — and the client's copy
        /// is only half of that. The server keeps its own and refuses independently; this
        /// pins the half that stops the page offering a chest it cannot pay.
        /// </summary>
        [Test]
        public void ThePaidColumnIsRefusedWithoutTheEntitlement()
        {
            SeasonLedger.LoadFrom(File(("vector_watch", 30, 0, 0)));

            var season = new GroveEvent("vector_watch", 100, 200,
                                        new[] { new EventMilestone(10, "wood", "silver") },
                                        passGems: 250);

            Assert.IsFalse(SeasonLedger.IsClaimable(season, season.Milestones[0], SeasonTrack.Pass));
            Assert.IsFalse(SeasonLedger.TryClaim(season, season.Milestones[0], SeasonTrack.Pass, out _));
        }

        /// <summary>
        /// <b>And the reading has to agree with the refusal.</b> The hub's event box lights its
        /// border, swaps its caption to <em>collect</em> and pins a count to its corner off
        /// <c>EventProgress.Waiting</c>, while the page it opens refuses every one of them —
        /// so a player on eight marks who had taken the only free rung they had reached was
        /// shown a badge reading one with nothing behind it.
        ///
        /// <para>
        /// The season here is the shipped shape: rungs that pay both columns, the free one
        /// settled and the paid one untouched. Both readings now come off
        /// <see cref="EventLedger.Opens"/>, so there is no arrangement in which they differ.
        /// </para>
        /// </summary>
        [Test]
        public void NothingIsWaitingOnAPaidColumnNobodyBought()
        {
            var season = new GroveEvent("vector_watch", 100, 200,
                                        new[]
                                        {
                                            new EventMilestone(5, "wood", "silver"),
                                            new EventMilestone(10, "wood", "gold"),
                                        },
                                        passGems: 250);

            SeasonLedger.LoadFrom(File(("vector_watch", 8, 5, 0)));

            var unbought = SeasonLedger.ProgressOf(season);
            Assert.AreEqual(1, unbought.Pass.Reached, "the rung was reached by play either way");
            Assert.AreEqual(0, unbought.Pass.Waiting, "and it is not waiting for somebody who cannot take it");
            Assert.AreEqual(0, unbought.Waiting, "so the box wears no badge");
            Assert.IsFalse(unbought.AnyWaiting);

            SeasonLedger.LoadFrom(Held("vector_watch", 8, 5, 0));

            var bought = SeasonLedger.ProgressOf(season);
            Assert.AreEqual(1, bought.Pass.Waiting, "and it is waiting the moment the pass is held");
            Assert.AreEqual(1, bought.Waiting);
        }

        /// <summary>
        /// The consequence that is worse than the badge. <see cref="GroveEvents.Featured"/> is
        /// the oldest season still owing something, so a season owing only a paid column
        /// nobody bought would hold the hub's box — and the page it opens — on a closed season
        /// for ever, with no way to reach the live one (invariant 47m).
        /// </summary>
        [Test]
        public void AClosedSeasonOwingOnlyThePaidColumnDoesNotHoldTheBox()
        {
            var closed = new GroveEvent("vector_watch", 100, 200,
                                        new[] { new EventMilestone(5, null, "silver") },
                                        passGems: 250);

            SeasonLedger.LoadFrom(File(("vector_watch", 30, 0, 0)));
            Assert.IsFalse(SeasonLedger.ProgressOf(closed).AnyWaiting);

            SeasonLedger.LoadFrom(Held("vector_watch", 30, 0, 0));
            Assert.IsTrue(SeasonLedger.ProgressOf(closed).AnyWaiting,
                          "a paid chest that really was bought still holds it");
        }

        /// <summary>
        /// A season with no price sells nothing, whatever a screen asks — the refusal a
        /// free-track-only season has to give.
        /// </summary>
        [Test]
        public void ASeasonWithNoPriceSellsNoPass()
        {
            var free = new GroveEvent("free_only", 100, 200,
                                      new[] { new EventMilestone(10, "wood", null) });

            Assert.IsFalse(free.HasPremium);
            Assert.AreEqual(SeasonLedger.PassBuy.NotSold, SeasonLedger.TryBuyPass(free));
            Assert.AreEqual(SeasonLedger.PassBuy.NotSold, SeasonLedger.TryBuyPass(null));
        }

        // -------------------------------------------------------------- the seed
        /// <summary>
        /// The subject layout is contract with the server's <c>markSubject</c>. A change
        /// here re-rolls every chest on every ladder, so it is pinned as a literal.
        /// </summary>
        [Test]
        public void TheSeedSubjectIsContract()
        {
            Assert.AreEqual("watch_0000:free:200",
                            SeasonLedger.Subject("watch_0000", SeasonTrack.Free, 200));
            Assert.AreEqual("watch_0000:pass:5",
                            SeasonLedger.Subject("watch_0000", SeasonTrack.Pass, 5));
            Assert.AreEqual("mark", SeasonLedger.SeedTag);
        }

        /// <summary>
        /// And so is the claim id, which the server parses back. It has to stay inside the
        /// sixty-four characters the grant document id allows.
        /// </summary>
        [Test]
        public void TheClaimIdIsContractAndFitsTheGrantKey()
        {
            string id = GrantEntry.MarkChestId("watch_0000", SeasonTrack.Pass, 200, "credits");

            Assert.AreEqual("mark:watch_0000:pass:200:credits", id);
            Assert.Less(id.Length, 64);

            // And it still fits at the far end of the calendar, which is the one the season id
            // grows toward: a cycle id is the same length whatever cycle it names, by
            // construction (`SeasonCycle.IndexDigits`), so this is a proof rather than a spot
            // check — but the length ceiling is what `season.ts` parses against and a claim id
            // that overflows it is a claim refused for ever with every file correct.
            Assert.Less(GrantEntry.MarkChestId("watch_9999", SeasonTrack.Pass,
                                               EventRules.MaxGoal, "credits").Length, 64);
        }

        /// <summary>
        /// And so is the pass's own debit id. <c>submitSpends</c> turns exactly this spend
        /// into the entitlement that gates a paid-track chest, so a change here is a change
        /// to who can claim.
        /// </summary>
        [Test]
        public void ThePassSpendIdIsContract()
        {
            Assert.AreEqual("pass:watch_0000", SpendEntry.SeasonPassId("watch_0000"));
            Assert.Less(SpendEntry.SeasonPassId("watch_9999").Length, 64);
        }

        // ------------------------------------------------------------- the ceiling
        /// <summary>
        /// Past the ceiling the join evicts rather than truncating, and what it keeps first is
        /// anything that might still be holding a chest.
        ///
        /// <para>
        /// <b>This was a live bug waiting for a repeating season.</b> The old rule sorted by id
        /// and lopped off the tail — and ordinal order is calendar order, so it kept the
        /// <em>oldest</em> sixty-four rows and deleted the newest. On a calendar that ends, the
        /// sixty-fifth season is a decade away and nobody meets it; on one that does not, it is
        /// the season being played, deleted silently at the moment it opens.
        /// </para>
        /// </summary>
        [Test]
        public void PastTheCeilingASettledSeasonGoesBeforeAnythingStillOwed()
        {
            var rows = new EventStateDto[SeasonLedger.MaxSeasons + 1];

            // Sixty-four settled seasons, oldest first: every rung claimed, so nothing is owed.
            for (int i = 0; i < SeasonLedger.MaxSeasons; i++)
                rows[i] = new EventStateDto { id = Cycle(i), marks = 200, collectedGoal = 200 };

            // And the newest, mid-season, with marks past its claim floor — something waiting.
            string newest = Cycle(SeasonLedger.MaxSeasons);
            rows[SeasonLedger.MaxSeasons] =
                new EventStateDto { id = newest, marks = 40, collectedGoal = 10 };

            var joined = SeasonLedger.Join(rows, Array.Empty<EventStateDto>());

            Assert.AreEqual(SeasonLedger.MaxSeasons, joined.Length, "the ceiling still binds");
            Assert.IsTrue(Has(joined, newest), "the season being played survives the cull");
            Assert.IsFalse(Has(joined, Cycle(0)), "and the oldest settled one is what went");
        }

        /// <summary>
        /// With nothing settled to drop, the oldest goes — there is no arrangement in which a
        /// bounded list keeps everything, and the honest fallback is the one a player is least
        /// likely to be looking at.
        /// </summary>
        [Test]
        public void WithEverythingOwedTheOldestIsStillWhatGoes()
        {
            var rows = new EventStateDto[SeasonLedger.MaxSeasons + 1];
            for (int i = 0; i <= SeasonLedger.MaxSeasons; i++)
                rows[i] = new EventStateDto { id = Cycle(i), marks = 40, collectedGoal = 10 };

            var joined = SeasonLedger.Join(rows, Array.Empty<EventStateDto>());

            Assert.AreEqual(SeasonLedger.MaxSeasons, joined.Length);
            Assert.IsFalse(Has(joined, Cycle(0)));
            Assert.IsTrue(Has(joined, Cycle(SeasonLedger.MaxSeasons)));
        }

        /// <summary>
        /// The rows that survive are still written in id order, whatever order the eviction
        /// considered them in.
        ///
        /// Not tidiness: <c>SaveDelta</c> decides whether to sync by walking these in order, so
        /// rows that came out in eviction order would make an unchanged save read as changed on
        /// every launch — a write and an upload for nothing, for ever.
        /// </summary>
        [Test]
        public void TheSurvivorsAreStillWrittenInIdOrder()
        {
            var rows = new EventStateDto[SeasonLedger.MaxSeasons + 4];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new EventStateDto
                {
                    id = Cycle(i),
                    marks = 200,
                    collectedGoal = i % 2 == 0 ? 200 : 10,      // every other one still owes
                };

            var joined = SeasonLedger.Join(rows, Array.Empty<EventStateDto>());

            for (int i = 1; i < joined.Length; i++)
                Assert.Less(string.CompareOrdinal(joined[i - 1].id, joined[i].id), 0,
                            "rows are written in id order");
        }

        /// <summary>A padded cycle id, so ordinal order is calendar order.</summary>
        static string Cycle(int index) => "watch_" + index.ToString("D4");

        static bool Has(EventStateDto[] rows, string id)
        {
            foreach (var row in rows)
                if (string.Equals(row.id, id, StringComparison.Ordinal)) return true;

            return false;
        }

        static int Field(EventStateDto[] rows, string id, Func<EventStateDto, int> read)
        {
            foreach (var row in rows)
                if (string.Equals(row.id, id, StringComparison.Ordinal)) return read(row);

            return -1;
        }
    }
}
