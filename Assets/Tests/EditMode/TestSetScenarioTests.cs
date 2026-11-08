using NUnit.Framework;
using static Bagels.Core.Tests.TestUtil;

namespace Bagels.Core.Tests
{
    /// <summary>
    /// Plays the design.md test set using only A placements:
    ///   B = A–A;  C = B–B (K3 board);  D = triangle A–A–B;  E = B* –Down→ D, B* –Down→ D (B is a catalyst).
    /// Root is W7 (hub 0, rim 1–6). Formulas are listed most-specific first: D, E, C, B.
    /// </summary>
    public class TestSetScenarioTests
    {
        static readonly Formula FB = F("B", Symbol.B, new[] { N(Symbol.A), N(Symbol.A) }, Edge(0, 1));
        static readonly Formula FC = F("C", Symbol.C, new[] { N(Symbol.B), N(Symbol.B) }, Edge(0, 1));
        static readonly Formula FD = F("D", Symbol.D, new[] { N(Symbol.A), N(Symbol.A), N(Symbol.B) },
                                       Edge(0, 1), Edge(1, 2), Edge(0, 2));
        static readonly Formula FE = F("E", Symbol.E, new[] { Cat(Symbol.B), N(Symbol.D), N(Symbol.D) },
                                       Edge(0, 1, AdjacencyKind.Down), Edge(0, 2, AdjacencyKind.Down));

        GameState game;
        Board root;

        [SetUp]
        public void SetUp()
        {
            game = Game(new[] { FD, FE, FC, FB }, Symbol.E);
            root = game.RootBoard;
        }

        PlacementResult A(Board board, int slot) => game.PlaceA(board, slot);

        /// <summary>A@4, A@3 → B@3; A@1, A@2 → B@2 → cascade C@2.</summary>
        Board BuildCAt2()
        {
            A(root, 4);
            A(root, 3);
            A(root, 1);
            var result = A(root, 2);
            Assert.AreEqual(Symbol.C, result.Final.Symbol, Dump(root));
            return root[2].Child;
        }

        /// <summary>A@6, A@5 → B@5; A@3, A@4 → B@4 → cascade C@4. Needs slots 3–6 empty.</summary>
        Board BuildCAt4()
        {
            A(root, 6);
            A(root, 5);
            A(root, 3);
            var result = A(root, 4);
            Assert.AreEqual(Symbol.C, result.Final.Symbol, Dump(root));
            return root[4].Child;
        }

        /// <summary>Inside a K3: A@0, A@1 → B@1; A@0; A@2 → D@2.</summary>
        PlacementResult BuildD(Board c)
        {
            A(c, 0);
            A(c, 1);
            A(c, 0);
            return A(c, 2);
        }

        [Test]
        public void A_CanBePlacedAnywhereEmpty_IncludingNestedBoards()
        {
            Assert.AreEqual(Symbol.A, A(root, 0).Final.Symbol);
            Assert.Throws<System.InvalidOperationException>(() => A(root, 0));

            var fresh = Game(new[] { FD, FE, FC, FB });
            game = fresh;
            root = fresh.RootBoard;
            var c = BuildCAt2();
            var nested = A(c, 1);
            Assert.AreEqual(0, nested.Steps.Count);
            AssertSlot(c, 1, Symbol.A);
        }

        [Test]
        public void B_FromTwoAdjacentAs()
        {
            A(root, 1);
            var result = A(root, 2);
            Assert.AreEqual(1, result.Steps.Count);
            Assert.AreSame(FB, result.Steps[0].Formula);
            AssertSlot(root, 2, Symbol.B);
            AssertEmpty(root, 1);
        }

        [Test]
        public void C_FromTwoBs_CascadesIntoEmptyTriangleBoard()
        {
            A(root, 4);
            A(root, 3);
            AssertSlot(root, 3, Symbol.B);
            A(root, 1);
            var result = A(root, 2);

            Assert.AreEqual(2, result.Steps.Count);
            Assert.AreSame(FB, result.Steps[0].Formula);
            Assert.AreSame(FC, result.Steps[1].Formula);
            AssertSlot(root, 2, Symbol.C);
            AssertEmpty(root, 0, 1, 3, 4, 5, 6);
            Assert.AreSame(BoardLayout.K3, root[2].Child.Layout);
            AssertEmpty(root[2].Child, 0, 1, 2);
        }

        [Test]
        public void D_FromTriangleInsideC()
        {
            var c = BuildCAt2();
            var result = BuildD(c);

            Assert.AreEqual(1, result.Steps.Count);
            Assert.AreSame(FD, result.Steps[0].Formula);
            AssertSlot(c, 2, Symbol.D);
            AssertEmpty(c, 0, 1);
        }

        [Test]
        public void D_IsUnreachableIfBIsListedFirst()
        {
            game = Game(new[] { FB, FC, FD, FE });
            root = game.RootBoard;
            var c = BuildCAt2();
            var result = BuildD(c);

            Assert.AreSame(FB, result.Steps[0].Formula, "A+A->B steals the triangle's final A");
            Assert.IsFalse(game.Contains(Symbol.D));
        }

        [Test]
        public void E_WhenLastDFormsBelowB_CatalystIsKept()
        {
            var c1 = BuildCAt2();
            var c2 = BuildCAt4();
            A(root, 1);
            A(root, 0);
            AssertSlot(root, 0, Symbol.B);

            Assert.AreSame(FD, BuildD(c1).Steps[0].Formula);
            var result = BuildD(c2);

            Assert.AreEqual(2, result.Steps.Count);
            Assert.AreSame(FD, result.Steps[0].Formula);
            Assert.AreSame(FE, result.Steps[1].Formula);
            AssertSlot(c2, 2, Symbol.E);
            AssertEmpty(c1, 0, 1, 2);
            AssertSlot(root, 0, Symbol.B);
            Assert.IsTrue(result.TargetReached);
        }

        [Test]
        public void E_WhenBArrivesLast_CatalystIsConsumed()
        {
            var c1 = BuildCAt2();
            var c2 = BuildCAt4();
            BuildD(c1);
            BuildD(c2);
            AssertSlot(c1, 2, Symbol.D);
            AssertSlot(c2, 2, Symbol.D);

            A(root, 1);
            var result = A(root, 0);

            Assert.AreEqual(2, result.Steps.Count);
            Assert.AreSame(FB, result.Steps[0].Formula);
            Assert.AreSame(FE, result.Steps[1].Formula);
            AssertSlot(root, 0, Symbol.E);
            AssertEmpty(c1, 2);
            AssertEmpty(c2, 2);
            Assert.IsTrue(result.TargetReached);
        }
    }
}
