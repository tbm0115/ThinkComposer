# JSON Persistence Hardening Review

Original parity review: 2026-07-08

JSON loading/performance update: 2026-07-13

This review records the parity hardening work for native `.tcom` and `.tdom` persistence after moving the package source of truth from binary parts to root JSON payloads.

## Result

Modern packages are JSON-authoritative:

- `.tcom`: `/manifest.json`, authoritative `/Composition.json`, authoritative embedded `/Domain.json`, optional `/Interchange/*` sidecars and `/Previews/views/*.png`.
- `.tdom`: `/manifest.json`, authoritative `/Domain.json`, optional authoritative `/TemplateComposition.json`, and optional sidecars/previews.
- `/Interchange/*` and preview PNGs are inspection/context sidecars only. They are never used as the native load source.
- New saves are JSON-only and record `legacyBinaryFallback.present: false`; they never create `/Composition.bin` or `/Domain.bin`.
- Legacy binary-only and transitional packages remain readable. Saving them migrates the package to the JSON-only contract.
- If root JSON fails and the exact matching legacy binary part physically exists, load reports a JSON persistence diagnostic and uses that part only as a recovery path. If it does not exist, open fails with the JSON diagnostic.
- Visual parity for the Shop-Connect deployment example was rechecked after the JSON-authoritative migration. The current JSON-only package restores the visual content missing from the earlier JSON-authority PDF regression: colored grouping regions, free text complements, detail posters, visual positions, shortcut visuals, and routed connector paths.

## Flow Audit

Composition open:

- UI open commands call `CompositionEngine.Materialize(...)` from `CompositionsManager.ProjectCommands`.
- CLI composition commands call the same materialization path through `HeadlessThinkComposerOperations`.
- `CompositionEngine.Materialize(...)` calls `TryLoadCompositionFromJsonPackage(...)`.
- `TryLoadCompositionFromJsonPackage(...)` reads `/Composition.json` and `/Domain.json` with `JsonPackagePersistence.ReadCompositionPackage(...)`, then rehydrates full state with `CompositionJsonImporter.RehydrateFullState(...)`.
- Legacy binary deserialization is reached only when root JSON is absent, or when root JSON fails and `/Composition.bin` exists as fallback.

Composition save:

- UI save commands call `CompositionEngine.Store(...)`.
- CLI save/convert commands call `HeadlessThinkComposerOperations.SaveComposition(...)`.
- Both routes write through `JsonPackagePersistence.StoreComposition(...)`, which emits required root JSON/root manifest and optional sidecars/previews through the safe package-replacement path. No binary fallback is serialized.

Domain open:

- UI domain open commands route through `OpenDomainAndCreateCompositionOfIt(...)` or domain materialization helpers.
- CLI domain commands call `HeadlessThinkComposerOperations.LoadDomain(...)`.
- Both routes call `CompositionEngine.MaterializeDomain(...)`, which calls `TryLoadDomainFromJsonPackage(...)`.
- `TryLoadDomainFromJsonPackage(...)` reads root `/Domain.json` with `JsonPackagePersistence.ReadDomainPackage(...)` and rehydrates via Domain JSON import.
- Legacy binary deserialization is reached only when root JSON is absent, or when root JSON fails and `/Domain.bin` exists as fallback.

Domain save:

- UI domain save calls `DomainsManager.SaveDomainAs`, which writes through `JsonPackagePersistence.StoreDomain(...)`.
- CLI domain save/convert calls `HeadlessThinkComposerOperations.SaveDomain(...)`, which also writes through `JsonPackagePersistence.StoreDomain(...)`.
- Both routes require `/Domain.json` and `/manifest.json` (plus `/TemplateComposition.json` when requested), omit `/Domain.bin`, and keep optional sidecar failures non-fatal.

Manual JSON export/import commands remain separate interchange workflows. They share DTOs/exporters/importers with persistence, but the manual import commands still preview and merge into an open document instead of replacing normal package load.

## Hardening Added

CLI validators now assert the failure and authority cases that were easy to regress:

- JSON-only composition package opens after `/Composition.bin` is removed.
- JSON-only domain package opens after `/Domain.bin` is removed.
- Packages open after `/Interchange/*` and `/Previews/*` are removed.
- Packages open when `/manifest.json` is missing.
- Packages open when `/manifest.json` is corrupt, and `package inspect` reports `manifestWarning`.
- Root `/Composition.json` and root `/Domain.json` win over stale binary fallback and stale `/Interchange` sidecars.
- Corrupt root JSON with a binary fallback recovers through the legacy fallback diagnostic.
- Corrupt root JSON without a binary fallback fails cleanly with the JSON diagnostic.
- Reopen/resave remains deterministic by comparing canonical root JSON payloads.
- Composition JSON persistence now serializes/imports reconstructable view visual state: view-owned complements, symbol-attached complements, connector route points, connector endpoint visual-representation ids for shortcut-specific links, `VisualRepresentation.customFormatValues`, text formats, WPF brush payloads, idea/relationship pictogram `ImageSource` payloads, normalized z-order, and symbol state such as details poster visibility/height, multiple display, flips, and tilt.
- Domain JSON persistence now serializes/imports the native state needed for report and visual parity: model revision, report configuration, concept visual symbol formats, relationship connector formats, text formats, and brush/dash values.

