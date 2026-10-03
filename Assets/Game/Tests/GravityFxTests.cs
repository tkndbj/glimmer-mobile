using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The gravity well's four pieces, and the Gravity Hole's bar icon.
    ///
    /// <para>
    /// <b>Their addresses are built from <see cref="GravityFx"/>'s names, so
    /// <c>artnames.py</c> cannot see them</b> - <c>StrikeFxTests</c>' situation exactly, and its
    /// three questions: is every piece on disk, are the names the tool's, and does the siege
    /// scope ask for every one. A piece missing from any of the three is a well drawn without
    /// it and nothing saying so (invariant 7b).
    /// </para>
    /// </summary>
    public sealed class GravityFxTests
    {
        static string Art => Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art");

        static string Folder => Path.Combine(Art, "Fx", "Gravity");

        [Test]
        public void EveryPieceIsOnDisk()
        {
            foreach (var piece in GravityFx.All)
            {
                var png = Path.Combine(Folder, piece + ".png");
                Assert.That(File.Exists(png), $"{piece}.png is not under Art/Fx/Gravity - run " +
                                              "python Tools/make_gravity_fx.py --write");
                Assert.That(File.Exists(png + ".meta"), $"{piece}.png has no .meta - the tool writes one");
            }
        }

        [Test]
        public void ThePiecesAreTheToolsPieces()
        {
            var tool = File.ReadAllText(Path.Combine(TestJson.RepoRoot(), "Tools", "make_gravity_fx.py"));
            var cuts = Regex.Match(tool, @"CUTS\s*=\s*\{(.*?)\n\}", RegexOptions.Singleline);
            Assert.That(cuts.Success, "make_gravity_fx.py no longer carries a CUTS table");

            var named = Regex.Matches(cuts.Groups[1].Value, @"^\s*""(\w+)"":", RegexOptions.Multiline)
                             .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();

            Assert.That(named, Is.EquivalentTo(GravityFx.All),
                        "GravityFx.All and make_gravity_fx.py's CUTS name different pieces");
        }

        [Test]
        public void EveryPieceIsInTheSiegeScope()
        {
            var siege = LevelModes.Find(GameMode.Siege);
            Assert.That(siege, Is.Not.Null);

            var scope = siege.ArtFor(null).Select(r => r.Address).ToList();

            foreach (var piece in GravityFx.All)
                Assert.That(scope, Does.Contain(AssetManifest.GravityFx(piece)),
                            $"{piece} is not scoped by SiegeMode.ArtFor, so a well draws without it");
        }

        [Test]
        public void TheBarIconIsOnDiskAndPreloaded()
        {
            var png = Path.Combine(Art, "Ui", "Utility", "gravityhole.png");
            Assert.That(File.Exists(png), "Ui/Utility/gravityhole.png is missing - run " +
                                          "python Tools/make_gravity_fx.py --write --source <painting>");
            Assert.That(File.Exists(png + ".meta"), "the icon has no .meta - the tool writes one");

            var loaded = AssetManifest.GlobalAssets().Select(r => r.Address).ToList();
            Assert.That(loaded, Does.Contain(AssetManifest.ArtRoot + "Ui/Utility/gravityhole"),
                        "the icon is not in the global preload set, so the bar draws a white square");
        }
    }
}
