# ThinkComposer Headless CLI

ThinkComposer now includes a console executable named `ThinkComposer.Cli.exe` for headless automation. The desktop WPF application remains unchanged for normal launches. Installer builds also deploy a `thinkcomposer.cmd` shim beside the executable.

For user-facing installation, PATH, safety, and workflow guidance, see [Command-Line Interface](user-manual/07-command-line-interface.md) in the user manual. This page is the compact technical reference for the command surface.

## Commands

```cmd
thinkcomposer composition export-json --input <file.tcom> --output <file.json>
thinkcomposer composition export-image --input <file.tcom> --output <file.png|file.jpg|file.gif|file.tif|file.bmp> [--view <view-tech-name>] [--fit <idea-tech-name>] [--width <px>] [--height <px>] [--padding <px>] [--transparent]
thinkcomposer composition import-json --input <file.tcom> --json <file.json> --output <file.tcom> [--in-place] [--preview-only]
thinkcomposer composition validate-routing --input <file.tcom> --output-dir <dir> [--layout <route|spider|hierarchy|flowchart|system>]
thinkcomposer composition validate-json-roundtrip --input <file.tcom> --output-dir <dir>
thinkcomposer composition convert-json-persistence --input <file.tcom> --output <file.tcom>
thinkcomposer composition validate-json-persistence --input <file.tcom> --output-dir <dir>
thinkcomposer domain export-json --input <file.tdom|file.tcom> --output <file.json>
thinkcomposer domain import-json --input <file.tdom|file.tcom> --json <file.json> --output <file.tdom|file.tcom> [--in-place] [--preview-only]
thinkcomposer domain update-embedded --input <file.tcom> --domain <file.tdom> --output <file.tcom> [--in-place] [--preview-only]
thinkcomposer domain validate-json-roundtrip --input <file.tdom|file.tcom> --output-dir <dir>
thinkcomposer domain convert-json-persistence --input <file.tdom> --output <file.tdom>
thinkcomposer domain validate-json-persistence --input <file.tdom> --output-dir <dir>
thinkcomposer package inspect --input <file.tcom|file.tdom>
thinkcomposer git link --input <file.tcom|file.tdom> --remote <url> --branch <branch> --path <repo-path> [--domain-path <repo-tdom-path>] --output <file> [--in-place]
thinkcomposer git unlink --input <file.tcom|file.tdom> --output <file> [--in-place]
thinkcomposer git status --input <file.tcom|file.tdom>
thinkcomposer git pull --input <file.tcom|file.tdom> --output <file> [--in-place] [--backup-dir <dir>]
thinkcomposer git push --input <file.tcom> --message <message>
thinkcomposer report pdf --input <file.tcom> --output <file.pdf|file.xps>
thinkcomposer output generate --input <file.tcom> --output-dir <dir> --language <language-tech-name> [--relationships] [--composition-root-dir] [--use-tech-names] [--exclude <idea-id-or-tech-name>]
thinkcomposer performance prepare-json-persistence-corpus --source-root <repo> --output-dir <dir> [--mode <development|certification>] [--real-package <sanitized-slow-file>]...
thinkcomposer performance benchmark-json-persistence --corpus <dir>\corpus.json --output <report.json> [--warmup 1] [--iterations 5] [--baseline <report.json>] [--minimum-speedup 2.0] [--allow-legacy-baseline-output] [--skip-splash-responsiveness-gate]
```

Use `thinkcomposer --help`, `thinkcomposer composition --help`, or `thinkcomposer composition export-json --help` for command-line help.

## PATH Helper

If `thinkcomposer` is not available from a new Command Prompt after installation, open **Add ThinkComposer to PATH** from the ThinkComposer Start Menu folder, or run this from the install folder:

```cmd
thinkcomposer --add-to-path
```

The helper updates the machine `Path` idempotently and prompts for administrator elevation when needed. A new Command Prompt is required after the update. Related commands:

```cmd
thinkcomposer --path-status
thinkcomposer --remove-from-path
```

Pass `-User` after any helper command to update or check only the current user's `Path` instead of the machine `Path`.

## Import Safety

Imports always require `--output`. The CLI refuses to overwrite the input path unless `--in-place` is also present and `--output` matches `--input`. `--preview-only` validates the input JSON and prints the planned import summary without saving any document.

`domain update-embedded` is the CLI equivalent of `Composition -> Domain -> Update Embedded Domain...` for native `.tdom` sources. It previews or applies the safe embedded-domain merge, then writes a `.tcom` output.

For GPT-assisted Composition edits, keep `/Composition.json` as an exact authoritative snapshot. Write a standalone `operations[]` patch, preview it, then apply the same patch through the safe output or explicit in-place path. Import directives embedded in root snapshot state are a legacy compatibility case, not the normal edit workflow.