`package inspect` was also hardened so root JSON presence marks a package JSON-authoritative even when `/manifest.json` is missing or unreadable.

## Loading Responsiveness and Performance Hardening

- Interactive Composition and Domain opens now create a non-cancellable loading splash on an independent STA thread/dispatcher. The model is still rebuilt synchronously on the main WPF thread, while the splash continues painting elapsed time, indeterminate parse activity, and determinate importer counts.
- One operation context reports nine stable stages: package open, Composition parse, Domain/template parse, Domain rebuild, concepts, relationships, views/visuals, final repair, and workspace activation. Nested opens share the outer splash, progress is throttled, and cleanup runs through `finally` on success or failure.
- Native rehydration uses summary diagnostics instead of per-field log traffic. The console roll batches dispatcher work and remains bounded without replacing the whole observable collection for each new line.
- Composition and Domain importers use per-operation indexes instead of repeated full-model scans. Native blank-target loading skips merge-preview work and duplicate repair/collection passes.
- Save exports each authoritative DTO/UTF-8 payload/hash once and reuses it for root and sidecar parts. Required root-write failures preserve the original package; optional sidecar failures remain warnings. The Composition and Domain persistence validators inject both failures, compare the required-failure target byte-for-byte, and reopen the optional-failure package's required payload.
- Container snapshot manifest v2 records preview `inputSha256`, `renderProfile`, and `disposition`. An unchanged preview is reused only after manifest metadata, byte count, and PNG SHA-256 verification; otherwise that view is rerendered.
- The developer performance harness prepares a whole-package-hash/byte-locked corpus and runs fresh-process load, first-save, and steady-save samples. Certification mode requires and tags a sanitized slow package; every measured output is inspected. Baseline comparison requires equivalent corpus mode, per-case hashes/sizes, machine, and run counts, with a default 2x median gate for both load and first save.

## Visual Regression Evidence

The visual regression was confirmed by rendering the two supplied PDFs:

- Binary/before PDF: `W:\True Analytics Solutions\Projects\_True Analytics Manufacturing Solutions\TAMS Edge-Connect\Shop-Connect Deployment Options.pdf`.
- Earlier JSON-authority PDF: `C:\Users\LightWorks\Downloads\Shop-Connect Deployment Options_JSON Authority.pdf`.

The earlier JSON-authority PDF was missing substantial visual content: grouping region fills, several nested region boundaries, the `Customer-Approved Exposure to IT` free text complement, visible detail posters, and explicit connector routes.

After the parity fix, the current JSON-only package/report artifacts in `artifacts\json-visual-check` show the restored visual state:

- `Shop-Connect-json-reportdisplay-jsononly.tcom`: root JSON-authoritative package with `/Composition.bin` removed.
- `jsononly-reportdisplay.pdf`: report generated from the JSON-only package.
- `jsononly-reportdisplay-2.png`: rendered report page showing restored colors, positions, complements, detail posters, shortcuts, and connector paths.

These artifacts are local validation outputs and are intentionally not committed.

## Manual WPF Smoke Checklist

Codex did not have a reliable WPF desktop UI automation surface for Open/Save dialog interaction in this environment. The CLI exercises the same materialization and store paths, and the report render validates visual output, but the following manual WPF checklist remains the release gate before claiming release readiness:

