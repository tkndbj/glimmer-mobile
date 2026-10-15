using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The phoenix's rise (<see cref="SiegeBoosts.Phoenixes"/>, the Shuffle lane's legendary
    /// <c>phoenix</c> card): the whole line stands back up at full health, the hill is thrown
    /// back off it, and for <see cref="SiegeTuning.PhoenixMantle"/> seconds nothing reaches a
    /// post.
    ///
    /// <para>
    /// <b>Why a rise needs both halves.</b> A line raised at the moment it fell is raised under
    /// the bodies that felled it, every one already at the line with its blow wound up, so the
    /// bare revival bought a second or two and then the same loss. The push takes the bodies off
    /// the line, and they put their weapons down while they walk back (<c>AtTheLine</c> reads the
    /// march). The mantle covers the posts while the player re-fuels them. Both are bounded:
    /// the push is a fraction of the hill and the mantle is a fixed time, and neither repeats,
    /// because the phoenix itself is spent once.
    /// </para>
    /// <para>
    /// <b>The mantle is one clock on the board</b>, for the roar's and the hourglass's reason: no
    /// post holds a copy, so nothing can stay covered after the window closes, and a second rise
    /// inside the first <em>extends</em> it rather than stacking.
    /// </para>
    /// <para>
    /// <b>It turns a hit away where the hit starts, not where it lands.</b> A blow, a boss's
    /// spell and a roar each ask <see cref="Mantled"/> before they do anything to a post, so a
    /// covered post is never doused, chained, sealed or sundered either, and each refusal is
    /// reported (<see cref="SiegeReport.Turned"/>) so the view can draw the hit landing on the
    /// mantle. <see cref="Bear"/> and <see cref="Topple"/> ask it as well, as a backstop, so a
    /// future way of hurting the line is covered even if it never asks first.
    /// </para>
    /// <para>
    /// <b>The plain line never reaches here</b>: <see cref="Revive"/> returns on the identity
    /// build, so the clock is nought on every chapter and on the Infinite lane, bit for bit
    /// (<c>ShuffleTests.TheIdentityBoostChangesNothing</c>). It draws nothing from either random
    /// stream.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>Seconds the phoenix's mantle has left over the line, or nought.</summary>
        float _mantle;

        /// <summary>Whether the phoenix's mantle is over the line right now.</summary>
        public bool Mantled => _mantle > 0f;

        /// <summary>Seconds the mantle has left, for the view. Nought when it is down.</summary>
        public float MantleLeft => _mantle;

        /// <summary>
        /// Runs the mantle down. <b>First in the step</b>, before anything can raise it, so a
        /// rise that happens this step keeps every one of its seconds and the frame the clock
        /// reaches nought is the first frame a post can be hurt again.
        /// </summary>
        void Wear(float dt)
        {
            if (_mantle > 0f) _mantle = Math.Max(0f, _mantle - dt);
        }

        /// <summary>
        /// The phoenix's rise: the whole line at full health, exactly as a continue raises it
        /// (<see cref="Rally"/>), then the hill thrown back and the mantle put on.
        ///
        /// <para>
        /// <b>The push is the anvil's own shove</b> (<see cref="SiegeRaider.Shove"/>), so a boss
        /// and a body still in the wings are refused for the anvil's reasons, and the body slides
        /// back over the model's frames at <see cref="SiegeTuning.AnvilPace"/> instead of
        /// jumping. It is not reported as an anvil (<see cref="SiegeReport.Heaved"/>), because
        /// the view draws an anvil's throw as amber dust, a reversed dial and a slow-motion beat,
        /// and none of that is the phoenix.
        /// </para>
        /// </summary>
        void Rise()
        {
            Rally(_wards.Length);
            for (int w = 0; w < _wards.Length; w++) _report.Revived.Add(w);

            int shoved = 0;
            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Shove(SiegeTuning.PhoenixPush)) shoved++;

            _mantle = Math.Max(_mantle, SiegeTuning.PhoenixMantle);

            _report.Mantled = _mantle;
            _report.Repelled = shoved;
        }

        /// <summary>
        /// Answers whether the mantle turned away a hit aimed at post <paramref name="ward"/>,
        /// and reports it if so. Every door a hit comes through asks this before it touches the
        /// post.
        /// </summary>
        bool Turned(int ward)
        {
            if (_mantle <= 0f) return false;

            _report.Turned.Add(ward);
            return true;
        }
    }
}
