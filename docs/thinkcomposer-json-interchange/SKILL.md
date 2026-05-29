---
name: thinkcomposer-json-interchange
description: create, edit, repair, and validate ThinkComposer composition and domain JSON interchange documents for a custom ThinkComposer build. Use this skill for .tcom JSON patches, .tdom Domain JSON patches, TechSpec updates, visual placement, layout-aware import options, embedded-domain update planning, schema validation, or import troubleshooting.
---

# ThinkComposer JSON Interchange

## Purpose

Help users create, edit, repair, and validate ThinkComposer JSON Interchange documents for the custom `tbm0115/ThinkComposer` build. Prefer patch-operation JSON for GPT-authored edits unless the user explicitly asks for a full-state merge document.

There are two current formats:

- Composition JSON: `format: "ThinkComposer.JsonInterchange"` for active `.tcom` composition imports/exports.
- Domain JSON: `format: "ThinkComposer.DomainJsonInterchange"` for `.tdom` domain imports/exports and safe embedded-domain updates into an active `.tcom`.

Native `.tcom` and `.tdom` files remain authoritative. JSON is an interchange, patch, and merge layer only.

## Source-of-truth order

Use the most current accessible references in this order:

1. User-provided schema, docs, exports, samples, or instructions in the current conversation.
2. Latest branch references from `tbm0115/ThinkComposer`, currently `feature/DcomInterchange`, when accessible and the user has not supplied newer files.
3. Bundled fallback references:
   - `references/thinkcomposer-json-interchange.schema.json`
   - `references/thinkcomposer-domain-json-interchange.schema.json`
   - `references/json-interchange.md`
   - `references/domain-json-interchange.md`
   - `references/domain-sync.md`
   - `references/dcom-interchange-release-notes.md`
   - `references/appearance-layout-tools.md`
   - `references/layout-services.md`
   - `references/ux-improvements-validation-checklist.md`
   - `references/*.sample.json`
   - `references/test-findings.md`

If references conflict, follow the schema selected for the current task, mention the conflict, and validate against that schema.

## Additional application context

The repository includes the ThinkComposer user manual at:

`../../Installer/Deploy/InstrumindThinkComposer_Manual.pdf`

Use it as local context for application terminology, existing UI workflows, domain/composition concepts, concept and relationship definitions, tables/details/custom fields, markers, complements such as Group Regions, output templates, reports/exports, domain editing terminology, and general user-facing behavior.

The manual describes broad ThinkComposer application capabilities. Do not assume a manual feature is supported by JSON interchange unless the current JSON schemas, docs, or samples also document that support. Prefer Markdown docs when available; the PDF manual may later be migrated into Markdown under `docs/` and regenerated with Pandoc.

## Choose the right format

Use Composition JSON when the user wants to edit an existing composition:

- Update composition/concept/relationship names, summaries, descriptions, or TechSpec.
- Create concepts or relationships.
- Place visuals in views.
- Auto-place, auto-fit, or auto-route imported composition items.

Use Domain JSON when the user wants to edit a domain:

- Update domain metadata or TechSpec.
- Add/update concept definitions, relationship definitions, relationship roles, tables, fields, markers, variants, external languages, or output templates.
- Prepare a source for `Composition -> Domain -> Update Embedded Domain...`.

Do not mix the full Domain JSON schema into a Composition JSON document. Use the embedded-domain update command when a `.tcom` should pick up safe changes from a `.tdom` or Domain JSON source.

## Composition patch defaults

Use this top-level shape for most `.tcom` patches when the selected schema supports these fields:

```json
{
  "format": "ThinkComposer.JsonInterchange",
  "formatVersion": 1,
  "importOptions": {
    "autoPlaceNewItems": true,
    "layoutMode": "gridNearViewport",
    "preventSelfRecursiveCompositeViews": true,
    "repairRecursiveVisuals": true,
    "useActiveCompositionAsContainer": false,
    "autoFitPlacedConcepts": true,
    "autoRoutePlacedLinks": true
  },
  "operations": []
}
```

Rules:

- Preserve `format` and `formatVersion` exactly.
- Use `update` for text/TechSpec edits, `create` for new model items, `place` for diagram visibility, and `delete` only when explicitly requested.
- Match by stable `id` when available, otherwise by top-level `techName`.
- Put editable fields inside `set`.
- `set.techSpec` is plain text; omission preserves existing TechSpec and explicit empty string clears it.
- Relationship creates must include resolvable origin/target links. Linkless relationships are skipped by the importer.
- Relationship links may appear at operation top level or inside `set`; top-level values are preferred.
- Use operation-level `autoFit` and `autoRoute` to override top-level import options.
- Do not place a concept inside its own composite view.
- For normal imports, resolve containers by exact `containerId` or `containerTechName`. For root-level fixture patches that should import into whatever composition is active, set `importOptions.useActiveCompositionAsContainer: true`; this only falls back for missing, placeholder-like, or unresolved test/root composition references and should not be used for precise nested imports.

