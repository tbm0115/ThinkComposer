# ThinkComposer Codex Plugin

Local Codex plugin for working with ThinkComposer diagrams through modern `.tcom`
and `.tdom` containers, authoritative root JSON payloads, application logs, and
embedded or exported view images.

## What It Adds

- A `thinkcomposer` Codex skill with a canonical-snapshot and standalone-patch workflow.
- A bundled `thinkcomposer-json-interchange` skill with the detailed Composition JSON v2 and Domain JSON v2 schemas, samples, CLI reference, validation helper, and JSON patching rules.
- A local stdio MCP server for discovering `.tcom`/`.tdom` containers and exported artifacts, classifying snapshot versus patch JSON, writing and safely previewing/applying standalone patches, reporting route health, exporting the resulting view image, extracting package JSON/screenshots, analyzing copied application logs, and finding recent images.
- A `.codex-plugin/plugin.json` manifest so the plugin can be packaged or referenced from a local marketplace entry.

## Normal Loop

1. Prefer a modern `.tcom` or `.tdom` container when available. Current saves include root `manifest.json`, `Composition.json`, `Domain.json`, optional `TemplateComposition.json`, `Interchange/*` sidecars, and `Previews/views/*.png`; old binary-only/transitional packages may still contain a legacy binary fallback until resaved.
2. Inspect root `/manifest.json` and the authoritative root JSON parts first. Treat `/Interchange/*` and previews as context only.
3. For Composition changes, let Codex prepare the smallest standalone `operations[]` patch. Do not splice `importOptions`, `visualStrategy`, or operations into authoritative `/Composition.json` snapshot state.
4. Preview and apply the patch with `thinkcomposer_apply_patch` or `thinkcomposer composition import-json`; the application writes a canonical snapshot and refreshes `/manifest.json` safely. Direct root edits are reserved for exact snapshot recovery or deliberate expert maintenance.
5. Use `thinkcomposer package inspect`, `composition validate-routing`, and `validate-json-persistence` to verify the updated package.
6. Use the CLI for headless tasks that do not need visual editing: `thinkcomposer git status/pull/push`, `thinkcomposer report pdf`, `thinkcomposer output generate`, and the developer-only JSON persistence performance corpus/benchmark commands.
7. Use `Composition -> Domain -> Update Embedded Domain...` or the equivalent CLI path when a `.tcom` should pick up safe domain changes from a `.tdom`.
8. Copy or save the lower-left application log if there are warnings, skips, or errors.
9. Use embedded previews or export the active view as PNG with `Export Image` so Codex can visually verify the result.

The plugin treats native root JSON as canonical saved state, not as a place to embed generated edit directives. New saves are JSON-only. Do not treat `/Interchange/*` sidecars as authoritative, and do not edit `/Composition.bin` or `/Domain.bin` when inspecting an older package. Container snapshot manifest v2 preview hashes describe optional verified PNG reuse; they never replace root JSON validation.

Domain JSON v2 persists ordered `detailDesignators` on Concept and Relationship Definitions, including stable designator identity and shared Domain table references. Composition details must reuse the exported designator `id`; synthesizing an instance-only Table Detail cannot survive a save/reopen cycle. Domain JSON v1 remains readable as migration input, while current exports emit v2 so older applications reject unsupported definition-detail state instead of silently dropping it.

## Packaging

Run the repo-local packager from the repository root:

```powershell
python docs\thinkcomposer-plugin\scripts\package_thinkcomposer_plugin.py
```

It writes:

- `docs\thinkcomposer-json-interchange.zip`, with `SKILL.md` at the ZIP root for standalone skill installs.
- `docs\thinkcomposer-plugin.zip`, with `.codex-plugin`, `.mcp.json`, `skills`, and `scripts` at the ZIP root for plugin installs.
