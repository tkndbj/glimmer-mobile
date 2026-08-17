using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Events;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The event calendar: what advances a track, what a track pays, and what the reader
    /// refuses.
    ///
    /// <para>
    /// The reward here is <em>derived</em>, which is what makes an event cost no save
    /// schema, no merge rule and no claim — see <see cref="EventLedger"/>. It also makes
    /// the arithmetic load-bearing in a way a granted reward's would not be: the total is
    /// recomputed from scratch on every launch and by the server on every sync, so a rule
    /// that is wrong is wrong retroactively for everybody, and the wallet's earned floor
    /// means an overpayment can never be taken back.
    /// </para>
    /// <para>
    /// The numbers match <c>firebase/shared/reward-vectors.json</c>. Those are also run
    /// end to end by <c>RewardVectorTests</c>, which needs the Editor; these run anywhere.
    /// </para>
    /// </summary>
    public sealed class EventTests
    {
        const long Start = 1_700_000_000L;
        const long End = 1_701_000_000L;
        const long Inside = Start + 5_000L;
        const long Outside = Start - 5_000L;

        static readonly string[] EventLevels = { "plain_one", "plain_two", "generous_one" };

        static GroveEvent Bloom() => new GroveEvent(
            "vector_bloom", Start, End,
            new[] { LevelId.Parse("plain_one"), LevelId.Parse("plain_two"), LevelId.Parse("generous_one") },
            new[] { new EventMilestone(1, 50), new EventMilestone(3, 200) });

        static Dictionary<LevelId, LevelRecord> Records(params (string id, int stars, long at)[] entries)
        {
            var map = new Dictionary<LevelId, LevelRecord>();
            foreach (var (id, stars, at) in entries)
            {
                var levelId = LevelId.Parse(id);
                map[levelId] = new LevelRecord(levelId, stars, bestMoves: 10, clears: 1,
                                               firstClearedUnix: at, lastPlayedUnix: at);
            }
            return map;
        }

        /// <summary>
        /// The track as seen by a player who has collected everything they have reached.
        ///
        /// <c>ProgressOf</c> clamps the floor to the glades actually finished, so
        /// <see cref="int.MaxValue"/> means "nothing is still waiting" rather than a number
        /// anything trusts — which is the reading these cases want: they are about what a
        /// track <em>pays</em>, not about the hand-collection introduced in save schema v11.
        /// The waiting half is covered by <see cref="ATrackPaysNothingUntilItIsCollected"/>.
        /// </summary>
        static EventProgress Collected(GroveEvent groveEvent,
                                       Dictionary<LevelId, LevelRecord> records)
            => EventLedger.ProgressOf(groveEvent, records, int.MaxValue);

        // ------------------------------------------------------------ the count
        [Test]
        public void AClearInsideTheWindowCounts()
        {
            Assert.AreEqual(1, EventLedger.Finished(Bloom(), Records(("plain_one", 3, Inside))));
        }

        /// <summary>
        /// The rule that makes an event an event. Without it, a player finishes the same
        /// glades on the last day of every event forever and the calendar becomes a chore
        /// list rather than a reason to play something new.
        /// </summary>
        [Test]
        public void AClearOutsideTheWindowCountsForNothing()
        {
            Assert.AreEqual(0, EventLedger.Finished(Bloom(), Records(("plain_one", 3, Outside))));
            Assert.AreEqual(0, EventLedger.Finished(Bloom(), Records(("plain_one", 3, End))),
                            "the end of the window is exclusive");
            Assert.AreEqual(1, EventLedger.Finished(Bloom(), Records(("plain_one", 3, Start))),
                            "the start of it is not");
        }

        [Test]
        public void AGladeThatWasPlayedButNeverClearedCountsForNothing()
        {
            Assert.AreEqual(0, EventLedger.Finished(Bloom(), Records(("plain_one", 0, Inside))));
        }

        [Test]
        public void AGladeOutsideTheEventCountsForNothing()
        {
            Assert.AreEqual(0, EventLedger.Finished(Bloom(), Records(("free_one", 3, Inside))));
        }

        // ----------------------------------------------------------- the track
        [Test]
        public void TheTrackPaysEveryMilestoneThatHasBeenPassed()
        {
            var one = Collected(Bloom(), Records(("plain_one", 3, Inside)));
            Assert.AreEqual(1, one.Finished);
            Assert.AreEqual(1, one.Milestones);
            Assert.AreEqual(50, one.Credits);
            Assert.AreEqual(3, one.NextGoal);
            Assert.AreEqual(2, one.ToNext);
            Assert.IsFalse(one.IsComplete);

            var all = Collected(Bloom(), Records(("plain_one", 2, Inside),
                                                 ("plain_two", 1, Inside),
                                                 ("generous_one", 3, Inside)));
            Assert.AreEqual(3, all.Finished);
            Assert.AreEqual(2, all.Milestones);
            Assert.AreEqual(250, all.Credits);
            Assert.AreEqual(0, all.NextGoal);
            Assert.IsTrue(all.IsComplete);
        }

        /// <summary>
        /// Stars do not matter to an event, only clears. That is deliberate: an event is a
        /// reason to visit glades, and gating it on three stars would turn a fortnight of
        /// invitation into a fortnight of grinding the hardest glade in the set.
        /// </summary>
        [Test]
        public void OneStarAdvancesATrackAsMuchAsThree()
        {
            Assert.AreEqual(Collected(Bloom(), Records(("plain_one", 1, Inside))).Credits,
                            Collected(Bloom(), Records(("plain_one", 3, Inside))).Credits);
        }

        /// <summary>
        /// A closed event still pays what it paid. The derived total would otherwise fall
        /// the moment a window shut, and a balance that drops for no reason the player can
        /// see is the worst thing an economy can do in front of somebody.
        /// </summary>
        [Test]
        public void AClosedEventKeepsPaying()
        {
            var finished = Records(("plain_one", 3, Inside), ("plain_two", 3, Inside),
                                   ("generous_one", 3, Inside));

            // The progress is a pure function of the records and the window; nothing about
            // it consults the clock, which is exactly why an ended event cannot un-pay.
            Assert.AreEqual(250, Collected(Bloom(), finished).Credits);
        }

        /// <summary>
        /// The v11 half: a milestone the player has reached but not tapped is
        /// <em>waiting</em>, and pays nothing until it is. Without this the floor could be
        /// ignored entirely and every case above would still be green.
        /// </summary>
        [Test]
        public void ATrackPaysNothingUntilItIsCollected()
        {
            var records = Records(("plain_one", 3, Inside), ("plain_two", 3, Inside),
                                  ("generous_one", 3, Inside));

            var uncollected = EventLedger.ProgressOf(Bloom(), records, 0);
            Assert.AreEqual(0, uncollected.Credits, "nothing has been handed over yet");
            Assert.AreEqual(2, uncollected.Waiting);
            Assert.AreEqual(250, uncollected.WaitingCredits);

            // Collecting the first rung moves it into the balance and leaves the second.
            var half = EventLedger.ProgressOf(Bloom(), records, 1);
            Assert.AreEqual(50, half.Credits);
            Assert.AreEqual(1, half.Waiting);
            Assert.AreEqual(200, half.WaitingCredits);
        }

        // ------------------------------------------------------------ the reader
        static ManifestEventDto Entry(params (int goal, int credits)[] milestones)
        {
            var rungs = new ManifestEventMilestoneDto[milestones.Length];
            for (int i = 0; i < milestones.Length; i++)
                rungs[i] = new ManifestEventMilestoneDto
                {
                    goal = milestones[i].goal, credits = milestones[i].credits,
                };

            return new ManifestEventDto
            {
                id = "bloom", startUnix = Start, endUnix = End,
                levels = EventLevels, milestones = rungs,
            };
        }

        static bool Reads(ManifestEventDto entry, out CatalogIndexBuilder builder)
        {
            builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c_plain", order = 10, version = 1,
                levels = new[] { "plain_one", "plain_two", "generous_one" },
            }, 1);

            return builder.AddEvent(entry);
        }

        [Test]
        public void AWellFormedEventIsRead()
        {
            Assert.IsTrue(Reads(Entry((1, 50), (3, 200)), out var builder));

            var index = builder.Build();
            Assert.AreEqual(1, index.Events.Count);
            Assert.AreEqual(3, index.Events[0].Levels.Count);
            Assert.AreEqual(250, index.Events[0].TotalCredits);
        }

        /// <summary>
        /// Refused whole rather than sorted. Reordering a track would pay rewards nobody
        /// authored, and because the reward is derived and floored, every player who
        /// finished it would keep the wrong figure permanently.
        /// </summary>
        [Test]
        public void AnOutOfOrderTrackIsRefused()
        {
            Assert.IsFalse(Reads(Entry((3, 200), (1, 50)), out _));
        }

        [Test]
        public void AMilestoneBeyondTheGladesItNamesIsRefused()
        {
            Assert.IsFalse(Reads(Entry((1, 50), (9, 200)), out _));
        }

        [Test]
        public void AnEventWithNoMilestonesIsRefused()
        {
            Assert.IsFalse(Reads(Entry(), out _));
        }

        /// <summary>
        /// An icon is carried through untouched, and an absent one is empty rather than null.
        ///
        /// Domain deliberately has no list of the marks that exist — that is a question about
        /// what has been drawn, and it is answered in Presentation by <c>EventMark</c>. So the
        /// only thing checkable here is that the string survives the trip.
        /// </summary>
        [Test]
        public void AnEventCarriesTheMarkItAsksFor()
        {
            var entry = Entry((1, 50));
            entry.icon = "bloom";

            Assert.IsTrue(Reads(entry, out var builder));
            Assert.AreEqual("bloom", builder.Build().Events[0].Icon);
        }

        [Test]
        public void AnEventWithNoIconAsksForNothingRatherThanNull()
        {
            Assert.IsTrue(Reads(Entry((1, 50)), out var builder));
            Assert.AreEqual(string.Empty, builder.Build().Events[0].Icon);
        }

        /// <summary>
        /// Refused for being unusable as a name, and for nothing else.
        ///
        /// A build that has never heard of the mark a manifest names must still read that
        /// manifest: content ships ahead of clients, so an unknown mark has to be a fallback
        /// at draw time rather than a rejected event. Refusing it here would pull the whole
        /// event — its window, its glades and its track — over a picture.
        /// </summary>
        [Test]
        public void AnUnusableIconNameIsRefusedButAnUnknownOneIsNot()
        {
            var bad = Entry((1, 50));
            bad.icon = "Ui/ic_stars.png";
            Assert.IsFalse(Reads(bad, out _), "a name that is not a clean id");

            var unknown = Entry((1, 50));
            unknown.icon = "a_mark_this_build_has_never_drawn";
            Assert.IsTrue(Reads(unknown, out _), "a clean id this build does not recognise");
        }

        [Test]
        public void AWindowThatEndsBeforeItStartsIsRefused()
        {
            var entry = Entry((1, 50));
            entry.endUnix = entry.startUnix - 1;

            Assert.IsFalse(Reads(entry, out _));
        }

        /// <summary>
        /// A "limited time" that outlives interest in it is content with a countdown
        /// attached — and a window authored with a typo'd year is exactly that.
        /// </summary>
        [Test]
        public void AnAbsurdlyLongWindowIsRefused()
        {
            var entry = Entry((1, 50));
            entry.endUnix = entry.startUnix + (EventRules.MaxWindowDays + 1) * EventRules.SecondsPerDay;

            Assert.IsFalse(Reads(entry, out _));
        }

        /// <summary>
        /// Checked at build time rather than on read, because a manifest may legitimately
        /// list an event before the chapter it runs over. Running it over the glades that
        /// do exist would silently lower every goal on the track.
        /// </summary>
        [Test]
        public void AnEventNamingAGladeNoChapterHoldsIsDropped()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c_plain", order = 10, version = 1, levels = new[] { "plain_one" },
            }, 1);

            Assert.IsTrue(builder.AddEvent(Entry((1, 50))), "the entry itself is well formed");
            Assert.AreEqual(0, builder.Build().Events.Count, "but two of its glades do not exist");
        }

        [Test]
        public void ADisabledEventIsSkippedWithoutComplaint()
        {
            var entry = Entry((1, 50));
            entry.disabled = true;

            Assert.IsFalse(Reads(entry, out var builder));
            Assert.IsFalse(builder.HasProblems, "pulling an event is a decision, not a mistake");
        }

        // ----------------------------------------------------------- the window
        [Test]
        public void LivenessIsDecidedByTheWindowAndNothingElse()
        {
            var bloom = Bloom();

            Assert.IsTrue(bloom.StartsAfter(Start - 1));
            Assert.IsTrue(bloom.IsLiveAt(Start));
            Assert.IsTrue(bloom.IsLiveAt(End - 1));
            Assert.IsFalse(bloom.IsLiveAt(End));
            Assert.IsTrue(bloom.HasEndedAt(End));
            Assert.AreEqual(0, bloom.SecondsLeftAt(End + 100));
        }

        // -------------------------------------------------------------- totals
        /// <summary>
        /// The event track is inside derived earnings rather than beside them, which is the
        /// decision that makes the whole feature free of save state. This proves the wiring
        /// as well as the arithmetic: the same records with and without a calendar differ
        /// by exactly the track.
        /// </summary>
        [Test]
        public void EventCreditsLandInTheDerivedTotal()
        {
            var map = new FixedChapterMap();
            foreach (var id in EventLevels) map.Add(id, "c_plain");

            var records = new List<LevelRecord>();
            foreach (var id in EventLevels)
            {
                var levelId = LevelId.Parse(id);
                records.Add(new LevelRecord(levelId, 3, 10, 1, Inside, Inside));
            }

            var table = ProgressionTable.Default;

            long without = ProgressionLedger.Compute(records, map, table).EarnedCredits;

            // The floors have to be handed over as well as the calendar: since v11 a track
            // pays what has been *collected*, so a caller with no floors gets nothing — the
            // deliberate default, because understating is recoverable and a giveaway is not.
            var collected = new Dictionary<string, int> { { "vector_bloom", 3 } };

            long uncollected = ProgressionLedger.Compute(records, map, table, null,
                                                         new[] { Bloom() }).EarnedCredits;
            Assert.AreEqual(without, uncollected,
                            "an uncollected track is worth nothing to the balance");

            long with = ProgressionLedger.Compute(records, map, table, null,
                                                  new[] { Bloom() }, collected).EarnedCredits;

            Assert.AreEqual(250, with - without,
                            "the whole track is 50 + 200, and nothing else may have moved");
        }
    }
}
