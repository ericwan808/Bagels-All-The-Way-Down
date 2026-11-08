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

### Testing
We will consider these testing Pastry types:
A - the default element we can place
B - created through two adjacent As
C - created through two adjacent Bs, is a board with the shape of a triangle
D - created through a triangle of two As and one B
E - created with a B that is vertically adjacent to two Ds at a lower level with the B being a catalyst

Try to create each Pastry and verify its behavior against the design description.