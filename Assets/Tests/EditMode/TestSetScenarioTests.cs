using NUnit.Framework;
using static Bagels.Core.Tests.TestUtil;

namespace Bagels.Core.Tests
{
    /// <summary>
    /// The design.md test set (see the formula table there for the notation):
    ///   B = A-A;  C = B-B (K3 triangle board);  D = A1-B-A-1;  E = D&lt;A&gt;D (K2,3 board);  F = B-[C]-B;
    ///   G = F-B-F;  H = B&gt;G&lt;B (W7 board);  I = F1&gt;G&lt;F-1;  J = D&gt;G&gt;[I]&lt;G&lt;D.
    /// Root is W7 (hub 0, rim 1–6 in a cycle). Formulas are checked in reverse product order: J, I, ..., B.
    /// A-only tests play from scratch; the rest place ingredients directly to check each pattern in isolation.
    /// </summary>
    public class TestSetScenarioTests
    {
        const AdjacencyKind Up = AdjacencyKind.Up, Dn = AdjacencyKind.Down;

        static readonly Formula FB = F("B", Symbol.B, new[] { N(Symbol.A), N(Symbol.A) }, Edge(0, 1));
        static readonly Formula FC = F("C", Symbol.C, new[] { N(Symbol.B), N(Symbol.B) }, Edge(0, 1));
        static readonly Formula FD = F("D", Symbol.D, new[] { N(Symbol.A), N(Symbol.B), N(Symbol.A) },
                                       Edge(0, 1), Edge(1, 2), Edge(0, 2));
        static readonly Formula FE = F("E", Symbol.E, new[] { N(Symbol.D), N(Symbol.A), N(Symbol.D) },
                                       Edge(0, 1, Up), Edge(1, 2, Dn));
        static readonly Formula FF = F("F", Symbol.F, new[] { N(Symbol.B), Cat(Symbol.C), N(Symbol.B) },
                                       Edge(0, 1), Edge(1, 2));
        static readonly Formula FG = F("G", Symbol.G, new[] { N(Symbol.F), N(Symbol.B), N(Symbol.F) },
                                       Edge(0, 1), Edge(1, 2));
        static readonly Formula FH = F("H", Symbol.H, new[] { N(Symbol.B), N(Symbol.G), N(Symbol.B) },
                                       Edge(0, 1, Dn), Edge(1, 2, Up));
        static readonly Formula FI = F("I", Symbol.I, new[] { N(Symbol.F), N(Symbol.G), N(Symbol.F) },
                                       Edge(0, 1, Dn), Edge(1, 2, Up), Edge(0, 2));
        static readonly Formula FJ = F("J", Symbol.J,
                                       new[] { N(Symbol.D), N(Symbol.G), Cat(Symbol.I), N(Symbol.G), N(Symbol.D) },
                                       Edge(0, 1, Dn), Edge(1, 2, Dn), Edge(2, 3, Up), Edge(3, 4, Up));

        static readonly Formula[] LevelOrder = { FJ, FI, FH, FG, FF, FE, FD, FC, FB };

        GameState game;
        Board root;

        static GameState TestSet(Formula[] formulas) =>
            Game(formulas, Symbol.J, (Symbol.C, BoardLayout.K3), (Symbol.E, BoardLayout.K23), (Symbol.H, BoardLayout.W7));

        [SetUp]
        public void SetUp()
        {
            game = TestSet(LevelOrder);
            root = game.RootBoard;
        }

        PlacementResult A(Board board, int slot) => game.PlaceA(board, slot);
        PlacementResult Put(Board board, int slot, Symbol symbol) => game.Place(board, slot, symbol);

        static void AssertMade(PlacementResult result, params Formula[] formulas)
        {
            Assert.AreEqual(formulas.Length, result.Steps.Count, "cascade length");
            for (int i = 0; i < formulas.Length; i++) Assert.AreSame(formulas[i], result.Steps[i].Formula, $"step {i}");
        }

        // ------------------------------------------------------------ from As only

        [Test]
        public void B_FromTwoAdjacentAs()
        {
            A(root, 1);
            var result = A(root, 2);
            AssertMade(result, FB);
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

            AssertMade(result, FB, FC);
            AssertSlot(root, 2, Symbol.C);
            AssertEmpty(root, 0, 1, 3, 4, 5, 6);
            Assert.AreSame(BoardLayout.K3, root[2].Child.Layout);
            AssertEmpty(root[2].Child, 0, 1, 2);
        }

