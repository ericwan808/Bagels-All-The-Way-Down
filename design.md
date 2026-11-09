## Design Document for Bagels All The Way Down
### Introduction
This is a design document for a Unity puzzle game. The goal is to craft a certain bagel by placing elements (bagels) in slots on a board (bread). The board is recursive in the sense that boards can be placed within the slots of other boards. Bagels and bread are crafted by satisfying formulas, which require other bagels and bread to be adjacent to each other in a certain pattern.

Formulas will automatically trigger and return the result into the most recently placed location. When cascades occur, this rule holds and the entire cascading chain will land where the player just placed an element. Components cannot be moved once placed or created on the board. A formula might require that A is adjacent to another A to create B. This consumes both As. An element is considered vertically adjacent to the children of an adjacent board. Certain formulas will produce new boards as their output. Boards may be used as elements in some formulas, so in essence a board is just a special element that can hold other elements. Some formulas will have catalysts. In the case that the catalyst is placed last, it will be consumed. Otherwise, the catalyst will not be consumed.

Players will start with an infinite amount of "A"s from which to create the other elements.

### Technical design
There will be a Pastry object that will have a name, a symbol code enum (just letters of the alphabet for now), and a sprite.
Bread is a special case of a Pastry object that will contain its own Graph object.
A Graph object will have a series of "slots" in a particular adjacency pattern which can be filled by Pastry instances.
When a Pastry object is placed into a Graph's slots, it will check whether a formula is satisfied. Note that we only need to check formulas involving the most recently place or created Pastry.
There will be a global list of formulas to check.

Overall, the structure will be similar to a tree with a root that is a Pastry Object with a Graph that is W7.
In Unity, these Pastries should be Prefabs that can be instantiated multiple times.

### User Interface
Use the canvas object to create the user interface. There will be a side panel with a list of formulas. This side panel should also contain a A object icon that spawns a new A object which can be dragged onto the board when clicked.

Only show items "two levels down" from the current board. That is to say, if board H contains (board C contains (element A and board C contains(element B))), all icons sould be showing except element B. When the user hovers over a board contained in the current board and scrolls up, the screen should zoom in onto that board which then becomes the new current board. When hovering over a board contained in the current board, it should highlight to indicate it can be scrolled up on. Scrolling down on the current board will zoom out to its parent unless it is the root in which case this does nothing.

To implement this zoom in, you should not literally zoom in the camera. Instead, temporarily move the parent board and the other sibling elements to the side out of view and expand the child we want to zoom in on.

Once this is working, you can start working on showing a zoom in and zoom out animation by making the parent board gradually transparent.

### Specifications
A pattern is a series of adjacencies that must be satisfied. This could be a chain of adjacencies (including vertical adjacencies), or it could be more complex like a triangle. 
When more than one match fires, we will use the one first in the list. Formulas should therefore be listed most-specific first (e.g. D's triangle is completed by an A that would also satisfy A+A->B, so D must come before B).

To the game, there will always be a "most recent element", whether that was placed by the player or created by a previous formula. This is what can trigger new formulas. If it is a catalyst, it will be consumed and replaced with the formula's product.

Vertical adjacency applies to all children in a board. It only works one level down and its direction (up/parent or down/child) is specified in the formula.

The formula will differentiate between normal adjacency and vertical adjacency.

It is okay for the formula product to end up on a different board than its ingredients.

When a board is used as an ingredient, its children are simply destoryed. Its content does not matter for the formula.
Different types of boards will have different slot layouts (P3 and W7 to name a few, W7 is a hub plus 6 around it)

The target bagel can appear anywhere. As can go to any empty slot.

Keep the game logic separate from the visuals. Pastry types and formulas fit well as ScriptableObjects: data assets for name, symbol, sprite and recipe. The board state is easiest as plain C# (graph, slots, matching, cascades), with prefabs only for display. That makes cascades testable without the Unity scene, and lets animations play after the logic has finished.

### Formulas

#### Text notation (developer-facing only)
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
Listed in product order. Two rules keep the list consistent:
- A formula only uses elements that come before its product in this list.
- When more than one formula matches, the game checks them from the bottom of this table up (J first, B last), and the first match fires.

| # | Product | Condensed formula | Explicit node/edge formula |
|---|---|---|---|
| 1 | B | `A-A` | `A A \| 0-1` |
| 2 | C (board, K3 triangle) | `B-B` | `B B \| 0-1` |
| 3 | D | `A1-B-A-1` | `A B A \| 0-1 1-2 0-2` |
| 4 | E (board, K2,3) | `D<A>D` | `D A D \| 0<1 1>2` |
| 5 | F | `B-[C]-B` | `B [C] B \| 0-1 1-2` |
| 6 | G | `F-B-F` | `F B F \| 0-1 1-2` |
| 7 | H (board, W7) | `B>G<B` | `B G B \| 0>1 1<2` |
| 8 | I | `F1>G<F-1` | `F G F \| 0>1 1<2 0-2` |
| 9 | J | `A>A>[I]` | `A A [I] \| 0>1 1>2` |
