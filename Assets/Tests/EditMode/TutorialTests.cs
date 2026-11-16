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
            Assert.That(order.Reward, Is.InRange(19, 21), "worth about its $20 cost");
        }
    }
}
