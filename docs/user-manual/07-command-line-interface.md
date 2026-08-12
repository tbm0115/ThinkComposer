# Command-Line Interface

ThinkComposer includes a headless command-line interface for repeatable import, export, package persistence validation, report, and output-generation work. The desktop application remains the primary visual editor. Use the CLI when you want the same operations available from Command Prompt, scripts, build jobs, or other automation.

Modern `.tcom` and `.tdom` files use root JSON payloads as their native persistence source of truth. Manual JSON exports are still exchange artifacts, while image exports, PDF, XPS, and generated files are publication/output artifacts.

Related manual topics:

- [Overview](01-overview.md) explains how compositions and domains fit into a normal ThinkComposer workflow.
- [Base Model](02-base-model.md) defines compositions, domains, ideas, relationships, output templates, and external languages.
- [Application Guide](03-application-guide.md) covers installation, working with compositions, reporting, and the desktop commands mirrored by the CLI.
- [Current Features](04-current-features.md) explains JSON interchange, Domain JSON, Git sync, embedded-domain updates, and output template generation.
- [Template Language](05-template-language.md) and [Composition Information Model](06-information-model.md) describe the template model used by generated output.

Detailed technical references are also available for [Composition JSON Interchange](../json-interchange.md), [Domain JSON Interchange](../domain-json-interchange.md), [Domain Sync](../domain-sync.md), [Output Template Generation](../output-template-generation.md), and the compact [CLI reference](../cli.md).

## When To Use The CLI

Use the CLI for:

- exporting a composition to JSON for compatibility review
- exporting a fitted image of the main view or a selected view area
- importing reviewed Composition JSON through the compatibility merge path
- validating Relationship hubs and multi-point connector routes after edits or layouts
- exporting a native `.tdom` domain or a composition's embedded domain to JSON for compatibility review
- importing Domain JSON through the compatibility merge path
- updating a composition's embedded domain directly from a native `.tdom`
- inspecting, converting, and validating JSON-authoritative `.tcom` and `.tdom` packages
- preparing and benchmarking a reproducible JSON-persistence performance corpus
- linking packages to Git remotes, pulling linked packages, and pushing linked compositions
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
thinkcomposer git --help
thinkcomposer report --help
thinkcomposer output --help
thinkcomposer performance --help
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

Treat the export and root `/Composition.json` as snapshots for inspection. For an edit, write a separate document containing `operations[]`; do not splice pending directives into authoritative snapshot state. Composition JSON import applies that standalone patch and writes a `.tcom` output:

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

Modern `.tcom` persistence uses root `/Composition.json` and `/Domain.json` as the native source of truth. Native load restores saved visual state exactly and does not execute `importOptions` or `visualStrategy`. The safe GPT workflow is: inspect/export, write a standalone operations patch, preview it, apply through the CLI, validate routing, then export an image for visual review. Generated Relationships should request `autoRoute:true` and endpoint-corridor hub placement while omitting explicit route coordinates.

