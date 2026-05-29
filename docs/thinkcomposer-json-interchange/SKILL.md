---
name: thinkcomposer-json-interchange
description: create, edit, repair, and validate thinkcomposer json interchange documents for a custom thinkcomposer build. use this skill when the user provides or requests thinkcomposer .tcom json exports, json patch operations, composition edits, concept or relationship creation, summary/description updates, visual placement, import troubleshooting, schema validation, or json validation against the latest thinkcomposer json schema from tbm0115/thinkcomposer or bundled fallback references.
---

# ThinkComposer JSON Interchange

## Purpose

Help users create, edit, repair, and validate ThinkComposer JSON Interchange documents that can be imported into a custom ThinkComposer build. Prefer patch-operation JSON for GPT-authored edits unless the user explicitly asks for a full-state merge document.

## Source-of-truth order

Use the most current accessible references in this order:

1. User-provided schema, docs, exports, samples, or instructions in the current conversation.
2. Latest schema from `https://raw.githubusercontent.com/tbm0115/ThinkComposer/feature/UXImprovements/docs/thinkcomposer-json-interchange.schema.json` when accessible and the user has not supplied a newer schema.
3. Latest import/export behavior docs from `https://raw.githubusercontent.com/tbm0115/ThinkComposer/feature/UXImprovements/docs/json-interchange.md` while the feature branch is active.
4. Latest Appearance/layout behavior docs from `https://raw.githubusercontent.com/tbm0115/ThinkComposer/feature/UXImprovements/docs/appearance-layout-tools.md` when the task involves visual placement, routing, or layout options.
5. Repository README or adjacent docs in `tbm0115/ThinkComposer` when the user asks about general ThinkComposer composition concepts.
6. Bundled fallback references when network or repository access is unavailable:
   - `references/thinkcomposer-json-interchange.schema.json`
   - `references/json-interchange.md`
   - `references/appearance-layout-tools.md`
   - `references/layout-services.md`
   - `references/ux-improvements-validation-checklist.md`
   - `references/json-interchange-patch.sample.json`
   - `references/json-interchange-regression.sample.json`
   - `references/obstacle-avoidance-regression.sample.json`
   - `references/spider-map-regression.sample.json`
   - `references/hierarchy-map-regression.sample.json`
   - `references/flowchart-regression.sample.json`
   - `references/system-map-regression.sample.json`
   - `references/test-findings.md`

If sources conflict, follow the schema selected for the current task, mention the conflict, and validate against that schema. Do not assume bundled references are current when the user has supplied updated files.

## Default workflow

1. Identify whether the user wants a patch document, full-state merge document, validation report, repair, or explanation.
2. Load provided JSON using UTF-8 BOM-tolerant handling (`utf-8-sig` or equivalent). A BOM parse error is not a ThinkComposer structure error.
3. If editing an existing composition export, inspect available `id`, `techName`, containers, views, definitions, and existing concepts/relationships.
4. Prefer stable `id` matching when present. Use `techName` when ids are missing, placeholder-like, or the user supplies tech-name-based instructions.
5. Generate minimal valid JSON. Preserve unrelated native data by using `operations` rather than rewriting full exports.
6. Validate the exact JSON artifact to be returned against the latest selected schema. If validation cannot run, state that clearly and still check structure manually against the references.
7. Return only import-ready JSON when the user asks for the document itself. Put explanations outside the JSON unless the user requests annotated output.

## Patch document rules

