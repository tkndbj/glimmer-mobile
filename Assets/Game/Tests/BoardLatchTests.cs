using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The board's latch, and the one thing every control on the bottom bar depends on: that
    /// <see cref="BoardView.Locked"/> moving is something the screen hears about.
    ///
    /// <para>
    /// This exists because of a bug reported from play — "I use one hint and then I cannot
    /// use another one". There was no cooldown and nothing wrong with the pool.
    /// <c>PlayScreen</c> recomputes the hint and undo buttons from
    /// <see cref="BoardView.OnChanged"/>; the hint's reveal latches the board, every tween
    /// along the way raises that event <em>while it is still latched</em>, and the unlatch at
    /// the end raised nothing. So the last thing the screen was ever told was "disabled", and
    /// it stayed disabled for the rest of the run unless the player happened to turn a tile.
    /// The same hole sat under the entry animation, which is why the button was also dead
    /// from the first frame of every glade.
    /// </para>
    /// <para>
    /// <b>These tests are written the way the screen reads the board</b>, which is the only
    /// framing that could have caught it: they keep the value <em>the handler was told</em>
    /// on the last event it received, because that is what ends up on the button. Asserting
    /// <c>board.CanHint</c> directly after the dust settles passes on the broken code too —
    /// the state was always right, and nobody was ever told.
    /// </para>
    /// <para>
    /// They need the Editor (a <c>GameObject</c> and a real component are ECalls the offline
    /// runner cannot make) and they drive <see cref="Tween.Tick"/> by hand rather than
    /// waiting on frames, which is the seam <c>TweenOwnerTests</c> established for exactly
    /// this kind of proof.
    /// </para>
    /// </summary>
    public sealed class BoardLatchTests
    {
        GameObject _root;

        /// <summary>
        /// The hint's reveal beckons the tile, which spawns a <c>Ripple</c> that tidies
        /// itself up with <c>Object.Destroy</c> — correct in a player, and an error log
        /// outside play mode ("Destroy may not be called from edit mode"). Unity's runner
        /// fails a test on any unexpected error log, so without this the three cases that
        /// take a hint fail on the animation rather than on anything they assert.
        ///
        /// <para>
        /// Deliberately not <c>LogAssert.Expect</c>: the ripple fires once per beckoned
        /// tile, so the count is a fact about the animation rather than about the rule, and
        /// a test that has to be edited whenever a flourish changes is a test that gets
        /// deleted. Every claim here is an explicit assertion, so nothing is resting on the
        /// absence of a log line — and the suite still has <c>LogAssert</c> tests elsewhere
        /// for the rules that are genuinely about what was logged.
        /// </para>
        /// </summary>
        /// <summary>
        /// Set from inside the test body rather than from <c>[SetUp]</c>, which is too early:
        /// the runner opens its log scope per test after set-up has run, and the flag is a
        /// property of that scope.
        /// </summary>
        static void IgnoreEditModeAnimationLogs()
            => UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        /// <summary>Frames at 50fps, enough to outlast every beat here.</summary>
        static void Settle(float seconds = 4f)
        {
            int frames = Mathf.CeilToInt(seconds / .02f);
            for (int i = 0; i < frames; i++) Tween.Inst.Tick(.02f, .02f);
        }

        /// <summary>
        /// A red heart, <paramref name="conduits"/> straight runs each a quarter turn off
        /// their solution, and a red critter. Every conduit owes exactly one turn, so a
        /// board of three needs three hints and is not finished by the first.
        /// </summary>
        BoardView Board(int conduits, out Puzzle puzzle)
        {
            IgnoreEditModeAnimationLogs();

            var cells = new string[conduits + 2];
            cells[0] = "*E#R/0";
            for (int i = 0; i < conduits; i++) cells[i + 1] = "-EW/1";
            cells[conduits + 1] = "@W#R/0";

            int width = cells.Length;
            var layout = new LevelLayout(width, 1, new[] { string.Join(" ", cells) });
            var parsed = LevelGridParser.Parse(layout);
            Assert.IsTrue(parsed.Ok, string.Join("; ", parsed.Errors));

            int par = Mathf.Max(1, PuzzleFactory.MinimumMoves(parsed.Cells));
            puzzle = new Puzzle(LevelId.Parse("t_level"), width, 1,
                                LevelTuning.Default(par), parsed.Cells);
            puzzle.Evaluate();

            _root = new GameObject("~BoardLatchTests", typeof(RectTransform), typeof(Canvas))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var host = new GameObject("~Host", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            host.transform.SetParent(_root.transform, false);

            var rt = (RectTransform)host.transform;
            rt.sizeDelta = new Vector2(200f * width, 300f);

            return host.AddComponent<BoardView>();
        }

        /// <summary>
        /// What the screen would have painted the last time it was told anything — the whole
        /// point of these tests. <c>PlayScreen.Refresh</c> is this line.
        /// </summary>
        sealed class Bar
        {
            public bool HintEnabled;
            public bool UndoEnabled;
            public int Events;

            public void Watch(BoardView board)
                => board.OnChanged = () =>
                {
                    Events++;
                    HintEnabled = board.CanHint;
                    UndoEnabled = board.CanUndo;
                };
        }

        // ------------------------------------------------------------- the entry
        /// <summary>
        /// The board raises during its entry animation and unlatches when it lands. If the
        /// unlatch says nothing, a player opens a glade to a hint button that is already
        /// dead — which is how this shipped.
        /// </summary>
        [Test]
        public void TheGladeOpensWithALiveHintButton()
        {
            var board = Board(3, out var puzzle);
            var bar = new Bar();
            bar.Watch(board);

            board.Build((RectTransform)board.transform, puzzle, Pal.BoardTheme.From(Color.gray));
            Assert.IsTrue(board.Locked, "the raise animation latches the board");

            Settle();

            Assert.IsFalse(board.Locked, "and hands it back when it lands");
            Assert.IsTrue(bar.HintEnabled, "the screen has to hear about that, or the button never wakes up");
        }

        // -------------------------------------------------------------- the hint
        /// <summary>
        /// The reported bug, in one assertion: after a hint has played out, the last thing
        /// the screen was told must be that another one can be taken.
        /// </summary>
        [Test]
        public void AHintLeavesTheButtonReadyForTheNextOne()
        {
            var board = Board(3, out var puzzle);
            var bar = new Bar();
            bar.Watch(board);

            board.Build((RectTransform)board.transform, puzzle, Pal.BoardTheme.From(Color.gray));
            Settle();

            Assert.IsTrue(board.Hint(), "a board three turns from its solution has a hint to give");
            Assert.IsFalse(bar.HintEnabled, "and the button is dead while the conduit is turning");

            Settle();

            Assert.IsFalse(puzzle.Won, "one hint cannot finish a three-conduit board");
            Assert.IsTrue(bar.HintEnabled, "the button comes back when the reveal ends");
            Assert.IsTrue(bar.UndoEnabled, "and so does undo, which failed the same way");
        }

        /// <summary>
        /// Three in a row, because "the first one works" was never the complaint. Each is
        /// asked for through the same predicate the button is drawn from.
        /// </summary>
        [Test]
        public void EveryHintOnABoardIsAvailableInTurn()
        {
            var board = Board(3, out var puzzle);
            var bar = new Bar();
            bar.Watch(board);

            board.Build((RectTransform)board.transform, puzzle, Pal.BoardTheme.From(Color.gray));
            Settle();

            for (int i = 1; i <= 3; i++)
            {
                Assert.IsTrue(bar.HintEnabled, "the button is live before hint " + i);
                Assert.IsTrue(board.Hint(), "hint " + i + " is accepted");
                Settle();
            }

            Assert.IsTrue(puzzle.Won, "three hints solve a three-conduit board");
            Assert.IsFalse(bar.HintEnabled, "and then there is nothing left to point at");
        }

        /// <summary>
        /// The callback is what tells <c>PlayScreen</c> the pool may have just emptied, and
        /// it has to arrive on the beat the board comes back — not while it is still latched,
        /// where a panel would cover the conduit the hint was spent on.
        /// </summary>
        [Test]
        public void TheRevealReportsOnTheBeatTheBoardComesBack()
        {
            var board = Board(3, out var puzzle);
            board.Build((RectTransform)board.transform, puzzle, Pal.BoardTheme.From(Color.gray));
            Settle();

            bool lockedWhenTold = true;
            int calls = 0;

            Assert.IsTrue(board.Hint(() => { calls++; lockedWhenTold = board.Locked; }));
            Assert.AreEqual(0, calls, "not while the tiles are still turning");

            Settle();

            Assert.AreEqual(1, calls, "exactly once");
            Assert.IsFalse(lockedWhenTold, "and with the board already handed back");
        }

        // ------------------------------------------------------------- the latch
        /// <summary>
        /// Only a real move raises. Without this the property would be a repaint per
        /// assignment — several of the game's paths set the latch to what it already is —
        /// and it would not be safe to assign from inside a handler of its own event.
        /// </summary>
        [Test]
        public void TheLatchIsSilentWhenItDoesNotMove()
        {
            var board = Board(1, out var puzzle);
            var bar = new Bar();

            board.Build((RectTransform)board.transform, puzzle, Pal.BoardTheme.From(Color.gray));
            Settle();

            bar.Watch(board);
            int before = bar.Events;

            board.Locked = false;               // already false
            Assert.AreEqual(before, bar.Events, "assigning the value it already holds says nothing");

            board.Locked = true;
            Assert.AreEqual(before + 1, bar.Events, "a real change is one event");

            board.Locked = true;
            Assert.AreEqual(before + 1, bar.Events, "and repeating it is still one");
        }
    }
}
