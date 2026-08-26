using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That the price of walking away from a run is written down in exactly one place.
    ///
    /// <para>
    /// <b>This suite exists because the alternative shipped.</b> Every mode used to carry its own
    /// <c>Commit</c>, <c>Resolve</c>, <c>Forfeit</c> and <c>ConfirmForfeit</c> — four
    /// near-identical methods about charging a player a heart. They drifted, as copies do: one
    /// guarded a closing cascade and the other did not. Then Lightweave's restart simply never
    /// called its copy, so a restart there was free — and since a restart also deals a fresh pot
    /// of ink, the mode's entire fail state could be walked out of for nothing.
    /// </para>
    /// <para>
    /// <b>Nothing could have caught it except playing the game.</b> It compiled, it validated,
    /// 1,206 tests passed, and the rule it broke was not written anywhere: it was in two places
    /// and missing from a third. What follows is the cheapest guard that would have failed —
    /// not a test of what a restart costs, which needs a screen, but a test that no mode is in a
    /// position to have an opinion about it.
    /// </para>
    /// <para>
    /// Reflection rather than a compile-time trick, because C# has no way to say "no subclass
    /// may declare a member by this name". It reads types only; nothing here builds a screen.
    /// </para>
    /// </summary>
    public sealed class RunStakeTests
    {
        /// <summary>
        /// The stake's members. A mode declaring any of these is a second copy of the heart
        /// accounting, whatever it happens to do.
        ///
        /// <para>
        /// The <c>Continue*</c> names are the same rule read from the other end: a continue is
        /// the one way out of a run that takes money instead of a heart, and two copies of it
        /// would be two prices, two idempotency keys and two chances to charge somebody for a
        /// board that was still lost. <c>Continue</c> and <c>Teaching</c> are the collaborators
        /// that own those rules, and a mode declaring either would <em>hide</em> the real one
        /// rather than replace it — the <c>ModeScreen.Resolve</c> trap, which compiled fine and
        /// would have charged a won grove for again at the next launch.
        /// </para>
        /// <para>
        /// What a mode <em>is</em> expected to declare — <c>MeasuredIn</c>,
        /// <c>ContinueDeficit</c>, <c>ContinueWith</c> — is deliberately absent from this list:
        /// those are the questions only the mode can answer, and none of them touches a price.
        /// </para>
        /// <para>
        /// <c>Committed</c> and <c>Staked</c> are the two facts the exits are priced from — has
        /// this run been paid for, and is it one that costs anything at all (a mode's free
        /// opening is not; see <c>HeartStake</c>). They are read by the modes and written only
        /// here, so a mode declaring either would shadow the answer rather than change it, and
        /// every exit would silently price itself from a field nothing sets.
        /// </para>
        /// </summary>
        static readonly string[] StakeMembers =
        {
            "Commit", "Resolve", "Forfeit", "ConfirmForfeit", "RestartLevel",
            "LeaveToMap", "LeaveToHome",
            "Committed", "Staked",
            "Continue", "Teaching",
            "LoseOrContinue", "OfferOrLose", "DoneDeciding", "ResetContinues",
        };

        static IEnumerable<Type> Modes
            => typeof(RunScreen).Assembly.GetTypes()
                                .Where(t => t.IsSubclassOf(typeof(RunScreen)));

        [Test]
        public void EveryModeInheritsTheStakeRatherThanCarryingOne()
        {
            var found = new List<string>();

            foreach (var mode in Modes)
                foreach (var member in mode.GetMembers(BindingFlags.Instance | BindingFlags.Public
                                                       | BindingFlags.NonPublic
                                                       | BindingFlags.DeclaredOnly))
                {
                    if (member is Type) continue;
                    if (Array.IndexOf(StakeMembers, member.Name) < 0) continue;

                    // A mode may still *say* what leaving means for its own board when the base
                    // asks it to — those hooks are named differently on purpose (Rewind,
                    // RunOver, NoteAbandoned, StakeLevel) so the two cannot be confused.
                    found.Add($"{mode.Name}.{member.Name}");
                }

            CollectionAssert.IsEmpty(found,
                "these modes declare part of the stake themselves, which is a second copy of a "
                + "rule about charging players a heart (invariant 9a). Lightweave's restart was "
                + "free for a whole session because of exactly this: " + string.Join(", ", found));
        }

        [Test]
        public void EveryModeSaysHowToPutItsBoardBackAndWhenItsRunIsOver()
        {
            // The other half: the base can only price an exit if every mode fills in the hooks
            // it prices them with. An abstract member cannot be forgotten, so this is really a
            // guard against one being quietly relaxed to virtual with a do-nothing default —
            // which would put a mode back in the position of deciding by omission.
            foreach (var name in new[] { "Rewind", "RunOver", "NoteAbandoned", "StakeLevel" })
            {
                var member = typeof(RunScreen)
                    .GetMember(name, BindingFlags.Instance | BindingFlags.NonPublic
                                     | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .FirstOrDefault();

                Assert.IsNotNull(member, $"RunScreen no longer asks a mode for '{name}'");

                bool isAbstract = member is MethodInfo m ? m.IsAbstract
                                : member is PropertyInfo p && (p.GetMethod?.IsAbstract ?? false);

                Assert.IsTrue(isAbstract,
                    $"RunScreen.{name} is no longer abstract, so a mode can inherit a default "
                    + "answer about its own stake rather than being made to give one");
            }
        }

        /// <summary>
        /// The two rules a run carries that are not the stake live in classes of their own, and
        /// a mode reaches them only through the screen.
        ///
        /// <para>
        /// It is a guard against the drift that made this refactor necessary rather than a test
        /// of behaviour: <c>RunScreen</c> had grown the stake, the hold, the lesson sequence,
        /// the review key and the continue offer, at which point no single rule in it could be
        /// changed without reading all five. If either collaborator is folded back in, this
        /// fails and says why.
        /// </para>
        /// </summary>
        [Test]
        public void TeachingAndTheContinueOfferAreCollaboratorsRatherThanMoreOfTheScreen()
        {
            foreach (var name in new[] { "Teaching", "Continue" })
            {
                var member = typeof(RunScreen)
                    .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic
                                       | BindingFlags.Public | BindingFlags.DeclaredOnly);

                Assert.IsNotNull(member,
                    $"RunScreen no longer holds a '{name}' collaborator; if its work has moved "
                    + "back into the screen, that is the five-responsibility base class this "
                    + "split exists to prevent");
            }

            Assert.AreEqual(typeof(RunLessons),
                            typeof(RunScreen).GetProperty("Teaching",
                                BindingFlags.Instance | BindingFlags.NonPublic)?.PropertyType);

            Assert.AreEqual(typeof(RunContinueFlow),
                            typeof(RunScreen).GetProperty("Continue",
                                BindingFlags.Instance | BindingFlags.NonPublic)?.PropertyType);
        }

        [Test]
        public void ARestartCannotBeOverriddenByAMode()
        {
            // The specific hole that shipped. RestartLevel is where the price is applied, so a
            // mode that could replace it could replace the price with nothing — which is what
            // Lightweave did.
            var restart = typeof(RunScreen).GetMethod("RestartLevel",
                                                      BindingFlags.Instance | BindingFlags.Public);

            Assert.IsNotNull(restart, "RunScreen no longer offers a restart");
            Assert.IsFalse(restart.IsVirtual,
                           "RestartLevel is virtual again, so a mode can quietly ship a free one");
        }
    }
}
