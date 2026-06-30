---
name: thinkcomposer
description: Work directly with ThinkComposer diagrams and domains using modern .tcom containers with embedded interchange/previews, Composition JSON export/import, Domain JSON export/import, application log diagnostics, and exported view images. Use when the user asks Codex to inspect, edit, generate, repair, or verify ThinkComposer projects.
---

# ThinkComposer Direct Workflow

Use this skill when Codex is helping with a ThinkComposer `.tcom` composition/container, `.tdom` domain, exported Composition JSON, exported Domain JSON, import patch, lower-left application log, or embedded/exported view image.

## Operating Model

ThinkComposer remains the source of truth for native `.tcom` and `.tdom` files. Codex should work through high-fidelity interchange artifacts:

- Modern container context: a `.tcom` can be a ZIP package containing `Interchange/manifest.json`, `Interchange/Composition.json`, `Interchange/Domain.json`, and `Previews/views/*.png`. Prefer this when available because it gives Codex model context and visual screenshots together.
- Composition context: `Composition > File > Export JSON...`, usually saved as `*.tc.json`.
- Composition edits: create a JSON import patch, then have the user run `Composition > File > Import JSON...`.
- Domain context or edits: `Domain > Export Domain JSON...` and `Domain > Import/Update Domain JSON...`, usually `*.tdom.json`.
- Visual verification: run `Export Image` on the active view, preferably PNG. Hold Ctrl while exporting when a transparent PNG is useful.
- Diagnostics: inspect the lower-left application log. If it is not persisted to disk, ask the user to copy the relevant log lines into a `.log` or `.txt` file.

If the plugin MCP tools are available, prefer them for artifact discovery, JSON summaries, patch writing, log analysis, and latest-image lookup. The expected tool names are:

- `thinkcomposer_discover`
- `thinkcomposer_read_container_summary`
- `thinkcomposer_read_json_summary`
- `thinkcomposer_validate_json`
- `thinkcomposer_write_patch`
- `thinkcomposer_extract_container_artifacts`
- `thinkcomposer_analyze_log`
- `thinkcomposer_latest_image`

For detailed Composition JSON, Domain JSON, container manifest, schema, sample, and import-troubleshooting guidance, use the sibling `thinkcomposer-json-interchange` skill in `../thinkcomposer-json-interchange/`. Keep this skill focused on the direct ThinkComposer workflow and use the sibling skill as the authoritative JSON interchange reference.

## Modern `.tcom` Containers

When the user provides a modern `.tcom`, inspect it before asking for separate exports. Treat these parts as read-only context:

- `Interchange/manifest.json`: package metadata, source composition identity/version, embedded JSON part metadata, preview metadata, warnings, and hashes.
- `Interchange/Composition.json`: full Composition JSON context.
- `Interchange/Domain.json`: embedded Domain JSON context when present.
- `Previews/views/*.png`: view screenshots keyed by `viewName`, `viewTechName`, `viewId`, width, height, skipped state, and part URI.

Use embedded previews as Codex's first visual pass. Extract previews only when a local image file path is needed for direct visual inspection. Do not modify the `.tcom` package directly; still write separate JSON patches and import them through ThinkComposer.

## Safe JSON Rules

- Before authoring non-trivial JSON patches, read the sibling `thinkcomposer-json-interchange/SKILL.md` or its targeted `references/` files. It carries the full patch rules, schemas, samples, and validation workflow.
- Prefer patch-style `operations[]` for GPT-authored changes. Full-state exports are best as context.
- Do not delete anything unless the user explicitly asks for deletion. Omission from JSON never means delete.
- Match existing objects by `id` when available, otherwise by `techName`.
- For root-level generated composition patches, set:

```json
{
  "importOptions": {
    "useActiveCompositionAsContainer": true
  }
}
```

Use `containerTechName: "Active_Composition_Root"` for new root-level concepts and relationships when the active composition should be the container.

- Concepts require `definitionTechName`, a container, and `set.name` or `set.techName`.
- Relationships require `definitionTechName`, a container, and resolvable origin/target links. Prefer `set.links[]` with `roleType` plus `ideaId` or `ideaTechName`.
- Re-imported relationship creates are upserts when `id` or `techName` matches; use stable `techName` values to avoid duplicates.
- For generated diagrams, use `importOptions.relationshipVisualPlacementMode: "endpointCorridor"` or `"auto"` unless coordinates are hand-curated.
- For large models, use top-level `visualStrategy.mode: "modelOnly"` or `"overviewAndModel"` with deferred routing, auto-fit, and refresh instead of placing every visual by hand.
- Use Domain JSON first when a Composition JSON patch depends on new or changed concept definitions, relationship definitions, roles, details, fields, output templates, or compatibility signatures.

## Iteration Loop

1. Discover or request current artifacts: modern `.tcom` container if available, otherwise composition JSON, domain JSON when relevant, latest exported PNG/image, and log text.
2. Summarize the current model before changing it: composition/domain tech names, active/root view, definitions, idea/relationship counts, operation groups, warnings, and compatibility metadata.
3. Write the smallest safe import patch that satisfies the request.
4. Validate the patch structurally and inspect risky items such as deletes, unresolved placeholders, missing links, or large exact visuals.
5. Give the user the patch path and exact ThinkComposer menu command to import it.
6. After import, inspect the preview/apply summary and lower-left log for `warning`, `skipped`, `error`, `failed`, `blocked`, compatibility, rollback, and affected-view lines.
7. Ask for or find a fresh exported image, then visually inspect it before declaring the diagram done.
8. Iterate with follow-up patches rather than editing native `.tcom`/`.tdom` files directly.

## Useful Log Prefixes

Composition JSON import/export writes lines such as:

- `JSON export start`
- `JSON export summary`
- `JSON import start`
- `JSON import parse/preview failed`
- `JSON import preview summary`
- `JSON import planned summary`
- `JSON import applied summary`
- `JSON import warning`
- `JSON import skipped`
- `JSON import error`
- `JSON import affected views`

Domain JSON import/export writes lines such as:

- `Domain JSON export started`
- `Domain JSON export summary`
- `Domain JSON import started`
- `Domain JSON import warning`
- `Domain JSON skipped`
- `Domain JSON error`
- `Domain JSON import completed`
- `Embedded Domain update completed`

Image export success writes:

- `View '<view route>' successfully exported to '<path>'.`

## Repository References

When the ThinkComposer repository is available, use these local docs for format details:

- `docs/thinkcomposer-plugin/skills/thinkcomposer-json-interchange/SKILL.md`
- `docs/thinkcomposer-plugin/skills/thinkcomposer-json-interchange/references/*`
- `docs/json-interchange.md`
- `docs/domain-json-interchange.md`
- `docs/domain-sync.md`
- `docs/appearance-layout-tools.md`
- `docs/thinkcomposer-json-interchange.schema.json`
- `docs/thinkcomposer-domain-json-interchange.schema.json`
- `samples/*.sample.json`

Do not invent schema behavior when those files are available. Read the relevant document or sample first.
