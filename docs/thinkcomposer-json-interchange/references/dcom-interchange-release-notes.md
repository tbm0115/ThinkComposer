# Dcom Interchange Release Notes

This branch consolidates the first Domain/Composition JSON Interchange pass for ThinkComposer. Native `.tcom` and `.tdom` files remain authoritative; JSON is an explicit export, patch, import, and safe merge workflow.

## New Commands

- `Composition -> File -> Export JSON...` and `Composition -> File -> Import JSON...` now include TechSpec-aware composition interchange and clearer source/import warning reporting.
- `Domain -> Export Domain JSON...` exports text-safe Domain JSON using `format: "ThinkComposer.DomainJsonInterchange"`.
- `Domain -> Import/Update Domain JSON...` previews and applies safe additive domain merges.
- `Composition -> Domain -> Update Embedded Domain...` safely updates an active `.tcom` composition's embedded domain snapshot from a native `.tdom` or Domain JSON file.

## Supported Workflows

- Update `.tcom` composition, concept, relationship, and supported definition summary/TechSpec fields through `ThinkComposer.JsonInterchange`.
- Create and visually place composition concepts/relationships with auto-place, auto-fit, auto-route, recursive-composite protection, and root fixture fallback via `importOptions.useActiveCompositionAsContainer`.
- Export `.tdom` domain metadata, definitions, tables, fields, roles, markers, external languages, and output templates as text-safe Domain JSON.
- Import additive Domain JSON patches for metadata, TechSpec, definitions, tables/fields, external languages, and output templates.
- Update an existing `.tcom` embedded domain snapshot without deleting legacy objects or replacing the domain wholesale.

## Validated Manual Scenarios

- Domain metadata/TechSpec patch import.
- Domain additive import with five created objects and no skips on a fresh test copy.
- Domain save/reopen persistence after import.
- Embedded Domain update from native `.tdom`.
- Output-template external language alias resolution such as `Mermaid.js_Flowchart` to `Mermaid_JS_Flowchart`.
- Source-warning vs import-warning dialog categorization.
- Composition layout fixture import into a fresh blank composition using `useActiveCompositionAsContainer`.
- Existing Appearance layout commands remain manual v1 commands and are separate from JSON import layout modes.

## Warning Model

Dialogs distinguish source warnings, import warnings, skipped operations, dangerous skipped operations, notes, and errors. Preserved export/source warnings such as missing category summaries or text-only template notes are context, not failures of the current operation.

## Recommended User Workflow

1. Save or copy the native `.tcom` or `.tdom`.
2. Export JSON when a full-state reference is useful, or author a patch directly against the schema.
3. Preview the import/update operation.
4. Confirm only after reviewing skipped and dangerous skipped counts.
5. Save the native `.tcom` or `.tdom` after successful apply.
6. Re-export JSON when verification or GPT follow-up work needs a fresh snapshot.

## Bundled Skill

The bundled `thinkcomposer-json-interchange` Skill is maintained with the schemas, docs, and samples. It now references the local ThinkComposer user manual PDF at `../../Installer/Deploy/InstrumindThinkComposer_Manual.pdf` for broader application context. The schemas and Markdown docs remain authoritative for current JSON interchange support; the manual may describe application features that are not yet supported by JSON interchange.

## Remaining Limitations

- Destructive domain cleanup and migrations are skipped by default.
- Live automatic `.tdom` synchronization is not implemented.
- Custom domain shape import is not implemented.
- Rich/binary content is summarized or preserved in native files, not fully exported/imported through JSON.
- General full multi-bend connector routing and full graph crossing minimization remain backlog.
- Spider, Hierarchy, Flowchart, and System Map are manual Appearance commands; JSON import currently integrates auto-placement, concept auto-fit, and link auto-route only.
