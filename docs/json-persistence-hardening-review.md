# JSON Persistence Hardening Review

Date: 2026-07-08

This review records the parity hardening work for native `.tcom` and `.tdom` persistence after moving the package source of truth from binary parts to root JSON payloads.

## Result

Modern packages are JSON-authoritative:

- `.tcom`: `/manifest.json`, authoritative `/Composition.json`, authoritative embedded `/Domain.json`, optional recovery `/Composition.bin`, optional `/Interchange/*` sidecars and `/Previews/views/*.png`.
- `.tdom`: `/manifest.json`, authoritative `/Domain.json`, optional authoritative `/TemplateComposition.json`, optional recovery `/Domain.bin`, optional sidecars/previews.
- `/Interchange/*` and preview PNGs are inspection/context sidecars only. They are never used as the native load source.
- Legacy binary-only packages remain readable. Saving them migrates the package to the JSON-authoritative contract.
- If root JSON fails and a binary fallback exists, load reports a JSON persistence diagnostic and uses the binary only as a recovery path. If root JSON fails without a binary fallback, open fails with the JSON diagnostic.

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
- Both routes write through `JsonPackagePersistence.StoreComposition(...)`, which emits root JSON, root manifest, optional binary fallback, sidecars, and previews.

Domain open:

- UI domain open commands route through `OpenDomainAndCreateCompositionOfIt(...)` or domain materialization helpers.
- CLI domain commands call `HeadlessThinkComposerOperations.LoadDomain(...)`.
- Both routes call `CompositionEngine.MaterializeDomain(...)`, which calls `TryLoadDomainFromJsonPackage(...)`.
- `TryLoadDomainFromJsonPackage(...)` reads root `/Domain.json` with `JsonPackagePersistence.ReadDomainPackage(...)` and rehydrates via Domain JSON import.
- Legacy binary deserialization is reached only when root JSON is absent, or when root JSON fails and `/Domain.bin` exists as fallback.

Domain save:

- UI domain save calls `DomainsManager.SaveDomainAs`, which writes through `JsonPackagePersistence.StoreDomain(...)`.
- CLI domain save/convert calls `HeadlessThinkComposerOperations.SaveDomain(...)`, which also writes through `JsonPackagePersistence.StoreDomain(...)`.

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

`package inspect` was also hardened so root JSON presence marks a package JSON-authoritative even when `/manifest.json` is missing or unreadable.

## Validation Commands

Build:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe' ThinkComposer.Cli\ThinkComposer.Cli.csproj /p:Configuration=Debug /v:minimal /m
```

Result: passed with existing warnings only.

Composition validators:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition validate-json-persistence --input docs\Examples\Shop-Connect_Deployment_Options.tcom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-shop-final
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition validate-json-persistence --input docs\Examples\IMTS_2022_Network_Diagram.tcom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-imts-final
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition validate-json-persistence --input docs\Examples\Boeing_Tool_ID_Proposed_Architecture.tcom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-boeing-final
```

Result: all passed. Logs include `Composition JSON persistence validation passed.` and the hardening notes for binary removal, sidecar removal, missing/corrupt manifests, stale binary/sidecar root JSON authority, fallback recovery, and no-fallback failure.

Domain validator:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe domain validate-json-persistence --input PredefinedContent\All-Purpose.tdom --output-dir C:\tmp\thinkcomposer-json-persistence\hardening-all-purpose-final
```

Result: passed. Log includes `Domain JSON persistence validation passed.` and the equivalent hardening notes.

Convert commands:

```powershell
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe composition convert-json-persistence --input docs\Examples\Shop-Connect_Deployment_Options.tcom --output C:\tmp\thinkcomposer-json-persistence\Shop-Connect.hardening-converted.tcom
ThinkComposer.Cli\bin\Debug\ThinkComposer.Cli.exe domain convert-json-persistence --input PredefinedContent\All-Purpose.tdom --output C:\tmp\thinkcomposer-json-persistence\All-Purpose.hardening-converted.tdom
```

Result: both passed.

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

Manifest schema contract:

- Validated required manifest fields, constants/enums, authoritative paths, SHA-256 hashes, byte counts, and UTC timestamp parsing for converted packages and JSON-only variants.
- Result: all checked manifests passed. No external `jsonschema` or `ajv` package was available locally, so this was a focused contract validation rather than a third-party full JSON Schema run.

## Unsupported Native State

Root JSON persists the documented Composition JSON and Domain JSON fields. Native-only state that is not represented by a documented JSON field is intentionally not reconstructed from JSON persistence.

Warning and documentation coverage now includes:

- Composition exporter: generic warning for custom visual formatting, store-box references, and native/binary-only content, plus specific warnings for attachments, text-only details, malformed table cells, metadata-only links, and non-visual view children.
- Domain exporter: generic warning for visual style details, rich style object graphs, custom domain shape resources, and binary pictogram/image content, plus grouped missing category warnings and output template text-only warnings.
- Docs: `docs/json-interchange.md`, `docs/domain-json-interchange.md`, `docs/container-readable-snapshots.md`, `docs/cli.md`, user manual CLI sections, schema docs, and plugin skill references describe root JSON authority, legacy fallback, sidecar non-authority, and unsupported binary/native payload limitations.

## Remaining Limitations

- Transitional packages still write optional binary fallbacks for recovery and backwards compatibility. These are not authoritative when root JSON is present.
- Unsupported native-only/binary payloads are surfaced as warnings and documentation limitations; they are not reconstructed from JSON.
- External JSON Schema tooling was not installed in the local environment. The manifest contract was validated by a focused local checker.
- Manual PDF regeneration depends on the maintainer's local Pandoc/TeX setup and is tracked separately in the final verification notes.
