# Appearance and Layout Tools

ThinkComposer includes a v1 set of reusable appearance and layout commands under:

`Edit -> Appearance`

These commands are intended to make imported or hand-authored diagrams readable quickly, then leave room for normal manual polish. They do not change concept or relationship meaning; they move, resize, or route visual representations in the active view.

## Recommended Workflow

1. Apply a Composition JSON patch with `thinkcomposer composition import-json --input <file.tcom> --json <patch.json> --output <updated-file.tcom>`.
2. Let JSON import auto-place, auto-fit, and auto-route new items when those options are enabled.
3. Apply one manual layout command from `Edit -> Appearance` that matches the diagram intent.
4. Manually polish dense or domain-specific areas.
5. Save the native `.tcom` file and export PDF/report when needed.

## Command Summary

| Command | Use When | What It Changes |
|---|---|---|
| Fit Concept Width to Text | Concept labels are clipped, too wide, or recently edited. | Selected concept symbol widths only. |
| Route Links with Obstacle Avoidance | Connector lines cross concept symbols or need cleanup after moving nodes. | In-scope connector intermediate points and hidden relationship junctions. |
| Arrange as Spider Map | One central idea should radiate to related ideas. | In-scope concept positions and in-scope routed links. |
| Arrange as Hierarchy Map | Roots/parents should appear above children/dependencies. | In-scope concept positions, visible relationship bubbles, and in-scope routed links. |
| Arrange as Flowchart | A process should read left-to-right. | In-scope concept positions, feedback/cross-link bubbles, and in-scope routed links. |
| Arrange as System Map | A system boundary should separate internal components from external actors. | In-scope concept positions, visible Group Region boundary, relationship bubbles, and in-scope routed links. |

## Selection, Undo, and Persistence

- If selected concept symbols exist, arrangement commands operate only on the selected concepts and visible relationships among them.
- If no concept symbols are selected, arrangement commands ask before arranging all visible concepts in the active view.
- Link routing operates on selected connectors/relationship visuals; if no routeable link is selected, it asks before routing all visible links.
- Every command runs inside one undoable ThinkComposer command variation. Normal undo/redo should restore positions, widths, connector bends, hidden junctions, and Group Region changes.
- Layout results are native visual model changes. After saving, closing, and reopening the `.tcom`, positions/routes/Group Regions are expected to persist.
- Completion dialogs separate changed/applied counts, unchanged counts, skipped items, notes, layout warnings, and errors. If a command inspects items and leaves them unchanged because they are already valid, that is a successful no-op rather than a warning.
- Detailed diagnostics go to the lower-left application log. Completion dialogs stay intentionally concise.

## Feature Matrix

| Feature | Manual Command | JSON Import Option | Current Status |
|---|---|---|---|
| Auto-place concepts | Import only | `autoPlaceNewItems` | v1 |
| Auto-fit concepts | Fit Concept Width to Text | `autoFitPlacedConcepts` / operation `autoFit` | v1 |
| Route links | Route Links with Obstacle Avoidance | `autoRoutePlacedLinks` / operation `autoRoute` | v1 |
| Spider Map | Arrange as Spider Map | not yet | v1 manual |
| Hierarchy Map | Arrange as Hierarchy Map | not yet | v1 manual |
| Flowchart | Arrange as Flowchart | not yet | v1 manual |
| System Map | Arrange as System Map | not yet | v1 manual |

## Backlog

- Custom domain shape import.
- Full multi-bend general connector route model.
- Full graph crossing minimization.
- Better layout option dialogs.
- JSON import integration for Spider, Hierarchy, Flowchart, and System Map beyond the current auto-place, auto-fit, and auto-route options.

Domain JSON import/export and explicit embedded-domain updates are covered by `docs/domain-json-interchange.md` and `docs/domain-sync.md`; they are separate from these manual Appearance layout commands.

