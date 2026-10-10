using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The hub a lane with no ladder draws, and the band it has to fit into.
    ///
    /// <para>
    /// <b>It has the tightest budget of any stack in this game and the least able to be seen
    /// wrong.</b> The column is drawn between two pieces of furniture that were sized without it
    /// - the map's header above it and the loadout shelf below - so the room it has is a
    /// subtraction nobody looking at either of those numbers would think to do, and the device
    /// where it runs out is the <em>shortest</em> canvas rather than the commonest one. That is
    /// <c>PanelStack</c>'s fault exactly, met a seventh time: a hand-written height that was
    /// short of what it was drawing, with a compile, a validator and a screenshot on the one
    /// aspect it was tuned on all perfectly happy.
    /// </para>
    /// </summary>
    public sealed class EndlessHubTests
    {
        /// <summary>
        /// The column leaves clear air between every pair of things in it.
        ///
        /// Asked of the stack rather than of a caller, because it is a fact about the constants:
        /// nothing on this screen chooses how many lines there are.
        /// </summary>
        [Test]
        public void TheColumnLeavesClearAirEverywhere()
        {
            Assert.IsTrue(EndlessHubLayout.IsClear(out string fault), fault);
        }

        /// <summary>
        /// Every centre in the column is below the one before it, in the order they are read, and
        /// the column's height is where its last piece ends.
        ///
        /// <b>Separate from the clearance check on purpose</b>, and it is <c>WheelPanel</c>'s
        /// lesson: that stack had the test <em>and</em> the arithmetic and still drew a row
        /// through its neighbour, because one number in it meant something different from the
        /// rest. This asks the ordering directly, so a centre that was quietly re-read as a top
        /// fails here rather than on a device.
        /// </summary>
        [Test]
        public void TheColumnReadsDownwards()
        {
            Assert.Less(EndlessHubLayout.HeroCentre, EndlessHubLayout.PanelCentre,
                        "the plate is not under the rank");
            Assert.Less(EndlessHubLayout.PanelCentre, EndlessHubLayout.RecordCentre,
                        "the record is not under the plate");
            Assert.Less(EndlessHubLayout.RecordCentre, EndlessHubLayout.CheckpointCentre,
                        "the checkpoint bar is not under the record");
            Assert.Less(EndlessHubLayout.CheckpointCentre, EndlessHubLayout.ButtonCentre,
                        "the button is not under the checkpoint bar");
            Assert.AreEqual(EndlessHubLayout.ButtonWidth, EndlessHubLayout.CheckpointWidth, .001f,
                            "the checkpoint bar speaks for the key and is drawn its width");

            float last = EndlessHubLayout.RowCentre(0);

            for (int i = 1; i < EndlessHubLayout.Points; i++)
            {
                Assert.Less(last, EndlessHubLayout.RowCentre(i), $"row {i + 1} is out of order");
                last = EndlessHubLayout.RowCentre(i);
            }

            Assert.AreEqual(EndlessHubLayout.ButtonCentre + EndlessHubLayout.ButtonHeight * .5f,
                            EndlessHubLayout.Height, .001f,
                            "the column's height is not where its last piece ends");
        }

        /// <summary>
        /// <b>The whole column fits the room the map's own furniture leaves it, on the shortest
        /// canvas this game is drawn on.</b>
        ///
        /// <para>
        /// The subtraction is the point. <c>CanvasFit.ShortestCanvas</c> is what a 7:4 display
        /// gets - the squarest phone this game supports, and the one with the least height to
        /// spend - and out of it come the header column (<c>LevelsScreen.HeaderUnderside</c>, the
        /// plaque and both switchers) and the loadout shelf. A taller phone has more room in hand
        /// even after its notch and its home indicator are paid for, because the canvas grows by
        /// far more than the insets do.
        /// </para>
        /// <para>
        /// <b>The shelf is <c>LoadoutBar.Bare</c> rather than <c>Height</c>, and that is not a
        /// convenience.</b> <c>Height</c> reads the display's safe area - a native call - so a
        /// fixture asking it is reported as "needs the Editor" and becomes the one gate nobody
        /// runs on the way past (invariant 29e). On this canvas the two are the same number
        /// anyway: a squarish phone has no home indicator to pay for.
        /// </para>
        /// </summary>
        [Test]
        public void TheColumnFitsTheShortestCanvasTheMapLeavesIt()
        {
            // **Both switcher pills, which is the case the shipped catalog does not draw.** One
            // mode means no mode pill and the track pill takes its slot, so today the header is a
            // whole pill shallower - and a catalog that re-enabled a hidden mode would put that
            // pill back with nothing to say the column no longer fits under it.
            float header = LevelsScreen.HeaderUnderside
                         + ModeSwitch.PillHeight + LevelsScreen.SwitcherGap;

            float band = CanvasFit.ShortestCanvas
                       - header
                       - EndlessHubLayout.HeadClear
                       - LoadoutBar.Bare - LoadoutBar.Overhang;

            // **Asked at the floor scale since 2026-09-28**, when the record moved under the lines
            // and the column became taller than this band at full size: the column is drawn to
            // fit (`ScaleIn`), so what can fail is the floor, never a key behind the shelf.
            Assert.IsTrue(EndlessHubLayout.Fits(band),
                          $"the hub's column is {EndlessHubLayout.Height} tall, "
                          + $"{EndlessHubLayout.Height * EndlessHubLayout.MinScale} at its floor "
                          + $"scale, and the shortest canvas leaves it {band} once a two-pill "
                          + $"header's {header} and the shelf's {LoadoutBar.Bare} plus its "
                          + $"{LoadoutBar.Overhang} of tab are taken out - so the key is drawn behind "
                          + "the loadout bar on the squarest phone this game supports");

            float scale = EndlessHubLayout.ScaleIn(band);
            Assert.GreaterOrEqual(band, EndlessHubLayout.Height * scale - .001f,
                                  $"at x{scale} the column is still taller than its band");
        }

        /// <summary>
        /// The column is drawn whole wherever it fits, shrunk to its band where it does not, and
        /// never below the floor.
        /// </summary>
        [Test]
        public void TheColumnScalesToItsBandAndNeverBelowTheFloor()
        {
            float h = EndlessHubLayout.Height;

            Assert.AreEqual(1f, EndlessHubLayout.ScaleIn(h + 400f), .0001f, "a roomy band shrank the column");
            Assert.AreEqual(1f, EndlessHubLayout.ScaleIn(h), .0001f, "an exact band shrank the column");
            Assert.AreEqual(.9f, EndlessHubLayout.ScaleIn(h * .9f), .0001f,
                            "a short band did not shrink the column to fit it");
            Assert.AreEqual(EndlessHubLayout.MinScale, EndlessHubLayout.ScaleIn(10f), .0001f,
                            "the column was drawn below its floor");
            Assert.Less(EndlessHubLayout.MinScale, 1f);
        }

        /// <summary>
        /// The column is centred in whatever band it is given, and never starts above the top of
        /// one too short to hold it.
        ///
        /// A band that cannot hold it is a composition somebody can see is wrong; a column
        /// started at a negative offset is one drawn off the top of the screen, where nobody can.
        /// </summary>
        [Test]
        public void TheColumnCentresInItsBandAndNeverClimbsOutOfIt()
        {
            float roomy = EndlessHubLayout.Height + 400f;

            Assert.AreEqual(400f * EndlessHubLayout.Lift, EndlessHubLayout.TopIn(roomy), .001f,
                            "the column does not sit where the lift says in a roomy band");
            Assert.Less(EndlessHubLayout.Lift, .5f,
                        "the column is meant to sit above centre, so the slack collects at the "
                        + "foot where the shelf's tab is");

            Assert.AreEqual(0f, EndlessHubLayout.TopIn(EndlessHubLayout.Height), .001f);
            Assert.AreEqual(0f, EndlessHubLayout.TopIn(10f), .001f,
                            "a band too short to hold the column started it above the top");
        }

        /// <summary>
        /// <b>A lane's crest stands centred in the gap between the header and the lines, and
        /// inside it</b>, whatever air the band leaves above the column - from none beyond
        /// <c>HeadClear</c> on the squarest phone to a tall phone's slack. The owner asked for it
        /// centred in what is seen (2026-10-09); centred in its box alone it sat low.
        /// </summary>
        [Test]
        public void TheCrestIsCentredInTheGapItIsSeenIn()
        {
            float plateTop = EndlessHubLayout.PanelCentre - EndlessHubLayout.PanelHeight * .5f;

            foreach (float air in new[] { EndlessHubLayout.HeadClear, 120f, 200f, 400f })
            {
                float tall = EndlessHubLayout.CrestHeight(air);
                float centre = EndlessHubLayout.CrestCentre(air);

                Assert.AreEqual(centre - (-air), plateTop - centre, .001f,
                                $"with {air} of air the crest is not midway between header and plate");
                Assert.LessOrEqual(centre + tall * .5f, plateTop - EndlessHubLayout.CrestAir + .001f,
                                   $"with {air} of air the crest reaches the plate");
                Assert.GreaterOrEqual(centre - tall * .5f, -air + EndlessHubLayout.CrestAir - .001f,
                                      $"with {air} of air the crest reaches the header");
                Assert.LessOrEqual(tall * 1.5f, EndlessHubLayout.CrestWidest + .001f,
                                   "the crest is wider than the plate under it");
            }
        }

        /// <summary>
        /// <b>Every mark the hub draws is global art the game always holds.</b>
        ///
        /// <para>
        /// The three addresses are a table rather than literals at the call site, because a loop
        /// cannot write a literal - and <c>Tools/verify/artnames.py</c> reads literals off call
        /// sites and can see nothing else. So the chain is closed here instead, exactly as
        /// <c>SkinsTests</c> closes it for the interface kit's own names: what the hub asks for has
        /// to be something <c>AssetManifest.GlobalAssets</c> loads, or the first player to open
        /// the Infinite lane gets three white rectangles (invariant 7b).
        /// </para>
        /// <para>
        /// <b>Global rather than scoped, and that is the judgement being pinned.</b> The hub is
        /// drawn on the map screen, whose scope is loaded for the <em>chapter</em>; a mark filed
        /// with the mode would be asked for on a screen that may not hold it yet.
        /// </para>
        /// </summary>
        [Test]
        public void TheHubsMarksAreGlobalArt()
        {
            var global = new System.Collections.Generic.HashSet<string>();

            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets())
                global.Add(request.Address);

            foreach (var hub in new[] { EndlessHub.HubLane.Infinite, EndlessHub.HubLane.Shuffle })
            {
                var marks = hub.Marks;

                Assert.AreEqual(hub.Track.Points, marks.Length,
                                "the hub draws a line it has no mark for, or a mark it never reads");
                Assert.LessOrEqual(hub.Track.Points, EndlessHubLayout.Points,
                                   $"'{hub.Track}' says more lines than the plate holds");

                foreach (string mark in marks)
                    Assert.IsTrue(global.Contains(AssetPipeline.AssetManifest.ArtRoot + mark),
                                  $"the hub asks for '{mark}', which nothing loads - an Image with no "
                                  + "sprite is a white rectangle rather than a blank");
            }

            // Each lane's row names its own wall, and both walls are global for the same reason;
            // so is a crest a lane stands as its hero.
            foreach (var lane in new[] { EndlessHub.HubLane.Infinite, EndlessHub.HubLane.Shuffle })
            {
                Assert.IsTrue(global.Contains(AssetPipeline.AssetManifest.ArtRoot + lane.Wall),
                              $"the {lane.Track} hub stands on '{lane.Wall}', which nothing loads");

                if (lane.Crest != null)
                    Assert.IsTrue(global.Contains(AssetPipeline.AssetManifest.ArtRoot + lane.Crest),
                                  $"the {lane.Track} hub draws '{lane.Crest}' as its hero, which nothing loads");
            }
        }

        // ----------------------------------------------------------------- the lane
        /// <summary>
        /// <b>Exactly one shipped lane is not a ladder, and it is the endless one.</b>
        ///
        /// The map asks this and nothing else to decide which of two completely different
        /// screens to draw (<c>LevelsScreen.BuildChapter</c>), so a lane answering wrongly is a
        /// chapter's whole map replaced by a hub or an endless run drawn as a chain of one.
        /// </summary>
        [Test]
        public void OnlyTheEndlessLaneIsNotALadder()
        {
            Assert.IsTrue(GameTrack.Main.Laddered, "the ordinary ladder stopped being one");
            Assert.IsFalse(GameTrack.Infinite.Laddered, "the endless lane reads as a ladder");

            int hubs = 0;

            foreach (var track in GameTrack.Shipped)
                if (!track.Laddered) hubs++;

            // The Infinite lane and the Shuffle lane (MODES.md 59). A third draws a hub too -
            // which is fine, and its own lines have to be authored before it ships
            // (GameTrack.PointKey), which both content gates hold.
            Assert.AreEqual(2, hubs, "a lane that draws a hub was added or lost");
            Assert.IsFalse(GameTrack.Shuffle.Laddered, "the Shuffle lane reads as a ladder");
        }

        /// <summary>
        /// A lane's lines are its own, derived from its id, and there are as many of them as the
        /// hub draws.
        ///
        /// <b>That they resolve to real strings is a content question</b>, so both content gates
        /// ask it of every shipped lane rather than a fixture asking it of one - a key that
        /// resolves to nothing is a blank row on a screen, which is exactly the failure invariant
        /// 6 exists to make impossible.
        /// </summary>
        [Test]
        public void ALanesLinesAreDerivedFromItsOwnId()
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            for (int i = 1; i <= EndlessHubLayout.Points; i++)
            {
                string key = GameTrack.Infinite.PointKey(i);

                Assert.IsTrue(key.StartsWith("track.infinite."), key);
                Assert.IsTrue(seen.Add(key), $"two of the hub's lines read '{key}'");
            }

            Assert.AreNotEqual(GameTrack.Main.PointKey(1), GameTrack.Infinite.PointKey(1),
                               "two lanes share a line");

            // A bad index answers the first key rather than building a name nothing resolves.
            Assert.AreEqual(GameTrack.Infinite.PointKey(1), GameTrack.Infinite.PointKey(0));
            Assert.AreEqual(GameTrack.Infinite.PointKey(1), GameTrack.Infinite.PointKey(-3));
        }

        // ------------------------------------------------------------ redrawing on a sync
        /// <summary>
        /// A sync that moved nothing the column draws redraws nothing.
        ///
        /// <b>The fault this holds</b> is the screen "reloading" while nobody touched it: every
        /// sync that learned anything at all took the column down and popped it back in.
        /// </summary>
        [Test]
        public void AnUnchangedReadingIsNotRedrawn()
        {
            var drawn = new EndlessHub.Reading(12, true, null);

            Assert.IsFalse(EndlessHub.Redraws(true, drawn, new EndlessHub.Reading(12, true, null)));

            // An open key carries no wall, and "no wall" is one answer however it was spelt.
            Assert.IsFalse(EndlessHub.Redraws(true, drawn, new EndlessHub.Reading(12, true, string.Empty)));
        }

        /// <summary>
        /// Each of the three facts the column draws, moved alone, is a redraw - so none of them
        /// can be dropped from the comparison without this failing by name.
        /// </summary>
        [Test]
        public void EveryFactTheColumnDrawsForcesARedraw()
        {
            var drawn = new EndlessHub.Reading(12, false, "REACH LEVEL 10");

            Assert.IsTrue(EndlessHub.Redraws(true, drawn, new EndlessHub.Reading(13, false, "REACH LEVEL 10")),
                          "a new best wave was not redrawn");
            Assert.IsTrue(EndlessHub.Redraws(true, drawn, new EndlessHub.Reading(12, true, "REACH LEVEL 10")),
                          "the lane opening was not redrawn");
            Assert.IsTrue(EndlessHub.Redraws(true, drawn, new EndlessHub.Reading(12, false, "REACH LEVEL 11")),
                          "a changed wall was not redrawn");
        }

        /// <summary>A column that is not standing is always drawn, whatever it last said.</summary>
        [Test]
        public void AMissingColumnIsAlwaysDrawn()
        {
            var reading = new EndlessHub.Reading(5, true, null);

            Assert.IsTrue(EndlessHub.Redraws(false, reading, reading));
            Assert.IsTrue(EndlessHub.Redraws(false, default, default));
        }

        /// <summary>Equal readings hash alike, so a reading is safe as a key anywhere.</summary>
        [Test]
        public void EqualReadingsHashAlike()
        {
            var a = new EndlessHub.Reading(40, true, null);
            var b = new EndlessHub.Reading(40, true, string.Empty);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            // `default` skips the constructor and leaves the wall null, and must still compare.
            Assert.AreEqual(default(EndlessHub.Reading), new EndlessHub.Reading(0, false, null));
            Assert.AreEqual(default(EndlessHub.Reading).GetHashCode(),
                            new EndlessHub.Reading(0, false, null).GetHashCode());
        }
    }
}
