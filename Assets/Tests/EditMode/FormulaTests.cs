using System;
using NUnit.Framework;
using static Bagels.Core.Tests.TestUtil;

namespace Bagels.Core.Tests
{
    public class FormulaValidationTests
    {
        [Test]
        public void RejectsSingleNode() =>
            Assert.Throws<ArgumentException>(() => F("one", Symbol.P, new[] { N(Symbol.X) }));

        [Test]
        public void RejectsDisconnectedPattern() =>
            Assert.Throws<ArgumentException>(() =>
                F("split", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y), N(Symbol.Z) }, Edge(0, 1)));

        [Test]
        public void RejectsBadEdgeIndex() =>
            Assert.Throws<ArgumentException>(() => F("bad", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y) }, Edge(0, 2)));

        [Test]
        public void RulesRejectNonBreadRoot() =>
            Assert.Throws<ArgumentException>(() =>
                new Rules(Symbol.A, Array.Empty<Formula>(), new System.Collections.Generic.Dictionary<Symbol, BoardLayout>()));
    }

    public class MatchingTests
    {
        static readonly Formula XYtoP = F("XY->P", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y) }, Edge(0, 1));
        static readonly Formula XYtoQ = F("XY->Q", Symbol.Q, new[] { N(Symbol.X), N(Symbol.Y) }, Edge(0, 1));

        [Test]
        public void PairConsumesBoth_ProductLandsInPlacedSlot()
        {
            var game = Game(XYtoP);
            var root = game.RootBoard;
            game.Place(root, 1, Symbol.X);
            var result = game.Place(root, 2, Symbol.Y);

            Assert.AreEqual(1, result.Steps.Count);
            AssertSlot(root, 2, Symbol.P);
            AssertEmpty(root, 1);
            Assert.AreEqual(2, result.Steps[0].Consumed.Count);
            Assert.IsFalse(result.Placed.IsAlive);
        }

        [Test]
        public void NonAdjacentIngredients_DoNotMatch()
        {
            var game = Game(XYtoP);
            var root = game.RootBoard;
            game.Place(root, 1, Symbol.X);
            var result = game.Place(root, 3, Symbol.Y);
            Assert.AreEqual(0, result.Steps.Count);
        }

        [Test]
        public void Chain_MatchesWhenComplete()
        {
            var chain = F("chain", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y), N(Symbol.Z) }, Edge(0, 1), Edge(1, 2));
            var game = Game(chain);
            var root = game.RootBoard;
            Assert.AreEqual(0, game.Place(root, 1, Symbol.X).Steps.Count);
            Assert.AreEqual(0, game.Place(root, 2, Symbol.Y).Steps.Count);
            Assert.AreEqual(1, game.Place(root, 3, Symbol.Z).Steps.Count);
            AssertSlot(root, 3, Symbol.P);
            AssertEmpty(root, 1, 2);
        }

        [Test]
        public void Triangle_DoesNotMatchAPath_ButMatchesATriangle()
        {
            var tri = F("tri", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y), N(Symbol.Z) }, Edge(0, 1), Edge(1, 2), Edge(0, 2));

            var path = Game(tri);
            path.Place(path.RootBoard, 1, Symbol.X);
            path.Place(path.RootBoard, 2, Symbol.Y);
            Assert.AreEqual(0, path.Place(path.RootBoard, 3, Symbol.Z).Steps.Count, "1-2-3 is a path, not a triangle");

            var triangle = Game(tri);
            triangle.Place(triangle.RootBoard, 1, Symbol.X);
            triangle.Place(triangle.RootBoard, 2, Symbol.Y);
            Assert.AreEqual(1, triangle.Place(triangle.RootBoard, 0, Symbol.Z).Steps.Count, "hub,1,2 is a triangle");
            AssertSlot(triangle.RootBoard, 0, Symbol.P);
        }

        [Test]
        public void FirstFormulaInListWins()
        {
            var p = Game(XYtoP, XYtoQ);
            p.Place(p.RootBoard, 1, Symbol.X);
            p.Place(p.RootBoard, 2, Symbol.Y);
            AssertSlot(p.RootBoard, 2, Symbol.P);

            var q = Game(XYtoQ, XYtoP);
            q.Place(q.RootBoard, 1, Symbol.X);
            q.Place(q.RootBoard, 2, Symbol.Y);
            AssertSlot(q.RootBoard, 2, Symbol.Q);
        }

        [Test]
        public void DownEdge_RequiresVerticalAdjacency_AndProductCanLandOnAnotherBoard()
        {
            var down = F("X down Y", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y) }, Edge(0, 1, AdjacencyKind.Down));

            var flat = Game(down);
            flat.Place(flat.RootBoard, 0, Symbol.X);
            Assert.AreEqual(0, flat.Place(flat.RootBoard, 1, Symbol.Y).Steps.Count, "normal adjacency is not Down");

            var game = Game(down);
            var root = game.RootBoard;
            game.Place(root, 1, Symbol.C);
            game.Place(root, 0, Symbol.X);
            var inner = root[1].Child;
            var result = game.Place(inner, 0, Symbol.Y);

            Assert.AreEqual(1, result.Steps.Count);
            AssertSlot(inner, 0, Symbol.P);
            AssertEmpty(root, 0);
            AssertSlot(root, 1, Symbol.C);
        }

        [Test]
        public void UpEdge_IsDirectional()
        {
            // Y sits below X in both games.
            var yUpX = F("Y up X", Symbol.P, new[] { N(Symbol.Y), N(Symbol.X) }, Edge(0, 1, AdjacencyKind.Up));
            var xUpY = F("X up Y", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y) }, Edge(0, 1, AdjacencyKind.Up));

            foreach (var (formula, shouldMatch) in new[] { (yUpX, true), (xUpY, false) })
            {
                var game = Game(formula);
                var root = game.RootBoard;
                game.Place(root, 1, Symbol.C);
                game.Place(root, 0, Symbol.X);
                var result = game.Place(root[1].Child, 0, Symbol.Y);
                Assert.AreEqual(shouldMatch ? 1 : 0, result.Steps.Count, formula.Name);
            }
        }

        [Test]
        public void Catalyst_KeptWhenNotTrigger_ConsumedWhenTrigger()
        {
            var cat = F("K*X->P", Symbol.P, new[] { Cat(Symbol.K), N(Symbol.X) }, Edge(0, 1));

            var kept = Game(cat);
            kept.Place(kept.RootBoard, 0, Symbol.K);
            kept.Place(kept.RootBoard, 1, Symbol.X);
            AssertSlot(kept.RootBoard, 0, Symbol.K);
            AssertSlot(kept.RootBoard, 1, Symbol.P);

            var consumed = Game(cat);
            consumed.Place(consumed.RootBoard, 1, Symbol.X);
            consumed.Place(consumed.RootBoard, 0, Symbol.K);
            AssertSlot(consumed.RootBoard, 0, Symbol.P);
            AssertEmpty(consumed.RootBoard, 1);
        }

        [Test]
        public void Cascade_LandsInOriginalSlot()
        {
            var xy = XYtoP;
            var pz = F("PZ->Q", Symbol.Q, new[] { N(Symbol.P), N(Symbol.Z) }, Edge(0, 1));
            var game = Game(xy, pz);
            var root = game.RootBoard;
            game.Place(root, 3, Symbol.Z);
            game.Place(root, 1, Symbol.X);
            var result = game.Place(root, 2, Symbol.Y);

            Assert.AreEqual(2, result.Steps.Count);
            AssertSlot(root, 2, Symbol.Q);
            AssertEmpty(root, 1, 3);
            Assert.AreSame(root[2], result.Final);
        }

        [Test]
        public void BreadProduct_IsAnEmptyBoard()
        {
            var makeC = F("XY->C", Symbol.C, new[] { N(Symbol.X), N(Symbol.Y) }, Edge(0, 1));
            var game = Game(makeC);
            game.Place(game.RootBoard, 1, Symbol.X);
            var c = game.Place(game.RootBoard, 2, Symbol.Y).Final;

            Assert.IsTrue(c.IsBread);
            Assert.AreSame(BoardLayout.K3, c.Child.Layout);
            AssertEmpty(c.Child, 0, 1, 2);
        }

        [Test]
        public void ConsumedBoard_DestroysItsChildren()
        {
            var eatBoard = F("X+C->P", Symbol.P, new[] { N(Symbol.X), N(Symbol.C) }, Edge(0, 1));
            var game = Game(eatBoard);
            var root = game.RootBoard;
            game.Place(root, 1, Symbol.C);
            var board = root[1];
            var child = game.Place(board.Child, 0, Symbol.Y).Placed;

            game.Place(root, 0, Symbol.X);

            AssertSlot(root, 0, Symbol.P);
            AssertEmpty(root, 1);
            Assert.IsFalse(board.IsAlive);
            Assert.IsFalse(child.IsAlive);
            Assert.Throws<InvalidOperationException>(() => game.PlaceA(board.Child, 1));
        }

        [Test]
        public void TriggerBoardConsumed_ProductLandsInThatBoardsSlot()
        {
            // Y (inside C) is Up-adjacent to X, and X is normally adjacent to C itself.
            var f = F("Y^X+C->P", Symbol.P, new[] { N(Symbol.Y), N(Symbol.X), N(Symbol.C) },
                      Edge(0, 1, AdjacencyKind.Up), Edge(1, 2));
            var game = Game(f);
            var root = game.RootBoard;
            game.Place(root, 1, Symbol.C);
            game.Place(root, 0, Symbol.X);
            var result = game.Place(root[1].Child, 0, Symbol.Y);

            Assert.AreEqual(1, result.Steps.Count);
            AssertSlot(root, 1, Symbol.P);
            AssertEmpty(root, 0);
        }

        [Test]
        public void CatalystCycle_HitsCascadeLimit()
        {
            var kxy = F("K*X->Y", Symbol.Y, new[] { Cat(Symbol.K), N(Symbol.X) }, Edge(0, 1));
            var kyx = F("K*Y->X", Symbol.X, new[] { Cat(Symbol.K), N(Symbol.Y) }, Edge(0, 1));
            var game = Game(kxy, kyx);
            game.Place(game.RootBoard, 0, Symbol.K);
            var ex = Assert.Throws<CascadeLimitException>(() => game.Place(game.RootBoard, 1, Symbol.X));
            Assert.AreEqual(GameState.MaxCascadeSteps, ex.Steps.Count);
        }

        [Test]
        public void OccupiedSlot_Throws()
        {
            var game = Game();
            game.PlaceA(game.RootBoard, 0);
            Assert.Throws<InvalidOperationException>(() => game.PlaceA(game.RootBoard, 0));
        }

        [Test]
        public void TargetDetected()
        {
            var game = Game(new[] { F("XY->P", Symbol.P, new[] { N(Symbol.X), N(Symbol.Y) }, Edge(0, 1)) }, Symbol.P);
            Assert.IsFalse(game.Place(game.RootBoard, 1, Symbol.X).TargetReached);
            Assert.IsTrue(game.Place(game.RootBoard, 2, Symbol.Y).TargetReached);
        }
    }
}
