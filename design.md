## Design Document for Bagels All The Way Down

### Overview
Bagels All The Way Down is a Unity puzzle game. The goal is to craft a final target bagel by placing elements (bagels) in the slots of a board (bread). Boards are recursive: a board is a special element that can itself be placed in a slot of another board and hold elements of its own.

Bagels and breads are crafted by satisfying formulas. A formula requires certain bagels and breads to be adjacent to each other in a specific pattern. The player earns money by fulfilling orders, and uses that money to buy components and unlock new formulas on the way to the final bagel.

### Core rules

#### Boards
- Each type of board has its own slot layout: C is K3 (a triangle), E is K2,3, and H is W7 (a hub plus 6 slots around it).
- The root of the game is a W7 board.
- Elements cannot be moved once they are placed or created on the board. The only exception is dragging them to an order tray (see Orders).
- An undo button reverts the player's most recent placement. A placement is an element the player placed themselves, not a product created by a formula. Undoing a placement also reverts the cascade it triggered. Undo goes back at most 10 placements from the current state.
- A reset button unlocks once H is unlocked. It throws away the whole board and starts over on an empty H, keeping money, stock, unlocked formulas and orders. A reset is final: it clears the undo history.
- The game opens on a title screen with the game's name and a Start Game button. The menu button in the game opens a popup with Resume, which closes it, and Quit to Title, which returns to the title screen and throws the game away: starting again begins a new game.

#### Adjacency
- **Normal adjacency**: two elements in neighbouring slots of the same board.
- **Vertical adjacency** has a direction, and only reaches one level:
  - **Down**: an element is adjacent to every child of a board in a neighbouring slot.
  - **Up**: an element is adjacent to every element in a slot next to the board it sits in.
- The children of two adjacent boards are not adjacent to each other.
- Formulas distinguish between normal and vertical adjacency, and they specify the direction of each vertical link.

#### Formulas
- A formula is a pattern of adjacencies that must all be satisfied. It can be a chain, including vertical links, or a more complex shape like a triangle.
- Satisfying a formula consumes its ingredients. For example, A adjacent to another A creates B and consumes both As.
- Some formulas produce new boards, and boards can be ingredients in other formulas. When a board is used as an ingredient, its children are destroyed. Its contents do not matter to the formula.
- **Catalysts**: some formulas have a catalyst ingredient. If the catalyst was the last element placed, it is consumed and replaced by the product. Otherwise it is not consumed, and if it is a board, its children stay.
- **Priority**: when more than one formula matches, the one furthest down the formula list fires. For example, an A that completes D's triangle would also satisfy `A-A` for B. D is lower in the list, so D fires.
- **Ties**: if a formula can be satisfied by more than one set of elements, the first set found is used. The search runs in slot order on each board, so the result is deterministic.

#### Placement and cascades
- The game always tracks a "most recent element", which is either the element the player just placed or the product a formula just created. Only formulas that involve the most recent element are checked.
- Formulas trigger automatically and put their product in the most recent element's location. During a cascade, each product becomes the new most recent element, so the whole chain lands in the slot where the player last placed an element.
- A product may end up on a different board than its ingredients.

### Economy

#### Money and purchasing
- The player starts with $0. They are meant to earn their first money by crafting and selling Bs and Cs, and to buy a component directly only when it is hard to make through formulas.
- A is free, and the player can place as many as they like.
- Every unlocked component with a price, boards included, can be bought and placed in any empty slot on any board. To buy one, the player drags its icon out of its formula in the side panel.
- Components with no price (K and L) can never be bought. They can only be crafted.
- Money only comes from order tips.
- **Stock**: components given by order rewards are kept in a stock. Each formula's product icon in the side panel shows how many free copies are stocked. Dragging the icon out places a stocked copy for free, and only buys one once the stock runs out. Undoing that placement returns the copy to stock.
- Stocked components do not count as money, so they cannot pay for formulas.