## Command Details

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
- Before routing, the command can reposition visible relationship central symbols with the `EndpointCorridorRelationshipCenters` strategy. This places relationship bubbles near the midpoint/corridor between their visible origin and target concepts instead of leaving imported bubbles in distant global label rows.
- Visible relationship central symbols can be included as obstacles for other connectors, while each connector excludes its own relationship center from its own obstacle set.
- When a hidden-central simple relationship needs more than one elbow, the router can use a dogleg route without adding a new route model: the hidden relationship symbol becomes the hidden junction, and the two connector `IntermediatePosition` values become the source-side and target-side bends.
- Selecting one segment of a hidden-central relationship routes the whole relationship once, not each hidden-endpoint segment separately.
- Existing valid hand-routed connectors are preserved unless a candidate is materially better.
- If no valid straight or one-bend route exists, the connector is left unchanged and a warning is logged.

The command runs as one undoable command variation. Undo/redo should restore connector intermediate points and hidden relationship junction positions. Detailed per-connector and per-relationship diagnostics are written to the lower-left application log; the completion dialog only shows a concise summary.

v1 limitations:

- Only one bend is supported.
- Hidden-central simple relationships can dogleg around same-row or same-column blockers, but this is still limited to one hidden junction plus one bend per connector segment.
- Complements and group regions are not obstacles by default. Relationship central symbols may be obstacles for other links during route cleanup/import routing.
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

## Arrange as Hierarchy Map

Use `Edit -> Appearance -> Arrange as Hierarchy Map` to arrange concepts into top-down levels with root concepts at the top and child/dependent concepts below.

Selection behavior:

- If selected objects include concept symbols, only those selected concepts are arranged.
- Visible relationships whose endpoints are both in the arranged concept set are used for hierarchy edges and post-layout routing.
- If no concepts are selected, ThinkComposer asks before arranging all visible concepts in the active view.
- The command is disabled when the active view has no visible concept symbols.

Root detection:

- Relationships are interpreted as directed from Origin/source roles to Target roles when role data is available.
- Roots are concepts with no incoming in-scope directed edges.
- If exactly one concept is selected and it has outgoing relationships, that concept is preferred as the root for the selected layout.
- If a component has no roots because it is cyclic or rootless, the command chooses a deterministic fallback root by highest out-degree, then total degree, then distance to the selection/view center, then name/techName/id.
- Relationships without clear origin/target role data are used for component membership, but not for parent/child direction unless a fallback is needed.

Layout behavior:

- Level assignment uses BFS from each component's root concepts.
- Root level is 0; children are placed at parent level + 1, using the shallowest level when multiple paths reach the same concept.
- Disconnected components are arranged as separate hierarchy groups and placed side-by-side.
- Within each level, concepts are ordered by parent order where possible, then by deterministic name/techName/id.
- The command auto-fits arranged concept labels first, moves concepts into top-down rows, normalizes the arranged batch into reachable canvas bounds, declutters visible relationship central symbols, routes in-scope links, then reveals the final bounds.
- Visible relationship central symbols are placed near the midpoint between their in-scope origin and target concepts, grouped by unordered visual level band, and staggered horizontally to avoid overlapping each other. Reverse-level and cross-links then participate in a bounded global collision pass so bubbles from neighboring or opposite-direction bands do not hide behind each other. Each bubble has an inflated endpoint corridor around its connected concepts; primary parent-child links and short local links are strongly leashed to that corridor, while longer cross-links may move farther when needed to avoid hiding another bubble. If a staggered symbol would collide with a concept, the pass tries small vertical and perpendicular offsets near the endpoint midpoint.

Cycle handling:

- Cycle and cross edges are logged and do not loop forever.
- Cycle members keep the first safe discovered level unless a shallower path is found.
- Dense cyclic graphs may need manual cleanup after the first-pass layout.

Limitations:

