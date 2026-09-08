using System;
using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Which way a chapter is played. <see cref="Glade"/> turns conduits until the light
    /// reaches every critter; the rest are their own games entirely.
    ///
    /// <para>
    /// <b>A mode is code, and a chapter names one.</b> That split is the whole design. A mode
    /// brings an interaction, a fail state and a par rule, so content can never add one — but
    /// content decides which glades are played that way, which is what lets a drop ship ten
    /// wisp runs with no app update. The manifest carries the name; this build carries the
    /// list of names it can honour, and a chapter naming a mode it has never heard of is
    /// skipped whole rather than half-read, exactly as <c>minAppVersion</c> is. That is the
    /// forward-compatibility rule the content system already lives by: content ships ahead of
    /// builds, so an older client meeting newer content must lose that content and nothing
    /// else.
    /// </para>
    /// <para>
    /// It is a struct wearing a permanent string rather than an enum for
    /// <see cref="LevelId"/>'s reason: the value reaches the manifest, analytics and loc keys,
    /// so an enum's ordinal would be a second identity nobody authored, and renumbering one
    /// would silently repoint a chapter at a different mode.
    /// </para>
    /// <para>
    /// Note what is deliberately absent: nothing here reaches the save file. A mode-two glade
    /// is an ordinary glade with its own permanent <see cref="LevelId"/>, so its record,
    /// its stars, its rewards and its merge are the ones every other glade already has — see
    /// <c>ProgressionLedger</c>. That is why a second mode cost no schema version and no
    /// server work.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct GameMode : IEquatable<GameMode>, IComparable<GameMode>
    {
        /// <summary>Short because it is a manifest field, a loc key stem and an analytics dimension.</summary>
        public const int MaxLength = 16;

        readonly string _value;

        GameMode(string value) => _value = value;

        public static readonly GameMode None = default;

        /// <summary>
        /// Turn the conduits until every critter wakes. The mode the game shipped with, and
        /// the one a chapter that names none is read as — so every chapter authored before
        /// modes existed keeps working with its file untouched.
        /// </summary>
        public static readonly GameMode Glade = new GameMode("glade");

        /// <summary>Motes drop into columns and cook toward white. See <c>FallBoard</c>.</summary>
        public static readonly GameMode Fall = new GameMode("fall");

        /// <summary>Buds that burst and ripen what is beside them. See <c>BudBoard</c>.</summary>
        public static readonly GameMode Bud = new GameMode("bud");

        /// <summary>Pull a stone and the cairn comes down. See <c>ToppleBoard</c>.</summary>
        public static readonly GameMode Topple = new GameMode("topple");

        /// <summary>
        /// Nova Raid. Drag a crystal so three alike line up; four forge a laser, five a nova;
        /// the raiders come apart and the monsters fall home. See <c>NovaBoard</c>.
        ///
        /// <para>
        /// The first mode here built on the genre's own verb, and the first with a story since
        /// Deep Orbit was withdrawn. Neither cost the save file anything: a Nova Raid level is
        /// an ordinary level with its own permanent <see cref="LevelId"/> (invariant 20a), and
        /// its dialogue is presentation that nothing grades.
        /// </para>
        /// </summary>
        public static readonly GameMode Nova = new GameMode("nova");

        /// <summary>
        /// Hollowmarch. The raiders are walking a line of pods to a portal and some of them are
        /// carrying caged critters; fire a core into the line and three alike go off, the gap
        /// closes, and if the closure makes three more it goes again. See <c>MarchBoard</c>.
        ///
        /// <para>
        /// The first mode here built on the cascade the whole casual genre runs on, and the
        /// first whose allowance is drawn on the board rather than in a corner — every core
        /// spent is a step the raiders take toward the gate, so how much time is left is
        /// something a player reads off the line. It cost the rules nothing that every mode
        /// before it did not also pay: one shot is one layer of the search, so depth is cores
        /// spent and par falls out of a breadth-first walk (invariant 20j).
        /// </para>
        /// </summary>
        public static readonly GameMode March = new GameMode("march");

        /// <summary>
        /// Emberforge. Swap two neighbours so three alike line up and they fuse into an
        /// <b>ember</b>; tap it and it throws a cross of light down its whole row and column,
        /// or push two together for a star that takes the diagonals too. See
        /// <c>EmberBoard</c>.
        ///
        /// <para>
        /// The genre's own verb and its own special ladder, with one thing changed: nothing
        /// refills. Every ember is three shards that are not coming back and the beams destroy
        /// the wall they cross, so a board is worth about three explosions and where they are
        /// spent is the whole game. That is also what keeps it searchable — the wall only ever
        /// shrinks, so the state graph is a DAG and par is a breadth-first walk (invariant 20j).
        /// </para>
        /// </summary>
        public static readonly GameMode Ember = new GameMode("ember");

        /// <summary>
        /// Prismvale. A field of coloured gems with lanterns standing in it; drag a gem onto its
        /// neighbour to line the lantern's own colour up all the way to a sleeping critter, and
        /// the vein between them lights. See <c>PrismBoard</c>.
        ///
        /// <para>
        /// The classic glade's question - get light onto every critter - asked with the jewel
        /// board's own gesture and none of its match-three. Nothing bursts and nothing falls: a
        /// gem is never removed, so every move is a rearrangement and a vein is something the
        /// player builds rather than something they spend. Light is read off the arrangement
        /// rather than stored, which is what makes a vein breakable and a careless swap on one
        /// side of the board something that can put out the line on the other.
        /// </para>
        /// <para>
        /// Note what it cost the save file, the wire and the server: nothing (invariant 20a). It
        /// is the twelfth mode the prototype level shape has carried.
        /// </para>
        /// </summary>
        public static readonly GameMode Prism = new GameMode("prism");

        /// <summary>
        /// Thornwatch. The raiders come down the hill at the grove's ward line; match a colour and
        /// the ward of that colour fuels up and looses bolts until its fuel fades. Let them
        /// through and they break the wards. See <c>SiegeBoard</c>.
        ///
        /// <para>
        /// <b>The first mode here that runs on a clock, and the first whose par is not a proof.</b>
        /// Everything else in this game is turn-based and searchable — a move is a layer of a
        /// breadth-first walk and par is the depth of the first layer that wins. Raiders that walk
        /// while nobody is touching the board have no such graph, so par is arithmetic over what
        /// the level sends: the fewest matches that could possibly have destroyed it. Everything
        /// else about being a run is unchanged, and it cost the save file no schema version, no
        /// merge rule and no server work (invariant 20a).
        /// </para>
        /// </summary>
        public static readonly GameMode Siege = new GameMode("siege");

        // "kindle" is a **retired mode id and must never be reused.** Kindlewake shipped one
        // chapter of ten hollows - join two embers of a colour and a strand of light burns
        // between them for good - and was withdrawn by the owner after playing it: the way it
        // played was not the way the mode had been asked for, and its animations were the
        // second half of the same verdict. Prismvale took its slot and was built against the
        // same commission stated again. Its chapter id `k01_kindlewake` and its ten level ids
        // `k01_firstlight`, `k01_stillwood`, `k01_crossways`, `k01_stonerow`, `k01_thechoir`,
        // `k01_dimhollow`, `k01_threefold`, `k01_lanternweft`, `k01_deepwake` and
        // `k01_kindleheart` are spent with it, as are the lesson ids `kindle_join` and
        // `kindle_cross`. It was played on a device, so a real save may hold a record or a
        // `tipsSeen` entry against any of them - which is exactly why `ProgressionStore`'s
        // high-water floors exist (invariant 9): derived XP and credits fall when levels leave
        // the catalog, and the floors are what stop a player noticing.

        // "quarry" is a **retired mode id and must never be reused.** The Iron Quarry shipped
        // three levels - cut a charge loose, it slides until something stops it and goes off
        // there - and was withdrawn by the owner without being played: a floor of cut stone
        // with one hot thing crossing it, three flicks deep, with nothing on it the player
        // *made* and nothing that kept paying out after they stopped. That is 20j and 26h
        // asking their questions a sixth time. Its chapter id `q01_ironquarry` and its three
        // level ids `q01_cutloose`, `q01_wardenrow` and `q01_thedeepcut` are spent with it, as
        // are the lesson ids `quarry_flick` and `quarry_armour`. Hollowmarch took its slot and
        // inherited its cast, its explosions, its story band and its village backdrops, which
        // is what a seam is for.

        // "nova" and "topple" are **retired mode ids and must never be reused.** Nova Raid
        // (drag a crystal, three alike burst) and Toppleglen (pull a stone, the cairn comes
        // down) were both withdrawn by the owner after play, on the day Nova Raid was built:
        // one was a match-three on a small lattice and the other a heap of stone, and the
        // verdict on the pair was that neither was the fresh thing the slot was commissioned
        // for. Both were played on a device, so a save may hold a record against their chapter
        // and level ids, which are spent with them: v01_harvester with v01_firstlight,
        // v01_ironwatch and v01_thehold; and t01_toppleglen with t01_firstfall. Their lesson
        // ids are spent too - nova_drag, nova_forge, nova_armour, nova_skiff, topple_pull and
        // topple_seat. What survived is everything that was a *seam* rather than a mode: the
        // prototype level shape, the story band and its cast, the village world of backdrops,
        // and the cast and explosion art, all of which the Iron Quarry inherited.

        // "orbit" and "moonwake" are **retired mode ids and must never be reused.** Deep Orbit
        // (aim a salvage drum down a lane of an alien deck) and Moonwake (ring a bell and the
        // monsters slide) were both built on 2026-09-06 as three-level and one-level
        // commissions to be judged by playing, and both were withdrawn by the owner on the
        // same day for not putting enough on the screen. Both were played on a device, so a
        // save may hold a record against their chapter and level ids, which are spent with
        // them: o01_hollowfleet with o01_firstbreak, o01_sentryline and o01_wardensgate; and
        // m01_moonwake with m01_bellbreak. The story system they brought (StoryScript,
        // StoryBubble) stayed, re-cast, because it is a seam and not a mode.

        // "nectar", "ribbon", "fling" and "warren" are **retired mode ids and must never be
        // reused.** Four of the five prototypes commissioned into Groovekeeper's slot were
        // withdrawn by the owner after one session of play each; Toppleglen is the one kept. An
        // id travels into the manifest, analytics and loc keys exactly as a level id does, so
        // re-pointing one at a different way of playing would silently relabel whatever history
        // a device already holds. Their four chapter ids and their four level ids are spent with
        // them - n01_nectarrun/n01_firstpour, r01_ribbonfall/r01_firstribbon,
        // s01_seedfling/s01_firstfling and w01_warrenwake/w01_firstparade - because all four
        // were played on a device, so a save may hold a record against any of them. That is what
        // ProgressionStore's high-water floors are for (invariant 9): derived XP and credits fall
        // when a level leaves the catalog, and the floors are what stop a player noticing.
        // A chapter file still naming one of these modes is simply skipped, which is the same
        // forward-compatibility rule an unknown mode has always had.

        // "keeper" is a **retired mode id and must never be reused.** Groovekeeper shipped one
        // chapter and was withdrawn: laying tiles so that unlike edges bloom was a sound rule that
        // played as arithmetic, and the owner's verdict on it was one word. Its ten level ids went
        // with it and are spent too - k01_first_grove, k01_two_beds, k01_stonecircle,
        // k01_openhand, k01_four_petals, k01_heartwood, k01_narrows, k01_prismfall,
        // k01_thelace and k01_grovekeeper's own chapter id - because an id travels into the
        // manifest, analytics and loc keys exactly as a level id does, and a save on a real device
        // may still hold a record against one of them. Its slot on the map was taken by five
        // prototype modes of one level each, of which Toppleglen is the one that survived play.

        // "weave" is a **retired mode id and must never be reused.** Lightweave shipped three
        // chapters and was removed: dragging a channel from a crystal to its critter turned out
        // to reject almost nothing (invariant 5d), and the two rules that did bite — the ring and
        // the hedge — were bought by making the *route* longer rather than by making the decision
        // harder. An id travels into the manifest, analytics and loc keys exactly as a level id
        // does, so re-pointing it at a different way of playing would silently relabel three
        // chapters' worth of history. A chapter file still naming it is simply skipped, which is
        // the same forward-compatibility rule an unknown mode has always had.

        /// <summary>What a chapter with no <c>mode</c> field is played as.</summary>
        public static GameMode Default => Glade;

        /// <summary>
        /// Every mode this build can play, in the order the switcher offers them.
        ///
        /// <para>
        /// Derived from <see cref="LevelModes"/> rather than listed again here, so a mode is
        /// registered in exactly one place and this list cannot come to disagree with what the
        /// game can actually load. The classic mode is first and stays first: it is where a new
        /// player is, and a switcher that reorders itself as modes are added would move the
        /// entry somebody reaches for without looking.
        /// </para>
        /// </summary>
        public static IReadOnlyList<GameMode> Shipped => LevelModes.Ids;

        public string Value => _value ?? string.Empty;

        public bool IsValid => !string.IsNullOrEmpty(_value);

        /// <summary>Whether this build knows how to play it. A chapter's whole membership rests on this.</summary>
        public bool IsPlayable
        {
            get
            {
                var shipped = Shipped;
                for (int i = 0; i < shipped.Count; i++) if (shipped[i].Equals(this)) return true;
                return false;
            }
        }

        /// <summary>
        /// Reads a manifest's <c>mode</c> field. An empty one is <see cref="Default"/> rather
        /// than a rejection, which is what makes the field optional forever.
        ///
        /// <para>
        /// Answering true for a mode this build cannot play is deliberate: the caller has to
        /// tell "this is malformed" (a problem worth reporting) from "this is newer than me"
        /// (a decision reported to nobody). <see cref="IsPlayable"/> is the second question.
        /// </para>
        /// </summary>
        public static bool TryParse(string raw, out GameMode mode, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(raw)) { mode = Default; return true; }

            if (raw.Length > MaxLength)
            {
                mode = None;
                error = $"longer than {MaxLength} characters";
                return false;
            }

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') continue;

                mode = None;
                error = $"illegal character '{c}' at {i}, use a-z 0-9 and underscore";
                return false;
            }

            mode = new GameMode(raw);
            return true;
        }

        /// <summary>The mode's name, derived from its id so a mode names itself once.</summary>
        public string NameKey => "mode." + Value + ".name";

        /// <summary>One line saying what the player does in it, for the switcher.</summary>
        public string TaglineKey => "mode." + Value + ".tagline";

        public bool Equals(GameMode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GameMode other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(GameMode other) => string.CompareOrdinal(Value, other.Value);
        public override string ToString() => Value;

        public static bool operator ==(GameMode a, GameMode b) => a.Equals(b);
        public static bool operator !=(GameMode a, GameMode b) => !a.Equals(b);
    }
}
