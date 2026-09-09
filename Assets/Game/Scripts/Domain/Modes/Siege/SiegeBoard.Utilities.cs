using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What the action bar spends into the board: a firepot into one box of the hill, a storm
    /// over all of it, a mending into a ward, a surge of fuel.
    ///
    /// <para>
    /// <b>Every one of them reports what it actually did</b>, because that is what the run is
    /// charged for (invariant 39): damage <em>absorbed</em> rather than offered, health really
    /// restored rather than poured at a full ward.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        // ------------------------------------------------------------------ utilities
        /// <summary>
        /// Burns everything standing in one box of the hill.
        ///
        /// <para>
        /// <b>A box rather than a radius, and that is a change of kind rather than of degree.</b>
        /// A blast used to take everything within a distance of the point a finger left; exact in
        /// the rule, and on the screen it asked the player to judge a radius against raiders that
        /// were moving. The hill is a grid now (<see cref="SiegeTuning.BlastRows"/> bands by
        /// <see cref="SiegeTuning.Lanes"/> lanes), the boxes are drawn, and a firepot takes
        /// exactly what is standing in the one that was tapped - which is invariant 33g's rule at
        /// its strongest, because the drawn thing and the played thing are now the same integers.
        /// </para>
        /// <para>
        /// <b>It reports damage <em>absorbed</em>, not damage offered</b>, and that number is
        /// what the run is charged for (invariant 39). Overkill on a raider with three health
        /// left is not work the player was spared, so charging for it would price a firepot
        /// above what it saved - safe, but wrong in a way the player would feel.
        /// </para>
        /// <para>
        /// A raider still walking on (<c>Wait &gt; 0</c>) is untouched: it is not on the hill
        /// yet, so it is not drawn there, and burning something the player cannot see is the
        /// class of fault invariant 32c refuses.
        /// </para>
        /// </summary>
        public int Blast(int lane, int row, int damage, List<SiegeStrike> into)
        {
            if (damage <= 0) return 0;
            if (lane < 0 || lane >= SiegeTuning.Lanes) return 0;
            if (row < 0 || row >= SiegeTuning.BlastRows) return 0;

            int absorbed = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                if (raider.Lane != lane) continue;

                // **What its body covers, not the box its feet are in.** A boss is three cells of
                // silhouette on a four-row hill, so aiming at the thing you can see and missing is
                // the whole of the "bombs don't hit bosses" report.
                if (!SiegeTuning.Caught(raider.Kind, raider.March, row)) continue;

                int took = damage < raider.Health ? damage : raider.Health;
                absorbed += took;

                raider.Health -= took;
                raider.Flash = .18f;

                bool killed = Fell(raider);

                into?.Add(new SiegeStrike(raider.Id, took, killed));
            }

            // Felled raiders are cleared at the top of the next Advance, exactly as a bolt's
            // are, so the view sees them one last time and can play the death it was handed.
            return absorbed;
        }

        /// <summary>
        /// Strikes <b>everything on the hill</b> for the same damage, and answers how much was
        /// absorbed.
        ///
        /// <para>
        /// <b>The strikes are shuffled, and they are shuffled off the board's own stream.</b> The
        /// view draws them one at a time down the list, so the order is what a player sees — bolts
        /// falling here and there across the hill rather than a tidy sweep left to right, which
        /// reads as a list being processed. It comes from <see cref="Next"/> rather than from
        /// <c>Random</c> because everything else this mode deals does (invariant 37e): two devices
        /// on the same board see the same storm, so a report about one is a report somebody else
        /// can meet.
        /// </para>
        /// <para>
        /// <b>A shield is not read here</b>, and that is the rule rather than an omission. A
        /// bulwark halves what a <em>ward's bolt</em> does to it because the shield is answered by
        /// <em>colour</em>; a storm has no colour, so there is nothing for the soak to be measured
        /// against. See <see cref="SiegeKind.Bulwark"/>.
        /// </para>
        /// </summary>
        public int Storm(int damage, List<SiegeStrike> into)
        {
            if (damage <= 0) return 0;

            int first = into != null ? into.Count : 0;
            int absorbed = 0;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                int took = damage < raider.Health ? damage : raider.Health;
                absorbed += took;

                raider.Health -= took;
                raider.Flash = .18f;

                bool killed = Fell(raider);

                into?.Add(new SiegeStrike(raider.Id, took, killed));
            }

            // Fisher-Yates over what this call added, so a storm on a board that already had
            // strikes pending cannot reorder somebody else's.
            if (into != null)
            {
                for (int i = into.Count - 1; i > first; i--)
                {
                    int j = first + (int)(Next() % (uint)(i - first + 1));
                    var held = into[i];
                    into[i] = into[j];
                    into[j] = held;
                }
            }

            return absorbed;
        }

        /// <summary>
        /// Mends a ward, and answers how much health it actually took.
        ///
        /// <para>
        /// <b>It mends a standing ward and can never raise a fallen one.</b> That is not a
        /// kindness withheld, and the reason moved once without the rule moving with it. It used
        /// to be that nothing at all could put a ward back up, so <see cref="Stranded"/> could
        /// honestly say a fallen line was beyond rescue. <see cref="Rally"/> now can — but only
        /// as a <em>continue</em>, which is a purchase made after the run is over, offered before
        /// any of the accounting happens and priced against the grade (invariant 23). A mending
        /// that raised a ward would be that same purchase sold for eight gems in the middle of a
        /// run, outside the ordering the continue exists to keep and outside the toll that stops
        /// it buying a star. What a utility may sell is survival, never resurrection.
        /// </para>
        /// </summary>
        public int Mend(int ward, int health)
        {
            if (ward < 0 || ward >= _wards.Length || health <= 0) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = SiegeTuning.WardHealth - post.Health;
            if (room <= 0) return 0;

            int given = health < room ? health : room;
            post.Health += given;

            return given;
        }

        /// <summary>
        /// Pours fuel into a ward, in tenths, and answers how many tenths it took.
        ///
        /// <para>
        /// <b>Tenths rather than the ward's own float</b>, because what comes back decides a
        /// graded number: <c>SiegeUtility</c> converts it to matches, and a graded number
        /// decided by a float is one three code generators round three ways. The ward's live
        /// fuel stays a float because it is drained by a clock, which is the one quantity here
        /// that genuinely is continuous.
        /// </para>
        /// <para>
        /// Room is <em>floored</em> to whole tenths, so this can never report taking more than
        /// it gave. What refuses a ward too full to be worth it is <c>SiegeUtility</c>, before
        /// an item is spent.
        /// </para>
        /// </summary>
        public int Surge(int ward, int tenths)
        {
            if (ward < 0 || ward >= _wards.Length || tenths <= 0) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = RoomForFuel(ward);
            if (room <= 0) return 0;

            // **A surge lifts a douse, and that is what makes it the blightcaller's answer.**
            // Fuel poured into a ward that cannot fire is fuel spent on nothing until the dark
            // runs out on its own, which is a utility charged for a delay — so pouring re-lights
            // it. The player is buying the seconds rather than the fuel, which is exactly what
            // invariant 39 says a utility may sell: a finish, never a grade.
            post.Dark = 0f;

            int given = tenths < room ? tenths : room;
            post.Fuel += given / 10f;

            if (post.Fuel > post.Capacity) post.Fuel = post.Capacity;

            return given;
        }

        /// <summary>
        /// Puts the ward line back up, because a continue was paid for. Answers how many turrets
        /// really stood again.
        ///
        /// <para>
        /// <b>The one thing in this mode that raises a fallen ward, and it is a continue rather
        /// than a utility</b> — see <see cref="Mend"/> for the difference and why it matters.
        /// The hill is not touched: every raider stands where it stood, mid-march or mid-swing,
        /// with the wave clock where it was, which is what "carry on from where you left off"
        /// has to mean when the thing that ended the run was the line and not the board.
        /// </para>
        /// <para>
        /// <b>Full health, and the rank the cogs bought.</b> Health, because a line raised at
        /// anything less is a line that falls again in a breath and a continue that does not
        /// continue is a charge (invariant 23). Rank, because it is the one thing in this mode a
        /// player earns (invariant 37w) and nothing has taken it away — a continue that
        /// confiscated it would be selling back less than was lost.
        /// </para>
        /// <para>
        /// <b>No fuel, deliberately.</b> Fuel is damage, and damage is progress the run was
        /// graded against; a continue may buy a finish and never a grade, so the wards come back
        /// standing and empty and the player matches to light them. The dark a blightcaller left
        /// is cleared for the opposite reason — it is seconds the player would be paying for and
        /// not receiving.
        /// </para>
        /// <para>
        /// Raised left to right so that a published figure smaller than the line is at least
        /// deterministic. It ships as the whole line, so in practice the order decides nothing.
        /// </para>
        /// </summary>
        public int Rally(int wards)
        {
            if (wards <= 0) return 0;

            int raised = 0;

            for (int i = 0; i < _wards.Length && raised < wards; i++)
            {
                var post = _wards[i];
                if (post.Alive) continue;

                post.Alive = true;
                post.Health = SiegeTuning.WardHealth;
                post.Fuel = 0f;
                post.Dark = 0f;
                post.Cool = 0f;

                raised++;
            }

            return raised;
        }

        /// <summary>Whether this ward is standing but smothered. Asked by a surge's offer.</summary>
        public bool Doused(int ward)
            => ward >= 0 && ward < _wards.Length && _wards[ward].Doused;

        /// <summary>
        /// Seconds left on a warbringer's roar, or nought. The view draws the hill charging.
        /// </summary>
        public float Roaring => _roar;

        /// <summary>
        /// How much more fuel a ward could take, in whole tenths. Nought for a fallen one.
        ///
        /// Floored, so an offer is never made on room that turns out not to be there.
        /// </summary>
        public int RoomForFuel(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            float room = post.Capacity - post.Fuel;
            return room <= 0f ? 0 : (int)(room * 10f);
        }

        /// <summary>How much more health a ward could take. Nought for a fallen one.</summary>
        public int RoomForHealth(int ward)
        {
            if (ward < 0 || ward >= _wards.Length) return 0;

            var post = _wards[ward];
            if (!post.Alive) return 0;

            int room = SiegeTuning.WardHealth - post.Health;
            return room < 0 ? 0 : room;
        }

        /// <summary>The raider with this id, or null once it has been taken off the hill.</summary>
        public SiegeRaider Find(int id)
        {
            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Id == id) return _raiders[i];

            return null;
        }
    }
}