- This is a simple BFS hierarchy, not a full crossing-minimization or Sugiyama layout.
- Relationship central-symbol decluttering is a local overlap-resolution pass with a bounded global bubble-vs-bubble validation pass, not a full crossing-minimizing graph layout.
- Very dense relationship bands may still need manual cleanup if no non-overlapping midpoint-near candidate can be found.
- Endpoint corridor constraints keep local relationship bubbles visually associated with their source/target concepts, but a warning is logged if the command cannot satisfy concept avoidance, bubble separation, and the corridor at the same time.
- Children are row-ordered near parent order, but complex shared-child graphs are not fully optimized.
- Complements and group regions are not arranged.
- The command does not create, delete, or relink model entities.

Troubleshooting:

- If two visible relationship bubbles still overlap after Hierarchy Map, inspect the application log for `Relationship bubble overlap` and `relationship declutter global pass`. Cross-links and reverse-level links should use the same unordered visual band first, then the global pass should either move one bubble or report a remaining overlap warning.
- If a relationship bubble appears too far from its endpoints, inspect the log for `Relationship bubble outside endpoint corridor`. Local parent-child and short same-level links should remain inside their endpoint corridor unless every in-corridor candidate collides with a concept or another relationship bubble.
- If a bubble cannot be moved without colliding with concepts or other bubbles, the command leaves it in the least disruptive position it found and logs a warning rather than looping indefinitely.

## Arrange as Flowchart

Use `Edit -> Appearance -> Arrange as Flowchart` to arrange concepts into a left-to-right process flow.

Selection behavior:

- If selected objects include concept symbols, only those selected concepts are arranged.
- Visible relationships whose endpoints are both in the arranged concept set are used for directed flow edges and post-layout routing.
- If no concepts are selected, ThinkComposer asks before arranging all visible concepts in the active view.
- The command is disabled when the active view has no visible concept symbols.

Start detection:

- Relationships are interpreted as directed from Origin/source roles to Target roles when role data is available.
- Starts are concepts with no incoming in-scope directed edges.
- If exactly one concept is selected and it has outgoing relationships, that concept is preferred as the start for the selected layout.
- If a component has no starts because it is cyclic or rootless, the command chooses a deterministic fallback start by highest out-degree, then total degree, then distance to the selection/view center, then name/techName/id.
- Relationships without clear origin/target role data are used for component/lane membership, but not for flow direction.

Layout behavior:

- Step assignment uses a guarded BFS/topological-like traversal from each component's start concepts.
- Start step is 0; downstream concepts are placed at increasing left-to-right steps.
- When a concept has multiple predecessors, the command favors the later predecessor step when that remains safe, so joins tend to appear after their inputs.
- Disconnected components are arranged as separate horizontal lanes stacked vertically.
- Within each step, concepts are ordered by parent order where possible, then by deterministic name/techName/id.
- The command classifies in-scope directed relationships as primary-forward, branch-forward, same-level, feedback/reverse, long-cross-link, or ambiguous.
- Feedback/reverse and long-cross-link relationships are moved to a dedicated feedback lane outside the main process band before normal relationship-bubble decluttering runs.
- The feedback lane prefers the top of the flow when there is safe canvas room; otherwise it uses a bottom lane and stacks multiple feedback/cross-link bubbles deterministically.
- Feedback connector segments are routed through the relationship central symbol and each connector's existing `IntermediatePosition`, so no new route-point model is introduced.
- The command auto-fits arranged concept labels first, moves concepts into flow steps, normalizes the arranged batch into reachable canvas bounds, places feedback/cross-link bubbles in their outer lane, declutters normal visible relationship central symbols, routes in-scope forward links, validates the result, then reveals the final bounds.

Cycle handling:

- Feedback/cycle edges are logged and do not loop forever or pull upstream concepts to the right indefinitely.
- Cycle members keep the first safe discovered step when a directed path would otherwise feed back into an already placed upstream node.
- Feedback/reverse connector validation logs warnings if a segment still intersects a concept or another relationship bubble.
- Dense cyclic flows may need manual cleanup after the first-pass layout.

Limitations:

- This is a simple process-flow layout, not a full crossing-minimization graph layout.
- Feedback lanes use local outer-lane routing, not a full global edge-routing optimizer.
- Branches are stacked vertically inside each flow step; dense branches may need manual cleanup.
- Complements and group regions are not arranged.
- The command does not create, delete, or relink model entities.