Use this top-level shape for most GPT-authored edits when the selected schema supports these import options:

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "importOptions": {
    "autoPlaceNewItems": true,
    "layoutMode": "gridNearViewport",
    "preventSelfRecursiveCompositeViews": true,
    "repairRecursiveVisuals": true,
    "autoFitPlacedConcepts": true,
    "autoRoutePlacedLinks": true
  },
  "operations": []
}
```

If the selected schema does not allow a newer field, remove that field instead of returning invalid JSON. Do not include `importOptions` values from docs unless the selected schema accepts them.

Required invariants:

- Preserve `format` exactly as `ThinkComposer.JsonInterchange`.
- Preserve `formatVersion` as `1` unless the current schema says otherwise.
- Do not delete by omission. Only use `op: "delete"` or `delete: true` when the user explicitly requests deletion.
- Use `update` for text edits and renames.
- Use `create` for new concepts and relationships.
- Use `place` when diagram visibility matters and the selected schema/importer supports it.
- Use operation-level `autoFit` to override imported concept auto-fit behavior and operation-level `autoRoute` to override imported relationship connector routing behavior.
- Include `warnings` only when intentionally skipping requested edits or surfacing import-relevant caveats.
- Replace example placeholder ids such as `existing-concept-guid` or `replace-with-root-composition-id` before schema validation. They are not UUID-valid.

## Common operations

### Update a concept summary or description

Use `entity: "concept"` and match by `id` when available, otherwise by top-level `techName`. Put editable fields inside `set`.

```json
{
  "op": "update",
  "entity": "concept",
  "id": "existing-concept-guid",
  "set": {
    "summary": "Updated summary."
  }
}
```

### Create a concept

Include `definitionTechName` and either `containerId` or `containerTechName`. Add view and placement fields when the concept should appear in a diagram.

```json
{
  "op": "create",
  "entity": "concept",
  "definitionTechName": "Concept",
  "containerTechName": "Root_Composition_TechName",
  "viewTechName": "Main",
  "x": 100,
  "y": 200,
  "width": 180,
  "height": 80,
  "set": {
    "name": "New concept",
    "techName": "New_Concept",
    "summary": "Created through JSON import."
  }
}
```

### Create a relationship

Include relationship definition, container, and origin/target role links. Prefer top-level `originIdeaIds`/`targetIdeaIds` when ids are known. Otherwise use `set.links` with `roleType` and `ideaTechName`.

```json
{
  "op": "create",
  "entity": "relationship",
  "definitionTechName": "Relationship",
  "containerTechName": "Root_Composition_TechName",
  "set": {
    "name": "Supports",
    "techName": "Supports",
    "summary": "Shows a supporting dependency.",
    "links": [
      { "roleType": "origin", "ideaTechName": "Source_Concept" },
      { "roleType": "target", "ideaTechName": "Target_Concept" }
    ]
  }
}
```

Add a `place` operation for relationships when the relationship should be visible in a specific view and the schema/importer supports placement:

```json
{
  "op": "place",
  "entity": "relationship",
  "techName": "Supports",
  "viewTechName": "Main"
}
```

Never create a relationship without resolvable origin/target links unless the user is explicitly testing linkless relationship behavior.

## Layout guidance

- Creating a model item does not guarantee it appears in a diagram.
- For important new concepts, provide `viewTechName`, `x`, `y`, `width`, and `height` when the selected schema supports those fields.
- For relationships, add a `place` operation in the same target view as the endpoints when visibility matters.
- If the user does not specify layout, set `importOptions.autoPlaceNewItems` to true and prefer `layoutMode: "gridNearViewport"` when accepted by the schema. This keeps imported batches near the visible viewport or normal content cluster and avoids being pushed toward extreme outlier coordinates.
- Use `layoutMode: "gridNearContainer"` when the user wants placement near the target composite/container view. Use `gridAfterExistingContent` only when preserving the older after-content behavior is specifically useful. Use `none` to disable automatic placement unless explicit placement fields are supplied.
- `importOptions.autoFitPlacedConcepts` defaults to true in the current custom build for concepts created or newly placed by import. Use `autoFit: false` on an operation to suppress it, or `autoFit: true` to fit an updated existing concept that was not newly placed.
- `importOptions.autoRoutePlacedLinks` defaults to true in the current custom build for relationships/connectors created, placed, or repaired by import. Use `autoRoute: false` on an operation to suppress it, or `autoRoute: true` to route an existing visible relationship touched by an operation.
- Do not assume the active root view is the intended view; use exported `views`, `activeViewId`, `rootViewId`, `ownerIdeaId`, and view tech names when available.
- Preserve or include `preventSelfRecursiveCompositeViews: true` and `repairRecursiveVisuals: true` unless the user explicitly needs otherwise and the schema accepts them.
- Do not place a concept inside its own composite view.
- Do not place a relationship visual in a composite view when one endpoint is the owner of that same composite view. In that case, create or repair the relationship model links but skip the unsafe visual placement and include a warning if the user expected the connector to be visible there.
- If an export contains extreme coordinates, do not infer that new items should be placed at similar extremes. Prefer schema-supported auto-placement or normal cluster placement.

## Appearance layout context

The current custom ThinkComposer build also includes manual Appearance tools that JSON import can reuse or complement:

- `Edit -> Appearance -> Fit Concept Width to Text` uses the same `ConceptAutoFitService` as JSON import auto-fit.
- `Edit -> Appearance -> Route Links with Obstacle Avoidance` uses the same `LinkObstacleRoutingService` as JSON import auto-route. It supports straightening, one-bend routing, hidden-central relationship routing, and dogleg routing using existing connector intermediate points.
- `Edit -> Appearance -> Arrange as Spider Map` arranges one root concept with connected concepts radially around it.
- `Edit -> Appearance -> Arrange as Hierarchy Map` arranges roots above child/dependent concepts and declutters visible relationship central symbols with endpoint-corridor constraints.
- `Edit -> Appearance -> Arrange as Flowchart` arranges directed process flow left-to-right and routes feedback/reverse edges through outer lanes.
- `Edit -> Appearance -> Arrange as System Map` arranges a system/root concept, internal components, and external actors, and creates/updates a visible Group Region boundary. Cross-boundary relationship bubbles use side lanes near the boundary.

These are deterministic v1 layout helpers, not full graph optimizers. Spider, Hierarchy, Flowchart, and System Map are manual commands only; they are not automatic JSON import layout modes yet. Dense maps, high crossing counts, or unusual domain-specific semantics may still need manual cleanup.

## Export consistency and repair guidance

- Some exports may contain `childIdeaIds` that are not present in the exported `ideas` array. Do not chase or repair those references unless the user asks for consistency repair.
- Build edit patches against actual exported objects. If missing references affect the requested edit, include a warning outside the JSON or as a valid `warnings` entry.
- Do not rename `techName` values casually. When renaming both `name` and `techName`, consider existing relationships, visuals, or user references that may still target the old `techName`.
- Prefer small patches over broad reorganization when the export contains duplicate names, duplicate tech names, or incomplete child references.

## Backlog / Not Implemented Yet

- `.tdom` JSON import/export.
- `.tdom` to `.tcom` domain update/sync.
- Tech Spec JSON coverage outside what the `.tcom` JSON interchange already exports/imports.
- Custom domain shape import.
- A full multi-bend general connector route model beyond the existing hidden-center and connector `IntermediatePosition` fields.
- Full graph crossing minimization or domain-specific layout optimization.

## Validation

Use `scripts/validate_json.py` when validating a JSON file:

```bash
python scripts/validate_json.py path/to/document.json
```

Useful options:

```bash
python scripts/validate_json.py path/to/document.json --schema path/to/schema.json
python scripts/validate_json.py path/to/document.json --no-fetch
```

The script reads JSON with UTF-8 BOM tolerance, attempts to fetch the latest schema from GitHub, then falls back to the bundled schema. If the user supplied a schema for the current task, pass it with `--schema` and do not fetch another schema. If validation fails, report the exact schema path and message, then suggest the smallest valid repair.

If the bundled validator script is unavailable in the active environment, manually check the document against the selected schema rules and state that schema validation was not executed.

## Response conventions

- For import-ready output, provide strict JSON with no comments or trailing commas.
- Prefer patch documents for modifications to an existing `.tcom` composition.
- Preserve user-provided ids and tech names exactly.
- Generate new `techName` values that are stable, readable, and identifier-like.
- Generate UUIDs only when the schema or workflow benefits from stable ids; otherwise let ThinkComposer assign ids if import supports it.
- Keep summaries/descriptions as plain text and preserve meaningful line breaks.
- Do not include binary payloads, images, styling, or unsupported native visual details in JSON output.
- When troubleshooting imports, use ThinkComposer's lower-left log diagnostics for layout decisions, recursive visual repairs, skipped unsafe connector visuals, unresolved endpoints, and rollback details.
- When producing a downloadable JSON artifact, validate the saved artifact itself before returning it.
