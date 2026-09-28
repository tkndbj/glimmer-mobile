using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The strike kit: the nine masks a stormcall and an overcharge are lit with.
    ///
    /// <para>
    /// <b>Their addresses are built from <see cref="StrikeFx"/>'s names, so
    /// <c>Tools/verify/artnames.py</c> cannot see them</b> (a constant is invisible to it, and the
    /// chain is closed by a fixture - the same bargain <c>SkinsTests</c> strikes for the skin
    /// table). Three things have to agree and nothing else holds them: the names the code
    /// asks for, the pieces <c>Tools/make_strike_fx.py</c> cuts, and the scope the siege loads.
    /// A piece named here and not cut is a strike short of its flare on every device with
    /// nothing saying so; a piece cut and not scoped is a warning in the console and the same
    /// missing flare (invariant 7b).
    /// </para>
    /// </summary>
    public sealed class StrikeFxTests
    {
        static string Folder => Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Fx", "Strike");

        [Test]
        public void EveryPieceIsOnDisk()
        {
            foreach (var piece in StrikeFx.All)
            {
                var png = Path.Combine(Folder, piece + ".png");
                Assert.That(File.Exists(png), $"{piece}.png is not under Art/Fx/Strike - run " +
                                              "python Tools/make_strike_fx.py --write");
                Assert.That(File.Exists(png + ".meta"), $"{piece}.png has no .meta - the tool writes one");
            }
        }

        [Test]
        public void ThePiecesAreTheToolsPieces()
        {
            var tool = File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Tools", "make_strike_fx.py"));
            var cuts = Regex.Match(tool, @"CUTS\s*=\s*\{(.*?)\n\}", RegexOptions.Singleline);
            Assert.That(cuts.Success, "make_strike_fx.py no longer carries a CUTS table");

            var named = Regex.Matches(cuts.Groups[1].Value, @"^\s*""(\w+)"":", RegexOptions.Multiline)
                             .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();

            Assert.That(named, Is.EquivalentTo(StrikeFx.All),
                        "StrikeFx.All and make_strike_fx.py's CUTS name different pieces");
        }

        [Test]
        public void EveryPieceIsInTheSiegeScope()
        {
            var siege = LevelModes.Find(GameMode.Siege);
            Assert.That(siege, Is.Not.Null);

            var scope = siege.ArtFor(null).Select(r => r.Address).ToList();

            foreach (var piece in StrikeFx.All)
                Assert.That(scope, Does.Contain(AssetManifest.StrikeFx(piece)),
                            $"{piece} is not scoped by SiegeMode.ArtFor, so a strike draws without it");
        }

        [Test]
        public void TheOldReelIsGone()
        {
            // The baked storm reel was withdrawn with its Addressables rows (8d). A frame of it
            // coming back on disk is dead weight in the global bundle for the life of the game.
            var old = Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Fx", "Siege", "storm");
            Assert.That(Directory.Exists(old), Is.False, "Art/Fx/Siege/storm is back - nothing draws it");

            var group = File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Assets", "AddressableAssetsData",
                                                      "AssetGroups", "Glimmer Global.asset"));
            Assert.That(group, Does.Not.Contain("Art/Fx/Siege/storm"));
        }
    }
}