#### Unlocking formulas
- At the start, the player has only As and knows the formulas for B and C.
- Formulas unlock in the order of the formula list. The next formula is bought: it costs the price of its component, and the player buys it with the button on the greyed-out stand-in in the side panel once they have that much money. Buying a formula also lets the player buy that component.
- Buying a formula is final: undo never takes the formula back or refunds it.
- A component with no price has its formula revealed once the component before it in the list is created on the board by a formula. Buying that component does not count. K unlocks when a J is first crafted, and L unlocks when K is first crafted.

### Orders
- Orders are the only source of money. Each order asks for a specific set of bagels and breads. A bread in an order can require specific contents, including nested breads with their own contents. For example, an order might ask for a B and a C containing an A and a B.
- **Matching**: a bread must contain every listed content, but it may also hold extra toppings. Each extra topping makes the order imperfect.
  - An extra is any component that is not on the list, at any level of nesting. For example, an extra G inside a nested E counts.
  - Each extra counts once, whatever is inside it. An extra E holding two Ds is one extra.
  - A bread with no listed contents accepts anything, but everything inside it is extra.
  - If the listed contents could be matched in more than one way, the way with the fewest extras counts.
  - If an element fits more than one of an order's items, it fills the item it matches the most: the one that asks for the most components. A tie goes to the item it fits with the fewest extras, then to the first. For example, an H holding an A and a B fills "H holding an A" rather than a plain "H".
- To fulfill an order, the player drags matching components from the board to that order's tray. An order can be filled one item at a time, but each item goes in as one unit. In the example, the B and the "C containing an A and a B" can be added separately, but the C goes in together with its contents.
- An order has at most 3 items.
- Orders only ask for bagels and breads that are currently available.
- **Rewards**: an order's reward is worth about the total cost of its items, give or take 5% of the cost. In the example above, the items cost $14, so the reward is worth about $14. The reward has two parts: components and a tip.
  - **Components**: worth about 1/3 of the reward, which is about 1/3 of the order's cost. They may fall a little short of that, and never exceed 40% of the reward.
    - A reward only gives components that are unlocked and priced. It never gives A, or either of the two most recently unlocked components. Breads are given empty.
    - A reward gives at most 5 components.
    - Rewards favour the most valuable component that still fits, so they shift to higher-value components as the player progresses. Some randomness stays: each cheaper component has half the chance of the one above it.
    - Given components go into the player's stock (see Money and purchasing). They are given even when the order is imperfect.
  - **Tip**: the rest of the reward's value is paid as money. A perfect order earns the full tip. An imperfect order earns 90% of it with 1 extra topping, 70% with 2, 50% with 3 to 6, and 10% with 7 or more.
  - In the early game, when no component can be given, the whole reward is the tip.
  - The tip amount is never shown in advance. An order card shows only "Rewards:" and an icon for each component it gives, with "xN" when it gives more than one, above a thin line that separates the rewards from the order's items. An order that gives no components has neither.
- **Customer response**: when an order is completed, a small text box with the customer's reaction pops up in the top-right corner of the board, next to the orders panel. It fades in quickly, lingers for about 3 seconds, then fades out. The line is picked at random from those written for that many extra toppings, in `Assets/Resources/CustomerResponses.txt`.
  - Along with the text box, the tip appears as a small green "+$amount" beside the box that fades in, floats up to the top of the screen, and disappears after about a second. An order that earns no tip shows a red "+0".
- The game starts with 1 order, and at most 3 orders are active at a time.
- When an order is fulfilled, a replacement order is generated right away. If fewer than 3 orders are active after that, one more is added.
- **Priority orders**: an order with a timer and a bigger reward.
  - Once D's formula is unlocked, one priority order appears straight away, on top of the orders already active (so the docket can briefly hold 4). After that, every new order has a 15% chance to be a priority order.
  - A priority order has 2 or 3 items, equally likely. Its timer is 1 minute with 2 items and 3 minutes with 3.
  - It is worth 3 times what the same order would be worth normally when it appears. The multiplier falls steadily to 2 times when 30 seconds are left, and stays at 2 times until the timer runs out. The components are those of a normal order; all the extra value is tip, and extra toppings cut it as usual. The tip paid is the one at the moment the last item goes in.
  - When the timer runs out, the customer leaves: the order is removed, along with anything already handed in, and is replaced like a fulfilled order.
  - The timer pauses while a popup or a tutorial overlay is showing.
  - Its card is tinted and shows "VIP", the time left (in red for the last 30 seconds). Priority orders sit at the top of the docket, below the final order.
