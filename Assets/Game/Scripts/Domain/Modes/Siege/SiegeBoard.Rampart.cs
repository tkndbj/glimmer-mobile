using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The wall (<c>UtilityKind.Wall</c>): a straight wall across the whole width of the hill,
    /// on the near edge of the band the player tapped, standing for the item's magnitude.
    ///
    /// <para>
    /// <b>It holds rather than pushes.</b> A body behind the wall when it rises - and everything
    /// still mustering at the crest - walks down to it and stops (<see cref="Held"/>); a body
    /// already past it walks on. Nothing is moved backwards, so nothing a firepot was aimed at
    /// jumps, and a body at the line keeps swinging: the wall answers what is <em>coming</em>.
    /// </para>
    /// <para>
    /// <b>A boss is never held</b>, for <see cref="SiegeRaider.Shove"/>'s reason and the
    /// 2026-10-05 ruling: a boss cannot be hurt until it reaches its ground, so a wall in its way
    /// would be the player's own damage gated. A body a gravity well drags is moved by the
    /// player's other purchase and is not fought over; once let go it is held again only if it
    /// was left behind the wall.
    /// </para>
    /// <para>
    /// <b>One clock on the board</b>, for the roar's and the hourglass's reason: nothing can be
    /// left standing at a wall that has fallen, and a second wall is refused while one stands
    /// (<see cref="Walled"/>), as a second well is. It hurts nothing and draws from neither random
    /// stream (41), so it is charged no matches (39). Nought on every board nobody raised one on,
    /// so every other board plays bit for bit as it did.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>Seconds the wall has left, or nought when none stands.</summary>
        float _rampart;

        /// <summary>Which aiming band the standing wall was raised on.</summary>
        int _rampartRow;

        /// <summary>Whether a wall stands on the hill right now.</summary>
        public bool Walled => _rampart > 0f;

        /// <summary>Seconds the wall has left, for the view. Nought when none stands.</summary>
        public float WallLeft => _rampart;

        /// <summary>The band the standing wall was raised on, or -1 when none stands.</summary>
        public int WallRow => _rampart > 0f ? _rampartRow : -1;

        /// <summary>
        /// How many bodies a wall on band <paramref name="row"/> would hold right now: every
        /// living raider that is not a boss and has not yet walked past where it would stand.
        /// </summary>
        public int Holdable(int row)
        {
            if (row < 0 || row >= SiegeTuning.BlastRows) return 0;

            float at = SiegeTuning.WallMarch(row);
            int n = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (raider.Alive && !raider.Boss && raider.March < at) n++;
            }

            return n;
        }

        /// <summary>
        /// Raises a wall on band <paramref name="row"/> for <paramref name="tenths"/> tenths of a
        /// second, and answers how many bodies it holds. Nought - and nothing raised - when a wall
        /// already stands, the band is not on the hill, or there is nothing it could hold.
        /// </summary>
        public int Wall(int row, int tenths)
        {
            if (tenths <= 0 || _rampart > 0f) return 0;

            int held = Holdable(row);
            if (held <= 0) return 0;

            _rampartRow = row;
            _rampart = tenths / 10f;

            return held;
        }

        /// <summary>
        /// Keeps one body that was behind the standing wall behind it, after this step's walk.
        /// <paramref name="before"/> is where it stood before the step: a body at or past the
        /// wall walks on, and a held one is never moved backwards.
        /// </summary>
        void Held(SiegeRaider raider, float before)
        {
            if (_rampart <= 0f || raider.Boss) return;

            float at = SiegeTuning.WallMarch(_rampartRow);
            if (before >= at) return;

            float stop = Math.Max(before, at - SiegeTuning.WallStand);
            if (raider.March > stop) raider.March = stop;
        }
    }
}
