using System;
using System.Threading.Tasks;
using GlimmerGrove.Async;
using GlimmerGrove.Privacy;
using GlimmerGrove.Release;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Puts the consent form and Apple's tracking prompt in front of the player on the hub,
    /// never earlier (invariant 55).
    ///
    /// <para>
    /// <b>Why the hub.</b> A new player used to meet the consent form and Apple's tracking
    /// prompt over the splash, before a single frame of the game - and a share of them left
    /// there, without reaching the tutorial. Neither dialog has to be there: the law and Apple
    /// only require that nothing which needs the answer runs before it. So the splash now asks
    /// nothing (<see cref="AdPrivacy.PrepareAsync"/>), the tutorial shows no ads, and the
    /// questions are put here, once the tutorial has handed the player to the hub. A player who
    /// has already answered both is never shown anything, because <see cref="AdPrivacy.Owed"/>
    /// is derived from the CMP's and iOS's own records and both are already closed.
    /// </para>
    /// <para>
    /// <b>A poll, for <see cref="UpdateGate"/>'s reason.</b> "Ask when the hub is presented"
    /// misses every way of getting back to an idle hub that is not a presentation - a panel
    /// closed, a sheet dismissed, a splash refresh that only finished after the hub arrived.
    /// So this asks every frame whether the moment has come, which is a handful of field reads,
    /// and the answer is a drawing of the state rather than a sequence of events to keep in
    /// step with.
    /// </para>
    /// <para>
    /// <b>The moment</b> is the hub, current and finished arriving, with nothing over it - no
    /// panel, no update wall - with the app focused (a store sheet or a system dialog of
    /// somebody else's is not ours to stack on), held for <see cref="Beat"/> so the player sees
    /// where they have landed before anything is laid over it. The same test is asked again
    /// immediately before each dialog, because the form takes a network round trip to load and
    /// the player may have tapped into a run while it did.
    /// </para>
    /// <para>
    /// <b>The same beat settles <see cref="LaunchCalm"/></b>, which is what the mediation SDK
    /// waits for before it starts. One definition of "the player has landed and is looking at
    /// the hub" for both: the dialog that must not be laid over a hub still arriving, and the
    /// native start-up that must not be paid on the frames the player is watching it arrive.
    /// </para>
    /// </summary>
    public static class ConsentMoment
    {
        /// <summary>
        /// How long the hub must have stood idle before a dialog is laid over it, in seconds.
        /// Long enough for the iris to have opened and the hub to read as a place; short enough
        /// that the form is there before the player has decided where to tap.
        /// </summary>
        const float Beat = .6f;

        /// <summary>When the hub was first seen idle in the current run of idle frames; negative when not.</summary>
        static float _idleSince = -1f;

        /// <summary>
        /// One frame. Driven from <c>Boot.Pump</c>, beside the other things that outlive any
        /// particular screen.
        /// </summary>
        public static void Tick()
        {
            if (!HubIsIdle())
            {
                _idleSince = -1f;
                return;
            }

            float now = Time.unscaledTime;

            if (_idleSince < 0f) _idleSince = now;
            if (now - _idleSince < Beat) return;

            // Idle for a beat: the launch has calmed, whether or not anything is owed.
            LaunchCalm.Settle();

            if (!AdPrivacy.Owed) return;

            _idleSince = -1f;
            Begin();
        }

        /// <summary>
        /// The player is standing on the hub with nothing in front of it. Asked by the poll and
        /// again by <see cref="AdPrivacy.AskAsync"/> before each dialog.
        /// </summary>
        static bool HubIsIdle()
            => Flow.Current is HomeScreen
            && !Flow.Busy
            && !Flow.Covered
            && !ReleaseGate.IsShut
            && Application.isFocused;

        /// <summary>
        /// Starts the hub's step and observes it. <see cref="AdPrivacy.AskAsync"/> never throws
        /// by contract, but an unobserved faulted task is how a failure on this path once became
        /// invisible (see <c>RewardedAds.BeginStart</c>), so the continuation is awaited rather
        /// than discarded. <see cref="AdPrivacy.Owed"/> is false while it runs, so the poll
        /// cannot start a second one.
        /// </summary>
        static void Begin()
        {
            _ = Asked();

            static async Task Asked()
            {
                try
                {
                    await AdPrivacy.AskAsync(HubIsIdle);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Privacy] asking on the hub failed: {e}");
                }
            }
        }
    }
}
