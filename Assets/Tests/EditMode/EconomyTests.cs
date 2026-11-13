using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Bagels.Core.Tests.TestUtil;

namespace Bagels.Core.Tests
{
    /// <summary>The Economy, Orders and Winning sections of design.md, on the full formula list.</summary>
    public class EconomyTests
    {
        static readonly (Symbol symbol, string name, int? price, string formula)[] List =
        {
            (Symbol.A, "Everything bagel", 0, null),
            (Symbol.B, "Everything bagel + Cream cheese", 5, "A-A"),
            (Symbol.C, "Slice of wonderbread", 15, "B-B"),
            (Symbol.D, "Everything everything bagel", 20, "A1-B-A-1"),
            (Symbol.E, "Croissant", 50, "D<A>D"),
            (Symbol.F, "Dharma bagel", 30, "B-[C]-B"),
            (Symbol.G, "Earth bagel", 75, "F-B-F"),
            (Symbol.H, "Na'an", 100, "B>G<B"),
            (Symbol.I, "Black hole bagel", 150, "F1>G<F-1"),
            (Symbol.J, "Galaxy bagel", 400, "D>G>[I]<G<D"),
            (Symbol.K, "Bagel on a turtle on a bagel on a turtle", null, "J1-J>F-F<J-J>F-F<1"),
            (Symbol.L, "True plain bagel", null, "K>J>I(-G>F1(>D>B(-A)(-A))(>D>B(-A)(-A)))(-G>1)"),
        };

        public static Rules FullRules()
        {
            var formulas = List.Where(r => r.formula != null)
                               .Select(r => FormulaNotation.ToFormula(r.symbol.ToString(), r.symbol, r.formula))
                               .Reverse(); // the furthest-down formula wins
            var catalog = List.Select(r => new PastryInfo(r.symbol, r.name, r.price,
                                                          unlockedAtStart: r.symbol <= Symbol.C,
                                                          unlocksWithPrevious: r.symbol == Symbol.F,
                                                          orderLevels: r.symbol == Symbol.H ? 3 : r.symbol == Symbol.E ? 2 : 1));
            var bread = new Dictionary<Symbol, BoardLayout>
            {
                [Root] = BoardLayout.W7, [Symbol.C] = BoardLayout.K3, [Symbol.E] = BoardLayout.K23, [Symbol.H] = BoardLayout.W7,
            };
            return new Rules(Root, formulas, bread, Symbol.L, catalog);
        }

        GameSession session;
        Board root;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(FullRules(), seed: 1);
            root = session.State.RootBoard;
        }

        // ------------------------------------------------------------ start, unlocking

