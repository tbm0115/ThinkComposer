# ThinkComposer JSON Interchange

ThinkComposer JSON interchange exports the active `.tcom` composition to an editable text file, then safely merges edited JSON back into the currently open project.

The native `.tcom` package remains the authoritative format. JSON import/export is an interchange workflow only; it does not replace, convert, or alter native persistence.

## Workflow

1. Open a composition in ThinkComposer.
2. Use `Composition > File > Export JSON...`.
3. Edit the `.json` file manually or with GPT assistance.
4. Reopen or keep the original `.tcom` composition active.
5. Use `Composition > File > Import JSON...`.
6. Review the preview summary and confirm the merge.
7. Save the `.tcom` normally when you want to keep the imported changes.

## Format

Every supported file starts with:

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "application": "ThinkComposer"
}
```

`formatVersion` is the interchange schema version. Version `1` supports full-state merge files and patch-operation files. Unknown JSON fields are ignored. The JSON Schema is maintained at `docs/thinkcomposer-json-interchange.schema.json`.

The repository also carries a bundled Codex skill under `docs/thinkcomposer-json-interchange/` plus `docs/thinkcomposer-json-interchange.zip`. Keep that skill bundle synchronized with this document, the schema, and the sample JSON files when the interchange format or layout-related import behavior changes.

Exports include deterministic, pretty-printed DTO data rather than native binary or WPF object graphs. TechSpec fields are exported where supported and imported as plain text only; ThinkComposer does not execute TechSpec text during JSON import. The main top-level sections are:

- `composition`: document identity, name, tech name, summary, TechSpec, version fields, view prefix, active/root view ids, and domain summary.
- `definitions`: domain definition names, tech names, summaries, and TechSpec where available for exported definitions referenced by ideas.
- `ideas`: concepts with stable ids, definition references, editable text, TechSpec, container, markers, details, and child idea ids.
- `relationships`: relationships with stable ids, definition references, editable text, TechSpec, role links, origin/target idea ids, and container.
- `views`: view identity plus safe layout data for visible representations.
- `operations`: optional patch instructions.
- `warnings`: source/export notes for skipped or metadata-only native data. These are preserved as source warnings during import rather than treated as new import failures.

Import always merges into the active `.tcom` composition. Existing entities are matched by `id` first, then by `techName` when no id is supplied. Missing JSON objects are left untouched.

Import and export write diagnostic messages to ThinkComposer's lower-left application log window. The confirmation dialog intentionally stays concise; use the log for parse details, per-operation planning, applied operation results, skipped reasons, warnings, and rollback diagnostics.

Dialogs separate `Source warnings` from `Import warnings`. Source warnings are preserved notes from the JSON file, such as fixture notes or export limitations. Import warnings are generated while planning or applying the current operation. `Skipped` means an operation was intentionally not applied and the log contains the reason. `Errors` mean an actual failure occurred.

## Full-State Merge Example

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "exportedAtUtc": "2026-05-28T00:00:00Z",
  "application": "ThinkComposer",
  "composition": {
    "id": "00000000-0000-0000-0000-000000000001",
    "name": "Research Map",
    "techName": "ResearchMap",
    "summary": "GPT-edited project summary",
    "viewsPrefix": "View"
  },
  "ideas": [
    {
      "id": "00000000-0000-0000-0000-000000000010",
      "kind": "Concept",
      "definitionTechName": "Concept",
      "name": "Renamed concept",
      "techName": "RenamedConcept",
      "summary": "Editable concept summary",
      "markers": [
        {
          "definitionTechName": "Priority_1",
          "descriptorName": "Review"
        }
      ]
    }
  ],
  "relationships": [],
  "views": [],
  "operations": [],
  "warnings": []
}
```

Full-state import updates matching objects by `id` first. If `id` is absent, it matches by `techName`. Omitted objects are never deleted.

By default, full-state `ideas[]`, `relationships[]`, and `views[]` are interpreted as merge/update data for objects that already exist in the active `.tcom`. This prevents an exported snapshot from accidentally recreating another composition inside the current one.

For GPT-generated full-state-style documents intended to populate a blank composition, opt in explicitly:

```json
{
  "importOptions": {
    "useActiveCompositionAsContainer": true,
    "treatMissingFullStateItemsAsCreates": true
  }
}
```

With this option, missing top-level concepts and relationships can be created when they include enough safe create data: definition, name or techName, container, and valid relationship endpoints. Matching existing objects are still updated/upserted rather than duplicated. A single full-state item can also opt in with `isNew: true`.

Visuals in `views[].visuals[]` that refer to newly created or planned ideas are treated as placement requests. The importer uses the visual `x`, `y`, `width`, and `height` when supplied; otherwise it falls back to normal auto-placement. If a referenced idea/relationship was skipped, dependent visual skips are grouped in the log instead of flooding the dialog.

