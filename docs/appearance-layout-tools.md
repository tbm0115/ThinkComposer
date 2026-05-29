# Appearance and Layout Tools

ThinkComposer now has a foundation for reusable diagram appearance and layout commands. The first implemented command is available from:

`Edit -> Appearance -> Fit Concept Width to Text`

## Fit Concept Width to Text

Select one or more concept symbols in the active view, then run the command. ThinkComposer measures the visible concept title text with the current symbol text format, applies conservative padding, respects minimum and maximum width limits, and preserves the current symbol height.

The command:

- fits only selected concept symbols
- skips relationships, complements, and other non-concept visuals
- runs as one undoable command variation
- re-renders affected symbols and connectors
- writes detailed counts and warnings to the lower-left application log

You can undo and redo the result with the normal edit history.

## Resize Handle Shortcut

Double-clicking a selected concept's left or right resize handle runs the same width auto-fit service used by the menu command. Normal click-and-drag resizing is unchanged.

## Route Links with Obstacle Avoidance

Use `Edit -> Appearance -> Route Links with Obstacle Avoidance` to reroute visible relationship connectors around concept symbols in the active view.

Selection behavior:

- If selected objects include connectors or relationship visual representations, only those selected links are routed.
- If no routeable link is selected, ThinkComposer asks before routing all visible links in the active view.
- The command is disabled when the active view has no visible routeable connectors.

Routing behavior:

- v1 uses the existing `VisualConnector.IntermediatePosition` field only.
- A connector is left straight when the straight segment avoids all inflated concept obstacles.
- Otherwise, the router tries one-bend orthogonal candidates: horizontal-first and vertical-first.
- Concept symbols are treated as obstacles, except the connector's own origin and target symbols.
- Simple relationships that hide their central symbol are routed as one relationship-level unit. ThinkComposer represents these as two connector segments, `source concept -> hidden relationship symbol -> target concept`; the router moves that hidden relationship symbol to the midpoint for a straight route or to the chosen orthogonal elbow for an L-shaped route.
- When a hidden-central simple relationship needs more than one elbow, the router can use a dogleg route without adding a new route model: the hidden relationship symbol becomes the hidden junction, and the two connector `IntermediatePosition` values become the source-side and target-side bends.
- Selecting one segment of a hidden-central relationship routes the whole relationship once, not each hidden-endpoint segment separately.
- Existing valid hand-routed connectors are preserved unless a candidate is materially better.
- If no valid straight or one-bend route exists, the connector is left unchanged and a warning is logged.

The command runs as one undoable command variation. Undo/redo should restore connector intermediate points and hidden relationship junction positions. Detailed per-connector and per-relationship diagnostics are written to the lower-left application log; the completion dialog only shows a concise summary.

v1 limitations:

- Only one bend is supported.
- Hidden-central simple relationships can dogleg around same-row or same-column blockers, but this is still limited to one hidden junction plus one bend per connector segment.
- Complements, group regions, and relationship central symbols are not obstacles yet.
- The native model is not extended with a serialized multi-point route, and ordinary visible-center connectors still use only one `IntermediatePosition`.
- If a non-hidden-central link needs more than one bend to avoid obstacles, it is skipped rather than force-routed.

Troubleshooting:

- For hidden-central relationships, inspect the candidate diagnostics in the application log. The log reports source, target, current hidden-center point, obstacle count, current-route validity, and straight/horizontal-first/vertical-first candidate validity.
- A hidden relationship junction inside an inflated concept obstacle invalidates the current route. If all hidden-central relationships are reported unchanged, check whether the log still says the current hidden center is inside an obstacle; that should force a new candidate or a skipped route.
- In `samples/obstacle-avoidance-regression.sample.json`, Scenario A is expected to route around `OA_Test_A_Obstacle` with a one-bend hidden-center route; Scenario B is expected to use a hidden-center dogleg route if adequate clearance exists; Scenario C should remain straight or be straightened.

## Arrange as Spider Map

Use `Edit -> Appearance -> Arrange as Spider Map` to place one root concept near the center and arrange connected concepts radially around it.

Selection behavior:

- If selected objects include concept symbols, only those selected concepts are arranged.
- Visible relationships whose endpoints are both in the arranged concept set are used for adjacency and post-layout routing.
- If no concepts are selected, ThinkComposer asks before arranging all visible concepts in the active view.
- The command is disabled when the active view has no visible concept symbols.

Root selection:

- If exactly one concept is selected, that concept is the root.
- If multiple concepts are selected, the root is the selected concept with the highest visible relationship degree.
- Ties are resolved by distance to the selection/view center, then by deterministic name/techName/id order.
- When arranging all visible concepts, the highest-degree visible concept is used as root.

