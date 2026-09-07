using System;
using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// A moment in a run that somebody might have something to say about.
    ///
    /// <para>
    /// <b>Cues rather than a script with a timeline.</b> A run is played, not watched, so
    /// nothing can know in advance when the third critter comes out — what content can say is
    /// "when one does, this is what Bolt says". Every cue below is a thing the run already knows
    /// about because a readout or an ending depends on it, so a story costs the mode no new
    /// state at all: <c>MarchScreen</c> raises them off the shot it just resolved.
    /// </para>
    /// <para>
    /// <b>The ids are permanent and travel in content, not in the save.</b> Nothing here reaches
    /// <c>tipsSeen</c> — a lesson is once in a player's life and a story line is once in a run —
    /// so a cue can be retired by deleting its beats, and an unknown one in a chapter body is a
    /// build error rather than something silently ignored (invariant 5f's rule applied to a
    /// third vocabulary).
    /// </para>
    /// </summary>
    public enum StoryCue
    {
        /// <summary>The board has just opened. The only cue that is not a reaction.</summary>
        Intro = 0,

        /// <summary>A cage is open and a monster is out.</summary>
        Freed = 1,

        /// <summary>The player made something out of two things: two charges fused.</summary>
        Forged = 2,

        /// <summary>Something the player did not touch went off: a prism shattered.</summary>
        Fired = 3,

        /// <summary>A warden was cut down.</summary>
        Kill = 4,

        /// <summary>The moves left are no more than the goals still standing.</summary>
        Tight = 5,

        /// <summary>Every cage is open and every raider is scrap.</summary>
        Won = 6,

        /// <summary>The run is lost.</summary>
        Lost = 7,
    }

    /// <summary>
    /// Who may speak, and the whole reason the set is written down.
    ///
    /// <para>
    /// A speaker names a folder of frames under <c>Art/March/</c>, so a typo is a portrait that
    /// does not load — which invariant 7b says draws as a <em>white rectangle</em>, not as a
    /// blank. That is a content mistake with no symptom until somebody looks at the screen, so
    /// the set is a list a validator can walk rather than a convention.
    /// </para>
    /// <para>
    /// It is Domain, and it names one mode's cast, which is a compromise made deliberately: the
    /// alternative is a story system that cannot be checked, and a check that cannot run is not
    /// a check (the name-fold note in CLAUDE.md). A second story-telling mode adds its cast
    /// here and nothing else moves.
    /// </para>
    /// </summary>
    public static class StoryCast
    {
        /// <summary>Bolt, the little robot who came over to our side and waits on the pad.</summary>
        public const string Bolt = "bolt";

        /// <summary>The Collector, who runs the raid and does the gloating.</summary>
        public const string Collector = "collector";

        /// <summary>The three taken monsters, who speak when they are out.</summary>
        public const string Mon1 = "mon1", Mon2 = "mon2", Mon3 = "mon3";

        public static readonly string[] All = { Bolt, Collector, Mon1, Mon2, Mon3 };

        public static bool Knows(string speaker)
        {
            if (string.IsNullOrEmpty(speaker)) return false;
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i], speaker, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>One thing one character says.</summary>
    public readonly struct StoryLine
    {
        /// <summary>Who says it. A <see cref="StoryCast"/> id.</summary>
        public readonly string Speaker;

        /// <summary>
        /// What they say, as a loc key.
        ///
        /// <b>Authored rather than derived, and that is the one place this differs from a
        /// level's own strings</b> (invariant 5a). A level's name key is a function of its id
        /// because anything holding a <see cref="LevelId"/> has to be able to name it without
        /// reading a chapter body; nothing ever needs to name a line of dialogue it has not
        /// read. What the derivation would buy is protection from typos, and that is bought
        /// instead by the build gate resolving every one of these against <c>loc/en.json</c> —
        /// which is a stricter check than a naming convention, because it also catches a key
        /// that is correctly shaped and simply missing.
        /// </summary>
        public readonly string Key;

        public StoryLine(string speaker, string key)
        {
            Speaker = speaker;
            Key = key;
        }

        public bool IsValid => StoryCast.Knows(Speaker) && !string.IsNullOrEmpty(Key);
    }

    /// <summary>Everything said at one cue, in order.</summary>
    public sealed class StoryBeat
    {
        public readonly StoryCue Cue;
        public readonly IReadOnlyList<StoryLine> Lines;

        public StoryBeat(StoryCue cue, IReadOnlyList<StoryLine> lines)
        {
            Cue = cue;
            Lines = lines ?? new StoryLine[0];
        }
    }

    /// <summary>
    /// What a level has to say for itself.
    ///
    /// <para>
    /// <b>Several beats may share a cue, and they are handed out in order.</b> A run that frees
    /// four critters and hears the same sentence four times is a run that stops reading them, so
    /// <see cref="Take"/> is asked for the <em>nth</em> time a cue has fired and answers null
    /// once the author has run out of things to say. Silence is the default and costs nothing.
    /// </para>
    /// <para>
    /// Presentation, not rules: a story cannot move par, cannot move a star line and cannot be
    /// read by anything that grades a run. That is why it hangs off
    /// <see cref="LevelPresentation"/> rather than off the mode's own block — a mode's block is
    /// the board, and the board is the thing every graded number derives from.
    /// </para>
    /// </summary>
    public sealed class StoryScript
    {
        public static readonly StoryScript Silent = new StoryScript(new StoryBeat[0]);

        readonly StoryBeat[] _beats;

        public StoryScript(IReadOnlyList<StoryBeat> beats)
        {
            _beats = new StoryBeat[beats == null ? 0 : beats.Count];
            for (int i = 0; i < _beats.Length; i++) _beats[i] = beats[i];
        }

        public bool Any => _beats.Length > 0;

        public IReadOnlyList<StoryBeat> Beats => _beats;

        /// <summary>The beat for the <paramref name="nth"/> firing of this cue, or null.</summary>
        public StoryBeat Take(StoryCue cue, int nth)
        {
            if (nth < 0) return null;

            int seen = 0;
            for (int i = 0; i < _beats.Length; i++)
            {
                if (_beats[i].Cue != cue) continue;
                if (seen == nth) return _beats[i];
                seen++;
            }

            return null;
        }

        /// <summary>Reads a cue's name as authored. Unknown names are refused, never ignored.</summary>
        public static bool TryReadCue(string raw, out StoryCue cue)
        {
            switch (raw)
            {
                case "intro": cue = StoryCue.Intro; return true;
                case "freed": cue = StoryCue.Freed; return true;
                case "forged": cue = StoryCue.Forged; return true;
                case "fired": cue = StoryCue.Fired; return true;
                case "kill": cue = StoryCue.Kill; return true;
                case "tight": cue = StoryCue.Tight; return true;
                case "won": cue = StoryCue.Won; return true;
                case "lost": cue = StoryCue.Lost; return true;
                default: cue = StoryCue.Intro; return false;
            }
        }

        /// <summary>Every cue name a chapter body may write, for the messages that refuse one.</summary>
        public const string CueNames = "intro, freed, forged, fired, kill, tight, won, lost";
    }
}