Patch-style `operations` remain preferred for GPT-authored creation because they make intent, ordering, and safety easier to inspect.

## Large Import Visual Strategy

Large generated models should not hand-place, auto-fit, and auto-route every concept and relationship by default. Use top-level `visualStrategy` to describe how much visual materialization is intended:

```json
{
  "visualStrategy": {
    "mode": "overviewAndModel",
    "largeModelThresholds": {
      "concepts": 300,
      "relationships": 300,
      "visuals": 600
    },
    "fullModelVisuals": false,
    "overviewView": true,
    "overviewViewTechName": "Overview_View",
    "maxOverviewConcepts": 150,
    "maxOverviewRelationships": 200,
    "groupBy": [
      "Contains_Components",
      "Contains_Data_Item"
    ],
    "deferRouting": true,
    "deferAutoFit": true,
    "deferViewRefresh": true,
    "relationshipVisualPlacement": "endpointCorridor"
  }
}
```

Supported strategy modes:

- `modelOnly` creates semantic concepts, relationships, details, markers, and TechSpec while suppressing visual placement. This is the safest mode for output-template generation and very large model imports.
- `overviewAndModel` creates the full semantic model and materializes only a capped overview when `fullModelVisuals=false`. The importer prefers `overviewViewTechName` if that view already exists, otherwise it safely falls back to the active/root view. Creating new views from JSON is still deferred.
- `optimizedFullVisual` allows full visual placement but defaults `deferAutoFit`, `deferRouting`, and `deferViewRefresh` to true so expensive UI work can be run manually later.
- `exactFullVisual` preserves the previous exact-placement behavior and is appropriate for small or explicitly requested diagrams.
- `auto` chooses `overviewAndModel` when document counts meet the configured thresholds; otherwise it behaves like `exactFullVisual`.

When strategy deferral is active, the preview/apply dialog reports `Visual strategy`, `Visuals suppressed by strategy`, `Auto-fit deferred by strategy`, `Auto-route deferred by strategy`, and whether view refresh was deferred. Deferral is intentional and should be treated as a note, not an import failure. Manual Appearance commands can still be run after import when the user is ready to create or refine diagrams.

## Relationship Center Placement And Routing

ThinkComposer relationships can have visible central symbols, so a visible relationship often routes as `source concept -> relationship center -> target concept`. If generated JSON places all relationship centers in a global row or label band, connector lines are pulled through those distant centers and can sweep across the diagram.

For generated diagrams, prefer concept placement plus endpoint-corridor relationship placement:

```json
{
  "importOptions": {
    "relationshipVisualPlacementMode": "endpointCorridor",
    "recomputeSuspiciousRelationshipVisuals": true,
    "maxRelationshipCenterDisplacement": 250,
    "relationshipCenterObstaclePadding": 16,
    "relationshipCenterOverlapPadding": 8
  }
}
```

Modes:

- `auto` preserves relationship centers already near their endpoint corridor and recomputes suspicious centers, such as imported labels far from their source/target concepts.
- `endpointCorridor` recomputes visible relationship centers near the midpoint of their visible origin/target concepts, rejects candidates that overlap concepts, scores relationship-bubble overlaps, and runs before auto-route.
- `midpoint` uses the endpoint midpoint as the primary placement intent.
- `explicit` preserves supplied relationship visual coordinates. Use this only for hand-curated diagrams.
- `hideGeneric` is reserved for safe future hiding/minimizing of generic relationship centers; v1 keeps visible centers and places them near endpoints.
- `defer` skips relationship visual placement correction.

`visualStrategy.relationshipVisualPlacement` provides the same intent at the large-import strategy level, while `importOptions.relationshipVisualPlacementMode` overrides it. For large or uncertain imports, do not emit exact relationship visual `x/y` coordinates unless they are intentionally curated and close to the endpoint corridor.

Regression sample: `samples/composition-relationship-center-placement.sample.json` intentionally imports relationship centers in a top/global label band. With `relationshipVisualPlacementMode: "auto"`, the final import summary should report suspicious centers and recomputed relationship centers before routing.

## Intent-Agnostic Import Primitives

ThinkComposer JSON import is intentionally source-neutral. The importer does not infer that a source-format subgraph is a Group Region, that a relationship named "contains" is membership, that a particular domain should be model-only, or that a concept name implies layout behavior. Those choices belong in the Skill or JSON generator.

Use explicit metadata when the source intent matters:

```json
{
  "groups": [
    {
      "name": "Subsystem A",
      "techName": "Subsystem_A_Group",
      "memberTechNames": ["A1", "A2", "A3"],
      "headerConceptTechName": "Subsystem_A",
      "createGroupRegion": true,
      "padding": 80,
      "sendToBack": true
    }
  ],
  "operations": [
    {
      "op": "create",
      "entity": "relationship",
      "definitionTechName": "Relationship",
      "containerTechName": "Active_Composition_Root",
      "layoutRole": "Membership",
      "visual": {
        "display": "hidden",
        "includeInArrangement": false,
        "includeInRouting": false
      }
    }
  ]
}
```