```cmd
thinkcomposer composition import-json --input model.tcom --json change.patch.json --output model.updated.tcom --preview-only
thinkcomposer composition import-json --input model.tcom --json change.patch.json --output model.updated.tcom
thinkcomposer composition validate-routing --input model.updated.tcom --output-dir routing-check
thinkcomposer composition export-image --input model.updated.tcom --output routing-check\result.png
```

Generated Relationship operations should request `autoRoute:true` and `visual.relationshipCenterPlacement:"endpointCorridor"`; omit explicit hub coordinates and connector route points.

## Routing Validation

`composition validate-routing` inspects Relationship hubs and connector geometry without rewriting unrelated manual routes. It reports nonfinite or oversized route-point collections, distant hubs, excessive detours, stale endpoints, and ambiguous connector identities. Persisted packages do not record who authored a route, so the CLI cannot infer GPT authorship; the plugin patch validator separately rejects generated `routePoints`/`intermediatePosition` by default. The output directory receives structured route-health diagnostics and, when supported by the selected profile, before/after route JSON and view images.

Use `--layout route` for route-only validation, or `spider`, `hierarchy`, `flowchart`, or `system` to exercise the shared routing coordinator after that layout's placement policy. Runs are deterministic and suitable for idempotence and save/reopen checks. A degraded direct fallback is reported explicitly rather than silently accepting stale geometry.

## Image Export

`composition export-image` opens a `.tcom` composition and writes a fitted raster image of a view. By default it exports the root/main view fitted into a 1600x1200 image:

```cmd
thinkcomposer composition export-image --input model.tcom --output exports\model-main.png
```

Use `--view <view-tech-name>` to export another view. Use repeated `--fit <idea-tech-name>` values to fit the export viewport around specific visible idea TechNames on that view. `--fit-tech-name` is accepted as a clearer alias for `--fit`.

```cmd
thinkcomposer composition export-image --input model.tcom --output exports\service-slice.png --view SystemMap --fit Customer --fit Service --width 1920
```

If only one of `--width` or `--height` is supplied, the other dimension is inferred from the fitted source area. `--padding <px>` controls source-area padding around fitted TechNames; the default is 20. `--transparent` keeps the background transparent when the chosen output format supports alpha, such as PNG.

## Package Persistence

`package inspect` reports whether a `.tcom` or `.tdom` is JSON-authoritative, transitional with a binary fallback, or legacy binary-only.

`composition convert-json-persistence` and `domain convert-json-persistence` open legacy packages through the normal loader and save a modern JSON-authoritative package.

New saves are JSON-only: `.tcom` packages do not write `/Composition.bin`, `.tdom` packages do not write `/Domain.bin`, and the root manifest records `legacyBinaryFallback.present: false`. Binary-only and transitional packages remain readable. Resaving one migrates it to JSON-only persistence. A corrupt root JSON payload falls back only when the matching legacy binary part is physically present; otherwise the open fails with the JSON diagnostic.

`composition validate-json-persistence` and `domain validate-json-persistence` save a JSON-authoritative package, reopen it through normal load, assert that JSON was used instead of binary fallback, save again, compare canonical root JSON payloads, and verify the result is binary-free.

## JSON Persistence Performance

Prepare a reproducible corpus from the repository examples, predefined Domains, optional sanitized real-world packages, and deterministic large synthetic Composition and Domain cases:

```cmd
thinkcomposer performance prepare-json-persistence-corpus --source-root C:\src\ThinkComposer --output-dir C:\bench\tc-corpus --mode certification --real-package C:\bench\sanitized-slow.tcom
```

`--mode development` is the default and permits a repository-only developer corpus. `--mode certification` requires at least one repeatable `--real-package`; each such input is tagged as a sanitized slow package so the report can identify the required splash-responsiveness cases. Corpus preparation converts every source through normal persistence, records authoritative-payload and whole-package SHA-256 hashes, exact package byte lengths, and model counts, and writes `corpus.json` plus package cases beneath the output directory. Benchmark startup rejects any case whose package hash or actual byte length changed after preparation.

Benchmark loading, the first save, and a second steady-state save:

```cmd
thinkcomposer performance benchmark-json-persistence --corpus C:\bench\tc-corpus\corpus.json --output C:\bench\baseline.json --warmup 1 --iterations 5 --allow-legacy-baseline-output
thinkcomposer performance benchmark-json-persistence --corpus C:\bench\tc-corpus\corpus.json --output C:\bench\candidate.json --warmup 1 --iterations 5 --baseline C:\bench\baseline.json --minimum-speedup 2.0
```