- The orders panel lists orders as a docket, with the final order (once it appears) always at the top. When an order is fulfilled, its card fades away slowly. Then the orders below it slide up smoothly, and the new order fades in at the bottom.
- An order card draws each item the way it will look on the board: a bread is drawn with its toppings inside its slots. A card shows one layer of toppings. For example, a C with a B and an A shows as a C holding a B and an A. When an item's toppings have toppings of their own, those nested breads are drawn empty. Every bread item with toppings has a small + button next to it that opens a popup with every layer, even when there is only one. For example, an H holding an E that holds an A shows on the card as an H holding an empty E, and the popup shows the H holding the E, with the E holding the A on its own row below. In the popup, each bread that has toppings gets its own row, drawn with its direct toppings inside it and indented under its parent's row. The popup has no title, only a close button, and is drawn large: rows 272 px tall with 248 px bread icons. It shrinks only as far as needed for a deep order to fit on screen.
- For recursion in orders, Cs are limited to one level of recusion (they must directly hold bagels or nothing, not breads), Es can have two levels, and Hs can have three. This counts from the top-most level.
- Each bread also limits which toppings an order may ask for in it. A bread may still be ordered empty. These limits apply to every bread of that type, at any level of nesting. They only cover what the order asks for, not extra toppings the player adds:
  - A C holds exactly one of these sets as its direct contents: A, B+A, D, D+A, D+B or F. This only counts the C's own toppings.
  - An E holds at most one G or I in total, and nothing after I in the formula list (no J, K or L). This counts everything inside the E, including the contents of nested breads. For example, an E holding a G and a C is allowed, but an E holding a G and another E that holds an I is not.
  - An H holds at most one J or K in total. This also counts everything inside the H, including the contents of nested breads. For example, an H holding a J and an E that holds another J is not allowed.
- **Shifting toward newer components**: as more formulas are unlocked, orders become less likely to ask for lower-level components (those earlier in the formula list). This applies at every level of an order: the order's own items and the contents of breads at any depth. For example, once G is unlocked, an order should rarely ask for a plain B or a C holding only an A.
  - Each component's chance of being picked is weighted by how recently it was unlocked. The 3 most recently unlocked components get full weight, and each step further back halves the weight.
  - A component's weight never drops below 5% of full weight, so older components still appear now and then.
- **Spreading out high-level components**: G and everything after it in the formula list count as high-level. A layer is one set of siblings: either the order's own items or the direct contents of one bread. A layer usually holds at most 2 high-level components and never more than 3. For example, an order should not ask for four Is side by side, or an H whose direct contents are four Gs.

### Winning
- The final target bagel (L) can be crafted anywhere on the board.
- As soon as L's formula is unlocked, a special order appears that asks only for L. Fulfilling that order wins the game.

### User interface

#### Side panel
- The UI is built on a Canvas. A side panel shows the unlocked formulas.
- The formulas are split across multiple pages instead of one scrolling list.
- Each formula's product icon can be dragged onto the board to buy that component. The panel also has an A icon that spawns a free A to drag onto the board.
- A greyed-out rectangle stands in for the next component to unlock. For a formula that is bought, it has a buy button showing the price, dimmed until the player can afford it. For one that unlocks by crafting, it says what to craft.
- The top-left corner has two small buttons: a menu button (no menu yet) and a "?" help button. The help button opens a popup over the screen with the controls (opening boards, getting free As and buying components, handing items to orders) and the legend for reading a formula. The legend's vertical adjacency row has a + button that plays the tutorial's vertical adjacency video in its corner window (see Tutorial). Clicking outside the popup, its close button or Escape closes it.
- When a formula unlocks, the grey box wipes away to reveal it: the formula fades in and its product pops into place. The panel does not turn the page by itself. If the new formula is on a page the player isn't viewing, that page's arrow button pulses, and the reveal plays when the player turns to it.

