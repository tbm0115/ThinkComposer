# Layout Services Architecture

This document summarizes the reusable v1 layout services behind `Edit -> Appearance` and the JSON import visual cleanup options.

All visual mutations must run inside an active ThinkComposer edit command/variation. Services that move symbols, resize symbols, create/update visual routes, or change Group Regions should be called from a command such as `Fit Concept Width to Text`, `Route Links with Obstacle Avoidance`, `Arrange as Spider Map`, `Arrange as Hierarchy Map`, `Arrange as Flowchart`, `Arrange as System Map`, or the JSON import command variation. Context builders and validators may inspect the model outside a command, but they must not mutate it.

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

## Services

### LayoutSelectionContext

Purpose: captures active composition/view state, selected symbols/connectors, visible concept symbols, visible relationship visuals, visible connectors, and viewport/selection context without mutating the model.

Used by: every Appearance command and JSON import visual cleanup entry points.

JSON import integration: provides a UI-independent bridge so import can call auto-fit and auto-route services for only touched visuals.

Limitations: it is a snapshot helper, not a live graph database. Callers should rebuild it after large visual changes when they need current bounds or connector sets.

### ConceptAutoFitService

Purpose: measures concept title text with WPF text metrics and resizes concept symbols to a conservative width while preserving height.

Used by: `Fit Concept Width to Text`, double-click resize-handle auto-fit, Spider Map, Hierarchy Map, Flowchart, System Map, and JSON import auto-fit.

JSON import integration: `importOptions.autoFitPlacedConcepts` defaults to true for concepts created or newly placed by import. Operation `autoFit` overrides the top-level behavior.

Limitations: fits concept symbol text only. It skips relationships, complements, and symbols whose text/format cannot be measured safely.

### LinkObstacleRoutingService

Purpose: routes visible relationship connectors around concept obstacles using the existing visual model. For ordinary connectors it uses `VisualConnector.IntermediatePosition`. For hidden-central simple relationships it can move the hidden relationship symbol as a junction and use dogleg bends on the two connector segments.

Used by: `Route Links with Obstacle Avoidance`, Spider Map, Hierarchy Map, Flowchart, System Map, and JSON import auto-route.

JSON import integration: `importOptions.autoRoutePlacedLinks` defaults to true for relationships/connectors created, placed, or repaired by import. Operation `autoRoute` overrides the top-level behavior.

Limitations: v1 does not add a new serialized multi-point route model. Non-hidden-central connectors still have at most one intermediate point.

### LayoutBoundsNormalizer

Purpose: translates arranged symbols into reachable canvas coordinates when a layout would otherwise put content above or left of the scrollable origin.

Used by: Spider Map, Hierarchy Map, Flowchart, and System Map.

JSON import integration: informs the same placement philosophy used by `layoutMode: "gridNearViewport"` and future JSON layout modes.

Limitations: normalization translates only the provided arranged scope. It should not move unrelated concepts or links.

### RelationshipNodeDeclutterService

Purpose: moves visible relationship central symbols so relationship bubbles do not overlap each other or concept symbols, while keeping local relationship bubbles close to their endpoint corridor.

Used by: Hierarchy Map, Flowchart, and System Map.

JSON import integration: not directly exposed as an import option yet. It is available for future JSON layout modes.

Limitations: local overlap resolution only. It is not a full crossing-minimizing graph layout.

### SpiderMapLayoutService

Purpose: chooses a root concept and places connected concepts radially in a simple two-ring spider map.

Used by: `Arrange as Spider Map`.

JSON import integration: not yet exposed as a JSON import layout mode.

Limitations: treats relationships as undirected for adjacency and does not optimize dense graphs globally.

### HierarchyMapLayoutService

Purpose: builds a directed visible concept graph, chooses root concepts, assigns BFS levels, places concepts top-down, declutters visible relationship bubbles, normalizes bounds, and routes links.

Used by: `Arrange as Hierarchy Map`.

JSON import integration: not yet exposed as a JSON import layout mode.

Limitations: simple BFS hierarchy, not a Sugiyama/crossing-minimization layout. Cyclic and dense graphs may still require manual cleanup.

### FlowchartLayoutService

Purpose: arranges directed process flow left-to-right, separates disconnected components into lanes, classifies feedback/reverse/cross-link relationships, places feedback bubbles in outer lanes, declutters relationship bubbles, normalizes bounds, and routes links.

Used by: `Arrange as Flowchart`.

JSON import integration: not yet exposed as a JSON import layout mode.

Limitations: local feedback lane routing only. It does not globally minimize crossings in dense process graphs.

### SystemMapLayoutService

Purpose: detects a system/root concept, classifies internal and external concepts, arranges internal components inside a visible Group Region, places external actors outside the boundary, positions cross-boundary relationship bubbles in side lanes, normalizes bounds, and routes links.

Used by: `Arrange as System Map`.

JSON import integration: not yet exposed as a JSON import layout mode.

Limitations: classification is heuristic. Group Region creation is visual-only and does not change semantic containment.

## Backlog

- JSON import modes for Spider, Hierarchy, Flowchart, and System Map.
- Full multi-bend connector route model.
- Full graph crossing minimization.
- User-facing option dialogs for layout settings.
- Domain-aware layout rules beyond v1 heuristics.
