---
name: thinkcomposer
description: Work directly with ThinkComposer diagrams and domains using modern .tcom/.tdom snapshot containers, standalone operation patches, multi-point Relationship routing, embedded previews, CLI validation, Git synchronization, reports, logs, and exported view images. Use when Codex needs to inspect, edit, generate, repair, route, synchronize, or verify ThinkComposer projects.
---

# ThinkComposer Direct Workflow

Use this skill when Codex is helping with a ThinkComposer `.tcom` composition/container, `.tdom` domain, authoritative package JSON, compatibility Composition/Domain JSON export, lower-left application log, or embedded/exported view image.

## Operating Model

ThinkComposer native `.tcom` and `.tdom` packages remain the source of truth. Inspect authoritative root JSON first, but distinguish saved snapshot state from one-shot edit intent:

- Modern `.tcom` context: root `manifest.json`, optional package-level `manifest.json` `gitSync`, optional embedded Domain `manifest.json` `embeddedDomainGitSync`, authoritative `Composition.json`, authoritative embedded `Domain.json`, non-authoritative `Interchange/*` sidecars, and `Previews/views/*.png`. Current saves do not write `Composition.bin`; it can exist only in an older binary-only/transitional package.
- Modern `.tdom` context: root `manifest.json`, optional `manifest.json` `gitSync`, authoritative `Domain.json`, optional authoritative `TemplateComposition.json`, non-authoritative `Interchange/*` sidecars, and optional previews. Current saves do not write `Domain.bin`; it can exist only in an older binary-only/transitional package.
- Composition edits: write a standalone `operations[]` patch, preview it, and apply it through `thinkcomposer_apply_patch` or `thinkcomposer composition import-json`. The application writes canonical `/Composition.json` and updates `/manifest.json` atomically. The MCP apply tool also runs route-health validation and exports a result image after a successful Composition apply.
- Domain edits: patch root `/Domain.json` inside the `.tdom` or `.tcom`, and update `/manifest.json` authoritative part metadata.
- CLI paths: use `composition import-json` as the safe Composition patch materializer, `domain import-json` for Domain patches, `package inspect`, `composition validate-routing`, and `validate-json-persistence` for verification. Developers can use the persistence corpus/benchmark commands for performance work.
- CLI automation: use `thinkcomposer report pdf`, `thinkcomposer output generate`, and `thinkcomposer git status/pull/push` on canonical saved packages.
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
- `thinkcomposer_apply_patch`
- `thinkcomposer_extract_container_artifacts`
- `thinkcomposer_analyze_log`
- `thinkcomposer_latest_image`

For detailed Composition JSON, Domain JSON, package manifest, schema, sample, and import-troubleshooting guidance, use the sibling `thinkcomposer-json-interchange` skill in `../thinkcomposer-json-interchange/`. Keep this skill focused on the direct ThinkComposer workflow and use the sibling skill as the authoritative JSON reference.

## Modern Containers

When the user provides a modern `.tcom` or `.tdom`, inspect it before asking for separate exports. Treat root JSON as authoritative and sidecars/previews as context:

- `manifest.json`: root package metadata, persistence format, authoritative JSON part hashes, and legacy fallback metadata. Current saves emit `legacyBinaryFallback.present:false` without a binary URI/hash.
- `manifest.json` optional `gitSync`: generic Git remote URL, branch, and repo-relative baseline package paths for the package itself. Optional `.tcom` `embeddedDomainGitSync` carries the embedded Domain source `.tdom` link. Do not add credentials or tokens.
- `Composition.json`: authoritative Composition JSON payload in `.tcom`.
- `Domain.json`: authoritative Domain JSON payload in `.tdom` or embedded-domain payload in `.tcom`.
- `TemplateComposition.json`: optional authoritative template composition payload in `.tdom`.
- `Interchange/manifest.json`: non-authoritative sidecar metadata. Format v2 includes source identity, preview render-input hashes/profiles, `rendered`/`reused`/`empty` disposition, PNG hashes, and warnings.
- `Interchange/Composition.json`: sidecar Composition JSON context.
- `Interchange/Domain.json`: sidecar embedded Domain JSON context when present.
- `Previews/views/*.png`: view screenshots keyed by `viewName`, `viewTechName`, `viewId`, width, height, skipped state, and part URI.

