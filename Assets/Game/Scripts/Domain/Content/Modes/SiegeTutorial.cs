using GlimmerGrove.Modes;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The board the opening tutorial is played on, and the readings its script is driven by.
    ///
    /// <para>
    /// <b>It is a real siege, and that is the whole design.</b> A tutorial drawn as a diagram
    /// teaches a diagram: the player then has to translate it onto a board they have never seen.
    /// So this is <c>SiegeBoard</c>, <c>SiegeView</c>, the real gems, the real turrets, the real
    /// raiders and the real clock - the same code the first rung runs - with one wave on it and a
    /// script standing beside it. Nothing below re-implements a rule.
    /// </para>
    /// <para>
    /// <b>It is authored here rather than in a chapter body, and that is not a breach of
    /// invariant 4.</b> Invariant 4 is about <em>content</em>: a chapter must never need a code
    /// change. This is not a chapter and never becomes one - it has no <c>LevelId</c>, no record,
    /// no stars, no rewards, no place in the manifest and no gate. What it is, is the fixture
    /// every siege rule test already builds by hand (<c>SiegeRuleTests</c> builds a
    /// <c>SiegeLayout</c> exactly this way), promoted to something the game can draw. Authoring
    /// it as content would put the board in one file and the script that points at particular
    /// cells of it in another, where nothing could hold the two together.
    /// </para>
    /// <para>
    /// <b>The one rule the script adds is that this board cannot be lost</b>, and that rule is
    /// not here - it is <c>SiegeBoard.Sheltered</c>, read where a ward is hurt. A first-timer
    /// who puts the phone down must not come back to a defeat panel, and a tutorial whose fail
    /// state is real is a tutorial that has to explain one. Everything else - what a match
    /// feeds, what an overcharge throws, how far a raider walks - is the shipped mode answering
    /// for itself.
    /// </para>
    /// <para>
    /// <b>Nothing here writes to a board.</b> Every scripted change goes through a method the
    /// board or the ward already owns (<c>SiegeBoard.Pour</c>, <c>.Kindle</c>,
    /// <c>.Sheltered</c>), so this class is a description: the field, where the hand points, and
    /// how much a feed is worth. It holds no state and mutates nothing.
    /// </para>
    /// </summary>
    public static class SiegeTutorial
    {
        /// <summary>The field, in the shape the first rung of Thornwatch uses.</summary>
        public const int Width = 8, Height = 5;

        /// <summary>
        /// The colours: all four, because the first rung of the chapter this leads into deals
        /// all four (2026-10-26) and a tutorial teaches the board the player is about to meet.
        /// On three a run of four came along on most moves, which would teach the gem bomb as
        /// something that happens rather than something you make.
        /// </summary>
        public const string Deal = "rgby";

        /// <summary>The line, one turret per colour. See <see cref="Deal"/>.</summary>
        public const string Line = "rgby";

        /// <summary>
        /// The one wave: three of each colour, walking on one at a time - **red first**, the
        /// colour of the taught match, so the first turret a player fuels has the first raider to
        /// shoot at.
        ///
        /// <para>
        /// <b>Twelve, and it has been three and then six.</b> A wave walks on at
        /// <c>SiegeTuning.RaiderSpacing</c> rather than all together, so a short one is a
        /// trickle that is over before the player has finished reading the board - and the whole
        /// of what this mode is about is a hill with something on it. Three was not a raid; six
        /// still ended too quickly; twelve keeps bodies arriving for a quarter of a minute and
        /// is still one wave, in the sense that matters: nothing arrives in a second act, and
        /// the hill is never cleared and then repopulated.
        /// </para>
        /// <para>
        /// <b>Every colour evenly rather than twelve of one</b>, so that whichever turret the player
        /// fuels has something of its own to shoot at - a turret burns its own colour and little
        /// else. It is also what makes <see cref="SiegeBoard.Kindle"/> certain to finish: every
        /// raider on this hill is answered by a turret standing in front of it.
        /// </para>
        /// <para>
        /// <b>No boss, no bomber, no bulwark, no cog and no charm.</b> Each of those is a second
        /// thing to learn, and the tutorial teaches two.
        /// </para>
        /// </summary>
        public const string Wave = "rgbyrgbyrgby";

        /// <summary>
        /// The field, authored settled and with a swap planted where a thumb can reach it.
        ///
        /// <para>
        /// <b>Two moves were planted, and nothing else was left</b> (searched rather than typed,
        /// ten of each colour): <see cref="TaughtA"/> ↔ <see cref="TaughtB"/> lines up three reds
        /// along the fourth row - the first lesson - and <see cref="FourA"/> ↔ <see cref="FourB"/>
        /// lines up four blues along the bottom row - the gem bomb. The one other move on the
        /// field is the three the bomb's own pattern always allows beside it. The four sits in the
        /// bottom row and in columns the first match never touches, so it is still standing when
        /// the second lesson asks for it.
        /// </para>
        /// <para>
        /// <b>Do not retype this by eye.</b> <c>TutorialTests</c> holds all of it - that the field
        /// is settled, that the taught pair really lines something up, and that the pair is
        /// adjacent - because a board that is one letter wrong is a tutorial that points a hand at
        /// a move the rules refuse, and nothing else here could see it.
        /// </para>
        /// </summary>
        public static readonly string[] Rows =
        {
            "yrgyrgrb",
            "bbyrgyyg",
            "bygbbygr",
            "grrgrgbr",
            "ygrybbyb",
        };

        /// <summary>
        /// The gem the coaching hand picks up, at (4,3), and the cell it slides it into, (3,3) -
        /// lining up three reds along the fourth row.
        ///
        /// <para>
        /// <b>Chosen rather than searched, and the difference matters on this one board.</b>
        /// <c>SiegeBoard.FindSwap</c> answers the first pair in reading order, which on this field
        /// is up near the top edge - a long reach on a phone and a poor first gesture. These two
        /// sit low and central. <see cref="Taught"/> falls back to the search if they ever stop
        /// working, so the worst a mistyped row can do is move the hand rather than break it.
        /// </para>
        /// <para>
        /// <b><see cref="TaughtA"/> is the red, and the order is the lesson.</b> The swap lines
        /// up three reds along the fourth row, so the gem the hand picks up has to be the green
        /// that completes them - the one the ring is on and the one the player then watches light
        /// up with the other two. It shipped the other way round: the hand picked up the red at
        /// (3,3) and slid it right, so the gem ringed and swiped was the one gem on the row that
        /// was <em>not</em> part of the match, and the first thing the game ever demonstrated read
        /// as pointing at the wrong gem. <see cref="Oriented"/> is the rule and this pair is
        /// authored to obey it, so a re-typed row is caught by the fixture rather than redrawn.
        /// </para>
        /// </summary>
        public const int TaughtA = 3 * Width + 4, TaughtB = 3 * Width + 3;

        /// <summary>
        /// The gem bomb's move (MODES.md 37fk): the blue at (6,3) slid down into (6,4), lining up
        /// four blues along the bottom row. <see cref="FourA"/> is the gem that joins the run, for
        /// <see cref="TaughtA"/>'s reason. <see cref="Four"/> falls back to a search if the field
        /// has moved under it.
        /// </summary>
        public const int FourA = 3 * Width + 6, FourB = 4 * Width + 6;

        /// <summary>
        /// How long one feed takes to climb the tube. See <see cref="Feed"/>.
        ///
        /// Long enough to be watched and short enough that nobody waits: the point of pouring a
        /// feed rather than setting it is that the player sees their own match arrive as fuel.
        /// </summary>
        public const float PourSeconds = .55f;

        /// <summary>
        /// How many matches fill a tube here, once the gem bomb has been taught.
        ///
        /// <para>
        /// <b>Three, and the first cut was one.</b> A tube really holds fourteen gems' worth
        /// (<c>SiegeTuning.WardCapacity</c> against <c>FuelPerGem</c>), which is four or five
        /// matches - a good pace for a rung and far too long for the second thing a player is
        /// ever told. Filling it from a single match fixed the waiting and broke something
        /// worse: a turret that goes from empty to armed on one move teaches that an overcharge
        /// is what happens, rather than something you build. Reported in one word: <em>instant</em>.
        /// </para>
        /// <para>
        /// <b>Two since 2026-10-26</b>: by the time the tube is taught the player has already
        /// matched three and set off a bomb, so the line already holds fuel, and a tube that
        /// climbs once and lights on the next move still reads as built rather than handed over. The shortfall is the tutorial's own (<see cref="Feed"/>); what the
        /// player sees is a tube filling from their matches, which is exactly what will happen on
        /// the first rung.
        /// </para>
        /// <para>
        /// <b>A target rather than a contract, and a caller that treats it as one hangs.</b> The
        /// match's own fuel lands beside the tutorial's share, so a player who matches three
        /// times into one turret before the wave arrives brims it on the second - and a turret
        /// whose tube is spending itself on the hill may want a fourth. The loop ends on
        /// <see cref="Charged"/>; this only decides how generous each feed is.
        /// </para>
        /// </summary>
        public const int MatchesToArm = 2;

        /// <summary>The rules a tutorial run is played by.</summary>
        /// <remarks>
        /// <b>The starter line, explicitly, rather than the player's own.</b> Two reasons and
        /// both are rules. The tutorial teaches the game as it is dealt, and a loadout is
        /// something the player has not met yet; and the starter is the one line whose art is
        /// guaranteed to be resident (<c>SiegeMode.ArtFor</c> names it as the safety net), so a
        /// tutorial can never open on four white rectangles (invariant 7b).
        /// </remarks>
        public static SiegeRules Rules()
            => new SiegeRules(Layout(), WardLine.Starter(WardCatalog.Default), longMatches: true);

        /// <summary>The board this tutorial is played on. Never null; see <see cref="Fault"/>.</summary>
        public static SiegeLayout Layout()
        {
            ProtoGrid.TryRead(Rows, Width, Height, SiegeLayout.Cells, out var grid, out _);

            return new SiegeLayout(grid, Deal, Line, new[] { Wave }, string.Empty);
        }

        /// <summary>
        /// What is wrong with the board above, or null - the same reading every authored siege
        /// gets, asked of the one that is not authored.
        ///
        /// It exists so a fixture can ask, because this layout goes through none of the content
        /// gates: it is not in the manifest, so <c>content.py</c> never sees it, and a field that
        /// stopped being settled would simply cascade in front of a first-time player.
        /// </summary>
        public static string Fault()
        {
            if (!ProtoGrid.TryRead(Rows, Width, Height, SiegeLayout.Cells, out _, out var error))
                return error;

            return Layout().Fault;
        }

        // ------------------------------------------------------------------ the script
        /// <summary>
        /// The swap the hand demonstrates: <see cref="TaughtA"/> ↔ <see cref="TaughtB"/> while
        /// the field still accepts it, and whatever the board offers otherwise. <c>A</c> is
        /// always the gem the hand picks up and <c>B</c> where it goes (<see cref="Oriented"/>).
        ///
        /// <b>Asked of the board rather than trusted</b>, through the one door every other reader
        /// goes through (<c>SiegeBoard.Lines</c>), so a hand is never drawn over a move the drag
        /// would refuse.
        /// </summary>
        public static SiegeSwap Taught(SiegeBoard board)
        {
            if (board == null) return default;

            if (board.Lines(TaughtA, TaughtB))
                return Oriented(board, new SiegeSwap(TaughtA, TaughtB, 0));

            return Oriented(board, board.FindSwap(0));
        }

        /// <summary>
        /// The same swap, turned so that <c>A</c> is the gem that joins the line it makes and
        /// <c>B</c> is the cell it slides into.
        ///
        /// <para>
        /// <b>A swap is two gems trading places, and a demonstration is one gem being moved.</b>
        /// Which of the two the hand picks up is the whole of what the player is shown: the ring
        /// sits on it, the finger lands on it, and the sentence beside it says <em>line up three
        /// of a colour</em>. If that gem is the one the match pushes out of the way, the player
        /// is told to swipe a gem and then watches three others light up - the fault the first
        /// cut shipped with. So the mover is the gem that stands in a run once the trade is made,
        /// read off the same rule the board scores matches by (<c>SiegeLayout.Runs</c>) rather
        /// than off the colours by hand.
        /// </para>
        /// <para>
        /// <b>Both or neither leaves the order alone.</b> A trade that lines up both gems (two
        /// matches at once) has no wrong answer, and a trade that lines up neither is one
        /// <c>SiegeBoard.Lines</c> would have refused, so there is nothing to turn.
        /// </para>
        /// </summary>
        public static SiegeSwap Oriented(SiegeBoard board, SiegeSwap swap)
        {
            if (board == null || !swap.Found) return swap;

            return Mover(board, swap.A, swap.B) == swap.B
                ? new SiegeSwap(swap.B, swap.A, swap.At)
                : swap;
        }

        /// <summary>
        /// Which of two adjacent cells holds the gem that joins a run once they trade places:
        /// <paramref name="a"/> unless only <paramref name="b"/>'s gem does. See <see cref="Oriented"/>.
        /// </summary>
        public static int Mover(SiegeBoard board, int a, int b)
        {
            if (board == null) return a;
            if (a < 0 || b < 0 || a >= board.Count || b >= board.Count) return a;

            var cells = new char[board.Count];
            var charms = new SiegeCharm[board.Count];

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = board.At(i);
                charms[i] = board.CharmAt(i);
            }

            char cell = cells[a];
            cells[a] = cells[b];
            cells[b] = cell;

            var charm = charms[a];
            charms[a] = charms[b];
            charms[b] = charm;

            var runs = SiegeLayout.Runs(cells, board.Width, board.Height, charms);

            // After the trade a's gem stands at b and b's at a.
            bool aJoins = runs.Contains(b);
            bool bJoins = runs.Contains(a);

            return bJoins && !aJoins ? b : a;
        }

        /// <summary>
        /// The gem bomb's move: <see cref="FourA"/> ↔ <see cref="FourB"/> while the field still
        /// lines four up there, else the first move on the field that lines up four or more, else
        /// nothing (<see cref="SiegeSwap.Found"/> false). Oriented, for <see cref="Taught"/>'s
        /// reason.
        /// </summary>
        public static SiegeSwap Four(SiegeBoard board)
        {
            if (board == null) return default;

            if (Lengthens(board, FourA, FourB))
                return Oriented(board, new SiegeSwap(FourA, FourB, 0));

            for (int i = 0; i < board.Count; i++)
            {
                int x = i % board.Width, y = i / board.Width;

                if (x + 1 < board.Width && Lengthens(board, i, i + 1))
                    return Oriented(board, new SiegeSwap(i, i + 1, 0));

                if (y + 1 < board.Height && Lengthens(board, i, i + board.Width))
                    return Oriented(board, new SiegeSwap(i, i + board.Width, 0));
            }

            return default;
        }

        /// <summary>
        /// Whether trading <paramref name="a"/> and <paramref name="b"/> is a move the board takes
        /// <em>and</em> lines up a straight run of four or more through either of them.
        /// </summary>
        static bool Lengthens(SiegeBoard board, int a, int b)
        {
            if (!board.Lines(a, b)) return false;

            var cells = new char[board.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = board.At(i);

            char cell = cells[a];
            cells[a] = cells[b];
            cells[b] = cell;

            return Through(cells, board.Width, board.Height, a) >= SiegeLongMatch.BombRun
                || Through(cells, board.Width, board.Height, b) >= SiegeLongMatch.BombRun;
        }

        /// <summary>The longest straight run of one colour through <paramref name="cell"/>.</summary>
        static int Through(char[] cells, int width, int height, int cell)
        {
            char c = cells[cell];
            if (SiegeLayout.Letters.IndexOf(c) < 0) return 0;

            int x = cell % width, y = cell / width;

            int across = 1;
            for (int k = x - 1; k >= 0 && cells[y * width + k] == c; k--) across++;
            for (int k = x + 1; k < width && cells[y * width + k] == c; k++) across++;

            int down = 1;
            for (int k = y - 1; k >= 0 && cells[k * width + x] == c; k--) down++;
            for (int k = y + 1; k < height && cells[k * width + x] == c; k++) down++;

            return across > down ? across : down;
        }

        /// <summary>
        /// Whether this board's one charge has been banked and thrown already: the line no longer
        /// banks (<see cref="SiegeBoard.OneCharge"/>) and no turret holds one. The tube lesson
        /// then has nothing left to wait for.
        /// </summary>
        public static bool Spent(SiegeBoard board)
        {
            if (board == null || !board.OneCharge) return false;

            for (int i = 0; i < board.Wards.Count; i++)
                if (board.Wards[i].Banks || board.Wards[i].Charges > 0) return false;

            return true;
        }

        /// <summary>The cell a gem bomb is standing in, or -1 while none is.</summary>
        public static int Bomb(SiegeBoard board)
        {
            if (board == null) return -1;

            for (int i = 0; i < board.Count; i++)
                if (SiegeCharms.IsBomb(board.CharmAt(i))) return i;

            return -1;
        }

        /// <summary>
        /// The standing turret closest to banking a charge, or -1: where the tutorial's feeds go
        /// while the tube is being taught, so the one that lights is the one already nearly lit.
        /// </summary>
        public static int Fullest(SiegeBoard board)
        {
            if (board == null) return -1;

            int best = -1;
            float most = -1f;

            for (int i = 0; i < board.Wards.Count; i++)
            {
                var ward = board.Wards[i];
                if (!ward.Alive || ward.Charge <= most) continue;

                most = ward.Charge;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// The turret the player's match has just fed and which is not yet armed, or -1.
        ///
        /// <b>Read off the line rather than off the swap</b>, because fuel crosses the field
        /// before it arrives (<c>SiegeTuning</c>'s flight): the moment worth reacting to is the
        /// moment the tube moves, which is a fact about the turret and not about the gems.
        /// </summary>
        public static int Fed(SiegeBoard board)
        {
            if (board == null) return -1;

            var line = board.Wards;
            for (int i = 0; i < line.Count; i++)
                if (line[i].Alive && line[i].Charges == 0 && line[i].Fuel > 0f) return i;

            return -1;
        }

        /// <summary>
        /// What one match is worth here: its share of a tube, or whatever is left of one on the
        /// feed that arms it.
        ///
        /// <para>
        /// <b>The tutorial's one gift, and it is given where it cannot be seen as one.</b> A
        /// real match pays <c>SiegeTuning.FuelPerGem</c> a gem and a tube holds fourteen of
        /// them; here each match pays a third of a tube, so the third one lights it
        /// (<see cref="MatchesToArm"/>). What the player sees is exactly what they will see on
        /// the first rung - motes arrive, the tube climbs - at a pace the second sentence of a
        /// tutorial can afford.
        /// </para>
        /// <para>
        /// <b>The last feed is measured rather than shared</b>, and that is what makes the
        /// arming certain: thirds of a tube do not have to add up to one when the player has fed
        /// three different turrets, or when the ward's own fire has spent some of it in between.
        /// Asking the board what is left (<see cref="SiegeBoard.ToBrim"/>) is the only reading
        /// that cannot drift.
        /// </para>
        /// </summary>
        public static float Feed(SiegeBoard board, int ward, int match)
        {
            if (board == null || ward < 0 || ward >= board.Wards.Count) return 0f;

            float room = board.ToBrim(ward);

            // The feed that arms it: everything that is left, and never nought - `SiegeWard.Fill`
            // refuses nought outright, so a tube that has somehow arrived brim-full is handed a
            // drop rather than a zero that would quietly do nothing and leave a caller waiting.
            if (match >= MatchesToArm) return room > Drop ? room : Drop;

            float share = board.Wards[ward].Capacity / MatchesToArm;
            return share < room ? share : room;
        }

        /// <summary>The smallest pour that still banks a charge. See <see cref="Feed"/>.</summary>
        const float Drop = .01f;

        /// <summary>
        /// The first turret holding a banked charge, or -1 while none does.
        ///
        /// <para>
        /// <b>Not <see cref="Armed"/>, and the pair is the difference between "the tube is full"
        /// and "tapping it would do something".</b> This is the one the script's feeding loop
        /// ends on, because a tube that has banked is a tube that has finished being filled
        /// whether or not there is anything on the hill yet.
        /// </para>
        /// <para>
        /// <b>It is what stops that loop being able to hang.</b> Counting feeds instead looks
        /// right and is not: <see cref="Feed"/> caps what the <em>tutorial</em> pours, while the
        /// match's own fuel lands beside it through the ordinary door - so a player who matches
        /// three times before the wave arrives can brim a tube a feed early. A loop owed one
        /// more feed then waits for <see cref="Fed"/>, which skips a ward that has already
        /// banked, and if the next match is that same colour it waits for ever.
        /// </para>
        /// </summary>
        public static int Charged(SiegeBoard board)
        {
            if (board == null) return -1;

            for (int i = 0; i < board.Wards.Count; i++)
                if (board.Wards[i].Alive && board.Wards[i].Charges > 0) return i;

            return -1;
        }

        /// <summary>
        /// The turret the player is being asked to tap, or -1 when there is nothing to tap yet.
        ///
        /// <b><c>SiegeBoard.CanOvercharge</c> and not "is it armed"</b>, which is that method's
        /// own rule: a tube with nothing on the hill to throw at is a control that would refuse
        /// the tap, and a lesson that rings a control which then refuses teaches the refusal.
        /// </summary>
        public static int Armed(SiegeBoard board)
        {
            if (board == null) return -1;

            for (int i = 0; i < board.Wards.Count; i++)
                if (board.CanOvercharge(i)) return i;

            return -1;
        }

        /// <summary>
        /// How many raiders should be walking before the second panel goes up.
        ///
        /// <b>Because a payoff wants something to land on.</b> A wave walks on one raider at a
        /// time (<c>SiegeTuning.RaiderSpacing</c>), so the frame <see cref="Armed"/> first
        /// answers is the frame the <em>first</em> body appears - and an overcharge taught
        /// against a hill with one creeper on it is the biggest thing in the mode spent on the
        /// smallest thing in it. Two is enough for a blast to read as a blast and is reached a
        /// second and a half later, which the player spends watching a wave arrive.
        /// </summary>
        public const int Crowd = 2;

        /// <summary>
        /// The turret the second panel rings, waiting for a crowd while
        /// <paramref name="patient"/> and taking whatever is armed once it is not.
        ///
        /// <para>
        /// <b>The impatience is a parameter rather than a clock in here</b>, so this stays a pure
        /// reading of the board and the one thing that could hang the script - a crowd that never
        /// arrives, on a wave somebody later shortens - is answered by the caller's own deadline
        /// instead of by a rule nobody can see. See <c>TutorialScreen.CrowdCeiling</c>.
        /// </para>
        /// </summary>
        public static int Target(SiegeBoard board, bool patient)
        {
            if (board == null) return -1;
            if (patient && board.OnTheHill < Crowd) return -1;

            return Armed(board);
        }

        /// <summary>
        /// <b>Deliberately empty of anything that writes to the line.</b>
        ///
        /// Everything this class used to do to a board is now a method on the board: the
        /// guarantee that the line cannot fall is <c>SiegeBoard.Sheltered</c>, read where a ward
        /// is hurt rather than repaired afterwards; the closing sweep is
        /// <c>SiegeBoard.Kindle</c>; and a feed goes in through <c>SiegeBoard.Pour</c>, which is
        /// <c>SiegeWard.Fill</c>'s own door. What is left here is the board, the readings and
        /// the pacing - a description of a tutorial, holding no state and mutating nothing.
        /// </summary>
    }
}
