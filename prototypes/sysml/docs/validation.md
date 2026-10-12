# Validation and reproducibility

## Declared checks

`scripts/validate.py` uses the installed ThinkComposer CLI for native persistence and output generation. It does not implement an alternative exporter or a full language parser. `reports/native-regressions.json` records the CLI executable SHA-256, exact commands, timings and results. Its native persistence operations save, reopen and resave through the application implementation, then compare canonical payloads. Additional checks compare original identities, ownership, relationship endpoints, Detail values and ordered rows against the saved JSON so an initial lossy load cannot be hidden by a stable second save.

The runner covers four Domains, twelve reusable Compositions, nine Survey/slice language exports, repeat export identity, rename/typing behavior, and rendered previews. Domain tests also exercise their embedded starter templates. Curated source slices are generated as native packages and exercised by output generation; their full persistence tests can be run independently with the same CLI command.

Every checked export has `coverage-validation.json` alongside it. The report includes emitted IDs/counts, folded relationships, omitted elements, structural diagnostics, external references, output SHA-256 and repeat comparison. `completeExport` is always false for this prototype: even a slice with no omitted elements omits wider source/language semantics.

The local model checker detects duplicate identities, missing owners/endpoints, containment cycles, unknown definitions, a deliberately unsupported metaclass, invalid multiplicity tokens and malformed expressions in its explicitly restricted integer-comparison grammar. It is not a general expression parser and never evaluates constraints. XMI checks add XML parsing, duplicate IDs, recognized local reference targets, selected profile base attributes and comparison with expected emitted content. Full XMI/profile metamodel validation is unverified.

Text checks compare identity coverage, delimiters and runtime-error markers. Syntax, standard-library resolution and language semantic constraints require the separately pinned reference implementation. **No `.sysml` or `.kerml` parser-conformance pass is claimed.** No Java/Maven/Gradle/SysML parser executable was available in this environment; nothing was downloaded or installed to conceal that gap. Tool/library versions must be recorded with a future parser run, including any differences from the normative specification editions.

## Negative and blocked fixtures

`fixtures/negative/` includes native packages for duplicate IDs, broken references, invalid ownership, ownership cycles, unsupported metaclasses and malformed expressions, plus empty XMI. `reports/negative-tests.json` records expected failures detected by the helper. Do not treat these files as valid authoring examples.

`fixtures/blocked/` preserves three native template reproducers: chained Detail indexing, a regex-escaping error and traversal exceeding 12 levels. The native generation command may exit successfully while the companion checks correctly reject its output. Their reports record those expected problems. A CLI exit status of zero is therefore necessary but not sufficient for a usable export.

The headless runner uses a timeout per small native operation and does not close a user desktop application. Logs and intermediate round-trip files live under `artifacts/sysml-validation/`, separate from reusable deliverables.

## Reference import audit

```powershell
& $python prototypes/sysml/scripts/audit_reference.py
& $python prototypes/sysml/scripts/validate.py check --input prototypes/sysml/reference/mtconnect/MTConnect_Imported.tcom
```

The accounting audit verifies source SHA-256, XML element count, every ledger ID/status, every mapped target and the retained source names/text/attributes. Generalization targets are checked through native edges. It validates the authored package structure; it does not substitute for a completed native large-model save/reopen.

The supplied source contains **179,605 XML elements**. The import report records **15,044 mapped**, **18,001 partially mapped**, **158 unresolved**, **146 preserved-only**, and **146,256 unsupported** elements. These are XML-element counts, not counts of semantic SysML elements. The package contains **32,326 concepts and 1,020 native Generalization relationships**. All 144 source diagram nodes are outside editable diagram support; the original XML retains them. Recognized mapped semantic references include 22,233 resolved, 158 external-unresolved and 174 unresolved entries. The reference set is declared in the importer; proprietary diagram references are not certified.

Source SHA-256: `d00efd04932bfd7809a8f7c663b5591796c22be4325559f90c7a8c6afe95c6cd`. Exporter metadata: MagicDraw UML, `2026x v2`. The original source copy is ignored by Git and should be retained locally with the report. Rerun import with `--input` if moving the prototype to another checkout.

The first full native scale run loaded a 33,348-concept version without reported rehydration errors, then did not finish the overall persistence test during extended processing. It was interrupted. A final bounded run on the delivered package also reached native rehydration of all 32,326 concepts and 1,020 relationships with zero reported import errors/skips, but the overall persistence test exceeded 60 seconds. [The scale report](../reports/full-model-scale.json) records its input hash, command, timeout and log tail; the input remained unchanged. No successful full-model native save/reopen or memory ceiling is asserted. This is an explicit outstanding acceptance limitation, not a silent pass.

To repeat with a chosen time budget, run `scripts/scale_check.py --cli <existing-cli> --timeout 180`. The script only terminates its own headless child if the budget expires. It never closes an existing desktop application.

## Visual review and unverified experiments

The two generated Survey previews were inspected for readable, non-overlapping model-browser content. They show generic rectangles and native shortcut/composite controls. Their view names explicitly reject a conforming SysML-diagram claim. No standards-compliant BDD, IBD, state, activity or sequence view is certified. Manual interaction with the WPF editor and full-model navigation remains unverified.

Subsequent desktop use exposed a coordinate/startup mismatch and an application exception on model-only navigation. [The incident report](desktop-runtime-issue.md) supersedes any inference that fitted image export establishes desktop usability. Coordinates were repaired and checked separately. The subsequently approved source guard is applied and built, with an actual-handler regression reproducing the old failure and passing on the fixed build. The scale report above predates the coordinate repair.

Cameo import is untested and not a prerequisite for a prototype. Proprietary diagram roundtrip, complete profile/library resolution, full MTConnect migration and conformance to later SysML v1 editions are outside the demonstrated subset.

The later empty-workspace report exposed a visual-control collision between distinct elements sharing a TechName. All intended prototype overview visuals now carry explicit per-view visibility controls. The native persistence comparison also checks for lost visuals. A two-element regression reproduces zero native symbols before the repair and one afterward; the corrected fixture renders in the actual desktop View control at its initial scroll position. See the incident report for the test and its limitations. The full-model source-accounting audit still passes after the package-only visibility repair.

The subsequent save-error investigation adds generic streaming persistence and legacy-finalization fixes, plus populated selective subviews. Full-model load/save/reopen/resave now passes on the updated build; [large-model runtime verification](large-model-runtime.md) supersedes the earlier timeout and unverified full-save status. The source language conformance gaps remain. No user-manual files were changed, so the generated user-manual PDF is unaffected.
