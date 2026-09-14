using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The four casts a siege can draw, and the ways a cast can silently stop being drawable.
    ///
    /// <para>
    /// <b>This is <c>SiegeGroundTests</c>'s question asked of the raiders.</b> A rung's ground is
    /// named twice and a test holds the two names together; a cast is worse, because it is twelve
    /// bodies rather than one and there are four of them. What that fixture records applies here
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
            SiegeMode.Insects, SiegeMode.Baked, SiegeMode.Brood, SiegeMode.Bones,
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
        /// No two casts share a reel.
        ///
        /// <b>The property rather than a table</b> (invariant 44e): two casts that overlap are two
        /// chapters that look the same on the bodies they share, and the whole reason a second cast
        /// exists is that they should not.
        /// </summary>
        [Test]
        public void NoTwoCastsShareABody()
        {
            var owner = new Dictionary<string, int>();

            foreach (int set in Sets)
                foreach (var request in SiegeMode.CastArt(set))
                {
                    if (owner.TryGetValue(request.Address, out int already))
                        Assert.Fail($"{request.Address} is in cast {already} and cast {set}");

                    owner[request.Address] = set;
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
        /// The three chapters this game ships draw <b>different</b> casts, and the Infinite lane
        /// draws the baked one.
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

            Assert.AreEqual(SiegeMode.Baked, SiegeMode.CastFor(GameTrack.Infinite, 0),
                            "the Infinite lane does not draw the baked cast");
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
        /// **A cast that swings names one reel per body, in the same order it names its walks** —
        /// or names none at all, which is what two of the four do.
        ///
        /// <para>
        /// <b>Null and a full twelve are the only two legal answers</b>, and that is the whole of
        /// what this checks. A partial table would index correctly for the rows it had and answer
        /// an address that is not on disk for the rest — which loads as nothing, and an
        /// <c>Image</c> with a null sprite is a white rectangle over a raider standing at the ward
        /// line (invariant 7b), on the rungs where the line is already being lost.
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
                foreach (var request in swings) loaded.Add(request.Address);

                var seen = new HashSet<string>();

                foreach (var kind in Bodies)
                    for (int colour = 0; colour < Wards.WardLine.Colours.Length; colour++)
                    {
                        string address = SiegeMode.CastSwing(set, kind, colour);

                        Assert.IsNotEmpty(address,
                                          $"cast {set} swings nothing for a {kind} in colour "
                                          + $"{Wards.WardLine.Colours[colour]}");

                        Assert.That(loaded.Contains(address), Is.True,
                                    $"cast {set} swings {address} and never loads it");

                        seen.Add(address);
                    }

                Assert.AreEqual(SiegeMode.CastBodies, seen.Count,
                                $"cast {set} swings {seen.Count} distinct reels for "
                                + $"{SiegeMode.CastBodies} slots, so the array is not in the order "
                                + "CastSwing assumes");
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
