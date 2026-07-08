# Command-Line Interface

ThinkComposer includes a headless command-line interface for repeatable import, export, package persistence validation, report, and output-generation work. The desktop application remains the primary visual editor. Use the CLI when you want the same operations available from Command Prompt, scripts, build jobs, or other automation.

Modern `.tcom` and `.tdom` files use root JSON payloads as their native persistence source of truth. Manual JSON exports are still exchange artifacts, while PDF, XPS, and generated files are publication/output artifacts.

Related manual topics:

- [Overview](01-overview.md) explains how compositions and domains fit into a normal ThinkComposer workflow.
- [Base Model](02-base-model.md) defines compositions, domains, ideas, relationships, output templates, and external languages.
- [Application Guide](03-application-guide.md) covers installation, working with compositions, reporting, and the desktop commands mirrored by the CLI.
- [Current Features](04-current-features.md) explains JSON interchange, Domain JSON, embedded-domain updates, and output template generation.
- [Template Language](05-template-language.md) and [Composition Information Model](06-information-model.md) describe the template model used by generated output.

Detailed technical references are also available for [Composition JSON Interchange](../json-interchange.md), [Domain JSON Interchange](../domain-json-interchange.md), [Domain Sync](../domain-sync.md), [Output Template Generation](../output-template-generation.md), and the compact [CLI reference](../cli.md).

## When To Use The CLI

Use the CLI for:

- exporting a composition to JSON before external review or AI-assisted editing
- importing reviewed Composition JSON back into a saved `.tcom`
- exporting a native `.tdom` domain or a composition's embedded domain to JSON
- importing Domain JSON into a `.tdom` or into the embedded domain of a `.tcom`
- inspecting, converting, and validating JSON-authoritative `.tcom` and `.tdom` packages
- generating a standard composition report as PDF or XPS
- generating files from output templates without opening the desktop shell

Use the desktop application when you need to visually edit diagrams, inspect a composition, tune report settings, design output templates, or review import diagnostics interactively.

## Installation And PATH

The installer deploys the command-line files beside the desktop application:

- `ThinkComposer.Cli.exe`
- `thinkcomposer.cmd`
- `thinkcomposer-path.cmd`
- `thinkcomposer-path.ps1`

The installer attempts to add the ThinkComposer install folder to the machine `Path` so `thinkcomposer` works from a new Command Prompt. Open a new Command Prompt after installation and test:

```cmd
thinkcomposer --help
where thinkcomposer
```

If `thinkcomposer` is not recognized, use the installed helper. From the ThinkComposer Start Menu folder, run **Add ThinkComposer to PATH**. You can also open Command Prompt in the ThinkComposer installation folder and run:

```cmd
thinkcomposer --add-to-path
```

The default helper updates the machine `Path` and may prompt for administrator elevation. To update only the current user's `Path`, run:

```cmd
thinkcomposer --add-to-path -User
```

Check or remove the entry with:

```cmd
thinkcomposer --path-status
thinkcomposer --path-status -User
thinkcomposer --remove-from-path
thinkcomposer --remove-from-path -User
```

Open a new Command Prompt after adding or removing a `Path` entry. Existing terminal windows usually keep the old environment.

