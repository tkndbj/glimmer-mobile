using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The proof a publish has to pass: that the server built the card from the save this
    /// device had just settled with it, or a later one.
    ///
    /// <para>
    /// Small, and worth pinning exactly, because both edges are traps. Too strict and a
    /// deployment that predates the field refuses every client for ever (invariant 13a);
    /// too loose and the check is decoration, and the failure it exists for — a card one
    /// session behind its grove, with a successful call and a well-formed card on every
    /// publish — comes back without a symptom.
    /// </para>
    /// </summary>
    public sealed class GrovePublicationTests
    {
        static GrovePublication Built(long revision) => new GrovePublication(GroveCard.Empty, revision);

        [Test]
        public void ACardBuiltFromTheSaveAskedForIsProved()
        {
            Assert.IsTrue(Built(41L).Proves(41L));
        }

        [Test]
        public void ACardBuiltFromALaterSaveIsProved()
        {
            // Another device pushed in between. The card holds that grove too, which is the
            // merge's whole promise, and the next settled sync here will re-judge it anyway.
            Assert.IsTrue(Built(42L).Proves(41L));
        }

        [Test]
        public void ACardBuiltFromAnOlderSaveIsNot()
        {
            Assert.IsFalse(Built(40L).Proves(41L));
            Assert.IsFalse(Built(0L).Proves(41L));
        }

        [Test]
        public void AServerThatDoesNotSayCannotBeHeldToAnything()
        {
            // Absent is not stale: a deployment predating the field reports nothing, and a
            // client refusing that would retry against it for the life of the account.
            var unproven = GrovePublication.Unproven;

            Assert.IsFalse(unproven.ReportsRevision);
            Assert.IsTrue(unproven.Proves(41L));
            Assert.IsTrue(new GrovePublication(GroveCard.Empty, -7L).Proves(41L));
        }

        [Test]
        public void AClientThatDidNotKnowWhatItPushedCannotHoldTheServerToIt()
        {
            // Nought means the receipt could not say. The card is taken, exactly as it was
            // before the field existed, rather than refused for a reason on this side.
            Assert.IsTrue(Built(3L).Proves(0L));
            Assert.IsTrue(Built(0L).Proves(0L));
        }

        [Test]
        public void ANullCardIsCarriedAsTheEmptyOne()
        {
            var published = new GrovePublication(null, 5L);

            Assert.IsNotNull(published.Card);
            Assert.IsFalse(published.Card.IsValid);
            Assert.AreEqual(5L, published.SaveRevision);
        }
    }
}