#### Board navigation
- Only items up to two levels below the current board are shown. For example, if board H contains a board C, which contains an element A and another board C holding an element B, every icon is shown except B.
- Hovering over a board inside the current board highlights it to show that the player can scroll up on it. Scrolling up zooms into that board, which becomes the new current board.
- Scrolling down zooms out to the parent board. On the root board this does nothing.
- Zooming does not move the camera. Instead, the parent board and the sibling elements move to the side, out of view, and the target child expands to fill the view.
- Once that works, add zoom-in and zoom-out animations in which the parent board gradually fades out or in.

#### Tutorial
- A new game starts with a tutorial: a series of overlays whose text lives in `Assets/Resources/TutorialOverlays.txt`, one `[id]` header per overlay and one line per page. An overlay with no text is skipped.
- Each page shows in a box at the top centre of the screen. A page either waits for a click on the box, or waits for the player to do what it asks and then moves on by itself. While a page waits for a click, the rest of the screen is darkened to draw the eye to the text. Some overlays also point at something with a pulsing gold frame. The tutorial hides while a popup is open.
- The first order of every game is a B and a C holding an A, so the tutorial can walk through making them.
- The overlays, in order:
  - `welcome`: click through.
  - `place-a`: frames the free A in the side panel. The first page is clicked through; the second moves on once something is on the board.
  - `first-formula`: frames the formula for B; moves on once a B is crafted.
  - `first-formula-complete`: click through.
  - `recursive-breads`: frames the formula for C; moves on once a C is crafted.
  - `zoom`: the first page moves on once the player opens a bread's board, the second once a C holding an A is on the main board and they are back on it.
  - `consuming-ingredients`: making the C used up the Bs; moves on once a B is on the main board again.
  - `hand-in`: the first page frames the first order and moves on once it is complete; the second is clicked through.
- Some overlays are not in the sequence: each shows once per game, the first time its condition holds, and waits its turn if another overlay is showing. Their pages are clicked through.
  - `unlocking-D`: the player has enough money to buy D's formula.
  - `priority-order`: a priority order is on the docket (the first one turns up as soon as D is unlocked). It frames that order's card.
  - `creating-D`: the first D is crafted by its formula (buying one does not count).
  - `vertical-adjacency`: E is unlocked. Once it is clicked through, `Assets/Resources/vertical_adjacency_example.mp4` loops, muted, in a small window in the lower right of the board (just left of the orders panel) until the player closes it with its X button.
  - `catalysts`: F is unlocked.
  - `extras-and-tips`: an order is completed with at least one extra topping. Handing in an imperfect item does not count until the whole order is in.

#### Audio
- The main theme (`Assets/Resources/Audio/BATWD_01_MainTheme.ogg`) loops in the background for the whole session. It fades in from silence over about 3 seconds when the game begins, and keeps playing when a new game starts.

### Technical design
- **Pastry**: has a name, a symbol code enum (letters of the alphabet for now) and a sprite. In Unity, pastries are prefabs that can be instantiated many times.
- **Bread**: a special case of Pastry that contains its own Graph.
- **Graph**: a set of slots in a particular adjacency pattern. Each slot can hold a Pastry instance.
- The overall structure is a tree. Its root is a Pastry whose Graph is a W7.
- There is one global list of formulas. When a Pastry is placed into a slot, the game checks only the formulas that involve the most recently placed or created Pastry.
- Keep the game logic separate from the visuals. Pastry types and formulas work well as ScriptableObjects: data assets that hold the name, symbol, sprite and recipe. The board state (graph, slots, matching, cascades) is plain C#, with prefabs used only for display. This makes cascades testable without the Unity scene and lets animations play after the logic has finished.

