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

Exports include deterministic, pretty-printed DTO data rather than native binary or WPF object graphs. The main top-level sections are:

- `composition`: document identity, name, tech name, summary, version fields, view prefix, active/root view ids, and domain summary.
- `definitions`: domain definition names and tech names referenced by exported ideas.
- `ideas`: concepts with stable ids, definition references, editable text, container, markers, details, and child idea ids.
- `relationships`: relationships with stable ids, definition references, editable text, role links, origin/target idea ids, and container.
- `views`: view identity plus safe layout data for visible representations.
- `operations`: optional patch instructions.
- `warnings`: export notes for skipped or metadata-only native data.

Import always merges into the active `.tcom` composition. Existing entities are matched by `id` first, then by `techName` when no id is supplied. Missing JSON objects are left untouched.

Import and export write diagnostic messages to ThinkComposer's lower-left application log window. The confirmation dialog intentionally stays concise; use the log for parse details, per-operation planning, applied operation results, skipped reasons, warnings, and rollback diagnostics.

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
    "autoPlaceNewItems": true
  },
  "operations": [
    {
      "op": "update",
      "entity": "concept",
      "id": "existing-concept-guid",
      "set": {
        "name": "Renamed concept",
        "summary": "New GPT-edited summary"
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

Patch semantics:

- `update` operations must match an existing object by `id` or top-level `techName`; unmatched updates are skipped with warnings.
- `create` operations require enough safe native information. Concepts require `definitionTechName` plus `containerId` or `containerTechName`. Relationships require a relationship definition, container, and valid origin/target links.
- `place` operations create or update a visual representation for an existing concept or relationship in a target view.
- `delete` only runs for explicit delete operations or `delete: true`; omission never deletes native objects.
- Existing native data that is not represented in JSON is preserved.

Relationship connectivity can be supplied at the operation top level or inside `set`. Top-level values are preferred when both are present, but `set.originIdeaIds`, `set.originIdeaTechNames`, `set.targetIdeaIds`, `set.targetIdeaTechNames`, and `set.links` are accepted for GPT-authored patches. Link `roleType` values are normalized case-insensitively, so `Origin`, `Target`, `origin`, and `target` are accepted.

Relationship create operations are upserts. If a `create relationship` operation matches an existing relationship by `id` or `techName`, the importer updates editable fields and repairs missing role links instead of creating a duplicate. Re-importing the same patch should not duplicate relationships, links, visuals, or connectors.

Linkless relationships are skipped by default. This prevents invalid native relationship objects that can later break view or context-menu code expecting origin/target roles.

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

Create operations can include the same `viewId`, `viewTechName`, `x`, `y`, `width`, and `height` fields. If no coordinates are supplied and `importOptions.autoPlaceNewItems` is omitted or true, the importer chooses the created item's container view, then the active view, and places new concepts in a deterministic grid to the right of existing content. Relationships are connected when their linked endpoint concepts are visible in the chosen view.

If a relationship target view is missing one or both endpoint concept symbols and auto-placement is enabled, the importer attempts to place those endpoint concepts in that view before placing the relationship. If endpoints still cannot be resolved or placed, the relationship connector is skipped with a warning listing the missing endpoints.

Imported visuals may be placed in nested or composite views such as a concept's own view, not necessarily the root view currently visible in the workspace. After import, the completion dialog and application log list affected views, and the importer attempts to open, fit, and select imported visuals in the first affected view when safe.

Set `"autoPlace": false` on a single create operation, or `"importOptions": { "autoPlaceNewItems": false }` at the top level, to create model objects without automatic visual placement.

GPT prompt example:

```text
Edit this ThinkComposer JSON using patch operations only. Update existing summaries by id or techName. For each new concept or relationship, include definitionTechName and containerTechName. For relationships, include origin/target links, preferably as set.links with roleType and ideaId or ideaTechName. Also include viewTechName plus x/y/width/height for important new concepts, and add place operations for relationships so the new model items are visible in the intended view. Do not delete anything unless I explicitly request it.
```

Additional sample files are available at `samples/json-interchange-patch.sample.json` and `samples/json-interchange-regression.sample.json`.

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
10. Re-import the same patch and verify it does not duplicate concepts, relationships, links, visual representations, or connectors.
11. Right-click imported concepts and relationships/links and verify no runtime exception appears.
12. Save, close, reopen, and verify imported visuals and relationship links persist.
13. Verify warnings are readable, including object-valued warnings.
14. Verify no `Put-visual must be applied within a Command` error appears.
15. If an error occurs, inspect the log for the full exception, current operation, and rollback/undo result.

## Troubleshooting

Detailed import diagnostics are in the lower-left application log window. The modal confirmation and completion dialogs only summarize planned or applied counts.

`Put-visual must be applied within a Command` means WPF visual refresh or placement was attempted while no ThinkComposer edit command variation was active. JSON import refreshes affected views inside the single `Import JSON` command variation so view updates remain undoable and command-safe. If this error appears again, the log should show the operation being applied and whether rollback completed.

If imported concepts appear in the tree but not on the canvas, check the log for affected-view and visual-placement lines. They may have been placed in a nested/composite view rather than the active root view. The importer materializes view children for placed visuals and attempts to open and fit the first affected view, but unopened views still render only when the workspace opens them.

If relationships import but connectors are missing, check whether the relationship has resolved origin/target links and whether both endpoint symbols are visible in the same target view. The importer logs the relationship link source (`top-level`, `set`, or `none`), resolved endpoints, missing endpoint symbols, and whether endpoint concepts were auto-placed before connector creation. Re-importing the same patch can repair relationships that were previously created without links.

`Sequence contains no matching element` was caused by UI code assuming invalid relationships always had origin/target connectors. The importer now skips linkless relationship creation and repairs existing JSON-created linkless relationships when a matching create operation supplies links. The UI also avoids crashing on missing relationship roles and logs a warning instead.

On import failure, ThinkComposer logs the exception message, full exception details, current operation index and summary, and the rollback path. The importer attempts to complete and undo the open command variation; if that fails and a variation remains open, it attempts to discard it.

## Limits

Images, attachments, styling, custom visual formatting, store-box references, and binary content are preserved in the `.tcom` file but exported as metadata or warnings only. Unsupported details are omitted from editable import behavior rather than failing export. Import does not delete by omission; deletions require explicit `delete: true` or an operation with `op: "delete"`.

For details, text-like content is exported as editable text where possible. Table details are exported as arrays of field/value records when their field metadata can be represented safely. Resource links, internal links, and attachments are exported as metadata; large binary payloads are not inlined.
