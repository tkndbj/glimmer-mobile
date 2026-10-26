using System.Reflection;
using GlimmerGrove.Social;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Stepping into a keeper's profile and back lands the boards where the player left them
    /// (<c>LeaderboardScreen.ReturnFromProfile</c>, <c>GridView.Show(openOffset:)</c>), and only
    /// that way back does: the nav bar's key still opens at the top.
    /// </summary>
    public sealed class BoardReturnTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        const float RowH = 196f;

        GameObject _root;
        LeaderboardScreen _screen;
        GridView _grid;

        sealed class Cell : IGridCell
        {
            public RectTransform Root { get; }

            public Cell(RectTransform parent)
            {
                Root = new GameObject("ReturnCell", typeof(RectTransform)).GetComponent<RectTransform>();
                Root.SetParent(parent, false);
            }

            public void Bind(int index) { }
        }

        static readonly FieldInfo Left = typeof(LeaderboardScreen).GetField("s_left", Static);

        static LeaderboardBoard Board(int rows, string boardId = LeaderboardBoard.Shuffle)
        {
            var entries = new LeaderboardEntry[rows];
            for (int i = 0; i < rows; i++)
                entries[i] = new LeaderboardEntry(i + 1, "u" + i, "Keeper " + i, "", 1, shuffleWave: 200 - i);
            return new LeaderboardBoard(boardId, entries, 1, rows);
        }

        [SetUp]
        public void SetUp()
        {
            Left.SetValue(null, default((string, float)));

            _root = new GameObject("BoardReturnFixture", typeof(RectTransform))
                { hideFlags = HideFlags.HideAndDontSave };
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 1000f);
            _screen = _root.AddComponent<LeaderboardScreen>();

            var viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.SetParent(_root.transform, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(0f, 132f);
            viewport.offsetMax = new Vector2(0f, -368f);
            _grid = GridView.Attach(viewport, 1, 960f, RowH, parent => new Cell(parent), 0f, 0f);

            typeof(LeaderboardScreen).GetField("_grid", Instance).SetValue(_screen, _grid);
            typeof(LeaderboardScreen).GetField("_viewport", Instance).SetValue(_screen, viewport);
        }

        [TearDown]
        public void TearDown()
        {
            Left.SetValue(null, default((string, float)));
            if (_root) Object.DestroyImmediate(_root);
        }

        [Test]
        public void AReturnFromAProfileLandsWhereTheListWasLeft()
        {
            Left.SetValue(null, (LeaderboardBoard.Shuffle, 2000f));

            _screen.ReturnFromProfile();
            _screen.ApplyBoard(Board(100));

            Assert.AreEqual(2000f, _grid.Offset, "The list must open where the player left it");
            Assert.AreEqual(default((string, float)), Left.GetValue(null), "The place is taken once");
        }

        [Test]
        public void TheBoardTheReturnNamesIsTheBoardShown()
        {
            Left.SetValue(null, (LeaderboardBoard.Normal, 900f));
            _screen.ShowBoard(LeaderboardBoard.Shuffle);

            _screen.ReturnFromProfile();
            _screen.ApplyBoard(Board(100, LeaderboardBoard.Normal));

            Assert.AreEqual(900f, _grid.Offset);
        }

        [Test]
        public void AListThatShrankMeanwhileIsClampedToItsEnd()
        {
            Left.SetValue(null, (LeaderboardBoard.Shuffle, 50000f));

            _screen.ReturnFromProfile();
            _screen.ApplyBoard(Board(20));

            float most = 20 * RowH - 500f;
            Assert.AreEqual(most, _grid.Offset, .01f);
        }

        [Test]
        public void TheReturnIsSpentOnTheFirstPaint()
        {
            Left.SetValue(null, (LeaderboardBoard.Shuffle, 2000f));

            _screen.ReturnFromProfile();
            _screen.ApplyBoard(Board(100));

            // A later fresh paint of a wave board (a publish re-reads it) is a new list.
            _screen.ApplyBoard(Board(100));
            Assert.AreEqual(0f, _grid.Offset);
        }

        [Test]
        public void ArrivingAnyOtherWayOpensAtTheTop()
        {
            // Nothing was left: the nav bar's BOARDS key, or a profile reached some other way.
            _screen.ShowBoard(LeaderboardBoard.Shuffle);
            _screen.ReturnFromProfile();
            _screen.ApplyBoard(Board(100));

            Assert.AreEqual(0f, _grid.Offset);
        }

        [Test]
        public void AnUnknownBoardIsIgnored()
        {
            Left.SetValue(null, ("global", 2000f));
            _screen.ShowBoard(LeaderboardBoard.Shuffle);

            _screen.ReturnFromProfile();
            _screen.ApplyBoard(Board(100));

            Assert.AreEqual(0f, _grid.Offset);
        }
    }
}