## Arrange as System Map

Use `Edit -> Appearance -> Arrange as System Map` to arrange concepts as a system/context map with a visible Group Region boundary.

Selection behavior:

- If selected objects include concept symbols, only those selected concepts are arranged.
- Visible relationships whose endpoints are both in the arranged concept set are used for classification, relationship-bubble declutter, and post-layout routing.
- If no concepts are selected, ThinkComposer asks before arranging all visible concepts in the active view.
- The command is disabled when the active view has no visible concept symbols.

System/root detection:

- If exactly one concept is selected, it becomes the system/root.
- Otherwise the command prefers the highest-degree candidate, then names/techNames containing terms such as System, Manager, Platform, Application, Service, Hub, Root, or Control, then proximity to the view/selection center, then deterministic name/techName/id order.
- If no obvious system root exists, the highest-degree deterministic fallback is used and logged.

Classification:

- The selected/detected root is placed inside the implicit system boundary.
- Concepts with External, Client, User, Customer, Supplier, Host, Agent, Device, Environment, Endpoint, Source, or Network in their name/techName are classified as external unless they look strongly internal.
- External source/client/user/supplier/source concepts are placed on the left side of the boundary; host/agent/device/environment/endpoint/network concepts are placed on the right.
- Direct root neighbors and leaf concepts connected to internal components are internal.
- Ambiguous concepts default inside the implicit boundary and are logged as ambiguous-internal.

Layout behavior:

- The command auto-fits arranged concept labels first.
- The system/root is placed as a centered system header inside the Group Region.
- Internal and ambiguous concepts are arranged in a compact grid inside the boundary.
- External/environment concepts are stacked outside the boundary to the left or right.
- The arranged batch is normalized into reachable canvas bounds, visible relationship central symbols are decluttered, in-scope links are routed with obstacle avoidance, and the final bounds are revealed.
- System Map creates or updates a visible Group Region complement around the root/internal/ambiguous concepts when possible.
- The Group Region is recomputed from final symbol bounds after movement/normalization, not from preliminary centers.
- The Group Region uses asymmetric padding by default: left/right/top 120 px and bottom 140 px, so lower-row internal concepts and internal relationship bubbles have breathing room.
- After creation/update, the command validates every internal/root/ambiguous concept against the region bounds and expands the region rather than moving concepts if containment is too tight.
- Existing Group Regions are reused in this order: selected Group Region, root-attached Group Region, then an existing Group Region containing most internal concepts.
- A newly created Group Region is attached to the system/root concept, resized to the computed boundary, rendered immediately, and sent behind concepts within the region layer.
- Cross-boundary relationship bubbles are classified as external-to-internal or internal-to-external and are placed in side-specific ingress/egress lanes before link routing.
- Cross-boundary side lanes sit between the external actor cluster and the Group Region boundary when space permits. Candidate positions treat external concepts, internal concepts, and other visible relationship bubbles as hard obstacles, so an ingress bubble such as Package Source -> Catalog should not be placed over User.
- When several cross-boundary bubbles share a side, the command sorts them by external endpoint height and deterministic relationship name/techName/id, then chooses non-overlapping lane slots above/below nearby actors as needed.
- The Group Region is visual-only. It does not change semantic containment, composite ownership, or relationship links.
- The Group Region is not treated as an obstacle for link routing in v1, so links can cross the system boundary.
- Group Region creation/resizing runs inside the same undoable `Arrange as System Map` command variation; undo removes a new region or restores a resized one.

Limitations:

- Classification is heuristic, not a full systems-theory model.
- Group Region labels are not authored directly by System Map v1; the boundary is attached to the root concept instead.
- Cross-boundary relationship bubble placement is local and side-lane based; very dense ingress/egress lanes may still need manual cleanup if every non-overlapping candidate would move a bubble too far from its relationship line.
- Dense system maps may still need manual cleanup.
- The command does not create, delete, or relink model entities.

