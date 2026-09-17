using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The five casts a siege can draw — four a chapter picks from and the Infinite lane's
    /// medley — and the ways a cast can silently stop being drawable.
    ///
    /// <para>
    /// <b>This is <c>SiegeGroundTests</c>'s question asked of the raiders.</b> A rung's ground is
    /// named twice and a test holds the two names together; a cast is worse, because it is twelve
    /// bodies rather than one and there are five of them. What that fixture records applies here
    /// unchanged: if what a chapter <em>loads</em> and what the board <em>draws</em> come apart, an
    /// <c>Image</c> with a null sprite is a <b>white rectangle</b> rather than a blank (invariant
    /// 7b) — over every raider on the hill, on one chapter, with every other gate green. The
    /// address would be real, registered, audited and even loaded, by a different chapter.
    /// </para>
    /// <para>
    /// <b>The names are one copy now</b> (<see cref="SiegeMode.CastAddress"/> indexes the very
    /// array <see cref="SiegeMode.ArtFor"/> preloads), so the drift this guards against has been
    /// made unrepresentable rather than merely checked. What is left to check is the part a shape
    /// cannot enforce: that the ordering the index rests on is the ordering the arrays are written
    /// in, that every chapter this game ships resolves to a cast whose reels it also loads, and
    /// that no two casts share a reel.
    /// </para>
    /// <para>
    /// It asks about <b>addresses</b> and never sprites, which is <c>SkinsTests</c>' bargain and
    /// for its reason: nothing is loaded in an offline run, so a sprite lookup answers null
    /// whatever the table says, and a check that cannot fail is not a check. That the PNGs behind
    /// these addresses exist is proved by <c>Tools/make_siege_art.py --check</c>, which holds every
    /// one of them byte-for-byte to what the tool cuts.
    /// </para>
    /// </summary>
    public sealed class SiegeCastTests
    {
        static readonly int[] Sets =
        {
            SiegeMode.Insects, SiegeMode.Medley, SiegeMode.Brood, SiegeMode.Bones,
            SiegeMode.Rabble, SiegeMode.Wild,
        };

        /// <summary>
        /// The four a <b>chapter</b> can draw. The medley is not one of them: it is dealt out of
        /// these and is the Infinite lane's alone.
        /// </summary>
        static readonly int[] Chapters =
        {
            SiegeMode.Insects, SiegeMode.Brood, SiegeMode.Bones, SiegeMode.Rabble,
            SiegeMode.Wild,
        };

        static readonly SiegeKind[] Bodies =
        {
            SiegeKind.Creeper, SiegeKind.Brute, SiegeKind.Bulwark,
        };

        /// <summary>Every cast holds a body for every kind in every colour.</summary>
        [Test]
        public void EveryCastHoldsABodyForEveryKindAndColour()
        {
            Assert.AreEqual(Sets.Length, SiegeMode.CastSets,
                            "a cast has been added or removed and this fixture was not told");

            foreach (int set in Sets)
            {
                var art = SiegeMode.CastArt(set);

                Assert.IsNotNull(art, $"cast {set} has no reels at all");
                Assert.AreEqual(SiegeMode.CastBodies, art.Count,
                                $"cast {set} holds {art.Count} reels rather than "
                                + $"{SiegeMode.CastBodies}, so the index CastAddress rests on is "
                                + "off");

                var seen = new HashSet<string>();

                foreach (var kind in Bodies)
                    for (int colour = 0; colour < Wards.WardLine.Colours.Length; colour++)
                    {
                        string address = SiegeMode.CastAddress(set, kind, colour);

                        Assert.IsNotEmpty(address,
                                          $"cast {set} names nothing for a {kind} in colour "
                                          + $"{Wards.WardLine.Colours[colour]}");
                        seen.Add(address);
                    }

                Assert.AreEqual(SiegeMode.CastBodies, seen.Count,
                                $"cast {set} draws {seen.Count} distinct bodies for "
                                + $"{SiegeMode.CastBodies} slots, so two kinds or colours share "
                                + "one reel and the array is not in the order CastAddress assumes");
            }
        }

        /// <summary>
        /// **What a cast draws is what that cast loads.** The comparison this fixture is for.
        ///
        /// Every address <see cref="SiegeMode.CastAddress"/> can answer with has to be one of the
        /// reels in the same cast's own array — which is true by construction today and is checked
        /// anyway, because the construction is an index and an index is exactly what a re-ordered
        /// array breaks.
        /// </summary>
        [Test]
        public void EveryBodyACastDrawsIsAReelThatCastLoads()
        {
            foreach (int set in Sets)
            {
                var loaded = new HashSet<string>();
                foreach (var request in SiegeMode.CastArt(set)) loaded.Add(request.Address);

                foreach (var kind in Bodies)
                    for (int colour = 0; colour < Wards.WardLine.Colours.Length; colour++)
                    {
                        string address = SiegeMode.CastAddress(set, kind, colour);

                        Assert.That(loaded.Contains(address), Is.True,
                                    $"cast {set} draws {address} for a {kind} and never loads it");
                    }
            }
        }

        /// <summary>
        /// A bomber has no body of its own and draws the creeper's, which is what it has always
        /// done: it is a creeper carrying something.
        ///
        /// <b>Checked rather than left to a fall-through</b>, because the fall-through is a
        /// <c>default</c> arm and invariant 44e's lesson is that a <c>default</c> which is a real
        /// answer hides the case nobody is looking at.
        /// </summary>
        [Test]
        public void ABomberDrawsTheCreepersBody()
        {
            foreach (int set in Sets)
                for (int colour = 0; colour < Wards.WardLine.Colours.Length; colour++)
                    Assert.AreEqual(SiegeMode.CastAddress(set, SiegeKind.Creeper, colour),
                                    SiegeMode.CastAddress(set, SiegeKind.Bomber, colour),
                                    $"cast {set} draws a bomber as something other than a creeper");
        }

        /// <summary>
        /// No two <b>chapter</b> casts share a reel.
        ///
        /// <b>The property rather than a table</b> (invariant 44e): two casts that overlap are two
        /// chapters that look the same on the bodies they share, and the whole reason a second cast
        /// exists is that they should not.
        ///
        /// <b>The medley is deliberately excluded and is checked the other way round below</b>
        /// (<see cref="TheMedleyIsDealtOutOfTheChapterCasts"/>): the Infinite lane draws every
        /// body the player has already fought, so every one of its reels is some chapter's.
        /// </summary>
        [Test]
        public void NoTwoChapterCastsShareABody()
        {
            var owner = new Dictionary<string, int>();

            foreach (int set in Chapters)
                foreach (var request in SiegeMode.CastArt(set))
                {
                    if (owner.TryGetValue(request.Address, out int already))
                        Assert.Fail($"{request.Address} is in cast {already} and cast {set}");

                    owner[request.Address] = set;
                }
        }

        /// <summary>
        /// **The medley is dealt out of the four chapter casts and owns no reel of its own.**
        ///
        /// <para>
        /// That is the whole of what makes the Infinite lane free: it loads twelve reels as every
        /// chapter does and all twelve are already on disk for a chapter, where the cast it
        /// replaced was twenty-four reels nothing else drew. A row here that named art of its own
        /// would be that cost coming back with nothing announcing it.
        /// </para>
        /// <para>
        /// <b>And every slot is dealt from the cast its own square names</b>, which is what stops
        /// the walk and the swing coming from different families — a body that loaded one
        /// family's walk and another's swing would change into a different animal at the ward
        /// line.
        /// </para>
        /// </summary>
        [Test]
        public void TheMedleyIsDealtOutOfTheChapterCasts()
        {
            var chapters = new HashSet<string>();

            foreach (int set in Chapters)
            {
                foreach (var request in SiegeMode.CastArt(set)) chapters.Add(request.Address);

                var swings = SiegeMode.CastSwingArt(set);
                if (swings == null) continue;

                foreach (var request in swings) chapters.Add(request.Address);
            }

            foreach (var request in SiegeMode.CastArt(SiegeMode.Medley))
                Assert.That(chapters.Contains(request.Address), Is.True,
                            $"the medley draws {request.Address}, which no chapter cast owns");

            foreach (var request in SiegeMode.CastSwingArt(SiegeMode.Medley))
                Assert.That(string.IsNullOrEmpty(request.Address)
                            || chapters.Contains(request.Address), Is.True,
                            $"the medley swings {request.Address}, which no chapter cast owns");
        }

        /// <summary>
        /// **Every family the main ladder sends turns up on the Infinite hill**, which is the one
        /// thing the arithmetic above cannot say and the whole reason this lane draws a medley: a
        /// square that dealt three slots to one cast and none to another would load, index, draw
        /// and read as an ordinary chapter's wave.
        /// </summary>
        [Test]
        public void TheMedleyDrawsFromEveryChapterCast()
        {
            var drawn = new HashSet<string>();
            foreach (var request in SiegeMode.CastArt(SiegeMode.Medley)) drawn.Add(request.Address);

            foreach (int set in Chapters)
            {
                bool any = false;
                foreach (var request in SiegeMode.CastArt(set))
                    any |= drawn.Contains(request.Address);

                Assert.That(any, Is.True, $"the medley sends no body from cast {set}");
            }
        }

        /// <summary>
        /// **A chapter's cast is arithmetic on its ordinal**, so a chapter published next year
        /// costs no cast at all — invariant 7c, which is the same rule the map and the skies follow.
        /// </summary>
        [Test]
        public void AChapterPastTheLastCastWrapsRatherThanDrawingNothing()
        {
            // **Written against the number of main casts rather than against three**, because the
            // first cut said "the third chapter wraps onto the first" and stopped being true the
            // day a third cast was cut - green, and about arithmetic the mode no longer does.
            int casts = SiegeMode.MainCastCount;

            Assert.Greater(casts, 0, "the main ladder draws from no casts at all");

            for (int ordinal = 0; ordinal < casts; ordinal++)
                Assert.AreEqual(SiegeMode.CastFor(GameTrack.Main, ordinal),
                                SiegeMode.CastFor(GameTrack.Main, ordinal + casts),
                                $"chapter {ordinal + casts + 1} does not wrap back onto the cast "
                                + $"chapter {ordinal + 1} draws");
        }

        /// <summary>
        /// The four chapters this game ships draw <b>different</b> casts, and the Infinite lane
        /// draws the medley.
        ///
        /// <b>The fact, not the arithmetic</b> — the arithmetic is checked above, and this is what a
        /// reader actually wants to know. It is also what catches a re-ordered
        /// <see cref="SiegeMode.MainCasts"/>, which would leave every test above green.
        /// </summary>
        [Test]
        public void TheShippedChaptersDrawTheCastsTheyWereBuiltFor()
        {
            Assert.AreEqual(SiegeMode.Insects, SiegeMode.CastFor(GameTrack.Main, 0),
                            "Thornwatch does not draw the insects");

            Assert.AreEqual(SiegeMode.Brood, SiegeMode.CastFor(GameTrack.Main, 1),
                            "Broodmarch does not draw the brood");

            Assert.AreEqual(SiegeMode.Bones, SiegeMode.CastFor(GameTrack.Main, 2),
                            "Barrowfell does not draw the bone cast");

            Assert.AreEqual(SiegeMode.Rabble, SiegeMode.CastFor(GameTrack.Main, 3),
                            "Ashenhold does not draw the rabble");
            Assert.AreEqual(SiegeMode.Wild, SiegeMode.CastFor(GameTrack.Main, 4),
                            "Thundercrag does not draw the wild");

            Assert.AreEqual(SiegeMode.Medley, SiegeMode.CastFor(GameTrack.Infinite, 0),
                            "the Infinite lane does not draw the medley");
        }

        /// <summary>
        /// A chapter this catalog has never heard of answers with a cast that is certainly on disk.
        ///
        /// <c>CatalogIndex.ChapterOrderOf</c> reports -1 for one, and the honest answer to "which
        /// cast" is never "none" — an empty answer is twelve white rectangles.
        /// </summary>
        [Test]
        public void AnUnknownChapterStillDrawsACast()
        {
            Assert.AreEqual(SiegeMode.Insects, SiegeMode.CastFor(GameTrack.Main, -1));
            Assert.IsNotEmpty(SiegeMode.CastAddress(SiegeMode.CastFor(GameTrack.Main, -1),
                                                    SiegeKind.Creeper, 0));
        }

        /// <summary>
        /// **A cast that swings names one reel per body, in the same order it names its walks**
        /// — or names none at all, or, in exactly one case, names nothing for a body whose pack
        /// drew no attack.
        ///
        /// <para>
        /// <b>What must never happen is an address that is neither empty nor on disk</b>: that
        /// loads as nothing, and an <c>Image</c> with a null sprite is a white rectangle over a
        /// raider standing at the ward line (invariant 7b), on the rungs where the line is
        /// already being lost. So the check is that every non-empty entry is one this cast also
        /// loads, that the table is the full twelve when there is one at all, and that no two
        /// slots share a reel.
        /// </para>
        /// <para>
        /// <b>The gap is the medley's</b> and it is honest rather than convenient: two of the
        /// four families it is dealt from have no attack animation in their packs, so six of its
        /// twelve bodies keep walking at the line — which is exactly what those two chapters do
        /// today. <c>CastSwing</c> answering empty is already the "this does not swing" reply for
        /// a whole cast; this is the same reply one body at a time.
        /// </para>
        /// </summary>
        [Test]
        public void ACastEitherSwingsWithEveryBodyOrWithNone()
        {
            foreach (int set in Sets)
            {
                var swings = SiegeMode.CastSwingArt(set);

                if (swings == null)
                {
                    foreach (var kind in Bodies)
                        for (int colour = 0; colour < Wards.WardLine.Colours.Length; colour++)
                            Assert.IsEmpty(SiegeMode.CastSwing(set, kind, colour),
                                           $"cast {set} has no swing reels and still names one "
                                           + $"for a {kind}");
                    continue;
                }

                Assert.AreEqual(SiegeMode.CastBodies, swings.Count,
                                $"cast {set} holds {swings.Count} swing reels rather than "
                                + $"{SiegeMode.CastBodies}, so the index CastSwing rests on is off");

                var loaded = new HashSet<string>();
                foreach (var request in swings)
                    if (!string.IsNullOrEmpty(request.Address)) loaded.Add(request.Address);

                var seen = new HashSet<string>();

                foreach (var kind in Bodies)
                    for (int colour = 0; colour < Wards.WardLine.Colours.Length; colour++)
                    {
                        string address = SiegeMode.CastSwing(set, kind, colour);

                        // A body that does not swing says so with an empty address and the view
                        // keeps walking. Only the medley has one (see the remarks).
                        if (string.IsNullOrEmpty(address))
                        {
                            Assert.AreEqual(SiegeMode.Medley, set,
                                            $"cast {set} swings nothing for a {kind} in colour "
                                            + $"{Wards.WardLine.Colours[colour]}, and only the "
                                            + "medley is allowed a gap");
                            continue;
                        }

                        Assert.That(loaded.Contains(address), Is.True,
                                    $"cast {set} swings {address} and never loads it");

                        Assert.That(seen.Add(address), Is.True,
                                    $"cast {set} swings {address} for two different bodies, so "
                                    + "the array is not in the order CastSwing assumes");
                    }

                Assert.AreEqual(loaded.Count, seen.Count,
                                $"cast {set} loads {loaded.Count} swing reels and draws "
                                + $"{seen.Count} of them");
            }
        }

        /// <summary>
        /// A swing reel is never one of the walk reels.
        ///
        /// <b>The one way this could be wrong and still pass everything above</b>: a swing table
        /// pointed at the walks would index, load and draw, and what it would look like is the
        /// cast that <em>has</em> an attack animation not playing it — the exact fault the reels
        /// were cut to fix, shipped green.
        /// </summary>
        [Test]
        public void NoSwingReelIsAWalkReel()
        {
            foreach (int set in Sets)
            {
                var swings = SiegeMode.CastSwingArt(set);
                if (swings == null) continue;

                var walks = new HashSet<string>();
                foreach (var request in SiegeMode.CastArt(set)) walks.Add(request.Address);

                foreach (var request in swings)
                    Assert.That(walks.Contains(request.Address), Is.False,
                                $"cast {set} swings {request.Address}, which is one of its walks");
            }
        }

        /// <summary>
        /// A boss never swings: it stands in the middle of the hill and casts from there.
        ///
        /// <b>Asked here rather than left to the view</b>, because <c>CastSwing</c> takes a kind
        /// and the row it indexes for anything that is not a brute or a bulwark is the creeper's -
        /// so a caller that handed it a boss would get a creeper's swing reel at three cells tall.
        /// The view refuses first (<c>SiegeView.Swing</c>); this is what stops that refusal being
        /// the only thing standing between a boss and a beetle's animation.
        /// </summary>
        [Test]
        public void ABossNeverSwings()
        {
            var bosses = new[]
            {
                SiegeKind.Boss, SiegeKind.Overlord, SiegeKind.Blightcaller,
                SiegeKind.Warbringer, SiegeKind.Gravemaw, SiegeKind.Bonecaller,
                SiegeKind.Shackler, SiegeKind.Ironclad,
            };

            foreach (var kind in bosses)
                Assert.That(SiegeTuning.IsBoss(kind), Is.True,
                            $"{kind} is not one of this mode's bosses any more");
        }

        /// <summary>
        /// Every cast reel is addressed under this mode's own art, so it lands in Thornwatch's
        /// Addressables group rather than in the global set — which is what bounds memory by what is
        /// on screen (invariant 7b) instead of by how many casts the game has ever shipped.
        /// </summary>
        [Test]
        public void EveryCastReelIsAddressedUnderThisModesOwnArt()
        {
            string root = AssetManifest.SiegeArt(string.Empty);

            foreach (int set in Sets)
                foreach (var request in SiegeMode.CastArt(set))
                    Assert.That(request.Address.StartsWith(root), Is.True,
                                $"{request.Address} is not under {root}");
        }

        /// <summary>
        /// **Every cast is reachable from <c>SiegeMode.Art</c>**, which is the set
        /// <c>AddressableAddresses.FrameFolders</c> walks to give a sprite set its frame label.
        ///
        /// <b>This is the fault invariant 37at was found by, written down as a test.</b> A reel the
        /// mode's whole-art answer never names is one that ships addressed, grouped, built into a
        /// bundle and <em>impossible to load</em> — twelve <c>No Location found for Key=…</c> lines
        /// and a hill of health bars floating over nothing. It went unnoticed because every gate
        /// there was walked the manifest, and the manifest never asked for that cast.
        /// </summary>
        [Test]
        public void EveryCastIsReachableFromTheModesWholeArt()
        {
            var whole = new HashSet<string>();
            foreach (var request in new SiegeMode().Art) whole.Add(request.Address);

            foreach (int set in Sets)
                foreach (var request in SiegeMode.CastArt(set))
                    Assert.That(whole.Contains(request.Address), Is.True,
                                $"{request.Address} is in cast {set} and is not named by "
                                + "SiegeMode.Art, so its frames would never be labelled");
        }
    }
}
