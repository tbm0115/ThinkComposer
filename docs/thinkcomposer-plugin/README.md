# ThinkComposer Codex Plugin

Local Codex plugin for working with ThinkComposer diagrams through modern `.tcom`
containers, JSON interchange, application logs, and embedded or exported view images.

## What It Adds

- A `thinkcomposer` Codex skill with the safe export/import workflow.
- A bundled `thinkcomposer-json-interchange` skill with the detailed schemas, samples, validation helper, and JSON patching rules.
- A local stdio MCP server for discovering `.tcom` containers and exported artifacts, summarizing and validating embedded or standalone JSON, extracting embedded interchange/screenshots, writing import patches, analyzing copied application logs, and finding recent embedded or exported images.
- A `.codex-plugin/plugin.json` manifest so the plugin can be packaged or referenced from a local marketplace entry.

## Normal Loop

1. Prefer a modern `.tcom` container when available. It can include root `Composition.json`, root `Domain.json`, optional `Interchange/*` sidecars, and `Previews/views/*.png`.
2. If using an older file or a standalone interchange artifact is needed, export Composition JSON with `thinkcomposer composition export-json --input <file.tcom> --output <file.json>`.
3. Export Domain JSON first if composition changes depend on domain definitions, roles, tables, details, or output templates and it is not embedded in the `.tcom`.
4. Let Codex inspect the container or JSON and write a patch file.
5. Apply the Composition patch with `thinkcomposer composition import-json --input <file.tcom> --json <patch.json> --output <updated-file.tcom>`, or use `Domain > Import/Update Domain JSON...` for Domain patches.
6. Copy or save the lower-left application log if there are warnings, skips, or errors.
7. Use embedded `.tcom` previews or export the active view as PNG with `Export Image` so Codex can visually verify the result.

The plugin does not edit native `.tcom` or `.tdom` files directly. It works through ThinkComposer's safe CLI/package merge commands.

## Packaging

Run the repo-local packager from the repository root:

```powershell
python docs\thinkcomposer-plugin\scripts\package_thinkcomposer_plugin.py
```

It writes:

- `docs\thinkcomposer-json-interchange.zip`, with `SKILL.md` at the ZIP root for standalone skill installs.
- `docs\thinkcomposer-plugin.zip`, with `.codex-plugin`, `.mcp.json`, `skills`, and `scripts` at the ZIP root for plugin installs.
