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

Relationship create operations are upserts. If a `create relationship` operation matches an existing relationship by `id` or `techName`, the importer updates editable fields and repairs missing role links instead of creating a duplicate. Re-importing the same patch should not duplicate relationships, links, visuals, or connectors.

Linkless relationships are skipped by default. This prevents invalid native relationship objects that can later break view or context-menu code expecting origin/target roles.

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

Create operations can include the same `viewId`, `viewTechName`, `x`, `y`, `width`, and `height` fields. If no coordinates are supplied and `importOptions.autoPlaceNewItems` is omitted or true, the importer chooses the created item's container view, then the active view, and places new concepts in a deterministic grid near the current viewport or main content cluster. Extreme outlier symbols are ignored when choosing the default layout origin, so imported items should not be pushed to coordinates such as `x=9000, y=5000` unless the patch explicitly requests those coordinates. Relationships are connected when their linked endpoint concepts are visible in the chosen view.

If a relationship target view is missing one or both endpoint concept symbols and auto-placement is enabled, the importer attempts to place those endpoint concepts in that view before placing the relationship. If endpoints still cannot be resolved or placed, the relationship connector is skipped with a warning listing the missing endpoints.

Imported visuals may be placed in nested or composite views such as a concept's own view, not necessarily the root view currently visible in the workspace. After import, the completion dialog and application log list affected views, and the importer attempts to open, fit, and select imported visuals in the first affected view when safe.

The importer will not place a concept inside its own composite view. It also will not place a relationship visual in a composite view when one endpoint is the owner of that same composite view, because showing the owner's nested content as details would recursively render the view inside itself. In that case the relationship model links are still created or repaired, but the unsafe connector visual is skipped with a warning. Re-import can repair older bad imports by removing only self-recursive visual representations while preserving the underlying concepts and relationships.

Set `"autoPlace": false` on a single create operation, or `"importOptions": { "autoPlaceNewItems": false }` at the top level, to create model objects without automatic visual placement.

Concept visuals created or newly placed during import are auto-fitted to their visible text by default. This uses the same `ConceptAutoFitService` as `Edit -> Appearance -> Fit Concept Width to Text`, so connector refresh and undo/redo behavior match the manual command. Existing concepts that are only text-updated are not resized unless their operation explicitly includes `"autoFit": true`. Use `"autoFit": false` on a create or place operation to keep the supplied/default width for that operation.

Relationship visuals created, placed, or repaired during import are auto-routed by default after concept auto-fit completes. This uses the same `LinkObstacleRoutingService` as `Edit -> Appearance -> Route Links with Obstacle Avoidance`, including hidden-central simple relationship routing and dogleg fallback. Existing unrelated links in the view are not routed. Use `"autoRoute": false` on an operation to preserve that operation's current connector geometry, or `"autoRoute": true` to route an existing visible relationship touched by an update/place operation.

Layout options:

- `gridNearViewport` is the default. It places the batch near the visible center when the view is open, otherwise near the normal content cluster or `100,100` for an empty/outlier-only view.
- `gridNearContainer` keeps the batch near the existing cluster for the target composite/container view.
- `gridAfterExistingContent` uses the older behavior of placing the batch after existing non-outlier content.
- `none` disables automatic placement unless an operation supplies explicit placement fields.
- `autoFitPlacedConcepts` defaults to true. It fits newly created or newly placed concept symbols to text during import without resizing every existing concept in the view.
- `autoRoutePlacedLinks` defaults to true. It routes newly created, newly placed, or repaired relationship visuals/connectors during import without routing every existing connector in the view.
- `useActiveCompositionAsContainer` defaults to false. It is an opt-in convenience for root-level fixture imports into a fresh active composition.
- `preventSelfRecursiveCompositeViews` defaults to true and blocks self-recursive owner-in-own-view placements.
- `repairRecursiveVisuals` defaults to true and removes previously imported self-recursive visuals during JSON import/re-import.

Manual Appearance layout commands are currently separate from JSON import layout modes. `Edit -> Appearance -> Arrange as Spider Map`, `Arrange as Hierarchy Map`, `Arrange as Flowchart`, and `Arrange as System Map` are v1 manual commands; they are not automatic JSON import `layoutMode` values yet. The current JSON import integration is limited to auto-placement, concept auto-fit, and link auto-route.

GPT prompt example:

```text
Edit this ThinkComposer JSON using patch operations only. Update existing summaries by id or techName. For each new concept or relationship, include definitionTechName and containerTechName. For relationships, include origin/target links, preferably as set.links with roleType and ideaId or ideaTechName. Also include viewTechName plus x/y/width/height for important new concepts, and add place operations for relationships so the new model items are visible in the intended view. Leave importOptions.autoFitPlacedConcepts and importOptions.autoRoutePlacedLinks true so new concept labels fit their text and new links route around obstacles; use operation autoFit:false or autoRoute:false only when I provide deliberate sizing or connector geometry. Do not delete anything unless I explicitly request it.
```

Additional sample files are available at `samples/json-interchange-patch.sample.json` and `samples/json-interchange-regression.sample.json`.

## Domain Interchange

Domain JSON Interchange is separate from Composition JSON Interchange. Use `format: "ThinkComposer.DomainJsonInterchange"` for `.tdom` export/import or for updating an active composition's embedded domain snapshot.

Related docs:

- `docs/domain-json-interchange.md`
- `docs/domain-sync.md`
- `docs/thinkcomposer-domain-json-interchange.schema.json`

Use `Composition > Domain > Update Embedded Domain...` when an existing `.tcom` should pick up safe additions or updates from a newer `.tdom` or Domain JSON file. This command updates the embedded domain snapshot explicitly; it does not create a live sync link and does not delete legacy embedded-domain objects by omission.

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

## Troubleshooting

Detailed import diagnostics are in the lower-left application log window. The modal confirmation and completion dialogs only summarize planned or applied counts.

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
