using System;
using System.Linq;
using NUnit.Framework;

namespace Bagels.Core.Tests
{
    /// <summary>design.md, "Priority orders": timed orders worth more, starting once D is unlocked.</summary>
    public class PriorityOrderTests
    {
        GameSession session;
        Board root;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(EconomyTests.FullRules(), seed: 1);
            root = session.State.RootBoard;
        }

        void BuyD()
        {
            session.GrantMoney(session.Progress.NextFormulaPrice.Value);
            session.BuyNextFormula();
        }

        Element Build(ItemSpec spec, Board board, int slot)
        {
            var e = session.State.PutWithoutCascade(board, slot, spec.Symbol);
            for (int i = 0; i < spec.Contents.Count; i++) Build(spec.Contents[i], e.Child, i);
            return e;
        }

        static Order Priority(double seconds, int reward = 100, int tip = 100) =>
            new Order(1, new[] { new ItemSpec(Symbol.B), new ItemSpec(Symbol.C) }, reward,
                      tip == reward ? null : new[] { Symbol.B }, tip, timeLimit: seconds);

        [Test]
        public void NoPriorityOrders_BeforeD()
        {
            Assert.IsFalse(session.PriorityOrdersStarted);
            Assert.IsFalse(session.Orders.Any(o => o.IsPriority));
        }

        [Test]
        public void UnlockingD_AddsOnePriorityOrderStraightAway()
        {
            int before = session.Orders.Count;
            BuyD();
            Assert.AreEqual(before + 1, session.Orders.Count);
            var priority = session.Orders.Single(o => o.IsPriority);
            Assert.That(priority.Items.Count, Is.InRange(2, 3));
            Assert.AreEqual(priority.Items.Count == 2 ? 60.0 : 180.0, priority.TimeLimit);
            Assert.AreEqual(priority.TimeLimit, priority.TimeLeft);

            // Later unlocks don't add another.
            session.GrantMoney(session.Progress.NextFormulaPrice.Value);
            session.BuyNextFormula();
            Assert.AreEqual(1, session.Orders.Count(o => o.IsPriority));
        }

        [Test]
        public void Timer_IsOneMinuteForTwoItems_ThreeForThree()
        {
            Assert.AreEqual(60.0, Order.PrioritySecondsFor(2));
            Assert.AreEqual(180.0, Order.PrioritySecondsFor(3));
        }

        [Test]
        public void Value_FallsFromThreeTimesToTwice_At30SecondsLeft_ThenStays()
        {
            var order = Priority(120);
            Assert.AreEqual(3.0, order.RewardMultiplier, 1e-9);
            Assert.AreEqual(300, order.Reward);
            Assert.AreEqual(300, order.Tip);
            order.Tick(45); // 75 s left: halfway from 120 to 30
            Assert.AreEqual(2.5, order.RewardMultiplier, 1e-9);
            Assert.AreEqual(250, order.Reward);
            order.Tick(45); // 30 s left
            Assert.AreEqual(2.0, order.RewardMultiplier, 1e-9);
            order.Tick(20);
            Assert.AreEqual(2.0, order.RewardMultiplier, 1e-9);
            Assert.AreEqual(200, order.Reward);
            Assert.IsFalse(order.IsExpired);
            order.Tick(20);
            Assert.AreEqual(0, order.TimeLeft);
            Assert.IsTrue(order.IsExpired);
        }

        [Test]
        public void ExtraValue_IsAllTip_ComponentsStayTheSame()
        {
            var order = Priority(120, reward: 100, tip: 95); // a B worth 5 (the price here is only for the arithmetic), plus a tip of 95
            CollectionAssert.AreEqual(new[] { Symbol.B }, order.RewardComponents);
            Assert.AreEqual(300, order.Reward);
            Assert.AreEqual(295, order.Tip);
            Assert.AreEqual(100, order.BaseReward);
        }

