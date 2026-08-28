using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// That every run gets a frame, and that no mode can take one for itself.
    ///
    /// <para>
    /// <b>The rule this file guards was opt-in for a year and nobody noticed.</b> Whether a run
    /// may advance at all — the board is built, nothing is over it, the opening transition is
    /// done, no lesson is being read — used to be asked by each mode calling
    /// <c>RunScreen.Tick</c> from its own <c>Update</c>. Three modes out of four never called
    /// it. Nothing broke, because each of them latched its own board for its own reasons, but
    /// the guarantee the funnel exists to give was being given by one screen out of four, and
    /// two of the three would accept input while the iris was still opening — long enough to
    /// commit a run, and be charged a heart for it, before the player had seen the board.
    /// </para>
    /// <para>
    /// <b>Most of the fix is the compiler's.</b> <c>Runnable</c> and <c>Running</c> are abstract
    /// and <c>Tick</c> is private, so a mode cannot decline to answer and cannot advance itself.
    /// A default would have left the hole exactly where it was: a mode that overrode nothing
    /// would compile, run, and opt out in silence.
    /// </para>
    /// <para>
    /// <b>What is left is the half no language can express.</b> Unity dispatches <c>Update</c>
    /// to the most-derived declaration only, so a mode that declares one silently replaces
    /// <c>RunScreen</c>'s and takes the run's frame with it — no error, no warning, and a board
    /// that simply never hears whether it is allowed to run. That is the same hazard two members
    /// sharing a name caused when <c>ModeScreen</c>'s <c>Resolve</c> coroutine hid the stake's
    /// <c>Resolve</c> and a won grove was charged for at the next launch. This is the check for
    /// it, and it is written the way <c>RunStakeTests</c> is: by reflection over what the
    /// assembly actually declares, because the point is to catch a mode nobody thought about.
    /// </para>
    /// </summary>
    public sealed class RunFrameTests
    {
        /// <summary>Every concrete run screen that ships, found rather than listed.</summary>
        static IEnumerable<Type> Screens()
        {
            foreach (var type in typeof(RunScreen).Assembly.GetTypes())
                if (typeof(RunScreen).IsAssignableFrom(type) && !type.IsAbstract)
                    yield return type;
        }

        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public |
                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        [Test]
        public void ThereAreRunScreensToCheck()
        {
            var found = new List<Type>(Screens());

            Assert.GreaterOrEqual(found.Count, 4,
                                  "this check found " + found.Count + " run screen(s), which " +
                                  "means it has stopped looking in the right place and would " +
                                  "pass whatever was there");
        }

        /// <summary>
        /// The one that cannot be a compiler error, and the whole reason this file exists.
        /// </summary>
        [Test]
        public void NoModeDeclaresItsOwnUpdate()
        {
            foreach (var screen in Screens())
            {
                var update = screen.GetMethod("Update", Declared, null, Type.EmptyTypes, null);

                Assert.IsNull(update,
                              screen.Name + " declares its own Update, which Unity dispatches " +
                              "*instead of* RunScreen's — so this run never hears whether it is " +
                              "allowed to advance. Override Running(bool) instead; it is called " +
                              "every frame with the answer.");
            }
        }

        /// <summary>
        /// A mode cannot advance its own run, because it cannot reach the method that would let
        /// it. Stated here as well as enforced by the language, so that widening it later is a
        /// failing test rather than a quiet convenience.
        /// </summary>
        [Test]
        public void NothingOutsideTheBaseCanAdvanceARun()
        {
            var tick = typeof(RunScreen).GetMethod("Tick", Declared);

            Assert.IsNotNull(tick, "RunScreen.Tick has been renamed or removed");
            Assert.IsTrue(tick.IsPrivate,
                          "Tick is reachable from a mode again, which is what made this rule " +
                          "opt-in the first time");
        }

        [Test]
        public void EveryRunScreenAnswersBothHalvesOfTheFrame()
        {
            foreach (var screen in Screens())
            {
                var runnable = screen.GetProperty("Runnable",
                                                  BindingFlags.Instance | BindingFlags.Public |
                                                  BindingFlags.NonPublic);

                var running = screen.GetMethod("Running",
                                               BindingFlags.Instance | BindingFlags.Public |
                                               BindingFlags.NonPublic);

                Assert.IsNotNull(runnable, screen.Name + " has no Runnable");
                Assert.IsNotNull(running, screen.Name + " has no Running");

                Assert.IsFalse(runnable.GetMethod.IsAbstract,
                               screen.Name + " never says whether it may run");
                Assert.IsFalse(running.IsAbstract,
                               screen.Name + " is never told whether it is running");
            }
        }
    }
}
