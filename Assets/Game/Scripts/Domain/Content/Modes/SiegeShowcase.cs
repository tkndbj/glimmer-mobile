using System.Collections.Generic;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// One board the advert is recorded on: the field, the roster, the line and the pictures.
    ///
    /// <para>
    /// <b>A description, holding no state.</b> Two of these exist (<see cref="SiegeShowcase.Overrun"/>
    /// and <see cref="SiegeShowcase.Warlords"/>) and the screen, the model and the fixture are
    /// handed one; nothing about a board is decided anywhere but here.
    /// </para>
    /// </summary>
    public sealed class ShowcaseBoard
    {
        /// <summary>A name for the door and the fixture's table. Never a loc key; never seen by a player.</summary>
        public readonly string Id;

        /// <summary>The field, authored settled. See <see cref="SiegeShowcase.Rows"/>.</summary>
        public readonly string[] Rows;

        /// <summary>The roster, wave by wave, in the grammar's spelling — or null when <see cref="Roster"/> is used.</summary>
        public readonly string[] Waves;

        /// <summary>The boss token in the grammar's spelling, or empty. Only with <see cref="Waves"/>.</summary>
        public readonly string Boss;

        /// <summary>The roster as specs, for a shape the grammar cannot say — or null when <see cref="Waves"/> is used.</summary>
        public readonly SiegeSpec[][] Roster;

        /// <summary>How often a felled raider leaves a cog, in a hundred.</summary>
        public readonly int Cogs;

        /// <summary>Raider health, in tenths of the plain figure.</summary>
        public readonly int Tough;

        /// <summary>What stands on the line, one turret per colour.</summary>
        public readonly (char Colour, string Ward)[] Seats;

        /// <summary>The raider cast (<c>SiegeMode.Insects</c> and so on).</summary>
        public readonly int CastSet;

        /// <summary>Which hill picture the board stands on. See <c>SiegeMode.Ground</c>.</summary>
        public readonly int Ground;

        /// <summary>The sky behind it, a chapter picture the screen holds for its own life.</summary>
        public readonly string Backdrop;

        public ShowcaseBoard(string id, string[] rows, string[] waves, string boss, SiegeSpec[][] roster,
                             int cogs, int tough, (char Colour, string Ward)[] seats, int castSet,
                             int ground, string backdrop)
        {
            Id = id;
            Rows = rows;
            Waves = waves;
            Boss = boss ?? string.Empty;
            Roster = roster;
            Cogs = cogs;
            Tough = tough;
            Seats = seats;
            CastSet = castSet;
            Ground = ground;
            Backdrop = backdrop;
        }

        /// <summary>The line, every seat at <see cref="SiegeShowcase.Stars"/>.</summary>
        public WardLine LineUp()
        {
            var chosen = new List<WardSlot>(Seats.Length);
            for (int i = 0; i < Seats.Length; i++)
                chosen.Add(new WardSlot(Seats[i].Colour, Seats[i].Ward));

            return WardLine.Resolve(WardCatalog.Default, chosen, (_, __) => true,
                                    (_, __) => SiegeShowcase.Stars);
        }

        /// <summary>The board. Never null; see <see cref="Fault"/>.</summary>
        public SiegeLayout Layout()
        {
            ProtoGrid.TryRead(Rows, SiegeShowcase.Width, SiegeShowcase.Height, SiegeLayout.Cells,
                              out var grid, out _);

            return Roster != null
                 ? new SiegeLayout(grid, SiegeShowcase.Deal, SiegeShowcase.Line, Roster, Cogs, Tough,
                                   SiegeShowcase.Charms)
                 : new SiegeLayout(grid, SiegeShowcase.Deal, SiegeShowcase.Line, Waves, Boss, Cogs, null,
                                   Tough, SiegeShowcase.Charms);
        }

        /// <summary>The rules the recording is played by: this board on this line.</summary>
        public SiegeRules Rules() => new SiegeRules(Layout(), LineUp());

        /// <summary>
        /// What is wrong with the board, or null — the reading every authored siege gets, asked
        /// of one that is not authored. The fixture asks it, because nothing else can.
        /// </summary>
        public string Fault()
        {
            if (!ProtoGrid.TryRead(Rows, SiegeShowcase.Width, SiegeShowcase.Height, SiegeLayout.Cells,
                                   out _, out var error))
                return error;

            return Layout().Fault;
        }

        /// <summary>Every boss kind this board sends, each once, in the order first met.</summary>
        public List<SiegeKind> BossKinds()
        {
            var layout = Layout();
            var kinds = new List<SiegeKind>(2);

            for (int w = 0; w < layout.Coming.Length; w++)
                for (int i = 0; i < layout.Coming[w].Length; i++)
                {
                    var kind = layout.KindAt(w, i);
                    if (SiegeTuning.IsBoss(kind) && !kinds.Contains(kind)) kinds.Add(kind);
                }

            return kinds;
        }
    }

    /// <summary>
    /// The boards the advert is recorded on, and the player model that plays them.
    ///
    /// <para>
    /// <b>It is <see cref="SiegeTutorial"/>'s shape, pointed the other way.</b> The tutorial is
    /// the live mode with a script beside it that teaches two things; this is the live mode with
    /// a script beside it that <em>plays</em> — every raider kind, every charm, a cog rate high
    /// enough to be seen, and the biggest lines the shelf sells — so a screen recording of it is
    /// a recording of the game and not of a mock-up. Nothing below re-implements a rule:
    /// <c>SiegeBoard</c> answers every question, and every decision the model takes goes through
    /// a door the board already has (<c>Swap</c>, <c>Overcharge</c>, <c>Take</c>, <c>Detonate</c>,
    /// <c>Dig</c>).
    /// </para>
    /// <para>
    /// <b>Authored here rather than in a chapter body</b>, for the tutorial's reason and one
    /// more: these have no <c>LevelId</c>, no record, no stars, no reward, no place in the
    /// manifest and no gate (53d) — and they stand lines the player does not own, which no
    /// chapter may do (15a). Descriptions, holding no state and mutating nothing except through
    /// <see cref="Plant"/>, which is the one deliberate gift: a charm stood on the field so the
    /// recording meets all six in a minute rather than in the two hundred gems the dealt window
    /// would take (<c>SiegeTuning.CharmWithin</c>).
    /// </para>
    /// <para>
    /// <b>They reach no player.</b> Nothing in the game opens <c>ShowcaseScreen</c> any more:
    /// the CUSTOM keys (<c>Dev/ShowcaseDoor.cs</c>) were deleted after the recordings on
    /// 2026-09-27, and the fixture that holds all of this (<c>ShowcaseTests</c>) is the only
    /// gate they pass through — a board here is read by no content gate, exactly as the
    /// tutorial's is not.
    /// </para>
    /// </summary>
    public static class SiegeShowcase
    {
        // ------------------------------------------------------------------ what every board shares
        /// <summary>The field, in the shape every siege chapter uses.</summary>
        public const int Width = 8, Height = 5;

        /// <summary>All four colours, so the line stands four turrets and the hill sends four.</summary>
        public const string Deal = "rgby";

        /// <summary>The line, one seat per colour.</summary>
        public const string Line = "rgby";

        /// <summary>Every charm the mode has. The dealt window still runs; <see cref="Plant"/> is on top of it.</summary>
        public const string Charms = "plsfha";

        /// <summary>The rung every seat stands at: the top of the ladder.</summary>
        public const int Stars = WardStars.Most;

        /// <summary>
        /// The field, authored settled with eight swaps on it, shared by both boards.
        ///
        /// <b>Do not retype this by eye.</b> <c>ShowcaseTests</c> holds it settled and holds it
        /// to offering a move, because a field that is one letter wrong cascades in the first
        /// frame of the recording and nothing else here could see it.
        /// </summary>
        public static readonly string[] Rows =
        {
            "ybrygbyy",
            "rbrbbgyr",
            "yygybrbr",
            "gbrgrgbg",
            "ygbgrgyy",
        };

        // ------------------------------------------------------------------ the boards
        /// <summary>
        /// <b>Custom.</b> A lost run: five waves of sixty raiders — the cap — overrun a line of
        /// four five-star legendaries on the fifth wave.
        ///
        /// <para>
        /// <b>Tuned to lose, at the owner's instruction after the first recording</b>: the
        /// advert wants a watcher to feel the wall going. <b>No boss</b>, and that is the shape
        /// rather than an omission: a boss walks onto a cleared hill only (37dn) and a run that
        /// ends with the crowd on the hill is a run no boss can be in. <b>No bombers</b>, at the
        /// same instruction: a bomb is a thing to look at beside the fight. The waves grow and
        /// come on the clock (<c>SiegeBoard.Muster</c>), so the fifth stacks on what is left of
        /// the fourth. <c>Tough</c> is chaotic — a step of twenty moves which wave the line falls
        /// on — so retune by reading what <c>ShowcaseTests</c> prints.
        /// </para>
        /// </summary>
        public static readonly ShowcaseBoard Overrun = new ShowcaseBoard(
            "overrun", Rows,
            waves: new[]
            {
                "rgbyrgbyR",
                "RGbyrgby#rG",
                "RGBYrgby#g#bB",
                "RGBYRGrgby#y#rBY",
                "RGBYRGBY#g#brgbyRG",
            },
            boss: string.Empty, roster: null,
            cogs: 70, tough: 160,
            seats: new[] { ('r', "sunderer"), ('g', "eclipse"), ('b', "wellspring"), ('y', "tempest") },
            castSet: SiegeMode.Wild, ground: 4, backdrop: "sky_14");

        /// <summary>
        /// <b>Custom 2.</b> Two bosses walk in at once with the insects behind them, and the
        /// run is won.
        ///
        /// <para>
        /// <b>A shape the chapter grammar cannot say</b> — a boss there is one token and always
        /// the last wave — and the board can already play, because the endless lane deals a
        /// pair with an escort through exactly the same muster (<c>SiegeTuning.BossLane</c>
        /// seats two either side of the middle). So the roster is handed in as specs
        /// (<c>SiegeLayout</c>'s second constructor): an overlord and a harrower at the head of
        /// the first wave, the insect cast walking in behind them from the first second, and
        /// two crowds after — which cannot muster until both bosses are down (37dn), so the
        /// duel is the whole of the opening.
        /// </para>
        /// <para>
        /// <b>Overlord and harrower</b> because both are cut from the head-on insect family
        /// the first chapter's cast belongs to, and because their verbs read on a recording:
        /// the overlord shells the line and sunders a rank, the harrower tears a rank off a
        /// post and drops it on the hill as a cog the hand goes and picks back up.
        /// </para>
        /// <para>
        /// <b>Four turrets the other board does not stand</b>, at the owner's instruction:
        /// pyroclast (fire that keeps burning), permafrost (frost), starfall (meteors) and
        /// railgun (a piercing bolt).
        /// </para>
        /// </summary>
        public static readonly ShowcaseBoard Warlords = new ShowcaseBoard(
            "warlords", Rows,
            waves: null, boss: string.Empty,
            roster: new[]
            {
                Specs("rgbyRGby#g#yrgby", Boss(SiegeKind.Overlord, 'r'), Boss(SiegeKind.Harrower, 'b')),
                Specs("RGBYrgbyRGBY#r#brgbyrg"),
                Specs("RGBYRGBYrgby#g#yRGbyrgbyRG"),
            },
            cogs: 70, tough: 20,
            seats: new[] { ('r', "pyroclast"), ('g', "permafrost"), ('b', "starfall"), ('y', "railgun") },
            castSet: SiegeMode.Insects, ground: 0, backdrop: "sky_02");

        /// <summary>Every board, in the order the door draws them.</summary>
        public static readonly ShowcaseBoard[] All = { Overrun, Warlords };

        /// <summary>A boss spec: the kind, wearing a colour (which decides nothing about the line, 37dn).</summary>
        public static SiegeSpec Boss(SiegeKind kind, char colour) => new SiegeSpec(colour, kind);

        /// <summary>
        /// One wave as specs: <paramref name="lead"/> first, then <paramref name="wave"/> read in
        /// the grammar's spelling — lower case a creeper, upper case a brute, <c>#</c> a bulwark.
        ///
        /// <b>No bombers, by construction.</b> A <c>!</c> is refused rather than read, because
        /// the owner took bombs off the advert and a token that read as something would be the
        /// silent drop invariant 5f is about.
        /// </summary>
        public static SiegeSpec[] Specs(string wave, params SiegeSpec[] lead)
        {
            var made = new List<SiegeSpec>(lead.Length + (wave?.Length ?? 0));
            made.AddRange(lead);

            for (int i = 0; wave != null && i < wave.Length; i++)
            {
                char mark = wave[i];
                if (mark == ' ') continue;

                if (mark == SiegeLayout.Shield)
                {
                    if (++i >= wave.Length) break;
                    made.Add(new SiegeSpec(char.ToLowerInvariant(wave[i]), SiegeKind.Bulwark));
                    continue;
                }

                if (SiegeLayout.Letters.IndexOf(char.ToLowerInvariant(mark)) < 0)
                    throw new System.ArgumentException(
                        $"'{mark}' is not a raider this board spells; a wave is letters and '#'");

                made.Add(new SiegeSpec(char.ToLowerInvariant(mark),
                                       char.IsUpper(mark) ? SiegeKind.Brute : SiegeKind.Creeper));
            }

            return made.ToArray();
        }

        // ------------------------------------------------------------------ the player
        /// <summary>
        /// How many bodies the hill wants on it before an overcharge is thrown.
        ///
        /// Three rather than the tutorial's two, because the lines here kill faster and a blast
        /// spent on one creeper reads as a misfire on a recording. A standing boss counts.
        /// </summary>
        public const int Crowd = 3;

        /// <summary>
        /// The swap the player makes next, or false when the field offers none.
        ///
        /// <para>
        /// <b>Chosen the way a good player chooses, with a little of a real one's noise.</b>
        /// Every legal swap is scored: one that springs a charm standing on the field wins
        /// outright, because a charm is the reason it was stood there; otherwise the run's colour
        /// is weighed against what is walking down the hill, furthest body first, the way the
        /// chapter sweep's model weighs it (<c>SiegeRuleTests.Aimed</c>); a longer run scores a
        /// little more; and a seeded jitter breaks ties so the same board is not always answered
        /// from its top-left corner, which is the one tell of a machine playing.
        /// </para>
        /// <para>
        /// <b>Pure, seeded, and deterministic</b>: <paramref name="seed"/> is the only state, so
        /// the fixture and the screen play the same game from the same seed.
        /// </para>
        /// </summary>
        public static bool Aimed(SiegeBoard board, ref uint seed, out int a, out int b)
        {
            a = b = -1;
            if (board == null) return false;

            int width = board.Width, height = board.Height;

            var cells = new char[board.Count];
            var charms = new SiegeCharm[board.Count];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = board.At(i);
                charms[i] = board.CharmAt(i);
            }

            var wanted = Wanted(board);

            int bestScore = int.MinValue;

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int here = y * width + x;

                    for (int d = 0; d < 2; d++)
                    {
                        if (d == 0 && x + 1 >= width) continue;
                        if (d == 1 && y + 1 >= height) continue;

                        int other = d == 0 ? here + 1 : here + width;
                        if (!board.Lines(here, other)) continue;

                        int score = Score(cells, charms, width, height, here, other, wanted)
                                  + (int)(Next(ref seed) % 12u);

                        if (score <= bestScore) continue;

                        bestScore = score;
                        a = here;
                        b = other;
                    }
                }

            return a >= 0;
        }

        /// <summary>The colours walking the hill whose turret still stands, furthest down first.</summary>
        static List<char> Wanted(SiegeBoard board)
        {
            var plan = board.Layout;
            var raiders = board.Raiders;

            var wanted = new List<char>(4);
            var reach = new List<float>(4);

            for (int i = 0; i < raiders.Count; i++)
            {
                var raider = raiders[i];
                if (!raider.Alive || !raider.OnTheHill) continue;

                char colour = SiegeLayout.Letters[raider.Colour];
                int ward = plan.WardOf(colour);
                if (ward < 0 || !board.Wards[ward].Alive) continue;

                int at = wanted.IndexOf(colour);
                if (at >= 0)
                {
                    if (raider.March > reach[at]) reach[at] = raider.March;
                    continue;
                }

                wanted.Add(colour);
                reach.Add(raider.March);
            }

            for (int i = 1; i < wanted.Count; i++)
                for (int j = i; j > 0 && reach[j] > reach[j - 1]; j--)
                {
                    (reach[j], reach[j - 1]) = (reach[j - 1], reach[j]);
                    (wanted[j], wanted[j - 1]) = (wanted[j - 1], wanted[j]);
                }

            return wanted;
        }

        /// <summary>What springs a charm is worth against everything else on the field.</summary>
        const int SpringsACharm = 1000;

        static int Score(char[] cells, SiegeCharm[] charms, int width, int height, int a, int b,
                         List<char> wanted)
        {
            // The field as it would stand after the swap, and the runs it would make. A charm
            // travels with its gem (`SiegeBoard.Trade`), so both arrays are swapped together.
            (cells[a], cells[b]) = (cells[b], cells[a]);
            (charms[a], charms[b]) = (charms[b], charms[a]);

            var hit = SiegeLayout.Runs(cells, width, height, charms);

            int score = hit.Count * 4;
            bool springs = false;

            foreach (int cell in hit)
            {
                if (charms[cell] != SiegeCharm.None) springs = true;

                int rank = wanted.IndexOf(cells[cell]);
                if (rank >= 0) score += 40 - rank * 10;
            }

            (cells[a], cells[b]) = (cells[b], cells[a]);
            (charms[a], charms[b]) = (charms[b], charms[a]);

            return springs ? score + SpringsACharm : score;
        }

        /// <summary>
        /// The turret to overcharge now, or -1: one the board would let fire
        /// (<c>SiegeBoard.CanOvercharge</c>) while there is a crowd or a boss to throw at.
        /// </summary>
        public static int Armed(SiegeBoard board)
        {
            if (board == null) return -1;
            if (board.OnTheHill < Crowd && !board.BossStanding) return -1;

            for (int i = 0; i < board.Wards.Count; i++)
                if (board.CanOvercharge(i)) return i;

            return -1;
        }

        /// <summary>
        /// The cog to pick up, or -1: the one nearest to fading, so nothing is trampled.
        ///
        /// <b>Only a cog its turret can take</b> (<c>SiegeBoard.Take</c> refuses one for a ward
        /// at the top of the ladder), because a hand told to tap a cog that stays where it is
        /// would tap it again on the next pass, for ever. The refusal is the board's own
        /// (<c>SiegeWard.Upgradable</c>), asked here first.
        /// </summary>
        public static int Loot(SiegeBoard board)
        {
            if (board == null) return -1;

            var cogs = board.Cogs;
            int pick = -1;
            float least = float.MaxValue;

            for (int i = 0; i < cogs.Count; i++)
            {
                var cog = cogs[i];
                if (cog.Ward < 0 || cog.Ward >= board.Wards.Count) continue;
                if (!board.Wards[cog.Ward].Upgradable) continue;
                if (cog.Left >= least) continue;

                least = cog.Left;
                pick = cog.Id;
            }

            return pick;
        }

        /// <summary>
        /// The bomb worth tapping, or -1.
        ///
        /// <b>Asked before the tap, because the board refuses a bomb that catches nothing and a
        /// refused tap on a recording reads as a fumble — and a refused bomb stays where it is,
        /// so a hand told to tap it would tap it again on the next pass, for ever.</b> The
        /// reach is the firepot's own (<c>SiegeTuning.Caught</c>) and the hurt is
        /// <c>SiegeBoard.Wound</c>'s own refusals (a boss still walking in, or resting on its
        /// floor), so this can never say yes to a tap the board says no to. Found by the
        /// fixture: a bomb under a colossus on its floor held the model's hand for a minute.
        /// Neither board sends a bomber now; the reading stays because the door is the board's.
        /// </summary>
        public static int Fuse(SiegeBoard board)
        {
            if (board == null) return -1;

            var bombs = board.Bombs;
            var raiders = board.Raiders;

            for (int i = 0; i < bombs.Count; i++)
                for (int r = 0; r < raiders.Count; r++)
                {
                    var raider = raiders[r];
                    if (!raider.Alive || !raider.OnTheHill || raider.Arriving) continue;
                    if (raider.Health <= raider.Floor) continue;
                    if (!SiegeTuning.Caught(raider.Kind, raider.Lane, raider.March,
                                            bombs[i].Lane, bombs[i].Row)) continue;

                    return bombs[i].Id;
                }

            return -1;
        }

        /// <summary>The turret under a boulder, or -1.</summary>
        public static int Buried(SiegeBoard board)
        {
            if (board == null) return -1;

            for (int i = 0; i < board.Wards.Count; i++)
                if (board.Wards[i].Buried) return i;

            return -1;
        }

        // ------------------------------------------------------------------ the gift
        /// <summary>
        /// The charms stood on the field by hand, in the order the chapters introduce them.
        ///
        /// <b>Every one, once, and never while another stands</b>: the dealt window would take
        /// two hundred gems to show them all and a recording has a minute. A charm is stood on a
        /// cell a legal swap will sweep into a run, so the next move springs it — the point of
        /// showing one is showing it go off.
        /// </summary>
        public static readonly SiegeCharm[] Planted =
        {
            SiegeCharm.Prism, SiegeCharm.Lance, SiegeCharm.Storm,
            SiegeCharm.Furnace, SiegeCharm.Hourglass, SiegeCharm.Anvil,
        };

        /// <summary>The match after which the first charm is stood, and the gap between the rest.</summary>
        public const int PlantFrom = 2, PlantEvery = 2;

        /// <summary>
        /// Which charm is owed after <paramref name="matches"/> swaps with <paramref name="planted"/>
        /// already stood, or <c>None</c>.
        /// </summary>
        public static SiegeCharm Due(int matches, int planted)
        {
            if (planted < 0 || planted >= Planted.Length) return SiegeCharm.None;
            return matches >= PlantFrom + planted * PlantEvery ? Planted[planted] : SiegeCharm.None;
        }

        /// <summary>
        /// Stands <paramref name="charm"/> on the field and answers the cell, or -1 when the
        /// field already carries one or offers nowhere to put it.
        ///
        /// <para>
        /// <b>On a cell a legal swap sweeps into a run</b>, so the model's next move springs it
        /// (<see cref="Aimed"/> scores that above everything). The cell is the swapped gem that
        /// lands inside the run — stood there, the charm travels with its gem into the match. A
        /// prism is a wild and can line up where it stands, so a seat where it would already
        /// be three-alike is refused (<c>SiegeLayout.Lined</c>), because a charm that goes off
        /// before anybody moved is the tutorial's cascade fault with a halo on it.
        /// </para>
        /// <para>
        /// <b>The one write this class makes</b>, through <c>SiegeBoard.Stand</c>, which is the
        /// board's own door for a charm arriving on a cell.
        /// </para>
        /// </summary>
        public static int Plant(SiegeBoard board, SiegeCharm charm, ref uint seed)
        {
            if (board == null || charm == SiegeCharm.None || board.AnyCharm) return -1;

            int width = board.Width, height = board.Height;

            var cells = new char[board.Count];
            var charms = new SiegeCharm[board.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = board.At(i);

            var seats = new List<int>(16);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int here = y * width + x;

                    for (int d = 0; d < 2; d++)
                    {
                        if (d == 0 && x + 1 >= width) continue;
                        if (d == 1 && y + 1 >= height) continue;

                        int other = d == 0 ? here + 1 : here + width;
                        if (!board.Lines(here, other)) continue;

                        // The gem that lands inside the run is the one to carry the charm.
                        (cells[here], cells[other]) = (cells[other], cells[here]);
                        var hit = SiegeLayout.Runs(cells, width, height, null);
                        (cells[here], cells[other]) = (cells[other], cells[here]);

                        if (hit.Contains(other)) Offer(here);
                        if (hit.Contains(here)) Offer(other);
                    }
                }

            if (seats.Count == 0)
            {
                // Nowhere a swap would sweep it: anywhere it can stand without lining up.
                for (int i = 0; i < cells.Length; i++) Offer(i);
                if (seats.Count == 0) return -1;
            }

            int seat = seats[(int)(Next(ref seed) % (uint)seats.Count)];
            board.Stand(seat, charm);
            return seat;

            void Offer(int cell)
            {
                if (seats.Contains(cell)) return;

                charms[cell] = charm;
                bool lined = SiegeLayout.Lined(cells, width, height, charms, cell);
                charms[cell] = SiegeCharm.None;

                if (!lined) seats.Add(cell);
            }
        }

        // ------------------------------------------------------------------ noise
        /// <summary>xorshift32, so a seed is one number and both halves draw the same sequence.</summary>
        public static uint Next(ref uint seed)
        {
            uint x = seed == 0u ? 2463534242u : seed;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            seed = x;
            return x;
        }

        /// <summary>A unit float off the same stream.</summary>
        public static float Unit(ref uint seed) => (Next(ref seed) & 0xFFFFu) / 65536f;
    }
}