Use embedded previews as Codex's first visual pass. Extract previews only when a local image file path is needed for inspection. Treat root Composition JSON as snapshot state: do not splice `operations`, `importOptions`, or `visualStrategy` into it. Do not edit `/Composition.bin`, `/Domain.bin`, or `/Interchange/*`.

## CLI Task Routing

Use the ThinkComposer CLI when the user asks for repeatable package checks or headless output:

- Inspect package contract: `thinkcomposer package inspect --input <file.tcom|file.tdom>`.
- Validate JSON-authoritative persistence: `thinkcomposer composition validate-json-persistence --input <file.tcom> --output-dir <dir>` or `thinkcomposer domain validate-json-persistence --input <file.tdom> --output-dir <dir>`.
- Validate Relationship geometry and layout idempotence: `thinkcomposer composition validate-routing --input <file.tcom> --output-dir <dir> [--layout route|spider|hierarchy|flowchart|system]`.
- Preview/apply a Composition patch: `thinkcomposer composition import-json --input <file.tcom> --json <patch.json> --output <file.tcom> --preview-only`, then rerun without `--preview-only` after reviewing the plan.
- Convert legacy binary-backed files: `thinkcomposer composition convert-json-persistence` or `thinkcomposer domain convert-json-persistence`; the output is JSON-only and omits the matching binary part.
- Benchmark native JSON persistence: prepare a corpus with `thinkcomposer performance prepare-json-persistence-corpus`, record a pre-optimization baseline with `--allow-legacy-baseline-output` only when its JSON-authoritative writer retains the matching binary fallback, then run a strict JSON-only candidate with `--baseline` on the same machine.
- Synchronize Git-linked packages: `thinkcomposer git status`, `thinkcomposer git pull`, and, for linked `.tcom` packages, `thinkcomposer git push`.
- Generate reports: `thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>`.
- Generate language output files: `thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name>`.

Use CLI import for normal generated Composition edits because it separates one-shot directives from canonical snapshot persistence. Direct root Composition edits are reserved for exact snapshot recovery or deliberate expert maintenance.

## Safe JSON Rules

- Before authoring non-trivial JSON patches, read the sibling `thinkcomposer-json-interchange/SKILL.md` or its targeted `references/` files. It carries the full patch rules, schemas, samples, and validation workflow.
- Prefer patch-style `operations[]` for GPT-authored changes. Full-state exports are best as context.
- Write operations into a separate `.json` patch file. Never embed them into authoritative `/Composition.json`.
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
- For generated Relationships, omit Relationship visual `x/y`, connector endpoints, and `routePoints`; set `visual.relationshipCenterPlacement:"endpointCorridor"` and `autoRoute:true`. Only preserve exact geometry when the user explicitly requests it.
- For large models, use top-level `visualStrategy.mode: "modelOnly"` or `"overviewAndModel"` with deferred routing, auto-fit, and refresh instead of placing every visual by hand.
- Update or inspect root `Domain.json` first when a Composition JSON patch depends on new or changed concept definitions, relationship definitions, roles, details, fields, output templates, or compatibility signatures.

## Iteration Loop

1. Discover or request current artifacts: modern `.tcom`/`.tdom` container if available, otherwise compatibility composition/domain JSON, latest exported PNG/image, and log text.
2. Summarize the current model before changing it: composition/domain tech names, active/root view, definitions, idea/relationship counts, operation groups, warnings, and compatibility metadata.
3. Write the smallest standalone operations patch that satisfies the request.
4. Validate it, preview it through the CLI, then apply it to a separate output unless in-place replacement was explicitly requested.
5. Validate the result with `package inspect`, `composition validate-routing`, and `validate-json-persistence`. Preserve `gitSync` and `embeddedDomainGitSync` metadata.
6. Inspect the validation output and lower-left log for `warning`, `skipped`, `error`, `failed`, `blocked`, compatibility, rollback, and affected-view lines.
7. Ask for or find a fresh exported image, then visually inspect it before declaring the diagram done.
8. Iterate by applying a revised standalone patch to the latest canonical package, not by editing sidecars or legacy binaries.

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