Generic concept visual roles are `Normal`, `GroupHeader`, `GroupRegionAnchor`, `Summary`, `Annotation`, `Diagnostic`, `Hidden`, and `Deferred`. `Hidden`/`Deferred` or `includeInView:false` creates semantic concepts while suppressing visual placement for this import.

Generic relationship layout roles are `Normal`, `Membership`, `Dependency`, `DataFlow`, `ControlFlow`, `SequenceFlow`, `Feedback`, `CrossLink`, `Annotation`, `Diagnostic`, and `Unknown`. Relationship visual display values are `visible`, `hidden`, `deferred`, `labelOnly`, and `diagnostic`. Per-relationship `visual.relationshipCenterPlacement` can override the import-wide center placement mode with `explicit`, `midpoint`, `endpointCorridor`, `auto`, `hideGeneric`, or `defer`.

`groups[]` creates or updates a visible Group Region complement only when `createGroupRegion:true` is supplied. The importer uses the listed visible member concepts and optional header concept; it does not infer groups from source names, relationship names, or domains.

Regression samples:

- `samples/composition-intent-agnostic-groups.sample.json` demonstrates explicit `groups[]`, a hidden membership relationship, and a visible dependency relationship.
- `samples/composition-intent-agnostic-visual-controls.sample.json` demonstrates summary/deferred concepts, diagnostic relationship metadata, and endpoint-corridor relationship-center placement.

