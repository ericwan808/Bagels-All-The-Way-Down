using System.Linq;
using Bagels.Core;
using Bagels.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bagels.View.Tests
{
    /// <summary>
    /// The User Interface rules from design.md, checked against what <see cref="GameView"/> draws. Outside Play mode
    /// zooming is instant, so these run as EditMode tests.
    /// <para>
    /// Uses its own small level, because the test set can't nest bread two deep (inside a K3 board the D triangle
    /// always fires before a second C can form): R is a W7 root, C = A–A is a K3 bread, and E = C–C.
    /// </para>
    /// </summary>
    public class ViewNavigationTests
    {
        GameView view;
        Object[] owned;

        Board Root => view.State.RootBoard;

        [SetUp]
        public void SetUp()
        {
            var r = Pastry("R", Symbol.R, LayoutKind.W7);
            var a = Pastry("A", Symbol.A, LayoutKind.None);
            var c = Pastry("C", Symbol.C, LayoutKind.K3);
            var e = Pastry("E", Symbol.E, LayoutKind.None);
            var fe = Formula(e, c, c);
            var fc = Formula(c, a, a);
            var level = ScriptableObject.CreateInstance<LevelAsset>();
            level.EditorInit(r, e, a, new[] { fe, fc });
            owned = new Object[] { r, a, c, e, fe, fc, level };

            view = new GameObject("Test Game").AddComponent<GameView>();
            view.EditorInit(level,
                            AssetDatabase.LoadAssetAtPath<PastryView>("Assets/Prefabs/Pastry.prefab"),
                            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Circle.png"),
                            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Generated/Ring.png"),
                            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Generated/Square.png"),
                            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Generated/Triangle.png"));
            view.RefreshPreview();
            Assert.IsNotNull(view.State, "preview did not build a game");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(view.gameObject);
            foreach (var o in owned) Object.DestroyImmediate(o);
        }

        /// <summary>
        /// Root R holds outer C at slot 2; outer C holds inner C at slot 1 and an A at slot 2; inner C holds an A
        /// at slot 0. From the root, the inner C is two levels down and its A three.
        /// </summary>
        (Element outer, Element inner) BuildNested()
        {
            view.Place(Root, 1);
            view.Place(Root, 2);
            var outer = Root[2];
            Assert.AreEqual(Symbol.C, outer.Symbol);

            view.Place(outer.Child, 0);
            view.Place(outer.Child, 1);
            var inner = outer.Child[1];
            Assert.AreEqual(Symbol.C, inner.Symbol);

            view.Place(outer.Child, 2);
            view.Place(inner.Child, 0);
            return (outer, inner);
        }

        string[] Drawn() =>
            view.transform.Find("Board").GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();

        int DrawnAs() => Drawn().Count(n => n.StartsWith("A @"));

        [Test]
        public void RootViewShowsTwoLevelsDown()
        {
            BuildNested();
            var names = Drawn();
            Assert.AreEqual(1, DrawnAs(), "only the A on outer C (level 2) should show; the A in inner C is level 3");
            Assert.AreEqual(1, names.Count(n => n == "C (bread)"), "outer C is drawn open");
            Assert.AreEqual(1, names.Count(n => n == "C (bread, closed)"), "inner C is drawn closed");
        }

        [Test]
        public void ZoomInMakesTheChildTheCurrentBoardAndRevealsALevel()
        {
            var (outer, inner) = BuildNested();
            view.ZoomIn(outer);
            Assert.AreSame(outer, view.Focus);
            Assert.AreEqual(2, DrawnAs(), "outer's A and inner's A are now levels 1 and 2");
            Assert.AreEqual(0, Drawn().Count(n => n == "C (bread, closed)"));
            StringAssert.EndsWith("C", view.FocusPath());
        }

        [Test]
        public void ZoomInOnlyWorksOnBreadOnTheCurrentBoard()
        {
            var (_, inner) = BuildNested();
            view.ZoomIn(inner); // two levels down
            Assert.AreSame(view.State.Root, view.Focus);
            view.Place(Root, 5);
            Assert.AreEqual(Symbol.A, Root[5].Symbol);
            view.ZoomIn(Root[5]); // not bread
            Assert.AreSame(view.State.Root, view.Focus);
        }

        [Test]
        public void ZoomOutReturnsToParentAndStopsAtRoot()
        {
            var (outer, inner) = BuildNested();
            view.ZoomIn(outer);
            view.ZoomIn(inner);
            Assert.AreSame(inner, view.Focus);
            view.ZoomOut();
            Assert.AreSame(outer, view.Focus);
            view.ZoomOut();
            Assert.AreSame(view.State.Root, view.Focus);
            view.ZoomOut();
            Assert.AreSame(view.State.Root, view.Focus, "zooming out at the root does nothing");
            Assert.AreEqual(1, DrawnAs());
        }

        [Test]
        public void ConsumedCurrentBoardFallsBackToNearestBoardStillInPlay()
        {
            BuildNested();
            var outer = Root[2];
            view.ZoomIn(outer);
            // A@4, A@3 → C@3, which sits next to outer C@2 → E consumes both.
            view.Place(Root, 4);
            view.Place(Root, 3);
            Assert.AreEqual(Symbol.E, Root[3].Symbol);
            Assert.IsFalse(outer.IsAlive);
            Assert.AreSame(view.State.Root, view.Focus);
        }

        [Test]
        public void ResetReturnsToRoot()
        {
            var (outer, _) = BuildNested();
            view.ZoomIn(outer);
            view.NewGame();
            Assert.AreSame(view.State.Root, view.Focus);
        }

        static PastryType Pastry(string name, Symbol symbol, LayoutKind layout)
        {
            var p = ScriptableObject.CreateInstance<PastryType>();
            p.name = name;
            p.EditorInit(name, symbol, Color.white, layout);
            return p;
        }

        static FormulaAsset Formula(PastryType product, PastryType first, PastryType second)
        {
            var f = ScriptableObject.CreateInstance<FormulaAsset>();
            f.EditorInit(product,
                         new[] { new FormulaAsset.Ingredient { type = first }, new FormulaAsset.Ingredient { type = second } },
                         new[] { new FormulaAsset.Link { from = 0, to = 1, kind = AdjacencyKind.Normal } });
            return f;
        }
    }
}