### Formulas

#### Text notation
Formulas are small graphs: ingredients are nodes and required adjacencies are edges. They are written in one of two forms.

**Condensed form** (modeled on chemistry's SMILES notation):
- **Atoms**: an ingredient is its pastry symbol, e.g. `A`, `B`.
- **Catalysts**: wrapped in brackets, e.g. `[B]`.
- **Normal adjacency**: `-` between two atoms, e.g. `A-B`.
- **Vertical adjacency**: `<` or `>`, with the lower-level atom on the small side. `D<B` means D is one level below B.
- **Branches**: extra neighbors of an atom go in parentheses right after it. `A(-B)(-C)-D` means A is adjacent to B, C and D.
- **Cycles (ring closures)**: a digit after an atom tags it. The next time that digit appears, it closes a bond back to the tagged atom. The bond symbol goes before the closing digit and is read as if the tagged atom were written in its place. `A1-B-A-1` is a triangle, and `B<1` would mean B is below the tagged atom. A digit can be reused once its ring has closed. Use more digits for overlapping rings.
- **Spelling is not unique**: `A-A-B` and `B-A-A` are the same formula. If a string is ever used as a key (e.g. duplicate detection), pick a single canonical spelling.

**Explicit form** works for any shape and is the easiest to parse. It maps directly onto `FormulaAsset`'s ingredients and links:
- The nodes come first, separated by spaces, with catalysts in brackets. Then a `|`, then the edges.
- Edges use 0-based node indices with the same bond symbols: `i-j` is normal, `i<j` means i is below j, and `i>j` means j is below i.
- Example: `A B A | 0-1 1-2 0-2`.

#### Formula list
Listed in product order, which is also the unlock order. Two rules keep the list consistent:
- A formula only uses elements that come before its product in this list.
- When more than one formula matches, the game checks them from the bottom of this table up (L first, B last), and the first match fires.

| Product  | Name                                     | Price    | Condensed formula | Explicit node/edge formula |
| -------- | ---------------------------------------- | -------- | ----------------- | -------------------------- |
| A        | Everything bagel                         | 0        | no formula        |                            |
| B        | Everything bagel + Cream cheese          | 2        | `A-A`             | `A A \| 0-1`               |
| C (K3)   | Croissant                                | 10       | `B-B`             | `B B \| 0-1`               |
| D        | Everything everything bagel              | 25       | `A1-B-A-1`        | `A B A \| 0-1 1-2 0-2`     |
| E (K2,3) | Baguette                                 | 100       | `D<A>D`           | `D A D \| 0<1 1>2`         |
| F        | Dharma bagel                             | 888       | `B-[C]-B`         | `B [C] B \| 0-1 1-2`       |
| G        | Earth bagel                              | 1970       | `F-B-F`           | `F B F \| 0-1 1-2`         |
| H (W7)   | Slice of wonderbread                     | 11500      | `B>G<B`           | `B G B \| 0>1 1<2`         |
| I        | Black hole bagel                         | 197800      | `F1>G<F-1`        | `F G F \| 0>1 1<2 0-2`     |
| J        | Galaxy bagel                             | 4297000      | `D>G>[I]<G<D`     | `D G [I] G D \| 0>1 1>2 2<3 3<4` |
| K        | Bagel on a turtle on a bagel on a turtle | No price | `J1-J>F-F<J-J>F-F<1` | `J J F F J J F F \| 0-1 1>2 2-3 3<4 4-5 5>6 6-7 7<0` |
| L        | True plain bagel                         | No price | `K>J>I(-G>F1(>D>B(-A)(-A))(>D>B(-A)(-A)))(-G>1)` | `K J I G G F D D A B A A B A \| 0>1 1>2 2-3 2-4 3>5 4>5 5>6 5>7 6>9 7>12 9-8 9-10 12-11 12-13` |