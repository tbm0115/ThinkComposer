---
name: thinkcomposer
description: Work directly with ThinkComposer diagrams and domains using modern .tcom/.tdom containers with authoritative root JSON persistence, package JSON patching, embedded previews, CLI validation, Git synchronization, report/output generation, application log diagnostics, and exported view images. Use when the user asks Codex to inspect, edit, generate, repair, synchronize, or verify ThinkComposer projects.
---

# ThinkComposer Direct Workflow

Use this skill when Codex is helping with a ThinkComposer `.tcom` composition/container, `.tdom` domain, authoritative package JSON, compatibility Composition/Domain JSON export, lower-left application log, or embedded/exported view image.

## Operating Model

ThinkComposer native `.tcom` and `.tdom` packages remain the source of truth, with modern packages using root JSON persistence payloads. Codex should work through the package root JSON first:

- Modern `.tcom` context: root `manifest.json`, optional package-level `manifest.json` `gitSync`, optional embedded Domain `manifest.json` `embeddedDomainGitSync`, authoritative `Composition.json`, authoritative embedded `Domain.json`, optional legacy fallback `Composition.bin`, non-authoritative `Interchange/*` sidecars, and `Previews/views/*.png`.
- Modern `.tdom` context: root `manifest.json`, optional `manifest.json` `gitSync`, authoritative `Domain.json`, optional authoritative `TemplateComposition.json`, optional legacy fallback `Domain.bin`, non-authoritative `Interchange/*` sidecars, and optional previews.
- Composition edits: patch root `/Composition.json` inside the `.tcom`, and update `/manifest.json` authoritative part metadata.
- Domain edits: patch root `/Domain.json` inside the `.tdom` or `.tcom`, and update `/manifest.json` authoritative part metadata.
- Compatibility CLI paths: use `thinkcomposer composition export-json/import-json`, `thinkcomposer domain export-json/import-json`, `package inspect`, and `validate-json-persistence` for migration, validation, or preview/merge diagnostics when needed.
- CLI automation: use `thinkcomposer report pdf` for headless PDF/XPS reports, `thinkcomposer output generate` for output-template generation, and `thinkcomposer git status/pull/push` for linked package synchronization. These commands operate on saved packages; patch and validate authoritative root JSON first when the model itself must change.
- Embedded-domain refresh: use `Composition -> Domain -> Update Embedded Domain...` or `thinkcomposer domain update-embedded --input <file.tcom> --domain <file.tdom> --output <updated-file.tcom>` when a `.tcom` should pick up safe domain changes from a `.tdom`.
- Git sync: use `thinkcomposer git link/status/pull/push` for package-level synchronization. Composition push is supported; Domain packages are link/pull only. `gitSync` stores package remote/branch/path metadata only; `.tcom` `embeddedDomainGitSync` stores the embedded Domain source link separately. Commit/hash state is machine-local.
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

For detailed Composition JSON, Domain JSON, package manifest, schema, sample, and import-troubleshooting guidance, use the sibling `thinkcomposer-json-interchange` skill in `../thinkcomposer-json-interchange/`. Keep this skill focused on the direct ThinkComposer workflow and use the sibling skill as the authoritative JSON reference.

## Modern Containers

When the user provides a modern `.tcom` or `.tdom`, inspect it before asking for separate exports. Treat root JSON as authoritative and sidecars/previews as context:

- `manifest.json`: root package metadata, persistence format, authoritative JSON part hashes, and legacy fallback metadata.
- `manifest.json` optional `gitSync`: generic Git remote URL, branch, and repo-relative baseline package paths for the package itself. Optional `.tcom` `embeddedDomainGitSync` carries the embedded Domain source `.tdom` link. Do not add credentials or tokens.
- `Composition.json`: authoritative Composition JSON payload in `.tcom`.
- `Domain.json`: authoritative Domain JSON payload in `.tdom` or embedded-domain payload in `.tcom`.
- `TemplateComposition.json`: optional authoritative template composition payload in `.tdom`.
- `Interchange/manifest.json`: sidecar metadata, source composition identity/version, preview metadata, warnings, and hashes.
- `Interchange/Composition.json`: sidecar Composition JSON context.
- `Interchange/Domain.json`: sidecar embedded Domain JSON context when present.
- `Previews/views/*.png`: view screenshots keyed by `viewName`, `viewTechName`, `viewId`, width, height, skipped state, and part URI.

Use embedded previews as Codex's first visual pass. Extract previews only when a local image file path is needed for direct visual inspection. When editing a package, patch the root JSON and refresh `manifest.json`; do not edit `/Composition.bin`, `/Domain.bin`, or `/Interchange/*` as if they were authoritative.

## CLI Task Routing

Use the ThinkComposer CLI when the user asks for repeatable package checks or headless output:

- Inspect package contract: `thinkcomposer package inspect --input <file.tcom|file.tdom>`.
- Validate JSON-authoritative persistence: `thinkcomposer composition validate-json-persistence --input <file.tcom> --output-dir <dir>` or `thinkcomposer domain validate-json-persistence --input <file.tdom> --output-dir <dir>`.
- Convert legacy binary-backed files: `thinkcomposer composition convert-json-persistence` or `thinkcomposer domain convert-json-persistence`.
- Synchronize Git-linked packages: `thinkcomposer git status`, `thinkcomposer git pull`, and, for linked `.tcom` packages, `thinkcomposer git push`.
- Generate reports: `thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>`.
- Generate language output files: `thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name>`.

Do not use CLI import/export as the normal persistence edit path. They are compatibility/interchange commands for preview, migration, diagnostics, or external review. For model edits, patch root package JSON and refresh `/manifest.json`, then validate with the CLI.

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
- Update or inspect root `Domain.json` first when a Composition JSON patch depends on new or changed concept definitions, relationship definitions, roles, details, fields, output templates, or compatibility signatures.

## Iteration Loop

1. Discover or request current artifacts: modern `.tcom`/`.tdom` container if available, otherwise compatibility composition/domain JSON, latest exported PNG/image, and log text.
2. Summarize the current model before changing it: composition/domain tech names, active/root view, definitions, idea/relationship counts, operation groups, warnings, and compatibility metadata.
3. Write the smallest safe root JSON update that satisfies the request.
4. Update the package root JSON and refresh root `manifest.json` authoritative metadata for changed parts.
5. Validate the package with `package inspect` and `validate-json-persistence`; use CLI import/export compatibility paths only when preview/merge diagnostics are needed. If the package is Git-linked, preserve or deliberately update `gitSync` and `embeddedDomainGitSync` in `/manifest.json`.
6. Inspect the validation output and lower-left log for `warning`, `skipped`, `error`, `failed`, `blocked`, compatibility, rollback, and affected-view lines.
7. Ask for or find a fresh exported image, then visually inspect it before declaring the diagram done.
8. Iterate by patching the authoritative root JSON, not sidecars or legacy binaries.

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

Domain JSON compatibility import/export writes lines such as:

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
- `docs/cli.md`
- `docs/json-interchange.md`
- `docs/domain-json-interchange.md`
- `docs/domain-sync.md`
- `docs/appearance-layout-tools.md`
- `docs/thinkcomposer-json-interchange.schema.json`
- `docs/thinkcomposer-domain-json-interchange.schema.json`
- `samples/*.sample.json`

Do not invent schema behavior when those files are available. Read the relevant document or sample first.