- [ ] Open legacy `docs\Examples\Shop-Connect_Deployment_Options.tcom` in the desktop app.
- [ ] Save As a new `.tcom` package.
- [ ] Reopen the new `.tcom` in the desktop app.
- [ ] Confirm model metadata, embedded domain, active/root view, visual bounds, colored grouping regions, relationships, shortcuts, free complements, detail posters, and connector paths are intact.
- [ ] Run `thinkcomposer composition export-json --input <saved-file.tcom> --output <composition.json>` and confirm export still works as interchange.
- [ ] Run `thinkcomposer composition import-json --input <saved-file.tcom> --json <composition.json> --output <imported-file.tcom> --preview-only`, then without `--preview-only` if needed, and confirm interchange remains separate from the native package load path. Desktop Composition JSON buttons are deprecated.
- [ ] Save the migrated `.tcom` again and confirm `ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input <saved-file.tcom>` reports `jsonAuthoritative: true`.
- [ ] Confirm the migrated `.tcom` contains no `/Composition.bin` and reports `transitionalWithBinaryFallback: false`.
- [ ] Open legacy `PredefinedContent\All-Purpose.tdom` in the desktop app.
- [ ] Save As a new `.tdom` package.
- [ ] Reopen the new `.tdom` in the desktop app.
- [ ] Inspect root `/Domain.json` and confirm it carries the authoritative domain payload.
- [ ] Run `thinkcomposer domain export-json --input <saved-file.tdom> --output <domain.json>` and, when preview diagnostics are needed, `thinkcomposer domain import-json --input <saved-file.tdom> --json <domain.json> --output <imported-file.tdom> --preview-only` to confirm CLI compatibility remains separate from the native package load path. Desktop Domain JSON buttons are deprecated.
- [ ] Save the migrated `.tdom` again and confirm `ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input <saved-file.tdom>` reports `jsonAuthoritative: true`.
- [ ] Confirm the migrated `.tdom` contains no `/Domain.bin` and reports `transitionalWithBinaryFallback: false`.
- [ ] Reopen both saved packages after deleting `/Interchange/*` and `/Previews/*` from copies of the packages to confirm sidecars are not authoritative.
- [ ] Reopen JSON-only copies after deleting `/Composition.bin` or `/Domain.bin` to confirm binary fallback is not required when root JSON is valid.
- [ ] Open a large `.tcom` and `.tdom`; confirm the splash paints promptly, elapsed time keeps moving during JSON parsing, item counts advance during reconstruction, and the splash closes exactly once.
- [ ] Repeat unchanged and one-view-changed saves; confirm the sidecar v2 manifest reports reused previews for the first case and rerenders only affected previews for the second.
- [ ] Prepare a `--mode certification` JSON persistence corpus with a sanitized slow package and benchmark it; compare against a same-machine, hash/size-matched baseline with the default 2x load/first-save median gate.

## Validation Commands

Build:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe' ThinkComposer.Cli\ThinkComposer.Cli.csproj /p:Configuration=Debug /v:minimal /m
```

Result: passed with existing warnings only.

Final release-review build checks also passed:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe' ThinkComposer.Cli\ThinkComposer.Cli.csproj /t:Build /p:Configuration=Debug /v:minimal
& 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe' Instrumind_ThinkComposer.sln /t:Build /p:Configuration=Debug /v:minimal
```

Result: both passed with the existing `MSB3270` x86/MSIL warning only.

Composition validators:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition validate-json-persistence --input docs\Examples\Shop-Connect_Deployment_Options.tcom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-shop-final
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition validate-json-persistence --input docs\Examples\IMTS_2022_Network_Diagram.tcom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-imts-final
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition validate-json-persistence --input docs\Examples\Boeing_Tool_ID_Proposed_Architecture.tcom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-boeing-final
```

Result: all passed. Logs include `Composition JSON persistence validation passed.` and the hardening notes for binary removal, sidecar removal, missing/corrupt manifests, stale binary/sidecar root JSON authority, fallback recovery, and no-fallback failure.

Final Shop-Connect parity run:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition validate-json-persistence --input docs\Examples\Shop-Connect_Deployment_Options.tcom --output-dir artifacts\json-validation-final\composition-shopconnect-final
```

Result: passed. The run covered binary removal, sidecar/previews removal, missing/corrupt manifest handling, stale binary disagreement, stale sidecar disagreement, corrupt JSON with fallback, and corrupt JSON without fallback.

Domain validator:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe domain validate-json-persistence --input PredefinedContent\All-Purpose.tdom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-all-purpose-final
```

Result: passed. Log includes `Domain JSON persistence validation passed.` and the equivalent hardening notes.

Final All-Purpose domain run:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe domain validate-json-persistence --input PredefinedContent\All-Purpose.tdom --output-dir artifacts\json-validation-final\domain-allpurpose-rerun
```

Result: passed. The run covered binary removal, sidecar/previews removal, missing/corrupt manifest handling, stale binary/sidecar authority, corrupt JSON with fallback, and corrupt JSON without fallback.

Convert commands:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition convert-json-persistence --input docs\Examples\Shop-Connect_Deployment_Options.tcom --output C:\tmp\thinkcomposer-json-persistence\Shop-Connect.hardening-converted.tcom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe domain convert-json-persistence --input PredefinedContent\All-Purpose.tdom --output C:\tmp\thinkcomposer-json-persistence\All-Purpose.hardening-converted.tdom
```

Result: both passed.

Final convert checks:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition convert-json-persistence --input docs\Examples\Shop-Connect_Deployment_Options.tcom --output artifacts\json-validation-final\Shop-Connect-converted-final.tcom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe domain convert-json-persistence --input PredefinedContent\All-Purpose.tdom --output artifacts\json-validation-final\All-Purpose-converted-final.tdom
```

