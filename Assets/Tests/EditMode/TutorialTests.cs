using System;
using System.Linq;
using NUnit.Framework;

namespace Bagels.Core.Tests
{
    /// <summary>The tutorial's script file and its fixed first order (design.md, "Tutorial").</summary>
    public class TutorialTests
    {
        [Test]
        public void Script_GroupsLinesUnderHeaders_AsPages()
        {
            var script = TutorialScript.Parse(
                "# comment\n[welcome]\nHello.\r\n\nSecond page.\n[place-a]\n[vertical adjacency]\nOne.\n");
            CollectionAssert.AreEqual(new[] { "Hello.", "Second page." }, script.PagesOf("welcome"));
            Assert.IsEmpty(script.PagesOf("place-a"), "a header with no lines has no pages");
            CollectionAssert.AreEqual(new[] { "One." }, script.PagesOf("vertical adjacency"), "ids may hold spaces");
            Assert.IsEmpty(script.PagesOf("missing"));
        }

        [Test]
        public void Script_IgnoresATrailingDoubleBackslash()
        {
            var script = TutorialScript.Parse("[catalysts]\nFirst page.\\\\\r\nSecond page.\n\\\\\n");
            CollectionAssert.AreEqual(new[] { "First page.", "Second page." }, script.PagesOf("catalysts"));
        }

        [Test]
        public void ImperfectOrders_CountOnlyOnceTheWholeOrderIsIn()
        {
            var session = new GameSession(EconomyTests.FullRules(), seed: 7);
            var root = session.State.RootBoard;
            var order = new Order(99, new[] { new ItemSpec(Symbol.C, new ItemSpec(Symbol.A)), new ItemSpec(Symbol.B) }, 20);
            session.AddOrder(order);

            var c = session.State.PutWithoutCascade(root, 0, Symbol.C);
            session.State.PutWithoutCascade(c.Child, 0, Symbol.A);
            session.State.PutWithoutCascade(c.Child, 1, Symbol.B); // an extra
            Assert.AreEqual(DeliveryOutcome.Accepted, session.Deliver(order, c));
            Assert.AreEqual(0, session.ImperfectOrdersCompleted, "the imperfect item alone doesn't count");

            var b = session.State.PutWithoutCascade(root, 3, Symbol.B);
            Assert.AreEqual(DeliveryOutcome.Completed, session.Deliver(order, b));
            Assert.AreEqual(1, session.ImperfectOrdersCompleted);
        }

        [Test]
        public void PerfectOrders_DoNotCountAsImperfect()
        {
            var session = new GameSession(EconomyTests.FullRules(), seed: 7);
            var order = new Order(99, new[] { new ItemSpec(Symbol.B) }, 20);
            session.AddOrder(order);
            var b = session.State.PutWithoutCascade(session.State.RootBoard, 0, Symbol.B);
            Assert.AreEqual(DeliveryOutcome.Completed, session.Deliver(order, b));
            Assert.AreEqual(0, session.ImperfectOrdersCompleted);
        }

        [Test]
        public void Script_RejectsTextBeforeAnyHeader()
        {
            Assert.Throws<FormatException>(() => TutorialScript.Parse("Hello.\n[welcome]\n"));
            Assert.Throws<FormatException>(() => TutorialScript.Parse("[ ]\nHello.\n"));
        }

        [Test]
        public void FirstOrder_IsABAndACHoldingAnA()
        {
            var session = new GameSession(EconomyTests.FullRules(), seed: 7);
            Assert.AreEqual(1, session.Orders.Count);
            var order = session.Orders[0];
            CollectionAssert.AreEqual(new[] { "B", "C(A)" }, order.Items.Select(i => i.ToString()));
            Assert.AreEqual(order.Reward, order.Tip, "nothing can be given yet, so the whole reward is the tip");
            Assert.That(order.Reward, Is.InRange(11, 13), "worth about its $12 cost");
        }
    }
}