For the JSON model, patch operations, visual strategies, and diagnostics, see [Composition JSON Interchange](04-current-features.md#composition-json-interchange) and the detailed [Composition JSON Interchange reference](../json-interchange.md).

## Relationship Routing Validation

Validate route health after applying a generated patch or running a layout:

```cmd
thinkcomposer composition validate-routing --input "Models\ServiceMap.updated.tcom" --output-dir "Validation\ServiceMap" --layout route
```

`--layout` accepts `route`, `spider`, `hierarchy`, `flowchart`, or `system`. The validator checks nonfinite or oversized route-point collections, Relationship hubs far outside their endpoint corridor, excessive detours, stale endpoints, and ambiguous connector identities. A saved package has no route-authorship marker, so GPT-authored coordinates are rejected earlier by the plugin patch validator rather than inferred here. The command emits structured diagnostics and routing artifacts to the output directory without changing unrelated hand-routed links.

The planner is deterministic: repeated runs with the same model and layout profile produce the same route decisions. Any degraded direct fallback is reported explicitly. Use `composition export-image` on the resulting package to complete visual review.

## Composition Image Export

Composition image export writes a fitted raster image from a `.tcom` composition. By default, it exports the root or main view fitted into a 1600x1200 image:

```cmd
thinkcomposer composition export-image --input "Models\ServiceMap.tcom" --output "Exports\ServiceMap.main.png"
```

Use `--view` to export a specific view by TechName:

```cmd
thinkcomposer composition export-image --input "Models\ServiceMap.tcom" --output "Exports\ServiceMap.system.png" --view "SystemMap"
```

Use repeated `--fit` values to fit the export viewport around specific visible idea TechNames on the chosen view. `--fit-tech-name` is accepted as an explicit alias.

```cmd
thinkcomposer composition export-image --input "Models\ServiceMap.tcom" --output "Exports\ServiceMap.slice.png" --view "SystemMap" --fit "Customer" --fit "Service" --width 1920
```

Supported raster extensions are `.png`, `.jpg`, `.jpeg`, `.gif`, `.tif`, `.tiff`, and `.bmp`. If only `--width` or `--height` is supplied, ThinkComposer infers the other dimension from the fitted source area. `--padding` controls source-area padding around fitted TechNames; the default is 20. `--transparent` keeps the background transparent when the selected image format supports alpha, such as PNG.

## Domain JSON

Domain JSON export works with native `.tdom` domain files as a compatibility/interchange command:

```cmd
thinkcomposer domain export-json --input "Domains\ServiceDesign.tdom" --output "Exports\ServiceDesign.domain.json"
```

It can also export the embedded domain snapshot from a `.tcom` composition:

```cmd
thinkcomposer domain export-json --input "Models\ServiceMap.tcom" --output "Exports\ServiceMap.embedded-domain.json"
```

Import Domain JSON into a native domain through the compatibility merge path:

```cmd
thinkcomposer domain import-json --input "Domains\ServiceDesign.tdom" --json "Patches\ServiceDesign.domain.json" --output "Domains\ServiceDesign.updated.tdom"
```

Import Domain JSON into a composition's embedded domain through the compatibility merge path:

```cmd
thinkcomposer domain import-json --input "Models\ServiceMap.tcom" --json "Patches\ServiceDesign.domain.json" --output "Models\ServiceMap.updated.tcom"
```

Update a composition's embedded domain directly from a native `.tdom` source:

```cmd
thinkcomposer domain update-embedded --input "Models\ServiceMap.tcom" --domain "Domains\ServiceDesign.tdom" --output "Models\ServiceMap.updated.tcom"
```

Preview or in-place behavior follows the same safety rules as Composition JSON import:

```cmd
thinkcomposer domain import-json --input "Domains\ServiceDesign.tdom" --json "Patches\ServiceDesign.domain.json" --output "Domains\ServiceDesign.updated.tdom" --preview-only
thinkcomposer domain import-json --input "Domains\ServiceDesign.tdom" --json "Patches\ServiceDesign.domain.json" --output "Domains\ServiceDesign.tdom" --in-place
thinkcomposer domain update-embedded --input "Models\ServiceMap.tcom" --domain "Domains\ServiceDesign.tdom" --output "Models\ServiceMap.updated.tcom" --preview-only
```

Modern `.tdom` persistence uses root `/Domain.json` as the native source of truth. Use CLI domain import/export when you need a compatibility merge, preview, or standalone interchange document; patch root package JSON directly for JSON-authoritative persistence edits.

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

The validation commands save a modern package, reopen it through normal load, fail if binary fallback was used, save again, compare canonical root JSON payloads, and verify that the result is binary-free. New saves omit `/Composition.bin` and `/Domain.bin`; opening and resaving an older binary-backed package is its migration to JSON-only persistence.

## JSON Persistence Performance

The performance commands are developer diagnostics for reproducible load/save measurement. Development mode is the default and permits a repository-only corpus. Certification mode requires at least one sanitized slow package, tags it for the splash gate, and combines it with the repository examples, predefined Domains, and deterministic large synthetic cases:

```cmd
thinkcomposer performance prepare-json-persistence-corpus --source-root "C:\src\ThinkComposer" --output-dir "C:\bench\tc-corpus" --mode certification --real-package "C:\bench\sanitized-slow.tcom"
```

Run one warmup and five measured iterations:

```cmd
thinkcomposer performance benchmark-json-persistence --corpus "C:\bench\tc-corpus\corpus.json" --output "C:\bench\baseline.json" --warmup 1 --iterations 5 --allow-legacy-baseline-output
```

To enforce the standard target, compare a candidate on the same machine and unchanged corpus:

```cmd
thinkcomposer performance benchmark-json-persistence --corpus "C:\bench\tc-corpus\corpus.json" --output "C:\bench\candidate.json" --warmup 1 --iterations 5 --baseline "C:\bench\baseline.json" --minimum-speedup 2.0
```

Each sample runs in a fresh process. The report records authoritative-payload and whole-package SHA-256 hashes, exact byte lengths, machine details, raw stage timings, per-case and aggregate median/p95 load, first-save and steady-save measurements, per-sample output validation, plus splash first-paint, heartbeat-gap, and clean-shutdown telemetry. Corpus validation rejects any package whose full hash or actual byte length changed after preparation. Use `--allow-legacy-baseline-output` only for a pre-optimization baseline whose JSON-authoritative save still retains the matching legacy binary fallback. Baseline workers do not require the candidate-only v2 preview-reuse contract; candidate workers do, and every measured candidate package is inspected for strict JSON-only output. JSON/hash parity is still required, binary-only or unrelated binary output is rejected, and the option cannot be combined with `--baseline`.

A `2.0` gate requires both aggregate median load and first-save time to be no more than half of the baseline. Certification corpora apply the splash gate to every tagged sanitized slow package; development corpora apply it to all cases. Each selected splash must paint within 250 ms, keep heartbeat gaps within 500 ms, and stop its dispatcher cleanly. Use `--skip-splash-responsiveness-gate` only when a constrained CI/headless environment cannot host the splash; telemetry then remains diagnostic.

For final certification, capture separate baseline and candidate Windows Performance Recorder traces around the sanitized-package benchmark with CPU, file/disk I/O, allocation/.NET activity, and UI/WPF responsiveness enabled, then inspect them in Windows Performance Analyzer. Keep sanitized customer packages and trace files outside version control.

## Git Sync

Git sync requires `git.exe` to be installed and available on `Path`. ThinkComposer uses normal Git remotes and your existing Git credentials or SSH configuration. It does not store passwords, tokens, GitHub credentials, or Bitbucket credentials. Package-level links are stored as `gitSync`; Composition packages can also store `embeddedDomainGitSync` for the source `.tdom` link copied from a Git-linked Domain.

Link a composition to a Git remote and repo-relative `.tcom` path:

```cmd
thinkcomposer git link --input "Models\ServiceMap.tcom" --remote "https://example.com/team/models.git" --branch main --path "compositions/ServiceMap.tcom" --output "Models\ServiceMap.tcom" --in-place
```

If the composition also has a related Domain source in the same repository, include `--domain-path`:

```cmd
thinkcomposer git link --input "Models\ServiceMap.tcom" --remote "https://example.com/team/models.git" --branch main --path "compositions/ServiceMap.tcom" --domain-path "domains/ServiceDesign.tdom" --output "Models\ServiceMap.tcom" --in-place
```

Link a standalone Domain package:

```cmd
thinkcomposer git link --input "Domains\ServiceDesign.tdom" --remote "https://example.com/team/models.git" --branch main --path "domains/ServiceDesign.tdom" --output "Domains\ServiceDesign.tdom" --in-place
```

Inspect the link and remote status:

```cmd
thinkcomposer git status --input "Models\ServiceMap.tcom"
```

Pull a linked package. In-place pull creates a backup before replacing the package. If `--backup-dir` is omitted, the backup is stored in the ThinkComposer user application data folder under `GitSync\backups`; temporary pull staging files are stored under `GitSync\temp`, not beside the `.tcom` or `.tdom`.

```cmd
thinkcomposer git pull --input "Models\ServiceMap.tcom" --output "Models\ServiceMap.tcom" --in-place
```

Push is supported for Composition packages:

```cmd
thinkcomposer git push --input "Models\ServiceMap.tcom" --message "Update service map"
```

For a new blank remote repository, link the package and push first. ThinkComposer creates the linked branch and baseline package path during the first push. Pull requires the linked branch and package path to exist already, and reports a warning when the remote is still empty.

Domain packages are pull-only in this version. To update a Composition's embedded Domain after pulling a `.tdom`, use `thinkcomposer domain update-embedded`. In the desktop UI, Domain `Pull from Git` can use a Composition package's `embeddedDomainGitSync` link to pull and merge the embedded Domain source directly.

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

The `--language` value is the external language TechName from the composition's embedded domain. If you are not sure which language names are available, inspect root `/Domain.json`, run `thinkcomposer domain export-json` as a compatibility export, or review the domain in the desktop application.

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

If import fails because JSON is invalid, export a fresh JSON document from the same source file and compare the format header, `formatVersion`, identifiers, and patch operations. Composition v2 adds `routePoints`; v1 remains readable, but older applications reject v2 rather than discarding multi-point geometry. Use `--preview-only` before saving.

If output generation reports an unknown language TechName, inspect root `/Domain.json` from the same `.tcom` or `.tdom`, or run `thinkcomposer domain export-json` as a compatibility export, and confirm the intended external language TechName.

If report generation writes an empty or unexpected report, open the composition in the desktop application and verify that the composition opens correctly and that the report configuration is suitable for that document.
