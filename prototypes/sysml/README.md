# Experimental SysML Domains

Four self-contained ThinkComposer Domains demonstrate how far the existing Domain system can support SysML authoring. The Domain definitions and exports use existing application capabilities. Subsequent runtime fixes address model-only navigation, slow legacy finalization and large-model persistence; they add no SysML-specific application features. These are prototypes, not conforming SysML tools. The full MTConnect import remains an experimental model with incomplete semantic mappings.

**Desktop fixes:** overview visuals now have explicit visibility controls, preventing hidden same-name elements from suppressing them during native loading. Coordinates have also been recentered, and an approved application guard fixes navigation of model-only elements. Reopen the repaired package to receive the visual changes; use a rebuilt executable for the navigation guard. See the [diagnosis and repair status](docs/desktop-runtime-issue.md).

**Drill-down and saving:** `MTConnect_Imported.tcom` now includes ten populated subviews for its five overview entry points and their ownership paths. The full model passes native load/save/reopen/resave with the updated build in `artifacts/sysml-large-model-build/`. Use that executable, or rebuild the application after closing the old process. The former executable still has the memory limitation. See [large-model repair and verification](docs/large-model-runtime.md).

## Start here

| Domain | Native file | First example | Exports |
| --- | --- | --- | --- |
| SysML v1 | [SysML_v1.tdom](domains/SysML_v1.tdom) | [Survey](examples/SysML_v1_Survey.tcom) | Cameo-style UML/SysML XMI subset |
| MTConnect SysML v1 | [MTConnect_SysML_v1.tdom](domains/MTConnect_SysML_v1.tdom) | [Survey](examples/MTConnect_SysML_v1_Survey.tcom) | XMI plus retained MTConnect stereotype attributes |
| SysML v2 | [SysML_v2.tdom](domains/SysML_v2.tdom) | [Survey](examples/SysML_v2_Survey.tcom) | SysML text; separate KerML subset |
| MTConnect SysML v2 | [MTConnect_SysML_v2.tdom](domains/MTConnect_SysML_v2.tdom) | [Survey](examples/MTConnect_SysML_v2_Survey.tcom) | SysML/KerML subset; MTConnect metadata remains model data |

Open a Survey Composition first. Its embedded Domain contains everything needed; no installed catalog changes are necessary. Open a `.tdom` through the existing Domain workflow to inspect or edit definitions, or choose it when creating a Composition. Each Domain includes a starter template; separate `_Starter.tcom` files are also supplied. Keep originals and use Save As for experiments.

The `_ModelingLab.tcom` examples contain connected requirements, interfaces, behavior and analysis data. Their specialized exports are deliberately blocked: they reproduce gaps instead of emitting plausible but incorrect language constructs. The [coverage matrix](docs/coverage.md) distinguishes data representation from semantics and notation. Every supplied view is labeled a model browser, not a conforming SysML diagram.

The [gap assessment](docs/gaps.md) is the second-pass software backlog. [Mapping and authoring](docs/mapping.md) describes editable fields and export rules. [Validation](docs/validation.md) explains exactly what passed and what remains unverified.

## Author and export

1. Keep one **ModelDocument** concept as the export root. Put the semantic Model/Package inside it, and model elements under their actual owners. A visual shortcut reuses an element; it does not establish ownership.
2. For the initial prototype, copy an appropriate example element and assign a fresh identity for a genuinely new element. Populate its **ModelMetadata** and **Attributes** Details. Definitions in the palette identify metatypes; they do not automatically initialize every export field. A blank export selector omits that element and its subtree.
3. Use explicit Typing and Generalization relationships. Definition inheritance is not SysML generalization. Keep `SourceId` unchanged when renaming imported elements. Native IDs supply stable export IDs for new elements.
4. Use the existing output-generation UI and choose `SysML_v1_XMI`, `SysML_v2_Text`, or `KerML_Text`. Generate the whole Composition; the ModelDocument template emits the document. Individual element templates are intentionally disabled.
5. Run the companion check below before treating the output as usable. The application can report success for empty or erroneous template output. Read omissions and unresolved references in the report. Parser validation is a separate, currently unverified step.

From the repository root in PowerShell, set `$cli` to your existing built CLI and `$python` to a Python 3 interpreter:

```powershell
$cli = '.\artifacts\git-ui-build\ThinkComposer.Cli.exe'
$python = 'python'
& $cli output generate --input prototypes/sysml/examples/SysML_v1_Survey.tcom --language SysML_v1_XMI --output-dir artifacts/sysml-my-export
& $python prototypes/sysml/scripts/validate.py export-report --input prototypes/sysml/examples/SysML_v1_Survey.tcom --language SysML_v1_XMI --output artifacts/sysml-my-export/Model.xml
```

For v2 use `SysML_v2_Text` and `Model.sysml`; for KerML use `KerML_Text` and `Model.kerml`. KerML is generated by its own Domain template, with explicit reductions and omissions. Changing a file extension is not conversion. The helper validates output; it never generates the language document.

## Reproduce the artifacts

```powershell
& $python prototypes/sysml/scripts/prototype.py build
& $python prototypes/sysml/scripts/prototype.py import-xmi --input C:/Users/LightWorks/Downloads/MTConnectSysMLModel.xml --output-dir prototypes/sysml/reference/mtconnect
& $python prototypes/sysml/scripts/validate.py negative
& $python prototypes/sysml/scripts/validate.py native --cli $cli
```

`build` replaces generated prototype Domains, examples and template copies. Save personal edits elsewhere first. It does not change application source, install Domains, fetch libraries or alter Git configuration. Each MTConnect variant is generated from the same core plus explicit extensions in `make_domain`; the `.tdom` files have no live inheritance relationship. Exact language/tool assumptions are in [validation-tools.json](sources/validation-tools.json).

## MTConnect reference model

[MTConnect_Imported.tcom](reference/mtconnect/MTConnect_Imported.tcom) is the full best-effort import. Start with the smaller [v1 slice](reference/mtconnect/MTConnect_SysML_v1_Slice.tcom) and [v2 slice](reference/mtconnect/MTConnect_SysML_v2_Slice.tcom) for practical exploration. The slices deliberately select particular source classes named Device, Component, DataItem and Observation; they are not automatic migration results.

The [import report](reference/mtconnect/import-report.json) accounts for every XML element. Compressed ledgers identify every source path/ID, mapping status and recognized semantic reference. The unchanged source copy lives in `reference/mtconnect/original/` and is excluded from Git; retain it with your local reference artifacts. The report records its SHA-256. Original proprietary diagrams and opaque extensions are recoverable from that source, not editable SysML content in the prototype.

Do not infer that the large model is production-ready from the small examples. The initial full native round-trip was interrupted after extended processing; large-model desktop save/reopen remains a recorded scale gap. No source Domain changes are silently applied to embedded copies.
