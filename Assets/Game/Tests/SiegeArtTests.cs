using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That each of the eight bosses is drawn as <b>itself</b>.
    ///
    /// <para>
    /// <b>Invariant 37z has always had two halves and only one of them was checked.</b> "A boss is
    /// a way of fighting, and telling two apart by a hue is not telling them apart at all" is
    /// enforced on the <em>rules</em> by `SiegeRuleTests` — each boss takes something different —
    /// and on the <em>bodies</em> by `SiegeCastTests`, which refuses two casts sharing a reel. The
    /// third half is the spell, and nothing asked about it. Three of the eight shipped sharing one
    /// pair of reels: a warbringer's roar, worn by a gravemaw in green and a bonecaller in white.
    /// </para>
    /// <para>
    /// <b>It is the cheapest possible check and it is the only one that could have seen it.</b>
    /// Every other gate in this project passes a shared reel with nothing to say, and passes it
    /// honestly: the address is real, it is registered, `AddressableAudit` finds its asset,
    /// `artnames.py` resolves it, the scope loads it and the board draws it. It is simply the
    /// wrong picture, and "wrong picture" is only ever visible as a *collision* or to an eye.
    /// A collision is what a computer can see.
    /// </para>
    /// <para>
    /// <b>Addresses and never sprites</b>, which is `SiegeCastTests`' bargain and for its reason:
    /// nothing is loaded in an offline run, so a sprite lookup answers null whatever the table
    /// says, and a check that cannot fail is not a check. That the pictures behind these addresses
    /// are pictures rather than threads is `Tools/verify/fxreels.py`, which measures them; that
    /// they read as what they are meant to be is a render and a pair of eyes (invariant 32b).
    /// </para>
    /// </summary>
    public sealed class SiegeArtTests
    {
        /// <summary>
        /// Every boss the mode ships, taken from the one table that decides what a chapter may
        /// author rather than from a list written out here.
        ///
        /// <b>A ninth boss has to arrive in this fixture by itself</b>, or the fixture is a
        /// record of what somebody remembered in 2026 and a new boss can share reels freely —
        /// which is exactly how the last two got here.
        /// </summary>
        static IEnumerable<SiegeKind> Bosses()
        {
            for (int i = 0; i < SiegeLayout.BossNames.Length; i++)
                yield return SiegeLayout.BossNames[i].Kind;
        }

        static List<AssetRequest> ScopeOf(SiegeKind kind)
        {
            var into = new List<AssetRequest>();
            SiegeMode.Bosses(kind, into);
            return into;
        }

        /// <summary>
        /// The one that shipped: no two bosses may name the same picture.
        ///
        /// <b>Over the whole scope rather than over the spell reels alone</b>, because a body and
        /// a spell fail the same way and a boss is only ever met once — a player who fights a
        /// gravemaw on rung five and a bonecaller on rung ten has no way to compare them side by
        /// side, so a shared reel does not read as a bug. It reads as the game having one boss.
        /// </summary>
        [Test]
        public void EveryBossSpellIsItsOwnDrawing()
        {
            var owner = new Dictionary<string, SiegeKind>();

            foreach (var kind in Bosses())
            {
                foreach (var want in ScopeOf(kind))
                {
                    if (!owner.TryGetValue(want.Address, out var first))
                    {
                        owner[want.Address] = kind;
                        continue;
                    }

                    if (WearsBorrowedBody(kind, first, want.Address)) continue;

                    Assert.Fail(
                        $"{kind} and {first} are both drawn with '{want.Address}'. A boss is a " +
                        "way of fighting and two of them separated by a hue are one boss " +
                        "(invariant 37z). Give it a reel of its own in SiegeShotBake and scope " +
                        "that one in SiegeMode.Bosses.");
                }
            }
        }

        /// <summary>
        /// <b>The seventh chapter's two bosses wear the first chapter's bodies</b>, at the
        /// owner's instruction on 2026-09-22 — their own cut was drawn side-on. Only the body
        /// and its cast reel are shared, and only between these two pairs by name; each spell is
        /// still its own drawing, which is what the rule above is about.
        /// </summary>
        static bool WearsBorrowedBody(SiegeKind kind, SiegeKind first, string address)
        {
            bool Pair(SiegeKind a, SiegeKind b, string stem) =>
                ((kind == a && first == b) || (kind == b && first == a))
                && (address == AssetManifest.SiegeArt(stem) || address == AssetManifest.SiegeArt(stem + "_cast"));

            return Pair(SiegeKind.Harrower, SiegeKind.Boss, "boss")
                || Pair(SiegeKind.Hollowking, SiegeKind.Overlord, "over");
        }

        /// <summary>
        /// And every boss is drawn by <b>something</b>: an arm that names nothing is an arm
        /// nobody wrote.
        ///
        /// <b>`Bosses` is a switch with no `default`</b>, so a kind it has never heard of leaves
        /// the list untouched and the boss walks onto the hill as four white rectangles
        /// (invariant 7b). That failure is silent, it is per chapter, and it cannot happen in the
        /// Editor, because the Editor does not route StreamingAssets the way Android does.
        /// </summary>
        [Test]
        public void EveryBossScopesArtOfItsOwn()
        {
            foreach (var kind in Bosses())
            {
                var scope = ScopeOf(kind);

                Assert.That(scope, Is.Not.Empty,
                            $"{kind} scopes no art at all - SiegeMode.Bosses has no arm for it.");

                // A body, a cast and the two ends of a spell is the floor. Every boss has at
                // least that; the ones that throw something have a fifth.
                Assert.That(scope.Count, Is.GreaterThanOrEqualTo(4),
                            $"{kind} scopes only {scope.Count} reels.");
            }
        }

        /// <summary>
        /// Every boss is classified by <b>both</b> of the two rules the drawing asks, and the two
        /// are not the same rule.
        ///
        /// <para>
        /// <b>A warbringer aims at no ward and reaches every one of them</b>, which is the whole
        /// gap between <see cref="SiegeTuning.AimsAtAWard"/> and
        /// <see cref="SiegeTuning.ReachesTheLine"/> — one boss, and the one that used to be the
        /// sole named case in every clause that needed either of them. Writing the pair down here
        /// is what stops a ninth boss being classified by whichever clause it happens to fall
        /// through: `Leaving` asks the first (does it lay its muzzle flat on the ground?) and
        /// `Aftermath` asks the second (is there a post to paint?), and a kind that answers the
        /// wrong one draws a ground wash standing upright in the air or throws lightning between
        /// four turrets nothing touched. Both have shipped.
        /// </para>
        /// <para>
        /// <b>It asserts the shape rather than the table</b>: aiming at a ward implies reaching
        /// the line, and that implication is the thing that must never invert. The one boss that
        /// reaches without aiming is named, because a rule with no exception does not need two
        /// predicates.
        /// </para>
        /// </summary>
        [Test]
        public void EveryBossIsClassifiedByBothAimingRules()
        {
            var reachWithoutAiming = new List<SiegeKind>();

            foreach (var kind in Bosses())
            {
                bool aims = SiegeTuning.AimsAtAWard(kind);
                bool reaches = SiegeTuning.ReachesTheLine(kind);
                bool carries = SiegeTuning.CarriesAWard(SiegeTuning.SpellOf(kind));

                Assert.That(!aims || reaches, Is.True,
                            $"{kind} is aimed at a ward but does not reach the line, which cannot " +
                            "be true of anything: the ward it is aimed at is on the line.");

                // The third predicate is the board's, and it is held to the other two: a cast
                // carries a ward exactly when it is aimed at one, or when it never reaches the
                // line and so takes the freshest for its smite (a devour, a raise). What reaches
                // the line without aiming — a roar, a wane — is booked per post at the landing
                // and carries nothing. A spell that breaks this is one `SiegeBoard.Wanted` will
                // be asked about with no arm to answer, which is what the wane was for a chapter.
                Assert.That(carries, Is.EqualTo(aims || !reaches),
                            $"{kind} {(carries ? "carries" : "carries no")} ward, but " +
                            $"{(aims ? "aims at one" : "aims at none")} and " +
                            $"{(reaches ? "reaches the line" : "never reaches the line")}.");

                if (!aims && reaches) reachWithoutAiming.Add(kind);
            }

            // Named rather than counted. The count was 1 for six chapters and the seventh made
            // it 2 — and a count that moved was a warning about every clause that said "the
            // warbringer", which is what an audit of those clauses found (`SiegeBoard.Clock`
            // handed the wane a ward it never read). A list names the exceptions so that a
            // third spell of this shape fails by name, and its author reads this before
            // touching the board.
            Assert.That(reachWithoutAiming,
                        Is.EquivalentTo(new[] { SiegeKind.Warbringer, SiegeKind.Hollowking }),
                        "The bosses that reach the line without aiming at a ward are the " +
                        "warbringer (a roar, one record per post) and the hollowking (a wane, one " +
                        "record per idle post). A third means a new spell has been given that " +
                        "shape: read `SiegeTuning.CarriesAWard` and every clause that names those " +
                        "two before adding it here.");
        }

        /// <summary>
        /// A boss that throws something loads a flight, and a boss that does not, does not.
        ///
        /// <b>Both directions, because both have cost something.</b> A missing flight is a white
        /// rectangle crossing the hill; an unused one is a reel addressed frame by frame with no
        /// label, impossible to load as a reel at all, and built into a bundle for the life of
        /// the game — which is what `roar` was until the audit reported it as dead weight in as
        /// many words. `SiegeTuning.AimsAtAWard` is the one place that rule lives.
        /// </summary>
        [Test]
        public void OnlyABossThatThrowsSomethingLoadsAFlight()
        {
            foreach (var kind in Bosses())
            {
                bool flies = SiegeTuning.AimsAtAWard(kind);

                // A flight's address is the bare key; its companions carry a suffix. Naming the
                // pair is what tells "hex" from "hex_muzzle" without this fixture holding a
                // second copy of the table.
                int found = 0;

                foreach (var want in ScopeOf(kind))
                {
                    if (!want.Address.Contains("/Fx/")) continue;
                    if (want.Address.EndsWith("_muzzle") || want.Address.EndsWith("_hit")) continue;

                    found++;
                }

                Assert.That(found, Is.EqualTo(flies ? 1 : 0),
                            $"{kind} aims at a ward: {flies}, but scopes {found} flight reels.");
            }
        }
    }
}
