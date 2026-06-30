# ThinkComposer Codex Plugin

Local Codex plugin for working with ThinkComposer diagrams through modern `.tcom`
containers, JSON interchange, application logs, and embedded or exported view images.

## What It Adds

- A `thinkcomposer` Codex skill with the safe export/import workflow.
- A bundled `thinkcomposer-json-interchange` skill with the detailed schemas, samples, validation helper, and JSON patching rules.
- A local stdio MCP server for discovering `.tcom` containers and exported artifacts, summarizing and validating embedded or standalone JSON, extracting embedded interchange/screenshots, writing import patches, analyzing copied application logs, and finding recent embedded or exported images.
- A `.codex-plugin/plugin.json` manifest so the plugin can be packaged or referenced from a local marketplace entry.

## Normal Loop

1. Prefer a modern `.tcom` container when available. It can include `Interchange/Composition.json`, `Interchange/Domain.json`, `Interchange/manifest.json`, and `Previews/views/*.png`.
2. If using an older file, export Composition JSON from ThinkComposer with `Composition > File > Export JSON...`.
3. Export Domain JSON first if composition changes depend on domain definitions, roles, tables, details, or output templates and it is not embedded in the `.tcom`.
4. Let Codex inspect the container or JSON and write a patch file.
5. Import the patch in ThinkComposer with `Composition > File > Import JSON...` or `Domain > Import/Update Domain JSON...`.
6. Copy or save the lower-left application log if there are warnings, skips, or errors.
7. Use embedded `.tcom` previews or export the active view as PNG with `Export Image` so Codex can visually verify the result.

The plugin does not edit native `.tcom` or `.tdom` files directly. It works through ThinkComposer's own safe merge commands.

## Packaging

Run the repo-local packager from the repository root:

```powershell
python docs\thinkcomposer-plugin\scripts\package_thinkcomposer_plugin.py
```

It writes:

- `docs\thinkcomposer-json-interchange.zip`, with `SKILL.md` at the ZIP root for standalone skill installs.
- `docs\thinkcomposer-plugin.zip`, with `.codex-plugin`, `.mcp.json`, `skills`, and `scripts` at the ZIP root for plugin installs.
