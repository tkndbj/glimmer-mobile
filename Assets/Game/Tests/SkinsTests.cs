using System.Collections.Generic;
using System.Reflection;
using GlimmerGrove;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Store;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That every sprite <see cref="Skins"/> names is one the game actually loads.
    ///
    /// <para>
    /// <b>It exists because a constant is invisible to <c>Tools/verify/artnames.py</c>.</b>
    /// That gate reads <em>literals</em> at a call site — <c>Art.S("Ui/ic_gem")</c> — which is
    /// exactly what a named skin is not, so a typo in this table resolves to nothing and draws
    /// a <b>white rectangle</b> (invariant 7b) with every offline gate green. The count that
    /// check prints of names that are *built rather than written* is the only thing that
    /// reports the loss, and it went up by thirteen the day the storefront's furniture was
    /// first routed through a table. It has gone up again by the whole of this one, so the
    /// chain has to be closed here.
    /// </para>
    /// <para>
    /// It is the same shape as the check <c>artnames.py</c> makes and not a second copy of it:
    /// that one holds a literal to what is on <em>disk</em>, this one holds a constant to what
    /// the <em>manifest preloads</em> — and the manifest's own entries are literals, so they
    /// are already held to disk. The two together are what make the chain complete.
    /// </para>
    /// <para>
    /// <b>The table is walked by reflection rather than listed.</b> A hand-written list is a
    /// second copy of <see cref="Skins"/> that goes stale the moment somebody adds a piece —
    /// and the failure of a stale list here is silent, because a skin nobody checked is
    /// exactly the skin that draws nothing.
    /// </para>
    /// </summary>
    public sealed class SkinsTests
    {
        /// <summary>Every <c>string</c> constant on <see cref="Skins"/>, name and value.</summary>
        static IEnumerable<KeyValuePair<string, string>> Named()
        {
            foreach (var field in typeof(Skins).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string) || !field.IsLiteral) continue;
                yield return new KeyValuePair<string, string>(field.Name, (string)field.GetRawConstantValue());
            }
        }

        [Test]
        public void EverySkinTheGameNamesIsOneTheManifestLoads()
        {
            var loaded = new HashSet<string>();
            foreach (var request in AssetManifest.GlobalAssets()) loaded.Add(request.Address);

            int checked_ = 0;
            foreach (var skin in Named())
            {
                checked_++;

                // `Art.S("Ui/" + skin)` is what these are handed to, so the address is built
                // the way a screen builds it rather than the way this test guesses it.
                Assert.That(loaded, Contains.Item(AssetManifest.ArtRoot + "Ui/" + skin.Value),
                            $"Skins.{skin.Key} asks for '{skin.Value}', which AssetManifest does "
                            + "not load - an address that resolves to nothing draws a white rectangle");
            }

            // A reflection walk that finds nothing passes every assertion in it, so this is the
            // one thing the walk cannot check about itself. The floor is deliberately well below
            // any plausible size of the table rather than close to its current one: it is asking
            // "did reflection work", not "is the table still this big", and a floor set at the
            // count of the day fails the first time somebody removes a skin — which it did, on
            // the same afternoon, when six pieces nothing drew were taken out.
            Assert.Greater(checked_, 12, "Skins is walked by reflection and came back nearly "
                                         + "empty - the walk is broken, not the table");
        }

        /// <summary>
        /// That no two shelves in the storefront are told apart by nothing.
        ///
        /// <para>
        /// The restyle took the coloured card frames away — every card is the kit's one teal
        /// plate now — so what says which shelf a player is on is the lit tab and the coloured
        /// light under the goods. This is that second half held to its job: two shelves sharing
        /// an accent would be two shelves whose cards are pixel-identical, which is the fault
        /// the old three-frames-across-five-shelves table had and got away with only because
        /// the two that shared were never on screen together.
        /// </para>
        /// </summary>
        [Test]
        public void NoTwoShelvesShareAnAccent()
        {
            var seen = new Dictionary<UnityEngine.Color, StoreShelf>();

            foreach (StoreShelf shelf in System.Enum.GetValues(typeof(StoreShelf)))
            {
                var accent = Skins.Accent(shelf);
                Assert.IsFalse(seen.ContainsKey(accent),
                               $"{shelf} and {(seen.ContainsKey(accent) ? seen[accent] : shelf)} "
                               + "carry the same accent, so their cards are identical");
                seen[accent] = shelf;
            }
        }
    }
}
