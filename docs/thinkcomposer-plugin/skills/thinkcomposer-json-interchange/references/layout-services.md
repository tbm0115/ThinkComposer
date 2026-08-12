# Layout Services Architecture

This document summarizes the reusable layout and Relationship-routing services behind `Edit -> Appearance`, Composition JSON import, and headless routing validation.

All visual mutations must run inside an active ThinkComposer edit command/variation. Services that move symbols, resize symbols, create/update visual routes, or change Group Regions should be called from a command such as `Fit Concept Width to Text`, `Route Links with Obstacle Avoidance`, `Arrange as Spider Map`, `Arrange as Hierarchy Map`, `Arrange as Flowchart`, `Arrange as System Map`, or the JSON import command variation. Context builders and validators may inspect the model outside a command, but they must not mutate it.

## Feature Matrix

| Feature | Manual Command | JSON Import Option | Current Status |
|---|---|---|---|
| Auto-place concepts | Import only | `autoPlaceNewItems` | shared import pipeline |
| Auto-fit concepts | Fit Concept Width to Text | `autoFitPlacedConcepts` / operation `autoFit` | shared service |
| Route links | Route Links with Obstacle Avoidance | `autoRoutePlacedLinks` / operation `autoRoute` | shared coordinator |
| Spider Map | Arrange as Spider Map | CLI `--layout spider` validation | shared routing coordinator |
| Hierarchy Map | Arrange as Hierarchy Map | CLI `--layout hierarchy` validation | shared routing coordinator |
| Flowchart | Arrange as Flowchart | CLI `--layout flowchart` validation | shared routing coordinator with lanes |
| System Map | Arrange as System Map | CLI `--layout system` validation | shared routing coordinator |

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

### OrthogonalRoutePlanner

Purpose: pure, dependency-free path planning over endpoint anchors, rectangular obstacles, already accepted routes, and optional mandatory waypoints/corridors. It builds a deterministic bounded orthogonal visibility/Hanan grid and searches directional states with A*. Cost is geometric length plus 40 per bend, 50 per near miss, and 250 per crossing with an accepted route. A clear straight route always wins.

Safety bounds: at most 64 coordinate-producing obstacles, 64 coordinates per axis, 4,096 grid nodes, 12,288 directional states per connector, and 500,000 search-work units per batch. Search uses two fixed envelopes and an outer-perimeter fallback; it never uses elapsed wall time as an abort condition. Automatic results target at most eight route points and never exceed 16.

Result data records intent, dirty reason, old/new points, status, obstacle/work counts, bends, detour ratio, crossings, cap hits, and fallback diagnostics. Fallback order is a safe planned route, safe outer route, collision-free direct route, then a direct degraded route with an explicit warning. Stale geometry is never silently restored.

### RelationshipRoutingCoordinator

Purpose: owns the shared routing pipeline: determine affected scope, place and declutter Relationship hubs, build obstacles, plan in stable-id order, validate/simplify, then apply and render once. It is the only production entry point for automatic Relationship routing.

Used by: Composition JSON import, Route Links, Spider, Hierarchy, Flowchart, System Map, and CLI routing validation. Specialized Flowchart feedback lanes are passed as mandatory waypoints/corridors. Visible Relationship hubs are obstacles consistently, including Spider.

Preservation rules: untouched hand-routed links are unchanged. Moving a symbol or hub invalidates only incident routes. If a complete connected selection moves by one common delta, its route points translate intact. Suspicious, dirty, or generated routes never retain an invalid, distant, or excessively detoured bend.

Hub rules: binary hubs remain inside their endpoint corridor; multi-endpoint hubs use the endpoint centroid/geometric median and route as a star. Binary dogleg behavior is restricted to genuinely simple two-ended Relationships.

### LinkObstacleRoutingService

Purpose: compatibility-facing service that collects visual context and delegates automatic work to `RelationshipRoutingCoordinator`. It no longer owns a separate one-bend algorithm.

JSON import integration: `importOptions.autoRoutePlacedLinks` defaults to true for Relationships/connectors created, placed, repaired, or invalidated by import. Operation `autoRoute` overrides the top-level behavior.

### LayoutBoundsNormalizer

Purpose: translates arranged symbols into reachable canvas coordinates when a layout would otherwise put content above or left of the scrollable origin.

Used by: Spider Map, Hierarchy Map, Flowchart, and System Map.

JSON import integration: informs the same placement philosophy used by `layoutMode: "gridNearViewport"` and future JSON layout modes.

Limitations: normalization translates only the provided arranged scope. It should not move unrelated concepts or links.

### RelationshipNodeDeclutterService

Purpose: moves visible relationship central symbols so relationship bubbles do not overlap each other or concept symbols, while keeping local relationship bubbles close to their endpoint corridor.

Used by: Hierarchy Map, Flowchart, and System Map.

JSON import integration: invoked by the shared routing pipeline before obstacle construction.

Limitations: local overlap resolution only. It is not a full crossing-minimizing graph layout.

### SpiderMapLayoutService

Purpose: chooses a root concept and places connected concepts radially in a simple two-ring spider map.

Used by: `Arrange as Spider Map`.

JSON/CLI integration: layout placement stays specialized; connector routing is shared and can be validated with `--layout spider`.

Limitations: treats relationships as undirected for adjacency and does not optimize dense graphs globally.

### HierarchyMapLayoutService

Purpose: builds a directed visible concept graph, chooses root concepts, assigns BFS levels, places concepts top-down, declutters visible relationship bubbles, normalizes bounds, and routes links.

Used by: `Arrange as Hierarchy Map`.

JSON/CLI integration: layout placement stays specialized; connector routing is shared and can be validated with `--layout hierarchy`.

Limitations: simple BFS hierarchy, not a Sugiyama/crossing-minimization layout. Cyclic and dense graphs may still require manual cleanup.

### FlowchartLayoutService

Purpose: arranges directed process flow left-to-right, separates disconnected components into lanes, classifies feedback/reverse/cross-link relationships, places feedback bubbles in outer lanes, declutters relationship bubbles, normalizes bounds, and routes links.

Used by: `Arrange as Flowchart`.

JSON/CLI integration: layout placement stays specialized; mandatory feedback lanes and connector routing can be validated with `--layout flowchart`.

Limitations: local feedback lane routing only. It does not globally minimize crossings in dense process graphs.

### SystemMapLayoutService

Purpose: detects a system/root concept, classifies internal and external concepts, arranges internal components inside a visible Group Region, places external actors outside the boundary, positions cross-boundary relationship bubbles in side lanes, normalizes bounds, and routes links.

Used by: `Arrange as System Map`.

JSON/CLI integration: layout placement stays specialized; connector routing is shared and can be validated with `--layout system`.

Limitations: classification is heuristic. Group Region creation is visual-only and does not change semantic containment.

## Backlog

- Full graph crossing minimization.
- User-facing option dialogs for layout settings.
- Domain-aware layout rules beyond current heuristics.
