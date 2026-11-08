using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Bagels.Core.Tests
{
    static class TestUtil
    {
        public const Symbol Root = Symbol.R;

        public static PatternNode N(Symbol s) => new PatternNode(s);
        public static PatternNode Cat(Symbol s) => new PatternNode(s, isCatalyst: true);
        public static PatternEdge Edge(int a, int b, AdjacencyKind kind = AdjacencyKind.Normal) => new PatternEdge(a, b, kind);

        public static Formula F(string name, Symbol product, PatternNode[] nodes, params PatternEdge[] edges) =>
            new Formula(name, product, nodes, edges);

        /// <summary>Root R is W7, C is a K3 board, plus any extra bread layouts.</summary>
        public static GameState Game(IEnumerable<Formula> formulas, Symbol? target = null,
                                     params (Symbol, BoardLayout)[] extraBread)
        {
            var bread = new Dictionary<Symbol, BoardLayout> { [Root] = BoardLayout.W7, [Symbol.C] = BoardLayout.K3 };
            foreach (var (s, l) in extraBread) bread[s] = l;
            return new GameState(new Rules(Root, formulas, bread, target));
        }

        public static GameState Game(params Formula[] formulas) => Game((IEnumerable<Formula>)formulas);

        public static void AssertSlot(Board board, int slot, Symbol expected)
        {
            Assert.IsFalse(board.IsEmpty(slot), $"slot {slot} should hold {expected} but is empty");
            Assert.AreEqual(expected, board[slot].Symbol, $"slot {slot}");
        }

        public static void AssertEmpty(Board board, params int[] slots)
        {
            foreach (int s in slots)
                Assert.IsTrue(board.IsEmpty(s), $"slot {s} should be empty but holds {board[s]}");
        }

        public static string Dump(Board board) =>
            string.Join(" ", Enumerable.Range(0, board.SlotCount).Select(i => board.IsEmpty(i) ? "." : board[i].Symbol.ToString()));
    }
}
