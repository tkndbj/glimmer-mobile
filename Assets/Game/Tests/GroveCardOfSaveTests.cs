using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The card a save file describes, which is what decides whether a publish is owed.
    ///
    /// <para>
    /// Two things are under contract. The fingerprint must follow exactly what a visitor can
    /// see and nothing else — a star or a heart moving must not cost a publish, and a new best
    /// wave must — and a save and the ledgers it loads into must describe <em>the same</em>
    /// card, because the request is judged from the file and the player's own screen is drawn
    /// from the ledgers, and a disagreement between them is a card that publishes on every
    /// sync or never.
    /// </para>
    /// <para>
    /// <b>Most of this fixture used to be about the grove</b> — placements, land, worth and the
    /// hall's seat — and went with the Grovement on 2026-09-21. The two rules above did not,
    /// so they are asked here of what a card still carries: the name, the wave, the badge and
    /// the turret line.
    /// </para>
    /// </summary>
    public sealed class GroveCardOfSaveTests
    {
        // ------------------------------------------------------------- fixtures
        static SaveFileDto Save()
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                wallet = new WalletDto { displayName = "Fern", avatarId = "monarch" },
                endlessBest = new[]
                {
                    new EndlessBestDto { level = "s02_endlesswatch", wave = 40, waves = 200 },
                },
                wardLoadout = new[]
                {
                    new WardSlotDto { colour = "r", ward = "ember" },
                },
                wardStars = new[]
                {
                    new WardStarDto { ward = "ember:r", stars = 3 },
                },
                levels = new LevelRecordDto[0],
            };

        static string Print(SaveFileDto save)
            => GroveCard.OfSave(save, "uid", 3, 1_000L).Fingerprint();

        // ================================================================= tests
        [Test]
        public void TheFingerprintFollowsWhatAVisitorCanSee()
        {
            string baseline = Print(Save());

            // The name is drawn on every row.
            var renamed = Save();
            renamed.wallet.displayName = "Bramble";
            Assert.AreNotEqual(baseline, Print(renamed), "a rename is visible");

            // The wave is what the endless board is ordered on. Without it in the hash, a
            // keeper could hold out further than anybody alive and never reach the board.
            var further = Save();
            further.endlessBest[0].wave = 55;
            Assert.AreNotEqual(baseline, Print(further), "a new best is visible");

            // The turret line is drawn on a public profile.
            var moved = Save();
            moved.wardLoadout[0].ward = "pyre";
            Assert.AreNotEqual(baseline, Print(moved), "a change of turret is visible");

            // And how far it has been taken, which is drawn beside it.
            var upgraded = Save();
            upgraded.wardStars[0].stars = 5;
            Assert.AreNotEqual(baseline, Print(upgraded), "a turret's rung is visible");
        }

        [Test]
        public void TheFingerprintIgnoresWhatAVisitorCannotSee()
        {
            string baseline = Print(Save());

            var played = Save();
            played.levels = new[] { new LevelRecordDto { levelId = "c01_first_light", stars = 3 } };
            played.wallet.heartsProduced = 40L;
            played.wallet.heartsSpent = 12L;
            played.updatedUnix = 9_999_999L;
            played.cloud = new CloudStateDto { revision = 77L, userId = "uid" };

            Assert.AreEqual(baseline, Print(played));

            // Nor the keeper level: it is drawn on the card, and it moves with every star,
            // so fingerprinting it would be a publish per session for a number the ranking
            // job does not read. It reaches the board with the next real change.
            Assert.AreEqual(baseline, GroveCard.OfSave(Save(), "uid", 9, 1_000L).Fingerprint());
        }

        [Test]
        public void AnUnnamedKeeperReadsTheDefaultName()
        {
            var save = Save();
            save.wallet.displayName = string.Empty;

            // The wallet shows the default and never stores it (invariant 11c); the file
            // reading has to show the same thing, or an unnamed keeper publishes twice.
            var named = Save();
            named.wallet.displayName = Wallet.DefaultName;

            Assert.AreEqual(Print(named), Print(save));
        }

        [Test]
        public void ACardCarriesTheSavesLineAndWave()
        {
            var card = GroveCard.OfSave(Save(), "uid", 3, 1_000L);

            Assert.AreEqual(40, card.BestWave, "the lifetime tally is not the best wave");
            Assert.IsTrue(card.HasLine);
            Assert.AreEqual(3, card.StarsOn('r'));
            Assert.AreEqual("Fern", card.Name);
        }

        [Test]
        public void AnEmptySaveIsWorthNothingAndBreaksNothing()
        {
            var card = GroveCard.OfSave(new SaveFileDto(), "uid", 1, 1_000L);

            Assert.AreEqual(0, card.BestWave);
            Assert.IsFalse(card.HasLine);
            Assert.IsFalse(GrovePublishPolicy.WorthPublishing(card));

            Assert.DoesNotThrow(() => GroveCard.OfSave(null, "uid", 1, 1_000L));
        }
    }
}
