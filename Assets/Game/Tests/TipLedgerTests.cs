using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The lesson ledger against the bound the security rules put on it.
    ///
    /// <para>
    /// This fixture exists because of one afternoon: the ledger keeps every id it has ever
    /// seen, the rules capped the list at 64, and the owner's own account — the longest-played
    /// one, carrying the lessons of every withdrawn mode — crossed the cap on 2026-09-16. From
    /// that write on the rules refused every save for the account (invariant 12a), and the only
    /// thing any screen said was that an account switch could not save the grove. Nothing here
    /// could have seen it, because nothing held the ledger's size to the rules' bound.
    /// </para>
    /// </summary>
    public sealed class TipLedgerTests
    {
        static List<string> Sorted(IEnumerable<string> ids)
        {
            var list = new List<string>(ids);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        static IEnumerable<string> Unknown(int count)
        {
            for (int i = 0; i < count; i++) yield return $"zz_unknown_{i:000}";
        }

        [Test]
        public void EveryLessonTheGameHasEverHadFitsUnderTheCapWithRoom()
        {
            int all = Mechanic.All.Length + Mechanic.Retired.Length;

            // Half, deliberately: a cap the live set is close to is a cap the next mode crosses.
            Assert.LessOrEqual(all, TipLedger.MaxIds / 2,
                               $"{all} lessons have existed against a cap of {TipLedger.MaxIds}; " +
                               "raise TipLedger.MaxIds and the rules' bound together");
        }

        [Test]
        public void UnderTheCapNothingIsTouched()
        {
            var ids = Sorted(Unknown(TipLedger.MaxIds));
            CollectionAssert.AreEqual(ids, TipLedger.Bounded(ids),
                                      "a list that fits must come back exactly as it went in");
        }

        [Test]
        public void TheWriterNeverExceedsTheCap()
        {
            var ids = new List<string>(Unknown(TipLedger.MaxIds * 3));
            foreach (var mechanic in Mechanic.All) ids.Add(mechanic.Id);
            foreach (var mechanic in Mechanic.Retired) ids.Add(mechanic.Id);

            var written = TipLedger.Bounded(Sorted(ids));

            Assert.LessOrEqual(written.Length, TipLedger.MaxIds);
        }

        [Test]
        public void ALiveLessonIsNeverDroppedToMakeRoom()
        {
            var ids = new List<string>(Unknown(TipLedger.MaxIds * 2));
            foreach (var mechanic in Mechanic.All) ids.Add(mechanic.Id);

            var written = new HashSet<string>(TipLedger.Bounded(Sorted(ids)));

            foreach (var mechanic in Mechanic.All)
                Assert.IsTrue(written.Contains(mechanic.Id),
                              $"'{mechanic.Id}' is live and was dropped; it would be taught again");
        }

        [Test]
        public void RetiredLessonsGoFirstWhenTheLedgerIsFull()
        {
            // Exactly one over the cap, with one retired id in it: the retired id is what goes,
            // and every unknown id — which might be a newer build's lesson — stays.
            var ids = new List<string>(Unknown(TipLedger.MaxIds));
            ids.Add(Mechanic.Retired[0].Id);

            var written = new HashSet<string>(TipLedger.Bounded(Sorted(ids)));

            Assert.IsFalse(written.Contains(Mechanic.Retired[0].Id), "the retired lesson goes first");
            foreach (var id in Unknown(TipLedger.MaxIds))
                Assert.IsTrue(written.Contains(id), $"'{id}' was dropped while a retired id could have gone");
        }

        [Test]
        public void TheMergeIsBoundedTooBecauseItIsWhatGetsPushed()
        {
            var mine = Sorted(Unknown(TipLedger.MaxIds)).ToArray();
            var theirs = new[] { "zz_unknown_extra", Mechanic.All[0].Id };

            var joined = TipLedger.Join(mine, theirs);

            Assert.LessOrEqual(joined.Length, TipLedger.MaxIds,
                               "SaveMerge pushes Join's answer straight to the server");
            CollectionAssert.Contains(joined, Mechanic.All[0].Id);
        }
    }
}