        /// <summary>A@1, A@2 → B@2; A@1; A@0 closes the hub/1/2 triangle.</summary>
        [Test]
        public void D_FromTriangleOnTheRoot()
        {
            A(root, 1);
            A(root, 2);
            A(root, 1);
            var result = A(root, 0);

            AssertMade(result, FD);
            AssertSlot(root, 0, Symbol.D);
            AssertEmpty(root, 1, 2);
        }

        [Test]
        public void D_IsUnreachableIfBIsListedFirst()
        {
            game = TestSet(new[] { FB, FC, FD, FE, FF, FG, FH, FI, FJ });
            root = game.RootBoard;
            A(root, 1);
            A(root, 2);
            A(root, 1);
            var result = A(root, 0);

            // The stolen A makes B at the hub, which then joins the B at 2 into C.
            AssertMade(result, FB, FC);
            Assert.IsFalse(game.Contains(Symbol.D), "A+A->B steals the triangle's final A");
        }

        /// <summary>C at the hub: B@2, then A@4 + A@0 → B@0 → C@0.</summary>
        void BuildCAtHub()
        {
            A(root, 1);
            A(root, 2);
            A(root, 4);
            AssertMade(A(root, 0), FB, FC);
            AssertSlot(root, 0, Symbol.C);
        }

        /// <summary>
        /// C at the hub; a B at 1 waits, and a B at 3 completes B-[C]-B. The C stays, so a new B at 1 and one at 5
        /// make a second F.
        /// </summary>
        [Test]
        public void F_FromTwoBsAroundC_KeepsTheCatalyst()
        {
            BuildCAtHub();
            A(root, 2);
            AssertMade(A(root, 1), FB);
            A(root, 4);
            AssertMade(A(root, 3), FB, FF);
            AssertSlot(root, 3, Symbol.F);
            AssertSlot(root, 0, Symbol.C);
            AssertEmpty(root, 1, 2, 4);

            A(root, 2);
            AssertMade(A(root, 1), FB);
            A(root, 6);
            AssertMade(A(root, 5), FB, FF);
            AssertSlot(root, 5, Symbol.F);
            AssertSlot(root, 0, Symbol.C);
            AssertEmpty(root, 1, 2, 4, 6);
        }

        [Test]
        public void F_NeedsTwoBs()
        {
            Put(root, 0, Symbol.C);
            AssertMade(Put(root, 1, Symbol.B));
            AssertSlot(root, 1, Symbol.B);
        }

        [Test]
        public void F_CatalystConsumed_WhenCArrivesLast()
        {
            Put(root, 1, Symbol.B);
            Put(root, 3, Symbol.B);
            var result = Put(root, 0, Symbol.C);

            AssertMade(result, FF);
            AssertSlot(root, 0, Symbol.F);
            AssertEmpty(root, 1, 3);
        }

        /// <summary>Ds inside the C boards at 1 and 3; an A at 2 sits next to both boards.</summary>
        [Test]
        public void E_AAboveTwoDs_MakesAK23Board()
        {
            Put(root, 1, Symbol.C);
            Put(root, 3, Symbol.C);
            Put(root[1].Child, 0, Symbol.D);
            Put(root[3].Child, 0, Symbol.D);
            var result = A(root, 2);

            AssertMade(result, FE);
            AssertSlot(root, 2, Symbol.E);
            Assert.AreSame(BoardLayout.K23, root[2].Child.Layout);
            AssertEmpty(root[1].Child, 0);
            AssertEmpty(root[3].Child, 0);
            AssertSlot(root, 1, Symbol.C);
            AssertSlot(root, 3, Symbol.C);
        }

        [Test]
        public void E_LandsInsideTheBoardWhenALowerDIsLast()
        {
            Put(root, 1, Symbol.C);
            Put(root, 3, Symbol.C);
            A(root, 2);
            Put(root[1].Child, 0, Symbol.D);
            var result = Put(root[3].Child, 1, Symbol.D);

            AssertMade(result, FE);
            AssertSlot(root[3].Child, 1, Symbol.E);
            AssertEmpty(root, 2);
        }