## Patch Operations Example

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "importOptions": {
    "autoPlaceNewItems": true,
    "autoFitPlacedConcepts": true,
    "autoRoutePlacedLinks": true,
    "useActiveCompositionAsContainer": false,
    "layoutMode": "gridNearViewport",
    "preventSelfRecursiveCompositeViews": true,
    "repairRecursiveVisuals": true
  },
  "operations": [
    {
      "op": "update",
      "entity": "concept",
      "id": "existing-concept-guid",
      "set": {
        "name": "Renamed concept",
        "summary": "New GPT-edited summary",
        "techSpec": "api: /api/packages/import\nstorage: PackageStorageRoot\npersistence: PostgreSQL"
      }
    },
    {
      "op": "create",
      "entity": "concept",
      "definitionTechName": "Concept",
      "containerId": "root-composition-guid",
      "viewTechName": "Main",
      "x": 100.0,
      "y": 200.0,
      "width": 180.0,
      "height": 80.0,
      "set": {
        "name": "New concept from GPT",
        "techName": "NewConceptFromGpt",
        "summary": "Created via JSON import"
      }
    },
    {
      "op": "create",
      "entity": "relationship",
      "definitionTechName": "Relationship",
      "containerTechName": "root-composition-tech-name",
      "set": {
        "name": "New relationship from GPT",
        "techName": "NewRelationshipFromGpt",
        "summary": "Created via JSON import",
        "links": [
          {
            "roleType": "origin",
            "ideaTechName": "ExistingSourceConcept"
          },
          {
            "roleType": "target",
            "ideaTechName": "NewConceptFromGpt"
          }
        ]
      }
    },
    {
      "op": "place",
      "entity": "relationship",
      "techName": "NewRelationshipFromGpt",
      "viewTechName": "Main"
    }
  ]
}
```

Supported operations are `update`, `create`, `delete`, and `place` for the entity types where the native model can safely apply them.

`set.techSpec` is supported for compositions, concepts, relationships, and exported definition summaries where the target native object exposes TechSpec. Omission preserves the current value. An explicit empty string clears TechSpec. The importer logs applied, unchanged, and skipped TechSpec field updates.

Patch semantics:

- `update` operations must match an existing object by `id` or top-level `techName`; unmatched updates are skipped with warnings.
- `create` operations require enough safe native information. Concepts require `definitionTechName` plus `containerId` or `containerTechName`. Relationships require a relationship definition, container, and valid origin/target links.
- `place` operations create or update a visual representation for an existing concept or relationship in a target view.
- `delete` only runs for explicit delete operations or `delete: true`; omission never deletes native objects.
- Existing native data that is not represented in JSON is preserved.

Relationship connectivity can be supplied at the operation top level or inside `set`. Top-level values are preferred when both are present, but `set.originIdeaIds`, `set.originIdeaTechNames`, `set.targetIdeaIds`, `set.targetIdeaTechNames`, and `set.links` are accepted for GPT-authored patches. Link `roleType` values are normalized case-insensitively, so `Origin`, `Target`, `origin`, and `target` are accepted.

Each entry in `links[]` can also preserve link-level labels/descriptors:

- `roleVariantTechName` / `roleVariantName` selects an allowed link-role variant when the relationship role exposes one.
- `descriptorName`, `descriptorTechName`, and `descriptorSummary` preserve the optional link descriptor shown on the connector between the relationship and that endpoint concept. These are different from the relationship's own `name`, `techName`, `summary`, and `techSpec`.

On import, an existing matching relationship link is updated with explicit descriptor and role-variant metadata instead of creating a duplicate link.

Relationship create operations are upserts. If a `create relationship` operation matches an existing relationship by `id` or `techName`, the importer updates editable fields and repairs missing role links instead of creating a duplicate. Re-importing the same patch should not duplicate relationships, links, visuals, or connectors.

Linkless relationships are skipped by default. This prevents invalid native relationship objects that can later break view or context-menu code expecting origin/target roles.

### Relationship Definition Compatibility

Relationship definitions may restrict which concept definitions can be linked through each role. A relationship patch can resolve its container, definition, endpoint ideas, and role names and still be skipped if the endpoint concept definitions are not valid for the requested relationship definition.

For example, domain-specific relationship definitions such as `Subject_Verb`, `MUST_be`, or `Targets_Device_Component` may require endpoint concepts with particular definitions. A generated link like `Use-Case -> Use-Case` can be invalid for `Subject_Verb` even though both concepts exist and the role names resolve.

When native validation rejects a pair, the importer logs a compatibility diagnostic with:

- relationship name/techName and requested definition
- resolved origin and target endpoint ids/techNames
- each endpoint concept definition
- resolved origin and target role names/types
- allowed role-side endpoint definitions when the domain exposes them
- the native `CanLink` rejection message

The preview/final summary also includes `Relationships skipped by compatibility`, and the log groups those skips by relationship definition.

For domain-accurate imports, fix the JSON so every relationship definition connects compatible endpoint concept definitions. Recommended workflow:

1. Import/update the Domain JSON or embedded domain first.
2. Export or inspect the Domain JSON relationship definitions and role constraints.
3. Generate the `.tcom` patch using relationship definitions that accept the endpoint concept definitions.
4. Use fallback only for draft graph preservation, not final domain-accurate semantics.

### Version and Domain Compatibility Metadata

Composition JSON exports include optional `targetContext` metadata for the composition and its embedded domain. GPT-generated patches may also include a `requires` block:

```json
{
  "requires": {
    "domain": {
      "techName": "MTConnect",
      "versionSequence": 390,
      "compatibilitySignature": "..."
    },
    "composition": {
      "techName": "MTConnect_Machine_Monitoring_Utilization_Productivity_Use_Case",
      "versionSequence": 4
    }
  }
}
```

Import compares this metadata to the active `.tcom` according to these options:

- `domainCompatibilityPolicy`: `ignore`, `warn` (default), `requireTechName`, `requireId`, `requireVersion`, or `requireSignature`.
- `compositionVersionPolicy`: `ignore`, `warn` (default), `requireTechName`, `requireId`, or `requireVersion`.
- `strictRelationshipCompatibility`: preflight relationship creates against native relationship-definition endpoint compatibility.
- `abortOnRelationshipCompatibilityFailure`: with strict relationship compatibility, block the import before apply if any relationship would be skipped by compatibility.
- `strictDetailsCompatibility` and `abortOnDetailCompatibilityFailure`: block strict imports when details cannot be applied to native detail designators.

Version and signature checks are stale-contract guards. They do not replace native semantic validation; relationship endpoints still must satisfy the active domain's relationship definitions. For strict domain-correct GPT patches, use:

```json
{
  "importOptions": {
    "domainCompatibilityPolicy": "requireTechName",
    "compositionVersionPolicy": "warn",
    "strictRelationshipCompatibility": true,
    "abortOnRelationshipCompatibilityFailure": true
  }
}
```

When strict relationship compatibility blocks an import, no command variation is opened and no concepts or relationships are applied. The lower-left log includes a copyable block beginning with `BEGIN THINKCOMPOSER RELATIONSHIP COMPATIBILITY REPORT`; use that report to regenerate domain-valid relationship definitions, endpoint concept definitions, or roles.

The MTConnect machine-monitoring sample is a useful example: the patch can create concepts mechanically, but some relationships using definitions such as `Subject_Verb`, `MUST_be`, and `Targets_Device_Component` are rejected by native endpoint compatibility. Strict mode should block that partial import before apply so the JSON can be regenerated from the Domain JSON compatibility metadata.

Draft imports can opt into a generic fallback:

```json
{
  "importOptions": {
    "relationshipDefinitionFallbackTechName": "Relationship"
  }
}
```

If a relationship create fails endpoint compatibility and fallback is configured, the importer retries the endpoint pair with the fallback definition. This is disabled by default. A single operation can override with `"fallbackDefinitionTechName": "Relationship"` or block fallback with `"strictDefinition": true`. Fallback is only attempted after the requested relationship definition exists and the endpoint/role references resolve; it is not used for missing definitions or missing endpoints.

## Container Matching

Create operations normally resolve `containerId` first, then `containerTechName`. If both are omitted, the importer uses the active root composition as the container. If a named container is not found, the create operation is skipped so imports do not accidentally place items in the wrong nested concept.

Regression fixtures and GPT-generated root-level samples can opt into:

```json
{
  "importOptions": {
    "useActiveCompositionAsContainer": true
  }
}
```

When this option is true, root-level create operations with missing, placeholder-like, or unresolved test-composition container references such as `Test__Flowchart` can fall back to the active composition root. The importer logs the fallback:

`JSON import container fallback: requested='Test__Flowchart' not found; using active composition 'Composition1'.`

The canonical GPT sentinel for this workflow is:

```json
{
  "containerTechName": "Active_Composition_Root"
}
```

Use it with `importOptions.useActiveCompositionAsContainer: true` when a patch is intentionally authored for whichever `.tcom` is currently active. Accepted active-root placeholder variants include `ACTIVE_COMPOSITION_ROOT`, `activeCompositionRoot`, `active-composition-root`, `active_composition_root`, `__ACTIVE_COMPOSITION_ROOT__`, `Current_Composition`, `CurrentComposition`, `Active_Composition`, `Composition_Root`, and `Root_Composition`. If one of these sentinels is used while the option is false, the operation is skipped with a precise message instead of being treated as a normal missing techName.

This option is intended for fixtures and patches targeting the active root composition. It is not a nested-container repair tool; if a patch names a real existing container, that container is used, and nested imports should still specify the intended nested concept container explicitly. If many operations skip for the same missing container, the dialog/log adds a note explaining which container was missing and how to use the fallback.

## Visual Placement

Creating a concept or relationship in the composition model does not by itself guarantee that it appears in a diagram. A diagram shows visual representations inside a `View`, so GPT-authored patches should either supply explicit placement fields or rely on auto-placement.

Explicit concept placement uses top-left coordinates:

```json
{
  "op": "place",
  "entity": "concept",
  "techName": "Concept_TechName",
  "viewTechName": "Main",
  "x": 100.0,
  "y": 200.0,
  "width": 180.0,
  "height": 80.0
}
```

Relationship placement can omit coordinates. When endpoints are visible in the target view, the importer places the relationship symbol near the midpoint and creates connectors for visible linked endpoints:

```json
{
  "op": "place",
  "entity": "relationship",
  "techName": "Relationship_TechName",
  "viewTechName": "Main"
}
```

Create operations can include the same `viewId`, `viewTechName`, `x`, `y`, `width`, and `height` fields. If no view is supplied for a root-level create/place operation, the importer uses the active view when safe, then the composition root view. For nested containers, it prefers the target container's composite view. `Active_View`, `Main_View`, and `Active_Composition_Root_View` are accepted view sentinels when the exact exported view techName is unknown. If no coordinates are supplied and `importOptions.autoPlaceNewItems` is omitted or true, the importer places new concepts in a deterministic grid near the current viewport or main content cluster. Extreme outlier symbols are ignored when choosing the default layout origin, so imported items should not be pushed to coordinates such as `x=9000, y=5000` unless the patch explicitly requests those coordinates. Relationships are connected when their linked endpoint concepts are visible in the chosen view.

If a relationship target view is missing one or both endpoint concept symbols and auto-placement is enabled, the importer attempts to place those endpoint concepts in that view before placing the relationship. If endpoints still cannot be resolved or placed, the relationship connector is skipped with a warning listing the missing endpoints.

Imported visuals may be placed in nested or composite views such as a concept's own view, not necessarily the root view currently visible in the workspace. After import, the completion dialog and application log list affected views, and the importer attempts to open, fit, and select imported visuals in the first affected view when safe.

The importer will not place a concept inside its own composite view. It also will not place a relationship visual in a composite view when one endpoint is the owner of that same composite view, because showing the owner's nested content as details would recursively render the view inside itself. In that case the relationship model links are still created or repaired, but the unsafe connector visual is skipped with a warning. Re-import can repair older bad imports by removing only self-recursive visual representations while preserving the underlying concepts and relationships.

Set `"autoPlace": false` on a single create operation, or `"importOptions": { "autoPlaceNewItems": false }` at the top level, to create model objects without automatic visual placement.

Concept visuals created or newly placed during import are auto-fitted to their visible text by default. This uses the same `ConceptAutoFitService` as `Edit -> Appearance -> Fit Concept Width to Text`, so connector refresh and undo/redo behavior match the manual command. Existing concepts that are only text-updated are not resized unless their operation explicitly includes `"autoFit": true`. Use `"autoFit": false` on a create or place operation to keep the supplied/default width for that operation.

Relationship visuals created, placed, or repaired during import are auto-routed by default after concept auto-fit completes. This uses the same `LinkObstacleRoutingService` as `Edit -> Appearance -> Route Links with Obstacle Avoidance`, including hidden-central simple relationship routing and dogleg fallback. Existing unrelated links in the view are not routed. Use `"autoRoute": false` on an operation to preserve that operation's current connector geometry, or `"autoRoute": true` to route an existing visible relationship touched by an update/place operation.

Details can be supplied at `operation.details` or `operation.set.details`. The importer merges both forms deterministically, with operation-level details taking precedence for duplicate designators. GPT-authored details can use `name`/`techName` or native `designatorName`/`designatorTechName`, and table rows can be supplied as `rows` or `records`.

Native details require matching detail designators on the target idea definition. If the designator is missing or the detail shape is not implemented, the idea itself is still created/updated and the detail is reported separately. By default unsupported details are skipped:

```json
{
  "importOptions": {
    "detailFallbackMode": "skip"
  }
}
```

For draft imports where preserving generated detail text matters more than native detail fidelity, set `detailFallbackMode` to `appendToTechSpec` or `appendToDescription`. The importer appends a clearly delimited text section containing the detail name, techName, kind, reason, text, and table rows. Prefer putting critical generated content directly in `summary`, `description`, or `techSpec` when the target domain does not define matching detail designators.

Layout options:

- `gridNearViewport` is the default. It places the batch near the visible center when the view is open, otherwise near the normal content cluster or `100,100` for an empty/outlier-only view.
- `gridNearContainer` keeps the batch near the existing cluster for the target composite/container view.
- `gridAfterExistingContent` uses the older behavior of placing the batch after existing non-outlier content.
- `none` disables automatic placement unless an operation supplies explicit placement fields.
- `autoFitPlacedConcepts` defaults to true. It fits newly created or newly placed concept symbols to text during import without resizing every existing concept in the view.
- `autoRoutePlacedLinks` defaults to true. It routes newly created, newly placed, or repaired relationship visuals/connectors during import without routing every existing connector in the view.
- `useActiveCompositionAsContainer` defaults to false. It is an opt-in convenience for root-level fixture imports into a fresh active composition.
- `treatMissingFullStateItemsAsCreates` defaults to false. It is an opt-in for full-state-style GPT documents that should create missing top-level `ideas[]` and `relationships[]` in the active composition. Patch operations remain preferred.
- `visualStrategy` is top-level metadata, not an `importOptions` field. Use it for large imports that should be model-only, overview-only, optimized/deferred, or exact full visual.
- `relationshipVisualPlacementMode` defaults to `auto`. Use `endpointCorridor` for generated diagrams where relationship centers should be recomputed near the concepts they connect; use `explicit` only for curated coordinates.
- `relationshipDefinitionFallbackTechName` defaults to disabled. It can preserve draft graph structure by retrying compatibility-failed relationship creates with a generic relationship definition.
- `detailFallbackMode` defaults to `skip`. `appendToTechSpec` and `appendToDescription` preserve unsupported details as delimited text on the idea.
- `domainCompatibilityPolicy` defaults to `warn`. Use `requireTechName`, `requireId`, `requireVersion`, or `requireSignature` when a patch must target a specific embedded domain contract.
- `compositionVersionPolicy` defaults to `warn`. Use a require policy only when a patch must target a specific composition identity or version.
- `strictRelationshipCompatibility` plus `abortOnRelationshipCompatibilityFailure` blocks domain-invalid relationship creates before apply.
- `strictDetailsCompatibility` plus `abortOnDetailCompatibilityFailure` blocks imports when GPT-authored details cannot be applied to native detail designators.
- `preventSelfRecursiveCompositeViews` defaults to true and blocks self-recursive owner-in-own-view placements.
- `repairRecursiveVisuals` defaults to true and removes previously imported self-recursive visuals during JSON import/re-import.

Manual Appearance layout commands are currently separate from JSON import layout modes. `Edit -> Appearance -> Arrange as Spider Map`, `Arrange as Hierarchy Map`, `Arrange as Flowchart`, and `Arrange as System Map` are v1 manual commands; they are not automatic JSON import `layoutMode` values yet. The current JSON import integration is limited to auto-placement, concept auto-fit, and link auto-route.

GPT prompt example:

```text
Edit this ThinkComposer JSON using patch operations only. Update existing summaries by id or techName. For root-level GPT patches targeting the active composition, set importOptions.useActiveCompositionAsContainer=true and use containerTechName Active_Composition_Root. For each new concept or relationship, include definitionTechName and containerTechName. For relationships, include origin/target links, preferably as set.links with roleType and ideaId or ideaTechName. Prefer explicit viewTechName when known; otherwise active view fallback can place root-level creates. For small diagrams, include x/y/width/height only when deliberate placement matters and leave importOptions.autoFitPlacedConcepts/autoRoutePlacedLinks true. For large model imports, prefer top-level visualStrategy mode modelOnly or overviewAndModel with deferAutoFit, deferRouting, and deferViewRefresh true instead of hand-placing/routing every item. Do not delete anything unless I explicitly request it.
```

Additional sample files are available at `samples/json-interchange-patch.sample.json`, `samples/json-interchange-regression.sample.json`, `samples/composition-active-root-fallback.sample.json`, `samples/composition-relationship-fallback.sample.json`, `samples/composition-strict-domain-compatibility.sample.json`, `samples/composition-full-state-create.sample.json`, `samples/composition-large-visual-strategy.sample.json`, `samples/composition-relationship-center-placement.sample.json`, `samples/composition-intent-agnostic-groups.sample.json`, and `samples/composition-intent-agnostic-visual-controls.sample.json`. The active-root fallback sample is the smallest fixture for verifying that `Active_Composition_Root` imports into a fresh active composition and active/root view without editing every `containerTechName`. The relationship fallback sample demonstrates explicit draft fallback and detail fallback behavior when the target domain supports the referenced definitions. The strict compatibility sample demonstrates `requires.domain`, strict relationship preflight, and abort-before-apply behavior. The full-state-create sample demonstrates explicit opt-in creation from top-level `ideas[]`, `relationships[]`, and `views[]`. The large visual strategy sample demonstrates a model import that suppresses or caps visual work so semantic data can import without forcing immediate full-diagram rendering. The intent-agnostic samples demonstrate explicit generic visual/group controls without source-specific importer behavior.

## Domain Interchange

Domain JSON Interchange is separate from Composition JSON Interchange. Use `format: "ThinkComposer.DomainJsonInterchange"` for `.tdom` export/import or for updating an active composition's embedded domain snapshot.

Related docs:

- `docs/domain-json-interchange.md`
- `docs/domain-sync.md`
- `docs/output-template-generation.md`
- `docs/thinkcomposer-domain-json-interchange.schema.json`

Use `Composition > Domain > Update Embedded Domain...` when an existing `.tcom` should pick up safe additions or updates from a newer `.tdom` or Domain JSON file. This command updates the embedded domain snapshot explicitly; it does not create a live sync link and does not delete legacy embedded-domain objects by omission.

When an embedded Domain update adds or changes output templates, ThinkComposer still treats template bodies as text during import. Composition output generation prepares the imported definition-level templates before rendering, so users should not need to open Concept/Relationship Definition Output-Templates tabs after the update.

`Tools -> Output -> Generation Preview` and `Generate Files...` now log template resolution details for imported templates, including owner scope, owner techName, selected external language, template hash, inferred role, subtemplate registry entries, and XML/JSON validation results. Use `docs/output-template-generation.md` when troubleshooting whether output came from an imported embedded-domain template, a definition-level template, a base/fallback template, or older native template text.

## Manual Regression

1. Open `MTConnect_Endless_Forge_and_LOTAR.tcom` or another composition with the same exported structure.
2. Use `Composition > File > Import JSON...`.
3. Select `deployment_manager_thinkcomposer_patch.json`, or use `samples/json-interchange-regression.sample.json` after replacing placeholder ids/tech names with values from your composition.
4. Confirm the import.
5. Verify the lower-left log contains parse, planning, per-operation, and final summary lines.
6. Confirm the final dialog lists affected views when visuals were placed.
7. Verify new concepts appear in the requested view or in the auto-placement area without toggling Show/Hide Details.
8. Verify imported concepts may appear in nested composite views, not only the root view.
9. Verify relationships have origin/target links and are visible when both endpoints are visible in the same view.
10. Find `Deployment Manager Web App`, then right-click and toggle Display/Hide Composite-Content as Detail.
11. Verify no `StackOverflowException` occurs; nested content either appears safely or an import warning explains why it cannot be shown.
12. Re-import the same patch and verify it does not duplicate concepts, relationships, links, visual representations, connectors, or recursive self-visuals.
13. Right-click imported concepts and relationships/links and verify no runtime exception appears.
14. Save, close, reopen, and verify imported visuals and relationship links persist.
15. Repeat the nested-content toggle after reopen.
16. Verify source warnings and import warnings are shown separately and object-valued warnings are readable.
17. Verify no `Put-visual must be applied within a Command` error appears.
18. If an error occurs, inspect the log for the full exception, current operation, and rollback/undo result.
19. Import `samples/composition-active-root-fallback.sample.json` into a fresh blank composition and confirm the preview/apply summary creates two concepts and one relationship with zero skipped operations.
20. For generated MTConnect patches such as `machine_monitoring_utilization_productivity_composition.json`, import/update the required MTConnect Domain JSON first, then import the composition patch. Expected result after active-root fallback is fixed: the preview plans 20 concept creates and 34 relationship creates, missing-container skips are zero, auto-fit and auto-route run for newly placed items, and save/reopen preserves the created ideas and relationships.
21. Import `samples/composition-strict-domain-compatibility.sample.json` into an All-Purpose composition and confirm strict relationship compatibility passes with zero compatibility skips. Change `requires.domain.techName` to a non-active domain and confirm the preview blocks before apply.
22. Add strict options to a known partially compatible generated patch. Expected result: preview reports compatibility failures and apply is blocked before creating concepts.
23. Import `samples/composition-full-state-create.sample.json` into a blank All-Purpose composition. Expected result: two concepts created, one relationship created, visuals placed, skipped zero.
24. Import a full-state-style generated file without `treatMissingFullStateItemsAsCreates`. Expected result: the dialog/log notes that missing full-state ids were treated as updates and explains how to enable full-state-create mode.

## Troubleshooting

Detailed import diagnostics are in the lower-left application log window. The modal confirmation and completion dialogs only summarize planned or applied counts.

Before preview, the importer logs a preflight block with active composition/domain/view context, import options, required concept and relationship definitions, referenced containers/views/endpoints, planned ids/techNames, and unresolved references. Common skip causes are:

- Missing container: use an exact container id/techName, or use `Active_Composition_Root` with `useActiveCompositionAsContainer=true` for root-level GPT patches.
- Missing concept definition or relationship definition: import/update the required Domain JSON first, or use a definition techName that exists in the active embedded domain.
- Unresolved relationship endpoint: create the endpoint earlier in the patch, use the correct endpoint id/techName, or repair the active composition before importing.
- Unresolved role: use `roleType` of `Origin`/`Target` or a roleDefinitionTechName present on the selected relationship definition.
- Relationship compatibility failure: the endpoint concepts exist, but their concept definitions are not valid for the requested relationship definition/roles. Fix the relationship definition/endpoints for domain-accurate imports, or opt into `relationshipDefinitionFallbackTechName` for drafts.
- Unsupported detail shape: the concept/relationship is still created when possible; the detail is skipped with name/techName/kind diagnostics, or appended to TechSpec/description when `detailFallbackMode` requests it.

`Put-visual must be applied within a Command` means WPF visual refresh or placement was attempted while no ThinkComposer edit command variation was active. JSON import refreshes affected views inside the single `Import JSON` command variation so view updates remain undoable and command-safe. If this error appears again, the log should show the operation being applied and whether rollback completed.

If imported concepts appear in the tree but not on the canvas, check the log for affected-view and visual-placement lines. They may have been placed in a nested/composite view rather than the active root view. The importer materializes view children for placed visuals and attempts to open and fit the first affected view, but unopened views still render only when the workspace opens them.

If imported content appears far away, check the `JSON import layout` log lines. The default `gridNearViewport` mode ignores extreme outlier visuals and starts empty/outlier-only views near `100,100`. Explicit `x`/`y` values in the patch are still honored.

If relationships import but connectors are missing, check whether the relationship has resolved origin/target links and whether both endpoint symbols are visible in the same target view. The importer logs the relationship link source (`top-level`, `set`, or `none`), resolved endpoints, missing endpoint symbols, and whether endpoint concepts were auto-placed before connector creation. Re-importing the same patch can repair relationships that were previously created without links.

`Sequence contains no matching element` was caused by UI code assuming invalid relationships always had origin/target connectors. The importer now skips linkless relationship creation and repairs existing JSON-created linkless relationships when a matching create operation supplies links. The UI also avoids crashing on missing relationship roles and logs a warning instead.

If the nested-content toggle does nothing, the action may be disabled because the composite view is unsafe to render as details. The lower-left log includes a composite toggle diagnostic with the source concept, source view, nested view, whether the nested view contains a visual of the same concept, whether it has same-view complements, and the current nested render stack.

`Nested content view would recursively render itself` means a concept's composite view contains a visual representation of that same concept, or the renderer has already entered that view higher in the nested render stack. JSON import prevents this shape by default and can repair older bad imports by removing the unsafe visual only.

`StackOverflowException in PresentationCore.dll` can happen when WPF recursively renders a composite view into a symbol inside the same composite view. The draw path now refuses to enter that recursive render and logs a warning before WPF rendering begins.

On import failure, ThinkComposer logs the exception message, full exception details, current operation index and summary, and the rollback path. The importer attempts to complete and undo the open command variation; if that fails and a variation remains open, it attempts to discard it.

## Limits

Images, attachments, styling, custom visual formatting, store-box references, and binary content are preserved in the `.tcom` file but exported as metadata or warnings only. Unsupported details are omitted from editable import behavior rather than failing export. Import does not delete by omission; deletions require explicit `delete: true` or an operation with `op: "delete"`.

For details, text-like content is exported as editable text where possible. Table details are exported as arrays of field/value records when their field metadata can be represented safely. Resource links, internal links, and attachments are exported as metadata; large binary payloads are not inlined.