        [Test]
        public void Start_NoMoney_KnowsABAndC_OneOrder()
        {
            Assert.AreEqual(0, session.Money);
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.A));
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.B));
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.C));
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.D));
            Assert.AreEqual(Symbol.D, session.Progress.NextLocked.Symbol);
            Assert.AreEqual(1, session.Orders.Count);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void Unlocks_FollowMoneyInListOrder_FWithE()
        {
            session.GrantMoney(19);
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.D));
            session.GrantMoney(1);
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.D));
            CollectionAssert.AreEqual(new[] { Symbol.D }, session.LastUnlocked.Select(i => i.Symbol));

            session.GrantMoney(29); // $49: E costs 50; F (30) waits for E even though it is cheaper
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.E));
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.F));
            session.GrantMoney(1);
            CollectionAssert.AreEqual(new[] { Symbol.E, Symbol.F }, session.LastUnlocked.Select(i => i.Symbol));
        }

        [Test]
        public void Unlocks_OneJumpCanUnlockSeveral_ButPricelessOnesNeedCrafting()
        {
            session.GrantMoney(10000);
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.J));
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.K), "K unlocks only when a J is crafted");
            Assert.AreEqual(Symbol.K, session.Progress.NextLocked.Symbol);
        }

        [Test]
        public void Progression_KAfterJCrafted_LAfterKCrafted()
        {
            var progress = new Progression(FullRules().Catalog);
            progress.Update(10000);
            Assert.IsFalse(progress.IsUnlocked(Symbol.K));
            progress.NoteCrafted(Symbol.J);
            progress.Update(0); // money doesn't matter any more; unlocks stay
            Assert.IsTrue(progress.IsUnlocked(Symbol.K));
            Assert.IsFalse(progress.IsUnlocked(Symbol.L));
            progress.NoteCrafted(Symbol.K);
            progress.Update(0);
            Assert.IsTrue(progress.IsUnlocked(Symbol.L));
            Assert.IsNull(progress.NextLocked);
        }

        [Test]
        public void LockedFormulas_DoNotFire()
        {
            // An A-B-A triangle would make D, but D is locked, so the A-A pair makes B and then B-B makes C.
            session.PlaceA(root, 1);
            session.PlaceA(root, 2);          // B@2
            session.PlaceA(root, 3);          // A@3 next to B@2: nothing
            var result = session.PlaceA(root, 0); // hub: A0-B2-A3 triangle, A0-A3 pair
            Assert.IsFalse(result.Steps.Any(s => s.Product.Symbol == Symbol.D));
            AssertSlot(root, 0, Symbol.C);
        }

        [Test]
        public void UnlockedFormula_Fires()
        {
            session.GrantMoney(20);
            session.PlaceA(root, 1);
            session.PlaceA(root, 2);
            session.PlaceA(root, 3);
            var result = session.PlaceA(root, 0);
            Assert.AreEqual(Symbol.D, result.Steps[0].Product.Symbol);
        }

        // ------------------------------------------------------------ buying

        [Test]
        public void Buying_NeedsUnlockAndMoney_AndPricelessIsNeverForSale()
        {
            Assert.IsFalse(session.CanBuy(Symbol.B), "no money yet");
            Assert.Throws<InvalidOperationException>(() => session.Place(root, 0, Symbol.B));
            session.GrantMoney(10000);
            Assert.IsTrue(session.CanBuy(Symbol.J));
            Assert.IsFalse(session.CanBuy(Symbol.K));
            session.Progress.NoteCrafted(Symbol.J);
            session.GrantMoney(0);
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.K));
            Assert.IsFalse(session.CanBuy(Symbol.K), "K has no price");
        }

        [Test]
        public void Buying_ChargesThePrice_PlacesEmptyBread_AndCanCascade()
        {
            session.GrantMoney(40);
            session.Place(root, 0, Symbol.C);
            Assert.AreEqual(25, session.Money);
            Assert.AreEqual(0, root[0].Child.Elements.Count());

            session.Place(root, 1, Symbol.B);
            session.Place(root, 3, Symbol.B); // two Bs next to the hub C, not to each other: no formula (F is locked)
            Assert.AreEqual(15, session.Money);
            AssertSlot(root, 0, Symbol.C);
        }

        [Test]
        public void BuyingACraftedComponent_DoesNotCountAsCrafting()
        {
            session.GrantMoney(10000);
            session.Place(root, 0, Symbol.J);
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.K));
        }

        // ------------------------------------------------------------ undo

        [Test]
        public void Undo_RevertsAPlacementAndItsCascade_AndRefundsPurchases()
        {
            session.GrantMoney(5);
            session.PlaceA(root, 1);
            session.Place(root, 2, Symbol.B); // bought B next to A: nothing fires
            Assert.AreEqual(0, session.Money);
            session.Undo();
            Assert.AreEqual(5, session.Money);
            AssertEmpty(session.State.RootBoard, 2);
            AssertSlot(session.State.RootBoard, 1, Symbol.A);

            session.PlaceA(session.State.RootBoard, 2); // A-A → B
            AssertSlot(session.State.RootBoard, 2, Symbol.B);
            session.Undo();
            AssertSlot(session.State.RootBoard, 1, Symbol.A);
            AssertEmpty(session.State.RootBoard, 2);
        }

        [Test]
        public void Undo_GoesBackAtMostTenPlacements()
        {
            session.GrantMoney(100000);
            int start = session.Money;
            session.Place(root, 0, Symbol.H);
            var h = root[0].Child;
            for (int i = 0; i < 7; i++) session.Place(h, i, Symbol.G);
            for (int i = 1; i <= 4; i++) session.Place(root, i, Symbol.G); // 12 placements in all
            Assert.AreEqual(GameSession.MaxUndo, session.UndoCount);

            while (session.CanUndo) session.Undo();
            // The first two placements (H, and one G in it) are beyond reach.
            Assert.AreEqual(start - 100 - 75, session.Money);
            var board = session.State.RootBoard;
            AssertSlot(board, 0, Symbol.H);
            Assert.AreEqual(1, board[0].Child.Elements.Count());
            AssertEmpty(board, 1, 2, 3, 4);
        }

        // ------------------------------------------------------------ orders

        /// <summary>Builds an item directly on the board (no formulas), as if the player had made it.</summary>
        Element Build(ItemSpec spec, Board board, int slot)
        {
            var e = session.State.PutWithoutCascade(board, slot, spec.Symbol);
            for (int i = 0; i < spec.Contents.Count; i++) Build(spec.Contents[i], e.Child, i);
            return e;
        }

        [Test]
        public void ItemSpec_MatchesExactContents_InAnyArrangement()
        {
            var spec = new ItemSpec(Symbol.C, new ItemSpec(Symbol.A), new ItemSpec(Symbol.B));
            var c = session.State.PutWithoutCascade(root, 0, Symbol.C);
            session.State.PutWithoutCascade(c.Child, 2, Symbol.B);
            Assert.IsFalse(spec.Matches(c), "missing the A");
            session.State.PutWithoutCascade(c.Child, 0, Symbol.A);
            Assert.IsTrue(spec.Matches(c));
            session.State.PutWithoutCascade(c.Child, 1, Symbol.A);
            Assert.IsFalse(spec.Matches(c), "nothing extra allowed");
            Assert.IsFalse(new ItemSpec(Symbol.C).Matches(c), "a bare C means an empty C");
            Assert.AreEqual(25 - 5, spec.Cost(session.Rules), "C 15 + A 0 + B 5");
        }

        [Test]
        public void Delivery_FillsItemsOneUnitAtATime_ThenPaysAndReplaces()
        {
            var order = session.Orders[0];
            var stray = session.State.PutWithoutCascade(root, 6, Symbol.A);
            Assert.AreEqual(DeliveryOutcome.Rejected, session.Deliver(order, stray));
            Assert.IsTrue(session.State.IsInPlay(stray), "a rejected element stays on the board");

            session.PlaceA(root, 3); // something to undo
            Assert.IsTrue(session.CanUndo);
            for (int i = 0; i < order.Items.Count; i++)
            {
                var element = Build(order.Items[i], root, i == 3 ? 5 : i);
                var outcome = session.Deliver(order, element);
                Assert.AreEqual(i < order.Items.Count - 1 ? DeliveryOutcome.Accepted : DeliveryOutcome.Completed, outcome);
                Assert.IsFalse(element.IsAlive);
                Assert.IsFalse(session.CanUndo, "delivering is final");
            }
            Assert.AreEqual(order.Reward, session.Money);
            Assert.IsFalse(session.Orders.Contains(order));
            Assert.AreEqual(2, session.Orders.Count, "a replacement, plus one more while under three");
        }

        [Test]
        public void Orders_NeverMoreThanThree()
        {
            for (int round = 0; round < 6; round++)
            {
                var order = session.Orders[0];
                foreach (var item in order.Items)
                {
                    int slot = Enumerable.Range(0, root.SlotCount).First(root.IsEmpty);
                    session.Deliver(order, Build(item, root, slot));
                }
                Assert.LessOrEqual(session.Orders.Count, GameSession.MaxOrders);
            }
            Assert.AreEqual(GameSession.MaxOrders, session.Orders.Count);
        }

        [Test]
        public void GeneratedOrders_FollowTheRules()
        {
            var rules = FullRules();
            var maxLevels = new Dictionary<Symbol, int> { [Symbol.C] = 0, [Symbol.E] = 0, [Symbol.H] = 0 };
            foreach (Symbol s in Enum.GetValues(typeof(Symbol))) if (!maxLevels.ContainsKey(s)) maxLevels[s] = 0;
            foreach (int money in new[] { 0, 20, 50, 100, 400 })
            {
                var progress = new Progression(rules.Catalog);
                progress.Update(money);
                var generator = new OrderGenerator(rules, seed: money);
                for (int n = 0; n < 150; n++)
                {
                    var order = generator.Generate(progress, progress.IsUnlocked);
                    Assert.LessOrEqual(order.Items.Count, Order.MaxItems);
                    int cost = order.Items.Sum(i => i.Cost(rules));
                    Assert.Greater(cost, 0);
                    Assert.GreaterOrEqual(order.Reward, 1);
                    Assert.LessOrEqual(2 * order.Reward, cost, $"reward {order.Reward} is more than half of {cost}: {order}");
                    foreach (var item in order.Items)
                    {
                        Assert.IsTrue(item.WithinNestingLimits(rules), $"{item} nests too deep");
                        int levels = item.Levels(rules);
                        if (levels > maxLevels[item.Symbol]) maxLevels[item.Symbol] = levels;
                        AssertAvailable(item, progress, rules);
                        Assert.IsTrue(generator.CanBuild(item, progress.IsUnlocked), $"{item} can't be built");
                    }
                }
            }
            Assert.LessOrEqual(maxLevels[Symbol.C], 1);
            Assert.LessOrEqual(maxLevels[Symbol.E], 2);
            Assert.LessOrEqual(maxLevels[Symbol.H], 3);
            TestContext.WriteLine($"deepest seen: C {maxLevels[Symbol.C]}, E {maxLevels[Symbol.E]}, H {maxLevels[Symbol.H]}");
        }

        [Test]
        public void NestingLimits_CHoldsOnlyBagels_EAndHGoDeeper()
        {
            var rules = FullRules();
            ItemSpec I(Symbol s, params ItemSpec[] c) => new ItemSpec(s, c);
            Assert.IsTrue(I(Symbol.C, I(Symbol.A), I(Symbol.B)).WithinNestingLimits(rules));
            Assert.IsFalse(I(Symbol.C, I(Symbol.C)).WithinNestingLimits(rules), "a C may not hold bread, even empty");
            Assert.IsTrue(I(Symbol.E, I(Symbol.C, I(Symbol.B))).WithinNestingLimits(rules));
            Assert.IsFalse(I(Symbol.E, I(Symbol.E, I(Symbol.C))).WithinNestingLimits(rules), "three levels inside an E");
            Assert.IsTrue(I(Symbol.H, I(Symbol.E, I(Symbol.C, I(Symbol.A)))).WithinNestingLimits(rules));
            Assert.IsFalse(I(Symbol.H, I(Symbol.C, I(Symbol.C))).WithinNestingLimits(rules), "the inner C still holds only bagels");
            Assert.IsFalse(I(Symbol.H, I(Symbol.H, I(Symbol.H, I(Symbol.C)))).WithinNestingLimits(rules), "four levels from the top");
        }

        [Test]
        public void DesignExample_CostsTwentyFive_PaysAtMostHalf()
        {
            var rules = FullRules();
            var items = new[] { new ItemSpec(Symbol.B), new ItemSpec(Symbol.C, new ItemSpec(Symbol.A), new ItemSpec(Symbol.B)) };
            Assert.AreEqual(25, items.Sum(i => i.Cost(rules)));
            var order = new Order(1, items, 10);
            Assert.LessOrEqual(2 * order.Reward, 25);
        }

        static void AssertAvailable(ItemSpec item, Progression progress, Rules rules)
        {
            Assert.IsTrue(progress.IsUnlocked(item.Symbol), $"{item.Symbol} is not unlocked");
            Assert.IsNotNull(rules.PriceOf(item.Symbol), $"{item.Symbol} has no price");
            foreach (var c in item.Contents) AssertAvailable(c, progress, rules);
        }

        [Test]
        public void CanBuild_RejectsContentsThatWouldReact()
        {
            var rules = FullRules();
            var progress = new Progression(rules.Catalog);
            var generator = new OrderGenerator(rules, 0);
            // Inside a triangle every pair touches, so two As always become a B.
            Assert.IsFalse(generator.CanBuild(new ItemSpec(Symbol.C, new ItemSpec(Symbol.A), new ItemSpec(Symbol.A)), progress.IsUnlocked));
            Assert.IsTrue(generator.CanBuild(new ItemSpec(Symbol.C, new ItemSpec(Symbol.A), new ItemSpec(Symbol.B)), progress.IsUnlocked));
        }

        // ------------------------------------------------------------ winning

        [Test]
        public void FinalOrder_AppearsWhenLUnlocks_AndWinsTheGame()
        {
            session.GrantMoney(10000);
            Assert.IsFalse(session.Orders.Any(o => o.IsFinal));
            session.Progress.NoteCrafted(Symbol.J);
            session.GrantMoney(0);
            session.Progress.NoteCrafted(Symbol.K);
            session.GrantMoney(0);

            var final = session.Orders.Single(o => o.IsFinal);
            CollectionAssert.AreEqual(new[] { Symbol.L }, final.Items.Select(i => i.Symbol));
            var l = session.State.PutWithoutCascade(root, 0, Symbol.L);
            Assert.AreEqual(DeliveryOutcome.Won, session.Deliver(final, l));
            Assert.IsTrue(session.Won);
            Assert.Throws<InvalidOperationException>(() => session.PlaceA(root, 1));
        }
    }
}
