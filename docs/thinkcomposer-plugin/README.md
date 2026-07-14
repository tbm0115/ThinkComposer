# ThinkComposer Codex Plugin

Local Codex plugin for working with ThinkComposer diagrams through modern `.tcom`
and `.tdom` containers, authoritative root JSON payloads, application logs, and
embedded or exported view images.

## What It Adds

- A `thinkcomposer` Codex skill with the safe direct-package JSON workflow.
- A bundled `thinkcomposer-json-interchange` skill with the detailed schemas, samples, CLI reference, validation helper, and JSON patching rules.
- A local stdio MCP server for discovering `.tcom`/`.tdom` containers and exported artifacts, summarizing and validating root or sidecar JSON, extracting package JSON/screenshots, writing JSON patch documents, analyzing copied application logs, and finding recent embedded or exported images.
- A `.codex-plugin/plugin.json` manifest so the plugin can be packaged or referenced from a local marketplace entry.

## Normal Loop

1. Prefer a modern `.tcom` or `.tdom` container when available. Current saves include root `manifest.json`, `Composition.json`, `Domain.json`, optional `TemplateComposition.json`, `Interchange/*` sidecars, and `Previews/views/*.png`; old binary-only/transitional packages may still contain a legacy binary fallback until resaved.
2. Inspect root `/manifest.json` and the authoritative root JSON parts first. Treat `/Interchange/*` and previews as context only.
3. Let Codex prepare the smallest safe JSON update for root `/Composition.json`, `/Domain.json`, or `/TemplateComposition.json`.
4. Patch the native package root JSON and refresh `/manifest.json` authoritative part hashes and byte counts.
5. Use `thinkcomposer package inspect` and `validate-json-persistence` commands to verify the updated package. Use CLI import/export only as a compatibility or migration path.
6. Use the CLI for headless tasks that do not need visual editing: `thinkcomposer git status/pull/push`, `thinkcomposer report pdf`, `thinkcomposer output generate`, and the developer-only JSON persistence performance corpus/benchmark commands.
7. Use `Composition -> Domain -> Update Embedded Domain...` or the equivalent CLI path when a `.tcom` should pick up safe domain changes from a `.tdom`.
8. Copy or save the lower-left application log if there are warnings, skips, or errors.
9. Use embedded previews or export the active view as PNG with `Export Image` so Codex can visually verify the result.

The plugin is now expected to work against native `.tcom` and `.tdom` package JSON directly. New saves are JSON-only. Do not treat `/Interchange/*` sidecars as authoritative, and do not edit `/Composition.bin` or `/Domain.bin` when inspecting an older package. Container snapshot manifest v2 preview hashes describe optional verified PNG reuse; they never replace root JSON validation.

## Packaging

Run the repo-local packager from the repository root:

```powershell
python docs\thinkcomposer-plugin\scripts\package_thinkcomposer_plugin.py
```

It writes:

- `docs\thinkcomposer-json-interchange.zip`, with `SKILL.md` at the ZIP root for standalone skill installs.
- `docs\thinkcomposer-plugin.zip`, with `.codex-plugin`, `.mcp.json`, `skills`, and `scripts` at the ZIP root for plugin installs.