Historical 2026-07-08 result: both passed and produced JSON-authoritative transitional packages with optional binary fallback. Current saves are expected to produce JSON-only packages and must be revalidated under the checklist above.

Package inspect checks:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input C:\tmp\thinkcomposer-json-persistence\Shop-Connect.hardening-converted.tcom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input C:\tmp\thinkcomposer-json-persistence\All-Purpose.hardening-converted.tdom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input C:\tmp\thinkcomposer-json-persistence\hardening-imts-final\IMTS_2022_Network_Diagram-json-persistence-1-without-composition-bin.tcom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input C:\tmp\thinkcomposer-json-persistence\hardening-all-purpose-final\All-Purpose-json-persistence-1-without-domain-bin.tdom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input C:\tmp\thinkcomposer-json-persistence\hardening-imts-final\IMTS_2022_Network_Diagram-json-persistence-1-root-json-authority.tcom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input C:\tmp\thinkcomposer-json-persistence\hardening-all-purpose-final\All-Purpose-json-persistence-1-root-json-authority.tdom
```

Result: converted and mutated packages reported `jsonAuthoritative: true`. Binary-removed variants reported `transitionalWithBinaryFallback: false`. Stale-authority variants reported root JSON present with binary fallback present, and validator logs prove root JSON won.

Final package inspection included:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe package inspect --input artifacts\json-visual-check\Shop-Connect-json-reportdisplay-jsononly.tcom
```

Result: reported `persistenceFormat: json`, `jsonAuthoritative: true`, `transitionalWithBinaryFallback: false`, root `/Composition.json: true`, root `/Domain.json: true`, and `/Composition.bin: false`.

Manifest schema contract:

- Validated required manifest fields, constants/enums, authoritative paths, SHA-256 hashes, byte counts, and UTC timestamp parsing for converted packages and JSON-only variants.
- Result: all checked manifests passed. No external `jsonschema` or `ajv` package was available locally, so this was a focused contract validation rather than a third-party full JSON Schema run.

## Unsupported Native State

Root JSON persists the documented Composition JSON and Domain JSON fields. Native-only state that is not represented by a documented JSON field is intentionally not reconstructed from JSON persistence.

Warning and documentation coverage now includes:

- Composition exporter: generic warning for custom visual formatting, store-box references, and native/binary-only content, plus specific warnings for attachments, text-only details, malformed table cells, metadata-only links, image complements, and non-visual view children. Documented visual formats and idea/relationship pictograms are serialized.
- Domain exporter: generic warning for unsupported custom domain shape resources, rich native object graphs, and domain-level binary pictogram/image resources, plus grouped missing category warnings and output template text-only warnings. Supported visual text formats and WPF brush payloads are serialized.
- Docs: `docs/json-interchange.md`, `docs/domain-json-interchange.md`, `docs/container-readable-snapshots.md`, `docs/cli.md`, user manual CLI sections, schema docs, and plugin skill references describe root JSON authority, legacy fallback, sidecar non-authority, and unsupported binary/native payload limitations.

## Remaining Limitations

- Existing transitional packages may still contain legacy binary fallbacks until resaved. Current saves omit them.
- Unsupported native-only/binary payloads are surfaced as warnings and documentation limitations; they are not reconstructed from JSON.
- External JSON Schema tooling was not installed in the local environment. The manifest contract was validated by a focused local checker.
- Manual PDF regeneration depends on the maintainer's local Pandoc/TeX setup and is tracked separately in the final verification notes.

## Final Release-Review Notes

- `git diff --check HEAD~1 HEAD` passed.
- Canonical schemas and plugin reference schema copies parsed with `ConvertFrom-Json`.
- `docs\thinkcomposer-plugin\skills\thinkcomposer-json-interchange\references\` was synced from the canonical docs/schemas.
- `docs\thinkcomposer-json-interchange.zip` and `docs\thinkcomposer-plugin.zip` were regenerated with `docs\thinkcomposer-plugin\scripts\package_thinkcomposer_plugin.py` using the bundled Codex Python runtime because `python` was not on PATH.
- ZIP contents were checked for relative paths only. The full plugin ZIP includes `.codex-plugin/plugin.json`, `.mcp.json`, `skills/`, and `scripts/`.
- Installer packaging still references `docs\user-manual\output\ThinkComposer_User_Manual.pdf`.
- The manual PDF rebuild was attempted with `powershell -ExecutionPolicy Bypass -File docs\user-manual\build.ps1` and was blocked because `xelatex` is not installed or not on PATH. The generated PDF was not hand-edited.