## JSON Import Relationship

JSON import now uses the same auto-fit service for concept visuals created or newly placed during import when `importOptions.autoFitPlacedConcepts` is omitted or true. A patch operation can override this with `autoFit: false`, or can force fitting for an updated existing concept with `autoFit: true`.

JSON import also uses the same link-routing service for relationship visuals/connectors created, placed, or repaired during import when `importOptions.autoRoutePlacedLinks` is omitted or true. A patch operation can override this with `autoRoute: false`, or can force routing for an existing visible relationship touched by an update/place operation with `autoRoute: true`. Before routing, import can apply relationship-center placement with `importOptions.relationshipVisualPlacementMode` or `visualStrategy.relationshipVisualPlacement`; `auto` preserves centers already near their endpoints, while `endpointCorridor` recomputes centers near the source/target corridor. Auto-route runs after auto-fit and relationship-center correction so obstacle bounds and relationship centers are current.

JSON import can also carry explicit, source-neutral layout metadata such as concept `visual.role`, relationship `layoutRole`, `visual.display`, `includeInArrangement`, `includeInRouting`, `includeInAutoFit`, and top-level `groups[]`. ThinkComposer honors these controls only when they are supplied; it does not infer layout roles or Group Regions from source formats, domains, concept names, or relationship names. The Skill or JSON generator is responsible for translating source-specific intent into those generic primitives.

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
27. Open `Test__Hierarchy_Map.tcom`, import `samples/hierarchy-map-regression.sample.json`, deselect everything, and run `Edit -> Appearance -> Arrange as Hierarchy Map`.
28. Verify the all-visible prompt appears, roots are placed at the top, children and second-level items appear below, disconnected components are separated, and no concepts are off-canvas.
29. Verify the Publish Notes -> Planning relationship bubble does not overlap or hide behind Discovery -> Map Stakeholders, and that the Qualify Need -> Estimate Work bubble remains between or near its endpoint concepts.
30. Verify hierarchy links are routed after movement, then undo and redo.
31. Save, close, reopen, and export PDF/report to confirm the hierarchy layout persists and renders.
32. Open `Test__Flowchart.tcom`, import `samples/flowchart-regression.sample.json`, deselect everything, and run `Edit -> Appearance -> Arrange as Flowchart`.
33. Verify the all-visible prompt appears, starts are placed on the left, downstream steps flow left-to-right, branches are vertically separated, and no concepts are off-canvas.
34. Verify flowchart relationship bubbles remain readable, links route after movement, and feedback/cycle edges are logged without crashing.
35. Verify the Error Feedback relationship from Error Handler to Validate Input is placed in an outer feedback lane and does not overlap the Invalid concept or the Invalid To Error relationship bubble.
36. Undo and redo the Flowchart arrangement.
37. Save, close, reopen, and export PDF/report to confirm the flowchart layout persists and renders.
38. Open `Test__System_Map.tcom`, import `samples/system-map-regression.sample.json`, deselect everything, and run `Edit -> Appearance -> Arrange as System Map`.
39. Verify the all-visible prompt appears, Deployment Manager System is selected as the root/header, internal components are grouped inside the Group Region, and external actors are placed outside the cluster.
40. Verify Web App, Package Catalog, Job Orchestrator, Configuration Manager, and Audit Log are comfortably inside the Group Region, with Web App and Package Catalog no longer touching the bottom border.
41. Verify User and Package Source remain outside left, Host Agent and OT Network Endpoint remain outside right, and relationship bubbles such as Package Source To Catalog, User To Web App, and Web App To Audit Log do not overlap external actors or each other.
42. Verify relationship links cross the Group Region boundary cleanly, no concepts are off-canvas, and the Group Region is behind concepts rather than covering them.
43. Undo and redo the System Map arrangement.
44. If the Group Region was newly created, verify undo removes it and redo recreates it; if it was reused, verify undo restores its prior bounds.
45. Save, close, reopen, and export PDF/report to confirm the system map layout and Group Region persist and render.
