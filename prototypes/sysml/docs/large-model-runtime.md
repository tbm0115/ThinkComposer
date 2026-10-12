# MTConnect drill-down and large-model persistence repair

The previous repair restored only the five overview symbols. The package contained no authored subviews. ThinkComposer's composition indicator also reflects model containment, so it could open a new, empty View even though children existed in the Content tree. The reported save error was a separate, reproduced `OutOfMemoryException` in the 32-bit persistence path.

## What changed

The imported package now has eleven Views: its root overview plus ten selective drill-downs. The four Block Views expose direct semantic members: Observation (15), Component (17), DataItem (27) and Device (21). MTConnect_Reference leads to the MTConnect model; the remaining Views follow the ownership paths through Observation Information Model, Device Information Model, Components and DataItems. All together they contain 94 representations of existing elements. No model element was duplicated, renamed, moved or removed.

These are selective model browsers, not reconstructed Cameo diagrams. Annotations, external references and other imported content remain in the Content tree and Details. A containment indicator on a deeper, model-only element still does not promise an authored diagram. The supplied Views are intended entry points, not a visualization of all 32,326 concepts and 1,020 relationships.

`scripts/repair_mtconnect_browser.py` applies only View additions, checks for concurrent file changes, and preserves a content-addressed backup. `reports/mtconnect-browser-repair.json` records the backup and hashes. Future `prototype.py import-xmi` runs create these Views too.

The generic application repairs are:

- Legacy model migrations use sets for visited-object checks and indexed duplicate-ID lookups instead of repeatedly scanning whole lists. No migration revision is skipped.
- Composition JSON writes one entity at a time. Native saves spool UTF-8 to a temporary file, reused for authoritative and interchange parts; the file is closed and deleted after success or failure.
- Composition JSON reads one root collection item at a time, retaining the existing per-entity conversion rules. This avoids retaining a complete UTF-16 document and intermediate dictionary graph when reopening a large save.
- Preview input hashes consume the same canonical JSON incrementally. Previews remain enabled; this repair does not hide the failure by disabling them.

Canonical JSON output is unchanged. The original compact prototype payload is approximately 52 MB uncompressed; a native save is approximately 393 MB because native export includes field definitions and other explicit state. Streaming handles that expansion without another contiguous whole-document allocation. Reducing this repeated data is possible future work, not part of this repair.

## Using the repair

Close the old document and reopen `reference/mtconnect/MTConnect_Imported.tcom` using `artifacts/sysml-large-model-build/Instrumind.ThinkComposer.exe`, or a fresh normal build. Save unsaved user edits to a separate copy before closing. Do not overwrite the repaired package with the old in-memory document. Existing running executables do not receive source fixes automatically.

The build output is isolated to avoid the previously reported locked-DLL errors. No installed application or running process was replaced or terminated.

## Verification

The x86 native test completed the full model's load, save, fresh-process reopen and second save. On this machine, the repaired input loaded in 12.42 seconds and completed load plus save in 33.53 seconds. The saved package reopened in 15.51 seconds and completed reopen plus save in 36.44 seconds. These are isolated test measurements, not a desktop responsiveness guarantee. The user's prior desktop log recorded 393.06 seconds to load, including 385.73 seconds in finalization.

Both saves generated all eleven previews with zero preview warnings, empty previews or skips, and no memory exceptions. All model IDs, ownership, relationship endpoints, ordered Detail values, View identities, View owners and symbol geometry survived. Entity snapshots from the two native saves are identical. Manifest part hashes were independently recalculated. See `reports/full-model-save-preservation.json` and `reports/large-model-runtime-validation.json`.

The 66 small-model native checks pass. Twelve canonical serializer outputs match the previous build byte-for-byte, including Unicode and escaping; the text and streaming readers agree. Nine malformed inputs are rejected. The legacy duplicate-ID test verifies repair, preservation of unique/nested identities, and repeat-run stability. The actual desktop View control renders the Component subview from a source-derived fixture; `reports/mtconnect-component-drilldown.png` is that fixture's startup viewport, not a manual full-model screenshot.

To reproduce the full native save test from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File prototypes/sysml/scripts/test_large_model.ps1
powershell -ExecutionPolicy Bypass -File prototypes/sysml/scripts/test_large_model.ps1 -InputPackage artifacts/sysml-large-model-build/MTConnect_Saved.tcom -OutputPackage artifacts/sysml-large-model-build/MTConnect_Resaved.tcom
python prototypes/sysml/scripts/test_serializer.py
python prototypes/sysml/scripts/check_full_model_save.py
```

The test writes only to the specified separate output files. Full manual WPF interaction, source language conformance and external parser validation remain separate acceptance items. Earlier timeout and five-symbol reports are historical evidence, superseded for persistence and drill-down by these results.

## Subsequent desktop observation

The user supplied a successful desktop open/save log on October 9, 2026 (open operation `ab030d61-9ddf-432e-9277-d722f35d061f`). Opening took 16.124 seconds: finalization 8.986 seconds, concept reconstruction 5.177 seconds, and composition JSON parsing 1.542 seconds. The splash painted initially but recorded zero heartbeats and a 16.125-second maximum heartbeat gap; responsiveness thresholds still failed. Faster completion has not resolved UI blocking.

Saving completed successfully with eleven generated previews and no snapshot warnings. The supplied log does not include a save timing summary, so its elapsed save time and dominant stage cannot be established. Zero reused previews on this save alone does not demonstrate a cache defect.

The user's preference is to retain these observations for later work unless an obvious improvement emerges. No further runtime changes were made from this log. Follow-up should profile remaining finalization and save stages, check preview reuse on subsequent unchanged saves, and address responsiveness without compromising WPF thread affinity or persistence correctness.

## Generate Files selection dialog

A subsequent October 9 report identified a separate hang when opening **Generate Files**, before generation began. The attached debugger log contained 11,498 errors for a tooltip binding to `IdeaSelection.Summary`, which does not exist. The selector also forced every tree node to expand, creating controls throughout the large model.

The selector now binds to `SourceIdea.Summary`, opens only the composition root, and virtualizes/recycles rows. Expansion state belongs to each selection item so scrolling does not transfer it to another item. All selection data remains available for collapsed branches: single-element, subtree, select-all and unselect-all behavior is retained. Repeated Loaded events preserve pending choices. Saved exclusions are indexed once instead of scanned for every model element.

The actual selector control was loaded and laid out in a hidden WPF HWND using the full MTConnect package: 32,327 selection entries, two initially realized rows, and 0.338 seconds for selector creation/loading/layout **after the composition had loaded**. Restoring 16,164 exclusions took 0.013 seconds. The regression also checks bulk/subtree/single selection, repeated loading, expansion, scrolling and absence of selection binding errors. A synthetic 2,000-sibling test checks virtualization across a wide tree. These are automated selector tests, not a manual test of the complete modal dialog or of export execution.

Reproduce with `scripts/test_generation_selection.ps1`; add `-InputPackage prototypes/sysml/examples/SysML_v1_Starter.tcom -Wide` for the wide-tree case. The isolated build is `artifacts/sysml-generation-dialog-build/Instrumind.ThinkComposer.exe`. Neither test saves the input composition or generates output files. Restarting with the rebuilt application is required; an already-running process does not receive the fix.
