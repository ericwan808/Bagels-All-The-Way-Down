using System.Linq;
using NUnit.Framework;
using static Bagels.Core.Tests.TestUtil;

namespace Bagels.Core.Tests
{
    public class LayoutTests
    {
        [Test]
        public void W7_HubTouchesAllRimSlots_RimFormsCycle()
        {
            var w7 = BoardLayout.W7;
            Assert.AreEqual(7, w7.SlotCount);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, w7.Neighbors(0));
            for (int i = 1; i <= 6; i++)
            {
                Assert.AreEqual(3, w7.Neighbors(i).Count, $"rim slot {i}");
                Assert.IsTrue(w7.AreAdjacent(i, i % 6 + 1));
            }
            Assert.IsTrue(w7.AreAdjacent(6, 1));
            Assert.IsFalse(w7.AreAdjacent(1, 3));
            Assert.IsFalse(w7.AreAdjacent(1, 4));
        }

        [Test]
        public void P3_IsAPath()
        {
            var p3 = BoardLayout.P3;
            Assert.IsTrue(p3.AreAdjacent(0, 1));
            Assert.IsTrue(p3.AreAdjacent(1, 2));
            Assert.IsFalse(p3.AreAdjacent(0, 2));
        }

        [Test]
        public void K23_IsCompleteBipartite()
        {
            var k23 = BoardLayout.K23;
            Assert.AreEqual(5, k23.SlotCount);
            foreach (int a in new[] { 0, 1 })
                foreach (int b in new[] { 2, 3, 4 })
                    Assert.IsTrue(k23.AreAdjacent(a, b), $"{a}-{b}");
            Assert.IsFalse(k23.AreAdjacent(0, 1));
            Assert.IsFalse(k23.AreAdjacent(2, 3));
            Assert.IsFalse(k23.AreAdjacent(3, 4));
            Assert.IsFalse(k23.AreAdjacent(2, 4));
        }

        [Test]
        public void K3_IsATriangle()
        {
            var k3 = BoardLayout.K3;
            Assert.IsTrue(k3.AreAdjacent(0, 1));
            Assert.IsTrue(k3.AreAdjacent(1, 2));
            Assert.IsTrue(k3.AreAdjacent(0, 2));
        }
    }

    public class AdjacencyTests
    {
        // No formulas: we only inspect the tree.
        GameState game;
        Board root;

        [SetUp]
        public void SetUp()
        {
            game = Game();
            root = game.RootBoard;
        }

        [Test]
        public void DownAndUp_AreInverseAndDirectional()
        {
            game.Place(root, 1, Symbol.C);
            var x = game.Place(root, 0, Symbol.X).Placed;
            var inner = root[1].Child;
            var y = game.Place(inner, 0, Symbol.Y).Placed;

            CollectionAssert.AreEqual(new[] { root[1] }, Adjacency.Neighbors(x, AdjacencyKind.Normal).ToArray());
            CollectionAssert.AreEqual(new[] { y }, Adjacency.Neighbors(x, AdjacencyKind.Down).ToArray());
            CollectionAssert.AreEqual(new[] { x }, Adjacency.Neighbors(y, AdjacencyKind.Up).ToArray());

            Assert.IsTrue(Adjacency.AreAdjacent(x, y, AdjacencyKind.Down));
            Assert.IsFalse(Adjacency.AreAdjacent(x, y, AdjacencyKind.Up));
            Assert.IsFalse(Adjacency.AreAdjacent(x, y, AdjacencyKind.Normal));
            Assert.IsTrue(Adjacency.AreAdjacent(y, x, AdjacencyKind.Up));

            CollectionAssert.IsEmpty(Adjacency.Neighbors(y, AdjacencyKind.Down).ToArray());
            CollectionAssert.IsEmpty(Adjacency.Neighbors(x, AdjacencyKind.Up).ToArray(), "root-board elements have nothing above");
        }

        [Test]
        public void Vertical_OnlyReachesOneLevel()
        {
            game.Place(root, 1, Symbol.C);
            var x = game.Place(root, 0, Symbol.X).Placed;
            var mid = root[1].Child;
            var y = game.Place(mid, 0, Symbol.Y).Placed;
            game.Place(mid, 1, Symbol.C);
            var z = game.Place(mid[1].Child, 0, Symbol.Z).Placed;

            CollectionAssert.DoesNotContain(Adjacency.Neighbors(x, AdjacencyKind.Down).ToArray(), z);
            CollectionAssert.AreEqual(new[] { y }, Adjacency.Neighbors(z, AdjacencyKind.Up).ToArray());
        }

        [Test]
        public void ChildrenOfAdjacentBoards_AreNotAdjacent()
        {
            game.Place(root, 1, Symbol.C);
            game.Place(root, 2, Symbol.C);
            var m = game.Place(root[1].Child, 0, Symbol.M).Placed;
            var n = game.Place(root[2].Child, 0, Symbol.N).Placed;

            var up = Adjacency.Neighbors(m, AdjacencyKind.Up).ToArray();
            CollectionAssert.Contains(up, root[2], "a child is Up-adjacent to its board's neighbour");
            CollectionAssert.DoesNotContain(up, n);
            CollectionAssert.IsEmpty(Adjacency.Neighbors(m, AdjacencyKind.Down).ToArray());
            CollectionAssert.DoesNotContain(Adjacency.Neighbors(m, AdjacencyKind.Normal).ToArray(), n);
        }
    }
}