Each measured sample runs in a fresh CLI process and copies its immutable source before timing. Reports include the executable, authoritative-payload, and whole-package corpus hashes, exact source/output byte lengths, machine/CLR/process-architecture fingerprint, raw per-case samples, splash first-paint/heartbeat/dispatcher-stop telemetry, aggregate median and nearest-rank p95, and per-sample persistence validation results. Raw `stageTimings` entries prefixed with `firstSave.` and `steadySave.` separately report `save-export-dto`, `save-json-serialization-hash`, `save-preview-cache-read`, `save-preview-input-hash`, `save-preview-render`, `save-preview-reuse`, `save-required-package-write`, `save-package-close`, `save-safe-replacement`, and `save-optional-sidecars`. The required-writer span contains its export/serialization sub-spans, and the optional-sidecar span contains its preview hash/render/reuse sub-spans, so nested values diagnose their parent and must not be summed as disjoint total time. Use `--allow-legacy-baseline-output` only when recording a pre-optimization baseline whose save writer retains the exact matching `/Composition.bin` or `/Domain.bin` beside authoritative root JSON. Baseline workers measure the second save without requiring the candidate-only v2 preview-reuse contract; candidate workers require and report that contract. Baseline mode still requires JSON authority and authoritative-hash parity, and rejects binary-only output or an unrelated binary part. It cannot be combined with `--baseline`; candidate comparison runs always enforce strict JSON-only output on every measured package.

Baseline comparison requires the same corpus mode, per-case authoritative and whole-package hashes, exact byte lengths, machine fingerprint, warmup/iteration counts, splash-certification mode, and a minimum speedup for both aggregate median load and first-save time. A validated report marked as allowing legacy baseline output is accepted as the baseline, while the candidate remains strict. The default `2.0` gate means the candidate must complete each median in no more than half of the baseline time. Certification corpora apply the splash gate to every tagged sanitized slow package; development corpora retain the all-case diagnostic gate. Each selected open must paint within 250 ms, keep its maximum heartbeat gap within 500 ms, and stop the dedicated dispatcher cleanly. `--skip-splash-responsiveness-gate` makes those checks diagnostic only for constrained CI/headless environments. This benchmark escape hatch does not change the application's best-effort splash degradation behavior. The steady-state save measurement reports preview-cache reuse but is diagnostic rather than part of the speed gate.

For final certification, capture separate baseline and candidate Windows Performance Recorder traces around the sanitized-package benchmark. Enable CPU sampling, file/disk I/O, allocation/.NET activity, and UI/WPF responsiveness providers, then compare the traces in Windows Performance Analyzer alongside the JSON reports. Keep sanitized packages and trace files outside version control.

## Git Sync

Git sync links a JSON-authoritative `.tcom` or `.tdom` package to a normal Git remote and repo-relative package path. ThinkComposer shells out to installed `git.exe`; GitHub, Bitbucket, Azure DevOps, SSH remotes, HTTPS remotes, and local bare repositories are all treated as generic Git remotes. Credentials are not stored by ThinkComposer; use Git Credential Manager, SSH keys, or existing Git configuration.

`git link` writes portable link metadata to root `/manifest.json`:

```cmd
thinkcomposer git link --input model.tcom --remote https://example.com/repo.git --branch main --path diagrams/model.tcom --domain-path domains/base.tdom --output model.tcom --in-place
```

The manifest stores only `remote.url`, `remote.branch`, and `baselines[]` entries. Package-level Composition or Domain links live in `gitSync`. A `.tcom` may also carry `embeddedDomainGitSync`, copied from a linked source `.tdom`, so the embedded Domain can be pulled from its own Git remote independently of the Composition package link. Last-seen commit and package hash state is machine-local under the ThinkComposer user application data folder, not inside the package.

`git pull` fetches the linked branch, validates the linked package as JSON-authoritative, creates a backup for in-place updates, replaces the output package, and preserves the local `gitSync` manifest link. Pull is a whole-package operation; it does not perform a JSON semantic merge. When `--backup-dir` is omitted, in-place pull backups are stored under the ThinkComposer user application data folder in `GitSync\backups`; temporary pull staging files are stored in `GitSync\temp`, not beside the `.tcom` or `.tdom`.

`git push` is supported for `.tcom` compositions only. It validates the package, refuses to push when the remote branch advanced since local sync state, commits the linked `.tcom` path, and pushes. `.tdom` packages are link/pull only in this version; update a Composition from a pulled Domain with `domain update-embedded`. In the desktop UI, Domain `Pull from Git` can also use a `.tcom` package's `embeddedDomainGitSync` link to pull the source `.tdom` and run the embedded-domain update flow.

For a new blank remote repository, link the package and run `git push` first; ThinkComposer creates the linked branch and baseline package path during that first push. `git pull` requires the linked branch and package path to exist already, and reports a clear warning when the remote is still empty.

## Exit Codes

`0` means success. `1` means usage, validation, or expected operation failure. `2` means an unexpected exception escaped the operation wrapper.

## Headless Behavior

The CLI initializes ThinkComposer services and WPF resources without setting `StartupUri`, creating `MainWindow`, or rendering the desktop shell. Report generation still uses WPF document primitives internally to produce XPS/PDF output.