        /// <summary>The A that joins the Ds sits between two Fs; G needs a B there, so it doesn't steal the A.</summary>
        [Test]
        public void E_IsNotTakenByG_WhenFsTouchTheA()
        {
            Put(root, 2, Symbol.C);
            Put(root, 5, Symbol.C);
            Put(root, 3, Symbol.F);
            Put(root, 4, Symbol.F);
            Put(root[2].Child, 0, Symbol.D);
            Put(root[5].Child, 0, Symbol.D);
            var result = A(root, 0);

            AssertMade(result, FE);
            AssertSlot(root, 0, Symbol.E);
            AssertSlot(root, 3, Symbol.F);
            AssertSlot(root, 4, Symbol.F);
        }

        [Test]
        public void G_FromBBetweenTwoFs()
        {
            Put(root, 1, Symbol.F);
            Put(root, 3, Symbol.F);
            var result = Put(root, 2, Symbol.B);

            AssertMade(result, FG);
            AssertSlot(root, 2, Symbol.G);
            AssertEmpty(root, 1, 3);
        }

        /// <summary>
        /// Bs at 1 and 3 (not adjacent, so no B-B) both touch the board at the hub, which holds G. The board is an H,
        /// not a C, because the second B would complete B-[C]-B and make F.
        /// </summary>
        [Test]
        public void H_FromGBelowTwoBs_MakesAW7Board()
        {
            Put(root, 0, Symbol.H);
            Put(root, 1, Symbol.B);
            Put(root, 3, Symbol.B);
            var result = Put(root[0].Child, 1, Symbol.G);

            AssertMade(result, FH);
            AssertSlot(root[0].Child, 1, Symbol.H);
            Assert.AreSame(BoardLayout.W7, root[0].Child[1].Child.Layout);
            AssertEmpty(root, 1, 3);
        }

        [Test]
        public void I_FromGBelowTwoAdjacentFs()
        {
            Put(root, 0, Symbol.C);
            Put(root, 1, Symbol.F);
            Put(root, 2, Symbol.F);
            var result = Put(root[0].Child, 1, Symbol.G);

            AssertMade(result, FI);
            AssertSlot(root[0].Child, 1, Symbol.I);
            AssertEmpty(root, 1, 2);
        }

        [Test]
        public void I_NeedsTheFsToBeAdjacent()
        {
            Put(root, 0, Symbol.C);
            Put(root, 1, Symbol.F);
            Put(root, 3, Symbol.F);
            var result = Put(root[0].Child, 1, Symbol.G);

            AssertMade(result);
            AssertSlot(root[0].Child, 1, Symbol.G);
        }

        /// <summary>
        /// Root: Ds at 1 and 4, both next to the W7 board X at the hub. X: Gs at 1 and 4, both next to the C board Y
        /// at X's hub. Y: the catalyst I. Each G is below a D, and I is below both Gs.
        /// </summary>
        Board BuildJ(bool catalystLast)
        {
            Put(root, 0, Symbol.H);
            var x = root[0].Child;
            Put(x, 0, Symbol.C);
            var y = x[0].Child;
            Put(root, 1, Symbol.D);
            Put(x, 1, Symbol.G);
            Put(x, 4, Symbol.G);
            if (!catalystLast) Put(y, 0, Symbol.I);
            return y;
        }

        [Test]
        public void J_CatalystKept_WhenDArrivesLast()
        {
            var y = BuildJ(catalystLast: false);
            var result = Put(root, 4, Symbol.D);

            AssertMade(result, FJ);
            AssertSlot(root, 4, Symbol.J);
            AssertEmpty(root, 1);
            AssertEmpty(root[0].Child, 1, 4);
            AssertSlot(y, 0, Symbol.I);
            Assert.IsTrue(result.TargetReached);
        }

        [Test]
        public void J_CatalystConsumed_WhenIArrivesLast()
        {
            var y = BuildJ(catalystLast: true);
            Put(root, 4, Symbol.D);
            var result = Put(y, 0, Symbol.I);

            AssertMade(result, FJ);
            AssertSlot(y, 0, Symbol.J);
            AssertEmpty(root, 1, 4);
            AssertEmpty(root[0].Child, 1, 4);
            Assert.IsTrue(result.TargetReached);
        }

        [Test]
        public void J_NeedsBothGsAboveTheI()
        {
            var y = BuildJ(catalystLast: true);
            Put(root, 4, Symbol.D);
            game.Remove(root[0].Child[4]); // drop one G
            var result = Put(y, 0, Symbol.I);

            AssertMade(result);
            AssertSlot(y, 0, Symbol.I);
        }
    }
}
