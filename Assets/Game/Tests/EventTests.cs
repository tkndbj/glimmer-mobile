using GlimmerGrove.Content;
using GlimmerGrove.Events;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The season calendar: what a mark count adds up to, and what the reader refuses.
    ///
    /// <para>
    /// These are the arithmetic half — <see cref="EventLedger"/> is a pure function of its
    /// arguments, so every case here runs offline. What a season does to the <em>save</em>
    /// is <see cref="SeasonLedgerTests"/>, and what it does to a wallet is the shared
    /// vectors.
    /// </para>
    /// <para>
    /// <b>What is deliberately absent is any case about levels.</b> A season used to be
    /// graded on glades first cleared inside its window, which tied it to a named list of
    /// content: the modes those levels belonged to were withdrawn and the season went with
    /// them. It is graded on marks now — see <see cref="GroveEvent"/> — and the tests that
    /// proved the old rule are gone with the rule rather than kept as a record of it.
    /// </para>
    /// </summary>
    public sealed class EventTests
    {
        const long Start = 1_700_000_000L;
        const long End = 1_701_000_000L;

        /// <summary>A three-rung season with both tracks, at 10, 20 and 30 marks.</summary>
        static GroveEvent Season() => new GroveEvent(
            "vector_bloom", Start, End,
            new[]
            {
                new EventMilestone(10, "wood", "silver"),
                new EventMilestone(20, "wood", "gold"),
                new EventMilestone(30, "silver", "royal"),
            },
            passGems: 250);

        // ---------------------------------------------------------------- the ladder
        [Test]
        public void ARungIsReachedWhenTheBloomsReachIt()
        {
            var none = EventLedger.ProgressOf(Season(), 9, 0, 0, passHeld: true);
            Assert.AreEqual(0, none.Rungs);
            Assert.AreEqual(10, none.NextGoal);
            Assert.AreEqual(1, none.ToNext);
            Assert.AreEqual(0, none.LastGoal);
            Assert.IsFalse(none.IsComplete);

            var one = EventLedger.ProgressOf(Season(), 10, 0, 0, passHeld: true);
            Assert.AreEqual(1, one.Rungs);
            Assert.AreEqual(20, one.NextGoal);
            Assert.AreEqual(10, one.ToNext);
            Assert.AreEqual(10, one.LastGoal);
        }

        [Test]
        public void ATopppedLadderHasNoNextRung()
        {
            var all = EventLedger.ProgressOf(Season(), 30, 0, 0, passHeld: true);
            Assert.AreEqual(3, all.Rungs);
            Assert.AreEqual(0, all.NextGoal);
            Assert.AreEqual(0, all.ToNext);
            Assert.IsTrue(all.IsComplete);
            Assert.AreEqual(1f, all.ToNext01, "a finished track draws full, not empty");
        }

        /// <summary>
        /// The bar measures the run between two rungs rather than the whole ladder. Over
        /// forty rungs the second reading barely moves, which is a bar that says nothing.
        /// </summary>
        [Test]
        public void TheBarMeasuresTheRunBetweenTwoRungs()
        {
            Assert.AreEqual(.5f, EventLedger.ProgressOf(Season(), 5, 0, 0, passHeld: true).ToNext01, 1e-5f);
            Assert.AreEqual(.5f, EventLedger.ProgressOf(Season(), 15, 0, 0, passHeld: true).ToNext01, 1e-5f);
            Assert.AreEqual(0f, EventLedger.ProgressOf(Season(), 10, 0, 0, passHeld: true).ToNext01, 1e-5f);
        }

        // ---------------------------------------------------------------- the tracks
        [Test]
        public void EachTrackCountsItsOwnClaims()
        {
            var fresh = EventLedger.ProgressOf(Season(), 30, 0, 0, passHeld: true);
            Assert.AreEqual(3, fresh.Free.Reached);
            Assert.AreEqual(0, fresh.Free.Claimed);
            Assert.AreEqual(3, fresh.Free.Waiting);
            Assert.AreEqual(3, fresh.Pass.Waiting);
            Assert.AreEqual(6, fresh.Waiting, "a badge counts both columns");

            var half = EventLedger.ProgressOf(Season(), 30, 20, 0, passHeld: true);
            Assert.AreEqual(2, half.Free.Claimed);
            Assert.AreEqual(1, half.Free.Waiting);
            Assert.AreEqual(3, half.Pass.Waiting, "claiming the free column takes nothing from the paid one");
        }

        /// <summary>
        /// <b>A track nobody may claim from has nothing waiting on it.</b> The count is what a
        /// badge draws, what lights the hub's box and what decides which season that box
        /// points at, so counting a rung the paid column would refuse to hand over is an
        /// instruction to collect something no screen will give.
        ///
        /// <para>
        /// <see cref="SeasonTrackProgress.Reached"/> is deliberately left alone — the page
        /// draws the paid column whether or not it is held, and how far up it the player has
        /// climbed is a true fact about the ladder. It is only <em>waiting</em>, which means
        /// "a tap would hand this over", that the entitlement decides.
        /// </para>
        /// </summary>
        [Test]
        public void ThePaidColumnCountsNothingWaitingWithoutThePass()
        {
            var without = EventLedger.ProgressOf(Season(), 30, 30, 0, passHeld: false);
            Assert.AreEqual(3, without.Pass.Reached, "reached by play, whoever may take it");
            Assert.AreEqual(0, without.Pass.Claimed);
            Assert.AreEqual(0, without.Pass.Waiting);
            Assert.IsFalse(without.Pass.Open);
            Assert.AreEqual(0, without.Waiting, "the free column is settled, so the badge is gone");

            var with = EventLedger.ProgressOf(Season(), 30, 30, 0, passHeld: true);
            Assert.AreEqual(3, with.Pass.Waiting);
            Assert.AreEqual(3, with.Waiting);
        }

        /// <summary>
        /// The free column is open to everybody, and a pass nobody bought takes nothing off
        /// it. The failure this guards is an over-correction — gating the wrong track would
        /// hide a chest a player has genuinely earned.
        /// </summary>
        [Test]
        public void TheFreeColumnIsOpenWithoutAnything()
        {
            var progress = EventLedger.ProgressOf(Season(), 30, 10, 0, passHeld: false);
            Assert.IsTrue(progress.Free.Open);
            Assert.AreEqual(2, progress.Free.Waiting);
            Assert.AreEqual(2, progress.Waiting);

            Assert.IsTrue(EventLedger.Opens(SeasonTrack.Free, passHeld: false));
            Assert.IsFalse(EventLedger.Opens(SeasonTrack.Pass, passHeld: false));
            Assert.IsTrue(EventLedger.Opens(SeasonTrack.Pass, passHeld: true));
        }

        /// <summary>
        /// The same predicate decides both, which is the whole repair: the count and the
        /// claim used to answer this question separately and one of them said yes.
        /// </summary>
        [Test]
        public void APaidRungIsNotClaimableWithoutThePass()
        {
            var season = Season();
            var rung = season.Milestones[0];        // 10 marks, pays both columns

            Assert.IsFalse(EventLedger.IsClaimable(season, rung, SeasonTrack.Pass, 10, 0,
                                                   passHeld: false));
            Assert.IsTrue(EventLedger.IsClaimable(season, rung, SeasonTrack.Pass, 10, 0,
                                                  passHeld: true));
            Assert.IsTrue(EventLedger.IsClaimable(season, rung, SeasonTrack.Free, 10, 0,
                                                  passHeld: false),
                          "and the free column is untouched by it");
        }

        /// <summary>
        /// The clamp that is the whole of the client-side security property: a floor is
        /// written by the client, and one that outruns the marks behind it is either an
        /// impossible merge or an edited file. Either way the honest reading is the smaller,
        /// so the most a forged floor can do is take early what was coming anyway.
        /// </summary>
        [Test]
        public void AFloorIsNeverTrustedAboveTheBloomsBehindIt()
        {
            var forged = EventLedger.ProgressOf(Season(), 10, int.MaxValue, int.MaxValue, passHeld: true);
            Assert.AreEqual(1, forged.Rungs);
            Assert.AreEqual(1, forged.Free.Claimed);
            Assert.AreEqual(0, forged.Free.Waiting);
            Assert.AreEqual(1, forged.Pass.Claimed, "a clamp, not a refusal — the rung was reached");

            Assert.IsFalse(EventLedger.IsClaimable(Season(), Season().Milestones[0],
                                                   SeasonTrack.Free, 10, int.MaxValue, passHeld: true));
        }

        [Test]
        public void ARungIsClaimableOnceAndOnlyWhenReached()
        {
            var season = Season();
            var rung = season.Milestones[1];        // 20 marks

            Assert.IsFalse(EventLedger.IsClaimable(season, rung, SeasonTrack.Free, 19, 0, passHeld: true),
                           "not reached");
            Assert.IsTrue(EventLedger.IsClaimable(season, rung, SeasonTrack.Free, 20, 10, passHeld: true),
                          "reached, and the floor is below it");
            Assert.IsFalse(EventLedger.IsClaimable(season, rung, SeasonTrack.Free, 20, 20, passHeld: true),
                           "already taken");
        }

        [Test]
        public void ASeasonWithNoPassPaysNothingOnThePaidTrack()
        {
            var free = new GroveEvent("free_only", Start, End,
                                      new[] { new EventMilestone(10, "wood", null) });

            Assert.IsTrue(free.Milestones[0].Pays(SeasonTrack.Free));
            Assert.IsFalse(free.Milestones[0].Pays(SeasonTrack.Pass));
            Assert.AreEqual(0, EventLedger.ProgressOf(free, 10, 0, 0, passHeld: true).Pass.Reached);
        }

        [Test]
        public void ARungIsFoundByItsGoalRatherThanItsPosition()
        {
            var season = Season();

            Assert.IsTrue(season.TryRung(20, out var rung));
            Assert.AreEqual("gold", rung.PassTier);
            Assert.IsFalse(season.TryRung(25, out _), "no rung asks for exactly that");
        }

        // ---------------------------------------------------------------- the reader
        static ManifestEventDto Entry(params (int goal, string tier, string premium)[] rungs)
        {
            var milestones = new ManifestEventMilestoneDto[rungs.Length];
            for (int i = 0; i < rungs.Length; i++)
                milestones[i] = new ManifestEventMilestoneDto
                {
                    goal = rungs[i].goal, tier = rungs[i].tier, premiumTier = rungs[i].premium,
                };

            return new ManifestEventDto
            {
                id = "a_season", startUnix = Start, endUnix = End,
                passGems = 250, milestones = milestones,
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
        public void AWellFormedSeasonIsRead()
        {
            Assert.IsTrue(Reads(Entry((10, "wood", "silver"), (20, "silver", "gold")), out var builder));

            var index = builder.Build();
            Assert.AreEqual(1, index.Events.Count);
            Assert.AreEqual(2, index.Events[0].Milestones.Count);
            Assert.AreEqual(20, index.Events[0].FinalGoal);
        }

        /// <summary>
        /// Refused whole rather than sorted. Reordering a ladder would pay chests nobody
        /// authored, and eighty of them is not a thing anybody eyeballs afterwards.
        /// </summary>
        [Test]
        public void AnOutOfOrderLadderIsRefused()
        {
            Assert.IsFalse(Reads(Entry((20, "wood", "silver"), (10, "wood", "silver")), out _));
        }

        [Test]
        public void ASeasonWithNoRungsIsRefused()
        {
            Assert.IsFalse(Reads(Entry(), out _));
        }

        /// <summary>
        /// A paid column with a hole in it is a player looking at what they bought and
        /// seeing nothing — so a season that sells a pass has to pay on every rung of it.
        /// </summary>
        [Test]
        public void ASeasonSellingAPassMustPayOnEveryRungOfIt()
        {
            Assert.IsFalse(Reads(Entry((10, "wood", "silver"), (20, "wood", null)), out _));
        }

        /// <summary>
        /// And the other way: a rung that pays a paid chest on a season nobody can buy into
        /// is a reward nothing could ever hand over.
        /// </summary>
        [Test]
        public void APaidRungOnASeasonWithNoPassIsRefused()
        {
            var entry = Entry((10, "wood", "silver"));
            entry.passGems = 0;

            Assert.IsFalse(Reads(entry, out _));
        }

        [Test]
        public void ARungWithNoFreeTierIsRefused()
        {
            Assert.IsFalse(Reads(Entry((10, null, "silver")), out _));
            Assert.IsFalse(Reads(Entry((10, "Wood Chest", "silver")), out _),
                           "a tier id names art and copy, so it is lower case and underscores");
        }

        /// <summary>
        /// An icon is carried through untouched, and an absent one is empty rather than null.
        ///
        /// Domain deliberately has no list of the marks that exist — that is a question about
        /// what has been drawn, and it is answered in Presentation by <c>SeasonCrest</c>. So the
        /// only thing checkable here is that the string survives the trip.
        /// </summary>
        [Test]
        public void ASeasonCarriesTheMarkItAsksFor()
        {
            var entry = Entry((10, "wood", "silver"));
            entry.icon = "watch";

            Assert.IsTrue(Reads(entry, out var builder));
            Assert.AreEqual("watch", builder.Build().Events[0].Icon);
        }

        [Test]
        public void ASeasonWithNoIconAsksForNothingRatherThanNull()
        {
            Assert.IsTrue(Reads(Entry((10, "wood", "silver")), out var builder));
            Assert.AreEqual(string.Empty, builder.Build().Events[0].Icon);
        }

        /// <summary>
        /// Refused for being unusable as a name, and for nothing else.
        ///
        /// A build that has never heard of the mark a manifest names must still read that
        /// manifest: content ships ahead of clients, so an unknown mark has to be a fallback
        /// at draw time rather than a rejected season.
        /// </summary>
        [Test]
        public void AnUnusableIconNameIsRefusedButAnUnknownOneIsNot()
        {
            var bad = Entry((10, "wood", "silver"));
            bad.icon = "Ui/ic_stars.png";
            Assert.IsFalse(Reads(bad, out _), "a name that is not a clean id");

            var unknown = Entry((10, "wood", "silver"));
            unknown.icon = "a_mark_this_build_has_never_drawn";
            Assert.IsTrue(Reads(unknown, out _), "a clean id this build does not recognise");
        }

        [Test]
        public void AWindowThatEndsBeforeItStartsIsRefused()
        {
            var entry = Entry((10, "wood", "silver"));
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
            var entry = Entry((10, "wood", "silver"));
            entry.endUnix = entry.startUnix + (EventRules.MaxWindowDays + 1) * EventRules.SecondsPerDay;

            Assert.IsFalse(Reads(entry, out _));
        }

        /// <summary>
        /// The v28 bargain, stated as a test: a season names no content, so no withdrawal
        /// can take one down with it. The old reader dropped a season whose glades had gone.
        /// </summary>
        [Test]
        public void ASeasonSurvivesAnEmptyCatalog()
        {
            var builder = new CatalogIndexBuilder();

            Assert.IsTrue(builder.AddEvent(Entry((10, "wood", "silver"))));
            Assert.AreEqual(1, builder.Build().Events.Count,
                            "a season is a window and a ladder; it needs no chapter to run over");
        }

        [Test]
        public void ADisabledSeasonIsSkippedWithoutComplaint()
        {
            var entry = Entry((10, "wood", "silver"));
            entry.disabled = true;

            Assert.IsFalse(Reads(entry, out var builder));
            Assert.IsFalse(builder.HasProblems, "pulling a season is a decision, not a mistake");
        }

        // ---------------------------------------------------------------- the window
        [Test]
        public void LivenessIsDecidedByTheWindowAndNothingElse()
        {
            var season = Season();

            Assert.IsTrue(season.StartsAfter(Start - 1));
            Assert.IsTrue(season.IsLiveAt(Start));
            Assert.IsTrue(season.IsLiveAt(End - 1));
            Assert.IsFalse(season.IsLiveAt(End));
            Assert.IsTrue(season.HasEndedAt(End));
            Assert.AreEqual(0, season.SecondsLeftAt(End + 100));
        }

        [Test]
        public void ATrackIdIsContractAndRoundTrips()
        {
            foreach (var track in SeasonTracks.All)
                Assert.AreEqual(track, SeasonTracks.Parse(SeasonTracks.Id(track)));

            Assert.AreEqual("free", SeasonTracks.Id(SeasonTrack.Free));
            Assert.AreEqual("pass", SeasonTracks.Id(SeasonTrack.Pass));
            Assert.IsNull(SeasonTracks.Parse("premium"), "a name this build does not know is not a guess");
        }
    }
}
