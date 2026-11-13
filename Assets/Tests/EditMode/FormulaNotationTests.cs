using System;
using System.Linq;
using NUnit.Framework;

namespace Bagels.Core.Tests
{
    public class FormulaNotationTests
    {
        // The design.md formula table: condensed form and explicit form of each row.
        static readonly (string condensed, string expl)[] DesignTable =
        {
            ("A-A", "A A | 0-1"),
            ("B-B", "B B | 0-1"),
            ("A1-B-A-1", "A B A | 0-1 1-2 0-2"),
            ("D<A>D", "D A D | 0<1 1>2"),
            ("B-[C]-B", "B [C] B | 0-1 1-2"),
            ("F-B-F", "F B F | 0-1 1-2"),
            ("B>G<B", "B G B | 0>1 1<2"),
            ("F1>G<F-1", "F G F | 0>1 1<2 0-2"),
            ("D>G>[I]<G<D", "D G [I] G D | 0>1 1>2 2<3 3<4"),
            ("J1-J>F-F<J-J>F-F<1", "J J F F J J F F | 0-1 1>2 2-3 3<4 4-5 5>6 6-7 7<0"),
        };

        // L's explicit form numbers its nodes differently from the condensed spelling, so it is compared as a graph.
        const string LCondensed = "K>J>I(-G>F1(>D>B(-A)(-A))(>D>B(-A)(-A)))(-G>1)";
        const string LExplicit = "K J I G G F D D A B A A B A | 0>1 1>2 2-3 2-4 3>5 4>5 5>6 5>7 6>9 7>12 9-8 9-10 12-11 12-13";

        static string Explicit(string text) => FormulaNotation.Parse(text).ToString();

        [Test]
        public void DesignTable_CondensedAndExplicitFormsAgree()
        {
            foreach (var (condensed, expl) in DesignTable)
            {
                Assert.AreEqual(expl, Explicit(condensed), condensed);
                Assert.AreEqual(expl, Explicit(expl), expl); // explicit form round-trips
            }
        }

        [Test]
        public void DesignTable_L_CondensedAndExplicitAreTheSameGraph()
        {
            var condensed = FormulaNotation.Parse(LCondensed);
            var expl = FormulaNotation.Parse(LExplicit);
            Assert.AreEqual(14, condensed.Nodes.Count);
            Assert.AreEqual(14, condensed.Edges.Count);
            Assert.IsTrue(FormulaNotation.Equivalent(condensed, expl));
            Assert.DoesNotThrow(() => FormulaNotation.ToFormula("L", Symbol.L, LCondensed));
        }

        [Test]
        public void Equivalent_ChecksDirectionAndCatalysts()
        {
            Assert.IsTrue(FormulaNotation.Equivalent(FormulaNotation.Parse("A-A-B"), FormulaNotation.Parse("B-A-A")));
            Assert.IsTrue(FormulaNotation.Equivalent(FormulaNotation.Parse("D<A>D"), FormulaNotation.Parse("A D D | 0>1 0>2")));
            Assert.IsFalse(FormulaNotation.Equivalent(FormulaNotation.Parse("D<A"), FormulaNotation.Parse("D>A")));
            Assert.IsFalse(FormulaNotation.Equivalent(FormulaNotation.Parse("B-[C]-B"), FormulaNotation.Parse("B-C-B")));
            Assert.IsFalse(FormulaNotation.Equivalent(FormulaNotation.Parse("A-A-B"), FormulaNotation.Parse("A-B-A")));
        }

        [Test]
        public void Bonds_MapToAdjacencyKinds()
        {
            // "x<y": x is below y, so y is Up from x. "x>y": y is below x, so y is Down from x.
            var p = FormulaNotation.Parse("D<A>D");
            Assert.AreEqual(AdjacencyKind.Up, p.Edges[0].Kind);
            Assert.AreEqual((0, 1), (p.Edges[0].A, p.Edges[0].B));
            Assert.AreEqual(AdjacencyKind.Down, p.Edges[1].Kind);
            Assert.AreEqual((1, 2), (p.Edges[1].A, p.Edges[1].B));
        }

        [Test]
        public void Catalyst_IsMarked()
        {
            var p = FormulaNotation.Parse("B-[C]-B");
            CollectionAssert.AreEqual(new[] { false, true, false }, p.Nodes.Select(n => n.IsCatalyst));
            Assert.AreEqual(Symbol.C, p.Nodes[1].Symbol);
        }

        [Test]
        public void Branches_AttachToTheAtomBeforeThem()
        {
            Assert.AreEqual("A B C D | 0-1 0-2 0-3", Explicit("A(-B)(-C)-D"));
            Assert.AreEqual("A B C D | 0-1 1>2 0-3", Explicit("A(-B>C)-D"));
        }

        [Test]
        public void RingClosure_ReadsAsIfTaggedAtomWereWrittenThere()
        {
            Assert.AreEqual("A B | 0-1 1<0", Explicit("A1-B<1"));       // B is below the tagged A
            Assert.AreEqual("A B A | 0-1 1-2 0-2", Explicit("A1-B-A1"));  // closing without a bond is normal
        }

        [Test]
        public void RingDigit_CanBeReusedAfterClosing()
        {
            Assert.AreEqual("A B C D E F | 0-1 1-2 0-2 2-3 3-4 4-5 3-5", Explicit("A1-B-C1-D1-E-F-1"));
        }

        [Test]
        public void Whitespace_IsIgnored()
        {
            Assert.AreEqual("A B A | 0-1 1-2 0-2", Explicit(" A1 - B - A - 1 "));
        }

        [TestCase("")]
        [TestCase("A-")]
        [TestCase("A--B")]
        [TestCase("a-b")]
        [TestCase("A1-B")]          // ring never closed
        [TestCase("[A-B")]
        [TestCase("A(B)")]          // branch without a bond
        [TestCase("A-1B")]          // ring opened with a bond
        [TestCase("A B | 0-2")]     // missing node
        [TestCase("A B | 01")]
        [TestCase("A B | 0-1 | 1-0")]
        public void Malformed_Throws(string text)
        {
            Assert.Throws<FormatException>(() => FormulaNotation.Parse(text));
        }

        [Test]
        public void ToFormula_ValidatesPattern()
        {
            var f = FormulaNotation.ToFormula("D", Symbol.D, "A1-B-A-1");
            Assert.AreEqual(Symbol.D, f.Product);
            Assert.AreEqual(3, f.Edges.Count);
            Assert.Throws<ArgumentException>(() => FormulaNotation.ToFormula("bad", Symbol.B, "A B | ")); // disconnected
        }

        [Test]
        public void ParsedFormulas_PlayLikeHandBuiltOnes()
        {
            // D's triangle must beat A-A, so D comes first.
            var fd = FormulaNotation.ToFormula("D", Symbol.D, "A1-B-A-1");
            var fb = FormulaNotation.ToFormula("B", Symbol.B, "A-A");
            var game = TestUtil.Game(fd, fb);
            var root = game.RootBoard;
            game.PlaceA(root, 1);
            game.PlaceA(root, 2);            // B at 2
            game.PlaceA(root, 3);
            var result = game.PlaceA(root, 0); // hub touches 2 and 3: A(0)-B(2)-A(3) triangle
            Assert.AreSame(fd, result.Steps[0].Formula);
            TestUtil.AssertSlot(root, 0, Symbol.D);
        }
    }
}