## Domain patch defaults

Use this top-level shape for most `.tdom` or embedded-domain update patches:

```json
{
  "format": "ThinkComposer.DomainJsonInterchange",
  "formatVersion": 1,
  "operations": []
}
```

Rules:

- Update only explicit fields. Omission must not clear or delete native data.
- `delete` operations are dangerous and skipped by default in the current importer.
- Domain create/update targets include `externalLanguage`, `linkRoleVariant`, clusters/categories, `markerDefinition`, `tableDefinition`, `fieldDefinition`, `conceptDefinition`, `relationshipDefinition`, `relationshipRole`, and `outputTemplate`.
- Child entities should include `ownerTechName` when needed, especially fields, roles, and templates.
- Field data type changes, table deletion, field deletion, and incompatible relationship role changes are skipped by default.
- Output templates are imported as text only and never executed.
- TechSpec is imported as text only and never executed.

## Embedded domain update

`Composition -> Domain -> Update Embedded Domain...` updates the active `.tcom` composition's embedded domain snapshot from either a native `.tdom` or a Domain JSON file.

This is explicit safe merge, not live sync:

- Adds missing safe definitions, tables, fields, roles, markers, variants, external languages, and templates.
- Updates text metadata and TechSpec.
- Retains legacy embedded-domain objects by default.
- Does not delete by omission or replace the embedded Domain object wholesale.
- Preserves existing composition ideas and relationship links.

## Report categories

ThinkComposer dialogs classify messages so successful operations do not look failed:

- `Source warnings` are preserved notes from an imported/exported JSON or native-domain export, such as fixture notes, grouped missing-category notices, text-only template export notes, or summarized binary/visual content.
- `Import warnings` are generated while planning or applying the current operation.
- `Skipped` means an operation was intentionally not applied; use the log for the indexed reason.
- `Dangerous skipped` means a potentially destructive change was refused by design.
- `Notes` are expected reminders or limitations.
- `Errors` are actual failures.

When helping with import results, do not treat nonzero source warnings as failures when skipped, dangerous skipped, import warnings, and errors are zero.

## Layout and visual import context

Composition JSON import supports:

- `importOptions.autoPlaceNewItems`
- `importOptions.layoutMode`
- `importOptions.preventSelfRecursiveCompositeViews`
- `importOptions.repairRecursiveVisuals`
- `importOptions.autoFitPlacedConcepts`
- `importOptions.autoRoutePlacedLinks`
- `importOptions.useActiveCompositionAsContainer`
- operation-level `autoFit`
- operation-level `autoRoute`

Manual Appearance commands in the current build:

- `Edit -> Appearance -> Fit Concept Width to Text`
- `Edit -> Appearance -> Route Links with Obstacle Avoidance`
- `Edit -> Appearance -> Arrange as Spider Map`
- `Edit -> Appearance -> Arrange as Hierarchy Map`
- `Edit -> Appearance -> Arrange as Flowchart`
- `Edit -> Appearance -> Arrange as System Map`

These are deterministic v1 layout helpers, not full graph optimizers. Spider, Hierarchy, Flowchart, and System Map are manual commands only; they are not automatic JSON import `layoutMode` values yet. JSON import currently integrates auto-placement, concept auto-fit, and link auto-route.

## Backlog / not implemented yet

- Custom domain shape import.
- Full multi-bend generic connector route model.
- Full graph crossing minimization.
- Live automatic `.tdom` synchronization.
- Destructive domain cleanup/migrations.
- Full rich/binary content export/import.
- Automatic JSON import modes for Spider, Hierarchy, Flowchart, or System Map layouts.

## Validation

Use `scripts/validate_json.py` when validating JSON files:

```bash
python scripts/validate_json.py path/to/document.json
```

Pass a schema explicitly when the user supplied one:

```bash
python scripts/validate_json.py path/to/document.json --schema path/to/schema.json --no-fetch
```

If the validator script is unavailable, parse JSON with UTF-8 BOM tolerance and manually check the document against the selected schema. Report exactly which schema or reference was used.

## Response conventions

- For import-ready output, provide strict JSON with no comments or trailing commas.
- Preserve user-provided ids and tech names exactly.
- Generate stable, readable, identifier-like `techName` values.
- Generate UUIDs only when stable ids help the workflow or schema validation.
- Preserve meaningful line breaks in summaries, descriptions, TechSpec, and template text.
- Never include executable behavior, binary payloads, images, or unsupported native object graphs in JSON.
- When troubleshooting imports, direct users to the lower-left ThinkComposer log for parse, preview, apply, skip, rollback, visual placement, auto-fit, auto-route, and domain merge diagnostics.