        [Test]
        public void NormalOrders_HaveNoTimer()
        {
            var order = session.Orders[0];
            Assert.IsFalse(order.IsPriority);
            Assert.AreEqual(1.0, order.RewardMultiplier);
            session.Tick(1000);
            Assert.IsFalse(order.IsExpired);
            Assert.IsTrue(session.Orders.Contains(order));
        }

        [Test]
        public void CompletingAPriorityOrder_PaysTheTipAtThatMoment()
        {
            BuyD();
            var order = session.Orders.Single(o => o.IsPriority);
            session.Tick(order.TimeLimit.Value - Order.PriorityLateSeconds); // down to twice the value
            int expectedTip = order.Tip;
            int money = session.Money;
            for (int i = 0; i < order.Items.Count; i++)
                session.Deliver(order, Build(order.Items[i], root, Enumerable.Range(0, root.SlotCount).First(root.IsEmpty)));
            Assert.IsFalse(session.Orders.Contains(order));
            Assert.AreEqual(money + expectedTip, session.Money);
            Assert.AreEqual(order.BaseReward * 2, order.Reward, 1);
        }

        [Test]
        public void ARunOutTimer_TakesTheOrderAway_AndItIsReplaced()
        {
            BuyD();
            var order = session.Orders.Single(o => o.IsPriority);
            var others = session.Orders.Where(o => o != order).ToList();
            var expired = session.Tick(order.TimeLimit.Value - 1);
            Assert.IsEmpty(expired);
            expired = session.Tick(1);
            CollectionAssert.AreEqual(new[] { order }, expired);
            Assert.IsFalse(session.Orders.Contains(order));
            Assert.AreEqual(1, session.ExpiredOrders);
            foreach (var o in others) Assert.IsTrue(session.Orders.Contains(o), "the other orders stay");
            Assert.AreEqual(Math.Min(GameSession.MaxOrders, others.Count + 2), session.Orders.Count,
                            "replaced, plus one more while under three");
            Assert.LessOrEqual(session.Orders.Count(o => !o.IsFinal), GameSession.MaxOrders);
        }

        [Test]
        public void Tick_DoesNothing_ForNoTime()
        {
            BuyD();
            var order = session.Orders.Single(o => o.IsPriority);
            session.Tick(0);
            session.Tick(-5);
            Assert.AreEqual(order.TimeLimit, order.TimeLeft);
        }

        [Test]
        public void Generator_PriorityOrders_HaveTwoOrThreeItems_EquallyOften()
        {
            var rules = EconomyTests.FullRules();
            var progress = new Progression(rules.Catalog);
            progress.UnlockUpTo(25); // D
            var generator = new OrderGenerator(rules, seed: 3);
            int two = 0, three = 0;
            for (int n = 0; n < 400; n++)
            {
                var order = generator.Generate(progress, progress.IsUnlocked, priority: true);
                Assert.IsTrue(order.IsPriority);
                Assert.AreEqual(Order.PrioritySecondsFor(order.Items.Count), order.TimeLimit);
                if (order.Items.Count == 2) two++;
                else if (order.Items.Count == 3) three++;
                else Assert.Fail($"a priority order has 2 or 3 items: {order}");
            }
            Assert.That(two, Is.InRange(160, 240), "about half have 2 items");
            Assert.That(three, Is.InRange(160, 240), "about half have 3 items");
        }

        [Test]
        public void Generator_RollsPriority_About15PercentOfTheTime()
        {
            var generator = new OrderGenerator(EconomyTests.FullRules(), seed: 5);
            int hits = Enumerable.Range(0, 2000).Count(_ => generator.RollPriority());
            Assert.That(hits / 2000.0, Is.InRange(0.12, 0.18));
        }

        [Test]
        public void Order_RejectsABadTimer()
        {
            Assert.Throws<ArgumentException>(() => Priority(0));
            Assert.Throws<ArgumentException>(() =>
                new Order(1, new[] { new ItemSpec(Symbol.L) }, 0, null, 0, isFinal: true, timeLimit: 60));
        }
    }
}
