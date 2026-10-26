using System.Collections.Generic;
using System.Reflection;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>Exercise the real screen/grid refresh path, including recycling and scroll state.</summary>
    public sealed class NormalLeaderboardRefreshTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _root;
        LeaderboardScreen _screen;
        GridView _grid;
        readonly List<Cell> _cells = new List<Cell>();
        LeaderboardEntry[] _rows;

        sealed class Cell : IGridCell
        {
            readonly LeaderboardScreen _screen;
            public RectTransform Root { get; }
            public int Binds;
            public LeaderboardEntry Entry;

            public Cell(LeaderboardScreen screen, RectTransform parent)
            {
                _screen = screen;
                Root = new GameObject("RefreshCell", typeof(RectTransform)).GetComponent<RectTransform>();
                Root.SetParent(parent, false);
                Root.sizeDelta = new Vector2(940f, 160f);
            }

            public void Bind(int index)
            {
                var board = (LeaderboardBoard)typeof(LeaderboardScreen).GetField("_board", Fields).GetValue(_screen);
                Assert.Less(index, board.Entries.Count, "Never bind a row beyond the updated snapshot");
                Entry = board.Entries[index];
                Binds++;
            }
        }

        static void Set(object target, string field, object value)
            => target.GetType().GetField(field, Fields).SetValue(target, value);

        static LeaderboardEntry At(int index, long? score = null, int keeperLevel = 1)
            => new LeaderboardEntry(index + 1, "u" + index, "Keeper " + index, "", keeperLevel,
                normalScore: score ?? 245840 - index * 100);

        static LeaderboardBoard Board(IReadOnlyList<LeaderboardEntry> rows, long built = 1, int population = 100)
            => new LeaderboardBoard(LeaderboardBoard.Normal, rows, built, population);

        Dictionary<int, IGridCell> Live
            => (Dictionary<int, IGridCell>)typeof(GridView).GetField("_live", Fields).GetValue(_grid);

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("NormalRefreshFixture", typeof(RectTransform))
                { hideFlags = HideFlags.HideAndDontSave };
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1000f);
            _screen = _root.AddComponent<LeaderboardScreen>();
            _screen.ShowBoard(LeaderboardBoard.Normal);
            var viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.SetParent(_root.transform, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(0f, 132f);
            viewport.offsetMax = new Vector2(0f, -368f);
            _grid = GridView.Attach(viewport, 1, 960f, 176f, parent =>
            {
                var cell = new Cell(_screen, parent);
                _cells.Add(cell);
                return cell;
            }, 0f, 0f);
            Set(_screen, "_grid", _grid);
            Set(_screen, "_viewport", viewport);
            Set(_screen, "_rowsOwnerId", CloudState.UserId);
            _rows = new LeaderboardEntry[100];
            for (int i = 0; i < _rows.Length; i++) _rows[i] = At(i);
            Set(_screen, "_board", Board(_rows));
            _grid.Show(_rows.Length, animate: false);
            _grid.Scroll.content.anchoredPosition = new Vector2(0f, 2000f);
            _grid.Relayout();
            Assert.IsTrue(Live.ContainsKey(12), "The changed test row must actually be visible");
            Set(_grid, "_shownAt", -1000f);
            _grid.Scroll.velocity = new Vector2(0f, 30f);
            foreach (var cell in _cells) cell.Binds = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root) Object.DestroyImmediate(_root);
            _cells.Clear();
        }

        void AssertPositionKept(int allocated)
        {
            Assert.AreEqual(new Vector2(0f, 2000f), _grid.Scroll.content.anchoredPosition);
            Assert.AreEqual(allocated, _cells.Count, "Refreshing must reuse existing cards");
            Assert.AreEqual(-1000f, typeof(GridView).GetField("_shownAt", Fields).GetValue(_grid),
                "Refreshing must not restart the entrance animation");
        }

        [Test]
        public void NewPublishTimestampWithIdenticalRowsDoesNoCardWork()
        {
            int allocated = _cells.Count;
            var oldRoot = Live[12].Root;
            _screen.ApplyBoard(Board((LeaderboardEntry[])_rows.Clone(), built: 999, population: 4000));
            foreach (var cell in _cells) Assert.AreEqual(0, cell.Binds);
            Assert.AreSame(oldRoot, Live[12].Root);
            Assert.AreEqual(new Vector2(0f, 30f), _grid.Scroll.velocity);
            AssertPositionKept(allocated);
            Assert.AreEqual(999, ((LeaderboardBoard)typeof(LeaderboardScreen).GetField("_board", Fields)
                .GetValue(_screen)).BuiltUnix, "Keep the newest freshness metadata without repainting cards");
        }

        [Test]
        public void OneChangedVisibleRowBindsOnlyThatExistingCard()
        {
            int allocated = _cells.Count;
            var cell = (Cell)Live[12];
            var next = (LeaderboardEntry[])_rows.Clone();
            next[12] = At(12, score: _rows[12].NormalScore + 100);
            _screen.ApplyBoard(Board(next, built: 2));
            Assert.AreSame(cell, Live[12]);
            Assert.AreEqual(next[12].NormalScore, cell.Entry.NormalScore);
            foreach (var other in _cells) Assert.AreEqual(other == cell ? 1 : 0, other.Binds);
            AssertPositionKept(allocated);
        }

        [Test]
        public void OffscreenChangesDoNoVisibleCardWork()
        {
            int allocated = _cells.Count;
            var next = (LeaderboardEntry[])_rows.Clone();
            next[90] = At(90, score: 999);
            _screen.ApplyBoard(Board(next));
            foreach (var cell in _cells) Assert.AreEqual(0, cell.Binds);
            AssertPositionKept(allocated);
        }

        [Test]
        public void ReorderedPlayersUpdateInPlaceWithoutMovingTheViewport()
        {
            int allocated = _cells.Count;
            var a = Live[12];
            var b = Live[13];
            var next = (LeaderboardEntry[])_rows.Clone();
            next[12] = new LeaderboardEntry(13, _rows[13].OwnerId, _rows[13].Name, "", 1,
                normalScore: _rows[13].NormalScore);
            next[13] = new LeaderboardEntry(14, _rows[12].OwnerId, _rows[12].Name, "", 1,
                normalScore: _rows[12].NormalScore);
            _screen.ApplyBoard(Board(next));
            Assert.AreSame(a, Live[12]);
            Assert.AreSame(b, Live[13]);
            Assert.AreEqual("u13", ((Cell)a).Entry.OwnerId);
            Assert.AreEqual("u12", ((Cell)b).Entry.OwnerId);
            AssertPositionKept(allocated);
        }

        [Test]
        public void AChangedRowAlsoUpdatesTheMetadataUsedWhenTapped()
        {
            var next = (LeaderboardEntry[])_rows.Clone();
            next[12] = At(12, keeperLevel: 33);
            _screen.ApplyBoard(Board(next));
            Assert.AreEqual(33, ((Cell)Live[12]).Entry.KeeperLevel);
            Assert.AreEqual(1, ((Cell)Live[12]).Binds);
        }

        [Test]
        public void ShrinkingAndGrowingTheListKeepsTheScrollAndExistingCards()
        {
            int allocated = _cells.Count;
            var cell = Live[12];
            var fewer = new List<LeaderboardEntry>(_rows);
            fewer.RemoveAt(99);
            _screen.ApplyBoard(Board(fewer));
            Assert.AreSame(cell, Live[12]);
            AssertPositionKept(allocated);
            _screen.ApplyBoard(Board(_rows));
            Assert.AreSame(cell, Live[12]);
            AssertPositionKept(allocated);
        }

        [Test]
        public void AnEmptySnapshotRetiresOldCardsWithoutRestartingTheList()
        {
            _screen.ApplyBoard(Board(new LeaderboardEntry[0]));
            Assert.AreEqual(0, Live.Count);
            Assert.AreEqual(Vector2.zero, _grid.Scroll.content.anchoredPosition);
            Assert.AreEqual(-1000f, typeof(GridView).GetField("_shownAt", Fields).GetValue(_grid));
        }

        [Test]
        public void PublishingOurOwnCardDoesNotFetchOrRebuildTheNormalSnapshot()
        {
            int allocated = _cells.Count;
            Set(_screen, "_normalRefreshAt", 12345f);
            typeof(LeaderboardScreen).GetMethod("OnPublished", Fields).Invoke(_screen, null);
            Assert.AreEqual(12345f, typeof(LeaderboardScreen).GetField("_normalRefreshAt", Fields).GetValue(_screen));
            foreach (var cell in _cells) Assert.AreEqual(0, cell.Binds);
            AssertPositionKept(allocated);
        }
    }
}
