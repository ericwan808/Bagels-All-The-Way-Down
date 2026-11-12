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

#### Unlocking formulas
- At the start, the player has only As and knows the formulas for B and C.
- Formulas unlock in the order of the formula list. The next formula is revealed as soon as the player's current money reaches that component's price. Unlocking a formula also lets the player buy that component.
- F is a special case: it unlocks at the same time as E.
- A component with no price has its formula revealed once the component before it in the list is created on the board by a formula. Buying that component does not count. K unlocks when a J is first crafted, and L unlocks when K is first crafted.

### Orders
- Orders are the only source of money. Each order asks for a specific set of bagels and breads. A bread in an order can require specific contents, including nested breads with their own contents. For example, an order might pay $15 for a B and a C containing an A and a B.
- Matching is strict: a bread must contain exactly the listed contents, with nothing extra.
- To fulfill an order, the player drags matching components from the board to that order's tray. An order can be filled one item at a time, but each item goes in as one unit. In the example, the B and the "C containing an A and a B" can be added separately, but the C goes in together with its contents.
- An order has at most 5 items.
- Orders only ask for bagels and breads that are currently available.
- The total cost of the items in an order must not be more than twice the order's reward. In the example above, the items cost $25 and the order pays $15.
- The game starts with 1 order, and at most 3 orders are active at a time.
- When an order is fulfilled, a replacement order is generated right away. If fewer than 3 orders are active after that, one more is added.

### Winning
- The final target bagel (L) can be crafted anywhere on the board.
- As soon as L's formula is unlocked, a special order appears that asks only for L. Fulfilling that order wins the game.

### User interface

#### Side panel
- The UI is built on a Canvas. A side panel shows the unlocked formulas.
- The formulas are split across multiple pages instead of one scrolling list.
- Each formula's product icon can be dragged onto the board to buy that component. The panel also has an A icon that spawns a free A to drag onto the board.
- A greyed-out rectangle stands in for the next component to unlock and shows its unlock condition.

#### Board navigation
- Only items up to two levels below the current board are shown. For example, if board H contains a board C, which contains an element A and another board C holding an element B, every icon is shown except B.
- Hovering over a board inside the current board highlights it to show that the player can scroll up on it. Scrolling up zooms into that board, which becomes the new current board.
- Scrolling down zooms out to the parent board. On the root board this does nothing.
- Zooming does not move the camera. Instead, the parent board and the sibling elements move to the side, out of view, and the target child expands to fill the view.
- Once that works, add zoom-in and zoom-out animations in which the parent board gradually fades out or in.

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
| B        | Everything bagel + Cream cheese          | 5        | `A-A`             | `A A \| 0-1`               |
| C (K3)   | Slice of wonderbread                     | 15       | `B-B`             | `B B \| 0-1`               |
| D        | Everything everything bagel              | 20       | `A1-B-A-1`        | `A B A \| 0-1 1-2 0-2`     |
| E (K2,3) | Croissant                                | 50       | `D<A>D`           | `D A D \| 0<1 1>2`         |
| F        | Dharma bagel                             | 30       | `B-[C]-B`         | `B [C] B \| 0-1 1-2`       |
| G        | Earth bagel                              | 75       | `F-B-F`           | `F B F \| 0-1 1-2`         |
| H (W7)   | Na'an                                    | 100      | `B>G<B`           | `B G B \| 0>1 1<2`         |
| I        | Black hole bagel                         | 150      | `F1>G<F-1`        | `F G F \| 0>1 1<2 0-2`     |
| J        | Galaxy bagel                             | 400      | `D>G>[I]<G<D`     | `D G [I] G D \| 0>1 1>2 2<3 3<4` |
| K        | Bagel on a turtle on a bagel on a turtle | No price | `J1-J>F-F<J-J>F-F<1` | `J J F F J J F F \| 0-1 1>2 2-3 3<4 4-5 5>6 6-7 7<0` |
| L        | True plain bagel                         | No price | `K>J>I(-G>F1(>D>B(-A)(-A))(>D>B(-A)(-A)))(-G>1)` | `K J I G G F D D A B A A B A \| 0>1 1>2 2-3 2-4 3>5 4>5 5>6 5>7 6>9 7>12 9-8 9-10 12-11 12-13` |
