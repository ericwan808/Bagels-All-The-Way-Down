using System;
using NUnit.Framework;
using static Bagels.Core.Tests.TestUtil;

namespace Bagels.Core.Tests
{
    /// <summary>Undo: replaying all but the last A placement. Uses B = A-A and C = B-B (C is a K3 board).</summary>
    public class PlacementHistoryTests
    {
        static readonly Formula FB = F("B", Symbol.B, new[] { N(Symbol.A), N(Symbol.A) }, Edge(0, 1));
        static readonly Formula FC = F("C", Symbol.C, new[] { N(Symbol.B), N(Symbol.B) }, Edge(0, 1));

        GameState game;
        PlacementHistory history;

        [SetUp]
        public void SetUp()
        {
            game = Game(FC, FB);
            history = new PlacementHistory();
        }

        PlacementResult Play(Board board, int slot)
        {
            history.Record(board, slot);
            return game.PlaceA(board, slot);
        }

        void Undo() => game = history.UndoLast(game.Rules);

        /// <summary>A@4, A@3 → B@3; A@1, A@2 → B@2 → C@2.</summary>
        void BuildCAt2()
        {
            Play(game.RootBoard, 4);
            Play(game.RootBoard, 3);
            Play(game.RootBoard, 1);
            Assert.AreEqual(Symbol.C, Play(game.RootBoard, 2).Final.Symbol);
        }

        [Test]
        public void Undo_RemovesAPlainPlacement()
        {
            Play(game.RootBoard, 1);
            Play(game.RootBoard, 3);
            Undo();

            AssertSlot(game.RootBoard, 1, Symbol.A);
            AssertEmpty(game.RootBoard, 3);
            Assert.AreEqual(1, history.Count);
        }

        [Test]
        public void Undo_RevertsAWholeCascade()
        {
            BuildCAt2();
            Undo();

            AssertSlot(game.RootBoard, 1, Symbol.A);
            AssertSlot(game.RootBoard, 3, Symbol.B);
            AssertEmpty(game.RootBoard, 0, 2, 4, 5, 6);
        }

        [Test]
        public void Undo_ReplaysPlacementsOnNestedBoards()
        {
            BuildCAt2();
            Play(game.RootBoard[2].Child, 0);
            Play(game.RootBoard[2].Child, 2);
            AssertSlot(game.RootBoard[2].Child, 2, Symbol.B);

            Undo();
            var inner = game.RootBoard[2].Child;
            AssertSlot(inner, 0, Symbol.A);
            AssertEmpty(inner, 1, 2);

            Undo();
            AssertSlot(game.RootBoard, 2, Symbol.C);
            AssertEmpty(game.RootBoard[2].Child, 0, 1, 2);
        }

        [Test]
        public void Undo_EverythingGivesAnEmptyBoard()
        {
            BuildCAt2();
            while (history.Count > 0) Undo();
            AssertEmpty(game.RootBoard, 0, 1, 2, 3, 4, 5, 6);
        }

        [Test]
        public void Undo_WithNoHistoryThrows()
        {
            Assert.Throws<InvalidOperationException>(() => history.UndoLast(game.Rules));
        }

        [Test]
        public void PathOf_AndFind_RoundTrip()
        {
            BuildCAt2();
            Play(game.RootBoard[2].Child, 1);
            var a = game.RootBoard[2].Child[1];

            CollectionAssert.AreEqual(new[] { 2, 1 }, PlacementHistory.PathOf(a));
            Assert.AreSame(a, PlacementHistory.Find(game, new[] { 2, 1 }));
            Assert.AreSame(game.Root, PlacementHistory.Find(game, Array.Empty<int>()));
            Assert.IsNull(PlacementHistory.Find(game, new[] { 5 }));
        }
    }
}
