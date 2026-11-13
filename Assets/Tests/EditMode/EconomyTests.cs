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
                                                          orderLevels: r.symbol == Symbol.H ? 3 : r.symbol == Symbol.E ? 2 : 1,
                                                          contentRules: ContentRulesFor(r.symbol),
                                                          highLevel: r.symbol >= Symbol.G));
            var bread = new Dictionary<Symbol, BoardLayout>
            {
                [Root] = BoardLayout.W7, [Symbol.C] = BoardLayout.K3, [Symbol.E] = BoardLayout.K23, [Symbol.H] = BoardLayout.W7,
            };
            return new Rules(Root, formulas, bread, Symbol.L, catalog);
        }

        /// <summary>design.md, "Orders": what each bread may hold in an order.</summary>
        static ContentRules ContentRulesFor(Symbol symbol) => symbol switch
        {
            Symbol.C => new ContentRules(new[] { "A", "BA", "D", "DA", "DB", "F" }.Select(ContentRules.ParseSymbols)),
            Symbol.E => new ContentRules(limited: new[] { Symbol.G, Symbol.I }, limitedMax: 1, highest: Symbol.I),
            Symbol.H => new ContentRules(limited: new[] { Symbol.J, Symbol.K }, limitedMax: 1),
            _ => null,
        };

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
        public void Formulas_AreBoughtInListOrder_ForTheirComponentsPrice_FWithE()
        {
            Assert.AreEqual(20, session.Progress.NextFormulaPrice, "D's formula costs what a D costs");
            session.GrantMoney(19);
            Assert.IsFalse(session.CanBuyNextFormula);
            Assert.Throws<InvalidOperationException>(() => session.BuyNextFormula());
            session.GrantMoney(1);
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.D), "money alone doesn't unlock it");
            Assert.IsTrue(session.CanBuyNextFormula);
            session.BuyNextFormula();
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.D));
            Assert.AreEqual(0, session.Money);
            CollectionAssert.AreEqual(new[] { Symbol.D }, session.LastUnlocked.Select(i => i.Symbol));

            session.GrantMoney(49); // E costs 50; F (30) comes with E
            Assert.AreEqual(50, session.Progress.NextFormulaPrice);
            Assert.IsFalse(session.CanBuyNextFormula);
            session.GrantMoney(1);
            session.BuyNextFormula();
            CollectionAssert.AreEqual(new[] { Symbol.E, Symbol.F }, session.LastUnlocked.Select(i => i.Symbol));
            Assert.AreEqual(0, session.Money);
        }

        [Test]
        public void BuyingFormulas_StopsAtPricelessOnes_WhichNeedCrafting()
        {
            session.GrantMoney(100000);
            while (session.CanBuyNextFormula) session.BuyNextFormula();
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.J));
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.K), "K unlocks only when a J is crafted");
            Assert.AreEqual(Symbol.K, session.Progress.NextLocked.Symbol);
            Assert.IsNull(session.Progress.NextFormulaPrice, "K's formula can't be bought");
        }

        [Test]
        public void BuyingAFormula_IsNotTakenBackByUndo_NorRefunded()
        {
            session.GrantMoney(25);
            session.Place(root, 0, Symbol.B); // $5
            session.BuyNextFormula();         // D, $20
            Assert.AreEqual(0, session.Money);
            session.Undo();
            Assert.AreEqual(5, session.Money, "undo refunds the B only");
            Assert.IsTrue(session.Progress.IsUnlocked(Symbol.D));
        }

        [Test]
        public void Progression_KAfterJCrafted_LAfterKCrafted()
        {
            var progress = new Progression(FullRules().Catalog);
            progress.UnlockUpTo(10000);
            Assert.IsFalse(progress.IsUnlocked(Symbol.K));
            progress.NoteCrafted(Symbol.J);
            progress.Update();
            Assert.IsTrue(progress.IsUnlocked(Symbol.K));
            Assert.IsFalse(progress.IsUnlocked(Symbol.L));
            progress.NoteCrafted(Symbol.K);
            progress.Update();
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
            session.BuyNextFormula();
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
            session.UnlockUpTo(10000);
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
            session.UnlockUpTo(10000);
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
            session.UnlockUpTo(100000);
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
        public void ItemSpec_NeedsEveryListedContent_InAnyArrangement_AndCountsExtras()
        {
            var spec = new ItemSpec(Symbol.C, new ItemSpec(Symbol.A), new ItemSpec(Symbol.B));
            var c = session.State.PutWithoutCascade(root, 0, Symbol.C);
            session.State.PutWithoutCascade(c.Child, 2, Symbol.B);
            Assert.IsFalse(spec.Matches(c), "missing the A");
            session.State.PutWithoutCascade(c.Child, 0, Symbol.A);
            Assert.AreEqual(0, spec.ExtrasIn(c), "perfect");
            session.State.PutWithoutCascade(c.Child, 1, Symbol.A);
            Assert.AreEqual(1, spec.ExtrasIn(c), "an extra A is accepted but counted");
            Assert.AreEqual(3, new ItemSpec(Symbol.C).ExtrasIn(c), "a bare C takes anything, all of it extra");
            Assert.IsNull(new ItemSpec(Symbol.E).ExtrasIn(c), "the wrong bread");
            Assert.AreEqual(25 - 5, spec.Cost(session.Rules), "C 15 + A 0 + B 5");
        }

        [Test]
        public void Extras_CountAtAnyDepth_ButAnExtraBreadCountsOnce()
        {
            var spec = new ItemSpec(Symbol.H, new ItemSpec(Symbol.E, new ItemSpec(Symbol.D)));
            var h = session.State.PutWithoutCascade(root, 0, Symbol.H);
            var e =session.State.PutWithoutCascade(h.Child, 0, Symbol.E);
            session.State.PutWithoutCascade(e.Child, 0, Symbol.D);
            Assert.AreEqual(0, spec.ExtrasIn(h));
            session.State.PutWithoutCascade(e.Child, 1, Symbol.G);
            Assert.AreEqual(1, spec.ExtrasIn(h), "an extra G inside the inner E");
            var extraE = session.State.PutWithoutCascade(h.Child, 1, Symbol.E);
            session.State.PutWithoutCascade(extraE.Child, 0, Symbol.D);
            session.State.PutWithoutCascade(extraE.Child, 1, Symbol.D);
            Assert.AreEqual(2, spec.ExtrasIn(h), "the extra E counts once, whatever it holds");
        }

        [Test]
        public void Extras_AreCountedTheCheapestWay()
        {
            // H(E(D)) holding E(D, G) and E(D): matching the plain E gives one extra (the other E), not two.
            var spec = new ItemSpec(Symbol.H, new ItemSpec(Symbol.E, new ItemSpec(Symbol.D)));
            var h = session.State.PutWithoutCascade(root, 0, Symbol.H);
            var stuffed = session.State.PutWithoutCascade(h.Child, 0, Symbol.E);
            session.State.PutWithoutCascade(stuffed.Child, 0, Symbol.D);
            session.State.PutWithoutCascade(stuffed.Child, 1, Symbol.G);
            var plain = session.State.PutWithoutCascade(h.Child, 1, Symbol.E);
            session.State.PutWithoutCascade(plain.Child, 0, Symbol.D);
            Assert.AreEqual(1, spec.ExtrasIn(h));
        }

        [Test]
        public void Tip_FullWhenPerfect_SixtyPercentAfterOneExtra_GoneAtFive()
        {
            var order = new Order(1, new[] { new ItemSpec(Symbol.B) }, 100, null, 100);
            CollectionAssert.AreEqual(new[] { 100, 60, 30, 10, 5, 0, 0 }, Enumerable.Range(0, 7).Select(order.TipAfter));
        }

        [Test]
        public void ImperfectOrder_CutsTheTip_ButStillGivesTheComponents()
        {
            var items = new[] { new ItemSpec(Symbol.C, new ItemSpec(Symbol.A)) };
            var order = new Order(99, items, 60, new[] { Symbol.B }, 55);
            session.AddOrder(order);

            var c = session.State.PutWithoutCascade(root, 0, Symbol.C);
            session.State.PutWithoutCascade(c.Child, 0, Symbol.A);
            session.State.PutWithoutCascade(c.Child, 1, Symbol.B); // one extra
            Assert.AreEqual(DeliveryOutcome.Completed, session.Deliver(order, c));
            Assert.AreEqual(1, order.Imperfections);
            Assert.AreEqual(33, session.Money, "60% of the $55 tip");
            Assert.AreEqual(1, session.StockOf(Symbol.B), "the components are given anyway");
        }

        [Test]
        public void FindItemFor_PrefersTheItemWithFewestExtras()
        {
            var order = new Order(1, new[] { new ItemSpec(Symbol.C), new ItemSpec(Symbol.C, new ItemSpec(Symbol.A)) }, 10);
            var c = session.State.PutWithoutCascade(root, 0, Symbol.C);
            session.State.PutWithoutCascade(c.Child, 0, Symbol.A);
            Assert.AreEqual(1, order.FindItemFor(c, out int extras), "C(A) fits the second item perfectly");
            Assert.AreEqual(0, extras);
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
            Assert.AreEqual(0, order.Imperfections);
            Assert.AreEqual(order.Tip, session.Money, "a perfect order earns the full tip");
            foreach (var g in order.RewardComponents.GroupBy(s => s))
                Assert.AreEqual(g.Count(), session.StockOf(g.Key), $"{g.Key} should be in stock");
            Assert.IsFalse(session.Orders.Contains(order));
            Assert.AreEqual(2, session.Orders.Count, "a replacement, plus one more while under three");
        }

        [Test]
        public void CompletedOrder_TheRestMoveUp_AndTheReplacementJoinsTheBottom()
        {
            // Fill the docket to three orders first.
            while (session.Orders.Count < GameSession.MaxOrders)
                foreach (var item in session.Orders[0].Items.ToList())
                    session.Deliver(session.Orders[0], Build(item, root, Enumerable.Range(0, root.SlotCount).First(root.IsEmpty)));
            var before = session.Orders.ToList();
            var first = before[0];
            foreach (var item in first.Items)
                session.Deliver(first, Build(item, root, Enumerable.Range(0, root.SlotCount).First(root.IsEmpty)));
            CollectionAssert.AreEqual(new[] { before[1], before[2] }, session.Orders.Take(2));
            Assert.IsFalse(before.Contains(session.Orders[2]), "the replacement is last");
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
                progress.UnlockUpTo(money);
                var generator = new OrderGenerator(rules, seed: money);
                for (int n = 0; n < 150; n++)
                {
                    var order = generator.Generate(progress, progress.IsUnlocked);
                    Assert.LessOrEqual(order.Items.Count, Order.MaxItems);
                    Assert.LessOrEqual(order.Items.Count(i => rules.IsHighLevel(i.Symbol)), OrderGenerator.MaxHighLevelPerLayer,
                                       $"too many high-level items side by side: {order}");
                    int cost = order.Items.Sum(i => i.Cost(rules));
                    Assert.Greater(cost, 0);
                    Assert.GreaterOrEqual(order.Reward, 1);
                    Assert.That(order.Reward, Is.InRange(cost * (OrderGenerator.RewardShare - OrderGenerator.RewardJitter) - 0.5,
                                                         cost * (OrderGenerator.RewardShare + OrderGenerator.RewardJitter) + 0.5),
                                $"reward {order.Reward} is not about the cost {cost}: {order}");
                    foreach (var item in order.Items)
                    {
                        Assert.IsTrue(item.WithinNestingLimits(rules), $"{item} nests too deep");
                        Assert.IsTrue(item.FollowsContentRules(rules), $"{item} holds toppings its bread doesn't allow");
                        AssertHighLevelPerLayer(item, rules);
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
        public void ContentRules_CToppingSets_EAndHLimits()
        {
            var rules = FullRules();
            ItemSpec I(Symbol s, params ItemSpec[] c) => new ItemSpec(s, c);
            ItemSpec Bread(Symbol s, string letters) =>
                I(s, ContentRules.ParseSymbols(letters).Select(x => I(x)).ToArray());

            foreach (var ok in new[] { "", "A", "BA", "AB", "D", "DA", "DB", "F" })
                Assert.IsTrue(Bread(Symbol.C, ok).FollowsContentRules(rules), $"C({ok})");
            foreach (var bad in new[] { "B", "AA", "DD", "FA", "DBA", "G" })
                Assert.IsFalse(Bread(Symbol.C, bad).FollowsContentRules(rules), $"C({bad})");

            Assert.IsTrue(Bread(Symbol.E, "GDA").FollowsContentRules(rules));
            Assert.IsTrue(Bread(Symbol.E, "IF").FollowsContentRules(rules));
            Assert.IsFalse(Bread(Symbol.E, "GG").FollowsContentRules(rules), "two Gs");
            Assert.IsFalse(Bread(Symbol.E, "GI").FollowsContentRules(rules), "a G and an I");
            Assert.IsFalse(Bread(Symbol.E, "J").FollowsContentRules(rules), "J comes after I");
            Assert.IsTrue(I(Symbol.E, I(Symbol.C, I(Symbol.D))).FollowsContentRules(rules));
            Assert.IsFalse(I(Symbol.E, I(Symbol.C, I(Symbol.B))).FollowsContentRules(rules), "the inner C follows its own sets");

            // An E's limits count everything inside it, at any depth.
            Assert.IsTrue(I(Symbol.E, I(Symbol.G), I(Symbol.C, I(Symbol.D))).FollowsContentRules(rules), "the design.md example");
            Assert.IsFalse(I(Symbol.E, I(Symbol.G), Bread(Symbol.E, "I")).FollowsContentRules(rules), "a G, and an I one level down");
            Assert.IsFalse(I(Symbol.E, Bread(Symbol.E, "G"), Bread(Symbol.E, "G")).FollowsContentRules(rules), "two Gs in two inner Es");
            Assert.IsFalse(I(Symbol.E, Bread(Symbol.E, "J")).FollowsContentRules(rules), "a J one level down");
            Assert.IsFalse(I(Symbol.E, Bread(Symbol.H, "J")).FollowsContentRules(rules), "a J inside an inner H");

            Assert.IsTrue(Bread(Symbol.H, "JGG").FollowsContentRules(rules));
            Assert.IsFalse(Bread(Symbol.H, "JJ").FollowsContentRules(rules), "two Js");
            Assert.IsFalse(Bread(Symbol.H, "JK").FollowsContentRules(rules), "a J and a K");
            Assert.IsTrue(I(Symbol.H, I(Symbol.J), I(Symbol.E, I(Symbol.G))).FollowsContentRules(rules), "Gs don't count for H");
            // An H's limit also counts everything inside it, and every E and H follows its rules, however deep.
            Assert.IsFalse(I(Symbol.H, I(Symbol.J), Bread(Symbol.H, "J")).FollowsContentRules(rules), "the design.md example");
            Assert.IsFalse(I(Symbol.H, I(Symbol.J), I(Symbol.H, Bread(Symbol.E, "K"))).FollowsContentRules(rules), "a K two levels down");
            Assert.IsFalse(I(Symbol.H, Bread(Symbol.E, "GG")).FollowsContentRules(rules), "a nested E keeps its own limit");
            Assert.IsFalse(I(Symbol.H, Bread(Symbol.H, "JJ")).FollowsContentRules(rules), "a nested H keeps its own limit");
        }

        [Test]
        public void RecencyWeight_NewestThreeFull_ThenHalving_NeverBelowFivePercent()
        {
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 0.5, 0.25, 0.125, 0.0625, 0.05, 0.05 },
                                      Enumerable.Range(0, 9).Select(OrderGenerator.RecencyWeight));
        }

        [Test]
        public void Rewards_AddUpToTheValue_AndNeverGiveAOrTheTwoNewest()
        {
            var rules = FullRules();
            foreach (int money in new[] { 0, 20, 50, 100, 400 })
            {
                var progress = new Progression(rules.Catalog);
                progress.UnlockUpTo(money);
                var forSale = rules.Catalog.Where(i => i.Price > 0 && progress.IsUnlocked(i.Symbol)).ToList();
                var newest = forSale.Skip(forSale.Count - OrderGenerator.RecentNotGiven).Select(i => i.Symbol).ToList();
                var generator = new OrderGenerator(rules, seed: money);
                for (int n = 0; n < 150; n++)
                {
                    var order = generator.Generate(progress, progress.IsUnlocked);
                    int componentValue = order.RewardComponents.Sum(s => rules.PriceOf(s).Value);
                    Assert.AreEqual(order.Reward, order.Tip + componentValue, $"the reward's parts don't add up: {order}");
                    Assert.LessOrEqual(componentValue, order.Reward * OrderGenerator.MaxComponentShare,
                                       $"the components leave too small a tip: {order}");
                    Assert.LessOrEqual(order.RewardComponents.Count, OrderGenerator.MaxRewardComponents);
                    foreach (var s in order.RewardComponents)
                    {
                        Assert.AreNotEqual(Symbol.A, s);
                        Assert.IsTrue(progress.IsUnlocked(s), $"{s} is not unlocked");
                        CollectionAssert.DoesNotContain(newest, s, $"{s} is one of the two newest: {order}");
                    }
                }
            }
        }

        [Test]
        public void Rewards_ShiftToMoreValuableComponents_AsThePlayerProgresses()
        {
            var rules = FullRules();
            double AverageComponentPrice(int money)
            {
                var progress = new Progression(rules.Catalog);
                progress.UnlockUpTo(money);
                var generator = new OrderGenerator(rules, seed: 3);
                var prices = new List<int>();
                for (int n = 0; n < 300; n++)
                    prices.AddRange(generator.Generate(progress, progress.IsUnlocked).RewardComponents.Select(s => rules.PriceOf(s).Value));
                TestContext.WriteLine($"${money}: {prices.Count} components, average ${(prices.Count > 0 ? prices.Average() : 0):0.0}");
                return prices.Count > 0 ? prices.Average() : 0;
            }
            Assert.AreEqual(0, AverageComponentPrice(0), "at the start only B and C are for sale, and they are the two newest");
            double early = AverageComponentPrice(50), late = AverageComponentPrice(400);
            Assert.Greater(early, 0);
            Assert.Greater(late, early * 2);
        }

        [Test]
        public void Stock_IsUsedBeforeMoney_AndUndoReturnsIt()
        {
            session.GrantMoney(100);
            session.GrantStock(Symbol.C);
            Assert.IsTrue(session.CanTake(Symbol.C));
            session.Place(root, 0, Symbol.C);
            Assert.AreEqual(100, session.Money, "a stocked C is free");
            Assert.AreEqual(0, session.StockOf(Symbol.C));
            session.Place(root, 1, Symbol.C);
            Assert.AreEqual(85, session.Money, "with no stock left, a C is bought");

            session.Undo();
            session.Undo();
            Assert.AreEqual(100, session.Money);
            Assert.AreEqual(1, session.StockOf(Symbol.C), "undo puts the C back in stock");
        }

        [Test]
        public void Stock_LetsThePlayerPlaceWithNoMoney_ButDoesNotCountTowardsUnlocks()
        {
            session.GrantStock(Symbol.C, 10); // worth $150
            session.GrantMoney(0);
            Assert.IsFalse(session.Progress.IsUnlocked(Symbol.D), "stock can't buy formulas");
            Assert.IsFalse(session.CanBuy(Symbol.C));
            Assert.IsTrue(session.CanTake(Symbol.C));
            session.Place(root, 0, Symbol.C);
            AssertSlot(root, 0, Symbol.C);
        }

        [Test]
        public void RewardText_ListsComponents_ButNotTheTipAmount()
        {
            var items = new[] { new ItemSpec(Symbol.B) };
            Assert.AreEqual("tip", new Order(1, items, 20).RewardText());
            Assert.AreEqual("C + tip", new Order(1, items, 20, new[] { Symbol.C }, 5).RewardText());
            Assert.AreEqual("D + 2×B", new Order(1, items, 30, new[] { Symbol.B, Symbol.D, Symbol.B }, 0).RewardText());
        }

        [Test]
        public void CustomerResponses_ByImperfections_EachHeaderCoversUpToTheNext()
        {
            var responses = CustomerResponses.Parse(
                "# comment\n[0]\nPerfect!\nLovely!\n\n[1]\nHm.\n[5+]\r\nWhat is this?\r\n");
            CollectionAssert.AreEqual(new[] { "Perfect!", "Lovely!" }, responses.LinesFor(0));
            CollectionAssert.AreEqual(new[] { "Hm." }, responses.LinesFor(1));
            CollectionAssert.AreEqual(new[] { "Hm." }, responses.LinesFor(4), "[1] covers up to [5+]");
            CollectionAssert.AreEqual(new[] { "What is this?" }, responses.LinesFor(9));
            Assert.AreEqual("Hm.", responses.Pick(2, new Random(0)));

            var fromTwo = CustomerResponses.Parse("[2]\nOops.");
            CollectionAssert.AreEqual(new[] { "Oops." }, fromTwo.LinesFor(0), "below the first header, use the first header");
            Assert.IsNull(CustomerResponses.Parse("# nothing yet").Pick(0, new Random(0)));
            Assert.Throws<FormatException>(() => CustomerResponses.Parse("A line before any header"));
            Assert.Throws<FormatException>(() => CustomerResponses.Parse("[lots]\nHi"));
        }

        [Test]
        public void CustomerResponses_ShippedFile_HasLinesForEveryCount()
        {
            var path = System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "CustomerResponses.txt");
            if (!System.IO.File.Exists(path)) path = "Assets/Resources/CustomerResponses.txt"; // in Unity, the project folder
            if (!System.IO.File.Exists(path)) Assert.Ignore("CustomerResponses.txt not found from " + Environment.CurrentDirectory);
            var responses = CustomerResponses.Parse(System.IO.File.ReadAllText(path));
            for (int n = 0; n <= Order.ImperfectionsToLoseTip + 2; n++)
                Assert.IsNotEmpty(responses.LinesFor(n), $"no lines for {n} extras");
        }

        [Test]
        public void Rewards_AreAllTip_WhileNothingCanBeGiven()
        {
            var rules = FullRules();
            var progress = new Progression(rules.Catalog); // only B and C for sale: the two newest
            var generator = new OrderGenerator(rules, seed: 5);
            for (int n = 0; n < 50; n++)
            {
                var order = generator.Generate(progress, progress.IsUnlocked);
                Assert.IsEmpty(order.RewardComponents);
                Assert.AreEqual(order.Reward, order.Tip);
            }
        }

        [Test]
        public void Rewards_ComponentsAreAboutHalfTheValue_LaterOn()
        {
            var rules = FullRules();
            var progress = new Progression(rules.Catalog);
            progress.UnlockUpTo(400);
            var generator = new OrderGenerator(rules, seed: 11);
            double components = 0, total = 0;
            for (int n = 0; n < 300; n++)
            {
                var order = generator.Generate(progress, progress.IsUnlocked);
                components += order.RewardComponents.Sum(s => rules.PriceOf(s).Value);
                total += order.Reward;
            }
            TestContext.WriteLine($"components are {components / total:P0} of reward value");
            Assert.That(components / total, Is.InRange(0.35, 0.6));
        }

        [Test]
        public void GeneratedOrders_FavourNewerComponents_ButStillUseOlderOnes()
        {
            var rules = FullRules();
            var progress = new Progression(rules.Catalog);
            progress.UnlockUpTo(400); // everything up to J
            var generator = new OrderGenerator(rules, seed: 7);
            var counts = new Dictionary<Symbol, int>();
            foreach (Symbol s in Enum.GetValues(typeof(Symbol))) counts[s] = 0;
            for (int n = 0; n < 400; n++)
                foreach (var item in generator.Generate(progress, progress.IsUnlocked).Items)
                    counts[item.Symbol]++;
            TestContext.WriteLine(string.Join(", ", counts.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}")));
            // J (newest) has full weight, B (eight steps back) 5%. B turns up a bit more than that, since once a layer
            // has its high-level components only the older ones are left.
            Assert.Less(counts[Symbol.B] * 4, counts[Symbol.J], "old components should be rare");
            Assert.Greater(counts[Symbol.B], 0, "but not gone");
        }

        static void AssertHighLevelPerLayer(ItemSpec item, Rules rules)
        {
            Assert.LessOrEqual(item.Contents.Count(c => rules.IsHighLevel(c.Symbol)), OrderGenerator.MaxHighLevelPerLayer,
                               $"{item} holds too many high-level components side by side");
            foreach (var c in item.Contents) AssertHighLevelPerLayer(c, rules);
        }

        [Test]
        public void DesignExample_CostsTwentyFive()
        {
            var rules = FullRules();
            var items = new[] { new ItemSpec(Symbol.B), new ItemSpec(Symbol.C, new ItemSpec(Symbol.A), new ItemSpec(Symbol.B)) };
            Assert.AreEqual(25, items.Sum(i => i.Cost(rules)));
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
            session.UnlockUpTo(10000);
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