In PowerShell, if you are already in the installation folder and the command is not on `Path` yet, prefix the shim with `.\`:

```powershell
.\thinkcomposer --add-to-path -User
```

## General Syntax

Use global or command-specific help:

```cmd
thinkcomposer --help
thinkcomposer composition --help
thinkcomposer domain --help
thinkcomposer report --help
thinkcomposer output --help
```

Quote paths that contain spaces:

```cmd
thinkcomposer composition export-json --input "C:\Work Models\Service Map.tcom" --output "C:\Exports\Service Map.json"
```

Exit codes:

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Usage, validation, or expected operation failure. |
| `2` | Unexpected exception. |

Headless commands initialize ThinkComposer services and drawing resources without showing the WPF shell. Report generation still uses WPF document primitives internally, but no application window is created.

## Import Safety

Imports always require `--output`. The CLI refuses to overwrite the input file unless both conditions are true:

1. `--in-place` is present.
2. `--output` is the same path as `--input`.

Use `--preview-only` to validate the JSON and print the planned import summary without saving any document. A preview still requires `--output` so the command line is the same as the eventual import.

For important work, write imports to a new output file first, open the result in ThinkComposer, and only then decide whether to replace the original.

## Composition JSON

Composition JSON export writes an editable interchange document from a `.tcom` composition:

```cmd
thinkcomposer composition export-json --input "Models\ServiceMap.tcom" --output "Exports\ServiceMap.composition.json"
```

Composition JSON import merges a JSON document back into a composition and writes a `.tcom` output:

```cmd
thinkcomposer composition import-json --input "Models\ServiceMap.tcom" --json "Patches\ServiceMap.patch.json" --output "Models\ServiceMap.updated.tcom"
```

Preview the same operation without saving:

```cmd
thinkcomposer composition import-json --input "Models\ServiceMap.tcom" --json "Patches\ServiceMap.patch.json" --output "Models\ServiceMap.updated.tcom" --preview-only
```

Overwrite the input only when that is intentional:

```cmd
thinkcomposer composition import-json --input "Models\ServiceMap.tcom" --json "Patches\ServiceMap.patch.json" --output "Models\ServiceMap.tcom" --in-place
```

For the JSON model, patch operations, visual strategies, and diagnostics, see [Composition JSON Interchange](04-current-features.md#composition-json-interchange) and the detailed [Composition JSON Interchange reference](../json-interchange.md).

## Domain JSON

Domain JSON export works with native `.tdom` domain files:

```cmd
thinkcomposer domain export-json --input "Domains\ServiceDesign.tdom" --output "Exports\ServiceDesign.domain.json"
```

It can also export the embedded domain snapshot from a `.tcom` composition:

```cmd
thinkcomposer domain export-json --input "Models\ServiceMap.tcom" --output "Exports\ServiceMap.embedded-domain.json"
```

Import Domain JSON into a native domain:

```cmd
thinkcomposer domain import-json --input "Domains\ServiceDesign.tdom" --json "Patches\ServiceDesign.domain.json" --output "Domains\ServiceDesign.updated.tdom"
```

Import Domain JSON into a composition's embedded domain:

```cmd
thinkcomposer domain import-json --input "Models\ServiceMap.tcom" --json "Patches\ServiceDesign.domain.json" --output "Models\ServiceMap.updated.tcom"
```

Preview or in-place behavior follows the same safety rules as Composition JSON import:

```cmd
thinkcomposer domain import-json --input "Domains\ServiceDesign.tdom" --json "Patches\ServiceDesign.domain.json" --output "Domains\ServiceDesign.updated.tdom" --preview-only
thinkcomposer domain import-json --input "Domains\ServiceDesign.tdom" --json "Patches\ServiceDesign.domain.json" --output "Domains\ServiceDesign.tdom" --in-place
```

For domain concepts, see [Domains](02-base-model.md#domains), [Output Templates](02-base-model.md#output-templates), and [External Languages](02-base-model.md#external-languages). For merge behavior, see [Domain JSON Interchange](04-current-features.md#domain-json-interchange), [Embedded Domain Updates](04-current-features.md#embedded-domain-updates), and the detailed [Domain JSON Interchange reference](../domain-json-interchange.md).

## Package Persistence

Inspect a native package:

```cmd
thinkcomposer package inspect --input "Models\ServiceMap.tcom"
```

Convert a legacy binary-backed composition or domain to the JSON-authoritative package format:

```cmd
thinkcomposer composition convert-json-persistence --input "Models\LegacyMap.tcom" --output "Models\LegacyMap.json-persistence.tcom"
thinkcomposer domain convert-json-persistence --input "Domains\LegacyDomain.tdom" --output "Domains\LegacyDomain.json-persistence.tdom"
```

Validate normal JSON-first load/save behavior:

```cmd
thinkcomposer composition validate-json-persistence --input "Models\ServiceMap.tcom" --output-dir "Validation\ServiceMap"
thinkcomposer domain validate-json-persistence --input "Domains\ServiceDesign.tdom" --output-dir "Validation\ServiceDesign"
```

The validation commands save a modern package, reopen it through normal load, fail if binary fallback was used, save again, and compare canonical root JSON payloads.

## Reports

Generate a standard composition report as PDF:

```cmd
thinkcomposer report pdf --input "Models\ServiceMap.tcom" --output "Reports\ServiceMap.pdf"
```

You can also write the intermediate XPS format:

```cmd
thinkcomposer report pdf --input "Models\ServiceMap.tcom" --output "Reports\ServiceMap.xps"
```

This uses the existing ThinkComposer standard report workflow and the saved or default report configuration. If the report layout or included sections are not what you expect, open the composition in the desktop application and review the reporting workflow described in [Reporting](03-application-guide.md#reporting).

## Output Generation

Generate files from output templates:

```cmd
thinkcomposer output generate --input "Models\ServiceMap.tcom" --output-dir "Generated\ServiceMap" --language "Xml"
```

The `--language` value is the external language TechName from the composition's embedded domain. If you are not sure which language names are available, export Domain JSON and inspect its external-language entries, or review the domain in the desktop application.

Common options:

| Option | Meaning |
|---|---|
| `--relationships` | Also emit standalone relationship files when templates allow them. |
| `--composition-root-dir` | Create a composition-root directory inside the target output directory. |
| `--use-tech-names` | Use TechNames as programming identifiers during generation. |
| `--exclude <idea-id>` | Exclude an idea by GlobalId or TechName. Repeat the option to exclude more than one idea. |

Example with options:

```cmd
thinkcomposer output generate --input "Models\ServiceMap.tcom" --output-dir "Generated\ServiceMap" --language "Xml" --relationships --composition-root-dir --use-tech-names --exclude "LegacyNode"
```

Output generation uses the same template preparation, linting, subtemplate registration, and post-processing path described in [Output Template Generation](04-current-features.md#output-template-generation). Template syntax is documented in [Template Language](05-template-language.md), and the data exposed to templates is summarized in [Composition Information Model](06-information-model.md).

## Troubleshooting

If `thinkcomposer` is not recognized, open a new Command Prompt first. If it is still unavailable, run **Add ThinkComposer to PATH** from the Start Menu or run `thinkcomposer --add-to-path` from the installation folder.

If a new terminal still resolves an old install, run:

```cmd
where thinkcomposer
```

Then remove the stale folder from `Path` or run `thinkcomposer --remove-from-path` from the old installation folder before adding the current one.

If import fails because JSON is invalid, export a fresh JSON document from the same source file and compare the format header, `formatVersion`, identifiers, and patch operations. Use `--preview-only` before saving.

If output generation reports an unknown language TechName, export Domain JSON from the same `.tcom` or `.tdom` and confirm the intended external language TechName.

If report generation writes an empty or unexpected report, open the composition in the desktop application and verify that the composition opens correctly and that the report configuration is suitable for that document.