Layout behavior:

- The root keeps its current position when arranging a selection.
- When arranging all visible concepts, the root moves to the current viewport center when available, otherwise to the visible cluster center.
- First-level connected concepts are placed on a ring around the root.
- Remaining concepts are placed on a second ring, grouped near their nearest already placed neighbor when possible.
- The arranged batch is normalized into reachable canvas bounds with an 80px default padding. If preserving the root would put ring nodes above or left of the canvas, ThinkComposer shifts the whole arranged group back into the reachable area before routing links.
- The command auto-fits arranged concept labels first, then moves concepts, then routes in-scope links with the same obstacle-avoidance service.
- Relationship direction is ignored for layout adjacency in this first slice.

Limitations:

- Spider Map is a deterministic two-ring layout, not a full graph optimizer.
- Dense graphs may still need manual cleanup.
- Complements and group regions are not arranged.
- Concepts outside the selected/all-visible scope are not moved.
- The command does not create, delete, or relink model entities.

Troubleshooting:

- If arranged concepts appear off-canvas, inspect the application log for `Spider Map normalize bounds`. The final bounds should have x/y at or above the configured canvas padding.
- The command reveals the arranged bounds with a non-mutating `BringIntoView` call after the undoable layout command completes. If the view is not open or cannot reveal the bounds, the log reports `action=none`.

## Future Appearance Tools

The Appearance group includes disabled placeholders for planned layout tools:

- Arrange as Flowchart
- Arrange as Hierarchy Map
- Arrange as System Map

These commands are intentionally disabled until their layout algorithms are implemented.

## JSON Import Relationship

JSON import now uses the same auto-fit service for concept visuals created or newly placed during import when `importOptions.autoFitPlacedConcepts` is omitted or true. A patch operation can override this with `autoFit: false`, or can force fitting for an updated existing concept with `autoFit: true`.

JSON import also uses the same link-routing service for relationship visuals/connectors created, placed, or repaired during import when `importOptions.autoRoutePlacedLinks` is omitted or true. A patch operation can override this with `autoRoute: false`, or can force routing for an existing visible relationship touched by an update/place operation with `autoRoute: true`. Auto-route runs after auto-fit so obstacle bounds reflect fitted concept widths.

The auto-fit service, link-routing service, and `LayoutSelectionContext` remain UI-independent enough for JSON import layout passes and manual layout tools to share the same measurement, visible-graph, and connector-routing behavior.

## Manual Regression

1. Open an existing composition.
2. Select one concept with a short or long label.
3. Run `Edit -> Appearance -> Fit Concept Width to Text`.
4. Verify the width changes appropriately and connectors update.
5. Undo and redo.
6. Select multiple concept symbols with different label lengths.
7. Run the command again and verify all selected concepts update in one undoable step.
8. Select relationship symbols or complements and verify they are skipped safely.
9. Resize a concept normally and verify drag resizing still works.
10. Double-click a concept's left or right resize handle and verify auto-fit runs without breaking drag resize.
11. Select one connector crossing a concept and run `Edit -> Appearance -> Route Links with Obstacle Avoidance`.
12. Verify it becomes straight or one-bend orthogonal without crossing concept symbols, then undo and redo.
13. Select multiple connectors and route them.
14. Run routing with no selected connectors and verify the all-visible confirmation appears.
15. Verify already valid hand-routed connectors are not changed unnecessarily.
16. Open `Test__Object_Avoidance.tcom`, import `samples/obstacle-avoidance-regression.sample.json`, select Scenario A's visible relationship line, and run the routing command.
17. Verify the log reports one hidden-central relationship route instead of two skipped hidden-endpoint connectors, and that Scenario A becomes L-shaped around the obstacle.
18. Verify Scenario B chooses a horizontal or vertical hidden-center dogleg route, and Scenario C remains straight or is straightened.
19. Save, close, reopen, and verify routed connector intermediate points or hidden junction positions persist.
20. Export PDF and verify routed connectors render.
21. Select one central concept and several connected concepts, then run `Edit -> Appearance -> Arrange as Spider Map`.
22. Verify the selected/root concept remains central, child concepts are placed radially, labels are readable, and links are routed after movement.
23. Undo and redo the Spider Map arrangement.
24. Run Spider Map with no selected concepts and verify the all-visible confirmation appears.
25. Verify no arranged concepts are above or left of the scrollable canvas origin.
26. Save, close, reopen, and verify arranged concept positions persist.
