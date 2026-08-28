using System.Runtime.CompilerServices;

// Presentation's counterpart to Domain's, and it exists for one narrow reason: the numbers a
// panel is *built* from.
//
// A panel whose height depends on what it is saying has to derive that height, and this project
// has paid twice for the alternative — GladeRewardsOverlay drew its last paragraph 78 units into
// its own close button, and WheelPanel drew a row through its neighbour while its own test
// passed, because the test restated the arithmetic instead of reading what the panel used. The
// lesson both times was the same: the check has to read the constants the panel reads.
//
// Those constants are layout details nothing in the game should reach for, so they stay
// internal. The test assembly gets to see them; nothing else does.
[assembly: InternalsVisibleTo("GlimmerGrove.Tests")]
